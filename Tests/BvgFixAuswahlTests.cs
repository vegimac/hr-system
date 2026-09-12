using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Bei Dubletten (Schritt 4c kopiert den Januar-Fix, Schritt 5 schreibt 5050)
/// muss der Lohnlauf den 5050-Betrag nehmen — TF22 Bucher Februar 2025.
/// </summary>
public class BvgFixAuswahlTests
{
    [Fact]
    public void WaehleBvgFix_BevorzugtLohnart5050_BeiGleichemAb()
    {
        var mutation = Zeile(1, new DateOnly(2025, 2, 1), 151.66m, "Swissdec-Testdaten Mutation");
        var csv = Zeile(2, new DateOnly(2025, 2, 1), 320.83m, "Swissdec-Testdaten (Lohnart 5050)");

        var gewaehlt = PayrollCalculations.WaehleBvgFix(new[] { mutation, csv });

        Assert.NotNull(gewaehlt);
        Assert.Equal(320.83m, gewaehlt!.BeitragFixAn);
        Assert.Equal(2, gewaehlt.Id);
    }

    [Fact]
    public void WaehleBvgFix_NimmtJuengeresAb_NichtDenJanuar()
    {
        var jan = Zeile(1, new DateOnly(2025, 1, 1), 151.66m, "Swissdec-Testdaten (Lohnart 5050)");
        jan.ValidTo = new DateOnly(2025, 1, 31);
        var feb = Zeile(2, new DateOnly(2025, 2, 1), 320.83m, "Swissdec-Testdaten (Lohnart 5050)");
        feb.ValidTo = new DateOnly(2025, 6, 30);

        var stichtag = new DateOnly(2025, 2, 1);
        var amStichtag = new[] { jan, feb }.Where(e => e.GiltAm(stichtag));
        var gewaehlt = PayrollCalculations.WaehleBvgFix(amStichtag);

        Assert.Equal(320.83m, gewaehlt!.BeitragFixAn);
    }

    private static EmployeeVersicherungCode Zeile(int id, DateOnly von, decimal fix, string bemerkung) => new()
    {
        Id = id, EmployeeId = 22, Art = "BVG", Code = "22",
        ValidFrom = von, BeitragFixAn = fix, BeitragFixAg = fix,
        Bemerkung = bemerkung, CreatedAt = DateTime.Now,
    };
}
