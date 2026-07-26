using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Anonymer Austritts-Fragebogen (Walter 26.07.2026) — öffentliche Seite
/// <c>/kuendigung/</c>, ersetzt das frühere Google-Formular im QR der
/// Kündigungsbestätigung. Kein Mitarbeiter-Bezug; Filiale optional via QR
/// (?f=RestaurantCode), damit HR die Filiale kennt ohne den MA zu wissen.
/// </summary>
[ApiController]
[Route("api/exit-survey")]
public class ExitSurveyController : ControllerBase
{
    private readonly AppDbContext _db;

    /// <summary>Erlaubte Hauptgrund-Codes (max. 3 pro Antwort).</summary>
    public static readonly string[] ReasonCodes =
    [
        "ANDERER_JOB",
        "STUDIUM",
        "ZU_VIELE_STUNDEN",
        "ZU_WENIG_STUNDEN",
        "ARBEITSZEITEN",
        "GASTRONOMIE",
        "ENTWICKLUNG",
        "FAMILIE",
        "ATMOSPHAERE",
        "LOHN",
        "ANDERES",
    ];

    public ExitSurveyController(AppDbContext db) => _db = db;

    public class SubmitDto
    {
        public string[]? Reasons { get; set; }
        public string?   ReasonOther { get; set; }
        public string?   AtmosphereDetail { get; set; }
        public int?      Rating { get; set; }
        public string?   Comment { get; set; }
        /// <summary>RestaurantCode aus dem QR (?f=075) — Filiale, anonym.</summary>
        public string?   FilialeCode { get; set; }
        /// <summary>Alternativ: company_profile_id (?b=).</summary>
        public int?      CompanyProfileId { get; set; }
        /// <summary>Honeypot — muss leer bleiben.</summary>
        public string?   Website { get; set; }
    }

    /// <summary>
    /// Öffentliche Filial-Liste für den Fragebogen (nur Code + Anzeigename) —
    /// falls jemand ohne QR öffnet und die Filiale manuell wählen will.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("branches")]
    public async Task<IActionResult> Branches()
    {
        var list = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.RestaurantCode != null && c.RestaurantCode != "")
            .OrderBy(c => c.City ?? c.BranchName ?? c.CompanyName)
            .Select(c => new
            {
                code = c.RestaurantCode!,
                label = (c.RestaurantCode ?? "") + " "
                      + (c.City ?? c.BranchName ?? c.CompanyName ?? ""),
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Öffentliche Abgabe — anonym, ohne Login; Filiale optional.</summary>
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Website))
            return Ok(new { ok = true }); // Bot still beantworten, nichts speichern

        var reasons = (dto.Reasons ?? Array.Empty<string>())
            .Select(r => (r ?? "").Trim().ToUpperInvariant())
            .Where(r => ReasonCodes.Contains(r))
            .Distinct()
            .Take(3)
            .ToList();

        if (reasons.Count == 0 && string.IsNullOrWhiteSpace(dto.Comment)
            && dto.Rating is null && string.IsNullOrWhiteSpace(dto.ReasonOther))
            return BadRequest(new { error = "LEER", message = "Bitte mindestens einen Grund, eine Note oder einen Kommentar angeben." });

        if (dto.Rating is < 1 or > 6)
            return BadRequest(new { error = "RATING", message = "Die Note muss zwischen 1 und 6 liegen." });

        var cpId = await ResolveBranchIdAsync(dto.FilialeCode, dto.CompanyProfileId);

        var ipHash = HashIp(HttpContext.Connection.RemoteIpAddress?.ToString());
        if (ipHash != null)
        {
            var since = DateTime.Now.AddHours(-24);
            var n = await _db.ExitSurveyResponses.AsNoTracking()
                .CountAsync(x => x.IpHash == ipHash && x.CreatedAt >= since);
            if (n >= 8)
                return StatusCode(429, new { error = "RATE_LIMIT", message = "Zu viele Antworten von diesem Gerät — bitte später erneut versuchen." });
        }

