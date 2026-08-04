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
///   • v3 Brutto-Umgliederung (Walter-Vorgabe 04.08.2026, Mirus-Deckungsgleichheit):
///     drei Anteile stecken im snapshot.Brutto, sind aber KEIN echter Personal-
///     aufwand des Monats — sie werden aus der 400x-Aufwand-Buchung herausgelöst
///     und separat gebucht. Die Haben-Seite von 1920 bleibt in Summe identisch
///     (Rest-Aufwand + Spezialzeilen = Brutto) → Balance strukturell unverändert:
///       – Familienzulagen (190.x — QST-pflichtig, daher in lohnLines/Brutto)
///         → S 2071 / H 1920 (Durchlauf — die FAK erstattet sie dem AG)
///       – KTG-/UVG-Taggeld 80% (Codes 70.2/60.2) → S 2014 / H 1920 (Forderung
///         Versicherung); die KARENZ-Entschädigungen 88% (Codes 70/60) bleiben
///         bewusst echter AG-Aufwand (400x)
///       – 13.-ML-Auszahlung AUS DEM SALDO («Saldo-Auszahlung» / «Nachzahlung
///         nach Probezeit») → S 2017 (Crew) / 2016 (Mgmt/Gerant) / H 1920
///         (RST-Abbau). Der im Auszahlungsmonat NEU verdiente Anteil («akt.
///         Monat» bzw. FLEX-Monatszeile) bleibt Aufwand — für ihn wird im
///         selben Monat KEINE RST gebildet (thirteenthPctForSaldo=0 im
///         Auszahlungsmonat → ThirteenthMonthMonthly=0, geprüft 04.08.2026)
///       – 13.-ML-Verfall in Probezeit (Slip-betrag=0, Wert in accrued) →
///         RST-Auflösung S 2017/2016 / H Aufwand 4010/4057 (berührt 1920 nicht)
///     Extraktion via ExtractBruttoUmgliederung (statisch, seiteneffektfrei,
///     unit-getestet): lohnLines tragen KEINE Codes → Matching über die
///     Lohnposition-Bezeichnungen aus der DB + fixe Engine-Strings als Fallback
///     (Alt-Snapshots). Es wird exakt der extrahierte Betrag vom Aufwand
///     abgezogen und identisch auf den Spezialkonten gebucht → doppelt oder gar
///     nicht buchen ist strukturell unmöglich; findet die Extraktion nichts,
///     verhält sich das Journal wie v2.3 (alles Personalaufwand).
///
/// Das Journal balanciert, WEIL snapshot.Netto = totalLohn − Abzüge (Identität
/// Brutto = Netto + Abzüge). Spesen liegen in zulagenExtraLines und sind im
/// snapshot.Netto NICHT enthalten (nettolohn ist VOR den Extras) → sie werden
/// hier NICHT gebucht. Die RST-Buchungen berühren 1920 NICHT (Aufwand ↔ RST),
/// können das Durchlaufkonto also nicht aus der Balance bringen.
///
/// v3 NOCH OHNE: Spesen/übrige zulagenExtraLines (liegen ausserhalb des Netto)
/// und das Abacus-Exportformat (E3).
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

    // ── v3: Brutto-Umgliederung (Walter-Vorgabe 04.08.2026) ────────────────
    // Statische, seiteneffektfreie Extraktion der Umgliederungs-Summen aus den
    // SlipJson-lohnLines EINES Snapshots — bewusst public static, damit die
    // Summen-Logik ohne DB unit-testbar ist (Tests/FibuJournalUmgliederungTests).

    /// <summary>Extrahierte Umgliederungs-Summen aus den lohnLines eines Snapshots.</summary>
    public sealed record BruttoUmgliederung(
        decimal Famz,
        decimal KtgTaggeld,
        decimal UvgTaggeld,
        decimal Ml13Auszahlung,
        decimal Ml13Verfall)
    {
        /// <summary>
        /// Summe, die der Brutto-Aufwand-Buchung (400x) entnommen wird. Der
        /// Verfall zählt NICHT dazu — dessen Slip-Zeile hat betrag=0 und steckt
        /// damit gar nicht im Brutto (reine RST-Auflösung Aufwand ↔ RST).
        /// </summary>
        public decimal AufwandAbzug => Famz + KtgTaggeld + UvgTaggeld + Ml13Auszahlung;
        public static readonly BruttoUmgliederung Leer = new(0, 0, 0, 0, 0);
    }

    // Fixe Engine-Strings als Fallback (Alt-Snapshots bzw. umbenannte
    // Lohnpositionen — die Slip-Bezeichnung friert den Stand zur Rechenzeit ein).
    public static readonly string[] FamzFallbackPrefixes =
        { "Kinderzulage", "Ausbildungszulage", "Geburtszulage", "Adoptionszulage", "Geburts-/Adoptionszulage" };
    public static readonly string[] KtgTaggeldFallbackPrefixes = { "Krankheit (Taggeld" };
    public static readonly string[] UvgTaggeldFallbackPrefixes = { "Unfall (Taggeld" };
    // 13.-ML-Auszahlung AUS DEM SALDO: NUR diese zwei exakten Engine-Zeilen.
    // «13. Monatslohn (akt. Monat)» und die FLEX-Monatszeile «13. Monatslohn»
    // bleiben bewusst Personalaufwand — für sie wird im selben Monat keine RST
    // gebildet (ThirteenthMonthMonthly=0 im Auszahlungsmonat).
    // ACHTUNG importierte Alt-Saldi (Walter-Entscheidung 04.08.2026): der
    // 906-Vortrag (Mirus «Rückstellungsliste Saldomethode», auch FLEX) hat
    // KEINE OneCrew-RST-Bildungsbuchung — der Bestand stammt aus der Mirus-
    // Eröffnungsbilanz. Seine Auszahlung («Saldo-Auszahlung» / «Nachzahlung
    // nach Probezeit») bucht hier trotzdem den RST-Abbau S 2017/2016 / H 1920;
    // das RST-Konto muss den Anfangsbestand aus der Eröffnungsbilanz tragen,
    // sonst läuft es durch den Abbau ins Minus (Buchhaltung, nicht OneCrew).
    public static readonly string[] Ml13AuszahlungPrefixes =
        { "13. Monatslohn (Saldo-Auszahlung)", "13. Monatslohn (Nachzahlung nach Probezeit)" };
    // Verfall in Probezeit: Zeile hat betrag=0 (kein Lohn) — der verfallene
    // RST-Bestand steht im Feld «accrued» → RST auflösen (S RST / H Aufwand).
    public static readonly string[] Ml13VerfallPrefixes = { "13. Monatslohn (verfallen" };

    /// <summary>
    /// Summiert FamZ / KTG-Taggeld / UVG-Taggeld / 13.-ML-Saldo-Auszahlung /
    /// 13.-ML-Verfall aus den lohnLines. Die *Namen-Listen sind die aktuellen
    /// Lohnposition-Bezeichnungen aus der DB (Match: exakt ODER «Name (…»,
    /// weil die Engine optional « (Bemerkung)» anhängt); die statischen
    /// Fallback-Prefixe greifen zusätzlich für Alt-Snapshots.
    /// </summary>
    public static BruttoUmgliederung ExtractBruttoUmgliederung(
        JsonElement slipRoot,
        IReadOnlyList<string>? famzNamen = null,
        IReadOnlyList<string>? ktgTaggeldNamen = null,
        IReadOnlyList<string>? uvgTaggeldNamen = null)
    {
        if (!slipRoot.TryGetProperty("lohnLines", out var lines) || lines.ValueKind != JsonValueKind.Array)
            return BruttoUmgliederung.Leer;

        static bool NameMatch(string bez, IReadOnlyList<string>? namen)
        {
            if (namen == null) return false;
            foreach (var n in namen)
            {
                if (string.IsNullOrWhiteSpace(n)) continue;
                if (bez == n || bez.StartsWith(n + " (", StringComparison.Ordinal)) return true;
            }
            return false;
        }
        static bool PrefixMatch(string bez, string[] prefixes)
        {
            foreach (var p in prefixes)
                if (bez.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        decimal famz = 0, ktg = 0, uvg = 0, ausz13 = 0, verfall13 = 0;
        foreach (var line in lines.EnumerateArray())
        {
            string bez = line.TryGetProperty("bezeichnung", out var bz) && bz.ValueKind == JsonValueKind.String
                ? (bz.GetString() ?? "") : "";
            if (bez.Length == 0) continue;
            decimal betrag = line.TryGetProperty("betrag", out var b) && b.ValueKind == JsonValueKind.Number
                ? b.GetDecimal() : 0m;

            // Verfall zuerst — betrag=0, der Wert steht in «accrued». Der Prefix
            // «13. Monatslohn (verfallen» kollidiert nicht mit den Auszahlungs-Zeilen.
            if (PrefixMatch(bez, Ml13VerfallPrefixes))
            {
                if (line.TryGetProperty("accrued", out var a) && a.ValueKind == JsonValueKind.Number)
                    verfall13 += a.GetDecimal();
                continue;
            }
            if (betrag == 0) continue;   // reine Anzeige-Zeilen (Accrual etc.)

            if (PrefixMatch(bez, Ml13AuszahlungPrefixes))
                ausz13 += betrag;
            else if (NameMatch(bez, ktgTaggeldNamen) || PrefixMatch(bez, KtgTaggeldFallbackPrefixes))
                ktg += betrag;
            else if (NameMatch(bez, uvgTaggeldNamen) || PrefixMatch(bez, UvgTaggeldFallbackPrefixes))
                uvg += betrag;
            else if (NameMatch(bez, famzNamen) || PrefixMatch(bez, FamzFallbackPrefixes))
                famz += betrag;
        }
        return new BruttoUmgliederung(
            Math.Round(famz, 2), Math.Round(ktg, 2), Math.Round(uvg, 2),
            Math.Round(ausz13, 2), Math.Round(verfall13, 2));
    }

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

        // v3: Bezeichnungs-Quellen für die Brutto-Umgliederung — die lohnLines
        // tragen KEINE Codes, daher Matching über die Lohnposition-Bezeichnungen
        // (DB, inkl. inaktiver Versionen) + fixe Engine-Fallbacks (Alt-Snapshots).
        var famzNamen = await _db.Lohnpositionen
            .Where(l => l.Kategorie == "Familienzulagen"
                     || l.Code == "190.1" || l.Code == "190.2" || l.Code == "190.3")
            .Select(l => l.Bezeichnung)
            .ToListAsync();
        var ktgTaggeldNamen = await _db.Lohnpositionen
            .Where(l => l.Code == "70.2").Select(l => l.Bezeichnung).ToListAsync();
        var uvgTaggeldNamen = await _db.Lohnpositionen
            .Where(l => l.Code == "60.2").Select(l => l.Bezeichnung).ToListAsync();

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
        // v3-Lookups (Walter-Vorgabe 04.08.2026) ------------------------------
        // Familienzulagen → Mirus-Position 190 (S 2071 / H 1920); Fallback die
        // Nachzahlungs-Zeile 200.190.
        LohnKontoMapping? FindFamz() =>
            maps.FirstOrDefault(m => m.Position == 190 && m.Gegenkonto == "1920")
            ?? maps.FirstOrDefault(m => m.Position == 200 && m.SubPosition == 190 && m.Gegenkonto == "1920");
        // Vers.-Entschädigung (Taggeld) = die 2014-Zeile der Position (Mirus:
        // KTG 70.2, UVG 60.3 — Engine-Code 60.2 wird bewusst auf die 2014-Zeile
        // der Position 60 gemappt, egal welche SubPos der Seed führt).
        LohnKontoMapping? FindVersEntsch(int pos, string? kst) =>
            maps.FirstOrDefault(m => m.Position == pos && m.Fibukonto == "2014" && m.KostenstelleNr == kst)
            ?? maps.FirstOrDefault(m => m.Position == pos && m.Fibukonto == "2014");
        // 13.-ML-Auszahlung aus dem Saldo → Position 180 (S 2017 Crew /
        // 2016 Management+Gerant, H 1920) × Kostenstelle.
        LohnKontoMapping? Find13MlAuszahlung(string? kst) =>
            maps.FirstOrDefault(m => m.Position == 180 && m.KostenstelleNr == kst && m.Gegenkonto == "1920")
            ?? maps.FirstOrDefault(m => m.Position == 180 && m.Gegenkonto == "1920");
        // RST-13-Bildung/-Verfall (Position 2010) × Kostenstelle. Der Mirus-
        // Kontoplan kennt KEINE 2010-Zeile für KSt 200 (Crew Flex zahlt den 13.
        // monatlich aus) — der FLEX-PROBEZEIT-Pot braucht sie aber. Fallback
        // daher KSt-korrekt auf die RST-Seite: Crew (100/200) → 2017-Zeile,
        // Management/Gerant (300/400) → 2016-Zeile. NIE blind die erste
        // 2010-Zeile nehmen (könnte die Management-Zeile sein → falsche Konten).
        LohnKontoMapping? Find13MlRst(string? kst) =>
            maps.FirstOrDefault(m => m.Position == 2010 && m.KostenstelleNr == kst)
            ?? (kst is "300" or "400"
                ? maps.FirstOrDefault(m => m.Position == 2010 && m.Gegenkonto == "2016")
                : maps.FirstOrDefault(m => m.Position == 2010 && m.Gegenkonto == "2017"));
        string KstName(string kst) =>
            maps.FirstOrDefault(m => m.KostenstelleNr == kst && m.KostenstelleName != null)?.KostenstelleName
            ?? ("KSt " + kst);

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
        // v3: Umgliederungs-Totale über alle MA — für den Transparenz-Hinweis
        // (Abgleich mit dem Mirus-Referenzjournal).
        decimal totFamz = 0, totTaggeld = 0, tot13Ausz = 0;

        foreach (var s in snapshots)
        {
            modelByEmp.TryGetValue(s.EmployeeId, out var model);
            var kst = KstFor(model);

            // v3 (Walter-Vorgabe 04.08.2026): Durchlauf-/RST-Anteile aus den
            // lohnLines extrahieren, BEVOR der Brutto-Aufwand gebucht wird.
            // Bei defektem/leerem Slip → Leer = Verhalten exakt wie v2.3.
            var umgl = BruttoUmgliederung.Leer;
            try
            {
                using var slipDoc = JsonDocument.Parse(string.IsNullOrWhiteSpace(s.SlipJson) ? "{}" : s.SlipJson);
                umgl = ExtractBruttoUmgliederung(slipDoc.RootElement, famzNamen, ktgTaggeldNamen, uvgTaggeldNamen);
            }
            catch { /* defekter Slip → alles bleibt Personalaufwand (v2.3) */ }
            totFamz    += umgl.Famz;
            totTaggeld += umgl.KtgTaggeld + umgl.UvgTaggeld;
            tot13Ausz  += umgl.Ml13Auszahlung;

            // 1) Bruttolohn → Position 10 × Kostenstelle — REDUZIERT um die
            //    umgegliederten Anteile (FamZ / Taggelder 80% / 13.-ML-Saldo-
            //    Auszahlung). Haben-Seite 1920 bleibt in Summe identisch
            //    (Rest-Aufwand + Spezialzeilen = Brutto) → Balance unverändert.
            decimal bruttoAufwand = Math.Round(s.Brutto - umgl.AufwandAbzug, 2);
            if (bruttoAufwand != 0)
            {
                var m = FindByPosKst(10, kst);
                if (m != null)
                {
                    if (bruttoAufwand > 0) Add(m.Fibukonto, m.Gegenkonto, m.Bezeichnung, bruttoAufwand);
                    else                   Add(m.Gegenkonto, m.Fibukonto, m.Bezeichnung + " (negativ)", -bruttoAufwand);
                }
            }

            // 1b) Familienzulagen → S 2071 / H 1920 (Durchlauf — FAK erstattet dem AG).
            if (umgl.Famz != 0)
            {
                var mf = FindFamz();
                var (soll, gegen, bez) = mf != null
                    ? (mf.Fibukonto, mf.Gegenkonto, mf.Bezeichnung)
                    : ("2071", "1920", "Familienzulagen");   // Kontoplan-Zeile fehlt → feste Mirus-Konten
                if (umgl.Famz > 0) Add(soll, gegen, bez, umgl.Famz);
                else               Add(gegen, soll, bez + " Rückforderung", -umgl.Famz);
            }

            // 1c) KTG-/UVG-Taggeld 80% → S 2014 / H 1920 (Forderung Versicherung).
            //     Karenz-Entschädigungen 88% (Codes 70/60) bleiben bewusst
            //     Personalaufwand — die zahlt der AG selbst.
            if (umgl.KtgTaggeld != 0)
            {
                var mk = FindVersEntsch(70, kst);
                var (soll, gegen, bez) = mk != null
                    ? (mk.Fibukonto, mk.Gegenkonto, mk.Bezeichnung)
                    : ("2014", "1920", "Vers.-Entsch. KTG " + KstName(kst));
                if (umgl.KtgTaggeld > 0) Add(soll, gegen, bez, umgl.KtgTaggeld);
                else                     Add(gegen, soll, bez + " Korrektur", -umgl.KtgTaggeld);
            }
            if (umgl.UvgTaggeld != 0)
            {
                var mu = FindVersEntsch(60, kst);
                var (soll, gegen, bez) = mu != null
                    ? (mu.Fibukonto, mu.Gegenkonto, mu.Bezeichnung)
                    : ("2014", "1920", "Vers.-Entsch. UVG " + KstName(kst));
                if (umgl.UvgTaggeld > 0) Add(soll, gegen, bez, umgl.UvgTaggeld);
                else                     Add(gegen, soll, bez + " Korrektur", -umgl.UvgTaggeld);
            }

            // 1d) 13.-ML-Auszahlung aus dem Saldo → RST-Abbau S 2017 (Crew) /
            //     2016 (Management) / H 1920. Der im Auszahlungsmonat NEU
            //     verdiente Anteil («akt. Monat» / FLEX-Monatszeile) bleibt
            //     Personalaufwand — für ihn wird im selben Monat KEINE RST
            //     gebildet (ThirteenthMonthMonthly ist im Auszahlungsmonat 0).
            if (umgl.Ml13Auszahlung != 0)
            {
                var m13 = Find13MlAuszahlung(kst);
                var (soll, gegen, bez) = m13 != null
                    ? (m13.Fibukonto, m13.Gegenkonto, m13.Bezeichnung)
                    : (kst is "300" or "400" ? "2016" : "2017", "1920", "Auszahlung 13. ML " + KstName(kst));
                if (umgl.Ml13Auszahlung > 0) Add(soll, gegen, bez, umgl.Ml13Auszahlung);
                else                         Add(gegen, soll, bez + " Korrektur", -umgl.Ml13Auszahlung);
            }

            // 1e) 13.-ML-Verfall in Probezeit: Slip-betrag=0 → steckt NICHT im
            //     Brutto. Die in Vormonaten gebildete RST wird aufgelöst —
            //     S RST (2017/2016) / H Aufwand (4010/4057), via Umkehrung der
            //     RST-Bildungszeile (Position 2010). Berührt 1920 NICHT.
            if (umgl.Ml13Verfall > 0)
            {
                var mr = Find13MlRst(kst);
                if (mr != null)
                    Add(mr.Gegenkonto, mr.Fibukonto, "Auflösung 13. ML Verfall Probezeit " + KstName(kst), umgl.Ml13Verfall);
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

            // Hinweis: Spesen und übrige nicht-SV-/QST-pflichtige Zulagen liegen
            // in zulagenExtraLines und sind im snapshot.Netto NICHT enthalten
            // (nettolohn ist VOR den Extras) → sie werden hier bewusst NICHT
            // gebucht, sonst ginge 1920 nicht auf. Familienzulagen sind seit der
            // QST-Pflicht-Migration in lohnLines/Brutto und werden in Schritt 1b
            // nach 2071 umgegliedert (v3).

            // 3) Nettolohn → Position 1060.
            if (s.Netto != 0)
            {
                var m = FindByPosKst(1060, null);
                if (m != null) Add(m.Fibukonto, m.Gegenkonto, m.Bezeichnung, s.Netto);
            }

            // 4) RST 13. ML (aktueller Monat) → Position 2010 × Kostenstelle.
            //    Für FLEX (Probezeit-Pot, KSt 200 ohne eigene Mirus-Zeile) fällt
            //    Find13MlRst auf die Crew-2017-Konten zurück — dann mit eigener
            //    Bezeichnung «RST 13. ML Crew Flex» statt dem Crew-Fix-Label,
            //    damit die Zeile im Journal von Crew Fix unterscheidbar bleibt.
            if (saldoByEmp.TryGetValue(s.EmployeeId, out var sal) && sal.ThirteenthMonthMonthly != 0)
            {
                var m = Find13MlRst(kst);
                if (m != null)
                {
                    var bez = m.KostenstelleNr == kst ? m.Bezeichnung : "RST 13. ML " + KstName(kst);
                    Add(m.Fibukonto, m.Gegenkonto, bez, sal.ThirteenthMonthMonthly);
                }
            }
        }

        // Rundungsdifferenz automatisch ausgleichen (Walter 03.08.2026):
        // Jede Slip-Zeile ist auf Rappen gerundet, das Netto zusaetzlich auf
        // 0.05 — ueber viele MA bleibt auf 1920 ein Rest von wenigen Rappen.
        // Bis CHF 2.00 wird er als eigene Zeile gegen den Personalaufwand
        // ausgeglichen (Mirus kennt dafuer die Lohnart «Rundung»). Groessere
        // Differenzen bleiben als Warnung stehen — die deuten auf einen
        // echten Snapshot-/Code-Fehler hin und duerfen nicht stillschweigend
        // weggebucht werden.
        {
            decimal preK1920 = acc.Values.Where(l => l.Soll  == "1920").Sum(l => l.Betrag)
                             - acc.Values.Where(l => l.Gegen == "1920").Sum(l => l.Betrag);
            if (preK1920 != 0 && Math.Abs(preK1920) <= 2.00m)
            {
                if (preK1920 < 0)
                    Add("1920", "4000", "Rundungsdifferenz", Math.Abs(preK1920));
                else
                    Add("4000", "1920", "Rundungsdifferenz", preK1920);
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
            "v3 — Bruttolohn (Personalaufwand nach Umgliederung), AN-Abzüge (SV/QST/LGAV), Nettolohn, RST 13. ML, RST Ferien/Feiertage, AG-Sozialbeiträge inkl. FAK (406x, wo ein AG-Satz gepflegt ist) sowie Mirus-konforme Umgliederung: Familienzulagen → 2071 (Durchlauf), KTG-/UVG-Taggelder 80% → 2014 (Forderung Versicherung; Karenz 88% bleibt Aufwand), 13.-ML-Auszahlung aus dem Saldo → 2017/2016 (RST-Abbau; der im Auszahlungsmonat neu verdiente Anteil bleibt Aufwand), 13.-ML-Verfall in Probezeit → RST-Auflösung. NOCH OHNE: Spesen/übrige Netto-Extras (liegen ausserhalb des Journal-Netto) und Abacus-Exportformat. Hinweis: AG-Sätze (KTG/BVG/BU) in Systemeinstellungen → SV-Sätze pflegen, sonst werden sie nicht gebucht.",
        };
        if (totFamz != 0 || totTaggeld != 0 || tot13Ausz != 0)
            hinweise.Add($"Umgliederung aus dem Bruttolohn: Familienzulagen CHF {Chf(totFamz)} · Taggelder KTG/UVG CHF {Chf(totTaggeld)} · 13.-ML-Saldo-Auszahlung CHF {Chf(tot13Ausz)} — zum Abgleich mit dem Mirus-Journal.");
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

    // ══════════════════════════════════════════════════════════════════════
    // E3: Abacus-Export — AbaConnect «FIBU / XML Buchungen» Version 2014.00
    // (Walter-Vorgabe 04.08.2026). Vorlage = echter Mirus-Export
    // «FibuExpAbacusV2014.xml» (Juni 2026): pro Journal-Zeile eine
    // <Transaction> mit <CollectiveInformation> (Soll-Konto, DebitCredit D)
    // + <SingleInformation> (Gegenkonto). Sammelbuchungs-Stil (EntryType S,
    // EntryLevel A), Währung CHF, Kostenstellen NICHT übergeben (BookingLevel
    // 0 — wie Mirus; die KSt steckt im Text «Crew Fix»/«Manager»).
    //
    // Abweichung zu Mirus (bewusst): EntryDate = PERIODENENDE (z.B. 31.07.)
    // als Default, nicht das Exportdatum — die Buchung gehört in die Lohn-
    // periode. Im UI ist das Buchungsdatum wählbar (Walter/Treuhänder-
    // Empfehlung 04.08.2026). Beträge InvariantCulture «0.00» (Punkt, keine
    // Tausender-Trennzeichen); negative Beträge sind erlaubt und werden NIE
    // Soll/Haben-getauscht (auch Mirus liefert sie, z.B. Überstunden-Korrektur
    // mit TaxCode). XML via XElement → korrektes Escaping der Texte.
    //
    // MWST-Felder (Treuhänder-Analyse 04.08.2026, aus der Mirus-Referenz):
    // ALLE Personalaufwand-Buchungen (Soll 4xxx / Gegen 1920 — Bruttolöhne,
    // ausbezahlte Ferien/Feiertage, Überstunden/Nacht, Karenzentschädigungen,
    // Ferien-/Feiertagsentschädigungen) tragen zusätzlich:
    //   • Aufwand-Seite (CI):  <TaxAccount>1067</TaxAccount>
    //   • 1920-Seite (SI):     <TaxAccount>1920</TaxAccount> + <TaxData> mit
    //     TaxCode 200, Country CH, TaxIncluded I, Betrag/Satz 0 (Null-MWST-
    //     Deklaration — Lohn ist nicht steuerbar, Abacus will den Code trotzdem).
    // RST-Zeilen (4xxx↔2019/2017/2016), AG-Beiträge (406x→20xx), Abzüge
    // (1920→2xxx) und Umgliederungen (2014/2016/2017/2071→1920) haben in der
    // Mirus-Referenz KEINE Steuerfelder → bei uns ebenso.
    // ══════════════════════════════════════════════════════════════════════

    // Mandant-Konstanten aus der Mirus-Referenzdatei (Schaub Restaurants GmbH).
    // Sollte der Treuhänder den Abacus-Mandanten umstellen, hier anpassen.
    private const string AbaTaxAccountAufwand = "1067";
    private const string AbaTaxCode           = "200";

    /// <summary>Statisch + seiteneffektfrei — unit-testbar (Tests/AbaConnectExportTests).</summary>
    public static string BuildAbaConnectXml(IReadOnlyList<JournalLine> lines, DateOnly entryDate)
    {
        static string Amt(decimal v) =>
            v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        string datum = entryDate.ToString("yyyy-MM-dd");

        var task = new System.Xml.Linq.XElement("Task",
            new System.Xml.Linq.XElement("Parameter",
                new System.Xml.Linq.XElement("Application", "FIBU"),
                new System.Xml.Linq.XElement("Id", "XML Buchungen"),
                new System.Xml.Linq.XElement("MapId", "AbaDefault"),
                new System.Xml.Linq.XElement("Version", "2014.00")));

        int id = 0;
        foreach (var l in lines)
        {
            id++;
            // MWST-Felder nur auf Personalaufwand → Durchlaufkonto (Mirus-Muster).
            bool mitTax = l.Soll.StartsWith("4") && l.Gegen == "1920";

            var ci = new System.Xml.Linq.XElement("CollectiveInformation", new System.Xml.Linq.XAttribute("mode", "SAVE"),
                new System.Xml.Linq.XElement("EntryLevel", "A"),
                new System.Xml.Linq.XElement("EntryType", "S"),
                new System.Xml.Linq.XElement("Type", "Normal"),
                new System.Xml.Linq.XElement("DebitCredit", "D"),
                new System.Xml.Linq.XElement("Client", ""),
                new System.Xml.Linq.XElement("Division", ""),
                new System.Xml.Linq.XElement("KeyCurrency", "CHF"),
                new System.Xml.Linq.XElement("EntryDate", datum),
                new System.Xml.Linq.XElement("ValueDate", ""),
                new System.Xml.Linq.XElement("AmountData", new System.Xml.Linq.XAttribute("mode", "SAVE"),
                    new System.Xml.Linq.XElement("Currency", "CHF"),
                    new System.Xml.Linq.XElement("Amount", Amt(l.Betrag))),
                new System.Xml.Linq.XElement("KeyAmount", Amt(l.Betrag)),
                new System.Xml.Linq.XElement("Account", l.Soll));
            if (mitTax)
                ci.Add(new System.Xml.Linq.XElement("TaxAccount", AbaTaxAccountAufwand));
            ci.Add(
                new System.Xml.Linq.XElement("BookingLevel1", "0"),
                new System.Xml.Linq.XElement("BookingLevel2", "0"),
                new System.Xml.Linq.XElement("BookingLevel3", "0"),
                new System.Xml.Linq.XElement("Text1", l.Bezeichnung),
                new System.Xml.Linq.XElement("Text2", ""),
                new System.Xml.Linq.XElement("DocumentNumber", ""),
                new System.Xml.Linq.XElement("SingleCount", "0"));

            var si = new System.Xml.Linq.XElement("SingleInformation", new System.Xml.Linq.XAttribute("mode", "SAVE"),
                new System.Xml.Linq.XElement("Type", "Normal"),
                new System.Xml.Linq.XElement("DebitCredit", "D"),
                new System.Xml.Linq.XElement("EntryDate", datum),
                new System.Xml.Linq.XElement("ValueDate", ""),
                new System.Xml.Linq.XElement("AmountData", new System.Xml.Linq.XAttribute("mode", "SAVE"),
                    new System.Xml.Linq.XElement("Currency", "CHF"),
                    new System.Xml.Linq.XElement("Amount", Amt(l.Betrag))),
                new System.Xml.Linq.XElement("KeyAmount", Amt(l.Betrag)),
                new System.Xml.Linq.XElement("Account", l.Gegen));
            if (mitTax)
                si.Add(new System.Xml.Linq.XElement("TaxAccount", l.Gegen));
            si.Add(
                new System.Xml.Linq.XElement("BookingLevel1", "0"),
                new System.Xml.Linq.XElement("BookingLevel2", "0"),
                new System.Xml.Linq.XElement("BookingLevel3", "0"),
                new System.Xml.Linq.XElement("Text1", l.Bezeichnung),
                new System.Xml.Linq.XElement("Text2", ""),
                new System.Xml.Linq.XElement("DocumentNumber", ""),
                new System.Xml.Linq.XElement("SelectionCode", ""));
            if (mitTax)
                si.Add(new System.Xml.Linq.XElement("TaxData", new System.Xml.Linq.XAttribute("mode", "SAVE"),
                    new System.Xml.Linq.XElement("TaxIncluded", "I"),
                    new System.Xml.Linq.XElement("TaxType", ""),
                    new System.Xml.Linq.XElement("UseCode", "0"),
                    new System.Xml.Linq.XElement("AmountData", new System.Xml.Linq.XAttribute("mode", "SAVE"),
                        new System.Xml.Linq.XElement("Currency", "CHF"),
                        new System.Xml.Linq.XElement("Amount", "0")),
                    new System.Xml.Linq.XElement("KeyAmount", "0.00"),
                    new System.Xml.Linq.XElement("TaxRate", "0"),
                    new System.Xml.Linq.XElement("TaxCoefficient", "0"),
                    new System.Xml.Linq.XElement("Country", "CH"),
                    new System.Xml.Linq.XElement("TaxCode", AbaTaxCode),
                    new System.Xml.Linq.XElement("FlatRate", "0")));
            si.Add(new System.Xml.Linq.XElement("NoteData", new System.Xml.Linq.XAttribute("mode", "SAVE"),
                new System.Xml.Linq.XElement("Text", "")));

            task.Add(new System.Xml.Linq.XElement("Transaction",
                new System.Xml.Linq.XAttribute("id", id),
                new System.Xml.Linq.XElement("Entry", new System.Xml.Linq.XAttribute("mode", "SAVE"), ci, si)));
        }

        var container = new System.Xml.Linq.XElement("AbaConnectContainer",
            new System.Xml.Linq.XElement("TaskCount", "1"),
            task);

        var doc = new System.Xml.Linq.XDocument(
            new System.Xml.Linq.XDeclaration("1.0", "UTF-8", null),
            container);

        // XDocument.ToString() lässt die Declaration weg → über StringWriter
        // mit UTF-8-Encoding serialisieren.
        var sb = new System.Text.StringBuilder();
        using (var w = System.Xml.XmlWriter.Create(sb, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = System.Text.Encoding.UTF8,
            // sb ist UTF-16 — die Declaration soll trotzdem UTF-8 sagen (die
            // Datei wird als UTF-8-Bytes ausgeliefert).
            OmitXmlDeclaration = false
        }))
        {
            doc.Save(w);
        }
        // XmlWriter auf StringBuilder schreibt encoding="utf-16" in die
        // Declaration — auf utf-8 korrigieren (ausgeliefert wird UTF-8).
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"");
    }

    /// <summary>
    /// Journal rechnen + als AbaConnect-XML-Bytes (UTF-8) liefern.
    /// <paramref name="entryDate"/> = FIBU-Buchungsdatum (wählbar im UI);
    /// null → Periodenende als Default.
    /// </summary>
    public async Task<(byte[] Xml, JournalResult Journal)> GenerateAbaConnectXmlAsync(
        int companyProfileId, int year, int month, DateOnly? entryDate = null)
    {
        var r = await GenerateAsync(companyProfileId, year, month);
        var xml = BuildAbaConnectXml(r.Lines, entryDate ?? r.Periode.PeriodTo);
        return (System.Text.Encoding.UTF8.GetBytes(xml), r);
    }
}
