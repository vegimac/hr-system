using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Betriebszugehörigkeit getrennt vom Eintritt (Walter-Vorgabe 24.09.2026).
///
/// Anlass: Simona Dan wechselte von Sursee nach Reinach. easy@work vergab dort ein
/// neues «Datum der Betriebszugehörigkeit» (24.09.2026) — damit fiel sie von fünf
/// Dienstjahren zurück ins erste. Daran hängen die Lohnfortzahlung bei Krankheit,
/// die Karenztage und die Sperrfrist nach Art. 336c.
///
/// Seither gibt es zwei Felder: <c>EntryDate</c> ist der AKTUELLE Eintritt
/// (Probezeit, Vertrag), <c>DienstalterSeit</c> die Zeit, ab der die Dienstjahre
/// zählen. Gesetzt wird das zweite nur von HR — nie vom Import, nie automatisch,
/// weil bei einem Unterbruch die Dienstjahre rechtlich neu beginnen können.
/// </summary>
public class DienstalterTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("Dienstalter_" + t + "_" + Guid.NewGuid()).Options);

    private static Employee Ma(int id, DateTime? eintritt, DateTime? dienstalter = null) => new()
    {
        Id = id,
        FirstName = "Simona", LastName = "Test" + id,
        EmployeeNumber = "129009" + id,
        IsActive = true, IsHidden = false, IsPayrollExcluded = false,
        MaritalStatus = "Verheiratet", SocialSecurityNumber = "756.1234.5678.97",
        EntryDate = eintritt,
        DienstalterSeit = dienstalter,
    };

    private static Employment Vertrag(int id, int empId, DateTime von, DateTime? bis, int filiale = 58) => new()
    {
        Id = id, EmployeeId = empId, CompanyProfileId = filiale,
        ContractStartDate = von, ContractEndDate = bis,
        EmploymentModel = "FLEX", IsActive = bis == null || bis >= DateTime.Today,
    };

    // ── Das massgebende Datum ────────────────────────────────────────────────

    [Fact]
    public void OhneDienstalter_GiltDerEintritt()
    {
        var ma = Ma(1, new DateTime(2026, 9, 24));
        Assert.Equal(new DateTime(2026, 9, 24), ma.DienstalterMassgebend);
    }

    [Fact]
    public void MitDienstalter_GiltDasDienstalter()
    {
        var ma = Ma(1, new DateTime(2026, 9, 24), new DateTime(2021, 10, 1));
        Assert.Equal(new DateTime(2021, 10, 1), ma.DienstalterMassgebend);
        // Der Eintritt bleibt unangetastet — er gilt weiter für Probezeit/Vertrag.
        Assert.Equal(new DateTime(2026, 9, 24), ma.EntryDate);
    }

    [Fact]
    public void OhneBeides_BleibtLeer()
        => Assert.Null(Ma(1, null).DienstalterMassgebend);

    // ── Sperrfrist rechnet ab der Betriebszugehörigkeit ──────────────────────

    /// <summary>Sperrfrist am Stichtag, ohne Krankheit egal — geprüft wird das Dienstjahr.</summary>
    private static async Task<int?> DienstjahrAsync(AppDbContext db, int empId)
    {
        var info = await new SperrfristService(db).ComputeAsync(empId, DateOnly.FromDateTime(DateTime.Today));
        return info.DienstjahrAmStichtag;
    }

    [Fact]
    public async Task Uebertritt_OhneDienstalter_FaelltInsErsteDienstjahr()
    {
        // Genau der Fehlerfall: Eintritt sprang auf heute, alte Verträge seit 2021.
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-5), DateTime.Today.AddDays(-50), 75));
        db.Employments.Add(Vertrag(2, 1, DateTime.Today, null, 129));
        await db.SaveChangesAsync();

        Assert.Equal(1, await DienstjahrAsync(db, 1));
    }

    [Fact]
    public async Task Uebertritt_MitDienstalter_ZaehltWeiter()
    {
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today, DateTime.Today.AddYears(-5)));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-5), DateTime.Today.AddDays(-50), 75));
        db.Employments.Add(Vertrag(2, 1, DateTime.Today, null, 129));
        await db.SaveChangesAsync();

        // Fünf Jahre gedient ⇒ sechstes Dienstjahr ⇒ die lange Sperrfrist greift.
        Assert.Equal(6, await DienstjahrAsync(db, 1));
        var info = await new SperrfristService(db).ComputeAsync(1, DateOnly.FromDateTime(DateTime.Today));
        Assert.Equal(180, info.SperrfristTage ?? 180);
    }

    [Fact]
    public async Task Probezeit_HaengtAmEintritt_NichtAmDienstalter()
    {
        // Beim Übertritt beginnt die Probezeit neu, die Dienstjahre aber nicht.
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today.AddDays(-10), DateTime.Today.AddYears(-5)));
        var v = Vertrag(1, 1, DateTime.Today.AddDays(-10), null, 129);
        v.ProbationPeriodMonths = 3;
        db.Employments.Add(v);
        await db.SaveChangesAsync();

        var info = await new SperrfristService(db).ComputeAsync(1, DateOnly.FromDateTime(DateTime.Today));
        // In Probezeit — obwohl das Dienstalter fünf Jahre zurückliegt.
        Assert.Equal("IN_PROBEZEIT", info.Status);
    }

    // ── To-do «Betriebszugehörigkeit prüfen» ─────────────────────────────────

    private static DashboardService Svc(AppDbContext db)
        => new(db, new QstPflichtCheckService(db), new SperrfristService(db));

    private static async Task<List<string>> AlertsAsync(AppDbContext db)
        => (await Svc(db).BuildAsync(null)).Alerts
            .Where(a => a.Category == "dienstalter_pruefen")
            .Select(a => a.Subtitle ?? "").ToList();

    [Fact]
    public async Task AeltereVertraegeAlsDerEintritt_Melden()
    {
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-5), DateTime.Today.AddDays(-50), 75));
        db.Employments.Add(Vertrag(2, 1, DateTime.Today, null, 129));
        await db.SaveChangesAsync();

        var einzeln = Assert.Single(await AlertsAsync(db));
        Assert.Contains("Verträge seit " + DateTime.Today.AddYears(-5).ToString("dd.MM.yyyy"), einzeln);
    }

    [Fact]
    public async Task EntschiedenerFall_MeldetNichtMehr()
    {
        // Sobald HR entschieden hat (Feld gesetzt), ist die Frage beantwortet.
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today, DateTime.Today.AddYears(-5)));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-5), DateTime.Today.AddDays(-50), 75));
        db.Employments.Add(Vertrag(2, 1, DateTime.Today, null, 129));
        await db.SaveChangesAsync();

        Assert.Empty(await AlertsAsync(db));
    }

    [Fact]
    public async Task NormalerMa_MeldetNicht()
    {
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today.AddYears(-3)));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-3), null));
        await db.SaveChangesAsync();

        Assert.Empty(await AlertsAsync(db));
    }

    [Fact]
    public async Task KleineVerschiebung_MeldetNicht()
    {
        // Vertrag beginnt zwei Wochen vor dem erfassten Eintritt — Alltag, keine Meldung.
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today.AddDays(-30)));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddDays(-44), null));
        await db.SaveChangesAsync();

        Assert.Empty(await AlertsAsync(db));
    }

    [Fact]
    public async Task PhantomMa_MeldetNicht()
    {
        using var db = NewDb();
        var ma = Ma(1, DateTime.Today);
        ma.IsPayrollExcluded = true;
        db.Employees.Add(ma);
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-5), DateTime.Today.AddDays(-50), 75));
        db.Employments.Add(Vertrag(2, 1, DateTime.Today, null, 129));
        await db.SaveChangesAsync();

        Assert.Empty(await AlertsAsync(db));
    }
}
