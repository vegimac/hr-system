using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Kandidaten-Pipeline GF → HR (Walter-Vorgabe 10.08.2026, Etappe 1):
/// Der GF reicht nach dem Vorstellungsgespräch einen Kandidaten ein (Name,
/// frühester Eintritt, L-GAV-Ausbildung, Onboarding-Wunschtermin, Anhänge).
/// HR prüft in der ONBOARDING-Kachel und nimmt an / lehnt ab (Info zurück
/// ins Filial-Postfach). Bewusst KEIN Employee — der MA entsteht erst nach
/// Annahme in easy@work (Etappe 2: Checkliste + Verknüpfung).
/// Reine Rekrutierungsdaten, keine Lohndaten (EditLock-Whitelist).
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/kandidaten")]
public class KandidatenController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly Services.EcallSmsService _sms;
    private readonly Services.EmailService _email;
    private readonly string _docStorage;   // Wurzel der MA-Dokumente
    private readonly string _storageRoot;  // …/kandidaten

    public KandidatenController(AppDbContext db, IConfiguration config, IWebHostEnvironment env,
                                Services.EcallSmsService sms, Services.EmailService email)
    {
        _db = db;
        _sms = sms;
        _email = email;
        var configured = config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(configured))
            configured = Path.Combine(env.ContentRootPath, "data", "documents");
        _docStorage = configured;
        _storageRoot = Path.Combine(configured, "kandidaten");
    }

    private int? UserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private bool IstHr() => User.IsInRole("admin") || User.IsInRole("superuser");

    private async Task<string?> ActorNameAsync()
    {
        var uid = UserId();
        if (uid == null) return null;
        var u = await _db.AppUsers.AsNoTracking()
            .Where(x => x.Id == uid.Value)
            .Select(x => new { x.FirstName, x.LastName, x.Username })
            .FirstOrDefaultAsync();
        if (u == null) return null;
        var voll = $"{u.FirstName} {u.LastName}".Trim();
        return string.IsNullOrWhiteSpace(voll) ? u.Username : voll;
    }

    /// <summary>Filialen, für die der eingeloggte User Kandidaten einreichen darf.</summary>
    private async Task<HashSet<int>> ErlaubteFilialenAsync()
    {
        if (IstHr())
            return (await _db.CompanyProfiles.AsNoTracking().Select(c => c.Id).ToListAsync()).ToHashSet();
        var uid = UserId();
        if (uid == null) return new HashSet<int>();
        return (await _db.UserBranchAccesses.AsNoTracking()
            .Where(u => u.UserId == uid.Value)
            .Select(u => u.CompanyProfileId)
            .ToListAsync()).ToHashSet();
    }

    private string KandidatDir(int kandidatId)
    {
        var d = Path.Combine(_storageRoot, kandidatId.ToString());
        Directory.CreateDirectory(d);
        return d;
    }

    // ── Onboarding-Termine für das Wunschtermin-Dropdown (auch für GF) ─────
    [HttpGet("termine")]
    public async Task<IActionResult> Termine()
    {
        var heute = DateOnly.FromDateTime(DateTime.Now);
        var termine = await _db.HrInterviewTermine.AsNoTracking()
            .Where(t => t.Datum >= heute)
            .OrderBy(t => t.Datum).ThenBy(t => t.VonZeit)
            .ToListAsync();
        var ids = termine.Select(t => t.Id).ToList();
        var belegt = await _db.HrInterviewBuchungen.AsNoTracking()
            .Where(b => ids.Contains(b.TerminId) && b.Status == "GEPLANT")
            .GroupBy(b => b.TerminId)
            .Select(g => new { TerminId = g.Key, N = g.Count() })
            .ToListAsync();
        return Ok(termine.Select(t => new
        {
            t.Id,
            datum = t.Datum.ToString("yyyy-MM-dd"),
            von = t.VonZeit.ToString("HH:mm"),
            bis = t.BisZeit?.ToString("HH:mm"),
            frei = t.Plaetze - (belegt.FirstOrDefault(x => x.TerminId == t.Id)?.N ?? 0),
        }));
    }

    // ── GF: Kandidat einreichen (multipart: Felder + Anhänge) ──────────────
    [HttpPost]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Create(
        [FromForm] int companyProfileId,
        [FromForm] string? vorname,
        [FromForm] string? name,
        [FromForm] string? telefon,
        [FromForm] string? email,
        [FromForm] string? fruehesterEintritt,
        [FromForm] string? lgavAusbildung,
        [FromForm] int? wunschTerminId,
        [FromForm] string? bemerkung,
        [FromForm] List<IFormFile>? files)
    {
        if (string.IsNullOrWhiteSpace(vorname) || string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "NAME_FEHLT", message = "Vorname und Name angeben." });
        var erlaubt = await ErlaubteFilialenAsync();
        if (!erlaubt.Contains(companyProfileId))
            return StatusCode(403, new { error = "KEINE_FILIALE", message = "Kein Zugriff auf diese Filiale." });

        DateOnly? eintritt = null;
        if (!string.IsNullOrWhiteSpace(fruehesterEintritt))
        {
            if (!DateOnly.TryParse(fruehesterEintritt, out var d))
                return BadRequest(new { error = "DATUM_UNGUELTIG" });
            eintritt = d;
        }

        var actor = await ActorNameAsync();
        var k = new Kandidat
        {
            CompanyProfileId = companyProfileId,
            Vorname = vorname.Trim(),
            Name = name.Trim(),
            Telefon = string.IsNullOrWhiteSpace(telefon) ? null : telefon.Trim(),
            Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            FruehesterEintritt = eintritt,
            LgavAusbildung = string.IsNullOrWhiteSpace(lgavAusbildung) ? null : lgavAusbildung.Trim(),
            WunschTerminId = wunschTerminId,
            Bemerkung = string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung.Trim(),
            Status = "NEU",
            CreatedAt = DateTime.Now,
            CreatedBy = actor,
        };
        _db.Kandidaten.Add(k);
        await _db.SaveChangesAsync();

        // Anhänge speichern (best-effort pro Datei).
        foreach (var f in files ?? new List<IFormFile>())
        {
            if (f.Length == 0) continue;
            var orig = Path.GetFileName(f.FileName ?? "datei");
            if (orig.Length > 200) orig = orig[..200];
            var ext = Path.GetExtension(orig);
            var storage = $"{Guid.NewGuid():N}{ext}";
            var path = Path.Combine(KandidatDir(k.Id), storage);
            await using (var fs = System.IO.File.Create(path))
                await f.CopyToAsync(fs);
            _db.KandidatDokumente.Add(new KandidatDokument
            {
                KandidatId = k.Id,
                OriginalFilename = orig,
                StorageFilename = storage,
                CreatedAt = DateTime.Now,
                CreatedBy = actor,
            });
        }
        await _db.SaveChangesAsync();
        return Ok(new { k.Id });
    }

    // ── Liste: GF sieht seine Filialen, HR alles; optional nach Status ─────
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status)
    {
        var erlaubt = await ErlaubteFilialenAsync();
        var q = _db.Kandidaten.AsNoTracking().Where(k => erlaubt.Contains(k.CompanyProfileId));
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(k => k.Status == status);
        var list = await q.OrderByDescending(k => k.CreatedAt).Take(200).ToListAsync();

        var kIds = list.Select(k => k.Id).ToList();
        var doks = await _db.KandidatDokumente.AsNoTracking()
            .Where(d => kIds.Contains(d.KandidatId))
            .Select(d => new { d.Id, d.KandidatId, d.OriginalFilename })
            .ToListAsync();
        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.WorkLocation, c.City, c.BranchName })
            .ToListAsync();
        var terminIds = list.Where(k => k.WunschTerminId != null).Select(k => k.WunschTerminId!.Value).Distinct().ToList();
        var termine = await _db.HrInterviewTermine.AsNoTracking()
            .Where(t => terminIds.Contains(t.Id)).ToListAsync();

        return Ok(list.Select(k =>
        {
            var b = branches.FirstOrDefault(x => x.Id == k.CompanyProfileId);
            var t = k.WunschTerminId == null ? null : termine.FirstOrDefault(x => x.Id == k.WunschTerminId.Value);
            return new
            {
                k.Id,
                k.Vorname,
                k.Name,
                k.Telefon,
                k.Email,
                fruehesterEintritt = k.FruehesterEintritt?.ToString("yyyy-MM-dd"),
                k.LgavAusbildung,
                k.Bemerkung,
                k.Status,
                k.Ablehnungsgrund,
                createdAt = k.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                k.CreatedBy,
                k.DecidedBy,
                decidedAt = k.DecidedAt?.ToString("yyyy-MM-dd HH:mm"),
                absageGesendetAm = k.AbsageGesendetAm?.ToString("yyyy-MM-dd HH:mm"),
                k.AbsageKanal,
                filiale = b == null ? "" : (!string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation : (b.City ?? b.BranchName ?? "")),
                wunschTermin = t == null ? null : $"{t.Datum:dd.MM.yyyy} {t.VonZeit:HH\\:mm}",
                dokumente = doks.Where(d => d.KandidatId == k.Id)
                    .Select(d => new { d.Id, name = d.OriginalFilename }),
            };
        }));
    }

    /// <summary>Anzahl unbearbeitete Kandidaten (Badge in der ONBOARDING-Kachel).</summary>
    [HttpGet("count-offen")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> CountOffen()
        => Ok(new { offen = await _db.Kandidaten.CountAsync(k => k.Status == "NEU") });

    public class EntscheidDto
    {
        public bool Angenommen { get; set; }
        public string? Grund { get; set; }
    }

    // ── HR: Entscheid — Info geht zurück ins Filial-Postfach des GF ─────────
    [HttpPost("{id:int}/entscheid")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Entscheid(int id, [FromBody] EntscheidDto dto)
    {
        var k = await _db.Kandidaten.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        if (k.Status != "NEU")
            return Conflict(new { error = "SCHON_ENTSCHIEDEN", message = $"Kandidat ist bereits «{k.Status}»." });
        if (!dto.Angenommen && string.IsNullOrWhiteSpace(dto.Grund))
            return BadRequest(new { error = "GRUND_FEHLT", message = "Bei einer Ablehnung bitte den Grund angeben." });

        var actor = await ActorNameAsync();
        k.Status = dto.Angenommen ? "ANGENOMMEN" : "ABGELEHNT";
        k.Ablehnungsgrund = dto.Angenommen ? null : dto.Grund!.Trim();
        k.DecidedAt = DateTime.Now;
        k.DecidedBy = actor;
        await _db.SaveChangesAsync();

        // Best-effort-Info ins Filial-Postfach (Konvention: nicht-persönliche
        // Nachrichten gehen an die Filiale).
        try
        {
            // Nur die Info an den GF — die easy@work-Erfassung und das gesamte
            // Onboarding macht HR selbst (Walter-Vorgabe 10.08.2026).
            var text = dto.Angenommen
                ? $"HR hat den Kandidaten {k.Vorname} {k.Name} ANGENOMMEN. HR übernimmt die Erfassung und das Onboarding."
                : $"HR hat den Kandidaten {k.Vorname} {k.Name} abgelehnt. Grund: {k.Ablehnungsgrund}";
            _db.MailboxDocuments.Add(new MailboxDocument
            {
                CompanyProfileId = k.CompanyProfileId,
                UploadedBy = UserId(),
                UploadedAt = DateTime.Now,
                OriginalFilename = $"Kandidat {k.Vorname} {k.Name}",
                StorageFilename = $"msg-{Guid.NewGuid():N}",
                MimeType = null,
                FileSizeBytes = null,
                Bemerkung = "Kandidaten-Entscheid",
                MessageBody = text,
                EmployeeId = null,
                NotifyUserId = null,
                TargetType = "BRANCH",
            });
            await _db.SaveChangesAsync();
        }
        catch { /* best-effort */ }

        return Ok(new { ok = true, k.Status });
    }

    public class AbsageDto
    {
        /// <summary>SMS | EMAIL</summary>
        public string? Kanal { get; set; }
    }

    // ── HR: Absage an den Kandidaten senden (Etappe 2) ──────────────────────
    [HttpPost("{id:int}/absage")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Absage(int id, [FromBody] AbsageDto dto)
    {
        var k = await _db.Kandidaten.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        if (k.Status != "ABGELEHNT")
            return Conflict(new { error = "NICHT_ABGELEHNT", message = "Absagen gibt es nur für abgelehnte Kandidaten." });

        var firma = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == k.CompanyProfileId)
            .Select(c => c.FullDisplayName)
            .FirstOrDefaultAsync() ?? "McDonald's";

        var kanal = (dto.Kanal ?? "").ToUpperInvariant();
        if (kanal == "SMS")
        {
            var tel = (k.Telefon ?? "").Trim();
            if (tel.Length == 0)
                return BadRequest(new { error = "TELEFON_FEHLT", message = "Keine Telefonnummer erfasst." });
            var text = $"Guten Tag {k.Vorname}, vielen Dank für dein Interesse und das Gespräch bei {firma}. "
                     + "Leider können wir dir zurzeit keine Stelle anbieten. Wir wünschen dir für deine Zukunft alles Gute.";
            var res = await _sms.SendSmsAsync(tel, text, purpose: "KANDIDAT_ABSAGE");
            if (!res.Ok)
                return StatusCode(502, new { error = $"SMS-Versand fehlgeschlagen: {res.Error}" });
        }
        else if (kanal == "EMAIL")
        {
            var mail = (k.Email ?? "").Trim();
            if (mail.Length == 0)
                return BadRequest(new { error = "EMAIL_FEHLT", message = "Keine E-Mail-Adresse erfasst." });
            var subject = $"Deine Bewerbung bei {firma}";
            var textBody = $"Guten Tag {k.Vorname} {k.Name}\n\n"
                + $"Vielen Dank für dein Interesse und das Gespräch bei {firma}. "
                + "Wir haben uns intensiv mit deiner Bewerbung auseinandergesetzt — leider können wir dir zurzeit keine Stelle anbieten.\n\n"
                + "Wir wünschen dir für deine berufliche Zukunft alles Gute.\n\n"
                + $"Freundliche Grüsse\n{firma}";
            var htmlBody = System.Net.WebUtility.HtmlEncode(textBody).Replace("\n", "<br>");
            var ok = await _email.SendAsync(mail, $"{k.Vorname} {k.Name}", subject, htmlBody, textBody);
            if (!ok)
                return StatusCode(502, new { error = "E-Mail-Versand fehlgeschlagen (SMTP-Konfiguration prüfen)." });
        }
        else
        {
            return BadRequest(new { error = "KANAL_UNGUELTIG", message = "Kanal SMS oder EMAIL angeben." });
        }

        k.AbsageGesendetAm = DateTime.Now;
        k.AbsageKanal = kanal;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ── HR: MA-Vorschläge für die Verknüpfung (nach easy-Import) ────────────
    [HttpGet("{id:int}/ma-vorschlaege")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> MaVorschlaege(int id)
    {
        var k = await _db.Kandidaten.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        var vn = k.Vorname.Trim();
        var nn = k.Name.Trim();
        // Felder ROH laden, Datum im Speicher formatieren (Datum/Zeit-Regelwerk
        // 13.07.2026: ToString mit Format ist nicht EF-übersetzbar).
        var roh = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden
                     && (EF.Functions.ILike(e.FirstName ?? "", $"%{vn}%")
                      || EF.Functions.ILike(e.LastName ?? "", $"%{nn}%")))
            .OrderByDescending(e => e.EntryDate)
            .Take(12)
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber, e.EntryDate })
            .ToListAsync();
        return Ok(roh.Select(e => new
        {
            e.Id,
            name = $"{e.FirstName} {e.LastName}".Trim(),
            e.EmployeeNumber,
            entryDate = e.EntryDate?.ToString("yyyy-MM-dd"),
        }));
    }

    public class VerknuepfenDto
    {
        public int EmployeeId { get; set; }
    }

    // ── HR: Kandidat mit importiertem MA verknüpfen — Dokumente wandern in
    //    die Personalakte, danach wird der Kandidat GELÖSCHT (Etappe 2). ─────
    [HttpPost("{id:int}/verknuepfen")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Verknuepfen(int id, [FromBody] VerknuepfenDto dto)
    {
        var k = await _db.Kandidaten.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        if (k.Status != "ANGENOMMEN")
            return Conflict(new { error = "NICHT_ANGENOMMEN", message = "Nur angenommene Kandidaten können verknüpft werden." });
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == dto.EmployeeId);
        if (emp == null) return NotFound(new { error = "MA_FEHLT" });

        // Dokumenttyp: «Bewerbung*» bevorzugt, sonst «Sonstig*», sonst erster aktiver.
        var typen = await _db.DokumentTypen.AsNoTracking().Where(t => t.Aktiv).ToListAsync();
        var typ = typen.FirstOrDefault(t => t.Name.Contains("Bewerbung", StringComparison.OrdinalIgnoreCase))
               ?? typen.FirstOrDefault(t => t.Name.Contains("Sonstig", StringComparison.OrdinalIgnoreCase))
               ?? typen.FirstOrDefault();
        if (typ == null)
            return Conflict(new { error = "KEIN_DOKUMENTTYP", message = "Kein aktiver Dokumenttyp vorhanden." });

        var branchCode = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == k.CompanyProfileId)
            .Select(c => c.RestaurantCode)
            .FirstOrDefaultAsync() ?? "0";
        var safeBranch = new string(branchCode.Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-').ToArray());
        if (safeBranch.Length == 0) safeBranch = "0";

        var actor = await ActorNameAsync();
        var doks = await _db.KandidatDokumente.Where(d => d.KandidatId == k.Id).ToListAsync();
        int uebernommen = 0;
        foreach (var d in doks)
        {
            var src = Path.Combine(_storageRoot, k.Id.ToString(), d.StorageFilename);
            if (!System.IO.File.Exists(src)) continue;
            var empDir = Path.Combine(_docStorage, safeBranch, emp.Id.ToString());
            Directory.CreateDirectory(empDir);
            var ext = Path.GetExtension(d.OriginalFilename);
            var storageName = Guid.NewGuid().ToString("N") + ext;
            System.IO.File.Copy(src, Path.Combine(empDir, storageName));
            _db.EmployeeDokumente.Add(new EmployeeDokument
            {
                EmployeeId = emp.Id,
                DokumentTypId = typ.Id,
                BranchCode = safeBranch,
                FilenameOriginal = d.OriginalFilename,
                FilenameStorage = storageName,
                MimeType = ext.ToLowerInvariant() switch
                {
                    ".pdf" => "application/pdf",
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    _ => "application/octet-stream",
                },
                GroesseBytes = new FileInfo(src).Length,
                Bemerkung = "Aus Kandidaten-Dossier übernommen",
                HochgeladenVon = UserId(),
                HochgeladenAm = DateTime.Now,
            });
            uebernommen++;
        }

        // Kandidat + Dateien endgültig entfernen (Walter: mit dem Import
        // in OneCrew kann der Kandidat gelöscht werden).
        _db.KandidatDokumente.RemoveRange(doks);
        _db.Kandidaten.Remove(k);
        await _db.SaveChangesAsync();
        try
        {
            var dir = Path.Combine(_storageRoot, k.Id.ToString());
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* best-effort */ }

        return Ok(new { ok = true, dokumente = uebernommen, employeeId = emp.Id });
    }

    // ── Dokument ansehen (GF eigene Filialen, HR alle) ──────────────────────
    [HttpGet("dokumente/{dokId:int}/preview")]
    public async Task<IActionResult> DokPreview(int dokId)
    {
        var d = await _db.KandidatDokumente.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dokId);
        if (d == null) return NotFound();
        var k = await _db.Kandidaten.AsNoTracking().FirstOrDefaultAsync(x => x.Id == d.KandidatId);
        if (k == null) return NotFound();
        var erlaubt = await ErlaubteFilialenAsync();
        if (!erlaubt.Contains(k.CompanyProfileId)) return Forbid();
        var path = Path.Combine(_storageRoot, d.KandidatId.ToString(), d.StorageFilename);
        if (!System.IO.File.Exists(path)) return NotFound();
        var mime = Path.GetExtension(d.OriginalFilename).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream",
        };
        return PhysicalFile(path, mime);
    }
}
