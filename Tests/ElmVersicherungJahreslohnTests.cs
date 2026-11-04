using System.Globalization;
using System.Xml.Linq;
using HrSystem.Services.Elm;
using HrSystem.Tests.Swissdec;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Jahresmeldung UVG/UVGZ/KTG pro Person (ELM 6.0 Richtlinien 8.3.7, 8.4, 8.5).
/// Beitragsdauern je Code, Höchstlohn nach Aufrollmethode und die Totale — die Totale
/// werden direkt gegen RefXML_2025-12_RETROSPECTIVE nachgerechnet.
/// </summary>
public class ElmVersicherungJahreslohnTests
{
    static ElmVersicherungJahreslohn.Monat M(int nr, string code, decimal basis, decimal? pflichtig = null,
        decimal brutto = 0, decimal ahv = 0) => new(nr, code, brutto, basis, pflichtig, ahv);

    [Fact]
    public void Codewechsel_beginnt_neue_Beitragsdauer()
    {
        // Referenz TF03 Pia Lusser: KTG 12 bis Mai, KTG 11 ab Juni.
        var a = ElmVersicherungJahreslohn.Abschnitte(new[]
        {
            M(1, "12", 5000, 0), M(2, "12", 5000, 0), M(3, "12", 5000, 0), M(4, "12", 5000, 0), M(5, "12", 5000, 0),
            M(6, "11", 1500, 1500), M(7, "11", 1500, 1500),
        });
        Assert.Equal(2, a.Count);
        Assert.Equal(("12", 1, 5, 25000m), (a[0].Code, a[0].Von, a[0].Bis, a[0].Basis));
        Assert.Equal(("11", 6, 7, 3000m), (a[1].Code, a[1].Von, a[1].Bis, a[1].Basis));
    }

    [Fact]
    public void Zwei_Codes_gleichzeitig_je_eigene_Zeile()
    {
        // KTG 11 Grundlohn + 12 Überschusslohn im selben Monat.
        var a = ElmVersicherungJahreslohn.Abschnitte(new[]
        {
            M(1, "11", 15000, 10000), M(1, "12", 15000, 5000),
            M(2, "11", 15000, 10000), M(2, "12", 15000, 5000),
        });
        Assert.Equal(2, a.Count);
        Assert.Equal(20000m, a.Single(x => x.Code == "11").Beitragspflichtig);
        Assert.Equal(10000m, a.Single(x => x.Code == "12").Beitragspflichtig);
    }

    [Fact]
    public void Luecke_unterbricht_Beitragsdauer()
    {
        // Austritt März, Wiedereintritt Juli: zwei Beitragsdauern mit demselben Code.
        var a = ElmVersicherungJahreslohn.Abschnitte(new[] { M(1, "A1", 1), M(2, "A1", 1), M(7, "A1", 1) });
        Assert.Equal(2, a.Count);
        Assert.Null(a[0].Beitragspflichtig);
    }

    [Fact]
    public void Hoechstlohn_ganze_Monate_und_Teilmonat()
    {
        // Jan–Mär = 3 × 12'350 = 37'050 (Referenz UVG A1, Beitragsdauer 01.01.–31.03.2025).
        Assert.Equal(37050m, ElmVersicherungJahreslohn.Hoechstlohn(new DateTime(2025, 1, 1), new DateTime(2025, 3, 31), 12350m));
        // Eintritt 28.02. (Monatsletzter) bis 01.03.: 2 Tage = 823.33 (Referenz TF15 Degelo 823.35).
        Assert.Equal(12350m * 2 / 30m,
            ElmVersicherungJahreslohn.Hoechstlohn(new DateTime(2025, 2, 28), new DateTime(2025, 3, 1), 12350m));
        // Eintritt 27.02.: 4 Tage (TF12 Casanova 1'646.65).
        Assert.Equal(12350m * 4 / 30m,
            ElmVersicherungJahreslohn.Hoechstlohn(new DateTime(2025, 2, 27), new DateTime(2025, 2, 28), 12350m));
        // Austritt 20.12.: 20/30 × 12'350.
        Assert.Equal(12350m * 20 / 30m,
            ElmVersicherungJahreslohn.Hoechstlohn(new DateTime(2024, 12, 1), new DateTime(2024, 12, 20), 12350m));
    }

    [Theory]
    [InlineData("A0", false)]
    [InlineData("A1", true)]
    [InlineData("A3", true)]
    public void Uvg_Code_0_nicht_versichert(string code, bool versichert)
        => Assert.Equal(versichert, ElmVersicherungJahreslohn.UvgVersichert(code));

