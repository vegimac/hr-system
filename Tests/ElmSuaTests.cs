using System.Security.Cryptography;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Foundation F07 — CSR/Subject-DN und Statusmeldungen (Walter/Cursor 24.09.2026).</summary>
public class ElmSuaTests
{
    [Fact]
    public void SubjectDn_KommtAusEmpfaengerFeldern_NichtSelbstErfunden()
    {
        var s = new ElmSuaSubject
        {
            CommonName = "CHE-123.456.789",
            OrganizationName = "Muster AG",
            LocalityName = "Bern",
            StateOrProvinceName = "BE",
            CountryName = "CH",
        };
        var dn = s.AlsDn();
        Assert.Contains("CN=CHE-123.456.789", dn);
        Assert.Contains("O=Muster AG", dn);
        Assert.Contains("L=Bern", dn);
        Assert.Contains("S=BE", dn);
        Assert.Contains("C=CH", dn);
    }

    [Fact]
    public void Csr_WirdAlsPemBase64Erzeugt()
    {
        var s = new ElmSuaSubject
        {
            CommonName = "Test",
            OrganizationName = "Org",
            LocalityName = "Ort",
            StateOrProvinceName = "LU",
            CountryName = "CH",
        };
        var (b64, key) = ElmSuaService.ErzeugeCsrPemBase64(s);
        Assert.False(string.IsNullOrWhiteSpace(b64));
        var pem = System.Text.Encoding.ASCII.GetString(Convert.FromBase64String(b64));
        Assert.Contains("BEGIN CERTIFICATE REQUEST", pem);
        Assert.NotNull(key.ExportParameters(true).D);
        key.Dispose();
    }

    [Fact]
    public void SignBlock_EnthaeltOneTimePassword_RenewNicht()
    {
        var s = new ElmSuaSubject
        {
            CommonName = "CN", OrganizationName = "O", LocalityName = "L",
            StateOrProvinceName = "S", CountryName = "CH",
        };
        var (sign, k1) = ElmSuaService.BaueSignBlock(s, "geheim-otp");
        Assert.Equal("SignCertificate", sign.Name.LocalName);
        Assert.Contains(sign.Elements(), e => e.Name.LocalName == "OneTimePassword" && e.Value == "geheim-otp");
        Assert.Contains(sign.Elements(), e => e.Name.LocalName == "PEM");
        k1.Dispose();

        var (renew, k2) = ElmSuaService.BaueRenewBlock(s);
        Assert.Equal("RenewCertificate", renew.Name.LocalName);
        Assert.DoesNotContain(renew.Elements(), e => e.Name.LocalName == "OneTimePassword");
        k2.Dispose();
    }

    [Theory]
    [InlineData("processing")]
    [InlineData("registered")]
    [InlineData("rejected")]
    [InlineData("verified")]
    [InlineData("expired")]
    public void StateMeldung_DecktAlleSechsZustaendeAb(string state)
    {
        var m = ElmSuaService.StateMeldung(state);
        Assert.Contains(state, m, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PemAusBase64_AkzeptiertDerUndPem()
    {
        using var rsa = RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=T", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var z = req.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1));
        var der = z.RawData;
        var pem = ElmSuaService.PemAusBase64(Convert.ToBase64String(der));
        Assert.Contains("BEGIN CERTIFICATE", pem);

        var alsPemText = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(pem));
        var wieder = ElmSuaService.PemAusBase64(alsPemText);
        Assert.Contains("BEGIN CERTIFICATE", wieder);
    }
}
