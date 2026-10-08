using HrSystem.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Virenscanner (Walter-Vorgabe 08.10.2026): Antworten von clamd auswerten und
/// «nicht bereit» statt «sauber», wenn der Scanner fehlt (Upload wird dann abgelehnt).
/// </summary>
public class VirenScannerTests
{
    [Theory]
    [InlineData("stream: OK")]
    [InlineData("stream: OK\0")]
    [InlineData("OK")]
    public void Sauber(string antwort)
        => Assert.Equal(ScanStatus.Sauber, VirenScanner.Auswerten(antwort).Status);

    [Fact]
    public void Fund_liefert_Namen()
    {
        var erg = VirenScanner.Auswerten("stream: Win.Test.EICAR_HDB-1 FOUND\0");
        Assert.Equal(ScanStatus.Fund, erg.Status);
        Assert.Equal("Win.Test.EICAR_HDB-1", erg.Virus);
    }

    [Theory]
    [InlineData("INSTREAM size limit exceeded. ERROR")]
    [InlineData("")]
    [InlineData(null)]
    public void Alles_andere_ist_nicht_bereit(string? antwort)
    {
        var erg = VirenScanner.Auswerten(antwort);
        Assert.Equal(ScanStatus.NichtBereit, erg.Status);
        Assert.False(string.IsNullOrWhiteSpace(erg.Fehler));
    }

    [Fact]
    public async Task Fehlender_Scanner_gilt_nie_als_sauber()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["VirenScanner:Socket"] = Path.Combine(Path.GetTempPath(), $"gibtsnicht-{Guid.NewGuid():N}.ctl"),
            ["VirenScanner:Pflicht"] = "true",
        }).Build();
        var scanner = new VirenScanner(config, NullLogger<VirenScanner>.Instance);

        var erg = await scanner.PruefeAsync(new byte[] { 1, 2, 3 });

        Assert.Equal(ScanStatus.NichtBereit, erg.Status);
        Assert.False(await scanner.PingAsync());
    }
}
