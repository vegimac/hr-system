using System.Xml.Linq;
using HrSystem.Models;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Einstellung am Hauptsitz und Empfängerprüfung vor dem Senden (Walter 05.11.2026).</summary>
public class ElmMeldeeinheitTests
{
    private static CompanyProfile F(int id, int? hs) => new() { Id = id, HauptsitzId = hs, CompanyName = "Firma", BranchName = "F" + id };
    private static Hauptsitz H(int id, bool jeFiliale) => new() { Id = id, Name = "H" + id, LohnmeldungJeFiliale = jeFiliale };

    [Fact]
    public void Je_Filiale_meldet_nur_die_gewaehlte_Filiale()
    {
        var e = ElmMeldeeinheit.Bestimme(new[] { F(1, 1), F(2, 1) }, new[] { H(1, true) }, 2, new())!;
        Assert.True(e.JeFiliale);
        Assert.Equal(2, Assert.Single(e.Filialen).Id);
    }

    [Fact]
    public void Ueber_den_Hauptsitz_mit_Filialwahl_behaelt_alle_Arbeitsorte_und_nimmt_den_Lohn_der_Filiale()
    {
        var e = ElmMeldeeinheit.Bestimme(new[] { F(1, 1), F(2, 1) }, new[] { H(1, false) }, 2, new())!;
        Assert.False(e.JeFiliale);
        Assert.Equal(2, e.Filialen.Count);
        Assert.Equal(2, Assert.Single(e.Gewaehlt).Id);
    }

