using System.Linq;
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

    /// <summary>
    /// UID-BFS (CHE-XXX.XXX.XXX): Format und Prüfziffer nach BFS/eCH-0097.
    /// Gewichte 5-4-3-2-7-6-5-4 auf die acht Ziffern vor der Prüfziffer;
    /// Rest 1 → ungültig, Rest 0 → Prüfziffer 0, sonst 11 − Rest.
    /// Beleg: Muster-AG «CHE-999.999.996» ist gültig, «CHE-999.999.997» nicht.
    /// </summary>
    public static bool UidBfsGueltig(string? uid)
    {
        var u = (uid ?? "").Trim().ToUpperInvariant();
        if (!Regex.IsMatch(u, @"^CHE-\d{3}\.\d{3}\.\d{3}$")) return false;
        var z = new string(u.Where(char.IsDigit).ToArray());
        int[] gewicht = { 5, 4, 3, 2, 7, 6, 5, 4 };
        var summe = 0;
        for (int i = 0; i < 8; i++) summe += (z[i] - '0') * gewicht[i];
        var rest = summe % 11;
        if (rest == 1) return false;
        var pruef = rest == 0 ? 0 : 11 - rest;
        return pruef == (z[8] - '0');
    }

    /// <summary>
    /// Fehlermeldung zur UID, oder null wenn leer/gültig.
    /// Leer ist erlaubt (noch nicht erfasst); «unbekannt» kommt später als eigener Wert.
    /// </summary>
    public static string? UidBfsFehler(string? uid)
    {
        if (string.IsNullOrWhiteSpace(uid)) return null;
        var u = uid.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(u, @"^CHE-\d{3}\.\d{3}\.\d{3}$"))
            return "UID bitte im Format CHE-XXX.XXX.XXX erfassen.";
        if (!UidBfsGueltig(u))
            return $"UID «{u}» ist ungültig (Prüfziffer stimmt nicht).";
        return null;
    }

    /// <summary>Fehlermeldung zur BUR-/REE-Nummer, oder null wenn leer/gültig.</summary>
    public static string? BurNummerFehler(string? bur)
    {
        if (string.IsNullOrWhiteSpace(bur)) return null;
        if (BurNummerGueltig(bur)) return null;
        var b = bur.Trim().ToUpperInvariant();
        if (BurFormatOkPruefzifferFalsch(b))
            return $"BUR-Nummer «{b}» ist ungültig (Prüfziffer stimmt nicht).";
        return $"BUR-Nummer «{b}» ist ungültig (Format: ein Buchstabe, acht Ziffern, z.B. A89058593).";
    }
}
