using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

// ═══════════════════════════════════════════════════════════════════════
// BFS Lohnstrukturerhebung (LSE) — Walter-Vorgabe 13.08.2026.
//
// Grundlage: offizielle technische BFS-Spezifikation LSE 2024, V1.4/12.2024
// (Spalten A–S Unternehmensdaten, T–AS Mitarbeiterdaten). KEINE eigenen
// Felder/Logik erfinden; bestehende OneCrew-Daten (Employee, Employment,
// TimeEntries, PayrollSnapshot/SlipJson) verwenden. Nur LSE-spezifische
// Ergänzungsfelder werden hier separat gespeichert.
//
// Versionskonzept: lse_version hält pro Erhebungsjahr die Felddefinition
// (Codes, Wertebereiche, Pflichtfelder, Exportreihenfolge) als JSON-Konfig —
// LSE 2026 wird als neue Zeile ergänzt, ohne die Businesslogik umzubauen.
// ═══════════════════════════════════════════════════════════════════════

/// <summary>Versions-Konfiguration pro Erhebung (2024, 2026, …).</summary>
public class LseVersion
{
    public int Id { get; set; }
    /// <summary>Erhebungsjahr, z.B. 2024. surveyYear im Export.</summary>
    public int SurveyYear { get; set; }
    /// <summary>z.B. «1.4 / 12.2024» (BFS-Spezifikationsversion).</summary>
    public string? SpecVersion { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>JSON: Felddefinitionen, erlaubte Codes (education 1–8,
    /// universityDegree 1–3, position 1–5, contract 1–7, basisOfSalary 1–3),
    /// Wertebereiche (activityRate 1–175, leave 0–99, vn 756…),
    /// Pflichtfelder und Export-Spaltenreihenfolge A–AS.</summary>
    public string ConfigJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// LSE-Ergänzungsfelder pro Mitarbeiter (1:1 zu employee). NUR was OneCrew
/// nicht ohnehin führt: BFS-Ausbildung, Hochschultitel, Stellung-Override,
/// ausgeübter Beruf. Gepflegt im kleinen Bereich «BFS / Statistik»
/// (MA → Restaurant Admin), nicht prominent im Stamm.
/// </summary>
public class EmployeeLse
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>U «education»: BFS-Code 1–8 (höchste abgeschlossene Ausbildung).</summary>
    public int? Education { get; set; }
    /// <summary>V «universityDegree»: BFS-Code 1–3 — nur relevant bei Hochschul-Ausbildung.</summary>
    public int? UniversityDegree { get; set; }
    /// <summary>X «position»-Override: BFS-Code 1–5. NULL = aus lse_code_mapping
    /// (JobGroup → Stellung) ableiten.</summary>
    public int? PositionOverride { get; set; }
    /// <summary>AD «practicedProfessionOct»: Freitext max. 255 Zeichen
    /// (z.B. «Restaurantmitarbeiter», «Schichtführer»). NULL = aus
    /// JobGroup-Klartext-Vorschlag.</summary>
    public string? PracticedProfession { get; set; }
    /// <summary>AS «inHouseID» (optional, nur wenn mit BFS vereinbart).</summary>
    public string? InHouseId { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Lohnarten-Mapping: OneCrew-Lohnart (lohnposition.code bzw. SV-categoryCode)
/// → BFS-Kategorie. KEINE Auto-Zuordnung anhand des Namens — der Benutzer
/// bestätigt jede Zuordnung einmal (Confirmed), danach wird sie wiederverwendet.
/// Unbekannte Lohnarten erscheinen als «BFS-Zuordnung fehlt».
/// </summary>
public class LseLohnartMapping
{
    public int Id { get; set; }
    /// <summary>Lohnart-Code wie im SlipJson (z.B. «10.1», «600.11», «901»).</summary>
    public string LohnartCode { get; set; } = "";
    public string? Bezeichnung { get; set; }
    /// <summary>BFS-Kategorie: GRUNDLOHN | ZULAGEN | FAMILIENZULAGEN |
    /// SV_AN | BVG_AN | DREIZEHNTER | UEBERSTUNDEN | UNREGELMAESSIG |
    /// NEBENLEISTUNGEN | KAPITALLEISTUNGEN | WEITERE | NICHT_RELEVANT.</summary>
    public string? BfsKategorie { get; set; }
    public DateOnly? GueltigAb { get; set; }
    public DateOnly? GueltigBis { get; set; }
    /// <summary>Vom Benutzer kontrolliert/bestätigt.</summary>
    public bool Confirmed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Code-Mapping OneCrew → BFS für berufliche Stellung (X, «position» 1–5,
/// MappingTyp=STELLUNG, SourceCode=JobGroup-Code) und Vertragsart (Y,
/// «contract» 1–7, MappingTyp=VERTRAG, SourceCode=Vertragsmodell, bei
/// befristeten Verträgen mit Suffix «_BEFRISTET»). Keine Zuordnung = Zeile
/// gilt als fehlend und wird in der Prüfmaske markiert.
/// </summary>
public class LseCodeMapping
{
    public int Id { get; set; }
    /// <summary>STELLUNG | VERTRAG</summary>
    public string MappingTyp { get; set; } = "";
    /// <summary>z.B. «CREW», «REST_MANAGER» bzw. «FIX», «FLEX_BEFRISTET».</summary>
    public string SourceCode { get; set; } = "";
    public int? BfsCode { get; set; }
    public bool Confirmed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? UpdatedBy { get; set; }
}
