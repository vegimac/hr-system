using HrSystem.Models;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Gründe der QST-Mutation aus dem Tarifcode (RefXML Juni/Juli 2025).</summary>
public class ElmQstMutationTests
{
    [Theory]
    [InlineData("A0N", "B0N", "civilstate")]                  // TF23 Koller Heirat
    [InlineData("A0N", "C0N", "partnerWork,civilstate")]      // TF22 Bucher Heirat + Partner erwerbstätig
    [InlineData("B0Y", "C0Y", "partnerWork")]                 // TF36 Maldini August
    [InlineData("B0Y", "B1Y", "childrenDeduction")]           // TF33 Châtelain Kind
    [InlineData("A0N", "A0Y", "churchTax")]
    [InlineData("A0N", "A0N", "")]
    [InlineData("A0Y", "SFN", "")]                            // TF38 Jung: nur «residence» aus dem Wohnsitz
    [InlineData("MEY", "A0Y", "")]                            // TF30 Müller Mai
    public void Gruende(string alt, string neu, string erwartet)
        => Assert.Equal(erwartet, string.Join(",", ElmMonthlyDeclarationBuilder.MutationsGruende(alt, neu)));
}

/// <summary>Reihenfolge der CorrectionMonth-Totale wie die Swissdec-Referenz (Java-HashMap).</summary>
public class ElmKorrekturReihenfolgeTests
{
    [Theory]
    [InlineData("2025-04,2025-05", "2025-05,2025-04")]   // RefXML Juni 2025
    [InlineData("2025-05,2025-06", "2025-05,2025-06")]   // RefXML Juli 2025
    public void Wie_Referenz(string ein, string aus)
        => Assert.Equal(aus, string.Join(",", ElmMonthlyDeclarationBuilder.JavaHashReihenfolge(ein.Split(','))));
}

/// <summary>Quality Tool Sep 2025: TF36 Maldini, Arbeitsort der Partnerin TI → EX ab 01.09.</summary>
public class ElmPartnerArbeitsortTests
{
    private static EmployeeFamilyMember Sandra() => new()
    {
        Id = 7, MemberType = "Ehepartner", FirstName = "Sandra", LastName = "Maldini", DateOfBirth = new DateTime(1997, 9, 27),
        Erwerbstaetig = true, ArbeitgeberKanton = "EX", Stellenantritt = new DateTime(2025, 7, 28),
    };

    private static List<FamilyMemberErwerbHistory> Verlauf() => new()
    {
        new() { Id = 1, FamilyMemberId = 7, Erwerbstaetig = true, ArbeitgeberKanton = "TI", ValidFrom = new DateOnly(2025, 7, 28) },
        new() { Id = 2, FamilyMemberId = 7, Erwerbstaetig = true, ArbeitgeberKanton = "EX", ValidFrom = new DateOnly(2025, 9, 1) },
    };

    [Theory]
    [InlineData(2025, 8, 31, "TI")]
    [InlineData(2025, 9, 30, "EX")]
    public void Arbeitsort_aus_dem_Verlauf(int j, int m, int t, string soll)
        => Assert.Equal(soll, ElmMonthlyDeclarationBuilder.PartnerArbeitsort(Sandra(), Verlauf(), new DateOnly(j, m, t)));

    [Fact]
    public void Ohne_Verlauf_gilt_das_Feld()
        => Assert.Equal("EX", ElmMonthlyDeclarationBuilder.PartnerArbeitsort(Sandra(), new List<FamilyMemberErwerbHistory>(), new DateOnly(2025, 8, 31)));

    [Fact]
    public void Ehepartner_meldet_Arbeitsort_am_Stichtag()
    {
        var e = new Employee { FirstName = "Fabio", LastName = "Maldini", Street = "Via Milano 26", ZipCode = "22100", City = "Como", Country = "IT" };
        var aug = ElmMonthlyDeclarationBuilder.Ehepartner(e, Sandra(), null, new List<string>(), new DateOnly(2025, 8, 31), "TI")!;
        Assert.Equal("TI", aug.Element(ElmGemeinsam.Sd + "WorkOrCompensatory")?.Element(ElmGemeinsam.Sd + "Workplace")?.Value);
    }
}

