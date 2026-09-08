using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Models;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 2 des Swissdec-Testmandanten: Lohndaten-Empfänger der Muster AG
/// (Ausgleichskasse, FAK je Kanton, UVG/UVGZ/KTG, BVG, Quellensteuer je Kanton,
/// Lohnausweis BE/TI) — zentraler Katalog + Zuordnung zu den 6 Filialen mit
/// Kunden-/Vertragsnummern und Gültig ab/bis.
///
/// Besonderheit der Testdaten: per 1.1.2025 wechselt die Muster AG von der
/// Spida (AK + FAK, Kassen-Nr. 079) zur Ausgleichskasse Luzern (003) und zu
/// kantonalen FAK — genau der Fall für «Gültig bis» (Walter 07.09.2026).
/// </summary>
public partial class SwissdecTestmandantController
{
    [HttpGet("schritt2/vorschau")]
    public async Task<IActionResult> Schritt2Vorschau() => await Schritt2(vorschau: true);

    [HttpPost("schritt2/anlegen")]
    public async Task<IActionResult> Schritt2Anlegen() => await Schritt2(vorschau: false);

    /// <summary>Ein Empfänger-Satz aus den Testdaten inkl. Zuordnungsregel.</summary>
    private sealed record EmpfSpec(
        string Art, string Bezeichnung, string? Zusatz, string? Kanton, string? Kassennummer,
        string? Uid, string? Strasse, string? Postfach, string? Plz, string? Ort,
        string? Mitglied, string? Sub, DateOnly? Ab, DateOnly? Bis,
        string[]? NurFilialen,          // null = alle Filialen des Testmandanten
        string? Bemerkung);

    private async Task<IActionResult> Schritt2(bool vorschau)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var pfad = CsvPfad("company_export.csv");
        if (!System.IO.File.Exists(pfad))
            return NotFound(new { error = "CSV_FEHLT", message = $"Datei fehlt: {pfad}" });

        var tags = LeseSscCsv(pfad);
        // Spaltenindex: 1 = 2024, 2 = 2025, 3 = 2026
        string? J(string tag, int jahrIdx) => tags.TryGetValue(tag, out var v) && v.Length > jahrIdx ? v[jahrIdx]?.Trim() : null;
        string? T25(string tag) => J(tag, 2) ?? J(tag, 3) ?? J(tag, 1);
        string? T24(string tag) => J(tag, 1);

        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();

        var uid = T25("CompanyUIDBFS") ?? "";
        var hs = await _db.Hauptsitze.AsNoTracking().FirstOrDefaultAsync(h => h.Uid == uid);
        if (hs == null)
            return BadRequest(new { error = "SCHRITT1_FEHLT", message = "Zuerst Schritt 1 (Firma + Filialen) anlegen." });
        var filialen = await _db.CompanyProfiles.Where(c => c.HauptsitzId == hs.Id && c.IsActive).OrderBy(c => c.RestaurantCode).ToListAsync();
        if (filialen.Count == 0)
            return BadRequest(new { error = "SCHRITT1_FEHLT", message = "Keine Filialen des Testmandanten gefunden — zuerst Schritt 1." });

        var ab2025  = new DateOnly(2025, 1, 1);
        var bis2024 = new DateOnly(2024, 12, 31);
        DateOnly? D(string? s) => DateOnly.TryParse(s, out var d) ? d : null;

        var specs = new List<EmpfSpec>();

