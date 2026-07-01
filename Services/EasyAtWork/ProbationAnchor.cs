namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Gemeinsame Probezeit-Anker-Rechnung (Walter 29.06.2026), damit der
/// Mitarbeiter-Import UND der Stempel-Sync IDENTISCH rechnen: die Probezeit
/// läuft ab dem tatsächlichen 1. Arbeitstag (= erste Stempelzeit), nicht ab
/// Vertragsbeginn. Das Ende wird um (erste Stempelzeit − Vertragsbeginn)
/// verschoben — egal welche Probezeit-Länge, die Länge bleibt erhalten.
/// </summary>
public static class ProbationAnchor
{
    /// <summary>Verschiebung in Tagen (negativ = vorgezogen, positiv = verschoben).</summary>
    public static int Delta(DateOnly contractStart, DateOnly firstStamp)
        => firstStamp.DayNumber - contractStart.DayNumber;

    /// <summary>Klartext-Grund für die History-Zeile.</summary>
    public static string Grund(DateOnly contractStart, DateOnly firstStamp)
    {
        var delta = Delta(contractStart, firstStamp);
        if (delta == 0)
            return $"Vertragsbeginn = 1. Arbeitstag (erste Stempelzeit {firstStamp:dd.MM.yyyy})";
        if (delta < 0)
            return $"Vertragsbeginn > 1. Arbeitstag — Probezeit um {-delta} Tag(e) vorgezogen (erste Stempelzeit {firstStamp:dd.MM.yyyy})";
        return $"Vertragsbeginn < 1. Arbeitstag — Probezeit um {delta} Tag(e) verschoben (erste Stempelzeit {firstStamp:dd.MM.yyyy})";
    }
}
