using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
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
        var pfad = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png");
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

    /// <summary>
    /// Setzt den Briefkopf in den freien Streifen über der Absenderzeile und
    /// schreibt Datum, Betreff und Text unter das Fenstercouvert. Absender,
    /// Marke und Empfängeradresse bleiben, wie die Post sie gesetzt hat.
    /// </summary>
    public byte[] SchreibeAufFrankatur(byte[] frankaturPdf, BriefDaten d)
    {
        var absaetze = (d.Text ?? "").Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(a => a.Replace("\n", " ").Trim()).Where(a => a.Length > 0).ToList();
        var datumZeile = string.IsNullOrWhiteSpace(d.Ort)
            ? d.Datum.ToString("dd.MM.yyyy")
            : $"{d.Ort!.Trim()}, {d.Datum:dd.MM.yyyy}";

        using var reader = new PdfReader(new MemoryStream(frankaturPdf));
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using (var pdf = new PdfDocument(reader, writer))
        {
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var fett = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            var seite = pdf.GetFirstPage();
            var stift = new PdfCanvas(seite);
            var farbe = new DeviceRgb(26, 26, 26);
            float Mm(float mm) => mm * 72f / 25.4f;
            var x = Mm(RandLinks);
            var unten = Mm(RandUnten);
            var y = seite.GetPageSize().GetHeight() - Mm(FensterObenMm + FensterHoeheMm + 8);
            var breite = seite.GetPageSize().GetWidth() - x - Mm(RandRechts);

            void NeueSeite()
            {
                seite = pdf.AddNewPage(iText.Kernel.Geom.PageSize.A4);
                stift = new PdfCanvas(seite);
                y = seite.GetPageSize().GetHeight() - Mm(20);
                breite = seite.GetPageSize().GetWidth() - x - Mm(RandRechts);
            }

            void Absatz(string text, PdfFont f, float groesse, float zeile, float danach)
            {
                var rest = FuerHelvetica(text);
                while (rest.Length > 0)
                {
                    if (y < unten + groesse) NeueSeite();
                    var n = Passend(rest, f, groesse, breite);
                    var stueck = rest[..n].TrimEnd();
                    stift.BeginText()
                        .SetFontAndSize(f, groesse)
                        .SetFillColor(farbe)
                        .MoveText(x, y)
                        .ShowText(stueck)
                        .EndText();
                    rest = rest[n..].TrimStart();
                    y -= zeile;
                }
                y -= danach;
            }

            if (BannerBytes != null)
            {
                var bild = ImageDataFactory.Create(BannerBytes);
                var kastenBreite = breite;
                var kastenHoehe = kastenBreite * bild.GetHeight() / bild.GetWidth();
                // Die Absenderzeilen der Post beginnen bei 18 mm. Der Kopf bleibt darüber.
                var maxHoehe = Mm(14);
                if (kastenHoehe > maxHoehe)
                {
                    kastenHoehe = maxHoehe;
                    kastenBreite = kastenHoehe * bild.GetWidth() / bild.GetHeight();
                }
                var oben = seite.GetPageSize().GetHeight() - Mm(2);
                stift.AddImageFittedIntoRectangle(bild,
                    new Rectangle(x, oben - kastenHoehe, kastenBreite, kastenHoehe), false);
            }

            Absatz(datumZeile, font, 10.5f, 14f, 10f);
            Absatz(d.Betreff.Trim(), fett, 12.5f, 16f, 8f);
            foreach (var a in absaetze) Absatz(a, font, 10.5f, 14f, 6f);
        }
        return ms.ToArray();
    }

    private static int Passend(string text, PdfFont font, float groesse, float breite)
    {
        if (font.GetWidth(text, groesse) <= breite) return text.Length;
        var n = text.Length;
        while (n > 1 && font.GetWidth(text[..n], groesse) > breite) n--;
        var leer = text.LastIndexOf(' ', Math.Max(0, n - 1));
        return leer > 0 ? leer + 1 : Math.Max(1, n);
    }

    /// <summary>Helvetica (WinAnsi) kennt kein Zeichen über Latin-1.</summary>
    private static string FuerHelvetica(string text)
    {
        var ziel = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            ziel[i] = c is '\n' or '\r' or '\t' ? ' ' : c <= 0xFF ? c : '?';
        }
        return new string(ziel);
    }
}
