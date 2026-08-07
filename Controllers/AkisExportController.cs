using HrSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.XSSF.UserModel;

namespace HrSystem.Controllers;

/// <summary>
/// AKISnet-Upload-Excel für GastroSocial (Walter-Vorgabe 06.08.2026):
/// MA-An-/Abmeldungen bei der Ausgleichskasse laufen über das AKIS-Portal
/// (akisnet.ch), das Excel-Sammeldateien akzeptiert. OneCrew füllt die
/// ORIGINAL-Vorlagen (Assets/Forms/Akis_*.xlsx, Daten ab Zeile 8) aus den
/// Ein-/Austritten der Filiale — hochladen bleibt manuell (kein API/ELM).
///
/// Anmeldung  = Vertrag startet im Zeitraum und es lief am Vortag KEIN
///              anderer Vertrag derselben Filiale (nahtlose Vertrags-
///              anpassungen lösen also keine Anmeldung aus).
/// Abmeldung  = Vertrag endet im Zeitraum und es folgt KEIN Anschluss-
///              vertrag derselben Filiale (Folgevertrag = kein Austritt).
/// Jede Filiale ist eine eigene GmbH — Filialwechsel ist damit korrekt
/// eine Abmeldung + Anmeldung.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/akis-export")]
public class AkisExportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public AkisExportController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db; _env = env;
    }

    // ── Kandidaten-Ermittlung ──────────────────────────────────────────

    private sealed record Row(int EmployeeId, string? Ahv, string Name, string Vorname,
        DateTime? GebDat, string? Geschlecht, DateTime Datum, string Sprache);

    private async Task<(List<Row> rows, List<Row> ohneAhv)> AnmeldungenAsync(
        int cpId, DateTime from, DateTime to)
    {
        var emps = await _db.Employments.AsNoTracking()
            .Where(e => e.CompanyProfileId == cpId)
            .Select(e => new { e.EmployeeId, e.ContractStartDate, e.ContractEndDate })
            .ToListAsync();
        var byEmp = emps.GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var kandidaten = new Dictionary<int, DateTime>();
        foreach (var (empId, list) in byEmp)
        {
            foreach (var c in list.Where(c => c.ContractStartDate >= from && c.ContractStartDate <= to))
            {
                var vortag = c.ContractStartDate.AddDays(-1);
                bool liefSchon = list.Any(o => o != c
                    && o.ContractStartDate <= vortag
                    && (o.ContractEndDate == null || o.ContractEndDate >= vortag));
                if (liefSchon) continue;
                // frühester Anmelde-relevanter Start im Zeitraum
                if (!kandidaten.TryGetValue(empId, out var d) || c.ContractStartDate < d)
                    kandidaten[empId] = c.ContractStartDate;
            }
        }
        return await LoadRowsAsync(kandidaten);
    }

    /// <summary>
    /// Abmeldung NUR bei ERFASSTEM Austritt (Employee.ExitDate im Zeitraum) —
    /// ein blosses Ende eines befristeten Vertrags ist KEINE Abmeldung
    /// (Walter 06.08.2026: Verlängerung ist oft nur noch nicht erfasst).
    /// Filial-Zuordnung: der letzte Vertrag des MA (über alle Filialen) muss
    /// in dieser Filiale liegen.
    /// </summary>
    private async Task<(List<Row> rows, List<Row> ohneAhv)> AbmeldungenAsync(
        int cpId, DateTime from, DateTime to)
    {
        var exitEmps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && !e.IsPayrollExcluded
                     && e.ExitDate != null && e.ExitDate >= from && e.ExitDate <= to)
            .Select(e => new { e.Id, e.ExitDate })
            .ToListAsync();
        if (exitEmps.Count == 0) return (new List<Row>(), new List<Row>());

        var ids = exitEmps.Select(e => e.Id).ToList();
        var alleVertraege = await _db.Employments.AsNoTracking()
            .Where(e => ids.Contains(e.EmployeeId) && e.CompanyProfileId != null)
            .Select(e => new { e.EmployeeId, e.CompanyProfileId, e.ContractStartDate, e.ContractEndDate })
            .ToListAsync();

        var kandidaten = new Dictionary<int, DateTime>();
        foreach (var e in exitEmps)
        {
            var vs = alleVertraege.Where(v => v.EmployeeId == e.Id).ToList();
            if (vs.Count == 0) continue;
            // letzter Vertrag = offenes Ende zuerst, sonst spätestes Ende
            var letzter = vs.OrderByDescending(v => v.ContractEndDate == null)
                            .ThenByDescending(v => v.ContractEndDate)
                            .ThenByDescending(v => v.ContractStartDate)
                            .First();
            if (letzter.CompanyProfileId == cpId)
                kandidaten[e.Id] = e.ExitDate!.Value;
        }
        return await LoadRowsAsync(kandidaten);
    }

    /// <summary>
    /// Info-Liste: befristete Verträge der Filiale, die im Zeitraum enden,
    /// OHNE Anschlussvertrag und OHNE erfassten Austritt — hier muss Walter
    /// entscheiden: verlängern oder Austritt erfassen.
    /// </summary>
    private async Task<List<Row>> BefristetOffenAsync(int cpId, DateTime from, DateTime to)
    {
        var emps = await _db.Employments.AsNoTracking()
            .Where(e => e.CompanyProfileId == cpId)
            .Select(e => new { e.EmployeeId, e.ContractStartDate, e.ContractEndDate })
            .ToListAsync();
        var byEmp = emps.GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var kandidaten = new Dictionary<int, DateTime>();
        foreach (var (empId, list) in byEmp)
        {
            foreach (var c in list.Where(c => c.ContractEndDate != null
                                           && c.ContractEndDate >= from && c.ContractEndDate <= to))
            {
                var folgetag = c.ContractEndDate!.Value.AddDays(1);
                bool folgt = list.Any(o => o != c
                    && o.ContractStartDate <= folgetag
                    && (o.ContractEndDate == null || o.ContractEndDate > c.ContractEndDate));
                if (folgt) continue;
                if (!kandidaten.TryGetValue(empId, out var d) || c.ContractEndDate > d)
                    kandidaten[empId] = c.ContractEndDate!.Value;
            }
        }
        if (kandidaten.Count == 0) return new List<Row>();
        // MA mit erfasstem Austritt raus (die sind echte Abmeldungen)
        var ids = kandidaten.Keys.ToList();
        var ohneExit = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.ExitDate == null)
            .Select(e => e.Id)
            .ToListAsync();
        var gefiltert = kandidaten.Where(k => ohneExit.Contains(k.Key))
            .ToDictionary(k => k.Key, k => k.Value);
        var (rows, ohneAhv) = await LoadRowsAsync(gefiltert);
        return rows.Concat(ohneAhv)
            .OrderBy(r => r.Vorname, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<(List<Row> rows, List<Row> ohneAhv)> LoadRowsAsync(Dictionary<int, DateTime> kandidaten)
    {
        if (kandidaten.Count == 0) return (new List<Row>(), new List<Row>());
        var ids = kandidaten.Keys.ToList();
        var people = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && !e.IsPayrollExcluded && !e.IsHidden)
            .Select(e => new
            {
                e.Id, e.SocialSecurityNumber, e.FirstName, e.LastName,
                e.DateOfBirth, e.Gender, e.Salutation, e.LanguageCode,
            })
            .ToListAsync();

        var rows = new List<Row>();
        foreach (var p in people)
        {
            rows.Add(new Row(
                p.Id,
                string.IsNullOrWhiteSpace(p.SocialSecurityNumber) ? null : p.SocialSecurityNumber.Trim(),
                p.LastName ?? "", p.FirstName ?? "",
                p.DateOfBirth,
                MapGeschlecht(p.Gender, p.Salutation),
                kandidaten[p.Id],
                MapSprache(p.LanguageCode)));
        }
        // Sortierung Vorname (Projekt-Konvention)
        rows = rows.OrderBy(r => r.Vorname, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return (rows.Where(r => r.Ahv != null).ToList(),
                rows.Where(r => r.Ahv == null).ToList());
    }

    private static string? MapGeschlecht(string? gender, string? salutation)
    {
        var g = (gender ?? "").Trim().ToLowerInvariant();
        if (g is "f" or "w" or "female" or "frau" or "weiblich") return "W";
        if (g is "m" or "male" or "mann" or "herr" or "männlich") return "M";
        var s = (salutation ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("frau")) return "W";
        if (s.StartsWith("herr")) return "M";
        return null;
    }

    private static string MapSprache(string? code) =>
        (code ?? "").Trim().ToLowerInvariant() switch
        {
            "fr" or "f" => "F",
            "it" or "i" => "I",
            "en" or "e" => "E",
            _           => "D",
        };

    // ── Vorschau (JSON für die UI) ─────────────────────────────────────

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(
        [FromQuery] int companyProfileId, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var (an, anOhne) = await AnmeldungenAsync(companyProfileId, from.Date, to.Date);
        var (ab, abOhne) = await AbmeldungenAsync(companyProfileId, from.Date, to.Date);
        var befristet = await BefristetOffenAsync(companyProfileId, from.Date, to.Date);
        object Map(Row r) => new
        {
            r.EmployeeId, ahv = r.Ahv, name = r.Name, vorname = r.Vorname,
            gebDat = r.GebDat?.ToString("dd.MM.yyyy"),
            geschlecht = r.Geschlecht,
            datum = r.Datum.ToString("dd.MM.yyyy"),
            sprache = r.Sprache,
        };
        return Ok(new
        {
            anmeldungen = an.Select(Map),
            anmeldungenOhneAhv = anOhne.Select(Map),   // → zuerst AHV-Anmeldung 318.260!
            abmeldungen = ab.Select(Map),
            abmeldungenOhneAhv = abOhne.Select(Map),
            // Befristungen ohne erfassten Austritt: verlängern oder Austritt erfassen?
            befristetOffen = befristet.Select(Map),
        });
    }

    // ── Excel-Erzeugung aus den ORIGINAL-Vorlagen ──────────────────────

    [HttpGet("anmeldung")]
    public async Task<IActionResult> AnmeldungXlsx(
        [FromQuery] int companyProfileId, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var (rows, _) = await AnmeldungenAsync(companyProfileId, from.Date, to.Date);
        var bytes = FillTemplate("Akis_AnmeldungMitarbeitende.xlsx", ws =>
        {
            int r = 7; // 0-basiert → Zeile 8
            foreach (var x in rows)
            {
                var row = ws.CreateRow(r++);
                row.CreateCell(0).SetCellValue(x.Ahv);
                row.CreateCell(1).SetCellValue(x.Name);
                row.CreateCell(2).SetCellValue(x.Vorname);
                row.CreateCell(3).SetCellValue(x.GebDat?.ToString("dd.MM.yyyy") ?? "");
                row.CreateCell(4).SetCellValue(x.Geschlecht ?? "");
                row.CreateCell(5).SetCellValue(x.Datum.ToString("dd.MM.yyyy"));
                row.CreateCell(6).SetCellValue(x.Sprache);
                // Spalte H (Duplikat Versicherungsausweis) bleibt leer.
            }
        });
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AnmeldungMitarbeitende_{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }

    [HttpGet("abmeldung")]
    public async Task<IActionResult> AbmeldungXlsx(
        [FromQuery] int companyProfileId, [FromQuery] DateTime from, [FromQuery] DateTime to)
    {
        var (rows, _) = await AbmeldungenAsync(companyProfileId, from.Date, to.Date);
        var bytes = FillTemplate("Akis_AbmeldungMitarbeitende.xlsx", ws =>
        {
            int r = 7;
            foreach (var x in rows)
            {
                var row = ws.CreateRow(r++);
                row.CreateCell(0).SetCellValue(x.Ahv);
                row.CreateCell(1).SetCellValue(x.Name);
                row.CreateCell(2).SetCellValue(x.Vorname);
                row.CreateCell(3).SetCellValue(x.Datum.ToString("dd.MM.yyyy"));
            }
        });
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AbmeldungMitarbeitende_{from:yyyyMMdd}-{to:yyyyMMdd}.xlsx");
    }

    private byte[] FillTemplate(string templateFile, Action<NPOI.SS.UserModel.ISheet> fill)
    {
        var path = System.IO.Path.Combine(_env.ContentRootPath, "Assets", "Forms", templateFile);
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException($"Vorlage fehlt: {path}");
        using var fs = System.IO.File.OpenRead(path);
        var wb = new XSSFWorkbook(fs);
        var ws = wb.GetSheet("Mitarbeitende") ?? wb.GetSheetAt(0);
        fill(ws);
        // COUNTA-Formel (Anzahl MA) beim Öffnen neu rechnen lassen.
        wb.SetForceFormulaRecalculation(true);
        using var ms = new MemoryStream();
        wb.Write(ms, leaveOpen: true);
        return ms.ToArray();
    }
}
