using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter 02.08.2026: Dienstplan-Import darf Korrekturen (gleicher Typ,
/// geänderter Zeitraum/Stunden) nicht als harte Überlappung blockieren.
/// </summary>
public class RosterAbsenceImportLogicTests
{
    private static RosterAbsenceImportLogic.ExistingAbs Abs(
        int id, string type, string from, string to, decimal hours = 0m)
        => new(id, type, DateOnly.Parse(from), DateOnly.Parse(to), hours);

    [Fact]
    public void None_when_no_overlap()
    {
        var existing = new[] { Abs(1, "FERIEN", "2026-07-01", "2026-07-07") };
        var r = RosterAbsenceImportLogic.Classify(
            "FERIEN", DateOnly.Parse("2026-07-25"), DateOnly.Parse("2026-07-31"), 0m, existing);
        Assert.Equal(RosterAbsenceImportLogic.OverlapKind.None, r.Kind);
    }

    [Fact]
    public void ExactDuplicate_when_same_type_dates_and_hours()
    {
        var existing = new[] { Abs(1, "FERIEN", "2026-07-25", "2026-07-31", 0m) };
        var r = RosterAbsenceImportLogic.Classify(
            "FERIEN", DateOnly.Parse("2026-07-25"), DateOnly.Parse("2026-07-31"), 0m, existing);
        Assert.Equal(RosterAbsenceImportLogic.OverlapKind.ExactDuplicate, r.Kind);
        Assert.Equal(1, r.Primary!.Id);
    }

    [Fact]
    public void Correction_when_same_type_but_dates_changed()
    {
        // Bestehend 20.–31., Import 25.–31. → Korrektur (nicht blockieren)
        var existing = new[] { Abs(1, "FERIEN", "2026-07-20", "2026-07-31", 0m) };
        var r = RosterAbsenceImportLogic.Classify(
            "FERIEN", DateOnly.Parse("2026-07-25"), DateOnly.Parse("2026-07-31"), 0m, existing);
        Assert.Equal(RosterAbsenceImportLogic.OverlapKind.Correction, r.Kind);
        Assert.Equal(1, r.Primary!.Id);
    }

    [Fact]
    public void Correction_when_same_dates_but_hours_changed()
    {
        var existing = new[] { Abs(1, "KRANK", "2026-07-25", "2026-07-31", 40m) };
        var r = RosterAbsenceImportLogic.Classify(
            "KRANK", DateOnly.Parse("2026-07-25"), DateOnly.Parse("2026-07-31"), 32m, existing);
        Assert.Equal(RosterAbsenceImportLogic.OverlapKind.Correction, r.Kind);
    }

    [Fact]
    public void Correction_merges_multiple_same_type_overlaps()
    {
        var existing = new[]
        {
            Abs(1, "FERIEN", "2026-07-20", "2026-07-22", 0m),
            Abs(2, "FERIEN", "2026-07-28", "2026-07-31", 0m),
        };
        var r = RosterAbsenceImportLogic.Classify(
            "FERIEN", DateOnly.Parse("2026-07-20"), DateOnly.Parse("2026-07-31"), 0m, existing);
        Assert.Equal(RosterAbsenceImportLogic.OverlapKind.Correction, r.Kind);
        Assert.Equal(2, r.SameTypeOverlaps.Count);
        Assert.Equal(1, r.Primary!.Id);
    }

    [Fact]
    public void TypeConflict_when_other_type_on_same_days()
    {
        var existing = new[] { Abs(1, "KRANK", "2026-07-25", "2026-07-28", 24m) };
        var r = RosterAbsenceImportLogic.Classify(
            "FERIEN", DateOnly.Parse("2026-07-25"), DateOnly.Parse("2026-07-31"), 0m, existing);
        Assert.Equal(RosterAbsenceImportLogic.OverlapKind.TypeConflict, r.Kind);
        Assert.Equal("KRANK", r.ConflictWith!.AbsenceType);
    }
}
