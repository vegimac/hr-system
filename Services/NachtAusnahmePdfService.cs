using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// „Ausnahmeregelung zum Wechsel zwischen Tag- und Nachtarbeit (Anlage zum
/// Arbeitsvertrag)" (Walter-Vorgabe 22.06.2026, ArG/ArGV1 Art. 30 + SECO).
/// Verdichtetes 1-Seiten-Layout im Haus-Stil: Titel einzeilig + zentriert ÜBER
/// dem gelben Banner; Kopf zweispaltig ohne Labels (links MA, rechts Filiale),
/// alles aus dem Programm vorausgefüllt. Unterschrift ohne Strich — Name +
/// Funktion in einer Zeile, beim Arbeitgeber Ort/Datum vorausgefüllt.
/// </summary>
public class NachtAusnahmePdfService
{
    private const string Dark     = "#1a1a1a";
    private const string Grid     = "#999999";
    private const string Yellow   = "#FFBC0D";
    private const string LightYel = "#FFF4D6";

    private static readonly byte[] BannerBytes =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public record NachtAusnahmeData(
        string? MaName, string? MaStrasse, string? MaPlzOrt, string? MaGeburtsdatum,
        string? FilialeName, string? FilialeStrasse, string? FilialePlzOrt, string? FilialeTelefon,
        string? FilialeOrt,
        string? UnterzeichnerName, string? UnterzeichnerFunktion);

    private static readonly string[] Bedingungen =
    {
        "Die Mehrheit der Mitarbeiter hat schriftlich (anhand des vorliegenden Formulars) darum gebeten, auf den Wechsel zwischen Tag- und Nachtarbeit zu verzichten, da diese Wechselpflicht für sie insbesondere aus persönlichen oder familiären Gründen unvorteilhaft ist.",
        "Die Mitarbeiter wurden einer ärztlichen Pflichtuntersuchung und Beratung unterzogen (vor Einteilung zu ununterbrochener Nachtarbeit, auf Kosten des Arbeitgebers).",
        "Der Mitarbeiter wurde für diese Art von Arbeit infolge einer ärztlichen Untersuchung (alle zwei Jahre durchzuführen) für geeignet erklärt.",
        "Für Mitarbeiter, welche eine zweite Beschäftigung haben, wurde vom 2. Arbeitgeber eine Erlaubnis angefordert. Die Arbeitgeber vergewissern sich, dass der Mitarbeiter die zulässigen Grenzen (Einhaltung der Arbeitsdauer und Ruhezeiten) nicht überschreitet. Der Mitarbeiter hat zudem die Pflicht, diesbezügliche Mitteilungen unverzüglich vorzunehmen.",
        "Der Mitarbeiter arbeitet maximal an 6 aufeinander folgenden Tagen.",
        "Der Mitarbeiter ist keinen erhöhten chemischen, biologischen oder körperlichen Risiken ausgesetzt.",
        "Der Mitarbeiter ist keinem übermässigen physischen, psychischen oder geistigen Druck ausgesetzt.",
        "Der Arbeitseinsatz ist so organisiert, dass die Leistungsfähigkeit des Mitarbeiters erhalten bleibt und dadurch die Entstehung von Gefahrensituationen vermieden werden kann.",
        "Die tatsächliche Arbeitsdauer beträgt maximal 10 Stunden innerhalb eines Zeitraums von 24 Stunden.",
    };

    private const string ZusatzText =
        "Insofern es die Umstände erfordern, hat ein Arbeitgeber, der Mitarbeiter regelmässig zu " +
        "Nachtarbeit einsetzt, geeignete Zusatzmassnahmen zum Schutz der Mitarbeiter zu ergreifen. " +
        "Dies betrifft insbesondere die Sicherheit auf dem Arbeitsweg, die Transportorganisation, " +
        "die Möglichkeiten, Ruhepausen einzulegen und Nahrung zu sich zu nehmen, sowie die " +
        "Kinderbetreuung.";

