using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// K3 (Walter 29.08.2026, Konzept Kap. 4): Darlehensvertrag für das
/// generische, ZINSLOSE MA-Darlehen (QST-Nachzahlung oder freier Vorschuss,
/// z.B. «Vorschuss Hochzeit»). Inhalt: Betrag, Zweck, Ratenplan, zinslos,
/// schriftliche Einwilligung zur Lohnverrechnung (Art. 323b OR), Fälligkeit
/// des Restsaldos bei Austritt. Unterschriften: Arbeitgeber LINKS,
/// Mitarbeiter RECHTS (Walter-Konvention 16.07.2026).
/// </summary>
public class DarlehenVertragPdfService
{
    private const string Dark = "#1a1a1a";

    private static readonly byte[] BannerBytes =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public record DarlehenVertragData(
        string? ArbeitgeberName, string? ArbeitgeberStrasse, string? ArbeitgeberPlzOrt,
        string? MaName, string? MaStrasse, string? MaPlzOrt, string? MaGeburtsdatum,
        string Zweck, decimal Betrag, decimal RateBetrag,
        int StartJahr, int StartMonat, DateOnly? AuszahlungDatum,
        string? AuszahlungArt,
        string? AgVertreterName);

    public byte[] Generate(DarlehenVertragData d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        const float sizeText = 10f;
        string Chf(decimal v) => v.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("de-CH"));

