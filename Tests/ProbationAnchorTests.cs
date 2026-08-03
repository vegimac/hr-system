using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter 02.08.2026: Probezeit ab erster Stempelzeit ≥ Eintritt.
/// </summary>
public class ProbationAnchorTests
{
    [Fact]
    public void ReferenceStart_bevorzugt_Eintritt()
    {
        var entry = new DateOnly(2026, 7, 27);
        var contract = new DateOnly(2026, 7, 28);
        Assert.Equal(entry, ProbationAnchor.ReferenceStart(entry, contract));
        Assert.Equal(contract, ProbationAnchor.ReferenceStart(null, contract));
    }

    [Fact]
    public void ComputeEnd_14_Tage_inkl_letzten_Tag()
    {
        // 27.7. + 14 Tage − 1 = 9.8.
        Assert.Equal(new DateOnly(2026, 8, 9),
            ProbationAnchor.ComputeEnd(new DateOnly(2026, 7, 27), 14));
    }

    [Fact]
    public void Delta_erste_Stempel_nach_Eintritt_verschiebt_Ende()
    {
        var entry = new DateOnly(2026, 7, 27);
        var firstStamp = new DateOnly(2026, 7, 29);
        Assert.Equal(2, ProbationAnchor.Delta(entry, firstStamp));
        var provisorisch = ProbationAnchor.ComputeEnd(entry, 14);
        Assert.Equal(new DateOnly(2026, 8, 11), provisorisch.AddDays(2));
    }
}
