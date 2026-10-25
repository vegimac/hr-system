using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// ELM-Monatsmeldung: Zivilstand und Wohnort am Monatsende, nicht heutiger MA-Stand
/// (Walter 16.10.2026 — QT Jan 2025: Mutationen wirkten zu früh).
/// </summary>
public class ElmStichtagHistorieTests
{
    [Fact]
    public void Zivilstand_VorHeiratsdatum_NochLedig()
    {
        var hist = new[]
        {
            new EmployeeZivilstandHistory { Id = 1, Zivilstand = "ledig", GueltigAb = null },
            new EmployeeZivilstandHistory
            {
                Id = 2, Zivilstand = "verheiratet", GueltigAb = new DateOnly(2025, 2, 1),
                ErfahrenAm = new DateOnly(2025, 2, 1),
            },
        };
        var (z, seit, aus) = ZivilstandHistorieService.Am(hist, new DateOnly(2025, 1, 31), "verheiratet", new DateOnly(2025, 2, 1));
        Assert.Equal("ledig", z);
        Assert.Null(seit);
        Assert.True(aus);
    }

    [Fact]
    public void Zivilstand_AbHeiratsmonat_Verheiratet()
    {
        var hist = new[]
        {
            new EmployeeZivilstandHistory { Id = 1, Zivilstand = "ledig", GueltigAb = null },
            new EmployeeZivilstandHistory
            {
                Id = 2, Zivilstand = "verheiratet", GueltigAb = new DateOnly(2025, 2, 1),
                ErfahrenAm = new DateOnly(2025, 2, 1),
            },
        };
        var (z, seit, _) = ZivilstandHistorieService.Am(hist, new DateOnly(2025, 2, 28), "ledig", null);
        Assert.Equal("verheiratet", z);
        Assert.Equal(new DateOnly(2025, 2, 1), seit);
    }

    [Fact]
    public void Zivilstand_SpaeterErfahren_ZaehltNochNicht()
    {
        var hist = new[]
        {
            new EmployeeZivilstandHistory { Id = 1, Zivilstand = "ledig", GueltigAb = null },
            new EmployeeZivilstandHistory
            {
                Id = 2, Zivilstand = "verheiratet", GueltigAb = new DateOnly(2025, 1, 15),
                ErfahrenAm = new DateOnly(2025, 3, 1),
            },
        };
        var (z, _, _) = ZivilstandHistorieService.Am(hist, new DateOnly(2025, 1, 31), "ledig", null);
        Assert.Equal("ledig", z);
    }

    [Fact]
    public void Wohnort_VorUmzug_NochAusland()
    {
        var hist = new[]
        {
            new EmployeeWohnortHistory
            {
                Id = 1, Plz = "81669", Ort = "München", Strasse = "Rosenheimer Str. 1",
                KantonCode = null, Land = "DE", GueltigAb = null,
            },
            new EmployeeWohnortHistory
            {
                Id = 2, Plz = "3011", Ort = "Bern", Strasse = "Bundesplatz 1",
                KantonCode = "BE", Land = "CH", GueltigAb = new DateOnly(2025, 4, 1),
            },
        };
        var s = WohnortHistorieService.Am(hist, new DateOnly(2025, 1, 31),
            "Bundesplatz 1", "3011", "Bern", "BE", "CH");
        Assert.Equal("81669", s.Plz);
        Assert.Equal("München", s.Ort);
        Assert.Equal("DE", s.Land);
        Assert.True(s.AusHistorie);
    }

    [Fact]
    public void Wohnort_NachUmzug_Schweiz()
    {
        var hist = new[]
        {
            new EmployeeWohnortHistory
            {
                Id = 1, Plz = "81669", Ort = "München", Land = "DE", GueltigAb = null,
            },
            new EmployeeWohnortHistory
            {
                Id = 2, Plz = "3011", Ort = "Bern", KantonCode = "BE", Land = "CH",
                GueltigAb = new DateOnly(2025, 4, 1),
            },
        };
        var s = WohnortHistorieService.Am(hist, new DateOnly(2025, 4, 30),
            "81669", "München", null, null, "DE");
        Assert.Equal("3011", s.Plz);
        Assert.Equal("Bern", s.Ort);
        Assert.Equal("CH", s.Land);
        Assert.Equal("BE", s.KantonCode);
    }

