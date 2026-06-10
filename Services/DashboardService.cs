using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Dashboard-Cockpit: sammelt alle "Was wartet auf mich?"-Alarme.
///
/// Phase 1 (dieser Entwurf):
///   • Bewilligungen die in 30/60/90 Tagen ablaufen
///   • Probezeit endet in 14 Tagen
///   • Befristete Verträge enden in 30 Tagen
///   • Lohnperioden im Status 'provisorisch_abgeschlossen' (warten auf
///     Definitiv-Abschluss = Aktion HR)
///   • Lohnperioden im Status 'offen' wo alle Snapshots provisorisch sind
///   • Geburtstage in den nächsten 7 Tagen
///   • Eintritts-Jubiläen (5/10/15/20/25 Jahre) in 30 Tagen
///
/// Phase 2 (später): MA ohne Bankverbindung, ungelesene Posteingangs-
/// Dokumente, neue MA-Postfach-Uploads, Karteileichen.
///
/// Filial-Filter: alle Endpoints akzeptieren optional companyProfileId.
/// Wenn gesetzt → nur Alarme zur Filiale. Sonst alle. User mit beschränkten
/// Rechten sollten das im Frontend auf ihre Filialen einschränken.
/// </summary>
public class DashboardService
{
    private readonly AppDbContext _db;
    private readonly QstPflichtCheckService _qstCheck;
    public DashboardService(AppDbContext db, QstPflichtCheckService qstCheck)
    {
        _db = db;
        _qstCheck = qstCheck;
    }

    public class DashboardAlert
    {
        public string Category    { get; set; } = "";   // "permit_expiring", "probation_end", etc.
        public string Severity    { get; set; } = "info"; // critical|warning|info
        // Title/Subtitle bleiben als deutscher Fallback gefüllt — Frontend-i18n
        // bevorzugt aber TitleKey + TitleArgs (siehe wwwroot/js/i18n.js Dictionary
        // mit Keys wie "alert.permit.expired"). Bei i18n-Switch zwischen DE/EN
        // wird live übersetzt ohne Backend-Roundtrip.
        public string Title       { get; set; } = "";
        public string Subtitle    { get; set; } = "";
        public string? TitleKey   { get; set; }          // z.B. "alert.permit.expired"
        public Dictionary<string, object>? TitleArgs { get; set; }
        public string? SubtitleKey { get; set; }
        public Dictionary<string, object>? SubtitleArgs { get; set; }
        public DateTime? DueDate  { get; set; }          // wann läuft etwas ab / passiert was
        public int? DaysUntil     { get; set; }          // wieviele Tage bis DueDate
        public int? EmployeeId    { get; set; }          // optional: Klick zum MA
        public string? EmployeeNumber { get; set; }
        public string? EmployeeName   { get; set; }
        public int? PeriodeId         { get; set; }      // optional: Klick zum Lohnlauf
    }

    public class DashboardData
    {
        public List<DashboardAlert> Alerts { get; set; } = new();
        public Dictionary<string, int> CountsBySeverity { get; set; } = new();
        public Dictionary<string, int> CountsByCategory { get; set; } = new();
    }

