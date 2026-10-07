using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Feiertage einer Filiale (Walter 07.10.2026): Geltung national / Kanton / Filiale
/// und Jahresvorschlag zum Bestätigen. Die Vorauswahl ist nur ein Vorschlag — welche
/// Tage im Kanton bzw. in der Gemeinde gelten und welche dem Sonntag gleichgestellt sind,
/// bestätigt HR (kantonale Liste). Der 1. August ist immer sonntagsgleich (Art. 110 Abs. 3 BV).
/// </summary>
public static class FeiertagVorschlag
{
    public record Kandidat(DateOnly Datum, string Bezeichnung, bool Vorgewaehlt, bool Sonntagsgleich, bool National);

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

    public static List<Kandidat> Fuer(int jahr, string? kantonCode)
    {
        var o = Ostern(jahr);
        bool kath = kantonCode != null && Katholisch.Contains(kantonCode);
        bool karfreitag = kantonCode == null || !OhneKarfreitag.Contains(kantonCode);
        return new List<Kandidat>
        {
            new(new(jahr, 1, 1), "Neujahr", true, false, false),
            new(new(jahr, 1, 2), "Berchtoldstag", false, false, false),
            new(new(jahr, 3, 19), "Josefstag", false, false, false),
            new(o.AddDays(-2), "Karfreitag", karfreitag, false, false),
            new(o.AddDays(1), "Ostermontag", true, false, false),
            new(new(jahr, 5, 1), "Tag der Arbeit", false, false, false),
            new(o.AddDays(39), "Auffahrt", true, false, false),
            new(o.AddDays(50), "Pfingstmontag", true, false, false),
            new(o.AddDays(60), "Fronleichnam", kath, false, false),
            new(new(jahr, 8, 1), "Bundesfeiertag", true, true, true),
            new(new(jahr, 8, 15), "Mariä Himmelfahrt", kath, false, false),
            new(new(jahr, 11, 1), "Allerheiligen", kath, false, false),
            new(new(jahr, 12, 8), "Mariä Empfängnis", kath, false, false),
            new(new(jahr, 12, 25), "Weihnachten", true, false, false),
            new(new(jahr, 12, 26), "Stephanstag", true, false, false),
        };
    }
}
