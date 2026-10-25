namespace HrSystem.Services.Elm;

/// <summary>
/// BFS am Hauptsitz (Swissdec 12.2 / 12.2.1): ein Buchungskreis, eine Lohnvereinbarung.
/// </summary>
public static class BfsAngaben
{
    public const string Verband = "CLA-Association";
    public const string Firma = "CLA-BusinessOrGovernment";
    public const string Kollektiv = "collectiveContratOutside-CLA";
    public const string Einzel = "individualContract";

    public static bool IstGueltig(string? code)
        => code is Verband or Firma or Kollektiv or Einzel;

    /// <summary>Leer oder ungültig wird nicht gespeichert.</summary>
    public static string? Code(string? roh)
    {
        var c = (roh ?? "").Trim();
        return IstGueltig(c) ? c : null;
    }

    /// <summary>XML-Wert. Leer bleibt der Einzelarbeitsvertrag der Muster AG.</summary>
    public static string FuerXml(string? roh) => Code(roh) ?? Einzel;

    public static string? Kreis(string? roh)
    {
        var c = (roh ?? "").Trim();
        return c.Length == 0 ? null : c;
    }
}
