namespace HrSystem.Services;

/// <summary>
/// Arbeitsmuster aus den Stempelzeiten (Walter 15.10.2026, McAdmin → «Arbeitsmuster»):
/// Wochenraster Mo–So × Stunde für die Dienstplanung. Pro Feld die durchschnittliche
/// Besetzung (gestempelte Personenstunden ÷ Anzahl dieses Wochentags im Zeitraum) und
/// wer dort wie oft arbeitet (Anteil der Wochentage, an denen der MA in dieser Stunde
/// mindestens <see cref="MinMinutenProStunde"/> Minuten gestempelt hat).
/// Rein und seiteneffektfrei — Stempel kommen als Parameter (Lokalzeit Zürich).
/// </summary>
public static class ArbeitsMuster
{
    public const int MinMinutenProStunde = 15;

    public sealed record Stempel(int EmployeeId, DateTime Kommen, DateTime Gehen);

    public sealed record MaAnteil(int EmployeeId, decimal Anteil);

    public sealed record Zelle(decimal Besetzung, List<MaAnteil> Mitarbeiter);

    public sealed record MaZusammenfassung(int EmployeeId, decimal Stunden, decimal StundenProWoche,
        decimal[] StundenProWochentag, string Hauptzeiten);

    public sealed record Ergebnis(int[] WochentagAnzahl, Zelle[][] Raster, List<MaZusammenfassung> Mitarbeiter);

    /// <summary>Wochentag-Index Mo=0 … So=6.</summary>
    public static int Index(DayOfWeek d) => ((int)d + 6) % 7;

    public static Ergebnis Rechne(IEnumerable<Stempel> stempel, DateOnly von, DateOnly bis)
    {
        var anzahl = new int[7];
        for (var d = von; d <= bis; d = d.AddDays(1)) anzahl[Index(d.DayOfWeek)]++;

        var vonDt = von.ToDateTime(TimeOnly.MinValue);
        var bisDt = bis.AddDays(1).ToDateTime(TimeOnly.MinValue);

        // Minuten pro (MA, Kalendertag, Stunde) — Schichten über Mitternacht landen am richtigen Tag.
        var minuten = new Dictionary<(int Ma, DateOnly Tag, int Std), double>();
        foreach (var s in stempel)
        {
            var a = s.Kommen < vonDt ? vonDt : s.Kommen;
            var e = s.Gehen > bisDt ? bisDt : s.Gehen;
            while (a < e)
            {
                var stundeEnde = new DateTime(a.Year, a.Month, a.Day, a.Hour, 0, 0).AddHours(1);
                var teilEnde = stundeEnde < e ? stundeEnde : e;
                var key = (s.EmployeeId, DateOnly.FromDateTime(a), a.Hour);
                minuten[key] = minuten.GetValueOrDefault(key) + (teilEnde - a).TotalMinutes;
                a = teilEnde;
            }
        }

        var personenMinuten = new double[7, 24];
        var tageProMa = new Dictionary<(int Ma, int Wt, int Std), int>();
        var maStunden = new Dictionary<int, double[]>();
        foreach (var ((ma, tag, std), min) in minuten)
        {
            var wt = Index(tag.DayOfWeek);
            personenMinuten[wt, std] += min;
            if (min >= MinMinutenProStunde)
                tageProMa[(ma, wt, std)] = tageProMa.GetValueOrDefault((ma, wt, std)) + 1;
            if (!maStunden.TryGetValue(ma, out var proWt)) maStunden[ma] = proWt = new double[7];
            proWt[wt] += min / 60.0;
        }

        var raster = new Zelle[7][];
        for (var wt = 0; wt < 7; wt++)
        {
            raster[wt] = new Zelle[24];
            for (var std = 0; std < 24; std++)
            {
                var n = anzahl[wt];
                var besetzung = n == 0 ? 0m : Math.Round((decimal)(personenMinuten[wt, std] / 60.0 / n), 1);
                var mas = n == 0 ? new List<MaAnteil>() : tageProMa
                    .Where(kv => kv.Key.Wt == wt && kv.Key.Std == std)
                    .Select(kv => new MaAnteil(kv.Key.Ma, Math.Round((decimal)kv.Value / n, 2)))
                    .OrderByDescending(m => m.Anteil).ThenBy(m => m.EmployeeId)
                    .ToList();
                raster[wt][std] = new Zelle(besetzung, mas);
            }
        }

        var wochen = Math.Max(1m, (bis.DayNumber - von.DayNumber + 1) / 7m);
        var zusammenfassung = maStunden.Select(kv =>
        {
            var total = kv.Value.Sum();
            return new MaZusammenfassung(kv.Key,
                Math.Round((decimal)total, 1),
                Math.Round((decimal)total / wochen, 1),
                kv.Value.Select(h => Math.Round((decimal)h, 1)).ToArray(),
                Hauptzeiten(kv.Key, tageProMa, anzahl));
        }).ToList();

        return new Ergebnis(anzahl, raster, zusammenfassung);
    }

    static readonly string[] Kurz = { "Mo", "Di", "Mi", "Do", "Fr", "Sa", "So" };

    /// <summary>
    /// Typische Zeiten eines MA in Worten, z.B. «Fr, Sa 17–23 · Mo 11–14»: Wochentage, an denen
    /// er in mindestens der Hälfte der Wochen gearbeitet hat, mit dem Stundenbereich, den er dort
    /// in mindestens der Hälfte dieser Tage abdeckt. Gleiche Bereiche werden zusammengefasst.
    /// </summary>
    static string Hauptzeiten(int ma, Dictionary<(int Ma, int Wt, int Std), int> tageProMa, int[] anzahl)
    {
        var bereiche = new List<(int Wt, int Von, int Bis)>();
        for (var wt = 0; wt < 7; wt++)
        {
            if (anzahl[wt] == 0) continue;
            var stunden = Enumerable.Range(0, 24)
                .Where(std => tageProMa.GetValueOrDefault((ma, wt, std)) * 2 >= anzahl[wt])
                .ToList();
            if (stunden.Count == 0) continue;
            bereiche.Add((wt, stunden.Min(), stunden.Max() + 1));
        }
        return string.Join(" · ", bereiche
            .GroupBy(b => (b.Von, b.Bis))
            .OrderBy(g => g.Min(b => b.Wt))
            .Select(g => $"{string.Join(", ", g.Select(b => Kurz[b.Wt]))} {g.Key.Von}–{g.Key.Bis}"));
    }
}
