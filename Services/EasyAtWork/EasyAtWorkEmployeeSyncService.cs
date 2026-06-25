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

    public sealed class SingleEmployeeSyncResult
    {
        public bool Success { get; set; }
        public int EmployeeId { get; set; }
        public int? EasyAtWorkEmployeeId { get; set; }
        public List<string> UpdatedFields { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    public async Task<SingleEmployeeSyncResult> SyncSingleCoworkEmployeeAsync(
        int employeeId, int? companyProfileId, CancellationToken ct = default)
    {
        var result = new SingleEmployeeSyncResult { EmployeeId = employeeId };
        var emp = await _db.Employees
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == employeeId && !e.IsHidden, ct);
        if (emp == null)
        {
            result.Errors.Add("Mitarbeiter nicht gefunden.");
            return result;
        }

        if (!emp.EasyAtWorkEmployeeId.HasValue)
        {
            result.Errors.Add("Bei diesem Mitarbeiter ist keine easy@work-ID hinterlegt. Bitte zuerst den easy@work-MA eindeutig zuordnen.");
            return result;
        }

        var mappings = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .OrderBy(m => m.CompanyProfileId)
            .ToListAsync(ct);
        if (mappings.Count == 0)
        {
            result.Errors.Add("Es ist keine Filiale mit easy@work verknüpft.");
            return result;
        }

        EawEmployee? eaw = null;
        int? matchedCustomerId = null;
        foreach (var mapping in mappings)
        {
            eaw = await _client.GetEmployeeByIdAsync(mapping.EasyAtWorkCustomerId, emp.EasyAtWorkEmployeeId.Value, ct);
            if (eaw != null)
            {
                matchedCustomerId = mapping.EasyAtWorkCustomerId;
                break;
            }
        }
        if (eaw == null)
        {
            // Legacy-Reparatur: In alten Daten steht teils easy@work user_id in
            // employee.easyatwork_employee_id. Der Single-Endpoint erwartet aber
            // employee.id. Wir holen genau diesen MA per n+Personalnummer, prüfen
            // die user_id gegen den gespeicherten Wert und speichern unten die
            // echte employee.id zurück. Falls der n+Nummer-Single-Endpoint beim
            // API-Anbieter nicht sauber greift, lösen wir dieselbe Person über
            // die Employee-Liste des Customers auf (nur für diese Legacy-Reparatur).
            var employeeNumber = (emp.EmployeeNumber ?? "").Trim();
            foreach (var mapping in mappings)
            {
                EawEmployee? byNumber = null;
                if (employeeNumber.Length == 0) break;
                byNumber = await _client.GetEmployeeByNumberAsync(mapping.EasyAtWorkCustomerId, employeeNumber, ct);
                if (byNumber == null)
                {
                    try
                    {
                        var rows = await _client.GetAllEmployeesIncludingInactiveAsync(mapping.EasyAtWorkCustomerId, ct);
                        byNumber = rows.FirstOrDefault(x =>
                            x.UserId == emp.EasyAtWorkEmployeeId.Value
                            || string.Equals((x.Number ?? "").Trim(), employeeNumber, StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception ex)
                    {
                        result.Notes.Add($"Legacy-ID-Reparatur Customer {mapping.EasyAtWorkCustomerId}: Employee-Liste nicht abrufbar ({ex.Message}).");
                    }
                }
                if (byNumber == null) continue;
                var numberMatches = string.Equals((byNumber.Number ?? "").Trim(), employeeNumber, StringComparison.OrdinalIgnoreCase);
                var userIdMatches = byNumber.UserId == emp.EasyAtWorkEmployeeId.Value;
                if (!numberMatches && !userIdMatches) continue;

                eaw = byNumber;
                matchedCustomerId = mapping.EasyAtWorkCustomerId;
                result.Notes.Add($"Gespeicherte easy@work-ID {emp.EasyAtWorkEmployeeId.Value} war user_id; korrigiere auf employee.id {eaw.Id} (Customer {mapping.EasyAtWorkCustomerId}).");
                break;
            }
        }
        if (eaw == null)
        {
            result.Errors.Add($"Mitarbeiter in easy@work nicht gefunden (gespeicherte ID {emp.EasyAtWorkEmployeeId.Value}; employee.id-Suche und Legacy-user_id-Reparatur über alle gemappten Filialen ohne Treffer).");
            return result;
        }
        result.EasyAtWorkEmployeeId = eaw.Id;

        var natByCode = await _db.Nationalities.AsNoTracking()
            .ToDictionaryAsync(n => (n.Code ?? "").ToUpperInvariant(), n => n.Id, ct);
        var propsInfo = await FetchPropsInfoAsync(matchedCustomerId!.Value, eaw.Id, ct);
        EawFiscalInfo? fiscal = null;
        try { fiscal = await _client.GetFiscalInfoAsync(matchedCustomerId.Value, eaw.Id, ct); }
        catch (Exception ex) { result.Notes.Add($"IBAN/Fiscal-Info nicht abrufbar: {ex.Message}"); }

        var normalizedGender = NormalizeGender(eaw.Gender);
        var salutation = SalutationFromGender(eaw.Gender);
        var letterSalutation = BuildLetterSalutation(normalizedGender, eaw.FirstName);
        var (street, houseNumber) = SplitStreetHouse(eaw.Address1);
        var zip = string.IsNullOrWhiteSpace(eaw.PostalCode) ? null : eaw.PostalCode.Trim();
        var loc = await ResolveSwissLocationAsync(zip, eaw.City, ct);
        var phone = NormalizePhone(eaw.Phone);
        var email = string.IsNullOrWhiteSpace(eaw.Email) ? null : eaw.Email.Trim().ToLowerInvariant();
        var ahv = propsInfo.Ahv;
        var iban = fiscal?.Iban?.Replace(" ", "").Trim().ToUpperInvariant();
        var nationality = ResolveNationalityCode(eaw.Nationality, natByCode);

        if (string.IsNullOrWhiteSpace(eaw.FirstName)) result.Errors.Add("Vorname fehlt in easy@work.");
        if (string.IsNullOrWhiteSpace(eaw.LastName)) result.Errors.Add("Nachname fehlt in easy@work.");
        if (!string.IsNullOrWhiteSpace(email) && !IsValidEmail(email)) result.Errors.Add($"E-Mail ist ungültig: {email}");
        if (!string.IsNullOrWhiteSpace(ahv) && !IsValidAhv(ahv)) result.Errors.Add($"AHV-Nummer ist ungültig: {ahv}");
        if (!string.IsNullOrWhiteSpace(iban) && !IsValidIban(iban)) result.Errors.Add($"IBAN ist ungültig: {iban}");
        if (!string.IsNullOrWhiteSpace(zip) && loc.Error != null) result.Errors.Add(loc.Error);
        if (result.Errors.Count > 0) return result;

        void SetString(string label, string? current, string? next, Action<string?> set, bool allowNull = true)
        {
            var value = string.IsNullOrWhiteSpace(next) ? null : next.Trim();
            if (value == null && !allowNull) return;
            if (string.Equals(current?.Trim(), value, StringComparison.OrdinalIgnoreCase)) return;
            set(value);
            result.UpdatedFields.Add(label);
        }

        SetString("Vorname", emp.FirstName, eaw.FirstName, v => emp.FirstName = v ?? emp.FirstName, allowNull: false);
        SetString("Nachname", emp.LastName, eaw.LastName, v => emp.LastName = v ?? emp.LastName, allowNull: false);
        SetString("Geschlecht", emp.Gender, normalizedGender, v => emp.Gender = v);
        SetString("Anrede", emp.Salutation, salutation, v => emp.Salutation = v);
        SetString("Briefanrede", emp.LetterSalutation, letterSalutation, v => emp.LetterSalutation = v);
        if (eaw.BirthDate.HasValue)
        {
            var dob = eaw.BirthDate.Value.ToDateTime(TimeOnly.MinValue);
            if (emp.DateOfBirth?.Date != dob.Date) { emp.DateOfBirth = dob; result.UpdatedFields.Add("Geburtsdatum"); }
        }
        SetString("AHV-Nummer", emp.SocialSecurityNumber, ahv, v => emp.SocialSecurityNumber = v);
        SetString("Zivilstand", emp.MaritalStatus, propsInfo.Marital, v => emp.MaritalStatus = v);
        SetString("Sprache", emp.LanguageCode, "de", v => emp.LanguageCode = v);
        SetString("Nationalität", emp.Nationality, nationality, v => emp.Nationality = v);
        if (!string.IsNullOrWhiteSpace(nationality) && natByCode.TryGetValue(nationality.ToUpperInvariant(), out var natId) && emp.NationalityId != natId)
        {
            emp.NationalityId = natId;
            if (!result.UpdatedFields.Contains("Nationalität")) result.UpdatedFields.Add("Nationalität");
        }
        SetString("Strasse", emp.Street, street, v => emp.Street = v);
        SetString("Hausnummer", emp.HouseNumber, houseNumber, v => emp.HouseNumber = v);
        SetString("PLZ", emp.ZipCode, zip, v => emp.ZipCode = v);
        SetString("Ort", emp.City, loc.City ?? eaw.City, v => emp.City = v);
        SetString("Kanton", emp.CantonCode, loc.Canton, v => emp.CantonCode = v);
        SetString("Land", emp.Country, string.IsNullOrWhiteSpace(zip) ? (eaw.CountryKey ?? eaw.Country)?.ToUpperInvariant() : "CH", v => emp.Country = v);
        SetString("Telefon", emp.PhoneMobile, phone, v => emp.PhoneMobile = v);
        SetString("E-Mail", emp.Email, email, v => emp.Email = v);
        if (eaw.From.HasValue)
        {
            var entry = eaw.From.Value.ToDateTime(TimeOnly.MinValue);
            if (emp.EntryDate?.Date != entry.Date) { emp.EntryDate = entry; result.UpdatedFields.Add("Eintrittsdatum"); }
        }
        if (emp.EasyAtWorkEmployeeId != eaw.Id)
        {
            emp.EasyAtWorkEmployeeId = eaw.Id;
            result.UpdatedFields.Add("easy@work-ID");
        }
        if (!string.IsNullOrWhiteSpace(iban))
        {
            var changed = await EnsureBankAccountFromEasyWorkAsync(emp, iban, ct);
            if (changed) result.UpdatedFields.Add("IBAN");
        }

        await _db.SaveChangesAsync(ct);
        result.Success = true;
        if (result.UpdatedFields.Count == 0) result.Notes.Add("Keine Änderungen — Cowork war bereits aktuell.");
        return result;
    }

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
        // MA, die bereits eine Bankverbindung haben (Backfill-Erkennung, Walter 22.06.2026).
        var bankSet = (await _db.EmployeeBankAccounts.AsNoTracking()
            .Select(b => b.EmployeeId).Distinct().ToListAsync(ct)).ToHashSet();
        // JobGroups (Funktion → Kader-Flag) für die Modell-Ableitung aus /positions.
        var jobGroupByCode = new Dictionary<string, JobGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in await _db.JobGroups.AsNoTracking().ToListAsync(ct))
            if (!string.IsNullOrWhiteSpace(g.Code)) jobGroupByCode.TryAdd(g.Code.Trim(), g);
        // MA, deren Anstellung bereits eine Funktion (JobGroup) trägt (Backfill-Erkennung).
        var jobGroupEmpSet = (await _db.Employments.AsNoTracking()
            .Where(em => em.JobGroupId != null).Select(em => em.EmployeeId).Distinct().ToListAsync(ct)).ToHashSet();
        // Aktuellste Anstellung pro MA in DIESER Filiale — für die Vertrags-Mismatch-
        // Erkennung (UTP/MTP mit Pensum %, fehlender Lohn, Enddatum trotz unbefristet).
        // Sonst würden solche MA als UNCHANGED übersprungen und der Vertrag nie korrigiert.
        var empByIdThisBranch = (await _db.Employments.AsNoTracking()
            .Where(em => em.CompanyProfileId == req.CompanyProfileId).ToListAsync(ct))
            .GroupBy(em => em.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.ContractStartDate).First());
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

        // Leere Auswahl bedeutet im Frontend bewusst: alle gematchten MA
        // (inkl. UNCHANGED) trotzdem durch den Commit-Pfad schicken, damit
        // Vertrags-/Lohnhistorie, easy@work-IDs und Backfills nachgezogen werden.
        // Vorher wurde [] als "niemand" interpretiert → Timeline lief nicht.
        var selected = req.SelectedNumbers is { Count: > 0 }
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
            else if (byEawId.TryGetValue(eaw.Id, out co)) matchedByEawId = true;
            else if (eaw.UserId.HasValue && byEawId.TryGetValue(eaw.UserId.Value, out co)) matchedByEawId = true;
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
            else if (co != null && !co.IsActive && (!eaw.To.HasValue || eaw.To.Value >= activeAt))
            {
                // Reaktivierung (Walter-Bug 22.06.2026, Filialwechsel): MA ist bei
                // uns inaktiv, in DIESER Filiale laut easy@work aber noch aktiv
                // (kein/zukünftiges Austrittsdatum). Ohne Feld-Diff wäre er sonst
                // UNCHANGED und würde übersprungen → als UPDATE behandeln.
                row.Status = "UPDATE";
                row.Reason = "MA war als ausgetreten markiert, ist in dieser Filiale laut easy@work aber aktiv (Filialwechsel) → wird reaktiviert, Austrittsdatum wird entfernt.";
                res.CountUpdate++;
            }
            else if (co != null && empByIdThisBranch.TryGetValue(co.Id, out var curEmp)
                     && EmploymentNeedsFix(curEmp, eaw))
            {
                // Falscher Vertrag (Walter 23.06.2026): UTP/MTP mit Pensum %, fehlender
                // Stundenlohn, MTP ohne garantierte Stunden, oder Enddatum trotz
                // unbefristet. Ohne Feld-Diff wäre der MA UNCHANGED → der Vertrag würde
                // nie korrigiert. Als UPDATE behandeln, damit der Backfill greift.
                row.Status = "UPDATE";
                row.Reason = "Vertrag wird korrigiert (Pensum / Stundenlohn / Enddatum gemäss easy@work).";
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
                var newId = eaw.Id;
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

            // Zu schreibende Zeilen: NEW/UPDATE PLUS bereits zugeordnete UNCHANGED-MA
            // (Walter-Vorgabe 23.06.2026). Letztere werden still mit easy@work
            // abgeglichen — sonst bliebe ein einmal falsch angelegter Vertrag (z.B.
            // UTP statt MTP) für immer stehen, weil ohne Feld-Diff niemand ihn anfasst.
            // EnsureEmploymentAsync ist idempotent (Modell führend, Lohn fill-if-empty).
            var rowsToProcess = res.Rows
                .Where(r =>
                    // NEW/UPDATE bleiben selektiv: nur schreiben, wenn ausgewählt
                    // (oder wenn selected=null = "alle"). UNCHANGED-MA mit Cowork-
                    // Zuordnung müssen aber IMMER durch den Commit-Pfad laufen:
                    // dort hängen Timeline-Sync, easy@work-IDs und Backfills dran.
                    // Walter-Bug 23.06.2026: Sobald einige UPDATE-Zeilen angewählt
                    // waren, wurden UNCHANGED-Zeilen wie Amire ausgeschlossen.
                    (r.Status == "NEW" || r.Status == "UPDATE")
                        ? (selected == null || selected.Contains(r.Number ?? ""))
                        : (r.Status == "UNCHANGED" && r.CoworkEmployeeId.HasValue))
                .ToList();
            var rowsForTimeline = res.Rows
                .Where(r => r.CoworkEmployeeId.HasValue)
                .ToList();

            // Detail-Daten (Verträge/Pay-Rates/Zivilstand) PARALLEL vorladen (max. 10
            // gleichzeitig) statt 3 sequenzielle API-Calls pro MA. Diese Calls nutzen
            // NUR den HTTP-Client (nicht den DbContext) → thread-safe. Beim Schnell-
            // Import (SkipDetailCalls) ganz überspringen. Walter-Vorgabe 21.06.2026.
            // Rohe Verträge + Pay-Rates pro MA → daraus baut der zweite Durchgang die
            // komplette Employment-Timeline (Walter-Vorgabe 23.06.2026).
            var contractsRawByEaw = new ConcurrentDictionary<int, List<EawContract>>();
            var ratesRawByEaw     = new ConcurrentDictionary<int, List<EawPayRate>>();
            var maritalByEaw  = new ConcurrentDictionary<int, string?>();
            var ahvByEaw      = new ConcurrentDictionary<int, string?>();
            var ibanByEaw     = new ConcurrentDictionary<int, string?>();
            var positionByEaw = new ConcurrentDictionary<int, string?>();
            if (rowsToProcess.Count > 0)
            {
                using var sem = new SemaphoreSlim(10);
                var detailTasks = rowsToProcess
                    .Concat(rowsForTimeline)
                    .Select(r => r.EawEmployeeId).Distinct()
                    .Select(async eawId =>
                    {
                        await sem.WaitAsync(ct);
                        try
                        {
                            // PFLICHT — die Vertrags-/Lohn-/Funktionshistorie darf NIE
                            // übersprungen werden (Walter-Vorgabe 23.06.2026), auch nicht
                            // bei SkipDetailCalls. Ohne diese Endpunkte gäbe es keine
                            // Timeline und alte falsche Verträge blieben unkorrigiert.
                            try { contractsRawByEaw[eawId] = (await _client.GetContractsAsync(mapping.EasyAtWorkCustomerId, eawId, ct))?.Data ?? new(); }
                            catch (Exception ex) { _log.LogDebug(ex, "Verträge für easy@work-MA {Id} nicht abrufbar", eawId); contractsRawByEaw[eawId] = new(); }
                            try { ratesRawByEaw[eawId] = (await _client.GetPayRatesAsync(mapping.EasyAtWorkCustomerId, eawId, ct))?.Data ?? new(); }
                            catch (Exception ex) { _log.LogDebug(ex, "Pay-Rates für easy@work-MA {Id} nicht abrufbar", eawId); ratesRawByEaw[eawId] = new(); }
                            try { var pos = await _client.GetPositionsAsync(mapping.EasyAtWorkCustomerId, eawId, ct); positionByEaw[eawId] = pos?.Data?.FirstOrDefault()?.Name; }
                            catch (Exception ex) { _log.LogDebug(ex, "Positionen für easy@work-MA {Id} nicht abrufbar", eawId); }

                            // OPTIONALE Zusatz-Stammdaten — nur diese hängen an SkipDetailCalls.
                            if (!req.SkipDetailCalls)
                            {
                                // Eine Property-Abfrage → Zivilstand UND AHV-Nr.
                                var (marital, ahv) = await FetchPropsInfoAsync(mapping.EasyAtWorkCustomerId, eawId, ct);
                                maritalByEaw[eawId] = marital;
                                ahvByEaw[eawId]     = ahv;
                                // IBAN aus fiscal_info.
                                try { var fiscal = await _client.GetFiscalInfoAsync(mapping.EasyAtWorkCustomerId, eawId, ct); ibanByEaw[eawId] = fiscal?.Iban; }
                                catch (Exception ex) { _log.LogDebug(ex, "Fiscal-Info (IBAN) für easy@work-MA {Id} nicht abrufbar", eawId); }
                            }
                        }
                        finally { sem.Release(); }
                    });
                await Task.WhenAll(detailTasks);
            }
            List<EawContract> ContractsFor(int eawId) => contractsRawByEaw.TryGetValue(eawId, out var c) ? c : new();
            List<EawPayRate>  RatesFor(int eawId)     => ratesRawByEaw.TryGetValue(eawId, out var r) ? r : new();
            string? MaritalFor(int eawId)             => maritalByEaw.TryGetValue(eawId, out var m) ? m : null;
            string? AhvFor(int eawId)                 => ahvByEaw.TryGetValue(eawId, out var a) ? a : null;
            string? IbanFor(int eawId)                => ibanByEaw.TryGetValue(eawId, out var b) ? b : null;
            string? PositionFor(int eawId)            => positionByEaw.TryGetValue(eawId, out var p) ? p : null;
            // Timeline-Arbeit + Bankverbindungen sammeln → zweiter Durchgang NACH dem
            // Speichern (emp.Id muss persistiert sein, damit der Natural-Key-Upsert greift).
            var timelineWork = new System.Collections.Generic.List<(Employee emp, int eawId, int? jobGroupId, string? jobGroupCode, bool isKader, DateOnly? eawTo)>();
            var bankWork     = new System.Collections.Generic.List<(Employee emp, string iban)>();

            foreach (var row in rowsToProcess)
            {
                var eaw = eawEmps.FirstOrDefault(e => e.Id == row.EawEmployeeId);
                if (eaw == null) continue;
                // Funktion aus /positions → JobGroup; Kader ⇒ FIX-M (Walter 22.06.2026).
                // Gilt für ALLE Timeline-Segmente dieses MA (Position ist MA-bezogen).
                var posName = PositionFor(row.EawEmployeeId);
                int? jobGroupId = null; string? jobGroupCode = null; bool isKader = false;
                if (!string.IsNullOrWhiteSpace(posName) && jobGroupByCode.TryGetValue(posName!.Trim(), out var jg))
                {
                    jobGroupId = jg.Id; jobGroupCode = jg.Code; isKader = jg.IsKader;
                }

                if (row.Status == "NEW")
                {
                    // Duplikat-Prävention Stufe 1 (Walter-Vorgabe 21.06.2026): existiert
                    // schon ein Employee mit dieser easy@work-ID (egal welche Filiale)?
                    var eawKey = eaw.Id;
                    var existingByEawId = await _db.Employees.FirstOrDefaultAsync(
                        e => !e.IsHidden && (e.EasyAtWorkEmployeeId == eawKey || (eaw.UserId.HasValue && e.EasyAtWorkEmployeeId == eaw.UserId.Value)), ct);
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
                        if (existingByEawId.EasyAtWorkEmployeeId != eaw.Id)
                            existingByEawId.EasyAtWorkEmployeeId = eaw.Id;
                        // 3) Employment-Timeline in DIESER Filiale spiegeln (2. Durchgang).
                        timelineWork.Add((existingByEawId, row.EawEmployeeId, jobGroupId, jobGroupCode, isKader, eaw.To));
                        // 3b) Status-Korrektur (Walter-Bug 22.06.2026, Filialwechsel):
                        //     Ist der MA bei uns inaktiv, aber diese easy@work-Anstellung
                        //     hat KEIN Austrittsdatum (oder in der Zukunft), dann ist er in
                        //     DIESER Filiale aktiv → Person reaktivieren.
                        bool eawStillActiveEx = !eaw.To.HasValue || eaw.To.Value >= activeAt;
                        bool reactivated = false;
                        if (!existingByEawId.IsActive && eawStillActiveEx)
                        {
                            existingByEawId.IsActive = true;
                            existingByEawId.ExitDate = null;
                            reactivated = true;
                        }
                        // 4) Als EXISTING markieren, NICHT als neuen Employee anlegen.
                        row.Status = "EXISTING";
                        row.CoworkEmployeeId = existingByEawId.Id;
                        row.Reason = viaNameDob
                            ? $"Wiedereintritt (Name+Geb.datum): bestehender MA #{existingByEawId.Id} {existingByEawId.EmployeeNumber}. Alte eaw-ID {eaw.Id} als Alias gesichert."
                            : $"MA existiert bereits (#{existingByEawId.Id} {existingByEawId.EmployeeNumber}). Nummer als Alias gesichert, Employment nachgeholt.";
                        if (reactivated) row.Reason += " MA reaktiviert — Austrittsdatum entfernt (Filialwechsel).";
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
                        EasyAtWorkEmployeeId = eaw.Id,
                    };
                    ApplyDiffs(emp, row.Diffs, eaw, natByCode);
                    if (string.IsNullOrWhiteSpace(emp.LanguageCode)) emp.LanguageCode = "de";
                    if (string.IsNullOrWhiteSpace(emp.Religion))     emp.Religion     = "keine";
                    if (string.IsNullOrWhiteSpace(emp.MaritalStatus)) emp.MaritalStatus = MaritalFor(row.EawEmployeeId);
                    if (string.IsNullOrWhiteSpace(emp.SocialSecurityNumber)) emp.SocialSecurityNumber = AhvFor(row.EawEmployeeId);
                    if (string.IsNullOrWhiteSpace(emp.CantonCode)) emp.CantonCode = await LookupCantonAsync(emp.ZipCode, ct);
                    if (string.IsNullOrWhiteSpace(emp.LetterSalutation)) emp.LetterSalutation = BuildLetterSalutation(emp.Gender, emp.FirstName);
                    _db.Employees.Add(emp);
                    res.CountInserted++;
                    timelineWork.Add((emp, row.EawEmployeeId, jobGroupId, jobGroupCode, isKader, eaw.To));
                    var ibN = IbanFor(row.EawEmployeeId); if (!string.IsNullOrWhiteSpace(ibN)) bankWork.Add((emp, ibN!));
                }
                else // UPDATE
                {
                    if (row.CoworkEmployeeId == null) continue;
                    var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == row.CoworkEmployeeId, ct);
                    if (emp == null) continue;
                    var newEawId = eaw.Id;
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
                    if (string.IsNullOrWhiteSpace(emp.SocialSecurityNumber)) emp.SocialSecurityNumber = AhvFor(row.EawEmployeeId);
                    if (string.IsNullOrWhiteSpace(emp.CantonCode)) emp.CantonCode = await LookupCantonAsync(emp.ZipCode, ct);
                    if (string.IsNullOrWhiteSpace(emp.LetterSalutation)) emp.LetterSalutation = BuildLetterSalutation(emp.Gender, emp.FirstName);
                    // Aktiv-Status (Walter-Bug 22.06.2026, Filialwechsel): eaw.To ist das
                    // Austrittsdatum DIESER Filiale, nicht des Menschen.
                    bool eawStillActiveUp = !eaw.To.HasValue || eaw.To.Value >= activeAt;
                    if (!emp.IsActive && eawStillActiveUp)
                    {
                        // In dieser Filiale laut easy@work noch aktiv → Person reaktivieren.
                        emp.IsActive = true;
                        emp.ExitDate = null;
                    }
                    else if (eaw.To.HasValue && eaw.To.Value < activeAt)
                    {
                        // Nur inaktiv setzen, wenn der MA NICHT in einer ANDEREN Filiale
                        // noch aktiv ist. C) NICHT über is_active prüfen (kann stale sein),
                        // sondern über das Vertragsende: offen oder Ende ab heute = aktiv.
                        var todayD = DateTime.Today;
                        bool activeElsewhere = await _db.Employments
                            .AnyAsync(em => em.EmployeeId == emp.Id
                                         && em.CompanyProfileId != req.CompanyProfileId
                                         && (em.ContractEndDate == null || em.ContractEndDate >= todayD), ct);
                        if (!activeElsewhere)
                        {
                            emp.IsActive = false;
                            emp.ExitDate = eaw.To.Value.ToDateTime(TimeOnly.MinValue);
                        }
                    }
                    timelineWork.Add((emp, row.EawEmployeeId, jobGroupId, jobGroupCode, isKader, eaw.To));
                    var ibU = IbanFor(row.EawEmployeeId); if (!string.IsNullOrWhiteSpace(ibU)) bankWork.Add((emp, ibU!));
                    res.CountUpdated++;
                }
            }

            // Vertrags-/Lohnhistorie ist nicht optional und nicht an Checkboxen
            // gebunden: Auch nicht ausgewählte UPDATE/UNCHANGED-MA müssen die
            // easy@work-Timeline bekommen. Stammdaten-Diffs bleiben oben
            // selektiv, aber Contracts/PayRates spiegeln wir immer.
            var timelineEawIds = timelineWork.Select(x => x.eawId).ToHashSet();
            foreach (var row in rowsForTimeline.Where(r => !timelineEawIds.Contains(r.EawEmployeeId)))
            {
                var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == row.CoworkEmployeeId, ct);
                if (emp == null) continue;
                var eaw = eawEmps.FirstOrDefault(e => e.Id == row.EawEmployeeId);
                var posName = PositionFor(row.EawEmployeeId);
                int? jobGroupId = null; string? jobGroupCode = null; bool isKader = false;
                if (!string.IsNullOrWhiteSpace(posName) && jobGroupByCode.TryGetValue(posName!.Trim(), out var jg))
                {
                    jobGroupId = jg.Id; jobGroupCode = jg.Code; isKader = jg.IsKader;
                }
                timelineWork.Add((emp, row.EawEmployeeId, jobGroupId, jobGroupCode, isKader, eaw?.To));
            }
            await _db.SaveChangesAsync(ct);

            // ── Zweiter Durchgang: komplette Employment-Timeline spiegeln (erst JETZT,
            //    wo alle Employee-IDs gespeichert sind → Natural-Key-Upsert greift).
            //    Alle historischen + aktuellen + zukünftigen Verträge/Lohnstufen aus
            //    easy@work werden als Employment-Versionen ge-upsertet. Walter 23.06.2026.
            if (timelineWork.Count > 0 || bankWork.Count > 0)
            {
                foreach (var (temp, teawId, tJgId, tJgCode, tIsKader, tEawTo) in timelineWork)
                {
                    var tContracts = ContractsFor(teawId);
                    var tRates     = RatesFor(teawId);
                    var timeline   = BuildEmploymentTimeline(tContracts, tRates, activeAt, tIsKader);
                    _log.LogInformation("easy@work-Sync MA {Num}: contracts={C}, payRates={R}, timeline={T}",
                        temp.EmployeeNumber, tContracts.Count, tRates.Count, timeline.Count);
                    await SyncEmploymentTimelineAsync(_db, temp, req.CompanyProfileId, timeline, tJgId, tJgCode, tEawTo, ct);
                }
                foreach (var (bemp, iban) in bankWork)
                    await EnsureBankAccountAsync(bemp, iban, ct);
                await _db.SaveChangesAsync(ct);
            }

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
    public sealed class HistContractInfo
    {
        public DateTime? StartDate;            // = frühestes Pay-Rate-From (Lohn-Beginn)
        public DateOnly? RateFrom;             // Von-Datum des aktuell gültigen Tarifs (= Vertrags-„ab")
        public DateOnly? ContractFrom;         // Beginn DIESES Vertrags (easy@work contract.from)
        public DateOnly? ContractTo;           // Ende DIESES Vertrags (null = unbefristet)
        public int?      JobGroupId;           // aus /positions → job_group
        public string?   JobGroupCode;         // z.B. REST_MANAGER / SHIFT_LEADER_7_PLUS / CREW
        public string?   EmploymentModel;      // FIX / MTP / UTP / FIX-M
        public string?   SalaryType;           // monthly / hourly
        public string?   ContractType;
        public string?   JobTitle;
        public decimal?  WeeklyHours;
        public decimal?  GuaranteedHoursPerWeek; // MTP: garantierte Wochenstunden
        public decimal?  EmploymentPercentage;
        public decimal?  HourlyRate;
        public decimal?  MonthlySalary;
        public decimal?  MonthlySalaryFte;       // FIX/FIX-M: 100%-Lohn
    }

    /// <summary>
    /// Liefert (a) den am Stichtag <paramref name="asOf"/> gültigen Vertrag (current)
    /// und (b) optional einen zukünftig startenden Vertrag (future, From &gt; asOf).
    /// Best-effort: Fehlschläge lassen Felder leer. Mapping: amount_type month → FIX;
    /// hour + Type MTP/TPM → MTP; hour sonst → UTP. Lohnsatz = der am jeweiligen
    /// Datum gültige (jüngster Pay-Rate mit From ≤ Datum). Walter-Vorgabe 22.06.2026.
    /// </summary>
    private async Task<(HistContractInfo current, HistContractInfo? future)> BuildHistContractInfoAsync(
        int customerId, int eawEmployeeId, DateOnly asOf, CancellationToken ct)
    {
        List<EawContract> contracts = new();
        List<EawPayRate>  rates     = new();
        try { contracts = (await _client.GetContractsAsync(customerId, eawEmployeeId, ct))?.Data ?? new(); }
        catch (Exception ex) { _log.LogDebug(ex, "Verträge für easy@work-MA {Id} nicht abrufbar", eawEmployeeId); }
        try { rates = (await _client.GetPayRatesAsync(customerId, eawEmployeeId, ct))?.Data ?? new(); }
        catch (Exception ex) { _log.LogDebug(ex, "Pay-Rates für easy@work-MA {Id} nicht abrufbar", eawEmployeeId); }

        var ordered  = contracts.OrderBy(c => c.From ?? DateOnly.MinValue).ToList();
        var currentC = ordered.LastOrDefault(c => (c.From ?? DateOnly.MinValue) <= asOf) ?? ordered.FirstOrDefault();
        var futureC  = ordered.FirstOrDefault(c => (c.From ?? DateOnly.MinValue) > asOf);

        var current = ComputeContractInfo(currentC, rates, asOf);
        HistContractInfo? future = (futureC?.From != null) ? ComputeContractInfo(futureC, rates, futureC.From.Value) : null;
        return (current, future);
    }

    /// <summary>
    /// Reine Mapping-Logik easy@work-Vertrag + Pay-Rates → <see cref="HistContractInfo"/>
    /// (API-frei, statisch, unit-testbar). Walter-Vorgabe 23.06.2026.
    ///   • Modell aus amount_type: "month"/"percent" → FIX (Monatslohn), "week"/"hour"
    ///     → Stundenlohn (MTP wenn Typ MTP/TPM oder Wochenstunden > 17, sonst UTP).
    ///   • FIX/FIX-M: MonthlySalary = effektiver Pensumslohn aus easy@work,
    ///     MonthlySalaryFte = 100%-Lohn (effektiv / Pensum × 100).
    ///   • Lohnsatz ≤ 1.00 = Platzhalter ("kein Lohn") → ignoriert.
    ///   • <paramref name="isKader"/> (Position IsKader) → Modell FIX-M, Monatslohnfelder
    ///     bleiben erhalten (spiegelt ApplyJg im Commit-Loop).
    /// </summary>
    public static HistContractInfo ComputeContractInfo(
        EawContract? c, List<EawPayRate> rates, DateOnly rateDate, bool isKader = false)
    {
        rates ??= new();
        var earliestRateFrom = rates.Where(r => r.From.HasValue).OrderBy(r => r.From)
            .Select(r => r.From!.Value.ToDateTime(TimeOnly.MinValue)).Cast<DateTime?>().FirstOrDefault();
        // Typ-Präfix-Match: easy@work liefert "hour"/"month" (NICHT "hourly"/"monthly").
        // Lohnsatz ≤ 1.00 = Platzhalter "kein Lohn" → ignorieren (Feld bleibt leer).
        IEnumerable<EawPayRate> RatesOfType(string t, DateOnly date) => rates
            .Where(r => (r.Type ?? "").StartsWith(t, StringComparison.OrdinalIgnoreCase) && r.Rate.HasValue
                     && r.Rate.Value > 1.00m
                     && (r.From ?? DateOnly.MinValue) <= date)
            .OrderByDescending(r => r.From ?? DateOnly.MinValue);
        decimal?   RateAt(string t, DateOnly date)     => RatesOfType(t, date).Select(r => r.Rate).FirstOrDefault();
        DateOnly?  RateFromAt(string t, DateOnly date)  => RatesOfType(t, date).Select(r => r.From).FirstOrDefault();

        var info = new HistContractInfo { StartDate = earliestRateFrom, ContractFrom = c?.From, ContractTo = c?.To };
        if (c != null)
        {
            info.ContractType = string.IsNullOrWhiteSpace(c.Type)  ? null : c.Type!.Trim();
            info.JobTitle     = string.IsNullOrWhiteSpace(c.Title) ? null : c.Title!.Trim();
            var amt = (c.AmountType ?? "").Trim().ToLowerInvariant();
            var typ = (c.Type ?? "").Trim().ToUpperInvariant();
            // Vertragsmodell aus dem easy@work-Vertrag (Walter-Vorgabe 23.06.2026):
            //   amount_type "month"/"percent" → FIX (Monatslohn; "percent" = Pensum-
            //                                   Monatslohnvertrag, KEIN Stundenlohn)
            //   amount_type "week"/"hour"      → Stundenlohn:
            //       Typ MTP/TPM  ODER  Wochenstunden (amount) > 17  → MTP
            //       sonst (z.B. amount 17, der UTP-Default)         → UTP
            // Leeres amount_type → Contract-Type Fix/Full ⇒ month, sonst week.
            if (string.IsNullOrEmpty(amt))
                amt = (typ.Contains("FIX") || typ.Contains("FULL")) ? "month" : "week";
            if (amt.StartsWith("month") || amt.StartsWith("percent")) { info.EmploymentModel = "FIX"; info.SalaryType = "monthly"; }
            else
            {
                var wochenStd = c.Amount ?? c.WeekHours;
                bool isMtp = typ.Contains("MTP") || typ.Contains("TPM")
                             || (wochenStd.HasValue && wochenStd.Value > 17m);
                info.EmploymentModel = isMtp ? "MTP" : "UTP";
                info.SalaryType = "hourly";
            }
        }

        // Felder je Vertragsmodell (EXAKT wie der CSV-Import buildEmploymentPayload):
        //   UTP → Stundenlohn, kein Pensum; MTP → Stundenlohn + garantierte Stunden;
        //   FIX/FIX-M → Monatslohn (effektiv + 100%-FTE) + Pensum %.
        var hourly  = RateAt("hour", rateDate);
        var monthly = RateAt("month", rateDate) ?? RateAt("fte", rateDate);
        info.RateFrom = RateFromAt("hour", rateDate) ?? RateFromAt("month", rateDate) ?? RateFromAt("fte", rateDate);
        if (info.EmploymentModel == "FIX" || info.EmploymentModel == "FIX-M")
        {
            // easy@work liefert beim Monatslohn den EFFEKTIVEN Pensumslohn (z.B. 2760
            // bei 60%). Bei uns ist MonthlySalaryFte IMMER der 100%-Lohn → hochrechnen.
            // Beispiel: 2760 / 60 × 100 = 4600.
            var pct = c?.Percentage ?? c?.Amount;
            info.EmploymentPercentage = pct;
            info.WeeklyHours          = null;
            info.GuaranteedHoursPerWeek = null;
            info.SalaryType           = "monthly";
            if (monthly.HasValue)
            {
                info.MonthlySalary    = monthly.Value;                       // effektiver Pensumslohn
                info.MonthlySalaryFte = (pct.HasValue && pct.Value > 0)
                    ? Math.Round(monthly.Value / pct.Value * 100m, 2)        // 100%-Lohn
                    : monthly.Value;
            }
        }
        else // UTP / MTP / unbekannt → Stundenlohn, kein Pensum
        {
            info.HourlyRate           = hourly;
            info.EmploymentPercentage = null;
            info.WeeklyHours          = null;
            info.GuaranteedHoursPerWeek = info.EmploymentModel == "MTP" ? (c?.Amount ?? c?.WeekHours) : null;
            info.SalaryType           = "hourly";
        }

        // Kader (Position IsKader) ⇒ FIX-M. Monatslohnfelder bleiben unverändert
        // (spiegelt ApplyJg im Commit-Loop). Walter-Vorgabe 23.06.2026.
        if (isKader)
        {
            info.EmploymentModel = "FIX-M";
            info.SalaryType      = "monthly";
        }
        return info;
    }

    /// <summary>Ein Segment der easy@work-Employment-Timeline (eine Vertrags-/Lohnperiode).</summary>
    public sealed class EmploymentSegment
    {
        public DateOnly  Start;
        public DateOnly? End;                    // null = offen
        public HistContractInfo Info = new();
        public int?      EasyAtWorkContractId;   // Herkunfts-Contract (easy@work)
        public int?      EasyAtWorkPayRateId;    // Herkunfts-PayRate (easy@work)
        public DateTime? EasyAtWorkUpdatedAt;    // max(contract.updated_at, pay_rate.updated_at)
        public bool      EasyAtWorkManualOverride; // true bei Platzhalterlohn rate<=1
    }

    /// <summary>
    /// Baut aus ALLEN easy@work-Verträgen + Pay-Rates eine lückenlose Employment-
    /// Timeline (Walter-Vorgabe 23.06.2026). Grenzen = contract.from, contract.to+1,
    /// pay_rate.from, pay_rate.to+1. Pro Segment gilt genau EIN Vertrag + EINE
    /// Lohnstufe; gilt an einem Segmentstart weder Vertrag NOCH Lohnstufe → übersprungen.
    /// Direkt angrenzende, inhaltlich identische Segmente werden zusammengeführt.
    /// API-frei + statisch → unit-testbar. (asOf nur zur Signatur-Kompatibilität.)
    /// </summary>
    public static List<EmploymentSegment> BuildEmploymentTimeline(
        List<EawContract>? contracts, List<EawPayRate>? rates, DateOnly asOf, bool isKader = false)
    {
        contracts ??= new();
        rates     ??= new();

        static bool CApplies(EawContract c, DateOnly d) => c.From.HasValue && c.From.Value <= d && (!c.To.HasValue || c.To.Value >= d);
        static bool RApplies(EawPayRate r, DateOnly d)  => r.From.HasValue && r.From.Value <= d && (!r.To.HasValue || r.To.Value >= d);

        var bset = new SortedSet<DateOnly>();
        foreach (var c in contracts)
        {
            if (c.From.HasValue) bset.Add(c.From.Value);
            if (c.To.HasValue)   bset.Add(c.To.Value.AddDays(1));
        }
        foreach (var r in rates)
        {
            if (r.From.HasValue) bset.Add(r.From.Value);
            if (r.To.HasValue)   bset.Add(r.To.Value.AddDays(1));
        }
        var bounds = bset.ToList();
        var segments = new List<EmploymentSegment>();
        for (int i = 0; i < bounds.Count; i++)
        {
            var start = bounds[i];
            // easy@work liefert Contracts/PayRates NICHT garantiert chronologisch →
            // IMMER den jüngsten passenden nehmen (OrderByDescending(From)), nie das
            // erste Listenelement. Walter-Vorgabe 23.06.2026.
            var cAt = contracts
                .Where(c => CApplies(c, start))
                .OrderByDescending(c => c.From ?? DateOnly.MinValue)
                .FirstOrDefault();
            var rAt = rates
                .Where(r => RApplies(r, start))
                .OrderByDescending(r => r.From ?? DateOnly.MinValue)
                .FirstOrDefault();
            if (cAt == null) continue; // Ohne gültigen Vertrag kein Employment-Segment

            DateOnly? end = (i < bounds.Count - 1) ? bounds[i + 1].AddDays(-1) : (DateOnly?)null;
            if (i == bounds.Count - 1)
            {
                bool anyOpen = (cAt != null && !cAt.To.HasValue)
                            || rates.Any(r => RApplies(r, start) && !r.To.HasValue);
                if (!anyOpen)
                {
                    DateOnly? maxClose = cAt?.To;
                    foreach (var r in rates.Where(r => RApplies(r, start) && r.To.HasValue))
                        if (!maxClose.HasValue || r.To!.Value > maxClose.Value) maxClose = r.To;
                    end = maxClose;
                }
            }
            segments.Add(new EmploymentSegment
            {
                Start = start,
                End   = end,
                Info  = ComputeContractInfo(cAt, rates, start, isKader),
                EasyAtWorkContractId = (cAt != null && cAt.Id != 0) ? cAt.Id : (int?)null,
                EasyAtWorkPayRateId  = (rAt != null && rAt.Id != 0) ? rAt.Id : (int?)null,
                EasyAtWorkUpdatedAt  = MaxUpdated(cAt?.UpdatedAt, rAt?.UpdatedAt),
                EasyAtWorkManualOverride = rAt?.Rate.HasValue == true && rAt.Rate.Value <= 1.00m,
            });
        }

        // Direkt angrenzende, inhaltlich identische Segmente zusammenführen.
        var merged = new List<EmploymentSegment>();
        foreach (var seg in segments)
        {
            var last = merged.Count > 0 ? merged[merged.Count - 1] : null;
            if (last != null && last.End.HasValue && seg.Start == last.End.Value.AddDays(1) && SameSegment(last, seg))
                last.End = seg.End;
            else
                merged.Add(seg);
        }
        return merged;
    }

    private static bool SameSegment(EmploymentSegment a, EmploymentSegment b)
        => a.EasyAtWorkContractId == b.EasyAtWorkContractId
        && a.EasyAtWorkPayRateId == b.EasyAtWorkPayRateId
        && a.EasyAtWorkManualOverride == b.EasyAtWorkManualOverride
        && SameTerms(a.Info, b.Info);

    private static DateTime? MaxUpdated(DateTime? a, DateTime? b)
    {
        var max = (a.HasValue && b.HasValue) ? (a.Value >= b.Value ? a : b) : (a ?? b);
        // Spalte ist `timestamp without time zone` → Npgsql verbietet Kind=Utc.
        // Auf Unspecified normalisieren (der Wert dient nur als Versions-Marker).
        return max.HasValue ? DateTime.SpecifyKind(max.Value, DateTimeKind.Unspecified) : (DateTime?)null;
    }

    private static bool SameTerms(HistContractInfo a, HistContractInfo b)
        => a.EmploymentModel == b.EmploymentModel
        && a.EmploymentPercentage == b.EmploymentPercentage
        && a.MonthlySalary == b.MonthlySalary
        && a.MonthlySalaryFte == b.MonthlySalaryFte
        && a.HourlyRate == b.HourlyRate
        && a.GuaranteedHoursPerWeek == b.GuaranteedHoursPerWeek;

    /// <summary>
    /// Spiegelt die Timeline-Segmente als Employment-Versionen in Cowork (Walter-
    /// Vorgabe 23.06.2026). UPSERT nach Natural Key (employee_id + company_profile_id
    /// + contract_start_date): existiert die Zeile → Modell/Lohn/Pensum/Ende/JobGroup/
    /// IsActive korrigieren; sonst neu anlegen. NICHTS löschen (Historie bleibt).
    /// Schliesst zum Schluss offene Verträge in ANDEREN Filialen (Filialwechsel).
    /// </summary>
    public static async Task SyncEmploymentTimelineAsync(
        AppDbContext db, Employee emp, int companyProfileId, List<EmploymentSegment> timeline,
        int? jobGroupId, string? jobGroupCode, DateOnly? eawTo, CancellationToken ct = default)
    {
        if (emp.Id == 0 || timeline == null || timeline.Count == 0) return;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var existingAll = await db.Employments
            .Where(em => em.EmployeeId == emp.Id && em.CompanyProfileId == companyProfileId)
            .ToListAsync(ct);

        var matched = new HashSet<Employment>();
        foreach (var seg in timeline)
        {
            var startDt  = seg.Start.ToDateTime(TimeOnly.MinValue);
            var endDt    = seg.End.HasValue ? seg.End.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null;
            bool active  = !seg.End.HasValue || seg.End.Value >= today;
            var info     = seg.Info;

            // Match-Reihenfolge (Walter-Vorgabe 23.06.2026):
            //   1) externe IDs Contract + PayRate (stabilster Schlüssel)
            //   2) Contract-ID + gleicher Start (PayRate-ID war noch nicht gesetzt)
            //   3) Fallback Natural Key (employee+filiale+contract_start_date)
            // So ist der Re-Sync idempotent, auch wenn sich der Start leicht verschiebt
            // oder eine Alt-Zeile noch keine easy@work-IDs trägt.
            Employment? existing = null;
            if (seg.EasyAtWorkContractId.HasValue && seg.EasyAtWorkPayRateId.HasValue)
                existing = existingAll.FirstOrDefault(e => !matched.Contains(e)
                    && e.EasyAtWorkContractId == seg.EasyAtWorkContractId
                    && e.EasyAtWorkPayRateId  == seg.EasyAtWorkPayRateId);
            if (existing == null && seg.EasyAtWorkContractId.HasValue)
                existing = existingAll.FirstOrDefault(e => !matched.Contains(e)
                    && e.EasyAtWorkContractId == seg.EasyAtWorkContractId
                    && e.ContractStartDate == startDt);
            if (existing == null)
                existing = existingAll.FirstOrDefault(e => !matched.Contains(e) && e.ContractStartDate == startDt);

            if (existing == null)
            {
                db.Employments.Add(new Employment
                {
                    Employee             = emp,
                    EmployeeId           = emp.Id,
                    CompanyProfileId     = companyProfileId,
                    ContractStartDate    = startDt,
                    ContractEndDate      = endDt,
                    IsActive             = active,
                    EmploymentModel      = string.IsNullOrWhiteSpace(info.EmploymentModel) ? "UTP"    : info.EmploymentModel!,
                    SalaryType           = string.IsNullOrWhiteSpace(info.SalaryType)      ? "hourly" : info.SalaryType!,
                    ContractType         = info.ContractType,
                    JobGroupId           = jobGroupId,
                    JobTitle             = jobGroupCode ?? info.JobGroupCode ?? info.JobTitle,
                    EmploymentPercentage = info.EmploymentPercentage,
                    WeeklyHours          = info.WeeklyHours,
                    GuaranteedHoursPerWeek = info.GuaranteedHoursPerWeek,
                    HourlyRate           = info.HourlyRate,
                    MonthlySalary        = info.MonthlySalary,
                    MonthlySalaryFte     = info.MonthlySalaryFte,
                    EasyAtWorkContractId = seg.EasyAtWorkContractId,
                    EasyAtWorkPayRateId  = seg.EasyAtWorkPayRateId,
                    EasyAtWorkUpdatedAt  = seg.EasyAtWorkUpdatedAt,
                    EasyAtWorkManualOverride = seg.EasyAtWorkManualOverride,
                });
            }
            else
            {
                matched.Add(existing);
                var wasManualOverride = existing.EasyAtWorkManualOverride;
                // easy@work-Herkunft immer setzen/aktualisieren. Auch lokal
                // geschützte Verträge behalten so ihre externe Zuordnung.
                existing.EasyAtWorkContractId = seg.EasyAtWorkContractId;
                existing.EasyAtWorkPayRateId  = seg.EasyAtWorkPayRateId;
                existing.EasyAtWorkUpdatedAt  = seg.EasyAtWorkUpdatedAt;
                if (seg.EasyAtWorkManualOverride) existing.EasyAtWorkManualOverride = true;

                // Lokaler Override schützt Vertrag UND Lohn vollständig vor
                // easy@work-Überschreibung. Nur externe IDs/UpdatedAt werden oben
                // aktualisiert. Wenn der Override erst durch dieses Segment (rate<=1)
                // entsteht, dürfen Vertragsdaten noch gespiegelt werden, aber Lohn-
                // felder werden nicht geleert/überschrieben.
                if (wasManualOverride)
                    continue;

                existing.ContractStartDate = startDt;   // Start auf das Segment ausrichten (ID-Match kann verschieben)
                existing.ContractEndDate   = endDt;
                existing.IsActive          = active;
                if (!string.IsNullOrWhiteSpace(info.EmploymentModel)) existing.EmploymentModel = info.EmploymentModel!;
                if (!string.IsNullOrWhiteSpace(info.SalaryType))      existing.SalaryType      = info.SalaryType!;
                if (!string.IsNullOrWhiteSpace(info.ContractType))    existing.ContractType    = info.ContractType;
                if (jobGroupId != null) { existing.JobGroupId = jobGroupId; existing.JobTitle = jobGroupCode ?? existing.JobTitle; }

                var m = existing.EmploymentModel;
                if (m == "FIX" || m == "FIX-M")
                {
                    existing.HourlyRate             = null;
                    existing.GuaranteedHoursPerWeek = null;
                    existing.WeeklyHours            = null;
                    existing.EmploymentPercentage   = info.EmploymentPercentage ?? existing.EmploymentPercentage;
                    if (!seg.EasyAtWorkManualOverride && info.MonthlySalaryFte.HasValue) existing.MonthlySalaryFte = info.MonthlySalaryFte;
                    if (!seg.EasyAtWorkManualOverride && info.MonthlySalary.HasValue)    existing.MonthlySalary    = info.MonthlySalary;
                    else if (!seg.EasyAtWorkManualOverride && existing.MonthlySalaryFte.HasValue && existing.EmploymentPercentage.HasValue && existing.EmploymentPercentage.Value > 0)
                        existing.MonthlySalary = Math.Round(existing.MonthlySalaryFte.Value * existing.EmploymentPercentage.Value / 100m, 2);
                }
                else // UTP / MTP
                {
                    existing.MonthlySalary        = null;
                    existing.MonthlySalaryFte     = null;
                    existing.EmploymentPercentage = null;
                    existing.WeeklyHours          = null;
                    if (!seg.EasyAtWorkManualOverride && info.HourlyRate.HasValue) existing.HourlyRate = info.HourlyRate;
                    existing.GuaranteedHoursPerWeek = m == "MTP" ? (info.GuaranteedHoursPerWeek ?? existing.GuaranteedHoursPerWeek) : null;
                }
            }
        }

        // Überlappende Cowork-Zeilen, die NICHT von der Timeline gematcht wurden,
        // korrigieren (NICHT löschen): hinten auf den Tag vor dem frühesten
        // überlappenden Segment kappen. So verschwinden Doppel-/Altzeilen, die
        // Historie bleibt erhalten.
        var todayDt = DateTime.Today;
        foreach (var ex in existingAll.Where(e => !matched.Contains(e)))
        {
            foreach (var seg in timeline.OrderBy(s => s.Start))
            {
                var segStart = seg.Start.ToDateTime(TimeOnly.MinValue);
                if (ex.ContractStartDate <= segStart && (ex.ContractEndDate == null || ex.ContractEndDate >= segStart))
                {
                    ex.ContractEndDate = segStart.AddDays(-1);
                    ex.IsActive        = ex.ContractEndDate >= todayDt;
                    break;
                }
            }
        }

        // Aktuellstes aktives Segment → offene Verträge in anderen Filialen schliessen.
        var latestActive = timeline.Where(s => !s.End.HasValue || s.End.Value >= today)
                                   .OrderByDescending(s => s.Start).FirstOrDefault();
        if (latestActive != null)
            await CloseOtherBranchOpenEmploymentsAsync(
                db, emp.Id, companyProfileId, latestActive.Start.ToDateTime(TimeOnly.MinValue), eawTo, ct);
    }

    /// <summary>
    /// Stellt für einen importierten MA eine Employment-Zeile sicher (Walter-
    /// Vorgabe 21.06.2026). NEW-MA: immer anlegen. UPDATE-MA: nur NACHHOLEN, wenn
    /// für (MA, Filiale) noch KEIN Employment existiert (Backfill, idempotent).
    /// Felder: Filiale + Start/Ende aus EntryDate/ExitDate (Fallback Von/Bis),
    /// IsActive vom MA, Modell/Lohn/Funktion soweit easy@work liefert, sonst
    /// UTP-Default (Stundenlohn = häufigstes Crew-Modell).
    /// </summary>
    /// <summary>
    /// Erkennt einen strukturell falschen Vertrag, der vom Sync korrigiert werden
    /// muss (sonst bliebe der MA UNCHANGED). Spiegelt die CSV-Import-Regeln:
    /// UTP/MTP dürfen kein Pensum % haben und brauchen einen Stundenlohn, MTP
    /// braucht garantierte Wochenstunden, FIX/FIX-M einen Monatslohn, und ein
    /// Enddatum darf nur stehen, wenn easy@work ein „Bis" hat. Walter 23.06.2026.
    /// </summary>
    private static bool EmploymentNeedsFix(Employment e, EawEmployee eaw)
    {
        var m = e.EmploymentModel;
        if (m == "UTP" || m == "MTP")
        {
            if (e.EmploymentPercentage != null) return true;   // Pensum bei Stundenlohn = falsch
            if (e.HourlyRate == null)           return true;   // Stundenlohn fehlt
            if (m == "MTP" && e.GuaranteedHoursPerWeek == null) return true;
        }
        else if (m == "FIX" || m == "FIX-M")
        {
            if (e.MonthlySalary == null && e.MonthlySalaryFte == null) return true;
        }
        // Enddatum gesetzt, obwohl easy@work unbefristet (Bis leer) meldet.
        if (e.ContractEndDate != null && !eaw.To.HasValue) return true;
        return false;
    }

    private async Task EnsureEmploymentAsync(
        Employee emp, EawEmployee eaw, int companyProfileId, bool isNewEmployee, HistContractInfo info, CancellationToken ct)
    {
        // UPDATE-MA: existiert schon ein Employment für (MA, Filiale)? Dann NICHT mehr
        // früh raus, sondern leere Felder aus easy@work NACHFÜLLEN (Walter 22.06.2026,
        // fill-if-empty — bestehende, ggf. manuell gepflegte Werte NIE überschreiben).
        if (!isNewEmployee && emp.Id != 0)
        {
            var existing = await _db.Employments
                .Where(em => em.EmployeeId == emp.Id && em.CompanyProfileId == companyProfileId)
                .OrderByDescending(em => em.ContractStartDate)
                .FirstOrDefaultAsync(ct);
            if (existing != null)
            {
                // Vertrags-„ab" = Beginn der aktuell gültigen Lohnstufe (Tarif-Von, z.B.
                // 01.01.2026), Fallback Eintritt (eaw.From). Ende = easy@work Bis
                // (eaw.To); leer = unbefristet. Behebt das stehengebliebene ExitDate als
                // Vertragsende und das falsche Eintrittsdatum als Vertrags-„ab".
                var newStart = info.RateFrom?.ToDateTime(TimeOnly.MinValue)
                               ?? eaw.From?.ToDateTime(TimeOnly.MinValue);
                if (newStart.HasValue) existing.ContractStartDate = newStart.Value;
                existing.ContractEndDate = eaw.To.HasValue ? eaw.To.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null;
                // A) IsActive MUSS mit dem Vertragsende synchron sein (Walter 23.06.2026):
                // contract_end_date konnte gesetzt sein, während is_active alt/falsch blieb.
                var todayDate = DateOnly.FromDateTime(DateTime.Today);
                existing.IsActive = !eaw.To.HasValue || eaw.To.Value >= todayDate;
                // Funktion (JobGroup) ist führend aus easy@work /positions → setzen.
                if (info.JobGroupId != null)
                {
                    existing.JobGroupId = info.JobGroupId;
                    existing.JobTitle   = info.JobGroupCode;
                }
                // Vertragsmodell ist FÜHREND aus easy@work (Vertrag amount_type/amount +
                // Position/Kader) — korrigiert eine Fehlklassifizierung, z.B. UTP → MTP
                // (Wochenstunden > 17) oder UTP → FIX-M (Kader). LOHNRELEVANT.
                // NUR beim AKTUELLEN Vertrag (kein Ende oder Ende in der Zukunft) —
                // historische/abgeschlossene Verträge bleiben unangetastet, damit
                // bereits abgerechnete Perioden nicht verändert werden. Walter 23.06.2026.
                bool istAktuellerVertrag = existing.ContractEndDate == null
                                           || existing.ContractEndDate >= DateTime.Today;
                if (istAktuellerVertrag && !string.IsNullOrWhiteSpace(info.EmploymentModel))
                {
                    existing.EmploymentModel = info.EmploymentModel!;
                    if (!string.IsNullOrWhiteSpace(info.SalaryType)) existing.SalaryType = info.SalaryType!;
                    if (info.EmploymentModel == "FIX-M" && existing.MonthlySalary == null)
                        existing.MonthlySalary = info.MonthlySalary;
                }
                // Übrige leere Felder nachfüllen (kein Überschreiben).
                if (existing.HourlyRate == null)               existing.HourlyRate = info.HourlyRate;
                if (existing.MonthlySalary == null)            existing.MonthlySalary = info.MonthlySalary;
                if (existing.MonthlySalaryFte == null)         existing.MonthlySalaryFte = info.MonthlySalaryFte;
                if (string.IsNullOrWhiteSpace(existing.ContractType))    existing.ContractType = info.ContractType;
                if (string.IsNullOrWhiteSpace(existing.EmploymentModel) && !string.IsNullOrWhiteSpace(info.EmploymentModel)) existing.EmploymentModel = info.EmploymentModel;
                if (string.IsNullOrWhiteSpace(existing.SalaryType) && !string.IsNullOrWhiteSpace(info.SalaryType))           existing.SalaryType = info.SalaryType;

                // Modell-Strukturfelder korrigieren (klassifizierungs-abgeleitet, KEIN
                // manueller Userwert) — exakt wie der CSV-Import buildEmploymentPayload:
                //   UTP/MTP: KEIN Pensum %, kein Monatslohn; MTP trägt garantierte
                //            Wochenstunden. Behebt das fälschlich gesetzte Pensum
                //            (z.B. 40.48 % bei UTP). Walter-Vorgabe 23.06.2026.
                // Nur am AKTUELLEN Vertrag (historische unangetastet, s.o.).
                var effModel = existing.EmploymentModel;
                if (istAktuellerVertrag && (effModel == "UTP" || effModel == "MTP"))
                {
                    existing.EmploymentPercentage = null;
                    existing.MonthlySalary        = null;
                    existing.MonthlySalaryFte     = null;
                    existing.WeeklyHours          = null;
                    existing.GuaranteedHoursPerWeek = effModel == "MTP"
                        ? (info.GuaranteedHoursPerWeek ?? existing.GuaranteedHoursPerWeek)
                        : null;
                }
                else if (istAktuellerVertrag && (effModel == "FIX" || effModel == "FIX-M"))
                {
                    // Monatslohn: easy@work ist führend → auch eine LOHNÄNDERUNG übernehmen
                    // (nicht nur „if null"). MonthlySalaryFte = 100%-Lohn, MonthlySalary =
                    // effektiver Pensumslohn (bzw. aus FTE×Pensum berechnet). Walter 23.06.2026.
                    existing.HourlyRate             = null;
                    existing.GuaranteedHoursPerWeek = null;
                    existing.WeeklyHours            = null;
                    existing.EmploymentPercentage   = info.EmploymentPercentage ?? existing.EmploymentPercentage;
                    if (info.MonthlySalaryFte.HasValue)
                        existing.MonthlySalaryFte = info.MonthlySalaryFte;
                    if (info.MonthlySalary.HasValue)
                        existing.MonthlySalary = info.MonthlySalary;
                    else if (existing.MonthlySalaryFte.HasValue && existing.EmploymentPercentage.HasValue && existing.EmploymentPercentage.Value > 0)
                        existing.MonthlySalary = Math.Round(existing.MonthlySalaryFte.Value * existing.EmploymentPercentage.Value / 100m, 2);
                }
                // B) Bei aktivem Vertrag in DIESER Filiale alte offene Verträge desselben
                // MA in ANDEREN Filialen sauber beenden (Filialwechsel). Historie bleibt.
                if (existing.IsActive)
                    await CloseOtherBranchOpenEmploymentsAsync(_db, emp.Id, companyProfileId, existing.ContractStartDate, eaw.To, ct);
                return;
            }
        }

        // Vertrags-Start/-Ende kommen aus easy@work Von/Bis (= eaw.From/eaw.To), genau
        // wie der CSV-Import (dort die Spalten „Von"/„Bis"). KEIN Rückgriff auf das
        // personenbezogene ExitDate — das gehört nicht zum Filial-Vertrag und ist bei
        // Filialwechsel verfälscht. Bis leer = unbefristet. Walter-Vorgabe 23.06.2026.
        var startDate = info.RateFrom?.ToDateTime(TimeOnly.MinValue)
                        ?? eaw.From?.ToDateTime(TimeOnly.MinValue)
                        ?? emp.EntryDate
                        ?? info.StartDate
                        ?? DateTime.UtcNow.Date;
        var endDate = eaw.To.HasValue ? eaw.To.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null;

        // Employment-IsActive ist FILIALBEZOGEN (Vertrag in DIESER Filiale aktiv),
        // NICHT personenbezogen wie emp.IsActive — aus eaw.To ableiten. So bleibt
        // die alte Filiale beim Wechsel korrekt „beendet", die Person aber aktiv
        // (Walter-Bug 22.06.2026).
        var employmentIsActive = !eaw.To.HasValue
                                 || eaw.To.Value >= DateOnly.FromDateTime(DateTime.Today);
        await AddEmploymentIfMissingAsync(
            _db, emp, companyProfileId, isNewEmployee,
            startDate, endDate, employmentIsActive,
            info.EmploymentModel, info.SalaryType, info.ContractType, info.JobGroupCode ?? info.JobTitle,
            info.WeeklyHours, info.EmploymentPercentage, info.HourlyRate, info.MonthlySalary, ct,
            jobGroupId: info.JobGroupId,
            guaranteedHoursPerWeek: info.GuaranteedHoursPerWeek, monthlySalaryFte: info.MonthlySalaryFte);

        // B) Neuer aktiver Filialvertrag → alte offene Verträge desselben MA in ANDEREN
        // Filialen beenden (nur bestehender MA mit Id; ein brandneuer hat keine).
        if (employmentIsActive && emp.Id != 0)
            await CloseOtherBranchOpenEmploymentsAsync(_db, emp.Id, companyProfileId, startDate, eaw.To, ct);
    }

    /// <summary>
    /// B) Schließt offene/überlappende Employments desselben MA in ANDEREN Filialen,
    /// wenn in der aktuellen Filiale ein aktiver Vertrag (eaw.To leer oder zukünftig)
    /// gilt — Filialwechsel. Historie bleibt erhalten (kein Löschen), das alte
    /// Employment bekommt nur ContractEndDate = Tag vor dem neuen Start + IsActive=false.
    /// Walter-Vorgabe 23.06.2026.
    /// </summary>
    public static async Task CloseOtherBranchOpenEmploymentsAsync(
        AppDbContext db, int employeeId, int currentBranchId, DateTime newStart, DateOnly? eawTo,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (eawTo.HasValue && eawTo.Value < today) return; // neuer Vertrag nicht aktiv → nichts schließen

        var closeDate = newStart.Date.AddDays(-1);
        var otherOpenEmployments = await db.Employments
            .Where(em => em.EmployeeId == employeeId
                      && em.CompanyProfileId != currentBranchId
                      && (em.ContractEndDate == null || em.ContractEndDate >= newStart))
            .ToListAsync(ct);
        foreach (var old in otherOpenEmployments)
        {
            if (old.ContractStartDate.Date <= closeDate)
            {
                old.ContractEndDate = closeDate;
                old.IsActive = false;
            }
        }
    }

    /// <summary>
    /// Legt für einen zukünftig startenden easy@work-Vertrag eine versionierte
    /// Anstellung an (ContractStartDate = Zukunftsdatum) und begrenzt die laufende
    /// Anstellung auf den Vortag. Idempotent: existiert für das Startdatum bereits
    /// eine Anstellung, passiert nichts. Wird im ZWEITEN Durchgang (nach Save)
    /// aufgerufen, damit emp.Id + die laufende Anstellung persistiert sind.
    /// Walter-Vorgabe 22.06.2026.
    /// </summary>
    private async Task EnsureFutureEmploymentAsync(Employee emp, int companyProfileId, HistContractInfo future, CancellationToken ct)
    {
        if (emp.Id == 0 || future.ContractFrom == null) return;
        var futureStart = future.ContractFrom.Value.ToDateTime(TimeOnly.MinValue);

        // Schon eine Anstellung mit genau diesem Startdatum? → nichts tun.
        var exists = await _db.Employments.AnyAsync(
            em => em.EmployeeId == emp.Id && em.CompanyProfileId == companyProfileId
               && em.ContractStartDate == futureStart, ct);
        if (exists) return;

        // Laufende Anstellung (Start < Zukunft, noch offen oder über Zukunft hinaus)
        // auf den Vortag des Zukunftsstarts begrenzen.
        var capDate = futureStart.AddDays(-1);
        var current = await _db.Employments
            .Where(em => em.EmployeeId == emp.Id && em.CompanyProfileId == companyProfileId
                      && em.ContractStartDate < futureStart
                      && (em.ContractEndDate == null || em.ContractEndDate > capDate))
            .OrderByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync(ct);
        if (current != null) current.ContractEndDate = capDate;

        _db.Employments.Add(new Employment
        {
            EmployeeId           = emp.Id,
            CompanyProfileId     = companyProfileId,
            JobGroupId           = future.JobGroupId,
            ContractStartDate    = futureStart,
            ContractEndDate      = null,
            IsActive             = true,
            EmploymentModel      = string.IsNullOrWhiteSpace(future.EmploymentModel) ? "UTP"    : future.EmploymentModel!.Trim(),
            SalaryType           = string.IsNullOrWhiteSpace(future.SalaryType)      ? "hourly" : future.SalaryType!.Trim(),
            ContractType         = future.ContractType,
            JobTitle             = future.JobGroupCode ?? future.JobTitle,
            WeeklyHours          = future.WeeklyHours,
            GuaranteedHoursPerWeek = future.GuaranteedHoursPerWeek,
            EmploymentPercentage = future.EmploymentPercentage,
            HourlyRate           = future.HourlyRate,
            MonthlySalary        = future.MonthlySalary,
            MonthlySalaryFte     = future.MonthlySalaryFte,
        });
    }

    /// <summary>
    /// Legt eine Bankverbindung aus der easy@work-IBAN an, WENN der MA noch keine
    /// hat (fill-if-empty — bestehende werden nie überschrieben). Als Hauptbank,
    /// Aufteilung VOLL. Walter-Vorgabe 22.06.2026.
    /// </summary>
    private async Task EnsureBankAccountAsync(Employee emp, string iban, CancellationToken ct)
    {
        if (emp.Id == 0 || string.IsNullOrWhiteSpace(iban)) return;
        var hasAny = await _db.EmployeeBankAccounts.AnyAsync(b => b.EmployeeId == emp.Id, ct);
        if (hasAny) return;

        var clean = iban.Replace(" ", "").Trim().ToUpperInvariant();
        _db.EmployeeBankAccounts.Add(new EmployeeBankAccount
        {
            EmployeeId    = emp.Id,
            Iban          = clean,
            IsHauptbank   = true,
            AufteilungTyp = "VOLL",
            ValidFrom     = emp.EntryDate.HasValue
                ? DateOnly.FromDateTime(emp.EntryDate.Value)
                : DateOnly.FromDateTime(DateTime.Today),
            Bemerkung     = "Import easy@work",
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        });
    }

    private async Task<bool> EnsureBankAccountFromEasyWorkAsync(Employee emp, string iban, CancellationToken ct)
    {
        if (emp.Id == 0 || string.IsNullOrWhiteSpace(iban)) return false;
        var clean = iban.Replace(" ", "").Trim().ToUpperInvariant();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var active = await _db.EmployeeBankAccounts
            .Where(b => b.EmployeeId == emp.Id && b.ValidFrom <= today && (b.ValidTo == null || b.ValidTo >= today))
            .OrderByDescending(b => b.IsHauptbank)
            .ThenByDescending(b => b.ValidFrom)
            .ToListAsync(ct);
        if (active.Any(b => string.Equals(b.Iban?.Replace(" ", ""), clean, StringComparison.OrdinalIgnoreCase)))
            return false;

        foreach (var old in active.Where(b => b.ValidFrom < today))
        {
            old.ValidTo = today.AddDays(-1);
            old.IsHauptbank = false;
            old.UpdatedAt = DateTime.UtcNow;
        }
        foreach (var old in active.Where(b => b.ValidFrom >= today))
        {
            old.IsHauptbank = false;
            old.UpdatedAt = DateTime.UtcNow;
        }

        _db.EmployeeBankAccounts.Add(new EmployeeBankAccount
        {
            EmployeeId = emp.Id,
            Iban = clean,
            IsHauptbank = true,
            AufteilungTyp = "VOLL",
            ValidFrom = today,
            Bemerkung = "easy@work Abgleich",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        return true;
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
        CancellationToken ct = default, int? jobGroupId = null,
        decimal? guaranteedHoursPerWeek = null, decimal? monthlySalaryFte = null)
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
            JobGroupId           = jobGroupId,
            ContractStartDate    = startDate,
            ContractEndDate      = endDate,
            IsActive             = isActive,
            EmploymentModel      = string.IsNullOrWhiteSpace(employmentModel) ? "UTP"    : employmentModel!.Trim(),
            SalaryType           = string.IsNullOrWhiteSpace(salaryType)      ? "hourly" : salaryType!.Trim(),
            ContractType         = contractType,
            JobTitle             = jobTitle,
            WeeklyHours          = weeklyHours,
            GuaranteedHoursPerWeek = guaranteedHoursPerWeek,
            EmploymentPercentage = percentage,
            HourlyRate           = hourlyRate,
            MonthlySalary        = monthlySalary,
            MonthlySalaryFte     = monthlySalaryFte,
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

    private static string NameDobKey(string? firstName, string? lastName, DateTime dateOfBirth)
    {
        static string Norm(string? s)
            => new string((s ?? "")
                .Trim()
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());

        return $"{Norm(firstName)}|{Norm(lastName)}|{dateOfBirth:yyyy-MM-dd}";
    }

    private static string NameDobKey(string? firstName, string? lastName, DateOnly dateOfBirth)
        => NameDobKey(firstName, lastName, dateOfBirth.ToDateTime(TimeOnly.MinValue));

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
    /// <summary>
    /// Best-effort aus den easy@work-Custom-Fields (Properties) in EINER Abfrage:
    /// Zivilstand (cf_marital_status) UND AHV-Nummer (cf_swiss_national_id).
    /// Unbekannt/Fehlschlag → null (bleibt manuell). Walter-Vorgabe 22.06.2026.
    /// </summary>
    private async Task<(string? Marital, string? Ahv)> FetchPropsInfoAsync(int customerId, int eawEmployeeId, CancellationToken ct)
    {
        try
        {
            var props = await _client.GetAllPropertiesAsync(customerId, eawEmployeeId, ct);
            string? Pick(params string[] needles) => props
                .Where(p =>
                {
                    var k = (p.Key ?? "").ToLowerInvariant();
                    return needles.Any(n => k.Contains(n));
                })
                .OrderByDescending(p => p.From ?? DateOnly.MinValue)
                .Select(p => p.Value)
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

            var marital = MapMaritalStatus(Pick("marital", "civil", "zivil", "familienstand", "family_status"));
            var ahv     = FormatAhv(Pick("swiss_national_id", "national_id", "ahv", "avs", "sozialvers"));
            return (marital, ahv);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Properties (Zivilstand/AHV) für easy@work-MA {Id} nicht abrufbar", eawEmployeeId); return (null, null); }
    }

    /// <summary>13-stellige AHV-Nr. (756…) → Format 756.XXXX.XXXX.XX. Sonst roh.</summary>
    private static string? FormatAhv(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var d = new string(v.Where(char.IsDigit).ToArray());
        if (d.Length != 13 || !d.StartsWith("756")) return v.Trim();
        return $"{d[..3]}.{d[3..7]}.{d[7..11]}.{d[11..]}";
    }

    /// <summary>Briefanrede aus Geschlecht + Vorname (Walter-Vorgabe 22.06.2026):
    /// weiblich „Liebe {Vorname}", männlich „Lieber {Vorname}". Sonst null.</summary>
    private static string? BuildLetterSalutation(string? gender, string? firstName)
    {
        var fn = (firstName ?? "").Trim();
        if (fn.Length == 0) return null;
        return (gender ?? "").Trim().ToLowerInvariant() switch
        {
            "female" => $"Liebe {fn}",
            "male"   => $"Lieber {fn}",
            _        => null
        };
    }

    /// <summary>Kanton aus der PLZ ableiten (AMTOVZ). Mehrdeutige PLZ → erster Treffer.</summary>
    private async Task<string?> LookupCantonAsync(string? plz, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plz)) return null;
        var p = plz.Trim();
        return await _db.SwissLocations.AsNoTracking()
            .Where(l => l.Plz4 == p)
            .Select(l => l.Kantonskuerzel)
            .FirstOrDefaultAsync(ct);
    }

    private async Task<(string? City, string? Canton, string? Error)> ResolveSwissLocationAsync(string? plz, string? eawCity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plz)) return (null, null, null);
        var p = plz.Trim();
        var locs = await _db.SwissLocations.AsNoTracking()
            .Where(l => l.Plz4 == p)
            .Select(l => new { l.Gemeindename, l.Kantonskuerzel })
            .ToListAsync(ct);
        if (locs.Count == 0)
            return (null, null, $"PLZ {p} wurde im Schweizer Ortschaftsverzeichnis nicht gefunden.");

        var match = locs.FirstOrDefault(l =>
            string.Equals(l.Gemeindename?.Trim(), eawCity?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match == null && locs.Select(l => l.Kantonskuerzel).Distinct().Count() == 1)
            match = locs.OrderBy(l => l.Gemeindename).First();
        if (match == null)
            return (null, null, $"PLZ {p} ist mehrdeutig und Ort '{eawCity}' konnte nicht zugeordnet werden.");
        return (match.Gemeindename, match.Kantonskuerzel, null);
    }

    private static string? MapMaritalStatus(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var s = v.Trim().ToLowerInvariant();
        // easy@work-Einzelbuchstaben-Codes (cf_marital_status): M/S/D/W/P.
        switch (s)
        {
            case "m": return "verheiratet";
            case "s": return "ledig";
            case "d": return "geschieden";
            case "w": return "verwitwet";
            case "p": return "eingetragene_partnerschaft";
        }
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

    private static bool IsValidEmail(string email)
        => System.Text.RegularExpressions.Regex.IsMatch(
            email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsValidAhv(string ahv)
    {
        var digits = new string((ahv ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length != 13 || !digits.StartsWith("756")) return false;
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            var d = digits[i] - '0';
            sum += (i % 2 == 0) ? d : d * 3;
        }
        var expected = (10 - (sum % 10)) % 10;
        return expected == digits[12] - '0';
    }

    private static bool IsValidIban(string iban)
    {
        var clean = new string((iban ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (clean.Length < 15 || clean.Length > 34) return false;
        if (!System.Text.RegularExpressions.Regex.IsMatch(clean, @"^[A-Z]{2}\d{2}[A-Z0-9]+$")) return false;
        var rearranged = clean[4..] + clean[..4];
        var remainder = 0;
        foreach (var ch in rearranged)
        {
            if (char.IsDigit(ch))
            {
                remainder = (remainder * 10 + (ch - '0')) % 97;
            }
            else if (ch is >= 'A' and <= 'Z')
            {
                var val = ch - 'A' + 10;
                remainder = (remainder * 10 + (val / 10)) % 97;
                remainder = (remainder * 10 + (val % 10)) % 97;
            }
            else return false;
        }
        return remainder == 1;
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
