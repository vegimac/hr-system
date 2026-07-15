using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Kündigungsschreiben (Walter-Vorgabe 22.06.2026). Formeller Geschäftsbrief im
/// Haus-Stil (gelber Briefkopf): Absender-Filiale, Empfänger-MA, Ort/Datum,
/// Betreff, Text mit Kündigungsfrist + letztem Arbeitstag, optional Grund,
/// Unterschrift des eingeloggten Users (Bild + Klarname).
/// </summary>
public class KuendigungPdfService
{
    private const string Dark = "#1a1a1a";

    private static readonly byte[] BannerBytes =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public record KuendigungData(
        // Arbeitgeber / Filiale
        string? FirmaName, string? FirmaStrasse, string? FirmaPlzOrt,
        // Mitarbeitende/r
        string? MaName, string? MaStrasse, string? MaPlzOrt,
        string  Briefanrede,          // "Sehr geehrte Frau Muster" / "Sehr geehrter Herr Muster"
        // Brief
        string  Ort, DateOnly KuendigungsDatum,
        string  FristText,            // z.B. "2 Monaten auf Ende eines Monats" / "7 Tagen"
        DateOnly LetzterArbeitstag,
        string? Grund,                // optional, sonst null
        string? UnterzeichnerName,
        bool    Eingeschrieben = false);   // «EINSCHREIBEN» ueber der MA-Adresse

    public byte[] Generate(KuendigungData d, byte[]? signaturePng)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        const float sizeText = 10.5f;

        var firmaLines = new[] { d.FirmaName, d.FirmaStrasse, d.FirmaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        var maLines = new[] { d.MaName, d.MaStrasse, d.MaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.3f, Unit.Centimetre);
                page.MarginHorizontal(2.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(sizeText).FontColor(Dark).LineHeight(1.4f));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    // Absender (Filiale) — klein oben links (Walter 15.07.2026:
                    // Filiale, MA und Datum ALLE linksbuendig).
                    foreach (var ln in firmaLines)
                        col.Item().Text(ln).FontSize(8.5f).FontColor("#475569");

                    // «EINSCHREIBEN» ueber der Empfaenger-Adresse (Walter 15.07.2026).
                    if (d.Eingeschrieben)
                        col.Item().PaddingTop(26).Text("EINSCHREIBEN").Bold().LetterSpacing(0.06f);

                    // Empfänger-Adressblock.
                    col.Item().PaddingTop(d.Eingeschrieben ? 4 : 26).Column(c =>
                    {
                        foreach (var ln in maLines) c.Item().Text(ln);
                    });

                    // Ort, Datum — linksbuendig (Walter 15.07.2026).
                    col.Item().PaddingTop(30)
                        .Text($"{d.Ort}, {d.KuendigungsDatum:dd.MM.yyyy}");

                    // Betreff.
                    col.Item().PaddingTop(30).Text("Kündigung des Arbeitsverhältnisses").Bold().FontSize(12.5f);

                    // Anrede.
                    col.Item().PaddingTop(22).Text($"{d.Briefanrede},");

                    // Haupttext.
                    col.Item().PaddingTop(14).Text(t =>
                    {
                        t.Span("hiermit kündigen wir das mit Ihnen bestehende Arbeitsverhältnis ordentlich unter Einhaltung der vertraglichen bzw. gesetzlichen Kündigungsfrist von ");
                        t.Span(d.FristText).Bold();
                        t.Span(" per ");
                        t.Span($"{d.LetzterArbeitstag:dd.MM.yyyy}").Bold();
                        t.Span(" (letzter Arbeitstag).");
                    });

                    if (!string.IsNullOrWhiteSpace(d.Grund))
                        col.Item().PaddingTop(14).Text($"Grund der Kündigung: {d.Grund}");

                    col.Item().PaddingTop(14).Text(
                        "Wir bitten Sie, bis zu Ihrem letzten Arbeitstag Ihre Aufgaben ordnungsgemäss zu übergeben und sämtliches Firmeneigentum (Schlüssel, Badge, Uniform etc.) zurückzugeben.");

                    col.Item().PaddingTop(14).Text(
                        "Wir wünschen Ihnen für Ihre berufliche und private Zukunft alles Gute.");
                });

                // Gruss + Unterschrift als FOOTER — am Seitenende verankert,
                // der Brief verteilt sich damit ueber die ganze Seite
                // (Walter 15.07.2026, gleiches Muster wie die Zeugnisse).
                page.Footer().Column(col =>
                {
                    col.Item().Text("Freundliche Grüsse");
                    if (!string.IsNullOrWhiteSpace(d.FirmaName))
                        col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();

                    // Unterschrift (Bild des eingeloggten Users) + Klarname.
                    if (signaturePng is { Length: > 0 })
                        col.Item().PaddingTop(8).Height(48).AlignLeft().Image(signaturePng).FitHeight();
                    else
                        col.Item().PaddingTop(8).Height(40); // Freiraum zum Unterschreiben

                    col.Item().PaddingTop(2).Text(d.UnterzeichnerName ?? "");
                });
            });
        }).GeneratePdf();
    }
}
