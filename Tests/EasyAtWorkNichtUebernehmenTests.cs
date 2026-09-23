using System.Collections.Generic;
using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// «Nicht übernehmen»-Marke (Walter-Vorgabe 23.09.2026, Fall Llalloshi): Ein in
/// easy@work nicht löschbarer Fehl-Datensatz wird über das Feld «Employeenumber
/// in Payroll system» markiert und von OneCrew überall übergangen.
/// </summary>
public class EasyAtWorkNichtUebernehmenTests
{
    [Theory]
    [InlineData("diesen Datensatz nicht übernehmen", true)]
    [InlineData("Nicht Übernehmen", true)]
    [InlineData("nicht uebernehmen", true)]
    [InlineData("deleted", true)]
    [InlineData("  DELETE ", true)]
    [InlineData("gelöscht", true)]
    [InlineData("2300060", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Marke_WirdErkannt(string? wert, bool erwartet)
        => Assert.Equal(erwartet, EasyAtWorkEmployeeSyncService.IstNichtUebernehmenMarke(wert));

    [Fact]
    public void NeuesteVersionDesPayrollFelds_Entscheidet()
    {
        var props = new List<EawProperty>
        {
            new() { Key = "cf_employeenumber_in_payroll_system", Value = "2300060",
                    FromRaw = "2026-06-06 22:00:00" },
            new() { Key = "cf_employeenumber_in_payroll_system", Value = "diesen Datensatz nicht übernehmen",
                    FromRaw = "2026-09-22 22:00:00" },
            new() { Key = "cf_familienstand", Value = "Verheiratet" },
        };
        Assert.True(EasyAtWorkEmployeeSyncService.IstNichtUebernehmen(props));
    }

    [Fact]
    public void AndereFelder_LoesenNichtsAus()
    {
        var props = new List<EawProperty>
        {
            new() { Key = "cf_notes", Value = "deleted" },
            new() { Key = "cf_employeenumber_in_payroll_system", Value = "2300060" },
        };
        Assert.False(EasyAtWorkEmployeeSyncService.IstNichtUebernehmen(props));
    }
}
