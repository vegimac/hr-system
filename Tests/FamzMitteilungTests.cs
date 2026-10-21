using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Eine GastroSocial-Mitteilung einlesen und gegen die Zulagen des Kindes halten.
/// Der Satz kommt aus dem Tarif der Filiale, die Ziffer auf dem Entscheid ist der Beendigungsgrund.
/// </summary>
public class FamzMitteilungTests
{
    const string Etemi = """
        Abrech.-Nr.: LU 629.0535.00
        Datum: 20.10.2026
        Familienzulagen: Entscheid
        Berechtigte(r): Etemi, Sibela Massg. Gesetz: FAK LU
        AHV-Nummer: 756.7521.3228.80 / 4'431'817 Gültig ab: 01.10.2026
        Kind(er) Geburtsdatum Gültig Ab - Bis Zulagen Art Beend. Grund Domizil Staat Zulage
        Lejan 10.03.19 01.10.26 31.03.31 KZ 1 CH 215.00
        Alina 23.11.21 01.10.26 30.11.33 KZ 1 CH 215.00
        Anspruch für 2 Kinder 01.10.26 bis auf weiteres 430.00
        Wichtige Hinweise
        Lejan 10.03.19 01.01.01 02.01.01 KZ 1 CH 999.00
        Legende zum Beendigungsgrund
        1 Wechsel in eine andere Altersstufe
        """;

    const string DenisAz = """
        AbrechNr: LU 629.0535.00
        Datum: 07.08.2026
        Familienzulagen: Entscheid
        Berechtigte(r):
        AHV-Nummer:
        Velaj, Valbone
        756.7027.2234.83 / 2'697'176
        Massg. Gesetz: FAK LU
        Kind Geburtsdatum Gültig ab Gültig bis Zulagenart Beend.-grund Domizil-Staat monatliche Zulage
        Denis 04.05.05 01.08.26 31.01.27 AZ 3 CH 268.00
        Anspruch für 1 Kind 01.08.26 bis auf weiteres 268.00
        """;

    const string DenisVerfall = """
        AbrechNr: LU 629.0535.00
        Datum: 30.07.2026
        Familienzulagen: Verfallanzeige
        Berechtigte(r):
        AHV-Nummer:
        Velaj, Valbone
        756.7027.2234.83 / 2'697'176
        Massg. Gesetz: FAK LU
        Der Zulagenanspruch für folgendes Kind/folgende Kinder ist per 31.07.2026 erloschen.
        Kind(er) Geburtsdatum Anspruchsende Beendigungsgrund
        Denis 04.05.2005 31.07.2026 Ausbildungsende (allg.)
        Wir bitten Sie daher, die Zahlung der Familienzulagen per obgenanntem Datum einzustellen.
        """;

    /// <summary>
    /// Direktauszahlung: oben links steht die AHV des Kindes («AHVNr.»), die der
    /// Berechtigten erst bei «AHV-Nummer:» — nicht die Kind-Nummer als MA nehmen.
    /// </summary>
    const string IslamajDirekt = """
        AHVNr. 756.4747.2853.88
        Datum: 01.07.2025
        Familienzulagen: Entscheid (Direktauszahlung monatlich ab 01.07.2025)
        Berechtigte(r): Islamaj, Samire
        AHV-Nummer: 756.5230.7859.65 / 4'290'733
        Arbeitgeber: Schaub Restaurants GmbH
        Massg. Gesetz: FAK LU
        Kind Geburtsdatum Gültig ab Gültig bis Zulagenart Beend.-grund Domizil-Staat monatliche Zulage
        Sevime 30.08.06 01.07.25 31.07.26 AZ 3 CH 268.00
        Anspruch für 1 Kind 01.07.25 bis auf weiteres 268.00
        Wichtige Hinweise
        """;

    [Fact]
    public void Entscheid_liest_Kinder_und_nicht_die_Legende()
    {
        var m = FamzMitteilungLeser.Lies(Etemi);
        Assert.Equal(FamzMitteilungArt.Entscheid, m.Art);
        Assert.Equal(new DateOnly(2026, 10, 20), m.Briefdatum);
        Assert.Equal("756.7521.3228.80", m.Ahv);
        Assert.Equal("Etemi", m.Nachname);
        Assert.Equal("Sibela", m.Vorname);
        Assert.Equal("LU", m.FakKanton);
        Assert.Equal(2, m.Kinder.Count);
        Assert.Equal("Lejan", m.Kinder[0].Vorname);
        Assert.Equal(new DateOnly(2019, 3, 10), m.Kinder[0].Geburt);
        Assert.Equal(new DateOnly(2026, 10, 1), m.Kinder[0].Von);
        Assert.Equal(new DateOnly(2031, 3, 31), m.Kinder[0].Bis);
        Assert.Equal("KZ", m.Kinder[0].Zulagenart);
        Assert.Equal(1, m.Kinder[0].Beendigungsgrund);
        Assert.Equal(215m, m.Kinder[0].Betrag);
        Assert.Equal(new DateOnly(2033, 11, 30), m.Kinder[1].Bis);
    }

