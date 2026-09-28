using System.Text.Json;
using HrSystem.Models;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Inhaltliche Abweichungen der Monatsmeldung Januar 2025 gegen RefXML_2025-01_MONTHLY.xml
/// (Walter 28.09.2026). Jeder Fall nennt den Testfall der Muster AG, an dem die Regel belegt ist.
/// </summary>
public class ElmMonatJanuar2025Tests
{
    private static JsonElement Slip(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Sozialabgaben_je_Abzug_gerundet()
    {
        // Quality Tool TF18 Blanc: −62.51 → −62.50, −12.97 → −12.95 = −75.45 (nicht −75.50)
        var slip = Slip("""{"abzugLines":[{"categoryCode":"AHV","betrag":-62.51},{"categoryCode":"ALV","betrag":-12.97},{"categoryCode":"ALVZ","betrag":0},{"categoryCode":"BVG","betrag":-76.44}]}""");
        var sozial = ElmMonthlyDeclarationBuilder.Sozialabgaben(slip, out var bvg);
        Assert.Equal(-75.45m, sozial);
        Assert.Equal(-76.45m, bvg);
    }

    [Fact]
    public void Lektionen_aus_Betrag_wenn_Anzahl_fehlt()
    {
        // TF02 Paganini: Lektionenlohn 600 bei CHF 30 = 20 Lektionen
        var slip = Slip("""{"lohnLines":[{"code":"20","anzahl":150,"betrag":4500},{"code":"1006","anzahl":null,"betrag":600}]}""");
        var lohnart = new Dictionary<string, string> { ["20"] = "1005", ["1006"] = "1006" };
        Assert.Equal(20m, ElmMonthlyDeclarationBuilder.Lektionen(slip, lohnart, 30m));
    }

    [Theory]
    [InlineData(1, 1, "doctorate")]           // TF09 Estermann, TF39 Hasler
    [InlineData(1, 2, "universityMaster")]
    [InlineData(1, null, "universityMaster")]
    [InlineData(1, 3, "universityBachelor")]  // TF23 Koller, TF02 Paganini
    [InlineData(2, null, "higherEducationBachelor")]
    [InlineData(2, 2, "higherEducationMaster")]
    [InlineData(6, null, "vocEducationCompl")]
    public void Ausbildung_mit_Hochschultitel(int bfs, int? titel, string soll)
        => Assert.Equal(soll, ElmStatistikCodes.Ausbildung(bfs, titel));

    [Theory]
    [InlineData("doctorate", 1, 1)]
    [InlineData("universityBachelor", 1, 3)]
    [InlineData("universityMaster", 1, 2)]
    [InlineData("higherEducationBachelor", 2, 3)]
    public void Ausbildung_Rueckrichtung_Testmandant(string swissdec, int bfs, int titel)
    {
        Assert.Equal(bfs, ElmStatistikCodes.BfsAusbildung(swissdec));
        Assert.Equal(titel, ElmStatistikCodes.BfsHochschultitel(swissdec));
        Assert.Equal(swissdec, ElmStatistikCodes.Ausbildung(bfs, titel));
    }

    [Fact]
    public void Schichtzulage_ist_Zulage()   // TF02 Paganini: 1070 = 90 → Allowances
        => Assert.Equal(ElmStatistikCodes.Topf.Zulagen, ElmStatistikCodes.TopfFuer(1070));

    [Theory]
    [InlineData("B_EU_EFTA", "annual-B")]
    [InlineData("C_EU_EFTA", "settled-C")]
    [InlineData("L", "shortTerm-L")]
    [InlineData("MV90", "NotificationProcedureForShorttermWork90Days")]   // TF20 Arnold
    [InlineData("MV120", "NotificationProcedureForShorttermWork120Days")] // TF21 Meier
    [InlineData("ANDERE", "othersNotSwiss")]                              // TF30 Müller, TF39 Hasler
    public void Bewilligung_Katalogcodes(string code, string soll)
        => Assert.Equal(soll, ElmGemeinsam.Bewilligung(code));

    [Fact]
    public void Arbeitszeit_Stundenlohn_mit_Wochenstunden_ist_Steady()
    {
        // TF18 Blanc: 8.40 h bei 42 h = 20 %
        var wt = ElmGemeinsam.WorkingTime(new Employment { EmploymentModel = "FLEX", WeeklyHours = 8.4m }, 42m);
        Assert.Equal("Steady", wt.Name.LocalName);
        Assert.Equal("8.40", wt.Element(ElmGemeinsam.C + "WeeklyHours")?.Value);
        Assert.Equal("20.00", wt.Element(ElmGemeinsam.C + "ActivityRate")?.Value);
    }

    [Fact]
    public void Arbeitszeit_Stundenlohn_ohne_Wochenstunden_bleibt_Unsteady()   // FLEX aus easy@work
        => Assert.Equal("Unsteady", ElmGemeinsam.WorkingTime(new Employment { EmploymentModel = "FLEX" }, 42m).Name.LocalName);

    [Fact]
    public void Arbeitszeit_Verwaltungsrat_Unsteady()   // TF39 Hasler
        => Assert.Equal("Unsteady", ElmGemeinsam.WorkingTime(
            new Employment { EmploymentModel = "FIX", EmploymentPercentage = 100m, SwissdecVertragsart = "administrativeBoard" }, 40m).Name.LocalName);

    [Fact]
    public void Aperiodische_QST_Leistungen_ohne_13_Monatslohn()
    {
        var set = ElmMonthlyDeclarationBuilder.AperiodischeQstCodes(new[]
        {
            ("1210", (string?)"1210", true, false),   // Bonus (TF37 Oberli 2000)
            ("1500", (string?)"1500", true, false),   // VR-Honorar (TF39 Hasler)
            ("180.1", (string?)"1200", true, false),  // 13. ML — nie (TF14 Egli)
            ("1070", (string?)"1070", true, true),    // Schichtzulage periodisch
            ("1420", (string?)"1420", false, false),  // nicht QST-pflichtig
            ("1960", (string?)"1960", true, false),   // Beteiligungsrechte, LA Ziffer 5 (TF30 Müller MEY)
            ("1962", (string?)"1962", true, false),   // Mitarbeiteroptionen, LA Ziffer 5
            ("1410", (string?)"1410", true, false),   // Kapitalleistung, LA Ziffer 4
        });
        Assert.Equal(new[] { "1210", "1500" }, set.OrderBy(x => x).ToArray());
    }

    [Theory]
    [InlineData("B0Y", true)]    // TF18 Blanc
    [InlineData("B0N", true)]    // TF24 Utzinger
    [InlineData("C0N", true)]
    [InlineData("T1N", true)]
    [InlineData("A0N", false)]
    [InlineData("H1N", false)]
    [InlineData("NON", false)]
    public void Tarif_mit_Ehepartner(string code, bool soll)
        => Assert.Equal(soll, ElmMonthlyDeclarationBuilder.TarifMitEhepartner(code));

    [Fact]
    public void Ehepartner_mit_eigener_Adresse()
    {
        // TF18 Blanc: Partnerin in Riehen BS, AHV unbekannt, nicht erwerbstätig
        var e = new Employee { FirstName = "Pierre", LastName = "Blanc", Street = "Kramgasse 1", ZipCode = "3011", City = "Bern", Country = "CH", CantonCode = "BE" };
        var p = new EmployeeFamilyMember { MemberType = "Ehepartner", FirstName = "Anita", LastName = "Blanc", DateOfBirth = new DateTime(1994, 6, 29), Erwerbstaetig = false };
        var a = new EmployeeAddress { Street = "Bäumlihofstrasse 385", ZipCode = "4125", City = "Riehen", Country = "CH", Canton = "BS" };
        var warn = new List<string>();
        var mp = ElmMonthlyDeclarationBuilder.Ehepartner(e, p, a, warn)!;
        Assert.NotNull(mp.Element(ElmGemeinsam.Sd + "Social-InsuranceIdentification")?.Element(ElmGemeinsam.C + "unknown"));
        Assert.Equal("Riehen", mp.Element(ElmGemeinsam.Sd + "Address")?.Element(ElmGemeinsam.C + "City")?.Value);
        Assert.Equal("BS", mp.Element(ElmGemeinsam.Sd + "Residence")?.Element(ElmGemeinsam.Sd + "CantonCH")?.Value);
        Assert.Null(mp.Element(ElmGemeinsam.Sd + "WorkOrCompensatory"));
        Assert.Empty(warn);
    }

    [Fact]
    public void Ehepartner_im_Haushalt_hat_Adresse_des_MA_und_Arbeitsort()
    {
        // TF24 Utzinger (ab Okt 2025 Tarif C): gleiche Adresse, erwerbstätig im TI ab 20.09.2025
        var e = new Employee { FirstName = "Jan", LastName = "Utzinger", Street = "Via Lugano 40", ZipCode = "6500", City = "Bellinzona", Country = "CH", CantonCode = "TI" };
        var p = new EmployeeFamilyMember { MemberType = "Ehepartner", FirstName = "Julie", LastName = "Utzinger", DateOfBirth = new DateTime(1983, 7, 7),
                                           SocialSecurityNumber = "7566549907826", Erwerbstaetig = true, ArbeitgeberKanton = "TI", Stellenantritt = new DateTime(2025, 9, 20) };
        var mp = ElmMonthlyDeclarationBuilder.Ehepartner(e, p, null, new List<string>())!;
        Assert.Equal("756.6549.9078.26", mp.Element(ElmGemeinsam.Sd + "Social-InsuranceIdentification")?.Element(ElmGemeinsam.C + "SV-AS-Number")?.Value);
        Assert.Equal("Via Lugano 40", mp.Element(ElmGemeinsam.Sd + "Address")?.Element(ElmGemeinsam.C + "Street")?.Value);
        Assert.Equal("TI", mp.Element(ElmGemeinsam.Sd + "Residence")?.Element(ElmGemeinsam.Sd + "CantonCH")?.Value);
        var w = mp.Element(ElmGemeinsam.Sd + "WorkOrCompensatory");
        Assert.Equal("TI", w?.Element(ElmGemeinsam.Sd + "Workplace")?.Value);
        Assert.Equal("2025-09-20", w?.Element(ElmGemeinsam.Sd + "Start")?.Value);
    }

    [Fact]
    public void Weitere_Erwerbstaetigkeit_Stundenlohn_aus_Stunden()
    {
        // Quality Tool TF18 Blanc: 35 h ÷ 182 h = 19.23 %; Gesamtpensum anderswo 60 %
        var q = new EmployeeQuellensteuer { WeitereBeschaftigungen = true, GesamtpensumWeitereAg = 60m };
        var em = new Employment { EmploymentModel = "FLEX", SwissdecVertragsart = "indefiniteSalaryHrs", WeeklyHours = 8.4m };
        var filiale = new CompanyProfile { NormalWeeklyHours = 42m };
        var oa = ElmMonthlyDeclarationBuilder.WeitereErwerbstaetigkeit(q, em, filiale, Slip("""{"workedHours":35}"""))!;
        Assert.Equal("19.23", oa.Element(ElmGemeinsam.Sd + "HourlyOrLessonSalary")?.Value);
        Assert.Equal("60.00", oa.Element(ElmGemeinsam.Sd + "TotalOtherActivityRate")?.Value);
    }

    [Fact]
    public void Weitere_Erwerbstaetigkeit_Monatslohn_ohne_Gesamtpensum()
    {
        // TF20 Arnold: 40 % bei uns, Pensum anderswo unbekannt
        var q = new EmployeeQuellensteuer { WeitereBeschaftigungen = true };
        var em = new Employment { EmploymentModel = "FIX", SwissdecVertragsart = "fixedSalaryMth", EmploymentPercentage = 40m };
        var oa = ElmMonthlyDeclarationBuilder.WeitereErwerbstaetigkeit(q, em, new CompanyProfile(), Slip("{}"))!;
        Assert.Equal("40.00", oa.Element(ElmGemeinsam.Sd + "MonthlySalary")?.Value);
        Assert.Null(oa.Element(ElmGemeinsam.Sd + "TotalOtherActivityRate"));
    }

    [Fact]
    public void Ohne_weitere_Erwerbstaetigkeit_kein_Element()
        => Assert.Null(ElmMonthlyDeclarationBuilder.WeitereErwerbstaetigkeit(
            new EmployeeQuellensteuer(), new Employment { EmploymentModel = "FIX" }, new CompanyProfile(), Slip("{}")));
}
