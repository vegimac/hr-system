using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Blanko-Bewerbungsbogen als PDF (Walter 27./28.07.2026).
/// Moderner, übersichtlicher Aufbau — alle Felder aus dem bisherigen
/// «Bewerbungsbogen neu.doc» bleiben erhalten. 2 Seiten A4.
/// </summary>
public record BewerbungsbogenInput(
    string CompanyName,
    string? RestaurantName,
    string? Strasse,
    string? PlzOrt,
    string? Telefon);

public class BewerbungsbogenPdfService
{
    private const string Dark = "#27251F";
    private const string Gold = "#FFC72C";
    private const string GoldSoft = "#FFF6D6";
    private const string Soft = "#F5F2EC";
    private const string Muted = "#6B7280";
    private const string Line = "#9AA3B2";
    private const string Body = "#3F3F3F";
    private const string CardBorder = "#E5E0D6";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public byte[] Generate(BewerbungsbogenInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(container =>
        {
            container.Page(page => ComposePage1(page, d));
            container.Page(page => ComposePage2(page));
        }).GeneratePdf();
    }

    private static void ApplyPageChrome(PageDescriptor page, string pageHint)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(0.85f, Unit.Centimetre);
        page.MarginBottom(0.55f, Unit.Centimetre);
        page.MarginHorizontal(1.3f, Unit.Centimetre);
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9f).FontColor(Dark).LineHeight(1.12f));

        page.Header().Height(34).Layers(layers =>
        {
            layers.Layer().Image(BannerBytes).FitWidth();
            layers.PrimaryLayer()
                .PaddingHorizontal(12)
                .PaddingTop(8)
                .Row(r =>
                {
                    r.RelativeItem().Text("Bewerbungsbogen").Bold().FontSize(11.5f).FontColor(Dark);
                    r.AutoItem().AlignMiddle().Text(pageHint).FontSize(7.5f).FontColor(Dark);
                });
        });
    }

    private static void ComposePage1(PageDescriptor page, BewerbungsbogenInput d)
    {
        ApplyPageChrome(page, "Seite 1 / 2");

        page.Content().PaddingTop(5).Column(col =>
        {
            col.Item().Background(Soft).PaddingVertical(5).PaddingHorizontal(9).Column(c =>
            {
                var titel = string.IsNullOrWhiteSpace(d.RestaurantName)
                    ? d.CompanyName
                    : $"{d.CompanyName} · {d.RestaurantName}";
                c.Item().Text(titel).Bold().FontSize(9f);
                var line2 = string.Join("  ·  ", new[] { d.Strasse, d.PlzOrt, d.Telefon }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
                if (!string.IsNullOrWhiteSpace(line2))
                    c.Item().PaddingTop(1).Text(line2).FontSize(7.5f).FontColor(Muted);
            });

            col.Item().PaddingTop(8).Element(e =>
                SectionHead(e, "01", "Personalien", "Bitte in Blockschrift ausfüllen"));

            col.Item().PaddingTop(4).Element(Card).Column(c =>
            {
                c.Item().Element(e => TwoFields(e, "Name", "Vorname"));
                c.Item().PaddingTop(6).Element(e => TwoFields(e, "Adresse", "E-Mail"));
                c.Item().PaddingTop(6).Element(e => TwoFields(e, "PLZ, Ort", "Tel."));
                c.Item().PaddingTop(6).Element(e => TwoFields(e, "Geburtsdatum", "Nationalität"));
                c.Item().PaddingTop(6).Element(e => TwoFields(e, "Geburtsort", "Heimatort"));
                c.Item().PaddingTop(6).Row(r =>
                {
                    r.RelativeItem().Element(e => YesNoInline(e, "Quellensteuerpflichtig?"));
                    r.ConstantItem(12);
                    r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "AHV-Nummer"));
                });
                c.Item().PaddingTop(6).Element(e => TwoFields(e, "Zivilstand", "Anzahl Kinder"));
                c.Item().PaddingTop(6).Element(e => LabeledLine(e, "Namen, Geburtstag der Kinder"));
                c.Item().PaddingTop(6).Row(r =>
                {
                    r.AutoItem().AlignMiddle().Text("Ausweis (nur für Ausländer)").FontSize(8f).FontColor(Body);
                    r.ConstantItem(12);
                    r.AutoItem().Element(e => CheckLabel(e, "B"));
                    r.ConstantItem(8);
                    r.AutoItem().Element(e => CheckLabel(e, "C"));
                });
            });

            col.Item().PaddingTop(8).Element(e =>
                SectionHead(e, "02", "Schulen / Berufserfahrung", null));

            col.Item().PaddingTop(4).Element(Card).Column(c =>
            {
                c.Item().Element(e =>
                    OpenLinesTable(e, new[] { "Schule", "Ort", "von", "bis" },
                        new[] { 2.4f, 1.4f, 0.7f, 0.7f }, 3));
                c.Item().PaddingTop(8).Element(e =>
                    OpenLinesTable(e, new[] { "Bisherige Arbeitgeber", "tätig als", "von", "bis" },
                        new[] { 2.4f, 1.4f, 0.7f, 0.7f }, 3));
                c.Item().PaddingTop(7).Element(e => LabeledLine(e, "Wo dürfen Referenzen eingeholt werden?"));
            });

            col.Item().PaddingTop(8).Element(e =>
                SectionHead(e, "03", "Sprachkenntnisse", null));
            col.Item().PaddingTop(4).Element(Card).Element(LangGrid);

            col.Item().PaddingTop(8).Element(e =>
                SectionHead(e, "04", "Verfügbarkeit & Eintritt",
                    "Öffnungszeiten 08.00–01.00 · Fr/Sa bis 03.00 Uhr"));
            col.Item().PaddingTop(4).Element(Card).Column(c =>
            {
                c.Item().Element(AvailabilityTable);
                c.Item().PaddingTop(7).Element(e =>
                    TwoFields(e, "Frühestes Eintrittsdatum", "Für eine Dauer von mindestens"));
            });
        });
    }

    private static void ComposePage2(PageDescriptor page)
    {
        ApplyPageChrome(page, "Seite 2 / 2");

        page.Content().PaddingTop(6).Column(col =>
        {
            col.Item().Element(e => SectionHead(e, "05", "Angaben über den Ehepartner", null));
            col.Item().PaddingTop(4).Element(Card).Column(c =>
            {
                c.Item().Element(e => TwoFields(e, "Name", "Vorname"));
                c.Item().PaddingTop(7).Element(e => TwoFields(e, "Geburtsort", "Aufenthaltsort"));
                c.Item().PaddingTop(7).Row(r =>
                {
                    r.RelativeItem().Element(e => YesNoInline(e, "Arbeitet Ehemann / Ehefrau?"));
                    r.ConstantItem(12);
                    r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "Ausweis"));
                });
                c.Item().PaddingTop(7).Element(e => LabeledLine(e, "Arbeitgeber des Ehepartners, Adresse"));
            });

            col.Item().PaddingTop(10).Element(e => SectionHead(e, "06", "Ergänzende Angaben", null));
            col.Item().PaddingTop(4).Element(Card).Column(c =>
            {
                c.Item().Element(e => LabeledLine(e, "Krankenkasse"));
                c.Item().PaddingTop(7).Element(e => TwoFields(e, "Bank", "Kontonummer / IBAN"));
                c.Item().PaddingTop(7).Element(e => TwoFields(e, "Bankadresse", "Clearing-Nr."));

                c.Item().PaddingTop(9).Text("Haben Sie schon einmal bei McDonald's gearbeitet?")
                    .FontSize(8f).FontColor(Body);
                c.Item().PaddingTop(4).Row(r =>
                {
                    r.AutoItem().Element(e => CheckLabel(e, "Ja"));
                    r.ConstantItem(8);
                    r.RelativeItem().Element(f => LabeledLine(f, "Ort"));
                    r.ConstantItem(8);
                    r.RelativeItem().Element(f => LabeledLine(f, "als"));
                    r.ConstantItem(10);
                    r.AutoItem().Element(e => CheckLabel(e, "Nein"));
                });

                c.Item().PaddingTop(7).Element(e => LabeledLine(e, "Welche Angestellten kennen Sie?"));

                c.Item().PaddingTop(8).Text(
                        "Leiden Sie an einer chronischen Krankheit oder an einem Hautleiden?")
                    .FontSize(8f).FontColor(Body);
                c.Item().PaddingTop(4).Row(r =>
                {
                    r.AutoItem().Element(e => CheckLabel(e, "Ja"));
                    r.ConstantItem(8);
                    r.RelativeItem().AlignMiddle().Element(DottedFill);
                    r.ConstantItem(10);
                    r.AutoItem().Element(e => CheckLabel(e, "Nein"));
                });

                c.Item().PaddingTop(7).Element(e => YesNoRow(e, "Besteht Schwangerschaft?"));
                c.Item().PaddingTop(5).Element(e => YesNoRow(e, "Sind Sie vorbestraft?"));
                c.Item().PaddingTop(5).Element(e => YesNoRow(e, "Sind Sie bevormundet?"));
                c.Item().PaddingTop(7).Element(e => LabeledLine(e, "Nächste militärische Verpflichtung"));
            });

            col.Item().PaddingTop(10).Element(e => SectionHead(e, "07", "Allgemeine Bedingungen", null));
            col.Item().PaddingTop(4).Background(GoldSoft)
                .BorderLeft(3).BorderColor(Gold)
                .PaddingVertical(7).PaddingHorizontal(9).Column(c =>
                {
                    foreach (var line in new[]
                    {
                        "Aussehen: Haare kragenlang bzw. zusammengebunden, sauber rasiert, diskretes Make-up, kein Nagellack.",
                        "Es müssen schwarze, geschlossene Schuhe getragen werden.",
                        "Die vereinbarten Arbeitszeiten können frühestens nach 4 Monaten geändert werden.",
                        "Für Teilzeit-Angestellte richtet sich die wöchentliche Arbeitszeit nach den Bedürfnissen des Arbeitgebers und ist — innerhalb der vereinbarten Arbeitszeiten — variabel.",
                        "Jugendliche bis zum vollendeten 18. Altersjahr dürfen bis spätestens 22.00 Uhr arbeiten.",
                    })
                    {
                        c.Item().PaddingBottom(2.5f).Row(r =>
                        {
                            r.ConstantItem(10).AlignTop().Text("–").FontSize(8.5f).FontColor(Dark);
                            r.RelativeItem().Text(line).FontSize(7.5f).FontColor(Body);
                        });
                    }
                });

            col.Item().PaddingTop(7).Text(
                    "Der Bewerber / die Bewerberin nimmt zur Kenntnis, dass es sich beim vorliegenden Formular um kein Anstellungsversprechen handelt. Er / sie verpflichtet sich, den Bewerbungsbogen wahrheitsgetreu und nach bestem Wissen auszufüllen. Unwahre oder irreführende Angaben können die Ungültigkeit der Anstellung zur Folge haben.")
                .FontSize(7f).FontColor(Muted).Italic();

            col.Item().PaddingTop(12).Element(Card).Column(c =>
            {
                c.Item().Row(r =>
                {
                    r.RelativeItem().Element(f => LabeledLine(f, "Ort, Datum"));
                    r.ConstantItem(16);
                    r.RelativeItem().Element(f => LabeledLine(f, "Unterschrift"));
                });
                c.Item().PaddingTop(10).Element(e =>
                    LabeledLine(e, "Unterschrift des gesetzlichen Vertreters"));
            });
        });
    }

    // ─── Building blocks ───────────────────────────────────────────────

    private static void SectionHead(IContainer e, string num, string title, string? hint)
    {
        e.Row(r =>
        {
            r.AutoItem().Background(Gold)
                .PaddingHorizontal(6).PaddingVertical(2.5f)
                .AlignMiddle().Text(num).Bold().FontSize(7.5f).FontColor(Dark);
            r.ConstantItem(7);
            r.AutoItem().AlignMiddle().Text(title).Bold().FontSize(10f).FontColor(Dark);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                r.ConstantItem(8);
                r.RelativeItem().AlignMiddle().Text(hint!).FontSize(7f).FontColor(Muted).Italic();
            }
            else
            {
                r.RelativeItem();
            }
        });
    }

    private static IContainer Card(IContainer e) =>
        e.Background(Colors.White)
            .Border(0.6f).BorderColor(CardBorder)
            .PaddingVertical(7).PaddingHorizontal(9);

    private static void Check(IContainer e) =>
        e.Width(10).Height(10).Border(1f).BorderColor(Dark);

    private static void CheckLabel(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().Element(Check);
            r.ConstantItem(4);
            r.AutoItem().AlignMiddle().Text(label).FontSize(8f).FontColor(Body);
        });
    }

    private static void DottedFill(IContainer e)
    {
        e.Height(12).AlignBottom().PaddingBottom(1).Height(2.4f).Svg(size =>
        {
            var w = size.Width.ToString("0.###", CultureInfo.InvariantCulture);
            return
                $"<svg width=\"{w}\" height=\"3\" viewBox=\"0 0 {w} 3\" xmlns=\"http://www.w3.org/2000/svg\">" +
                $"<line x1=\"0\" y1=\"2\" x2=\"{w}\" y2=\"2\" stroke=\"{Line}\" stroke-width=\"0.85\" " +
                "stroke-dasharray=\"1 1.8\" stroke-linecap=\"round\"/></svg>";
        });
    }

    private static void LabeledLine(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(1)
                .Text(label).FontSize(8f).FontColor(Body);
            r.ConstantItem(5);
            r.RelativeItem().Element(DottedFill);
        });
    }

    private static void TwoFields(IContainer e, string left, string right)
    {
        e.Row(r =>
        {
            r.RelativeItem().Element(f => LabeledLine(f, left));
            r.ConstantItem(12);
            r.RelativeItem().Element(f => LabeledLine(f, right));
        });
    }

    private static void YesNoInline(IContainer e, string label)
    {
        e.Column(c =>
        {
            c.Item().Text(label).FontSize(8f).FontColor(Body);
            c.Item().PaddingTop(3).Row(x =>
            {
                x.AutoItem().Element(ch => CheckLabel(ch, "ja"));
                x.ConstantItem(10);
                x.AutoItem().Element(ch => CheckLabel(ch, "nein"));
            });
        });
    }

    private static void YesNoRow(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(label).FontSize(8f).FontColor(Body);
            r.AutoItem().Element(ch => CheckLabel(ch, "Ja"));
            r.ConstantItem(10);
            r.AutoItem().Element(ch => CheckLabel(ch, "Nein"));
        });
    }

    private static void OpenLinesTable(IContainer e, string[] headers, float[] weights, int emptyRows)
    {
        e.Column(col =>
        {
            col.Item().Background(Soft).PaddingVertical(3).PaddingHorizontal(5).Row(r =>
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    if (i > 0) r.ConstantItem(8);
                    r.RelativeItem(weights[i]).Text(headers[i])
                        .FontSize(7.5f).SemiBold().FontColor(Muted);
                }
            });
            for (var row = 0; row < emptyRows; row++)
            {
                col.Item().PaddingTop(row == 0 ? 5 : 7).PaddingHorizontal(5).Row(r =>
                {
                    for (var i = 0; i < headers.Length; i++)
                    {
                        if (i > 0) r.ConstantItem(8);
                        r.RelativeItem(weights[i]).Element(DottedFill);
                    }
                });
            }
        });
    }

    private static void LangGrid(IContainer e)
    {
        e.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.5f);
                c.RelativeColumn(0.9f);
                c.RelativeColumn(0.7f);
                c.RelativeColumn(1.1f);
            });

            t.Cell().PaddingBottom(3).Text("");
            foreach (var h in new[] { "sehr gut", "gut", "Grundkenntnisse" })
                t.Cell().PaddingBottom(3).AlignCenter().Text(h).FontSize(6.5f).SemiBold().FontColor(Muted);

            void LangRow(string name, bool free = false)
            {
                if (free)
                    t.Cell().PaddingVertical(3).PaddingRight(4).Element(DottedFill);
                else
                    t.Cell().PaddingVertical(3).AlignMiddle().Text(name).FontSize(8f);
                for (var i = 0; i < 3; i++)
                    t.Cell().PaddingVertical(3).AlignCenter().Element(Check);
            }
            LangRow("Deutsch");
            LangRow("Englisch");
            LangRow("Französisch");
            LangRow("", free: true);
        });
    }

    private static void AvailabilityTable(IContainer e)
    {
        var days = new[] { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };
        e.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                foreach (var _ in days) c.RelativeColumn();
            });

            foreach (var day in days)
            {
                t.Cell().Padding(1.5f).Element(cell =>
                    cell.Background(Gold).PaddingVertical(3)
                        .AlignCenter().Text(day).Bold().FontSize(7.5f).FontColor(Dark));
            }

            foreach (var _ in days)
            {
                t.Cell().Padding(1.5f).Element(cell =>
                    cell.Background(Soft).PaddingVertical(4).PaddingHorizontal(3).Column(c =>
                    {
                        c.Item().Row(r =>
                        {
                            r.RelativeItem().AlignCenter().Text("von").FontSize(5.5f).FontColor(Muted);
                            r.RelativeItem().AlignCenter().Text("bis").FontSize(5.5f).FontColor(Muted);
                        });
                        c.Item().PaddingTop(2).Row(r =>
                        {
                            r.RelativeItem().Element(DottedFill);
                            r.ConstantItem(2);
                            r.RelativeItem().Element(DottedFill);
                        });
                    }));
            }
        });
    }
}
