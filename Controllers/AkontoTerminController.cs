using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace HrSystem.Controllers;

/// <summary>
/// Akonto-Auszahlungstermine pro Filiale, Jahr und Monat (Akonto-Lohn-Modell).
///
/// Die Lohnperiode ist immer der Kalendermonat. Der Akonto-Termin ist das
/// tatsächliche Auszahlungsdatum der Akonto-Vorauszahlung — pro Monat einzeln
/// hinterlegt, weil ein fixer Tag (z. B. „immer der 23.") an Wochenenden und
/// Feiertagen scheitert. Das Frontend füllt ein Jahr mit einem Default-Tag
/// vor (Wochenend-verschoben) und Walter korrigiert die Ausreisser von Hand.
///
/// Siehe AKONTO-LOHN-PLAN.md, Abschnitt 4.1.
/// </summary>
[ApiController]
[Route("api/akonto-termine")]
public class AkontoTerminController : ControllerBase
{
    private readonly AppDbContext _db;
    public AkontoTerminController(AppDbContext db) => _db = db;

    // GET /api/akonto-termine?companyProfileId=X&year=Y
    // Liefert die hinterlegten Akonto-Termine einer Filiale für ein Jahr
    // (0–12 Einträge). Leere Liste = noch nichts hinterlegt.
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int companyProfileId, [FromQuery] int year)
    {
        var list = await _db.AkontoTermine
            .Where(t => t.CompanyProfileId == companyProfileId && t.Year == year)
            .OrderBy(t => t.Month)
            .Select(t => new
            {
                t.Id,
                t.Month,
                PayoutDate = t.PayoutDate.ToString("yyyy-MM-dd")
            })
            .ToListAsync();

        return Ok(list);
    }

    // POST /api/akonto-termine/save
    // Upsert: pro übergebenem Monat wird der bestehende Termin aktualisiert
    // oder ein neuer angelegt. Monate, die nicht im DTO stehen, bleiben
    // unverändert.
    [HttpPost("save")]
    public async Task<IActionResult> Save([FromBody] AkontoTermineSaveDto dto)
    {
        if (dto.CompanyProfileId <= 0)
            return BadRequest(new { error = "companyProfileId fehlt." });
        if (dto.Year < 2024 || dto.Year > 2099)
            return BadRequest(new { error = "Jahr ungültig (erwartet 2024–2099)." });
        if (dto.Termine == null || dto.Termine.Count == 0)
            return BadRequest(new { error = "Keine Termine übergeben." });

        var branchExists = await _db.CompanyProfiles.AnyAsync(c => c.Id == dto.CompanyProfileId);
        if (!branchExists)
            return NotFound(new { error = $"Filiale {dto.CompanyProfileId} nicht gefunden." });

        // Bestehende Termine des Jahres für den Upsert laden.
        var existing = await _db.AkontoTermine
            .Where(t => t.CompanyProfileId == dto.CompanyProfileId && t.Year == dto.Year)
            .ToListAsync();

        int saved = 0;
        foreach (var e in dto.Termine)
        {
            if (e.Month < 1 || e.Month > 12)
                return BadRequest(new { error = $"Monat {e.Month} ungültig." });
            if (!DateOnly.TryParseExact(e.PayoutDate, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var payout))
                return BadRequest(new { error = $"Datum '{e.PayoutDate}' (Monat {e.Month}) ungueltig - erwartet JJJJ-MM-TT." });

            var row = existing.FirstOrDefault(x => x.Month == e.Month);
            if (row == null)
            {
                _db.AkontoTermine.Add(new AkontoTermin
                {
                    CompanyProfileId = dto.CompanyProfileId,
                    Year             = dto.Year,
                    Month            = e.Month,
                    PayoutDate       = payout,
                    CreatedAt        = DateTime.UtcNow,
                    UpdatedAt        = DateTime.UtcNow
                });
            }
            else
            {
                row.PayoutDate = payout;
                row.UpdatedAt  = DateTime.UtcNow;
            }
            saved++;
        }

        await _db.SaveChangesAsync();
        return Ok(new { success = true, count = saved });
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────────
public record AkontoTerminEintragDto(int Month, string PayoutDate);
public record AkontoTermineSaveDto(int CompanyProfileId, int Year, List<AkontoTerminEintragDto> Termine);
