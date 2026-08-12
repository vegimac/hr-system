using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Globalization;

namespace HrSystem.Services;

/// <summary>
/// Arbeitszeugnis bei MA-Austritt (Walter-Vorgabe 14.07.2026).
/// Text nach dem Mirus-/Reinach-Muster «Gherasim» mit drei Qualitätsstufen
/// (durchschnitt / gut / sehr_gut) und Mehrfachauswahl der verrichteten
/// Arbeit (kueche / kasse / drive). Briefkopf = gelbes Banner wie überall
/// (letterhead_banner.png, analog ContractPdfService). Unterschrift =
/// eingeloggter User (Klarname + signature_png), Telefon = Filiale.
/// </summary>
public record ArbeitszeugnisInput(
    string CompanyName,          // «Schaub Restaurants GmbH»
    string RestaurantName,       // «McDonald's Restaurant Reinach»
    string CompanyStreet,        // «Aarauerstrasse 72»
    string CompanyZipCity,       // «5734 Reinach»
    string? CompanyPhone,
    string? CompanyEmail,
    string Ort,                  // für «Reinach, 24. April 2025» + «in unserem Restaurant in Reinach»
    DateTime Datum,
    string Salutation,           // Herr / Frau
    string FirstName,
    string LastName,
    DateTime? DateOfBirth,
    string? WohnOrt,             // «aus Birrwil»
    string? EmpStreet,
    string? EmpZipCity,
    DateTime Von,
    DateTime Bis,
    bool Vollzeit,
    bool Female,
    string Qualitaet,            // durchschnitt | gut | sehr_gut
    IReadOnlyList<string> Bereiche,   // kueche | kasse | drive
    string SignatoryName,
    string? SignatoryTitle,
    byte[]? SignaturePng,
    bool AufEigenenWunsch = false,
    /// <summary>Funktions-Text aus der Vorlage (z.B. «Crew-Trainerin»,
    /// «Schichtkoordinator»). NULL = Fallback Teilzeit/Vollzeit-Mitarbeiter/in.</summary>
    string? Funktion = null,
    /// <summary>Explizit gewählte Aufgaben (13er-Katalog der Vorlage).
    /// NULL/leer = Ableitung aus den Bereichen (Kasse/Drive/Küche).</summary>
    IReadOnlyList<string>? Aufgaben = null,
    /// <summary>true = ZWISCHENzeugnis (Vorlage «289 Hendschiken», 15.07.2026):
    /// Präsens, «ist seit dem … tätig», Arbeitsmittel-Absatz, Abschluss
    /// «wird auf Wunsch ausgestellt» — kein Austritts-/Dank-Absatz.</summary>
    bool Zwischen = false,
    /// <summary>true = ARBEITSBESTÄTIGUNG (Vorlage «244 Sursee», 15.07.2026):
    /// nur der Bestätigungssatz — «angestellt ist» (aktiv, seit Eintritt)
    /// bzw. «angestellt war» (Von–Bis), abgeleitet aus Bis &lt; Datum.</summary>
    bool Bestaetigung = false
);

public class ArbeitszeugnisPdfService
{
    private const string Dark = "#27251F";

    private static byte[]? _bannerBytes;
    private static byte[] BannerBytes => _bannerBytes ??=
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    /// <summary>
    /// Einseitigkeit ist PFLICHT (Walter-Vorgabe 12.08.2026): die interne
    /// Schätzung kann bei sehr langem Inhalt danebenliegen — deshalb wird das
    /// Ergebnis nachgemessen (echte Seitenzahl via iText) und bei Überlauf mit
    /// stufenweise kleinerer Schrift neu gesetzt, bis es auf 1 A4 passt.
    /// </summary>
    public byte[] Generate(ArbeitszeugnisInput d)
    {
        foreach (var fs in new[] { 10.5f, 10.0f, 9.5f, 9.0f, 8.5f })
        {
            var bytes = GenerateInternal(d, fs);
            try
            {
                using var reader = new iText.Kernel.Pdf.PdfReader(new MemoryStream(bytes));
                using var pdf = new iText.Kernel.Pdf.PdfDocument(reader);
                if (pdf.GetNumberOfPages() <= 1) return bytes;
            }
            catch { return bytes; /* Messung fehlgeschlagen → Ergebnis so nehmen */ }
        }
        return GenerateInternal(d, 8.5f);
    }

