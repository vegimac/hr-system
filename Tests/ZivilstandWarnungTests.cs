using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// To-do «Zivilstand fehlt» (Walter 04.09.2026, Reichweite erweitert 23.09.2026).
///
/// Ohne Zivilstand greift weder die Ehegatten-Befreiung noch der richtige
/// QST-Tarif (Fall Leonora Cana). Die erste Fassung meldete nur MA mit einem
/// HEUTE schon laufenden Vertrag, dessen <c>IsActive</c>-Flag zusätzlich stimmte —
/// beides zu eng: ein neu erfasster MA mit Eintritt nächsten Monat fiel durch,
/// und das IsActive-Flag ist im Altbestand unzuverlässig.
///
/// Geprüft wird: künftiger Eintritt meldet, laufender Vertrag mit falschem
/// IsActive-Flag meldet, Ausgetretene und Phantom-MA melden nicht.
/// </summary>
public class ZivilstandWarnungTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("Zivilstand_" + t + "_" + Guid.NewGuid()).Options);

    private static DashboardService Svc(AppDbContext db)
        => new(db, new QstPflichtCheckService(db), new SperrfristService(db));

    private static Employee Ma(int id, string? zivilstand, bool phantom = false)
        => new()
        {
            Id = id,
            FirstName = "Anastasija", LastName = "Test" + id,
            EmployeeNumber = "129000" + id,
            IsActive = true,
            IsHidden = false,
            IsPayrollExcluded = phantom,
            MaritalStatus = zivilstand,
        };

    private static Employment Vertrag(int id, int empId, DateTime von, DateTime? bis, bool istAktivFlag)
        => new()
        {
            Id = id,
            EmployeeId = empId,
            CompanyProfileId = 58,
            ContractStartDate = von,
            ContractEndDate   = bis,
            EmploymentModel   = "FLEX",
            IsActive          = istAktivFlag,
        };

    private static async Task<List<string>> ZivilstandAlertsAsync(AppDbContext db)
    {
        var data = await Svc(db).BuildAsync(null);
        return data.Alerts
            .Where(a => a.Category == "zivilstand_fehlt")
            .Select(a => a.Subtitle ?? "")
            .ToList();
    }

    [Fact]
    public async Task KuenftigerEintritt_OhneZivilstand_Meldet()
    {
        using var db = NewDb();
        db.Employees.Add(Ma(1, null));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddDays(20), null, true));
        await db.SaveChangesAsync();

        var alerts = await ZivilstandAlertsAsync(db);
        var einzeln = Assert.Single(alerts);
        Assert.Contains("Eintritt am " + DateTime.Today.AddDays(20).ToString("dd.MM.yyyy"), einzeln);
    }

    [Fact]
    public async Task LaufenderVertrag_MitFalschemIsActiveFlag_MeldetTrotzdem()
    {
        using var db = NewDb();
        db.Employees.Add(Ma(1, ""));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-2), null, istAktivFlag: false));
        await db.SaveChangesAsync();

        Assert.Single(await ZivilstandAlertsAsync(db));
    }

    [Fact]
    public async Task Ausgetreten_MeldetNicht()
    {
        using var db = NewDb();
        db.Employees.Add(Ma(1, null));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-3), DateTime.Today.AddDays(-40), true));
        await db.SaveChangesAsync();

        Assert.Empty(await ZivilstandAlertsAsync(db));
    }

    [Fact]
    public async Task PhantomMa_MeldetNicht()
    {
        using var db = NewDb();
        db.Employees.Add(Ma(1, null, phantom: true));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-1), null, true));
        await db.SaveChangesAsync();

        Assert.Empty(await ZivilstandAlertsAsync(db));
    }

    [Fact]
    public async Task ZivilstandErfasst_MeldetNicht()
    {
        using var db = NewDb();
        db.Employees.Add(Ma(1, "ledig"));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-1), null, true));
        await db.SaveChangesAsync();

        Assert.Empty(await ZivilstandAlertsAsync(db));
    }

    [Fact]
    public async Task EhepartnerErfasst_IstKritisch()
    {
        using var db = NewDb();
        db.Employees.Add(Ma(1, null));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-1), null, true));
        db.EmployeeFamilyMembers.Add(new EmployeeFamilyMember
        {
            Id = 1, EmployeeId = 1, MemberType = "Ehepartner", FirstName = "Ivan",
        });
        await db.SaveChangesAsync();

        var data = await Svc(db).BuildAsync(null);
        var alert = Assert.Single(data.Alerts.Where(a => a.Category == "zivilstand_fehlt"));
        Assert.Equal("critical", alert.Severity);
    }
}
