using System.Text.RegularExpressions;

namespace HrSystem.Services.Elm;

/// <summary>
/// Prüft die Betriebs-Stammdaten, die eine ELM-Meldung braucht (Walter 27.09.2026).
/// Anlass: die Muster-Filiale ZG trägt in den Testdaten PLZ 6003 (Luzern) und eine
/// BUR-Nummer mit falscher Prüfziffer — die Referenz erwartet 6300 und A38197423.
/// Swissdec schickt so etwas zurück; OneCrew soll es vorher sagen.
/// </summary>
public static class ElmStammdatenPruefung
{
    /// <summary>
    /// BUR-/REE-Nummer: ein Grossbuchstabe und acht Ziffern, wobei die letzte Ziffer
    /// eine Prüfziffer nach dem Modulo-11-Verfahren des BFS ist (Gewichte 5, 4, 3, 2,
    /// 7, 6, 5 auf die sieben Stellen davor; Rest 0 → Prüfziffer 0, Rest 1 → ungültig).
    /// </summary>
    public static bool BurNummerGueltig(string? bur)
    {
        var b = (bur ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(b, "^[A-Z][0-9]{8}$")) return false;
        int[] gewicht = { 5, 4, 3, 2, 7, 6, 5 };
        var summe = 0;
        for (int i = 0; i < 7; i++) summe += (b[i + 1] - '0') * gewicht[i];
        var rest = summe % 11;
        if (rest == 1) return false;                 // laut BFS keine gültige Nummer
        var pruef = rest == 0 ? 0 : 11 - rest;
        return pruef == (b[8] - '0');
    }

    /// <summary>Formal richtig aufgebaut, aber mit falscher Prüfziffer?</summary>
    public static bool BurFormatOkPruefzifferFalsch(string? bur)
        => Regex.IsMatch((bur ?? "").Trim().ToUpperInvariant(), "^[A-Z][0-9]{8}$") && !BurNummerGueltig(bur);
}
