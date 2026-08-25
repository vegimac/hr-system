using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Formular «Quellensteuer-Informationen» (Walter-Vorgabe 23.08.2026).
/// Ersetzt das alte Mirus-Formular von 2021 — sammelt ALLE Angaben, die das
/// kantonale Anmeldeformular und die OneCrew-QST-Erfassung brauchen.
/// Zwei Modi (Walter 23.08.2026 v2): BLANKO (ohne MA) oder VORBEFÜLLT mit
/// den beim MA bekannten Daten — der MA prüft/ergänzt dann nur noch.
/// Grosszügige Schreibzeilen für Handausfüllung. 2 Seiten A4, OneCrew-Stil.
/// </summary>
public record QstInfoKind(string? Name, string? Geburtsdatum, bool? Haushalt, bool? Erstausbildung);

public record QstInfoPrefill(
    string? Personalnummer = null, string? NameVorname = null, string? Geburtsdatum = null,
    string? AhvNr = null, string? Nationalitaet = null, string? Bewilligung = null,
    string? StrasseNr = null, string? PlzOrtKanton = null, string? Beruf = null,
    string? Zivilstand = null,            // ledig|verheiratet|eingetragen|geschieden|verwitwet
    bool GetrenntLebend = false, string? ZivilstandSeit = null, string? GetrenntSeit = null,
    string? Konfession = null,            // ref|rk|ck|andere|keine
    bool Grenzgaenger = false, bool Wochenaufenthalter = false, string? AuslandAdresse = null,
    bool? WeitereErwerb = null, string? GesamtPensum = null,
    string? PartnerName = null, string? PartnerGeburtsdatum = null, string? PartnerAhv = null,
    string? PartnerNationalitaet = null, string? PartnerBewilligung = null,
    string? PartnerAdresse = null, bool? PartnerErwerb = null,
    string? PartnerArbeitgeber = null, string? PartnerAgStrasse = null, string? PartnerAgOrt = null,
    string? PartnerStellenantritt = null, string? PartnerArbeitskanton = null,
    List<QstInfoKind>? Kinder = null);

