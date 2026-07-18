using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

/// <summary>
/// Sync-State pro Filiale + Resource-Typ. Hält fest, bis wohin (updated_at)
/// wir die jeweilige Entity (Employees, Timepunches, Contracts ...) zuletzt
/// gezogen haben. Beim nächsten Sync nur Datensätze mit
/// <c>updated_at &gt; LastSyncAt</c> verarbeiten.
/// </summary>
[Table("easyatwork_sync_state")]
public class EasyAtWorkSyncState
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_profile_id")]
    public int CompanyProfileId { get; set; }
    [ForeignKey(nameof(CompanyProfileId))]
    public CompanyProfile? CompanyProfile { get; set; }

    /// <summary>EMPLOYEE / CONTRACT / PAY_RATE / FISCAL_INFO / TIMEPUNCH</summary>
    [Column("resource")]
    [MaxLength(32)]
    public string Resource { get; set; } = "";

    /// <summary>Letzter erfolgreicher Sync-Lauf (UTC).</summary>
    [Column("last_sync_at")]
    public DateTime? LastSyncAt { get; set; }

    /// <summary>Höchstes verarbeitetes updated_at aus easy@work (UTC).</summary>
    [Column("last_seen_updated_at")]
    public DateTime? LastSeenUpdatedAt { get; set; }

    /// <summary>Anzahl Datensätze beim letzten Lauf (für Logging/Anzeige).</summary>
    [Column("last_row_count")]
    public int? LastRowCount { get; set; }

    /// <summary>Fehlermeldung beim letzten Lauf (oder NULL bei Erfolg).</summary>
    [Column("last_error")]
    public string? LastError { get; set; }
}
