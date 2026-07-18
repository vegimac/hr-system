using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für den Personalnummern-Wechsel beim easy@work-MA-Sync (Walter-Vorgabe
/// 21.06.2026). Die bisherige Nummer wird in der Tabelle employee_number_alias
/// gesichert, die neue als employee_number gesetzt. Guards verhindern Dubletten.
/// </summary>
public class EasyAtWorkNumberChangeTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("NumChange_" + testName + "_" + System.Guid.NewGuid()).Options);

    // ───────────────────── ShouldSaveNumberChange (Guards) ─────────────────────

    [Fact]
    public void NeueNummer_AndersAlsAlle_WirdGespeichert()
    {
        Assert.True(EasyAtWorkEmployeeSyncService.ShouldSaveNumberChange("1040025", "1220062", null));
    }

    [Fact]
    public void LeereNeueNummer_WirdNichtGespeichert()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldSaveNumberChange("1040025", "  ", null));
    }

    [Fact]
    public void GleicheNummer_WirdNichtGespeichert()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldSaveNumberChange("1040025", "1040025", null));
    }

    [Fact]
    public void NeueNummerSchonAlias_WirdNichtGespeichert()
    {
        var aliases = new[] { "1030011", "1040025" };
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldSaveNumberChange("1220062", "1040025", aliases));
    }

    // ───────────────────── SaveNumberChange (Alias-Insert) ─────────────────────

    [Fact]
    public async Task SaveNumberChange_LegtAliasAn_UndSetztNeueNummer()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "1040025", FirstName = "Max", LastName = "Muster" };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        EasyAtWorkEmployeeSyncService.SaveNumberChange(db, emp, "1220062");
        await db.SaveChangesAsync();

        Assert.Equal("1220062", emp.EmployeeNumber);
        var alias = Assert.Single(await db.EmployeeNumberAliases.Where(a => a.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal("1040025", alias.Number);
        Assert.Equal("easyatwork_sync", alias.Source);
        Assert.NotNull(alias.ValidTo);
    }

    [Fact]
    public async Task ZweiterWechsel_BehaeltBeideAlteNummern()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "1030011", FirstName = "Max", LastName = "Muster" };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();

        EasyAtWorkEmployeeSyncService.SaveNumberChange(db, emp, "1040025");
        await db.SaveChangesAsync();
        EasyAtWorkEmployeeSyncService.SaveNumberChange(db, emp, "1220062");
        await db.SaveChangesAsync();

        Assert.Equal("1220062", emp.EmployeeNumber);
        var nums = await db.EmployeeNumberAliases.Where(a => a.EmployeeId == emp.Id)
            .Select(a => a.Number).OrderBy(n => n).ToListAsync();
        Assert.Equal(new[] { "1030011", "1040025" }, nums);
    }

    // Rollen-Tausch (Walter 12.07.2026, Alaa/Rasakumary): ist die neue
    // Hauptnummer bereits ein ALIAS des MA, wird diese Alias-Zeile zur alten
    // Hauptnummer umgeschrieben — dieselbe Nummer darf nie doppelt existieren
    // (als Haupt- UND Alias-Nummer).
    [Fact]
    public async Task WechselAufBestehendenAlias_TauschtRollen_OhneDuplikat()
    {
        using var db = NewDb();
        var emp = new Employee { EmployeeNumber = "581026", FirstName = "Alaa", LastName = "Aerni" };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        db.EmployeeNumberAliases.Add(new EmployeeNumberAlias
        {
            EmployeeId = emp.Id, Number = "1040001", Source = "easyatwork_sync"
        });
        await db.SaveChangesAsync();

        EasyAtWorkEmployeeSyncService.SaveNumberChange(db, emp, "1040001");
        await db.SaveChangesAsync();

        Assert.Equal("1040001", emp.EmployeeNumber);
        var aliases = await db.EmployeeNumberAliases.Where(a => a.EmployeeId == emp.Id).ToListAsync();
        var alias = Assert.Single(aliases);               // KEINE zweite Zeile
        Assert.Equal("581026", alias.Number);             // Rollen getauscht
        Assert.NotNull(alias.ValidTo);
    }

    // ───────────────────── Archiv-«alt» vs. nackte easy@work-Nummer ─────────────
    // Walter-Bug 18.07.2026 (Sweeba Akhtar): Sync darf «581039alt» nicht zu
    // «581039» hochstufen — easy@work kennt das Suffix nie, es ist dieselbe Badge.

    [Theory]
    [InlineData("581039alt", "581039")]
    [InlineData("581039", "581039alt")]
    [InlineData("58631alt", "58631")]
    public void IsSameNumberIgnoringAlt_GleicheBadge(string a, string b)
    {
        Assert.True(EasyAtWorkEmployeeSyncService.IsSameNumberIgnoringAlt(a, b));
    }

    [Theory]
    [InlineData("581039alt", "580050")]
    [InlineData("581039", "581040")]
    [InlineData("", "581039")]
    [InlineData(null, "581039")]
    public void IsSameNumberIgnoringAlt_AndereBadge(string? a, string b)
    {
        Assert.False(EasyAtWorkEmployeeSyncService.IsSameNumberIgnoringAlt(a, b));
    }

    [Fact]
    public void ShouldPromote_ArchivAlt_NichtZuNackterNummer()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldPromoteEawNumberToMain(
            "581039alt", "581039", eawRecordAktiv: true, nummerBesetzt: false));
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldPromoteEawNumberToMain(
            "581039alt", "581039", eawRecordAktiv: false, nummerBesetzt: false));
    }

    [Fact]
    public void ShouldPromote_EchteNeueNummer_BeiWiedereintritt()
    {
        Assert.True(EasyAtWorkEmployeeSyncService.ShouldPromoteEawNumberToMain(
            "580050alt", "581100", eawRecordAktiv: true, nummerBesetzt: false));
        Assert.True(EasyAtWorkEmployeeSyncService.ShouldPromoteEawNumberToMain(
            "580050alt", "581100", eawRecordAktiv: false, nummerBesetzt: false));
    }

    [Fact]
    public void ShouldPromote_BesetzteNummer_Nein()
    {
        Assert.False(EasyAtWorkEmployeeSyncService.ShouldPromoteEawNumberToMain(
            "580050alt", "581100", eawRecordAktiv: true, nummerBesetzt: true));
    }
}
