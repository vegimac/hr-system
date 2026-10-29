namespace HrSystem.Services;

/// <summary>
/// Wer in welche Grenzgängermeldung gehört (Swissdec S03_08).
/// Die Jahresübermittlung selbst ist ein eigener Lauf.
/// </summary>
public static class GrenzgaengerMeldung
{
    public static bool WohnsitzFrankreich(string? land)
    {
        var l = (land ?? "").Trim().ToUpperInvariant();
        return l is "FR" or "FRA" or "FRANKREICH" or "FRANCE";
    }

    public static bool NationalitaetSchweiz(string? code)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return c is "CH" or "CHE" or "SCHWEIZ";
    }

    public static bool ArbeitsortSchweiz(string? land, string? kanton)
    {
        var l = (land ?? "").Trim().ToUpperInvariant();
        if (l is "CH" or "CHE" or "SCHWEIZ") return true;
        if (l.Length > 0 && l is not "CH") return false;
        return (kanton ?? "").Trim().Length == 2;
    }

    /// <summary>
    /// Wohnsitz Frankreich und gewöhnlicher Arbeitsort Schweiz.
    /// Ausgenommen: Schweizer Staatsangehörigkeit bei einem öffentlich-rechtlichen Arbeitgeber.
    /// </summary>
    public static bool Frankreich(string? wohnLand, bool arbeitsortSchweiz, string? nationalitaet, bool oeffentlichRechtlich)
        => WohnsitzFrankreich(wohnLand)
           && arbeitsortSchweiz
           && !(NationalitaetSchweiz(nationalitaet) && oeffentlichRechtlich);

    public static bool Sfn(string? qstCode)
        => string.Equals((qstCode ?? "").Trim(), "SFN", StringComparison.OrdinalIgnoreCase);
}
