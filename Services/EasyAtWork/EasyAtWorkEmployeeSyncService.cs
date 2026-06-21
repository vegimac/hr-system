using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Holt Mitarbeiter-Stammdaten aus easy@work und legt sie in
/// <c>employee</c> an / aktualisiert sie. Zwei Modi:
///   - <see cref="PreviewAsync"/> — read-only Diff.
///   - <see cref="CommitAsync"/>  — INSERT/UPDATE.
///
/// Match-Schlüssel: <c>employee_number</c>. Pro Match werden Feld-Diffs
/// gerechnet. Updates werden nur dann angewendet, wenn easy@work einen
/// NICHT-leeren Wert liefert (so wischen wir nicht versehentlich Cowork-
/// Daten weg, die in easy@work fehlen).
///
/// Selektive Übernahme: der Commit-Aufrufer übergibt die <c>employee_number</c>s,
/// für die geschrieben werden soll. So kann Walter auf Vorschau-Ebene pro
/// MA bestätigen.
/// </summary>
public class EasyAtWorkEmployeeSyncService
{
    private readonly AppDbContext _db;
    private readonly EasyAtWorkClient _client;
    private readonly ILogger<EasyAtWorkEmployeeSyncService> _log;

    public EasyAtWorkEmployeeSyncService(
        AppDbContext db,
        EasyAtWorkClient client,
        ILogger<EasyAtWorkEmployeeSyncService> log)
    {
        _db = db;
        _client = client;
        _log = log;
    }

    // ─────────────────────────── DTOs ───────────────────────────────

    public class SyncRequest
    {
        public int CompanyProfileId { get; set; }
        public DateOnly? ActiveAt { get; set; }   // Default = heute
        /// <summary>
        /// Wenn gesetzt: holt zusätzlich zu den aktiven MA auch alle, deren
        /// Austrittsdatum NACH diesem Stichtag liegt.
        /// </summary>
        public DateOnly? ExitedAfter { get; set; }
        /// <summary>
        /// Wenn true: holt ALLE MA inkl. aller jemals ausgetretener. Überschreibt
        /// <see cref="ExitedAfter"/> und <see cref="ActiveAt"/>. Walter-Vorgabe
        /// 17.06.2026 für den Initial-Import einer Filiale.
        /// </summary>
        public bool IncludeAllInactive { get; set; } = false;
        /// <summary>
        /// Walter-Vorgabe 19.06.2026: einziger Modus-Schalter. true = nur am
        /// Stichtag aktive MA; false = ALLE (inkl. ausgetretene), aber OHNE die
        /// Pre-Mirus-Austritte vor dem 1.1.2025 (gleicher Filter wie der
        /// Stempelzeiten-Sync, <see cref="EasyAtWorkTimepunchSyncService.FilterRelevantEmployees"/>).
        /// Ersetzt das frühere „Austritt nach"-Datumsfeld.
        /// </summary>
        public bool OnlyActive { get; set; } = false;
        /// <summary>Beim Commit: nur diese Personalnummern schreiben (NULL = alle NEW+UPDATE).</summary>
        public List<string>? SelectedNumbers { get; set; }

        /// <summary>
        /// Walter-Vorgabe 21.06.2026 (einmaliger Tief-Import): wenn gesetzt, wird
        /// dieser Stichtag statt dem Standard 1.1.2025 für den Pre-Mirus-Filter
        /// verwendet — so kommen auch ausgetretene MA bis z.B. 1.1.2021 mit.
        /// Wirkt nur bei OnlyActive=false.
        /// </summary>
        public DateOnly? EmployeeCutoffOverride { get; set; }

        /// <summary>
        /// Walter-Vorgabe 21.06.2026: MA mit Austritt VOR der Mirus-Grenze
        /// (1.1.2025) bekommen den Suffix „alt" an die Personalnummer
        /// (z.B. 58001 → 58001alt), damit sie nicht mit den aktuellen Mirus-
        /// Nummern kollidieren. Gilt für Matching UND Neuanlage.
        /// </summary>
        public bool AltSuffixForPreMirusExits { get; set; } = false;
    }