        // ── Ausgleichskasse: 2024 Spida (079), ab 2025 Kasse 003 ──
        var ak24Nr = T24("CompanyAKCCBranchNumber"); var ak24Kunde = T24("CompanyAKCCCustomerNumber");
        var ak25Nr = T25("CompanyAKCCBranchNumber"); var ak25Kunde = T25("CompanyAKCCCustomerNumber");
        var kassenwechsel = ak24Nr != null && ak25Nr != null && ak24Nr != ak25Nr;
        if (kassenwechsel)
        {
            specs.Add(new EmpfSpec("AUSGLEICHSKASSE", "Spida Ausgleichskasse", "AHV", null, ak24Nr, null, null, null, null, null,
                ak24Kunde, null, null, bis2024, null, "Swissdec-Testdaten 2024 (bis Kassenwechsel 31.12.2024)"));
            hinweise.Add($"Kassenwechsel per 01.01.2025 in den Testdaten: AK {ak24Nr} ({ak24Kunde}) → AK {ak25Nr} ({ak25Kunde}). Der alte Satz bekommt «gültig bis 31.12.2024», der neue «gültig ab 01.01.2025».");
        }
        specs.Add(new EmpfSpec("AUSGLEICHSKASSE", AkName(ak25Nr), "AHV", null, ak25Nr, null, null, null, null, null,
            ak25Kunde, null, kassenwechsel ? ab2025 : null, null, null, "Swissdec-Testdaten (Kassen-Nr. " + ak25Nr + ")"));
        hinweise.Add("Die CSV nennt für die Ausgleichskasse nur Kassen-Nr. + Kundennummer, keinen Namen — Bezeichnung «" + AkName(ak25Nr) + "» stammt aus der offiziellen Kassenliste (Nr. 003 = Luzern). Im XML zählt nur die Nummer.");

        // ── FAK: 2024 Spida (alle Kantone), ab 2025 je Kanton ──
        if (T24("CompanyFAKCAFSpidaName") is { } spidaFak)
            specs.Add(new EmpfSpec("FAK", spidaFak + " Familienausgleichskasse", "FAK", null, T24("CompanyFAKCAFSpidaBranchNumber"),
                null, null, null, null, null, T24("CompanyFAKCAFSpidaCustomerNumber"), null, null, bis2024, null,
                "Swissdec-Testdaten 2024 (bis Kassenwechsel 31.12.2024)"));
        foreach (var kt in new[] { "LU", "BE", "VD", "TI", "AG", "ZG" })
        {
            var name = T25($"CompanyFAKCAF{kt}Name");
            if (name == null) continue;
            var ikv  = T25($"CompanyFAKCAF{kt}IKV");
            var ika  = T25($"CompanyFAKCAF{kt}IntercantonalAgreement");
            var bem  = ikv != null ? $"IKV: Abrechnung über Kanton {ikv} (Testdaten)"
                     : ika != null ? "Interkantonale Vereinbarung (Testdaten: x)" : null;
            specs.Add(new EmpfSpec("FAK", name, "FAK", T25($"CompanyFAKCAF{kt}WorkplaceCanton") ?? kt, T25($"CompanyFAKCAF{kt}BranchNumber"),
                null, null, null, null, null, T25($"CompanyFAKCAF{kt}CustomerNumber"), null,
                kassenwechsel ? ab2025 : null, null, new[] { kt }, bem));
        }
        hinweise.Add("FAK-Ansätze (Kinder-/Ausbildungs-/Geburtszulagen je Kanton) und die IKV-Regel (ZG über LU, TI) sind Tarife — kommen im Tarif-Schritt, nicht hier.");

        // ── Versicherer UVG / UVGZ / KTG (Backwork) ──
        void Versicherer(string art, string prefix, string zusatz, DateOnly? ab)
        {
            var name = T25($"{prefix}InsurerName");
            if (name == null) { hinweise.Add($"{art}: kein Versicherer in der CSV."); return; }
            var (str, pf, plz, ort) = TrenneAdresse(T25($"{prefix}InsurerAddress"));
            var uidV = art == "UVG" ? T25("CompanyAHVAVSUVGLAAInsurerUIDBFS")
                     : art == "BVG" ? T25("CompanyAHVAVSBVGLPPInsurerUIDBFS") : null;
            specs.Add(new EmpfSpec(art, name, zusatz, null, T25($"{prefix}InsuranceID"), uidV, str, pf, plz, ort,
                T25($"{prefix}CustomerIdentity"), T25($"{prefix}ContractIdentity"), ab, null, null, "Swissdec-Testdaten"));
        }
        Versicherer("UVG",  "CompanyUVGLAA",   "UVG",  D(T25("CompanyAHVAVSUVGLAAInsurerValidAsOf")));
        Versicherer("UVGZ", "CompanyUVGZLAAC", "UVGZ", null);
        Versicherer("KTG",  "CompanyKTGAMC",   "KTG",  null);
        Versicherer("BVG",  "CompanyBVGLPP",   "BVG",  D(T25("CompanyAHVAVSBVGLPPInsurerValidAsOf")));
        hinweise.Add("UVG/UVGZ/KTG teilen sich Versicherer «Backwork» (S1000) — pro Art ein eigener Katalog-Eintrag, wie im Programm vorgesehen. Lösungs-Codes und Prämiensätze (UVG A/B/P…, KTG/UVGZ 10/11/12, BVG 11/21/22/K2010) folgen im Tarif-Schritt.");
        hinweise.Add("Die UID der Backwork gilt laut CSV für die UVG; UVGZ/KTG-Einträge bleiben ohne UID (nicht in den Testdaten).");