    private byte[] GenerateInternal(ArbeitszeugnisInput d, float baseFont)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var ci = new CultureInfo("de-CH");
        bool f = d.Female;

        // Pronomen/Formen — Muster nutzt «Herr Gherasim» (Nominativ) durchgängig.
        string anrede    = string.IsNullOrWhiteSpace(d.Salutation) ? (f ? "Frau" : "Herr") : d.Salutation.Trim();
        string nameKurz  = $"{anrede} {d.LastName}".Trim();
        string er        = f ? "sie" : "er";
        string erCap     = f ? "Sie" : "Er";
        string ihm       = f ? "ihr" : "ihm";
        string maWort    = f ? "Mitarbeiterin" : "Mitarbeiter";
        string pensumTxt = d.Vollzeit ? "Vollzeit-" : "Teilzeit-";

        // Possessiv-Formen für den Sehr-gut-Text (Fodor-Muster).
        string ihrer  = f ? "ihrer"  : "seiner";   // «im Rahmen ihrer Tätigkeit»
        string ihren  = f ? "ihren"  : "seinen";   // «zu ihren Hauptaufgaben»
        string ihreC  = f ? "Ihre"   : "Seine";    // «Ihre Arbeiten»
        string ihrC   = f ? "Ihr"    : "Sein";     // «Ihr Verhalten»

        // ── Aufgaben nach Bereichen (Mehrfachauswahl) ──────────────────
        bool hatKueche = d.Bereiche.Contains("kueche");
        bool hatKasse  = d.Bereiche.Contains("kasse");
        bool hatDrive  = d.Bereiche.Contains("drive");
        bool hatGast   = hatKasse || hatDrive;

        List<string> aufgaben;
        if (d.Aufgaben != null && d.Aufgaben.Count > 0)
        {
            // Explizite Auswahl aus dem 13er-Katalog der Word-Vorlage (15.07.2026).
            aufgaben = d.Aufgaben.Where(a => !string.IsNullOrWhiteSpace(a))
                                 .Select(a => a.Trim()).Distinct().ToList();
        }
        else
        {
            aufgaben = new List<string>();
            if (hatKasse && hatDrive) aufgaben.Add("Bedienen unserer Gäste an der Kasse und am Drive");
            else if (hatKasse)        aufgaben.Add("Bedienen unserer Gäste an der Kasse");
            else if (hatDrive)        aufgaben.Add("Bedienen unserer Gäste am Drive");
            if (hatKueche)            aufgaben.Add("Produzieren und Garnieren unserer Qualitätsprodukte");
            aufgaben.Add("Diverse Reinigungsarbeiten im ganzen Restaurant");
            aufgaben.Add("Gästebetreuung");
        }

        bool sehrGut = d.Qualitaet == "sehr_gut";

