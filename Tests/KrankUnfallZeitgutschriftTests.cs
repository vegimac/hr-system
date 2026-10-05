using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Zeitgutschrift Krankheit/Unfall (Walter 05.10.2026): bis «Dienstplan bis»
/// geplante Tage ÷ Arbeitstage, nicht geplant 0, danach 1/7 pro Kalendertag.
/// Beispiele aus dem Gespräch, Daten erfunden.
/// </summary>
public class KrankUnfallZeitgutschriftTests
{
    private const string Plan = KrankUnfallZeitgutschrift.DienstplanDann17;
    private static DateOnly D(int m, int t) => new(2026, m, t);
    private static HashSet<DateOnly> Tage(params DateOnly[] d) => d.ToHashSet();

    [Theory]
    [InlineData(50, 2.5)]
    [InlineData(60, 3.0)]
    [InlineData(70, 3.5)]
    [InlineData(80, 4.0)]
    [InlineData(90, 4.5)]
    [InlineData(100, 5.0)]
    public void Vorschlag_FIX_ist_fuenf_mal_Pensum(int pensum, double erwartet)
        => Assert.Equal((decimal)erwartet, KrankUnfallZeitgutschrift.ArbeitstageVorschlag("FIX", pensum, null, 42m));

    [Theory]
    [InlineData(21, 2.5)]
    [InlineData(25, 3.0)]
    [InlineData(42, 5.0)]
    public void Vorschlag_MTP_aus_Garantie(int garantie, double erwartet)
        => Assert.Equal((decimal)erwartet, KrankUnfallZeitgutschrift.ArbeitstageVorschlag("MTP", null, garantie, 42m));

    [Fact]
    public void Vorschlag_MTP_ohne_Garantie_fuenf()
        => Assert.Equal(5m, KrankUnfallZeitgutschrift.ArbeitstageVorschlag("MTP", null, null, 42m));

    [Fact]
    public void Handeingabe_sticht_Vorschlag()
    {
        var emp = new Employment { EmploymentModel = "FIX", EmploymentPercentage = 100m, ArbeitstageProWoche = 4m };
        Assert.Equal(4m, KrankUnfallZeitgutschrift.Arbeitstage(emp, 42m));
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(2.5, true)]
    [InlineData(6, true)]
    [InlineData(0, false)]
    [InlineData(2.3, false)]
    [InlineData(6.5, false)]
    public void Arbeitstage_Schritte_halbe_Tage(double wert, bool gueltig)
        => Assert.Equal(gueltig, KrankUnfallZeitgutschrift.ArbeitstageGueltig((decimal)wert));

    [Fact]
    public void Wochenstunden_FIX_Pensum_MTP_Garantie()
    {
        Assert.Equal(33.6m, KrankUnfallZeitgutschrift.Wochenstunden(
            new Employment { EmploymentModel = "FIX", EmploymentPercentage = 80m }, 42m));
        Assert.Equal(25m, KrankUnfallZeitgutschrift.Wochenstunden(
            new Employment { EmploymentModel = "MTP", GuaranteedHoursPerWeek = 25m, EmploymentPercentage = 60m }, 42m));
    }

    [Theory]
    [InlineData("KRANK", "FIX", true)]
    [InlineData("UNFALL", "FIX-M", true)]
    [InlineData("KRANK", "MTP", true)]
    [InlineData("KRANK", "FLEX", false)]
    [InlineData("FERIEN", "FIX", false)]
    public void Betrifft_nur_Krank_Unfall_FIX_MTP(string typ, string modell, bool erwartet)
        => Assert.Equal(erwartet, KrankUnfallZeitgutschrift.Betrifft(typ, modell));

    [Fact]
    public void FIX_80_vier_Tage_krank_am_Arbeitstag_volle_Tagesstunden()
    {
        // Mo–Do gearbeitet, Mittwoch krank: 33.6 ÷ 4 = 8.4 h — nicht 1/7 × 80 %.
        var mi = D(9, 2);
        var h = KrankUnfallZeitgutschrift.Stunden(Plan, mi, mi, mi, Tage(mi), 33.6m, 4m, 100m);
        Assert.Equal(8.4m, h);
    }

    [Fact]
    public void Arztzeugnis_an_freien_Tagen_gibt_kein_Plus()
    {
        // Zuletzt Sa gearbeitet, Zeugnis So–Mi, an diesen Tagen frei → 0 h statt 4 × 1/7.
        var h = KrankUnfallZeitgutschrift.Stunden(Plan, D(9, 6), D(9, 9), D(9, 9), Tage(), 42m, 5m, 100m);
        Assert.Equal(0m, h);
    }

