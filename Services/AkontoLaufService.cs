using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Akonto-Lohn — Berechnung der Vorauszahlung pro MA nach Walter's 6-Regel-Werk
/// (Walter-Vorgabe 16.05.2026, Etappe 5):
///
///   Regel 1: kein Akonto wenn Vertragsende ≤ Periodenende
///   Regel 2: kein Akonto bei Krankheit/Unfall/Mutterschaft AM Stichtag
///   Regel 3: FIX     → AkontoProzentFix × Definitiv-Auszahlung
///   Regel 4: FIX-M   → wie Regel 3
///   Regel 5: MTP     → AkontoProzentHourly × (Stunden × Rate + Ferien-Pott − SV-Abzüge)
///   Regel 6: UTP     → wie Regel 5
///
/// Ferien-Pott (Regel 5/6, Walter 16.05.2026): nur Ferien-Bezüge die BIS zum
/// Akonto-Stichtag vollständig abgeschlossen sind (DateTo ≤ Stichtag) werden
/// anteilsmässig aus dem Pott ausbezahlt — siehe <see cref="FerienAuszahlungService"/>.
/// Ferien-Perioden die über den Stichtag hinausragen werden komplett ignoriert
/// und im Definitivlauf am Monatsende nachverrechnet.
///
/// FIX/FIX-M (Regel 3/4) werden in dieser Service nur grob geschätzt (Monatslohn
/// als Brutto-Proxy) — die exakte Korrektur erfolgt im Frontend via
/// <c>POST /api/akonto/workflow/sync-fix-from-slip</c>, das den Wert auf Basis
/// des echten Definitivlauf-Slips überschreibt.
///
/// Der Service ist read-only — schreibt nichts. Das Commit-Schreiben der
/// akonto_zahlung-Datensätze passiert im AkontoWorkflowController.
/// </summary>
public class AkontoLaufService
{
    private readonly AppDbContext _db;
    public AkontoLaufService(AppDbContext db) => _db = db;

    public record AkontoRowDto(
        int     EmployeeId,
        string? EmployeeNumber,
        string  FirstName,
        string  LastName,
        string  EmploymentModel,
        decimal? EmploymentPercentage,
        decimal GeschaetzterBrutto,
        decimal GeschaetzteAbzuege,
        decimal NettoVorPfaendung,
        decimal PfaendungAbzug,
        decimal NettoAkonto,
        string  BruttoErlaeuterung,
        bool    IsEligible,
        string? AusschlussGrund,
        bool    HasPfaendung);

    public record AkontoVorschauResponse(
        int     Year,
        int     Month,
        string  Stichtag,            // yyyy-MM-dd
        string  PeriodFrom,
        string  PeriodTo,
        string? PayoutDate,          // aus akonto_termin, falls hinterlegt
        decimal AkontoProzentFix,    // % der Filiale für FIX/FIX-M
        int     CountEligible,
        int     CountExcluded,
        decimal TotalNetto,
        List<AkontoRowDto> Rows);

    public async Task<AkontoVorschauResponse> PreviewAsync(
        int companyProfileId, int year, int month, DateOnly stichtag)
    {
        var profile = await _db.CompanyProfiles.FindAsync(companyProfileId)
            ?? throw new InvalidOperationException("Filiale nicht gefunden.");

        var periodFrom = new DateOnly(year, month, 1);
        var periodTo   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Stichtag in der Periode normalisieren — wenn ausserhalb, auf Periodenende setzen.
        if (stichtag < periodFrom) stichtag = periodFrom;
        if (stichtag > periodTo)   stichtag = periodTo;

        // MA der Filiale mit aktivem Employment. Phantom-MA (IsPayrollExcluded=true,
        // z.B. Supervisor mit easy@work-Zugang ohne Lohn) bewusst ausschliessen —
        // sie kriegen NIE einen Akonto (Walter-Vorgabe 16.05.2026).
        var employees = await _db.Employees
            .Include(e => e.Employments)
            .Where(e => !e.IsPayrollExcluded
                     && e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId && emp.IsActive))
            .ToListAsync();
        var empIds = employees.Select(e => e.Id).ToList();