        // ── Beurteilungs-Absatz nach Qualitätsstufe (14.07.2026) ───────
        // Alle drei Stufen folgen ECHTEN Reinach-Vorlagen:
        //   sehr_gut     = Muster «Fodor»    (vollste Zufriedenheit, vorbildlich)
        //   gut          = Muster «Körner»   (sehr teamfähig, vollste Zufriedenheit)
        //   durchschnitt = Muster «Gherasim» (volle Zufriedenheit)
        string beurteilung = d.Qualitaet switch
        {
            "sehr_gut" =>
                $"{nameKurz} überzeugte durch eine sehr schnelle Auffassungsgabe, hohe Belastbarkeit und eine " +
                $"überdurchschnittliche Einsatzbereitschaft. {ihreC} Arbeiten erledigte {er} stets äusserst zuverlässig, " +
                $"effizient und selbstständig. Auch in stressigen Situationen behielt {er} stets den Überblick und handelte " +
                $"stets zu unserer vollsten Zufriedenheit. {ihrC} Verhalten gegenüber Vorgesetzten, Mitarbeitern sowie " +
                $"Gästen war jederzeit vorbildlich. Aufgrund {ihrer} freundlichen, hilfsbereiten und teamorientierten Art " +
                $"war {er} bei allen sehr geschätzt.",
            "genuegend" =>
                $"Wir haben {nameKurz} als teamfähige{(f ? "" : "n")} und hilfsbereite{(f ? "" : "n")} {maWort} kennen und schätzen gelernt. " +
                $"{erCap} arbeitete gewissenhaft und erledigte sämtliche Arbeiten zu unserer Zufriedenheit. " +
                $"In aussergewöhnlichen Situationen arbeitete {er} routiniert und war bereit länger zu arbeiten, sofern es erforderlich war. " +
                $"Bei Vorgesetzten, Mitarbeitern und Gästen war {er} beliebt.",
            "durchschnitt" =>
                $"Wir haben {nameKurz} als teamfähige{(f ? "" : "n")} und hilfsbereite{(f ? "" : "n")} {maWort} kennen und schätzen gelernt. " +
                $"{erCap} arbeitete stets gewissenhaft und erledigte sämtliche Arbeiten zu unserer vollen Zufriedenheit. " +
                $"In aussergewöhnlichen Situationen arbeitete {er} stets routiniert und war bereit länger zu arbeiten, sofern es erforderlich war. " +
                $"Bei Vorgesetzten, Mitarbeitern und Gästen war {er} gleichermassen beliebt.",
            _ => // gut (Default, Muster «Körner»)
                $"Wir haben {nameKurz} als sehr teamfähige{(f ? "" : "n")} und hilfsbereite{(f ? "" : "n")} {maWort} kennen und schätzen gelernt. " +
                $"{erCap} arbeitete stets gewissenhaft und erledigte sämtliche Arbeiten zu unserer vollsten Zufriedenheit. " +
                $"In aussergewöhnlichen Situationen arbeitete {er} stets routiniert und war auch bereit länger zu arbeiten, sofern es erforderlich war. " +
                $"Bei Vorgesetzten, Mitarbeitern und Gästen war {er} gleichermassen beliebt."
        };

        string geboren = d.DateOfBirth.HasValue ? $" geboren am {d.DateOfBirth:dd.MM.yyyy}," : "";
        string herkunft = string.IsNullOrWhiteSpace(d.WohnOrt) ? "" : $" aus {d.WohnOrt},";

        // Intro in zwei Teilen: Name wird im PDF fett gesetzt (wie im Muster).
        string funktion = !string.IsNullOrWhiteSpace(d.Funktion) ? d.Funktion!.Trim() : $"{pensumTxt}{maWort}";
        // Kurzform ohne Pensum-Präfix («Ihr Aufgabenbereich als Crewmitarbeiterin …»).
        string funktionKurz = funktion.Replace("Teilzeit-", "").Replace("Vollzeit-", "");
        bool zw = d.Zwischen;
        string introRest = zw
            ? $",{geboren}{herkunft} ist seit dem {d.Von:dd.MM.yyyy} " +
              $"in unserem Restaurant in {d.Ort} als {funktion} tätig."
            : $",{geboren}{herkunft} war vom {d.Von:dd.MM.yyyy} bis {d.Bis:dd.MM.yyyy} " +
              $"in unserem Restaurant in {d.Ort} als {funktion} tätig.";