    public class FieldDiff
    {
        public string Field { get; set; } = "";
        public string? Cowork  { get; set; }
        public string? Easy    { get; set; }
        public bool   WillSet { get; set; }
    }

    public class EmployeePreviewRow
    {
        public int      EawEmployeeId    { get; set; }
        public string?  Number           { get; set; }
        public string?  FirstName        { get; set; }
        public string?  LastName         { get; set; }
        public int?     CoworkEmployeeId { get; set; }
        /// <summary>NEW / UPDATE / UNCHANGED / CONFLICT</summary>
        public string   Status           { get; set; } = "NEW";
        public string?  Reason           { get; set; }
        public List<FieldDiff> Diffs     { get; set; } = new();

        // Anzeige beim 2021-Massenimport (Walter-Vorgabe 21.06.2026):
        /// <summary>Gefüllt, wenn über eine ALTE Personalnummer gematcht wurde (die Alt-Nr.).</summary>
        public string?  MatchedViaAltNumber       { get; set; }
        /// <summary>„wird angelegt" / „wird nachgeholt" / „existiert" — nur beim Massenimport.</summary>
        public string?  EmploymentInfo            { get; set; }
        public int?     AssignedCompanyProfileId  { get; set; }
        public string?  AssignedBranchName        { get; set; }
    }

    public class SyncResult
    {
        public bool    IsPreview         { get; set; }
        public int     CountTotal        { get; set; }
        public int     CountNew          { get; set; }
        public int     CountUpdate       { get; set; }
        public int     CountUnchanged    { get; set; }
        public int     CountConflict     { get; set; }
        public int     CountInserted     { get; set; }
        public int     CountUpdated      { get; set; }
        public List<EmployeePreviewRow> Rows { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    // ────────────────────────── Public API ──────────────────────────

    public Task<SyncResult> PreviewAsync(SyncRequest req, CancellationToken ct = default)
        => SyncCoreAsync(req, commit: false, ct);

    public Task<SyncResult> CommitAsync(SyncRequest req, CancellationToken ct = default)
        => SyncCoreAsync(req, commit: true, ct);

    // ─────────────────────────── Core ───────────────────────────────

    private async Task<SyncResult> SyncCoreAsync(SyncRequest req, bool commit, CancellationToken ct)
    {
        var res = new SyncResult { IsPreview = !commit };

        // 1) Mapping
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == req.CompanyProfileId, ct);
        if (mapping == null)
        {
            res.Notes.Add("Filiale hat kein easy@work-Mapping.");
            return res;
        }

        var activeAt = req.ActiveAt ?? DateOnly.FromDateTime(DateTime.UtcNow.Date);
        // Pre-Mirus-Stichtag (Standard 1.1.2025) — beim Tief-Import überschreibbar.
        var maCutoff      = req.EmployeeCutoffOverride ?? EasyAtWorkTimepunchSyncService.EmployeeCutoff;
        var mirusCutoff   = EasyAtWorkTimepunchSyncService.EmployeeCutoff;   // 1.1.2025 — Grenze für den alt-Suffix

        // 2) easy@work-MA laden (Walter-Vorgabe 19.06.2026, ein Schalter):
        //    - OnlyActive=true  → nur am Stichtag aktive MA
        //    - OnlyActive=false → ALLE inkl. ausgetretene, ABER ohne die
        //      Pre-Mirus-Austritte vor 1.1.2025 (FilterRelevantEmployees).
        List<EawEmployee> eawEmps;
        try
        {
            if (req.OnlyActive)
            {
                eawEmps = await _client.GetAllEmployeesActiveAtAsync(mapping.EasyAtWorkCustomerId, activeAt, ct);
                res.Notes.Add($"{eawEmps.Count} aktive MA.");
            }
            else
            {
                var all = await _client.GetAllEmployeesIncludingInactiveAsync(mapping.EasyAtWorkCustomerId, ct);
                eawEmps = EasyAtWorkTimepunchSyncService.FilterRelevantEmployees(all, maCutoff);
                res.Notes.Add($"{all.Count} MA insgesamt, {eawEmps.Count} nach Filter (aktive + Austritt ab {maCutoff:dd.MM.yyyy}).");
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "easy@work GET employees fehlgeschlagen");
            res.Notes.Add($"easy@work-Aufruf fehlgeschlagen: {ex.Message}");
            return res;
        }

        // 3) Bestehende Cowork-MA per Personalnummer (alle Filialen — Schaub ist EIN AHV-Arbeitgeber)
        var coworkAll = await _db.Employees
            .Where(e => !e.IsHidden)
            .ToListAsync(ct);
        // Match-Dictionary: aktuelle Personalnummer UND alte/zweite Nummern
        // (employee_number_alt1/alt2) als Lookup-Keys (Walter-Vorgabe 21.06.2026),
        // damit ein MA auch gefunden wird, wenn easy@work ihn unter einer alten
        // Nummer führt. Erster Eintrag gewinnt (TryAdd).
        var byNumber = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in coworkAll)
        {
            if (!string.IsNullOrWhiteSpace(e.EmployeeNumber))     byNumber.TryAdd(e.EmployeeNumber.Trim(), e);
            if (!string.IsNullOrWhiteSpace(e.EmployeeNumberAlt1)) byNumber.TryAdd(e.EmployeeNumberAlt1.Trim(), e);
            if (!string.IsNullOrWhiteSpace(e.EmployeeNumberAlt2)) byNumber.TryAdd(e.EmployeeNumberAlt2.Trim(), e);
        }

