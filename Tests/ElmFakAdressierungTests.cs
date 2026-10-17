using HrSystem.Models;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec S_ELM_V6_01_01_05 — Nullmeldung FAK (Direktive 8.2.4):
/// Im Job nur FAKs, die einem Arbeitsort am Stichtag zugeordnet sind —
/// auch ohne Personen an diesem Ort. Abgelaufene / nicht zugewiesene raus.
/// </summary>
public class ElmFakAdressierungTests
{
    static LohndatenEmpfaenger Fak(string nr, string name, params CompanyProfileEmpfaenger[] z) =>
        new()
        {
            Art = "FAK",
            Kassennummer = nr,
            Bezeichnung = name,
            IsActive = true,
            KantonCode = "BE",
            Zuordnungen = z.ToList()
        };

    static CompanyProfileEmpfaenger Zu(int filiale, string mitglied, DateOnly? ab = null, DateOnly? bis = null) =>
        new()
        {
            CompanyProfileId = filiale,
            Mitgliednummer = mitglied,
            GueltigAb = ab,
            GueltigBis = bis,
            IsActive = true
        };

    [Fact]
    public void Nur_zugeordnete_FAKs_am_Stichtag()
    {
        var stichtag = new DateOnly(2025, 12, 31);
        var katalog = new[]
        {
            Fak("003.000", "FAK LU", Zu(10, "100-1")),
            Fak("002.000", "FAK BE", Zu(20, "100-2")),
            Fak("099.000", "FAK alt", Zu(10, "alt", bis: new DateOnly(2024, 12, 31))),
            Fak("088.000", "FAK ohne Zuordnung"),
            new LohndatenEmpfaenger
            {
                Art = "AUSGLEICHSKASSE", Kassennummer = "003.000", Bezeichnung = "AK",
                IsActive = true, Zuordnungen = { Zu(10, "ak-1") }
            }
        };
        var workplaces = new HashSet<int> { 10 }; // nur Filiale 10

        var list = ElmFakAdressierung.AusZuordnungen(
            katalog, workplaces, stichtag, id => id == 10 ? "LU" : "BE");

        Assert.Single(list);
        Assert.Equal("003.000", list[0].Kassennummer);
        Assert.Equal("100-1", list[0].CustomerNumber);
        Assert.Equal(new[] { "LU" }, list[0].Kantone);
        Assert.Equal("#FAK-003.000", list[0].AddresseeId);
    }

    [Fact]
    public void Workplace_ohne_Personen_bekommt_trotzdem_FAK()
    {
        // Kern der Nullmeldung: Filiale 30 hat FAK, aber (simuliert) niemand dort —
        // die Adressierung hängt nur an der Zuordnung, nicht an Personenzählung.
        var list = ElmFakAdressierung.AusZuordnungen(
            new[] { Fak("019.000", "FAK AG", Zu(30, "5050.1")) },
            new HashSet<int> { 10, 30 },
            new DateOnly(2025, 12, 31),
            id => id == 30 ? "AG" : "BE");

        Assert.Single(list);
        Assert.Equal("019.000", list[0].Kassennummer);
        Assert.Equal(new[] { "AG" }, list[0].Kantone);
    }

    [Fact]
    public void Gleiche_Kassennummer_mehrere_Filialen_merged_Kantone()
    {
        var list = ElmFakAdressierung.AusZuordnungen(
            new[]
            {
                Fak("079.000", "Spida",
                    Zu(10, "5676.3"),
                    Zu(20, "5676.3"))
            },
            new HashSet<int> { 10, 20 },
            new DateOnly(2025, 12, 31),
            id => id == 10 ? "BE" : "LU");

        Assert.Single(list);
        Assert.Equal(new[] { "BE", "LU" }, list[0].Kantone);
    }

    [Fact]
    public void Job_Institution_Totals_XML()
    {
        var e = new ElmFakAdressierung.Eintrag(
            "#FAK-003.000", "003.000", "100-9976.70", "FAK LU", new[] { "LU" });

        var job = ElmFakAdressierung.JobAddressee(e);
        Assert.Equal("#FAK-003.000", (string?)job.Attribute("addresseeID"));
        Assert.Equal("003.000", job.Element(ElmGemeinsam.Ep + "AddresseeIdentification")?.Value);
        Assert.Equal("true", job.Element(ElmGemeinsam.Ep + "ProcessByDistributor")?.Value);

        var inst = ElmFakAdressierung.Institution(e);
        Assert.Equal("FAK-CAF", inst.Name.LocalName);
        Assert.Equal("#FAK-003.000", (string?)inst.Attribute("addresseeIDRef"));
        Assert.Equal("100-9976.70", inst.Element(ElmGemeinsam.Sd + "FAK-CAF-CustomerNumber")?.Value);

        var tot = ElmFakAdressierung.TotalsNull(e);
        Assert.Equal("0.00", tot.Element(ElmGemeinsam.Sd + "Total-FAK-CAF-PerCanton")
            ?.Element(ElmGemeinsam.Sd + "Total-FAK-CAF-ContributorySalary")?.Value);
        Assert.Equal("LU", tot.Element(ElmGemeinsam.Sd + "Total-FAK-CAF-PerCanton")
            ?.Element(ElmGemeinsam.Sd + "Canton")?.Value);
    }

    [Fact]
    public void Inaktive_oder_fremde_Workplace_raus()
    {
        var fak = Fak("002.000", "FAK BE", Zu(99, "x")); // Filiale 99 nicht in workplaceIds
        fak.IsActive = true;
        var inaktiv = Fak("001.000", "tot", Zu(10, "y"));
        inaktiv.IsActive = false;

        var list = ElmFakAdressierung.AusZuordnungen(
            new[] { fak, inaktiv },
            new HashSet<int> { 10 },
            new DateOnly(2025, 12, 31),
            _ => "BE");

        Assert.Empty(list);
    }
}