        // Bereichs-Text: Muster «Fodor» (sehr gut) nutzt «sowohl … als auch»,
        // «Gherasim/Körner» (gut/durchschnitt) die kompakte Form.
        string bereichSowohl = hatKueche && hatGast
            ? "sowohl im Küchen- als auch im Gästebereich"
            : hatKueche ? "im Küchenbereich" : "im Gästebereich";
        string bereichKurz = hatKueche && hatGast
            ? "im Küchen- und Gästebereich"
            : hatKueche ? "im Küchenbereich" : "im Gästebereich";

        string aufgabenIntro = zw
            ? $"{ihrC} Aufgabenbereich als {funktionKurz} umfasst folgende Tätigkeiten:"
            : sehrGut
                ? $"Im Rahmen {ihrer} Tätigkeit wurde {nameKurz} {bereichSowohl} eingesetzt. " +
                  $"Zu {ihren} Hauptaufgaben gehörten:"
                : $"{nameKurz} konnte von uns {bereichKurz} mit folgenden Aufgaben betraut werden:";

        string schulung = zw
            ? $"Für die Verrichtung dieser Aufgaben wurde {er} von uns intern geschult. Somit kann {er} unsere " +
              "Richtlinien; Qualität, Service, Sauberkeit, Hygiene und Umweltschutz umsetzen."
            : sehrGut
            ? $"Für die Verrichtung dieser Aufgaben wurde {er} von uns intern umfassend geschult und war in der " +
              "Lage sämtliche Aufgaben gemäss unseren Richtlinien; Qualität, Service, Sauberkeit, Hygiene und " +
              "Umweltschutz kompetent umzusetzen."
            : $"Für die Verrichtung dieser Aufgaben wurde {er} von uns intern geschult. Somit konnte {er} unsere " +
              "Richtlinien; Qualität, Service, Sauberkeit, Hygiene und Umweltschutz umsetzen.";

        // ── Zwischenzeugnis-Absätze (Vorlage «289 Hendschiken») ────────
        // Abstufungen: Arbeitsmittel «sinnvoll/stets sinnvoll», «routiniert/
        // stets routiniert», Baustein-Dropdown (teamfähig / sehr teamfähig /
        // sehr zuverlässig+einsatzfreudig+belastbar), «gewissenhaft/stets»,
        // Zufriedenheit 3-stufig, «vorbildlich/sehr vorbildlich».
        bool topStufe  = d.Qualitaet is "sehr_gut";
        bool mindGut   = d.Qualitaet is "sehr_gut" or "gut";
        bool mindDsch  = d.Qualitaet is "sehr_gut" or "gut" or "durchschnitt";
        string zwArbeitsmittel =
            $"Die {ihm} zur Verfügung stehenden Arbeitsmittel setzt {er} {(mindDsch ? "stets sinnvoll" : "sinnvoll")} ein " +
            $"und die Produkte behandelt {er} jederzeit gemäss den Vorschriften. In aussergewöhnlichen Situationen " +
            $"arbeitet {er} {(mindDsch ? "stets routiniert" : "routiniert")} und ist auch bereit länger zu arbeiten, " +
            "sofern es erforderlich ist.";
        string zwBaustein = topStufe
            ? (f ? "sehr zuverlässige, jederzeit einsatzfreudige und stark belastbare Mitarbeiterin"
                 : "sehr zuverlässigen, jederzeit einsatzfreudigen und stark belastbaren Mitarbeiter")
            : mindGut
                ? $"sehr teamfähige{(f ? "" : "n")} und hilfsbereite{(f ? "" : "n")} {maWort}"
                : $"teamfähige{(f ? "" : "n")} und hilfsbereite{(f ? "" : "n")} {maWort}";
        string zwZufriedenheit = topStufe || mindGut ? "vollsten Zufriedenheit"
                               : mindDsch ? "vollen Zufriedenheit" : "Zufriedenheit";
        string zwBeurteilung =
            $"Wir haben {nameKurz} als {zwBaustein} kennen und schätzen gelernt. " +
            $"{erCap} arbeitet {(mindDsch ? "stets gewissenhaft" : "gewissenhaft")} und erledigt sämtliche Arbeiten " +
            $"zu unserer {zwZufriedenheit}. Gegenüber {ihren} Vorgesetzten, Kollegen und unseren Gästen zeigt {er} " +
            $"sich in jeder Hinsicht {(topStufe ? "sehr vorbildlich" : "vorbildlich")}.";
        string frauHerrn = f ? "Frau" : "Herrn";
        string ihreSeine = f ? "ihre" : "seine";
        string zwAbschluss =
            $"Dieses Zwischenzeugnis wird auf Wunsch von {frauHerrn} {d.LastName} ausgestellt. " +
            $"An dieser Stelle bedanken wir uns für {ihreSeine} wertvolle Mitarbeit.";

