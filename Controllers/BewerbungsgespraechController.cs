using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Controllers;

/// <summary>
/// Gesprächsmodus Bewerbungsgespräch (Walter 03.09.2026) — Backend für
/// js/gespraech.js. Reine Rekrutierungsdaten, keine Lohndaten
/// (EditLock-Whitelist).
///
/// Ablauf: POST legt ein leeres Gespräch für die Sidebar-Filiale an;
/// PATCH antworten merged einzelne Felder (Autosave nach jedem Feld) und
/// zählt die Revision hoch — ein Client mit veralteter Revision bekommt
/// 409 + den aktuellen Stand; POST abschliessen setzt Entscheid + Status.
/// GET pdf rendert die Zusammenfassung (inkl. Unterschrift) für die Akte.
/// </summary>
[Route("api/bewerbungsgespraech")]
[ApiController]
public class BewerbungsgespraechController : HrControllerBase
{
    private readonly string _kandidatenRoot;   // …/documents/kandidaten (wie KandidatenController)

    public BewerbungsgespraechController(AppDbContext db, IConfiguration config, IWebHostEnvironment env) : base(db)
    {
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _kandidatenRoot = Path.Combine(configured, "kandidaten");
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private async Task<string?> ActorNameAsync()
    {
        var uid = GetCurrentUserId();
        if (uid is null) return null;
        var u = await _db.AppUsers.AsNoTracking()
            .Where(x => x.Id == uid.Value)
            .Select(x => new { x.FirstName, x.LastName, x.Username })
            .FirstOrDefaultAsync();
        if (u == null) return null;
        var voll = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(voll) ? u.Username : voll;
    }

    private static Dictionary<string, JsonElement> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts) ?? new();
        }
        catch { return new(); }
    }

    private static string? Str(Dictionary<string, JsonElement> a, string key)
    {
        if (!a.TryGetValue(key, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "ja",
            JsonValueKind.False => "nein",
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.GetRawText()
        };
    }

    private static DateOnly? Datum(string? iso)
        => DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private object ToDto(Bewerbungsgespraech g, bool mitAntworten)
    {
        var antworten = Parse(g.AntwortenJson);
        return new
        {
            g.Id,
            g.CompanyProfileId,
            g.Status,
            g.Entscheid,
            g.Vorname,
            g.Nachname,
            geburtsdatum = g.Geburtsdatum?.ToString("yyyy-MM-dd"),
            g.Schritt,
            g.Revision,
            anzahlAntworten = antworten.Count(kv => kv.Value.ValueKind != JsonValueKind.Null
                                                   && !(kv.Value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(kv.Value.GetString()))),
            g.GestartetAm,
            g.GestartetVon,
            g.GeaendertAm,
            g.AbgeschlossenAm,
            g.AbgeschlossenVon,
            g.KandidatId,
            antworten = mitAntworten ? JsonSerializer.Deserialize<JsonElement>(g.AntwortenJson, JsonOpts) : (JsonElement?)null,
        };
    }

    // ─────────────────────────── Liste / Anlegen ───────────────────────────

    /// <summary>Gespräche der Filiale: alle in Arbeit + die letzten 30 abgeschlossenen.</summary>
    [HttpGet]
    public async Task<IActionResult> Liste([FromQuery] int companyProfileId)
    {
        if (companyProfileId <= 0) return BadRequest(new { error = "companyProfileId fehlt." });
        if (!await CanAccessBranchAsync(companyProfileId)) return Forbid();

        var offen = await _db.Bewerbungsgespraeche.AsNoTracking()
            .Where(g => g.CompanyProfileId == companyProfileId && g.Status == "in_arbeit")
            .OrderByDescending(g => g.GeaendertAm)
            .ToListAsync();
        var fertig = await _db.Bewerbungsgespraeche.AsNoTracking()
            .Where(g => g.CompanyProfileId == companyProfileId && g.Status == "abgeschlossen")
            .OrderByDescending(g => g.AbgeschlossenAm)
            .Take(30)
            .ToListAsync();
        return Ok(new
        {
            inArbeit = offen.Select(g => ToDto(g, false)),
            abgeschlossen = fertig.Select(g => ToDto(g, false)),
        });
    }

    public record NeuDto(int CompanyProfileId);

    /// <summary>Neues, leeres Gespräch — wird beim Klick auf «Gespräch starten» angelegt.</summary>
    [HttpPost]
    public async Task<IActionResult> Neu([FromBody] NeuDto dto)
    {
        if (dto.CompanyProfileId <= 0) return BadRequest(new { error = "Bitte zuerst links eine Filiale wählen." });
        if (!await CanAccessBranchAsync(dto.CompanyProfileId)) return Forbid();
        if (!await _db.CompanyProfiles.AnyAsync(c => c.Id == dto.CompanyProfileId))
            return BadRequest(new { error = "Filiale nicht gefunden." });

        var g = new Bewerbungsgespraech
        {
            CompanyProfileId = dto.CompanyProfileId,
            GestartetVon = await ActorNameAsync(),
            GestartetAm = DateTime.Now,
            GeaendertAm = DateTime.Now,
            AntwortenJson = "{}",
        };
        _db.Bewerbungsgespraeche.Add(g);
        await _db.SaveChangesAsync();
        return Ok(ToDto(g, true));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var g = await _db.Bewerbungsgespraeche.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound();
        if (!await CanAccessBranchAsync(g.CompanyProfileId)) return Forbid();
        return Ok(ToDto(g, true));
    }

    // ─────────────────────────── Autosave ───────────────────────────

    public class AntwortenDto
    {
        public int Revision { get; set; }
        public Dictionary<string, JsonElement>? Antworten { get; set; }
        public string? Schritt { get; set; }
    }

    /// <summary>
    /// Einzelne Felder mergen (Autosave). Revision muss dem Server-Stand
    /// entsprechen, sonst 409 mit dem aktuellen Gespräch — der Client lädt
    /// dann neu und spielt seine Warteschlange erneut ab.
    /// </summary>
    [HttpPatch("{id:int}/antworten")]
    public async Task<IActionResult> Antworten(int id, [FromBody] AntwortenDto dto)
    {
        var g = await _db.Bewerbungsgespraeche.FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound();
        if (!await CanAccessBranchAsync(g.CompanyProfileId)) return Forbid();
        if (g.Status != "in_arbeit")
            return Conflict(new { error = "ABGESCHLOSSEN", message = "Das Gespräch ist abgeschlossen — zuerst wieder öffnen.", gespraech = ToDto(g, true) });
        if (dto.Revision != g.Revision)
            return Conflict(new { error = "REVISION", message = "Das Gespräch wurde inzwischen an anderer Stelle geändert.", gespraech = ToDto(g, true) });

        var a = Parse(g.AntwortenJson);
        if (dto.Antworten != null)
        {
            foreach (var (k, v) in dto.Antworten)
            {
                if (string.IsNullOrWhiteSpace(k) || k.Length > 64) continue;
                if (v.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) a.Remove(k);
                else a[k] = v;
            }
        }
        g.AntwortenJson = JsonSerializer.Serialize(a, JsonOpts);
        g.Vorname = Str(a, "vorname")?.Trim();
        g.Nachname = Str(a, "nachname")?.Trim();
        g.Geburtsdatum = Datum(Str(a, "geburtsdatum"));
        if (!string.IsNullOrWhiteSpace(dto.Schritt)) g.Schritt = dto.Schritt;
        g.Revision++;
        g.GeaendertAm = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { g.Revision, g.GeaendertAm });
    }

    public record AbschlussDto(string Entscheid, int Revision);

    [HttpPost("{id:int}/abschliessen")]
    public async Task<IActionResult> Abschliessen(int id, [FromBody] AbschlussDto dto)
    {
        var g = await _db.Bewerbungsgespraeche.FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound();
        if (!await CanAccessBranchAsync(g.CompanyProfileId)) return Forbid();
        var e = (dto.Entscheid ?? "").Trim();
        if (e is not ("Zusage" or "Absage" or "Rueckstellung"))
            return BadRequest(new { error = "Entscheid muss Zusage, Absage oder Rueckstellung sein." });
        if (dto.Revision != g.Revision)
            return Conflict(new { error = "REVISION", message = "Das Gespräch wurde inzwischen geändert.", gespraech = ToDto(g, true) });
        g.Entscheid = e;
        g.Status = "abgeschlossen";
        g.AbgeschlossenAm = DateTime.Now;
        g.AbgeschlossenVon = await ActorNameAsync();
        g.GeaendertAm = DateTime.Now;
        g.Revision++;
        await _db.SaveChangesAsync();
        return Ok(ToDto(g, true));
    }

    /// <summary>
    /// «An HR senden &amp; beenden» (Walter 03.09.2026): Gespräch mit Entscheid
    /// abschliessen UND als Kandidat in die HR-Pipeline stellen (wie «Kandidat
    /// an HR»), mit dem Gesprächs-PDF als Anhang. HR sieht den GF-Entscheid
    /// in der Bemerkung und entscheidet dort weiter. Idempotent: ist schon
    /// ein Kandidat verknüpft, wird keiner doppelt angelegt.
    /// </summary>
    [HttpPost("{id:int}/an-hr-senden")]
    public async Task<IActionResult> AnHrSenden(int id, [FromBody] AbschlussDto dto)
    {
        var g = await _db.Bewerbungsgespraeche.Include(x => x.CompanyProfile).FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound();
        if (!await CanAccessBranchAsync(g.CompanyProfileId)) return Forbid();
        var e = (dto.Entscheid ?? "").Trim();
        if (e is not ("Zusage" or "Absage" or "Rueckstellung"))
            return BadRequest(new { error = "Entscheid muss Zusage, Absage oder Rueckstellung sein." });
        if (dto.Revision != g.Revision)
            return Conflict(new { error = "REVISION", message = "Das Gespräch wurde inzwischen geändert.", gespraech = ToDto(g, true) });

        var a = Parse(g.AntwortenJson);
        var vorname = (Str(a, "vorname") ?? g.Vorname ?? "").Trim();
        var nachname = (Str(a, "nachname") ?? g.Nachname ?? "").Trim();
        if (vorname.Length == 0 || nachname.Length == 0)
            return BadRequest(new { error = "NAME_FEHLT", message = "Vorname und Name müssen erfasst sein, bevor das Gespräch an HR geht." });

        var actor = await ActorNameAsync();
        g.Vorname = vorname;
        g.Nachname = nachname;
        g.Entscheid = e;
        g.Status = "abgeschlossen";
        g.AbgeschlossenAm = DateTime.Now;
        g.AbgeschlossenVon = actor;
        g.GeaendertAm = DateTime.Now;
        g.Revision++;

        if (g.KandidatId == null)
        {
            var eintrittIso = Str(a, "eintritt_vereinbart") ?? Str(a, "eintritt");
            var bem = new List<string>
            {
                $"Bewerbungsgespräch vom {g.GestartetAm:dd.MM.yyyy} ({actor ?? g.GestartetVon ?? "GF"}) — Entscheid GF: {EntscheidText(e)}",
            };
            var pensum = Str(a, "pensum");
            if (!string.IsNullOrWhiteSpace(pensum)) bem.Add($"Pensum {pensum} %");
            var dauer = Str(a, "dauer_mind");
            if (!string.IsNullOrWhiteSpace(dauer)) bem.Add($"Dauer mind. {dauer}");
            var wuensche = Str(a, "verf_bemerkung");
            if (!string.IsNullOrWhiteSpace(wuensche)) bem.Add($"Verfügbarkeit: {wuensche}");
            var notizen = Str(a, "notizen");
            if (!string.IsNullOrWhiteSpace(notizen)) bem.Add($"Notizen: {notizen}");

            var k = new Kandidat
            {
                CompanyProfileId = g.CompanyProfileId,
                Vorname = vorname,
                Name = nachname,
                Telefon = Str(a, "mobile"),
                Email = Str(a, "email"),
                FruehesterEintritt = Datum(eintrittIso),
                LgavAusbildung = a.TryGetValue("ausbildung_gastro", out var ag) && ag.ValueKind == JsonValueKind.True ? "ja"
                               : (ag.ValueKind == JsonValueKind.False ? "nein" : null),
                Bemerkung = string.Join(" · ", bem),
                // Im Gespräch gewählter Onboarding-Tag → Wunschtermin des Kandidaten
                WunschTerminId = int.TryParse(Str(a, "willkommenstag_termin_id"), out var wt) && wt > 0 ? wt : null,
                Status = "NEU",
                CreatedAt = DateTime.Now,
                CreatedBy = actor,
            };
            _db.Kandidaten.Add(k);
            await _db.SaveChangesAsync();
            g.KandidatId = k.Id;

            // Gesprächs-PDF als Anhang in die Kandidaten-Akte (best-effort)
            try
            {
                QuestPDF.Settings.License = LicenseType.Community;
                var pdf = BuildPdf(g, a);
                var dir = Path.Combine(_kandidatenRoot, k.Id.ToString());
                Directory.CreateDirectory(dir);
                var storage = $"{Guid.NewGuid():N}.pdf";
                await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, storage), pdf);
                _db.KandidatDokumente.Add(new KandidatDokument
                {
                    KandidatId = k.Id,
                    OriginalFilename = $"Bewerbungsgespraech_{nachname}_{vorname}_{g.GestartetAm:yyyyMMdd}.pdf".Replace(' ', '_'),
                    StorageFilename = storage,
                    CreatedAt = DateTime.Now,
                    CreatedBy = actor,
                });
            }
            catch { /* PDF-Anhang ist Komfort — der Kandidat ist wichtiger */ }
        }

        await _db.SaveChangesAsync();
        return Ok(ToDto(g, true));
    }

    [HttpPost("{id:int}/wieder-oeffnen")]
    public async Task<IActionResult> WiederOeffnen(int id)
    {
        var g = await _db.Bewerbungsgespraeche.FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound();
        if (!await CanAccessBranchAsync(g.CompanyProfileId)) return Forbid();
        g.Status = "in_arbeit";
        g.AbgeschlossenAm = null;
        g.AbgeschlossenVon = null;
        g.GeaendertAm = DateTime.Now;
        g.Revision++;
        await _db.SaveChangesAsync();
        return Ok(ToDto(g, true));
    }

    /// <summary>Nur Gespräche in Arbeit — leere Fehlstarts wegräumen.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Loeschen(int id)
    {
        var g = await _db.Bewerbungsgespraeche.FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound();
        if (!await CanAccessBranchAsync(g.CompanyProfileId)) return Forbid();
        if (g.Status != "in_arbeit")
            return Conflict(new { error = "ABGESCHLOSSEN", message = "Abgeschlossene Gespräche können nicht gelöscht werden." });
        _db.Bewerbungsgespraeche.Remove(g);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ─────────────────────────── Dubletten ───────────────────────────

    /// <summary>
    /// «Kennen wir schon?» — nach Name + Vorname (+ Geburtsdatum) in MA aller
    /// Filialen, Kandidaten und früheren Gesprächen suchen. Wird still nach
    /// den ersten Feldern aufgerufen.
    /// </summary>
    [HttpGet("dubletten")]
    public async Task<IActionResult> Dubletten([FromQuery] string? vorname, [FromQuery] string? nachname,
        [FromQuery] string? geburtsdatum, [FromQuery] int ausserId = 0)
    {
        var v = (vorname ?? "").Trim().ToLowerInvariant();
        var n = (nachname ?? "").Trim().ToLowerInvariant();
        if (v.Length < 2 || n.Length < 2) return Ok(new { treffer = Array.Empty<object>() });
        var geb = Datum(geburtsdatum);
        var gebDt = geb?.ToDateTime(TimeOnly.MinValue);

        var treffer = new List<object>();

        var mas = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden
                     && e.FirstName.ToLower() == v && e.LastName.ToLower() == n)
            .Select(e => new
            {
                e.Id, e.FirstName, e.LastName, e.DateOfBirth, e.IsActive, e.EntryDate, e.ExitDate, e.Austrittsgrund,
                Filialen = e.Employments
                    .Where(em => em.CompanyProfile != null)
                    .Select(em => (em.CompanyProfile!.RestaurantCode ?? "") + " " + (em.CompanyProfile.City ?? ""))
                    .Distinct().ToList(),
            })
            .Take(10)
            .ToListAsync();
        foreach (var m in mas)
        {
            var gebPasst = gebDt == null || m.DateOfBirth == null || m.DateOfBirth.Value.Date == gebDt.Value.Date;
            treffer.Add(new
            {
                art = "mitarbeiter",
                id = m.Id,
                name = $"{m.FirstName} {m.LastName}",
                geburtsdatum = m.DateOfBirth?.ToString("dd.MM.yyyy"),
                gebPasst,
                aktiv = m.IsActive && (m.ExitDate == null || m.ExitDate >= DateTime.Today),
                eintritt = m.EntryDate?.ToString("dd.MM.yyyy"),
                austritt = m.ExitDate?.ToString("dd.MM.yyyy"),
                austrittsgrund = m.Austrittsgrund,
                filialen = string.Join(", ", m.Filialen.Select(f => f.Trim()).Where(f => f.Length > 0)),
            });
        }

        var kand = await _db.Kandidaten.AsNoTracking()
            .Where(k => k.Vorname.ToLower() == v && k.Name.ToLower() == n)
            .OrderByDescending(k => k.CreatedAt)
            .Take(5)
            .Select(k => new { k.Id, k.Vorname, k.Name, k.Status, k.CreatedAt, k.Ablehnungsgrund, k.CompanyProfileId })
            .ToListAsync();
        foreach (var k in kand)
            treffer.Add(new
            {
                art = "kandidat",
                id = k.Id,
                name = $"{k.Vorname} {k.Name}",
                status = k.Status,
                datum = k.CreatedAt.ToString("dd.MM.yyyy"),
                grund = k.Ablehnungsgrund,
                companyProfileId = k.CompanyProfileId,
            });

        var alte = await _db.Bewerbungsgespraeche.AsNoTracking()
            .Where(g => g.Id != ausserId && g.Vorname != null && g.Nachname != null
                     && g.Vorname.ToLower() == v && g.Nachname.ToLower() == n)
            .OrderByDescending(g => g.GeaendertAm)
            .Take(5)
            .Select(g => new { g.Id, g.Status, g.Entscheid, g.GeaendertAm, g.Geburtsdatum, g.CompanyProfileId })
            .ToListAsync();
        foreach (var g in alte)
            treffer.Add(new
            {
                art = "gespraech",
                id = g.Id,
                status = g.Status,
                entscheid = g.Entscheid,
                datum = g.GeaendertAm.ToString("dd.MM.yyyy"),
                gebPasst = geb == null || g.Geburtsdatum == null || g.Geburtsdatum == geb,
                companyProfileId = g.CompanyProfileId,
            });

        return Ok(new { treffer });
    }

    // ─────────────────────────── PDF ───────────────────────────

    private static readonly (string Sektion, string Key, string Label)[] Katalog =
    {
        ("Personalien", "nachname", "Name"),
        ("Personalien", "vorname", "Vorname"),
        ("Personalien", "geburtsdatum", "Geburtsdatum"),
        ("Personalien", "geschlecht", "Geschlecht"),
        ("Personalien", "adresse", "Adresse"),
        ("Personalien", "plz", "PLZ"),
        ("Personalien", "ort", "Ort"),
        ("Personalien", "mobile", "Mobile / Tel."),
        ("Personalien", "email", "E-Mail"),
        ("Personalien", "nationalitaet", "Nationalität"),
        ("Personalien", "zivilstand", "Zivilstand"),
        ("Personalien", "zivilstand_seit", "Zivilstand seit"),
        ("Personalien", "bewilligung", "Bewilligung / Ausweis"),
        ("Personalien", "bewilligung_bis", "Bewilligung gültig bis"),
        ("Personalien", "ahv", "AHV-Nummer"),
        ("Personalien", "qst", "Quellensteuerpflichtig"),
        ("Personalien", "konfession", "Konfession"),
        ("Sprachkenntnisse", "sprache_deutsch", "Deutsch"),
        ("Sprachkenntnisse", "sprache_englisch", "Englisch"),
        ("Sprachkenntnisse", "sprache_franzoesisch", "Französisch"),
        ("Sprachkenntnisse", "sprache_andere", "Andere Sprache"),
        ("Sprachkenntnisse", "sprache_andere_niveau", "Niveau andere Sprache"),
        ("Dein Einsatz bei uns", "pensum", "Gewünschtes Pensum (%)"),
        ("Dein Einsatz bei uns", "eintritt", "Frühester Eintritt"),
        ("Dein Einsatz bei uns", "erfahrung", "Erfahrung in Gastronomie"),
        ("Berufserfahrung & weitere Angaben", "krankheit", "Chronische Krankheit / Allergien"),
        ("Berufserfahrung & weitere Angaben", "krankheit_welche", "welche"),
        ("Berufserfahrung & weitere Angaben", "sozialleistungen", "Sozialleistungen"),
        ("Berufserfahrung & weitere Angaben", "iv_grad", "Invaliditätsgrad"),
        ("Berufserfahrung & weitere Angaben", "vorbestraft", "Vorbestraft"),
        ("Berufserfahrung & weitere Angaben", "militaer", "Militärservice demnächst"),
        ("Berufserfahrung & weitere Angaben", "militaer_dauer", "Dauer vom – bis"),
        ("Berufserfahrung & weitere Angaben", "ausbildung_gastro", "Ausbildung Hotellerie / Restauration"),
        ("Angaben über Partner", "partner_nachname", "Name"),
        ("Angaben über Partner", "partner_vorname", "Vorname"),
        ("Angaben über Partner", "partner_geschlecht", "Geschlecht"),
        ("Angaben über Partner", "partner_ahv", "AHV-Nummer"),
        ("Angaben über Partner", "partner_adresse", "Adresse (falls abweichend)"),
        ("Angaben über Partner", "partner_arbeitet", "Arbeitet Partner"),
        ("Angaben über Partner", "partner_ausweis", "Ausweis"),
        ("Angaben über Partner", "partner_arbeitgeber", "Arbeitgeber, Adresse"),
        ("Angaben über Partner", "partner_stellenantritt", "Stellenantritt"),
        ("Ergänzende Angaben", "krankenkasse", "Krankenkasse"),
        ("Ergänzende Angaben", "iban", "IBAN"),
        ("Ergänzende Angaben", "bank", "Bank"),
        ("Ergänzende Angaben", "bankadresse", "Bankadresse"),
        ("Willkommenstag", "willkommenstag_teilnahme", "Teilnahme"),
        ("Willkommenstag", "willkommenstag_termin", "Gewünschter Onboarding-Tag"),
        ("Bedingungen", "bedingungen_akzeptiert", "Allgemeine Bedingungen akzeptiert"),
        ("Minderjährige", "vertreter_name", "Gesetzlicher Vertreter"),
        ("Minderjährige", "vertreter_telefon", "Telefon Vertreter"),
        ("Gespräch (intern)", "teilnehmende", "Teilnehmende"),
        ("Gespräch (intern)", "eintritt_vereinbart", "Eintritt vereinbart per"),
        ("Gespräch (intern)", "dauer_mind", "Für eine Dauer von mindestens"),
        ("Gespräch (intern)", "notizen", "Eindruck / Notizen"),
    };

    private static string Wert(JsonElement v)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.String:
                var s = v.GetString() ?? "";
                if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return d.ToString("dd.MM.yyyy");
                return s;
            case JsonValueKind.True: return "ja";
            case JsonValueKind.False: return "nein";
            case JsonValueKind.Number: return v.GetRawText();
            case JsonValueKind.Array:
                return string.Join(", ", v.EnumerateArray().Select(Wert).Where(x => x.Length > 0));
            case JsonValueKind.Object:
                return string.Join(", ", v.EnumerateObject().Select(p => $"{p.Name}: {Wert(p.Value)}"));
            default: return "";
        }
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> Pdf(int id)
    {
        var g = await _db.Bewerbungsgespraeche.AsNoTracking()
            .Include(x => x.CompanyProfile)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (g == null) return NotFound();
        if (!await CanAccessBranchAsync(g.CompanyProfileId)) return Forbid();

        var a = Parse(g.AntwortenJson);
        QuestPDF.Settings.License = LicenseType.Community;
        var bytes = BuildPdf(g, a);
        var fn = $"Bewerbungsgespraech_{g.Nachname}_{g.Vorname}_{g.GestartetAm:yyyyMMdd}.pdf".Replace(' ', '_');
        return File(bytes, "application/pdf", fn);
    }

    private static byte[] BuildPdf(Bewerbungsgespraech g, Dictionary<string, JsonElement> a)
    {
        var filiale = g.CompanyProfile == null ? "" : $"{g.CompanyProfile.RestaurantCode} {g.CompanyProfile.City}".Trim();
        var name = $"{g.Vorname} {g.Nachname}".Trim();
        byte[]? unterschrift = null;
        if (a.TryGetValue("unterschrift", out var u) && u.ValueKind == JsonValueKind.String)
        {
            var s = u.GetString() ?? "";
            var comma = s.IndexOf(',');
            if (s.StartsWith("data:image", StringComparison.OrdinalIgnoreCase) && comma > 0)
            {
                try { unterschrift = Convert.FromBase64String(s[(comma + 1)..]); } catch { unterschrift = null; }
            }
        }

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(24); page.MarginBottom(24); page.MarginHorizontal(32);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor("#222"));
                page.Header().Column(col =>
                {
                    col.Item().Row(r =>
                    {
                        r.RelativeItem().Text($"Bewerbungsgespräch — {name}").SemiBold().FontSize(13).FontColor("#1a1a1a");
                        r.ConstantItem(220).AlignRight().Text($"{filiale}  ·  {g.GestartetAm:dd.MM.yyyy HH:mm}").FontSize(8).FontColor("#666");
                    });
                    col.Item().PaddingTop(1).Text(
                        $"Geführt von {g.GestartetVon ?? "—"}" +
                        (g.Status == "abgeschlossen" ? $"  ·  Entscheid: {EntscheidText(g.Entscheid)} ({g.AbgeschlossenAm:dd.MM.yyyy})" : "  ·  in Arbeit"))
                        .FontSize(8).FontColor("#666");
                    col.Item().PaddingTop(4).LineHorizontal(0.6f).LineColor("#ccc");
                });
                page.Footer().AlignCenter().DefaultTextStyle(x => x.FontSize(7.5f).FontColor("#777")).Text(t =>
                {
                    t.Span("Bewerbungsgespräch · vertraulich · Seite ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
                page.Content().PaddingTop(6).Column(col =>
                {
                    foreach (var sektion in Katalog.Select(k => k.Sektion).Distinct())
                    {
                        var zeilen = Katalog.Where(k => k.Sektion == sektion)
                            .Select(k => (k.Label, Wert: a.TryGetValue(k.Key, out var v) ? Wert(v) : ""))
                            .Where(z => z.Wert.Length > 0)
                            .ToList();
                        if (sektion == "Personalien" && zeilen.Count == 0) continue;
                        if (zeilen.Count == 0 && sektion != "Personalien") continue;
                        col.Item().PaddingTop(8).Text(sektion.ToUpperInvariant()).SemiBold().FontSize(8.5f).FontColor("#0e7490");
                        col.Item().PaddingTop(2).Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.ConstantColumn(170); c.RelativeColumn(); });
                            foreach (var (label, wert) in zeilen)
                            {
                                t.Cell().BorderBottom(0.3f).BorderColor("#e5e7eb").Padding(3).Text(label).FontSize(8.5f).FontColor("#555");
                                t.Cell().BorderBottom(0.3f).BorderColor("#e5e7eb").Padding(3).Text(wert).FontSize(9);
                            }
                        });
                    }

                    // Verfügbarkeit
                    var tage = new[] { ("mo", "Montag"), ("di", "Dienstag"), ("mi", "Mittwoch"), ("do", "Donnerstag"), ("fr", "Freitag"), ("sa", "Samstag"), ("so", "Sonntag") };
                    if (tage.Any(t => a.ContainsKey($"verf_{t.Item1}_von") || a.ContainsKey($"verf_{t.Item1}_bis")))
                    {
                        col.Item().PaddingTop(8).Text("WANN KANNST DU ARBEITEN?").SemiBold().FontSize(8.5f).FontColor("#0e7490");
                        col.Item().PaddingTop(2).Table(t =>
                        {
                            t.ColumnsDefinition(c => { foreach (var _ in tage) c.RelativeColumn(); });
                            foreach (var (_, lbl) in tage)
                                t.Cell().Background("#f3f3f3").Padding(3).AlignCenter().Text(lbl).SemiBold().FontSize(8);
                            foreach (var (k, _) in tage)
                            {
                                var von = a.TryGetValue($"verf_{k}_von", out var v1) ? Wert(v1) : "";
                                var bis = a.TryGetValue($"verf_{k}_bis", out var v2) ? Wert(v2) : "";
                                var txt = von.Length + bis.Length == 0 ? "—" : $"{von} – {bis}";
                                t.Cell().Border(0.3f).BorderColor("#e5e7eb").Padding(4).AlignCenter().Text(txt).FontSize(8.5f);
                            }
                        });
                    }

                    // Kinder
                    if (a.TryGetValue("kinder", out var kinder) && kinder.ValueKind == JsonValueKind.Array && kinder.GetArrayLength() > 0)
                    {
                        col.Item().PaddingTop(8).Text("KINDER").SemiBold().FontSize(8.5f).FontColor("#0e7490");
                        col.Item().PaddingTop(2).Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.ConstantColumn(60); c.ConstantColumn(70); c.ConstantColumn(80); c.ConstantColumn(70); });
                            foreach (var h in new[] { "Name", "Vorname", "Geschlecht", "Geburtsdatum", "Gleicher Haushalt", "In der CH" })
                                t.Cell().Background("#f3f3f3").Padding(3).Text(h).SemiBold().FontSize(8);
                            foreach (var kind in kinder.EnumerateArray())
                            {
                                string K(string p) => kind.ValueKind == JsonValueKind.Object && kind.TryGetProperty(p, out var pv) ? Wert(pv) : "";
                                foreach (var val in new[] { K("nachname"), K("vorname"), K("geschlecht"), K("geburtsdatum"), K("haushalt"), K("ch") })
                                    t.Cell().BorderBottom(0.3f).BorderColor("#e5e7eb").Padding(3).Text(val.Length == 0 ? "—" : val).FontSize(8.5f);
                            }
                        });
                    }

                    // Unterschrift
                    col.Item().PaddingTop(14).Row(r =>
                    {
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Unterschrift Bewerber/in").FontSize(8).FontColor("#555");
                            if (unterschrift != null)
                                c.Item().PaddingTop(2).Height(50).AlignLeft().Image(unterschrift).FitHeight();
                            else
                                c.Item().PaddingTop(38).LineHorizontal(0.6f).LineColor("#999");
                            var wann = a.TryGetValue("unterschrift_am", out var ua) ? Wert(ua) : "";
                            if (wann.Length > 0) c.Item().PaddingTop(2).Text($"unterschrieben {wann}").FontSize(7.5f).FontColor("#777");
                        });
                        r.ConstantItem(30);
                        r.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Visum Geschäftsführung").FontSize(8).FontColor("#555");
                            c.Item().PaddingTop(38).LineHorizontal(0.6f).LineColor("#999");
                            c.Item().PaddingTop(2).Text(g.AbgeschlossenVon ?? g.GestartetVon ?? "").FontSize(7.5f).FontColor("#777");
                        });
                    });
                });
            });
        }).GeneratePdf();
    }

    private static string EntscheidText(string? e) => e switch
    {
        "Zusage" => "Zusage",
        "Absage" => "Absage",
        "Rueckstellung" => "Rückstellung",
        _ => "—"
    };
}
