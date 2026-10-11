using System.Globalization;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using static HrSystem.Services.GastroSocialAbgleich;

namespace HrSystem.Services;

/// <summary>
/// HR-Hub → Kontrolle → «FZ-Abgleich GastroSocial» (Walter 12.10.2026).
///
/// GastroSocial «Familienzulagenbescheinigung» (Excel: Leistung · Bezüger AHV-Nr. · Bezüger Name/Vorname ·
/// Kind AHV-Nr. · Kind Vorname · GebDat · Von · Bis · Monate · Ansatz · TotalBetrag) gegen die Zulagen
/// bei den Kindern in OneCrew (MA → Familie), Monat für Monat:
///   • Fehlt in OneCrew — GastroSocial zahlt, in OneCrew kein Kind / keine Zulage in diesen Monaten
///   • Abweichung — Monate oder Ansatz unterscheiden sich
///   • Nur in OneCrew — Zulage in OneCrew (Vertragsmonate dieser Filiale), Kind fehlt in der Bescheinigung
///   • Nicht zuordenbar — Bezüger in OneCrew nicht gefunden
///   • Passt
/// Ein Monat zählt in OneCrew, wenn eine Zulage ihn berührt (wie im Lohn). Reine Auswertung.
/// </summary>
public static class GastroSocialFzAbgleich
{
    public class Zeile
    {
        public int Nr { get; set; }
        public string? Leistung { get; set; }
        public string? BezuegerAhv { get; set; }
        public string? BezuegerName { get; set; }
        public string? BezuegerVorname { get; set; }
        public string? KindAhv { get; set; }
        public string? KindVorname { get; set; }
        /// <summary>Nur wenn der Export eine Spalte «Kind Name» hat (Bescheinigung 2026: nein).</summary>
        public string? KindNachname { get; set; }
        /// <summary>Nur wenn der Export eine Geschlechts-Spalte hat (Bescheinigung 2026: nein).</summary>
        public string? KindGeschlecht { get; set; }
        public DateTime? KindGeburt { get; set; }
        public DateTime? Von { get; set; }
        public DateTime? Bis { get; set; }
        public int? Monate { get; set; }
        public decimal? Ansatz { get; set; }
        public decimal? Total { get; set; }
        public string? Bemerkung { get; set; }
        /// <summary>Geburts-/Adoptionszulage (einmalig) statt laufender Kinder-/Ausbildungszulage.</summary>
        public bool Einmalig => (Leistung ?? "").Contains("geburt", StringComparison.OrdinalIgnoreCase)
                             || (Leistung ?? "").Contains("adopt", StringComparison.OrdinalIgnoreCase);
    }

    public class Zulage
    {
        public int Id { get; set; }
        public string? Typ { get; set; }
        public int? Satz { get; set; }
        public DateOnly Von { get; set; }
        public DateOnly? Bis { get; set; }
        /// <summary>Monatsbetrag wie im Lohn (FAK-Tarif der Filiale), je Monatserster im Bescheinigungs-Zeitraum.</summary>
        public Dictionary<DateOnly, decimal> Betraege { get; set; } = new();
        public bool Einmalig => Typ is "GZ" or "AdoptZ";
    }

    public class Kind
    {
        public int Id { get; set; }
        public string? Vorname { get; set; }
        public string? Nachname { get; set; }
        public string? Ahv { get; set; }
        public DateTime? Geburt { get; set; }
        /// <summary>«Männlich» / «Weiblich» / «Divers» wie im Familien-Fenster.</summary>
        public string? Geschlecht { get; set; }
        public List<Zulage> Zulagen { get; set; } = new();
    }

    public class Ma
    {
        public int Id { get; set; }
        public string? Nummer { get; set; }
        public string? Vorname { get; set; }
        public string? Nachname { get; set; }
        public string? Ahv { get; set; }
        /// <summary>Hat (oder hatte) einen Vertrag in der Filiale.</summary>
        public bool InFiliale { get; set; }
        /// <summary>Verträge in dieser Filiale (Ende null = läuft).</summary>
        public List<(DateTime Von, DateTime? Bis)> VertraegeHier { get; set; } = new();
        public List<Kind> Kinder { get; set; } = new();
    }

