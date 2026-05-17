using HrSystem.Models;
using HrSystem.Services;

namespace hr_system.Tests;

/// <summary>
/// Tests für KarenzService.ComputeKarenzjahr — die reine Logik
/// zur Bestimmung des Karenzjahr-Zeitraums (ohne Datenbank).
/// </summary>
public class KarenzServiceTests
{
    private readonly KarenzService _service;

    public KarenzServiceTests()
    {
        // KarenzService braucht einen DbContext, aber ComputeKarenzjahr ist
        // eine reine Logik-Methode die nur Employee + CompanyProfile liest.
        // Wir übergeben null — die Methode greift nicht auf die DB zu.
        _service = new KarenzService(null!);
    }

    private static Employee MakeEmployee(DateTime entryDate) => new()
    {
        Id = 1,
        FirstName = "Test",
        LastName = "Mitarbeiter",
        EntryDate = entryDate
    };

    private static CompanyProfile MakeProfile(string karenzjahrBasis = "ARBEITSJAHR") => new()
    {
        Id = 1,
        CompanyName = "Test GmbH",
        KarenzjahrBasis = karenzjahrBasis,
        KarenzTageMax = 14m,
        KarenzTageMaxUnfall = 2m
    };

    // ── KALENDERJAHR ─────────────────────────────────────────────────────────

    [Fact]
    public void Kalenderjahr_ImmerJanuar1BisDezember31()
    {
        var employee = MakeEmployee(new DateTime(2020, 6, 15));
        var profile = MakeProfile("KALENDERJAHR");

        var (von, bis) = _service.ComputeKarenzjahr(
            new DateOnly(2026, 8, 20), employee, profile);

        Assert.Equal(new DateOnly(2026, 1, 1), von);
        Assert.Equal(new DateOnly(2026, 12, 31), bis);
    }

    // ── ARBEITSJAHR ──────────────────────────────────────────────────────────

    [Fact]
    public void Arbeitsjahr_NachAnniversary()
    {
        // Eintritt 1.3.2020, Datum 15.5.2026
        // → Anniversary 1.3.2026 liegt vor Datum → Karenzjahr 1.3.2026 – 28.2.2027
        var employee = MakeEmployee(new DateTime(2020, 3, 1));
        var profile = MakeProfile("ARBEITSJAHR");

        var (von, bis) = _service.ComputeKarenzjahr(
            new DateOnly(2026, 5, 15), employee, profile);

        Assert.Equal(new DateOnly(2026, 3, 1), von);
        Assert.Equal(new DateOnly(2027, 2, 28), bis);
    }

    [Fact]
    public void Arbeitsjahr_VorAnniversary()
    {
        // Eintritt 1.9.2021, Datum 15.3.2026
        // → Anniversary 1.9.2026 liegt NACH Datum → Karenzjahr 1.9.2025 – 31.8.2026
        var employee = MakeEmployee(new DateTime(2021, 9, 1));
        var profile = MakeProfile("ARBEITSJAHR");

        var (von, bis) = _service.ComputeKarenzjahr(
            new DateOnly(2026, 3, 15), employee, profile);

        Assert.Equal(new DateOnly(2025, 9, 1), von);
        Assert.Equal(new DateOnly(2026, 8, 31), bis);
    }

    [Fact]
    public void Arbeitsjahr_AmAnniversary()
    {
        // Eintritt 15.4.2022, Datum genau am Anniversary 15.4.2026
        // → Karenzjahr 15.4.2026 – 14.4.2027
        var employee = MakeEmployee(new DateTime(2022, 4, 15));
        var profile = MakeProfile("ARBEITSJAHR");

        var (von, bis) = _service.ComputeKarenzjahr(
            new DateOnly(2026, 4, 15), employee, profile);

        Assert.Equal(new DateOnly(2026, 4, 15), von);
        Assert.Equal(new DateOnly(2027, 4, 14), bis);
    }

    [Fact]
    public void Arbeitsjahr_Schaltjahr_29Februar()
    {
        // Eintritt 29.2.2024 (Schaltjahr), Datum 15.5.2026 (kein Schaltjahr)
        // → Anniversary in 2026: 28.2. (da kein 29.2.) → Karenzjahr 28.2.2026 – 27.2.2027
        var employee = MakeEmployee(new DateTime(2024, 2, 29));
        var profile = MakeProfile("ARBEITSJAHR");

        var (von, bis) = _service.ComputeKarenzjahr(
            new DateOnly(2026, 5, 15), employee, profile);

        Assert.Equal(new DateOnly(2026, 2, 28), von);
        Assert.Equal(new DateOnly(2027, 2, 27), bis);
    }

    [Fact]
    public void Arbeitsjahr_OhneEintrittsdatum_FallbackKalenderjahr()
    {
        // Kein Eintrittsdatum → Fallback auf Kalenderjahr
        var employee = new Employee { Id = 1, FirstName = "X", LastName = "Y" };
        var profile = MakeProfile("ARBEITSJAHR");

        var (von, bis) = _service.ComputeKarenzjahr(
            new DateOnly(2026, 7, 10), employee, profile);

        Assert.Equal(new DateOnly(2026, 1, 1), von);
        Assert.Equal(new DateOnly(2026, 12, 31), bis);
    }
}
