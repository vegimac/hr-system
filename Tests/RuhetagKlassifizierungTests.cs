using System;
using System.Collections.Generic;
using HrSystem.Services;
using Xunit;
using Art = HrSystem.Services.StundenkontrollePdfService.RuhetagArt;

namespace HrSystem.Tests;

/// <summary>
/// Ruhetag-Klassifizierung L-GAV Art. 16 (Walter-Vorgabe 04.08.2026) für das
/// Stundenkontrollblatt. Regeln:
///   • Ganzer Ruhetag: ≥ 35 zusammenhängende freie Stunden (11 Std Nachtruhe
///     + 24 Std) zwischen letztem Arbeitsende und nächstem Arbeitsbeginn;
///     k-ter freier Tag am Stück braucht ≥ 11 + 24×k Std.
///   • Halber Ruhetag vormittags: Arbeitsbeginn ≥ 12:00, Tagesarbeit ≤ 5 Std.
///   • Halber Ruhetag nachmittags: Arbeitsende ≤ 14:30, Tagesarbeit ≤ 5 Std.
///   • Gegenbeispiel der L-GAV-Kontrollstelle: 10:00–15:00 = 5 Std, aber KEIN
///     halber Ruhetag (weder Vormittag frei noch Ende ≤ 14:30).
/// </summary>
public class RuhetagKlassifizierungTests
{
    private static DateTime D(int day, int h, int min = 0) => new(2026, 7, day, h, min, 0);
    private static DateOnly Day(int day) => new(2026, 7, day);

    private static Dictionary<DateOnly, Art> Classify(
        List<(DateTime, DateTime)> work, DateOnly from, DateOnly to,
        Func<DateOnly, bool>? abwesend = null) =>
        StundenkontrollePdfService.ClassifyRuhetage(from, to, work, abwesend ?? (_ => false));

    // ── Walters Beispiel 1: Di 22:00 → Do 09:00 = exakt 35 Std ────────────
    [Fact]
    public void Ganzer_Ruhetag_bei_exakt_35_Stunden()
    {
        var work = new List<(DateTime, DateTime)>
        {
            (D(7, 14), D(7, 22, 0)),   // Di bis 22:00
            (D(9, 9), D(9, 17)),        // Do ab 09:00
        };
        var r = Classify(work, Day(8), Day(8));
        Assert.Equal(Art.Ganzer, r[Day(8)]);   // Mi = ganzer Ruhetag
    }

    [Fact]
    public void Kein_ganzer_Ruhetag_bei_34_Stunden()
    {
        var work = new List<(DateTime, DateTime)>
        {
            (D(7, 14), D(7, 22)),      // Di bis 22:00
            (D(9, 8), D(9, 17)),        // Do ab 08:00 → nur 34 Std
        };
        var r = Classify(work, Day(8), Day(8));
        Assert.Equal(Art.FreiOhneRuhetag, r[Day(8)]);
    }

    // ── Wochenende: Fr 22:00 → Mo 06:00 = 56 Std → nur EIN ganzer ────────
    [Fact]
    public void Zwei_freie_Tage_aber_nur_ein_ganzer_Ruhetag()
    {
        var work = new List<(DateTime, DateTime)>
        {
            (D(3, 14), D(3, 22)),      // Fr bis 22:00
            (D(6, 6), D(6, 14)),        // Mo ab 06:00
        };
        var r = Classify(work, Day(4), Day(5));
        Assert.Equal(Art.Ganzer, r[Day(4)]);          // Sa: braucht 35, hat 56
        Assert.Equal(Art.FreiOhneRuhetag, r[Day(5)]); // So: bräuchte 59
    }

    [Fact]
    public void Zwei_ganze_Ruhetage_wenn_Fenster_gross_genug()
    {
        var work = new List<(DateTime, DateTime)>
        {
            (D(3, 14), D(3, 22)),      // Fr bis 22:00
            (D(6, 10), D(6, 14)),       // Mo ab 10:00 → 60 Std ≥ 59
        };
        var r = Classify(work, Day(4), Day(5));
        Assert.Equal(Art.Ganzer, r[Day(4)]);
        Assert.Equal(Art.Ganzer, r[Day(5)]);
    }