    public class Treffer
    {
        public int? Zeile { get; set; }
        public string? Bezueger { get; set; }
        public string? BezuegerAhv { get; set; }
        public string? Kind { get; set; }
        public string? KindAhv { get; set; }
        public DateTime? KindGeburt { get; set; }
        /// <summary>GastroSocial: Zeiträume mit Ansatz, z.B. «01.01.2026–31.07.2026 · 7 × 278.00».</summary>
        public string? GastroSocial { get; set; }
        public decimal? GsTotal { get; set; }
        /// <summary>OneCrew: Zulagen des Kindes, die den Zeitraum berühren.</summary>
        public string? OneCrew { get; set; }
        /// <summary>OneCrew-Betrag in den Monaten, die GastroSocial bescheinigt.</summary>
        public decimal? OcTotal { get; set; }
        public int? EmployeeId { get; set; }
        public string? OneCrewName { get; set; }
        public string? Personalnummer { get; set; }
        public int? FamilyMemberId { get; set; }
        public string? MatchArt { get; set; }
        public string? Hinweis { get; set; }
        /// <summary>AHV-Nummer des Kindes laut GastroSocial, wenn sie in OneCrew fehlt und gültig ist — «übernehmen».</summary>
        public string? KindAhvUebernehmen { get; set; }
        /// <summary>Abweichende/fehlende Angaben zu Kind und Bezüger (AHV, Vorname, Name, Geburtsdatum, Geschlecht).</summary>
        public List<string> Angaben { get; set; } = new();
    }

    public class Ergebnis
    {
        public int Zeilen { get; set; }
        public int Kinder { get; set; }
        public DateTime? Von { get; set; }
        public DateTime? Bis { get; set; }
        public decimal TotalGs { get; set; }
        public List<Treffer> Fehlt { get; set; } = new();
        public List<Treffer> Abweichung { get; set; } = new();
        public List<Treffer> NurOneCrew { get; set; } = new();
        public List<Treffer> NichtZuordenbar { get; set; } = new();
        public List<Treffer> Passt { get; set; } = new();
        /// <summary>Zusätzliche Sicht: Kinder (zugeordnet), bei denen Angaben abweichen oder in OneCrew fehlen.</summary>
        public List<Treffer> AngabenPruefen { get; set; } = new();
    }

    public static IEnumerable<DateOnly> Monate(DateTime von, DateTime bis)
    {
        for (var m = new DateOnly(von.Year, von.Month, 1); m <= DateOnly.FromDateTime(bis); m = m.AddMonths(1))
            yield return m;
    }

    private static bool Beruehrt(Zulage z, DateOnly monat) =>
        z.Von <= monat.AddMonths(1).AddDays(-1) && (z.Bis == null || z.Bis >= monat);

    private static decimal OcMonat(Kind k, DateOnly monat) =>
        k.Zulagen.Where(z => !z.Einmalig && Beruehrt(z, monat))
            .Sum(z => z.Betraege.TryGetValue(monat, out var b) ? b : 0m);

    private static bool VertragHier(Ma m, DateOnly monat)
    {
        var start = monat.ToDateTime(TimeOnly.MinValue);
        var ende = monat.AddMonths(1).AddDays(-1).ToDateTime(TimeOnly.MinValue);
        return m.VertraegeHier.Any(v => v.Von.Date <= ende && (v.Bis == null || v.Bis.Value.Date >= start));
    }

    public static string Bereiche(IEnumerable<DateOnly> monate)
    {
        var l = monate.Distinct().OrderBy(m => m).ToList();
        var teile = new List<string>();
        for (int i = 0; i < l.Count;)
        {
            int j = i;
            while (j + 1 < l.Count && l[j + 1] == l[j].AddMonths(1)) j++;
            teile.Add(i == j ? $"{l[i]:MM.yyyy}" : $"{l[i]:MM.yyyy}–{l[j]:MM.yyyy}");
            i = j + 1;
        }
        return string.Join(", ", teile);
    }

