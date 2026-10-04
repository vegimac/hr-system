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
    public async Task Vertrag_beginnt_nach_Stichtag_kein_Tagessatz()
    {
        var (db, empId, cpId) = await SeedAsync();
        using var _ = db;
        var svc = new KtgTagessatzService(db, NullLogger<KtgTagessatzService>.Instance);

        Assert.Null(await svc.CalculateAsync(empId, cpId, new DateOnly(2026, 8, 31)));
    }
}
