using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Mutterschafts-Timeline als A4-PDF (Walter-Vorgabe 10.06.2026).
///
/// Pro Schwangerschaft eine sortierte Liste aller Fristen mit Wochentag und
/// TT.MM.JJJJ — was ab welchem Datum erlaubt ist und was nicht. Dient als
/// Übergabe-Dokument für die MA und als Personalakte-Beleg, damit die
/// gesetzlichen Vorgaben dokumentiert sind.
///
/// On-demand generiert (kein Storage). Regelwerk wird live aus
/// pregnancy_rule gelesen — Änderungen am Regelwerk wirken sofort.
/// </summary>
public class PregnancyPdfService
{
    private readonly AppDbContext _db;
    public PregnancyPdfService(AppDbContext db) => _db = db;

    private const string Pink     = "#be185d";
    private const string PinkSoft = "#fce7f3";
    private const string Dark     = "#0f172a";
    private const string Muted    = "#475569";
    private const string RedSoft  = "#fee2e2";
    private const string RedTxt   = "#991b1b";
    private const string GreenSoft= "#dcfce7";
    private const string GreenTxt = "#166534";

    private static readonly System.Globalization.CultureInfo DeCh =
        System.Globalization.CultureInfo.GetCultureInfo("de-CH");

    public async Task<byte[]> GenerateAsync(int pregnancyId)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var preg = await _db.EmployeePregnancies
            .Include(p => p.Employee)
            .FirstOrDefaultAsync(p => p.Id == pregnancyId);
        if (preg is null) throw new InvalidOperationException("Schwangerschaft nicht gefunden.");