        // Nationality-Lookup (ISO-Code → Id)
        var natByCode = await _db.Nationalities.AsNoTracking()
            .ToDictionaryAsync(n => (n.Code ?? "").ToUpperInvariant(), n => n.Id, ct);

        var selected = req.SelectedNumbers != null
            ? new HashSet<string>(req.SelectedNumbers, StringComparer.OrdinalIgnoreCase)
            : null;

        // Filialname für die Vorschau-Anzeige (nur beim Massenimport relevant).
        var assignBranchName = await _db.CompanyProfiles
            .Where(c => c.Id == req.CompanyProfileId)
            .Select(c => string.IsNullOrWhiteSpace(c.BranchName) ? c.CompanyName : c.BranchName)
            .FirstOrDefaultAsync(ct);

        foreach (var eaw in eawEmps)
        {
            // Pre-Mirus-Austritt (vor 1.1.2025) → Personalnummer mit „alt"-Suffix,
            // damit sie nicht mit der aktuellen Mirus-Nummer kollidiert. Greift
            // für Matching UND Neuanlage (Walter-Vorgabe 21.06.2026).
            var rawNumber     = (eaw.Number ?? "").Trim();
            var preMirusExit  = eaw.To.HasValue && eaw.To.Value < mirusCutoff;
            var effNumber     = (req.AltSuffixForPreMirusExits && preMirusExit && rawNumber.Length > 0)
                                ? rawNumber + "alt" : rawNumber;

            var row = new EmployeePreviewRow
            {
                EawEmployeeId = eaw.Id,
                Number        = effNumber,
                FirstName     = eaw.FirstName,
                LastName      = eaw.LastName,
            };

            if (string.IsNullOrWhiteSpace(row.Number))
            {
                row.Status = "CONFLICT";
                row.Reason = "easy@work-MA hat keine Personalnummer — nicht eindeutig zuordenbar.";
                res.Rows.Add(row); res.CountConflict++;
                continue;
            }

            // Match: erst über die (ggf. alt-suffigierte) effektive Nummer, dann
            // über die rohe easy@work-Nummer — beide auch gegen die Alt-Nummern-
            // Keys (employee_number_alt1/alt2). Walter-Vorgabe 21.06.2026.
            Employee? co = null;
            string? matchedKey = null;
            if (byNumber.TryGetValue(row.Number, out co)) matchedKey = row.Number;
            else if (!string.Equals(effNumber, rawNumber, StringComparison.OrdinalIgnoreCase)
                     && byNumber.TryGetValue(rawNumber, out co)) matchedKey = rawNumber;
            row.CoworkEmployeeId = co?.Id;
            // Über eine ALTE Nummer gematcht? (matchender Key ≠ aktuelle Personalnr.)
            if (co != null && matchedKey != null
                && !string.Equals(co.EmployeeNumber?.Trim(), matchedKey, StringComparison.OrdinalIgnoreCase))
                row.MatchedViaAltNumber = matchedKey;

            // Diffs berechnen (auch für NEW — dann sind alle Cowork-Werte leer)
            var diffs = ComputeDiffs(co, eaw, natByCode);
            row.Diffs = diffs;

            if (co == null)
            {
                row.Status = "NEW";
                res.CountNew++;
            }
            else if (diffs.Any(d => d.WillSet))
            {
                row.Status = "UPDATE";
                res.CountUpdate++;
            }
            else
            {
                row.Status = "UNCHANGED";
                res.CountUnchanged++;
            }

            // Employment-Vorschau: wird angelegt / nachgeholt / existiert + Filiale.
            row.AssignedCompanyProfileId = req.CompanyProfileId;
            row.AssignedBranchName       = assignBranchName;
            if (co == null)
                row.EmploymentInfo = "wird angelegt";
            else
            {
                var hasEmp = await _db.Employments
                    .AnyAsync(em => em.EmployeeId == co.Id && em.CompanyProfileId == req.CompanyProfileId, ct);
                row.EmploymentInfo = hasEmp ? "existiert" : "wird nachgeholt";
            }
            res.Rows.Add(row);
        }
        res.CountTotal = res.Rows.Count;

