using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Manager-Dienstplan (Walter-Vorgabe 08.08.2026, ersetzt die Excel):
/// Monats-Grid über ALLE Filialen — Zeilen = FIX-M-MA mit laufendem Vertrag
/// (Heimatfiliale = Filiale des laufenden FIX-M-Vertrags), Zellen = Kürzel
/// aus dienstplan_code. Absenzen (Ferien/Krank/…) kommen als Live-Overlay
/// aus den Absences und sperren die Zelle serverseitig.
///
/// Rechte: SEHEN darf jeder mit Zugriff (admin/superuser/user), PLANEN darf
/// admin überall, sonst nur Filialen mit user_branch_access.can_dienstplan
/// (Pflege im Filial-Tab «Unterzeichner»).
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/manager-dienstplan")]
public class ManagerDienstplanController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Services.ManagerDienstplanPdfService _pdf;
    public ManagerDienstplanController(AppDbContext db, Services.ManagerDienstplanPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    private int? GetUserId()
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private async Task<(bool isAdmin, HashSet<int> planBranches, string? name)> GetPlanRechteAsync()
    {
        var uid = GetUserId();
        var isAdmin = User.IsInRole("admin");
        var branches = new HashSet<int>();
        string? name = null;
        if (uid.HasValue)
        {
            // DisplayName ist ein berechnetes Property (nicht EF-übersetzbar) —
            // Rohfelder laden und im Speicher zusammensetzen.
            var u = await _db.AppUsers.AsNoTracking()
                .Where(x => x.Id == uid.Value)
                .Select(x => new
                {
                    x.FirstName, x.LastName, x.Username,
                    Branches = x.BranchAccess.Where(b => b.CanDienstplan)
                                             .Select(b => b.CompanyProfileId).ToList(),
                })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                var voll = $"{u.FirstName} {u.LastName}".Trim();
                name = string.IsNullOrWhiteSpace(voll) ? u.Username : voll;
                foreach (var b in u.Branches) branches.Add(b);
            }
        }
        return (isAdmin, branches, name);
    }

    /// <summary>Monats-Grid: Zeilen, Plan-Zellen, Absenz-Overlay, Kürzel-Katalog.</summary>
    [HttpGet]
    public async Task<IActionResult> GetMonth([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG" });
        var (zeilen, filialen, codes, feiertage, schulferien) = await BuildMonthDataAsync(year, month);
        return Ok(new
        {
            year, month,
            filialen = filialen.Select(b => new { id = b.Id, code = b.Code, name = b.Name, kanton = b.Kanton }),
            feiertage = feiertage.Select(f => new
            {
                companyProfileId = f.CompanyProfileId,
                datum = f.Datum.ToString("yyyy-MM-dd"),
                bezeichnung = f.Bezeichnung,
            }),
            schulferien = schulferien.Select(s => new
            {
                companyProfileId = s.CompanyProfileId,
                von = s.Von.ToString("yyyy-MM-dd"),
                bis = s.Bis.ToString("yyyy-MM-dd"),
                bezeichnung = s.Bezeichnung,
            }),
            zeilen = zeilen.Select(z => new
            {
                employeeId = z.EmployeeId,
                vorname = z.Vorname,
                nachname = z.Nachname,
                companyProfileId = z.CompanyProfileId,
                istGf = z.IstGf,
                planbar = z.Planbar,
                zellen = z.Zellen,
                absenzen = z.Absenzen.Select(a => new
                {
                    typ = a.Typ,
                    von = a.Von.ToString("yyyy-MM-dd"),
                    bis = a.Bis.ToString("yyyy-MM-dd"),
                }),
            }),
            codes = codes.Select(x => new { code = x.Code, bezeichnung = x.Bezeichnung, farbe = x.Farbe }),
        });
    }

    /// <summary>Manager-Dienstplan als A4-quer-PDF (Walter-Vorgabe 09.08.2026).</summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf([FromQuery] int year, [FromQuery] int month)
    {
        if (year < 2020 || year > 2100 || month < 1 || month > 12)
            return BadRequest(new { error = "PERIODE_UNGUELTIG" });
        try
        {
            var (zeilen, filialen, codes, feiertage, schulferien) = await BuildMonthDataAsync(year, month);
            var bytes = _pdf.Generate(year, month, zeilen, filialen, codes, feiertage, schulferien);
            return File(bytes, "application/pdf", $"Manager-Dienstplan_{year}-{month:D2}.pdf");
        }
        catch (Exception ex)
        {
            // Fehler sichtbar machen — file-preview.js zeigt das error-Feld an.
            return StatusCode(500, new { error = $"PDF-Fehler: {ex.Message}" });
        }
    }

    private async Task<(List<DpZeileInfo> zeilen, List<DpFilialeInfo> filialen, List<DpCodeInfo> codes,
                       List<DpFeiertagInfo> feiertage, List<DpSchulferienInfo> schulferien)>
        BuildMonthDataAsync(int year, int month)
    {
        var from = new DateOnly(year, month, 1);
        var to   = from.AddMonths(1).AddDays(-1);
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MinValue);

        // FIX-M-MA mit im Monat LAUFENDEM Vertrag; Heimatfiliale = Filiale
        // des (spätesten) laufenden FIX-M-Vertrags.
        var fixmRows = await _db.Employments.AsNoTracking()
            .Where(em => em.EmploymentModel == "FIX-M"
                      && em.CompanyProfileId != null
                      && em.ContractStartDate <= toDt
                      && (em.ContractEndDate == null || em.ContractEndDate >= fromDt)
                      && !em.Employee!.IsHidden && !em.Employee!.IsPayrollExcluded)
            .Select(em => new
            {
                em.EmployeeId, em.CompanyProfileId, em.ContractStartDate,
                em.Employee!.FirstName, em.Employee!.LastName,
                JobCode = em.JobGroup != null ? em.JobGroup.Code : em.JobTitle,
            })
            .ToListAsync();
        var proMa = fixmRows
            .GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ContractStartDate).First())
            .ToList();
        var empIds = proMa.Select(x => x.EmployeeId).ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.BranchName, c.City, c.KantonCode, c.WorkLocation })
            .ToListAsync();

        // Feiertage des Monats pro Filiale auflösen: NATIONAL → alle,
        // KANTON → Filialen mit passendem Kanton, FILIALE → genau die eine.
        var ftRoh = await _db.DienstplanFeiertage.AsNoTracking()
            .Where(f => f.Datum >= from && f.Datum <= to)
            .ToListAsync();
        var feiertage = new List<DpFeiertagInfo>();
        foreach (var f in ftRoh)
            foreach (var b in branches)
            {
                bool gilt = f.Scope switch
                {
                    "NATIONAL" => true,
                    "KANTON"   => !string.IsNullOrEmpty(f.KantonCode)
                                  && string.Equals(b.KantonCode, f.KantonCode, StringComparison.OrdinalIgnoreCase),
                    "FILIALE"  => f.CompanyProfileId == b.Id,
                    _ => false,
                };
                if (gilt) feiertage.Add(new DpFeiertagInfo(b.Id, f.Datum, f.Bezeichnung));
            }

        // Schulferien, die den Monat überlappen — auf den Monat geclampt.
        var schulferien = (await _db.BranchSchulferien.AsNoTracking()
                .Where(s => s.Von <= to && s.Bis >= from)
                .ToListAsync())
            .Select(s => new DpSchulferienInfo(
                s.CompanyProfileId,
                s.Von < from ? from : s.Von,
                s.Bis > to ? to : s.Bis,
                s.Bezeichnung))
            .ToList();

        var plan = await _db.ManagerDienstplanEntries.AsNoTracking()
            .Where(p => empIds.Contains(p.EmployeeId) && p.Datum >= from && p.Datum <= to)
            .Select(p => new { p.EmployeeId, p.Datum, p.Code })
            .ToListAsync();

        var absences = await _db.Absences.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.DateFrom <= to && a.DateTo >= from)
            .Select(a => new { a.EmployeeId, a.AbsenceType, a.DateFrom, a.DateTo })
            .ToListAsync();

        var codes = await _db.DienstplanCodes.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new DpCodeInfo(c.Code, c.Bezeichnung, c.Farbe))
            .ToListAsync();

        var (isAdmin, planBranches, _) = await GetPlanRechteAsync();

        // Filial-Reihenfolge wie überall in OneCrew: nach Restaurant-Nummer.
        // Innerhalb GF zuoberst, dann Vorname + Nachname (Walter 09.08.2026).
        var zeilen = proMa
            .OrderBy(x => branches.FirstOrDefault(b => b.Id == x.CompanyProfileId)?.RestaurantCode ?? "")
            .ThenByDescending(x => x.JobCode == "REST_MANAGER")
            .ThenBy(x => x.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.LastName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DpZeileInfo(
                x.EmployeeId,
                x.FirstName ?? "",
                x.LastName ?? "",
                x.CompanyProfileId,
                x.JobCode == "REST_MANAGER",
                isAdmin || (x.CompanyProfileId.HasValue && planBranches.Contains(x.CompanyProfileId.Value)),
                plan.Where(p => p.EmployeeId == x.EmployeeId)
                    .ToDictionary(p => p.Datum.ToString("yyyy-MM-dd"), p => p.Code),
                absences.Where(a => a.EmployeeId == x.EmployeeId)
                    .Select(a => new DpAbsenzInfo(
                        a.AbsenceType,
                        a.DateFrom < from ? from : a.DateFrom,
                        a.DateTo > to ? to : a.DateTo))
                    .ToList()))
            .ToList();

        // Anzeigename = Arbeitsort pur («Hendschiken», «Langenthal Drive») —
        // ohne Restaurant-Code und ohne «Filiale»-Präfix (Walter 09.08.2026).
        var filialen = branches
            .Select(b => new DpFilialeInfo(
                b.Id,
                null,
                !string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation : (b.City ?? b.BranchName),
                b.KantonCode))
            .ToList();
        return (zeilen, filialen, codes, feiertage, schulferien);
    }

    // ── Feiertage (zentral, admin/superuser) ─────────────────────────────
    [HttpGet("feiertage")]
    public async Task<IActionResult> GetFeiertage([FromQuery] int? year)
    {
        var q = _db.DienstplanFeiertage.AsNoTracking();
        if (year.HasValue)
            q = q.Where(f => f.Datum >= new DateOnly(year.Value, 1, 1) && f.Datum <= new DateOnly(year.Value, 12, 31));
        var list = await q.OrderBy(f => f.Datum).ToListAsync();
        return Ok(list.Select(f => new
        {
            f.Id,
            datum = f.Datum.ToString("yyyy-MM-dd"),
            f.Bezeichnung,
            f.Scope,
            f.KantonCode,
            f.CompanyProfileId,
        }));
    }

    public class FeiertagDto
    {
        public string? Datum { get; set; }
        public string? Bezeichnung { get; set; }
        public string? Scope { get; set; }
        public string? KantonCode { get; set; }
        public int? CompanyProfileId { get; set; }
    }

    [Authorize(Roles = "admin,superuser")]
    [HttpPost("feiertage")]
    public async Task<IActionResult> AddFeiertag([FromBody] FeiertagDto dto)
    {
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM_UNGUELTIG" });
        if (string.IsNullOrWhiteSpace(dto.Bezeichnung))
            return BadRequest(new { error = "BEZEICHNUNG_FEHLT" });
        var scope = (dto.Scope ?? "NATIONAL").ToUpperInvariant();
        if (scope is not ("NATIONAL" or "KANTON" or "FILIALE"))
            return BadRequest(new { error = "SCOPE_UNGUELTIG" });
        if (scope == "KANTON" && string.IsNullOrWhiteSpace(dto.KantonCode))
            return BadRequest(new { error = "KANTON_FEHLT" });
        if (scope == "FILIALE" && !dto.CompanyProfileId.HasValue)
            return BadRequest(new { error = "FILIALE_FEHLT" });
        _db.DienstplanFeiertage.Add(new DienstplanFeiertag
        {
            Datum = datum,
            Bezeichnung = dto.Bezeichnung.Trim(),
            Scope = scope,
            KantonCode = scope == "KANTON" ? dto.KantonCode!.Trim().ToUpperInvariant() : null,
            CompanyProfileId = scope == "FILIALE" ? dto.CompanyProfileId : null,
            CreatedAt = DateTime.Now,
        });
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [Authorize(Roles = "admin,superuser")]
    [HttpDelete("feiertage/{id:int}")]
    public async Task<IActionResult> DeleteFeiertag(int id)
    {
        var f = await _db.DienstplanFeiertage.FindAsync(id);
        if (f == null) return NotFound();
        _db.DienstplanFeiertage.Remove(f);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ── Schulferien pro Filiale (admin überall, sonst Planungsrecht) ─────
    [HttpGet("schulferien")]
    public async Task<IActionResult> GetSchulferien([FromQuery] int? year)
    {
        var q = _db.BranchSchulferien.AsNoTracking();
        if (year.HasValue)
            q = q.Where(s => s.Von <= new DateOnly(year.Value, 12, 31) && s.Bis >= new DateOnly(year.Value, 1, 1));
        var list = await q.OrderBy(s => s.Von).ToListAsync();
        return Ok(list.Select(s => new
        {
            s.Id,
            s.CompanyProfileId,
            s.Bezeichnung,
            von = s.Von.ToString("yyyy-MM-dd"),
            bis = s.Bis.ToString("yyyy-MM-dd"),
        }));
    }

    public class SchulferienDto
    {
        public int CompanyProfileId { get; set; }
        public string? Bezeichnung { get; set; }
        public string? Von { get; set; }
        public string? Bis { get; set; }
    }

    [HttpPost("schulferien")]
    public async Task<IActionResult> AddSchulferien([FromBody] SchulferienDto dto)
    {
        if (!DateOnly.TryParse(dto.Von, out var von) || !DateOnly.TryParse(dto.Bis, out var bis) || bis < von)
            return BadRequest(new { error = "DATUM_UNGUELTIG" });
        if (string.IsNullOrWhiteSpace(dto.Bezeichnung))
            return BadRequest(new { error = "BEZEICHNUNG_FEHLT" });
        var (isAdmin, planBranches, _) = await GetPlanRechteAsync();
        if (!isAdmin && !planBranches.Contains(dto.CompanyProfileId))
            return StatusCode(403, new { error = "KEIN_PLANRECHT", message = "Kein Planungsrecht für diese Filiale." });
        _db.BranchSchulferien.Add(new BranchSchulferien
        {
            CompanyProfileId = dto.CompanyProfileId,
            Bezeichnung = dto.Bezeichnung.Trim(),
            Von = von,
            Bis = bis,
            CreatedAt = DateTime.Now,
        });
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("schulferien/{id:int}")]
    public async Task<IActionResult> DeleteSchulferien(int id)
    {
        var s = await _db.BranchSchulferien.FindAsync(id);
        if (s == null) return NotFound();
        var (isAdmin, planBranches, _) = await GetPlanRechteAsync();
        if (!isAdmin && !planBranches.Contains(s.CompanyProfileId))
            return StatusCode(403, new { error = "KEIN_PLANRECHT", message = "Kein Planungsrecht für diese Filiale." });
        _db.BranchSchulferien.Remove(s);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Vorstellungsgespräch-Zeitfenster (Walter-Vorgabe 09.08.2026, Stufe 1):
    //  Der GF teilt NUR mit, wann er Zeit für Vorstellungsgespräche hat —
    //  an einem Tag, der im Manager-DP als ARBEIT (F/M/S) geplant ist.
    //  HR sieht die Fenster im HR-Hub. Terminbuchung = Stufe 2 (offen).
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Heimatfiliale des Managers am Datum (laufender FIX-M-Vertrag).</summary>
    private async Task<int?> HomeBranchAsync(int employeeId, DateOnly datum)
    {
        var dt = datum.ToDateTime(TimeOnly.MinValue);
        return await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == employeeId
                      && em.EmploymentModel == "FIX-M"
                      && em.ContractStartDate <= dt
                      && (em.ContractEndDate == null || em.ContractEndDate >= dt))
            .OrderByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfileId)
            .FirstOrDefaultAsync();
    }

    /// <summary>Kommende Fenster (ab heute); optional nur ein Manager.</summary>
    [HttpGet("interview-fenster")]
    public async Task<IActionResult> GetInterviewFenster([FromQuery] int? employeeId)
    {
        var heute = DateOnly.FromDateTime(DateTime.Now);
        var q = _db.InterviewFenster.AsNoTracking().Where(f => f.Datum >= heute);
        if (employeeId.HasValue) q = q.Where(f => f.EmployeeId == employeeId.Value);
        var fenster = await q.OrderBy(f => f.Datum).ThenBy(f => f.VonZeit).ToListAsync();

        var empIds = fenster.Select(f => f.EmployeeId).Distinct().ToList();
        var namen = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id);
        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.WorkLocation, c.City, c.BranchName })
            .ToListAsync();

        // Gebuchte Termine (GEPLANT) aller Fenster in einem Rutsch.
        var fensterIds = fenster.Select(f => f.Id).ToList();
        var termine = await _db.InterviewTermine.AsNoTracking()
            .Where(t => fensterIds.Contains(t.FensterId) && t.Status == "GEPLANT")
            .ToListAsync();

        var result = new List<object>();
        foreach (var f in fenster)
        {
            var cpId = await HomeBranchAsync(f.EmployeeId, f.Datum);
            var b = branches.FirstOrDefault(x => x.Id == cpId);
            namen.TryGetValue(f.EmployeeId, out var n);
            var slots = BerechneSlots(f.VonZeit, f.BisZeit).Select(s =>
            {
                var t = termine.FirstOrDefault(x => x.FensterId == f.Id && x.VonZeit == s.von);
                return new
                {
                    von = s.von.ToString("HH:mm"),
                    bis = s.bis.ToString("HH:mm"),
                    terminId = t?.Id,
                    kandidat = t?.Kandidat,
                    telefon = t?.Telefon,
                    terminBemerkung = t?.Bemerkung,
                };
            }).ToList();
            result.Add(new
            {
                f.Id,
                f.EmployeeId,
                manager = n == null ? "" : $"{n.FirstName} {(string.IsNullOrEmpty(n.LastName) ? "" : n.LastName[..1] + ".")}".Trim(),
                companyProfileId = cpId,
                filiale = b == null ? "" : (!string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation : (b.City ?? b.BranchName ?? "")),
                datum = f.Datum.ToString("yyyy-MM-dd"),
                von = f.VonZeit.ToString("HH:mm"),
                bis = f.BisZeit.ToString("HH:mm"),
                f.Bemerkung,
                slots,
            });
        }
        return Ok(result);
    }

    /// <summary>
    /// Slot-Raster eines Fensters: 30 Min Gespräch + 15 Min Puffer (= 45er-
    /// Raster, verankert am Fensterstart). 15:00–17:00 → 15:00, 15:45, 16:30.
    /// </summary>
    private static List<(TimeOnly von, TimeOnly bis)> BerechneSlots(TimeOnly von, TimeOnly bis)
    {
        var slots = new List<(TimeOnly, TimeOnly)>();
        var t = von;
        while (true)
        {
            var ende = t.AddMinutes(30);
            if (ende < t || ende > bis) break;   // Fenster voll oder Mitternachts-Wrap
            slots.Add((t, ende));
            var next = t.AddMinutes(45);
            if (next <= t) break;
            t = next;
        }
        return slots;
    }

    /// <summary>Geplante Arbeitstage (F/M/S) eines Managers ab heute — für das Tag-Dropdown.</summary>
    [HttpGet("interview-fenster/arbeitstage/{employeeId:int}")]
    public async Task<IActionResult> GetArbeitstage(int employeeId)
    {
        var heute = DateOnly.FromDateTime(DateTime.Now);
        var bis = heute.AddDays(60);
        var arbeit = new[] { "F", "M", "S" };
        var tage = await _db.ManagerDienstplanEntries.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.Datum >= heute && p.Datum <= bis && arbeit.Contains(p.Code))
            .OrderBy(p => p.Datum)
            .Select(p => new { datum = p.Datum, p.Code })
            .ToListAsync();
        return Ok(tage.Select(t => new { datum = t.datum.ToString("yyyy-MM-dd"), code = t.Code }));
    }

    public class InterviewFensterDto
    {
        public int EmployeeId { get; set; }
        public string? Datum { get; set; }     // ISO yyyy-MM-dd
        public string? Von { get; set; }       // HH:mm
        public string? Bis { get; set; }       // HH:mm
        public string? Bemerkung { get; set; }
    }

    [HttpPost("interview-fenster")]
    public async Task<IActionResult> AddInterviewFenster([FromBody] InterviewFensterDto dto)
    {
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM_UNGUELTIG" });
        if (!TimeOnly.TryParse(dto.Von, out var von) || !TimeOnly.TryParse(dto.Bis, out var bis) || bis <= von)
            return BadRequest(new { error = "ZEIT_UNGUELTIG", message = "Von/Bis-Zeit prüfen (Bis muss nach Von liegen)." });

        // Rechte: admin überall, sonst Planungsrecht auf der Heimatfiliale.
        var (isAdmin, planBranches, name) = await GetPlanRechteAsync();
        var cpId = await HomeBranchAsync(dto.EmployeeId, datum);
        if (!isAdmin && (!cpId.HasValue || !planBranches.Contains(cpId.Value)))
            return StatusCode(403, new { error = "KEIN_PLANRECHT", message = "Kein Planungsrecht für die Filiale dieses Managers." });

        // Nur an Tagen, die im Manager-DP als ARBEIT (F/M/S) geplant sind.
        var arbeit = new[] { "F", "M", "S" };
        bool istArbeitstag = await _db.ManagerDienstplanEntries.AnyAsync(p =>
            p.EmployeeId == dto.EmployeeId && p.Datum == datum && arbeit.Contains(p.Code));
        if (!istArbeitstag)
            return Conflict(new
            {
                error = "KEIN_ARBEITSTAG",
                message = "An diesem Tag ist im Manager-Dienstplan keine Arbeit (F/M/S) geplant — zuerst den Dienstplan pflegen.",
            });

        _db.InterviewFenster.Add(new InterviewFenster
        {
            EmployeeId = dto.EmployeeId,
            Datum = datum,
            VonZeit = von,
            BisZeit = bis,
            Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            CreatedAt = DateTime.Now,
            CreatedBy = name,
        });
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("interview-fenster/{id:int}")]
    public async Task<IActionResult> DeleteInterviewFenster(int id)
    {
        var f = await _db.InterviewFenster.FindAsync(id);
        if (f == null) return NotFound();
        var (isAdmin, planBranches, _) = await GetPlanRechteAsync();
        if (!isAdmin)
        {
            var cpId = await HomeBranchAsync(f.EmployeeId, f.Datum);
            if (!cpId.HasValue || !planBranches.Contains(cpId.Value))
                return StatusCode(403, new { error = "KEIN_PLANRECHT", message = "Kein Planungsrecht für die Filiale dieses Managers." });
        }
        // Fenster mit gebuchten Gesprächen kann nicht gelöscht werden —
        // HR muss die Termine zuerst absagen (Kandidaten sind informiert!).
        bool hatTermine = await _db.InterviewTermine.AnyAsync(t => t.FensterId == id && t.Status == "GEPLANT");
        if (hatTermine)
            return Conflict(new
            {
                error = "TERMINE_VORHANDEN",
                message = "In diesem Fenster sind bereits Gespräche gebucht — bitte HR kontaktieren, damit die Termine zuerst abgesagt werden.",
            });
        _db.InterviewFenster.Remove(f);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ── Termin-Buchung durch HR (Stufe 2, Walter 09.08.2026) ─────────────
    public class InterviewTerminDto
    {
        public int FensterId { get; set; }
        public string? Von { get; set; }       // Slot-Start HH:mm (muss im Raster liegen)
        public string? Kandidat { get; set; }
        public string? Telefon { get; set; }
        public string? Bemerkung { get; set; }
    }

    [Authorize(Roles = "admin,superuser")]
    [HttpPost("interview-termin")]
    public async Task<IActionResult> AddInterviewTermin([FromBody] InterviewTerminDto dto)
    {
        var f = await _db.InterviewFenster.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.FensterId);
        if (f == null) return NotFound(new { error = "FENSTER_FEHLT" });
        if (f.Datum < DateOnly.FromDateTime(DateTime.Now))
            return Conflict(new { error = "FENSTER_VERGANGEN", message = "Dieses Zeitfenster liegt in der Vergangenheit." });
        if (string.IsNullOrWhiteSpace(dto.Kandidat))
            return BadRequest(new { error = "KANDIDAT_FEHLT", message = "Kandidatenname angeben." });
        if (!TimeOnly.TryParse(dto.Von, out var von))
            return BadRequest(new { error = "ZEIT_UNGUELTIG" });
        if (!BerechneSlots(f.VonZeit, f.BisZeit).Any(s => s.von == von))
            return BadRequest(new { error = "SLOT_UNGUELTIG", message = "Startzeit liegt nicht im 45-Minuten-Raster des Fensters." });
        bool belegt = await _db.InterviewTermine.AnyAsync(t =>
            t.FensterId == f.Id && t.VonZeit == von && t.Status == "GEPLANT");
        if (belegt)
            return Conflict(new { error = "SLOT_BELEGT", message = "Dieser Slot ist bereits gebucht." });

        var (_, _, name) = await GetPlanRechteAsync();
        _db.InterviewTermine.Add(new InterviewTermin
        {
            FensterId = f.Id,
            VonZeit = von,
            Kandidat = dto.Kandidat.Trim(),
            Telefon = string.IsNullOrWhiteSpace(dto.Telefon) ? null : dto.Telefon.Trim(),
            Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            Status = "GEPLANT",
            CreatedAt = DateTime.Now,
            CreatedBy = name,
        });
        await _db.SaveChangesAsync();
        await TryPostfachNachrichtAsync(f,
            $"HR hat ein Vorstellungsgespräch gebucht: {f.Datum:dd.MM.yyyy}, {von:HH\\:mm}–{von.AddMinutes(30):HH\\:mm} — Kandidat/in: {dto.Kandidat.Trim()}"
            + (string.IsNullOrWhiteSpace(dto.Bemerkung) ? "" : $" ({dto.Bemerkung.Trim()})"));
        return Ok(new { ok = true });
    }

    [Authorize(Roles = "admin,superuser")]
    [HttpPost("interview-termin/{id:int}/absagen")]
    public async Task<IActionResult> AbsagenInterviewTermin(int id)
    {
        var t = await _db.InterviewTermine.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (t.Status == "ABGESAGT") return Ok(new { ok = true });
        t.Status = "ABGESAGT";
        await _db.SaveChangesAsync();
        var f = await _db.InterviewFenster.AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.FensterId);
        if (f != null)
            await TryPostfachNachrichtAsync(f,
                $"HR hat das Vorstellungsgespräch vom {f.Datum:dd.MM.yyyy} um {t.VonZeit:HH\\:mm} (Kandidat/in {t.Kandidat}) abgesagt.");
        return Ok(new { ok = true });
    }

    /// <summary>Best-effort-Textnachricht ins MA-Postfach des Managers (wie Lohnanpassung).</summary>
    private async Task TryPostfachNachrichtAsync(InterviewFenster f, string text)
    {
        try
        {
            var cpId = await HomeBranchAsync(f.EmployeeId, f.Datum);
            if (!cpId.HasValue) return;
            _db.MailboxDocuments.Add(new MailboxDocument
            {
                CompanyProfileId = cpId.Value,
                UploadedBy = GetUserId(),
                UploadedAt = DateTime.Now,
                OriginalFilename = $"Vorstellungsgespräch {f.Datum:dd.MM.yyyy}",
                StorageFilename = $"msg-{Guid.NewGuid():N}",
                MimeType = null,
                FileSizeBytes = null,
                Bemerkung = "Vorstellungsgespräch",
                MessageBody = text,
                EmployeeId = f.EmployeeId,
                NotifyUserId = null,
                TargetType = "EMPLOYEE",
            });
            await _db.SaveChangesAsync();
        }
        catch
        {
            // best-effort — Buchung/Absage ist bereits gespeichert.
        }
    }

    public class CellDto
    {
        public int EmployeeId { get; set; }
        public string? Datum { get; set; }   // ISO yyyy-MM-dd
        public string? Code { get; set; }    // null/leer = Zelle löschen
    }

    /// <summary>Zelle setzen/löschen — nur mit Planungsrecht auf der Heimatfiliale des MA.</summary>
    [HttpPut("cell")]
    public async Task<IActionResult> PutCell([FromBody] CellDto dto)
    {
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM_UNGUELTIG" });

        var (isAdmin, planBranches, name) = await GetPlanRechteAsync();
        if (!isAdmin)
        {
            // Heimatfiliale des MA am Datum (laufender FIX-M-Vertrag).
            var dt = datum.ToDateTime(TimeOnly.MinValue);
            var cpId = await _db.Employments.AsNoTracking()
                .Where(em => em.EmployeeId == dto.EmployeeId
                          && em.EmploymentModel == "FIX-M"
                          && em.ContractStartDate <= dt
                          && (em.ContractEndDate == null || em.ContractEndDate >= dt))
                .OrderByDescending(em => em.ContractStartDate)
                .Select(em => em.CompanyProfileId)
                .FirstOrDefaultAsync();
            if (!cpId.HasValue || !planBranches.Contains(cpId.Value))
                return StatusCode(403, new
                {
                    error = "KEIN_PLANRECHT",
                    message = "Kein Planungsrecht für die Filiale dieses MA (Filial-Tab «Unterzeichner» → Häkchen «Dienstplan»).",
                });
        }

        // Absenz-Kollision: Zelle mit Absenz ist gesperrt.
        bool absenz = await _db.Absences.AnyAsync(a =>
            a.EmployeeId == dto.EmployeeId && a.DateFrom <= datum && a.DateTo >= datum);
        if (absenz)
            return Conflict(new
            {
                error = "ABSENZ_GESPERRT",
                message = "An diesem Tag besteht eine Absenz — Planung nicht möglich (Absenzen im MA-Detail pflegen).",
            });

        var entry = await _db.ManagerDienstplanEntries
            .FirstOrDefaultAsync(p => p.EmployeeId == dto.EmployeeId && p.Datum == datum);
        var code = (dto.Code ?? "").Trim();
        if (string.IsNullOrEmpty(code))
        {
            if (entry != null) _db.ManagerDienstplanEntries.Remove(entry);
        }
        else
        {
            bool gueltig = await _db.DienstplanCodes.AnyAsync(c => c.Code == code && c.IsActive);
            if (!gueltig) return BadRequest(new { error = "CODE_UNBEKANNT" });
            if (entry == null)
            {
                entry = new ManagerDienstplanEntry { EmployeeId = dto.EmployeeId, Datum = datum };
                _db.ManagerDienstplanEntries.Add(entry);
            }
            entry.Code = code;
            entry.UpdatedAt = DateTime.Now;
            entry.UpdatedBy = name;
        }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Einmal-Import aus der alten Excel «Manager DP 2026.xlsx»
    //  (Walter-Vorgabe 08.08.2026). Struktur: 12 Monats-Sheets JAN…DEZ,
    //  Zeile «Datum» = Tag-Spalten, Filial-Blöcke (Zelle A = FILIALORT in
    //  Grossbuchstaben), darunter Namenszeilen mit Kürzeln pro Tag.
    //  Übernommen werden NUR Kürzel aus unserem dienstplan_code-Katalog
    //  (F/M/S/-/SK/SKM) — Absenz-Zeichen der Excel (#, K, *, …) werden
    //  übersprungen, weil Absenzen bei uns aus dem System kommen. Tage mit
    //  System-Absenz werden ebenfalls übersprungen (Absenz gewinnt).
    // ═════════════════════════════════════════════════════════════════════
    private static readonly Dictionary<string, int> SHEET_MONATE = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JAN"] = 1, ["FEB"] = 2, ["MAR"] = 3, ["APR"] = 4, ["MAI"] = 5, ["JUN"] = 6,
        ["JUL"] = 7, ["AUG"] = 8, ["SEP"] = 9, ["OKT"] = 10, ["NOV"] = 11, ["DEZ"] = 12,
    };

    [Authorize(Roles = "admin")]
    [HttpPost("import-excel")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ImportExcel([FromForm] IFormFile file, [FromQuery] int year = 2026, [FromQuery] bool dryRun = true)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "DATEI_FEHLT" });

        NPOI.SS.UserModel.IWorkbook wb;
        try
        {
            using var stream = file.OpenReadStream();
            wb = new NPOI.XSSF.UserModel.XSSFWorkbook(stream);
        }
        catch
        {
            return BadRequest(new { error = "DATEI_UNGUELTIG", message = "Datei konnte nicht als Excel (.xlsx) gelesen werden." });
        }

        // Roster: alle FIX-M-MA mit Vertrag im Jahr, Heimatfiliale = spätester Vertrag.
        var jahrFrom = new DateTime(year, 1, 1);
        var jahrTo   = new DateTime(year, 12, 31);
        var fixm = await _db.Employments.AsNoTracking()
            .Where(em => em.EmploymentModel == "FIX-M"
                      && em.CompanyProfileId != null
                      && em.ContractStartDate <= jahrTo
                      && (em.ContractEndDate == null || em.ContractEndDate >= jahrFrom)
                      && !em.Employee!.IsHidden && !em.Employee!.IsPayrollExcluded)
            .Select(em => new { em.EmployeeId, em.CompanyProfileId, em.ContractStartDate, em.Employee!.FirstName, em.Employee!.LastName })
            .ToListAsync();
        var roster = fixm.GroupBy(x => x.EmployeeId)
            .Select(g => g.OrderByDescending(x => x.ContractStartDate).First())
            .ToList();

        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.BranchName, c.City })
            .ToListAsync();
        var codes = await _db.DienstplanCodes.Where(c => c.IsActive).Select(c => c.Code).ToListAsync();
        var codeSet = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);

        var absences = await _db.Absences.AsNoTracking()
            .Where(a => a.DateFrom <= new DateOnly(year, 12, 31) && a.DateTo >= new DateOnly(year, 1, 1))
            .Select(a => new { a.EmployeeId, a.DateFrom, a.DateTo })
            .ToListAsync();

        try
        {
        // Globaler Namens-Fallback: die Excel-Blöcke decken sich nicht 1:1 mit
        // unseren Filialen («LANGENTHAL 2», «HENDSCHIKEN» im LENZBURG-Block) —
        // wenn der Name im Block-Roster nicht gefunden wird, über ALLE FIX-M
        // eindeutig matchen (Eintrag hängt ohnehin nur am MA, nicht an der Filiale).
        var alleMas = roster.Select(x => (x.EmployeeId, x.FirstName ?? "", x.LastName ?? "")).ToList();

        var eintraege = new Dictionary<(int empId, DateOnly datum), string>();
        var matched = new List<object>();
        var matchedNames = new HashSet<string>();   // «SHEET|BRANCHZEILE|NAME» einmalig melden
        var unmatched = new List<object>();
        var unmatchedNames = new HashSet<string>();
        var skippedCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int absenzGesperrt = 0;

        for (int s = 0; s < wb.NumberOfSheets; s++)
        {
            var sheet = wb.GetSheetAt(s);
            if (!SHEET_MONATE.TryGetValue(sheet.SheetName.Trim(), out var monat)) continue;
            int tageImMonat = DateTime.DaysInMonth(year, monat);

            // «Datum»-Zeile suchen → Spalte→Tag-Mapping.
            var tagSpalten = new Dictionary<int, int>();
            int datumRowIdx = -1;
            for (int r = 0; r <= Math.Min(sheet.LastRowNum, 10); r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                if (string.Equals(CellText(row.GetCell(0)), "Datum", StringComparison.OrdinalIgnoreCase))
                {
                    datumRowIdx = r;
                    for (int c = 1; c <= row.LastCellNum; c++)
                    {
                        var t = CellText(row.GetCell(c));
                        if (int.TryParse(t, out var tag) && tag >= 1 && tag <= 31)
                            tagSpalten[c] = tag;
                    }
                    break;
                }
            }
            if (datumRowIdx < 0 || tagSpalten.Count == 0) continue;

            object? curBranch = null;
            List<(int EmpId, string FirstName, string LastName)> branchMas = new();
            string curBranchLabel = "";

            for (int r = datumRowIdx + 2; r <= sheet.LastRowNum; r++)
            {
                var row = sheet.GetRow(r);
                if (row == null) continue;
                var nameCell = CellText(row.GetCell(0)).Trim();
                if (string.IsNullOrEmpty(nameCell)) continue;
                // Zahlen-/Datums-Müll in der Namensspalte überspringen.
                if (nameCell.All(ch => char.IsDigit(ch) || ch == '.' || ch == '-' || ch == ':' || ch == ' ')) continue;

                // Filial-Header? (Ortsname in GROSSBUCHSTABEN; bidirektional, damit
                // auch «LANGENTHAL 2» den Ort Langenthal trifft)
                var br = branches.FirstOrDefault(b =>
                    (!string.IsNullOrEmpty(b.City)
                        && (b.City.Contains(nameCell, StringComparison.OrdinalIgnoreCase)
                         || nameCell.Contains(b.City, StringComparison.OrdinalIgnoreCase)))
                    || (!string.IsNullOrEmpty(b.BranchName)
                        && (b.BranchName.Contains(nameCell, StringComparison.OrdinalIgnoreCase)
                         || nameCell.Contains(b.BranchName, StringComparison.OrdinalIgnoreCase))));
                if (br != null && nameCell == nameCell.ToUpperInvariant() && nameCell.Length >= 4)
                {
                    curBranch = br;
                    curBranchLabel = nameCell;
                    branchMas = roster.Where(x => x.CompanyProfileId == br.Id)
                        .Select(x => (x.EmployeeId, x.FirstName ?? "", x.LastName ?? "")).ToList();
                    continue;
                }
                if (curBranch == null) continue;   // Zeilen vor dem ersten Filial-Block (Supervisoren) überspringen

                // MA-Match: exakt → Präfix (≥3) → Tippfehler-Toleranz (Levenshtein ≤ 2);
                // erst im Block-Roster, dann eindeutig über alle FIX-M.
                var empId = MatchName(nameCell, branchMas, out var maName);
                if (empId == null) empId = MatchName(nameCell, alleMas, out maName);
                var meldeKey = $"{curBranchLabel}|{nameCell}";
                if (empId == null)
                {
                    if (unmatchedNames.Add(meldeKey))
                        unmatched.Add(new { name = nameCell, filiale = curBranchLabel });
                    continue;
                }
                if (matchedNames.Add(meldeKey))
                    matched.Add(new { name = nameCell, ma = maName, filiale = curBranchLabel });

                foreach (var (col, tag) in tagSpalten)
                {
                    if (tag > tageImMonat) continue;
                    var code = CellText(row.GetCell(col)).Trim();
                    if (string.IsNullOrEmpty(code)) continue;
                    if (!codeSet.Contains(code))
                    {
                        skippedCodes[code] = skippedCodes.GetValueOrDefault(code) + 1;
                        continue;
                    }
                    var datum = new DateOnly(year, monat, tag);
                    if (absences.Any(a => a.EmployeeId == empId.Value && a.DateFrom <= datum && a.DateTo >= datum))
                    {
                        absenzGesperrt++;
                        continue;
                    }
                    var kanonisch = codes.First(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase));
                    eintraege[(empId.Value, datum)] = kanonisch;
                }
            }
        }

        if (!dryRun && eintraege.Count > 0)
        {
            var (_, _, actorName) = await GetPlanRechteAsync();
            var von = new DateOnly(year, 1, 1);
            var bis = new DateOnly(year, 12, 31);
            var empIds = eintraege.Keys.Select(k => k.empId).Distinct().ToList();
            var existing = await _db.ManagerDienstplanEntries
                .Where(p => empIds.Contains(p.EmployeeId) && p.Datum >= von && p.Datum <= bis)
                .ToListAsync();
            var byKey = existing.ToDictionary(p => (p.EmployeeId, p.Datum));
            foreach (var ((empId, datum), code) in eintraege)
            {
                if (byKey.TryGetValue((empId, datum), out var entry))
                {
                    entry.Code = code;
                    entry.UpdatedAt = DateTime.Now;
                    entry.UpdatedBy = actorName;
                }
                else
                {
                    _db.ManagerDienstplanEntries.Add(new ManagerDienstplanEntry
                    {
                        EmployeeId = empId, Datum = datum, Code = code,
                        UpdatedAt = DateTime.Now, UpdatedBy = actorName,
                    });
                }
            }
            await _db.SaveChangesAsync();
        }

        return Ok(new
        {
            dryRun, year,
            eintraege = eintraege.Count,
            matched,
            unmatched,
            uebersprungeneKuerzel = skippedCodes.OrderByDescending(kv => kv.Value).Select(kv => new { kuerzel = kv.Key, anzahl = kv.Value }),
            absenzGesperrt,
        });
        }
        catch (Exception ex)
        {
            // Fehler transparent machen — «Analyse fehlgeschlagen» ohne Grund
            // hilft niemandem (Lehre 09.08.2026).
            return StatusCode(500, new { error = "IMPORT_FEHLER", message = $"Import-Fehler: {ex.Message}" });
        }
    }

    private static string CellText(NPOI.SS.UserModel.ICell? cell)
    {
        if (cell == null) return "";
        return cell.CellType switch
        {
            NPOI.SS.UserModel.CellType.String => cell.StringCellValue ?? "",
            NPOI.SS.UserModel.CellType.Numeric => cell.NumericCellValue == Math.Floor(cell.NumericCellValue)
                ? ((long)cell.NumericCellValue).ToString()
                : cell.NumericCellValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            NPOI.SS.UserModel.CellType.Formula => cell.CachedFormulaResultType == NPOI.SS.UserModel.CellType.String
                ? cell.StringCellValue ?? "" : "",
            _ => "",
        };
    }

    /// <summary>
    /// Von Walter bestätigte Excel-Spitznamen (09.08.2026): Excel-Name →
    /// Vorname + Nachnamen-Initial. Löst auch Mehrdeutigkeiten (zwei Meritas).
    /// </summary>
    private static readonly Dictionary<string, (string Vorname, string Initial)> EXCEL_ALIASES =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Lulu"]   = ("Ludmila", "S"),
            ["Sinthy"] = ("Sinthuja", "K"),
            ["Poja"]   = ("Pooja", "K"),
            ["Merita"] = ("Merita", "B"),
            ["Lita"]   = ("Merita", "R"),
            ["Sadis"]  = ("Nimalneethan", "S"),
        };

    /// <summary>Excel-Name (oft Kurzform: «Sinthy», «Xheva») → MA der Filiale.</summary>
    private static int? MatchName(string excelName, List<(int EmpId, string FirstName, string LastName)> mas, out string? maName)
    {
        maName = null;
        var n = excelName.Trim();
        // 0) bestätigte Aliase (Vorname + Nachnamen-Initial, eindeutig)
        if (EXCEL_ALIASES.TryGetValue(n, out var alias))
        {
            var ahit = mas.Where(m => string.Equals(m.FirstName, alias.Vorname, StringComparison.OrdinalIgnoreCase)
                                   && m.LastName.StartsWith(alias.Initial, StringComparison.OrdinalIgnoreCase)).ToList();
            if (ahit.Count == 1)
            {
                maName = $"{ahit[0].FirstName} {ahit[0].LastName}".Trim();
                return ahit[0].EmpId;
            }
        }
        // 1) exakt (Vorname)
        var hit = mas.Where(m => string.Equals(m.FirstName, n, StringComparison.OrdinalIgnoreCase)).ToList();
        // 2) Präfix in beide Richtungen (≥ 3 Zeichen; leere Vornamen ausschliessen —
        //    n.StartsWith("") wäre sonst für JEDEN true)
        if (hit.Count == 0 && n.Length >= 3)
            hit = mas.Where(m => m.FirstName.Length >= 3
                              && (m.FirstName.StartsWith(n, StringComparison.OrdinalIgnoreCase)
                               || n.StartsWith(m.FirstName, StringComparison.OrdinalIgnoreCase))).ToList();
        // 3) Tippfehler-Toleranz (Levenshtein ≤ 2 bei Namen ab 5 Zeichen)
        if (hit.Count == 0 && n.Length >= 5)
            hit = mas.Where(m => Levenshtein(m.FirstName.ToLowerInvariant(), n.ToLowerInvariant()) <= 2).ToList();
        if (hit.Count != 1) return null;   // 0 oder mehrdeutig → manuell klären
        maName = $"{hit[0].FirstName} {hit[0].LastName}".Trim();
        return hit[0].EmpId;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                   d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}

// Geteilte Daten-Records für JSON-Grid UND PDF (ManagerDienstplanPdfService).
public sealed record DpFilialeInfo(int Id, string? Code, string? Name, string? Kanton = null);
public sealed record DpFeiertagInfo(int CompanyProfileId, DateOnly Datum, string Bezeichnung);
public sealed record DpSchulferienInfo(int CompanyProfileId, DateOnly Von, DateOnly Bis, string Bezeichnung);
public sealed record DpAbsenzInfo(string Typ, DateOnly Von, DateOnly Bis);
public sealed record DpCodeInfo(string Code, string? Bezeichnung, string? Farbe);
public sealed record DpZeileInfo(
    int EmployeeId, string Vorname, string Nachname, int? CompanyProfileId,
    bool IstGf, bool Planbar,
    Dictionary<string, string> Zellen, List<DpAbsenzInfo> Absenzen);
