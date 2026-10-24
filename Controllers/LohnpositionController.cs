using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.LohnFormelwerk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/lohnpositionen")]
[Authorize]
public class LohnpositionController : ControllerBase
{
    private readonly AppDbContext _db;
    public LohnpositionController(AppDbContext db) => _db = db;

    // GET – alle Positionen (aktiv + inaktiv), sortiert
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var items = await _db.Lohnpositionen
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Code)
            .ToListAsync();
        var verwendet = await VerwendeteCodesAsync();
        foreach (var item in items)
            item.InLohnVerwendet = verwendet.Contains(item.Code);
        return Ok(items);
    }

    // GET – nur aktive Positionen
    [HttpGet("active")]
    public async Task<IActionResult> GetActive()
    {
        var items = await _db.Lohnpositionen
            .Where(l => l.IsActive)
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Code)
            .ToListAsync();
        return Ok(items);
    }

    // GET api/lohnpositionen/swissdec-lohnarten — Musterlohnartenstamm für die
    // Auswahlliste «Swissdec-Lohnart» im Dialog (Walter 08.09.2026).
    [HttpGet("swissdec-lohnarten")]
    public IActionResult SwissdecLohnarten()
        => Ok(SwissdecLohnartenKatalog.Alle
            .Select(l => new { l.Code, l.Bezeichnung, l.Kategorie, l.Typ, l.Ahv, l.Uvg, l.Uvgz, l.Ktg, l.Bvg, l.Qst, l.Ml13, l.Lohnausweis, l.FibuKonto }));

    // GET api/lohnpositionen/swissdec-zuordnung — feste OneCrew→Swissdec-Tabelle + Ist-Stand (Walter 17.10.2026).
    [HttpGet("swissdec-zuordnung")]
    public async Task<IActionResult> SwissdecZuordnung()
    {
        var positionen = await _db.Lohnpositionen.AsNoTracking()
            .Where(p => p.IsActive)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Code)
            .ToListAsync();
        var katalog = SwissdecLohnartenKatalog.Alle.ToDictionary(k => k.Code, k => k.Bezeichnung);
        var zeilen = positionen.Select(p =>
        {
            var vorschlag = SwissdecLohnartZuordnung.FuerOneCrewCode(p.Code);
            var ist = string.IsNullOrWhiteSpace(p.SwissdecLohnart) ? null : p.SwissdecLohnart.Trim();
            return new
            {
                p.Id,
                oneCrewCode = p.Code,
                oneCrewBezeichnung = p.Bezeichnung,
                swissdecIst = ist,
                swissdecIstBez = ist != null && katalog.TryGetValue(ist, out var ib) ? ib : null,
                swissdecVorschlag = vorschlag,
                swissdecVorschlagBez = vorschlag != null && katalog.TryGetValue(vorschlag, out var vb) ? vb : null,
                status = ist == null
                    ? (vorschlag != null ? "leer_mit_vorschlag" : "leer_ohne_vorschlag")
                    : (vorschlag != null && !string.Equals(ist, vorschlag, StringComparison.OrdinalIgnoreCase)
                        ? "abweichend"
                        : "ok")
            };
        }).ToList();
        return Ok(new
        {
            zuordnungstabelle = SwissdecLohnartZuordnung.AlsListe()
                .Select(z => new
                {
                    oneCrewCode = z.OneCrew,
                    swissdecCode = z.Swissdec,
                    swissdecBez = katalog.TryGetValue(z.Swissdec, out var b) ? b : null
                }),
            positionen = zeilen,
            zusammenfassung = new
            {
                total = zeilen.Count,
                ok = zeilen.Count(z => z.status == "ok"),
                leerMitVorschlag = zeilen.Count(z => z.status == "leer_mit_vorschlag"),
                leerOhneVorschlag = zeilen.Count(z => z.status == "leer_ohne_vorschlag"),
                abweichend = zeilen.Count(z => z.status == "abweichend")
            }
        });
    }

    // POST api/lohnpositionen/swissdec-zuordnung-anwenden — leere Felder aus Vorschlag füllen (Walter 17.10.2026).
    // nurLeere=true (Default): überschreibt nie eine vorhandene Swissdec-Lohnart.
    [HttpPost("swissdec-zuordnung-anwenden")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SwissdecZuordnungAnwenden([FromQuery] bool nurLeere = true)
    {
        var positionen = await _db.Lohnpositionen.Where(p => p.IsActive).ToListAsync();
        var verwendet = await VerwendeteCodesAsync();
        int gesetzt = 0, uebersprungen = 0, ohneVorschlag = 0;
        var details = new List<object>();
        foreach (var p in positionen)
        {
            var vorschlag = SwissdecLohnartZuordnung.FuerOneCrewCode(p.Code);
            if (vorschlag == null) { ohneVorschlag++; continue; }
            if (nurLeere && !string.IsNullOrWhiteSpace(p.SwissdecLohnart)) { uebersprungen++; continue; }
            if (verwendet.Contains(p.Code)) { uebersprungen++; continue; }
            if (string.Equals(p.SwissdecLohnart?.Trim(), vorschlag, StringComparison.OrdinalIgnoreCase))
            { uebersprungen++; continue; }
            var alt = p.SwissdecLohnart;
            p.SwissdecLohnart = vorschlag;
            gesetzt++;
            details.Add(new { p.Code, p.Bezeichnung, vorher = alt, nachher = vorschlag });
        }
        if (gesetzt > 0) await _db.SaveChangesAsync();
        return Ok(new { gesetzt, uebersprungen, ohneVorschlag, details });
    }

    // GET – einzelne Position
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _db.Lohnpositionen.FindAsync(id);
        if (item is null) return NotFound();
        item.InLohnVerwendet = (await VerwendeteCodesAsync()).Contains(item.Code);
        return Ok(item);
    }

    // POST – neue Position anlegen (nur Admin)
    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] Lohnposition dto)
    {
        dto.Id        = 0;
        dto.IsActive  = true;
        dto.Code      = (dto.Code ?? "").Trim();
        dto.MirusCode = string.IsNullOrWhiteSpace(dto.MirusCode) ? null : dto.MirusCode.Trim();
        dto.FormelId  = string.IsNullOrWhiteSpace(dto.FormelId) ? null : dto.FormelId.Trim();
        if (!LohnCodes.IstSwissdecStammNr(dto.Code) && !LohnCodes.IstInternOhneSwissdec(dto.Code)
            && !string.Equals(dto.Code, "180.3", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "CODE_NICHT_SWISSDEC",
                message = "Nur Swissdec-Stammnummern oder bekannte Betrieb-/Vortrag-Codes (600.24, 901–906, 180.3 …)." });
        // code = Swissdec-Stamm; swissdec_lohnart = vierstelliger ELM-Kopf (leer bei Betrieb)
        dto.SwissdecLohnart = LohnCodes.IstInternOhneSwissdec(dto.Code)
            ? null
            : SwissdecKopf(dto.Code, dto.SwissdecLohnart);
        if (Lohnposition.SwissdecEinmalig(dto.SwissdecLohnart))
        {
            dto.QstPeriodisch = false;
            dto.QstCharakter = LohnartSteuerung.QstJaehrlich;
        }
        else if (string.IsNullOrWhiteSpace(dto.QstCharakter))
            dto.QstCharakter = dto.QstPeriodisch ? LohnartSteuerung.QstMonatlich : LohnartSteuerung.QstJaehrlich;
        var stammNeu = SwissdecLohnartenKatalog.FindeBvg(dto.Code);
        if (stammNeu?.BvgErwartet != null)
        {
            var z = LohnartSteuerung.BvgAbleiten(stammNeu.BvgErwartet.Value, stammNeu.BvgRueck ?? 0, stammNeu.BvgFaktorRoh);
            dto.BvgBetrachtung = z.Betrachtung;
            dto.BvgFaktor = z.Faktor;
            dto.BvgFaktorX = z.FaktorX;
            dto.BvgRueckCode = z.RueckCode;
            dto.BvgPflichtig = z.Pflichtig;
        }
        else
            LohnartSteuerung.SetzeBvg(dto, dto.BvgBetrachtung, dto.BvgFaktor, dto.BvgFaktorX, dto.BvgRueckCode, pflichtAusBetrachtung: false);
        dto.CreatedAt = DateTime.Now;
        _db.Lohnpositionen.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(dto);
    }

    // PUT – Position aktualisieren (nur Admin)
    [HttpPut("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Update(int id, [FromBody] Lohnposition dto)
    {
        var item = await _db.Lohnpositionen.FindAsync(id);
        if (item is null) return NotFound();

        var neuerCode = (dto.Code ?? "").Trim();
        if (!LohnCodes.IstSwissdecStammNr(neuerCode) && !LohnCodes.IstInternOhneSwissdec(neuerCode)
            && !string.Equals(neuerCode, "180.3", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "CODE_NICHT_SWISSDEC",
                message = "Nur Swissdec-Stammnummern oder bekannte Betrieb-/Vortrag-Codes (600.24, 901–906, 180.3 …)." });
        var probe = new Lohnposition
        {
            Code = neuerCode,
            Typ = dto.Typ,
            AhvAlvPflichtig = dto.AhvAlvPflichtig,
            NbuvPflichtig = dto.NbuvPflichtig,
            UvgPflichtig = dto.UvgPflichtig,
            UvgzPflichtig = dto.UvgzPflichtig,
            KtgPflichtig = dto.KtgPflichtig,
            BvgPflichtig = dto.BvgPflichtig,
            QstPflichtig = dto.QstPflichtig,
            QstPeriodisch = dto.QstPeriodisch,
            QstCharakter = dto.QstCharakter,
            BvgBetrachtung = dto.BvgBetrachtung,
            BvgFaktor = dto.BvgFaktor,
            BvgFaktorX = dto.BvgFaktorX,
            BvgRueckCode = dto.BvgRueckCode,
            StatistikMonat = dto.StatistikMonat,
            StatistikJahr = dto.StatistikJahr,
            DreijehnterMlPflichtig = dto.DreijehnterMlPflichtig,
            ZaehltAlsBasisFeiertag = dto.ZaehltAlsBasisFeiertag,
            ZaehltAlsBasisFerien = dto.ZaehltAlsBasisFerien,
            ZaehltAlsBasis13ml = dto.ZaehltAlsBasis13ml,
            LohnausweisCode = dto.LohnausweisCode,
            SwissdecLohnart = LohnCodes.IstInternOhneSwissdec(neuerCode)
                ? null
                : SwissdecKopf(neuerCode, dto.SwissdecLohnart),
            IsActive = dto.IsActive,
        };
        var stamm = SwissdecLohnartenKatalog.FindeBvg(neuerCode);
        LohnartSteuerung.SetzeBvg(probe, dto.BvgBetrachtung, dto.BvgFaktor, dto.BvgFaktorX, dto.BvgRueckCode,
            pflichtAusBetrachtung: stamm?.BvgErwartet != null);
        if ((await VerwendeteCodesAsync()).Contains(item.Code)
            && LohnartSperre.SteuerungGeaendert(item, probe))
            return Conflict(new { error = LohnartSperre.Fehler, message = LohnartSperre.MeldungAendern(item.Code, item.Bezeichnung) });

        item.Code            = neuerCode;
        item.MirusCode       = string.IsNullOrWhiteSpace(dto.MirusCode) ? null : dto.MirusCode.Trim();
        item.FormelId        = string.IsNullOrWhiteSpace(dto.FormelId) ? null : dto.FormelId.Trim();
        item.Bezeichnung     = dto.Bezeichnung;
        item.Typ             = dto.Typ;
        item.AhvAlvPflichtig = dto.AhvAlvPflichtig;
        item.NbuvPflichtig   = dto.NbuvPflichtig;
        item.UvgPflichtig    = dto.UvgPflichtig;
        item.UvgzPflichtig   = dto.UvgzPflichtig;
        item.KtgPflichtig    = dto.KtgPflichtig;
        item.BvgPflichtig    = dto.BvgPflichtig;
        item.QstPflichtig           = dto.QstPflichtig;
        item.QstPeriodisch          = dto.QstPeriodisch;
        item.QstCharakter           = string.IsNullOrWhiteSpace(dto.QstCharakter) ? LohnartSteuerung.QstMonatlich : dto.QstCharakter.Trim();
        item.BvgBetrachtung         = probe.BvgBetrachtung;
        item.BvgFaktor              = probe.BvgFaktor;
        item.BvgFaktorX             = probe.BvgFaktorX;
        item.BvgRueckCode           = probe.BvgRueckCode;
        if (stamm?.BvgErwartet != null) item.BvgPflichtig = probe.BvgPflichtig;
        item.StatistikMonat         = string.IsNullOrWhiteSpace(dto.StatistikMonat) ? null : dto.StatistikMonat.Trim();
        item.StatistikJahr          = string.IsNullOrWhiteSpace(dto.StatistikJahr) ? null : dto.StatistikJahr.Trim();
        item.DreijehnterMlPflichtig = dto.DreijehnterMlPflichtig;
        item.ZaehltAlsBasisFeiertag = dto.ZaehltAlsBasisFeiertag;
        item.ZaehltAlsBasisFerien   = dto.ZaehltAlsBasisFerien;
        item.ZaehltAlsBasis13ml     = dto.ZaehltAlsBasis13ml;
        item.LohnausweisCode = dto.LohnausweisCode;
        item.SwissdecLohnart = LohnCodes.IstInternOhneSwissdec(item.Code)
            ? null
            : SwissdecKopf(item.Code, dto.SwissdecLohnart);
        item.SortOrder       = dto.SortOrder;
        item.IsActive        = dto.IsActive;

        await _db.SaveChangesAsync();
        return Ok(item);
    }

    /// <summary>
    /// Codes, die auf einem gespeicherten Lohnzettel stehen.
    /// Vorschau ohne Snapshot zählt nicht.
    /// </summary>
    async Task<HashSet<string>> VerwendeteCodesAsync()
    {
        var codes = await _db.Database.SqlQueryRaw<string>("""
            SELECT DISTINCT code AS "Value" FROM (
                SELECT elem->>'code' AS code
                  FROM payroll_snapshot s,
                       jsonb_array_elements(
                           CASE WHEN jsonb_typeof(s.slip_json->'lohnLines') = 'array'
                                THEN s.slip_json->'lohnLines' ELSE '[]'::jsonb END) elem
                UNION
                SELECT elem->>'code'
                  FROM payroll_snapshot s,
                       jsonb_array_elements(
                           CASE WHEN jsonb_typeof(s.slip_json->'zulagenExtraLines') = 'array'
                                THEN s.slip_json->'zulagenExtraLines' ELSE '[]'::jsonb END) elem
            ) q
            WHERE code IS NOT NULL AND code <> ''
            """).ToListAsync();
        return codes.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>ELM-Viersteller aus Positions-Code (1000.2 → 1000).</summary>
    static string? SwissdecKopf(string? code, string? explizit)
    {
        if (!string.IsNullOrWhiteSpace(explizit))
        {
            var e = explizit.Trim();
            return e.Length >= 4 ? e[..Math.Min(4, e.Length)] : e;
        }
        var k = LohnCodes.SwissdecVon(code);
        return string.IsNullOrEmpty(k) ? null : k;
    }

    /// <summary>Lohnartenstamm als Excel. Keine Vorschau — der Browser fragt «Speichern unter».</summary>
    [HttpGet("export")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Export()
    {
        var items = await _db.Lohnpositionen.OrderBy(l => l.Code).ToListAsync();
        var verwendet = await VerwendeteCodesAsync();
        var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet("Lohnarten");
        var bold = wb.CreateFont();
        bold.IsBold = true;
        var hdr = wb.CreateCellStyle();
        hdr.SetFont(bold);
        hdr.FillForegroundColor = IndexedColors.Grey25Percent.Index;
        hdr.FillPattern = FillPattern.SolidForeground;
        var headers = new[]
        {
            "Nr", "Bezeichnung", "Typ", "AHV", "UVG", "NBU", "UVGZ", "KTG", "BVG",
            "BVG-Betrachtung", "BVG-Faktor", "BVG-Rück", "QST", "QST periodisch", "QST-Charakter",
            "Lohnausweis", "Statistik Monat", "Statistik Jahr", "früher", "aktiv", "im Lohn"
        };
        var hr = sheet.CreateRow(0);
        for (int i = 0; i < headers.Length; i++)
        {
            var c = hr.CreateCell(i);
            c.SetCellValue(headers[i]);
            c.CellStyle = hdr;
        }
        static string Ja(bool v) => v ? "ja" : "nein";
        for (int i = 0; i < items.Count; i++)
        {
            var l = items[i];
            var row = sheet.CreateRow(i + 1);
            var col = 0;
            row.CreateCell(col++).SetCellValue(l.Code);
            row.CreateCell(col++).SetCellValue(l.Bezeichnung);
            row.CreateCell(col++).SetCellValue(l.Typ == "ABZUG" ? "Abzug" : "Zulage");
            row.CreateCell(col++).SetCellValue(Ja(l.AhvAlvPflichtig));
            row.CreateCell(col++).SetCellValue(Ja(l.UvgPflichtig));
            row.CreateCell(col++).SetCellValue(Ja(l.NbuvPflichtig));
            row.CreateCell(col++).SetCellValue(Ja(l.UvgzPflichtig));
            row.CreateCell(col++).SetCellValue(Ja(l.KtgPflichtig));
            row.CreateCell(col++).SetCellValue(Ja(l.BvgPflichtig));
            row.CreateCell(col++).SetCellValue(l.BvgBetrachtung switch
            {
                LohnartSteuerung.BvgVoraussichtlich => "voraussichtlich",
                LohnartSteuerung.BvgKeine => "keine",
                _ => "rückwirkend"
            });
            row.CreateCell(col++).SetCellValue(l.BvgFaktorX ? "x" : l.BvgFaktor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            row.CreateCell(col++).SetCellValue(l.BvgRueckCode);
            row.CreateCell(col++).SetCellValue(Ja(l.QstPflichtig));
            row.CreateCell(col++).SetCellValue(Ja(l.QstPeriodisch));
            row.CreateCell(col++).SetCellValue(l.QstCharakter == LohnartSteuerung.QstJaehrlich ? "jährlich" : "monatlich");
            row.CreateCell(col++).SetCellValue(l.LohnausweisCode ?? "");
            row.CreateCell(col++).SetCellValue(l.StatistikMonat ?? "");
            row.CreateCell(col++).SetCellValue(l.StatistikJahr ?? "");
            row.CreateCell(col++).SetCellValue(l.MirusCode ?? "");
            row.CreateCell(col++).SetCellValue(Ja(l.IsActive));
            row.CreateCell(col++).SetCellValue(verwendet.Contains(l.Code) ? "ja" : "nein");
        }
        for (int i = 0; i < headers.Length; i++) sheet.AutoSizeColumn(i);
        using var ms = new MemoryStream();
        wb.Write(ms);
        var name = $"Lohnartenstamm_{DateTime.Now:yyyy-MM-dd}.xlsx";
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }

    // DELETE – soft-delete (nur Admin)
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _db.Lohnpositionen.FindAsync(id);
        if (item is null) return NotFound();
        if ((await VerwendeteCodesAsync()).Contains(item.Code))
            return Conflict(new { error = LohnartSperre.Fehler, message = LohnartSperre.MeldungLoeschen(item.Code, item.Bezeichnung) });
        item.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
