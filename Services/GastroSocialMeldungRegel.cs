namespace HrSystem.Services;

/// <summary>
/// An-/Abmeldung bei der Ausgleichskasse GastroSocial (Walter 13.10.2026) — EINE Regel für
/// AKIS-Seite, AHV-Abgleich und To-do. Jede Filiale ist eine eigene GmbH mit eigenem Konto.
///
/// Anmeldung = Vertrag in der Filiale beginnt, und am Vortag lief dort kein anderer Vertrag
///             (nahtlose Vertragsanpassung = keine neue Anmeldung).
/// Abmeldung = zwei Auslöser, dieselbe Zeile (Walter 30.10.2026, Filialwechsel):
///             (1) erfasster Austritt (Employee.ExitDate) UND gesetztes «Gekündigt per»,
///                 Filiale des letzten Vertrags. Ein befristeter Vertrag ohne Kündigung per
///                 ist keine Abmeldung.
///             (2) Filialwechsel: der Abschnitt in der alten Filiale endet, und als Nächstes
///                 wird in einer anderen Filiale gearbeitet. Datum = letzter Vertragstag dort.
///                 Die Person bleibt angestellt, «Gekündigt per» bleibt leer.
///             Auf der AKIS-Liste erscheint die Zeile erst am Folgetag.
/// Erledigt  = in <c>gastrosocial_meldung</c> steht für MA, Filiale und Art eine Meldung, deren
///             Stichtag höchstens <see cref="ToleranzTage"/> vom Ereignis abweicht (kleine Korrekturen
///             am Eintritts-/Austrittsdatum heben die Meldung nicht auf).
/// </summary>
public static class GastroSocialMeldungRegel
{
    public const string Anmeldung = "ANMELDUNG";
    public const string Abmeldung = "ABMELDUNG";
    public const int ToleranzTage = 31;

    /// <summary>Ab diesem Datum meldet das To-do fehlende An-/Abmeldungen (älteres = Altbestand).</summary>
    public static readonly DateOnly ToDoAb = new(2026, 1, 1);

    public sealed record Vertrag(int EmployeeId, int? CompanyProfileId, DateTime Start, DateTime? Ende);
    public sealed record Ereignis(int EmployeeId, int CompanyProfileId, string Art, DateOnly Datum);
    public sealed record Meldung(int EmployeeId, int CompanyProfileId, string Art, DateOnly Stichtag);
    public sealed record PersonAustritt(int EmployeeId, DateTime? Datum, DateTime? GekuendigtPer);

    /// <summary>Alle Anmelde-Ereignisse (über alle Zeit), pro MA und Filiale.</summary>
    public static List<Ereignis> Anmeldungen(IEnumerable<Vertrag> vertraege)
    {
        var res = new List<Ereignis>();
        foreach (var g in vertraege.Where(v => v.CompanyProfileId != null)
                     .GroupBy(v => (v.EmployeeId, Cp: v.CompanyProfileId!.Value)))
        {
            var liste = g.ToList();
            var tage = new HashSet<DateOnly>();
            for (int i = 0; i < liste.Count; i++)
            {
                var c = liste[i];
                var vortag = c.Start.Date.AddDays(-1);
                bool liefSchon = liste.Where((o, j) => j != i)
                    .Any(o => o.Start.Date <= vortag && (o.Ende == null || o.Ende.Value.Date >= vortag));
                if (!liefSchon) tage.Add(DateOnly.FromDateTime(c.Start));
            }
            res.AddRange(tage.OrderBy(t => t).Select(t => new Ereignis(g.Key.EmployeeId, g.Key.Cp, Anmeldung, t)));
        }
        return res;
    }

