using System.Text.RegularExpressions;
using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Controllers;

/// <summary>
/// OneCrew ↔ Mirus Adress-/Kontakt-Vergleich (Walter 29.07.2026).
/// Read-only Auswertung während Doppelspur-Phase. Quelle: Mirus «Adressliste»
/// (XLS/XLSX) mit Spalten Betriebsnummer · Pers. Nr. · Name · Vorname ·
/// Strasse · PLZ · Ort · Telefon 1 · Telefon 2 · E-Mail · E-Mail Kontaktdaten.
/// Match per Personalnummer. PLZ/Ort gegen swiss_location (Ortschaft) geprüft.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/mirus-address-compare")]
public class MirusAddressCompareController : ControllerBase
{
    private readonly AppDbContext _db;
    public MirusAddressCompareController(AppDbContext db) => _db = db;

    public class FieldDiff
    {
        public string Field { get; set; } = "";
        public string? OneCrew { get; set; }
        public string? Mirus { get; set; }
    }

    public class PlzCheck
    {
        public string Source { get; set; } = ""; // «OneCrew» | «Mirus»
        public string? Plz { get; set; }
        public string? Ort { get; set; }
        public string Status { get; set; } = "OK"; // OK | PLZ_UNKNOWN | ORT_MISMATCH | EMPTY
        public string? Message { get; set; }
        public List<string> KnownOrte { get; set; } = new();
    }

    public class CompareRow
    {
        public int RowNum { get; set; }
        public string Status { get; set; } = "OK"; // OK | DIFF | NO_MATCH | ONLY_MIRUS | ONLY_ONECREW
        public string? EmployeeNumber { get; set; }
        public int? EmployeeId { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public bool? IsActive { get; set; }

        public string? MirusStreet { get; set; }
        public string? MirusZip { get; set; }
        public string? MirusCity { get; set; }
        public string? MirusPhone1 { get; set; }
        public string? MirusPhone2 { get; set; }
        public string? MirusEmail { get; set; }
        public string? MirusEmailKontakt { get; set; }

        public string? OcStreet { get; set; }
        public string? OcZip { get; set; }
        public string? OcCity { get; set; }
        public string? OcPhone { get; set; }
        public string? OcPhone2 { get; set; }
        public string? OcEmail { get; set; }

        public List<FieldDiff> Diffs { get; set; } = new();
        public List<PlzCheck> PlzChecks { get; set; } = new();
        public string? Note { get; set; }
    }

    public class CompareResponse
    {
        public int TotalMirus { get; set; }
        public int Matched { get; set; }
        public int Identical { get; set; }
        public int WithDiffs { get; set; }
        public int NoMatch { get; set; }
        public int OnlyOneCrew { get; set; }
        public int PlzIssues { get; set; }
        public List<CompareRow> Rows { get; set; } = new();
    }

    /// <summary>POST analyze — reine Auswertung, schreibt nichts.</summary>
    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze(
        [FromForm] IFormFile file,
        [FromForm] int companyProfileId = 0,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Datei fehlt." });
        if (companyProfileId <= 0)
            return BadRequest(new { error = "Bitte zuerst links eine Filiale wählen." });

        List<MirusRow> mirusRows;
        try
        {
            await using var stream = file.OpenReadStream();
            mirusRows = ParseAdressliste(stream, file.FileName);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Datei konnte nicht gelesen werden: {ex.Message}" });
        }

        if (mirusRows.Count == 0)
            return BadRequest(new { error = "Keine Datenzeilen gefunden. Erwartet: Mirus «Adressliste» mit Spalte Pers. Nr." });

        var ocEmployees = await _db.Employees.AsNoTracking()
            .Where(e => e.Employments.Any(em => em.CompanyProfileId == companyProfileId)
                     || !e.Employments.Any())
            .Select(e => new OcEmp(
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.IsActive,
                e.Street, e.ZipCode, e.City,
                e.PhoneMobile, e.Phone2, e.Email))
            .ToListAsync(ct);

        // Filial-MA bevorzugen; Personaldossiers ohne Vertrag als Fallback-Pool
        var branchIds = await _db.Employments.AsNoTracking()
            .Where(em => em.CompanyProfileId == companyProfileId)
            .Select(em => em.EmployeeId)
            .Distinct()
            .ToListAsync(ct);
        var branchSet = branchIds.ToHashSet();
        var branchEmps = ocEmployees.Where(e => branchSet.Contains(e.Id)).ToList();
        var byNumber = branchEmps
            .Where(e => !string.IsNullOrWhiteSpace(e.EmployeeNumber))
            .GroupBy(e => NormNumber(e.EmployeeNumber!))
            .ToDictionary(g => g.Key, g => g.ToList());

        // PLZ → Ortschaften einmal laden (nur benötigte)
        var allPlz = mirusRows.Select(r => NormPlz(r.Zip))
            .Concat(branchEmps.Select(e => NormPlz(e.ZipCode)))
            .Where(p => p.Length == 4)
            .Distinct()
            .ToList();
        var locByPlz = await _db.SwissLocations.AsNoTracking()
            .Where(l => allPlz.Contains(l.Plz4))
            .Select(l => new { l.Plz4, Ort = l.Ortschaftsname, Gem = l.Gemeindename })
            .ToListAsync(ct);
        var ortMap = locByPlz
            .GroupBy(l => l.Plz4)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.Ort)
                      .Concat(g.Select(x => x.Gem))
                      .Where(n => !string.IsNullOrWhiteSpace(n))
                      .Distinct(StringComparer.OrdinalIgnoreCase)
                      .OrderBy(n => n)
                      .ToList());