    [Fact]
    public void Uvg_Totale_wie_Referenz_Dezember_2025()
    {
        var (personen, totals) = Referenz();
        var zeilen = personen.SelectMany(p => p.Salaries("UVG-LAA-Salary", "UVG-LAA-Code", "UVG-LAA-ContributorySalary")).ToList();
        var b = ElmVersicherungJahreslohn.UvgTotale(zeilen).Single();
        var t = totals.Element(Sd + "UVG-LAA-Totals")!;
        var soll = t.Descendants(Sd + "UVG-LAA-BranchTotal").Single();
        Assert.Equal(soll.Element(Sd + "BranchIdentifier")!.Value, b.Betriebsteil);
        Assert.Equal(D(soll.Element(Sd + "Female-Totals")!.Element(Sd + "NBU-BU-ANP-AP-Total")!), b.FrauNbu);
        Assert.Equal(D(soll.Element(Sd + "Female-Totals")!.Element(Sd + "BU-AP-Total")!), b.FrauBu);
        Assert.Equal(D(soll.Element(Sd + "Male-Totals")!.Element(Sd + "NBU-BU-ANP-AP-Total")!), b.MannNbu);
        Assert.Equal(D(soll.Element(Sd + "Male-Totals")!.Element(Sd + "BU-AP-Total")!), b.MannBu);
        Assert.Equal(D(t.Element(Sd + "UVG-LAA-MasterTotal")!), b.FrauNbu + b.FrauBu + b.MannNbu + b.MannBu);
    }

    [Theory]
    [InlineData("UVGZ-LAAC")]
    [InlineData("KTG-AMC")]
    public void Kategorie_Totale_wie_Referenz_Dezember_2025(string prefix)
    {
        var (personen, totals) = Referenz();
        var zeilen = personen.SelectMany(p => p.Salaries($"{prefix}-Salary", $"{prefix}-Code", $"{prefix}-ContributorySalary")).ToList();
        var t = totals.Element(Sd + $"{prefix}-Totals")!;
        var soll = t.Descendants(Sd + $"{prefix}-CategoryTotal").ToList();
        var ist = ElmVersicherungJahreslohn.KategorieTotale(zeilen, soll.Select(s => s.Element(Sd + "CategoryCode")!.Value));
        Assert.Equal(soll.Count, ist.Count);
        foreach (var s in soll)
        {
            var i = ist.Single(x => x.Code == s.Element(Sd + "CategoryCode")!.Value);
            Assert.Equal(D(s.Element(Sd + "Female-Total")!), i.Frau);
            Assert.Equal(D(s.Element(Sd + "Male-Total")!), i.Mann);
        }
        Assert.Equal(D(t.Element(Sd + $"{prefix}-MasterTotal")!), ist.Sum(x => x.Frau + x.Mann));
    }

    // ── Referenz lesen ──────────────────────────────────────────────────────
    static readonly XNamespace Sd = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration";
    static readonly XNamespace C = "urn:ch:swissdec:common:v3:20260306";
    static decimal D(XElement e) => decimal.Parse(e.Value.Trim(), CultureInfo.InvariantCulture);

    sealed record RefPerson(XElement P)
    {
        public IEnumerable<ElmVersicherungJahreslohn.Zeile> Salaries(string element, string code, string betrag)
        {
            var sex = P.Descendants(C + "Sex").First().Value.Trim();
            return P.Descendants(Sd + element).Select(s =>
                new ElmVersicherungJahreslohn.Zeile(s.Element(Sd + code)!.Value.Trim(), sex, D(s.Element(Sd + betrag)!)));
        }
    }

    static (List<RefPerson> Personen, XElement Totals) Referenz()
    {
        var pfad = Path.Combine(TestmandantDaten.RepoRoot, "SWISSCEC", "RefXML", "RefXML_2025-12_RETROSPECTIVE.xml");
        var doc = XDocument.Load(pfad);
        return (doc.Descendants(Sd + "Person").Select(p => new RefPerson(p)).ToList(),
                doc.Descendants(Sd + "SalaryTotals").Single());
    }
}

/// <summary>
/// Ganze Jahresmeldung aus der Datenbank: UVG/UVGZ/KTG pro Person, Versicherer im Job und in den
/// Institutionen, Totale und Zähler — XSD-gültig.
/// </summary>
public class ElmVersicherungJahresmeldungTests
{
    static readonly System.Xml.Linq.XNamespace Sd = HrSystem.Services.Elm.ElmGemeinsam.Sd;