        // 4) Commit-Pfad
        if (commit)
        {
            // Silent Backfill: ALLE gematchten MA (auch UNCHANGED) bekommen die
            // easyatwork_employee_id wenn sie fehlt. Das ist nicht-destruktiv
            // (nur null → Wert) und ohne diese ID lässt sich `edited_by_id` aus
            // Stempel-Audits nicht zum Manager auflösen. Walter 17.06.2026.
            int backfilled = 0;
            foreach (var row in res.Rows.Where(r => r.CoworkEmployeeId.HasValue))
            {
                var eaw = eawEmps.FirstOrDefault(e => e.Id == row.EawEmployeeId);
                if (eaw == null) continue;
                var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == row.CoworkEmployeeId, ct);
                if (emp == null) continue;
                var newId = eaw.UserId ?? eaw.Id;
                if (emp.EasyAtWorkEmployeeId != newId)
                {
                    emp.EasyAtWorkEmployeeId = newId;
                    backfilled++;
                }
            }
            if (backfilled > 0)
            {
                await _db.SaveChangesAsync(ct);
                res.Notes.Add($"easy@work-ID stillschweigend bei {backfilled} bestehenden MA nachgetragen.");
            }

            foreach (var row in res.Rows)
            {
                if (row.Status != "NEW" && row.Status != "UPDATE") continue;
                if (selected != null && !selected.Contains(row.Number ?? "")) continue;

                if (row.Status == "NEW")
                {
                    var eaw = eawEmps.FirstOrDefault(e => e.Id == row.EawEmployeeId);
                    if (eaw == null) continue;
                    var emp = new Employee
                    {
                        EmployeeNumber       = row.Number ?? "",
                        FirstName            = eaw.FirstName ?? "",
                        LastName             = eaw.LastName ?? "",
                        // Bereits ausgetretene MA werden als inaktiv angelegt
                        // (Walter-Vorgabe 21.06.2026 — Tief-Import inaktiver MA).
                        IsActive             = !(eaw.To.HasValue && eaw.To.Value < activeAt),
                        ExitDate             = eaw.To?.ToDateTime(TimeOnly.MinValue),
                        // Wir speichern primär die user_id (auf die edited_by_id zeigt),
                        // mit Fallback auf die Employee-Id. Walter 17.06.2026.
                        EasyAtWorkEmployeeId = eaw.UserId ?? eaw.Id,
                    };
                    ApplyDiffs(emp, row.Diffs, eaw, natByCode);
                    _db.Employees.Add(emp);
                    res.CountInserted++;
                    // Jeder neue MA bekommt eine Employment-Zeile (Filiale + Dates +
                    // UTP-Default), inaktive MA als inaktive Zeile (Walter-Vorgabe
                    // 21.06.2026). emp.Id ist hier noch 0 → EF-Navigation.
                    await EnsureEmploymentAsync(emp, eaw, req.CompanyProfileId, mapping.EasyAtWorkCustomerId, isNewEmployee: true, ct);
                }
                else // UPDATE
                {
                    if (row.CoworkEmployeeId == null) continue;
                    var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == row.CoworkEmployeeId, ct);
                    if (emp == null) continue;
                    var eaw = eawEmps.FirstOrDefault(e => e.Id == row.EawEmployeeId);
                    if (eaw == null) continue;
                    var newEawId = eaw.UserId ?? eaw.Id;
                    if (emp.EasyAtWorkEmployeeId != newEawId) emp.EasyAtWorkEmployeeId = newEawId;
                    ApplyDiffs(emp, row.Diffs, eaw, natByCode);
                    // Inaktiver MA → is_active=false + Austrittsdatum. Und ein
                    // Employment NACHHOLEN, falls für (MA, Filiale) noch keines
                    // existiert (idempotent, kein Duplikat bei Re-Import).
                    if (eaw.To.HasValue && eaw.To.Value < activeAt)
                    {
                        emp.IsActive = false;
                        emp.ExitDate = eaw.To.Value.ToDateTime(TimeOnly.MinValue);
                    }
                    await EnsureEmploymentAsync(emp, eaw, req.CompanyProfileId, mapping.EasyAtWorkCustomerId, isNewEmployee: false, ct);
                    res.CountUpdated++;
                }
            }
            await _db.SaveChangesAsync(ct);

