using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Six;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Bank-Stammdaten: lokal in bank_master (Nacht-Sync aus SIX Bank Master API).
/// Lookup bleibt lokal; IBAN-Prüfung gegen SIX optional (validate-iban).
/// MA-Bankverbindungen speichern BIC/Name selbst — Sync ändert sie nicht.
/// </summary>
[Authorize]
[ApiController]
[Route("api/banks")]
public class BanksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly BankLookupService _svc;
    private readonly SixBankMasterSyncService _sixSync;
    private readonly SixBankMasterClient _sixClient;

    public BanksController(
        AppDbContext db,
        BankLookupService svc,
        SixBankMasterSyncService sixSync,
        SixBankMasterClient sixClient)
    {
        _db = db;
        _svc = svc;
        _sixSync = sixSync;
        _sixClient = sixClient;
    }

    // GET /api/banks/lookup?iban=CH75...  — lokal aus bank_master
    [HttpGet("lookup")]
    public IActionResult Lookup([FromQuery] string? iban)
    {
        var info = _svc.LookupByIban(iban);
        if (info is null) return NotFound(new { message = "Bank nicht gefunden für diese IBAN." });
        return Ok(new {
            iid     = info.Iid,
            bic     = info.Bic,
            name    = info.Name,
            ort     = info.Ort,
            strasse = info.Strasse,
            plz     = info.Plz
        });
    }

    /// <summary>
    /// IBAN bei SIX prüfen + lokale Bankdaten. IBAN geht an SIX (öffentliche API).
    /// </summary>
    [HttpGet("validate-iban")]
    public async Task<IActionResult> ValidateIban([FromQuery] string? iban, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(iban))
            return BadRequest(new { error = "IBAN_FEHLT", message = "IBAN fehlt." });

        var clean = new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        var local = _svc.LookupByIban(clean);

        SixIbanResponse six;
        try
        {
            six = await _sixClient.ValidateIbanAsync(clean, ct);
        }
        catch (Exception ex)
        {
            return Ok(new {
                iban = clean,
                sixOk = false,
                sixResult = "ERROR",
                sixError = ex.Message,
                iid = local?.Iid,
                bank = local is null ? null : new {
                    local.Iid, local.Bic, local.Name, local.Ort, local.Strasse, local.Plz
                }
            });
        }

        var result = (six.ValidationResult ?? "").Trim().ToUpperInvariant();
        var sixOk = result is "OK" or "VALID";
        if (local is null && six.Iid is > 0)
            local = _svc.LookupByIid(six.Iid.Value.ToString());

        return Ok(new {
            iban = clean,
            sixOk,
            sixResult = six.ValidationResult,
            iid = six.Iid?.ToString() ?? local?.Iid,
            bank = local is null ? null : new {
                local.Iid, local.Bic, local.Name, local.Ort, local.Strasse, local.Plz
            }
        });
    }

    /// <summary>Status des letzten SIX-Syncs (Nacht / manuell).</summary>
    [HttpGet("six-status")]
    [Authorize(Roles = "admin")]
    public IActionResult SixStatus()
    {
        var e = _sixSync.LetztesErgebnis;
        return Ok(new {
            laeuft = _sixSync.LaeuftGerade,
            letzterStart = _sixSync.LetzterStart,
            letztesEnde = _sixSync.LetztesEnde,
            ausloeser = _sixSync.LetzterAusloeser,
            ok = e?.Ok,
            anzahl = e?.Anzahl,
            validOn = e?.ValidOn?.ToString("yyyy-MM-dd"),
            fertigAm = e?.FertigAm,
            fehler = e?.Fehler
        });
    }

    /// <summary>Bankenstamm jetzt von SIX holen (ersetzt bank_master).</summary>
    [HttpPost("sync-from-six")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SyncFromSix(CancellationToken ct)
    {
        if (_sixSync.LaeuftGerade)
            return Conflict(new { error = "LAEUFT", message = "Sync läuft bereits." });
        try
        {
            var erg = await _sixSync.SyncAsync("manuell", ct);
            return Ok(new {
                ok = true,
                anzahl = erg.Anzahl,
                validOn = erg.ValidOn?.ToString("yyyy-MM-dd"),
                fertigAm = erg.FertigAm,
                message = $"✓ {erg.Anzahl:N0} Banken von SIX übernommen"
                    + (erg.ValidOn is { } v ? $" (gültig ab {v:dd.MM.yyyy})." : ".")
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = "SIX_SYNC", message = ex.Message });
        }
    }

    // GET /api/banks?q=post
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q)
    {
        var query = _db.BankMasters.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var qLower = q.Trim().ToLower();
            query = query.Where(b =>
                b.Iid.ToLower().Contains(qLower) ||
                b.Name.ToLower().Contains(qLower) ||
                (b.Bic != null && b.Bic.ToLower().Contains(qLower)) ||
                (b.Ort != null && b.Ort.ToLower().Contains(qLower)));
        }
        var list = await query
            .OrderBy(b => b.Iid)
            .Take(500)
            .ToListAsync();
        var total = await _db.BankMasters.CountAsync();
        return Ok(new { total, items = list });
    }

    // POST /api/banks — manuell einen Eintrag anlegen
    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BankMaster dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Iid) || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "IID und Name sind erforderlich." });
        if (await _db.BankMasters.AnyAsync(b => b.Iid == dto.Iid))
            return Conflict(new { message = $"Eintrag für IID {dto.Iid} existiert bereits." });

        dto.ImportedAt = DateTime.Now;
        _db.BankMasters.Add(dto);
        await _db.SaveChangesAsync();
        await _svc.ReloadAsync();
        return Ok(dto);
    }

    // PUT /api/banks/{iid} — manuell bearbeiten
    [Authorize(Roles = "admin")]
    [HttpPut("{iid}")]
    public async Task<IActionResult> Update(string iid, [FromBody] BankMaster dto)
    {
        var b = await _db.BankMasters.FindAsync(iid);
        if (b is null) return NotFound();
        b.Bic     = dto.Bic;
        b.Name    = dto.Name;
        b.Ort     = dto.Ort;
        b.Strasse = dto.Strasse;
        b.Plz     = dto.Plz;
        b.ImportedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        await _svc.ReloadAsync();
        return Ok(b);
    }

    // DELETE /api/banks/{iid}
    [Authorize(Roles = "admin")]
    [HttpDelete("{iid}")]
    public async Task<IActionResult> Delete(string iid)
    {
        var b = await _db.BankMasters.FindAsync(iid);
        if (b is null) return NotFound();
        _db.BankMasters.Remove(b);
        await _db.SaveChangesAsync();
        await _svc.ReloadAsync();
        return Ok();
    }

}
