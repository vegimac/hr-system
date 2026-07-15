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

    public byte[] Generate(ArbeitszeugnisInput d)
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

        // Arbeitsbestaetigung (nur 1 Satz): Inhalt vertikal ausbalancieren,
        // damit der Brief nicht in der oberen Haelfte klebt (Walter 15.07.2026).
        float padDatum = d.Bestaetigung ? 56f : 24f;
        float padTitel = d.Bestaetigung ? 72f : 22f;
        float padSatz  = d.Bestaetigung ? 60f : 22f;
        float padGruss = d.Bestaetigung ? 48f : 18f;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.3f, Unit.Centimetre);
                page.MarginHorizontal(1.8f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).LineHeight(1.22f).FontColor(Dark));

                // Briefkopf: gelbes Banner wie überall (Walter-Vorgabe).
                page.Header().Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(12).Column(col =>
                {
                    // ── Adressblock: links Empfänger, rechts Filiale ──
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().PaddingTop(12).Column(c =>
                        {
                            c.Item().Text(anrede);
                            c.Item().Text($"{d.FirstName} {d.LastName}".Trim());
                            if (!string.IsNullOrWhiteSpace(d.EmpStreet))  c.Item().Text(d.EmpStreet);
                            if (!string.IsNullOrWhiteSpace(d.EmpZipCity)) c.Item().Text(d.EmpZipCity);
                        });
                        row.ConstantItem(200).Column(c =>
                        {
                            var small = TextStyle.Default.FontSize(8.5f);
                            c.Item().Text(d.CompanyName).Style(small);
                            c.Item().Text(d.RestaurantName).Style(small);
                            c.Item().Text(d.CompanyStreet).Style(small);
                            c.Item().Text(d.CompanyZipCity).Style(small);
                            if (!string.IsNullOrWhiteSpace(d.CompanyPhone)) c.Item().Text($"T {d.CompanyPhone}").Style(small);
                            if (!string.IsNullOrWhiteSpace(d.CompanyEmail)) c.Item().Text(d.CompanyEmail).Style(small);
                        });
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
                    col.Item().PaddingTop(18).Text(t =>
                    {
                        t.Justify();
                        t.Span($"{anrede} ");
                        t.Span($"{d.FirstName} {d.LastName}".Trim()).Bold();
                        t.Span(introRest);
                    });

                    col.Item().PaddingTop(12).Text(aufgabenIntro);

                    col.Item().PaddingTop(8).PaddingLeft(14).Column(c =>
                    {
                        foreach (var a in aufgaben)
                            c.Item().PaddingBottom(2).Row(r =>
                            {
                                r.ConstantItem(14).Text("•");
                                r.RelativeItem().Text(a);
                            });
                    });

                    col.Item().PaddingTop(12).Text(schulung).Justify();
                    if (zw)
                    {
                        col.Item().PaddingTop(12).Text(zwArbeitsmittel).Justify();
                        col.Item().PaddingTop(12).Text(zwBeurteilung).Justify();
                        col.Item().PaddingTop(12).Text(zwAbschluss).Justify();
                    }
                    else
                    {
                        col.Item().PaddingTop(12).Text(beurteilung).Justify();
                        col.Item().PaddingTop(12).Text(austritt).Justify();
                        col.Item().PaddingTop(12).Text(dank).Justify();
                    }
                    }   // Ende Zeugnis-Absätze (nicht Bestätigung)

                    // Rest-Freiraum hier aufgehen lassen (QuestPDF Extend):
                    // Gruss + Firma + Unterschrift sitzen am Seitenende — die
                    // Seite wirkt gefuellt, egal wie viele Aufgaben gewaehlt sind.
                    col.Item().Extend();

                    col.Item().PaddingTop(padGruss).Text("Freundliche Grüsse");

                    col.Item().PaddingTop(8).Column(c =>
                    {
                        c.Item().Text(d.CompanyName).Bold();
                        c.Item().Text(d.RestaurantName);
                    });

                    // Unterschrift des EINGELOGGTEN Users (Konvention: nie die
                    // Unterschrift einer anderen Person — Urkundenfälschung).
                    col.Item().PaddingTop(6).Column(c =>
                    {
                        if (d.SignaturePng is { Length: > 0 })
                            c.Item().MaxHeight(42).AlignLeft().Image(d.SignaturePng).FitHeight();
                        else
                            c.Item().PaddingTop(26); // Platz für handschriftliche Unterschrift

                        c.Item().PaddingTop(2).Width(180).LineHorizontal(0.8f).LineColor(Dark);
                        c.Item().Text(d.SignatoryName);
                        if (!string.IsNullOrWhiteSpace(d.SignatoryTitle))
                            c.Item().Text(d.SignatoryTitle);
                    });
                });
            });
        }).GeneratePdf();
    }
}
