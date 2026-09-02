using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Admin-Endpoint für die eCall-SMS-Konfiguration (Singleton, ecall_setting)
/// + Test-Versand. Analog <see cref="AdminSmtpController"/>.
///
/// Sichtbarkeit/Schreibrechte: nur admin — SMS-Versand passiert im Namen
/// der Firma und das API-Passwort ist sensibel.
///
/// Endpoints:
///   GET  /api/ecall/settings — aktuelle Konfig (Passwort NICHT ausgegeben,
///                              nur Flag hasPassword)
///   PUT  /api/ecall/settings — Konfig speichern (Passwort nur ändern wenn
///                              nicht-leer)
///   POST /api/ecall/test     — Test-SMS mit der gespeicherten Konfig
/// </summary>
[Authorize(Roles = "admin")]
[ApiController]
[Route("api/ecall")]
public class EcallController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SimpleAesService _aes;
    private readonly EcallSmsService _sms;
    private readonly ILogger<EcallController> _log;

    public EcallController(AppDbContext db, SimpleAesService aes,
                           EcallSmsService sms, ILogger<EcallController> log)
    {
        _db = db;
        _aes = aes;
        _sms = sms;
        _log = log;
    }

    public class EcallSettingsDto
    {
        public bool Enabled { get; set; }
        public string? Username { get; set; }
        public string? Sender { get; set; }
        public string? TestRedirectTo { get; set; } // Test-Umleitung: alle SMS an diese Nummer; leer = Echtbetrieb
        public string? Password { get; set; }   // PUT: leer/null = unverändert; sonst neu setzen
        public bool HasPassword { get; set; }    // nur GET
    }

    public class EcallTestDto
    {
        public string To { get; set; } = "";
        public string? Text { get; set; }
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings()
    {
        var row = await _db.EcallSettings.AsNoTracking().FirstOrDefaultAsync(r => r.Id == 1);
        return Ok(new EcallSettingsDto
        {
            Enabled        = row?.Enabled ?? false,
            Username       = row?.Username ?? "",
            Sender         = row?.Sender ?? "",
            TestRedirectTo = row?.TestRedirectTo ?? "",
            HasPassword    = !string.IsNullOrEmpty(row?.PasswordEncrypted)
        });
    }

    [HttpPut("settings")]
    public async Task<IActionResult> PutSettings([FromBody] EcallSettingsDto dto)
    {
        // Test-Nummer ist PFLICHT (Walter-Vorgabe 01.09.2026) — analog zur
        // Test-Adresse beim Mail: Umleitungsziel für jeden Verteiler ohne Haken.
        if (string.IsNullOrWhiteSpace(dto.TestRedirectTo))
            return BadRequest(new { ok = false, error = "TESTNUMMER_FEHLT",
                message = "Die Test-Nummer ist Pflicht — sie ist das Umleitungsziel für alle Verteiler ohne Haken." });

        var row = await _db.EcallSettings.FirstOrDefaultAsync(r => r.Id == 1);
        if (row == null)
        {
            row = new EcallSetting { Id = 1 };
            _db.EcallSettings.Add(row);
        }

        row.Enabled        = dto.Enabled;
        row.Username       = string.IsNullOrWhiteSpace(dto.Username) ? null : dto.Username.Trim();
        row.Sender         = string.IsNullOrWhiteSpace(dto.Sender) ? null : dto.Sender.Trim();
        row.TestRedirectTo = dto.TestRedirectTo!.Trim();

        // Passwort nur ändern, wenn ein nicht-leerer Wert kommt.
        if (!string.IsNullOrEmpty(dto.Password))
            row.PasswordEncrypted = _aes.Encrypt(dto.Password);

        row.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        _log.LogInformation("[Ecall] Konfig gespeichert (enabled={Enabled} sender={Sender} testRedirect={Redirect})",
                            row.Enabled, row.Sender ?? "<leer>", row.TestRedirectTo ?? "<aus>");
        return Ok(new { ok = true });
    }

    [HttpPost("test")]
    public async Task<IActionResult> SendTest([FromBody] EcallTestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.To))
            return BadRequest(new { ok = false, error = "Zielnummer fehlt." });

        var text = string.IsNullOrWhiteSpace(dto.Text)
            ? "OneCrew Test-SMS - die eCall-Anbindung funktioniert."
            : dto.Text!.Trim();

        // Test-SMS steht bewusst ausserhalb der Freigabe-Matrix: sie geht an
        // die von Hand eingetippte Nummer (Walter 01.09.2026).
        var result = await _sms.SendSmsAsync(dto.To.Trim(), text);
        return Ok(new { ok = result.Ok, messageId = result.MessageId, error = result.Error });
    }
}
