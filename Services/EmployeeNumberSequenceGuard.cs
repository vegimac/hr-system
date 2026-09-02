using System.Globalization;
using System.Text.RegularExpressions;

namespace HrSystem.Services;

/// <summary>
/// Harte Personalnummern-Folge beim Neuzugang (Walter-Vorgabe 03.08.2026):
/// Ausgewählte NEW-Nummern müssen sich an die höchste bestehende Nummer der
/// Filiale (Präfix) und untereinander lückenlos anschliessen:
/// <c>max+1 … max+N</c>. Admin-Massen-Sync ist davon nicht betroffen.
/// </summary>
public static class EmployeeNumberSequenceGuard
{
    /// <summary>
    /// Prüft die NEW-Nummern eines Neuzugang-Imports.
    /// <paramref name="maxExisting"/> = höchste rein numerische Nummer mit
    /// Filial-Präfix in OneCrew; null = noch keine (nur untereinander fortlaufend).
    /// </summary>
    public static bool TryValidate(
        IReadOnlyList<string> newNumbers,
        long? maxExisting,
        out string message,
        out IReadOnlyList<long> expected,
        out IReadOnlyList<long> received)
    {
        expected = Array.Empty<long>();
        received = Array.Empty<long>();
        message = "";

        if (newNumbers == null || newNumbers.Count == 0)
            return true; // keine NEW → Regel greift nicht

        var parsed = new List<long>(newNumbers.Count);
        foreach (var raw in newNumbers)
        {
            var n = (raw ?? "").Trim();
            if (!Regex.IsMatch(n, @"^\d+$") || !long.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            {
                message = $"Personalnummer «{n}» ist nicht rein numerisch — Import gesperrt.";
                return false;
            }
            parsed.Add(v);
        }

        if (parsed.Count != parsed.Distinct().Count())
        {
            message = "Doppelte Personalnummern in der Auswahl — Import gesperrt.";
            received = parsed.OrderBy(x => x).ToList();
            return false;
        }

        parsed.Sort();
        received = parsed;

        List<long> exp;
        if (maxExisting is long max)
        {
            exp = new List<long>(parsed.Count);
            for (var i = 1; i <= parsed.Count; i++)
                exp.Add(max + i);
        }
        else
        {
            // Noch keine Nummer mit Präfix: nur untereinander lückenlos.
            exp = new List<long>(parsed.Count);
            for (var i = 0; i < parsed.Count; i++)
                exp.Add(parsed[0] + i);
        }

        expected = exp;

        if (parsed.Count == exp.Count && parsed.SequenceEqual(exp))
            return true;

        var letzte = maxExisting.HasValue
            ? maxExisting.Value.ToString(CultureInfo.InvariantCulture)
            : "(keine)";
        var erwartetTxt = string.Join(", ", exp.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        var erhaltenTxt = string.Join(", ", parsed.Select(x => x.ToString(CultureInfo.InvariantCulture)));
        message = parsed.Count == 1
            ? $"Personalnummer muss direkt anschliessen. Letzte Nr. in OneCrew: {letzte}. Erwartet: {erwartetTxt}. Erhalten: {erhaltenTxt}."
            : $"Neue Personalnummern müssen fortlaufend an die letzte Nr. und untereinander anschliessen. Letzte Nr. in OneCrew: {letzte}. Erwartet: {erwartetTxt}. Erhalten: {erhaltenTxt}.";
        return false;
    }

    /// <summary>
    /// Höchste rein numerische Personalnummer mit Filial-Präfix
    /// (wie «letzte Nr.» in der MA-Liste: keine «alt»-Suffixe, keine
    /// Archivnummern). Ohne Nummernkreis wird nur das Präfix geprüft.
    /// </summary>
    public static long? FindMaxExisting(IEnumerable<string?> employeeNumbers, string? prefix)
        => new Nummernkreis(prefix, null).Hoechste(employeeNumbers);

    /// <summary>
    /// Variante MIT Nummernkreis (Walter-Vorgabe 02.09.2026): zählt nur
    /// Nummern, die auch die richtige Länge haben. Ein Vertipper wie «122023»
    /// gilt damit nicht mehr als «letzte Nummer» der Filiale 122.
    /// </summary>
    public static long? FindMaxExisting(IEnumerable<string?> employeeNumbers, Nummernkreis kreis)
        => kreis.Hoechste(employeeNumbers);

    /// <summary>
    /// Ziffern eines Restaurant-Codes ohne führende Nullen («075» → «75»).
    /// Die Logik lebt in <see cref="Nummernkreis"/>; hier bleibt nur der
    /// bisherige Name für bestehende Aufrufer.
    /// </summary>
    public static string NormalizeRestaurantPrefix(string? restaurantCode)
        => Nummernkreis.NurZiffern(restaurantCode);
}
