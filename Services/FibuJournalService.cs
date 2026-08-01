using System.Text.Json;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Fibu-Journal-Generator (Walter-Vorgabe 22.05.2026, Etappe 2). Erzeugt aus den
/// bestätigten Lohn-Snapshots einer Periode das Buchungsjournal (Soll/Gegenkonto/
/// Betrag/Text) — wie der heutige Mirus-Fibujournal-Export.
///
/// Verlinkung NUR über stabile Codes → Kontoplan (lohn_konto_mapping), kein
/// Text-Matching:
///   • Bruttolohn  → Kontoplan Position 10  × Kostenstelle (Modell)
///   • SV/QST-Abzug→ Snapshot-SlipJson abzugLines[].categoryCode → SV.fibu_position
///                   (AHV→500, ALV→510, KTG→530, NBU→540, BVG→550, BVG-Zusatz→590,
///                    QST→560) → Kontoplan AN-Zeile (Soll 1920)
///   • SV-AG-Beitrag→ abzugLines[].agBetrag (von der ENGINE pro Zeile mit der
///                    korrekten alters-/modellgestaffelten Stufe gerechnet, z.B. BVG)
///                    → Kontoplan AG-Zeile (Soll 4060/4061/4062 / Gegen 2070/2080/2090).
///                    Berührt 1920 NICHT. agBetrag erscheint erst in NEU gerechneten
///                    Snapshots → für Alt-Perioden „♻️ Snapshots neu berechnen".
///   • FAK         → SV-Satz Code "FAK" (nur AG, Rate 0 + rate_employer), läuft auf
///                    der AHV-Basis → Position 501 (Soll 4062 / Gegen 2070). Wird beim
///                    Verarbeiten der AHV-Zeile mitgebucht (FAK hat keine AN-Zeile).
///   • LGAV/Lohnpos→ abzugLines[].code (z.B. "600.24") → Kontoplan Position+SubPos
///   • Nettolohn   → Kontoplan Position 1060
///   • RST 13. ML  → PayrollSaldo.ThirteenthMonthMonthly → Kontoplan Position 2010 × KSt
///   • RST Ferien/Feiertage → monatlicher Netto-Zuwachs (Accrual − Bezug) aus dem
///                   SlipJson; FIX/FIX-M Tage × Tagessatz (Monatslohn×12/364, 7-Tage-
///                   Basis), UTP/MTP Ferien als CHF (Ferien-Geld). Kontoplan 4070.20
///                   (Ferien) / 4070.30 (Feiertag): Aufwand 4000/4001/4055 ↔ RST 2019.
///
/// Das Journal balanciert, WEIL snapshot.Netto = totalLohn − Abzüge (Identität
/// Brutto = Netto + Abzüge). Familienzulagen/Spesen liegen in zulagenExtraLines
/// und sind im snapshot.Netto NICHT enthalten (nettolohn ist VOR den Extras) →
/// sie werden hier NICHT gebucht. Mirus-konform (auszahlungsbetrag inkl. FamZ als
/// Netto + FamZ → 2071/1920) ist v3-Arbeit. Die RST-Buchungen berühren 1920 NICHT
/// (Aufwand ↔ RST), können das Durchlaufkonto also nicht aus der Balance bringen.
///
/// v2.1 NOCH OHNE: Familienzulagen/Spesen (s.o.) und Arbeitgeber-Sozialbeiträge
/// (rechnet die Engine nicht). Beide berühren Konto 1920 NICHT — Hinweis im Journal.
/// </summary>
public class FibuJournalService
{
    private readonly AppDbContext _db;
    public FibuJournalService(AppDbContext db) => _db = db;

    private const string Dark  = "#000000";
    private const string Muted = "#404040";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    private static readonly System.Globalization.CultureInfo CH =
        System.Globalization.CultureInfo.GetCultureInfo("de-CH");
    private static string Chf(decimal v) => v.ToString("N2", CH);
    private static readonly string[] MonatsNamen =
        { "Januar","Februar","März","April","Mai","Juni","Juli","August","September","Oktober","November","Dezember" };

    // Modell → Kostenstelle (Walter-Vorgabe 22.05.2026): FIX ist IMMER Crew.
    private static string KstFor(string? model) => (model ?? "").ToUpperInvariant() switch
    {
        "FLEX"   => "200",
        "MTP"   => "100",
        "FIX"   => "100",
        "FIX-M" => "300",
        _       => "100"
    };