    [Fact]
    public async Task Jahresmeldung_mit_Uvg_Uvgz_Ktg_ist_xsd_gueltig()
    {
        await using var db = await Db();
        var r = await new ElmAnnualDeclarationBuilder(db, new ElmXmlValidator(),
            new ElmEinstellungen(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())).BuildAhvAsync(2025);
        Assert.Empty(r.XsdFehler);
        var root = System.Xml.Linq.XDocument.Parse(r.Xml).Root!;

        var uvg = root.Descendants(Sd + "UVG-LAA-Salary").ToList();
        Assert.Equal(2, uvg.Count);   // A1 Jan–Feb, A3 März
        Assert.Equal("A1", uvg[0].Element(Sd + "UVG-LAA-Code")!.Value);
        Assert.Equal("2025-02-28", uvg[0].Element(Sd + "AccountingTime")!.Elements().Last().Value);
        Assert.Equal("13000.00", uvg[0].Element(Sd + "UVG-LAA-GrossSalary")!.Value);
        Assert.Equal("12000.00", uvg[0].Element(Sd + "UVG-LAA-BaseSalary")!.Value);
        Assert.Equal("12000.00", uvg[0].Element(Sd + "UVG-LAA-ContributorySalary")!.Value);
        // A3 ohne NBU-Abzugszeile: beitragspflichtig = Basis bis zum Höchstlohn.
        Assert.Equal("A3", uvg[1].Element(Sd + "UVG-LAA-Code")!.Value);
        Assert.Equal("6000.00", uvg[1].Element(Sd + "UVG-LAA-ContributorySalary")!.Value);

        var ktg = root.Descendants(Sd + "KTG-AMC-Salary").ToList();
        Assert.Equal(new[] { "11", "12" }, ktg.Select(k => k.Element(Sd + "KTG-AMC-Code")!.Value).ToArray());
        Assert.Equal("15000.00", ktg[0].Element(Sd + "KTG-AMC-ContributorySalary")!.Value);
        Assert.Equal("3000.00", ktg[1].Element(Sd + "KTG-AMC-ContributorySalary")!.Value);
        Assert.Equal("18000.00", ktg[0].Element(Sd + "Reference-AHV-AVS-Salary")!.Value);

        var uvgz = root.Descendants(Sd + "UVGZ-LAAC-Salary").Single();
        Assert.Equal("11", uvgz.Element(Sd + "UVGZ-LAAC-Code")!.Value);

        // Totale: Frau, A1 = BU+NBU 12'000, A3 = nur BU 6'000.
        var b = root.Descendants(Sd + "UVG-LAA-BranchTotal").Single();
        Assert.Equal("12000.00", b.Element(Sd + "Female-Totals")!.Element(Sd + "NBU-BU-ANP-AP-Total")!.Value);
        Assert.Equal("6000.00", b.Element(Sd + "Female-Totals")!.Element(Sd + "BU-AP-Total")!.Value);
        Assert.Equal("18000.00", root.Descendants(Sd + "UVG-LAA-MasterTotal").Single().Value);
        Assert.Equal("18000.00", root.Descendants(Sd + "KTG-AMC-MasterTotal").Single().Value);
        Assert.Equal("2", root.Descendants(Sd + "NumberOf-UVG-LAA-Salary-Tags").Single().Value);
        Assert.Equal("2", root.Descendants(Sd + "NumberOf-KTG-AMC-Salary-Tags").Single().Value);

        var ids = root.Descendants().Where(e => e.Name.LocalName == "AddresseeIdentification").Select(e => e.Value).ToList();
        Assert.Contains("S1000", ids);
        var inst = root.Descendants(Sd + "KTG-AMC").Single(e => e.Parent!.Name.LocalName == "Institutions");
        Assert.Equal("K-77", inst.Element(Sd + "CustomerIdentity")!.Value);
        Assert.Equal("V-12", inst.Element(Sd + "ContractIdentity")!.Value);
    }

