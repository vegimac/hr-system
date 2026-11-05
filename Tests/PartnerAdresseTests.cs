using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Partner-Adresse mit Verlauf (TF36 Maldini: Bern ab Mai, Como ab September).</summary>
public class PartnerAdresseTests
{
    static EmployeeAddress A(int id, string ort, DateOnly? ab, int emp = 36, string typ = PartnerAdresse.Typ)
        => new() { Id = id, EmployeeId = emp, AddressType = typ, City = ort, ValidFrom = ab };

    static readonly EmployeeFamilyMember Partnerin = new() { EmployeeId = 36, AlternativeAddressId = 3 };

    [Theory]
    [InlineData(2025, 6, 30, "Bern")]
    [InlineData(2025, 8, 31, "Bern")]
    [InlineData(2025, 9, 30, "Como")]
    public void Adresse_am_Stichtag(int j, int m, int t, string ort)
    {
        var verlauf = new[] { A(2, "Bern", new DateOnly(2025, 5, 1)), A(3, "Como", new DateOnly(2025, 9, 1)) };
        Assert.Equal(ort, PartnerAdresse.Am(Partnerin, verlauf, new DateOnly(j, m, t))!.City);
    }

    [Fact]
    public void Vor_der_ersten_eigenen_Adresse_lebt_die_Person_im_Haushalt()
    {
        var verlauf = new[] { A(3, "Como", new DateOnly(2025, 9, 1)) };
        Assert.Null(PartnerAdresse.Am(Partnerin, verlauf, new DateOnly(2025, 6, 30)));
        Assert.Equal("Como", PartnerAdresse.Am(Partnerin, verlauf, new DateOnly(2025, 9, 30))!.City);
    }

    [Fact]
    public void Fremde_Adressen_zaehlen_nicht()
    {
        // TF35 Roos darf nie die Adresse von Maldinis Partnerin bekommen.
        var roos = new EmployeeFamilyMember { EmployeeId = 35, AlternativeAddressId = 2 };
        var adressen = new[] { A(2, "Bern", new DateOnly(2025, 5, 1), emp: 36), A(7, "Bellinzona", new DateOnly(2025, 5, 1), emp: 35) };
        Assert.Equal("Bellinzona", PartnerAdresse.Am(roos, adressen, new DateOnly(2025, 6, 30))!.City);
    }

    [Fact]
    public void Ohne_Verlauf_die_verknuepfte_Adresse_im_Haushalt_keine()
    {
        var adressen = new[] { A(5, "Riehen", null, typ: "Korrespondenzadresse") };
        Assert.Equal("Riehen", PartnerAdresse.Am(new EmployeeFamilyMember { EmployeeId = 36, AlternativeAddressId = 5 }, adressen, new DateOnly(2025, 6, 30))!.City);
        Assert.Null(PartnerAdresse.Am(new EmployeeFamilyMember { EmployeeId = 36, LebtImHaushalt = true, AlternativeAddressId = null }, adressen, new DateOnly(2025, 6, 30)));
    }
}
