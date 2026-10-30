using System.Xml.Linq;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.Elm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Die drei Grenzgängermeldungen sind je eine eigene Jahresmeldung und enthalten
/// nur ihre eigene Lohnzeile (kein AHV, keine FAK, nicht die anderen beiden Arten).
/// </summary>
public class ElmGrenzgaengerJahresTests
{
    [Fact]
    public async Task Italien_enthaelt_nur_die_italienische_Zeile_und_die_Betraege()
    {
        await using var db = await Db();
        var r = await Bau(db).BuildAsync(ElmGrenzgaengerJahresBuilder.ArtItalien, 2024);
        Assert.Empty(r.XsdFehler);
        var root = XDocument.Parse(r.Xml).Root!;
        Assert.Equal("DeclareAnnualSalary", root.Name.LocalName);
        Assert.Single(root.Descendants().Where(e => e.Name.LocalName == "ItalianCrossborderSalary"));
        Assert.DoesNotContain(root.Descendants(), e => e.Name.LocalName is "FrenchCrossborderSalary" or "SFNCrossborderSalary" or "AHV-AVS-Salary" or "AHV-AVS");
        var zeile = root.Descendants().Single(e => e.Name.LocalName == "ItalianCrossborderSalary");
        Assert.Equal("Como", zeile.Element(ElmGemeinsam.Sd + "PlaceOfBirth")?.Value);
        Assert.Equal("RSSMRA85T10A562S", zeile.Element(ElmGemeinsam.Sd + "TaxID")?.Value);
        Assert.Equal("2024-03-01", zeile.Element(ElmGemeinsam.Sd + "CrossborderValidAsOf")?.Value);
        Assert.Equal("1000.00", zeile.Element(ElmGemeinsam.Sd + "TaxableEarning")?.Value);
        Assert.Equal("50.00", zeile.Element(ElmGemeinsam.Sd + "DeductionAtSource")?.Value);
        Assert.Equal("71.00", zeile.Element(ElmGemeinsam.Sd + "AHV-ALV-NBUV-AVS-AC-AANP-Contribution")?.Value);
        Assert.Equal("40.00", zeile.Element(ElmGemeinsam.Sd + "BVG-LPP-ContributionRegular")?.Value);
        Assert.Equal("TI", root.Descendants().Single(e => e.Name.LocalName == "AddresseeIdentification").Value);
    }

    [Fact]
    public async Task Frankreich_laesst_SFN_und_Italien_draussen_und_meldet_Telearbeit()
    {
        await using var db = await Db();
        var r = await Bau(db).BuildAsync(ElmGrenzgaengerJahresBuilder.ArtFrankreich, 2024);
        Assert.Empty(r.XsdFehler);
        var root = XDocument.Parse(r.Xml).Root!;
        Assert.Single(root.Descendants().Where(e => e.Name.LocalName == "FrenchCrossborderSalary"));
        Assert.DoesNotContain(root.Descendants(), e => e.Name.LocalName is "ItalianCrossborderSalary" or "SFNCrossborderSalary");
        var zeile = root.Descendants().Single(e => e.Name.LocalName == "FrenchCrossborderSalary");
        Assert.Equal("40.00", zeile.Element(ElmGemeinsam.Sd + "TeleWorkPercentage")?.Value);
        Assert.Equal("GE", zeile.Element(ElmGemeinsam.Sd + "TaxAtSourceCanton")?.Value);
        Assert.DoesNotContain("Claire", root.ToString());
    }

    [Fact]
    public async Task Sfn_enthaelt_nur_den_Tarif_SFN()
    {
        await using var db = await Db();
        var r = await Bau(db).BuildAsync(ElmGrenzgaengerJahresBuilder.ArtSfn, 2024);
        Assert.Empty(r.XsdFehler);
        var root = XDocument.Parse(r.Xml).Root!;
        Assert.Single(root.Descendants().Where(e => e.Name.LocalName == "SFNCrossborderSalary"));
        Assert.DoesNotContain(root.Descendants(), e => e.Name.LocalName is "ItalianCrossborderSalary" or "FrenchCrossborderSalary");
        Assert.Contains("Claire", root.ToString());
        var zeile = root.Descendants().Single(e => e.Name.LocalName == "SFNCrossborderSalary");
        Assert.Equal("BE", zeile.Element(ElmGemeinsam.Sd + "TaxAtSourceCanton")?.Value);
    }

    [Fact]
    public async Task Ohne_Person_entsteht_kein_Xml()
    {
        await using var db = await Db();
        var r = await Bau(db).BuildAsync(ElmGrenzgaengerJahresBuilder.ArtItalien, 2020);
        Assert.Equal("", r.Xml);
        Assert.Contains("Datenaustausch Italien", string.Join(" ", r.Warnungen));
    }

