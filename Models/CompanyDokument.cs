namespace HrSystem.Models;

/// <summary>
/// Filial-Dokument (Walter-Vorgabe 06.08.2026): Dokumentenverwaltung pro
/// FILIALE — Versicherungspolicen, AHV-/Sozialversicherungs-Korrespondenz,
/// QST-Unterlagen, Verträge mit Behörden etc. Analog zu den MA-Dokumenten
/// (EmployeeDokument), aber am CompanyProfile aufgehängt. Die Datei selbst
/// liegt im Filesystem unter
///   {Documents:StoragePath}/filiale/{company_profile_id}/{storage_filename}
/// und wird NIE in der DB als BLOB gespeichert. Zugriff: admin immer; andere
/// Benutzer nur mit AppUser.CanCompanyDokumente UND user_branch_access-Eintrag
/// für die Filiale (Häkchen in der Benutzerverwaltung).
/// </summary>
public class CompanyDokument
{
    public long Id { get; set; }

    /// <summary>Filiale, zu der das Dokument gehört (Pflicht).</summary>
    public int CompanyProfileId { get; set; }

    /// <summary>
    /// Kategorie-Code (fixe Liste, KEINE eigene Verwaltungstabelle):
    /// VERSICHERUNG · AHV_SV · QST · VERTRAEGE · SONSTIGES.
    /// Labels zentral in CompanyDokumenteController.Kategorien (Backend)
    /// und CDOK_KATEGORIEN in branches-detail.js (Frontend).
    /// </summary>
    public string Kategorie { get; set; } = "SONSTIGES";

    /// <summary>Original-Dateiname beim Upload (z.B. "KTG-Police_2026.pdf").</summary>
    public string OriginalFilename { get; set; } = "";

    /// <summary>Storage-Dateiname (Guid + sanitisierte Extension), unique.</summary>
    public string StorageFilename { get; set; } = "";

    /// <summary>Optionale freie Notiz.</summary>
    public string? Bemerkung { get; set; }

    /// <summary>Klarname des Uploaders (Anzeigename, kein FK).</summary>
    public string? UploadedByName { get; set; }

    /// <summary>Lokalzeit (timestamp without time zone) — nie UTC (Walter 30.06.2026).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>"Zugriffsdatum" — zuletzt angeschaut/heruntergeladen (Lokalzeit).</summary>
    public DateTime? ZugriffAm { get; set; }

    /// <summary>Anzeigename, wer zuletzt angeschaut hat.</summary>
    public string? ZugriffVon { get; set; }
}