public record QstInfoFormularInput(
    string CompanyName,
    string? RestaurantName,
    string? Strasse,
    string? PlzOrt,
    string? Telefon,
    string? Email = null,
    QstInfoPrefill? Prefill = null);

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
        var p = d.Prefill ?? new QstInfoPrefill();
        return Document.Create(container =>
        {
            container.Page(page => ComposePage1(page, d, p));
            container.Page(page => ComposePage2(page, p));
        }).GeneratePdf();
    }

    private static void ApplyPageChrome(PageDescriptor page, bool withBanner)
    {
        page.Size(PageSizes.A4);
        page.PageColor(Colors.White);
        page.MarginTop(withBanner ? 0.9f : 1.4f, Unit.Centimetre);
        page.MarginBottom(0.8f, Unit.Centimetre);
        page.MarginHorizontal(1.5f, Unit.Centimetre);
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9f).FontColor(Ink).LineHeight(1.25f));

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
    private static void ComposePage1(PageDescriptor page, QstInfoFormularInput d, QstInfoPrefill p)
    {
        ApplyPageChrome(page, withBanner: true);
        page.Content().PaddingTop(8).Column(col =>
        {
            var titel = string.IsNullOrWhiteSpace(d.RestaurantName)
                ? d.CompanyName : $"{d.CompanyName} · {d.RestaurantName}";
            col.Item().Text(titel).SemiBold().FontSize(9.5f).FontColor(Ink);
            var meta = string.Join("  ·  ", new[] { d.Strasse, d.PlzOrt, d.Telefon, d.Email }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            if (meta.Length > 0)
                col.Item().Text(meta).FontSize(7.5f).FontColor(Muted);

            col.Item().PaddingTop(8).Background(Soft).Padding(8).Text(
                "Bitte vollständig ausfüllen bzw. vorgedruckte Angaben prüfen, unterschreiben und " +
                "innert 8 Tagen an die HR-Abteilung retournieren. Die Angaben dienen der Festlegung " +
                "des Quellensteuer-Tarifs (Kreisschreiben 45). Ohne vollständige Angaben muss der " +
                "höchste Tarif angewendet werden.")
                .FontSize(7.8f).FontColor(Ink);

            col.Item().PaddingTop(10).Row(r =>
            {
                r.RelativeItem(3).Element(e => LabeledLine(e, "Gültig ab (Monat/Jahr)"));
                r.ConstantItem(18);
                r.RelativeItem(4).Element(e => LabeledLine(e, "Personalnummer", p.Personalnummer));
            });

            // 1 · Person
            col.Item().PaddingTop(14).Element(e => SectionHead(e, "1 · Angaben zur Person", null));
            col.Item().PaddingTop(4).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Name, Vorname", p.NameVorname));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Geburtsdatum", p.Geburtsdatum));
            });
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(4).Element(e => LabeledLine(e, "AHV-Nr.", p.AhvNr));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Nationalität", p.Nationalitaet));
                r.ConstantItem(18);
                r.RelativeItem(2).Element(e => LabeledLine(e, "Bewilligung", p.Bewilligung));
            });
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Strasse / Nr.", p.StrasseNr));
                r.ConstantItem(18);
                r.RelativeItem(4).Element(e => LabeledLine(e, "PLZ / Ort / Kanton", p.PlzOrtKanton));
            });
            col.Item().PaddingTop(7).Element(e => LabeledLine(e, "Beruf / Funktion", p.Beruf));

            var zs = (p.Zivilstand ?? "").ToLowerInvariant();
            col.Item().PaddingTop(11).Text("Zivilstand").SemiBold().FontSize(9f);
            // Alle 6 Optionen in EINER Zeile (Walter 23.08.2026): «getrennt»
            // statt «getrennt lebend», Partnerschaft abgekürzt — passt so.
            col.Item().PaddingTop(4).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "ledig", zs == "ledig"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "verheiratet", zs == "verheiratet"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "eingetr. Partnerschaft", zs == "eingetragen"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "geschieden", zs == "geschieden"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "verwitwet", zs == "verwitwet"));
                r.ConstantItem(12);
                r.AutoItem().Element(e => CheckLabel(e, "getrennt", p.GetrenntLebend));
            });
            col.Item().PaddingTop(6).Row(r =>
            {
                r.RelativeItem(3).Element(e => LabeledLine(e, "Zivilstand seit", p.ZivilstandSeit));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "getrennt seit", p.GetrenntSeit));
                r.RelativeItem(3);
            });

            var kf = (p.Konfession ?? "").ToLowerInvariant();
            col.Item().PaddingTop(11).Text("Konfession").SemiBold().FontSize(9f);
            col.Item().PaddingTop(4).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "evang.-reformiert", kf == "ref"));
                r.ConstantItem(16);
                r.AutoItem().Element(e => CheckLabel(e, "röm.-katholisch", kf == "rk"));
                r.ConstantItem(16);
                r.AutoItem().Element(e => CheckLabel(e, "christ-katholisch", kf == "ck"));
                r.ConstantItem(16);
                r.AutoItem().Element(e => CheckLabel(e, "andere", kf == "andere"));
                r.ConstantItem(16);
                r.AutoItem().Element(e => CheckLabel(e, "keine", kf == "keine"));
            });

            col.Item().PaddingTop(11).Text("Wohnsitz / Aufenthalt").SemiBold().FontSize(9f);
            col.Item().PaddingTop(4).Row(r =>
            {
                r.AutoItem().Element(e => CheckLabel(e, "Grenzgänger/in mit täglicher Rückkehr", p.Grenzgaenger));
                r.ConstantItem(20);
                r.AutoItem().Element(e => CheckLabel(e, "Wochenaufenthalter/in mit wöchentlicher Rückkehr", p.Wochenaufenthalter));
            });
            col.Item().PaddingTop(6).Element(e =>
                LabeledLine(e, "Ausland-Adresse / CH-Aufenthaltsadresse", p.AuslandAdresse));

            // 2 · Weitere Erwerbstätigkeit
            col.Item().PaddingTop(14).Element(e =>
                SectionHead(e, "2 · Weitere Erwerbstätigkeit", "bezieht sich auf die Schweiz UND das Ausland"));
            col.Item().PaddingTop(4).Element(e =>
                JaNeinFrage(e, "Gehen Sie einer weiteren Erwerbstätigkeit nach?", p.WeitereErwerb));
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(3).Element(e => LabeledLine(e, "seit wann"));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "bis wann (falls befristet)"));
            });
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Arbeitgeber (Name)"));
                r.ConstantItem(18);
                r.RelativeItem(4).Element(e => LabeledLine(e, "Strasse / Nr."));
            });
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(4).Element(e => LabeledLine(e, "PLZ / Ort / Kanton"));
                r.ConstantItem(18);
                r.RelativeItem(2).Element(e => LabeledLine(e, "Land"));
            });
            col.Item().PaddingTop(7).Row(r =>
            {
                // Bruttolohn dort ENTFERNT (Walter 25.08.2026: «das Einkommen
                // muss nicht erfasst werden, nur die Stellenprozent»).
                r.RelativeItem(2).Element(e => LabeledLine(e, "Pensum dort (%)"));
                r.ConstantItem(18);
                r.AutoItem().AlignBottom().PaddingBottom(1).Element(e => CheckLabel(e, "Pensum nicht ermittelbar", false));
                r.RelativeItem(3);
            });
            col.Item().PaddingTop(7).Element(e =>
                LabeledLine(e, "Gesamtpensum ALLER Erwerbstätigkeiten (% oder Std./Woche)", p.GesamtPensum));

            // 3 · Ersatzeinkünfte
            col.Item().PaddingTop(14).Element(e =>
                SectionHead(e, "3 · Ersatzeinkünfte", "Taggelder IV/UV/ALV/KTG, Renten, Kapitalleistungen"));
            col.Item().PaddingTop(4).Element(e =>
                JaNeinFrage(e, "Erhalten Sie Ersatzeinkünfte?", null));
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Art (z.B. IV / UV / ALV / KTG / Rente)"));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Betrag (CHF/Monat)"));
            });

            col.Item().PaddingTop(12).Text("→ Verheiratet oder in eingetragener Partnerschaft? Bitte Seite 2, Abschnitt 4 ausfüllen. Kinder? Abschnitt 5 + 6.")
                .FontSize(7.8f).Italic().FontColor(Muted);
        });
        page.Footer().AlignRight().Text("Seite 1 / 2").FontSize(7f).FontColor(Muted);
    }

    // ── Seite 2: Ehepartner · Kinder · Elterntarif · Unterschrift ───────────
    private static void ComposePage2(PageDescriptor page, QstInfoPrefill p)
    {
        ApplyPageChrome(page, withBanner: false);
        page.Content().Column(col =>
        {
            // 4 · Ehepartner
            col.Item().Element(e => SectionHead(e,
                "4 · Ehepartner/in bzw. eingetragene/r Partner/in",
                "nur ausfüllen bei verheiratet / eingetragener Partnerschaft"));
            col.Item().PaddingTop(4).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Name, Vorname", p.PartnerName));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Geburtsdatum", p.PartnerGeburtsdatum));
            });
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(4).Element(e => LabeledLine(e, "AHV-Nr.", p.PartnerAhv));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Nationalität", p.PartnerNationalitaet));
                r.ConstantItem(18);
                r.RelativeItem(2).Element(e => LabeledLine(e, "Bewilligung", p.PartnerBewilligung));
            });
            col.Item().PaddingTop(7).Element(e =>
                LabeledLine(e, "Adresse (nur falls abweichend — Strasse, PLZ, Ort, Land)", p.PartnerAdresse));
            col.Item().PaddingTop(8).Element(e =>
                JaNeinFrage(e, "Geht Ihr/e Partner/in einer Erwerbstätigkeit nach?", p.PartnerErwerb));
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(5).Element(e => LabeledLine(e, "Arbeitgeber (Name)", p.PartnerArbeitgeber));
                r.ConstantItem(18);
                r.RelativeItem(4).Element(e => LabeledLine(e, "Strasse / Nr.", p.PartnerAgStrasse));
            });
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(4).Element(e => LabeledLine(e, "PLZ / Ort / Kanton", p.PartnerAgOrt));
                r.ConstantItem(18);
                r.RelativeItem(3).Element(e => LabeledLine(e, "Stellenantritt (Datum)", p.PartnerStellenantritt));
            });
            // Haupt-/Nebenerwerb bewusst weggelassen (Walter 23.08.2026):
            // das offizielle Anmeldeformular fragt es beim Partner nicht,
            // tarifrelevant ist nur OB der Partner erwerbstätig ist.
            col.Item().PaddingTop(7).Row(r =>
            {
                r.RelativeItem(3).Element(e => LabeledLine(e, "Arbeitskanton", p.PartnerArbeitskanton));
                r.RelativeItem(6);
            });

            // 5 · Kinder
            col.Item().PaddingTop(14).Element(e => SectionHead(e, "5 · Kinder",
                "alle Kinder unter 18 sowie volljährige Kinder in Erstausbildung"));
            col.Item().PaddingTop(5).Element(e => KinderTabelle(e, p.Kinder ?? new List<QstInfoKind>()));
            col.Item().PaddingTop(7).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Kinder-/Ausbildungszulagen werden bezogen durch:").FontSize(9f);
                r.ConstantItem(14);
                r.AutoItem().Element(e => CheckLabel(e, "mich", false));
                r.ConstantItem(14);
                r.AutoItem().Element(e => CheckLabel(e, "anderen Elternteil", false));
                r.ConstantItem(14);
                r.AutoItem().Element(e => CheckLabel(e, "niemanden", false));
            });

            // 6 · Abklärung Elterntarif (H)
            col.Item().PaddingTop(14).Element(e => SectionHead(e, "6 · Abklärung Elterntarif",
                "nur ausfüllen bei Zivilstand ledig / geschieden / verwitwet / getrennt UND Kindern"));
            col.Item().PaddingTop(5).Element(e => JaNeinFrage(e, "Leben Sie mit Kindern im gleichen Haushalt?  (wenn Ja: Anzahl ______ )", null));
            col.Item().PaddingTop(6).Element(e => JaNeinFrage(e, "Leben Sie im Konkubinat?", null));
            col.Item().PaddingTop(6).Element(e => JaNeinFrage(e, "Üben Sie die elterliche Sorge gemeinsam aus?", null));
            col.Item().PaddingTop(6).Element(e => JaNeinFrage(e, "Zahlen Sie Unterhalt für volljährige Kinder?", null));
            col.Item().PaddingTop(6).Element(e => JaNeinFrage(e, "Erzielen Sie das höhere Bruttoeinkommen als der/die Konkubinatspartner/in?", null));

            // Info + Meldepflicht
            col.Item().PaddingTop(14).Background(Soft).Padding(9).Column(info =>
            {
                info.Item().Text("Wichtig").Bold().FontSize(8.5f).FontColor(Ink);
                info.Item().PaddingTop(3).Text(
                    "Quellensteuer-relevante Änderungen (Zivilstand, Konfession, Kinder, Aufnahme oder " +
                    "Aufgabe einer Erwerbstätigkeit — auch des Partners/der Partnerin, Ersatzeinkünfte, " +
                    "Wohnsitzwechsel) sind der HR-Abteilung umgehend zu melden. Werden Pensum oder Lohn " +
                    "der weiteren Tätigkeit nicht angegeben, wird satzbestimmend auf 100 % umgerechnet.")
                    .FontSize(7.8f).FontColor(Ink);
            });

            col.Item().PaddingTop(22).Row(r =>
            {
                r.RelativeItem(4).Element(e => SignatureLine(e, "Ort und Datum"));
                r.ConstantItem(28);
                r.RelativeItem(5).Element(e => SignatureLine(e, "Unterschrift Mitarbeiter/in"));
            });
        });
        page.Footer().AlignRight().Text("Seite 2 / 2").FontSize(7f).FontColor(Muted);
    }

    // ── Bausteine ────────────────────────────────────────────────────────────
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

    private static void Check(IContainer e, bool isChecked)
    {
        // Kreuz als ASCII-«X» (Walter 23.08.2026): das Unicode-✕ fehlt in
        // Arial-Print und wurde als �-Kästchen gedruckt.
        var box = e.Width(12).Height(12).Border(1f).BorderColor(Ink);
        if (isChecked) box.AlignCenter().AlignMiddle().Text("X").FontSize(9.5f).Bold().FontColor(Ink);
        else box.Text("");
    }

    private static void CheckLabel(IContainer e, string label, bool isChecked)
    {
        e.Row(r =>
        {
            r.AutoItem().Element(f => Check(f, isChecked));
            r.ConstantItem(5);
            r.AutoItem().AlignMiddle().Text(label).FontSize(9f).FontColor(Ink);
        });
    }

    private static void JaNeinFrage(IContainer e, string frage, bool? wert)
    {
        e.Row(r =>
        {
            r.RelativeItem().AlignMiddle().Text(frage).FontSize(9f).FontColor(Ink);
            r.ConstantItem(14);
            r.AutoItem().Element(f => CheckLabel(f, "Ja", wert == true));
            r.ConstantItem(14);
            r.AutoItem().Element(f => CheckLabel(f, "Nein", wert == false));
        });
    }

    /// <summary>Grosszügige Schreibzeile (22pt); Vorbefüllungs-Wert sitzt AUF der Linie.</summary>
    private static void WriteLine(IContainer e, string? value = null) => WriteLineAt(e, 22f, value);

    private static void WriteLineAt(IContainer e, float height, string? value = null)
    {
        e.Height(height).AlignBottom()
            .BorderBottom(0.55f).BorderColor(Line)
            .PaddingBottom(1)
            .Text(string.IsNullOrWhiteSpace(value) ? " " : value)
            .FontSize(9.5f).FontColor(Ink);
    }

    private static void LabeledLine(IContainer e, string label, string? value = null)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(2)
                .Text(label).FontSize(9f).FontColor(Ink);
            r.ConstantItem(8);
            r.RelativeItem().Element(f => WriteLine(f, value));
        });
    }

    private static void SignatureLine(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(2)
                .Text(label).FontSize(9f).FontColor(Ink);
            r.ConstantItem(8);
            r.RelativeItem().Element(f => WriteLineAt(f, 40f));
        });
    }

    /// <summary>Kinder-Tabelle: 5 Zeilen, bekannte Kinder vorgedruckt.</summary>
    private static void KinderTabelle(IContainer e, List<QstInfoKind> kinder)
    {
        e.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(4);
                c.RelativeColumn(2);
                c.RelativeColumn(2.4f);
                c.RelativeColumn(2.6f);
            });
            void Head(string txt) => t.Cell().Background(Soft).BorderBottom(0.7f).BorderColor(Rule)
                .Padding(4).Text(txt).SemiBold().FontSize(8f).FontColor(Ink);
            Head("Name, Vorname");
            Head("Geburtsdatum");
            Head("im gleichen Haushalt?");
            Head("ab 18: in Erstausbildung?");
            for (var i = 0; i < 5; i++)
            {
                var k = i < kinder.Count ? kinder[i] : null;
                t.Cell().Padding(3).Element(f => WriteLine(f, k?.Name));
                t.Cell().Padding(3).Element(f => WriteLine(f, k?.Geburtsdatum));
                t.Cell().Padding(3).AlignMiddle().Row(r =>
                {
                    r.AutoItem().Element(f => CheckLabel(f, "Ja", k?.Haushalt == true));
                    r.ConstantItem(10);
                    r.AutoItem().Element(f => CheckLabel(f, "Nein", k?.Haushalt == false));
                });
                t.Cell().Padding(3).AlignMiddle().Row(r =>
                {
                    r.AutoItem().Element(f => CheckLabel(f, "Ja", k?.Erstausbildung == true));
                    r.ConstantItem(10);
                    r.AutoItem().Element(f => CheckLabel(f, "Nein", k?.Erstausbildung == false));
                });
            }
        });
    }
}
