using HrSystem.Controllers;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Wiederholter Testmandant-Schritt 4a darf einen Austritt aus 4c nicht überschreiben
/// (Walter 28.09.2026: Aebi-Vertrag endete danach am 14.01.2025 statt 20.12.2024,
/// Burri-Vertrag ganz ohne Ende → Dezember-Meldung ohne WithdrawalDate, ALV/NBU ohne
/// anteiligen Höchstlohn).
/// </summary>
public class Testmandant4aVertragsendeTests
{
    private static DateTime D(int j, int m, int t) => new(j, m, t);

    [Fact]
    public void Austritt_vor_Wiedereintritt_bleibt()
        => Assert.Equal(D(2024, 12, 20),
            SwissdecTestmandantController.VertragsEndeBeiWiederholung(D(2024, 12, 20), D(2025, 1, 15)));

    [Fact]
    public void Austritt_ohne_naechsten_Abschnitt_bleibt()
        => Assert.Equal(D(2024, 12, 31),
            SwissdecTestmandantController.VertragsEndeBeiWiederholung(D(2024, 12, 31), null));

    [Fact]
    public void Ohne_Ende_endet_am_Vortag_des_naechsten()
        => Assert.Equal(D(2025, 1, 14),
            SwissdecTestmandantController.VertragsEndeBeiWiederholung(null, D(2025, 1, 15)));

    [Fact]
    public void Ueberlappendes_Ende_wird_gekuerzt()
        => Assert.Equal(D(2025, 1, 14),
            SwissdecTestmandantController.VertragsEndeBeiWiederholung(D(2025, 3, 27), D(2025, 1, 15)));

    [Fact]
    public void Neuer_Vertrag_ohne_Folgeabschnitt_bleibt_offen()
        => Assert.Null(SwissdecTestmandantController.VertragsEndeBeiWiederholung(null, null));
}
