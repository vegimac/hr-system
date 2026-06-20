using System.Net.Http;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die abgesicherte easy@work-Stempelzeit-Synchronisation
/// (Walter-Vorgabe 19.06.2026):
///   - manueller Commit nutzt denselben lock-gegateten Schreibpfad
///     (<see cref="EasyAtWorkTimepunchSyncService.ApplyTimepunchesAsync"/>),
///   - Preflight ignoriert gesperrte (nicht editierbare) Stempel,
///   - Orchestrator setzt den Sync-State bei Success/Block/Error korrekt,
///   - Catch-up-Entscheidung nach Neustart.
///
/// Schreibwege werden mit EF-InMemory + einem Fake-Client (kein HTTP) geprüft.
/// </summary>
public class EasyAtWorkWritePathTests
{
    // ─────────────────────── Test-Infrastruktur ─────────────────────────

    private sealed class FakeEawClient : EasyAtWorkClient
    {
        public List<EawEmployee>  Employees   { get; } = new();
        public List<EawTimepunch> Timepunches { get; } = new();
        public bool ThrowOnFetch { get; set; }

        public FakeEawClient() : base(
            new HttpClient(),
            new EasyAtWorkSettings { BaseUrl = "x", ClientId = "x", ClientSecret = "x" },
            NullLogger<EasyAtWorkClient>.Instance) { }

        public override Task<List<EawEmployee>> GetAllEmployeesIncludingInactiveAsync(int c, CancellationToken ct = default)
            => Task.FromResult(Employees);
        public override Task<List<EawTimepunch>> GetAllTimepunchesAsync(int c, DateOnly f, DateOnly t, CancellationToken ct = default)
            => ThrowOnFetch ? throw new HttpRequestException("boom") : Task.FromResult(Timepunches);
        public override Task<List<EawTimepunch>> GetAllTimepunchUpdatesAsync(int c, DateTime ls, CancellationToken ct = default)
            => ThrowOnFetch ? throw new HttpRequestException("boom") : Task.FromResult(Timepunches);
    }

