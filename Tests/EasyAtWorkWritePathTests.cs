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
        // EnrichEditedChangelogsAsync holt Einzel-Stempel — Fake liefert den
        // Listeneintrag (oder null), nie HTTP.
        public override Task<EawTimepunch?> GetTimepunchAsync(int c, int timepunchId, CancellationToken ct = default)
            => Task.FromResult(Timepunches.FirstOrDefault(p => p.Id == timepunchId));
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

    /// <summary>
    /// Walter-Bug 18.07.2026: Re-Import zeigte immer wieder «N geändert».
    /// Ursache: List-API ohne Comments + CreatedAt-/UpdatedAt-Fallback hat
    /// gespeicherte Metadaten überschrieben. Zweiter Lauf ohne Enrich-Daten
    /// muss «unverändert» bleiben.
    /// </summary>
    [Fact]
    public async Task Reimport_WithoutComments_DoesNotRecountAsUpdated()
    {
        var db = NewDb(nameof(Reimport_WithoutComments_DoesNotRecountAsUpdated));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "580099", FirstName = "Anna", LastName = "Meier" });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        await db.SaveChangesAsync();

        var date = new DateOnly(2026, 7, 10);
        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 47, Number = "580099", FirstName = "Anna", LastName = "Meier" });

        // 1. Lauf: bearbeiteter Stempel MIT Comment + Changelog (wie nach Enrich).
        var rich = Punch(700, 47, date);
        rich.EditedById = 99;
        rich.CreatedAt = new DateTime(2026, 7, 10, 5, 38, 12, DateTimeKind.Utc); // ≠ In
        rich.UpdatedAt = new DateTime(2026, 7, 10, 14, 0, 0, DateTimeKind.Utc);
        rich.Comments = new List<EawTimepunchComment>
        {
            new()
            {
                Text = "Falsch gestempelt",
                CreatedAt = new DateTime(2026, 7, 10, 13, 0, 0, DateTimeKind.Utc),
                CreatedByName = "Max Manager"
            }
        };
        rich.Changelog = new List<EawTimepunchChangelogEntry>
        {
            new() { Text = "Ein vom 10.7.2026, 07:38 bis zum 10.7.2026, 08:00 geändert" }
        };
        client.Timepunches.Add(rich);

        var svc = NewService(db, client);
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = 10, From = new DateOnly(2026, 7, 1), To = new DateOnly(2026, 7, 17)
        };
        var first = await svc.CommitAsync(req, firstAllowed: null);
        Assert.Equal(1, first.Inserted);

        var stored = db.EmployeeTimeEntries.Single();
        Assert.Equal("Falsch gestempelt", stored.Comment);
        Assert.NotNull(stored.OriginalTimeIn);
        Assert.Equal("Max Manager", stored.EditedBy);

        // 2. Lauf: dieselben Stempelzeiten, aber OHNE Comments/Changelog
        // (List-API / Enrich-Miss) — darf NICHT als «geändert» zählen.
        client.Timepunches.Clear();
        var bare = Punch(700, 47, date);
        bare.EditedById = 99;
        bare.CreatedAt = rich.CreatedAt;
        bare.UpdatedAt = rich.UpdatedAt;
        client.Timepunches.Add(bare);

        // EF trackt die Entity noch — neuen Scope via Reload simulieren:
        db.ChangeTracker.Clear();
        var second = await svc.CommitAsync(req, firstAllowed: null);
        Assert.Equal(0, second.Inserted);
        Assert.Equal(0, second.Updated);
        Assert.Equal(1, second.Unchanged);

        var again = db.EmployeeTimeEntries.Single();
        Assert.Equal("Falsch gestempelt", again.Comment);
        Assert.Equal(stored.OriginalTimeIn, again.OriginalTimeIn);
        Assert.Equal("Max Manager", again.EditedBy);
    }

    // ─────────── Tief-Import: Matching wie Vorschau (Walter 21.06.2026) ───────────
    // Regression: der Commit-Pfad (ApplyTimepunchesAsync) muss DIESELBE Matching-
    // Logik nutzen wie die Vorschau (byEawId + ResolvePayrollSink) und cutoff +
    // ignoreMissing respektieren — sonst importiert der Tief-Import 0 Stempel für
    // Pre-Mirus-„alt"-MA (Bug: Mohamed #58631alt, eaw-id 4430).

    [Fact]
    public async Task DeepImport_2021_MatchesViaEawId_WithCutoffOverride()
    {
        var db = NewDb(nameof(DeepImport_2021_MatchesViaEawId_WithCutoffOverride));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        // Pre-Mirus-MA: Nummer trägt „alt"-Suffix, easy@work sendet die nackte
        // Nummer + die eaw-employee-id. Match MUSS über die eaw-id laufen.
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "58631alt", EasyAtWorkEmployeeId = 4430, FirstName = "Mohamed", LastName = "Ahmed" });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 4430, Number = "58631", FirstName = "Mohamed", LastName = "Ahmed", To = new DateOnly(2021, 8, 10) });
        client.Timepunches.Add(Punch(500, 4430, new DateOnly(2021, 2, 15)));

        var svc = NewService(db, client);
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = 10, From = new DateOnly(2021, 1, 1), To = new DateOnly(2021, 3, 31),
            EmployeeCutoffOverride = new DateOnly(2021, 1, 1), IgnoreMissing = true
        };

        var res = await svc.CommitAsync(req, firstAllowed: null);

        Assert.False(res.IsBlocked);
        Assert.Equal(1, res.Inserted);
        Assert.Equal(1, db.EmployeeTimeEntries.Single().EmployeeId);   // dem Pre-Mirus-MA zugeordnet
    }

    [Fact]
    public async Task DeepImport_2021_WithoutCutoffOverride_PunchLockedOut()
    {
        var db = NewDb(nameof(DeepImport_2021_WithoutCutoffOverride_PunchLockedOut));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "58631alt", EasyAtWorkEmployeeId = 4430 });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 4430, Number = "58631", To = new DateOnly(2021, 8, 10) });
        client.Timepunches.Add(Punch(510, 4430, new DateOnly(2021, 2, 15)));

        var svc = NewService(db, client);
        // KEIN Override → Standard-Stichtag 1.1.2025 → der 2021er-Stempel ist gesperrt.
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = 10, From = new DateOnly(2021, 1, 1), To = new DateOnly(2021, 3, 31)
        };

        var res = await svc.CommitAsync(req, firstAllowed: null);

        Assert.False(res.IsBlocked);
        Assert.Equal(0, res.Inserted);
        Assert.Equal(1, res.LockedSkipped);
        Assert.Empty(db.EmployeeTimeEntries);
    }

    [Fact]
    public async Task DeepImport_IgnoreMissing_SkipsUnknownMa_DoesNotBlock()
    {
        var db = NewDb(nameof(DeepImport_IgnoreMissing_SkipsUnknownMa_DoesNotBlock));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "58631alt", EasyAtWorkEmployeeId = 4430 });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 4430, Number = "58631", To = new DateOnly(2021, 8, 10) });   // matchbar
        client.Employees.Add(new EawEmployee { Id = 9999, Number = "999999", To = new DateOnly(2021, 5, 1) });  // NICHT in Cowork
        client.Timepunches.Add(Punch(600, 4430, new DateOnly(2021, 2, 15)));   // matchbar
        client.Timepunches.Add(Punch(601, 9999, new DateOnly(2021, 2, 16)));   // fehlend

        var svc = NewService(db, client);
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = 10, From = new DateOnly(2021, 1, 1), To = new DateOnly(2021, 3, 31),
            EmployeeCutoffOverride = new DateOnly(2021, 1, 1), IgnoreMissing = true
        };

        var res = await svc.CommitAsync(req, firstAllowed: null);

        Assert.False(res.IsBlocked);           // fehlender MA blockiert NICHT (Tief-Import)
        Assert.Equal(1, res.Inserted);         // nur der matchbare
        Assert.Equal(1, db.EmployeeTimeEntries.Single().EmployeeId);
        Assert.Single(res.SkippedMissingEmployees);   // der fehlende MA fürs UI gezählt
        Assert.Empty(res.MissingEmployees);           // NICHT in der blockierenden Liste
    }

    [Fact]
    public async Task DeepImport_WithoutIgnoreMissing_UnknownMa_Blocks()
    {
        var db = NewDb(nameof(DeepImport_WithoutIgnoreMissing_UnknownMa_Blocks));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "58631alt", EasyAtWorkEmployeeId = 4430 });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 9999, Number = "999999", To = new DateOnly(2021, 5, 1) });
        client.Timepunches.Add(Punch(610, 9999, new DateOnly(2021, 2, 16)));

        var svc = NewService(db, client);
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = 10, From = new DateOnly(2021, 1, 1), To = new DateOnly(2021, 3, 31),
            EmployeeCutoffOverride = new DateOnly(2021, 1, 1), IgnoreMissing = false
        };

        var res = await svc.CommitAsync(req, firstAllowed: null);

        Assert.True(res.IsBlocked);
        Assert.Empty(db.EmployeeTimeEntries);
    }

    [Fact]
    public async Task DeepImport_MultipleCoworkSameEawId_WritesToPayrollSink()
    {
        var db = NewDb(nameof(DeepImport_MultipleCoworkSameEawId_WritesToPayrollSink));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        // Gleiche Person in zwei Filialen abgelegt, gleiche eaw-id. Genau einer
        // ist Lohn-MA (IsPayrollExcluded=false) → dort landet der Stempel.
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "58631alt", EasyAtWorkEmployeeId = 4430, IsPayrollExcluded = true });
        db.Employees.Add(new Employee { Id = 2, EmployeeNumber = "58631",   EasyAtWorkEmployeeId = 4430, IsPayrollExcluded = false });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 4430, Number = "58631", To = new DateOnly(2021, 8, 10) });
        client.Timepunches.Add(Punch(700, 4430, new DateOnly(2021, 2, 15)));

        var svc = NewService(db, client);
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = 10, From = new DateOnly(2021, 1, 1), To = new DateOnly(2021, 3, 31),
            EmployeeCutoffOverride = new DateOnly(2021, 1, 1), IgnoreMissing = true
        };

        var res = await svc.CommitAsync(req, firstAllowed: null);

        Assert.False(res.IsBlocked);
        Assert.Equal(1, res.Inserted);
        Assert.Equal(2, db.EmployeeTimeEntries.Single().EmployeeId);   // der Lohn-MA, NICHT der ausgeschlossene
    }

    [Fact]
    public async Task DeepImport_MultiplePayrollSinks_Blocks()
    {
        var db = NewDb(nameof(DeepImport_MultiplePayrollSinks_Blocks));
        db.CompanyProfiles.Add(new CompanyProfile { Id = 10 });
        // Datenfehler: ZWEI Lohn-MA (beide IsPayrollExcluded=false) für dieselbe
        // Person → Ambiguous → blockiert IMMER, auch beim Tief-Import.
        db.Employees.Add(new Employee { Id = 1, EmployeeNumber = "58631alt", EasyAtWorkEmployeeId = 4430, IsPayrollExcluded = false });
        db.Employees.Add(new Employee { Id = 2, EmployeeNumber = "58631",   EasyAtWorkEmployeeId = 4430, IsPayrollExcluded = false });
        db.EasyAtWorkBranchMappings.Add(new EasyAtWorkBranchMapping { Id = 1, CompanyProfileId = 10, EasyAtWorkCustomerId = 769 });
        await db.SaveChangesAsync();

        var client = new FakeEawClient();
        client.Employees.Add(new EawEmployee { Id = 4430, Number = "58631", To = new DateOnly(2021, 8, 10) });
        client.Timepunches.Add(Punch(710, 4430, new DateOnly(2021, 2, 15)));

        var svc = NewService(db, client);
        var req = new EasyAtWorkTimepunchSyncService.SyncRequest
        {
            CompanyProfileId = 10, From = new DateOnly(2021, 1, 1), To = new DateOnly(2021, 3, 31),
            EmployeeCutoffOverride = new DateOnly(2021, 1, 1), IgnoreMissing = true
        };

        var res = await svc.CommitAsync(req, firstAllowed: null);

        Assert.True(res.IsBlocked);
        Assert.Single(res.AmbiguousEmployees);   // sauber getrennt von MissingEmployees
        Assert.Empty(res.MissingEmployees);
        Assert.Empty(db.EmployeeTimeEntries);
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
