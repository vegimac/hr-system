using System;
using System.Collections.Generic;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
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

/// <summary>
/// Rechnung für den Ferienkürzungs-Eintrag (Walter 23.09.2026) am Beispiel
/// Gamze Demirel: Eintritt 10.06.2013 → Dienstjahr 10.06.2026–09.06.2027.
/// </summary>
public class FerienKuerzungInfoTests
{
    private static HrSystem.Data.AppDbContext NewDb([System.Runtime.CompilerServices.CallerMemberName] string t = "")
        => new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<HrSystem.Data.AppDbContext>()
            .UseInMemoryDatabase("FkInfo_" + t + "_" + Guid.NewGuid()).Options);

    private static async System.Threading.Tasks.Task<int> SeedAsync(HrSystem.Data.AppDbContext db)
    {
        var e = new Employee { EmployeeNumber = "580020", FirstName = "Gamze", LastName = "Demirel", IsActive = true,
                               EntryDate = new DateTime(2013, 6, 10), DateOfBirth = new DateTime(1991, 2, 25) };
        db.Employees.Add(e);
        await db.SaveChangesAsync();
        void A(string typ, DateOnly von, DateOnly bis, decimal pct = 100, bool ff = false)
            => db.Absences.Add(new Absence { EmployeeId = e.Id, AbsenceType = typ, DateFrom = von, DateTo = bis, Prozent = pct, Ferienfaehig = ff });
        A("KRANK",  new(2026, 3, 19), new(2026, 6, 30));            // davon 21 Tage ab 10.06.
        A("FERIEN", new(2026, 7, 1),  new(2026, 7, 25), ff: true);  // zählt NICHT
        A("KRANK",  new(2026, 7, 26), new(2026, 8, 13));            // 19
        A("KRANK",  new(2026, 8, 14), new(2026, 8, 31), 50);         // 18 × 0.5 = 9
        A("KRANK",  new(2026, 9, 1),  new(2026, 9, 30), 50);         // 30 × 0.5 = 15
        await db.SaveChangesAsync();
        return e.Id;
    }

    [Fact]
    public async System.Threading.Tasks.Task Gamze_LaufendesDienstjahr_64Tage_Ein_Zwoelftel()
    {
        using var db = NewDb();
        int id = await SeedAsync(db);
        var info = await new FerienKuerzungService(db).InfoAsync(id, new DateOnly(2026, 9, 23), heute: new DateOnly(2026, 9, 23));
        Assert.Equal(new DateOnly(2026, 6, 10), info.DienstjahrVon);
        Assert.Equal(64m, info.TageKrankUnfall);
        Assert.Equal(1m, info.Zwoelftel);
        Assert.Equal(35m, info.JahresFerienTage);
        Assert.Equal(2.92m, info.GesamtTage);
        Assert.Equal(2.92m, info.NochMoeglich);
        Assert.Equal(2m, info.VorschlagGanzeTage);   // abgerundet
    }

    [Fact]
    public async System.Threading.Tasks.Task LaufendesDienstjahr_ZaehltNurBisEndeAktuellerMonat()
    {
        // Walter 23.09.2026: Zeugnis reicht bis 31.10. — im September zählen nur Tage bis 30.09.
        using var db = NewDb();
        int id = await SeedAsync(db);
        db.Absences.Add(new Absence { EmployeeId = id, AbsenceType = "KRANK",
                                      DateFrom = new DateOnly(2026, 10, 1), DateTo = new DateOnly(2026, 10, 31) });
        await db.SaveChangesAsync();
        var sept = await new FerienKuerzungService(db).InfoAsync(id, new DateOnly(2026, 9, 23), heute: new DateOnly(2026, 9, 23));
        Assert.True(sept.Laufend);
        Assert.Equal(64m, sept.TageKrankUnfall);
        var okt = await new FerienKuerzungService(db).InfoAsync(id, new DateOnly(2026, 10, 15), heute: new DateOnly(2026, 10, 15));
        Assert.Equal(95m, okt.TageKrankUnfall);   // 64 + 31 → 2/12
        Assert.Equal(2m, okt.Zwoelftel);
    }

    [Fact]
    public async System.Threading.Tasks.Task AbgelaufenesDienstjahr_ZaehltAlleTage_InklusiveVorjahr()
    {
        // Walter 23.09.2026: 2025 nichts gekürzt → abgelaufenes Dienstjahr voll, auch Nov./Dez. 2025.
        using var db = NewDb();
        int id = await SeedAsync(db);
        db.Absences.Add(new Absence { EmployeeId = id, AbsenceType = "KRANK",
                                      DateFrom = new DateOnly(2025, 11, 27), DateTo = new DateOnly(2025, 12, 31) });
        await db.SaveChangesAsync();
        var info = await new FerienKuerzungService(db).InfoAsync(id, new DateOnly(2025, 12, 1), heute: new DateOnly(2026, 9, 23));
        Assert.False(info.Laufend);
        Assert.Equal(new DateOnly(2025, 6, 10), info.DienstjahrVon);
        // 35 (27.11.–31.12.25) + 83 (19.03.–09.06.26) = 118 → 2/12
        Assert.Equal(35m + 83m, info.TageKrankUnfall);
        Assert.Equal(2m, info.Zwoelftel);
    }

    [Fact]
    public async System.Threading.Tasks.Task BereitsErfassteKuerzung_WirdAbgezogen()
    {
        using var db = NewDb();
        int id = await SeedAsync(db);
        db.FerienKuerzungen.Add(new FerienKuerzungEintrag { EmployeeId = id, Datum = new DateOnly(2026, 9, 30),
                                                            DienstjahrVon = new DateOnly(2026, 6, 10), Tage = 2m });
        await db.SaveChangesAsync();
        var info = await new FerienKuerzungService(db).InfoAsync(id, new DateOnly(2026, 9, 23), heute: new DateOnly(2026, 9, 23));
        Assert.Equal(2m, info.BereitsGekuerzt);
        Assert.Equal(0.92m, info.NochMoeglich);
        Assert.Equal(0m, info.VorschlagGanzeTage);
    }
}