    [Fact]
    public void Direktauszahlung_nimmt_AHV_der_Berechtigten_nicht_des_Kindes()
    {
        var m = FamzMitteilungLeser.Lies(IslamajDirekt);
        Assert.Equal(FamzMitteilungArt.Entscheid, m.Art);
        Assert.Equal("756.5230.7859.65", m.Ahv);
        Assert.NotEqual("756.4747.2853.88", m.Ahv);
        Assert.Equal("Islamaj", m.Nachname);
        Assert.Equal("Samire", m.Vorname);
        var k = Assert.Single(m.Kinder);
        Assert.Equal("Sevime", k.Vorname);
        Assert.Equal(new DateOnly(2006, 8, 30), k.Geburt);
        Assert.Equal(new DateOnly(2025, 7, 1), k.Von);
        Assert.Equal(new DateOnly(2026, 7, 31), k.Bis);
        Assert.Equal("AZ", k.Zulagenart);
        Assert.Equal(268m, k.Betrag);
    }

    [Fact]
    public void Ausbildungszulage_Grund_3_ist_nicht_der_Satz()
    {
        var m = FamzMitteilungLeser.Lies(DenisAz);
        Assert.Equal("Velaj", m.Nachname);
        Assert.Equal("Valbone", m.Vorname);
        Assert.Equal("756.7027.2234.83", m.Ahv);
        var d = Assert.Single(m.Kinder);
        Assert.Equal(new DateOnly(2005, 5, 4), d.Geburt);
        Assert.Equal(new DateOnly(2026, 8, 1), d.Von);
        Assert.Equal(new DateOnly(2027, 1, 31), d.Bis);
        Assert.Equal("AZ", d.Zulagenart);
        Assert.Equal(3, d.Beendigungsgrund);
        Assert.Equal(268m, d.Betrag);
    }

    [Fact]
    public void Verfallanzeige_liest_das_Anspruchsende()
    {
        var m = FamzMitteilungLeser.Lies(DenisVerfall);
        Assert.Equal(FamzMitteilungArt.Verfall, m.Art);
        Assert.Equal(new DateOnly(2026, 7, 30), m.Briefdatum);
        var d = Assert.Single(m.Kinder);
        Assert.Equal("Denis", d.Vorname);
        Assert.Equal(new DateOnly(2005, 5, 4), d.Geburt);
        Assert.Equal(new DateOnly(2026, 7, 31), d.Bis);
        Assert.Contains("Ausbildungsende", d.GrundText);
        Assert.Null(d.Betrag);
    }

    [Fact]
    public void Zweistellige_Jahre_00_bis_50_sind_2000er()
    {
        Assert.Equal(new DateOnly(2005, 5, 4), FamzMitteilungLeser.Datum("04.05.05"));
        Assert.Equal(new DateOnly(2050, 1, 1), FamzMitteilungLeser.Datum("01.01.50"));
        Assert.Equal(new DateOnly(1999, 2, 18), FamzMitteilungLeser.Datum("18.02.99"));
        Assert.Equal(new DateOnly(2031, 3, 31), FamzMitteilungLeser.Datum("31.03.31"));
    }

    [Fact]
    public void Neue_Kinderzulage_in_Luzern_Satz_1()
    {
        var m = FamzMitteilungLeser.Lies(Etemi);
        var v = FamzMitteilungVorschlag.Erstelle(m, Kontext(m, "LU", "Sursee", Kind("Lejan", new DateOnly(2019, 3, 10)), Kind("Alina", new DateOnly(2021, 11, 23))));
        Assert.Equal(2, v.Zeilen.Count);
        Assert.All(v.Zeilen, z => Assert.Equal(FamzMitteilungVorschlag.Neu, z.Aktion));
        Assert.Contains("Kinderzulage Satz 1", v.Zeilen[0].Text);
        Assert.Equal(215m, v.Zeilen[0].Betrag);
        Assert.Equal(1, v.Zeilen[0].TarifSatzNr);
        Assert.Equal(new DateOnly(2026, 10, 20), v.Zeilen[0].ErfahrenAm);
    }

