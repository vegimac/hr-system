using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Lohn-Nachtrag auf abgeschlossenen Abschnitten (Walter-Vorgabe 22.09.2026,
/// Fall 1220009 Acar-Hasanoglu): Verträge aus der Erst-Migration haben keinen
/// Lohn; easy liefert ihn in `pay_rates` MIT from/to. Der Abschluss-Schutz
/// verhindert ÄNDERUNGEN an abgerechneten Monaten — ein leeres Feld zu füllen
/// ändert dort aber nichts. Steht bereits ein Betrag, bleibt er unangetastet.
/// </summary>
public class EasyAtWorkLohnNachtragTests
{
    private static readonly DateOnly AsOf = new(2026, 9, 22);

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("LohnNachtrag_" + t + "_" + Guid.NewGuid()).Options);

    private static async Task<Employee> SeedAsync(AppDbContext db)
    {
        var e = new Employee { EmployeeNumber = "1220009", FirstName = "Derya", LastName = "Acar-Hasanoglu", IsActive = true };
        db.Employees.Add(e);
        await db.SaveChangesAsync();
        return e;
    }

    /// <summary>Ihre easy-Daten: ein Vertrag 100 %, Monatslohn 4'750.</summary>
    private static (List<EawContract>, List<EawPayRate>) EasyDaten() => (
        new List<EawContract>
        {
            // amount_type «percent» = Monatslohn-Vertrag (Pensum). Mit «week» würde
            // easy einen Stundenlohn-Vertrag beschreiben — dazu passt ein Monatstarif
            // nicht, und das Segment fiele als Erfassungsfehler raus (siehe
            // Vertragsart-Befund 22.09.2026, MA 1220009).
            new() { Id = 26295, AmountType = "percent", Amount = 100m, Percentage = 100m,
                    FromRaw = "2022-12-20 23:00:00", ToRaw = "2024-12-31 22:59:59" },
        },
        new List<EawPayRate>
        {
            new() { Id = 35298, Type = "month", Rate = 4750m,
                    FromRaw = "2022-12-20 23:00:00", ToRaw = "2024-12-31 22:59:59" },
        });

    [Fact]
    public async Task AbschnittOhneLohn_BekommtDenLohnAusEasy()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db);
        // Migrierter Abschnitt: richtig datiert, aber ohne Lohn.
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2022, 12, 21), ContractEndDate = new DateTime(2024, 12, 31),
            EmploymentModel = "FIX-M", SalaryType = "monthly", IsActive = false,
        });
        await db.SaveChangesAsync();

        var (c, r) = EasyDaten();
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, AsOf, isKader: true);
        // Lohnperiode ab 01.09.2026 abgeschlossen → der Abschnitt ist geschützt.
        var notes = new List<string>();
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, 1, tl, 5, "SHIFT_LEADER_7_PLUS", null,
            firstAllowedDate: new DateOnly(2026, 9, 1), skippedContracts: null, cleanupNotes: notes);
        await db.SaveChangesAsync();

        var v = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal(4750m, v.MonthlySalary ?? v.MonthlySalaryFte);
        Assert.Contains(notes, n => n.Contains("nachgetragen"));
        Assert.Contains(notes, n => n.Contains("nachgetragen"));
    }

    [Fact]
    public async Task VorhandenerLohn_BleibtUnangetastet()
    {
        using var db = NewDb();
        var emp = await SeedAsync(db);
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2022, 12, 21), ContractEndDate = new DateTime(2024, 12, 31),
            EmploymentModel = "FIX-M", SalaryType = "monthly", MonthlySalary = 4600m, IsActive = false,
        });
        await db.SaveChangesAsync();

        var (c, r) = EasyDaten();
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, AsOf, isKader: true);
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, 1, tl, 5, "SHIFT_LEADER_7_PLUS", null,
            firstAllowedDate: new DateOnly(2026, 9, 1));
        await db.SaveChangesAsync();

        var v = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal(4600m, v.MonthlySalary);   // easy sagt 4'750 — der Abschluss-Schutz gewinnt
    }

    [Fact]
    public void WochenstundenMitMonatstarif_IstMonatslohnVertrag()
    {
        // Fall 1220009: easy sagt «Woche 33.6», der Tarif ist aber monatlich —
        // in easys Oberfläche steht «Fix». Ohne diese Regel fiel das Segment als
        // «Kein Stundenlohn-Tarif erfasst» heraus (Walter 22.09.2026).
        var c = new List<EawContract>
        {
            new() { Id = 4663, AmountType = "week", Amount = 33.6m, Percentage = 80m,
                    FromRaw = "2021-06-20 22:00:00", ToRaw = "2022-12-20 22:59:59" },
        };
        var r = new List<EawPayRate>
        {
            new() { Id = 12060, Type = "month", Rate = 3376.80m,
                    FromRaw = "2021-06-20 22:00:00", ToRaw = "2022-12-20 22:59:59" },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, AsOf, isKader: true);
        var seg = Assert.Single(tl);
        Assert.Null(seg.Info.DataError);                       // kein Erfassungsfehler mehr
        Assert.Equal("FIX-M", seg.Info.EmploymentModel);       // Kader + Monatslohn
        Assert.Equal(3376.80m, seg.Info.MonthlySalary);
        Assert.Equal(4221m, seg.Info.MonthlySalaryFte);        // 3'376.80 / 80 × 100
    }

    [Fact]
    public void WochenstundenMitStundentarif_BleibtStundenlohn()
    {
        var c = new List<EawContract>
        {
            new() { Id = 43605, AmountType = "week", Amount = 34m, Percentage = 81m,
                    FromRaw = "2025-09-30 22:00:00", ToRaw = "2026-07-31 21:59:59" },
        };
        var r = new List<EawPayRate>
        {
            new() { Id = 77534, Type = "hour", Rate = 20.40m,
                    FromRaw = "2025-09-30 22:00:00", ToRaw = "2026-07-31 21:59:59" },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, AsOf, isKader: false);
        var seg = Assert.Single(tl);
        Assert.Equal("MTP", seg.Info.EmploymentModel);
        Assert.Equal(20.40m, seg.Info.HourlyRate);
    }
}
