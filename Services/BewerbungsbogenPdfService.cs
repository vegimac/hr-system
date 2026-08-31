using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Blanko-Bewerbungsbogen als PDF (Walter 27./28.07.2026).
/// OneCrew-Stil (ruhig, monochrom) — grosszuegige Schreibzeilen fuer
/// Handausfuellung. Felder gemaess altem Formular (Walter 13.08.2026). 2 Seiten A4.
/// </summary>
public record BewerbungsbogenInput(
    string CompanyName,
    string? RestaurantName,
    string? Strasse,
    string? PlzOrt,
    string? Telefon,
    string? Email = null,
    // Fertig formatierte Öffnungszeiten der Filiale, Ortszeit
    // (Walter 31.08.2026). Leer = nicht erfasst → der Kasten entfällt.
    string? Oeffnungszeiten = null);

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

    /// <summary>
    /// Teil 1 — kurzes Bewerbungsformular fuer den Bewerber. Eine Seite,
    /// nur was fuer die Vorselektion noetig ist (Walter 31.08.2026).
    /// </summary>
    public byte[] GenerateBewerbung(BewerbungsbogenInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container =>
        {
            container.Page(page => ComposeBewerbung(page, d));
        }).GeneratePdf();
    }

    /// <summary>
    /// Teil 2 — wird im Bewerbungsgespraech ausgefuellt: alle Angaben, die
    /// erst bei einer konkreten Anstellung gebraucht werden, plus interne
    /// Gespraechsnotizen (Walter 31.08.2026).
    /// </summary>
    public byte[] GenerateGespraech(BewerbungsbogenInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container =>
        {
            container.Page(page => ComposeGespraechSeite1(page, d));
            container.Page(page => ComposeGespraechSeite2(page));
        }).GeneratePdf();
    }

    /// <summary>
    /// Der frühere, ungeteilte Bogen — nur zur Kontrolle (Walter 31.08.2026).
    /// Wird nicht mehr weiterentwickelt.
    /// </summary>
    public byte[] GenerateAlt(BewerbungsbogenInput d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        return Document.Create(container =>
        {
            container.Page(page => ComposeAltSeite1(page, d));
            container.Page(page => ComposeAltSeite2(page));
        }).GeneratePdf();
    }

    private static void ApplyPageChrome(PageDescriptor page, bool withBanner,
        string bannerTitel = "Bewerbungsbogen")
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
                .Text(bannerTitel).Bold().FontSize(11f).FontColor(Ink);
        });
    }

    /// <summary>Filiale + Kontaktzeile unter dem Balken — auf beiden Formularen gleich.</summary>
    private static void Briefkopf(ColumnDescriptor col, BewerbungsbogenInput d)
    {
        var titel = string.IsNullOrWhiteSpace(d.RestaurantName)
            ? d.CompanyName
            : $"{d.CompanyName} · {d.RestaurantName}";
        col.Item().Text(titel).SemiBold().FontSize(9.5f).FontColor(Ink);
        var meta = string.Join("  ·  ", new[] { d.Strasse, d.PlzOrt, d.Telefon, d.Email }
            .Where(s => !string.IsNullOrWhiteSpace(s)));
        if (!string.IsNullOrWhiteSpace(meta))
            col.Item().PaddingTop(2).Text(meta).FontSize(8f).FontColor(Muted);
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEIL 1 — Bewerbung (eine Seite, gibt der Bewerber ab)
    // ═══════════════════════════════════════════════════════════════════
    private static void ComposeBewerbung(PageDescriptor page, BewerbungsbogenInput d)
    {
        ApplyPageChrome(page, withBanner: true, bannerTitel: "Bewerbung");

        page.Content().PaddingTop(6).Column(col =>
        {
            Briefkopf(col, d);

            col.Item().PaddingTop(12).Element(e =>
                SectionHead(e, "Über dich", "Bitte gut lesbar in Blockschrift ausfüllen"));
            // Reihenfolge nach Walter-Vorgabe 31.08.2026.
            // Zwei gleich breite Spalten durchgehend — jedes Feld hat die volle
            // halbe Zeile (Walter 31.08.2026: «für Vorname ist zuwenig Platz»).
            col.Item().PaddingTop(12).Element(e => TwoFields(e, "Vorname", "E-Mail"));
            col.Item().PaddingTop(13).Element(e => TwoFields(e, "Name", "Mobile / Tel."));
            col.Item().PaddingTop(13).Element(e => TwoFields(e, "Adresse", "Geburtsdatum"));
            col.Item().PaddingTop(13).Element(e => TwoFields(e, "PLZ, Ort", "Zivilstand"));
            col.Item().PaddingTop(13).Row(r =>
            {
                r.RelativeItem().AlignBottom().Element(f => CheckOptionsRow(f, "Geschlecht", "W", "M"));
                r.ConstantItem(16);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "Nationalität"));
            });
            col.Item().PaddingTop(13).Element(e => TwoFields(e, "Bewilligung", "gültig bis"));

            col.Item().PaddingTop(16).Element(e => SectionHead(e, "Sprachkenntnisse", null));
            col.Item().PaddingTop(10).Element(LangGridKurz);

            col.Item().PaddingTop(16).Element(e => SectionHead(e, "Dein Einsatz bei uns", null));
            col.Item().PaddingTop(12).Element(e =>
                TwoFields(e, "Gewünschtes Pensum (%)", "Frühester Eintritt"));
            col.Item().PaddingTop(16).Element(e =>
                LabeledLine(e, "Erfahrung in Gastronomie — wo / was?"));

            col.Item().PaddingTop(18).Element(e =>
                SectionHead(e, "Wann kannst du arbeiten?", null));
            col.Item().PaddingTop(5)
                .Text("Bitte die normalen verfügbaren Arbeitszeiten eintragen.")
                .Italic().FontSize(8f).FontColor(Body);
            col.Item().PaddingTop(7).Element(AvailabilityTableGross);
            // Öffnungszeiten UNTER der Tabelle (Walter 31.08.2026) — sie sind
            // der Rahmen, in dem der Bewerber eintragen darf, nicht die
            // Hauptsache. Deshalb kleiner und unter dem Raster.
            if (!string.IsNullOrWhiteSpace(d.Oeffnungszeiten))
            {
                col.Item().PaddingTop(5).Text(t =>
                {
                    t.Span("Öffnungszeiten Filiale:  ").Bold().FontSize(8f).FontColor(Ink);
                    t.Span(d.Oeffnungszeiten).FontSize(8f).FontColor(Ink);
                });
            }

            col.Item().PaddingTop(18).Row(r =>
            {
                r.RelativeItem().Element(f => SignatureLine(f, "Datum"));
                r.ConstantItem(24);
                r.RelativeItem().Element(f => SignatureLine(f, "Unterschrift"));
            });

            // Minderjährige (Walter 31.08.2026): gehört hierher, weil der
            // Bewerber dieses Formular unterschreibt — nicht aufs
            // Gesprächsformular, wo es vorher stand.
            col.Item().PaddingTop(10).Background(Soft).Padding(8).Column(c =>
            {
                c.Item().Text(t =>
                {
                    t.Span("Für Minderjährige").SemiBold().FontSize(8f).FontColor(Ink);
                    t.Span(", Angaben und Einverständnis des gesetzlichen Vertreters:")
                        .FontSize(8f).FontColor(Ink);
                });
                c.Item().PaddingTop(9).Row(r =>
                {
                    r.RelativeItem().Element(f => LabeledLine(f, "Vorname Name"));
                    r.ConstantItem(24);
                    r.RelativeItem().Element(f => LabeledLine(f, "Unterschrift"));
                });
            });

            col.Item().PaddingTop(12).Text(
                    "Die Angaben dienen der Prüfung deiner Bewerbung. Dieses Formular ist noch kein Anstellungsversprechen.")
                .Italic().FontSize(7.5f).FontColor(Muted);
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // TEIL 2 — Bewerbungsgespräch (zwei Seiten, wird intern ausgefüllt)
    // ═══════════════════════════════════════════════════════════════════
    private static void ComposeGespraechSeite1(PageDescriptor page, BewerbungsbogenInput d)
    {
        ApplyPageChrome(page, withBanner: true, bannerTitel: "Bewerbungsgespräch");

        page.Content().PaddingTop(6).Column(col =>
        {
            // Nur Firma und Filiale — die Filial-Adresse steht schon auf dem
            // Bewerbungsformular und wird hier nicht gebraucht (Walter 31.08.2026).
            var titel = string.IsNullOrWhiteSpace(d.RestaurantName)
                ? d.CompanyName
                : $"{d.CompanyName} · {d.RestaurantName}";
            col.Item().Text(titel).SemiBold().FontSize(9.5f).FontColor(Ink);
            col.Item().PaddingTop(3)
                .Text("Wird im Bewerbungsgespräch ausgefüllt — gehört zum Bewerbungsformular des Bewerbers.")
                .Italic().FontSize(8f).FontColor(Body);

            col.Item().PaddingTop(11).Element(e =>
                SectionHead(e, "Personalien", null));
            // Nur Name und Vorname — Adresse, E-Mail, Telefon, Geburtsdatum
            // und Nationalität stehen bereits auf dem Bewerbungsformular
            // (Walter 31.08.2026). Alles Weitere unten wird erst im Gespräch
            // erhoben und ist dort NICHT enthalten.
            col.Item().PaddingTop(10).Element(e => TwoFields(e, "Name", "Vorname"));
            col.Item().PaddingTop(9).Row(r =>
            {
                r.AutoItem().Element(e => YesNoInline(e, "Quellensteuerpflichtig?"));
                r.ConstantItem(14);
                r.RelativeItem().AlignBottom().Element(AhvBoxes);
            });
            // Geschlecht entfernt (Walter 31.08.2026) — steht auf der Bewerbung.
            col.Item().PaddingTop(9).Element(e => TwoFields(e, "Zivilstand", "seit dem:"));
            // Israelitische Kultusgemeinde ist bei der Quellensteuer Y-fähig wie
            // die Landeskirchen (Walter 30.08.2026) — darf nicht unter «Andere».
            col.Item().PaddingTop(9).Element(e => CheckOptionsInline(e, "Konfession",
                "Evang.-reformiert", "Röm.-katholisch", "Christ-katholisch",
                "Israelitisch", "Andere", "Keine"));
            col.Item().PaddingTop(9).Element(e =>
                LabeledLine(e, "Bewilligung / Ausweis (nur für Ausländer)"));

            col.Item().PaddingTop(11).Element(e =>
                SectionHead(e, "Berufserfahrung & weitere Angaben", null));
            col.Item().PaddingTop(8)
                .Text("Leidest du an einer chronischen Krankheit oder an Allergien (v.a. Hautallergien)?")
                .FontSize(8.5f).FontColor(Ink);
            col.Item().PaddingTop(4).Row(r =>
            {
                r.ConstantItem(258);
                r.ConstantItem(42).AlignMiddle().Element(ch => CheckLabel(ch, "Ja"));
                r.ConstantItem(8);
                r.ConstantItem(52).AlignMiddle().Element(ch => CheckLabel(ch, "Nein"));
                r.ConstantItem(10);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "welche:"));
            });
            col.Item().PaddingTop(8).Row(r =>
            {
                r.ConstantItem(250).AlignMiddle().Text("Beziehst du Sozialleistungen?").FontSize(8.5f).FontColor(Ink);
                r.ConstantItem(8);
                r.AutoItem().Element(ch => CheckLabel(ch, "Arbeitslosengeld"));
                r.ConstantItem(14);
                r.AutoItem().Element(ch => CheckLabel(ch, "AHV-Rente"));
            });
            col.Item().PaddingTop(6).Row(r =>
            {
                r.ConstantItem(258);
                r.AutoItem().Element(ch => CheckLabel(ch, "IV-Rente"));
                r.ConstantItem(12);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "Invaliditätsgrad"));
            });
            col.Item().PaddingTop(8).Element(e => KatalogZeile(e, "Bist du vorbestraft?"));
            col.Item().PaddingTop(8).Element(e => KatalogZeile(e,
                "Musst du nächstens Militärservice leisten?",
                rechts: f => LabeledLine(f, "Dauer vom – bis")));
            col.Item().PaddingTop(8).Element(e => KatalogZeile(e,
                "Hast du eine Ausbildung in der Hotellerie oder Restauration?",
                hinweis: "Falls ja, bitte eine Kopie beilegen"));
            // Entfernt (Walter 31.08.2026): Frage nach frueherer Arbeit in der
            // Hotellerie, Frage nach anderen Taetigkeiten, die zwei
            // Arbeitgeber-Zeilen und die Referenzen-Zeile.
        });
    }

    private static void ComposeGespraechSeite2(PageDescriptor page)
    {
        ApplyPageChrome(page, withBanner: false);

        page.Content().PaddingTop(2).Column(col =>
        {
            col.Item().Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Angaben über Partner")
                    .Bold().FontSize(11f).FontColor(Ink);
                r.ConstantItem(10);
                r.AutoItem().AlignMiddle()
                    .Text("— nur auszufüllen, wenn quellensteuerpflichtig")
                    .Italic().FontSize(8.5f).FontColor(Body);
            });
            col.Item().PaddingTop(5).Element(e => TwoFields(e, "Name", "Vorname"));
            col.Item().PaddingTop(5).Row(r =>
            {
                r.AutoItem().Element(e => CheckOptionsInline(e, "Geschlecht Partner", "W", "M"));
                r.ConstantItem(16);
                r.RelativeItem().AlignBottom().Element(AhvBoxes);
            });
            col.Item().PaddingTop(5).Element(e => LabeledLine(e, "Adresse (nur falls abweichend)"));
            col.Item().PaddingTop(6).Row(r =>
            {
                r.RelativeItem().Element(e => YesNoInline(e, "Arbeitet Partner?"));
                r.ConstantItem(16);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "Ausweis"));
            });
            col.Item().PaddingTop(5).Element(e => LabeledLine(e, "Arbeitgeber Partner, Adresse (Strasse/Nr., PLZ, Ort)"));
            col.Item().PaddingTop(6).Row(r =>
            {
                r.RelativeItem().Element(e => LabeledLine(e, "Stellenantritt Partner (Datum)"));
                r.ConstantItem(16);
                r.RelativeItem();
            });

            col.Item().PaddingTop(6).Element(e => SectionHead(e, "Kinder", null));
            col.Item().PaddingTop(6).Element(KinderTabelle);

            col.Item().PaddingTop(8).Element(e => SectionHead(e, "Ergänzende Angaben", null));
            col.Item().PaddingTop(5).Element(e => LabeledLine(e, "Krankenkasse"));
            col.Item().PaddingTop(5).Element(e => TwoFields(e, "Bank", "Kontonummer / IBAN"));
            col.Item().PaddingTop(5).Element(e => TwoFields(e, "Bankadresse", "Clearing-Nr."));

            col.Item().PaddingTop(6).Element(e => SectionHead(e, "Allgemeine Bedingungen", null));
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

            col.Item().PaddingTop(4).Text(t =>
            {
                t.Span("Wichtig: ").Bold().FontSize(8.5f).FontColor(Ink);
                t.Span("Im Falle von Änderungen jeder Art, im Laufe des Arbeitsverhältnisses, besteht die Verpflichtung den Arbeitgeber zu informieren.")
                    .FontSize(8.5f).FontColor(Ink);
            });
            // Der Block für Minderjährige steht seit 31.08.2026 auf dem
            // Bewerbungsformular — dort unterschreibt der Bewerber, hier nicht.
            col.Item().PaddingTop(4).Background(Soft).Padding(8).Column(c =>
            {
                c.Item().Text("Datum und Unterschrift").FontSize(8.5f).FontColor(Ink);
                c.Item().Height(30);
            });

            // Interner Teil — bewusst ganz am Schluss und optisch abgesetzt,
            // damit er nie mit dem unterschriebenen Teil verwechselt wird.
            col.Item().PaddingTop(9).Element(e => SectionHead(e,
                "Notizen zum Gespräch", "intern — nicht Teil der Bewerbung"));
            col.Item().PaddingTop(6).Element(e => TwoFields(e, "Datum des Gesprächs", "Teilnehmende"));
            col.Item().PaddingTop(6).Element(e =>
                TwoFields(e, "Eintritt vereinbart per", "Für eine Dauer von mindestens"));
            col.Item().PaddingTop(8).Text("Eindruck / Notizen").SemiBold().FontSize(8.5f).FontColor(Ink);
            // Walter 31.08.2026: 3 Zeilen — das Formular MUSS auf 2 Seiten bleiben.
            for (var i = 0; i < 3; i++)
                col.Item().PaddingTop(8).Element(WriteLine);
            col.Item().PaddingTop(8).Row(r =>
            {
                r.AutoItem().Element(e =>
                    CheckOptionsInline(e, "Entscheid", "Zusage", "Absage", "Rückstellung"));
                r.ConstantItem(16);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "Visum"));
            });
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    // ALT — der eine, ungeteilte Bewerbungsbogen (Stand 711eec6).
    // Walter 31.08.2026: NUR zur Kontrolle wieder erzeugbar (teil=alt).
    // Unveraendert aus der Historie uebernommen — bitte hier nichts
    // weiterentwickeln, gepflegt werden die beiden neuen Formulare.
    // ═══════════════════════════════════════════════════════════════════
    private static void ComposeAltSeite1(PageDescriptor page, BewerbungsbogenInput d)
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
            col.Item().PaddingTop(9).Element(e => TwoFields(e, "Adresse", "E-Mail"));
            col.Item().PaddingTop(9).Element(e => TwoFields(e, "PLZ, Ort", "Tel."));
            col.Item().PaddingTop(9).Element(e => TwoFields(e, "Geburtsdatum", "Nationalität"));
            // Notfallkontakt (Walter 25.08.2026 v3): bei den Personalien des
            // Kandidaten — direkt nach Geb.-Datum/Nationalität, fett, eine Zeile.
            col.Item().PaddingTop(9).Row(r =>
            {
                r.AutoItem().AlignBottom().PaddingBottom(2)
                    .Text("Notfallkontakt").Bold().FontSize(8.5f).FontColor(Ink);
                r.ConstantItem(10);
                r.RelativeItem(4).Element(f => BoldLabeledLine(f, "Name"));
                r.ConstantItem(12);
                r.RelativeItem(3).Element(f => BoldLabeledLine(f, "Beziehung"));
                r.ConstantItem(12);
                r.RelativeItem(3).Element(f => BoldLabeledLine(f, "Telefon"));
            });
            // Geburtsort/Heimatort entfernt (Walter 13.08.2026) — dafür die
            // AHV-Nummer als Ziffern-Boxen 756·XXXX·XXXX·XX (besser lesbar
            // bei Handausfüllung).
            // QST + AHV-Boxen auf EINER Zeile (Walter 13.08.2026) — der Bogen
            // MUSS auf 2 Seiten bleiben.
            col.Item().PaddingTop(9).Row(r =>
            {
                r.AutoItem().Element(e => YesNoInline(e, "Quellensteuerpflichtig?"));
                r.ConstantItem(14);
                r.RelativeItem().AlignBottom().Element(AhvBoxes);
            });
            // Geschlecht zum Ankreuzen W/M/D, Zivilstand in der Mitte,
            // «seit dem:» dahinter (Walter 13.08.2026).
            col.Item().PaddingTop(9).Row(r =>
            {
                r.RelativeItem(1.0f).Element(e => CheckOptionsInline(e, "Geschlecht", "W", "M", "D"));
                r.ConstantItem(16);
                r.RelativeItem(1.1f).AlignBottom().Element(f => LabeledLine(f, "Zivilstand"));
                r.ConstantItem(16);
                r.RelativeItem(0.9f).AlignBottom().Element(f => LabeledLine(f, "seit dem:"));
            });
            // Konfession zum Ankreuzen — gleiche Werte wie MA-Stammdaten
            // (Walter 03.08.2026).
            // Walter-Vorgabe 30.08.2026: Israelitische Kultusgemeinde ergänzt —
            // sie ist bei der Quellensteuer Y-fähig wie die Landeskirchen, darf
            // also nicht unter «Andere» verschwinden.
            col.Item().PaddingTop(9).Element(e => CheckOptionsInline(e, "Konfession",
                "Evang.-reformiert", "Röm.-katholisch", "Christ-katholisch",
                "Israelitisch", "Andere", "Keine"));
            // Kinder-Block entfernt (Walter 13.08.2026) — dafür die
            // Verfügbarkeit von Seite 2 unten auf Seite 1 (siehe unten).
            col.Item().PaddingTop(9).Element(e =>
                LabeledLine(e, "Bewilligung / Ausweis (nur für Ausländer)"));


            col.Item().PaddingTop(11).Element(e => SectionHead(e, "Sprachkenntnisse", null));
            col.Item().PaddingTop(8).Element(LangGrid);

            // Fragenkatalog von Seite 2 hierher (Walter 13.08.2026, Tausch mit Verfuegbarkeit).
            // Block gemäss altem Bewerbungsformular (Walter 13.08.2026) —
            // ersetzt die früheren Schule-/Arbeitgeber-Tabellen.
            col.Item().PaddingTop(11).Element(e => SectionHead(e, "Berufserfahrung & weitere Angaben", null));
            // Spalten-Layout (Walter 13.08.2026): Frage links in fester Spalte,
            // ☐ Ja / ☐ Nein fluchtend untereinander, Hinweis/Zusatzfeld rechts.
            // Grosszügigere Zeilenabstände (PaddingTop 8 statt 5).
            // Gesundheitsfrage: Frage über die VOLLE Breite (Walter 13.08.2026 —
            // «genug Platz für Text», kein 2-zeiliger Umbruch in der Spalte);
            // Ja/Nein darunter in denselben fluchtenden Spalten wie die übrigen.
            col.Item().PaddingTop(8)
                .Text("Leidest du an einer chronischen Krankheit oder an Allergien (v.a. Hautallergien)?")
                .FontSize(8.5f).FontColor(Ink);
            col.Item().PaddingTop(4).Row(r =>
            {
                r.ConstantItem(258);
                r.ConstantItem(42).AlignMiddle().Element(ch => CheckLabel(ch, "Ja"));
                r.ConstantItem(8);
                r.ConstantItem(52).AlignMiddle().Element(ch => CheckLabel(ch, "Nein"));
                r.ConstantItem(10);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "welche:"));
            });
            // Sozialleistungen: zwei Zeilen — Kästchen beginnen in derselben
            // Spalte wie die Ja/Nein-Kästchen, IV-Rente direkt vor dem
            // Invaliditätsgrad (Walter 13.08.2026).
            col.Item().PaddingTop(8).Row(r =>
            {
                r.ConstantItem(250).AlignMiddle().Text("Beziehst du Sozialleistungen?").FontSize(8.5f).FontColor(Ink);
                r.ConstantItem(8);
                r.AutoItem().Element(ch => CheckLabel(ch, "Arbeitslosengeld"));
                r.ConstantItem(14);
                r.AutoItem().Element(ch => CheckLabel(ch, "AHV-Rente"));
            });
            col.Item().PaddingTop(6).Row(r =>
            {
                r.ConstantItem(258);
                r.AutoItem().Element(ch => CheckLabel(ch, "IV-Rente"));
                r.ConstantItem(12);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "Invaliditätsgrad"));
            });
            col.Item().PaddingTop(8).Element(e => KatalogZeile(e, "Bist du vorbestraft?"));
            col.Item().PaddingTop(8).Element(e => KatalogZeile(e,
                "Musst du nächstens Militärservice leisten?",
                rechts: f => LabeledLine(f, "Dauer vom – bis")));
            col.Item().PaddingTop(8).Element(e => KatalogZeile(e,
                "Hast du eine Ausbildung in der Hotellerie oder Restauration?",
                hinweis: "Falls ja, bitte eine Kopie beilegen"));
            col.Item().PaddingTop(8).Element(e => KatalogZeile(e,
                "Hast du schon in der Hotellerie/Restauration gearbeitet?",
                hinweis: "Falls ja, Kopie der Arbeitszeugnisse beilegen"));
            col.Item().PaddingTop(8).Element(e => KatalogZeile(e,
                "Hast du andere berufliche Aktivitäten oder freiwillige Einsätze?",
                hinweis: "Falls ja, bitte unten ausfüllen"));
            // Walter 25.08.2026: 2 statt 3 Zeilen — «so viele Arbeitgeber hat niemand».
            col.Item().PaddingTop(10).Element(ArbeitgeberZeile);
            col.Item().PaddingTop(8).Element(ArbeitgeberZeile);
            col.Item().PaddingTop(9).Element(e => LabeledLine(e, "Wo dürfen Referenzen eingeholt werden?"));

        });
    }

    private static void ComposeAltSeite2(PageDescriptor page)
    {
        ApplyPageChrome(page, withBanner: false);

        page.Content().PaddingTop(2).Column(col =>
        {

            // (verschoben von Seite 1 — Seite 1 traegt jetzt die Verfuegbarkeit, 13.08.2026)
            // Verfuegbarkeit von Seite 1 hierher (Walter 13.08.2026, Tausch mit Fragenkatalog).
            col.Item().Element(e =>
                SectionHead(e, "Verfügbarkeit & Eintritt",
                    "08.00–01.00 · Fr/Sa bis 03.00 Uhr"));
            col.Item().PaddingTop(6).Element(AvailabilityTable);
            col.Item().PaddingTop(10).Element(e =>
                TwoFields(e, "Frühestes Eintrittsdatum", "Für eine Dauer von mindestens"));

            // Partner-Block im bewährten OneCrew-Layout (Walter 13.08.2026):
            // wie früher «Angaben über Partner», mit zwei Änderungen —
            // AHV-Nummer als Ziffern-Boxen ANSTELLE des Geburtsorts und
            // Geschlecht als W/M/D-Ankreuz (wie beim MA auf Seite 1).
            // Hinweis lesbar, aber dezent (Walter 13.08.2026): kein Badge —
            // normal grosser kursiver Text direkt neben dem Titel.
            col.Item().PaddingTop(12).Row(r =>
            {
                r.AutoItem().AlignMiddle().Text("Angaben über Partner")
                    .Bold().FontSize(11f).FontColor(Ink);
                r.ConstantItem(10);
                r.AutoItem().AlignMiddle()
                    .Text("— nur auszufüllen, wenn quellensteuerpflichtig")
                    .Italic().FontSize(8.5f).FontColor(Body);
            });
            col.Item().PaddingTop(6).Element(e => TwoFields(e, "Name", "Vorname"));
            // Geschlecht Partner nur noch W/M (Walter 20.08.2026, kein D mehr).
            col.Item().PaddingTop(6).Row(r =>
            {
                r.AutoItem().Element(e => CheckOptionsInline(e, "Geschlecht Partner", "W", "M"));
                r.ConstantItem(16);
                r.RelativeItem().AlignBottom().Element(AhvBoxes);
            });
            // Statt «Aufenthaltsort» (Walter 13.08.2026): Adresse nur, wenn
            // sie von der des Bewerbers abweicht.
            col.Item().PaddingTop(6).Element(e => LabeledLine(e, "Adresse (nur falls abweichend)"));
            col.Item().PaddingTop(6).Row(r =>
            {
                r.RelativeItem().Element(e => YesNoInline(e, "Arbeitet Partner?"));
                r.ConstantItem(16);
                r.RelativeItem().AlignBottom().Element(f => LabeledLine(f, "Ausweis"));
            });
            // Arbeitgeber Partner mit GENUG Platz für die volle Adresse +
            // Stellenantritt (Walter 20.08.2026 — Angaben landen 1:1 in den
            // neuen Ehepartner-Feldern des Familien-Tabs).
            col.Item().PaddingTop(6).Element(e => LabeledLine(e, "Arbeitgeber Partner, Adresse (Strasse/Nr., PLZ, Ort)"));
            col.Item().PaddingTop(6).Row(r =>
            {
                r.RelativeItem().Element(e => LabeledLine(e, "Stellenantritt Partner (Datum)"));
                r.ConstantItem(16);
                r.RelativeItem();
            });

            // Kinder-Tabelle gemäss altem Formular (Walter 13.08.2026; der
            // frühere Kinder-Block von Seite 1 lebt jetzt hier).
            col.Item().PaddingTop(8).Element(e => SectionHead(e, "Kinder", null));
            col.Item().PaddingTop(6).Element(KinderTabelle);

            // Rest von Seite 3 hierher — Bogen wieder 2-seitig (Walter 13.08.2026).
            col.Item().PaddingTop(10).Element(e => SectionHead(e, "Ergänzende Angaben", null));
            col.Item().PaddingTop(6).Element(e => LabeledLine(e, "Krankenkasse"));
            col.Item().PaddingTop(6).Element(e => TwoFields(e, "Bank", "Kontonummer / IBAN"));
            col.Item().PaddingTop(6).Element(e => TwoFields(e, "Bankadresse", "Clearing-Nr."));

            // Notfallkontakt: seit 25.08.2026 v3 auf SEITE 1 bei den
            // Personalien (nach Geb.-Datum/Nationalität) — hier entfernt.

            // Alt-Fragen-Block (McDonald's / Angestellte / Krankheit / Schwangerschaft /
            // vorbestraft / bevormundet / Militaer) entfernt (Walter 13.08.2026).

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

            // Unterschriften-Bereich gemäss altem Bogen (Walter 13.08.2026):
            // «Wichtig»-Satz, links Datum + Unterschrift (freier Schreibraum),
            // rechts der Block für Minderjährige (gesetzlicher Vertreter).
            col.Item().PaddingTop(6).Text(t =>
            {
                t.Span("Wichtig: ").Bold().FontSize(8.5f).FontColor(Ink);
                t.Span("Im Falle von Änderungen jeder Art, im Laufe des Arbeitsverhältnisses, besteht die Verpflichtung den Arbeitgeber zu informieren.")
                    .FontSize(8.5f).FontColor(Ink);
            });
            col.Item().PaddingTop(5).Row(r =>
            {
                r.RelativeItem().Background(Soft).Padding(8).Column(c =>
                {
                    c.Item().Text("Datum und Unterschrift").FontSize(8.5f).FontColor(Ink);
                    c.Item().Height(54); // freier Schreibraum (v2: 2-Seiten-Zwang)
                });
                r.ConstantItem(14);
                r.RelativeItem().Background(Soft).Padding(8).Column(c =>
                {
                    c.Item().Text(t =>
                    {
                        t.Span("Für Minderjährige").SemiBold().FontSize(8f).FontColor(Ink);
                        t.Span(", Angaben und Einverständnis des gesetzlichen Vertreters:")
                            .FontSize(8f).FontColor(Ink);
                    });
                    // Nur Vorname Name + Unterschrift, grosszügige Abstände
                    // (Walter 13.08.2026).
                    c.Item().PaddingTop(10).Element(f => LabeledLine(f, "Vorname Name"));
                    c.Item().PaddingTop(12).Element(f => LabeledLine(f, "Unterschrift"));
                });
            });

        });
    }

    /// <summary>
    /// Wie CheckOptionsInline, aber alles auf EINER Zeile — damit ein
    /// Ankreuzfeld neben einer Schreibzeile stehen kann (Walter 31.08.2026).
    /// </summary>
    private static void CheckOptionsRow(IContainer e, string label, params string[] options)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignMiddle().PaddingRight(10)
                .Text(label).FontSize(8.5f).FontColor(Ink);
            for (var i = 0; i < options.Length; i++)
            {
                if (i > 0) r.ConstantItem(10);
                r.AutoItem().AlignMiddle().Element(ch => CheckLabel(ch, options[i]));
            }
        });
    }

    /// <summary>
    /// Sprachkenntnisse kurz (Bewerbungsformular, Walter 31.08.2026):
    /// nur Deutsch plus eine freie Zeile für eine andere Sprache.
    /// </summary>
    private static void LangGridKurz(IContainer e)
    {
        e.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.9f);
                c.RelativeColumn(1f);
                c.RelativeColumn(0.85f);
                c.RelativeColumn(1.2f);
            });

            t.Cell().PaddingBottom(4).Text("");
            foreach (var h in new[] { "sehr gut", "gut", "Grundkenntnisse" })
                t.Cell().PaddingBottom(4).AlignCenter().Text(h).FontSize(7.5f).FontColor(Ink);

            t.Cell().PaddingVertical(4).AlignMiddle().Text("Deutsch").FontSize(8.5f).FontColor(Ink);
            for (var i = 0; i < 3; i++) t.Cell().PaddingVertical(4).AlignCenter().Element(Check);

            t.Cell().PaddingVertical(4).PaddingRight(10).AlignBottom()
                .Element(f => LabeledLine(f, "Andere"));
            for (var i = 0; i < 3; i++) t.Cell().PaddingVertical(4).AlignCenter().Element(Check);
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

    /// <summary>Geburtsdatum als Ziffern-Kästchen TT·MM·JJJJ (Walter 13.08.2026).</summary>
    private static void DatumBoxes(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(3)
                .Text(label).FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(8);
            int[] gruppen = { 2, 2, 4 };
            for (var g = 0; g < gruppen.Length; g++)
            {
                if (g > 0) r.ConstantItem(7);
                for (var i = 0; i < gruppen[g]; i++)
                {
                    if (i > 0) r.ConstantItem(2);
                    r.ConstantItem(19).Element(b => b
                        .Height(24).Border(0.8f).BorderColor(Line).Text(" "));
                }
            }
        });
    }

    /// <summary>
    /// Kinder-Tabelle gemäss altem Formular (Walter 13.08.2026): Name,
    /// Vorname, Geschlecht M/W, Geburtsdatum, gleicher Haushalt Ja/Nein,
    /// wenn nein in der CH Ja/Nein — 4 Leerzeilen für Handausfüllung.
    /// </summary>
    private static void KinderTabelle(IContainer e)
    {
        e.Table(t =>
        {
            t.ColumnsDefinition(c =>
            {
                c.RelativeColumn(1.2f);   // Name
                c.RelativeColumn(1.2f);   // Vorname
                c.RelativeColumn(0.75f);  // Geschlecht
                c.RelativeColumn(1.0f);   // Geburtsdatum
                c.RelativeColumn(0.95f);  // gleicher Haushalt
                c.RelativeColumn(0.95f);  // wenn nein: in der CH
            });
            foreach (var h in new[] { "Name", "Vorname", "Geschlecht", "Geburtsdatum",
                                      "Leben sie im gleichen Haushalt?", "Wenn nein: leben sie in der CH?" })
                t.Cell().PaddingBottom(3).PaddingRight(6).AlignCenter()
                    .Text(h).FontSize(7.5f).FontColor(Ink);
            for (var row = 0; row < 4; row++)
            {
                t.Cell().PaddingVertical(3).PaddingRight(8).Element(WriteLine);
                t.Cell().PaddingVertical(3).PaddingRight(8).Element(WriteLine);
                t.Cell().PaddingVertical(3).AlignCenter().Row(x =>
                {
                    x.AutoItem().Element(ch => CheckLabel(ch, "M"));
                    x.ConstantItem(8);
                    x.AutoItem().Element(ch => CheckLabel(ch, "W"));
                });
                t.Cell().PaddingVertical(3).PaddingRight(8).Element(WriteLine);
                t.Cell().PaddingVertical(3).AlignCenter().Row(x =>
                {
                    x.AutoItem().Element(ch => CheckLabel(ch, "Ja"));
                    x.ConstantItem(8);
                    x.AutoItem().Element(ch => CheckLabel(ch, "Nein"));
                });
                t.Cell().PaddingVertical(3).AlignCenter().Row(x =>
                {
                    x.AutoItem().Element(ch => CheckLabel(ch, "Ja"));
                    x.ConstantItem(8);
                    x.AutoItem().Element(ch => CheckLabel(ch, "Nein"));
                });
            }
        });
    }

    /// <summary>Frage + ☐Nein ☐Ja + kursiver Hinweis (Walter 13.08.2026,
    /// Block aus dem alten Bewerbungsformular).</summary>
    private static void FrageMitHinweis(IContainer e, string frage, string hinweis)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignMiddle().Text(frage).FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(12);
            r.AutoItem().Element(ch => CheckLabel(ch, "Nein"));
            r.ConstantItem(12);
            r.AutoItem().Element(ch => CheckLabel(ch, "Ja"));
            r.ConstantItem(12);
            r.RelativeItem().AlignMiddle().Text(hinweis).FontSize(7.5f).Italic().FontColor(Body);
        });
    }

    /// <summary>Zeile «Name des Arbeitgebers … Beschäftigungsgrad … Fix/Variabel … PLZ/Land».</summary>
    private static void ArbeitgeberZeile(IContainer e)
    {
        // Kompakte Form (Walter 13.08.2026):
        // Arbeitgeber ___  PLZ/Ort ___  ___ %  ☐ Fix  ☐ Var.  ___ Std/Wo
        e.Row(r =>
        {
            r.RelativeItem(1.4f).Element(f => LabeledLine(f, "Arbeitgeber"));
            r.ConstantItem(10);
            r.RelativeItem(1.0f).Element(f => LabeledLine(f, "PLZ/Ort"));
            r.ConstantItem(10);
            r.ConstantItem(30).AlignBottom().Element(WriteLine);
            r.ConstantItem(3);
            r.AutoItem().AlignBottom().PaddingBottom(1).Text("%").FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(10);
            r.AutoItem().AlignBottom().PaddingBottom(1).Element(ch => CheckLabel(ch, "Fix"));
            r.ConstantItem(8);
            r.AutoItem().AlignBottom().PaddingBottom(1).Element(ch => CheckLabel(ch, "Var."));
            r.ConstantItem(10);
            r.ConstantItem(36).AlignBottom().Element(WriteLine);
            r.ConstantItem(3);
            r.AutoItem().AlignBottom().PaddingBottom(1).Text("Std/Wo").FontSize(8.5f).FontColor(Ink);
        });
    }

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

    /// <summary>Wie LabeledLine, aber FETTE Beschriftung — Notfallkontakt
    /// (Walter-Vorgabe 25.08.2026: soll ins Auge stechen).</summary>
    private static void BoldLabeledLine(IContainer e, string label)
    {
        e.Row(r =>
        {
            r.AutoItem().AlignBottom().PaddingBottom(2)
                .Text(label).Bold().FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(8);
            r.RelativeItem().Element(WriteLine);
        });
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

    /// <summary>Extra-hohe Schreibzeile fuer Unterschriften — 38pt
    /// (Walter 13.08.2026: grosszügiger, wird von Hand ausgefüllt).</summary>
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
            // Grosse Kästchen für Handausfüllung (Walter 13.08.2026); Breite so
            // bemessen, dass die Zeile NEBEN «Quellensteuerpflichtig?» passt.
            int[] gruppen = { 3, 4, 4, 2 };
            for (var g = 0; g < gruppen.Length; g++)
            {
                if (g > 0) r.ConstantItem(7);
                for (var i = 0; i < gruppen[g]; i++)
                {
                    if (i > 0) r.ConstantItem(2);
                    r.ConstantItem(19).Element(b => b
                        .Height(24).Border(0.8f).BorderColor(Line).Text(" "));
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
    /// Fragenkatalog-Zeile mit festen Spalten (Walter 13.08.2026):
    /// Frage links (250pt), ☐ Ja / ☐ Nein in fluchtenden Spalten,
    /// rechts optional ein Zusatzfeld (rechts) oder ein kursiver Hinweis.
    /// Spaltensumme: 250+8+42+8+52+10 = 370 → Rest ~125pt (A4 ~495pt).
    /// </summary>
    private static void KatalogZeile(IContainer e, string frage,
        Action<IContainer>? rechts = null, string? hinweis = null)
    {
        e.Row(r =>
        {
            r.ConstantItem(250).AlignMiddle().Text(frage).FontSize(8.5f).FontColor(Ink);
            r.ConstantItem(8);
            r.ConstantItem(42).AlignMiddle().Element(ch => CheckLabel(ch, "Ja"));
            r.ConstantItem(8);
            r.ConstantItem(52).AlignMiddle().Element(ch => CheckLabel(ch, "Nein"));
            r.ConstantItem(10);
            var rest = r.RelativeItem();
            if (rechts != null) rest.AlignBottom().Element(el => rechts(el));
            else if (hinweis != null) rest.AlignMiddle().Text(hinweis).FontSize(7.5f).Italic().FontColor(Muted);
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

            // Kompakter (Walter 13.08.2026): PaddingVertical 6→3 spart Platz
            // zugunsten des grösseren Kinder-Blocks auf Seite 1.
            void LangRow(string name, bool free = false)
            {
                if (free)
                    t.Cell().PaddingVertical(3).PaddingRight(8).Element(WriteLine);
                else
                    t.Cell().PaddingVertical(3).AlignMiddle().Text(name).FontSize(8.5f).FontColor(Ink);
                for (var i = 0; i < 3; i++)
                    t.Cell().PaddingVertical(3).AlignCenter().Element(Check);
            }
            LangRow("Deutsch");
            LangRow("Englisch");
            LangRow("Französisch");
            LangRow("", free: true);
        });
    }

    /// <summary>
    /// Verfügbarkeits-Raster fürs Bewerbungsformular (Walter 31.08.2026):
    /// gleiche Struktur wie AvailabilityTable, aber deutlich mehr Schreibraum
    /// pro Feld — das Formular wird von Hand ausgefüllt. Der alte Bogen
    /// benutzt weiterhin AvailabilityTable und bleibt unverändert.
    /// </summary>
    private static void AvailabilityTableGross(IContainer e)
    {
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
                t.Cell().Border(0.6f).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(2).Row(r =>
                {
                    r.RelativeItem().AlignCenter().Text("von").FontSize(7.5f).FontColor(Ink);
                    r.RelativeItem().AlignCenter().Text("bis").FontSize(7.5f).FontColor(Ink);
                });
            }

            // Schreibfeld: hoeher und mit weniger Rand, damit die Linien
            // spuerbar breiter werden.
            foreach (var _ in days)
            {
                t.Cell().Border(0.6f).BorderColor(Rule).PaddingVertical(14).PaddingHorizontal(2).Row(r =>
                {
                    r.RelativeItem().Element(f => WriteLineAt(f, 30f));
                    r.ConstantItem(3);
                    r.RelativeItem().Element(f => WriteLineAt(f, 30f));
                });
            }
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
                t.Cell().Border(0.6f).BorderColor(Rule).PaddingVertical(8).PaddingHorizontal(4).Row(r =>
                {
                    r.RelativeItem().Element(f => WriteLineAt(f, 20f));
                    r.ConstantItem(4);
                    r.RelativeItem().Element(f => WriteLineAt(f, 20f));
                });
            }
        });
    }
}
