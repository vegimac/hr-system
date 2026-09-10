using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Models;
using System.Globalization;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 3a des Swissdec-Testmandanten: AHV/ALV-Sätze (globale SV-Sätze)
/// und FAK-Ansätze je Kanton (familienzulagen_tarif) aus company_export.csv.
/// Versicherungs-Lösungen mit Codes (UVG/UVGZ/KTG/BVG) und ALVZ (Lohnband)
/// folgen in Schritt 3b als neues Modul.
/// </summary>
public partial class SwissdecTestmandantController
{
    [HttpGet("schritt3/vorschau")]
    public async Task<IActionResult> Schritt3Vorschau() => await Schritt3(vorschau: true);

    [HttpPost("schritt3/anlegen")]
    public async Task<IActionResult> Schritt3Anlegen() => await Schritt3(vorschau: false);

    private async Task<IActionResult> Schritt3(bool vorschau)
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
        decimal? Pct(string tag) => Dz(T25(tag)) is { } f ? Math.Round(f * 100m, 4) : (decimal?)null;   // 0.053 → 5.3
        string F(decimal? d) => d?.ToString("0.####", CultureInfo.InvariantCulture) ?? "–";

        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();
        var ab = new DateOnly(2024, 1, 1);   // frühestes Testjahr

        // ── Unterschiede zwischen den Jahren melden (Sätze/Grenzen) ──
        foreach (var tag in new[] { "CompanyAHVAVSEmployeeContributions", "CompanyALVACEmployeeContributions", "CompanyAHVAVSExemptionLimit", "CompanyALVACLimit", "CompanyALVZACSLimit" })
        {
            var w = new[] { J(tag, 1), J(tag, 2), J(tag, 3) }.Where(x => x != null).Distinct().ToList();
            if (w.Count > 1) hinweise.Add($"{tag}: unterschiedliche Werte je Jahr ({string.Join(" / ", w)}) — es wird der 2025er-Wert geladen; Jahresstaffelung bitte prüfen.");
        }

        // ── A) AHV / ALV (globale Sätze) ──
        var ahvAn = Pct("CompanyAHVAVSEmployeeContributions"); var ahvAg = Pct("CompanyAHVAVSEmployerContributions");
        var alvAn = Pct("CompanyALVACEmployeeContributions");  var alvAg = Pct("CompanyALVACEmployerContributions");
        var freibetragJahr = Dz(T25("CompanyAHVAVSExemptionLimit"));
        var alvLimitJahr   = Dz(T25("CompanyALVACLimit"));
        var freibetragMt   = freibetragJahr.HasValue ? Math.Round(freibetragJahr.Value / 12m, 2) : (decimal?)null;
        var alvMaxMt       = alvLimitJahr.HasValue ? Math.Round(alvLimitJahr.Value / 12m, 2) : (decimal?)null;

        var svAlle = await _db.SocialInsuranceRates.Where(r => r.CompanyProfileId == null && (r.Code == "AHV" || r.Code == "ALV")).ToListAsync();