    // ── Halbe Ruhetage (Walters Beispiele) ────────────────────────────────
    [Fact]
    public void Halber_Ruhetag_Vormittag_frei()
    {
        var work = new List<(DateTime, DateTime)> { (D(8, 12), D(8, 17)) }; // 12–17 = 5 Std
        var r = Classify(work, Day(8), Day(8));
        Assert.Equal(Art.HalberVormittag, r[Day(8)]);
    }

    [Fact]
    public void Halber_Ruhetag_Nachmittag_frei()
    {
        var work = new List<(DateTime, DateTime)> { (D(8, 9, 30), D(8, 14, 30)) }; // 09:30–14:30
        var r = Classify(work, Day(8), Day(8));
        Assert.Equal(Art.HalberNachmittag, r[Day(8)]);
    }

    [Fact]
    public void Gegenbeispiel_der_Kontrollstelle_10_bis_15_ist_kein_halber_Ruhetag()
    {
        var work = new List<(DateTime, DateTime)> { (D(8, 10), D(8, 15)) }; // exakt 5 Std
        var r = Classify(work, Day(8), Day(8));
        Assert.Equal(Art.Keiner, r[Day(8)]);
    }

    [Fact]
    public void Mehr_als_5_Stunden_ist_kein_halber_Ruhetag()
    {
        var work = new List<(DateTime, DateTime)> { (D(8, 12), D(8, 17, 30)) }; // 5.5 Std ab 12:00
        var r = Classify(work, Day(8), Day(8));
        Assert.Equal(Art.Keiner, r[Day(8)]);
    }

    // ── Ränder + Sonderfälle ──────────────────────────────────────────────
    [Fact]
    public void Offenes_Fenster_ohne_Arbeit_davor_oder_danach_qualifiziert()
    {
        // Keine Arbeit im geladenen Bereich → alle Tage ganze Ruhetage
        // (MA hat schlicht frei, z.B. Monatsanfang vor Stellenantritt).
        var r = Classify(new List<(DateTime, DateTime)>(), Day(1), Day(3));
        Assert.All(r.Values, v => Assert.Equal(Art.Ganzer, v));
    }

    [Fact]
    public void Absenz_Tage_werden_nicht_klassifiziert_und_verbrauchen_keinen_Slot()
    {
        var work = new List<(DateTime, DateTime)>
        {
            (D(3, 14), D(3, 22)),      // Fr bis 22:00
            (D(10, 9), D(10, 17)),      // nächster Einsatz erst Fr drauf
        };
        // Mo–Do (6.–9.) Ferien; Sa+So (4./5.) frei.
        bool Abw(DateOnly d) => d >= Day(6) && d <= Day(9);
        var r = Classify(work, Day(4), Day(9), Abw);
        Assert.Equal(Art.Ganzer, r[Day(4)]);   // Sa: k=1, Fenster riesig
        Assert.Equal(Art.Ganzer, r[Day(5)]);   // So: k=2, Fenster ≥ 59
        Assert.False(r.ContainsKey(Day(6)));   // Ferien-Tage: keine Klassifizierung
        Assert.False(r.ContainsKey(Day(9)));
    }

    [Fact]
    public void Nachtschicht_Auslauf_macht_den_Folgetag_nicht_frei()
    {
        var work = new List<(DateTime, DateTime)>
        {
            (D(3, 18), D(4, 1, 30)),   // Fr 18:00 – Sa 01:30 (über Mitternacht)
            (D(6, 9), D(6, 17)),        // Mo ab 09:00
        };
        var r = Classify(work, Day(4), Day(5));
        // Sa hat den Schicht-Auslauf → kein voller freier Kalendertag.
        Assert.Equal(Art.Keiner, r[Day(4)]);
        // So: erster zählbarer freier Tag nach Ende Sa 01:30 → Fenster
        // Sa 01:30 – Mo 09:00 = 55.5 Std ≥ 35 → ganzer Ruhetag.
        Assert.Equal(Art.Ganzer, r[Day(5)]);
    }
}
