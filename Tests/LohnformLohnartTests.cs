using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec-Lohnart nach Lohnform (Walter 02.11.2026): Stundenteil 1160/1161/1201,
/// Lektionenteil 1162/1163/1202 — je eine Zeile. Soll = Swissdec-Testdaten TF02 Paganini Juni 2025
/// (100 h × 30 = 3'000, 40 Lektionen × 30 = 1'200, Ferien 13.04 %, Feiertag 4 %, 13. ML 8.33 %).
/// </summary>
public class LohnformLohnartTests
{
    [Fact]
    public void Ferien_PaganiniJuni()
    {
        var t = PayrollCalculations.TeileNachLohnform(4200m, 1200m, 13.04m, "1160", "1162");
        Assert.Equal(new[] { ("1160", 391.20m), ("1162", 156.50m) }, t.Select(x => (x.Code, x.Betrag)));
    }

    [Fact]
    public void Feiertag_PaganiniJuni()
    {
        var t = PayrollCalculations.TeileNachLohnform(4200m, 1200m, 4m, "1161", "1163");
        Assert.Equal(new[] { ("1161", 120.00m), ("1163", 48.00m) }, t.Select(x => (x.Code, x.Betrag)));
    }

    [Fact]
    public void Dreizehnter_PaganiniJuni()
    {
        // Basis inkl. Ferien und Feiertag: Stunden 3'511.20, Lektionen 1'404.50
        var t = PayrollCalculations.TeileNachLohnform(3511.20m + 1404.50m, 1404.50m, 8.33m, "1201", "1202");
        Assert.Equal(new[] { ("1201", 292.50m), ("1202", 117.00m) }, t.Select(x => (x.Code, x.Betrag)));
    }

    [Fact]
    public void NurStunden_EineZeile()
    {
        var t = PayrollCalculations.TeileNachLohnform(9548.05m, 0m, 8.33m, "1201", "1202");   // TF01 Herz März
        Assert.Single(t);
        Assert.Equal(("1201", 795.35m), (t[0].Code, t[0].Betrag));
    }

    [Fact]
    public void NurLektionen_EineZeile()
    {
        var t = PayrollCalculations.TeileNachLohnform(2800.35m, 2800.35m, 8.33m, "1201", "1202");
        Assert.Single(t);
        Assert.Equal("1202", t[0].Code);
    }
}
