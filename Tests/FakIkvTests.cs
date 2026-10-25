using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec 8.2.4.2 — interkantonale Vereinbarung auf der Filial-Zuordnung.
/// Ohne Häkchen bleibt die bisherige Kasse. Mit Häkchen geht die Meldung an
/// die kantonale FAK des Hauptsitzes, der Arbeitskanton bleibt in der Meldung,
/// die Zulage folgt dem Tarif des Hauptsitzkantons.
/// </summary>
public class FakIkvTests
{
    static LohndatenEmpfaenger Fak(int id, string nr, string kt, string name) => new()
    {
        Id = id,
        Art = "FAK",
        Kassennummer = nr,
        KantonCode = kt,
        Bezeichnung = name,
        KassenTyp = KassenArt.Kantonal,
        IsActive = true
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
    public void A_ohne_Vereinbarung_bisherige_Kasse_und_Arbeitskanton()
    {
        var lu = Fak(1, "003.000", "LU", "FAK LU");
        lu.Zuordnungen.Add(Zu(10, "LU-1"));
        var zg = Fak(2, "009.000", "ZG", "FAK ZG");
        zg.Zuordnungen.Add(Zu(20, "ZG-1"));

        var list = ElmFakAdressierung.AusZuordnungen(
            new[] { lu, zg }, new HashSet<int> { 10, 20 }, new DateOnly(2025, 12, 31),
            id => id == 10 ? "LU" : "ZG");

        Assert.Equal(2, list.Count);
        Assert.Contains(list, e => e.Kassennummer == "009.000" && e.Kantone.SequenceEqual(new[] { "ZG" }));
        Assert.Contains(list, e => e.Kassennummer == "003.000" && e.CustomerNumber == "LU-1");
        Assert.Equal("ZG", FakIkv.TarifKanton("ZG", false, "LU"));
    }

    [Fact]
    public void B_mit_Vereinbarung_Empfaenger_ist_die_Kasse_des_Hauptsitzes()
    {
        var lu = Fak(1, "003.000", "LU", "FAK LU");
        lu.Zuordnungen.Add(Zu(10, "LU-100"));
        var zg = Fak(2, "009.000", "ZG", "FAK ZG");
        var z = Zu(20, "ZG-1");
        z.IkvAktiv = true;
        z.IkvEmpfaengerId = lu.Id;
        zg.Zuordnungen.Add(z);

        var list = ElmFakAdressierung.AusZuordnungen(
            new[] { lu, zg }, new HashSet<int> { 10, 20 }, new DateOnly(2025, 12, 31),
            id => id == 10 ? "LU" : "ZG");

        var eintrag = Assert.Single(list);
        Assert.Equal("003.000", eintrag.Kassennummer);
        Assert.Equal("LU-100", eintrag.CustomerNumber);
        Assert.Equal(new[] { "LU", "ZG" }, eintrag.Kantone);
        Assert.DoesNotContain(list, e => e.Kassennummer == "009.000");
    }

    [Fact]
    public void C_Tarifkanton_ist_der_Hauptsitz()
    {
        Assert.Equal("LU", FakIkv.TarifKanton("ZG", true, "LU"));
        Assert.Equal("ZG", FakIkv.TarifKanton("ZG", false, "LU"));
    }

    [Fact]
    public void D_Zulage_folgt_dem_Tarif_des_Hauptsitzes()
    {
        var arbeits = new FamilienzulagenTarif { KantonCode = "ZG", KinderzulageSatz1 = 300m };
        var sitz = new FamilienzulagenTarif { KantonCode = "LU", KinderzulageSatz1 = 215m };
        var kanton = FakIkv.TarifKanton("ZG", true, "LU");
        var tarif = kanton == "LU" ? sitz : arbeits;
        var betrag = FamilienzulagenResolverService.ResolveBySatz(tarif, "KZ", 1).Amount;
        Assert.Equal(215m, betrag);
    }

    [Fact]
    public void E_mehrere_Filialen_auf_dieselbe_Kasse()
    {
        var lu = Fak(1, "003.000", "LU", "FAK LU");
        lu.Zuordnungen.Add(Zu(10, "LU-100"));
        var zg = Fak(2, "009.000", "ZG", "FAK ZG");
        var z1 = Zu(20, "ZG-1");
        z1.IkvAktiv = true;
        z1.IkvEmpfaengerId = 1;
        zg.Zuordnungen.Add(z1);
        var ag = Fak(3, "019.000", "AG", "FAK AG");
        var z2 = Zu(30, "AG-1");
        z2.IkvAktiv = true;
        z2.IkvEmpfaengerId = 1;
        ag.Zuordnungen.Add(z2);

        var list = ElmFakAdressierung.AusZuordnungen(
            new[] { lu, zg, ag }, new HashSet<int> { 10, 20, 30 }, new DateOnly(2025, 12, 31),
            id => id == 10 ? "LU" : id == 20 ? "ZG" : "AG");

        var eintrag = Assert.Single(list);
        Assert.Equal("003.000", eintrag.Kassennummer);
        Assert.Equal(new[] { "AG", "LU", "ZG" }, eintrag.Kantone);
    }

    [Fact]
    public void F_Beginn_im_Jahr_gilt_erst_ab_dem_Stichtag()
    {
        var lu = Fak(1, "003.000", "LU", "FAK LU");
        lu.Zuordnungen.Add(Zu(10, "LU-100"));
        var zg = Fak(2, "009.000", "ZG", "FAK ZG");
        zg.Zuordnungen.Add(Zu(20, "ZG-1", bis: new DateOnly(2025, 6, 30)));
        var z = Zu(20, "ZG-1", ab: new DateOnly(2025, 7, 1));
        z.IkvAktiv = true;
        z.IkvEmpfaengerId = 1;
        zg.Zuordnungen.Add(z);

        List<ElmFakAdressierung.Eintrag> Am(DateOnly tag) => ElmFakAdressierung.AusZuordnungen(
            new[] { lu, zg }, new HashSet<int> { 10, 20 }, tag,
            id => id == 10 ? "LU" : "ZG");

        var juni = Am(new DateOnly(2025, 6, 30));
        Assert.Contains(juni, e => e.Kassennummer == "009.000");
        Assert.DoesNotContain(juni, e => e.Kassennummer == "003.000" && e.Kantone.Contains("ZG"));

        var dezember = Am(new DateOnly(2025, 12, 31));
        Assert.Contains(dezember, e => e.Kassennummer == "003.000" && e.Kantone.Contains("ZG"));
        Assert.DoesNotContain(dezember, e => e.Kassennummer == "009.000");
    }

    [Fact]
    public void G_Verband_fremde_Kasse_und_fehlende_Zielkasse_werden_abgelehnt()
    {
        var zg = Fak(2, "009.000", "ZG", "FAK ZG");
        var verband = Fak(9, "046.000", "LU", "GastroSocial");
        verband.KassenTyp = KassenArt.Verband;
        var bern = Fak(4, "002.000", "BE", "FAK BE");
        var lu = Fak(1, "003.000", "LU", "FAK LU");

        Assert.Contains("kantonalen", FakIkv.Pruefen(verband, true, lu, "ZG", "LU")!);
        Assert.Contains("wählen", FakIkv.Pruefen(zg, true, null, "ZG", "LU")!);
        Assert.Contains("kantonale", FakIkv.Pruefen(zg, true, verband, "ZG", "LU")!);
        Assert.Contains("Hauptsitzkanton LU", FakIkv.Pruefen(zg, true, bern, "ZG", "LU")!);
        Assert.Contains("andere", FakIkv.Pruefen(lu, true, lu, "ZG", "LU")!);
        var ohneId = Fak(0, "003.000", "LU", "FAK LU");
        Assert.Contains("gleich", FakIkv.Pruefen(ohneId, true, Fak(0, "003.001", "LU", "andere LU"), "LU", "LU")!);
        Assert.Null(FakIkv.Pruefen(zg, true, lu, "ZG", "LU"));
        Assert.Null(FakIkv.Pruefen(zg, false, null, "ZG", "LU"));
    }

    [Fact]
    public void H_XML_adressiert_die_Zielkasse_und_behaelt_den_Arbeitskanton()
    {
        var lu = Fak(1, "003.000", "LU", "FAK LU");
        lu.Zuordnungen.Add(Zu(10, "100-9976.70"));
        var zg = Fak(2, "009.000", "ZG", "FAK ZG");
        var z = Zu(20, "ZG-1");
        z.IkvAktiv = true;
        z.IkvEmpfaengerId = 1;
        zg.Zuordnungen.Add(z);

        var e = Assert.Single(ElmFakAdressierung.AusZuordnungen(
            new[] { lu, zg }, new HashSet<int> { 20 }, new DateOnly(2025, 12, 31), _ => "ZG"));

        var job = ElmFakAdressierung.JobAddressee(e);
        Assert.Equal("003.000", job.Element(ElmGemeinsam.Ep + "AddresseeIdentification")?.Value);
        var tot = ElmFakAdressierung.TotalsNull(e);
        Assert.Equal("ZG", tot.Descendants().First(x => x.Name.LocalName == "Canton").Value);
        Assert.Equal("0.00", tot.Descendants().First(x => x.Name.LocalName == "Total-FAK-CAF-ContributorySalary").Value);
        var inst = ElmFakAdressierung.Institution(e);
        Assert.Equal("FAK-CAF", inst.Name.LocalName);
        Assert.Equal("003.000", inst.Element(ElmGemeinsam.Sd + "FAK-CAF-CustomerNumber")?.Value);
    }
}
