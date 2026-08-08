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
