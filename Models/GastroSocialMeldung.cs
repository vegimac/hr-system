namespace HrSystem.Models;

/// <summary>
/// Gemerkte An- bzw. Abmeldung bei der Ausgleichskasse GastroSocial (Walter 13.10.2026).
/// Pro MA, Filiale (eigenes Abrechnungskonto) und Art; Stichtag = Eintritts- bzw. Austrittsdatum.
/// Regel, wann ein Ein-/Austritt als gemeldet gilt: <see cref="HrSystem.Services.GastroSocialMeldungRegel"/>.
/// </summary>
public class GastroSocialMeldung
{
    public const string QuelleAkis     = "AKIS";
    public const string QuelleAbgleich = "ABGLEICH";

    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }
    /// <summary>ANMELDUNG | ABMELDUNG</summary>
    public string Art { get; set; } = "";
    public DateOnly Stichtag { get; set; }
    public string Quelle { get; set; } = QuelleAkis;
    public DateTime ErfasstAm { get; set; } = DateTime.Now;
    public int? ErfasstVonUserId { get; set; }
    public string? ErfasstVon { get; set; }
}
