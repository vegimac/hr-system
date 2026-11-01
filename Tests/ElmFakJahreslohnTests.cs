using HrSystem.Models;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// FAK-Jahresmeldung pro Person (ELM 6.0 Richtlinien 8.2.5). Erwartungen aus den Swissdec-Referenzen
/// RefXML_2025-12_RETROSPECTIVE: Zeitraum wie AHV mit genauem Ein-/Austritt, neuer Abschnitt beim
/// Kantonswechsel, FAK-Lohn = AHV-Lohn, Zulagen wiederkehrend (3000–3029) / einmalig (3030–3049).
/// </summary>
public class ElmFakJahreslohnTests
{
    static ElmFakJahreslohn.Monat M(int nr, decimal lohn, string kasse = "003.000", string kanton = "LU",
        decimal wieder = 0, decimal einmal = 0) => new(nr, lohn, wieder, einmal, kasse, kanton);

    [Fact]
    public void Zeitraum_Eintritt_und_Austritt_im_Monat_genau()
    {
        // TF12 Casanova: Eintritt 27.02.2025; TF10 Ganz: Austritt 30.10.2025
        var (von, bis) = ElmFakJahreslohn.Zeitraum(2025, 2, 10, new DateTime(2025, 2, 27), new DateTime(2025, 10, 30));
        Assert.Equal(new DateTime(2025, 2, 27), von);
        Assert.Equal(new DateTime(2025, 10, 30), bis);
    }

    [Fact]
    public void Zeitraum_Nachzahlung_nach_Austritt_ganzer_Monat()
    {
        // TF41 Meier Max: Austritt 31.03., Nachzahlung im Juli → 01.07.–31.07.
        var (von, bis) = ElmFakJahreslohn.Zeitraum(2025, 7, 7, new DateTime(2025, 1, 1), new DateTime(2025, 3, 31));
        Assert.Equal(new DateTime(2025, 7, 1), von);
        Assert.Equal(new DateTime(2025, 7, 31), bis);
    }

    [Fact]
    public void Zeitraum_Eintritt_im_Vorjahr_beginnt_am_Jahresanfang()
    {
        // TF37 Oberli: Eintritt 16.11.2024, Austritt 18.01.2025 → 01.01.–18.01.2025
        var (von, bis) = ElmFakJahreslohn.Zeitraum(2025, 1, 1, new DateTime(2024, 11, 16), new DateTime(2025, 1, 18));
        Assert.Equal(new DateTime(2025, 1, 1), von);
        Assert.Equal(new DateTime(2025, 1, 18), bis);
    }

    [Fact]
    public void Kantonswechsel_teilt_den_Abschnitt()
    {
        // TF12 Casanova: LU 27.02.–30.09., BE 01.10.–31.12.
        var monate = Enumerable.Range(2, 8).Select(m => M(m, 10_000)).ToList();
        monate.AddRange(Enumerable.Range(10, 3).Select(m => M(m, 12_000, "002.000", "BE")));

        var teile = ElmFakJahreslohn.Abschnitte(2025, 2, 12, monate, new DateTime(2025, 2, 27), new DateTime(2025, 12, 31));

        Assert.Equal(2, teile.Count);
        Assert.Equal(("003.000", "LU", new DateTime(2025, 2, 27), new DateTime(2025, 9, 30), 80_000m),
            (teile[0].Kasse, teile[0].Kanton, teile[0].Von, teile[0].Bis, teile[0].Lohn));
        Assert.Equal(("002.000", "BE", new DateTime(2025, 10, 1), new DateTime(2025, 12, 31), 36_000m),
            (teile[1].Kasse, teile[1].Kanton, teile[1].Von, teile[1].Bis, teile[1].Lohn));
    }