        var res = new CompareResponse();
        var matchedIds = new HashSet<int>();
        var rows = new List<CompareRow>();
        int rowNum = 1;

        foreach (var m in mirusRows)
        {
            rowNum++;
            var cr = new CompareRow
            {
                RowNum = rowNum,
                EmployeeNumber = m.Number,
                FirstName = m.FirstName,
                LastName = m.LastName,
                MirusStreet = m.Street,
                MirusZip = m.Zip,
                MirusCity = m.City,
                MirusPhone1 = m.Phone1,
                MirusPhone2 = m.Phone2,
                MirusEmail = m.Email,
                MirusEmailKontakt = m.EmailKontakt,
            };

            var key = NormNumber(m.Number);
            OcEmp? oc = null;
            if (key.Length > 0 && byNumber.TryGetValue(key, out var cands))
            {
                if (cands.Count == 1) oc = cands[0];
                else
                {
                    // Bei Nummern-Kollision Namen als Tie-Break
                    var byName = cands.Where(c =>
                        string.Equals(NormName(c.FirstName), NormName(m.FirstName), StringComparison.OrdinalIgnoreCase)
                     && string.Equals(NormName(c.LastName), NormName(m.LastName), StringComparison.OrdinalIgnoreCase)).ToList();
                    oc = byName.Count == 1 ? byName[0] : cands.OrderByDescending(c => c.IsActive).First();
                    if (byName.Count != 1)
                        cr.Note = $"Mehrere OneCrew-MA mit Nr. {m.Number} — aktivster gewählt.";
                }
            }

            if (oc == null)
            {
                cr.Status = "NO_MATCH";
                cr.PlzChecks.Add(CheckPlzOrt("Mirus", m.Zip, m.City, ortMap));
                rows.Add(cr);
                continue;
            }

            matchedIds.Add(oc.Id);
            cr.EmployeeId = oc.Id;
            cr.FirstName = oc.FirstName;
            cr.LastName = oc.LastName;
            cr.IsActive = oc.IsActive;
            cr.OcStreet = oc.Street;
            cr.OcZip = oc.ZipCode;
            cr.OcCity = oc.City;
            cr.OcPhone = oc.PhoneMobile;
            cr.OcPhone2 = oc.Phone2;
            cr.OcEmail = oc.Email;

            AddDiff(cr, "Strasse", oc.Street, m.Street, compareStreet: true);
            AddDiff(cr, "PLZ", oc.ZipCode, m.Zip, comparePlz: true);
            AddDiff(cr, "Ort", oc.City, m.City);
            AddDiff(cr, "Telefon", oc.PhoneMobile, m.Phone1, comparePhone: true);
            // Telefon 2: Mirus vs OneCrew Phone2 (wenn Mirus leer und OC leer → kein Diff)
            if (!IsBlank(m.Phone2) || !IsBlank(oc.Phone2))
                AddDiff(cr, "Telefon 2", oc.Phone2, m.Phone2, comparePhone: true);
            AddDiff(cr, "E-Mail", oc.Email, m.Email, compareEmail: true);
            // E-Mail Kontaktdaten: nur Hinweis wenn gesetzt und ≠ Haupt-E-Mail
            if (!IsBlank(m.EmailKontakt)
                && !EqEmail(m.EmailKontakt, m.Email)
                && !EqEmail(m.EmailKontakt, oc.Email))
            {
                cr.Diffs.Add(new FieldDiff
                {
                    Field = "E-Mail Kontakt (nur Mirus)",
                    OneCrew = oc.Email,
                    Mirus = m.EmailKontakt
                });
            }

            cr.PlzChecks.Add(CheckPlzOrt("OneCrew", oc.ZipCode, oc.City, ortMap));
            cr.PlzChecks.Add(CheckPlzOrt("Mirus", m.Zip, m.City, ortMap));

            cr.Status = cr.Diffs.Count > 0 ? "DIFF" : "OK";
            rows.Add(cr);
        }

