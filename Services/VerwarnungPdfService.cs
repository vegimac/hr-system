using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Verwarnungs-Formular als PDF (Walter-Vorgabe 15.07.2026, Ein-Seiten-
/// Blatt 14.09.2026): Titel VERWARNUNG, Für/Datum, nur die angekreuzten
/// Gründe (leere Kästchen werden nicht gedruckt), Bemerkung, Unterschriften
/// Mitarbeiter + Schichtführer. Briefkopf = gelbes Banner wie überall.
/// Ablauf: erfassen → speichern → Formular drucken → unterschreiben →
/// Scan nachführen.
/// </summary>
public record VerwarnungFormularInput(
    string CompanyName,
    string RestaurantName,
    string MaName,
    string? EmployeeNumber,
    DateTime Datum,
    string StufeLabel,          // «1. Verwarnung» | «2. Verwarnung» | «Letzte Verwarnung (Kündigungsandrohung)»
    bool StufeKritisch,         // LETZTE → roter Stufen-Text
    IReadOnlyList<string> GewaehlteGruende,
    string? Beschreibung
);

public class VerwarnungPdfService
{
    private const string Dark = "#27251F";
    private const string Red  = "#B91C1C";
    private const int BemerkungMaxZeichen = 480;

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public byte[] Generate(VerwarnungFormularInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var gruende = (d.GewaehlteGruende ?? Array.Empty<string>())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .ToList();
        var bemerkung = KuerzeBemerkung(d.Beschreibung);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.1f, Unit.Centimetre);
                page.MarginHorizontal(2.0f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).LineHeight(1.25f).FontColor(Dark));

                page.Header().Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Item().Text($"{d.CompanyName} · {d.RestaurantName}")
                        .FontSize(9f).FontColor("#6b6152");

                    col.Item().PaddingTop(14).AlignCenter().Text("VERWARNUNG")
                        .FontSize(17f).Bold().LetterSpacing(0.08f);
                    col.Item().PaddingTop(2).AlignCenter().Text(d.StufeLabel)
                        .FontSize(11f).Bold().FontColor(d.StufeKritisch ? Red : Dark);

                    col.Item().PaddingTop(14).Row(r =>
                    {
                        r.RelativeItem().Text(t =>
                        {
                            t.Span("Für:  ").Bold();
                            t.Span(d.MaName);
                            if (!string.IsNullOrWhiteSpace(d.EmployeeNumber))
                                t.Span($"  (Personalnr. {d.EmployeeNumber})").FontColor("#6b6152");
                        });
                        r.ConstantItem(160).AlignRight().Text(t =>
                        {
                            t.Span("Datum:  ").Bold();
                            t.Span($"{d.Datum:dd.MM.yyyy}");
                        });
                    });

                    if (gruende.Count > 0)
                    {
                        col.Item().PaddingTop(12).Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn();
                                c.ConstantColumn(10);
                                c.RelativeColumn();
                            });
                            for (int i = 0; i < gruende.Count; i += 2)
                            {
                                t.Cell().Element(e => GrundZelle(e, gruende[i]));
                                t.Cell();
                                if (i + 1 < gruende.Count)
                                    t.Cell().Element(e => GrundZelle(e, gruende[i + 1]));
                                else
                                    t.Cell();
                            }
                        });
                    }

                    col.Item().PaddingTop(10).Column(c =>
                    {
                        c.Item().Text("Bemerkung:").Bold().FontSize(10f);
                        if (!string.IsNullOrWhiteSpace(bemerkung))
                        {
                            c.Item().PaddingTop(3).Text(bemerkung).FontSize(10f).LineHeight(1.22f);
                        }
                        else
                        {
                            c.Item().PaddingTop(8).LineHorizontal(0.6f).LineColor("#9a958c");
                            c.Item().PaddingTop(14).LineHorizontal(0.6f).LineColor("#9a958c");
                        }
                    });

                    col.Item().PaddingTop(28).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Height(32);
                            c.Item().Text("Schichtführer / Vorgesetzter").FontSize(10f);
                        });
                        r.ConstantItem(40);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Height(32);
                            c.Item().Text("Mitarbeiter").FontSize(10f);
                        });
                    });

                    col.Item().PaddingTop(16).Text(
                        "Diese Verwarnung wird in der Personalakte abgelegt. Bei wiederholtem " +
                        "Fehlverhalten müssen arbeitsrechtliche Konsequenzen bis hin zur Kündigung " +
                        "in Betracht gezogen werden.")
                        .FontSize(8.5f).FontColor("#6b6152").Italic();
                });
            });
        }).GeneratePdf();
    }

    private static void GrundZelle(IContainer container, string text)
    {
        container.PaddingBottom(5).Row(r =>
        {
            r.ConstantItem(16).AlignMiddle().Element(e =>
            {
                var box = e.Width(11).Height(11).Border(1.1f).BorderColor(Dark);
                box.AlignCenter().AlignMiddle().Text("X")
                    .FontSize(7.5f).Bold().LineHeight(1f);
            });
            r.RelativeItem().AlignMiddle().Text(text).FontSize(10f).Bold();
        });
    }

    /// <summary>
    /// Lange Kommentare kürzen, damit das Blatt eine Seite bleibt
    /// (Walter 14.09.2026). Ungefähr 6 Zeilen auf A4.
    /// </summary>
    public static string? KuerzeBemerkung(string? s)
    {
        var t = (s ?? "").Trim();
        if (t.Length == 0) return null;
        if (t.Length <= BemerkungMaxZeichen) return t;
        return t.Substring(0, BemerkungMaxZeichen - 1).TrimEnd() + "…";
    }
}
