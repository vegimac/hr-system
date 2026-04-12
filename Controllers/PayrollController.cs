using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/payroll")]
public class PayrollController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly QuellensteuerTarifService _tarifService;

    public PayrollController(AppDbContext db, QuellensteuerTarifService tarifService)
    {
        _db          = db;
        _tarifService = tarifService;
    }

    // GET /api/payroll/calculate?employeeId=X&year=Y&month=M&companyProfileId=Z
    [HttpGet("calculate")]
    public async Task<IActionResult> Calculate(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
    {
        // ── Stammdaten laden ───────────────────────────────────────────────
        var employee = await _db.Employees
            .Include(e => e.Employments)
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null) return NotFound("Mitarbeiter nicht gefunden.");

        var emp = employee.Employments
            .Where(e => e.IsActive && e.CompanyProfileId == companyProfileId)
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefault();
        if (emp is null) return NotFound("Kein aktives Anstellungsverhältnis gefunden.");

        var company = await _db.CompanyProfiles.FindAsync(companyProfileId);
        if (company is null) return NotFound("Filiale nicht gefunden.");

        // ── Lohnperiode berechnen ──────────────────────────────────────────
        int startDay = company.PayrollPeriodStartDay ?? 1;
        var (periodFrom, periodTo) = CalcPeriod(startDay, year, month);

        // ── Stempelzeiten laden ────────────────────────────────────────────
        var timeEntries = await _db.EmployeeTimeEntries
            .Where(t => t.EmployeeId == employeeId
                     && t.EntryDate >= periodFrom
                     && t.EntryDate <= periodTo)
            .ToListAsync();

        // ── Absenzen laden ────────────────────────────────────────────────
        var absences = await _db.Absences
            .Where(a => a.EmployeeId == employeeId
                     && a.DateFrom <= periodTo
                     && a.DateTo   >= periodFrom)
            .ToListAsync();

        // ── Absenz-Typen Konfiguration laden (Zeitgutschrift-Regeln) ─────
        var absenzTypConfig = await _db.AbsenzTypen
            .Where(t => t.Aktiv)
            .ToDictionaryAsync(t => t.Code, t => t);
        // Fallback-Konfiguration falls Tabelle noch leer (Backward-Compatibility)
        AbsenzTyp GetAbsenzTyp(string code) => absenzTypConfig.TryGetValue(code, out var t) ? t
            : new AbsenzTyp { Code = code, Zeitgutschrift = code != "FEIERTAG", GutschriftModus = code == "FERIEN" ? "1/7" : "1/5" };

        // ── Mitarbeiter-Alter berechnen (für BVG, AHV-Schwellen) ─────────────
        // Schweizer Regel: Beitragspflicht gilt ab 1.1. des Jahres, in dem das
        // massgebende Alter erreicht wird — unabhängig vom genauen Geburtstagsdatum.
        // Daher: Alter = Lohnperioden-Jahr − Geburtsjahr (kein Monats-Adjustment).
        int? employeeAge = null;
        if (employee.DateOfBirth.HasValue)
            employeeAge = year - employee.DateOfBirth.Value.Year;

        // ── Quellensteuer-Pflicht ──────────────────────────────────────────────
        // Regel: QST-pflichtig wenn Nationalität ≠ CH UND noch nicht befreit.
        // «Befreit ab»: gesetzt wenn MA C-Ausweis oder CH-Bürgerrecht erhält.
        bool isSchweizer = string.Equals(
            employee.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase);
        bool bereitsBefreit = employee.QuellensteuerBefreitAb.HasValue
            && employee.QuellensteuerBefreitAb.Value <= periodFrom;
        bool isQuellensteuer = !isSchweizer && !bereitsBefreit;

        // ── Quellensteuer-Einstellungen des Mitarbeiters laden ────────────────
        EmployeeQuellensteuer? qstEinstellung = null;
        if (isQuellensteuer)
        {
            qstEinstellung = await _db.EmployeeQuellensteuer
                .Where(q => q.EmployeeId == employeeId
                         && q.ValidFrom  <= periodFrom
                         && (q.ValidTo == null || q.ValidTo >= periodFrom))
                .OrderByDescending(q => q.ValidFrom)
                .FirstOrDefaultAsync();
        }

        // ── Abzugsregeln laden (nach Alter + QST-Status filtern) ─────────────
        var allRules = await _db.DeductionRules
            .Where(r => r.CompanyProfileId == companyProfileId
                     && r.IsActive
                     && r.ValidFrom <= periodTo
                     && (r.ValidTo == null || r.ValidTo >= periodFrom)
                     && r.CategoryCode != "195") // Zulagen sind keine Abzüge
            .OrderBy(r => r.CategoryCode)
            .ThenBy(r => r.SortOrder)
            .ToListAsync();

        // Fallback: wenn keine Regeln konfiguriert → Schweizer Standard 2026 verwenden
        bool usingDefaultDeductions = false;
        if (!allRules.Any())
        {
            allRules = BuildSwissStandardDeductions(companyProfileId);
            usingDefaultDeductions = true;
        }

        // Altersfilter + QST-Filter in Memory anwenden
        var deductions = allRules
            .Where(r => (r.MinAge == null || employeeAge == null || employeeAge >= r.MinAge)
                     && (r.MaxAge == null || employeeAge == null || employeeAge <= r.MaxAge)
                     && (!r.OnlyQuellensteuer || isQuellensteuer))
            .ToList();

        // ── Vormonat-Saldo ─────────────────────────────────────────────────
        var (prevYear, prevMonth) = PrevPeriod(year, month);
        var prevSaldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId
                                   && s.PeriodYear  == prevYear
                                   && s.PeriodMonth == prevMonth);
        decimal vormonatHourSaldo    = prevSaldo?.HourSaldo        ?? 0;
        decimal vormonatNachtSaldo   = prevSaldo?.NachtSaldo       ?? 0;
        decimal vormonatFerienGeld   = prevSaldo?.FerienGeldSaldo  ?? 0;
        decimal vormonatFerienTage   = prevSaldo?.FerienTageSaldo  ?? 0;
        decimal prevThirteenth       = prevSaldo?.ThirteenthMonthAccumulated ?? 0;

        // ── Berechnung je Modell ───────────────────────────────────────────
        var isMTP = emp.EmploymentModel == "MTP";
        var isUTP = emp.EmploymentModel == "UTP";
        var isFIX = emp.EmploymentModel is "FIX" or "FIX-M";

        decimal hourlyRate    = emp.HourlyRate      ?? 0;
        decimal vacationPct   = emp.VacationPercent  ?? 0;
        decimal holidayPct    = emp.HolidayPercent   ?? 0;
        decimal thirteenthPct = emp.ThirteenthSalaryPercent ?? 0;

        // Tatsächlich gestempelte Stunden (exkl. NACHT_KOMP-Gutschriften)
        decimal workedHours = timeEntries.Sum(t => t.TotalHours ?? 0);

        // Nachtstunden dieser Periode
        decimal nightHours = timeEntries.Sum(t => t.NightHours ?? 0);

        // Nacht-Zeitzuschlag: 10% der Nachtstunden → Saldo-Zuwachs
        decimal nightBonus = Math.Round(nightHours * 0.10m, 2);

        // Absenz-Gutschriften
        decimal absenzGutschrift  = 0;
        decimal feiertagStunden   = 0;   // ausbezahlte Feiertage
        decimal nachtKompStunden  = 0;   // Nacht-Kompensationstage (reduzieren NachtSaldo)

        foreach (var a in absences)
        {
            // HoursCredited enthält bereits die Gesamtstunden der Absenz
            decimal hours = a.HoursCredited;
            var typCfg = GetAbsenzTyp(a.AbsenceType);

            if (a.AbsenceType == "NACHT_KOMP")
            {
                // Nacht-Ruhetag: gilt für alle Modelle inkl. UTP, immer Gutschrift
                nachtKompStunden += hours;
                absenzGutschrift += hours;
            }
            else if (isUTP)
            {
                // UTP: keine automatische Zeitgutschrift (Businessregel, unabhängig von Konfiguration).
                // Ausnahme NACHT_KOMP: bereits oben behandelt.
                // → nichts tun
            }
            else if (isMTP && a.AbsenceType == "FERIEN")
            {
                // MTP: Ferien = kein Stunden-Saldo-Effekt (Feriengeld wird separat akkumuliert)
            }
            else if (a.AbsenceType == "FEIERTAG" || (!typCfg.Zeitgutschrift))
            {
                // Feiertag (ausbezahlt) oder Typ ohne Zeitgutschrift: separat ausbezahlen
                feiertagStunden += hours;
            }
            else if (typCfg.Zeitgutschrift)
            {
                // Alle anderen Typen mit Zeitgutschrift (KRANK, UNFALL, SCHULUNG, MILITAER etc.)
                absenzGutschrift += hours;
            }
        }

        // ── Ferien-Tage-Saldo (alle Modelle) ──────────────────────────────
        // 5 Wochen = 35 Tage/Jahr (vacationPct < ~12.5%)
        // 6 Wochen = 42 Tage/Jahr (vacationPct >= 12.5%)
        int     vacationWeeks       = vacationPct >= 12.5m ? 6 : 5;
        decimal annualFerienTage    = vacationWeeks * 7m;
        decimal ferienTageAccrual   = Math.Round(annualFerienTage / 12m, 4); // monatliche Gutschrift

        // Tatsächlich bezogene Ferientage aus FERIEN-Absenzen
        decimal ferienTageGenommen = 0;
        foreach (var a in absences.Where(x => x.AbsenceType == "FERIEN"))
        {
            if (!string.IsNullOrEmpty(a.WorkedDays))
            {
                try
                {
                    var dayArr = System.Text.Json.JsonSerializer.Deserialize<string[]>(a.WorkedDays);
                    ferienTageGenommen += dayArr?.Length ?? 0;
                }
                catch { ferienTageGenommen += 1; }
            }
            else
            {
                // Fallback: Kalendertage (inkl. Wochenenden, wie Abacus)
                ferienTageGenommen += (decimal)(a.DateTo.ToDateTime(TimeOnly.MinValue)
                                     - a.DateFrom.ToDateTime(TimeOnly.MinValue)).TotalDays + 1;
            }
        }
        decimal ferienTageSaldoNeu = Math.Round(vormonatFerienTage + ferienTageAccrual - ferienTageGenommen, 4);

        // ── Ferien-Geld-Saldo (nur UTP + MTP) ─────────────────────────────
        // Ferienentschädigung wird NICHT monatlich ausbezahlt, sondern akkumuliert.
        // Bei Ferienbezug: proportionale Auszahlung (Tage genommen / Saldo vorher)
        decimal ferienGeldSaldoNeu   = vormonatFerienGeld;  // wird unten angepasst
        decimal ferienGeldAuszahlung = 0;
        // Wird nach Modell-Berechnung gesetzt (ferienEnt ist modellabhängig)

        // ── Zulagen & Abzüge für diese Periode laden ──────────────────────
        string periodeStr = $"{year:D4}-{month:D2}";
        var zulagenEntries = await _db.LohnZulagen
            .Include(z => z.Typ)
            .Where(z => z.EmployeeId == employeeId && z.Periode == periodeStr)
            .OrderBy(z => z.Typ!.SortOrder)
            .ThenBy(z => z.CreatedAt)
            .ToListAsync();

        // SV-pflichtige Zulagen → werden zu totalLohn addiert (fliessen in AHV/ALV/QST-Basis ein)
        var zulagenSvLines  = new List<object>();
        decimal zulagenSvTotal = 0;
        foreach (var z in zulagenEntries.Where(z => z.Typ!.Typ == "ZULAGE" && z.Typ.SvPflichtig))
        {
            decimal b = Round05(z.Betrag);
            zulagenSvLines.Add(new { bezeichnung = z.Typ!.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""),
                                     anzahl = (decimal?)null, prozent = (decimal?)null, basis = (decimal?)null, betrag = b });
            zulagenSvTotal += b;
        }

        // Nicht-SV-pflichtige Zulagen → separate Zeilen nach Nettolohn
        var zulagenExtraLines = new List<object>();
        decimal zulagenExtraTotal = 0;
        foreach (var z in zulagenEntries.Where(z => z.Typ!.Typ == "ZULAGE" && !z.Typ.SvPflichtig))
        {
            decimal b = Round05(z.Betrag);
            zulagenExtraLines.Add(new { bezeichnung = z.Typ!.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""), betrag = b });
            zulagenExtraTotal += b;
        }

        // Abzüge → separate Zeilen nach Nettolohn (immer post-netto, kein SV-Einfluss)
        var abzuegeExtraLines = new List<object>();
        decimal abzuegeExtraTotal = 0;
        foreach (var z in zulagenEntries.Where(z => z.Typ!.Typ == "ABZUG"))
        {
            decimal b = Round05(z.Betrag);
            abzuegeExtraLines.Add(new { bezeichnung = z.Typ!.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""), betrag = -b });
            abzuegeExtraTotal += b;
        }

        var lohnLines  = new List<object>();
        var abzugLines = new List<object>();
        decimal totalLohn = 0;

        if (isMTP)
        {
            // ── MTP ──────────────────────────────────────────────────────
            decimal guaranteedH    = emp.GuaranteedHoursPerWeek ?? 0;
            decimal sollStunden    = Math.Round(guaranteedH * 52m / 12m, 2);
            // Festlohn: Zwischenwert auf 0.01, Schlussresultat auf 0.05
            decimal festlohnRaw    = Math.Round(guaranteedH * hourlyRate * 52m / 12m, 2);
            decimal festlohn       = Round05(festlohnRaw);

            // Stunden-Saldo inkl. Vormonat
            decimal nettoH         = workedHours + absenzGutschrift - sollStunden + vormonatHourSaldo;
            decimal mehrstundenAus = Math.Round(Math.Max(0, nettoH), 2);
            decimal neuerSaldo     = Math.Round(Math.Min(0, nettoH), 2);

            // Basis für Zuschläge: auf 0.01 präzise; Betrag auf 0.05 runden

            decimal mtpBasisRaw = Math.Round(mehrstundenAus * hourlyRate, 2);
            decimal mtpBasis    = Round05(mtpBasisRaw);
            decimal feiertagEnt = Round05(mtpBasis * holidayPct  / 100m);
            decimal ferienEnt   = Math.Round(mtpBasis * vacationPct / 100m, 2); // → FerienGeldSaldo (Saldo: 0.01)

            // Ausbezahlte Feiertage
            decimal feiertagAusz = Round05(feiertagStunden * hourlyRate);

            // Festlohn
            lohnLines.Add(new { bezeichnung = "Festlohn", anzahl = (decimal?)sollStunden, prozent = (decimal?)null, basis = (decimal?)null, betrag = festlohn });
            totalLohn += festlohn;

            if (feiertagAusz > 0)
            {
                lohnLines.Add(new { bezeichnung = $"{Math.Round(feiertagStunden,2)} Ausbezahlte Feiertage", anzahl = (decimal?)feiertagStunden, prozent = (decimal?)null, basis = (decimal?)null, betrag = feiertagAusz });
                totalLohn += feiertagAusz;
            }

            if (mehrstundenAus > 0)
            {
                lohnLines.Add(new { bezeichnung = $"MTP + Stunden", anzahl = (decimal?)mehrstundenAus, prozent = (decimal?)100m, basis = (decimal?)hourlyRate, betrag = mtpBasis });
                totalLohn += mtpBasis;

                if (feiertagEnt > 0)
                {
                    lohnLines.Add(new { bezeichnung = "Feiertagentschädigung", anzahl = (decimal?)null, prozent = (decimal?)holidayPct, basis = (decimal?)mtpBasis, betrag = feiertagEnt });
                    totalLohn += feiertagEnt;
                }
                // Ferienentschädigung wird NICHT ausbezahlt → FerienGeldSaldo
            }

            // Ferien-Geld: akkumulieren; bei Ferienbezug auszahlen
            decimal ferienGeldZuwachs = ferienEnt;
            (ferienGeldAuszahlung, ferienGeldSaldoNeu) = CalcFerienGeld(
                vormonatFerienGeld, ferienGeldZuwachs, vormonatFerienTage, ferienTageSaldoNeu,
                ferienTageGenommen, ref lohnLines, ref totalLohn, vacationPct, mtpBasis);

            // Nacht-Saldo
            decimal neuerNachtSaldo = Math.Round(vormonatNachtSaldo + nightBonus - nachtKompStunden, 2);

            // ── SV-pflichtige Zulagen zu totalLohn addieren (MTP) ─────────
            lohnLines.AddRange(zulagenSvLines);
            totalLohn += zulagenSvTotal;

            // ── Quellensteuer-Abzug (MTP) ─────────────────────────────────
            var qstRule = ComputeQstDeduction(qstEinstellung, totalLohn, companyProfileId, periodFrom);
            if (qstRule is not null) deductions.Add(qstRule);

            var result = BuildResult(employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn,
                zulagenExtraLines, zulagenExtraTotal, abzuegeExtraLines, abzuegeExtraTotal,
                neuerSaldo, vormonatHourSaldo, thirteenthPct, prevThirteenth,
                workedHours, sollStunden, mehrstundenAus,
                nightHours, nightBonus, nachtKompStunden, vormonatNachtSaldo, neuerNachtSaldo,
                vacationWeeks, ferienTageAccrual, ferienTageGenommen, vormonatFerienTage, ferienTageSaldoNeu,
                vormonatFerienGeld, ferienGeldSaldoNeu, ferienGeldAuszahlung, usingDefaultDeductions);
            return Ok(result);
        }
        else if (isUTP)
        {
            // ── UTP ──────────────────────────────────────────────────────
            // Zwischenwerte auf 0.01; Schlussbeträge auf 0.05 runden
            decimal lohnBruttoRaw = Math.Round(workedHours * hourlyRate, 2);
            decimal lohnBrutto    = Round05(lohnBruttoRaw);
            decimal feiertagEnt   = Round05(lohnBrutto * holidayPct  / 100m);
            decimal ferienEnt     = Math.Round(lohnBrutto * vacationPct / 100m, 2); // Saldo: 0.01
            decimal feiertagAusz  = Round05(feiertagStunden * hourlyRate);

            lohnLines.Add(new { bezeichnung = "Stundenlohn", anzahl = (decimal?)workedHours, prozent = (decimal?)null, basis = (decimal?)hourlyRate, betrag = lohnBrutto });
            totalLohn += lohnBrutto;

            if (feiertagAusz > 0)
            {
                lohnLines.Add(new { bezeichnung = "Ausbezahlte Feiertage", anzahl = (decimal?)feiertagStunden, prozent = (decimal?)null, basis = (decimal?)null, betrag = feiertagAusz });
                totalLohn += feiertagAusz;
            }
            if (feiertagEnt > 0)
            {
                lohnLines.Add(new { bezeichnung = "Feiertagentschädigung", anzahl = (decimal?)null, prozent = (decimal?)holidayPct, basis = (decimal?)lohnBrutto, betrag = feiertagEnt });
                totalLohn += feiertagEnt;
            }
            // Ferienentschädigung → FerienGeldSaldo (nicht ausbezahlen)

            (ferienGeldAuszahlung, ferienGeldSaldoNeu) = CalcFerienGeld(
                vormonatFerienGeld, ferienEnt, vormonatFerienTage, ferienTageSaldoNeu,
                ferienTageGenommen, ref lohnLines, ref totalLohn, vacationPct, lohnBrutto);

            decimal neuerNachtSaldoUtp = Math.Round(vormonatNachtSaldo + nightBonus - nachtKompStunden, 2);

            // ── SV-pflichtige Zulagen zu totalLohn addieren (UTP) ─────────
            lohnLines.AddRange(zulagenSvLines);
            totalLohn += zulagenSvTotal;

            // ── Quellensteuer-Abzug (UTP) ─────────────────────────────────
            var qstRuleUtp = ComputeQstDeduction(qstEinstellung, totalLohn, companyProfileId, periodFrom);
            if (qstRuleUtp is not null) deductions.Add(qstRuleUtp);

            var result = BuildResult(employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn,
                zulagenExtraLines, zulagenExtraTotal, abzuegeExtraLines, abzuegeExtraTotal,
                0, 0, thirteenthPct, prevThirteenth,
                workedHours, 0, 0,
                nightHours, nightBonus, nachtKompStunden, vormonatNachtSaldo, neuerNachtSaldoUtp,
                vacationWeeks, ferienTageAccrual, ferienTageGenommen, vormonatFerienTage, ferienTageSaldoNeu,
                vormonatFerienGeld, ferienGeldSaldoNeu, ferienGeldAuszahlung, usingDefaultDeductions);
            return Ok(result);
        }
        else // FIX / FIX-M – nur Tage-Saldo, kein Ferien-Geld
        {
            decimal pct            = emp.EmploymentPercentage ?? 100m;
            // MonthlySalary enthält den tatsächlichen Lohn (nach Pensum), MonthlySalaryFte den 100%-Wert
            // Monatslohn: Zwischenwert auf 0.01, Schlussresultat auf 0.05
            decimal monthSalaryRaw = emp.MonthlySalary ?? Math.Round((emp.MonthlySalaryFte ?? 0) * pct / 100m, 2);
            decimal monthSalary    = Round05(monthSalaryRaw);
            decimal fteSalary      = emp.MonthlySalaryFte ?? (pct > 0 ? Math.Round(monthSalaryRaw * 100m / pct, 2) : monthSalaryRaw);
            lohnLines.Add(new
            {
                bezeichnung = "Monatslohn",
                anzahl      = (decimal?)null,
                prozent     = pct < 100m ? (decimal?)pct : (decimal?)null,
                basis       = pct < 100m ? (decimal?)Math.Round(fteSalary, 2) : (decimal?)null,
                betrag      = monthSalary
            });
            totalLohn += monthSalary;

            decimal neuerNachtSaldoFix = Math.Round(vormonatNachtSaldo + nightBonus - nachtKompStunden, 2);

            // ── SV-pflichtige Zulagen zu totalLohn addieren (FIX) ─────────
            lohnLines.AddRange(zulagenSvLines);
            totalLohn += zulagenSvTotal;

            // ── Quellensteuer-Abzug (FIX) ─────────────────────────────────
            var qstRuleFix = ComputeQstDeduction(qstEinstellung, totalLohn, companyProfileId, periodFrom);
            if (qstRuleFix is not null) deductions.Add(qstRuleFix);

            var result = BuildResult(employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn,
                zulagenExtraLines, zulagenExtraTotal, abzuegeExtraLines, abzuegeExtraTotal,
                0, 0, thirteenthPct, prevThirteenth,
                workedHours, 0, 0,
                nightHours, nightBonus, nachtKompStunden, vormonatNachtSaldo, neuerNachtSaldoFix,
                vacationWeeks, ferienTageAccrual, ferienTageGenommen, vormonatFerienTage, ferienTageSaldoNeu,
                0, 0, 0, usingDefaultDeductions); // FIX: kein Ferien-Geld
            return Ok(result);
        }
    }

    // POST /api/payroll/save – Saldo speichern
    [HttpPost("save")]
    public async Task<IActionResult> SaveSaldo([FromBody] SaveSaldoDto dto)
    {
        var existing = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId    == dto.EmployeeId
                                   && s.PeriodYear    == dto.Year
                                   && s.PeriodMonth   == dto.Month
                                   && s.CompanyProfileId == dto.CompanyProfileId);
        if (existing is null)
        {
            existing = new PayrollSaldo
            {
                EmployeeId       = dto.EmployeeId,
                CompanyProfileId = dto.CompanyProfileId,
                PeriodYear       = dto.Year,
                PeriodMonth      = dto.Month,
                CreatedAt        = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)
            };
            _db.PayrollSaldos.Add(existing);
        }

        existing.HourSaldo                   = dto.HourSaldo;
        existing.NachtSaldo                  = dto.NachtSaldo;
        existing.NightHoursWorked            = dto.NightHoursWorked;
        existing.FerienGeldSaldo             = dto.FerienGeldSaldo;
        existing.FerienTageSaldo             = dto.FerienTageSaldo;
        existing.ThirteenthMonthMonthly      = dto.ThirteenthMonthMonthly;
        existing.ThirteenthMonthAccumulated  = dto.ThirteenthMonthAccumulated;
        existing.GrossAmount                 = dto.GrossAmount;
        existing.NetAmount                   = dto.NetAmount;
        existing.Status                      = dto.Status;
        existing.UpdatedAt                   = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    // GET /api/payroll/saldo?employeeId=X&year=Y&month=M
    [HttpGet("saldo")]
    public async Task<IActionResult> GetSaldo([FromQuery] int employeeId, [FromQuery] int year, [FromQuery] int month)
    {
        var saldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId == employeeId && s.PeriodYear == year && s.PeriodMonth == month);
        return Ok(saldo);
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────

    /// <summary>
    /// Akkumuliert Ferienentschädigung und berechnet Auszahlung bei Ferienbezug.
    /// Gibt (auszahlung, neuerSaldo) zurück.
    /// </summary>
    private static (decimal auszahlung, decimal neuerSaldo) CalcFerienGeld(
        decimal prevGeld, decimal accrual,
        decimal prevTage, decimal neuTageSaldo,
        decimal tageGenommen,
        ref List<object> lohnLines, ref decimal totalLohn,
        decimal vacationPct, decimal basis)
    {
        // Neuer Saldo = Vormonat + Zuwachs (Auszahlung wird danach abgezogen)
        decimal neu = Math.Round(prevGeld + accrual, 2);
        decimal ausz = 0;

        if (tageGenommen > 0 && prevTage > 0)
        {
            // Proportionaler Anteil des akkumulierten Guthabens → Schlussresultat auf 0.05
            ausz = Round05(prevGeld * (tageGenommen / prevTage));
            ausz = Math.Min(ausz, prevGeld); // nie mehr als Guthaben
            if (ausz > 0)
            {
                lohnLines.Add(new
                {
                    bezeichnung = $"Ferienentschädigung-Auszahlung ({tageGenommen:F1} Tage)",
                    anzahl      = (decimal?)tageGenommen,
                    prozent     = (decimal?)null,
                    basis       = (decimal?)null,
                    betrag      = ausz
                });
                totalLohn += ausz;
                neu = Math.Round(neu - ausz, 2);
            }
        }

        return (ausz, neu);
    }

    /// <summary>
    /// Berechnet den Quellensteuer-Abzug aus dem Tarif-Service und gibt eine
    /// synthetische DeductionRule zurück (Type = "fixed", Rate = CHF-Betrag positiv).
    /// Gibt null zurück wenn kein Tarif gefunden oder Betrag = 0.
    /// </summary>
    private DeductionRule? ComputeQstDeduction(
        EmployeeQuellensteuer? einstellung,
        decimal bruttolohn,
        int companyProfileId,
        DateOnly periodFrom)
    {
        if (einstellung is null
            || string.IsNullOrEmpty(einstellung.Steuerkanton)
            || string.IsNullOrEmpty(einstellung.TarifCode))
            return null;

        decimal qstBetrag;

        if (einstellung.Prozentsatz.HasValue)
        {
            // Manuell überschriebener Prozentsatz
            qstBetrag = Math.Round(bruttolohn * einstellung.Prozentsatz.Value / 100m, 2);
        }
        else
        {
            // Dynamisch aus ESTV-Tarifdatei
            decimal? betrag = _tarifService.GetSteuerBetrag(
                einstellung.Steuerkanton,
                einstellung.TarifCode,
                einstellung.AnzahlKinder,
                einstellung.Kirchensteuer,
                bruttolohn);
            if (betrag is null || betrag.Value <= 0) return null;
            qstBetrag = betrag.Value;
        }

        if (qstBetrag <= 0) return null;

        // Satz für Anzeige (best-effort; null wenn kein Tarif)
        decimal? satzPct = einstellung.Prozentsatz
            ?? _tarifService.GetSteuersatzProzent(
                einstellung.Steuerkanton,
                einstellung.TarifCode,
                einstellung.AnzahlKinder,
                einstellung.Kirchensteuer,
                bruttolohn);

        string satzAnzeige = satzPct.HasValue ? $" ({satzPct.Value:F2}%)" : "";
        string qstCode     = einstellung.QstCode ?? $"{einstellung.TarifCode}{einstellung.AnzahlKinder}{(einstellung.Kirchensteuer ? 'Y' : 'N')}";

        return new DeductionRule
        {
            Id               = -99,
            CompanyProfileId = companyProfileId,
            CategoryCode     = "QST",
            CategoryName     = "Quellensteuer",
            Name             = $"Quellensteuer {qstCode} {einstellung.Steuerkanton}{satzAnzeige}",
            Type             = "fixed",
            Rate             = qstBetrag,   // BuildResult negiert diesen Wert
            BasisType        = "gross",
            IsActive         = true,
            ValidFrom        = periodFrom,
            SortOrder        = 90,
        };
    }

    private static object BuildResult(
        Employee employee, Employment emp, CompanyProfile company,
        int year, int month, DateOnly periodFrom, DateOnly periodTo,
        List<object> lohnLines, List<object> abzugLines,
        List<DeductionRule> deductions, decimal totalLohn,
        List<object> zulagenExtraLines, decimal zulagenExtraTotal,
        List<object> abzuegeExtraLines, decimal abzuegeExtraTotal,
        decimal neuerHourSaldo, decimal vormonatHourSaldo,
        decimal thirteenthPct, decimal prevThirteenth,
        decimal workedHours, decimal sollStunden, decimal mehrstunden,
        decimal nightHours, decimal nightBonus, decimal nachtKompStunden,
        decimal vormonatNachtSaldo, decimal neuerNachtSaldo,
        int vacationWeeks, decimal ferienTageAccrual, decimal ferienTageGenommen,
        decimal vormonatFerienTage, decimal ferienTageSaldoNeu,
        decimal vormonatFerienGeld, decimal ferienGeldSaldoNeu, decimal ferienGeldAuszahlung,
        bool usingDefaultDeductions = false)
    {
        // Abzüge berechnen
        decimal totalAbzuege = 0;
        var abzugResult = new List<object>();
        foreach (var d in deductions)
        {
            // Berechnungsbasis je Basistyp
            decimal basis = d.BasisType == "bvg"
                ? Math.Max(0, totalLohn - (d.CoordinationDeduction ?? 0))
                : totalLohn;

            // Freibetrag abziehen (z.B. AHV 65+: CHF 1'400/Mt.)
            // Basis = max(0, Lohn − Freibetrag)
            if (d.FreibetragMonthly is > 0)
                basis = Math.Max(0, basis - d.FreibetragMonthly.Value);

            // Abzug-Betrag: Zwischenwert 0.01, Schlussresultat auf 0.05 runden
            decimal betrag = d.Type == "fixed"
                ? -Round05(d.Rate)
                : -Round05(basis * d.Rate / 100m);

            totalAbzuege += betrag;
            abzugResult.Add(new
            {
                bezeichnung = d.FreibetragMonthly is > 0
                    ? $"{d.Name} (−CHF {d.FreibetragMonthly:F2} Freibetrag)"
                    : d.Name,
                prozent     = d.Type == "percent" ? (decimal?)d.Rate : null,
                basis       = (decimal?)Math.Round(basis, 2),
                betrag
            });
        }

        // Nettolohn: Schlussresultat auf 0.05 (Einzelbeträge bereits gerundet → meist schon 0.05)
        decimal nettolohn = Round05(totalLohn + totalAbzuege);

        // Auszahlungsbetrag = Nettolohn + nicht-SV-pflichtige Zulagen − Abzüge
        decimal auszahlungsbetrag = Round05(nettolohn + zulagenExtraTotal - abzuegeExtraTotal);

        // 13. ML: Rückstellung intern auf 0.01; Schlussresultat auf 0.05
        decimal thirteenthMonthly     = thirteenthPct > 0 ? Round05(totalLohn * thirteenthPct / 100m) : 0;
        decimal thirteenthAccumulated = Math.Round(prevThirteenth + thirteenthMonthly, 2);

        var monthNames = new[] { "", "Januar", "Februar", "März", "April", "Mai", "Juni",
                                     "Juli", "August", "September", "Oktober", "November", "Dezember" };

        return new
        {
            // Kopf
            employeeId      = employee.Id,
            employeeName    = $"{employee.FirstName} {employee.LastName}",
            salutation      = employee.Salutation,
            address         = $"{employee.Street} {employee.HouseNumber}".Trim(),
            zipCity         = $"{employee.ZipCode} {employee.City}".Trim(),
            companyName     = company.BranchName ?? company.CompanyName,
            companyAddress  = $"{company.Street} {company.HouseNumber}".Trim(),
            companyZipCity  = $"{company.ZipCode} {company.City}".Trim(),
            periodLabel     = $"{monthNames[month]} {year}",
            periodFrom      = periodFrom.ToString("dd.MM.yyyy"),
            periodTo        = periodTo.ToString("dd.MM.yyyy"),
            printDate       = DateTime.Now.ToString("dd.MM.yyyy"),

            // Lohn
            lohnLines,
            totalLohn       = Math.Round(totalLohn, 2),

            // Abzüge (SV-Abzüge: AHV, ALV, QST etc.)
            abzugLines      = abzugResult,
            totalAbzuege    = Math.Round(totalAbzuege, 2),

            // Netto
            nettolohn,

            // Nicht-SV-pflichtige Zulagen & Abzüge (nach Netto)
            zulagenExtraLines,
            zulagenExtraTotal   = Math.Round(zulagenExtraTotal, 2),
            abzuegeExtraLines,
            abzuegeExtraTotal   = Math.Round(abzuegeExtraTotal, 2),
            auszahlungsbetrag,

            // Stunden-Info
            workedHours       = Math.Round(workedHours, 2),
            sollStunden       = Math.Round(sollStunden, 2),
            mehrstunden       = Math.Round(mehrstunden, 2),
            vormonatHourSaldo,
            neuerHourSaldo,

            // Nacht-Zeitzuschlag
            nightHours        = Math.Round(nightHours, 2),
            nightBonus        = Math.Round(nightBonus, 2),        // +10% Zeitgutschrift
            nachtKompStunden  = Math.Round(nachtKompStunden, 2),  // eingelöste Ruhetage
            vormonatNachtSaldo,
            neuerNachtSaldo,

            // 13. ML
            thirteenthMonthly,
            thirteenthAccumulated,
            prevThirteenth,

            // Modell
            employmentModel = emp.EmploymentModel,

            // Ferien-Saldo
            vacationWeeks,
            ferienTageAccrual   = Math.Round(ferienTageAccrual, 4),
            ferienTageGenommen  = Math.Round(ferienTageGenommen, 4),
            vormonatFerienTage,
            ferienTageSaldoNeu,
            // Ferien-Geld (nur UTP/MTP, bei FIX immer 0)
            vormonatFerienGeld,
            ferienGeldAuszahlung,
            ferienGeldSaldoNeu,
            // Zuwachs = Saldo neu + Auszahlung - Vormonat  (rückrechenbar)
            ferienGeldAccrual = Math.Round(ferienGeldSaldoNeu + ferienGeldAuszahlung - vormonatFerienGeld, 2),

            // Hinweis: Schweizer Standardsätze verwendet (keine firmenspezifischen Regeln konfiguriert)
            usingDefaultDeductions,
        };
    }

    /// <summary>
    /// Schweizer Standardabzüge 2026 (AHV/IV/EO + ALV) als Fallback,
    /// wenn keine firmenspezifischen DeductionRule-Einträge vorhanden sind.
    /// </summary>
    private static List<DeductionRule> BuildSwissStandardDeductions(int companyProfileId)
    {
        var validFrom = new DateOnly(2026, 1, 1);
        return new List<DeductionRule>
        {
            // AHV/IV/EO Arbeitnehmer (18–64)
            new DeductionRule
            {
                Id                  = -1,
                CompanyProfileId    = companyProfileId,
                CategoryCode        = "AHV",
                CategoryName        = "AHV / IV / EO",
                Name                = "AHV/IV/EO Arbeitnehmer",
                Type                = "percent",
                Rate                = 5.3m,
                BasisType           = "gross",
                MinAge              = 18,
                MaxAge              = 64,
                FreibetragMonthly   = null,
                ValidFrom           = validFrom,
                SortOrder           = 10,
                IsActive            = true,
            },
            // AHV/IV/EO Arbeitnehmer (65+) – mit Freibetrag CHF 1'400/Mt.
            new DeductionRule
            {
                Id                  = -2,
                CompanyProfileId    = companyProfileId,
                CategoryCode        = "AHV",
                CategoryName        = "AHV / IV / EO",
                Name                = "AHV/IV/EO Arbeitnehmer (65+)",
                Type                = "percent",
                Rate                = 5.3m,
                BasisType           = "gross",
                MinAge              = 65,
                MaxAge              = null,
                FreibetragMonthly   = 1400m,
                ValidFrom           = validFrom,
                SortOrder           = 20,
                IsActive            = true,
            },
            // ALV Arbeitnehmer (bis Höchstlohn; vereinfacht: kein Höchstlohn-Check)
            new DeductionRule
            {
                Id                  = -3,
                CompanyProfileId    = companyProfileId,
                CategoryCode        = "ALV",
                CategoryName        = "Arbeitslosenversicherung",
                Name                = "ALV Arbeitnehmer",
                Type                = "percent",
                Rate                = 1.1m,
                BasisType           = "gross",
                MinAge              = 18,
                MaxAge              = 64,
                FreibetragMonthly   = null,
                ValidFrom           = validFrom,
                SortOrder           = 30,
                IsActive            = true,
            },
        };
    }

    /// <summary>
    /// Kaufmännische Rundung auf 5 Rappen (Schlussresultat).
    /// 0.025 wird aufgerundet (MidpointRounding.AwayFromZero).
    /// </summary>
    private static decimal Round05(decimal value)
        => Math.Round(value / 0.05m, 0, MidpointRounding.AwayFromZero) * 0.05m;

    private static (DateOnly from, DateOnly to) CalcPeriod(int startDay, int year, int month)
    {
        if (startDay <= 1)
        {
            var from = new DateOnly(year, month, 1);
            var to   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            return (from, to);
        }
        // z.B. startDay=21: Periode 21.02–20.03 für März
        var toDate   = new DateOnly(year, month, startDay - 1);
        var fromDate = startDay <= 28
            ? new DateOnly(month == 1 ? year - 1 : year, month == 1 ? 12 : month - 1, startDay)
            : new DateOnly(month == 1 ? year - 1 : year, month == 1 ? 12 : month - 1,
                           Math.Min(startDay, DateTime.DaysInMonth(month == 1 ? year - 1 : year, month == 1 ? 12 : month - 1)));
        return (fromDate, toDate);
    }

    private static (int year, int month) PrevPeriod(int year, int month)
        => month == 1 ? (year - 1, 12) : (year, month - 1);
}

public record SaveSaldoDto(
    int EmployeeId, int CompanyProfileId, int Year, int Month,
    decimal HourSaldo, decimal NachtSaldo, decimal NightHoursWorked,
    decimal FerienGeldSaldo, decimal FerienTageSaldo,
    decimal ThirteenthMonthMonthly, decimal ThirteenthMonthAccumulated,
    decimal GrossAmount, decimal NetAmount, string Status);
