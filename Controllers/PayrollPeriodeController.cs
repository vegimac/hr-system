using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/payroll-perioden")]
public class PayrollPeriodeController : ControllerBase
{
    private readonly AppDbContext _db;

    public PayrollPeriodeController(AppDbContext db)
    {
        _db = db;
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PERIODEN-KONFIGURATION
    // ══════════════════════════════════════════════════════════════════════════

    // GET /api/payroll-perioden/config?companyProfileId=X
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig([FromQuery] int companyProfileId)
    {
        var cfg = await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == companyProfileId)
            .OrderByDescending(c => c.ValidFromYear)
            .ThenByDescending(c => c.ValidFromMonth)
            .FirstOrDefaultAsync();

        if (cfg is null)
            return Ok(null);

        return Ok(new {
            cfg.Id,
            cfg.CompanyProfileId,
            cfg.FromDay,
            cfg.ToDay,
            cfg.ValidFromYear,
            cfg.ValidFromMonth,
            cfg.IsLocked,
            cfg.CreatedAt
        });
    }

    // GET /api/payroll-perioden/config/all?companyProfileId=X  – alle Konfigs (Historie)
    [HttpGet("config/all")]
    public async Task<IActionResult> GetAllConfigs([FromQuery] int companyProfileId)
    {
        var cfgs = await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == companyProfileId)
            .OrderByDescending(c => c.ValidFromYear)
            .ThenByDescending(c => c.ValidFromMonth)
            .Select(c => new {
                c.Id, c.CompanyProfileId, c.FromDay, c.ToDay,
                c.ValidFromYear, c.ValidFromMonth, c.IsLocked, c.CreatedAt
            })
            .ToListAsync();

