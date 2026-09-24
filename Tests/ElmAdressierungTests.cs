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
    /// Der Controller darf keine URL mehr aus dem Request übernehmen. Der Test liest
    /// die Datei, weil genau das die Regressionsgefahr ist: ein neuer Endpunkt, der
    /// wieder `dto.Url` an den Transmitter reicht.
    /// </summary>
    [Fact]
    public void ControllerNimmtKeineUrlAusDemRequest()
    {
        var pfad = Path.Combine(ProjektWurzel(), "Controllers", "ElmController.cs");
        var code = File.ReadAllText(pfad);
        Assert.DoesNotContain("dto.Url", code);
        Assert.DoesNotContain("ElmUrlDto", code);
        // Ziel-Auflösung ist der einzige Weg zu einer Adresse.
        Assert.Contains("ElmEndpunkte.Finde", code);
        // Superadmin-Schranke steht auf jedem Aufruf.
        Assert.Contains("IstSuperAdminAsync", code);
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
