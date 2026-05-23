namespace HrSystem.Models;

/// <summary>
/// Posteingang pro Filiale: Geschäftsführer laden Dokumente hoch
/// (Arztzeugnisse, unterschriebene Verträge etc.), Admin/Superuser
/// sortieren sie in die MA-Personalakte (employee_dokument) ein
/// oder löschen sie.
/// </summary>
public class MailboxDocument
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }
    public CompanyProfile? CompanyProfile { get; set; }

    public int? UploadedBy { get; set; }
    public AppUser? Uploader { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    public string OriginalFilename { get; set; } = "";
    public string StorageFilename  { get; set; } = "";
    public string? MimeType        { get; set; }
    public long?   FileSizeBytes   { get; set; }

    /// <summary>Optionale Beschreibung vom Uploader (z.B. „Arztzeugnis Maria, August").</summary>
    public string? Bemerkung { get; set; }

    /// <summary>
    /// Reine Text-Mitteilung statt Datei (Walter-Vorgabe 23.05.2026). Wenn gesetzt,
    /// ist dieser Eintrag eine Nachricht ins MA-Postfach OHNE Anhang
    /// (StorageFilename leer, MimeType null) — z.B. „Dein Stundenlohn steigt per
    /// 01.01.2027 auf CHF 20.45." Das Frontend rendert solche Einträge als
    /// Text-Notiz (kein Download/Preview). OriginalFilename dient als Titel.
    /// </summary>
    public string? MessageBody { get; set; }

    /// <summary>Optional: MA, auf den sich das Dokument bezieht.</summary>
    public int? EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Optional: User der per Email benachrichtigt werden soll (Phase 3).</summary>
    public int? NotifyUserId { get; set; }
    public AppUser? NotifyUser { get; set; }

    /// <summary>
    /// In welches Postfach landet das Dokument:
    ///   "BRANCH" → Filial-Postfach (alle mit Zugriff auf CompanyProfileId sehen's)
    ///   "HR"     → gemeinsames HR-Postfach (nur AppUser.IsHrTeam + Admin sehen's)
    ///   "ADMIN"  → Admin-Postfach (nur Admin sieht's)
    /// CompanyProfileId bleibt immer gesetzt (Filiale des Senders) als Herkunft —
    /// dadurch weiss der Empfänger, woher das Dokument kommt, und es kann später
    /// in eine MA-Personalakte der richtigen Filiale verschoben werden.
    /// </summary>
    public string TargetType { get; set; } = "BRANCH";
}
