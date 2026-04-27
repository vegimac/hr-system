using System.Text.Json;
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
    private readonly KtgTagessatzService _ktgService;
    private readonly KarenzService _karenz;
    private readonly LgavBeitragService _lgav;
    private readonly FerienKuerzungService _ferienKuerzung;
    private readonly PayrollPdfService _payrollPdf;

    public PayrollController(
        AppDbContext db,
        QuellensteuerTarifService tarifService,
        KtgTagessatzService ktgService,
        KarenzService karenz,
        LgavBeitragService lgav,
        FerienKuerzungService ferienKuerzung,
        PayrollPdfService payrollPdf)
    {
        _db             = db;
        _tarifService   = tarifService;
        _ktgService     = ktgService;
        _karenz         = karenz;
        _lgav           = lgav;
        _ferienKuerzung = ferienKuerzung;
        _payrollPdf     = payrollPdf;
    }

    // GET /api/payroll/calculate?employeeId=X&year=Y&month=M&companyProfileId=Z
    [HttpGet("calculate")]
    public async Task<IActionResult> Calculate(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
    {
      try {
        // ── Stammdaten laden ───────────────────────────────────────────────
        var employee = await _db.Employees
            .Include(e => e.Employments)
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null) return NotFound("Mitarbeiter nicht gefunden.");

        var company = await _db.CompanyProfiles.FindAsync(companyProfileId);
        if (company is null) return NotFound("Filiale nicht gefunden.");

        // ── Lohnperiode berechnen ──────────────────────────────────────────
        // Wichtig: Periode muss VOR der Vertragsauswahl berechnet werden,
        // damit wir den Vertrag laden können, der in dieser Periode gültig
        // war (nicht den neuesten verfügbaren).
        int startDay = company.PayrollPeriodStartDay ?? 1;
        var (periodFrom, periodToFull) = CalcPeriod(startDay, year, month);
        int normalPeriodDays = periodToFull.DayNumber - periodFrom.DayNumber + 1;

        // Bemerkungstext für die Lohnabrechnung (Periode-spezifisch falls
        // vorhanden, sonst Filial-Default)
        string? periodeFooterText = await _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == companyProfileId
                     && p.Year == year && p.Month == month && !p.IsTransition)
            .Select(p => p.PdfFooterText)
            .FirstOrDefaultAsync();

        // ── Den Vertrag laden, der in DIESER Periode gültig war ──────────
        // Regel: ContractStartDate <= periodToFull UND
        //        (ContractEndDate IS NULL ODER ContractEndDate >= periodFrom)
        // Wenn mehrere matchen (z.B. weil eine Lohnänderung mitten in die
        // Periode fiel — sollte aber per Konvention immer auf 21. liegen),
        // nehmen wir den mit dem spätesten Vertragsbeginn.
        var emp = employee.Employments
            .Where(e => e.IsActive
                     && e.CompanyProfileId == companyProfileId
                     && DateOnly.FromDateTime(e.ContractStartDate) <= periodToFull
                     && (!e.ContractEndDate.HasValue
                         || DateOnly.FromDateTime(e.ContractEndDate.Value) >= periodFrom))
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefault();
        if (emp is null) return NotFound(
            $"Kein in der Lohnperiode {periodFrom:dd.MM.yyyy}–{periodToFull:dd.MM.yyyy} gültiger Vertrag gefunden.");

        // ── Austritt in der Periode? → Kurzperiode bis ContractEndDate ─────
        // Gesetzliche Regel CH: Arbeitsverhältnis endet auf Monatsende.
        // Wenn ContractEndDate (z.B. 31.3.) vor dem regulären Periodenende
        // (20.4.) liegt, wird der Monatslohn anteilig per Tagessatz-Formel
        // (MonthlySalary × 12 / 365 × Tage) berechnet. Prozent-basierte
        // Positionen (13. ML, Ferien, Feiertag) skalieren automatisch mit.
        DateOnly periodTo = periodToFull;
        bool isShortPeriod = false;
        int shortPeriodDays = normalPeriodDays;

        if (emp.ContractEndDate.HasValue)
        {
            var endDateOnly = DateOnly.FromDateTime(emp.ContractEndDate.Value);
            if (endDateOnly >= periodFrom && endDateOnly < periodToFull)
            {
                periodTo = endDateOnly;
                isShortPeriod = true;
                shortPeriodDays = periodTo.DayNumber - periodFrom.DayNumber + 1;
            }
        }

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

        // ── Abzugsregeln: ausschliesslich aus social_insurance_rate ───────────
        bool usingDefaultDeductions = false;
        var globalRates = await _db.SocialInsuranceRates
            .Where(r => r.IsActive
                     && r.ValidFrom <= periodTo
                     && (r.ValidTo == null || r.ValidTo >= periodFrom))
            .ToListAsync();

        // Deduplizieren: pro (Code + Altersband + Vertragsmodell + OnlyQst) nur
        // die Regel mit dem neuesten ValidFrom. Verhindert Doppel-Abzüge wenn
        // in der DB alte und neue Regeln mit überlappender Gültigkeit liegen
        // (z.B. Rate ab 2024 und Rate ab 2026 beide noch IsActive/ValidTo=null).
        globalRates = globalRates
            .GroupBy(r => new {
                r.Code,
                r.MinAge,
                r.MaxAge,
                r.EmploymentModelCode,
                r.OnlyQuellensteuer,
                r.BasisType
            })
            .Select(g => g.OrderByDescending(r => r.ValidFrom).First())
            .OrderBy(r => r.SortOrder)
            .ToList();

        List<DeductionRule> allRules;
        if (globalRates.Any())
        {
            allRules = globalRates.Select(r => new DeductionRule
            {
                Id                    = -r.Id,
                CompanyProfileId      = companyProfileId,
                CategoryCode          = r.Code,
                CategoryName          = r.Name,
                Name                  = r.Name,
                Type                  = "percent",
                Rate                  = r.Rate,
                BasisType             = r.BasisType,
                MinAge                = r.MinAge,
                MaxAge                = r.MaxAge,
                FreibetragMonthly     = r.FreibetragMonthly,
                CoordinationDeduction = r.CoordinationDeduction,
                OnlyQuellensteuer     = r.OnlyQuellensteuer,
                EmploymentModelCode   = r.EmploymentModelCode,
                ValidFrom             = r.ValidFrom,
                SortOrder             = r.SortOrder,
                IsActive              = true,
            }).ToList();
        }
        else
        {
            allRules = BuildSwissStandardDeductions(companyProfileId);
            usingDefaultDeductions = true;
        }

        // Vertragstyp des Mitarbeiters (für BVG_ZUSATZ-Filter)
        string? empModelCode = emp.EmploymentModel; // PARTTIME | MTP | FIX | FIX-M

        // Altersfilter + QST-Filter + Vertragstyp-Filter in Memory anwenden
        var deductions = allRules
            .Where(r => (r.MinAge == null || employeeAge == null || employeeAge >= r.MinAge)
                     && (r.MaxAge == null || employeeAge == null || employeeAge <= r.MaxAge)
                     && (!r.OnlyQuellensteuer || isQuellensteuer)
                     // Vertragstyp: NULL = gilt für alle; gesetzt = nur wenn MA-Modell übereinstimmt
                     && (r.EmploymentModelCode == null
                         || string.Equals(r.EmploymentModelCode, empModelCode,
                                          StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // ── Vormonat-Saldo ─────────────────────────────────────────────────
        // Auch hier CompanyProfileId mitfiltern — sonst könnte der Vormonats-
        // Saldo aus einer anderen Filiale stammen, wenn der MA dort ebenfalls
        // einen Saldo-Eintrag hat.
        var (prevYear, prevMonth) = PrevPeriod(year, month);
        var prevSaldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId       == employeeId
                                   && s.PeriodYear       == prevYear
                                   && s.PeriodMonth      == prevMonth
                                   && s.CompanyProfileId == companyProfileId);
        decimal vormonatHourSaldo    = prevSaldo?.HourSaldo        ?? 0;
        decimal vormonatNachtSaldo   = prevSaldo?.NachtSaldo       ?? 0;
        decimal vormonatFerienGeld   = prevSaldo?.FerienGeldSaldo  ?? 0;
        decimal vormonatFerienTage   = prevSaldo?.FerienTageSaldo  ?? 0;
        decimal prevThirteenth       = prevSaldo?.ThirteenthMonthAccumulated ?? 0;

        // ── KTG/UVG: Krankheit + Unfall Absenzen laden ─────────────────────────
        var krankAbsenzen  = absences.Where(a => a.AbsenceType == "KRANK").ToList();
        var unfallAbsenzen = absences.Where(a => a.AbsenceType == "UNFALL").ToList();

        // Karenz-Saldo für diesen MA in seinem aktuellen Arbeitsjahr
        // (Legacy: die echte Karenz-Grenze kommt aus CompanyProfile.KarenzTageMax
        // und wird im KarenzService angewendet — diese Werte sind nur noch für
        // das alte Arbeitsjahr-Anker-Datum nötig.)
        decimal karenztageVerbraucht = 0;
        DateOnly arbeitsjahrVon = periodFrom, arbeitsjahrBis = periodFrom;
        if (krankAbsenzen.Any() && employee.EntryDate.HasValue)
        {
            var hired = DateOnly.FromDateTime(employee.EntryDate.Value);
            int yd = periodFrom.Year - hired.Year;
            if (new DateOnly(periodFrom.Year, hired.Month, hired.Day) > periodFrom) yd--;
            arbeitsjahrVon = new DateOnly(hired.Year + yd, hired.Month, hired.Day);
            arbeitsjahrBis = arbeitsjahrVon.AddYears(1).AddDays(-1);
            var ks = await _db.KrankheitKarenzSaldos
                .FirstOrDefaultAsync(k => k.EmployeeId == employeeId
                                       && k.ArbeitsjährVon == arbeitsjahrVon);
            karenztageVerbraucht = ks?.KarenztageUsed ?? 0;
        }

        // ── Berechnung je Modell ───────────────────────────────────────────
        var isMTP = emp.EmploymentModel == "MTP";
        var isUTP = emp.EmploymentModel == "UTP";
        var isFIX = emp.EmploymentModel is "FIX" or "FIX-M";

        decimal hourlyRate    = emp.HourlyRate      ?? 0;
        decimal vacationPct   = emp.VacationPercent  ?? 0;
        decimal holidayPct    = emp.HolidayPercent   ?? 0;
        decimal thirteenthPct = emp.ThirteenthSalaryPercent ?? 0;

        // ── Ferien-% Auto-Upgrade ab Alter 50 (CH-GAV-Standard) ────────────
        // Mitarbeiter ab vollendetem 50. Lebensjahr haben Anspruch auf 6 Wochen
        // Ferien. Wir prüfen tag-genau: Sobald der 50. Geburtstag innerhalb
        // oder vor der aktuellen Lohnperiode liegt (≤ periodTo), gilt der
        // 6-Wochen-Satz für diese und alle Folgeperioden.
        // Beispiel: Geboren 15.5.1976 → 50. Geburtstag 15.5.2026.
        //   Periode 21.4.-20.5.2026: 50. Geb. liegt in Periode → 6 Wochen ✓
        //   Periode 21.3.-20.4.2026: 50. Geb. nach periodTo → noch 5 Wochen
        // Wir ziehen NIE runter — wenn der Vertrag z.B. 15% (7 Wochen) hat,
        // bleibt das so.
        if (employee.DateOfBirth.HasValue)
        {
            var dob = DateOnly.FromDateTime(employee.DateOfBirth.Value);
            var fuenfzigsterGeburtstag = dob.AddYears(50);
            if (fuenfzigsterGeburtstag <= periodTo)
            {
                decimal sechsWochenPct = company.DefaultVacationPercent6Weeks ?? 13.04m;
                if (vacationPct < sechsWochenPct)
                    vacationPct = sechsWochenPct;
            }
        }

        // Tatsächlich gestempelte Stunden (exkl. NACHT_KOMP-Gutschriften)
        decimal workedHours = timeEntries.Sum(t => t.TotalHours ?? 0);

        // Nachtstunden dieser Periode
        decimal nightHours = timeEntries.Sum(t => t.NightHours ?? 0);

        // Nacht-Zeitzuschlag: 10% der Nachtstunden → Saldo-Zuwachs
        decimal nightBonus = Math.Round(nightHours * 0.10m, 2);

        // Absenz-Buckets
        decimal absenzGutschrift      = 0;   // Zeitgutschrift auf Stunden-Saldo (FIX/MTP)
        decimal feiertagStunden       = 0;   // ausbezahlte Feiertage (MTP)
        decimal nachtKompStunden      = 0;   // reduzieren Nacht-Saldo
        decimal utpAuszahlungStunden  = 0;   // UTP: als Stundenlohn auszahlen (z. B. NACHT_KOMP)
        decimal ferienStundenMtp      = 0;   // MTP: Ferien-Anteil für 10.6-Lohnzeile separat ausweisen

        // Helper: berechne Zeitgutschrift dynamisch aus den AbsenzTyp-Regeln
        // statt aus dem gespeicherten HoursCredited. So passen sich historische
        // Absenzen automatisch an Regeländerungen an (z.B. Wochensoll für
        // FIX/FIX-M Ferien/Feiertag pensum-adjustiert).
        decimal ComputeAbsenzHours(Absence a, AbsenzTyp typCfg)
        {
            int daysInPeriod = CountAbsenceDaysInPeriod(a, periodFrom, periodTo);
            if (daysInPeriod == 0) return 0;

            decimal betriebWeekly = company.NormalWeeklyHours ?? 42m;
            decimal pct           = emp.EmploymentPercentage ?? 100m;
            decimal weeklyH       = betriebWeekly;

            if (typCfg.BasisStunden == "VERTRAG")
            {
                if (emp.EmploymentModel == "MTP")
                {
                    weeklyH = emp.GuaranteedHoursPerWeek
                           ?? emp.WeeklyHours
                           ?? betriebWeekly;
                }
                else if (emp.EmploymentModel == "FIX" || emp.EmploymentModel == "FIX-M")
                {
                    // Walter-Regel: FIX/FIX-M nur bei FERIEN/FEIERTAG pensum-adjustiert
                    // (1/7-Modus). Krank/Unfall/Schulung etc. weiter Betriebs-Wochen.
                    if (a.AbsenceType == "FERIEN" || a.AbsenceType == "FEIERTAG")
                    {
                        // Pensum-adjustierte Wochensoll (33.6 h bei 80% × 42 h Betrieb)
                        weeklyH = Math.Round(betriebWeekly * pct / 100m, 2);
                    }
                    // sonst: weeklyH = betriebWeekly (Default)
                }
                // UTP: bleibt auf betriebWeekly
            }

            string modus = typCfg.GutschriftModus ?? "1/5";
            decimal divisor = modus == "1/7" ? 7m : 5m;
            decimal prozent = a.Prozent > 0 ? a.Prozent : 100m;
            return Math.Round(daysInPeriod * weeklyH / divisor * prozent / 100m, 2);
        }

        foreach (var a in absences)
        {
            var typCfg = GetAbsenzTyp(a.AbsenceType);

            // Stunden dynamisch berechnen (statt aus a.HoursCredited).
            // Damit sind alte Datensätze automatisch konsistent mit den neuen
            // Regeln, sobald Walter eine AbsenzTyp-Konfig anpasst.
            decimal hours = ComputeAbsenzHours(a, typCfg);
            if (hours <= 0) continue;

            // 1) Saldo-Reduktion (flag-basiert statt hart verdrahtet)
            //    FERIEN_TAGE wird separat aus ferienTageGenommen gezählt.
            if (typCfg.ReduziertSaldo == "NACHT_STUNDEN")
                nachtKompStunden += hours;

            // 2) Wohin fliessen die Stunden?
            if (isUTP)
            {
                // UTP: nur wenn der Typ explizit UTP-Auszahlung aktiviert hat
                // (heute: NACHT_KOMP). Sonst keine automatische Wirkung.
                if (typCfg.UtpAuszahlung)
                    utpAuszahlungStunden += hours;
            }
            else if (isMTP && a.AbsenceType == "FERIEN")
            {
                // MTP + FERIEN: KEINE Zeitgutschrift (Walter-Regel).
                // Die eigentliche Verarbeitung passiert im MTP-Block weiter
                // unten:
                //   - Sollstunden werden um die Ferientage (× GuarH/7) reduziert,
                //   - Festlohn (10.5) wird per Tagessatz-Formel gekürzt,
                //   - Auszahlung (10.6) erfolgt aus FerienGeldSaldo.
                // Hier nur zusätzlich das Stunden-Äquivalent tracken, damit
                // MTP-Block darauf zugreifen kann (Backward-Compat mit alten
                // Absenzen, bei denen HoursCredited noch mit Zeitgutschrift
                // gefüllt war — neue Absenzen senden HoursCredited=0).
                ferienStundenMtp += hours;
                // ACHTUNG: absenzGutschrift NICHT mehr addieren.
            }
            else if (a.AbsenceType == "FEIERTAG" && isFIX)
            {
                // FIX/FIX-M: Feiertage sind durch den Monatslohn abgedeckt.
                // → als normale Gutschrift zählen, damit der Stunden-Saldo nicht negativ wird.
                absenzGutschrift += hours;
            }
            else if (a.AbsenceType == "FEIERTAG" || !typCfg.Zeitgutschrift)
            {
                // Feiertag (ausbezahlt) oder Typ ohne Zeitgutschrift (MTP): separat ausbezahlen.
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

        // Tatsächlich bezogene Ferientage aus FERIEN-Absenzen — nur Tage in
        // der aktuellen Lohnperiode zählen (Absenzen können sich über mehrere
        // Perioden erstrecken).
        decimal ferienTageGenommen = 0;
        foreach (var a in absences.Where(x => x.AbsenceType == "FERIEN"))
        {
            ferienTageGenommen += CountAbsenceDaysInPeriod(a, periodFrom, periodTo);
        }
        decimal ferienTageSaldoNeu = Math.Round(vormonatFerienTage + ferienTageAccrual - ferienTageGenommen, 4);

        // ── Ferienanspruch-Kürzungs-Vorschlag (Art. 329b OR) ──────────────
        // Berechnet kumulierte Abwesenheits-Tage pro Dienstjahr und schlägt
        // ggfs. eine Kürzung vor (1/12 pro vollem Monat über Schwellwert).
        // Operator entscheidet pro Lohnabrechnung ob anwenden.
        var kuerzungVorschlag = await _ferienKuerzung.CalculateAsync(employeeId, periodTo);
        // Wenn vorhandener Saldo bereits eine angewendete Kürzung enthält
        // (aus früherer Periode), wird sie nicht erneut abgezogen — der
        // ferienTageSaldoNeu hat sie schon drin (über vormonatFerienTage).
        decimal kuerzungVorschlagTage = kuerzungVorschlag.HasKuerzungVorschlag
            ? Math.Round(kuerzungVorschlag.TotalKuerzung12tel * (vacationWeeks * 7m) / 12m, 2)
            : 0m;

        // ── Feiertag-Tage-Saldo (nur FIX / FIX-M) ─────────────────────────
        // Monatliche Gutschrift: +0.5 Tage (fix); Abzug bei FEIERTAG-Absenz
        // anteilig nach Prozent (100% → 1 Tag, 50% → 0.5 Tag). Andere Modelle:
        // Feld bleibt 0 — sie bekommen Feiertage separat über 50.1/MTP-Logik.
        decimal vormonatFeiertagTage   = prevSaldo?.FeiertagTageSaldo ?? 0m;
        decimal feiertagTageAccrual    = 0m;
        decimal feiertagTageGenommen   = 0m;
        if (isFIX)
        {
            feiertagTageAccrual = 0.5m;
            foreach (var a in absences.Where(x => x.AbsenceType == "FEIERTAG"))
            {
                decimal prozent = a.Prozent > 0 ? a.Prozent : 100m;
                int tageInPeriode = CountAbsenceDaysInPeriod(a, periodFrom, periodTo);
                feiertagTageGenommen += tageInPeriode * (prozent / 100m);
            }
        }
        decimal feiertagTageSaldoNeu = Math.Round(
            vormonatFeiertagTage + feiertagTageAccrual - feiertagTageGenommen, 4);

        // ── Ferien-Geld-Saldo (nur UTP + MTP) ─────────────────────────────
        // Ferienentschädigung wird NICHT monatlich ausbezahlt, sondern akkumuliert.
        // Bei Ferienbezug: proportionale Auszahlung (Tage genommen / Saldo vorher)
        decimal ferienGeldSaldoNeu   = vormonatFerienGeld;  // wird unten angepasst
        decimal ferienGeldAuszahlung = 0;
        // Wird nach Modell-Berechnung gesetzt (ferienEnt ist modellabhängig)


        // ── L-GAV-Jahresbeitrag: automatisch einfügen wenn fällig ─────────
        // Idempotent — erzeugt pro MA/Jahr maximal einen Eintrag auf
        // Lohnposition 600.24. Wird VOR dem Laden der Zulagen aufgerufen
        // damit der neu angelegte Abzug in dieser Periode mit berechnet wird.
        await _lgav.EnsureAsync(employee, emp, company, year, month, periodFrom, periodTo);

        // ── Zulagen & Abzüge für diese Periode laden ──────────────────────
        // Einmalige Einträge (manuell pro Periode erfasst) + wiederkehrende
        // Einträge (Mitarbeiter-Stammdaten) werden zusammengeführt und gleich
        // behandelt. Wiederkehrende liefern eine "synthetische" LohnZulage
        // in-memory (ohne DB-Eintrag), damit die bestehende Berechnungslogik
        // unverändert bleibt.
        string periodeStr = $"{year:D4}-{month:D2}";
        var einmaligeZulagen = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.EmployeeId == employeeId && z.Periode == periodeStr)
            .OrderBy(z => z.Lohnposition!.SortOrder)
            .ThenBy(z => z.CreatedAt)
            .ToListAsync();

        // Wiederkehrende Einträge, die in diese Periode fallen
        // (Überlappung von [valid_from, valid_to] mit [periodFrom, periodTo])
        var wiederkehrendeRaw = await _db.EmployeeRecurringWages
            .Include(r => r.Lohnposition)
            .Where(r => r.EmployeeId == employeeId
                     && r.ValidFrom <= periodTo
                     && (r.ValidTo == null || r.ValidTo >= periodFrom))
            .OrderBy(r => r.Lohnposition!.SortOrder)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync();

        var zulagenEntries = einmaligeZulagen
            .Concat(wiederkehrendeRaw.Select(r => new LohnZulage
            {
                Id             = -r.Id,            // negative ID kennzeichnet "virtuell"
                EmployeeId     = r.EmployeeId,
                Periode        = periodeStr,
                LohnpositionId = r.LohnpositionId,
                Lohnposition   = r.Lohnposition,
                Betrag         = r.Betrag,
                Bemerkung      = r.Bemerkung,
                CreatedAt      = r.CreatedAt
            }))
            .OrderBy(z => z.Lohnposition!.SortOrder)
            .ThenBy(z => z.CreatedAt)
            .ToList();

        // ── Lohnabtretungen (Lohnpfändung / Sozialamt) laden ─────────────
        // Aktive Zuweisungen für diesen Mitarbeiter im Perioden-Zeitraum.
        // Werden nach Netto vom Lohn abgezogen.
        var lohnAssignments = await _db.EmployeeLohnAssignments
            .Include(la => la.Behoerde)
            .Where(la => la.EmployeeId == employeeId
                      && la.ValidFrom <= periodTo
                      && (la.ValidTo == null || la.ValidTo >= periodFrom))
            .OrderBy(la => la.ValidFrom)
            .ThenBy(la => la.Id)
            .ToListAsync();

        // ── Lohnposition-Katalog + flag-basiertes Basis-Tracking ──────────
        // Die Feiertags-/Ferien-/13.-ML-Basis wird aus den Beträgen pro
        // Lohnarten-Code gebildet — je nach dem, welche Flags die Lohnart
        // in `lohnposition` trägt (ZaehltAlsBasisFeiertag, -Ferien, -13ml).
        // Ersetzt die frühere hart verdrahtete Zuordnung
        // (z. B. "feiertagBasis = festlohn + mtpBasis" im MTP-Modell).
        var lohnposByCode = await _db.Lohnpositionen
            .Where(l => l.IsActive)
            .ToDictionaryAsync(l => l.Code);

        var codeAmounts = new Dictionary<string, decimal>();
        void AddAmount(string code, decimal amt)
        {
            if (string.IsNullOrEmpty(code) || amt == 0) return;
            codeAmounts[code] = (codeAmounts.TryGetValue(code, out var v) ? v : 0m) + amt;
        }
        decimal SumByFlag(Func<Lohnposition, bool> selector)
        {
            decimal sum = 0;
            foreach (var kv in codeAmounts)
                if (lohnposByCode.TryGetValue(kv.Key, out var lp) && selector(lp))
                    sum += kv.Value;
            return sum;
        }
        // Lohnposition-Bezeichnung aus dem Katalog holen (mit Fallback).
        // Damit erscheint auf dem Lohnzettel der Name den der Admin in der
        // Lohnposition-Verwaltung hinterlegt hat (z.B. "KTG Karenzentschädigung"),
        // nicht ein hart kodierter Text.
        string LabelFor(string code, string fallback)
            => lohnposByCode.TryGetValue(code, out var lp) && !string.IsNullOrWhiteSpace(lp.Bezeichnung)
                ? lp.Bezeichnung
                : fallback;

        // SV-pflichtige Zulagen → werden zu totalLohn addiert (fliessen in SV-Basen ein)
        // SV-Flags kommen direkt aus Lohnposition (kein Umweg über LohnZulagTyp mehr)
        var zulagenSvLines  = new List<object>();
        decimal zulagenSvTotal = 0;
        // Per-SV-Typ Zulage-Deltas (für separate SV-Basen)
        decimal deltaAhv = 0, deltaNbuv = 0, deltaKtg = 0, deltaBvg = 0, deltaQst = 0;

        foreach (var z in zulagenEntries.Where(z => z.Lohnposition!.Typ == "ZULAGE"))
        {
            decimal b  = Math.Round(z.Betrag, 2);
            var     lp = z.Lohnposition!;

            bool anyFlag = lp.AhvAlvPflichtig || lp.NbuvPflichtig || lp.KtgPflichtig
                        || lp.BvgPflichtig    || lp.QstPflichtig;
            if (!anyFlag) continue; // → geht in zulagenExtraLines

            if (lp.DreijehnterMlPflichtig && b > 0)
            {
                // Split: Eingegebener Betrag = Total (inkl. 13. ML)
                // Basis  = Total × 12/13  (auf 2 Dezimalen)
                // 13. ML = Total − Basis  (Rest → Summe bleibt exakt)
                decimal basis13  = Math.Round(b * 12m / 13m, 2);
                decimal ml13     = b - basis13;
                string  bez      = lp.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : "");

                zulagenSvLines.Add(new { bezeichnung = bez,
                    anzahl = (decimal?)null, prozent = (decimal?)null, basis = (decimal?)null, betrag = basis13 });
                zulagenSvLines.Add(new { bezeichnung = $"13. ML a/{lp.Bezeichnung}",
                    anzahl = (decimal?)null, prozent = (decimal?)8.33m, basis = (decimal?)basis13, betrag = ml13 });
                zulagenSvTotal += b;  // Beide Zeilen fliessen in SV-Basis (Summe = b)
            }
            else
            {
                zulagenSvLines.Add(new {
                    bezeichnung = lp.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""),
                    anzahl = (decimal?)null, prozent = (decimal?)null, basis = (decimal?)null, betrag = b
                });
                zulagenSvTotal += b;
            }

            if (lp.AhvAlvPflichtig) deltaAhv  += b;
            if (lp.NbuvPflichtig)   deltaNbuv += b;
            if (lp.KtgPflichtig)    deltaKtg  += b;
            if (lp.BvgPflichtig)    deltaBvg  += b;
            if (lp.QstPflichtig)    deltaQst  += b;

            // Beitrag in das flag-basierte Basis-Tracking aufnehmen
            AddAmount(lp.Code, b);
        }

        // Nicht-SV-pflichtige Zulagen → separate Zeilen nach Nettolohn (Spesen etc.)
        var zulagenExtraLines = new List<object>();
        decimal zulagenExtraTotal = 0;
        foreach (var z in zulagenEntries.Where(z => z.Lohnposition!.Typ == "ZULAGE"))
        {
            var lp2 = z.Lohnposition!;
            bool anyFlag2 = lp2.AhvAlvPflichtig || lp2.NbuvPflichtig || lp2.KtgPflichtig
                         || lp2.BvgPflichtig    || lp2.QstPflichtig;
            if (anyFlag2) continue; // bereits in zulagenSvLines

            decimal b = Math.Round(z.Betrag, 2);
            zulagenExtraLines.Add(new { bezeichnung = lp2.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""), betrag = b });
            zulagenExtraTotal += b;
        }

        // Abzüge → separate Zeilen nach Nettolohn (immer post-netto, kein SV-Einfluss)
        var abzuegeExtraLines = new List<object>();
        decimal abzuegeExtraTotal = 0;
        foreach (var z in zulagenEntries.Where(z => z.Lohnposition!.Typ == "ABZUG"))
        {
            decimal b  = Math.Round(z.Betrag, 2);
            var     lp = z.Lohnposition!;
            abzuegeExtraLines.Add(new { bezeichnung = lp.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""), betrag = -b });
            abzuegeExtraTotal += b;
        }

        var lohnLines  = new List<object>();
        var abzugLines = new List<object>();
        decimal totalLohn = 0;

        // ── Manuelle Ferien-Geld-Saldo-Auszahlung (Code 195.3) ──────────
        // Wird bei Austritt oder Jahresende gebucht — die entsprechende
        // Zulage wurde bereits oben als SV-pflichtige Zeile verarbeitet
        // (fließt in totalLohn, alle Sozialversicherungen und 13.-ML-Basis).
        // Hier lesen wir nur den Gesamtbetrag, um im MTP/UTP-Block damit
        // das Ferien-Geld-Saldo zu reduzieren.
        decimal ferienGeldAuszahlungManuell = zulagenEntries
            .Where(z => z.Lohnposition?.Code == "195.3" && z.Lohnposition.Typ == "ZULAGE")
            .Sum(z => Math.Round(z.Betrag, 2));

        // ── Automatische Ferien-Geld-Auszahlung im Dezember (UTP/MTP) ──
        // Wenn am CompanyProfile aktiviert (AutoFerienGeldAuszahlungDezember)
        // und der Lohnlauf im Dezember ist: nach CalcFerienGeld wird der
        // verbleibende Saldo als synthetische 195.3-Lohnzeile ausbezahlt.
        // Lohnposition 195.3 einmalig laden — wird unten benötigt für
        // Bezeichnung, SV-Flags und Basis-Tracking.
        Lohnposition? lpFerienAuszahlung = null;
        bool autoDezemberAuszahlung = month == 12 && company.AutoFerienGeldAuszahlungDezember;
        if (autoDezemberAuszahlung)
        {
            lpFerienAuszahlung = await _db.Lohnpositionen
                .FirstOrDefaultAsync(l => l.Code == "195.3" && l.IsActive);
        }

        // ── Krankheit & Unfall: tag-genaue Listen (Datum + Prozent + InKarenz)
        // Arbeitet auf der tatsächlichen Lohnperiode (z.B. 21.01.–20.02.),
        // nicht auf Kalendermonaten. Die Karenz-Kumulation berücksichtigt
        // dabei frühere Tage des Karenzjahrs (auch aus Vorperioden).
        // Krank und Unfall laufen getrennt — eigene Tage-Grenze pro Typ
        // (Default: 14 Krank, 2 Unfall), aber gleiche Lohn-Logik.
        var krankBreakdown = await _karenz.GetPeriodBreakdownAsync(
            employeeId, companyProfileId, periodFrom, periodTo, "KRANK");
        var unfallBreakdown = await _karenz.GetPeriodBreakdownAsync(
            employeeId, companyProfileId, periodFrom, periodTo, "UNFALL");

        if (isMTP)
        {
            // ── MTP ──────────────────────────────────────────────────────
            decimal guaranteedH    = emp.GuaranteedHoursPerWeek ?? 0;
            // Bei Austritt in der Periode: Sollstunden und Festlohn per Tagessatz:
            //   guaranteedH × 52 / 365 × Kalendertage_Kurzperiode
            // Sonst: monatliche Standard-Umrechnung (52 / 12).
            decimal sollStundenVoll = isShortPeriod
                ? Math.Round(guaranteedH * 52m / 365m * shortPeriodDays, 2)
                : Math.Round(guaranteedH * 52m / 12m, 2);
            // Festlohn: Zwischenbetrag auf 2 Dezimalen (keine 0.05-Pre-Rundung mehr)
            decimal festlohnVoll = isShortPeriod
                ? Math.Round(guaranteedH * hourlyRate * 52m / 365m * shortPeriodDays, 2)
                : Math.Round(guaranteedH * hourlyRate * 52m / 12m, 2);

            // ── MTP + FERIEN Regel (Walter 24.04.2026) ────────────────
            // Pro Ferientag:
            //   • Sollstunden um GuaranteedH/7 reduzieren (MA muss an diesen
            //     Tagen nicht arbeiten — keine Minus-Stunden im Saldo).
            //   • Festlohn (10.5) wird um Tagessatz × Ferientage gekürzt
            //     (Tagessatz = festlohnVoll × 12 / 365, gleiche Formel wie
            //     bei Krankheit).
            //   • Die Auszahlung aus FerienGeldSaldo erfolgt separat durch
            //     CalcFerienGeld() weiter unten ("Ferienentschädigung-
            //     Auszahlung" anteilig vom akkumulierten Guthaben).
            //   • Kein 10.6-Split mehr — die frühere Zeile "MTP Festlohn Ferien"
            //     (aus festlohn split) wird nicht mehr gebucht, sonst wäre
            //     der Betrag doppelt ausbezahlt (einmal im festlohn, einmal
            //     aus dem Saldo).
            decimal mtpTagessatz       = festlohnVoll * 12m / 365m;
            decimal mtpFerienTage      = (ferienStundenMtp > 0 && guaranteedH > 0)
                ? Math.Round(ferienStundenMtp * 7m / guaranteedH, 4)
                : 0m;
            // Alternative Zählung: direkt aus Absenzen (robust gegen alte Daten
            // wo HoursCredited noch mit Zeitgutschrift gefüllt war).
            decimal mtpFerienTageAusAbsenzen = absences
                .Where(a => a.AbsenceType == "FERIEN")
                .Sum(a => (decimal)CountAbsenceDaysInPeriod(a, periodFrom, periodTo));
            if (mtpFerienTageAusAbsenzen > mtpFerienTage) mtpFerienTage = mtpFerienTageAusAbsenzen;
            decimal festlohnFerienKuerzung = Math.Round(mtpTagessatz * mtpFerienTage, 2);

            // Festlohn-Arbeit = voller Festlohn abzüglich Ferien-Kürzung.
            decimal festlohnArbeitBetrag  = Math.Round(festlohnVoll - festlohnFerienKuerzung, 2);
            // Sollstunden für Stundenkontrolle ebenfalls um Ferien-Äquivalent reduziert
            decimal ferienStundenAequivalent = mtpFerienTage * guaranteedH / 7m;
            decimal sollStunden = Math.Round(sollStundenVoll - ferienStundenAequivalent, 2);
            decimal festlohnArbeitStunden = Math.Round(sollStunden, 2);

            // Stunden-Saldo inkl. Vormonat (Ferien wurden bereits durch sollStunden
            // abgebildet, absenzGutschrift enthält nur noch Krank/Schulung/etc.)
            decimal nettoH         = workedHours + absenzGutschrift - sollStunden + vormonatHourSaldo;
            decimal mehrstundenAus = Math.Round(Math.Max(0, nettoH), 2);
            decimal neuerSaldo     = Math.Round(Math.Min(0, nettoH), 2);

            // Mehrstunden-Betrag: Zwischenbetrag auf 2 Dezimalen
            decimal mtpBasis    = Math.Round(mehrstundenAus * hourlyRate, 2);

            // Ausbezahlte Feiertage (eigene Stunden-Auszahlung)
            decimal feiertagAusz = Math.Round(feiertagStunden * hourlyRate, 2);

            // Basis für Minimum-Lohn-Kontrolle = Stundenlohn
            if (festlohnArbeitBetrag > 0 || mtpFerienTage == 0)
            {
                // Label bei Ferien-Kürzung um Hinweis erweitern
                string mtpFestlohnLabel;
                if (isShortPeriod) {
                    mtpFestlohnLabel = $"{LabelFor("10", "Festlohn")} ({shortPeriodDays} von {normalPeriodDays} Tagen – Austritt {periodTo:dd.MM.yyyy})";
                } else if (mtpFerienTage > 0) {
                    mtpFestlohnLabel = $"{LabelFor("10", "Festlohn")} (gekürzt um {Math.Round(mtpFerienTage, 2)} Ferientage × CHF {mtpTagessatz:F2})";
                } else {
                    mtpFestlohnLabel = LabelFor("10", "Festlohn");
                }
                lohnLines.Add(new {
                    bezeichnung = mtpFestlohnLabel,
                    anzahl  = (decimal?)festlohnArbeitStunden,
                    prozent = (decimal?)null,
                    basis   = (decimal?)hourlyRate,
                    betrag  = festlohnArbeitBetrag,
                    accrued = (decimal?)festlohnArbeitBetrag
                });
                totalLohn += festlohnArbeitBetrag;
                AddAmount("10", festlohnArbeitBetrag);
            }

            // Hinweis: MTP Ferien-Auszahlung wird weiter unten verbucht
            // (nach ferienEnt-Berechnung), weil sie auf dem aktuellen
            // FerienGeldSaldo (inkl. Accrual dieser Periode) basiert.

            // Auszahlung"-Zeile eingefügt.

            if (feiertagAusz > 0)
            {
                lohnLines.Add(new { bezeichnung = $"{Math.Round(feiertagStunden,2)} Ausbezahlte Feiertage", anzahl = (decimal?)feiertagStunden, prozent = (decimal?)null, basis = (decimal?)null, betrag = feiertagAusz, accrued = (decimal?)feiertagAusz });
                totalLohn += feiertagAusz;
            }

            if (mehrstundenAus > 0)
            {
                lohnLines.Add(new { bezeichnung = $"MTP + Stunden", anzahl = (decimal?)mehrstundenAus, prozent = (decimal?)100m, basis = (decimal?)hourlyRate, betrag = mtpBasis, accrued = (decimal?)mtpBasis });
                totalLohn += mtpBasis;
                AddAmount("4", mtpBasis);  // Basis-Tracking (Zusatzstunden)
            }

            // ── Krankheit: Lohnkürzung + 88%-Gutschrift (im Karenzfenster) ──
            // Basis MTP: Tagessatz100 aus KtgTagessatzService — enthält
            // Garantie-Anteil + Ø Mehrstunden-Anteil (bei Regel B, also
            // ab 4 abgeschlossenen Perioden). Fallback auf die statische
            // Regel-A-Formel (guaranteedH × hourlyRate × 52/365), falls
            // der Service null liefert (z.B. kein Employment gefunden).
            // 80%-Gutschrift kommt nach dem mainLohn-Snapshot, siehe unten.
            // Für Tage innerhalb der BVG-Wartefrist (3 Kalendermonate ab
            // AU-Beginn) wird die fehlende Differenz zu 100% in deltaBvg
            // geschoben → BVG-Basis steht unverändert auf bisherigem Lohn.
            decimal krankTagesBasisMtp = 0m;
            if (krankBreakdown.Count > 0 || unfallBreakdown.Count > 0)
            {
                var ktgMtp = await _ktgService.CalculateAsync(employeeId, companyProfileId);
                krankTagesBasisMtp = ktgMtp?.Tagessatz100
                                  ?? (guaranteedH * hourlyRate * 52m / 365m);
            }
            decimal krankAbzugMtp = 0m, krank88Mtp = 0m, krank80Mtp = 0m;
            decimal krankTage88Mtp = 0m, krankTage80Mtp = 0m;
            decimal krankBvgKorrekturMtp = 0m;
            foreach (var t in krankBreakdown)
            {
                decimal tagWert = krankTagesBasisMtp * (t.Prozent / 100m);
                krankAbzugMtp += tagWert;
                if (t.InKarenz)
                {
                    krank88Mtp += tagWert * 0.88m;
                    krankTage88Mtp += t.Prozent / 100m;
                    if (t.BvgAuf100) krankBvgKorrekturMtp += tagWert * 0.12m;  // fehlende 12%
                }
                else
                {
                    krank80Mtp += tagWert * 0.80m;
                    krankTage80Mtp += t.Prozent / 100m;
                    if (t.BvgAuf100) krankBvgKorrekturMtp += tagWert * 0.20m;  // fehlende 20%
                }
            }
            krankAbzugMtp        = Math.Round(krankAbzugMtp,        2);
            krank88Mtp           = Math.Round(krank88Mtp,           2);
            krank80Mtp           = Math.Round(krank80Mtp,           2);
            krankBvgKorrekturMtp = Math.Round(krankBvgKorrekturMtp, 2);

            if (krankAbzugMtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("75", "Korrektur Krankheit"),
                    anzahl  = (decimal?)krankBreakdown.Count,
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(krankTagesBasisMtp, 2),
                    betrag  = -krankAbzugMtp,
                    accrued = (decimal?)(-krankAbzugMtp)
                });
                totalLohn -= krankAbzugMtp;
                AddAmount("75", -krankAbzugMtp);
            }
            if (krank88Mtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70", "Krankheit (Karenzentschädigung)"),
                    anzahl  = (decimal?)krankTage88Mtp,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(krankTagesBasisMtp, 2),
                    betrag  = krank88Mtp,
                    accrued = (decimal?)krank88Mtp
                });
                totalLohn += krank88Mtp;
                AddAmount("70", krank88Mtp);
            }

            // ── Unfall: Lohnkürzung + 88%-Gutschrift (im Karenzfenster) ───
            // Berechnung identisch zu Krankheit, nur mit eigener Tage-Grenze
            // (Default 2 Tage) und eigener BVG-Wartefrist — Unfall hat seine
            // separate 3-Monate-Wartefrist, unabhängig von Krank (andere
            // Versicherung, daher eigene Zählung).
            decimal unfallTagesBasisMtp = krankTagesBasisMtp;  // gleicher Tageswert
            decimal unfallAbzugMtp = 0m, unfall88Mtp = 0m, unfall80Mtp = 0m;
            decimal unfallTage88Mtp = 0m, unfallTage80Mtp = 0m;
            decimal unfallBvgKorrekturMtp = 0m;
            foreach (var t in unfallBreakdown)
            {
                decimal tagWert = unfallTagesBasisMtp * (t.Prozent / 100m);
                unfallAbzugMtp += tagWert;
                if (t.InKarenz)
                {
                    unfall88Mtp += tagWert * 0.88m;
                    unfallTage88Mtp += t.Prozent / 100m;
                    if (t.BvgAuf100) unfallBvgKorrekturMtp += tagWert * 0.12m;
                }
                else
                {
                    unfall80Mtp += tagWert * 0.80m;
                    unfallTage80Mtp += t.Prozent / 100m;
                    if (t.BvgAuf100) unfallBvgKorrekturMtp += tagWert * 0.20m;
                }
            }
            unfallAbzugMtp        = Math.Round(unfallAbzugMtp,        2);
            unfall88Mtp           = Math.Round(unfall88Mtp,           2);
            unfall80Mtp           = Math.Round(unfall80Mtp,           2);
            unfallBvgKorrekturMtp = Math.Round(unfallBvgKorrekturMtp, 2);

            if (unfallAbzugMtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("65", "Korrektur Unfall"),
                    anzahl  = (decimal?)unfallBreakdown.Count,
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(unfallTagesBasisMtp, 2),
                    betrag  = -unfallAbzugMtp,
                    accrued = (decimal?)(-unfallAbzugMtp)
                });
                totalLohn -= unfallAbzugMtp;
                AddAmount("65", -unfallAbzugMtp);
            }
            if (unfall88Mtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60", "Unfall (Karenzentschädigung)"),
                    anzahl  = (decimal?)unfallTage88Mtp,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(unfallTagesBasisMtp, 2),
                    betrag  = unfall88Mtp,
                    accrued = (decimal?)unfall88Mtp
                });
                totalLohn += unfall88Mtp;
                AddAmount("60", unfall88Mtp);
            }

            // MTP: Feiertag-/Ferien-Basis aus Lohnpositions-Flags
            //   → Festlohn (10.1) + Zusatzstunden (10.4) haben ZaehltAlsBasisFeiertag=true
            //   → nur Zusatzstunden (10.4) hat ZaehltAlsBasisFerien=true
            // Zusätzlich tragen alle Zulagen bei, deren Lohnart die Flags trägt.
            decimal feiertagBasis = SumByFlag(lp => lp.ZaehltAlsBasisFeiertag);
            decimal ferienBasis   = SumByFlag(lp => lp.ZaehltAlsBasisFerien);

            decimal ferienEnt = Math.Round(ferienBasis * vacationPct / 100m, 2);
            if (ferienEnt > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = "Ferienentschädigung",
                    anzahl  = (decimal?)null,
                    prozent = (decimal?)vacationPct,
                    basis   = (decimal?)ferienBasis,
                    betrag  = 0m,           // wandert in den Saldo
                    accrued = (decimal?)ferienEnt
                });
            }

            decimal feiertagEnt = Math.Round(feiertagBasis * holidayPct / 100m, 2);
            if (feiertagEnt > 0)
            {
                lohnLines.Add(new { bezeichnung = "Feiertagentschädigung", anzahl = (decimal?)null, prozent = (decimal?)holidayPct, basis = (decimal?)feiertagBasis, betrag = feiertagEnt, accrued = (decimal?)feiertagEnt });
                totalLohn += feiertagEnt;
                AddAmount("3", feiertagEnt);  // Basis-Tracking (Festlohn Feiertage MTP)
            }

            // ── MTP Ferien-Auszahlung aus FerienGeldSaldo (Walter-Formel) ─
            // Regel: Ferien dürfen NIE im Voraus bezogen werden — Auszahlung
            // erfolgt nur aus dem Vormonats-Saldo (was bereits angesammelt
            // war), nicht aus dem aktuellen Accrual.
            //   Auszahlung = (vormonatGeld / vormonatTage) × bezogene Tage
            // Cap: nie mehr als der vorhandene Vormonats-Saldo.
            //
            // Wenn Vormonat = 0 (neuer MA): keine Auszahlung. Operator
            // sollte Ferien erst nach Aufbau des Saldos zulassen, oder am
            // Jahresende läuft sowieso die Auto-Auszahlung in Dezember.
            decimal mtpFerienAuszahlungBetrag = 0;
            decimal mtpAvgTagessatz           = 0;
            if (mtpFerienTage > 0 && vormonatFerienTage > 0 && vormonatFerienGeld > 0)
            {
                mtpAvgTagessatz = vormonatFerienGeld / vormonatFerienTage;
                mtpFerienAuszahlungBetrag = Math.Round(mtpAvgTagessatz * mtpFerienTage, 2);
                // Cap: nie mehr als der Vormonats-Saldo (kein Vorbezug)
                if (mtpFerienAuszahlungBetrag > vormonatFerienGeld)
                    mtpFerienAuszahlungBetrag = vormonatFerienGeld;
            }

            if (mtpFerienAuszahlungBetrag > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = $"{LabelFor("2", "Festlohn für bezogene Ferien")} ({Math.Round(mtpFerienTage,2)} Tage × Ø CHF {mtpAvgTagessatz:F2})",
                    anzahl  = (decimal?)Math.Round(mtpFerienTage, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)null,
                    betrag  = mtpFerienAuszahlungBetrag,
                    accrued = (decimal?)mtpFerienAuszahlungBetrag
                });
                totalLohn += mtpFerienAuszahlungBetrag;
                AddAmount("2", mtpFerienAuszahlungBetrag);
            }

            // Ferien-Geld-Saldo neu: Vormonat + Accrual − Auszahlung
            // Bleibt durch Cap immer ≥ 0 (Saldo wird nie negativ).
            ferienGeldSaldoNeu  = Math.Round(vormonatFerienGeld + ferienEnt - mtpFerienAuszahlungBetrag, 2);
            ferienGeldAuszahlung = mtpFerienAuszahlungBetrag;

            // Manuelle Ferien-Geld-Saldo-Auszahlung (Code 195.3): reduziert
            // das Saldo. Der Betrag wurde schon als SV-pflichtige Zulage
            // zu totalLohn addiert — hier nur noch die Saldo-Führung.
            if (ferienGeldAuszahlungManuell > 0)
            {
                ferienGeldAuszahlung += ferienGeldAuszahlungManuell;
                ferienGeldSaldoNeu   = Math.Max(0m, ferienGeldSaldoNeu - ferienGeldAuszahlungManuell);
            }

            // Automatische Jahresend-Auszahlung des Ferien-Geld-Saldos (MTP).
            // Synthetische 195.3-Zeile mit dem aktuellen Saldo, voll SV-pflichtig.
            if (autoDezemberAuszahlung && lpFerienAuszahlung != null && ferienGeldSaldoNeu > 0)
            {
                decimal autoBetrag = Math.Round(ferienGeldSaldoNeu, 2);
                lohnLines.Add(new {
                    bezeichnung = lpFerienAuszahlung.Bezeichnung + " (Jahresende)",
                    anzahl  = (decimal?)null,
                    prozent = (decimal?)null,
                    basis   = (decimal?)null,
                    betrag  = autoBetrag,
                    accrued = (decimal?)autoBetrag
                });
                totalLohn += autoBetrag;
                if (lpFerienAuszahlung.AhvAlvPflichtig) deltaAhv  += autoBetrag;
                if (lpFerienAuszahlung.NbuvPflichtig)   deltaNbuv += autoBetrag;
                if (lpFerienAuszahlung.KtgPflichtig)    deltaKtg  += autoBetrag;
                if (lpFerienAuszahlung.BvgPflichtig)    deltaBvg  += autoBetrag;
                if (lpFerienAuszahlung.QstPflichtig)    deltaQst  += autoBetrag;
                AddAmount(lpFerienAuszahlung.Code, autoBetrag);
                ferienGeldAuszahlung += autoBetrag;
                ferienGeldSaldoNeu    = 0m;
            }

            // Nacht-Saldo
            decimal neuerNachtSaldo = Math.Round(vormonatNachtSaldo + nightBonus - nachtKompStunden, 2);

            // ── SV-pflichtige Zulagen zu totalLohn addieren (MTP) ─────────
            decimal mainLohnMtp = totalLohn;
            lohnLines.AddRange(zulagenSvLines);
            totalLohn += zulagenSvTotal;

            // ── 13. Monatslohn: Auszahlung oder Rückstellung je Firmen-Rhythmus ─
            // Basis für die aktuelle Monats-Akkumulation: totalLohn inkl.
            // SV-pflichtiger Zulagen (ohne 13. ML selbst).
            bool isPayoutMonthMtp = IsThirteenthPayoutMonth(company.ThirteenthMonthPayoutsPerYear, month);
            decimal dreizehnterMtp = 0;
            decimal thirteenthPctForSaldo  = thirteenthPct;   // Wird akkumuliert …
            decimal prevThirteenthForSaldo = prevThirteenth;
            if (thirteenthPct > 0 && isPayoutMonthMtp)
            {
                // … ausser im Auszahlungsmonat: Vormonats-Saldo + akt. Zuwachs ausbezahlen.
                decimal currentAccrual = Math.Round(totalLohn * thirteenthPct / 100m, 2);
                dreizehnterMtp = Math.Round(prevThirteenth + currentAccrual, 2);
                if (dreizehnterMtp > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = prevThirteenth > 0
                            ? $"13. Monatslohn (inkl. CHF {prevThirteenth:F2} Saldo)"
                            : "13. Monatslohn",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)Math.Round(totalLohn, 2),
                        betrag      = dreizehnterMtp,
                        accrued     = (decimal?)dreizehnterMtp
                    });
                    totalLohn += dreizehnterMtp;
                }
                thirteenthPctForSaldo  = 0;   // Saldo geleert, keine weitere Rückstellung
                prevThirteenthForSaldo = 0;
            }
            else if (thirteenthPct > 0)
            {
                // Nicht-Auszahlungsmonat: 13.-ML-Zuwachs als reine Berechnungs-Zeile
                // anzeigen (betrag=0, accrued=currentAccrual) — analog zur
                // Ferienentschädigung. So sieht der MA monatlich, wie sich der
                // 13.-ML akkumuliert. Der Betrag wandert über thirteenthPctForSaldo
                // weiter in den Saldo-Block "Rückst. 13. Monatslohn".
                decimal currentAccrual = Math.Round(totalLohn * thirteenthPct / 100m, 2);
                if (currentAccrual > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)Math.Round(totalLohn, 2),
                        betrag      = 0m,                 // keine Auszahlung
                        accrued     = (decimal?)currentAccrual
                    });
                }
            }

            // ── Krankheit: 80%-Gutschrift (nach Karenz, nach 13. ML einfügen) ──
            // Versicherungsleistung: BVG + QST pflichtig (Lohnraster 70.2),
            // aber KEIN AHV/ALV/NBU/KTG. Auch kein 13. ML-/Ferien-/Feiertag-Aufbau.
            if (krank80Mtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70", "Krankheit (Taggeld 80%)"),
                    anzahl  = (decimal?)krankTage80Mtp,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(krankTagesBasisMtp, 2),
                    betrag  = krank80Mtp,
                    accrued = (decimal?)krank80Mtp
                });
                totalLohn += krank80Mtp;
                AddAmount("70", krank80Mtp);
                deltaBvg += krank80Mtp;   // BVG-pflichtig (L-GAV Art. 23)
                deltaQst += krank80Mtp;   // QST-pflichtig
            }

            // Unfall: 80%-Gutschrift (nach Karenz, nach 13. ML) — analog Krank.
            if (unfall80Mtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60", "Unfall (Taggeld 80%)"),
                    anzahl  = (decimal?)unfallTage80Mtp,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(unfallTagesBasisMtp, 2),
                    betrag  = unfall80Mtp,
                    accrued = (decimal?)unfall80Mtp
                });
                totalLohn += unfall80Mtp;
                AddAmount("60", unfall80Mtp);
                deltaBvg += unfall80Mtp;
                deltaQst += unfall80Mtp;
            }

            // BVG-Wartefrist (GastroSocial, 3 Monate auf 100%-Lohn).
            // Krank und Unfall haben jeweils ihre eigene Wartefrist — die
            // fehlende Differenz zum Vollohn wird hier zur BVG-Basis addiert.
            deltaBvg += krankBvgKorrekturMtp + unfallBvgKorrekturMtp;

            var svBasesMtp = new SvBases(mainLohnMtp + deltaAhv  + dreizehnterMtp,
                                         mainLohnMtp + deltaNbuv + dreizehnterMtp,
                                         mainLohnMtp + deltaKtg  + dreizehnterMtp,
                                         mainLohnMtp + deltaBvg  + dreizehnterMtp,
                                         mainLohnMtp + deltaQst  + dreizehnterMtp);

            // ── Quellensteuer-Abzug (MTP) ─────────────────────────────────
            var qstRule = ComputeQstDeduction(qstEinstellung, svBasesMtp.Qst, companyProfileId, periodFrom);
            if (qstRule is not null) deductions.Add(qstRule);

            var result = BuildResult(employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn, svBasesMtp,
                zulagenExtraLines, zulagenExtraTotal, abzuegeExtraLines, abzuegeExtraTotal,
                new SaldoBlock(
                    VormonatHourSaldo:    vormonatHourSaldo,
                    NeuerHourSaldo:       neuerSaldo,
                    WorkedHours:          workedHours,
                    SollStunden:          sollStunden,
                    Mehrstunden:          mehrstundenAus,
                    AbsenzGutschrift:     absenzGutschrift,
                    SollStundenVoll:        sollStundenVoll,
                    SollFerienReduktion:    Math.Round(ferienStundenAequivalent, 2),
                    GuaranteedHoursPerWeek: guaranteedH,
                    FerienTageInPeriode:    Math.Round(mtpFerienTage, 2),
                    FerienKuerzungVorschlag:     kuerzungVorschlag,
                    FerienKuerzungVorschlagTage: kuerzungVorschlagTage,
                    NightHours:           nightHours,
                    NightBonus:           nightBonus,
                    NachtKompStunden:     nachtKompStunden,
                    VormonatNachtSaldo:   vormonatNachtSaldo,
                    NeuerNachtSaldo:      neuerNachtSaldo,
                    VacationWeeks:        vacationWeeks,
                    VormonatFerienTage:   vormonatFerienTage,
                    FerienTageAccrual:    ferienTageAccrual,
                    FerienTageGenommen:   ferienTageGenommen,
                    FerienTageSaldoNeu:   ferienTageSaldoNeu,
                    VormonatFerienGeld:   vormonatFerienGeld,
                    FerienGeldSaldoNeu:   ferienGeldSaldoNeu,
                    FerienGeldAuszahlung: ferienGeldAuszahlung,
                    VormonatFeiertagTage: vormonatFeiertagTage,
                    FeiertagTageAccrual:  feiertagTageAccrual,
                    FeiertagTageGenommen: feiertagTageGenommen,
                    FeiertagTageSaldoNeu: feiertagTageSaldoNeu,
                    ThirteenthPct:        thirteenthPctForSaldo,
                    PrevThirteenth:       prevThirteenthForSaldo),
                lohnAssignments, usingDefaultDeductions, periodeFooterText: periodeFooterText);
            return Ok(result);
        }
        else if (isUTP)
        {
            // ── UTP ──────────────────────────────────────────────────────
            // Zwischenbeträge auf 2 Dezimalen (keine 0.05-Pre-Rundung); erst
            // Brutto/Netto/Auszahlung am Ende kaufmännisch auf 0.05 runden.
            decimal lohnBrutto       = Math.Round(workedHours * hourlyRate, 2);
            decimal nachtKompBrutto  = Math.Round(utpAuszahlungStunden * hourlyRate, 2);
            decimal feiertagAusz     = Math.Round(feiertagStunden * hourlyRate, 2);

            // UTP: Feiertag-Basis aus Lohnpositions-Flags
            //   → Stundenlohn (20.1) trägt ZaehltAlsBasisFeiertag=true
            //   → zusätzlich fliessen alle Zulagen mit der Flag ein.
            //   → Nacht-Kompensation wird unter demselben Code geführt
            //     (SV-gleich wie Stundenlohn).
            AddAmount("20", lohnBrutto + nachtKompBrutto);
            decimal feiertagBasisUtp = SumByFlag(lp => lp.ZaehltAlsBasisFeiertag);
            decimal feiertagEnt      = Math.Round(feiertagBasisUtp * holidayPct / 100m, 2);

            lohnLines.Add(new { bezeichnung = "Stundenlohn", anzahl = (decimal?)workedHours, prozent = (decimal?)null, basis = (decimal?)hourlyRate, betrag = lohnBrutto, accrued = (decimal?)lohnBrutto });
            totalLohn += lohnBrutto;

            if (nachtKompBrutto > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = "Nacht-Kompensation",
                    anzahl  = (decimal?)utpAuszahlungStunden,
                    prozent = (decimal?)null,
                    basis   = (decimal?)hourlyRate,
                    betrag  = nachtKompBrutto,
                    accrued = (decimal?)nachtKompBrutto
                });
                totalLohn += nachtKompBrutto;
            }

            if (feiertagAusz > 0)
            {
                lohnLines.Add(new { bezeichnung = "Ausbezahlte Feiertage", anzahl = (decimal?)feiertagStunden, prozent = (decimal?)null, basis = (decimal?)null, betrag = feiertagAusz, accrued = (decimal?)feiertagAusz });
                totalLohn += feiertagAusz;
            }
            if (feiertagEnt > 0)
            {
                lohnLines.Add(new { bezeichnung = "Feiertagentschädigung", anzahl = (decimal?)null, prozent = (decimal?)holidayPct, basis = (decimal?)feiertagBasisUtp, betrag = feiertagEnt, accrued = (decimal?)feiertagEnt });
                totalLohn += feiertagEnt;
                AddAmount("50", feiertagEnt);  // Basis-Tracking (Ausbezahlte Feiertage UTP)
            }

            // UTP-Kaskade: Ferien-Basis enthält auch die Feiertagentschädigung.
            //   → Stundenlohn (20.1) und Stundenlohn Feiertage (20.3) tragen beide
            //     ZaehltAlsBasisFerien=true.
            decimal ferienBasisUtp = SumByFlag(lp => lp.ZaehltAlsBasisFerien);
            decimal ferienEnt      = Math.Round(ferienBasisUtp * vacationPct / 100m, 2);
            if (ferienEnt > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = "Ferienentschädigung",
                    anzahl  = (decimal?)null,
                    prozent = (decimal?)vacationPct,
                    basis   = (decimal?)ferienBasisUtp,
                    betrag  = 0m,
                    accrued = (decimal?)ferienEnt
                });
            }

            (ferienGeldAuszahlung, ferienGeldSaldoNeu) = CalcFerienGeld(
                vormonatFerienGeld, ferienEnt, vormonatFerienTage, ferienTageSaldoNeu,
                ferienTageGenommen, ref lohnLines, ref totalLohn, vacationPct, lohnBrutto);

            // Manuelle Ferien-Geld-Saldo-Auszahlung (Code 195.3): reduziert
            // das Saldo. Der Betrag wurde schon als SV-pflichtige Zulage
            // zu totalLohn addiert — hier nur noch die Saldo-Führung.
            if (ferienGeldAuszahlungManuell > 0)
            {
                ferienGeldAuszahlung += ferienGeldAuszahlungManuell;
                ferienGeldSaldoNeu   = Math.Max(0m, ferienGeldSaldoNeu - ferienGeldAuszahlungManuell);
            }

            // Automatische Jahresend-Auszahlung des Ferien-Geld-Saldos (UTP).
            // Synthetische 195.3-Zeile mit dem aktuellen Saldo, voll SV-pflichtig.
            if (autoDezemberAuszahlung && lpFerienAuszahlung != null && ferienGeldSaldoNeu > 0)
            {
                decimal autoBetrag = Math.Round(ferienGeldSaldoNeu, 2);
                lohnLines.Add(new {
                    bezeichnung = lpFerienAuszahlung.Bezeichnung + " (Jahresende)",
                    anzahl  = (decimal?)null,
                    prozent = (decimal?)null,
                    basis   = (decimal?)null,
                    betrag  = autoBetrag,
                    accrued = (decimal?)autoBetrag
                });
                totalLohn += autoBetrag;
                if (lpFerienAuszahlung.AhvAlvPflichtig) deltaAhv  += autoBetrag;
                if (lpFerienAuszahlung.NbuvPflichtig)   deltaNbuv += autoBetrag;
                if (lpFerienAuszahlung.KtgPflichtig)    deltaKtg  += autoBetrag;
                if (lpFerienAuszahlung.BvgPflichtig)    deltaBvg  += autoBetrag;
                if (lpFerienAuszahlung.QstPflichtig)    deltaQst  += autoBetrag;
                AddAmount(lpFerienAuszahlung.Code, autoBetrag);
                ferienGeldAuszahlung += autoBetrag;
                ferienGeldSaldoNeu    = 0m;
            }

            decimal neuerNachtSaldoUtp = Math.Round(vormonatNachtSaldo + nightBonus - nachtKompStunden, 2);

            // ── SV-pflichtige Zulagen zu totalLohn addieren (UTP) ─────────
            decimal mainLohnUtp = totalLohn;
            lohnLines.AddRange(zulagenSvLines);
            totalLohn += zulagenSvTotal;

            // ── 13. Monatslohn monatlich auszahlen (UTP) ───────────────────
            decimal dreizehnterUtp = 0;
            if (thirteenthPct > 0)
            {
                decimal basis13 = totalLohn;
                dreizehnterUtp = Math.Round(basis13 * thirteenthPct / 100m, 2);
                if (dreizehnterUtp > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)Math.Round(basis13, 2),
                        betrag      = dreizehnterUtp,
                        accrued     = (decimal?)dreizehnterUtp
                    });
                    totalLohn += dreizehnterUtp;
                }
            }

            // ── Krankheit UTP: 88%/80% vom KTG-Tagessatz (inkl. Aufschläge) ──
            // Basis = Tagessatz100 aus KtgTagessatzService — der enthält bereits
            // Ferien/Feiertag/13. ML (Regel A: MaxPartTimeHours × stdLohnBrutto × 52/365;
            // Regel B: AHV-Ø der letzten Monate × 12/365). Wir fügen NACH 13. ML
            // ein, damit darauf kein weiterer Aufschlag gerechnet wird, und
            // schreiben direkt in delta* um die SV-Basis korrekt zu setzen.
            // Unfall UTP nutzt denselben Tagessatz (gleiche Berechnung).
            decimal krankTagesBasisUtp = 0m;
            if (krankBreakdown.Count > 0 || unfallBreakdown.Count > 0)
            {
                var ktgUtp = await _ktgService.CalculateAsync(employeeId, companyProfileId);
                krankTagesBasisUtp = ktgUtp?.Tagessatz100 ?? 0m;
            }
            decimal krank88Utp    = 0m;
            decimal krank80Utp    = 0m;
            decimal krankTage88Utp = 0m, krankTage80Utp = 0m;
            decimal krankBvgKorrekturUtp = 0m;
            if (krankTagesBasisUtp > 0)
            {
                foreach (var t in krankBreakdown)
                {
                    decimal tagWert = krankTagesBasisUtp * (t.Prozent / 100m);
                    if (t.InKarenz)
                    {
                        krank88Utp     += tagWert * 0.88m;
                        krankTage88Utp += t.Prozent / 100m;
                        if (t.BvgAuf100) krankBvgKorrekturUtp += tagWert * 0.12m;
                    }
                    else
                    {
                        krank80Utp     += tagWert * 0.80m;
                        krankTage80Utp += t.Prozent / 100m;
                        if (t.BvgAuf100) krankBvgKorrekturUtp += tagWert * 0.20m;
                    }
                }
                krank88Utp           = Math.Round(krank88Utp,           2);
                krank80Utp           = Math.Round(krank80Utp,           2);
                krankBvgKorrekturUtp = Math.Round(krankBvgKorrekturUtp, 2);
            }

            // 88%: voll SV-pflichtig (AhvAlv/Nbu/Ktg/Bvg/Qst). NACH 13. ML,
            // damit kein weiterer 13. ML-Aufschlag (Tagessatz100 enthält bereits 8.33%).
            if (krank88Utp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70", "Krankheit (Karenzentschädigung)"),
                    anzahl  = (decimal?)krankTage88Utp,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(krankTagesBasisUtp, 2),
                    betrag  = krank88Utp,
                    accrued = (decimal?)krank88Utp
                });
                totalLohn += krank88Utp;
                // Manuelle Delta-Updates (kein AddAmount — Aufschläge sind
                // schon im Tagessatz100 enthalten).
                deltaAhv  += krank88Utp;
                deltaNbuv += krank88Utp;
                deltaKtg  += krank88Utp;
                deltaBvg  += krank88Utp;
                deltaQst  += krank88Utp;
            }
            // 80%: Versicherungsleistung, nur BVG + QST pflichtig.
            if (krank80Utp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70", "Krankheit (Taggeld 80%)"),
                    anzahl  = (decimal?)krankTage80Utp,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(krankTagesBasisUtp, 2),
                    betrag  = krank80Utp,
                    accrued = (decimal?)krank80Utp
                });
                totalLohn += krank80Utp;
                deltaBvg  += krank80Utp;
                deltaQst  += krank80Utp;
            }

            // ── Unfall UTP: identische Logik wie Krankheit UTP ────────────
            // Gleicher Tagessatz (KtgTagessatzService), gleiche SV-Behandlung.
            // Eigene Kumulation → eigene Tage-Grenze (Default 2) aus
            // CompanyProfile.KarenzTageMaxUnfall.
            decimal unfall88Utp    = 0m;
            decimal unfall80Utp    = 0m;
            decimal unfallTage88Utp = 0m, unfallTage80Utp = 0m;
            decimal unfallBvgKorrekturUtp = 0m;
            if (krankTagesBasisUtp > 0)
            {
                foreach (var t in unfallBreakdown)
                {
                    decimal tagWert = krankTagesBasisUtp * (t.Prozent / 100m);
                    if (t.InKarenz)
                    {
                        unfall88Utp     += tagWert * 0.88m;
                        unfallTage88Utp += t.Prozent / 100m;
                        if (t.BvgAuf100) unfallBvgKorrekturUtp += tagWert * 0.12m;
                    }
                    else
                    {
                        unfall80Utp     += tagWert * 0.80m;
                        unfallTage80Utp += t.Prozent / 100m;
                        if (t.BvgAuf100) unfallBvgKorrekturUtp += tagWert * 0.20m;
                    }
                }
                unfall88Utp           = Math.Round(unfall88Utp,           2);
                unfall80Utp           = Math.Round(unfall80Utp,           2);
                unfallBvgKorrekturUtp = Math.Round(unfallBvgKorrekturUtp, 2);
            }
            if (unfall88Utp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60", "Unfall (Karenzentschädigung)"),
                    anzahl  = (decimal?)unfallTage88Utp,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(krankTagesBasisUtp, 2),
                    betrag  = unfall88Utp,
                    accrued = (decimal?)unfall88Utp
                });
                totalLohn += unfall88Utp;
                deltaAhv  += unfall88Utp;
                deltaNbuv += unfall88Utp;
                deltaKtg  += unfall88Utp;
                deltaBvg  += unfall88Utp;
                deltaQst  += unfall88Utp;
            }
            if (unfall80Utp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60", "Unfall (Taggeld 80%)"),
                    anzahl  = (decimal?)unfallTage80Utp,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(krankTagesBasisUtp, 2),
                    betrag  = unfall80Utp,
                    accrued = (decimal?)unfall80Utp
                });
                totalLohn += unfall80Utp;
                deltaBvg  += unfall80Utp;
                deltaQst  += unfall80Utp;
            }

            // BVG-Wartefrist: siehe MTP-Kommentar.
            deltaBvg += krankBvgKorrekturUtp + unfallBvgKorrekturUtp;

            var svBasesUtp = new SvBases(mainLohnUtp + deltaAhv  + dreizehnterUtp,
                                         mainLohnUtp + deltaNbuv + dreizehnterUtp,
                                         mainLohnUtp + deltaKtg  + dreizehnterUtp,
                                         mainLohnUtp + deltaBvg  + dreizehnterUtp,
                                         mainLohnUtp + deltaQst  + dreizehnterUtp);

            // ── Quellensteuer-Abzug (UTP) ─────────────────────────────────
            var qstRuleUtp = ComputeQstDeduction(qstEinstellung, svBasesUtp.Qst, companyProfileId, periodFrom);
            if (qstRuleUtp is not null) deductions.Add(qstRuleUtp);

            // UTP: 13. ML monatlich ausbezahlt → keine Rückstellung mehr
            var result = BuildResult(employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn, svBasesUtp,
                zulagenExtraLines, zulagenExtraTotal, abzuegeExtraLines, abzuegeExtraTotal,
                new SaldoBlock(
                    VormonatHourSaldo:    0,
                    NeuerHourSaldo:       0,
                    WorkedHours:          workedHours,
                    SollStunden:          0,
                    Mehrstunden:          0,
                    AbsenzGutschrift:     0m,
                    NightHours:           nightHours,
                    NightBonus:           nightBonus,
                    NachtKompStunden:     nachtKompStunden,
                    VormonatNachtSaldo:   vormonatNachtSaldo,
                    NeuerNachtSaldo:      neuerNachtSaldoUtp,
                    VacationWeeks:        vacationWeeks,
                    VormonatFerienTage:   vormonatFerienTage,
                    FerienTageAccrual:    ferienTageAccrual,
                    FerienTageGenommen:   ferienTageGenommen,
                    FerienTageSaldoNeu:   ferienTageSaldoNeu,
                    VormonatFerienGeld:   vormonatFerienGeld,
                    FerienGeldSaldoNeu:   ferienGeldSaldoNeu,
                    FerienGeldAuszahlung: ferienGeldAuszahlung,
                    VormonatFeiertagTage: vormonatFeiertagTage,
                    FeiertagTageAccrual:  feiertagTageAccrual,
                    FeiertagTageGenommen: feiertagTageGenommen,
                    FeiertagTageSaldoNeu: feiertagTageSaldoNeu,
                    ThirteenthPct:        0m,
                    PrevThirteenth:       prevThirteenth,
                    FerienKuerzungVorschlag:     kuerzungVorschlag,
                    FerienKuerzungVorschlagTage: kuerzungVorschlagTage),
                lohnAssignments, usingDefaultDeductions, periodeFooterText: periodeFooterText);
            return Ok(result);
        }
        else // FIX / FIX-M – Monatslohn + Stunden-Saldo (Soll/Ist), kein Mehrstunden-Auszahlung
        {
            decimal pct            = emp.EmploymentPercentage ?? 100m;
            // MonthlySalary enthält den tatsächlichen Lohn (nach Pensum), MonthlySalaryFte den 100%-Wert
            // Monatslohn: auf 2 Dezimalen (keine 0.05-Pre-Rundung)
            decimal monthSalaryFull = emp.MonthlySalary ?? Math.Round((emp.MonthlySalaryFte ?? 0) * pct / 100m, 2);
            decimal fteSalary       = emp.MonthlySalaryFte ?? (pct > 0 ? Math.Round(monthSalaryFull * 100m / pct, 2) : monthSalaryFull);

            // Bei Austritt innerhalb der Periode: Monatslohn per Tagessatz-Formel
            //   Tagessatz = MonthlySalary × 12 / 365
            //   Lohn      = Tagessatz × Kalendertage der Kurzperiode
            decimal monthSalary = isShortPeriod
                ? Math.Round(monthSalaryFull * 12m / 365m * shortPeriodDays, 2)
                : monthSalaryFull;

            string monatslohnLabel = isShortPeriod
                ? $"Monatslohn ({shortPeriodDays} von {normalPeriodDays} Tagen – Austritt {periodTo:dd.MM.yyyy})"
                : "Monatslohn";

            // ── FIX/FIX-M Festlohn-Split (Mirus-Style) ────────────────────
            // Festlohn wird in 3 Lohnzeilen aufgeteilt:
            //   10  "Festlohn"                       (Arbeit, gekürzt)
            //   2   "Festlohn für bezogene Ferien"   (Tagessatz × Ferientage)
            //   3   "Festlohn für bezogene Feiertage" (Tagessatz × Feiertage)
            // Total = monthSalary (nichts ändert sich am Brutto, nur klare
            // Aufschlüsselung — analog Mirus, ohne -/+ Aufrechnung).
            decimal fixTagessatz = monthSalaryFull * 12m / 365m;
            decimal ferienBetragFix   = Math.Round(fixTagessatz * ferienTageGenommen, 2);
            decimal feiertagBetragFix = Math.Round(fixTagessatz * feiertagTageGenommen, 2);
            decimal festlohnArbeitFix = Math.Round(monthSalary - ferienBetragFix - feiertagBetragFix, 2);

            lohnLines.Add(new
            {
                bezeichnung = monatslohnLabel,
                anzahl      = (decimal?)null,
                prozent     = pct < 100m ? (decimal?)pct : (decimal?)null,
                basis       = pct < 100m ? (decimal?)Math.Round(fteSalary, 2) : (decimal?)null,
                betrag      = festlohnArbeitFix,
                accrued     = (decimal?)festlohnArbeitFix
            });
            totalLohn += festlohnArbeitFix;
            AddAmount("10", festlohnArbeitFix);

            if (ferienBetragFix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("2", "Festlohn für bezogene Ferien"),
                    anzahl  = (decimal?)Math.Round(ferienTageGenommen, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(fixTagessatz, 2),
                    betrag  = ferienBetragFix,
                    accrued = (decimal?)ferienBetragFix
                });
                totalLohn += ferienBetragFix;
                AddAmount("2", ferienBetragFix);
            }

            if (feiertagBetragFix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("3", "Festlohn für bezogene Feiertage"),
                    anzahl  = (decimal?)Math.Round(feiertagTageGenommen, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(fixTagessatz, 2),
                    betrag  = feiertagBetragFix,
                    accrued = (decimal?)feiertagBetragFix
                });
                totalLohn += feiertagBetragFix;
                AddAmount("3", feiertagBetragFix);
            }

            // ── Krankheit: Lohnkürzung + 88%-Gutschrift (FIX / FIX-M) ──
            // Basis = MonthlySalary × 12 / 365 (1/365-Tageswert des Fixlohns).
            // 80%-Gutschrift kommt nach dem mainLohn-Snapshot, siehe unten.
            // BVG-Korrektur für Tage in der 3-Monate-Wartefrist: siehe MTP.
            // Tagessatz für Krankheit basiert immer auf dem VOLLEN Monatslohn,
            // nicht auf dem bereits kurz-pro-ratierten monthSalary (sonst würde
            // der Abzug in der Austritts-Kurzperiode doppelt reduziert).
            decimal krankTagesBasisFix = monthSalaryFull * 12m / 365m;
            decimal krankAbzugFix = 0m, krank88Fix = 0m, krank80Fix = 0m;
            decimal krankTage88Fix = 0m, krankTage80Fix = 0m;
            decimal krankBvgKorrekturFix = 0m;
            foreach (var t in krankBreakdown)
            {
                decimal tagWert = krankTagesBasisFix * (t.Prozent / 100m);
                krankAbzugFix += tagWert;
                if (t.InKarenz)
                {
                    krank88Fix += tagWert * 0.88m;
                    krankTage88Fix += t.Prozent / 100m;
                    if (t.BvgAuf100) krankBvgKorrekturFix += tagWert * 0.12m;
                }
                else
                {
                    krank80Fix += tagWert * 0.80m;
                    krankTage80Fix += t.Prozent / 100m;
                    if (t.BvgAuf100) krankBvgKorrekturFix += tagWert * 0.20m;
                }
            }
            krankAbzugFix        = Math.Round(krankAbzugFix,        2);
            krank88Fix           = Math.Round(krank88Fix,           2);
            krank80Fix           = Math.Round(krank80Fix,           2);
            krankBvgKorrekturFix = Math.Round(krankBvgKorrekturFix, 2);

            if (krankAbzugFix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("75", "Korrektur Krankheit"),
                    anzahl  = (decimal?)krankBreakdown.Count,
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(krankTagesBasisFix, 2),
                    betrag  = -krankAbzugFix,
                    accrued = (decimal?)(-krankAbzugFix)
                });
                totalLohn -= krankAbzugFix;
                AddAmount("75", -krankAbzugFix);
            }
            if (krank88Fix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70", "Krankheit (Karenzentschädigung)"),
                    anzahl  = (decimal?)krankTage88Fix,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(krankTagesBasisFix, 2),
                    betrag  = krank88Fix,
                    accrued = (decimal?)krank88Fix
                });
                totalLohn += krank88Fix;
                AddAmount("70", krank88Fix);
            }

            // ── Unfall FIX/FIX-M: identische Logik wie Krankheit ──────────
            // Gleicher Tageswert (monthSalary × 12 / 365), eigene Tage-Grenze
            // (Default 2) aus CompanyProfile.KarenzTageMaxUnfall. BVG-
            // Wartefrist läuft separat (eigene 3 Monate ab Unfall-Beginn).
            decimal unfallTagesBasisFix = krankTagesBasisFix;
            decimal unfallAbzugFix = 0m, unfall88Fix = 0m, unfall80Fix = 0m;
            decimal unfallTage88Fix = 0m, unfallTage80Fix = 0m;
            decimal unfallBvgKorrekturFix = 0m;
            foreach (var t in unfallBreakdown)
            {
                decimal tagWert = unfallTagesBasisFix * (t.Prozent / 100m);
                unfallAbzugFix += tagWert;
                if (t.InKarenz)
                {
                    unfall88Fix += tagWert * 0.88m;
                    unfallTage88Fix += t.Prozent / 100m;
                    if (t.BvgAuf100) unfallBvgKorrekturFix += tagWert * 0.12m;
                }
                else
                {
                    unfall80Fix += tagWert * 0.80m;
                    unfallTage80Fix += t.Prozent / 100m;
                    if (t.BvgAuf100) unfallBvgKorrekturFix += tagWert * 0.20m;
                }
            }
            unfallAbzugFix        = Math.Round(unfallAbzugFix,        2);
            unfall88Fix           = Math.Round(unfall88Fix,           2);
            unfall80Fix           = Math.Round(unfall80Fix,           2);
            unfallBvgKorrekturFix = Math.Round(unfallBvgKorrekturFix, 2);

            if (unfallAbzugFix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("65", "Korrektur Unfall"),
                    anzahl  = (decimal?)unfallBreakdown.Count,
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(unfallTagesBasisFix, 2),
                    betrag  = -unfallAbzugFix,
                    accrued = (decimal?)(-unfallAbzugFix)
                });
                totalLohn -= unfallAbzugFix;
                AddAmount("65", -unfallAbzugFix);
            }
            if (unfall88Fix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60", "Unfall (Karenzentschädigung)"),
                    anzahl  = (decimal?)unfallTage88Fix,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(unfallTagesBasisFix, 2),
                    betrag  = unfall88Fix,
                    accrued = (decimal?)unfall88Fix
                });
                totalLohn += unfall88Fix;
                AddAmount("60", unfall88Fix);
            }

            // ── Stunden-Saldo (FIX / FIX-M) ─────────────────────────────
            // Sollstunden für die Lohnperiode =
            //   wöchentliche Sollstunden / 7 × Anzahl Kalendertage der Periode.
            //
            // Wöchentliche Sollstunden:
            //   1. expliziter WeeklyHours-Wert auf der Anstellung
            //   2. sonst aus Pensum × NormalWeeklyHours der Filiale
            //   3. Fallback: 42h (GAV Gastro)
            //
            // Der Wert variiert dadurch monatlich (z. B. 31 vs. 28 Tage) und
            // entspricht der gleichen Logik, die bei Absenzberechnungen im
            // Frontend schon verwendet wird.
            decimal normalWeekly = company.NormalWeeklyHours ?? 42m;
            decimal weeklySoll   = emp.WeeklyHours ?? Math.Round(normalWeekly * pct / 100m, 2);
            int periodDays       = periodTo.DayNumber - periodFrom.DayNumber + 1;
            decimal sollStundenFix = Math.Round(weeklySoll / 7m * periodDays, 2);

            // Ist-/Saldo-Berechnung (wie MTP, aber ohne Payout):
            //   Netto = Worked + AbsenzGutschrift − Soll + Vormonat-Saldo
            //   → Neuer Saldo (kann positiv oder negativ sein; keine Auszahlung).
            decimal nettoHFix      = workedHours + absenzGutschrift - sollStundenFix + vormonatHourSaldo;
            decimal neuerHourSaldoFix = Math.Round(nettoHFix, 2);

            decimal neuerNachtSaldoFix = Math.Round(vormonatNachtSaldo + nightBonus - nachtKompStunden, 2);

            // ── SV-pflichtige Zulagen zu totalLohn addieren (FIX) ─────────
            decimal mainLohnFix = totalLohn;
            lohnLines.AddRange(zulagenSvLines);
            totalLohn += zulagenSvTotal;

            // ── 13. Monatslohn: Auszahlung oder Rückstellung je Firmen-Rhythmus ─
            bool isPayoutMonthFix = IsThirteenthPayoutMonth(company.ThirteenthMonthPayoutsPerYear, month);
            decimal dreizehnterFix = 0;
            decimal thirteenthPctForSaldoFix  = thirteenthPct;
            decimal prevThirteenthForSaldoFix = prevThirteenth;
            if (thirteenthPct > 0 && isPayoutMonthFix)
            {
                decimal currentAccrual = Math.Round(totalLohn * thirteenthPct / 100m, 2);
                dreizehnterFix = Math.Round(prevThirteenth + currentAccrual, 2);
                if (dreizehnterFix > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = prevThirteenth > 0
                            ? $"13. Monatslohn (inkl. CHF {prevThirteenth:F2} Saldo)"
                            : "13. Monatslohn",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)Math.Round(totalLohn, 2),
                        betrag      = dreizehnterFix,
                        accrued     = (decimal?)dreizehnterFix
                    });
                    totalLohn += dreizehnterFix;
                }
                thirteenthPctForSaldoFix  = 0;
                prevThirteenthForSaldoFix = 0;
            }

            // ── Krankheit: 80%-Gutschrift (nach Karenz, nach 13. ML) ──────
            // Versicherungsleistung: BVG + QST pflichtig (Lohnraster 70.2),
            // keine AHV/ALV/NBU/KTG.
            if (krank80Fix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70", "Krankheit (Taggeld 80%)"),
                    anzahl  = (decimal?)krankTage80Fix,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(krankTagesBasisFix, 2),
                    betrag  = krank80Fix,
                    accrued = (decimal?)krank80Fix
                });
                totalLohn += krank80Fix;
                AddAmount("70", krank80Fix);
                deltaBvg += krank80Fix;   // BVG-pflichtig (L-GAV Art. 23)
                deltaQst += krank80Fix;
            }

            // Unfall: 80%-Gutschrift (nach Karenz, nach 13. ML) — analog Krank.
            if (unfall80Fix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60", "Unfall (Taggeld 80%)"),
                    anzahl  = (decimal?)unfallTage80Fix,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(unfallTagesBasisFix, 2),
                    betrag  = unfall80Fix,
                    accrued = (decimal?)unfall80Fix
                });
                totalLohn += unfall80Fix;
                AddAmount("60", unfall80Fix);
                deltaBvg += unfall80Fix;
                deltaQst += unfall80Fix;
            }

            // BVG-Wartefrist: siehe MTP-Kommentar.
            deltaBvg += krankBvgKorrekturFix + unfallBvgKorrekturFix;

            var svBasesFix = new SvBases(mainLohnFix + deltaAhv  + dreizehnterFix,
                                          mainLohnFix + deltaNbuv + dreizehnterFix,
                                          mainLohnFix + deltaKtg  + dreizehnterFix,
                                          mainLohnFix + deltaBvg  + dreizehnterFix,
                                          mainLohnFix + deltaQst  + dreizehnterFix);

            // ── Quellensteuer-Abzug (FIX) ─────────────────────────────────
            var qstRuleFix = ComputeQstDeduction(qstEinstellung, svBasesFix.Qst, companyProfileId, periodFrom);
            if (qstRuleFix is not null) deductions.Add(qstRuleFix);

            var result = BuildResult(employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn, svBasesFix,
                zulagenExtraLines, zulagenExtraTotal, abzuegeExtraLines, abzuegeExtraTotal,
                new SaldoBlock(
                    VormonatHourSaldo:    vormonatHourSaldo,
                    NeuerHourSaldo:       neuerHourSaldoFix,
                    WorkedHours:          workedHours,
                    SollStunden:          sollStundenFix,
                    Mehrstunden:          0,
                    AbsenzGutschrift:     absenzGutschrift,
                    NightHours:           nightHours,
                    NightBonus:           nightBonus,
                    NachtKompStunden:     nachtKompStunden,
                    VormonatNachtSaldo:   vormonatNachtSaldo,
                    NeuerNachtSaldo:      neuerNachtSaldoFix,
                    VacationWeeks:        vacationWeeks,
                    VormonatFerienTage:   vormonatFerienTage,
                    FerienTageAccrual:    ferienTageAccrual,
                    FerienTageGenommen:   ferienTageGenommen,
                    FerienTageSaldoNeu:   ferienTageSaldoNeu,
                    // FIX: kein Ferien-Geld-Saldo (Feriengeld ist im Monatslohn enthalten)
                    VormonatFerienGeld:   0,
                    FerienGeldSaldoNeu:   0,
                    FerienGeldAuszahlung: 0,
                    VormonatFeiertagTage: vormonatFeiertagTage,
                    FeiertagTageAccrual:  feiertagTageAccrual,
                    FeiertagTageGenommen: feiertagTageGenommen,
                    FeiertagTageSaldoNeu: feiertagTageSaldoNeu,
                    ThirteenthPct:        thirteenthPctForSaldoFix,
                    PrevThirteenth:       prevThirteenthForSaldoFix,
                    FerienKuerzungVorschlag:     kuerzungVorschlag,
                    FerienKuerzungVorschlagTage: kuerzungVorschlagTage),
                lohnAssignments, usingDefaultDeductions, periodeFooterText: periodeFooterText);
            return Ok(result);
        }
      } // end try
      catch (Exception ex)
      {
          var inner = ex.InnerException?.Message ?? "";
          return StatusCode(500, new { error = ex.Message, detail = inner });
      }
    }

    // POST /api/payroll/save – Saldo speichern (Zwischenstand, "draft")
    //
    // WICHTIG: Dieser Endpunkt darf den Status NIE auf "confirmed" setzen.
    // Der einzige legitime Weg zu "confirmed" ist POST /api/payroll/confirm,
    // weil nur dort zusammen mit dem Saldo auch der PayrollSnapshot geschrieben
    // wird. Würde /save ein "confirmed" durchlassen, entstünden Saldos ohne
    // zugehörigen Snapshot → das bricht den Reopen-Flow und die Jahresausweise.
    [HttpPost("save")]
    public async Task<IActionResult> SaveSaldo([FromBody] SaveSaldoDto dto)
    {
        var existing = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId    == dto.EmployeeId
                                   && s.PeriodYear    == dto.Year
                                   && s.PeriodMonth   == dto.Month
                                   && s.CompanyProfileId == dto.CompanyProfileId);

        // Einen bereits bestätigten Saldo über /save zu überschreiben würde den
        // Snapshot-Status aus der Synchronität kippen. Dafür gibt es /reopen.
        if (existing is not null && existing.Status == "confirmed")
            return Conflict(new {
                error = "Saldo ist bereits bestätigt. Bitte zuerst über /reopen wieder eröffnen."
            });

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
        existing.FeiertagTageSaldo           = dto.FeiertagTageSaldo;
        existing.ThirteenthMonthMonthly      = dto.ThirteenthMonthMonthly;
        existing.ThirteenthMonthAccumulated  = dto.ThirteenthMonthAccumulated;
        existing.GrossAmount                 = dto.GrossAmount;
        existing.NetAmount                   = dto.NetAmount;
        // Nie "confirmed" über /save — das ist /confirm vorbehalten.
        existing.Status                      = (dto.Status == "confirmed") ? "draft" : dto.Status;
        existing.UpdatedAt                   = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    // GET /api/payroll/saldo?employeeId=X&year=Y&month=M&companyProfileId=Z
    //
    // CompanyProfileId ist Pflicht: ein Mitarbeiter kann in mehreren Filialen
    // (CompanyProfiles) einen Saldo haben. Ohne diesen Filter würde das
    // FirstOrDefault einen zufälligen davon zurückliefern — und der Reopen-
    // Button im Frontend wäre dann für den falschen Datensatz aktiv.
    // GET /api/payroll/pdf?employeeId=X&year=Y&month=M&companyProfileId=Z
    // Liefert die Lohnabrechnung als PDF (gleicher Look wie der Vertrag).
    // Internally: ruft Calculate (gleiches Lohn-Result) und übergibt es an
    // PayrollPdfService → A4-PDF mit Banner.
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
    {
        // Berechnung über bestehende Calculate-Logik holen
        var calcResult = await Calculate(employeeId, year, month, companyProfileId);
        if (calcResult is not OkObjectResult ok || ok.Value is null)
            return calcResult;  // Fehler oder NotFound durchreichen

        // Result-Objekt → JSON → JsonElement (der PdfService braucht JsonElement)
        var json = System.Text.Json.JsonSerializer.SerializeToElement(ok.Value);
        var pdf = _payrollPdf.Generate(json);

        var employee = await _db.Employees.FindAsync(employeeId);
        var name = employee != null
            ? $"{employee.FirstName}_{employee.LastName}".Replace(" ", "_")
            : $"Mitarbeiter_{employeeId}";
        var fileName = $"Lohnabrechnung_{name}_{year}-{month:D2}.pdf";
        return File(pdf, "application/pdf", fileName);
    }

    [HttpGet("saldo")]
    public async Task<IActionResult> GetSaldo(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
    {
        var saldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId       == employeeId
                                   && s.PeriodYear       == year
                                   && s.PeriodMonth      == month
                                   && s.CompanyProfileId == companyProfileId);
        return Ok(saldo);
    }

    // POST /api/payroll/confirm – Lohn bestätigen: Saldo + Snapshot speichern
    [HttpPost("confirm")]
    public async Task<IActionResult> ConfirmPayroll([FromBody] ConfirmPayrollDto dto)
    {
        // 1) Snapshot-Schutz: abgeschlossene Periode → kein Update
        var snapshot = await _db.PayrollSnapshots
            .Include(s => s.Periode)
            .FirstOrDefaultAsync(s => s.EmployeeId == dto.EmployeeId
                                   && s.PayrollPeriodeId == dto.PayrollPeriodeId);

        if (snapshot?.IsFinal == true)
            return Conflict(new { error = "Lohnperiode ist abgeschlossen. Keine Änderungen mehr möglich." });

        // 2) Saldo speichern (identisch wie /save)
        var saldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId    == dto.EmployeeId
                                   && s.PeriodYear    == dto.Year
                                   && s.PeriodMonth   == dto.Month
                                   && s.CompanyProfileId == dto.CompanyProfileId);
        if (saldo is null)
        {
            saldo = new PayrollSaldo
            {
                EmployeeId       = dto.EmployeeId,
                CompanyProfileId = dto.CompanyProfileId,
                PeriodYear       = dto.Year,
                PeriodMonth      = dto.Month,
                CreatedAt        = DateTime.UtcNow
            };
            _db.PayrollSaldos.Add(saldo);
        }
        saldo.HourSaldo                  = dto.HourSaldo;
        saldo.NachtSaldo                 = dto.NachtSaldo;
        saldo.NightHoursWorked           = dto.NightHoursWorked;
        saldo.FerienGeldSaldo            = dto.FerienGeldSaldo;
        saldo.FerienTageSaldo            = dto.FerienTageSaldo;
        saldo.FeiertagTageSaldo          = dto.FeiertagTageSaldo;
        saldo.ThirteenthMonthMonthly     = dto.ThirteenthMonthMonthly;
        saldo.ThirteenthMonthAccumulated = dto.ThirteenthMonthAccumulated;
        saldo.GrossAmount                = dto.GrossAmount;
        saldo.NetAmount                  = dto.NetAmount;
        saldo.Status                     = "confirmed";
        saldo.UpdatedAt                  = DateTime.UtcNow;

        // 3) Snapshot speichern / aktualisieren
        if (snapshot is null)
        {
            snapshot = new PayrollSnapshot
            {
                PayrollPeriodeId = dto.PayrollPeriodeId,
                EmployeeId       = dto.EmployeeId,
                CompanyProfileId = dto.CompanyProfileId,
                CreatedAt        = DateTime.UtcNow
            };
            _db.PayrollSnapshots.Add(snapshot);
        }
        snapshot.SlipJson               = dto.SlipJson;
        snapshot.Brutto                 = dto.GrossAmount;
        snapshot.Netto                  = dto.NetAmount;
        snapshot.SvBasisAhv             = dto.SvBasisAhv;
        snapshot.SvBasisBvg             = dto.SvBasisBvg;
        snapshot.QstBetrag              = dto.QstBetrag;
        snapshot.ThirteenthAccumulated  = dto.ThirteenthMonthAccumulated;
        snapshot.FerienGeldSaldo        = dto.FerienGeldSaldo;
        snapshot.UpdatedAt              = DateTime.UtcNow;

        // KTG-Tagessatz wird on-demand im GET /api/payroll/ktg-tagessatz berechnet
        // (kein Cache mehr \u2014 ersetzt die fr\u00fchere 6-Monats-\u00d8-Logik)

        // ── Lohnabtretungen: Historien-Einträge + bereits_abgezogen pflegen ──
        // Re-Confirm-sicher (idempotent):
        //   1) Alte Entries für diesen Snapshot werden rückgebucht
        //      (BereitsAbgezogen -= alter Betrag) und gelöscht.
        //   2) Neue Entries werden mit Snapshot der Abtretungs-Regel und
        //      der Behörde (Name, IBAN, QR-IBAN) angelegt — bleiben damit
        //      auch nach späteren Behörden-Umbenennungen korrekt.
        //   3) BereitsAbgezogen wird neu hochgezählt.
        // Diese Einträge sind die Grundlage für DTA-Zahlungsexport und
        // Abacus-FIBU-Buchungen.
        List<PayrollLohnAbtretungEntry> existingEntries = new();
        if (snapshot.Id != 0)
        {
            existingEntries = await _db.PayrollLohnAbtretungEntries
                .Where(e => e.PayrollSnapshotId == snapshot.Id)
                .ToListAsync();
        }
        foreach (var old in existingEntries)
        {
            var laOld = await _db.EmployeeLohnAssignments.FindAsync(old.EmployeeLohnAssignmentId);
            if (laOld != null)
            {
                laOld.BereitsAbgezogen = Math.Max(0, Math.Round(laOld.BereitsAbgezogen - old.Betrag, 2));
                laOld.UpdatedAt        = DateTime.UtcNow;
            }
            _db.PayrollLohnAbtretungEntries.Remove(old);
        }

        if (dto.LohnAbtretungen is { Length: > 0 })
        {
            foreach (var ab in dto.LohnAbtretungen)
            {
                var la = await _db.EmployeeLohnAssignments
                    .Include(x => x.Behoerde)
                    .FirstOrDefaultAsync(x => x.Id == ab.AssignmentId);
                if (la == null) continue;

                decimal betrag = Math.Round(ab.Betrag, 2);
                if (betrag <= 0) continue;

                decimal vorher = la.BereitsAbgezogen;
                la.BereitsAbgezogen = Math.Round(vorher + betrag, 2);
                la.UpdatedAt        = DateTime.UtcNow;

                _db.PayrollLohnAbtretungEntries.Add(new PayrollLohnAbtretungEntry
                {
                    Snapshot                 = snapshot,         // EF setzt FK nach SaveChanges
                    EmployeeLohnAssignmentId = la.Id,
                    EmployeeId               = la.EmployeeId,
                    BehoerdeId               = la.BehoerdeId,
                    PeriodYear               = dto.Year,
                    PeriodMonth              = dto.Month,
                    Bezeichnung              = la.Bezeichnung,
                    ReferenzAmt              = la.ReferenzAmt,
                    ZahlungsReferenz         = la.ZahlungsReferenz,
                    BehoerdeName             = la.Behoerde?.Name,
                    Iban                     = la.Behoerde?.Iban,
                    QrIban                   = la.Behoerde?.QrIban,
                    Betrag                   = betrag,
                    BereitsAbgezogenVorher   = vorher,
                    BereitsAbgezogenNachher  = la.BereitsAbgezogen,
                    CreatedAt                = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { snapshotId = snapshot.Id, message = "Lohn bestätigt und gespeichert." });
    }

    // POST /api/payroll/reopen – Bestätigten Lohn wieder eröffnen
    // Setzt den Saldo zurück auf "draft", entfernt die Lohnabtretungs-
    // Historien-Einträge für diesen Snapshot und macht die
    // bereits_abgezogen-Hochzählung rückgängig. Nur möglich solange die
    // Periode noch nicht final abgeschlossen ist.
    //
    // Robustheit: Der Snapshot wird zuerst über den Periode-Kontext
    // (EmployeeId + CompanyProfileId + Year + Month) gesucht, und erst als
    // Fallback über die rohe PayrollPeriodeId. Damit funktioniert Reopen
    // auch wenn die Periode zwischenzeitlich neu angelegt wurde und das
    // Frontend eine andere PayrollPeriodeId mitschickt als am Snapshot hängt.
    //
    // Recovery: Falls kein Snapshot existiert, aber der Saldo auf "confirmed"
    // steht (Inkonsistenz z.B. aus alten Datenbeständen), wird der Saldo
    // trotzdem auf "draft" zurückgesetzt, damit der User nicht feststeckt.
    [HttpPost("reopen")]
    public async Task<IActionResult> ReopenPayroll([FromBody] ReopenPayrollDto dto)
    {
        // 1) Snapshot finden — primär über Periode-Kontext (Year+Month+Company),
        //    Fallback über die vom Frontend mitgegebene PayrollPeriodeId.
        var snapshot = await _db.PayrollSnapshots
            .Include(s => s.Periode)
            .Where(s => s.EmployeeId == dto.EmployeeId
                     && s.CompanyProfileId == dto.CompanyProfileId
                     && s.Periode != null
                     && s.Periode.Year  == dto.Year
                     && s.Periode.Month == dto.Month
                     && !s.Periode.IsTransition)
            .OrderByDescending(s => s.UpdatedAt)
            .FirstOrDefaultAsync();

        if (snapshot is null)
        {
            snapshot = await _db.PayrollSnapshots
                .Include(s => s.Periode)
                .FirstOrDefaultAsync(s => s.EmployeeId == dto.EmployeeId
                                       && s.PayrollPeriodeId == dto.PayrollPeriodeId);
        }

        // 2) Saldo laden (für Status-Check und Recovery-Pfad)
        var saldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId       == dto.EmployeeId
                                   && s.PeriodYear       == dto.Year
                                   && s.PeriodMonth      == dto.Month
                                   && s.CompanyProfileId == dto.CompanyProfileId);

        // 3) Schutz: abgeschlossene Periode kann nicht wieder eröffnet werden
        if (snapshot?.IsFinal == true)
            return Conflict(new { error = "Lohnperiode ist abgeschlossen. Wieder-Eröffnen nicht mehr möglich." });

        // 4) Recovery-Fall: kein Snapshot, aber Saldo existiert → zurücksetzen
        // Wir akzeptieren JEDEN Saldo (nicht nur "confirmed"), weil der
        // Reopen-Button im Frontend nur erscheint, wenn die GET-Saldo-Route
        // den Saldo als bestätigt liefert. Falls es trotzdem zu einem
        // Status-Mismatch kommt (Whitespace, Casing, alte Daten), soll der
        // Operator das Resetten können.
        if (snapshot is null)
        {
            if (saldo is null)
                return NotFound(new {
                    error = $"Kein Saldo gefunden für Mitarbeiter {dto.EmployeeId}, " +
                            $"Periode {dto.Year}/{dto.Month:D2}, Filiale {dto.CompanyProfileId}."
                });

            string altStatus = saldo.Status ?? "(null)";
            saldo.Status    = "draft";
            saldo.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Ok(new {
                message  = $"Saldo zurückgesetzt (vorheriger Status: '{altStatus}'). Kein Snapshot gefunden — vermutlich Altbestand. Bitte Lohn neu prüfen und bestätigen.",
                recovery = true
            });
        }

        // 5) Lohnabtretungs-Entries rückbuchen und löschen
        var existingEntries = await _db.PayrollLohnAbtretungEntries
            .Where(e => e.PayrollSnapshotId == snapshot.Id)
            .ToListAsync();
        foreach (var old in existingEntries)
        {
            var la = await _db.EmployeeLohnAssignments.FindAsync(old.EmployeeLohnAssignmentId);
            if (la != null)
            {
                la.BereitsAbgezogen = Math.Max(0, Math.Round(la.BereitsAbgezogen - old.Betrag, 2));
                la.UpdatedAt        = DateTime.UtcNow;
            }
            _db.PayrollLohnAbtretungEntries.Remove(old);
        }

        // 6) Saldo zurück auf draft setzen
        if (saldo != null)
        {
            saldo.Status    = "draft";
            saldo.UpdatedAt = DateTime.UtcNow;
        }

        // 7) Snapshot NICHT löschen — bleibt als Referenz erhalten,
        //    wird beim nächsten Confirm mit neuen Werten überschrieben.

        await _db.SaveChangesAsync();
        return Ok(new { message = "Lohnzettel wieder eröffnet. Absenzen und Zulagen können erneut bearbeitet werden." });
    }

    // GET /api/payroll/ktg-tagessatz?employeeId=X&companyProfileId=Y
    // Liefert den KTG/UVG-Tagessatz nach Spezialistenvorgabe (Regel A/B).
    [HttpGet("ktg-tagessatz")]
    public async Task<IActionResult> GetKtgTagessatz(
        [FromQuery] int employeeId,
        [FromQuery] int companyProfileId)
    {
        var result = await _ktgService.CalculateAsync(employeeId, companyProfileId);
        if (result is null)
            return NotFound(new { error = "Kein aktives Anstellungsverh\u00e4ltnis." });
        return Ok(result);
    }

    // ───────────────────────────────────────────────────────────────────
    // Lohnabtretungs-Historie — pro Lohnlauf × Abtretung ein Eintrag.
    // Filter: employeeId, behoerdeId, year, monthFrom/monthTo, onlyNotExportedFibu/Dta
    // Nutzung: Reporting pro MA, DTA-Zahlungsexport aggregiert pro Behörde,
    //          Abacus-FIBU-Buchungslauf.
    // ───────────────────────────────────────────────────────────────────
    [HttpGet("lohn-abtretungen/history")]
    public async Task<IActionResult> GetLohnAbtretungHistory(
        [FromQuery] int? employeeId,
        [FromQuery] int? behoerdeId,
        [FromQuery] int? year,
        [FromQuery] int? monthFrom,
        [FromQuery] int? monthTo,
        [FromQuery] bool onlyNotExportedFibu = false,
        [FromQuery] bool onlyNotExportedDta  = false)
    {
        var q = _db.PayrollLohnAbtretungEntries.AsQueryable();

        if (employeeId.HasValue) q = q.Where(e => e.EmployeeId == employeeId.Value);
        if (behoerdeId.HasValue) q = q.Where(e => e.BehoerdeId == behoerdeId.Value);
        if (year.HasValue)       q = q.Where(e => e.PeriodYear == year.Value);
        if (monthFrom.HasValue)  q = q.Where(e => e.PeriodMonth >= monthFrom.Value);
        if (monthTo.HasValue)    q = q.Where(e => e.PeriodMonth <= monthTo.Value);
        if (onlyNotExportedFibu) q = q.Where(e => e.FibuExportiertAm == null);
        if (onlyNotExportedDta)  q = q.Where(e => e.DtaExportiertAm  == null);

        var list = await q
            .OrderBy(e => e.PeriodYear).ThenBy(e => e.PeriodMonth)
            .ThenBy(e => e.BehoerdeName).ThenBy(e => e.EmployeeId)
            .Select(e => new {
                id                      = e.Id,
                payrollSnapshotId       = e.PayrollSnapshotId,
                assignmentId            = e.EmployeeLohnAssignmentId,
                employeeId              = e.EmployeeId,
                behoerdeId              = e.BehoerdeId,
                periodYear              = e.PeriodYear,
                periodMonth             = e.PeriodMonth,
                bezeichnung             = e.Bezeichnung,
                behoerdeName            = e.BehoerdeName,
                iban                    = e.Iban,
                qrIban                  = e.QrIban,
                referenzAmt             = e.ReferenzAmt,
                zahlungsReferenz        = e.ZahlungsReferenz,
                betrag                  = e.Betrag,
                bereitsAbgezogenVorher  = e.BereitsAbgezogenVorher,
                bereitsAbgezogenNachher = e.BereitsAbgezogenNachher,
                fibuBelegnr             = e.FibuBelegnr,
                fibuExportiertAm        = e.FibuExportiertAm,
                dtaExportiertAm         = e.DtaExportiertAm,
                dtaExportRef            = e.DtaExportRef,
                createdAt               = e.CreatedAt
            })
            .ToListAsync();

        var total = list.Sum(x => x.betrag);
        return Ok(new { total, count = list.Count, entries = list });
    }

    // GET /api/payroll/lohn-abtretungen/summary?year=2025&behoerdeId=X
    // Aggregiert pro Behörde × Monat — ideal für FIBU-/DTA-Übersicht.
    [HttpGet("lohn-abtretungen/summary")]
    public async Task<IActionResult> GetLohnAbtretungSummary(
        [FromQuery] int? year,
        [FromQuery] int? behoerdeId)
    {
        var q = _db.PayrollLohnAbtretungEntries.AsQueryable();
        if (year.HasValue)       q = q.Where(e => e.PeriodYear == year.Value);
        if (behoerdeId.HasValue) q = q.Where(e => e.BehoerdeId == behoerdeId.Value);

        var grouped = await q
            .GroupBy(e => new { e.BehoerdeId, e.BehoerdeName, e.PeriodYear, e.PeriodMonth })
            .Select(g => new {
                behoerdeId   = g.Key.BehoerdeId,
                behoerdeName = g.Key.BehoerdeName,
                periodYear   = g.Key.PeriodYear,
                periodMonth  = g.Key.PeriodMonth,
                anzahl       = g.Count(),
                total        = g.Sum(e => e.Betrag)
            })
            .OrderBy(x => x.periodYear).ThenBy(x => x.periodMonth).ThenBy(x => x.behoerdeName)
            .ToListAsync();

        return Ok(grouped);
    }

    // GET /api/payroll/snapshot?periodeId=X&employeeId=Y
    [HttpGet("snapshot")]
    public async Task<IActionResult> GetSnapshot([FromQuery] int periodeId, [FromQuery] int employeeId)
    {
        var snap = await _db.PayrollSnapshots
            .FirstOrDefaultAsync(s => s.PayrollPeriodeId == periodeId && s.EmployeeId == employeeId);
        if (snap is null) return NotFound();
        return Ok(new { snap.Id, snap.IsFinal, snap.SlipJson, snap.Brutto, snap.Netto,
                        snap.CreatedAt, snap.UpdatedAt });
    }

    // GET /api/payroll/snapshot/{id}/print – Snapshot für Nachdruck
    [HttpGet("snapshot/{id}/print")]
    public async Task<IActionResult> PrintSnapshot(int id)
    {
        var snap = await _db.PayrollSnapshots
            .Include(s => s.Periode)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (snap is null) return NotFound();
        // SlipJson direkt zurückgeben (wird im Frontend gleich gerendert wie live-Berechnung)
        return Content(snap.SlipJson, "application/json");
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
            // Proportionaler Anteil des akkumulierten Guthabens (2 Dezimalen;
            // finale 0.05-Rundung passiert erst auf Brutto/Netto/Auszahlung).
            ausz = Math.Round(prevGeld * (tageGenommen / prevTage), 2);
            ausz = Math.Min(ausz, prevGeld); // nie mehr als Guthaben
            if (ausz > 0)
            {
                lohnLines.Add(new
                {
                    bezeichnung = $"Ferienentschädigung-Auszahlung ({tageGenommen:F1} Tage)",
                    anzahl      = (decimal?)tageGenommen,
                    prozent     = (decimal?)null,
                    basis       = (decimal?)null,
                    betrag      = ausz,
                    accrued     = (decimal?)0m    // reine Saldo-Auszahlung, keine neue Akkumulation
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

        string qstCode     = einstellung.QstCode ?? $"{einstellung.TarifCode}{einstellung.AnzahlKinder}{(einstellung.Kirchensteuer ? 'Y' : 'N')}";

        return new DeductionRule
        {
            Id               = -99,
            CompanyProfileId = companyProfileId,
            CategoryCode     = "QST",
            CategoryName     = "Quellensteuer",
            // Satz nicht mehr im Namen — kommt über DisplayRatePercent in die
            // Prozent-Spalte des Lohnzettels (konsistent mit AHV/ALV/NBU/...).
            Name             = $"Quellensteuer {qstCode} {einstellung.Steuerkanton}",
            Type             = "fixed",
            Rate             = qstBetrag,   // BuildResult negiert diesen Wert
            BasisType        = "gross",
            IsActive         = true,
            ValidFrom        = periodFrom,
            SortOrder        = 90,
            DisplayRatePercent = satzPct,   // transient, nur für die Anzeige
        };
    }

    private static object BuildResult(
        Employee employee, Employment emp, CompanyProfile company,
        int year, int month, DateOnly periodFrom, DateOnly periodTo,
        List<object> lohnLines, List<object> abzugLines,
        List<DeductionRule> deductions, decimal totalLohn, SvBases svBases,
        List<object> zulagenExtraLines, decimal zulagenExtraTotal,
        List<object> abzuegeExtraLines, decimal abzuegeExtraTotal,
        SaldoBlock saldo,
        List<EmployeeLohnAssignment> lohnAssignments,
        bool usingDefaultDeductions = false,
        string? periodeFooterText = null)
    {
        // Abzüge berechnen
        decimal totalAbzuege = 0;
        decimal qstBetragOut = 0m;   // für Snapshot-Denormalisierung
        var abzugResult = new List<object>();
        foreach (var d in deductions)
        {
            // Berechnungsbasis je BasisType und SV-Kategorie:
            //   "bvg_basis"      → BVG-pflichtige Basis minus Koordinationsabzug
            //   "coord_deduction"→ Koordinationsabzug fix (CHF 2'205/Mt., BVG Zusatz Kader)
            //   "gross" / sonst  → per SV-Typ aus svBases (AHV, NBUV, KTG, BVG, QST)
            decimal basis = d.BasisType switch
            {
                "bvg_basis"       => Math.Max(0, svBases.Bvg - (d.CoordinationDeduction ?? 0)),
                "coord_deduction" => d.CoordinationDeduction ?? 0,
                "bvg"             => Math.Max(0, svBases.Bvg - (d.CoordinationDeduction ?? 0)), // legacy
                _ => d.CategoryCode switch   // "gross"
                {
                    "AHV" or "ALV" => svBases.Ahv,
                    "NBUV"         => svBases.Nbuv,
                    "KTG"          => svBases.Ktg,
                    "BVG"          => svBases.Bvg,
                    _              => svBases.Ahv  // QST fixed → Betrag kommt aus ComputeQst; andere: AHV-Basis
                }
            };

            // Freibetrag abziehen (z.B. AHV 65+: CHF 1'400/Mt.)
            // Basis = max(0, Lohn − Freibetrag)
            if (d.FreibetragMonthly is > 0)
                basis = Math.Max(0, basis - d.FreibetragMonthly.Value);

            // Abzug-Betrag: auf 2 Dezimalen (0.05-Rundung erst auf Schlussresultat)
            decimal betrag = d.Type == "fixed"
                ? -Math.Round(d.Rate, 2)
                : -Math.Round(basis * d.Rate / 100m, 2);

            totalAbzuege += betrag;
            if (d.CategoryCode == "QST") qstBetragOut += Math.Abs(betrag);
            abzugResult.Add(new
            {
                bezeichnung = d.FreibetragMonthly is > 0
                    ? $"{d.Name} (−CHF {d.FreibetragMonthly:F2} Freibetrag)"
                    : d.Name,
                // Prozent: zuerst DisplayRatePercent (z.B. QST mit Tarif-Satz),
                // sonst die echte Rate bei Type=percent, sonst null.
                prozent     = d.DisplayRatePercent
                              ?? (d.Type == "percent" ? (decimal?)d.Rate : null),
                basis       = (decimal?)Math.Round(basis, 2),
                betrag
            });
        }

        // Schlussresultat: nur Nettolohn und Auszahlungsbetrag werden auf 0.05
        // gerundet. Total Lohn und Total Abzüge bleiben auf 2 Dezimalen.
        decimal nettolohn = Round05(totalLohn + totalAbzuege);

        // ── Lohnabtretungen (Pfändung / Sozialamt) nach Netto verrechnen ──
        // Pro Zuweisung: Abzug = max(0, verbleibender Netto − Freigrenze)
        //                gedeckelt auf (Zielbetrag − BereitsAbgezogen) falls Zielbetrag > 0.
        // Werden als Zeilen in abzuegeExtraLines angefügt und reduzieren
        // damit automatisch den Auszahlungsbetrag.
        var lohnAbtretungResults = new List<object>();
        decimal verbleibenderNetto = nettolohn;
        foreach (var la in lohnAssignments)
        {
            decimal ueber = Math.Max(0, verbleibenderNetto - la.Freigrenze);
            if (la.Zielbetrag > 0)
            {
                decimal restSchuld = Math.Max(0, la.Zielbetrag - la.BereitsAbgezogen);
                ueber = Math.Min(ueber, restSchuld);
            }
            ueber = Math.Round(ueber, 2);
            if (ueber <= 0) continue;

            string amtName = la.Behoerde?.Name ?? "Behörde";
            abzuegeExtraLines.Add(new {
                bezeichnung = $"{la.Bezeichnung} an {amtName}",
                betrag      = -ueber
            });
            abzuegeExtraTotal += ueber;
            verbleibenderNetto -= ueber;

            lohnAbtretungResults.Add(new {
                assignmentId = la.Id,
                behoerdeId   = la.BehoerdeId,
                behoerdeName = amtName,
                bezeichnung  = la.Bezeichnung,
                betrag       = ueber
            });
        }

        decimal auszahlungsbetrag = Round05(nettolohn + zulagenExtraTotal - abzuegeExtraTotal);

        // 13. ML: Rückstellung intern auf 2 Dezimalen; Summe ist Saldo-Wert
        decimal thirteenthMonthly     = saldo.ThirteenthPct > 0 ? Math.Round(totalLohn * saldo.ThirteenthPct / 100m, 2) : 0;
        decimal thirteenthAccumulated = Math.Round(saldo.PrevThirteenth + thirteenthMonthly, 2);

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
            companyParentName = company.CompanyName,                       // z.B. "Schaub Restaurants GmbH"
            companyName       = company.BranchName ?? company.CompanyName,  // z.B. "Filiale Oftringen"
            companyAddress    = $"{company.Street} {company.HouseNumber}".Trim(),
            companyZipCity    = $"{company.ZipCode} {company.City}".Trim(),
            companyCity       = company.City ?? "",
            // Periodenspezifischer Footer überschreibt Filial-Default
            pdfFooterText     = !string.IsNullOrWhiteSpace(periodeFooterText)
                                    ? periodeFooterText
                                    : company.PdfFooterText,
            periodLabel     = $"{monthNames[month]} {year}",
            periodFrom      = periodFrom.ToString("dd.MM.yyyy"),
            periodTo        = periodTo.ToString("dd.MM.yyyy"),
            printDate       = DateTime.Now.ToString("dd.MM.yyyy"),

            // Lohn
            lohnLines,
            totalLohn       = Math.Round(totalLohn, 2),   // 2 Dezimalen, nicht 0.05

            // Abzüge (SV-Abzüge: AHV, ALV, QST etc.)
            abzugLines      = abzugResult,
            totalAbzuege    = Math.Round(totalAbzuege, 2),   // 2 Dezimalen, nicht 0.05

            // Netto
            nettolohn,

            // Nicht-SV-pflichtige Zulagen & Abzüge (nach Netto)
            zulagenExtraLines,
            zulagenExtraTotal   = Math.Round(zulagenExtraTotal, 2),
            abzuegeExtraLines,
            abzuegeExtraTotal   = Math.Round(abzuegeExtraTotal, 2),
            lohnAbtretungen     = lohnAbtretungResults,  // für Confirm: bereits_abgezogen aktualisieren
            auszahlungsbetrag,

            // Stunden-Info
            workedHours        = Math.Round(saldo.WorkedHours, 2),
            sollStunden        = Math.Round(saldo.SollStunden, 2),
            mehrstunden        = Math.Round(saldo.Mehrstunden, 2),
            absenzGutschrift   = Math.Round(saldo.AbsenzGutschrift, 2),
            vormonatHourSaldo  = saldo.VormonatHourSaldo,
            neuerHourSaldo     = saldo.NeuerHourSaldo,
            // Optional: Soll-Berechnungs-Erläuterung (MTP)
            sollStundenVoll        = saldo.SollStundenVoll,
            sollFerienReduktion    = saldo.SollFerienReduktion,
            guaranteedHoursPerWeek = saldo.GuaranteedHoursPerWeek,
            ferienTageInPeriode    = saldo.FerienTageInPeriode,

            // Nacht-Zeitzuschlag
            nightHours         = Math.Round(saldo.NightHours, 2),
            nightBonus         = Math.Round(saldo.NightBonus, 2),        // +10% Zeitgutschrift
            nachtKompStunden   = Math.Round(saldo.NachtKompStunden, 2),  // eingelöste Ruhetage
            vormonatNachtSaldo = saldo.VormonatNachtSaldo,
            neuerNachtSaldo    = saldo.NeuerNachtSaldo,

            // 13. ML
            thirteenthMonthly,
            thirteenthAccumulated,
            prevThirteenth     = saldo.PrevThirteenth,

            // Modell
            employmentModel = emp.EmploymentModel,

            // Ferien-Saldo
            vacationWeeks      = saldo.VacationWeeks,
            ferienTageAccrual  = Math.Round(saldo.FerienTageAccrual, 4),
            ferienTageGenommen = Math.Round(saldo.FerienTageGenommen, 4),
            vormonatFerienTage = saldo.VormonatFerienTage,
            ferienTageSaldoNeu = saldo.FerienTageSaldoNeu,
            // Ferien-Geld (nur UTP/MTP, bei FIX immer 0)
            vormonatFerienGeld   = saldo.VormonatFerienGeld,
            ferienGeldAuszahlung = saldo.FerienGeldAuszahlung,
            ferienGeldSaldoNeu   = saldo.FerienGeldSaldoNeu,
            // Zuwachs = Saldo neu + Auszahlung - Vormonat  (rückrechenbar)
            ferienGeldAccrual = Math.Round(saldo.FerienGeldSaldoNeu + saldo.FerienGeldAuszahlung - saldo.VormonatFerienGeld, 2),

            // Ferien-Kürzungs-Vorschlag (Art. 329b OR)
            ferienKuerzung = saldo.FerienKuerzungVorschlag != null && saldo.FerienKuerzungVorschlag.HasKuerzungVorschlag
                ? new {
                    dienstjahrVon = saldo.FerienKuerzungVorschlag.DienstjahrVon,
                    dienstjahrBis = saldo.FerienKuerzungVorschlag.DienstjahrBis,
                    tageKrankUnfall  = saldo.FerienKuerzungVorschlag.TageKrankUnfall,
                    tageUnbezUrlaub  = saldo.FerienKuerzungVorschlag.TageUnbezUrlaub,
                    tageMutterschaft = saldo.FerienKuerzungVorschlag.TageMutterschaft,
                    kuerzungUnverschuldet12tel = saldo.FerienKuerzungVorschlag.KuerzungUnverschuldet12tel,
                    kuerzungSelbst12tel        = saldo.FerienKuerzungVorschlag.KuerzungSelbst12tel,
                    kuerzungSchwanger12tel     = saldo.FerienKuerzungVorschlag.KuerzungSchwanger12tel,
                    totalKuerzung12tel         = saldo.FerienKuerzungVorschlag.TotalKuerzung12tel,
                    vorschlagTage              = saldo.FerienKuerzungVorschlagTage ?? 0
                  }
                : null,

            // Feiertag-Saldo (nur FIX/FIX-M, sonst alle 0)
            vormonatFeiertagTage = saldo.VormonatFeiertagTage,
            feiertagTageAccrual  = Math.Round(saldo.FeiertagTageAccrual,  4),
            feiertagTageGenommen = Math.Round(saldo.FeiertagTageGenommen, 4),
            feiertagTageSaldoNeu = saldo.FeiertagTageSaldoNeu,

            // Hinweis: Schweizer Standardsätze verwendet (keine firmenspezifischen Regeln konfiguriert)
            usingDefaultDeductions,

            // SV-Basen + QST für Snapshot-Denormalisierung
            svBasisAhv  = Math.Round(svBases.Ahv,  2),
            svBasisBvg  = Math.Round(svBases.Bvg,  2),
            qstBetrag   = Math.Round(qstBetragOut, 2),
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

    /// <summary>
    /// Bestimmt ob in diesem Monat der angesammelte 13.-ML-Saldo ausbezahlt wird.
    /// Konfigurierbar pro Firmenprofil (ThirteenthMonthPayoutsPerYear):
    ///   12 = monatlich (jeden Monat Auszahlung)
    ///    4 = quartalsweise (Mt 3, 6, 9, 12)
    ///    2 = halbjährlich  (Mt 6, 12)
    ///    1 = jährlich      (nur Mt 12)
    /// </summary>
    private static bool IsThirteenthPayoutMonth(int payoutsPerYear, int month) =>
        payoutsPerYear switch
        {
            4  => month == 3 || month == 6 || month == 9 || month == 12,
            2  => month == 6 || month == 12,
            1  => month == 12,
            _  => true   // 12 oder unbekannt → immer monatlich
        };

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

    /// <summary>
    /// Skaliert HoursCredited einer Absenz proportional auf die Anzahl
    /// markierter Tage innerhalb der gegebenen Periode.
    ///
    /// Beispiel: Absenz 25.02.–25.07. mit 100 markierten Tagen und 840 h
    /// Gesamtgutschrift; Lohnperiode 21.03.–20.04. enthält 22 dieser Tage
    /// → skaliert 22/100 × 840 = 184.80 h.
    ///
    /// Fallback wenn WorkedDays leer/null: alle Kalendertage zwischen
    /// DateFrom..DateTo zählen, proportional genauso.
    /// </summary>
    private static decimal ScaleAbsenceHoursToPeriod(Absence a, DateOnly periodFrom, DateOnly periodTo)
    {
        if (a.HoursCredited <= 0) return 0;

        // Markierte Tage parsen
        DateOnly[] allDays;
        if (!string.IsNullOrWhiteSpace(a.WorkedDays))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<string[]>(a.WorkedDays);
                allDays = arr?
                    .Select(s => DateOnly.TryParse(s, out var d) ? d : (DateOnly?)null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .ToArray() ?? Array.Empty<DateOnly>();
            }
            catch { allDays = Array.Empty<DateOnly>(); }
        }
        else
        {
            allDays = Array.Empty<DateOnly>();
        }

        // Fallback: alle Kalendertage zwischen DateFrom und DateTo
        if (allDays.Length == 0)
        {
            allDays = Enumerable.Range(0, a.DateTo.DayNumber - a.DateFrom.DayNumber + 1)
                .Select(i => a.DateFrom.AddDays(i))
                .ToArray();
        }

        if (allDays.Length == 0) return 0;

        int daysInPeriod = allDays.Count(d => d >= periodFrom && d <= periodTo);
        if (daysInPeriod == 0) return 0;
        if (daysInPeriod == allDays.Length) return a.HoursCredited;   // komplett in Periode

        decimal proTag = a.HoursCredited / allDays.Length;
        return Math.Round(proTag * daysInPeriod, 2);
    }

    /// <summary>
    /// Zählt, wie viele Tage einer Absenz in [periodFrom..periodTo] fallen.
    ///
    /// Reihenfolge:
    ///   1) WorkedDays (JSON-Array ISO-Datums) — Tage in Periode zählen.
    ///   2) Wenn WorkedDays leer/"[]"/unparsbar → Fallback auf Kalendertage
    ///      zwischen DateFrom..DateTo (Schnittmenge mit Periode).
    ///
    /// Wichtig: Der "[]"-Fall (leeres JSON-Array) MUSS auf den Fallback gehen,
    /// sonst landen Einträge, bei denen der User keine Checkboxen setzte
    /// (z.B. Feiertag-Eintrag im alten Frontend), auf 0 Tage — obwohl sie
    /// einen echten Zeitraum abdecken.
    /// </summary>
    private static int CountAbsenceDaysInPeriod(Absence a, DateOnly periodFrom, DateOnly periodTo)
    {
        DateOnly[] allDays = Array.Empty<DateOnly>();
        if (!string.IsNullOrWhiteSpace(a.WorkedDays))
        {
            try
            {
                var arr = JsonSerializer.Deserialize<string[]>(a.WorkedDays);
                allDays = arr?
                    .Select(s => DateOnly.TryParse(s, out var d) ? d : (DateOnly?)null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .ToArray() ?? Array.Empty<DateOnly>();
            }
            catch { allDays = Array.Empty<DateOnly>(); }
        }

        // Fallback: Kalendertage zwischen DateFrom..DateTo
        if (allDays.Length == 0 && a.DateTo >= a.DateFrom)
        {
            allDays = Enumerable.Range(0, a.DateTo.DayNumber - a.DateFrom.DayNumber + 1)
                .Select(i => a.DateFrom.AddDays(i))
                .ToArray();
        }

        return allDays.Count(d => d >= periodFrom && d <= periodTo);
    }

    /// <summary>
    /// Granulare SV-Berechnungsbasen pro Versicherungstyp.
    /// Jede Basis = Summe aller Lohnpositionen, die für diese SV pflichtig sind.
    /// </summary>
    private record SvBases(
        decimal Ahv,   // AHV / IV / EO + ALV
        decimal Nbuv,  // Nichtberufsunfallversicherung
        decimal Ktg,   // Krankentaggeldversicherung
        decimal Bvg,   // Pensionskasse (vor Koordinationsabzug)
        decimal Qst    // Quellensteuer-Basis
    );

    /// <summary>
    /// Alle Saldo-relevanten Werte für BuildResult gebündelt:
    /// Stunden (Arbeit + Nacht), Ferien-Tage, Ferien-Geld, Feiertag-Tage
    /// und 13. ML. Jeder Block folgt dem Muster
    /// Vormonat → Accrual → Genommen → Neu (so weit zutreffend).
    ///
    /// Die einzelnen Felder werden im Return-Objekt von BuildResult 1:1
    /// als JSON-Felder ausgegeben — die Property-Namen hier müssen also
    /// stabil bleiben, wenn das Frontend nicht brechen soll.
    /// </summary>
    private record SaldoBlock(
        // ── Stunden (Arbeitszeit-Saldo) ──────────────────────────────
        decimal VormonatHourSaldo,
        decimal NeuerHourSaldo,
        decimal WorkedHours,
        decimal SollStunden,
        decimal Mehrstunden,
        decimal AbsenzGutschrift,

        // ── Nacht-Zeitzuschlag ───────────────────────────────────────
        decimal NightHours,
        decimal NightBonus,
        decimal NachtKompStunden,
        decimal VormonatNachtSaldo,
        decimal NeuerNachtSaldo,

        // ── Ferien-Tage-Saldo ────────────────────────────────────────
        int     VacationWeeks,
        decimal VormonatFerienTage,
        decimal FerienTageAccrual,
        decimal FerienTageGenommen,
        decimal FerienTageSaldoNeu,

        // ── Ferien-Geld-Saldo (nur UTP/MTP; FIX = 0) ────────────────
        decimal VormonatFerienGeld,
        decimal FerienGeldSaldoNeu,
        decimal FerienGeldAuszahlung,

        // ── Feiertag-Tage-Saldo (nur FIX/FIX-M; sonst = 0) ──────────
        decimal VormonatFeiertagTage,
        decimal FeiertagTageAccrual,
        decimal FeiertagTageGenommen,
        decimal FeiertagTageSaldoNeu,

        // ── 13. Monatslohn ───────────────────────────────────────────
        decimal ThirteenthPct,
        decimal PrevThirteenth,

        // ── Optional: Soll-Berechnungs-Details für Anzeige im Lohnzettel ──
        decimal? SollStundenVoll = null,             // vor Ferien-Reduktion
        decimal? SollFerienReduktion = null,         // GuarH/7 × Ferientage
        decimal? GuaranteedHoursPerWeek = null,      // 21 (für Erläuterung)
        decimal? FerienTageInPeriode = null,         // 4 (für Erläuterung)

        // ── Optional: Ferien-Kürzungs-Vorschlag (Art. 329b OR) ────────────
        FerienKuerzungResult? FerienKuerzungVorschlag = null,
        decimal? FerienKuerzungVorschlagTage = null
    );
}

public record SaveSaldoDto(
    int EmployeeId, int CompanyProfileId, int Year, int Month,
    decimal HourSaldo, decimal NachtSaldo, decimal NightHoursWorked,
    decimal FerienGeldSaldo, decimal FerienTageSaldo,
    decimal ThirteenthMonthMonthly, decimal ThirteenthMonthAccumulated,
    decimal GrossAmount, decimal NetAmount, string Status,
    decimal FeiertagTageSaldo = 0m);

public record ConfirmPayrollDto(
    int EmployeeId, int CompanyProfileId, int PayrollPeriodeId,
    int Year, int Month,
    decimal HourSaldo, decimal NachtSaldo, decimal NightHoursWorked,
    decimal FerienGeldSaldo, decimal FerienTageSaldo,
    decimal ThirteenthMonthMonthly, decimal ThirteenthMonthAccumulated,
    decimal GrossAmount, decimal NetAmount,
    decimal SvBasisAhv, decimal SvBasisBvg, decimal QstBetrag,
    string SlipJson,
    LohnAbtretungConfirmDto[]? LohnAbtretungen = null,
    decimal FeiertagTageSaldo = 0m);

public record LohnAbtretungConfirmDto(int AssignmentId, decimal Betrag);

public record ReopenPayrollDto(
    int EmployeeId, int CompanyProfileId, int PayrollPeriodeId,
    int Year, int Month);