        var row = new ExitSurveyResponse
        {
            CreatedAt = DateTime.Now,
            CompanyProfileId = cpId,
            ReasonsJson = JsonSerializer.Serialize(reasons),
            ReasonOther = Clip(dto.ReasonOther, 500),
            AtmosphereDetail = reasons.Contains("ATMOSPHAERE") ? Clip(dto.AtmosphereDetail, 2000) : null,
            Rating = dto.Rating,
            Comment = Clip(dto.Comment, 4000),
            IpHash = ipHash,
        };
        _db.ExitSurveyResponses.Add(row);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Klartext-Labels zu den Hauptgrund-Codes (HR-Ansicht).</summary>
    private static readonly Dictionary<string, string> ReasonLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ANDERER_JOB"] = "Andere Stelle im Fachgebiet",
        ["STUDIUM"] = "Studium",
        ["ZU_VIELE_STUNDEN"] = "Zu viele Stunden",
        ["ZU_WENIG_STUNDEN"] = "Zu wenig Stunden",
        ["ARBEITSZEITEN"] = "Arbeitszeiten / Verfügbarkeit",
        ["GASTRONOMIE"] = "Gastronomie nicht das Richtige",
        ["ENTWICKLUNG"] = "Keine Entwicklungsmöglichkeiten",
        ["FAMILIE"] = "Familiäre / nicht berufliche Gründe",
        ["ATMOSPHAERE"] = "Atmosphäre / Organisation",
        ["LOHN"] = "Gehalt",
        ["ANDERES"] = "Anderer Grund",
    };

    /// <summary>HR-Übersicht der anonymen Antworten (neueste zuerst), inkl. Filiale + Gründe.</summary>
    [Authorize(Roles = "admin,superuser")]
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 500);
        var raw = await (
            from x in _db.ExitSurveyResponses.AsNoTracking()
            join c in _db.CompanyProfiles.AsNoTracking() on x.CompanyProfileId equals c.Id into gj
            from c in gj.DefaultIfEmpty()
            orderby x.CreatedAt descending
            select new
            {
                x.Id,
                x.CreatedAt,
                x.CompanyProfileId,
                FilialeCode = c != null ? c.RestaurantCode : null,
                Filiale = c == null ? null
                    : ((c.RestaurantCode ?? "") + " " + (c.City ?? c.BranchName ?? c.CompanyName ?? "")).Trim(),
                x.ReasonsJson,
                x.ReasonOther,
                x.AtmosphereDetail,
                x.Rating,
                x.Comment,
            }
        ).Take(take).ToListAsync();

        // Gründe als Klartext-Array mitgeben — HR muss sie ohne JSON lesen können
        // (Walter 26.07.2026). Anonym = kein MA-Name, Gründe/Bemerkung bleiben sichtbar.
        var rows = raw.Select(x =>
        {
            var codes = ParseReasonCodes(x.ReasonsJson);
            var labels = codes
                .Select(c => ReasonLabels.TryGetValue(c, out var lbl) ? lbl : c)
                .ToList();
            if (!string.IsNullOrWhiteSpace(x.ReasonOther))
                labels.Add(x.ReasonOther.Trim());
            return new
            {
                id = x.Id,
                createdAt = x.CreatedAt,
                companyProfileId = x.CompanyProfileId,
                filialeCode = x.FilialeCode,
                filiale = x.Filiale,
                reasonsJson = x.ReasonsJson,
                reasons = labels,
                reasonCodes = codes,
                reasonOther = x.ReasonOther,
                atmosphereDetail = x.AtmosphereDetail,
                rating = x.Rating,
                comment = x.Comment,
            };
        }).ToList();
        return Ok(rows);
    }

    private static List<string> ParseReasonCodes(string? reasonsJson)
    {
        if (string.IsNullOrWhiteSpace(reasonsJson)) return new List<string>();
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(reasonsJson);
            if (arr == null || arr.Length == 0) return new List<string>();
            return arr
                .Select(r => (r ?? "").Trim().ToUpperInvariant())
                .Where(r => r.Length > 0)
                .Distinct()
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<int?> ResolveBranchIdAsync(string? filialeCode, int? companyProfileId)
    {
        if (companyProfileId is > 0)
        {
            var ok = await _db.CompanyProfiles.AsNoTracking()
                .AnyAsync(c => c.Id == companyProfileId.Value);
            if (ok) return companyProfileId.Value;
        }
        var code = (filialeCode ?? "").Trim();
        if (code.Length == 0) return null;
        return await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.RestaurantCode == code)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync();
    }

    private static string? Clip(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var t = s.Trim();
        return t.Length <= max ? t : t[..max];
    }

    private static string? HashIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("exit|" + ip.Trim()));
        return Convert.ToHexString(hash).ToLowerInvariant()[..32];
    }
}
