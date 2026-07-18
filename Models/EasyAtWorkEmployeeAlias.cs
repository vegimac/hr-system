using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

/// <summary>
/// Zusätzliche / alte easy@work-Mitarbeiter-IDs pro Cowork-MA.
/// Nötig, wenn die easy@work-interne employee_id mittendrin wechselt
/// (Wiedereintritt / Neuanlage) — alte Stempel hängen dann an der alten ID.
/// Der Stempel-Sync nutzt diese Tabelle als Fallback, wenn die Stempel-
/// employee_id nicht über die normale MA-Liste auflösbar ist.
/// Befüllt per Ein-Klick-Knopf an der UNMATCHED-Zeile (Walter 18.06.2026).
/// </summary>
[Table("easyatwork_employee_alias")]
public class EasyAtWorkEmployeeAlias
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }
    [ForeignKey(nameof(EmployeeId))]
    public Employee? Employee { get; set; }

    /// <summary>Die alte / zweite easy@work-employee_id, die an Stempeln hängt.</summary>
    [Column("easyatwork_id")]
    public int EasyAtWorkId { get; set; }

    [Column("note")]
    public string? Note { get; set; }

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; } = DateTime.Now;

    [Column("created_by")]
    public string? CreatedBy { get; set; }
}