    [Fact]
    public void Gleicher_Betrag_in_Aargau_ist_kein_Luzerner_Satz()
    {
        var m = FamzMitteilungLeser.Lies(Etemi);
        var tarif = new TarifAmTag(new DateOnly(2026, 10, 1), "AG", "Oftringen", "129", 2, null,
            [new SatzOption("KZ", 1, 225m, "Kinderzulage Satz 1"), new SatzOption("AZ", 1, 278.80m, "Ausbildungszulage Satz 1")]);
        var v = FamzMitteilungVorschlag.Erstelle(m, Kontext(m, tarif, Kind("Lejan", new DateOnly(2019, 3, 10)), Kind("Alina", new DateOnly(2021, 11, 23))));
        Assert.All(v.Zeilen, z => Assert.Equal(FamzMitteilungVorschlag.Unklar, z.Aktion));
        Assert.Contains("AG", v.Zeilen[0].Text);
    }

    [Fact]
    public void Entscheid_eines_anderen_Kantons_wird_nicht_uebernommen()
    {
        var m = FamzMitteilungLeser.Lies(Etemi);
        var tarif = new TarifAmTag(new DateOnly(2026, 10, 1), "AG", "Oftringen", "129", 2, null,
            [new SatzOption("KZ", 1, 215m, "Kinderzulage Satz 1")]);
        var v = FamzMitteilungVorschlag.Erstelle(m, Kontext(m, tarif, Kind("Lejan", new DateOnly(2019, 3, 10))));
        Assert.Equal(FamzMitteilungVorschlag.Unklar, v.Zeilen[0].Aktion);
        Assert.Contains("FAK LU", v.Zeilen[0].Text);
    }

    [Fact]
    public void Gleiche_Zulage_aendert_nichts()
    {
        var m = FamzMitteilungLeser.Lies(Etemi);
        var vorhanden = new FamzZulage(9, "KZ", 1, new DateOnly(2026, 10, 1), new DateOnly(2031, 3, 31), false, true);
        var v = FamzMitteilungVorschlag.Erstelle(m, Kontext(m, "LU", "Sursee",
            Kind("Lejan", new DateOnly(2019, 3, 10), vorhanden),
            Kind("Alina", new DateOnly(2021, 11, 23))));
        Assert.Equal(FamzMitteilungVorschlag.Nichts, v.Zeilen[0].Aktion);
        Assert.Equal(FamzMitteilungVorschlag.ModusBeleg, v.Zeilen[0].Modus);
        Assert.Equal(9, v.Zeilen[0].AllowanceId);
    }

    [Fact]
    public void Laufende_Zulage_endet_am_Vortag()
    {
        var m = FamzMitteilungLeser.Lies(DenisAz);
        var alt = new FamzZulage(4, "AZ", 1, new DateOnly(2025, 8, 1), null, false, false);
        var v = FamzMitteilungVorschlag.Erstelle(m, Kontext(m, "LU", "Sursee", Kind("Denis", new DateOnly(2005, 5, 4), alt)));
        var z = Assert.Single(v.Zeilen);
        Assert.Equal(FamzMitteilungVorschlag.Aendern, z.Aktion);
        Assert.Equal(FamzMitteilungVorschlag.ModusTeilen, z.Modus);
        Assert.Equal(1, z.TarifSatzNr);
        Assert.Equal(268m, z.Betrag);
        Assert.Contains("31.07.2026", z.Text);
        Assert.Equal(new DateOnly(2026, 8, 1), z.Von);
    }

    [Fact]
    public void Verfall_beendet_die_laufende_Zulage()
    {
        var m = FamzMitteilungLeser.Lies(DenisVerfall);
        var alt = new FamzZulage(4, "AZ", 1, new DateOnly(2025, 8, 1), null, false, true);
        var v = FamzMitteilungVorschlag.Erstelle(m, Kontext(m, "LU", "Sursee", Kind("Denis", new DateOnly(2005, 5, 4), alt)));
        var z = Assert.Single(v.Zeilen);
        Assert.Equal(FamzMitteilungVorschlag.Beenden, z.Aktion);
        Assert.Equal(FamzMitteilungVorschlag.ModusEnde, z.Modus);
        Assert.Equal(new DateOnly(2026, 7, 31), z.Bis);
        Assert.Contains("Dossier", z.Text);
    }

