namespace HrSystem.Services;

/// <summary>
/// Arbeitszeit-Verstösse aus den Stempelzeiten (Walter 07.10.2026, McAdmin-Bericht).
/// Reine Rechnung ohne DB. Grenzwerte aus <see cref="ArbeitszeitEinstellungen"/>
/// (Standard = ArG/ArGV1/L-GAV, einstellbar am Hauptsitz und pro Filiale), abgeleitet
/// aus dem easy@work-Bericht «Anomalien Stempelzeiten 2.0». Bewusste Abweichungen zu easy:
/// Pausen zählen erst ab 15 Min. (Art. 15 ArG «Viertelstunde»), Pausenpflicht erst
/// bei MEHR als 5½ Std., Stempel ohne Dauer und Tage mit 0 Min. zählen nicht.
/// </summary>
public static class ArbeitszeitVerstoesse
{
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
    public const string SonntagJugend = "SONNTAG_JUGEND";

    public static readonly string[] Reihenfolge = { Pause, Block, NachtTag, Praesenz, Woche, Ruhetage, Naechte, Jugend, SonntagJugend };

    public record Stempel(DateOnly Tag, DateTime Ein, DateTime Aus)
    {
        public int Minuten => (int)Math.Round((Aus - Ein).TotalMinutes);
    }

    public record Person(int EmployeeId, DateTime? Geburt, IReadOnlyList<Stempel> Stempel,
                         IReadOnlyCollection<DateOnly> NachtTage);

    public record Verstoss(int EmployeeId, string Art, DateOnly Von, DateOnly Bis,
                           string Text, int Ist, int Grenze, string Recht,
                           IReadOnlyList<Stempel> Stempel);

    static string H(decimal std) => Dauer((int)Math.Round(std * 60));
    static string Uhr(decimal std) => $"{(int)std}:{(int)Math.Round((std - (int)std) * 60):00}";

    public static string Titel(string art, ArbeitszeitEinstellungen? e = null)
    {
        e ??= ArbeitszeitEinstellungen.Gesetz;
        return art switch
        {
            NachtTag => $"Nachtarbeit über {H(e.Wert(NachtTag, "max_std"))}",
            Woche    => $"Über {H(e.Wert(Woche, "max_std"))} pro Woche",
            Ruhetage => $"Weniger als {e.Wert(Ruhetage, "min_tage"):0} Ruhetage",
            _        => ArbeitszeitRegelKatalog.Finde(art)?.Titel ?? art,
        };
    }

    public static string Beschreibung(string art, ArbeitszeitEinstellungen? e = null)
    {
        e ??= ArbeitszeitEinstellungen.Gesetz;
        var eigen = e.EigenerText(art);
        if (eigen != null) return eigen;
        decimal W(string k) => e.Wert(art, k);
        return art switch
        {
            Pause => $"Mehr als {H(W("ab1_std"))} → {W("pause1_min"):0} Min., mehr als {H(W("ab2_std"))} → {W("pause2_min"):0} Min., "
                   + $"mehr als {H(W("ab3_std"))} → {W("pause3_min"):0} Min. Pause (davon {W("am_stueck_min"):0} am Stück). "
                   + $"Unterbrüche unter {W("zaehlt_ab_min"):0} Min. zählen nicht.",
            Block => $"Vor und nach einer Pause höchstens {H(W("max_std"))} am Stück arbeiten.",
            NachtTag => $"Wer zwischen {Uhr(W("nacht_von"))} und {Uhr(W("nacht_bis"))} Uhr arbeitet, darf an diesem Tag höchstens {H(W("max_std"))} arbeiten.",
            Praesenz => $"Erster Stempel bis letzter Stempel inkl. Pausen: höchstens {H(W("max_std"))}, mit Nachtarbeit {H(W("max_nacht_std"))}, Jugendliche {H(W("max_jugend_std"))}.",
            Woche => $"Höchstens {H(W("max_std"))} pro Woche (Montag–Sonntag).",
            Ruhetage => $"Mindestens {W("min_tage"):0} freie Kalendertage pro Woche (Montag–Sonntag).",
            Naechte => $"Mehr als {NaechteMax} Nächte in 6 Wochen = dauernde Nachtarbeit mit Untersuchungspflicht.",
            Jugend => $"Unter 18: höchstens {H(W("max_std"))} pro Tag, nicht nach {Uhr(W("ende_ab16"))} Uhr (unter 16: {Uhr(W("ende_unter16"))} Uhr) und nicht vor {Uhr(W("beginn"))} Uhr.",
            SonntagJugend => W("sonntage") != 0
                ? "Unter 18: keine Arbeit an Sonntagen und an Feiertagen, die dem Sonntag gleichgestellt sind (Samstag 23 Uhr bis Sonntag 23 Uhr). Ausnahmen wie Lehre oder Bewilligung sind nicht geprüft."
                : "Unter 18: keine Arbeit an Feiertagen, die dem Sonntag gleichgestellt sind (Vortag 23 Uhr bis 23 Uhr). Ausnahmen wie Lehre oder Bewilligung sind nicht geprüft.",
            _ => "",
        };
    }

