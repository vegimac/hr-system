using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

/// <summary>
/// Lohnausweis-Angaben der Person (Swissdec Basistest 03_05, Wegleitung 2026).
/// Leer bzw. null heisst: Vorgabe der Filiale gilt. Eigene Werte überschreiben sie.
/// Nicht im easy@work-Import — nur über den eigenen Endpunkt.
/// </summary>
[Table("employee_lohnausweis")]
public class EmployeeLohnausweis
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("employee_id")]
    public int EmployeeId { get; set; }

    /// <summary>Erstes Jahr, in dem diese Fassung gilt. Bis bleibt leer, solange sie offen ist.</summary>
    [Column("gueltig_ab")]
    public int GueltigAb { get; set; } = 2000;

    /// <summary>Letztes Jahr. Leer = offen. Eine spätere Fassung setzt das auf ihr Anfangsjahr minus 1.</summary>
    [Column("gueltig_bis")]
    public int? GueltigBis { get; set; }

    /// <summary>LOHN = Buchstabe A, RENTE = Buchstabe B (Rentenbescheinigung).</summary>
    [Column("art")]
    [MaxLength(10)]
    public string Art { get; set; } = "LOHN";

    [Column("box_f")]
    public bool? BoxF { get; set; }

    [Column("box_g")]
    public bool? BoxG { get; set; }

    /// <summary>null = Text der Filiale, EIGEN = eigener Text, KEIN = kein Text.</summary>
    [Column("ziffer14_modus")]
    [MaxLength(8)]
    public string? Ziffer14Modus { get; set; }

    [Column("ziffer14_text")]
    public string? Ziffer14Text { get; set; }

    [Column("expat_modus")]
    [MaxLength(8)]
    public string? ExpatModus { get; set; }

    [Column("expat_datum", TypeName = "date")]
    public DateOnly? ExpatDatum { get; set; }

    [Column("expat_kanton")]
    [MaxLength(2)]
    public string? ExpatKanton { get; set; }

    [Column("wagen_abklaeren")]
    public bool? WagenAbklaeren { get; set; }

    [Column("wagen_minimal")]
    public bool? WagenMinimal { get; set; }

    [Column("spesen_modus")]
    [MaxLength(8)]
    public string? SpesenModus { get; set; }

    [Column("spesen_datum", TypeName = "date")]
    public DateOnly? SpesenDatum { get; set; }

    [Column("spesen_kanton")]
    [MaxLength(2)]
    public string? SpesenKanton { get; set; }

    [Column("rz52")]
    public bool? Rz52 { get; set; }

    [Column("wert_modus")]
    [MaxLength(8)]
    public string? WertModus { get; set; }

    [Column("wert_datum", TypeName = "date")]
    public DateOnly? WertDatum { get; set; }

    [Column("wert_kanton")]
    [MaxLength(2)]
    public string? WertKanton { get; set; }

    /// <summary>null = Gründe der Filiale, EIGEN = eigene Gründe, KEIN = keine.</summary>
    [Column("beteiligung_modus")]
    [MaxLength(8)]
    public string? BeteiligungModus { get; set; }

    [Column("beteiligung_gesperrt")]
    public bool BeteiligungGesperrt { get; set; }

    [Column("beteiligung_nicht_kotiert")]
    public bool BeteiligungNichtKotiert { get; set; }

    [Column("beteiligung_anwartschaft")]
    public bool BeteiligungAnwartschaft { get; set; }

    [Column("beteiligung_unecht")]
    public bool BeteiligungUnecht { get; set; }

    [Column("kinderzulage_ahv")]
    public bool KinderzulageAhv { get; set; }

    [Column("umzug_betrag", TypeName = "numeric(12,2)")]
    public decimal? UmzugBetrag { get; set; }

    [Column("umzug_jahr")]
    public int? UmzugJahr { get; set; }

    [Column("nachgenuss")]
    public bool Nachgenuss { get; set; }

    [Column("nachgenuss_betrag", TypeName = "numeric(12,2)")]
    public decimal? NachgenussBetrag { get; set; }

    [Column("nachgenuss_jahr")]
    public int? NachgenussJahr { get; set; }

    [Column("nachgenuss_name")]
    [MaxLength(100)]
    public string? NachgenussName { get; set; }

    [Column("nachgenuss_vorname")]
    [MaxLength(100)]
    public string? NachgenussVorname { get; set; }

    [Column("nachgenuss_strasse")]
    [MaxLength(120)]
    public string? NachgenussStrasse { get; set; }

    [Column("nachgenuss_plz")]
    [MaxLength(10)]
    public string? NachgenussPlz { get; set; }

    [Column("nachgenuss_ort")]
    [MaxLength(80)]
    public string? NachgenussOrt { get; set; }

    /// <summary>null = Kontakt der Filiale, EIGEN = eigene Kontaktperson, KEIN = keine.</summary>
    [Column("kontakt_modus")]
    [MaxLength(8)]
    public string? KontaktModus { get; set; }

    [Column("kontakt_org")]
    [MaxLength(200)]
    public string? KontaktOrg { get; set; }

    [Column("kontakt_strasse")]
    [MaxLength(120)]
    public string? KontaktStrasse { get; set; }

    [Column("kontakt_plz")]
    [MaxLength(10)]
    public string? KontaktPlz { get; set; }

    [Column("kontakt_ort")]
    [MaxLength(80)]
    public string? KontaktOrt { get; set; }

    [Column("kontakt_person")]
    [MaxLength(120)]
    public string? KontaktPerson { get; set; }

    [Column("kontakt_telefon")]
    [MaxLength(40)]
    public string? KontaktTelefon { get; set; }

    [Column("geaendert_am", TypeName = "timestamp without time zone")]
    public DateTime GeaendertAm { get; set; } = DateTime.Now;
}