    private static AppDbContext NewDb(string name)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(name).Options);

    private static EasyAtWorkTimepunchSyncService NewService(AppDbContext db, EasyAtWorkClient client)
        => new(db, client, NullLogger<EasyAtWorkTimepunchSyncService>.Instance, new LohnEditLockService(db));

    private static EawTimepunch Punch(int id, int empId, DateOnly date) => new()
    {
        Id = id, EmployeeId = empId, BusinessDate = date,
        In  = new DateTime(date.Year, date.Month, date.Day, 8, 0, 0, DateTimeKind.Utc),
        Out = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0, DateTimeKind.Utc),
    };

    // ─────────────── Manueller Commit: Lock + Schreibpfad ────────────────

    [Fact]
    public async Task ManualCommit_SkipsLockedPunches_AndSetsSyncState()
    {
        var db = NewDb(nameof(ManualCommit_SkipsLockedPunches_AndSetsSyncState));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "580099", FirstName = "A", LastName = "M" });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        // Januar 2026 ABGESCHLOSSEN → Stempel im Januar sind gesperrt (per-Periode).
        db.PayrollPerioden.Add(new PayrollPeriode { CompanyProfileId = 10, Year = 2026, Month = 1,
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31), Status = "abgeschlossen" });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 47, Number = "580099", FirstName = "A", LastName = "M" });
        client.Timepunches.Add(Punch(100, 47, new DateOnly(2026, 2, 20)));  // editierbar (Feb offen)
        client.Timepunches.Add(Punch(101, 47, new DateOnly(2026, 1, 31)));  // gesperrt (Jan abgeschlossen)

        var svc = NewService(db, client);
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest { CompanyProfileId = 10, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 2, 28) };

        var res = await svc.CommitAsync(req, firstAllowed: null);

        Assert.False(res.IsBlocked);
        Assert.Equal(1, res.Inserted);
        Assert.Equal(1, res.LockedSkipped);
        Assert.Single(db.EmployeeTimeEntries);
        Assert.Equal(new DateOnly(2026, 2, 20), db.EmployeeTimeEntries.Single().EntryDate);

        var st = db.EasyAtWorkSyncStates.Single(s => s.Resource == "TIMEPUNCH");
        Assert.NotNull(st.LastSyncAt);
        Assert.Null(st.LastError);
        Assert.Equal(1, st.LastRowCount);
    }

    [Fact]
    public async Task ManualCommit_MissingEmployeeOnlyInLockedPeriod_DoesNotBlock()
    {
        var db = NewDb(nameof(ManualCommit_MissingEmployeeOnlyInLockedPeriod_DoesNotBlock));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "580099", FirstName = "A", LastName = "M" });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        // Januar 2026 ABGESCHLOSSEN → Januar-Stempel gesperrt (per-Periode).
        db.PayrollPerioden.Add(new PayrollPeriode { CompanyProfileId = 10, Year = 2026, Month = 1,
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31), Status = "abgeschlossen" });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 47, Number = "580099" });          // matchbar
        client.Employees.Add(new EawEmployee { Id = 99, Number = "999999" });          // NICHT in Cowork
        client.Timepunches.Add(Punch(200, 99, new DateOnly(2026, 1, 31)));  // fehlender MA, ABER gesperrt (Jan)
        client.Timepunches.Add(Punch(201, 47, new DateOnly(2026, 2, 20)));  // editierbar, matchbar (Feb offen)

        var svc = NewService(db, client);
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest { CompanyProfileId = 10, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 2, 28) };

        var res = await svc.CommitAsync(req, firstAllowed: null);

        // Der fehlende MA #99 ist NUR in der gesperrten Periode → blockiert NICHT.
        Assert.False(res.IsBlocked);
        Assert.Equal(1, res.Inserted);     // nur der editierbare #47
        Assert.Equal(1, res.LockedSkipped);
        Assert.Single(db.EmployeeTimeEntries);
    }

    [Fact]
    public async Task ManualCommit_MissingEmployeeInEditablePeriod_Blocks()
    {
        var db = NewDb(nameof(ManualCommit_MissingEmployeeInEditablePeriod_Blocks));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "580099" });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 99, Number = "999999" });          // NICHT in Cowork
        client.Timepunches.Add(Punch(300, 99, new DateOnly(2026, 2, 20)));  // editierbar + fehlend → Block

        var svc = NewService(db, client);
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest { CompanyProfileId = 10, From = new DateOnly(2026, 1, 1), To = new DateOnly(2026, 2, 28) };

        var res = await svc.CommitAsync(req, firstAllowed: new DateOnly(2026, 2, 1));

        Assert.True(res.IsBlocked);
        Assert.Empty(db.EmployeeTimeEntries);   // nichts geschrieben
    }

    // ───────────────── Orchestrator: Sync-State Setzen ───────────────────

    private static (ServiceProvider sp, FakeEawClient client) BuildProvider(string dbName, FakeEawClient client)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<LohnEditLockService>();
        services.AddScoped<EasyAtWorkTimepunchSyncService>();
        services.AddSingleton<EasyAtWorkClient>(client);
        services.AddLogging();
        return (services.BuildServiceProvider(), client);
    }

    private static void Seed(ServiceProvider sp, bool addCoworkEmployee)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        if (addCoworkEmployee) db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "580099" });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769, AutoSyncEnabled = true });
        db.PayrollPerioden.Add(new PayrollPeriode
        {
            CompanyProfileId = 10, Year = 2026, Month = 6,
            PeriodFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-40),
            Status = "offen", AkontoStatus = "OFFEN",
        });
        db.SaveChanges();
    }

    private static EasyAtWorkAutoSyncRunner NewRunner(ServiceProvider sp, FakeEawClient client)
        => new(sp.GetRequiredService<IServiceScopeFactory>(), client, NullLogger<EasyAtWorkAutoSyncRunner>.Instance);

    private static EasyAtWorkSyncState State(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.EasyAtWorkSyncStates.Single(s => s.Resource == "TIMEPUNCH");
    }

    [Fact]
    public async Task Orchestrator_Success_SetsStateWithoutError()
    {
        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 47, Number = "580099" });
        client.Timepunches.Add(Punch(1, 47, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
        var (sp, _) = BuildProvider(nameof(Orchestrator_Success_SetsStateWithoutError), client);
        Seed(sp, addCoworkEmployee: true);

        await NewRunner(sp, client).RunAllBranchesAsync(CancellationToken.None);

        var st = State(sp);
        Assert.Null(st.LastError);
        Assert.NotNull(st.LastSyncAt);
        Assert.Equal(1, st.LastRowCount);
    }

    [Fact]
    public async Task Orchestrator_Block_SetsLastError()
    {
        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 99, Number = "999999" });   // nicht in Cowork
        client.Timepunches.Add(Punch(1, 99, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1)));
        var (sp, _) = BuildProvider(nameof(Orchestrator_Block_SetsLastError), client);
        Seed(sp, addCoworkEmployee: true);

        await NewRunner(sp, client).RunAllBranchesAsync(CancellationToken.None);

        var st = State(sp);
        Assert.NotNull(st.LastError);
        Assert.Contains("blockiert", st.LastError!);
    }

    [Fact]
    public async Task Orchestrator_Error_SetsLastError()
    {
        var client = new FakeEawClient { ThrowOnFetch = true };
        var (sp, _) = BuildProvider(nameof(Orchestrator_Error_SetsLastError), client);
        Seed(sp, addCoworkEmployee: true);

        await NewRunner(sp, client).RunAllBranchesAsync(CancellationToken.None);

        var st = State(sp);
        Assert.NotNull(st.LastError);
    }

    // ────────────────────────── Catch-up ────────────────────────────────

    private static readonly DateTime After5 = new(2026, 6, 19, 9, 0, 0);
    private static readonly DateTime Before5 = new(2026, 6, 19, 4, 0, 0);
    private static readonly DateOnly Today = new(2026, 6, 19);

    [Fact]
    public void CatchUp_After5_NotRunToday_True()
        => Assert.True(EasyAtWorkAutoSyncRunner.ShouldCatchUp(After5, new DateOnly?[] { Today.AddDays(-1) }));

    [Fact]
    public void CatchUp_After5_NeverRun_True()
        => Assert.True(EasyAtWorkAutoSyncRunner.ShouldCatchUp(After5, new DateOnly?[] { null }));

    [Fact]
    public void CatchUp_After5_AlreadyRunToday_False()
        => Assert.False(EasyAtWorkAutoSyncRunner.ShouldCatchUp(After5, new DateOnly?[] { Today }));

    [Fact]
    public void CatchUp_Before5_NotRunToday_False()
        => Assert.False(EasyAtWorkAutoSyncRunner.ShouldCatchUp(Before5, new DateOnly?[] { Today.AddDays(-1) }));

    [Fact]
    public void CatchUp_NoActiveBranches_False()
        => Assert.False(EasyAtWorkAutoSyncRunner.ShouldCatchUp(After5, System.Array.Empty<DateOnly?>()));
}
