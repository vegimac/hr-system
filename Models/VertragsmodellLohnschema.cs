namespace HrSystem.Models;

/// <summary>
/// Lohnschema pro Vertragsmodell (Walter-Vorgabe 17.08.2026, Phase 2 des
/// Konzepts docs/lohnschema-vertragsmodelle.docx): Standard-Lohnblatt —
/// welche Lohnpositionen gehören zu welchem Vertragsmodell und wann
/// erscheinen sie. REINE STAMMDATEN/ANZEIGE — die Rechen-Engine liest das
/// Schema (noch) nicht; Steuerung ist Phase 3 (nach längerem Grün-Lauf der
/// Basen-Kontrolle).
/// Modell: FLEX | MTP | FIX | FIX-M | ALLE (gilt für jedes Modell).
/// Art:    automatisch (jeden Monat) | saldo (automatisch in den Saldo/Pott)
///         | ereignis (Krankheit, Ferienbezug …) | austritt | manuell.
/// </summary>
public class VertragsmodellLohnschema
{
    public int Id { get; set; }
    public string Modell { get; set; } = "";
    public int LohnpositionId { get; set; }
    public Lohnposition? Lohnposition { get; set; }
    public string Art { get; set; } = "automatisch";
    public int SortOrder { get; set; }
    public string? Bemerkung { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
