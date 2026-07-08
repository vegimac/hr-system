using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Saldo-Listen zum Definitiv-Lohnabschluss als A4-PDF (Walter-Vorgabe 21.05.2026).
/// Pro Filiale + Periode, on-demand generiert (kein Storage). Quelle: die beim
/// Bestätigen persistierten <see cref="Models.PayrollSaldo"/>-Zeilen.
///
/// Zwei Varianten:
///   • Buchhaltung (GenerateBuchhaltungAsync): alle Saldi pro MA + Brutto/Netto
///     + IBAN + Summenzeile. Belegt die Verbuchung; spätere Abacus-Schnittstelle
///     (Lohnposition → Konto) kommt separat.
///   • GF (GenerateGfAsync): kompakte Übersicht der Saldi pro MA. Bei UTP bleibt
///     die 13.-ML-Spalte leer — der 13. wird bei UTP monatlich ausbezahlt, es gibt
///     also keinen Rückstellungs-Saldo.
/// </summary>
public class LohnSaldoListePdfService
{
    private readonly AppDbContext _db;
    public LohnSaldoListePdfService(AppDbContext db) => _db = db;

    private const string Dark  = "#000000";
    private const string Muted = "#404040";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    private static readonly System.Globalization.CultureInfo CH =
        System.Globalization.CultureInfo.GetCultureInfo("de-CH");

    private static string Chf(decimal v) => v.ToString("N2", CH);
    private static string Tage(decimal v) => v == 0 ? "—" : v.ToString("0.##", CH);
    private static string Std(decimal v)  => v == 0 ? "—" : v.ToString("0.##", CH);
    private static string ChfOrDash(decimal v) => v == 0 ? "—" : v.ToString("N2", CH);

    private static readonly string[] MonatsNamen =
        { "Januar", "Februar", "März", "April", "Mai", "Juni",
          "Juli", "August", "September", "Oktober", "November", "Dezember" };

    private sealed record Row(
        string PersonalNr, string FirstName, string LastName, string Model,
        decimal Brutto, decimal Netto,
        decimal HourSaldo, decimal NachtSaldo, decimal FerienTage,
        decimal FerienGeld, decimal FeiertagTage, decimal Dreizehnter,
        string Iban);

    // ── Daten laden (geteilt von beiden Varianten) ─────────────────────────
    private async Task<(Models.PayrollPeriode periode, List<Row> rows)> LoadAsync(
        int companyProfileId, int year, int month)
    {
        var periode = await _db.PayrollPerioden
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId
                                   && p.Year == year && p.Month == month);
        if (periode is null)
            throw new InvalidOperationException($"Periode {year}-{month:D2} für Filiale {companyProfileId} nicht gefunden.");
        if (periode.Company is null)
            throw new InvalidOperationException("Filiale-Stammdaten fehlen.");

        var saldi = await _db.PayrollSaldos
            .Where(s => s.CompanyProfileId == companyProfileId
                     && s.PeriodYear == year && s.PeriodMonth == month)
            .ToListAsync();

        var empIds = saldi.Select(s => s.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        // Aktiver Vertrag pro MA in der Periode → Modell (FIX/FIX-M/MTP/UTP).
        var periodStartDt = new DateTime(year, month, 1);
        var periodEndDt   = new DateOnly(year, month, DateTime.DaysInMonth(year, month)).ToDateTime(TimeOnly.MinValue);
        var allEmployments = await _db.Employments
            .Where(e => empIds.Contains(e.EmployeeId)
                     && e.ContractStartDate <= periodEndDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= periodStartDt))
            .OrderByDescending(e => e.ContractStartDate)
            .ToListAsync();
        var contracts = allEmployments
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        // Hauptbank-IBAN pro MA, in der Periode gültig.
        var stichtag    = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var periodStart = new DateOnly(year, month, 1);
        var allBanks = await _db.EmployeeBankAccounts
            .Where(b => empIds.Contains(b.EmployeeId)
                     && b.ValidFrom <= stichtag
                     && (b.ValidTo == null || b.ValidTo >= periodStart))
            .OrderByDescending(b => b.IsHauptbank)
            .ThenByDescending(b => b.ValidFrom)
            .ToListAsync();
        var banks = allBanks
            .GroupBy(b => b.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        var rows = saldi.Select(s =>
        {
            employees.TryGetValue(s.EmployeeId, out var emp);
            contracts.TryGetValue(s.EmployeeId, out var ct);
            banks.TryGetValue(s.EmployeeId, out var bank);
            return new Row(
                PersonalNr:   emp?.EmployeeNumber ?? s.EmployeeId.ToString(),
                FirstName:    emp?.FirstName ?? "?",
                LastName:     emp?.LastName  ?? "?",
                Model:        (ct?.EmploymentModel ?? "").ToUpperInvariant(),
                Brutto:       s.GrossAmount,
                Netto:        s.NetAmount,
                HourSaldo:    s.HourSaldo,
                NachtSaldo:   s.NachtSaldo,
                FerienTage:   s.FerienTageSaldo,
                FerienGeld:   s.FerienGeldSaldo,
                FeiertagTage: s.FeiertagTageSaldo,
                Dreizehnter:  s.ThirteenthMonthAccumulated,
                Iban:         bank?.Iban ?? "—");
        })
        // Walter-Konvention: IMMER nach Vorname sortieren, Tie-Break Nachname.
        .OrderBy(r => r.FirstName, StringComparer.Create(CH, false))
        .ThenBy(r => r.LastName, StringComparer.Create(CH, false))
        .ToList();

        return (periode, rows);
    }