    /// <summary>Alle Verstösse im Zeitraum. Wochen zählen dort, wo ihr Sonntag liegt;
    /// Stempel müssen eine Woche vor/nach dem Zeitraum mitgeliefert werden, Nächte 6 Wochen davor.
    /// <paramref name="sonntagsgleich"/> = Feiertage der Filiale, die dem Sonntag gleichgestellt sind (1. August immer).</summary>
    public static List<Verstoss> Pruefe(Person p, DateOnly von, DateOnly bis,
        ArbeitszeitEinstellungen? e = null, IReadOnlyDictionary<DateOnly, string>? sonntagsgleich = null)
    {
        e ??= ArbeitszeitEinstellungen.Gesetz;
        var gueltig = p.Stempel.Where(s => s.Aus > s.Ein).OrderBy(s => s.Ein).ToList();
        var res = new List<Verstoss>();

        foreach (var tag in gueltig.Where(s => s.Tag >= von && s.Tag <= bis).GroupBy(s => s.Tag).OrderBy(g => g.Key))
            res.AddRange(PruefeTag(p, tag.Key, tag.ToList(), e));

        for (var mo = Montag(von); mo <= bis; mo = mo.AddDays(7))
        {
            var so = mo.AddDays(6);
            if (so < von || so > bis) continue;
            res.AddRange(PruefeWoche(p, mo, gueltig, e));
        }

        if (e.IstAktiv(Naechte)) res.AddRange(PruefeNaechte(p, von, bis));
        if (e.IstAktiv(SonntagJugend)) res.AddRange(PruefeSonntagJugend(p, von, bis, gueltig, e, sonntagsgleich));
        return res.OrderBy(v => v.Von).ThenBy(v => v.Art).ToList();
    }

