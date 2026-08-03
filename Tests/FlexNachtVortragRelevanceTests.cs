using HrSystem.Controllers;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter 02.08.2026: FLEX führt Nacht-Saldo (Zeitzuschlag) — Monatsblatt-Vortrag
/// 904 darf für FLEX nicht mehr übersprungen werden (Fall Miteva Angela 0.41 h).
/// </summary>
public class FlexNachtVortragRelevanceTests
{
    [Theory]
    [InlineData("FLEX")]
    [InlineData("UTP")]   // Legacy-Alias
    [InlineData("MTP")]
    [InlineData("FIX")]
    [InlineData("FIX-M")]
    public void NachtVortrag_904_ist_fuer_alle_Modelle_relevant(string model)
    {
        Assert.True(SaldoVortragImportController.IsNachtVortragRelevantForModel(model));
        Assert.True(SaldoVortragController.IsVortragRelevantForModel("904", model));
    }

    [Fact]
    public void Zeitsaldo_901_bleibt_fuer_FLEX_irrelevant()
    {
        Assert.False(SaldoVortragController.IsVortragRelevantForModel("901", "FLEX"));
        Assert.True(SaldoVortragController.IsVortragRelevantForModel("901", "MTP"));
    }
}
