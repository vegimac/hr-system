using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class WohnortErfahrenTests
{
    static readonly List<EmployeeWohnortHistory> Verlauf = new()
    {
        new() { Id = 1, Ort = "Bern" },
        new() { Id = 2, Ort = "Como", GueltigAb = new DateOnly(2025, 8, 29), ErfahrenAm = new DateOnly(2025, 9, 1) },
    };

    [Fact]
    public void Umzug_zaehlt_erst_ab_dem_Tag_an_dem_er_bekannt_ist()
    {
        // Referenz 2025-08/-09: TF36 Maldini, Wegzug 28.08., in der Meldung erst ab September.
        Assert.Equal("Bern", WohnortHistorieService.Am(Verlauf, new DateOnly(2025, 8, 31), null, null, null, null, null).Ort);
        Assert.Null(WohnortHistorieService.UmzugImMonat(Verlauf, new DateOnly(2024, 9, 1), new DateOnly(2025, 8, 31)));
        Assert.Equal("Como", WohnortHistorieService.Am(Verlauf, new DateOnly(2025, 9, 30), null, null, null, null, null).Ort);
        var umzug = WohnortHistorieService.UmzugImMonat(Verlauf, new DateOnly(2024, 10, 1), new DateOnly(2025, 9, 30));
        Assert.Equal(new DateOnly(2025, 8, 28), umzug!.Value.Wegzug);
    }
}
