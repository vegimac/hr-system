using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

/// <summary>
/// Verknüpft eine Cowork-Filiale (CompanyProfile) mit einer easy@work-Customer-ID.
/// Wird gepflegt im Filial-Einstellungen-Tab. Ohne Eintrag wird die Filiale
/// nicht aus easy@work synchronisiert.
/// </summary>
[Table("easyatwork_branch_mapping")]
public class EasyAtWorkBranchMapping
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_profile_id")]
    public int CompanyProfileId { get; set; }
    [ForeignKey(nameof(CompanyProfileId))]
    public CompanyProfile? CompanyProfile { get; set; }

    /// <summary>Customer-ID in der easy@work-API.</summary>
    [Column("easyatwork_customer_id")]
    public int EasyAtWorkCustomerId { get; set; }

    /// <summary>Customer-Number aus easy@work (zum Cross-Check / Anzeige).</summary>
    [Column("easyatwork_customer_number")]
    [MaxLength(64)]
    public string? EasyAtWorkCustomerNumber { get; set; }

    /// <summary>Display-Name aus easy@work (Cache zur Anzeige).</summary>
    [Column("easyatwork_customer_name")]
    [MaxLength(256)]
    public string? EasyAtWorkCustomerName { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
