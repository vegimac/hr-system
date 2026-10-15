using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Lohnmeldungen für den Benutzer (Walter 15.10.2026): Stand je Empfänger, Ampel, automatische
/// Abfrage, Ersatzmeldung übernimmt die DeclarationID (F05_06).
/// </summary>
public class ElmMeldungAnzeigeTests
{
    private static readonly DateTime Jetzt = new(2026, 10, 15, 12, 0, 0);

    private static ElmVorgangAdressat Adr(string zustand, string? state = null, bool credentials = true) => new()
    {
        AddresseeId = Guid.NewGuid().ToString("N")[..6], Identification = "BE", Domain = "TaxAtSource",
        Zustand = zustand, State = state,
        FallId = zustand == "Success" ? "18da230ab6d74b23e" : null,
        Key = credentials ? "k" : null, Password = credentials ? "p" : null,
    };

    private static string DialogXml(bool mitFrage) => $"""
        <sdc:DialogMessage xmlns:sdc="urn:ch:swissdec:elm:v6:20260306:salarydeclaration:container"
                           xmlns:ep="urn:ch:swissdec:basis:v1:20260306:components">
          <ep:Creation>2026-10-15T15:37:00.000+02:00</ep:Creation>
          <ep:StoryID>DM-1</ep:StoryID>
          <ep:StandardDialogID>0000.0001.0001-001</ep:StandardDialogID>
          <ep:Title>Dialog Simple Message</ep:Title>
          <ep:Paragraph>
            <ep:ID>1</ep:ID>
            <ep:Label>This has to be confirmed</ep:Label>
            {(mitFrage ? "<ep:Answer><ep:String/></ep:Answer>" : "<ep:Value><ep:String>Info</ep:String></ep:Value>")}
          </ep:Paragraph>
        </sdc:DialogMessage>
        """;

    /// <summary>Empfänger im Zustand DialogMessagePending mit einer Rückfrage (mit oder ohne Eingabefeld).</summary>
    private static ElmVorgangAdressat Rueckfrage(bool mitFrage = true)
    {
        var a = Adr("Success", "DialogMessagePending");
        a.Stories.Add(new ElmStoryEmpfangen { StoryId = "DM-1", Art = "DialogMessage", Xml = DialogXml(mitFrage) });
        return a;
    }

    private static ElmVorgang Vorgang(bool fertig, params ElmVorgangAdressat[] a) => new()
    {
        Art = "monthly", Titel = "Monatsmeldung 01.2025 · TestCase", JobKey = "job", JobFinished = fertig,
        Gesendet = Jetzt.AddHours(-1), Adressaten = a.ToList(), Url = ElmEndpunkte.TestUrl,
    };

    [Theory]
    [InlineData("Processing", null, "laeuft")]
    [InlineData("Error", null, "rot")]
    [InlineData("Ignored", null, "grau")]
    [InlineData("Success", null, "laeuft")]
    [InlineData("Success", "Finished", "ok")]
    [InlineData("Success", "DialogMessagePending", "laeuft")]
    [InlineData("Success", "CompletionReleaseMissing", "warn")]
    [InlineData("Success", "Rejected", "rot")]
    public void Empfaenger_Stufe(string zustand, string? state, string stufe)
        => Assert.Equal(stufe, ElmMeldungAnzeige.Empfaenger(Adr(zustand, state)).Stufe);

    [Fact]
    public void Meldung_RotVorWarnVorLaeuft()
    {
        Assert.Equal("rot", ElmMeldungAnzeige.Meldung(Vorgang(true, Rueckfrage(), Adr("Error"))).Stufe);
        Assert.Equal("warn", ElmMeldungAnzeige.Meldung(Vorgang(true, Rueckfrage(), Adr("Success"))).Stufe);
        Assert.Equal("laeuft", ElmMeldungAnzeige.Meldung(Vorgang(true, Adr("Success", "Finished"), Adr("Success"))).Stufe);
        Assert.Equal("ok", ElmMeldungAnzeige.Meldung(Vorgang(true, Adr("Success", "Finished"), Adr("Ignored"))).Stufe);
    }

    [Fact]
    public void Ersatz_NimmtDeclarationIdUndPeriodeAusAltemTitel()
    {
        var v = Vorgang(true, Adr("Ignored"), Adr("Success", "Finished"));
        Assert.True(ElmMeldungAnzeige.Ersetzbar(v));
        Assert.Equal("18da230ab6d74b23e", ElmMeldungAnzeige.DeclarationId(v));
        Assert.Equal((2025, 1), ElmMeldungAnzeige.Periode(v));
        Assert.Equal((2025, (int?)null), ElmMeldungAnzeige.Periode(new ElmVorgang { Art = "annual", Titel = "Jahresmeldung AHV 2025 · TestCase" }));
    }

    [Fact]
    public void Ersatz_NichtVorJobFinished_UndNichtOhneAnnahme()
    {
        Assert.False(ElmMeldungAnzeige.Ersetzbar(Vorgang(false, Adr("Success"))));
        Assert.False(ElmMeldungAnzeige.Ersetzbar(Vorgang(true, Adr("Error"))));
    }

