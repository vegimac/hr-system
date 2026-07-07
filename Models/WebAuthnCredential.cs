namespace HrSystem.Models;

/// <summary>
/// Ein registrierter Passkey / WebAuthn-Credential eines Login-Users (Walter 01.07.2026).
/// Für biometrisches Login (Face ID / Touch ID / Fingerprint) im MA-Postfach.
///
/// WICHTIG: Hier liegt NUR der öffentliche Schlüssel. Der private Schlüssel und
/// die Biometrie bleiben im Gerät (Secure Enclave) und verlassen es nie. Es
/// werden KEINE biometrischen Daten gespeichert.
///
/// Ein User kann mehrere Credentials haben (mehrere Geräte). Passkeys sind an die
/// Domain gebunden (RP-ID = onecrew.ch produktiv).
/// </summary>
public class WebAuthnCredential
{
    public int Id { get; set; }

    /// <summary>Login-Account (app_user), zu dem dieser Passkey gehört.</summary>
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    /// <summary>Credential-ID (vom Authenticator vergeben) — eindeutig.</summary>
    public byte[] CredentialId { get; set; } = System.Array.Empty<byte>();

    /// <summary>Öffentlicher Schlüssel (COSE-kodiert).</summary>
    public byte[] PublicKey { get; set; } = System.Array.Empty<byte>();

    /// <summary>Signaturzähler (Klon-Erkennung); wird bei jedem Login aktualisiert.</summary>
    public long SignCount { get; set; }

    /// <summary>User-Handle, das dem Authenticator übergeben wurde (für discoverable credentials).</summary>
    public byte[]? UserHandle { get; set; }

    /// <summary>Transports (z.B. „internal", „hybrid") als CSV — nur informativ.</summary>
    public string? Transports { get; set; }

    /// <summary>Authenticator-AAGUID (Gerätetyp) — nur informativ.</summary>
    public string? Aaguid { get; set; }

    /// <summary>Vom MA vergebener Gerätename, z.B. „iPhone von Eleni".</summary>
    public string? DeviceLabel { get; set; }

    public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;
    public System.DateTime? LastUsedAt { get; set; }
}
