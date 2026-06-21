using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Globale, im Admin editierbare App-Einstellungen (Walter-Vorgabe 21.06.2026).
/// Erste Einstellung: Aufbewahrungsdauer der Stempelzeiten in Jahren
/// (min. 5 — gesetzlich nicht weiter herunterstellbar ohne Code-Freigabe).
/// </summary>
[Authorize(Roles = "admin")]
[ApiController]
[Route("api/app-settings")]
public class AppSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    public AppSettingsController(AppDbContext db) => _db = db;

    public record RetentionDto(int Years);

    /// <summary>Aktuelle Stempelzeiten-Aufbewahrung (Jahre). Default 5, wenn nichts gesetzt.</summary>
    [HttpGet("time-entry-retention")]
    public async Task<IActionResult> GetRetention()
    {
        var raw = await _db.AppSettings.AsNoTracking()
            .Where(s => s.Key == TimeEntryRetentionService.SettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync();
        var years = int.TryParse(raw, out var v) ? v : TimeEntryRetentionPolicy.MinRetentionYears;
        return Ok(new { years, min = TimeEntryRetentionPolicy.MinRetentionYears });
    }

    /// <summary>Stempelzeiten-Aufbewahrung setzen. Min. 5 Jahre (gesetzliche Untergrenze).</summary>
    [HttpPut("time-entry-retention")]
    public async Task<IActionResult> SetRetention([FromBody] RetentionDto dto)
    {
        if (dto == null || dto.Years < TimeEntryRetentionPolicy.MinRetentionYears)
            return BadRequest(new {
                error = "RETENTION_TOO_LOW",
                message = $"Die Aufbewahrung darf nicht unter {TimeEntryRetentionPolicy.MinRetentionYears} Jahren liegen."
            });
        if (dto.Years > 100)
            return BadRequest(new { error = "RETENTION_TOO_HIGH", message = "Bitte einen Wert ≤ 100 Jahre eingeben." });

        var setting = await _db.AppSettings
            .FirstOrDefaultAsync(s => s.Key == TimeEntryRetentionService.SettingKey);
        if (setting == null)
        {
            setting = new AppSetting { Key = TimeEntryRetentionService.SettingKey };
            _db.AppSettings.Add(setting);
        }
        setting.Value     = dto.Years.ToString();
        setting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return Ok(new { years = dto.Years });
    }
}
