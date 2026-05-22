namespace HrSystem.Models;

public class SocialInsuranceRate
{
    public int      Id                    { get; set; }
    public string   Code                  { get; set; } = "";   // AHV | ALV | NBUV | BVG
    public string   Name                  { get; set; } = "";   // AHV / IV / EO
    public string?  Description           { get; set; }
    public decimal  Rate                  { get; set; }          // Prozentsatz AN-Anteil
    public decimal? RateEmployer          { get; set; }          // Prozentsatz AG-Anteil (Fibu-Journal AG-Beiträge); NULL = nicht gebucht
    public string   BasisType             { get; set; } = "gross"; // gross | bvg_basis | coord_deduction
    public string?  EmploymentModelCode   { get; set; }            // NULL = alle | UTP | MTP | FIX | FIX-M
    public int?     MinAge                { get; set; }
    public int?     MaxAge                { get; set; }
    public decimal? FreibetragMonthly     { get; set; }          // AHV 65+
    public decimal? CoordinationDeduction { get; set; }          // BVG Koordinationsabzug/Mt.
    public decimal? MaxBaseMonthly        { get; set; }          // Höchstlohn/Mt. (ALV+NBU: 148'200/Jahr = 12'350/Mt.); NULL = unbegrenzt — MIT Dezember-Aufrollverfahren
    public decimal? MaxBaseFlatMonthly    { get; set; }          // Flacher Monats-Cap auf die (koordinierte) Basis, OHNE Jahresausgleich (z.B. BVG Max pfl. Betrag 5'355/Mt.); NULL = unbegrenzt
    public decimal? MinBaseMonthly        { get; set; }          // Min. pflichtige (koordinierte) Basis/Mt. (z.B. BVG 315); versicherte zahlen mind. darauf. NULL = keine Untergrenze
    public decimal? EntryThresholdYearly  { get; set; }          // Eintrittsschwelle/Jahr (z.B. BVG 22'680): wer drunter liegt, ist nicht versichert → keine Basis. NULL = keine Schwelle
    public bool     OnlyQuellensteuer     { get; set; }

    /// <summary>
    /// Fibu-Position (Mirus-Lohnart-Code) für das Fibu-Journal / den Abacus-Export
    /// (Walter-Vorgabe 22.05.2026). Verlinkt diesen SV-Satz STABIL mit dem
    /// Kontoplan (`lohn_konto_mapping.position`) — kein Text-Matching.
    /// Konvention: AHV→500, ALV→510, KTG→530, NBU/NBUV→540, BVG→550. NULL = nicht
    /// verbucht / noch nicht zugeordnet.
    /// </summary>
    public int?     FibuPosition          { get; set; }

    public DateOnly ValidFrom             { get; set; }
    public DateOnly? ValidTo              { get; set; }
    public int      SortOrder             { get; set; } = 99;
    public bool     IsActive              { get; set; } = true;
    public DateTime CreatedAt             { get; set; } = DateTime.UtcNow;
}
