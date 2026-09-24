using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Ab wann die Dienstjahre zählen (Walter-Vorgabe 24.09.2026, präzisiert gegenüber
/// dem ersten Entwurf vom selben Tag).
///
/// **Die Regel, im Wortlaut:** Kündigt ein Mitarbeitender in einer Filiale und kommt
/// später zurück, gibt es ein NEUES Eintrittsdatum — alle Fristen laufen ab dann,
/// auch das Arbeitsjahr für die Krankenversicherung, und es gibt eine neue Probezeit
/// wie bei einem ganz frischen Mitarbeitenden. Nur beim **nahtlosen Übertritt** von
/// Filiale A nach B zählt das alte Eintrittsdatum weiter. Jede Filiale ist eine eigene
/// Rechtseinheit mit eigenem HR-Eintrag und eigener Versicherung.
///
/// Das ist also kein Ermessensentscheid, sondern eine Rechnung: Wir gehen von der
/// heutigen Anstellung rückwärts durch die Vertragskette, solange ein Abschnitt
/// LÜCKENLOS an den nächsten anschliesst. Wo eine Lücke ist, hört die Kette auf.
///
/// «Nahtlos» heisst hier: der neue Abschnitt beginnt spätestens am Tag nach dem Ende
/// des vorherigen. Bewusst streng und ohne Toleranz — wer eine Ausnahme braucht
/// (z.B. ein Wochenende dazwischen, das niemand als Unterbruch versteht), trägt sie
/// von Hand im Feld «Dienstalter seit» ein. Diese Handeingabe sticht die Rechnung
/// immer.
/// </summary>
public static class Dienstalter
{
    /// <summary>
    /// Massgebendes Datum für alles, was nach Dienstjahren staffelt (Lohnfortzahlung,
    /// Karenz, Sperrfrist, Ferienkürzung). NULL = weder Eintritt noch Verträge bekannt.
    /// </summary>
    /// <param name="stichtag">Tag, für den gerechnet wird — die Kette wird von der
    /// an diesem Tag geltenden Anstellung aus rückwärts verfolgt.</param>
    public static DateOnly? Massgebend(Employee employee, IEnumerable<Employment>? abschnitte,
                                       DateOnly stichtag)
    {
        // 1) Handeingabe sticht alles.
        if (employee.DienstalterSeit.HasValue)
            return DateOnly.FromDateTime(employee.DienstalterSeit.Value);

        var eintritt = employee.EntryDate.HasValue
            ? DateOnly.FromDateTime(employee.EntryDate.Value)
            : (DateOnly?)null;

        var kettenStart = NahtloseKetteStart(abschnitte, stichtag);

        // 2) Nur ein nahtloser VORGÄNGER zieht das Datum zurück. Gibt es keinen,
        //    bleibt der Eintritt stehen — auch wenn ältere Verträge mit Lücke
        //    existieren (das ist dann ein Wiedereintritt, kein Übertritt).
        if (kettenStart.HasValue && (!eintritt.HasValue || kettenStart.Value < eintritt.Value))
            return kettenStart;

        return eintritt;
    }

    /// <summary>
    /// Beginn der lückenlosen Vertragskette, die am Stichtag gilt — aber NUR, wenn
    /// mindestens ein nahtloser Vorgänger dazugehört. Ein einzelner Abschnitt ergibt
    /// NULL: dort sagt der Eintritt die Wahrheit, nicht der Vertragsbeginn.
    /// </summary>
    public static DateOnly? NahtloseKetteStart(IEnumerable<Employment>? abschnitte, DateOnly stichtag)
    {
        if (abschnitte == null) return null;
        var liste = abschnitte
            .Select(e => (
                Start: DateOnly.FromDateTime(e.ContractStartDate),
                Ende: e.ContractEndDate.HasValue ? DateOnly.FromDateTime(e.ContractEndDate.Value) : (DateOnly?)null))
            .OrderBy(e => e.Start)
            .ToList();
        if (liste.Count == 0) return null;

        // Aktueller Abschnitt: der letzte, der am Stichtag begonnen hat — sonst der
        // früheste überhaupt (künftiger Eintritt, noch nichts angefangen).
        var idx = liste.FindLastIndex(e => e.Start <= stichtag);
        if (idx < 0) idx = 0;

        var start = liste[idx].Start;
        bool verschoben = false;

        // Rückwärts: schliesst ein früherer Abschnitt lückenlos an?
        for (var i = idx - 1; i >= 0; i--)
        {
            var vor = liste[i];
            if (!vor.Ende.HasValue) { start = vor.Start; verschoben = true; continue; }  // offener Vorgänger
            if (vor.Ende.Value.AddDays(1) < start) break;                                // Lücke → Kette endet
            if (vor.Start < start) { start = vor.Start; verschoben = true; }
        }

        return verschoben ? start : null;
    }

    /// <summary>
    /// Grösste Lücke zwischen zwei aufeinanderfolgenden Abschnitten, in Tagen.
    /// Für die Anzeige: sie erklärt, warum die Kette abbricht. NULL = keine Lücke.
    /// </summary>
    public static int? GroessteLueckeTage(IEnumerable<Employment>? abschnitte)
    {
        if (abschnitte == null) return null;
        var liste = abschnitte.OrderBy(e => e.ContractStartDate).ToList();
        int? groesste = null;
        for (var i = 1; i < liste.Count; i++)
        {
            var vorEnde = liste[i - 1].ContractEndDate;
            if (!vorEnde.HasValue) continue;
            var tage = (int)(liste[i].ContractStartDate.Date - vorEnde.Value.Date).TotalDays - 1;
            if (tage > 0 && (groesste == null || tage > groesste)) groesste = tage;
        }
        return groesste;
    }
}