    [Fact]
    public void Automatik_Status_HoechstensAlle30Minuten()
    {
        var v = Vorgang(false, Adr("Processing"));
        Assert.True(ElmMeldungAnzeige.StatusFaellig(v, Jetzt));
        v.LetzteStatusAbfrage = Jetzt.AddMinutes(-10);
        Assert.False(ElmMeldungAnzeige.StatusFaellig(v, Jetzt));
        v.LetzteStatusAbfrage = Jetzt.AddMinutes(-31);
        Assert.True(ElmMeldungAnzeige.StatusFaellig(v, Jetzt));
        v.Gesendet = Jetzt.AddDays(-91);
        Assert.False(ElmMeldungAnzeige.StatusFaellig(v, Jetzt));
    }

    [Fact]
    public void Rueckfrage_NurMitEingabefeld_VerlangtAntwort()
    {
        var mitFrage = ElmMeldungAnzeige.Empfaenger(Rueckfrage(mitFrage: true));
        Assert.Equal("warn", mitFrage.Stufe);
        Assert.True(mitFrage.AktionNoetig);

        // SimpleMessage ohne Answer-Element: nur anzeigen und quittieren (Anhang D.2.5).
        var nurInfo = ElmMeldungAnzeige.Empfaenger(Rueckfrage(mitFrage: false));
        Assert.Equal("laeuft", nurInfo.Stufe);
        Assert.False(nurInfo.AktionNoetig);

        var beantwortet = Rueckfrage(mitFrage: true);
        beantwortet.Stories[0].Beantwortet = true;
        Assert.False(ElmMeldungAnzeige.Empfaenger(beantwortet).AktionNoetig);
    }

    [Fact]
    public void Automatik_QuittiertEmpfangeneNachrichtNach10Minuten()
    {
        var a = Rueckfrage(mitFrage: false);
        a.LetzteSynchronisierung = Jetzt.AddMinutes(-11);
        Assert.Contains(a, ElmMeldungAnzeige.SyncFaellig(Vorgang(true, a), Jetzt));
        a.Stories[0].Quittiert = true;
        a.State = "Finished";
        Assert.DoesNotContain(a, ElmMeldungAnzeige.SyncFaellig(Vorgang(true, a), Jetzt));
    }

    [Fact]
    public void Automatik_FragtNachQuittungBaldNach_WennEmpfaengerNochWartet()
    {
        // Nachricht quittiert, Empfänger meldet trotzdem noch DialogMessagePending ⇒ nach 10 Min. wieder.
        var a = Rueckfrage(mitFrage: false);
        a.Stories[0].Quittiert = true;
        a.LetzteSynchronisierung = Jetzt.AddMinutes(-11);
        Assert.Contains(a, ElmMeldungAnzeige.SyncFaellig(Vorgang(true, a), Jetzt));

        // Offene Frage: wartet auf den Benutzer, nicht auf uns ⇒ normaler Abstand.
        var frage = Rueckfrage(mitFrage: true);
        frage.Stories[0].Quittiert = true;
        frage.LetzteSynchronisierung = Jetzt.AddMinutes(-11);
        Assert.DoesNotContain(frage, ElmMeldungAnzeige.SyncFaellig(Vorgang(true, frage), Jetzt));
    }

    [Fact]
    public void Zertifizierungstest_OhneAdresse_GrauStattInBearbeitung()
    {
        var v = Vorgang(true, Adr("Success", "Processing"));
        v.Url = null;
        var st = ElmMeldungAnzeige.Meldung(v);
        Assert.Equal("grau", st.Stufe);
        Assert.False(st.AktionNoetig);

        v.Adressaten[0] = Adr("Success", "Finished");
        Assert.Equal("ok", ElmMeldungAnzeige.Meldung(v).Stufe);
    }

    [Fact]
    public void Automatik_LaesstPruefpunktFaelleOhneAdresseInRuhe()
    {
        var v = Vorgang(false, Adr("Processing"));
        v.Url = null;
        Assert.False(ElmMeldungAnzeige.StatusFaellig(v, Jetzt));
        v.JobFinished = true;
        v.Adressaten[0] = Adr("Success", "DialogMessagePending");
        Assert.Empty(ElmMeldungAnzeige.SyncFaellig(v, Jetzt));
    }

    [Fact]
    public void Automatik_Sync_NurOffeneMitCredentials_OderNochZuQuittieren()
    {
        var offen = Rueckfrage(mitFrage: true);
        offen.Stories[0].Quittiert = true;
        var fertig = Adr("Success", "Finished");
        var fertigMitQuittung = Adr("Success", "Finished");
        fertigMitQuittung.Stories.Add(new ElmStoryEmpfangen { StoryId = "s1", Art = "AHV-AVS-Quittance" });
        var ohneKey = Adr("Success", null, credentials: false);
        var v = Vorgang(true, offen, fertig, fertigMitQuittung, ohneKey, Adr("Error"));

        var faellig = ElmMeldungAnzeige.SyncFaellig(v, Jetzt);
        Assert.Equal(new[] { offen, fertigMitQuittung }, faellig);

        offen.LetzteSynchronisierung = Jetzt.AddMinutes(-20);
        Assert.DoesNotContain(offen, ElmMeldungAnzeige.SyncFaellig(v, Jetzt));
    }

    [Fact]
    public void EmpfaengerName_Alltagssprache()
    {
        Assert.Equal("Quellensteuer Kanton BE", ElmMeldungAnzeige.EmpfaengerName("TaxAtSource", "BE", null));
        Assert.Equal("AHV-Ausgleichskasse 046.000", ElmMeldungAnzeige.EmpfaengerName("AHV-AVS", "046.000", null));
        Assert.Equal("GastroSocial", ElmMeldungAnzeige.EmpfaengerName("AHV-AVS", "046.000", "GastroSocial"));
    }
}