    public byte[] Generate(NachtAusnahmeData d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        const float sizeText = 8.5f;

        // Kopf links: Name (+ geb.), Strasse, PLZ Ort.
        var maName = string.IsNullOrWhiteSpace(d.MaGeburtsdatum)
            ? (d.MaName ?? "")
            : $"{d.MaName}, geb. {d.MaGeburtsdatum}";
        var maLines = new[] { maName, d.MaStrasse, d.MaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        // Kopf rechts: Filiale ohne Telefon.
        var filLines = new[] { d.FilialeName, d.FilialeStrasse, d.FilialePlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

        var unterzeichner = (d.UnterzeichnerName ?? "")
            + (string.IsNullOrWhiteSpace(d.UnterzeichnerFunktion) ? "" : $", {d.UnterzeichnerFunktion}");
        var ortDatumGf = string.IsNullOrWhiteSpace(d.FilialeOrt)
            ? "Ort und Datum:"
            : $"{d.FilialeOrt}, {DateTime.Now:dd.MM.yyyy}";

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(0.88f, Unit.Centimetre);   // ~15 pt mehr oberer Rand (Walter 22.06.2026)
                page.MarginBottom(0.9f, Unit.Centimetre);
                page.MarginHorizontal(1.3f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(sizeText).FontColor(Dark));

                // Titel einzeilig + zentriert ÜBER dem Banner (rechts bleibt das „M" frei).
                page.Header().Height(38).Layers(layers =>
                {
                    layers.Layer().Image(BannerBytes).FitWidth();
                    layers.PrimaryLayer().PaddingLeft(10).PaddingRight(55).AlignMiddle().Text(t =>
                    {
                        t.AlignCenter();
                        t.Span("Ausnahmeregelung zum Wechsel zwischen Tag- und Nachtarbeit")
                            .Bold().FontSize(10f).FontColor(Dark);
                        t.Span("  (Anlage zum Arbeitsvertrag)").FontSize(8f).FontColor(Dark);
                    });
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    // ── Kopf: links Mitarbeitende, rechts Filiale (ohne Labels) ──
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Element(c => Party(c, maLines));
                        row.ConstantItem(24);
                        row.RelativeItem().Element(c => Party(c, filLines));
                    });

                    // ── Rechtlicher Rahmen / Ausnahmeregelung ──
                    col.Item().Element(c => Lead(c, "Rechtlicher Rahmen:",
                        "Durchgehende Nachtarbeit darf normalerweise den Zeitraum von 6 Wochen nicht überschreiten (innerhalb der letzten 6 Arbeitswochen darf die Anzahl der gearbeiteten Nächte 18 nicht überschreiten). ArGV1 Art. 30 – Die Nachtarbeit darf höchstens 6 aufeinander folgende Wochen dauern. Nach Ablauf dieses Zeitraums ist der Arbeitnehmer einer Tag- bzw. Abendschicht zuzuteilen."));
                    col.Item().Element(c => Lead(c, "Ausnahmeregelung:",
                        "Das SECO lässt unter bestimmten Bedingungen eine Abweichung von der Rechtsgrundlage zum Wechsel zwischen Tag- und Nachtarbeit zu. Der Arbeitgeber und der Mitarbeiter vereinbaren rechtmässig und im gegenseitigen Einvernehmen, von dieser Wechselpflicht abzuweichen. Der Mitarbeiter und der Arbeitgeber bestätigen, dass sämtliche Voraussetzungen, unter denen es dem Mitarbeiter und dem Arbeitgeber gestattet ist, regelmässige Nachtarbeit zu leisten bzw. leisten zu lassen und somit von der Wechselpflicht abzuweichen, gegeben sind. Der Arbeitgeber und der Mitarbeiter verpflichten sich zudem, für die Aufrechterhaltung und Einhaltung dieser Bedingungen Sorge zu tragen, anderenfalls kann diese Ausnahmeregelung widerrufen bzw. nichtig werden."));

                    // ── Bedingungs-Tabelle ──
                    col.Item().PaddingTop(5).Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.ConstantColumn(74);
                            c.ConstantColumn(74);
                        });
                        t.Header(h =>
                        {
                            h.Cell().Element(HeadCell).AlignMiddle().Text("Bedingungen").Bold().FontSize(8.5f);
                            h.Cell().Element(HeadCell).AlignMiddle().Text(x =>
                                { x.AlignCenter(); x.Span("Visa\nMitarbeiter").Bold().FontSize(8.5f); });
                            h.Cell().Element(HeadCell).AlignMiddle().Text(x =>
                                { x.AlignCenter(); x.Span("Visa\nArbeitgeber").Bold().FontSize(8.5f); });
                        });
                        foreach (var b in Bedingungen)
                        {
                            t.Cell().Element(BodyCell).Text(b).FontSize(8.5f);
                            t.Cell().Element(BodyCell).Text("");
                            t.Cell().Element(BodyCell).Text("");
                        }
                        t.Cell().ColumnSpan(3).Element(SubCell)
                            .Text("Zusatzmassnahmen bei Nachtarbeit").Bold().FontSize(8.5f);
                        t.Cell().Element(BodyCell).Text(ZusatzText).FontSize(8.5f);
                        t.Cell().Element(BodyCell).Text("");
                        t.Cell().Element(BodyCell).Text("");
                    });

                    // ── Schlusstext ──
                    col.Item().PaddingTop(6).Element(c => P(c,
                        "Der Mitarbeiter wurde auf die vom SECO auferlegten und in jedem Restaurant (sowie unter www.seco.ch) einsehbaren Bedingungen dieser Ausnahmeregelung ausdrücklich hingewiesen. Zusätzliche Informationen, Gesetzesbestimmungen und Richtlinien können beim Restaurant- oder Personalverantwortlichen erfragt werden."));

                    col.Item().PaddingTop(3).Text("Dem Mitarbeiter werden zusätzlich folgende Arbeitsanweisungen und Richtlinien ausgehändigt:").FontSize(sizeText);
                    col.Item().PaddingTop(1).Text(t =>
                    {
                        t.Span("•  ").FontSize(sizeText);
                        t.Span("Merkblatt Nachtarbeit ohne Wechsel").Bold().FontSize(sizeText);
                        t.Span("        •  ").FontSize(sizeText);
                        t.Span("Ärztliches Untersuchungsformular").Bold().FontSize(sizeText);
                    });

                    col.Item().PaddingTop(3).Element(c => P(c,
                        "Das vorliegende Formular sowie das ärztliche Untersuchungsformular sind während der gesamten Dauer des Arbeitsverhältnisses aufzubewahren und im Kontrollfall der kantonalen Aufsichtsbehörde zur Verfügung zu stellen."));
                    col.Item().PaddingTop(3).Element(c => P(c,
                        "Die vorliegende Ausnahmeregelung wird in zwei Exemplaren erstellt, von denen jede Partei jeweils ein Exemplar versehen mit beiden Unterschriften ausgehändigt bekommt. Die Parteien bestätigen die Richtigkeit der vorstehend genannten Punkte sowie deren Umsetzung."));

                    // ── Unterschriften (kein Strich; Name + Funktion auf einer Zeile) ──
                    col.Item().PaddingTop(10).ShowEntire().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(unterzeichner).Bold();
                            c.Item().PaddingTop(3).Text(ortDatumGf);
                            c.Item().Height(32);   // Freiraum für die Unterschrift
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(d.MaName ?? "").Bold();
                            c.Item().PaddingTop(3).Text("Ort und Datum:");
                            c.Item().Height(32);
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    // Kopf-Spalte ohne Label: nur die Angaben (Name/Strasse/Ort).
    private static void Party(IContainer c, System.Collections.Generic.List<string> lines) =>
        c.Column(col =>
        {
            if (lines.Count == 0)
                col.Item().PaddingBottom(2).LineHorizontal(0.6f).LineColor(Dark);
            else
                foreach (var ln in lines) col.Item().Text(ln).FontSize(9f);
        });

    private static IContainer HeadCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Grid).Background(Yellow).PaddingVertical(3).PaddingHorizontal(5);
    private static IContainer BodyCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Grid).PaddingVertical(3.5f).PaddingHorizontal(5);
    private static IContainer SubCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Grid).Background(LightYel).PaddingVertical(3.5f).PaddingHorizontal(5);

    private static void Lead(IContainer c, string label, string body) =>
        c.PaddingTop(6).Text(t =>
        {
            t.Justify();
            t.Span(label + " ").Bold();
            t.Span(body);
        });

    private static void P(IContainer c, string text) =>
        c.Text(t => { t.Justify(); t.Span(text); });
}
