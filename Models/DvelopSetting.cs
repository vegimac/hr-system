namespace HrSystem.Models;

/// <summary>
/// d.velop-documents-API-Konfiguration (Walter-Vorgabe 10.07.2026):
/// Direktzugriff auf das alte DMS für den API-Voll-Scan der Personaldossiers
/// («alle nochmals durchgehen, dass keines vergessen ist»). Singleton-Tabelle
/// analog <see cref="EcallSetting"/> — immer nur Row Id=1.
///
/// BaseUrl: z.B. https://xxxx.d-velop.cloud (ohne Slash am Ende).
/// Der API-Key wird AES-verschlüsselt gespeichert (SimpleAesService) und
/// nie an die API ausgegeben (nur Flag HasApiKey).
/// </summary>
public class DvelopSetting
{
    public int Id { get; set; }                     // Singleton — immer 1
    public string? BaseUrl { get; set; }
    public string? ApiKeyEncrypted { get; set; }    // AES-verschlüsselt, Base64
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
