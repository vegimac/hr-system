namespace HrSystem.Services;

/// <summary>
/// Ein neuer Code derselben Art beendet den bisherigen am Vortag
/// (EmployeeVersicherungCodeController.Create). Löschen macht das rückgängig:
/// der Vorgänger übernimmt das Ende des gelöschten Eintrags (leer = wieder offen).
/// </summary>
public static class VersicherungNachfolge
{
    public static bool WurdeDurchNachfolgerBeendet(DateOnly? vorgaengerBis, DateOnly nachfolgerAb)
        => vorgaengerBis == nachfolgerAb.AddDays(-1);

    /// <summary>AHV schliesst nur denselben Code. Die übrigen Arten schliessen die ganze Art.</summary>
    public static bool GleicherSchluss(string art, string? codeA, string? codeB)
        => !string.Equals(art, "AHV", StringComparison.OrdinalIgnoreCase)
           || string.Equals(codeA, codeB, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Ende des Vorgängers nach dem Löschen. Ein späterer Eintrag bleibt die Grenze,
    /// damit zwei Zeilen nicht übereinander liegen.
    /// </summary>
    public static DateOnly? EndeNachLoeschen(DateOnly? endeDesGeloeschten, DateOnly? naechsterBeginn)
    {
        var ende = endeDesGeloeschten;
        if (naechsterBeginn == null) return ende;
        var bisVorNaechstem = naechsterBeginn.Value.AddDays(-1);
        if (ende == null || ende > bisVorNaechstem) return bisVorNaechstem;
        return ende;
    }

    /// <summary>Wieder öffnen (ohne Ende) scheitert, sobald eine andere Zeile in die Zeit ab «von» reicht.</summary>
    public static bool BlockiertWiederOeffnen(DateOnly von, DateOnly? andererBis)
        => andererBis == null || andererBis >= von;
}