    /// <summary>
    /// Abmelde-Ereignis eines MA: Austrittsdatum, Filiale des letzten Vertrags. Null = keins.
    /// Ohne «Gekündigt per» gibt es keine Abmeldung — befristete Verträge haben oft trotzdem ein Austrittsdatum.
    /// </summary>
    public static Ereignis? AbmeldungVon(int employeeId, DateTime? austritt, DateTime? gekuendigtPer, IEnumerable<Vertrag> vertraegeDesMa)
    {
        if (austritt == null || gekuendigtPer == null) return null;
        var letzter = vertraegeDesMa.Where(v => v.EmployeeId == employeeId && v.CompanyProfileId != null)
            .OrderByDescending(v => v.Ende == null)
            .ThenByDescending(v => v.Ende)
            .ThenByDescending(v => v.Start)
            .FirstOrDefault();
        return letzter == null ? null
            : new Ereignis(employeeId, letzter.CompanyProfileId!.Value, Abmeldung, DateOnly.FromDateTime(austritt.Value));
    }

    /// <summary>
    /// Abmeldung, weil die Beschäftigung in einer anderen Filiale weitergeht (Walter 30.10.2026).
    /// Verträge derselben Filiale, die lückenlos anschliessen, sind ein Abschnitt. Er ist zu Ende,
    /// wenn am Folgetag dort nichts mehr läuft und die nächste Beschäftigung in einer anderen
    /// Filiale liegt — auch mit Lücke dazwischen. Rückkehr in dieselbe Filiale ist keine Abmeldung.
    /// Datum ist der letzte Vertragstag des Abschnitts.
    /// </summary>
    public static List<Ereignis> AbmeldungenBeiFilialwechsel(IEnumerable<Vertrag> vertraege)
    {
        var alle = vertraege.Where(v => v.CompanyProfileId != null).ToList();
        var res = new List<Ereignis>();
        foreach (var ma in alle.GroupBy(v => v.EmployeeId))
        {
            var desMa = ma.ToList();
            foreach (var fil in desMa.GroupBy(v => v.CompanyProfileId!.Value))
            {
                foreach (var abschnitt in Abschnitte(fil))
                {
                    if (abschnitt.Ende == null) continue;
                    var ende = abschnitt.Ende.Value.Date;
                    if (!AndereFilialeZuerst(desMa, fil.Key, ende)) continue;
                    res.Add(new Ereignis(ma.Key, fil.Key, Abmeldung, DateOnly.FromDateTime(ende)));
                }
            }
        }
        return res;
    }

    /// <summary>
    /// Filialwechsel und Firmenaustritt zusammen, dieselbe Person/Filiale/Datum nur einmal.
    /// </summary>
    public static List<Ereignis> Abmeldungen(IEnumerable<Vertrag> vertraege, IEnumerable<PersonAustritt> austritte)
    {
        var liste = vertraege as IList<Vertrag> ?? vertraege.ToList();
        var res = AbmeldungenBeiFilialwechsel(liste);
        var gesehen = res.Select(e => (e.EmployeeId, e.CompanyProfileId, e.Datum)).ToHashSet();
        var proMa = liste.Where(v => v.CompanyProfileId != null).ToLookup(v => v.EmployeeId);
        foreach (var a in austritte)
        {
            var ab = AbmeldungVon(a.EmployeeId, a.Datum, a.GekuendigtPer, proMa[a.EmployeeId]);
            if (ab == null || !gesehen.Add((ab.EmployeeId, ab.CompanyProfileId, ab.Datum))) continue;
            res.Add(ab);
        }
        return res;
    }

    private readonly record struct Abschnitt(DateTime Start, DateTime? Ende);

    /// <summary>Lückenlose Verträge einer Filiale zu Abschnitten zusammenziehen.</summary>
    private static List<Abschnitt> Abschnitte(IEnumerable<Vertrag> einerFiliale)
    {
        var sorted = einerFiliale
            .OrderBy(v => v.Start.Date)
            .ThenBy(v => v.Ende == null ? DateTime.MaxValue : v.Ende.Value.Date)
            .ToList();
        var res = new List<Abschnitt>();
        foreach (var c in sorted)
        {
            if (res.Count > 0)
            {
                var last = res[^1];
                if (last.Ende == null || c.Start.Date <= last.Ende.Value.Date.AddDays(1))
                {
                    DateTime? ende = last.Ende == null || c.Ende == null
                        ? null
                        : (c.Ende.Value.Date > last.Ende.Value.Date ? c.Ende : last.Ende);
                    res[^1] = new Abschnitt(last.Start, ende);
                    continue;
                }
            }
            res.Add(new Abschnitt(c.Start, c.Ende));
        }
        return res;
    }

