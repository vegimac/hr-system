using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// easy@work kennt die Funktion ohne Historie (Walter-Bug 10.10.2026, Fall 750017 Nikollaj):
/// ein nachträglich erfasster Lohnsatz ab 01.01.2026 erzeugte einen abgelaufenen Abschnitt mit
/// der heutigen Funktion SHIFT_LEADER_7_PLUS statt der damaligen SHIFT_LEADER_1_6.
/// </summary>
public class EasyAtWorkFunktionNeuerAbschnittTests
{
    private const int Sl16 = 11;
    private const int Sl7  = 12;

    [Fact]
    public void Abgelaufen_ErbtFunktionVomVorgaenger()
    {
        var vorher = new Employment { JobGroupId = Sl16, JobTitle = "SHIFT_LEADER_1_6" };
        var (id, titel) = EasyAtWorkEmployeeSyncService.FunktionFuerNeuenAbschnitt(
            aktiv: false, heuteJobGroupId: Sl7, heuteTitel: "SHIFT_LEADER_7_PLUS", vorgaenger: vorher);
        Assert.Equal(Sl16, id);
        Assert.Equal("SHIFT_LEADER_1_6", titel);
    }

    [Fact]
    public void Laufend_NimmtHeutigenEasyStand()
    {
        var vorher = new Employment { JobGroupId = Sl16, JobTitle = "SHIFT_LEADER_1_6" };
        var (id, titel) = EasyAtWorkEmployeeSyncService.FunktionFuerNeuenAbschnitt(
            aktiv: true, heuteJobGroupId: Sl7, heuteTitel: "SHIFT_LEADER_7_PLUS", vorgaenger: vorher);
        Assert.Equal(Sl7, id);
        Assert.Equal("SHIFT_LEADER_7_PLUS", titel);
    }

    [Fact]
    public void Abgelaufen_OhneVorgaengerFunktion_NimmtHeutigenStand()
    {
        var (id, _) = EasyAtWorkEmployeeSyncService.FunktionFuerNeuenAbschnitt(
            aktiv: false, heuteJobGroupId: Sl7, heuteTitel: "SHIFT_LEADER_7_PLUS",
            vorgaenger: new Employment { JobGroupId = null });
        Assert.Equal(Sl7, id);

        var (id2, _) = EasyAtWorkEmployeeSyncService.FunktionFuerNeuenAbschnitt(
            aktiv: false, heuteJobGroupId: Sl7, heuteTitel: "SHIFT_LEADER_7_PLUS", vorgaenger: null);
        Assert.Equal(Sl7, id2);
    }
}
