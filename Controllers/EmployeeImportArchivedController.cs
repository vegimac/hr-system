using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace HrSystem.Controllers;

/// <summary>
/// Importiert ausgetretene Mitarbeiter aus einer CSV-Datei (Mirus-/Bouncer-Export).
/// Nur Mitarbeiter mit "Bis"-Datum werden importiert. Bestehende werden via
/// Vorname+Nachname+Geburtsdatum erkannt und übersprungen.
/// Importierte bekommen Suffix "alt" an die employee_number, um Kollisionen mit
/// zukünftigen Auto-Nummern aus dem Upstream-System auszuschliessen.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/employees/import-archived")]
public class EmployeeImportArchivedController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeImportArchivedController(AppDbContext db) => _db = db;

    public class ImportResult
    {
        public int TotalRows { get; set; }
        public int WithExitDate { get; set; }
        public int WithoutExitDate { get; set; }
        public int Imported { get; set; }
        public int Updated { get; set; }
        public int SkippedAlreadyExists { get; set; }
        public int SkippedNoBranch { get; set; }
        public int SkippedInvalid { get; set; }
        public List<PreviewRow> Preview { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public bool DryRun { get; set; }
    }

    public class PreviewRow
    {
        public int RowNum { get; set; }
        public string EmployeeNumber { get; set; } = "";
        public string OriginalEmployeeNumber { get; set; } = "";
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public DateOnly? DateOfBirth { get; set; }
        public DateOnly? EntryDate { get; set; }
        public DateOnly? ExitDate { get; set; }
        public string? BranchCode { get; set; }
        public string? BranchName { get; set; }
        public string Action { get; set; } = "";   // create | update | skip-exists | skip-no-branch | skip-no-bis | skip-invalid
        public string? Reason { get; set; }
        public int? ExistingEmployeeId { get; set; }

        // Vertragsdaten-Mapping (sichtbar im Preview)
        public string? CsvFunktion { get; set; }
        public string? CsvContractType { get; set; }
        public string? JobGroupCode { get; set; }
        public bool? IsKader { get; set; }
        public string? EmploymentModel { get; set; }
        public decimal? EmploymentPercentage { get; set; }
        public decimal? GuaranteedHoursPerWeek { get; set; }
        public bool ResolvedActive { get; set; }
    }

    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)] // 20 MB CSV
    public async Task<IActionResult> Import(
        [FromForm] IFormFile file,
        [FromForm] bool dryRun = true,
        [FromForm] bool updateExisting = false,
        [FromForm] bool fullMigration = false)
    {
        if (file is null || file.Length == 0) return BadRequest("Keine CSV hochgeladen.");

        var result = new ImportResult { DryRun = dryRun };

        // CSV einlesen — UTF-8 mit BOM-Tolerance
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        var lines = new List<string>();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null) lines.Add(line);
        if (lines.Count == 0) return BadRequest("CSV ist leer.");

        // Header parsen
        var headers = ParseCsvLine(lines[0]);
        var idx = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headers.Count; i++) idx[headers[i].Trim('﻿', '"', ' ')] = i;

        int Get(string name) => idx.TryGetValue(name, out var i) ? i : -1;

        int colNummer    = Get("Nummer");
        int colVorname   = Get("Vorname");
        int colNachname  = Get("Nachname");
        int colGebDatum  = Get("Geburtsdatum");
        int colVon       = Get("Von");
        int colBis       = Get("Bis");
        int colStoreNum  = Get("Store number");
        int colAdresse   = Get("Adresse");
        int colPLZ       = Get("Postleitzahl");
        int colStadt     = Get("Stadt");
        int colTel       = Get("Telefon");
        int colEmail     = Get("E-Mail");
        int colGender    = Get("Geschlecht");
        int colAhv       = Get("AHV Nummer");
        int colNat       = Get("Nationalität");

        // Vertrags-Felder (für Voll-Migration)
        int colFunktion       = Get("Funktion");
        int colAnzahl         = Get("Anzahl");
        int colContractType   = Get("Contract type");
        int colSalaryFte      = Get("Salary (FTE)");
        int colSalaryActual   = Get("Salary (actual)");
        int colTarifsatz      = Get("Tarifsatz");
        int colBetrZugehDatum = Get("Datum der Betriebszugehörigkeit");

        if (colNummer < 0 || colVorname < 0 || colNachname < 0 || colGebDatum < 0 || colBis < 0 || colStoreNum < 0)
            return BadRequest("CSV hat nicht die erwarteten Spalten (Nummer, Vorname, Nachname, Geburtsdatum, Bis, Store number).");

        // Filialen vorladen für schnellen Lookup
        var branches = await _db.CompanyProfiles
            .Select(c => new { c.Id, c.RestaurantCode, c.BranchName, c.CompanyName })
            .ToListAsync();

        // Bestehende Employees vorladen für Identitäts-Check
        var existing = await _db.Employees
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth })
            .ToListAsync();

        // Bestehende Employments pro Mitarbeiter (für Update-Modus)
        var existingEmployments = await _db.Employments
            .Select(em => new { em.EmployeeId, em.CompanyProfileId })
            .ToListAsync();

        // JobGroups laden (für Funktion → Kader-Lookup)
        var jobGroups = await _db.JobGroups.Where(j => j.IsActive).ToListAsync();

        result.TotalRows = lines.Count - 1;

        for (int rowIdx = 1; rowIdx < lines.Count; rowIdx++)
        {
            var fields = ParseCsvLine(lines[rowIdx]);
            string Get0(int c) => c >= 0 && c < fields.Count ? fields[c].Trim() : "";

            var p = new PreviewRow {
                RowNum = rowIdx,
                FirstName = Get0(colVorname).Trim('"'),
                LastName  = Get0(colNachname).Trim('"'),
                OriginalEmployeeNumber = Get0(colNummer)
            };
            p.EmployeeNumber = string.IsNullOrEmpty(p.OriginalEmployeeNumber) ? "" : p.OriginalEmployeeNumber + "alt";

            // Datum parsen (Format: 1995-01-01)
            p.DateOfBirth = ParseDateOnly(Get0(colGebDatum));
            p.EntryDate   = ParseDateOnly(Get0(colVon));
            p.ExitDate    = ParseDateOnly(Get0(colBis));

            // Filiale ermitteln (Store number 58 → restaurant_code 058)
            var storeRaw = Get0(colStoreNum);
            if (!string.IsNullOrEmpty(storeRaw))
            {
                var storeNum = int.TryParse(storeRaw, out var sn) ? sn.ToString("D3") : storeRaw;
                p.BranchCode = storeNum;
                var br = branches.FirstOrDefault(b => b.RestaurantCode == storeNum);
                if (br != null) p.BranchName = br.BranchName ?? br.CompanyName;
            }

            // Aktiv-Status: Bis leer ODER Bis >= heute → aktiv
            var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
            bool isActive = !p.ExitDate.HasValue || p.ExitDate.Value >= today;
            if (p.ExitDate.HasValue) result.WithExitDate++; else result.WithoutExitDate++;

            // Standard-Modus (nicht Voll-Migration): nur ausgetretene MA importieren
            if (!fullMigration && !p.ExitDate.HasValue)
            {
                p.Action = "skip-no-bis";
                p.Reason = "Kein Bis-Datum (= aktiv, nicht zu importieren ohne Voll-Migration)";
                result.Preview.Add(p);
                continue;
            }
            // Im Standard-Modus: Bis in Zukunft ⇒ trotzdem als ausgetreten behandeln (alt!)
            // Im Voll-Migration: aktive MA bleiben aktiv

            // Validierung
            if (string.IsNullOrEmpty(p.FirstName) || string.IsNullOrEmpty(p.LastName) || p.DateOfBirth is null)
            {
                p.Action = "skip-invalid";
                p.Reason = "Vorname/Nachname/Geburtsdatum unvollständig";
                result.SkippedInvalid++;
                result.Preview.Add(p);
                continue;
            }

            // Bestehender MA via Name+DOB
            var dup = existing.FirstOrDefault(e =>
                e.FirstName.Equals(p.FirstName, StringComparison.OrdinalIgnoreCase) &&
                e.LastName.Equals(p.LastName, StringComparison.OrdinalIgnoreCase) &&
                e.DateOfBirth.HasValue &&
                DateOnly.FromDateTime(e.DateOfBirth.Value) == p.DateOfBirth);

            // Filiale check (vor Duplikat-Behandlung, weil Update-Modus die Filiale braucht)
            if (string.IsNullOrEmpty(p.BranchCode) ||
                !branches.Any(b => b.RestaurantCode == p.BranchCode))
            {
                p.Action = "skip-no-branch";
                p.Reason = $"Filiale {p.BranchCode ?? "?"} existiert nicht im System";
                result.SkippedNoBranch++;
                result.Preview.Add(p);
                continue;
            }

            if (dup != null)
            {
                p.ExistingEmployeeId = dup.Id;

                // Standard-Modus: skippen
                if (!updateExisting)
                {
                    p.Action = "skip-exists";
                    p.Reason = $"MA existiert (ID {dup.Id}, Nr. {dup.EmployeeNumber}) — updateExisting=false";
                    result.SkippedAlreadyExists++;
                    result.Preview.Add(p);
                    continue;
                }

                // Update-Modus: existierenden MA mit Archiv-Daten anreichern + Employment ergänzen
                p.Action = "update";
                p.EmployeeNumber = dup.EmployeeNumber; // bestehende Nummer behalten
                var brExisting = branches.First(b => b.RestaurantCode == p.BranchCode);
                p.Reason = existingEmployments.Any(em => em.EmployeeId == dup.Id && em.CompanyProfileId == brExisting.Id)
                    ? $"Update Bis-Datum/inaktiv (Employment für {p.BranchCode} schon da)"
                    : $"Update Bis-Datum/inaktiv + neues Employment für {p.BranchCode}";

                // Vertragsdaten aus CSV (für Voll-Migration auch im Update-Pfad relevant)
                var ctRawU = colContractType >= 0 ? Get0(colContractType).Trim('"') : "";
                var fnRawU = colFunktion     >= 0 ? Get0(colFunktion).Trim('"')     : "";
                var anzahlU = colAnzahl      >= 0 ? Get0(colAnzahl)                 : "";
                var jgU = FindJobGroupByFunktion(fnRawU, jobGroups);
                var employmentModelU = MapEmploymentModel(ctRawU, anzahlU, jgU?.IsKader == true);
                var salaryTypeU      = MapSalaryType(employmentModelU);
                var (pctParsedU, weeklyParsedU) = ParseAnzahl(anzahlU);
                var salaryActualU = colSalaryActual >= 0 ? ParseDecimal(Get0(colSalaryActual)) : null;
                var salaryFteU    = colSalaryFte    >= 0 ? ParseDecimal(Get0(colSalaryFte))    : null;
                var hourlyRateU   = colTarifsatz    >= 0 ? ParseDecimal(Get0(colTarifsatz))    : null;
                var betrZugehU    = colBetrZugehDatum >= 0 ? ParseDateOnly(Get0(colBetrZugehDatum)) : null;

                // Mapping-Info im Preview anzeigen
                p.CsvFunktion     = fnRawU;
                p.CsvContractType = ctRawU;
                p.JobGroupCode    = jgU?.Code;
                p.IsKader         = jgU?.IsKader;
                p.EmploymentModel = employmentModelU;
                p.EmploymentPercentage = (employmentModelU == "FIX-M" || employmentModelU == "FIX") ? pctParsedU : null;
                p.GuaranteedHoursPerWeek = (employmentModelU == "MTP") ? weeklyParsedU : null;
                p.ResolvedActive = isActive;

                if (!dryRun)
                {
                    var emp = await _db.Employees.FindAsync(dup.Id);
                    if (emp != null)
                    {
                        // In Voll-Migration: CSV ist Source of Truth → IMMER überschreiben (sofern CSV-Wert vorhanden)
                        // Sonst: nur befüllen wenn leer (mit IsNullOrWhiteSpace, fängt auch " " ab)
                        var csvAdresse = Get0(colAdresse).Trim('"', ' ');
                        var csvPlz     = Get0(colPLZ);
                        var csvStadt   = Get0(colStadt).Trim('"', ' ');
                        var csvTel     = Get0(colTel);
                        var csvEmail   = Get0(colEmail);
                        var csvNat     = Get0(colNat);
                        var csvAhv     = Get0(colAhv);
                        var csvGender  = NormalizeGender(Get0(colGender));

                        bool overwrite = fullMigration;
                        if ((overwrite && !string.IsNullOrWhiteSpace(csvAdresse)) || string.IsNullOrWhiteSpace(emp.Street))    emp.Street    = csvAdresse;
                        if ((overwrite && !string.IsNullOrWhiteSpace(csvPlz))     || string.IsNullOrWhiteSpace(emp.ZipCode))   emp.ZipCode   = csvPlz;
                        if ((overwrite && !string.IsNullOrWhiteSpace(csvStadt))   || string.IsNullOrWhiteSpace(emp.City))      emp.City      = csvStadt;
                        if ((overwrite && !string.IsNullOrWhiteSpace(csvTel))     || string.IsNullOrWhiteSpace(emp.PhoneMobile)) emp.PhoneMobile = csvTel;
                        if ((overwrite && !string.IsNullOrWhiteSpace(csvEmail))   || string.IsNullOrWhiteSpace(emp.Email))     emp.Email     = csvEmail;
                        if ((overwrite && !string.IsNullOrWhiteSpace(csvNat))     || string.IsNullOrWhiteSpace(emp.Nationality)) emp.Nationality = csvNat;
                        if ((overwrite && !string.IsNullOrWhiteSpace(csvAhv))     || string.IsNullOrWhiteSpace(emp.SocialSecurityNumber)) emp.SocialSecurityNumber = csvAhv;
                        if ((overwrite && csvGender != null)                       || emp.Gender == null) emp.Gender = csvGender;
                        if (emp.EntryDate == null || (overwrite && (p.EntryDate ?? betrZugehU).HasValue))
                            emp.EntryDate = (p.EntryDate ?? betrZugehU)?.ToDateTime(TimeOnly.MinValue);

                        // Bis-Datum + Aktiv-Flag setzen
                        emp.ExitDate = p.ExitDate?.ToDateTime(TimeOnly.MinValue);
                        emp.IsActive = isActive;
                        if (string.IsNullOrWhiteSpace(emp.Country)) emp.Country = "CH";

                        // Employment für diese Filiale ergänzen, falls noch keines da
                        if (!existingEmployments.Any(em => em.EmployeeId == emp.Id && em.CompanyProfileId == brExisting.Id))
                        {
                            var employment = new Employment {
                                EmployeeId = emp.Id,
                                CompanyProfileId = brExisting.Id,
                                EmploymentModel = employmentModelU,
                                SalaryType = salaryTypeU,
                                ContractStartDate = emp.EntryDate ?? DateTime.UtcNow,
                                ContractEndDate = emp.ExitDate,
                                JobTitle = string.IsNullOrEmpty(fnRawU) ? null : fnRawU,
                                ContractType = string.IsNullOrEmpty(ctRawU) ? null : ctRawU,
                                EmploymentPercentage = (employmentModelU == "FIX-M" || employmentModelU == "FIX") ? pctParsedU : null,
                                GuaranteedHoursPerWeek = (employmentModelU == "MTP") ? weeklyParsedU : null,
                                MonthlySalary    = (salaryTypeU == "monthly") ? salaryActualU : null,
                                MonthlySalaryFte = (salaryTypeU == "monthly") ? salaryFteU    : null,
                                HourlyRate       = (salaryTypeU == "hourly")  ? hourlyRateU   : null,
                                IsActive = isActive
                            };
                            _db.Employments.Add(employment);
                            existingEmployments.Add(new { EmployeeId = emp.Id, CompanyProfileId = (int?)brExisting.Id });
                        }

                        await _db.SaveChangesAsync();
                    }
                }
                result.Updated++;
                result.Preview.Add(p);
                continue;
            }

            // Bei Voll-Migration: keine "alt"-Suffix — Original-Nummer verwenden
            if (fullMigration) p.EmployeeNumber = p.OriginalEmployeeNumber;

            // employee_number-Kollision prüfen
            if (!string.IsNullOrEmpty(p.EmployeeNumber) &&
                existing.Any(e => e.EmployeeNumber == p.EmployeeNumber))
            {
                p.Action = "skip-invalid";
                p.Reason = $"Nummer {p.EmployeeNumber} existiert bereits (Re-Import?)";
                result.SkippedInvalid++;
                result.Preview.Add(p);
                continue;
            }

            // Vertragsdaten aus CSV ableiten (für Voll-Migration)
            var ctRaw = colContractType >= 0 ? Get0(colContractType).Trim('"') : "";
            var fnRaw = colFunktion     >= 0 ? Get0(colFunktion).Trim('"')     : "";
            var anzahl = colAnzahl      >= 0 ? Get0(colAnzahl)                 : "";
            var jg = FindJobGroupByFunktion(fnRaw, jobGroups);
            var employmentModel = MapEmploymentModel(ctRaw, anzahl, jg?.IsKader == true);
            var salaryType      = MapSalaryType(employmentModel);
            var (pctParsedC, weeklyParsedC) = ParseAnzahl(anzahl);

            // Mapping-Info im Preview anzeigen
            p.CsvFunktion     = fnRaw;
            p.CsvContractType = ctRaw;
            p.JobGroupCode    = jg?.Code;
            p.IsKader         = jg?.IsKader;
            p.EmploymentModel = employmentModel;
            p.EmploymentPercentage = (employmentModel == "FIX-M" || employmentModel == "FIX") ? pctParsedC : null;
            p.GuaranteedHoursPerWeek = (employmentModel == "MTP") ? weeklyParsedC : null;
            p.ResolvedActive = isActive;
            var (pctParsed, weeklyParsed) = ParseAnzahl(anzahl);
            var salaryActual    = colSalaryActual >= 0 ? ParseDecimal(Get0(colSalaryActual)) : null;
            var salaryFte       = colSalaryFte    >= 0 ? ParseDecimal(Get0(colSalaryFte))    : null;
            var hourlyRate      = colTarifsatz    >= 0 ? ParseDecimal(Get0(colTarifsatz))    : null;
            var betrZugehDate   = colBetrZugehDatum >= 0 ? ParseDateOnly(Get0(colBetrZugehDatum)) : null;

            // Importieren!
            p.Action = "create";
            if (!dryRun)
            {
                var emp = new Employee {
                    EmployeeNumber = p.EmployeeNumber,
                    FirstName = p.FirstName,
                    LastName = p.LastName,
                    DateOfBirth = p.DateOfBirth.HasValue ? p.DateOfBirth.Value.ToDateTime(TimeOnly.MinValue) : null,
                    Gender = NormalizeGender(Get0(colGender)),
                    Street = Get0(colAdresse).Trim('"', ' '),
                    ZipCode = Get0(colPLZ),
                    City = Get0(colStadt).Trim('"', ' '),
                    PhoneMobile = Get0(colTel),
                    Email = Get0(colEmail),
                    Nationality = Get0(colNat),
                    SocialSecurityNumber = Get0(colAhv),
                    EntryDate = (p.EntryDate ?? betrZugehDate)?.ToDateTime(TimeOnly.MinValue),
                    ExitDate  = p.ExitDate.HasValue ? p.ExitDate.Value.ToDateTime(TimeOnly.MinValue) : null,
                    Country = "CH",
                    IsActive = isActive
                };
                _db.Employees.Add(emp);
                await _db.SaveChangesAsync();

                // Employment mit korrektem Vertragsmodell + Lohn/Pensum
                var br = branches.First(b => b.RestaurantCode == p.BranchCode);
                var employment = new Employment {
                    EmployeeId = emp.Id,
                    CompanyProfileId = br.Id,
                    EmploymentModel = employmentModel,
                    SalaryType = salaryType,
                    ContractStartDate = emp.EntryDate ?? DateTime.UtcNow,
                    ContractEndDate = emp.ExitDate, // auch wenn in Zukunft (befristet)
                    JobTitle = string.IsNullOrEmpty(fnRaw) ? null : fnRaw,
                    ContractType = string.IsNullOrEmpty(ctRaw) ? null : ctRaw,
                    EmploymentPercentage = (employmentModel == "FIX-M" || employmentModel == "FIX") ? pctParsed : null,
                    GuaranteedHoursPerWeek = (employmentModel == "MTP") ? weeklyParsed : null,
                    MonthlySalary    = (salaryType == "monthly") ? salaryActual : null,
                    MonthlySalaryFte = (salaryType == "monthly") ? salaryFte    : null,
                    HourlyRate       = (salaryType == "hourly")  ? hourlyRate   : null,
                    IsActive = isActive
                };
                _db.Employments.Add(employment);
                await _db.SaveChangesAsync();

                // In existing einfügen, falls weitere Zeilen mit gleichem MA referenzieren
                existing.Add(new { emp.Id, emp.EmployeeNumber, emp.FirstName, emp.LastName, emp.DateOfBirth });
                existingEmployments.Add(new { EmployeeId = emp.Id, CompanyProfileId = (int?)br.Id });
            }
            result.Imported++;
            result.Preview.Add(p);
        }

        return Ok(result);
    }

    // ──────────────────────────────────────────────────────────────────
    // CSV-Parser — minimal aber RFC-4180-konform genug für unseren Export
    // ──────────────────────────────────────────────────────────────────
    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') inQuotes = false;
                else sb.Append(c);
            }
            else
            {
                if (c == ',') { result.Add(sb.ToString()); sb.Clear(); }
                else if (c == '"') inQuotes = true;
                else sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    private static DateOnly? ParseDateOnly(string s)
    {
        s = s.Trim('"', ' ');
        if (string.IsNullOrEmpty(s)) return null;
        // ISO 1995-01-01
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d1)) return d1;
        // CH 01.01.1995
        if (DateOnly.TryParseExact(s, "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d2)) return d2;
        // Datetime mit Zeit
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return DateOnly.FromDateTime(dt);
        return null;
    }

    private static string? NormalizeGender(string s)
    {
        s = s?.Trim().ToLowerInvariant() ?? "";
        if (s == "male" || s == "m" || s == "männlich") return "male";
        if (s == "female" || s == "f" || s == "weiblich") return "female";
        return null;
    }

    /// <summary>
    /// Mappt CSV-Vertragstyp + Anzahl + Kader-Status auf unser EmploymentModel.
    ///   Fix + Kader     → FIX-M
    ///   Fix + Non-Kader → FIX
    ///   MTP/TPM mit "X Stunden/Woche" → MTP
    ///   Alles andere (auch "Stunden/Monat", Flex, leer) → UTP
    /// is_kader wird via job_group.mirus_funktion_aliases bestimmt.
    /// </summary>
    private static string MapEmploymentModel(string contractType, string anzahl, bool isKader)
    {
        var ct = (contractType ?? "").Trim().ToLowerInvariant();
        var an = (anzahl ?? "").Trim().ToLowerInvariant();
        if (ct == "fix")
            return isKader ? "FIX-M" : "FIX";
        if ((ct.Contains("mtp") || ct.Contains("tpm")) && an.Contains("stunden/woche"))
            return "MTP";
        return "UTP";
    }

    /// <summary>
    /// Findet die JobGroup für eine CSV-Funktion via mirus_funktion_aliases.
    /// Rückgabe: gefundene JobGroup oder null. Vergleich case-insensitive +
    /// trim auf jeder Alias-Position.
    /// </summary>
    private static JobGroup? FindJobGroupByFunktion(string funktion, List<JobGroup> all)
    {
        if (string.IsNullOrWhiteSpace(funktion)) return null;
        var fn = funktion.Trim();
        foreach (var g in all)
        {
            if (string.IsNullOrWhiteSpace(g.MirusFunktionAliases)) continue;
            var aliases = g.MirusFunktionAliases.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (aliases.Any(a => a.Equals(fn, StringComparison.OrdinalIgnoreCase)))
                return g;
        }
        return null;
    }

    private static string MapSalaryType(string employmentModel)
        => employmentModel == "UTP" ? "hourly" : "monthly";

    /// <summary>
    /// Parsed "Anzahl"-Feld:
    ///   "80%"               → percentage = 80
    ///   "17 Stunden/Woche"  → weekly hours = 17
    ///   "17 Stunden/Monat"  → monatlich → grob auf Wochenstunden runter (÷ 4.33)
    ///   leer                → beides null
    /// </summary>
    private static (decimal? percentage, decimal? weeklyHours) ParseAnzahl(string anzahl)
    {
        anzahl = (anzahl ?? "").Trim().Trim('"');
        if (string.IsNullOrEmpty(anzahl)) return (null, null);

        // 80% / 80 %
        var m1 = System.Text.RegularExpressions.Regex.Match(anzahl, @"^\s*(\d+(?:[.,]\d+)?)\s*%\s*$");
        if (m1.Success && decimal.TryParse(m1.Groups[1].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return (pct, null);

        // 17 Stunden/Woche
        var m2 = System.Text.RegularExpressions.Regex.Match(anzahl, @"(\d+(?:[.,]\d+)?)\s*Stunden?/Woche", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m2.Success && decimal.TryParse(m2.Groups[1].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var wh))
            return (null, wh);

        // 17 Stunden/Monat → ÷ 4.33 ≈ Wochenstunden
        var m3 = System.Text.RegularExpressions.Regex.Match(anzahl, @"(\d+(?:[.,]\d+)?)\s*Stunden?/Monat", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m3.Success && decimal.TryParse(m3.Groups[1].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var mh))
            return (null, Math.Round(mh / 4.33m, 2));

        return (null, null);
    }

    private static decimal? ParseDecimal(string s)
    {
        s = (s ?? "").Trim().Trim('"');
        if (string.IsNullOrEmpty(s)) return null;
        return decimal.TryParse(s.Replace(',', '.'),
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? d : null;
    }
}
