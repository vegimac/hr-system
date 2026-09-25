using HrSystem.Controllers;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// QST-Infoformular (Walter 25.09.2026): Geschlecht des Kindes als erste
/// Spalte W/M, vorausgefüllt aus dem Familie-Tab.
/// </summary>
public class QstInfoKindGeschlechtTests
{
    [Theory]
    [InlineData("female", true)]
    [InlineData("weiblich", true)]
    [InlineData("W", true)]
    [InlineData("male", false)]
    [InlineData("Männlich", false)]
    [InlineData("m", false)]
    [InlineData("divers", null)]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void Geschlecht_wird_auf_W_M_abgebildet(string? gender, bool? weiblich)
        => Assert.Equal(weiblich, QstInfoFormularController.GeschlechtWeiblich(gender));

    [Fact]
    public void Formular_mit_Geschlechtsspalte_lässt_sich_erzeugen()
    {
        var kinder = new List<QstInfoKind>
        {
            new("Muster Lea", "01.03.2019", Haushalt: true, Erstausbildung: null, Unterhalt: true, Weiblich: true),
            new("Muster Tim", "12.11.2006", Haushalt: false, Erstausbildung: true, Unterhalt: true, Weiblich: false),
            new("Muster Kim", "05.05.2015", Haushalt: true, Erstausbildung: null, Unterhalt: true, Weiblich: null),
        };
        var pdf = new QstInfoFormularPdfService().Generate(new QstInfoFormularInput(
            "Schaub Restaurants GmbH", "Filiale Test", "Hauptstrasse 1", "6000 Luzern", "041 000 00 00",
            Prefill: new QstInfoPrefill(NameVorname: "Muster Anna", Kinder: kinder)));
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }
}
