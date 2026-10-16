using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Wohnort zum Stichtag aus der Historie (Walter 16.10.2026): ELM-Monatsmeldung und
/// Lohnbeleg brauchen den Stand im Meldemonat, nicht die heutige MA-Adresse.
/// </summary>
public class WohnortHistorieService
{
    private readonly AppDbContext _db;
    public WohnortHistorieService(AppDbContext db) => _db = db;

    /// <summary>Adresse am Stichtag (bestätigter Stand, ohne offene Sync-Einträge).</summary>
    public record Stand(string? Strasse, string? Plz, string? Ort, string? KantonCode, string? Land,
        DateOnly? GueltigAb, bool AusHistorie);

    /// <summary>
    /// Letzter bestätigter Eintrag mit Gültig-ab ≤ Stichtag, sonst heutiger MA-Stand.
    /// Land: Spalte, sonst CH bei Schweizer Kanton.
    /// </summary>
    public static Stand Am(IEnumerable<EmployeeWohnortHistory> hist, DateOnly stichtag,
        string? fallbackStrasse, string? fallbackPlz, string? fallbackOrt,
        string? fallbackKanton, string? fallbackLand)
    {
        var sortiert = hist.Where(h => !h.DatumOffen)
            .OrderBy(h => h.GueltigAb == null ? 0 : 1).ThenBy(h => h.GueltigAb).ThenBy(h => h.Id)
            .ToList();
        EmployeeWohnortHistory? treffer = null;
        foreach (var h in sortiert)
            if (h.GueltigAb == null || h.GueltigAb <= stichtag) treffer = h;
        if (treffer != null)
            return new Stand(treffer.Strasse, treffer.Plz, treffer.Ort, treffer.KantonCode,
                LandAus(treffer), treffer.GueltigAb, true);
        return new Stand(fallbackStrasse, fallbackPlz, fallbackOrt, fallbackKanton, fallbackLand, null, false);
    }

    public async Task<Stand> AmAsync(int employeeId, DateOnly stichtag)
    {
        var hist = await _db.EmployeeWohnortHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId)
            .ToListAsync();
        var e = await _db.Employees.AsNoTracking()
            .Where(x => x.Id == employeeId)
            .Select(x => new { x.Street, x.ZipCode, x.City, x.CantonCode, x.Country })
            .FirstOrDefaultAsync();
        return Am(hist, stichtag, e?.Street, e?.ZipCode, e?.City, e?.CantonCode, e?.Country);
    }

    /// <summary>ISO-Land aus Historie; bei Schweizer Kanton «CH», sonst gespeichertes Land.</summary>
    public static string? LandAus(EmployeeWohnortHistory h)
    {
        var roh = (h.Land ?? "").Trim();
        if (roh.Length > 0)
        {
            var u = roh.ToUpperInvariant();
            if (u is "CH" or "CHE" or "SCHWEIZ" or "SWITZERLAND") return "CH";
            return u.Length == 2 ? u : roh;
        }
        var kt = (h.KantonCode ?? "").Trim().ToUpperInvariant();
        if (kt.Length == 2 && kt != "EX") return "CH";
        return null;
    }
}