        // OneCrew-Filial-MA ohne Mirus-Zeile
        foreach (var oc in branchEmps.Where(e => e.IsActive && !matchedIds.Contains(e.Id))
                                     .OrderBy(e => e.FirstName).ThenBy(e => e.LastName))
        {
            var cr = new CompareRow
            {
                Status = "ONLY_ONECREW",
                EmployeeId = oc.Id,
                EmployeeNumber = oc.EmployeeNumber,
                FirstName = oc.FirstName,
                LastName = oc.LastName,
                IsActive = oc.IsActive,
                OcStreet = oc.Street,
                OcZip = oc.ZipCode,
                OcCity = oc.City,
                OcPhone = oc.PhoneMobile,
                OcPhone2 = oc.Phone2,
                OcEmail = oc.Email,
                Note = "In OneCrew aktiv, fehlt in Mirus-Adressliste.",
            };
            cr.PlzChecks.Add(CheckPlzOrt("OneCrew", oc.ZipCode, oc.City, ortMap));
            rows.Add(cr);
        }

        // Sort: Diffs zuerst, dann NO_MATCH, ONLY_ONECREW, OK — innerhalb Vorname
        int Rank(string s) => s switch
        {
            "DIFF" => 0,
            "NO_MATCH" => 1,
            "ONLY_ONECREW" => 2,
            _ => 3
        };
        rows = rows
            .OrderBy(r => Rank(r.Status))
            .ThenBy(r => r.FirstName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LastName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

        res.Rows = rows;
        res.TotalMirus = mirusRows.Count;
        res.Matched = rows.Count(r => r.Status is "OK" or "DIFF");
        res.Identical = rows.Count(r => r.Status == "OK");
        res.WithDiffs = rows.Count(r => r.Status == "DIFF");
        res.NoMatch = rows.Count(r => r.Status == "NO_MATCH");
        res.OnlyOneCrew = rows.Count(r => r.Status == "ONLY_ONECREW");
        res.PlzIssues = rows.Count(r => r.PlzChecks.Any(p => p.Status is "PLZ_UNKNOWN" or "ORT_MISMATCH"));

        return Ok(res);
    }

    // ─────────────────────────── Parsing ───────────────────────────

    private sealed record MirusRow(
        string Number, string LastName, string FirstName,
        string? Street, string? Zip, string? City,
        string? Phone1, string? Phone2, string? Email, string? EmailKontakt);

    private sealed record OcEmp(
        int Id, string? EmployeeNumber, string FirstName, string LastName, bool IsActive,
        string? Street, string? ZipCode, string? City,
        string? PhoneMobile, string? Phone2, string? Email);

