using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// ELM-Lohnraster-PickList (Walter-Vorgabe 17.08.2026): read-only-Referenz-
/// katalog aller 309 Raster-Positionen. «Uebernehmen» legt aus einem Eintrag
/// eine OneCrew-Lohnposition an und verlinkt sie; «Verknuepfen» ordnet eine
/// BESTEHENDE Lohnposition zu (Alias, ohne Neuanlage); «Loesen» hebt die
/// Verknuepfung auf (loescht die Lohnposition NICHT).
/// Reiner Katalog ohne Lohnlauf-Datumsbezug — im EditLock-Audit whitelisted.
/// </summary>
[Authorize(Roles = "admin")]
[ApiController]
[Route("api/elm-lohnraster")]
public class ElmLohnrasterController : ControllerBase
{
    private readonly AppDbContext _db;
    public ElmLohnrasterController(AppDbContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Auto-Match (Walter-Vorgabe 17.08.2026): bestehende OneCrew-Lohnpositionen
        // mit identischem Code werden beim Laden automatisch verknuepft, damit die
        // Liste sofort zeigt, was schon im Lohn drin ist (Haekchen statt «—»).
        // Idempotent: nur unverlinkte Eintraege, nur exakte Code-Gleichheit,
        // nur wenn die Lohnposition nicht schon einem anderen Eintrag zugeordnet ist.
        // ACHTUNG: nur Code-Gleichheit reicht NICHT — die OneCrew-Codes wurden bei
        // der Migration vereinfacht und dieselbe Nummer kann etwas ANDERES bedeuten
        // (OneCrew 60.2 = Unfall-Taggeld 80% vs. Raster 60.2 = Taggeld Karenz 88%).
        // Darum verlangt der Auto-Match Code UND Kategorie/Gruppe (normalisiert).
        // Alles andere laeuft ueber die manuelle Verknuepfung bzw. das einmalige
        // Mapping-SQL (migrations-archive/link_elm_lohnraster_legacy.sql).
        static string Norm(string? s) =>
            new string((s ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var unverlinkt = await _db.ElmLohnraster
            .Where(e => e.VerwendetLohnpositionId == null)
            .ToListAsync();
        if (unverlinkt.Count > 0)
        {
            var lohnposList = await _db.Lohnpositionen
                .Select(l => new { l.Id, l.Code, l.Kategorie })
                .ToListAsync();
            var lohnposByCode = lohnposList
                .GroupBy(l => l.Code)
                .ToDictionary(g => g.Key, g => g.First());
            var schonBelegt = await _db.ElmLohnraster
                .Where(e => e.VerwendetLohnpositionId != null)
                .Select(e => e.VerwendetLohnpositionId!.Value)
                .ToListAsync();
            var belegtSet = new HashSet<int>(schonBelegt);
            var geaendert = false;
            foreach (var e in unverlinkt)
            {
                if (lohnposByCode.TryGetValue(e.Code, out var lp)
                    && !belegtSet.Contains(lp.Id)
                    && Norm(lp.Kategorie) == Norm(e.Gruppe))
                {
                    e.VerwendetLohnpositionId = lp.Id;
                    belegtSet.Add(lp.Id);
                    geaendert = true;
                }
            }
            if (geaendert) await _db.SaveChangesAsync();
        }

        var rows = await _db.ElmLohnraster.AsNoTracking()
            .Include(e => e.VerwendetLohnposition)
            .ToListAsync();
        return Ok(rows
            .OrderBy(e => decimal.TryParse(e.Pos, out var p) ? p : 9999)
            .ThenBy(e => decimal.TryParse(e.Sub, out var s) ? s : 0)
            .Select(e => new
            {
                e.Id, e.Code, e.Bezeichnung, e.Gruppe, e.Typ,
                e.UebersetzungIt, e.UebersetzungFr,
                e.Lohnausweisfeld, e.StatistikCode, e.Steuerung, e.BetragProzent,
                e.Inaktiv, e.Ahv, e.Qst, e.QstPeriodisch, e.Bvg, e.Uvg, e.Uvgz, e.Ktg, e.Ml13,
                attrs = e.AttrsJson,
                verwendetLohnpositionId = e.VerwendetLohnpositionId,
                verwendetCode = e.VerwendetLohnposition?.Code,
                verwendetBezeichnung = e.VerwendetLohnposition?.Bezeichnung,
            }));
    }

    /// <summary>Legt aus dem Raster-Eintrag eine neue Lohnposition an + verlinkt.</summary>
    [HttpPost("{id}/uebernehmen")]
    public async Task<IActionResult> Uebernehmen(int id)
    {
        var e = await _db.ElmLohnraster.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        if (e.VerwendetLohnpositionId != null)
            return Conflict(new { error = "SCHON_VERWENDET", message = "Diese Position ist bereits in OneCrew übernommen." });
        if (e.Typ != "LOHNART")
            return BadRequest(new { error = "NUR_LOHNARTEN", message = "Nur Lohnarten können als Lohnposition übernommen werden — SV-Abzüge/Absenzarten werden über SV-Sätze bzw. Absenz-Typen gepflegt." });
        // Existiert der Code schon als Lohnposition → direkt verknuepfen statt Fehler
        // (Haekchen-Semantik: «ist in OneCrew» — egal ob neu angelegt oder schon da).
        var vorhanden = await _db.Lohnpositionen.FirstOrDefaultAsync(l => l.Code == e.Code);
        if (vorhanden != null)
        {
            var anderweitigBelegt = await _db.ElmLohnraster
                .AnyAsync(x => x.VerwendetLohnpositionId == vorhanden.Id && x.Id != id);
            if (anderweitigBelegt)
                return Conflict(new { error = "LOHNPOSITION_BELEGT", message = $"Lohnposition «{e.Code}» ist bereits einem anderen Raster-Eintrag zugeordnet." });
            e.VerwendetLohnpositionId = vorhanden.Id;
            await _db.SaveChangesAsync();
            return Ok(new { lohnpositionId = vorhanden.Id, code = vorhanden.Code, verknuepft = true });
        }

        // Lohnausweisfeld «9.  Beiträge …» → Kurzcode «9»
        string? laFeld = null;
        if (!string.IsNullOrWhiteSpace(e.Lohnausweisfeld))
            laFeld = e.Lohnausweisfeld.Split('.')[0].Trim();

        var lp = new Lohnposition
        {
            Code            = e.Code,
            Bezeichnung     = e.Bezeichnung,
            Kategorie       = e.Gruppe ?? "",
            Typ             = e.Steuerung == "Negativ" ? "ABZUG" : "ZULAGE",
            AhvAlvPflichtig = e.Ahv  ?? true,
            NbuvPflichtig   = e.Uvg  ?? true,
            KtgPflichtig    = e.Ktg  ?? true,
            BvgPflichtig    = e.Bvg  ?? true,
            // ACHTUNG (Walter-Erkenntnis 17.08.2026, Basen-Kontrolle Juli):
            // das Raster-Attribut «qstpfl» ist im Export durchgängig LEER und
            // «Periodische Lohnart (QST)» beschreibt nur die Periodizität,
            // NICHT die Pflicht. QST-Pflicht folgt in der Praxis der AHV-
            // Pflicht (steuerbar was AHV-pflichtig ist; Spesen etc. beide frei).
            QstPflichtig    = e.Ahv ?? true,
            ZaehltAlsBasis13ml = e.Ml13 ?? false,
            Lohnausweisfeld = laFeld,
            IsActive        = true,
        };
        _db.Lohnpositionen.Add(lp);
        await _db.SaveChangesAsync();
        e.VerwendetLohnpositionId = lp.Id;
        await _db.SaveChangesAsync();
        return Ok(new { lohnpositionId = lp.Id, code = lp.Code });
    }

    public class VerknuepfenDto { public int LohnpositionId { get; set; } }

    /// <summary>Ordnet eine BESTEHENDE Lohnposition zu (kein Neuanlegen).</summary>
    [HttpPost("{id}/verknuepfen")]
    public async Task<IActionResult> Verknuepfen(int id, [FromBody] VerknuepfenDto dto)
    {
        var e = await _db.ElmLohnraster.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        var lp = await _db.Lohnpositionen.FirstOrDefaultAsync(l => l.Id == dto.LohnpositionId);
        if (lp == null) return NotFound(new { error = "LOHNPOSITION_FEHLT" });
        var belegt = await _db.ElmLohnraster.AnyAsync(x => x.VerwendetLohnpositionId == lp.Id && x.Id != id);
        if (belegt)
            return Conflict(new { error = "LOHNPOSITION_BELEGT", message = "Diese Lohnposition ist bereits einem anderen Raster-Eintrag zugeordnet." });
        e.VerwendetLohnpositionId = lp.Id;
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("{id}/loesen")]
    public async Task<IActionResult> Loesen(int id)
    {
        var e = await _db.ElmLohnraster.FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound();
        e.VerwendetLohnpositionId = null;
        await _db.SaveChangesAsync();
        return Ok();
    }
}
