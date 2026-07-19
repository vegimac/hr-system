using System.Text.Json.Serialization;

namespace HrSystem.Models;

/// <summary>
/// Zusatz-Adressen eines Mitarbeiters (z.B. Korrespondenzadresse, Ferienwohnung,
/// Sozialamt). Die HAUPTADRESSE liegt direkt am Employee (für QST/Wohnkanton-Logik).
/// </summary>
public class EmployeeAddress
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }

    /// <summary>z.B. "Korrespondenzadresse", "Ferienwohnung", "Sozialamt"</summary>
    public string AddressType { get; set; } = "Korrespondenzadresse";

    public DateOnly? ValidFrom { get; set; }
    public string? Description { get; set; }

    public string? Street { get; set; }
    public string? Street2 { get; set; }
    public string? PoBox { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? BfsNumber { get; set; }
    public string? Canton { get; set; }
    public string Country { get; set; } = "Schweiz";

    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Email { get; set; }
    public bool IncamailDisabled { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public Employee? Employee { get; set; }
}
