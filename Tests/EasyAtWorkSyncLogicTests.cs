using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die reine (DB-/HTTP-freie) Matching-Logik des easy@work-
/// Stempelzeiten-Sync (Walter-Vorgabe 18.06.2026):
///   - MA-Liste = inkl. inaktive, lokal nur Austritte VOR 2025-01-01 wegfiltern.
///   - Preflight: easy@work-MA mit Stempeln müssen auf eine Cowork-
///     Personalnummer (oder Alias) abbildbar sein, sonst Block.
///
/// Geprüft als Aufrufe von <see cref="EasyAtWorkTimepunchSyncService.FilterRelevantEmployees"/>
/// und <see cref="EasyAtWorkTimepunchSyncService.ComputePreflightMissing"/>.
/// </summary>
public class EasyAtWorkSyncLogicTests
{
    private static EawEmployee Emp(int id, string? number, DateOnly? from = null, DateOnly? to = null)
        => new() { Id = id, Number = number, FirstName = "Test", LastName = "MA #" + id, From = from, To = to };

    private static EawTimepunch Punch(int id, int employeeId, bool deleted = false, bool hasIn = true)
        => new()
        {
            Id = id,
            EmployeeId = employeeId,
            In  = hasIn ? new DateTime(2026, 2, 28, 14, 10, 0, DateTimeKind.Utc) : (DateTime?)null,
            Out = new DateTime(2026, 2, 28, 16, 48, 0, DateTimeKind.Utc),
            DeletedAt = deleted ? new DateTime(2026, 2, 28, 17, 0, 0, DateTimeKind.Utc) : (DateTime?)null,
        };

    // ─────────────────── FilterRelevantEmployees ────────────────────

    [Fact]
    public void MaStartetMittenImZeitraum_BleibtInDerListe()
    {
        // Eintritt am 16.02. (mitten in der Februar-Periode), kein Austritt.
        var emp = Emp(1, "580099", from: new DateOnly(2026, 2, 16), to: null);

        var filtered = EasyAtWorkTimepunchSyncService.FilterRelevantEmployees(new[] { emp });

        Assert.Contains(filtered, e => e.Id == 1);
    }

    [Fact]
    public void MaAusgetretenNach20250101_BleibtInDerListe()
    {
        var emp = Emp(2, "580050", to: new DateOnly(2025, 6, 30));

        var filtered = EasyAtWorkTimepunchSyncService.FilterRelevantEmployees(new[] { emp });

        Assert.Contains(filtered, e => e.Id == 2);
    }

    [Fact]
    public void MaAusgetretenVor20250101_WirdIgnoriert()
    {
        var emp = Emp(3, "580001", to: new DateOnly(2024, 12, 31));

        var filtered = EasyAtWorkTimepunchSyncService.FilterRelevantEmployees(new[] { emp });

        Assert.DoesNotContain(filtered, e => e.Id == 3);
    }

    [Fact]
    public void MaAusgetrittGenauAmStichtag_BleibtInDerListe()
    {
        // To == 2025-01-01 ist NICHT „< Stichtag" → bleibt drin (Randfall).
        var emp = Emp(4, "580002", to: new DateOnly(2025, 1, 1));

        var filtered = EasyAtWorkTimepunchSyncService.FilterRelevantEmployees(new[] { emp });

        Assert.Contains(filtered, e => e.Id == 4);
    }

    // ───────────────────── ComputePreflightMissing ──────────────────

    [Fact]
    public void SauberZugeordneterMa_IstNichtMissing()
    {
        var emp = Emp(10, "580099", from: new DateOnly(2026, 2, 16));
        var eawById = new Dictionary<int, EawEmployee> { [10] = emp };
        var cowork  = new HashSet<string> { "580099" };

        var missing = EasyAtWorkTimepunchSyncService.ComputePreflightMissing(
            new[] { Punch(1, 10) }, eawById, cowork, new Dictionary<int, int>());

        Assert.Empty(missing);
    }

    [Fact]
    public void FehlendePersonalnummer_BlockiertCommit()
    {
        // easy@work-MA ohne Nummer, hat aber Stempel → Block.
        var emp = Emp(20, number: null);
        var eawById = new Dictionary<int, EawEmployee> { [20] = emp };

        var missing = EasyAtWorkTimepunchSyncService.ComputePreflightMissing(
            new[] { Punch(1, 20) }, eawById, new HashSet<string>(), new Dictionary<int, int>());

        var m = Assert.Single(missing);
        Assert.Equal(20, m.EawEmployeeId);
        Assert.Contains("keine Personalnummer", m.Reason);
    }

    [Fact]
    public void NummerNichtInCowork_BlockiertCommit()
    {
        var emp = Emp(21, "999999");
        var eawById = new Dictionary<int, EawEmployee> { [21] = emp };
        var cowork  = new HashSet<string> { "580099" }; // 999999 fehlt

        var missing = EasyAtWorkTimepunchSyncService.ComputePreflightMissing(
            new[] { Punch(1, 21) }, eawById, cowork, new Dictionary<int, int>());

        var m = Assert.Single(missing);
        Assert.Equal(21, m.EawEmployeeId);
        Assert.Contains("existiert nicht in Cowork", m.Reason);
    }

    [Fact]
    public void PerAliasAufloesbar_IstNichtMissing()
    {
        // Stempel zeigt auf alte ID 47828, die NICHT in der MA-Liste ist —
        // aber per Alias auf einen Cowork-MA gemappt → kein Block.
        var eawById = new Dictionary<int, EawEmployee>();        // 47828 unbekannt
        var alias   = new Dictionary<int, int> { [47828] = 500 }; // → Cowork-MA 500

        var missing = EasyAtWorkTimepunchSyncService.ComputePreflightMissing(
            new[] { Punch(1, 47828) }, eawById, new HashSet<string>(), alias);

        Assert.Empty(missing);
    }

    [Fact]
    public void GeloeschteUndUngueltigeStempel_ZaehlenNicht()
    {
        // MA ohne Nummer, aber nur soft-deleted bzw. ohne TimeIn → kein Import,
        // also auch kein Block.
        var emp = Emp(30, number: null);
        var eawById = new Dictionary<int, EawEmployee> { [30] = emp };

        var missing = EasyAtWorkTimepunchSyncService.ComputePreflightMissing(
            new[] { Punch(1, 30, deleted: true), Punch(2, 30, hasIn: false) },
            eawById, new HashSet<string>(), new Dictionary<int, int>());

        Assert.Empty(missing);
    }

    [Fact]
    public void MehrereStempelProMissingMa_WerdenGezaehlt()
    {
        var emp = Emp(40, number: null);
        var eawById = new Dictionary<int, EawEmployee> { [40] = emp };

        var missing = EasyAtWorkTimepunchSyncService.ComputePreflightMissing(
            new[] { Punch(1, 40), Punch(2, 40), Punch(3, 40) },
            eawById, new HashSet<string>(), new Dictionary<int, int>());

        var m = Assert.Single(missing);
        Assert.Equal(3, m.TimepunchCount);
    }
}
