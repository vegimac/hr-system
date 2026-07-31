using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Aufforderung zur Arbeit (Walter 30.07.2026) — formeller Brief im Haus-Stil
/// (gelber Briefkopf), Text analog zur Langenthal-Vorlage (OR 321 / 337d).
/// </summary>
public class AufforderungZurArbeitPdfService
{
    private const string Dark = "#1a1a1a";

    private static readonly byte[] BannerBytes =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public record AufforderungData(
        string? FirmaName,
        string? RestaurantName,
        string? FirmaStrasse,
        string? FirmaPlzOrt,
        string? MaAnrede,           // «Frau» / «Herr» — eigene Adresszeile
        string? MaName,
        string? MaStrasse,
        string? MaPlzOrt,
        string  GutenTagAnrede,     // «Guten Tag Frau Duqi»
        string  Ort,
        DateOnly Datum,
        DateOnly FristBis,
        string  KontaktName,        // Restaurantleiter/in
        string? KontaktTelefon,
        string? KontaktFunktion,
        string? UnterzeichnerName,
        string? UnterzeichnerFunktion = null,
        bool    Eingeschrieben = false);

    public byte[] Generate(AufforderungData d, byte[]? signaturePng)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var firmaLines = new[] { d.FirmaName, d.RestaurantName, d.FirmaStrasse, d.FirmaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        var maLines = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.MaAnrede)) maLines.Add(d.MaAnrede!.Trim());
        if (!string.IsNullOrWhiteSpace(d.MaName)) maLines.Add(d.MaName!.Trim());
        if (!string.IsNullOrWhiteSpace(d.MaStrasse)) maLines.Add(d.MaStrasse!.Trim());
        if (!string.IsNullOrWhiteSpace(d.MaPlzOrt)) maLines.Add(d.MaPlzOrt!.Trim());

        var fristLabel = FormatLongDate(d.FristBis);
        var kontaktFunktion = string.IsNullOrWhiteSpace(d.KontaktFunktion)
            ? "Restaurantleiter"
            : d.KontaktFunktion!.Trim();
        var kontaktTel = string.IsNullOrWhiteSpace(d.KontaktTelefon)
            ? ""
            : $" (Tel. {d.KontaktTelefon.Trim()})";

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.3f, Unit.Centimetre);
                page.MarginHorizontal(2.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).FontColor(Dark).LineHeight(1.4f));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    foreach (var ln in firmaLines)
                        col.Item().Text(ln).FontSize(8.5f).FontColor("#475569");

                    col.Item().Height(40);
                    if (d.Eingeschrieben)
                        col.Item().Text("EINSCHREIBEN").Bold().LetterSpacing(0.06f).FontSize(9.5f);

                    col.Item().PaddingTop(d.Eingeschrieben ? 3 : 16).Column(c =>
                    {
                        foreach (var ln in maLines) c.Item().Text(ln);
                    });

                    col.Item().PaddingTop(30).Text($"{d.Ort}, {d.Datum:dd.MM.yyyy}");

                    col.Item().PaddingTop(28).Text("Aufforderung zur Arbeit").Bold().FontSize(12.5f);

                    col.Item().PaddingTop(20).Text($"{d.GutenTagAnrede}");

                    col.Item().PaddingTop(14).Text(
                        "In den letzten Tagen sind Sie ohne Abmeldung einfach der Arbeit ferngeblieben, obwohl Sie auf dem Schichtplan eingetragen waren. Erfolgt das Nichterscheinen am Arbeitsplatz ohne Meldung an den Arbeitgeber, liegt eine Treuepflichtverletzung seitens des Mitarbeitenden vor (OR 321) und der Arbeitgeber hat Anspruch auf eine entsprechende Entschädigung (OR 337d).");

                    col.Item().PaddingTop(14).Text(t =>
                    {
                        t.Span("Deshalb bitten wir Sie, sich umgehend beim ");
                        t.Span(kontaktFunktion);
                        t.Span(" ");
                        t.Span(d.KontaktName).Bold();
                        t.Span(kontaktTel);
                        t.Span(" zu melden, um das weitere Vorgehen zu besprechen.");
                    });

                    col.Item().PaddingTop(14).Text(
                        "Wir helfen Ihnen gerne bei jeglichen Angelegenheiten.");

                    col.Item().PaddingTop(14).Text(t =>
                    {
                        t.Span("Sollten wir jedoch bis am ");
                        t.Span(fristLabel).Bold();
                        t.Span(" keine Meldung erhalten, erachten wir dies als sofortige Kündigung Ihrerseits.");
                    });

                    col.Item().PaddingTop(14).Text("Besten Dank für die Kenntnisnahme.");

                    col.Item().PaddingTop(28).Text("Freundliche Grüsse");
                    if (!string.IsNullOrWhiteSpace(d.FirmaName))
                        col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();
                    if (!string.IsNullOrWhiteSpace(d.RestaurantName))
                        col.Item().Text(d.RestaurantName!);

                    if (signaturePng is { Length: > 0 })
                        col.Item().PaddingTop(10).Height(48).AlignLeft().Image(signaturePng).FitHeight();
                    else
                        col.Item().PaddingTop(10).Height(40);

                    col.Item().PaddingTop(2).Text(d.UnterzeichnerName ?? "");
                    if (!string.IsNullOrWhiteSpace(d.UnterzeichnerFunktion))
                        col.Item().Text(d.UnterzeichnerFunktion!).FontColor("#475569");
                });
            });
        }).GeneratePdf();
    }

    private static string FormatLongDate(DateOnly d)
    {
        var months = new[] {
            "Januar", "Februar", "März", "April", "Mai", "Juni",
            "Juli", "August", "September", "Oktober", "November", "Dezember"
        };
        return $"{d.Day}. {months[d.Month - 1]} {d.Year}";
    }
}
