namespace HrSystem.Models;

/// <summary>
/// Eingang aus easy@work (Walter-Vorgabe 08.09.2026): Dateien, die ein MA in
/// der easy@work-App «an HR» hochgeladen hat (HR-Datei mit Anhang, dessen
/// <c>user_id</c> gesetzt ist), werden NIE direkt ins OneCrew-Dossier gelegt,
/// sondern ins HR-Postfach — HR entscheidet, was damit passiert.
///
/// Diese Tabelle merkt sich pro easy@work-Anhang, dass er schon geholt wurde,
/// damit derselbe Upload nicht bei jedem Lauf erneut im Postfach landet.
/// </summary>
public class EasyAtWorkHrFileEingang
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public int EasyAtWorkCustomerId { get; set; }
    public int EasyAtWorkEmployeeId { get; set; }

    /// <summary>easy@work HR-Datei (hr_files.id).</summary>
    public long EasyAtWorkFileId { get; set; }

    /// <summary>easy@work Anhang (attachments.id) — eindeutig, Dedupe-Schlüssel.</summary>
    public long EasyAtWorkAttachmentId { get; set; }

    /// <summary>Dokumentname in easy@work (hr_files.name).</summary>
    public string DokumentName { get; set; } = "";

    /// <summary>Dateiname des Anhangs in easy@work.</summary>
    public string DateiName { get; set; } = "";

    public string? MimeType { get; set; }
    public long? FileSizeBytes { get; set; }

    /// <summary>easy@work-Benutzer, der hochgeladen hat (attachments.user_id).</summary>
    public long? HochgeladenVonEawUserId { get; set; }

    /// <summary>Zeitpunkt des Uploads in easy@work (attachments.created_at).</summary>
    public DateTime? HochgeladenAm { get; set; }

    /// <summary>Postfach-Eintrag, der daraus entstanden ist.</summary>
    public int? MailboxDocumentId { get; set; }
    public MailboxDocument? MailboxDocument { get; set; }

    public DateTime GeholtAm { get; set; } = DateTime.Now;
}