        // SV-Sätze gültig am Stichtag, dedupliziert nach fachlichem Schlüssel
        // (latest-ValidFrom gewinnt). QST-only und inaktive sind ausgeschlossen.
        var allSv = await _db.SocialInsuranceRates
            .Where(r => r.IsActive
                     && !r.OnlyQuellensteuer
                     && r.ValidFrom <= stichtag
                     && (r.ValidTo == null || r.ValidTo >= stichtag))
            .ToListAsync();
        var svRates = allSv
            .GroupBy(r => new {
                r.Code, r.MinAge, r.MaxAge,
                EmpModel = r.EmploymentModelCode ?? "",
                r.BasisType
            })
            .Select(g => g.OrderByDescending(r => r.ValidFrom).First())
            .ToList();

        // Absenzen, Stempelzeiten, Pfändungen vorladen
        var absences = await _db.Absences
            .Where(a => empIds.Contains(a.EmployeeId)
                     && a.DateFrom <= periodTo
                     && a.DateTo   >= periodFrom)
            .ToListAsync();
        var timeEntries = await _db.EmployeeTimeEntries
            .Where(t => empIds.Contains(t.EmployeeId)
                     && t.EntryDate >= periodFrom
                     && t.EntryDate <= stichtag)
            .ToListAsync();
        var assignments = await _db.EmployeeLohnAssignments
            .Where(la => empIds.Contains(la.EmployeeId)
                      && la.ValidFrom <= periodTo
                      && (la.ValidTo == null || la.ValidTo >= periodFrom))
            .ToListAsync();

        var akontoTermin = await _db.AkontoTermine
            .FirstOrDefaultAsync(t => t.CompanyProfileId == companyProfileId
                                    && t.Year == year && t.Month == month);

