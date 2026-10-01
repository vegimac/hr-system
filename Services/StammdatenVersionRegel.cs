namespace HrSystem.Services;

/// <summary>
/// Versionierte Lohn-Stammdaten (Mindestlöhne, SV-Sätze) — wer darf was ändern
/// (Walter-Vorgabe 01.10.2026): vergangene Versionen NIE, die aktuelle nur
/// solange sie in keinem Lohnlauf steckt, künftige frei (ausser sie stecken
/// schon in einem Lohnlauf, z.B. Akonto vor Monatsbeginn). Massgebend ist das
/// Tagesdatum, nicht die Lohnperiode — es geht um die Gültigkeit des Satzes.
/// </summary>
public static class StammdatenVersionRegel
{
    public const string Vergangen = "vergangen";
    public const string Aktuell   = "aktuell";
    public const string Geplant   = "geplant";

    public static string Zeitlage(DateOnly gueltigAb, DateOnly? gueltigBis, DateOnly heute)
    {
        if (gueltigAb > heute) return Geplant;
        if (gueltigBis.HasValue && gueltigBis.Value < heute) return Vergangen;
        return Aktuell;
    }

    public static bool Bearbeitbar(string zeitlage, bool inLohnVerwendet)
        => zeitlage != Vergangen && !inLohnVerwendet;

    /// <summary>Grund der Sperre als Text für die Meldung, null = bearbeitbar.</summary>
    public static string? Sperrgrund(string zeitlage, bool inLohnVerwendet) =>
        zeitlage == Vergangen ? "Diese Version ist abgelaufen und bleibt unverändert."
        : inLohnVerwendet     ? "Diese Version wurde bereits in einem Lohnlauf verwendet."
        : null;

    /// <summary>Eine neue Version darf frühestens morgen beginnen.</summary>
    public static bool NeuerStartErlaubt(DateOnly gueltigAb, DateOnly heute) => gueltigAb > heute;
}
