using HrSystem.Models;
using Xunit;

namespace HrSystem.Tests;

/// <summary>QST periodisch/einmalig als Flag an der Lohnart (Walter 21.09.2026) — Seed-Regel aus dem Swissdec-Katalog.</summary>
public class LohnpositionQstPeriodischTests
{
    [Theory]
    [InlineData("1000", false)]   // Monatslohn
    [InlineData("1033", false)]   // Ortszulage
    [InlineData("1070", false)]   // Schichtzulage
    [InlineData("3000", false)]   // Kinderzulage
    [InlineData("2030", false)]   // Unfall-Taggeld
    [InlineData("1910", false)]   // Privatanteil Geschäftswagen (monatlich)
    [InlineData("1212", true)]    // Sonderzulage
    [InlineData("1216", true)]    // Verbesserungsvorschläge (TF28/TF29)
    [InlineData("1203", true)]    // Gratifikation
    [InlineData("1401", true)]    // Abgangsentschädigung
    [InlineData("1500", true)]    // VR-Honorar
    [InlineData("1960", true)]    // Beteiligungsrechte
    [InlineData("1973", true)]    // AG-übernommener BVG-Einkauf
    [InlineData("3034", true)]    // Geburtszulage
    [InlineData("1067", true)]    // Überzeit nach Austritt
    [InlineData(null, false)]
    public void SwissdecEinmalig_Seed(string? code, bool einmalig)
        => Assert.Equal(einmalig, Lohnposition.SwissdecEinmalig(code));

    [Fact]
    public void Default_Periodisch() => Assert.True(new Lohnposition().QstPeriodisch);
}