    private static string FmtIban(string iban) =>
        string.IsNullOrWhiteSpace(iban) || iban == "—"
            ? "—"
            : System.Text.RegularExpressions.Regex.Replace(iban, "(.{4})", "$1 ").Trim();

    // ── Gemeinsamer Seitenkopf ──────────────────────────────────────────────
    private void Kopf(QuestPDF.Infrastructure.IContainer headerContainer, string title)
    {
        headerContainer.Height(38).Layers(layers =>
        {
            layers.Layer().Image(BannerBytes).FitWidth();
            layers.PrimaryLayer().PaddingTop(10).AlignCenter()
                .Text(title).Bold().FontSize(12f).FontColor(Dark);
        });
    }

    private void Meta(ColumnDescriptor col, Models.PayrollPeriode periode, int count)
    {
        var parentName = periode.Company!.CompanyName ?? "";
        var company    = periode.Company.BranchName ?? "";
        var compAddr   = string.IsNullOrWhiteSpace(periode.Company.HouseNumber)
                            ? periode.Company.Street ?? ""
                            : $"{periode.Company.Street} {periode.Company.HouseNumber}".Trim();
        var compZip    = $"{periode.Company.ZipCode} {periode.Company.City}".Trim();
        col.Item().Column(p =>
        {
            if (!string.IsNullOrWhiteSpace(parentName)) p.Item().Text(parentName).Bold().FontSize(10f);
            p.Item().Text(company).FontSize(9.5f);
            if (!string.IsNullOrWhiteSpace(compAddr)) p.Item().Text(compAddr).FontSize(9f);
            if (!string.IsNullOrWhiteSpace(compZip))  p.Item().Text(compZip).FontSize(9f);
        });
        col.Item().PaddingTop(8).Column(c2 =>
        {
            c2.Item().Text($"Druckdatum: {DateTime.Today:dd.MM.yyyy}").FontSize(9f).FontColor(Muted);
            c2.Item().Text($"Periode: {periode.PeriodFrom:dd.MM.yyyy} – {periode.PeriodTo:dd.MM.yyyy}").FontSize(9f).FontColor(Muted);
            c2.Item().Text($"Status: {periode.Status}").FontSize(9f).FontColor(Muted);
            c2.Item().Text($"Anzahl Mitarbeiter: {count}").FontSize(9f).FontColor(Muted);
        });
    }

    // ════════════════════════════════════════════════════════════════════════
    //  Buchhaltungs-Liste — alle Saldi + Brutto/Netto + IBAN + Summen
    // ════════════════════════════════════════════════════════════════════════
    public async Task<byte[]> GenerateBuchhaltungAsync(int companyProfileId, int year, int month)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var (periode, rows) = await LoadAsync(companyProfileId, year, month);
        var title = $"Lohn-Saldi Buchhaltung {MonatsNamen[month - 1]} {year}";

        return Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.MarginTop(0.5f, Unit.Centimetre);
                page.MarginBottom(1.0f, Unit.Centimetre);
                page.MarginLeft(1.2f, Unit.Centimetre);
                page.MarginRight(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(8.5f).LineHeight(1.15f).FontColor(Dark));

                page.Header().Element(h => Kopf(h, title));

                page.Content().PaddingTop(8).Column(col =>
                {
                    Meta(col, periode, rows.Count);

                    col.Item().PaddingTop(12).Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(40);   // Pers-Nr
                            cd.RelativeColumn(1);     // Mitarbeiter
                            cd.ConstantColumn(40);   // Modell
                            cd.ConstantColumn(58);   // Brutto
                            cd.ConstantColumn(58);   // Netto
                            cd.ConstantColumn(44);   // Std-Saldo
                            cd.ConstantColumn(44);   // Nacht h
                            cd.ConstantColumn(46);   // Ferien Tage
                            cd.ConstantColumn(58);   // Ferien-Geld CHF
                            cd.ConstantColumn(50);   // Feiertag Tage
                            cd.ConstantColumn(56);   // 13. ML CHF
                            cd.ConstantColumn(130);  // IBAN
                        });

