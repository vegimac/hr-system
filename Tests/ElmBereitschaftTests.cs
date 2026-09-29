using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HrSystem.Services.Elm;
using Xunit;
using static HrSystem.Services.Elm.ElmTransmitterClient;

namespace HrSystem.Tests;

/// <summary>Bereitschafts-Check vor dem Foundation-Termin mit itserv (Walter 29.09.2026).</summary>
public class ElmBereitschaftTests
{
    private static readonly DateTime Jetzt = new(2026, 9, 29, 18, 0, 0);

    private static X509Certificate2 Zert(string name, DateTime von, DateTime bis, bool mitSchluessel = true, string? aussteller = null)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest(name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        X509Certificate2 z;
        if (aussteller == null)
        {
            z = req.CreateSelfSigned(von, bis);
        }
        else
        {
            using var caRsa = RSA.Create(2048);
            var caReq = new CertificateRequest(aussteller, caRsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            caReq.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            using var ca = caReq.CreateSelfSigned(von.AddDays(-1), bis.AddDays(1));
            using var ohne = req.Create(ca, von, bis, RandomNumberGenerator.GetBytes(8));
            z = ohne.CopyWithPrivateKey(rsa);
        }
        return mitSchluessel ? z : X509CertificateLoader.LoadCertificate(z.RawData);
    }

    private static ElmCallResult Antwort(bool ok = true) => new(ok, ok ? 200 : 500, 300, "", "", ok ? null : "HTTP 500");

    private static ElmWsSecurity.PruefErgebnis SecOk => new(ElmWsSecurity.Befund.Gueltig, "Signatur gültig, Antwort war verschlüsselt.");

    private static ElmInterop.Befund InteropOk =>
        new(true, "Interoperabilität bestätigt", Array.Empty<string>(), "äëöü", true, true, 1m, 1m, 1m, 1m);

    [Fact]
    public void Sua_ZehnTageRest_IstGruen_MitHinweisAufErneuern()
    {
        var p = ElmBereitschaft.Sua(Zert("CN=SUA", Jetzt.AddDays(-1), Jetzt.AddDays(10)), Jetzt);
        Assert.Equal(ElmBereitschaft.Ok, p.Stufe);
        Assert.Contains("noch 10 Tage", p.Text);
        Assert.Contains("F07_07", p.Tipp);
    }

    [Fact]
    public void Sua_ZweiTageRest_IstOrange()
        => Assert.Equal(ElmBereitschaft.Warn, ElmBereitschaft.Sua(Zert("CN=SUA", Jetzt.AddDays(-8), Jetzt.AddDays(2)), Jetzt).Stufe);

    [Fact]
    public void Sua_Abgelaufen_IstRot_ErneuernNichtMehrMoeglich()
    {
        var p = ElmBereitschaft.Sua(Zert("CN=SUA", Jetzt.AddDays(-11), Jetzt.AddDays(-1)), Jetzt);
        Assert.Equal(ElmBereitschaft.Rot, p.Stufe);
        Assert.Contains("Erneuern geht jetzt nicht mehr", p.Text);
    }

    [Fact]
    public void Sua_FehltOderOhneSchluessel_IstRot()
    {
        Assert.Equal(ElmBereitschaft.Rot, ElmBereitschaft.Sua(null, Jetzt).Stufe);
        Assert.Equal(ElmBereitschaft.Rot,
            ElmBereitschaft.Sua(Zert("CN=SUA", Jetzt.AddDays(-1), Jetzt.AddDays(10), mitSchluessel: false), Jetzt).Stufe);
    }

    [Fact]
    public void Erp_SelbstSigniert_IstOrange_VonSwissdec_IstGruen()
    {
        Assert.Equal(ElmBereitschaft.Warn,
            ElmBereitschaft.Erp(Zert("CN=OneCrew", Jetzt.AddDays(-1), Jetzt.AddYears(1)), Jetzt).Stufe);
        var swissdec = Zert("CN=All Transmitters Test, O=Swissdec, C=CH", Jetzt.AddDays(-1), Jetzt.AddYears(1),
            aussteller: "CN=Test ELM Transmitter CA, O=Swissdec, C=CH");
        var p = ElmBereitschaft.Erp(swissdec, Jetzt);
        Assert.Equal(ElmBereitschaft.Ok, p.Stufe);
        Assert.Contains("Test ELM Transmitter CA", p.Text);
    }

    [Fact]
    public void Ziel_ProdIstRot_TestIstGruen()
    {
        Assert.Equal(ElmBereitschaft.Rot, ElmBereitschaft.Ziel(ElmEndpunkte.ProdUrl).Stufe);
        Assert.Equal(ElmBereitschaft.Ok, ElmBereitschaft.Ziel(ElmEndpunkte.TestUrl).Stufe);
        Assert.Equal(ElmBereitschaft.Rot, ElmBereitschaft.Ziel(null).Stufe);
    }

    [Fact]
    public void Interop_AllesGut_DoppeltSigniert_IstGruen()
    {
        var r = Antwort() with { Security = SecOk, Interop = InteropOk, DoppeltSigniert = true, DiffSekunden = 0.2 };
        var p = ElmBereitschaft.Interop(r, suaVorhanden: true);
        Assert.Equal(ElmBereitschaft.Ok, p.Stufe);
        Assert.Contains("doppelt signiert", p.Text);
    }

    [Fact]
    public void Interop_SuaDaAberEinfachSigniert_IstOrange()
    {
        var r = Antwort() with { Security = SecOk, Interop = InteropOk, DoppeltSigniert = false };
        Assert.Equal(ElmBereitschaft.Warn, ElmBereitschaft.Interop(r, suaVorhanden: true).Stufe);
    }

    [Fact]
    public void Interop_SecurityAbgelehnt_IstRot_MitMeldung()
    {
        var r = Antwort() with
        {
            Security = new ElmWsSecurity.PruefErgebnis(ElmWsSecurity.Befund.ZertifikatNichtVertrauenswuerdig, "nicht vertrauenswürdig")
        };
        var p = ElmBereitschaft.Interop(r, suaVorhanden: false);
        Assert.Equal(ElmBereitschaft.Rot, p.Stufe);
        Assert.Contains("nicht vertrauenswürdig", p.Text);
    }

    [Fact]
    public void Interop_Fault_IstRot()
    {
        var r = Antwort(false) with { FaultCode = "Client.security", FaultText = "security requirements not met" };
        var p = ElmBereitschaft.Interop(r, suaVorhanden: false);
        Assert.Equal(ElmBereitschaft.Rot, p.Stufe);
        Assert.Contains("Client.security", p.Text);
    }

    [Fact]
    public void Ping_Zeitabweichung_IstRot()
    {
        var r = Antwort() with { DiffSekunden = 90 };
        Assert.Equal(ElmBereitschaft.Rot, ElmBereitschaft.Ping(r).Stufe);
        Assert.Equal(ElmBereitschaft.Ok, ElmBereitschaft.Ping(Antwort() with { DiffSekunden = 0.3 }).Stufe);
    }

    [Fact]
    public void Monitoring_Leer_IstRot()
    {
        Assert.Equal(ElmBereitschaft.Rot, ElmBereitschaft.Monitoring(null).Stufe);
        Assert.Equal(ElmBereitschaft.Ok, ElmBereitschaft.Monitoring("OneCrew-Test").Stufe);
    }
}
