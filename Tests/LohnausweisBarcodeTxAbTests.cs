using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec-TxAB-Barcode-Tests (Walter-Vorgabe 16.08.2026):
///
/// 1. V5/Mirus-Referenz — die aus dem echten Mirus-Barcode rückentwickelten
///    Invarianten (Header-Bytes, ZIP-Entry «txab», v5-Namespace) bleiben
///    unverändert erhalten. Bricht dieser Test, ist die Kompatibilität mit
///    den heute produktiven Steuerverwaltungs-Scannern gefährdet.
///
/// 2. ELM-6-Schema — das v6-XML validiert gegen das OFFIZIELLE
///    SalaryDeclarationTxAB.xsd (Ausgabe 06.03.2026, inkl. Include auf
///    ELMv6SalaryDeclaration_Tax_noNS.xsd).
///
/// 3. Pflichtfelder — fehlendes GrossIncome/NetIncome bricht die Erzeugung
///    mit Klartext-Meldung ab (kein stilles, unvollständiges XML).
/// </summary>
public class LohnausweisBarcodeTxAbTests
{
    private static LohnausweisData MusterDaten() => new()
    {
        CompanyUidFormatted = "CHE-262.373.037",
        CompanyName         = "Schaub Restaurants GmbH",
        BranchName          = "Zweigniederlassung Oftringen",
        CompanyStreet       = "Äussere Luzernerstrasse 13",
        CompanyZip          = "4665",
        CompanyCity         = "Oftringen",
        CompanyPhone        = "+41 62 797 76 76",
        HrVerantwortlicherName = "Walter Schaub",
        MaLastname   = "Muster",
        MaFirstname  = "Max",
        MaStreet     = "Teststrasse 1",
        MaZip        = "4665",
        MaCity       = "Oftringen",
        AhvNummer    = "756.1234.5678.97",
        PeriodeVon   = "01.01.2026",
        PeriodeBis   = "31.12.2026",
        BoxFFreierTransport = true,
        BoxGKantineGratis   = true,
        Ziffer1Lohn          = 52000.00m,
        Ziffer8BruttoTotal   = 54000.00m,
        Ziffer9AhvIvEoAlvNbu = 3456.75m,
        Ziffer101BvgOrdentlich = 1890.50m,
        Ziffer11Nettolohn    = 48652.75m,
        Ziffer12Quellensteuer = 2100.00m,
        Ziffer141Bemerkungen = "Krankengeldversicherung CHF 312.00",
    };

    // ── 1) V5 / Mirus-Referenz ──────────────────────────────────────────

    [Fact]
    public void V5_Header_EntsprichtMirusReferenzLayout()
    {
        var svc = new LohnausweisBarcodeService();
        var payload = svc.BuildPayload(MusterDaten());

        Assert.True(payload.Length > 14, "Payload muss Header + ZIP enthalten");
        // Byte 5: Kompressionstyp 'z' (Info-ZIP)
        Assert.Equal((byte)'z', payload[4]);
        // Bytes 6–8: Groesse big-endian inkl. der 14 Header-Bytes
        int size = (payload[5] << 16) | (payload[6] << 8) | payload[7];
        Assert.Equal(payload.Length, size);
        // Byte 9: Page Control = 1 / Byte 10: Steuerzeichen-Version = 3
        Assert.Equal(0x01, payload[8]);
        Assert.Equal(0x03, payload[9]);
        // Bytes 11–14: Barcode 01 von 01
        Assert.Equal(new byte[] { 0x00, 0x01, 0x00, 0x01 }, payload.Skip(10).Take(4).ToArray());
    }

    [Fact]
    public void V5_ZipEnthaeltGenauEinenEintragTxab_MitV5Namespace()
    {
        var svc = new LohnausweisBarcodeService();
        var payload = svc.BuildPayload(MusterDaten());

        using var ms = new MemoryStream(payload, 14, payload.Length - 14);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = Assert.Single(zip.Entries);
        Assert.Equal("txab", entry.Name);

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        var xml = reader.ReadToEnd();
        var doc = XDocument.Parse(xml);
        Assert.Equal("http://www.swissdec.ch/schema/sd/20200220/SalaryDeclarationTxAB",
                     doc.Root!.Name.NamespaceName);
        Assert.Equal("T", doc.Root.Name.LocalName);
    }

