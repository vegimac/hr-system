using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Verwarnungs-Formular als PDF (Walter-Vorgabe 15.07.2026) — nach der
/// Papier-Vorlage «Verwarnungen.doc»: Titel VERWARNUNG, Für/Datum, die 16
/// Ankreuz-Gründe (gewählte angekreuzt), Bemerkungszeilen, Unterschrifts-
/// zeilen Mitarbeiter + Schichtführer. Briefkopf = gelbes Banner wie überall.
/// Ablauf: erfassen → Formular drucken → unterschreiben → Scan hinterlegen.
/// </summary>
public record VerwarnungFormularInput(
    string CompanyName,
    string RestaurantName,
    string MaName,
    string? EmployeeNumber,
    DateTime Datum,
    string StufeLabel,          // «1. Verwarnung» | «2. Verwarnung» | «Letzte Verwarnung (Kündigungsandrohung)»
    bool StufeKritisch,         // LETZTE → roter Stufen-Text
    IReadOnlyList<string> AlleGruende,
    IReadOnlyList<string> GewaehlteGruende,
    string? Beschreibung
);

public class VerwarnungPdfService
{
    private const string Dark = "#27251F";
    private const string Red  = "#B91C1C";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public byte[] Generate(VerwarnungFormularInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var gewaehlt = new HashSet<string>(d.GewaehlteGruende, StringComparer.OrdinalIgnoreCase);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.2f, Unit.Centimetre);
                page.MarginHorizontal(2.0f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(11f).LineHeight(1.3f).FontColor(Dark));

                page.Header().Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(16).Column(col =>
                {
                    col.Item().Text($"{d.CompanyName} · {d.RestaurantName}")
                        .FontSize(9f).FontColor("#6b6152");

                    col.Item().PaddingTop(18).AlignCenter().Text("VERWARNUNG")
                        .FontSize(18f).Bold().LetterSpacing(0.08f);
                    col.Item().PaddingTop(2).AlignCenter().Text(d.StufeLabel)
                        .FontSize(11.5f).Bold().FontColor(d.StufeKritisch ? Red : Dark);

                    // Für / Datum
                    col.Item().PaddingTop(20).Row(r =>
                    {
                        r.RelativeItem().Text(t =>
                        {
                            t.Span("Für:  ").Bold();
                            t.Span(d.MaName);
                            if (!string.IsNullOrWhiteSpace(d.EmployeeNumber))
                                t.Span($"  (Personalnr. {d.EmployeeNumber})").FontColor("#6b6152");
                        });
                        r.ConstantItem(170).AlignRight().Text(t =>
                        {
                            t.Span("Datum:  ").Bold();
                            t.Span($"{d.Datum:dd.MM.yyyy}");
                        });
                    });

                    // Ankreuz-Gründe (wie Papier-Formular)
                    col.Item().PaddingTop(16).Column(list =>
                    {
                        foreach (var g in d.AlleGruende)
                        {
                            bool isChecked = gewaehlt.Contains(g);
                            list.Item().PaddingBottom(5).Row(r =>
                            {
                                r.ConstantItem(20).AlignMiddle().Element(e =>
                                {
                                    // Kaestchen 13pt, X mit LineHeight 1 und 8pt — sonst
                                    // passt der Text nicht in die fixe Box und QuestPDF
                                    // wirft eine DocumentLayoutException (HTTP 500).
                                    var box = e.Width(13).Height(13).Border(1.1f).BorderColor(Dark);
                                    if (isChecked)
                                        box.AlignCenter().AlignMiddle().Text("X")
                                           .FontSize(8f).Bold().LineHeight(1f);
                                });
                                var label = r.RelativeItem().AlignMiddle().Text(g).FontSize(11f);
                                if (isChecked) label.Bold();
                            });
                        }
                    });

                    // Bemerkung / Freitext (oder Leerzeilen wie im Formular)
                    col.Item().PaddingTop(10).Column(c =>
                    {
                        if (!string.IsNullOrWhiteSpace(d.Beschreibung))
                        {
                            c.Item().Text("Bemerkung:").Bold().FontSize(10.5f);
                            c.Item().PaddingTop(3).Text(d.Beschreibung).FontSize(11f);
                        }
                        else
                        {
                            c.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor("#9a958c");
                            c.Item().PaddingTop(16).LineHorizontal(0.6f).LineColor("#9a958c");
                        }
                    });

                    // Unterschriften Mitarbeiter + Schichtführer (wie Vorlage)
                    col.Item().PaddingTop(46).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Width(190).LineHorizontal(0.8f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text("Mitarbeiter").FontSize(10f);
                        });
                        r.ConstantItem(40);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Width(190).LineHorizontal(0.8f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text("Schichtführer / Vorgesetzter").FontSize(10f);
                        });
                    });

                    col.Item().PaddingTop(24).Text(
                        "Diese Verwarnung wird in der Personalakte abgelegt. Bei wiederholtem " +
                        "Fehlverhalten müssen arbeitsrechtliche Konsequenzen bis hin zur Kündigung " +
                        "in Betracht gezogen werden.")
                        .FontSize(9f).FontColor("#6b6152").Italic();
                });
            });
        }).GeneratePdf();
    }
}
