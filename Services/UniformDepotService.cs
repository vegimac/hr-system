using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Uniformen-Depot CHF 50 (Walter Aug 2026).
/// Lohnart 600.32 = Fibu «Kleiderdepot/Uniform» (1920/2021).
/// </summary>
public class UniformDepotService
{
    public const string LohnpositionCode = "600.32";
    public const decimal DepotBetrag = 50m;
    public static readonly DateOnly BackfillEntryBefore = new(2026, 7, 1);

    private readonly AppDbContext _db;

    public UniformDepotService(AppDbContext db) => _db = db;

    /// <summary>
    /// Beim ersten Lohn: CHF 50 als ABZUG (LohnZulage) + Depot EINBEHALTEN.
    /// Idempotent. Backfill-Depots werden nicht nochmals belastet.
    /// </summary>
    public async Task EnsureChargeAsync(Employee employee, int year, int month)
    {
        if (employee is null || employee.IsPayrollExcluded) return;

        var existing = await _db.EmployeeUniformDepots
            .FirstOrDefaultAsync(d => d.EmployeeId == employee.Id);
        if (existing != null) return; // schon EINBEHALTEN / zurück / verfallen

        var lpId = await ResolveLpIdAsync();
        if (lpId is null) return;

        // Schon jemals belastet (manuell oder früherer Lauf)?
        bool alreadyCharged = await _db.LohnZulagen
            .AnyAsync(z => z.EmployeeId == employee.Id && z.LohnpositionId == lpId);
        if (alreadyCharged)
        {
            // Depot-Zeile nachziehen ohne zweiten Abzug
            _db.EmployeeUniformDepots.Add(new EmployeeUniformDepot
            {
                EmployeeId     = employee.Id,
                Balance        = DepotBetrag,
                Status         = "EINBEHALTEN",
                ChargedPeriode = "LEGACY",
                Bemerkung      = "Depot aus bestehendem Lohnabzug übernommen",
                CreatedAt      = DateTime.Now,
                UpdatedAt      = DateTime.Now,
            });
            await _db.SaveChangesAsync();
            return;
        }

        // Erster Lohn = noch kein nicht-stornierter Snapshot
        bool hadPayroll = await _db.PayrollSnapshots
            .AnyAsync(s => s.EmployeeId == employee.Id && s.Status != "STORNIERT");
        if (hadPayroll) return;

        var periode = $"{year:D4}-{month:D2}";
        _db.LohnZulagen.Add(new LohnZulage
        {
            EmployeeId     = employee.Id,
            LohnpositionId = lpId.Value,
            Periode        = periode,
            Betrag         = DepotBetrag,
            Bemerkung      = "1. Lohn — Pauschale Uniform",
            CreatedAt      = DateTime.Now,
            UpdatedAt      = DateTime.Now,
        });
        _db.EmployeeUniformDepots.Add(new EmployeeUniformDepot
        {
            EmployeeId     = employee.Id,
            Balance        = DepotBetrag,
            Status         = "EINBEHALTEN",
            ChargedPeriode = periode,
            Bemerkung      = "Automatisch beim 1. Lohn",
            CreatedAt      = DateTime.Now,
            UpdatedAt      = DateTime.Now,
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Rückerstattungs-Zeile für den Slip (noch ohne Status-Change).
    /// Nur wenn Depot EINBEHALTEN, Rückgabe bestätigt, Austritt in/vor Periode.
    /// </summary>
    public async Task<(bool refund, decimal amount, string? label)> GetPendingRefundAsync(
        int employeeId, DateOnly periodFrom, DateOnly periodTo)
    {
        var depot = await _db.EmployeeUniformDepots
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.EmployeeId == employeeId);
        if (depot is null || depot.Status != "EINBEHALTEN" || depot.Balance <= 0)
            return (false, 0, null);
        if (depot.ReturnConfirmed != true)
            return (false, 0, null);

        var emp = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.ExitDate)
            .FirstOrDefaultAsync();
        if (!emp.HasValue) return (false, 0, null);

        var exit = DateOnly.FromDateTime(emp.Value);
        // Refund ab Austrittsmonat (auch wenn Periode später = Korrektur)
        if (exit > periodTo) return (false, 0, null);

        return (true, depot.Balance, "Uniformen-Depot Rückerstattung");
    }

    /// <summary>
    /// Nach Confirm: Status setzen (Refund oder Verfall).
    /// </summary>
    public async Task ApplyAfterConfirmAsync(int employeeId, int year, int month)
    {
        var depot = await _db.EmployeeUniformDepots
            .FirstOrDefaultAsync(d => d.EmployeeId == employeeId);
        if (depot is null || depot.Status != "EINBEHALTEN") return;

        var periode = $"{year:D4}-{month:D2}";
        var emp = await _db.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId)
            .Select(e => e.ExitDate)
            .FirstOrDefaultAsync();
        if (!emp.HasValue) return;

        var exit = DateOnly.FromDateTime(emp.Value);
        var periodTo = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        if (exit > periodTo) return;

        if (depot.ReturnConfirmed == true && depot.Balance > 0)
        {
            depot.Balance       = 0;
            depot.Status        = "ZURUECKBEZAHLT";
            depot.RefundPeriode = periode;
            depot.UpdatedAt     = DateTime.Now;
            await _db.SaveChangesAsync();
        }
        else if (depot.ReturnConfirmed == false)
        {
            depot.Balance   = 0;
            depot.Status    = "VERFALLEN";
            depot.Bemerkung = string.IsNullOrWhiteSpace(depot.Bemerkung)
                ? "Verfallen — Uniform nicht zurückgegeben"
                : depot.Bemerkung;
            depot.UpdatedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Austritts-Entscheidung speichern (aus Terminate).
    /// </summary>
    public async Task SetReturnDecisionAsync(int employeeId, bool returned, int? userId)
    {
        var depot = await _db.EmployeeUniformDepots
            .FirstOrDefaultAsync(d => d.EmployeeId == employeeId);
        if (depot is null)
        {
            // Kein Depot → nichts zu entscheiden (MA ohne Einbehalt)
            return;
        }
        if (depot.Status != "EINBEHALTEN") return;

        depot.ReturnConfirmed   = returned;
        depot.ReturnConfirmedAt = DateTime.Now;
        depot.ReturnConfirmedBy = userId;
        depot.UpdatedAt         = DateTime.Now;
        if (!returned)
            depot.Bemerkung = "Austritt nicht ordentlich — Depot verfällt bei letzter Abrechnung";
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Einmaliger Backfill: Eintritt vor 01.07.2026 → Depot EINBEHALTEN CHF 50
    /// ohne Lohn-Abzug (historisch bereits abgezogen).
    /// </summary>
    public async Task<int> BackfillAsync()
    {
        var cutoff = BackfillEntryBefore.ToDateTime(TimeOnly.MinValue);
        var empIds = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && !e.IsPayrollExcluded
                     && e.EntryDate != null && e.EntryDate < cutoff
                     && (e.IsActive || e.ExitDate == null || e.ExitDate >= cutoff))
            .Select(e => e.Id)
            .ToListAsync();

        var existing = await _db.EmployeeUniformDepots.AsNoTracking()
            .Where(d => empIds.Contains(d.EmployeeId))
            .Select(d => d.EmployeeId)
            .ToListAsync();
        var have = existing.ToHashSet();

        int n = 0;
        foreach (var id in empIds)
        {
            if (have.Contains(id)) continue;
            _db.EmployeeUniformDepots.Add(new EmployeeUniformDepot
            {
                EmployeeId     = id,
                Balance        = DepotBetrag,
                Status         = "EINBEHALTEN",
                ChargedPeriode = "BACKFILL",
                Bemerkung      = "Backfill: Eintritt vor 01.07.2026",
                CreatedAt      = DateTime.Now,
                UpdatedAt      = DateTime.Now,
            });
            n++;
        }
        if (n > 0) await _db.SaveChangesAsync();
        return n;
    }

    public async Task<object?> GetDtoAsync(int employeeId)
    {
        var d = await _db.EmployeeUniformDepots.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmployeeId == employeeId);
        if (d is null) return null;
        return new
        {
            id = d.Id,
            employeeId = d.EmployeeId,
            balance = d.Balance,
            status = d.Status,
            chargedPeriode = d.ChargedPeriode,
            refundPeriode = d.RefundPeriode,
            returnConfirmed = d.ReturnConfirmed,
            bemerkung = d.Bemerkung,
        };
    }

    private async Task<int?> ResolveLpIdAsync()
        => await _db.Lohnpositionen
            .Where(l => l.Code == LohnpositionCode && l.IsActive)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync();
}