    [Fact]
    public void Wohnort_SchweizerKantonOhneLand_IstCH()
    {
        var hist = new[]
        {
            new EmployeeWohnortHistory
            {
                Id = 1, Plz = "6003", Ort = "Luzern", KantonCode = "LU", Land = null, GueltigAb = null,
            },
        };
        var s = WohnortHistorieService.Am(hist, new DateOnly(2025, 1, 31), null, null, null, null, null);
        Assert.Equal("CH", s.Land);
    }

    [Fact]
    public void Particulars_Stichtag_ZivilstandUndAdresse()
    {
        var e = new Employee
        {
            EmployeeNumber = "30", FirstName = "Heinrich", LastName = "Müller",
            Gender = "male", DateOfBirth = new DateTime(1980, 1, 1),
            SocialSecurityNumber = "7569217076985",
            NationalityRef = new Nationality { Code = "CH" },
            MaritalStatus = "verheiratet", MaritalStatusSince = new DateOnly(2025, 6, 1),
            Street = "Bundesplatz 1", ZipCode = "3011", City = "Bern", CantonCode = "BE", Country = "CH",
        };
        var warn = new List<string>();
        var xml = ElmGemeinsam.Particulars(e, warn, wohnGemeindeNr: null,
            zivilstandAmStichtag: "ledig", zivilstandSeit: null,
            adresseAmStichtag: new ElmGemeinsam.PersonAdresseAm(
                "Rosenheimer Str. 1", "81669", "München", "DE", null));

        Assert.Equal("single", xml.Element(ElmGemeinsam.C + "CivilStatus")!
            .Element(ElmGemeinsam.C + "Status")!.Value);
        var adr = xml.Element(ElmGemeinsam.C + "Addresses")!.Element(ElmGemeinsam.C + "Address")!;
        Assert.Equal("81669", adr.Element(ElmGemeinsam.C + "ZIP-Code")!.Value);
        Assert.Equal("München", adr.Element(ElmGemeinsam.C + "City")!.Value);
        Assert.Equal("GERMANY", adr.Element(ElmGemeinsam.C + "Country")!.Value);
        Assert.Equal("EX", adr.Element(ElmGemeinsam.C + "ResidenceCanton")!.Value);
    }

    [Fact]
    public void Umzug_im_Monat_meldet_alte_Adresse_mit_Wegzug()
    {
        var hist = new[]
        {
            new EmployeeWohnortHistory
            {
                Id = 1, Strasse = "Alt 1", Plz = "6003", Ort = "Luzern", KantonCode = "LU", Land = "CH",
                GueltigAb = new DateOnly(2024, 1, 1),
            },
            new EmployeeWohnortHistory
            {
                Id = 2, Strasse = "Neu 2", Plz = "3011", Ort = "Bern", KantonCode = "BE", Land = "CH",
                GueltigAb = new DateOnly(2025, 4, 15),
            },
        };
        var u = WohnortHistorieService.UmzugImMonat(hist, new DateOnly(2025, 4, 1), new DateOnly(2025, 4, 30));
        Assert.NotNull(u);
        Assert.Equal("6003", u.Value.Alt.Plz);
        Assert.Equal(new DateOnly(2025, 4, 14), u.Value.Wegzug);
        Assert.Null(WohnortHistorieService.UmzugImMonat(hist, new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31)));
    }

    [Fact]
    public void Particulars_Nationalitaet_11_und_Ahv_unbekannt()
    {
        var e = new Employee
        {
            EmployeeNumber = "1", FirstName = "Felix", LastName = "Müller",
            Gender = "male", DateOfBirth = new DateTime(1990, 1, 1),
            SocialSecurityNumber = "unbekannt",
            NationalityRef = new Nationality { Code = "11" },
            Street = "A 1", ZipCode = "6003", City = "Luzern", CantonCode = "LU", Country = "CH",
        };
        var warn = new List<string>();
        var xml = ElmGemeinsam.Particulars(e, warn);
        Assert.Equal("11", xml.Element(ElmGemeinsam.C + "Nationality")!.Value);
        Assert.Equal("unknown", xml.Element(ElmGemeinsam.C + "Social-InsuranceIdentification")!
            .Element(ElmGemeinsam.C + "unknown")!.Name.LocalName);
        Assert.DoesNotContain(warn, w => w.Contains("AHV-Nummer fehlt"));
    }
}
