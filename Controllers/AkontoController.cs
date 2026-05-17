using System.Globalization;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Akonto-Lohn-Lauf — Vorschau und Erfassung der Vorauszahlung pro Monat.
///
/// Vorschau (GET /api/payroll/akonto/preview) berechnet pro MA der Filiale die
/// Akonto-Höhe und liefert die Liste ohne zu schreiben — Walter kann sie
/// prüfen und mit der Realität abgleichen, bevor er commitet.
///
/// Commit (POST /api/payroll/akonto/commit) schreibt die akonto_zahlung-
/// Datensätze für die Periode (Status BERECHNET). Idempotent: bestehende
/// BERECHNET-Datensätze werden überschrieben; falls einzelne Datensätze
/// bereits AUSBEZAHLT sind (DTA gelaufen), bricht der Commit mit Hinweis ab.
///
/// Die DTA-Generierung (pain.001) ist Phase 3d — kommt separat.
///
/// Siehe AKONTO-LOHN-PLAN.md.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/payroll/akonto")]
public class AkontoController : ControllerBase
{
    private readonly AppDbContext       _db;
    private readonly AkontoLaufService  _service;
    private readonly ILogger<AkontoController> _log;

    public AkontoController(AppDbContext db, AkontoLaufService service,
                            ILogger<AkontoController> log)
    {
        _db      = db;
        _service = service;
        _log     = log;
    }

    /// <summary>
    /// Vorschau ohne DB-Schreiben. Stichtag default = heute (auf Periode
    /// geclipped). companyProfileId + year + month sind Pflicht.
    /// </summary>
    [HttpGet("preview")]
    public async Task<IActionResult> Preview(
        [FromQuery] int    companyProfileId,
        [FromQuery] int    year,
        [FromQuery] int    month,
        [FromQuery] string? stichtag = null)
    {
        if (companyProfileId <= 0) return BadRequest(new { error = "companyProfileId fehlt." });
        if (year < 2020 || year > 2099)  return BadRequest(new { error = "Jahr ausserhalb des erlaubten Bereichs." });
        if (month < 1 || month > 12)     return BadRequest(new { error = "Monat ausserhalb 1–12." });

        DateOnly st;
        if (!string.IsNullOrWhiteSpace(stichtag)
            && DateOnly.TryParseExact(stichtag, "yyyy-MM-dd",
                                      CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            st = parsed;
        }
        else
        {
            st = DateOnly.FromDateTime(DateTime.Today);
        }

        try
        {
            var result = await _service.PreviewAsync(companyProfileId, year, month, st);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[AkontoController] Vorschau fehlgeschlagen für Filiale {CP} {Year}-{Month}",
                          companyProfileId, year, month);
            return StatusCode(500, new { error = "Vorschau-Berechnung fehlgeschlagen: " + ex.Message });
        }
    }

    /// <summary>
    /// Commit-Body: nur die Eckdaten — die eigentliche Berechnung läuft im
    /// Service nochmal frisch (kein Vertrauen auf vom Client gelieferte Werte).
    /// </summary>
    public record CommitRequest(int CompanyProfileId, int Year, int Month, string Stichtag);

    /// <summary>
    /// Schreibt die akonto_zahlung-Datensätze. Idempotent gegen bestehende
    /// BERECHNET-Datensätze (werden überschrieben). Bricht ab, wenn auch nur
    /// ein Datensatz schon AUSBEZAHLT ist — dann müsste vorher die Akonto-
    /// Auszahlung storniert werden.
    /// </summary>
    [HttpPost("commit")]
    public async Task<IActionResult> Commit([FromBody] CommitRequest req)
    {
        if (req.CompanyProfileId <= 0) return BadRequest(new { error = "companyProfileId fehlt." });
        if (req.Year < 2020 || req.Year > 2099) return BadRequest(new { error = "Jahr ausserhalb des erlaubten Bereichs." });
        if (req.Month < 1 || req.Month > 12)    return BadRequest(new { error = "Monat ausserhalb 1–12." });
        if (!DateOnly.TryParseExact(req.Stichtag, "yyyy-MM-dd",
                                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var st))
            return BadRequest(new { error = "Stichtag-Format: JJJJ-MM-TT." });

        // Sicherheits-Check: gibt es schon AUSBEZAHLT-Datensätze für diese Periode?
        // Wenn ja → blockieren (Walter müsste vorher stornieren).
        var existingPaid = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == req.CompanyProfileId
                     && z.PeriodYear  == req.Year
                     && z.PeriodMonth == req.Month
                     && z.Status      == "AUSBEZAHLT")
            .CountAsync();
        if (existingPaid > 0)
            return StatusCode(409, new {
                error = $"{existingPaid} Akonto-Datensatz/-Datensätze sind bereits AUSBEZAHLT "
                      + "(DTA gelaufen). Bitte zuerst stornieren, dann neu erfassen."
            });

