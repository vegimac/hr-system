using System;
using HrSystem.Controllers;
using Xunit;
using V = HrSystem.Controllers.LohnFerienAuszahlungController.Vertrag;

namespace HrSystem.Tests;

/// <summary>
/// Ferien-Auszahlung im Lohnlauf (Walter 11.10.2026): das Datum ergibt sich aus der
/// Lohnperiode und muss in der Engine derselben Filiale zugeordnet werden
/// (jüngster Vertrag mit Start ≤ Datum).
/// </summary>
public class LohnFerienAuszahlungDatumTests
{
    [Fact]
    public void EinVertrag_Monatsende()
    {
        var d = LohnFerienAuszahlungController.BuchungsDatum(
            new[] { new V(new DateTime(2025, 9, 1), 2) }, 2, 2026, 1);
        Assert.Equal(new DateOnly(2026, 1, 31), d);
    }

    [Fact]
    public void UebertrittMitteMonat_AlteFiliale_LetzterTagVorWechsel()
    {
        var v = new[] { new V(new DateTime(2024, 1, 1), 2), new V(new DateTime(2026, 1, 16), 5) };
        Assert.Equal(new DateOnly(2026, 1, 15), LohnFerienAuszahlungController.BuchungsDatum(v, 2, 2026, 1));
        Assert.Equal(new DateOnly(2026, 1, 31), LohnFerienAuszahlungController.BuchungsDatum(v, 5, 2026, 1));
    }

    [Fact]
    public void OhneVertrag_Monatsende()
    {
        Assert.Equal(new DateOnly(2026, 2, 28),
            LohnFerienAuszahlungController.BuchungsDatum(Array.Empty<V>(), 2, 2026, 2));
    }
}
