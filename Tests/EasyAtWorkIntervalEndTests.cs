using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// «Bis» bei Vertrag und Lohnsatz (Walter-Vorgabe 24.09.2026): UTC → Zürich,
/// dann den Kalendertag. 23:59:59 und 00:00 bleiben beide dieser Tag — 00:00
/// ist NICHT der Vortag. Genau so, wie easy die Spalte «Bis» anzeigt.
/// </summary>
public class EasyAtWorkIntervalEndTests
{
    [Fact]
    public void Bis_IstDerZuercherKalendertag()
    {
        // MA 1220009: Vertrag 23:59:59 am 31.01., Lohnsatz 00:00 am 01.02.
        var c = new EawContract { ToRaw = "2026-01-31 22:59:59" };
        var r = new EawPayRate  { ToRaw = "2026-01-31 23:00:00" };
        Assert.Equal(new DateOnly(2026, 1, 31), c.To);
        Assert.Equal(new DateOnly(2026, 2, 1), r.To);
    }

    [Theory]
    [InlineData("2025-01-19 23:00:00", 2025, 1, 20)]   // 750041 Lohnsatz
    [InlineData("2025-01-20 22:59:59", 2025, 1, 20)]   // 750041 Vertrag
    [InlineData("2026-10-30 23:00:00", 2026, 10, 31)]  // 580101 Lohnsatz
    [InlineData("2026-10-31 22:59:59", 2026, 10, 31)]  // 580101 Vertrag
    [InlineData("2025-02-09 22:59:59", 2025, 2, 9)]    // 750006 Vertrag
    [InlineData("2025-02-09 23:00:00", 2025, 2, 10)]   // 750006 Lohnsatz
    public void Belege(string roh, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), new EawContract { ToRaw = roh }.To);
        Assert.Equal(new DateOnly(y, m, d), new EawPayRate  { ToRaw = roh }.To);
    }

    [Fact]
    public void Bis_Leer_HeisstKeinEnde()
        => Assert.Null(new EawPayRate { ToRaw = null }.To);

    [Fact]
    public void Beginn_BleibtDerKalendertag()
    {
        // Sommerzeit (UTC+2): 22:00 des Vortags = Zürich 00:00 des Folgetags.
        var r = new EawPayRate { FromRaw = "2021-06-20 22:00:00" };
        Assert.Equal(new DateOnly(2021, 6, 21), r.From);
        // Winterzeit (UTC+1): 23:00 des Vortags.
        var c = new EawContract { FromRaw = "2024-12-31 23:00:00" };
        Assert.Equal(new DateOnly(2025, 1, 1), c.From);
    }

    [Fact]
    public void AnschlussSegmente_Luckenlos()
    {
        // Vertrag 1 endet, Vertrag 2 beginnt am Folgetag — ohne Überlappung.
        var alt = new EawContract { ToRaw   = "2022-12-20 22:59:59" };
        var neu = new EawContract { FromRaw = "2022-12-20 23:00:00" };
        Assert.Equal(new DateOnly(2022, 12, 20), alt.To);
        Assert.Equal(new DateOnly(2022, 12, 21), neu.From);
    }
}
