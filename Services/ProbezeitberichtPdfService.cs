using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Probezeit Gespräch / Gesprächsprotokoll (Walter 20.07.2026, gekürzt 21.07.2026) —
/// 1-seitiges Formular (Schaub Restaurants GmbH).
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
            // ── Eine Seite (Walter 21.07.2026) ─────────────────────────
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    col.Item().AlignCenter().Text("Probezeit Gespräch")
                        .FontSize(16f).Bold().FontColor(Dark);
                    if (d.GespraechNr is 1 or 2)
                        col.Item().AlignCenter().PaddingTop(2)
                            .Text($"{d.GespraechNr}. Gespräch")
                            .FontSize(10.5f).FontColor(Soft);

                    // MA | Ersteller
                    col.Item().PaddingTop(10).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Mitarbeiterin / Mitarbeiter").Bold().FontSize(10f);
                            c.Item().PaddingTop(4).Element(e => FieldLine(e, "Name", d.MaNachname));
                            c.Item().PaddingTop(3).Element(e => FieldLine(e, "Vorname", d.MaVorname));
                            c.Item().PaddingTop(3).Element(e => FieldLine(e, "Abteilung", d.Abteilung ?? ""));
                            c.Item().PaddingTop(3).Element(e => FieldLine(e, "Eintritt am",
                                d.Eintritt.HasValue ? d.Eintritt.Value.ToString("dd.MM.yyyy") : ""));
                        });
                        r.ConstantItem(18);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Bericht wurde erstellt von").Bold().FontSize(10f);
                            c.Item().PaddingTop(4).Element(e => FieldLine(e, "Name", d.ErstellerNachname));
                            c.Item().PaddingTop(3).Element(e => FieldLine(e, "Vorname", d.ErstellerVorname));
                            c.Item().PaddingTop(3).Element(e => FieldLine(e, "Funktion", d.ErstellerFunktion ?? ""));
                            c.Item().PaddingTop(3).Element(e => FieldLine(e, "Tel.", d.ErstellerTelefon ?? ""));
                        });
                    });

                    // 1. Zufriedenheit — kein Trennstrich darüber (Walter 21.07.2026)
                    col.Item().PaddingTop(12).Text("1.  Zufriedenheitsgrad des/der Mitarbeitenden")
                        .Bold().FontSize(11f);
                    col.Item().PaddingTop(3).Text(
                            "Wie beurteilen Sie den Zufriedenheitsgrad des/der neuen Mitarbeitenden nach den ersten 2 Monaten seit Eintritt in ihren Verantwortungsbereich?")
                        .FontSize(9f).FontColor(Soft);
                    col.Item().PaddingTop(6).Element(e => RatingRow(e, ratings));
                    col.Item().PaddingTop(8).Text("Begründung:").FontSize(9.5f).Bold();
                    col.Item().Element(e => WriteSpace(e, 2));

                    col.Item().Element(SectionRule);

                    // 2. Erste Beurteilung (früher Punkt 4 — Punkte 2/3/5 entfernt, Walter 21.07.2026)
                    col.Item().PaddingTop(8).Text("2.  Erste Beurteilung").Bold().FontSize(11f);
                    col.Item().PaddingTop(8).Element(e => BeurteilungsZeile(e, "a)  Arbeitsleistung: – Qualität", ratings));
                    col.Item().PaddingTop(6).Element(e => BeurteilungsZeile(e, "– Quantität", ratings, indent: true));
                    col.Item().PaddingTop(6).Element(e => BeurteilungsZeile(e, "b)  Persönliches Verhalten", ratings));
                    col.Item().PaddingTop(6).Element(e => BeurteilungsZeile(e, "c)  Integration ins Team", ratings));
                    col.Item().PaddingTop(6).Element(e => BeurteilungsZeile(e, "d)  Gesamtbeurteilung", ratings));
                    col.Item().PaddingTop(8).Text("Bemerkungen:").FontSize(9.5f).Bold();
                    col.Item().Element(e => WriteSpace(e, 2));

                    col.Item().Element(SectionRule);

                    // 3. Gespräch — keine Unterschriftsstriche (Walter 20.07.2026):
                    // rechts Vor-/Nachname MA (kein «Unterschrift Mitarbeitende»).
                    col.Item().PaddingTop(8).Text("3.  Gespräch mit der Mitarbeiterin/dem Mitarbeiter geführt am:")
                        .Bold().FontSize(11f);
                    // Ort ohne Titel-Label (Walter 20.07.2026) — nur der Ortsname.
                    col.Item().PaddingTop(10).Row(r =>
                    {
                        r.RelativeItem().PaddingTop(4).MinHeight(20)
                            .Text(string.IsNullOrWhiteSpace(d.GespraechOrt) ? " " : d.GespraechOrt)
                            .FontSize(11.5f).Bold();
                        r.ConstantItem(20);
                        r.RelativeItem().Element(e => SoftField(e, "Datum",
                            d.GespraechAm.HasValue ? d.GespraechAm.Value.ToString("dd.MM.yyyy") : ""));
                    });

                    var maName = $"{d.MaVorname} {d.MaNachname}".Trim();
                    col.Item().PaddingTop(22).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().MinHeight(28);
                            c.Item().Text("Unterschrift der/des Vorgesetzten").FontSize(9f).FontColor(Soft);
                        });
                        r.ConstantItem(28);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().MinHeight(28).AlignBottom()
                                .Text(maName).FontSize(11.5f).Bold();
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void SetupPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(1.0f, Unit.Centimetre);
        page.MarginBottom(1.0f, Unit.Centimetre);
        page.MarginHorizontal(1.6f, Unit.Centimetre);
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10f).LineHeight(1.2f).FontColor(Dark));
    }

    private static void FieldLine(IContainer c, string label, string value)
    {
        c.Row(r =>
        {
            r.ConstantItem(88).AlignMiddle().Text(label).FontSize(9.5f).FontColor(Soft);
            r.RelativeItem().AlignMiddle().BorderBottom(0.7f).BorderColor(Line)
                .PaddingBottom(2).MinHeight(15)
                .Text(value ?? "").FontSize(10.5f).Bold();
        });
    }

    /// <summary>
    /// Einheitlicher Schreibzeilen-Abstand (Walter 20.07.2026):
    /// Jede Zeile = genau HandLinePitch Schreibraum mit Linie am unteren Rand.
    /// </summary>
    private const float HandLinePitch = 20f;

    /// <summary>
    /// Eine Schreibzeile: fester Slot (HandLinePitch) mit Linie unten —
    /// Abstand Linie-zu-Linie ist damit überall identisch.
    /// </summary>
    private static void HandLineSlot(IContainer c, string? value = null)
    {
        c.Height(HandLinePitch).AlignBottom()
            .BorderBottom(0.55f).BorderColor(Line)
            .Text(string.IsNullOrWhiteSpace(value) ? " " : value)
            .FontSize(10.5f).Bold();
    }

    /// <summary>
    /// Abschnitts-Trenner im gleichen Raster wie Schreibzeilen.
    /// </summary>
    private static void SectionRule(IContainer c)
    {
        c.PaddingTop(HandLinePitch).LineHorizontal(0.7f).LineColor(Line);
    }

    /// <summary>Freitext-Bereich ohne Unterschriftsstriche — nur Luft zum Schreiben.</summary>
    private static void SoftField(IContainer c, string label, string value)
    {
        c.Column(col =>
        {
            col.Item().Text(label).FontSize(9.5f).FontColor(Soft);
            col.Item().PaddingTop(3).MinHeight(20)
                .Text(string.IsNullOrWhiteSpace(value) ? " " : value)
                .FontSize(11.5f).Bold();
        });
    }

    /// <summary>
    /// Schreibzeilen für Handschrift: N gleiche Slots (exakt HandLinePitch).
    /// </summary>
    private static void WriteSpace(IContainer c, int lines)
    {
        c.Column(col =>
        {
            for (var i = 0; i < lines; i++)
                col.Item().Element(e => HandLineSlot(e));
        });
    }

    private static void RatingRow(IContainer c, string[] ratings)
    {
        // Gleiche Spaltenbreiten → Checkboxen aller Zeilen übereinander ausgerichtet.
        c.Row(r =>
        {
            foreach (var label in ratings)
            {
                r.RelativeItem().Element(e => CheckboxLabel(e, label));
            }
        });
    }

    private static void CheckboxLabel(IContainer c, string label)
    {
        c.Row(r =>
        {
            r.ConstantItem(16).AlignMiddle().Element(box =>
                box.Width(11).Height(11).Border(1.0f).BorderColor(Dark));
            r.RelativeItem().AlignMiddle().PaddingLeft(4).Text(label).FontSize(9.5f);
        });
    }

    /// <summary>
    /// Beurteilungszeile: feste Label-Spalte + Rating-Spalten (nie mit einrücken),
    /// damit «sehr gut / gut / …» in allen Zeilen vertikal fluchten.
    /// </summary>
    private static void BeurteilungsZeile(IContainer c, string title, string[] ratings, bool indent = false)
    {
        const float labelW = 200f;
        c.Row(r =>
        {
            r.ConstantItem(labelW).AlignMiddle().PaddingLeft(indent ? 14 : 0)
                .Text(title).FontSize(10f).Bold();
            r.RelativeItem().AlignMiddle().Element(e => RatingRow(e, ratings));
        });
    }
}
