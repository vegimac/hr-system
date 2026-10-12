namespace HrSystem.Services;

/// <summary>
/// Freie Sonntage pro Kalenderjahr (Walter-Vorgabe 12.10.2026).
/// Soll: 12 pro Jahr, solange «Familienbetreuung Ja» mit Beginn gilt, sonst 4 —
/// anteilig nach Tagen der Anstellung im Zählzeitraum, kaufmännisch gerundet.
/// Frei = an diesem Sonntag kein Stempel (Schichtbeginn = Kalendertag) und keine
/// Absenz irgendeiner Art. Gezählt wird erst ab <c>datenAb</c> (erster Monat mit
/// Stempelzeiten), davor wären Sonntage fälschlich «frei».
/// </summary>
public static class SonntagKontrolle
{
    public const decimal ProJahr = 4m;
    public const decimal ProJahrFamilie = 12m;
    /// <summary>Weniger Spielraum (verbleibende − fehlende Sonntage) als das ⇒ «knapp».</summary>
    public const int SpielraumKnapp = 4;

    public record Eingabe(
        int Jahr,
        DateOnly Stichtag,
        DateOnly? DatenAb,
        IReadOnlyList<(DateOnly Von, DateOnly? Bis)> Anstellungen,
        (DateOnly Von, DateOnly? Bis)? Familienbetreuung,
        IReadOnlySet<DateOnly> GestempelteTage,
        IReadOnlyList<(DateOnly Von, DateOnly Bis)> Absenzen);

    public record Ergebnis(
        string Status,              // ok | offen | knapp | rot | keine_daten
        int Soll,
        decimal SollGenau,
        bool MitFamilie,            // mind. ein Tag mit 12er-Satz
        DateOnly? ZaehlAb,
        int FreiBisher,
        int GearbeitetBisher,       // Sonntage mit Stempel oder Absenz
        int Fehlend,
        int Verbleibend);           // künftige Sonntage ohne geplante Absenz

    public static Ergebnis Berechne(Eingabe e)
    {
        var jahrVon = new DateOnly(e.Jahr, 1, 1);
        var jahrBis = new DateOnly(e.Jahr, 12, 31);
        if (e.DatenAb == null || e.DatenAb > jahrBis)
            return new Ergebnis("keine_daten", 0, 0, false, null, 0, 0, 0, 0);

        var ab = e.DatenAb.Value > jahrVon ? e.DatenAb.Value : jahrVon;
        int tageImJahr = DateTime.IsLeapYear(e.Jahr) ? 366 : 365;
        bool Angestellt(DateOnly d) => e.Anstellungen.Any(a => a.Von <= d && (a.Bis == null || d <= a.Bis));
        bool Familie(DateOnly d) => e.Familienbetreuung is { } f && f.Von <= d && (f.Bis == null || d <= f.Bis);
        bool Abwesend(DateOnly d) => e.Absenzen.Any(a => a.Von <= d && d <= a.Bis);

        decimal soll = 0;
        bool mitFamilie = false;
        int frei = 0, gearbeitet = 0, verbleibend = 0;
        for (var d = ab; d <= jahrBis; d = d.AddDays(1))
        {
            if (!Angestellt(d)) continue;
            bool fam = Familie(d);
            mitFamilie |= fam;
            soll += (fam ? ProJahrFamilie : ProJahr) / tageImJahr;
            if (d.DayOfWeek != DayOfWeek.Sunday) continue;
            if (d <= e.Stichtag)
            {
                if (e.GestempelteTage.Contains(d) || Abwesend(d)) gearbeitet++;
                else frei++;
            }
            else if (!Abwesend(d)) verbleibend++;
        }

        int sollRund = (int)Math.Round(soll, MidpointRounding.AwayFromZero);
        int fehlend = Math.Max(0, sollRund - frei);
        string status = fehlend == 0 ? "ok"
            : fehlend > verbleibend ? "rot"
            : verbleibend - fehlend < SpielraumKnapp ? "knapp"
            : "offen";
        return new Ergebnis(status, sollRund, Math.Round(soll, 2), mitFamilie, ab, frei, gearbeitet, fehlend, verbleibend);
    }
}
