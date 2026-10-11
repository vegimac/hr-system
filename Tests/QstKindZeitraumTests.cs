using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// QST-Zeitraum am Kind (Walter 11.10.2026, Familie-Tab): muss mit
/// <see cref="QstTarifVorschlagLogic.IstQstBerechtigt"/> übereinstimmen.
/// </summary>
public class QstKindZeitraumTests
{
    static readonly (DateOnly, DateOnly?)[] KeineAz = Array.Empty<(DateOnly, DateOnly?)>();

    static QstKindInput Kind(DateOnly? geb, bool erst = false, DateOnly? von = null, DateOnly? bis = null, bool keinUnterhalt = false)
        => new(von, bis, geb, null, erst, true, null, keinUnterhalt);

    [Fact]
    public void Minderjaehrig_bis_18_Geburtstag()
    {
        var z = QstTarifVorschlagLogic.QstZeitraeume(Kind(new DateOnly(2011, 2, 28)), KeineAz);
        Assert.Single(z);
        Assert.Equal(new DateOnly(2011, 2, 28), z[0].Von);
        Assert.Equal(new DateOnly(2029, 2, 28), z[0].Bis);
        Assert.Equal("bis 18. Geburtstag", z[0].Grund);
    }

    [Fact]
    public void Erstausbildung_ist_offen()
    {
        var z = QstTarifVorschlagLogic.QstZeitraeume(Kind(new DateOnly(2006, 10, 14), erst: true), KeineAz);
        Assert.Single(z);
        Assert.Null(z[0].Bis);
        Assert.Equal("Erstausbildung", z[0].Grund);
    }

    [Fact]
    public void Ausbildungszulage_anschliessend_verlaengert()
    {
        var geb = new DateOnly(2006, 10, 14);
        var az = new[] { (new DateOnly(2024, 10, 1), (DateOnly?)new DateOnly(2026, 7, 31)) };
        var z = QstTarifVorschlagLogic.QstZeitraeume(Kind(geb), az);
        Assert.Single(z);
        Assert.Equal(new DateOnly(2026, 7, 31), z[0].Bis);
        Assert.Contains("Ausbildungszulage", z[0].Grund);
    }

    [Fact]
    public void Ausbildungszulage_mit_Luecke_ergibt_zweiten_Abschnitt()
    {
        var geb = new DateOnly(2004, 4, 29);
        var az = new[] { (new DateOnly(2024, 9, 1), (DateOnly?)null) };
        var z = QstTarifVorschlagLogic.QstZeitraeume(Kind(geb), az);
        Assert.Equal(2, z.Count);
        Assert.Equal(new DateOnly(2022, 4, 29), z[0].Bis);
        Assert.Equal(new DateOnly(2024, 9, 1), z[1].Von);
        Assert.Null(z[1].Bis);
        Assert.False(QstTarifVorschlagLogic.IstQstBerechtigt(Kind(geb), new DateOnly(2023, 1, 1)));
    }

    [Fact]
    public void Gepflegter_Zeitraum_gilt()
    {
        var z = QstTarifVorschlagLogic.QstZeitraeume(
            Kind(new DateOnly(2002, 3, 19), von: new DateOnly(2020, 1, 1), bis: new DateOnly(2025, 6, 30)), KeineAz);
        Assert.Single(z);
        Assert.Equal(new DateOnly(2020, 1, 1), z[0].Von);
        Assert.Equal(new DateOnly(2025, 6, 30), z[0].Bis);
    }

    [Fact]
    public void Keine_Unterhaltspflicht_oder_ohne_Geburtsdatum_zaehlt_nie()
    {
        Assert.Empty(QstTarifVorschlagLogic.QstZeitraeume(Kind(new DateOnly(2015, 1, 1), keinUnterhalt: true), KeineAz));
        Assert.Empty(QstTarifVorschlagLogic.QstZeitraeume(Kind(null), KeineAz));
    }

    [Theory]
    [InlineData("2029-02-28", true)]
    [InlineData("2029-03-01", false)]
    public void Ende_stimmt_mit_IstQstBerechtigt(string stichtag, bool erwartet)
    {
        var k = Kind(new DateOnly(2011, 2, 28));
        Assert.Equal(erwartet, QstTarifVorschlagLogic.IstQstBerechtigt(k, DateOnly.Parse(stichtag)));
    }
}
