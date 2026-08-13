using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// HR-Büro-Kalender für Vorstellungsgespräche (Walter-Vorgabe 09.08.2026,
/// ersetzt den GF-Zeitfenster-Prozess): HR pflegt Termine mit einer Anzahl
/// verfügbarer Plätze — maximal 2 Monate im Voraus — und bucht beim Einladen
/// eines Kandidaten selbst einen Platz. Kein GF-Schritt mehr.
/// Reine Planungsdaten, kein Lohn-Bezug (EditLock-Whitelist).
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/hr-interview")]
public class HrInterviewController : ControllerBase
{
    private readonly AppDbContext _db;
    public HrInterviewController(AppDbContext db) => _db = db;

    private async Task<string?> ActorNameAsync()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idStr, out var id)) return null;
        var u = await _db.AppUsers.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.FirstName, x.LastName, x.Username })
            .FirstOrDefaultAsync();
        if (u == null) return null;
        var voll = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(voll) ? u.Username : voll;
    }

    /// <summary>Termine ab heute (max-Horizont deckt der Client mit ab).</summary>
    [HttpGet("termine")]
    public async Task<IActionResult> GetTermine()
    {
        var heute = DateOnly.FromDateTime(DateTime.Now);
        var termine = await _db.HrInterviewTermine.AsNoTracking()
            .Where(t => t.Datum >= heute)
            .OrderBy(t => t.Datum).ThenBy(t => t.VonZeit)
            .ToListAsync();
        var ids = termine.Select(t => t.Id).ToList();
        var buchungen = await _db.HrInterviewBuchungen.AsNoTracking()
            .Where(b => ids.Contains(b.TerminId) && b.Status == "GEPLANT")
            .OrderBy(b => b.CreatedAt)
            .ToListAsync();
        return Ok(termine.Select(t => new
        {
            t.Id,
            datum = t.Datum.ToString("yyyy-MM-dd"),
            von = t.VonZeit.ToString("HH:mm"),
            bis = t.BisZeit?.ToString("HH:mm"),
            t.Plaetze,
            t.Bemerkung,
            t.Ort,
            buchungen = buchungen.Where(b => b.TerminId == t.Id).Select(b => new
            {
                b.Id, b.Kandidat, b.Telefon, b.Bemerkung, b.MaAntwort,
            }),
        }));
    }

    public class TerminDto
    {
        public string? Datum { get; set; }   // ISO yyyy-MM-dd
        public string? Von { get; set; }     // HH:mm
        public string? Bis { get; set; }     // optional
        public int Plaetze { get; set; }
        public string? Bemerkung { get; set; }
        /// <summary>Durchführungs-Ort (Schulungsraum) — Walter 12.08.2026.</summary>
        public string? Ort { get; set; }
    }

    [HttpPost("termine")]
    public async Task<IActionResult> AddTermin([FromBody] TerminDto dto)
    {
        if (!DateOnly.TryParse(dto.Datum, out var datum))
            return BadRequest(new { error = "DATUM_UNGUELTIG" });
        var heute = DateOnly.FromDateTime(DateTime.Now);
        if (datum < heute)
            return BadRequest(new { error = "DATUM_VERGANGEN", message = "Termin liegt in der Vergangenheit." });
        if (datum > heute.AddMonths(2))
            return BadRequest(new { error = "ZU_WEIT_VORAUS", message = "Termine können maximal 2 Monate im Voraus erfasst werden." });
        if (!TimeOnly.TryParse(dto.Von, out var von))
            return BadRequest(new { error = "ZEIT_UNGUELTIG" });
        TimeOnly? bis = null;
        if (!string.IsNullOrWhiteSpace(dto.Bis))
        {
            if (!TimeOnly.TryParse(dto.Bis, out var b) || b <= von)
                return BadRequest(new { error = "ZEIT_UNGUELTIG", message = "Bis-Zeit muss nach der Von-Zeit liegen." });
            bis = b;
        }
        if (dto.Plaetze < 1 || dto.Plaetze > 50)
            return BadRequest(new { error = "PLAETZE_UNGUELTIG", message = "Anzahl Plätze zwischen 1 und 50 angeben." });

        _db.HrInterviewTermine.Add(new HrInterviewTermin
        {
            Datum = datum,
            VonZeit = von,
            BisZeit = bis,
            Plaetze = dto.Plaetze,
            Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            Ort = string.IsNullOrWhiteSpace(dto.Ort) ? null : dto.Ort.Trim(),
            CreatedAt = DateTime.Now,
            CreatedBy = await ActorNameAsync(),
        });
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>Termin bearbeiten (Zeit/Plätze/Bemerkung) — Plätze nie unter die
    /// bereits gebuchten reduzieren (Walter 10.08.2026).</summary>
    [HttpPut("termine/{id:int}")]
    public async Task<IActionResult> UpdateTermin(int id, [FromBody] TerminDto dto)
    {
        var t = await _db.HrInterviewTermine.FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound();
        if (!TimeOnly.TryParse(dto.Von, out var von))
            return BadRequest(new { error = "ZEIT_UNGUELTIG" });
        TimeOnly? bis = null;
        if (!string.IsNullOrWhiteSpace(dto.Bis))
        {
            if (!TimeOnly.TryParse(dto.Bis, out var b) || b <= von)
                return BadRequest(new { error = "ZEIT_UNGUELTIG", message = "Bis-Zeit muss nach der Von-Zeit liegen." });
            bis = b;
        }
        if (dto.Plaetze < 1 || dto.Plaetze > 50)
            return BadRequest(new { error = "PLAETZE_UNGUELTIG", message = "Anzahl Plätze zwischen 1 und 50 angeben." });
        int belegt = await _db.HrInterviewBuchungen.CountAsync(b => b.TerminId == id && b.Status == "GEPLANT");
        if (dto.Plaetze < belegt)
            return Conflict(new { error = "PLAETZE_ZU_KLEIN", message = $"Es sind bereits {belegt} Plätze gebucht — weniger geht nicht." });
        // Zeit ist GESPERRT, sobald MA gebucht sind (Walter 10.08.2026): die
        // Eingeladenen haben diese Zeit erhalten. Verschieben = pro MA
        // umbuchen (nach telefonischer Absprache).
        if (belegt > 0 && (von != t.VonZeit || bis != t.BisZeit))
            return Conflict(new
            {
                error = "ZEIT_GESPERRT",
                message = $"Es sind bereits {belegt} MA auf diese Zeit eingeladen — Zeit nicht änderbar. Bei Verschiebung/Absage: MA telefonisch informieren und einzeln auf einen anderen Termin umbuchen.",
            });

        t.VonZeit = von;
        t.BisZeit = bis;
        t.Plaetze = dto.Plaetze;
        t.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        t.Ort = string.IsNullOrWhiteSpace(dto.Ort) ? null : dto.Ort.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("termine/{id:int}")]
    public async Task<IActionResult> DeleteTermin(int id)
    {
        var t = await _db.HrInterviewTermine.FindAsync(id);
        if (t == null) return NotFound();
        bool hatBuchungen = await _db.HrInterviewBuchungen.AnyAsync(b => b.TerminId == id && b.Status == "GEPLANT");
        if (hatBuchungen)
            return Conflict(new
            {
                error = "BUCHUNGEN_VORHANDEN",
                message = "Für diesen Termin sind bereits Kandidaten gebucht — zuerst die Buchungen absagen.",
            });
        _db.HrInterviewTermine.Remove(t);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    public class BuchungDto
    {
        public string? Kandidat { get; set; }
        public string? Telefon { get; set; }
        public string? Bemerkung { get; set; }
    }

    [HttpPost("termine/{id:int}/buchen")]
    public async Task<IActionResult> Buchen(int id, [FromBody] BuchungDto dto)
    {
        var t = await _db.HrInterviewTermine.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (t == null) return NotFound(new { error = "TERMIN_FEHLT" });
        if (t.Datum < DateOnly.FromDateTime(DateTime.Now))
            return Conflict(new { error = "TERMIN_VERGANGEN", message = "Dieser Termin liegt in der Vergangenheit." });
        if (string.IsNullOrWhiteSpace(dto.Kandidat))
            return BadRequest(new { error = "KANDIDAT_FEHLT", message = "Kandidatenname angeben." });
        int belegt = await _db.HrInterviewBuchungen.CountAsync(b => b.TerminId == id && b.Status == "GEPLANT");
        if (belegt >= t.Plaetze)
            return Conflict(new { error = "AUSGEBUCHT", message = "Dieser Termin ist ausgebucht." });

        _db.HrInterviewBuchungen.Add(new HrInterviewBuchung
        {
            TerminId = id,
            Kandidat = dto.Kandidat.Trim(),
            Telefon = string.IsNullOrWhiteSpace(dto.Telefon) ? null : dto.Telefon.Trim(),
            Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            Status = "GEPLANT",
            CreatedAt = DateTime.Now,
            CreatedBy = await ActorNameAsync(),
        });
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    public class UmbuchenDto
    {
        public int NeuerTerminId { get; set; }
    }

    /// <summary>
    /// Buchung auf einen anderen Termin verschieben (Walter 10.08.2026) —
    /// nach telefonischer Absprache mit dem MA. Der bestehende Vertrags-Link
    /// des MA wird mitgezogen (Landing-Page + Kalender-Datei zeigen den
    /// neuen Termin), es braucht KEINE neue SMS.
    /// </summary>
    [HttpPost("buchungen/{id:int}/umbuchen")]
    public async Task<IActionResult> Umbuchen(int id, [FromBody] UmbuchenDto dto)
    {
        var b = await _db.HrInterviewBuchungen.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();
        if (b.Status != "GEPLANT")
            return Conflict(new { error = "NICHT_GEPLANT", message = "Nur geplante Buchungen können umgebucht werden." });
        if (dto.NeuerTerminId == b.TerminId)
            return BadRequest(new { error = "GLEICHER_TERMIN" });
        var neu = await _db.HrInterviewTermine.AsNoTracking().FirstOrDefaultAsync(t => t.Id == dto.NeuerTerminId);
        if (neu == null) return NotFound(new { error = "TERMIN_FEHLT" });
        if (neu.Datum < DateOnly.FromDateTime(DateTime.Now))
            return Conflict(new { error = "TERMIN_VERGANGEN", message = "Der Ziel-Termin liegt in der Vergangenheit." });
        int belegt = await _db.HrInterviewBuchungen.CountAsync(x => x.TerminId == neu.Id && x.Status == "GEPLANT");
        if (belegt >= neu.Plaetze)
            return Conflict(new { error = "AUSGEBUCHT", message = "Der Ziel-Termin ist ausgebucht." });

        var alterTerminId = b.TerminId;
        b.TerminId = neu.Id;
        // Neuer Termin = neue Bestätigung nötig (Walter 10.08.2026): eine
        // frühere Annahme des MA galt für den alten Termin.
        b.MaAntwort = null;
        b.MaAntwortAm = null;

        // Vertrags-Link des MA mitziehen: aktive Tokens mit dem ALTEN Termin,
        // deren MA per Telefon (normalisiert) oder Name zur Buchung passt.
        var jetzt = DateTime.Now;
        var tokens = await _db.ContractShareTokens
            .Where(t => t.OnboardingTerminId == alterTerminId && t.RevokedAt == null && t.ExpiresAt > jetzt)
            .ToListAsync();
        if (tokens.Count > 0)
        {
            static string Digits(string? s) => new string((s ?? "").Where(char.IsDigit).ToArray());
            var empIds = tokens.Select(t => t.EmployeeId).Distinct().ToList();
            var emps = await _db.Employees.AsNoTracking()
                .Where(e => empIds.Contains(e.Id))
                .Select(e => new { e.Id, e.FirstName, e.LastName, e.PhoneMobile })
                .ToListAsync();
            foreach (var tok in tokens)
            {
                var e = emps.FirstOrDefault(x => x.Id == tok.EmployeeId);
                if (e == null) continue;
                bool passt =
                    (Digits(b.Telefon).Length > 0 && Digits(b.Telefon) == Digits(e.PhoneMobile))
                    || string.Equals($"{e.FirstName} {e.LastName}".Trim(), b.Kandidat.Trim(), StringComparison.OrdinalIgnoreCase);
                if (passt) tok.OnboardingTerminId = neu.Id;
            }
        }

        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpPost("buchungen/{id:int}/absagen")]
    public async Task<IActionResult> Absagen(int id)
    {
        var b = await _db.HrInterviewBuchungen.FirstOrDefaultAsync(x => x.Id == id);
        if (b == null) return NotFound();
        if (b.Status != "ABGESAGT")
        {
            b.Status = "ABGESAGT";
            await _db.SaveChangesAsync();
        }
        return Ok(new { ok = true });
    }
}
