using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Quality Tool Februar 2025 (Walter 02.11.2026): Satz-Lohn Jahresmodell TI,
/// Grunddaten im Eintrittsmonat (Schritt 5b), Pensum aus Wochenlektionen.
/// </summary>
public class SwissdecFebruar2025Tests
{
    [Theory]
    // TF23 Koller / TF35 Roos: 2 × 5'000 periodisch, Bonus 30'000 → 7'500
    [InlineData(10000, 60, 30000, 7500.00)]
    // TF41 Meier Max: 2 × 10'000, Gratifikation 20'000 → 11'666.65
    [InlineData(20000, 60, 20000, 11666.65)]
    // TF21 Meier Christian (40 % hochgerechnet 5'000), Sonderzulage 4'500 → 5'375
    [InlineData(10000, 60, 4500, 5375.00)]
    // TF22 Bucher: Jan 5'111 + Feb 4'717.85 → 4'914.45
    [InlineData(9828.85, 60, 0, 4914.45)]
    public void SatzLohnJahresmodell_WieQualityTool(decimal ytdPeriodisch, int qstTage, decimal ytdAperiodisch, decimal erwartet)
        => Assert.Equal(erwartet, QstJahresmodell.SatzLohnAusTagen(ytdPeriodisch, qstTage, ytdAperiodisch));

    [Fact]
    public void Grunddaten_GeltenImEintrittsmonat()
    {
        // TF25 Lehmann: Arbeitstage CH 10 ab Eintritt 10.02.2025, Mutation 9 ab März
        var reihen = new Dictionary<string, SortedDictionary<DateOnly, decimal>>
        {
            ["TF25"] = new() { [new DateOnly(2025, 2, 10)] = 10m, [new DateOnly(2025, 3, 1)] = 9m },
        };
        Assert.Null(SwissdecTestmandantController.WertImMonat(reihen, "TF25", new DateOnly(2025, 1, 1)));
        Assert.Equal(10m, SwissdecTestmandantController.WertImMonat(reihen, "TF25", new DateOnly(2025, 2, 1)));
        Assert.Equal(9m, SwissdecTestmandantController.WertImMonat(reihen, "TF25", new DateOnly(2025, 3, 1)));
    }

    [Fact]
    public void Lektionen_OhnePensum_AusArbeitszeitmodell()
    {
        // TF15 Degelo: FLEX, 10.5 Wochenlektionen, Modell 21 Lektionen → 50 %
        var em = new Employment { EmploymentModel = "FLEX", WeeklyLessons = 10.5m };
        var x = ElmGemeinsam.WorkingTime(em, 42m, vollzeitWochenlektionen: 21m);
        Assert.Equal("50.00", x.Descendants().First(d => d.Name.LocalName == "ActivityRate").Value);
    }

    [Fact]
    public void Lektionen_MitPensum_PensumGewinnt()
    {
        var em = new Employment { EmploymentModel = "FLEX", WeeklyLessons = 10.5m, EmploymentPercentage = 60m };
        var x = ElmGemeinsam.WorkingTime(em, 42m, vollzeitWochenlektionen: 21m);
        Assert.Equal("60.00", x.Descendants().First(d => d.Name.LocalName == "ActivityRate").Value);
    }
}
