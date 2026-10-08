namespace HrSystem.Models;

/// <summary>
/// Vom Virenscanner gemeldete Datei (Walter-Vorgabe 08.10.2026). Bei einem Upload wird
/// nichts gespeichert — die Zeile hält nur fest, wer was versucht hat. Beim Bestands-Scan
/// bleibt die Datei liegen, bis ein Admin sie geprüft und gelöscht hat.
/// </summary>
public class VirenFund
{
    public const string QuelleUpload     = "UPLOAD";
    public const string QuelleWebDav     = "WEBDAV";
    public const string QuelleEasyAtWork = "EASYATWORK";
    public const string QuelleBestand    = "BESTAND";

    public int Id { get; set; }
    public DateTime GefundenAm { get; set; } = DateTime.Now;
    public string Quelle { get; set; } = QuelleUpload;
    public string Dateiname { get; set; } = "";
    public string Virus { get; set; } = "";
    /// <summary>Upload: aufgerufene Adresse. Bestand: Pfad relativ zur Dokumentablage.</summary>
    public string Ort { get; set; } = "";
    public int? UserId { get; set; }
    public string? Benutzer { get; set; }
    public int? EmployeeId { get; set; }
    public bool Erledigt { get; set; }
    public DateTime? ErledigtAm { get; set; }
    public int? ErledigtVonUserId { get; set; }
}
