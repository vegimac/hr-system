using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Controllers;

/// <summary>
/// Stammdaten-Anreicherung aus Mirus BVG-Pension-XLSX.
///
/// Erwartetes Format (Sheet "Tabelle1", Header in Zeile 1):
///   Numéro AVS | Nom | Prénom | Date de naissance | Sexe | Nationalité
///   | Permis de Travail | Etat Civil | Langue | Adresse | NPA | Localité
///
/// Importiert pro MA:
///   - SocialSecurityNumber (AHV)   wenn DB-Feld leer
///   - MaritalStatus                 wenn DB-Feld leer
///   - LanguageCode (de/fr/it)       wenn DB-Feld leer
///   - Street / HouseNumber          wenn DB-Feld leer ODER abweichend
///   - ZipCode (NPA)                 wenn DB-Feld leer ODER abweichend
///   - City (Localité)               wenn DB-Feld leer ODER abweichend
///   - CantonCode + Country          aus PLZ-Lookup (eindeutiger Kanton) +
///                                   „Schweiz" — bei Adressänderung neu abgeleitet
///   - Religion = „keine"            als Default wenn DB-Feld leer
///
/// Walter-Vorgabe 13.05.2026: AHV/Zivilstand/Sprache/Konfession werden nur
/// in LEERE Felder geschrieben (keine stille Überschreibung). Die ADRESSE
/// dagegen wird auch bei Abweichung überschrieben — die GastroSocial-/BVG-
/// Liste enthält i.d.R. die aktuellste Wohnadresse. Permit-Bis-Datum kommt
/// aus dem separaten Bewilligungslisten-Importer.
///
/// Match-Strategie:
///   1) Primär: AHV-Nummer (eindeutig)
///   2) Fallback: FirstName + LastName + DateOfBirth (case-insensitive)
///
/// Endpoints:
///   POST /api/imports/stammdaten/preview  → Datei parsen + match, kein Schreiben
///   POST /api/imports/stammdaten/commit   → ausgewählte Rows anwenden
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/stammdaten")]
public class EmployeeStammdatenImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<EmployeeStammdatenImportController> _log;

    public EmployeeStammdatenImportController(AppDbContext db, ILogger<EmployeeStammdatenImportController> log)
    {
        _db = db;
        _log = log;
    }

    public class PreviewRow
    {
        public int RowNum { get; set; }
        public string CsvFirstName { get; set; } = "";
        public string CsvLastName  { get; set; } = "";
        public string? CsvAhv      { get; set; }
        public DateOnly? CsvDateOfBirth { get; set; }
        public string? CsvMaritalStatusRaw { get; set; }   // Klartext aus XLSX
        public string? CsvMaritalStatusCode { get; set; }  // gemappt (ledig/verheiratet/...)
        public string? CsvLanguageRaw  { get; set; }       // "1 Deutsch" etc.
        public string? CsvLanguageCode { get; set; }       // "de"/"fr"/"it"
        public string? CsvAddressRaw   { get; set; }       // "Buchiackerweg 8"
        public string? CsvStreet       { get; set; }       // "Buchiackerweg" (gesplittet)
        public string? CsvHouseNumber  { get; set; }       // "8"
        public string? CsvZipCode      { get; set; }       // "4922"
        public string? CsvCity         { get; set; }       // "Bützberg"

        public int?    EmployeeId { get; set; }
        public string? EmployeeNumber { get; set; }
        public string? DbFirstName { get; set; }
        public string? DbLastName  { get; set; }
        public string? DbAhv       { get; set; }
        public string? DbMaritalStatus { get; set; }
        public string? DbLanguageCode  { get; set; }
        public string? DbStreet        { get; set; }
        public string? DbHouseNumber   { get; set; }
        public string? DbZipCode       { get; set; }
        public string? DbCity          { get; set; }
        public string? DbReligion      { get; set; }

        // Was würde das Commit aktuell tun?
        public bool WillSetAhv { get; set; }
        public bool WillSetMaritalStatus { get; set; }
        public bool WillSetLanguage { get; set; }
        public bool WillSetStreet { get; set; }
        public bool WillSetHouseNumber { get; set; }
        public bool WillSetZipCode { get; set; }
        public bool WillSetCity { get; set; }
        public bool WillSetReligion { get; set; }   // Default „keine" wenn leer
        public string MatchedBy { get; set; } = "";   // "AHV" | "NAME_DOB" | ""

        public string Status { get; set; } = "OK";  // OK | NO_MATCH | NO_CHANGE | INVALID_AHV
        public string? Note { get; set; }
    }

    // Kompakte MA-Liste der Filiale — damit das Frontend bei NO_MATCH /
    // AMBIGUOUS einen manuellen Auswahl-Dropdown anbieten kann (Walter-
    // Vorgabe 14.05.2026: "wenn du nicht sicher bist, den MA auswählen lassen").
    public class BranchEmployeeDto
    {
        public int Id { get; set; }
        public string? EmployeeNumber { get; set; }
        public string? FirstName { get; set; }
        public string? LastName  { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Ahv { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class PreviewResponse
    {
        public List<PreviewRow> Rows { get; set; } = new();
        public int TotalRows { get; set; }
        public int Matched { get; set; }
        public int NoMatch { get; set; }
        public int Importable { get; set; }   // Status=OK + (WillSetAhv OR WillSetMaritalStatus)
        // Alle MA der gewählten Filiale (nach Vorname sortiert) für den
        // manuellen MA-Picker im Frontend.
        public List<BranchEmployeeDto> BranchEmployees { get; set; } = new();
    }

    [HttpPost("preview")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Preview([FromForm] IFormFile file, [FromForm] int companyProfileId)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        var rows = ParseXls(file);
        if (rows == null) return BadRequest(new { error = "Konnte XLSX nicht parsen." });

        // MA der gewählten Filiale laden — der Importer arbeitet pro Filiale,
        // damit man nicht versehentlich MA aus anderen Filialen anrührt wenn
        // die BVG-Datei z.B. nur teilweise korrekt sortiert ist.
        // Zusätzlich: MA GANZ OHNE Vertrag (Personaldossiers ausgetretener MA,
        // Phantom-MA / Supervisor) — die sind keiner Filiale fest zugeordnet,
        // werden aber NUR dann mit reingenommen wenn die Personalnummer zum
        // Filial-Präfix passt (Walter-Vorgabe 14.06.2026 — vorher landeten
        // global ALLE vertragslosen MA aus jeder Filiale im Picker, was beim
        // BVG-Import von Langenthal #104 z.B. Pers-Nr 2250xxx/2300xxx/9999xxx
        // ergab). Restaurant-Code → Pers-Nr-Präfix wie beim easy@work-Import:
        // führende Nullen weg (075 → "75" → "750xxx"; 104 → "104" → "104xxxx").
        var branchPrefix = "";
        if (companyProfileId > 0)
        {
            var restCode = await _db.CompanyProfiles
                .Where(p => p.Id == companyProfileId)
                .Select(p => p.RestaurantCode)
                .FirstOrDefaultAsync();
            branchPrefix = (restCode ?? "").TrimStart('0');
        }
        var employees = await _db.Employees
            .Where(e => companyProfileId == 0
                     || e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId)
                     || (!e.Employments.Any()
                          && branchPrefix != ""
                          && e.EmployeeNumber != null
                          && e.EmployeeNumber.StartsWith(branchPrefix)))
            .Select(e => new {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName,
                e.DateOfBirth, e.SocialSecurityNumber, e.MaritalStatus,
                e.LanguageCode,
                e.Street, e.HouseNumber, e.ZipCode, e.City,
                e.Religion, e.IsActive
            })
            .ToListAsync();

        foreach (var r in rows)
        {
            // 1) Match per AHV (eindeutig)
            var ahvNorm = NormalizeAhv(r.CsvAhv);
            var match = !string.IsNullOrWhiteSpace(ahvNorm)
                ? employees.FirstOrDefault(e => NormalizeAhv(e.SocialSecurityNumber) == ahvNorm)
                : null;
            if (match != null) r.MatchedBy = "AHV";

            // 2) Fallback per Name + Geburtsdatum. Namensvergleich ist
            //    token-basiert (NameTokensMatch) — fängt zusammengesetzte
            //    Nachnamen (Mädchenname + Ehename, z.B. "Trajkov Colic" vs.
            //    "Colic"), vertauschte Vor-/Nachnamen und Mittelnamen ab.
            if (match == null && r.CsvDateOfBirth.HasValue)
            {
                match = employees.FirstOrDefault(e =>
                    NameTokensMatch(e.FirstName, e.LastName, r.CsvFirstName, r.CsvLastName)
                 && e.DateOfBirth.HasValue
                 && DateOnly.FromDateTime(e.DateOfBirth.Value) == r.CsvDateOfBirth.Value);
                if (match != null) r.MatchedBy = "NAME_DOB";
            }

            // 3) Fallback per Name allein — Mirus liefert oft 01.01.YYYY als
            //    Platzhalter-Geburtsdatum bei MAs aus Ländern ohne verlässliches
            //    Geburtsregister (ER/AF/SY etc.). Walter pflegt das echte Datum
            //    aus dem Ausländerausweis im System; Mirus weiss davon nichts.
            //    → bei genau EINEM Namens-Match in der Filiale akzeptieren wir
            //    den Match mit Warnung. Bei Mehrdeutigkeit AMBIGUOUS.
            if (match == null)
            {
                var nameMatches = employees.Where(e =>
                    NameTokensMatch(e.FirstName, e.LastName, r.CsvFirstName, r.CsvLastName)
                ).ToList();

                if (nameMatches.Count == 1)
                {
                    match = nameMatches[0];
                    r.MatchedBy = "NAME_ONLY";
                    var dbDob  = match.DateOfBirth.HasValue
                                    ? DateOnly.FromDateTime(match.DateOfBirth.Value).ToString("dd.MM.yyyy")
                                    : "—";
                    var csvDob = r.CsvDateOfBirth.HasValue
                                    ? r.CsvDateOfBirth.Value.ToString("dd.MM.yyyy")
                                    : "—";
                    r.Note = $"Geburtsdatum weicht ab: System {dbDob} vs. Mirus {csvDob}. "
                           + "Bitte prüfen — Mirus nutzt oft 01.01.YYYY als Platzhalter.";
                }
                else if (nameMatches.Count > 1)
                {
                    r.Status = "AMBIGUOUS";
                    r.Note   = $"{nameMatches.Count} MA mit diesem Namen in der Filiale — bitte MA manuell auswählen.";
                    continue;
                }
            }

            if (match == null)
            {
                r.Status = "NO_MATCH";
                r.Note   = "Kein MA mit passender AHV / Name+Geburtsdatum gefunden — bitte MA manuell auswählen.";
                continue;
            }

            r.EmployeeId        = match.Id;
            r.EmployeeNumber    = match.EmployeeNumber;
            r.DbFirstName       = match.FirstName;
            r.DbLastName        = match.LastName;
            r.DbAhv             = match.SocialSecurityNumber;
            r.DbMaritalStatus   = match.MaritalStatus;
            r.DbLanguageCode    = match.LanguageCode;
            r.DbStreet          = match.Street;
            r.DbHouseNumber     = match.HouseNumber;
            r.DbZipCode         = match.ZipCode;
            r.DbCity            = match.City;
            r.DbReligion        = match.Religion;

            // No-overwrite-Policy: nur setzen wenn DB-Feld leer ist
            r.WillSetAhv = string.IsNullOrWhiteSpace(match.SocialSecurityNumber)
                        && !string.IsNullOrWhiteSpace(ahvNorm);
            r.WillSetMaritalStatus = string.IsNullOrWhiteSpace(match.MaritalStatus)
                        && !string.IsNullOrWhiteSpace(r.CsvMaritalStatusCode);
            r.WillSetLanguage = string.IsNullOrWhiteSpace(match.LanguageCode)
                        && !string.IsNullOrWhiteSpace(r.CsvLanguageCode);
            // Adresse: WillSet wenn DB-Feld leer ODER abweichend (Walter-Vorgabe
            // 13.05.2026 — die GastroSocial-Liste darf veraltete Adressen
            // überschreiben). Konsistent mit der Commit-Logik (AddrDiffers).
            r.WillSetStreet      = AddrDiffers(match.Street,      r.CsvStreet);
            r.WillSetHouseNumber = AddrDiffers(match.HouseNumber, r.CsvHouseNumber);
            r.WillSetZipCode     = AddrDiffers(match.ZipCode,     r.CsvZipCode);
            r.WillSetCity        = AddrDiffers(match.City,        r.CsvCity);
            // Konfession: BVG-File liefert keine — wir setzen Default „keine"
            // wenn DB-Feld leer ist (Walter-Vorgabe).
            r.WillSetReligion = string.IsNullOrWhiteSpace(match.Religion);

            if (!r.WillSetAhv && !r.WillSetMaritalStatus && !r.WillSetLanguage
                && !r.WillSetStreet && !r.WillSetHouseNumber
                && !r.WillSetZipCode && !r.WillSetCity
                && !r.WillSetReligion)
            {
                r.Status = "NO_CHANGE";
                r.Note   = "MA hat AHV + Zivilstand + Sprache + Adresse + Konfession bereits erfasst — nichts zu tun.";
            }
        }

        return Ok(new PreviewResponse {
            Rows       = rows,
            TotalRows  = rows.Count,
            Matched    = rows.Count(r => r.EmployeeId != null),
            NoMatch    = rows.Count(r => r.Status == "NO_MATCH" || r.Status == "AMBIGUOUS"),
            Importable = rows.Count(r => r.Status == "OK" && (
                r.WillSetAhv || r.WillSetMaritalStatus || r.WillSetLanguage
             || r.WillSetStreet || r.WillSetHouseNumber || r.WillSetZipCode || r.WillSetCity
             || r.WillSetReligion)),
            // MA-Liste der Filiale für den manuellen Picker — nach Vorname
            // sortiert (CLAUDE.md-Konvention für alle MA-Auswahllisten).
            BranchEmployees = employees
                .OrderBy(e => e.FirstName ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.LastName ?? "", StringComparer.OrdinalIgnoreCase)
                .Select(e => new BranchEmployeeDto {
                    Id             = e.Id,
                    EmployeeNumber = e.EmployeeNumber,
                    FirstName      = e.FirstName,
                    LastName       = e.LastName,
                    DateOfBirth    = e.DateOfBirth.HasValue
                                        ? DateOnly.FromDateTime(e.DateOfBirth.Value)
                                        : null,
                    Ahv            = e.SocialSecurityNumber,
                    IsActive       = e.IsActive
                })
                .ToList()
        });
    }

    public record CommitRequest(int CompanyProfileId, List<int> RowNums);

    [HttpPost("commit")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Commit([FromForm] IFormFile file,
                                            [FromForm] int companyProfileId,
                                            [FromForm] string? rowNums,
                                            [FromForm] string? manualMatches)
    {
        // rowNums ist eine Komma-Liste (z.B. "1,3,7"). Wenn leer → alle
        // importierbaren Rows.
        var selectedRows = string.IsNullOrWhiteSpace(rowNums)
            ? new HashSet<int>()
            : rowNums.Split(',', StringSplitOptions.RemoveEmptyEntries
                                | StringSplitOptions.TrimEntries)
                     .Select(int.Parse)
                     .ToHashSet();

        // manualMatches: vom User im Frontend manuell zugeordnete Zeilen,
        // Format "rowNum:employeeId,rowNum:employeeId" (Walter-Vorgabe
        // 14.05.2026 — bei NO_MATCH / AMBIGUOUS den MA von Hand wählen).
        var manualMap = new Dictionary<int, int>();
        if (!string.IsNullOrWhiteSpace(manualMatches))
        {
            foreach (var pair in manualMatches.Split(',', StringSplitOptions.RemoveEmptyEntries
                                                        | StringSplitOptions.TrimEntries))
            {
                var kv = pair.Split(':', StringSplitOptions.RemoveEmptyEntries
                                       | StringSplitOptions.TrimEntries);
                if (kv.Length == 2 && int.TryParse(kv[0], out var rn) && int.TryParse(kv[1], out var eid))
                    manualMap[rn] = eid;
            }
        }

        var rows = ParseXls(file);
        if (rows == null) return BadRequest(new { error = "Konnte XLSX nicht parsen." });

        // Match-Logik nochmal — Preview ist nicht persistent, daher
        // zuverlässiger nochmal laufen. Pool identisch zur Preview: Filial-MA
        // + MA ganz ohne Vertrag, deren Pers-Nr zum Filial-Präfix passt
        // (Walter-Vorgabe 14.06.2026 — sonst landeten alle vertragslosen MA
        // anderer Filialen mit im Pool).
        var branchPrefixCommit = "";
        if (companyProfileId > 0)
        {
            var restCode = await _db.CompanyProfiles
                .Where(p => p.Id == companyProfileId)
                .Select(p => p.RestaurantCode)
                .FirstOrDefaultAsync();
            branchPrefixCommit = (restCode ?? "").TrimStart('0');
        }
        var employees = await _db.Employees
            .Where(e => companyProfileId == 0
                     || e.Employments.Any(emp => emp.CompanyProfileId == companyProfileId)
                     || (!e.Employments.Any()
                          && branchPrefixCommit != ""
                          && e.EmployeeNumber != null
                          && e.EmployeeNumber.StartsWith(branchPrefixCommit)))
            .ToListAsync();

        // PLZ → Kanton-Lookup vorbereiten (Walter-Vorgabe 13.05.2026: beim
        // Adress-Import auch den Wohnkanton aus der Ortschaft ableiten).
        // Eine PLZ kann mehrere Gemeinden haben — wir nehmen den Kanton nur,
        // wenn ALLE Gemeinden derselben PLZ im selben Kanton liegen (eindeutig).
        var allZips = rows.Select(r => r.CsvZipCode?.Trim())
                          .Where(z => !string.IsNullOrWhiteSpace(z))
                          .Select(z => z!)
                          .Distinct()
                          .ToList();
        var swissLocs = await _db.SwissLocations
            .Where(l => allZips.Contains(l.Plz4))
            .Select(l => new { l.Plz4, l.Kantonskuerzel })
            .ToListAsync();
        var plzKantonLookup = swissLocs
            .GroupBy(l => l.Plz4)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var kt = g.Select(x => x.Kantonskuerzel).Distinct().ToList();
                    return kt.Count == 1 ? kt[0] : null;   // nur eindeutige PLZ
                });

        int updatedAhv = 0, updatedMs = 0, updatedLang = 0, updatedAddr = 0, updatedRel = 0, skipped = 0;
        foreach (var r in rows)
        {
            if (selectedRows.Count > 0 && !selectedRows.Contains(r.RowNum)) continue;

            var ahvNorm = NormalizeAhv(r.CsvAhv);

            // Stufe 0: manuelle Zuordnung aus dem Frontend gewinnt IMMER —
            // der User hat den MA explizit gewählt.
            Employee? emp = null;
            if (manualMap.TryGetValue(r.RowNum, out var manualEmpId))
                emp = employees.FirstOrDefault(e => e.Id == manualEmpId);

            // Stufe 1: AHV-Nummer (eindeutig)
            if (emp == null && !string.IsNullOrWhiteSpace(ahvNorm))
                emp = employees.FirstOrDefault(e => NormalizeAhv(e.SocialSecurityNumber) == ahvNorm);

            // Stufe 2: Name (token-basiert) + Geburtsdatum
            if (emp == null && r.CsvDateOfBirth.HasValue)
            {
                emp = employees.FirstOrDefault(e =>
                    NameTokensMatch(e.FirstName, e.LastName, r.CsvFirstName, r.CsvLastName)
                 && e.DateOfBirth.HasValue
                 && DateOnly.FromDateTime(e.DateOfBirth.Value) == r.CsvDateOfBirth.Value);
            }
            // Stufe 3: Name-only Fallback (Mirus-DOB-Platzhalter) — nur wenn
            // EINDEUTIG. Bei Mehrdeutigkeit überspringen.
            if (emp == null)
            {
                var nameMatches = employees.Where(e =>
                    NameTokensMatch(e.FirstName, e.LastName, r.CsvFirstName, r.CsvLastName)
                ).ToList();
                if (nameMatches.Count == 1) emp = nameMatches[0];
            }
            if (emp == null) { skipped++; continue; }

            // No-overwrite: nur leere Felder befüllen.
            if (string.IsNullOrWhiteSpace(emp.SocialSecurityNumber) && !string.IsNullOrWhiteSpace(ahvNorm))
            {
                emp.SocialSecurityNumber = ahvNorm;
                updatedAhv++;
            }
            if (string.IsNullOrWhiteSpace(emp.MaritalStatus) && !string.IsNullOrWhiteSpace(r.CsvMaritalStatusCode))
            {
                emp.MaritalStatus = r.CsvMaritalStatusCode;
                updatedMs++;
            }
            if (string.IsNullOrWhiteSpace(emp.LanguageCode) && !string.IsNullOrWhiteSpace(r.CsvLanguageCode))
            {
                emp.LanguageCode = r.CsvLanguageCode;
                updatedLang++;
            }
            // Adresse: importieren wenn DB-Feld LEER ODER der CSV-Wert sich
            // unterscheidet (Walter-Vorgabe 13.05.2026: die BVG-/Stammdaten-Liste
            // von GastroSocial enthält i.d.R. die aktuellste Wohnadresse — eine
            // veraltete DB-Adresse darf damit überschrieben werden).
            // updatedAddr zählt MAs bei denen mind. 1 Adress-Feld gesetzt wurde.
            bool addrTouched = false;
            if (AddrDiffers(emp.Street, r.CsvStreet))
            { emp.Street = r.CsvStreet!.Trim(); addrTouched = true; }
            if (AddrDiffers(emp.HouseNumber, r.CsvHouseNumber))
            { emp.HouseNumber = r.CsvHouseNumber!.Trim(); addrTouched = true; }
            if (AddrDiffers(emp.ZipCode, r.CsvZipCode))
            { emp.ZipCode = r.CsvZipCode!.Trim(); addrTouched = true; }
            if (AddrDiffers(emp.City, r.CsvCity))
            { emp.City = r.CsvCity!.Trim(); addrTouched = true; }

            // Kanton aus PLZ ableiten + Land = Schweiz (Walter-Vorgabe 13.05.2026).
            // Bei Adressänderung (addrTouched) IMMER neu ableiten — neuer Wohnort
            // kann einen anderen Kanton bedeuten. Sonst nur befüllen wenn leer.
            var zipForLookup = !string.IsNullOrWhiteSpace(emp.ZipCode)
                ? emp.ZipCode.Trim()
                : r.CsvZipCode?.Trim();
            if (!string.IsNullOrWhiteSpace(zipForLookup))
            {
                // Land: „CH" (ISO-Code, systemweiter Standard — PLZ stammt aus
                // dem CH-Ortschaftsverzeichnis).
                if (addrTouched || string.IsNullOrWhiteSpace(emp.Country))
                {
                    if (emp.Country != "CH") { emp.Country = "CH"; addrTouched = true; }
                }
                // Wohnkanton aus eindeutigem PLZ-Lookup
                if ((addrTouched || string.IsNullOrWhiteSpace(emp.CantonCode))
                    && plzKantonLookup.TryGetValue(zipForLookup, out var kanton)
                    && !string.IsNullOrWhiteSpace(kanton)
                    && emp.CantonCode != kanton)
                {
                    emp.CantonCode = kanton;
                    addrTouched = true;
                }
            }
            if (addrTouched) updatedAddr++;

            // Konfession: Default „keine" wenn DB-Feld leer.
            if (string.IsNullOrWhiteSpace(emp.Religion))
            {
                emp.Religion = "keine";
                updatedRel++;
            }
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("[StammdatenImport] Filiale={CP} AHV={Ahv}, Zivilstand={Ms}, Sprache={Lang}, Adresse={Addr}, Konfession={Rel}, übersprungen={Skip}",
            companyProfileId, updatedAhv, updatedMs, updatedLang, updatedAddr, updatedRel, skipped);

        return Ok(new {
            updatedAhv, updatedMaritalStatus = updatedMs, updatedLanguage = updatedLang,
            updatedAddress = updatedAddr, updatedReligion = updatedRel, skipped
        });
    }

    // ── XLSX-Parser ─────────────────────────────────────────────────────────

    private List<PreviewRow>? ParseXls(IFormFile file)
    {
        try
        {
            using var stream = file.OpenReadStream();
            using var wb     = new XSSFWorkbook(stream);
            var sheet = wb.GetSheetAt(0);
            if (sheet == null) return null;

            // Header-Zeile finden + Spalten-Indizes mappen.
            var header = sheet.GetRow(0);
            if (header == null) return null;

            int colAhv = -1, colNom = -1, colPrenom = -1, colDob = -1, colMs = -1, colLang = -1;
            int colAddr = -1, colZip = -1, colCity = -1;
            for (int c = 0; c < header.LastCellNum; c++)
            {
                var v = (header.GetCell(c)?.ToString() ?? "").Trim().ToLowerInvariant();
                if (v.Contains("avs") || v.Contains("ahv"))         colAhv = c;
                else if (v == "nom")                                colNom = c;
                else if (v == "prénom" || v == "prenom")            colPrenom = c;
                else if (v.Contains("naissance") || v.Contains("geburt")) colDob = c;
                else if (v.Contains("etat civil") || v.Contains("zivil")) colMs = c;
                else if (v == "langue" || v.Contains("sprache"))    colLang = c;
                else if (v == "adresse" || v == "rue" || v == "strasse") colAddr = c;
                else if (v == "npa" || v == "plz" || v == "cp")     colZip = c;
                else if (v == "localité" || v == "localite"
                       || v == "ort"   || v == "lieu")              colCity = c;
            }

            if (colAhv < 0 || colNom < 0 || colPrenom < 0)
            {
                _log.LogWarning("[StammdatenImport] Header-Spalten nicht gefunden (AHV={A}, Nom={N}, Prenom={P})",
                    colAhv, colNom, colPrenom);
                return null;
            }

            var rows = new List<PreviewRow>();
            for (int r = 1; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                var ahvRaw = (row.GetCell(colAhv)?.ToString() ?? "").Trim();
                var nom    = (row.GetCell(colNom)?.ToString() ?? "").Trim();
                var prenom = (row.GetCell(colPrenom)?.ToString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(nom) && string.IsNullOrWhiteSpace(prenom))
                    continue;

                DateOnly? dob = null;
                if (colDob >= 0)
                {
                    var cell = row.GetCell(colDob);
                    if (cell != null)
                    {
                        if (cell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(cell))
                            dob = DateOnly.FromDateTime(cell.DateCellValue ?? DateTime.MinValue);
                        else if (DateOnly.TryParse(cell.ToString(), out var parsed))
                            dob = parsed;
                    }
                }

                string? msRaw = colMs >= 0
                    ? (row.GetCell(colMs)?.ToString() ?? "").Trim()
                    : null;
                string? langRaw = colLang >= 0
                    ? (row.GetCell(colLang)?.ToString() ?? "").Trim()
                    : null;
                string? addrRaw = colAddr >= 0
                    ? (row.GetCell(colAddr)?.ToString() ?? "").Trim()
                    : null;
                string? zipRaw = colZip >= 0
                    ? (row.GetCell(colZip)?.ToString() ?? "").Trim()
                    : null;
                string? cityRaw = colCity >= 0
                    ? (row.GetCell(colCity)?.ToString() ?? "").Trim()
                    : null;

                var (street, houseNum) = SplitStreetAndHouseNumber(addrRaw);

                rows.Add(new PreviewRow {
                    RowNum               = r,
                    CsvFirstName         = prenom,
                    CsvLastName          = nom,
                    CsvAhv               = ahvRaw,
                    CsvDateOfBirth       = dob,
                    CsvMaritalStatusRaw  = msRaw,
                    CsvMaritalStatusCode = MapMaritalStatus(msRaw),
                    CsvLanguageRaw       = langRaw,
                    CsvLanguageCode      = MapLanguage(langRaw),
                    CsvAddressRaw        = addrRaw,
                    CsvStreet            = street,
                    CsvHouseNumber       = houseNum,
                    CsvZipCode           = string.IsNullOrWhiteSpace(zipRaw) ? null : zipRaw,
                    CsvCity              = string.IsNullOrWhiteSpace(cityRaw) ? null : cityRaw
                });
            }
            return rows;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[StammdatenImport] XLSX-Parse fehlgeschlagen");
            return null;
        }
    }

    /// <summary>
    /// True wenn der CSV-Wert vorhanden ist UND sich vom DB-Wert unterscheidet
    /// (oder das DB-Feld leer ist). Trimmt + ignoriert Gross-/Kleinschreibung.
    /// Wird beim Adress-Import verwendet: leere ODER abweichende DB-Felder
    /// werden mit dem CSV-Wert überschrieben.
    /// </summary>
    private static bool AddrDiffers(string? dbValue, string? csvValue)
    {
        if (string.IsNullOrWhiteSpace(csvValue)) return false;
        return !string.Equals((dbValue ?? "").Trim(), csvValue.Trim(),
                              StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Zerlegt Namens-Teile in eine Token-Menge (lowercase, getrennt an
    /// Leerzeichen / Bindestrich / Punkt). „Trajkov Colic" → {trajkov, colic}.
    /// </summary>
    private static HashSet<string> NameTokens(params string?[] parts)
    {
        var tokens = new HashSet<string>();
        foreach (var p in parts)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            foreach (var tok in p.Split(new[] { ' ', '-', '–', '.', '\t', '/' },
                                        StringSplitOptions.RemoveEmptyEntries))
            {
                var t = tok.Trim().ToLowerInvariant();
                if (t.Length > 0) tokens.Add(t);
            }
        }
        return tokens;
    }

    /// <summary>
    /// Token-basierter Namensvergleich. True wenn die kleinere Token-Menge
    /// (Vor- + Nachname zusammengeworfen) vollständig in der grösseren
    /// enthalten ist und mindestens 2 Tokens teilt. Fängt zusammengesetzte
    /// Nachnamen (Mädchenname + Ehename, z.B. „Trajkov Colic" vs. „Colic"),
    /// vertauschte Vor-/Nachnamen und zusätzliche Mittelnamen ab — ohne bei
    /// blossem Vornamens-Treffer zu matchen.
    /// </summary>
    private static bool NameTokensMatch(string? dbFirst, string? dbLast,
                                        string? csvFirst, string? csvLast)
    {
        var db  = NameTokens(dbFirst, dbLast);
        var csv = NameTokens(csvFirst, csvLast);
        if (db.Count == 0 || csv.Count == 0) return false;
        var smaller = db.Count <= csv.Count ? db : csv;
        var larger  = db.Count <= csv.Count ? csv : db;
        if (smaller.Count < 2) return false;          // mind. Vor- + Nachname
        return smaller.IsSubsetOf(larger);
    }

    // AHV: Format 756.XXXX.XXXX.XX. Whitespace + Trennzeichen normalisieren.
    // Returns null wenn nicht plausibel (zu kurz, falsches Präfix etc.).
    private static string? NormalizeAhv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = new string(raw.Where(c => char.IsDigit(c) || c == '.').ToArray());
        // Reine Ziffernform → in 756.XXXX.XXXX.XX umformatieren wenn 13 stellig
        var digits = new string(cleaned.Where(char.IsDigit).ToArray());
        if (digits.Length == 13 && digits.StartsWith("756"))
            return $"{digits[..3]}.{digits[3..7]}.{digits[7..11]}.{digits[11..]}";
        // Standard-Format mit Punkten
        if (cleaned.Length == 16 && cleaned.StartsWith("756."))
            return cleaned;
        return null;
    }

    // Mirus-Klartext → DB-Code (siehe Employee.MaritalStatus-XML-Doku).
    private static string? MapMaritalStatus(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var v = raw.Trim().ToLowerInvariant();
        return v switch
        {
            "ledig"                                          => "ledig",
            "verheiratet"                                    => "verheiratet",
            "geschieden"                                     => "geschieden",
            "verwitwet"                                      => "verwitwet",
            "getrennt"                                       => "getrennt",
            "eingetragene partnerschaft"                     => "eingetragene_partnerschaft",
            "aufgelöste partnerschaft" or "aufgeloeste partnerschaft"
                                                             => "aufgeloeste_partnerschaft",
            // Französische Varianten falls Datei aus Romandie kommt
            "célibataire" or "celibataire"                   => "ledig",
            "marié(e)" or "marié" or "mariee"                => "verheiratet",
            "divorcé(e)" or "divorce" or "divorcee"          => "geschieden",
            "veuf(ve)" or "veuf" or "veuve"                  => "verwitwet",
            "séparé(e)" or "separe" or "separee"             => "getrennt",
            _ => null
        };
    }

    // Splittet eine Adresse "Strasse 12a" in Strasse + Hausnummer.
    // Hausnummer-Erkennung: am Ende ein Token aus Ziffern + optional 1 Buchstabe
    // oder Bindestrich-Bereich (z.B. "8", "12a", "3-5"). Wenn keine Nummer
    // erkennbar, wird die ganze Eingabe als Strasse zurückgegeben.
    private static (string? Street, string? HouseNumber) SplitStreetAndHouseNumber(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        var trimmed = raw.Trim();
        // Match: "Anything <space> <housenum>" am Ende
        var m = System.Text.RegularExpressions.Regex.Match(
            trimmed, @"^(.+?)\s+(\d+\s*[a-zA-Z]?(?:[-–]\d+\s*[a-zA-Z]?)?)\s*$");
        if (m.Success)
        {
            return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim());
        }
        return (trimmed, null);
    }

    // Mirus-Sprache → ISO-Code. Format meist "1 Deutsch", "2 Französisch",
    // "3 Italienisch", "4 Englisch" — die führende Ziffer ist optional.
    private static string? MapLanguage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var v = raw.Trim().ToLowerInvariant();
        // führende Ziffer + Whitespace entfernen falls vorhanden
        var stripped = System.Text.RegularExpressions.Regex.Replace(v, @"^\d+\s*", "");
        return stripped switch
        {
            "deutsch" or "allemand" or "tedesco" or "german"   => "de",
            "französisch" or "franzoesisch" or "français"
                or "francais" or "francese" or "french"        => "fr",
            "italienisch" or "italiano" or "italien"
                or "italian"                                   => "it",
            "englisch" or "anglais" or "inglese" or "english"  => "en",
            _ => null
        };
    }

    private int GetCurrentUserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var v) ? v : 0;
}
