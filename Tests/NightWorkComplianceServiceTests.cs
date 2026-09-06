using System;
using System.Collections.Generic;
using System.Linq;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Unit-Tests für <see cref="NightWorkComplianceService"/> (Walter-Vorgabe
/// 22.06.2026, ArGV1 Art. 30). Regel: Warnung bei MEHR ALS 18 Nächten in
/// den LETZTEN 6 Wochen (42 Tage bis asOf, Walter 06.09.2026). > 18, NICHT ≥ 18.
/// Die kombinierte Warnung (Regel UND fehlende Nachweise) wird wie in den
/// Controllern zusammengesetzt und mitgetestet.
/// </summary>
public class NightWorkComplianceServiceTests
{
    private static List<DateOnly> Consecutive(DateOnly start, int count)
        => Enumerable.Range(0, count).Select(i => start.AddDays(i)).ToList();

    // Spiegelt die Controller-Logik: Warnfall = > 18 Nächte/6 Wochen UND
    // Nachweise unvollständig (Arztzeugnis/Verzicht UND Ausnahmeregelung).
    private static bool Warn(IEnumerable<DateOnly> dates, DateOnly asOf, bool hasExam, bool hasChecklist)
        => NightWorkComplianceService.Evaluate(dates, asOf).RequiresDocuments && !(hasExam && hasChecklist);

    [Fact]
    public void Achtzehn_Naechte_in_42_Tagen_keine_Warnung()
    {
        var start = new DateOnly(2026, 1, 1);
        var dates = Consecutive(start, 18);                 // 18 aufeinanderfolgende Nacht-Tage
        var r = NightWorkComplianceService.Evaluate(dates, start.AddDays(30));
        Assert.Equal(18, r.MaxNightsInSixWeeks);
        Assert.False(r.RequiresDocuments);                  // 18 ist NICHT > 18
    }

    [Fact]
    public void Neunzehn_Naechte_in_42_Tagen_Warnung()
    {
        var start = new DateOnly(2026, 1, 1);
        var dates = Consecutive(start, 19);                 // 19 Tage, alle innerhalb 42
        var r = NightWorkComplianceService.Evaluate(dates, start.AddDays(30));
        Assert.Equal(19, r.MaxNightsInSixWeeks);
        Assert.True(r.RequiresDocuments);                   // 19 > 18
    }

    [Fact]
    public void Fuenfundzwanzig_uebers_Jahr_aber_nie_mehr_als_18_in_42_Tagen_keine_Warnung()
    {
        // 25 Nächte alle 14 Tage verteilt → in keinem 42-Tage-Fenster mehr als 3.
        var start = new DateOnly(2026, 1, 1);
        var dates = Enumerable.Range(0, 25).Select(i => start.AddDays(i * 14)).ToList();
        var r = NightWorkComplianceService.Evaluate(dates, start.AddDays(400));
        Assert.True(r.MaxNightsInSixWeeks <= 18);
        Assert.False(r.RequiresDocuments);
    }

    [Fact]
    public void Alter_Nachtblock_ausserhalb_der_letzten_6_Wochen_zaehlt_nicht()
    {
        // 23 Nächte im letzten Herbst, seither keine Nachtarbeit → keine Pflicht
        // mehr (Walter 06.09.2026, Fall «23 Nächte / 6 Wochen» ohne Nachtarbeit).
        var start = new DateOnly(2025, 9, 1);
        var dates = Consecutive(start, 23);
        var r = NightWorkComplianceService.Evaluate(dates, new DateOnly(2026, 9, 6));
        Assert.Equal(0, r.MaxNightsInSixWeeks);
        Assert.False(r.RequiresDocuments);
    }

    [Fact]
    public void Gekuendigte_MA_sind_ausgenommen()
    {
        Assert.True(NightWorkComplianceService.Ausgenommen(new DateTime(2026, 12, 31), null));
        Assert.True(NightWorkComplianceService.Ausgenommen(null, new DateTime(2026, 11, 30)));
        Assert.False(NightWorkComplianceService.Ausgenommen(null, null));
    }

    [Fact]
    public void Neunzehn_Naechte_mit_vollstaendigen_Nachweisen_keine_Warnung()
    {
        var start = new DateOnly(2026, 1, 1);
        var dates = Consecutive(start, 19);
        Assert.False(Warn(dates, start.AddDays(30), hasExam: true, hasChecklist: true));
    }

    [Fact]
    public void Neunzehn_Naechte_ohne_Nachweise_Warnung()
    {
        var start = new DateOnly(2026, 1, 1);
        var dates = Consecutive(start, 19);
        Assert.True(Warn(dates, start.AddDays(30), hasExam: false, hasChecklist: false));
        // Auch wenn nur EIN Nachweis fehlt, bleibt es ein Warnfall:
        Assert.True(Warn(dates, start.AddDays(30), hasExam: true, hasChecklist: false));
    }
}
