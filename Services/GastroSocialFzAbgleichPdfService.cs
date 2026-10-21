using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static HrSystem.Services.GastroSocialFzAbgleich;

namespace HrSystem.Services;

/// <summary>
/// Arbeits-PDF der offenen FZ-Abgleich-Pendenzen (Walter 21.10.2026) —
/// Fehlt / Abweichung / Nur OneCrew / Nicht zuordenbar / Angaben prüfen, mit Checkbox.
/// </summary>
public class GastroSocialFzAbgleichPdfService
{
    private const string Ink = "#1a1a1a";
    private const string Body = "#3f3f3f";
    private const string Muted = "#8b8b8b";
    private const string Line = "#d6d1c7";
    private const string Soft = "#f1efe9";

    public byte[] Generate(string filiale, Ergebnis e)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var zeitraum = e.Von != null && e.Bis != null
            ? $"{e.Von:dd.MM.yyyy} – {e.Bis:dd.MM.yyyy}"
            : "—";
        var abschnitte = new (string Titel, string Aufgabe, List<Treffer> Zeilen)[]
        {
            ("Fehlt in OneCrew",
                "Kind/Zulage im MA → Familie erfassen.",
                e.Fehlt),
            ("Abweichung — Beginn/Ende oder Ansatz",
                "Zulage prüfen (Gültig ab/bis, Art, Satz) und anpassen.",
                e.Abweichung),
            ("Nur in OneCrew",
                "Antrag fehlt, anderer Elternteil bezieht, oder Zulage beenden.",
                e.NurOneCrew),
            ("Nicht zuordenbar",
                "AHV beim MA nachtragen oder prüfen, ob die Person bei uns arbeitet.",
                e.NichtZuordenbar),
            ("Angaben prüfen",
                "AHV/Name/Geburtsdatum mit Ausweis klären — OneCrew oder GastroSocial korrigieren.",
                e.AngabenPruefen),
        };
        var offen = abschnitte.Sum(a => a.Zeilen.Count);

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.1f, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(9f).FontColor(Body));

                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text("FZ-Abgleich — Pendenzen").Bold().FontSize(16f).FontColor(Ink);
                        r.AutoItem().AlignBottom().Text(filiale).FontSize(11f).FontColor(Body);
                    });
                    col.Item().PaddingTop(2).Text(
                            $"Bescheinigung {zeitraum} · {e.Zeilen} Zeilen · {e.Kinder} Kinder · {offen} offen · Stand {DateTime.Now:dd.MM.yyyy HH:mm}")
                        .FontSize(8.5f).FontColor(Muted);
                    col.Item().PaddingTop(2).Text(
                            "Nur offene Punkte (Passt nicht). Checkbox zum Abhaken von Hand. Direktauszahlung und anteilige Teilmonate sind im Abgleich kein Alarm.")
                        .FontSize(8.5f).FontColor(Body);
                    col.Item().PaddingTop(6);
                });

                page.Footer().AlignCenter().DefaultTextStyle(t => t.FontSize(8f).FontColor(Muted)).Text(t =>
                {
                    t.Span("Seite ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                    t.Span("  ·  vertraulich");
                });

                page.Content().Column(col =>
                {
                    if (offen == 0)
                    {
                        col.Item().PaddingTop(20).Text("Keine offenen Pendenzen — alles passt.")
                            .FontSize(12f).FontColor("#166534");
                        return;
                    }

                    foreach (var (titel, aufgabe, zeilen) in abschnitte)
                    {
                        if (zeilen.Count == 0) continue;
                        col.Item().PaddingTop(10).Text($"{titel} ({zeilen.Count})")
                            .Bold().FontSize(11f).FontColor(Ink);
                        col.Item().PaddingBottom(4).Text(aufgabe).FontSize(8f).FontColor(Muted);

                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(18);   // ☐
                                c.RelativeColumn(2.2f); // Bezüger
                                c.RelativeColumn(2.0f); // Kind
                                c.RelativeColumn(2.4f); // GastroSocial
                                c.RelativeColumn(2.4f); // OneCrew
                                c.RelativeColumn(2.6f); // Hinweis
                            });

                            t.Header(h =>
                            {
                                void Th(string s)
                                {
                                    h.Cell().Background(Soft).BorderBottom(1f).BorderColor(Ink)
                                        .PaddingVertical(3).PaddingHorizontal(3)
                                        .Text(s).Bold().FontSize(8f).FontColor(Ink);
                                }
                                Th("✓");
                                Th("Bezüger / Pers.-Nr.");
                                Th("Kind / Geburt");
                                Th("GastroSocial");
                                Th("OneCrew");
                                Th("Hinweis");
                            });

                            foreach (var z in zeilen)
                            {
                                void Td(string? text, bool bold = false)
                                {
                                    var cell = t.Cell().BorderBottom(0.5f).BorderColor(Line)
                                        .PaddingVertical(3).PaddingHorizontal(3);
                                    var tx = cell.Text(string.IsNullOrWhiteSpace(text) ? "—" : text);
                                    tx.FontSize(8f);
                                    if (bold) tx.SemiBold();
                                }

                                t.Cell().BorderBottom(0.5f).BorderColor(Line)
                                    .PaddingVertical(3).PaddingHorizontal(3)
                                    .Text("☐").FontSize(11f);

                                var bezTeile = new List<string> { z.OneCrewName ?? z.Bezueger ?? "—" };
                                if (!string.IsNullOrWhiteSpace(z.Personalnummer)) bezTeile.Add(z.Personalnummer!);
                                if (!string.IsNullOrWhiteSpace(z.BezuegerAhv)) bezTeile.Add(z.BezuegerAhv!);
                                Td(string.Join(" · ", bezTeile), true);

                                var kindTeile = new List<string> { z.Kind ?? "—" };
                                if (z.KindGeburt != null) kindTeile.Add($"{z.KindGeburt:dd.MM.yyyy}");
                                if (!string.IsNullOrWhiteSpace(z.KindAhv)) kindTeile.Add(z.KindAhv!);
                                Td(string.Join(" · ", kindTeile));

                                Td(z.GastroSocial);
                                Td(z.OneCrew);

                                var hinweis = z.Hinweis;
                                if (z.Angaben is { Count: > 0 })
                                    hinweis = string.Join(" · ", z.Angaben);
                                Td(hinweis);
                            }
                        });
                    }
                });
            });
        }).GeneratePdf();
    }
}
