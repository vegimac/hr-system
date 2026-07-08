using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die Employment-Zeile beim easy@work-MA-Import (Walter-Vorgabe
/// 21.06.2026). Geprüft wird der reine DB-Schreiber
/// <see cref="EasyAtWorkEmployeeSyncService.AddEmploymentIfMissingAsync"/>
/// (API-frei) sowie die Lohnlauf-Selektion.
///   • Inaktiver MA bekommt Employment mit is_active=false + Filiale.
///   • Default UTP, wenn easy@work kein Modell liefert.
///   • Filiale ist in (SQL-)Auswertungen sichtbar.
///   • Lohnlauf listet inaktiven MA trotzdem nicht.
///   • Re-Import (UPDATE-Backfill) erzeugt keine Dubletten.
/// </summary>
public class EasyAtWorkInactiveEmploymentTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("InactEmp_" + testName + "_" + System.Guid.NewGuid()).Options);

    private static async Task<Employee> AddInactiveEmployeeAsync(AppDbContext db, string number)
    {
        var e = new Employee
        {
            EmployeeNumber = number,
            FirstName = "Max",
            LastName  = "Muster",
            IsActive  = false,
            ExitDate  = new System.DateTime(2024, 6, 30),
        };
        db.Employees.Add(e);
        await db.SaveChangesAsync();
        return e;
    }

    [Fact]
    public async Task InaktiverMa_BekommtInaktivesEmployment()
    {
        using var db = NewDb();
        var emp = await AddInactiveEmployeeAsync(db, "750040");

        await EasyAtWorkEmployeeSyncService.AddEmploymentIfMissingAsync(
            db, emp, companyProfileId: 5, isNewEmployee: true,
            startDate: new System.DateTime(2021, 7, 21), endDate: new System.DateTime(2024, 6, 30), isActive: false,
            employmentModel: "FLEX", salaryType: "hourly", contractType: "Flex", jobTitle: null,
            weeklyHours: 17m, percentage: null, hourlyRate: 22m, monthlySalary: null);
        await db.SaveChangesAsync();

        var emps = await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync();
        var one = Assert.Single(emps);
        Assert.False(one.IsActive);
        Assert.Equal(5, one.CompanyProfileId);
        Assert.Equal(new System.DateTime(2024, 6, 30), one.ContractEndDate);
        Assert.Equal("FLEX", one.EmploymentModel);
    }

    [Fact]
    public async Task OhneModell_DefaultUTP()
    {
        using var db = NewDb();
        var emp = await AddInactiveEmployeeAsync(db, "750041");

        // easy@work liefert kein Modell/SalaryType → Default UTP / hourly.
        await EasyAtWorkEmployeeSyncService.AddEmploymentIfMissingAsync(
            db, emp, companyProfileId: 5, isNewEmployee: true,
            startDate: new System.DateTime(2021, 1, 1), endDate: new System.DateTime(2024, 6, 30), isActive: false,
            employmentModel: null, salaryType: null, contractType: null, jobTitle: null,
            weeklyHours: null, percentage: null, hourlyRate: null, monthlySalary: null);
        await db.SaveChangesAsync();

        var one = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal("FLEX", one.EmploymentModel);
        Assert.Equal("hourly", one.SalaryType);
    }

    [Fact]
    public async Task Filiale_IstInAuswertungSichtbar()
    {
        using var db = NewDb();
        var emp = await AddInactiveEmployeeAsync(db, "580003");

        await EasyAtWorkEmployeeSyncService.AddEmploymentIfMissingAsync(
            db, emp, companyProfileId: 7, isNewEmployee: true,
            startDate: new System.DateTime(2022, 1, 1), endDate: new System.DateTime(2024, 12, 31), isActive: false,
            employmentModel: "FIX", salaryType: "monthly", contractType: null, jobTitle: null,
            weeklyHours: null, percentage: 100m, hourlyRate: null, monthlySalary: 4200m);
        await db.SaveChangesAsync();

        var row = await db.Employees.Where(e => e.Id == emp.Id)
            .Select(e => new { e.EmployeeNumber, Branch = e.Employments.Select(x => x.CompanyProfileId).FirstOrDefault() })
            .SingleAsync();
        Assert.Equal(7, row.Branch);
    }

    [Fact]
    public async Task Lohnlauf_ListetInaktivenMaNicht()
    {
        using var db = NewDb();
        var emp = await AddInactiveEmployeeAsync(db, "750099");

        await EasyAtWorkEmployeeSyncService.AddEmploymentIfMissingAsync(
            db, emp, companyProfileId: 5, isNewEmployee: true,
            startDate: new System.DateTime(2021, 1, 1), endDate: new System.DateTime(2024, 6, 30), isActive: false,
            employmentModel: "FLEX", salaryType: "hourly", contractType: null, jobTitle: null,
            weeklyHours: null, percentage: null, hourlyRate: null, monthlySalary: null);
        await db.SaveChangesAsync();

        // Replik der LohnlaufService-Selektion für eine AKTUELLE Periode (Juni 2026).
        var periodFrom = new System.DateTime(2026, 6, 1);
        var periodTo   = new System.DateTime(2026, 6, 30);
        var liste = await db.Employees
            .Where(e => e.IsActive && !e.IsPayrollExcluded
                 && e.Employments.Any(em => em.CompanyProfileId == 5
                     && em.ContractStartDate <= periodTo
                     && (em.ContractEndDate == null || em.ContractEndDate >= periodFrom)))
            .ToListAsync();

        Assert.DoesNotContain(liste, e => e.Id == emp.Id);
    }

    [Fact]
    public async Task ReImport_ErzeugtKeineDubletten()
    {
        using var db = NewDb();
        var emp = await AddInactiveEmployeeAsync(db, "750040");

        // Re-Import = UPDATE-Pfad (isNewEmployee:false): erster Lauf legt an,
        // weitere Läufe finden das Employment und legen NICHTS nach.
        for (int i = 0; i < 3; i++)
        {
            await EasyAtWorkEmployeeSyncService.AddEmploymentIfMissingAsync(
                db, emp, companyProfileId: 5, isNewEmployee: false,
                startDate: new System.DateTime(2021, 7, 21), endDate: new System.DateTime(2024, 6, 30), isActive: false,
                employmentModel: "FLEX", salaryType: "hourly", contractType: null, jobTitle: null,
                weeklyHours: null, percentage: null, hourlyRate: 22m, monthlySalary: null);
            await db.SaveChangesAsync();
        }

        var emps = await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync();
        Assert.Single(emps);
    }
}
