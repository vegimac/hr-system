using System.Text;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Bank-Stammdaten-Verwaltung. Daten werden lokal in bank_master gehalten
/// und über Admin-UI per CSV aktualisiert. Keine externen Lookups — keine
/// IBAN verlässt den Server.
/// </summary>
[Authorize]
[ApiController]
[Route("api/banks")]
public class BanksController : ControllerBase
{
    private readonly AppDbContext       _db;
    private readonly BankLookupService  _svc;
    public BanksController(AppDbContext db, BankLookupService svc)
    {
        _db  = db;
        _svc = svc;
    }

    // GET /api/banks/lookup?iban=CH75...
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
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] BankMaster dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Iid) || string.IsNullOrWhiteSpace(dto.Name))
            return BadRequest(new { message = "IID und Name sind erforderlich." });
        if (await _db.BankMasters.AnyAsync(b => b.Iid == dto.Iid))
            return Conflict(new { message = $"Eintrag für IID {dto.Iid} existiert bereits." });

        dto.ImportedAt = DateTime.UtcNow;
        _db.BankMasters.Add(dto);
        await _db.SaveChangesAsync();
        await _svc.ReloadAsync();
        return Ok(dto);
    }

    // PUT /api/banks/{iid} — manuell bearbeiten
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
        b.ImportedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _svc.ReloadAsync();
        return Ok(b);
    }

    // DELETE /api/banks/{iid}
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

    // POST /api/banks/import — CSV-Upload, ersetzt den kompletten Bestand
    // Format: Semikolon-getrennt, erste Zeile Header
    //         iid;bic;name;ort;strasse;plz
    [HttpPost("import")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Import(IFormFile file, [FromQuery] bool replace = true)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Keine Datei übermittelt." });
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "Datei zu gross (max. 10 MB)." });

        var encoding = Encoding.UTF8;
        try
        {
            using var reader = new StreamReader(file.OpenReadStream(), encoding);
            var records = new List<BankMaster>();
            int lineNo = 0;
            var now = DateTime.UtcNow;

            // Header lesen und Format erkennen (einfach vs. SIX Bank-Master)
            string? headerLine = await reader.ReadLineAsync();
            if (headerLine == null)
                return BadRequest(new { message = "CSV ist leer." });
            lineNo = 1;
            var header = headerLine.Split(';').Select(h => h.Trim().Trim('"')).ToArray();

            int idxIid, idxBic, idxName, idxOrt, idxStr, idxHausNr, idxPlz;
            if (header[0].StartsWith("IID", StringComparison.OrdinalIgnoreCase)
                && header.Any(h => h.StartsWith("Name of bank", StringComparison.OrdinalIgnoreCase)))
            {
                // SIX-Format erkannt
                idxIid    = 0;
                idxName   = Array.FindIndex(header, h => h.StartsWith("Name of bank", StringComparison.OrdinalIgnoreCase));
                idxBic    = Array.FindIndex(header, h => h.Equals("BIC", StringComparison.OrdinalIgnoreCase));
                idxStr    = Array.FindIndex(header, h => h.StartsWith("Street Name", StringComparison.OrdinalIgnoreCase));
                idxHausNr = Array.FindIndex(header, h => h.StartsWith("Building Number", StringComparison.OrdinalIgnoreCase));
                idxPlz    = Array.FindIndex(header, h => h.StartsWith("Post Code", StringComparison.OrdinalIgnoreCase));
                idxOrt    = Array.FindIndex(header, h => h.StartsWith("Town Name", StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // Unser einfaches Format: iid;bic;name;ort;strasse;plz
                idxIid    = 0;
                idxBic    = 1;
                idxName   = 2;
                idxOrt    = 3;
                idxStr    = 4;
                idxPlz    = 5;
                idxHausNr = -1;
            }

            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                lineNo++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;

                var cols = line.Split(';');
                if (cols.Length < 3) continue;

                var iid = Get(cols, idxIid);
                if (string.IsNullOrEmpty(iid)) continue;

                // Strasse + Hausnummer kombinieren (nur bei SIX-Format)
                string? strasse = Get(cols, idxStr);
                if (idxHausNr >= 0)
                {
                    var hausNr = Get(cols, idxHausNr);
                    if (!string.IsNullOrEmpty(hausNr))
                        strasse = string.IsNullOrEmpty(strasse) ? hausNr : $"{strasse} {hausNr}";
                }

                records.Add(new BankMaster
                {
                    Iid        = iid,
                    Bic        = Get(cols, idxBic),
                    Name       = Get(cols, idxName) ?? iid,
                    Ort        = Get(cols, idxOrt),
                    Strasse    = strasse,
                    Plz        = Get(cols, idxPlz),
                    ImportedAt = now
                });
            }

            if (records.Count == 0)
                return BadRequest(new { message = "CSV enthält keine gültigen Zeilen. Unterstützte Formate: einfaches (iid;bic;name;ort;strasse;plz) oder SIX Bank-Master (mit Header 'IID/QR-IID'…)." });

            // Duplikate in der Datei selbst entfernen (letzter gewinnt)
            records = records.GroupBy(r => r.Iid).Select(g => g.Last()).ToList();

            if (replace)
            {
                // Replace-All: alles löschen, dann neu einfügen
                await _db.Database.ExecuteSqlRawAsync("DELETE FROM bank_master");
            }
            else
            {
                // Merge: bestehende IIDs aktualisieren, neue einfügen
                var existingIids = await _db.BankMasters.Select(b => b.Iid).ToListAsync();
                var existingSet  = new HashSet<string>(existingIids, StringComparer.OrdinalIgnoreCase);
                var toUpdate     = records.Where(r => existingSet.Contains(r.Iid)).ToList();
                var toAdd        = records.Where(r => !existingSet.Contains(r.Iid)).ToList();

                foreach (var r in toUpdate)
                {
                    var existing = await _db.BankMasters.FindAsync(r.Iid);
                    if (existing == null) continue;
                    existing.Bic     = r.Bic;
                    existing.Name    = r.Name;
                    existing.Ort     = r.Ort;
                    existing.Strasse = r.Strasse;
                    existing.Plz     = r.Plz;
                    existing.ImportedAt = r.ImportedAt;
                }
                _db.BankMasters.AddRange(toAdd);
                await _db.SaveChangesAsync();
                await _svc.ReloadAsync();
                return Ok(new { total = records.Count, updated = toUpdate.Count, added = toAdd.Count, mode = "merge" });
            }

            _db.BankMasters.AddRange(records);
            await _db.SaveChangesAsync();
            await _svc.ReloadAsync();
            return Ok(new { total = records.Count, mode = "replace" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Import fehlgeschlagen: " + ex.Message });
        }

        static string? Get(string[] cols, int idx)
            => idx >= 0 && idx < cols.Length && !string.IsNullOrWhiteSpace(cols[idx])
                ? cols[idx].Trim().Trim('"')
                : null;
    }
}
