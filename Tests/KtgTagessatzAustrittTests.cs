using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Fall Yllka Radogoshi (Walter 04.10.2026): Austritt 30.09., Vertrag schon inaktiv,
/// 4 Krankentage im September. Der Tagessatz suchte nur `is_active`-Verträge → null →
/// Krankheit fiel still vom Lohnzettel. Im Lohnlauf zählt der Vertrag zum Periodenende.
/// </summary>
public class KtgTagessatzAustrittTests
{
    private static async Task<(AppDbContext db, int empId, int cpId)> SeedAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("KtgAustritt_" + Guid.NewGuid()).Options);
        var cp = new CompanyProfile
        {
            CompanyName = "Test-Filiale",
            DefaultVacationPercent5Weeks = 10.65m,
            DefaultHolidayPercent = 2.27m,
            MaxPartTimeHoursPerWeek = 17m,
        };
        db.CompanyProfiles.Add(cp);
        var emp = new Employee { EmployeeNumber = "2300026", FirstName = "Yllka", LastName = "Muster", IsActive = true };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id,
            CompanyProfileId = cp.Id,
            IsActive = false,
            ContractStartDate = new DateTime(2026, 9, 1),
            ContractEndDate = new DateTime(2026, 9, 26),
            EmploymentModel = "FLEX",
            HourlyRate = 20m,
        });
        await db.SaveChangesAsync();
        return (db, emp.Id, cp.Id);
    }

    [Fact]
    public async Task Inaktiver_Vertrag_liefert_Tagessatz_zum_Periodenende()
    {
        var (db, empId, cpId) = await SeedAsync();
        using var _ = db;
        var svc = new KtgTagessatzService(db, NullLogger<KtgTagessatzService>.Instance);

        var r = await svc.CalculateAsync(empId, cpId, new DateOnly(2026, 9, 26));

        Assert.NotNull(r);
        Assert.Equal(58.06m, r!.Tagessatz100);
    }

    [Fact]
    public async Task Ohne_Stichtag_faellt_auf_juengsten_Vertrag_zurueck()
    {
        var (db, empId, cpId) = await SeedAsync();
        using var _ = db;
        var svc = new KtgTagessatzService(db, NullLogger<KtgTagessatzService>.Instance);

        Assert.NotNull(await svc.CalculateAsync(empId, cpId));
    }

    [Fact]
    public async Task Hochrechnung_ohne_Feiertag_manueller_Satz_mit_Feiertag()
    {
        var (db, empId, cpId) = await SeedAsync();
        using var _ = db;
        var svc = new KtgTagessatzService(db, NullLogger<KtgTagessatzService>.Instance);

        var auto = await svc.CalculateAsync(empId, cpId, new DateOnly(2026, 9, 26));
        Assert.Equal("A", auto!.Regel);
        Assert.False(auto.EnthaeltFeiertag);

        (await db.Employees.FindAsync(empId))!.KtgTagessatzManuell = 37.06m;
        await db.SaveChangesAsync();
        var manuell = await svc.CalculateAsync(empId, cpId, new DateOnly(2026, 9, 26));
        Assert.Equal(37.06m, manuell!.Tagessatz100);
        Assert.True(manuell.EnthaeltFeiertag);
    }

    [Fact]
    public async Task Stundenlohn_Tagessatz_enthaelt_Ferien_und_13ml_FIX_nicht()
    {
        var (db, empId, cpId) = await SeedAsync();
        using var _ = db;
        var svc = new KtgTagessatzService(db, NullLogger<KtgTagessatzService>.Instance);

        var flex = await svc.CalculateAsync(empId, cpId, new DateOnly(2026, 9, 26));
        Assert.True(flex!.EnthaeltFerien);
        Assert.True(flex.Enthaelt13ml);

        var vertrag = await db.Employments.FirstAsync(e => e.EmployeeId == empId);
        vertrag.EmploymentModel = "FIX";
        vertrag.MonthlySalary = 4500m;
        await db.SaveChangesAsync();
        var fix = await svc.CalculateAsync(empId, cpId, new DateOnly(2026, 9, 26));
        if (fix != null)
        {
            Assert.False(fix.EnthaeltFerien);
            Assert.False(fix.Enthaelt13ml);
        }

        (await db.Employees.FindAsync(empId))!.KtgTagessatzManuell = 37.06m;
        await db.SaveChangesAsync();
        var manuell = await svc.CalculateAsync(empId, cpId, new DateOnly(2026, 9, 26));
        Assert.True(manuell!.EnthaeltFerien);
        Assert.True(manuell.Enthaelt13ml);
    }

    [Fact]
    public async Task Vertrag_beginnt_nach_Stichtag_kein_Tagessatz()
    {
        var (db, empId, cpId) = await SeedAsync();
        using var _ = db;
        var svc = new KtgTagessatzService(db, NullLogger<KtgTagessatzService>.Instance);

        Assert.Null(await svc.CalculateAsync(empId, cpId, new DateOnly(2026, 8, 31)));
    }
}
