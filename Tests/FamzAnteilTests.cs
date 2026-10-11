using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Familienzulage im angebrochenen Monat (Walter 04.10.2026, Fall Fomina): Ausgleichskasse
/// rechnet 30-Tage-Basis — AZ 310 ab 14.08.2026 → August 175.65, September 310.
/// Erfahren am 08.09.2026, August noch in Mirus abgerechnet → Nachzahlung im September.
/// </summary>
public class FamzAnteilTests
{
    private static readonly DateOnly Aug = new(2026, 8, 1);

    [Fact]
    public void Fomina_August_17_von_30_Tagen()
    {
        var anteil = PayrollCalculations.FamzMonatsAnteil(Aug, new DateOnly(2026, 8, 14), null, false, false);
        Assert.Equal(17m / 30m, anteil);
        Assert.Equal(175.65m, PayrollCalculations.FamzAnteilBetrag(310m, anteil));
    }

    [Fact]
    public void VollerMonat_bleibt_voll()
    {
        var anteil = PayrollCalculations.FamzMonatsAnteil(new DateOnly(2026, 9, 1), new DateOnly(2026, 8, 14), null, false, false);
        Assert.Equal(1m, anteil);
        Assert.Equal(310m, PayrollCalculations.FamzAnteilBetrag(310m, anteil));
    }

