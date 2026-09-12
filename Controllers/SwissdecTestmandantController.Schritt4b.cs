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

    /// <summary>
    /// OneCrew-Code → Swissdec-Lohnart für alles, was der Lohnlauf selbst rechnet.
    /// Mehrere OneCrew-Codes dürfen auf dieselbe Swissdec-Lohnart zeigen.
    /// Nur angewendet, wenn die Position existiert UND noch keine Swissdec-Lohnart hat.
    /// </summary>
    private static readonly (string OneCrew, string Swissdec)[] EngineZuordnung =
    {
        ("10.1", "1000"), ("10.2", "1000"), ("10.3", "1000"), ("10.4", "1000"),   // Festlohn (+ Ferien/Feiertage/Zusatzstd.)
        ("20", "1005"), ("20.1", "1005"), ("20.2", "1005"), ("20.3", "1005"), ("22", "1005"), ("55.3", "1005"), // Stundenlohn
        ("55.1", "1061"), ("55.2", "1065"),                                        // Überstunden 25 % / ohne Zuschlag
        ("40.1", "1160"), ("50.1", "1161"),                                        // ausbezahlte Ferien-/Feiertage
        ("60.3", "2030"), ("70.2", "2035"),                                        // Versicherungstaggeld UVG / KTG
        ("65.1", "2050"), ("65.2", "2050"), ("75.1", "2050"), ("75.2", "2050"),    // Korrektur Taggelder
        ("180.1", "1200"), ("180.2", "1200"),                                      // 13. Monatslohn
        ("190.1", "3000"), ("190.3", "3034"),                                      // Kinderzulage / Geburtszulage
        ("195.1", "1160"), ("195.3", "1160"), ("195.5", "1160"), ("195.6", "1160"), // Ferienentschädigung FLEX/MTP
        ("195.2", "1161"), ("195.4", "1161"),                                      // Feiertagentschädigung FLEX/MTP
        ("200.1", "6070"), ("200.5", "1210"), ("200.11", "1230"), ("200.20", "3034"), ("200.50", "1910"), ("200.60", "1900"),
    };

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
            var ziele = EngineZuordnung.Where(z => z.Swissdec == code).Select(z => z.OneCrew).ToHashSet();
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
                Code = lpCode, Bezeichnung = k.Bezeichnung, Kategorie = k.Kategorie, Typ = k.Typ,
                AhvAlvPflichtig = k.Ahv, NbuvPflichtig = k.Uvg, KtgPflichtig = k.Ktg, BvgPflichtig = k.Bvg, QstPflichtig = k.Qst,
                ZaehltAlsBasis13ml = k.Ml13, LohnausweisCode = k.Lohnausweis.Length > 0 ? k.Lohnausweis : null,
                SwissdecLohnart = code, SortOrder = ++maxSort, IsActive = true, CreatedAt = DateTime.Now,   // created_at ohne Zeitzone → lokale Zeit (Npgsql-Kind)
            };
            WendeSwissdecBasisFlagsAn(lp);
            _db.Lohnpositionen.Add(lp);
            positionen.Add(lp);
        }
        if (!vorschau)
        {
            foreach (var lp in positionen.Where(p => p.IsActive && (p.SwissdecLohnart == "1006" || p.SwissdecLohnart == "1070")))
                WendeSwissdecBasisFlagsAn(lp);
            await _db.SaveChangesAsync();
            _log.LogInformation("Swissdec-Testmandant Schritt 4b: {Neu} neu, {Z} zugeordnet, {B} bereits, {U} kein Import", neu, zugeordnet, bereits, uebersprungen);
        }
        hinweise.Insert(0, $"{verwendet.Count} Swissdec-Lohnarten in den Testfällen: {bereits} bereits zugeordnet, {zugeordnet} an bestehende OneCrew-Positionen zugeordnet, {neu} neu, {uebersprungen} kein Import.");
        hinweise.Add("Neue Positionen tragen die Swissdec-Nummer als Code und die Pflichten aus dem Musterlohnartenstamm — danach im Lohnpositionen-Dialog editierbar (Feld «Swissdec-Lohnart»). Für die Anzeige beim MA müssen sie im Lohnschema des Vertragsmodells stehen.");
        hinweise.Add("1006 Lektionenlohn: Ferien-, Feiertag- und 13.-ML-Basis (wie Stundenlohn). 1070 Schichtzulage: nicht 13.-ML-Basis (CSV 1201/1202 ohne Schicht, trotz Katalog ml13).");
        return Ok(new SchrittErgebnis("4b · Lohnpositionen ↔ Swissdec-Lohnarten", vorschau, aktionen, hinweise));
    }

    /// <summary>
    /// CSV-massgebliche Basis-Flags, die der Musterlohnartenstamm nicht kennt
    /// (Ferien/Feiertag) bzw. bei Schicht (1070) widerspricht.
    /// </summary>
    private static void WendeSwissdecBasisFlagsAn(Lohnposition lp)
    {
        if (lp.SwissdecLohnart == "1006")
        {
            lp.ZaehltAlsBasisFerien = true;
            lp.ZaehltAlsBasisFeiertag = true;
            lp.ZaehltAlsBasis13ml = true;
        }
        else if (lp.SwissdecLohnart == "1070")
            lp.ZaehltAlsBasis13ml = false;
    }
}
