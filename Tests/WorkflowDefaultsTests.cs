using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Status-Default-Tests (Walter-Vorgabe 19.05.2026).
///
/// Stellt sicher, dass NEU angelegte Entities mit den korrekten Anfangs-
/// Status auf die Welt kommen — sonst wirken sie sofort als „bestätigt"
/// in der UI, obwohl GF nichts geklickt hat (genau Walters Bug
/// vom 19.05.2026).
///
/// Doku-Quelle: CLAUDE.md → „Bearbeitungs-Status-Map".
/// </summary>
public class WorkflowDefaultsTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "WfDef_" + testName + "_" + Guid.NewGuid())
            .Options;
        return new AppDbContext(options);
    }

    // ──────────────────────────────────────────────────────────────────
    // PayrollSnapshot: neuer Snapshot startet im Definitiv-Workflow mit
    // BERECHNET — NICHT FREIGEGEBEN_GF (Walter-Bug 19.05.2026).
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NewPayrollSnapshot_DefaultsToBerechnet()
    {
        var snap = new PayrollSnapshot();
        Assert.Equal("BERECHNET", snap.Status);
        Assert.False(snap.IsFinal);
    }

    [Fact]
    public async Task PayrollSnapshot_RoundTripsThroughDb_WithBerechnetStatus()
    {
        using var db = NewDb();
        db.PayrollSnapshots.Add(new PayrollSnapshot
        {
            PayrollPeriodeId = 1, EmployeeId = 1, CompanyProfileId = 58,
            SlipJson = "{}"
        });
        await db.SaveChangesAsync();

        var loaded = await db.PayrollSnapshots.FirstAsync();
        Assert.Equal("BERECHNET", loaded.Status);
        Assert.False(loaded.IsFinal);
    }

    // ──────────────────────────────────────────────────────────────────
    // AkontoZahlung: neue Zahlung startet auch mit BERECHNET.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NewAkontoZahlung_DefaultsToBerechnet()
    {
        var z = new AkontoZahlung();
        Assert.Equal("BERECHNET", z.Status);
    }

    // ──────────────────────────────────────────────────────────────────
    // PayrollPeriode: Status-Defaults für beide Workflows.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NewPeriode_DefinitivStatus_IsOffen()
    {
        var p = new PayrollPeriode();
        Assert.Equal("offen", p.Status);
    }

    [Fact]
    public void NewPeriode_AkontoStatus_IsOffen()
    {
        var p = new PayrollPeriode();
        Assert.Equal("OFFEN", p.AkontoStatus);
    }

    [Fact]
    public void NewPeriode_BothAuszahlungsdaten_AreNull()
    {
        var p = new PayrollPeriode();
        Assert.Null(p.Auszahlungsdatum);
        Assert.Null(p.AkontoAuszahlungsdatum);
    }

    // ──────────────────────────────────────────────────────────────────
    // PayrollSaldo: Default-Status ist "draft" (nicht "confirmed").
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NewPayrollSaldo_DefaultsToDraft()
    {
        var sld = new PayrollSaldo
        {
            EmployeeId = 1, CompanyProfileId = 58,
            PeriodYear = 2026, PeriodMonth = 1
        };
        Assert.Equal("draft", sld.Status);
    }
}
