using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// UVG-Höchstlohn kumuliert über alle UVG-versicherten Monate (Quality Tool Okt 2025,
/// RETROSPECTIVE UVG-LAA-ContributorySalary): Codewechsel A2 → A1 rollt weiter auf,
/// Monate mit A0 zählen nicht.
/// </summary>
public class UvgKumulationTests
{
    private static EmployeeVersicherungCode Code(string c, int vonM, int? bisM)
        => new() { Art = "UVG", Code = c, ValidFrom = new DateOnly(2025, vonM, 1),
                   ValidTo = bisM is int b ? new DateOnly(2025, b, 1).AddMonths(1).AddDays(-1) : null };

    [Fact]
    public void Farine_A0_Maerz_zaehlt_nicht()
    {
        var m = PayrollCalculations.UvgVersicherteMonate(new[] { Code("A1", 1, 2), Code("A0", 3, 9), Code("A1", 10, null) }, 2025)!;
        Assert.Contains(1, m); Assert.Contains(2, m); Assert.DoesNotContain(3, m); Assert.Contains(10, m);
    }

    [Fact]
    public void Casanova_A2_und_A1_beide_versichert()
    {
        var m = PayrollCalculations.UvgVersicherteMonate(new[] { Code("A2", 2, 9), Code("A1", 10, null) }, 2025)!;
        Assert.All(new[] { 2, 3, 9, 10 }, x => Assert.Contains(x, m));
    }

    [Fact]
    public void Ohne_Codes_null_wie_bisher()
        => Assert.Null(PayrollCalculations.UvgVersicherteMonate(Array.Empty<EmployeeVersicherungCode>(), 2025));

    [Fact]
    public void Casanova_Oktober_7303_35()
    {
        // Höchstlohn 12'350/Mt.; 27.02.–30.09. = 7 4/30 Monate, Oktober dazu.
        decimal bisher = 7m + 4m / 30m, total = bisher + 1m;
        decimal kumBisher = Math.Min(94400m, 12350m * bisher);
        decimal kumTotal = Math.Min(94400m + 1000m, 12350m * total);
        Assert.Equal(7303.33m, Math.Round(kumTotal - kumBisher, 2));
        Assert.Equal(117.30m, PayrollCalculations.Round05(Math.Round(kumTotal - kumBisher, 2) * 1.606m / 100m));
    }
}