        // Zielzeilen — Struktur wie die Standard-Sätze des Programms (18–64 / 65+ Freibetrag);
        // das Referenzalter (Frauen-Übergangsgeneration) rechnet die Engine monatsgenau selbst.
        var ziele = new[]
        {
            new { Code = "AHV", Name = "AHV / IV / EO",        Desc = "AN-/AG-Anteil, Alter 18–64 (Swissdec-Testmandant)",           MinAge = (int?)18, MaxAge = (int?)64, An = ahvAn, Ag = ahvAg, Freib = (decimal?)null, MaxMt = (decimal?)null, Sort = 10 },
            new { Code = "AHV", Name = "AHV / IV / EO (65+)",  Desc = "ab Referenzalter mit Freibetrag (Swissdec-Testmandant)",      MinAge = (int?)65, MaxAge = (int?)null, An = ahvAn, Ag = ahvAg, Freib = freibetragMt, MaxMt = (decimal?)null, Sort = 11 },
            new { Code = "ALV", Name = "Arbeitslosenversicherung", Desc = $"ALV bis CHF {F(alvLimitJahr)}/Jahr (Swissdec-Testmandant)", MinAge = (int?)18, MaxAge = (int?)64, An = alvAn, Ag = alvAg, Freib = (decimal?)null, MaxMt = alvMaxMt, Sort = 20 },
        };
        foreach (var z in ziele)
        {
            var r = svAlle.Where(x => x.Code == z.Code && x.MinAge == z.MinAge && x.MaxAge == z.MaxAge && x.Gender == null && x.EmploymentModelCode == null)
                          .OrderByDescending(x => x.IsActive).ThenByDescending(x => x.ValidFrom).FirstOrDefault();
            var felder = new Dictionary<string, string?>
            {
                ["Code"] = z.Code, ["Alter"] = $"{z.MinAge}–{(z.MaxAge?.ToString() ?? "∞")}",
                ["AN %"] = F(z.An), ["AG %"] = F(z.Ag), ["Freibetrag/Mt."] = F(z.Freib), ["Höchstlohn/Mt."] = F(z.MaxMt),
                ["gültig ab"] = ab.ToString("dd.MM.yyyy"),
                ["bisher"] = r == null ? "keine Zeile" : $"AN {F(r.Rate)} / AG {F(r.RateEmployer)} · Freib. {F(r.FreibetragMonthly)} · Max {F(r.MaxBaseMonthly)} · ab {r.ValidFrom:dd.MM.yyyy}{(r.IsActive ? "" : " · inaktiv")}",
            };
            aktionen.Add(new Aktion(r == null ? "anlegen" : "aktualisieren", "SV-Satz", z.Name, felder));
            if (vorschau) continue;
            if (r == null) { r = new SocialInsuranceRate { Code = z.Code, MinAge = z.MinAge, MaxAge = z.MaxAge, BasisType = "gross", CreatedAt = DateTime.Now }; _db.SocialInsuranceRates.Add(r); svAlle.Add(r); }
            r.Name = z.Name; r.Description = z.Desc; r.Rate = z.An ?? r.Rate; r.RateEmployer = z.Ag;
            r.FreibetragMonthly = z.Freib; r.MaxBaseMonthly = z.MaxMt; r.SortOrder = z.Sort; r.IsActive = true;
            if (r.ValidFrom > ab) r.ValidFrom = ab;
            r.ValidTo = null;
        }
        // übrige globale AHV/ALV-Zeilen (andere Schlüssel) melden
        var fremdeSv = svAlle.Where(x => x.IsActive && !ziele.Any(z => z.Code == x.Code && z.MinAge == x.MinAge && z.MaxAge == x.MaxAge) ).ToList();
        if (fremdeSv.Count > 0)
            hinweise.Add("Weitere aktive AHV/ALV-Zeilen (nicht aus den Testdaten): " + string.Join(", ", fremdeSv.Select(x => $"{x.Code} {x.Name} ab {x.ValidFrom:dd.MM.yyyy}")) + " — bitte prüfen/deaktivieren.");
        hinweise.Add($"ALVZ (Solidaritätsprozent) {F(Pct("CompanyALVZACSEmployeeContributions"))} % auf CHF {F(alvLimitJahr)}–{F(Dz(T25("CompanyALVZACSLimit")))} steht in den Testdaten, hat aber im Programm noch kein Lohnband-Modell → Schritt 3b (zusammen mit UVGZ/KTG-Überschusslohn).");
        hinweise.Add($"Beitragsbeginn {T25("CompanyAHVAVSStartPayContributions")} · Referenzalter M {T25("CompanyAHVAVSStartPensionAgeMale")} / F {T25("CompanyAHVAVSStartPensionAgeFemale")}: die Engine rechnet Referenzalter nach AHV 21 monatsgenau (Übergangsjahrgänge 1961–63) — bei Testfällen mit Frauen nahe 64 auf die Soll-Werte achten.");
        hinweise.Add("NBUV/KTG/BVG-Zeilen (aus Prod kopiert) bleiben vorerst — sie werden in Schritt 3b durch die Versicherungs-Lösungen der Muster AG ersetzt.");

