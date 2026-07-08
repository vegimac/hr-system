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
}
