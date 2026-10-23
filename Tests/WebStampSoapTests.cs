using System.Xml.Linq;
using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services.WebStamp;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Briefpost über WebStamp (Walter 24.09.2026): Aufbau der SOAP-Nachrichten
/// und Auswertung der Antworten. Die Antwort-Beispiele entsprechen dem, was die
/// stabile Testumgebung (wsredesignint2) am 24.09.2026 geliefert hat.
/// </summary>
public class WebStampSoapTests
{
    private static readonly WebStampSoap.Zugang Z = new("APP123", "K12345", "geheim");

    [Fact]
    public void Ping_ist_leeres_Methodenelement_ohne_args()
    {
        var doc = XDocument.Parse(WebStampSoap.Ping());
        var body = doc.Root!.Element(WebStampSoap.Env + "Body")!;
        var ping = Assert.Single(body.Elements());
        Assert.Equal(WebStampSoap.Ns + "ping", ping.Name);
        Assert.Empty(ping.Elements());
    }

    [Fact]
    public void Kinder_unter_der_Methode_sind_unqualifiziert()
    {
        var doc = XDocument.Parse(WebStampSoap.KundenDaten(Z));
        var methode = doc.Descendants(WebStampSoap.Ns + "get_customer_data").Single();
        var args = methode.Element("args");
        Assert.NotNull(args);
        var id = args!.Element("identification")!;
        Assert.Equal("APP123", id.Element("application")!.Value);
        Assert.Equal("de", id.Element("language")!.Value);
        Assert.Equal("K12345", id.Element("userid")!.Value);
        Assert.Equal("geheim", id.Element("password")!.Value);
    }

    [Fact]
    public void Ohne_Login_fehlen_userid_und_password()
    {
        var id = WebStampSoap.Identifikation(new WebStampSoap.Zugang("APP", null, null));
        Assert.Null(id.Element("userid"));
        Assert.Null(id.Element("password"));
    }

    private static readonly WebStampAnschrift Empfaenger = new(null, null, "Frau", "Anna", "Muster", "Hauptstrasse 1", "6000", "Luzern", "CH");
    private static readonly WebStampAnschrift Absender = new("Schaub Restaurants GmbH", null, null, null, null, "Brauereiweg 7", "5600", "Lenzburg", "CH");

    [Fact]
    public void Frankatur_ist_Selbstdruck_ohne_Dokument_und_kuerzt_Referenz()
    {
        var xml = WebStampSoap.Frankatur("new_order_preview", Z,
            new WebStampSoap.FrankaturAuftrag(4711, 128, Empfaenger, Absender, new string('R', 80), new string('N', 80)));
        var args = XDocument.Parse(xml).Descendants(WebStampSoap.Ns + "new_order_preview").Single().Element("args")!;
        Assert.Equal("4711", args.Element("product")!.Value);
        Assert.Equal("false", args.Element("printservice")!.Value);
        Assert.Equal("pdf", args.Element("file_type")!.Value);
        Assert.Equal("true", args.Element("single")!.Value);
        Assert.Equal("128", args.Element("media")!.Value);
        Assert.Null(args.Element("document"));
        Assert.Equal("Anna", args.Element("a_addresses")!.Element("item")!.Element("firstname")!.Value);
        Assert.Equal("6000", args.Element("a_addresses")!.Element("item")!.Element("zip")!.Value);
        Assert.Equal("Lenzburg", args.Element("sender")!.Element("city")!.Value);
        Assert.Equal(WebStampSoap.MaxReferenz, args.Element("reference")!.Value.Length);
        Assert.Equal(WebStampSoap.MaxNotiz, args.Element("order_comment")!.Value.Length);
    }

    [Fact]
    public void Frankatur_nimmt_nur_die_beiden_Bestellmethoden()
    {
        Assert.Throws<ArgumentException>(() =>
            WebStampSoap.Frankatur("copy_order_by_id", Z, new WebStampSoap.FrankaturAuftrag(1, 1, Empfaenger, Absender)));
    }

    [Fact]
    public void Freie_Adresse_liest_Name_Strasse_und_Plz_Ort()
    {
        var a = WebStampAnschrift.AusZeilen(new[] { "Frau", "Anna Muster", "Hauptstrasse 1", "6000 Luzern" });
        Assert.NotNull(a);
        Assert.Equal("Frau", a!.Titel);
        Assert.Equal("Anna", a.Vorname);
        Assert.Equal("Muster", a.Nachname);
        Assert.Equal("Hauptstrasse 1", a.Strasse);
        Assert.Equal("6000", a.Plz);
        Assert.Equal("Luzern", a.Ort);
    }

    [Fact]
    public void Fenster_links_bevorzugt_die_Vorlage_mit_C4()
    {
        var medien = new[]
        {
            new WebStampSoap.Medium(150, "C5, C5/6 - Fenster links", 3),
            new WebStampSoap.Medium(128, "C4, C5, C5/6 - Fenster links", 3),
            new WebStampSoap.Medium(129, "C4, C5, C5/6 - Fenster rechts", 3),
        };
        Assert.Equal(128, WebStampSoap.FensterMedium(medien, rechts: false));
        Assert.Equal(129, WebStampSoap.FensterMedium(medien, rechts: true));
    }

