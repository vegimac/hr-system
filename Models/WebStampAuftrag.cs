namespace HrSystem.Models;

/// <summary>
/// Protokoll jeder Vorschau und Bestellung beim Webservice WebStamp
/// (Walter 24.09.2026). Bei einer Bestellung bleibt das verschickte PDF
/// in <see cref="BriefPdf"/> erhalten — damit ist später belegbar, was
/// genau an wen ging.
/// </summary>
public class WebStampAuftrag
{
    public int Id { get; set; }
    public DateTime ErstelltAm { get; set; } = DateTime.Now;   // Lokalzeit (timestamp without time zone)
    public int? ErstelltVon { get; set; }                      // app_user.id aus dem Token

    /// <summary>«test» oder «prod».</summary>
    public string Umgebung { get; set; } = "test";

    /// <summary>«vorschau» (new_order_preview, kostenlos) oder «bestellung» (new_order).</summary>
    public string Art { get; set; } = "vorschau";

    public int? EmployeeId { get; set; }
    public string Empfaenger { get; set; } = "";               // Adresszeilen, mit « · » verbunden
    public string Betreff { get; set; } = "";
    public int ProduktNummer { get; set; }
    public string? Referenz { get; set; }

    public bool Ok { get; set; }
    public int? OrderId { get; set; }
    public decimal? Preis { get; set; }
    public string? Meldung { get; set; }                       // Fehler bzw. Grund aus der Antwort

    public byte[]? BriefPdf { get; set; }                      // nur bei Bestellung
}
