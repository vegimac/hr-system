using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Programmbegrüssung auf der Anmeldeseite (Walter 04.09.2026): pro Tageszeit
/// ein kleiner Pool an Begrüssungen, zufällig gewählt. Bisher fix im Code —
/// jetzt unter Entwicklung › Programmbegrüssung pflegbar. Gespeichert als JSON
/// in app_setting «Login.Greetings»; ohne Eintrag gelten die Standardtexte.
///
///   GET /api/login-greeting        (anonym — Anmeldeseite)
///   GET /api/login-greeting/admin  (admin — mit Standard zum Zurücksetzen)
///   PUT /api/login-greeting/admin  (admin)
/// </summary>
[ApiController]
[Route("api/login-greeting")]
public class LoginGreetingController : ControllerBase
{
    private const string Key = "Login.Greetings";
    private readonly AppDbContext _db;
    public LoginGreetingController(AppDbContext db) => _db = db;

    public sealed class Slot
    {
        public string Key { get; set; } = "";
        public string Label { get; set; } = "";
        /// <summary>Stunde ab (inkl.)</summary>
        public int Von { get; set; }
        /// <summary>Stunde bis (exkl.)</summary>
        public int Bis { get; set; }
        public List<string> Texte { get; set; } = new();
    }

    /// <summary>Standard — identisch mit der bisherigen Liste im Login-Script.</summary>
    public static List<Slot> Standard() => new()
    {
        new Slot { Key = "nacht",       Label = "Nacht",        Von = 0,  Bis = 5,  Texte = new() { "Hallo Nachteule", "Noch wach?", "Die Nachtschicht grüsst" } },
        new Slot { Key = "frueh",       Label = "Früh",         Von = 5,  Bis = 8,  Texte = new() { "Hallo Frühaufsteher", "Guten Morgen — Kaffee schon bereit?", "Der frühe Vogel …" } },
        new Slot { Key = "morgen",      Label = "Morgen",       Von = 8,  Bis = 11, Texte = new() { "Guten Morgen", "Schönen guten Morgen", "Auf in den Tag" } },
        new Slot { Key = "mittag",      Label = "Mittag",       Von = 11, Bis = 13, Texte = new() { "Guten Tag", "Ä Guete!", "Hallo zur Mittagszeit" } },
        new Slot { Key = "nachmittag",  Label = "Nachmittag",   Von = 13, Bis = 18, Texte = new() { "Guten Nachmittag", "Schön, bist du da", "Willkommen zurück" } },
        new Slot { Key = "abend",       Label = "Abend",        Von = 18, Bis = 22, Texte = new() { "Guten Abend", "Schönen Abend", "Der Abend gehört dir" } },
        new Slot { Key = "spaet",       Label = "Spät",         Von = 22, Bis = 24, Texte = new() { "Hallo Nachteule", "Noch ein Spätdienst?", "Gleich geschafft" } },
    };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private async Task<List<Slot>> LadenAsync()
    {
        var s = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == Key);
        if (s == null || string.IsNullOrWhiteSpace(s.Value)) return Standard();
        try
        {
            var slots = JsonSerializer.Deserialize<List<Slot>>(s.Value, Json);
            if (slots == null || slots.Count == 0) return Standard();
            // Fehlende Zeitfenster aus dem Standard ergänzen, leere Pools mit Standard füllen
            var std = Standard();
            foreach (var d in std)
            {
                var m = slots.FirstOrDefault(x => x.Key == d.Key);
                if (m == null) slots.Add(d);
                else { m.Label = d.Label; m.Von = d.Von; m.Bis = d.Bis; if (m.Texte.Count == 0) m.Texte = d.Texte; }
            }
            return slots.OrderBy(x => x.Von).ToList();
        }
        catch { return Standard(); }
    }

    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var slots = await LadenAsync();
        return Ok(slots.Select(s => new { s.Key, s.Von, s.Bis, s.Texte }));
    }

    [Authorize(Roles = "admin")]
    [HttpGet("admin")]
    public async Task<IActionResult> GetAdmin()
    {
        var s = await _db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == Key);
        return Ok(new { slots = await LadenAsync(), standard = Standard(), individuell = s != null, updatedAt = s?.UpdatedAt });
    }

    public sealed class SaveDto { public List<Slot>? Slots { get; set; } }

    [Authorize(Roles = "admin")]
    [HttpPut("admin")]
    public async Task<IActionResult> Save([FromBody] SaveDto dto)
    {
        if (dto?.Slots == null) return BadRequest(new { error = "Keine Daten." });
        var std = Standard();
        var clean = new List<Slot>();
        foreach (var d in std)
        {
            var m = dto.Slots.FirstOrDefault(x => x.Key == d.Key);
            var texte = (m?.Texte ?? new()).Select(t => (t ?? "").Trim()).Where(t => t.Length > 0 && t.Length <= 80).Distinct().ToList();
            clean.Add(new Slot { Key = d.Key, Label = d.Label, Von = d.Von, Bis = d.Bis, Texte = texte.Count > 0 ? texte : d.Texte });
        }
        var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == Key);
        if (s == null) { s = new AppSetting { Key = Key }; _db.AppSettings.Add(s); }
        s.Value = JsonSerializer.Serialize(clean, Json);
        s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true, slots = clean });
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("admin")]
    public async Task<IActionResult> Reset()
    {
        var s = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == Key);
        if (s != null) { _db.AppSettings.Remove(s); await _db.SaveChangesAsync(); }
        return Ok(new { ok = true, slots = Standard() });
    }
}
