using HrSystem.Services.LohnFormelwerk;
using Xunit;

namespace HrSystem.Tests;

public class LohnFormelwerkTests
{
    [Fact]
    public void Registry_EnthaeltKernformeln()
    {
        Assert.NotNull(LohnFormelwerk.Finde(LohnFormelwerk.TagessatzFix));
        Assert.NotNull(LohnFormelwerk.Finde(LohnFormelwerk.NachtKomp10));
        Assert.NotNull(LohnFormelwerk.Finde(LohnFormelwerk.FerienFixSplit));
        Assert.True(LohnFormelwerk.Alle.Count >= 15);
    }

    [Fact]
    public void TagessatzFix_MonatslohnMal12Durch365()
    {
        var t = LohnFormelwerk.BerechneTagessatzFix(3650m);
        Assert.Equal(120m, t); // 3650×12/365 = 120
    }

    [Fact]
    public void NachtKomp_10Prozent()
    {
        var b = LohnFormelwerk.BerechneNachtKomp(10m, 25m); // 25.00
        Assert.Equal(25.00m, b);
    }

    [Fact]
    public void LohnCodes_SwissdecUndMirus()
    {
        Assert.Equal("1000", LohnCodes.Festlohn);
        Assert.Equal("1076", LohnCodes.NachtKompensation);
        Assert.Equal("1166", LohnCodes.FerienFixGeld);
        Assert.Equal("1168", LohnCodes.FerienAustritt);
        Assert.Equal("10.1", LohnCodes.MirusFuer(LohnCodes.Festlohn));
        Assert.Equal("55.10", LohnCodes.MirusFuer(LohnCodes.NachtKompensation));
    }

    [Fact]
    public void Alias_NormiertMirusAufSwissdec()
    {
        Assert.Equal(LohnCodes.Festlohn, LohnCodeAlias.Normiere("10.1"));
        Assert.Equal(LohnCodes.NachtKompensation, LohnCodeAlias.Normiere("55.10"));
        Assert.True(LohnCodeAlias.Ist("10.1", LohnCodes.Festlohn));
        Assert.True(LohnCodeAlias.Ist(LohnCodes.Festlohn, "10.1"));
        Assert.Equal("2002", LohnCodeAlias.Normiere("2001.2"));
        Assert.Equal("2002", LohnCodeAlias.Normiere("120.2"));
        Assert.Equal("2050", LohnCodeAlias.Normiere("125.1"));
        Assert.Equal("1200", LohnCodes.SwissdecVon("180.3"));
        Assert.True(LohnCodeAlias.Ist13mlAuszahlen("180.3", "180.3"));
    }

    [Fact]
    public void Inventar_DecktFestlohnUndNacht()
    {
        Assert.Contains(LohnFormelInventar.Alle, z => z.SwissdecCode == LohnCodes.Festlohn);
        Assert.Contains(LohnFormelInventar.Alle, z => z.SwissdecCode == LohnCodes.NachtKompensation);
        Assert.Contains(LohnFormelInventar.Alle, z => z.SwissdecCode == LohnCodes.FerienFixGeld);
    }

    [Fact]
    public void KontoplanSchluessel_AusMirus()
    {
        var (pos, sub) = LohnCodes.KontoplanSchluessel("600.24");
        Assert.Equal(600, pos);
        Assert.Equal(24, sub);
        var (p2, s2) = LohnCodes.KontoplanSchluessel(LohnCodes.Lgav);
        Assert.Equal(600, p2);
        Assert.Equal(24, s2);
    }

    [Fact]
    public void Berechne_Generisch()
    {
        var e = LohnFormelwerk.Berechne(LohnFormelwerk.Round05,
            new Dictionary<string, decimal> { ["betrag"] = 10.03m });
        Assert.Equal(10.05m, e.Betrag);
    }
}
