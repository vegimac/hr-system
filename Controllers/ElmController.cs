using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.Elm;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Swissdec ELM 6.0 — Etappe E1 (Walter 27.08.2026): Verbindungstests
/// Ping + CheckInteroperability. NUR Admin (eigener Sidebar-Bereich
/// «Swissdec»). Rein manuell ausgelöste Calls (Richtlinien Kap. 4: Ping
/// nie automatisieren). Kein Lohn-Edit → EditLock-Audit unkritisch
/// (GET-frei, POSTs rufen nur externe Test-Endpunkte).
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/elm")]
public class ElmController : ControllerBase
{
    private readonly ElmTransmitterClient _client;
    private readonly ElmAnnualDeclarationBuilder _builder;
    private readonly AppDbContext _db;
    public ElmController(ElmTransmitterClient client, ElmAnnualDeclarationBuilder builder, AppDbContext db)
    {
        _client = client;
        _builder = builder;
        _db = db;
    }

    public record ElmUrlDto(string Url);

    private static bool UrlOk(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp);

    [HttpPost("ping")]
    public async Task<IActionResult> Ping([FromBody] ElmUrlDto dto, CancellationToken ct)
    {
        if (!UrlOk(dto.Url))
            return BadRequest(new { error = "URL_INVALID", message = "Bitte eine gültige Endpoint-URL angeben." });
        var r = await _client.PingAsync(dto.Url.Trim(), ct);
        return Ok(r);
    }

    [HttpPost("check-interoperability")]
    public async Task<IActionResult> CheckInteroperability([FromBody] ElmUrlDto dto, CancellationToken ct)
    {
        if (!UrlOk(dto.Url))
            return BadRequest(new { error = "URL_INVALID", message = "Bitte eine gültige Endpoint-URL angeben." });
        var r = await _client.CheckInteroperabilityAsync(dto.Url.Trim(), ct);
        return Ok(r);
    }

    /// <summary>
    /// E2: Jahresmeldung AHV als DeclareAnnualSalary-XML erzeugen + gegen
    /// die ELM-6.0-Schemas validieren. Nur mit KUNSTDATEN (test.onecrew.ch)
    /// im Refapps-Transmitter hochladen — nie Echtdaten.
    /// </summary>
    [HttpGet("annual-ahv/{year:int}")]
    public async Task<IActionResult> AnnualAhv(int year, CancellationToken ct)
    {
        if (year < 2020 || year > 2100)
            return BadRequest(new { error = "YEAR_INVALID", message = "Bitte ein gültiges Jahr angeben." });
        var r = await _builder.BuildAhvAsync(year, ct);
        return Ok(new
        {
            xml = r.Xml,
            personen = r.Personen,
            uebersprungen = r.Uebersprungen,
            totalAhv = r.TotalAhv,
            totalAlv = r.TotalAlv,
            warnungen = r.Warnungen,
            xsdFehler = r.XsdFehler,
            valid = r.XsdFehler.Count == 0 && r.Xml.Length > 0
        });
    }

    // ── E3: Stammdaten Rechtseinheit (Walter 28.08.2026) ──────────────
    // EINE Zeile (Meldeeinheit). Kein Lohn-Edit → EditLock unkritisch
    // (Controller ist in der Audit-Whitelist).

    public record ElmStammdatenDto(
        string? Uid,
        string? AkName, string? AkKassenNummer, string? AkAbrechnungsNummer,
        string? FakKassenNummer, string? FakAbrechnungsNummer,
        string? UvgVersicherer, string? UvgKundenNummer, string? UvgVertragsNummer,
        string? UvgUid, DateOnly? UvgVersichertSeit,
        string? UvgzVersicherer, string? UvgzKundenNummer, string? UvgzVertragsNummer,
        string? KtgVersicherer, string? KtgKundenNummer, string? KtgVertragsNummer,
        string? BvgVersicherer, string? BvgKundenNummer, string? BvgVertragsNummer,
        string? BvgUid, DateOnly? BvgVersichertSeit);

    [HttpGet("stammdaten")]
    public async Task<IActionResult> GetStammdaten(CancellationToken ct)
    {
        var s = await _db.ElmStammdaten.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        return Ok(s ?? new ElmStammdaten());
    }

    [HttpPut("stammdaten")]
    public async Task<IActionResult> SaveStammdaten([FromBody] ElmStammdatenDto dto, CancellationToken ct)
    {
        var uid = (dto.Uid ?? "").Trim();
        if (uid.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(uid, @"^CHE-\d{3}\.\d{3}\.\d{3}$"))
            return BadRequest(new { error = "UID_INVALID", message = "UID bitte im Format CHE-XXX.XXX.XXX erfassen." });

        var s = await _db.ElmStammdaten.OrderBy(x => x.Id).FirstOrDefaultAsync(ct);
        if (s == null) { s = new ElmStammdaten(); _db.ElmStammdaten.Add(s); }

        static string? T(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        s.Uid = T(uid);
        s.AkName = T(dto.AkName); s.AkKassenNummer = T(dto.AkKassenNummer); s.AkAbrechnungsNummer = T(dto.AkAbrechnungsNummer);
        s.FakKassenNummer = T(dto.FakKassenNummer); s.FakAbrechnungsNummer = T(dto.FakAbrechnungsNummer);
        s.UvgVersicherer = T(dto.UvgVersicherer); s.UvgKundenNummer = T(dto.UvgKundenNummer); s.UvgVertragsNummer = T(dto.UvgVertragsNummer);
        s.UvgUid = T(dto.UvgUid); s.UvgVersichertSeit = dto.UvgVersichertSeit;
        s.UvgzVersicherer = T(dto.UvgzVersicherer); s.UvgzKundenNummer = T(dto.UvgzKundenNummer); s.UvgzVertragsNummer = T(dto.UvgzVertragsNummer);
        s.KtgVersicherer = T(dto.KtgVersicherer); s.KtgKundenNummer = T(dto.KtgKundenNummer); s.KtgVertragsNummer = T(dto.KtgVertragsNummer);
        s.BvgVersicherer = T(dto.BvgVersicherer); s.BvgKundenNummer = T(dto.BvgKundenNummer); s.BvgVertragsNummer = T(dto.BvgVertragsNummer);
        s.BvgUid = T(dto.BvgUid); s.BvgVersichertSeit = dto.BvgVersichertSeit;
        s.UpdatedAt = DateTime.Now;
        s.UpdatedBy = User.FindFirst(ClaimTypes.Name)?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        await _db.SaveChangesAsync(ct);
        return Ok(s);
    }
}
