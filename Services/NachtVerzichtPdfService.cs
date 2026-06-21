using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// „Verzicht auf medizinische Untersuchung und Beratung bei regelmässiger
/// Nachtarbeit" (Walter-Vorgabe 20.06.2026, ArG). Gleiches Layout wie die
/// Arbeitsvertrag-Beilage „Mutterschutz": gelber Briefkopf-Banner mit Logo,
/// Arial, justierte Absätze. Arbeitgeber + Mitarbeitende/r + Funktion werden
/// vorausgefüllt; Ort/Datum und Unterschriften bleiben leer.
/// </summary>
public class NachtVerzichtPdfService
{
    private const string Dark = "#1a1a1a";

    private static readonly byte[] BannerBytes =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public record NachtVerzichtData(
        string? ArbeitgeberName, string? ArbeitgeberStrasse, string? ArbeitgeberPlzOrt, string? ArbeitgeberOrt,
        string? MaName, string? MaStrasse, string? MaPlzOrt, string? MaGeburtsdatum,
        string? UnterzeichnerName, string? UnterzeichnerFunktion);

    public byte[] Generate(NachtVerzichtData d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        const float sizeText  = 10f;
        const float sizeTitle = 11f;

        // Kopf-Werte als Zeilen-Listen (leere Zeilen rausfiltern).
        var agLines = new[] { d.ArbeitgeberName, d.ArbeitgeberStrasse, d.ArbeitgeberPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        var maLines = new[] { d.MaName, d.MaStrasse, d.MaPlzOrt,
                              string.IsNullOrWhiteSpace(d.MaGeburtsdatum) ? null : $"geb. {d.MaGeburtsdatum}" }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

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
                    // Titel grösser, fett, linksbündig (Walter-Vorgabe 20.06.2026).
                    col.Item().Text("Verzicht auf medizinische Untersuchung und Beratung bei regelmässiger Nachtarbeit")
                        .Bold().FontSize(13f);

                    // ── Kopf-Felder (vorausgefüllt) — Funktion entfällt ──
                    col.Item().PaddingTop(10).Element(c => FieldBlock(c, "Arbeitgeber:",    agLines, sizeText));
                    col.Item().PaddingTop(5).Element(c => FieldBlock(c, "Mitarbeitende/r:", maLines, sizeText));

                    col.Item().Element(c => T(c, "Information über den gesetzlichen Anspruch", sizeTitle));
                    col.Item().Element(c => P(c, "Der/die Mitarbeitende bestätigt mit seiner/ihrer Unterschrift, dass er/sie durch den Arbeitgeber über den gesetzlichen Anspruch auf medizinische Untersuchung und Beratung im Zusammenhang mit regelmässiger Nachtarbeit informiert wurde.", sizeText));
                    col.Item().Element(c => P(c, "Insbesondere wurde darauf hingewiesen, dass die medizinische Untersuchung und Beratung auf Verlangen des/der Mitarbeitenden durchgeführt werden kann und die dadurch entstehenden Kosten vom Arbeitgeber übernommen werden.", sizeText));

                    col.Item().Element(c => T(c, "Freiwilliger Verzicht", sizeTitle));
                    col.Item().Element(c => P(c, "Nach erfolgter Information verzichtet der/die Mitarbeitende zum heutigen Zeitpunkt freiwillig auf die Durchführung einer medizinischen Untersuchung und Beratung im Zusammenhang mit der geleisteten Nachtarbeit.", sizeText));
                    col.Item().Element(c => P(c, "Der Verzicht erfolgt ohne Zwang und in Kenntnis des bestehenden Anspruchs.", sizeText));

                    col.Item().Element(c => T(c, "Wahrung des Anspruchs", sizeTitle));
                    col.Item().Element(c => P(c, "Der vorliegende Verzicht stellt keinen dauerhaften oder endgültigen Verzicht auf gesetzliche Rechte dar.", sizeText));
                    col.Item().Element(c => P(c, "Der/die Mitarbeitende kann die Durchführung einer medizinischen Untersuchung und Beratung jederzeit verlangen, sofern die gesetzlichen Voraussetzungen erfüllt sind. Die Kosten der Untersuchung und Beratung werden in diesem Fall vom Arbeitgeber übernommen.", sizeText));

                    col.Item().Element(c => T(c, "Befristung des Verzichts", sizeTitle));
                    col.Item().Element(c => P(c, "Aus Gründen des Gesundheitsschutzes und zur regelmässigen Überprüfung der persönlichen Situation gilt diese Verzichtserklärung für eine Dauer von höchstens zwei Jahren ab dem Datum der Unterzeichnung.", sizeText));
                    col.Item().Element(c => P(c, "Nach Ablauf dieser Frist wird der Arbeitgeber den/die Mitarbeitende/n erneut über den Anspruch auf medizinische Untersuchung und Beratung informieren und die Möglichkeit zur Inanspruchnahme dieser Leistung erneut anbieten.", sizeText));
                    col.Item().Element(c => P(c, "Sollte der/die Mitarbeitende auch zu diesem Zeitpunkt auf die medizinische Untersuchung und Beratung verzichten wollen, ist eine neue schriftliche Verzichtserklärung erforderlich.", sizeText));

                    col.Item().Element(c => T(c, "Schlussbestimmungen", sizeTitle));
                    col.Item().Element(c => P(c, "Diese Erklärung dient ausschliesslich der Dokumentation, dass der Arbeitgeber seiner Informationspflicht nachgekommen ist und der/die Mitarbeitende den Anspruch auf medizinische Untersuchung und Beratung kennt.", sizeText));
                    col.Item().Element(c => P(c, "Die übrigen Rechte und Pflichten aus dem Arbeitsverhältnis sowie die gesetzlichen Bestimmungen des Arbeitsgesetzes bleiben durch diese Erklärung unberührt.", sizeText));

                    // ── Unterschriften — exakt wie der Arbeitsvertrag. Labels höher,
                    //    grösserer Unterschriftsraum (Walter-Vorgabe 20.06.2026). ──
                    col.Item().PaddingTop(14).ShowEntire().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Der Arbeitgeber:").Bold();
                            c.Item().PaddingTop(2).Text(string.IsNullOrWhiteSpace(d.ArbeitgeberOrt)
                                ? DateTime.Now.ToString("dd.MM.yyyy")
                                : $"{d.ArbeitgeberOrt}, {DateTime.Now:dd.MM.yyyy}");
                            c.Item().PaddingTop(110).PaddingRight(20).LineHorizontal(0.5f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text(d.UnterzeichnerName ?? "");
                            c.Item().Text(d.UnterzeichnerFunktion ?? "");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Der Mitarbeiter:").Bold();
                            c.Item().PaddingTop(2).Text("Ort und Datum:");
                            c.Item().PaddingTop(110).PaddingRight(20).LineHorizontal(0.5f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text(d.MaName ?? "");
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    // Label fett + mehrzeiliger Wert (Name / Strasse / PLZ Ort / geb. …).
    private static void FieldBlock(IContainer c, string label, System.Collections.Generic.List<string> lines, float size) =>
        c.Row(r =>
        {
            r.ConstantItem(115).Text(label).SemiBold().FontSize(size);
            r.RelativeItem().Column(col =>
            {
                if (lines.Count == 0)
                    col.Item().PaddingBottom(2).LineHorizontal(0.6f).LineColor(Dark);
                else
                    foreach (var ln in lines) col.Item().Text(ln).FontSize(size);
            });
        });

    private static void T(IContainer c, string title, float size) =>
        c.PaddingTop(9).Text(title).Bold().FontSize(size);

    private static void P(IContainer c, string text, float size) =>
        c.PaddingTop(2).Text(text).FontSize(size);
}