        var rules = await _db.PregnancyRules
            .Where(r => r.Aktiv)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Id)
            .ToListAsync();

        // Fristen berechnen + nach Datum sortieren (Walter 10.06.2026:
        // chronologisch aufsteigend — was als erstes greift, steht oben).
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var rows = rules.Select(r => CalcFrist(r, preg, heute))
                        .OrderBy(f => f.Datum).ThenBy(f => f.SortOrder)
                        .ToList();

        var schutzBeginn = PregnancyFristCalculator.SchwangerschaftsBeginn(preg);
        var schutzEnde   = PregnancyFristCalculator.KuendigungsschutzEnde(preg);

        var doc = Document.Create(c =>
        {
            c.Page(p =>
            {
                // Walter 10.06.2026: Mittelweg — 1 Seite garantiert, lesbar.
                p.Size(PageSizes.A4);
                p.Margin(16);
                p.PageColor(Colors.White);
                p.DefaultTextStyle(s => s.FontSize(8).FontColor(Dark));

                p.Header().PaddingBottom(3).Row(r =>
                {
                    r.RelativeItem().Text("Mutterschaft — Fristen-Übersicht")
                        .FontSize(12).Bold().FontColor(Pink);
                    r.AutoItem().AlignRight().AlignBottom().Text(
                        $"{preg.Employee?.FirstName} {preg.Employee?.LastName} · Pers.-Nr. {preg.Employee?.EmployeeNumber}")
                        .FontSize(9.5f).SemiBold().FontColor(Muted);
                });

                p.Content().Column(col =>
                {
                    // Eckdaten — eine breite Zeile mit allen Daten
                    col.Item().PaddingBottom(3).Background(PinkSoft).PaddingHorizontal(7).PaddingVertical(3).Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(8.5f));
                        t.Span("Meldedatum: ").SemiBold();
                        t.Span(FmtFullDate(preg.Meldedatum));
                        t.Span("    ·    ").FontColor("#f9a8d4");
                        t.Span("Beginn (ET − 280 T.): ").SemiBold();
                        t.Span(FmtFullDate(schutzBeginn));
                        t.Span("    ·    ").FontColor("#f9a8d4");
                        t.Span("ET: ").SemiBold();
                        t.Span(FmtFullDate(preg.ErrechneterTermin));
                        t.Span("    ·    ").FontColor("#f9a8d4");
                        t.Span("Geburt: ").SemiBold();
                        if (preg.Geburtsdatum.HasValue)
                            t.Span(FmtFullDate(preg.Geburtsdatum.Value));
                        else
                            t.Span("offen").FontColor(Muted);
                        t.Span("    ·    ").FontColor("#f9a8d4");
                        t.Span("Kündigungsschutz: ").SemiBold();
                        t.Span($"{FmtFullDate(schutzBeginn)} – {FmtFullDate(schutzEnde)}");
                    });
                    if (!string.IsNullOrWhiteSpace(preg.Bemerkung))
                    {
                        col.Item().PaddingBottom(2).PaddingHorizontal(7).Text(t =>
                        {
                            t.Span("Bemerkung: ").SemiBold().FontSize(8);
                            t.Span(preg.Bemerkung).FontSize(8);
                        });
                    }

                    // Tabelle
                    col.Item().Table(tbl =>
                    {
                        tbl.ColumnsDefinition(cd =>
                        {
                            cd.ConstantColumn(58);   // Ab
                            cd.ConstantColumn(58);   // Bis
                            cd.RelativeColumn(3);    // Bezeichnung + (sub)
                            cd.ConstantColumn(75);   // Lohn
                            cd.ConstantColumn(62);   // Status
                        });

                        tbl.Header(h =>
                        {
                            h.Cell().Element(HeaderCell).Text("Ab").SemiBold().FontSize(7.5f);
                            h.Cell().Element(HeaderCell).Text("Bis").SemiBold().FontSize(7.5f);
                            h.Cell().Element(HeaderCell).Text("Was greift").SemiBold().FontSize(7.5f);
                            h.Cell().Element(HeaderCell).Text("Lohn").SemiBold().FontSize(7.5f);
                            h.Cell().Element(HeaderCell).Text("Status").SemiBold().FontSize(7.5f);
                        });

                        foreach (var f in rows)
                        {
                            // AB-Datum
                            tbl.Cell().Element(BodyCell).Text(t =>
                            {
                                t.Span(WochentagDe(f.Datum).Substring(0, 2) + " ").FontSize(7).FontColor(Muted);
                                t.Span(f.Datum.ToString("dd.MM.yy")).FontSize(8.5f).Bold();
                            });

                            // BIS-Datum
                            tbl.Cell().Element(BodyCell).Text(t =>
                            {
                                if (f.DatumEnde.HasValue)
                                {
                                    t.Span(WochentagDe(f.DatumEnde.Value).Substring(0, 2) + " ").FontSize(7).FontColor(Muted);
                                    t.Span(f.DatumEnde.Value.ToString("dd.MM.yy")).FontSize(8.5f).Bold().FontColor(Muted);
                                }
                                else
                                {
                                    t.AlignCenter();
                                    t.Span("—").FontSize(8.5f).FontColor("#cbd5e1");
                                }
                            });

                            // Was greift
                            tbl.Cell().Element(BodyCell).Column(cc =>
                            {
                                if (f.IstArbeitsverbot)
                                {
                                    cc.Item().Row(rr =>
                                    {
                                        rr.AutoItem().Background(RedSoft).PaddingHorizontal(3).PaddingVertical(0)
                                            .Text("VERBOT").FontSize(6.5f).Bold().FontColor(RedTxt);
                                        rr.RelativeItem().PaddingLeft(3).Text(f.Bezeichnung).Bold().FontColor(RedTxt).FontSize(8.5f);
                                    });
                                }
                                else
                                {
                                    cc.Item().Text(f.Bezeichnung).SemiBold().FontSize(8.5f);
                                }
                                // Gesetz · Beschreibung · Staffel auf 1 Zeile
                                var sub = new List<string>();
                                if (!string.IsNullOrWhiteSpace(f.Gesetz))       sub.Add(f.Gesetz!);
                                if (!string.IsNullOrWhiteSpace(f.Beschreibung)) sub.Add(f.Beschreibung!);
                                if (!string.IsNullOrWhiteSpace(f.StaffelText))  sub.Add(f.StaffelText!);
                                if (sub.Count > 0)
                                    cc.Item().Text(string.Join(" · ", sub)).FontSize(6.5f).FontColor(Muted);
                            });

                            // Lohn
                            tbl.Cell().Element(BodyCell).Text(t =>
                            {
                                var parts = new List<string>();
                                if (f.LohnersatzPct.HasValue)    parts.Add($"{f.LohnersatzPct.Value:0.##}%");
                                if (f.MaxBetragProTag.HasValue)  parts.Add($"max. CHF {f.MaxBetragProTag.Value:0}");
                                if (parts.Count == 0)
                                {
                                    t.AlignCenter();
                                    t.Span("—").FontSize(7.5f).FontColor("#cbd5e1");
                                }
                                else
                                {
                                    t.Span(string.Join(" · ", parts)).FontSize(7.5f).SemiBold();
                                }
                            });

                            tbl.Cell().Element(BodyCell).AlignCenter().Element(StatusBox(f.Status, f.IstArbeitsverbot))
                              .AlignCenter().AlignMiddle()
                              .Text(StatusLabel(f.Status))
                              .FontSize(7).SemiBold();
                        }
                    });

                    col.Item().PaddingTop(3).Text(t =>
                    {
                        t.Span("Rot markierte Zeilen = Arbeitsverbot. ").SemiBold().FontColor(Muted).FontSize(6.5f);
                        t.Span("Basis: ArG Art. 35/35a, ArGV 1, OR Art. 336c. Ersetzt keine ärztliche Beurteilung — im Einzelfall entscheiden Arzt und MA.")
                            .FontColor(Muted).FontSize(6.5f);
                    });
                });

                p.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Erstellt am " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"))
                        .FontSize(6.5f).FontColor(Muted);
                });
            });
        });

        return doc.GeneratePdf();

        static IContainer HeaderCell(IContainer c) =>
            c.Background(PinkSoft).PaddingVertical(2.5f).PaddingHorizontal(4).BorderBottom(1).BorderColor("#f9a8d4");
        static IContainer BodyCell(IContainer c) =>
            c.PaddingVertical(2.5f).PaddingHorizontal(4).BorderBottom(1).BorderColor("#f1f5f9");
        static Func<IContainer,IContainer> StatusBox(string status, bool verbot) => c =>
        {
            string bg, fg;
            if (status == "aktiv" && verbot) { bg = RedSoft;   fg = RedTxt; }
            else if (status == "aktiv")      { bg = GreenSoft; fg = GreenTxt; }
            else if (status == "abgeschlossen") { bg = "#f1f5f9"; fg = "#64748b"; }
            else                              { bg = "#dbeafe"; fg = "#1e40af"; }
            return c.Background(bg).Padding(3);
        };
    }

    private static string StatusLabel(string status) => status switch
    {
        "aktiv"        => "aktiv",
        "abgeschlossen"=> "abgeschlossen",
        _              => "bevorstehend"
    };

    private static string FmtFullDate(DateOnly d) =>
        $"{WochentagDe(d)}, {d:dd.MM.yyyy}";

    private static string WochentagDe(DateOnly d) =>
        DeCh.DateTimeFormat.GetDayName(d.DayOfWeek);

    // Walter-Vorgabe 13.06.2026: Berechnung zentralisiert in
    // PregnancyFristCalculator — hier nur noch die PDF-spezifische
    // Übersetzung in den lokalen FristEntry-Record.
    private record FristEntry(string Bezeichnung, string? Beschreibung, string? Gesetz,
        DateOnly Datum, DateOnly? DatumEnde,
        decimal? LohnersatzPct, decimal? MaxBetragProTag, string? StaffelText,
        string Status, bool IstArbeitsverbot, int SortOrder);

    private static FristEntry CalcFrist(PregnancyRule r, EmployeePregnancy p, DateOnly today)
    {
        var f = PregnancyFristCalculator.Calculate(r, p, today);
        return new FristEntry(r.Bezeichnung, r.Beschreibung, r.Gesetz,
            f.Datum, f.DatumEnde,
            r.LohnersatzPct, r.MaxBetragProTag, r.StaffelText,
            f.Status, r.IstArbeitsverbot, r.SortOrder);
    }
}
