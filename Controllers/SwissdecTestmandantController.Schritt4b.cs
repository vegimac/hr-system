using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Models;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 4b: Swissdec-Lohnarten der Testfälle (wagetypes_export.csv) als
/// OneCrew-Lohnpositionen — über den ELM-Lohnraster-Katalog (System → Lohnraster),
/// exakt wie «Übernehmen» dort: bereits verknüpfte Lohnarten bleiben (z.B. 1005
/// Stundenlohn → 20), fehlende werden mit den Basen-Häkchen des Rasters angelegt
/// und verknüpft. SV-Abzüge/Absenzarten (Typ ≠ LOHNART) werden nur gemeldet.
/// </summary>
public partial class SwissdecTestmandantController
{
    [HttpGet("schritt4b/vorschau")]
    public async Task<IActionResult> Schritt4bVorschau() => await Schritt4b(vorschau: true);

    [HttpPost("schritt4b/anlegen")]
    public async Task<IActionResult> Schritt4bAnlegen() => await Schritt4b(vorschau: false);

    private async Task<IActionResult> Schritt4b(bool vorschau)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var pfad = CsvPfad("wagetypes_export.csv");
        if (!System.IO.File.Exists(pfad))
            return NotFound(new { error = "CSV_FEHLT", message = $"Datei fehlt: {pfad}" });

        // testcaseId,sscTag,label,<Monate…>,total
        var verwendet = new Dictionary<string, (string Label, int Faelle, int Zeilen)>();
        foreach (var ln in System.IO.File.ReadAllLines(pfad).Skip(1))
        {
            var t = ln.Split(',');
            if (t.Length < 3) continue;
            var code = t[1].Trim(); if (code.Length == 0) continue;
            if (!verwendet.TryGetValue(code, out var v)) v = (t[2].Trim(), 0, 0);
            verwendet[code] = (v.Label, v.Faelle + 1, v.Zeilen + 1);
        }

        var raster = await _db.ElmLohnraster.Include(e => e.VerwendetLohnposition).ToListAsync();
        var positionen = await _db.Lohnpositionen.ToListAsync();
        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();
        int vorhanden = 0, verknuepft = 0, neu = 0, uebersprungen = 0;

        foreach (var (code, info) in verwendet.OrderBy(k => k.Key))
        {
            var e = raster.FirstOrDefault(r => r.Code == code);
            var felder = new Dictionary<string, string?> { ["Swissdec"] = $"{code} {info.Label}", ["Testfälle"] = info.Faelle.ToString() };
            if (e == null)
            {
                hinweise.Add($"Lohnart {code} «{info.Label}» steht nicht im ELM-Lohnraster — manuell als Lohnposition anlegen.");
                uebersprungen++; continue;
            }
            if (e.VerwendetLohnposition != null)
            {
                felder["OneCrew"] = $"{e.VerwendetLohnposition.Code} {e.VerwendetLohnposition.Bezeichnung} (bereits verknüpft)";
                aktionen.Add(new Aktion("aktualisieren", "Lohnart", $"{code} → {e.VerwendetLohnposition.Code}", felder));
                vorhanden++; continue;
            }
            if (e.Typ != "LOHNART")
            {
                felder["OneCrew"] = $"— ({e.Typ}: wird über SV-Sätze/Versicherungs-Codes bzw. Absenzen abgebildet)";
                aktionen.Add(new Aktion("aktualisieren", "Lohnart (kein Import)", code, felder));
                uebersprungen++; continue;
            }
            var lp = positionen.FirstOrDefault(l => l.Code == code);
            if (lp != null)
            {
                felder["OneCrew"] = $"{lp.Code} {lp.Bezeichnung} (vorhanden → verknüpfen)";
                aktionen.Add(new Aktion("aktualisieren", "Lohnart", $"{code} → {lp.Code}", felder));
                if (!vorschau) e.VerwendetLohnpositionId = lp.Id;
                verknuepft++; continue;
            }
            var basen = string.Join(" ", new[] { (e.Ahv ?? true) ? "AHV" : null, (e.Uvg ?? true) ? "UVG" : null, (e.Ktg ?? true) ? "KTG" : null, (e.Bvg ?? true) ? "BVG" : null, (e.Ml13 ?? false) ? "13.ML" : null }.Where(x => x != null));
            felder["OneCrew"] = $"neu: {code} {e.Bezeichnung} · {(e.Steuerung == "Negativ" ? "ABZUG" : "ZULAGE")} · Basen {basen}" + (e.Lohnausweisfeld != null ? $" · LA {e.Lohnausweisfeld.Split('.')[0].Trim()}" : "");
            aktionen.Add(new Aktion("anlegen", "Lohnposition", $"{code} {e.Bezeichnung}", felder));
            neu++;
            if (vorschau) continue;
            string? laFeld = string.IsNullOrWhiteSpace(e.Lohnausweisfeld) ? null : e.Lohnausweisfeld.Split('.')[0].Trim();
            lp = new Lohnposition
            {
                Code = e.Code, Bezeichnung = e.Bezeichnung, Kategorie = e.Gruppe ?? "",
                Typ = e.Steuerung == "Negativ" ? "ABZUG" : "ZULAGE",
                AhvAlvPflichtig = e.Ahv ?? true, NbuvPflichtig = e.Uvg ?? true, KtgPflichtig = e.Ktg ?? true, BvgPflichtig = e.Bvg ?? true,
                QstPflichtig = e.Ahv ?? true,   // wie ElmLohnrasterController.Uebernehmen (Walter 17.08.2026)
                ZaehltAlsBasis13ml = e.Ml13 ?? false, Lohnausweisfeld = laFeld, IsActive = true,
            };
            _db.Lohnpositionen.Add(lp);
            await _db.SaveChangesAsync();
            e.VerwendetLohnpositionId = lp.Id;
            positionen.Add(lp);
        }
        if (!vorschau)
        {
            await _db.SaveChangesAsync();
            _log.LogInformation("Swissdec-Testmandant Schritt 4b: {Neu} neu, {V} verknüpft, {B} bereits, {U} übersprungen", neu, verknuepft, vorhanden, uebersprungen);
        }
        hinweise.Insert(0, $"{verwendet.Count} Swissdec-Lohnarten in den Testfällen: {vorhanden} bereits verknüpft, {verknuepft} vorhanden → verknüpft, {neu} neu, {uebersprungen} kein Import.");
        hinweise.Add("Basen-Häkchen (AHV/UVG/KTG/BVG/13.ML) kommen aus dem ELM-Lohnraster; QST folgt der AHV-Pflicht (wie beim Übernehmen im Lohnraster). Vor Schritt 5 im Lohnschema pro Vertragsmodell prüfen.");
        return Ok(new SchrittErgebnis("4b · Lohnpositionen aus ELM-Lohnraster", vorschau, aktionen, hinweise));
    }
}
