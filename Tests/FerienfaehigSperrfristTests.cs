using System;
using System.Collections.Generic;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Ferien während Arbeitsunfähigkeit (Walter-Vorgabe 23.09.2026):
///   • Sperrfrist Art. 336c OR — markierte («ferienfähige») Ferien unterbrechen
///     die AU-Kette NICHT, die Sperrfrist läuft durch.
///   • Ferienkürzung L-GAV — erster voller Monat Karenz, danach 1/12 pro vollem
///     Monat (85 Tage → 1/12). Ferien zählen dort nicht als Krankheitstage.
/// </summary>
public class FerienfaehigSperrfristTests
{
    private static Absence Abs(string typ, int y1, int m1, int d1, int y2, int m2, int d2, bool ferienfaehig = false)
        => new() { AbsenceType = typ, DateFrom = new DateOnly(y1, m1, d1), DateTo = new DateOnly(y2, m2, d2), Ferienfaehig = ferienfaehig };

    [Fact]
    public void WaltersBeispiel_FerienfaehigeFerien_KetteLaeuftDurch()
    {
        var liste = new List<Absence>
        {
            Abs("KRANK",  2025, 11, 27, 2025, 12, 31),
            Abs("FERIEN", 2026,  1,  1, 2026,  1,  6, ferienfaehig: true),
            Abs("KRANK",  2026,  1,  7, 2026,  3, 18),
        };
        var k = SperrfristService.FindeKette(liste, new DateOnly(2026, 2, 1));
        Assert.NotNull(k);
        Assert.Equal(new DateOnly(2025, 11, 27), k!.Beginn);   // kein Neustart am 07.01.
        Assert.Equal(new DateOnly(2026, 3, 18), k.Ende);
        Assert.Equal("KRANK", k.Grund);                        // Ferien sind kein eigener Grund
    }

    [Fact]
    public void OhneMarkierung_FerienUnterbrechenWieBisher()
    {
        var liste = new List<Absence>
        {
            Abs("KRANK",  2025, 11, 27, 2025, 12, 31),
            Abs("FERIEN", 2026,  1,  1, 2026,  1,  6),
            Abs("KRANK",  2026,  1,  7, 2026,  3, 18),
        };
        var k = SperrfristService.FindeKette(liste, new DateOnly(2026, 2, 1));
        Assert.Equal(new DateOnly(2026, 1, 7), k!.Beginn);
    }

    [Fact]
    public void StichtagMittenInFerienfaehigenFerien_IstGeschuetzt()
    {
        // Beispiel Fachtext: krank 1.1.–15.4., Ferien 1.–14.3. ferienfähig
        var liste = new List<Absence>
        {
            Abs("KRANK",  2026, 1,  1, 2026, 2, 28),
            Abs("FERIEN", 2026, 3,  1, 2026, 3, 14, ferienfaehig: true),
            Abs("KRANK",  2026, 3, 15, 2026, 4, 15),
        };
        var k = SperrfristService.FindeKette(liste, new DateOnly(2026, 3, 10));
        Assert.NotNull(k);
        Assert.Equal(new DateOnly(2026, 1, 1), k!.Beginn);
    }

    [Fact]
    public void FerienfaehigeFerienOhneVorausgehendeKrankheit_StartenKeineKette()
    {
        var liste = new List<Absence> { Abs("FERIEN", 2026, 3, 1, 2026, 3, 14, ferienfaehig: true) };
        Assert.Null(SperrfristService.FindeKette(liste, new DateOnly(2026, 3, 5)));
    }

    [Theory]
    [InlineData(0,   0)]
    [InlineData(59,  0)]
    [InlineData(60,  1)]
    [InlineData(85,  1)]   // Beispiel L-GAV
    [InlineData(89,  1)]
    [InlineData(90,  2)]
    [InlineData(119, 2)]
    [InlineData(120, 3)]
    [InlineData(150, 4)]
    [InlineData(179, 4)]
    public void Ferienkuerzung_NachLgavTabelle(int tage, int zwoelftel)
        => Assert.Equal(zwoelftel, FerienKuerzungService.BerechneKuerzungNachKarenz(tage, karenzMonate: 1));

    [Fact]
    public void Ferienkuerzung_TeilAu_GewichteteTage()
        // 150 Tage zu 50 % = 75 gewichtete Tage → 1/12
        => Assert.Equal(1, FerienKuerzungService.BerechneKuerzungNachKarenz(75m, karenzMonate: 1));
}