        // Vormonat-PayrollSaldos (Walter Regel 5/6): brauchen wir für die
        // Ferien-Pott-Berechnung bei UTP/MTP. Wir nehmen pro MA den jüngsten
        // Saldo vor der aktuellen Periode — egal welche Filiale, weil ein MA
        // typischerweise nur in einer Filiale Lohn bekommt (Phantom-MA sind
        // schon oben gefiltert). Bei Periode 01/2026 → letzte Saldo aus 12/2025.
        var refKey = year * 12 + month;
        var allSaldos = await _db.PayrollSaldos
            .Where(s => empIds.Contains(s.EmployeeId))
            .ToListAsync();
        var lastSaldoByEmp = allSaldos
            .Where(s => s.PeriodYear * 12 + s.PeriodMonth < refKey)
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key,
                          g => g.OrderByDescending(s => s.PeriodYear * 12 + s.PeriodMonth).First());

        var rows = new List<AkontoRowDto>();
        foreach (var e in employees)
        {
            // Vertrag muss in der Lohnperiode tatsächlich gültig sein
            // (Walter-Bug 16.05.2026: bisher reichte IsActive — MA mit Vertrag
            // der erst im Folgemonat beginnt oder schon vor der Periode endete
            // bekamen einen Akonto, obwohl die Lohnzettel-Vorschau "kein
            // gültiger Vertrag in der Periode" sagte).
            var emp = e.Employments
                .Where(x => x.CompanyProfileId == companyProfileId && x.IsActive)
                .Where(x => DateOnly.FromDateTime(x.ContractStartDate) <= periodTo)
                .Where(x => !x.ContractEndDate.HasValue
                         || DateOnly.FromDateTime(x.ContractEndDate.Value) >= periodFrom)
                .OrderByDescending(x => x.ContractStartDate)
                .FirstOrDefault();
            if (emp is null) continue;     // MA hat in dieser Periode keinen Vertrag → kein Akonto-Datensatz

            lastSaldoByEmp.TryGetValue(e.Id, out var lastSaldo);

            rows.Add(BuildRow(e, emp, profile, stichtag, periodFrom, periodTo,
                              svRates, absences, timeEntries, assignments, lastSaldo));
        }

        // Sortierung nach Vorname (CLAUDE.md-Konvention für alle MA-Listen,
        // Walter-Vorgabe 16.05.2026); Tie-Break über Nachname.
        rows = rows
            .OrderBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LastName,   StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AkontoVorschauResponse(
            year, month,
            stichtag.ToString("yyyy-MM-dd"),
            periodFrom.ToString("yyyy-MM-dd"),
            periodTo.ToString("yyyy-MM-dd"),
            akontoTermin?.PayoutDate.ToString("yyyy-MM-dd"),
            profile.AkontoProzentFix,
            rows.Count(r => r.IsEligible),
            rows.Count(r => !r.IsEligible),
            rows.Sum(r => r.NettoAkonto),
            rows);
    }

    // ── Pro-MA-Berechnung ───────────────────────────────────────────────────

    private AkontoRowDto BuildRow(
        Employee e, Employment emp, CompanyProfile profile,
        DateOnly stichtag, DateOnly periodFrom, DateOnly periodTo,
        List<SocialInsuranceRate> svRates,
        List<Absence> absences,
        List<EmployeeTimeEntry> timeEntries,
        List<EmployeeLohnAssignment> assignments,
        PayrollSaldo? lastSaldo)
    {
        var model = (emp.EmploymentModel ?? "").ToUpperInvariant();

        // 1) Eligibility (Regeln 1 + 2)
        var (isEligible, reason) = CheckEligibility(e, emp, stichtag, periodFrom, periodTo, absences);

        // Pfändung-Datensatz (auch wenn nicht eligible, fürs Anzeigen).
        var assignment = assignments.FirstOrDefault(la => la.EmployeeId == e.Id);

        if (!isEligible)
        {
            return new AkontoRowDto(
                e.Id, e.EmployeeNumber, e.FirstName ?? "", e.LastName ?? "",
                model, emp.EmploymentPercentage,
                0m, 0m, 0m, 0m, 0m, "",
                false, reason, assignment != null);
        }

        // 2) Brutto-Schätzung pro Modell
        decimal brutto;
        string  bruttoErlaeuterung;
        switch (model)
        {
            case "UTP":
            case "MTP":
                // Regel 5/6: Stunden bis Stichtag × Rate + Ferien-Pott (nur abgeschlossene Bezüge)
                (brutto, bruttoErlaeuterung) = ComputeBruttoHourly(
                    e, emp, periodFrom, stichtag, timeEntries, absences, lastSaldo);
                break;
            case "FIX":
            case "FIX-M":
                // Regel 3/4: grobe Schätzung über Monatslohn. Exakte Korrektur
                // erfolgt via Frontend-Trigger /sync-fix-from-slip (PayrollController
                // liefert dort die echte voraussichtliche Auszahlung).
                brutto = emp.MonthlySalary ?? emp.MonthlySalaryFte ?? 0m;
                bruttoErlaeuterung = $"Monatslohn CHF {brutto:0.00} (Vorschätzung, wird via Slip-Sync korrigiert)";
                break;
            default:
                brutto = 0m;
                bruttoErlaeuterung = $"Unbekanntes Modell '{model}'";
                break;
        }

        // 3) Abzüge (SV + BVG, kein QST)
        int? age = AgeAt(e.DateOfBirth, stichtag);
        decimal abzuege = ComputeDeductions(brutto, svRates, model, age);

        // 4) Netto-Vorschlag — AkontoProzent je nach Modell-Familie
        //    Regel 3/4: AkontoProzentFix (Default 80%)
        //    Regel 5/6: AkontoProzentHourly (Default 100%)
        decimal nettoVoll = brutto - abzuege;
        decimal factor = (model == "FIX" || model == "FIX-M")
            ? Math.Clamp(profile.AkontoProzentFix,    0m, 100m) / 100m
            : Math.Clamp(profile.AkontoProzentHourly, 0m, 100m) / 100m;
        decimal nettoVor = nettoVoll * factor;

        // 5) Auf CHF 10 abrunden, untere Grenze 0
        nettoVor = Math.Floor(nettoVor / 10m) * 10m;
        if (nettoVor < 0m) nettoVor = 0m;

        // 6) Pfändungs-Cap
        decimal pfaendungAbzug = 0m;
        decimal nettoAkonto    = nettoVor;
        bool hasPfaendung = assignment != null;
        if (assignment != null)
        {
            if (assignment.Freigrenze <= 0m)
            {
                pfaendungAbzug = nettoVor;
                nettoAkonto    = 0m;
                isEligible     = false;
                reason         = "Lohnpfändung — Freigrenze 0";
            }
            else if (nettoVor > assignment.Freigrenze)
            {
                pfaendungAbzug = nettoVor - assignment.Freigrenze;
                nettoAkonto    = assignment.Freigrenze;
            }
        }

        return new AkontoRowDto(
            e.Id, e.EmployeeNumber, e.FirstName ?? "", e.LastName ?? "",
            model, emp.EmploymentPercentage,
            Math.Round(brutto, 2),
            Math.Round(abzuege, 2),
            Math.Round(nettoVor, 2),
            Math.Round(pfaendungAbzug, 2),
            Math.Round(nettoAkonto, 2),
            bruttoErlaeuterung,
            isEligible,
            reason,
            hasPfaendung);
    }

    // ── Eligibility ────────────────────────────────────────────────────────

    private static (bool ok, string? reason) CheckEligibility(
        Employee e, Employment emp,
        DateOnly stichtag, DateOnly periodFrom, DateOnly periodTo,
        List<Absence> absences)
    {
        // Probezeit aktiv (ProbationEndDate liegt am oder nach dem Stichtag).
        if (emp.ProbationEndDate.HasValue
            && DateOnly.FromDateTime(emp.ProbationEndDate.Value) >= stichtag)
            return (false, "in Probezeit");

        // Regel 1 (Walter 16.05.2026): kein Akonto wenn Vertragsende ≤ Periodenende.
        // ExitDate am Employee oder ContractEndDate am Employment — was zuerst kommt.
        DateOnly? exit = e.ExitDate.HasValue
            ? DateOnly.FromDateTime(e.ExitDate.Value)
            : (emp.ContractEndDate.HasValue
                ? DateOnly.FromDateTime(emp.ContractEndDate.Value)
                : (DateOnly?)null);
        if (exit.HasValue && exit.Value <= periodTo)
            return (false, $"Austritt {exit.Value:dd.MM.yyyy}");

        // Krankheit/Unfall/Mutterschaft AM STICHTAG aktiv (Walter-Vorgabe
        // 15.05.2026): nur ausschliessen wenn die Absenz zum Auszahlungs-
        // Zeitpunkt noch läuft — kurze Absenzen vor dem Stichtag (z.B.
        // 1-Tages-Krank Anfang Monat) hindern den Akonto NICHT.
        var k = absences.FirstOrDefault(a => a.EmployeeId == e.Id
                                          && a.AbsenceType == "KRANK"
                                          && a.DateFrom <= stichtag && a.DateTo >= stichtag);
        if (k != null) return (false, $"Krank am Stichtag ({k.DateFrom:dd.MM.}–{k.DateTo:dd.MM.})");
        var u = absences.FirstOrDefault(a => a.EmployeeId == e.Id
                                          && a.AbsenceType == "UNFALL"
                                          && a.DateFrom <= stichtag && a.DateTo >= stichtag);
        if (u != null) return (false, $"Unfall am Stichtag ({u.DateFrom:dd.MM.}–{u.DateTo:dd.MM.})");
        var m = absences.FirstOrDefault(a => a.EmployeeId == e.Id
                                          && a.AbsenceType == "MUTT_VATER"
                                          && a.DateFrom <= stichtag && a.DateTo >= stichtag);
        if (m != null) return (false, $"Mutter/Vater am Stichtag ({m.DateFrom:dd.MM.}–{m.DateTo:dd.MM.})");

        return (true, null);
    }

    // ── Brutto-Schätzung UTP/MTP (Walter Regel 5/6, 16.05.2026) ────────────
    //
    // Brutto = gestempelte Stunden bis Stichtag × HourlyRate
    //        + Ferien-Auszahlung aus Pott (nur abgeschlossene Bezüge ≤ Stichtag)
    //
    // Ferien-Pott (FerienAuszahlungService) berücksichtigt:
    //   • Vormonats-Feriengeld-Saldo (PayrollSaldo) + Akkumulation diesen Monat
    //   • Vormonats-Tage-Saldo + Tage-Accrual diesen Monat
    //   • Tagessatz × bezogene Tage = Auszahlung, gedeckelt auf Pott CHF
    //
    // KEIN Voll-Tagessatz mehr (alter Bug: 6 Tage × WeeklyH/5 × HourlyRate
    // überschätzte den Definitiv-Tagessatz; jetzt wird der echte Pott-Ø-Satz
    // verwendet → Akonto kann nie höher sein als der Definitivlohn).
    private static (decimal Brutto, string Erlaeuterung) ComputeBruttoHourly(
        Employee e, Employment emp,
        DateOnly periodFrom, DateOnly stichtag,
        List<EmployeeTimeEntry> timeEntries,
        List<Absence> absences,
        PayrollSaldo? lastSaldo)
    {
        decimal hourly = emp.HourlyRate ?? 0m;
        decimal hours = (decimal)timeEntries
            .Where(t => t.EmployeeId == e.Id)
            .Sum(t => (double)(t.TotalHours ?? t.DurationHours ?? 0m));
        decimal bruttoStunden = hours * hourly;

        // Ferien-Pott (nur abgeschlossene Bezüge bis Stichtag) ──────────────
        var empAbsences = absences.Where(a => a.EmployeeId == e.Id).ToList();
        decimal bezogeneTage = FerienAuszahlungService
            .SumAbgeschlosseneFerientageBisStichtag(empAbsences, periodFrom, stichtag);

        decimal vacationPct = emp.VacationPercent ?? 10.64m;
        int vacationWeeks   = vacationPct >= 12.5m ? 6 : 5;
        decimal vormonatChf  = lastSaldo?.FerienGeldSaldo ?? 0m;
        decimal vormonatTage = lastSaldo?.FerienTageSaldo ?? 0m;
        decimal accrualChf  = Math.Round(bruttoStunden * vacationPct / 100m, 2);
        decimal accrualTage = Math.Round(vacationWeeks * 7m / 12m, 4);

        var pott = FerienAuszahlungService.Compute(
            vormonatChf, accrualChf, vormonatTage, accrualTage, bezogeneTage);

        decimal brutto = bruttoStunden + pott.AuszahlungChf;
        string s = $"{hours:0.00}h × CHF {hourly:0.00} = CHF {bruttoStunden:0.00}"
                 + (pott.AuszahlungChf > 0
                     ? $" + Ferien-Pott {pott.BezogeneTage:0.00} Tg × Ø CHF {pott.Tagessatz:0.00} = CHF {pott.AuszahlungChf:0.00}"
                     : "");
        return (brutto, s);
    }

    // ── Abzüge-Schätzung ────────────────────────────────────────────────────
    // Schleife über die effektiv gültigen SV-Sätze. Filter:
    //   • EmploymentModelCode null = alle Modelle, sonst gleich.
    //   • Alters-Filter: MinAge/MaxAge (falls am MA Geburtsdatum hinterlegt).
    // BasisType:
    //   • gross           → Rate × max(brutto − FreibetragMonthly, 0)
    //   • bvg_basis       → Rate × max(brutto − CoordinationDeduction, 0)
    //   • coord_deduction → Rate × CoordinationDeduction (Kaderlösung)
    private static decimal ComputeDeductions(
        decimal brutto, List<SocialInsuranceRate> svRates, string modelCode, int? age)
    {
        if (brutto <= 0m) return 0m;
        decimal total = 0m;
        foreach (var r in svRates)
        {
            if (!string.IsNullOrEmpty(r.EmploymentModelCode)
                && !r.EmploymentModelCode.Equals(modelCode, StringComparison.OrdinalIgnoreCase))
                continue;
            if (r.MinAge.HasValue && age.HasValue && age.Value < r.MinAge.Value) continue;
            if (r.MaxAge.HasValue && age.HasValue && age.Value > r.MaxAge.Value) continue;

            decimal basis;
            switch (r.BasisType)
            {
                case "bvg_basis":
                {
                    var koord = r.CoordinationDeduction ?? 0m;
                    basis = brutto > koord ? brutto - koord : 0m;
                    break;
                }
                case "coord_deduction":
                    basis = r.CoordinationDeduction ?? 0m;
                    break;
                default:    // "gross"
                {
                    var freibetrag = r.FreibetragMonthly ?? 0m;
                    basis = brutto > freibetrag ? brutto - freibetrag : 0m;
                    break;
                }
            }
            total += basis * (r.Rate / 100m);
        }
        return total;
    }

    private static int? AgeAt(DateTime? dob, DateOnly stichtag)
    {
        if (!dob.HasValue) return null;
        var d = DateOnly.FromDateTime(dob.Value);
        int age = stichtag.Year - d.Year;
        if (stichtag.Month < d.Month || (stichtag.Month == d.Month && stichtag.Day < d.Day)) age--;
        return age;
    }
}
