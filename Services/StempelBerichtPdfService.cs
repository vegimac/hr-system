using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

// ── Daten der Stempel-Berichte (Controller, Bildschirm und PDF teilen sie) ──
public record StempelZeit(DateOnly Tag, string Ein, string Aus, int EinMin, int AusMin, int Minuten);
public record StempelVerstossZeile(string Art, string Titel, DateOnly Von, DateOnly Bis, string Text,
                                   int Ist, int Grenze, string Recht, List<StempelZeit> Stempel);
public record StempelVerstossMa(int EmployeeId, string? Nummer, string? Vorname, string? Nachname,
                                List<StempelVerstossZeile> Verstoesse);
public record StempelVerstossArt(string Art, string Titel, string Regel, int Anzahl);
public record StempelVerstoesseDaten(string Filiale, DateOnly Von, DateOnly Bis, int AnzahlMa,
                                     List<StempelVerstossArt> ProArt, List<StempelVerstossMa> Mitarbeiter,
                                     List<string> Ausgeschaltet);

public record StempelKorrekturZeile(int Id, DateOnly Tag, string Ein, string? Aus, string? VorherEin, string? VorherAus,
                                    string Art, string? Von, DateTime? Am, string? Kommentar, string? Protokoll);
public record StempelKorrekturMa(int EmployeeId, string? Nummer, string? Vorname, string? Nachname,
                                 List<StempelKorrekturZeile> Zeilen);
public record StempelAnzahl(string Name, int Anzahl);
public record StempelKorrekturenDaten(string Filiale, DateOnly Von, DateOnly Bis, int StempelTotal, int Korrigiert,
                                      List<StempelAnzahl> ProBearbeiter, List<StempelKorrekturMa> Mitarbeiter,
                                      Dictionary<string, int>? StempelProMonat = null);

public record StempelFilialMonat(string Monat, int Stempel, int Korrigiert, int Verstoesse);
public record StempelFilialZeile(int Id, string Filiale, int Stempel, int Korrigiert, int MaMitKorrektur,
    Dictionary<string, int> KorrekturProArt, int AnzahlMa, int Verstoesse, int MaMitVerstoss,
    Dictionary<string, int> VerstossProArt, List<string> VerstossAus, List<StempelFilialMonat> ProMonat);
public record StempelFilialArt(string Art, string Titel, string Regel);
public record StempelFilialvergleichDaten(DateOnly Von, DateOnly Bis, List<StempelFilialArt> VerstossArten,
    List<StempelFilialZeile> Filialen, List<string> OhneStempel);

/// <summary>PDFs der McAdmin-Stempelberichte (Walter 07.10.2026) — A4 hoch, Karten pro MA.</summary>
public class StempelBerichtPdfService
{
    const string Ink = "#1a1a1a", Body = "#3f3f3f", Muted = "#8b8b8b", Line = "#e2ddd3",
                 Soft = "#f6f3ee", Band = "#eeece4";

    public static readonly IReadOnlyDictionary<string, string> Farbe = new Dictionary<string, string>
    {
        [ArbeitszeitVerstoesse.Pause]    = "#d97706",
        [ArbeitszeitVerstoesse.Block]    = "#ea580c",
        [ArbeitszeitVerstoesse.NachtTag] = "#4f46e5",
        [ArbeitszeitVerstoesse.Praesenz] = "#0e7490",
        [ArbeitszeitVerstoesse.Woche]    = "#be123c",
        [ArbeitszeitVerstoesse.Ruhetage] = "#7c3aed",
        [ArbeitszeitVerstoesse.Naechte]  = "#1e3a8a",
        [ArbeitszeitVerstoesse.Jugend]   = "#db2777",
        [ArbeitszeitVerstoesse.SonntagJugend] = "#9d174d",
        [ArbeitszeitVerstoesse.Ruhezeit] = "#0f766e",
        [ArbeitszeitVerstoesse.GanzerRuhetag] = "#6d28d9",
        [ArbeitszeitVerstoesse.SiebenTage] = "#b91c1c",
        ["ZEIT"]       = "#b45309",
        ["MANUELL"]    = "#4f46e5",
        ["BEARBEITET"] = "#6b7280",
        ["KOMMENTAR"]  = "#0e7490",
    };

