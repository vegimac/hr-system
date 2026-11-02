using HrSystem.Services.LohnFormelwerk;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Zulage ohne SV-Häkchen: Spesen nach dem Netto, alles andere im Bruttolohn
/// (Walter 02.11.2026, TF10 Ganz März 2025: 2060 Lohnabzug KA/SW −3'000 kürzt das Brutto).
/// </summary>
public class BruttoOderSpesenTests
{
    [Theory]
    [InlineData("6000", "6000", "Spesen", true)]
    [InlineData("6050", "6050", null, true)]
    [InlineData("7001", null, "Spesen", true)]
    [InlineData("2060", "2060", "Kurzarbeit/Korrekturen", false)]
    [InlineData("1420", "1420", "Austritt/Kapital", false)]
    [InlineData("1000", "1000", null, false)]
    public void Spesen_SindAusserhalbBrutto(string code, string? swissdec, string? kategorie, bool erwartet)
        => Assert.Equal(erwartet, LohnCodes.IstAusserhalbBrutto(code, swissdec, kategorie));
}
