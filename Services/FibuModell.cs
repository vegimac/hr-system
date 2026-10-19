namespace HrSystem.Services;

/// <summary>
/// Fibu: Aufwandskonto aus dem Vertrag (Modell), keine Kostenstellen-Pflege
/// (Walter 19.10.2026 — Abacus nimmt nur Fibu-Konten).
/// CREW = MTP + FIX, FLEX = FLEX, MGMT = FIX-M.
/// </summary>
public static class FibuModell
{
    public const string Crew = "CREW";
    public const string Flex = "FLEX";
    public const string Mgmt = "MGMT";

    /// <summary>Gruppe für Kontoplan-Zeilen (lohn_konto_mapping.modell).</summary>
    public static string Gruppe(string? employmentModel) => (employmentModel ?? "").ToUpperInvariant() switch
    {
        "FLEX" => Flex,
        "FIX-M" => Mgmt,
        "MTP" or "FIX" => Crew,
        _ => Crew
    };

    /// <summary>Personalaufwand-Sollkonto (4000 Crew / 4001 Flex / 4055 Management).</summary>
    public static string AufwandSoll(string? employmentModel) => Gruppe(employmentModel) switch
    {
        Flex => "4001",
        Mgmt => "4055",
        _ => "4000"
    };

    public static string Bezeichnung(string? gruppeOderModel)
    {
        var g = gruppeOderModel is Crew or Flex or Mgmt
            ? gruppeOderModel
            : Gruppe(gruppeOderModel);
        return g switch
        {
            Flex => "Crew Flex",
            Mgmt => "Management",
            _ => "Crew Fix / MTP"
        };
    }

    /// <summary>Legacy-Kostenstelle nur noch als Fallback beim Lesen alter Zeilen.</summary>
    public static string? LegacyKostenstelle(string? gruppe) => gruppe switch
    {
        Flex => "200",
        Mgmt => "300",
        Crew => "100",
        _ => null
    };

    public static string? GruppeAusLegacyKst(string? kst) => kst switch
    {
        "200" => Flex,
        "300" or "400" => Mgmt,
        "100" => Crew,
        _ => null
    };
}