    static async Task<HrSystem.Data.AppDbContext> Db()
    {
        var db = new HrSystem.Data.AppDbContext(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<HrSystem.Data.AppDbContext>()
            .UseInMemoryDatabase("VersElm_" + Guid.NewGuid()).Options);
        var filiale = new HrSystem.Models.CompanyProfile
        {
            CompanyName = "Muster AG", BranchName = "Luzern", RestaurantCode = "058",
            Street = "Bahnhofstrasse", HouseNumber = "1", ZipCode = "6003", City = "Luzern",
            Country = "CH", KantonCode = "LU", NormalWeeklyHours = 42,
        };
        db.CompanyProfiles.Add(filiale);
        var nat = new HrSystem.Models.Nationality { Code = "CH", NameDe = "Schweiz" };
        var anna = new HrSystem.Models.Employee
        {
            FirstName = "Anna", LastName = "Muster", EmployeeNumber = "1", Gender = "f",
            DateOfBirth = new DateTime(1985, 12, 10), Country = "CH", City = "Luzern",
            ZipCode = "6003", Street = "Weg 1", SocialSecurityNumber = "756.1234.5678.97",
            MaritalStatus = "ledig", EntryDate = new DateTime(2024, 1, 1), NationalityRef = nat,
        };
        db.Employees.Add(anna);
        await db.SaveChangesAsync();
        db.Employments.Add(new HrSystem.Models.Employment
        {
            EmployeeId = anna.Id, CompanyProfileId = filiale.Id,
            ContractStartDate = new DateTime(2024, 1, 1), WeeklyHours = 42, EmploymentModel = "FIX",
        });
        db.EmployeeVersicherungCodes.AddRange(
            new HrSystem.Models.EmployeeVersicherungCode { EmployeeId = anna.Id, Art = "UVG", Code = "A1", ValidFrom = new DateOnly(2024, 1, 1), ValidTo = new DateOnly(2025, 2, 28) },
            new HrSystem.Models.EmployeeVersicherungCode { EmployeeId = anna.Id, Art = "UVG", Code = "A3", ValidFrom = new DateOnly(2025, 3, 1) },
            new HrSystem.Models.EmployeeVersicherungCode { EmployeeId = anna.Id, Art = "UVGZ", Code = "11", ValidFrom = new DateOnly(2024, 1, 1) },
            new HrSystem.Models.EmployeeVersicherungCode { EmployeeId = anna.Id, Art = "KTG", Code = "11", ValidFrom = new DateOnly(2024, 1, 1) },
            new HrSystem.Models.EmployeeVersicherungCode { EmployeeId = anna.Id, Art = "KTG", Code = "12", ValidFrom = new DateOnly(2024, 1, 1) });
        HrSystem.Models.LohndatenEmpfaenger E(string art, string name, string nr) => new()
        {
            Art = art, Bezeichnung = name, Kassennummer = nr, IsActive = true,
            Zuordnungen = { new HrSystem.Models.CompanyProfileEmpfaenger
            {
                CompanyProfileId = filiale.Id, Mitgliednummer = art == "KTG" ? "K-77" : "K-1",
                Subnummer = art == "KTG" ? "V-12" : "V-1", IsActive = true,
            } },
        };
        db.LohndatenEmpfaengers.AddRange(
            E("AUSGLEICHSKASSE", "Ausgleichskasse Luzern", "003.000"),
            E("UVG", "Backwork", "S1000"), E("UVGZ", "Backwork", "S1000"), E("KTG", "Backwork", "S1000"));
        await db.SaveChangesAsync();

        for (var m = 1; m <= 3; m++)
        {
            var periode = new HrSystem.Models.PayrollPeriode
            {
                CompanyProfileId = filiale.Id, Year = 2025, Month = m, Status = "abgeschlossen",
                PeriodFrom = new DateOnly(2025, m, 1), PeriodTo = new DateOnly(2025, m, DateTime.DaysInMonth(2025, m)),
                Label = $"{m}/2025",
            };
            db.PayrollPerioden.Add(periode);
            await db.SaveChangesAsync();
            var nbu = m < 3 ? """{"categoryCode":"NBUV","basis":6000.00,"betrag":-60.00},""" : "";
            db.PayrollSnapshots.Add(new HrSystem.Models.PayrollSnapshot
            {
                PayrollPeriodeId = periode.Id, EmployeeId = anna.Id, Status = "BESTAETIGT",
                Brutto = 6500, Netto = 5000, SvBasisAhv = 6000, SvBasisNbuv = 6000, SvBasisKtg = 6000,
                SlipJson = "{\"abzugLines\":[" + nbu + """
                  {"categoryCode":"AHV","basis":6000.00,"betrag":-318.00},
                  {"categoryCode":"UVGZ","basis":6000.00,"betrag":-30.00,"loesungsCode":"11"},
                  {"categoryCode":"KTG","basis":5000.00,"betrag":-25.00,"loesungsCode":"11"},
                  {"categoryCode":"KTG","basis":1000.00,"betrag":-5.00,"loesungsCode":"12"}
                ]}
                """,
            });
            await db.SaveChangesAsync();
        }
        return db;
    }

    [Fact]
    public void Empfaenger_zugeordnet_geht_vor_erster_im_Katalog()
    {
        var swica = new HrSystem.Models.LohndatenEmpfaenger { Id = 4, Art = "UVG", Bezeichnung = "Swica" };
        var backwork = new HrSystem.Models.LohndatenEmpfaenger { Id = 19, Art = "UVG", Bezeichnung = "Backwork" };
        backwork.Zuordnungen.Add(new() { CompanyProfileId = 1, IsActive = true });
        var w = HrSystem.Services.Elm.ElmAnnualDeclarationBuilder.WaehleEmpfaenger(new[] { swica, backwork }, new DateOnly(2024, 11, 30), null);
        Assert.Equal(19, w!.Id);
        Assert.Equal(4, HrSystem.Services.Elm.ElmAnnualDeclarationBuilder.WaehleEmpfaenger(new[] { swica }, new DateOnly(2024, 11, 30), null)!.Id);
    }
}
