namespace HrSystem.Models;

/// <summary>
/// BVG-Versicherungspflicht pro Person (Walter 04.10.2026). Massgebend ist der
/// mutmassliche Jahreslohn inkl. 13. ML (BVG Art. 2 / BVV 2 Art. 3), nicht der
/// einzelne Monat. Versioniert ab/bis; ohne Eintrag rechnet der Lohn mit der
/// Monats-Schwelle wie bisher.
/// </summary>
public class EmployeeBvgPflicht
{
    public const string QuelleHand = "HAND";
    public const string QuelleVorschlag = "VORSCHLAG";
    public const string QuelleMirus = "MIRUS";

    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public bool Versichert { get; set; }
    public DateOnly GueltigAb { get; set; }
    public DateOnly? GueltigBis { get; set; }
    public string Quelle { get; set; } = QuelleHand;
    /// <summary>Geschätzter Jahreslohn zum Zeitpunkt des Entscheids (nur Info).</summary>
    public decimal? Jahreslohn { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public int? CreatedBy { get; set; }

    public bool Ueberlappt(DateOnly von, DateOnly bis) =>
        GueltigAb <= bis && (GueltigBis == null || GueltigBis >= von);
}
