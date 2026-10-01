using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Controllers;

/// <summary>
/// Excel-Import der Manager-Schulungen (Nothelfer / Peak-Verifizierung / Seco
/// + eID/SSO). Seit 01.10.2026 landen die Daten als Einträge in der
/// Schulungs-Historie (<c>employee_schulung</c>, Art UEBERNOMMEN) — Pflege
/// und Übersicht laufen über <see cref="SchulungenController"/>.
/// </summary>
[Authorize(Roles = "admin")]
[ApiController]
[Route("api/manager-schulungen")]
public class ManagerSchulungenController : ControllerBase
{
    private readonly AppDbContext _db;
    public ManagerSchulungenController(AppDbContext db) { _db = db; }

    // ── POST /api/manager-schulungen/import-excel?dryRun= ────────────────
    /// <summary>
    /// Import aus der Nothelfer-Excel: Spalten Name / Nothelfer /
    /// Peak-Verif. / SSO / Seco / ID(=eID) / Gb.D. Match per Namens-Tokens,
    /// Geburtsdatum als Absicherung. Nur gefüllte Excel-Werte zählen; ein
    /// Schulungsdatum, das beim MA schon als Eintrag existiert, wird übersprungen.
    /// </summary>
    [HttpPost("import-excel")]
    public async Task<IActionResult> ImportExcel(IFormFile file, [FromQuery] bool dryRun = true)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { error = "DATEI_FEHLT", message = "Bitte eine .xlsx-Datei hochladen." });

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream);
        stream.Position = 0;
        IWorkbook wb;
        try { wb = new XSSFWorkbook(stream); }
        catch { return BadRequest(new { error = "FORMAT", message = "Datei konnte nicht als .xlsx gelesen werden." }); }
        var sheet = wb.GetSheetAt(0);

        // Header-Zeile finden (Zelle A = «Name»).
        int headerRow = -1;
        var colIdx = new Dictionary<string, int>();
        for (int r = 0; r <= Math.Min(10, sheet.LastRowNum); r++)
        {
            var row = sheet.GetRow(r);
            if (row is null) continue;
            for (int c = 0; c < row.LastCellNum; c++)
            {
                var v = (row.GetCell(c)?.ToString() ?? "").Trim().ToLowerInvariant();
                if (c == 0 && v == "name") headerRow = r;
            }
            if (headerRow == r)
            {
                for (int c = 0; c < row.LastCellNum; c++)
                {
                    var v = (row.GetCell(c)?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (v.StartsWith("nothelfer")) colIdx["nothelfer"] = c;
                    else if (v.StartsWith("peak")) colIdx["peak"] = c;
                    else if (v.StartsWith("seco")) colIdx["seco"] = c;
                    else if (v == "sso") colIdx["sso"] = c;
                    else if (v == "id") colIdx["eid"] = c;
                    else if (v.StartsWith("gb")) colIdx["geb"] = c;
                }
                break;
            }
        }
        if (headerRow < 0 || !colIdx.ContainsKey("nothelfer"))
            return BadRequest(new { error = "HEADER_FEHLT", message = "Kopfzeile (Name / Nothelfer / Peak-Verif. / SSO / Seco / ID / Gb.D) nicht gefunden." });

        var typen = await _db.SchulungTypen.AsNoTracking()
            .Where(t => t.Code == "NOTHELFER" || t.Code == "PEAK" || t.Code == "SECO")
            .ToDictionaryAsync(t => t.Code, t => t.Id);
        if (typen.Count < 3)
            return BadRequest(new { error = "TYP_FEHLT", message = "Im Verzeichnis «Schulungen & Ausbildungen» fehlt Nothelfer, Peak oder SECO." });

        static DateTime? CellDate(ICell? cell)
        {
            if (cell is null) return null;
            try
            {
                if (cell.CellType == CellType.Numeric && DateUtil.IsCellDateFormatted(cell))
                    return cell.DateCellValue?.Date;
            }
            catch { }
            var s = (cell.ToString() ?? "").Trim();
            return DateTime.TryParse(s, out var d) ? d.Date : null;
        }
        static string? CellText(ICell? cell)
        {
            var s = (cell?.ToString() ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ? null : s;
        }
        static HashSet<string> Tokens(string? name) =>
            (name ?? "").ToLowerInvariant()
                .Split(new[] { ' ', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1)
                .ToHashSet();

        // Alle MA laden (Match-Pool; auch inaktive — eID/SSO schadet nicht).
        var emps = await _db.Employees
            .Where(e => !e.IsHidden)
            .ToListAsync();
        var empInfo = emps.Select(e => new
        {
            Emp = e,
            Toks = Tokens($"{e.FirstName} {e.LastName}"),
            Geb = e.DateOfBirth?.Date,
        }).ToList();

        var typIds = typen.Values.ToList();
        var vorhanden = (await _db.EmployeeSchulungen.AsNoTracking()
                .Where(s => typIds.Contains(s.SchulungTypId))
                .Select(s => new { s.EmployeeId, s.SchulungTypId, s.Datum })
                .ToListAsync())
            .Select(s => (s.EmployeeId, s.SchulungTypId, s.Datum))
            .ToHashSet();
        var actor = User.Identity?.Name;

        var matched = new List<object>();
        var unmatched = new List<object>();
        int updated = 0, neueEintraege = 0;

        for (int r = headerRow + 1; r <= sheet.LastRowNum; r++)
        {
            var row = sheet.GetRow(r);
            if (row is null) continue;
            var name = CellText(row.GetCell(0));
            if (name is null) continue;

            var toks = Tokens(name);
            if (toks.Count == 0) continue;
            var geb = colIdx.TryGetValue("geb", out var gc) ? CellDate(row.GetCell(gc)) : null;

            // Match: 1) Geburtsdatum + mind. 1 Namens-Token,
            //        2) sonst alle Excel-Tokens ⊆ DB-Tokens (oder umgekehrt).
            var cands = empInfo.Where(x =>
                (geb != null && x.Geb == geb && x.Toks.Overlaps(toks))
                || toks.IsSubsetOf(x.Toks) || x.Toks.IsSubsetOf(toks)).ToList();
            if (geb != null && cands.Count > 1)
                cands = cands.Where(x => x.Geb == geb).ToList();

            if (cands.Count != 1)
            {
                unmatched.Add(new { zeile = r + 1, name, grund = cands.Count == 0 ? "kein Treffer" : "mehrdeutig" });
                continue;
            }
            var emp = cands[0].Emp;

            var eid = colIdx.TryGetValue("eid", out var ec) ? CellText(row.GetCell(ec)) : null;
            var sso = colIdx.TryGetValue("sso", out var sc) ? CellText(row.GetCell(sc)) : null;
            var nothelfer = CellDate(row.GetCell(colIdx["nothelfer"]));
            var peak = colIdx.TryGetValue("peak", out var pc) ? CellDate(row.GetCell(pc)) : null;
            var seco = colIdx.TryGetValue("seco", out var xc) ? CellDate(row.GetCell(xc)) : null;

            matched.Add(new
            {
                zeile = r + 1, name,
                employeeId = emp.Id,
                maName = $"{emp.FirstName} {emp.LastName}".Trim(),
                employeeNumber = emp.EmployeeNumber,
                eid, sso,
                nothelfer = nothelfer?.ToString("yyyy-MM-dd"),
                peak = peak?.ToString("yyyy-MM-dd"),
                seco = seco?.ToString("yyyy-MM-dd"),
            });

            if (!dryRun)
            {
                // Nur gefüllte Excel-Werte übernehmen (kein Leer-Überschreiben).
                if (eid != null) emp.Eid = eid;
                if (sso != null) emp.Sso = sso;
                foreach (var (code, datum) in new[] { ("NOTHELFER", nothelfer), ("PEAK", peak), ("SECO", seco) })
                {
                    if (datum is null) continue;
                    var tag = DateOnly.FromDateTime(datum.Value);
                    if (!vorhanden.Add((emp.Id, typen[code], tag))) continue;
                    _db.EmployeeSchulungen.Add(new EmployeeSchulung
                    {
                        EmployeeId = emp.Id,
                        SchulungTypId = typen[code],
                        Datum = tag,
                        Art = "UEBERNOMMEN",
                        Bemerkung = "Excel-Import",
                        ErfasstVon = actor,
                        ErfasstAm = DateTime.Now,
                    });
                    neueEintraege++;
                }
                updated++;
            }
        }

        if (!dryRun) await _db.SaveChangesAsync();
        return Ok(new { dryRun, matched, unmatched, updated, neueEintraege });
    }
}