    [Fact]
    public void Gleiche_Kasse_anderer_Kanton_ist_ein_neuer_Abschnitt()
    {
        // TF15 Degelo: Kasse LU, Arbeitskanton ZG — der Kanton gehört zur Zeile
        var monate = new[] { M(2, 1_000, "003.000", "LU"), M(3, 1_800, "003.000", "ZG") };
        var teile = ElmFakJahreslohn.Abschnitte(2025, 2, 3, monate, null, null);
        Assert.Equal(new[] { "LU", "ZG" }, teile.Select(t => t.Kanton));
    }

    [Fact]
    public void Zulagen_wiederkehrend_und_einmalig_summiert()
    {
        // TF11 Bosshard: Kinderzulagen 4'750, Geburtszulage 1'000
        var monate = Enumerable.Range(1, 12)
            .Select(m => M(m, 29_416.65m, wieder: m <= 10 ? 400 : 375, einmal: m == 6 ? 1_000 : 0)).ToList();
        var t = Assert.Single(ElmFakJahreslohn.Abschnitte(2025, 1, 12, monate, null, null));
        Assert.Equal(4_750m, t.Wiederkehrend);
        Assert.Equal(1_000m, t.Einmalig);
    }

    [Fact]
    public void Monat_ohne_Kasse_fehlt()
    {
        var monate = new[] { M(1, 5_000), new ElmFakJahreslohn.Monat(2, 5_000, 0, 0, null, "LU"), M(3, 5_000) };
        var teile = ElmFakJahreslohn.Abschnitte(2025, 1, 3, monate, null, null);
        Assert.Equal(2, teile.Count);
        Assert.Equal(10_000m, teile.Sum(t => t.Lohn));
    }

    [Theory]
    [InlineData(3000, ElmFakJahreslohn.ZulageArt.Wiederkehrend)]
    [InlineData(3010, ElmFakJahreslohn.ZulageArt.Wiederkehrend)]
    [InlineData(3034, ElmFakJahreslohn.ZulageArt.Einmalig)]
    public void Zulagenart_nach_Lohnart(int lohnart, ElmFakJahreslohn.ZulageArt art)
        => Assert.Equal(art, ElmFakJahreslohn.Zulage(lohnart));

    [Fact]
    public void Andere_Lohnarten_sind_keine_FAK_Zulage()
    {
        Assert.Null(ElmFakJahreslohn.Zulage(1000));
        Assert.Null(ElmFakJahreslohn.Zulage(3050));
    }

    [Fact]
    public void Kasse_der_Filiale_und_Totale_je_Kanton()
    {
        var katalog = new[]
        {
            new LohndatenEmpfaenger
            {
                Id = 1, Art = "FAK", Kassennummer = "079.000", Bezeichnung = "Spida FAK", IsActive = true,
                Zuordnungen = { new CompanyProfileEmpfaenger { CompanyProfileId = 10, Mitgliednummer = "5676.3", IsActive = true } }
            }
        };
        var k = ElmFakAdressierung.FuerFiliale(katalog, 10, new DateOnly(2025, 6, 30), new HashSet<int> { 10 }, _ => "LU");
        Assert.NotNull(k);
        Assert.Equal("#FAK-079.000", k!.AddresseeId);
        Assert.Equal("5676.3", k.CustomerNumber);
        Assert.Null(ElmFakAdressierung.FuerFiliale(katalog, 20, new DateOnly(2025, 6, 30), new HashSet<int> { 10, 20 }, _ => "BE"));

        var totals = ElmFakAdressierung.Totals(k, new Dictionary<string, ElmFakAdressierung.Summe>
        {
            ["LU"] = new(476_577.45m, 4_400m, 1_000m),
        });
        string Wert(string name) => totals.Descendants().Single(x => x.Name.LocalName == name).Value;
        Assert.Equal("476577.45", Wert("Total-FAK-CAF-ContributorySalary"));
        Assert.Equal("4400.00", Wert("Total-FAK-CAF-FamilyIncomeSupplementRepetitive"));
        Assert.Equal("1000.00", Wert("Total-FAK-CAF-FamilyIncomeSupplementSingular"));
        Assert.Equal("LU", Wert("Canton"));
    }
}
