namespace HrSystem.Services;

/// <summary>
/// Arbeitszeit-Verstösse aus den Stempelzeiten (Walter 07.10.2026, McAdmin-Bericht).
/// Reine Rechnung ohne DB — Grundlage: ArG/ArGV1 + L-GAV, abgeleitet aus dem
/// easy@work-Bericht «Anomalien Stempelzeiten 2.0». Bewusste Abweichungen zu easy:
/// Pausen zählen erst ab 15 Min. (Art. 15 ArG «Viertelstunde»), Pausenpflicht erst
/// bei MEHR als 5½ Std., Stempel ohne Dauer und Tage mit 0 Min. zählen nicht.
/// </summary>
public static class ArbeitszeitVerstoesse
{
    public const int PauseZaehltAbMin = 15;
    public const int BlockMaxMin = 330;            // Art. 18 Abs. 1 ArGV1: 5½ Std. am Stück
    public const int NachtTagMaxMin = 540;         // Art. 17a ArG: 9 Std. bei Nachtarbeit
    public const int PraesenzMaxMin = 14 * 60;     // Art. 10 Abs. 3 ArG
    public const int PraesenzNachtMaxMin = 10 * 60;// Art. 17a ArG
    public const int PraesenzJugendMaxMin = 12 * 60;// Art. 31 Abs. 1 ArG
    public const int JugendTagMaxMin = 540;        // Art. 31 Abs. 1 ArG
    public const int WocheMaxMin = 50 * 60;        // Art. 9 Abs. 1 lit. b ArG
    public const int RuhetageMin = 2;              // L-GAV Art. 15
    public const int NaechteFensterTage = NightWorkComplianceService.WindowDays;
    public const int NaechteMax = NightWorkComplianceService.Threshold;

    public const string Pause = "PAUSE";
    public const string Block = "BLOCK";
    public const string NachtTag = "NACHT_9H";
    public const string Praesenz = "PRAESENZ";
    public const string Woche = "WOCHE_50H";
    public const string Ruhetage = "RUHETAGE";
    public const string Naechte = "NAECHTE";
    public const string Jugend = "JUGEND";

    public record Stempel(DateOnly Tag, DateTime Ein, DateTime Aus)
    {
        public int Minuten => (int)Math.Round((Aus - Ein).TotalMinutes);
    }

    public record Person(int EmployeeId, DateTime? Geburt, IReadOnlyList<Stempel> Stempel,
                         IReadOnlyCollection<DateOnly> NachtTage);

    public record Verstoss(int EmployeeId, string Art, DateOnly Von, DateOnly Bis,
                           string Text, int Ist, int Grenze, string Recht,
                           IReadOnlyList<Stempel> Stempel);

    public static readonly IReadOnlyDictionary<string, string> Titel = new Dictionary<string, string>
    {
        [Pause]    = "Pause zu kurz",
        [Block]    = "Zu lange am Stück",
        [NachtTag] = "Nachtarbeit über 9 Std.",
        [Praesenz] = "Präsenzzeit zu lang",
        [Woche]    = "Über 50 Std. pro Woche",
        [Ruhetage] = "Weniger als 2 Ruhetage",
        [Naechte]  = "Zu viele Nächte",
        [Jugend]   = "Jugendschutz",
    };

    public static readonly IReadOnlyDictionary<string, string> Regel = new Dictionary<string, string>
    {
        [Pause]    = "Mehr als 5½ Std. → 15 Min., mehr als 7 Std. → 30 Min., mehr als 9 Std. → 60 Min. Pause (davon 30 am Stück). Unterbrüche unter 15 Min. zählen nicht.",
        [Block]    = "Vor und nach einer Pause höchstens 5½ Std. am Stück arbeiten.",
        [NachtTag] = "Wer zwischen 23 und 6 Uhr arbeitet, darf an diesem Tag höchstens 9 Std. arbeiten.",
        [Praesenz] = "Erster Stempel bis letzter Stempel inkl. Pausen: höchstens 14 Std., mit Nachtarbeit 10 Std., Jugendliche 12 Std.",
        [Woche]    = "Höchstens 50 Std. pro Woche (Montag–Sonntag).",
        [Ruhetage] = "Mindestens 2 freie Kalendertage pro Woche (Montag–Sonntag).",
        [Naechte]  = "Mehr als 18 Nächte in 6 Wochen = dauernde Nachtarbeit mit Untersuchungspflicht.",
        [Jugend]   = "Unter 18: höchstens 9 Std. pro Tag, nicht nach 22 Uhr (unter 16: 20 Uhr) und nicht vor 6 Uhr.",
    };

    public static readonly string[] Reihenfolge = { Pause, Block, NachtTag, Praesenz, Woche, Ruhetage, Naechte, Jugend };