        // ── Quellensteuer je Kanton (TAS) ──
        foreach (var kt in (T25("CompanyTASCantons") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ssl = T25($"CompanyTAS{kt}CustomerIdentity");
            specs.Add(new EmpfSpec("QST", $"Steuerverwaltung Kanton {kt}", "Quellensteuer", kt, null, null, null, null, null, null,
                ssl, null, null, null, null, "Swissdec-Testdaten (SSL-Nummer)"));
        }
        // ── Lohnausweis (TAC) BE/TI ──
        foreach (var kt in tags.Keys.Where(k => k.StartsWith("CompanyTAC") && k.EndsWith("CantonID")).Select(k => k["CompanyTAC".Length..^"CantonID".Length]).Where(k => k.Length == 2))
        {
            specs.Add(new EmpfSpec("LOHNAUSWEIS", $"Steuerverwaltung Kanton {kt}", "Lohnausweis", kt, null, null, null, null, null, null,
                T25($"CompanyTAC{kt}CustomerIdentity"), null, null, null, null, "Swissdec-Testdaten (elektronischer Lohnausweis)"));
        }
        hinweise.Add("Namen der Steuerverwaltungen stehen nicht in der CSV («Steuerverwaltung Kanton XX» ist Platzhalter, frei umbenennbar) — gemeldet wird über Kanton + SSL-Nummer.");
        hinweise.Add("Statistik (BFS, #BFS) ist kein Lohndaten-Empfänger im Katalog — kommt mit der Statistik-Domäne (E7).");

        // ── Anwenden ──
        var katalog = await _db.LohndatenEmpfaengers.Include(e => e.Zuordnungen).ToListAsync();
        foreach (var s in specs)
        {
            var e = katalog.FirstOrDefault(x => x.Art == s.Art && x.Bezeichnung == s.Bezeichnung && (x.KantonCode ?? "") == (s.Kanton ?? ""));
            var ziel = s.NurFilialen == null ? filialen : filialen.Where(f => s.NurFilialen.Contains(f.RestaurantCode ?? "", StringComparer.OrdinalIgnoreCase)).ToList();
            var felder = new Dictionary<string, string?>
            {
                ["Art"] = s.Art, ["Zusatz"] = s.Zusatz, ["Kanton"] = s.Kanton, ["Kassen-/Versicherer-Nr."] = s.Kassennummer, ["UID"] = s.Uid,
                ["Adresse"] = string.Join(", ", new[] { s.Strasse, s.Postfach, ((s.Plz ?? "") + " " + (s.Ort ?? "")).Trim() }.Where(x => !string.IsNullOrWhiteSpace(x))),
                ["Kunden-/Mitgliednummer"] = s.Mitglied, ["Vertrags-/Subnummer"] = s.Sub,
                ["Gültig"] = GueltigText(s.Ab, s.Bis),
                ["Filialen"] = string.Join(", ", ziel.Select(f => f.RestaurantCode)),
                ["Bemerkung"] = s.Bemerkung,
            };
            var neueZuord = ziel.Count(f => e == null || !e.Zuordnungen.Any(z => z.CompanyProfileId == f.Id && z.GueltigAb == s.Ab));
            aktionen.Add(new Aktion(e == null ? "anlegen" : "aktualisieren", "Empfänger", $"{s.Bezeichnung} ({ziel.Count} Filialen, {neueZuord} neue Zuordnungen)", felder));
            if (vorschau) continue;

            if (e == null)
            {
                e = new LohndatenEmpfaenger { Art = s.Art, Bezeichnung = s.Bezeichnung, KantonCode = s.Kanton, CreatedAt = DateTime.Now };
                _db.LohndatenEmpfaengers.Add(e); katalog.Add(e);
            }
            e.Zusatz = s.Zusatz; e.Kassennummer = s.Kassennummer; e.UidNummer = s.Uid;
            e.Strasse = s.Strasse; e.Postfach = s.Postfach; e.Plz = s.Plz; e.Ort = s.Ort;
            e.Bemerkung = "Swissdec-Testmandant Muster AG"; e.IsActive = true; e.UpdatedAt = DateTime.Now;
            foreach (var f in ziel)
            {
                var z = e.Zuordnungen.FirstOrDefault(x => x.CompanyProfileId == f.Id && x.GueltigAb == s.Ab)
                     ?? e.Zuordnungen.FirstOrDefault(x => x.CompanyProfileId == f.Id && x.GueltigAb == null && x.GueltigBis == null && e.Zuordnungen.Count(y => y.CompanyProfileId == f.Id) == 1);
                if (z == null) { z = new CompanyProfileEmpfaenger { CompanyProfileId = f.Id, CreatedAt = DateTime.Now }; e.Zuordnungen.Add(z); }
                z.Mitgliednummer = s.Mitglied; z.Subnummer = s.Sub; z.GueltigAb = s.Ab; z.GueltigBis = s.Bis;
                z.Bemerkung = s.Bemerkung; z.IsActive = true; z.UpdatedAt = DateTime.Now;
            }
        }
        if (!vorschau)
        {
            await _db.SaveChangesAsync();
            _log.LogInformation("Swissdec-Testmandant Schritt 2 angelegt: {N} Empfänger", specs.Count);
        }

        // Fremde (kopierte) Katalog-Einträge ohne Zuordnung zum Testmandanten melden
        var fremd = katalog.Where(k => k.IsActive && k.Bemerkung != "Swissdec-Testmandant Muster AG" && !specs.Any(s => s.Art == k.Art && s.Bezeichnung == k.Bezeichnung))
                           .Select(k => k.Bezeichnung).Distinct().ToList();
        if (fremd.Count > 0)
            hinweise.Add("Im Katalog stehen weitere (aus Prod kopierte) Empfänger ohne Bezug zur Muster AG: " + string.Join(", ", fremd) + " — sie sind keiner Test-Filiale zugeordnet und stören nicht; bei Bedarf deaktivieren.");

        return Ok(new SchrittErgebnis("2 · Lohndaten-Empfänger", vorschau, aktionen, hinweise));
    }

