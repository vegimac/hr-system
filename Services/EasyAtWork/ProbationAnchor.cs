namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Gemeinsame Probezeit-Anker-Rechnung (Walter 29.06.2026, präzisiert 02.08.2026):
/// die Probezeit läuft ab dem tatsächlichen 1. Arbeitstag = erste Stempelzeit
/// ab Eintrittsdatum (nicht ab einem späteren Vertrags-Split-Beginn).
/// Fehlt die Stempelzeit noch, ist die provisorische Basis das Eintrittsdatum
/// (Fallback: Vertragsbeginn). Sobald die erste Stempelzeit da ist, wird das
/// Ende um (erste Stempelzeit − Referenz) verschoben — Länge bleibt erhalten.
/// </summary>
public static class ProbationAnchor
{
    /// <summary>Verschiebung in Tagen (negativ = vorgezogen, positiv = verschoben).</summary>
    public static int Delta(DateOnly referenceStart, DateOnly firstStamp)
        => firstStamp.DayNumber - referenceStart.DayNumber;

    /// <summary>
    /// Referenz für provisorische Probezeit / Delta: Eintritt, sonst Vertragsbeginn.
    /// </summary>
    public static DateOnly ReferenceStart(DateOnly? entryDate, DateOnly contractStart)
        => entryDate ?? contractStart;

    /// <summary>
    /// Probezeit-Ende: Basis + Filialdauer − 1 Tag (14 = Tage, sonst Monate).
    /// </summary>
    public static DateOnly ComputeEnd(DateOnly basis, int branchProbationMonths)
        => branchProbationMonths == 14
            ? basis.AddDays(14).AddDays(-1)
            : basis.AddMonths(branchProbationMonths).AddDays(-1);

    /// <summary>Klartext-Grund für die History-Zeile.</summary>
    public static string Grund(DateOnly referenceStart, DateOnly firstStamp)
    {
        var delta = Delta(referenceStart, firstStamp);
        if (delta == 0)
            return $"Eintritt/Beginn = 1. Arbeitstag (erste Stempelzeit {firstStamp:dd.MM.yyyy})";
        if (delta < 0)
            return $"Beginn > 1. Arbeitstag — Probezeit um {-delta} Tag(e) vorgezogen (erste Stempelzeit {firstStamp:dd.MM.yyyy})";
        return $"Beginn < 1. Arbeitstag — Probezeit um {delta} Tag(e) verschoben (erste Stempelzeit {firstStamp:dd.MM.yyyy})";
    }
}