    public record JournalLine(string Soll, string Gegen, string Bezeichnung, decimal Betrag);
    public record JournalResult(
        PayrollPeriode Periode,
        List<JournalLine> Lines,
        decimal SollTotal,
        decimal HabenTotal,
        decimal Konto1920Saldo,
        int AnzahlMitarbeiter,
        List<string> Hinweise);

    public async Task<JournalResult> GenerateAsync(int companyProfileId, int year, int month)
    {
        var periode = await _db.PayrollPerioden.Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId && p.Year == year && p.Month == month);
        if (periode is null)
            throw new InvalidOperationException($"Periode {year}-{month:D2} für Filiale {companyProfileId} nicht gefunden.");
        if (periode.Company is null)
            throw new InvalidOperationException("Filiale-Stammdaten fehlen.");

        // Bestätigte Snapshots (STORNIERTE raus).
        var snapshots = await _db.PayrollSnapshots
            .Where(s => s.PayrollPeriodeId == periode.Id
                     && s.CompanyProfileId == companyProfileId
                     && s.Status != "STORNIERT")
            .ToListAsync();
        if (snapshots.Count == 0)
            throw new InvalidOperationException("Keine bestätigten Lohnabrechnungen in dieser Periode.");

        var empIds = snapshots.Select(s => s.EmployeeId).Distinct().ToList();

