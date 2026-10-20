using System.Text.RegularExpressions;

namespace HrSystem.Services;

/// <summary>
/// Funktionsname aus dem Vertrag (app_text JOB_GROUP, z.B. «Schichtkoordinator/in ab 7. Monat»)
/// in der Form der Person: Schichtkoordinatorin bzw. Schichtkoordinator.
/// </summary>
public static partial class Funktionsbezeichnung
{
    public static string Geschlecht(string? text, bool? weiblich)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var roh = text.Trim();
        if (weiblich == null) return roh;
        return SlashIn().Replace(roh, m => weiblich.Value ? m.Groups[1].Value + "in" : m.Groups[1].Value);
    }

    [GeneratedRegex(@"(\p{L}+)/in\b")]
    private static partial Regex SlashIn();
}
