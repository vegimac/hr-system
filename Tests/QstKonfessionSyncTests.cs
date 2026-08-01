using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter 01.08.2026: Konfession → QST-Code-Buchstabe Y/N.
/// </summary>
public class QstKonfessionSyncTests
{
    [Theory]
    [InlineData("C", 2, true,  "C2N", "C2Y")]
    [InlineData("C", 2, false, "C2Y", "C2N")]
    [InlineData("A", 0, true,  "A0N", "A0Y")]
    [InlineData(null, 2, true, "C2N", "C2Y")] // Tarif aus bisherigem Code
    public void RebuildQstCode_FlipsKircheLetter(
        string? tarif, int kinder, bool kirche, string previous, string expected)
    {
        var code = QstKonfessionSyncService.RebuildQstCode(tarif, kinder, kirche, previous);
        Assert.Equal(expected, code);
    }

    [Fact]
    public void ChristKatholisch_IstKirchensteuerPflichtig()
    {
        Assert.True(QstTarifVorschlagLogic.IstKirchensteuerPflichtig("christ_katholisch"));
        Assert.True(QstTarifVorschlagLogic.IstKirchensteuerPflichtig("Christ-katholisch"));
        Assert.False(QstTarifVorschlagLogic.IstKirchensteuerPflichtig("keine"));
        Assert.False(QstTarifVorschlagLogic.IstKirchensteuerPflichtig("andere"));
    }
}