    static IEnumerable<Verstoss> PruefeTag(Person p, DateOnly tag, List<Stempel> st, ArbeitszeitEinstellungen e)
    {
        int zaehltAb = (int)e.Wert(Pause, "zaehlt_ab_min");
        int gearbeitet = st.Sum(s => s.Minuten);
        var pausen = new List<int>();
        var bloecke = new List<(DateTime Von, DateTime Bis)>();
        var cur = (Von: st[0].Ein, Bis: st[0].Aus);
        for (int i = 1; i < st.Count; i++)
        {
            int luecke = (int)Math.Round((st[i].Ein - cur.Bis).TotalMinutes);
            if (luecke >= zaehltAb)
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
        int soll = gearbeitet > e.Minuten(Pause, "ab3_std") ? (int)e.Wert(Pause, "pause3_min")
                 : gearbeitet > e.Minuten(Pause, "ab2_std") ? (int)e.Wert(Pause, "pause2_min")
                 : gearbeitet > e.Minuten(Pause, "ab1_std") ? (int)e.Wert(Pause, "pause1_min") : 0;
        int sollAmStueck = gearbeitet > e.Minuten(Pause, "ab3_std") ? (int)e.Wert(Pause, "am_stueck_min") : 0;
        bool pauseFehlt = e.IstAktiv(Pause) && (pauseTotal < soll || pauseMax < sollAmStueck);
        if (pauseFehlt)
        {
            var text = $"{Dauer(gearbeitet)} gearbeitet — vorgeschrieben sind mindestens {soll} Min. Pause"
                     + (sollAmStueck > 0 ? $", davon {sollAmStueck} Min. am Stück" : "")
                     + $". Genommen: {(pauseTotal == 0 ? "keine" : pauseTotal + " Min.")}"
                     + (sollAmStueck > 0 && pausen.Count > 0 ? $" (längste {pauseMax} Min.)" : "") + ".";
            yield return new(p.EmployeeId, Pause, tag, tag, text, pauseTotal, soll, Recht(Pause), st);
        }
        else if (e.IstAktiv(Block))
        {
            int blockMax = e.Minuten(Block, "max_std");
            var lang = bloecke.Where(b => (b.Bis - b.Von).TotalMinutes > blockMax).ToList();
            if (lang.Count > 0)
            {
                var b = lang.OrderByDescending(x => x.Bis - x.Von).First();
                int min = (int)Math.Round((b.Bis - b.Von).TotalMinutes);
                yield return new(p.EmployeeId, Block, tag, tag,
                    $"{b.Von:HH:mm}–{b.Bis:HH:mm} ({Dauer(min)}) ohne Pause gearbeitet — am Stück sind höchstens {Dauer(blockMax)} erlaubt."
                    + (pausen.Count > 0 ? " Die Pause ist zwar lang genug, liegt aber falsch." : ""),
                    min, blockMax, Recht(Block), st);
            }
        }

        decimal nVon = e.Wert(NachtTag, "nacht_von"), nBis = e.Wert(NachtTag, "nacht_bis");
        bool nacht = st.Any(s => MinutenImFenster(s.Ein, s.Aus, nVon, nBis) > 0);
        int? alter = p.Geburt.HasValue ? MindestlohnAlter.Alter(p.Geburt.Value, tag.ToDateTime(TimeOnly.MinValue)) : null;
        bool jugend = alter is < 18;

        int nachtMax = e.Minuten(NachtTag, "max_std");
        if (e.IstAktiv(NachtTag) && nacht && gearbeitet > nachtMax)
            yield return new(p.EmployeeId, NachtTag, tag, tag,
                $"{Dauer(gearbeitet)} gearbeitet mit Nachtarbeit ({Uhr(nVon)}–{Uhr(nBis)} Uhr) — erlaubt sind höchstens {Dauer(nachtMax)}.",
                gearbeitet, nachtMax, Recht(NachtTag), st);

        int praesenz = (int)Math.Round((st.Max(s => s.Aus) - st[0].Ein).TotalMinutes);
        int praesenzMax = jugend ? e.Minuten(Praesenz, "max_jugend_std")
                        : nacht ? e.Minuten(Praesenz, "max_nacht_std") : e.Minuten(Praesenz, "max_std");
        if (e.IstAktiv(Praesenz) && praesenz > praesenzMax)
            yield return new(p.EmployeeId, Praesenz, tag, tag,
                $"Von {st[0].Ein:HH:mm} bis {st.Max(s => s.Aus):HH:mm} = {Dauer(praesenz)} inkl. Pausen — erlaubt sind "
                + (jugend ? "für Jugendliche " : nacht ? "mit Nachtarbeit " : "") + Dauer(praesenzMax) + ".",
                praesenz, praesenzMax, jugend ? "Art. 31 ArG" : nacht ? "Art. 17a ArG" : "Art. 10 ArG", st);

        if (jugend && e.IstAktiv(Jugend))
        {
            decimal ende = alter >= 16 ? e.Wert(Jugend, "ende_ab16") : e.Wert(Jugend, "ende_unter16");
            decimal beginn = e.Wert(Jugend, "beginn");
            int tagMax = e.Minuten(Jugend, "max_std");
            var teile = new List<string>();
            if (gearbeitet > tagMax) teile.Add($"{Dauer(gearbeitet)} gearbeitet (höchstens {Dauer(tagMax)})");
            var spaet = st.Where(s => MinutenImFenster(s.Ein, s.Aus, ende, beginn) > 0).ToList();
            if (spaet.Count > 0)
                teile.Add($"gearbeitet nach {Uhr(ende)} oder vor {Uhr(beginn)} Uhr (bis {spaet.Max(s => s.Aus):HH:mm})");
            if (teile.Count > 0)
                yield return new(p.EmployeeId, Jugend, tag, tag,
                    $"{alter} Jahre: " + string.Join(", ", teile) + ". Ausnahmen (Lehre, Bewilligung) sind nicht geprüft.",
                    gearbeitet, tagMax, Recht(Jugend), st);
        }
    }

    static IEnumerable<Verstoss> PruefeWoche(Person p, DateOnly mo, List<Stempel> alle, ArbeitszeitEinstellungen e)
    {
        var so = mo.AddDays(6);
        var st = alle.Where(s => s.Tag >= mo && s.Tag <= so).ToList();
        if (st.Count == 0) yield break;

        int summe = st.Sum(s => s.Minuten);
        int wocheMax = e.Minuten(Woche, "max_std");
        if (e.IstAktiv(Woche) && summe > wocheMax)
            yield return new(p.EmployeeId, Woche, mo, so,
                $"{Dauer(summe)} gearbeitet in der Woche — erlaubt sind höchstens {Dauer(wocheMax)}.",
                summe, wocheMax, Recht(Woche), st);

        if (!e.IstAktiv(Ruhetage)) yield break;
        var arbeitstage = new HashSet<DateOnly>();
        foreach (var s in alle)
            for (var t = DateOnly.FromDateTime(s.Ein); t <= DateOnly.FromDateTime(s.Aus); t = t.AddDays(1))
            {
                if (t < mo || t > so) continue;
                var tVon = t.ToDateTime(TimeOnly.MinValue);
                if (Ueberlappung(s.Ein, s.Aus, tVon, tVon.AddDays(1)) > 0) arbeitstage.Add(t);
            }
        int frei = 7 - arbeitstage.Count;
        int minFrei = (int)e.Wert(Ruhetage, "min_tage");
        if (frei < minFrei)
            yield return new(p.EmployeeId, Ruhetage, mo, so,
                $"{arbeitstage.Count} Tage gearbeitet, nur {(frei == 0 ? "kein" : frei.ToString())} freier Tag — vorgeschrieben sind {minFrei} Ruhetage pro Woche.",
                frei, minFrei, Recht(Ruhetage), st);
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
            max, NaechteMax, Recht(Naechte), Array.Empty<Stempel>());

    /// <summary>Sonntag bzw. Feiertag gilt von 23 Uhr am Vortag bis 23 Uhr (Art. 18 ArG).</summary>
    static IEnumerable<Verstoss> PruefeSonntagJugend(Person p, DateOnly von, DateOnly bis, List<Stempel> alle,
        ArbeitszeitEinstellungen e, IReadOnlyDictionary<DateOnly, string>? sonntagsgleich)
    {
        if (!p.Geburt.HasValue) yield break;
        bool sonntage = e.Wert(SonntagJugend, "sonntage") != 0;
        for (var d = von; d <= bis; d = d.AddDays(1))
        {
            string? feiertag = null;
            if (sonntagsgleich != null && sonntagsgleich.TryGetValue(d, out var name)) feiertag = name;
            else if (d.Month == 8 && d.Day == 1) feiertag = "Bundesfeiertag";
            bool sonntag = d.DayOfWeek == DayOfWeek.Sunday;
            if (feiertag == null && !(sonntage && sonntag)) continue;

            int alter = MindestlohnAlter.Alter(p.Geburt.Value, d.ToDateTime(TimeOnly.MinValue));
            if (alter >= 18) continue;
            var fVon = d.ToDateTime(TimeOnly.MinValue).AddHours(-1);
            var fBis = fVon.AddDays(1);
            var st = alle.Where(s => Ueberlappung(s.Ein, s.Aus, fVon, fBis) > 0).ToList();
            if (st.Count == 0) continue;
            int min = (int)Math.Round(st.Sum(s => Ueberlappung(s.Ein, s.Aus, fVon, fBis)));
            var wann = feiertag != null ? $"am Feiertag «{feiertag}» (dem Sonntag gleichgestellt)" : "am Sonntag";
            yield return new(p.EmployeeId, SonntagJugend, d, d,
                $"{alter} Jahre: {Dauer(min)} gearbeitet {wann} — Jugendliche dürfen dann nicht arbeiten. Ausnahmen (Lehre, Bewilligung) sind nicht geprüft.",
                min, 0, Recht(SonntagJugend), st);
        }
    }

    static string Recht(string art) => ArbeitszeitRegelKatalog.Finde(art)?.Recht ?? "";

    /// <summary>Minuten im Fenster vonStd–bisStd (über Mitternacht, wenn vonStd ≥ bisStd).</summary>
    public static int MinutenImFenster(DateTime ein, DateTime aus, decimal vonStd, decimal bisStd)
    {
        double sum = 0;
        var v = TimeSpan.FromHours((double)vonStd);
        var b = TimeSpan.FromHours((double)bisStd);
        for (var t = ein.Date.AddDays(-1); t <= aus.Date; t = t.AddDays(1))
            sum += vonStd >= bisStd
                ? Ueberlappung(ein, aus, t + v, t.AddDays(1) + b)
                : Ueberlappung(ein, aus, t + v, t + b);
        return (int)Math.Round(sum);
    }

    /// <summary>Minuten im gesetzlichen Nachtfenster 23–6 Uhr (Art. 10 ArG).</summary>
    public static int NachtMinuten(DateTime ein, DateTime aus) => MinutenImFenster(ein, aus, 23, 6);

    static double Ueberlappung(DateTime a1, DateTime a2, DateTime b1, DateTime b2)
    {
        var von = a1 > b1 ? a1 : b1;
        var bis = a2 < b2 ? a2 : b2;
        return bis > von ? (bis - von).TotalMinutes : 0;
    }

    public static DateOnly Montag(DateOnly d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    public static string Dauer(int min) => $"{min / 60} h {min % 60:00}";
}