        string wunsch = d.AufEigenenWunsch ? " auf eigenen Wunsch," : "";
        string austritt =
            $"{nameKurz} verlässt unser Unternehmen{wunsch} frei von jeglicher Verpflichtung mit Ausnahme der " +
            "gesetzlichen Schweigepflicht.";

        string dank = sehrGut
            ? $"Wir möchten {ihm} für die stets hervorragende Zusammenarbeit danken und wünschen {ihm} in " +
              "privater und beruflicher Hinsicht weiterhin viel Erfolg und alles Gute für die Zukunft."
            : $"Wir möchten {ihm} für die erbrachten Arbeitsleistungen recht herzlich danken und wünschen {ihm} in " +
              "privater und beruflicher Hinsicht alles erdenklich Gute für die Zukunft.";

        // ── Arbeitsbestätigung (Vorlage «244 Sursee») ──────────────────
        // «war»-Variante, sobald das Ende VOR dem Ausstell-Datum liegt.
        bool bestPast = d.Bis.Date < d.Datum.Date;
        string bestRest = bestPast
            ? $", geboren am {d.DateOfBirth:dd.MM.yyyy}, wohnhaft in {d.WohnOrt}, vom {d.Von:dd.MM.yyyy} bis am {d.Bis:dd.MM.yyyy} " +
              $"in unserem McDonald's Restaurant in {d.Ort} als {funktion} angestellt war."
            : $", geboren am {d.DateOfBirth:dd.MM.yyyy}, wohnhaft in {d.WohnOrt}, seit dem {d.Von:dd.MM.yyyy} " +
              $"in unserem McDonald's Restaurant in {d.Ort} als {funktion} angestellt ist.";

        string datumZeile = $"{d.Ort}, {d.Datum.ToString("d. MMMM yyyy", ci)}";

        // ── Vertikale GLEICHVERTEILUNG (Walter-Vorgabe 15.07.2026, Referenz
        // «216 Oftringen Öztürk»): der Text soll die Seite gleichmaessig
        // fuellen — nicht Text oben, Loch, Unterschrift unten. Vorgehen:
        // Texthoehe wird geschaetzt (konservative Zeichen-pro-Zeile-Naeherung),
        // dann werden die Absatz-Abstaende so weit vergroessert, dass der
        // Freiraum gleichmaessig auf die Luecken verteilt ist. Passt der Text
        // selbst mit luftigem Satz nicht, sinkt die Zeilenhoehe stufenweise —
        // Einseitigkeit ist Pflicht. Schaetzfehler faengt der am Seitenende
        // verankerte Footer ab (Rest landet oberhalb des Grusses).
        float contentW = 595f - 2f * 51.0f;              // A4-Breite − 2×1.8cm, in pt
        float bannerH  = contentW * 0.066f;              // letterhead_banner (1500×99)
        float availH   = 842f - 2f * 28.35f - bannerH - 12f;   // A4 − Raender − Banner (+12pt Banner-Luft)

        static int LinesFor(string t, float w) => Math.Max(1, (int)Math.Ceiling(t.Length * 5.6f / w));

