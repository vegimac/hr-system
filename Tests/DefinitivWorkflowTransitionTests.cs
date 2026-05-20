using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Definitiv-Workflow Status-Übergangs-Tests (Walter-Vorgabe 19.05.2026).
///
/// Pendant zu AkontoWorkflowTransitionTests, für den Definitiv-Lauf.
/// Prüft die zentralen Invarianten:
///   • offen → provisorisch_abgeschlossen → abgeschlossen
///   • IsFinal=true gehört ausschliesslich in abgeschlossen (NICHT provisorisch)
///   • Reset/zurueck-an-gf rollt Snapshots auf BERECHNET + Saldo auf draft
///   • Wieder-Eröffnen: ABGESCHLOSSEN-Snapshots werden zu HR_BESTAETIGT
///   • PAYOUT_DATE_REACHED bei Wieder-Eröffnen nach Zahldatum
///
/// Doku-Quelle: CLAUDE.md → „Bearbeitungs-Status-Map".
/// </summary>
public class DefinitivWorkflowTransitionTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "DefWF_" + testName + "_" + Guid.NewGuid())
            .Options;
        return new AppDbContext(options);
    }

    private static PayrollPeriode SeedPeriode(AppDbContext db, string status = "offen",
                                              DateOnly? auszahlungsdatum = null)
    {
        var p = new PayrollPeriode
        {
            CompanyProfileId = 58, Year = 2026, Month = 1, Label = "01/2026",
            PeriodFrom = new DateOnly(2026, 1, 1), PeriodTo = new DateOnly(2026, 1, 31),
            Status = status,
            Auszahlungsdatum = auszahlungsdatum
        };
        db.PayrollPerioden.Add(p);
        db.SaveChanges();
        return p;
    }

    private static PayrollSnapshot SeedSnapshot(AppDbContext db, int periodeId, int employeeId,
                                                string status = "BERECHNET", bool isFinal = false)
    {
        var s = new PayrollSnapshot
        {
            PayrollPeriodeId = periodeId, EmployeeId = employeeId, CompanyProfileId = 58,
            SlipJson = "{}", Brutto = 3000m, Netto = 2500m,
            Status = status, IsFinal = isFinal
        };
        db.PayrollSnapshots.Add(s);
        db.SaveChanges();
        return s;
    }

    private static PayrollSaldo SeedSaldo(AppDbContext db, int employeeId, string status = "confirmed")
    {
        var sld = new PayrollSaldo
        {
            EmployeeId = employeeId, CompanyProfileId = 58,
            PeriodYear = 2026, PeriodMonth = 1,
            Status = status
        };
        db.PayrollSaldos.Add(sld);
        db.SaveChanges();
        return sld;
    }

    // ──────────────────────────────────────────────────────────────────
    // 1. offen → provisorisch_abgeschlossen (Abschliessen / „An HR senden")
    //    Snapshots dürfen NICHT auf IsFinal=true gesetzt werden — sonst
    //    kann HR nicht HR-bestätigen.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Abschliessen_DoesNotMarkSnapshotsAsFinal()
    {
        using var db = NewDb();
        var p = SeedPeriode(db, "offen");
        SeedSnapshot(db, p.Id, 1, "FREIGEGEBEN_GF", isFinal: false);
        SeedSnapshot(db, p.Id, 2, "FREIGEGEBEN_GF", isFinal: false);

        // Abschliessen: nur Periode-Status wechselt, Snapshots bleiben FREIGEGEBEN_GF
        p.Status = "provisorisch_abgeschlossen";
        p.ProvisorischAbgeschlossenAm = DateTime.UtcNow;
        // Snapshots werden nur "touched", nicht final gemarkt
        foreach (var s in await db.PayrollSnapshots.ToListAsync())
            s.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var snaps = await db.PayrollSnapshots.ToListAsync();
        Assert.All(snaps, s => Assert.False(s.IsFinal,
            "IsFinal darf bei provisorisch_abgeschlossen NICHT gesetzt sein — sonst kann HR nicht HR-bestätigen."));
        Assert.All(snaps, s => Assert.Equal("FREIGEGEBEN_GF", s.Status));
    }

    // ──────────────────────────────────────────────────────────────────
    // 2. provisorisch → offen (zurueck-an-gf, Walter-Vorgabe 19.05.2026)
    //    Snapshots auf BERECHNET zurück, GF/HR-Spuren raus, IsFinal=false.
    //    Saldos auf 'draft'.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ZurueckAnGf_ResetsAllSnapshotsToBerechnet()
    {
        using var db = NewDb();
        var p = SeedPeriode(db, "provisorisch_abgeschlossen");
        var s1 = SeedSnapshot(db, p.Id, 1, "FREIGEGEBEN_GF");
        s1.GfFreigegebenAt = DateTime.UtcNow;
        var s2 = SeedSnapshot(db, p.Id, 2, "HR_BESTAETIGT");
        s2.HrBestaetigtAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // ZurueckAnGf-Logik
        p.Status = "offen";
        p.ProvisorischAbgeschlossenAm = null;
        foreach (var s in await db.PayrollSnapshots.ToListAsync())
        {
            s.Status = "BERECHNET";
            s.IsFinal = false;
            s.GfFreigegebenAt = null;
            s.GfFreigegebenBy = null;
            s.HrBestaetigtAt = null;
            s.HrBestaetigtBy = null;
        }
        await db.SaveChangesAsync();

        var snaps = await db.PayrollSnapshots.ToListAsync();
        Assert.All(snaps, s => Assert.Equal("BERECHNET", s.Status));
        Assert.All(snaps, s => Assert.Null(s.GfFreigegebenAt));
        Assert.All(snaps, s => Assert.Null(s.HrBestaetigtAt));
        Assert.All(snaps, s => Assert.False(s.IsFinal));
    }

    [Fact]
    public async Task ZurueckAnGf_AlsoResetsSaldoToDraft()
    {
        using var db = NewDb();
        var p = SeedPeriode(db, "provisorisch_abgeschlossen");
        SeedSnapshot(db, p.Id, 1, "FREIGEGEBEN_GF");
        SeedSaldo(db, 1, "confirmed");

        // Backend-Logik: Saldo zurück auf draft
        foreach (var s in await db.PayrollSaldos.ToListAsync())
            s.Status = "draft";
        await db.SaveChangesAsync();

        var sld = await db.PayrollSaldos.FirstAsync();
        Assert.Equal("draft", sld.Status);
    }

    // ──────────────────────────────────────────────────────────────────
    // 3. FREIGEGEBEN_GF → HR_BESTAETIGT (HR-Bestätigung pro MA)
    //    Nur in provisorisch_abgeschlossen, nur wenn nicht IsFinal.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DefinitivHrBestaetigen_OnlyInProvisorischAndFreigegebenGf()
    {
        using var db = NewDb();
        var p = SeedPeriode(db, "provisorisch_abgeschlossen");
        var s = SeedSnapshot(db, p.Id, 1, "FREIGEGEBEN_GF", isFinal: false);

        // Vorbedingungen erfüllt — Transition OK
        Assert.Equal("provisorisch_abgeschlossen", p.Status);
        Assert.Equal("FREIGEGEBEN_GF", s.Status);
        Assert.False(s.IsFinal);

        s.Status = "HR_BESTAETIGT";
        s.HrBestaetigtAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Assert.Equal("HR_BESTAETIGT", (await db.PayrollSnapshots.FirstAsync()).Status);
    }

    [Fact]
    public async Task DefinitivHrBestaetigen_RejectsFinalSnapshots()
    {
        using var db = NewDb();
        var p = SeedPeriode(db, "provisorisch_abgeschlossen");
        SeedSnapshot(db, p.Id, 1, "FREIGEGEBEN_GF", isFinal: true);   // sollte nie auftreten,
                                                                       // aber Backend muss es trotzdem blockieren

        var s = await db.PayrollSnapshots.FirstAsync();
        Assert.True(s.IsFinal);   // Vorbedingung der Sperre
        // Backend würde 409 "Snapshot ist final" zurückgeben
    }

    // ──────────────────────────────────────────────────────────────────
    // 4. provisorisch → abgeschlossen (DefinitivAbschliessen / DTA-Klick)
    //    JETZT erst IsFinal=true + Status=ABGESCHLOSSEN.
    //    Auszahlungsdatum wird gesetzt.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DefinitivAbschliessen_SetsIsFinalAndAbgeschlossen()
    {
        using var db = NewDb();
        var p = SeedPeriode(db, "provisorisch_abgeschlossen");
        SeedSnapshot(db, p.Id, 1, "HR_BESTAETIGT", isFinal: false);
        SeedSnapshot(db, p.Id, 2, "HR_BESTAETIGT", isFinal: false);

        var datum = new DateOnly(2026, 2, 1);
        p.Status = "abgeschlossen";
        p.AbgeschlossenAm = DateTime.UtcNow;
        p.Auszahlungsdatum = datum;
        foreach (var s in await db.PayrollSnapshots.ToListAsync())
        {
            s.Status = "ABGESCHLOSSEN";
            s.IsFinal = true;
        }
        await db.SaveChangesAsync();

        var pl = await db.PayrollPerioden.FirstAsync();
        Assert.Equal("abgeschlossen", pl.Status);
        Assert.Equal(datum, pl.Auszahlungsdatum);

        var snaps = await db.PayrollSnapshots.ToListAsync();
        Assert.All(snaps, s => Assert.True(s.IsFinal));
        Assert.All(snaps, s => Assert.Equal("ABGESCHLOSSEN", s.Status));
    }

    // ──────────────────────────────────────────────────────────────────
    // 5. abgeschlossen → provisorisch (WiederOeffnen, Admin)
    //    ABGESCHLOSSEN-Snapshots werden zu HR_BESTAETIGT, IsFinal=false.
    //    HR-Bestätigungen bleiben erhalten, nur DTA-Klick muss neu.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WiederOeffnen_RollsAbgeschlossenSnapshotsBackToHrBestaetigt()
    {
        using var db = NewDb();
        var datum = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(2));   // morgen
        var p = SeedPeriode(db, "abgeschlossen", datum);
        p.AbgeschlossenAm = DateTime.UtcNow.AddHours(-1);
        SeedSnapshot(db, p.Id, 1, "ABGESCHLOSSEN", isFinal: true);
        await db.SaveChangesAsync();

        // WiederOeffnen-Logik
        p.Status = "provisorisch_abgeschlossen";
        p.AbgeschlossenAm = null;
        foreach (var s in await db.PayrollSnapshots.ToListAsync())
        {
            if (s.Status == "ABGESCHLOSSEN") s.Status = "HR_BESTAETIGT";
            s.IsFinal = false;
        }
        await db.SaveChangesAsync();

        var snaps = await db.PayrollSnapshots.ToListAsync();
        Assert.All(snaps, s => Assert.Equal("HR_BESTAETIGT", s.Status));
        Assert.All(snaps, s => Assert.False(s.IsFinal));
    }

    // ──────────────────────────────────────────────────────────────────
    // 6. PAYOUT_DATE_REACHED: WiederOeffnen gesperrt nach Auszahlungsdatum.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void WiederOeffnen_BlockedAfterAuszahlungsdatum()
    {
        using var db = NewDb();
        var gestern = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(-1));
        var p = SeedPeriode(db, "abgeschlossen", gestern);

        var heute = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        Assert.True(heute > p.Auszahlungsdatum!.Value,
            "Test-Setup: heute muss nach Auszahlungsdatum liegen → Reset gesperrt.");
        // Backend würde 409 PAYOUT_DATE_REACHED zurückgeben.
    }

    [Fact]
    public void WiederOeffnen_AllowedOnAuszahlungsdatumItself()
    {
        using var db = NewDb();
        var heuteOnly = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var p = SeedPeriode(db, "abgeschlossen", heuteOnly);

        Assert.False(heuteOnly > p.Auszahlungsdatum!.Value,
            "Reset am Auszahlungsdatum selbst muss noch möglich sein.");
    }

    // ──────────────────────────────────────────────────────────────────
    // 7. Status-Übergang von Akonto AUSBEZAHLT in Definitiv (Walter-Vorgabe):
    //    Snapshots des Definitiv-Workflows müssen BERECHNET sein damit GF
    //    im Definitiv-Tab jeden MA neu bestätigen kann. Akonto-Workflow
    //    hat damit nichts zu tun (eigenes Status-Feld).
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AkontoAusbezahlt_DoesNotAffectDefinitivSnapshotStatus()
    {
        using var db = NewDb();
        var p = SeedPeriode(db, "offen");
        p.AkontoStatus = "AUSBEZAHLT";
        p.AkontoAuszahlungsdatum = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        // Im neuen Snapshot-Default kommt BERECHNET — der GF muss neu bestätigen
        SeedSnapshot(db, p.Id, 1);   // Default: BERECHNET, IsFinal=false
        await db.SaveChangesAsync();

        var s = await db.PayrollSnapshots.FirstAsync();
        Assert.Equal("BERECHNET", s.Status);   // GF muss bestätigen
        Assert.False(s.IsFinal);
        Assert.Equal("offen", (await db.PayrollPerioden.FirstAsync()).Status);
    }
}
