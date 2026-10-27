using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class MindestlohnLgavTests
{
    [Fact]
    public void Lgav_und_vier_Modelle_pruefen_die_Tabelle()
    {
        Assert.True(MinimumWageCheckService.LgavTabelleGilt(true, null));
        Assert.True(MinimumWageCheckService.LgavTabelleGilt(true, "  "));
    }

    [Fact]
    public void Ohne_Lgav_bleibt_die_Tabelle_aus()
    {
        Assert.False(MinimumWageCheckService.LgavTabelleGilt(false, null));
        Assert.False(MinimumWageCheckService.LgavTabelleGilt(false, "apprentice"));
    }

    [Fact]
    public void Nur_Bezeichnung_L_Gav_ist_das_volle_Programm()
    {
        Assert.True(CompanyProfile.IstLgavBezeichnung(true, "L-GAV"));
        Assert.True(CompanyProfile.IstLgavBezeichnung(true, "l-gav"));
        Assert.True(CompanyProfile.IstLgavBezeichnung(true, "L-GAV (Gastgewerbe)"));
        Assert.False(CompanyProfile.IstLgavBezeichnung(false, "L-GAV"));
        Assert.False(CompanyProfile.IstLgavBezeichnung(true, "Landesmantelvertrag"));
        Assert.False(CompanyProfile.IstLgavBezeichnung(true, ""));
    }

    [Fact]
    public void Swissdec_Art_ist_kein_Lgav_Vertrag()
    {
        Assert.False(MinimumWageCheckService.LgavTabelleGilt(true, "internshipContract"));
        Assert.False(MinimumWageCheckService.LgavTabelleGilt(true, "administrativeBoard"));
    }
}
