using System.Xml.Linq;
using HrSystem.Services.Elm;
using Microsoft.Extensions.Configuration;
using Xunit;
using static HrSystem.Services.Elm.ElmGemeinsam;
using static HrSystem.Services.Elm.ElmUebermittlungXml;

namespace HrSystem.Tests;

/// <summary>
/// Foundation F05 / F06 / F08 (30.09.2026): jede Anfrage, die wir bauen, gegen die
/// ELM-6.0-Schemas halten und die Antworten an den Musterdateien der
/// Transmitter-Richtlinien (docs/swissdec/Transmitter_Richtlinien/samples) lesen.
/// </summary>
public class ElmUebermittlungTests
{
    private static readonly ElmXmlValidator Validator = new();

    private static readonly ElmEinstellungen Einstellungen = new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Swissdec:MonitoringId"] = "onecrew-test" })
        .Build());

    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.sln")))
                dir = Directory.GetParent(dir)?.FullName;
            return dir ?? throw new InvalidOperationException("Repo-Wurzel nicht gefunden.");
        }
    }

    private static XDocument Muster(string datei)
        => XDocument.Load(Path.Combine(RepoRoot, "docs", "swissdec", "Transmitter_Richtlinien", "samples", datei));

    private static XElement Kontext() => RequestContext("Muster AG", new DateTime(2026, 9, 30, 22, 0, 0), Einstellungen);

    private static void IstGueltig(XElement body)
    {
        var fehler = Validator.Validate(body.ToString());
        Assert.True(fehler.Count == 0, string.Join("\n", fehler));
    }

    private static ElmVorgang Vorgang(string art, bool testCase = true) => new()
    {
        Art = art, TestCase = testCase, Uid = "CHE-999.999.996", Firmenname = "Muster AG", JobKey = "JobKey1234",
    };

    private static ElmVorgangAdressat Adressat(string domain) => new()
    {
        AddresseeId = "#a", Identification = domain == "TaxAtSource" ? "BE" : "1234", Domain = domain,
        Zustand = "Success", FallId = "DEC-1", Key = "Key", Password = "Password",
    };

    private const string Dialog = """
        <sdc:DialogMessage xmlns:sdc="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:container"
                           xmlns:ep="urn:ch:swissdec:basis:v1:20260306:components">
          <ep:Creation>2026-09-30T10:00:00.000+02:00</ep:Creation>
          <ep:StoryID>DM-1</ep:StoryID>
          <ep:StandardDialogID>notStandard</ep:StandardDialogID>
          <ep:Title>Rückfrage</ep:Title>
          <ep:Description>Bitte ergänzen</ep:Description>
          <ep:Paragraph sectionIDRef="#s1">
            <ep:ID>1</ep:ID>
            <ep:Label>Hinweis</ep:Label>
            <ep:Value><ep:String>Nur Info</ep:String></ep:Value>
          </ep:Paragraph>
          <ep:Paragraph sectionIDRef="#s1">
            <ep:ID>2</ep:ID>
            <ep:Label>Betrag</ep:Label>
            <ep:Answer><ep:Amount/></ep:Answer>
          </ep:Paragraph>
          <ep:Paragraph>
            <ep:ID>3</ep:ID>
            <ep:Label>Bemerkung</ep:Label>
            <ep:Answer optional="true"><ep:String/></ep:Answer>
          </ep:Paragraph>
          <ep:Paragraph>
            <ep:ID>4</ep:ID>
            <ep:Label>Bestätigt</ep:Label>
            <ep:Answer><ep:YesNoUnknown><ep:Default>unknown</ep:Default></ep:YesNoUnknown></ep:Answer>
          </ep:Paragraph>
          <ep:Section sectionID="#s1">
            <ep:Heading>Lohn</ep:Heading>
          </ep:Section>
        </sdc:DialogMessage>
        """;

    // ── Anfragen: Schema ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("monthly")]
    [InlineData("annual")]
    public void GetStatus_IstSchemaGueltig(string art)
    {
        var body = BaueGetStatus(art, "JobKey1234", Kontext());
        IstGueltig(body);
        Assert.Equal(art == "annual" ? "GetStatusFromDeclareAnnualSalary" : "GetStatusFromDeclareMonthlySalary",
            body.Name.LocalName);
        Assert.DoesNotContain(body.Descendants(), e => e.Name.LocalName == "TestCase");
    }

    [Theory]
    [InlineData("monthly", "TaxAtSource")]
    [InlineData("monthly", "Statistic")]
    [InlineData("annual", "AHV-AVS")]
    [InlineData("annual", "UVG-LAA")]
    public void SynchronizeDeclare_Minimal_IstSchemaGueltig(string art, string domain)
        => IstGueltig(BaueSynchronizeDeclare(Vorgang(art, testCase: false), Adressat(domain), Array.Empty<string>(), Kontext()));

    [Fact]
    public void SynchronizeDeclare_Voll_IstSchemaGueltig_UndInReihenfolge()
    {
        var a = Adressat("TaxAtSource");
        a.State = "DialogMessagePending";
        a.UnterdrueckteInstitutionStories.Add("SI-1");
        a.UnterdrueckteSenderStories.Add("SS-1");
        var fehler = new List<string>();
        var antwort = ElmDialog.BaueAntwort(XElement.Parse(Dialog),
            new Dictionary<short, string?> { [2] = "1'234,5" }, fehler, new DateTime(2026, 9, 30, 22, 0, 0), "DM-A");
        Assert.Empty(fehler);
        a.Ausstehend.Add(new ElmStoryGesendet { StoryId = "DM-A", Xml = antwort.ToString() });

        var body = BaueSynchronizeDeclare(Vorgang("monthly"), a, new[] { "Q-1", "DM-1" }, Kontext());
        IstGueltig(body);

        var ctx = body.Descendants(Sdc + "CaseContext").Single();
        Assert.Equal(new[] { "ReceivedStoryIDs", "SuppressedSenderStoryIDs", "SuppressedInstitutionStoryIDs",
                             "Credentials", "DeclarationID", "TestCase" },
            ctx.Elements().Select(e => e.Name.LocalName));
        var fall = body.Element(Sdc + "Case")!;
        Assert.Equal("DialogMessagePending", fall.Element(Sdc + "ReceivedState")!.Value);
        Assert.Single(fall.Elements(Sdc + "DialogMessage"));
    }

    [Theory]
    [InlineData("UVG-LAA", true)]
    [InlineData("UVGZ-LAAC", false)]
    [InlineData("KTG-AMC", true)]
    [InlineData("BVG-LPP", false)]
    public void Subscribe_IstSchemaGueltig(string domain, bool testCase)
    {
        var body = BaueSubscribe("CHE-999.999.996", "Muster AG", "Peter Müller", "1234", domain,
            "6000", "Luzern", "Versicherer", "K-1", "V-1", testCase, Kontext());
        IstGueltig(body);
        Assert.Equal(testCase, body.Descendants(Sdc + "TestCase").Any());
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("subscribed", false)]
    [InlineData("subscribed", true)]
    public void SynchronizeSubscribe_IstSchemaGueltig(string? state, bool abmelden)
    {
        var v = Vorgang("subscribe");
        var a = Adressat("UVG-LAA");
        a.Key = null; a.Password = null; a.State = state;
        var body = BaueSynchronizeSubscribe(v, a, new[] { "S-1" }, abmelden, Kontext());
        IstGueltig(body);
        Assert.Equal(abmelden, body.Descendants(Sdc + "Unsubscribe").Any());
        // Die Anmeldung kennt keine Credentials im CaseContext.
        Assert.DoesNotContain(body.Descendants(), e => e.Name.LocalName == "Credentials");
    }

    // ── Declare vorbereiten: Adressaten-Auswahl, TestCase, Substitution ────

    [Fact]
    public void Vorlage_LiestAdressatenUndDomain()
    {
        var v = LiesVorlage(Muster("DeclareAnnualSalary_UVG.xml").Root!);
        Assert.Equal("annual", v.Art);
        var a = Assert.Single(v.Adressaten);
        Assert.Equal("#uvg", a.AddresseeId);
        Assert.Equal("1234", a.Identification);
        Assert.Equal("UVG-LAA", a.Domain);
        Assert.True(a.Verarbeiten);
        Assert.Equal("CHE-123.123.123", v.Uid);
        Assert.Equal("ICHAG", v.Firmenname);
    }

    [Fact]
    public void Declare_Auswahl_TestCase_Substitution_InSchemaReihenfolge()
    {
        var root = Muster("DeclareAnnualSalary.xml").Root!;
        var ids = LiesVorlage(root).Adressaten.Select(a => a.AddresseeId).ToList();
        Assert.True(ids.Count > 2);
        var auswahl = ids.ToDictionary(i => i, i => i != "#uvg");

        var el = BereiteDeclareVor(root, auswahl, testCase: true, substitution: "DEC-ALT", "REQ-42", DateTime.Now);

        var job = el.Element(Sdc + "Job")!;
        Assert.Equal(new[] { "Addressees", "TestCase", "Substitution" }, job.Elements().Select(e => e.Name.LocalName));
        Assert.Equal("DEC-ALT", job.Element(Sdc + "Substitution")!.Element(Sdc + "PredecessorDeclarationIDWithAcceptedState")!.Value);
        var neu = LiesVorlage(el).Adressaten;
        Assert.False(neu.Single(a => a.AddresseeId == "#uvg").Verarbeiten);
        Assert.All(neu.Where(a => a.AddresseeId != "#uvg"), a => Assert.True(a.Verarbeiten));
        Assert.Equal("REQ-42", el.Element(Ep + "RequestContext")!.Element(Ep + "RequestID")!.Value);
        // Das Original bleibt unverändert.
        Assert.Equal("RequestID", root.Element(Ep + "RequestContext")!.Element(Ep + "RequestID")!.Value);
    }

    [Fact]
    public void Declare_OhneTestCase_EntferntVorhandeneMarke()
    {
        var root = BereiteDeclareVor(Muster("DeclareAnnualSalary_UVG.xml").Root!, null, true, null, "R1", DateTime.Now);
        var ohne = BereiteDeclareVor(root, null, false, " ", "R2", DateTime.Now);
        Assert.DoesNotContain(ohne.Descendants(), e => e.Name.LocalName is "TestCase" or "Substitution");
    }

    [Fact]
    public void Declare_VorbereitetBleibtSoGueltigWieDasMuster()
    {
        var root = Muster("DeclareAnnualSalary_UVG.xml").Root!;
        var vorher = Validator.Validate(root.ToString());
        var nachher = Validator.Validate(BereiteDeclareVor(root, new Dictionary<string, bool> { ["#uvg"] = false },
            true, "DEC-ALT", "REQ-1", DateTime.Now).ToString());
        Assert.True(nachher.Count <= vorher.Count, string.Join("\n", nachher));
    }

    // ── Antworten lesen ──────────────────────────────────────────────────────

    [Fact]
    public void DeclareAntwort_LiefertJobKey()
    {
        var body = AntwortBody(Muster("DeclareAnnualSalaryResponse.xml"));
        Assert.Equal("JobKey1234", LiesStatus(body).JobKey);
        Assert.Equal("ResponseID", LiesKopf(body).ResponseId);
        Assert.Equal("RequestID", LiesKopf(body).RequestId);
    }

    [Fact]
    public void GetStatusAntwort_Erfolg_MitDeclarationIdUndCredentials()
    {
        var st = LiesStatus(AntwortBody(Muster("GetStatusFromDeclareAnnualSalaryResponse.xml")));
        Assert.True(st.JobFinished);
        var a = Assert.Single(st.Adressaten);
        Assert.Equal("#addressee", a.AddresseeId);
        Assert.Equal("Success", a.Zustand);
        Assert.Equal("DeclarationID", a.FallId);
        Assert.Equal("Key", a.Key);
        Assert.Equal("Password", a.Password);
        Assert.Equal("BE", a.Institution);
    }

    [Fact]
    public void GetStatusAntwort_IgnoredErrorProcessing()
    {
        var body = XElement.Parse("""
            <sdst:GetStatusFromDeclareMonthlySalaryResponse
                xmlns:sdst="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types"
                xmlns:sdc="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:container"
                xmlns:ep="urn:ch:swissdec:basis:v1:20260306:components">
              <ep:JobFinished>false</ep:JobFinished>
              <sdc:Addressees>
                <sdc:Addressee addresseeID="#QST-BE">
                  <ep:AddresseeIdentification>BE</ep:AddresseeIdentification>
                  <ep:ProcessByDistributor>false</ep:ProcessByDistributor>
                  <sdc:Ignored/>
                </sdc:Addressee>
                <sdc:Addressee addresseeID="#QST-ZH">
                  <ep:AddresseeIdentification>ZH</ep:AddresseeIdentification>
                  <ep:ProcessByDistributor>true</ep:ProcessByDistributor>
                  <sdc:Error>
                    <ep:EndUserInformation>Empfänger nicht erreichbar</ep:EndUserInformation>
                    <ep:FaultInformation>
                      <ep:FaultState><ep:Code>NOT_plausible</ep:Code>
                        <ep:Notifications><ep:Error><ep:Notification>
                          <ep:QualityLevel>Plausibility</ep:QualityLevel>
                          <ep:DescriptionCode>4711</ep:DescriptionCode>
                          <ep:Description>Kanton passt nicht</ep:Description>
                        </ep:Notification></ep:Error></ep:Notifications>
                      </ep:FaultState>
                    </ep:FaultInformation>
                  </sdc:Error>
                </sdc:Addressee>
                <sdc:Addressee addresseeID="#BFS">
                  <ep:AddresseeIdentification>BFS</ep:AddresseeIdentification>
                  <ep:ProcessByDistributor>true</ep:ProcessByDistributor>
                  <sdc:Processing/>
                </sdc:Addressee>
              </sdc:Addressees>
            </sdst:GetStatusFromDeclareMonthlySalaryResponse>
            """);
        var st = LiesStatus(body);
        Assert.False(st.JobFinished);
        Assert.Equal(new[] { "Ignored", "Error", "Processing" }, st.Adressaten.Select(a => a.Zustand));
        Assert.False(st.Adressaten[0].Verarbeiten);
        var err = st.Adressaten[1];
        Assert.Equal("Empfänger nicht erreichbar", err.Fehler);
        Assert.Equal("NOT_plausible", err.FehlerCode);
        var h = Assert.Single(err.Hinweise);
        Assert.Equal(("Error", "4711", "Kanton passt nicht"), (h.Art, h.Code, h.Text));
    }

    [Fact]
    public void SubscribeAntwort_LiefertSubscriptionId()
    {
        var a = Assert.Single(LiesStatus(AntwortBody(Muster("SubscribeOrganizationResponse.xml"))).Adressaten);
        Assert.Equal("Success", a.Zustand);
        Assert.Equal("DeclarationID", a.FallId);
        Assert.Null(a.Key);
    }

    [Fact]
    public void Fault_Plausibilitaet_ImKlartext()
    {
        var f = LiesFault(Muster("SalaryDeclarationFault.xml"));
        Assert.NotNull(f);
        Assert.Equal("NOT_plausible", f!.Code);
        var h = Assert.Single(f.Hinweise);
        Assert.Equal("1000", h.Code);
        Assert.Equal("Configured exception from RefApps", h.Text);
        Assert.StartsWith("NOT_plausible — ", FaultText(f.Code));
    }

    [Fact]
    public void Synchronize_Declare_CompletionMitCredentialsAusDemFall()
    {
        var s = LiesSynchronize(AntwortBody(Muster("DeclareAnnualSalarySynchronizeResponse_uvg.xml")));
        Assert.Null(s.Fehler);
        Assert.Equal("Accepted", s.State);
        Assert.Equal("DeclarationID", s.FallId);
        var story = Assert.Single(s.Stories);
        Assert.Equal(("StoryID", "Completion"), (story.StoryId, story.Art));
        Assert.Equal("httpo", s.CompletionUrl);
        Assert.Null(s.CompletionKey);

        var a = Adressat("UVG-LAA");
        Uebernehme(a, s, Array.Empty<string>());
        Assert.Equal("Accepted", a.State);
        Assert.NotNull(a.Completion);
        Assert.True(a.Completion!.KeyAusFall);
        Assert.Equal("httpo?key=Key&password=Password", a.Completion.AufrufUrl);
        Assert.Equal(new[] { "StoryID" }, ZuQuittieren(a));
    }

    [Fact]
    public void Synchronize_Anmeldung_StateUndAvailable()
    {
        var s = LiesSynchronize(AntwortBody(Muster("SubscribeOrganizationSynchronizeResponse.xml")));
        Assert.Equal("subscribed", s.State);
        Assert.Equal("SubscriptionID", s.FallId);
        Assert.Equal(new[] { "DeclareAnnualSalary: sdsdsdsd" }, s.Verfuegbar);
        Assert.Empty(s.Stories);
    }

    [Fact]
    public void Synchronize_Fehler_WirdGemeldet_StandBleibt()
    {
        var s = LiesSynchronize(XElement.Parse("""
            <x:R xmlns:x="urn:x" xmlns:ep="urn:ch:swissdec:basis:v1:20260306:components">
              <ep:Error><ep:EndUserInformation>Fall unbekannt</ep:EndUserInformation></ep:Error>
            </x:R>
            """));
        Assert.Equal("Fall unbekannt", s.Fehler);
        var a = Adressat("UVG-LAA");
        a.State = "Accepted";
        Uebernehme(a, s, Array.Empty<string>());
        Assert.Equal("Accepted", a.State);
        Assert.Equal("Fall unbekannt", a.Fehler);
    }

    // ── Stories: quittieren, Doubletten, Unterdrückung (UC005 / UC009) ──────

    private static SyncAntwort Sync(string? state, IEnumerable<SyncStory>? stories = null,
        IEnumerable<string>? quittiert = null, IEnumerable<string>? suppSender = null, IEnumerable<string>? suppInst = null)
        => new(null, null, state, "DEC-1", (quittiert ?? Array.Empty<string>()).ToList(),
            (suppSender ?? Array.Empty<string>()).ToList(), (suppInst ?? Array.Empty<string>()).ToList(),
            (stories ?? Array.Empty<SyncStory>()).ToList(), new(), new(), null, null, null, null, true);

    [Fact]
    public void Stories_WerdenNachDemNaechstenSynchronizeQuittiert()
    {
        var a = Adressat("TaxAtSource");
        Uebernehme(a, Sync("Processing", new[] { new SyncStory("Q-1", "TaxAtSource-Quittance", "<q/>") }), Array.Empty<string>());
        Assert.Equal(new[] { "Q-1" }, ZuQuittieren(a));

        Uebernehme(a, Sync("Finished"), new[] { "Q-1" });
        Assert.Empty(ZuQuittieren(a));
        Assert.Equal("Finished", a.State);
    }

    [Fact]
    public void DoppelteStory_WirdNochmalsQuittiert()
    {
        var a = Adressat("TaxAtSource");
        var q = new SyncStory("Q-1", "TaxAtSource-Quittance", "<q/>");
        Uebernehme(a, Sync("Processing", new[] { q }), Array.Empty<string>());
        Uebernehme(a, Sync("Processing"), new[] { "Q-1" });
        Uebernehme(a, Sync("Processing", new[] { q }), Array.Empty<string>());
        var s = Assert.Single(a.Stories);
        Assert.Equal(2, s.Empfangszaehler);
        Assert.Equal(new[] { "Q-1" }, ZuQuittieren(a));
    }

    [Fact]
    public void SyncText_NenntQuittierteNeueUndErneutErhalteneStoriesMitId()
    {
        // F08_03: der Empfänger schickt die schon quittierte Quittung nochmals, dazu die Completion.
        var a = Adressat("AHV-AVS");
        Uebernehme(a, Sync("CompletionReleaseMissing", new[] { new SyncStory("Q-1", "AHV-AVS-Quittance", "<q/>") }), Array.Empty<string>());
        Uebernehme(a, Sync("Finished", new[]
        {
            new SyncStory("Q-1", "AHV-AVS-Quittance", "<q/>"),
            new SyncStory("C-1", "Completion", "<c/>"),
        }), new[] { "Q-1" });

        var text = SyncText(a, new[] { "Q-1" }, new[] { "C-1" }, new[] { "Q-1" });
        Assert.Contains("quittiert: AHV-AVS-Quittance Q-1", text);
        Assert.Contains("neu: Completion C-1", text);
        Assert.Contains("erneut erhalten: AHV-AVS-Quittance Q-1", text);
        Assert.StartsWith(a.Identification + ": Finished", text);
    }

    [Fact]
    public void UnterdrueckteIds_BleibenUndWerdenImmerMitgesendet()
    {
        var a = Adressat("TaxAtSource");
        a.Ausstehend.Add(new ElmStoryGesendet { StoryId = "EIGEN-1", Xml = "<x/>" });
        Uebernehme(a, Sync("Processing", new[] { new SyncStory("I-1", "DialogMessage", Dialog) },
            suppSender: new[] { "EIGEN-1" }, suppInst: new[] { "I-1" }), Array.Empty<string>());
        Assert.Empty(a.Ausstehend);
        Assert.Empty(ZuQuittieren(a)); // unterdrückte Empfänger-Story nicht quittieren
        Uebernehme(a, Sync("Processing"), Array.Empty<string>());
        var body = BaueSynchronizeDeclare(Vorgang("monthly"), a, ZuQuittieren(a), Kontext());
        Assert.Contains(body.Descendants(Ep + "SuppressedSenderStoryIDs").Elements(), e => e.Value == "EIGEN-1");
        Assert.Contains(body.Descendants(Ep + "SuppressedInstitutionStoryIDs").Elements(), e => e.Value == "I-1");
        IstGueltig(body);
    }

    [Fact]
    public void EigeneAntwort_BleibtBisZurQuittung()
    {
        var a = Adressat("TaxAtSource");
        a.Ausstehend.Add(new ElmStoryGesendet { StoryId = "EIGEN-1", Xml = "<x/>" });
        Uebernehme(a, Sync("DialogMessagePending"), Array.Empty<string>());
        Assert.Single(a.Ausstehend);
        Uebernehme(a, Sync("Processing", quittiert: new[] { "EIGEN-1" }), Array.Empty<string>());
        Assert.Empty(a.Ausstehend);
    }

    // ── Completion-URL (Anhang E) ────────────────────────────────────────────

    [Fact]
    public void Completion_BeispielAusAnhangE()
        => Assert.Equal("https://www.institutiona.ch/?key=u1%23&password=cxsy2%25%40%3d30%23dl%c3%bc",
            ElmCompletion.Url("https://www.institutiona.ch/", "u1#", "cxsy2%@=30#dlü"),
            StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("https://x.ch/a?b=1", "https://x.ch/a?b=1&key=K&password=P")]
    [InlineData("https://x.ch/a?", "https://x.ch/a?key=K&password=P")]
    [InlineData("https://x.ch/a?b=1&", "https://x.ch/a?b=1&key=K&password=P")]
    [InlineData("https://x.ch/a#teil", "https://x.ch/a?key=K&password=P#teil")]
    public void Completion_Trenner(string basis, string erwartet)
        => Assert.Equal(erwartet, ElmCompletion.Url(basis, "K", "P"));

    [Fact]
    public void Completion_EntitaetenNichtDoppeltDekodiert()
    {
        // «&amp;» im XML ist nach dem Parsen schon «&»; «%23» darf NICHT zu «#» werden.
        var url = XElement.Parse("<u>https://x.ch/?a=1&amp;b=%23</u>").Value;
        Assert.Equal("https://x.ch/?a=1&b=%23&key=K", ElmCompletion.Url(url, "K", null));
    }

    // ── DialogMessage (Anhang D) ─────────────────────────────────────────────

    [Fact]
    public void Dialog_WirdGelesen()
    {
        var n = ElmDialog.Lies(XElement.Parse(Dialog));
        Assert.Equal("DM-1", n.StoryId);
        Assert.Equal("Rückfrage", n.Titel);
        Assert.True(n.Beantwortbar);
        Assert.Equal("frei (notStandard)", n.Art);
        Assert.Equal(4, n.Absaetze.Count);
        Assert.False(n.Absaetze[0].IstFrage);
        Assert.Equal(("String", "Nur Info"), (n.Absaetze[0].WertTyp, n.Absaetze[0].Wert));
        Assert.Equal("Amount", n.Absaetze[1].FrageTyp);
        Assert.False(n.Absaetze[1].Optional);
        Assert.True(n.Absaetze[2].Optional);
        Assert.Equal("unknown", n.Absaetze[3].Default);
        var s = Assert.Single(n.Abschnitte);
        Assert.Equal(("#s1", "Lohn"), (s.Id, s.Titel));
    }

    [Fact]
    public void Dialog_Antwort_SpiegeltUndSetztPrevious()
    {
        var fehler = new List<string>();
        var a = ElmDialog.BaueAntwort(XElement.Parse(Dialog),
            new Dictionary<short, string?> { [2] = "1'234,5" }, fehler, new DateTime(2026, 9, 30, 22, 0, 0), "DM-A");
        Assert.Empty(fehler);
        var ep = (XNamespace)"urn:ch:swissdec:basis:v1:20260306:components";
        Assert.Equal("DM-A", a.Element(ep + "StoryID")!.Value);
        Assert.Equal("DM-1", a.Element(ep + "Previous")!.Element(ep + "ResponseStoryID")!.Value);
        Assert.Equal(new[] { "Creation", "StoryID", "StandardDialogID", "Previous", "Title", "Description",
                             "Paragraph", "Paragraph", "Paragraph", "Paragraph", "Section" },
            a.Elements().Select(e => e.Name.LocalName));
        var n = ElmDialog.Lies(a);
        Assert.Equal("1234.50", n.Absaetze[1].Antwort);
        Assert.Null(n.Absaetze[2].Antwort);           // optional, leer gelassen
        Assert.Equal("unknown", n.Absaetze[3].Antwort); // Default übernommen
        Assert.Equal("Nur Info", n.Absaetze[0].Wert);
    }

    [Fact]
    public void Dialog_Pflichtfeld_FehltWirdGemeldet()
    {
        var fehler = new List<string>();
        ElmDialog.BaueAntwort(XElement.Parse(Dialog), new Dictionary<short, string?>(), fehler, DateTime.Now, "DM-A");
        Assert.Equal(new[] { "«Betrag» ist ein Pflichtfeld." }, fehler);
    }

    [Fact]
    public void Dialog_FalscherTyp_WirdGemeldet()
    {
        var fehler = new List<string>();
        ElmDialog.BaueAntwort(XElement.Parse(Dialog),
            new Dictionary<short, string?> { [2] = "viel", [4] = "vielleicht" }, fehler, DateTime.Now, "DM-A");
        Assert.Equal(2, fehler.Count);
        Assert.Contains(fehler, f => f.StartsWith("«Betrag»"));
        Assert.Contains(fehler, f => f.StartsWith("«Bestätigt»"));
    }

    [Theory]
    [InlineData("Integer", " 42 ", "42")]
    [InlineData("Double", "1,5", "1.5")]
    [InlineData("Amount", "-12", "-12.00")]
    [InlineData("Boolean", "Ja", "true")]
    [InlineData("YesNoUnknown", "nein", "no")]
    [InlineData("Date", "30.09.2026", "2026-09-30")]
    [InlineData("Date", "2026-09-30", "2026-09-30")]
    public void Dialog_WerteWerdenNormalisiert(string typ, string roh, string erwartet)
    {
        Assert.Null(ElmDialog.PruefeWert(typ, roh, out var normal));
        Assert.Equal(erwartet, normal);
    }
}
