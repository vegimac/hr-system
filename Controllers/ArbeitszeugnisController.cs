using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Arbeitszeugnis bei MA-Austritt (Walter-Vorgabe 14.07.2026). Drei
/// Qualitätsstufen (durchschnitt/gut/sehr_gut), Mehrfachauswahl der
/// verrichteten Arbeit (kueche/kasse/drive). PDF read-only — schreibt nichts.
/// GF darf Zeugnisse für seine Filiale erstellen → admin/superuser/user;
/// buchhaltung (HR-Team) für die Entwurf-Bearbeitung (Walter 06.09.2026).
/// </summary>
[Authorize(Roles = "admin,superuser,user,buchhaltung")]
[ApiController]
[Route("api/arbeitszeugnis")]
public class ArbeitszeugnisController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ArbeitszeugnisPdfService _pdf;
    private readonly string _storagePath;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ArbeitszeugnisController> _log;

    public ArbeitszeugnisController(AppDbContext db, ArbeitszeugnisPdfService pdf,
        IConfiguration config, IWebHostEnvironment env,
        IServiceScopeFactory scopeFactory, ILogger<ArbeitszeugnisController> log)
    {
        _db = db; _pdf = pdf; _scopeFactory = scopeFactory; _log = log;
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _storagePath = configured;
    }

    public class ZeugnisDto
    {
        /// <summary>durchschnitt | gut | sehr_gut</summary>
        public string Qualitaet { get; set; } = "gut";
        /// <summary>kueche | kasse | drive (Mehrfachauswahl)</summary>
        public List<string> Bereiche { get; set; } = new();
        /// <summary>Zeugnis-Datum (Default: heute).</summary>
        public DateOnly? Datum { get; set; }
        /// <summary>«verlässt unser Unternehmen auf eigenen Wunsch» (Default: true).</summary>
        public bool AufEigenenWunsch { get; set; } = true;
        /// <summary>Funktion aus der Vorlage (z.B. «Crew-Trainerin», «Schichtkoordinator»).
        /// Leer = Teilzeit/Vollzeit-Mitarbeiter/in aus dem Vertrag.</summary>
        public string? Funktion { get; set; }
        /// <summary>Explizit gewählte Aufgaben (13er-Katalog der Word-Vorlage, 15.07.2026).
        /// Leer = Ableitung aus den Bereichen.</summary>
        public List<string>? Aufgaben { get; set; }
        /// <summary>true = ZWISCHENzeugnis (Vorlage «289 Hendschiken»).</summary>
        public bool Zwischen { get; set; }
        /// <summary>true = ARBEITSBESTÄTIGUNG (Vorlage «244 Sursee») — nur der
        /// Bestätigungssatz, keine Qualität/Bereiche/Aufgaben nötig.</summary>
        public bool Bestaetigung { get; set; }
        /// <summary>Abgabe durch Restaurant (Walter 12.08.2026): true =
        /// Allgemein-Unterzeichner der Filiale unterschreibt; false =
        /// Versand an MA, der eingeloggte User unterschreibt.</summary>
        public bool Abgabe { get; set; }
        /// <summary>Fiktives Austrittsdatum (Walter 15.07.2026): nur fürs
        /// ARBEITSzeugnis, wenn der LETZTE Vertrag offen ist und kein Austritt
        /// erfasst wurde. Vorschlag im UI: Ende des laufenden Monats.</summary>
        public DateOnly? Austritt { get; set; }
        /// <summary>Entwurf, aus dem HR das PDF erstellt (wird damit erledigt).</summary>
        public int? EntwurfId { get; set; }
        /// <summary>Bemerkung des Erstellers an HR (nur beim Entwurf).</summary>
        public string? Bemerkung { get; set; }
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static bool IstHr(AppUser u)
        => u.IsHrTeam || string.Equals(u.Role, "admin", StringComparison.OrdinalIgnoreCase);

    private static string Name(AppUser? u)
        => u == null ? "" : (string.IsNullOrWhiteSpace($"{u.FirstName} {u.LastName}".Trim()) ? (u.Username ?? "") : $"{u.FirstName} {u.LastName}".Trim());

    private static string ArtLabel(string? art) => art switch
    {
        "zwischen"     => "Zwischenzeugnis",
        "bestaetigung" => "Arbeitsbestätigung",
        _              => "Arbeitszeugnis",
    };

    private object EntwurfView(ArbeitszeugnisEntwurf x) => new
    {
        x.Id, x.EmployeeId, x.CompanyProfileId, x.Art, artLabel = ArtLabel(x.Art),
        employeeName = x.Employee == null ? "" : $"{x.Employee.FirstName} {x.Employee.LastName}".Trim(),
        employeeNumber = x.Employee?.EmployeeNumber,
        daten = string.IsNullOrWhiteSpace(x.Daten) ? null : System.Text.Json.JsonSerializer.Deserialize<ZeugnisDto>(x.Daten, JsonOpts),
        x.Bemerkung, x.ErstelltVon, erstelltVonName = Name(x.ErstelltVonUser), x.ErstelltAm,
        x.Status, x.ErledigtVon, erledigtVonName = Name(x.ErledigtVonUser), x.ErledigtAm, x.Antwort,
        x.MailboxDocumentId,
    };

    // ── Entwurf an HR senden ─────────────────────────────────────────────
    [HttpPost("{empId:int}/entwurf")]
    public async Task<IActionResult> EntwurfSenden(int empId, [FromBody] ZeugnisDto dto)
    {
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var benutzer = await AktuellerBenutzerAsync();
        if (benutzer == null) return Unauthorized();

        var last = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive).ThenByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync();
        var cpId = last?.CompanyProfileId
                   ?? await _db.CompanyProfiles.Where(c => c.IsActive).OrderBy(c => c.Id).Select(c => (int?)c.Id).FirstOrDefaultAsync();
        if (!cpId.HasValue)
            return BadRequest(new { error = "KEINE_FILIALE", message = "Dem MA ist keine Filiale zugeordnet." });

        // Ein offener Entwurf pro MA und Art — ein zweiter ersetzt den ersten.
        var art = dto.Bestaetigung ? "bestaetigung" : dto.Zwischen ? "zwischen" : "arbeitszeugnis";
        var alt = await _db.ArbeitszeugnisEntwuerfe
            .Where(x => x.EmployeeId == empId && x.Art == art && x.Status == "offen")
            .ToListAsync();
        foreach (var a in alt)
        {
            if (a.MailboxDocumentId.HasValue)
            {
                var md = await _db.MailboxDocuments.FirstOrDefaultAsync(m => m.Id == a.MailboxDocumentId.Value);
                if (md != null) _db.MailboxDocuments.Remove(md);
            }
            _db.ArbeitszeugnisEntwuerfe.Remove(a);
        }

        var bem = (dto.Bemerkung ?? "").Trim();
        dto.EntwurfId = null; dto.Bemerkung = null;
        var entwurf = new ArbeitszeugnisEntwurf
        {
            EmployeeId = empId, CompanyProfileId = cpId, Art = art,
            Daten = System.Text.Json.JsonSerializer.Serialize(dto, JsonOpts),
            Bemerkung = bem.Length > 0 ? bem : null,
            ErstelltVon = benutzer.Id, ErstelltAm = DateTime.Now, Status = "offen",
        };
        _db.ArbeitszeugnisEntwuerfe.Add(entwurf);
        await _db.SaveChangesAsync();

        // Eintrag im HR-Postfach: Mitteilung mit Verweis auf den Entwurf
        // (StorageFilename «zeugnis-entwurf-{id}» → Posteingang zeigt «Entwurf öffnen»).
        var maName = $"{e.FirstName} {e.LastName}".Trim();
        var body = $"{Name(benutzer)} hat ein {ArtLabel(art)} für {maName} ({e.EmployeeNumber}) ausgefüllt — Funktion «{dto.Funktion}». " +
                   $"Bitte prüfen, bei Bedarf anpassen und mit Unterschrift erstellen." +
                   (bem.Length > 0 ? $"\n\nBemerkung: {bem}" : "");
        var doc = new MailboxDocument
        {
            CompanyProfileId = cpId.Value,
            UploadedBy = benutzer.Id, UploadedAt = DateTime.Now,
            OriginalFilename = $"Zeugnis-Entwurf: {maName}",
            StorageFilename = $"zeugnis-entwurf-{entwurf.Id}",
            MessageBody = body, EmployeeId = empId, TargetType = "HR",
        };
        _db.MailboxDocuments.Add(doc);
        await _db.SaveChangesAsync();
        entwurf.MailboxDocumentId = doc.Id;
        await _db.SaveChangesAsync();

        return Ok(new { id = entwurf.Id, mailboxDocumentId = doc.Id });
    }

    // ── Entwürfe listen (HR: alle offenen; sonst eigene) ─────────────────
    [HttpGet("entwuerfe")]
    public async Task<IActionResult> Entwuerfe([FromQuery] int? employeeId, [FromQuery] string? status)
    {
        var benutzer = await AktuellerBenutzerAsync();
        if (benutzer == null) return Unauthorized();
        var q = _db.ArbeitszeugnisEntwuerfe.AsNoTracking()
            .Include(x => x.Employee).Include(x => x.ErstelltVonUser).Include(x => x.ErledigtVonUser)
            .AsQueryable();
        if (!IstHr(benutzer)) q = q.Where(x => x.ErstelltVon == benutzer.Id);
        if (employeeId.HasValue) q = q.Where(x => x.EmployeeId == employeeId.Value);
        var st = (status ?? "offen").Trim().ToLowerInvariant();
        if (st != "alle") q = q.Where(x => x.Status == st);
        var rows = await q.OrderByDescending(x => x.ErstelltAm).ToListAsync();
        return Ok(rows.Select(EntwurfView).ToList());
    }

    [HttpGet("entwurf/{id:int}")]
    public async Task<IActionResult> Entwurf(int id)
    {
        var benutzer = await AktuellerBenutzerAsync();
        if (benutzer == null) return Unauthorized();
        var x = await _db.ArbeitszeugnisEntwuerfe.AsNoTracking()
            .Include(e => e.Employee).Include(e => e.ErstelltVonUser).Include(e => e.ErledigtVonUser)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (x == null) return NotFound();
        if (!IstHr(benutzer) && x.ErstelltVon != benutzer.Id) return Forbid();
        return Ok(EntwurfView(x));
    }

    /// <summary>Entwurf zurückziehen (Ersteller) oder löschen (HR).</summary>
    [HttpDelete("entwurf/{id:int}")]
    public async Task<IActionResult> EntwurfLoeschen(int id)
    {
        var benutzer = await AktuellerBenutzerAsync();
        if (benutzer == null) return Unauthorized();
        var x = await _db.ArbeitszeugnisEntwuerfe.FirstOrDefaultAsync(e => e.Id == id);
        if (x == null) return NotFound();
        if (!IstHr(benutzer) && x.ErstelltVon != benutzer.Id) return Forbid();
        if (x.MailboxDocumentId.HasValue)
        {
            var md = await _db.MailboxDocuments.FirstOrDefaultAsync(m => m.Id == x.MailboxDocumentId.Value);
            if (md != null) _db.MailboxDocuments.Remove(md);
        }
        _db.ArbeitszeugnisEntwuerfe.Remove(x);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    public class ZurueckweisenDto { public string? Grund { get; set; } }

    /// <summary>HR weist den Entwurf mit Begründung zurück — Ersteller bekommt eine Mitteilung.</summary>
    [HttpPost("entwurf/{id:int}/zurueckweisen")]
    public async Task<IActionResult> Zurueckweisen(int id, [FromBody] ZurueckweisenDto dto)
    {
        var benutzer = await AktuellerBenutzerAsync();
        if (benutzer == null) return Unauthorized();
        if (!IstHr(benutzer)) return Forbid();
        var x = await _db.ArbeitszeugnisEntwuerfe.Include(e => e.Employee).FirstOrDefaultAsync(e => e.Id == id);
        if (x == null) return NotFound();
        var grund = (dto.Grund ?? "").Trim();
        x.Status = "zurueckgewiesen"; x.ErledigtVon = benutzer.Id; x.ErledigtAm = DateTime.Now;
        x.Antwort = grund.Length > 0 ? grund : null;
        await EntwurfAbschliessenAsync(x, benutzer,
            $"{ArtLabel(x.Art)} für {x.Employee?.FirstName} {x.Employee?.LastName}: Entwurf zurückgewiesen",
            $"{Name(benutzer)} (HR) hat deinen Zeugnis-Entwurf zurückgewiesen." + (grund.Length > 0 ? $"\n\nGrund: {grund}" : "") +
            "\n\nDu kannst den Entwurf beim MA (Austritt → Arbeitszeugnis) anpassen und erneut an HR senden.",
            null, null);
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    /// <summary>HR-Postfach-Eintrag entfernen + Rückmeldung (Mitteilung, optional mit PDF) an den Ersteller.</summary>
    private async Task EntwurfAbschliessenAsync(ArbeitszeugnisEntwurf x, AppUser hr, string betreff, string text, byte[]? pdf, string? pdfName)
    {
        if (x.MailboxDocumentId.HasValue)
        {
            var md = await _db.MailboxDocuments.FirstOrDefaultAsync(m => m.Id == x.MailboxDocumentId.Value);
            if (md != null) _db.MailboxDocuments.Remove(md);
            x.MailboxDocumentId = null;
        }
        if (!x.ErstelltVon.HasValue) return;
        var ersteller = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == x.ErstelltVon.Value);
        if (ersteller == null) return;

        var branchId = x.CompanyProfileId
                       ?? await _db.CompanyProfiles.Where(c => c.IsActive).OrderBy(c => c.Id).Select(c => c.Id).FirstOrDefaultAsync();
        string storage;
        if (pdf != null)
        {
            storage = $"{Guid.NewGuid():N}.pdf";
            var dir = Path.Combine(_storagePath, "mailbox", branchId.ToString());
            Directory.CreateDirectory(dir);
            await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, storage), pdf);
        }
        else storage = $"msg-{Guid.NewGuid():N}";

        var doc = new MailboxDocument
        {
            CompanyProfileId = branchId,
            UploadedBy = hr.Id, UploadedAt = DateTime.Now,
            OriginalFilename = betreff, StorageFilename = storage,
            MimeType = pdf != null ? "application/pdf" : null,
            FileSizeBytes = pdf?.Length,
            Bemerkung = pdf != null ? pdfName : null,
            MessageBody = text, EmployeeId = x.EmployeeId,
            TargetType = "USER", TargetUserId = ersteller.Id, NotifyUserId = ersteller.Id,
        };
        _db.MailboxDocuments.Add(doc);
        await _db.SaveChangesAsync();

        // Ankündigung per Mail (ohne Inhalt) — wie bei «Mitteilung an Benutzer».
        if (!string.IsNullOrWhiteSpace(ersteller.Email))
        {
            var to = ersteller.Email!; var name = Name(ersteller); var docId = doc.Id; var abs = Name(hr); var betr = betreff;
            var scopeFactory = _scopeFactory; var log = _log;
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<EmailService>();
                    var cfg = await svc.GetEffectiveConfigAsync();
                    var site = (cfg.SiteUrl ?? "https://onecrew.ch/").TrimEnd('/') + "/";
                    string Enc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
                    var url = $"{site}mobil.html#m={docId}";
                    var html = $@"<div style=""font-family:-apple-system,Segoe UI,Arial,sans-serif;font-size:15px;color:#1a1a1a;line-height:1.5"">
<p>Hallo {Enc(name)}</p>
<p><b>{Enc(abs)}</b> (HR) hat dir in OneCrew eine Mitteilung geschickt:</p>
<p style=""font-size:17px;font-weight:700;margin:14px 0"">{Enc(betr)}</p>
<p><a href=""{url}"" style=""display:inline-block;background:#1a1a1a;color:#fff;text-decoration:none;padding:12px 26px;border-radius:10px;font-weight:600"">Mitteilung öffnen</a></p>
<p style=""font-size:12px;color:#6b6b6b"">Der Inhalt liegt in OneCrew Mobil — nach der Anmeldung siehst du ihn direkt. Diese Mail enthält bewusst keinen Inhalt.</p>
</div>";
                    var txt = $"Hallo {name}\n\n{abs} (HR) hat dir in OneCrew eine Mitteilung geschickt: {betr}\n\nÖffnen: {url}\n";
                    await svc.SendAsync(to, name, $"Neue Mitteilung in OneCrew: {betr}", html, txt, VersandKategorie.Intern);
                }
                catch (Exception ex) { log.LogWarning(ex, "[Zeugnis-Entwurf] Ankündigung an {To} fehlgeschlagen", to); }
            });
        }
    }

    /// <summary>Eingeloggter Benutzer (für Druck-Berechtigung / Entwurf-Ersteller).</summary>
    private async Task<AppUser?> AktuellerBenutzerAsync()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idStr, out var uid)) return null;
        return await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid);
    }

    /// <summary>Druck-Berechtigung des eingeloggten Benutzers (Frontend-Anzeige).</summary>
    [HttpGet("berechtigung")]
    public async Task<IActionResult> Berechtigung()
    {
        var u = await AktuellerBenutzerAsync();
        if (u == null) return Unauthorized();
        var code = ZeugnisBerechtigung.Effektiv(u);
        return Ok(new { code, stufe = ZeugnisBerechtigung.Stufe(code), label = ZeugnisBerechtigung.Label(code) });
    }

    [HttpPost("{empId:int}/pdf")]
    public async Task<IActionResult> GetPdf(int empId, [FromBody] ZeugnisDto dto)
    {
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });

        // Druck-Berechtigung (Walter 06.09.2026): Zeugnis-Funktion über der
        // Stufe des Benutzers → kein PDF, nur Entwurf an HR (Serverseitig,
        // nicht nur am Knopf).
        var benutzer = await AktuellerBenutzerAsync();
        if (benutzer == null) return Unauthorized();
        if (!ZeugnisBerechtigung.DarfDrucken(benutzer, dto.Funktion))
            return StatusCode(403, new { error = "ZEUGNIS_DRUCK_GESPERRT",
                message = $"Zeugnisse für «{dto.Funktion}» darfst du nicht selbst erstellen (deine Stufe: {ZeugnisBerechtigung.Label(ZeugnisBerechtigung.Effektiv(benutzer))}). Bitte als Entwurf an HR senden." });

        var quali = (dto.Qualitaet ?? "gut").Trim().ToLowerInvariant();
        if (quali is not ("genuegend" or "durchschnitt" or "gut" or "sehr_gut"))
            return BadRequest(new { error = "QUALITAET_UNGUELTIG", message = "Qualität muss genuegend, durchschnitt, gut oder sehr_gut sein." });

        var bereiche = (dto.Bereiche ?? new())
            .Select(b => b.Trim().ToLowerInvariant())
            .Where(b => b is "kueche" or "kasse" or "drive")
            .Distinct().ToList();
        if (bereiche.Count == 0 && !dto.Bestaetigung)
            return BadRequest(new { error = "BEREICH_FEHLT", message = "Mindestens einen Bereich wählen (Küche, Kasse, Drive)." });

        // Verträge: jüngster für Filiale + Pensum, ältester Start + jüngstes Ende
        // für die Beschäftigungsdauer (Fallback: EntryDate/ExitDate am MA).
        var emps = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .ToListAsync();
        var last = emps.FirstOrDefault();

        CompanyProfile? cp = null;
        if (last?.CompanyProfileId != null)
            cp = await _db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == last.CompanyProfileId.Value);
        if (cp == null)
            return BadRequest(new { error = "KEINE_FILIALE", message = "Dem MA ist keine Filiale zugeordnet (kein Vertrag mit Filiale)." });

        var von = e.EntryDate
                  ?? emps.OrderBy(x => x.ContractStartDate).FirstOrDefault()?.ContractStartDate
                  ?? DateTime.Today;
        // Bis-Datum (Walter-Korrektur 15.07.2026): IMMER der LETZTE Vertrag —
        // nicht das juengste Enddatum irgendeines (alten) Vertrags. Ist der
        // letzte Vertrag offen und kein Austritt erfasst, kommt das fiktive
        // Austrittsdatum aus dem Modal (Fallback: Ende laufender Monat).
        var lastByStart = emps.OrderByDescending(x => x.ContractStartDate).FirstOrDefault();
        var monatsEnde = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1)
                             .AddMonths(1).AddDays(-1);
        // Walter-Vorgabe 12.08.2026: Das im Modal EINGETRAGENE Austrittsdatum
        // hat IMMER Vorrang (der MA-Austritt ist nur der Vorschlag im Feld).
        // Vorher gewann das Vertragsende über die Eingabe → falsches «bis».
        var bis = (dto.Austritt.HasValue ? dto.Austritt.Value.ToDateTime(TimeOnly.MinValue) : (DateTime?)null)
                  ?? e.ExitDate
                  ?? lastByStart?.ContractEndDate
                  ?? monatsEnde;

        // Vollzeit nur bei FIX/FIX-M mit Pensum ≥ 100 % — Crew/FLEX/MTP = Teilzeit.
        bool vollzeit = last != null
            && (last.EmploymentModel == "FIX" || last.EmploymentModel == "FIX-M")
            && (last.EmploymentPercentage ?? 100m) >= 100m;

        bool female = string.Equals(e.Gender, "female", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(e.Gender, "w", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(e.Gender, "f", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(e.Salutation, "Frau", StringComparison.OrdinalIgnoreCase);

        // Unterzeichner folgt der Zustellart (HR-Idee, Walter 12.08.2026):
        // Versand an MA = EINGELOGGTER User · Abgabe durch Restaurant =
        // Allgemein-Unterzeichner der Filiale (IsDefault, Fallback GF).
        byte[]? sigPng = null; string signerName = ""; string? signerTitle = null;
        if (dto.Abgabe)
        {
            var uba = await _db.UserBranchAccesses.AsNoTracking()
                .Include(a => a.User)
                .Where(a => a.CompanyProfileId == cp.Id && a.IsDefault
                         && a.User != null && a.User.IsActive)
                .FirstOrDefaultAsync()
                ?? await _db.UserBranchAccesses.AsNoTracking()
                    .Include(a => a.User)
                    .Where(a => a.CompanyProfileId == cp.Id && a.Role == "GESCHAEFTSFUEHRER"
                             && a.User != null && a.User.IsActive)
                    .OrderBy(a => a.Id)
                    .FirstOrDefaultAsync();
            if (uba?.User == null)
                return BadRequest(new { error = "KEIN_ALLGEMEIN_UNTERZEICHNER",
                    message = "Kein Allgemein-Unterzeichner für diese Filiale definiert — im Filial-Tab «Unterzeichner» das grüne «Allgemein» setzen, oder «Versand an Mitarbeiter» wählen." });
            sigPng = uba.User.SignaturePng;
            var fullD = $"{uba.User.FirstName} {uba.User.LastName}".Trim();
            signerName = string.IsNullOrWhiteSpace(fullD) ? (uba.User.Username ?? "") : fullD;
            signerTitle = !string.IsNullOrWhiteSpace(uba.FunctionTitle)
                ? uba.FunctionTitle
                : (uba.Role == "GESCHAEFTSFUEHRER" ? "Geschäftsführer/in" : null);
        }
        else
        {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(idStr, out var uid))
        {
            var u = await _db.AppUsers.AsNoTracking()
                .Where(x => x.Id == uid)
                .Select(x => new { x.SignaturePng, x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                sigPng = u.SignaturePng;
                var full = $"{u.FirstName} {u.LastName}".Trim();
                signerName = string.IsNullOrWhiteSpace(full) ? (u.Username ?? "") : full;
            }
            // Funktionsbezeichnung aus dem Filial-Zugang (z.B. «Restaurantleiterin»).
            signerTitle = await _db.UserBranchAccesses.AsNoTracking()
                .Where(a => a.UserId == uid && a.CompanyProfileId == cp.Id
                         && a.FunctionTitle != null && a.FunctionTitle != "")
                .Select(a => a.FunctionTitle)
                .FirstOrDefaultAsync();
        }
        }

        var strasse = string.Join(" ", new[] { cp.Street, cp.HouseNumber }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

        var input = new ArbeitszeugnisInput(
            CompanyName:    cp.CompanyName,
            RestaurantName: cp.BranchName ?? cp.FullDisplayName,
            CompanyStreet:  strasse,
            CompanyZipCity: $"{cp.ZipCode} {cp.City}".Trim(),
            CompanyPhone:   cp.Phone,
            CompanyEmail:   cp.Email,
            Ort:            cp.City ?? "",
            Datum:          dto.Datum.HasValue
                                ? dto.Datum.Value.ToDateTime(TimeOnly.MinValue)
                                : DateTime.Today,
            Salutation:     e.Salutation ?? (female ? "Frau" : "Herr"),
            FirstName:      e.FirstName,
            LastName:       e.LastName,
            DateOfBirth:    e.DateOfBirth,
            WohnOrt:        e.City,
            EmpStreet:      e.Street,
            EmpZipCity:     $"{e.ZipCode} {e.City}".Trim(),
            Von:            von,
            Bis:            bis,
            Vollzeit:       vollzeit,
            Female:         female,
            Qualitaet:      quali,
            Bereiche:       bereiche,
            SignatoryName:  signerName,
            SignatoryTitle: signerTitle,
            SignaturePng:   sigPng,
            AufEigenenWunsch: dto.AufEigenenWunsch,
            Funktion:       string.IsNullOrWhiteSpace(dto.Funktion) ? null : dto.Funktion.Trim(),
            Aufgaben:       dto.Aufgaben,
            Zwischen:       dto.Zwischen,
            Bestaetigung:   dto.Bestaetigung
        );

        var bytes = _pdf.Generate(input);
        var art = dto.Bestaetigung ? "Arbeitsbestaetigung" : dto.Zwischen ? "Zwischenzeugnis" : "Arbeitszeugnis";
        var fileName = $"{art}_{e.LastName}_{e.FirstName}.pdf".Replace(" ", "_");

        // Aus einem Entwurf erstellt (Walter 06.09.2026): Entwurf erledigen,
        // HR-Postfach-Eintrag weg, fertiges PDF als Mitteilung an den Ersteller.
        if (dto.EntwurfId.HasValue)
        {
            var x = await _db.ArbeitszeugnisEntwuerfe.FirstOrDefaultAsync(z => z.Id == dto.EntwurfId.Value);
            if (x != null && x.Status == "offen")
            {
                x.Status = "erledigt"; x.ErledigtVon = benutzer.Id; x.ErledigtAm = DateTime.Now;
                if (x.ErstelltVon != benutzer.Id)
                    await EntwurfAbschliessenAsync(x, benutzer,
                        $"{ArtLabel(x.Art)} für {e.FirstName} {e.LastName} ist erstellt",
                        $"{Name(benutzer)} (HR) hat das {ArtLabel(x.Art)} für {e.FirstName} {e.LastName} aus deinem Entwurf erstellt und unterschrieben. Das PDF ist angehängt — bitte ausdrucken bzw. dem MA übergeben.",
                        bytes, fileName);
                else if (x.MailboxDocumentId.HasValue)
                {
                    var md = await _db.MailboxDocuments.FirstOrDefaultAsync(m => m.Id == x.MailboxDocumentId.Value);
                    if (md != null) _db.MailboxDocuments.Remove(md);
                    x.MailboxDocumentId = null;
                }
                await _db.SaveChangesAsync();
            }
        }

        return File(bytes, "application/pdf", fileName);
    }
}
