namespace HrSystem.Services.Elm;

/// <summary>
/// FAK-Jahresmeldung pro Person (Walter 01.11.2026, ELM 6.0 Richtlinien 8.2.5, Tabelle 8.27).
/// Ein FAK-CAF-Salary je AHV-Abschnitt und FAK-Kasse/Arbeitskanton: Wechselt die Person den
/// Arbeitskanton (bzw. die Filiale mit anderer FAK), beginnt ein neuer Abschnitt.
/// FAK-Lohn = AHV-Lohn des Abschnitts (Freibetrag und «unter 18» zählen nicht, wie in den
/// Swissdec-Referenzen: Lohn in AHV-AVS-Open ⇒ keine FAK-Zeile). Wiederkehrend = Kinder- und
/// Ausbildungszulagen, einmalig = Geburts- und Adoptionszulagen — nur die von der FAK bezahlten
/// Swissdec-Lohnarten 3000–3049, nicht zusätzliche Zulagen des Arbeitgebers.
/// Rein, ohne DB.
/// </summary>
public static class ElmFakJahreslohn
{
    /// <summary>Ein Lohnmonat der Person: AHV-Lohn, ausbezahlte Zulagen, FAK-Kasse und Arbeitskanton.</summary>
    public readonly record struct Monat(int Nr, decimal AhvLohn, decimal Wiederkehrend, decimal Einmalig,
        string? Kasse, string? Kanton);

    public readonly record struct Abschnitt(DateTime Von, DateTime Bis, string Kasse, string Kanton,
        decimal Lohn, decimal Wiederkehrend, decimal Einmalig);

    public enum ZulageArt { Wiederkehrend, Einmalig }

    /// <summary>Swissdec-Lohnart einer FAK-Zulage: 3000–3029 wiederkehrend, 3030–3049 einmalig.</summary>
    public static ZulageArt? Zulage(int lohnart) => lohnart switch
    {
        >= 3000 and <= 3029 => ZulageArt.Wiederkehrend,
        >= 3030 and <= 3049 => ZulageArt.Einmalig,
        _ => null,
    };

    /// <summary>
    /// Zeitraum eines AHV-/FAK-Abschnitts: ganze Monate, ausser Eintritt bzw. Austritt fällt in den
    /// ersten bzw. letzten Monat — dann das genaue Datum (Referenz TF12 Casanova 27.02., TF10 Ganz 30.10.;
    /// Nachzahlung nach Austritt im Juli = ganzer Juli, TF41 Meier Max).
    /// </summary>
    public static (DateTime Von, DateTime Bis) Zeitraum(int jahr, int vonMonat, int bisMonat,
        DateTime? eintritt, DateTime? austritt)
    {
        var von = new DateTime(jahr, vonMonat, 1);
        var bis = new DateTime(jahr, bisMonat, DateTime.DaysInMonth(jahr, bisMonat));
        if (eintritt is DateTime e && e.Year == jahr && e.Month == vonMonat && e.Date > von) von = e.Date;
        if (austritt is DateTime a && a.Year == jahr && a.Month == bisMonat && a.Date < bis && a.Date >= von) bis = a.Date;
        return (von, bis);
    }

    /// <summary>
    /// FAK-Abschnitte innerhalb eines AHV-Abschnitts (Monate <paramref name="vonMonat"/>–<paramref name="bisMonat"/>).
    /// Aufeinanderfolgende Monate mit derselben Kasse und demselben Kanton bilden einen Abschnitt.
    /// Monate ohne Kasse fehlen (Meldung hängt dann eine Warnung an).
    /// </summary>
    public static List<Abschnitt> Abschnitte(int jahr, int vonMonat, int bisMonat,
        IReadOnlyList<Monat> monate, DateTime? eintritt, DateTime? austritt, bool ueberLuecken = false)
    {
        var liste = monate.Where(m => m.Nr >= vonMonat && m.Nr <= bisMonat && !string.IsNullOrWhiteSpace(m.Kasse))
            .OrderBy(m => m.Nr).ToList();
        var result = new List<Abschnitt>();
        var i = 0;
        while (i < liste.Count)
        {
            var kasse = liste[i].Kasse!;
            var kanton = liste[i].Kanton ?? "";
            var j = i;
            while (j + 1 < liste.Count
                   && (ueberLuecken || liste[j + 1].Nr == liste[j].Nr + 1)
                   && string.Equals(liste[j + 1].Kasse, kasse, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(liste[j + 1].Kanton ?? "", kanton, StringComparison.OrdinalIgnoreCase))
                j++;

            decimal lohn = 0, wieder = 0, einmal = 0;
            for (var k = i; k <= j; k++)
            {
                lohn += liste[k].AhvLohn;
                wieder += liste[k].Wiederkehrend;
                einmal += liste[k].Einmalig;
            }
            // Erster/letzter Teil folgen dem AHV-Abschnitt (genaues Ein-/Austrittsdatum), Teile dazwischen
            // beginnen am Monatsersten bzw. enden am Monatsletzten.
            var (von, bis) = Zeitraum(jahr, liste[i].Nr, liste[j].Nr,
                liste[i].Nr == vonMonat ? eintritt : null,
                liste[j].Nr == bisMonat ? austritt : null);
            if (lohn != 0 || wieder != 0 || einmal != 0)
                result.Add(new Abschnitt(von, bis, kasse, kanton, lohn, wieder, einmal));
            i = j + 1;
        }
        return result;
    }
}
