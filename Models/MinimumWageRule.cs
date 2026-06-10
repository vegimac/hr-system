namespace HrSystem.Models;

public class MinimumWageRuleNew
{
    public int Id { get; set; }

    /// <summary>
    /// Legacy-Text-Spalte — hielt den JobGroupCode redundant. Bleibt
    /// vorerst, wird beim Speichern aus <see cref="JobGroup"/>.Code synchron
    /// gehalten (Walter-Vorgabe 26.05.2026: id-FK ist die Wahrheit). Kann
    /// später per `ALTER TABLE … DROP COLUMN job_group_code` entfernt werden.
    /// </summary>
    public string JobGroupCode { get; set; } = "";

    /// <summary>
    /// FK auf <c>job_group.id</c> — die saubere Referenz auf die Funktionsgruppe.
    /// </summary>
    public int? JobGroupId { get; set; }
    public JobGroup? JobGroup { get; set; }

    public string EmploymentModelCode { get; set; } = "";

    public int EducationLevelId { get; set; }

    public string SalaryType { get; set; } = ""; // hourly / monthly

    public decimal Amount { get; set; }

    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public bool IsActive { get; set; }

    /// <summary>
    /// Maximales Alter (inklusiv). NULL = keine Altersgrenze.
    /// Beispiel: AgeMax=17 → Regel gilt bis zum 18. Geburtstag.
    /// L-GAV Anhang II hat Sonderregeln für Jugendliche.
    /// </summary>
    public int? AgeMax { get; set; }

    /// <summary>
    /// „Bestätigt" — nur für GEPLANTE (zukünftige) Sätze relevant (Walter-Vorgabe
    /// 23.05.2026). Frisch via /copy erzeugte Folge-Sätze sind false (rot = noch
    /// zu prüfen). Beim Speichern (PUT /{id}) wird true gesetzt. Frontend-Farbe:
    /// rot = nicht bestätigt, grün = Betrag ≠ aktuell (geändert), orange = bestätigt
    /// aber unverändert.
    /// </summary>
    public bool Confirmed { get; set; }

    public EducationLevel? EducationLevel { get; set; }
}