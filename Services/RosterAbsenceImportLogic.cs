namespace HrSystem.Services;

/// <summary>
/// Klassifikation von Überlappungen beim Dienstplan-Absenzen-Import.
/// Walter 02.08.2026: gleiche Absenz-Art mit geänderten Grenzen/Stunden
/// = Korrektur (ersetzen), nicht hart blockieren. Anderer Typ auf denselben
/// Tagen bleibt gesperrt («pro Tag nur eine Absenz»).
/// </summary>
public static class RosterAbsenceImportLogic
{
    public enum OverlapKind
    {
        /// <summary>Keine Überlappung mit bestehender Absenz.</summary>
        None,
        /// <summary>Gleicher Typ, gleicher Zeitraum, gleiche Stunden — idempotent überspringen.</summary>
        ExactDuplicate,
        /// <summary>Gleicher Typ, überlappend, aber Zeitraum/Stunden anders — bestehende ersetzen.</summary>
        Correction,
        /// <summary>Anderer Typ belegt bereits (Teil der) Tage — hart blockieren.</summary>
        TypeConflict,
    }

    public sealed record ExistingAbs(
        int Id,
        string AbsenceType,
        DateOnly DateFrom,
        DateOnly DateTo,
        decimal HoursCredited);

    public sealed record ClassifyResult(
        OverlapKind Kind,
        ExistingAbs? Primary,
        IReadOnlyList<ExistingAbs> SameTypeOverlaps,
        ExistingAbs? ConflictWith);

    public static ClassifyResult Classify(
        string importType,
        DateOnly dateFrom,
        DateOnly dateTo,
        decimal hoursCredited,
        IEnumerable<ExistingAbs> existingForEmployee)
    {
        var overlaps = existingForEmployee
            .Where(a => a.DateFrom <= dateTo && a.DateTo >= dateFrom)
            .OrderBy(a => a.DateFrom)
            .ThenBy(a => a.Id)
            .ToList();

        if (overlaps.Count == 0)
            return new ClassifyResult(OverlapKind.None, null, Array.Empty<ExistingAbs>(), null);

        var other = overlaps.FirstOrDefault(a =>
            !string.Equals(a.AbsenceType, importType, StringComparison.OrdinalIgnoreCase));
        if (other != null)
            return new ClassifyResult(OverlapKind.TypeConflict, null, Array.Empty<ExistingAbs>(), other);

        var sameType = overlaps; // alle gleichen Typs
        var exact = sameType.FirstOrDefault(a => a.DateFrom == dateFrom && a.DateTo == dateTo);
        if (exact != null && HoursEqual(exact.HoursCredited, hoursCredited) && sameType.Count == 1)
            return new ClassifyResult(OverlapKind.ExactDuplicate, exact, sameType, null);

        var primary = exact ?? sameType[0];
        return new ClassifyResult(OverlapKind.Correction, primary, sameType, null);
    }

    public static bool HoursEqual(decimal a, decimal b) => Math.Abs(a - b) < 0.01m;
}
