using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter-Bug 03.10.2026: Pelagia Pantouveri (easy@work Nr. 1220052) wurde über die
/// Alias-Nummer 1220052 auf Sahra Dschafaris Datensatz geschrieben. Der Import darf
/// bei offensichtlich anderer Person nie überschreiben.
/// </summary>
public class EasyAtWorkAnderePersonTests
{
    [Fact]
    public void PelagiaAufSahra_IstAnderePerson()
        => Assert.True(EasyAtWorkEmployeeSyncService.IstAnderePerson(
            "Sahra", "Dschafari", new DateTime(1983, 12, 22),
            "Pelagia", "Pantouveri", new DateOnly(1982, 6, 30)));

    [Fact]
    public void Heirat_NurNachnameNeu_GleichePerson()
        => Assert.False(EasyAtWorkEmployeeSyncService.IstAnderePerson(
            "Sahra", "Dschafari", new DateTime(1983, 12, 22),
            "Sahra", "Muster", new DateOnly(1983, 12, 22)));

    [Fact]
    public void BeideNamenAnders_GleicherGeburtstag_GleichePerson()
        => Assert.False(EasyAtWorkEmployeeSyncService.IstAnderePerson(
            "Zahra", "Dschafary", new DateTime(1983, 12, 22),
            "Sahra", "Dschafari", new DateOnly(1983, 12, 22)));

    [Fact]
    public void GrossKleinUndBindestrich_GleichePerson()
        => Assert.False(EasyAtWorkEmployeeSyncService.IstAnderePerson(
            "Anna-Lena", "MEIER", null,
            "anna lena", "Meier", null));

    [Fact]
    public void Sync_AndererDatensatz_GleicherNameUndGeburtstag_Verboten()
        => Assert.True(EasyAtWorkEmployeeSyncService.IstZweiterEasyDatensatz(
            100, "580108", "Gazele", "Jemmo", new DateTime(1999, 1, 1),
            200, "580999", "Gazele", "Jemmo", new DateOnly(1999, 1, 1)));

    [Fact]
    public void Sync_GleicheBadge_IdReparatur_Erlaubt()
        => Assert.False(EasyAtWorkEmployeeSyncService.IstZweiterEasyDatensatz(
            100, "580108alt", "Gazele", "Jemmo", new DateTime(1999, 1, 1),
            200, "580108", "Gazele", "Jemmo", new DateOnly(1999, 1, 1)));

    [Fact]
    public void Sync_GleicheEasyId_Erlaubt()
        => Assert.False(EasyAtWorkEmployeeSyncService.IstZweiterEasyDatensatz(
            100, "580108", "Gazele", "Jemmo", new DateTime(1999, 1, 1),
            100, "580999", "Gazele", "Jemmo", new DateOnly(1999, 1, 1)));

    [Fact]
    public void BeideNamenAnders_OhneGeburtstag_AnderePerson()
        => Assert.True(EasyAtWorkEmployeeSyncService.IstAnderePerson(
            "Sahra", "Dschafari", null,
            "Pelagia", "Pantouveri", null));
}
