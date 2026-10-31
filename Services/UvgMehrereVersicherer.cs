namespace HrSystem.Services;

/// <summary>
/// Mehrere UVG-Versicherer (Swissdec 8.3.4, Systemtest S02_03_03).
/// Öffentliche Verwaltungen versichern einen Teil privat und einen Teil bei der Suva.
/// Ein zweiter Versicherer an der Filiale bleibt daneben offen. Die Person
/// bekommt einen davon, sobald mehr als einer offen ist.
/// </summary>
public static class UvgMehrereVersicherer
{
    public const string ZuweisungPflicht =
        "Die Filiale hat mehrere UVG-Versicherer. Bitte den zuständigen der Person zuweisen.";

    /// <summary>UVG und BVG: nur derselbe Empfänger wird versioniert, ein anderer bleibt offen.</summary>
    public static bool BleibtDanebenOffen(string? art, int altEmpfaengerId, int neuEmpfaengerId)
        => art is "UVG" or "BVG" && altEmpfaengerId != neuEmpfaengerId;

    /// <summary>null = Zuweisung darf leer bleiben (kein oder genau ein offener Versicherer).</summary>
    public static string? ZuweisungFehlt(int anzahlOffen, int? empfaengerId)
        => anzahlOffen > 1 && empfaengerId is not > 0 ? ZuweisungPflicht : null;
}
