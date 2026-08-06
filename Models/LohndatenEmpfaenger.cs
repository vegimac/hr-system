namespace HrSystem.Models;

/// <summary>
/// Lohndatenempfänger — zentraler Katalog (Walter-Vorgabe 06.08.2026, Mirus-
/// Vorbild «Lohndatenempfänger»). Stammdaten der Kassen/Versicherungen/
/// Steuerverwaltungen werden EINMAL erfasst (Adresse, Kassennummer, Support-
/// Mail); die filial-spezifischen Angaben (Mitgliednummer, Subnummer) liegen
/// in der Zuordnung <see cref="CompanyProfileEmpfaenger"/>. Grundlage für
/// Behörden-Formulare (AHV-Anmeldung, QST) und später ELM/Swissdec.
/// </summary>
public class LohndatenEmpfaenger
{
    public int Id { get; set; }

    /// <summary>AUSGLEICHSKASSE | FAK | KTG | UVG | BVG | QST | LOHNAUSWEIS | ANDERE</summary>
    public string Art { get; set; } = "AUSGLEICHSKASSE";

    /// <summary>z.B. «AK GastroSocial», «Swica Versicherungen KTG»</summary>
    public string Bezeichnung { get; set; } = "";

    /// <summary>Zusatz-Zeile (Mirus «Zusatz», z.B. «AHV»)</summary>
    public string? Zusatz { get; set; }

    public string? UidNummer { get; set; }

    // Adresse
    public string? Strasse { get; set; }
    public string? Postfach { get; set; }
    public string? Plz { get; set; }
    public string? Ort { get; set; }
    /// <summary>Kanton-Code (AG, BE, …) — bei QST-Empfängern der Steuer-Kanton.</summary>
    public string? KantonCode { get; set; }

    /// <summary>Nummer der Kasse (z.B. «046.000») — gehört zur Kasse, nicht zur Filiale.</summary>
    public string? Kassennummer { get; set; }

    public string? SupportEmail { get; set; }
    public string? Bemerkung { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    public List<CompanyProfileEmpfaenger> Zuordnungen { get; set; } = new();
}

/// <summary>
/// Zuordnung Lohndatenempfänger ↔ Filiale mit den filial-spezifischen
/// Angaben (jede Filiale = eigene GmbH = eigene Mitgliednummer).
/// </summary>
public class CompanyProfileEmpfaenger
{
    public int Id { get; set; }

    public int CompanyProfileId { get; set; }
    public CompanyProfile? CompanyProfile { get; set; }

    public int EmpfaengerId { get; set; }
    public LohndatenEmpfaenger? Empfaenger { get; set; }

    /// <summary>Mitgliednummer der Filiale bei diesem Empfänger (z.B. «629.0714.00»).</summary>
    public string? Mitgliednummer { get; set; }

    public string? Subnummer { get; set; }
    public string? Bemerkung { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
