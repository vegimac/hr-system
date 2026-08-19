using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Militär / Zivildienst / Zivilschutz nach L-GAV Art. 28 (Walter-Entscheid
/// 19.08.2026): Stufen pro ARBEITSJAHR — Tag 1–25 = 100 % Bruttolohn,
/// Tag 26 bis Berner Skala (324a OR) = 88 %, danach EO 80 %. Die 25
/// 100%-Tage zählen in die 324a-Frist HINEIN (L-GAV-Kommentar). Gemeinsamer
/// Tage-Topf für Militär UND Zivilschutz; Arbeitsjahr-Wechsel innerhalb der
/// Periode setzt den Topf zurück. Nagelt die reine Stufen-Mathematik
/// (PayrollCalculations.MilitaerStufenTage) fest.
/// </summary>
public class MilitaerArt28Tests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(1, 21)]   // 1. Dienstjahr: 3 Wochen
    [InlineData(2, 30)]   // 2. Dienstjahr: 1 Monat
    [InlineData(3, 60)]
    [InlineData(4, 60)]
    [InlineData(5, 90)]
    [InlineData(9, 90)]
    [InlineData(10, 120)]
    [InlineData(14, 120)]
    [InlineData(15, 150)]
    [InlineData(19, 150)]
    [InlineData(20, 180)]
    [InlineData(25, 180)]
    public void BernerSkala_LiefertKorrekteAnspruchstage(int dienstjahr, int erwartet)
        => Assert.Equal(erwartet, PayrollCalculations.BernerSkalaTage(dienstjahr));

    [Fact]
    public void Arbeitsjahr_StartIstEintrittsJubilaeum()
    {
        var eintritt = D(2024, 9, 15);
        Assert.Equal(D(2025, 9, 15), PayrollCalculations.ArbeitsjahrStart(eintritt, D(2026, 9, 1)));
        Assert.Equal(D(2026, 9, 15), PayrollCalculations.ArbeitsjahrStart(eintritt, D(2026, 9, 15)));
        Assert.Equal(D(2026, 9, 15), PayrollCalculations.ArbeitsjahrStart(eintritt, D(2027, 3, 1)));
        Assert.Equal(eintritt, PayrollCalculations.ArbeitsjahrStart(eintritt, eintritt));
    }

    [Fact]
    public void ErstesDienstjahr_Skala21_Nach25Tagen_DirektEo()
    {
        // 1. Dienstjahr: Skala 21 Tage < 25 → nach den 25 100%-Tagen ist die
        // 324a-Frist bereits konsumiert → KEINE 88%-Tage, direkt EO.
        var stufen = PayrollCalculations.MilitaerStufenTage(
            eintritt: D(2026, 5, 1), periodFrom: D(2026, 6, 1), periodTo: D(2026, 6, 30),
            dienste: new[] { (D(2026, 6, 1), D(2026, 6, 30), true) },
            vorDienste: Array.Empty<(DateOnly, DateOnly)>());
        Assert.Equal(25, stufen.Mil100);
        Assert.Equal(0,  stufen.Mil88);
        Assert.Equal(5,  stufen.Mil80);
    }

    [Fact]
    public void ZweitesDienstjahr_Skala30_25x100_Dann88()
    {
        var stufen = PayrollCalculations.MilitaerStufenTage(
            eintritt: D(2025, 5, 1), periodFrom: D(2026, 6, 1), periodTo: D(2026, 6, 30),
            dienste: new[] { (D(2026, 6, 1), D(2026, 6, 30), true) },
            vorDienste: Array.Empty<(DateOnly, DateOnly)>());
        Assert.Equal(25, stufen.Mil100);
        Assert.Equal(5,  stufen.Mil88);
        Assert.Equal(0,  stufen.Mil80);
    }

    [Fact]
    public void FuenftesDienstjahr_Skala90_40Tage_25x100_15x88()
    {
        var stufen = PayrollCalculations.MilitaerStufenTage(
            eintritt: D(2022, 1, 1), periodFrom: D(2026, 6, 1), periodTo: D(2026, 7, 31),
            dienste: new[] { (D(2026, 6, 1), D(2026, 7, 10), true) },   // 40 Kalendertage
            vorDienste: Array.Empty<(DateOnly, DateOnly)>());
        Assert.Equal(25, stufen.Mil100);
        Assert.Equal(15, stufen.Mil88);
        Assert.Equal(0,  stufen.Mil80);
    }

    [Fact]
    public void VorDiensttage_ZaehlenInDenTopf()
    {
        // DJ2 (Skala 30), 20 Vor-Diensttage → Periode-Tage laufen als 21–30:
        // Tage 21–25 = 100 % (5), Tage 26–30 = 88 % (5).
        var stufen = PayrollCalculations.MilitaerStufenTage(
            eintritt: D(2025, 5, 1), periodFrom: D(2026, 6, 1), periodTo: D(2026, 6, 30),
            dienste: new[] { (D(2026, 6, 1), D(2026, 6, 10), true) },
            vorDienste: new[] { (D(2026, 5, 1), D(2026, 5, 20)) });
        Assert.Equal(5, stufen.Mil100);
        Assert.Equal(5, stufen.Mil88);
        Assert.Equal(0, stufen.Mil80);
    }

    [Fact]
    public void VorDiensttage_UeberSkala_DirektEo()
    {
        // DJ2 (Skala 30), 30 Vor-Diensttage → Frist konsumiert → alles EO.
        var stufen = PayrollCalculations.MilitaerStufenTage(
            eintritt: D(2025, 5, 1), periodFrom: D(2026, 6, 1), periodTo: D(2026, 6, 30),
            dienste: new[] { (D(2026, 6, 1), D(2026, 6, 5), true) },
            vorDienste: new[] { (D(2026, 5, 1), D(2026, 5, 30)) });
        Assert.Equal(0, stufen.Mil100);
        Assert.Equal(0, stufen.Mil88);
        Assert.Equal(5, stufen.Mil80);
    }

    [Fact]
    public void ArbeitsjahrWechsel_InDerPeriode_SetztTopfZurueck()
    {
        // Eintritt 15.09.2024 → AJ-Wechsel am 15.09.2026 mitten in der Periode.
        // Vor-Dienst 25 Tage im alten AJ (DJ2, Skala 30):
        //   10.–14.09. = Diensttage 26–30 → 88 % (5 Tage)
        //   15.–20.09. = neues AJ, Topf zurück → 100 % (6 Tage)
        var stufen = PayrollCalculations.MilitaerStufenTage(
            eintritt: D(2024, 9, 15), periodFrom: D(2026, 9, 1), periodTo: D(2026, 9, 30),
            dienste: new[] { (D(2026, 9, 10), D(2026, 9, 20), true) },
            vorDienste: new[] { (D(2026, 8, 1), D(2026, 8, 25)) });
        Assert.Equal(6, stufen.Mil100);
        Assert.Equal(5, stufen.Mil88);
        Assert.Equal(0, stufen.Mil80);
    }

    [Fact]
    public void MilitaerUndZivilschutz_TeilenEinenTopf()
    {
        // DJ2 (Skala 30): Militär 1.–20.6. (20 Tg) + Zivilschutz 21.–30.6. (10 Tg)
        // → Militär voll 100 %; Zivilschutz Tage 21–25 = 100 %, 26–30 = 88 %.
        var stufen = PayrollCalculations.MilitaerStufenTage(
            eintritt: D(2025, 5, 1), periodFrom: D(2026, 6, 1), periodTo: D(2026, 6, 30),
            dienste: new[]
            {
                (D(2026, 6, 1),  D(2026, 6, 20), true),    // Militär
                (D(2026, 6, 21), D(2026, 6, 30), false),   // Zivilschutz
            },
            vorDienste: Array.Empty<(DateOnly, DateOnly)>());
        Assert.Equal(20, stufen.Mil100);
        Assert.Equal(0,  stufen.Mil88);
        Assert.Equal(5,  stufen.Ziv100);
        Assert.Equal(5,  stufen.Ziv88);
        Assert.Equal(0,  stufen.Ziv80);
    }

    [Fact]
    public void OhneDienst_AllesNull()
    {
        var stufen = PayrollCalculations.MilitaerStufenTage(
            eintritt: D(2025, 5, 1), periodFrom: D(2026, 6, 1), periodTo: D(2026, 6, 30),
            dienste: Array.Empty<(DateOnly, DateOnly, bool)>(),
            vorDienste: Array.Empty<(DateOnly, DateOnly)>());
        Assert.Equal(0, stufen.Total);
    }
}