        var absaetze = new List<string>
        {
            $"{anrede} {d.FirstName} {d.LastName}{introRest}",
            aufgabenIntro, schulung
        };
        if (zw) absaetze.AddRange(new[] { zwArbeitsmittel, zwBeurteilung, zwAbschluss });
        else    absaetze.AddRange(new[] { beurteilung, austritt, dank });

        // padGruss ≈ 2 Zeilen Abstand Text → «Freundliche Grüsse» (Walter 21.07.2026).
        float lh = 1.22f, padAbs = 12f, padDatum = 20f, padTitel = 18f, padGruss = 28f;
        float bulletPad = 2f;
        float rest = 0f;
        float[] lhOpts = { 1.3f, 1.22f, 1.14f, 1.07f };
        foreach (var tryLh in lhOpts)
        {
            float lineH = baseFont * tryLh;
            float est = 5f + 116f                                   // Content-Pad + Adressblock inkl. Fenster-Abstandhalter (40pt + 6pt)
                      + padDatum + lineH                            // Ortszeile
                      + padTitel + 20f;                             // Titel
            foreach (var a in absaetze) est += padAbs + LinesFor(a, contentW) * lineH;
            est += 6f;                                              // Bullets-Einstieg
            foreach (var b in aufgaben) est += LinesFor(b, contentW - 28f) * lineH + bulletPad;
            // Gruss + Firma + grosszuegiger Unterschriftsraum (ohne Strich).
            float footerH = padGruss + 78f + 5f * lineH;
            if (est + footerH <= availH || tryLh == lhOpts[^1])
            {
                lh = tryLh;
                rest = Math.Max(0f, availH - est - footerH);
                break;
            }
        }
        // Freiraum gleichmaessig auf die Absatz-Luecken verteilen (Word-Optik).
        int gaps = absaetze.Count + 3;                              // + Datum, Titel, Gruss
        float extra = Math.Clamp(rest / gaps, 0f, 26f);
        padAbs   += extra;
        padDatum += extra;
        padTitel += extra;
        padGruss += extra;
        if (padGruss < 28f) padGruss = 28f;                         // mind. ≈ 2 Zeilen
        float padIntro = padAbs;

