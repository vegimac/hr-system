namespace HrSystem.Models;

/// <summary>
/// Öffentlicher Token-Link zum Anschauen des Arbeitsvertrag-PDFs OHNE Login
/// (Walter 07.07.2026). HR erzeugt den Link im MA-Detail; der MA öffnet ihn und
/// sieht sein Vertrags-PDF inline im Browser. Der Klartext-Token steht NUR im
/// Link — in der DB liegt ausschliesslich der SHA-256-Hash. Zeitlich begrenzt
/// (ExpiresAt, 14 Tage). Mehrfach öffenbar solange gültig; UsedAt hält fest,
/// wann er das erste Mal aufgerufen wurde.
/// </summary>
public class ContractShareToken
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public int EmploymentId { get; set; }

    /// <summary>SHA-256-Hex des Einmal-Tokens.</summary>
    public string TokenHash { get; set; } = "";

    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }

    /// <summary>Erstes Öffnen der Landing-Page (Walter 07.07.2026) — UsedAt
    /// bleibt der erste PDF-Abruf.</summary>
    public DateTime? OpenedAt { get; set; }

    /// <summary>Manuell widerrufen (Walter 07.07.2026). Gesetzt beim
    /// Widerruf-Button UND automatisch beim Neuversand (alte Links desselben
    /// Vertrags werden entwertet). Widerrufene Links liefern die
    /// «ungültig»-Seite bzw. 410.</summary>
    public DateTime? RevokedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }
}