                        table.Header(h =>
                        {
                            void hdr(string txt, bool right = false)
                            {
                                var cell = h.Cell().PaddingVertical(4).PaddingHorizontal(2).BorderBottom(1).BorderColor(Dark);
                                (right ? cell.AlignRight().Text(txt) : cell.Text(txt)).Bold().FontSize(7.8f);
                            }
                            hdr("Pers-Nr"); hdr("Mitarbeiter"); hdr("Modell");
                            hdr("Brutto", true); hdr("Netto", true);
                            hdr("Std-Sld", true); hdr("Nacht h", true); hdr("Ferien Tg", true);
                            hdr("Ferien CHF", true); hdr("Feiert. Tg", true); hdr("13.ML CHF", true);
                            hdr("IBAN");
                        });

                        foreach (var r in rows)
                        {
                            var bg = (rows.IndexOf(r) % 2 == 1) ? "#F8F8F8" : "#FFFFFF";
                            void cell(Action<QuestPDF.Infrastructure.IContainer> content) =>
                                content(table.Cell().Background(bg).PaddingVertical(2.5f).PaddingHorizontal(2)
                                    .BorderBottom(0.3f).BorderColor("#CCCCCC"));
                            // UTP: kein 13.-ML-Saldo (monatlich ausbezahlt) → "—"
                            var dreizehntStr = r.Model == "FLEX" ? "—" : ChfOrDash(r.Dreizehnter);
                            cell(x => x.Text(r.PersonalNr).FontSize(8f));
                            cell(x => x.Text($"{r.FirstName} {r.LastName}".Trim()).FontSize(8f));
                            cell(x => x.Text(r.Model.Length > 0 ? r.Model : "—").FontSize(8f).FontColor(Muted));
                            cell(x => x.AlignRight().Text(Chf(r.Brutto)).FontSize(8f).FontFamily("Consolas"));
                            cell(x => x.AlignRight().Text(Chf(r.Netto)).FontSize(8f).FontFamily("Consolas"));
                            cell(x => x.AlignRight().Text(Std(r.HourSaldo)).FontSize(8f));
                            cell(x => x.AlignRight().Text(Std(r.NachtSaldo)).FontSize(8f));
                            cell(x => x.AlignRight().Text(Tage(r.FerienTage)).FontSize(8f));
                            cell(x => x.AlignRight().Text(ChfOrDash(r.FerienGeld)).FontSize(8f).FontFamily("Consolas"));
                            cell(x => x.AlignRight().Text(Tage(r.FeiertagTage)).FontSize(8f));
                            cell(x => x.AlignRight().Text(dreizehntStr).FontSize(8f).FontFamily("Consolas"));
                            cell(x => x.Text(FmtIban(r.Iban)).FontSize(7.2f).FontFamily("Consolas"));
                        }

