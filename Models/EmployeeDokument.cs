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

    // ── Dokument-Metadaten (Walter-Vorgabe 24.05.2026) ──────────────────────
    // Übernommen aus d.velop bzw. gepflegt durch das System.

    /// <summary>"Erstellt am" — Erstellzeitpunkt des Dokuments (aus d.velop).</summary>
    public DateTime? ErstelltAm { get; set; }

    /// <summary>"Geändert am" — Eintrag in d.velop zuletzt geändert.</summary>
    public DateTime? GeaendertAm { get; set; }

    /// <summary>"Datei geändert am" — die Datei selbst wurde geändert (z.B. PDF gedreht + gespeichert).</summary>
    public DateTime? DateiGeaendertAm { get; set; }

    /// <summary>"Zugriffsdatum" — zuletzt angeschaut.</summary>
    public DateTime? ZugriffAm { get; set; }

    /// <summary>Anzeigename, wer zuletzt geändert hat (live: eingeloggter User; Backfill: d.velop "Im Besitz von").</summary>
    public string? GeaendertVon { get; set; }

    /// <summary>Anzeigename, wer zuletzt angeschaut hat.</summary>
    public string? ZugriffVon { get; set; }

    /// <summary>
    /// d.velop-Dokument-ID (z.B. "XG00011124") — eindeutige Identifikation,
    /// damit der Metadaten-Backfill zuverlässig matcht, auch wenn mehrere
    /// d.velop-Dokumente denselben Dateinamen haben. Wird beim ZIP-Import
    /// und beim Quick-Upload aus der „fehlende Dokumente"-Liste mit
    /// geschrieben. NULL bei Dokumenten, die nicht aus d.velop stammen.
    /// Walter-Vorgabe 06.06.2026.
    /// </summary>
    public string? DvelopDokumentId { get; set; }
}
