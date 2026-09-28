using System.Xml.Linq;
using HrSystem.Controllers;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Elm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// TF14 Egli Dez 2024 (Walter 28.09.2026): C-Ausweis rückwirkend ab Eintritt, erfahren im
/// Dezember (A0Y → NON, «AwaitCorrectionFromCompany»). Soll: November-QST erstattet,
/// Dezember-Meldung mit Correction-Block (RefXML_2024-12_MONTHLY.xml).
/// </summary>
public class QstKorrekturNonTests
{
    private static readonly XNamespace Sd = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration";
    private static readonly XNamespace C = "urn:ch:swissdec:common:v3:20260306";

    private static EmployeeQuellensteuer Version(int id, string von, string? bis, string erfahren, string? tarif, string? qstCode = null)
        => new()
        {
            Id = id, EmployeeId = 14, Steuerkanton = "LU", QstGemeindeBfsNr = 1069,
            ValidFrom = DateOnly.Parse(von), ValidTo = bis == null ? null : DateOnly.Parse(bis),
            ErfahrenAm = DateOnly.Parse(erfahren),
            TarifCode = tarif, AnzahlKinder = 0, Kirchensteuer = tarif != null, QstCode = qstCode,
        };

    [Fact]
    public void RueckwirkendAb_BeginntMitDerVersionAufDemNovemberBeleg()
    {
        var versionen = new[]
        {
            Version(1, "2024-11-01", "2024-11-30", "2024-11-01", "A"),
            Version(2, "2024-12-01", "2024-11-30", "2024-12-01", "A"),       // Überbleibsel «1.12.–30.11.»
        };
        Assert.Equal(new DateOnly(2024, 11, 1),
            SwissdecTestmandantController.RueckwirkendAb(new DateOnly(2024, 12, 1), versionen));
    }

    [Fact]
    public void RueckwirkendAb_ZweiterLaufFindetDasselbeDatum()
    {
        var versionen = new[]
        {
            Version(1, "2024-11-01", "2024-11-30", "2024-11-01", "A"),
            Version(3, "2024-11-01", null, "2024-12-01", null, "NON"),       // erfahren erst im Dezember
        };
        Assert.Equal(new DateOnly(2024, 11, 1),
            SwissdecTestmandantController.RueckwirkendAb(new DateOnly(2024, 12, 1), versionen));
    }

    [Fact]
    public void RueckwirkendAb_VersionAusDemMutationsmonat_IstNichtRueckwirkend()
    {
        var versionen = new[] { Version(1, "2024-12-01", null, "2024-12-01", "A") };
        Assert.Null(SwissdecTestmandantController.RueckwirkendAb(new DateOnly(2024, 12, 1), versionen));
    }

