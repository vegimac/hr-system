using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HrSystem.Models;

/// <summary>
/// Lohnausweis-Vorgabe einer Filiale, gültig ab einem Jahr.
/// Bis bleibt leer, bis eine spätere Fassung sie per Ende des Vorjahres schliesst.
/// </summary>
[Table("company_lohnausweis")]
public class CompanyLohnausweis
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("company_profile_id")]
    public int CompanyProfileId { get; set; }

    [Column("gueltig_ab")]
    public int GueltigAb { get; set; } = 2000;

    [Column("gueltig_bis")]
    public int? GueltigBis { get; set; }

    [Column("box_f")]
    public bool BoxF { get; set; }

    [Column("box_g")]
    public bool BoxG { get; set; }

    [Column("pos21", TypeName = "numeric(10,2)")]
    public decimal? Pos21 { get; set; }

    [Column("ziffer14")]
    public string? Ziffer14 { get; set; }

    [Column("expat_datum", TypeName = "date")]
    public DateOnly? ExpatDatum { get; set; }

    [Column("expat_kanton")]
    [MaxLength(2)]
    public string? ExpatKanton { get; set; }

    [Column("wagen_abklaeren")]
    public bool WagenAbklaeren { get; set; }

    [Column("wagen_minimal")]
    public bool WagenMinimal { get; set; }

    [Column("spesen_datum", TypeName = "date")]
    public DateOnly? SpesenDatum { get; set; }

    [Column("spesen_kanton")]
    [MaxLength(2)]
    public string? SpesenKanton { get; set; }

    [Column("rz52")]
    public bool Rz52 { get; set; }

    [Column("wert_datum", TypeName = "date")]
    public DateOnly? WertDatum { get; set; }

    [Column("wert_kanton")]
    [MaxLength(2)]
    public string? WertKanton { get; set; }

    [Column("bet_gesperrt")]
    public bool BetGesperrt { get; set; }

    [Column("bet_nicht_kotiert")]
    public bool BetNichtKotiert { get; set; }

    [Column("bet_anwartschaft")]
    public bool BetAnwartschaft { get; set; }

    [Column("bet_unecht")]
    public bool BetUnecht { get; set; }

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
