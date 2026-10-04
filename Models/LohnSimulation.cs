namespace HrSystem.Models;

/// <summary>
/// Saldo-Vortrag für die Lohn-Simulation (Walter 04.10.2026): Mirus-Saldi per
/// 31.12.2025, getrennt vom echten Vortrag (lohn_zulage Kategorie «Saldo-Vortrag»),
/// damit der Vortrag des ersten echten OneCrew-Monats unberührt bleibt.
/// Codes wie der echte Vortrag: 901 Stunden, 902 Feiertag-Tage, 903 Ferien-Tage,
/// 904 Nacht, 905 Ferien-Geld CHF, 906 13. ML CHF.
/// </summary>
public class SimulationVortrag
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }
    public string Code { get; set; } = "";
    public decimal Betrag { get; set; }
    public string? Quelle { get; set; }
    public DateTime ImportiertAm { get; set; } = DateTime.Now;
}

/// <summary>
/// Ein simulierter Lohnzettel (MA × Filiale × Monat). Entsteht ausschliesslich in
/// <see cref="HrSystem.Services.Vorsystem.LohnSimulationService"/> — kein Snapshot,
/// keine Periode, kein Saldo, kein Versand. Die Saldo-Felder tragen die Kette in
/// den nächsten Simulationsmonat.
/// </summary>
public class SimulationLohn
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int CompanyProfileId { get; set; }
    public int Jahr { get; set; }
    public int Monat { get; set; }

    public decimal Brutto { get; set; }
    public decimal Netto { get; set; }
    public decimal Auszahlung { get; set; }
    public decimal SvBasisAhv { get; set; }
    public decimal SvBasisNbuv { get; set; }
    public decimal SvBasisKtg { get; set; }

    public decimal HourSaldo { get; set; }
    public decimal NachtSaldo { get; set; }
    public decimal FerienGeldSaldo { get; set; }
    public decimal FerienTageSaldo { get; set; }
    public decimal FeiertagTageSaldo { get; set; }
    public decimal ThirteenthAccumulated { get; set; }

    public string? SlipJson { get; set; }
    /// <summary>Fehlermeldung der Engine (Lohnzettel nicht rechenbar) — dann sind die Beträge 0.</summary>
    public string? Fehler { get; set; }
    /// <summary>Aus dem Mirus-Lohnkonto übernommene Sonderzahlungen, z.B. «200.5 McBonus 120.00».</summary>
    public string? Sonderzahlungen { get; set; }
    public DateTime BerechnetAm { get; set; } = DateTime.Now;
}