    [Fact]
    public void Jede_Filiale_gehoert_zu_ihrem_Hauptsitz()
    {
        var e = ElmMeldeeinheit.Bestimme(new[] { F(1, 1), F(2, 2) }, new[] { H(1, true), H(2, true) }, 2, new())!;
        Assert.Equal(2, e.Hauptsitz.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ohne_Filialwahl_bei_zwei_Hauptsitzen_entsteht_nichts(bool jeFiliale)
    {
        var warn = new List<string>();
        Assert.Null(ElmMeldeeinheit.Bestimme(new[] { F(1, 1), F(2, 2) }, new[] { H(1, jeFiliale), H(2, jeFiliale) }, null, warn));
        Assert.NotEmpty(warn);
    }

    [Fact]
    public void Filiale_ohne_Hauptsitz_entsteht_nichts()
        => Assert.Null(ElmMeldeeinheit.Bestimme(new[] { F(1, null) }, new[] { H(1, true) }, 1, new()));

    private static ElmVorgang V(bool test, int? jahr = 2026, int? filiale = 1, string art = "monthly", string? url = "https://x")
        => new() { TestCase = test, Jahr = jahr, CompanyProfileId = filiale, Art = art, Url = url };

    [Fact]
    public void Nur_eine_echte_Meldung_im_Lohnjahr_sperrt_die_Einstellung()
    {
        var f = new HashSet<int> { 1 };
        Assert.True(ElmMeldeeinheit.EinstellungGesperrt(new[] { V(false) }, f, 2026));
        Assert.False(ElmMeldeeinheit.EinstellungGesperrt(new[] { V(true) }, f, 2026));
        Assert.False(ElmMeldeeinheit.EinstellungGesperrt(new[] { V(false, jahr: 2025) }, f, 2026));
        Assert.False(ElmMeldeeinheit.EinstellungGesperrt(new[] { V(false, filiale: 9) }, f, 2026));
        Assert.False(ElmMeldeeinheit.EinstellungGesperrt(new[] { V(false, art: "subscribe") }, f, 2026));
        Assert.False(ElmMeldeeinheit.EinstellungGesperrt(new[] { V(false, url: null) }, f, 2026));
    }

    private static readonly DateOnly Von = new(2026, 1, 1), Bis = new(2026, 1, 31);

    private static XElement Meldung(params (string Id, string Kennung, XElement? Inst)[] a) => new("Root",
        new XElement("Job", new XElement("Addressees", a.Select(x => new XElement("Addressee", new XAttribute("addresseeID", x.Id),
            new XElement("AddresseeIdentification", x.Kennung))))),
        new XElement("Institutions", a.Select(x => x.Inst)));

    private static XElement Ahv(string id, string nr) => new("AHV-AVS", new XAttribute("addresseeIDRef", id), new XElement("AK-CC-CustomerNumber", nr));

    private static LohndatenEmpfaenger Kasse(string art, string nr, int filiale, string? mitglied, string? sub = null,
        DateOnly? ab = null, DateOnly? bis = null) => new()
    {
        Art = art, Kassennummer = nr, IsActive = true,
        Zuordnungen = { new CompanyProfileEmpfaenger { CompanyProfileId = filiale, Mitgliednummer = mitglied, Subnummer = sub, IsActive = true, GueltigAb = ab, GueltigBis = bis } },
    };

    private static List<string> Pruefe(XElement m, bool jeFiliale, params LohndatenEmpfaenger[] k)
        => ElmEmpfaengerPruefung.Pruefe(m, k, new HashSet<int> { 1 }, Von, Bis, jeFiliale);

    [Fact]
    public void Hinterlegter_Empfaenger_mit_Nummer_geht_durch()
        => Assert.Empty(Pruefe(Meldung(("#AHV", "046.000", Ahv("#AHV", "629.0714.00"))), true, Kasse("AUSGLEICHSKASSE", "046.000", 1, "629.0714.00")));

    [Fact]
    public void Nummer_einer_anderen_Filiale_wird_abgewiesen()
        => Assert.Contains(Pruefe(Meldung(("#AHV", "046.000", Ahv("#AHV", "629.0714.00"))), true, Kasse("AUSGLEICHSKASSE", "046.000", 2, "629.0714.00")),
            f => f.Contains("nicht zugeordnet"));

    [Fact]
    public void Kassennummer_als_Ersatz_fuer_die_Mitgliednummer_wird_abgewiesen()
        => Assert.Contains(Pruefe(Meldung(("#AHV", "046.000", Ahv("#AHV", "046.000"))), true, Kasse("AUSGLEICHSKASSE", "046.000", 1, "629.0714.00")),
            f => f.Contains("Mitglied"));

    [Fact]
    public void Empfaenger_ausserhalb_der_Gueltigkeit_wird_abgewiesen()
        => Assert.Contains(Pruefe(Meldung(("#AHV", "046.000", Ahv("#AHV", "1"))), true, Kasse("AUSGLEICHSKASSE", "046.000", 1, "1", bis: new DateOnly(2025, 12, 31))),
            f => f.Contains("nicht zugeordnet"));

    [Fact]
    public void Leerer_Empfaenger_wird_abgewiesen()
        => Assert.Contains(Pruefe(Meldung(("#AHV", "", Ahv("#AHV", "1"))), true), f => f.Contains("Kein Lohndaten-Empfänger"));

    [Fact]
    public void Lohnausweis_und_Statistik_brauchen_keine_Nummer()
        => Assert.Empty(Pruefe(Meldung(("#TAX-LU", "LU", new XElement("Tax", new XAttribute("addresseeIDRef", "#TAX-LU"))),
            ("#BFS", "Statistic", new XElement("Statistic", new XAttribute("addresseeIDRef", "#BFS")))), true));

    [Fact]
    public void Gemeinsame_Meldung_mit_zwei_Nummern_wird_abgewiesen()
    {
        var kasse = Kasse("AUSGLEICHSKASSE", "046.000", 1, "629.0714.00");
        kasse.Zuordnungen.Add(new CompanyProfileEmpfaenger { CompanyProfileId = 2, Mitgliednummer = "629.0535.00", IsActive = true });
        var m = Meldung(("#AHV", "046.000", Ahv("#AHV", "629.0714.00")));
        Assert.Contains(ElmEmpfaengerPruefung.Pruefe(m, new[] { kasse }, new HashSet<int> { 1, 2 }, Von, Bis, false), f => f.Contains("Mehrere Nummern"));
        Assert.Empty(ElmEmpfaengerPruefung.Pruefe(m, new[] { kasse }, new HashSet<int> { 1 }, Von, Bis, true));
    }

    [Fact]
    public void Fruehere_Kasse_ist_mit_dem_ganzen_Zeitraum_erlaubt()
    {
        var spida = Kasse("AUSGLEICHSKASSE", "079.000", 1, "7019.2", bis: new DateOnly(2024, 12, 31));
        var m = Meldung(("#AHV", "079.000", Ahv("#AHV", "7019.2")));
        Assert.Empty(ElmEmpfaengerPruefung.Pruefe(m, new[] { spida }, new HashSet<int> { 1 }, DateOnly.MinValue, new DateOnly(2025, 12, 31), false));
    }

    [Fact]
    public void Uvg_und_Uvgz_desselben_Versicherers_haben_eigene_Nummern()
    {
        var uvg = Kasse("UVG", "S1000", 1, "12577.2", "125");
        var uvgz = Kasse("UVGZ", "S1000", 1, "7651-873.1", "4566-4");
        uvg.Zuordnungen.Add(new CompanyProfileEmpfaenger { CompanyProfileId = 2, Mitgliednummer = "12577.2", Subnummer = "125", IsActive = true });
        uvgz.Zuordnungen.Add(new CompanyProfileEmpfaenger { CompanyProfileId = 2, Mitgliednummer = "7651-873.1", Subnummer = "4566-4", IsActive = true });
        var inst = new XElement("UVGZ-LAAC", new XAttribute("addresseeIDRef", "#z"), new XElement("CustomerIdentity", "7651-873.1"), new XElement("ContractIdentity", "4566-4"));
        Assert.Empty(ElmEmpfaengerPruefung.Pruefe(Meldung(("#z", "S1000", inst)), new[] { uvg, uvgz }, new HashSet<int> { 1, 2 }, Von, Bis, false));
    }

    private static PayrollPeriode P(int filiale, int monat, string status, int jahr = 2026)
        => new() { CompanyProfileId = filiale, Year = jahr, Month = monat, Status = status, Label = "" };

    [Fact]
    public void Offener_Lohnlauf_sperrt_nur_die_gewaehlte_Filiale_und_ihren_Monat()
    {
        var perioden = new[] { P(1, 1, "abgeschlossen"), P(1, 2, "offen"), P(2, 1, "offen") };
        Assert.Empty(ElmMeldeeinheit.OffeneLohnlaeufe(perioden, new[] { F(1, 1) }, "monthly", 2026, 1));
        Assert.Single(ElmMeldeeinheit.OffeneLohnlaeufe(perioden, new[] { F(1, 1) }, "monthly", 2026, 2));
        Assert.Single(ElmMeldeeinheit.OffeneLohnlaeufe(perioden, new[] { F(2, 1) }, "monthly", 2026, 1));
    }

    [Fact]
    public void Provisorisch_gilt_als_offen_und_Jahresmeldung_prueft_bis_zum_Monat()
    {
        var perioden = new[] { P(1, 3, "provisorisch_abgeschlossen"), P(1, 11, "offen") };
        Assert.Single(ElmMeldeeinheit.OffeneLohnlaeufe(perioden, new[] { F(1, 1) }, "annual", 2026, 6));
        Assert.Equal(2, ElmMeldeeinheit.OffeneLohnlaeufe(perioden, new[] { F(1, 1) }, "annual", 2026, null).Count);
        Assert.Equal(2, ElmMeldeeinheit.OffeneLohnlaeufe(perioden, new[] { F(1, 1) }, "grenz-it", 2026, null).Count);
    }

    [Theory]
    [InlineData("ema")]
    [InlineData("prospective")]
    public void Ema_und_voraussichtliche_Meldung_haengen_nicht_am_Lohnlauf(string art)
        => Assert.Empty(ElmMeldeeinheit.OffeneLohnlaeufe(new[] { P(1, 1, "offen") }, new[] { F(1, 1) }, art, 2026, 1));

    private static XElement AhvMit(string uvg, string bvg) => new("AHV-AVS", new XAttribute("addresseeIDRef", "#AHV"),
        new XElement("AK-CC-CustomerNumber", "1"),
        new XElement("UVG-LAA-Insurance", new XElement(uvg, "x")),
        new XElement("BVG-LPP-Insurance", new XElement(bvg, "x")));

    [Fact]
    public void Versicherer_zugeordnet_aber_ohne_Angaben_wird_abgewiesen()
    {
        var f = Pruefe(Meldung(("#AHV", "046.000", AhvMit("NoneWithReason", "Name"))), true,
            Kasse("AUSGLEICHSKASSE", "046.000", 1, "1"), Kasse("UVG", "S122", 1, "K", "V"));
        Assert.Contains(f, x => x.StartsWith("UVG:") && x.Contains("versichert seit"));
    }

    [Fact]
    public void Ohne_UVG_wird_abgewiesen_ohne_BVG_nicht()
    {
        var kasse = Kasse("AUSGLEICHSKASSE", "046.000", 1, "1");
        Assert.Contains(Pruefe(Meldung(("#AHV", "046.000", AhvMit("NoneWithReason", "Name"))), true, kasse), x => x.StartsWith("UVG:"));
        Assert.Empty(Pruefe(Meldung(("#AHV", "046.000", AhvMit("Name", "NoneWithReason"))), true, kasse));
    }

    private static XElement Bvg(string kunde, string vertrag) => new("BVG-LPP", new XAttribute("addresseeIDRef", "#BVG"),
        new XElement("CustomerIdentity", kunde), new XElement("ContractIdentity", vertrag));

    [Fact]
    public void Vertragsnummer_muss_hinterlegt_sein()
    {
        Assert.Empty(Pruefe(Meldung(("#BVG", "L1", Bvg("K", "V"))), true, Kasse("BVG", "L1", 1, "K", "V")));
        Assert.Contains(Pruefe(Meldung(("#BVG", "L1", Bvg("K", "L1"))), true, Kasse("BVG", "L1", 1, "K", "V")), f => f.Contains("Vertragsnummer"));
    }

    private static LohndatenEmpfaenger Qst(string kt, string? zentral, params (int Filiale, string Nr)[] z) => new()
    {
        Art = "QST", KantonCode = kt, Kassennummer = zentral, IsActive = true,
        Zuordnungen = z.Select(x => new CompanyProfileEmpfaenger { CompanyProfileId = x.Filiale, Mitgliednummer = x.Nr, IsActive = true }).ToList(),
    };

    [Fact]
    public void Ssl_Nummer_je_Filiale_kommt_von_der_Filiale()
    {
        var lu = new[] { Qst("LU", null, (1, "2246382"), (2, "1914715")) };
        Assert.Equal("1914715", ElmEmpfaengerPruefung.SslNummer(lu, "LU", new HashSet<int> { 2 }, Von, Bis, true));
        Assert.Equal("", ElmEmpfaengerPruefung.SslNummer(lu, "LU", new HashSet<int> { 3 }, Von, Bis, true));
    }

    [Fact]
    public void Ssl_Nummer_je_Filiale_nimmt_nie_die_zentrale_Nummer()
        => Assert.Equal("", ElmEmpfaengerPruefung.SslNummer(new[] { Qst("LU", "999") }, "LU", new HashSet<int> { 1 }, Von, Bis, true));

    [Fact]
    public void Ssl_Nummer_ueber_den_Hauptsitz_bleibt_die_zentrale_Nummer()
        => Assert.Equal("999", ElmEmpfaengerPruefung.SslNummer(new[] { Qst("LU", "999", (1, "111")) }, "LU", new HashSet<int> { 1 }, Von, Bis, false));

    [Fact]
    public void Quellensteuer_ohne_Nummer_wird_abgewiesen()
        => Assert.Contains(Pruefe(Meldung(("#QST-LU", "LU", new XElement("TaxAtSource", new XAttribute("addresseeIDRef", "#QST-LU"), new XElement("CustomerIdentity", "")))), true, Qst("LU", null)),
            f => f.Contains("SSL-Nummer"));
}
