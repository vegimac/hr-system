using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für <see cref="EasyAtWorkEmployeeSyncService.NormalizeCityName"/>
/// (Walter-Bug 08.07.2026): PLZ 4914 hat zwei Ortschaften (Murgenthal AG,
/// Roggwil BE). Das BFS-Verzeichnis führt den mehrdeutigen Ort als
/// «Roggwil (BE)», easy@work liefert nur «Roggwil» — der Abgleich muss
/// Klammer-Zusätze und angehängte Kantonskürzel ignorieren.
/// </summary>
public class EasyAtWorkCityNormalizeTests
{
    [Theory]
    [InlineData("Roggwil (BE)", "roggwil")]
    [InlineData("Roggwil", "roggwil")]
    [InlineData("Roggwil BE", "roggwil")]
    [InlineData("  Murgenthal ", "murgenthal")]
    [InlineData("Buchs (AG)", "buchs")]
    [InlineData("La Chaux-de-Fonds", "la chaux-de-fonds")]
    [InlineData("Biel/Bienne", "biel/bienne")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeCityName_EntferntZusaetze(string? input, string expected)
        => Assert.Equal(expected, EasyAtWorkEmployeeSyncService.NormalizeCityName(input));

    [Fact]
    public void RoggwilBfs_MatchtRoggwilEasy()
        => Assert.Equal(
            EasyAtWorkEmployeeSyncService.NormalizeCityName("Roggwil (BE)"),
            EasyAtWorkEmployeeSyncService.NormalizeCityName("Roggwil"));

    // «St. Gallen» endet auf Wort > 2 Zeichen — darf nicht beschnitten werden.
    [Fact]
    public void StGallen_BleibtVollstaendig()
        => Assert.Equal("st. gallen", EasyAtWorkEmployeeSyncService.NormalizeCityName("St. Gallen"));

    // Walter-Bug 29.07.2026: PLZ 4922 = Aarwangen + Thunstetten (beide BE).
    // easy@work liefert Ortschaft «Bützberg» — darf NICHT durch alphabetischen
    // Gemeinde-Fallback «Aarwangen» überschrieben werden.
    [Fact]
    public void OrtBuetzberg_BleibtBeiPlz4922()
    {
        var locs = new List<(string?, string?)>
        {
            ("Aarwangen", "BE"),
            ("Thunstetten", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4922", "Bützberg", locs);
        Assert.Null(err);
        Assert.Equal("Bützberg", city);
        Assert.Equal("BE", canton);
    }

    [Fact]
    public void GemeindeTreffer_NutztBfsSchreibweise()
    {
        var locs = new List<(string?, string?)>
        {
            ("Aarwangen", "BE"),
            ("Thunstetten", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4922", "aarwangen", locs);
        Assert.Null(err);
        Assert.Equal("Aarwangen", city);
        Assert.Equal("BE", canton);
    }

    [Fact]
    public void OhneEasyOrt_FallbackErsteGemeinde()
    {
        var locs = new List<(string?, string?)>
        {
            ("Aarwangen", "BE"),
            ("Thunstetten", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4922", null, locs);
        Assert.Null(err);
        Assert.Equal("Aarwangen", city);
        Assert.Equal("BE", canton);
    }

    [Fact]
    public void MehrdeutigUeberKantone_OhneMatch_Fehler()
    {
        var locs = new List<(string?, string?)>
        {
            ("Murgenthal", "AG"),
            ("Roggwil (BE)", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4914", "Irgendwo", locs);
        Assert.NotNull(err);
        Assert.Null(city);
        Assert.Null(canton);
    }
}
