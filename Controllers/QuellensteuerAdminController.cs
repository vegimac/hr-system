using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Admin-Endpoints für den Import und Status der Quellensteuer-Tarifdateien.
/// Sicherheit (Walter-Vorgabe 23.05.2026): kompletter QST-Tarif-Admin-Bereich
/// nur für admin (Import/Reload ändern systemweite Tarife).
/// </summary>
[ApiController]
[Route("api/admin/quellensteuer")]
[Authorize(Roles = "admin")]
public class QuellensteuerAdminController : ControllerBase
{
    private readonly QuellensteuerTarifService _tarifService;
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;

    public QuellensteuerAdminController(
        QuellensteuerTarifService tarifService, AppDbContext db, IWebHostEnvironment env)
    {
        _tarifService = tarifService;
        _db = db;
        _env = env;
    }

    private void SyncSatzart11()
        => QstSatzart11.SyncAusVerzeichnis(
            _db, Path.Combine(_env.ContentRootPath, "Assets", "Quellensteuer"), force: true);

    // ── GET /api/admin/quellensteuer/probe ───────────────────────────────
    /// <summary>
    /// Tarif-Probe (Walter 29.08.2026): schlägt Satz + Betrag direkt in der
    /// geladenen OFFIZIELLEN ESTV-Tarifdatei nach — zum Verifizieren gegen
    /// Kantons-Tabellen/Treuhand-Abrechnungen (z.B. AG 2026, C0N, 2500).
    /// </summary>
    [HttpGet("probe")]
    public IActionResult Probe(
        [FromQuery] string kanton, [FromQuery] string tarifCode,
        [FromQuery] int kinder, [FromQuery] bool kirche,
        [FromQuery] decimal brutto, [FromQuery] int? jahr)
    {
        if (string.IsNullOrWhiteSpace(kanton) || string.IsNullOrWhiteSpace(tarifCode) || brutto <= 0)
            return BadRequest(new { error = "PARAMS", message = "Kanton, Tarifcode und Bruttolohn angeben." });

        var b = _tarifService.Berechne(kanton.Trim().ToUpperInvariant(),
            tarifCode.Trim().ToUpperInvariant(), kinder, kirche, brutto, brutto, jahr);
        if (b == null)
            return NotFound(new { error = "TARIF_NICHT_GEFUNDEN",
                message = $"Kein Tarif gefunden für {kanton.ToUpperInvariant()} {tarifCode.ToUpperInvariant()}{kinder}{(kirche ? "Y" : "N")}" +
                          (jahr != null ? $" Jahr {jahr}" : "") + " — Tarifdatei geladen?" });

        return Ok(new
        {
            betrag = b.SteuerbetragCHF,
            satzPct = b.SteuersatzPct,
            mindeststeuer = b.MindeststeuerCHF,
            mindestAngewendet = b.MindeststeuerAngewendet
        });
    }

