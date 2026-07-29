using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für <see cref="EasyAtWorkEmployeeSyncService.NormalizeCityName"/>
/// und <see cref="EasyAtWorkEmployeeSyncService.ResolveCityFromLocations"/>.
/// </summary>
public class EasyAtWorkCityNormalizeTests
{
    [Theory]
    [InlineData("Roggwil (BE)", "roggwil")]
    [InlineData("Roggwil", "roggwil")]
    [InlineData("Roggwil BE", "roggwil")]
    [InlineData("Roggwil be", "roggwil")]
    [InlineData("  Murgenthal ", "murgenthal")]
    [InlineData("Buchs (AG)", "buchs")]
    [InlineData("La Chaux-de-Fonds", "la chaux-de-fonds")]
    [InlineData("Biel/Bienne", "biel/bienne")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void NormalizeCityName_EntferntZusaetze(string? input, string expected)
        => Assert.Equal(expected, EasyAtWorkEmployeeSyncService.NormalizeCityName(input));

    [Theory]
    [InlineData("Roggwil (BE)", "Roggwil")]
    [InlineData("Roggwil BE", "Roggwil")]
    [InlineData("Roggwil", "Roggwil")]
    public void StripCityCantonSuffix_BehaeltSchreibweise(string input, string expected)
        => Assert.Equal(expected, EasyAtWorkEmployeeSyncService.StripCityCantonSuffix(input));

    [Fact]
    public void RoggwilBfs_MatchtRoggwilEasy()
        => Assert.Equal(
            EasyAtWorkEmployeeSyncService.NormalizeCityName("Roggwil (BE)"),
            EasyAtWorkEmployeeSyncService.NormalizeCityName("Roggwil"));

    [Fact]
    public void StGallen_BleibtVollstaendig()
        => Assert.Equal("st. gallen", EasyAtWorkEmployeeSyncService.NormalizeCityName("St. Gallen"));

    // Walter-Bug 29.07.2026: nach Re-Import mit Ortschaftsname matcht Bützberg direkt.
    [Fact]
    public void OrtBuetzberg_MatchtOrtschaft()
    {
        var locs = new List<(string?, string?, string?)>
        {
            ("Bützberg", "Thunstetten", "BE"),
            ("Thunstetten", "Thunstetten", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4922", "Bützberg", locs);
        Assert.Null(err);
        Assert.Equal("Bützberg", city);
        Assert.Equal("BE", canton);
    }

    [Fact]
    public void GemeindeAlsEasyOrt_LiefertOrtschaft()
    {
        // easy liefert Gemeinde-Namen → Ortschaft derselben Zeile als Adress-Ort
        var locs = new List<(string?, string?, string?)>
        {
            ("Bützberg", "Thunstetten", "BE"),
            ("Thunstetten", "Thunstetten", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4922", "Thunstetten", locs);
        Assert.Null(err);
        Assert.Equal("Thunstetten", city);
        Assert.Equal("BE", canton);
    }

    [Fact]
    public void UnbekannteOrtschaft_FehlerAuchBeiEindeutigemKanton()
    {
        // Walter 29.07.2026: kein stilles Behalten von Fantasie-Orten / «Roggwil BE».
        var locs = new List<(string?, string?, string?)>
        {
            ("Aarwangen", "Aarwangen", "BE"),
            ("Bannwil", "Bannwil", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4912", "Irgendwo", locs);
        Assert.NotNull(err);
        Assert.Null(city);
        Assert.Equal("BE", canton);
        Assert.Contains("Irgendwo", err);
        Assert.Contains("4912", err);
    }

    [Theory]
    [InlineData("Roggwil (BE)")]
    [InlineData("Roggwil BE")]
    [InlineData("Roggwil")]
    public void RoggwilMitKantonSuffix_MatchtOrtschaft(string eawCity)
    {
        // Echte AMTOVZ-Zeile: Ortschaft «Roggwil BE», Gemeinde «Roggwil (BE)» —
        // gespeichert wird ohne Suffix: «Roggwil».
        var locs = new List<(string?, string?, string?)>
        {
            ("Roggwil BE", "Roggwil (BE)", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4914", eawCity, locs);
        Assert.Null(err);
        Assert.Equal("Roggwil", city);
        Assert.Equal("BE", canton);
    }

    [Fact]
    public void OhneEasyOrt_FallbackErsteOrtschaft()
    {
        var locs = new List<(string?, string?, string?)>
        {
            ("Bützberg", "Thunstetten", "BE"),
            ("Thunstetten", "Thunstetten", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4922", null, locs);
        Assert.Null(err);
        Assert.Equal("Bützberg", city);
        Assert.Equal("BE", canton);
    }

    [Fact]
    public void MehrdeutigUeberKantone_OhneMatch_Fehler()
    {
        var locs = new List<(string?, string?, string?)>
        {
            ("Murgenthal", "Murgenthal", "AG"),
            ("Roggwil BE", "Roggwil (BE)", "BE"),
        };
        var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations("4914", "Irgendwo", locs);
        Assert.NotNull(err);
        Assert.Null(city);
        Assert.Null(canton);
    }
}
