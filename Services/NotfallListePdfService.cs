using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Notfallkontakte-Liste pro Filiale (Walter-Vorgabe 25.08.2026).
/// A4 hoch, eine Zeile pro aktivem MA: MA-Nr · Vorname · Name · Notfall-Name ·
/// Beziehung · Telefon. Fehlt der Notfallkontakt, bleiben die drei Spalten
/// als Schreiblinien leer — die Liste hängt ausgedruckt im Restaurant und
/// wird dort von Hand nachgeführt.
/// </summary>
public class NotfallListePdfService
{
    public record NotfallListeZeile(
        string? EmpNr,
        string? Vorname,
        string? Name,
        string? NotfallName,
        string? NotfallBeziehung,
        string? NotfallTelefon);

    public record NotfallListeInput(
        string FilialeTitel,
        List<NotfallListeZeile> Zeilen);

    private static readonly string Ink   = "#1a1a1a";
    private static readonly string Body  = "#3f3f3f";
    private static readonly string Muted = "#8b8b8b";
    private static readonly string Line  = "#b9b4aa";
    private static readonly string Soft  = "#f1efe9";

    public byte[] Generate(NotfallListeInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9f).FontColor(Body));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("Notfallkontakte").Bold().FontSize(16f).FontColor(Ink);
                        r.AutoItem().AlignBottom().Text(d.FilialeTitel).FontSize(10.5f).FontColor(Body);
                    });
                    col.Item().PaddingTop(2).Text(
                        $"Stand {DateTime.Now:dd.MM.yyyy} — bitte Änderungen von Hand nachtragen und im HR-System erfassen.")
                        .FontSize(7.5f).FontColor(Muted);
                    col.Item().PaddingTop(6);
                });

                page.Content().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.ConstantColumn(52);   // MA-Nr
                        c.RelativeColumn(3);    // Vorname
                        c.RelativeColumn(3);    // Name
                        c.RelativeColumn(4);    // Notfall Name
                        c.RelativeColumn(3);    // Beziehung
                        c.RelativeColumn(3.4f); // Telefon
                    });

                    // Kopfzeile (wiederholt auf jeder Seite)
                    t.Header(h =>
                    {
                        void Th(string s) => h.Cell().Background(Soft)
                            .BorderBottom(1f).BorderColor(Ink)
                            .PaddingVertical(4).PaddingHorizontal(4)
                            .Text(s).Bold().FontSize(8.5f).FontColor(Ink);
                        Th("MA-Nr");
                        Th("Vorname");
                        Th("Name");
                        Th("Notfall — Name");
                        Th("Beziehung");
                        Th("Telefon");
                    });

                    foreach (var z in d.Zeilen)
                    {
                        // Zellen mit Unterlinie; leere Notfall-Felder = Schreib-
                        // linie (min. 20pt hoch, damit Handschrift Platz hat).
                        void Td(string? s, bool fett = false)
                        {
                            var cell = t.Cell().BorderBottom(0.55f).BorderColor(Line)
                                .PaddingVertical(3).PaddingHorizontal(4)
                                .MinHeight(20).AlignBottom();
                            var txt = cell.Text(string.IsNullOrWhiteSpace(s) ? " " : s)
                                .FontSize(9f).FontColor(Body);
                            if (fett) txt.Bold();
                        }
                        Td(z.EmpNr);
                        Td(z.Vorname);
                        Td(z.Name);
                        Td(z.NotfallName, fett: true);
                        Td(z.NotfallBeziehung);
                        Td(z.NotfallTelefon, fett: true);
                    }
                });

                page.Footer().AlignRight().Text(txt =>
                {
                    txt.DefaultTextStyle(s => s.FontSize(7.5f).FontColor(Muted));
                    txt.Span("Seite ");
                    txt.CurrentPageNumber();
                    txt.Span(" / ");
                    txt.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
