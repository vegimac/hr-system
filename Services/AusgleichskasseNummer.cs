namespace HrSystem.Services;

/// <summary>
/// Nummer der Ausgleichskasse (Swissdec 8.1.3) und der Familienausgleichskasse (8.2.3).
/// Format zum Beispiel 003.000. Der erste Block muss grösser als 0 sein. «000.003» ist ungültig.
/// </summary>
public static class AusgleichskasseNummer
{
    public const string BegriffAhv = "Nummer der Ausgleichskasse";
    public const string BegriffFak = "Nummer der Familienausgleichskasse";

    public const string Meldung =
        "Nummer der Ausgleichskasse: der erste Block muss grösser als 0 sein (nicht «000.003»).";

    public static string MeldungZu(string begriff) =>
        begriff + ": der erste Block muss grösser als 0 sein (nicht «000.003»).";

    /// <summary>Leer ist noch keine Nummer. Sonst muss der erste Block grösser als 0 sein.</summary>
    public static string? Fehler(string? nummer, string? begriff = null)
    {
        if (string.IsNullOrWhiteSpace(nummer)) return null;
        var erster = nummer.Trim().Split('.')[0];
        if (!int.TryParse(erster, out var wert) || wert <= 0 || erster.Any(c => !char.IsDigit(c)))
            return MeldungZu(string.IsNullOrWhiteSpace(begriff) ? BegriffAhv : begriff);
        return null;
    }
}
