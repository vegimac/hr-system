using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Betriebszugehörigkeit getrennt vom Eintritt (Walter-Vorgabe 24.09.2026).
///
/// Anlass: Simona Dan wechselte von Sursee nach Reinach. easy@work vergab dort ein
/// neues «Datum der Betriebszugehörigkeit» (24.09.2026) — damit fiel sie von fünf
/// Dienstjahren zurück ins erste. Daran hängen die Lohnfortzahlung bei Krankheit,
/// die Karenztage und die Sperrfrist nach Art. 336c.
///
/// **Die Regel (Walter 24.09.2026):** Kündigt jemand in einer Filiale und kommt
/// später zurück, gibt es ein NEUES Eintrittsdatum — alle Fristen laufen ab dann,
/// auch das Arbeitsjahr für die Krankenversicherung, und es gibt eine neue Probezeit
/// wie bei einem ganz frischen Mitarbeitenden. Nur beim NAHTLOSEN Übertritt von
/// Filiale A nach B zählt das alte Datum weiter. Jede Filiale ist eine eigene
/// Rechtseinheit mit eigenem HR-Eintrag und eigener Versicherung.
///
/// Das ist gerechnet, nicht gefragt: <see cref="Dienstalter"/> geht von der heutigen
/// Anstellung rückwärts durch die Vertragskette, solange die Abschnitte lückenlos
/// aneinander anschliessen. <c>DienstalterSeit</c> ist nur die Handeingabe für
/// Ausnahmen und sticht die Rechnung.
/// </summary>
public class DienstalterTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("Dienstalter_" + t + "_" + Guid.NewGuid()).Options);

    private static Employee Ma(int id, DateTime? eintritt, DateTime? dienstalter = null) => new()
    {
        Id = id,
        FirstName = "Simona", LastName = "Test" + id,
        EmployeeNumber = "129009" + id,
        IsActive = true, IsHidden = false, IsPayrollExcluded = false,
        MaritalStatus = "Verheiratet", SocialSecurityNumber = "756.1234.5678.97",
        EntryDate = eintritt,
        DienstalterSeit = dienstalter,
    };

    private static Employment Vertrag(int id, int empId, DateTime von, DateTime? bis, int filiale = 58) => new()
    {
        Id = id, EmployeeId = empId, CompanyProfileId = filiale,
        ContractStartDate = von, ContractEndDate = bis,
        EmploymentModel = "FLEX", IsActive = bis == null || bis >= DateTime.Today,
    };

    // ── Die Kettenregel ──────────────────────────────────────────────────────

    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public void NahtloserUebertritt_NimmtDasAlteDatum()
    {
        // Sursee endet 31.08., Reinach beginnt 01.09. — kein Tag dazwischen.
        var ma = Ma(1, new DateTime(2026, 9, 1));
        var kette = new[]
        {
            Vertrag(1, 1, new DateTime(2021, 10, 1), new DateTime(2026, 8, 31), 75),
            Vertrag(2, 1, new DateTime(2026, 9, 1), null, 129),
        };
        Assert.Equal(new DateOnly(2021, 10, 1), Dienstalter.Massgebend(ma, kette, new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void WiedereintrittMitLuecke_BeginntNeu()
    {
        // Der Fall Simona: Austritt 31.07., Wiedereintritt 22.09. — knapp zwei Monate.
        var ma = Ma(1, new DateTime(2026, 9, 24));
        var kette = new[]
        {
            Vertrag(1, 1, new DateTime(2021, 10, 1), new DateTime(2026, 7, 31), 75),
            Vertrag(2, 1, new DateTime(2026, 9, 22), null, 129),
        };
        Assert.Equal(new DateOnly(2026, 9, 24), Dienstalter.Massgebend(ma, kette, new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void EinTagLuecke_IstSchonEinUnterbruch()
    {
        // Bewusst streng: 31.08. zu Ende, 02.09. wieder angefangen = Unterbruch.
        // Wer das anders sieht, trägt es von Hand ein.
        var ma = Ma(1, new DateTime(2026, 9, 2));
        var kette = new[]
        {
            Vertrag(1, 1, new DateTime(2021, 10, 1), new DateTime(2026, 8, 31), 75),
            Vertrag(2, 1, new DateTime(2026, 9, 2), null, 129),
        };
        Assert.Equal(new DateOnly(2026, 9, 2), Dienstalter.Massgebend(ma, kette, new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void MehrereNahtloseAbschnitte_GehenBisGanzZurueck()
    {
        // Lohnerhöhungen und Filialwechsel ohne Lücke — die ganze Kette zählt.
        var ma = Ma(1, new DateTime(2026, 1, 1));
        var kette = new[]
        {
            Vertrag(1, 1, new DateTime(2019, 4, 1), new DateTime(2022, 12, 31), 75),
            Vertrag(2, 1, new DateTime(2023, 1, 1), new DateTime(2025, 12, 31), 75),
            Vertrag(3, 1, new DateTime(2026, 1, 1), null, 129),
        };
        Assert.Equal(new DateOnly(2019, 4, 1), Dienstalter.Massgebend(ma, kette, Heute));
    }

    [Fact]
    public void KetteBrichtAnDerLuecke_NichtDavor()
    {
        // 2019–2022 nahtlos, dann ein Jahr Pause, dann 2024 nahtlos weiter.
        // Massgebend ist der Beginn der JÜNGEREN Kette, nicht 2019.
        var ma = Ma(1, new DateTime(2024, 3, 1));
        var kette = new[]
        {
            Vertrag(1, 1, new DateTime(2019, 4, 1), new DateTime(2022, 12, 31), 75),
            Vertrag(2, 1, new DateTime(2024, 3, 1), new DateTime(2025, 2, 28), 129),
            Vertrag(3, 1, new DateTime(2025, 3, 1), null, 129),
        };
        Assert.Equal(new DateOnly(2024, 3, 1), Dienstalter.Massgebend(ma, kette, Heute));
    }

    [Fact]
    public void Handeingabe_StichtDieRechnung()
    {
        var ma = Ma(1, new DateTime(2026, 9, 24), new DateTime(2021, 10, 1));
        var kette = new[]
        {
            Vertrag(1, 1, new DateTime(2021, 10, 1), new DateTime(2026, 7, 31), 75),
            Vertrag(2, 1, new DateTime(2026, 9, 22), null, 129),
        };
        Assert.Equal(new DateOnly(2021, 10, 1), Dienstalter.Massgebend(ma, kette, Heute));
    }

    [Fact]
    public void OhneVertraege_GiltDerEintritt()
        => Assert.Equal(new DateOnly(2026, 9, 24),
                        Dienstalter.Massgebend(Ma(1, new DateTime(2026, 9, 24)), null, Heute));

    [Fact]
    public void OhneAllesBleibtLeer()
        => Assert.Null(Dienstalter.Massgebend(Ma(1, null), null, Heute));

    [Fact]
    public void GroessteLuecke_WirdGemessen()
    {
        var kette = new[]
        {
            Vertrag(1, 1, new DateTime(2021, 10, 1), new DateTime(2026, 7, 31), 75),
            Vertrag(2, 1, new DateTime(2026, 9, 22), null, 129),
        };
        Assert.Equal(52, Dienstalter.GroessteLueckeTage(kette));
    }

    // ── Sperrfrist rechnet ab der Betriebszugehörigkeit ──────────────────────

    /// <summary>Sperrfrist am Stichtag, ohne Krankheit egal — geprüft wird das Dienstjahr.</summary>
    private static async Task<int?> DienstjahrAsync(AppDbContext db, int empId)
    {
        var info = await new SperrfristService(db).ComputeAsync(empId, DateOnly.FromDateTime(DateTime.Today));
        return info.DienstjahrAmStichtag;
    }

    [Fact]
    public async Task WiedereintrittMitLuecke_FaelltInsErsteDienstjahr()
    {
        // Gewollt: nach einem Unterbruch beginnt alles neu — auch die Sperrfrist.
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-5), DateTime.Today.AddDays(-50), 75));
        db.Employments.Add(Vertrag(2, 1, DateTime.Today, null, 129));
        await db.SaveChangesAsync();

        Assert.Equal(1, await DienstjahrAsync(db, 1));
    }

    [Fact]
    public async Task NahtloserUebertritt_ZaehltWeiter_OhneHandeingabe()
    {
        // Alter Vertrag endet gestern, neuer beginnt heute — keine Lücke.
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-5), DateTime.Today.AddDays(-1), 75));
        db.Employments.Add(Vertrag(2, 1, DateTime.Today, null, 129));
        await db.SaveChangesAsync();

        // Fünf Jahre gedient ⇒ sechstes Dienstjahr ⇒ die lange Sperrfrist greift.
        Assert.Equal(6, await DienstjahrAsync(db, 1));
    }

    [Fact]
    public async Task Probezeit_HaengtAmEintritt_NichtAmDienstalter()
    {
        // Beim Übertritt beginnt die Probezeit neu, die Dienstjahre aber nicht.
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today.AddDays(-10), DateTime.Today.AddYears(-5)));
        var v = Vertrag(1, 1, DateTime.Today.AddDays(-10), null, 129);
        v.ProbationPeriodMonths = 3;
        db.Employments.Add(v);
        await db.SaveChangesAsync();

        var info = await new SperrfristService(db).ComputeAsync(1, DateOnly.FromDateTime(DateTime.Today));
        // In Probezeit — obwohl das Dienstalter fünf Jahre zurückliegt.
        Assert.Equal("IN_PROBEZEIT", info.Status);
    }

    [Fact]
    public async Task NahtloserUebertritt_HatTrotzdemNeueProbezeit()
    {
        // Jede Filiale ist eine eigene Rechtseinheit mit eigenem HR-Eintrag —
        // die Probezeit beginnt auch beim nahtlosen Übertritt neu (Walter 24.09.2026).
        using var db = NewDb();
        db.Employees.Add(Ma(1, DateTime.Today.AddDays(-10)));
        db.Employments.Add(Vertrag(1, 1, DateTime.Today.AddYears(-5), DateTime.Today.AddDays(-11), 75));
        var neu = Vertrag(2, 1, DateTime.Today.AddDays(-10), null, 129);
        neu.ProbationPeriodMonths = 3;
        db.Employments.Add(neu);
        await db.SaveChangesAsync();

        var info = await new SperrfristService(db).ComputeAsync(1, DateOnly.FromDateTime(DateTime.Today));
        Assert.Equal("IN_PROBEZEIT", info.Status);
    }
}