    /// <summary>Alle Verstösse im Zeitraum. Wochen zählen dort, wo ihr Sonntag liegt;
    /// Stempel müssen eine Woche vor/nach dem Zeitraum mitgeliefert werden, Nächte 6 Wochen davor.</summary>
    public static List<Verstoss> Pruefe(Person p, DateOnly von, DateOnly bis)
    {
        var gueltig = p.Stempel.Where(s => s.Aus > s.Ein).OrderBy(s => s.Ein).ToList();
        var res = new List<Verstoss>();

        foreach (var tag in gueltig.Where(s => s.Tag >= von && s.Tag <= bis).GroupBy(s => s.Tag).OrderBy(g => g.Key))
            res.AddRange(PruefeTag(p, tag.Key, tag.ToList()));

        for (var mo = Montag(von); mo <= bis; mo = mo.AddDays(7))
        {
            var so = mo.AddDays(6);
            if (so < von || so > bis) continue;
            res.AddRange(PruefeWoche(p, mo, gueltig));
        }

        res.AddRange(PruefeNaechte(p, von, bis));
        return res.OrderBy(v => v.Von).ThenBy(v => v.Art).ToList();
    }

    static IEnumerable<Verstoss> PruefeTag(Person p, DateOnly tag, List<Stempel> st)
    {
        int gearbeitet = st.Sum(s => s.Minuten);
        var pausen = new List<int>();
        var bloecke = new List<(DateTime Von, DateTime Bis)>();
        var cur = (Von: st[0].Ein, Bis: st[0].Aus);
        for (int i = 1; i < st.Count; i++)
        {
            int luecke = (int)Math.Round((st[i].Ein - cur.Bis).TotalMinutes);
            if (luecke >= PauseZaehltAbMin)
            {
                pausen.Add(luecke);
                bloecke.Add(cur);
                cur = (st[i].Ein, st[i].Aus);
            }
            else if (st[i].Aus > cur.Bis) cur.Bis = st[i].Aus;
        }
        bloecke.Add(cur);

        int pauseTotal = pausen.Sum();
        int pauseMax = pausen.Count == 0 ? 0 : pausen.Max();
        int soll = gearbeitet > 540 ? 60 : gearbeitet > 420 ? 30 : gearbeitet > 330 ? 15 : 0;
        int sollAmStueck = gearbeitet > 540 ? 30 : 0;
        bool pauseFehlt = pauseTotal < soll || pauseMax < sollAmStueck;
        if (pauseFehlt)
        {
            var text = $"{Dauer(gearbeitet)} gearbeitet — vorgeschrieben sind mindestens {soll} Min. Pause"
                     + (sollAmStueck > 0 ? $", davon {sollAmStueck} Min. am Stück" : "")
                     + $". Genommen: {(pauseTotal == 0 ? "keine" : pauseTotal + " Min.")}"
                     + (sollAmStueck > 0 && pausen.Count > 0 ? $" (längste {pauseMax} Min.)" : "") + ".";
            yield return new(p.EmployeeId, Pause, tag, tag, text, pauseTotal, soll, "Art. 15 ArG", st);
        }
        else
        {
            var lang = bloecke.Where(b => (b.Bis - b.Von).TotalMinutes > BlockMaxMin).ToList();
            if (lang.Count > 0)
            {
                var b = lang.OrderByDescending(x => x.Bis - x.Von).First();
                int min = (int)Math.Round((b.Bis - b.Von).TotalMinutes);
                yield return new(p.EmployeeId, Block, tag, tag,
                    $"{b.Von:HH:mm}–{b.Bis:HH:mm} ({Dauer(min)}) ohne Pause gearbeitet — am Stück sind höchstens 5 h 30 erlaubt. Die Pause ist zwar lang genug, liegt aber falsch.",
                    min, BlockMaxMin, "Art. 18 ArGV1", st);
            }
        }

        bool nacht = st.Any(s => NachtMinuten(s.Ein, s.Aus) > 0);
        int? alter = p.Geburt.HasValue ? MindestlohnAlter.Alter(p.Geburt.Value, tag.ToDateTime(TimeOnly.MinValue)) : null;
        bool jugend = alter is < 18;

        if (nacht && gearbeitet > NachtTagMaxMin)
            yield return new(p.EmployeeId, NachtTag, tag, tag,
                $"{Dauer(gearbeitet)} gearbeitet mit Nachtarbeit (23–6 Uhr) — erlaubt sind höchstens 9 Std.",
                gearbeitet, NachtTagMaxMin, "Art. 17a ArG", st);

        int praesenz = (int)Math.Round((st.Max(s => s.Aus) - st[0].Ein).TotalMinutes);
        int praesenzMax = jugend ? PraesenzJugendMaxMin : nacht ? PraesenzNachtMaxMin : PraesenzMaxMin;
        if (praesenz > praesenzMax)
            yield return new(p.EmployeeId, Praesenz, tag, tag,
                $"Von {st[0].Ein:HH:mm} bis {st.Max(s => s.Aus):HH:mm} = {Dauer(praesenz)} inkl. Pausen — erlaubt sind "
                + (jugend ? "für Jugendliche 12 Std." : nacht ? "mit Nachtarbeit 10 Std." : "14 Std."),
                praesenz, praesenzMax, jugend ? "Art. 31 ArG" : nacht ? "Art. 17a ArG" : "Art. 10 ArG", st);

        if (jugend)
        {
            int grenzeStunde = alter >= 16 ? 22 : 20;
            var teile = new List<string>();
            if (gearbeitet > JugendTagMaxMin) teile.Add($"{Dauer(gearbeitet)} gearbeitet (höchstens 9 Std.)");
            var spaet = st.Where(s => MinutenAusserhalb(s.Ein, s.Aus, grenzeStunde) > 0).ToList();
            if (spaet.Count > 0)
                teile.Add($"gearbeitet nach {grenzeStunde}:00 oder vor 6:00 (bis {spaet.Max(s => s.Aus):HH:mm})");
            if (teile.Count > 0)
                yield return new(p.EmployeeId, Jugend, tag, tag,
                    $"{alter} Jahre: " + string.Join(", ", teile) + ". Ausnahmen (Lehre, Bewilligung) sind nicht geprüft.",
                    gearbeitet, JugendTagMaxMin, "Art. 31 ArG", st);
        }
    }

