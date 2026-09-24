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

    [Fact]
    public void AbgelaufenerStundentarif_TaeuschtKeinenStundenlohnVertragVor()
    {
        // MA 580005 Tomic: bis 31.03.2025 MTP 21.00/h, ab 01.04.2025 Fix mit
        // Monatstarif 4'295. Die alte Tarifsuche prüfte nur «From ≤ Datum» —
        // der beendete Stundentarif galt damit auch im April weiter, das Segment
        // wurde als Stundenlohn-Vertrag gelesen und fiel als «Kein Stundenlohn-
        // Tarif erfasst» heraus (Abschnitt ohne Lohn).
        var c = new List<EawContract>
        {
            new() { Id = 1, Type = "MTP/TPM", AmountType = "week", Amount = 35m,
                    FromRaw = "2023-12-31 23:00:00", ToRaw = "2025-03-31 21:59:59" },
            // easy liefert den Typ-Namen aus type_id: «Fix» mit «Woche 42».
            new() { Id = 2, Type = "Fix", AmountType = "week", Amount = 42m,
                    FromRaw = "2025-03-31 22:00:00", ToRaw = "2025-06-06 21:59:59" },
        };
        var r = new List<EawPayRate>
        {
            new() { Id = 10, Type = "hour",  Rate = 21m,
                    FromRaw = "2023-12-31 23:00:00", ToRaw = "2025-03-31 21:59:59" },
            new() { Id = 11, Type = "month", Rate = 4295m,
                    FromRaw = "2025-03-31 22:00:00", ToRaw = "2025-06-07 21:59:59" },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, AsOf, isKader: true);

        var erste = tl.First(x => x.Start == new DateOnly(2024, 1, 1));
        Assert.Equal("MTP", erste.Info.EmploymentModel);
        Assert.Equal(21m, erste.Info.HourlyRate);

        var zweite = tl.First(x => x.Start == new DateOnly(2025, 4, 1));
        Assert.Null(zweite.Info.DataError);
        Assert.Equal("FIX-M", zweite.Info.EmploymentModel);   // Kader + Monatslohn
        Assert.Equal(4295m, zweite.Info.MonthlySalary);
    }

    [Fact]
    public async Task AbschnitteVor2026_WerdenVomSyncNichtAngetastet()
    {
        // Walter-Entscheid 22.09.2026: Die easy-Historie ist für die Vergangenheit
        // unbrauchbar (mehrere offene Verträge, doppelte/gelöschte Tarife). Ein
        // VORHANDENER Abschnitt vor dem 01.01.2026 wird deshalb nie mehr geändert —
        // sonst wäre die Handarbeit beim nächsten Lauf weg. (Fehlende darf der Sync
        // weiterhin anlegen, dort gibt es nichts zu überschreiben.)
        using var db = NewDb();
        var emp = await SeedAsync(db);
        db.Employments.Add(new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2022, 12, 21), ContractEndDate = new DateTime(2024, 12, 31),
            EmploymentModel = "FIX-M", SalaryType = "monthly", MonthlySalary = 4600m,
            JobTitle = "SHIFT_LEADER_7_PLUS", IsActive = false,
        });
        await db.SaveChangesAsync();

        var (c, r) = EasyDaten();   // easy sagt 4'750 für denselben Zeitraum
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, AsOf, isKader: true);
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, 1, tl, 5, "SHIFT_LEADER_7_PLUS", null,
            historieStichtag: EasyAtWorkEmployeeSyncService.HistorieStichtag);
        await db.SaveChangesAsync();

        var v = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal(4600m, v.MonthlySalary);                       // unangetastet
        Assert.Equal("SHIFT_LEADER_7_PLUS", v.JobTitle);
        Assert.Equal(new DateTime(2024, 12, 31), v.ContractEndDate); // auch das Ende bleibt
    }

    private static async Task<Employment> OffenerAltvertragAsync(AppDbContext db, Employee emp)
    {
        var v = new Employment
        {
            EmployeeId = emp.Id, CompanyProfileId = 1,
            ContractStartDate = new DateTime(2022, 12, 21), ContractEndDate = null,
            EmploymentModel = "FIX-M", SalaryType = "monthly", MonthlySalary = 4600m,
            JobTitle = "SHIFT_LEADER_7_PLUS", IsActive = true,
        };
        db.Employments.Add(v);
        await db.SaveChangesAsync();
        return v;
    }

    [Fact]
    public async Task OffenerAltvertrag_WirdBeiAustrittInEasyAbgeschlossen()
    {
        // Walter 23.09.2026: Vertrag 13.1.2021 – offen, easy: Ende 18.03.2021,
        // «Eingestellt bis» 19.03.2021 → Austritt blieb leer, weil der offene
        // Alt-Vertrag ihn blockierte. Das leere Ende darf gefüllt werden.
        using var db = NewDb();
        var emp = await SeedAsync(db);
        await OffenerAltvertragAsync(db, emp);

        var (c, r) = EasyDaten();
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, AsOf, isKader: true);
        var notes = new List<string>();
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, 1, tl, 5, "SHIFT_LEADER_7_PLUS", new DateOnly(2024, 12, 31),
            cleanupNotes: notes, historieStichtag: EasyAtWorkEmployeeSyncService.HistorieStichtag);
        await db.SaveChangesAsync();

        var v = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Equal(new DateTime(2024, 12, 31), v.ContractEndDate);
        Assert.False(v.IsActive);
        Assert.Equal(4600m, v.MonthlySalary);                       // sonst unangetastet
        Assert.Contains(notes, n => n.Contains("abgeschlossen"));
    }

    [Fact]
    public async Task OffenerAltvertrag_OhneAustrittInEasy_BleibtOffen()
    {
        // Läuft die Person in easy weiter (kein «Eingestellt bis»), bleibt der
        // Alt-Vertrag unangetastet — der Historie-Stichtag gilt dann voll.
        using var db = NewDb();
        var emp = await SeedAsync(db);
        await OffenerAltvertragAsync(db, emp);

        var (c, r) = EasyDaten();
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, AsOf, isKader: true);
        await EasyAtWorkEmployeeSyncService.SyncEmploymentTimelineAsync(
            db, emp, 1, tl, 5, "SHIFT_LEADER_7_PLUS", null,
            historieStichtag: EasyAtWorkEmployeeSyncService.HistorieStichtag);
        await db.SaveChangesAsync();

        var v = Assert.Single(await db.Employments.Where(x => x.EmployeeId == emp.Id).ToListAsync());
        Assert.Null(v.ContractEndDate);
    }

    [Fact]
    public void TarifEndeEinenTagVorVertragsende_KeinEinTagesVertrag()
    {
        // MA 580101 Vogt: Vertrag 23:59:59, Tarif 00:00 Zürich — beide der 31.10.2026
        // (Walter-Vorgabe 24.09.2026). Ein Segment, ohne ToRaw zu verbiegen.
        var c = new List<EawContract>
        {
            new() { Id = 45767, AmountType = "week", Amount = 17m,
                    FromRaw = "2026-05-03 22:00:00", ToRaw = "2026-10-31 22:59:59" },
        };
        var r = new List<EawPayRate>
        {
            new() { Id = 72604, Type = "hour", Rate = 16.85m,
                    FromRaw = "2026-05-03 22:00:00", ToRaw = "2026-10-30 23:00:00" },
        };
        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, new DateOnly(2026, 9, 24));
        var s = Assert.Single(tl);
        Assert.Equal(new DateOnly(2026, 5, 4), s.Start);
        Assert.Equal(new DateOnly(2026, 10, 31), s.End);
        Assert.Equal(16.85m, s.Info.HourlyRate);
        Assert.False(s.EasyAtWorkManualOverride);
        Assert.Equal("2026-10-30 23:00:00", r[0].ToRaw);
    }

    [Fact]
    public void Fall750041_BeideEnden20Januar_EinSegment()
    {
        var c = new List<EawContract> { new() { Id = 1, AmountType = "week", Amount = 17m,
                    FromRaw = "2024-05-31 22:00:00", ToRaw = "2025-01-20 22:59:59" } };
        var r = new List<EawPayRate>  { new() { Id = 2, Type = "hour", Rate = 20m,
                    FromRaw = "2024-05-31 22:00:00", ToRaw = "2025-01-19 23:00:00" } };
        var s = Assert.Single(EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, new DateOnly(2026, 9, 24)));
        Assert.Equal(new DateOnly(2025, 1, 20), s.End);
    }

    [Fact]
    public void Fall750006_LohnsatzEinenTagLaenger_KeinEmploymentAm10Februar()
    {
        var c = new List<EawContract> { new() { Id = 1, AmountType = "week", Amount = 17m,
                    FromRaw = "2024-05-31 22:00:00", ToRaw = "2025-02-09 22:59:59" } };
        var r = new List<EawPayRate>  { new() { Id = 2, Type = "hour", Rate = 20m,
                    FromRaw = "2024-05-31 22:00:00", ToRaw = "2025-02-09 23:00:00" } };
        var s = Assert.Single(EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, new DateOnly(2026, 9, 24)));
        Assert.Equal(new DateOnly(2025, 2, 9), s.End);
        Assert.Equal(new DateOnly(2025, 2, 10), r[0].To);   // Lohnsatz-Datum bleibt, wie easy es liefert
    }

    [Fact]
    public void Fall750035_LohnsatzBeginntEinenTagSpaeter_EinEmploymentAbVertragsbeginn()
    {
        var c = new List<EawContract> { new() { Id = 1, AmountType = "week", Amount = 17m,
                    FromRaw = "2024-04-08 22:00:00" } };
        var r = new List<EawPayRate>  { new() { Id = 2, Type = "hour", Rate = 20m,
                    FromRaw = "2024-04-09 22:00:00" } };
        var s = Assert.Single(EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(c, r, new DateOnly(2026, 9, 24)));
        Assert.Equal(new DateOnly(2024, 4, 9), s.Start);
        Assert.Null(s.End);
        Assert.Equal(20m, s.Info.HourlyRate);
        Assert.False(s.EasyAtWorkManualOverride);
        Assert.Equal(new DateOnly(2024, 4, 10), r[0].From);
    }
}
