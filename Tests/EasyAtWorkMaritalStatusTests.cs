using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// easy@work cf_marital_status → Cowork-Zivilstand.
/// Walter-Bug 01.08.2026: Code «E» (Getrennt) fehlte im Sync-Mapper —
/// Einzel- und Tages-Sync liessen Zivilstand leer.
/// </summary>
public class EasyAtWorkMaritalStatusTests
{
    [Theory]
    [InlineData("E", "getrennt")]
    [InlineData("e", "getrennt")]
    [InlineData("Getrennt", "getrennt")]
    [InlineData("separated", "getrennt")]
    [InlineData("M", "verheiratet")]
    [InlineData("S", "ledig")]
    [InlineData("D", "geschieden")]
    [InlineData("W", "verwitwet")]
    [InlineData("P", "eingetragene_partnerschaft")]
    [InlineData("verheiratet", "verheiratet")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("xyz", null)]
    public void MapMaritalStatus_Mapped(string? input, string? expected)
        => Assert.Equal(expected, EasyAtWorkEmployeeSyncService.MapMaritalStatus(input));
}
