using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Niederlassungsbewilligung C befreit von der Quellensteuer — auch in der
/// Variante «C EU/EFTA» (Code <c>C_EU_EFTA</c> aus dem Import), beim MA selbst
/// und beim Ehepartner.
/// </summary>
public class QstCAusweisEuEftaTests
{
    private static readonly DateOnly Stichtag = new(2026, 9, 1);

    private static AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("QstCEuEfta_" + t + "_" + Guid.NewGuid()).Options);

    private static Employee Ma(int id, string? zivilstand = "ledig") => new()
    {
        Id = id,
        FirstName = "Test", LastName = "C" + id,
        EmployeeNumber = "129100" + id,
        IsActive = true,
        MaritalStatus = zivilstand,
        Country = "CH",
    };

    [Theory]
    [InlineData("C", false)]
    [InlineData("C_EU_EFTA", false)]
    [InlineData("B_EU_EFTA", true)]
    public async Task EigenerAusweis(string code, bool pflichtig)
    {
        await using var db = NewDb();
        db.PermitTypes.Add(new PermitType { Id = 1, Code = code, Description = code });
        db.Employees.Add(Ma(1));
        db.EmployeePermitHistories.Add(new EmployeePermitHistory
        {
            Id = 1, EmployeeId = 1, PermitTypeId = 1, ValidFrom = new DateOnly(2020, 1, 1),
        });
        await db.SaveChangesAsync();

        var r = await new QstPflichtCheckService(db).CheckAsync(1, Stichtag);

        Assert.Equal(pflichtig, r.IsQstPflichtig);
        if (!pflichtig) Assert.Equal("C-Ausweis", r.BefreiungsGrund);
    }

    [Fact]
    public async Task EhepartnerMitCEuEftaBefreit()
    {
        await using var db = NewDb();
        db.PermitTypes.Add(new PermitType { Id = 1, Code = "C_EU_EFTA", Description = "C EU/EFTA" });
        db.Employees.Add(Ma(1, "verheiratet"));
        db.EmployeeFamilyMembers.Add(new EmployeeFamilyMember
        {
            Id = 1, EmployeeId = 1, MemberType = "Ehepartner",
            FirstName = "Partner", LastName = "C1",
            PermitTypeId = 1, LebtImHaushalt = true,
        });
        await db.SaveChangesAsync();

        var r = await new QstPflichtCheckService(db).CheckAsync(1, Stichtag);

        Assert.False(r.IsQstPflichtig);
        Assert.Equal("Ehepartner-C", r.BefreiungsGrund);
    }

    [Theory]
    [InlineData("C", true)]
    [InlineData("c_eu_efta", true)]
    [InlineData("CI_EU_EFTA", false)]
    [InlineData("B", false)]
    [InlineData(null, false)]
    public void IstCAusweis(string? code, bool erwartet)
        => Assert.Equal(erwartet, QstPflichtCheckService.IstCAusweis(code));
}
