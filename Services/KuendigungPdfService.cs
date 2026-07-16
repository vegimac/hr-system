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

    /// <summary>
    /// Rückzug einer ausgesprochenen Kündigung (Walter-Vorgabe 16.07.2026) —
    /// z.B. wegen nachträglich gemeldeter Schwangerschaft (Sperrfrist OR 336c).
    /// Rechtlich braucht der Rückzug das Einverständnis der/des MA → der Brief
    /// enthält unten einen Einverständnis-Block mit Unterschriftszeile.
    /// </summary>
    public record RueckzugData(
        string? FirmaName, string? FirmaStrasse, string? FirmaPlzOrt,
        string? MaName, string? MaStrasse, string? MaPlzOrt,
        string  Briefanrede,
        string  Ort, DateOnly Datum,
        DateOnly KuendigungVom,          // Datum der urspruenglichen Kuendigung
        string? Grund,                   // optionaler Rueckzugs-Grund
        string? UnterzeichnerName,
        bool    Eingeschrieben = false);

    public byte[] GenerateRueckzug(RueckzugData d, byte[]? signaturePng)
    {
        QuestPDF.Settings.License = LicenseType.Community;

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
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).FontColor(Dark).LineHeight(1.4f));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    foreach (var ln in firmaLines)
                        col.Item().Text(ln).FontSize(8.5f).FontColor("#475569");

                    if (d.Eingeschrieben)
                        col.Item().PaddingTop(26).Text("EINSCHREIBEN").Bold().LetterSpacing(0.06f);

                    col.Item().PaddingTop(d.Eingeschrieben ? 4 : 26).Column(c =>
                    {
                        foreach (var ln in maLines) c.Item().Text(ln);
                    });

                    col.Item().PaddingTop(30).Text($"{d.Ort}, {d.Datum:dd.MM.yyyy}");

                    col.Item().PaddingTop(30).Text($"Rückzug unserer Kündigung vom {d.KuendigungVom:dd.MM.yyyy}")
                        .Bold().FontSize(12.5f);

                    col.Item().PaddingTop(22).Text($"{d.Briefanrede},");

                    col.Item().PaddingTop(14).Text(t =>
                    {
                        t.Span("hiermit ziehen wir die Ihnen gegenüber am ");
                        t.Span($"{d.KuendigungVom:dd.MM.yyyy}").Bold();
                        t.Span(" ausgesprochene Kündigung des Arbeitsverhältnisses zurück.");
                    });

                    if (!string.IsNullOrWhiteSpace(d.Grund))
                        col.Item().PaddingTop(14).Text($"Grund des Rückzugs: {d.Grund}");

                    col.Item().PaddingTop(14).Text(
                        "Das Arbeitsverhältnis wird unverändert und ohne Unterbruch zu den bisherigen Vertragsbedingungen fortgesetzt, wie wenn die Kündigung nie ausgesprochen worden wäre.");

                    col.Item().PaddingTop(14).Text(
                        "Da der Rückzug einer Kündigung rechtlich nur mit Ihrem Einverständnis wirksam wird, bitten wir Sie, Ihr Einverständnis mit Ihrer Unterschrift auf der Kopie dieses Schreibens zu bestätigen und uns diese zurückzugeben.");

                    col.Item().PaddingTop(14).Text(
                        "Wir freuen uns auf die weitere Zusammenarbeit mit Ihnen.");
                });

                page.Footer().Column(col =>
                {
                    col.Item().Text("Freundliche Grüsse");
                    if (!string.IsNullOrWhiteSpace(d.FirmaName))
                        col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();

                    if (signaturePng is { Length: > 0 })
                        col.Item().PaddingTop(6).Height(44).AlignLeft().Image(signaturePng).FitHeight();
                    else
                        col.Item().PaddingTop(6).Height(36);

                    col.Item().PaddingTop(2).Text(d.UnterzeichnerName ?? "");

                    // Einverstaendnis-Block der/des MA
                    col.Item().PaddingTop(18).Text("Mit dem Rückzug der Kündigung einverstanden:").FontSize(9f).FontColor("#475569");
                    col.Item().PaddingTop(16).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Width(190).LineHorizontal(0.8f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text("Ort und Datum").FontSize(8.5f).FontColor("#475569");
                        });
                        r.ConstantItem(40);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Width(190).LineHorizontal(0.8f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text($"Unterschrift {d.MaName}").FontSize(8.5f).FontColor("#475569");
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

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
