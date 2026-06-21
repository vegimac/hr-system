using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

using static HrSystem.Services.EasyAtWork.EasyAtWorkTimepunchSyncService;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die Payroll-Sink-Regel (Walter-Vorgabe 21.06.2026): ein easy@work-
/// MA, der in mehreren Filialen stempelt, hat dort dieselbe easy@work-id; bei
/// uns liegt er ggf. mehrfach (1× pro Filiale). Alle Stempel gehen auf den EINEN
/// Cowork-MA mit IsPayrollExcluded=false. Sind alle ausgeschlossen → keine
/// Stempel. Mehrere nicht-ausgeschlossene → Block. Zusätzlich: die Herkunfts-
/// filiale wird gespeichert, und die Lohnberechnung liest weiterhin ALLE Stempel
/// des Lohn-MA nach EmployeeId.
/// </summary>
public class EasyAtWorkPayrollSinkTests
{
    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string testName = "")
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "PayrollSink_" + testName + "_" + System.Guid.NewGuid())
            .Options;
        return new AppDbContext(options);
    }

    // ───────────────────────── ResolvePayrollSink ─────────────────────────

    [Fact]
    public void GenauEinNichtAusgeschlossenerMa_WirdAlsSinkGewaehlt()
    {
        // Derselbe Mensch in 3 Filialen: 2× Phantom (excluded), 1× Lohn-MA.
        var candidates = new[]
        {
            new CoworkCandidate(10, IsPayrollExcluded: true),
            new CoworkCandidate(11, IsPayrollExcluded: false),  // ← Lohn-MA
            new CoworkCandidate(12, IsPayrollExcluded: true),
        };

        var r = ResolvePayrollSink(candidates);

        Assert.Equal(PayrollMatchKind.Matched, r.Kind);
        Assert.Equal(11, r.SinkEmployeeId);
        Assert.Equal(1, r.PayrollCandidateCount);
    }

    [Fact]
    public void AlleKandidatenAusgeschlossen_WirdUebersprungen()
    {
        // Supervisor wie Nihat: in jeder Filiale IsPayrollExcluded=true.
        var candidates = new[]
        {
            new CoworkCandidate(10, IsPayrollExcluded: true),
            new CoworkCandidate(11, IsPayrollExcluded: true),
        };

        var r = ResolvePayrollSink(candidates);

        Assert.Equal(PayrollMatchKind.AllExcluded, r.Kind);
        Assert.Null(r.SinkEmployeeId);
    }

    [Fact]
    public void ZweiNichtAusgeschlosseneMa_BlockiertAlsAmbiguous()
    {
        var candidates = new[]
        {
            new CoworkCandidate(11, IsPayrollExcluded: false),
            new CoworkCandidate(12, IsPayrollExcluded: false),
        };

        var r = ResolvePayrollSink(candidates);

        Assert.Equal(PayrollMatchKind.Ambiguous, r.Kind);
        Assert.Null(r.SinkEmployeeId);
        Assert.Equal(2, r.PayrollCandidateCount);
    }

    [Fact]
    public void KeinKandidat_IstNoCandidate()
    {
        var r = ResolvePayrollSink(System.Array.Empty<CoworkCandidate>());
        Assert.Equal(PayrollMatchKind.NoCandidate, r.Kind);
    }

    [Fact]
    public void DupliziertePerEmployeeId_ZaehltNurEinmal()
    {
        // Dieselbe Cowork-Id kann über mehrere Schlüssel (eaw-id + Nummer)
        // mehrfach gesammelt werden → darf NICHT als Ambiguous gelten.
        var candidates = new[]
        {
            new CoworkCandidate(11, IsPayrollExcluded: false),
            new CoworkCandidate(11, IsPayrollExcluded: false),
        };

        var r = ResolvePayrollSink(candidates);

        Assert.Equal(PayrollMatchKind.Matched, r.Kind);
        Assert.Equal(11, r.SinkEmployeeId);
    }

    // ─────────────────── Herkunftsfiliale wird gespeichert ───────────────────

    [Fact]
    public async Task Herkunftsfiliale_WirdGespeichert()
    {
        var dbName = "Origin_" + System.Guid.NewGuid();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options;

        await using (var db = new AppDbContext(options))
        {
            db.EmployeeTimeEntries.Add(new EmployeeTimeEntry
            {
                EmployeeId             = 11,
                EntryDate              = new System.DateOnly(2021, 6, 1),
                TimeIn                 = new System.DateTime(2021, 6, 1, 17, 0, 0),
                EasyAtWorkTimepunchId  = 555,
                EasyAtWorkCustomerId   = 999,   // Filiale, in der gestempelt wurde
                SourceCompanyProfileId = 5,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = new AppDbContext(options))
        {
            var entry = await db.EmployeeTimeEntries.SingleAsync(e => e.EasyAtWorkTimepunchId == 555);
            Assert.Equal(999, entry.EasyAtWorkCustomerId);
            Assert.Equal(5,   entry.SourceCompanyProfileId);
        }
    }

    // ──────── Lohnlauf liest ALLE Stempel des Lohn-MA nach EmployeeId ────────

    [Fact]
    public async Task Lohnlauf_LiestAlleStempelDesLohnMa_UnabhaengigVonHerkunft()
    {
        using var db = NewDb();
        // Lohn-MA 11 hat in DREI Filialen gestempelt (Customer 5/6/7) →
        // alle drei Stempel liegen auf MA 11. Ein anderer MA 12 hat einen.
        db.EmployeeTimeEntries.AddRange(
            new EmployeeTimeEntry { EmployeeId = 11, EntryDate = new System.DateOnly(2021, 6, 1), TimeIn = new System.DateTime(2021, 6, 1, 8, 0, 0), EasyAtWorkCustomerId = 5 },
            new EmployeeTimeEntry { EmployeeId = 11, EntryDate = new System.DateOnly(2021, 6, 2), TimeIn = new System.DateTime(2021, 6, 2, 8, 0, 0), EasyAtWorkCustomerId = 6 },
            new EmployeeTimeEntry { EmployeeId = 11, EntryDate = new System.DateOnly(2021, 6, 3), TimeIn = new System.DateTime(2021, 6, 3, 8, 0, 0), EasyAtWorkCustomerId = 7 },
            new EmployeeTimeEntry { EmployeeId = 12, EntryDate = new System.DateOnly(2021, 6, 1), TimeIn = new System.DateTime(2021, 6, 1, 9, 0, 0), EasyAtWorkCustomerId = 5 }
        );
        await db.SaveChangesAsync();

        // Lohnberechnung liest rein nach EmployeeId (kein Filial-Filter).
        var fuerLohnMa = await db.EmployeeTimeEntries
            .Where(e => e.EmployeeId == 11)
            .ToListAsync();

        Assert.Equal(3, fuerLohnMa.Count);
        Assert.Equal(new[] { 5, 6, 7 }, fuerLohnMa.Select(e => e.EasyAtWorkCustomerId).OrderBy(x => x).ToArray());
    }
}
