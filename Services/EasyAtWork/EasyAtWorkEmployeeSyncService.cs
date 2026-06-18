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
        /// <summary>Beim Commit: nur diese Personalnummern schreiben (NULL = alle NEW+UPDATE).</summary>
        public List<string>? SelectedNumbers { get; set; }
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

        // 2) easy@work-MA laden
        //    - Wenn ExitedAfter gesetzt: ALLE (inkl. ehemalige) laden + lokal filtern
        //      (aktiv ODER Austritt > ExitedAfter)
        //    - sonst: nur am Stichtag aktive
        List<EawEmployee> eawEmps;
        try
        {
            if (req.IncludeAllInactive)
            {
                // ALLE — auch ehemalige (Initial-Import).
                eawEmps = await _client.GetAllEmployeesIncludingInactiveAsync(mapping.EasyAtWorkCustomerId, ct);
                res.Notes.Add($"{eawEmps.Count} MA insgesamt (inkl. alle ausgetretenen).");
            }
            else if (req.ExitedAfter.HasValue)
            {
                var raw = await _client.GetAllEmployeesIncludingInactiveAsync(mapping.EasyAtWorkCustomerId, ct);
                var cutoff = req.ExitedAfter.Value;
                eawEmps = raw.Where(e =>
                    !e.To.HasValue ||                                      // noch aktiv (kein Austritt)
                    e.To.Value >= activeAt ||                              // Austritt liegt nach Stichtag (= aktiv am Stichtag)
                    e.To.Value > cutoff                                    // Austritt liegt nach dem Cutoff
                ).ToList();
                res.Notes.Add($"{raw.Count} MA insgesamt, davon {eawEmps.Count} nach Filter (aktiv oder Austritt > {cutoff:dd.MM.yyyy}).");
            }
            else
            {
                eawEmps = await _client.GetAllEmployeesActiveAtAsync(mapping.EasyAtWorkCustomerId, activeAt, ct);
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
        var byNumber = coworkAll
            .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeNumber))
            .GroupBy(e => e.EmployeeNumber.Trim())
            .ToDictionary(g => g.Key, g => g.First());

        // Nationality-Lookup (ISO-Code → Id)
        var natByCode = await _db.Nationalities.AsNoTracking()
            .ToDictionaryAsync(n => (n.Code ?? "").ToUpperInvariant(), n => n.Id, ct);

        var selected = req.SelectedNumbers != null
            ? new HashSet<string>(req.SelectedNumbers, StringComparer.OrdinalIgnoreCase)
            : null;

        foreach (var eaw in eawEmps)
        {
            var row = new EmployeePreviewRow
            {
                EawEmployeeId = eaw.Id,
                Number        = (eaw.Number ?? "").Trim(),
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

            byNumber.TryGetValue(row.Number, out var co);
            row.CoworkEmployeeId = co?.Id;

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
                        IsActive             = true,
                        // Wir speichern primär die user_id (auf die edited_by_id zeigt),
                        // mit Fallback auf die Employee-Id. Walter 17.06.2026.
                        EasyAtWorkEmployeeId = eaw.UserId ?? eaw.Id,
                    };
                    ApplyDiffs(emp, row.Diffs, eaw, natByCode);
                    _db.Employees.Add(emp);
                    res.CountInserted++;
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
