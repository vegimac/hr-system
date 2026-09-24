namespace HrSystem.Services.Elm;

/// <summary>
/// Die EINZIGEN Adressen, die OneCrew als Empfänger ansprechen darf
/// (Swissdec Foundation-Test F01_01 «Adressierung», Walter 24.09.2026).
///
/// Prüfpunkt im Wortlaut: «Das Sendersystem ist für die korrekte Adressierung
/// des Distributors verantwortlich… Die URL kann vom Endbenutzer nicht
/// beliebig verändert werden.» Darum steht die Adresse HIER im Code und
/// kommt nicht mehr aus einem Eingabefeld: Das UI und die API wählen nur
/// noch ein ZIEL («test» oder «prod»), die URL dazu liefert diese Klasse.
///
/// Ändert Swissdec eine Adresse, wird sie hier angepasst und deployt — es
/// gibt bewusst keinen Weg, sie zur Laufzeit zu überschreiben (auch nicht
/// über Einstellungen oder appsettings).
/// </summary>
public static class ElmEndpunkte
{
    /// <summary>Refapps Receiver der Swissdec-Testinfrastruktur — unser Übungsplatz.</summary>
    public const string TestUrl = "https://test.swissdec.ch/refapps/stable/receiver/services/elm/SalaryDeclaration/V6";

    /// <summary>Produktiver Distributor — nimmt ohne Transmitter-Zertifikat nur den Ping an.</summary>
    public const string ProdUrl = "https://distributor.swissdec.ch/services/elm/SalaryDeclaration/V6";

    public record Ziel(string Schluessel, string Name, string Url, bool IstTest);

    public static readonly IReadOnlyList<Ziel> Alle = new List<Ziel>
    {
        new("test", "Refapps Receiver (Testinfrastruktur)", TestUrl, true),
        new("prod", "Produktiver Distributor",              ProdUrl, false),
    };

    /// <summary>
    /// Nur verschlüsselte Adressen sind zulässig (Foundation-Test F02_01 «Transportsicherheit»,
    /// Walter 24.09.2026): «Der Übermittlungskanal muss verschlüsselt sein. Alle Verbindungen sind
    /// mittels TLS gesichert.» Eine `http://`-Adresse wird darum abgewiesen — auch wenn sie ein
    /// Super-Admin von Hand einträgt.
    /// </summary>
    public static bool IstSicher(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps;

    /// <summary>Ziel-Schlüssel → Eintrag; unbekannt oder leer ⇒ null (Aufrufer antwortet mit 400).</summary>
    public static Ziel? Finde(string? schluessel)
        => Alle.FirstOrDefault(z => string.Equals(z.Schluessel, (schluessel ?? "").Trim(),
                                                  StringComparison.OrdinalIgnoreCase));
}
