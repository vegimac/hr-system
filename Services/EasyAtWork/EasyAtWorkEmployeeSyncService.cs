using System.Collections.Concurrent;
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

        /// <summary>
        /// Walter-Vorgabe 21.06.2026 (Performance): überspringt die langsamen
        /// Detail-API-Calls (Verträge, Pay-Rates, Zivilstand-Properties). Der
        /// Import läuft dann nur mit den Basis-Daten aus dem Massen-Endpoint
        /// (Name, Nummer, Eintritt, Austritt, Nationalität) — für den Tief-Import
        /// alter MA (nur für Stempelzeiten) ausreichend. Employment wird mit
        /// UTP-Default angelegt, Zivilstand bleibt leer.
        /// </summary>
        public bool SkipDetailCalls { get; set; } = false;
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
        /// <summary>Personalnummern-Wechsel erkannt: alte → neue Nummer (alte wird in Alt1 gesichert).</summary>
        public string?  NumberChangeFrom          { get; set; }
        public string?  NumberChangeTo            { get; set; }

        // Möglicher Wiedereintritt (Walter-Vorgabe 21.06.2026): gleicher Name +
        // Geburtsdatum wie ein bestehender MA, aber NEUE easy@work-ID. Vorschlag,
        // kein Auto-Merge — als Warnung in der Vorschau zeigen.
        public bool     PossibleReentry           { get; set; }
        public int?     ReentryEmployeeId         { get; set; }
        public string?  ReentryEmployeeNumber     { get; set; }
        public int?     ReentryNewEawId           { get; set; }   // die NEUE eaw-ID, die als Alias gesichert würde
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
        /// <summary>MA, die schon existierten (gleiche easy@work-ID) → kein neuer Employee.</summary>
        public int     CountExisting     { get; set; }
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
        // Alte Personalnummern aus der Alias-Tabelle (Walter-Vorgabe 21.06.2026).
        var aliases = await _db.EmployeeNumberAliases.AsNoTracking()
            .Select(a => new { a.Number, a.EmployeeId })
            .ToListAsync(ct);
        var coworkById = coworkAll.Where(e => e.Id > 0).ToDictionary(e => e.Id);
        // Match-Dictionary: aktuelle Personalnummer UND alle Alias-Nummern als
        // Lookup-Keys, damit ein MA auch unter einer alten Nummer gefunden wird.
        // Erster Eintrag gewinnt (TryAdd).
        var byNumber = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in coworkAll)
            if (!string.IsNullOrWhiteSpace(e.EmployeeNumber)) byNumber.TryAdd(e.EmployeeNumber.Trim(), e);
        foreach (var a in aliases)
            if (!string.IsNullOrWhiteSpace(a.Number) && coworkById.TryGetValue(a.EmployeeId, out var emp))
                byNumber.TryAdd(a.Number.Trim(), emp);
        // Alias-Nummern pro MA (für den Nummernwechsel-Guard).
        var aliasesByEmp = aliases
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Number).ToList());
        // Zusätzlich per hinterlegter easy@work-employee-id (Walter-Vorgabe
        // 21.06.2026): so wird ein MA auch dann gefunden, wenn easy@work eine
        // GANZ NEUE Personalnummer liefert (Wiedereintritt) — die weder als
        // aktuelle noch als Alt-Nummer bekannt ist. Match-Reihenfolge: Nummer
        // (inkl. Alt) zuerst, dann easy@work-ID.
        var byEawId = new Dictionary<int, Employee>();
        foreach (var e in coworkAll)
            if (e.EasyAtWorkEmployeeId.HasValue)
                byEawId.TryAdd(e.EasyAtWorkEmployeeId.Value, e);
        // Duplikat-Erkennung Stufe 2 (Walter-Vorgabe 21.06.2026): gleicher
        // Vorname+Nachname+Geburtsdatum = sehr wahrscheinlich dieselbe Person,
        // auch wenn easy@work eine neue ID vergeben hat (Wiedereintritt). Aus
        // coworkAll gebaut → KEINE Extra-DB-Abfrage pro Zeile.
        var byNameDob = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in coworkAll)
            if (e.DateOfBirth.HasValue)
                byNameDob.TryAdd(NameDobKey(e.FirstName, e.LastName, e.DateOfBirth.Value), e);

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
            // Keys (employee_number_alt1/alt2). Findet das nichts: über die
            // easy@work-employee-id (deckt einen Nummernwechsel ab — neue Nummer
            // ist nirgends bekannt). Walter-Vorgabe 21.06.2026.
            Employee? co = null;
            string? matchedKey = null;
            bool matchedByEawId = false;
            if (byNumber.TryGetValue(row.Number, out co)) matchedKey = row.Number;
            else if (!string.Equals(effNumber, rawNumber, StringComparison.OrdinalIgnoreCase)
                     && byNumber.TryGetValue(rawNumber, out co)) matchedKey = rawNumber;
            else if (byEawId.TryGetValue(eaw.UserId ?? eaw.Id, out co)) matchedByEawId = true;
            row.CoworkEmployeeId = co?.Id;
            // Über eine ALTE Nummer gematcht? (matchender Key ≠ aktuelle Personalnr.)
            if (co != null && matchedKey != null
                && !string.Equals(co.EmployeeNumber?.Trim(), matchedKey, StringComparison.OrdinalIgnoreCase))
                row.MatchedViaAltNumber = matchedKey;

            // Diffs berechnen (auch für NEW — dann sind alle Cowork-Werte leer)
            var diffs = ComputeDiffs(co, eaw, natByCode);
            row.Diffs = diffs;

            // ── Personalnummern-Wechsel (Walter-Vorgabe 21.06.2026) ────────────
            // Nur wenn über die easy@work-ID gematcht wurde (die neue Nummer ist
            // also weder aktuelle noch Alt-Nummer) UND sie sich tatsächlich von der
            // aktuellen unterscheidet UND noch nicht in alt1/alt2 steht (keine
            // Endlos-Rotation). Dann: Diff „Personalnummer" → wird beim Commit
            // rotiert (aktuelle → alt1, alt1 → alt2) und die neue Nr. gesetzt.
            if (matchedByEawId && co != null
                && ShouldSaveNumberChange(co.EmployeeNumber, rawNumber,
                       aliasesByEmp.TryGetValue(co.Id, out var coAliases) ? coAliases : null))
            {
                row.NumberChangeFrom = co.EmployeeNumber?.Trim();
                row.NumberChangeTo   = rawNumber;
                diffs.Add(new FieldDiff { Field = "Personalnummer", Cowork = row.NumberChangeFrom, Easy = rawNumber, WillSet = true });
            }

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

            // Duplikat-Erkennung Stufe 2 (Walter-Vorgabe 21.06.2026): NEW-Zeile,
            // aber gleicher Name+Geburtsdatum wie ein bestehender MA → möglicher
            // Wiedereintritt mit neuer easy@work-ID. NUR Warnung/Vorschlag — der
            // Commit merged nur, wenn die Zeile selektiert bleibt.
            if (co == null && eaw.BirthDate.HasValue
                && byNameDob.TryGetValue(NameDobKey(eaw.FirstName, eaw.LastName, eaw.BirthDate.Value), out var reentry))
            {
                row.PossibleReentry       = true;
                row.ReentryEmployeeId     = reentry.Id;
                row.ReentryEmployeeNumber = reentry.EmployeeNumber;
                row.ReentryNewEawId       = eaw.Id;
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

            // Zu schreibende Zeilen (NEW/UPDATE, ausgewählt).
            var rowsToProcess = res.Rows
                .Where(r => (r.Status == "NEW" || r.Status == "UPDATE")
                         && (selected == null || selected.Contains(r.Number ?? "")))
                .ToList();

            // Detail-Daten (Verträge/Pay-Rates/Zivilstand) PARALLEL vorladen (max. 10
            // gleichzeitig) statt 3 sequenzielle API-Calls pro MA. Diese Calls nutzen
            // NUR den HTTP-Client (nicht den DbContext) → thread-safe. Beim Schnell-
            // Import (SkipDetailCalls) ganz überspringen. Walter-Vorgabe 21.06.2026.
            var contractByEaw = new ConcurrentDictionary<int, HistContractInfo>();
            var maritalByEaw  = new ConcurrentDictionary<int, string?>();
            if (!req.SkipDetailCalls && rowsToProcess.Count > 0)
            {
                using var sem = new SemaphoreSlim(10);
                var detailTasks = rowsToProcess
                    .Select(r => r.EawEmployeeId).Distinct()
                    .Select(async eawId =>
                    {
                        await sem.WaitAsync(ct);
                        try
                        {
                            contractByEaw[eawId] = await BuildHistContractInfoAsync(mapping.EasyAtWorkCustomerId, eawId, ct);
                            maritalByEaw[eawId]  = await FetchMaritalStatusAsync(mapping.EasyAtWorkCustomerId, eawId, ct);
                        }
                        finally { sem.Release(); }
                    });
                await Task.WhenAll(detailTasks);
            }
            HistContractInfo InfoFor(int eawId) => contractByEaw.TryGetValue(eawId, out var i) ? i : new HistContractInfo();
            string? MaritalFor(int eawId)       => maritalByEaw.TryGetValue(eawId, out var m) ? m : null;

            foreach (var row in rowsToProcess)
            {
                var eaw = eawEmps.FirstOrDefault(e => e.Id == row.EawEmployeeId);
                if (eaw == null) continue;
                var info = InfoFor(row.EawEmployeeId);

                if (row.Status == "NEW")
                {
                    // Duplikat-Prävention Stufe 1 (Walter-Vorgabe 21.06.2026): existiert
                    // schon ein Employee mit dieser easy@work-ID (egal welche Filiale)?
                    var eawKey = eaw.UserId ?? eaw.Id;
                    var existingByEawId = await _db.Employees.FirstOrDefaultAsync(
                        e => !e.IsHidden && (e.EasyAtWorkEmployeeId == eawKey || e.EasyAtWorkEmployeeId == eaw.Id), ct);
                    // Stufe 2: gleicher Name+Geburtsdatum (Wiedereintritt mit NEUER eaw-ID).
                    // Nur, wenn die Vorschau das vermutet hat (row.PossibleReentry) —
                    // ist ein VORSCHLAG; wer es nicht will, deselektiert die Zeile.
                    bool viaNameDob = false;
                    if (existingByEawId == null && row.PossibleReentry && row.ReentryEmployeeId.HasValue)
                    {
                        existingByEawId = await _db.Employees.FirstOrDefaultAsync(e => e.Id == row.ReentryEmployeeId.Value, ct);
                        viaNameDob = existingByEawId != null;
                    }
                    if (existingByEawId != null)
                    {
                        // 1) Personalnummer als Alias sichern (falls anders + neu).
                        var newNum   = (eaw.Number ?? "").Trim();
                        var existNum = (existingByEawId.EmployeeNumber ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(newNum)
                            && !string.Equals(newNum, existNum, StringComparison.OrdinalIgnoreCase)
                            && !await _db.EmployeeNumberAliases.AnyAsync(a => a.EmployeeId == existingByEawId.Id && a.Number == newNum, ct))
                        {
                            _db.EmployeeNumberAliases.Add(new EmployeeNumberAlias
                            {
                                EmployeeId = existingByEawId.Id, Number = newNum,
                                Source = "easyatwork_sync", CreatedAt = DateTime.UtcNow,
                            });
                        }
                        // 2) easy@work-ID als Alias sichern, wenn die eaw-ID des
                        //    Duplikats abweicht (v.a. beim Name+Geb.-Match: alte ID
                        //    → der Stempel-Sync findet sie über easyatwork_employee_alias).
                        if (existingByEawId.EasyAtWorkEmployeeId != eaw.Id
                            && !await _db.EasyAtWorkEmployeeAliases.AnyAsync(a => a.EmployeeId == existingByEawId.Id && a.EasyAtWorkId == eaw.Id, ct))
                        {
                            _db.EasyAtWorkEmployeeAliases.Add(new EasyAtWorkEmployeeAlias
                            {
                                EmployeeId   = existingByEawId.Id,
                                EasyAtWorkId = eaw.Id,
                                Note         = viaNameDob ? "Auto-Merge: gleicher Name+Geb.datum, neue eaw-ID" : "Merge: zweite easy@work-ID",
                                CreatedAt    = DateTime.UtcNow,
                            });
                        }
                        // 3) Employment in DIESER Filiale nachholen (falls fehlt).
                        await EnsureEmploymentAsync(existingByEawId, eaw, req.CompanyProfileId, isNewEmployee: false, info, ct);
                        // 4) Als EXISTING markieren, NICHT als neuen Employee anlegen.
                        row.Status = "EXISTING";
                        row.CoworkEmployeeId = existingByEawId.Id;
                        row.Reason = viaNameDob
                            ? $"Wiedereintritt (Name+Geb.datum): bestehender MA #{existingByEawId.Id} {existingByEawId.EmployeeNumber}. Alte eaw-ID {eaw.Id} als Alias gesichert."
                            : $"MA existiert bereits (#{existingByEawId.Id} {existingByEawId.EmployeeNumber}). Nummer als Alias gesichert, Employment nachgeholt.";
                        if (viaNameDob)
                            res.Notes.Add($"Wiedereintritt erkannt: {eaw.FirstName} {eaw.LastName} → MA #{existingByEawId.Id} (alte eaw-ID {eaw.Id} als Alias).");
                        res.CountExisting++;
                        res.CountNew = Math.Max(0, res.CountNew - 1);
                        continue;
                    }

                    var emp = new Employee
                    {
                        EmployeeNumber       = row.Number ?? "",
                        FirstName            = eaw.FirstName ?? "",
                        LastName             = eaw.LastName ?? "",
                        IsActive             = !(eaw.To.HasValue && eaw.To.Value < activeAt),
                        ExitDate             = eaw.To?.ToDateTime(TimeOnly.MinValue),
                        EasyAtWorkEmployeeId = eaw.UserId ?? eaw.Id,
                    };
                    ApplyDiffs(emp, row.Diffs, eaw, natByCode);
                    if (string.IsNullOrWhiteSpace(emp.LanguageCode)) emp.LanguageCode = "de";
                    if (string.IsNullOrWhiteSpace(emp.Religion))     emp.Religion     = "keine";
                    if (string.IsNullOrWhiteSpace(emp.MaritalStatus)) emp.MaritalStatus = MaritalFor(row.EawEmployeeId);
                    _db.Employees.Add(emp);
                    res.CountInserted++;
                    await EnsureEmploymentAsync(emp, eaw, req.CompanyProfileId, isNewEmployee: true, info, ct);
                }
                else // UPDATE
                {
                    if (row.CoworkEmployeeId == null) continue;
                    var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == row.CoworkEmployeeId, ct);
                    if (emp == null) continue;
                    var newEawId = eaw.UserId ?? eaw.Id;
                    if (emp.EasyAtWorkEmployeeId != newEawId) emp.EasyAtWorkEmployeeId = newEawId;
                    ApplyDiffs(emp, row.Diffs, eaw, natByCode);
                    if (!string.IsNullOrWhiteSpace(row.NumberChangeTo))
                    {
                        SaveNumberChange(_db, emp, row.NumberChangeTo!);
                        res.Notes.Add($"Personalnummer geändert: {row.NumberChangeFrom} → {row.NumberChangeTo} (alte Nr. als Alias gesichert).");
                    }
                    if (string.IsNullOrWhiteSpace(emp.LanguageCode)) emp.LanguageCode = "de";
                    if (string.IsNullOrWhiteSpace(emp.Religion))     emp.Religion     = "keine";
                    if (string.IsNullOrWhiteSpace(emp.MaritalStatus)) emp.MaritalStatus = MaritalFor(row.EawEmployeeId);
                    if (eaw.To.HasValue && eaw.To.Value < activeAt)
                    {
                        emp.IsActive = false;
                        emp.ExitDate = eaw.To.Value.ToDateTime(TimeOnly.MinValue);
                    }
                    await EnsureEmploymentAsync(emp, eaw, req.CompanyProfileId, isNewEmployee: false, info, ct);
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
        Employee emp, EawEmployee eaw, int companyProfileId, bool isNewEmployee, HistContractInfo info, CancellationToken ct)
    {
        // UPDATE-MA: existiert schon ein Employment für (MA, Filiale)? Dann früh raus.
        if (!isNewEmployee && emp.Id != 0)
        {
            var has = await _db.Employments
                .AnyAsync(em => em.EmployeeId == emp.Id && em.CompanyProfileId == companyProfileId, ct);
            if (has) return;
        }

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
        Add("Geschlecht",  co?.Gender,      NormalizeGender(eaw.Gender));
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
                case "Geschlecht":   emp.Gender       = d.Easy; break;
                // „Personalnummer" wird NICHT hier angewendet — der Wechsel legt
                // einen Alias an (braucht den DbContext) und läuft im Commit-Pfad
                // via SaveNumberChange. Der Diff dient nur der Anzeige + Status UPDATE.
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

    /// <summary>
    /// Soll bei einem Nummernwechsel ein Alias gespeichert werden? Nur wenn die
    /// neue Nummer NICHT leer ist, sich von der aktuellen unterscheidet UND noch
    /// nicht als Alias hinterlegt ist (sonst Dublette). Seiteneffektfrei →
    /// unit-testbar. Walter-Vorgabe 21.06.2026.
    /// </summary>
    public static bool ShouldSaveNumberChange(string? currentNumber, string? newNumber, IEnumerable<string>? existingAliases)
    {
        if (string.IsNullOrWhiteSpace(newNumber)) return false;
        var n = newNumber.Trim();
        if (string.Equals(currentNumber?.Trim(), n, StringComparison.OrdinalIgnoreCase)) return false;
        if (existingAliases != null && existingAliases.Any(a =>
                string.Equals(a?.Trim(), n, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    /// <summary>
    /// Nummernwechsel: die bisherige Personalnummer als Alias sichern (mit
    /// valid_to = heute) und die neue als employee_number setzen.
    /// </summary>
    public static void SaveNumberChange(AppDbContext db, Employee emp, string newNumber)
    {
        db.EmployeeNumberAliases.Add(new EmployeeNumberAlias
        {
            Employee   = emp,
            EmployeeId = emp.Id,
            Number     = emp.EmployeeNumber,
            ValidTo    = DateOnly.FromDateTime(DateTime.Today),
            Source     = "easyatwork_sync",
        });
        emp.EmployeeNumber = newNumber.Trim();
    }

    /// <summary>Schlüssel für den Name+Geburtsdatum-Match (case-insensitiv, getrimmt).</summary>
    private static string NameDobKey(string? firstName, string? lastName, DateTime dob)
        => $"{(firstName ?? "").Trim().ToLowerInvariant()}|{(lastName ?? "").Trim().ToLowerInvariant()}|{dob:yyyy-MM-dd}";
    private static string NameDobKey(string? firstName, string? lastName, DateOnly dob)
        => $"{(firstName ?? "").Trim().ToLowerInvariant()}|{(lastName ?? "").Trim().ToLowerInvariant()}|{dob:yyyy-MM-dd}";

    /// <summary>easy@work-Gender → unser Wert „male"/„female" (sonst null).</summary>
    private static string? NormalizeGender(string? g)
    {
        if (string.IsNullOrWhiteSpace(g)) return null;
        return g.Trim().ToLowerInvariant() switch
        {
            "male" or "m" or "herr"   => "male",
            "female" or "f" or "frau" => "female",
            _                          => null
        };
    }

    /// <summary>
    /// Zivilstand best-effort aus den easy@work-Custom-Fields (Properties). Sucht
    /// ein Feld mit marital-/Zivilstand-Schlüssel und mappt den Wert auf unsere
    /// Codes. Fehlschlag/unbekannt → null (bleibt manuell). Walter-Vorgabe 21.06.2026.
    /// </summary>
    private async Task<string?> FetchMaritalStatusAsync(int customerId, int eawEmployeeId, CancellationToken ct)
    {
        try
        {
            var props = await _client.GetAllPropertiesAsync(customerId, eawEmployeeId, ct);
            var val = props
                .Where(p =>
                {
                    var k = (p.Key ?? "").ToLowerInvariant();
                    return k.Contains("marital") || k.Contains("civil") || k.Contains("zivil")
                        || k.Contains("familienstand") || k.Contains("family_status");
                })
                .OrderByDescending(p => p.From ?? DateOnly.MinValue)
                .Select(p => p.Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            return MapMaritalStatus(val);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Properties (Zivilstand) für easy@work-MA {Id} nicht abrufbar", eawEmployeeId); return null; }
    }

    private static string? MapMaritalStatus(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var s = v.Trim().ToLowerInvariant();
        if (s.Contains("ledig") || s.Contains("single") || s.Contains("celibat")) return "ledig";
        if (s.Contains("getrennt") || s.Contains("separat") || s.Contains("separe")) return "getrennt";
        if (s.Contains("geschieden") || s.Contains("divorc")) return "geschieden";
        if (s.Contains("verwitwet") || s.Contains("widow") || s.Contains("veuf") || s.Contains("veuve")) return "verwitwet";
        if (s.Contains("eingetragene") || s.Contains("registered") || s.Contains("partnerschaft")) return "eingetragene_partnerschaft";
        if (s.Contains("verheiratet") || s.Contains("married") || s.Contains("marie") || s.Contains("mariée")) return "verheiratet";
        return null;   // unbekannt → bleibt manuell
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
