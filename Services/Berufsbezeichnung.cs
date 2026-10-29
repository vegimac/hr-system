namespace HrSystem.Services;

/// <summary>
/// «Im Unternehmen ausgeübter Beruf» ist die genaue Bezeichnung.
/// Zahlen und Sonderzeichen gehören nicht in dieses Feld (Swissdec S03_02_05).
/// Bindestrich und Apostroph bleiben, sie gehören zum Wort (Sous-Chef, d'étage).
/// Die Funktionsgruppe bleibt ein eigenes Feld.
/// </summary>
public static class Berufsbezeichnung
{
    public static bool IstGesperrt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var c in text)
        {
            if (char.IsLetter(c) || char.IsWhiteSpace(c)) continue;
            if (c is '-' or '\'' or '’' or '´') continue;
            return true;
        }
        return false;
    }

    public const string Meldung =
        "Im Unternehmen ausgeübter Beruf ist die genaue Berufsbezeichnung, ohne Zahlen oder Sonderzeichen.";
}
