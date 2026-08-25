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
                // Quer (Walter 25.08.2026): Platz für die Ankreuz-Beziehung
                // und breite Schreiblinien; MA-Nr dafür weggelassen.
                page.Size(PageSizes.A4.Landscape());
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
                        // Walter 25.08.2026 v2: Vorname/Name schmaler → Notfall-
                        // Name rückt nach links; Beziehung deutlich breiter,
                        // damit die «Andere»-Linie Platz für Schwester/Bruder/
                        // Mutter … hat.
                        c.RelativeColumn(2.0f); // Vorname
                        c.RelativeColumn(2.0f); // Name
                        c.RelativeColumn(3.2f); // Notfall Name
                        c.RelativeColumn(5.6f); // Beziehung (Ankreuz-Reihe + Linie)
                        c.RelativeColumn(2.8f); // Telefon
                    });

                    // Kopfzeile (wiederholt auf jeder Seite)
                    t.Header(h =>
                    {
                        void Th(string s) => h.Cell().Background(Soft)
                            .BorderBottom(1f).BorderColor(Ink)
                            .PaddingVertical(4).PaddingHorizontal(4)
                            .Text(s).Bold().FontSize(8.5f).FontColor(Ink);
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
                        Td(z.Vorname);
                        Td(z.Name);
                        Td(z.NotfallName, fett: true);
                        BeziehungCell(t, z.NotfallBeziehung);
                        Td(z.NotfallTelefon, fett: true);
                    }
                });

                static void BeziehungCell(TableDescriptor t, string? bez)
                {
                    // Ankreuz-Reihe (Walter 25.08.2026): ☐ Partner ☐ Kind
                    // ☐ Andere ______ — bei erfasster Beziehung wird das
                    // passende Kästchen mit X markiert (Ehepartner → Partner),
                    // sonst bleibt alles leer zum Handankreuzen.
                    var b = (bez ?? "").Trim().ToLowerInvariant();
                    bool isPartner = b.Contains("partner") || b.Contains("ehe");
                    bool isKind    = !isPartner && b.Contains("kind");
                    bool isAndere  = b.Length > 0 && !isPartner && !isKind;

                    t.Cell().BorderBottom(0.55f).BorderColor(Line)
                        .PaddingVertical(3).PaddingHorizontal(4)
                        .MinHeight(20).AlignBottom().PaddingBottom(2)
                        .Row(r =>
                        {
                            void Chk(bool on, string label)
                            {
                                r.AutoItem().AlignMiddle().Width(9).Height(9)
                                    .Border(0.8f).BorderColor(Body)
                                    .AlignCenter().AlignMiddle()
                                    .Text(on ? "X" : " ").Bold().FontSize(7f).FontColor(Ink);
                                r.ConstantItem(3);
                                r.AutoItem().AlignMiddle().Text(label).FontSize(7.5f).FontColor(Body);
                                r.ConstantItem(7);
                            }
                            Chk(isPartner, "Partner");
                            Chk(isKind, "Kind");
                            Chk(isAndere, "Andere");
                            // Schreiblinie für «Andere» — bei erfasstem
                            // Freitext steht er direkt auf der Linie.
                            r.RelativeItem().AlignBottom()
                                .BorderBottom(0.55f).BorderColor(Line)
                                .Text(isAndere ? bez! : " ").FontSize(7.5f).FontColor(Body);
                        });
                }

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