    private static string? GeschlechtNorm(string? g)
    {
        var s = (g ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        if (s is "m" or "mann" or "male" || s.StartsWith("männ") || s.StartsWith("maenn")) return "männlich";
        if (s is "w" or "f" or "frau" or "female" || s.StartsWith("weib")) return "weiblich";
        return s;
    }

    /// <summary>Angaben zum Kind laut GastroSocial gegen OneCrew — jede Abweichung und jedes fehlende Feld.</summary>
    public static IEnumerable<string> KindAngaben(Zeile z, Kind k)
    {
        var gsAhv = NormAhv(z.KindAhv);
        if (gsAhv.Length == 13 && NormAhv(k.Ahv) != gsAhv)
            yield return NormAhv(k.Ahv).Length == 0
                ? "AHV-Nummer des Kindes fehlt in OneCrew"
                : $"AHV-Nummer des Kindes: GastroSocial {FormatAhv(z.KindAhv)}, OneCrew {FormatAhv(k.Ahv)}";
        if (!string.IsNullOrWhiteSpace(z.KindVorname) && NormName(z.KindVorname) != NormName(k.Vorname))
            yield return string.IsNullOrWhiteSpace(k.Vorname)
                ? $"Vorname des Kindes fehlt in OneCrew (GastroSocial «{z.KindVorname}»)"
                : $"Vorname des Kindes: GastroSocial «{z.KindVorname}», OneCrew «{k.Vorname}»";
        if (!string.IsNullOrWhiteSpace(z.KindNachname) && NormName(z.KindNachname) != NormName(k.Nachname))
            yield return string.IsNullOrWhiteSpace(k.Nachname)
                ? $"Name des Kindes fehlt in OneCrew (GastroSocial «{z.KindNachname}»)"
                : $"Name des Kindes: GastroSocial «{z.KindNachname}», OneCrew «{k.Nachname}»";
        if (z.KindGeburt != null && k.Geburt?.Date != z.KindGeburt.Value.Date)
            yield return k.Geburt == null
                ? $"Geburtsdatum des Kindes fehlt in OneCrew (GastroSocial {z.KindGeburt:dd.MM.yyyy})"
                : $"Geburtsdatum des Kindes: GastroSocial {z.KindGeburt:dd.MM.yyyy}, OneCrew {k.Geburt:dd.MM.yyyy}";
        var gsG = GeschlechtNorm(z.KindGeschlecht);
        var ocG = GeschlechtNorm(k.Geschlecht);
        if (gsG != null && gsG != ocG)
            yield return ocG == null
                ? $"Geschlecht des Kindes fehlt in OneCrew (GastroSocial {gsG})"
                : $"Geschlecht des Kindes: GastroSocial {gsG}, OneCrew {ocG}";
        // Spalte «Nat» bewusst nicht: die Nationalität wird beim Kind seit 11.10.2026 nicht mehr erfasst.
    }

    /// <summary>Bezüger laut GastroSocial gegen den zugeordneten MA (AHV-Nummer, Name, Vorname).</summary>
    public static IEnumerable<string> BezuegerAngaben(Zeile z, Ma m)
    {
        var gsAhv = NormAhv(z.BezuegerAhv);
        if (gsAhv.Length == 13 && NormAhv(m.Ahv) != gsAhv)
            yield return NormAhv(m.Ahv).Length == 0
                ? "AHV-Nummer des MA fehlt in OneCrew"
                : $"AHV-Nummer des MA: GastroSocial {FormatAhv(z.BezuegerAhv)}, OneCrew {FormatAhv(m.Ahv)}";
        var gsName = NameKey(z.BezuegerVorname, z.BezuegerName);
        if (!string.IsNullOrWhiteSpace(z.BezuegerName) && gsName != NameKey(m.Vorname, m.Nachname)
            && NameKey(z.BezuegerName, z.BezuegerVorname) != NameKey(m.Vorname, m.Nachname))
            yield return $"Name des MA: GastroSocial «{z.BezuegerVorname} {z.BezuegerName}», OneCrew «{m.Vorname} {m.Nachname}»";
    }

    /// <summary>«Ana Marija» = «Anamarija», «Lea» = «Lea Sophie» — gleiche Schreibweise ohne Leerzeichen oder Vorname enthalten.</summary>
    private static bool VornamePasst(string? a, string? b)
    {
        var x = NormName(a).Replace(" ", "");
        var y = NormName(b).Replace(" ", "");
        if (x.Length == 0 || y.Length == 0) return x.Length == 0;
        return x == y || x.StartsWith(y) || y.StartsWith(x);
    }

    private static string Chf(decimal b) => b.ToString("#,##0.00", CultureInfo.InvariantCulture).Replace(",", "'");

    private static string ZulageText(Zulage z)
    {
        var art = z.Satz != null ? $"{z.Typ} Satz {z.Satz}" : z.Typ ?? "Zulage";
        var bis = z.Bis != null ? $"{z.Bis:dd.MM.yyyy}" : "offen";
        var betrag = z.Betraege.Values.Where(b => b > 0).Distinct().ToList();
        return $"{art} · {z.Von:dd.MM.yyyy}–{bis}" + (betrag.Count == 1 ? $" · {Chf(betrag[0])}" : "");
    }

    public static Ergebnis Vergleiche(IReadOnlyList<Zeile> zeilen, IReadOnlyList<Ma> alle)
    {
        var res = new Ergebnis
        {
            Zeilen = zeilen.Count,
            Von = zeilen.Where(z => z.Von != null).Select(z => z.Von).Min(),
            Bis = zeilen.Where(z => z.Bis != null).Select(z => z.Bis).Max(),
            TotalGs = zeilen.Sum(z => z.Total ?? 0m),
        };

        var byAhv = alle.Where(m => NormAhv(m.Ahv).Length == 13).ToLookup(m => NormAhv(m.Ahv));
        var filiale = alle.Where(m => m.InFiliale).ToList();
        var byName = filiale.ToLookup(m => NameKey(m.Vorname, m.Nachname));
        var kindByAhv = alle.SelectMany(m => m.Kinder.Select(k => (m, k)))
            .Where(x => NormAhv(x.k.Ahv).Length == 13).ToLookup(x => NormAhv(x.k.Ahv));

        // Bezüger + Kind je Zeile auflösen, dann pro Kind zusammenfassen (KZ → AZ im Jahr = zwei Zeilen).
        var gruppen = new Dictionary<string, (Ma? ma, Kind? kind, string? art, List<Zeile> zeilen, List<string> hinweise)>();
        foreach (var z in zeilen)
        {
            Ma? ma = null;
            string? art = null;
            var hinweise = new List<string>();
            var ahv = NormAhv(z.BezuegerAhv);
            if (ahv.Length == 13 && byAhv.Contains(ahv))
            {
                var c = byAhv[ahv].ToList();
                ma = c.FirstOrDefault(m => m.InFiliale) ?? c.First();
                art = "AHV";
            }
            var kAhv = NormAhv(z.KindAhv);
            if (ma == null && kAhv.Length == 13 && kindByAhv.Contains(kAhv))
            {
                var c = kindByAhv[kAhv].Select(x => x.m).Distinct().ToList();
                if (c.Count == 1) { ma = c[0]; art = "KIND_AHV"; }
            }
            if (ma == null)
            {
                var c = byName[NameKey(z.BezuegerVorname, z.BezuegerName)].ToList();
                if (c.Count == 0) c = byName[NameKey(z.BezuegerName, z.BezuegerVorname)].ToList();
                if (c.Count == 1) { ma = c[0]; art = "NAME"; }
            }
            if (ma != null) hinweise.AddRange(BezuegerAngaben(z, ma));

            Kind? kind = null;
            if (ma != null)
            {
                if (kAhv.Length == 13) kind = ma.Kinder.FirstOrDefault(k => NormAhv(k.Ahv) == kAhv);
                if (kind == null && z.KindGeburt != null)
                {
                    var c = ma.Kinder.Where(k => k.Geburt?.Date == z.KindGeburt.Value.Date && VornamePasst(k.Vorname, z.KindVorname)).ToList();
                    if (c.Count == 1) kind = c[0];
                }
                if (kind == null)
                {
                    var c = ma.Kinder.Where(k => NormName(k.Vorname) == NormName(z.KindVorname)).ToList();
                    if (c.Count == 1) kind = c[0];
                }
                if (kind != null) hinweise.AddRange(KindAngaben(z, kind));
            }

            var key = ma == null ? $"x{z.Nr}"
                : kind != null ? $"k{kind.Id}"
                : $"m{ma.Id}|{NormName(z.KindVorname)}|{z.KindGeburt:yyyyMMdd}";
            if (!gruppen.TryGetValue(key, out var g))
            {
                g = (ma, kind, art, new List<Zeile>(), new List<string>());
                gruppen[key] = g;
            }
            g.zeilen.Add(z);
            foreach (var h in hinweise) if (!g.hinweise.Contains(h)) g.hinweise.Add(h);
        }
        res.Kinder = gruppen.Count;

        var gefundeneKinder = new HashSet<int>();
        foreach (var g in gruppen.Values)
        {
            var z0 = g.zeilen[0];
            var t = new Treffer
            {
                Zeile = z0.Nr,
                Bezueger = $"{z0.BezuegerVorname} {z0.BezuegerName}".Trim(),
                BezuegerAhv = FormatAhv(z0.BezuegerAhv),
                Kind = z0.KindVorname,
                KindAhv = FormatAhv(z0.KindAhv),
                KindGeburt = z0.KindGeburt,
                GastroSocial = string.Join("; ", g.zeilen.OrderBy(z => z.Von).Select(z =>
                    (z.Einmalig ? (z.Leistung ?? "Geburtszulage") + " · " : "")
                    + $"{z.Von:dd.MM.yyyy}–{z.Bis:dd.MM.yyyy}"
                    + (z.Monate is > 0 && z.Ansatz != null ? $" · {z.Monate} × {Chf(z.Ansatz.Value)}" : ""))),
                GsTotal = g.zeilen.Sum(z => z.Total ?? 0m),
                MatchArt = g.art,
            };
            var hinweise = new List<string>(g.hinweise);
            t.Angaben = new List<string>(g.hinweise);
            if (t.Angaben.Count > 0 && g.ma != null) res.AngabenPruefen.Add(t);

            if (g.ma == null)
            {
                hinweise.Insert(0, "Bezüger in OneCrew nicht gefunden — AHV-Nummer beim MA nachtragen oder prüfen, ob die Person bei uns arbeitet");
                t.Hinweis = string.Join(" · ", hinweise);
                res.NichtZuordenbar.Add(t);
                continue;
            }
            t.EmployeeId = g.ma.Id;
            t.OneCrewName = $"{g.ma.Vorname} {g.ma.Nachname}".Trim();
            t.Personalnummer = g.ma.Nummer;
            if (!g.ma.InFiliale) hinweise.Add("MA hat keinen Vertrag in dieser Filiale");

            if (g.kind == null)
            {
                hinweise.Insert(0, "Kind ist in OneCrew nicht erfasst (MA → Familie) — Kind und Zulage erfassen, sonst fehlt sie im Lohn");
                t.Hinweis = string.Join(" · ", hinweise);
                res.Fehlt.Add(t);
                continue;
            }
            gefundeneKinder.Add(g.kind.Id);
            t.FamilyMemberId = g.kind.Id;
            t.Kind = $"{g.kind.Vorname} {g.kind.Nachname}".Trim();
            if (NormAhv(g.kind.Ahv).Length == 0)
                t.KindAhvUebernehmen = g.zeilen.Select(z => z.KindAhv)
                    .Where(a => Elm.ElmPlausibilitaet.AhvPruefzifferStimmt(a ?? ""))
                    .Select(FormatAhv).FirstOrDefault();

            var gsMonate = new Dictionary<DateOnly, decimal>();
            foreach (var z in g.zeilen.Where(z => !z.Einmalig && z.Von != null && z.Bis != null))
                foreach (var m in Monate(z.Von!.Value, z.Bis!.Value))
                    gsMonate[m] = (gsMonate.TryGetValue(m, out var b) ? b : 0m) + (z.Ansatz ?? 0m);
            var ocMonate = new Dictionary<DateOnly, decimal>();
            if (res.Von != null && res.Bis != null)
                foreach (var m in Monate(res.Von.Value, res.Bis.Value))
                {
                    var b = OcMonat(g.kind, m);
                    if (b > 0 && (gsMonate.ContainsKey(m) || VertragHier(g.ma, m))) ocMonate[m] = b;
                }

            var relevant = g.kind.Zulagen.Where(z => res.Von == null || res.Bis == null
                || (z.Von <= DateOnly.FromDateTime(res.Bis.Value) && (z.Bis == null || z.Bis >= DateOnly.FromDateTime(res.Von.Value)))).ToList();
            t.OneCrew = relevant.Count > 0 ? string.Join("; ", relevant.OrderBy(z => z.Von).Select(ZulageText)) : null;
            t.OcTotal = gsMonate.Keys.Sum(m => ocMonate.TryGetValue(m, out var b) ? b : 0m);

            var fehlen = gsMonate.Keys.Where(m => !ocMonate.ContainsKey(m)).ToList();
            var zuviel = ocMonate.Keys.Where(m => !gsMonate.ContainsKey(m)).ToList();
            var betrag = gsMonate.Keys.Where(m => ocMonate.TryGetValue(m, out var b) && b != gsMonate[m]).ToList();
            var einmaligFehlt = g.zeilen.Any(z => z.Einmalig) && !g.kind.Zulagen.Any(z => z.Einmalig);

            if (gsMonate.Count > 0 && fehlen.Count == gsMonate.Count && zuviel.Count == 0)
            {
                hinweise.Insert(0, $"GastroSocial zahlt {Bereiche(fehlen)}, in OneCrew keine Zulage in diesen Monaten — Zulage beim Kind erfassen");
                t.Hinweis = string.Join(" · ", hinweise);
                res.Fehlt.Add(t);
                continue;
            }
            var abw = new List<string>();
            if (fehlen.Count > 0) abw.Add($"in OneCrew fehlt {Bereiche(fehlen)}");
            if (zuviel.Count > 0) abw.Add($"OneCrew zahlt zusätzlich {Bereiche(zuviel)}, GastroSocial nicht");
            foreach (var grp in betrag.GroupBy(m => (gsMonate[m], ocMonate[m])))
                abw.Add($"Ansatz {Bereiche(grp)}: GastroSocial {Chf(grp.Key.Item1)}, OneCrew {Chf(grp.Key.Item2)}");
            if (einmaligFehlt) abw.Add("Geburts-/Adoptionszulage fehlt in OneCrew");
            if (abw.Count > 0)
            {
                hinweise.InsertRange(0, abw);
                t.Hinweis = string.Join(" · ", hinweise);
                res.Abweichung.Add(t);
                continue;
            }
            t.Hinweis = hinweise.Count > 0 ? string.Join(" · ", hinweise) : null;
            res.Passt.Add(t);
        }

        // Zulagen in OneCrew, deren Kind GastroSocial nicht bescheinigt (nur Vertragsmonate dieser Filiale).
        if (res.Von != null && res.Bis != null)
        {
            var monate = Monate(res.Von.Value, res.Bis.Value).ToList();
            foreach (var m in filiale)
                foreach (var k in m.Kinder.Where(k => !gefundeneKinder.Contains(k.Id)))
                {
                    var oc = monate.Where(mo => VertragHier(m, mo) && OcMonat(k, mo) > 0).ToList();
                    if (oc.Count == 0) continue;
                    res.NurOneCrew.Add(new Treffer
                    {
                        Bezueger = $"{m.Vorname} {m.Nachname}".Trim(),
                        BezuegerAhv = FormatAhv(m.Ahv),
                        Kind = $"{k.Vorname} {k.Nachname}".Trim(),
                        KindAhv = FormatAhv(k.Ahv),
                        KindGeburt = k.Geburt,
                        OneCrew = string.Join("; ", k.Zulagen.Where(z => !z.Einmalig && monate.Any(mo => Beruehrt(z, mo)))
                            .OrderBy(z => z.Von).Select(ZulageText)),
                        OcTotal = oc.Sum(mo => OcMonat(k, mo)),
                        EmployeeId = m.Id,
                        OneCrewName = $"{m.Vorname} {m.Nachname}".Trim(),
                        Personalnummer = m.Nummer,
                        FamilyMemberId = k.Id,
                        Hinweis = $"OneCrew zahlt {Bereiche(oc)}, GastroSocial bescheinigt nichts — Antrag fehlt, anderer Elternteil bezieht sie oder Zulage in OneCrew beenden",
                    });
                }
        }

        static List<Treffer> Sort(List<Treffer> l) => l
            .OrderBy(t => t.OneCrewName ?? t.Bezueger, StringComparer.OrdinalIgnoreCase)
            .ThenBy(t => t.Kind, StringComparer.OrdinalIgnoreCase).ToList();
        res.Fehlt = Sort(res.Fehlt);
        res.Abweichung = Sort(res.Abweichung);
        res.NurOneCrew = Sort(res.NurOneCrew);
        res.NichtZuordenbar = Sort(res.NichtZuordenbar);
        res.Passt = Sort(res.Passt);
        res.AngabenPruefen = Sort(res.AngabenPruefen);
        return res;
    }

    // ─────────────────────────── Excel lesen ───────────────────────────

    public static List<Zeile> LiesExcel(Stream stream, string fileName)
    {
        IWorkbook wb = fileName.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
            ? new HSSFWorkbook(stream)
            : new XSSFWorkbook(stream);
        var sheet = wb.GetSheetAt(0) ?? throw new InvalidOperationException("Leeres Arbeitsblatt.");

        int kopf = -1;
        var spalten = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r <= Math.Min(sheet.LastRowNum, 15) && kopf < 0; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < row.LastCellNum; c++)
            {
                var h = Ws(Text(row.GetCell(c)));
                if (h.Length > 0 && !map.ContainsKey(h)) map[h] = c;
            }
            if (map.Keys.Any(k => k.StartsWith("Bezüger AHV", StringComparison.OrdinalIgnoreCase))
                && map.ContainsKey("Kind Vorname"))
            {
                kopf = r;
                spalten = map;
            }
        }
        if (kopf < 0)
            throw new InvalidOperationException("Kopfzeile mit «Bezüger AHV-Nr.» und «Kind Vorname» nicht gefunden — bitte die «Familienzulagenbescheinigung» von GastroSocial hochladen.");

