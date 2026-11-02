using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// MTP ist ein Stundenlöhner (Walter 02.11.2026): gemeldet wird Stunden × Stundenlohn,
/// die garantierten Wochenstunden sind nur interne Stundenkontrolle.
/// </summary>
public class ElmMtpStundenlohnTests
{
    [Theory]
    [InlineData("FIX", true)]
    [InlineData("fix-m", true)]
    [InlineData("MTP", false)]
    [InlineData("FLEX", false)]
    [InlineData(null, false)]
    public void NurFixUndFixM_SindMonatslohn(string? modell, bool monatslohn)
        => Assert.Equal(monatslohn, ElmGemeinsam.IstMonatslohnModell(modell));

    [Fact]
    public void MtpMitFerienProzent_MeldetKeineFerientage()
    {
        var r = ElmStatistikCodes.FerienRegelFuer("MTP", null, null, new DateOnly(2025, 1, 31), 50, null, 10.64m);
        Assert.Equal(0m, r.Tage);
    }
}
