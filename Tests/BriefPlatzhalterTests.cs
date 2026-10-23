using System.Text.RegularExpressions;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class BriefPlatzhalterTests
{
    [Fact]
    public void Bekannte_Platzhalter_werden_eingesetzt()
    {
        var text = BriefPlatzhalter.FuegeEin(
            "Hallo {{ vorname }}, Austritt {{austrittsdatum}}.",
            new Dictionary<string, string> { ["vorname"] = "Ana", ["Austrittsdatum"] = "31.10.2026" });
        Assert.Equal("Hallo Ana, Austritt 31.10.2026.", text);
    }

    [Fact]
    public void Unbekannte_Klammern_bleiben_stehen()
    {
        var text = BriefPlatzhalter.FuegeEin("{{vorname}} {{unbekannt}}",
            new Dictionary<string, string> { ["vorname"] = "Ana" });
        Assert.Equal("Ana {{unbekannt}}", text);
    }

    [Fact]
    public void Leerer_Wert_raeumt_die_Klammer_weg()
    {
        var text = BriefPlatzhalter.FuegeEin("per {{austrittsdatum}}.",
            new Dictionary<string, string> { ["austrittsdatum"] = "" });
        Assert.Equal("per .", text);
    }

    [Fact]
    public void Standardtext_enthaelt_die_Grussformel_und_kennt_seine_Platzhalter()
    {
        var roh = BriefVorlageText.AustrittBetreff + "\n" + BriefVorlageText.AustrittText;
        Assert.Contains("Freundliche Grüsse", roh);
        var keys = Regex.Matches(roh, @"\{\{\s*([A-Za-z0-9_]+)\s*\}\}")
            .Select(m => m.Groups[1].Value);
        foreach (var key in keys)
            Assert.Contains(BriefPlatzhalter.Bekannt, b => b.Key == key);
    }
}