        // Arbeitsbestaetigung (nur 1 Satz): grosszuegige feste Abstaende.
        if (d.Bestaetigung) { padDatum = 56f; padTitel = 72f; padGruss = 48f; }
        float padSatz = d.Bestaetigung ? 60f : 22f;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.0f, Unit.Centimetre);
                page.MarginHorizontal(1.8f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(baseFont).LineHeight(lh).FontColor(Dark));

                // Briefkopf: gelbes Banner wie überall (Walter-Vorgabe).
                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(5).Column(col =>
                {
                    // ── Moderner Adressblock (Walter-Vorgabe 15.07.2026):
                    // Absenderzeile der Filiale EINZEILIG klein oben links
                    // (Fenster-Kuvert-Stil), darunter die MA-Adresse. ──
                    col.Item().Text(string.Join("  –  ", new[]
                        {
                            $"{d.CompanyName} · {d.RestaurantName}",
                            d.CompanyStreet,
                            d.CompanyZipCity
                        }.Where(x => !string.IsNullOrWhiteSpace(x))))
                        .FontSize(8f).FontColor("#6b6152");

                    // MA-Adresse im COUVERT-FENSTER (Walter 12.08.2026, gleiche
                    // Konvention wie Kündigung/Rückzug): Schweizer C5-Fenster
                    // links beginnt ~4.5 cm ab Papierkante — der fixe
                    // Abstandhalter schiebt den Adressblock in die Fensterzone.
                    col.Item().Height(40);
                    col.Item().PaddingTop(16).Column(c =>
                    {
                        c.Item().Text(anrede);
                        c.Item().Text($"{d.FirstName} {d.LastName}".Trim());
                        if (!string.IsNullOrWhiteSpace(d.EmpStreet))  c.Item().Text(d.EmpStreet);
                        if (!string.IsNullOrWhiteSpace(d.EmpZipCity)) c.Item().Text(d.EmpZipCity);
                    });

                    col.Item().PaddingTop(padDatum).Text(datumZeile);

                    col.Item().PaddingTop(padTitel).AlignCenter()
                        .Text(d.Bestaetigung ? "Arbeitsbestätigung" : zw ? "Zwischenzeugnis" : "Arbeitszeugnis")
                        .FontSize(15f).Bold();

                    if (d.Bestaetigung)
                    {
                        col.Item().PaddingTop(padSatz).PaddingHorizontal(14).Text(t =>
                        {
                            t.Justify();
                            t.DefaultTextStyle(x => x.FontSize(11.5f).LineHeight(1.5f));
                            t.Span("Wir bestätigen hiermit, dass ");
                            t.Span($"{anrede} ");
                            t.Span($"{d.FirstName} {d.LastName}".Trim()).Bold();
                            t.Span(bestRest);
                        });
                    }
                    else
                    {
                    col.Item().PaddingTop(padIntro).Text(t =>
                    {
                        t.Justify();
                        t.Span($"{anrede} ");
                        t.Span($"{d.FirstName} {d.LastName}".Trim()).Bold();
                        t.Span(introRest);
                    });

                    col.Item().PaddingTop(padAbs).Text(aufgabenIntro);

                    col.Item().PaddingTop(6).PaddingLeft(14).Column(c =>
                    {
                        foreach (var a in aufgaben)
                            c.Item().PaddingBottom(bulletPad).Row(r =>
                            {
                                r.ConstantItem(14).Text("•");
                                r.RelativeItem().Text(a);
                            });
                    });

                    col.Item().PaddingTop(padAbs).Text(schulung).Justify();
                    if (zw)
                    {
                        col.Item().PaddingTop(padAbs).Text(zwArbeitsmittel).Justify();
                        col.Item().PaddingTop(padAbs).Text(zwBeurteilung).Justify();
                        col.Item().PaddingTop(padAbs).Text(zwAbschluss).Justify();
                    }
                    else
                    {
                        col.Item().PaddingTop(padAbs).Text(beurteilung).Justify();
                        col.Item().PaddingTop(padAbs).Text(austritt).Justify();
                        col.Item().PaddingTop(padAbs).Text(dank).Justify();
                    }
                    }   // Ende Zeugnis-Absätze (nicht Bestätigung)
                });

                // ── Gruss + Firma + Unterschrift als FOOTER (Walter 15.07.2026):
                // sauber am Seitenende verankert. Kein Unterschrifts-Strich mehr
                // (Walter 21.07.2026 — «old fashion» raus); mehr Platz zum
                // Unterschreiben; ≈ 2 Zeilen vor «Freundliche Grüsse».
                page.Footer().Column(col =>
                {
                    col.Item().PaddingTop(padGruss).Text("Freundliche Grüsse");

                    col.Item().PaddingTop(8).Column(c =>
                    {
                        c.Item().Text(d.CompanyName).Bold();
                        c.Item().Text(d.RestaurantName);
                    });

                    // Unterschrift des EINGELOGGTEN Users (Konvention: nie die
                    // Unterschrift einer anderen Person — Urkundenfälschung).
                    col.Item().PaddingTop(16).Column(c =>
                    {
                        if (d.SignaturePng is { Length: > 0 })
                            c.Item().MaxHeight(52).AlignLeft().Image(d.SignaturePng).FitHeight();
                        else
                            c.Item().Height(48); // Platz für handschriftliche Unterschrift

                        c.Item().PaddingTop(6).Text(d.SignatoryName);
                        if (!string.IsNullOrWhiteSpace(d.SignatoryTitle))
                            c.Item().Text(d.SignatoryTitle);
                    });
                });
            });
        }).GeneratePdf();
    }
}
