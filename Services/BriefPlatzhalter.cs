using System.Text.RegularExpressions;

namespace HrSystem.Services;

/// <summary>
/// Ersetzt {{platzhalter}} im Lauftext eines Briefs. Unbekannte Klammern
/// bleiben stehen, damit ein Tippfehler im Text sichtbar ist.
/// </summary>
public static class BriefPlatzhalter
{
    public static readonly IReadOnlyList<(string Key, string Label)> Bekannt = new (string, string)[]
    {
        ("anrede", "Briefanrede"),
        ("vorname", "Vorname"),
        ("nachname", "Nachname"),
        ("name", "Vor- und Nachname"),
        ("personalnummer", "Personalnummer"),
        ("austrittsdatum", "Austrittsdatum"),
        ("funktion", "Funktion"),
        ("filiale", "Filiale"),
        ("firma", "Firma"),
        ("unterzeichner", "Unterzeichner"),
        ("ort", "Ort der Filiale"),
        ("datum", "Heutiges Datum"),
    };

    private static readonly Regex Token = new(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    public static string FuegeEin(string? vorlage, IReadOnlyDictionary<string, string>? werte)
    {
        if (string.IsNullOrEmpty(vorlage)) return "";
        if (werte == null || werte.Count == 0) return vorlage;
        var map = new Dictionary<string, string>(werte, StringComparer.OrdinalIgnoreCase);
        return Token.Replace(vorlage, m =>
            map.TryGetValue(m.Groups[1].Value, out var wert) ? wert ?? "" : m.Value);
    }
}

/// <summary>Starttext der mitgelieferten Briefe. Die Grussformel steht im Text, nicht im PDF.</summary>
public static class BriefVorlageText
{
    public const string AustrittCode = "AUSTRITT";
    public const string AustrittName = "Austrittsbrief";
    public const string AustrittBetreff = "Dein Austritt per {{austrittsdatum}}";
    public const string AustrittText =
        "{{anrede}}\n\n" +
        "wir bestätigen dir deinen Austritt per {{austrittsdatum}}.\n\n" +
        "Deine Funktion war {{funktion}}, deine Filiale {{filiale}}, deine Personalnummer {{personalnummer}}.\n\n" +
        "Bitte gib Uniform, Schlüssel und Badge bis zum letzten Arbeitstag ab.\n\n" +
        "Freundliche Grüsse";
}