    public static readonly IReadOnlyDictionary<string, string> KorrekturArt = new Dictionary<string, string>
    {
        ["ZEIT"] = "Zeit korrigiert", ["MANUELL"] = "Von Hand erfasst",
        ["BEARBEITET"] = "Bearbeitet", ["KOMMENTAR"] = "Nur Kommentar",
    };

    static readonly string[] Wt = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };
    static string Tag(DateOnly d) => $"{Wt[(int)d.DayOfWeek]} {d:dd.MM.yyyy}";
    static string Name(string? v, string? n) => $"{v} {n}".Trim();

    public byte[] Verstoesse(StempelVerstoesseDaten d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        int total = d.ProArt.Sum(a => a.Anzahl);
        return Document.Create(doc => doc.Page(page =>
        {
            Seite(page, "Arbeitszeit-Verstösse", d.Filiale, d.Von, d.Bis);
            page.Content().Column(col =>
            {
                col.Spacing(8);
                col.Item().Text(t =>
                {
                    t.Span($"{total} Verstösse").Bold().FontSize(11f).FontColor(Ink);
                    t.Span($" bei {d.Mitarbeiter.Count} von {d.AnzahlMa} Mitarbeitenden").FontSize(9f);
                });
                col.Item().Table(tb =>
                {
                    tb.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                    foreach (var a in d.ProArt)
                        tb.Cell().Padding(2).Background(Soft).Border(0.5f).BorderColor(Line).Padding(5).Row(r =>
                        {
                            r.ConstantItem(4).Background(Farbe[a.Art]);
                            r.ConstantItem(6);
                            r.RelativeItem().Column(c2 =>
                            {
                                c2.Item().Text(a.Anzahl.ToString()).Bold().FontSize(13f)
                                    .FontColor(a.Anzahl > 0 ? Farbe[a.Art] : Muted);
                                c2.Item().Text(a.Titel).FontSize(7.5f).FontColor(Body);
                            });
                        });
                });

                if (d.Mitarbeiter.Count == 0)
                    col.Item().PaddingTop(20).AlignCenter().Text("Keine Verstösse im Zeitraum.").FontSize(11f).FontColor(Muted);

                foreach (var m in d.Mitarbeiter)
                {
                    col.Item().EnsureSpace(90).Column(k =>
                    {
                        MaKopf(k, Name(m.Vorname, m.Nachname), m.Nummer, $"{m.Verstoesse.Count} Verstoss" + (m.Verstoesse.Count == 1 ? "" : "e"));
                        foreach (var v in m.Verstoesse)
                            k.Item().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).Row(r =>
                            {
                                r.ConstantItem(3).Background(Farbe[v.Art]);
                                r.ConstantItem(7);
                                r.ConstantItem(92).Column(c2 =>
                                {
                                    c2.Item().Text(v.Von == v.Bis ? Tag(v.Von) : $"{v.Von:dd.MM.} – {v.Bis:dd.MM.yyyy}")
                                        .Bold().FontSize(8f).FontColor(Ink);
                                    c2.Item().Text(v.Titel).FontSize(7.5f).FontColor(Farbe[v.Art]).SemiBold();
                                });
                                r.RelativeItem().Column(c2 =>
                                {
                                    c2.Item().Text(v.Text).FontSize(8f);
                                    if (v.Stempel.Count > 0)
                                        c2.Item().PaddingTop(1).Text(StempelText(v)).FontSize(7f).FontColor(Muted);
                                });
                                r.ConstantItem(70).AlignRight().Text(v.Recht).FontSize(6.5f).FontColor(Muted);
                            });
                    });
                }

                col.Item().PaddingTop(10).Text("Regeln").Bold().FontSize(9f).FontColor(Ink);
                foreach (var a in d.ProArt)
                    col.Item().Row(r =>
                    {
                        r.ConstantItem(3).Background(Farbe[a.Art]);
                        r.ConstantItem(6);
                        r.ConstantItem(110).Text(a.Titel).SemiBold().FontSize(7.5f);
                        r.RelativeItem().Text(a.Regel).FontSize(7.5f).FontColor(Body);
                    });
                if (d.Ausgeschaltet.Count > 0)
                    col.Item().PaddingTop(4).Text("Für diese Filiale ausgeschaltet: " + string.Join(", ", d.Ausgeschaltet) + ".")
                        .FontSize(7f).FontColor(Body);
                col.Item().PaddingTop(4).Text("Grundlage: Stempelzeiten aus easy@work, Regeln und Feiertage der Filiale. Wochen zählen zum Zeitraum, in dem ihr Sonntag liegt. " +
                    "Stempel ohne Dauer werden nicht gezählt.").FontSize(6.5f).FontColor(Muted);
            });
        })).GeneratePdf();
    }

    static string StempelText(StempelVerstossZeile v)
    {
        if (v.Von == v.Bis) return string.Join("  ·  ", v.Stempel.Select(s => $"{s.Ein}–{s.Aus}"));
        if (v.Art == ArbeitszeitVerstoesse.Ruhezeit)
            return string.Join("  ·  ", v.Stempel.Select(s => $"{Wt[(int)s.Tag.DayOfWeek]} {s.Ein}–{s.Aus}"));
        return string.Join("  ·  ", v.Stempel.GroupBy(s => s.Tag).Select(g =>
            $"{Wt[(int)g.Key.DayOfWeek]} {ArbeitszeitVerstoesse.Dauer(g.Sum(s => s.Minuten))}"));
    }

    public byte[] Korrekturen(StempelKorrekturenDaten d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var alle = d.Mitarbeiter.SelectMany(m => m.Zeilen).ToList();
        return Document.Create(doc => doc.Page(page =>
        {
            Seite(page, "Korrekturen Stempelzeiten", d.Filiale, d.Von, d.Bis);
            page.Content().Column(col =>
            {
                col.Spacing(8);
                col.Item().Text(t =>
                {
                    t.Span($"{d.Korrigiert} von {d.StempelTotal} Stempeln").Bold().FontSize(11f).FontColor(Ink);
                    var anteil = d.StempelTotal == 0 ? 0 : 100m * d.Korrigiert / d.StempelTotal;
                    t.Span($" korrigiert oder kommentiert ({anteil:0.0} %) · {d.Mitarbeiter.Count} Mitarbeitende").FontSize(9f);
                });
                col.Item().Row(r =>
                {
                    foreach (var a in KorrekturArt)
                    {
                        int n = alle.Count(z => z.Art == a.Key);
                        r.RelativeItem().Padding(2).Background(Soft).Border(0.5f).BorderColor(Line).Padding(5).Row(rr =>
                        {
                            rr.ConstantItem(4).Background(Farbe[a.Key]);
                            rr.ConstantItem(6);
                            rr.RelativeItem().Column(c2 =>
                            {
                                c2.Item().Text(n.ToString()).Bold().FontSize(13f).FontColor(n > 0 ? Farbe[a.Key] : Muted);
                                c2.Item().Text(a.Value).FontSize(7.5f);
                            });
                        });
                    }
                });
                if (d.ProBearbeiter.Count > 0)
                    col.Item().Text(t =>
                    {
                        t.Span("Korrigiert durch: ").SemiBold().FontSize(8f);
                        t.Span(string.Join("  ·  ", d.ProBearbeiter.Select(b => $"{b.Name} {b.Anzahl}"))).FontSize(8f);
                    });

                if (d.Mitarbeiter.Count == 0)
                    col.Item().PaddingTop(20).AlignCenter().Text("Keine Korrekturen im Zeitraum.").FontSize(11f).FontColor(Muted);

                foreach (var m in d.Mitarbeiter)
                {
                    col.Item().EnsureSpace(90).Column(k =>
                    {
                        MaKopf(k, Name(m.Vorname, m.Nachname), m.Nummer, $"{m.Zeilen.Count} Stempel");
                        foreach (var z in m.Zeilen)
                            k.Item().BorderBottom(0.5f).BorderColor(Line).PaddingVertical(4).Row(r =>
                            {
                                r.ConstantItem(3).Background(Farbe[z.Art]);
                                r.ConstantItem(7);
                                r.ConstantItem(92).Column(c2 =>
                                {
                                    c2.Item().Text(Tag(z.Tag)).Bold().FontSize(8f).FontColor(Ink);
                                    c2.Item().Text(KorrekturArt[z.Art]).FontSize(7.5f).SemiBold().FontColor(Farbe[z.Art]);
                                });
                                r.ConstantItem(92).Column(c2 =>
                                {
                                    c2.Item().Text($"{z.Ein} – {z.Aus ?? "offen"}").Bold().FontSize(9f).FontColor(Ink);
                                    if (z.VorherEin != null || z.VorherAus != null)
                                        c2.Item().Text(t =>
                                        {
                                            t.DefaultTextStyle(s => s.FontSize(7f).FontColor(Muted));
                                            t.Span("vorher ");
                                            t.Span($"{z.VorherEin ?? z.Ein} – {z.VorherAus ?? z.Aus ?? "offen"}").Strikethrough();
                                        });
                                });
                                r.RelativeItem().Column(c2 =>
                                {
                                    if (z.Kommentar != null) c2.Item().Text($"«{z.Kommentar}»").Italic().FontSize(8f);
                                    if (z.Protokoll != null) c2.Item().Text(z.Protokoll).FontSize(7f).FontColor(Muted);
                                });
                                r.ConstantItem(90).AlignRight().Column(c2 =>
                                {
                                    if (z.Von != null) c2.Item().AlignRight().Text(z.Von).FontSize(7.5f);
                                    if (z.Am != null) c2.Item().AlignRight().Text($"{z.Am:dd.MM.yyyy HH:mm}").FontSize(7f).FontColor(Muted);
                                });
                            });
                    });
                }
            });
        })).GeneratePdf();
    }

    static readonly string[] Linie = { "#0ea5e9", "#f59e0b", "#10b981", "#8b5cf6", "#ef4444", "#06b6d4", "#f97316", "#84cc16", "#ec4899", "#6366f1" };
    static readonly string[] Monat = { "Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez" };
    static double Pro100(int n, int stempel) => stempel == 0 ? 0 : n * 100.0 / stempel;
    static string Zahl(double x) => x.ToString("0.0");
    static string MonatKurz(string jjjjMm) => $"{Monat[int.Parse(jjjjMm[5..7]) - 1]} {jjjjMm[2..4]}";

    /// <summary>HR-Hub «Stempelzeiten alle Filialen» (Walter 08.10.2026) — A4 quer, gleiche Zahlen wie der Bildschirm.</summary>
    public byte[] Filialvergleich(StempelFilialvergleichDaten d)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        int stempel = d.Filialen.Sum(f => f.Stempel), korr = d.Filialen.Sum(f => f.Korrigiert), verst = d.Filialen.Sum(f => f.Verstoesse);
        double schnittK = Pro100(korr, stempel), schnittV = Pro100(verst, stempel);
        var artTitel = d.VerstossArten.ToDictionary(a => a.Art, a => a.Titel);

        return Document.Create(doc => doc.Page(page =>
        {
            Seite(page, "Stempelzeiten alle Filialen", "Filialvergleich", d.Von, d.Bis, quer: true);
            page.Content().Column(col =>
            {
                col.Spacing(10);
                if (d.Filialen.Count == 0)
                {
                    col.Item().PaddingTop(20).AlignCenter().Text("Keine Stempel im Zeitraum.").FontSize(11f).FontColor(Muted);
                    return;
                }

                col.Item().Row(r =>
                {
                    Kennzahl(r.RelativeItem(), Zahl(schnittK), $"Korrekturen pro 100 Stempel · {korr} von {stempel}");
                    Kennzahl(r.RelativeItem(), Zahl(schnittV), $"Verstösse pro 100 Stempel · {verst} total");
                    Kennzahl(r.RelativeItem(), d.Filialen.Count.ToString(), "Filialen mit Stempeln");
                });

                var top = d.Filialen.Where(f => f.Korrigiert > 0)
                    .Select(f => (f.Filiale, Pro100: Pro100(f.Korrigiert, f.Stempel), f.Korrigiert, f.Stempel))
                    .OrderByDescending(x => x.Pro100).ThenByDescending(x => x.Korrigiert)
                    .ThenBy(x => x.Filiale, StringComparer.OrdinalIgnoreCase).Take(3).ToList();
                if (top.Count > 0)
                    col.Item().Element(Karte).Column(k =>
                    {
                        k.Spacing(3);
                        KartenTitel(k, "Meiste Korrekturen · Rang 1–3", "pro 100 Stempel");
                        k.Item().Row(r =>
                        {
                            r.Spacing(8);
                            for (int i = 0; i < top.Count; i++)
                            {
                                var t = top[i];
                                r.RelativeItem().Border(0.5f).BorderColor(Line).Background(i == 0 ? "#f5efe4" : Soft)
                                    .Padding(6).AlignCenter().Column(c =>
                                    {
                                        c.Item().Text($"{i + 1}.").Bold().FontSize(11f).FontColor(i == 0 ? "#b45309" : Ink);
                                        c.Item().Text(t.Filiale).SemiBold().FontSize(9f);
                                        c.Item().Text(Zahl(t.Pro100)).Bold().FontSize(14f).FontColor(Ink);
                                        c.Item().Text($"{t.Korrigiert} / {t.Stempel}").FontSize(7f).FontColor(Muted);
                                    });
                            }
                        });
                    });

                col.Item().Row(r =>
                {
                    r.Spacing(12);
                    BalkenKarte(r.RelativeItem(), "Korrekturen pro 100 Stempel", schnittK, d.Filialen,
                        f => KorrekturArt.Keys.Select(a => (a, f.KorrekturProArt.GetValueOrDefault(a))).ToList(),
                        a => KorrekturArt[a], f => $"{f.Korrigiert} / {f.Stempel}");
                    BalkenKarte(r.RelativeItem(), "Verstösse pro 100 Stempel", schnittV, d.Filialen,
                        f => d.VerstossArten.Select(a => (a.Art, f.VerstossProArt.GetValueOrDefault(a.Art))).ToList(),
                        a => artTitel.GetValueOrDefault(a, a), f => $"{f.Verstoesse} bei {f.MaMitVerstoss} MA");
                });

                col.Item().PageBreak();
                col.Item().Element(Karte).Column(k =>
                {
                    k.Spacing(4);
                    KartenTitel(k, "Übersicht aller Filialen", "Farbe = Häufung innerhalb der Spalte · «aus» = Regel in der Filiale ausgeschaltet");
                    k.Item().Element(c => Uebersicht(c, d));
                });

                var svg = VerlaufSvg(d);
                if (svg != null)
                    col.Item().ShowEntire().Element(Karte).Column(k =>
                    {
                        k.Spacing(4);
                        KartenTitel(k, "Verlauf: Korrekturen pro 100 Stempel", "pro Monat · Monat ohne Stempel = Lücke");
                        k.Item().Inlined(i =>
                        {
                            i.Spacing(8);
                            for (int fi = 0; fi < d.Filialen.Count; fi++)
                                i.Item().Text(t =>
                                {
                                    t.Span("■ ").FontColor(Linie[fi % Linie.Length]);
                                    t.Span(d.Filialen[fi].Filiale).FontSize(7.5f);
                                });
                        });
                        k.Item().Svg(svg).FitWidth();
                    });

                col.Item().Element(c => Rechenweg(c, d));
            });
        })).GeneratePdf();
    }

    static IContainer Karte(IContainer c) =>
        c.Background(Soft).Border(0.5f).BorderColor(Line).Padding(8);

    static void KartenTitel(ColumnDescriptor k, string titel, string zusatz) =>
        k.Item().Text(t =>
        {
            t.Span(titel).Bold().FontSize(10f).FontColor(Ink);
            t.Span("   " + zusatz).FontSize(7f).FontColor(Muted);
        });

    static void Kennzahl(IContainer c, string zahl, string text) =>
        c.Padding(2).Background(Soft).Border(0.5f).BorderColor(Line).Padding(6).Column(k =>
        {
            k.Item().Text(zahl).Bold().FontSize(16f).FontColor(Ink);
            k.Item().Text(text).FontSize(7.5f);
        });

    static void BalkenKarte(IContainer c, string titel, double schnitt, List<StempelFilialZeile> filialen,
        Func<StempelFilialZeile, List<(string Art, int N)>> teileVon, Func<string, string> artName,
        Func<StempelFilialZeile, string> roh)
    {
        var zeilen = filialen.Select(f =>
        {
            var teile = teileVon(f).Where(t => t.N > 0).Select(t => (t.Art, W: Pro100(t.N, f.Stempel))).ToList();
            return (F: f, Teile: teile, Wert: teile.Sum(t => t.W));
        }).OrderByDescending(z => z.Wert).ToList();
        double max = Math.Max(1, Math.Max(schnitt, zeilen.Max(z => z.Wert))) * 1.08;
        var arten = zeilen.SelectMany(z => z.Teile.Select(t => t.Art)).Distinct().ToList();

        c.Element(Karte).Column(k =>
        {
            k.Spacing(4);
            KartenTitel(k, titel, $"Ø alle Filialen {Zahl(schnitt)} · senkrechte Linie");
            if (arten.Count > 0)
                k.Item().Inlined(i =>
                {
                    i.Spacing(6);
                    foreach (var a in arten)
                        i.Item().Text(t =>
                        {
                            t.Span("■ ").FontColor(Farbe.GetValueOrDefault(a, "#6b7280"));
                            t.Span(artName(a)).FontSize(6.5f);
                        });
                });
            foreach (var z in zeilen)
                k.Item().PaddingVertical(1.5f).Row(r =>
                {
                    r.ConstantItem(78).AlignMiddle().Text(z.F.Filiale).SemiBold().FontSize(8f).FontColor(Body);
                    r.RelativeItem().AlignMiddle().Height(10).Layers(l =>
                    {
                        l.PrimaryLayer().Background("#e9e5dc").Row(b =>
                        {
                            double rest = max;
                            foreach (var t in z.Teile)
                            {
                                b.RelativeItem((float)t.W).Height(10).Background(Farbe.GetValueOrDefault(t.Art, "#6b7280"));
                                rest -= t.W;
                            }
                            if (rest > 0.0001) b.RelativeItem((float)rest);
                        });
                        l.Layer().Row(b =>
                        {
                            if (schnitt > 0.0001) b.RelativeItem((float)schnitt);
                            b.ConstantItem(1.2f).Height(10).Background(Ink);
                            if (max - schnitt > 0.0001) b.RelativeItem((float)(max - schnitt));
                        });
                    });
                    r.ConstantItem(30).AlignMiddle().AlignRight().Text(Zahl(z.Wert)).Bold().FontSize(9f).FontColor(Ink);
                    r.ConstantItem(70).AlignMiddle().PaddingLeft(6).Text(roh(z.F)).FontSize(7f).FontColor(Muted);
                });
        });
    }

    static string? VerlaufSvg(StempelFilialvergleichDaten d)
    {
        var monate = d.Filialen[0].ProMonat.Select(m => m.Monat).ToList();
        if (monate.Count < 2) return null;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.#", ci);
        const double W = 760, H = 125, L = 34, R = 12, T = 8, B = 20;
        var punkte = d.Filialen.Select(f => f.ProMonat.Select(m => m.Stempel > 0 ? Pro100(m.Korrigiert, m.Stempel) : (double?)null).ToList()).ToList();
        double max = Math.Max(1, punkte.SelectMany(p => p).Where(v => v != null).Select(v => v!.Value).DefaultIfEmpty(0).Max()) * 1.1;
        double X(int i) => L + (W - L - R) * i / (monate.Count - 1);
        double Y(double v) => T + (H - T - B) * (1 - v / max);

        var sb = new System.Text.StringBuilder();
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 {F(W)} {F(H)}' width='{F(W)}' height='{F(H)}' font-family='Arial'>");
        for (int s = 0; s <= 4; s++)
        {
            double v = max * s / 4;
            sb.Append($"<line x1='{F(L)}' x2='{F(W - R)}' y1='{F(Y(v))}' y2='{F(Y(v))}' stroke='#e2ddd3' stroke-width='0.6'/>");
            sb.Append($"<text x='{F(L - 4)}' y='{F(Y(v) + 3)}' text-anchor='end' font-size='7' fill='#8b8b8b'>{Zahl(v)}</text>");
        }
        for (int i = 0; i < monate.Count; i++)
            sb.Append($"<text x='{F(X(i))}' y='{F(H - 6)}' text-anchor='middle' font-size='7' fill='#8b8b8b'>{MonatKurz(monate[i])}</text>");
        for (int fi = 0; fi < punkte.Count; fi++)
        {
            var farbe = Linie[fi % Linie.Length];
            var pfad = new System.Text.StringBuilder();
            bool offen = false;
            for (int i = 0; i < punkte[fi].Count; i++)
            {
                var v = punkte[fi][i];
                if (v == null) { offen = false; continue; }
                pfad.Append($"{(offen ? "L" : "M")}{F(X(i))},{F(Y(v.Value))} ");
                offen = true;
                sb.Append($"<circle cx='{F(X(i))}' cy='{F(Y(v.Value))}' r='2' fill='{farbe}'/>");
            }
            if (pfad.Length > 0) sb.Append($"<path d='{pfad}' fill='none' stroke='{farbe}' stroke-width='1.4'/>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    static string Mischen(string hex, double anteil)
    {
        int Kanal(int i) => Convert.ToInt32(hex.Substring(1 + 2 * i, 2), 16);
        string K(int i) => ((int)Math.Round(255 + (Kanal(i) - 255) * anteil)).ToString("x2");
        return $"#{K(0)}{K(1)}{K(2)}";
    }

    static void Uebersicht(IContainer c, StempelFilialvergleichDaten d)
    {
        var arten = d.VerstossArten.Where(a => d.Filialen.Any(f => !f.VerstossAus.Contains(a.Art))).ToList();
        var maxArt = arten.ToDictionary(a => a.Art, a => Math.Max(1, d.Filialen.Max(f => f.VerstossProArt.GetValueOrDefault(a.Art))));
        int Summe(Func<StempelFilialZeile, int> sel) => d.Filialen.Sum(sel);

        c.Table(tb =>
        {
            tb.ColumnsDefinition(cd =>
            {
                cd.RelativeColumn();
                for (int i = 0; i < 7; i++) cd.ConstantColumn(46);
                foreach (var _ in arten) cd.ConstantColumn(22);
            });

            IContainer Kopf(IContainer x) => x.BorderBottom(0.8f).BorderColor(Line).PaddingHorizontal(3).PaddingVertical(3).AlignBottom();
            tb.Header(h =>
            {
                h.Cell().Element(Kopf).Text("Filiale").Bold().FontSize(7f);
                foreach (var t in new[] { "Stempel", "Korrek-\nturen", "pro 100", "MA mit\nKorr.", "Verstösse", "pro 100", "MA mit\nVerst." })
                    h.Cell().Element(Kopf).AlignRight().Text(t).Bold().FontSize(7f);
                foreach (var a in arten)
                    h.Cell().Element(Kopf).AlignCenter().RotateLeft().Text(a.Titel).Bold().FontSize(6.5f);
            });

            IContainer Zelle(IContainer x) => x.BorderBottom(0.4f).BorderColor(Line).PaddingHorizontal(3).PaddingVertical(3).AlignMiddle();
            void Wert(string s, bool fett = false)
            {
                var t = tb.Cell().Element(Zelle).AlignRight().Text(s).FontSize(8f);
                if (fett) t.Bold();
            }

            foreach (var f in d.Filialen)
            {
                tb.Cell().Element(Zelle).Text(f.Filiale).SemiBold().FontSize(8f).FontColor(Ink);
                Wert(f.Stempel.ToString()); Wert(f.Korrigiert.ToString()); Wert(Zahl(Pro100(f.Korrigiert, f.Stempel)), true);
                Wert(f.MaMitKorrektur.ToString()); Wert(f.Verstoesse.ToString());
                Wert(Zahl(Pro100(f.Verstoesse, f.Stempel)), true); Wert($"{f.MaMitVerstoss} / {f.AnzahlMa}");
                foreach (var a in arten)
                {
                    if (f.VerstossAus.Contains(a.Art))
                    {
                        tb.Cell().Element(Zelle).AlignCenter().Text("aus").FontSize(6.5f).FontColor("#b8b2a7");
                        continue;
                    }
                    int n = f.VerstossProArt.GetValueOrDefault(a.Art);
                    var farbe = Farbe.GetValueOrDefault(a.Art, "#6b7280");
                    var zelle = tb.Cell().Element(Zelle);
                    if (n > 0) zelle = zelle.Background(Mischen(farbe, 0.15 + 0.6 * n / maxArt[a.Art]));
                    zelle.AlignCenter().Text(n > 0 ? n.ToString() : "·").SemiBold().FontSize(7.5f);
                }
            }

            IContainer Fuss(IContainer x) => x.BorderTop(1.2f).BorderColor(Line).PaddingHorizontal(3).PaddingVertical(3).AlignMiddle();
            tb.Cell().Element(Fuss).Text("Total").Bold().FontSize(8f).FontColor(Ink);
            int stT = Summe(x => x.Stempel), kT = Summe(x => x.Korrigiert), vT = Summe(x => x.Verstoesse);
            foreach (var s in new[] { stT.ToString(), kT.ToString(), Zahl(Pro100(kT, stT)), Summe(x => x.MaMitKorrektur).ToString(),
                                      vT.ToString(), Zahl(Pro100(vT, stT)), $"{Summe(x => x.MaMitVerstoss)} / {Summe(x => x.AnzahlMa)}" })
                tb.Cell().Element(Fuss).AlignRight().Text(s).Bold().FontSize(8f);
            foreach (var a in arten)
                tb.Cell().Element(Fuss).AlignCenter().Text(d.Filialen.Sum(f => f.VerstossProArt.GetValueOrDefault(a.Art)).ToString()).Bold().FontSize(7.5f);
        });
    }

    static void Rechenweg(IContainer c, StempelFilialvergleichDaten d) => c.Column(k =>
    {
        k.Spacing(2);
        k.Item().Text("Wie wird gerechnet?").Bold().FontSize(9f).FontColor(Ink);
        k.Item().Text("Pro Filiale exakt dieselbe Rechnung wie in den Einzelberichten «Korrekturen Stempelzeiten» und «Arbeitszeit-Verstösse». " +
            "«Pro 100 Stempel» macht grosse und kleine Filialen vergleichbar: Korrekturen bzw. Verstösse ÷ Stempel × 100. " +
            "Ein Verstoss zählt im Monat, in dem er endet (Wochenregeln: Sonntag der Woche).").FontSize(7.5f);
        if (d.OhneStempel.Count > 0)
            k.Item().Text("Ohne Stempel im Zeitraum: " + string.Join(", ", d.OhneStempel) + ".").FontSize(7.5f);
        k.Item().PaddingTop(4).Text("Regeln").Bold().FontSize(8f).FontColor(Ink);
        foreach (var a in d.VerstossArten)
            k.Item().Row(r =>
            {
                r.ConstantItem(3).Background(Farbe.GetValueOrDefault(a.Art, "#6b7280"));
                r.ConstantItem(6);
                r.ConstantItem(130).Text(a.Titel).SemiBold().FontSize(7f);
                r.RelativeItem().Text(a.Regel).FontSize(7f).FontColor(Body);
            });
    });

    static void Seite(PageDescriptor page, string titel, string filiale, DateOnly von, DateOnly bis, bool quer = false)
    {
        page.Size(quer ? PageSizes.A4.Landscape() : PageSizes.A4);
        page.Margin(1.3f, Unit.Centimetre);
        page.DefaultTextStyle(t => t.FontFamily("Arial").FontSize(8f).FontColor(Body));
        page.Header().PaddingBottom(8).Column(c =>
        {
            c.Item().Row(r =>
            {
                r.RelativeItem().Text(titel).Bold().FontSize(16f).FontColor(Ink);
                r.AutoItem().AlignBottom().Text(filiale).FontSize(10f);
            });
            c.Item().Text($"{von:dd.MM.yyyy} – {bis:dd.MM.yyyy}").FontSize(9f).FontColor(Muted);
            c.Item().PaddingTop(4).LineHorizontal(1f).LineColor(Ink);
        });
        page.Footer().Row(r =>
        {
            r.RelativeItem().Text($"OneCrew · erstellt {DateTime.Now:dd.MM.yyyy HH:mm}").FontSize(6.5f).FontColor(Muted);
            r.AutoItem().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(6.5f).FontColor(Muted));
                t.Span("Seite "); t.CurrentPageNumber(); t.Span(" / "); t.TotalPages();
            });
        });
    }

    static void MaKopf(ColumnDescriptor k, string name, string? nummer, string rechts)
    {
        k.Item().PaddingTop(6).Background(Band).PaddingVertical(4).PaddingHorizontal(6).Row(r =>
        {
            r.RelativeItem().Text(t =>
            {
                t.Span(name).Bold().FontSize(9.5f).FontColor(Ink);
                if (!string.IsNullOrWhiteSpace(nummer)) t.Span($"   {nummer}").FontSize(7.5f).FontColor(Muted);
            });
            r.AutoItem().Text(rechts).FontSize(7.5f).FontColor(Body);
        });
    }
}
