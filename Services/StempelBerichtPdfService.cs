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

    static void Seite(PageDescriptor page, string titel, string filiale, DateOnly von, DateOnly bis)
    {
        page.Size(PageSizes.A4);
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