        return Ok(cfgs);
    }

    // POST /api/payroll-perioden/config  – neue Konfiguration anlegen (nur wenn aktuelle nicht gesperrt)
    [HttpPost("config")]
    public async Task<IActionResult> CreateConfig([FromBody] CreatePeriodeConfigDto dto)
    {
        // Prüfe ob bestehende Config gesperrt ist
        var existing = await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == dto.CompanyProfileId
                     && c.ValidFromYear == dto.ValidFromYear
                     && c.ValidFromMonth == dto.ValidFromMonth)
            .FirstOrDefaultAsync();

        if (existing is not null)
            return Conflict("Eine Konfiguration für dieses Jahr/Monat existiert bereits.");

        // Prüfe ob aktuelle Config gesperrt und neue Werte sich unterscheiden
        var current = await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == dto.CompanyProfileId)
            .OrderByDescending(c => c.ValidFromYear)
            .ThenByDescending(c => c.ValidFromMonth)
            .FirstOrDefaultAsync();

        if (current is not null && current.IsLocked
            && current.FromDay == dto.FromDay && current.ToDay == dto.ToDay)
            return BadRequest("Die aktuelle Konfiguration ist identisch und gesperrt. Keine Änderung nötig.");

        var cfg = new PayrollPeriodeConfig
        {
            CompanyProfileId = dto.CompanyProfileId,
            FromDay          = dto.FromDay,
            ToDay            = dto.ToDay,
            ValidFromYear    = dto.ValidFromYear,
            ValidFromMonth   = dto.ValidFromMonth,
            IsLocked         = false
        };
        _db.PayrollPeriodeConfigs.Add(cfg);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetConfig), new { companyProfileId = dto.CompanyProfileId },
            new { cfg.Id, cfg.FromDay, cfg.ToDay, cfg.ValidFromYear, cfg.ValidFromMonth, cfg.IsLocked });
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PERIODEN  (konkrete Lohnperioden)
    // ══════════════════════════════════════════════════════════════════════════

    // GET /api/payroll-perioden?companyProfileId=X&year=Y
    [HttpGet]
    public async Task<IActionResult> GetPerioden(
        [FromQuery] int companyProfileId,
        [FromQuery] int? year)
    {
        var q = _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == companyProfileId);

        if (year.HasValue)
            q = q.Where(p => p.Year == year.Value);

        var list = await q
            .OrderByDescending(p => p.Year)
            .ThenByDescending(p => p.Month)
            .Select(p => new {
                p.Id, p.CompanyProfileId, p.ConfigId,
                p.Year, p.Month, p.Label,
                PeriodFrom = p.PeriodFrom.ToString("yyyy-MM-dd"),
                PeriodTo   = p.PeriodTo.ToString("yyyy-MM-dd"),
                p.IsTransition, p.Status,
                p.AbgeschlossenAm, p.AbgeschlossenVon,
                p.CreatedAt,
                SnapshotCount = p.Snapshots.Count,
                FinalCount    = p.Snapshots.Count(s => s.IsFinal)
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET /api/payroll-perioden/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPeriode(int id)
    {
        var p = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (p is null) return NotFound();

        return Ok(new {
            p.Id, p.CompanyProfileId, p.ConfigId,
            p.Year, p.Month, p.Label,
            PeriodFrom = p.PeriodFrom.ToString("yyyy-MM-dd"),
            PeriodTo   = p.PeriodTo.ToString("yyyy-MM-dd"),
            p.IsTransition, p.Status,
            p.AbgeschlossenAm, p.AbgeschlossenVon,
            p.CreatedAt,
            p.PdfFooterText,
            SnapshotCount = p.Snapshots.Count,
            FinalCount    = p.Snapshots.Count(s => s.IsFinal)
        });
    }

    // GET /api/payroll-perioden/current?companyProfileId=X&year=Y&month=M
    // Gibt die Periode für den angegebenen Jahr/Monat zurück (oder null wenn nicht angelegt)
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentPeriode(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var p = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .Where(p => p.CompanyProfileId == companyProfileId
                     && p.Year == year
                     && p.Month == month
                     && !p.IsTransition)
            .FirstOrDefaultAsync();

        if (p is null) return Ok(null);

        return Ok(new {
            p.Id, p.CompanyProfileId, p.ConfigId,
            p.Year, p.Month, p.Label,
            PeriodFrom = p.PeriodFrom.ToString("yyyy-MM-dd"),
            PeriodTo   = p.PeriodTo.ToString("yyyy-MM-dd"),
            p.IsTransition, p.Status,
            p.AbgeschlossenAm, p.AbgeschlossenVon,
            p.CreatedAt,
            p.PdfFooterText,
            SnapshotCount = p.Snapshots.Count,
            FinalCount    = p.Snapshots.Count(s => s.IsFinal)
        });
    }

    // POST /api/payroll-perioden  – neue Periode anlegen (oder öffnen)
    [HttpPost]
    public async Task<IActionResult> CreatePeriode([FromBody] CreatePeriodeDto dto)
    {
        // Doppel-Check: existiert bereits eine normale Periode für diesen Monat?
        var existing = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == dto.CompanyProfileId
                                   && p.Year  == dto.Year
                                   && p.Month == dto.Month
                                   && !p.IsTransition);
        if (existing is not null)
            return Conflict(new { message = $"Periode {dto.Month}/{dto.Year} existiert bereits.", id = existing.Id });

        // Aktuelle Config laden und ggf. sperren
        var cfg = await _db.PayrollPeriodeConfigs
            .Where(c => c.CompanyProfileId == dto.CompanyProfileId)
            .OrderByDescending(c => c.ValidFromYear)
            .ThenByDescending(c => c.ValidFromMonth)
            .FirstOrDefaultAsync();

        // Fallback: CompanyProfile.PayrollPeriodStartDay (z.B. 21) → fromDay=21, toDay=20
        // Erst danach: generischer Standard 1–31
        if (cfg is null)
        {
            var cpFallback = await _db.CompanyProfiles.FindAsync(dto.CompanyProfileId);
            int sd = cpFallback?.PayrollPeriodStartDay ?? 1;
            // nur als Default-Config nehmen (nicht sperren)
            cfg = new PayrollPeriodeConfig
            {
                CompanyProfileId = dto.CompanyProfileId,
                FromDay          = sd <= 1 ? 1 : sd,
                ToDay            = sd <= 1 ? 31 : sd - 1,
                ValidFromYear    = dto.Year,
                ValidFromMonth   = 1,
                IsLocked         = false
            };
            _db.PayrollPeriodeConfigs.Add(cfg);
            // sofort speichern damit cfg.Id vergeben wird
            await _db.SaveChangesAsync();
        }

        int fromDay = cfg.FromDay;
        int toDay   = cfg.ToDay;

        // Perioden-Datumsgrenzen berechnen
        var (periodFrom, periodTo) = CalcPeriodDates(fromDay, dto.Year, dto.Month);

        var periode = new PayrollPeriode
        {
            CompanyProfileId = dto.CompanyProfileId,
            ConfigId         = cfg?.Id,
            Year             = dto.Year,
            Month            = dto.Month,
            PeriodFrom       = periodFrom,
            PeriodTo         = periodTo,
            Label            = dto.Label ?? FormatLabel(dto.Year, dto.Month),
            IsTransition     = false,
            Status           = "offen"
        };
        _db.PayrollPerioden.Add(periode);

        // Config sperren sobald erste Periode angelegt wird
        if (cfg is not null && !cfg.IsLocked)
        {
            cfg.IsLocked = true;
        }

        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetPeriode), new { id = periode.Id },
            new {
                periode.Id, periode.Year, periode.Month, periode.Label,
                PeriodFrom = periode.PeriodFrom.ToString("yyyy-MM-dd"),
                PeriodTo   = periode.PeriodTo.ToString("yyyy-MM-dd"),
                periode.Status
            });
    }

    // POST /api/payroll-perioden/{id}/abschliessen
    // Schliesst die Periode ab: alle Snapshots werden IsFinal=true, keine Korrekturen mehr möglich
    // PATCH /api/payroll-perioden/{id}/bemerkung – Footer-Text der Periode setzen
    public class BemerkungDto { public string? Text { get; set; } }

    [HttpPatch("{id}/bemerkung")]
    public async Task<IActionResult> UpdateBemerkung(int id, [FromBody] BemerkungDto dto)
    {
        var periode = await _db.PayrollPerioden.FindAsync(id);
        if (periode is null) return NotFound("Periode nicht gefunden.");
        periode.PdfFooterText = string.IsNullOrWhiteSpace(dto.Text) ? null : dto.Text.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { periodeId = id, pdfFooterText = periode.PdfFooterText });
    }

    [HttpPost("{id}/abschliessen")]
    public async Task<IActionResult> AbschliessePeriode(int id, [FromBody] AbschliessenDto dto)
    {
        var periode = await _db.PayrollPerioden
            .Include(p => p.Snapshots)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (periode is null) return NotFound("Periode nicht gefunden.");
        if (periode.Status == "abgeschlossen")
            return Conflict("Periode ist bereits abgeschlossen.");
        if (periode.Snapshots.Count == 0)
            return BadRequest("Keine bestätigten Lohnzettel vorhanden. Bitte zuerst alle Löhne bestätigen.");

        // Alle Snapshots finalisieren
        foreach (var snap in periode.Snapshots)
        {
            snap.IsFinal   = true;
            snap.UpdatedAt = DateTime.UtcNow;
        }

        periode.Status           = "abgeschlossen";
        periode.AbgeschlossenAm  = DateTime.UtcNow;
        periode.AbgeschlossenVon = dto.UserId;

        await _db.SaveChangesAsync();

        return Ok(new {
            message    = $"Periode '{periode.Label}' wurde abgeschlossen. {periode.Snapshots.Count} Lohnzettel finalisiert.",
            periodeId  = periode.Id,
            finalCount = periode.Snapshots.Count
        });
    }

    // GET /api/payroll-perioden/{id}/snapshots  – alle Snapshots einer Periode
    [HttpGet("{id}/snapshots")]
    public async Task<IActionResult> GetSnapshots(int id)
    {
        var snaps = await _db.PayrollSnapshots
            .Include(s => s.Employee)
            .Where(s => s.PayrollPeriodeId == id)
            .OrderBy(s => s.Employee!.LastName)
            .ThenBy(s => s.Employee!.FirstName)
            .Select(s => new {
                s.Id,
                s.EmployeeId,
                Name = s.Employee == null ? "" : s.Employee.LastName + " " + s.Employee.FirstName,
                s.Brutto,
                s.Netto,
                s.SvBasisAhv,
                s.SvBasisBvg,
                s.QstBetrag,
                s.ThirteenthAccumulated,
                s.FerienGeldSaldo,
                s.IsFinal,
                s.CreatedAt,
                s.UpdatedAt
            })
            .ToListAsync();

        return Ok(snaps);
    }

    // GET /api/payroll-perioden/jahresausweis?companyProfileId=X&year=Y&employeeId=Z
    // Aggregiert alle Snapshots eines Mitarbeitenden für den Lohnausweis
    [HttpGet("jahresausweis")]
    public async Task<IActionResult> GetJahresausweis(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int employeeId)
    {
        var snaps = await _db.PayrollSnapshots
            .Include(s => s.Periode)
            .Where(s => s.CompanyProfileId == companyProfileId
                     && s.EmployeeId == employeeId
                     && s.Periode!.Year == year
                     && s.IsFinal)
            .OrderBy(s => s.Periode!.Month)
            .ToListAsync();

        if (!snaps.Any())
            return Ok(new { year, employeeId, message = "Keine finalisierten Perioden gefunden.", perioden = new object[0] });

        var result = new {
            year,
            employeeId,
            companyProfileId,
            totalBrutto               = snaps.Sum(s => s.Brutto),
            totalNetto                = snaps.Sum(s => s.Netto),
            totalSvBasisAhv           = snaps.Sum(s => s.SvBasisAhv),
            totalSvBasisBvg           = snaps.Sum(s => s.SvBasisBvg),
            totalQstBetrag            = snaps.Sum(s => s.QstBetrag),
            thirteenthAccumulatedDez  = snaps.OrderByDescending(s => s.Periode!.Month).First().ThirteenthAccumulated,
            ferienGeldSaldoDez        = snaps.OrderByDescending(s => s.Periode!.Month).First().FerienGeldSaldo,
            perioden = snaps.Select(s => new {
                periodeId = s.PayrollPeriodeId,
                month     = s.Periode!.Month,
                label     = s.Periode.Label,
                s.Brutto, s.Netto, s.SvBasisAhv, s.SvBasisBvg, s.QstBetrag,
                s.ThirteenthAccumulated, s.FerienGeldSaldo
            })
        };

        return Ok(result);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  HELPER
    // ══════════════════════════════════════════════════════════════════════════

    private static (DateOnly from, DateOnly to) CalcPeriodDates(int startDay, int year, int month)
    {
        if (startDay <= 1)
        {
            var from = new DateOnly(year, month, 1);
            var to   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            return (from, to);
        }
        // z.B. startDay=21: Periode 21.02–20.03 für Auszahlung März
        var toDate   = new DateOnly(year, month, startDay - 1);
        int prevYear = month == 1 ? year - 1 : year;
        int prevMonth = month == 1 ? 12 : month - 1;
        int clampedStart = Math.Min(startDay, DateTime.DaysInMonth(prevYear, prevMonth));
        var fromDate = new DateOnly(prevYear, prevMonth, clampedStart);
        return (fromDate, toDate);
    }

    private static readonly string[] MonthNames = {
        "", "Januar", "Februar", "März", "April", "Mai", "Juni",
        "Juli", "August", "September", "Oktober", "November", "Dezember"
    };

    private static string FormatLabel(int year, int month)
        => $"{MonthNames[month]} {year}";
}

// ── DTOs ──────────────────────────────────────────────────────────────────────

public record CreatePeriodeConfigDto(
    int CompanyProfileId,
    int FromDay,
    int ToDay,
    int ValidFromYear,
    int ValidFromMonth);

public record CreatePeriodeDto(
    int CompanyProfileId,
    int Year,
    int Month,
    string? Label);

public record AbschliessenDto(int UserId);