    private static List<MirusRow> ParseAdressliste(Stream stream, string fileName)
    {
        IWorkbook wb = fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            ? new XSSFWorkbook(stream)
            : new HSSFWorkbook(stream);
        var sheet = wb.GetSheetAt(0)
            ?? throw new InvalidOperationException("Leeres Arbeitsblatt.");

        // Header-Zeile finden (enthält «Pers» + «PLZ» o.ä.)
        int headerRow = -1;
        var col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int r = 0; r <= Math.Min(sheet.LastRowNum, 10); r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < row.LastCellNum; c++)
            {
                var h = CellStr(row.GetCell(c));
                if (!string.IsNullOrWhiteSpace(h)) map[h.Trim()] = c;
            }
            bool hasPers = map.Keys.Any(k => k.Contains("Pers", StringComparison.OrdinalIgnoreCase)
                                          || k.Contains("Personal", StringComparison.OrdinalIgnoreCase));
            bool hasPlz = map.Keys.Any(k => k.Equals("PLZ", StringComparison.OrdinalIgnoreCase));
            if (hasPers && hasPlz)
            {
                headerRow = r;
                col = map;
                break;
            }
        }
        if (headerRow < 0)
            throw new InvalidOperationException("Header-Zeile mit «Pers. Nr.» und «PLZ» nicht gefunden.");

        int Col(params string[] names)
        {
            foreach (var n in names)
            {
                if (col.TryGetValue(n, out var i)) return i;
                var soft = col.FirstOrDefault(kv => kv.Key.Contains(n, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(soft.Key)) return soft.Value;
            }
            return -1;
        }

        int cNr = Col("Pers. Nr.", "Pers Nr", "Personalnummer", "Pers.-Nr.");
        int cName = Col("Name", "Nachname");
        int cVor = Col("Vorname");
        int cStr = Col("Strasse", "Straße", "Adresse");
        int cPlz = Col("PLZ");
        int cOrt = Col("Ort", "Stadt");
        int cTel1 = Col("Telefon 1", "Telefon1", "Tel. 1", "Telefon");
        int cTel2 = Col("Telefon 2", "Telefon2", "Tel. 2");
        int cMail = Col("E-Mail", "Email");
        int cMail2 = Col("E-Mail Kontaktdaten", "E-Mail Kontakt");

        if (cNr < 0) throw new InvalidOperationException("Spalte «Pers. Nr.» fehlt.");

        var list = new List<MirusRow>();
        for (int r = headerRow + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row == null) continue;
            var nr = CellStr(row.GetCell(cNr));
            if (string.IsNullOrWhiteSpace(nr)) continue;
            // Skip Totals / empty name rows
            var last = cName >= 0 ? CellStr(row.GetCell(cName)) : "";
            var first = cVor >= 0 ? CellStr(row.GetCell(cVor)) : "";
            if (string.IsNullOrWhiteSpace(last) && string.IsNullOrWhiteSpace(first)) continue;

            list.Add(new MirusRow(
                nr.Trim(),
                (last ?? "").Trim(),
                (first ?? "").Trim(),
                NullIfBlank(cStr >= 0 ? CellStr(row.GetCell(cStr)) : null),
                NullIfBlank(cPlz >= 0 ? CellStr(row.GetCell(cPlz)) : null),
                NullIfBlank(cOrt >= 0 ? CellStr(row.GetCell(cOrt)) : null),
                NullIfBlank(cTel1 >= 0 ? CellStr(row.GetCell(cTel1)) : null),
                NullIfBlank(cTel2 >= 0 ? CellStr(row.GetCell(cTel2)) : null),
                NullIfBlank(cMail >= 0 ? CellStr(row.GetCell(cMail)) : null),
                NullIfBlank(cMail2 >= 0 ? CellStr(row.GetCell(cMail2)) : null)
            ));
        }
        return list;
    }

    private static string CellStr(ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            CellType.Numeric => cell.NumericCellValue.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture),
            CellType.Boolean => cell.BooleanCellValue ? "true" : "false",
            CellType.Formula => cell.ToString() ?? "",
            _ => cell.ToString() ?? ""
        };
    }

    // ─────────────────────────── Diff / PLZ ───────────────────────────

    private static void AddDiff(CompareRow cr, string field, string? oc, string? mirus,
        bool compareStreet = false, bool comparePlz = false, bool comparePhone = false, bool compareEmail = false)
    {
        if (IsBlank(oc) && IsBlank(mirus)) return;

        bool same;
        if (comparePhone) same = EqPhone(oc, mirus);
        else if (compareEmail) same = EqEmail(oc, mirus);
        else if (comparePlz) same = NormPlz(oc) == NormPlz(mirus) && NormPlz(oc).Length > 0;
        else if (compareStreet) same = EqStreet(oc, mirus);
        else same = EqLoose(oc, mirus);

        if (same) return;
        cr.Diffs.Add(new FieldDiff { Field = field, OneCrew = BlankDash(oc), Mirus = BlankDash(mirus) });
    }

    private static PlzCheck CheckPlzOrt(string source, string? plz, string? ort, Dictionary<string, List<string>> ortMap)
    {
        var p = NormPlz(plz);
        var o = (ort ?? "").Trim();
        if (p.Length == 0 && o.Length == 0)
            return new PlzCheck { Source = source, Plz = plz, Ort = ort, Status = "EMPTY", Message = "PLZ/Ort leer" };
        if (p.Length != 4)
            return new PlzCheck { Source = source, Plz = plz, Ort = ort, Status = "PLZ_UNKNOWN", Message = "PLZ ungültig (erwartet 4 Ziffern)" };

        if (!ortMap.TryGetValue(p, out var orte) || orte.Count == 0)
            return new PlzCheck { Source = source, Plz = p, Ort = o, Status = "PLZ_UNKNOWN", Message = $"PLZ {p} nicht im Ortschaftsverzeichnis" };

        if (o.Length == 0)
            return new PlzCheck { Source = source, Plz = p, Ort = o, Status = "ORT_MISMATCH", Message = "Ort fehlt", KnownOrte = orte };

        var ok = orte.Any(x => CityMatch(x, o));
        if (ok)
            return new PlzCheck { Source = source, Plz = p, Ort = o, Status = "OK", KnownOrte = orte };

        return new PlzCheck
        {
            Source = source,
            Plz = p,
            Ort = o,
            Status = "ORT_MISMATCH",
            Message = $"Ort «{o}» passt nicht zu PLZ {p}. Bekannt: {string.Join(", ", orte)}",
            KnownOrte = orte
        };
    }

    private static bool CityMatch(string known, string given)
    {
        if (string.Equals(known.Trim(), given.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        return NormCity(known) == NormCity(given) && NormCity(given).Length > 0;
    }

    private static string NormCity(string? s)
    {
        var t = (s ?? "").Trim().ToLowerInvariant();
        var i = t.IndexOf('(');
        if (i > 0) t = t[..i].Trim();
        var parts = t.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && parts[^1].Length == 2 && parts[^1] == parts[^1].ToUpperInvariant())
            t = string.Join(' ', parts[..^1]);
        // «Roggwil BE» → nach ToLower ist «be» nicht == ToUpper — extra:
        if (parts.Length > 1 && parts[^1].Length == 2)
            t = string.Join(' ', parts[..^1]);
        return t;
    }

    private static bool EqLoose(string? a, string? b)
    {
        var aa = CollapseWs(a);
        var bb = CollapseWs(b);
        if (aa.Length == 0 && bb.Length == 0) return true;
        return string.Equals(aa, bb, StringComparison.OrdinalIgnoreCase);
    }

    private static bool EqStreet(string? a, string? b) => EqLoose(a, b);

    private static bool EqEmail(string? a, string? b)
    {
        var aa = (a ?? "").Trim().ToLowerInvariant();
        var bb = (b ?? "").Trim().ToLowerInvariant();
        return aa == bb;
    }

    private static bool EqPhone(string? a, string? b)
    {
        var aa = DigitsPhone(a);
        var bb = DigitsPhone(b);
        if (aa.Length == 0 && bb.Length == 0) return true;
        if (aa.Length == 0 || bb.Length == 0) return false;
        // Vergleich auf die letzten 9 Ziffern (CH-Mobil ohne Ländervorwahl)
        string Tail(string d) => d.Length >= 9 ? d[^9..] : d;
        return Tail(aa) == Tail(bb);
    }

    private static string DigitsPhone(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = new string(s.Where(char.IsDigit).ToArray());
        if (d.StartsWith("00")) d = d[2..];
        if (d.StartsWith("0") && d.Length >= 10) d = "41" + d[1..];
        return d;
    }

    private static string NormPlz(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = new string(s.Where(char.IsDigit).ToArray());
        if (d.Length > 4) d = d[..4];
        return d;
    }

    private static string NormNumber(string? s)
        => (s ?? "").Trim().ToLowerInvariant().Replace(" ", "");

    private static string NormName(string? s) => CollapseWs(s).ToLowerInvariant();

    private static string CollapseWs(string? s)
        => Regex.Replace((s ?? "").Trim(), @"\s+", " ");

    private static bool IsBlank(string? s) => string.IsNullOrWhiteSpace(s);
    private static string? NullIfBlank(string? s) => IsBlank(s) ? null : s!.Trim();
    private static string BlankDash(string? s) => IsBlank(s) ? "—" : s!.Trim();
}