    [Fact]
    public void V5_FreeTransport_UndCanteen_InSchemaReihenfolge()
    {
        var xml = LohnausweisBarcodeService.BuildXml(MusterDaten());
        var doc = XDocument.Parse(xml);
        var s = doc.Root!.Elements().First(e => e.Name.LocalName == "S");
        var namen = s.Elements().Select(e => e.Name.LocalName).ToList();

        Assert.Contains("FreeTransport", namen);
        Assert.Contains("CanteenLunchCheck", namen);
        Assert.True(namen.IndexOf("FreeTransport") < namen.IndexOf("CanteenLunchCheck"),
            "FreeTransport muss VOR CanteenLunchCheck stehen (Schema-Sequenz)");
        // Kern-Reihenfolge gemaess TaxSalaryType-Sequenz
        var erwartet = new[] { "DocID", "CreationDate", "Period", "FreeTransport",
            "CanteenLunchCheck", "Income", "GrossIncome",
            "AHV-ALV-NBUV-AVS-AC-AANP-Contribution", "BVG-LPP-Contribution",
            "NetIncome", "DeductionAtSource", "Remark" };
        Assert.Equal(erwartet, namen);
    }

    [Fact]
    public void V5_OhneBoxF_KeinFreeTransportElement()
    {
        var d = MusterDaten();
        d.BoxFFreierTransport = false;
        var xml = LohnausweisBarcodeService.BuildXml(d);
        Assert.DoesNotContain("FreeTransport", xml);
    }

    // ── 2) ELM 6 — Validierung gegen das offizielle XSD ─────────────────