    static IEnumerable<Verstoss> PruefeWoche(Person p, DateOnly mo, List<Stempel> alle)
    {
        var so = mo.AddDays(6);
        var st = alle.Where(s => s.Tag >= mo && s.Tag <= so).ToList();
        if (st.Count == 0) yield break;

        int summe = st.Sum(s => s.Minuten);
        if (summe > WocheMaxMin)
            yield return new(p.EmployeeId, Woche, mo, so,
                $"{Dauer(summe)} gearbeitet in der Woche — erlaubt sind höchstens 50 Std.",
                summe, WocheMaxMin, "Art. 9 ArG", st);

        var beginn = mo.ToDateTime(TimeOnly.MinValue);
        var arbeitstage = new HashSet<DateOnly>();
        foreach (var s in alle)
            for (var t = DateOnly.FromDateTime(s.Ein); t <= DateOnly.FromDateTime(s.Aus); t = t.AddDays(1))
            {
                if (t < mo || t > so) continue;
                var tVon = t.ToDateTime(TimeOnly.MinValue);
                if (Ueberlappung(s.Ein, s.Aus, tVon, tVon.AddDays(1)) > 0) arbeitstage.Add(t);
            }
        int frei = 7 - arbeitstage.Count;
        if (frei < RuhetageMin)
            yield return new(p.EmployeeId, Ruhetage, mo, so,
                $"{arbeitstage.Count} Tage gearbeitet, nur {(frei == 0 ? "kein" : frei.ToString())} freier Tag — vorgeschrieben sind 2 Ruhetage pro Woche.",
                frei, RuhetageMin, "L-GAV Art. 15", st);
    }

    static IEnumerable<Verstoss> PruefeNaechte(Person p, DateOnly von, DateOnly bis)
    {
        var naechte = p.NachtTage.Distinct().OrderBy(d => d).ToList();
        DateOnly? start = null, ende = null;
        int max = 0;
        foreach (var n in naechte.Where(d => d >= von && d <= bis))
        {
            int anzahl = naechte.Count(d => d > n.AddDays(-NaechteFensterTage) && d <= n);
            if (anzahl > NaechteMax)
            {
                start ??= n;
                ende = n;
                max = Math.Max(max, anzahl);
            }
            else if (start != null)
            {
                yield return NaechteVerstoss(p, start.Value, ende!.Value, max);
                start = null; max = 0;
            }
        }
        if (start != null) yield return NaechteVerstoss(p, start.Value, ende!.Value, max);
    }

    static Verstoss NaechteVerstoss(Person p, DateOnly von, DateOnly bis, int max) =>
        new(p.EmployeeId, Naechte, von, bis,
            $"Bis zu {max} Nächte in 6 Wochen — ab mehr als {NaechteMax} Nächten braucht es die ärztliche Untersuchung und das Zeugnis.",
            max, NaechteMax, "Art. 30 ArGV1", Array.Empty<Stempel>());

    /// <summary>Minuten im Nachtfenster 23–6 Uhr (Art. 10 ArG).</summary>
    public static int NachtMinuten(DateTime ein, DateTime aus)
    {
        double sum = 0;
        for (var t = ein.Date.AddDays(-1); t <= aus.Date; t = t.AddDays(1))
            sum += Ueberlappung(ein, aus, t.AddHours(23), t.AddDays(1).AddHours(6));
        return (int)Math.Round(sum);
    }

    static int MinutenAusserhalb(DateTime ein, DateTime aus, int grenzeStunde)
    {
        double sum = 0;
        for (var t = ein.Date.AddDays(-1); t <= aus.Date; t = t.AddDays(1))
            sum += Ueberlappung(ein, aus, t.AddHours(grenzeStunde), t.AddDays(1).AddHours(6));
        return (int)Math.Round(sum);
    }

    static double Ueberlappung(DateTime a1, DateTime a2, DateTime b1, DateTime b2)
    {
        var von = a1 > b1 ? a1 : b1;
        var bis = a2 < b2 ? a2 : b2;
        return bis > von ? (bis - von).TotalMinutes : 0;
    }

    public static DateOnly Montag(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public static string Dauer(int min) => $"{min / 60} h {min % 60:00}";
}
