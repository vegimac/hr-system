namespace HrSystem.Models;

/// <summary>
/// Bewusst bestätigter Befund der Personaldaten-Prüfung (Walter 15.10.2026), z.B. «Alter unter 16».
/// Gilt für genau einen Wert (<see cref="Code"/> + <see cref="Wert"/>) — ändert sich der Wert,
/// wird neu gefragt. Ein neuer MA aus easy@work hat noch keine OneCrew-Id, darum zusätzlich die
/// easy@work-Id.
/// </summary>
public class PersonaldatenBestaetigung
{
    public int Id { get; set; }
    public int? EmployeeId { get; set; }
    public int? EasyAtWorkEmployeeId { get; set; }
    public string Code { get; set; } = "";
    public string Wert { get; set; } = "";
    public string? BestaetigtVon { get; set; }
    public DateTime BestaetigtAm { get; set; } = DateTime.Now;
}
