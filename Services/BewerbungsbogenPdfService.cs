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
    string? Telefon,
    string? Email = null);

public class BewerbungsbogenPdfService
{
    // OneCrew / Liquid-Glass-Palette (Print)
    private const string Ink = "#3f3f3f";
    private const string Body = "#646464";
    private const string Muted = "#8b8b8b";
    private const string Soft = "#f6f3ee";
    // Schreiblinien wie Probezeitgespräch (Walter 28.07.2026).
    private const string Line = "#9a958c";
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

    private static void ApplyPageChrome(PageDescriptor page, bool withBanner)
    {
        page.Size(PageSizes.A4);
        page.PageColor(Colors.White);
        page.MarginTop(withBanner ? 0.9f : 1.2f, Unit.Centimetre);
        page.MarginBottom(0.7f, Unit.Centimetre);
        page.MarginHorizontal(1.5f, Unit.Centimetre);
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9f).FontColor(Ink).LineHeight(1.2f));

        // Gelber Logo-Balken nur auf Seite 1 (Walter 28.07.2026).
        if (!withBanner) return;

        page.Header().Height(36).Layers(layers =>
        {
            layers.Layer().Image(BannerBytes).FitWidth();
            layers.PrimaryLayer()
                .PaddingHorizontal(12)
                .PaddingTop(9)
                .Text("Bewerbungsbogen").Bold().FontSize(11f).FontColor(Ink);
        });
    }

    private static void ComposePage1(PageDescriptor page, BewerbungsbogenInput d)
    {
        ApplyPageChrome(page, withBanner: true);

        page.Content().PaddingTop(6).Column(col =>
        {
            // Adresse unter dem Balken — ruhig, eine Zeile Meta.
            var titel = string.IsNullOrWhiteSpace(d.RestaurantName)
                ? d.CompanyName
                : $"{d.CompanyName} · {d.RestaurantName}";
            col.Item().Text(titel).SemiBold().FontSize(9.5f).FontColor(Ink);
            var meta = string.Join("  ·  ", new[] { d.Strasse, d.PlzOrt, d.Telefon, d.Email }
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
            // Geburtsort/Heimatort entfernt (Walter 13.08.2026) — dafür die
            // AHV-Nummer als Ziffern-Boxen 756·XXXX·XXXX·XX (besser lesbar
            // bei Handausfüllung).
            col.Item().PaddingTop(12).Row(r =>
            {
                r.RelativeItem().Element(e => YesNoInline(e, "Quellensteuerpflichtig?"));
                r.ConstantItem(16);
                r.RelativeItem(1.4f).AlignBottom().Element(AhvBoxes);
            });
            // Geschlecht zum Ankreuzen W/M/D, Zivilstand in der Mitte,
            // «seit dem:» dahinter (Walter 13.08.2026).
            col.Item().PaddingTop(12).Row(r =>
            {
                r.RelativeItem(1.0f).Element(e => CheckOptionsInline(e, "Geschlecht", "W", "M", "D"));
                r.ConstantItem(16);
                r.RelativeItem(1.1f).AlignBottom().Element(f => LabeledLine(f, "Zivilstand"));
                r.ConstantItem(16);
                r.RelativeItem(0.9f).AlignBottom().Element(f => LabeledLine(f, "seit dem:"));
            });
            // Konfession zum Ankreuzen — gleiche Werte wie MA-Stammdaten
            // (Walter 03.08.2026).
            col.Item().PaddingTop(12).Element(e => CheckOptionsInline(e, "Konfession",
                "Evang.-reformiert", "Röm.-katholisch", "Christ-katholisch", "Andere", "Keine"));
            col.Item().PaddingTop(12).Element(e => LabeledLine(e, "Anzahl Kinder"));
            col.Item().PaddingTop(12).Element(e => LabeledLine(e, "Namen, Geburtstag der Kinder"));
            col.Item().PaddingTop(12).Element(e =>
                LabeledLine(e, "Bewilligung / Ausweis (nur für Ausländer)"));

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
        ApplyPageChrome(page, withBanner: false);

        page.Content().PaddingTop(2).Column(col =>
        {
            col.Item().Element(e =>
                SectionHead(e, "Verfügbarkeit & Eintritt",
                    "08.00–01.00 · Fr/Sa bis 03.00 Uhr"));
            col.Item().PaddingTop(6).Element(AvailabilityTable);
            col.Item().PaddingTop(10).Element(e =>
                TwoFields(e, "Frühestes Eintrittsdatum", "Für eine Dauer von mindestens"));

            col.Item().PaddingTop(10).Element(e =>
                SectionHead(e, "Angaben über Partner", null));
            col.Item().PaddingTop(6).Element(e => TwoFields(e, "Name", "Vorname"));
            col.Item().PaddingTop(8).Element(e => TwoFields(e, "Geschlecht Partner", "Geburtsort"));
            col.Item().PaddingTop(8).Element(e => LabeledLine(e, "Aufenthaltsort"));
            col.Item().PaddingTop(8).Row(r =>
            {
                r.RelativeItem().Element(e => YesNoInline(e, "Arbeitet Partner?"));
                r.ConstantItem(16);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "Ausweis"));
            });
            col.Item().PaddingTop(8).Element(e =>
                LabeledLine(e, "Arbeitgeber Partner, Adresse"));

            col.Item().PaddingTop(10).Element(e => SectionHead(e, "Ergänzende Angaben", null));
            col.Item().PaddingTop(6).Element(e => LabeledLine(e, "Krankenkasse"));
            col.Item().PaddingTop(8).Element(e => TwoFields(e, "Bank", "Kontonummer / IBAN"));
            col.Item().PaddingTop(8).Element(e => TwoFields(e, "Bankadresse", "Clearing-Nr."));

            col.Item().PaddingTop(8).Text("Haben Sie schon einmal bei McDonald's gearbeitet?")
                .FontSize(8.5f).FontColor(Body);
            col.Item().PaddingTop(4).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "Ja"));
                r.ConstantItem(10);
                r.RelativeItem().Element(f => LabeledLine(f, "Ort"));
                r.ConstantItem(10);
                r.RelativeItem().Element(f => LabeledLine(f, "als"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "Nein"));
            });

            col.Item().PaddingTop(8).Element(e => LabeledLine(e, "Welche Angestellten kennen Sie?"));

            col.Item().PaddingTop(8).Text(
                    "Leiden Sie an einer chronischen Krankheit oder an einem Hautleiden?")
                .FontSize(8.5f).FontColor(Body);
            col.Item().PaddingTop(4).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "Ja"));
                r.ConstantItem(10);
                r.RelativeItem().AlignMiddle().Element(WriteLine);
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "Nein"));
            });

            col.Item().PaddingTop(7).Element(e => YesNoRow(e, "Besteht Schwangerschaft?"));
            col.Item().PaddingTop(5).Element(e => YesNoRow(e, "Sind Sie vorbestraft?"));
            col.Item().PaddingTop(5).Element(e => YesNoRow(e, "Sind Sie bevormundet?"));
            col.Item().PaddingTop(7).Element(e => LabeledLine(e, "Nächste militärische Verpflichtung"));

            col.Item().PaddingTop(8).Element(e => SectionHead(e, "Allgemeine Bedingungen", null));
            col.Item().PaddingTop(3).Background(Soft).PaddingVertical(5).PaddingHorizontal(9).Column(c =>
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
                    c.Item().PaddingBottom(1).Row(r =>
                    {
                        r.ConstantItem(10).AlignTop().Text("–").FontSize(8.5f).FontColor(Muted);
                        r.RelativeItem().Text(line).FontSize(6.5f).FontColor(Body);
                    });
                }
            });

            col.Item().PaddingTop(4).Text(
                    "Der Bewerber / die Bewerberin nimmt zur Kenntnis, dass es sich beim vorliegenden Formular um kein Anstellungsversprechen handelt. Er / sie verpflichtet sich, den Bewerbungsbogen wahrheitsgetreu und nach bestem Wissen auszufüllen. Unwahre oder irreführende Angaben können die Ungültigkeit der Anstellung zur Folge haben.")
                .FontSize(6.5f).FontColor(Muted).Italic();

            // Mehr Platz fuer Unterschrift (Walter 28.07.2026).
            col.Item().PaddingTop(14).Row(r =>
            {
                r.RelativeItem().Element(f => SignatureLine(f, "Ort, Datum"));
                r.ConstantItem(20);
                r.RelativeItem().Element(f => SignatureLine(f, "Unterschrift"));
            });
            col.Item().PaddingTop(14).Element(e =>
                SignatureLine(e, "Unterschrift des gesetzlichen Vertreters"));
        });
    }

    // ─── Building blocks ───────────────────────────────────────────────

    private static void SectionHead(IContainer e, string title, string? hint)
    {
        // Linksbündig, fett — ohne Hintergrund/Unterstreich (Walter 28.07.2026).
        e.AlignLeft().Row(r =>
        {
            r.AutoItem().AlignMiddle().Text(title).Bold().FontSize(11f).FontColor(Ink);
            if (!string.IsNullOrWhiteSpace(hint))
            {
                r.ConstantItem(10);
                r.AutoItem().AlignMiddle().Text(hint!).FontSize(7.5f).FontColor(Muted).Italic();
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
            r.AutoItem().AlignMiddle().Text(label).FontSize(8.5f).FontColor(Ink);
        });
    }

    /// <summary>
    /// Schreibzeile wie Probezeitgespräch: feste Hoehe, durchgezogene
    /// BorderBottom-Linie (#9a958c, 0.55pt) — Walter 28.07.2026.
    /// </summary>
    private static void WriteLine(IContainer e) => WriteLineAt(e, 16f);

    private static void WriteLineAt(IContainer e, float height)
    {
        // Wie ProbezeitberichtPdfService.HandLineSlot — kein SVG-Punktmuster.
        e.Height(height).AlignBottom()
            .BorderBottom(0.55f).BorderColor(Line)
            .Text(" ");
    }

    private static void LabeledLine(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(2)
                .Text(label).FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(8);
            r.RelativeItem().Element(WriteLine);
        });
    }

    /// <summary>Extra-hohe Schreibzeile fuer Unterschriften.</summary>
    private static void SignatureLine(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(2)
                .Text(label).FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(8);
            r.RelativeItem().Element(f => WriteLineAt(f, 24f));
        });
    }

    /// <summary>
    /// AHV-Nummer als Ziffern-Kästchen in den Gruppen 3·4·4·2 (756.XXXX.XXXX.XX)
    /// — Walter 13.08.2026, bessere Lesbarkeit bei Handausfüllung.
    /// </summary>
    private static void AhvBoxes(IContainer e)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(3)
                .Text("AHV-Nummer").FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(8);
            int[] gruppen = { 3, 4, 4, 2 };
            for (var g = 0; g < gruppen.Length; g++)
            {
                if (g > 0) r.ConstantItem(7);
                for (var i = 0; i < gruppen[g]; i++)
                {
                    if (i > 0) r.ConstantItem(2);
                    r.ConstantItem(14).Element(b => b
                        .Height(17).Border(0.8f).BorderColor(Line).Text(" "));
                }
            }
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
            c.Item().Text(label).FontSize(8.5f).FontColor(Ink);
            c.Item().PaddingTop(5).Row(x =>
            {
                x.AutoItem().Element(ch => CheckLabel(ch, "ja"));
                x.ConstantItem(14);
                x.AutoItem().Element(ch => CheckLabel(ch, "nein"));
            });
        });
    }

    /// <summary>
    /// Label + Reihe Ankreuzfelder (z.B. Konfession). Gleiches Look wie
    /// Quellensteuerpflichtig ja/nein — Walter 03.08.2026.
    /// </summary>
    private static void CheckOptionsInline(IContainer e, string label, params string[] options)
    {
        e.Column(c =>
        {
            c.Item().Text(label).FontSize(8.5f).FontColor(Ink);
            c.Item().PaddingTop(5).Row(x =>
            {
                for (var i = 0; i < options.Length; i++)
                {
                    if (i > 0) x.ConstantItem(12);
                    x.AutoItem().Element(ch => CheckLabel(ch, options[i]));
                }
            });
        });
    }

    private static void YesNoRow(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(label).FontSize(8.5f).FontColor(Ink);
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
                        .FontSize(8f).FontColor(Ink);
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
                t.Cell().PaddingBottom(4).AlignCenter().Text(h).FontSize(7.5f).FontColor(Ink);

            void LangRow(string name, bool free = false)
            {
                if (free)
                    t.Cell().PaddingVertical(6).PaddingRight(8).Element(WriteLine);
                else
                    t.Cell().PaddingVertical(6).AlignMiddle().Text(name).FontSize(8.5f).FontColor(Ink);
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
        // Etwas groesser (Walter 28.07.2026) — bessere Handausfuellung.
        var days = new[] { "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag", "Sonntag" };
        e.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                foreach (var _ in days) c.RelativeColumn();
            });

            foreach (var day in days)
            {
                t.Cell().Border(0.6f).BorderColor(Rule).Background(Soft)
                    .PaddingVertical(6).PaddingHorizontal(2)
                    .AlignCenter().Text(day).SemiBold().FontSize(8f).FontColor(Ink);
            }

            foreach (var _ in days)
            {
                t.Cell().Border(0.6f).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(3).Row(r =>
                {
                    r.RelativeItem().AlignCenter().Text("von").FontSize(7.5f).FontColor(Ink);
                    r.RelativeItem().AlignCenter().Text("bis").FontSize(7.5f).FontColor(Ink);
                });
            }

            foreach (var _ in days)
            {
                t.Cell().Border(0.6f).BorderColor(Rule).PaddingVertical(12).PaddingHorizontal(4).Row(r =>
                {
                    r.RelativeItem().Element(f => WriteLineAt(f, 20f));
                    r.ConstantItem(4);
                    r.RelativeItem().Element(f => WriteLineAt(f, 20f));
                });
            }
        });
    }
}
