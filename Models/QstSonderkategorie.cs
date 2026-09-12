namespace HrSystem.Models;

/// <summary>
/// Swissdec CategoryPredefined — Wissens-Katalog, kein ESTV-Buchstabe A–H.
/// Zuordnung am MA bleibt <see cref="EmployeeQuellensteuer.QstCode"/>.
/// Walter 11.09.2026.
/// </summary>
public class QstSonderkategorie
{
    public string Code { get; set; } = "";
    public string Gruppe { get; set; } = "";
    public string Bezeichnung { get; set; } = "";
    public string Erklaerung { get; set; } = "";
    public string Automatik { get; set; } = "";
    public string Warnung { get; set; } = "";
    public bool Kirchensteuer { get; set; }
    public string AbzugArt { get; set; } = "NULL";
    public bool NieAlsNormalerTarif { get; set; } = true;
    public int SortOrder { get; set; }
}

/// <summary>
/// Linearer Pauschalsatz der Sonderkategorie, gültig von/bis.
/// Quelle «ESTV tar25be Satzart 11» kommt aus der Tarifdatei; andere Quelle
/// = Handpflege (Import überschreibt sie nicht).
/// </summary>
public class QstSonderkategorieSatz
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Gruppe { get; set; } = "";
    public string Kanton { get; set; } = "";
    public decimal SatzPct { get; set; }
    public string Quelle { get; set; } = "";
    public DateOnly ValidFrom { get; set; } = new(2018, 1, 1);
    public DateOnly? ValidTo { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
