using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Berechnet den möglichen Ferienanspruch-Kürzungs-Vorschlag nach Art. 329b OR.
///
/// Regeln:
///   • Unverschuldet (KRANK, UNFALL, MILITAER, ZIVILSCHUTZ):
///       Kürzung wenn > 2 Monate (60 Tage) Verhinderung im Dienstjahr.
///       Ab dem 2. vollen Monat: 1/12 pro vollem Monat.
///       Beispiel: 95 Tage Krankheit → 95-60 = 35 → ⌊35/30⌋ = 1 voller Monat → 1/12
///   • Selbstverschuldet (UNBEZ_URLAUB):
///       Kürzung wenn > 1 Monat (30 Tage) Verhinderung.
///       Pro vollem Monat ab Schwellwert: 1/12.
///   • Schwangerschaft (MUTTERSCHAFT, ohne EOG-Periode):
///       Erst ab 3. Monat (90 Tage) → 1/12 pro vollem Monat.
///
/// Dienstjahr wird ab Eintritt des Mitarbeiters gerechnet (anniversary-based).
/// </summary>
public class FerienKuerzungService
{
    private readonly AppDbContext _db;

    public FerienKuerzungService(AppDbContext db) => _db = db;

    public async Task<FerienKuerzungResult> CalculateAsync(
        int employeeId, DateOnly periodEndDate)
    {
        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee == null || !employee.EntryDate.HasValue)
            return FerienKuerzungResult.Empty();

        // Dienstjahr-Grenzen bestimmen (anniversary-based ab Eintritt)
        var hired = DateOnly.FromDateTime(employee.EntryDate.Value);
        var (jahrVon, jahrBis) = GetDienstjahr(hired, periodEndDate);

        // Alle Absenzen im Dienstjahr laden
        var absences = await _db.Absences
            .Where(a => a.EmployeeId == employeeId
                     && a.DateFrom <= jahrBis
                     && a.DateTo   >= jahrVon)
            .ToListAsync();

        // Tage pro Kategorie kumulieren (nur die in diesem Dienstjahr)
        decimal tageKrankUnfall    = 0; // KRANK + UNFALL + MILITAER + ZIVILSCHUTZ
        decimal tageUnbezUrlaub    = 0; // UNBEZ_URLAUB
        decimal tageMutterschaft   = 0; // MUTTERSCHAFT (ohne EOG-Periode — heute vereinfacht)
        foreach (var a in absences)
        {
            int t = TageInRange(a, jahrVon, jahrBis);
            if (t == 0) continue;

            decimal effTage = t * (a.Prozent > 0 ? a.Prozent / 100m : 1m);

            switch (a.AbsenceType)
            {
                case "KRANK":
                case "UNFALL":
                case "MILITAER":
                case "ZIVILSCHUTZ":
                    tageKrankUnfall += effTage;
                    break;
                case "UNBEZ_URLAUB":
                    tageUnbezUrlaub += effTage;
                    break;
                case "MUTTERSCHAFT":
                    tageMutterschaft += effTage;
                    break;
            }
        }

        // Kürzungs-Berechnung pro Kategorie
        decimal kuerzungUnverschuldet = BerechneKuerzung(tageKrankUnfall, schwellwertTage: 60);
        decimal kuerzungSelbst        = BerechneKuerzung(tageUnbezUrlaub, schwellwertTage: 30);
        decimal kuerzungSchwanger     = BerechneKuerzung(tageMutterschaft, schwellwertTage: 90);

        decimal totalKuerzung12tel = kuerzungUnverschuldet + kuerzungSelbst + kuerzungSchwanger;
        // Maximal 12/12 = 1 Jahresanspruch
        if (totalKuerzung12tel > 12m) totalKuerzung12tel = 12m;

        return new FerienKuerzungResult(
            DienstjahrVon:             jahrVon,
            DienstjahrBis:             jahrBis,
            TageKrankUnfall:           Math.Round(tageKrankUnfall, 2),
            TageUnbezUrlaub:           Math.Round(tageUnbezUrlaub, 2),
            TageMutterschaft:          Math.Round(tageMutterschaft, 2),
            KuerzungUnverschuldet12tel: kuerzungUnverschuldet,
            KuerzungSelbst12tel:        kuerzungSelbst,
            KuerzungSchwanger12tel:     kuerzungSchwanger,
            TotalKuerzung12tel:         totalKuerzung12tel
        );
    }

    /// <summary>
    /// Berechnet die 1/12-Anteile der Kürzung. Pro vollem Monat (30 Tage)
    /// über dem Schwellwert: 1/12.
    /// Beispiel: schwellwert=60, tage=95 → (95-60)/30 = 1.16... → ⌊⌋ = 1 → 1/12
    /// </summary>
    private static decimal BerechneKuerzung(decimal tage, int schwellwertTage)
    {
        if (tage <= schwellwertTage) return 0m;
        decimal ueber = tage - schwellwertTage;
        int volleMonate = (int)Math.Floor(ueber / 30m);
        return volleMonate;
    }

    private static (DateOnly von, DateOnly bis) GetDienstjahr(
        DateOnly hired, DateOnly periodEndDate)
    {
        // Dienstjahr-Anniversary: vom letzten Eintrittstag-Anniversary bis
        // zum nächsten. Beispiel: Eintritt 3.2.2007, periodEnd 25.4.2026
        //   → Dienstjahr 3.2.2026 - 2.2.2027
        int yearsSinceHired = periodEndDate.Year - hired.Year;
        var anniversary = new DateOnly(periodEndDate.Year, hired.Month,
            Math.Min(hired.Day, DateTime.DaysInMonth(periodEndDate.Year, hired.Month)));
        if (anniversary > periodEndDate)
        {
            // periodEnd liegt vor dem Anniversary dieses Jahres → Vorjahr
            anniversary = new DateOnly(periodEndDate.Year - 1, hired.Month,
                Math.Min(hired.Day, DateTime.DaysInMonth(periodEndDate.Year - 1, hired.Month)));
        }
        var jahrVon = anniversary;
        var jahrBis = anniversary.AddYears(1).AddDays(-1);
        return (jahrVon, jahrBis);
    }

    private static int TageInRange(Absence a, DateOnly von, DateOnly bis)
    {
        var start = a.DateFrom > von ? a.DateFrom : von;
        var end   = a.DateTo   < bis ? a.DateTo   : bis;
        if (end < start) return 0;
        return end.DayNumber - start.DayNumber + 1;
    }
}

public record FerienKuerzungResult(
    DateOnly DienstjahrVon,
    DateOnly DienstjahrBis,
    decimal TageKrankUnfall,
    decimal TageUnbezUrlaub,
    decimal TageMutterschaft,
    decimal KuerzungUnverschuldet12tel,
    decimal KuerzungSelbst12tel,
    decimal KuerzungSchwanger12tel,
    decimal TotalKuerzung12tel)
{
    public static FerienKuerzungResult Empty() => new(
        default, default, 0, 0, 0, 0, 0, 0, 0);

    public bool HasKuerzungVorschlag => TotalKuerzung12tel > 0;
}
