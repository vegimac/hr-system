using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Unit-Tests für <see cref="LohnEditLockService"/> — die "königliche
/// Kontrolle" der Lohnperioden-Sperre (Walter-Vorgabe 17.05.2026).
///
/// Deckt ab:
///   • Bypass für admin/superuser
///   • Alle 5 Akonto-Statuswerte → was sperrt, was nicht
///   • Alle 3 Definitiv-Statuswerte → was sperrt, was nicht
///   • FirstAllowedDate-Berechnung (= erster Tag des Folgemonats)
///   • CheckDateAsync (= konkretes Datum)
///   • CheckRangeAsync (= Datumsbereich)
///   • Mehrere Perioden — späteste in-Verarbeitung gewinnt
///   • Filiale-Trennung (Lock von Filiale A wirkt nicht auf Filiale B)
/// </summary>
public class LohnEditLockServiceTests
{
    // ──────────────────────────────────────────────────────────────────
    // Test-Setup-Helper
    // ──────────────────────────────────────────────────────────────────

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        // Jeder Test bekommt seine eigene InMemory-DB, damit Tests
        // unabhängig sind.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "LockTest_" + testName + "_" + Guid.NewGuid())
            .Options;
        return new AppDbContext(options);
    }

    private static ClaimsPrincipal User(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "test") };
        foreach (var r in roles) claims.Add(new Claim(ClaimTypes.Role, r));
        var id = new ClaimsIdentity(claims, "test");
        return new ClaimsPrincipal(id);
    }

    private static PayrollPeriode Periode(
        int companyProfileId, int year, int month,
        string status = "offen", string akontoStatus = "OFFEN")
    {
        return new PayrollPeriode
        {
            CompanyProfileId = companyProfileId,
            Year   = year,
            Month  = month,
            Label  = $"{month:D2}/{year}",
            PeriodFrom = new DateOnly(year, month, 1),
            PeriodTo   = new DateOnly(year, month, DateTime.DaysInMonth(year, month)),
            Status        = status,
            AkontoStatus  = akontoStatus,
            CreatedAt     = DateTime.UtcNow
        };
    }

    // ──────────────────────────────────────────────────────────────────
    // Rolle ist IRRELEVANT (Walter-Vorgabe final 17.05.2026):
    // Auch admin/superuser sind gelockt, müssen erst Periode zurücksetzen.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdminUser_AlsoLocked_WhenPeriodInProcess()
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "abgeschlossen", "AUSBEZAHLT"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);

        var first = await svc.GetFirstAllowedDateAsync(User("admin"), 58);
        Assert.Equal(new DateOnly(2026, 2, 1), first); // Auch admin sieht Lock

        var r = await svc.CheckDateAsync(User("admin"), 58, new DateOnly(2026, 1, 15));
        Assert.True(r.Locked);
    }

    [Fact]
    public async Task SuperuserUser_AlsoLocked_WhenAkontoBeiHr()
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", "BEI_HR"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var first = await svc.GetFirstAllowedDateAsync(User("superuser"), 58);
        Assert.Equal(new DateOnly(2026, 2, 1), first); // Auch superuser sieht Lock
    }

    [Fact]
    public async Task RegularUser_GetsLockedWhenAkontoBeiHr()
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", "BEI_HR"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var first = await svc.GetFirstAllowedDateAsync(User("user"), 58);
        Assert.NotNull(first);
        Assert.Equal(new DateOnly(2026, 2, 1), first); // 1. Tag des Folgemonats
    }

    // ──────────────────────────────────────────────────────────────────
    // Akonto-Status-Matrix
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("OFFEN",              false)] // GF noch nicht gestartet
    [InlineData("IN_BEARBEITUNG_GF",  false)] // GF in Vorbereitung — DARF noch editieren!
    [InlineData("BEI_HR",             true)]
    [InlineData("HR_FREIGEGEBEN",     true)]
    [InlineData("AUSBEZAHLT",         true)]
    public async Task AkontoStatus_LocksOrNot(string akontoStatus, bool shouldLock)
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", akontoStatus));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var first = await svc.GetFirstAllowedDateAsync(User("user"), 58);

        if (shouldLock)
            Assert.Equal(new DateOnly(2026, 2, 1), first);
        else
            Assert.Null(first);
    }

    // ──────────────────────────────────────────────────────────────────
    // Definitiv-Status-Matrix
    // ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("offen",                       false)]
    [InlineData("provisorisch_abgeschlossen",  true)]
    [InlineData("abgeschlossen",               true)]
    public async Task DefinitivStatus_LocksOrNot(string status, bool shouldLock)
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, status, "OFFEN"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var first = await svc.GetFirstAllowedDateAsync(User("user"), 58);

        if (shouldLock)
            Assert.Equal(new DateOnly(2026, 2, 1), first);
        else
            Assert.Null(first);
    }

    // ──────────────────────────────────────────────────────────────────
    // CheckDateAsync — konkrete Datums-Prüfung
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckDate_DateInLockedPeriod_IsLocked()
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", "BEI_HR"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var r = await svc.CheckDateAsync(User("user"), 58, new DateOnly(2026, 1, 15));
        Assert.True(r.Locked);
        Assert.Equal(new DateOnly(2026, 2, 1), r.FirstAllowedDate);
        Assert.Contains("15.01.2026", r.Reason);
    }

    [Fact]
    public async Task CheckDate_DateAfterLockedPeriod_IsAllowed()
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", "BEI_HR"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var r = await svc.CheckDateAsync(User("user"), 58, new DateOnly(2026, 2, 5));
        Assert.False(r.Locked);
    }

    [Fact]
    public async Task CheckDate_DateExactlyOnFirstAllowedDate_IsAllowed()
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", "BEI_HR"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var r = await svc.CheckDateAsync(User("user"), 58, new DateOnly(2026, 2, 1));
        Assert.False(r.Locked); // Inklusive — 1.2. ist erlaubt
    }

    // ──────────────────────────────────────────────────────────────────
    // CheckRangeAsync — Datumsbereich-Prüfung
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckRange_RangeStartsInLockedPeriod_IsLocked()
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", "BEI_HR"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        // Absenz 28.01–05.02 — Start in gesperrter Periode
        var r = await svc.CheckRangeAsync(User("user"), 58,
            new DateOnly(2026, 1, 28), new DateOnly(2026, 2, 5));
        Assert.True(r.Locked);
    }

    [Fact]
    public async Task CheckRange_RangeEntirelyAfterLockedPeriod_IsAllowed()
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", "BEI_HR"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var r = await svc.CheckRangeAsync(User("user"), 58,
            new DateOnly(2026, 2, 10), new DateOnly(2026, 2, 15));
        Assert.False(r.Locked);
    }

    // ──────────────────────────────────────────────────────────────────
    // Mehrere Perioden — späteste in-Verarbeitung gewinnt
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultiplePeriods_LatestInVerarbeitungWins()
    {
        using var db = NewDb();
        // Dez 2025 = abgeschlossen, Jan 2026 = AUSBEZAHLT, Feb 2026 = offen
        db.PayrollPerioden.Add(Periode(58, 2025, 12, "abgeschlossen", "AUSBEZAHLT"));
        db.PayrollPerioden.Add(Periode(58, 2026,  1, "offen",        "AUSBEZAHLT"));
        db.PayrollPerioden.Add(Periode(58, 2026,  2, "offen",        "OFFEN"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var first = await svc.GetFirstAllowedDateAsync(User("user"), 58);

        // Späteste gesperrte ist Jan 2026 → FirstAllowed = 01.02.2026
        Assert.Equal(new DateOnly(2026, 2, 1), first);
    }

    [Fact]
    public async Task MultiplePeriods_AllOpen_NoLock()
    {
        using var db = NewDb();
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", "OFFEN"));
        db.PayrollPerioden.Add(Periode(58, 2026, 2, "offen", "IN_BEARBEITUNG_GF"));
        db.PayrollPerioden.Add(Periode(58, 2026, 3, "offen", "OFFEN"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var first = await svc.GetFirstAllowedDateAsync(User("user"), 58);
        Assert.Null(first);
    }

    // ──────────────────────────────────────────────────────────────────
    // Filiale-Trennung
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BranchA_Locked_DoesNotAffectBranchB()
    {
        using var db = NewDb();
        // Filiale 58 (Oftringen): Akonto BEI_HR
        db.PayrollPerioden.Add(Periode(58, 2026, 1, "offen", "BEI_HR"));
        // Filiale 75 (Sursee): alles offen
        db.PayrollPerioden.Add(Periode(75, 2026, 1, "offen", "OFFEN"));
        await db.SaveChangesAsync();

        var svc = new LohnEditLockService(db);
        var firstA = await svc.GetFirstAllowedDateAsync(User("user"), 58);
        var firstB = await svc.GetFirstAllowedDateAsync(User("user"), 75);

        Assert.NotNull(firstA);
        Assert.Null(firstB);
    }

    // ──────────────────────────────────────────────────────────────────
    // Keine Periode existiert → kein Lock
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task NoPeriodes_NoLock()
    {
        using var db = NewDb();
        var svc = new LohnEditLockService(db);
        var first = await svc.GetFirstAllowedDateAsync(User("user"), 58);
        Assert.Null(first);
    }
}