        var agLines = new[] { d.ArbeitgeberName, d.ArbeitgeberStrasse, d.ArbeitgeberPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        var maLines = new[] { d.MaName, d.MaStrasse, d.MaPlzOrt,
                              string.IsNullOrWhiteSpace(d.MaGeburtsdatum) ? null : $"geb. {d.MaGeburtsdatum}" }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

        // Ratenplan berechnen: Rate pro Monat, letzte Rate = Rest.
        int anzahl = d.RateBetrag > 0 ? (int)Math.Ceiling(d.Betrag / d.RateBetrag) : 1;
        decimal letzteRate = Math.Round(d.Betrag - d.RateBetrag * (anzahl - 1), 2);
        var monatsnamen = new[] { "", "Januar", "Februar", "März", "April", "Mai", "Juni",
                                  "Juli", "August", "September", "Oktober", "November", "Dezember" };
        int endMonat = d.StartMonat + anzahl - 1;
        int endJahr  = d.StartJahr + (endMonat - 1) / 12;
        endMonat = ((endMonat - 1) % 12) + 1;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(0.3f, Unit.Centimetre);
                page.MarginBottom(1.5f, Unit.Centimetre);
                page.MarginHorizontal(1.8f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(sizeText).FontColor(Dark));
                page.Header().Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().Text("Darlehensvertrag (zinsloses Mitarbeiter-Darlehen)")
                        .Bold().FontSize(13f);

                    col.Item().PaddingTop(10).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Darlehensgeberin (Arbeitgeberin):").Bold();
                            foreach (var l in agLines) c.Item().Text(l);
                        });
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Darlehensnehmer/in (Mitarbeiter/in):").Bold();
                            foreach (var l in maLines) c.Item().Text(l);
                        });
                    });

                    col.Item().PaddingTop(18).Text("1. Darlehen und Zweck").Bold();
                    col.Item().Text(t =>
                    {
                        t.Justify();
                        t.Span("Die Arbeitgeberin gewährt dem/der Mitarbeitenden ein ");
                        t.Span("zinsloses").Bold();
                        t.Span($" Darlehen von ");
                        t.Span($"CHF {Chf(d.Betrag)}").Bold();
                        t.Span($" zum Zweck «{d.Zweck}».");
                        // Auszahlungsart (Walter 29.08.2026): bar aus dem
                        // Tresor ODER mit der Lohnzahlung; KEINE = das Geld
                        // ging nicht an den MA (z.B. QST-Zahlung an die Behörde).
                        var artU = (d.AuszahlungArt ?? "BAR").ToUpperInvariant();
                        var perTxt = d.AuszahlungDatum.HasValue ? $" per {d.AuszahlungDatum:dd.MM.yyyy}" : "";
                        if (artU == "LOHN")
                            t.Span($" Die Auszahlung erfolgt mit der Lohnzahlung{perTxt} und wird auf der Lohnabrechnung ausgewiesen.");
                        else if (artU == "KEINE")
                            t.Span($" Es erfolgt keine Auszahlung an den/die Mitarbeitende{perTxt} — der Betrag wurde durch die Arbeitgeberin direkt beglichen (z.B. Quellensteuer-Nachzahlung an die Behörde).");
                        else
                            t.Span($" Die Auszahlung erfolgt bar{perTxt}; der Empfang wird mit der Unterschrift unten quittiert.");
                    });

                    col.Item().PaddingTop(14).Text("2. Rückzahlung (Ratenplan)").Bold();
                    col.Item().Text(t =>
                    {
                        t.Justify();
                        t.Span($"Die Rückzahlung erfolgt in {anzahl} Monatsraten zu CHF {Chf(d.RateBetrag)} " +
                               $"(letzte Rate CHF {Chf(letzteRate)}), erstmals mit der Lohnabrechnung " +
                               $"{monatsnamen[d.StartMonat]} {d.StartJahr}, voraussichtlich bis " +
                               $"{monatsnamen[endMonat]} {endJahr}. Es werden keine Zinsen geschuldet.");
                    });

                    col.Item().PaddingTop(14).Text("3. Einwilligung zur Lohnverrechnung (Art. 323b OR)").Bold();
                    col.Item().Text(t =>
                    {
                        t.Justify();
                        t.Span("Der/die Mitarbeitende willigt ausdrücklich ein, dass die Raten gemäss " +
                               "Ziffer 2 mit dem monatlichen Lohn verrechnet werden (Art. 323b Abs. 2 OR). " +
                               "Der jeweilige Restsaldo wird auf der Lohnabrechnung ausgewiesen.");
                    });

                    col.Item().PaddingTop(14).Text("4. Fälligkeit bei Austritt").Bold();
                    col.Item().Text(t =>
                    {
                        t.Justify();
                        t.Span("Endet das Arbeitsverhältnis vor vollständiger Rückzahlung, wird der " +
                               "gesamte Restsaldo mit der letzten Lohnabrechnung zur Zahlung fällig und " +
                               "mit dieser verrechnet; ein danach verbleibender Rest ist innert 30 Tagen " +
                               "zu begleichen.");
                    });

                    col.Item().PaddingTop(14).Text("5. Vorzeitige Rückzahlung").Bold();
                    col.Item().Text(t =>
                    {
                        t.Justify();
                        t.Span("Eine vorzeitige (Teil-)Rückzahlung ist jederzeit ohne Kosten möglich.");
                    });

                    // Unterschriften: AG LINKS, MA RECHTS (Walter-Konvention).
                    // Walter 29.08.2026: Block ans SEITENENDE (Extend füllt den
                    // Rest der Seite — nichts nach oben gedrängt), viel Platz
                    // für Handschrift, NUR EINE Linie pro Partei (Ort/Datum
                    // ohne Unterstrich-Linie, wird über der Linie geschrieben).
                    col.Item().Extend();
                    col.Item().Row(r =>
                    {
                        // Bei BAR-Auszahlung quittiert der MA mit seiner
                        // Unterschrift zusätzlich den Bar-Empfang (Walter
                        // 29.08.2026): «Betrag bar am … erhalten».
                        bool bar = string.Equals(d.AuszahlungArt ?? "BAR", "BAR", StringComparison.OrdinalIgnoreCase);
                        void SigBlock(QuestPDF.Fluent.ColumnDescriptor c, string rolle, string? name, string? zusatz)
                        {
                            c.Item().Text("Ort / Datum, Unterschrift:").FontSize(9f).FontColor("#646464");
                            c.Item().Height(64);                     // Handschrift-Raum
                            c.Item().LineHorizontal(0.8f);
                            c.Item().PaddingTop(3).Text(rolle).FontSize(9f);
                            if (!string.IsNullOrWhiteSpace(name))
                                c.Item().Text(name!).FontSize(9f);
                            if (!string.IsNullOrWhiteSpace(zusatz))
                                c.Item().PaddingTop(6).Text(zusatz!).FontSize(9f);
                        }
                        r.RelativeItem().Column(c => SigBlock(c, "Die Arbeitgeberin", d.AgVertreterName, null));
                        r.ConstantItem(50);
                        r.RelativeItem().Column(c => SigBlock(c, "Der/die Mitarbeitende", d.MaName,
                            bar ? $"Betrag von CHF {Chf(d.Betrag)} bar am _______________ erhalten." : null));
                    });
                });
            });
        }).GeneratePdf();
    }
}
