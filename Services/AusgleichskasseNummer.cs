namespace HrSystem.Services;

/// <summary>
/// Nummer der Ausgleichskasse (Swissdec 8.1.3). Format zum Beispiel 003.000.
/// Der erste Block muss grösser als 0 sein. «000.003» ist ungültig.
/// </summary>
public static class AusgleichskasseNummer
{
    public const string Meldung =
        "Nummer der Ausgleichskasse: der erste Block muss grösser als 0 sein (nicht «000.003»).";

    /// <summary>Leer ist noch keine Nummer. Sonst muss der erste Block grösser als 0 sein.</summary>
    public static string? Fehler(string? nummer)
    {
        if (string.IsNullOrWhiteSpace(nummer)) return null;
        var erster = nummer.Trim().Split('.')[0];
        if (!int.TryParse(erster, out var wert) || wert <= 0 || erster.Any(c => !char.IsDigit(c)))
            return Meldung;
        return null;
    }
}
