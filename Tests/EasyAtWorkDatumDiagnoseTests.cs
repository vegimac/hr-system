using System.Collections.Generic;
using System.Linq;
using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Datums-Diagnose (Walter 24.09.2026): ordnet easy@work-Roh-Werte einer
/// Speicherart zu und meldet Ein-Tages-Verschiebungen. Beispiele = echte Fälle.
/// </summary>
public class EasyAtWorkDatumDiagnoseTests
{
    [Theory]
    [InlineData("2026-10-31 22:59:59", EasyAtWorkDatumDiagnose.Tagesende,   false)] // Winter
    [InlineData("2026-10-30 23:00:00", EasyAtWorkDatumDiagnose.Tagesanfang, false)] // 31.10. Winter
    [InlineData("2026-06-30 21:59:59", EasyAtWorkDatumDiagnose.Tagesende,   true)]  // Sommer
    [InlineData("2026-06-30 22:00:00", EasyAtWorkDatumDiagnose.Tagesanfang, true)]  // 01.07. Sommer
    [InlineData("2026-01-31",          EasyAtWorkDatumDiagnose.NurDatum,    false)]
    [InlineData("2026-03-15 10:00:00", EasyAtWorkDatumDiagnose.AndereZeit,  false)]
    [InlineData("quatsch",             EasyAtWorkDatumDiagnose.Unlesbar,    false)]
    public void Einordnen(string roh, string art, bool sommer)
    {
        var e = EasyAtWorkDatumDiagnose.Einordnen(roh);
        Assert.Equal(art, e.Art);
        Assert.Equal(sommer, e.Sommerzeit);
    }

    [Fact]
    public void Fall580101_LohnsatzEndetEinenTagFrueher()
    {
        var c = new List<EawContract> { new() { FromRaw = "2026-06-30 22:00:00", ToRaw = "2026-10-31 22:59:59" } };
        var r = new List<EawPayRate>  { new() { FromRaw = "2026-06-30 22:00:00", ToRaw = "2026-10-30 23:00:00" } };
        var erg = EasyAtWorkDatumDiagnose.Pruefe(c, r);
        Assert.Contains(erg.Befunde, b => b.Code == "LOHNSATZ_ENDE_1_TAG_FRUEHER");
        Assert.Equal(4, erg.Werte.Count);
    }

    [Fact]
    public void Fall1220009_ExklusiveMitternacht_SauberAngeschlossen()
    {
        // Vertrag bis 31.01. als exklusive Mitternacht, Folgevertrag ab 01.02.
        var c = new List<EawContract>
        {
            new() { FromRaw = "2025-05-31 22:00:00", ToRaw = "2026-01-31 23:00:00" },
            new() { FromRaw = "2026-01-31 23:00:00" },
        };
        var r = new List<EawPayRate>
        {
            new() { FromRaw = "2025-05-31 22:00:00", ToRaw = "2026-01-31 23:00:00" },
            new() { FromRaw = "2026-01-31 23:00:00" },
        };
        var erg = EasyAtWorkDatumDiagnose.Pruefe(c, r);
        Assert.Empty(erg.Befunde);
    }

    [Fact]
    public void EinTagesVertrag_UndLuecke_WerdenGemeldet()
    {
        var c = new List<EawContract>
        {
            new() { FromRaw = "2026-01-01", ToRaw = "2026-03-30" },
            new() { FromRaw = "2026-04-01", ToRaw = "2026-04-01" },
        };
        var erg = EasyAtWorkDatumDiagnose.Pruefe(c, new());
        Assert.Contains(erg.Befunde, b => b.Code == "VERTRAG_EIN_TAG");
        Assert.Contains(erg.Befunde, b => b.Code == "VERTRAG_LUECKE_1_TAG");
    }

    [Fact]
    public void GeloeschteEintraege_ZaehlenNicht()
    {
        var c = new List<EawContract> { new() { FromRaw = "2026-04-01", ToRaw = "2026-04-01", DeletedAtRaw = "2026-04-02 08:00:00" } };
        Assert.Empty(EasyAtWorkDatumDiagnose.Pruefe(c, new()).Befunde);
    }
}
