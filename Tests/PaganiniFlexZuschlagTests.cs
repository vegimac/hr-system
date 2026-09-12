using HrSystem.Services;
using Xunit;
using static HrSystem.Services.PayrollCalculations;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec TF02 Paganini Jan 2025: Ferien 13.04 % ab 60, Lektionen in derselben
/// %-Kaskade wie Stunden (eine Zeile, Summe = CSV 1160+1162 / 1161+1163 / 1201+1202).
/// Schicht 90 nicht in der 13.-ML-Basis.
/// </summary>
public class PaganiniFlexZuschlagTests
{
    [Fact]
    public void FerienUndFeiertag_StundenPlusLektionen_SummeGleichCsv()
    {
        const decimal ferienPct = 13.04m;
        const decimal feierPct  = 4.00m;
        decimal ferienStunden   = Round05(4500m * ferienPct / 100m);
        decimal ferienLektionen = Round05(600m  * ferienPct / 100m);
        decimal feierStunden    = Round05(4500m * feierPct  / 100m);
        decimal feierLektionen  = Round05(600m  * feierPct  / 100m);
        Assert.Equal(586.80m, ferienStunden);
        Assert.Equal(78.25m,  ferienLektionen);
        Assert.Equal(180.00m, feierStunden);
        Assert.Equal(24.00m,  feierLektionen);

        decimal ferienKombi = Round05(5100m * ferienPct / 100m);
        decimal feierKombi  = Round05(5100m * feierPct  / 100m);
        Assert.Equal(ferienStunden + ferienLektionen, ferienKombi);
        Assert.Equal(feierStunden + feierLektionen, feierKombi);
    }

    [Fact]
    public void Dreizehnter_OhneSchicht_SummeGleichCsv()
    {
        const decimal pct13 = 8.33m;
        decimal basisStunden   = 4500m + 586.80m + 180m;
        decimal basisLektionen = 600m + 78.25m + 24m;
        decimal ml13Stunden    = Round05(basisStunden * pct13 / 100m);
        decimal ml13Lektionen  = Round05(basisLektionen * pct13 / 100m);
        Assert.Equal(438.70m, ml13Stunden);
        Assert.Equal(58.50m,  ml13Lektionen);

        decimal basisKombi = 4500m + 600m + 665.05m + 204m;
        Assert.Equal(5969.05m, basisKombi);
        Assert.Equal(ml13Stunden + ml13Lektionen, Round05(basisKombi * pct13 / 100m));
    }
}
