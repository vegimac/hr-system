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
    public const string Ruhezeit = "RUHEZEIT";
    public const string GanzerRuhetag = "GANZER_RUHETAG";
    public const string SiebenTage = "SIEBEN_TAGE";
    public const string Naechte = "NAECHTE";
    public const string Jugend = "JUGEND";
    public const string SonntagJugend = "SONNTAG_JUGEND";

    public static readonly string[] Reihenfolge = { Pause, Block, NachtTag, Praesenz, Woche, Ruhezeit, GanzerRuhetag, Ruhetage, SiebenTage, Naechte, Jugend, SonntagJugend };

    static readonly string[] Wt = { "So", "Mo", "Di", "Mi", "Do", "Fr", "Sa" };
    static string Zeitpunkt(DateTime t) => $"{Wt[(int)t.DayOfWeek]} {t:dd.MM. HH:mm}";

    public record Stempel(DateOnly Tag, DateTime Ein, DateTime Aus)
    {
        public int Minuten => (int)Math.Round((Aus - Ein).TotalMinutes);
    }

    public record Person(int EmployeeId, DateTime? Geburt, IReadOnlyList<Stempel> Stempel,
                         IReadOnlyCollection<DateOnly> NachtTage, NachtNachweis? Nachweis = null);

    /// <summary>Unterlagen Nachtarbeit wie im Dashboard: Arztzeugnis als Dokument + Ausnahmeregelung.
    /// Gültig ab = Ausstellung, bis = gerechnetes Ende (leer = ohne Ende).</summary>
    public record NachtNachweis(bool Arztzeugnis, DateOnly? Ab, DateOnly? Bis, bool Ausnahmeregelung)
    {
        public bool GiltAm(DateOnly tag) =>
            Arztzeugnis && Ausnahmeregelung && (Ab == null || Ab <= tag) && (Bis == null || Bis >= tag);

        public string Fehlt(DateOnly von, DateOnly bis)
        {
            if (!Arztzeugnis) return Ausnahmeregelung ? "Kein Arztzeugnis hinterlegt." : "Kein Arztzeugnis und keine Ausnahmeregelung hinterlegt.";
            var teile = new List<string>();
            if (Ab != null && Ab > von) teile.Add($"Arztzeugnis erst ab {Ab:dd.MM.yyyy} gültig");
            if (Bis != null && Bis < bis) teile.Add($"Arztzeugnis nur bis {Bis:dd.MM.yyyy} gültig");
            if (!Ausnahmeregelung) teile.Add("Ausnahmeregelung fehlt");
            return teile.Count == 0 ? "" : string.Join(", ", teile) + ".";
        }
    }

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
            Ruhetage => $"Mindestens {W("min_tage"):0} Ruhetage pro Woche (Montag–Sonntag). Ein Tag ist frei, wenn an ihm keine Schicht beginnt. "
                      + $"Halber Ruhetag: höchstens {H(W("halbtag_max_std"))} Arbeit und frei bis {Uhr(W("halbtag_bis"))} Uhr oder ab {Uhr(W("halbtag_ab"))} Uhr.",
            Ruhezeit => $"Zwischen Schluss und nächstem Arbeitsbeginn mindestens {H(W("min_std"))} frei, einmal pro Woche {H(W("verkuerzt_std"))}; Jugendliche {H(W("jugend_std"))}.",
            GanzerRuhetag => $"Pro Woche mindestens einmal {H(W("min_std"))} am Stück frei (tägliche Ruhezeit + 24 Std. ganzer Ruhetag)."
                      + (W("ausnahme_24") != 0 ? " Mit zwei Ruhetagen oder Feiertagen in der Woche genügen einmal 24 Std." : ""),
            SiebenTage => $"Höchstens {W("max_tage"):0} Tage in Folge. Ein Tag mehr nur, wenn an jedem Tag höchstens {H(W("max_std_tag"))} gearbeitet wird und danach {H(W("frei_std"))} am Stück frei sind.",
            Naechte => $"Mehr als {NaechteMax} Nächte in 6 Wochen = dauernde Nachtarbeit mit Untersuchungspflicht. Nicht gemeldet, solange ein gültiges Arztzeugnis und die Ausnahmeregelung hinterlegt sind.",
            Jugend => $"Unter 18: höchstens {H(W("max_std"))} pro Tag, nicht nach {Uhr(W("ende_ab16"))} Uhr (unter 16: {Uhr(W("ende_unter16"))} Uhr) und nicht vor {Uhr(W("beginn"))} Uhr.",
            SonntagJugend => W("sonntage") != 0
                ? "Unter 18: keine Arbeit an Sonntagen und an Feiertagen, die dem Sonntag gleichgestellt sind (Samstag 23 Uhr bis Sonntag 23 Uhr). Ausnahmen wie Lehre oder Bewilligung sind nicht geprüft."
                : "Unter 18: keine Arbeit an Feiertagen, die dem Sonntag gleichgestellt sind (Vortag 23 Uhr bis 23 Uhr). Ausnahmen wie Lehre oder Bewilligung sind nicht geprüft.",
            _ => "",
        };
    }

    /// <summary>Alle Verstösse im Zeitraum. Wochen zählen dort, wo ihr Sonntag liegt;
    /// Stempel müssen zwei Wochen vor und eine Woche nach dem Zeitraum mitgeliefert werden, Nächte 6 Wochen davor.
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
            if (e.IstAktiv(GanzerRuhetag)) res.AddRange(PruefeGanzerRuhetag(p, mo, gueltig, e, sonntagsgleich));
        }

        if (e.IstAktiv(Ruhezeit)) res.AddRange(PruefeRuhezeit(p, von, bis, gueltig, e));
        if (e.IstAktiv(SiebenTage)) res.AddRange(PruefeSiebenTage(p, von, bis, gueltig, e));

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
        int frei = 0, halb = 0;
        for (int i = 0; i < 7; i++)
        {
            var d = mo.AddDays(i);
            var tag = st.Where(s => s.Tag == d).ToList();
            if (tag.Count == 0) frei++;
            else if (IstHalberRuhetag(d, tag, e)) halb++;
        }
        decimal ruhe = frei + halb * 0.5m;
        decimal minFrei = e.Wert(Ruhetage, "min_tage");
        if (ruhe < minFrei)
        {
            var freiText = frei == 0 ? "kein freier Tag" : frei == 1 ? "nur 1 freier Tag" : $"nur {frei} freie Tage";
            if (halb > 0) freiText += halb == 1 ? " und 1 halber" : $" und {halb} halbe";
            yield return new(p.EmployeeId, Ruhetage, mo, so,
                $"{7 - frei} Tage gearbeitet, {freiText} — vorgeschrieben sind {minFrei:0} Ruhetage pro Woche.",
                (int)Math.Floor(ruhe), (int)minFrei, Recht(Ruhetage), st);
        }
    }

    /// <summary>L-GAV Art. 16 Abs. 2: frei bis 12 Uhr oder ab 14.30 Uhr, höchstens 5 Std. Arbeit.</summary>
    static bool IstHalberRuhetag(DateOnly d, List<Stempel> tag, ArbeitszeitEinstellungen e)
    {
        if (tag.Sum(s => s.Minuten) > e.Minuten(Ruhetage, "halbtag_max_std")) return false;
        var basis = d.ToDateTime(TimeOnly.MinValue);
        var freiBis = basis.AddMinutes(e.Minuten(Ruhetage, "halbtag_bis"));
        var freiAb = basis.AddMinutes(e.Minuten(Ruhetage, "halbtag_ab"));
        return tag.All(s => s.Ein >= freiBis) || tag.All(s => s.Aus <= freiAb);
    }

    record Luecke(DateTime Von, DateTime Bis, bool Offen)
    {
        public int Minuten => (int)Math.Round((Bis - Von).TotalMinutes);
    }

    /// <summary>Freie Zeiten zwischen den Stempeln; vor dem ersten und nach dem letzten Stempel offen.</summary>
    static List<Luecke> Luecken(List<Stempel> alle)
    {
        var res = new List<Luecke>();
        if (alle.Count == 0) return res;
        res.Add(new Luecke(alle[0].Ein.AddDays(-30), alle[0].Ein, true));
        var ende = alle[0].Aus;
        foreach (var s in alle.Skip(1))
        {
            if (s.Ein > ende) res.Add(new Luecke(ende, s.Ein, false));
            if (s.Aus > ende) ende = s.Aus;
        }
        res.Add(new Luecke(ende, ende.AddDays(30), true));
        return res;
    }

    /// <summary>Gehört die freie Zeit als ganzer Ruhetag zur Woche? Massgebend ist die Mitte der
    /// 24 Std., die nach der täglichen Ruhezeit (Mindestdauer − 24 Std.) frühestens/spätestens liegen können.</summary>
    static bool ZaehltFuerWoche(Luecke l, int mindestMin, DateTime wVon, DateTime wBis)
    {
        if (l.Minuten < mindestMin) return false;
        var mitteFrueh = l.Von.AddMinutes(mindestMin - 12 * 60);
        var mitteSpaet = l.Bis.AddMinutes(-12 * 60);
        return mitteFrueh < wBis && mitteSpaet >= wVon;
    }

    static IEnumerable<Verstoss> PruefeGanzerRuhetag(Person p, DateOnly mo, List<Stempel> alle,
        ArbeitszeitEinstellungen e, IReadOnlyDictionary<DateOnly, string>? sonntagsgleich)
    {
        var so = mo.AddDays(6);
        var st = alle.Where(s => s.Tag >= mo && s.Tag <= so).ToList();
        if (st.Count == 0) yield break;
        var wVon = mo.ToDateTime(TimeOnly.MinValue);
        var wBis = wVon.AddDays(7);
        var luecken = Luecken(alle);
        int mindest = e.Minuten(GanzerRuhetag, "min_std");
        if (luecken.Any(l => ZaehltFuerWoche(l, mindest, wVon, wBis))) yield break;

        if (e.Wert(GanzerRuhetag, "ausnahme_24") != 0)
        {
            int ruhetage = Enumerable.Range(0, 7).Select(mo.AddDays)
                .Count(d => !alle.Any(s => s.Tag == d) || sonntagsgleich?.ContainsKey(d) == true);
            if (ruhetage >= 2 && luecken.Any(l => ZaehltFuerWoche(l, 24 * 60, wVon, wBis))) yield break;
        }

        var laengste = luecken.Where(l => !l.Offen && l.Bis > wVon && l.Von < wBis).MaxBy(l => l.Minuten);
        var text = laengste == null
            ? $"Keine freie Zeit am Stück — für den ganzen Ruhetag braucht es mindestens {Dauer(mindest)}."
            : $"Längste freie Zeit am Stück: {Dauer(laengste.Minuten)} ({Zeitpunkt(laengste.Von)} – {Zeitpunkt(laengste.Bis)}) — "
              + $"für den ganzen Ruhetag braucht es mindestens {Dauer(mindest)} (tägliche Ruhezeit + 24 Std.).";
        yield return new(p.EmployeeId, GanzerRuhetag, mo, so, text,
            laengste?.Minuten ?? 0, mindest, Recht(GanzerRuhetag), st);
    }

    record Arbeitstag(DateOnly Tag, DateTime Beginn, DateTime Ende, int Minuten, List<Stempel> Stempel);

    static List<Arbeitstag> Arbeitstage(List<Stempel> alle) =>
        alle.GroupBy(s => s.Tag).OrderBy(g => g.Key)
            .Select(g => new Arbeitstag(g.Key, g.Min(s => s.Ein), g.Max(s => s.Aus), g.Sum(s => s.Minuten), g.OrderBy(s => s.Ein).ToList()))
            .ToList();

    static IEnumerable<Verstoss> PruefeRuhezeit(Person p, DateOnly von, DateOnly bis, List<Stempel> alle, ArbeitszeitEinstellungen e)
    {
        var tage = Arbeitstage(alle);
        int normal = e.Minuten(Ruhezeit, "min_std");
        int kurz = e.Minuten(Ruhezeit, "verkuerzt_std");
        int jugend = e.Minuten(Ruhezeit, "jugend_std");
        var verkuerztInWoche = new HashSet<DateOnly>();
        for (int i = 0; i + 1 < tage.Count; i++)
        {
            var a = tage[i];
            var b = tage[i + 1];
            int frei = (int)Math.Round((b.Beginn - a.Ende).TotalMinutes);
            if (frei <= 0) continue;
            bool jung = p.Geburt.HasValue && MindestlohnAlter.Alter(p.Geburt.Value, a.Tag.ToDateTime(TimeOnly.MinValue)) < 18;
            int mindest = jung ? jugend : normal;
            if (frei >= mindest) continue;
            bool verkuerzungFrei = !jung && frei >= kurz && verkuerztInWoche.Add(Montag(a.Tag));
            if (verkuerzungFrei || a.Tag < von || a.Tag > bis) continue;

            string grund = jung ? "Jugendliche brauchen" : "vorgeschrieben sind";
            string zusatz = jung ? "" : frei >= kurz
                ? $" Die Verkürzung auf {Dauer(kurz)} ist in dieser Woche schon gebraucht."
                : $" Einmal pro Woche sind {Dauer(kurz)} erlaubt.";
            yield return new(p.EmployeeId, Ruhezeit, a.Tag, b.Tag,
                $"Schluss {Zeitpunkt(a.Ende)}, wieder Beginn {Zeitpunkt(b.Beginn)} — nur {Dauer(frei)} Ruhezeit, {grund} {Dauer(mindest)}.{zusatz}",
                frei, mindest, Recht(Ruhezeit), a.Stempel.Concat(b.Stempel).ToList());
        }
    }

    static IEnumerable<Verstoss> PruefeSiebenTage(Person p, DateOnly von, DateOnly bis, List<Stempel> alle, ArbeitszeitEinstellungen e)
    {
        var tage = Arbeitstage(alle);
        int max = (int)e.Wert(SiebenTage, "max_tage");
        int maxTag = e.Minuten(SiebenTage, "max_std_tag");
        int freiDanach = e.Minuten(SiebenTage, "frei_std");
        int i = 0;
        while (i < tage.Count)
        {
            int j = i;
            while (j + 1 < tage.Count && tage[j + 1].Tag == tage[j].Tag.AddDays(1)) j++;
            int anzahl = j - i + 1;
            var serie = tage.GetRange(i, anzahl);
            i = j + 1;
            if (anzahl <= max) continue;
            var ueber = serie[max].Tag;
            if (ueber < von || ueber > bis) continue;

            string text;
            if (anzahl == max + 1)
            {
                var lang = serie.Where(t => t.Minuten > maxTag).ToList();
                int frei = i < tage.Count ? (int)Math.Round((tage[i].Beginn - serie[^1].Ende).TotalMinutes) : int.MaxValue;
                if (lang.Count == 0 && frei >= freiDanach) continue;
                var gruende = new List<string>();
                if (lang.Count > 0)
                    gruende.Add("mehr als " + Dauer(maxTag) + " am " + string.Join(", ", lang.Select(t => $"{Wt[(int)t.Tag.DayOfWeek]} {t.Tag:dd.MM.} ({Dauer(t.Minuten)})")));
                if (frei < freiDanach)
                    gruende.Add($"danach nur {Dauer(frei)} frei statt {Dauer(freiDanach)}");
                text = $"{anzahl} Tage in Folge gearbeitet — erlaubt nur mit höchstens {Dauer(maxTag)} pro Tag und danach {Dauer(freiDanach)} frei. Hier: {string.Join("; ", gruende)}.";
            }
            else
                text = $"{anzahl} Tage in Folge gearbeitet — erlaubt sind höchstens {max + 1} (und nur mit höchstens {Dauer(maxTag)} pro Tag und danach {Dauer(freiDanach)} frei).";

            yield return new(p.EmployeeId, SiebenTage, serie[0].Tag, serie[^1].Tag, text,
                anzahl, max, Recht(SiebenTage), serie.SelectMany(t => t.Stempel).ToList());
        }
    }

    static IEnumerable<Verstoss> PruefeNaechte(Person p, DateOnly von, DateOnly bis)
    {
        var naechte = p.NachtTage.Distinct().OrderBy(d => d).ToList();
        DateOnly? start = null, ende = null;
        int max = 0;
        foreach (var n in naechte.Where(d => d >= von && d <= bis))
        {
            int anzahl = naechte.Count(d => d > n.AddDays(-NaechteFensterTage) && d <= n);
            if (anzahl > NaechteMax && p.Nachweis?.GiltAm(n) != true)
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

    static Verstoss NaechteVerstoss(Person p, DateOnly von, DateOnly bis, int max)
    {
        var fehlt = (p.Nachweis ?? new NachtNachweis(false, null, null, false)).Fehlt(von, bis);
        return new(p.EmployeeId, Naechte, von, bis,
            $"Bis zu {max} Nächte in 6 Wochen — ab mehr als {NaechteMax} Nächten braucht es die ärztliche Untersuchung und das Zeugnis. {fehlt}".TrimEnd(),
            max, NaechteMax, Recht(Naechte), Array.Empty<Stempel>());
    }

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