            // Sync-State
            var st = await _db.EasyAtWorkSyncStates
                .FirstOrDefaultAsync(s => s.CompanyProfileId == req.CompanyProfileId && s.Resource == "EMPLOYEE", ct);
            if (st == null) { st = new EasyAtWorkSyncState { CompanyProfileId = req.CompanyProfileId, Resource = "EMPLOYEE" }; _db.EasyAtWorkSyncStates.Add(st); }
            st.LastSyncAt = DateTime.UtcNow;
            st.LastRowCount = res.CountInserted + res.CountUpdated;
            st.LastError = null;
            await _db.SaveChangesAsync(ct);
        }
        return res;
    }

    // ───────────── Inaktive Employment-Zeile (Walter 21.06.2026) ─────────────

    /// <summary>Best-effort gemappte Vertrags-Infos aus easy@work (Verträge + Pay-Rates).</summary>
    private sealed class HistContractInfo
    {
        public DateTime? StartDate;            // = frühestes Pay-Rate-From (Lohn-Beginn)
        public string?   EmploymentModel;      // FIX / MTP / UTP
        public string?   SalaryType;           // monthly / hourly
        public string?   ContractType;
        public string?   JobTitle;
        public decimal?  WeeklyHours;
        public decimal?  EmploymentPercentage;
        public decimal?  HourlyRate;
        public decimal?  MonthlySalary;
    }

    /// <summary>
    /// Holt — soweit verfügbar — Vertrags-/Lohnstufen-Infos eines easy@work-MA und
    /// mappt sie auf unsere Felder. Vollständig best-effort: jeder Fehlschlag lässt
    /// das jeweilige Feld leer, der Import läuft trotzdem durch. Mapping analog
    /// CLAUDE.md: amount_type month → FIX; hour + Type MTP/TPM → MTP; hour sonst → UTP.
    /// </summary>
    private async Task<HistContractInfo> BuildHistContractInfoAsync(int customerId, int eawEmployeeId, CancellationToken ct)
    {
        var info = new HistContractInfo();
        try
        {
            var contracts = (await _client.GetContractsAsync(customerId, eawEmployeeId, ct))?.Data ?? new();
            var latest = contracts
                .OrderByDescending(c => c.From ?? DateOnly.MinValue)
                .ThenByDescending(c => c.UpdatedAt ?? DateTime.MinValue)
                .FirstOrDefault();
            if (latest != null)
            {
                info.ContractType         = string.IsNullOrWhiteSpace(latest.Type)  ? null : latest.Type!.Trim();
                info.JobTitle             = string.IsNullOrWhiteSpace(latest.Title) ? null : latest.Title!.Trim();
                info.WeeklyHours          = latest.WeekHours;
                info.EmploymentPercentage = latest.Percentage;
                var amt = (latest.AmountType ?? "").Trim().ToLowerInvariant();
                var typ = (latest.Type ?? "").Trim().ToUpperInvariant();
                if (amt == "month") { info.EmploymentModel = "FIX"; info.SalaryType = "monthly"; }
                else if (amt == "hour")
                {
                    info.EmploymentModel = (typ.Contains("MTP") || typ.Contains("TPM")) ? "MTP" : "UTP";
                    info.SalaryType = "hourly";
                }
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "Verträge für easy@work-MA {Id} nicht abrufbar", eawEmployeeId); }

        try
        {
            var rates = (await _client.GetPayRatesAsync(customerId, eawEmployeeId, ct))?.Data ?? new();
            if (rates.Count > 0)
            {
                info.StartDate = rates.Where(r => r.From.HasValue).OrderBy(r => r.From)
                    .Select(r => r.From!.Value.ToDateTime(TimeOnly.MinValue)).Cast<DateTime?>().FirstOrDefault();
                decimal? LatestRate(string t) => rates
                    .Where(r => (r.Type ?? "").Equals(t, StringComparison.OrdinalIgnoreCase) && r.Rate.HasValue)
                    .OrderByDescending(r => r.From ?? DateOnly.MinValue).Select(r => r.Rate).FirstOrDefault();
                info.HourlyRate    = LatestRate("hourly");
                info.MonthlySalary = LatestRate("monthly") ?? LatestRate("fte");
            }
        }
        catch (Exception ex) { _log.LogDebug(ex, "Pay-Rates für easy@work-MA {Id} nicht abrufbar", eawEmployeeId); }

        return info;
    }

    /// <summary>
    /// Stellt für einen importierten MA eine Employment-Zeile sicher (Walter-
    /// Vorgabe 21.06.2026). NEW-MA: immer anlegen. UPDATE-MA: nur NACHHOLEN, wenn
    /// für (MA, Filiale) noch KEIN Employment existiert (Backfill, idempotent).
    /// Felder: Filiale + Start/Ende aus EntryDate/ExitDate (Fallback Von/Bis),
    /// IsActive vom MA, Modell/Lohn/Funktion soweit easy@work liefert, sonst
    /// UTP-Default (Stundenlohn = häufigstes Crew-Modell).
    /// </summary>
    private async Task EnsureEmploymentAsync(
        Employee emp, EawEmployee eaw, int companyProfileId, int customerId, bool isNewEmployee, CancellationToken ct)
    {
        // UPDATE-MA: existiert schon ein Employment für (MA, Filiale)? Dann früh
        // raus — KEIN unnötiger easy@work-Vertrags-/Pay-Rate-Abruf.
        if (!isNewEmployee && emp.Id != 0)
        {
            var has = await _db.Employments
                .AnyAsync(em => em.EmployeeId == emp.Id && em.CompanyProfileId == companyProfileId, ct);
            if (has) return;
        }

        var info = await BuildHistContractInfoAsync(customerId, eaw.Id, ct);

        // contract_start_date = EntryDate → Von (eaw.From) → Pay-Rate-From → ExitDate → heute.
        var startDate = emp.EntryDate
                        ?? eaw.From?.ToDateTime(TimeOnly.MinValue)
                        ?? info.StartDate
                        ?? emp.ExitDate
                        ?? DateTime.UtcNow.Date;
        // contract_end_date = ExitDate (Austritt) → Bis.
        var endDate = emp.ExitDate ?? eaw.To?.ToDateTime(TimeOnly.MinValue);

        await AddEmploymentIfMissingAsync(
            _db, emp, companyProfileId, isNewEmployee,
            startDate, endDate, emp.IsActive,
            info.EmploymentModel, info.SalaryType, info.ContractType, info.JobTitle,
            info.WeeklyHours, info.EmploymentPercentage, info.HourlyRate, info.MonthlySalary, ct);
    }

    /// <summary>
    /// Reiner DB-Schreiber (ohne API) — separat, damit unit-testbar. Legt eine
    /// Employment-Zeile an. Bei <paramref name="isNewEmployee"/>=false (UPDATE)
    /// wird ZUERST geprüft, ob für (MA, Filiale) schon ein Employment existiert —
    /// falls ja, passiert NICHTS (kein Duplikat bei Re-Import). Modell/SalaryType
    /// defaulten auf UTP/hourly, wenn leer. Gibt true zurück, wenn angelegt wurde.
    /// </summary>
    public static async Task<bool> AddEmploymentIfMissingAsync(
        AppDbContext db, Employee emp, int companyProfileId, bool isNewEmployee,
        DateTime startDate, DateTime? endDate, bool isActive,
        string? employmentModel, string? salaryType, string? contractType, string? jobTitle,
        decimal? weeklyHours, decimal? percentage, decimal? hourlyRate, decimal? monthlySalary,
        CancellationToken ct = default)
    {
        if (!isNewEmployee && emp.Id != 0)
        {
            var has = await db.Employments
                .AnyAsync(em => em.EmployeeId == emp.Id && em.CompanyProfileId == companyProfileId, ct);
            if (has) return false;   // schon vorhanden → nichts tun (Backfill nur bei Lücke)
        }

        db.Employments.Add(new Employment
        {
            Employee             = emp,
            EmployeeId           = emp.Id,
            CompanyProfileId     = companyProfileId,
            ContractStartDate    = startDate,
            ContractEndDate      = endDate,
            IsActive             = isActive,
            EmploymentModel      = string.IsNullOrWhiteSpace(employmentModel) ? "UTP"    : employmentModel!.Trim(),
            SalaryType           = string.IsNullOrWhiteSpace(salaryType)      ? "hourly" : salaryType!.Trim(),
            ContractType         = contractType,
            JobTitle             = jobTitle,
            WeeklyHours          = weeklyHours,
            EmploymentPercentage = percentage,
            HourlyRate           = hourlyRate,
            MonthlySalary        = monthlySalary,
        });
        return true;
    }

    // ─────────────────────────── Diff-Logik ─────────────────────────

    private static List<FieldDiff> ComputeDiffs(Employee? co, EawEmployee eaw, Dictionary<string, int> natByCode)
    {
        var diffs = new List<FieldDiff>();

        void Add(string field, string? cur, string? eawVal)
        {
            var trimEaw = string.IsNullOrWhiteSpace(eawVal) ? null : eawVal.Trim();
            var trimCur = string.IsNullOrWhiteSpace(cur)    ? null : cur.Trim();
            // Nur setzen, wenn easy@work einen NICHT-leeren Wert hat UND sich unterscheidet.
            var willSet = trimEaw != null && !string.Equals(trimEaw, trimCur, StringComparison.OrdinalIgnoreCase);
            diffs.Add(new FieldDiff { Field = field, Cowork = trimCur, Easy = trimEaw, WillSet = willSet });
        }

        Add("Vorname",     co?.FirstName,   eaw.FirstName);
        Add("Nachname",    co?.LastName,    eaw.LastName);
        Add("Anrede",      co?.Salutation,  SalutationFromGender(eaw.Gender));
        Add("Geburtstag",  co?.DateOfBirth?.ToString("yyyy-MM-dd"),
                            eaw.BirthDate?.ToString("yyyy-MM-dd"));
        var (street, hno) = SplitStreetHouse(eaw.Address1);
        Add("Strasse",     co?.Street,      street);
        Add("Hausnr.",     co?.HouseNumber, hno);
        Add("PLZ",         co?.ZipCode,     eaw.PostalCode);
        Add("Ort",         co?.City,        eaw.City);
        Add("Land",        co?.Country,     (eaw.CountryKey ?? eaw.Country)?.ToUpperInvariant());
        Add("Nationalität",
            co?.Nationality,
            ResolveNationalityCode(eaw.Nationality, natByCode));
        Add("Telefon",     co?.PhoneMobile, NormalizePhone(eaw.Phone));
        Add("E-Mail",      co?.Email,       eaw.Email?.ToLowerInvariant());
        Add("Eintritt",    co?.EntryDate?.ToString("yyyy-MM-dd"),
                            eaw.From?.ToString("yyyy-MM-dd"));
        // Austritt nur setzen, wenn easy@work einen liefert (sonst überschreiben wir Cowork-Austritte).
        Add("Austritt",    co?.ExitDate?.ToString("yyyy-MM-dd"),
                            eaw.To?.ToString("yyyy-MM-dd"));
        return diffs;
    }

    private static void ApplyDiffs(Employee emp, List<FieldDiff> diffs, EawEmployee eaw, Dictionary<string, int> natByCode)
    {
        foreach (var d in diffs.Where(x => x.WillSet))
        {
            switch (d.Field)
            {
                case "Vorname":      emp.FirstName    = d.Easy ?? ""; break;
                case "Nachname":     emp.LastName     = d.Easy ?? ""; break;
                case "Anrede":       emp.Salutation   = d.Easy; break;
                case "Geburtstag":   emp.DateOfBirth  = DateTime.TryParse(d.Easy, out var dob) ? dob : emp.DateOfBirth; break;
                case "Strasse":      emp.Street       = d.Easy; break;
                case "Hausnr.":      emp.HouseNumber  = d.Easy; break;
                case "PLZ":          emp.ZipCode      = d.Easy; break;
                case "Ort":          emp.City         = d.Easy; break;
                case "Land":         emp.Country      = d.Easy; break;
                case "Nationalität":
                    emp.Nationality = d.Easy;
                    if (d.Easy != null && natByCode.TryGetValue(d.Easy.ToUpperInvariant(), out var nid))
                        emp.NationalityId = nid;
                    break;
                case "Telefon":      emp.PhoneMobile  = d.Easy; break;
                case "E-Mail":       emp.Email        = d.Easy; break;
                case "Eintritt":     emp.EntryDate    = DateTime.TryParse(d.Easy, out var ed) ? ed : emp.EntryDate; break;
                case "Austritt":     emp.ExitDate     = DateTime.TryParse(d.Easy, out var xd) ? xd : emp.ExitDate; break;
            }
        }
    }

    // ─────────────────────────── Helpers ────────────────────────────

    private static string? SalutationFromGender(string? g)
    {
        if (string.IsNullOrWhiteSpace(g)) return null;
        return g.Trim().ToLowerInvariant() switch
        {
            "male" or "m" or "herr"   => "Herr",
            "female" or "f" or "frau" => "Frau",
            _                          => null
        };
    }

    private static (string? street, string? hno) SplitStreetHouse(string? addr)
    {
        if (string.IsNullOrWhiteSpace(addr)) return (null, null);
        var s = addr.Trim();
        // letzte Zahl(en)+optionaler Buchstabe als Hausnummer
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^(.+?)\s+(\d+[a-zA-Z]?)$");
        if (m.Success) return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim());
        return (s, null);
    }

    private static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return null;
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.StartsWith("0"))  digits = "41" + digits[1..];
        if (!digits.StartsWith("41") && digits.Length == 9) digits = "41" + digits;
        // Format: +41 79 333 44 55
        if (digits.Length >= 11 && digits.StartsWith("41"))
        {
            var cc  = "+41";
            var rest = digits[2..];
            if (rest.Length >= 9)
                return $"{cc} {rest[..2]} {rest[2..5]} {rest[5..7]} {rest[7..9]}";
        }
        return phone.Trim();
    }

    private static string? ResolveNationalityCode(string? nat, Dictionary<string, int> natByCode)
    {
        if (string.IsNullOrWhiteSpace(nat)) return null;
        var s = nat.Trim().ToUpperInvariant();
        // Wenn easy@work bereits ISO-Code liefert
        if (s.Length == 2 && natByCode.ContainsKey(s)) return s;
        // Volltext-Mapping wäre ausführlicher — für jetzt geben wir den Rohwert zurück
        // (landet in Employee.Nationality als Freitext-Backup; NationalityId bleibt null).
        return nat.Trim();
    }
}