        AkontoLaufService.AkontoVorschauResponse data;
        try
        {
            data = await _service.PreviewAsync(req.CompanyProfileId, req.Year, req.Month, st);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        // Akonto-Termin (Auszahlungsdatum) — wenn nicht konfiguriert, Stichtag als Fallback.
        DateOnly payoutDate = !string.IsNullOrWhiteSpace(data.PayoutDate)
            && DateOnly.TryParseExact(data.PayoutDate, "yyyy-MM-dd",
                                      CultureInfo.InvariantCulture, DateTimeStyles.None, out var pd)
            ? pd : st;

        // Bestehende BERECHNET-Datensätze für die Periode löschen, dann neu schreiben.
        var oldRows = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == req.CompanyProfileId
                     && z.PeriodYear  == req.Year
                     && z.PeriodMonth == req.Month
                     && z.Status      == "BERECHNET")
            .ToListAsync();
        _db.AkontoZahlungen.RemoveRange(oldRows);

        int created = 0;
        decimal totalNetto = 0m;
        foreach (var r in data.Rows.Where(r => r.IsEligible && r.NettoAkonto > 0m))
        {
            _db.AkontoZahlungen.Add(new AkontoZahlung
            {
                EmployeeId          = r.EmployeeId,
                CompanyProfileId    = req.CompanyProfileId,
                PeriodYear          = req.Year,
                PeriodMonth         = req.Month,
                PayoutDate          = payoutDate,
                GeschaetzterBrutto  = r.GeschaetzterBrutto,
                FeriengeldAnteil    = 0m,
                GeschaetzteAbzuege  = r.GeschaetzteAbzuege,
                PfaendungAbzug      = r.PfaendungAbzug,
                NettoAkonto         = r.NettoAkonto,
                Status              = "BERECHNET",
                DtaRunId            = null,
                CreatedAt           = DateTime.UtcNow,
                UpdatedAt           = DateTime.UtcNow,
            });
            created++;
            totalNetto += r.NettoAkonto;
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("[AkontoController] Commit Filiale={CP} {Year}-{Month}: {Created} Datensätze, "
                          + "Total Netto CHF {Total}", req.CompanyProfileId, req.Year, req.Month, created, totalNetto);

        return Ok(new {
            created,
            overwritten = oldRows.Count,
            totalNetto,
            payoutDate  = payoutDate.ToString("yyyy-MM-dd"),
        });
    }

    /// <summary>
    /// Liste der erfassten Akonto-Datensätze für eine Periode (für die
    /// Status-Anzeige in der UI: schon berechnet / ausbezahlt).
    /// </summary>
    [HttpGet("list")]
    public async Task<IActionResult> List(
        [FromQuery] int companyProfileId,
        [FromQuery] int year,
        [FromQuery] int month)
    {
        var rows = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == companyProfileId
                     && z.PeriodYear  == year
                     && z.PeriodMonth == month)
            .Join(_db.Employees, z => z.EmployeeId, e => e.Id, (z, e) => new {
                z.Id, z.EmployeeId, e.EmployeeNumber, e.FirstName, e.LastName,
                z.GeschaetzterBrutto, z.GeschaetzteAbzuege, z.PfaendungAbzug,
                z.NettoAkonto, z.Status, z.DtaRunId,
                PayoutDate = z.PayoutDate.ToString("yyyy-MM-dd"),
            })
            .OrderByDescending(r => r.NettoAkonto)
            .ToListAsync();
        return Ok(rows);
    }
}
