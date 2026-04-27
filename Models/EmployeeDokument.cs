namespace HrSystem.Models;

/// <summary>
/// Hochgeladenes Dokument zu einem Mitarbeiter.
/// Die Datei selbst liegt im Filesystem unter
///   {Documents:StoragePath}/{employee_id}/{filename_storage}
/// und wird NIE in der DB als BLOB gespeichert.
/// </summary>
public class EmployeeDokument
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int DokumentTypId { get; set; }

    /// <summary>
    /// Filiale-Code (RestaurantCode der aktiven Filiale beim Upload, z.B. "058").
    /// Wird zur Strukturierung des Storage-Pfads verwendet:
    ///   data/documents/{branch_code}/{employee_id}/{filename_storage}
    /// NULL für Altdokumente vor der Branch-Migration → Fallback auf alte Struktur.
    /// </summary>
    public string? BranchCode { get; set; }

    /// <summary>Original-Dateiname beim Upload (z.B. "Arztzeugnis_Dr_Mueller_2026-04-15.pdf").</summary>
    public string FilenameOriginal { get; set; } = "";

    /// <summary>Storage-Dateiname (UUID + Extension), z.B. "a3b1c2d4-...-pdf".</summary>
    public string FilenameStorage { get; set; } = "";

    public string MimeType { get; set; } = "";
    public long GroesseBytes { get; set; }

    /// <summary>Optionale freie Notiz.</summary>
    public string? Bemerkung { get; set; }

    /// <summary>Optional: Gültigkeitsbeginn (z.B. Beginn Aufenthaltsbewilligung).</summary>
    public DateOnly? GueltigVon { get; set; }

    /// <summary>Optional: Ablaufdatum für Erinnerungs-Funktionen.</summary>
    public DateOnly? GueltigBis { get; set; }

    public int? HochgeladenVon { get; set; }
    public DateTime HochgeladenAm { get; set; } = DateTime.UtcNow;
}
