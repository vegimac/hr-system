using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Blanko-Bewerbungsbogen als PDF (Walter 27.07.2026) — Inhalt aus dem
/// bisherigen «Bewerbungsbogen neu.doc». Ausfüllfelder: Label + feine
/// Punktlinie (Name ·····). Filialadresse aus CompanyProfile. 2 Seiten A4.
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
    private const string Muted = "#64748b";
    private const string Line = "#94a3b8";

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

    private static void ComposePage1(PageDescriptor page, BewerbungsbogenInput d)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(1.0f, Unit.Centimetre);
        page.MarginBottom(0.7f, Unit.Centimetre);
        page.MarginHorizontal(1.5f, Unit.Centimetre);
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9f).FontColor(Dark).LineHeight(1.2f));

        // Titel auf dem gelben Banner — wie Arbeitsvertrag (Walter 28.07.2026).
        page.Header().Height(38).Layers(layers =>
        {
            layers.Layer().Image(BannerBytes).FitWidth();
            layers.PrimaryLayer()
                .PaddingHorizontal(10)
                .PaddingTop(10)
                .Text("Bewerbungsbogen").Bold().FontSize(11f).FontColor(Dark);
        });

        page.Content().PaddingTop(4).Column(col =>
        {
            // Restaurant-Adresse direkt unter dem gelben Balken (Walter 28.07.2026).
            col.Item().Column(c =>
            {
                var titel = string.IsNullOrWhiteSpace(d.RestaurantName)
                    ? d.CompanyName
                    : $"{d.CompanyName} · {d.RestaurantName}";
                c.Item().Text(titel).Bold().FontSize(9.5f);
                if (!string.IsNullOrWhiteSpace(d.Strasse))
                    c.Item().Text(d.Strasse!).FontSize(9f);
                if (!string.IsNullOrWhiteSpace(d.PlzOrt))
                    c.Item().Text(d.PlzOrt!).FontSize(9f);
                if (!string.IsNullOrWhiteSpace(d.Telefon))
                    c.Item().Text(d.Telefon!).FontSize(9f);
            });

            // Personalien + Blockschrift-Hinweis in einer Zeile.
            col.Item().PaddingTop(10).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Personalien").Bold().FontSize(10f);
                r.ConstantItem(10);
                r.AutoItem().AlignMiddle().Text("Bitte in Blockschrift ausfüllen")
                    .FontSize(8f).FontColor(Muted).Italic();
            });

            col.Item().PaddingTop(7).Element(e => TwoFields(e, "Name", "Vorname"));
            col.Item().PaddingTop(7).Element(e => TwoFields(e, "Adresse", "E-Mail"));
            col.Item().PaddingTop(7).Element(e => TwoFields(e, "PLZ, Ort", "Tel."));
            col.Item().PaddingTop(7).Element(e => TwoFields(e, "Geburtsdatum", "Nationalität"));
            col.Item().PaddingTop(7).Element(e => TwoFields(e, "Geburtsort", "Heimatort"));
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text("Quellensteuerpflichtig?").FontSize(8.5f);
                    c.Item().PaddingTop(3).Row(x =>
                    {
                        x.AutoItem().Element(Check);
                        x.ConstantItem(5);
                        x.AutoItem().AlignMiddle().Text("ja").FontSize(8.5f);
                        x.ConstantItem(12);
                        x.AutoItem().Element(Check);
                        x.ConstantItem(5);
                        x.AutoItem().AlignMiddle().Text("nein").FontSize(8.5f);
                    });
                });
                r.ConstantItem(10);
                r.RelativeItem().Element(f => LabeledLine(f, "AHV-Nummer"));
            });
            col.Item().PaddingTop(7).Element(e => TwoFields(e, "Zivilstand", "Anzahl Kinder"));
            col.Item().PaddingTop(7).Element(e => LabeledLine(e, "Namen, Geburtstag der Kinder"));
            col.Item().PaddingTop(7).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Ausweis (nur für Ausländer)").FontSize(8.5f);
                r.ConstantItem(12);
                r.AutoItem().Element(Check);
                r.ConstantItem(4);
                r.AutoItem().AlignMiddle().Text("B").FontSize(8.5f);
                r.ConstantItem(10);
                r.AutoItem().Element(Check);
                r.ConstantItem(4);
                r.AutoItem().AlignMiddle().Text("C").FontSize(8.5f);
            });

            col.Item().PaddingTop(10).Element(e => SectionTitle(e, "Schulen / Berufserfahrung"));
            col.Item().PaddingTop(5).Element(e =>
                OpenLinesTable(e, new[] { "Schule", "Ort", "von", "bis" },
                    new[] { 2.4f, 1.4f, 0.7f, 0.7f }, 3));
            col.Item().PaddingTop(8).Element(e =>
                OpenLinesTable(e, new[] { "Bisherige Arbeitgeber", "tätig als", "von", "bis" },
                    new[] { 2.4f, 1.4f, 0.7f, 0.7f }, 3));
            col.Item().PaddingTop(7).Element(e => LabeledLine(e, "Wo dürfen Referenzen eingeholt werden?"));

            col.Item().PaddingTop(7).Text("Sprachkenntnisse").SemiBold().FontSize(9f);
            col.Item().PaddingTop(2).Element(LangGrid);

            col.Item().PaddingTop(7).Text(t =>
            {
                t.Span("Verfügbare Arbeitszeiten").SemiBold().FontSize(9f);
                t.Span("  (08.00 – 01.00, Fr. und Sa. 08.00 – 03.00 Uhr)")
                    .FontSize(7.5f).FontColor(Muted);
            });
            col.Item().PaddingTop(2).Element(AvailabilityTable);

            col.Item().PaddingTop(5).Element(e =>
                TwoFields(e, "Frühestes Eintrittsdatum", "Für eine Dauer von mindestens"));
        });
    }

    private static void ComposePage2(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(1.0f, Unit.Centimetre);
        page.MarginBottom(0.7f, Unit.Centimetre);
        page.MarginHorizontal(1.5f, Unit.Centimetre);
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9f).FontColor(Dark).LineHeight(1.2f));

        page.Header().Height(38).Layers(layers =>
        {
            layers.Layer().Image(BannerBytes).FitWidth();
            layers.PrimaryLayer()
                .PaddingHorizontal(10)
                .PaddingTop(10)
                .Text("Bewerbungsbogen").Bold().FontSize(11f).FontColor(Dark);
        });

        page.Content().PaddingTop(8).Column(col =>
        {
            col.Item().Element(e => SectionTitle(e, "Angaben über den Ehepartner"));
            col.Item().PaddingTop(7).Element(e => TwoFields(e, "Name", "Vorname"));
            col.Item().PaddingTop(7).Element(e => TwoFields(e, "Geburtsort", "Aufenthaltsort"));
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text("Arbeitet Ehemann / Ehefrau?").FontSize(8.5f);
                    c.Item().PaddingTop(3).Row(x =>
                    {
                        x.AutoItem().Element(Check);
                        x.ConstantItem(5);
                        x.AutoItem().AlignMiddle().Text("ja").FontSize(8.5f);
                        x.ConstantItem(12);
                        x.AutoItem().Element(Check);
                        x.ConstantItem(5);
                        x.AutoItem().AlignMiddle().Text("nein").FontSize(8.5f);
                    });
                });
                r.ConstantItem(10);
                r.RelativeItem().Element(f => LabeledLine(f, "Ausweis"));
            });
            col.Item().PaddingTop(7).Element(e => LabeledLine(e, "Arbeitgeber des Ehepartners, Adresse"));

            col.Item().PaddingTop(12).Element(e => SectionTitle(e, "Ergänzende Angaben"));
            col.Item().PaddingTop(7).Element(e => LabeledLine(e, "Krankenkasse"));
            col.Item().PaddingTop(7).Element(e => TwoFields(e, "Bank", "Kontonummer / IBAN"));
            col.Item().PaddingTop(7).Element(e => TwoFields(e, "Bankadresse", "Clearing-Nr."));

            col.Item().PaddingTop(6).Text("Haben Sie schon einmal bei McDonald's gearbeitet?").FontSize(8.5f);
            col.Item().PaddingTop(2).Row(r =>
            {
                r.AutoItem().Element(Check);
                r.ConstantItem(4);
                r.AutoItem().AlignMiddle().Text("Ja").FontSize(8.5f);
                r.ConstantItem(8);
                r.RelativeItem().Element(f => LabeledLine(f, "Ort"));
                r.ConstantItem(6);
                r.RelativeItem().Element(f => LabeledLine(f, "als"));
                r.ConstantItem(8);
                r.AutoItem().Element(Check);
                r.ConstantItem(4);
                r.AutoItem().AlignMiddle().Text("Nein").FontSize(8.5f);
            });

            col.Item().PaddingTop(4).Element(e => LabeledLine(e, "Welche Angestellten kennen Sie?"));

            col.Item().PaddingTop(5).Text("Leiden Sie an einer chronischen Krankheit oder an einem Hautleiden?").FontSize(8.5f);
            col.Item().PaddingTop(2).Row(r =>
            {
                r.AutoItem().Element(Check);
                r.ConstantItem(4);
                r.AutoItem().AlignMiddle().Text("Ja").FontSize(8.5f);
                r.ConstantItem(6);
                r.RelativeItem().AlignMiddle().Element(DottedFill);
                r.ConstantItem(8);
                r.AutoItem().Element(Check);
                r.ConstantItem(4);
                r.AutoItem().AlignMiddle().Text("Nein").FontSize(8.5f);
            });

            col.Item().PaddingTop(4).Element(e => YesNoRow(e, "Besteht Schwangerschaft?"));
            col.Item().PaddingTop(3).Element(e => YesNoRow(e, "Sind Sie vorbestraft?"));
            col.Item().PaddingTop(3).Element(e => YesNoRow(e, "Sind Sie bevormundet?"));
            col.Item().PaddingTop(3).Element(e => LabeledLine(e, "Nächste militärische Verpflichtung"));

            col.Item().PaddingTop(10).Element(e => SectionTitle(e, "Allgemeine Bedingungen"));
            col.Item().PaddingTop(4).Column(c =>
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
                        r.ConstantItem(10).Text("•").FontSize(9f);
                        r.RelativeItem().Text(line).FontSize(8f).FontColor("#3f3f3f");
                    });
                }
            });

            col.Item().PaddingTop(6).Text(
                    "Der Bewerber / die Bewerberin nimmt zur Kenntnis, dass es sich beim vorliegenden Formular um kein Anstellungsversprechen handelt. Er / sie verpflichtet sich, den Bewerbungsbogen wahrheitsgetreu und nach bestem Wissen auszufüllen. Unwahre oder irreführende Angaben können die Ungültigkeit der Anstellung zur Folge haben.")
                .FontSize(7.5f).FontColor("#3f3f3f").Italic();

            col.Item().PaddingTop(14).Row(r =>
            {
                r.RelativeItem().Element(f => LabeledLine(f, "Ort, Datum"));
                r.ConstantItem(14);
                r.RelativeItem().Element(f => LabeledLine(f, "Unterschrift"));
            });
            col.Item().PaddingTop(10).Element(e =>
                LabeledLine(e, "Unterschrift des gesetzlichen Vertreters"));
        });
    }

    private static void SectionTitle(IContainer e, string title) =>
        e.BorderBottom(0.7f).BorderColor(Dark).PaddingBottom(2)
            .Text(title).Bold().FontSize(10f);

    private static void Check(IContainer e) =>
        e.Width(10).Height(10).Border(0.9f).BorderColor(Dark);

    /// <summary>Feine gepunktete Ausfüll-Linie (SVG stroke-dasharray).</summary>
    private static void DottedFill(IContainer e)
    {
        e.Height(12).AlignBottom().Height(3).Svg(size =>
        {
            var w = size.Width.ToString("0.###", CultureInfo.InvariantCulture);
            return
                $"<svg width=\"{w}\" height=\"3\" viewBox=\"0 0 {w} 3\" xmlns=\"http://www.w3.org/2000/svg\">" +
                $"<line x1=\"0\" y1=\"2\" x2=\"{w}\" y2=\"2\" stroke=\"{Line}\" stroke-width=\"0.9\" " +
                "stroke-dasharray=\"1 1.7\" stroke-linecap=\"round\"/></svg>";
        });
    }

    /// <summary>Label und Punktlinie in einer Zeile: «Name ·······».</summary>
    private static void LabeledLine(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(1)
                .Text(label).FontSize(8.5f).FontColor(Dark);
            r.ConstantItem(6);
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

    private static void YesNoRow(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(label).FontSize(8.5f);
            r.AutoItem().Element(Check);
            r.ConstantItem(4);
            r.AutoItem().AlignMiddle().Text("Ja").FontSize(8.5f);
            r.ConstantItem(12);
            r.AutoItem().Element(Check);
            r.ConstantItem(4);
            r.AutoItem().AlignMiddle().Text("Nein").FontSize(8.5f);
        });
    }

    /// <summary>
    /// Offene Spalten ohne Gitternetz: nur Spaltenköpfe + weit auseinander
    /// liegende Punktlinien (Walter 28.07.2026 — viel weniger Striche).
    /// </summary>
    private static void OpenLinesTable(IContainer e, string[] headers, float[] weights, int emptyRows)
    {
        e.Column(col =>
        {
            col.Item().Row(r =>
            {
                for (var i = 0; i < headers.Length; i++)
                {
                    if (i > 0) r.ConstantItem(10);
                    r.RelativeItem(weights[i]).Text(headers[i])
                        .FontSize(8f).SemiBold().FontColor(Muted);
                }
            });
            for (var row = 0; row < emptyRows; row++)
            {
                col.Item().PaddingTop(row == 0 ? 4 : 10).Row(r =>
                {
                    for (var i = 0; i < headers.Length; i++)
                    {
                        if (i > 0) r.ConstantItem(10);
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
                c.RelativeColumn(1f);
                c.RelativeColumn(1f);
                c.RelativeColumn(1.2f);
            });
            t.Cell().Padding(1).Text("");
            t.Cell().Padding(1).AlignCenter().Text("sehr gut").FontSize(7.5f).FontColor(Muted);
            t.Cell().Padding(1).AlignCenter().Text("gut").FontSize(7.5f).FontColor(Muted);
            t.Cell().Padding(1).AlignCenter().Text("Grundkenntnisse").FontSize(7.5f).FontColor(Muted);

            void LangRow(string name, bool free = false)
            {
                if (free)
                    t.Cell().Padding(2).Element(DottedFill);
                else
                    t.Cell().Padding(2).AlignMiddle().Text(name).FontSize(8.5f);
                for (var i = 0; i < 3; i++)
                    t.Cell().Padding(2).AlignCenter().Element(Check);
            }
            LangRow("Deutsch");
            LangRow("Englisch");
            LangRow("Französisch");
            LangRow("", free: true);
        });
    }

    private static void AvailabilityTable(IContainer e)
    {
        var days = new[] { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" };
        e.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                foreach (var _ in days) c.RelativeColumn();
            });
            foreach (var day in days)
                t.Cell().Border(0.5f).BorderColor(Line).Background("#f6f3ee")
                    .Padding(2).AlignCenter().Text(day).FontSize(7f).SemiBold();

            foreach (var _ in days)
            {
                t.Cell().Border(0.5f).BorderColor(Line).Padding(2).Row(r =>
                {
                    r.RelativeItem().AlignCenter().Text("von").FontSize(6.5f).FontColor(Muted);
                    r.RelativeItem().AlignCenter().Text("bis").FontSize(6.5f).FontColor(Muted);
                });
            }
            foreach (var _ in days)
            {
                t.Cell().Border(0.5f).BorderColor(Line).Padding(3).Row(r =>
                {
                    r.RelativeItem().Element(DottedFill);
                    r.ConstantItem(3);
                    r.RelativeItem().Element(DottedFill);
                });
            }
        });
    }
}
