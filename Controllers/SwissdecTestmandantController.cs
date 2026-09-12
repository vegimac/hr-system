using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Data;
using HrSystem.Models;

namespace HrSystem.Controllers;

/// <summary>
/// Swissdec-Testmandant «Muster AG» (Walter-Entscheid 07.09.2026): lädt die
/// offiziellen Swissdec-Testdaten (Assets/Swissdec/Testmandant/*.csv) Schritt
/// für Schritt in die TESTINSTANZ — exakt, ohne Umbenennen, denn gegen diese
/// Stammdaten laufen die Zertifizierungs-Testfälle.
///
/// Grundsätze (Walter): nichts anlegen, was das scharfe Programm nicht auch
/// hat — der Importer nutzt ausschliesslich die normalen Stammdaten-Strukturen
/// (Hauptsitz, CompanyProfile). Jeder Schritt hat eine VORSCHAU (schreibt nichts)
/// und ist wiederholbar (bestehende Datensätze werden aktualisiert, nie doppelt).
///
/// Nur auf der Testinstanz nutzbar (INSTANCE_LABEL gesetzt) — auf Produktiv 403.
/// Konzept: docs/swissdec-testmandant.md
/// </summary>
[ApiController]
[Authorize(Roles = "admin")]
[Route("api/swissdec/testmandant")]
public partial class SwissdecTestmandantController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SwissdecTestmandantController> _log;

    public SwissdecTestmandantController(AppDbContext db, IWebHostEnvironment env, ILogger<SwissdecTestmandantController> log)
    { _db = db; _env = env; _log = log; }

    /// <summary>Testfall-Filter «nur»: «TF07», «tf7», «07», «7» → TF07; kommagetrennt (Walter 11.09.2026).</summary>
    private static HashSet<string>? NurSet(string? nur)
    {
        if (string.IsNullOrWhiteSpace(nur)) return null;
        var set = new HashSet<string>();
        foreach (var raw in nur.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var t = raw.ToUpperInvariant();
            if (t.StartsWith("TF")) t = t[2..];
            set.Add(int.TryParse(t, out var n) ? $"TF{n:D2}" : raw.ToUpperInvariant());
        }
        return set;
    }

    private static bool IstTestinstanz()
        => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INSTANCE_LABEL"));

    private string CsvPfad(string name) => Path.Combine(_env.ContentRootPath, "Assets", "Swissdec", "Testmandant", name);

    public record Aktion(string Typ, string Objekt, string Was, Dictionary<string, string?> Felder);
    public record SchrittErgebnis(string Schritt, bool Vorschau, List<Aktion> Aktionen, List<string> Hinweise);

    // ── Status ────────────────────────────────────────────────────────────
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var test = IstTestinstanz();
        var csvs = new[] { "company_export.csv", "testcases_export.csv", "testcase_differences_export.csv", "wagetypes_export.csv" }
            .ToDictionary(n => n, n => System.IO.File.Exists(CsvPfad(n)));
        var hs = await _db.Hauptsitze.AsNoTracking().FirstOrDefaultAsync(h => h.Uid == "CHE-999.999.996");
        var filialen = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => hs != null && c.HauptsitzId == hs.Id)
            .OrderBy(c => c.RestaurantCode)
            .Select(c => new { c.RestaurantCode, c.BranchName, c.City, c.BurNummer, c.BfsGemeindeNr })
            .ToListAsync();
        return Ok(new { istTestinstanz = test, csv = csvs, hauptsitz = hs == null ? null : new { hs.Name, hs.Uid }, filialen });
    }

    // ── Schritt 1: Firma + Filialen ──────────────────────────────────────
    [HttpGet("schritt1/vorschau")]
    public async Task<IActionResult> Schritt1Vorschau() => await Schritt1(vorschau: true);

    [HttpPost("schritt1/anlegen")]
    public async Task<IActionResult> Schritt1Anlegen() => await Schritt1(vorschau: false);

    private async Task<IActionResult> Schritt1(bool vorschau)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });

        var pfad = CsvPfad("company_export.csv");
        if (!System.IO.File.Exists(pfad))
            return NotFound(new { error = "CSV_FEHLT", message = $"Datei fehlt: {pfad}" });

        // CSV: Spalte 0 = SSC-Tag, Spalten 1..3 = Jahre 2024/2025/2026. Firmen-
        // und Filialdaten sind über alle Jahre identisch → wir nehmen 2025 (Index 2)
        // und fallen auf die anderen Jahre zurück, falls leer.
        var tags = LeseSscCsv(pfad);
        string? T(string tag) { if (!tags.TryGetValue(tag, out var v)) return null; return v.Skip(1).Select(x => x?.Trim()).FirstOrDefault(x => !string.IsNullOrEmpty(x)); }
        string? T25(string tag) => tags.TryGetValue(tag, out var v) && v.Length > 2 && !string.IsNullOrWhiteSpace(v[2]) ? v[2].Trim() : T(tag);

        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();

        // ── Hauptsitz (Rechtseinheit) ──
        var firmaName = T25("CompanyName") ?? "Muster AG";
        var uid       = T25("CompanyUIDBFS") ?? "";
        var (sitzStr, sitzNr) = TrenneStrasse(T25("CompanyStreet"));
        var hs = await _db.Hauptsitze.FirstOrDefaultAsync(h => h.Uid == uid);
        var hsFelder = new Dictionary<string, string?>
        {
            ["Name"] = firmaName, ["UID"] = uid, ["Strasse"] = T25("CompanyStreet"),
            ["PLZ"] = T25("CompanyZIPCode"), ["Ort"] = T25("CompanyCity"), ["Kanton"] = T25("CompanyWorkplaceLUCanton"),
            ["Kontakt"] = $"{T25("CompanyContactPerson")} · {T25("CompanyPhoneNumber")} · {T25("CompanyEmailAddress")} (→ Filiale Hauptsitz: Telefon/E-Mail)",
        };
        aktionen.Add(new Aktion(hs == null ? "anlegen" : "aktualisieren", "Hauptsitz", firmaName, hsFelder));
        if (!vorschau)
        {
            hs ??= new Hauptsitz { CreatedAt = DateTime.Now };
            hs.Name = firmaName; hs.Uid = uid;
            hs.Strasse = T25("CompanyStreet"); hs.Plz = T25("CompanyZIPCode"); hs.Ort = T25("CompanyCity");
            hs.KantonCode = T25("CompanyWorkplaceLUCanton"); hs.IsActive = true; hs.UpdatedAt = DateTime.Now;
            hs.Bemerkung = "Swissdec-Testmandant (offizielle Testdaten) — Kontakt: " + T25("CompanyContactPerson");
            if (hs.Id == 0) _db.Hauptsitze.Add(hs);
            await _db.SaveChangesAsync();
        }

        // ── Workplaces → Filialen (exakt nach Testdaten) ──
        var workplaceKeys = tags.Keys
            .Where(k => k.StartsWith("CompanyWorkplace") && k.EndsWith("WorkplaceID"))
            .Select(k => k["CompanyWorkplace".Length..^"WorkplaceID".Length])
            .Where(k => k.Length == 2).ToList();
        if (workplaceKeys.Count == 0) hinweise.Add("Keine Workplaces in der CSV gefunden.");

        var bestehende = await _db.CompanyProfiles.ToListAsync();
        foreach (var wk in workplaceKeys)
        {
            string? W(string suffix) => T25($"CompanyWorkplace{wk}{suffix}");
            var code = (W("WorkplaceID") ?? ("#" + wk)).TrimStart('#');   // «#LU» → «LU»
            var (str, nr) = TrenneStrasse(W("Street"));
            int? gemeinde = int.TryParse(W("MunicipalityID"), out var g) ? g : null;
            var bur = W("BUR-REE-Number");
            var felder = new Dictionary<string, string?>
            {
                ["Filialcode"] = code, ["Firma"] = firmaName, ["Bezeichnung"] = W("Designation"),
                ["Strasse"] = str, ["Nr."] = nr, ["PLZ"] = W("ZIPCode"), ["Ort"] = W("City"), ["Kanton"] = W("Canton"),
                ["BUR-Nummer"] = bur, ["UID"] = uid, ["BFS-Gemeinde-Nr."] = gemeinde?.ToString(),
                ["Wochenstunden"] = "42 (Swissdec-Modell «Standard»)",
            };
            if (bur != null && !System.Text.RegularExpressions.Regex.IsMatch(bur, "^[A-Z][0-9]{8}$"))
                hinweise.Add($"{code}: BUR-Nummer «{bur}» entspricht nicht dem Muster A12345678.");
            if (gemeinde is null)
                hinweise.Add($"{code}: keine BFS-Gemeindenummer in der CSV.");
            if (wk == "ZG" && W("ZIPCode") == "6003")
                hinweise.Add("ZG: PLZ 6003 stammt so aus den Swissdec-Testdaten (Zug hätte 6300) — wird bewusst NICHT korrigiert (Testdaten sind massgebend).");

            var cp = bestehende.FirstOrDefault(c => string.Equals(c.RestaurantCode, code, StringComparison.OrdinalIgnoreCase));
            aktionen.Add(new Aktion(cp == null ? "anlegen" : "aktualisieren", "Filiale", $"{code} · {W("Designation")} {W("City")}", felder));
            if (vorschau) continue;

            cp ??= new CompanyProfile();
            cp.CompanyName    = firmaName;
            cp.BranchName     = W("Designation");
            cp.RestaurantCode = code;
            cp.HauptsitzId    = hs!.Id;
            cp.Street         = str;
            cp.HouseNumber    = nr;
            cp.ZipCode        = W("ZIPCode");
            cp.City           = W("City");
            cp.Country        = "CH";                       // Systemkonvention ISO-Code (Lohnausweis mappt → SWITZERLAND)
            cp.KantonCode     = W("Canton")?.ToUpperInvariant();
            cp.BurNummer      = bur;
            cp.UidNummer      = uid;
            cp.UidBfs         = uid;
            cp.BfsGemeindeNr  = gemeinde;
            cp.NormalWeeklyHours ??= 42m;
            cp.IsActive       = true;
            if (wk == "LU")
            {
                cp.Phone = T25("CompanyPhoneNumber");
                cp.Email = T25("CompanyEmailAddress");
            }
            if (cp.Id == 0) { _db.CompanyProfiles.Add(cp); bestehende.Add(cp); }
        }
        if (!vorschau) await _db.SaveChangesAsync();

        // Filialen, die NICHT zum Testmandanten gehören (z.B. Reste) melden.
        var fremd = bestehende.Where(c => hs != null && c.HauptsitzId != hs.Id && c.HauptsitzId != null).Select(c => c.FullDisplayName).ToList();
        if (fremd.Count > 0) hinweise.Add("Nicht zum Testmandanten gehörende Filialen vorhanden: " + string.Join(", ", fremd));

        if (!vorschau) _log.LogInformation("Swissdec-Testmandant Schritt 1 angelegt: {N} Aktionen", aktionen.Count);
        return Ok(new SchrittErgebnis("1 · Firma + Filialen", vorschau, aktionen, hinweise));
    }

    // ── Hilfen ───────────────────────────────────────────────────────────
    /// <summary>Liest die SSC-CSV (Tag,2024,2025,2026) — mehrzeilige Anführungszeichen-Felder inklusive.</summary>
    private static Dictionary<string, string?[]> LeseSscCsv(string pfad)
    {
        var text = System.IO.File.ReadAllText(pfad, System.Text.Encoding.UTF8);
        var zeilen = new List<List<string>>();
        var feld = new System.Text.StringBuilder(); var zeile = new List<string>(); bool inQ = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQ)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { feld.Append('"'); i++; }
                else if (c == '"') inQ = false;
                else feld.Append(c);
            }
            else if (c == '"') inQ = true;
            else if (c == ',') { zeile.Add(feld.ToString()); feld.Clear(); }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                zeile.Add(feld.ToString()); feld.Clear();
                if (zeile.Any(x => x.Length > 0)) zeilen.Add(zeile);
                zeile = new List<string>();
            }
            else feld.Append(c);
        }
        if (feld.Length > 0 || zeile.Count > 0) { zeile.Add(feld.ToString()); zeilen.Add(zeile); }

        var dict = new Dictionary<string, string?[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var z in zeilen.Skip(1))
        {
            var key = z[0].Trim();
            if (key.Length == 0) continue;
            dict[key] = z.Select(x => string.IsNullOrWhiteSpace(x) ? null : x).ToArray();
        }
        return dict;
    }

    /// <summary>«Via Canonico Ghiringhelli 19» → («Via Canonico Ghiringhelli», «19»); ohne Nummer bleibt Nr. leer.</summary>
    private static (string? Strasse, string? Nr) TrenneStrasse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return (null, null);
        var t = s.Trim();
        var m = System.Text.RegularExpressions.Regex.Match(t, @"^(.*\S)\s+(\d+[a-zA-Z]?)$");
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : (t, null);
    }
}
