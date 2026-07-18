using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

/// <summary>
/// Alte/zweite Personalnummern eines MA (Walter-Vorgabe 21.06.2026). Ersetzt die
/// starren Felder employee_number_alt1/alt2 — ein MA kann beliebig viele alte
/// Nummern haben (Wiedereintritte, Mirus-Migration, …). Dient als zusätzlicher
/// Match-Schlüssel im MA- und Stempelzeiten-Sync.
/// </summary>
[Table("employee_number_alias")]
public class EmployeeNumberAlias
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }
    [ForeignKey(nameof(EmployeeId))]
    public Employee? Employee { get; set; }

    /// <summary>Die alte Personalnummer.</summary>
    [Column("number")]
    public string Number { get; set; } = "";

    /// <summary>Optional: Gültigkeitsbereich der alten Nummer.</summary>
    [Column("valid_from")]
    public DateOnly? ValidFrom { get; set; }
    [Column("valid_to")]
    public DateOnly? ValidTo { get; set; }

    /// <summary>Herkunft: manual | easyatwork_sync | import | migration.</summary>
    [Column("source")]
    public string Source { get; set; } = "manual";

    [Column("created_at")]
    public DateTime? CreatedAt { get; set; } = DateTime.Now;
}