    [Fact]
    public void Fault_der_Testumgebung_wird_gelesen()
    {
        const string antwort = """
            <?xml version="1.0" encoding="UTF-8"?>
            <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/" xmlns:wsws="https://wsredesignint2.post.ch/wsws/soap/v6"><SOAP-ENV:Body><SOAP-ENV:Fault><faultcode>Client</faultcode><faultstring>WS-Kunden-ID, Passwort oder Applikations-ID sind ungültig.</faultstring><detail><wsws:code>2202</wsws:code><wsws:request_id>arU0s0QNsny5CHF2JVBpiAAAAAg</wsws:request_id></detail></SOAP-ENV:Fault></SOAP-ENV:Body></SOAP-ENV:Envelope>
            """;
        var f = WebStampSoap.LiesFault(XDocument.Parse(antwort.Trim()));
        Assert.NotNull(f);
        Assert.Equal("2202", f!.FehlerNummer);
        Assert.Equal("Client", f.Code);
        Assert.StartsWith("WS-Kunden-ID", f.Text);
        Assert.Equal("arU0s0QNsny5CHF2JVBpiAAAAAg", f.RequestId);
    }

    [Fact]
    public void Ping_Antwort_liefert_Resultat()
    {
        const string antwort = """
            <?xml version="1.0" encoding="UTF-8"?>
            <SOAP-ENV:Envelope xmlns:SOAP-ENV="http://schemas.xmlsoap.org/soap/envelope/" xmlns:ns1="https://webstamp.post.ch/wsws/soap/v6"><SOAP-ENV:Body><ns1:pingResponse><pingResult><date>2026-09-24T16:33:23+02:00</date><request_id>abc</request_id></pingResult></ns1:pingResponse></SOAP-ENV:Body></SOAP-ENV:Envelope>
            """;
        var doc = XDocument.Parse(antwort.Trim());
        Assert.Null(WebStampSoap.LiesFault(doc));
        var r = WebStampSoap.LiesResultat(doc, "ping");
        Assert.Equal("pingResult", r!.Name.LocalName);
        Assert.Equal("abc", r.Element("request_id")!.Value);
    }

