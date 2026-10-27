namespace HrSystem.Services;

/// <summary>
/// Welcher Manager bei einem Arbeitszeit-Verstoss an der Schicht war.
/// Manager = FIX-M der Filiale (Restaurant- und Schichtführung).
/// Dabei = der Stempel des Managers überlappt eine Stempelzeit des Verstosses.
/// Grenzwerte zählen nicht: wer genau beim Beginn geht, war nicht mehr dabei.
/// </summary>
public static class ManagerSchicht
{
    public readonly record struct Fenster(DateTime Ein, DateTime Aus);
    public readonly record struct Schicht(int EmployeeId, DateTime Ein, DateTime Aus);

    public static List<int> Dabei(IReadOnlyList<Fenster> verstoss, IEnumerable<Schicht> schichten)
    {
        if (verstoss.Count == 0) return new List<int>();
        var ids = new HashSet<int>();
        foreach (var s in schichten)
        {
            if (verstoss.Any(f => s.Ein < f.Aus && s.Aus > f.Ein))
                ids.Add(s.EmployeeId);
        }
        return ids.ToList();
    }
}
