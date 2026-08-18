using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[Authorize]
[ApiController]
[Route("api/absenz-typen")]
public class AbsenzTypenController : ControllerBase
{
    private readonly AppDbContext              _db;
    private readonly AbsenceHoursRecalcService _recalc;

    public AbsenzTypenController(AppDbContext db, AbsenceHoursRecalcService recalc)
    {
        _db     = db;
        _recalc = recalc;
    }

    // Prüft, ob der eingeloggte User Superadmin ist (Anlegen/Löschen von
    // Absenz-Typen ist Superadmin-only, Walter-Vorgabe 04.07.2026).
    private async Task<bool> CallerIsSuperAdminAsync()
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid)) return false;
        return await _db.AppUsers.Where(u => u.Id == uid).Select(u => u.IsSuperAdmin).FirstOrDefaultAsync();
    }

    /// <summary>Alle aktiven Typen (für Dropdown in Modal)</summary>
    [HttpGet]
    public async Task<IActionResult> GetAktive()
    {
        var list = await _db.AbsenzTypen
            .Where(t => t.Aktiv)
            .OrderBy(t => t.SortOrder)
            .Select(t => new {
                t.Id, t.Code, t.Bezeichnung, t.Zeitgutschrift, t.GutschriftModus,
                t.UtpAuszahlung, t.VerlaengertProbezeit, t.ReduziertSaldo, t.BasisStunden, t.BasisStundenMtp, t.WirkungFix, t.WirkungMtp, t.WirkungFlex, t.ZaehlweiseFix, t.ZaehlweiseMtp, t.ZaehlweiseFlex, t.BasisFix, t.BasisMtp, t.SortOrder, t.ZwischenverdienstKuerzel
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Alle Typen inkl. inaktiver (für Admin)</summary>
    [HttpGet("all")]
    public async Task<IActionResult> GetAlle()
    {
        var list = await _db.AbsenzTypen
            .OrderBy(t => t.SortOrder)
            .Select(t => new {
                t.Id, t.Code, t.Bezeichnung, t.Zeitgutschrift, t.GutschriftModus,
                t.UtpAuszahlung, t.VerlaengertProbezeit, t.ReduziertSaldo, t.BasisStunden, t.BasisStundenMtp, t.WirkungFix, t.WirkungMtp, t.WirkungFlex, t.ZaehlweiseFix, t.ZaehlweiseMtp, t.ZaehlweiseFlex, t.BasisFix, t.BasisMtp, t.SortOrder, t.Aktiv, t.ZwischenverdienstKuerzel
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Typ aktualisieren</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] AbsenzTypDto dto)
    {
        var typ = await _db.AbsenzTypen.FindAsync(id);
        if (typ is null) return NotFound();

        // Code-Eindeutigkeit prüfen (falls geändert)
        if (!string.Equals(typ.Code, dto.Code, StringComparison.OrdinalIgnoreCase))
        {
            bool exists = await _db.AbsenzTypen.AnyAsync(t => t.Id != id && t.Code == dto.Code.ToUpper());
            if (exists) return BadRequest("Ein Typ mit diesem Code existiert bereits.");
        }

        var err = ValidateFlags(dto);
        if (err != null) return BadRequest(err);

        // Walter 31.07.2026: wenn Berechnungs-Felder ändern → Absenzen nachrechnen
        // (alle Filialen/MA, ausser «In Lohn verwendet»).
        bool needsRecalc =
            typ.Zeitgutschrift != dto.Zeitgutschrift
            || !string.Equals(typ.GutschriftModus ?? "", dto.GutschriftModus ?? "", StringComparison.Ordinal)
            || typ.UtpAuszahlung != dto.UtpAuszahlung
            || !string.Equals(
                string.IsNullOrWhiteSpace(typ.BasisStunden) ? "BETRIEB" : typ.BasisStunden,
                string.IsNullOrWhiteSpace(dto.BasisStunden) ? "BETRIEB" : dto.BasisStunden,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                string.IsNullOrWhiteSpace(typ.BasisStundenMtp) ? "GARANTIE" : typ.BasisStundenMtp,
                string.IsNullOrWhiteSpace(dto.BasisStundenMtp) ? "GARANTIE" : dto.BasisStundenMtp,
                StringComparison.OrdinalIgnoreCase);

        typ.Code             = dto.Code.ToUpper().Trim();
        typ.Bezeichnung      = dto.Bezeichnung.Trim();
        typ.Zeitgutschrift   = dto.Zeitgutschrift;
        // Walter-Vorgabe 27.06.2026: Modus (1/5 vs 1/7) auch OHNE Zeitgutschrift
        // speichern — er steuert auch Lohn-Kürzungen ohne Gutschrift
        // (z.B. unbezahlter Urlaub: 1/7).
        typ.GutschriftModus  = dto.GutschriftModus;
        typ.UtpAuszahlung    = dto.UtpAuszahlung;
        typ.VerlaengertProbezeit = dto.VerlaengertProbezeit;
        typ.ReduziertSaldo   = string.IsNullOrWhiteSpace(dto.ReduziertSaldo) ? null : dto.ReduziertSaldo;
        typ.BasisStunden     = string.IsNullOrWhiteSpace(dto.BasisStunden)   ? "BETRIEB" : dto.BasisStunden;
        typ.BasisStundenMtp  = string.IsNullOrWhiteSpace(dto.BasisStundenMtp) ? "GARANTIE" : dto.BasisStundenMtp.ToUpper();
        typ.SortOrder        = dto.SortOrder;
        typ.Aktiv            = dto.Aktiv;
        typ.ZwischenverdienstKuerzel = string.IsNullOrWhiteSpace(dto.ZwischenverdienstKuerzel)
            ? null
            : dto.ZwischenverdienstKuerzel.ToUpper().Trim();
        // Matrix (18.08.2026): direkt aus dto oder Brücke aus Legacy-Feldern.
        var mxAlt = (typ.WirkungFix, typ.WirkungMtp, typ.WirkungFlex,
                     typ.ZaehlweiseFix, typ.ZaehlweiseMtp, typ.ZaehlweiseFlex,
                     typ.BasisFix, typ.BasisMtp);
        AbsenzTypMatrixMapper.Apply(typ, dto);
        needsRecalc = needsRecalc || mxAlt != (typ.WirkungFix, typ.WirkungMtp, typ.WirkungFlex,
                     typ.ZaehlweiseFix, typ.ZaehlweiseMtp, typ.ZaehlweiseFlex,
                     typ.BasisFix, typ.BasisMtp);

        await _db.SaveChangesAsync();

        AbsenceHoursRecalcService.RecalcResult? recalc = null;
        string? recalcError = null;
        if (needsRecalc)
        {
            try
            {
                recalc = await _recalc.RecalcForTypeAsync(typ);
            }
            catch (Exception ex)
            {
                // Typ ist bereits gespeichert — Nachrechnung getrennt melden
                // (häufig: timestamp-Konflikt vor Migration).
                recalcError = ex.InnerException?.Message ?? ex.Message;
            }
        }

        return Ok(new {
            typ.Id, typ.Code, typ.Bezeichnung, typ.Zeitgutschrift, typ.GutschriftModus,
            typ.UtpAuszahlung, typ.VerlaengertProbezeit, typ.ReduziertSaldo, typ.BasisStunden, typ.SortOrder, typ.Aktiv, typ.ZwischenverdienstKuerzel,
            recalcUpdated = recalc?.Updated ?? 0,
            recalcSkippedLocked = recalc?.SkippedLocked ?? 0,
            recalcSkippedNoChange = recalc?.SkippedNoChange ?? 0,
            recalcError
        });
    }

    /// <summary>
    /// Einmalige Altbestand-Bereinigung (Walter 13.08.2026): KRANK/UNFALL-
    /// hours_credited aus der BESTEHENDEN «hätte gearbeitet»-Tagesauswahl neu
    /// berechnen (Alt-Importe rechneten mit allen Kalendertagen). Die Auswahl
    /// bleibt unangetastet. Nicht lohnwirksam — reine Anzeige. Idempotent.
    /// </summary>
    [HttpPost("wartung/krank-wochenende-fix")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> FixKrankWochenende()
    {
        var r = await _recalc.FixKrankUnfallHoursAsync();
        return Ok(new { updated = r.Updated, unveraendert = r.SkippedNoChange });
    }

    /// <summary>Neuen Typ anlegen — nur Superadmin</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsenzTypDto dto)
    {
        if (!await CallerIsSuperAdminAsync())
            return StatusCode(403, "Nur der Superadmin darf neue Absenz-Typen anlegen.");

        var code = dto.Code.ToUpper().Trim();
        bool exists = await _db.AbsenzTypen.AnyAsync(t => t.Code == code);
        if (exists) return BadRequest("Ein Typ mit diesem Code existiert bereits.");

        var err = ValidateFlags(dto);
        if (err != null) return BadRequest(err);

        var typ = new AbsenzTyp
        {
            Code            = code,
            Bezeichnung     = dto.Bezeichnung.Trim(),
            Zeitgutschrift  = dto.Zeitgutschrift,
            GutschriftModus = dto.GutschriftModus,
            UtpAuszahlung   = dto.UtpAuszahlung,
            VerlaengertProbezeit = dto.VerlaengertProbezeit,
            ReduziertSaldo  = string.IsNullOrWhiteSpace(dto.ReduziertSaldo) ? null : dto.ReduziertSaldo,
            BasisStunden    = string.IsNullOrWhiteSpace(dto.BasisStunden)   ? "BETRIEB" : dto.BasisStunden,
            BasisStundenMtp = string.IsNullOrWhiteSpace(dto.BasisStundenMtp) ? "GARANTIE" : dto.BasisStundenMtp.ToUpper(),
            SortOrder       = dto.SortOrder,
            Aktiv           = true,
            // absenz_typ.created_at ist TIMESTAMPTZ → UTC Pflicht
            // (gleiche Falle wie der 502-Startcrash vom 17.08.2026).
            CreatedAt       = DateTime.UtcNow,
            ZwischenverdienstKuerzel = string.IsNullOrWhiteSpace(dto.ZwischenverdienstKuerzel)
                ? null
                : dto.ZwischenverdienstKuerzel.ToUpper().Trim()
        };
        AbsenzTypMatrixMapper.Apply(typ, dto);
        _db.AbsenzTypen.Add(typ);
        await _db.SaveChangesAsync();
        return Ok(new {
            typ.Id, typ.Code, typ.Bezeichnung, typ.Zeitgutschrift, typ.GutschriftModus,
            typ.UtpAuszahlung, typ.VerlaengertProbezeit, typ.ReduziertSaldo, typ.BasisStunden, typ.SortOrder, typ.Aktiv, typ.ZwischenverdienstKuerzel
        });
    }

    /// <summary>Typ löschen — nur Superadmin, nur wenn NICHT verwendet.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (!await CallerIsSuperAdminAsync())
            return StatusCode(403, "Nur der Superadmin darf Absenz-Typen löschen.");

        var typ = await _db.AbsenzTypen.FindAsync(id);
        if (typ is null) return NotFound();

        // „verwendet" = irgendeine Absenz trägt diesen Code (Absence.AbsenceType
        // speichert den Code in Grossbuchstaben).
        int usedCount = await _db.Absences.CountAsync(a => a.AbsenceType == typ.Code);
        if (usedCount > 0)
            return Conflict(new
            {
                error   = "ABSENZ_TYP_VERWENDET",
                message = $"Typ «{typ.Code}» wird in {usedCount} Absenz(en) verwendet und kann nicht gelöscht werden. "
                        + "Setze ihn stattdessen auf inaktiv."
            });

        _db.AbsenzTypen.Remove(typ);
        await _db.SaveChangesAsync();
        return Ok(new { deleted = true });
    }

    private static string? ValidateFlags(AbsenzTypDto dto)
    {
        if (dto.ReduziertSaldo != null
            && dto.ReduziertSaldo != ""
            && dto.ReduziertSaldo != "NACHT_STUNDEN"
            && dto.ReduziertSaldo != "FERIEN_TAGE")
            return "ReduziertSaldo: erlaubt sind NACHT_STUNDEN, FERIEN_TAGE oder leer.";
        if (dto.BasisStunden != null
            && dto.BasisStunden != ""
            && dto.BasisStunden != "BETRIEB"
            && dto.BasisStunden != "VERTRAG")
            return "BasisStunden: erlaubt sind BETRIEB oder VERTRAG.";
        return null;
    }
}

public record AbsenzTypDto(
    string  Code,
    string  Bezeichnung,
    bool    Zeitgutschrift,
    string? GutschriftModus,
    int     SortOrder,
    bool    Aktiv                    = true,
    bool    UtpAuszahlung            = false,
    string? ReduziertSaldo           = null,
    string? BasisStunden             = "BETRIEB",
    string? ZwischenverdienstKuerzel = null,
    bool    VerlaengertProbezeit     = false,
    // Basis bei MTP (Walter 18.08.2026): GARANTIE | BETRIEB
    string? BasisStundenMtp          = "GARANTIE",
    // Matrix pro Vertragsmodell (18.08.2026) — null = aus Legacy-Feldern ableiten
    bool?   WirkungFix               = null,
    bool?   WirkungMtp               = null,
    bool?   WirkungFlex              = null,
    string? ZaehlweiseFix            = null,
    string? ZaehlweiseMtp            = null,
    string? ZaehlweiseFlex           = null,
    string? BasisFix                 = null,
    string? BasisMtp                 = null
);

public static class AbsenzTypMatrixMapper
{
    /// <summary>Brücke: Matrix aus Legacy-Feldern ableiten (Backfill-Formel),
    /// solange das alte Formular noch Legacy-Felder sendet.</summary>
    public static void Apply(AbsenzTyp typ, AbsenzTypDto dto)
    {
        string ZwAbleiten(bool flexSpalte)
        {
            if ((dto.GutschriftModus ?? "") == "1/7") return "KALENDER";
            var c = (dto.Code ?? "").ToUpperInvariant();
            if (!flexSpalte && (c == "KRANK" || c == "UNFALL")) return "DIENSTPLAN";
            return "ARBEITSTAGE";
        }
        typ.WirkungFix     = dto.WirkungFix     ?? dto.Zeitgutschrift;
        typ.WirkungMtp     = dto.WirkungMtp     ?? dto.Zeitgutschrift;
        typ.WirkungFlex    = dto.WirkungFlex    ?? dto.UtpAuszahlung;
        typ.ZaehlweiseFix  = dto.ZaehlweiseFix  ?? ZwAbleiten(false);
        typ.ZaehlweiseMtp  = dto.ZaehlweiseMtp  ?? ZwAbleiten(false);
        typ.ZaehlweiseFlex = dto.ZaehlweiseFlex ?? ZwAbleiten(true);
        typ.BasisFix       = dto.BasisFix
            ?? (string.IsNullOrWhiteSpace(dto.BasisStunden) ? "BETRIEB" : dto.BasisStunden);
        typ.BasisMtp       = dto.BasisMtp
            ?? (string.IsNullOrWhiteSpace(dto.BasisStundenMtp) ? "GARANTIE" : dto.BasisStundenMtp.ToUpper());
    }
}
