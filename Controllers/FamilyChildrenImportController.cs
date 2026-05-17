using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.HSSF.UserModel;   // .xls (HSSF)
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;   // .xlsx (XSSF)

namespace HrSystem.Controllers;

/// <summary>
/// Import der Mirus-Familienzulagen-Kontroll-Datei (.xls aus JasperReports).
///
/// Struktur:
///   Zeile 5: Header („Kinder", „Geburtsdatum", „Zulage 1", „Zulage 2", „Zulage 3")
///   Datenzeilen ab Zeile 7:
///     • Hauptzeile: Col 2 = "1040001 Aerni Hussein Alaa" (Personalnr + MA-Name)
///     • Sub-Zeilen: Col 2 = Kindname (Mirus-Format: „Nachname Vorname"),
///                   Col 5 = Geburtsdatum,
///                   Col 8/9   = Zulage 1 (Kinderzulage Bis, typisch 16. Geburtstag),
///                   Col 11/12 = Zulage 2 (selten, leerer Slot),
///                   Col 14/15 = Zulage 3 (Ausbildungszulage Bis).
///
/// Match: MA per Personalnummer. Kind per Vorname + Nachname + Geburtsdatum
/// (Anti-Duplikat — beim Re-Import werden bestehende Kinder NICHT verdoppelt).
///
/// Output:
///   • EmployeeFamilyMember pro Kind (MemberType="Kind", FirstName, LastName,
///     DateOfBirth)
///   • FamilyMemberAllowance pro Kind: ValidFrom=heute (oder Geburtsdatum
///     wenn jünger), ValidTo=Bis-Datum aus Datei, AllowanceType="KZ" (wenn
///     Zulage 1/2 gefüllt) oder "AZ" (wenn Zulage 3 gefüllt), MonthlyAmount
///     aus FamilienzulagenTarif des Filial-Kantons.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/family-children")]
public class FamilyChildrenImportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ILogger<FamilyChildrenImportController> _log;

    public FamilyChildrenImportController(AppDbContext db, ILogger<FamilyChildrenImportController> log)
    {
        _db = db;
        _log = log;
    }

    public class ChildPreviewRow
    {
        public int RowNum { get; set; }
        public string EmployeeNumber { get; set; } = "";
        public string EmployeeName    { get; set; } = "";
        public string ChildLastName   { get; set; } = "";
        public string ChildFirstName  { get; set; } = "";
        public DateOnly? DateOfBirth  { get; set; }
        public DateOnly? Z1Until      { get; set; }
        public DateOnly? Z2Until      { get; set; }
        public DateOnly? Z3Until      { get; set; }
        // Resultierender Eintrag
        public string? AllowanceType  { get; set; }   // KZ | AZ
        public DateOnly? ValidTo      { get; set; }
        public decimal?  MonthlyAmount { get; set; }
        // Match-Resultat
        public int?    EmployeeId      { get; set; }
        public int?    ExistingChildId { get; set; }
        public string  Status          { get; set; } = "OK"; // OK | NO_EMPLOYEE | NO_DATE | DUPLICATE | NO_TARIF
        public string? Note            { get; set; }
    }

    public class PreviewResponse
    {
        public List<ChildPreviewRow> Rows { get; set; } = new();
        public int TotalRows { get; set; }
        public int Insertable { get; set; }
        public int Duplicates { get; set; }
        public int Skipped    { get; set; }
    }

    [HttpPost("preview")]
    public async Task<IActionResult> Preview([FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        var rows = await ParseAsync(file);
        if (rows == null) return BadRequest(new { error = "Konnte Datei nicht parsen — erwartet Mirus-Familienzulagen-Kontrolle (.xls)." });

        // Alle MA + bestehende Kinder vorladen
        var allEmps = await _db.Employees
            .AsNoTracking()
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
            .ToListAsync();
        var existingChildren = await _db.EmployeeFamilyMembers
            .AsNoTracking()
            .Where(f => f.MemberType == "Kind")
            .Select(f => new {
                f.Id, f.EmployeeId, f.FirstName, f.LastName, f.DateOfBirth
            })
            .ToListAsync();

        // Filial-Kanton-Code pro Employee bestimmen (wegen Tarif-Lookup) —
        // nur den AKTIVEN Vertrag berücksichtigen.
        // Aktive MA → Kanton-Code der Filiale. Wenn KantonCode null ist,
        // landet der Eintrag mit leerem String im Dictionary — damit das
        // Frontend den Unterschied „kein Vertrag" vs. „Filiale ohne Kanton"
        // erkennt.
        var empToKanton = await (from em in _db.Employments
                                 join cp in _db.CompanyProfiles on em.CompanyProfileId equals cp.Id
                                 where em.IsActive
                                 group cp.KantonCode by em.EmployeeId into g
                                 select new { EmployeeId = g.Key, KantonCode = g.First() })
                                .ToDictionaryAsync(x => x.EmployeeId, x => x.KantonCode ?? "");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var tarife = await _db.FamilienzulagenTarife
            .Where(t => t.ValidFrom <= today && (t.ValidTo == null || t.ValidTo >= today))
            .ToListAsync();

        foreach (var r in rows)
        {
            // 1. MA-Match
            var emp = allEmps.FirstOrDefault(e => e.EmployeeNumber == r.EmployeeNumber);
            if (emp == null)
            {
                r.Status = "NO_EMPLOYEE";
                r.Note   = $"Personalnummer {r.EmployeeNumber} nicht in DB.";
                continue;
            }
            r.EmployeeId = emp.Id;

            // 2. Kind-Match (Anti-Duplikat)
            var existing = existingChildren.FirstOrDefault(c =>
                c.EmployeeId == emp.Id
             && c.DateOfBirth.HasValue && r.DateOfBirth.HasValue
             && DateOnly.FromDateTime(c.DateOfBirth.Value) == r.DateOfBirth.Value
             && string.Equals(c.FirstName, r.ChildFirstName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                r.ExistingChildId = existing.Id;
                r.Status = "DUPLICATE";
                r.Note   = "Kind bereits in DB — wird übersprungen.";
                continue;
            }

            // 3. Zulagen-Typ + Bis-Datum bestimmen
            // Zulage 1/2 = Kinderzulage, Zulage 3 = Ausbildungszulage. Mirus
            // füllt nur eine Spalte pro Kind je nach aktueller Stufe.
            DateOnly? validTo = r.Z1Until ?? r.Z2Until ?? r.Z3Until;
            string? allowanceType = (r.Z1Until.HasValue || r.Z2Until.HasValue) ? "KZ"
                                  : r.Z3Until.HasValue ? "AZ"
                                  : null;
            if (validTo == null || allowanceType == null)
            {
                r.Status = "NO_DATE";
                r.Note   = "Kein Zulage-Bis-Datum in der Datei — Kind wird ohne Zulage angelegt.";
                // Wir importieren das Kind trotzdem (Stammdaten), aber ohne Allowance.
                continue;
            }
            r.ValidTo = validTo;
            r.AllowanceType = allowanceType;

            // 4. Tarif-Lookup für MonthlyAmount — differenzierte Diagnose
            if (!empToKanton.TryGetValue(emp.Id, out var kanton))
            {
                r.Status = "NO_TARIF";
                r.Note   = "Keinen aktiven Vertrag (oder Vertrag ohne Filiale) gefunden — bitte Vertragsanlage prüfen.";
                continue;
            }
            if (string.IsNullOrWhiteSpace(kanton))
            {
                r.Status = "NO_TARIF";
                r.Note   = "Filiale hat keinen Kanton-Code in den Stammdaten — bitte in Systemeinstellungen → Filiale eintragen.";
                continue;
            }
            var tarif = tarife.FirstOrDefault(t => t.KantonCode == kanton);
            if (tarif == null)
            {
                r.Status = "NO_TARIF";
                r.Note   = $"Kein gültiger FAK-Tarif für Kanton {kanton} per heute.";
                continue;
            }
            decimal? amount = null;
            if (allowanceType == "KZ")
            {
                // Bei Stufung: aktueller Satz richtet sich nach Alter heute.
                int? ageNow = r.DateOfBirth.HasValue ? CalcAge(r.DateOfBirth.Value, today) : null;
                if (tarif.KinderzulageSatz2AbAlter.HasValue
                 && ageNow.HasValue && ageNow.Value >= tarif.KinderzulageSatz2AbAlter.Value
                 && tarif.KinderzulageSatz2.HasValue)
                {
                    amount = tarif.KinderzulageSatz2;
                }
                else
                {
                    amount = tarif.KinderzulageSatz1;
                }
            }
            else
            {
                amount = tarif.AusbildungszulageSatz1;
            }
            if (amount == null || amount.Value <= 0)
            {
                r.Status = "NO_TARIF";
                r.Note   = $"Tarif-Satz {(allowanceType == "KZ" ? "Kinderzulage" : "Ausbildungszulage")} für Kanton {kanton} ist nicht gepflegt (NULL/0).";
                continue;
            }
            r.MonthlyAmount = amount;
        }

        return Ok(new PreviewResponse
        {
            Rows       = rows,
            TotalRows  = rows.Count,
            Insertable = rows.Count(r => r.Status == "OK"),
            Duplicates = rows.Count(r => r.Status == "DUPLICATE"),
            Skipped    = rows.Count(r => r.Status != "OK" && r.Status != "DUPLICATE")
        });
    }

    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromForm] IFormFile file, [FromForm] string? validFrom = null)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        // Beginn-Datum für die Allowances. Pflicht — die Datei selbst hat
        // kein Beginn-Datum, das gibt der User vor.
        if (string.IsNullOrWhiteSpace(validFrom)
         || !DateOnly.TryParse(validFrom, out var validFromDate))
        {
            return BadRequest(new { error = "Beginn-Datum (validFrom) erforderlich (Format YYYY-MM-DD)." });
        }

        // Wir nutzen die Preview-Logik wieder, um die rows zu klassifizieren.
        var rows = await ParseAsync(file);
        if (rows == null) return BadRequest(new { error = "Konnte Datei nicht parsen." });

        var allEmps = await _db.Employees
            .Select(e => new { e.Id, e.EmployeeNumber })
            .ToListAsync();
        var existingChildren = await _db.EmployeeFamilyMembers
            .Where(f => f.MemberType == "Kind")
            .ToListAsync();
        // Aktive MA → Kanton-Code der Filiale. Wenn KantonCode null ist,
        // landet der Eintrag mit leerem String im Dictionary — damit das
        // Frontend den Unterschied „kein Vertrag" vs. „Filiale ohne Kanton"
        // erkennt.
        var empToKanton = await (from em in _db.Employments
                                 join cp in _db.CompanyProfiles on em.CompanyProfileId equals cp.Id
                                 where em.IsActive
                                 group cp.KantonCode by em.EmployeeId into g
                                 select new { EmployeeId = g.Key, KantonCode = g.First() })
                                .ToDictionaryAsync(x => x.EmployeeId, x => x.KantonCode ?? "");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var tarife = await _db.FamilienzulagenTarife
            .Where(t => t.ValidFrom <= today && (t.ValidTo == null || t.ValidTo >= today))
            .ToListAsync();

        int childrenAdded = 0, allowancesAdded = 0, skipped = 0, duplicates = 0;

        foreach (var r in rows)
        {
            var emp = allEmps.FirstOrDefault(e => e.EmployeeNumber == r.EmployeeNumber);
            if (emp == null) { skipped++; continue; }

            // Anti-Duplikat
            var existing = existingChildren.FirstOrDefault(c =>
                c.EmployeeId == emp.Id
             && c.DateOfBirth.HasValue && r.DateOfBirth.HasValue
             && DateOnly.FromDateTime(c.DateOfBirth.Value) == r.DateOfBirth.Value
             && string.Equals(c.FirstName, r.ChildFirstName, StringComparison.OrdinalIgnoreCase));
            if (existing != null) { duplicates++; continue; }

            // Kind-Stammdaten anlegen
            var child = new EmployeeFamilyMember
            {
                EmployeeId  = emp.Id,
                MemberType  = "Kind",
                FirstName   = r.ChildFirstName,
                LastName    = r.ChildLastName,
                DateOfBirth = r.DateOfBirth?.ToDateTime(TimeOnly.MinValue),
                CreatedAt   = DateTime.UtcNow,
                UpdatedAt   = DateTime.UtcNow
            };
            _db.EmployeeFamilyMembers.Add(child);
            await _db.SaveChangesAsync();   // Id für Allowance-FK
            childrenAdded++;

            // Allowance anlegen, falls Datum + Tarif vorhanden
            DateOnly? validTo = r.Z1Until ?? r.Z2Until ?? r.Z3Until;
            string? allowanceType = (r.Z1Until.HasValue || r.Z2Until.HasValue) ? "KZ"
                                  : r.Z3Until.HasValue ? "AZ"
                                  : null;
            if (validTo == null || allowanceType == null) continue;

            if (!empToKanton.TryGetValue(emp.Id, out var kanton) || string.IsNullOrWhiteSpace(kanton)) continue;
            var tarif = tarife.FirstOrDefault(t => t.KantonCode == kanton);
            if (tarif == null) continue;

            decimal? amount = null;
            if (allowanceType == "KZ")
            {
                int? ageNow = r.DateOfBirth.HasValue ? CalcAge(r.DateOfBirth.Value, today) : null;
                if (tarif.KinderzulageSatz2AbAlter.HasValue
                 && ageNow.HasValue && ageNow.Value >= tarif.KinderzulageSatz2AbAlter.Value
                 && tarif.KinderzulageSatz2.HasValue)
                    amount = tarif.KinderzulageSatz2;
                else
                    amount = tarif.KinderzulageSatz1;
            }
            else
            {
                amount = tarif.AusbildungszulageSatz1;
            }
            if (amount == null || amount.Value <= 0) continue;

            // Beginn-Datum nicht VOR den Geburtstag des Kindes setzen — sonst
            // rechnet die Lohnberechnung rückwirkend Zulagen für Zeiten, in
            // denen das Kind noch nicht existiert hat. effectiveFrom = max(form-validFrom,
            // Geburtsdatum).
            var effectiveFrom = validFromDate;
            if (r.DateOfBirth.HasValue && r.DateOfBirth.Value > effectiveFrom)
                effectiveFrom = r.DateOfBirth.Value;

            _db.FamilyMemberAllowances.Add(new FamilyMemberAllowance
            {
                FamilyMemberId = child.Id,
                ValidFrom      = effectiveFrom,
                ValidTo        = validTo,
                MonthlyAmount  = amount.Value,
                AllowanceType  = allowanceType,
                Note           = "Mirus-Familienzulagen-Kontrolle Import",
                CreatedAt      = DateTime.UtcNow,
                UpdatedAt      = DateTime.UtcNow
            });
            allowancesAdded++;
        }

        await _db.SaveChangesAsync();

        return Ok(new {
            childrenAdded, allowancesAdded, skipped, duplicates
        });
    }

    // ──────────────────────────────────────────────────────────────────────
    // PARSING
    // ──────────────────────────────────────────────────────────────────────

    private async Task<List<ChildPreviewRow>?> ParseAsync(IFormFile file)
    {
        try
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            // Auto-detect HSSF (.xls) vs XSSF (.xlsx)
            IWorkbook wb;
            try { wb = new HSSFWorkbook(stream); }
            catch { stream.Position = 0; wb = new XSSFWorkbook(stream); }

            var sh = wb.GetSheetAt(0);
            if (sh == null) return null;

            var rows = new List<ChildPreviewRow>();
            string? currentEmpNr = null;
            string? currentEmpName = null;
            var empHeaderRegex = new Regex(@"^(\d{6,})\s+(.+)$");

            for (int r = 0; r <= sh.LastRowNum; r++)
            {
                var row = sh.GetRow(r);
                if (row == null) continue;
                var col2 = ExcelCellToString(row.GetCell(2)).Trim();
                if (string.IsNullOrEmpty(col2)) continue;

                // Header-Zeile? "1040001 Aerni Hussein Alaa"
                var m = empHeaderRegex.Match(col2);
                if (m.Success)
                {
                    currentEmpNr   = m.Groups[1].Value;
                    currentEmpName = m.Groups[2].Value;
                    continue;
                }

                if (currentEmpNr == null) continue;   // vor erstem MA: skip

                // Kind-Zeile — Geburtsdatum aus Col 5, Bis-Daten aus Col 8/9, 11/12, 14/15
                var geb = ReadDate(row, 5);
                if (geb == null) continue;   // ohne Geb-Datum kein Match möglich

                var z1 = ReadDate(row, 8) ?? ReadDate(row, 9);
                var z2 = ReadDate(row, 11) ?? ReadDate(row, 12);
                var z3 = ReadDate(row, 14) ?? ReadDate(row, 15);

                // Mirus-Format „Nachname Vorname" — erstes Wort = Nachname,
                // Rest = Vorname(n). Bei Doppel-Nachnamen kann Walter im UI
                // nachbessern.
                var parts = col2.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var lastName  = parts.Length > 0 ? parts[0] : "";
                var firstName = parts.Length > 1 ? parts[1] : "";

                rows.Add(new ChildPreviewRow
                {
                    RowNum         = r + 1,
                    EmployeeNumber = currentEmpNr,
                    EmployeeName   = currentEmpName ?? "",
                    ChildLastName  = lastName,
                    ChildFirstName = firstName,
                    DateOfBirth    = geb,
                    Z1Until        = z1,
                    Z2Until        = z2,
                    Z3Until        = z3
                });
            }
            return rows;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[FamilyChildrenImport] Parse-Fehler");
            return null;
        }
    }

    private static DateOnly? ReadDate(IRow row, int col)
    {
        var cell = row.GetCell(col);
        if (cell == null) return null;
        if (cell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(cell))
            return DateOnly.FromDateTime(cell.DateCellValue ?? DateTime.MinValue);
        if (cell.CellType == CellType.Numeric)
        {
            // Manche Mirus-Exports markieren das Datum nicht als „Date" —
            // wir konvertieren die Excel-Zahl trotzdem.
            try
            {
                var dt = DateUtil.GetJavaDate(cell.NumericCellValue);
                if (dt.Year > 1900 && dt.Year < 2100)
                    return DateOnly.FromDateTime(dt);
            }
            catch { }
            return null;
        }
        var s = ExcelCellToString(cell);
        if (DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
            return DateOnly.FromDateTime(d);
        return null;
    }

    private static string ExcelCellToString(ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            CellType.Numeric  => cell.NumericCellValue.ToString("0.################"),
            CellType.String   => cell.StringCellValue ?? "",
            CellType.Boolean  => cell.BooleanCellValue ? "TRUE" : "FALSE",
            CellType.Formula  => cell.ToString() ?? "",
            _                  => cell.ToString() ?? ""
        };
    }

    private static int CalcAge(DateOnly birth, DateOnly today)
    {
        int age = today.Year - birth.Year;
        if (today < new DateOnly(today.Year, birth.Month, birth.Day)) age--;
        return age;
    }
}
