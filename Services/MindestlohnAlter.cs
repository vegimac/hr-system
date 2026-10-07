namespace HrSystem.Services;

/// <summary>
/// Alter für altersabhängige Mindestlohn-Regeln (L-GAV-Jugendlohn, <c>age_max</c>).
/// Der tiefere Jugendlohn gilt bis zum vollendeten 18. Lebensjahr. Weil der Lohn pro
/// Monat läuft, zählt das Alter am LETZTEN Tag des Stichtag-Monats: wer im Juli 18 wird,
/// braucht ab dem 1. Juli den Erwachsenen-Mindestlohn (Walter 07.10.2026).
/// Eine Stelle für Vertrags-Check, Live-Check, Dashboard und Lohn-Check.
/// </summary>
public static class MindestlohnAlter
{
    public static int? AlterFuerRegel(DateTime? geburt, DateTime stichtag)
    {
        if (geburt is not DateTime bd) return null;
        var monatsende = new DateTime(stichtag.Year, stichtag.Month, DateTime.DaysInMonth(stichtag.Year, stichtag.Month));
        return Alter(bd, monatsende);
    }

    public static int? AlterFuerRegel(DateTime? geburt, DateOnly stichtag)
        => AlterFuerRegel(geburt, stichtag.ToDateTime(TimeOnly.MinValue));

    /// <summary>Vollendete Jahre am Tag. Geburtstag 29. Februar zählt in Nicht-Schaltjahren am 28. (Art. 77 OR).</summary>
    public static int Alter(DateTime geburt, DateTime tag)
    {
        int gebTag = geburt.Day;
        if (geburt.Month == 2 && gebTag == 29 && !DateTime.IsLeapYear(tag.Year)) gebTag = 28;
        int a = tag.Year - geburt.Year;
        if (tag.Month < geburt.Month || (tag.Month == geburt.Month && tag.Day < gebTag)) a--;
        return a;
    }
}
