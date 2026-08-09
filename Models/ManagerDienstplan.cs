namespace HrSystem.Models;

/// <summary>
/// Manager-Dienstplan (Walter-Vorgabe 08.08.2026, ersetzt die Excel
/// «Manager DP»): pro FIX-M-MA und Tag ein Schicht-Kürzel aus dem
/// <see cref="DienstplanCode"/>-Katalog. Absenzen (Ferien/Krank/…) werden
/// NICHT hier gespeichert — sie kommen als Live-Overlay aus den Absenzen
/// und sperren die Zelle.
/// </summary>
public class ManagerDienstplanEntry
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public DateOnly Datum { get; set; }
    /// <summary>Kürzel aus dienstplan_code (F/M/S/-/SK/SKM …).</summary>
    public string Code { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    /// <summary>Anzeigename des letzten Bearbeiters (aus JWT, nie aus dem Body).</summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>Kürzel-Katalog (Walter kann selbst ergänzen).</summary>
public class DienstplanCode
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Bezeichnung { get; set; } = "";
    /// <summary>Hex-Hintergrundfarbe der Zelle, z.B. «#fef9c3» (frei = gelb).</summary>
    public string? Farbe { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Feiertag für den Manager-Dienstplan (Walter-Vorgabe 09.08.2026).
/// Geltungsbereich dreistufig: NATIONAL (alle Filialen), KANTON (Filialen
/// mit passendem <see cref="CompanyProfile.KantonCode"/>), FILIALE (genau
/// eine Filiale — Gemeinde-Feiertage). Reiner Planungs-Marker, KEINE
/// Lohn-Wirkung (Feiertags-Saldo-Logik läuft separat in der Payroll).
/// </summary>
public class DienstplanFeiertag
{
    public int Id { get; set; }
    public DateOnly Datum { get; set; }
    public string Bezeichnung { get; set; } = "";
    /// <summary>NATIONAL | KANTON | FILIALE</summary>
    public string Scope { get; set; } = "NATIONAL";
    /// <summary>Bei Scope=KANTON: 2-Zeichen-Code (LU, AG, BE, …).</summary>
    public string? KantonCode { get; set; }
    /// <summary>Bei Scope=FILIALE: die betroffene Filiale.</summary>
    public int? CompanyProfileId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// Vorstellungsgespräch-Zeitfenster eines GF/Managers (Walter-Vorgabe
/// 09.08.2026, Stufe 1): der GF teilt NUR mit, wann er an einem seiner im
/// Manager-Dienstplan als ARBEIT (F/M/S) geplanten Tage Zeit für
/// Vorstellungsgespräche hat. HR sieht die Fenster im HR-Hub (read-only).
/// Die eigentliche Terminbuchung durch HR ist Stufe 2 (noch nicht gebaut).
/// </summary>
public class InterviewFenster
{
    public int Id { get; set; }
    /// <summary>Der Manager/GF (employee), dem das Fenster gehört.</summary>
    public int EmployeeId { get; set; }
    public DateOnly Datum { get; set; }
    public TimeOnly VonZeit { get; set; }
    public TimeOnly BisZeit { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    /// <summary>Anzeigename des Erfassers (aus JWT, nie aus dem Body).</summary>
    public string? CreatedBy { get; set; }
}

/// <summary>
/// Schulferien pro Filiale (Walter-Vorgabe 09.08.2026) — Anzeige-Band in der
/// Filial-Zeile des Manager-Dienstplans (wie «Sportferien» in der alten Excel).
/// </summary>
public class BranchSchulferien
{
    public int Id { get; set; }
    public int CompanyProfileId { get; set; }
    public string Bezeichnung { get; set; } = "";
    public DateOnly Von { get; set; }
    public DateOnly Bis { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
