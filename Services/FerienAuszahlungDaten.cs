using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>Datenzugriff für «Ferien auszahlen» (Lohn-Engine und Eingabemaske).</summary>
public static class FerienAuszahlungDaten
{
    /// <summary>
    /// Ferien-Saldo per 31.12. Vorjahr: jüngster Saldo des Vorjahres, sonst
    /// Saldo-Vortrag im Januar. Null = unbekannt. (Simulation: Vortrag, siehe Engine.)
    /// </summary>
    public static async Task<FerienAuszahlungRechnung.Vorjahr?> SaldoVorjahrAsync(AppDbContext db, int employeeId, int year)
    {
        var dez = await db.PayrollSaldos.AsNoTracking()
            .Where(s => s.EmployeeId == employeeId && s.PeriodYear == year - 1)
            .OrderByDescending(s => s.PeriodMonth).ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync();
        if (dez != null) return new(dez.FerienTageSaldo, dez.FerienGeldSaldo);
        var januar = $"{year}-01";
        var vortrag = await db.LohnZulagen.AsNoTracking()
            .Where(z => z.EmployeeId == employeeId && z.Periode == januar
                     && z.Lohnposition!.Kategorie == "Saldo-Vortrag"
                     && (z.Lohnposition.Code == "903" || z.Lohnposition.Code == "905"))
            .Select(z => new { z.Lohnposition!.Code, z.Betrag })
            .ToListAsync();
        if (vortrag.Count == 0) return null;
        return new(vortrag.Where(v => v.Code == "903").Sum(v => v.Betrag),
                   vortrag.Where(v => v.Code == "905").Sum(v => v.Betrag));
    }
}
