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
        string? UnterzeichnerName, string? UnterzeichnerTitel, byte[]? SignaturePng);

    // ── Vereinbarungs-Optionen (Ergebnis des Gesprächs) ─────────────────────
    public record MvOptionen(
        DateOnly GespraechsDatum,
        int VerlBezahlt,                // 0 = keine
        int VerlUnbezahlt,              // 0 = keine
        string Rueckkehr,               // GLEICH | ANDERS | KEINE
        decimal? PensumProzent,         // nur bei ANDERS
        string? RueckkehrRestaurant,    // nur bei ANDERS
        bool Eingeschrieben);           // sonst persönliche Aushändigung

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
