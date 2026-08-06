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
    /// Returns true wenn in dieser Periode neu ein Abzug angelegt wurde
    /// (Snapshot muss ggf. neu gerechnet werden — Feature kam oft NACH Confirm).
    /// </summary>
    public async Task<bool> EnsureChargeAsync(Employee employee, int year, int month)
    {
        if (employee is null || employee.IsPayrollExcluded) return false;

        var existing = await _db.EmployeeUniformDepots
            .FirstOrDefaultAsync(d => d.EmployeeId == employee.Id);
        if (existing != null) return false; // schon EINBEHALTEN / zurück / verfallen

        var lpId = await ResolveLpIdAsync();
        if (lpId is null) return false;

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
            return false;
        }

        // Eintritt vor 01.07.2026: historisch schon abgezogen → nur Depot-Zeile
        // (kein neuer Lohnabzug), analog BackfillAsync.
        if (employee.EntryDate.HasValue
            && DateOnly.FromDateTime(employee.EntryDate.Value) < BackfillEntryBefore)
        {
            _db.EmployeeUniformDepots.Add(new EmployeeUniformDepot
            {
                EmployeeId     = employee.Id,
                Balance        = DepotBetrag,
                Status         = "EINBEHALTEN",
                ChargedPeriode = "BACKFILL",
                Bemerkung      = "Backfill: Eintritt vor 01.07.2026",
                CreatedAt      = DateTime.Now,
                UpdatedAt      = DateTime.Now,
            });
            await _db.SaveChangesAsync();
            return false;
        }

        // Erster Lohn = kein Snapshot in einer FRÜHEREN Periode.
        // Snapshots der AKTUELLEN Periode dürfen nicht blockieren — das Depot-
        // Feature kam oft erst nach der Lohnbestätigung (Walter Aug 2026).
        bool hadEarlierPayroll = await (
            from s in _db.PayrollSnapshots
            join p in _db.PayrollPerioden on s.PayrollPeriodeId equals p.Id
            where s.EmployeeId == employee.Id
               && s.Status != "STORNIERT"
               && (p.Year < year || (p.Year == year && p.Month < month))
            select s.Id
        ).AnyAsync();
        if (hadEarlierPayroll) return false;

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
        return true;
    }

    /// <summary>
    /// Alle Eintritte / Erst-Löhne einer Filiale+Periode: Depot-Abzug nachziehen
    /// (auch wenn die Periode schon provisorisch bestätigt ist).
    /// </summary>
    public async Task<(int Charged, List<int> EmployeeIds)> EnsureChargesForPeriodAsync(
        int companyProfileId, int year, int month)
    {
        var periodFrom = new DateTime(year, month, 1);
        var periodTo   = periodFrom.AddMonths(1).AddDays(-1);

        var empIds = await _db.Employments.AsNoTracking()
            .Where(em => em.CompanyProfileId == companyProfileId
                      && em.ContractStartDate <= periodTo
                      && (em.ContractEndDate == null || em.ContractEndDate >= periodFrom))
            .Select(em => em.EmployeeId)
            .Distinct()
            .ToListAsync();

        var employees = await _db.Employees
            .Where(e => empIds.Contains(e.Id) && !e.IsHidden && !e.IsPayrollExcluded)
            .ToListAsync();

        var charged = new List<int>();
        foreach (var emp in employees)
        {
            if (await EnsureChargeAsync(emp, year, month))
                charged.Add(emp.Id);
        }
        return (charged.Count, charged);
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
        if (depot is null) return (false, 0, null);

        // Reproduzierbarkeit bei Neuberechnung (Walter-Bug 06.08.2026): nach dem
        // Confirm steht das Depot auf ZURUECKBEZAHLT — wird der Snapshot der
        // Refund-Periode danach neu gerechnet (SnapshotRecompute, wieder-öffnen),
        // muss die Refund-Zeile in DERSELBEN Periode wieder erscheinen, sonst
        // verschwindet die Rückerstattung still aus dem Slip.
        var refundPeriode = $"{periodTo.Year:D4}-{periodTo.Month:D2}";
        if (depot.Status == "ZURUECKBEZAHLT" && depot.RefundPeriode == refundPeriode)
            return (true, DepotBetrag, "Uniformen-Depot Rückerstattung");

        if (depot.Status != "EINBEHALTEN" || depot.Balance <= 0)
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
            // Korrekturlohn-Nachzug: historisch einbehaltenes Depot fehlt
            // (Backfill übersprang früher Ausgetretene) → bei «zurück» anlegen.
            if (!returned) return;
            depot = new EmployeeUniformDepot
            {
                EmployeeId     = employeeId,
                Balance        = DepotBetrag,
                Status         = "EINBEHALTEN",
                ChargedPeriode = "BACKFILL",
                Bemerkung      = "Nachträglich angelegt für Depot-Refund (Korrekturlohn)",
                CreatedAt      = DateTime.Now,
                UpdatedAt      = DateTime.Now,
            };
            _db.EmployeeUniformDepots.Add(depot);
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
        // Auch Ausgetretene — sonst fehlt das Depot beim Korrekturlohn (Qazimi).
        var empIds = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && !e.IsPayrollExcluded
                     && e.EntryDate != null && e.EntryDate < cutoff)
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
