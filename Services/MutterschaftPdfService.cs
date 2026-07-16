using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Mutterschafts-Gespräch (Walter-Vorgabe 16.07.2026, nach der Word-Vorlage
/// «Mutterschaftsvereinbarung.docx»): sobald die MA den Geburtstermin meldet,
/// wird mit ihr eine CHECKLISTE durchgearbeitet; daraus entsteht die
/// MUTTERSCHAFTSVEREINBARUNG (Du-Form, Varianten Verlängerung / Rückkehr).
/// Beide Dokumente auf dem Haus-Briefpapier (gelbes Banner).
/// </summary>
public class MutterschaftPdfService
{
    private const string Dark  = "#1a1a1a";
    private const string Muted = "#475569";
    private const string Pink  = "#be185d";

    private static readonly byte[] BannerBytes =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    // ── Gemeinsame Kopf-Daten ────────────────────────────────────────────────
    public record MvCommon(
        string? FirmaName, string? RestaurantName, string? FirmaStrasse, string? FirmaPlzOrt,
        string  MaVorname, string MaName, string? MaStrasse, string? MaPlzOrt,
        string? EmployeeNumber,
        DateTime? MaGeburtsdatum,
        string  Ort, DateOnly Datum,
        DateOnly ErrechneterTermin,
        string? UnterzeichnerName, string? UnterzeichnerTitel, byte[]? SignaturePng,
        // Erreichbarkeit der Filiale (Walter 16.07.2026, fuer den Arztbrief):
        // Restaurant-Telefon + -E-Mail — nie private Nummern.
        string? FirmaTelefon = null, string? FirmaEmail = null);

    // ── Vereinbarungs-Optionen (Ergebnis des Gesprächs) ─────────────────────
    public record MvOptionen(
        DateOnly GespraechsDatum,
        int VerlBezahlt,                // 0 = keine
        int VerlUnbezahlt,              // 0 = keine
        string Rueckkehr,               // GLEICH | ANDERS | KEINE
        decimal? PensumProzent,         // nur bei ANDERS
        string? RueckkehrRestaurant,    // nur bei ANDERS
        bool Eingeschrieben);           // sonst persönliche Aushändigung

    // ── Optionen Mutterschaftsbestaetigung (nach der Geburt) ────────────────
    public record BestOptionen(
        DateOnly Geburt,                 // effektives Geburtsdatum (entbunden am)
        string Rueckkehr,                // GLEICH | ANDERS | KEINE
        int UrlaubBezahlt,               // Resturlaubstage im Anschluss (0 = keine)
        int UrlaubUnbezahlt,             // unbezahlte Tage im Anschluss (0 = keine)
        DateOnly? Wiederaufnahme,        // Datum Wiederaufnahme (GLEICH/ANDERS)
        decimal? PensumProzent,          // nur ANDERS
        string? RueckkehrRestaurant,     // nur ANDERS
        bool Pensionskasse,              // nur KEINE: zahlt in die PK ein → Freizuegigkeit
        string? KindName,                // optional: Vorname des Kindes
        bool Eingeschrieben);

    // ── Arzt-Angaben fuer den Arztbrief ─────────────────────────────────────
    public record ArztInfo(
        string? Titel, string Vorname, string Nachname,
        string? Fachgebiet, string? PraxisName,
        string? Strasse, string? PlzOrt);

    // ═══════════════ BRIEF AN DEN BEHANDELNDEN ARZT ═════════════════════════
    // (Walter-Vorgabe 16.07.2026, nach Word-Vorlage «Brief an den behandelnden
    // Arzt»: medizinische Eignungsuntersuchung bei schwangeren Frauen und
    // stillenden Muettern. Arzt-Adresse im C5-Fenster.)
    public byte[] GenerateArztbrief(MvCommon d, ArztInfo a)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var arztName = string.Join(" ", new[] { a.Titel, a.Vorname, a.Nachname }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        var arztLines = new[] { a.PraxisName, arztName, a.Fachgebiet, a.Strasse, a.PlzOrt }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
        var firmaLines = new[] { d.FirmaName, d.RestaurantName, d.FirmaStrasse, d.FirmaPlzOrt }
            .Where(x => !string.IsNullOrWhiteSpace(x));

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.3f, Unit.Centimetre);
                page.MarginHorizontal(2.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).FontColor(Dark).LineHeight(1.35f));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Item().Text(string.Join(" · ", firmaLines)).FontSize(8.5f).FontColor(Muted);

                    // Arzt-Adresse in der C5-Fensterzone (wie Kuendigung/Rueckzug).
                    col.Item().Height(40);
                    col.Item().Column(c =>
                    {
                        foreach (var ln in arztLines) c.Item().Text(ln);
                    });

                    col.Item().PaddingTop(30).Text($"{d.Ort}, {d.Datum:dd.MM.yyyy}");

                    col.Item().PaddingTop(30).Text("Medizinische Eignungsuntersuchung bei schwangeren Frauen und stillenden Müttern")
                        .Bold().FontSize(12.5f);

                    col.Item().PaddingTop(20).Text(t =>
                    {
                        t.Span("An die behandelnde Ärztin / den behandelnden Arzt von ");
                        t.Span($"Frau {d.MaVorname} {d.MaName}").Bold();
                        if (d.MaGeburtsdatum.HasValue)
                        {
                            t.Span(", geb. ");
                            t.Span($"{d.MaGeburtsdatum.Value:dd.MM.yyyy}").Bold();
                        }
                        t.Span(".");
                    });

                    col.Item().PaddingTop(18).Text(
                        "Gemäss den Bestimmungen zum Mutterschutz hat unser Betrieb die Risikobeurteilung zum Schutz von schwangeren oder stillenden Frauen vor gefährlichen und beschwerlichen Arbeiten vorgenommen. Die Risikobeurteilung für den Arbeitsplatz der genannten Mitarbeiterin hat ergeben, dass unter Einhaltung der allfällig genannten Schutzmassnahmen eine gesundheitliche Belastung für Mutter und Kind weitgehend ausgeschlossen werden kann.");

                    col.Item().PaddingTop(18).Text(
                        "Bitte teilen Sie uns nach erfolgter Eignungsuntersuchung die Eignung mittels beiliegendem Formular «Eignungsbeurteilung» mit.");

                    col.Item().PaddingTop(18).Text(
                        "Für ergänzende Auskünfte oder Rückfragen stehen wir Ihnen gerne zur Verfügung.");

                    // Gruss + Unterschrift (Unterschriftsberechtigte der Filiale;
                    // Bild nur wenn die eingeloggte Person selbst unterzeichnet).
                    col.Item().PaddingTop(30).Text("Freundliche Grüsse");
                    if (!string.IsNullOrWhiteSpace(d.FirmaName))
                        col.Item().PaddingTop(2).Text($"{d.FirmaName}{(string.IsNullOrWhiteSpace(d.RestaurantName) ? "" : " · " + d.RestaurantName)}").Bold();
                    if (d.SignaturePng is { Length: > 0 })
                        col.Item().PaddingTop(8).Height(52).AlignLeft().Image(d.SignaturePng).FitHeight();
                    else
                        col.Item().PaddingTop(8).Height(56);
                    col.Item().Text(d.UnterzeichnerName ?? "");
                    if (!string.IsNullOrWhiteSpace(d.UnterzeichnerTitel))
                        col.Item().Text(d.UnterzeichnerTitel!).FontColor(Muted);

                    // Erreichbarkeit fuer Rueckfragen (Walter 16.07.2026):
                    // Restaurant-Telefon + -E-Mail unter der Unterschrift.
                    var kontakt = string.Join("  ·  ", new[]
                    {
                        string.IsNullOrWhiteSpace(d.FirmaTelefon) ? null : $"Tel. {d.FirmaTelefon}",
                        d.FirmaEmail
                    }.Where(x => !string.IsNullOrWhiteSpace(x)));
                    if (!string.IsNullOrWhiteSpace(kontakt))
                        col.Item().PaddingTop(6).Text(kontakt).FontSize(9.5f).FontColor(Muted);
                });

                page.Footer().Column(col =>
                {
                    col.Item().Text("Beilagen:").FontSize(9.5f).Bold();
                    col.Item().Text("Risikobeurteilung Mutterschutz").FontSize(9.5f).FontColor(Muted);
                    col.Item().Text("Eignungsbeurteilung").FontSize(9.5f).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }

    // ═══════════════ MUTTERSCHAFTSBESTAETIGUNG (nach der Geburt) ════════════
    // (Walter-Vorgabe 16.07.2026, nach Word-Vorlage «Mutterschaftsbestaetigung»:
    // nach Erfassung des definitiven Geburtsdatums — Gratulation, Urlaubs-
    // Zeitraum 14 Wochen/98 Tage ab Geburt, Rueckkehr-Varianten bzw.
    // Beendigung, EO-Formular-Frist 4 Wochen nach Entbindung.)
    public byte[] GenerateBestaetigung(MvCommon d, BestOptionen o)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var urlaubEnde = o.Geburt.AddDays(97);          // 98 Tage inkl. Geburtstag
        var eoFrist    = o.Geburt.AddDays(28);          // 4 Wochen nach Entbindung
        bool beendigung = o.Rueckkehr == "KEINE";

        var firmaLines = new[] { d.FirmaName, d.RestaurantName, d.FirmaStrasse, d.FirmaPlzOrt }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
        var maLines = new[] { $"{d.MaVorname} {d.MaName}".Trim(), d.MaStrasse, d.MaPlzOrt }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();

        // Absaetze nach Vorlage zusammenstellen
        var absaetze = new List<string>();
        absaetze.Add(string.IsNullOrWhiteSpace(o.KindName)
            ? "Wir freuen uns mit euch über die Geburt eures Kindes und wünschen euch viel Freude und Glück."
            : $"Wir freuen uns mit euch über die Geburt von {o.KindName.Trim()} und wünschen euch viel Freude und Glück.");
        absaetze.Add("Wie vereinbart bestätigen wir dir nun deine Mutterschaft wie folgt:");
        absaetze.Add($"Du hast am {o.Geburt:dd.MM.yyyy} entbunden.");
        absaetze.Add($"Dein Mutterschaftsurlaub über einen Zeitraum von 14 Wochen, d.h. 98 Tagen, hat am {o.Geburt:dd.MM.yyyy} begonnen und endet am {urlaubEnde:dd.MM.yyyy}.");

        // Urlaub im Anschluss (Vorlage: Variante bezahlter/unbezahlter Urlaub)
        if (o.UrlaubBezahlt > 0 && o.UrlaubUnbezahlt > 0)
            absaetze.Add($"Du hast beschlossen, im Anschluss an die Mutterschaft {o.UrlaubBezahlt} bezahlte Urlaubstage und {o.UrlaubUnbezahlt} Tage unbezahlten Urlaub zu nehmen.");
        else if (o.UrlaubBezahlt > 0)
            absaetze.Add($"Du hast beschlossen, im Anschluss an die Mutterschaft {o.UrlaubBezahlt} bezahlte Urlaubstage zu nehmen.");
        else if (o.UrlaubUnbezahlt > 0)
            absaetze.Add($"Du hast beschlossen, im Anschluss an deinen Mutterschaftsurlaub {o.UrlaubUnbezahlt} Tage unbezahlten Urlaub zu nehmen.");

        if (o.Rueckkehr == "GLEICH" && o.Wiederaufnahme.HasValue)
            absaetze.Add($"Die Wiederaufnahme deiner Arbeit erfolgt am {o.Wiederaufnahme.Value:dd.MM.yyyy} zu denselben Bedingungen wie vor der Mutterschaft.");
        else if (o.Rueckkehr == "ANDERS" && o.Wiederaufnahme.HasValue)
        {
            var pensum = o.PensumProzent.HasValue ? $"{o.PensumProzent.Value:0.##} %" : "____ %";
            var rest   = string.IsNullOrWhiteSpace(o.RueckkehrRestaurant) ? d.RestaurantName : o.RueckkehrRestaurant;
            absaetze.Add($"Die Wiederaufnahme deiner Arbeit erfolgt am {o.Wiederaufnahme.Value:dd.MM.yyyy} zu {pensum} im Restaurant {rest}. Dein entsprechender Stundenlohn bleibt unverändert. Beiliegend senden wir dir den Zusatz zu deinem derzeitigen Vertrag in zweifacher Ausfertigung — bitte unterzeichne diesen und sende ihn so bald wie möglich mithilfe des Antwortkuverts an uns zurück.");
        }
        else if (beendigung)
        {
            var ende = urlaubEnde;
            absaetze.Add($"Entsprechend deiner eigenen Entscheidung bestätigen wir dir hiermit die Beendigung unseres Arbeitsverhältnisses zum {ende:dd.MM.yyyy}, dem letzten Tag deines Mutterschaftsurlaubs. Die Mutterschaftsentschädigung wird dir bis zu diesem Datum ausbezahlt.");
            absaetze.Add("Wir bitten dich, die beiliegenden Dokumente zu lesen, zu unterzeichnen und mit dem Antwortkuvert an uns zurückzusenden: das Formular «Bitte um Referenzen» (bei fehlender Rücksendung gehen wir davon aus, dass du keine Übermittlung von Referenzen wünschst) sowie die Information «Taggeldversicherung und Unfalldeckung» (bei fehlender Rücksendung gehen wir davon aus, dass du entsprechend informiert worden bist).");
            if (o.Pensionskasse)
                absaetze.Add("An GastroSocial zurückzusenden: das Formular «Freizügigkeitsleistung».");
            absaetze.Add("Dein Arbeitszeugnis senden wir dir auf das Ende des Arbeitsverhältnisses zu.");
        }

        absaetze.Add($"Bitte fülle im Formular «Beantragung der Mutterschaftsentschädigung» den Teil A «Von der Mutter auszufüllen» (Seite 1 und 2) aus, unterzeichne es anschliessend auf Seite 4 und sende es uns bis spätestens am {eoFrist:dd.MM.yyyy} (4 Wochen nach der Entbindung) gemeinsam mit den angeforderten Nachweisen zurück.");
        absaetze.Add("Bis zur Rücksendung der Unterlagen stehen wir dir gerne zur Beantwortung möglicher Fragen zur Verfügung und verbleiben mit freundlichen Grüssen.");

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.3f, Unit.Centimetre);
                page.MarginHorizontal(2.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).FontColor(Dark).LineHeight(1.35f));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Item().Text(string.Join(" · ", firmaLines)).FontSize(8.5f).FontColor(Muted);

                    // MA-Adresse im C5-Fenster (wie Vereinbarung/Kuendigung)
                    col.Item().Height(40);
                    if (o.Eingeschrieben)
                        col.Item().Text("EINSCHREIBEN").Bold().LetterSpacing(0.06f).FontSize(9.5f);
                    col.Item().PaddingTop(o.Eingeschrieben ? 3 : 16).Column(c =>
                    {
                        foreach (var ln in maLines) c.Item().Text(ln);
                    });

                    col.Item().PaddingTop(22).Text($"{d.Ort}, {d.Datum:dd.MM.yyyy}");

                    col.Item().PaddingTop(22).Text(beendigung
                        ? "Mutterschaftsurlaub und Beendigung des Arbeitsverhältnisses"
                        : "Mutterschaftsurlaub und Wiederaufnahme der Arbeit").Bold().FontSize(12.5f);

                    col.Item().PaddingTop(16).Text($"Liebe {d.MaVorname},");

                    foreach (var a in absaetze)
                        col.Item().PaddingTop(10).Text(a);

                    // Gruss + Unterschrift (Unterschriftsberechtigte; Platz zum
                    // handschriftlichen Unterschreiben, Bild nur wenn selbst).
                    col.Item().PaddingTop(20).Text("Freundliche Grüsse");
                    if (!string.IsNullOrWhiteSpace(d.FirmaName))
                        col.Item().PaddingTop(2).Text($"{d.FirmaName}{(string.IsNullOrWhiteSpace(d.RestaurantName) ? "" : " · " + d.RestaurantName)}").Bold();
                    if (d.SignaturePng is { Length: > 0 })
                        col.Item().PaddingTop(6).Height(44).AlignLeft().Image(d.SignaturePng).FitHeight();
                    else
                        col.Item().PaddingTop(6).Height(50);
                    col.Item().Text(d.UnterzeichnerName ?? "");
                    if (!string.IsNullOrWhiteSpace(d.UnterzeichnerTitel))
                        col.Item().Text(d.UnterzeichnerTitel!).FontColor(Muted);
                });

                page.Footer().Column(col =>
                {
                    col.Item().Text("Anhänge: erwähnt").FontSize(9.5f).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }

    // ═══════ EIGNUNGSBEURTEILUNG — AERZTLICHES ZEUGNIS (MuSchV Art. 3) ══════
    // (Walter-Vorgabe 16.07.2026, nach Word-Vorlage «Eignungsbeurteilung Arzt»:
    // wird dem Arzt zusammen mit der Risikobeurteilung mitgegeben. Arzt,
    // Arbeitgeber und untersuchte Frau vorausgefuellt; Entscheid-Kaestchen
    // kreuzt der Arzt an. Seite 2 = Rechtsgrundlagen-Auszug.)
    public byte[] GenerateEignung(MvCommon d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var firmaLines = new[]
            {
                $"{d.FirmaName}{(string.IsNullOrWhiteSpace(d.RestaurantName) ? "" : " · " + d.RestaurantName)}",
                d.FirmaStrasse, d.FirmaPlzOrt,
                string.IsNullOrWhiteSpace(d.FirmaTelefon) ? null : $"Tel. {d.FirmaTelefon}"
            }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!).ToList();
        var maAdresse = string.Join(", ", new[] { d.MaStrasse, d.MaPlzOrt }
            .Where(x => !string.IsNullOrWhiteSpace(x)));

        return Document.Create(doc =>
        {
            // ── Seite 1: das Formular ──
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.2f, Unit.Centimetre);
                page.MarginHorizontal(2.0f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(9.5f).LineHeight(1.25f).FontColor(Dark));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(12).Column(col =>
                {
                    col.Item().AlignCenter().Text("Ärztliches Zeugnis").FontSize(15f).Bold();
                    col.Item().AlignCenter().Text("für schwangere Frauen und stillende Mütter (nach Artikel 3 der Mutterschutzverordnung)")
                        .FontSize(9f).FontColor(Muted);

                    // Arzt links, Arbeitgeber rechts
                    col.Item().PaddingTop(12).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Betreuende Ärztin / betreuender Arzt:").Bold().FontSize(9f);
                            // Bewusst LEER — jeder Arzt setzt hier seinen
                            // eigenen Praxis-Stempel (Walter 16.07.2026).
                            c.Item().PaddingTop(4).Height(66)
                                .Border(0.7f).BorderColor(Muted).Padding(6)
                                .AlignBottom().Text("Stempel der Praxis").FontSize(7.5f).FontColor(Muted).Italic();
                        });
                        r.ConstantItem(24);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Arbeitgeber:").Bold().FontSize(9f);
                            foreach (var ln in firmaLines) c.Item().Text(ln);
                        });
                    });

                    // Untersuchte Frau
                    col.Item().PaddingTop(12).Border(0.8f).BorderColor(Dark).Padding(8).Column(c =>
                    {
                        c.Item().Text("Untersuchte Frau").Bold();
                        c.Item().PaddingTop(3).Text(t =>
                        {
                            t.Span("Name, Vorname, Geburtsdatum:  ").FontColor(Muted).FontSize(9f);
                            t.Span($"{d.MaName} {d.MaVorname}").Bold();
                            if (d.MaGeburtsdatum.HasValue) t.Span($", {d.MaGeburtsdatum.Value:dd.MM.yyyy}").Bold();
                        });
                        c.Item().PaddingTop(3).Text(t =>
                        {
                            t.Span("Adresse:  ").FontColor(Muted).FontSize(9f);
                            t.Span(maAdresse).Bold();
                        });
                        c.Item().PaddingTop(3).Text(t =>
                        {
                            t.Span("Berechneter Geburtstermin:  ").FontColor(Muted).FontSize(9f);
                            t.Span($"{d.ErrechneterTermin:dd.MM.yyyy}").Bold();
                        });
                    });

                    // Entscheid
                    col.Item().PaddingTop(12).Text("Entscheid").Bold().FontSize(11f);
                    col.Item().PaddingTop(2).Text(
                        "Bei der vorgenannten schwangeren Frau / stillenden Mutter wurde von mir eine Beurteilung der Beschäftigung im vorgesehenen Betrieb oder Betriebsteil während der Schwangerschaft / Stillzeit vorgenommen. Das Ergebnis der Beurteilung lautet (Zutreffendes ankreuzen):")
                        .FontSize(9f).FontColor(Muted);

                    void Haupt(ColumnDescriptor c, string text)
                    {
                        c.Item().PaddingTop(8).Row(r =>
                        {
                            r.ConstantItem(18).Element(e => e.PaddingTop(1).Width(12).Height(12).Border(1.1f).BorderColor(Dark));
                            r.RelativeItem().Text(text).Bold().FontSize(10f);
                        });
                    }
                    void Sub(ColumnDescriptor c, string text)
                    {
                        c.Item().PaddingTop(3).PaddingLeft(18).Row(r =>
                        {
                            r.ConstantItem(15).Element(e => e.PaddingTop(1).Width(9).Height(9).Border(0.9f).BorderColor(Dark));
                            r.RelativeItem().Text(text).FontSize(9f);
                        });
                    }

                    col.Item().Column(c =>
                    {
                        Haupt(c, "Die Beschäftigung ist vorbehaltlos möglich.");

                        Haupt(c, "Die Beschäftigung ist nur unter folgenden bestimmten Voraussetzungen möglich:");
                        Sub(c, "Einsatz unter folgenden Bedingungen (Schutzmassnahmen): …………………………………………………………");
                        c.Item().PaddingLeft(33).Text("……………………………………………………………………………………………………………………").FontSize(9f).FontColor(Muted);
                        Sub(c, $"Entsprechend der vorliegenden Risikobeurteilung, datiert vom {DateTime.Today:dd.MM.yyyy}.");
                        Sub(c, "Andere: ………………………………………………………………………………………………………………");
                        c.Item().PaddingTop(3).PaddingLeft(18).Text("Bemerkungen: …………………………………………………………………………………………………………").FontSize(9f);
                        Sub(c, "Eine Rücksprache mit dem Arbeitgeber ist erforderlich.");
                        Sub(c, "Eine Rücksprache mit dem ASA-Spezialisten ist erforderlich.");

                        Haupt(c, "Die Beschäftigung ist aus folgendem Grund nicht oder zurzeit nicht möglich (Beschäftigungsverbot):");
                        Sub(c, "Fehlende oder ungenügende Risikobeurteilung.");
                        Sub(c, "Die erforderlichen Schutzmassnahmen sind nicht umgesetzt / werden nicht eingehalten.");
                        Sub(c, "Die erforderlichen Schutzmassnahmen sind nicht genügend wirksam.");
                        Sub(c, "Andere Hinweise auf eine Gefährdung: ……………………………………………………………………………");

                        c.Item().PaddingTop(10).Row(r =>
                        {
                            r.ConstantItem(18).Element(e => e.PaddingTop(1).Width(12).Height(12).Border(1.1f).BorderColor(Dark));
                            r.RelativeItem().Text("Neubeurteilung in ………… Wochen").FontSize(10f);
                        });
                    });

                    col.Item().PaddingTop(10).Text(
                        "Zur Beurteilung wurden die Kriterienliste der Mutterschutzverordnung, die Risikobeurteilung (falls vorhanden), die Befragung und die Untersuchung der Arbeitnehmerin berücksichtigt.")
                        .FontSize(8.5f).FontColor(Muted);
                });

                page.Footer().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Width(190).LineHorizontal(0.8f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text("Ort und Datum").FontSize(9f);
                        });
                        r.ConstantItem(40);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Width(220).LineHorizontal(0.8f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text("Unterschrift und Stempel Ärztin / Arzt").FontSize(9f);
                        });
                    });
                    col.Item().PaddingTop(6).Text("Der Entscheid geht an die untersuchte Frau und deren Arbeitgeber.")
                        .FontSize(8.5f).FontColor(Muted).Italic();
                });
            });

            // ── Seite 2: Rechtsgrundlagen (Auszug WBF-Verordnung) ──
            // MUSS auf EINE Seite passen (Walter 16.07.2026), aber lesbar:
            // 8.5pt zweispaltig, Spalten nach Textmenge balanciert
            // (Art. 2-6 links, Art. 7-18 rechts), schmalere Raender.
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.1f, Unit.Centimetre);
                page.MarginBottom(1.1f, Unit.Centimetre);
                page.MarginHorizontal(1.5f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(8.5f).LineHeight(1.2f).FontColor(Dark));

                page.Content().Column(col =>
                {
                    col.Item().Text("Rechtsgrundlagen").Bold().FontSize(12.5f);
                    col.Item().Text("Auszug aus der Verordnung des WBF über gefährliche und beschwerliche Arbeiten bei Schwangerschaft und Mutterschaft vom 20. März 2001 (Stand am 1. Juli 2015)")
                        .FontSize(8.5f).FontColor(Muted);

                    col.Item().PaddingTop(7).Row(r =>
                    {
                        void T(ColumnDescriptor c, string titel, string text)
                        {
                            c.Item().PaddingTop(7).Text(titel).Bold().FontSize(9f);
                            c.Item().PaddingTop(1).Text(text);
                        }

                        r.RelativeItem().Column(c =>
                        {
                            T(c, "Art. 2 Grundsatz",
                              "1 Die Beurteilung des Gesundheitszustandes der schwangeren Frau oder der stillenden Mutter ist durch den Arzt oder die Ärztin vorzunehmen, der oder die im Rahmen der Schwangerschaft die Arbeitnehmerin medizinisch betreut. "
                            + "2 Der Arzt oder die Ärztin nimmt eine Eignungsuntersuchung vor und berücksichtigt: die Befragung und Untersuchung der Arbeitnehmerin; das Ergebnis der vom Betrieb durch eine fachlich kompetente Person nach Artikel 17 veranlassten Risikobeurteilung; allenfalls weitere Informationen aus einer Rücksprache mit dem Verfasser oder der Verfasserin der Risikobeurteilung oder dem Arbeitgeber. "
                            + "3 Eine schwangere Frau oder eine stillende Mutter darf im von einer Gefahr betroffenen Betrieb oder Betriebsteil nicht beschäftigt werden, wenn der Arzt oder die Ärztin feststellt, dass: a. keine oder eine ungenügende Risikobeurteilung vorgenommen wurde; b. die erforderlichen Schutzmassnahmen nicht umgesetzt oder nicht eingehalten werden; c. die getroffenen Schutzmassnahmen nicht genügend wirksam sind; oder d. Hinweise auf eine Gefährdung bestehen.");
                            T(c, "Art. 3 Ärztliches Zeugnis",
                              "1 Der untersuchende Arzt oder die untersuchende Ärztin hält in einem Zeugnis fest, ob eine Beschäftigung am betreffenden Arbeitsplatz vorbehaltlos, nur unter bestimmten Voraussetzungen oder nicht mehr möglich ist. "
                            + "2 Er oder sie teilt der betroffenen Arbeitnehmerin und dem Arbeitgeber das Ergebnis der Beurteilung mit, damit der Arbeitgeber nötigenfalls die erforderlichen Massnahmen treffen kann.");
                            T(c, "Art. 4 Kostentragung",
                              "Der Arbeitgeber trägt die Kosten für die Aufwendungen nach den Artikeln 2 und 3.");
                            T(c, "Art. 5 Vermutung der Gefährdung",
                              "Sind die Voraussetzungen nach den Artikeln 7-13 erfüllt, wird eine Gefährdung von Mutter und Kind vermutet.");
                            T(c, "Art. 6 Gewichtung der Kriterien",
                              "Bei der Gewichtung der Kriterien sind auch die konkreten Umstände im Betrieb zu berücksichtigen, namentlich das Zusammenwirken verschiedener Belastungen, die Expositionsdauer, die Häufigkeit der Belastung oder der Gefährdung und weitere Faktoren mit Einfluss auf das Gefahrenpotenzial.");
                        });
                        r.ConstantItem(16);
                        r.RelativeItem().Column(c =>
                        {
                            T(c, "Art. 7-13 (Kriterien)",
                              "Art. 7 Bewegen schwerer Lasten · Art. 8 Arbeiten bei Kälte, Hitze oder Nässe · Art. 9 Bewegungen und Körperhaltungen, die zu vorzeitiger Ermüdung führen · Art. 10 Mikroorganismen · Art. 11 Einwirkung von Lärm · Art. 12 Arbeiten unter Einwirkung von ionisierender und nichtionisierender Strahlung · Art. 13 Einwirkung von chemischen Gefahrstoffen.");
                            T(c, "Art. 14 Stark belastende Arbeitszeitsysteme",
                              "Frauen dürfen während der gesamten Schwangerschaft und danach während der Stillzeit nicht Nacht- und Schichtarbeit leisten, wenn diese mit gefährlichen oder beschwerlichen Arbeiten nach den Artikeln 7-13 verbunden sind oder wenn ein besonders gesundheitsbelastendes Schichtsystem vorliegt. Als besonders gesundheitsbelastend gelten Schichtsysteme mit regelmässiger Rückwärtsrotation (Nacht-, Spät-, Frühschicht) oder mit mehr als drei hintereinander liegenden Nachtschichten.");
                            T(c, "Art. 15 Akkordarbeit und taktgebundene Arbeit",
                              "Nicht zulässig ist Arbeit im Akkord oder taktgebundene Arbeit, wenn der Arbeitsrhythmus durch eine Maschine oder technische Einrichtung vorgegeben wird und von der Arbeitnehmerin nicht beeinflusst werden kann.");
                            T(c, "Art. 16 Besondere Beschäftigungsverbote",
                              "1 Schwangere Frauen dürfen nicht beschäftigt werden für Arbeiten bei Überdruck wie Arbeiten in Druckkammern oder Taucharbeiten. 2 Schwangere Frauen dürfen Räumlichkeiten mit sauerstoffreduzierter Atmosphäre nicht betreten. 3 Der Arbeitgeber muss Frauen vor einer solchen Beschäftigung in angemessener Weise über die Gefahren während der Schwangerschaft informieren.");
                            T(c, "Art. 17 Fachlich kompetente Personen",
                              "1 Fachlich kompetente Personen nach Artikel 63 Absatz 1 ArGV 1 sind Arbeitsärzte und Arbeitsärztinnen sowie Arbeitshygieniker und Arbeitshygienikerinnen sowie weitere Fachspezialisten, die sich über die notwendigen Kenntnisse und Erfahrungen zur Durchführung einer Risikobeurteilung ausweisen können. 2 Es ist sicherzustellen, dass alle zu beurteilenden Fachbereiche kompetent abgedeckt werden.");
                            T(c, "Art. 18 Information",
                              "1 Der Arbeitgeber sorgt dafür, dass die zur Risikobeurteilung beigezogenen Personen zu allen Informationen gelangen, die für eine Beurteilung der betrieblichen Situation und zur Überprüfung der getroffenen Schutzmassnahmen notwendig sind. 2 Er sorgt auch dafür, dass der Arzt oder die Ärztin nach Artikel 2 zu den für die Beurteilung notwendigen Informationen gelangt.");
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    // ═════════════════════════════ CHECKLISTE ═══════════════════════════════
    public byte[] GenerateCheckliste(MvCommon d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.2f, Unit.Centimetre);
                page.MarginHorizontal(2.0f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).LineHeight(1.3f).FontColor(Dark));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    // Ganze Filial-Adresse in EINER Zeile (Walter 16.07.2026).
                    var absender = string.Join(" · ", new[] { d.FirmaName, d.RestaurantName, d.FirmaStrasse, d.FirmaPlzOrt }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));
                    col.Item().Text(absender).FontSize(9f).FontColor("#6b6152");

                    col.Item().PaddingTop(14).AlignCenter().Text("CHECKLISTE MUTTERSCHAFTSGESPRÄCH")
                        .FontSize(15f).Bold().LetterSpacing(0.06f);
                    col.Item().PaddingTop(2).AlignCenter()
                        .Text("Grundlage für die Mutterschaftsvereinbarung — gemeinsam mit der Mitarbeiterin durcharbeiten")
                        .FontSize(9f).FontColor(Muted);

                    // Eckdaten
                    col.Item().PaddingTop(14).Row(r =>
                    {
                        r.RelativeItem().Column(rc =>
                        {
                            rc.Item().Text(t =>
                            {
                                t.Span("Mitarbeiterin:  ").Bold();
                                t.Span($"{d.MaVorname} {d.MaName}");
                            });
                            // 2. Zeile: MA-Adresse + Geburtsdatum, KEINE Personalnummer
                            // (Walter 16.07.2026).
                            var teile = new List<string>();
                            var adr = string.Join(", ", new[] { d.MaStrasse, d.MaPlzOrt }
                                .Where(x => !string.IsNullOrWhiteSpace(x)));
                            if (!string.IsNullOrWhiteSpace(adr)) teile.Add(adr);
                            if (d.MaGeburtsdatum.HasValue) teile.Add($"geb. {d.MaGeburtsdatum.Value:dd.MM.yyyy}");
                            if (teile.Count > 0)
                                rc.Item().Text(string.Join("  ·  ", teile)).FontSize(9f).FontColor(Muted);
                        });
                        r.ConstantItem(250).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text(t =>
                            {
                                t.Span("Gesprächsdatum:  ").Bold();
                                t.Span("______ / ______ / ________");
                            });
                            c.Item().AlignRight().PaddingRight(30).Text("Tag         Monat        Jahr")
                                .FontSize(7f).FontColor(Muted);
                        });
                    });
                    col.Item().PaddingTop(3).Text(t =>
                    {
                        t.Span("Errechneter Geburtstermin:  ").Bold();
                        t.Span($"{d.ErrechneterTermin:dd.MM.yyyy}");
                        t.Span("      Beginn Schwangerschaft (ET − 280 Tage):  ").Bold();
                        t.Span($"{d.ErrechneterTermin.AddDays(-280):dd.MM.yyyy}");
                    });

                    // Punkte
                    var punkte = new (string Titel, string? Detail)[]
                    {
                        ("Voraussichtlicher Geburtstermin besprochen",
                         $"Die Geburt findet voraussichtlich um den {d.ErrechneterTermin:dd.MM.yyyy} herum statt — die ärztliche Terminbestätigung liegt vor."),
                        ("Anspruch erklärt: 14 Wochen bezahlte Mutterschaft",
                         "14 Wochen (98 Tage) bezahlter Mutterschaftsurlaub, beginnend mit dem Tag der Geburt (Mutterschaftsentschädigung EO)."),
                        ("Kündigungsschutz erklärt (OR Art. 336c)",
                         "Ab Beginn der Schwangerschaft bis 16 Wochen nach der Niederkunft."),
                        ("Mutterschutz am Arbeitsplatz besprochen",
                         "Risiko-Assessment, max. 9 Stunden/Tag, auf Verlangen keine Nachtarbeit — Details siehe Fristen-Übersicht Mutterschaft."),
                        ("Verlängerung des Urlaubs besprochen",
                         "Verlängerung um  ______  bezahlte Urlaubstage  und/oder  ______  unbezahlte Urlaubstage  (leer = keine Verlängerung)."),
                        ("Rückkehr an den Arbeitsplatz besprochen", null),
                        ("Hinweis Schlussberechnung",
                         "Das effektive Geburtsdatum ist massgebend für die Schlussberechnung der Tage und das Datum der Wiederaufnahme bzw. Beendigung."),
                        ("Unterlagen nach der Geburt",
                         "Unterlagen zur Beantragung der Mutterschaftsentschädigung sowie der Familien-/Kinderzulagen werden nach der Geburtsmeldung per Post zugestellt."),
                        ("Geburtsmeldung vereinbart",
                         "Die Mitarbeiterin meldet die Geburt umgehend der Filiale (Geburtsurkunde nachreichen)."),
                        ("Zustellung der Vereinbarung", null),
                    };

                    col.Item().PaddingTop(12).Column(list =>
                    {
                        // Unter-Option mit eigenem kleinen Ankreuz-Kaestchen
                        // (gezeichnet — das Zeichen ☐ fehlt in Arial und wuerde
                        // als Fragezeichen gerendert, Walter-Feedback 16.07.2026).
                        void SubOption(ColumnDescriptor c, string text)
                        {
                            c.Item().PaddingTop(2).Row(sr =>
                            {
                                sr.ConstantItem(16).Element(e =>
                                {
                                    e.PaddingTop(1).Width(10).Height(10).Border(1.0f).BorderColor(Dark);
                                });
                                sr.RelativeItem().Text(text).FontSize(9.5f).FontColor(Muted);
                            });
                        }

                        foreach (var (titel, detail) in punkte)
                        {
                            list.Item().PaddingBottom(7).Row(r =>
                            {
                                r.ConstantItem(20).Element(e =>
                                {
                                    e.Width(13).Height(13).Border(1.1f).BorderColor(Dark);
                                });
                                r.RelativeItem().Column(c =>
                                {
                                    c.Item().Text(titel).Bold().FontSize(10.5f);
                                    if (detail != null)
                                        c.Item().Text(detail).FontSize(9.5f).FontColor(Muted);
                                    if (titel.StartsWith("Rückkehr"))
                                    {
                                        SubOption(c, "dieselben Vertragsbedingungen wie vor der Geburt");
                                        SubOption(c, "geänderte Bedingungen: Pensum ______ %, Restaurant ______________________, neue Verfügbarkeitszeiten beilegen");
                                        SubOption(c, "keine Rückkehr nach dem Mutterschaftsurlaub (auf Wunsch der Mitarbeiterin)");
                                    }
                                    if (titel.StartsWith("Zustellung"))
                                    {
                                        SubOption(c, "persönliche Aushändigung");
                                        SubOption(c, "per Einschreiben");
                                    }
                                });
                            });
                        }
                    });

                    // Bemerkungen — nur Titel, freier Platz zum Schreiben
                    // (Walter 16.07.2026: keine Striche).
                    col.Item().PaddingTop(6).Text("Bemerkungen:").Bold().FontSize(10f);
                });

                // Unterschriften: Arbeitgeber (Name der Geschäftsführerin) LINKS,
                // Mitarbeiterin RECHTS — Walter-Vorgabe 16.07.2026, gilt für
                // JEDES Formular mit zwei Unterschriften.
                page.Footer().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Width(190).LineHorizontal(0.8f).LineColor(Dark);
                            // Wie beim Arbeitsvertrag: Name + Funktion der
                            // Unterschriftsberechtigten (Walter 16.07.2026).
                            c.Item().PaddingTop(3).Text(string.IsNullOrWhiteSpace(d.UnterzeichnerName)
                                ? "Arbeitgeber"
                                : d.UnterzeichnerName!).FontSize(9f);
                            if (!string.IsNullOrWhiteSpace(d.UnterzeichnerTitel))
                                c.Item().Text(d.UnterzeichnerTitel!).FontSize(9f).FontColor(Muted);
                        });
                        r.ConstantItem(40);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Width(190).LineHorizontal(0.8f).LineColor(Dark);
                            c.Item().PaddingTop(3).Text($"{d.MaVorname} {d.MaName}").FontSize(9f);
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    // ═══════════════════════════ VEREINBARUNG ═══════════════════════════════
    public byte[] GenerateVereinbarung(MvCommon d, MvOptionen o)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        bool keineRueckkehr = o.Rueckkehr == "KEINE";

        // Nummerierte Vereinbarungs-Punkte (nach Word-Vorlage)
        var punkte = new List<string>
        {
            $"Die Geburt findet voraussichtlich um den {d.ErrechneterTermin:dd.MM.yyyy} herum statt.",
            "Du hast Anspruch auf 14 Wochen (98 Tage) bezahlte Mutterschaft, die mit dem Tag der Geburt beginnt."
        };
        if (o.VerlBezahlt > 0 && o.VerlUnbezahlt > 0)
            punkte.Add($"Entsprechend deinem Antrag wird deine Mutterschaft um {o.VerlBezahlt} bezahlte und {o.VerlUnbezahlt} unbezahlte Urlaubstage verlängert.");
        else if (o.VerlBezahlt > 0)
            punkte.Add($"Entsprechend deinem Antrag wird deine Mutterschaft um {o.VerlBezahlt} bezahlte Urlaubstage verlängert.");
        else if (o.VerlUnbezahlt > 0)
            punkte.Add($"Entsprechend deinem Antrag wird deine Mutterschaft um {o.VerlUnbezahlt} unbezahlte Urlaubstage verlängert.");

        if (o.Rueckkehr == "GLEICH")
            punkte.Add("Für deine Rückkehr an den Arbeitsplatz nach der Mutterschaft bleiben die Vertragsbedingungen dieselben wie vor der Geburt eures Kindes.");
        else if (o.Rueckkehr == "ANDERS")
        {
            var pensum = o.PensumProzent.HasValue ? $"{o.PensumProzent.Value:0.##} %" : "____ %";
            var rest   = string.IsNullOrWhiteSpace(o.RueckkehrRestaurant) ? d.RestaurantName : o.RueckkehrRestaurant;
            punkte.Add($"Für deine Rückkehr an den Arbeitsplatz nach der Mutterschaft ändern sich die Vertragsbedingungen wie vereinbart mit einer Beschäftigungszeit in Höhe von {pensum} entsprechend den beigelegten neuen Verfügbarkeitszeiten. Dein zukünftiger Arbeitsplatz befindet sich im Restaurant {rest}. Dein entsprechender Stundenlohn bleibt unverändert.");
        }

        punkte.Add(keineRueckkehr
            ? "Selbstverständlich gilt das Datum der Geburt als massgebend für die Schlussberechnung der Tage und des Datums der Beendigung des Arbeitsverhältnisses."
            : "Selbstverständlich gilt das Datum der Geburt als massgebend für die Schlussberechnung der Tage und des Datums der Wiederaufnahme deiner Beschäftigung.");
        punkte.Add("Sobald wir von der Geburt eures Kindes benachrichtigt werden, senden wir dir die Unterlagen zur Beantragung der Mutterschaftsentschädigung sowie der Familien- und/oder Kinderzulagen per Post nach Hause.");

        string einleitung = keineRueckkehr
            ? $"Im Anschluss an unser Gespräch vom {o.GespraechsDatum:dd.MM.yyyy} nehmen wir zur Kenntnis, dass du deine Beschäftigung nach deinem Mutterschaftsurlaub nicht wieder aufnehmen möchtest. Wir bestätigen dir nachfolgend die gemeinsam getroffene Vereinbarung:"
            : $"Im Anschluss an unser Gespräch vom {o.GespraechsDatum:dd.MM.yyyy} bestätigen wir dir nachfolgend die gemeinsam getroffene Vereinbarung:";

        var firmaLines = new[] { d.FirmaName, d.FirmaStrasse, d.FirmaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        var maLines = new[] { $"{d.MaVorname} {d.MaName}".Trim(), d.MaStrasse, d.MaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.3f, Unit.Centimetre);
                page.MarginHorizontal(2.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).FontColor(Dark).LineHeight(1.35f));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    foreach (var ln in firmaLines)
                        col.Item().Text(ln).FontSize(8.5f).FontColor(Muted);

                    col.Item().PaddingTop(20).Text(o.Eingeschrieben ? "EINSCHREIBEN" : "PERSÖNLICHE AUSHÄNDIGUNG")
                        .Bold().LetterSpacing(0.06f).FontSize(9.5f);

                    col.Item().PaddingTop(4).Column(c =>
                    {
                        foreach (var ln in maLines) c.Item().Text(ln);
                    });

                    col.Item().PaddingTop(22).Text($"{d.Ort}, {d.Datum:dd.MM.yyyy}");

                    col.Item().PaddingTop(22).Text("Mutterschaftsvereinbarung").Bold().FontSize(12.5f);

                    col.Item().PaddingTop(16).Text($"Liebe {d.MaVorname},");

                    col.Item().PaddingTop(10).Text(einleitung);

                    col.Item().PaddingTop(8).Column(list =>
                    {
                        int n = 1;
                        foreach (var p in punkte)
                        {
                            list.Item().PaddingTop(6).Row(r =>
                            {
                                r.ConstantItem(22).Text($"{n}.").Bold();
                                r.RelativeItem().Text(p);
                            });
                            n++;
                        }
                    });

                    col.Item().PaddingTop(14).Text("Solltest du Fragen haben, stehen wir dir gern zur Verfügung.");

                    // Gruss + Unterschrift der Geschäftsführerin direkt nach dem
                    // Text (Walter 16.07.2026: weiter nach oben, nicht am
                    // Seitenende verankert) — mit grosszügigem Platz zum
                    // Unterschreiben vor dem Namen.
                    col.Item().PaddingTop(24).Text("Freundliche Grüsse");
                    if (!string.IsNullOrWhiteSpace(d.FirmaName))
                        col.Item().PaddingTop(2).Text($"{d.FirmaName}{(string.IsNullOrWhiteSpace(d.RestaurantName) ? "" : " · " + d.RestaurantName)}").Bold();

                    if (d.SignaturePng is { Length: > 0 })
                        col.Item().PaddingTop(8).Height(56).AlignLeft().Image(d.SignaturePng).FitHeight();
                    else
                        col.Item().PaddingTop(8).Height(64);

                    col.Item().Text(d.UnterzeichnerName ?? "");
                    if (!string.IsNullOrWhiteSpace(d.UnterzeichnerTitel))
                        col.Item().Text(d.UnterzeichnerTitel!).FontColor(Muted);
                });

                // Empfangs-/Einverständnis-Bestätigung der MA — NUR bei
                // persönlicher Aushändigung (Walter 16.07.2026); beim
                // Einschreiben-Versand entfällt der Block. Ohne Striche —
                // nur Beschriftungen mit Platz darüber.
                if (!o.Eingeschrieben)
                {
                    page.Footer().Column(col =>
                    {
                        col.Item().Text("Einverstanden und Original erhalten:").FontSize(9f).FontColor(Muted);
                        col.Item().PaddingTop(42).Row(r =>
                        {
                            r.RelativeItem().Text("Ort und Datum").FontSize(8.5f).FontColor(Muted);
                            r.ConstantItem(40);
                            r.RelativeItem().Text($"{d.MaVorname} {d.MaName}").FontSize(8.5f).FontColor(Muted);
                        });
                    });
                }
            });
        }).GeneratePdf();
    }
}
