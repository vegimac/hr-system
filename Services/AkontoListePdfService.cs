using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Akonto-Zahlungsliste als A4-PDF (Walter-Vorgabe 18.05.2026).
///
/// Pro Filiale + Periode: alle ausbezahlten Akonto-Datensätze in tabellarischer
/// Form — Personal-Nr, Name, IBAN, Bank, Netto-Akonto, HR-bestätigt-Zeitstempel.
/// Summe am Ende. Zweck:
///   • Begleitliste zum DTA-XML, das an die Bank geht (Papier-Backup).
///   • Beleg für die Buchhaltung („am 15. wurden CHF X als Vorauszahlung gebucht").
///   • Audit-Trail: pro MA sichtbar wer wann HR-bestätigt hat.
///
/// On-demand generiert (kein Storage), Re-Download jederzeit. Logik analog
/// zum DTA-Endpoint in AkontoWorkflowController.
/// </summary>
public class AkontoListePdfService
{
    private readonly AppDbContext _db;
    public AkontoListePdfService(AppDbContext db) => _db = db;

    private const string Yellow = "#FFC72C";
    private const string Dark   = "#000000";
    private const string Muted  = "#404040";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    private static string CHF(decimal v) =>
        v.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("de-CH"));

    private static readonly string[] MonatsNamen =
        { "Januar", "Februar", "März", "April", "Mai", "Juni",
          "Juli", "August", "September", "Oktober", "November", "Dezember" };

    public async Task<byte[]> GenerateAsync(int companyProfileId, int year, int month)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var periode = await _db.PayrollPerioden
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId
                                   && p.Year == year && p.Month == month);
        if (periode is null)
            throw new InvalidOperationException($"Periode {year}-{month:D2} für Filiale {companyProfileId} nicht gefunden.");
        if (periode.Company is null)
            throw new InvalidOperationException("Filiale-Stammdaten fehlen.");

        // Akonto-Zahlungen + Employee-Lookup für Name/Personal-Nr.
        var zahlungen = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == companyProfileId
                     && z.PeriodYear == year && z.PeriodMonth == month
                     && (z.Status == "AUSBEZAHLT"
                      || z.Status == "HR_BESTAETIGT"
                      || z.Status == "FREIGEGEBEN_GF"))
            .ToListAsync();

        var empIds = zahlungen.Select(z => z.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id);

        // Bankverbindung pro MA — in Lohnperiode gültig, Hauptbank zuerst.
        // (Erwartung: jede Bank hat valid_from spätestens ab Eintritt des
        // MA gesetzt. Falls historisch fehlt → einmaliger SQL-Backfill auf
        // 2024-01-01, siehe migrations-archive/fix_hauptbank_backfill.sql.)
        var stichtag    = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var periodStart = new DateOnly(year, month, 1);
        var stichtagDt  = stichtag.ToDateTime(TimeOnly.MinValue);
        var periodStartDt = periodStart.ToDateTime(TimeOnly.MinValue);
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

        // Aktiver Vertrag pro MA zum Stichtag — für die Gegenkontrolle.
        // FIX/FIX-M zeigen den Monatslohn; UTP/MTP zeigen die bis zum
        // Akonto-Stichtag gestempelten Stunden (= Basis der Akonto-Berechnung).
        var allEmployments = await _db.Employments
            .Where(e => empIds.Contains(e.EmployeeId)
                     && e.ContractStartDate <= stichtagDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= periodStartDt))
            .OrderByDescending(e => e.ContractStartDate)
            .ToListAsync();
        var contracts = allEmployments
            .GroupBy(e => e.EmployeeId)
            .ToDictionary(g => g.Key, g => g.First());

        // Akonto-Stichtag (= PayoutDate) — der Tag, bis zu dem die Stunden
        // für die Akonto-Berechnung aufsummiert werden. Alle Zahlungen einer
        // Periode haben dasselbe PayoutDate (kommt aus AkontoTermin).
        var akontoStichtag = zahlungen
            .Select(z => z.PayoutDate)
            .DefaultIfEmpty(stichtag)
            .Max();

        // Gestempelte Stunden pro UTP/MTP-MA bis Akonto-Stichtag.
        var utpMtpEmpIds = contracts
            .Where(kv => kv.Value.EmploymentModel == "FLEX"
                      || kv.Value.EmploymentModel == "MTP")
            .Select(kv => kv.Key)
            .ToList();
        var stundenByEmp = utpMtpEmpIds.Count == 0
            ? new Dictionary<int, decimal>()
            : (await _db.EmployeeTimeEntries
                .Where(t => utpMtpEmpIds.Contains(t.EmployeeId)
                         && t.EntryDate >= periodStart
                         && t.EntryDate <= akontoStichtag)
                .ToListAsync())
                .GroupBy(t => t.EmployeeId)
                .ToDictionary(g => g.Key, g => TimeEntryHours.SumAbsolute(g));

        // Rows zusammenbauen — Sortierung nach Nachname, Vorname (Treuhänder-Standard).
        var rows = zahlungen
            .Select(z =>
            {
                employees.TryGetValue(z.EmployeeId, out var emp);
                banks.TryGetValue(z.EmployeeId, out var bank);
                contracts.TryGetValue(z.EmployeeId, out var ct);

                // Gegenkontrolle in drei Spalten (Walter-Vorgabe 19.05.2026):
                //   • Vertrag (Modell)
                //   • Berechnung (links): UTP/MTP „101.3h × 21.66 + Pott 146.53"
                //                          FIX/FIX-M „6'687.50 × 80%" oder nur „4'880"
                //   • Brutto (rechts, rechtsbündig): das Ergebnis als reine Zahl
                string model = (ct?.EmploymentModel ?? "").ToUpperInvariant();
                string vertragLabel = model.Length > 0 ? model : "—";
                string berechnungLabel = "";
                string bruttoLabel     = "";
                var chCulture = System.Globalization.CultureInfo.GetCultureInfo("de-CH");
                if (model == "FIX" || model == "FIX-M")
                {
                    var lohn    = ct?.MonthlySalary    ?? 0m;
                    var lohnFte = ct?.MonthlySalaryFte ?? lohn;
                    var pensum  = ct?.EmploymentPercentage ?? 0m;
                    if (lohnFte > 0 && lohn > 0 && pensum > 0 && pensum < 100)
                    {
                        berechnungLabel = $"{lohnFte.ToString("N0", chCulture)} × {pensum.ToString("0.#", chCulture)}%";
                    }
                    else if (lohn > 0)
                    {
                        // 100%-Pensum oder Pensum unbekannt: zeige nur den FTE-Lohn
                        berechnungLabel = $"100% Festlohn";
                    }
                    bruttoLabel = lohn > 0
                        ? lohn.ToString("N0", chCulture)
                        : "—";
                }
                else if (model == "MTP" || model == "FLEX")
                {
                    stundenByEmp.TryGetValue(z.EmployeeId, out var hWorked);
                    var hourly = ct?.HourlyRate ?? 0m;
                    var ferien = z.FeriengeldAnteil;
                    if (hourly > 0)
                    {
                        var stdBrutto = hWorked * hourly;
                        var total     = stdBrutto + ferien;
                        berechnungLabel = ferien > 0
                            ? $"{hWorked.ToString("0.#", chCulture)}h × {hourly.ToString("0.00", chCulture)} + Pott {ferien.ToString("0.00", chCulture)}"
                            : $"{hWorked.ToString("0.#", chCulture)}h × {hourly.ToString("0.00", chCulture)}";
                        bruttoLabel = total.ToString("N2", chCulture);
                    }
                    else
                    {
                        berechnungLabel = $"{hWorked.ToString("0.#", chCulture)}h";
                        bruttoLabel = "—";
                    }
                }

                return new
                {
                    z.Id,
                    PersonalNr   = emp?.EmployeeNumber ?? z.EmployeeId.ToString(),
                    FirstName    = emp?.FirstName ?? "?",
                    LastName     = emp?.LastName  ?? "?",
                    Iban         = bank?.Iban     ?? "—",
                    Vertrag      = vertragLabel,
                    Berechnung   = berechnungLabel,
                    Brutto       = bruttoLabel,
                    NettoAkonto  = z.NettoAkonto,
                    Status       = z.Status,
                    HrBestaetigt = z.UpdatedAt,
                };
            })
            // Walter-Konvention: IMMER nach Vorname sortieren, Tie-Break Nachname.
            .OrderBy(r => r.FirstName, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("de-CH"), false))
            .ThenBy(r => r.LastName, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("de-CH"), false))
            .ToList();

        var summe = rows.Sum(r => r.NettoAkonto);
        var periodLabel = $"{MonatsNamen[month - 1]} {year}";
        var parentName  = periode.Company.CompanyName ?? "";
        var company     = periode.Company.BranchName ?? "";
        var compAddr    = string.IsNullOrWhiteSpace(periode.Company.HouseNumber)
                              ? periode.Company.Street ?? ""
                              : $"{periode.Company.Street} {periode.Company.HouseNumber}".Trim();
        var compZip     = $"{periode.Company.ZipCode} {periode.Company.City}".Trim();
        var printDate   = DateTime.Today.ToString("dd.MM.yyyy");
        var statusLabel = periode.AkontoStatus switch
        {
            "AUSBEZAHLT"      => "ausbezahlt am " + (periode.AkontoAusbezahltAt?.ToLocalTime().ToString("dd.MM.yyyy, HH:mm") ?? "?"),
            "HR_FREIGEGEBEN"  => "HR-freigegeben — wartet auf DTA",
            "BEI_HR"          => "bei HR zur Kontrolle",
            _                  => periode.AkontoStatus,
        };

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

                // Header: Banner mit Titel
                page.Header().Height(38).Layers(layers =>
                {
                    layers.Layer().Image(BannerBytes).FitWidth();
                    layers.PrimaryLayer()
                        .PaddingTop(10)
                        .AlignCenter()
                        .Text($"Akonto-Zahlungsliste {periodLabel}")
                        .Bold().FontSize(12f).FontColor(Dark);
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    // Filiale links
                    col.Item().Column(p =>
                    {
                        if (!string.IsNullOrWhiteSpace(parentName))
                            p.Item().Text(parentName).Bold().FontSize(10f);
                        p.Item().Text(company).FontSize(9.5f);
                        if (!string.IsNullOrWhiteSpace(compAddr)) p.Item().Text(compAddr).FontSize(9f);
                        if (!string.IsNullOrWhiteSpace(compZip))  p.Item().Text(compZip).FontSize(9f);
                    });

                    // Metadaten oben rechts
                    col.Item().PaddingTop(10).Row(r =>
                    {
                        r.RelativeItem().Column(c2 =>
                        {
                            c2.Item().Text($"Druckdatum: {printDate}").FontSize(9f).FontColor(Muted);
                            c2.Item().Text($"Periode: {periode.PeriodFrom:dd.MM.yyyy} – {periode.PeriodTo:dd.MM.yyyy}").FontSize(9f).FontColor(Muted);
                            c2.Item().Text($"Status: {statusLabel}").FontSize(9f).FontColor(Muted);
                            c2.Item().Text($"Anzahl Mitarbeiter: {rows.Count}").FontSize(9f).FontColor(Muted);
                        });
                    });

                    // Tabelle — Walter-Vorgabe 19.05.2026: Stunden-Rechnung
                    // und Brutto-Ergebnis in zwei getrennten Spalten (links
                    // Berechnung, rechts der numerische Wert), damit das Auge
                    // beim Querlesen die Beträge sofort vergleichen kann.
                    col.Item().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(50);    // Pers.-Nr
                            cd.RelativeColumn(1);     // Mitarbeiter
                            cd.ConstantColumn(50);    // Vertrag (Modell)
                            cd.ConstantColumn(140);   // Berechnung (links: „101.3h × 21.66" / „6'687.50 × 80%")
                            cd.ConstantColumn(60);    // Brutto CHF (rechtsbündig)
                            cd.ConstantColumn(155);   // IBAN
                            cd.ConstantColumn(70);    // Netto CHF (rechtsbündig)
                            cd.ConstantColumn(85);    // HR-bestätigt
                        });

                        // Header
                        table.Header(h =>
                        {
                            void hdr(string txt, bool right = false)
                            {
                                var cell = h.Cell().PaddingVertical(4).PaddingHorizontal(3)
                                    .BorderBottom(1).BorderColor(Dark);
                                var txtEl = right ? cell.AlignRight().Text(txt) : cell.Text(txt);
                                txtEl.Bold().FontSize(8.5f);
                            }
                            hdr("Pers-Nr");
                            hdr("Mitarbeiter");
                            hdr("Vertrag");
                            hdr("Berechnung");
                            hdr("Brutto", right: true);
                            hdr("IBAN");
                            hdr("Netto CHF", right: true);
                            hdr("HR-bestätigt");
                        });

                        // Body
                        foreach (var r in rows)
                        {
                            var bg = (rows.IndexOf(r) % 2 == 1) ? "#F8F8F8" : "#FFFFFF";
                            var name = $"{r.FirstName} {r.LastName}".Trim();
                            var iban = string.IsNullOrWhiteSpace(r.Iban) || r.Iban == "—"
                                          ? "—"
                                          : System.Text.RegularExpressions.Regex.Replace(r.Iban, "(.{4})", "$1 ").Trim();
                            var hrTs = r.HrBestaetigt.ToLocalTime().ToString("dd.MM.yy HH:mm");

                            void cell(Action<QuestPDF.Infrastructure.IContainer> content)
                            {
                                content(table.Cell()
                                    .Background(bg)
                                    .PaddingVertical(3)
                                    .PaddingHorizontal(3)
                                    .BorderBottom(0.3f).BorderColor("#CCCCCC"));
                            }
                            cell(c2 => c2.Text(r.PersonalNr).FontSize(8.5f));
                            cell(c2 => c2.Text(name).FontSize(8.5f));
                            cell(c2 => c2.Text(r.Vertrag).FontSize(8.5f).FontColor(Muted));
                            cell(c2 => c2.Text(r.Berechnung).FontSize(8.5f).FontColor(Muted));
                            cell(c2 => c2.AlignRight().Text(r.Brutto).FontSize(8.5f));
                            cell(c2 => c2.Text(iban).FontSize(7.8f).FontFamily("Consolas"));
                            cell(c2 => c2.AlignRight().Text(CHF(r.NettoAkonto)).FontSize(8.5f).FontFamily("Consolas"));
                            cell(c2 => c2.Text(hrTs).FontSize(7.5f).FontColor(Muted));
                        }

                        // Summen-Zeile — spannt über Pers-Nr / Name / Vertrag /
                        // Berechnung / Brutto / IBAN (6 Spalten), dann Total CHF + leer.
                        table.Cell().ColumnSpan(6)
                            .PaddingTop(4).PaddingHorizontal(3)
                            .BorderTop(1).BorderColor(Dark)
                            .Text("Total Akonto-Auszahlung").Bold().FontSize(9f);
                        table.Cell()
                            .PaddingTop(4).PaddingHorizontal(3)
                            .BorderTop(1).BorderColor(Dark)
                            .AlignRight()
                            .Text(CHF(summe)).Bold().FontSize(9.5f).FontFamily("Consolas");
                        table.Cell()
                            .PaddingTop(4).PaddingHorizontal(3)
                            .BorderTop(1).BorderColor(Dark)
                            .Text("");
                    });
                });

                // Footer
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Akonto-Zahlungsliste · generiert ").FontSize(7.5f).FontColor(Muted);
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
