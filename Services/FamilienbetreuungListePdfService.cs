using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Kontroll-Liste «Familienbetreuung» für die GF (Walter-Vorgabe 12.10.2026).
/// Links der Stand aus easy@work, rechts das Soll aus den Kindern im Haushalt;
/// abweichende easy@work-Werte rot — die GF korrigiert sie in easy@work.
/// Bewusst keine Kindernamen/Geburtsdaten, nur «jüngstes Kind wird 15 am».
/// </summary>
public class FamilienbetreuungListePdfService
{
    public record Zeile(
        string? EmpNr, string? Vorname, string? Nachname,
        bool IstJa, DateOnly? IstVon, DateOnly? IstBis,
        bool? SollJa, DateOnly? SollVon, DateOnly? SollBis,
        bool AbwJa, bool AbwVon, bool AbwBis,
        string? Hinweis);

    public record Input(string FilialeTitel, bool NurAbweichungen, int AnzahlMa, int AnzahlAbweichungen, List<Zeile> Zeilen);

    private const string Ink = "#1a1a1a";
    private const string Body = "#3f3f3f";
    private const string Muted = "#8b8b8b";
    private const string Line = "#d6d1c7";
    private const string Soft = "#f1efe9";
    private const string RotHg = "#ffc7ce";
    private const string RotTx = "#9c0006";

    public byte[] Generate(Input d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        static string Dt(DateOnly? x) => x?.ToString("dd.MM.yyyy") ?? "";

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(10f).FontColor(Body));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("Familienbetreuung – Kontrolle").Bold().FontSize(18f).FontColor(Ink);
                        r.AutoItem().AlignBottom().Text(d.FilialeTitel).FontSize(12f).FontColor(Body);
                    });
                    col.Item().PaddingTop(2).Text(
                        $"Stand {DateTime.Now:dd.MM.yyyy} · {d.AnzahlMa} Mitarbeitende, davon {d.AnzahlAbweichungen} mit Abweichung"
                        + (d.NurAbweichungen ? " · gedruckt: nur Abweichungen" : ""))
                        .FontSize(9f).FontColor(Muted);
                    col.Item().PaddingTop(3).Text(
                        "Soll: «Ja» bei Kindern unter 15 Jahren im Haushalt — ab Eintritt bzw. Geburt des Kindes bis zum Tag vor dem 15. Geburtstag des jüngsten Kindes (ArG Art. 36). "
                        + "Rot = Wert in easy@work weicht vom Soll ab → bitte in easy@work im Feld «Familienbetreuung» korrigieren.")
                        .FontSize(9.5f).FontColor(Body);
                    col.Item().PaddingTop(6);
                });

                page.Content().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1.05f); // MA-Nr
                        c.RelativeColumn(2.0f);  // Vorname
                        c.RelativeColumn(2.0f);  // Nachname
                        c.RelativeColumn(1.1f);  // easy Ja/Nein
                        c.RelativeColumn(1.35f); // easy von
                        c.RelativeColumn(1.35f); // easy bis
                        c.RelativeColumn(1.1f);  // Soll Ja/Nein
                        c.RelativeColumn(1.35f); // Soll von
                        c.RelativeColumn(1.35f); // Soll bis
                        c.RelativeColumn(4.6f);  // Hinweis — eine Zeile
                    });

                    t.Header(h =>
                    {
                        void Gruppe(string s, int span, bool soll = false) => h.Cell().ColumnSpan((uint)span)
                            .PaddingTop(2).PaddingHorizontal(4)
                            .Text(s.Length == 0 ? " " : s).Bold().FontSize(9f).FontColor(soll ? Muted : Ink);
                        Gruppe("", 3);
                        Gruppe("easy@work", 3);
                        Gruppe("Soll", 3, soll: true);
                        Gruppe("", 1);

                        void Th(string s, bool soll = false) => h.Cell().Background(Soft)
                            .BorderBottom(1f).BorderColor(Ink)
                            .PaddingVertical(3).PaddingHorizontal(3)
                            .Text(s).Bold().FontSize(9.5f).FontColor(soll ? Muted : Ink);
                        Th("MA-Nr.");
                        Th("Vorname");
                        Th("Nachname");
                        Th("Ja/Nein");
                        Th("von");
                        Th("bis");
                        Th("Ja/Nein", true);
                        Th("von", true);
                        Th("bis", true);
                        Th("Hinweis");
                    });

                    foreach (var z in d.Zeilen)
                    {
                        void Td(string? s, bool rot = false, bool soll = false)
                        {
                            var cell = t.Cell().BorderBottom(0.5f).BorderColor(Line);
                            if (rot) cell = cell.Background(RotHg);
                            var txt = cell.PaddingVertical(2).PaddingHorizontal(3)
                                .Text(string.IsNullOrEmpty(s) ? " " : s).FontSize(10f)
                                .FontColor(rot ? RotTx : soll ? Muted : Body);
                            if (rot) txt.Bold();
                        }
                        Td(z.EmpNr);
                        Td(z.Vorname);
                        Td(z.Nachname);
                        Td(z.IstJa ? "Ja" : "Nein", z.AbwJa);
                        Td(z.IstJa ? Dt(z.IstVon) : "", z.AbwVon);
                        Td(z.IstJa ? (z.IstVon.HasValue && !z.IstBis.HasValue ? "offen" : Dt(z.IstBis)) : "", z.AbwBis);
                        Td(z.SollJa == null ? "?" : z.SollJa.Value ? "Ja" : "Nein", soll: true);
                        Td(z.SollJa == true ? Dt(z.SollVon) : "", soll: true);
                        Td(z.SollJa == true ? Dt(z.SollBis) : "", soll: true);
                        Td(z.Hinweis);
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
