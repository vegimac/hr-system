using HrSystem.Models;

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

    public static bool WohnsitzItalien(string? land)
    {
        var l = (land ?? "").Trim().ToUpperInvariant();
        return l is "IT" or "ITA" or "ITALIEN" or "ITALY" or "ITALIA";
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

    /// <summary>
    /// Welche Grenzgängermeldungen diese Person trägt. Italien und SFN vor Frankreich,
    /// gleich wie der Jahres-Builder.
    /// </summary>
    public static IEnumerable<string> Arten(IEnumerable<EmployeeQuellensteuer> versionen, string? mitarbeiterLand, string? nationalitaet)
    {
        var liste = versionen.OrderByDescending(q => q.ValidFrom).ToList();
        if (liste.Count == 0) yield break;
        if (liste.Any(q => q.DatenaustauschItalien)) yield return "grenz-it";
        if (liste.Any(q => Sfn(q.QstCode) || Sfn(q.TarifCode))) yield return "grenz-sfn";
        if (liste.Any(q => q.DatenaustauschItalien || Sfn(q.QstCode) || Sfn(q.TarifCode)))
            yield break;
        var neueste = liste[0];
        if (Frankreich(neueste.Wohnsitzstaat ?? mitarbeiterLand, true, nationalitaet, false)
            || Frankreich(mitarbeiterLand, true, nationalitaet, false))
            yield return "grenz-fr";
    }
}