    /// <summary>
    /// Die nächste Beschäftigung nach <paramref name="ende"/> liegt in einer anderen Filiale.
    /// Am Folgetag zählt ein noch laufender Vertrag dort; sonst der früheste spätere Beginn.
    /// </summary>
    private static bool AndereFilialeZuerst(List<Vertrag> desMa, int filiale, DateTime ende)
    {
        var folgetag = ende.AddDays(1);
        DateTime? naechster = null;
        foreach (var v in desMa)
        {
            if (v.Ende != null && v.Ende.Value.Date <= ende) continue;
            if (v.Start.Date <= folgetag && (v.Ende == null || v.Ende.Value.Date >= folgetag))
            {
                naechster = folgetag;
                break;
            }
            if (v.Start.Date > ende && (naechster == null || v.Start.Date < naechster.Value))
                naechster = v.Start.Date;
        }
        if (naechster == null) return false;
        var tag = naechster.Value.Date;
        bool gleiche = desMa.Any(v => v.CompanyProfileId == filiale
            && v.Start.Date <= tag && (v.Ende == null || v.Ende.Value.Date >= tag));
        if (gleiche) return false;
        return desMa.Any(v => v.CompanyProfileId != filiale
            && v.Start.Date <= tag && (v.Ende == null || v.Ende.Value.Date >= tag));
    }

    /// <summary>Die Meldung, die dieses Ereignis abdeckt (nächstgelegener Stichtag), sonst null.</summary>
    public static Meldung? Passende(Ereignis e, IEnumerable<Meldung> meldungen) =>
        meldungen.Where(m => m.EmployeeId == e.EmployeeId && m.CompanyProfileId == e.CompanyProfileId && m.Art == e.Art
                          && Math.Abs(m.Stichtag.DayNumber - e.Datum.DayNumber) <= ToleranzTage)
                 .OrderBy(m => Math.Abs(m.Stichtag.DayNumber - e.Datum.DayNumber))
                 .FirstOrDefault();

    /// <summary>
    /// Was eine Zeile der GastroSocial-Liste «Mitarbeitende» bestätigt (Altbestand übernehmen):
    /// «Beschäftigt» ⇒ die Anmeldungen bis heute (bzw. die erste, falls der Eintritt noch kommt);
    /// «Ausgetreten» per S ⇒ die Anmeldungen bis S und die Abmeldung um S herum.
    /// </summary>
    public static List<Ereignis> BestaetigtDurchListe(bool beschaeftigt, DateTime? statusaenderung,
        IReadOnlyList<Ereignis> ereignisseDesMaInFiliale, DateOnly heute)
    {
        var an = ereignisseDesMaInFiliale.Where(e => e.Art == Anmeldung).OrderBy(e => e.Datum).ToList();
        var ab = ereignisseDesMaInFiliale.Where(e => e.Art == Abmeldung).ToList();
        if (beschaeftigt)
        {
            var bisHeute = an.Where(e => e.Datum <= heute).ToList();
            return bisHeute.Count > 0 ? bisHeute : an.Take(1).ToList();
        }
        var s = statusaenderung != null ? DateOnly.FromDateTime(statusaenderung.Value) : heute;
        var res = an.Where(e => e.Datum <= s).ToList();
        res.AddRange(ab.Where(e => statusaenderung == null
                                || Math.Abs(e.Datum.DayNumber - s.DayNumber) <= ToleranzTage));
        return res;
    }