    [Fact]
    public void Auftrag_mit_Sendungen_Preisen_und_Mitteilung_wird_gelesen()
    {
        var r = XElement.Parse("""
            <new_order_previewResult>
              <messages><item><message_type>confirm</message_type><customer_message>Neue AGB</customer_message><system_message/><system_message_code>1</system_message_code><confirm_until>2026-10-31T00:00:00+01:00</confirm_until><url>https://webstamp.post.ch/agb</url></item></messages>
              <order_id>98765</order_id>
              <price>1.8</price>
              <item_price>1.2</item_price>
              <price_details>
                <item><type>webstamp</type><quantity>1</quantity><amount>1.2</amount><description>A-Post</description></item>
                <item><type>charge</type><quantity>2</quantity><amount>0.6</amount><description>Druckservice</description></item>
              </price_details>
              <print_data>JVBERi0=</print_data>
              <reference>OC-7-20260924</reference>
              <stamps><item><tracking_number/><stamp_id>55</stamp_id><label_number>1</label_number></item></stamps>
              <consignments>
                <item><number>1</number><pages><item><number>1</number></item><item><number>2</number></item></pages><window>left</window><state>valid</state></item>
                <item><number>2</number><pages><item><number>3</number></item></pages><window>right</window><state>invalid</state><reason>Keine Adresse im Sichtfenster</reason></item>
              </consignments>
              <product_number>4711</product_number>
              <post_product_number>1</post_product_number>
              <valid_days>365</valid_days>
            </new_order_previewResult>
            """);
        var a = WebStampSoap.LiesAuftrag(r);
        Assert.Equal(98765, a.OrderId);
        Assert.Equal(1.80m, a.Preis);
        Assert.Equal(1.20m, a.EinzelPreis);
        Assert.Equal("OC-7-20260924", a.Referenz);
        Assert.Equal(4711, a.ProduktNummer);
        Assert.Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }, a.Druckdaten![..4]);
        Assert.Equal(2, a.Preise.Count);
        Assert.Equal("charge", a.Preise[1].Typ);
        Assert.Equal(2, a.Sendungen.Count);
        Assert.Equal(new List<int> { 1, 2 }, a.Sendungen[0].Seiten);
        Assert.Equal("invalid", a.Sendungen[1].Status);
        Assert.Equal("Keine Adresse im Sichtfenster", a.Sendungen[1].Grund);
        var m = Assert.Single(a.Nachrichten);
        Assert.Equal("confirm", m.Typ);
        Assert.NotNull(m.BestaetigenBis);
        Assert.Equal(55, Assert.Single(a.Frankiervermerke).StampId);
    }

    [Fact]
    public void Produkte_und_Kundendaten_werden_gelesen()
    {
        var p = WebStampSoap.LiesProdukte(XElement.Parse("""
            <get_productsResult><item><number>12</number><category>Brief Inland</category><category_number>1</category_number><product_list>GK</product_list><product_list_number>2</product_list_number><post_product_number>101</post_product_number><price>1.2</price><name>A-Post Standardbrief</name><additions/><delivery>A-Post</delivery><format>Standardbrief</format><size_din>B5</size_din><max_weight>100</max_weight><zone>3</zone><barcode>false</barcode></item></get_productsResult>
            """));
        var prod = Assert.Single(p);
        Assert.Equal(12, prod.Nummer);
        Assert.Equal(1.20m, prod.Preis);
        Assert.Equal(3, prod.Zone);
        Assert.False(prod.Barcode);

        var k = WebStampSoap.LiesKunde(XElement.Parse(
            "<r><license_state>none</license_state><payment_type>invoice</payment_type><product_lists><item>2</item><item>5</item></product_lists></r>"));
        Assert.Equal("invoice", k.Zahlungsart);
        Assert.Equal(new List<int> { 2, 5 }, k.Sortimente);
    }

    [Fact]
    public void Bestellung_nur_in_der_Testumgebung()
    {
        Assert.True(WebStampEndpunkte.BestellungErlaubt("test"));
        Assert.True(WebStampEndpunkte.BestellungErlaubt(null));
        Assert.False(WebStampEndpunkte.BestellungErlaubt("prod"));
        Assert.Equal(WebStampEndpunkte.TestUrl, WebStampEndpunkte.Url("irgendwas"));
    }

    [Fact]
    public void Adresszeilen_Schweiz_ohne_Land_Ausland_mit_Land()
    {
        var ch = new Employee { Salutation = "Frau", FirstName = "Anna", LastName = "Muster",
                                Street = "Hauptstrasse 1", ZipCode = "6000", City = "Luzern", Country = "CH" };
        Assert.Equal(new List<string> { "Frau", "Anna Muster", "Hauptstrasse 1", "6000 Luzern" },
                     WebStampController.AdressZeilen(ch));
        Assert.True(WebStampController.AdresseVollstaendig(ch));

        var de = new Employee { FirstName = "Max", LastName = "Meier", Street = "Weg 2",
                                ZipCode = "79539", City = "Lörrach", Country = "Deutschland" };
        Assert.Equal("DEUTSCHLAND", WebStampController.AdressZeilen(de).Last());

        Assert.False(WebStampController.AdresseVollstaendig(new Employee { FirstName = "X", LastName = "Y" }));
    }

    [Fact]
    public void Brief_PDF_entsteht()
    {
        var pdf = new WebStampBriefPdfService().Generate(new WebStampBriefPdfService.BriefDaten(
            new[] { "Schaub Restaurants GmbH", "Filiale Test" },
            new[] { "Frau", "Anna Muster", "Hauptstrasse 1", "6000 Luzern" },
            "Luzern", new DateOnly(2026, 9, 24), "Testbrief", "Absatz eins.\n\nAbsatz zwei.", "Walter Schaub", false));
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public void Begleitbrief_Arbeitszeugnis_auf_Frankaturseite()
    {
        var wurzel = VerzeichnisMitProjekt();
        var frankatur = Path.Combine(wurzel, ".tmp", "webstamp-testseite.pdf");
        var quelle = File.Exists(frankatur) ? File.ReadAllBytes(frankatur) : LeereA4();
        var pdf = new WebStampBriefPdfService().SchreibeAufFrankatur(quelle, BegleitbriefArbeitszeugnis());
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        using var doc = new PdfDocument(new PdfReader(new MemoryStream(pdf)));
        var text = PdfTextExtractor.GetTextFromPage(doc.GetFirstPage());
        Assert.Contains("Arbeitszeugnis", text);
        Assert.Contains("Lenzburg, 23.10.2026", text);
        Assert.Contains("Freundliche Grüsse", text);
        Assert.Contains("Walter Schaub", text);

        if (File.Exists(frankatur))
            File.WriteAllBytes(Path.Combine(wurzel, ".tmp", "webstamp-arbeitszeugnis.pdf"), pdf);
    }

    private static WebStampBriefPdfService.BriefDaten BegleitbriefArbeitszeugnis() => new(
        new[] { "Schaub Restaurants GmbH", "Filiale Lenzburg", "Brauereiweg 7", "5600 Lenzburg" },
        new[] { "Frau", "Anna Muster", "Hauptstrasse 1", "6000 Luzern" },
        "Lenzburg", new DateOnly(2026, 10, 23),
        "Arbeitszeugnis",
        "Guten Tag Frau Muster\n\n" +
        "Anbei erhalten Sie Ihr Arbeitszeugnis für Ihre Tätigkeit in der Filiale Lenzburg.\n\n" +
        "Wir danken Ihnen für Ihren Einsatz bei uns und wünschen Ihnen für die Zukunft alles Gute.",
        "Walter Schaub", false);

    private static string VerzeichnisMitProjekt()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "hr-system.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }

    private static byte[] LeereA4()
    {
        using var ms = new MemoryStream();
        using (var pdf = new PdfDocument(new PdfWriter(ms)))
            pdf.AddNewPage(iText.Kernel.Geom.PageSize.A4);
        return ms.ToArray();
    }
}
