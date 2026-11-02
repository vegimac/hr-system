using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Schritt 5b: BVG-Fixbetrag (Lohnart 5050) bei Eintritt Monatsmitte.
/// Anlass Walter 02.11.2026 (TF37 Oberli, Eintritt 16.11.2024, BVG 800.00): Quality Tool
/// meldete BVG-LPP-RegularContribution 0.00 statt −800.00. 5b suchte nur einen Eintrag
/// genau am 01.11., legte einen zweiten ab 01.11. an, und die Engine nahm den jüngeren
/// vom 16.11. mit Fix 0.
/// </summary>
public class SwissdecBvgFixEintrittTests
{
    private static DateOnly D(int j, int m, int t) => new(j, m, t);

    private static EmployeeVersicherungCode Bvg(int id, DateOnly ab, string? code, decimal? fix, string bemerkung = "Swissdec-Testdaten")
        => new() { Id = id, Art = "BVG", ValidFrom = ab, Code = code, BeitragFixAn = fix, BeitragFixAg = fix, Bemerkung = bemerkung };

    [Fact]
    public void EintrittMonatsmitte_EintrittsEintragBekommtDenBetrag()
    {
        var eintritt = Bvg(1, D(2024, 11, 16), "11", 0m);
        var (ziel, weg) = SwissdecTestmandantController.BvgFixZielImMonat(new[] { eintritt }, D(2024, 11, 1));
        Assert.Same(eintritt, ziel);
        Assert.Empty(weg);
    }

    [Fact]
    public void AlteFehlzeileAbDemErsten_WirdEntfernt_EngineNimmtDenEintrittsEintrag()
    {
        // Stand nach dem fehlerhaften 5b-Lauf: zwei Einträge im November.
        var fehl = Bvg(2, D(2024, 11, 1), null, 800m, "Swissdec-Testdaten (Lohnart 5050)");
        var eintritt = Bvg(1, D(2024, 11, 16), "11", 0m);
        var (ziel, weg) = SwissdecTestmandantController.BvgFixZielImMonat(new[] { fehl, eintritt }, D(2024, 11, 1));
        Assert.Same(eintritt, ziel);
        Assert.Equal(new[] { fehl }, weg);

        ziel!.BeitragFixAn = 800m;
        var gewaehlt = PayrollCalculations.WaehleBvgFix(new[] { eintritt });
        Assert.Equal(800m, gewaehlt!.BeitragFixAn);
        Assert.Equal("11", gewaehlt.Code);
    }

    [Fact]
    public void EintragAmErsten_WieBisher()
    {
        var a = Bvg(1, D(2024, 12, 1), "11", 800m);
        var b = Bvg(2, D(2024, 12, 1), "11", 800m, "Swissdec-Testdaten (Lohnart 5050)");
        var (ziel, weg) = SwissdecTestmandantController.BvgFixZielImMonat(new[] { a, b }, D(2024, 12, 1));
        Assert.Same(b, ziel);
        Assert.Equal(new[] { a }, weg);
    }

    [Fact]
    public void KeinEintragImMonat_NeuerEintragNoetig()
    {
        var vorher = Bvg(1, D(2024, 11, 16), "11", 800m);
        var (ziel, weg) = SwissdecTestmandantController.BvgFixZielImMonat(new[] { vorher }, D(2025, 1, 1));
        Assert.Null(ziel);
        Assert.Empty(weg);
    }
}
