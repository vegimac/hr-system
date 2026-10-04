using System.Text.Json;
using System.Text.Json.Nodes;
using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services.Vorsystem;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Lohn-Simulation: Saldo-Kette von Monat zu Monat, Übernahme der Ergebniswerte,
/// Auswahl der Sonderzahlungen aus dem Mirus-Lohnkonto. Kunstdaten.
/// </summary>
public class LohnSimulationTests
{
    private static SimulationLohn Z(int jahr, int monat, decimal stunden, string? fehler = null, int filiale = 1)
        => new() { EmployeeId = 7, CompanyProfileId = filiale, Jahr = jahr, Monat = monat, HourSaldo = stunden, Fehler = fehler };

    [Fact]
    public void Vormonat_ist_der_juengste_Monat_davor_auch_aus_anderer_Filiale()
    {
        var zeilen = new[] { Z(2026, 1, 10), Z(2026, 2, 12, filiale: 2), Z(2026, 4, 99) };
        var v = LohnSimulationKontext.WaehleVormonat(zeilen, 2026, 3);
        Assert.NotNull(v);
        Assert.Equal(2, v!.Monat);
        Assert.Equal(12m, v.HourSaldo);
    }

    [Fact]
    public void Vormonat_mit_Fehler_zaehlt_nicht()
    {
        var zeilen = new[] { Z(2026, 1, 10), Z(2026, 2, 0, fehler: "kein Vertrag") };
        var v = LohnSimulationKontext.WaehleVormonat(zeilen, 2026, 3);
        Assert.Equal(1, v!.Monat);
    }

    [Fact]
    public void Erster_Monat_hat_keinen_Vormonat()
    {
        Assert.Null(LohnSimulationKontext.WaehleVormonat(new[] { Z(2026, 2, 5) }, 2026, 1));
    }

    [Fact]
    public void Uebernimm_liest_Betraege_und_Saldi_aus_dem_Lohnzettel()
    {
        var slip = JsonNode.Parse("""
            { "totalLohn": 4100.50, "nettolohn": 3500.25, "auszahlungsbetrag": 3500.25,
              "svBasisAhv": 4000, "svBasisNbuv": 4000, "svBasisKtg": 4000,
              "neuerHourSaldo": -3.5, "neuerNachtSaldo": 1.25, "ferienGeldSaldoNeu": 812.40,
              "ferienTageSaldoNeu": 4.1667, "feiertagTageSaldoNeu": 2.5, "thirteenthAccumulated": 341.70 }
            """)!;
        var z = new SimulationLohn();
        LohnSimulationService.Uebernimm(z, slip, null, new Dictionary<string, decimal>());
        Assert.Equal(4100.50m, z.Brutto);
        Assert.Equal(3500.25m, z.Netto);
        Assert.Equal(-3.5m, z.HourSaldo);
        Assert.Equal(812.40m, z.FerienGeldSaldo);
        Assert.Equal(4.1667m, z.FerienTageSaldo);
        Assert.Equal(341.70m, z.ThirteenthAccumulated);
        Assert.NotNull(z.SlipJson);
    }

    [Fact]
    public void Fehlender_Saldo_im_Lohnzettel_traegt_den_Vortrag_weiter()
    {
        var slip = JsonNode.Parse("""{ "totalLohn": 0, "nettolohn": 0 }""")!;
        var z = new SimulationLohn();
        LohnSimulationService.Uebernimm(z, slip, null,
            new Dictionary<string, decimal> { ["901"] = -12.5m, ["903"] = 7m, ["906"] = 1200m });
        Assert.Equal(-12.5m, z.HourSaldo);
        Assert.Equal(7m, z.FerienTageSaldo);
        Assert.Equal(1200m, z.ThirteenthAccumulated);
    }

    [Fact]
    public void Fehlender_Saldo_im_Lohnzettel_traegt_den_Vormonat_weiter()
    {
        var slip = JsonNode.Parse("""{ "totalLohn": 0 }""")!;
        var z = new SimulationLohn();
        LohnSimulationService.Uebernimm(z, slip, Z(2026, 2, 8.75m), new Dictionary<string, decimal> { ["901"] = 99m });
        Assert.Equal(8.75m, z.HourSaldo);
    }

    [Theory]
    [InlineData("200.5", true)]    // McBonus
    [InlineData("950.1", true)]    // Vorschuss
    [InlineData("600.24", true)]   // LGAV — in der Simulation nicht automatisch
    [InlineData("10.1", false)]    // Festlohn rechnet die Engine
    [InlineData("20", false)]      // Stundenlohn
    [InlineData("180.1", false)]   // 13. ML
    [InlineData("190.1", false)]   // Familienzulagen
    [InlineData("200.9", false)]   // 13. ML a/McBonus folgt aus 200.5
    [InlineData("500.101", false)] // AHV-Abzug
    [InlineData("560.1", false)]   // QST
    public void Sonderzahlungen_nur_was_die_Engine_nicht_selbst_rechnet(string code, bool erwartet)
    {
        Assert.Equal(erwartet, LohnSimulationKontext.SonderzahlungsCodes.Contains(code));
    }

    [Fact]
    public void SlipZeilen_sammelt_alle_Lines_Listen()
    {
        var json = """
            { "lohnLines": [ { "code": "20", "bezeichnung": "Stundenlohn", "betrag": 3000.00 } ],
              "abzugLines": [ { "categoryCode": "AHV", "bezeichnung": "AHV / IV / EO", "betrag": 159.00 } ],
              "saldi": [ { "betrag": 1 } ] }
            """;
        var zeilen = LohnSimulationController.SlipZeilen(json);
        Assert.Equal(2, zeilen.Count);
        var text = JsonSerializer.Serialize(zeilen);
        Assert.Contains("Stundenlohn", text);
        Assert.Contains("AHV", text);
    }
}
