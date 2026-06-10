namespace HrSystem.Models;

/// <summary>
/// Zentrales Audit-Log fuer alle CRUD-Writes ueber EF Core
/// (Walter-Vorgabe 27.05.2026). Ein Eintrag pro geaenderter Entitaet
/// pro SaveChanges. Befuellt vom AuditSaveChangesInterceptor.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>UTC-Zeitstempel der Aenderung.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User-ID aus JWT (NameIdentifier-Claim). Null bei
    /// System-Aenderungen (Seed, Migrationsskripte).</summary>
    public int? UserId { get; set; }

    /// <summary>Username — denormalisiert, damit das Log auch nach
    /// User-Loeschung lesbar bleibt.</summary>
    public string? UserName { get; set; }

    /// <summary>Rolle des Users zum Zeitpunkt der Aenderung (admin/superuser/...).</summary>
    public string? UserRole { get; set; }

    /// <summary>EF-Entity-Klassenname, z.B. „Employee", „Employment".</summary>
    public string EntityType { get; set; } = "";

    /// <summary>Primary-Key-Wert als String (faengt zusammengesetzte PKs ebenfalls ab).</summary>
    public string? EntityId { get; set; }

    /// <summary>CREATE / UPDATE / DELETE</summary>
    public string Action { get; set; } = "";

    /// <summary>
    /// JSON-Object: bei CREATE/DELETE die kompletten Property-Werte,
    /// bei UPDATE pro geaendertem Property „{ field: { old, new } }".
    /// </summary>
    public string? ChangesJson { get; set; }

    /// <summary>HTTP-Route (z.B. „PUT /api/employees/123") falls bekannt.</summary>
    public string? Route { get; set; }

    /// <summary>Remote-IP des Requests falls bekannt.</summary>
    public string? IpAddress { get; set; }
}
