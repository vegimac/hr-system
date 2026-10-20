namespace HrSystem.Services;

/// <summary>
/// An-/Abmeldung bei der Ausgleichskasse GastroSocial (Walter 13.10.2026) — EINE Regel für
/// AKIS-Seite, AHV-Abgleich und To-do. Jede Filiale ist eine eigene GmbH mit eigenem Konto.
///
/// Anmeldung = Vertrag in der Filiale beginnt, und am Vortag lief dort kein anderer Vertrag
///             (nahtlose Vertragsanpassung = keine neue Anmeldung; Filialwechsel = Ab- + Anmeldung).
/// Abmeldung = erfasster Austritt (Employee.ExitDate) UND gesetztes «Gekündigt per».
///             Ein befristeter Vertrag trägt oft trotzdem ein Austrittsdatum — ohne Kündigung per
///             ist das keine Abmeldung. Zuständig ist die Filiale des letzten Vertrags.
///             Auf der AKIS-Liste erscheint die Zeile erst am Tag nach dem Austritt.
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