/// <summary>Quality Tool Nov 2025: TF38 Jung, Grenzgänger-Angabe «FR + SFN» in den Testdaten.</summary>
public class ElmWohnsitzstaatTests
{
    [Theory]
    [InlineData("FR + SFN", "FR", "FR")]
    [InlineData("IT", "IT", "IT")]
    [InlineData(null, "DE", "DE")]
    public void Nur_gueltiger_Laendercode(string? staat, string land, string soll)
    {
        var r = ElmMonthlyDeclarationBuilder.QstResidence(null, land, staat, false, null);
        Assert.Equal(soll, r.Element(ElmGemeinsam.Sd + "AbroadCountry")?.Value);
    }

    [Theory]
    [InlineData("FR + SFN", "FR")]
    [InlineData("IT", "IT")]
    [InlineData(null, null)]
    public void Testdaten_nur_das_Land(string? ein, string? aus)
        => Assert.Equal(aus, HrSystem.Controllers.SwissdecTestmandantController.GrenzgaengerLand(ein));
}

/// <summary>RL 10.6.5, Quality Tool Nov 2025 TF30 Müller: Ferienauszahlung nach Austritt = Korrektur des Austrittsmonats.</summary>
public class ElmQstAustrittKorrekturTests
{
    private static System.Text.Json.JsonElement Mueller() => System.Text.Json.JsonDocument.Parse(
        """{"jahr":2025,"monat":9,"code":"A0Y","altBasis":5500,"altSatzBasis":5500,"altSteuer":651.75,"neuBasis":6000,"neuSatzBasis":6000,"neuSteuer":751.80,"aperiodisch":500}""").RootElement;

    [Fact]
    public void Block_wie_Referenz()
    {
        var b = ElmMonthlyDeclarationBuilder.QstAustrittKorrekturBlock(Mueller(), "#BE");
        var sd = ElmGemeinsam.Sd;
        Assert.Equal("2025-09", b.Element(sd + "Month")?.Value);
        Assert.Equal("-5500.00", b.Element(sd + "Old")?.Element(sd + "TaxableEarning")?.Value);
        Assert.Equal("-651.75", b.Element(sd + "Old")?.Element(sd + "TaxAtSource")?.Value);
        Assert.Equal("6000.00", b.Element(sd + "New")?.Element(sd + "AscertainedTaxableEarning")?.Value);
        Assert.Equal("751.80", b.Element(sd + "New")?.Element(sd + "TaxAtSource")?.Value);
        Assert.Equal("500.00", b.Element(sd + "New")?.Element(sd + "SporadicBenefits")?.Value);
    }

    [Fact]
    public void Lohnkorrektur_allein_nicht()
    {
        Assert.True(HrSystem.Services.NachzahlungQstZeit.IstLohnkorrektur("1001"));
        Assert.False(HrSystem.Services.NachzahlungQstZeit.AnspruchMitAustritt("1001"));
    }

    [Fact]
    public void Wirkung_Differenz()
        => Assert.Equal((500m, 100.05m), ElmMonthlyDeclarationBuilder.QstAustrittKorrekturWirkung(Mueller()));

    [Theory]
    [InlineData("1168", null, true)]
    [InlineData("1067", null, true)]
    [InlineData("9000", "1168", true)]
    [InlineData("1061", "1061", true)]   // Überstunden nach Austritt (Produktiv)
    [InlineData("1065", "1065", true)]
    [InlineData("1209", null, false)]   // Bonus: Anspruch nach dem Austritt, Auszahlungsmonat
    [InlineData("1230", null, false)]
    public void Anspruch_mit_Austritt(string code, string? sd, bool soll)
        => Assert.Equal(soll, HrSystem.Services.NachzahlungQstZeit.AnspruchMitAustritt(code, sd));

