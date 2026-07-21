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
        bool    Eingeschrieben = false,    // true = EINSCHREIBEN; false = Übergeben (Aushändigung)
        string? UnterzeichnerFunktion = null);  // z.B. «HR-Verantwortliche» (user_branch_access.FunctionTitle)

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
        string? UnterzeichnerFunktion = null,   // z.B. «HR-Verantwortliche»
        bool    Eingeschrieben = false,
        // Schwangerschafts-Variante (Walter-Text 16.07.2026): die Kuendigung
        // ist nach OR 336c NICHTIG — Bestaetigungs-Brief «Fortbestehen des
        // Arbeitsverhaeltnisses», KEIN Einverstaendnis-Block noetig.
        bool    NichtigSchwangerschaft = false,
        DateOnly? SchwangerschaftGemeldetAm = null);

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

                    // MA-Adresse im COUVERT-FENSTER (Walter 16.07.2026): Schweizer
                    // C5-Fenster links beginnt ~4.5 cm ab Papierkante. Bis hier
                    // sind es ~3.0 cm (1 cm Rand + 1.1 cm Banner + Abstaende) —
                    // fixer Abstandhalter schiebt den Adressblock in die Zone.
                    col.Item().Height(40);
                    if (d.Eingeschrieben)
                        col.Item().Text("EINSCHREIBEN").Bold().LetterSpacing(0.06f).FontSize(9.5f);
                    col.Item().PaddingTop(d.Eingeschrieben ? 3 : 16).Column(c =>
                    {
                        foreach (var ln in maLines) c.Item().Text(ln);
                    });

                    col.Item().PaddingTop(34).Text($"{d.Ort}, {d.Datum:dd.MM.yyyy}");

                    if (d.NichtigSchwangerschaft)
                    {
                        // Walter-Textvorschlag 16.07.2026: nachtraeglich gemeldete
                        // Schwangerschaft → Kuendigung nichtig (OR 336c).
                        col.Item().PaddingTop(34).Text($"Kündigung vom {d.KuendigungVom:dd.MM.yyyy} – Fortbestehen des Arbeitsverhältnisses")
                            .Bold().FontSize(12.5f);

                        col.Item().PaddingTop(26).Text($"{d.Briefanrede},");

                        col.Item().PaddingTop(18).Text(t =>
                        {
                            t.Span("Sie haben uns");
                            if (d.SchwangerschaftGemeldetAm.HasValue)
                            {
                                t.Span(" am ");
                                t.Span($"{d.SchwangerschaftGemeldetAm.Value:dd.MM.yyyy}").Bold();
                            }
                            t.Span(" darüber informiert, dass Sie schwanger sind und die Schwangerschaft bereits zum Zeitpunkt der Zustellung unserer Kündigung vom ");
                            t.Span($"{d.KuendigungVom:dd.MM.yyyy}").Bold();
                            t.Span(" bestanden hat.");
                        });

                        col.Item().PaddingTop(18).Text(
                            "Gemäss Art. 336c OR ist eine nach Ablauf der Probezeit während der Schwangerschaft ausgesprochene Kündigung durch den Arbeitgeber nichtig.");

                        col.Item().PaddingTop(18).Text(t =>
                        {
                            t.Span("Wir bestätigen Ihnen deshalb, dass unsere Kündigung vom ");
                            t.Span($"{d.KuendigungVom:dd.MM.yyyy}").Bold();
                            t.Span(" keine Rechtswirkung entfaltet. Ihr Arbeitsverhältnis besteht ohne Unterbruch und zu den bisherigen vertraglichen Bedingungen weiter.");
                        });

                        col.Item().PaddingTop(18).Text(
                            "Sämtliche Rechte und Pflichten aus dem Arbeitsverhältnis bleiben unverändert bestehen.");

                        col.Item().PaddingTop(18).Text(
                            "Wir entschuldigen uns für die entstandene Unsicherheit.");
                    }
                    else
                    {
                        col.Item().PaddingTop(34).Text($"Rückzug unserer Kündigung vom {d.KuendigungVom:dd.MM.yyyy}")
                            .Bold().FontSize(12.5f);

                        col.Item().PaddingTop(26).Text($"{d.Briefanrede},");

                        col.Item().PaddingTop(18).Text(t =>
                        {
                            t.Span("hiermit ziehen wir die Ihnen gegenüber am ");
                            t.Span($"{d.KuendigungVom:dd.MM.yyyy}").Bold();
                            t.Span(" ausgesprochene Kündigung des Arbeitsverhältnisses zurück.");
                        });

                        if (!string.IsNullOrWhiteSpace(d.Grund))
                            col.Item().PaddingTop(18).Text($"Grund des Rückzugs: {d.Grund}");

                        col.Item().PaddingTop(18).Text(
                            "Das Arbeitsverhältnis wird unverändert und ohne Unterbruch zu den bisherigen Vertragsbedingungen fortgesetzt, wie wenn die Kündigung nie ausgesprochen worden wäre.");

                        col.Item().PaddingTop(18).Text(
                            "Da der Rückzug einer Kündigung rechtlich nur mit Ihrem Einverständnis wirksam wird, bitten wir Sie, Ihr Einverständnis mit Ihrer Unterschrift auf der Kopie dieses Schreibens zu bestätigen und uns diese zurückzugeben.");

                        col.Item().PaddingTop(18).Text(
                            "Wir freuen uns auf die weitere Zusammenarbeit mit Ihnen.");
                    }

                    // Gruss + Unterschrift direkt nach dem Text (Walter 16.07.2026:
                    // nicht mehr ganz unten am Seitenende). IMMER von Hand
                    // unterschreiben — kein Unterschrift-Bild, nur Freiraum.
                    col.Item().PaddingTop(34).Text("Freundliche Grüsse");
                    if (!string.IsNullOrWhiteSpace(d.FirmaName))
                        col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();
                    col.Item().PaddingTop(6).Height(56);
                    col.Item().Text(d.UnterzeichnerName ?? "");
                    if (!string.IsNullOrWhiteSpace(d.UnterzeichnerFunktion))
                        col.Item().Text(d.UnterzeichnerFunktion!).FontColor("#475569");
                });

                page.Footer().Column(col =>
                {
                    // Einverstaendnis-Block der/des MA — nur beim STANDARD-Rueckzug
                    // (bei der Schwangerschafts-Variante ist die Kuendigung von
                    // Gesetzes wegen nichtig, kein Einverstaendnis noetig).
                    if (!d.NichtigSchwangerschaft)
                    {
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
                    }
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

                    // MA-Adresse im COUVERT-FENSTER (Walter 16.07.2026, wie Rueckzug):
                    // fixer Abstandhalter schiebt den Block in die C5-Fensterzone
                    // (~4.5 cm ab Papierkante).
                    col.Item().Height(40);
                    // Zustellung: Einschreiben ODER persönliche Übergabe
                    // (oft am Probezeitgespräch, Walter 21.07.2026).
                    col.Item().Text(d.Eingeschrieben ? "EINSCHREIBEN" : "PERSÖNLICHE AUSHÄNDIGUNG")
                        .Bold().LetterSpacing(0.06f).FontSize(9.5f);

                    // Empfänger-Adressblock.
                    col.Item().PaddingTop(3).Column(c =>
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

                    // Persönliche Übergabe: Unterschriften IM Content (nicht Footer) —
                    // drei Spalten brauchen zu viel Höhe für den Footer-Slot
                    // (QuestPDF LayoutException, Walter 21.07.2026).
                    if (!d.Eingeschrieben)
                    {
                        col.Item().PaddingTop(28).Text("Freundliche Grüsse");
                        if (!string.IsNullOrWhiteSpace(d.FirmaName))
                            col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();

                        col.Item().PaddingTop(10).Text("Original persönlich übergeben:")
                            .FontSize(9f).FontColor("#475569");
                        col.Item().PaddingTop(22).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                if (signaturePng is { Length: > 0 })
                                    c.Item().Height(32).AlignLeft().Image(signaturePng).FitHeight();
                                else
                                    c.Item().Height(32);
                                c.Item().LineHorizontal(0.8f).LineColor(Dark);
                                c.Item().PaddingTop(3)
                                    .Text(d.UnterzeichnerName ?? "Arbeitgeber")
                                    .FontSize(8.5f).FontColor("#475569");
                                if (!string.IsNullOrWhiteSpace(d.UnterzeichnerFunktion))
                                    c.Item().Text(d.UnterzeichnerFunktion!).FontSize(8f).FontColor("#475569");
                            });
                            r.ConstantItem(12);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Height(32);
                                c.Item().LineHorizontal(0.8f).LineColor(Dark);
                                c.Item().PaddingTop(3).Text("Zeuge der Übergabe")
                                    .FontSize(8.5f).FontColor("#475569");
                            });
                            r.ConstantItem(12);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Height(32);
                                c.Item().LineHorizontal(0.8f).LineColor(Dark);
                                c.Item().PaddingTop(3)
                                    .Text(string.IsNullOrWhiteSpace(d.MaName)
                                        ? "Mitarbeiter (Empfang)"
                                        : d.MaName!)
                                    .FontSize(8.5f).FontColor("#475569");
                            });
                        });
                    }
                });

                // Einschreiben: Gruss + AG-Unterschrift als Footer (wie bisher).
                if (d.Eingeschrieben)
                {
                    page.Footer().Column(col =>
                    {
                        col.Item().Text("Freundliche Grüsse");
                        if (!string.IsNullOrWhiteSpace(d.FirmaName))
                            col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();

                        if (signaturePng is { Length: > 0 })
                            col.Item().PaddingTop(8).Height(48).AlignLeft().Image(signaturePng).FitHeight();
                        else
                            col.Item().PaddingTop(8).Height(40);

                        col.Item().PaddingTop(2).Text(d.UnterzeichnerName ?? "");
                        if (!string.IsNullOrWhiteSpace(d.UnterzeichnerFunktion))
                            col.Item().Text(d.UnterzeichnerFunktion!).FontColor("#475569");
                    });
                }
            });
        }).GeneratePdf();
    }
}
