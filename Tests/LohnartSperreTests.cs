using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Verwendete Lohnart: Steuerung und Löschen gesperrt, Name bleibt frei.</summary>
public class LohnartSperreTests
{
    static Lohnposition Monatslohn() => new()
    {
        Code = "1000",
        Bezeichnung = "Monatslohn",
        Typ = "ZULAGE",
        AhvAlvPflichtig = true,
        NbuvPflichtig = true,
        KtgPflichtig = true,
        BvgPflichtig = true,
        QstPflichtig = true,
        QstPeriodisch = true,
        LohnausweisCode = "1",
        SwissdecLohnart = "1000",
        IsActive = true,
    };

    [Fact]
    public void Bezeichnung_und_Sortierung_sind_keine_Steuerung()
    {
        var ist = Monatslohn();
        var neu = Monatslohn();
        neu.Bezeichnung = "Festlohn";
        neu.SortOrder = 5;
        neu.MirusCode = "10.1";
        Assert.False(LohnartSperre.SteuerungGeaendert(ist, neu));
    }

    [Fact]
    public void Ahv_wegnehmen_ist_eine_Aenderung()
    {
        var neu = Monatslohn();
        neu.AhvAlvPflichtig = false;
        Assert.True(LohnartSperre.SteuerungGeaendert(Monatslohn(), neu));
    }

    [Fact]
    public void Deaktivieren_ist_eine_Aenderung()
    {
        var neu = Monatslohn();
        neu.IsActive = false;
        Assert.True(LohnartSperre.SteuerungGeaendert(Monatslohn(), neu));
    }

    [Fact]
    public void Lohnausweis_leer_und_null_sind_gleich()
    {
        var neu = Monatslohn();
        neu.LohnausweisCode = "  ";
        var ist = Monatslohn();
        ist.LohnausweisCode = null;
        Assert.False(LohnartSperre.SteuerungGeaendert(ist, neu));
    }

    [Fact]
    public void Uvg_Charakter_Faktor_und_Statistik_sind_Steuerung()
    {
        var uvg = Monatslohn();
        uvg.UvgPflichtig = false;
        Assert.True(LohnartSperre.SteuerungGeaendert(Monatslohn(), uvg));

        var charakter = Monatslohn();
        charakter.QstCharakter = "jaehrlich";
        Assert.True(LohnartSperre.SteuerungGeaendert(Monatslohn(), charakter));

        var faktor = Monatslohn();
        faktor.BvgFaktor = 13m;
        Assert.True(LohnartSperre.SteuerungGeaendert(Monatslohn(), faktor));

        var statistik = Monatslohn();
        statistik.StatistikMonat = "Zulagen";
        Assert.True(LohnartSperre.SteuerungGeaendert(Monatslohn(), statistik));
    }

    [Fact]
    public void Meldung_nennt_die_Lohnart()
    {
        var text = LohnartSperre.MeldungLoeschen("1000", "Monatslohn");
        Assert.Contains("«1000 Monatslohn»", text);
        Assert.Contains("Löschen ist nicht möglich", text);
    }
}
