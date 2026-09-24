using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services.WebStamp;

/// <summary>
/// Brief für den WebStamp-Druck- und Versandservice (Walter 24.09.2026).
///
/// Anders als die übrigen Briefe (Kündigung, Mitteilung), die die Adresse mit
/// einem Abstandhalter «ungefähr» ins Fenster schieben, steht die Adresse hier
/// auf festen Millimetern: die Post sucht die Empfängeradresse automatisch im
/// Sichtfenster-Bereich und setzt die Frankatur direkt darüber ein. Darum bleibt
/// im Fenster oben ein freier Streifen (<see cref="FrankierStreifenMm"/>).
///
/// Ob die Post Adresse und Fenster erkennt, meldet <c>new_order_preview</c> pro
/// Sendung (<c>window</c> = left/right, <c>state</c> = valid/invalid + Grund) —
/// genau dafür ist die Vorschau auf der Seite «Briefpost» da. Stimmen die Masse
/// nicht, hier anpassen.
/// </summary>
public class WebStampBriefPdfService
{
    private const string Dark = "#1a1a1a";

    // Seitenränder (mm). Links 22 wie die übrigen OneCrew-Briefe.
    // RandOben 9 statt 12 (Walter 24.09.2026): alles 3 mm höher — sonst schaute bei ganz
    // nach unten gerutschtem Brief die Ortschaft der Filiale oben ins Fenster.
    private const float RandOben = 9, RandLinks = 22, RandRechts = 20, RandUnten = 15;

    /// <summary>
    /// Oberkante des Adressbereichs ab Papierkante (mm). 42 statt 45 (Walter 24.09.2026,
    /// im eigenen Couvert geprüft): Kopf, Absender und Adresse stehen 3 mm höher; über der
    /// Adresse bleiben im Fenster rund 1,7 cm frei für die Frankatur.
    /// </summary>
    public const float FensterObenMm = 42;
    public const float FensterHoeheMm = 45;
    /// <summary>Freier Streifen oben im Fenster für die Frankatur der Post (mm).</summary>
    public const float FrankierStreifenMm = 15;
    /// <summary>Linke Kante der Adresse ab Papierkante (mm): Fenster links bzw. rechts.</summary>
    public const float AdresseLinksMm = 22, AdresseRechtsMm = 118;

    private static readonly byte[]? BannerBytes = LadeBanner();

    private static byte[]? LadeBanner()
    {
        var pfad = Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png");
        return File.Exists(pfad) ? File.ReadAllBytes(pfad) : null;
    }

    public record BriefDaten(
        IReadOnlyList<string> AbsenderZeilen,   // Firma, Filiale, Strasse, PLZ Ort
        IReadOnlyList<string> EmpfaengerZeilen, // Anrede, Name, Strasse, PLZ Ort (Land)
        string? Ort,
        DateOnly Datum,
        string Betreff,
        string Text,                            // Absätze durch Leerzeilen getrennt
        string? AbsenderName,
        bool FensterRechts);

    public byte[] Generate(BriefDaten d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var absaetze = (d.Text ?? "").Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Trim()).Where(a => a.Length > 0).ToList();
        var datumZeile = string.IsNullOrWhiteSpace(d.Ort)
            ? d.Datum.ToString("dd.MM.yyyy")
            : $"{d.Ort!.Trim()}, {d.Datum:dd.MM.yyyy}";

        // Rechtes Fenster: schmaler, damit der Block nicht in den rechten Rand läuft.
        var adresseX = (d.FensterRechts ? AdresseRechtsMm : AdresseLinksMm) - RandLinks;
        var adresseBreite = d.FensterRechts ? 210 - RandRechts - AdresseRechtsMm : 85f;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(RandOben, Unit.Millimetre);
                page.MarginLeft(RandLinks, Unit.Millimetre);
                page.MarginRight(RandRechts, Unit.Millimetre);
                page.MarginBottom(RandUnten, Unit.Millimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).FontColor(Dark).LineHeight(1.35f));

                page.Content().Column(col =>
                {
                    // Kopf bis zur Fenster-Oberkante — feste Höhe, sonst verrutscht das Fenster.
                    col.Item().Height(FensterObenMm - RandOben, Unit.Millimetre).Column(kopf =>
                    {
                        if (BannerBytes != null)
                            kopf.Item().Height(14, Unit.Millimetre).AlignLeft().Image(BannerBytes).FitArea();
                        foreach (var ln in d.AbsenderZeilen)
                            kopf.Item().Text(ln).FontSize(8.5f).FontColor("#475569");
                    });

                    // Sichtfenster: nur die Adresse, darüber frei für die Frankatur.
                    col.Item().Height(FensterHoeheMm, Unit.Millimetre).Row(row =>
                    {
                        if (adresseX > 0) row.ConstantItem(adresseX, Unit.Millimetre);
                        row.ConstantItem(adresseBreite, Unit.Millimetre)
                           .PaddingTop(FrankierStreifenMm, Unit.Millimetre)
                           .Column(a =>
                           {
                               foreach (var ln in d.EmpfaengerZeilen) a.Item().Text(ln);
                           });
                        row.RelativeItem();
                    });

                    col.Item().PaddingTop(8, Unit.Millimetre).Text(datumZeile);
                    col.Item().PaddingTop(12).Text(d.Betreff.Trim()).Bold().FontSize(12.5f);

                    var erster = true;
                    foreach (var a in absaetze)
                    {
                        col.Item().PaddingTop(erster ? 14 : 10).Text(a);
                        erster = false;
                    }

                    col.Item().PaddingTop(26).Text("Freundliche Grüsse");
                    if (d.AbsenderZeilen.Count > 0)
                        col.Item().PaddingTop(2).Text(d.AbsenderZeilen[0]).Bold();
                    if (!string.IsNullOrWhiteSpace(d.AbsenderName))
                        col.Item().PaddingTop(10).Text(d.AbsenderName!.Trim());
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor("#94a3b8"));
                    t.Span("Seite ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();
    }
}
