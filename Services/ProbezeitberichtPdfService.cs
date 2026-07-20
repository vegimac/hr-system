using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Probezeitbericht / Gesprächsprotokoll (Walter 20.07.2026) — 2-seitiges
/// Formular nach Vorlage «PZ-… Probezeitbericht» (Schaub Restaurants GmbH).
/// Ablauf: generieren → ausfüllen/unterschreiben → Scan als Dokument
/// «Probezeitgespräch» verknüpfen.
/// </summary>
public record ProbezeitberichtInput(
    string CompanyName,
    string? RestaurantName,
    string MaNachname,
    string MaVorname,
    string? Abteilung,
    DateTime? Eintritt,
    string ErstellerNachname,
    string ErstellerVorname,
    string? ErstellerFunktion,
    string? ErstellerTelefon,   // Filial-Telefon (CompanyProfile.Phone)
    DateTime? GespraechAm,
    string? GespraechOrt,
    int GespraechNr                 // 1 oder 2 — nur Label im Titel
);

public class ProbezeitberichtPdfService
{
    private const string Dark = "#27251F";
    private const string Soft = "#6b6152";
    private const string Line = "#9a958c";

    public byte[] Generate(ProbezeitberichtInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var ratings = new[] { "sehr gut", "gut", "genügend", "ungenügend" };

        return Document.Create(container =>
        {
            // ── Seite 1 ────────────────────────────────────────────────
            container.Page(page =>
            {
                SetupPage(page);
                page.Header().Element(h => PageHeader(h, d));
                page.Footer().AlignRight().Text("1/2").FontSize(9f).FontColor(Soft);
                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Item().AlignCenter().Text("Probezeitbericht")
                        .FontSize(18f).Bold().FontColor(Dark);
                    if (d.GespraechNr is 1 or 2)
                        col.Item().AlignCenter().PaddingTop(2)
                            .Text($"{d.GespraechNr}. Gespräch")
                            .FontSize(11f).FontColor(Soft);

                    // MA | Ersteller
                    col.Item().PaddingTop(14).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Mitarbeiterin / Mitarbeiter").Bold().FontSize(10.5f);
                            c.Item().PaddingTop(6).Element(e => FieldLine(e, "Name", d.MaNachname));
                            c.Item().PaddingTop(5).Element(e => FieldLine(e, "Vorname", d.MaVorname));
                            c.Item().PaddingTop(5).Element(e => FieldLine(e, "Abteilung", d.Abteilung ?? ""));
                            c.Item().PaddingTop(5).Element(e => FieldLine(e, "Eintritt am",
                                d.Eintritt.HasValue ? d.Eintritt.Value.ToString("dd.MM.yyyy") : ""));
                        });
                        r.ConstantItem(18);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Bericht wurde erstellt von").Bold().FontSize(10.5f);
                            c.Item().PaddingTop(6).Element(e => FieldLine(e, "Name", d.ErstellerNachname));
                            c.Item().PaddingTop(5).Element(e => FieldLine(e, "Vorname", d.ErstellerVorname));
                            c.Item().PaddingTop(5).Element(e => FieldLine(e, "Funktion", d.ErstellerFunktion ?? ""));
                            c.Item().PaddingTop(5).Element(e => FieldLine(e, "Tel.", d.ErstellerTelefon ?? ""));
                        });
                    });

                    col.Item().PaddingTop(12).LineHorizontal(0.8f).LineColor(Line);

                    // 1. Zufriedenheit
                    col.Item().PaddingTop(10).Text("1.  Zufriedenheitsgrad des/der Mitarbeitenden")
                        .Bold().FontSize(11.5f);
                    col.Item().PaddingTop(4).Text(
                            "Wie beurteilen Sie den Zufriedenheitsgrad des/der neuen Mitarbeitenden nach den ersten 2 Monaten seit Eintritt in ihren Verantwortungsbereich?")
                        .FontSize(9.5f).FontColor(Soft);
                    col.Item().PaddingTop(8).Element(e => RatingRow(e, ratings));
                    col.Item().PaddingTop(10).Text("Begründung:").FontSize(10f).Bold();
                    col.Item().Element(e => WriteSpace(e, 3));

                    col.Item().PaddingTop(14).LineHorizontal(0.7f).LineColor(Line);

                    // 2. Zielsetzungen
                    col.Item().PaddingTop(10).Text("2.  Wurden die Zielsetzungen für die Einführungszeit erfüllt?")
                        .Bold().FontSize(11.5f);
                    col.Item().PaddingTop(10).Element(e => WriteField(e, "Zielsetzung", ""));
                    col.Item().PaddingTop(10).Element(e => CheckboxRow(e,
                        new[] { "erfüllt", "teilweise erfüllt", "nicht erfüllt" }));
                    col.Item().PaddingTop(10).Text("Begründung:").FontSize(10f).Bold();
                    col.Item().Element(e => WriteSpace(e, 3));

                    col.Item().PaddingTop(14).LineHorizontal(0.7f).LineColor(Line);

                    // 3. Module
                    col.Item().PaddingTop(10).Text("3.  abgeschlossene Module").Bold().FontSize(11.5f);
                    col.Item().PaddingTop(8).Element(e => ModuleLine(e, "Sicherheitsinformationsblatt"));
                    col.Item().PaddingTop(6).Element(e => ModuleLine(e, "Lebensmittelhygiene"));
                    col.Item().PaddingTop(6).Element(e => ModuleLine(e, "Diskriminierung & Arbeitssicherheit"));
                    col.Item().PaddingTop(8).Text("Die aufgeführten Module müssen zwingend abgeschlossen sein.")
                        .FontSize(9f).Italic().FontColor(Soft);
                });
            });

            // ── Seite 2 ────────────────────────────────────────────────
            container.Page(page =>
            {
                SetupPage(page);
                page.Header().Element(h => PageHeader(h, d));
                page.Footer().AlignRight().Text("2/2").FontSize(9f).FontColor(Soft);
                page.Content().PaddingTop(10).Column(col =>
                {
                    // 4. Erste Beurteilung
                    col.Item().Text("4.  Erste Beurteilung").Bold().FontSize(11.5f);
                    col.Item().PaddingTop(10).Element(e => BeurteilungsZeile(e, "a)  Arbeitsleistung: – Qualität", ratings));
                    col.Item().PaddingTop(8).Element(e => BeurteilungsZeile(e, "– Quantität", ratings, indent: true));
                    col.Item().PaddingTop(8).Element(e => BeurteilungsZeile(e, "b)  Persönliches Verhalten", ratings));
                    col.Item().PaddingTop(8).Element(e => BeurteilungsZeile(e, "c)  Integration ins Team", ratings));
                    col.Item().PaddingTop(10).Element(e => BeurteilungsZeile(e, "d)  Gesamtbeurteilung", ratings));
                    col.Item().PaddingTop(12).Text("Bemerkungen:").FontSize(10f).Bold();
                    col.Item().Element(e => WriteSpace(e, 3));

                    col.Item().PaddingTop(14).LineHorizontal(0.7f).LineColor(Line);

                    // 5. Zielvereinbarung — gleiche Schreibraum-Logik wie Begründung
                    // (Luft über der Linie, unterste Zeile nicht enger).
                    col.Item().PaddingTop(12).Text("5.  Zielvereinbarung für die laufende Beurteilungsperiode")
                        .Bold().FontSize(11.5f);
                    col.Item().PaddingTop(4).Element(e => WriteField(e, "Ziel", ""));
                    col.Item().Element(e => WriteField(e, "Massnahmen", ""));
                    col.Item().Element(e => WriteField(e, "Überprüfung am", ""));
                    col.Item().Element(e => WriteField(e, "Überprüfung durch", ""));
                    col.Item().Height(26f); // unterste Zeile: gleicher Abstand nach unten

                    col.Item().PaddingTop(14).LineHorizontal(0.7f).LineColor(Line);

                    // 6. Gespräch — keine Unterschriftsstriche (Walter 20.07.2026):
                    // rechts Vor-/Nachname MA (kein «Unterschrift Mitarbeitende»).
                    col.Item().PaddingTop(14).Text("6.  Gespräch mit der Mitarbeiterin/dem Mitarbeiter geführt am:")
                        .Bold().FontSize(11.5f);
                    // Ort ohne Titel-Label (Walter 20.07.2026) — nur der Ortsname.
                    col.Item().PaddingTop(14).Row(r =>
                    {
                        r.RelativeItem().PaddingTop(4).MinHeight(22)
                            .Text(string.IsNullOrWhiteSpace(d.GespraechOrt) ? " " : d.GespraechOrt)
                            .FontSize(12f).Bold();
                        r.ConstantItem(20);
                        r.RelativeItem().Element(e => SoftField(e, "Datum",
                            d.GespraechAm.HasValue ? d.GespraechAm.Value.ToString("dd.MM.yyyy") : ""));
                    });

                    var maName = $"{d.MaVorname} {d.MaNachname}".Trim();
                    col.Item().PaddingTop(40).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().MinHeight(36);
                            c.Item().Text("Unterschrift der/des Vorgesetzten").FontSize(9.5f).FontColor(Soft);
                        });
                        r.ConstantItem(28);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().MinHeight(36).AlignBottom()
                                .Text(maName).FontSize(12f).Bold();
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void SetupPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(1.2f, Unit.Centimetre);
        page.MarginBottom(1.2f, Unit.Centimetre);
        page.MarginHorizontal(1.8f, Unit.Centimetre);
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).LineHeight(1.25f).FontColor(Dark));
    }

    private static void PageHeader(IContainer c, ProbezeitberichtInput d)
    {
        var rest = string.IsNullOrWhiteSpace(d.RestaurantName) ? "" : $" · {d.RestaurantName}";
        c.Column(col =>
        {
            col.Item().Text($"Probezeitbericht / {d.CompanyName}{rest}")
                .FontSize(9.5f).FontColor(Soft);
            col.Item().PaddingTop(3).LineHorizontal(0.9f).LineColor(Dark);
        });
    }

    private static void FieldLine(IContainer c, string label, string value)
    {
        c.Row(r =>
        {
            r.ConstantItem(88).AlignMiddle().Text(label).FontSize(10f).FontColor(Soft);
            r.RelativeItem().AlignMiddle().BorderBottom(0.7f).BorderColor(Line)
                .PaddingBottom(2).MinHeight(16)
                .Text(value ?? "").FontSize(11f).Bold();
        });
    }

    /// <summary>
    /// Handschrift-Feld wie WriteSpace: Label, dann Luft, dann Linie unten
    /// (Walter 20.07.2026 — nicht eng am Label kleben).
    /// </summary>
    private static void WriteField(IContainer c, string label, string value)
    {
        const float handHeight = 26f;
        c.Column(col =>
        {
            col.Item().Text(label).FontSize(10f).FontColor(Soft);
            col.Item().Height(handHeight).AlignBottom()
                .Text(string.IsNullOrWhiteSpace(value) ? " " : value)
                .FontSize(11f).Bold();
            col.Item().LineHorizontal(0.55f).LineColor(Line);
        });
    }

    /// <summary>Freitext-Bereich ohne Unterschriftsstriche — nur Luft zum Schreiben.</summary>
    private static void SoftField(IContainer c, string label, string value)
    {
        c.Column(col =>
        {
            col.Item().Text(label).FontSize(10f).FontColor(Soft);
            col.Item().PaddingTop(4).MinHeight(22)
                .Text(string.IsNullOrWhiteSpace(value) ? " " : value)
                .FontSize(12f).Bold();
        });
    }

    /// <summary>
    /// Schreibzeilen für Handschrift: jede Zeile = Luft (Schreiben) + Linie unten.
    /// Nach der untersten Linie dieselbe Luft wie oberhalb — sonst wirkt der
    /// Abschluss eng (Walter 20.07.2026).
    /// </summary>
    private static void WriteSpace(IContainer c, int lines)
    {
        const float handHeight = 26f; // Schreibraum oberhalb / unterhalb der Linie
        c.Column(col =>
        {
            for (var i = 0; i < lines; i++)
            {
                col.Item().Column(slot =>
                {
                    slot.Item().Height(handHeight);
                    slot.Item().LineHorizontal(0.55f).LineColor(Line);
                });
            }
            // Unterste Zeile: gleicher Abstand nach unten wie oben
            col.Item().Height(handHeight);
        });
    }

    private static void RatingRow(IContainer c, string[] ratings)
    {
        c.Row(r =>
        {
            foreach (var label in ratings)
            {
                r.RelativeItem().Element(e => CheckboxLabel(e, label));
            }
        });
    }

    private static void CheckboxRow(IContainer c, string[] labels)
    {
        c.Row(r =>
        {
            foreach (var label in labels)
                r.RelativeItem().Element(e => CheckboxLabel(e, label));
        });
    }

    private static void CheckboxLabel(IContainer c, string label)
    {
        c.Row(r =>
        {
            r.ConstantItem(16).AlignMiddle().Element(box =>
                box.Width(11).Height(11).Border(1.0f).BorderColor(Dark));
            r.RelativeItem().AlignMiddle().PaddingLeft(4).Text(label).FontSize(10f);
        });
    }

    private static void ModuleLine(IContainer c, string name)
    {
        c.Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(name).FontSize(10.5f);
            r.ConstantItem(110).AlignMiddle().Element(e => CheckboxLabel(e, "abgeschlossen"));
        });
    }

    private static void BeurteilungsZeile(IContainer c, string title, string[] ratings, bool indent = false)
    {
        c.Column(col =>
        {
            col.Item().PaddingLeft(indent ? 18 : 0).Text(title).FontSize(10.5f).Bold();
            col.Item().PaddingTop(5).PaddingLeft(indent ? 18 : 0).Element(e => RatingRow(e, ratings));
        });
    }
}