        // ── B) FAK-Ansätze je Kanton ──
        var fakAlle = await _db.FamilienzulagenTarife.ToListAsync();
        var bis2026 = new DateOnly(2026, 12, 31);
        foreach (var kt in new[] { "LU", "BE", "VD", "TI", "AG", "ZG" })
        {
            string P(string s) => $"CompanyFAKCAF{kt}{s}";
            var r0to11 = Dz(T25(P("Rate0to11"))); var r12to15 = Dz(T25(P("Rate12to15")));
            var r0to15 = Dz(T25(P("Rate0to15"))); var r0to15_3 = Dz(T25(P("Rate0to15From3rdChild")));
            var r16to25 = Dz(T25(P("Rate16to25"))); var r16to25_3 = Dz(T25(P("Rate16to25From3rdChild")));
            var geburt = Dz(T25(P("RateBirthAllowance")));
            if (r0to11 == null && r0to15 == null && r16to25 == null)
            {
                hinweise.Add($"FAK {kt}: keine Ansätze in der CSV" + (T25(P("IKV")) is { } ikv ? $" — IKV über Kanton {ikv}: Abrechnung/Ansätze nach {ikv}? Klären wir mit den Testfällen." : "."));
                continue;
            }
            var kz1 = r0to11 ?? r0to15;
            decimal? kz2 = null; int? kz2Alter = null; int? schwelle = null; decimal? az2 = null;
            if (r12to15 != null) { kz2 = r12to15; kz2Alter = 12; }
            if (r0to15_3 != null) { kz2 = r0to15_3; schwelle = 3; az2 = r16to25_3; }

            var t = fakAlle.FirstOrDefault(x => x.KantonCode == kt && x.ValidFrom == ab);
            var ueberlappend = fakAlle.Where(x => x.KantonCode == kt && x != t && x.IsActive && x.ValidFrom <= bis2026 && (x.ValidTo == null || x.ValidTo >= ab)).ToList();
            var felder = new Dictionary<string, string?>
            {
                ["Kanton"] = kt, ["Kinderzulage"] = F(kz1) + (kz2 != null ? (kz2Alter != null ? $" / ab {kz2Alter} J. {F(kz2)}" : $" / ab {schwelle}. Kind {F(kz2)}") : ""),
                ["Ausbildungszulage"] = F(r16to25) + (az2 != null ? $" / ab {schwelle}. Kind {F(az2)}" : ""),
                ["Geburtszulage"] = F(geburt), ["gültig ab"] = ab.ToString("dd.MM.yyyy"),
                ["bisher"] = t == null ? "keine Zeile" : $"KZ {F(t.KinderzulageSatz1)} / AZ {F(t.AusbildungszulageSatz1)}",
                ["wird deaktiviert"] = ueberlappend.Count == 0 ? null : string.Join(", ", ueberlappend.Select(x => $"ab {x.ValidFrom:dd.MM.yyyy} (KZ {F(x.KinderzulageSatz1)})")),
            };
            aktionen.Add(new Aktion(t == null ? "anlegen" : "aktualisieren", "FAK-Tarif", $"{kt} · {T25(P("Name"))}", felder));
            if (vorschau) continue;
            if (t == null) { t = new FamilienzulagenTarif { KantonCode = kt, ValidFrom = ab, CreatedAt = DateTime.Now }; _db.FamilienzulagenTarife.Add(t); fakAlle.Add(t); }
            t.ValidTo = null;
            t.KinderzulageSatz1 = kz1; t.KinderzulageSatz2 = kz2; t.KinderzulageSatz2AbAlter = kz2Alter;
            t.AusbildungszulageSatz1 = r16to25; t.AusbildungszulageSatz2 = az2; t.AusbildungszulageSatz2AbAlter = null;
            t.SchwelleSatz2AnzahlKinder = schwelle;
            t.GeburtszulageBetrag = geburt; t.AdoptionszulageBetrag = geburt;
            t.AltersGrenzeKinder = 16; t.AltersGrenzeAusbildung = 25;
            t.Quelle = "Swissdec-Testmandant Muster AG (company_export.csv)";
            t.Bemerkung = "Swissdec-Testdaten — Mindesterwerbseinkommen nicht in den Testdaten enthalten";
            t.IsActive = true; t.UpdatedAt = DateTime.Now;
            foreach (var u in ueberlappend) { u.IsActive = false; u.UpdatedAt = DateTime.Now; }
        }
        hinweise.Add("Mindesterwerbseinkommen (Jahr/Monat) steht nicht in den Testdaten — bleibt leer; falls ein Testfall es braucht, ergänzen wir es dann.");
        hinweise.Add("Geburtszulage wird auch als Adoptionszulage übernommen (Testdaten nennen nur «BirthAllowance»).");

        if (!vorschau)
        {
            await _db.SaveChangesAsync();
            _log.LogInformation("Swissdec-Testmandant Schritt 3a angelegt: {N} Aktionen", aktionen.Count);
        }
        return Ok(new SchrittErgebnis("3a · AHV/ALV + FAK-Ansätze", vorschau, aktionen, hinweise));
    }
}
