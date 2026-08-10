namespace HrSystem.Models;

/// <summary>
/// Erst-Abruf eines Onboarding-Dokuments über den öffentlichen Vertrags-Link
/// (Walter-Vorgabe 10.08.2026): pro Token + Dokument wird das ERSTE Öffnen
/// festgehalten — Basis für die Auswertung «Onboarding-Dokumente gelesen»
/// im HR-Hub (Kachel ONBOARDING).
/// </summary>
public class ContractShareDokAbruf
{
    public int Id { get; set; }
    public int TokenId { get; set; }
    /// <summary>company_dokument.id (Kategorie ONBOARDING).</summary>
    public long DokId { get; set; }
    public DateTime AbgerufenAm { get; set; } = DateTime.Now;
}
