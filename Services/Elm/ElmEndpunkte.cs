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

    /// <summary>
    /// Quality Tool der Testinfrastruktur — Lohntest (Calculation Test): vergleicht jede Meldung mit der
    /// Referenz der Muster AG. Adresse aus dem Quality Tool («Your current Interface ELM V6», Walter 27.09.2026).
    /// </summary>
    public const string QualityToolUrl = "https://test.swissdec.ch/qualitytool/stable/domain-analyzer/services/elm/SalaryDeclaration/V6";

    /// <summary>REST-Schnittstelle des Quality Tools (Bericht, neuer Lauf) — nicht für Meldungen.</summary>
    public const string QualityToolApiUrl = "https://test.swissdec.ch/qualitytool/stable/api/elm/v6/runs/";

    public record Ziel(string Schluessel, string Name, string Url, bool IstTest);

    public static readonly IReadOnlyList<Ziel> Alle = new List<Ziel>
    {
        new("test",        "Refapps Receiver (Testinfrastruktur)", TestUrl,        true),
        new("prod",        "Produktiver Distributor",              ProdUrl,        false),
        new("qualitytool", "Swissdec Quality Tool (Lohntest)",     QualityToolUrl, true),
    };

    /// <summary>Testinstanz (INSTANCE_LABEL gesetzt) — nur dort gibt es Testziele und das Quality Tool.</summary>
    public static bool IstTestinstanz()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INSTANCE_LABEL"));

    /// <summary>
    /// Ziele, unter denen der Benutzer beim Senden einer Lohnmeldung wählen darf: auf der Testinstanz
    /// Refapps Receiver (Standard) und Quality Tool, auf der Produktion nur der Distributor.
    /// </summary>
    public static IReadOnlyList<Ziel> MeldungsZiele()
        => IstTestinstanz() ? new[] { Alle[0], Alle[2] } : new[] { Alle[1] };

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

    /// <summary>
    /// Ziel für Benutzer-Aktionen ohne Adresswahl (Zertifikat erneuern, Automatik): die Testinstanz
    /// spricht die Swissdec-Testinfrastruktur an, die Produktion den produktiven Distributor.
    /// </summary>
    public static Ziel Standard()
        => IstTestinstanz() ? Alle[0] : Alle[1];

    /// <summary>Eingetragene Adresse → hinterlegtes Ziel, wenn sie genau eines davon ist.</summary>
    public static Ziel? FindeNachUrl(string? url)
    {
        var u = (url ?? "").Trim().TrimEnd('/');
        return u.Length == 0 ? null
            : Alle.FirstOrDefault(z => string.Equals(z.Url, u, StringComparison.OrdinalIgnoreCase));
    }
}