                        // Summenzeile: Brutto/Netto/Ferien-Geld/13.ML aufsummiert.
                        decimal sBrutto = rows.Sum(r => r.Brutto);
                        decimal sNetto  = rows.Sum(r => r.Netto);
                        decimal sFerien = rows.Sum(r => r.FerienGeld);
                        decimal s13     = rows.Where(r => r.Model != "FLEX").Sum(r => r.Dreizehnter);
                        void sumLabel(int span, string txt) =>
                            table.Cell().ColumnSpan((uint)span).PaddingTop(4).PaddingHorizontal(2)
                                .BorderTop(1).BorderColor(Dark).Text(txt).Bold().FontSize(8.5f);
                        void sumNum(string txt, bool mono = true) {
                            var cell = table.Cell().PaddingTop(4).PaddingHorizontal(2).BorderTop(1).BorderColor(Dark).AlignRight();
                            var t = cell.Text(txt).Bold().FontSize(8.5f);
                            if (mono) t.FontFamily("Consolas");
                        }
                        void sumEmpty(int span = 1) =>
                            table.Cell().ColumnSpan((uint)span).PaddingTop(4).BorderTop(1).BorderColor(Dark).Text("");
                        sumLabel(3, "Total");        // Pers-Nr + Mitarbeiter + Modell
                        sumNum(Chf(sBrutto));        // Brutto
                        sumNum(Chf(sNetto));         // Netto
                        sumEmpty(3);                  // Std + Nacht + Ferien Tage
                        sumNum(Chf(sFerien));        // Ferien-Geld CHF
                        sumEmpty(1);                  // Feiertag Tage
                        sumNum(Chf(s13));            // 13. ML CHF
                        sumEmpty(1);                  // IBAN
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Lohn-Saldi Buchhaltung · generiert ").FontSize(7.5f).FontColor(Muted);
                    t.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm")).FontSize(7.5f).FontColor(Muted);
                    t.Span(" · ").FontSize(7.5f).FontColor(Muted);
                    t.CurrentPageNumber().FontSize(7.5f).FontColor(Muted);
                    t.Span(" / ").FontSize(7.5f).FontColor(Muted);
                    t.TotalPages().FontSize(7.5f).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }

    // ════════════════════════════════════════════════════════════════════════
    //  GF-Übersicht — kompakte Saldi pro MA, UTP ohne 13. ML
    // ════════════════════════════════════════════════════════════════════════
    public async Task<byte[]> GenerateGfAsync(int companyProfileId, int year, int month)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var (periode, rows) = await LoadAsync(companyProfileId, year, month);
        var title = $"Saldi-Übersicht GF {MonatsNamen[month - 1]} {year}";

        return Document.Create(c =>
        {
            c.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.MarginTop(0.5f, Unit.Centimetre);
                page.MarginBottom(1.0f, Unit.Centimetre);
                page.MarginLeft(1.5f, Unit.Centimetre);
                page.MarginRight(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9.5f).LineHeight(1.2f).FontColor(Dark));

                page.Header().Element(h => Kopf(h, title));

                page.Content().PaddingTop(8).Column(col =>
                {
                    Meta(col, periode, rows.Count);

                    col.Item().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(50);   // Pers-Nr
                            cd.RelativeColumn(1);     // Mitarbeiter
                            cd.ConstantColumn(55);   // Modell
                            cd.ConstantColumn(70);   // Std-Saldo
                            cd.ConstantColumn(70);   // Nacht-Saldo
                            cd.ConstantColumn(75);   // Ferien Tage
                            cd.ConstantColumn(90);   // Ferien-Geld CHF
                            cd.ConstantColumn(75);   // Feiertag Tage
                            cd.ConstantColumn(85);   // 13. ML CHF
                        });

                        table.Header(h =>
                        {
                            void hdr(string txt, bool right = false)
                            {
                                var cell = h.Cell().PaddingVertical(4).PaddingHorizontal(3).BorderBottom(1).BorderColor(Dark);
                                (right ? cell.AlignRight().Text(txt) : cell.Text(txt)).Bold().FontSize(8.5f);
                            }
                            hdr("Pers-Nr"); hdr("Mitarbeiter"); hdr("Modell");
                            hdr("Std-Saldo", true); hdr("Nacht-Sld", true); hdr("Ferien Tg", true);
                            hdr("Ferien-Geld", true); hdr("Feiertag Tg", true); hdr("13. ML CHF", true);
                        });

                        foreach (var r in rows)
                        {
                            var bg = (rows.IndexOf(r) % 2 == 1) ? "#F8F8F8" : "#FFFFFF";
                            void cell(Action<QuestPDF.Infrastructure.IContainer> content) =>
                                content(table.Cell().Background(bg).PaddingVertical(3).PaddingHorizontal(3)
                                    .BorderBottom(0.3f).BorderColor("#CCCCCC"));
                            var dreizehntStr = r.Model == "FLEX" ? "—" : ChfOrDash(r.Dreizehnter);
                            cell(x => x.Text(r.PersonalNr).FontSize(8.5f));
                            cell(x => x.Text($"{r.FirstName} {r.LastName}".Trim()).FontSize(8.5f));
                            cell(x => x.Text(r.Model.Length > 0 ? r.Model : "—").FontSize(8.5f).FontColor(Muted));
                            cell(x => x.AlignRight().Text(Std(r.HourSaldo)).FontSize(8.5f));
                            cell(x => x.AlignRight().Text(Std(r.NachtSaldo)).FontSize(8.5f));
                            cell(x => x.AlignRight().Text(Tage(r.FerienTage)).FontSize(8.5f));
                            cell(x => x.AlignRight().Text(ChfOrDash(r.FerienGeld)).FontSize(8.5f).FontFamily("Consolas"));
                            cell(x => x.AlignRight().Text(Tage(r.FeiertagTage)).FontSize(8.5f));
                            cell(x => x.AlignRight().Text(dreizehntStr).FontSize(8.5f).FontFamily("Consolas"));
                        }
                    });

                    col.Item().PaddingTop(8).Text(
                        "Hinweis: Bei UTP wird der 13. Monatslohn monatlich ausbezahlt — daher kein Rückstellungs-Saldo.")
                        .FontSize(8f).FontColor(Muted).Italic();
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Saldi-Übersicht GF · generiert ").FontSize(7.5f).FontColor(Muted);
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
