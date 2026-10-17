using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

public class SwissdecEmpfaengerListeTests
{
    static Stream? OpenSample()
    {
        // Bevorzugt die vom Entwickler heruntergeladene Liste; sonst /tmp vom Agenten.
        foreach (var p in new[]
        {
            "/Users/Walter/Downloads/Empfaengerliste (2).xlsx",
            "/tmp/swissdec-empf.xlsx",
        })
        {
            if (File.Exists(p)) return File.OpenRead(p);
        }
        return null;
    }

    [Fact]
    public void Parse_liefert_AK_Versicherer_und_Steuern()
    {
        using var stream = OpenSample();
        if (stream is null) return; // kein Sample in CI — still ok

        var list = SwissdecEmpfaengerListe.Parse(stream);
        Assert.True(list.Count > 50, $"erwartet viele Zeilen, war {list.Count}");
        Assert.Contains(list, z => z.Art == "AUSGLEICHSKASSE" && z.Kassennummer == "046.000");
        Assert.Contains(list, z => z.Art == "FAK" && z.Kassennummer == "046.000");
        Assert.Contains(list, z => z.Art == "UVG" && (z.Kassennummer ?? "").StartsWith("S"));
        Assert.Contains(list, z => z.Art == "QST" && z.KantonCode == "BE");
        Assert.Contains(list, z => z.Art == "LOHNAUSWEIS" && z.KantonCode == "BE");
    }

    [Fact]
    public void Vergleichen_erkennt_neu()
    {
        var liste = new List<SwissdecEmpfaengerListe.Zeile>
        {
            new("AUSGLEICHSKASSE", "Test AK", "999.000", null, null, "a@b.ch", "AHV"),
        };
        var diffs = SwissdecEmpfaengerListe.Vergleichen(liste, Array.Empty<HrSystem.Models.LohndatenEmpfaenger>());
        Assert.Single(diffs);
        Assert.Equal("neu", diffs[0].Status);
    }
}