        // Modell pro MA (aktiver Vertrag in der Periode).
        var periodStartDt = new DateTime(year, month, 1);
        var periodEndDt   = new DateOnly(year, month, DateTime.DaysInMonth(year, month)).ToDateTime(TimeOnly.MinValue);
        var employments = await _db.Employments
            .Where(e => empIds.Contains(e.EmployeeId)
                     && e.ContractStartDate <= periodEndDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= periodStartDt))
            .OrderByDescending(e => e.ContractStartDate)
            .ToListAsync();
        var modelByEmp = employments.GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First().EmploymentModel ?? "");
        var empByEmp = employments.GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        // Salden (für RST 13. ML).
        var saldi = await _db.PayrollSaldos
            .Where(s => s.CompanyProfileId == companyProfileId && s.PeriodYear == year && s.PeriodMonth == month)
            .ToListAsync();
        var saldoByEmp = saldi.GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        // Kontoplan + SV-Fibu-Positionen laden.
        var maps = await _db.LohnKontoMappings.Where(m => m.IsActive).ToListAsync();
        var svFibu = await _db.SocialInsuranceRates
            .Where(r => r.IsActive && r.FibuPosition != null)
            .Select(r => new { r.Code, r.FibuPosition, r.RateEmployer })
            .ToListAsync();
        var fibuByCode = svFibu
            .GroupBy(x => x.Code.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().FibuPosition!.Value);
        fibuByCode["QST"] = 560;   // QST ist kein SV-Satz, fixer Mirus-Code
        // AG-Satz pro Code (Walter 22.05.2026): AG-Beitrag = rate_employer × Basis.
        // NULL = kein AG-Anteil → wird nicht gebucht.
        var agRateByCode = svFibu
            .Where(x => x.RateEmployer != null)
            .GroupBy(x => x.Code.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().RateEmployer!.Value);

        // Kontoplan-Lookups -----------------------------------------------------
        LohnKontoMapping? FindByPosKst(int pos, string? kst) =>
            maps.FirstOrDefault(m => m.Position == pos && m.KostenstelleNr == kst)
            ?? maps.FirstOrDefault(m => m.Position == pos && m.KostenstelleNr == null)
            ?? maps.FirstOrDefault(m => m.Position == pos);
        // SV-AN-Buchung = Zeile mit Soll 1920.
        LohnKontoMapping? FindAn1920(int pos, int? sub) =>
            maps.FirstOrDefault(m => m.Position == pos && m.Fibukonto == "1920" && (sub == null || m.SubPosition == sub))
            ?? maps.FirstOrDefault(m => m.Position == pos && m.Fibukonto == "1920");
        // SV-AG-Buchung = Zeile mit Soll-Aufwand 4060/4061/4062 (Gegen Verbindlichkeit
        // 2080/2090/2070). Berührt 1920 NICHT.
        LohnKontoMapping? FindAg(int pos) =>
            maps.FirstOrDefault(m => m.Position == pos
                && (m.Fibukonto == "4060" || m.Fibukonto == "4061" || m.Fibukonto == "4062"));
        // RST-Bildungs-Zeile (aktueller Monat) für Ferien/Feiertage: Position 4070,
        // SubPos 20=Ferien / 30=Feiertag, is_vormonat=false → Soll Aufwand (4000/
        // 4001/4055) / Gegen RST-Konto (2019). Fibukonto = Aufwand, Gegenkonto = RST.
        LohnKontoMapping? FindRstBuild(int sub, string? kst) =>
            maps.FirstOrDefault(m => m.Position == 4070 && m.SubPosition == sub && m.KostenstelleNr == kst && !m.IsVormonat)
            ?? maps.FirstOrDefault(m => m.Position == 4070 && m.SubPosition == sub && !m.IsVormonat);

        // Aggregation: Schlüssel (Soll|Gegen|Bezeichnung) → Summe.
        var acc = new Dictionary<string, JournalLine>();
        void Add(string? soll, string? gegen, string bez, decimal betrag)
        {
            if (string.IsNullOrEmpty(soll) || string.IsNullOrEmpty(gegen)) return;
            betrag = Math.Round(betrag, 2);
            if (betrag == 0) return;
            var key = $"{soll}|{gegen}|{bez}";
            if (acc.TryGetValue(key, out var ex))
                acc[key] = ex with { Betrag = Math.Round(ex.Betrag + betrag, 2) };
            else
                acc[key] = new JournalLine(soll, gegen, bez, betrag);
        }

        int ohneCodes = 0;

        foreach (var s in snapshots)
        {
            modelByEmp.TryGetValue(s.EmployeeId, out var model);
            var kst = KstFor(model);

            // 1) Bruttolohn → Position 10 × Kostenstelle.
            if (s.Brutto != 0)
            {
                var m = FindByPosKst(10, kst);
                if (m != null) Add(m.Fibukonto, m.Gegenkonto, m.Bezeichnung, s.Brutto);
            }

            // 2) Abzüge aus dem SlipJson (SV/QST per categoryCode, LGAV/Lohnpos per code).
            try
            {
                using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(s.SlipJson) ? "{}" : s.SlipJson);
                if (doc.RootElement.TryGetProperty("abzugLines", out var abz) && abz.ValueKind == JsonValueKind.Array)
                {
                    foreach (var line in abz.EnumerateArray())
                    {
                        decimal betrag = line.TryGetProperty("betrag", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetDecimal() : 0m;
                        if (betrag == 0) continue;
                        decimal abzug = Math.Abs(betrag);

                        string? cat  = line.TryGetProperty("categoryCode", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                        string? code = line.TryGetProperty("code", out var cd) && cd.ValueKind == JsonValueKind.String ? cd.GetString() : null;

                        if (!string.IsNullOrEmpty(cat))
                        {
                            // SV / QST → fibu_position → AN-Zeile (Soll 1920).
                            var catU = cat!.ToUpperInvariant();
                            if (fibuByCode.TryGetValue(catU, out var pos))
                            {
                                var m = FindAn1920(pos, null);
                                if (m != null) Add(m.Fibukonto, m.Gegenkonto, m.Bezeichnung, abzug);
                                else ohneCodes++;

                                // AG-Beitrag (Walter 22.05.2026): kommt FERTIG GERECHNET aus
                                // der Engine (`agBetrag` — mit der korrekten alters-/modell-
                                // gestaffelten Stufe, z.B. BVG bis25/>25/>35/…). Das Journal
                                // bucht nur noch: Soll 406x / Gegen 207x/208x/209x. Berührt
                                // 1920 NICHT.
                                decimal agBetrag = line.TryGetProperty("agBetrag", out var ab) && ab.ValueKind == JsonValueKind.Number ? ab.GetDecimal() : 0m;
                                if (agBetrag != 0)
                                {
                                    var mag = FindAg(pos);
                                    if (mag != null) Add(mag.Fibukonto, mag.Gegenkonto, mag.Bezeichnung, agBetrag);
                                }

                                // FAK (nur AG, kein AN-Abzug → kein agBetrag in der Engine):
                                // läuft auf der AHV-Basis (Altersregeln + 65+-Freibetrag
                                // stecken drin) → beim Verarbeiten der AHV-Zeile mitbuchen,
                                // Position 501 (Soll 4062 / Gegen 2070). FAK ist einstufig.
                                if (catU == "AHV" && agRateByCode.TryGetValue("FAK", out var fakRate) && fakRate > 0)
                                {
                                    decimal basis = line.TryGetProperty("basis", out var bs) && bs.ValueKind == JsonValueKind.Number ? bs.GetDecimal() : 0m;
                                    decimal fak = Math.Round(basis * fakRate / 100m, 2);
                                    if (fak != 0)
                                    {
                                        var mfak = FindAg(501);
                                        if (mfak != null) Add(mfak.Fibukonto, mfak.Gegenkonto, mfak.Bezeichnung, fak);
                                    }
                                }
                            }
                            else ohneCodes++;
                        }
                        else if (!string.IsNullOrEmpty(code))
                        {
                            // Lohnpos-Abzug (z.B. LGAV "600.24") → Position.SubPos.
                            // Walter Aug 2026: positiver Slip-Betrag = Rückerstattung
                            // (Uniformen-Depot) → Konten tauschen (2021→1920).
                            var parts = code!.Split('.');
                            if (int.TryParse(parts[0], out var pos))
                            {
                                int? sub = parts.Length > 1 && int.TryParse(parts[1], out var sp) ? sp : (int?)null;
                                var m = FindAn1920(pos, sub);
                                if (m != null)
                                {
                                    if (betrag > 0)
                                        Add(m.Gegenkonto, m.Fibukonto, m.Bezeichnung + " Rückerstattung", abzug);
                                    else
                                        Add(m.Fibukonto, m.Gegenkonto, m.Bezeichnung, abzug);
                                }
                                else ohneCodes++;
                            }
                            else ohneCodes++;
                        }
                        else ohneCodes++;   // Zeile ohne Code (Alt-Snapshot) → bitte Periode neu bestätigen
                    }
                }

                // 2c) Rückstellung Ferien/Feiertage (Walter-Vorgabe 22.05.2026) →
                // Position 4070 (SubPos 20=Ferien / 30=Feiertag), Aufwand ↔ RST 2019.
                // Gebucht wird der MONATLICHE NETTO-Zuwachs (Accrual − Bezug); positiv
                // = RST bilden (Soll Aufwand/Gegen 2019), negativ = RST auflösen
                // (umgekehrt). Über die Monate ergibt das den laufenden RST-Saldo.
                // Tage→CHF nur für FIX/FIX-M (Tagessatz, 7-Tage-Basis); UTP/MTP führen
                // Ferien als CHF-Geld (ferienGeld...). Berührt 1920 NICHT.
                decimal SlipDec(string key) =>
                    doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;

                empByEmp.TryGetValue(s.EmployeeId, out var emp);
                var modelU = (model ?? "").ToUpperInvariant();

                // Tagessatz für FIX/FIX-M RST Ferien/Feiertag.
                // Walter-Vorgabe 26.05.2026 (final, ABSOLUT): FIX/FIX-M hat einen
                // FESTEN Monatslohn → der Ferien-Tagessatz rechnet auf KALENDER-Basis:
                //     Tagessatz = Monatslohn × 12 / 365
                // Damit konsistent zu PayrollCalculationEngine.fixTagessatz (Z.2017,
                // selbe Formel). Frühere /364-Formel (1/7-Logik) war falsch — sie
                // gilt nur für MTP (Stundenlöhner), wo der Monatslohn schwankt.
                decimal tagessatz = 0m;
                if (modelU is "FIX" or "FIX-M")
                    tagessatz = Math.Round((emp?.MonthlySalary ?? (emp?.MonthlySalaryFte * (emp?.EmploymentPercentage / 100m)) ?? 0m) * 12m / 365m, 4);

                // Ferien-RST: UTP/MTP in CHF (Ferien-Geld), FIX/FIX-M Tage × Tagessatz.
                decimal ferienRst = (modelU is "FLEX" or "MTP")
                    ? SlipDec("ferienGeldAccrual") - SlipDec("ferienGeldAuszahlung")
                    : (SlipDec("ferienTageAccrual") - SlipDec("ferienTageGenommen")) * tagessatz;
                ferienRst = Math.Round(ferienRst, 2);
                if (ferienRst != 0)
                {
                    var m = FindRstBuild(20, kst);
                    if (m != null)
                    {
                        if (ferienRst > 0) Add(m.Fibukonto, m.Gegenkonto, "RST Ferien " + (m.KostenstelleName ?? kst), ferienRst);
                        else               Add(m.Gegenkonto, m.Fibukonto, "RST Ferien Auflösung " + (m.KostenstelleName ?? kst), -ferienRst);
                    }
                }

                // Feiertag-RST: nur FIX/FIX-M haben Feiertag-Tage-Saldo (sonst 0).
                decimal feiertagRst = Math.Round((SlipDec("feiertagTageAccrual") - SlipDec("feiertagTageGenommen")) * tagessatz, 2);
                if (feiertagRst != 0)
                {
                    var m = FindRstBuild(30, kst);
                    if (m != null)
                    {
                        if (feiertagRst > 0) Add(m.Fibukonto, m.Gegenkonto, "RST Feiertage " + (m.KostenstelleName ?? kst), feiertagRst);
                        else                 Add(m.Gegenkonto, m.Fibukonto, "RST Feiertage Auflösung " + (m.KostenstelleName ?? kst), -feiertagRst);
                    }
                }
            }
            catch { ohneCodes++; }

            // Hinweis: Familienzulagen / Spesen liegen in zulagenExtraLines und sind
            // im snapshot.Netto NICHT enthalten (nettolohn ist VOR den Extras). Daher
            // werden sie hier bewusst NICHT gebucht — sonst ginge 1920 nicht auf.
            // Mirus-konforme Variante (auszahlungsbetrag inkl. FamZ als Netto + FamZ
            // → 2071) ist als v3 offen.

            // 3) Nettolohn → Position 1060.
            if (s.Netto != 0)
            {
                var m = FindByPosKst(1060, null);
                if (m != null) Add(m.Fibukonto, m.Gegenkonto, m.Bezeichnung, s.Netto);
            }

            // 4) RST 13. ML (aktueller Monat) → Position 2010 × Kostenstelle.
            if (saldoByEmp.TryGetValue(s.EmployeeId, out var sal) && sal.ThirteenthMonthMonthly != 0)
            {
                var m = FindByPosKst(2010, kst);
                if (m != null) Add(m.Fibukonto, m.Gegenkonto, m.Bezeichnung, sal.ThirteenthMonthMonthly);
            }
        }

        var lines = acc.Values
            .OrderBy(l => l.Soll).ThenBy(l => l.Gegen)
            .ToList();

        // Bilanz-Kontrollen.
        decimal sollTotal  = lines.Sum(l => l.Betrag);   // jede Zeile = ein Soll-Betrag
        decimal habenTotal = sollTotal;                  // ... und derselbe Haben-Betrag (immer ausgeglichen)
        decimal k1920 = lines.Where(l => l.Soll  == "1920").Sum(l => l.Betrag)
                      - lines.Where(l => l.Gegen == "1920").Sum(l => l.Betrag);

        var hinweise = new List<string>
        {
            "v2.3 — Bruttolohn, AN-Abzüge (SV/QST/LGAV), Nettolohn, RST 13. ML, RST Ferien/Feiertage und AG-Sozialbeiträge inkl. FAK (406x, wo ein AG-Satz gepflegt ist) verbucht. NOCH OHNE: Familienzulagen/Spesen (Mirus bucht sie über 2071 — folgt in v3). Berührt das Durchlaufkonto 1920 NICHT (FamZ ausserhalb Netto; RST + AG-Beiträge laufen Aufwand↔RST/Verbindlichkeit). Hinweis: AG-Sätze (KTG/BVG/BU) in Systemeinstellungen → SV-Sätze pflegen, sonst werden sie nicht gebucht.",
        };
        if (ohneCodes > 0)
            hinweise.Add($"{ohneCodes} Abzugszeile(n) ohne Code/Konto übersprungen — entweder Alt-Snapshot (》🔄 Codes nachtragen《) oder eine Lohnart fehlt im Kontoplan.");
        if (Math.Abs(k1920) > 0.05m)
            hinweise.Add($"Durchlaufkonto 1920 nicht 0 (Differenz CHF {Chf(k1920)}) — die abzugLines stimmen nicht mit (Brutto − Netto) überein; Periode zurücksetzen + neu bestätigen, damit Snapshot und Codes konsistent sind.");

        return new JournalResult(periode, lines, sollTotal, habenTotal, Math.Round(k1920, 2), snapshots.Count, hinweise);
    }

    public async Task<byte[]> GeneratePdfAsync(int companyProfileId, int year, int month)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var r = await GenerateAsync(companyProfileId, year, month);
        var periodLabel = $"{MonatsNamen[month - 1]} {year}";
        var parentName  = r.Periode.Company!.CompanyName ?? "";
        var company     = r.Periode.Company.BranchName ?? "";

        return Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.MarginTop(0.5f, Unit.Centimetre);
                page.MarginBottom(1.0f, Unit.Centimetre);
                page.MarginHorizontal(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9.5f).LineHeight(1.2f).FontColor(Dark));

                page.Header().Height(38).Layers(layers =>
                {
                    layers.Layer().Image(BannerBytes).FitWidth();
                    layers.PrimaryLayer().PaddingTop(10).AlignCenter()
                        .Text($"Fibu-Journal {periodLabel}").Bold().FontSize(12f).FontColor(Dark);
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    col.Item().Text(parentName).Bold().FontSize(10f);
                    col.Item().Text(company).FontSize(9.5f);
                    col.Item().PaddingTop(6).Text($"Druckdatum: {DateTime.Today:dd.MM.yyyy} · Periode: {r.Periode.PeriodFrom:dd.MM.yyyy}–{r.Periode.PeriodTo:dd.MM.yyyy} · {r.AnzahlMitarbeiter} MA").FontSize(9f).FontColor(Muted);

                    col.Item().PaddingTop(12).Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(28);   // S/H
                            cd.ConstantColumn(55);   // Soll
                            cd.ConstantColumn(55);   // Gegen
                            cd.RelativeColumn(1);    // Bezeichnung
                            cd.ConstantColumn(80);   // Betrag
                        });
                        table.Header(h =>
                        {
                            void hdr(string t, bool right = false)
                            {
                                var cell = h.Cell().PaddingVertical(4).PaddingHorizontal(3).BorderBottom(1).BorderColor(Dark);
                                (right ? cell.AlignRight().Text(t) : cell.Text(t)).Bold().FontSize(8.5f);
                            }
                            hdr("S/H"); hdr("Konto"); hdr("Gegen"); hdr("Bezeichnung"); hdr("Betrag CHF", true);
                        });
                        int i = 0;
                        foreach (var l in r.Lines)
                        {
                            var bg = (i++ % 2 == 1) ? "#F8F8F8" : "#FFFFFF";
                            void cell(Action<IContainer> content) =>
                                content(table.Cell().Background(bg).PaddingVertical(2.5f).PaddingHorizontal(3).BorderBottom(0.3f).BorderColor("#CCCCCC"));
                            cell(x => x.Text("S").FontSize(8.5f).FontColor(Muted));
                            cell(x => x.Text(l.Soll).FontSize(8.5f).FontFamily("Consolas"));
                            cell(x => x.Text(l.Gegen).FontSize(8.5f).FontFamily("Consolas"));
                            cell(x => x.Text(l.Bezeichnung).FontSize(8.5f));
                            cell(x => x.AlignRight().Text(Chf(l.Betrag)).FontSize(8.5f).FontFamily("Consolas"));
                        }
                        // Summenzeile
                        table.Cell().ColumnSpan(4).PaddingTop(4).PaddingHorizontal(3).BorderTop(1).BorderColor(Dark)
                            .Text("Total (Soll = Haben)").Bold().FontSize(9f);
                        table.Cell().PaddingTop(4).PaddingHorizontal(3).BorderTop(1).BorderColor(Dark)
                            .AlignRight().Text(Chf(r.SollTotal)).Bold().FontSize(9.5f).FontFamily("Consolas");
                    });

                    if (r.Hinweise.Count > 0)
                    {
                        col.Item().PaddingTop(12).Column(h =>
                        {
                            h.Item().Text("Hinweise").Bold().FontSize(9f).FontColor(Muted);
                            foreach (var hint in r.Hinweise)
                                h.Item().PaddingTop(2).Text("• " + hint).FontSize(8.5f).FontColor(Muted);
                        });
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Fibu-Journal · generiert ").FontSize(7.5f).FontColor(Muted);
                    t.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm")).FontSize(7.5f).FontColor(Muted);
                    t.Span(" · ").FontSize(7.5f).FontColor(Muted);
                    t.CurrentPageNumber().FontSize(7.5f).FontColor(Muted);
                    t.Span(" / ").FontSize(7.5f).FontColor(Muted);
                    t.TotalPages().FontSize(7.5f).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }
}
