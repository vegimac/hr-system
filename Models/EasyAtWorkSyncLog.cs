using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

/// <summary>
/// Protokoll-Zeile des automatischen easy@work-Stempelzeiten-Sync — pro Filiale
/// und Lauf eine Zeile (Walter-Vorgabe 19.06.2026). Damit kann der Admin den
/// Auto-Sync in der App nachvollziehen, ohne auf dem Server ins journalctl zu
/// schauen. Retention: der Job löscht Einträge älter als 90 Tage.
/// </summary>
[Table("easyatwork_sync_log")]
public class EasyAtWorkSyncLog
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_profile_id")]
    public int CompanyProfileId { get; set; }

    [Column("run_at")]
    public DateTime RunAt { get; set; } = DateTime.UtcNow;

    /// <summary>OK / BLOCKED / ERROR / SKIPPED</summary>
    [Column("status")]
    public string Status { get; set; } = "OK";

    [Column("period_from")]
    public DateOnly? PeriodFrom { get; set; }

    [Column("period_to")]
    public DateOnly? PeriodTo { get; set; }

    [Column("used_updates_feed")]
    public bool UsedUpdatesFeed { get; set; }

    [Column("inserted")]      public int Inserted { get; set; }
    [Column("updated")]       public int Updated { get; set; }
    [Column("deleted")]       public int Deleted { get; set; }
    [Column("locked_skipped")] public int LockedSkipped { get; set; }
    [Column("skipped")]       public int Skipped { get; set; }
    [Column("missing_count")] public int MissingCount { get; set; }

    [Column("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Detail der ECHTEN Änderungen dieses Laufs als JSON (Variante A,
    /// Walter-Vorgabe 20.06.2026) — Array von { employeeId, date, action
    /// ("neu"|"geaendert"), oldTotal, newTotal, oldNight, newNight }. Nur
    /// relevante Zeilen (keine identischen Neuschreibungen), gedeckelt.
    /// Wird mit der Log-Zeile nach 90 Tagen mitgelöscht.
    /// </summary>
    [Column("detail_json")]
    public string? DetailJson { get; set; }
}
