namespace HrSystem.Models;

/// <summary>
/// Vertragsarten, die eine Filiale anbieten kann (Walter 27.10.2026).
/// Die vier heutigen Modelle und die Swissdec-Arten sind dieselbe Liste.
/// Ausgeschaltet wird nur die Auswahl. Die Rechnung bleibt FLEX, MTP, FIX oder FIX-M.
/// Leer in der Filiale = die vier Modelle, die übrigen aus.
/// </summary>
public static class VertragsartKatalog
{
    public sealed record Art(
        string Code,
        string Bezeichnung,
        string Modell,
        string? SwissdecArt,
        bool Befristet,
        bool Standard);

    public static readonly Art[] Alle =
    [
        new("FLEX", "FLEX – Stundenlohn", "FLEX", null, false, true),
        new("MTP", "MTP – garantierte Stunden", "MTP", null, false, true),
        new("FIX", "FIX – Festpensum", "FIX", null, false, true),
        new("FIX-M", "FIX-M – Management", "FIX-M", null, false, true),

        new("indefiniteSalaryMth", "Unbefristeter Vertrag mit Monatslohn", "FIX", "indefiniteSalaryMth", false, false),
        new("indefiniteSalaryMthAWT", "Unbefristeter Vertrag mit Monatslohn und Jahresarbeitszeit", "FIX", "indefiniteSalaryMthAWT", false, false),
        new("fixedSalaryMth", "Befristeter Vertrag mit Monatslohn", "FIX", "fixedSalaryMth", true, false),
        new("apprentice", "Lehrvertrag", "FIX", "apprentice", false, false),
        new("internshipContract", "Praktikumsvertrag", "FIX", "internshipContract", false, false),
        new("indefiniteSalaryHrs", "Unbefristeter Vertrag mit Stundenlohn", "FLEX", "indefiniteSalaryHrs", false, false),
        new("fixedSalaryHrs", "Befristeter Vertrag mit Stundenlohn", "FLEX", "fixedSalaryHrs", true, false),
        new("indefiniteSalaryNoTimeConstraint", "Unbefristeter Vertrag mit Provision, Pauschale, Akkordlohn", "FIX", "indefiniteSalaryNoTimeConstraint", false, false),
        new("fixedSalaryNoTimeConstraint", "Befristeter Vertrag mit Provision, Pauschale, Akkordlohn", "FIX", "fixedSalaryNoTimeConstraint", true, false),
        new("administrativeBoard", "Verwaltungsrat", "FIX", "administrativeBoard", false, false),
    ];

    public static IReadOnlyList<string> StandardCodes { get; } =
        Alle.Where(a => a.Standard).Select(a => a.Code).ToArray();

    /// <summary>null = die vier Standardarten. Gespeicherter Text = genau diese Codes.</summary>
    public static IReadOnlyList<string> Aufgeloest(string? gespeichert)
    {
        if (gespeichert == null) return StandardCodes;
        var gesetzt = new HashSet<string>(
            gespeichert.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
        return Alle.Where(a => gesetzt.Contains(a.Code)).Select(a => a.Code).ToArray();
    }

    public static bool Erlaubt(string? gespeichert, string code) =>
        Aufgeloest(gespeichert).Any(c => c == code);

    public static Art? Find(string? code) =>
        Alle.FirstOrDefault(a => string.Equals(a.Code, (code ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Welche Katalogzeile ein Vertrag ist. Eine gesetzte Swissdec-Art gewinnt,
    /// sonst das Modell. «UTP» zählt als FLEX.
    /// </summary>
    public static string CodeVon(string? modell, string? swissdecArt)
    {
        var art = (swissdecArt ?? "").Trim();
        if (art.Length > 0)
        {
            var hit = Alle.FirstOrDefault(a =>
                string.Equals(a.SwissdecArt, art, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.Code, art, StringComparison.OrdinalIgnoreCase));
            if (hit != null) return hit.Code;
        }
        var m = (modell ?? "").Trim();
        if (m.Equals("UTP", StringComparison.OrdinalIgnoreCase)) m = "FLEX";
        var modellHit = Alle.FirstOrDefault(a =>
            a.SwissdecArt == null && string.Equals(a.Code, m, StringComparison.OrdinalIgnoreCase));
        return modellHit?.Code ?? m;
    }

    /// <summary>Kanonische Speicherung, Reihenfolge des Katalogs. Unbekannte Codes fallen weg.</summary>
    public static (string Gespeichert, IReadOnlyList<string> Unbekannt) Speichern(IEnumerable<string>? codes)
    {
        var gesetzt = new HashSet<string>(
            (codes ?? []).Select(c => (c ?? "").Trim()).Where(c => c.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var unbekannt = gesetzt
            .Where(c => Alle.All(a => !string.Equals(a.Code, c, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var geordnet = Alle.Where(a => gesetzt.Contains(a.Code)).Select(a => a.Code).ToArray();
        return (string.Join(",", geordnet), unbekannt);
    }
}
