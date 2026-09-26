using HrSystem.Controllers;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Schritt 4c: ab wann eine QST-Mutation des Testmandanten gilt.
/// Anlass Walter 26.09.2026: TF36 Maldini September — Code T0N / Kanton TI,
/// aber «PersonTASCodeValidAsOf» zeigt auf 01.11.2025. Der September-Beleg blieb
/// darum auf C0Y BE (550.00) statt T0N TI (320.00, RefXML 2025-09).
/// </summary>
public class SwissdecQstGueltigAbTests
{
    private static DateOnly D(int j, int m, int t) => new(j, m, t);

    [Fact]
    public void ValidAsOf_InDerZukunft_WirdBeiCodeWechsel_AufDenMutationsmonatGezogen()
    {
        // TF36 Maldini, Mutation 01.09.2025: C0Y→T0N, BE→TI, ValidAsOf 01.11.2025
        var ab = SwissdecTestmandantController.QstGueltigAb(D(2025, 9, 1), D(2025, 11, 1), "T0N", "TI");
        Assert.Equal(D(2025, 9, 1), ab);
    }

    [Fact]
    public void ValidAsOf_InDerZukunft_ZaehltAuchBeiReinemKantonswechsel()
    {
        var ab = SwissdecTestmandantController.QstGueltigAb(D(2025, 9, 1), D(2025, 11, 1), null, "TI");
        Assert.Equal(D(2025, 9, 1), ab);
    }

    [Fact]
    public void ValidAsOf_Rueckwirkend_BleibtMassgebend()
    {
        // TF31/TF33/TF34: Juni-Mutation mit B0N, gilt rueckwirkend ab 01.04.2025
        var ab = SwissdecTestmandantController.QstGueltigAb(D(2025, 6, 1), D(2025, 4, 1), "B0N", null);
        Assert.Equal(D(2025, 4, 1), ab);
    }

    [Fact]
    public void ValidAsOf_GleichesDatum_BleibtUnveraendert()
    {
        // TF23 Koller Juni, TF22 Bucher Juli, TF24 Utzinger Oktober …
        var ab = SwissdecTestmandantController.QstGueltigAb(D(2025, 6, 1), D(2025, 6, 1), "B0N", null);
        Assert.Equal(D(2025, 6, 1), ab);
    }

    [Fact]
    public void OhneCodeUndKanton_BleibtDieVorankuendigungStehen()
    {
        // TF36 April 2025: nur ValidAsOf 01.01.→01.06., kein Code-/Kantonwechsel.
        // Dieser Fall lief bisher richtig und darf sich nicht veraendern.
        var ab = SwissdecTestmandantController.QstGueltigAb(D(2025, 4, 1), D(2025, 6, 1), null, null);
        Assert.Equal(D(2025, 6, 1), ab);
    }

    [Fact]
    public void OhneValidAsOf_GiltDerMutationsmonat()
    {
        // TF35 Roos September: TI→BE, B0N→B0Y, ohne ValidAsOf-Zeile.
        var ab = SwissdecTestmandantController.QstGueltigAb(D(2025, 9, 1), null, "B0Y", "BE");
        Assert.Equal(D(2025, 9, 1), ab);
    }
}

/// <summary>
/// Schritt 4c: Kantonswechsel mit Wohnsitz im Ausland.
/// Anlass Walter 26.09.2026: TF36 Maldini zieht per 01.09.2025 nach Como und wird
/// Grenzgaenger; der QST-Kanton TI ist sein ARBEITSort. Der Inland-Zweig «Umzug = QST»
/// haette die eben gesetzten Grenzgaenger-Angaben wieder geloescht.
/// </summary>
public class SwissdecWohnsitzImAuslandTests
{
    [Fact]
    public void Grenzgaenger_ZaehltAlsAuslandwohnsitz()
        => Assert.True(SwissdecTestmandantController.WohnsitzImAusland("IT", "EX"));

    [Fact]
    public void WohnkantonEX_ReichtAuchOhneGrenzgaengerFeld()
        => Assert.True(SwissdecTestmandantController.WohnsitzImAusland(null, "EX"));

    [Fact]
    public void UmzugInnerhalbDerSchweiz_BleibtInland()
    {
        // TF35 Roos September (TI → BE), TF40 Farine Oktober (VD)
        Assert.False(SwissdecTestmandantController.WohnsitzImAusland(null, "BE"));
        Assert.False(SwissdecTestmandantController.WohnsitzImAusland(null, null));
    }
}
