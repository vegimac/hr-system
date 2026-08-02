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
    private readonly LohnEditLockService _editLock;

    public EasyAtWorkEmployeeSyncService(
        AppDbContext db,
        EasyAtWorkClient client,
        ILogger<EasyAtWorkEmployeeSyncService> log,
        LohnEditLockService editLock)
    {
        _db = db;
        _client = client;
        _log = log;
        _editLock = editLock;
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

        /// <summary>
        /// Tiefenimport-Modus (Walter-Vorgabe 08.07.2026): importiert NUR
        /// MA-Stammdaten, KEINE Verträge/Bankverbindungen. Zweck des
        /// Tiefenimports ist ausschliesslich, dass alte MA im System existieren,
        /// damit ihre Dokumente/Stempelzeiten angehängt werden können. Ersetzt
        /// die frühere Regel «Vertrags-Historie darf nie übersprungen werden»
        /// (23.06.2026) für diesen Import-Typ.
        /// </summary>
        public bool SkipContracts { get; set; } = false;
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

        /// <summary>
        /// Wiedereintritts-Duplikat (Walter 08.07.2026): dieselbe Person existiert
        /// in easy@work mehrfach (alter Datensatz + neuer nach Wiedereintritt) und
        /// beide matchen denselben Cowork-MA. true = dieser (ältere) Datensatz wird
        /// beim Commit KOMPLETT übersprungen — massgebend ist nur der neueste.
        /// </summary>
        public bool     SupersededDuplicate       { get; set; }
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
        /// <summary>Verträge, die wegen abgeschlossener Lohnperiode NICHT importiert wurden (Walter 29.06.2026).</summary>
        public List<string> SkippedContracts { get; set; } = new();
        /// <summary>
        /// Personalnummer-Kollisionen (Walter 29.06.2026): eine Nummer würde doppelt
        /// vergeben. Jede Zeile nennt die Nummer + beide Seiten (easy@work ↔ Cowork),
        /// damit Walter es in beiden Systemen prüfen kann. Ist diese Liste beim COMMIT
        /// nicht leer, wird NICHTS geschrieben (Blocked=true) — kein stilles Überspringen.
        /// </summary>
        public List<string> NumberConflicts { get; set; } = new();
        /// <summary>true, wenn der Commit wegen Nummern-Kollisionen blockiert wurde (kein Schreibvorgang).</summary>
        public bool Blocked { get; set; }
    }

    // ────────────────────────── Public API ──────────────────────────

    public Task<SyncResult> PreviewAsync(SyncRequest req, CancellationToken ct = default)
        => SyncCoreAsync(req, commit: false, progress: null, ct: ct);

    /// <summary>
    /// progress(done, total, phase) wird vom asynchronen Filial-Import (Hintergrund-
    /// Job) genutzt, um den Fortschritt zu melden. Bei NULL passiert nichts.
    /// </summary>
    public Task<SyncResult> CommitAsync(SyncRequest req, Action<int, int, string>? progress = null, CancellationToken ct = default)
        => SyncCoreAsync(req, commit: true, progress: progress, ct: ct);

    public sealed class SingleEmployeeSyncResult
    {
        public bool Success { get; set; }
        public int EmployeeId { get; set; }
        public int? EasyAtWorkEmployeeId { get; set; }
        public List<string> UpdatedFields { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> Notes { get; set; } = new();
        /// <summary>Verträge, die wegen abgeschlossener Lohnperiode NICHT importiert wurden (Walter 29.06.2026).</summary>
        public List<string> SkippedContracts { get; set; } = new();
        /// <summary>
        /// Offene OneCrew-Schwangerschaften, die aus easy@work stammten, aber
        /// dort nicht mehr als «Ja»+ET vorhanden sind. Frontend fragt nach dem
        /// Löschen — kein Auto-Delete (Walter 27.07.2026).
        /// </summary>
        public List<OrphanedPregnancyInfo> OrphanedPregnancies { get; set; } = new();
    }

    public sealed class OrphanedPregnancyInfo
    {
        public int Id { get; set; }
        public DateOnly Meldedatum { get; set; }
        public DateOnly ErrechneterTermin { get; set; }
    }

    private sealed class EmployeeMasterData
    {
        public int EawEmployeeId { get; set; }
        public string? Number { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        /// <summary>easy@work Nickname → Employee.ShortName.</summary>
        public string? ShortName { get; set; }
        public string? Gender { get; set; }
        public string? Salutation { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Ahv { get; set; }
        public string? MaritalStatus { get; set; }
        public string LanguageCode { get; set; } = "de";
        public string? LetterSalutation { get; set; }
        public string? Nationality { get; set; }
        public int? NationalityId { get; set; }
        public string? Street { get; set; }
        public string? ZipCode { get; set; }
        public string? City { get; set; }
        public string? CantonCode { get; set; }
        public string? Country { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public DateOnly? EntryDate { get; set; }
        public DateOnly? ExitDate { get; set; }
        public string? Iban { get; set; }
        /// <summary>Gültig-bis — IMMER gerechnet aus Beginn (nie easy-«to»).</summary>
        public DateOnly? NightWorkExamValidUntil { get; set; }
        /// <summary>Beginn 1:1 aus easy@work «from».</summary>
        public DateOnly? NightWorkExamIssued { get; set; }
        /// <summary>easy-«to» fehlt oder ≠ Soll (beide UTC-Lesarten geprüft).</summary>
        public bool NightWorkExamEasyMismatch { get; set; }
        /// <summary>Schwanger aus easy@work: «from» = gemeldet am (Walter 27.07.2026).</summary>
        public DateOnly? PregnantMeldedatum { get; set; }
        /// <summary>Schwanger aus easy@work: «to» = errechneter Geburtstermin.
        /// Schwangerschaftsbeginn = ET − 280 Tage (PregnancyFristCalculator).</summary>
        public DateOnly? PregnantErrechneterTermin { get; set; }
        /// <summary>Alle in easy@work erfassten Funktionen/Positionen (distinct).
        /// Mehr als eine = mehrdeutig → MA wird nicht importiert (Walter 05.07.2026).</summary>
        public List<string> Functions { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public List<string> Notes { get; set; } = new();
    }

    /// <summary>Ergebnis von <see cref="FetchPropsInfoAsync"/> — Custom Fields
    /// in EINER Properties-Abfrage (Zivilstand, AHV, Nachtarbeit, Seniorität,
    /// Schwangerschaft).</summary>
    private sealed class PropsInfo
    {
        public string? Marital { get; set; }
        public string? Ahv { get; set; }
        public DateOnly? NightWorkFrom { get; set; }
        public string? NightWorkToRaw { get; set; }
        public DateOnly? SeniorityDate { get; set; }
        public DateOnly? PregnantMeldedatum { get; set; }
        public DateOnly? PregnantErrechneterTermin { get; set; }
    }

    /// <summary>Vorab PARALLEL geholte easy@work-Detail-Daten pro MA (nur _client-
    /// Calls), damit die Vorschau nicht 3 Calls pro MA sequenziell macht.
    /// Walter-Vorgabe 05.07.2026.</summary>
    private sealed class DetailCache
    {
        public System.Collections.Concurrent.ConcurrentDictionary<int, PropsInfo> Props { get; } = new();
        public System.Collections.Concurrent.ConcurrentDictionary<int, List<string>> Functions { get; } = new();
        public System.Collections.Concurrent.ConcurrentDictionary<int, string?> Iban { get; } = new();
        // STRICT-Import (Walter 08.07.2026): Verträge + Tarife schon in der
        // VORSCHAU laden, damit Erfassungsfehler (Lohn fehlt, Überlappung,
        // Flex/Monat) pro MA als CONFLICT sichtbar sind — nicht erst im Commit.
        public System.Collections.Concurrent.ConcurrentDictionary<int, List<EawContract>> Contracts { get; } = new();
        public System.Collections.Concurrent.ConcurrentDictionary<int, List<EawPayRate>>  Rates     { get; } = new();
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
        // Performance (Walter 22.07.2026): die Filiale des MA ZUERST probieren —
        // der GetEmployeeById-Loop trifft dann fast immer beim 1. Call statt
        // alle gemappten Filialen sequenziell durchzuprobieren.
        var homeBranchId = emp.Employments
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => x.CompanyProfileId)
            .FirstOrDefault();
        if (homeBranchId.HasValue)
            mappings = mappings.OrderBy(m => m.CompanyProfileId == homeBranchId.Value ? 0 : 1)
                               .ThenBy(m => m.CompanyProfileId).ToList();

        EawEmployee? eaw = null;
        int? matchedCustomerId = null;
        foreach (var mapping in mappings)
        {
            try
            {
                eaw = await _client.GetEmployeeByIdAsync(mapping.EasyAtWorkCustomerId, emp.EasyAtWorkEmployeeId.Value, ct);
            }
            catch (Exception ex)
            {
                // API-Störung ≠ «MA nicht gefunden» — echten Grund melden und
                // abbrechen (Walter-Bug 09.07.2026).
                result.Errors.Add($"easy@work-API nicht erreichbar (Customer {mapping.EasyAtWorkCustomerId}): {ex.Message} — bitte später erneut versuchen.");
                return result;
            }
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
            // «alt»-Suffix (Pre-Mirus-Archiv, z.B. «9999356alt») ist eine reine
            // Cowork-Konvention — easy@work kennt nur die Original-Badge-Nummer.
            // Für die Suche entfernen (Walter-Bug 10.07.2026, Thu Chan Myae).
            var lookupNumber = System.Text.RegularExpressions.Regex.Replace(
                employeeNumber, "alt$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            var suffixStripped = !string.Equals(lookupNumber, employeeNumber, StringComparison.OrdinalIgnoreCase);
            foreach (var mapping in mappings)
            {
                EawEmployee? byNumber = null;
                if (lookupNumber.Length == 0) break;
                try { byNumber = await _client.GetEmployeeByNumberAsync(mapping.EasyAtWorkCustomerId, lookupNumber, ct); }
                catch (Exception ex)
                {
                    result.Notes.Add($"Nummer-Suche Customer {mapping.EasyAtWorkCustomerId}: API-Fehler ({ex.Message}).");
                }
                if (byNumber == null)
                {
                    try
                    {
                        var rows = await _client.GetAllEmployeesIncludingInactiveAsync(mapping.EasyAtWorkCustomerId, ct);
                        // NUR über die Personalnummer auflösen — die user_id ist nicht
                        // personen-eindeutig und könnte hier die FALSCHE Person greifen
                        // (Walter-Vorgabe 05.07.2026).
                        byNumber = rows.FirstOrDefault(x =>
                            string.Equals((x.Number ?? "").Trim(), lookupNumber, StringComparison.OrdinalIgnoreCase));
                    }
                    catch (Exception ex)
                    {
                        result.Notes.Add($"Legacy-ID-Reparatur Customer {mapping.EasyAtWorkCustomerId}: Employee-Liste nicht abrufbar ({ex.Message}).");
                    }
                }
                if (byNumber == null) continue;
                var numberMatches = string.Equals((byNumber.Number ?? "").Trim(), lookupNumber, StringComparison.OrdinalIgnoreCase);
                if (!numberMatches) continue;

                // Beim «alt»-MA zusätzlich die IDENTITÄT absichern: die Original-
                // nummer könnte theoretisch inzwischen neu vergeben sein. Person
                // gilt als dieselbe, wenn die gespeicherte ID (employee.id ODER
                // user_id) passt — sonst der Name (case-insensitive).
                if (suffixStripped)
                {
                    var idMatch = byNumber.Id == emp.EasyAtWorkEmployeeId.Value
                               || byNumber.UserId == emp.EasyAtWorkEmployeeId.Value;
                    var nameMatch =
                        string.Equals((byNumber.FirstName ?? "").Trim(), (emp.FirstName ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                     && string.Equals((byNumber.LastName ?? "").Trim(), (emp.LastName ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                    if (!idMatch && !nameMatch)
                    {
                        result.Notes.Add($"Nummer {lookupNumber} in Customer {mapping.EasyAtWorkCustomerId} gefunden, aber weder ID noch Name passen ({byNumber.FirstName} {byNumber.LastName}) — vermutlich neu vergebene Nummer, übersprungen.");
                        continue;
                    }
                }

                eaw = byNumber;
                matchedCustomerId = mapping.EasyAtWorkCustomerId;
                result.Notes.Add($"Gespeicherte easy@work-ID {emp.EasyAtWorkEmployeeId.Value} war veraltet/falsch; über die Personalnummer {lookupNumber} auf employee.id {eaw.Id} korrigiert (Customer {mapping.EasyAtWorkCustomerId}).");
                break;
            }
        }
        if (eaw == null)
        {
            result.Errors.Add($"Mitarbeiter in easy@work nicht gefunden (gespeicherte ID {emp.EasyAtWorkEmployeeId.Value}; employee.id-Suche und Legacy-user_id-Reparatur über alle gemappten Filialen ohne Treffer).");
            return result;
        }

        // ── Aktiv-Vorrang (Walter 12.07.2026, Alaa/Rasakumary): ist der
        //    aufgelöste Datensatz BEENDET (to in der Vergangenheit), die
        //    anderen gemappten Filialen nach einem AKTIVEN Datensatz derselben
        //    Person absuchen (über Haupt- UND Alias-Nummern, «alt»-Suffix
        //    gestrippt). Der aktive Datensatz gewinnt: er definiert Identität,
        //    Anker und Hauptnummer — sonst bleibt der Sync ewig am toten
        //    Filial-Datensatz hängen. Identitäts-Guard: user_id ODER Name
        //    ODER Vorname+Geburtsdatum (Nummern könnten neu vergeben sein). ──
        var eawTodayD = DateOnly.FromDateTime(DateTime.Today);
        if (eaw.To.HasValue && eaw.To.Value < eawTodayD)
        {
            var candNumbers = new List<string>();
            void AddCand(string? x)
            {
                var t = System.Text.RegularExpressions.Regex.Replace(
                    (x ?? "").Trim(), "alt$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                if (t.Length > 0 && !candNumbers.Contains(t, StringComparer.OrdinalIgnoreCase)) candNumbers.Add(t);
            }
            AddCand(emp.EmployeeNumber);
            foreach (var a in await _db.EmployeeNumberAliases.AsNoTracking()
                         .Where(a => a.EmployeeId == emp.Id).Select(a => a.Number).ToListAsync(ct))
                AddCand(a);

            bool aktivGefunden = false;
            foreach (var mapping in mappings)
            {
                EawEmployee? aktiv = null;
                List<EawEmployee>? fullList = null; // lazy — nur wenn n+Nummer nichts liefert
                foreach (var num in candNumbers)
                {
                    EawEmployee? cand = null;
                    try { cand = await _client.GetEmployeeByNumberAsync(mapping.EasyAtWorkCustomerId, num, ct); }
                    catch { /* best-effort — Kandidaten-Suche darf den Sync nie brechen */ }
                    if (cand == null)
                    {
                        // Der «n+Nummer»-Einzel-Endpoint greift beim API-Anbieter
                        // nicht überall sauber (gleiches Problem wie bei der
                        // Legacy-ID-Reparatur oben) — Fallback: Employee-Liste
                        // des Customers, EINMAL pro Filiale geladen.
                        if (fullList == null)
                        {
                            try { fullList = await _client.GetAllEmployeesIncludingInactiveAsync(mapping.EasyAtWorkCustomerId, ct); }
                            catch { fullList = new List<EawEmployee>(); }
                        }
                        cand = fullList.FirstOrDefault(x =>
                            string.Equals((x.Number ?? "").Trim(), num, StringComparison.OrdinalIgnoreCase));
                    }
                    if (cand == null || cand.Id == eaw.Id) continue;
                    if (cand.To.HasValue && cand.To.Value < eawTodayD) continue; // auch beendet
                    bool sameUser = cand.UserId.HasValue && cand.UserId == eaw.UserId;
                    bool sameName =
                        string.Equals((cand.FirstName ?? "").Trim(), (emp.FirstName ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
                     && string.Equals((cand.LastName ?? "").Trim(), (emp.LastName ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                    bool sameDobFirst = cand.BirthDate.HasValue && emp.DateOfBirth.HasValue
                     && cand.BirthDate.Value == DateOnly.FromDateTime(emp.DateOfBirth.Value)
                     && string.Equals((cand.FirstName ?? "").Trim(), (emp.FirstName ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
                    if (!sameUser && !sameName && !sameDobFirst) continue;
                    aktiv = cand;
                    break;
                }
                if (aktiv != null)
                {
                    result.Notes.Add($"Aktiv-Vorrang: gespeicherter easy@work-Datensatz (Nummer {eaw.Number}, Austritt {eaw.To:dd.MM.yyyy}) ist beendet — aktiver Datensatz Nummer {aktiv.Number} in Customer {mapping.EasyAtWorkCustomerId} übernimmt (Anker + Hauptnummer).");
                    eaw = aktiv;
                    matchedCustomerId = mapping.EasyAtWorkCustomerId;
                    aktivGefunden = true;
                    break;
                }
            }
            if (!aktivGefunden)
                result.Notes.Add($"Hinweis: der verknüpfte easy@work-Datensatz (Nummer {eaw.Number}) ist seit {eaw.To:dd.MM.yyyy} beendet — über die Nummern {string.Join(", ", candNumbers)} wurde in keiner gemappten Filiale ein aktiver Datensatz gefunden.");
        }
        result.EasyAtWorkEmployeeId = eaw.Id;

        var natByCode = await _db.Nationalities.AsNoTracking()
            .ToDictionaryAsync(n => (n.Code ?? "").ToUpperInvariant(), n => n.Id, ct);
        var master = await BuildMasterDataAsync(matchedCustomerId!.Value, eaw, natByCode, includeDetailCalls: true, ct);
        result.Notes.AddRange(master.Notes);
        result.Errors.AddRange(master.Errors);
        if (result.Errors.Count > 0) return result;

        // Mehrdeutige Funktion (mehr als eine in easy@work erfasst) → MA NICHT
        // importieren/anpassen — EXAKT dieselbe Logik wie der Massenimport
        // (Walter-Vorgabe 05.07.2026). Früher Abbruch VOR jeder Feld-/Vertrags-
        // Änderung, damit der Einzelimport identisch reagiert.
        if (master.Functions.Count > 1)
        {
            result.Errors.Add($"{master.Functions.Count} Funktionen gefunden: {string.Join(", ", master.Functions)} — mehrdeutig, MA wird nicht importiert/angepasst. Bitte in easy@work auf eine Funktion reduzieren.");
            return result;
        }

        void SetString(string label, string? current, string? next, Action<string?> set, bool allowNull = true, bool exactCase = false)
        {
            var value = string.IsNullOrWhiteSpace(next) ? null : next.Trim();
            if (value == null && !allowNull) return;
            // Namen case-SENSITIV vergleichen (Walter 10.07.2026): «KITANOVSKA» ≠
            // «Kitanovska» — easy@work-Schreibweise ist führend (Lohnzettel!).
            var cmp = exactCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            if (string.Equals(current?.Trim(), value, cmp)) return;
            set(value);
            result.UpdatedFields.Add(label);
        }

        SetString("Vorname", emp.FirstName, master.FirstName, v => emp.FirstName = v ?? emp.FirstName, allowNull: false, exactCase: true);
        SetString("Nachname", emp.LastName, master.LastName, v => emp.LastName = v ?? emp.LastName, allowNull: false, exactCase: true);
        // Kurzname = easy@work Nickname (Walter 17.07.2026) — UI nur Anzeige.
        SetString("Kurzname", emp.ShortName, master.ShortName, v => emp.ShortName = v);
        SetString("Geschlecht", emp.Gender, master.Gender, v => emp.Gender = v);
        SetString("Anrede", emp.Salutation, master.Salutation, v => emp.Salutation = v);
        SetString("Briefanrede", emp.LetterSalutation, master.LetterSalutation, v => emp.LetterSalutation = v);
        if (master.DateOfBirth.HasValue)
        {
            var dob = master.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue);
            if (emp.DateOfBirth?.Date != dob.Date) { emp.DateOfBirth = dob; result.UpdatedFields.Add("Geburtsdatum"); }
        }
        SetString("AHV-Nummer", emp.SocialSecurityNumber, master.Ahv, v => emp.SocialSecurityNumber = v);
        SetString("Zivilstand", emp.MaritalStatus, master.MaritalStatus, v => emp.MaritalStatus = v);
        SetString("Sprache", emp.LanguageCode, master.LanguageCode, v => emp.LanguageCode = v);
        SetString("Nationalität", emp.Nationality, master.Nationality, v => emp.Nationality = v);
        if (master.NationalityId.HasValue && emp.NationalityId != master.NationalityId.Value)
        {
            emp.NationalityId = master.NationalityId.Value;
            if (!result.UpdatedFields.Contains("Nationalität")) result.UpdatedFields.Add("Nationalität");
        }
        SetString("Strasse", emp.Street, master.Street, v => emp.Street = v);
        SetString("PLZ", emp.ZipCode, master.ZipCode, v => emp.ZipCode = v);
        SetString("Ort", emp.City, master.City, v => emp.City = v);
        SetString("Kanton", emp.CantonCode, master.CantonCode, v => emp.CantonCode = v);
        SetString("Land", emp.Country, master.Country, v => emp.Country = v);
        SetString("Telefon", emp.PhoneMobile, master.Phone, v => emp.PhoneMobile = v);
        SetString("E-Mail", emp.Email, master.Email, v => emp.Email = v);
        if (master.EntryDate.HasValue)
        {
            var entry = master.EntryDate.Value.ToDateTime(TimeOnly.MinValue);
            if (emp.EntryDate?.Date != entry.Date) { emp.EntryDate = entry; result.UpdatedFields.Add("Eintrittsdatum"); }
        }
        if (emp.EasyAtWorkEmployeeId != eaw.Id)
        {
            emp.EasyAtWorkEmployeeId = eaw.Id;
            result.UpdatedFields.Add("easy@work-ID");
        }
        // Hauptnummer folgt dem AKTIVEN Datensatz (Walter 12.07.2026): trägt der
        // (aktive) easy@work-Datensatz eine andere Nummer als unsere Hauptnummer,
        // wird getauscht — die bisherige Hauptnummer wandert als Alias in die
        // Historie (bzw. Rollen-Tausch, wenn die neue schon Alias war).
        // Kollisionsschutz: gehört die Nummer einem anderen MA, nur Hinweis.
        // Archiv-«alt» vs. nackte easy@work-Nummer = dieselbe Badge → behalten
        // (Walter-Bug 18.07.2026, Sweeba Akhtar).
        {
            var eawNum = (eaw.Number ?? "").Trim();
            var curNum = (emp.EmployeeNumber ?? "").Trim();
            bool eawAktivJetzt = !eaw.To.HasValue || eaw.To.Value >= DateOnly.FromDateTime(DateTime.Today);
            if (eawAktivJetzt && eawNum.Length > 0
                && !string.Equals(eawNum, curNum, StringComparison.OrdinalIgnoreCase)
                && !IsSameNumberIgnoringAlt(curNum, eawNum))
            {
                bool besetzt = await _db.Employees.AnyAsync(
                    x => x.Id != emp.Id && !x.IsHidden && x.EmployeeNumber == eawNum, ct);
                if (besetzt)
                    result.Notes.Add($"⚠ Nummer {eawNum} gehört bereits einem anderen MA — Hauptnummer bleibt {emp.EmployeeNumber}, bitte Dublette klären.");
                else
                {
                    var alteNr = emp.EmployeeNumber;
                    SaveNumberChange(_db, emp, eawNum);
                    result.UpdatedFields.Add($"Personalnummer ({alteNr} → {eawNum})");
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(master.Iban))
        {
            var changed = await EnsureBankAccountFromEasyWorkAsync(emp, master.Iban, ct);
            if (changed) result.UpdatedFields.Add("IBAN");
        }

        // Nachtarbeit: Beginn aus easy, Ende gerechnet, Mismatch-Flag (Walter 26.07.2026).
        if (master.NightWorkExamIssued.HasValue)
        {
            var issuedDt = master.NightWorkExamIssued.Value.ToDateTime(TimeOnly.MinValue);
            var untilDt  = master.NightWorkExamValidUntil!.Value.ToDateTime(TimeOnly.MinValue);
            if (emp.NightWorkExamIssued?.Date != issuedDt.Date
                || emp.NightWorkExamValidUntil?.Date != untilDt.Date
                || emp.NightWorkExamEasyMismatch != master.NightWorkExamEasyMismatch)
            {
                emp.NightWorkExamIssued = issuedDt;
                emp.NightWorkExamValidUntil = untilDt;
                emp.NightWorkExamEasyMismatch = master.NightWorkExamEasyMismatch;
                if (!result.UpdatedFields.Contains("Nachtarbeit")) result.UpdatedFields.Add("Nachtarbeit");
            }
        }

        // Austritt / Aktiv aus easy@work (Walter-Vorgabe 05.07.2026): der Einzel-Sync
        // hat den Austritt bisher NIE angefasst → ein stehengebliebenes (stale)
        // Austrittsdatum blieb ewig kleben. eaw.To ist der Austritt DIESER Filiale.
        var nowD = DateOnly.FromDateTime(DateTime.Today);
        if (!eaw.To.HasValue || eaw.To.Value >= nowD)
        {
            // In easy@work (noch) aktiv → Person aktiv, KEIN Austrittsdatum.
            if (!emp.IsActive) { emp.IsActive = true; result.UpdatedFields.Add("Aktiv"); }
            if (emp.ExitDate.HasValue) { emp.ExitDate = null; result.UpdatedFields.Add("Austrittsdatum"); }
        }
        else
        {
            // easy@work-Austritt in der Vergangenheit. Nur zum Personen-Austritt machen,
            // wenn der MA KEINEN offenen Vertrag mehr hat. Hat er noch einen offenen
            // Vertrag (Filialwechsel), wird ein stehengebliebener Austritt AKTIV entfernt.
            var todayDt = DateTime.Today;
            bool hasOpenContract = await _db.Employments.AnyAsync(em => em.EmployeeId == emp.Id
                && (em.ContractEndDate == null || em.ContractEndDate >= todayDt), ct);
            if (hasOpenContract)
            {
                if (emp.ExitDate.HasValue) { emp.ExitDate = null; result.UpdatedFields.Add("Austrittsdatum"); }
                if (!emp.IsActive) { emp.IsActive = true; result.UpdatedFields.Add("Aktiv"); }
            }
            else
            {
                var exit = eaw.To.Value.ToDateTime(TimeOnly.MinValue);
                if (emp.ExitDate?.Date != exit.Date) { emp.ExitDate = exit; result.UpdatedFields.Add("Austrittsdatum"); }
                if (emp.IsActive) { emp.IsActive = false; result.UpdatedFields.Add("Aktiv"); }
            }
        }

        await _db.SaveChangesAsync(ct);

        // ── Verträge aus easy@work mitziehen (Walter-Vorgabe 29.06.2026) ──────
        // Der Einzel-Button holt jetzt nicht nur Stammdaten, sondern auch die
        // komplette Vertrags-/Lohnhistorie — gleiche Logik wie der Filial-Sync,
        // inkl. Abschluss-Schutz (Verträge in geschlossenen Lohnperioden werden
        // übersprungen + gemeldet).
        try
        {
            var custId = matchedCustomerId!.Value;
            var cpId   = mappings.First(m => m.EasyAtWorkCustomerId == custId).CompanyProfileId;

            // Performance (Walter 22.07.2026): die drei unabhängigen API-Calls
            // parallel laden statt sequenziell (HttpClient ist thread-sicher;
            // _db wird hier nicht berührt).
            var contractsTask = _client.GetContractsAsync(custId, eaw.Id, ct);
            var ratesTask     = _client.GetPayRatesAsync(custId, eaw.Id, ct);
            var positionsTask = _client.GetPositionsAsync(custId, eaw.Id, ct);
            var contracts = (await contractsTask)?.Data ?? new();
            var rates     = (await ratesTask)?.Data ?? new();

            // Funktion/JobGroup (Kader-Flag → Modell) aus /positions.
            string? posName = null;
            try { posName = (await positionsTask)?.Data?.FirstOrDefault()?.Name; }
            catch (Exception ex) { result.Notes.Add($"Position aus easy@work nicht abrufbar ({ex.Message})."); }
            int? jgId = null; string? jgCode = null; bool isKader = false;
            if (!string.IsNullOrWhiteSpace(posName))
            {
                var p = posName.Trim();
                var jg = (await _db.JobGroups.AsNoTracking().ToListAsync(ct))
                    .FirstOrDefault(g => !string.IsNullOrWhiteSpace(g.Code)
                                         && string.Equals(g.Code.Trim(), p, StringComparison.OrdinalIgnoreCase));
                if (jg != null) { jgId = jg.Id; jgCode = jg.Code; isKader = jg.IsKader; }
            }

            var activeAt = DateOnly.FromDateTime(DateTime.Today);
            var timeline = BuildEmploymentTimeline(contracts, rates, activeAt, isKader);
            // Verträge: Sperre erst bei Definitiv abgeschlossen (Walter 01.08.2026).
            var firstAllowed = await _editLock.GetFirstAllowedDateForContractsAsync(cpId);

            // STRICT (Walter 08.07.2026): überlappende AKTIVE Verträge in easy@work
            // → KEIN Vertragsimport für diesen MA. Historische Überlappungen egal.
            var overlapErr = ValidateContractOverlaps(contracts, activeAt);
            if (overlapErr != null)
            {
                result.SkippedContracts.Add(overlapErr);
            }
            else
            {
                await SyncEmploymentTimelineAsync(_db, emp, cpId, timeline, jgId, jgCode, eaw.To,
                    firstAllowed, result.SkippedContracts, result.Notes, ct);

                var contractChanged = _db.ChangeTracker.Entries<Employment>()
                    .Any(e => e.State == EntityState.Added || e.State == EntityState.Modified);
                await _db.SaveChangesAsync(ct);
                if (contractChanged) result.UpdatedFields.Add("Verträge");
            }
        }
        catch (Exception ex)
        {
            result.Notes.Add($"Vertrags-Sync übersprungen: {ex.Message}");
            _log.LogWarning(ex, "Einzel-MA Vertrags-Sync für Employee {Id} fehlgeschlagen.", emp.Id);
        }

        // ── Austritt NACH dem Vertrags-Sync bewerten (Walter-Bug 15.07.2026) ──
        // Zwei Luecken der 05.07.-Logik: (a) ein ZUKUENFTIGER Austritt
        // («Eingestellt bis» gesetzt, Datum noch nicht erreicht) wurde nie als
        // Austrittsdatum uebernommen; (b) die Pruefung lief VOR dem Vertrags-
        // Sync — der noch offene Vertrag blockierte die Uebernahme, obwohl der
        // Sync ihn gleich danach beendete. Jetzt: nach dem Vertrags-Sync.
        try
        {
            if (await ApplyExitAfterContractSyncAsync(emp, eaw.To, ct))
            {
                await _db.SaveChangesAsync(ct);
                if (!result.UpdatedFields.Contains("Austrittsdatum")) result.UpdatedFields.Add("Austrittsdatum");
            }
        }
        catch (Exception ex)
        {
            result.Notes.Add($"Austritts-Bewertung übersprungen: {ex.Message}");
        }

        // ── Verfügbarkeit aus easy@work mitziehen (Walter-Vorgabe 09.07.2026) ──
        // Best-effort: ein Fehler hier bricht den restlichen Sync nicht ab.
        try
        {
            var changed = await SyncAvailabilitiesAsync(emp, matchedCustomerId!.Value, eaw.Id, result.Notes, ct);
            if (changed) result.UpdatedFields.Add("Verfügbarkeit");
        }
        catch (Exception ex)
        {
            result.Notes.Add($"Verfügbarkeits-Sync übersprungen: {ex.Message}");
            _log.LogWarning(ex, "Einzel-MA Verfügbarkeits-Sync für Employee {Id} fehlgeschlagen.", emp.Id);
        }

        // ── Schwangerschaft aus easy@work (Walter-Vorgabe 27.07.2026) ──
        // Custom Field «Schwanger»: from = gemeldet am, to = ET.
        // Beginn = ET − 280 Tage (PregnancyFristCalculator). Best-effort.
        // Wenn in easy gelöscht: Orphans melden, Frontend fragt nach Löschen.
        try
        {
            if (await SyncPregnancyFromEasyAsync(
                    emp, master.PregnantMeldedatum, master.PregnantErrechneterTermin,
                    result.Notes, result.OrphanedPregnancies, ct))
                result.UpdatedFields.Add("Schwangerschaft");
        }
        catch (Exception ex)
        {
            result.Notes.Add($"Schwangerschafts-Sync übersprungen: {ex.Message}");
            _log.LogWarning(ex, "Einzel-MA Schwangerschafts-Sync für Employee {Id} fehlgeschlagen.", emp.Id);
        }

        result.Success = true;
        if (result.UpdatedFields.Count == 0 && result.SkippedContracts.Count == 0)
            result.Notes.Add("Keine Änderungen — Cowork war bereits aktuell.");
        return result;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Verfügbarkeits-Sync (Walter-Vorgabe 09.07.2026)
    //  easy@work availabilities + /days → EmployeeAvailability + Slots.
    //  Mapping (aus echtem Dump MA 580009 verifiziert):
    //    • availability.from/to (UTC) → ValidFrom/ValidTo (lokal, Europe/Zurich)
    //    • day.from (UTC) → lokales Datum → Wochentag der Slot-Zeile
    //    • day.offset/length = Sekunden ab LOKALEM Tagesbeginn; whole_day → ganztags
    //    • FEHLENDER Wochentag im Muster = nicht verfügbar (= unser Slot-Modell)
    //    • alle 7 Tage ganztags → Type 'unrestricted', sonst 'table'
    //  Upsert über EasyAtWorkAvailabilityId: sync-erzeugte Versionen werden
    //  aktualisiert/entfernt, MANUELL erfasste (Id NULL) bleiben unangetastet.
    // ═════════════════════════════════════════════════════════════════════
    private async Task<bool> SyncAvailabilitiesAsync(
        Employee emp, int customerId, int eawEmployeeId, List<string> notes, CancellationToken ct)
    {
        var eawList = (await _client.GetAvailabilitiesAsync(customerId, eawEmployeeId, ct))?.Data ?? new();
        var relevant = eawList.Where(a => a.Active != false && !a.IsDeleted && a.From.HasValue).ToList();

        var existing = await _db.EmployeeAvailabilities
            .Include(a => a.Slots)
            .Where(a => a.EmployeeId == emp.Id && a.EasyAtWorkAvailabilityId != null)
            .ToListAsync(ct);

        var changed = false;
        var seenIds = new HashSet<long>();

        foreach (var eawAv in relevant)
        {
            seenIds.Add(eawAv.Id);
            List<EawAvailabilityDay> days;
            try { days = (await _client.GetAvailabilityDaysAsync(customerId, eawEmployeeId, eawAv.Id, ct))?.Data ?? new(); }
            catch (Exception ex)
            {
                notes.Add($"Verfügbarkeit {eawAv.Id}: Tage nicht abrufbar ({ex.Message}) — übersprungen.");
                continue;
            }
            days = days.Where(d => !d.IsDeleted).ToList();

            // Wochentag + Zeitfenster pro Muster-Tag
            var perDay = new List<(DayOfWeek Dow, TimeOnly? Von, TimeOnly? Bis, bool Ganz)>();
            foreach (var d in days)
            {
                if (d.LocalDate is not DateOnly ld) continue;
                var dow = ld.DayOfWeek;
                if (d.WholeDay || (d.Offset == 0 && d.Length >= 86400))
                {
                    perDay.Add((dow, null, null, true));
                    continue;
                }
                var vonSec = Math.Clamp(d.Offset, 0, 86399);
                var bisSec = Math.Clamp(d.Offset + d.Length, 0, 86400) % 86400; // 24:00 → 00:00
                perDay.Add((dow,
                    TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(vonSec)),
                    TimeOnly.FromTimeSpan(TimeSpan.FromSeconds(bisSec)), false));
            }

            var isUnrestricted = perDay.Count(p => p.Ganz) >= 7
                                 && perDay.Select(p => p.Dow).Distinct().Count() == 7;

            // Gleiche Zeitfenster zu EINER Slot-Zeile mit Wochentag-Flags gruppieren
            var newSlots = new List<EmployeeAvailabilitySlot>();
            if (!isUnrestricted)
            {
                var sort = 0;
                foreach (var grp in perDay.GroupBy(p => (p.Von, p.Bis, p.Ganz))
                                          .OrderBy(g => g.Key.Ganz ? TimeOnly.MinValue : (g.Key.Von ?? TimeOnly.MinValue)))
                {
                    var s = new EmployeeAvailabilitySlot
                    {
                        Von = grp.Key.Ganz ? null : grp.Key.Von,
                        Bis = grp.Key.Ganz ? null : grp.Key.Bis,
                        SortOrder = sort++,
                    };
                    foreach (var p in grp)
                        switch (p.Dow)
                        {
                            case DayOfWeek.Monday:    s.Mon = true; break;
                            case DayOfWeek.Tuesday:   s.Tue = true; break;
                            case DayOfWeek.Wednesday: s.Wed = true; break;
                            case DayOfWeek.Thursday:  s.Thu = true; break;
                            case DayOfWeek.Friday:    s.Fri = true; break;
                            case DayOfWeek.Saturday:  s.Sat = true; break;
                            case DayOfWeek.Sunday:    s.Sun = true; break;
                        }
                    newSlots.Add(s);
                }
            }

            var wunschTage = eawAv.WorkDays is { Count: 1 } ? eawAv.WorkDays[0] : (int?)null;
            var bemerkung = "easy@work-Sync" + (wunschTage.HasValue ? $" · Wunsch-Arbeitstage/Woche: {wunschTage}" : "");
            var type = isUnrestricted ? "unrestricted" : "table";
            var validFrom = eawAv.From!.Value;
            var validTo = eawAv.To;

            string Sig(string ty, DateOnly vf, DateOnly? vt, IEnumerable<EmployeeAvailabilitySlot> slots) =>
                ty + "|" + vf + "|" + vt + "|" + string.Join(";", slots
                    .OrderBy(s => s.SortOrder)
                    .Select(s => $"{s.Von}-{s.Bis}-{(s.Mon?1:0)}{(s.Tue?1:0)}{(s.Wed?1:0)}{(s.Thu?1:0)}{(s.Fri?1:0)}{(s.Sat?1:0)}{(s.Sun?1:0)}"));

            var match = existing.FirstOrDefault(a => a.EasyAtWorkAvailabilityId == eawAv.Id);
            if (match == null)
            {
                _db.EmployeeAvailabilities.Add(new EmployeeAvailability
                {
                    EmployeeId = emp.Id,
                    Type = type,
                    ValidFrom = validFrom,
                    ValidTo = validTo,
                    Bemerkung = bemerkung,
                    EasyAtWorkAvailabilityId = eawAv.Id,
                    Slots = newSlots,
                });
                changed = true;
            }
            else if (Sig(match.Type, match.ValidFrom, match.ValidTo, match.Slots)
                     != Sig(type, validFrom, validTo, newSlots))
            {
                match.Type = type;
                match.ValidFrom = validFrom;
                match.ValidTo = validTo;
                match.Bemerkung = bemerkung;
                _db.EmployeeAvailabilitySlots.RemoveRange(match.Slots);
                match.Slots = newSlots;
                changed = true;
            }
        }

        // In easy@work gelöschte/deaktivierte Verfügbarkeiten: sync-erzeugte
        // Spiegel-Version bei uns entfernen (manuelle bleiben).
        foreach (var orphan in existing.Where(a => !seenIds.Contains(a.EasyAtWorkAvailabilityId!.Value)))
        {
            _db.EmployeeAvailabilities.Remove(orphan);
            changed = true;
        }

        if (changed) await _db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>
    /// Schwangerschaft aus easy@work übernehmen (Walter 27.07.2026).
    /// <paramref name="meldedatum"/> = Property «from», <paramref name="et"/> =
    /// Property «to» (errechneter Geburtstermin). Beginn = ET − 280 Tage
    /// (nicht gespeichert — <see cref="PregnancyFristCalculator"/>).
    /// Fehlt die Schwangerschaft in easy@work und existiert in OneCrew noch
    /// ein aus easy synchronisierter offener Eintrag → Orphans melden
    /// (Frontend: «In OneCrew löschen?» — kein Auto-Delete).
    /// </summary>
    private async Task<bool> SyncPregnancyFromEasyAsync(
        Employee emp,
        DateOnly? meldedatum,
        DateOnly? et,
        List<string> notes,
        List<OrphanedPregnancyInfo>? orphanSink,
        CancellationToken ct)
    {
        if (!et.HasValue)
        {
            var orphans = await FindEasySyncedOpenPregnanciesAsync(emp.Id, ct);
            if (orphans.Count == 0) return false;
            notes.Add(
                $"Schwangerschaft in easy@work gelöscht — in OneCrew noch vorhanden " +
                $"(ET {string.Join(", ", orphans.Select(o => o.ErrechneterTermin.ToString("dd.MM.yyyy")))}).");
            orphanSink?.AddRange(orphans);
            return false;
        }

        var melde = meldedatum ?? DateOnly.FromDateTime(DateTime.Today);
        if (melde > et.Value)
        {
            notes.Add($"⚠ Schwangerschaft in easy@work: gemeldet am ({melde:dd.MM.yyyy}) liegt nach dem ET ({et.Value:dd.MM.yyyy}) — nicht übernommen.");
            return false;
        }

        var open = await _db.EmployeePregnancies
            .Where(p => p.EmployeeId == emp.Id && p.IsActive && p.Geburtsdatum == null)
            .OrderByDescending(p => p.ErrechneterTermin)
            .ToListAsync(ct);

        // Gleicher ET → Update; sonst offene Schwangerschaft (ET-Korrektur);
        // sonst neu anlegen.
        var match = open.FirstOrDefault(p => p.ErrechneterTermin == et.Value)
                 ?? open.FirstOrDefault();

        var beginn = et.Value.AddDays(-280);
        var marker = EasyAtWorkPregnancyMapper.SyncBemerkungMarker;
        if (match != null)
        {
            if (match.Meldedatum == melde && match.ErrechneterTermin == et.Value)
                return false;
            match.Meldedatum = melde;
            match.ErrechneterTermin = et.Value;
            match.UpdatedAt = DateTime.Now;
            if (string.IsNullOrWhiteSpace(match.Bemerkung))
                match.Bemerkung = marker;
            await _db.SaveChangesAsync(ct);
            notes.Add($"Schwangerschaft aktualisiert (gemeldet {melde:dd.MM.yyyy}, ET {et.Value:dd.MM.yyyy}, Beginn {beginn:dd.MM.yyyy}).");
            return true;
        }

        _db.EmployeePregnancies.Add(new EmployeePregnancy
        {
            EmployeeId = emp.Id,
            Meldedatum = melde,
            ErrechneterTermin = et.Value,
            Bemerkung = marker,
            IsActive = true,
            CreatedAt = DateTime.Now,
        });
        await _db.SaveChangesAsync(ct);
        notes.Add($"Schwangerschaft übernommen (gemeldet {melde:dd.MM.yyyy}, ET {et.Value:dd.MM.yyyy}, Beginn {beginn:dd.MM.yyyy}).");
        return true;
    }

    /// <summary>
    /// Offene, aus easy@work synchronisierte Schwangerschaften ohne Geburt —
    /// Kandidaten für «in easy gelöscht → in OneCrew löschen?».
    /// </summary>
    private async Task<List<OrphanedPregnancyInfo>> FindEasySyncedOpenPregnanciesAsync(
        int employeeId, CancellationToken ct)
    {
        var open = await _db.EmployeePregnancies
            .AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.IsActive && p.Geburtsdatum == null)
            .OrderByDescending(p => p.ErrechneterTermin)
            .ToListAsync(ct);
        return open
            .Where(p => EasyAtWorkPregnancyMapper.IsSyncedFromEasy(p.Bemerkung))
            .Select(p => new OrphanedPregnancyInfo
            {
                Id = p.Id,
                Meldedatum = p.Meldedatum,
                ErrechneterTermin = p.ErrechneterTermin,
            })
            .ToList();
    }

    private async Task<EmployeeMasterData> BuildMasterDataAsync(
        int customerId, EawEmployee eaw, Dictionary<string, int> natByCode,
        bool includeDetailCalls, CancellationToken ct, DetailCache? cache = null)
    {
        var data = new EmployeeMasterData
        {
            EawEmployeeId = eaw.Id,
            Number = string.IsNullOrWhiteSpace(eaw.Number) ? null : eaw.Number.Trim(),
            FirstName = string.IsNullOrWhiteSpace(eaw.FirstName) ? null : eaw.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(eaw.LastName) ? null : eaw.LastName.Trim(),
            ShortName = string.IsNullOrWhiteSpace(eaw.Nickname) ? null : eaw.Nickname.Trim(),
            Gender = NormalizeGender(eaw.Gender),
            DateOfBirth = eaw.BirthDate,
            Street = NormalizeStreet(eaw.Address1, eaw.Address2),
            ZipCode = string.IsNullOrWhiteSpace(eaw.PostalCode) ? null : eaw.PostalCode.Trim(),
            Phone = NormalizePhone(eaw.Phone),
            Email = string.IsNullOrWhiteSpace(eaw.Email) ? null : eaw.Email.Trim().ToLowerInvariant(),
            // Fallback «Eingestellt seit» (employee.from) — Primärquelle ist
            // «Datum der Betriebszugehörigkeit» aus den Properties (unten).
            EntryDate = eaw.From,
            ExitDate = eaw.To,
            LanguageCode = "de",
        };

        data.Salutation = SalutationFromGender(eaw.Gender);
        data.LetterSalutation = BuildLetterSalutation(data.Gender, data.FirstName);
        data.Nationality = ResolveNationalityCode(eaw.Nationality, natByCode);
        if (!string.IsNullOrWhiteSpace(data.Nationality) && natByCode.TryGetValue(data.Nationality.ToUpperInvariant(), out var natId))
            data.NationalityId = natId;

        var loc = await ResolveSwissLocationAsync(data.ZipCode, eaw.City, ct);
        // easy liefert den Ort ohne Kantonskürzel («Roggwil»). Mit PLZ:
        // Resolve speichert den easy-Ort (nie AMTOVZ «Roggwil BE»).
        data.City = !string.IsNullOrWhiteSpace(data.ZipCode)
            ? loc.City
            : (eaw.City?.Trim());
        data.CantonCode = loc.Canton;
        data.Country = string.IsNullOrWhiteSpace(data.ZipCode)
            ? (eaw.CountryKey ?? eaw.Country)?.ToUpperInvariant()
            : "CH";

        if (includeDetailCalls)
        {
            // Props: aus dem Vorab-Cache (Massenimport, parallel) ODER live (Einzel).
            var propsInfo = (cache != null && cache.Props.TryGetValue(eaw.Id, out var cp))
                ? cp
                : await FetchPropsInfoAsync(customerId, eaw.Id, ct);
            data.MaritalStatus = propsInfo.Marital;
            data.Ahv = propsInfo.Ahv;
            // Schwangerschaft (Walter 27.07.2026): Custom Field «Schwanger»
            // value=Ja, from=gemeldet am, to=errechneter Geburtstermin.
            data.PregnantMeldedatum = propsInfo.PregnantMeldedatum;
            data.PregnantErrechneterTermin = propsInfo.PregnantErrechneterTermin;
            // Eintritt = «Datum der Betriebszugehörigkeit» (easy@work Custom Field /
            // cf_seniority_date) — Walter 05.07.2026 + Klarstellung 26.07.2026.
            // «Eingestellt seit» (employee.from) ist nur Fallback (Filial-/Anstellungsbeginn).
            if (propsInfo.SeniorityDate.HasValue)
                data.EntryDate = propsInfo.SeniorityDate;
            // Funktionen (Positionen) aus easy@work — für die Mehrdeutigkeits-Prüfung:
            // mehr als eine distinct Funktion → MA wird nicht importiert (Walter 05.07.2026).
            if (cache != null && cache.Functions.TryGetValue(eaw.Id, out var cf))
                data.Functions = cf;
            else
            {
                try
                {
                    var posData = (await _client.GetPositionsAsync(customerId, eaw.Id, ct))?.Data ?? new();
                    data.Functions = posData
                        .Select(p => (p.Name ?? "").Trim())
                        .Where(n => n.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch { /* Positionen nicht abrufbar → keine Mehrdeutigkeits-Prüfung */ }
            }
            // Nachtarbeit-Arztzeugnis (Walter 05.07.2026, präzisiert 26.07.2026):
            // BEGINN 1:1 aus easy@work (from, UTC→Zürich). ENDE IMMER selbst rechnen
            // (Beginn + 2 Jahre − 1 Tag, ab Alter 45: + 1 Jahr − 1 Tag) — easy-«to»
            // ist UTC-inkonsistent und darf NICHT als Quelle für gültig-bis dienen.
            // Kontrolle: wenn easy ein «to» hat und KEINE der beiden UTC-Lesarten
            // dem Soll entspricht → Hinweis/ToDo (GF korrigiert in easy@work).
            if (propsInfo.NightWorkFrom.HasValue)
            {
                var von = propsInfo.NightWorkFrom.Value;
                var sollBis = Employee.NightWorkValidUntil(von, data.DateOfBirth);
                data.NightWorkExamIssued     = von;
                data.NightWorkExamValidUntil = sollBis; // immer gerechnet, nie easy-«to»

                var toRaw = propsInfo.NightWorkToRaw;
                bool easyOk = !string.IsNullOrWhiteSpace(toRaw)
                              && EawDateUtil.IntervalEndMatchesSoll(toRaw, sollBis);
                data.NightWorkExamEasyMismatch = !easyOk;

                bool maAusgetreten = eaw.To.HasValue && eaw.To.Value < DateOnly.FromDateTime(DateTime.Today);
                if (!maAusgetreten && !easyOk)
                {
                    var nwName = $"{data.FirstName} {data.LastName}".Trim();
                    if (string.IsNullOrWhiteSpace(toRaw))
                        data.Notes.Add($"⚠ Nachtarbeit-Arztzeugnis {nwName}: in easy@work fehlt das Enddatum. "
                                      + $"Soll gemäss Regel: {sollBis:dd.MM.yyyy} (Beginn {von:dd.MM.yyyy}). Bitte in easy@work nachtragen.");
                    else
                        data.Notes.Add($"⚠ Nachtarbeit-Arztzeugnis {nwName}: easy@work-Enddatum stimmt nicht mit der Regel überein "
                                      + $"(Beginn {von:dd.MM.yyyy} → Soll {sollBis:dd.MM.yyyy}). Bitte das Enddatum in easy@work auf {sollBis:dd.MM.yyyy} korrigieren.");
                }
            }
            if (cache != null && cache.Iban.TryGetValue(eaw.Id, out var ci))
                data.Iban = ci;
            else
            {
                try
                {
                    var fiscal = await _client.GetFiscalInfoAsync(customerId, eaw.Id, ct);
                    data.Iban = fiscal?.Iban?.Replace(" ", "").Trim().ToUpperInvariant();
                }
                catch (Exception ex)
                {
                    data.Notes.Add($"IBAN/Fiscal-Info nicht abrufbar: {ex.Message}");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(data.FirstName)) data.Errors.Add("Vorname fehlt in easy@work.");
        if (string.IsNullOrWhiteSpace(data.LastName)) data.Errors.Add("Nachname fehlt in easy@work.");
        if (!string.IsNullOrWhiteSpace(data.Email) && !IsValidEmail(data.Email)) data.Errors.Add($"E-Mail ist ungültig: {data.Email}");
        if (!string.IsNullOrWhiteSpace(data.Ahv) && !IsValidAhv(data.Ahv)) data.Errors.Add($"AHV-Nummer ist ungültig: {data.Ahv}");
        if (!string.IsNullOrWhiteSpace(data.Iban) && !IsValidIban(data.Iban)) data.Errors.Add($"IBAN ist ungültig: {data.Iban}");
        if (!string.IsNullOrWhiteSpace(data.ZipCode) && loc.Error != null) data.Errors.Add(loc.Error);

        return data;
    }

    // ─────────────────────────── Core ───────────────────────────────

    private async Task<SyncResult> SyncCoreAsync(SyncRequest req, bool commit, Action<int, int, string>? progress, CancellationToken ct)
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

        // Kader-Funktionsgruppen (is_kader, z.B. REST_MANAGER, ASST_1/2,
        // SHIFT_LEADER_*) — für die FIX→FIX-M-Erkennung in der Vorschau
        // (Walter 08.07.2026): Manager-Funktionen sind IMMER FIX-M.
        var kaderCodes = (await _db.JobGroups.AsNoTracking()
                .Where(g => g.IsKader && g.Code != null)
                .Select(g => g.Code!)
                .ToListAsync(ct))
            .Select(c => c.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // PERFORMANCE (Walter-Vorgabe 05.07.2026): die Detail-Daten pro MA
        // (Props/Zivilstand/AHV/Nachtarbeit, Positionen/Funktionen, Fiscal/IBAN)
        // sind 3 easy@work-API-Calls PRO MA. Bisher lief das in BuildMasterDataAsync
        // SEQUENZIELL für jeden MA → bei vielen MA sehr langsam. Wir holen diese
        // Calls jetzt VORAB PARALLEL (nur _client = thread-safe, KEIN _db) und
        // BuildMasterDataAsync liest aus dem Cache. Die restliche (DB-)Arbeit
        // bleibt sequenziell (DbContext ist nicht thread-safe).
        var detailCache = new DetailCache();
        if (!req.SkipDetailCalls)
        {
            var custId0 = mapping.EasyAtWorkCustomerId;
            using var sem = new SemaphoreSlim(10);
            var pfTasks = eawEmps.Select(async eaw =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    try { detailCache.Props[eaw.Id] = await FetchPropsInfoAsync(custId0, eaw.Id, ct); } catch { }
                    try
                    {
                        var pos = (await _client.GetPositionsAsync(custId0, eaw.Id, ct))?.Data ?? new();
                        detailCache.Functions[eaw.Id] = pos
                            .Select(p => (p.Name ?? "").Trim())
                            .Where(n => n.Length > 0)
                            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    }
                    catch { }
                    try
                    {
                        var fisc = await _client.GetFiscalInfoAsync(custId0, eaw.Id, ct);
                        detailCache.Iban[eaw.Id] = fisc?.Iban?.Replace(" ", "").Trim().ToUpperInvariant();
                    }
                    catch { }
                    // STRICT: Verträge + Tarife für die Fehlerprüfung in der Vorschau.
                    try { detailCache.Contracts[eaw.Id] = (await _client.GetContractsAsync(custId0, eaw.Id, ct))?.Data ?? new(); } catch { }
                    try { detailCache.Rates[eaw.Id]     = (await _client.GetPayRatesAsync(custId0, eaw.Id, ct))?.Data ?? new(); } catch { }
                }
                finally { sem.Release(); }
            });
            await Task.WhenAll(pfTasks);
        }

        var masterByEaw = new Dictionary<int, EmployeeMasterData>();
        foreach (var eaw in eawEmps)
            masterByEaw[eaw.Id] = await BuildMasterDataAsync(
                mapping.EasyAtWorkCustomerId, eaw, natByCode,
                includeDetailCalls: !req.SkipDetailCalls, ct, detailCache);

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

        int hansMusterSkipped = 0;
        foreach (var eaw in eawEmps)
        {
            var master = masterByEaw[eaw.Id];
            // Nachtarbeit-Hinweise (✓ korrekt / ⚠ korrigiert) ins Sync-Ergebnis
            // durchreichen — schon in der Vorschau sichtbar, im Frontend grün/rot
            // eingefärbt (Walter 30.06.2026).
            foreach (var n in master.Notes.Where(n => n.StartsWith("⚠") || n.StartsWith("✓")))
                res.Notes.Add(n);

            // „hans muster" ist ein Test-/Platzhalter-Datensatz (wie „John Doe")
            // und darf NIE importiert werden (Walter 29.06.2026). Komplett
            // überspringen — kein Row, kein Insert/Update. Verhindert auch künftige
            // Nummern-Kollisionen durch solche Alt-Testdaten.
            if (IsHansMuster(master.FirstName, master.LastName))
            {
                hansMusterSkipped++;
                continue;
            }

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
                FirstName     = master.FirstName,
                LastName      = master.LastName,
            };

            if (string.IsNullOrWhiteSpace(row.Number))
            {
                row.Status = "CONFLICT";
                row.Reason = "easy@work-MA hat keine Personalnummer — nicht eindeutig zuordenbar.";
                res.Rows.Add(row); res.CountConflict++;
                continue;
            }
            if (master.Errors.Count > 0)
            {
                row.Status = "CONFLICT";
                row.Reason = string.Join("; ", master.Errors);
                row.Diffs = new();
                res.Rows.Add(row); res.CountConflict++;
                continue;
            }
            // Mehrdeutige Funktion: mehr als eine Funktion in easy@work erfasst →
            // MA NICHT importieren/anpassen, als Konflikt melden mit Auflistung
            // (Walter-Vorgabe 05.07.2026).
            if (master.Functions.Count > 1)
            {
                row.Status = "CONFLICT";
                row.Reason = $"{master.Functions.Count} Funktionen gefunden: {string.Join(", ", master.Functions)} — mehrdeutig, MA wird nicht importiert/angepasst. Bitte in easy@work auf eine Funktion reduzieren.";
                row.Diffs = new();
                res.Rows.Add(row); res.CountConflict++;
                res.Notes.Add($"⚠ {master.FirstName} {master.LastName} (Nr. {row.Number}): {master.Functions.Count} Funktionen gefunden: {string.Join(", ", master.Functions)} — nicht importiert.");
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
            // user_id wird NICHT mehr gematcht (Walter 29.06.2026) — nur eaw.Id.
            row.CoworkEmployeeId = co?.Id;
            // Über eine ALTE Nummer gematcht? (matchender Key ≠ aktuelle Personalnr.)
            if (co != null && matchedKey != null
                && !string.Equals(co.EmployeeNumber?.Trim(), matchedKey, StringComparison.OrdinalIgnoreCase))
                row.MatchedViaAltNumber = matchedKey;

            // Diffs berechnen (auch für NEW — dann sind alle Cowork-Werte leer)
            var diffs = ComputeDiffs(co, master);
            row.Diffs = diffs;

            // ── Personalnummern-Wechsel (Walter-Vorgabe 21.06.2026) ────────────
            // Nur wenn über die easy@work-ID gematcht wurde (die neue Nummer ist
            // also weder aktuelle noch Alt-Nummer) UND sie sich tatsächlich von der
            // aktuellen unterscheidet UND noch nicht in alt1/alt2 steht (keine
            // Endlos-Rotation). Dann: Diff „Personalnummer" → wird beim Commit
            // rotiert (aktuelle → alt1, alt1 → alt2) und die neue Nr. gesetzt.
            // Nur ein AKTIVER Datensatz darf die Hauptnummer wechseln (Walter
            // 12.07.2026, Alaa/Rasakumary): der beendete Datensatz einer alten
            // Filiale hatte via eaw-ID-Match die Hauptnummer auf seine tote
            // Nummer rotiert.
            if (matchedByEawId && co != null
                && (!eaw.To.HasValue || eaw.To.Value >= activeAt)
                && ShouldSaveNumberChange(co.EmployeeNumber, rawNumber,
                       aliasesByEmp.TryGetValue(co.Id, out var coAliases) ? coAliases : null))
            {
                row.NumberChangeFrom = co.EmployeeNumber?.Trim();
                row.NumberChangeTo   = rawNumber;
                diffs.Add(new FieldDiff { Field = "Personalnummer", Cowork = row.NumberChangeFrom, Easy = rawNumber, WillSet = true });
            }

            // Austritt-Diff unterdrücken, wenn der Commit ihn ohnehin verwerfen
            // würde (Walter 08.07.2026, Endlos-UPDATE-Fix): der Vergangenheits-
            // Austritt eines ALTEN Filial-Datensatzes gilt NICHT als Personen-
            // Austritt, solange der MA irgendwo einen offenen Vertrag hat
            // (Filialwechsel/Wiedereintritt). Der Commit macht genau das (setzt
            // den Austritt sofort wieder zurück) — die Vorschau muss dieselbe
            // Regel anwenden, sonst erscheint derselbe MA bei JEDEM Import
            // erneut als UPDATE, obwohl netto nie etwas ändert.
            if (co != null && eaw.To.HasValue && eaw.To.Value < activeAt
                && diffs.Any(d => d.Field == "Austritt" && d.WillSet))
            {
                var todayDt = DateTime.Today;
                bool hasOpenContract = await _db.Employments.AsNoTracking()
                    .AnyAsync(em => em.EmployeeId == co.Id
                                 && (em.ContractEndDate == null || em.ContractEndDate >= todayDt), ct);
                if (hasOpenContract)
                    diffs.RemoveAll(d => d.Field == "Austritt");
            }

            // STRICT-Vertragsprüfung (Walter-Vorgabe 08.07.2026): Erfassungsfehler
            // in den easy@work-Verträgen (Überlappung, offener Alt-Vertrag,
            // FLEX/MTP mit Stunden pro Monat, fehlender Lohn ausser FIX-M) werden
            // HART als CONFLICT gezeigt — für diesen MA wird NICHTS importiert,
            // bis easy@work korrigiert ist. Kein Raten, kein «nächstbester Vertrag».
            string? vertragsFehler = null;
            if (!req.SkipDetailCalls)
            {
                var vContracts = detailCache.Contracts.TryGetValue(eaw.Id, out var vc) ? vc : new List<EawContract>();
                var vRates     = detailCache.Rates.TryGetValue(eaw.Id, out var vr) ? vr : new List<EawPayRate>();
                // Nur Fehler an AKTIVEN/zukünftigen Verträgen melden (Walter
                // 08.07.2026) — fehlerhafte abgelaufene Verträge werden beim
                // Import einfach still weggelassen (Historie = altes Lohnprogramm).
                vertragsFehler = ValidateContractOverlaps(vContracts, activeAt);
                if (vertragsFehler == null && vContracts.Count > 0)
                {
                    bool vKader = detailCache.Functions.TryGetValue(eaw.Id, out var vf)
                                  && vf.Any(f => kaderCodes.Contains(f.Trim()));
                    var tlPrev = BuildEmploymentTimeline(vContracts, vRates, activeAt, vKader);
                    vertragsFehler = tlPrev
                        .Where(s => !s.End.HasValue || s.End.Value >= activeAt)   // nur aktive/zukünftige Segmente
                        .Select(s => s.Info.DataError)
                        .FirstOrDefault(e2 => e2 != null);
                }
            }

            if (vertragsFehler != null)
            {
                row.Status = "CONFLICT";
                row.Reason = vertragsFehler;
                res.Rows.Add(row); res.CountConflict++;
                continue;
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
                     && !(eaw.To.HasValue && eaw.To.Value < activeAt)
                     && EmploymentFixReason(curEmp) is { } fixReason)
            {
                // Strukturell falscher Vertrag (Walter 23.06.2026): FLEX/MTP mit
                // Pensum %, fehlendem Stundenlohn, MTP ohne garantierte Stunden, oder
                // FIX/FIX-M ohne Monatslohn. Ohne Feld-Diff wäre der MA UNCHANGED →
                // der Vertrag würde nie korrigiert. Als UPDATE behandeln.
                // NUR für AKTIVE MA (Walter 08.07.2026): bei Ausgetretenen ist der
                // Vertrag Geschichte — es gibt keine künftigen Lohnläufe, eine
                // «Korrektur» wäre reines Rauschen im Massenimport («Alle»).
                row.Status = "UPDATE";
                row.Reason = $"Vertrag wird korrigiert: {fixReason}.";

                // Konkretes Ziel aus den (bereits geladenen) easy@work-Daten in den
                // Text — statt eines vagen «wird korrigiert». Fehler-Fälle (Lohn
                // fehlt, Überlappung, Flex/Monat) sind hier bereits als CONFLICT
                // abgefangen (STRICT-Prüfung weiter oben).
                if (!req.SkipDetailCalls
                    && (fixReason.Contains("Stundenlohn fehlt") || fixReason.Contains("ohne Monatslohn")))
                {
                    try
                    {
                        var vc2 = detailCache.Contracts.TryGetValue(eaw.Id, out var c2) ? c2 : new List<EawContract>();
                        var vr2 = detailCache.Rates.TryGetValue(eaw.Id, out var r2) ? r2 : new List<EawPayRate>();
                        bool vKader2 = detailCache.Functions.TryGetValue(eaw.Id, out var vf2)
                                       && vf2.Any(f => kaderCodes.Contains(f.Trim()));
                        var ordered2  = vc2.OrderBy(x => x.From ?? DateOnly.MinValue).ToList();
                        var currentC2 = ordered2.LastOrDefault(x => (x.From ?? DateOnly.MinValue) <= activeAt) ?? ordered2.FirstOrDefault();
                        var curInfo   = ComputeContractInfo(currentC2, vr2, activeAt, vKader2);
                        var zielLohn = curInfo.HourlyRate.HasValue
                            ? $"Stundenlohn CHF {curInfo.HourlyRate:0.00}"
                            : curInfo.MonthlySalary.HasValue
                                ? $"Monatslohn CHF {curInfo.MonthlySalary:0.00}"
                                : curInfo.MonthlySalaryFte.HasValue
                                    ? $"Monatslohn CHF {curInfo.MonthlySalaryFte:0.00} (100 %)"
                                    : "ohne Lohn (FIX-M)";
                        row.Reason = $"Vertrag wird korrigiert: {fixReason.Split('—')[0].Trim()} — "
                                   + $"aus easy@work kommt {curInfo.EmploymentModel}, {zielLohn}.";
                    }
                    catch { /* nur Anzeige-Komfort — bei Fehler bleibt der Standardtext */ }
                }
                res.CountUpdate++;
            }
            else if (co != null && !req.SkipDetailCalls
                     && !(eaw.To.HasValue && eaw.To.Value < activeAt)
                     && empByIdThisBranch.TryGetValue(co.Id, out var curEmpK)
                     && curEmpK.EmploymentModel == "FIX"
                     && detailCache.Functions.TryGetValue(eaw.Id, out var fnsK)
                     && fnsK.FirstOrDefault(f => kaderCodes.Contains(f.Trim())) is { } kaderFn)
            {
                // Kader-Funktion mit FIX-Vertrag (Walter 08.07.2026): Manager-
                // Funktionen (is_kader) sind IMMER FIX-M. Ohne Feld-Diff wäre der
                // MA UNCHANGED und die Korrektur liefe nie. Der Lohn bleibt beim
                // Commit unangetastet (gleiche Monatslohn-Basis).
                row.Status = "UPDATE";
                row.Reason = $"Funktion «{kaderFn}» ist Kader/Management → Vertragsmodell wird auf FIX-M korrigiert. Der erfasste Lohn bleibt unverändert.";
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
            else if (co.IsPayrollExcluded)
            {
                // Phantom-MA (Supervisor, Walter-Vorgabe 08.07.2026): ist in JEDER
                // Filiale in easy@work erfasst, hat aber nirgends Vertrag/Lohn/
                // Stempel. Der Sync legt für ihn NIE ein Employment an —
                // Stammdaten-Updates (Adresse, Eintritt …) bleiben erlaubt.
                row.EmploymentInfo = "MA ohne Lohn — kein Vertrag";
            }
            else
            {
                var hasEmp = await _db.Employments
                    .AnyAsync(em => em.EmployeeId == co.Id && em.CompanyProfileId == req.CompanyProfileId, ct);
                row.EmploymentInfo = hasEmp ? "existiert" : "wird nachgeholt";
            }
            res.Rows.Add(row);
        }
        res.CountTotal = res.Rows.Count;
        if (hansMusterSkipped > 0)
            res.Notes.Add($"{hansMusterSkipped} Test-Datensatz/-Datensätze „hans muster“ übersprungen (werden nie importiert).");

        // ── Wiedereintritts-Duplikate entschärfen (Walter 08.07.2026) ──────
        // Dieselbe Person kann in easy@work MEHRFACH existieren: alter Datensatz
        // (vor dem Wiedereintritt, alte Personalnummer) + neuer Datensatz — beide
        // matchen denselben Cowork-MA (der alte über die Alt-Nummer). Ohne diesen
        // Pass würde der ALTE Datensatz veraltete Werte zurückschreiben (z.B. das
        // frühere Austrittsdatum über den aktuellen Stand). Massgebend ist:
        // der in easy@work AKTIVE Datensatz, sonst der mit dem jüngsten Eintritt.
        // Alle übrigen werden markiert und beim Commit komplett übersprungen.
        {
            var eawById = eawEmps.ToDictionary(e => e.Id);
            var dupGroups = res.Rows
                .Where(r => r.CoworkEmployeeId.HasValue && r.Status != "CONFLICT")
                .GroupBy(r => r.CoworkEmployeeId!.Value)
                .Where(g => g.Count() > 1);
            foreach (var g in dupGroups)
            {
                var winner = g
                    .OrderByDescending(r => eawById.TryGetValue(r.EawEmployeeId, out var e)
                        && (!e.To.HasValue || e.To.Value >= activeAt) ? 1 : 0)   // aktiv zuerst
                    .ThenByDescending(r => eawById.TryGetValue(r.EawEmployeeId, out var e2)
                        ? (e2.From ?? DateOnly.MinValue) : DateOnly.MinValue)    // jüngster Eintritt
                    .ThenByDescending(r => r.EawEmployeeId)
                    .First();
                foreach (var loser in g.Where(r => r != winner))
                {
                    loser.SupersededDuplicate = true;
                    if (loser.Status == "UPDATE") { res.CountUpdate--; res.CountUnchanged++; }
                    loser.Status = "UNCHANGED";
                    loser.Reason = $"Älterer easy@work-Datensatz derselben Person (Wiedereintritt) — wird übersprungen; massgebend ist Nr. {winner.Number}.";
                    loser.Diffs.Clear();
                }
            }
        }

        // ── Personalnummer-Kollisionen ERKENNEN (Walter 29.06.2026) ──
        // Nichts überspringen, nichts umbiegen: Würde eine Personalnummer doppelt
        // vergeben, nennt die Vorschau exakt die Nummer + BEIDE Seiten
        // (easy@work ↔ Cowork). Beim COMMIT wird dann NICHTS geschrieben (Blocked) —
        // Walter klärt die Dublette zuerst in beiden Systemen. Geprüft werden:
        // NEW-Zeilen (neue Nummer) und Nummern-Wechsel beim Filialwechsel
        // (NumberChangeTo) — gegen bestehende Cowork-MA UND gegeneinander.
        {
            var existingByNum = (await _db.Employees.AsNoTracking()
                    .Where(e => e.EmployeeNumber != null && e.EmployeeNumber != "")
                    .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.EasyAtWorkEmployeeId })
                    .ToListAsync(ct))
                .GroupBy(e => e.EmployeeNumber!.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var reported       = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var claimedInBatch = new Dictionary<string, EmployeePreviewRow>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in res.Rows)
            {
                // Welche Nummer würde diese Zeile vergeben — und an welchen Cowork-MA?
                string num; int? ownId;
                if (row.Status == "NEW") { num = (row.Number ?? "").Trim(); ownId = null; }
                else if (!string.IsNullOrWhiteSpace(row.NumberChangeTo)) { num = row.NumberChangeTo!.Trim(); ownId = row.CoworkEmployeeId; }
                else continue;
                if (string.IsNullOrEmpty(num)) continue;

                var eawSide = $"{(row.FirstName ?? "").Trim()} {(row.LastName ?? "").Trim()}".Trim();
                if (string.IsNullOrWhiteSpace(eawSide)) eawSide = "?";

                // a) Kollision mit bestehendem Cowork-MA. KEINE Kollision, wenn es
                //    dieselbe Person ist — entweder derselbe Cowork-Datensatz
                //    (UPDATE-Nummernwechsel auf die EIGENE Zeile) oder der bestehende
                //    MA trägt bereits dieselbe easy@work-ID (dann ist es nur ein
                //    noch nicht erkannter Match, kein echter Doppel-Eintrag).
                if (existingByNum.TryGetValue(num, out var ex))
                {
                    var samePerson = (ownId.HasValue && ex.Id == ownId.Value)
                                  || (ex.EasyAtWorkEmployeeId.HasValue && ex.EasyAtWorkEmployeeId.Value == row.EawEmployeeId);
                    if (!samePerson)
                    {
                        if (reported.Add(num))
                            res.NumberConflicts.Add(
                                $"Personalnummer {num} doppelt: easy@work-MA „{eawSide}“ (eaw-id {row.EawEmployeeId}) " +
                                $"↔ bereits in Cowork bei MA #{ex.Id} „{($"{ex.FirstName} {ex.LastName}").Trim()}“ (Nr. {num}). " +
                                $"Bitte in beiden Systemen prüfen.");
                        continue;
                    }
                }

                // b) Within-Batch: zwei easy@work-Zeilen wollen dieselbe Nummer
                if (claimedInBatch.TryGetValue(num, out var first))
                {
                    if (reported.Add(num))
                    {
                        var firstSide = $"{(first.FirstName ?? "").Trim()} {(first.LastName ?? "").Trim()}".Trim();
                        res.NumberConflicts.Add(
                            $"Personalnummer {num} doppelt in easy@work: „{firstSide}“ (eaw-id {first.EawEmployeeId}) " +
                            $"UND „{eawSide}“ (eaw-id {row.EawEmployeeId}) tragen dieselbe Nummer. Bitte in easy@work prüfen.");
                    }
                }
                else claimedInBatch[num] = row;
            }
        }

        // 4) Commit-Pfad
        if (commit)
        {
            // Personalnummer-Kollision → NICHTS schreiben (Walter 29.06.2026).
            // Erst muss Walter die doppelte Nummer in easy@work + Cowork klären,
            // sonst würde der unique-Constraint uq_employee_employee_number den
            // ganzen (atomaren) Import sprengen. Die genauen Nummern stehen in
            // res.NumberConflicts und werden im Frontend als Blockmeldung gezeigt.
            if (res.NumberConflicts.Count > 0)
            {
                res.Blocked = true;
                await LogEmployeeSyncRunAsync(req, res, ct);
                return res;
            }

            // Silent Backfill: ALLE gematchten MA (auch UNCHANGED) bekommen die
            // easyatwork_employee_id wenn sie fehlt. Das ist nicht-destruktiv
            // (nur null → Wert) und ohne diese ID lässt sich `edited_by_id` aus
            // Stempel-Audits nicht zum Manager auflösen. Walter 17.06.2026.
            int backfilled = 0;
            // SupersededDuplicate ausschliessen — sonst würde der ALTE Wieder-
            // eintritts-Datensatz seine easy@work-ID über die des aktuellen
            // Datensatzes schreiben (letzter Loop-Durchlauf gewinnt).
            foreach (var row in res.Rows.Where(r => r.CoworkEmployeeId.HasValue && !r.SupersededDuplicate))
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
                // Schutz gegen Doppelvergabe derselben easy@work-id (Walter 29.06.2026).
                await RevertDuplicateEawIdsAsync(res, ct);
                await _db.SaveChangesAsync(ct);
                res.Notes.Add($"easy@work-ID stillschweigend bei {backfilled} bestehenden MA nachgetragen.");
            }

            // ── Wiedereintritts-Duplikate: alte easy@work-ID + Nummer als Alias
            // sichern (Walter 08.07.2026). Die Stempelzeiten laufen EINDEUTIG über
            // die easy@work-ID — der alte Datensatz wird zwar beim Schreiben
            // übersprungen, aber seine ID muss in easy_at_work_employee_alias auf
            // den Haupt-MA zeigen, sonst wären dessen Stempel nicht zuordenbar
            // (gleicher Mechanismus wie 2 Personalnummern in 2 Filialen).
            {
                int aliasAdded = 0;
                foreach (var dup in res.Rows.Where(r => r.SupersededDuplicate && r.CoworkEmployeeId.HasValue))
                {
                    var empId = dup.CoworkEmployeeId!.Value;
                    if (!await _db.EasyAtWorkEmployeeAliases.AnyAsync(
                            a => a.EmployeeId == empId && a.EasyAtWorkId == dup.EawEmployeeId, ct))
                    {
                        _db.EasyAtWorkEmployeeAliases.Add(new EasyAtWorkEmployeeAlias
                        {
                            EmployeeId   = empId,
                            EasyAtWorkId = dup.EawEmployeeId,
                            Note         = $"Wiedereintritt: alter easy@work-Datensatz (Nr. {dup.Number})",
                            CreatedAt    = DateTime.UtcNow,
                        });
                        aliasAdded++;
                    }
                    // Alte Personalnummer ebenfalls als Alias (falls abweichend + neu).
                    var num = (dup.Number ?? "").Trim();
                    if (num.Length > 0)
                    {
                        var empRow = await _db.Employees.AsNoTracking()
                            .Where(e => e.Id == empId).Select(e => e.EmployeeNumber).FirstOrDefaultAsync(ct);
                        if (!string.Equals((empRow ?? "").Trim(), num, StringComparison.OrdinalIgnoreCase)
                            && !await _db.EmployeeNumberAliases.AnyAsync(a => a.EmployeeId == empId && a.Number == num, ct))
                        {
                            _db.EmployeeNumberAliases.Add(new EmployeeNumberAlias
                            {
                                EmployeeId = empId, Number = num,
                                Source = "easyatwork_sync", CreatedAt = DateTime.UtcNow,
                            });
                            aliasAdded++;
                        }
                    }
                }
                if (aliasAdded > 0)
                {
                    await _db.SaveChangesAsync(ct);
                    res.Notes.Add($"Wiedereintritts-Duplikate: {aliasAdded} alte easy@work-ID(s)/Nummer(n) als Alias am Haupt-MA gesichert (Stempel-Zuordnung bleibt intakt).");
                }
            }

            // Zu schreibende Zeilen: NEW/UPDATE PLUS bereits zugeordnete UNCHANGED-MA
            // (Walter-Vorgabe 23.06.2026). Letztere werden still mit easy@work
            // abgeglichen — sonst bliebe ein einmal falsch angelegter Vertrag (z.B.
            // UTP statt MTP) für immer stehen, weil ohne Feld-Diff niemand ihn anfasst.
            // EnsureEmploymentAsync ist idempotent (Modell führend, Lohn fill-if-empty).
            var rowsToProcess = res.Rows
                .Where(r => !r.SupersededDuplicate)   // alte Wiedereintritts-Datensätze NIE schreiben (Walter 08.07.2026)
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
                .Where(r => r.CoworkEmployeeId.HasValue && !r.SupersededDuplicate
                            && r.Status != "CONFLICT")   // STRICT: bei Vertrags-Fehlern NICHTS importieren
                .ToList();
            // Tiefenimport (Walter-Vorgabe 08.07.2026): NUR Stammdaten, KEINE
            // Verträge — die Timeline-Arbeit entfällt komplett.
            if (req.SkipContracts) rowsForTimeline.Clear();

            // Detail-Daten (Verträge/Pay-Rates/Zivilstand) PARALLEL vorladen (max. 10
            // gleichzeitig) statt 3 sequenzielle API-Calls pro MA. Diese Calls nutzen
            // NUR den HTTP-Client (nicht den DbContext) → thread-safe. Beim Schnell-
            // Import (SkipDetailCalls) ganz überspringen. Walter-Vorgabe 21.06.2026.
            // Rohe Verträge + Pay-Rates pro MA → daraus baut der zweite Durchgang die
            // komplette Employment-Timeline (Walter-Vorgabe 23.06.2026); beim
            // Tiefenimport (SkipContracts) komplett überflüssig → überspringen.
            var contractsRawByEaw = new ConcurrentDictionary<int, List<EawContract>>();
            var ratesRawByEaw     = new ConcurrentDictionary<int, List<EawPayRate>>();
            var positionByEaw = new ConcurrentDictionary<int, string?>();
            if (rowsToProcess.Count > 0 && !req.SkipContracts)
            {
                using var sem = new SemaphoreSlim(10);
                // Fortschritt für den asynchronen Hintergrund-Import (Walter 29.06.2026):
                // diese easy@work-Detail-Calls (Verträge/Lohn/Position pro MA) sind der
                // langsame Teil (~1 Aufruf/Sekunde) — hier den Balken füttern.
                var detailIds = rowsToProcess.Concat(rowsForTimeline)
                    .Select(r => r.EawEmployeeId).Distinct().ToList();
                var detailTotal = detailIds.Count;
                int detailDone = 0;
                progress?.Invoke(0, detailTotal, "Lade Vertrags-/Lohndaten aus easy@work…");
                var detailTasks = detailIds
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
                            if (!req.SkipDetailCalls)
                            {
                                // Optionale Zusatz-Stammdaten (AHV/Zivilstand/IBAN)
                                // lädt BuildMasterDataAsync vor der Diff-Berechnung.
                                // Dieses Gate bleibt hier als Audit-Marker: Verträge,
                                // Pay-Rates und Positionen stehen bewusst oberhalb.
                            }
                        }
                        finally
                        {
                            sem.Release();
                            var d = System.Threading.Interlocked.Increment(ref detailDone);
                            progress?.Invoke(d, detailTotal, "Lade Vertrags-/Lohndaten aus easy@work…");
                        }
                    });
                await Task.WhenAll(detailTasks);
                progress?.Invoke(detailTotal, detailTotal, "Schreibe Mitarbeiter & Verträge…");
            }
            List<EawContract> ContractsFor(int eawId) => contractsRawByEaw.TryGetValue(eawId, out var c) ? c : new();
            List<EawPayRate>  RatesFor(int eawId)     => ratesRawByEaw.TryGetValue(eawId, out var r) ? r : new();
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
                    // user_id wird NICHT mehr geprüft (Walter 29.06.2026) — nur eaw.Id.
                    var existingByEawId = await _db.Employees.FirstOrDefaultAsync(
                        e => !e.IsHidden && e.EasyAtWorkEmployeeId == eawKey, ct);
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
                        // 1) Personalnummer übernehmen/sichern (Walter-Bug 10.07.2026,
                        //    Laila Seifeddin): Beim Wiedereintritts-Merge blieb bisher
                        //    IMMER die alte Hauptnummer stehen und die NEUE landete nur
                        //    als Alias — falsch herum, wenn der easy@work-Datensatz der
                        //    AKTUELLE ist (aktiv) oder die alte Hauptnummer eine
                        //    Archiv-«alt»-Nummer ist. Dann: neue Nummer wird HAUPTnummer,
                        //    alte wandert als Alias in die Historie. Kollisionsschutz:
                        //    gehört die Nummer schon einem ANDEREN MA, bleibt es beim
                        //    Alias (+ Hinweis) — nichts wird doppelt vergeben.
                        var newNum   = (eaw.Number ?? "").Trim();
                        var existNum = (existingByEawId.EmployeeNumber ?? "").Trim();
                        if (!string.IsNullOrWhiteSpace(newNum)
                            && !string.Equals(newNum, existNum, StringComparison.OrdinalIgnoreCase))
                        {
                            bool eawRecordAktiv = !eaw.To.HasValue || eaw.To.Value >= activeAt;
                            bool nummerBesetzt  = await _db.Employees.AnyAsync(
                                x => x.Id != existingByEawId.Id && !x.IsHidden && x.EmployeeNumber == newNum, ct);

                            // Walter-Bug 18.07.2026 (Sweeba Akhtar 581039): easy@work
                            // kennt kein «alt»-Suffix. «581039alt» vs. «581039» ist
                            // DIESELBE Badge — nie zum nackten Wert hochstufen.
                            // Echte Wiedereintritts-Nummern (andere Basis) bleiben ok.
                            if (ShouldPromoteEawNumberToMain(existNum, newNum, eawRecordAktiv, nummerBesetzt))
                            {
                                // Falls die neue Nummer schon als Alias hinterlegt war:
                                // Alias entfernen — sie wird jetzt die Hauptnummer.
                                var aliasRow = await _db.EmployeeNumberAliases.FirstOrDefaultAsync(
                                    a => a.EmployeeId == existingByEawId.Id && a.Number == newNum, ct);
                                if (aliasRow != null) _db.EmployeeNumberAliases.Remove(aliasRow);
                                SaveNumberChange(_db, existingByEawId, newNum); // alte Hauptnr. → Alias
                                res.Notes.Add($"{existingByEawId.FirstName} {existingByEawId.LastName}: Personalnummer {existNum} → {newNum} (alte Nummer als Alias gesichert).");
                            }
                            else if (!IsSameNumberIgnoringAlt(existNum, newNum)
                                     && !await _db.EmployeeNumberAliases.AnyAsync(a => a.EmployeeId == existingByEawId.Id && a.Number == newNum, ct))
                            {
                                _db.EmployeeNumberAliases.Add(new EmployeeNumberAlias
                                {
                                    EmployeeId = existingByEawId.Id, Number = newNum,
                                    Source = "easyatwork_sync", CreatedAt = DateTime.UtcNow,
                                });
                                if (nummerBesetzt)
                                    res.Notes.Add($"⚠ {existingByEawId.FirstName} {existingByEawId.LastName}: Nummer {newNum} gehört bereits einem anderen MA — nur als Alias gesichert, bitte Dublette klären.");
                            }
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
                        //    Phantom-MA (Supervisor, IsPayrollExcluded) ausgenommen —
                        //    für ihn wird NIE ein Vertrag angelegt (Walter 08.07.2026).
                        if (!existingByEawId.IsPayrollExcluded)
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
                        else if (existingByEawId.IsActive && existingByEawId.ExitDate.HasValue)
                        {
                            // Bereits aktiv, aber stehengebliebenes Austrittsdatum. Entfernen,
                            // wenn easy@work hier unbefristet ist ODER der MA noch einen offenen
                            // Vertrag hat (Filialwechsel → Filial-Austritt ist nicht der
                            // Personen-Austritt). Walter-Bug 05.07.2026.
                            bool clearIt = !eaw.To.HasValue
                                || await _db.Employments.AnyAsync(em => em.EmployeeId == existingByEawId.Id
                                        && (em.ContractEndDate == null || em.ContractEndDate >= DateTime.Today), ct);
                            if (clearIt) existingByEawId.ExitDate = null;
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
                    var master = masterByEaw[row.EawEmployeeId];
                    ApplyDiffs(emp, row.Diffs, master);
                    // Nachtarbeit: Beginn aus easy, Ende gerechnet (Walter 26.07.2026).
                    if (master.NightWorkExamIssued.HasValue)
                    {
                        emp.NightWorkExamIssued = master.NightWorkExamIssued.Value.ToDateTime(TimeOnly.MinValue);
                        emp.NightWorkExamValidUntil = master.NightWorkExamValidUntil!.Value.ToDateTime(TimeOnly.MinValue);
                        emp.NightWorkExamEasyMismatch = master.NightWorkExamEasyMismatch;
                    }
                    if (string.IsNullOrWhiteSpace(emp.LanguageCode)) emp.LanguageCode = "de";
                    if (string.IsNullOrWhiteSpace(emp.Religion))     emp.Religion     = "keine";
                    if (string.IsNullOrWhiteSpace(emp.CantonCode)) emp.CantonCode = await LookupCantonAsync(emp.ZipCode, ct);
                    if (string.IsNullOrWhiteSpace(emp.LetterSalutation)) emp.LetterSalutation = BuildLetterSalutation(emp.Gender, emp.FirstName);

                    _db.Employees.Add(emp);
                    res.CountInserted++;
                    timelineWork.Add((emp, row.EawEmployeeId, jobGroupId, jobGroupCode, isKader, eaw.To));
                    if (!string.IsNullOrWhiteSpace(master.Iban)) bankWork.Add((emp, master.Iban!));
                }
                else // UPDATE
                {
                    if (row.CoworkEmployeeId == null) continue;
                    var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == row.CoworkEmployeeId, ct);
                    if (emp == null) continue;
                    var newEawId = eaw.Id;
                    // Anker-Schutz (Walter 12.07.2026, Alaa/Rasakumary): ein BEENDETER
                    // easy@work-Datensatz (to in der Vergangenheit) darf die Verknüpfung
                    // NICHT mehr an sich reissen. Vorher überschrieb jeder Filial-Lauf
                    // blind den Anker — matchte danach ein Lauf den MA über die ID des
                    // toten Datensatzes, rotierte er die Hauptnummer auf die tote Nummer
                    // (so entstanden 581026/104188 als Hauptnummern am 21.06.2026).
                    bool eawStillActiveUp = !eaw.To.HasValue || eaw.To.Value >= activeAt;
                    if (emp.EasyAtWorkEmployeeId != newEawId
                        && (eawStillActiveUp || emp.EasyAtWorkEmployeeId == null))
                        emp.EasyAtWorkEmployeeId = newEawId;
                    var master = masterByEaw[row.EawEmployeeId];
                    ApplyDiffs(emp, row.Diffs, master);
                    // Nachtarbeit: Beginn aus easy, Ende gerechnet (Walter 26.07.2026).
                    if (master.NightWorkExamIssued.HasValue)
                    {
                        emp.NightWorkExamIssued = master.NightWorkExamIssued.Value.ToDateTime(TimeOnly.MinValue);
                        emp.NightWorkExamValidUntil = master.NightWorkExamValidUntil!.Value.ToDateTime(TimeOnly.MinValue);
                        emp.NightWorkExamEasyMismatch = master.NightWorkExamEasyMismatch;
                    }
                    if (!string.IsNullOrWhiteSpace(row.NumberChangeTo))
                    {
                        // Nur ein AKTIVER Datensatz darf die Hauptnummer setzen
                        // (Walter 12.07.2026) + Kollisionsschutz wie im Merge-Pfad.
                        var ncTo = row.NumberChangeTo!.Trim();
                        bool ncBesetzt = await _db.Employees.AnyAsync(
                            x => x.Id != emp.Id && !x.IsHidden && x.EmployeeNumber == ncTo, ct);
                        if (!eawStillActiveUp)
                            res.Notes.Add($"{emp.FirstName} {emp.LastName}: Nummernwechsel {row.NumberChangeFrom} → {ncTo} übersprungen — der easy@work-Datensatz ist beendet (Austritt {eaw.To:dd.MM.yyyy}).");
                        else if (ncBesetzt)
                            res.Notes.Add($"⚠ {emp.FirstName} {emp.LastName}: Nummer {ncTo} gehört bereits einem anderen MA — Nummernwechsel übersprungen, bitte Dublette klären.");
                        else
                        {
                            SaveNumberChange(_db, emp, ncTo);
                            res.Notes.Add($"Personalnummer geändert: {row.NumberChangeFrom} → {ncTo} (alte Nr. als Alias gesichert).");
                        }
                    }
                    if (string.IsNullOrWhiteSpace(emp.LanguageCode)) emp.LanguageCode = "de";
                    if (string.IsNullOrWhiteSpace(emp.Religion))     emp.Religion     = "keine";
                    if (string.IsNullOrWhiteSpace(emp.CantonCode)) emp.CantonCode = await LookupCantonAsync(emp.ZipCode, ct);
                    if (string.IsNullOrWhiteSpace(emp.LetterSalutation)) emp.LetterSalutation = BuildLetterSalutation(emp.Gender, emp.FirstName);
                    // Aktiv-Status (Walter-Bug 22.06.2026, Filialwechsel): eaw.To ist das
                    // Austrittsdatum DIESER Filiale, nicht des Menschen.
                    // (eawStillActiveUp ist oben beim Anker-Schutz deklariert.)
                    if (!emp.IsActive && eawStillActiveUp)
                    {
                        // In dieser Filiale laut easy@work noch aktiv → Person reaktivieren.
                        emp.IsActive = true;
                        emp.ExitDate = null;
                    }
                    else if (eaw.To.HasValue && eaw.To.Value < activeAt)
                    {
                        // easy@work-Austritt in der Vergangenheit. Er darf NUR dann zum
                        // Personen-Austritt werden, wenn der MA KEINEN offenen Vertrag mehr
                        // hat (irgendeine Filiale). Hat er noch einen offenen Vertrag
                        // (Filialwechsel!), ist dieser Filial-Austritt NICHT sein Austritt →
                        // ein evtl. stehengebliebenes Austrittsdatum wird AKTIV entfernt.
                        // Walter-Bug 05.07.2026: die frühere Filial-genaue Prüfung war fragil
                        // (null-Filiale / Reihenfolge) und liess den Austritt beim
                        // Gesamt-Import wieder aufleben.
                        var todayD = DateTime.Today;
                        bool hasOpenContract = await _db.Employments
                            .AnyAsync(em => em.EmployeeId == emp.Id
                                         && (em.ContractEndDate == null || em.ContractEndDate >= todayD), ct);
                        if (hasOpenContract)
                        {
                            if (emp.ExitDate.HasValue) emp.ExitDate = null;
                            if (!emp.IsActive) emp.IsActive = true;
                        }
                        else
                        {
                            emp.IsActive = false;
                            emp.ExitDate = eaw.To.Value.ToDateTime(TimeOnly.MinValue);
                        }
                    }
                    // Stale Austrittsdatum am BEREITS aktiven MA entfernen, wenn easy@work
                    // hier unbefristet ist (nahtloser Filialwechsel: Austritt alte Filiale =
                    // Eintritt neue Filiale → gleiches Datum blieb als Austritt stehen).
                    if (emp.IsActive && !eaw.To.HasValue && emp.ExitDate.HasValue)
                        emp.ExitDate = null;
                    // Phantom-MA (Supervisor): Stammdaten ja, aber NIE Vertrag/Bank
                    // anlegen (Walter 08.07.2026).
                    if (!emp.IsPayrollExcluded)
                    {
                        timelineWork.Add((emp, row.EawEmployeeId, jobGroupId, jobGroupCode, isKader, eaw.To));
                        if (!string.IsNullOrWhiteSpace(master.Iban)) bankWork.Add((emp, master.Iban!));
                    }
                    // NUR echte UPDATE-Zeilen als „aktualisiert" zählen. UNCHANGED-MA
                    // durchlaufen diesen Zweig nur für die Timeline-/Vertrags-Spiegelung
                    // (leere Auswahl = alle) — sie dürfen die Zahl NICHT hochtreiben,
                    // sonst zeigt jeder Import „54 aktualisiert" obwohl nichts änderte
                    // (Walter-Bug 05.07.2026).
                    if (row.Status == "UPDATE") res.CountUpdated++;
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
                if (emp.IsPayrollExcluded) continue;   // Phantom-MA: nie ein Vertrag (Walter 08.07.2026)
                var eaw = eawEmps.FirstOrDefault(e => e.Id == row.EawEmployeeId);
                var posName = PositionFor(row.EawEmployeeId);
                int? jobGroupId = null; string? jobGroupCode = null; bool isKader = false;
                if (!string.IsNullOrWhiteSpace(posName) && jobGroupByCode.TryGetValue(posName!.Trim(), out var jg))
                {
                    jobGroupId = jg.Id; jobGroupCode = jg.Code; isKader = jg.IsKader;
                }
                timelineWork.Add((emp, row.EawEmployeeId, jobGroupId, jobGroupCode, isKader, eaw?.To));
            }

            // ── Pre-Save-Schutz: Personalnummer-Kollision auf den TATSÄCHLICH zu
            //    schreibenden Werten (Walter 29.06.2026). Liest die echten
            //    EmployeeNumber direkt aus dem EF-ChangeTracker — egal über welchen
            //    Pfad sie gesetzt wurden — und bricht VOR dem Speichern ab, statt
            //    den unique-Constraint uq_employee_employee_number (und damit den
            //    ganzen atomaren Import) zu sprengen. Die EXAKTE doppelte Nummer +
            //    beide Seiten landen in res.NumberConflicts → Walter prüft sie in
            //    easy@work UND Cowork, bevor irgendetwas geschrieben wird.
            {
                var tracked = _db.ChangeTracker.Entries<Employee>()
                    .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
                    .ToList();
                var trackedIds = tracked.Where(e => e.Entity.Id != 0).Select(e => e.Entity.Id).ToHashSet();

                var dbHolders = (await _db.Employees.AsNoTracking()
                        .Where(e => !trackedIds.Contains(e.Id) && e.EmployeeNumber != null && e.EmployeeNumber != "")
                        .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
                        .ToListAsync(ct))
                    .GroupBy(e => e.EmployeeNumber!.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                var reported2   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var batchHolder = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in tracked)
                {
                    var num = (entry.Entity.EmployeeNumber ?? "").Trim();
                    if (string.IsNullOrEmpty(num)) continue;
                    var who   = $"{(entry.Entity.FirstName ?? "").Trim()} {(entry.Entity.LastName ?? "").Trim()}".Trim();
                    var eawId = entry.Entity.EasyAtWorkEmployeeId?.ToString() ?? "?";

                    if (dbHolders.TryGetValue(num, out var h))
                    {
                        if (reported2.Add(num))
                            res.NumberConflicts.Add(
                                $"Personalnummer {num} doppelt: Import-MA „{who}“ (eaw-id {eawId}) " +
                                $"↔ bereits in Cowork bei MA #{h.Id} „{($"{h.FirstName} {h.LastName}").Trim()}“. " +
                                $"Bitte in easy@work + Cowork prüfen.");
                    }
                    else if (batchHolder.TryGetValue(num, out var other))
                    {
                        if (reported2.Add(num))
                        {
                            var otherWho = $"{(other.FirstName ?? "").Trim()} {(other.LastName ?? "").Trim()}".Trim();
                            res.NumberConflicts.Add(
                                $"Personalnummer {num} doppelt im Import: „{otherWho}“ UND „{who}“ tragen dieselbe Nummer. Bitte in easy@work prüfen.");
                        }
                    }
                    else batchHolder[num] = entry.Entity;
                }

                if (res.NumberConflicts.Count > 0)
                {
                    res.Blocked = true;
                    // WICHTIG: Direkt-INSERT (kein SaveChanges) — die pendenten,
                    // verworfenen Entity-Änderungen dürfen NICHT mitgespeichert werden.
                    await LogEmployeeSyncRunAsync(req, res, ct);
                    return res;
                }
            }

            // Schutz gegen Doppelvergabe derselben easy@work-id (Walter 29.06.2026).
            await RevertDuplicateEawIdsAsync(res, ct);

            await _db.SaveChangesAsync(ct);

            // Schwangerschaft aus easy@work (Walter 27.07.2026) — nach dem
            // Employee-Save, damit neue MA eine Id haben. Best-effort.
            // Auch ohne ET aufrufen: erkennt «in easy gelöscht» (Hinweis in Notes).
            foreach (var (pEmp, pEawId, _, _, _, _) in timelineWork)
            {
                if (pEmp.Id == 0) continue;
                if (!masterByEaw.TryGetValue(pEawId, out var pMaster)) continue;
                try
                {
                    await SyncPregnancyFromEasyAsync(
                        pEmp, pMaster.PregnantMeldedatum, pMaster.PregnantErrechneterTermin,
                        res.Notes, orphanSink: null, ct);
                }
                catch (Exception ex)
                {
                    res.Notes.Add($"Schwangerschafts-Sync {pEmp.FirstName} {pEmp.LastName} übersprungen: {ex.Message}");
                }
            }

            // Tiefenimport: keine Verträge, keine Bankverbindungen (Walter 08.07.2026).
            if (req.SkipContracts) { timelineWork.Clear(); bankWork.Clear(); }

            // ── Zweiter Durchgang: komplette Employment-Timeline spiegeln (erst JETZT,
            //    wo alle Employee-IDs gespeichert sind → Natural-Key-Upsert greift).
            //    Alle historischen + aktuellen + zukünftigen Verträge/Lohnstufen aus
            //    easy@work werden als Employment-Versionen ge-upsertet. Walter 23.06.2026.
            if (timelineWork.Count > 0 || bankWork.Count > 0)
            {
                // Abschluss-Schutz: Verträge erst gesperrt wenn Definitiv
                // abgeschlossen (DTA) — während Kontrolle (provisorisch) noch erlaubt
                // (Walter 01.08.2026, präzisiert gegenüber 29.06.2026).
                var firstAllowed = await _editLock.GetFirstAllowedDateForContractsAsync(req.CompanyProfileId);
                foreach (var (temp, teawId, tJgId, tJgCode, tIsKader, tEawTo) in timelineWork)
                {
                    var tContracts = ContractsFor(teawId);
                    var tRates     = RatesFor(teawId);
                    // STRICT (Walter 08.07.2026): überlappende AKTIVE Verträge in
                    // easy@work → für diesen MA wird KEIN Vertrag importiert.
                    // Rein historische Überlappungen werden ignoriert.
                    var overlapErr = ValidateContractOverlaps(tContracts, activeAt);
                    if (overlapErr != null)
                    {
                        res.SkippedContracts.Add($"{temp.FirstName} {temp.LastName} ({temp.EmployeeNumber}): {overlapErr}");
                        continue;
                    }
                    var timeline   = BuildEmploymentTimeline(tContracts, tRates, activeAt, tIsKader);
                    _log.LogInformation("easy@work-Sync MA {Num}: contracts={C}, payRates={R}, timeline={T}",
                        temp.EmployeeNumber, tContracts.Count, tRates.Count, timeline.Count);
                    await SyncEmploymentTimelineAsync(_db, temp, req.CompanyProfileId, timeline, tJgId, tJgCode, tEawTo,
                        firstAllowed, res.SkippedContracts, res.Notes, ct);
                }
                foreach (var (bemp, iban) in bankWork)
                    await EnsureBankAccountAsync(bemp, iban, ct);
                await _db.SaveChangesAsync(ct);

                // Austritt je MA NACH der Vertrags-Timeline bewerten (Walter-Bug
                // 15.07.2026): uebernimmt auch ZUKUENFTIGE «Eingestellt bis»-Daten
                // als Austrittsdatum, sofern kein Vertrag darueber hinaus laeuft.
                bool exitChanged = false;
                foreach (var (temp2, _, _, _, _, tEawTo2) in timelineWork)
                    if (await ApplyExitAfterContractSyncAsync(temp2, tEawTo2, ct)) exitChanged = true;
                if (exitChanged) await _db.SaveChangesAsync(ct);

                // Probezeit wenn noch KEINE auf irgendeinem Vertrag (Walter 02.08.2026):
                // Entscheidend = erste Stempelzeit ab Eintrittsdatum (sonst Eintritt
                // provisorisch). Auch bei Sync-Splits (1-Tages-Vertrag + offen).
                // Idempotent: sobald irgendwo ProbationEndDate steht → unangetastet.
                var branchProb = await _db.CompanyProfiles
                    .Where(c => c.Id == req.CompanyProfileId)
                    .Select(c => c.ProbationMonths)
                    .FirstOrDefaultAsync(ct);
                if (branchProb.HasValue)
                {
                    var probEmpIds = timelineWork.Select(t => t.emp.Id).Where(eid => eid != 0).Distinct().ToList();
                    bool probChanged = false;
                    foreach (var eid in probEmpIds)
                    {
                        var emps = await _db.Employments
                            .Where(e => e.EmployeeId == eid).ToListAsync(ct);
                        if (emps.Count == 0 || emps.Any(e => e.ProbationEndDate != null)) continue;

                        // Ziel = offener Vertrag, sonst frühester Beginn.
                        var target = emps.Where(e => e.ContractEndDate == null)
                                         .OrderBy(e => e.ContractStartDate)
                                         .FirstOrDefault()
                                  ?? emps.OrderBy(e => e.ContractStartDate).First();

                        // Regel „befristet → keine Probezeit" (Walter 30.06.2026):
                        // vorbereitet, AKTUELL aber NICHT AKTIV.
                        const bool SkipProbationForBefristet = false;
                        var istBefristet = string.Equals(target.ContractType, "befristet", StringComparison.OrdinalIgnoreCase)
                                           || target.ContractEndDate.HasValue;
                        if (SkipProbationForBefristet && istBefristet) continue;

                        var contractStart = DateOnly.FromDateTime(target.ContractStartDate);
                        var entryDt = await _db.Employees.AsNoTracking()
                            .Where(e => e.Id == eid)
                            .Select(e => e.EntryDate)
                            .FirstOrDefaultAsync(ct);
                        DateOnly? entry = entryDt.HasValue ? DateOnly.FromDateTime(entryDt.Value) : null;
                        var reference = ProbationAnchor.ReferenceStart(entry, contractStart);

                        // Erste Stempelzeit ab Eintritt (= 1. Arbeitstag).
                        var stampQ = _db.EmployeeTimeEntries.Where(t => t.EmployeeId == eid);
                        if (entry.HasValue)
                            stampQ = stampQ.Where(t => t.EntryDate >= entry.Value);
                        var firstStamp = await stampQ
                            .OrderBy(t => t.EntryDate)
                            .Select(t => (DateOnly?)t.EntryDate)
                            .FirstOrDefaultAsync(ct);

                        var basis = firstStamp ?? reference;
                        var ende = ProbationAnchor.ComputeEnd(basis, branchProb.Value);
                        target.ProbationEndDate      = ende.ToDateTime(TimeOnly.MinValue);
                        target.ProbationPeriodMonths = branchProb.Value == 14 ? null : branchProb.Value;

                        if (firstStamp.HasValue)
                        {
                            target.ProbationStartDate = firstStamp.Value;
                            _db.EmploymentProbationLogs.Add(new EmploymentProbationLog
                            {
                                EmploymentId         = target.Id,
                                EventDate            = firstStamp.Value,
                                EventType            = "ANKER",
                                DeltaDays            = ProbationAnchor.Delta(reference, firstStamp.Value),
                                Grund                = ProbationAnchor.Grund(reference, firstStamp.Value),
                                ProbezeitEndeNachher = ende,
                                CreatedAt            = DateTime.Now,   // Spalte ist timestamp WITHOUT time zone → keine UTC-Kind
                            });
                        }
                        probChanged = true;
                    }
                    if (probChanged) await _db.SaveChangesAsync(ct);
                }
            }

            // Sync-State
            var st = await _db.EasyAtWorkSyncStates
                .FirstOrDefaultAsync(s => s.CompanyProfileId == req.CompanyProfileId && s.Resource == "EMPLOYEE", ct);
            if (st == null) { st = new EasyAtWorkSyncState { CompanyProfileId = req.CompanyProfileId, Resource = "EMPLOYEE" }; _db.EasyAtWorkSyncStates.Add(st); }
            st.LastSyncAt = DateTime.UtcNow;
            st.LastRowCount = res.CountInserted + res.CountUpdated;
            st.LastError = null;
            await _db.SaveChangesAsync(ct);

            // Lauf protokollieren (Walter 08.07.2026) — sichtbar im Sync-Log
            // auf der easy@work-Seite, wie der Stempel-Auto-Sync.
            await LogEmployeeSyncRunAsync(req, res, ct);
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

        /// <summary>
        /// Erfassungsfehler in easy@work (Walter-Vorgabe 08.07.2026): gesetzt,
        /// wenn der Vertrag in sich widersprüchlich ist (z.B. Typ Flex/MTP mit
        /// Stunden pro MONAT — FLEX/MTP haben IMMER Stunden pro Woche). Ein
        /// Vertrag mit DataError wird NIE importiert (EnsureEmployment +
        /// Timeline überspringen ihn mit Meldung); Korrektur erfolgt in easy@work.
        /// </summary>
        public string?   DataError;

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

            // Erfassungsfehler-Validierung (Walter-Vorgabe 08.07.2026): FLEX und
            // MTP sind Stundenlohn-Modelle mit Stunden PRO WOCHE — «17 h pro
            // Monat» gibt es nicht. Sagt der easy@work-Typ Flex/MTP, aber die
            // Vertragsart steht auf Monat/Prozent, ist der Vertrag in easy@work
            // falsch erfasst → DataError, wird NIE importiert. (Ohne diese
            // Prüfung wurde z.B. «Flex, 17.00, Monat» still zu FIX klassifiziert,
            // fand keinen Monatslohn und blieb als Dauer-Hinweis hängen — Fall
            // Beza 750080.)
            bool typIstStundenlohn = typ.Contains("FLEX") || typ.Contains("MTP") || typ.Contains("TPM");
            if (typIstStundenlohn && (amt.StartsWith("month") || amt.StartsWith("percent")))
            {
                var anz = c.Amount ?? c.WeekHours;
                info.DataError = $"Erfassungsfehler in easy@work: Vertragstyp «{c.Type}» (Stundenlohn) "
                               + $"mit Vertragsart «{c.AmountType}»{(anz.HasValue ? $" ({anz:0.##})" : "")} erfasst — "
                               + "FLEX/MTP haben IMMER Stunden pro WOCHE. Vertrag wird NICHT importiert; "
                               + "bitte in easy@work auf «Woche» korrigieren.";
                // Modell nur für die Anzeige nach Typ setzen — importiert wird nichts.
                info.EmploymentModel = typ.Contains("FLEX") ? "FLEX" : "MTP";
                info.SalaryType = "hourly";
            }
            else if (amt.StartsWith("month") || amt.StartsWith("percent")) { info.EmploymentModel = "FIX"; info.SalaryType = "monthly"; }
            else
            {
                var wochenStd = c.Amount ?? c.WeekHours;
                bool isMtp = typ.Contains("MTP") || typ.Contains("TPM")
                             || (wochenStd.HasValue && wochenStd.Value > 17m);
                info.EmploymentModel = isMtp ? "MTP" : "FLEX";
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

        // STRICT: Lohn ist PFLICHT (Walter-Vorgabe 08.07.2026) — der Import rät
        // nie und akzeptiert nie einen Vertrag ohne Lohn. EINZIGE Ausnahme:
        // FIX-M (Kader/GF) — dort darf der Lohn aus Vertraulichkeit fehlen und
        // wird direkt im OneCrew-Vertrag erfasst. Platzhalter ≤ CHF 1.00 gilt
        // als «kein Lohn».
        if (info.DataError == null && c != null && info.EmploymentModel != "FIX-M")
        {
            if ((info.EmploymentModel == "FLEX" || info.EmploymentModel == "MTP") && !info.HourlyRate.HasValue)
                info.DataError = $"Kein Stundenlohn-Tarif in easy@work erfasst (gültig per {rateDate:dd.MM.yyyy}) — "
                               + "FLEX/MTP brauchen zwingend einen Stundenlohn. Vertrag wird NICHT importiert; "
                               + "bitte den Tarif in easy@work erfassen.";
            else if (info.EmploymentModel == "FIX" && !info.MonthlySalary.HasValue && !info.MonthlySalaryFte.HasValue)
                info.DataError = $"Kein Monatslohn-Tarif in easy@work erfasst (gültig per {rateDate:dd.MM.yyyy}) — "
                               + "FIX braucht zwingend einen Monatslohn (nur FIX-M darf ohne Lohn sein). "
                               + "Vertrag wird NICHT importiert; bitte den Tarif in easy@work erfassen.";
        }
        return info;
    }

    /// <summary>
    /// STRICT-Validierung der easy@work-Verträge eines MA (Walter-Vorgabe
    /// 08.07.2026): Verträge dürfen sich NICHT überschneiden — auch nicht um
    /// einen Tag (Ende 1.4. + neuer Beginn 1.4. ist falsch; korrekt wäre Ende
    /// 31.3.). Ebenso darf es nur EINEN offenen (unbefristeten) Vertrag geben.
    /// Liefert die Fehlermeldung oder null. Bei Fehler wird für diesen MA
    /// KEIN Vertrag importiert — Korrektur erfolgt in easy@work.
    /// </summary>
    public static string? ValidateContractOverlaps(List<EawContract>? contracts, DateOnly? nurAktiveAb = null)
    {
        if (contracts == null || contracts.Count < 2) return null;
        var ordered = contracts.Where(c => c.From.HasValue)
            .OrderBy(c => c.From!.Value).ThenBy(c => c.To ?? DateOnly.MaxValue)
            .ToList();
        for (int i = 0; i < ordered.Count - 1; i++)
        {
            var a = ordered[i];
            var b = ordered[i + 1];
            // Nur-Historie-Filter (Walter-Vorgabe 08.07.2026): Überlappungen, die
            // AUSSCHLIESSLICH abgelaufene Verträge betreffen, werden NICHT gemeldet —
            // die Historie lebt im alten Lohnprogramm / in den MA-Dokumenten. Gemeldet
            // wird nur, wenn ein beteiligter Vertrag aktiv/zukünftig ist.
            if (nurAktiveAb.HasValue)
            {
                bool aAktiv = !a.To.HasValue || a.To.Value >= nurAktiveAb.Value;
                bool bAktiv = !b.To.HasValue || b.To.Value >= nurAktiveAb.Value;
                if (!aAktiv && !bAktiv) continue;
            }
            if (!a.To.HasValue)
                return $"Erfassungsfehler in easy@work: Vertrag ab {a.From:dd.MM.yyyy} ist OFFEN (kein Bis-Datum), "
                     + $"aber es existiert ein weiterer Vertrag ab {b.From:dd.MM.yyyy}. "
                     + "Verträge werden NICHT importiert; bitte den älteren Vertrag in easy@work beenden.";
            if (a.To.Value >= b.From!.Value)
                return $"Erfassungsfehler in easy@work: Verträge überschneiden sich — "
                     + $"«{a.Type}» endet am {a.To:dd.MM.yyyy}, der nächste «{b.Type}» beginnt bereits am {b.From:dd.MM.yyyy}. "
                     + $"Korrekt wäre: Ende am {b.From.Value.AddDays(-1):dd.MM.yyyy}. "
                     + "Verträge werden NICHT importiert; bitte in easy@work korrigieren.";
        }
        return null;
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
                // ManualOverride = Lohn wird LOKAL in OneCrew gepflegt, easy@work
                // fasst ihn nie an. Zwei Auslöser:
                //   a) Platzhalterlohn ≤ 1.00 (bestehende Regel), ODER
                //   b) GAR KEIN Tarif in easy@work erfasst («Pas de taux») —
                //      Walter-Vorgabe 08.07.2026: vertrauliche Löhne (z.B. GF)
                //      stehen bewusst nicht in easy@work; der Lohn wird im
                //      OneCrew-Vertrag erfasst (inkl. Mindestlohn-Prüfung) und
                //      darf vom Sync weder geleert noch das Modell gekippt werden.
                EasyAtWorkManualOverride = rAt == null
                    || (rAt.Rate.HasValue && rAt.Rate.Value <= 1.00m),
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
        int? jobGroupId, string? jobGroupCode, DateOnly? eawTo,
        DateOnly? firstAllowedDate = null, List<string>? skippedContracts = null,
        List<string>? cleanupNotes = null,
        CancellationToken ct = default)
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

            // Erfassungsfehler in easy@work (Walter 08.07.2026, z.B. FLEX mit
            // Stunden pro Monat, fehlender Lohn): Segment wird NIE importiert.
            // ABGELAUFENE Segmente werden STILL weggelassen (Historie lebt im
            // alten Lohnprogramm / in den MA-Dokumenten) — nur bei AKTIVEN/
            // zukünftigen Segmenten gibt es die Meldung. Ein evtl. schon
            // vorhandener Vertrag bleibt unangetastet (matched → nicht gekappt).
            if (info.DataError != null)
            {
                if (active)
                    skippedContracts?.Add($"{emp.FirstName} {emp.LastName} ({emp.EmployeeNumber}), Segment ab {seg.Start:dd.MM.yyyy}: {info.DataError}");
                var already = existingAll.FirstOrDefault(e => !matched.Contains(e) && e.ContractStartDate == startDt);
                if (already != null) matched.Add(already);
                continue;
            }

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

            // Abschluss-Schutz (Walter 29.06.2026 / präzisiert 01.08.2026):
            // Verträge/Segmente mit Start vor FirstAllowedDate (nur Definitiv
            // «abgeschlossen») werden NICHT importiert. Während provisorisch
            // (Kontrolle vor DTA) ist Import erlaubt. Vorhandenes Segment bleibt
            // unangetastet (matched); fehlendes → klare Skip-Meldung.
            if (firstAllowedDate.HasValue && seg.Start < firstAllowedDate.Value)
            {
                if (existing != null) matched.Add(existing);
                else skippedContracts?.Add(
                    $"Vertrag ab {seg.Start:dd.MM.yyyy} von {emp.FirstName} {emp.LastName} (Nr. {emp.EmployeeNumber}) konnte wegen abgeschlossener Lohnperiode nicht importiert werden.");
                continue;
            }

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
                    EmploymentModel      = string.IsNullOrWhiteSpace(info.EmploymentModel) ? "FLEX"    : info.EmploymentModel!,
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
                else if (wasManualOverride)
                {
                    // Override AUFLÖSEN (Walter 08.07.2026): easy@work liefert für
                    // dieses Segment jetzt einen ECHTEN Lohn (kein Platzhalter, kein
                    // fehlender Tarif mehr) — der Sperr-Grund ist weg, easy@work ist
                    // wieder führend. Ohne diese Auflösung blieben Zeilen, die in der
                    // Fehl-Import-Ära den Override-Stempel bekamen, FÜR IMMER
                    // eingefroren (Fall Beza 750080: FIX 1.4.–1.4. statt FLEX offen).
                    existing.EasyAtWorkManualOverride = false;
                    wasManualOverride = false;
                    cleanupNotes?.Add($"{emp.FirstName} {emp.LastName} ({emp.EmployeeNumber}): "
                        + $"easy@work-Override am Vertrag ab {seg.Start:dd.MM.yyyy} aufgelöst — "
                        + "easy@work liefert wieder einen echten Lohn und ist führend.");
                }

                // Kader-Korrektur AUCH bei lokal gepflegtem Lohn (Walter 08.07.2026):
                // FIX → FIX-M ist eine reine Modell-Umbenennung ohne Wechsel der
                // Lohnbasis (beide Monatslohn) — der vertraulich in OneCrew erfasste
                // Lohn bleibt unangetastet. Manager-Funktionen (is_kader, z.B.
                // REST_MANAGER) sind IMMER FIX-M.
                if (info.EmploymentModel == "FIX-M" && existing.EmploymentModel == "FIX")
                {
                    existing.EmploymentModel = "FIX-M";
                    existing.SalaryType      = "monthly";
                    if (jobGroupId != null) { existing.JobGroupId = jobGroupId; existing.JobTitle = jobGroupCode ?? existing.JobTitle; }
                }

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

        // Überlappende Cowork-Zeilen, die NICHT von der Timeline gematcht wurden:
        //
        // AUTO-CLEANUP (Walter-Vorgabe 08.07.2026): sync-erzeugte Vertrags-Leichen
        // aus früheren (Fehl-)Importen werden GELÖSCHT statt nur gekappt — aber
        // NUR unter drei strengen Bedingungen:
        //   1) die Zeile stammt vom easy@work-Sync (EasyAtWorkContractId gesetzt),
        //   2) sie ist NICHT lokal gepflegt (kein EasyAtWorkManualOverride —
        //      vertrauliche Löhne bleiben unantastbar),
        //   3) sie wurde NIE in einem abgeschlossenen Lohnlauf verwendet
        //      (gleiche Prüfung wie EmploymentsController.CanDelete).
        // Alles andere wird wie bisher gekappt (Historie bleibt).
        var todayDt = DateTime.Today;
        foreach (var ex in existingAll.Where(e => !matched.Contains(e)))
        {
            // Override-Zeilen sind grundsätzlich geschützt — AUSSER ihr Zeitraum
            // ist von der neuen Timeline VOLL abgedeckt (Walter 08.07.2026): dann
            // ist die Zeile ein redundanter Splitter aus der Fehl-Import-Ära
            // (der Override wurde damals automatisch gestempelt), kein manuell
            // gepflegter Vertrag. Nicht abgedeckte Override-Zeilen (z.B. GF-
            // Verträge, die easy@work gar nicht kennt) bleiben unantastbar.
            bool zeitraumAbgedeckt = timeline.Any(s =>
                s.Info.DataError == null
                && s.Start <= DateOnly.FromDateTime(ex.ContractStartDate)
                && (!s.End.HasValue || (ex.ContractEndDate.HasValue
                        && s.End.Value >= DateOnly.FromDateTime(ex.ContractEndDate.Value))));
            bool syncErzeugt = ex.EasyAtWorkContractId.HasValue
                               && (!ex.EasyAtWorkManualOverride || zeitraumAbgedeckt);
            if (syncErzeugt)
            {
                var exStart = DateOnly.FromDateTime(ex.ContractStartDate);
                var exEnd   = ex.ContractEndDate.HasValue
                    ? (DateOnly?)DateOnly.FromDateTime(ex.ContractEndDate.Value) : null;
                bool inLohnVerwendet = await (
                    from snap in db.PayrollSnapshots
                    join per in db.PayrollPerioden on snap.PayrollPeriodeId equals per.Id
                    where snap.EmployeeId == emp.Id
                       && per.Status == "abgeschlossen"
                       && per.PeriodTo >= exStart
                       && (exEnd == null || per.PeriodFrom <= exEnd)
                    select snap.Id).AnyAsync(ct);
                if (!inLohnVerwendet)
                {
                    db.Employments.Remove(ex);
                    cleanupNotes?.Add($"{emp.FirstName} {emp.LastName} ({emp.EmployeeNumber}): "
                        + $"veralteter Sync-Vertrag {ex.EmploymentModel} {ex.ContractStartDate:dd.MM.yyyy}"
                        + $"–{(ex.ContractEndDate.HasValue ? ex.ContractEndDate.Value.ToString("dd.MM.yyyy") : "offen")} "
                        + "gelöscht (von keinem easy@work-Segment mehr abgedeckt, nie im Lohn verwendet).");
                    continue;
                }
            }

            // Übersprungene (geschützte) Segmente NICHT zum Kappen heranziehen,
            // sonst würden vorhandene Zeilen anhand nicht-importierter Segmente
            // gekürzt.
            foreach (var seg in timeline
                         .Where(s => !(firstAllowedDate.HasValue && s.Start < firstAllowedDate.Value))
                         .OrderBy(s => s.Start))
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
    /// <summary>
    /// Liefert den KONKRETEN Grund, warum ein Vertrag strukturell korrigiert werden
    /// muss — oder null, wenn alles stimmt. Der Grund landet als Klartext im
    /// Vorschau-Reason, damit man pro MA sieht, welches Feld gemeint ist
    /// (Walter 29.06.2026). Wichtig: KEINE Enddatum-Regel mehr — die Timeline
    /// (`SyncEmploymentTimelineAsync`) setzt Vertrags-Enddaten aus den easy@work-
    /// Contracts, NICHT aus employee.to. Der frühere Vergleich gegen eaw.To
    /// flaggte ein legitimes contract-seitiges Enddatum bei JEDEM Lauf erneut →
    /// Endlos-„UPDATE", obwohl die Timeline das Enddatum bereits idempotent
    /// gespiegelt hatte. Enddaten gehören damit NICHT in diesen Klassifizierer.
    /// </summary>
    private static string? EmploymentFixReason(Employment e)
    {
        var m = e.EmploymentModel;
        if (m == "FLEX" || m == "MTP")
        {
            if (e.EmploymentPercentage != null) return "Pensum % bei Stundenlohn-Vertrag";
            if (e.HourlyRate == null)           return "Stundenlohn fehlt";
            if (m == "MTP" && e.GuaranteedHoursPerWeek == null) return "garantierte Wochenstunden fehlen (MTP)";
        }
        else if (m == "FIX")
        {
            // Als FIX erfasst, aber ohne Monatslohn — meist eine Fehlklassifizierung.
            // Der Import korrigiert Modell + Lohn aus easy@work.
            // FIX-M ist hier bewusst AUSGENOMMEN (Walter-Vorgabe 08.07.2026):
            // FIX-M ohne Monatslohn ist der legale GF-Fall (vertraulicher Lohn,
            // wird direkt im OneCrew-Vertrag erfasst) — kein Korrektur-Flag.
            if (e.MonthlySalary == null && e.MonthlySalaryFte == null)
                return "als Monatslohn-Vertrag (FIX) erfasst, aber ohne Monatslohn — Modell/Lohn wird aus easy@work korrigiert";
        }
        return null;
    }

    private async Task EnsureEmploymentAsync(
        Employee emp, EawEmployee eaw, int companyProfileId, bool isNewEmployee, HistContractInfo info, CancellationToken ct)
    {
        // Erfassungsfehler in easy@work (Walter 08.07.2026, z.B. FLEX mit Stunden
        // pro Monat): Vertrag wird NIE angelegt/verändert — Korrektur in easy@work.
        if (info.DataError != null) return;

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

                // Ausnahmeregelung «vertraulicher Lohn» (Walter-Vorgabe 08.07.2026):
                // liefert easy@work KEINEN Lohn (kein Tarif erfasst, z.B. GF aus
                // Vertraulichkeit) und ist der Lohn im OneCrew-Vertrag bereits
                // erfasst (oder der Vertrag als lokal gepflegt markiert), dann
                // Modell + Strukturfelder NICHT anfassen — sonst würde z.B. ein
                // FIX-Vertrag auf MTP gekippt und der manuell erfasste Monatslohn
                // geleert. Der Lohn wird in OneCrew gepflegt (Mindestlohn-Prüfung
                // greift dort wie überall).
                bool eawHatLohn = info.HourlyRate.HasValue || info.MonthlySalary.HasValue || info.MonthlySalaryFte.HasValue;
                bool coHatLohn  = existing.HourlyRate.HasValue || existing.MonthlySalary.HasValue || existing.MonthlySalaryFte.HasValue;
                bool lohnLokalGepflegt = !eawHatLohn && (coHatLohn || existing.EasyAtWorkManualOverride);

                // FIX ↔ FIX-M ist trotz lokal gepflegtem Lohn erlaubt (Walter
                // 08.07.2026): gleiche Monatslohn-Basis, der erfasste Lohn bleibt.
                // Wichtig für Kader-Funktionen (REST_MANAGER etc.) ⇒ FIX-M.
                bool monatsZuMonats = (info.EmploymentModel == "FIX" || info.EmploymentModel == "FIX-M")
                                   && (existing.EmploymentModel == "FIX" || existing.EmploymentModel == "FIX-M");

                if (istAktuellerVertrag && !string.IsNullOrWhiteSpace(info.EmploymentModel)
                    && (!lohnLokalGepflegt || monatsZuMonats))
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
                if (istAktuellerVertrag && !lohnLokalGepflegt && (effModel == "FLEX" || effModel == "MTP"))
                {
                    existing.EmploymentPercentage = null;
                    existing.MonthlySalary        = null;
                    existing.MonthlySalaryFte     = null;
                    existing.WeeklyHours          = null;
                    existing.GuaranteedHoursPerWeek = effModel == "MTP"
                        ? (info.GuaranteedHoursPerWeek ?? existing.GuaranteedHoursPerWeek)
                        : null;
                }
                else if (istAktuellerVertrag && !lohnLokalGepflegt && (effModel == "FIX" || effModel == "FIX-M"))
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
            EmploymentModel      = string.IsNullOrWhiteSpace(future.EmploymentModel) ? "FLEX"    : future.EmploymentModel!.Trim(),
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

    /// <summary>Austritt nach dem Vertrags-Sync bewerten (Walter-Bug 15.07.2026):
    /// Ist in easy@work «Eingestellt bis» (eaw.To) gesetzt und läuft KEIN Vertrag
    /// über dieses Datum hinaus (kein offenes Ende, kein späteres Ende — sonst
    /// Filialwechsel/Zweitfiliale), wird eaw.To als Austrittsdatum übernommen —
    /// auch wenn es in der ZUKUNFT liegt (geplanter Austritt sichtbar).
    /// IsActive bleibt true bis zum Austrittstag. Liefert true bei Änderung
    /// (Aufrufer speichert).</summary>
    private async Task<bool> ApplyExitAfterContractSyncAsync(Employee emp, DateOnly? eawTo, CancellationToken ct)
    {
        if (!eawTo.HasValue) return false;
        var toDt = eawTo.Value.ToDateTime(TimeOnly.MinValue);
        bool laeuftWeiter = await _db.Employments.AnyAsync(em => em.EmployeeId == emp.Id
            && (em.ContractEndDate == null || em.ContractEndDate > toDt), ct);
        if (laeuftWeiter) return false;

        bool changed = false;
        if (emp.ExitDate?.Date != toDt.Date) { emp.ExitDate = toDt; changed = true; }
        bool aktivSoll = eawTo.Value >= DateOnly.FromDateTime(DateTime.Today);
        if (emp.IsActive != aktivSoll) { emp.IsActive = aktivSoll; changed = true; }
        return changed;
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
            EmploymentModel      = string.IsNullOrWhiteSpace(employmentModel) ? "FLEX"    : employmentModel!.Trim(),
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

    private static List<FieldDiff> ComputeDiffs(Employee? co, EmployeeMasterData data)
    {
        var diffs = new List<FieldDiff>();

        void Add(string field, string? cur, string? eawVal, bool exactCase = false)
        {
            var trimEaw = string.IsNullOrWhiteSpace(eawVal) ? null : eawVal.Trim();
            var trimCur = string.IsNullOrWhiteSpace(cur)    ? null : cur.Trim();
            // Nur setzen, wenn easy@work einen NICHT-leeren Wert hat UND sich unterscheidet.
            // Namen case-SENSITIV (Walter 10.07.2026): «KITANOVSKA» → «Kitanovska».
            var cmp = exactCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            var willSet = trimEaw != null && !string.Equals(trimEaw, trimCur, cmp);
            diffs.Add(new FieldDiff { Field = field, Cowork = trimCur, Easy = trimEaw, WillSet = willSet });
        }
        void AddNullable(string field, string? cur, string? eawVal, bool mayClear)
        {
            var trimEaw = string.IsNullOrWhiteSpace(eawVal) ? null : eawVal.Trim();
            var trimCur = string.IsNullOrWhiteSpace(cur)    ? null : cur.Trim();
            var willSet = mayClear
                ? !string.Equals(trimEaw, trimCur, StringComparison.OrdinalIgnoreCase)
                : trimEaw != null && !string.Equals(trimEaw, trimCur, StringComparison.OrdinalIgnoreCase);
            diffs.Add(new FieldDiff { Field = field, Cowork = trimCur, Easy = trimEaw, WillSet = willSet });
        }

        Add("Vorname",     co?.FirstName,   data.FirstName,  exactCase: true);
        Add("Nachname",    co?.LastName,    data.LastName,   exactCase: true);
        Add("Kurzname",    co?.ShortName,   data.ShortName,  exactCase: true);
        AddNullable("Anrede", co?.Salutation, data.Salutation, data.Gender == "divers");
        Add("Geschlecht",  co?.Gender,      data.Gender);
        Add("Geburtstag",  co?.DateOfBirth?.ToString("yyyy-MM-dd"),
                            data.DateOfBirth?.ToString("yyyy-MM-dd"));
        Add("AHV-Nummer",  co?.SocialSecurityNumber, data.Ahv);
        Add("Zivilstand",  co?.MaritalStatus, data.MaritalStatus);
        if (co == null || !string.IsNullOrWhiteSpace(co.LanguageCode))
            Add("Sprache", co?.LanguageCode, data.LanguageCode);
        AddNullable("Briefanrede", co?.LetterSalutation, data.LetterSalutation, data.Gender == "divers");
        Add("Strasse",     co?.Street,      data.Street);
        Add("PLZ",         co?.ZipCode,     data.ZipCode);
        Add("Ort",         co?.City,        data.City);
        Add("Kanton",      co?.CantonCode,  data.CantonCode);
        Add("Land",        co?.Country,     data.Country);
        Add("Nationalität", co?.Nationality, data.Nationality);
        Add("Telefon",     co?.PhoneMobile, data.Phone);
        Add("E-Mail",      co?.Email,       data.Email);
        Add("Eintritt",    co?.EntryDate?.ToString("yyyy-MM-dd"),
                            data.EntryDate?.ToString("yyyy-MM-dd"));
        Add("Austritt",    co?.ExitDate?.ToString("yyyy-MM-dd"),
                            data.ExitDate?.ToString("yyyy-MM-dd"));
        return diffs;
    }

    private static void ApplyDiffs(Employee emp, List<FieldDiff> diffs, EmployeeMasterData data)
    {
        foreach (var d in diffs.Where(x => x.WillSet))
        {
            switch (d.Field)
            {
                case "Vorname":      emp.FirstName    = d.Easy ?? ""; break;
                case "Nachname":     emp.LastName     = d.Easy ?? ""; break;
                case "Kurzname":     emp.ShortName    = d.Easy; break;
                case "Anrede":       emp.Salutation   = d.Easy; break;
                case "Geschlecht":   emp.Gender       = d.Easy; break;
                // „Personalnummer" wird NICHT hier angewendet — der Wechsel legt
                // einen Alias an (braucht den DbContext) und läuft im Commit-Pfad
                // via SaveNumberChange. Der Diff dient nur der Anzeige + Status UPDATE.
                case "Geburtstag":   emp.DateOfBirth  = DateTime.TryParse(d.Easy, out var dob) ? dob : emp.DateOfBirth; break;
                case "AHV-Nummer":   emp.SocialSecurityNumber = d.Easy; break;
                case "Zivilstand":   emp.MaritalStatus = d.Easy; break;
                case "Sprache":      emp.LanguageCode = d.Easy; break;
                case "Briefanrede":  emp.LetterSalutation = d.Easy; break;
                case "Strasse":      emp.Street       = d.Easy; break;
                case "PLZ":          emp.ZipCode      = d.Easy; break;
                case "Ort":          emp.City         = d.Easy; break;
                case "Kanton":       emp.CantonCode   = d.Easy; break;
                case "Land":         emp.Country      = d.Easy; break;
                case "Nationalität":
                    emp.Nationality = d.Easy;
                    emp.NationalityId = data.NationalityId;
                    break;
                case "Telefon":      emp.PhoneMobile  = d.Easy; break;
                case "E-Mail":       emp.Email        = d.Easy; break;
                case "Eintritt":     emp.EntryDate    = DateTime.TryParse(d.Easy, out var ed) ? ed : emp.EntryDate; break;
                case "Austritt":     emp.ExitDate     = DateTime.TryParse(d.Easy, out var xd) ? xd : emp.ExitDate; break;
            }
        }
    }

    /// <summary>
    /// Datum aus easy@work-Property-Wert: ISO/UTC via <see cref="EawDateUtil"/>,
    /// sonst de-CH («3. Juli 2026», «03.07.2026»).
    /// </summary>
    private static DateOnly? ParsePropertyDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        var swiss = EawDateUtil.ParseSwissDate(s);
        if (swiss.HasValue) return swiss;
        var de = System.Globalization.CultureInfo.GetCultureInfo("de-CH");
        if (DateOnly.TryParse(s, de, System.Globalization.DateTimeStyles.None, out var d1))
            return d1;
        if (DateTime.TryParse(s, de, System.Globalization.DateTimeStyles.None, out var d2))
            return DateOnly.FromDateTime(d2);
        if (DateOnly.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var d3))
            return d3;
        return null;
    }

    // ─────────────────────────── Helpers ────────────────────────────

    private static string? SalutationFromGender(string? g)
    {
        if (string.IsNullOrWhiteSpace(g)) return null;
        return g.Trim().ToLowerInvariant() switch
        {
            "male" or "m" or "herr"   => "Herr",
            "female" or "f" or "frau" => "Frau",
            "divers" or "diverse" or "andere" or "other" or "nonbinary" or "non-binary" or "x" or "d" => null,
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
    /// «alt»-Suffix der Pre-Mirus-Archivnummern entfernen (nur End-Suffix).
    /// easy@work kennt das Suffix nie — Vergleich immer über die Basis.
    /// </summary>
    public static string StripAltSuffix(string? number)
    {
        var n = (number ?? "").Trim();
        if (n.Length >= 3 && n.EndsWith("alt", StringComparison.OrdinalIgnoreCase))
            return n[..^3];
        return n;
    }

    /// <summary>
    /// Dieselbe Badge-Nummer, egal ob mit oder ohne Archiv-«alt»-Suffix
    /// (z.B. «581039alt» ≡ «581039»). Leere Werte gelten nicht als gleich.
    /// </summary>
    public static bool IsSameNumberIgnoringAlt(string? a, string? b)
    {
        var sa = StripAltSuffix(a);
        var sb = StripAltSuffix(b);
        if (sa.Length == 0 || sb.Length == 0) return false;
        return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Darf die easy@work-Nummer die Hauptnummer werden?
    /// Nie, wenn sie nur das «alt»-Suffix der bestehenden Archivnummer
    /// abstreift (Walter-Bug 18.07.2026). Echte neue Nummern (Wiedereintritt)
    /// bleiben erlaubt.
    /// </summary>
    public static bool ShouldPromoteEawNumberToMain(
        string? existNum, string? newNum, bool eawRecordAktiv, bool nummerBesetzt)
    {
        if (string.IsNullOrWhiteSpace(newNum)) return false;
        var neu = newNum.Trim();
        var alt = (existNum ?? "").Trim();
        if (string.Equals(neu, alt, StringComparison.OrdinalIgnoreCase)) return false;
        if (nummerBesetzt) return false;
        if (neu.EndsWith("alt", StringComparison.OrdinalIgnoreCase)) return false;
        if (IsSameNumberIgnoringAlt(alt, neu)) return false;
        bool mainIstArchiv = alt.EndsWith("alt", StringComparison.OrdinalIgnoreCase);
        return eawRecordAktiv || mainIstArchiv;
    }

    /// <summary>
    /// Nummernwechsel: die bisherige Personalnummer als Alias sichern (mit
    /// valid_to = heute) und die neue als employee_number setzen.
    /// </summary>
    /// <summary>
    /// „hans muster" (Test-/Platzhalter-Datensatz, wie „John Doe") — wird beim
    /// easy@work-Import NIE angelegt/aktualisiert (Walter 29.06.2026).
    /// Gross-/Kleinschreibung + umgebende Leerzeichen egal.
    /// </summary>
    public static bool IsHansMuster(string? first, string? last)
        => string.Equals((first ?? "").Trim(), "hans",   StringComparison.OrdinalIgnoreCase)
        && string.Equals((last  ?? "").Trim(), "muster", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Schutz gegen Doppelvergabe derselben easy@work-id (Walter 29.06.2026):
    /// genau das erzeugte die „mehrere Lohn-MA"-Blockade (ein „…alt"-Archiv-MA
    /// trug die id eines aktiven MA). Geht VOR dem Speichern alle getrackten
    /// Employee-Änderungen durch — trägt ein MA eine easy@work-id, die bereits
    /// einem ANDEREN MA gehört (in der DB, nicht versteckt, ODER im selben Lauf),
    /// wird die id auf dem neu/geänderten Datensatz zurückgenommen (Added → null,
    /// Modified → vorheriger Wert) und gemeldet. Der bestehende rechtmässige
    /// Träger behält die id. So kann keine id mehr auf zwei MA zeigen.
    /// </summary>
    /// <summary>
    /// MA-Sync-Lauf ins easyatwork_sync_log protokollieren (Walter 08.07.2026) —
    /// bisher loggte nur der Stempel-Auto-Sync; manuelle MA-Commits waren nicht
    /// nachvollziehbar (v.a. BLOCKIERTE Läufe gingen unter). Direkt-INSERT per
    /// SQL, damit bei einem blockierten Lauf NICHT versehentlich die pendenten
    /// (verworfenen) Entity-Änderungen mitgespeichert werden.
    /// </summary>
    private async Task LogEmployeeSyncRunAsync(SyncRequest req, SyncResult res, CancellationToken ct)
    {
        try
        {
            var status = res.Blocked ? "BLOCKED" : "OK";
            var message = res.Blocked
                ? "MA-Sync BLOCKIERT — nichts geschrieben. " + string.Join(" | ", res.NumberConflicts.Take(2))
                  + (res.NumberConflicts.Count > 2 ? " …" : "")
                : $"MA-Sync: {res.CountNew} neu / {res.CountUpdate} Updates in der Vorschau; "
                  + $"geschrieben: {res.CountInserted} neu, {res.CountUpdated} aktualisiert"
                  + (res.CountConflict > 0 ? $"; ⚠ {res.CountConflict} CONFLICT(s) übersprungen (Fehler in easy@work)" : "")
                  + (res.SkippedContracts.Count > 0 ? $"; {res.SkippedContracts.Count} Vertrag/Verträge übersprungen" : "")
                  + (req.SkipContracts ? " (Tiefenimport: ohne Verträge)" : "")
                  + (req.SelectedNumbers is { Count: > 0 } ? $" (Auswahl: {req.SelectedNumbers.Count} MA)" : " (alle)");
            await _db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO easyatwork_sync_log
                    (company_profile_id, run_at, status, used_updates_feed,
                     inserted, updated, deleted, locked_skipped, skipped, missing_count, message)
                VALUES ({req.CompanyProfileId}, {DateTime.UtcNow}, {status}, {false},
                        {res.CountInserted}, {res.CountUpdated}, {0}, {res.SkippedContracts.Count},
                        {res.CountUnchanged}, {0}, {message})", ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "MA-Sync-Log-Eintrag konnte nicht geschrieben werden.");
        }
    }

    private async Task RevertDuplicateEawIdsAsync(SyncResult res, CancellationToken ct)
    {
        var tracked = _db.ChangeTracker.Entries<Employee>()
            .Where(e => (e.State == EntityState.Added || e.State == EntityState.Modified)
                        && e.Entity.EasyAtWorkEmployeeId.HasValue)
            .ToList();
        if (tracked.Count == 0) return;

        var trackedIds  = tracked.Where(e => e.Entity.Id != 0).Select(e => e.Entity.Id).ToHashSet();
        var eawValues   = tracked.Select(e => e.Entity.EasyAtWorkEmployeeId!.Value).Distinct().ToList();
        var dbOwners = (await _db.Employees.AsNoTracking()
                .Where(e => !e.IsHidden && !trackedIds.Contains(e.Id)
                            && e.EasyAtWorkEmployeeId.HasValue
                            && eawValues.Contains(e.EasyAtWorkEmployeeId.Value))
                .Select(e => new { e.Id, e.EasyAtWorkEmployeeId, e.FirstName, e.LastName })
                .ToListAsync(ct))
            .GroupBy(e => e.EasyAtWorkEmployeeId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        var claimed = new Dictionary<int, Employee>();   // eaw-id → erster Träger im Batch
        foreach (var entry in tracked)
        {
            var eid = entry.Entity.EasyAtWorkEmployeeId!.Value;
            var inDb    = dbOwners.ContainsKey(eid);
            var inBatch = claimed.ContainsKey(eid);
            if (!inDb && !inBatch) { claimed[eid] = entry.Entity; continue; }

            var who = $"{(entry.Entity.FirstName ?? "").Trim()} {(entry.Entity.LastName ?? "").Trim()}".Trim();
            string holder = inDb
                ? $"MA #{dbOwners[eid].Id} „{($"{dbOwners[eid].FirstName} {dbOwners[eid].LastName}").Trim()}“"
                : $"„{($"{claimed[eid].FirstName} {claimed[eid].LastName}").Trim()}“";

            if (entry.State == EntityState.Modified)
            {
                var orig = entry.OriginalValues["EasyAtWorkEmployeeId"] as int?;
                entry.Entity.EasyAtWorkEmployeeId = (orig.HasValue && orig.Value != eid) ? orig : null;
            }
            else
                entry.Entity.EasyAtWorkEmployeeId = null;

            res.Notes.Add($"easy@work-id {eid} NICHT an „{who}“ vergeben — gehört bereits {holder} (Doppelvergabe verhindert).");
        }
    }

    public static void SaveNumberChange(AppDbContext db, Employee emp, string newNumber)
    {
        var n = newNumber.Trim();
        // Rollen-Tausch (Walter 12.07.2026, Alaa/Rasakumary): hängt die neue
        // Hauptnummer bereits als ALIAS am MA (Wiedereintritt/Filialwechsel),
        // wird DIESE Alias-Zeile zur bisherigen Hauptnummer umgeschrieben —
        // sonst stünde dieselbe Nummer doppelt (als Haupt- UND Alias-Nummer).
        var lowered = n.ToLowerInvariant();
        var aliasRow = db.EmployeeNumberAliases.Local.FirstOrDefault(a =>
                a.EmployeeId == emp.Id
                && string.Equals((a.Number ?? "").Trim(), n, StringComparison.OrdinalIgnoreCase))
            ?? db.EmployeeNumberAliases.FirstOrDefault(a =>
                a.EmployeeId == emp.Id && a.Number.ToLower() == lowered);
        if (aliasRow != null)
        {
            aliasRow.Number  = emp.EmployeeNumber;
            aliasRow.ValidTo = DateOnly.FromDateTime(DateTime.Today);
        }
        else
        {
            db.EmployeeNumberAliases.Add(new EmployeeNumberAlias
            {
                Employee   = emp,
                EmployeeId = emp.Id,
                Number     = emp.EmployeeNumber,
                ValidTo    = DateOnly.FromDateTime(DateTime.Today),
                Source     = "easyatwork_sync",
            });
        }
        emp.EmployeeNumber = n;
    }

    /// <summary>easy@work-Gender → unser Wert „male"/„female" (sonst null).</summary>
    private static string? NormalizeGender(string? g)
    {
        if (string.IsNullOrWhiteSpace(g)) return null;
        return g.Trim().ToLowerInvariant() switch
        {
            "male" or "m" or "herr"   => "male",
            "female" or "f" or "frau" => "female",
            "divers" or "diverse" or "andere" or "other" or "nonbinary" or "non-binary" or "x" or "d" => "divers",
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
    /// Zivilstand, AHV, Nachtarbeit, Seniorität, Schwangerschaft.
    /// Unbekannt/Fehlschlag → null (bleibt manuell). Walter-Vorgabe 22.06.2026 /
    /// Schwangerschaft 27.07.2026.
    /// </summary>
    private async Task<PropsInfo> FetchPropsInfoAsync(int customerId, int eawEmployeeId, CancellationToken ct)
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

            var info = new PropsInfo
            {
                Marital = MapMaritalStatus(Pick("marital", "civil", "zivil", "familienstand", "family_status")),
                Ahv     = FormatAhv(Pick("swiss_national_id", "national_id", "ahv", "avs", "sozialvers")),
            };

            // Nachtarbeit-Arztzeugnis (cf_night_work_doctors_note, Walter 30.06.2026):
            // boolean (value "1" = vorhanden) + Gültigkeit von/bis (UTC → Zürich-Datum).
            // Wir geben Von UND Bis zurück; die Gültigkeit rechnen WIR aus dem Von
            // nach unserer Regel, und das easy@work-Bis dient als Prüfwert.
            // Benutzerdefinierte Felder sind in easy@work VERSIONIERT (mehrere
            // Zeilen mit Von/Bis). Massgebend ist die NEUESTE Version (jüngstes
            // Von, dann jüngste Änderung) — NICHT die mit dem spätesten Bis
            // (Walter-Bug 11.07.2026: alte Version 17.7.2028 überdeckte die
            // korrigierte aktuelle mit Bis 3.7.2028).
            // ACHTUNG (Walter-Dump 11.07.2026): id/updated_at im Property-JSON
            // gehören zur FELD-DEFINITION (id 64, updated 2020) — als Versions-
            // Kriterium unbrauchbar. Auswahl daher: HEUTE GÜLTIGE Version zuerst
            // (from ≤ heute ≤ to/offen), dann jüngstes Von.
            // Walter-Bug 12.07.2026 («abgelaufene Nachtarbeit wird nicht mehr
            // synchronisiert»): liegt das Bis der letzten JA-Version in der
            // Vergangenheit, zeigt easy@work aktuell «N/A» — je nach Datenlage
            // existiert dann eine NEUERE Version mit Wert leer/«0», welche die
            // Auswahl gewann und hasNote=false ergab → nichts wurde übernommen
            // und der veraltete Cowork-Stand blieb stehen. Massgebend sind
            // daher NUR Versionen mit Wert «Ja»: davon die heute gültige,
            // sonst die JÜNGSTE (auch wenn abgelaufen) — deren Von/Bis ist die
            // historische Wahrheit («ausgestellt … gültig bis … · abgelaufen»).
            // «to» für Gültigkeit: beide UTC-Lesarten akzeptieren (Walter 26.07.2026).
            var nwToday = DateOnly.FromDateTime(DateTime.Today);
            static bool PropJa(EawProperty p)
            {
                var v = (p.Value ?? "").Trim().ToLowerInvariant();
                return v == "1" || v == "true" || v == "yes" || v == "ja";
            }
            static bool NwToCoversToday(EawProperty p, DateOnly today)
            {
                if (string.IsNullOrWhiteSpace(p.ToRaw)) return true; // offen
                var plain = EawDateUtil.ParseSwissDate(p.ToRaw);
                var excl  = EawDateUtil.ParseSwissInclusiveEndDate(p.ToRaw);
                return (plain.HasValue && plain.Value >= today)
                    || (excl.HasValue && excl.Value >= today);
            }
            var nwProp = props
                .Where(p => (p.Key ?? "").ToLowerInvariant().Contains("night_work_doctors_note"))
                .Where(PropJa)
                .OrderByDescending(p => (p.From ?? DateOnly.MinValue) <= nwToday
                                     && NwToCoversToday(p, nwToday) ? 1 : 0)
                .ThenByDescending(p => p.From ?? DateOnly.MinValue)
                .FirstOrDefault();
            if (nwProp != null) { info.NightWorkFrom = nwProp.From; info.NightWorkToRaw = nwProp.ToRaw; }

            // «Datum der Betriebszugehörigkeit» (Walter 05.07. / 26.07.2026):
            // FIRMEN-Eintritt für Dienstjubiläen — überdauert Filialwechsel.
            // UI-Label in easy@work; API-Key typisch cf_seniority_date.
            // «Eingestellt seit» = employee.from = nur Anstellungs-/Filialbeginn.
            var seniorRaw = Pick(
                "seniority_date", "betriebszugeh", "betriebszugehörigkeit",
                "zugehörigkeit", "zugehorigkeit", "dienstalter_datum",
                "length_of_service", "company_seniority");
            if (!string.IsNullOrWhiteSpace(seniorRaw))
            {
                info.SeniorityDate = ParsePropertyDate(seniorRaw);
                if (!info.SeniorityDate.HasValue)
                    _log.LogWarning(
                        "easy@work-MA {Id}: Betriebszugehörigkeit-Wert «{Raw}» nicht als Datum lesbar",
                        eawEmployeeId, seniorRaw);
            }

            // Schwangerschaft (Walter 27.07.2026): Custom Field «Schwanger»
            // (API-Key typisch cf_pregnant / schwanger). value=Ja,
            // from = gemeldet am, to = errechneter Geburtstermin.
            // Schwangerschaftsbeginn wird NICHT gespeichert (ET − 280 Tage live).
            var (melde, et) = EasyAtWorkPregnancyMapper.PickDates(props);
            info.PregnantMeldedatum = melde;
            info.PregnantErrechneterTermin = et;

            return info;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Properties (Zivilstand/AHV/Nachtarbeit/Seniorität/Schwangerschaft) für easy@work-MA {Id} nicht abrufbar", eawEmployeeId);
            return new PropsInfo();
        }
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
            "divers" => null,
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

    /// <summary>Ortsname für den PLZ-Abgleich normalisieren: Klammer-Zusatz
    /// («Roggwil (BE)» → «roggwil»), angehängtes Kantonskürzel («Roggwil BE»)
    /// und Gross-/Kleinschreibung entfernen.</summary>
    public static string NormalizeCityName(string? s)
        => (StripCityCantonSuffix(s) ?? "").ToLowerInvariant();

    /// <summary>
    /// Kantons-Suffix vom Ortsnamen entfernen, Schreibweise sonst belassen.
    /// «Roggwil (BE)» / «Roggwil BE» → «Roggwil». Walter 29.07.2026.
    /// </summary>
    public static string? StripCityCantonSuffix(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        var i = t.IndexOf('(');
        if (i > 0) t = t[..i].Trim();
        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Angehängtes 2-Buchstaben-Kürzel (CH-Kanton), egal ob Gross/Klein.
        if (parts.Length > 1 && parts[^1].Length == 2)
            t = string.Join(' ', parts[..^1]);
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private async Task<(string? City, string? Canton, string? Error)> ResolveSwissLocationAsync(string? plz, string? eawCity, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(plz)) return (null, null, null);
        var p = plz.Trim();
        var locs = await _db.SwissLocations.AsNoTracking()
            .Where(l => l.Plz4 == p)
            .Select(l => new { l.Ortschaftsname, l.Gemeindename, l.Kantonskuerzel })
            .ToListAsync(ct);
        return ResolveCityFromLocations(p, eawCity,
            locs.Select(l => (l.Ortschaftsname, l.Gemeindename, l.Kantonskuerzel)).ToList());
    }

    /// <summary>
    /// PLZ → Ort/Kanton. Adress-Ort = easy@work-Stadt (ohne Kantons-Suffix).
    /// easy liefert z.B. «Roggwil» — AMTOVZ heisst oft «Roggwil BE»; das BE
    /// darf NICHT ins MA-Feld (Walter 29.07.2026). Match gegen Ortschaft/
    /// Gemeinde (normalisiert). Kein Treffer → Fehler. Ohne easy-Ort →
    /// erste Ortschaft, Suffix gestrippt.
    /// </summary>
    public static (string? City, string? Canton, string? Error) ResolveCityFromLocations(
        string plz,
        string? eawCity,
        IReadOnlyList<(string? Ortschaftsname, string? Gemeindename, string? Kantonskuerzel)> locs)
    {
        if (locs.Count == 0)
            return (null, null, $"PLZ {plz} wurde im Schweizer Ortschaftsverzeichnis nicht gefunden.");

        (string? Ortschaftsname, string? Gemeindename, string? Kantonskuerzel)? match = null;
        // easy hat typischerweise kein «(BE)» — Strip nur falls Alt-Daten/CSV.
        var eawClean = StripCityCantonSuffix(eawCity) ?? eawCity?.Trim();

        var exactOrt = locs.FirstOrDefault(l =>
            string.Equals(StripCityCantonSuffix(l.Ortschaftsname), eawClean, StringComparison.OrdinalIgnoreCase)
            || string.Equals(l.Ortschaftsname?.Trim(), eawCity?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(exactOrt.Ortschaftsname))
            match = exactOrt;
        else
        {
            var exactGem = locs.FirstOrDefault(l =>
                string.Equals(StripCityCantonSuffix(l.Gemeindename), eawClean, StringComparison.OrdinalIgnoreCase)
                || string.Equals(l.Gemeindename?.Trim(), eawCity?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exactGem.Ortschaftsname) || !string.IsNullOrWhiteSpace(exactGem.Gemeindename))
                match = exactGem;
            else
            {
                var eawNorm = NormalizeCityName(eawCity);
                if (eawNorm.Length > 0)
                {
                    var normOrt = locs.Where(l => NormalizeCityName(l.Ortschaftsname) == eawNorm).ToList();
                    if (normOrt.Count == 1) match = normOrt[0];
                    else
                    {
                        var normGem = locs.Where(l => NormalizeCityName(l.Gemeindename) == eawNorm).ToList();
                        if (normGem.Count == 1) match = normGem[0];
                    }
                }
            }
        }

        var cantons = locs.Select(l => l.Kantonskuerzel)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (match != null)
        {
            // easy-Ort gewinnt (ohne BE). Nie AMTOVZ «Roggwil BE» speichern.
            var city = !string.IsNullOrWhiteSpace(eawClean)
                ? eawClean
                : StripCityCantonSuffix(match.Value.Ortschaftsname ?? match.Value.Gemeindename);
            return (city, match.Value.Kantonskuerzel, null);
        }

        // easy-Ort passt nicht zur PLZ → Fehler (nicht Katalog-Namen mit BE speichern)
        if (!string.IsNullOrWhiteSpace(eawCity))
        {
            var known = string.Join(", ", locs
                .Select(l => StripCityCantonSuffix(l.Ortschaftsname)
                             ?? StripCityCantonSuffix(l.Gemeindename)
                             ?? l.Ortschaftsname ?? l.Gemeindename)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n));
            var shown = eawClean ?? eawCity.Trim();
            return (null,
                cantons.Count == 1 ? cantons[0] : null,
                $"Ort «{shown}» passt nicht zu PLZ {plz}. Bekannt: {known}");
        }

        if (cantons.Count == 1)
        {
            var fallback = locs.OrderBy(l => l.Ortschaftsname ?? l.Gemeindename).First();
            var city = StripCityCantonSuffix(fallback.Ortschaftsname ?? fallback.Gemeindename);
            return (city, fallback.Kantonskuerzel, null);
        }

        var names = locs.Select(l => StripCityCantonSuffix(l.Ortschaftsname)
                                     ?? l.Ortschaftsname ?? l.Gemeindename).OrderBy(n => n);
        return (null, null, $"PLZ {plz} ist mehrdeutig ({string.Join(" / ", names)}) und Ort konnte nicht zugeordnet werden.");
    }

    /// <summary>
    /// easy@work cf_marital_status → unser Code. Buchstaben: M/S/D/W/E/P
    /// (E = Getrennt — fehlte bis 01.08.2026 und liess Sync still leer).
    /// </summary>
    public static string? MapMaritalStatus(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var s = v.Trim().ToLowerInvariant();
        // easy@work-Einzelbuchstaben-Codes (cf_marital_status): M/S/D/W/E/P.
        switch (s)
        {
            case "m": return "verheiratet";
            case "s": return "ledig";
            case "d": return "geschieden";
            case "w": return "verwitwet";
            case "e": return "getrennt";
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

    private static string? NormalizeStreet(string? address1, string? address2 = null)
    {
        var parts = new[] { address1, address2 }
            .Select(x => string.IsNullOrWhiteSpace(x) ? null : x.Trim())
            .Where(x => x != null)
            .ToList();
        return parts.Count == 0 ? null : string.Join(" ", parts);
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