        int Sp(params string[] namen)
        {
            foreach (var n in namen) if (spalten.TryGetValue(n, out var i)) return i;
            return -1;
        }
        int cLst = Sp("Leistung"), cBAhv = Sp("Bezüger AHV-Nr.", "Bezüger AHV-Nr", "Bezüger AHV");
        int cBName = Sp("Bezüger Name"), cBVor = Sp("Bezüger Vorname");
        int cKAhv = Sp("Kind AHV-Nr.", "Kind AHV-Nr", "Kind AHV"), cKVor = Sp("Kind Vorname");
        int cKName = Sp("Kind Name", "Kind Nachname"), cKGes = Sp("Kind Geschlecht", "Geschlecht Kind", "Kind G");        int cGeb = Sp("GebDat", "Geburtsdatum", "Kind GebDat"), cVon = Sp("Von"), cBis = Sp("Bis");
        int cMon = Sp("Monate"), cAns = Sp("Ansatz"), cTot = Sp("TotalBetrag", "Total", "Betrag");
        int cBem = Sp("Bemerkungen", "Bemerkung");

        var liste = new List<Zeile>();
        for (int r = kopf + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            string Feld(int c) => c >= 0 ? Ws(Text(row.GetCell(c))) : "";
            decimal? Zahl(int c)
            {
                var s = Feld(c).Replace("'", "").Replace("’", "");
                return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;
            }
            var bName = Feld(cBName);
            var kVor = Feld(cKVor);
            var bAhv = Feld(cBAhv);
            if (bName.Length == 0 && kVor.Length == 0 && bAhv.Length == 0) continue;
            liste.Add(new Zeile
            {
                Nr = r + 1,
                Leistung = Leer(Feld(cLst)),
                BezuegerAhv = Leer(bAhv),
                BezuegerName = Leer(bName),
                BezuegerVorname = Leer(Feld(cBVor)),
                KindAhv = Leer(Feld(cKAhv)),
                KindVorname = Leer(kVor),
                KindNachname = Leer(Feld(cKName)),
                KindGeschlecht = Leer(Feld(cKGes)),                KindGeburt = cGeb >= 0 ? Datum(row.GetCell(cGeb)) : null,
                Von = cVon >= 0 ? Datum(row.GetCell(cVon)) : null,
                Bis = cBis >= 0 ? Datum(row.GetCell(cBis)) : null,
                Monate = Zahl(cMon) is decimal mo ? (int)mo : null,
                Ansatz = Zahl(cAns),
                Total = Zahl(cTot),
                Bemerkung = Leer(Feld(cBem)),
            });
        }
        return liste;
    }
}
