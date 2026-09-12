using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// AHV 21: Beitragsende = Folgemonat des Referenzalters, nicht 1.1. des
/// Kalenderjahres. TF05 Moser (m, 15.04.1960) darf im Januar 2025 nicht
/// als 65+ laufen — sonst AHV-Freibetrag und kein ALV (Walter 12.09.2026).
/// </summary>
public class AhvReferenzalterTests
{
    [Theory]
    [InlineData("male", 1960, 4, 15, 2025, 1, false)]
    [InlineData("male", 1960, 4, 15, 2025, 4, false)]
    [InlineData("male", 1960, 4, 15, 2025, 5, true)]
    [InlineData("female", 1960, 12, 16, 2024, 12, false)]
    [InlineData("female", 1960, 12, 16, 2025, 1, true)]
    public void Folgemonat_DesReferenzalters(
        string gender, int y, int m, int d, int jahr, int monat, bool erwartet)
    {
        var geb = new DateTime(y, m, d);
        Assert.Equal(erwartet, PayrollCalculations.HatReferenzalterErreicht(gender, geb, jahr, monat));
    }

    [Fact]
    public void Satzalter_Moser_JanNoch64()
    {
        var geb = new DateTime(1960, 4, 15);
        const int jahr = 2025;
        var kalender = jahr - geb.Year;
        Assert.Equal(65, kalender);
        Assert.False(PayrollCalculations.HatReferenzalterErreicht("male", geb, 2025, 1));
        Assert.Equal(64, PayrollCalculations.EffectiveAgeFuerSvSaetze(kalender, false));
    }

    [Fact]
    public void Satzalter_Moser_AbMai65()
    {
        var geb = new DateTime(1960, 4, 15);
        Assert.True(PayrollCalculations.HatReferenzalterErreicht("male", geb, 2025, 5));
        Assert.Equal(65, PayrollCalculations.EffectiveAgeFuerSvSaetze(2025 - 1960, true));
    }

    [Fact]
    public void Satzalter_Jugend_Unveraendert()
    {
        Assert.Equal(17, PayrollCalculations.EffectiveAgeFuerSvSaetze(17, false));
        Assert.Equal(18, PayrollCalculations.EffectiveAgeFuerSvSaetze(18, false));
    }
}
