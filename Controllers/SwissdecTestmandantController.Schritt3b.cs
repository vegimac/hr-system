using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Models;
using System.Globalization;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 3b des Swissdec-Testmandanten: Versicherungs-Lösungen der Muster AG
/// als SV-Sätze mit Lösungs-Code (Walter 07.09.2026: OneCrew-Logik behalten —
/// Sätze zentral, Code an der Zeile, Code am MA nur bei Abweichung).
///   UVG  : Betriebsteile A,B,P,… × Versicherungsart 1 (BU+NBU, NBU-Abzug AN),
///          2 (BU+NBU, AG trägt NBU), 3 (nur BU)  → Code z.B. «A1»; Standard A1
///   UVGZ : 11 (UVG-Lohn) / 12 (Überschusslohn)  mit Sätzen M/F, Lohnband
///   KTG  : 11 / 12 analog
///   BVG  : 11 / 21 / 22 (Prozent gesamt, hälftig AN/AG) — K2010 ohne Satz
///   ALVZ : Solidaritätsprozent als Lohnband über der ALV-Grenze
/// Aus Prod kopierte NBUV/KTG/BVG/BVG_ZUSATZ-Zeilen werden deaktiviert.
/// </summary>
public partial class SwissdecTestmandantController
{
    [HttpGet("schritt3b/vorschau")]
    public async Task<IActionResult> Schritt3bVorschau() => await Schritt3b(vorschau: true);

    [HttpPost("schritt3b/anlegen")]
    public async Task<IActionResult> Schritt3bAnlegen() => await Schritt3b(vorschau: false);

    private sealed record SvSpec(string Code, string Loesung, string Name, bool Default, decimal An, decimal? Ag,
                                 string? Gender, decimal? BandVon, decimal? Max, string BasisType, int Sort, string Bem);

    private async Task<IActionResult> Schritt3b(bool vorschau)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var pfad = CsvPfad("company_export.csv");
        if (!System.IO.File.Exists(pfad))
            return NotFound(new { error = "CSV_FEHLT", message = $"Datei fehlt: {pfad}" });

