using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Mitteilung an Mitarbeitende als PDF (Walter 08.09.2026): easy@work kennt nur
/// Dokumente, keine reinen Textnachrichten. Also macht OneCrew aus Betreff +
/// Text ein einseitiges Schreiben im Haus-Stil (gelber Briefkopf), das dann als
/// HR-Datei ins easy@work-Dossier gelegt wird — der MA sieht die Benachrichtigung
/// mit dem Betreff und öffnet die Mitteilung als PDF.
/// </summary>
public class MitteilungPdfService
{
    private const string Dark = "#1a1a1a";

    private static readonly byte[] BannerBytes =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public record MitteilungData(
        string? FirmaName,
        string? RestaurantName,
        string? EmpfaengerName,     // «Walter Schaub» — Anrede-Zeile, optional
        string  Betreff,
        string  Text,               // Absätze durch Leerzeilen getrennt
        DateOnly Datum,
        string? Ort,
        string? AbsenderName,       // wer die Mitteilung schickt (GF / HR)
        string? AbsenderFunktion = null);

    /// <param name="signaturePng">Unterschrift als Bild (AppUser.SignaturePng), optional.</param>
    public byte[] Generate(MitteilungData d, byte[]? signaturePng = null)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var firmaLines = new[] { d.FirmaName, d.RestaurantName }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
        var absaetze = (d.Text ?? "")
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim())
            .Where(a => a.Length > 0)
            .ToList();
        var datumZeile = string.IsNullOrWhiteSpace(d.Ort)
            ? d.Datum.ToString("dd.MM.yyyy")
            : $"{d.Ort!.Trim()}, {d.Datum:dd.MM.yyyy}";

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

                    col.Item().PaddingTop(28).Text(datumZeile);

                    col.Item().PaddingTop(14).Text(d.Betreff.Trim()).Bold().FontSize(12.5f);

                    if (!string.IsNullOrWhiteSpace(d.EmpfaengerName))
                        col.Item().PaddingTop(18).Text($"Guten Tag {d.EmpfaengerName!.Trim()}");

                    var first = true;
                    foreach (var a in absaetze)
                    {
                        col.Item().PaddingTop(first ? 14 : 10).Text(a);
                        first = false;
                    }

                    if (!string.IsNullOrWhiteSpace(d.AbsenderName) || firmaLines.Count > 0)
                    {
                        col.Item().PaddingTop(28).Text("Freundliche Grüsse");
                        if (!string.IsNullOrWhiteSpace(d.FirmaName))
                            col.Item().PaddingTop(2).Text(d.FirmaName!.Trim()).Bold();
                        if (!string.IsNullOrWhiteSpace(d.RestaurantName))
                            col.Item().Text(d.RestaurantName!.Trim());
                        if (signaturePng is { Length: > 0 })
                            col.Item().PaddingTop(10).Height(48).AlignLeft().Image(signaturePng).FitHeight();
                        if (!string.IsNullOrWhiteSpace(d.AbsenderName))
                            col.Item().PaddingTop(signaturePng is { Length: > 0 } ? 2 : 10).Text(d.AbsenderName!.Trim());
                        if (!string.IsNullOrWhiteSpace(d.AbsenderFunktion))
                            col.Item().Text(d.AbsenderFunktion!.Trim()).FontColor("#475569");
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor("#94a3b8"));
                    t.Span("Mitteilung aus OneCrew · Seite ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
