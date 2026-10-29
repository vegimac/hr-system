using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class BerufsbezeichnungTests
{
    [Theory]
    [InlineData("Info 23")]
    [InlineData("Crew 1")]
    [InlineData("A12")]
    [InlineData("Schichtleiterin 2")]
    [InlineData("Informatikeri@")]
    [InlineData("Crew_01")]
    [InlineData("Verkauf & Service")]
    public void Zahl_oder_Sonderzeichen_wird_erkannt(string text)
        => Assert.True(Berufsbezeichnung.IstGesperrt(text));

    [Theory]
    [InlineData("Informatikerin")]
    [InlineData("Schichtleiterin")]
    [InlineData("Küchenchef")]
    [InlineData("Sous-Chef")]
    [InlineData("Chef d'étage")]
    [InlineData("")]
    [InlineData(null)]
    public void Bezeichnung_ohne_Zahl_und_Sonderzeichen_bleibt(string? text)
        => Assert.False(Berufsbezeichnung.IstGesperrt(text));
}
