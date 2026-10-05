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
    public void Lohnabtretung_zaehlt_im_Vergleich_zur_Auszahlung()
    {
        var slip = """{"auszahlungsbetrag":0,"lohnAbtretungen":[{"behoerdeName":"Amt A","betrag":1234.55},{"betrag":10.00}]}""";
        Assert.Equal(1244.55m, LohnSimulationController.LohnAbtretungTotal(slip));
        Assert.Equal(0m, LohnSimulationController.LohnAbtretungTotal("""{"auszahlungsbetrag":500}"""));
        Assert.Equal(0m, LohnSimulationController.LohnAbtretungTotal(null));
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
    [InlineData("200.190", true)]  // FamZ-Nachzahlung Vorjahr
    [InlineData("565.1", true)]    // Korrektur Quellensteuer Vorjahr
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

    [Fact]
    public void FamZ_Nachzahlung_Vorjahr_geht_auf_die_laufende_Familienzulage()
    {
        var kz = new Lohnposition { Id = 5, Code = "190.1", IsActive = true };
        var r = LohnSimulationService.WaehleLohnpositionen(
            new[] { "200.190" }, new List<(string, Lohnposition)>(), new List<Lohnposition> { kz });
        Assert.Equal(5, r["200.190"].Id);
    }

    [Fact]
    public void Lohnart_kommt_zuerst_aus_der_Raster_Verknuepfung()
    {
        var bonus  = new Lohnposition { Id = 7, Code = "205", Bezeichnung = "Bonus", IsActive = true };
        var gleich = new Lohnposition { Id = 3, Code = "200.5", Bezeichnung = "Anderes", IsActive = true };
        var r = LohnSimulationService.WaehleLohnpositionen(
            new[] { "200.5" }, new List<(string, Lohnposition)> { ("200.5", bonus) }, new List<Lohnposition> { gleich });
        Assert.Equal(7, r["200.5"].Id);
    }

    [Fact]
    public void Ohne_Verknuepfung_gilt_gleicher_Code_inaktive_zaehlen_nicht()
    {
        var aktiv   = new Lohnposition { Id = 4, Code = "950.1", IsActive = true };
        var inaktiv = new Lohnposition { Id = 9, Code = "600.5", IsActive = false };
        var verkInaktiv = new Lohnposition { Id = 11, Code = "X", IsActive = false };
        var r = LohnSimulationService.WaehleLohnpositionen(
            new[] { "950.1", "600.5", "565.1" },
            new List<(string, Lohnposition)> { ("950.1", verkInaktiv) },
            new List<Lohnposition> { aktiv, inaktiv });
        Assert.Equal(4, r["950.1"].Id);
        Assert.False(r.ContainsKey("600.5"));
        Assert.False(r.ContainsKey("565.1"));
    }

    [Fact]
    public void McBonus_mit_orangem_Haekchen_bekommt_das_Total_inkl_13ml_Anteil()
    {
        var orange = new Lohnposition { Code = "200.5", DreijehnterMlPflichtig = true, IsActive = true };
        var ohne   = new Lohnposition { Code = "200.5", DreijehnterMlPflichtig = false, IsActive = true };
        Assert.Equal(150.00m, LohnSimulationService.SonderBetrag("200.5", 137.50m, orange, 12.50m));
        Assert.Equal(137.50m, LohnSimulationService.SonderBetrag("200.5", 137.50m, ohne, 12.50m));
        Assert.Equal(200.00m, LohnSimulationService.SonderBetrag("950.1", 200.00m, orange, 12.50m));
    }
}