    [Fact]
    public void Status_und_Sync_laufen_ueber_die_Jahresoperation()
    {
        var status = ElmUebermittlungXml.BaueGetStatus("grenz-fr", "JobKey1234",
            new XElement(ElmGemeinsam.Ep + "RequestContext"));
        Assert.Equal("GetStatusFromDeclareAnnualSalary", status.Name.LocalName);
        var sync = ElmUebermittlungXml.BaueSynchronizeDeclare(
            new ElmVorgang { Art = "grenz-sfn", Uid = "CHE-999.999.996", Firmenname = "Muster AG" },
            new ElmVorgangAdressat
            {
                AddresseeId = "#QST-BE", Identification = "BE", Domain = "TaxCrossborder",
                Zustand = "Success", FallId = "DEC-1", Key = "Key", Password = "Password",
            },
            Array.Empty<string>(),
            new XElement(ElmGemeinsam.Ep + "RequestContext"));
        Assert.Equal("SynchronizeDeclareAnnualSalary", sync.Name.LocalName);
    }

    private static ElmGrenzgaengerJahresBuilder Bau(AppDbContext db)
        => new(db, new ElmEinstellungen(new ConfigurationBuilder().Build()), new ElmXmlValidator());

    private static async Task<AppDbContext> Db()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("GrenzElm_" + Guid.NewGuid()).Options);
        var filiale = new CompanyProfile
        {
            CompanyName = "Muster AG", BranchName = "Luzern", RestaurantCode = "058",
            Street = "Bahnhofstrasse", HouseNumber = "1", ZipCode = "6003", City = "Luzern",
            Country = "CH", KantonCode = "LU", NormalWeeklyHours = 42,
        };
        db.CompanyProfiles.Add(filiale);
        var itNat = new Nationality { Code = "IT", NameDe = "Italien" };
        var frNat = new Nationality { Code = "FR", NameDe = "Frankreich" };
        var mario = Person("Mario", "Rossi", "IT", itNat);
        var anne = Person("Anne", "Martin", "FR", frNat);
        var claire = Person("Claire", "Dubois", "FR", frNat);
        db.Employees.AddRange(mario, anne, claire);
        await db.SaveChangesAsync();

        db.Employments.AddRange(
            Vertrag(mario, filiale), Vertrag(anne, filiale), Vertrag(claire, filiale));
        db.EmployeeQuellensteuer.AddRange(
            new EmployeeQuellensteuer
            {
                EmployeeId = mario.Id, ValidFrom = new DateOnly(2024, 1, 1), Steuerkanton = "TI",
                Wohnsitzstaat = "IT", DatenaustauschItalien = true, QstCode = "A0N",
                GrenzgaengerGeburtsort = "Como", GrenzgaengerGeburtsstaat = "IT",
                GrenzgaengerSteuerId = "RSSMRA85T10A562S", GrenzgaengerAb = new DateOnly(2024, 3, 1),
            },
            new EmployeeQuellensteuer
            {
                EmployeeId = anne.Id, ValidFrom = new DateOnly(2024, 1, 1), Steuerkanton = "GE",
                Wohnsitzstaat = "FR", QstCode = "A0N", TelearbeitProzent = 40,
            },
            new EmployeeQuellensteuer
            {
                EmployeeId = claire.Id, ValidFrom = new DateOnly(2024, 1, 1), Steuerkanton = "BE",
                Wohnsitzstaat = "FR", QstCode = "SFN",
            });
        db.LohndatenEmpfaengers.AddRange(
            Kasse("TI"), Kasse("GE"), Kasse("BE"));
        var periode = new PayrollPeriode
        {
            CompanyProfileId = filiale.Id, Year = 2024, Month = 3, Status = "abgeschlossen",
            PeriodFrom = new DateOnly(2024, 3, 1), PeriodTo = new DateOnly(2024, 3, 31), Label = "März 2024",
        };
        db.PayrollPerioden.Add(periode);
        await db.SaveChangesAsync();
        db.PayrollSnapshots.Add(new PayrollSnapshot
        {
            PayrollPeriodeId = periode.Id, EmployeeId = mario.Id, Status = "BESTAETIGT",
            Brutto = 1000, Netto = 800,
            SlipJson = """
                {"abzugLines":[
                  {"categoryCode":"QST","basis":1000.00,"betrag":-50.00},
                  {"categoryCode":"AHV","betrag":-53.00},
                  {"categoryCode":"ALV","betrag":-11.00},
                  {"categoryCode":"NBUV","betrag":-7.00},
                  {"categoryCode":"BVG","betrag":-40.00}
                ]}
                """,
        });
        await db.SaveChangesAsync();
        return db;
    }

    private static Employee Person(string vor, string nach, string land, Nationality nat) => new()
    {
        FirstName = vor, LastName = nach, EmployeeNumber = vor, Gender = "f",
        DateOfBirth = new DateTime(1985, 12, 10), Country = land, City = "Ausland",
        ZipCode = "22100", Street = "Via Roma 1", SocialSecurityNumber = "756.1234.5678.97",
        MaritalStatus = "ledig", EntryDate = new DateTime(2024, 1, 1), NationalityRef = nat,
    };

    private static Employment Vertrag(Employee e, CompanyProfile filiale) => new()
    {
        EmployeeId = e.Id, CompanyProfileId = filiale.Id,
        ContractStartDate = new DateTime(2024, 1, 1), WeeklyHours = 40, EmploymentModel = "FIX",
    };

    private static LohndatenEmpfaenger Kasse(string kanton) => new()
    {
        Art = "QST", Bezeichnung = "Steuer " + kanton, KantonCode = kanton, Kassennummer = "12345", IsActive = true,
    };
}
