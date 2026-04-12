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

                rows.Add(new ImportEmployeeRow
                {
                    EmployeeNumber = employeeNumber,
                    Salutation = FirstNonEmpty(
                        GetValue(fields, headerMap, "Anrede")
                    ),
                    // NEU: Gender aus CSV einlesen
                    Gender = MapGender(FirstNonEmpty(
                        GetValue(fields, headerMap, "Geschlecht"),
                        GetValue(fields, headerMap, "Gender")
                    )),
                    FirstName = FirstNonEmpty(
                        GetValue(fields, headerMap, "Vorname"),
                        GetValue(fields, headerMap, "First name")
                    ),
                    LastName = FirstNonEmpty(
                        GetValue(fields, headerMap, "Nachname"),
                        GetValue(fields, headerMap, "Name"),
                        GetValue(fields, headerMap, "Last name")
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
                    EntryDate = ParseDate(FirstNonEmpty(
                        GetValue(fields, headerMap, "Datum der Betriebszugehörigkeit"),
                        GetValue(fields, headerMap, "Eintrittsdatum"),
                        GetValue(fields, headerMap, "Von")
                    )),
                    ExitDate = ParseDate(FirstNonEmpty(
                        GetValue(fields, headerMap, "Austrittsdatum"),
                        GetValue(fields, headerMap, "Bis")
                    )),
                    PermitTypeRaw = FirstNonEmpty(
                        GetValue(fields, headerMap, "VISA_PERMIT_TYPE"),
                        GetValue(fields, headerMap, "UN_VISA_PERMIT_TYPE"),
                        GetValue(fields, headerMap, "Visa/Permit type")
                    ),
                    PermitExpiryDate = ParseDate(FirstNonEmpty(
                        GetValue(fields, headerMap, "EXPIRATN_DT"),
                        GetValue(fields, headerMap, "Visa expiration date")
                    )),
                    JobGroupCodeSuggestion = MapImportedJobGroup(FirstNonEmpty(
                        GetValue(fields, headerMap, "Funktion"),
                        GetValue(fields, headerMap, "Funktionen"),
                        GetValue(fields, headerMap, "Function"),
                        GetValue(fields, headerMap, "Job title")
                    )),
                    JobTitleSuggestion = FirstNonEmpty(
                        GetValue(fields, headerMap, "Funktion"),
                        GetValue(fields, headerMap, "Funktionen"),
                        GetValue(fields, headerMap, "Function"),
                        GetValue(fields, headerMap, "Job title")
                    ),
                    EmploymentModelSuggestion = MapImportedEmploymentModel(
                        FirstNonEmpty(GetValue(fields, headerMap, "Contract type")),
                        FirstNonEmpty(
                            GetValue(fields, headerMap, "Group memberships"),
                            GetValue(fields, headerMap, "Group membership"),
                            GetValue(fields, headerMap, "Member group"))
                    ),
                    ContractTypeSuggestion = FirstNonEmpty(
                        GetValue(fields, headerMap, "Contract type")
                    ),
                    HourlyRateSuggestion = IsHourlyModel(MapImportedEmploymentModel(
                        FirstNonEmpty(GetValue(fields, headerMap, "Contract type")),
                        FirstNonEmpty(
                            GetValue(fields, headerMap, "Group memberships"),
                            GetValue(fields, headerMap, "Group membership"),
                            GetValue(fields, headerMap, "Member group"))))
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
                    MonthlySalaryFteSuggestion = !IsHourlyModel(MapImportedEmploymentModel(
                        FirstNonEmpty(GetValue(fields, headerMap, "Contract type")),
                        FirstNonEmpty(
                            GetValue(fields, headerMap, "Group memberships"),
                            GetValue(fields, headerMap, "Group membership"),
                            GetValue(fields, headerMap, "Member group"))))
                        ? ParseDecimal(FirstNonEmpty(
                            GetValue(fields, headerMap, "Tarife"),
                            GetValue(fields, headerMap, "Salary (FTE)"),
                            GetValue(fields, headerMap, "Monatslohn")))
                        : null,
                    WeeklyHoursSuggestion = ParseWeeklyHours(FirstNonEmpty(
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

        var employeeNumbersInImport = rows
            .Select(r => r.EmployeeNumber)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int inserted = 0;
        int updated = 0;
        int reactivated = 0;
        int deactivated = 0;

        foreach (var row in rows)
        {
            var employee = existingEmployees
                .FirstOrDefault(e => e.EmployeeNumber == row.EmployeeNumber);

            if (employee == null)
            {
                employee = new Employee
                {
                    EmployeeNumber = row.EmployeeNumber,
                    IsActive = true
                };

                _context.Employees.Add(employee);
                existingEmployees.Add(employee);
                inserted++;
            }
            else
            {
                updated++;

                if (!employee.IsActive)
                    reactivated++;

                employee.IsActive = true;
            }

            employee.Salutation = NullIfEmpty(row.Salutation);
            employee.FirstName = NullIfEmpty(row.FirstName) ?? employee.FirstName;
            employee.LastName = NullIfEmpty(row.LastName) ?? employee.LastName;

            var (street, houseNumber) = SplitAddress(row.Address, row.Address2);
            employee.Street = street;
            employee.HouseNumber = houseNumber;

            employee.ZipCode = NullIfEmpty(row.ZipCode);
            employee.City = NullIfEmpty(row.City);
            employee.Country = NullIfEmpty(row.Country);

            employee.DateOfBirth = row.DateOfBirth;
            employee.Email = NullIfEmpty(row.Email);
            employee.PhoneMobile = NullIfEmpty(row.Phone);

            employee.Nationality = NullIfEmpty(row.Nationality);
            employee.NationalityId = ResolveNationalityId(row.Nationality, nationalities);

            employee.EntryDate = row.EntryDate;
            employee.ExitDate = row.ExitDate;

            employee.PermitTypeId = ResolvePermitTypeId(row.PermitTypeRaw, permitTypes);
            employee.PermitExpiryDate = row.PermitExpiryDate;

            // NEU: Gender auf dem Employee speichern
            if (!string.IsNullOrWhiteSpace(row.Gender))
                employee.Gender = row.Gender;

            await SaveSnapshotAsync(employee, row);
        }

        foreach (var employee in existingEmployees.Where(e =>
                     e.IsActive &&
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
            deactivated
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
            "w"         => "female",
            "m"         => "male",
            _           => null
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

    private static (string? street, string? houseNumber) SplitAddress(string? address1, string? address2)
    {
        var line1 = NullIfEmpty(address1);
        var line2 = NullIfEmpty(address2);

        if (string.IsNullOrWhiteSpace(line1))
            return (null, line2);

        var match = Regex.Match(line1, @"^(.*\D)\s+(\d+[A-Za-z\-\/]*)$");
        if (match.Success)
            return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());

        return (line1, line2);
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

        if (value.Contains("schicht") || value.Contains("shift"))
            return "SHIFT_LEADER_1_6";

        if (value.Contains("crew"))
            return "CREW";

        return "CREW";
    }

    private static string? MapImportedEmploymentModel(string? contractType, string? groupMembership)
    {
        var ct = (contractType ?? "").Trim().ToLowerInvariant();

        if (ct.Contains("mtp") || ct.Contains("tpm"))
            return "MTP";

        if (ct.Contains("flex"))
            return "UTP";

        if (ct.Contains("fix") || ct.Contains("full"))
        {
            return IsManagementGroup(groupMembership) ? "FIX-M" : "FIX";
        }

        return "UTP";
    }

    private static bool IsManagementGroup(string? groupMembership)
    {
        var g = (groupMembership ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(g))            return false;
        if (g.Contains("empolye") || g.Contains("employee")) return false;
        return true;
    }

    private static bool IsHourlyModel(string? model)
    {
        var m = (model ?? "").ToUpperInvariant();
        return m == "UTP" || m == "MTP";
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
        public string? Address { get; set; }
        public string? Address2 { get; set; }
        public string? ZipCode { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Nationality { get; set; }
        public DateTime? EntryDate { get; set; }
        public DateTime? ExitDate { get; set; }
        public string? PermitTypeRaw { get; set; }
        public DateTime? PermitExpiryDate { get; set; }

        public string? JobGroupCodeSuggestion { get; set; }
        public string? JobTitleSuggestion { get; set; }
        public string? EmploymentModelSuggestion { get; set; }
        public string? ContractTypeSuggestion { get; set; }
        public decimal? HourlyRateSuggestion { get; set; }
        public decimal? MonthlySalaryFteSuggestion { get; set; }   // 100%-Lohn aus CSV
        public decimal? EmploymentPercentageSuggestion { get; set; } // Pensum %
        // Tatsächlicher Lohn = FTE × Pensum (wird in SaveSnapshotAsync berechnet)
        public decimal? MonthlySalarySuggestion =>
            MonthlySalaryFteSuggestion.HasValue && EmploymentPercentageSuggestion.HasValue
                ? Math.Round(MonthlySalaryFteSuggestion.Value * EmploymentPercentageSuggestion.Value / 100m, 2)
                : MonthlySalaryFteSuggestion;  // Fallback: wenn kein Pensum, FTE direkt verwenden
        public decimal? WeeklyHoursSuggestion { get; set; }
    }
}