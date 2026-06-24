using System;
using System.Collections.Generic;
using System.Linq;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Unit-Tests für <see cref="NightWorkComplianceService"/> (Walter-Vorgabe
/// 22.06.2026, ArGV1 Art. 30). Neue Regel: Warnung bei MEHR ALS 18 Nächten in
/// einem rollierenden 6-Wochen-Fenster (42 Tage). > 18, NICHT ≥ 18.
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
        var r = NightWorkComplianceService.Evaluate(dates, start.AddDays(60));
        Assert.Equal(18, r.MaxNightsInSixWeeks);
        Assert.False(r.RequiresDocuments);                  // 18 ist NICHT > 18
    }

    [Fact]
    public void Neunzehn_Naechte_in_42_Tagen_Warnung()
    {
        var start = new DateOnly(2026, 1, 1);
        var dates = Consecutive(start, 19);                 // 19 Tage, alle innerhalb 42
        var r = NightWorkComplianceService.Evaluate(dates, start.AddDays(60));
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
    public void Neunzehn_Naechte_mit_vollstaendigen_Nachweisen_keine_Warnung()
    {
        var start = new DateOnly(2026, 1, 1);
        var dates = Consecutive(start, 19);
        Assert.False(Warn(dates, start.AddDays(60), hasExam: true, hasChecklist: true));
    }

    [Fact]
    public void Neunzehn_Naechte_ohne_Nachweise_Warnung()
    {
        var start = new DateOnly(2026, 1, 1);
        var dates = Consecutive(start, 19);
        Assert.True(Warn(dates, start.AddDays(60), hasExam: false, hasChecklist: false));
        // Auch wenn nur EIN Nachweis fehlt, bleibt es ein Warnfall:
        Assert.True(Warn(dates, start.AddDays(60), hasExam: true, hasChecklist: false));
    }
}
