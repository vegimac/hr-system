using HrSystem.Models;

namespace HrSystem.Services.Vorsystem;

/// <summary>
/// Schalter für die Lohn-Simulation (Walter 04.10.2026). Scoped — gilt nur für den
/// Request, in dem <see cref="LohnSimulationService"/> rechnet. Ist er aktiv, liest
/// die <see cref="PayrollCalculationEngine"/> Vormonat, Vortrag, Jahres-Vormonate
/// und einmalige Zulagen von hier statt aus payroll_saldo / payroll_snapshot /
/// lohn_zulage und schreibt nichts (LGAV, Uniform-Depot, QST-/FamZ-Korrekturposten,
/// Darlehen bleiben aus — diese Beträge kommen aus dem Mirus-Lohnkonto).
/// </summary>
public sealed class LohnSimulationKontext
{
    public bool Aktiv { get; private set; }

    /// <summary>Jüngster simulierter Monat vor dem gerechneten (null = erster Monat → Vortrag).</summary>
    public PayrollSaldo? Vormonat { get; private set; }

    /// <summary>Simulations-Vortrag per 31.12. (Codes 901–906).</summary>
    public IReadOnlyDictionary<string, decimal> Vortrag { get; private set; } = new Dictionary<string, decimal>();

    /// <summary>Simulierte Monate desselben Jahres vor dem gerechneten (alle Filialen).</summary>
    public IReadOnlyList<SimulationLohn> VormonateJahr { get; private set; } = Array.Empty<SimulationLohn>();

    /// <summary>Einmalige Zulagen/Abzüge des Monats = Sonderzahlungen aus dem Mirus-Lohnkonto.</summary>
    public IReadOnlyList<LohnZulage> Zulagen { get; private set; } = Array.Empty<LohnZulage>();

    public void Setze(PayrollSaldo? vormonat, IReadOnlyDictionary<string, decimal> vortrag,
                      IReadOnlyList<SimulationLohn> vormonateJahr, IReadOnlyList<LohnZulage> zulagen)
    {
        Aktiv = true;
        Vormonat = vormonat;
        Vortrag = vortrag;
        VormonateJahr = vormonateJahr;
        Zulagen = zulagen;
    }

    public void Beende()
    {
        Aktiv = false;
        Vormonat = null;
        Vortrag = new Dictionary<string, decimal>();
        VormonateJahr = Array.Empty<SimulationLohn>();
        Zulagen = Array.Empty<LohnZulage>();
    }

    /// <summary>
    /// Mirus-Codes, die OneCrew nicht selbst aus Stunden, Vertrag, Absenzen und
    /// Familienzulagen rechnet, sondern als Zulage/Abzug erfasst wird. Alles andere
    /// (Festlohn, Stundenlohn, Ferien/Feiertag, 13. ML, Karenz/Taggeld, EO, Militär,
    /// Familienzulagen, SV, BVG, QST) rechnet die Engine selbst — diese Zeilen sind
    /// genau das, was der Vergleich prüft.
    /// </summary>
    public static readonly IReadOnlySet<string> SonderzahlungsCodes = new HashSet<string>(StringComparer.Ordinal)
    {
        "10.5",    // Festlohn zusätzliche Freitage
        "55.1",    // Überstunden (von Hand)
        "65.2",    // Korr. Versicherungstaggeld UVG
        "75.2",    // Korr. Versicherungstaggeld KTG
        "85.1",    // Korrektur Militärtaggeld
        "125.1",   // Korr. Mutterschaftsentschädigung
        "125.2",   // Korr. Vaterschaftsentschädigung
        "200.5",   // McBonus (13. ML a/McBonus 200.9 rechnet die Engine)
        "200.10",  // 13. Monatslohn (Zulage)
        "200.41",  // Diverse Zulagen
        "200.190", // Familienzulagen Nachzahlung
        "565.1",   // Korrektur Quellensteuer
        "595.4",   // Korrektur UVG Versicherung
        "595.5",   // Korrektur BVG Versicherung
        "600.5",   // Korrektur BVG Vorjahr
        "600.22",  // Lohnabzug Art. 337d OR
        "600.23",  // Lohnabzug Art. 337d OR
        "600.24",  // LGAV-Beitrag (in der Simulation nicht automatisch)
        "600.32",  // Uniformen-Depot (in der Simulation nicht automatisch)
        "950.1",   // Vorschuss
    };

    /// <summary>
    /// Jüngster simulierter Monat vor (jahr, monat) — egal welche Filiale, Fehler-Zeilen
    /// zählen nicht (deren Saldi sind 0, nicht gerechnet).
    /// </summary>
    public static SimulationLohn? WaehleVormonat(IEnumerable<SimulationLohn> zeilen, int jahr, int monat)
    {
        int schluessel = jahr * 100 + monat;
        return zeilen
            .Where(z => z.Fehler == null && z.Jahr * 100 + z.Monat < schluessel)
            .OrderByDescending(z => z.Jahr * 100 + z.Monat)
            .ThenByDescending(z => z.BerechnetAm)
            .FirstOrDefault();
    }

    public static PayrollSaldo AlsSaldo(SimulationLohn z) => new()
    {
        EmployeeId = z.EmployeeId,
        CompanyProfileId = z.CompanyProfileId,
        PeriodYear = z.Jahr,
        PeriodMonth = z.Monat,
        HourSaldo = z.HourSaldo,
        NachtSaldo = z.NachtSaldo,
        FerienGeldSaldo = z.FerienGeldSaldo,
        FerienTageSaldo = z.FerienTageSaldo,
        FeiertagTageSaldo = z.FeiertagTageSaldo,
        ThirteenthMonthAccumulated = z.ThirteenthAccumulated,
        GrossAmount = z.Brutto,
        NetAmount = z.Netto,
        Status = "simulation",
    };
}
