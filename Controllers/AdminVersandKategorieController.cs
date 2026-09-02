using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Freigabe-Matrix für den Nachrichten-Versand (Walter-Vorgabe 01.09.2026).
///
/// Ersetzt den Alles-oder-nichts-Schalter «Test-Adresse gefüllt». Pro
/// Verteiler-Kategorie und Kanal (Mail / SMS) entscheidet ein Haken:
/// gesetzt = scharf an den echten Empfänger, nicht gesetzt = Umleitung an
/// die Test-Adresse bzw. Test-Nummer.
///
/// Nur admin — wer hier einen Haken setzt, kann Mails an alle MA auslösen.
///
/// Endpoints:
///   GET /api/admin/versand-kategorien — Matrix inkl. Stammdaten fürs UI
///   PUT /api/admin/versand-kategorien — Haken speichern
/// </summary>
[Authorize(Roles = "admin")]
[ApiController]
[Route("api/admin/versand-kategorien")]
public class AdminVersandKategorieController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly VersandFreigabeService _freigabe;
    private readonly ILogger<AdminVersandKategorieController> _log;

    public AdminVersandKategorieController(AppDbContext db, VersandFreigabeService freigabe,
                                           ILogger<AdminVersandKategorieController> log)
    {
        _db = db;
        _freigabe = freigabe;
        _log = log;
    }

    // ── GET ──────────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        // Neu im Code ergänzte Kategorien ohne SQL-Skript nachziehen.
        await _freigabe.EnsureRowsAsync(ct);
        var map = await _freigabe.GetMapAsync(ct);

        var testAdresse = await _db.SmtpSettings.AsNoTracking()
            .Where(r => r.Id == 1).Select(r => r.TestRedirectTo).FirstOrDefaultAsync(ct);
        var testNummer = await _db.EcallSettings.AsNoTracking()
            .Where(r => r.Id == 1).Select(r => r.TestRedirectTo).FirstOrDefaultAsync(ct);

        var zeilen = VersandKategorien.All.Select(i =>
        {
            map.TryGetValue(i.Code, out var haken);
            return new
            {
                code         = i.Code,
                bezeichnung  = i.Bezeichnung,
                beschreibung = i.Beschreibung,
                empfaenger   = i.Empfaenger,
                nutztMail    = i.NutztMail,
                nutztSms     = i.NutztSms,
                mailScharf   = i.NutztMail && haken.Mail,
                smsScharf    = i.NutztSms  && haken.Sms,
            };
        }).ToList();

        return Ok(new
        {
            zeilen,
            testAdresse = string.IsNullOrWhiteSpace(testAdresse) ? null : testAdresse!.Trim(),
            testNummer  = string.IsNullOrWhiteSpace(testNummer)  ? null : testNummer!.Trim(),
            // Ohne Umleitungsziel wird eine nicht-scharfe Kategorie blockiert,
            // nicht scharf durchgelassen — das UI weist darauf hin.
            mailBlockiert = string.IsNullOrWhiteSpace(testAdresse),
            smsBlockiert  = string.IsNullOrWhiteSpace(testNummer),
        });
    }

    // ── PUT ──────────────────────────────────────────────────────────────
    public class ZeileDto
    {
        public string Code { get; set; } = "";
        public bool MailScharf { get; set; }
        public bool SmsScharf { get; set; }
    }

    public class SaveDto
    {
        public List<ZeileDto> Zeilen { get; set; } = new();
    }

    [HttpPut]
    public async Task<IActionResult> Put([FromBody] SaveDto dto, CancellationToken ct)
    {
        if (dto?.Zeilen == null || dto.Zeilen.Count == 0)
            return BadRequest(new { error = "KEINE_ZEILEN", message = "Keine Kategorien übermittelt." });

        var userId = GetCurrentUserId();
        var rows = await _db.VersandKategorien.ToListAsync(ct);
        var geaendert = new List<string>();

        foreach (var z in dto.Zeilen)
        {
            var info = VersandKategorien.All.FirstOrDefault(i => i.Code == (z.Code ?? "").Trim().ToUpperInvariant());
            if (info == null) continue;               // unbekannter Code wird ignoriert

            // Ein Haken auf einem Kanal, den die Kategorie gar nicht nutzt,
            // wird gar nicht erst gespeichert — sonst steht in der DB eine
            // Freigabe, die nie jemand erklären kann.
            var mail = info.NutztMail && z.MailScharf;
            var sms  = info.NutztSms  && z.SmsScharf;

            var row = rows.FirstOrDefault(r => r.Code == info.Code);
            if (row == null)
            {
                row = new VersandKategorieSetting { Code = info.Code };
                _db.VersandKategorien.Add(row);
            }
            if (row.MailScharf != mail || row.SmsScharf != sms)
                geaendert.Add($"{info.Code}: Mail {(mail ? "scharf" : "Test")}, SMS {(sms ? "scharf" : "Test")}");

            row.MailScharf      = mail;
            row.SmsScharf       = sms;
            row.UpdatedAt       = DateTime.Now;
            row.UpdatedByUserId = userId;
        }

        await _db.SaveChangesAsync(ct);
        VersandFreigabeService.InvalidateCache();

        if (geaendert.Count > 0)
            _log.LogWarning("[VersandFreigabe] Benutzer {UserId} hat die Freigabe geändert — {Aenderungen}",
                            userId, string.Join(" | ", geaendert));

        return Ok(new { ok = true, geaendert = geaendert.Count });
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}
