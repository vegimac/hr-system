using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Angezeigte 13.-ML-Basis kommt aus den BELEG-Beträgen, nicht aus den exakten
/// Produkten (Walter 26.09.2026, Muster AG TF14 Egli August 2025):
/// 170 h × 25.00 = 4'250.00, Ferienentschädigung 8.33 % = 354.025 → Beleg 354.05,
/// Feiertagentschädigung 4 % = 170.00. Basis muss 4'774.05 zeigen (nicht 4'774.02);
/// der Betrag 397.70 bleibt, weil er weiter auf der exakten Basis gerechnet wird.
/// </summary>
public class Dreizehnter13BasisAnzeigeTests
{
    private const decimal Ferienpct = 8.33m, Feiertagpct = 4m, Dreizehntelpct = 8.33m;

    [Fact]
    public void Egli_August_BasisAusBelegbetraegen()
    {
        decimal stundenlohn   = 170m * 25m;                       // 4'250.00 (exakt = Beleg)
        decimal ferienExact   = stundenlohn * Ferienpct / 100m;   // 354.025
        decimal ferienBeleg   = PayrollCalculations.Round05(ferienExact);
        decimal feiertagExact = stundenlohn * Feiertagpct / 100m; // 170.00
        decimal feiertagBeleg = PayrollCalculations.Round05(feiertagExact);

        Assert.Equal(354.05m, ferienBeleg);
        Assert.Equal(170.00m, feiertagBeleg);

        decimal basisExact   = stundenlohn + ferienExact + feiertagExact;   // 4'774.025
        decimal basisAnzeige = stundenlohn + ferienBeleg + feiertagBeleg;   // 4'774.05

        Assert.Equal(4774.05m, decimal.Round(basisAnzeige, 2));
        Assert.Equal(4774.02m, decimal.Round(basisExact, 2));   // alter (falscher) Anzeigewert

        // Betrag unverändert: gerechnet wird weiter auf der exakten Basis.
        Assert.Equal(397.70m, PayrollCalculations.Round05(basisExact * Dreizehntelpct / 100m));
        // Auch aus der Anzeige-Basis käme derselbe Betrag — die Zeile bleibt in sich schlüssig.
        Assert.Equal(397.70m, PayrollCalculations.Round05(basisAnzeige * Dreizehntelpct / 100m));
    }

    [Fact]
    public void OhneRundungsdifferenz_BleibtDieBasisGleich()
    {
        // Glatte Beträge (Festlohn 5'000, Feiertag 4 % = 200): Beleg = exakt,
        // die Anzeige-Basis ändert sich durch die Umstellung nicht.
        decimal festlohn      = 5000m;
        decimal feiertagExact = festlohn * Feiertagpct / 100m;   // 200.00
        Assert.Equal(feiertagExact, PayrollCalculations.Round05(feiertagExact));
        Assert.Equal(festlohn + feiertagExact,
                     festlohn + PayrollCalculations.Round05(feiertagExact));
    }
}
