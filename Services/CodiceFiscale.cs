using System.Text.RegularExpressions;

namespace HrSystem.Services;

/// <summary>
/// Italienische Steuernummer (Codice fiscale), Swissdec 11.5.3:
/// 16 Zeichen, fester Aufbau, Prüfziffer über die ungeraden Stellen.
/// </summary>
public static class CodiceFiscale
{
    static readonly Dictionary<char, int> Ungerade = new()
    {
        ['0'] = 1,  ['1'] = 0,  ['2'] = 5,  ['3'] = 7,  ['4'] = 9,
        ['5'] = 13, ['6'] = 15, ['7'] = 17, ['8'] = 19, ['9'] = 21,
        ['A'] = 1,  ['B'] = 0,  ['C'] = 5,  ['D'] = 7,  ['E'] = 9,
        ['F'] = 13, ['G'] = 15, ['H'] = 17, ['I'] = 19, ['J'] = 21,
        ['K'] = 2,  ['L'] = 4,  ['M'] = 18, ['N'] = 20, ['O'] = 11,
        ['P'] = 3,  ['Q'] = 6,  ['R'] = 8,  ['S'] = 12, ['T'] = 14,
        ['U'] = 16, ['V'] = 10, ['W'] = 22, ['X'] = 25, ['Y'] = 24, ['Z'] = 23,
    };

    public static bool Gueltig(string? roh, out string grund)
    {
        var s = (roh ?? "").Trim().ToUpperInvariant().Replace(" ", "");
        if (s.Length != 16)
        {
            grund = "Die italienische Steuernummer hat 16 Zeichen.";
            return false;
        }
        if (!Regex.IsMatch(s, "^[A-Z]{6}[0-9]{2}[ABCDEHLMPRST][0-9]{2}[A-Z][0-9]{3}[A-Z]$"))
        {
            grund = "Die italienische Steuernummer entspricht nicht dem Aufbau des Codice fiscale.";
            return false;
        }
        var tag = int.Parse(s.Substring(9, 2));
        if (tag > 40) tag -= 40;
        if (tag is < 1 or > 31)
        {
            grund = "Der Geburtstag in der italienischen Steuernummer ist ungültig.";
            return false;
        }
        var summe = 0;
        for (var i = 0; i < 15; i++)
            summe += i % 2 == 0 ? Ungerade[s[i]] : Gerade(s[i]);
        var pruef = (char)('A' + summe % 26);
        if (s[15] != pruef)
        {
            grund = "Die Prüfziffer der italienischen Steuernummer stimmt nicht.";
            return false;
        }
        grund = "";
        return true;
    }

    static int Gerade(char c) => c is >= '0' and <= '9' ? c - '0' : c - 'A';
}
