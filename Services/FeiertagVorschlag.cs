using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Feiertage einer Filiale (Walter 07.10.2026): Geltung national / Kanton / Filiale
/// und Jahresvorschlag zum Bestätigen. Der 1. August ist immer sonntagsgleich (Art. 110 Abs. 3 BV,
/// Art. 20a Abs. 1 ArG); dazu darf jeder Kanton höchstens acht weitere Feiertage dem Sonntag
/// gleichstellen. Für AG, LU und BE ist die Liste hinterlegt (<see cref="Kantonslisten"/>),
/// für andere Kantone bleibt es ein Vorschlag, den HR anhand der kantonalen Liste bestätigt.
/// </summary>
public static class FeiertagVorschlag
{
    public record Kandidat(DateOnly Datum, string Bezeichnung, bool Vorgewaehlt, bool Sonntagsgleich, bool National);

    public enum Einstufung { Unbekannt, Kein, Feiertag, Sonntag }

    record Kantonsliste(string[] Sonntag, string[] WeitereFeiertage, string Quelle, string? Hinweis);

    /// <summary>
    /// Art. 20a ArG pro Kanton. AG gilt nur für die Bezirke Aarau, Brugg, Kulm, Lenzburg und Zofingen —
    /// die anderen Bezirke haben eigene Listen (EG ArR § 6 Abs. 1). LU: Ostermontag, Pfingstmontag und
    /// Mariä Empfängnis sind Feiertage, aber nicht dem Sonntag gleichgestellt.
    /// </summary>
    static readonly Dictionary<string, Kantonsliste> Kantonslisten = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AG"] = new(
            new[] { "Neujahr", "Berchtoldstag", "Karfreitag", "Ostermontag", "Auffahrt", "Pfingstmontag", "Weihnachten", "Stephanstag" },
            Array.Empty<string>(),
            "EG ArR § 6 Abs. 1 lit. a (SAR 961.200)",
            "Liste der Bezirke Aarau, Brugg, Kulm, Lenzburg und Zofingen. Die übrigen Aargauer Bezirke haben eine eigene Liste."),
        ["LU"] = new(
            new[] { "Neujahr", "Karfreitag", "Auffahrt", "Fronleichnam", "Mariä Himmelfahrt", "Allerheiligen", "Weihnachten", "Stephanstag" },
            new[] { "Ostermontag", "Pfingstmontag", "Mariä Empfängnis" },
            "Merkblatt Feiertage wira Luzern (Art. 20a ArG)",
            "Ostermontag, Pfingstmontag und Mariä Empfängnis sind in Luzern Feiertage, aber nicht dem Sonntag gleichgestellt."),
        ["BE"] = new(
            new[] { "Neujahr", "Berchtoldstag", "Karfreitag", "Ostermontag", "Auffahrt", "Pfingstmontag", "Weihnachten", "Stephanstag" },
            Array.Empty<string>(),
            "Gesetz über die Ruhe an öffentlichen Feiertagen Art. 2 (BSG 555.1)",
            null),
    };

    static readonly HashSet<string> Katholisch = new(StringComparer.OrdinalIgnoreCase)
        { "LU", "UR", "SZ", "OW", "NW", "ZG", "AI", "TI", "VS" };
    static readonly HashSet<string> OhneKarfreitag = new(StringComparer.OrdinalIgnoreCase) { "VS", "TI" };

    public static bool GiltFuer(DienstplanFeiertag f, int companyProfileId, string? kantonCode) => f.Scope switch
    {
        "NATIONAL" => true,
        "KANTON"   => !string.IsNullOrEmpty(f.KantonCode)
                      && string.Equals(f.KantonCode, kantonCode, StringComparison.OrdinalIgnoreCase),
        "FILIALE"  => f.CompanyProfileId == companyProfileId,
        _ => false,
    };

    public static bool IstSonntagsgleich(DienstplanFeiertag f) => f.Sonntagsgleich || (f.Datum.Month == 8 && f.Datum.Day == 1);

    public static bool KantonslisteVorhanden(string? kantonCode) =>
        kantonCode != null && Kantonslisten.ContainsKey(kantonCode.Trim());

    /// <summary>Wie der Kanton einen Tag führt; Unbekannt = keine Liste hinterlegt bzw. kein üblicher Feiertag.</summary>
    public static Einstufung Einstufen(string? kantonCode, DateOnly datum)
    {
        if (datum.Month == 8 && datum.Day == 1) return Einstufung.Sonntag;
        if (kantonCode == null || !Kantonslisten.TryGetValue(kantonCode.Trim(), out var liste)) return Einstufung.Unbekannt;
        var name = Tage(datum.Year).FirstOrDefault(t => t.Datum == datum).Name;
        if (name == null) return Einstufung.Unbekannt;
        if (liste.Sonntag.Contains(name)) return Einstufung.Sonntag;
        return liste.WeitereFeiertage.Contains(name) ? Einstufung.Feiertag : Einstufung.Kein;
    }

    public static string? Quelle(string? kantonCode) =>
        kantonCode != null && Kantonslisten.TryGetValue(kantonCode.Trim(), out var l) ? l.Quelle : null;

    public static string Hinweis(string? kantonCode)
    {
        if (kantonCode == null) return "Für diese Filiale ist kein Kanton erfasst. Nur der 1. August ist sicher dem Sonntag gleichgestellt.";
        if (!Kantonslisten.TryGetValue(kantonCode.Trim(), out var l))
            return $"Für den Kanton {kantonCode} ist keine Liste hinterlegt. Bitte prüfen, welche Tage frei und welche dem Sonntag gleichgestellt sind.";
        return $"Dem Sonntag gleichgestellt nach Art. 20a ArG: 1. August und acht kantonale Feiertage ({l.Quelle}).{(l.Hinweis != null ? " " + l.Hinweis : "")}";
    }

    /// <summary>Ostersonntag (gregorianisch, Meeus/Jones/Butcher).</summary>
    public static DateOnly Ostern(int jahr)
    {
        int a = jahr % 19, b = jahr / 100, c = jahr % 100, d = b / 4, e = b % 4;
        int f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int monat = (h + l - 7 * m + 114) / 31, tag = (h + l - 7 * m + 114) % 31 + 1;
        return new DateOnly(jahr, monat, tag);
    }

    static List<(DateOnly Datum, string Name)> Tage(int jahr)
    {
        var o = Ostern(jahr);
        return new()
        {
            (new(jahr, 1, 1), "Neujahr"),
            (new(jahr, 1, 2), "Berchtoldstag"),
            (new(jahr, 3, 19), "Josefstag"),
            (o.AddDays(-2), "Karfreitag"),
            (o.AddDays(1), "Ostermontag"),
            (new(jahr, 5, 1), "Tag der Arbeit"),
            (o.AddDays(39), "Auffahrt"),
            (o.AddDays(50), "Pfingstmontag"),
            (o.AddDays(60), "Fronleichnam"),
            (new(jahr, 8, 1), "Bundesfeiertag"),
            (new(jahr, 8, 15), "Mariä Himmelfahrt"),
            (new(jahr, 11, 1), "Allerheiligen"),
            (new(jahr, 12, 8), "Mariä Empfängnis"),
            (new(jahr, 12, 25), "Weihnachten"),
            (new(jahr, 12, 26), "Stephanstag"),
        };
    }

    public static List<Kandidat> Fuer(int jahr, string? kantonCode)
    {
        bool kath = kantonCode != null && Katholisch.Contains(kantonCode);
        bool karfreitag = kantonCode == null || !OhneKarfreitag.Contains(kantonCode);
        var ohneListe = new HashSet<string> { "Neujahr", "Ostermontag", "Auffahrt", "Pfingstmontag", "Weihnachten", "Stephanstag" };
        var katholischeTage = new HashSet<string> { "Fronleichnam", "Mariä Himmelfahrt", "Allerheiligen", "Mariä Empfängnis" };
        return Tage(jahr).Select(t =>
        {
            if (t.Name == "Bundesfeiertag") return new Kandidat(t.Datum, t.Name, true, true, true);
            var e = Einstufen(kantonCode, t.Datum);
            if (e != Einstufung.Unbekannt)
                return new Kandidat(t.Datum, t.Name, e != Einstufung.Kein, e == Einstufung.Sonntag, false);
            bool vor = ohneListe.Contains(t.Name)
                       || (t.Name == "Karfreitag" && karfreitag)
                       || (katholischeTage.Contains(t.Name) && kath);
            return new Kandidat(t.Datum, t.Name, vor, false, false);
        }).ToList();
    }
}
