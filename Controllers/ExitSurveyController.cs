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
/// <c>/kuendigung/</c>. Kein Mitarbeiter-Bezug; Filiale optional via QR
/// (?f=RestaurantCode).
/// </summary>
[ApiController]
[Route("api/exit-survey")]
public class ExitSurveyController : ControllerBase
{
    private readonly AppDbContext _db;

    /// <summary>Frage 1 — Entscheid (max. 3).</summary>
    public static readonly string[] ReasonCodes =
    [
        "STARTE_NEUES",
        "SCHULE_PLAENE",
        "WENIGER_EINSAETZE",
        "MEHR_EINSAETZE",
        "ARBEIT_PASST_NICHT",
        "ETWAS_ANDERES",
    ];

    /// <summary>Frage 2 — JA / NEIN.</summary>
    public static readonly string[] ImproveAnswers = ["JA", "NEIN"];

    /// <summary>Frage 2 Themen (nur bei JA, Mehrfachauswahl).</summary>
    public static readonly string[] ImproveThemeCodes =
    [
        "FUEHRUNG",
        "TEAMGEFUEHL",
        "PLANUNG_ORG",
        "ARBEITSZEITEN",
        "UNTERSTUETZUNG",
        "ENTWICKLUNG",
        "LOHN_BEDINGUNGEN",
        "THEMA_ANDERES",
    ];

    public ExitSurveyController(AppDbContext db) => _db = db;

    public class SubmitDto
    {
        public string[]? Reasons { get; set; }
        public string?   ImproveAnswer { get; set; }
        public string[]? ImproveThemes { get; set; }
        public string?   ReasonOther { get; set; }
        public string?   AtmosphereDetail { get; set; }
        public int?      Rating { get; set; }
        public string?   Comment { get; set; }
        public string?   FilialeCode { get; set; }
        public int?      CompanyProfileId { get; set; }
        public string?   Website { get; set; }
    }

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

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] SubmitDto dto)
    {
        if (!string.IsNullOrWhiteSpace(dto.Website))
            return Ok(new { ok = true });

        var reasons = (dto.Reasons ?? Array.Empty<string>())
            .Select(r => (r ?? "").Trim().ToUpperInvariant())
            .Where(r => ReasonCodes.Contains(r))
            .Distinct()
            .Take(3)
            .ToList();

        var improve = (dto.ImproveAnswer ?? "").Trim().ToUpperInvariant();
        if (improve.Length > 0 && !ImproveAnswers.Contains(improve))
            return BadRequest(new { error = "IMPROVE", message = "Ungültige Antwort bei Frage 2." });
        if (improve.Length == 0) improve = "";

        var themes = (dto.ImproveThemes ?? Array.Empty<string>())
            .Select(r => (r ?? "").Trim().ToUpperInvariant())
            .Where(r => ImproveThemeCodes.Contains(r))
            .Distinct()
            .ToList();
        if (improve != "JA") themes.Clear();

        if (reasons.Count == 0 && improve.Length == 0 && string.IsNullOrWhiteSpace(dto.Comment)
            && dto.Rating is null && string.IsNullOrWhiteSpace(dto.ReasonOther))
            return BadRequest(new { error = "LEER", message = "Wähl etwas aus oder schreib uns kurz etwas." });

        if (improve == "JA" && themes.Count == 0)
            return BadRequest(new { error = "THEMEN", message = "Wähl mindestens ein Thema." });

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
                return StatusCode(429, new { error = "RATE_LIMIT", message = "Gerade etwas zu viel — bitte später nochmals." });
        }

        var row = new ExitSurveyResponse
        {
            CreatedAt = DateTime.Now,
            CompanyProfileId = cpId,
            ReasonsJson = JsonSerializer.Serialize(reasons),
            ReasonOther = Clip(dto.ReasonOther, 500),
            AtmosphereDetail = Clip(dto.AtmosphereDetail, 2000),
            Rating = dto.Rating,
            Comment = Clip(dto.Comment, 4000),
            ImproveAnswer = improve.Length > 0 ? improve : null,
            ImproveThemesJson = JsonSerializer.Serialize(themes),
            IpHash = ipHash,
        };
        _db.ExitSurveyResponses.Add(row);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private static readonly Dictionary<string, string> ReasonLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        // Aktuell (Walter 26.07.2026)
        ["STARTE_NEUES"] = "Ich starte etwas Neues",
        ["SCHULE_PLAENE"] = "Schule, Studium oder persönliche Pläne",
        ["WENIGER_EINSAETZE"] = "Ich wollte weniger Einsätze",
        ["MEHR_EINSAETZE"] = "Ich hätte gerne mehr Einsätze gehabt",
        ["ARBEIT_PASST_NICHT"] = "Etwas bei der Arbeit hat nicht mehr gepasst",
        ["ETWAS_ANDERES"] = "Etwas anderes",
        // Zwischenstand OneCrew-Kurzliste
        ["NEUER_JOB"] = "Neuer Job",
        ["SCHULE_STUDIUM"] = "Schule / Studium",
        ["ZU_VIELE_EINSAETZE"] = "Zu viele Einsätze",
        ["ZU_WENIG_EINSAETZE"] = "Zu wenig Einsätze",
        ["PASST_NICHT_MEHR"] = "Es hat für mich nicht mehr gepasst",
        // Historische Konzern-Codes
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

    private static readonly Dictionary<string, string> ImproveAnswerLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["JA"] = "Ja, da gibt es etwas",
        ["NEIN"] = "Nein, für mich war es einfach Zeit für etwas Neues",
    };

    private static readonly Dictionary<string, string> ImproveThemeLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["FUEHRUNG"] = "Führung",
        ["TEAMGEFUEHL"] = "Teamgefühl",
        ["PLANUNG_ORG"] = "Planung und Organisation",
        ["ARBEITSZEITEN"] = "Arbeitszeiten",
        ["UNTERSTUETZUNG"] = "Unterstützung und Wertschätzung",
        ["ENTWICKLUNG"] = "Entwicklungsmöglichkeiten",
        ["LOHN_BEDINGUNGEN"] = "Lohn und Bedingungen",
        ["THEMA_ANDERES"] = "Etwas anderes",
    };

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
                x.ImproveAnswer,
                x.ImproveThemesJson,
            }
        ).Take(take).ToListAsync();

        var rows = raw.Select(x =>
        {
            var codes = ParseCodes(x.ReasonsJson);
            var labels = codes
                .Select(c => ReasonLabels.TryGetValue(c, out var lbl) ? lbl : c)
                .ToList();
            if (!string.IsNullOrWhiteSpace(x.ReasonOther))
                labels.Add(x.ReasonOther.Trim());

            var themeCodes = ParseCodes(x.ImproveThemesJson);
            var themeLabels = themeCodes
                .Select(c => ImproveThemeLabels.TryGetValue(c, out var lbl) ? lbl : c)
                .ToList();
            string? improveLabel = null;
            if (!string.IsNullOrWhiteSpace(x.ImproveAnswer)
                && ImproveAnswerLabels.TryGetValue(x.ImproveAnswer, out var il))
                improveLabel = il;

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
                improveAnswer = x.ImproveAnswer,
                improveAnswerLabel = improveLabel,
                improveThemesJson = x.ImproveThemesJson,
                improveThemes = themeLabels,
                improveThemeCodes = themeCodes,
            };
        }).ToList();
        return Ok(rows);
    }

    private static List<string> ParseCodes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(json);
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