    [Fact]
    public void Plan_bis_26_danach_ein_Siebtel()
    {
        // 1.–30.9., Plan bis 26.9. mit 19 geplanten Tagen; 27.–30.9. je 42/7.
        var geplant = new HashSet<DateOnly>();
        for (var d = D(9, 1); d <= D(9, 26); d = d.AddDays(1))
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) geplant.Add(d);
        var tage = KrankUnfallZeitgutschrift.Tage(Plan, D(9, 1), D(9, 30), D(9, 26), geplant, 42m, 5m, 100m);
        Assert.Equal(19, tage.Count(t => t.Art == KrankUnfallZeitgutschrift.TagArt.Geplant));
        Assert.Equal(7, tage.Count(t => t.Art == KrankUnfallZeitgutschrift.TagArt.Frei));
        Assert.Equal(4, tage.Count(t => t.Art == KrankUnfallZeitgutschrift.TagArt.Kalender));
        Assert.Equal(19 * 8.4m + 4 * 6m, tage.Sum(t => t.Stunden));
    }

    [Fact]
    public void Kreuze_nach_Planende_zaehlen_nicht()
    {
        var h = KrankUnfallZeitgutschrift.Stunden(Plan, D(9, 1), D(9, 2), D(9, 1), Tage(D(9, 1), D(9, 2)), 42m, 5m, 100m);
        Assert.Equal(8.4m + 6m, h);
    }

    [Fact]
    public void Ohne_Dienstplan_alles_ein_Siebtel()
    {
        var h = KrankUnfallZeitgutschrift.Stunden(Plan, D(9, 7), D(9, 13), null, Tage(D(9, 7)), 25m, 3m, 100m);
        Assert.Equal(25m, h);
    }

    [Fact]
    public void Krank_Prozent_wirkt_auf_jeden_Tag()
    {
        var mi = D(9, 2);
        Assert.Equal(4.2m, KrankUnfallZeitgutschrift.Stunden(Plan, mi, mi, mi, Tage(mi), 33.6m, 4m, 50m));
        Assert.Equal(3m, KrankUnfallZeitgutschrift.Stunden(Plan, mi, mi, null, Tage(), 42m, 5m, 50m));
    }

    [Fact]
    public void Keine_Wochengrenze_Plusstunden_moeglich()
    {
        // FIX 100 % auf 4 Tage: 42 ÷ 4 = 10.5 h pro geplantem Tag; 5 geplante Tage = 52.5 h.
        var geplant = Tage(D(9, 7), D(9, 8), D(9, 9), D(9, 10), D(9, 11));
        Assert.Equal(52.5m, KrankUnfallZeitgutschrift.Stunden(Plan, D(9, 7), D(9, 13), D(9, 13), geplant, 42m, 4m, 100m));
    }

    [Fact]
    public void Methode_Kalender_ein_Siebtel_ohne_Plan()
    {
        var h = KrankUnfallZeitgutschrift.Stunden(KrankUnfallZeitgutschrift.Kalender17,
            D(9, 7), D(9, 13), D(9, 13), Tage(), 42m, 5m, 100m);
        Assert.Equal(42m, h);
    }

    [Fact]
    public void Methode_Mo_Fr_ein_Fuenftel()
    {
        var tage = KrankUnfallZeitgutschrift.Tage(KrankUnfallZeitgutschrift.MoFr15,
            D(9, 11), D(9, 14), null, Tage(), 42m, 3m, 100m);   // Fr–Mo
        Assert.Equal(2 * 8.4m, tage.Sum(t => t.Stunden));
        Assert.Equal(2, tage.Count(t => t.Art == KrankUnfallZeitgutschrift.TagArt.Wochenende));
    }

    [Fact]
    public void Unbekannte_Methode_faellt_auf_Dienstplan()
        => Assert.Equal(Plan, KrankUnfallZeitgutschrift.Methode("XYZ"));

    [Fact]
    public void Absenz_wird_auf_Lohnperiode_beschnitten()
    {
        var a = new Absence { AbsenceType = "KRANK", DateFrom = D(9, 28), DateTo = D(10, 3), Prozent = 100m };
        var emp = new Employment { EmploymentModel = "MTP", GuaranteedHoursPerWeek = 21m };
        var filiale = new CompanyProfile { NormalWeeklyHours = 42m, ZeitgutschriftKrankMethode = Plan };
        Assert.Equal(3 * 3m, KrankUnfallZeitgutschrift.Stunden(a, emp, filiale, D(10, 1), D(10, 31)));
    }

    [Fact]
    public void Erklaerung_fuer_die_Maske()
    {
        var tage = KrankUnfallZeitgutschrift.Tage(Plan, D(9, 7), D(9, 10), D(9, 8), Tage(D(9, 7)), 42m, 5m, 100m);
        Assert.Equal("1 geplanter Tag × 8.40 h + 2 Kalendertage × 6.00 h + 1 × 0 h nicht eingeplant",
            KrankUnfallZeitgutschrift.Erklaerung(tage));
    }

    [Fact]
    public void Dienstplan_bis_wird_auf_die_Absenz_begrenzt()
    {
        Assert.Null(AbsencesController.DienstplanBisNormalisieren("2026-09-05", "FERIEN", D(9, 1), D(9, 10)));
        Assert.Null(AbsencesController.DienstplanBisNormalisieren("2026-08-31", "KRANK", D(9, 1), D(9, 10)));
        Assert.Null(AbsencesController.DienstplanBisNormalisieren(null, "KRANK", D(9, 1), D(9, 10)));
        Assert.Equal(D(9, 5), AbsencesController.DienstplanBisNormalisieren("2026-09-05", "KRANK", D(9, 1), D(9, 10)));
        Assert.Equal(D(9, 10), AbsencesController.DienstplanBisNormalisieren("2026-09-30", "UNFALL", D(9, 1), D(9, 10)));
    }

    [Fact]
    public void Gespeicherte_Kreuze_enden_beim_Planende()
    {
        var json = AbsencesController.TageBisPlanende("[\"2026-09-03\",\"2026-09-01\",\"2026-09-07\"]", D(9, 5));
        Assert.Equal("[\"2026-09-01\",\"2026-09-03\"]", json);
    }
}