    private static string GueltigText(DateOnly? ab, DateOnly? bis)
    {
        if (ab == null && bis == null) return "offen";
        if (bis == null) return $"ab {ab!.Value:dd.MM.yyyy}";
        if (ab == null)  return $"bis {bis.Value:dd.MM.yyyy}";
        return $"{ab.Value:dd.MM.yyyy} – {bis.Value:dd.MM.yyyy}";
    }

    private static string AkName(string? kassenNr) => kassenNr switch
    {
        "003" => "Ausgleichskasse Luzern",
        "079" => "Spida Ausgleichskasse",
        _     => $"Ausgleichskasse {kassenNr}",
    };

    /// <summary>«Bahnhofstrasse 7Postfach6003 Luzern» → (Bahnhofstrasse 7, Postfach, 6003, Luzern);
    /// «Fellerstrasse 233027 Bern» → (Fellerstrasse 23, –, 3027, Bern). Zeilenumbrüche fehlen in der CSV.</summary>
    private static (string? Strasse, string? Postfach, string? Plz, string? Ort) TrenneAdresse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (null, null, null, null);
        var m = System.Text.RegularExpressions.Regex.Match(s.Trim(), @"^(?<str>.*?)(?<pf>Postfach(?:\s*\d+)?)?(?<plz>\d{4})\s+(?<ort>\S.*)$");
        if (!m.Success) return (s.Trim(), null, null, null);
        var pf = m.Groups["pf"].Success && m.Groups["pf"].Length > 0 ? m.Groups["pf"].Value : null;
        return (m.Groups["str"].Value.Trim().TrimEnd(','), pf, m.Groups["plz"].Value, m.Groups["ort"].Value.Trim());
    }
}
