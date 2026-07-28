using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Blanko-Bewerbungsbogen als PDF (Walter 27./28.07.2026).
/// OneCrew-Stil (ruhig, monochrom) — grosszuegige Schreibzeilen fuer
/// Handausfuellung. Alle Felder aus «Bewerbungsbogen neu.doc». 2 Seiten A4.
/// </summary>
public record BewerbungsbogenInput(
    string CompanyName,
    string? RestaurantName,
    string? Strasse,
    string? PlzOrt,
    string? Telefon);

public class BewerbungsbogenPdfService
{
    // OneCrew / Liquid-Glass-Palette (Print)
    private const string Ink = "#3f3f3f";
    private const string Body = "#646464";
    private const string Muted = "#8b8b8b";
    private const string Soft = "#f6f3ee";
    private const string Line = "#b8b4ac";
    private const string Rule = "#d4d0c8";

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
        page.PageColor(Colors.White);
        page.MarginTop(0.9f, Unit.Centimetre);
        page.MarginBottom(0.7f, Unit.Centimetre);
        page.MarginHorizontal(1.5f, Unit.Centimetre);
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9f).FontColor(Ink).LineHeight(1.2f));

        // Letterhead bleibt (Firmenbogen) — Formular selbst ohne Farbakzente.
        page.Header().Height(36).Layers(layers =>
        {
            layers.Layer().Image(BannerBytes).FitWidth();
            layers.PrimaryLayer()
                .PaddingHorizontal(12)
                .PaddingTop(9)
                .Row(r =>
                {
                    r.RelativeItem().Text("Bewerbungsbogen").Bold().FontSize(11f).FontColor(Ink);
                    r.AutoItem().AlignMiddle().Text(pageHint).FontSize(7.5f).FontColor(Muted);
                });
        });
    }

    private static void ComposePage1(PageDescriptor page, BewerbungsbogenInput d)
    {
        ApplyPageChrome(page, "Seite 1 / 2");

        page.Content().PaddingTop(6).Column(col =>
        {
            // Adresse unter dem Balken — ruhig, eine Zeile Meta.
            var titel = string.IsNullOrWhiteSpace(d.RestaurantName)
                ? d.CompanyName
                : $"{d.CompanyName} · {d.RestaurantName}";
            col.Item().Text(titel).SemiBold().FontSize(9.5f).FontColor(Ink);
            var meta = string.Join("  ·  ", new[] { d.Strasse, d.PlzOrt, d.Telefon }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (!string.IsNullOrWhiteSpace(meta))
                col.Item().PaddingTop(2).Text(meta).FontSize(8f).FontColor(Muted);

            col.Item().PaddingTop(12).Element(e =>
                SectionHead(e, "Personalien", "Bitte in Blockschrift ausfüllen"));

            // Grosszuegige Schreibzeilen (Handschrift).
            col.Item().PaddingTop(10).Element(e => TwoFields(e, "Name", "Vorname"));
            col.Item().PaddingTop(12).Element(e => TwoFields(e, "Adresse", "E-Mail"));
            col.Item().PaddingTop(12).Element(e => TwoFields(e, "PLZ, Ort", "Tel."));
            col.Item().PaddingTop(12).Element(e => TwoFields(e, "Geburtsdatum", "Nationalität"));
            col.Item().PaddingTop(12).Element(e => TwoFields(e, "Geburtsort", "Heimatort"));
            col.Item().PaddingTop(12).Row(r =>
            {
                r.RelativeItem().Element(e => YesNoInline(e, "Quellensteuerpflichtig?"));
                r.ConstantItem(16);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "AHV-Nummer"));
            });
            col.Item().PaddingTop(12).Element(e => TwoFields(e, "Zivilstand", "Anzahl Kinder"));
            col.Item().PaddingTop(12).Element(e => LabeledLine(e, "Namen, Geburtstag der Kinder"));
            col.Item().PaddingTop(12).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Ausweis (nur für Ausländer)").FontSize(8.5f).FontColor(Body);
                r.ConstantItem(14);
                r.AutoItem().Element(e => CheckLabel(e, "B"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "C"));
            });

            col.Item().PaddingTop(16).Element(e => SectionHead(e, "Schulen / Berufserfahrung", null));
            col.Item().PaddingTop(10).Element(e =>
                OpenLinesTable(e, new[] { "Schule", "Ort", "von", "bis" },
                    new[] { 2.4f, 1.4f, 0.7f, 0.7f }, 2));
            col.Item().PaddingTop(14).Element(e =>
                OpenLinesTable(e, new[] { "Bisherige Arbeitgeber", "tätig als", "von", "bis" },
                    new[] { 2.4f, 1.4f, 0.7f, 0.7f }, 2));
            col.Item().PaddingTop(12).Element(e => LabeledLine(e, "Wo dürfen Referenzen eingeholt werden?"));

            col.Item().PaddingTop(16).Element(e => SectionHead(e, "Sprachkenntnisse", null));
            col.Item().PaddingTop(8).Element(LangGrid);
        });
    }

    private static void ComposePage2(PageDescriptor page)
    {
        ApplyPageChrome(page, "Seite 2 / 2");

        page.Content().PaddingTop(8).Column(col =>
        {
            col.Item().Element(e =>
                SectionHead(e, "Verfügbarkeit & Eintritt",
                    "08.00–01.00 · Fr/Sa bis 03.00 Uhr"));
            col.Item().PaddingTop(6).Element(AvailabilityTable);
            col.Item().PaddingTop(12).Element(e =>
                TwoFields(e, "Frühestes Eintrittsdatum", "Für eine Dauer von mindestens"));

            col.Item().PaddingTop(12).Element(e => SectionHead(e, "Angaben über den Ehepartner", null));
            col.Item().PaddingTop(8).Element(e => TwoFields(e, "Name", "Vorname"));
            col.Item().PaddingTop(10).Element(e => TwoFields(e, "Geburtsort", "Aufenthaltsort"));
            col.Item().PaddingTop(10).Row(r =>
            {
                r.RelativeItem().Element(e => YesNoInline(e, "Arbeitet Ehemann / Ehefrau?"));
                r.ConstantItem(16);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "Ausweis"));
            });
            col.Item().PaddingTop(10).Element(e => LabeledLine(e, "Arbeitgeber des Ehepartners, Adresse"));

            col.Item().PaddingTop(12).Element(e => SectionHead(e, "Ergänzende Angaben", null));
            col.Item().PaddingTop(8).Element(e => LabeledLine(e, "Krankenkasse"));
            col.Item().PaddingTop(10).Element(e => TwoFields(e, "Bank", "Kontonummer / IBAN"));
            col.Item().PaddingTop(10).Element(e => TwoFields(e, "Bankadresse", "Clearing-Nr."));

            col.Item().PaddingTop(10).Text("Haben Sie schon einmal bei McDonald's gearbeitet?")
                .FontSize(8.5f).FontColor(Body);
            col.Item().PaddingTop(5).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "Ja"));
                r.ConstantItem(10);
                r.RelativeItem().Element(f => LabeledLine(f, "Ort"));
                r.ConstantItem(10);
                r.RelativeItem().Element(f => LabeledLine(f, "als"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "Nein"));
            });

            col.Item().PaddingTop(10).Element(e => LabeledLine(e, "Welche Angestellten kennen Sie?"));

            col.Item().PaddingTop(10).Text(
                    "Leiden Sie an einer chronischen Krankheit oder an einem Hautleiden?")
                .FontSize(8.5f).FontColor(Body);
            col.Item().PaddingTop(5).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "Ja"));
                r.ConstantItem(10);
                r.RelativeItem().AlignMiddle().Element(WriteLine);
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "Nein"));
            });

            col.Item().PaddingTop(9).Element(e => YesNoRow(e, "Besteht Schwangerschaft?"));
            col.Item().PaddingTop(7).Element(e => YesNoRow(e, "Sind Sie vorbestraft?"));
            col.Item().PaddingTop(7).Element(e => YesNoRow(e, "Sind Sie bevormundet?"));
            col.Item().PaddingTop(9).Element(e => LabeledLine(e, "Nächste militärische Verpflichtung"));

            col.Item().PaddingTop(10).Element(e => SectionHead(e, "Allgemeine Bedingungen", null));
            col.Item().PaddingTop(4).Background(Soft).PaddingVertical(6).PaddingHorizontal(9).Column(c =>
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
                    c.Item().PaddingBottom(2).Row(r =>
                    {
                        r.ConstantItem(10).AlignTop().Text("–").FontSize(8.5f).FontColor(Muted);
                        r.RelativeItem().Text(line).FontSize(7f).FontColor(Body);
                    });
                }
            });

            col.Item().PaddingTop(5).Text(
                    "Der Bewerber / die Bewerberin nimmt zur Kenntnis, dass es sich beim vorliegenden Formular um kein Anstellungsversprechen handelt. Er / sie verpflichtet sich, den Bewerbungsbogen wahrheitsgetreu und nach bestem Wissen auszufüllen. Unwahre oder irreführende Angaben können die Ungültigkeit der Anstellung zur Folge haben.")
                .FontSize(6.5f).FontColor(Muted).Italic();

            col.Item().PaddingTop(12).Row(r =>
            {
                r.RelativeItem().Element(f => LabeledLine(f, "Ort, Datum"));
                r.ConstantItem(20);
                r.RelativeItem().Element(f => LabeledLine(f, "Unterschrift"));
            });
            col.Item().PaddingTop(12).Element(e =>
                LabeledLine(e, "Unterschrift des gesetzlichen Vertreters"));
        });
    }

    // ─── Building blocks ───────────────────────────────────────────────

    private static void SectionHead(IContainer e, string title, string? hint)
    {
        e.BorderBottom(0.7f).BorderColor(Rule).PaddingBottom(3).Row(r =>
        {
            r.AutoItem().AlignMiddle().Text(title).SemiBold().FontSize(10f).FontColor(Ink);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                r.ConstantItem(10);
                r.RelativeItem().AlignMiddle().Text(hint!).FontSize(7.5f).FontColor(Muted).Italic();
            }
            else
            {
                r.RelativeItem();
            }
        });
    }

    private static void Check(IContainer e) =>
        e.Width(12).Height(12).Border(1f).BorderColor(Ink);

    private static void CheckLabel(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().Element(Check);
            r.ConstantItem(5);
            r.AutoItem().AlignMiddle().Text(label).FontSize(8.5f).FontColor(Body);
        });
    }

    /// <summary>Hohe Schreibzeile — genug Luft fuer Handschrift.</summary>
    private static void WriteLine(IContainer e)
    {
        e.MinHeight(16).AlignBottom().PaddingBottom(1).Height(2.2f).Svg(size =>
        {
            var w = size.Width.ToString("0.###", CultureInfo.InvariantCulture);
            return
                $"<svg width=\"{w}\" height=\"3\" viewBox=\"0 0 {w} 3\" xmlns=\"http://www.w3.org/2000/svg\">" +
                $"<line x1=\"0\" y1=\"2\" x2=\"{w}\" y2=\"2\" stroke=\"{Line}\" stroke-width=\"0.8\" " +
                "stroke-dasharray=\"1 2\" stroke-linecap=\"round\"/></svg>";
        });
    }

    private static void LabeledLine(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(2)
                .Text(label).FontSize(8.5f).FontColor(Body);
            r.ConstantItem(8);
            r.RelativeItem().Element(WriteLine);
        });
    }

    private static void TwoFields(IContainer e, string left, string right)
    {
        e.Row(r =>
        {
            r.RelativeItem().Element(f => LabeledLine(f, left));
            r.ConstantItem(16);
            r.RelativeItem().Element(f => LabeledLine(f, right));
        });
    }

    private static void YesNoInline(IContainer e, string label)
    {
        e.Column(c =>
        {
            c.Item().Text(label).FontSize(8.5f).FontColor(Body);
            c.Item().PaddingTop(5).Row(x =>
            {
                x.AutoItem().Element(ch => CheckLabel(ch, "ja"));
                x.ConstantItem(14);
                x.AutoItem().Element(ch => CheckLabel(ch, "nein"));
            });
        });
    }

    private static void YesNoRow(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(label).FontSize(8.5f).FontColor(Body);
            r.AutoItem().Element(ch => CheckLabel(ch, "Ja"));
            r.ConstantItem(14);
            r.AutoItem().Element(ch => CheckLabel(ch, "Nein"));
        });
    }

    private static void OpenLinesTable(IContainer e, string[] headers, float[] weights, int emptyRows)
    {
        e.Column(col =>
        {
            col.Item().Row(r =>
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    if (i > 0) r.ConstantItem(12);
                    r.RelativeItem(weights[i]).Text(headers[i])
                        .FontSize(8f).FontColor(Muted);
                }
            });
            for (var row = 0; row < emptyRows; row++)
            {
                // Weite Zeilenabstaende fuer Handschrift.
                col.Item().PaddingTop(row == 0 ? 8 : 14).Row(r =>
                {
                    for (var i = 0; i < headers.Length; i++)
                    {
                        if (i > 0) r.ConstantItem(12);
                        r.RelativeItem(weights[i]).Element(WriteLine);
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
                c.RelativeColumn(1.6f);
                c.RelativeColumn(1f);
                c.RelativeColumn(0.85f);
                c.RelativeColumn(1.2f);
            });

            t.Cell().PaddingBottom(4).Text("");
            foreach (var h in new[] { "sehr gut", "gut", "Grundkenntnisse" })
                t.Cell().PaddingBottom(4).AlignCenter().Text(h).FontSize(7.5f).FontColor(Muted);

            void LangRow(string name, bool free = false)
            {
                if (free)
                    t.Cell().PaddingVertical(6).PaddingRight(8).Element(WriteLine);
                else
                    t.Cell().PaddingVertical(6).AlignMiddle().Text(name).FontSize(8.5f).FontColor(Body);
                for (var i = 0; i < 3; i++)
                    t.Cell().PaddingVertical(6).AlignCenter().Element(Check);
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
                t.Cell().Padding(2).Element(cell =>
                    cell.BorderBottom(0.6f).BorderColor(Rule).PaddingBottom(4)
                        .AlignCenter().Text(day).SemiBold().FontSize(8f).FontColor(Ink));
            }

            foreach (var _ in days)
            {
                t.Cell().Padding(2).PaddingTop(6).Column(c =>
                {
                    c.Item().Row(r =>
                    {
                        r.RelativeItem().AlignCenter().Text("von").FontSize(6.5f).FontColor(Muted);
                        r.RelativeItem().AlignCenter().Text("bis").FontSize(6.5f).FontColor(Muted);
                    });
                    c.Item().PaddingTop(5).Row(r =>
                    {
                        r.RelativeItem().Element(WriteLine);
                        r.ConstantItem(4);
                        r.RelativeItem().Element(WriteLine);
                    });
                });
            }
        });
    }
}