    public async Task<DashboardData> BuildAsync(int? companyProfileId)
    {
        var alerts = new List<DashboardAlert>();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var now = DateTime.Today;

        // ── 1) Bewilligungen die ablaufen ──────────────────────────────────
        // Walter-Vorgabe 01.06.2026: Quelle ist jetzt ausschliesslich
        // EmployeePermitHistory.ValidTo des jüngsten Eintrags pro MA.
        // (Vorher: denormalisierte employee.permit_expiry_date — entfernt.)
        // Severity skaliert mit Dringlichkeit. CH-/Einbürgerungs-Einträge
        // (PermitTypeId IS NULL → ValidTo darf NULL sein) sind unbefristet
        // und werden ignoriert.
        var dueDateLimit = DateOnly.FromDateTime(now.AddDays(90));
        var empBase = _db.Employees
            .Where(e => e.IsActive
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            var cpid = companyProfileId.Value;
            empBase = empBase.Where(e => e.Employments.Any(em => em.CompanyProfileId == cpid));
        }
        // Pro MA den jüngsten History-Eintrag mit PermitTypeId != NULL holen
        // (= aktive Bewilligung). NUR der jüngste — dessen ValidTo ist das
        // relevante Ablauf-Datum.
        var maList = await empBase
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber, e.ExitDate })
            .ToListAsync();
        var maIds = maList.Select(e => e.Id).ToList();
        var histories = await _db.EmployeePermitHistories
            .Include(h => h.PermitType)
            .Where(h => maIds.Contains(h.EmployeeId) && h.PermitTypeId != null)
            .ToListAsync();
        var maById = maList.ToDictionary(m => m.Id);
        // Walter-Vorgabe 07.06.2026: keine Warnung wenn die Bewilligung
        // mindestens bis zum Austrittsdatum gültig ist — der MA verlässt
        // die Firma vorher, eine Erneuerung wäre unnötig.
        var youngestPerMa = histories
            .GroupBy(h => h.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ValidFrom).ThenByDescending(x => x.Id).First())
            .Where(h => h.ValidTo.HasValue && h.ValidTo.Value <= dueDateLimit)
            .Where(h =>
            {
                if (!maById.TryGetValue(h.EmployeeId, out var e)) return false;
                if (e.ExitDate.HasValue)
                {
                    var exitDateOnly = DateOnly.FromDateTime(e.ExitDate.Value);
                    if (h.ValidTo!.Value >= exitDateOnly) return false;
                }
                return true;
            })
            .ToList();
        foreach (var h in youngestPerMa)
        {
            if (!maById.TryGetValue(h.EmployeeId, out var emp)) continue;
            var dueDate = h.ValidTo!.Value.ToDateTime(TimeOnly.MinValue);
            var days = (dueDate - now).Days;
            string severity = days < 0 ? "critical" : days <= 30 ? "critical" : days <= 60 ? "warning" : "info";
            var permitCode = h.PermitType?.Code ?? "?";
            alerts.Add(new DashboardAlert
            {
                Category = "permit_expiring",
                Severity = severity,
                Title    = days < 0
                    ? $"Bewilligung {permitCode} ist abgelaufen"
                    : $"Bewilligung {permitCode} läuft ab in {days} Tagen",
                TitleKey = days < 0 ? "alert.permit.expired" : "alert.permit.expires_in_days",
                TitleArgs = new Dictionary<string, object> { ["code"] = permitCode, ["days"] = days },
                Subtitle = $"{emp.FirstName} {emp.LastName} · Personalnr. {emp.EmployeeNumber}",
                SubtitleKey  = "subtitle.maPersonalnr",
                SubtitleArgs = new Dictionary<string, object> {
                    ["name"] = $"{emp.FirstName} {emp.LastName}".Trim(),
                    ["empNr"] = emp.EmployeeNumber
                },
                DueDate  = dueDate,
                DaysUntil = days,
                EmployeeId     = emp.Id,
                EmployeeNumber = emp.EmployeeNumber,
                EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
            });
        }

        // ── 2) Probezeit endet in 14 Tagen ─────────────────────────────────
        var probaQ = _db.Employments
            .Include(em => em.Employee)
            .Where(em => em.IsActive
                      && em.ProbationEndDate.HasValue
                      && em.ProbationEndDate >= now
                      && em.ProbationEndDate <= now.AddDays(14)
                      && em.Employee != null
                      && em.Employee.IsActive
                      && !em.Employee.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
            probaQ = probaQ.Where(em => em.CompanyProfileId == companyProfileId.Value);
        var probaList = await probaQ.ToListAsync();
        foreach (var em in probaList)
        {
            var dueDate = em.ProbationEndDate!.Value;
            var days = (dueDate.Date - now).Days;
            alerts.Add(new DashboardAlert
            {
                Category = "probation_end",
                Severity = days <= 7 ? "warning" : "info",
                Title    = $"Probezeit endet in {days} Tagen",
                TitleKey = "alert.probation.ends_in_days",
                TitleArgs = new Dictionary<string, object> { ["days"] = days },
                Subtitle = $"{em.Employee!.FirstName} {em.Employee.LastName} · Personalnr. {em.Employee.EmployeeNumber}",
                SubtitleKey  = "subtitle.maPersonalnr",
                SubtitleArgs = new Dictionary<string, object> {
                    ["name"] = $"{em.Employee.FirstName} {em.Employee.LastName}".Trim(),
                    ["empNr"] = em.Employee.EmployeeNumber
                },
                DueDate  = dueDate,
                DaysUntil = days,
                EmployeeId     = em.EmployeeId,
                EmployeeNumber = em.Employee.EmployeeNumber,
                EmployeeName   = $"{em.Employee.FirstName} {em.Employee.LastName}".Trim()
            });
        }

        // ── 3) Befristete Verträge enden in 30 Tagen ──────────────────────
        var fixedQ = _db.Employments
            .Include(em => em.Employee)
            .Where(em => em.IsActive
                      && em.ContractEndDate.HasValue
                      && em.ContractEndDate >= now
                      && em.ContractEndDate <= now.AddDays(30)
                      && em.Employee != null
                      && em.Employee.IsActive
                      && !em.Employee.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
            fixedQ = fixedQ.Where(em => em.CompanyProfileId == companyProfileId.Value);
        var fixedList = await fixedQ.ToListAsync();
        foreach (var em in fixedList)
        {
            var dueDate = em.ContractEndDate!.Value;
            var days = (dueDate.Date - now).Days;
            alerts.Add(new DashboardAlert
            {
                Category = "contract_end",
                Severity = days <= 14 ? "warning" : "info",
                Title    = $"Befristeter Vertrag endet in {days} Tagen",
                TitleKey = "alert.contract.ends_in_days",
                TitleArgs = new Dictionary<string, object> { ["days"] = days },
                Subtitle = $"{em.Employee!.FirstName} {em.Employee.LastName} · Personalnr. {em.Employee.EmployeeNumber} · {em.EmploymentModel}",
                SubtitleKey  = "subtitle.maPersonalnrModel",
                SubtitleArgs = new Dictionary<string, object> {
                    ["name"] = $"{em.Employee.FirstName} {em.Employee.LastName}".Trim(),
                    ["empNr"] = em.Employee.EmployeeNumber,
                    ["model"] = em.EmploymentModel
                },
                DueDate  = dueDate,
                DaysUntil = days,
                EmployeeId     = em.EmployeeId,
                EmployeeNumber = em.Employee.EmployeeNumber,
                EmployeeName   = $"{em.Employee.FirstName} {em.Employee.LastName}".Trim()
            });
        }

        // ── 3b) Austritt erfasst, aber MA noch aktiv (Walter 18.05.2026) ──
        // Walter pflegt das Aktiv-Flag bewusst manuell — der Auto-Sync aus
        // ExitDate wurde entfernt. Damit MA aus letzten Lohnzetteln nicht
        // versehentlich aktiv bleiben, zeigen wir hier eine Reminder-Liste.
        // Hinweis erscheint sobald ExitDate erreicht ist; Walter entscheidet
        // wann er den Haken in der MA-Maske wegnimmt (typisch: nach der
        // letzten Lohnabrechnung des Monats).
        var exitPendingQ = _db.Employees
            .Where(e => e.IsActive
                     && e.ExitDate.HasValue
                     && e.ExitDate.Value.Date <= now
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            // MA hat in einer der Filialen einen Vertrag → filtere auf passende.
            exitPendingQ = exitPendingQ.Where(e =>
                e.Employments.Any(em => em.CompanyProfileId == companyProfileId.Value));
        }
        var exitPendingList = await exitPendingQ.ToListAsync();
        foreach (var e in exitPendingList)
        {
            var exitDate = e.ExitDate!.Value;
            var daysAfter = (now - exitDate.Date).Days;
            alerts.Add(new DashboardAlert
            {
                Category = "exit_pending_active",
                // Nach 30 Tagen kritisch — bis dahin nur Hinweis.
                Severity = daysAfter > 30 ? "critical" : "warning",
                Title    = $"Austritt am {exitDate:dd.MM.yyyy} — MA noch aktiv",
                TitleKey = "alert.exit.pending_active",
                TitleArgs = new Dictionary<string, object> { ["date"] = exitDate.ToString("dd.MM.yyyy") },
                Subtitle = $"{e.FirstName} {e.LastName} · Personalnr. {e.EmployeeNumber} · {daysAfter} Tag(e) nach Austritt",
                SubtitleKey  = "subtitle.exitPendingActive",
                SubtitleArgs = new Dictionary<string, object> {
                    ["name"] = $"{e.FirstName} {e.LastName}".Trim(),
                    ["empNr"] = e.EmployeeNumber,
                    ["days"] = daysAfter
                },
                DueDate  = exitDate,
                DaysUntil = -daysAfter,
                EmployeeId     = e.Id,
                EmployeeNumber = e.EmployeeNumber,
                EmployeeName   = $"{e.FirstName} {e.LastName}".Trim()
            });
        }

        // ── 3c) QST-Pflicht offen (Walter-Vorgabe 26.05.2026) ─────────────
        // Aktive MA, die weder Schweizer noch C-Ausweis-Inhaber noch von der
        // Steuerbehörde befreit sind UND keinen Ehepartner mit CH/C haben UND
        // keine QST-Erfassung am Stichtag (heute) — die blocken den nächsten
        // Lohnlauf. Hier als Dashboard-Card sichtbar machen, damit Walter sie
        // proaktiv klären kann.
        var qstStichtag = DateOnly.FromDateTime(now);
        var qstCandidatesQ = _db.Employees
            .Where(e => e.IsActive && !e.IsPayrollExcluded
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
            qstCandidatesQ = qstCandidatesQ.Where(e =>
                e.Employments.Any(em => em.CompanyProfileId == companyProfileId.Value && em.IsActive));
        var qstCandidateIds = await qstCandidatesQ.Select(e => e.Id).ToListAsync();
        foreach (var empId in qstCandidateIds)
        {
            var r = await _qstCheck.CheckAsync(empId, qstStichtag);
            if (!r.IsPflichtOffen) continue;

            var emp = await _db.Employees.FirstOrDefaultAsync(x => x.Id == empId);
            if (emp == null) continue;
            alerts.Add(new DashboardAlert
            {
                Category = "qst_pflicht_offen",
                Severity = "critical",
                Title    = "QST-Pflicht offen — Lohnlauf gesperrt",
                TitleKey = "alert.qst.pflicht_offen",
                Subtitle = $"{emp.FirstName} {emp.LastName} · Personalnr. {emp.EmployeeNumber} · kein Befreiungs-Grund, keine QST erfasst",
                SubtitleKey = "subtitle.qstPflichtOffen",
                SubtitleArgs = new Dictionary<string, object> {
                    ["name"]  = $"{emp.FirstName} {emp.LastName}".Trim(),
                    ["empNr"] = emp.EmployeeNumber
                },
                EmployeeId     = emp.Id,
                EmployeeNumber = emp.EmployeeNumber,
                EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
            });
        }

        // ── 4) Lohnperioden warten auf Aktion ──────────────────────────────
        var lohnQ = _db.PayrollPerioden
            .Include(p => p.Company)
            .Where(p => p.Status == "provisorisch_abgeschlossen");
        if (companyProfileId.HasValue)
            lohnQ = lohnQ.Where(p => p.CompanyProfileId == companyProfileId.Value);
        var provPeriods = await lohnQ.ToListAsync();
        foreach (var p in provPeriods)
        {
            var monthNames = new[] {
                "Januar","Februar","März","April","Mai","Juni",
                "Juli","August","September","Oktober","November","Dezember" };
            var monatLabel = (p.Month >= 1 && p.Month <= 12)
                ? $"{monthNames[p.Month - 1]} {p.Year}"
                : $"{p.Year}-{p.Month:D2}";
            // Monatsname: für die Übersetzung übergeben wir den lokalisierten
            // Index, das Frontend übersetzt selbst falls EN gewählt ist.
            alerts.Add(new DashboardAlert
            {
                Category = "lohn_provisorisch",
                Severity = "warning",
                Title    = $"Lohn {monatLabel} wartet auf Definitiv-Abschluss",
                TitleKey = "alert.payroll.waits_for_final",
                TitleArgs = new Dictionary<string, object> {
                    ["month"] = p.Month,
                    ["year"]  = p.Year
                },
                Subtitle = $"Filiale: {p.Company?.RestaurantCode ?? "?"} — {p.Company?.BranchName ?? p.Company?.CompanyName ?? "?"}",
                SubtitleKey  = "subtitle.payrollBranch",
                SubtitleArgs = new Dictionary<string, object> {
                    ["code"] = p.Company?.RestaurantCode ?? "?",
                    ["name"] = p.Company?.BranchName ?? p.Company?.CompanyName ?? "?"
                },
                DueDate  = p.AbgeschlossenAm ?? p.ProvisorischAbgeschlossenAm,
                PeriodeId = p.Id
            });
        }

        // ── 5) Geburtstage in den nächsten 7 Tagen ─────────────────────────
        // Jahres-unabhängiger Vergleich: Tag und Monat zwischen heute und +7
        var birthQ = _db.Employees
            .Where(e => e.IsActive
                     && e.DateOfBirth.HasValue
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            var cpid = companyProfileId.Value;
            birthQ = birthQ.Where(e => e.Employments.Any(em => em.CompanyProfileId == cpid));
        }
        var birthEmps = await birthQ.ToListAsync();
        foreach (var e in birthEmps)
        {
            var bd = e.DateOfBirth!.Value;
            // Geburtstag in diesem Jahr (oder nächstes Jahr falls schon gewesen)
            var thisYearBday = new DateTime(now.Year, bd.Month, bd.Day == 29 && bd.Month == 2 && !DateTime.IsLeapYear(now.Year) ? 28 : bd.Day);
            if (thisYearBday < now) thisYearBday = thisYearBday.AddYears(1);
            var days = (thisYearBday - now).Days;
            if (days < 0 || days > 7) continue;

            var ageOnBday = thisYearBday.Year - bd.Year;
            alerts.Add(new DashboardAlert
            {
                Category = "birthday",
                Severity = "info",
                Title    = days == 0 ? $"🎂 Heute Geburtstag — {ageOnBday} Jahre"
                                     : $"Geburtstag in {days} Tagen — {ageOnBday} Jahre",
                TitleKey = days == 0 ? "alert.birthday.today" : "alert.birthday.in_days",
                TitleArgs = new Dictionary<string, object> { ["age"] = ageOnBday, ["days"] = days },
                Subtitle = $"{e.FirstName} {e.LastName} · Personalnr. {e.EmployeeNumber}",
                SubtitleKey  = "subtitle.maPersonalnr",
                SubtitleArgs = new Dictionary<string, object> {
                    ["name"] = $"{e.FirstName} {e.LastName}".Trim(),
                    ["empNr"] = e.EmployeeNumber
                },
                DueDate  = thisYearBday,
                DaysUntil = days,
                EmployeeId     = e.Id,
                EmployeeNumber = e.EmployeeNumber,
                EmployeeName   = $"{e.FirstName} {e.LastName}".Trim()
            });
        }

        // ── 6) Eintritts-Jubiläen in 30 Tagen ──────────────────────────────
        var milestoneYears = new[] { 5, 10, 15, 20, 25, 30, 35, 40 };
        var jubQ = _db.Employees
            .Where(e => e.IsActive
                     && e.EntryDate.HasValue
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            var cpid = companyProfileId.Value;
            jubQ = jubQ.Where(e => e.Employments.Any(em => em.CompanyProfileId == cpid));
        }
        var jubEmps = await jubQ.ToListAsync();
        foreach (var e in jubEmps)
        {
            var entry = e.EntryDate!.Value;
            // Nächstes Jubiläum-Datum berechnen
            var thisYearEntry = new DateTime(now.Year, entry.Month, entry.Day == 29 && entry.Month == 2 && !DateTime.IsLeapYear(now.Year) ? 28 : entry.Day);
            if (thisYearEntry < now) thisYearEntry = thisYearEntry.AddYears(1);
            var yearsAtEntry = thisYearEntry.Year - entry.Year;
            if (!milestoneYears.Contains(yearsAtEntry)) continue;
            var days = (thisYearEntry - now).Days;
            if (days < 0 || days > 30) continue;

            alerts.Add(new DashboardAlert
            {
                Category = "anniversary",
                Severity = "info",
                Title    = days == 0 ? $"🎉 {yearsAtEntry}-jähriges Dienstjubiläum heute"
                                     : $"{yearsAtEntry}-jähriges Dienstjubiläum in {days} Tagen",
                TitleKey = days == 0 ? "alert.anniversary.today" : "alert.anniversary.in_days",
                TitleArgs = new Dictionary<string, object> { ["years"] = yearsAtEntry, ["days"] = days },
                Subtitle = $"{e.FirstName} {e.LastName} · Personalnr. {e.EmployeeNumber} · seit {entry:dd.MM.yyyy}",
                SubtitleKey  = "subtitle.maEntry",
                SubtitleArgs = new Dictionary<string, object> {
                    ["name"]  = $"{e.FirstName} {e.LastName}".Trim(),
                    ["empNr"] = e.EmployeeNumber,
                    ["date"]  = entry.ToString("dd.MM.yyyy")
                },
                DueDate  = thisYearEntry,
                DaysUntil = days,
                EmployeeId     = e.Id,
                EmployeeNumber = e.EmployeeNumber,
                EmployeeName   = $"{e.FirstName} {e.LastName}".Trim()
            });
        }

        // ── 7) Mindestlohn-Compliance ──────────────────────────────────────
        // Pro aktivem Vertrag den L-GAV-Mindestlohn prüfen. JobGroupCode liegt
        // in employment.JobTitle (Konvention, vgl. ContractsController). Bei
        // Verstößen ein critical-Alert pro MA. Bei null Verstößen aber mind.
        // einem geprüften Vertrag ein einzelner Info-Alert „Alle Mindestlöhne
        // ok" — Walter-Anforderung: die positive Meldung soll auch sichtbar sein.
        // Walter-Vorgabe 28.05.2026: Stichtag fuer Lohn-/Compliance-Checks
        // ist IMMER die ÄLTESTE noch offene Lohnperiode (= die naechste die
        // verarbeitet wird) — NIE DateTime.Now. Grund: ein Mindestlohn-Check
        // soll fuer den Lohnlauf relevant sein, nicht fuer „heute". Bei
        // gefilterter Filiale die Periode dieser Filiale, sonst global.
        // Wenn keine offene Periode existiert (alles abgeschlossen), Fallback
        // auf heute.
        var openPeriodQ = _db.PayrollPerioden.Where(p => p.Status != "abgeschlossen");
        if (companyProfileId.HasValue)
            openPeriodQ = openPeriodQ.Where(p => p.CompanyProfileId == companyProfileId.Value);
        var earliestOpen = await openPeriodQ
            .OrderBy(p => p.PeriodFrom)
            .Select(p => (DateOnly?)p.PeriodFrom)
            .FirstOrDefaultAsync();
        var effectiveDt = earliestOpen.HasValue
            ? earliestOpen.Value.ToDateTime(TimeOnly.MinValue)
            : now;

        var mwQ = _db.Employments
            .Include(em => em.Employee)
            .Include(em => em.JobGroup)   // FK-Code statt JobTitle (Walter 26.05.2026)
            .Where(em => em.IsActive
                      && em.ContractStartDate.Date <= effectiveDt
                      && (em.ContractEndDate == null || em.ContractEndDate.Value.Date >= effectiveDt)
                      && em.Employee != null
                      && em.Employee.IsActive
                      && !em.Employee.IsPayrollExcluded
                      && !em.Employee.EmployeeNumber.ToLower().EndsWith("alt")
                      && em.JobGroupId != null);
        if (companyProfileId.HasValue)
            mwQ = mwQ.Where(em => em.CompanyProfileId == companyProfileId.Value);
        var mwContracts = await mwQ.ToListAsync();

        // Mindestlohn-Regeln + Education-Level-Lookup laden.
        // Walter-Architektur: EducationLevelCode liegt direkt am Vertrag
        // (Employment.EducationLevelCode). Bei einer Ausbildungs-Änderung
        // wird sowieso ein neuer Vertrag angelegt — die Vertrags-Versionierung
        // ersetzt die separate EmployeeEducationHistory.
        var rules = await _db.MinimumWageRulesNew
            .Where(r => r.IsActive)
            .ToListAsync();
        var eduLevelByCode = await _db.EducationLevels
            .Where(e => e.IsActive)
            .ToDictionaryAsync(e => e.Code, e => e.Id);
        // Default-Code „Ia" für Verträge ohne gesetzten EducationLevelCode.
        // Walter-Importer-Konvention: CCNT leer → 5 Sans qualification → Ia.
        var defaultEduCode = "Ia";

        int checkedCount = 0;
        int violationCount = 0;
        foreach (var em in mwContracts)
        {
            var emp = em.Employee!;
            // Walter-Vorgabe 28.05.2026: Stichtag = effectiveDt (aelteste
            // offene Lohnperiode), NICHT heute. Bei zukuenftigen Vertraegen
            // (Vertragsbeginn liegt nach dem Stichtag) gilt der Vertragsbeginn
            // selbst — so wird ein Lohn ab 1.1.2027 korrekt gegen den dann
            // gueltigen Mindestlohn geprueft.
            var checkDate = em.ContractStartDate.Date >= effectiveDt
                ? em.ContractStartDate.Date
                : effectiveDt;

            // Education Level kommt direkt vom Vertrag (Employment.EducationLevelCode).
            // Falls leer (Alt-Vertrag vor der Migration) → Default „Ia" annehmen.
            var eduCode = string.IsNullOrWhiteSpace(em.EducationLevelCode)
                ? defaultEduCode
                : em.EducationLevelCode;
            if (!eduLevelByCode.TryGetValue(eduCode!, out var eduLevelId)) continue;

            int? ageAtCheck = null;
            if (emp.DateOfBirth.HasValue)
            {
                var bd = emp.DateOfBirth.Value;
                int a = checkDate.Year - bd.Year;
                if (checkDate < new DateTime(checkDate.Year, bd.Month, bd.Day)) a--;
                ageAtCheck = a;
            }

            var modelCode = em.EmploymentModel.ToUpperInvariant() switch
            {
                "FIX-M" => "FIX-M",
                "FIX"   => "FIX",
                "MTP"   => "MTP",
                _       => "UTP"
            };
            var salaryType = (modelCode == "FIX" || modelCode == "FIX-M") ? "monthly" : "hourly";

            var rule = rules
                .Where(r => r.JobGroupCode == em.JobGroup!.Code
                         && r.EmploymentModelCode == modelCode
                         && r.EducationLevelId == eduLevelId
                         && r.SalaryType == salaryType
                         && r.ValidFrom <= checkDate
                         && (r.ValidTo == null || r.ValidTo >= checkDate)
                         && (r.AgeMax == null
                             || (ageAtCheck != null && ageAtCheck <= r.AgeMax)))
                .OrderBy(r => r.AgeMax ?? int.MaxValue)
                .ThenByDescending(r => r.ValidFrom)
                .FirstOrDefault();
            if (rule == null) continue;   // keine Regel → kein Check

            decimal? current = salaryType == "monthly" ? em.MonthlySalary : em.HourlyRate;
            if (current == null) continue;

            decimal minimum;
            if (salaryType == "monthly")
            {
                var pct = (em.EmploymentPercentage ?? 100m) / 100m;
                minimum = Math.Round(rule.Amount * pct, 2);
            }
            else
            {
                minimum = rule.Amount;
            }

            checkedCount++;
            var diff = current.Value - minimum;
            if (diff < 0)
            {
                violationCount++;
                var unit = salaryType == "monthly" ? "/Mt." : "/h";
                alerts.Add(new DashboardAlert
                {
                    Category = "minimum_wage_violation",
                    Severity = "critical",
                    Title    = $"Mindestlohn unterschritten · CHF {Math.Abs(diff):0.00} fehlen",
                    TitleKey = "alert.minWage.violation",
                    TitleArgs = new Dictionary<string, object> {
                        ["amount"] = Math.Abs(diff).ToString("0.00")
                    },
                    Subtitle = $"{emp.FirstName} {emp.LastName} · {em.EmploymentModel}/{em.JobTitle} · Aktuell {current:0.00}{unit}, Minimum {minimum:0.00}{unit}",
                    SubtitleKey  = "subtitle.minWageDetails",
                    SubtitleArgs = new Dictionary<string, object> {
                        ["name"]    = $"{emp.FirstName} {emp.LastName}".Trim(),
                        ["model"]   = em.EmploymentModel,
                        ["jobGrp"]  = em.JobTitle ?? "",
                        ["current"] = current.Value.ToString("0.00"),
                        ["minimum"] = minimum.ToString("0.00"),
                        ["unit"]    = unit
                    },
                    DueDate  = em.ContractStartDate,
                    EmployeeId     = em.EmployeeId,
                    EmployeeNumber = emp.EmployeeNumber,
                    EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                });
            }
        }
        // Positiv-Meldung wenn mind. 1 geprüft und 0 verletzt — bewusst nur
        // ein Title ohne Subtitle. Walter: bei Verstoss Details, sonst nur Info.
        if (checkedCount > 0 && violationCount == 0)
        {
            alerts.Add(new DashboardAlert
            {
                Category = "minimum_wage_ok",
                Severity = "info",
                Title    = "Alle Mindestlöhne ok",
                TitleKey = "alert.minWage.ok"
            });
        }

        // Sortieren:
        //   1. Mindestlohn-Verletzungen IMMER ganz oben (Walter-Priorität)
        //   2. Mindestlohn-OK direkt danach
        //   3. Restliche Alerts nach Severity (critical → warning → info)
        //   4. Innerhalb nach Datum
        int CategoryPriority(string cat) => cat switch
        {
            "minimum_wage_violation" => 0,
            "minimum_wage_ok"        => 1,
            _                        => 2
        };
        alerts = alerts
            .OrderBy(a => CategoryPriority(a.Category))
            .ThenBy(a => a.Severity == "critical" ? 0 : a.Severity == "warning" ? 1 : 2)
            .ThenBy(a => a.DueDate ?? DateTime.MaxValue)
            .ToList();

        var result = new DashboardData { Alerts = alerts };
        result.CountsBySeverity = alerts
            .GroupBy(a => a.Severity)
            .ToDictionary(g => g.Key, g => g.Count());
        result.CountsByCategory = alerts
            .GroupBy(a => a.Category)
            .ToDictionary(g => g.Key, g => g.Count());
        return result;
    }
}
