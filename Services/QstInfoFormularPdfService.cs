using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Blanko-Formular «Quellensteuer-Informationen» (Walter-Vorgabe 23.08.2026).
/// Ersetzt das alte Mirus-Formular von 2021 — sammelt ALLE Angaben, die das
/// kantonale Anmeldeformular (z.B. AG «Anmeldeformular für quellensteuer-
/// pflichtige Arbeitnehmende») und die OneCrew-QST-Erfassung brauchen:
/// Zivilstand, Konfession, Grenzgänger/Wochenaufenthalt, Nebenerwerb,
/// Ersatzeinkünfte, Ehepartner komplett, Kinder mit Haushalt/Erstausbildung,
/// Abklärung Elterntarif (Halbfamilie H). OneCrew-Stil wie der
/// Bewerbungsbogen (BewerbungsbogenPdfService) — Handausfüllung, 2 Seiten A4.
/// Druck aus McAdmin (Blanko pro Filiale, kein MA-Bezug nötig).
/// </summary>
public record QstInfoFormularInput(
    string CompanyName,
    string? RestaurantName,
    string? Strasse,
    string? PlzOrt,
    string? Telefon,
    string? Email = null);

public class QstInfoFormularPdfService
{
    // OneCrew / Liquid-Glass-Palette (Print) — identisch Bewerbungsbogen.
    private const string Ink = "#3f3f3f";
    private const string Muted = "#8b8b8b";
    private const string Line = "#9a958c";
    private const string Rule = "#d4d0c8";
    private const string Soft = "#f6f3ee";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public byte[] Generate(QstInfoFormularInput d)
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
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(8.5f).FontColor(Ink).LineHeight(1.2f));

        if (!withBanner) return;
        page.Header().Height(36).Layers(layers =>
        {
            layers.Layer().Image(BannerBytes).FitWidth();
            layers.PrimaryLayer()
                .PaddingHorizontal(12)
                .PaddingTop(9)
                .Text("Quellensteuer-Informationen").Bold().FontSize(11f).FontColor(Ink);
        });
    }

    // ── Seite 1: Person · Nebenerwerb · Ersatzeinkünfte ─────────────────────
    private static void ComposePage1(PageDescriptor page, QstInfoFormularInput d)
    {
        ApplyPageChrome(page, withBanner: true);
        page.Content().PaddingTop(6).Column(col =>
        {
            var titel = string.IsNullOrWhiteSpace(d.RestaurantName)
                ? d.CompanyName : $"{d.CompanyName} · {d.RestaurantName}";
            col.Item().Text(titel).SemiBold().FontSize(9.5f).FontColor(Ink);
            var meta = string.Join("  ·  ", new[] { d.Strasse, d.PlzOrt, d.Telefon, d.Email }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (meta.Length > 0)
                col.Item().Text(meta).FontSize(7.5f).FontColor(Muted);

            col.Item().PaddingTop(6).Background(Soft).Padding(7).Text(
                "Bitte vollständig ausfüllen, unterschreiben und innert 8 Tagen an die HR-Abteilung " +
                "retournieren. Die Angaben dienen der Festlegung des Quellensteuer-Tarifs " +
                "(Kreisschreiben 45). Ohne vollständige Angaben muss der höchste Tarif angewendet werden.")
                .FontSize(7.5f).FontColor(Ink);

            col.Item().PaddingTop(6).Row(r =>
            {
                r.RelativeItem(3).Element(e => LabeledLine(e, "Gültig ab (Monat/Jahr)"));
                r.ConstantItem(14);
                r.RelativeItem(4).Element(e => LabeledLine(e, "Personalnummer"));
            });

            // 1 · Person
            col.Item().PaddingTop(8).Element(e => SectionHead(e, "1 · Angaben zur Person", null));
            col.Item().PaddingTop(2).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Name, Vorname"));
                r.ConstantItem(14);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Geburtsdatum"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(4).Element(e => LabeledLine(e, "AHV-Nr. (756.…)"));
                r.ConstantItem(14);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Nationalität"));
                r.ConstantItem(14);
                r.RelativeItem(2).Element(e => LabeledLine(e, "Bewilligung (B/L/…)"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Strasse / Nr."));
                r.ConstantItem(14);
                r.RelativeItem(4).Element(e => LabeledLine(e, "PLZ / Ort / Kanton"));
            });
            col.Item().PaddingTop(3).Element(e => LabeledLine(e, "Beruf / Funktion"));

            col.Item().PaddingTop(6).Text("Zivilstand").SemiBold().FontSize(8.5f);
            col.Item().PaddingTop(2).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "ledig"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "verheiratet"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "eingetragene Partnerschaft"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "geschieden"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "verwitwet"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "getrennt lebend"));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Zivilstand seit (Datum)"));
                r.ConstantItem(14);
                r.RelativeItem(3).Element(e => LabeledLine(e, "gerichtlich getrennt seit"));
            });

            col.Item().PaddingTop(6).Text("Konfession").SemiBold().FontSize(8.5f);
            col.Item().PaddingTop(2).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "evang.-reformiert"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "röm.-katholisch"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "christ-katholisch"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "andere"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "keine"));
            });

            col.Item().PaddingTop(6).Text("Wohnsitz / Aufenthalt").SemiBold().FontSize(8.5f);
            col.Item().PaddingTop(2).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "Grenzgänger/in mit täglicher Rückkehr"));
                r.ConstantItem(16);
                r.AutoItem().Element(e => CheckLabel(e, "Wochenaufenthalter/in mit wöchentlicher Rückkehr"));
            });
            col.Item().PaddingTop(3).Element(e =>
                LabeledLine(e, "Bei Wohnsitz im Ausland: Land + Adresse  /  bei Wochenaufenthalt: Aufenthaltsadresse in der Schweiz"));

            // 2 · Weitere Erwerbstätigkeit
            col.Item().PaddingTop(9).Element(e =>
                SectionHead(e, "2 · Weitere Erwerbstätigkeit", "bezieht sich auf die Schweiz UND das Ausland"));
            col.Item().PaddingTop(2).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Gehen Sie einer weiteren Erwerbstätigkeit nach?").FontSize(8.5f);
                r.ConstantItem(14);
                r.AutoItem().Element(e => CheckLabel(e, "Ja"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "Nein"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(3).Element(e => LabeledLine(e, "seit wann"));
                r.ConstantItem(14);
                r.RelativeItem(3).Element(e => LabeledLine(e, "bis wann (falls befristet)"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Arbeitgeber (Name)"));
                r.ConstantItem(14);
                r.RelativeItem(4).Element(e => LabeledLine(e, "Strasse / Nr."));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(4).Element(e => LabeledLine(e, "PLZ / Ort / Kanton"));
                r.ConstantItem(14);
                r.RelativeItem(2).Element(e => LabeledLine(e, "Land"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(2).Element(e => LabeledLine(e, "Pensum dort (%)"));
                r.ConstantItem(14);
                r.AutoItem().AlignBottom().PaddingBottom(1).Element(e => CheckLabel(e, "Pensum nicht ermittelbar"));
                r.ConstantItem(14);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Bruttolohn dort (CHF/Monat)"));
            });
            col.Item().PaddingTop(3).Element(e =>
                LabeledLine(e, "Gesamtpensum ALLER Erwerbstätigkeiten (in % oder Std./Woche)"));

            // 3 · Ersatzeinkünfte
            col.Item().PaddingTop(9).Element(e =>
                SectionHead(e, "3 · Ersatzeinkünfte", "Taggelder IV/UV/ALV/KTG, Renten, Kapitalleistungen"));
            col.Item().PaddingTop(2).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Erhalten Sie Ersatzeinkünfte?").FontSize(8.5f);
                r.ConstantItem(14);
                r.AutoItem().Element(e => CheckLabel(e, "Ja"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "Nein"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Art (z.B. IV / UV / ALV / KTG / Rente)"));
                r.ConstantItem(14);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Betrag (CHF/Monat)"));
            });

            col.Item().PaddingTop(8).Text("→ Verheiratet oder in eingetragener Partnerschaft? Bitte Seite 2, Abschnitt 4 ausfüllen. Kinder? Abschnitt 5 + 6.")
                .FontSize(7.5f).Italic().FontColor(Muted);
        });
        page.Footer().AlignRight().Text("Seite 1 / 2").FontSize(7f).FontColor(Muted);
    }

    // ── Seite 2: Ehepartner · Kinder · Elterntarif · Unterschrift ───────────
    private static void ComposePage2(PageDescriptor page)
    {
        ApplyPageChrome(page, withBanner: false);
        page.Content().Column(col =>
        {
            // 4 · Ehepartner
            col.Item().Element(e => SectionHead(e,
                "4 · Ehepartner/in bzw. eingetragene/r Partner/in",
                "nur ausfüllen bei verheiratet / eingetragener Partnerschaft"));
            col.Item().PaddingTop(2).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Name, Vorname"));
                r.ConstantItem(14);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Geburtsdatum"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(4).Element(e => LabeledLine(e, "AHV-Nr. (756.…)"));
                r.ConstantItem(14);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Nationalität"));
                r.ConstantItem(14);
                r.RelativeItem(2).Element(e => LabeledLine(e, "Bewilligung (B/C/…)"));
            });
            col.Item().PaddingTop(3).Element(e =>
                LabeledLine(e, "Adresse (nur falls abweichend vom Mitarbeitenden — Strasse, PLZ, Ort, Land)"));
            col.Item().PaddingTop(4).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Geht Ihr/e Partner/in einer Erwerbstätigkeit nach?").FontSize(8.5f);
                r.ConstantItem(14);
                r.AutoItem().Element(e => CheckLabel(e, "Ja"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "Nein"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Arbeitgeber (Name)"));
                r.ConstantItem(14);
                r.RelativeItem(4).Element(e => LabeledLine(e, "Strasse / Nr."));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.RelativeItem(4).Element(e => LabeledLine(e, "PLZ / Ort / Kanton"));
                r.ConstantItem(14);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Stellenantritt (Datum)"));
            });
            col.Item().PaddingTop(3).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "Haupterwerb"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "Nebenerwerb"));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Arbeitskanton"));
            });

            // 5 · Kinder
            col.Item().PaddingTop(9).Element(e => SectionHead(e, "5 · Kinder",
                "alle Kinder unter 18 sowie volljährige Kinder in Erstausbildung"));
            col.Item().PaddingTop(3).Element(KinderTabelle);
            col.Item().PaddingTop(4).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Kinder-/Ausbildungszulagen werden bezogen durch:").FontSize(8.5f);
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "mich"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "anderen Elternteil"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "niemanden"));
            });

            // 6 · Abklärung Elterntarif (H)
            col.Item().PaddingTop(9).Element(e => SectionHead(e, "6 · Abklärung Elterntarif",
                "nur ausfüllen bei Zivilstand ledig / geschieden / verwitwet / getrennt UND Kindern"));
            col.Item().PaddingTop(2).Element(e => JaNeinFrage(e, "Leben Sie mit Kindern im gleichen Haushalt?  (wenn Ja: Anzahl ______ )"));
            col.Item().PaddingTop(2).Element(e => JaNeinFrage(e, "Leben Sie im Konkubinat?"));
            col.Item().PaddingTop(2).Element(e => JaNeinFrage(e, "Üben Sie die elterliche Sorge gemeinsam aus?"));
            col.Item().PaddingTop(2).Element(e => JaNeinFrage(e, "Zahlen Sie Unterhalt für volljährige Kinder?"));
            col.Item().PaddingTop(2).Element(e => JaNeinFrage(e, "Erzielen Sie das höhere Bruttoeinkommen als der/die Konkubinatspartner/in?"));

            // Info + Meldepflicht
            col.Item().PaddingTop(9).Background(Soft).Padding(8).Column(info =>
            {
                info.Item().Text("Wichtig").Bold().FontSize(8f).FontColor(Ink);
                info.Item().PaddingTop(2).Text(
                    "Quellensteuer-relevante Änderungen (Zivilstand, Konfession, Kinder, Aufnahme oder " +
                    "Aufgabe einer Erwerbstätigkeit — auch des Partners/der Partnerin, Ersatzeinkünfte, " +
                    "Wohnsitzwechsel) sind der HR-Abteilung umgehend zu melden. Werden Pensum oder Lohn " +
                    "der weiteren Tätigkeit nicht angegeben, wird satzbestimmend auf 100 % umgerechnet.")
                    .FontSize(7.5f).FontColor(Ink);
            });

            col.Item().PaddingTop(14).Row(r =>
            {
                r.RelativeItem(4).Element(e => SignatureLine(e, "Ort und Datum"));
                r.ConstantItem(24);
                r.RelativeItem(5).Element(e => SignatureLine(e, "Unterschrift Mitarbeiter/in"));
            });
        });
        page.Footer().AlignRight().Text("Seite 2 / 2").FontSize(7f).FontColor(Muted);
    }

    // ── Bausteine (identische Machart wie BewerbungsbogenPdfService) ────────
    private static void SectionHead(IContainer e, string title, string? hint)
    {
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
        e.Width(11).Height(11).Border(1f).BorderColor(Ink);

    private static void CheckLabel(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().Element(Check);
            r.ConstantItem(5);
            r.AutoItem().AlignMiddle().Text(label).FontSize(8.5f).FontColor(Ink);
        });
    }

    private static void JaNeinFrage(IContainer e, string frage)
    {
        e.Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(frage).FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(12);
            r.AutoItem().Element(f => CheckLabel(f, "Ja"));
            r.ConstantItem(12);
            r.AutoItem().Element(f => CheckLabel(f, "Nein"));
        });
    }

    private static void WriteLine(IContainer e) => WriteLineAt(e, 16f);

    private static void WriteLineAt(IContainer e, float height)
    {
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

    private static void SignatureLine(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(2)
                .Text(label).FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(8);
            r.RelativeItem().Element(f => WriteLineAt(f, 38f));
        });
    }

    /// <summary>Kinder-Tabelle: 5 leere Zeilen mit Haushalt/Erstausbildung.</summary>
    private static void KinderTabelle(IContainer e)
    {
        e.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);   // Name Vorname
                c.RelativeColumn(2);   // Geburtsdatum
                c.RelativeColumn(2.4f); // gleicher Haushalt
                c.RelativeColumn(2.6f); // Erstausbildung ab 18
            });
            void Head(string txt) => t.Cell().Background(Soft).BorderBottom(0.7f).BorderColor(Rule)
                .Padding(3).Text(txt).SemiBold().FontSize(7.5f).FontColor(Ink);
            Head("Name, Vorname");
            Head("Geburtsdatum");
            Head("im gleichen Haushalt?");
            Head("ab 18: in Erstausbildung?");
            for (var i = 0; i < 5; i++)
            {
                t.Cell().Padding(2).Element(WriteLine);
                t.Cell().Padding(2).Element(WriteLine);
                t.Cell().Padding(2).AlignMiddle().Row(r =>
                {
                    r.AutoItem().Element(f => CheckLabel(f, "Ja"));
                    r.ConstantItem(8);
                    r.AutoItem().Element(f => CheckLabel(f, "Nein"));
                });
                t.Cell().Padding(2).AlignMiddle().Row(r =>
                {
                    r.AutoItem().Element(f => CheckLabel(f, "Ja"));
                    r.ConstantItem(8);
                    r.AutoItem().Element(f => CheckLabel(f, "Nein"));
                });
            }
        });
    }
}
