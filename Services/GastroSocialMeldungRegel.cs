namespace HrSystem.Services;

/// <summary>
/// An-/Abmeldung bei der Ausgleichskasse GastroSocial (Walter 13.10.2026) — EINE Regel für
/// AKIS-Seite, AHV-Abgleich und To-do. Jede Filiale ist eine eigene GmbH mit eigenem Konto.
///
/// Anmeldung = Vertrag in der Filiale beginnt, und am Vortag lief dort kein anderer Vertrag
///             (nahtlose Vertragsanpassung = keine neue Anmeldung; Filialwechsel = Ab- + Anmeldung).
/// Abmeldung = erfasster Austritt (Employee.ExitDate); zuständig ist die Filiale des letzten Vertrags.
///             Ein blosses Ende eines befristeten Vertrags ist KEINE Abmeldung (Verlängerung oft nur
///             noch nicht erfasst).
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

    /// <summary>Abmelde-Ereignis eines MA: Austrittsdatum, Filiale des letzten Vertrags. Null = keins.</summary>
    public static Ereignis? AbmeldungVon(int employeeId, DateTime? austritt, IEnumerable<Vertrag> vertraegeDesMa)
    {
        if (austritt == null) return null;
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
}