    [Theory]
    [InlineData("B", 2025, 11, 30, true)]    // TF24 Utzinger B → C ab 01.11.: November noch QST
    [InlineData("B", 2025, 12, 31, false)]   // ab Folgemonat befreit
    [InlineData(null, 2025, 11, 30, false)]  // C schon beim Eintritt: sofort befreit
    [InlineData("C", 2025, 11, 30, false)]   // erneuerte Karte
    public void C_Wechsel_wirkt_ab_Folgemonat(string? vorgaenger, int j, int m, int t, bool nochPflichtig)
        => Assert.Equal(nochPflichtig, HrSystem.Services.QstPflichtCheckService.CWechselNochNichtWirksam(
            new DateOnly(2025, 11, 1), vorgaenger, new DateOnly(j, m, t)));

    [Fact]
    public void C_Ausweis_im_Monat()
    {
        var typen = new Dictionary<int, PermitType> { [7] = new PermitType { Id = 7, Code = "C" }, [6] = new PermitType { Id = 6, Code = "B" } };
        var v = new[] { new EmployeePermitHistory { PermitTypeId = 7, ValidFrom = new DateOnly(2025, 11, 1) } };
        Assert.True(ElmMonthlyDeclarationBuilder.CAusweisImMonat(v, typen, new DateTime(2025, 11, 1), new DateTime(2025, 11, 30)));
        Assert.False(ElmMonthlyDeclarationBuilder.CAusweisImMonat(v, typen, new DateTime(2025, 12, 1), new DateTime(2025, 12, 31)));
    }
}

/// <summary>RL 10.11.1.1: Korrektur durch den Arbeitgeber bis 31.03. des Folgejahres.</summary>
public class QstKorrekturFristTests
{
    [Theory]
    [InlineData(2025, 2025, 11, 1, "OFFEN")]
    [InlineData(2025, 2026, 1, 1, "OFFEN")]    // TF32 Armanini
    [InlineData(2025, 2026, 3, 31, "OFFEN")]
    [InlineData(2025, 2026, 4, 1, "VORJAHR")]
    [InlineData(2024, 2026, 1, 1, "VORJAHR")]
    public void Status(int postenJahr, int j, int m, int t, string soll)
        => Assert.Equal(soll, HrSystem.Services.QstKorrekturService.PostenStatus(postenJahr, new DateOnly(j, m, t)));
}

/// <summary>Arbeitsorte der Monatsmeldung, wie das Quality Tool sie verlangt.</summary>
public class ElmArbeitsortRueckblickTests
{
    [Theory]
    [InlineData(2025, 5, 2025, 8, true)]     // AG im August 2025
    [InlineData(2025, 3, 2025, 12, true)]    // ZG im Dezember 2025
    [InlineData(2025, 3, 2026, 1, false)]    // ZG nicht mehr im Januar 2026
    [InlineData(2025, 10, 2026, 1, true)]    // AG im Januar 2026
    [InlineData(2025, 8, 2025, 8, false)]    // Meldemonat selbst zählt über die gemeldeten Filialen
    public void Rueckblick(int j, int m, int mj, int mm, bool soll)
        => Assert.Equal(soll, ElmMonthlyDeclarationBuilder.ImArbeitsortRueckblick(j, m, mj, mm));
}

/// <summary>Quality Tool Feb 2026 TF29 Forster: Umzug nach dem Austritt ergibt keine Kantonswechsel-Meldung.</summary>
public class ElmNachAustrittTests
{
    private static readonly DateTime Feb = new(2026, 2, 1);

    [Fact]
    public void Ausgetreten_vor_dem_Monat()
        => Assert.True(ElmMonthlyDeclarationBuilder.NachAustritt(new DateTime(2025, 12, 31), new DateTime(2025, 1, 1), Feb));

    [Fact]
    public void Austritt_im_Monat_zaehlt_nicht()
        => Assert.False(ElmMonthlyDeclarationBuilder.NachAustritt(new DateTime(2026, 2, 28), new DateTime(2025, 1, 1), Feb));

    [Fact]
    public void Wiedereintritt_zaehlt_nicht()
        => Assert.False(ElmMonthlyDeclarationBuilder.NachAustritt(new DateTime(2025, 12, 31), new DateTime(2026, 1, 15), Feb));

    [Fact]
    public void Ohne_Austritt()
        => Assert.False(ElmMonthlyDeclarationBuilder.NachAustritt(null, new DateTime(2025, 1, 1), Feb));
}