    /// <summary>
    /// Fehlende Meldungen für das To-do. Anmeldung ab dem Eintrittstag.
    /// Abmeldung erst, wenn der Austritt vorbei ist: <paramref name="abmeldungNachTagen"/>
    /// Tage danach (1 = ab dem Folgetag; 3 = Austritt 31.10., Warnung ab 3.11.).
    /// </summary>
    public static List<Ereignis> Offen(IEnumerable<Ereignis> ereignisse, IReadOnlyList<Meldung> meldungen, DateOnly heute,
        int abmeldungNachTagen = 1) =>
        ereignisse.Where(e => e.Datum >= ToDoAb && Passende(e, meldungen) == null && Faellig(e, heute, abmeldungNachTagen))
                  .OrderBy(e => e.Datum).ToList();

    /// <summary>Ab wann eine fehlende Meldung in die Warnung darf.</summary>
    public static bool Faellig(Ereignis e, DateOnly heute, int abmeldungNachTagen)
    {
        if (e.Art != Abmeldung) return e.Datum <= heute;
        var nach = abmeldungNachTagen < 1 ? 1 : abmeldungNachTagen;
        return heute >= e.Datum.AddDays(nach);
    }

    /// <summary>
    /// Vorlauf der Warnung «Abmeldung fehlt». Minus heisst Tage nach dem Austritt
    /// (−3 → Austritt 31.10., Warnung ab 3.11.). Leer oder positiv = ab dem Folgetag.
    /// </summary>
    public static int AbmeldungNachTagen(int? warnDays) =>
        warnDays is < 0 ? -warnDays.Value : 1;

    /// <summary>Text neben der AHV-Nummer. Erledigt = der geschriebene Vermerk deckt das Ereignis.</summary>
    public sealed record Anzeige(string Text, bool Erledigt);

    /// <summary>
    /// Status für die MA-Maske, dieselbe Regel wie AKIS und To-do.
    /// Aktiv (Austritt leer oder noch nicht vorbei): letzte Anmeldung der Filiale, in der heute ein Vertrag läuft.
    /// Ausgetreten: Abmeldung, sobald Austritt und «Gekündigt per» gesetzt sind. Ohne Kündigung per
    /// gibt es keine Abmeldung (befristeter Vertrag) und deshalb auch keinen Text.
    /// </summary>
    public static Anzeige? AnzeigeFuerMaske(
        int employeeId, DateTime? austritt, DateTime? gekuendigtPer,
        IEnumerable<Vertrag> vertraege, IEnumerable<Meldung> meldungen, DateOnly heute)
    {
        var liste = vertraege.Where(v => v.EmployeeId == employeeId).ToList();
        bool ausgetreten = austritt != null && DateOnly.FromDateTime(austritt.Value) < heute;
        if (ausgetreten)
        {
            var ab = AbmeldungVon(employeeId, austritt, gekuendigtPer, liste);
            if (ab == null) return null;
            bool weg = Passende(ab, meldungen) != null;
            return new Anzeige(weg ? "abgemeldet" : "Abmeldung fehlt", weg);
        }

        var heuteDt = heute.ToDateTime(TimeOnly.MinValue);
        var laufend = liste
            .Where(v => v.CompanyProfileId != null && v.Start.Date <= heuteDt && (v.Ende == null || v.Ende.Value.Date >= heuteDt))
            .OrderByDescending(v => v.Start)
            .FirstOrDefault()
            ?? liste.Where(v => v.CompanyProfileId != null).OrderByDescending(v => v.Start).FirstOrDefault();
        if (laufend == null) return null;

        var an = Anmeldungen(liste)
            .Where(e => e.CompanyProfileId == laufend.CompanyProfileId)
            .OrderByDescending(e => e.Datum)
            .ToList();
        var massgebend = an.FirstOrDefault(e => e.Datum <= heute) ?? an.FirstOrDefault();
        if (massgebend == null) return null;
        bool da = Passende(massgebend, meldungen) != null;
        return new Anzeige(da ? "angemeldet" : "Anmeldung fehlt", da);
    }
}
