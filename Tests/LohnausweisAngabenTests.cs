using System.Linq;
using System.Xml.Linq;
using System.Xml.Schema;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class LohnausweisAngabenTests
{
    private static CompanyProfile Firma() => new()
    {
        LohnausweisBoxFFreierTransport = true,
        LohnausweisBoxGKantineGratis = false,
        LohnausweisZiffer14 = "Vergünstigung Versicherungsprämien",
        LohnausweisExpatDatum = new DateOnly(2021, 4, 15),
        LohnausweisExpatKanton = "LU",
        LohnausweisRz52 = true,
    };

    [Fact]
    public void Person_ohne_Eintrag_uebernimmt_die_Filiale()
    {
        var w = LohnausweisAngaben.Aufloesen(Firma(), null, 2026, 1);
        Assert.False(w.IstRente);
        Assert.True(w.BoxF);
        Assert.False(w.BoxG);
        Assert.Equal("Vergünstigung Versicherungsprämien", w.Ziffer14);
        Assert.Equal(new DateOnly(2021, 4, 15), w.Expat!.Value.Datum);
        Assert.Equal("LU", w.Expat.Value.Kanton);
        Assert.True(w.Rz52);
        Assert.Null(w.AnzahlLohnausweise);
        Assert.Contains("Expatriateruling durch Kanton LU am 15.04.2021 genehmigt", w.StandardBemerkungen);
        Assert.DoesNotContain(w.StandardBemerkungen, s => s.StartsWith("Einer von"));
    }

    [Fact]
    public void Person_ueberschreibt_F_und_unterdrueckt_Ziffer14()
    {
        var person = new EmployeeLohnausweis
        {
            Art = "RENTE",
            BoxF = false,
            Ziffer14Modus = "KEIN",
            ExpatModus = "KEIN",
            Rz52 = false,
        };
        var w = LohnausweisAngaben.Aufloesen(Firma(), person, 2026, null);
        Assert.True(w.IstRente);
        Assert.False(w.BoxF);
        Assert.Null(w.Ziffer14);
        Assert.Null(w.Expat);
        Assert.False(w.Rz52);
    }

    [Fact]
    public void Spesenreglement_verdraengt_das_Kreuz_der_Randziffer_52()
    {
        var person = new EmployeeLohnausweis
        {
            SpesenModus = "EIGEN",
            SpesenDatum = new DateOnly(2021, 4, 15),
            SpesenKanton = "LU",
            Rz52 = true,
        };
        var w = LohnausweisAngaben.Aufloesen(null, person, 2026, null);
        Assert.NotNull(w.Spesenreglement);
        Assert.False(w.Rz52);
        Assert.NotNull(w.SpesenHinweis);
        Assert.Contains("Spesenreglement durch Kanton LU am 15.04.2021 genehmigt", w.StandardBemerkungen);
    }

    [Fact]
    public void Mehrere_Lohnausweise_nur_ab_zwei()
    {
        Assert.Null(LohnausweisAngaben.EinerVon(1));
        Assert.Equal("Einer von zwei Lohnausweisen", LohnausweisAngaben.EinerVon(2));
        Assert.Equal("Einer von drei Lohnausweisen", LohnausweisAngaben.EinerVon(3));
        var w = LohnausweisAngaben.Aufloesen(null, null, 2026, 2);
        Assert.Contains("Einer von zwei Lohnausweisen", w.StandardBemerkungen);
    }

    [Fact]
    public void Umzug_Kinderzulage_Nachgenuss_und_Beteiligung_nach_Wegleitung()
    {
        var person = new EmployeeLohnausweis
        {
            BeteiligungModus = "EIGEN",
            BeteiligungGesperrt = true,
            BeteiligungAnwartschaft = true,
            KinderzulageAhv = true,
            UmzugBetrag = 4251.00m,
            UmzugJahr = 2026,
            Nachgenuss = true,
            NachgenussBetrag = 8000m,
            NachgenussJahr = 2026,
            NachgenussName = "Meier",
            NachgenussVorname = "Anna",
            NachgenussStrasse = "Bahnhofstrasse 1",
            NachgenussPlz = "6003",
            NachgenussOrt = "Luzern",
            WagenAbklaeren = true,
            WertModus = "EIGEN",
            WertDatum = new DateOnly(2010, 9, 15),
            WertKanton = "LU",
        };
        var w = LohnausweisAngaben.Aufloesen(null, person, 2026, null);
        Assert.Contains("Umzugskosten von CHF 4'251.00 bezahlt", w.StandardBemerkungen);
        Assert.Contains("Kinderzulagen im LA nicht enthalten. Auszahlung durch Ausgleichskasse", w.StandardBemerkungen);
        Assert.Contains(
            "Besoldungsnachgenuss an Meier, Anna, Bahnhofstrasse 1, 6003 Luzern",
            w.StandardBemerkungen);
        Assert.Contains(
            "Privatanteil Geschäftsfahrzeug im Veranlagungsverfahren abzuklären",
            w.StandardBemerkungen);
        Assert.Contains(
            "Mitarbeiterbeteiligung ohne steuerbares Einkommen. Grund: Gesperrte Mitarbeiteroptionen, Anwartschaften auf Mitarbeiteraktien",
            w.StandardBemerkungen);
        Assert.Contains("Verkehrswert durch Kanton LU am 15.09.2010 genehmigt", w.StandardBemerkungen);

        var anderesJahr = LohnausweisAngaben.Aufloesen(null, person, 2025, null);
        Assert.Null(anderesJahr.Umzugskosten);
        Assert.Null(anderesJahr.Nachgenuss);
    }

    [Fact]
    public void Barcode_traegt_die_Standardelemente_und_bleibt_schema_gueltig()
    {
        var person = new EmployeeLohnausweis
        {
            BoxF = true,
            Ziffer14Modus = "EIGEN",
            Ziffer14Text = "Stark verbilligte Abgabe eines Personenwagens",
            SpesenModus = "EIGEN",
            SpesenDatum = new DateOnly(2021, 4, 15),
            SpesenKanton = "LU",
            WagenMinimal = true,
            KinderzulageAhv = true,
            UmzugBetrag = 4251m,
            UmzugJahr = 2026,
            BeteiligungModus = "EIGEN",
            BeteiligungGesperrt = true,
            BeteiligungNichtKotiert = true,
            ExpatModus = "EIGEN",
            ExpatDatum = new DateOnly(2021, 4, 15),
            ExpatKanton = "LU",
            WertModus = "EIGEN",
            WertDatum = new DateOnly(2010, 9, 15),
            WertKanton = "LU",
            Nachgenuss = true,
            NachgenussBetrag = 1200m,
            NachgenussJahr = 2026,
            NachgenussName = "Meier",
            NachgenussVorname = "Anna",
            NachgenussStrasse = "Bahnhofstrasse 1",
            NachgenussPlz = "6003",
            NachgenussOrt = "Luzern",
        };
        var w = LohnausweisAngaben.Aufloesen(null, person, 2026, 2);
        var daten = Basis();
        daten.BoxFFreierTransport = w.BoxF;
        daten.Zusatz = LohnausweisAngaben.AlsZusatz(w, "80%-Stelle.");
        daten.Ziffer4Kapitalleistungen = w.Nachgenuss!.Betrag;

        var xml = LohnausweisBarcodeService.BuildXml(
            daten, LohnausweisBarcodeService.TxAbSchemaVersion.V6_20260306);
        var doc = XDocument.Parse(xml);
        var s = doc.Root!.Elements().Single(e => e.Name.LocalName == "S");
        var namen = s.Elements().Select(e => e.Name.LocalName).ToList();
        Assert.True(namen.IndexOf("CapitalPayment") < namen.IndexOf("GrossIncome"));
        Assert.True(namen.IndexOf("ChargesRule") < namen.IndexOf("OtherFringeBenefits"));
        Assert.True(namen.IndexOf("OtherFringeBenefits") < namen.IndexOf("StandardRemark"));
        Assert.True(namen.IndexOf("StandardRemark") < namen.IndexOf("Remark"));
        Assert.Contains("ExpatriateRuling", xml);
        Assert.Contains("NumberOfSalaryCertificate", xml);
        Assert.Equal("2", s.Descendants().Single(e => e.Name.LocalName == "NumberOfSalaryCertificate").Value);
        Assert.Contains("80%-Stelle.", xml);
        Assert.DoesNotContain("Einer von zwei", xml);

        var fehler = new System.Collections.Generic.List<string>();
        doc.Validate(LadeElm6Schemas(), (_, e) => fehler.Add(e.Message));
        Assert.True(fehler.Count == 0, string.Join("\n", fehler));
    }

    [Fact]
    public void Systemtest_05_01_bis_14_Druck_und_Barcode()
    {
        var rot = new System.Collections.Generic.List<string>();
        void Punkt(string id, bool gilt, string meldung)
        {
            if (!gilt) rot.Add(id + " — " + meldung);
        }

        var firma = new CompanyProfile
        {
            LohnausweisBoxFFreierTransport = true,
            LohnausweisBoxGKantineGratis = true,
            LohnausweisZiffer14 = "Vergünstigung Versicherungsprämien",
            LohnausweisExpatDatum = new DateOnly(2021, 4, 15),
            LohnausweisExpatKanton = "LU",
            LohnausweisWagenAbklaeren = true,
            LohnausweisWagenMinimal = true,
            LohnausweisRz52 = true,
            LohnausweisWertDatum = new DateOnly(2010, 9, 15),
            LohnausweisWertKanton = "LU",
            LohnausweisBetGesperrt = true,
            LohnausweisBetNichtKotiert = true,
            LohnausweisBetAnwartschaft = true,
            LohnausweisBetUnecht = true,
        };

        var vonFirma = LohnausweisAngaben.Aufloesen(firma, null, 2026, 1);
        Punkt("05_01", !vonFirma.IstRente, "ohne Personenangabe bleibt es ein Lohnausweis");
        Punkt("05_02", vonFirma.BoxF, "Ziffer F der Filiale fehlt auf der Person");
        Punkt("05_03", vonFirma.BoxG, "Ziffer G der Filiale fehlt auf der Person");
        Punkt("05_04", vonFirma.Ziffer14 == "Vergünstigung Versicherungsprämien",
            "Ziffer 14 der Filiale wird nicht übernommen");
        Punkt("05_05", vonFirma.Expat?.Kanton == "LU" && vonFirma.Expat?.Datum == new DateOnly(2021, 4, 15),
            "Expatriateruling der Filiale fehlt");
        Punkt("05_06", vonFirma.WagenAbklaeren && vonFirma.WagenMinimal,
            "die beiden Geschäftswagen-Angaben der Filiale fehlen");
        Punkt("05_08", vonFirma.Rz52, "Kreuz RZ 52 der Filiale fehlt");
        Punkt("05_09", vonFirma.AnzahlLohnausweise == null
            && !vonFirma.StandardBemerkungen.Any(s => s.Contains("Einer von")),
            "bei einem Lohnausweis darf «Einer von» nicht erscheinen");
        Punkt("05_10", vonFirma.Verkehrswert?.Kanton == "LU",
            "Verkehrswert der Filiale fehlt");
        Punkt("05_11", vonFirma.Gesperrt && vonFirma.NichtKotiert && vonFirma.Anwartschaft && vonFirma.Unecht,
            "die vier Gründe der Filiale fehlen");

        var rente = LohnausweisAngaben.Aufloesen(firma, new EmployeeLohnausweis { Art = "RENTE" }, 2026, 1);
        Punkt("05_01", rente.IstRente, "Rente an der Person wird nicht zur Rentenbescheinigung");

        var person = new EmployeeLohnausweis
        {
            Art = "LOHN",
            BoxF = true,
            BoxG = true,
            Ziffer14Modus = "EIGEN",
            Ziffer14Text = "Stark verbilligte Abgabe eines Personenwagens",
            ExpatModus = "EIGEN",
            ExpatDatum = new DateOnly(2021, 4, 15),
            ExpatKanton = "LU",
            WagenAbklaeren = true,
            WagenMinimal = true,
            SpesenModus = "EIGEN",
            SpesenDatum = new DateOnly(2021, 4, 15),
            SpesenKanton = "LU",
            Rz52 = true,
            WertModus = "EIGEN",
            WertDatum = new DateOnly(2010, 9, 15),
            WertKanton = "LU",
            BeteiligungModus = "EIGEN",
            BeteiligungGesperrt = true,
            BeteiligungNichtKotiert = true,
            BeteiligungAnwartschaft = true,
            BeteiligungUnecht = true,
            KinderzulageAhv = true,
            UmzugBetrag = 4251m,
            UmzugJahr = 2026,
            Nachgenuss = true,
            NachgenussBetrag = 1200m,
            NachgenussJahr = 2026,
            NachgenussName = "Meier",
            NachgenussVorname = "Anna",
            NachgenussStrasse = "Bahnhofstrasse 1",
            NachgenussPlz = "6003",
            NachgenussOrt = "Luzern",
        };
        var w = LohnausweisAngaben.Aufloesen(null, person, 2026, 2);
        LohnausweisAngaben.Zeilen(w, new[] { "80%-Stelle." }, out var z141, out _, out var z151, out var z152);
        var block15 = (z151 ?? "") + " " + (z152 ?? "");

        Punkt("05_04", z141 == "Stark verbilligte Abgabe eines Personenwagens",
            "Ziffer 14 steht nicht in der Bemerkungszeile 14");
        Punkt("05_05", block15.Contains("Expatriateruling durch Kanton LU am 15.04.2021 genehmigt"),
            "Expatriateruling wird nicht gedruckt");
        Punkt("05_06",
            block15.Contains("Privatanteil Geschäftsfahrzeug im Veranlagungsverfahren abzuklären")
            && block15.Contains("Privatanteil wird vom Arbeitnehmer bezahlt"),
            "eine der beiden Geschäftswagen-Bemerkungen fehlt");
        Punkt("05_07", block15.Contains("Spesenreglement durch Kanton LU am 15.04.2021 genehmigt") && !w.Rz52,
            "Spesenreglement fehlt oder das Kreuz RZ 52 bleibt trotzdem stehen");
        Punkt("05_08", !w.Rz52, "bei genehmigtem Spesenreglement darf Ziffer 13.1.1 nicht angekreuzt sein");
        Punkt("05_09", block15.Contains("Einer von zwei Lohnausweisen"),
            "«Einer von zwei Lohnausweisen» fehlt");
        Punkt("05_10", block15.Contains("Verkehrswert durch Kanton LU am 15.09.2010 genehmigt"),
            "Verkehrswert wird nicht gedruckt");
        Punkt("05_11", block15.Contains("Gesperrte Mitarbeiteroptionen")
            && block15.Contains("Nicht börsenkotierte Mitarbeiteroptionen")
            && block15.Contains("Anwartschaften auf Mitarbeiteraktien")
            && block15.Contains("Unechte Mitarbeiterbeteiligungen"),
            "ein Grund der Mitarbeiterbeteiligung fehlt im Druck");
        var nachgenuss = w.Nachgenuss;
        Punkt("05_12", nachgenuss != null
            && block15.Contains("Besoldungsnachgenuss an Meier, Anna, Bahnhofstrasse 1, 6003 Luzern")
            && nachgenuss.Betrag == 1200m,
            "Besoldungsnachgenuss oder der Betrag für Ziffer 4 fehlt");
        Punkt("05_13", block15.Contains("Kinderzulagen im LA nicht enthalten. Auszahlung durch Ausgleichskasse"),
            "Kinderzulagen-Bemerkung fehlt");
        Punkt("05_14", block15.Contains("Umzugskosten von CHF 4'251.00 bezahlt"),
            "Umzugskosten fehlen");

        var daten = Basis();
        daten.BoxFFreierTransport = w.BoxF;
        daten.BoxGKantineGratis = w.BoxG;
        daten.Ziffer4Kapitalleistungen = nachgenuss?.Betrag;
        daten.Ziffer4Art = "Besoldungsnachgenuss";
        daten.Ziffer1311EffektivOhneBeleg = w.Rz52;
        daten.Ziffer141Bemerkungen = z141;
        daten.Ziffer151Bemerkungen = z151;
        daten.Ziffer152Bemerkungen = z152;
        daten.Zusatz = LohnausweisAngaben.AlsZusatz(w, "80%-Stelle.");
        var xml = LohnausweisBarcodeService.BuildXml(
            daten, LohnausweisBarcodeService.TxAbSchemaVersion.V6_20260306);
        var doc = XDocument.Parse(xml);
        var s = doc.Root!.Elements().Single(e => e.Name.LocalName == "S");
        bool Hat(string name) => s.Descendants().Any(e => e.Name.LocalName == name);

        Punkt("05_02", Hat("FreeTransport"), "Barcode ohne FreeTransport");
        Punkt("05_03", Hat("CanteenLunchCheck"), "Barcode ohne CanteenLunchCheck");
        Punkt("05_04", s.Elements().Any(e => e.Name.LocalName == "OtherFringeBenefits"
            && e.Value.Contains("Personenwagens")), "Barcode ohne Ziffer 14");
        Punkt("05_05", Hat("ExpatriateRuling"), "Barcode ohne ExpatriateRuling");
        Punkt("05_06", Hat("CompanyCarClarify") && Hat("MinimalEmployeeCarPartPercentage"),
            "Barcode ohne eine der beiden Geschäftswagen-Angaben");
        Punkt("05_07", Hat("WithRegulation") && !Hat("Guidance"),
            "Barcode ohne Spesenreglement oder mit Kreuz RZ 52 daneben");
        Punkt("05_09", s.Descendants().Single(e => e.Name.LocalName == "NumberOfSalaryCertificate").Value == "2"
            && !xml.Contains("Einer von zwei"),
            "Gesamtzahl fehlt im Barcode oder der Satz steht doppelt im XML");
        Punkt("05_10", Hat("StaffShareMarketValue"), "Barcode ohne Verkehrswert");
        Punkt("05_11", Hat("BlockedOptions") && Hat("UnquotedOptions")
            && Hat("DeferredBenefitsStaffShares") && Hat("FictitiousStaffShare"),
            "ein Grund fehlt im Barcode");
        Punkt("05_12", s.Elements().Any(e => e.Name.LocalName == "CapitalPayment")
            && Hat("ContinuedProvisionOfSalary")
            && xml.Contains("1200.00"),
            "Besoldungsnachgenuss fehlt in Ziffer 4 oder in der Bemerkung des Barcodes");
        Punkt("05_13", Hat("ChildAllowancePerAHV-AVS"), "Barcode ohne Kinderzulage");
        Punkt("05_14", Hat("RelocationCosts") && xml.Contains("4251.00"),
            "Barcode ohne Umzugskosten");
        Punkt("05_01", doc.Root.Elements().All(e => e.Name.LocalName != "A"),
            "ein Lohnausweis darf nicht als Rentenbescheinigung (Element A) herausgehen");

        var fehler = new System.Collections.Generic.List<string>();
        doc.Validate(LadeElm6Schemas(), (_, e) => fehler.Add(e.Message));
        Punkt("05_01", fehler.Count == 0, "Barcode-XML ungültig: " + string.Join(" | ", fehler));

        var nurRz = LohnausweisAngaben.Aufloesen(null, new EmployeeLohnausweis { Rz52 = true }, 2026, 1);
        var rzDaten = Basis();
        rzDaten.Ziffer1311EffektivOhneBeleg = nurRz.Rz52;
        rzDaten.Zusatz = LohnausweisAngaben.AlsZusatz(nurRz, null);
        var rzXml = LohnausweisBarcodeService.BuildXml(
            rzDaten, LohnausweisBarcodeService.TxAbSchemaVersion.V6_20260306);
        Punkt("05_08", nurRz.Rz52 && rzXml.Contains("Guidance") && !rzXml.Contains("WithRegulation"),
            "ohne Spesenreglement fehlt das Kreuz RZ 52 im Barcode");

        var renteDaten = Basis();
        renteDaten.IstLohnausweis = false;
        renteDaten.Zusatz = LohnausweisAngaben.AlsZusatz(rente, null);
        var renteXml = XDocument.Parse(LohnausweisBarcodeService.BuildXml(
            renteDaten, LohnausweisBarcodeService.TxAbSchemaVersion.V6_20260306));
        Punkt("05_01", renteXml.Root!.Elements().Any(e => e.Name.LocalName == "A")
            && renteXml.Root.Elements().All(e => e.Name.LocalName != "S"),
            "Rentenbescheinigung kommt nicht als Element A");

        Assert.True(rot.Count == 0, string.Join("\n", rot));
    }

    [Fact]
    public void Rentenbescheinigung_verwendet_Element_A()
    {
        var w = LohnausweisAngaben.Aufloesen(null, new EmployeeLohnausweis { Art = "RENTE" }, 2026, null);
        var daten = Basis();
        daten.IstLohnausweis = false;
        daten.Zusatz = LohnausweisAngaben.AlsZusatz(w, null);
        var xml = LohnausweisBarcodeService.BuildXml(
            daten, LohnausweisBarcodeService.TxAbSchemaVersion.V6_20260306);
        var doc = XDocument.Parse(xml);
        Assert.Contains(doc.Root!.Elements(), e => e.Name.LocalName == "A");
        Assert.DoesNotContain(doc.Root.Elements(), e => e.Name.LocalName == "S");
        var fehler = new System.Collections.Generic.List<string>();
        doc.Validate(LadeElm6Schemas(), (_, e) => fehler.Add(e.Message));
        Assert.True(fehler.Count == 0, string.Join("\n", fehler));
    }

    private static LohnausweisData Basis() => new()
    {
        CompanyUidFormatted = "CHE-262.373.037",
        CompanyName = "Schaub Restaurants GmbH",
        CompanyStreet = "Äussere Luzernerstrasse 13",
        CompanyZip = "4665",
        CompanyCity = "Oftringen",
        CompanyPhone = "+41 62 797 76 76",
        HrVerantwortlicherName = "Walter Schaub",
        MaLastname = "Muster",
        MaFirstname = "Max",
        MaStreet = "Teststrasse 1",
        MaZip = "4665",
        MaCity = "Oftringen",
        AhvNummer = "756.1234.5678.97",
        PeriodeVon = "01.01.2026",
        PeriodeBis = "31.12.2026",
        Ziffer1Lohn = 52000m,
        Ziffer8BruttoTotal = 54000m,
        Ziffer9AhvIvEoAlvNbu = 3456.75m,
        Ziffer101BvgOrdentlich = 1890.50m,
        Ziffer11Nettolohn = 48652.75m,
        Ziffer12Quellensteuer = 2100m,
    };

    private static System.Xml.Schema.XmlSchemaSet LadeElm6Schemas()
    {
        var dir = System.IO.Path.Combine(System.AppContext.BaseDirectory, "TestData", "Swissdec");
        var set = new System.Xml.Schema.XmlSchemaSet { XmlResolver = new System.Xml.XmlUrlResolver() };
        set.Add("urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB",
            System.IO.Path.Combine(dir, "SalaryDeclarationTxAB.xsd"));
        set.Compile();
        return set;
    }
}
