using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>Zivilstand-Historie nachführen und zum Stichtag auflösen (Walter 04.09.2026).</summary>
public class ZivilstandHistorieService
{
    private readonly AppDbContext _db;
    public ZivilstandHistorieService(AppDbContext db) => _db = db;

    public static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant().Replace(' ', '_');

    /// <summary>
    /// Zivilstand hat gewechselt (alt → neu): neuen Eintrag ab «seit»
    /// (MaritalStatusSince, sonst heute). Ohne Historie wird der alte Stand
    /// als «seit jeher» vorangestellt. Kein SaveChanges — der Aufrufer speichert.
    /// </summary>
    public async Task NachfuehrenAsync(int employeeId, string? alt, string? neu, DateOnly? seit, string quelle)
    {
        var neuN = Norm(neu);
        if (neuN.Length == 0) return;
        var altN = Norm(alt);
        var hist = await _db.EmployeeZivilstandHistories
            .Where(h => h.EmployeeId == employeeId)
            .OrderBy(h => h.GueltigAb == null ? 0 : 1).ThenBy(h => h.GueltigAb).ThenBy(h => h.Id)
            .ToListAsync();
        var letzter = hist.LastOrDefault();
        if (letzter != null && Norm(letzter.Zivilstand) == neuN) return;   // schon so
        if (hist.Count == 0 && altN.Length > 0 && altN != neuN)
            _db.EmployeeZivilstandHistories.Add(new EmployeeZivilstandHistory
            {
                EmployeeId = employeeId, Zivilstand = altN, GueltigAb = null,
                Bemerkung = "Bestandsstand (automatisch beim Zivilstand-Wechsel)",
            });
        var ab = seit ?? DateOnly.FromDateTime(DateTime.Today);
        // Gleicher Tag wie der letzte Eintrag → ersetzen statt Dublette
        var gleich = hist.FirstOrDefault(h => h.GueltigAb == ab);
        if (gleich != null) { gleich.Zivilstand = neuN; gleich.Bemerkung = quelle; return; }
        _db.EmployeeZivilstandHistories.Add(new EmployeeZivilstandHistory
        {
            EmployeeId = employeeId, Zivilstand = neuN, GueltigAb = ab, Bemerkung = quelle,
        });
    }

    /// <summary>Zivilstand am Stichtag: Historie (letzter Eintrag mit GueltigAb ≤ Stichtag), sonst aktueller MA-Stand.</summary>
    public async Task<(string? Zivilstand, DateOnly? Seit, bool AusHistorie)> AmAsync(int employeeId, DateOnly stichtag)
    {
        var hist = await _db.EmployeeZivilstandHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId)
            .OrderBy(h => h.GueltigAb == null ? 0 : 1).ThenBy(h => h.GueltigAb).ThenBy(h => h.Id)
            .ToListAsync();
        EmployeeZivilstandHistory? treffer = null;
        foreach (var h in hist) if (h.GueltigAb == null || h.GueltigAb <= stichtag) treffer = h;
        // Stichtag vor dem ältesten datierten Eintrag → ältester bekannter Stand
        if (treffer == null && hist.Count > 0) treffer = hist[0];
        if (treffer != null) return (treffer.Zivilstand, treffer.GueltigAb, true);
        var e = await _db.Employees.AsNoTracking().Where(x => x.Id == employeeId)
            .Select(x => new { x.MaritalStatus, x.MaritalStatusSince }).FirstOrDefaultAsync();
        return (e?.MaritalStatus, e?.MaritalStatusSince, false);
    }
}
