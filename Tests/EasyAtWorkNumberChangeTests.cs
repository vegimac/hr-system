using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für den Personalnummern-Wechsel beim easy@work-MA-Sync (Walter-Vorgabe
/// 21.06.2026): liefert easy@work eine NEUE Nummer, wird die bisherige in Alt1
/// gesichert (Alt1 → Alt2) und die neue gesetzt. Guards verhindern Endlos-Rotation.
/// </summary>
public class EasyAtWorkNumberChangeTests
{
    // ───────────────────── ShouldRotateNumber (Guards) ─────────────────────

    [Fact]
    public void NeueNummer_AndersAlsAlle_RotiertWird()
    {
        Assert.True(EasyAtWorkEmployeeSyncService.ShouldRotateNumber("1040025", null, null, "1220062"));
    }

    [Fact]
    public void LeereNeueNummer_RotiertNicht()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldRotateNumber("1040025", null, null, "  "));
    }

    [Fact]
    public void GleicheNummer_RotiertNicht()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldRotateNumber("1040025", null, null, "1040025"));
    }

    [Fact]
    public void NeueNummerSchonInAlt1_RotiertNicht()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldRotateNumber("1220062", "1040025", null, "1040025"));
    }

    [Fact]
    public void NeueNummerSchonInAlt2_RotiertNicht()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldRotateNumber("1220062", "1100000", "1040025", "1040025"));
    }

    // ───────────────────── RotateEmployeeNumber (Shift) ─────────────────────

    [Fact]
    public void Rotation_OhneAlt1_SetztAktuelleInAlt1()
    {
        var emp = new Employee { EmployeeNumber = "1040025" };

        EasyAtWorkEmployeeSyncService.RotateEmployeeNumber(emp, "1220062");

        Assert.Equal("1220062", emp.EmployeeNumber);
        Assert.Equal("1040025", emp.EmployeeNumberAlt1);
        Assert.Null(emp.EmployeeNumberAlt2);
    }

    [Fact]
    public void Rotation_MitAlt1_SchiebtAlt1NachAlt2()
    {
        var emp = new Employee { EmployeeNumber = "1040025", EmployeeNumberAlt1 = "0950000" };

        EasyAtWorkEmployeeSyncService.RotateEmployeeNumber(emp, "1220062");

        Assert.Equal("1220062", emp.EmployeeNumber);
        Assert.Equal("1040025", emp.EmployeeNumberAlt1);
        Assert.Equal("0950000", emp.EmployeeNumberAlt2);
    }
}
