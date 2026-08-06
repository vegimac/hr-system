using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter-Bug 06.08.2026: Nach dem Confirm steht das Uniformen-Depot auf
/// ZURUECKBEZAHLT. Wurde der Snapshot der Refund-Periode danach neu gerechnet
/// (Snapshot-Recompute nach 10.65%-Umstellung), lieferte GetPendingRefundAsync
/// nichts mehr (Status != EINBEHALTEN) — die Refund-Zeile verschwand still aus
/// dem Slip, der Status blieb aber grün «bereits zurückbezahlt».
/// Regel: in der RefundPeriode selbst muss der Refund reproduzierbar bleiben.
/// </summary>
public class UniformDepotRefundReproTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("UniformDepot_" + testName + "_" + Guid.NewGuid()).Options);

    private static readonly DateOnly Jul1  = new(2026, 7, 1);
    private static readonly DateOnly Jul31 = new(2026, 7, 31);
    private static readonly DateOnly Aug1  = new(2026, 8, 1);
    private static readonly DateOnly Aug31 = new(2026, 8, 31);

    [Fact]
    public async Task ZurueckbezahltInGleicherPeriode_RefundReproduzierbar()
    {
        using var db = NewDb();
        db.EmployeeUniformDepots.Add(new EmployeeUniformDepot
        {
            EmployeeId = 1, Balance = 0, Status = "ZURUECKBEZAHLT",
            RefundPeriode = "2026-07", ReturnConfirmed = true,
        });
        await db.SaveChangesAsync();

        var svc = new UniformDepotService(db);
        var (refund, amount, label) = await svc.GetPendingRefundAsync(1, Jul1, Jul31);

        Assert.True(refund);
        Assert.Equal(UniformDepotService.DepotBetrag, amount);
        Assert.Equal("Uniformen-Depot Rückerstattung", label);
    }

    [Fact]
    public async Task ZurueckbezahltInAndererPeriode_KeinDoppelRefund()
    {
        using var db = NewDb();
        db.EmployeeUniformDepots.Add(new EmployeeUniformDepot
        {
            EmployeeId = 1, Balance = 0, Status = "ZURUECKBEZAHLT",
            RefundPeriode = "2026-07", ReturnConfirmed = true,
        });
        await db.SaveChangesAsync();

        var svc = new UniformDepotService(db);
        var (refund, _, _) = await svc.GetPendingRefundAsync(1, Aug1, Aug31);

        Assert.False(refund); // August darf den Juli-Refund NICHT wiederholen
    }

    [Fact]
    public async Task Einbehalten_MitBestaetigungUndAustritt_RefundKommt()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee
        {
            Id = 1, FirstName = "Test", LastName = "MA",
            ExitDate = new DateTime(2026, 7, 31),
        });
        db.EmployeeUniformDepots.Add(new EmployeeUniformDepot
        {
            EmployeeId = 1, Balance = 50m, Status = "EINBEHALTEN",
            ReturnConfirmed = true,
        });
        await db.SaveChangesAsync();

        var svc = new UniformDepotService(db);
        var (refund, amount, _) = await svc.GetPendingRefundAsync(1, Jul1, Jul31);

        Assert.True(refund);
        Assert.Equal(50m, amount);
    }

    [Fact]
    public async Task Einbehalten_OhneBestaetigung_KeinRefund()
    {
        using var db = NewDb();
        db.Employees.Add(new Employee
        {
            Id = 1, FirstName = "Test", LastName = "MA",
            ExitDate = new DateTime(2026, 7, 31),
        });
        db.EmployeeUniformDepots.Add(new EmployeeUniformDepot
        {
            EmployeeId = 1, Balance = 50m, Status = "EINBEHALTEN",
            ReturnConfirmed = null,
        });
        await db.SaveChangesAsync();

        var svc = new UniformDepotService(db);
        var (refund, _, _) = await svc.GetPendingRefundAsync(1, Jul1, Jul31);

        Assert.False(refund);
    }

    [Fact]
    public async Task Verfallen_KeinRefund()
    {
        using var db = NewDb();
        db.EmployeeUniformDepots.Add(new EmployeeUniformDepot
        {
            EmployeeId = 1, Balance = 0, Status = "VERFALLEN",
        });
        await db.SaveChangesAsync();

        var svc = new UniformDepotService(db);
        var (refund, _, _) = await svc.GetPendingRefundAsync(1, Jul1, Jul31);

        Assert.False(refund);
    }
}