    [Fact]
    public void Ende_mitten_im_Monat_anteilig()
    {
        var anteil = PayrollCalculations.FamzMonatsAnteil(Aug, new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 10), false, false);
        Assert.Equal(10m / 30m, anteil);
    }

    [Fact]
    public void Monatsende_31_und_Februar_gelten_als_voll()
    {
        Assert.Equal(1m, PayrollCalculations.FamzMonatsAnteil(Aug, new DateOnly(2026, 1, 1), new DateOnly(2026, 8, 31), false, false));
        Assert.Equal(1m, PayrollCalculations.FamzMonatsAnteil(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 28), false, false));
    }

    [Fact]
    public void Nahtloser_Wechsel_KZ_AZ_alter_Eintrag_zahlt_den_Monat()
    {
        // KZ bis 19.08. (16. Geburtstag), AZ ab 20.08.
        var kz = PayrollCalculations.FamzMonatsAnteil(Aug, new DateOnly(2020, 1, 1), new DateOnly(2026, 8, 19), false, hatNachfolger: true);
        var az = PayrollCalculations.FamzMonatsAnteil(Aug, new DateOnly(2026, 8, 20), null, hatVorgaenger: true, false);
        Assert.Equal(1m, kz);
        Assert.Equal(0m, az);
    }

    private static void Perioden(AppDbContext db, int cp, int jahr, params int[] monate)
    {
        foreach (var m in monate)
            db.PayrollPerioden.Add(new PayrollPeriode
            {
                CompanyProfileId = cp, Year = jahr, Month = m,
                PeriodFrom = new DateOnly(jahr, m, 1), PeriodTo = new DateOnly(jahr, m, 1).AddMonths(1).AddDays(-1)
            });
    }

    [Fact]
    public async Task Nachzahlung_fuer_Monat_ohne_abgeschlossenen_Lohn()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FamzAnteil_" + Guid.NewGuid()).Options);
        Perioden(db, 58, 2026, 8, 9);
        db.Employments.Add(new Employment
        {
            EmployeeId = 7, CompanyProfileId = 58, IsActive = true,
            ContractStartDate = new DateTime(2026, 8, 14)
        });
        var kind = new EmployeeFamilyMember { EmployeeId = 7, MemberType = "Kind", FirstName = "Amaliia", LastName = "Kubrak" };
        db.EmployeeFamilyMembers.Add(kind);
        await db.SaveChangesAsync();
        var az = new FamilyMemberAllowance
        {
            FamilyMemberId = kind.Id, AllowanceType = "AZ", MonthlyAmount = 310m,
            ValidFrom = new DateOnly(2026, 8, 14), ValidTo = new DateOnly(2027, 9, 30),
            ErfahrenAm = new DateOnly(2026, 9, 8)
        };
        db.FamilyMemberAllowances.Add(az);
        await db.SaveChangesAsync();

        await new FamzKorrekturService(db).EnsureKorrekturenFuerLohnlaufAsync(7, 2026, 9, "Test");

        var posten = await db.FamzKorrekturen.ToListAsync();
        var aug = Assert.Single(posten);
        Assert.Equal((2026, 8), (aug.Jahr, aug.Monat));
        Assert.Equal(175.65m, aug.Betrag);
        Assert.Equal("OFFEN", aug.Status);
        Assert.Equal(58, aug.CompanyProfileId);
    }

    [Fact]
    public async Task Vor_erstem_OneCrew_Lohnmonat_keine_Warnung()
    {
        // Walter 11.10.2026 (Djonlagic): OneCrew startet im Januar 2026 — 2025 hat Mirus
        // abgerechnet, dafür keine Warnung.
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FamzAnteil_" + Guid.NewGuid()).Options);
        Perioden(db, 2, 2026, 1);
        db.Employments.Add(new Employment
        {
            EmployeeId = 11, CompanyProfileId = 2, IsActive = true,
            ContractStartDate = new DateTime(2025, 11, 1)
        });
        var kind = new EmployeeFamilyMember { EmployeeId = 11, MemberType = "Kind", FirstName = "Arman" };
        db.EmployeeFamilyMembers.Add(kind);
        await db.SaveChangesAsync();
        db.FamilyMemberAllowances.Add(new FamilyMemberAllowance
        {
            FamilyMemberId = kind.Id, AllowanceType = "KZ", MonthlyAmount = 260m,
            ValidFrom = new DateOnly(2025, 1, 1), ErfahrenAm = new DateOnly(2026, 1, 7)
        });
        await db.SaveChangesAsync();

        await new FamzKorrekturService(db).EnsureKorrekturenFuerLohnlaufAsync(11, 2026, 1, "Test");

        Assert.Empty(await db.FamzKorrekturen.ToListAsync());
    }

    [Fact]
    public async Task Vor_Eintritt_keine_Nachzahlung()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FamzAnteil_" + Guid.NewGuid()).Options);
        Perioden(db, 58, 2026, 6, 7, 8, 9);
        db.Employments.Add(new Employment
        {
            EmployeeId = 8, CompanyProfileId = 58, IsActive = true,
            ContractStartDate = new DateTime(2026, 8, 1)
        });
        var kind = new EmployeeFamilyMember { EmployeeId = 8, MemberType = "Kind", FirstName = "K" };
        db.EmployeeFamilyMembers.Add(kind);
        await db.SaveChangesAsync();
        db.FamilyMemberAllowances.Add(new FamilyMemberAllowance
        {
            FamilyMemberId = kind.Id, AllowanceType = "KZ", MonthlyAmount = 250m,
            ValidFrom = new DateOnly(2026, 6, 1), ErfahrenAm = new DateOnly(2026, 9, 2)
        });
        await db.SaveChangesAsync();

        await new FamzKorrekturService(db).EnsureKorrekturenFuerLohnlaufAsync(8, 2026, 9, "Test");

        var posten = await db.FamzKorrekturen.OrderBy(k => k.Monat).ToListAsync();
        var aug = Assert.Single(posten);
        Assert.Equal(8, aug.Monat);
        Assert.Equal(250m, aug.Betrag);
    }

    // Walter 11.10.2026 (Djonlagic): «Erfahren am» vertippt → Nachzahlungs-Posten;
    // nach der Korrektur auf «Gültig ab» müssen sie weg sein.
    private static async Task<(AppDbContext Db, FamilyMemberAllowance Kz)> DjonlagicAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FamzAnteil_" + Guid.NewGuid()).Options);
        Perioden(db, 2, 2025, 11, 12);
        Perioden(db, 2, 2026, 1, 2, 3, 4, 5, 6);
        db.Employments.Add(new Employment
        {
            EmployeeId = 9, CompanyProfileId = 2, IsActive = true,
            ContractStartDate = new DateTime(2025, 1, 1)
        });
        var kind = new EmployeeFamilyMember { EmployeeId = 9, MemberType = "Kind", FirstName = "Arman" };
        db.EmployeeFamilyMembers.Add(kind);
        await db.SaveChangesAsync();
        var kz = new FamilyMemberAllowance
        {
            FamilyMemberId = kind.Id, AllowanceType = "KZ", MonthlyAmount = 260m,
            ValidFrom = new DateOnly(2025, 1, 1), ValidTo = new DateOnly(2027, 3, 31),
            ErfahrenAm = new DateOnly(2026, 6, 1)
        };
        db.FamilyMemberAllowances.Add(kz);
        await db.SaveChangesAsync();
        await new FamzKorrekturService(db).ErzeugeKorrekturenAsync(kz, "Test");
        Assert.NotEmpty(await db.FamzKorrekturen.ToListAsync());
        return (db, kz);
    }

    [Fact]
    public async Task ErfahrenAm_korrigiert_raeumt_alte_Posten_weg()
    {
        var (db, kz) = await DjonlagicAsync();
        kz.ErfahrenAm = kz.ValidFrom;
        await db.SaveChangesAsync();

        await new FamzKorrekturService(db).ErzeugeKorrekturenAsync(kz, "Test");

        Assert.Empty(await db.FamzKorrekturen.ToListAsync());
    }

    [Fact]
    public async Task Lohnlauf_raeumt_alte_Posten_weg()
    {
        var (db, kz) = await DjonlagicAsync();
        kz.ErfahrenAm = kz.ValidFrom;
        await db.SaveChangesAsync();

        await new FamzKorrekturService(db).EnsureKorrekturenFuerLohnlaufAsync(9, 2026, 1, "Test");

        Assert.Empty(await db.FamzKorrekturen.ToListAsync());
    }

    [Fact]
    public async Task Geloeschte_Zulage_Lohnlauf_raeumt_Posten_weg()
    {
        var (db, kz) = await DjonlagicAsync();
        db.FamilyMemberAllowances.Remove(kz);
        await db.SaveChangesAsync();

        await new FamzKorrekturService(db).EnsureKorrekturenFuerLohnlaufAsync(9, 2026, 1, "Test");

        Assert.Empty(await db.FamzKorrekturen.ToListAsync());
    }

    // Walter 11.10.2026: Nachzahlungen werden nicht mehr automatisch bezahlt — sie sind
    // Warnungen, die quittiert werden (als Zulage übernommen oder «nicht nötig»).

    [Fact]
    public async Task Quittierte_Warnung_kommt_nicht_wieder()
    {
        var (db, _) = await DjonlagicAsync();
        foreach (var k in await db.FamzKorrekturen.ToListAsync())
        {
            k.Status = "ERLEDIGT";
            k.ErledigtArt = "NICHT_NOETIG";
            k.Bemerkung = "schon in Mirus bezahlt";
        }
        await db.SaveChangesAsync();

        await new FamzKorrekturService(db).EnsureKorrekturenFuerLohnlaufAsync(9, 2026, 6, "Test");

        Assert.Empty(await FamzKorrekturService.OffeneWarnungen(db, 9, 2026, 6).ToListAsync());
    }

    [Fact]
    public async Task Uebernommene_Zulage_geloescht_Warnung_wieder_offen()
    {
        var (db, _) = await DjonlagicAsync();
        var mai = await db.FamzKorrekturen.SingleAsync(k => k.Jahr == 2026 && k.Monat == 5);
        var zulage = new LohnZulage { EmployeeId = 9, Periode = "2026-06", LohnpositionId = 1, Betrag = mai.Betrag };
        db.LohnZulagen.Add(zulage);
        await db.SaveChangesAsync();
        mai.Status = "ERLEDIGT";
        mai.ErledigtArt = "UEBERNOMMEN";
        mai.LohnZulageId = zulage.Id;
        await db.SaveChangesAsync();
        var service = new FamzKorrekturService(db);

        await service.EnsureKorrekturenFuerLohnlaufAsync(9, 2026, 6, "Test");
        Assert.DoesNotContain(await FamzKorrekturService.OffeneWarnungen(db, 9, 2026, 6).ToListAsync(), k => k.Id == mai.Id);

        db.LohnZulagen.Remove(zulage);
        await db.SaveChangesAsync();
        await service.EnsureKorrekturenFuerLohnlaufAsync(9, 2026, 6, "Test");

        var wieder = await db.FamzKorrekturen.SingleAsync(k => k.Id == mai.Id);
        Assert.Equal("OFFEN", wieder.Status);
        Assert.Null(wieder.LohnZulageId);
    }

    [Fact]
    public async Task Warnung_nur_fuer_fruehere_Monate()
    {
        var (db, _) = await DjonlagicAsync();
        var offen = await FamzKorrekturService.OffeneWarnungen(db, 9, 2026, 3).ToListAsync();
        Assert.All(offen, k => Assert.True(k.Jahr * 12 + k.Monat < 2026 * 12 + 3));
        Assert.Contains(offen, k => k.Jahr == 2025);
    }

    [Fact]
    public async Task Rueckwirkend_beendet_Rueckforderung_aus_bezahltem_Lohn()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FamzAnteil_" + Guid.NewGuid()).Options);
        db.Employments.Add(new Employment
        {
            EmployeeId = 10, CompanyProfileId = 2, IsActive = true,
            ContractStartDate = new DateTime(2025, 1, 1)
        });
        var kind = new EmployeeFamilyMember { EmployeeId = 10, MemberType = "Kind", FirstName = "Lea" };
        db.EmployeeFamilyMembers.Add(kind);
        await db.SaveChangesAsync();
        var az = new FamilyMemberAllowance
        {
            FamilyMemberId = kind.Id, AllowanceType = "AZ", MonthlyAmount = 310m,
            ValidFrom = new DateOnly(2025, 1, 1), ErfahrenAm = new DateOnly(2025, 1, 1)
        };
        db.FamilyMemberAllowances.Add(az);
        await db.SaveChangesAsync();
        foreach (var m in new[] { 1, 2 })
        {
            var p = new PayrollPeriode
            {
                CompanyProfileId = 2, Year = 2026, Month = m, Status = "abgeschlossen",
                PeriodFrom = new DateOnly(2026, m, 1), PeriodTo = new DateOnly(2026, m, 1).AddMonths(1).AddDays(-1)
            };
            db.PayrollPerioden.Add(p);
            await db.SaveChangesAsync();
            db.PayrollSnapshots.Add(new PayrollSnapshot
            {
                EmployeeId = 10, CompanyProfileId = 2, PayrollPeriodeId = p.Id, Status = "BESTAETIGT",
                SlipJson = $"{{\"famzZahlungen\":[{{\"allowanceId\":{az.Id},\"familyMemberId\":{kind.Id},\"betrag\":310}}]}}"
            });
        }
        await db.SaveChangesAsync();

        // Lehrabbruch im Januar erst im März erfahren
        az.ValidTo = new DateOnly(2026, 1, 31);
        await db.SaveChangesAsync();
        await new FamzKorrekturService(db).ErzeugeKorrekturenAsync(az, "Test");

        var feb = Assert.Single(await FamzKorrekturService.OffeneWarnungen(db, 10, 2026, 3).ToListAsync());
        Assert.Equal((2026, 2), (feb.Jahr, feb.Monat));
        Assert.Equal(-310m, feb.Betrag);
    }
}
