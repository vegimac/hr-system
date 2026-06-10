using System.Text.Json.Serialization;

namespace HrSystem.Models;

public class Employment
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int? CompanyProfileId { get; set; }

    [JsonIgnore]
    public CompanyProfile? CompanyProfile { get; set; }

    public string EmploymentModel { get; set; } = "";
    public string SalaryType { get; set; } = "";

    public DateTime ContractStartDate { get; set; }
    public DateTime? ContractEndDate { get; set; }

    /// <summary>
    /// Stellenbezeichnung (Free-Text) — wird 1:1 auf den Vertrag gedruckt,
    /// z.B. „Shift Coordinator", „Rest. Manager Stellvertreter". Hat NICHTS
    /// mit der Funktionsgruppen-/Mindestlohn-Klassifikation zu tun (das ist
    /// <see cref="JobGroupId"/>). (Walter-Vorgabe 26.05.2026 — Klarstellung
    /// nach Refactor: vorher hielt das Feld den JobGroupCode überladen.)
    /// </summary>
    public string? JobTitle { get; set; }

    /// <summary>
    /// FK auf <c>job_group.id</c> — die saubere Referenz auf die Funktionsgruppe.
    /// Steuert den Mindestlohn-Lookup. Bei Code-Umbenennungen in
    /// <see cref="JobGroup"/> bleibt die Zuordnung stabil.
    /// (Walter-Vorgabe 26.05.2026)
    /// </summary>
    public int? JobGroupId { get; set; }
    public JobGroup? JobGroup { get; set; }

    /// <summary>
    /// JSON-Convenience: liefert beim GET den Code aus der geladenen
    /// JobGroup-Nav (für Frontend-Anzeige) und nimmt beim POST/PUT den Code
    /// als String entgegen — Backend resolved zu <see cref="JobGroupId"/>.
    /// Nicht in der DB gespeichert.
    /// </summary>
    private string? _jobGroupCodeInput;
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string? JobGroupCode
    {
        get => JobGroup?.Code ?? _jobGroupCodeInput;
        set => _jobGroupCodeInput = value;
    }

    public string? ContractType { get; set; }

    /// <summary>
    /// Gastronomische Ausbildungsstufe (L-GAV-Code: Ia, Ib, II, IIIa, IIIb, IV).
    /// Wird beim Vertrag gespeichert, weil bei einer Ausbildungs-Änderung sowieso
    /// ein neuer Vertrag entsteht. Treibt zusammen mit JobTitle (= JobGroupCode)
    /// und EmploymentModel den Mindestlohn-Lookup in MinimumWageRulesNew.
    /// </summary>
    public string? EducationLevelCode { get; set; }

    public decimal? EmploymentPercentage { get; set; }
    public decimal? WeeklyHours { get; set; }
    public decimal? GuaranteedHoursPerWeek { get; set; }

    public decimal? MonthlySalaryFte { get; set; }   // 100%-Lohn (Vollpensum-Referenz)
    public decimal? MonthlySalary { get; set; }       // tatsächlicher Lohn (nach Pensum)
    public decimal? HourlyRate { get; set; }

    // Walter-Vorgabe 06.06.2026 (Stufe 1b): Ferien %, Feiertag %, 13. ML %
    // sind nicht mehr pro Vertrag, sondern pro Filiale (CompanyProfile.Default*
    // + altersaware Schwelle). Felder + DB-Spalten entfernt.

    public string? VacationPaymentMode { get; set; }

    public int? ProbationPeriodMonths { get; set; }
    public DateTime? ProbationEndDate { get; set; }

    public bool IsActive { get; set; } = true;

    [JsonIgnore]
    public Employee? Employee { get; set; }
}