    // ── GET /api/admin/quellensteuer/status ──────────────────────────────
    /// <summary>
    /// Gibt den Status aller geladenen Quellensteuer-Tarifdateien zurück.
    /// </summary>
    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var dateien = _tarifService.GetDateienStatus();
        return Ok(new
        {
            AnzahlDateien  = dateien.Count,
            Dateien        = dateien.Select(d => new
            {
                d.Jahr,
                d.Kanton,
                d.Dateiname,
                d.AnzahlKombinationen,
                d.AnzahlEintraege,
                MaxEinkommen   = d.MaxEinkommen,
                GeladenAm      = d.GeladenAm.ToString("dd.MM.yyyy HH:mm")
            })
        });
    }

    // ── POST /api/admin/quellensteuer/import ─────────────────────────────
    /// <summary>
    /// Importiert eine oder mehrere Tarifdateien (.txt oder .zip).
    /// Der Kanton und das Jahr werden automatisch aus dem Dateiinhalt erkannt.
    /// Nach dem Import wird der Cache automatisch neu geladen.
    /// </summary>
    [HttpPost("import")]
    [RequestSizeLimit(50 * 1024 * 1024)] // max 50 MB
    public async Task<IActionResult> Import([FromForm] IFormFileCollection files)
    {
        if (files == null || files.Count == 0)
            return BadRequest(new { error = "Keine Datei hochgeladen." });

        var alleErgebnisse = new List<object>();
        var fehler = new List<string>();

        foreach (var file in files)
        {
            string ext = Path.GetExtension(file.FileName).ToLower();
            if (ext != ".txt" && ext != ".zip")
            {
                fehler.Add($"{file.FileName}: Nur .txt und .zip Dateien erlaubt.");
                continue;
            }

            try
            {
                using var stream = file.OpenReadStream();
                var ergebnis = await _tarifService.ImportiereAsync(stream, file.FileName);

                if (ergebnis.Erfolg)
                {
                    foreach (var info in ergebnis.ImportierteDateien)
                    {
                        alleErgebnisse.Add(new
                        {
                            info.Kanton,
                            info.Jahr,
                            info.Dateiname,
                            Meldung = $"Kanton {info.Kanton} {info.Jahr} erfolgreich importiert."
                        });
                    }
                }
                else
                {
                    fehler.Add($"{file.FileName}: Kanton konnte nicht erkannt werden.");
                }
            }
            catch (Exception ex)
            {
                fehler.Add($"{file.FileName}: {ex.Message}");
            }
        }

        if (alleErgebnisse.Count == 0 && fehler.Count > 0)
            return BadRequest(new { error = "Import fehlgeschlagen.", fehler });

        if (alleErgebnisse.Count > 0) SyncSatzart11();

        return Ok(new
        {
            Erfolg         = alleErgebnisse.Count,
            Fehler         = fehler.Count,
            Importiert     = alleErgebnisse,
            Fehlermeldungen = fehler,
            Status         = _tarifService.GetDateienStatus().Select(d => new
            {
                d.Jahr, d.Kanton, d.AnzahlKombinationen, d.MaxEinkommen
            })
        });
    }

    // ── POST /api/admin/quellensteuer/reload ─────────────────────────────
    /// <summary>
    /// Lädt alle Tarifdateien aus dem Dateisystem neu (ohne Upload).
    /// </summary>
    [HttpPost("reload")]
    public IActionResult Reload()
    {
        _tarifService.Reload();
        SyncSatzart11();
        var status = _tarifService.GetDateienStatus();
        return Ok(new
        {
            Meldung       = $"Cache neu geladen: {status.Count} Dateien.",
            AnzahlDateien = status.Count
        });
    }

    // ── GET /api/admin/quellensteuer/sonderkategorien ────────────────────
    /// <summary>
    /// Katalog + alle Satzart-11-Sätze (gültig von/bis), inkl. abgelaufener.
    /// </summary>
    [HttpGet("sonderkategorien")]
    public async Task<IActionResult> Sonderkategorien()
    {
        var kat = await _db.QstSonderkategorien.AsNoTracking()
            .OrderBy(k => k.SortOrder).ToListAsync();
        var saetze = await _db.QstSonderkategorieSaetze.AsNoTracking()
            .OrderBy(s => s.Code).ThenBy(s => s.Kanton).ThenByDescending(s => s.ValidFrom)
            .ToListAsync();
        return Ok(new
        {
            kategorien = kat.Select(k => new
            {
                k.Code, k.Gruppe, k.Bezeichnung, k.Erklaerung, k.Automatik, k.Warnung,
                k.Kirchensteuer, k.AbzugArt
            }),
            saetze = saetze.Select(MapSatz)
        });
    }

    // ── POST /api/admin/quellensteuer/sonder-satz ─────────────────────────
    [HttpPost("sonder-satz")]
    public async Task<IActionResult> SonderSatzAnlegen([FromBody] SonderSatzDto dto)
    {
        var geprueft = PruefeSatzDto(dto);
        if (geprueft != null) return geprueft;

        var code = dto.Code.Trim().ToUpperInvariant();
        var kanton = dto.Kanton.Trim().ToUpperInvariant();
        var eintrag = QstVordefinierteKategorie.Parse(code)!;

        var konflikt = await _db.QstSonderkategorieSaetze.AnyAsync(s =>
            s.Code == code && s.Kanton == kanton && s.ValidFrom == dto.ValidFrom);
        if (konflikt)
            return Conflict(new { error = "KONFLIKT",
                message = $"Für {code} {kanton} ab {dto.ValidFrom:dd.MM.yyyy} gibt es schon einen Satz." });

        await SchliesseVorgaengerAsync(code, kanton, dto.ValidFrom, dto.PredecessorId);

        var neu = new QstSonderkategorieSatz
        {
            Code = code,
            Gruppe = QstVordefinierteKategorie.GruppeVon(eintrag.Value.Art),
            Kanton = kanton,
            SatzPct = dto.SatzPct,
            Quelle = string.IsNullOrWhiteSpace(dto.Quelle) ? "Handpflege" : dto.Quelle.Trim(),
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo,
            CreatedAt = DateTime.Now,
        };
        _db.QstSonderkategorieSaetze.Add(neu);
        await _db.SaveChangesAsync();
        return Ok(MapSatz(neu));
    }

    // ── PUT /api/admin/quellensteuer/sonder-satz/{id} ────────────────────
    [HttpPut("sonder-satz/{id:int}")]
    public async Task<IActionResult> SonderSatzAendern(int id, [FromBody] SonderSatzDto dto)
    {
        var geprueft = PruefeSatzDto(dto);
        if (geprueft != null) return geprueft;

        var satz = await _db.QstSonderkategorieSaetze.FirstOrDefaultAsync(s => s.Id == id);
        if (satz == null) return NotFound(new { error = "NICHT_GEFUNDEN" });

        if (IstEstv(satz)
            && (satz.SatzPct != dto.SatzPct
                || !string.Equals(satz.Code, dto.Code.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(satz.Kanton, dto.Kanton.Trim(), StringComparison.OrdinalIgnoreCase)
                || satz.ValidFrom != dto.ValidFrom))
        {
            return BadRequest(new { error = "ESTV_GESCHUETZT",
                message = "ESTV-Zeile: Satz, Code, Kanton und Gültig-ab kommen aus der Tarifdatei. Für eine Änderung «Neu ab» verwenden." });
        }

        var code = dto.Code.Trim().ToUpperInvariant();
        var kanton = dto.Kanton.Trim().ToUpperInvariant();
        var eintrag = QstVordefinierteKategorie.Parse(code)!;

        var konflikt = await _db.QstSonderkategorieSaetze.AnyAsync(s =>
            s.Id != id && s.Code == code && s.Kanton == kanton && s.ValidFrom == dto.ValidFrom);
        if (konflikt)
            return Conflict(new { error = "KONFLIKT",
                message = $"Für {code} {kanton} ab {dto.ValidFrom:dd.MM.yyyy} gibt es schon einen Satz." });

        if (!IstEstv(satz))
        {
            satz.Code = code;
            satz.Gruppe = QstVordefinierteKategorie.GruppeVon(eintrag.Value.Art);
            satz.Kanton = kanton;
            satz.SatzPct = dto.SatzPct;
            satz.ValidFrom = dto.ValidFrom;
            if (!string.IsNullOrWhiteSpace(dto.Quelle)
                && !dto.Quelle.Trim().StartsWith(QstSatzart11.QuellePrefix, StringComparison.Ordinal))
                satz.Quelle = dto.Quelle.Trim();
        }
        satz.ValidTo = dto.ValidTo;
        await _db.SaveChangesAsync();
        return Ok(MapSatz(satz));
    }

    // ── DELETE /api/admin/quellensteuer/sonder-satz/{id} ──────────────────
    [HttpDelete("sonder-satz/{id:int}")]
    public async Task<IActionResult> SonderSatzLoeschen(int id)
    {
        var satz = await _db.QstSonderkategorieSaetze.FirstOrDefaultAsync(s => s.Id == id);
        if (satz == null) return NotFound(new { error = "NICHT_GEFUNDEN" });
        if (IstEstv(satz))
            return Conflict(new { error = "ESTV_GESCHUETZT",
                message = "ESTV-Zeile nicht löschen — sie kommt mit der nächsten Tarifdatei wieder. Stattdessen Gültig-bis setzen." });
        _db.QstSonderkategorieSaetze.Remove(satz);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    private async Task SchliesseVorgaengerAsync(string code, string kanton, DateOnly neuVon, int? predecessorId)
    {
        IQueryable<QstSonderkategorieSatz> q = _db.QstSonderkategorieSaetze
            .Where(s => s.Code == code && s.Kanton == kanton && s.ValidTo == null && s.ValidFrom < neuVon);
        if (predecessorId is int pid)
            q = _db.QstSonderkategorieSaetze.Where(s => s.Id == pid && s.ValidFrom < neuVon);
        foreach (var v in await q.ToListAsync())
            v.ValidTo = neuVon.AddDays(-1);
    }

    private IActionResult? PruefeSatzDto(SonderSatzDto? dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.Code) || string.IsNullOrWhiteSpace(dto.Kanton))
            return BadRequest(new { error = "PARAMS", message = "Code, Kanton und Gültig-ab angeben." });
        if (QstVordefinierteKategorie.Parse(dto.Code) == null)
            return BadRequest(new { error = "CODE", message = $"Unbekannter Sondercode «{dto.Code.Trim().ToUpperInvariant()}»." });
        var kt = dto.Kanton.Trim().ToUpperInvariant();
        if (kt.Length != 2 || !kt.All(char.IsLetter))
            return BadRequest(new { error = "KANTON", message = "Kanton als zwei Buchstaben." });
        if (dto.SatzPct < 0 || dto.SatzPct > 100)
            return BadRequest(new { error = "SATZ", message = "Satz muss zwischen 0 und 100 % liegen." });
        if (dto.ValidTo != null && dto.ValidTo < dto.ValidFrom)
            return BadRequest(new { error = "DATUM", message = "Gültig-bis liegt vor Gültig-ab." });
        return null;
    }

    private static bool IstEstv(QstSonderkategorieSatz s)
        => (s.Quelle ?? "").StartsWith(QstSatzart11.QuellePrefix, StringComparison.Ordinal);

    private static object MapSatz(QstSonderkategorieSatz s) => new
    {
        s.Id, s.Code, s.Gruppe, s.Kanton, s.SatzPct, s.Quelle, s.ValidFrom, s.ValidTo,
        estv = IstEstv(s)
    };

    public record SonderSatzDto(
        string Code, string Kanton, decimal SatzPct, string? Quelle,
        DateOnly ValidFrom, DateOnly? ValidTo, int? PredecessorId);
}
