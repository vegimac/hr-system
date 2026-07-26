using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeeImportController : ControllerBase
{
    private readonly AppDbContext _context;

    public EmployeeImportController(AppDbContext context)
    {
        _context = context;
    }

    [HttpPost("upload/{companyProfileId:int}")]
    public async Task<IActionResult> UploadCsv(int companyProfileId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Keine CSV-Datei hochgeladen.");

        var company = await _context.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyProfileId);

        if (company == null)
            return BadRequest("Betrieb nicht gefunden.");

        if (string.IsNullOrWhiteSpace(company.RestaurantCode))
            return BadRequest("RestaurantCode fehlt im CompanyProfile.");

        var restaurantPrefix = NormalizeRestaurantPrefix(company.RestaurantCode);
        var rows = new List<ImportEmployeeRow>();

        using (var stream = file.OpenReadStream())
        using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, true, leaveOpen: true))
        {
            var firstLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(firstLine))
                return BadRequest("CSV ist leer.");

            var delimiter = DetectDelimiter(firstLine);
            stream.Position = 0;

            using var parser = new TextFieldParser(stream);
            parser.SetDelimiters(delimiter.ToString());
            parser.HasFieldsEnclosedInQuotes = true;
            parser.TrimWhiteSpace = false;

            if (parser.EndOfData)
                return BadRequest("CSV ist leer.");

            var headers = parser.ReadFields();
            if (headers == null)
                return BadRequest("CSV-Header konnte nicht gelesen werden.");

            var headerMap = headers
                .Select((h, i) => new { Header = (h ?? "").Trim(), Index = i })
                .ToDictionary(x => x.Header, x => x.Index, StringComparer.OrdinalIgnoreCase);

            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                if (fields == null || fields.Length == 0 || fields.All(string.IsNullOrWhiteSpace))
                    continue;

                var employeeNumber = GetEmployeeNumber(fields, headerMap);
                if (string.IsNullOrWhiteSpace(employeeNumber))
                    continue;

                employeeNumber = NormalizeEmployeeNumber(employeeNumber);
                var storeNumber = NormalizeStoreNumber(GetValue(fields, headerMap, "Store number"));

                if (!string.IsNullOrWhiteSpace(storeNumber) &&
                    !string.Equals(storeNumber, restaurantPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(storeNumber) &&
                    !employeeNumber.StartsWith(restaurantPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // ── Klassifizierung anhand Group memberships (Primärquelle) ──
                // Muss vor dem Zeilen-Aufbau passieren, weil JobGroup, Modell
                // und IsPayrollExcluded davon abhängen — und weil bei
                // „Supervisor" weder ein Modell noch ein Vertrag erzeugt
                // werden darf.
                var rawGroup     = GetValue(fields, headerMap, "Group memberships");
                if (string.IsNullOrWhiteSpace(rawGroup))
                    rawGroup = GetValue(fields, headerMap, "Group membership");
                if (string.IsNullOrWhiteSpace(rawGroup))
                    rawGroup = GetValue(fields, headerMap, "Member group");
                var rawCt        = GetValue(fields, headerMap, "Contract type");
                var rawPayFreq   = GetValue(fields, headerMap, "Pay frequency");
                var rawFunktion  = FirstNonEmpty(
                                       GetValue(fields, headerMap, "Funktion"),
                                       GetValue(fields, headerMap, "Funktionen"),
                                       GetValue(fields, headerMap, "Function"),
                                       GetValue(fields, headerMap, "Job title"));

                var classification = ResolveClassification(rawGroup, rawCt, rawPayFreq, rawFunktion);

                var genderMapped = MapGender(FirstNonEmpty(
                    GetValue(fields, headerMap, "Geschlecht"),
                    GetValue(fields, headerMap, "Gender")
                ));

                rows.Add(new ImportEmployeeRow
                {
                    EmployeeNumber = employeeNumber,
                    // Anrede: aus CSV, falls leer aus Geschlecht ableiten
                    // (Walter: easy@work liefert Anrede oft nicht).
                    Salutation = NormalizeSalutation(
                        GetValue(fields, headerMap, "Anrede"),
                        genderMapped),
                    Gender = genderMapped,
                    FirstName = FirstNonEmpty(
                        GetValue(fields, headerMap, "Vorname"),
                        GetValue(fields, headerMap, "First name")
                    ),
                    LastName = FirstNonEmpty(
                        GetValue(fields, headerMap, "Nachname"),
                        GetValue(fields, headerMap, "Name"),
                        GetValue(fields, headerMap, "Last name")
                    ),
                    // easy@work Nickname → short_name (Walter 17.07.2026).
                    ShortName = FirstNonEmpty(
                        GetValue(fields, headerMap, "Nickname"),
                        GetValue(fields, headerMap, "Kurzname"),
                        GetValue(fields, headerMap, "nickname")
                    ),
                    Address = FirstNonEmpty(
                        GetValue(fields, headerMap, "Adresse"),
                        GetValue(fields, headerMap, "Adresse 1")
                    ),
                    Address2 = FirstNonEmpty(
                        GetValue(fields, headerMap, "Adresse 2")
                    ),
                    ZipCode = FirstNonEmpty(
                        GetValue(fields, headerMap, "Postleitzahl"),
                        GetValue(fields, headerMap, "PLZ")
                    ),
                    City = FirstNonEmpty(
                        GetValue(fields, headerMap, "Stadt"),
                        GetValue(fields, headerMap, "Ort")
                    ),
                    Country = FirstNonEmpty(
                        GetValue(fields, headerMap, "COUNTRY"),
                        GetValue(fields, headerMap, "Country")
                    ),
                    DateOfBirth = ParseDate(FirstNonEmpty(
                        GetValue(fields, headerMap, "Geburtsdatum")
                    )),
                    Email = FirstNonEmpty(
                        GetValue(fields, headerMap, "E-Mail"),
                        GetValue(fields, headerMap, "Email")
                    ),
                    Phone = FirstNonEmpty(
                        GetValue(fields, headerMap, "Telefon"),
                        GetValue(fields, headerMap, "Phone")
                    ),
                    Nationality = FirstNonEmpty(
                        GetValue(fields, headerMap, "Nationalität"),
                        GetValue(fields, headerMap, "Nationality"),
                        "CH"
                    ),
                    // Eintritt = «Datum der Betriebszugehörigkeit» (Walter 26.07.2026,
                    // analog easy@work-Sync). Fallback «Von» / «Eintrittsdatum».
                    EntryDate = ParseDate(FirstNonEmpty(
                        GetValue(fields, headerMap, "Datum der Betriebszugehörigkeit"),
                        GetValue(fields, headerMap, "Von"),
                        GetValue(fields, headerMap, "Eintrittsdatum")
                    )),
                    // ContractStartDate = "Pay rate from" (echter Lohn-Beginn
                    // bei Lohnänderungen). Fallback "Von" wenn Pay-rate-Feld
                    // leer ist. Ein Lohnwechsel im easy@work-CSV manifestiert
                    // sich als neuer Pay-rate-from-Datensatz; "Von" bleibt
                    // dann das ursprüngliche Eintrittsdatum.
                    ContractStartDate = ParseDate(FirstNonEmpty(
                        GetValue(fields, headerMap, "Pay rate from"),
                        GetValue(fields, headerMap, "Von")
                    )),
                    ExitDate = ParseDate(FirstNonEmpty(
                        GetValue(fields, headerMap, "Austrittsdatum")
                    )),
                    ContractEndDate = ParseDate(FirstNonEmpty(
                        GetValue(fields, headerMap, "Bis")
                    )),
                    // easy@work-Quirk: EXPIRATN_DT enthält den Bewilligungs-CODE (B/C/L/S),
                    // EMISSION_DT enthält das ABLAUF-Datum. VISA_PERMIT_TYPE ist meist
                    // "CHE" und unbrauchbar (Land der Ausstellung).
                    // Robuste Logik in ResolvePermitFromCsv weiter unten — unterscheidet
                    // automatisch ob ein Wert ein Datum oder ein Code ist.
                    PermitTypeRaw = ResolvePermitCode(
                        GetValue(fields, headerMap, "EXPIRATN_DT"),
                        GetValue(fields, headerMap, "VISA_PERMIT_TYPE"),
                        GetValue(fields, headerMap, "UN_VISA_PERMIT_TYPE"),
                        GetValue(fields, headerMap, "Visa/Permit type")
                    ),
                    MaritalStatus = MapMaritalStatus(FirstNonEmpty(
                        GetValue(fields, headerMap, "Familienstand"),
                        GetValue(fields, headerMap, "Marital status")
                    )),
                    PermitExpiryDate = ParseDate(ResolvePermitExpiry(
                        GetValue(fields, headerMap, "EMISSION_DT"),
                        GetValue(fields, headerMap, "EXPIRATN_DT"),
                        GetValue(fields, headerMap, "Visa expiration date")
                    )),
                    // JobGroup + Modell + Phantom-Flag kommen aus der zentralen
                    // Klassifizierungs-Logik (siehe ResolveClassification weiter
                    // unten). Group memberships ist die Primärquelle, Funktion
                    // wirkt nur als Fallback bei leerer Group membership.
                    JobGroupCodeSuggestion = classification.JobGroupCode,
                    JobTitleSuggestion = rawFunktion,
                    EmploymentModelSuggestion = classification.EmploymentModel,
                    IsPayrollExcluded = classification.IsPayrollExcluded,
                    ContractTypeSuggestion = MapContractType(FirstNonEmpty(
                        GetValue(fields, headerMap, "Contract type"),
                        GetValue(fields, headerMap, "Vertragstyp")
                    )),
                    HourlyRateSuggestion = IsHourlyModel(classification.EmploymentModel)
                        ? ParseDecimal(FirstNonEmpty(
                            GetValue(fields, headerMap, "Tarife"),
                            GetValue(fields, headerMap, "Hourly rate"),
                            GetValue(fields, headerMap, "Stundenlohn")))
                        : null,
                    // Pensum aus CSV (z.B. "80" oder "80.00")
                    EmploymentPercentageSuggestion = ParseDecimal(FirstNonEmpty(
                        GetValue(fields, headerMap, "Pensum"),
                        GetValue(fields, headerMap, "Employment percentage"),
                        GetValue(fields, headerMap, "FTE percent"),
                        GetValue(fields, headerMap, "Percentage")
                    )),
                    // 100%-Lohn aus CSV (FTE = Full-Time Equivalent)
                    // Priorität: explizite FTE-Spalten vor generischen Lohn-Spalten
                    MonthlySalaryFteSuggestion = !IsHourlyModel(classification.EmploymentModel)
                        ? ParseDecimal(FirstNonEmpty(
                            GetValue(fields, headerMap, "Salary (FTE)"),
                            GetValue(fields, headerMap, "Tarife"),
                            GetValue(fields, headerMap, "Monatslohn")))
                        : null,
                    // Wochenstunden: aus „Anzahl"-Spalte; bei UTP mit leerem
                    // Wert greift unten der 17h-Default (analog Walter-Vorgabe).
                    WeeklyHoursSuggestion = ApplyWeeklyHoursDefault(
                        ParseAnzahlHours(FirstNonEmpty(
                            GetValue(fields, headerMap, "Anzahl"),
                            GetValue(fields, headerMap, "Anzahl Stunden"),
                            GetValue(fields, headerMap, "Weekly hours"),
                            GetValue(fields, headerMap, "Hours")
                        )),
                        classification.EmploymentModel),
                    // Pensum aus Anzahl-Spalte wenn Wert % enthält
                    EmploymentPercentageFromAnzahl = ParseAnzahlPercent(FirstNonEmpty(
                        GetValue(fields, headerMap, "Anzahl"),
                        GetValue(fields, headerMap, "Anzahl Stunden"),
                        GetValue(fields, headerMap, "Weekly hours"),
                        GetValue(fields, headerMap, "Hours")
                    ))
                });
            }
        }

        if (rows.Count == 0)
        {
            return Ok(new
            {
                restaurantCode = company.RestaurantCode,
                restaurantPrefix,
                importedRows = 0,
                inserted = 0,
                updated = 0,
                reactivated = 0,
                deactivated = 0,
                personnelOnly = 0,
                message = "Keine passenden Mitarbeitenden für diesen Betrieb im CSV gefunden."
            });
        }

        var permitTypes = await _context.PermitTypes
            .Where(p => p.IsActive)
            .ToListAsync();

        var nationalities = await _context.Nationalities
            .Where(n => n.IsActive)
            .ToListAsync();

        var existingEmployees = await _context.Employees.ToListAsync();

        // ── Aktiv vs. Personaldossier-Klassifizierung ───────────────────────
        // Walter-Regel: Bis-Datum < heute = inaktiv (echter Austritt) → nur
        // Personaldossier importieren, KEIN Vertrag, KEIN Lohn-Check.
        // Bis-Datum leer ODER >= heute = aktiv (auch für befristete laufende
        // Verträge) → voll mit Vertrag/Compliance-Check.
        // Pro Personalnummer aggregieren: hat der MA mind. eine offene Zeile?
        var today = DateTime.Today;
        var isActiveByNumber = rows
            .GroupBy(r => r.EmployeeNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Any(r => r.ExitDate == null || r.ExitDate.Value >= today),
                StringComparer.OrdinalIgnoreCase
            );

        // Walter-Vorgabe 14.06.2026: MA mit Austritt VOR dem 1.1.2025 werden
        // gar nicht importiert. Mirus-Cutoff = 1.1.2025 — alles davor ist
        // legacy und gehört NICHT ins neue System (auch nicht als Personal-
        // dossier). Pro Pers-Nr das SPÄTESTE Bis-Datum aggregieren; ist es
        // gesetzt UND < 1.1.2025 → die Zeile wird übersprungen.
        var minImportExitDate = new DateTime(2025, 1, 1);
        var latestExitByNumber = rows
            .Where(r => r.ExitDate.HasValue)
            .GroupBy(r => r.EmployeeNumber, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Max(r => r.ExitDate!.Value),
                StringComparer.OrdinalIgnoreCase
            );

        bool IsTooOldExit(string empNr)
            => latestExitByNumber.TryGetValue(empNr, out var lastExit)
               && !isActiveByNumber.GetValueOrDefault(empNr, true)
               && lastExit < minImportExitDate;

        // Alle Zeilen werden verarbeitet; pro Zeile entscheidet die Aktiv-
        // Aggregation über Stammdaten-only oder Voll-Verarbeitung. Inaktive
        // Zeilen sind reine Personaldossier (Karteileiche).
        var employeeNumbersInImport = rows
            .Where(r => !IsTooOldExit(r.EmployeeNumber))
            .Select(r => r.EmployeeNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int inserted = 0;
        int updated = 0;
        int reactivated = 0;
        int deactivated = 0;
        int personnelOnly = 0;   // inaktive MA als Personaldossier angelegt/aktualisiert
        int skippedTooOld = 0;   // Austritt vor 1.1.2025 — komplett übersprungen

        foreach (var row in rows)
        {
            // Walter-Vorgabe 14.06.2026: zu alte Austritte → nicht importieren.
            if (IsTooOldExit(row.EmployeeNumber))
            {
                skippedTooOld++;
                continue;
            }
            var rowIsActive = isActiveByNumber.GetValueOrDefault(row.EmployeeNumber, true);

            var employee = existingEmployees
                .FirstOrDefault(e => string.Equals(e.EmployeeNumber, row.EmployeeNumber, StringComparison.OrdinalIgnoreCase));

            if (employee == null)
            {
                employee = new Employee
                {
                    EmployeeNumber = row.EmployeeNumber,
                    IsActive = rowIsActive
                };

                _context.Employees.Add(employee);
                existingEmployees.Add(employee);
                inserted++;
                if (!rowIsActive) personnelOnly++;
            }
            else
            {
                updated++;

                if (rowIsActive)
                {
                    if (!employee.IsActive) reactivated++;
                    employee.IsActive = true;
                }
                else
                {
                    // Vorhandener MA, der jetzt inaktiv ist (Bis-Datum in der
                    // Vergangenheit). Auf inaktiv setzen, aber NICHT als
                    // „deactivated" zählen — der Re-Import ist die Quelle der
                    // Wahrheit.
                    employee.IsActive = false;
                    personnelOnly++;
                }
            }

            employee.Salutation = NullIfEmpty(row.Salutation);
            employee.FirstName = NullIfEmpty(row.FirstName) ?? employee.FirstName;
            employee.LastName = NullIfEmpty(row.LastName) ?? employee.LastName;
            // Kurzname nur setzen wenn CSV einen Wert liefert (nie still leeren).
            if (!string.IsNullOrWhiteSpace(row.ShortName))
                employee.ShortName = row.ShortName.Trim();

            employee.Street = MergeAddress(row.Address, row.Address2);

            employee.ZipCode = NullIfEmpty(row.ZipCode);
            employee.City = NullIfEmpty(row.City);
            employee.Country = NullIfEmpty(row.Country);

            employee.DateOfBirth = row.DateOfBirth;
            employee.Email = NullIfEmpty(row.Email);
            employee.PhoneMobile = NullIfEmpty(row.Phone);

            employee.Nationality = NullIfEmpty(row.Nationality);
            employee.NationalityId = ResolveNationalityId(row.Nationality, nationalities);

            // EntryDate/ExitDate werden weiter unten gesetzt — siehe
            // Eintrittsdatum-Schutz-Block.

            employee.PermitTypeId = ResolvePermitTypeId(row.PermitTypeRaw, permitTypes);
            // employee.PermitExpiryDate entfernt 01.06.2026 — Ablauf-Datum lebt
            // jetzt ausschliesslich auf EmployeePermitHistory.ValidTo. Der CSV-
            // Import liest die Spalte EXPIRATN_DT weiterhin in row.PermitExpiryDate
            // ein, aber dieser Pfad legt KEINE History-Zeile an — dafür ist
            // PermitImportController (Bewilligungsliste) zuständig.

            // NEU: Gender auf dem Employee speichern
            if (!string.IsNullOrWhiteSpace(row.Gender))
                employee.Gender = row.Gender;

            // Familienstand: nur überschreiben wenn neuer Wert da ist —
            // existierende manuell gepflegte Werte bleiben erhalten.
            if (!string.IsNullOrWhiteSpace(row.MaritalStatus))
                employee.MaritalStatus = row.MaritalStatus;

            // Eintritt nachführen (Betriebszugehörigkeit / Von) — Re-Import
            // überschreibt (früherer Leer-Guard entfernt, Walter 26.07.2026).
            if (row.EntryDate.HasValue)
                employee.EntryDate = row.EntryDate;

            employee.ExitDate = row.ExitDate;

            // IsPayrollExcluded: setzen wenn Klassifizierung „Supervisor" ergibt.
            // Walter-Regel: Phantom-MA-Markierung wird beim Re-Import NICHT
            // entfernt — wenn der Flag manuell gesetzt war, bleibt er auch
            // wenn der CSV-Eintrag jetzt anders aussieht. Wir setzen ihn nur
            // ZUSÄTZLICH auf true, niemals zurück auf false.
            if (row.IsPayrollExcluded)
                employee.IsPayrollExcluded = true;

            // Snapshot nur für AKTIVE MA mit Vertrag. Inaktive (Karteileiche)
            // und Phantom-MA (Supervisor) brauchen keinen Vorschlag, da nie
            // ein Vertrag aus dem Import-UI angelegt wird.
            if (rowIsActive && !row.IsPayrollExcluded)
                await SaveSnapshotAsync(employee, row);
        }

        // Bestehende aktive MA dieser Filiale, die nicht mehr im CSV stehen,
        // werden deaktiviert. Bereits archivierte (Suffix "alt") Einträge
        // bleiben unangetastet — die kommen aus dem Pre-Mirus-Migrations-Import.
        foreach (var employee in existingEmployees.Where(e =>
                     e.IsActive &&
                     !e.EmployeeNumber.EndsWith("alt", StringComparison.OrdinalIgnoreCase) &&
                     NormalizeEmployeeNumber(e.EmployeeNumber).StartsWith(restaurantPrefix, StringComparison.OrdinalIgnoreCase) &&
                     !employeeNumbersInImport.Contains(NormalizeEmployeeNumber(e.EmployeeNumber))))
        {
            employee.IsActive = false;
            deactivated++;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            restaurantCode = company.RestaurantCode,
            restaurantPrefix,
            importedRows = rows.Count,
            inserted,
            updated,
            reactivated,
            deactivated,
            personnelOnly,   // davon: als reines Personaldossier (Bis < heute)
            skippedTooOld    // Austritt vor 1.1.2025 — komplett übersprungen
        });
    }

    private async Task SaveSnapshotAsync(Employee employee, ImportEmployeeRow row)
    {
        if (employee.Id == 0)
        {
            await _context.SaveChangesAsync();
        }

        var oldSnapshots = await _context.EmployeeImportSnapshots
            .Where(x => x.EmployeeId == employee.Id && x.IsActive)
            .ToListAsync();

        foreach (var old in oldSnapshots)
        {
            old.IsActive = false;
        }

        _context.EmployeeImportSnapshots.Add(new EmployeeImportSnapshot
        {
            EmployeeId = employee.Id,
            Gender = row.Gender,   // NEU
            JobGroupCode = row.JobGroupCodeSuggestion,
            JobTitle = row.JobTitleSuggestion,
            EmploymentModel = row.EmploymentModelSuggestion,
            ContractType = row.ContractTypeSuggestion,
            HourlyRate = row.HourlyRateSuggestion,
            MonthlySalaryFte = row.MonthlySalaryFteSuggestion,
            MonthlySalary = row.MonthlySalarySuggestion,
            WeeklyHours = row.WeeklyHoursSuggestion,
            EmploymentPercentage = row.EmploymentPercentageSuggestion ?? row.EmploymentPercentageFromAnzahl,
            ContractEndDate = row.ContractEndDate.HasValue ? DateOnly.FromDateTime(row.ContractEndDate.Value) : null,
            NationalityCode = row.Nationality,
            ImportedAt = DateTime.UtcNow,
            IsActive = true
        });
    }

    private static char DetectDelimiter(string firstLine)
    {
        var semicolons = firstLine.Count(c => c == ';');
        var commas = firstLine.Count(c => c == ',');
        return semicolons >= commas ? ';' : ',';
    }

    private static string GetEmployeeNumber(string[] fields, Dictionary<string, int> headerMap)
    {
        return FirstNonEmpty(
            GetValue(fields, headerMap, "Nummer"),
            GetValue(fields, headerMap, "Personalnummer"),
            GetValue(fields, headerMap, "EMPLID"),
            GetValue(fields, headerMap, "ALTER_EMPLID"),
            GetValue(fields, headerMap, "custom_fields.EMPLOYEENUMBER IN PAYROLL SYSTEM")
        ) ?? "";
    }

    private static string GetValue(string[] fields, Dictionary<string, int> headerMap, string headerName)
    {
        if (!headerMap.TryGetValue(headerName, out var index))
            return "";

        if (index < 0 || index >= fields.Length)
            return "";

        return (fields[index] ?? "").Trim();
    }

    private static string? FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    private static string? NullIfEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    // NEU: Gender mappen (female/male aus CSV → normalisiert speichern)
    private static string? MapGender(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "female"    => "female",
            "male"      => "male",
            "weiblich"  => "female",
            "männlich"  => "male",
            "divers"    => "divers",
            "diverse"   => "divers",
            "andere"    => "divers",
            "other"     => "divers",
            "x"         => "divers",
            "d"         => "divers",
            "w"         => "female",
            "m"         => "male",
            _           => null
        };
    }

    /// <summary>
    /// easy@work hat die Bewilligungs-Spalten verdreht: in EXPIRATN_DT
    /// steht der CODE (B/C/L/S), in EMISSION_DT das Datum, und
    /// VISA_PERMIT_TYPE ist meist "CHE" (= Land der Ausstellung) und
    /// damit unbrauchbar.
    ///
    /// Diese Helper sind robust: sie unterscheiden automatisch ob ein
    /// Wert ein Datum oder ein Code ist — falls ein anderer Export-
    /// Dialekt es doch richtig herum hat, funktioniert's auch.
    /// </summary>
    private static string? ResolvePermitCode(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var v = c.Trim();
            // Datum erkennen → ist NICHT ein Code, überspringen
            if (LooksLikeDate(v)) continue;
            // Land-Code "CHE", "DEU" etc. ausschliessen — das ist Land der Ausstellung,
            // nicht Bewilligungstyp. Bewilligungstypen sind 1-2 Zeichen (B, C, L, S, F, G, N).
            if (v.Length >= 3 && v.All(char.IsLetter)) continue;
            return v;
        }
        return null;
    }

    private static string? ResolvePermitExpiry(params string?[] candidates)
    {
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var v = c.Trim();
            // Nur Werte zurückgeben die wie ein Datum aussehen — Codes wie "C" überspringen.
            if (LooksLikeDate(v)) return v;
        }
        return null;
    }

    private static bool LooksLikeDate(string v)
    {
        // Heuristik: enthält Trennzeichen (./-) und mindestens eine Zahlen-Gruppe ≥3 Zeichen
        // (Tag/Monat/Jahr-Format). z.B. "01.11.2028", "2028-11-01", "1.11.28".
        if (v.Length < 6) return false;
        var hasSep = v.Contains('.') || v.Contains('-') || v.Contains('/');
        if (!hasSep) return false;
        // Mindestens eine Zahl mit ≥3 Stellen (Jahr) oder ≥6 Stellen total
        return v.Any(char.IsDigit) && v.Count(char.IsDigit) >= 4;
    }

    /// <summary>
    /// Mappt easy@work-Familienstand-Klartext auf den internen Code, der
    /// im Frontend-Dropdown verwendet wird (employees.js). Werte gemäss
    /// easy@work-Liste: Unbekannt, Ledig, Verheiratet, Geschieden, Verwitwet,
    /// Getrennt, Eingetragene Partnerschaft.
    /// </summary>
    private static string? MapMaritalStatus(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "unbekannt" or "unknown"                                  => "unbekannt",
            "ledig" or "single"                                       => "ledig",
            "verheiratet" or "married"                                => "verheiratet",
            "geschieden" or "divorced"                                => "geschieden",
            "verwitwet" or "widowed"                                  => "verwitwet",
            "getrennt" or "separated"                                 => "getrennt",
            "eingetragene partnerschaft" or "eingetr. partnerschaft" or "registered partnership" => "eingetragene_partnerschaft",
            _ => null
        };
    }

    private static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var formats = new[]
        {
            "yyyy-MM-dd",
            "dd.MM.yyyy",
            "d.M.yyyy",
            "dd/MM/yyyy",
            "d/M/yyyy"
        };

        if (DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var exactDate))
            return exactDate;

        if (DateTime.TryParse(value.Trim(), out var parsedDate))
            return parsedDate;

        return null;
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Trim()
            .Replace("CHF", "", StringComparison.OrdinalIgnoreCase)
            .Replace("'", "")
            .Replace(" ", "");

        if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var inv))
            return inv;

        if (decimal.TryParse(cleaned, NumberStyles.Any, new CultureInfo("de-CH"), out var ch))
            return ch;

        if (decimal.TryParse(cleaned, NumberStyles.Any, new CultureInfo("de-DE"), out var de))
            return de;

        return null;
    }

    private static string? MergeAddress(string? address1, string? address2)
    {
        var line1 = NullIfEmpty(address1);
        var line2 = NullIfEmpty(address2);

        if (string.IsNullOrWhiteSpace(line1))
            return line2;

        return string.Join(" ", new[] { line1, line2 }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();
    }

    private static int? ResolvePermitTypeId(string? rawValue, List<PermitType> permitTypes)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        var value = rawValue.Trim();

        var directCodeMatch = permitTypes.FirstOrDefault(p =>
            string.Equals(p.Code, value, StringComparison.OrdinalIgnoreCase));
        if (directCodeMatch != null)
            return directCodeMatch.Id;

        var descriptionMatch = permitTypes.FirstOrDefault(p =>
            string.Equals(p.Description, value, StringComparison.OrdinalIgnoreCase));
        if (descriptionMatch != null)
            return descriptionMatch.Id;

        var normalized = value.ToUpperInvariant();

        var mappedCode = normalized switch
        {
            "B" => "B",
            "C" => "C",
            "CI" => "CI",
            "G" => "G",
            "L" => "L",
            "F" => "F",
            "N" => "N",
            "S" => "S",
            "B EU/EFTA" => "B_EU_EFTA",
            "C EU/EFTA" => "C_EU_EFTA",
            "CI EU/EFTA" => "CI_EU_EFTA",
            "G EU/EFTA" => "G_EU_EFTA",
            "L EU/EFTA" => "L_EU_EFTA",
            _ => null
        };

        if (mappedCode != null)
        {
            var mapped = permitTypes.FirstOrDefault(p =>
                string.Equals(p.Code, mappedCode, StringComparison.OrdinalIgnoreCase));

            if (mapped != null)
                return mapped.Id;
        }

        return null;
    }

    private static int? ResolveNationalityId(string? rawValue, List<Nationality> nationalities)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
            return null;

        var value = rawValue.Trim().ToUpperInvariant();

        var match = nationalities.FirstOrDefault(n =>
            string.Equals(n.Code, value, StringComparison.OrdinalIgnoreCase));

        return match?.Id;
    }

    // ── Klassifizierung ──────────────────────────────────────────────────
    // Walter-Regel (siehe CLAUDE.md): Group memberships ist Primärquelle für
    // Hierarchie und Vertragsmodell — die Funktion-Spalte ist nur Fallback.
    //
    //   Group membership            JobGroup              Modell    Bemerkung
    //   ────────────────────────────────────────────────────────────────────
    //   Store Manager               REST_MANAGER          FIX-M     Restaurant-Leiter
    //   Shift Manager+              SHIFT_LEADER_7_PLUS   FIX-M     >6 Mt. Erfahrung
    //   Shift Manager-              SHIFT_LEADER_1_6      FIX-M     1–6 Mt. Erfahrung
    //   Supervisor                  —                     —         IsPayrollExcluded=true (Phantom)
    //   Employee / leer             CREW                  s. Tab    abh. von Pay frequency × Contract type
    //
    // Für CREW: Pay frequency=month → FIX, hour+MTP/TPM → MTP, hour+Flex/leer → UTP
    private readonly struct Classification
    {
        public string? JobGroupCode { get; init; }       // null nur bei Phantom-MA
        public string? EmploymentModel { get; init; }    // null nur bei Phantom-MA
        public bool IsPayrollExcluded { get; init; }
    }

    private static Classification ResolveClassification(
        string? groupMembership, string? contractType, string? payFrequency, string? funktion)
    {
        var g = (groupMembership ?? "").Trim().ToLowerInvariant();

        // 1) Supervisor → Phantom-MA, kein Vertrag.
        if (g.Contains("supervisor"))
            return new Classification { IsPayrollExcluded = true };

        // 2) Manager-Gruppen — JobGroup deterministisch aus Group memberships,
        //    Modell IMMER FIX-M (egal was im Contract type steht).
        if (g.Contains("store manager"))
            return new Classification { JobGroupCode = "REST_MANAGER", EmploymentModel = "FIX-M" };

        if (g.Contains("shift manager"))
        {
            // „Shift Manager+" oder „Shift Manager Plus" → 7+ Monate Erfahrung
            // „Shift Manager-" oder „Shift Manager Minus" → 1–6 Monate
            // Default-Variante (nur „Shift Manager") behandeln wir konservativ
            // als 1–6 Mt. (= tieferer Mindestlohn — wenn falsch, wird Walter
            // im Edit hochstufen).
            var isPlus = g.Contains("+") || g.Contains("plus");
            return new Classification
            {
                JobGroupCode = isPlus ? "SHIFT_LEADER_7_PLUS" : "SHIFT_LEADER_1_6",
                EmploymentModel = "FIX-M"
            };
        }

        // 3) Funktion-basierter Fallback für 1./2. Assistent — diese Rollen
        //    haben in easy@work keine eigene Group membership, sind aber
        //    klar FIX-M (Management).
        var funkJobGroup = MapImportedJobGroup(funktion);
        if (funkJobGroup == "ASST_1" || funkJobGroup == "ASST_2" || funkJobGroup == "REST_MANAGER")
        {
            return new Classification
            {
                JobGroupCode = funkJobGroup,
                EmploymentModel = "FIX-M"
            };
        }

        // 4) Employee / leer / unbekannt → CREW + Pay-frequency × Contract-type
        var ct = (contractType ?? "").Trim().ToLowerInvariant();
        var pf = (payFrequency ?? "").Trim().ToLowerInvariant();

        // Pay frequency-Default ableiten: Contract type=Fix → month; sonst hour
        if (string.IsNullOrWhiteSpace(pf))
            pf = ct.Contains("fix") || ct.Contains("full") ? "month" : "hour";

        string model;
        if (pf.StartsWith("month") || pf.StartsWith("monat"))
            model = "FIX";
        else if (ct.Contains("mtp") || ct.Contains("tpm"))
            model = "MTP";
        else
            // hour + Flex / leer / unbekannt → UTP (Default)
            model = "FLEX";

        // CREW kann auch HOST_CT (Crew Trainer / Hostess) sein — aus Funktion
        // ableiten falls dort etwas spezifischeres steht; sonst CREW.
        var jobGroup = (funkJobGroup == "HOST_CT" || funkJobGroup == "SWING")
            ? funkJobGroup
            : "CREW";

        return new Classification
        {
            JobGroupCode = jobGroup,
            EmploymentModel = model
        };
    }

    // Anrede aus CSV oder — falls leer — aus Geschlecht ableiten
    // (Walter: easy@work liefert die Anrede oft gar nicht).
    private static string? NormalizeSalutation(string? raw, string? gender)
    {
        var v = (raw ?? "").Trim();
        if (!string.IsNullOrEmpty(v))
            return v;
        return gender switch
        {
            "female" => "Frau",
            "male"   => "Herr",
            "divers" => null,
            _        => null
        };
    }

    // Default-Wochenstunden für UTP wenn die CSV keine Anzahl liefert.
    // 17h ist der Walter-Default (siehe CLAUDE.md / FAK Mindesteinkommen).
    private static decimal? ApplyWeeklyHoursDefault(decimal? parsed, string? employmentModel)
    {
        if (parsed.HasValue) return parsed;
        var m = (employmentModel ?? "").ToUpperInvariant();
        if (m == "FLEX") return 17m;
        return null;
    }

    /// <summary>
    /// Mappt easy@work-Funktionsbezeichnung auf unseren JobGroup-Code.
    /// Wird seit dem Group-memberships-Refactor nur noch als Fallback für
    /// 1./2. Assistent + spezialisierte Crew-Rollen (HOST_CT/SWING) genutzt —
    /// die Hauptklassifizierung läuft über ResolveClassification().
    ///
    /// Empfehlung an Walter: in easy@work zwei separate Funktionen anlegen:
    ///   "Shift Coordinator 1-6 Mt." → SHIFT_LEADER_1_6 (Mindestlohn niedriger)
    ///   "Shift Coordinator 7+ Mt."  → SHIFT_LEADER_7_PLUS (Mindestlohn höher)
    /// </summary>
    private static string MapImportedJobGroup(string? rawFunction)
    {
        if (string.IsNullOrWhiteSpace(rawFunction))
            return "CREW";

        var value = rawFunction.Trim().ToLowerInvariant();

        if (value.Contains("restaurant manager") || value.Contains("store manager"))
            return "REST_MANAGER";

        if (value.Contains("1. assistent") || value.Contains("1.assistent") ||
            value.Contains("first assistant") || value.Contains("erster assistent"))
            return "ASST_1";

        if (value.Contains("2. assistent") || value.Contains("2.assistent") ||
            value.Contains("second assistant") || value.Contains("zweiter assistent"))
            return "ASST_2";

        if (value.Contains("supervisor"))
            return "SWING";

        if (value.Contains("trainer") || value.Contains("host"))
            return "HOST_CT";

        if (value.Contains("swing"))
            return "SWING";

        // Shift Leader / Coordinator: zuerst auf "7+", "ab 6", "ab 7" prüfen
        // (höhere Erfahrungsstufe), dann Default auf 1-6.
        if (value.Contains("schicht") || value.Contains("shift"))
        {
            var has7Plus =
                value.Contains("7+")           || value.Contains("7 +") ||
                value.Contains("ab 6")         || value.Contains("ab 7") ||
                value.Contains("ab 6 mt")      || value.Contains("ab 6 monat") ||
                value.Contains("ab 7 mt")      || value.Contains("ab 7 monat") ||
                value.Contains("7+ mt")        || value.Contains("7 mt+") ||
                value.Contains("7+ monat")     || value.Contains("7 monate +") ||
                value.Contains("> 6")          || value.Contains(">6")  ||
                value.Contains("erfahren")     || value.Contains("senior");
            return has7Plus ? "SHIFT_LEADER_7_PLUS" : "SHIFT_LEADER_1_6";
        }

        if (value.Contains("crew"))
            return "CREW";

        return "CREW";
    }

    /// <summary>Vertragstyp: befristet oder unbefristet</summary>
    private static string? MapContractType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "unbefristet";
        var v = value.Trim().ToLowerInvariant();
        if (v.Contains("befrist") && !v.Contains("un")) return "befristet";
        // Fix, MTP, Flex, UTP etc. → unbefristet
        return "unbefristet";
    }

    private static bool IsHourlyModel(string? model)
    {
        var m = (model ?? "").ToUpperInvariant();
        return m == "FLEX" || m == "MTP";
    }

    private static decimal? ParseWeeklyHours(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = Regex.Match(value.Trim(), @"^(\d+(?:[.,]\d+)?)");
        if (match.Success)
        {
            var numStr = match.Groups[1].Value.Replace(",", ".");
            if (decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var h))
                return h;
        }

        return null;
    }

    /// <summary>Anzahl-Spalte: nur Stunden zurückgeben (wenn kein % vorhanden)</summary>
    private static decimal? ParseAnzahlHours(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        // Wenn % enthalten → ist Pensum, nicht Stunden
        if (value.Contains('%')) return null;
        return ParseWeeklyHours(value);
    }

    /// <summary>Anzahl-Spalte: Prozent zurückgeben wenn Wert % enthält (z.B. "80%")</summary>
    private static decimal? ParseAnzahlPercent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!value.Contains('%')) return null;
        var match = Regex.Match(value.Trim(), @"(\d+(?:[.,]\d+)?)");
        if (match.Success)
        {
            var numStr = match.Groups[1].Value.Replace(",", ".");
            if (decimal.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                return p;
        }
        return null;
    }

    private static string NormalizeRestaurantPrefix(string? restaurantCode)
    {
        var digits = Regex.Replace(restaurantCode ?? "", @"\D", "");
        digits = digits.TrimStart('0');
        return string.IsNullOrWhiteSpace(digits) ? "" : digits;
    }

    private static string NormalizeStoreNumber(string? storeNumber)
    {
        var digits = Regex.Replace(storeNumber ?? "", @"\D", "");
        digits = digits.TrimStart('0');
        return string.IsNullOrWhiteSpace(digits) ? "" : digits;
    }

    private static string NormalizeEmployeeNumber(string? employeeNumber)
    {
        return Regex.Replace(employeeNumber ?? "", @"\s", "");
    }

    private class ImportEmployeeRow
    {
        public string EmployeeNumber { get; set; } = "";
        public string? Salutation { get; set; }
        public string? Gender { get; set; }        // NEU
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? ShortName { get; set; }
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? ZipCode { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Nationality { get; set; }
        // EntryDate = Firmen-Eintritt aus «Datum der Betriebszugehörigkeit».
        // ContractStartDate = «Pay rate from» (Lohn-Beginn pro Vertrag).
        public DateTime? EntryDate { get; set; }
        public DateTime? ContractStartDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public DateTime? ContractEndDate { get; set; }
        public string? MaritalStatus { get; set; }
        public string? PermitTypeRaw { get; set; }
        public DateTime? PermitExpiryDate { get; set; }

        public string? JobGroupCodeSuggestion { get; set; }
        public string? JobTitleSuggestion { get; set; }
        public string? EmploymentModelSuggestion { get; set; }
        public bool IsPayrollExcluded { get; set; }
        public string? ContractTypeSuggestion { get; set; }
        public decimal? HourlyRateSuggestion { get; set; }
        public decimal? MonthlySalaryFteSuggestion { get; set; }   // 100%-Lohn aus CSV
        public decimal? EmploymentPercentageSuggestion { get; set; } // Pensum %
        // Tatsächlicher Lohn = FTE × Pensum (wird in SaveSnapshotAsync berechnet)
        public decimal? MonthlySalarySuggestion
        {
            get
            {
                var pct = EmploymentPercentageSuggestion ?? EmploymentPercentageFromAnzahl;
                if (MonthlySalaryFteSuggestion.HasValue && pct.HasValue)
                    return Math.Round(MonthlySalaryFteSuggestion.Value * pct.Value / 100m, 2);
                return MonthlySalaryFteSuggestion; // Fallback: FTE direkt wenn kein Pensum
            }
        }
        public decimal? WeeklyHoursSuggestion { get; set; }
        /// <summary>Pensum % aus Anzahl-Spalte wenn Wert "80%" etc. enthält</summary>
        public decimal? EmploymentPercentageFromAnzahl { get; set; }
    }
}