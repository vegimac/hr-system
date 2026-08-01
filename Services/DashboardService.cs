using System.Globalization;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Dashboard-Cockpit: sammelt alle "Was wartet auf mich?"-Alarme.
///
/// Phase 1 (dieser Entwurf):
///   • Bewilligungen die in 30/60 Tagen ablaufen (>60 Tage = keine Warnung)
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
    private readonly SperrfristService _sperrfrist;
    public DashboardService(AppDbContext db, QstPflichtCheckService qstCheck, SperrfristService sperrfrist)
    {
        _db = db;
        _qstCheck = qstCheck;
        _sperrfrist = sperrfrist;
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
        /// <summary>Aus dashboard_warning_config.todo_priority — Frontend sortiert danach.</summary>
        public int TodoPriority { get; set; } = 100;
        /// <summary>Aus dashboard_warning_config.warn_color: none | red | red_overdue.</summary>
        public string WarnColor { get; set; } = "none";
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

        // ── Warnungs-Konfig laden (Walter-Vorgabe 06.07.2026) ──────────────
        // Globale Steuerung pro Warn-Kategorie: an/aus, Vorlauf (Tage),
        // Eskalations-Schwelle (Tage), Schweregrad (Basis + eskaliert).
        // Fehlt eine Zeile → Fallback auf enabled + den im Code hinterlegten
        // Ist-Wert (die Helfer-Methoden defaulten entsprechend).
        var warnCfg = await _db.DashboardWarningConfigs
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Category, c => c);

        // enabled: true wenn keine Zeile ODER Zeile.enabled
        bool Enabled(string cat) =>
            !warnCfg.TryGetValue(cat, out var c) || c.Enabled;
        // Vorlauf-Fenster in Tagen: konfiguriert (falls gesetzt) sonst fallback
        int WarnDays(string cat, int fallback) =>
            warnCfg.TryGetValue(cat, out var c) && c.WarnDays.HasValue
                ? c.WarnDays.Value : fallback;
        // Zwei-Stufen-Schweregrad: wenn escalate_days gesetzt und
        // daysRemaining ≤ escalate_days → eskaliert, sonst Basis.
        string Severity(string cat, int daysRemaining, string baseFallback, string escFallback)
        {
            if (!warnCfg.TryGetValue(cat, out var c))
                return daysRemaining <= 0 ? escFallback : baseFallback;
            if (c.EscalateDays.HasValue && daysRemaining <= c.EscalateDays.Value)
                return c.SeverityEscalated ?? c.SeverityBase;
            return c.SeverityBase;
        }
        // Zustandsbasiert: nur der Basis-Schweregrad.
        string SeverityState(string cat, string baseFallback) =>
            warnCfg.TryGetValue(cat, out var c) ? c.SeverityBase : baseFallback;

        // ── 1) Bewilligungen die ablaufen ──────────────────────────────────
        // Walter-Vorgabe 01.06.2026: Quelle ist jetzt ausschliesslich
        // EmployeePermitHistory.ValidTo des jüngsten Eintrags pro MA.
        // (Vorher: denormalisierte employee.permit_expiry_date — entfernt.)
        // Severity skaliert mit Dringlichkeit. CH-/Einbürgerungs-Einträge
        // (PermitTypeId IS NULL → ValidTo darf NULL sein) sind unbefristet
        // und werden ignoriert.
        // Walter-Vorgabe 14.06.2026: Cutoff von 90 → 60 Tage. Die blaue
        // „info"-Stufe (60–90 Tage Vorlauf) braucht keine Warnung — Walter
        // sieht sie erst, sobald sie in den orangen warning-Bereich rückt.
        var dueDateLimit = DateOnly.FromDateTime(now.AddDays(WarnDays("permit_expiring", 60)));
        var empBase = _db.Employees
            .Where(e => e.IsActive
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            var cpid = companyProfileId.Value;
            // Filial-Zuordnung (Walter-Bug 15.07.2026, «Berin»): aktive Verträge
            // bestimmen die Filiale; ohne aktiven Vertrag zählt der JÜNGSTE
            // Vertrag — alte, beendete Verträge (Filialwechsel) zählen NICHT mehr.
            empBase = empBase.Where(e => e.Employments.Any(em => em.IsActive && em.CompanyProfileId == cpid) || (!e.Employments.Any(em => em.IsActive) && e.Employments.OrderByDescending(em => em.ContractStartDate).Select(em => em.CompanyProfileId).FirstOrDefault() == cpid));
        }
        // Pro MA den jüngsten History-Eintrag mit PermitTypeId != NULL holen
        // (= aktive Bewilligung). NUR der jüngste — dessen ValidTo ist das
        // relevante Ablauf-Datum.
        var maList = await empBase
            .Select(e => new
            {
                e.Id, e.FirstName, e.LastName, e.EmployeeNumber, e.ExitDate,
                e.IsPayrollExcluded,
                NationalityCode   = e.NationalityRef != null ? e.NationalityRef.Code : null,
                NationalityLegacy = e.Nationality
            })
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
        // Massgebender Eintrag pro MA (Walter-Bug 15.07.2026, «Berin»):
        // NICHT stur der juengste nach Gueltig-ab — ein Datenmuell-Eintrag
        // (Ende VOR Beginn, HR-Review-Import) gewann sonst gegen die echte,
        // heute gueltige Bewilligung. Regel: 1) unplausible Zeilen
        // (ValidTo < ValidFrom) ignorieren, solange plausible existieren;
        // 2) HEUTE gueltige Bewilligung gewinnt; 3) sonst spaetestes Ende
        // (NULL = unbefristet = nie warnen).
        var heuteDo = DateOnly.FromDateTime(now);
        var youngestPerMa = histories
            .GroupBy(h => h.EmployeeId)
            .Select(g =>
            {
                var pool = g.Where(x => !x.ValidTo.HasValue || x.ValidTo.Value >= x.ValidFrom).ToList();
                if (pool.Count == 0) pool = g.ToList();
                return pool
                    .OrderByDescending(x => (x.ValidFrom <= heuteDo
                        && (!x.ValidTo.HasValue || x.ValidTo.Value >= heuteDo)) ? 1 : 0)
                    .ThenByDescending(x => x.ValidTo ?? DateOnly.MaxValue)
                    .ThenByDescending(x => x.Id)
                    .First();
            })
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
        // Eine Kategorie «Aufenthaltsbewilligung läuft ab»: Titel wechselt bei
        // Ablauf auf «seit X Tagen abgelaufen», Warnfarbe red_overdue → rot.
        foreach (var h in Enabled("permit_expiring") ? youngestPerMa : new List<EmployeePermitHistory>())
        {
            if (!maById.TryGetValue(h.EmployeeId, out var emp)) continue;
            var dueDate = h.ValidTo!.Value.ToDateTime(TimeOnly.MinValue);
            var days = (dueDate - now).Days;
            // Severity konfigurierbar: eskaliert bei days ≤ escalate_days → critical.
            // Abgelaufen (days < 0) ≤ jeder positiven Schwelle → bleibt critical.
            string severity = Severity("permit_expiring", days, "warning", "critical");
            var permitCode = h.PermitType?.Code ?? "?";
            alerts.Add(new DashboardAlert
            {
                Category = "permit_expiring",
                Severity = severity,
                // Abgelaufene Warnungen laufen WEITER als «seit X Tagen abgelaufen»
                // (Walter-Vorgabe 12.07.2026) — sie verschwinden nie von selbst.
                Title    = days < 0
                    ? $"Aufenthaltsbewilligung {permitCode} seit {-days} Tag(en) abgelaufen"
                    : $"Aufenthaltsbewilligung {permitCode} läuft ab in {days} Tagen",
                TitleKey = days < 0 ? "alert.permit.expired" : "alert.permit.expires_in_days",
                TitleArgs = new Dictionary<string, object> { ["code"] = permitCode, ["days"] = days < 0 ? -days : days },
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

        // ── 1b) Bewilligung fehlt komplett (Walter-Vorgabe 12.07.2026,
        //    KRITISCH): aktiver Ausländer (Nationalität ≠ CH) ganz OHNE
        //    Bewilligungs-Eintrag in der Historie — nicht mal ein CH-/
        //    Einbürgerungs-Eintrag (PermitTypeId NULL zählt als Eintrag).
        //    Ohne Bewilligung darf der MA nicht beschäftigt werden → Karte
        //    auf der Kritisch-Liste, Klick in den Bewilligung/QST-Tab.
        //    Unbekannte Nationalität (weder FK noch Legacy-Text) löst KEINE
        //    Warnung aus (keine Fehlalarme); Phantom-MA (ohne Lohn) auch nicht. ──
        if (Enabled("permit_missing"))
        {
            var mitEintrag = (await _db.EmployeePermitHistories
                .Where(h => maIds.Contains(h.EmployeeId))
                .Select(h => h.EmployeeId)
                .Distinct()
                .ToListAsync()).ToHashSet();
            foreach (var emp in maList)
            {
                if (emp.IsPayrollExcluded || mitEintrag.Contains(emp.Id)) continue;
                var nat = (emp.NationalityCode ?? emp.NationalityLegacy ?? "").Trim().ToUpperInvariant();
                if (nat.Length == 0) continue; // Nationalität unbekannt → kein Alarm
                if (nat is "CH" or "SCHWEIZ" or "SWITZERLAND" or "SUISSE" or "SVIZZERA") continue;
                alerts.Add(new DashboardAlert
                {
                    Category = "permit_missing",
                    Severity = SeverityState("permit_missing", "critical"),
                    Title    = "Aufenthaltsbewilligung fehlt",
                    TitleKey = "alert.permitMissing",
                    Subtitle = $"{emp.FirstName} {emp.LastName} · Personalnr. {emp.EmployeeNumber}",
                    SubtitleKey  = "subtitle.maPersonalnr",
                    SubtitleArgs = new Dictionary<string, object> {
                        ["name"]  = $"{emp.FirstName} {emp.LastName}".Trim(),
                        ["empNr"] = emp.EmployeeNumber
                    },
                    EmployeeId     = emp.Id,
                    EmployeeNumber = emp.EmployeeNumber,
                    EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                });
            }
        }

        // ── 2) Probezeit endet in 14 Tagen ─────────────────────────────────
        var probaWindow = now.AddDays(WarnDays("probation_end", 14));
        var probaQ = _db.Employments
            .Include(em => em.Employee)
            .Where(em => em.IsActive
                      && em.ProbationEndDate.HasValue
                      && em.ProbationEndDate >= now
                      && em.ProbationEndDate <= probaWindow
                      && em.Employee != null
                      && em.Employee.IsActive
                      && !em.Employee.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
            probaQ = probaQ.Where(em => em.CompanyProfileId == companyProfileId.Value);
        var probaList = Enabled("probation_end")
            ? await probaQ.ToListAsync()
            : new List<Employment>();
        foreach (var em in probaList)
        {
            var dueDate = em.ProbationEndDate!.Value;
            var days = (dueDate.Date - now).Days;
            var endeTxt = FormatWeekdayDateDe(dueDate);
            alerts.Add(new DashboardAlert
            {
                Category = "probation_end",
                Severity = Severity("probation_end", days, "info", "warning"),
                Title    = days == 0
                    ? $"Probezeit endet heute · {endeTxt}"
                    : days == 1
                        ? $"Probezeit endet morgen · {endeTxt}"
                        : $"Probezeit endet {endeTxt} (in {days} Tagen)",
                TitleKey = days == 0 ? "alert.probation.ends_today"
                         : days == 1 ? "alert.probation.ends_tomorrow"
                         : "alert.probation.ends_in_days",
                TitleArgs = new Dictionary<string, object> { ["days"] = days, ["ende"] = endeTxt },
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

        // ── 2b) Probezeitgespräch offen (Walter 21.07.2026) ────────────────
        // Todo, bis Gesprächsdatum UND Protokoll-Verknüpfung gesetzt sind.
        // Nur im Vorlauf-Fenster (warn_days, Default 14) — wie «Probezeit endet».
        // Walter 26.07.2026: abgelaufene Probezeit NICHT mehr zeigen (nach dem
        // Ende macht das Gesprächs-Todo keinen Sinn mehr).
        if (Enabled("probezeit_gespraech_offen"))
        {
            var pzGespVorlauf = WarnDays("probezeit_gespraech_offen", 14);
            var pzGespWindow = now.AddDays(pzGespVorlauf);
            var pzGespQ = _db.Employments
                .Include(em => em.Employee)
                .Where(em => em.IsActive
                          && em.ProbationEndDate.HasValue
                          && em.ProbationEndDate >= now
                          && em.ProbationEndDate <= pzGespWindow
                          && em.Employee != null
                          && em.Employee.IsActive
                          && !em.Employee.EmployeeNumber.ToLower().EndsWith("alt")
                          && (em.Employee.ProbezeitGespraech1Am == null
                              || em.Employee.ProbezeitGespraech1DokumentId == null));
            if (companyProfileId.HasValue)
                pzGespQ = pzGespQ.Where(em => em.CompanyProfileId == companyProfileId.Value);
            var pzGespList = await pzGespQ.ToListAsync();
            foreach (var em in pzGespList
                .GroupBy(e => e.EmployeeId)
                .Select(g => g.OrderBy(x => x.ProbationEndDate).First()))
            {
                var dueDate = em.ProbationEndDate!.Value;
                var days = (dueDate.Date - now).Days;
                var endeTxt = FormatWeekdayDateDe(dueDate);
                var fehltDatum = em.Employee!.ProbezeitGespraech1Am == null;
                var fehltDok = !em.Employee.ProbezeitGespraech1DokumentId.HasValue;
                var fehltTxt = fehltDatum && fehltDok ? "Gesprächsdatum + Protokoll"
                    : fehltDatum ? "Gesprächsdatum"
                    : "Protokoll";
                var name = $"{em.Employee.FirstName} {em.Employee.LastName}".Trim();
                // Walter 26.07.2026: Probezeit-Ende fett im Titel (auch bei
                // kritisch — gleiche Title-Zeile, nur Spalte/Farbe anders).
                alerts.Add(new DashboardAlert
                {
                    Category = "probezeit_gespraech_offen",
                    Severity = Severity("probezeit_gespraech_offen", days, "warning", "critical"),
                    Title    = $"Probezeitgespräch offen · {endeTxt}",
                    TitleKey = "alert.probation.gespraech_offen",
                    TitleArgs = new Dictionary<string, object> { ["ende"] = endeTxt },
                    Subtitle = $"{name} · fehlt: {fehltTxt}",
                    SubtitleKey  = "alert.probation.gespraech_fehlt",
                    SubtitleArgs = new Dictionary<string, object> {
                        ["name"] = name,
                        ["fehlt"] = fehltTxt
                    },
                    DueDate  = dueDate,
                    DaysUntil = days,
                    EmployeeId     = em.EmployeeId,
                    EmployeeNumber = em.Employee.EmployeeNumber,
                    EmployeeName   = name
                });
            }
        }

        // ── 3) Befristete Verträge enden in 30 Tagen ──────────────────────
        // Walter-Vorgabe 12.07.2026: die Warnung läuft nach dem Ablauf WEITER
        // («seit X Tagen abgelaufen») — ein aktiver MA ohne laufenden Vertrag
        // ist ein echtes Problem und darf nicht aus der Liste fallen. Kein
        // Alarm, wenn ein Folge-/anderer Vertrag den heutigen Tag abdeckt.
        // Walter-Vorgabe 26.07.2026: ist ein Kündigungsdatum gesetzt
        // (Gekündigt am / Kündigung per), ist das KEIN befristeter Vertrag
        // sondern ein Austritt → läuft über «kuendigung_ablauf» / Austritt,
        // nicht über contract_end.
        var fixedWindow = now.AddDays(WarnDays("contract_end", 30));
        var fixedQ = _db.Employments
            .Include(em => em.Employee)
            .Where(em => em.IsActive
                      && em.ContractEndDate.HasValue
                      && em.ContractEndDate <= fixedWindow
                      && em.Employee != null
                      && em.Employee.IsActive
                      && !em.Employee.KuendigungPer.HasValue
                      && !em.Employee.KuendigungAusgesprochenAm.HasValue
                      && !em.Employee.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
            fixedQ = fixedQ.Where(em => em.CompanyProfileId == companyProfileId.Value);
        var fixedListAll = Enabled("contract_end")
            ? await fixedQ.ToListAsync()
            : new List<Employment>();
        // Abgelaufene: pro MA nur der JÜNGSTE abgelaufene Vertrag, und nur wenn
        // KEIN anderer aktiver Vertrag den heutigen Tag abdeckt (Folgevertrag).
        var fixedEmpIds = fixedListAll.Select(em => em.EmployeeId).Distinct().ToList();
        var heuteAbgedeckt = (await _db.Employments.AsNoTracking()
            .Where(x => fixedEmpIds.Contains(x.EmployeeId) && x.IsActive
                     && x.ContractStartDate <= now
                     && (x.ContractEndDate == null || x.ContractEndDate >= now))
            .Select(x => x.EmployeeId).Distinct().ToListAsync()).ToHashSet();
        var fixedList = fixedListAll
            .Where(em => em.ContractEndDate!.Value.Date >= now)
            .Concat(fixedListAll
                .Where(em => em.ContractEndDate!.Value.Date < now
                          && !heuteAbgedeckt.Contains(em.EmployeeId))
                .GroupBy(em => em.EmployeeId)
                .Select(g => g.OrderByDescending(x => x.ContractEndDate).First()))
            .ToList();
        foreach (var em in fixedList)
        {
            var dueDate = em.ContractEndDate!.Value;
            var days = (dueDate.Date - now).Days;
            alerts.Add(new DashboardAlert
            {
                Category = "contract_end",
                Severity = Severity("contract_end", days, "info", "warning"),
                Title    = days < 0
                    ? $"Befristeter Vertrag seit {-days} Tag(en) abgelaufen"
                    : $"Befristeter Vertrag endet in {days} Tagen",
                TitleKey = days < 0 ? "alert.contract.expired_since" : "alert.contract.ends_in_days",
                TitleArgs = new Dictionary<string, object> { ["days"] = days < 0 ? -days : days },
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

        // ── 3b) Austritt in der ToDo (Walter 26.07.2026) ─────────────────
        // Erfasster Austritt (ExitDate) erscheint in der ToDo bis INKLUSIVE
        // Austrittstag — danach verschwindet die Karte (kein «noch aktiv»-
        // Nachlauf mehr). Vorlauf wie contract_end (Default 30 Tage).
        // ── Kündigungs-Ablauf (Walter-Vorgabe 16.07.2026): am MA ist eine
        // ausgesprochene Kündigung erfasst (kuendigung_per), aber noch KEIN
        // Austrittsdatum. 2 Wochen VOR Ablauf → ToDo «Vertragsende wegen
        // Kündigung». Sobald ExitDate gesetzt ist, übernimmt exit_pending_active.
        if (Enabled("kuendigung_ablauf"))
        {
            var kuendQ = _db.Employees
                .Where(e => e.IsActive
                         && e.KuendigungPer.HasValue
                         && !e.EmployeeNumber.ToLower().EndsWith("alt"));
            if (companyProfileId.HasValue)
            {
                kuendQ = kuendQ.Where(e =>
                    e.Employments.Any(em => em.IsActive && em.CompanyProfileId == companyProfileId.Value) || (!e.Employments.Any(em => em.IsActive) && e.Employments.OrderByDescending(em => em.ContractStartDate).Select(em => em.CompanyProfileId).FirstOrDefault() == companyProfileId.Value));
            }
            var kuendList = await kuendQ.ToListAsync();
            int kuendVorlauf = WarnDays("kuendigung_ablauf", 14);
            foreach (var e in kuendList)
            {
                // Austritt bereits erfasst → exit_pending_active bis zum Austrittstag.
                if (e.ExitDate.HasValue) continue;
                var per = e.KuendigungPer!.Value.Date;
                int daysUntil = (per - now).Days;
                if (daysUntil > kuendVorlauf) continue;   // noch zu früh
                // Walter-Vorgabe 16.07.2026 (Verschärfung): ist die Frist schon
                // ABGELAUFEN und noch immer kein Austritt erfasst → rote Warnung
                // mit eigenem Titel, verschwindet nie von selbst.
                bool abgelaufen = daysUntil < 0;
                alerts.Add(new DashboardAlert
                {
                    Category = "kuendigung_ablauf",
                    Severity = abgelaufen ? "critical"
                             : Severity("kuendigung_ablauf", daysUntil, "warning", "critical"),
                    Title    = abgelaufen
                        ? $"Kündigungsfrist abgelaufen ohne Austritt — Kündigung per {per:dd.MM.yyyy}"
                        : $"Vertragsende wegen Kündigung per {per:dd.MM.yyyy}",
                    Subtitle = $"{e.FirstName} {e.LastName} · Personalnr. {e.EmployeeNumber}"
                             + (e.KuendigungAusgesprochenAm.HasValue ? $" · gekündigt am {e.KuendigungAusgesprochenAm:dd.MM.yyyy}" : "")
                             + (e.KuendigungDurch == "AN" ? " · durch Mitarbeiter"
                                : e.KuendigungDurch == "AG" ? " · durch uns" : "")
                             + (!string.IsNullOrWhiteSpace(e.Austrittsgrund)
                                 ? $" · {AustrittsgrundCodes.LabelOf(e.Austrittsgrund)}" : "")
                             + (abgelaufen
                                 ? $" — seit {-daysUntil} Tag(en) überfällig: Austrittsdatum erfassen oder Kündigung aufheben"
                                 : " — Austrittsdatum erfassen und Vertrag beenden"),
                    DueDate  = e.KuendigungPer,
                    DaysUntil = daysUntil,
                    EmployeeId     = e.Id,
                    EmployeeNumber = e.EmployeeNumber,
                    EmployeeName   = $"{e.FirstName} {e.LastName}".Trim()
                });
            }
        }

        // ── Kündigung nach Sperrfrist möglich (Walter 25.07.2026) ─────────
        // Sobald Art. 336c-Sperrfrist bei durchgehender Krankheit/Unfall
        // ausgeschöpft ist → ToDo «Wichtig». Verschwindet bei erfasster
        // Kündigung (KuendigungPer) oder Austritt.
        if (Enabled("kuendigung_sperrfrist_ende"))
        {
            int sperrLookback = WarnDays("kuendigung_sperrfrist_ende", 90);
            var lookbackFrom = today.AddDays(-sperrLookback);
            var recentAuEmpIds = await _db.Absences.AsNoTracking()
                .Where(a => (a.AbsenceType == "KRANK" || a.AbsenceType == "UNFALL")
                         && a.DateTo >= lookbackFrom)
                .Select(a => a.EmployeeId)
                .Distinct()
                .ToListAsync();
            var sperrCandQ = _db.Employees.AsNoTracking()
                .Where(e => recentAuEmpIds.Contains(e.Id)
                         && e.IsActive
                         && e.EntryDate != null
                         && !e.KuendigungPer.HasValue
                         && !e.ExitDate.HasValue
                         && !e.EmployeeNumber.ToLower().EndsWith("alt"));
            if (companyProfileId.HasValue)
            {
                sperrCandQ = sperrCandQ.Where(e =>
                    e.Employments.Any(em => em.IsActive && em.CompanyProfileId == companyProfileId.Value)
                    || (!e.Employments.Any(em => em.IsActive)
                        && e.Employments.OrderByDescending(em => em.ContractStartDate)
                            .Select(em => em.CompanyProfileId).FirstOrDefault() == companyProfileId.Value));
            }
            var sperrCands = await sperrCandQ
                .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
                .ToListAsync();
            foreach (var e in sperrCands)
            {
                var info = await _sperrfrist.ComputeAsync(e.Id, today);
                // Nur bei laufender AU mit ausgeschöpfter Sperrfrist.
                // Nach AU-Ende gilt normale L-GAV-Kündigung — kein ToDo.
                if (info.Status != "SPERRFRIST_ABGELAUFEN")
                    continue;
                // Nur im konfigurierten Fenster nach «kündbar ab» behalten
                if (info.KuendigungAbDatum.HasValue)
                {
                    int daysSinceKuendbar = today.DayNumber - info.KuendigungAbDatum.Value.DayNumber;
                    if (daysSinceKuendbar > sperrLookback) continue;
                }
                var name = $"{e.FirstName} {e.LastName}".Trim();
                string auTxt = (info.AuBeginn.HasValue && info.AuEnde.HasValue)
                    ? $"AU {info.AuBeginn:dd.MM.yyyy} – {info.AuEnde:dd.MM.yyyy}"
                    : "durchgehende AU";
                string sperrTxt = info.SperrfristEnde.HasValue
                    ? $"Sperrfrist bis {info.SperrfristEnde:dd.MM.yyyy}"
                    : "Sperrfrist abgelaufen";
                int? daysUntil = info.KuendigungAbDatum.HasValue
                    ? info.KuendigungAbDatum.Value.DayNumber - today.DayNumber
                    : 0;
                alerts.Add(new DashboardAlert
                {
                    Category = "kuendigung_sperrfrist_ende",
                    Severity = SeverityState("kuendigung_sperrfrist_ende", "warning"),
                    Title    = "Kündigung jetzt möglich (Sperrfrist abgelaufen, AU läuft noch)",
                    Subtitle = $"{name} · Personalnr. {e.EmployeeNumber} · {auTxt} · {sperrTxt}"
                             + (info.KuendigungAbDatum.HasValue
                                 ? $" · kündbar seit {info.KuendigungAbDatum:dd.MM.yyyy}"
                                 : ""),
                    TitleKey = "alert.kuendigung.sperrfrist_ende",
                    DueDate   = info.KuendigungAbDatum?.ToDateTime(TimeOnly.MinValue),
                    DaysUntil = daysUntil,
                    EmployeeId     = e.Id,
                    EmployeeNumber = e.EmployeeNumber,
                    EmployeeName   = name
                });
            }
        }

        var exitPendingQ = _db.Employees
            .Where(e => e.IsActive
                     && e.ExitDate.HasValue
                     && e.ExitDate.Value.Date >= now
                     && !e.EmployeeNumber.ToLower().EndsWith("alt"));
        if (companyProfileId.HasValue)
        {
            // MA hat in einer der Filialen einen Vertrag → filtere auf passende.
            // Filial-Zuordnung (Walter-Bug 15.07.2026, «Berin»): aktive Verträge
            // bestimmen die Filiale; ohne aktiven Vertrag zählt der JÜNGSTE
            // Vertrag — alte, beendete Verträge (Filialwechsel) zählen NICHT mehr.
            exitPendingQ = exitPendingQ.Where(e =>
                e.Employments.Any(em => em.IsActive && em.CompanyProfileId == companyProfileId.Value) || (!e.Employments.Any(em => em.IsActive) && e.Employments.OrderByDescending(em => em.ContractStartDate).Select(em => em.CompanyProfileId).FirstOrDefault() == companyProfileId.Value));
        }
        var exitPendingList = Enabled("exit_pending_active")
            ? await exitPendingQ.ToListAsync()
            : new List<Employee>();
        // Walter 26.07.2026: ToDo NUR bis zum Austrittstag (inkl.). Tage bis
        // Austritt; escalate_days = kritisch kurz davor (wie contract_end).
        int exitVorlauf = WarnDays("exit_pending_active", 30);
        foreach (var e in exitPendingList)
        {
            var exitDate = e.ExitDate!.Value;
            var daysUntil = (exitDate.Date - now).Days;
            if (daysUntil > exitVorlauf) continue; // noch zu früh
            var name = $"{e.FirstName} {e.LastName}".Trim();
            var wegenKuend = e.KuendigungPer.HasValue || e.KuendigungAusgesprochenAm.HasValue;
            alerts.Add(new DashboardAlert
            {
                Category = "exit_pending_active",
                Severity = Severity("exit_pending_active", daysUntil, "warning", "critical"),
                Title    = daysUntil == 0
                    ? $"Austritt heute · {exitDate:dd.MM.yyyy}"
                    : $"Austritt am {exitDate:dd.MM.yyyy}",
                TitleKey = daysUntil == 0 ? "alert.exit.today" : "alert.exit.pending_active",
                TitleArgs = new Dictionary<string, object> { ["date"] = exitDate.ToString("dd.MM.yyyy") },
                Subtitle = $"{name} · Personalnr. {e.EmployeeNumber}"
                         + (daysUntil > 0 ? $" · in {daysUntil} Tag(en)" : "")
                         + (wegenKuend
                             ? (e.KuendigungAusgesprochenAm.HasValue
                                 ? $" · gekündigt am {e.KuendigungAusgesprochenAm:dd.MM.yyyy}"
                                 : " · Kündigung")
                             : ""),
                SubtitleKey  = "subtitle.exitPendingActive",
                SubtitleArgs = new Dictionary<string, object> {
                    ["name"] = name,
                    ["empNr"] = e.EmployeeNumber,
                    ["days"] = daysUntil
                },
                DueDate  = exitDate,
                DaysUntil = daysUntil,
                EmployeeId     = e.Id,
                EmployeeNumber = e.EmployeeNumber,
                EmployeeName   = name
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
        // Nur laufen, wenn mindestens eine der drei QST-Warnungen aktiv ist
        // (spart den teuren Per-MA-CheckAsync bei allen deaktiviert).
        var qstCandidateIds = (Enabled("qst_pflicht_offen")
                               || Enabled("spouse_doku_fehlt")
                               || Enabled("employee_doku_fehlt"))
            ? await qstCandidatesQ.Select(e => e.Id).ToListAsync()
            : new List<int>();
        foreach (var empId in qstCandidateIds)
        {
            var r = await _qstCheck.CheckAsync(empId, qstStichtag);

            // 3c.i) QST-Pflicht offen — critical, blockt Lohnlauf
            if (r.IsPflichtOffen && Enabled("qst_pflicht_offen"))
            {
                var emp = await _db.Employees.FirstOrDefaultAsync(x => x.Id == empId);
                if (emp == null) continue;
                alerts.Add(new DashboardAlert
                {
                    Category = "qst_pflicht_offen",
                    Severity = SeverityState("qst_pflicht_offen", "critical"),
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
            // 3c.ii) Ausweis Ehegatte fehlt (Walter-Vorgabe 12.06.2026)
            // Befreiung über Ehepartner (CH oder C) gilt, aber der Beleg
            // (Dokument vom Typ linked_field_code='spouse') ist noch nicht
            // hinterlegt. Warning, kein Lohnlauf-Block. Spiegelt die
            // KontrollListen-Liste, damit die Lücke auch im Dashboard sichtbar
            // ist. Klick → Familie-Tab (dort Variante-C-Upload des Ausweises).
            else if (r.SpouseDokumentFehlt && Enabled("spouse_doku_fehlt")
                     && (r.BefreiungsGrund == "Ehepartner-CH" || r.BefreiungsGrund == "Ehepartner-C"))
            {
                var emp = await _db.Employees.FirstOrDefaultAsync(x => x.Id == empId);
                if (emp == null) continue;
                var grundText = r.BefreiungsGrund == "Ehepartner-CH"
                    ? "Ehepartner ist CH-Bürger"
                    : "Ehepartner hat C-Bewilligung";
                alerts.Add(new DashboardAlert
                {
                    Category = "spouse_doku_fehlt",
                    Severity = SeverityState("spouse_doku_fehlt", "critical"),
                    Title    = "Ausweis Ehepartner fehlt für die QST-Befreiung",
                    TitleKey = "alert.spouseDokuFehlt",
                    Subtitle = $"{emp.FirstName} {emp.LastName} · Personalnr. {emp.EmployeeNumber} · {grundText}",
                    SubtitleKey = "subtitle.spouseDokuFehlt",
                    SubtitleArgs = new Dictionary<string, object> {
                        ["name"]  = $"{emp.FirstName} {emp.LastName}".Trim(),
                        ["empNr"] = emp.EmployeeNumber,
                        ["grund"] = grundText
                    },
                    EmployeeId     = emp.Id,
                    EmployeeNumber = emp.EmployeeNumber,
                    EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                });
            }
            // 3c.iii) Ausweis MA fehlt (Walter-Vorgabe 13.06.2026)
            // Analog zum Ehepartner-Check, aber für den MA selbst:
            //   CH-Bürger → ID-Karte ODER Pass muss hinterlegt sein
            //   C-Ausweis → Bewilligungs-Dokument muss hinterlegt sein
            // Klick → Dokumente-Tab (dort Upload).
            else if (r.EmployeeDokumentFehlt && Enabled("employee_doku_fehlt")
                     && (r.BefreiungsGrund == "CH-Buerger" || r.BefreiungsGrund == "C-Ausweis"))
            {
                var emp = await _db.Employees.FirstOrDefaultAsync(x => x.Id == empId);
                if (emp == null) continue;
                var grundText = r.BefreiungsGrund == "CH-Buerger"
                    ? "Schweizer Bürger — ID oder Pass fehlt"
                    : "C-Ausweis — Bewilligungs-Dokument fehlt";
                var titleText = r.BefreiungsGrund == "CH-Buerger"
                    ? "Ausweis fehlt (ID oder Pass)"
                    : "Ausweis fehlt (Bewilligung)";
                alerts.Add(new DashboardAlert
                {
                    Category = "employee_doku_fehlt",
                    Severity = SeverityState("employee_doku_fehlt", "critical"),
                    Title    = titleText,
                    TitleKey = r.BefreiungsGrund == "CH-Buerger"
                                ? "alert.employeeDokuFehlt.idPass"
                                : "alert.employeeDokuFehlt.permit",
                    Subtitle = $"{emp.FirstName} {emp.LastName} · Personalnr. {emp.EmployeeNumber} · {grundText}",
                    SubtitleKey = "subtitle.employeeDokuFehlt",
                    SubtitleArgs = new Dictionary<string, object> {
                        ["name"]  = $"{emp.FirstName} {emp.LastName}".Trim(),
                        ["empNr"] = emp.EmployeeNumber,
                        ["grund"] = grundText
                    },
                    EmployeeId     = emp.Id,
                    EmployeeNumber = emp.EmployeeNumber,
                    EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                });
            }
        }

        // ── 3d) Aktive Schwangerschaften (Walter-Vorgabe 10.06.2026) ───────
        // Pro aktive Schwangerschaft eine Info-Card mit Geburtstermin und
        // einer kurzen Liste „aktuell erlaubt/nicht erlaubt" — die wird live
        // aus dem Regelwerk berechnet.
        var pregnancyQ = _db.EmployeePregnancies
            .Include(p => p.Employee)
            .Where(p => p.IsActive);
        if (companyProfileId.HasValue)
            pregnancyQ = pregnancyQ.Where(p =>
                p.Employee != null && p.Employee.Employments.Any(em =>
                    em.CompanyProfileId == companyProfileId.Value && em.IsActive));
        var pregnancies = Enabled("schwangerschaft")
            ? await pregnancyQ.ToListAsync()
            : new List<EmployeePregnancy>();
        if (pregnancies.Any())
        {
            var pRules = await _db.PregnancyRules
                .Where(r => r.Aktiv)
                .OrderBy(r => r.SortOrder)
                .ToListAsync();
            var heute = DateOnly.FromDateTime(now);
            // Vorausschau-Fenster für „bevorstehende" Fristen — konfigurierbar.
            var pregLookahead = WarnDays("schwangerschaft", 30);

            foreach (var preg in pregnancies)
            {
                if (preg.Employee == null) continue;

                // Geburtsdatum bzw. ET — bei „Geburt + 16 Wochen vorbei"
                // wird die Card ausgeblendet (Schutz endet, Mutterschaft
                // abgeschlossen).
                var schutzBasis = preg.Geburtsdatum ?? preg.ErrechneterTermin;
                var schutzEnde  = schutzBasis.AddDays(16 * 7);
                if (heute > schutzEnde) continue;

                // Aktuelle Fristen berechnen — Liste „verboten/erlaubt jetzt".
                // Walter-Vorgabe 13.06.2026: Berechnung zentral via
                // PregnancyFristCalculator (vorher hier dupliziert).
                var aktivVerbote = new List<string>();
                var bevorstehende = new List<string>();
                foreach (var r in pRules)
                {
                    var f = PregnancyFristCalculator.Calculate(r, preg, heute);

                    if (f.Status == "bevorstehend")
                    {
                        // nur die konfigurierte Vorausschau anzeigen (Default 30 Tage)
                        if ((f.Datum.DayNumber - heute.DayNumber) <= pregLookahead)
                            bevorstehende.Add($"{r.Bezeichnung} (ab {f.Datum:dd.MM.yyyy})");
                    }
                    else if (f.Status == "abgeschlossen")
                    {
                        // Phase ist vorbei → kein aktiver Verbot mehr
                    }
                    else // "aktiv"
                    {
                        if (r.IstArbeitsverbot) aktivVerbote.Add(r.Bezeichnung);
                    }
                }

                string geburtTxt = preg.Geburtsdatum.HasValue
                    ? $"Geburt: {preg.Geburtsdatum.Value:dd.MM.yyyy}"
                    : $"Errechneter Termin: {preg.ErrechneterTermin:dd.MM.yyyy}";

                var sub = new List<string> { geburtTxt };
                if (aktivVerbote.Any())
                    sub.Add("Arbeitsverbot: " + string.Join(" · ", aktivVerbote));
                if (bevorstehende.Any())
                    sub.Add("Bald: " + string.Join(" · ", bevorstehende));

                // Schweregrad ist hier zustandsgetrieben (Arbeitsverbot aktiv →
                // eskaliert, sonst Basis) — Basis/Eskaliert kommen aus der Konfig.
                string schwEsc = warnCfg.TryGetValue("schwangerschaft", out var schwCfg)
                    ? (schwCfg.SeverityEscalated ?? schwCfg.SeverityBase) : "warning";
                string schwBase = warnCfg.TryGetValue("schwangerschaft", out var schwCfg2)
                    ? schwCfg2.SeverityBase : "info";
                alerts.Add(new DashboardAlert
                {
                    Category    = "schwangerschaft",
                    Severity    = aktivVerbote.Any() ? schwEsc : schwBase,
                    Title       = $"Mutterschaft: {preg.Employee.FirstName} {preg.Employee.LastName}",
                    Subtitle    = string.Join(" — ", sub),
                    EmployeeId     = preg.Employee.Id,
                    EmployeeNumber = preg.Employee.EmployeeNumber,
                    EmployeeName   = $"{preg.Employee.FirstName} {preg.Employee.LastName}".Trim()
                });
            }
        }

        // ── 4) Lohnperioden warten auf Aktion ──────────────────────────────
        var lohnQ = _db.PayrollPerioden
            .Include(p => p.Company)
            .Where(p => p.Status == "provisorisch_abgeschlossen");
        if (companyProfileId.HasValue)
            lohnQ = lohnQ.Where(p => p.CompanyProfileId == companyProfileId.Value);
        var provPeriods = Enabled("lohn_provisorisch")
            ? await lohnQ.ToListAsync()
            : new List<PayrollPeriode>();
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
                Severity = SeverityState("lohn_provisorisch", "warning"),
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
            // Filial-Zuordnung (Walter-Bug 15.07.2026, «Berin»): aktive Verträge
            // bestimmen die Filiale; ohne aktiven Vertrag zählt der JÜNGSTE
            // Vertrag — alte, beendete Verträge (Filialwechsel) zählen NICHT mehr.
            birthQ = birthQ.Where(e => e.Employments.Any(em => em.IsActive && em.CompanyProfileId == cpid) || (!e.Employments.Any(em => em.IsActive) && e.Employments.OrderByDescending(em => em.ContractStartDate).Select(em => em.CompanyProfileId).FirstOrDefault() == cpid));
        }
        var birthEmps = Enabled("birthday")
            ? await birthQ.ToListAsync()
            : new List<Employee>();
        var birthdayWindow = WarnDays("birthday", 7);
        foreach (var e in birthEmps)
        {
            var bd = e.DateOfBirth!.Value;
            // Geburtstag in diesem Jahr (oder nächstes Jahr falls schon gewesen)
            var thisYearBday = new DateTime(now.Year, bd.Month, bd.Day == 29 && bd.Month == 2 && !DateTime.IsLeapYear(now.Year) ? 28 : bd.Day);
            if (thisYearBday < now) thisYearBday = thisYearBday.AddYears(1);
            var days = (thisYearBday - now).Days;
            if (days < 0 || days > birthdayWindow) continue;

            var ageOnBday = thisYearBday.Year - bd.Year;
            alerts.Add(new DashboardAlert
            {
                Category = "birthday",
                Severity = SeverityState("birthday", "info"),
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
            // Filial-Zuordnung (Walter-Bug 15.07.2026, «Berin»): aktive Verträge
            // bestimmen die Filiale; ohne aktiven Vertrag zählt der JÜNGSTE
            // Vertrag — alte, beendete Verträge (Filialwechsel) zählen NICHT mehr.
            jubQ = jubQ.Where(e => e.Employments.Any(em => em.IsActive && em.CompanyProfileId == cpid) || (!e.Employments.Any(em => em.IsActive) && e.Employments.OrderByDescending(em => em.ContractStartDate).Select(em => em.CompanyProfileId).FirstOrDefault() == cpid));
        }
        var jubEmps = Enabled("anniversary")
            ? await jubQ.ToListAsync()
            : new List<Employee>();
        var anniversaryWindow = WarnDays("anniversary", 30);
        foreach (var e in jubEmps)
        {
            var entry = e.EntryDate!.Value;
            // Nächstes Jubiläum-Datum berechnen
            var thisYearEntry = new DateTime(now.Year, entry.Month, entry.Day == 29 && entry.Month == 2 && !DateTime.IsLeapYear(now.Year) ? 28 : entry.Day);
            if (thisYearEntry < now) thisYearEntry = thisYearEntry.AddYears(1);
            var yearsAtEntry = thisYearEntry.Year - entry.Year;
            if (!milestoneYears.Contains(yearsAtEntry)) continue;
            var days = (thisYearEntry - now).Days;
            if (days < 0 || days > anniversaryWindow) continue;

            alerts.Add(new DashboardAlert
            {
                Category = "anniversary",
                Severity = SeverityState("anniversary", "info"),
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
                _       => "FLEX"
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
                if (!Enabled("minimum_wage_violation")) continue;
                var unit = salaryType == "monthly" ? "/Mt." : "/h";
                alerts.Add(new DashboardAlert
                {
                    Category = "minimum_wage_violation",
                    Severity = SeverityState("minimum_wage_violation", "critical"),
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

        // ── Nachtarbeit-Nachweise fehlen (Walter-Vorgabe 22.06.2026, ArGV1 Art. 30) ──
        // MA mit > 18 gearbeiteten Nächten in einem rollierenden 6-Wochen-Fenster
        // (42 Tage) UND ohne vollständige Nachweise. Nachtarbeit ist obligatorisch
        // (kein Verzicht, HQ-Entscheid 30.06.2026): es braucht ein AKTUELLES
        // Arztzeugnis (nicht abgelaufen) UND die Ausnahmeregelung. Live gerechnet
        // aus den Nacht-Tagen der letzten 12 Monate; Dokument-Status kommt live vom
        // MA, das Erfassen entfernt die Warnung sofort.
        {
            var nwRollStart = new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
            // Nacht-Tage (nur Tage mit Nachtstunden) — distinct pro MA.
            var nightDays = await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => maIds.Contains(t.EmployeeId) && t.EntryDate >= nwRollStart
                         && t.EntryDate <= today && (t.NightHours ?? 0m) > 0m)
                .Select(t => new { t.EmployeeId, t.EntryDate })
                .Distinct().ToListAsync();
            var nightDatesByEmp = nightDays.GroupBy(x => x.EmployeeId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.EntryDate).ToList());
            // Dokument-Status der aktiven MA (Arzt/Verzicht + Ausnahmeregelung/Checkliste).
            var nwExam = await _db.Employees.AsNoTracking()
                .Where(e => maIds.Contains(e.Id))
                .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber,
                                   e.NightWorkExamDokumentId, e.NightWorkExamValidUntil,
                                   e.NightWorkExamIssued, e.NightWorkExamEasyMismatch, e.DateOfBirth,
                                   e.NightWorkAusnahmeDokumentId, e.ExitDate, e.KuendigungPer })
                .ToListAsync();
            // MA mit Austritt ODER Kündigung per (innerhalb 30 Tage / bereits
            // vorbei) NICHT mehr melden — Zeugnis erneuern lohnt nicht mehr
            // (Walter 20.06.2026 / präzisiert 20.07.2026: KuendigungPer zählt
            // auch wenn ExitDate noch leer ist).
            var nwExitCutoff = today.AddDays(30);
            foreach (var emp in nwExam)
            {
                var leaveAt = emp.ExitDate ?? emp.KuendigungPer;
                if (leaveAt.HasValue
                    && DateOnly.FromDateTime(leaveAt.Value) <= nwExitCutoff) continue;

                // Abgelaufenes Arztzeugnis IMMER melden (Walter-Vorgabe 12.07.2026):
                // auch wenn der MA aktuell nicht dokumentationspflichtig viele Nächte
                // arbeitet — vor der nächsten Nacht-Planung muss erneuert werden.
                // Läuft als «seit X Tagen abgelaufen» weiter, verschwindet nie von
                // selbst. (Für dokupflichtige Nacht-MA übernimmt weiter unten die
                // «Nachweise fehlen»-Karte — keine Doppel-Meldung.)
                int? abgelaufenSeit = emp.NightWorkExamValidUntil.HasValue
                    && DateOnly.FromDateTime(emp.NightWorkExamValidUntil.Value) < today
                    ? today.DayNumber - DateOnly.FromDateTime(emp.NightWorkExamValidUntil.Value).DayNumber
                    : (int?)null;
                // Ab 45 gilt das Nachtarbeit-Zeugnis nur 1 Jahr (ArGV1) — auf den
                // ToDo-/PDF-Zeilen sichtbar machen (Walter-Vorgabe 14.07.2026).
                bool ist45Plus = false;
                if (emp.DateOfBirth.HasValue)
                {
                    var nwDob45 = DateOnly.FromDateTime(emp.DateOfBirth.Value);
                    int alter = today.Year - nwDob45.Year;
                    if (today < nwDob45.AddYears(alter)) alter--;
                    ist45Plus = alter >= 45;
                }
                string hinweis45 = ist45Plus ? " · ab 45: Zeugnis nur 1 Jahr gültig" : "";
                void MeldeAbgelaufen()
                {
                    if (abgelaufenSeit == null || !Enabled("night_work_exam_expiring")) return;
                    alerts.Add(new DashboardAlert
                    {
                        Category = "night_work_exam_expiring",
                        Severity = Severity("night_work_exam_expiring", -abgelaufenSeit.Value, "warning", "critical"),
                        Title    = $"Nachtarbeit-Arztzeugnis seit {abgelaufenSeit} Tag(en) abgelaufen",
                        Subtitle = $"{emp.FirstName} {emp.LastName} · Personalnr. {emp.EmployeeNumber} · gültig bis {emp.NightWorkExamValidUntil:dd.MM.yyyy} — vor der nächsten Nacht-Planung erneuern{hinweis45}",
                        DueDate  = emp.NightWorkExamValidUntil,
                        DaysUntil = -abgelaufenSeit.Value,   // negativ = abgelaufen (ToDo: rote Schrift)
                        EmployeeId     = emp.Id,
                        EmployeeNumber = emp.EmployeeNumber,
                        EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                    });
                }

                // Datum erfasst = Nachtarbeit geplant → Dokumente prüfen
                // (Walter 19.07.2026). Zusätzlich ArGV1 Art. 30 (>18 Nächte/42 Tage).
                bool hasPlannedDates = emp.NightWorkExamIssued.HasValue
                                    || emp.NightWorkExamValidUntil.HasValue;

                NightWorkComplianceService.Result nw;
                if (!nightDatesByEmp.TryGetValue(emp.Id, out var dates) || dates.Count == 0)
                {
                    if (!hasPlannedDates)
                    {
                        MeldeAbgelaufen(); // nur Ablauf, keine Doku-Pflicht
                        continue;
                    }
                    // Datum erfasst = geplant → unten Dokumente prüfen (kein Doppel-Ablauf-Alert).
                    nw = new NightWorkComplianceService.Result(0, null, null, false);
                }
                else
                {
                    nw = NightWorkComplianceService.Evaluate(dates, today);
                    if (!nw.RequiresDocuments && !hasPlannedDates)
                    {
                        MeldeAbgelaufen();
                        continue;
                    }
                }

                // Enddatum-Kontrolle (Walter 26.07.2026): OneCrew speichert das
                // gerechnete Soll-Ende; Flag kommt vom Sync wenn easy-«to» abweicht.
                if (emp.NightWorkExamIssued.HasValue && emp.NightWorkExamEasyMismatch
                    && Enabled("night_work_exam_mismatch"))
                {
                    var nwIssued = DateOnly.FromDateTime(emp.NightWorkExamIssued.Value);
                    var nwDob = emp.DateOfBirth.HasValue ? DateOnly.FromDateTime(emp.DateOfBirth.Value) : (DateOnly?)null;
                    var nwSoll = Employee.NightWorkValidUntil(nwIssued, nwDob);
                    alerts.Add(new DashboardAlert
                    {
                        Category = "night_work_exam_mismatch",
                        Severity = SeverityState("night_work_exam_mismatch", "critical"),
                        Title    = "Nachtarbeit-Enddatum in easy@work falsch",
                        Subtitle = $"{emp.FirstName} {emp.LastName} · Personalnr. {emp.EmployeeNumber} · Beginn {nwIssued:dd.MM.yyyy} → Soll-Ende {nwSoll:dd.MM.yyyy}. Bitte in easy@work korrigieren.",
                        EmployeeId     = emp.Id,
                        EmployeeNumber = emp.EmployeeNumber,
                        EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                    });
                }

                // Nachtarbeit ist obligatorisch — KEIN Verzicht mehr (Walter-Vorgabe
                // 30.06.2026, HQ-Entscheid). Akzeptiert erst mit verknüpftem
                // Arztzeugnis-Dokument + aktueller Gültigkeit + Ausnahmeregelung.
                // Datum allein (easy@work/manuell) zählt NICHT (Walter 19.07.2026).
                bool hasArztDoc  = emp.NightWorkExamDokumentId.HasValue;
                bool dateCurrent = (emp.NightWorkExamValidUntil.HasValue
                                    && DateOnly.FromDateTime(emp.NightWorkExamValidUntil.Value) >= today)
                                 || (hasArztDoc && !emp.NightWorkExamValidUntil.HasValue);
                // Akzeptiert = Scan verknüpft UND Datum aktuell (Datum allein genügt nie).
                bool examAccepted = hasArztDoc && dateCurrent;
                bool examExpired  = emp.NightWorkExamValidUntil.HasValue
                                    && DateOnly.FromDateTime(emp.NightWorkExamValidUntil.Value) < today;
                bool hasChecklist = emp.NightWorkAusnahmeDokumentId.HasValue;
                bool nachweiseVoll = examAccepted && hasChecklist;

                // Ablauf-Warnung der Bewilligung (Walter-Vorgabe 05.07.2026):
                // ≤ 7 Tage vor Ablauf → KRITISCH; ≤ 1 Monat → WICHTIG.
                // Abgelaufen bei doku-pflichtigen MA: Titel-Priorität unten
                // («Arztzeugnis seit N Tagen abgelaufen», nicht «Nachweise fehlen»).
                // Hier nur noch GÜLTIGE, aber bald ablaufende Bewilligung.
                if (nachweiseVoll)
                {
                    if (emp.NightWorkExamValidUntil.HasValue && Enabled("night_work_exam_expiring"))
                    {
                        var bis = DateOnly.FromDateTime(emp.NightWorkExamValidUntil.Value);
                        int tage = bis.DayNumber - today.DayNumber;   // >= 0, weil dateCurrent
                        if (tage <= WarnDays("night_work_exam_expiring", 30))
                        {
                            string phrase = tage == 0 ? $"läuft heute ab ({bis:dd.MM.yyyy})"
                                          : $"läuft in {tage} Tag(en) ab ({bis:dd.MM.yyyy})";
                            alerts.Add(new DashboardAlert
                            {
                                Category = "night_work_exam_expiring",
                                Severity = Severity("night_work_exam_expiring", tage, "warning", "critical"),
                                Title    = "Nachtarbeit-Bewilligung läuft ab",
                                Subtitle = $"{emp.FirstName} {emp.LastName} · Personalnr. {emp.EmployeeNumber} · Bewilligung {phrase}{hinweis45}",
                                DueDate   = emp.NightWorkExamValidUntil,
                                DaysUntil = tage,   // ≥ 0 → noch nicht rot bei red_overdue
                                EmployeeId     = emp.Id,
                                EmployeeNumber = emp.EmployeeNumber,
                                EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                            });
                        }
                    }
                    continue;   // Nachweise vollständig + aktuell (Ablauf ggf. oben gemeldet)
                }

                // Getrennte Kategorien (Walter 31.07.2026):
                //  • night_work_untersuch_fehlt — kein aktuelles Datum (kein Untersuch) → Kritisch
                //  • night_work_exam_expiring   — Zeugnis abgelaufen → konfigurierbar
                //  • night_work_exam_fehlt      — nur Arztzeugnis-Scan fehlt → Kritisch
                //  • night_work_ausnahme_fehlt  — nur Ausnahmeregelung fehlt → Wichtig
                // Beide Dokument-Lücken können parallel als eigene Todos erscheinen.
                string subtitleBase =
                    $"{emp.FirstName} {emp.LastName} · Personalnr. {emp.EmployeeNumber}"
                    + (nw.MaxNightsInSixWeeks > 0
                        ? $" · {nw.MaxNightsInSixWeeks} Nächte in den letzten 6 Wochen"
                        : " · Nachtarbeit geplant (Datum erfasst)");

                void MeldeAusnahmeFehlt()
                {
                    if (hasChecklist || !Enabled("night_work_ausnahme_fehlt")) return;
                    alerts.Add(new DashboardAlert
                    {
                        Category = "night_work_ausnahme_fehlt",
                        Severity = SeverityState("night_work_ausnahme_fehlt", "warning"),
                        Title    = "Nachtarbeit-Ausnahmeregelung fehlt",
                        Subtitle = subtitleBase + hinweis45,
                        EmployeeId     = emp.Id,
                        EmployeeNumber = emp.EmployeeNumber,
                        EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                    });
                }

                if (examExpired)
                {
                    if (Enabled("night_work_exam_expiring"))
                    {
                        alerts.Add(new DashboardAlert
                        {
                            Category = "night_work_exam_expiring",
                            Severity = Severity("night_work_exam_expiring", -(abgelaufenSeit ?? 0), "warning", "critical"),
                            Title    = $"Nachtarbeit-Arztzeugnis seit {abgelaufenSeit ?? 0} Tag(en) abgelaufen",
                            Subtitle = subtitleBase + hinweis45,
                            DueDate   = emp.NightWorkExamValidUntil,
                            DaysUntil = abgelaufenSeit != null ? -abgelaufenSeit.Value : -1,
                            EmployeeId     = emp.Id,
                            EmployeeNumber = emp.EmployeeNumber,
                            EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                        });
                    }
                    MeldeAusnahmeFehlt();
                    continue;
                }

                // Weder aktuelles Datum noch akzeptierter Scan → Untersuch fehlt.
                // (Datum ohne Scan → unten «Arztzeugnis fehlt», nicht hier.)
                if (!dateCurrent && !hasArztDoc)
                {
                    if (Enabled("night_work_untersuch_fehlt"))
                    {
                        alerts.Add(new DashboardAlert
                        {
                            Category = "night_work_untersuch_fehlt",
                            Severity = SeverityState("night_work_untersuch_fehlt", "critical"),
                            Title    = "Nacht Untersuch fehlt",
                            Subtitle = subtitleBase + hinweis45,
                            EmployeeId     = emp.Id,
                            EmployeeNumber = emp.EmployeeNumber,
                            EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                        });
                    }
                    MeldeAusnahmeFehlt();
                    continue;
                }

                // Arztzeugnis-Scan fehlt (Datum ggf. aus easy@work vorhanden).
                if (!examAccepted && Enabled("night_work_exam_fehlt"))
                {
                    alerts.Add(new DashboardAlert
                    {
                        Category = "night_work_exam_fehlt",
                        Severity = SeverityState("night_work_exam_fehlt", "critical"),
                        Title    = "Nachtarbeit-Arztzeugnis fehlt",
                        Subtitle = subtitleBase + hinweis45,
                        EmployeeId     = emp.Id,
                        EmployeeNumber = emp.EmployeeNumber,
                        EmployeeName   = $"{emp.FirstName} {emp.LastName}".Trim()
                    });
                }

                MeldeAusnahmeFehlt();
            }
        }

        // ── Verfügbarkeit fehlt (Walter-Vorgabe 07.07.2026) ───────────────
        // Aktive MA mit einem aktiven Vertrag (ContractEndDate null oder ≥ heute),
        // aber OHNE gültige Verfügbarkeit heute (keine employee_availability-Zeile
        // mit valid_from ≤ heute ≤ valid_to|null). Die Verfügbarkeit ist eine
        // L-GAV-Anlage zum Vertrag und muss hinterlegt sein.
        if (Enabled("availability_missing"))
        {
            var availQ = _db.Employees
                .Where(e => e.IsActive
                         && !e.EmployeeNumber.ToLower().EndsWith("alt")
                         && !e.IsHidden
                         && !e.IsPayrollExcluded
                         && e.Employments.Any(em => em.IsActive
                                && (!em.ContractEndDate.HasValue || em.ContractEndDate.Value >= now)));
            if (companyProfileId.HasValue)
            {
                var cpid = companyProfileId.Value;
                // Filial-Zuordnung (Walter-Bug 15.07.2026, «Berin»): aktive Verträge
            // bestimmen die Filiale; ohne aktiven Vertrag zählt der JÜNGSTE
            // Vertrag — alte, beendete Verträge (Filialwechsel) zählen NICHT mehr.
            availQ = availQ.Where(e => e.Employments.Any(em => em.IsActive && em.CompanyProfileId == cpid) || (!e.Employments.Any(em => em.IsActive) && e.Employments.OrderByDescending(em => em.ContractStartDate).Select(em => em.CompanyProfileId).FirstOrDefault() == cpid));
            }
            var availCandidates = await availQ
                .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
                .ToListAsync();

            if (availCandidates.Count > 0)
            {
                var candIds = availCandidates.Select(x => x.Id).ToList();
                // MA-Ids mit einer HEUTE gültigen Verfügbarkeit.
                var haveAvail = await _db.EmployeeAvailabilities
                    .Where(a => candIds.Contains(a.EmployeeId)
                             && a.ValidFrom <= today
                             && (a.ValidTo == null || a.ValidTo >= today))
                    .Select(a => a.EmployeeId)
                    .Distinct()
                    .ToListAsync();
                var haveSet = haveAvail.ToHashSet();

                foreach (var e in availCandidates.Where(x => !haveSet.Contains(x.Id)))
                {
                    var name = $"{e.FirstName} {e.LastName}".Trim();
                    alerts.Add(new DashboardAlert
                    {
                        Category = "availability_missing",
                        Severity = SeverityState("availability_missing", "warning"),
                        Title    = "Verfügbarkeit fehlt",
                        TitleKey = "alert.availability.missing",
                        Subtitle = $"{name} · Personalnr. {e.EmployeeNumber} · aktiver Vertrag ohne hinterlegte Verfügbarkeit",
                        SubtitleKey  = "subtitle.availabilityMissing",
                        SubtitleArgs = new Dictionary<string, object> {
                            ["name"] = name,
                            ["empNr"] = e.EmployeeNumber
                        },
                        EmployeeId     = e.Id,
                        EmployeeNumber = e.EmployeeNumber,
                        EmployeeName   = name
                    });
                }
            }
        }

        // ── Audit-Log stumm (Walter 26.07.2026) ───────────────────────────
        // Wenn länger als warn_days (Default 1 = 24 h) nichts in audit_log
        // landet → kritische Admin-Warnung (DashboardController filtert
        // Nicht-Admins). Verhindert den nächsten «stummen» Zeitraum wie 23.–26.07.
        if (Enabled("audit_log_stumm"))
        {
            var silenceDays = Math.Max(1, WarnDays("audit_log_stumm", AuditLogHealth.DefaultSilenceDays));
            var health = await AuditLogHealth.CheckAsync(_db, silenceDays);
            if (!health.Ok)
            {
                var lastTxt = health.LastCreatedAt.HasValue
                    ? health.LastCreatedAt.Value.ToString("dd.MM.yyyy HH:mm")
                    : "nie";
                var silentDaysShown = health.LastCreatedAt.HasValue
                    ? Math.Max(1, (int)Math.Floor(health.SilentHours / 24.0))
                    : silenceDays;
                alerts.Add(new DashboardAlert
                {
                    Category = "audit_log_stumm",
                    Severity = SeverityState("audit_log_stumm", "critical"),
                    Title    = "Aktivitäts-Log schreibt nicht mehr",
                    TitleKey = "alert.audit.silent",
                    Subtitle = $"Letzter Eintrag: {lastTxt} · seit {silentDaysShown} Tag(en) keine Protokollierung",
                    SubtitleKey  = "alert.audit.silent_sub",
                    SubtitleArgs = new Dictionary<string, object> {
                        ["last"] = lastTxt,
                        ["days"] = silentDaysShown
                    },
                    DueDate  = health.LastCreatedAt,
                    DaysUntil = health.LastCreatedAt.HasValue ? -silentDaysShown : null
                });
            }
        }

        // ── Globale Austritts-Bedingung (Walter-Vorgabe 21.06.2026) ──
        // Dieselbe Regel wie bei der Nachtarbeit-Untersuchung auf ALLE
        // MA-bezogenen Warnungen anwenden: MA, deren Austritt ≤ heute + 30 Tage
        // liegt, werden nicht mehr gemeldet (MA ohne Austrittsdatum bleiben).
        // AUSNAHME: die Karten, die GENAU vom Austritt/Vertragsende handeln —
        // die sollen ja gerade erscheinen.
        {
            var keepCats = new HashSet<string> { "contract_end", "exit_pending_active", "kuendigung_ablauf", "kuendigung_sperrfrist_ende" };
            var filterIds = alerts
                .Where(a => a.EmployeeId.HasValue && !keepCats.Contains(a.Category))
                .Select(a => a.EmployeeId!.Value).Distinct().ToList();
            if (filterIds.Count > 0)
            {
                var cutoff = today.AddDays(30);
                var exits = await _db.Employees.AsNoTracking()
                    .Where(e => filterIds.Contains(e.Id) && e.ExitDate != null)
                    .Select(e => new { e.Id, e.ExitDate })
                    .ToListAsync();
                var soonLeavers = exits
                    .Where(x => DateOnly.FromDateTime(x.ExitDate!.Value) <= cutoff)
                    .Select(x => x.Id).ToHashSet();
                if (soonLeavers.Count > 0)
                    alerts.RemoveAll(a => a.EmployeeId.HasValue
                                       && !keepCats.Contains(a.Category)
                                       && soonLeavers.Contains(a.EmployeeId.Value));
            }
        }

        // Konfig auf jede Alert-Zeile stempeln (Priorität + Warnfarbe).
        foreach (var a in alerts)
        {
            if (warnCfg.TryGetValue(a.Category, out var cfg))
            {
                a.TodoPriority = cfg.TodoPriority;
                a.WarnColor    = string.IsNullOrWhiteSpace(cfg.WarnColor) ? "none" : cfg.WarnColor;
            }
        }

        // Sortieren (Walter 19.07.2026): konfigurierbare TodoPriority, dann
        // DaysUntil (stärker überfällig zuerst; null zuletzt), dann Name.
        // Mindestlohn-OK bleibt ganz unten in der eigenen Kategorie-Gruppe.
        alerts = alerts
            .OrderBy(a => a.Category == "minimum_wage_ok" ? 1 : 0)
            .ThenBy(a => a.TodoPriority)
            .ThenBy(a => a.DaysUntil ?? int.MaxValue)
            .ThenBy(a => a.EmployeeName ?? a.Title ?? "", StringComparer.OrdinalIgnoreCase)
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

    /// <summary>Wochentag kurz + Datum für Probezeit-Todos, z.B. «Fr, 31.07.2026»
    /// (Walter 26.07.2026: nicht «Freitag» — spart Platz in der To-do-Zeile).</summary>
    private static string FormatWeekdayDateDe(DateTime d)
    {
        var cult = CultureInfo.GetCultureInfo("de-CH");
        var wd = d.ToString("ddd", cult).TrimEnd('.');
        return wd + ", " + d.ToString("dd.MM.yyyy", cult);
    }
}