    [Fact]
    public void Verfall_auf_schon_beendete_Zulage_aendert_nichts()
    {
        var m = FamzMitteilungLeser.Lies(DenisVerfall);
        var alt = new FamzZulage(4, "AZ", 1, new DateOnly(2025, 8, 1), new DateOnly(2026, 7, 31), false, false);
        var v = FamzMitteilungVorschlag.Erstelle(m, Kontext(m, "LU", "Sursee", Kind("Denis", new DateOnly(2005, 5, 4), alt)));
        Assert.Equal(FamzMitteilungVorschlag.Nichts, Assert.Single(v.Zeilen).Aktion);
    }

    [Fact]
    public void Verfall_ohne_Zulage_wird_nicht_geraten()
    {
        var m = FamzMitteilungLeser.Lies(DenisVerfall);
        var v = FamzMitteilungVorschlag.Erstelle(m, Kontext(m, "LU", "Sursee", Kind("Denis", new DateOnly(2005, 5, 4))));
        var z = Assert.Single(v.Zeilen);
        Assert.Equal(FamzMitteilungVorschlag.Unklar, z.Aktion);
        Assert.False(z.Waehlbar);
    }

    [Fact]
    public void Erfahren_am_weicht_auf_den_ersten_offenen_Monat()
    {
        var von = new DateOnly(2026, 8, 1);
        Assert.Equal(von, FamzMitteilungVorschlag.ErfahrenAm(new DateOnly(2026, 7, 30), von, null));
        Assert.Equal(new DateOnly(2026, 10, 1),
            FamzMitteilungVorschlag.ErfahrenAm(new DateOnly(2026, 8, 7), von, new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void Echte_Mitteilungen_aus_dem_Download()
    {
        var dateien = new[]
        {
            "/Users/Walter/Downloads/FZ Etemi.pdf",
            "/Users/Walter/Downloads/15202600052855.pdf",
            "/Users/Walter/Downloads/15202600049682.pdf",
        };
        if (dateien.Any(p => !File.Exists(p))) return;

        var etemi = FamzMitteilungLeser.Lies(FamzMitteilungLeser.TextAusPdf(File.ReadAllBytes(dateien[0])));
        Assert.Equal(2, etemi.Kinder.Count);
        Assert.Equal(215m, etemi.Kinder[0].Betrag);
        Assert.Equal("756.7521.3228.80", etemi.Ahv);

        var az = FamzMitteilungLeser.Lies(FamzMitteilungLeser.TextAusPdf(File.ReadAllBytes(dateien[1])));
        Assert.Equal("AZ", Assert.Single(az.Kinder).Zulagenart);
        Assert.Equal(268m, az.Kinder[0].Betrag);
        Assert.Equal(3, az.Kinder[0].Beendigungsgrund);

        var verfall = FamzMitteilungLeser.Lies(FamzMitteilungLeser.TextAusPdf(File.ReadAllBytes(dateien[2])));
        Assert.Equal(FamzMitteilungArt.Verfall, verfall.Art);
        Assert.Equal(new DateOnly(2026, 7, 31), Assert.Single(verfall.Kinder).Bis);
    }

    static FamzKind Kind(string vorname, DateOnly geburt, params FamzZulage[] zulagen) =>
        new(vorname.GetHashCode(), vorname, geburt, zulagen);

    static FamzKontext Kontext(FamzMitteilung m, string kanton, string filiale, params FamzKind[] kinder)
    {
        var tage = m.Kinder.Select(k => (k.Von ?? k.Bis)!.Value).Distinct()
            .Select(t => Tarif(t, kanton, filiale)).ToList();
        return new FamzKontext(1, m.Vorname, m.Nachname, filiale, kanton, false, null, kinder, tage);
    }

    static FamzKontext Kontext(FamzMitteilung m, TarifAmTag tarif, params FamzKind[] kinder) =>
        new(1, m.Vorname, m.Nachname, tarif.Filiale, tarif.Kanton, false, null, kinder, [tarif]);

    static TarifAmTag Tarif(DateOnly tag, string kanton, string filiale) => new(
        tag, kanton, filiale, "750", 1, null,
        [
            new SatzOption("KZ", 1, 215m, "Kinderzulage Satz 1"),
            new SatzOption("KZ", 2, 260m, "Kinderzulage Satz 2 ab 12 J."),
            new SatzOption("AZ", 1, 268m, "Ausbildungszulage Satz 1"),
            new SatzOption("GZ", null, 1075m, "Geburtszulage"),
            new SatzOption("AdoptZ", null, 1075m, "Adoptionszulage"),
        ]);
}
