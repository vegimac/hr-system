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

    [Fact]
    public async Task Nachzahlung_fuer_Monat_ohne_OneCrew_Lohnlauf()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FamzAnteil_" + Guid.NewGuid()).Options);
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
    public async Task Vor_Eintritt_keine_Nachzahlung()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FamzAnteil_" + Guid.NewGuid()).Options);
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
}