    [Fact]
    public async Task Korrekturposten_NON_ErstattetDieNovemberQst()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("QstNon_" + Guid.NewGuid()).Options);
        db.PayrollPerioden.Add(new PayrollPeriode { Id = 11, CompanyProfileId = 5, Year = 2024, Month = 11,
            PeriodFrom = new DateOnly(2024, 11, 1), PeriodTo = new DateOnly(2024, 11, 30), Status = "abgeschlossen" });
        db.PayrollSnapshots.Add(new PayrollSnapshot { Id = 1, PayrollPeriodeId = 11, EmployeeId = 14, CompanyProfileId = 5,
            SlipJson = """{"totalLohn":2281.65,"abzugLines":[{"categoryCode":"QST","betrag":-36.05,"basis":2281.65,"satzBasis":2281.65,"qstCode":"A0Y"}]}""" });
        var a0y = Version(1, "2024-11-01", "2024-11-30", "2024-11-01", "A");
        var non = Version(2, "2024-11-01", null, "2024-12-01", null, "NON");
        db.EmployeeQuellensteuer.AddRange(a0y, non);
        await db.SaveChangesAsync();

        var svc = new QstKorrekturService(db, new QuellensteuerTarifService(null!, NullLogger<QuellensteuerTarifService>.Instance));
        await svc.EnsureKorrekturenFuerLohnlaufAsync(14, 2024, 12, "Test");

        var k = Assert.Single(db.QstKorrekturen);
        Assert.Equal((2024, 11), (k.Jahr, k.Monat));
        Assert.Equal("A0Y", k.AlterCode);
        Assert.Equal("NON", k.NeuerCode);
        Assert.Equal(36.05m, k.AlterBetrag);
        Assert.Equal(0m, k.NeuerBetrag);
        Assert.Equal(-36.05m, k.Differenz);
        Assert.Equal("OFFEN", k.Status);
    }

    private static QstKorrektur EgliPosten() => new()
    {
        Jahr = 2024, Monat = 11, AlterCode = "A0Y", NeuerCode = "NON",
        Basis = 2281.65m, SatzBasis = 2281.65m, AlterBetrag = 36.05m, NeuerBetrag = 0m, Differenz = -36.05m,
    };

    [Fact]
    public void Meldung_Egli_WieReferenzAusserAltemCode()
    {
        var x = ElmMonthlyDeclarationBuilder.QstKorrekturBlock(EgliPosten(), "#LU", new DateOnly(2024, 11, 1), "settled-C");
        Assert.Equal("2024-11", x.Element(Sd + "Month")!.Value);
        var alt = x.Element(Sd + "Old")!;
        Assert.Equal("#LU", alt.Attribute("workplaceIDRef")!.Value);
        Assert.Equal("A0Y", alt.Element(Sd + "TaxAtSourceCategory")!.Element(C + "TaxAtSourceCode")!.Value); // Referenz A0N (F1)
        Assert.Equal("-2281.65", alt.Element(Sd + "TaxableEarning")!.Value);
        Assert.Equal("-2281.65", alt.Element(Sd + "AscertainedTaxableEarning")!.Value);
        Assert.Equal("-36.05", alt.Element(Sd + "TaxAtSource")!.Value);                                   // Referenz −34.00 (F1)
        var neu = x.Element(Sd + "New")!;
        Assert.Equal("NON", neu.Element(Sd + "TaxAtSourceCategory")!.Element(C + "CategoryPredefined")!.Value);
        Assert.Equal("0.00", neu.Element(Sd + "TaxableEarning")!.Value);
        Assert.Equal("0.00", neu.Element(Sd + "AscertainedTaxableEarning")!.Value);
        Assert.Equal("0.00", neu.Element(Sd + "TaxAtSource")!.Value);
        var austritt = neu.Element(Sd + "DeclarationCategory")!.Element(Sd + "Withdrawal")!;
        Assert.Equal("2024-11-01", austritt.Element(Sd + "ValidAsOf")!.Value);
        Assert.Equal("settled-C", austritt.Element(Sd + "Reason")!.Value);
    }

    [Fact]
    public void Summen_Egli_KorrekturMonat()
    {
        var (basis, steuer) = ElmMonthlyDeclarationBuilder.QstKorrekturWirkung(EgliPosten());
        Assert.Equal(-2281.65m, basis);
        Assert.Equal(-36.05m, steuer);
    }

    [Theory]
    [InlineData("A0Y", "NON", "C", "DE", "settled-C")]
    [InlineData("A0Y", "NON", "B", "CH", "naturalization")]
    [InlineData("A0N", "B0N", null, "IT", "civilstate")]          // TF31 Bolletto Jun 2025
    [InlineData("B0Y", "B1Y", null, "CH", "childrenDeduction")]   // TF33 Châtelain Jul 2025
    [InlineData("A0N", "A0Y", null, "DE", "churchTax")]
    public void Grund(string alt, string neu, string? bewilligung, string nat, string soll)
    {
        var k = new QstKorrektur { AlterCode = alt, NeuerCode = neu };
        Assert.Equal(soll, ElmMonthlyDeclarationBuilder.QstKorrekturGrund(k, bewilligung, nat, "BE", "BE"));
    }

    [Fact]
    public void Mutation_BolettoJuni_WieReferenz()
    {
        var k = new QstKorrektur { Jahr = 2025, Monat = 4, AlterCode = "A0N", NeuerCode = "B0N",
            Basis = 5000m, SatzBasis = 5000m, AlterBetrag = 419m, NeuerBetrag = 117m, Differenz = -302m };
        var x = ElmMonthlyDeclarationBuilder.QstKorrekturBlock(k, "#VD", new DateOnly(2025, 4, 1), "civilstate");
        var neu = x.Element(Sd + "New")!;
        Assert.Equal("5000.00", neu.Element(Sd + "TaxableEarning")!.Value);
        Assert.Equal("117.00", neu.Element(Sd + "TaxAtSource")!.Value);
        Assert.Equal("civilstate", neu.Element(Sd + "DeclarationCategory")!.Element(Sd + "Mutation")!.Element(Sd + "Reason")!.Value);
        Assert.Equal((0m, -302m), ElmMonthlyDeclarationBuilder.QstKorrekturWirkung(k));
    }
}
