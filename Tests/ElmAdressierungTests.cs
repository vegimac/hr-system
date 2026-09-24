using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec Foundation-Test **F01_01 «Adressierung»** (Walter 24.09.2026).
///
/// Wortlaut der Prüfung: «Das Sendersystem ist für die korrekte Adressierung des
/// Distributors verantwortlich… Die URL kann vom Endbenutzer nicht beliebig
/// verändert werden.» Diese Tests halten genau das fest — sie schlagen an, wenn
/// jemand das freie URL-Feld wieder einführt.
/// </summary>
public class ElmAdressierungTests
{
    [Fact]
    public void NurZweiFesteZiele_MitHttpsAdressen()
    {
        Assert.Equal(2, ElmEndpunkte.Alle.Count);
        Assert.All(ElmEndpunkte.Alle, z => Assert.StartsWith("https://", z.Url));
        Assert.Contains(ElmEndpunkte.Alle, z => z.Schluessel == "test" && z.IstTest);
        Assert.Contains(ElmEndpunkte.Alle, z => z.Schluessel == "prod" && !z.IstTest);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("TEST")]
    [InlineData(" prod ")]
    public void BekannteSchluessel_WerdenAufgeloest(string eingabe)
        => Assert.NotNull(ElmEndpunkte.Finde(eingabe));

    [Theory]
    [InlineData("https://eigener-server.example/elm")]   // frei getippte Adresse
    [InlineData("http://localhost:8080")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("staging")]
    public void AllesAndere_WirdAbgewiesen(string? eingabe)
        => Assert.Null(ElmEndpunkte.Finde(eingabe));

    /// <summary>
    /// Die eigentliche Schranke für F01_01 ist die PERSON (Walter-Entscheid
    /// 24.09.2026): frei eingeben darf nur der Super-Admin. Der Test liest die
    /// Dateien, weil genau dort die Regressionsgefahr liegt — ein neuer Endpunkt
    /// ohne Super-Admin-Prüfung oder ein Admin, der sich den Bereich «Entwicklung»
    /// selbst zuteilt.
    /// </summary>
    [Fact]
    public void ElmEndpunkte_NurFuerSuperAdmin()
    {
        var code = File.ReadAllText(Path.Combine(ProjektWurzel(), "Controllers", "ElmController.cs"));
        Assert.Contains("NUR_SUPERADMIN", code);
        // Jeder Verbindungs-Einstieg prüft zuerst den Super-Admin.
        foreach (var methode in new[] { "Endpunkte(", "Ping(", "CheckInteroperability(" })
        {
            var start = code.IndexOf(methode, StringComparison.Ordinal);
            Assert.True(start > 0, $"Methode {methode} nicht gefunden.");
            var rumpf = code.Substring(start, Math.Min(600, code.Length - start));
            Assert.True(rumpf.Contains("IstSuperAdminAsync"),
                $"Super-Admin-Prüfung fehlt in {methode} — Swissdec F01_01.");
        }
    }

    [Fact]
    public void BereichEntwicklung_VergibtNurDerSuperAdmin()
    {
        var code = File.ReadAllText(Path.Combine(ProjektWurzel(), "Controllers", "UsersController.cs"));
        // Anlegen UND Ändern laufen über den Schutz — kein direktes JoinAreas(req.AllowedAreas) mehr.
        Assert.DoesNotContain("JoinAreas(req.AllowedAreas)", code);
        // Definition + zwei Aufrufe (Create und Update).
        Assert.True(Anzahl(code, "AreasMitEntwicklungsSchutz(") >= 3,
            "Anlegen und Ändern müssen beide über den Entwicklungs-Schutz laufen.");
    }

    private static int Anzahl(string text, string teil)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(teil, i, StringComparison.Ordinal)) >= 0) { n++; i += teil.Length; }
        return n;
    }

    private static string ProjektWurzel()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "hr-system.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