    private static XmlSchemaSet LadeElm6Schemas()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Swissdec");
        // .NET Core laedt xs:include (Chameleon-Include auf die Tax-XSD mit
        // den Typdefinitionen) nur mit explizitem Resolver.
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        set.Add("urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB",
                Path.Combine(dir, "SalaryDeclarationTxAB.xsd"));
        set.Compile();
        return set;
    }

    [Fact]
    public void V6_XmlValidiertGegenOffiziellesSwissdecXsd()
    {
        var xml = LohnausweisBarcodeService.BuildXml(
            MusterDaten(), LohnausweisBarcodeService.TxAbSchemaVersion.V6_20260306);

        var doc = XDocument.Parse(xml);
        Assert.Equal("urn:ch:swissdec:elm:v6:20260306:SalaryDeclarationTxAB",
                     doc.Root!.Name.NamespaceName);

        var fehler = new System.Collections.Generic.List<string>();
        doc.Validate(LadeElm6Schemas(), (_, e) => fehler.Add(e.Message));
        Assert.True(fehler.Count == 0,
            "ELM-6-XSD-Validierung fehlgeschlagen:\n" + string.Join("\n", fehler));
    }

    // ── OneCrew-Referenzfall Angela Skarcheska (Walter-Vorgabe 16.08.2026) ──
    // Realistischer Voll-Lohnausweis einer Oftringen-Crew-MA: sichtbare
    // Felder, Rundung auf ganze Franken, Bemerkung, Entwurf/Final-DocID.

    private static LohnausweisData AngelaSkarcheska() => new()
    {
        CompanyUidFormatted = "CHE-262.373.037",
        CompanyName         = "Schaub Restaurants GmbH",
        BranchName          = "Zweigniederlassung Oftringen",
        CompanyStreet       = "Äussere Luzernerstrasse 13",
        CompanyZip          = "4665",
        CompanyCity         = "Oftringen",
        CompanyPhone        = "+41 62 797 76 76",
        HrVerantwortlicherName = "Walter Schaub",
        MaLastname   = "Skarcheska",
        MaFirstname  = "Angela",
        MaStreet     = "Musterweg 5",
        MaZip        = "4665",
        MaCity       = "Oftringen",
        AhvNummer    = "756.5557.4464.71",
        Geburtsdatum = "03.10.1986",
        Jahr         = "2026",
        PeriodeVon   = "01.01.2026",
        PeriodeBis   = "31.12.2026",
        BoxFFreierTransport = false,      // McD: kein Werkbus
        BoxGKantineGratis   = true,       // Crew-Meal-Regelung Filiale
        Ziffer1Lohn          = 38452.35m, // mit Rappen → Rundungstest
        Ziffer8BruttoTotal   = 39104.60m,
        Ziffer9AhvIvEoAlvNbu = 2498.49m,
        Ziffer101BvgOrdentlich = 1042.50m,
        Ziffer11Nettolohn    = 35563.61m,
        Ziffer12Quellensteuer = 1876.20m,
        Ziffer141Bemerkungen = "Krankengeldversicherung CHF 268.80",
    };

    [Fact]
    public void Angela_Entwurf_TraegtEntwurfsDocId_UndGanzeFranken()
    {
        var xml = LohnausweisBarcodeService.BuildXml(AngelaSkarcheska());
        var doc = XDocument.Parse(xml);
        var s = doc.Root!.Elements().First(e => e.Name.LocalName == "S");
        string El(string n) => s.Elements().First(e => e.Name.LocalName == n).Value;

        Assert.Equal("Entwurf - Brouillon - Bozza", El("DocID"));
        // Rundung auf ganze Franken (kaufmaennisch), Schema-Format .00:
        Assert.Equal("38452.00", El("Income"));            // 38452.35 → 38452
        Assert.Equal("39105.00", El("GrossIncome"));       // 39104.60 → 39105
        Assert.Equal("2498.00",  El("AHV-ALV-NBUV-AVS-AC-AANP-Contribution")); // 2498.49 → 2498
        Assert.Equal("35564.00", El("NetIncome"));         // 35563.61 → 35564
        Assert.Equal("1876.00",  El("DeductionAtSource")); // 1876.20 → 1876
        Assert.Equal("Krankengeldversicherung CHF 268.80", El("Remark"));
        // Box F aus, Box G an:
        Assert.DoesNotContain("FreeTransport", xml);
        Assert.Contains("CanteenLunchCheck", xml);
        Assert.Equal("2026-01-01", s.Elements().First(e => e.Name.LocalName == "Period")
            .Elements().First().Value);
    }

    [Fact]
    public void Angela_Final_TraegtPersistierteDocIdUndCreationDate()
    {
        var d = AngelaSkarcheska();
        d.IstFinal          = true;
        d.DocId             = "0F8FAD5B-D9CB-469F-A165-70867728950E";
        d.CreationDateFinal = "2026-08-16T17:30:00";

        // Zwei «Ausdrucke» — beide muessen identische Identifikation tragen
        for (int i = 0; i < 2; i++)
        {
            var doc = XDocument.Parse(LohnausweisBarcodeService.BuildXml(d));
            var s = doc.Root!.Elements().First(e => e.Name.LocalName == "S");
            Assert.Equal("0F8FAD5B-D9CB-469F-A165-70867728950E",
                s.Elements().First(e => e.Name.LocalName == "DocID").Value);
            Assert.Equal("2026-08-16T17:30:00",
                s.Elements().First(e => e.Name.LocalName == "CreationDate").Value);
        }
    }

    [Fact]
    public void Angela_V6Final_ValidiertGegenOffiziellesXsd()
    {
        var d = AngelaSkarcheska();
        d.IstFinal          = true;
        d.DocId             = "0F8FAD5B-D9CB-469F-A165-70867728950E";
        d.CreationDateFinal = "2026-08-16T17:30:00";

        var xml = LohnausweisBarcodeService.BuildXml(
            d, LohnausweisBarcodeService.TxAbSchemaVersion.V6_20260306);
        var doc = XDocument.Parse(xml);
        var fehler = new System.Collections.Generic.List<string>();
        doc.Validate(LadeElm6Schemas(), (_, e) => fehler.Add(e.Message));
        Assert.True(fehler.Count == 0,
            "Angela-v6-XSD-Validierung fehlgeschlagen:\n" + string.Join("\n", fehler));
    }

    [Fact]
    public void Angela_Pflichtdaten_ListetFehlendeFelderAuf()
    {
        var d = AngelaSkarcheska();
        d.AhvNummer = null;
        d.Ziffer11Nettolohn = null;
        var fehlt = LohnausweisBarcodeService.PflichtdatenFehlen(d);
        Assert.Contains(fehlt, f => f.Contains("AHV-Nummer"));
        Assert.Contains(fehlt, f => f.Contains("Ziffer 11"));
        Assert.Equal(2, fehlt.Count);
    }

    // ── 3) Pflichtfeld-Validierung ──────────────────────────────────────

    [Fact]
    public void FehlendesGrossIncome_BrichtMitKlartextAb()
    {
        var d = MusterDaten();
        d.Ziffer8BruttoTotal = null;
        var ex = Assert.Throws<InvalidOperationException>(
            () => LohnausweisBarcodeService.BuildXml(d));
        Assert.Contains("Ziffer 8", ex.Message);
        Assert.Contains("GrossIncome", ex.Message);
    }

    [Fact]
    public void FehlendesNetIncome_BrichtMitKlartextAb()
    {
        var d = MusterDaten();
        d.Ziffer11Nettolohn = null;
        var ex = Assert.Throws<InvalidOperationException>(
            () => LohnausweisBarcodeService.BuildXml(d));
        Assert.Contains("Ziffer 11", ex.Message);
        Assert.Contains("NetIncome", ex.Message);
    }
}