        var tags = LeseSscCsv(pfad);
        string? J(string tag, int idx) => tags.TryGetValue(tag, out var v) && v.Length > idx ? v[idx]?.Trim() : null;
        string? T25(string tag) => J(tag, 2) ?? J(tag, 3) ?? J(tag, 1);
        decimal? Dz(string? s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
        decimal? Pct(string tag) => Dz(T25(tag)) is { } f ? Math.Round(f * 100m, 4) : (decimal?)null;
        decimal? Mt(string tag) => Dz(T25(tag)) is { } f ? Math.Round(f / 12m, 2) : (decimal?)null;
        string F(decimal? d) => d?.ToString("0.####", CultureInfo.InvariantCulture) ?? "–";

        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();
        var ab = new DateOnly(2024, 1, 1);
        var specs = new List<SvSpec>();

        // ── UVG: Betriebsteile × Versicherungsart ──
        var uvgMax = Mt("CompanyUVGLAALimit");
        var teile = (T25("CompanyUVGLAAPossibleSolutions") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                     .SelectMany(t => t.Length == 2 ? new[] { t[..1], t[1..] } : new[] { t })   // «ML» in der CSV = M und L
                     .Distinct().ToList();
        foreach (var t in teile)
        {
            var nbu = Pct($"CompanyUVGLAANBUVAANPRate{t}");
            var bu  = Pct($"CompanyUVGLAABUVAAPRate{t}");
            if (nbu == null && bu == null) { hinweise.Add($"UVG Betriebsteil {t}: keine Sätze in der CSV."); continue; }
            var name = $"UVG Betriebsteil {t}";
            if (nbu is > 0)
            {
                specs.Add(new SvSpec("NBUV", $"{t}1", $"{name} — BU+NBU, NBU-Abzug AN", t == "A", nbu.Value, bu, null, null, uvgMax, "gross", 40, "Swissdec-Testdaten UVG"));
                specs.Add(new SvSpec("NBUV", $"{t}2", $"{name} — BU+NBU, NBU zulasten AG", false, 0m, (bu ?? 0m) + nbu.Value, null, null, uvgMax, "gross", 41, "Swissdec-Testdaten UVG (AG trägt NBU)"));
            }
            if (bu is > 0)
                specs.Add(new SvSpec("NBUV", $"{t}3", $"{name} — nur BU", false, 0m, bu, null, null, uvgMax, "gross", 42, "Swissdec-Testdaten UVG (nur Berufsunfall, z.B. < 8 h/Woche)"));
        }
        hinweise.Add("UVG-Code = Betriebsteil + Versicherungsart (1 = BU+NBU mit AN-Abzug, 2 = BU+NBU zulasten AG, 3 = nur BU, 0 = nicht versichert → keine Zeile). Standard: A1. Der «< 8 h/Woche»-Schalter entspricht …3.");

        // ── UVGZ + KTG: Codes 11/12 mit Sätzen M/F und Lohnband ──
        void Loesungen(string svCode, string prefix, string label, int sort)
        {
            foreach (var c in new[] { "11", "12" })
            {
                var solName = T25($"{prefix}Code{c}SolutionName");
                if (solName == null) continue;
                var von = Mt($"{prefix}Limit{c}From"); var bis = Mt($"{prefix}Limit{c}Until");
                var m = Pct($"{prefix}RateMale{c}"); var f = Pct($"{prefix}RateFemale{c}");
                if (m == null && f == null) continue;
                var band = von is > 0 ? von : null;
                var name = $"{label} {c} — {solName}";
                if (m == f || f == null || m == null)
                {
                    var satz = (m ?? f)!.Value;
                    specs.Add(new SvSpec(svCode, c, name, c == "11", Math.Round(satz, 4), 0m, null, band, bis, "gross", sort, "Swissdec-Testdaten — Prämie voll zulasten AN (Soll TF01: UVGZ 80.63 = 0.774 % von 10'416.97)"));
                }
                else
                {
                    specs.Add(new SvSpec(svCode, c, name + " (M)", c == "11", Math.Round(m.Value, 4), 0m, "M", band, bis, "gross", sort, "Swissdec-Testdaten — Prämie voll zulasten AN (Soll TF01)"));
                    specs.Add(new SvSpec(svCode, c, name + " (F)", c == "11", Math.Round(f.Value, 4), 0m, "F", band, bis, "gross", sort, "Swissdec-Testdaten — Prämie voll zulasten AN (Soll TF01)"));
                }
            }
        }
        Loesungen("UVGZ", "CompanyUVGZLAAC", "UVG-Zusatz", 45);
        Loesungen("KTG",  "CompanyKTGAMC",   "KTG",        35);
        hinweise.Add("UVGZ/KTG: Die Swissdec-Prämie geht voll zulasten AN (Soll TF01 Jan 2025: UVGZ 80.63, KTG 3.77 = volle Sätze) — AG 0 %, in den SV-Sätzen änderbar. Code 10 «nicht versichert» = keine Zeile. Code 12 = Überschusslohn als Lohnband (ab/Höchst pro Monat).");

        // ── BVG: Pläne 11/21/22 (Prozent gesamt), K2010 ohne Satz ──
        foreach (var c in (T25("CompanyBVGLPPPossibleSolutions") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pct = Pct($"CompanyBVGLPPCode{c}Percentage");
            var name = $"BVG {c} — {T25($"CompanyBVGLPPCode{c}SolutionName")}";
            if (pct == null) { hinweise.Add($"{name}: kein Prozentsatz in den Testdaten (Beiträge kommen pro MA als Fixbetrag) — keine Satz-Zeile."); continue; }
            specs.Add(new SvSpec("BVG", c, name, false, Math.Round(pct.Value / 2m, 4), Math.Round(pct.Value / 2m, 4), null, null, null, "bvg_basis", 50, "Swissdec-Testdaten — Beitrag hälftig AN/AG (Annahme), ohne Koordinationsabzug"));
        }
        hinweise.Add("BVG: kein Plan als Standard markiert — MA ohne BVG-Code bekommen keinen BVG-Abzug; Testfälle liefern die BVG-Beiträge meist als Fixbetrag pro MA (Lohnart 5050) → MA → Versicherungen → BVG-Fixbetrag.");

        // ── ALVZ ──
        var alvzAn = Pct("CompanyALVZACSEmployeeContributions"); var alvzAg = Pct("CompanyALVZACSEmployerContributions");
        if (alvzAn is > 0)
            specs.Add(new SvSpec("ALVZ", "", "ALV Solidaritätsprozent", false, alvzAn.Value, alvzAg, null, Mt("CompanyALVACLimit"), Mt("CompanyALVZACSLimit"), "gross", 21, "Swissdec-Testdaten — Lohnband über der ALV-Grenze"));

        // ── Anwenden ──
        var alle = await _db.SocialInsuranceRates.Where(r => r.CompanyProfileId == null).ToListAsync();
        foreach (var s in specs)
        {
            var loes = string.IsNullOrEmpty(s.Loesung) ? null : s.Loesung;
            var r = alle.FirstOrDefault(x => x.Code == s.Code && (x.LoesungsCode ?? "") == (loes ?? "") && (x.Gender ?? "") == (s.Gender ?? "") && x.ValidFrom == ab);
            var felder = new Dictionary<string, string?>
            {
                ["Typ"] = s.Code, ["Code"] = loes, ["Standard"] = s.Default ? "★" : null, ["Geschlecht"] = s.Gender,
                ["AN %"] = F(s.An), ["AG %"] = F(s.Ag), ["Lohnband/Mt."] = (s.BandVon != null || s.Max != null) ? $"{F(s.BandVon ?? 0)} – {F(s.Max)}" : null,
                ["Basis"] = s.BasisType, ["gültig ab"] = ab.ToString("dd.MM.yyyy"),
            };
            aktionen.Add(new Aktion(r == null ? "anlegen" : "aktualisieren", "SV-Satz", s.Name, felder));
            if (vorschau) continue;
            if (r == null) { r = new SocialInsuranceRate { Code = s.Code, LoesungsCode = loes, Gender = s.Gender, ValidFrom = ab, CreatedAt = DateTime.Now }; _db.SocialInsuranceRates.Add(r); alle.Add(r); }
            r.Name = s.Name.Length > 100 ? s.Name[..100] : s.Name; r.Description = s.Bem;
            r.Rate = s.An; r.RateEmployer = s.Ag; r.BasisType = s.BasisType;
            r.IsDefaultCode = s.Default; r.BandVonMonthly = s.BandVon; r.MaxBaseMonthly = s.Max;
            r.MinAge = s.Code == "ALVZ" ? 18 : null; r.MaxAge = s.Code == "ALVZ" ? 64 : null;
            r.SortOrder = s.Sort; r.IsActive = true; r.ValidTo = null;
            r.FibuPosition = s.Code switch { "NBUV" => 540, "KTG" => 530, "BVG" => 550, "ALVZ" => 510, _ => null };
        }

        // Prod-Kopien ohne Lösungs-Code (NBUV/KTG/BVG/BVG_ZUSATZ) deaktivieren
        var kopien = alle.Where(x => x.IsActive && x.LoesungsCode == null && x.Code is "NBUV" or "KTG" or "BVG" or "BVG_ZUSATZ").ToList();
        foreach (var k in kopien)
        {
            aktionen.Add(new Aktion("aktualisieren", "SV-Satz (deaktivieren)", k.Name, new Dictionary<string, string?> { ["Typ"] = k.Code, ["AN %"] = F(k.Rate), ["ab"] = k.ValidFrom.ToString("dd.MM.yyyy"), ["Grund"] = "aus Prod kopiert — ersetzt durch Muster-AG-Lösungen" }));
            if (!vorschau) k.IsActive = false;
        }
        var filialKopien = await _db.SocialInsuranceRates.Where(r => r.CompanyProfileId != null && r.IsActive).ToListAsync();
        foreach (var k in filialKopien)
        {
            aktionen.Add(new Aktion("aktualisieren", "SV-Satz Filial-Abweichung (deaktivieren)", k.Name, new Dictionary<string, string?> { ["Typ"] = k.Code, ["AN %"] = F(k.Rate), ["Grund"] = "Filial-Abweichung aus Prod — Muster AG hat einheitliche Sätze" }));
            if (!vorschau) k.IsActive = false;
        }

        if (!vorschau)
        {
            await _db.SaveChangesAsync();
            _log.LogInformation("Swissdec-Testmandant Schritt 3b angelegt: {N} Aktionen", aktionen.Count);
        }
        return Ok(new SchrittErgebnis("3b · Versicherungs-Lösungen", vorschau, aktionen, hinweise));
    }
}
