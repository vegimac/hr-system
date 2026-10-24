using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Models;
using HrSystem.Services;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 4b: Swissdec-Lohnarten der Testfälle (wagetypes_export.csv) mit
/// OneCrew-Lohnpositionen verbinden (Walter 08.09.2026, Neufassung).
///
/// Prinzip: Die interne Nummer bleibt Schlüssel (Lohnschema, FIBU); die
/// Swissdec-Bedeutung hängt als <see cref="Lohnposition.SwissdecLohnart"/> an
/// der Position — wie bei Mirus, wo 60.3 «Versicherungstaggeld UVG» die
/// Swissdec-Lohnart 2030 ist. Ablauf pro verwendeter Swissdec-Lohnart:
///   1. Eine Position trägt die Swissdec-Lohnart bereits → nichts zu tun.
///   2. OneCrew rechnet die Lohnart selbst (Monatslohn, Stundenlohn, Ferien,
///      Feiertag, 13. ML, Kinderzulage …) → die Swissdec-Lohnart wird an den
///      bestehenden OneCrew-Positionen eingetragen (Zuordnungstabelle unten;
///      nur wo noch leer, Handeinträge werden nie überschrieben).
///   3. Sonst → neue Position mit der Swissdec-Nummer als Code und den
///      Pflichten/Lohnausweis-Ziffer aus dem Musterlohnartenstamm
///      (Assets/Swissdec/SwissdecLohnarten.json); danach an der Position editierbar.
/// 5050 (BVG-Beitrag) wird nicht als Position gebucht — Schritt 5b setzt daraus
/// den BVG-Fixbetrag am Versicherungs-Code.
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
        var verwendet = new Dictionary<string, (string Label, int Zeilen)>();
        foreach (var ln in System.IO.File.ReadAllLines(pfad).Skip(1))
        {
            var t = ln.Split(',');
            if (t.Length < 3) continue;
            var code = t[1].Trim(); if (code.Length == 0) continue;
            verwendet[code] = (t[2].Trim(), (verwendet.TryGetValue(code, out var v) ? v.Zeilen : 0) + 1);
        }

        var positionen = await _db.Lohnpositionen.ToListAsync();
        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();
        int bereits = 0, zugeordnet = 0, neu = 0, uebersprungen = 0;
        int maxSort = positionen.Count == 0 ? 0 : positionen.Max(p => p.SortOrder);

        foreach (var (code, info) in verwendet.OrderBy(k => k.Key))
        {
            var felder = new Dictionary<string, string?> { ["Swissdec"] = $"{code} {info.Label}", ["Lohnzeilen"] = info.Zeilen.ToString() };

            if (code == "5050")
            {
                felder["OneCrew"] = "BVG-Fixbetrag am Versicherungs-Code (Schritt 5b) — keine Lohnposition";
                aktionen.Add(new Aktion("aktualisieren", "Lohnart (kein Import)", code, felder));
                uebersprungen++; continue;
            }

            // 1) schon zugeordnet?
            var vorhandene = positionen.Where(p => p.SwissdecLohnart == code && p.IsActive).ToList();
            if (vorhandene.Count > 0)
            {
                felder["OneCrew"] = string.Join(" · ", vorhandene.Select(p => $"{p.Code} {p.Bezeichnung}")) + " (bereits zugeordnet)";
                aktionen.Add(new Aktion("aktualisieren", "Lohnart", $"{code} → {string.Join(", ", vorhandene.Select(p => p.Code))}", felder));
                bereits++; continue;
            }

            // 2) OneCrew rechnet selbst → Swissdec-Lohnart an bestehenden Positionen eintragen
            var ziele = SwissdecLohnartZuordnung.NachOneCrewCode
                .Where(z => z.Value == code).Select(z => z.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var kandidaten = positionen.Where(p => p.IsActive && ziele.Contains(p.Code) && string.IsNullOrEmpty(p.SwissdecLohnart)).ToList();
            if (kandidaten.Count > 0)
            {
                felder["OneCrew"] = string.Join(" · ", kandidaten.Select(p => $"{p.Code} {p.Bezeichnung}")) + " (Swissdec-Lohnart eintragen)";
                aktionen.Add(new Aktion("aktualisieren", "Lohnposition", $"{code} → {string.Join(", ", kandidaten.Select(p => p.Code))}", felder));
                if (!vorschau) foreach (var p in kandidaten) p.SwissdecLohnart = code;
                zugeordnet++; continue;
            }

            // 3) neu aus dem Musterlohnartenstamm
            var k = SwissdecLohnartenKatalog.Finde(code);
            if (k == null)
            {
                hinweise.Add($"Lohnart {code} «{info.Label}» fehlt im Swissdec-Katalog (Assets/Swissdec/SwissdecLohnarten.json) — manuell anlegen.");
                uebersprungen++; continue;
            }
            var basen = string.Join(" ", new[] { k.Ahv ? "AHV" : null, k.Uvg ? "UVG" : null, k.Uvgz ? "UVGZ" : null, k.Ktg ? "KTG" : null, k.Bvg ? "BVG" : null, k.Qst ? "QST" : null, k.Ml13 ? "13.ML" : null }.Where(x => x != null));
            felder["OneCrew"] = $"neu: {code} {k.Bezeichnung} · {k.Typ} · Basen {(basen.Length == 0 ? "keine" : basen)}" + (k.Lohnausweis.Length > 0 ? $" · LA {k.Lohnausweis}" : "");
            aktionen.Add(new Aktion("anlegen", "Lohnposition", $"{code} {k.Bezeichnung}", felder));
            neu++;
            if (vorschau) continue;

            // Code-Kollision mit einer bestehenden OneCrew-Position (z.B. «1000»)? Dann Suffix.
            var lpCode = code;
            if (positionen.Any(p => p.Code == lpCode && p.IsActive)) lpCode = code + ".sd";
            var lp = new Lohnposition
            {
                Code = lpCode, Bezeichnung = k.Bezeichnung, Typ = k.Typ,
                AhvAlvPflichtig = k.Ahv, NbuvPflichtig = k.Uvg, UvgPflichtig = k.Uvg, UvgzPflichtig = k.Uvgz,
                KtgPflichtig = k.Ktg, BvgPflichtig = k.Bvg, QstPflichtig = k.Qst,
                ZaehltAlsBasis13ml = k.Ml13, LohnausweisCode = k.Lohnausweis.Length > 0 ? k.Lohnausweis : null,
                SwissdecLohnart = code, QstPeriodisch = !Lohnposition.SwissdecEinmalig(code),
                QstCharakter = Lohnposition.SwissdecEinmalig(code) ? LohnartSteuerung.QstJaehrlich : LohnartSteuerung.QstMonatlich,
                BvgBetrachtung = LohnartSteuerung.BvgRueckwirkend,
                BvgFaktor = code is "1000" or "1005" or "1006" ? 13m : 1m,
                SortOrder = ++maxSort, IsActive = true, CreatedAt = DateTime.Now,   // created_at ohne Zeitzone → lokale Zeit (Npgsql-Kind)
            };
            WendeSwissdecBasisFlagsAn(lp);
            _db.Lohnpositionen.Add(lp);
            positionen.Add(lp);
        }
        if (!vorschau)
        {
            foreach (var lp in positionen.Where(p => p.IsActive && (p.SwissdecLohnart == "1006" || p.SwissdecLohnart == "1070" || p.SwissdecLohnart == "1033" || p.SwissdecLohnart == "1218")))
                WendeSwissdecBasisFlagsAn(lp);
            await _db.SaveChangesAsync();
            _log.LogInformation("Swissdec-Testmandant Schritt 4b: {Neu} neu, {Z} zugeordnet, {B} bereits, {U} kein Import", neu, zugeordnet, bereits, uebersprungen);
        }
        hinweise.Insert(0, $"{verwendet.Count} Swissdec-Lohnarten in den Testfällen: {bereits} bereits zugeordnet, {zugeordnet} an bestehende OneCrew-Positionen zugeordnet, {neu} neu, {uebersprungen} kein Import.");
        hinweise.Add("Neue Positionen tragen die Swissdec-Nummer als Code und die Pflichten aus dem Musterlohnartenstamm — danach im Lohnpositionen-Dialog editierbar (Feld «Swissdec-Lohnart»). Für die Anzeige beim MA müssen sie im Lohnschema des Vertragsmodells stehen.");
        hinweise.Add("1006 Lektionenlohn: Ferien-, Feiertag- und 13.-ML-Basis (wie Stundenlohn). 1070 Schichtzulage, 1033 Ortszulage und 1218 Provision: nicht 13.-ML-Basis (CSV 1200/1201 ohne Zulage; TF38 Dez 13. ML = 3'000 = nur Monatslohn).");
        return Ok(new SchrittErgebnis("4b · Lohnpositionen ↔ Swissdec-Lohnarten", vorschau, aktionen, hinweise));
    }

    /// <summary>
    /// CSV-massgebliche Basis-Flags, die der Musterlohnartenstamm nicht kennt
    /// (Ferien/Feiertag) bzw. bei Schicht (1070), Ortszulage (1033) und Provision (1218) widerspricht.
    /// </summary>
    private static void WendeSwissdecBasisFlagsAn(Lohnposition lp)
    {
        if (lp.SwissdecLohnart == "1006")
        {
            lp.ZaehltAlsBasisFerien = true;
            lp.ZaehltAlsBasisFeiertag = true;
            lp.ZaehltAlsBasis13ml = true;
        }
        else if (lp.SwissdecLohnart is "1070" or "1033" or "1218")
            lp.ZaehltAlsBasis13ml = false;   // 1218 Provision: TF38 Dez 13. ML = 3'000 = nur Monatslohn (Claude 21.09.2026)
    }
}
