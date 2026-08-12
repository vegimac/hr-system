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

    // ── GF: Kandidat bearbeiten + weitere Anhänge (Walter 11.08.2026) ──────
    //    Nur solange HR noch NICHT entschieden hat (Status NEU).
    [HttpPost("{id:int}/update")]
    [RequestSizeLimit(60_000_000)]
    public async Task<IActionResult> Update(
        int id,
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
        var k = await _db.Kandidaten.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        var erlaubt = await ErlaubteFilialenAsync();
        if (!erlaubt.Contains(k.CompanyProfileId) || !erlaubt.Contains(companyProfileId))
            return StatusCode(403, new { error = "KEINE_FILIALE", message = "Kein Zugriff auf diese Filiale." });
        if (k.Status != "NEU")
            return Conflict(new { error = "BEREITS_ENTSCHIEDEN", message = "HR hat bereits entschieden — Bearbeiten ist nicht mehr möglich." });
        if (string.IsNullOrWhiteSpace(vorname) || string.IsNullOrWhiteSpace(name))
            return BadRequest(new { error = "NAME_FEHLT", message = "Vorname und Name angeben." });

        DateOnly? eintritt = null;
        if (!string.IsNullOrWhiteSpace(fruehesterEintritt))
        {
            if (!DateOnly.TryParse(fruehesterEintritt, out var d))
                return BadRequest(new { error = "DATUM_UNGUELTIG" });
            eintritt = d;
        }

        k.CompanyProfileId = companyProfileId;
        k.Vorname = vorname.Trim();
        k.Name = name.Trim();
        k.Telefon = string.IsNullOrWhiteSpace(telefon) ? null : telefon.Trim();
        k.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        k.FruehesterEintritt = eintritt;
        k.LgavAusbildung = string.IsNullOrWhiteSpace(lgavAusbildung) ? null : lgavAusbildung.Trim();
        k.WunschTerminId = wunschTerminId;
        k.Bemerkung = string.IsNullOrWhiteSpace(bemerkung) ? null : bemerkung.Trim();

        // Neue Anhänge werden ANGEHÄNGT — bestehende bleiben unverändert.
        var actor = await ActorNameAsync();
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
        // Willkommenstag-Buchungen (Antwort-Status) pro Kandidat.
        var wkBuchungen = await _db.HrInterviewBuchungen.AsNoTracking()
            .Where(b => b.KandidatId != null && kIds.Contains(b.KandidatId.Value))
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return Ok(list.Select(k =>
        {
            var b = branches.FirstOrDefault(x => x.Id == k.CompanyProfileId);
            var t = k.WunschTerminId == null ? null : termine.FirstOrDefault(x => x.Id == k.WunschTerminId.Value);
            return new
            {
                k.Id,
                k.CompanyProfileId,
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
                erledigtAm = k.ErledigtAm?.ToString("yyyy-MM-dd HH:mm"),
                k.VerknuepftEmployeeId,
                k.Notiz,
                filiale = b == null ? "" : (!string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation : (b.City ?? b.BranchName ?? "")),
                k.WunschTerminId,
                wunschTermin = t == null ? null : $"{t.Datum:dd.MM.yyyy} {t.VonZeit:HH\\:mm}",
                willkommenGesendetAm = k.WillkommenGesendetAm?.ToString("yyyy-MM-dd HH:mm"),
                willkommenAntwort = wkBuchungen.FirstOrDefault(b => b.KandidatId == k.Id)?.MaAntwort,
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
        /// <summary>Pro Anhang: Ziel-Dokumenttyp + Beschreibung (Walter 10.08.2026).</summary>
        public List<VerknuepfenDok>? Dokumente { get; set; }
    }

    public class VerknuepfenDok
    {
        public int DokId { get; set; }
        public int DokumentTypId { get; set; }
        public string? Bemerkung { get; set; }
        public bool Uebernehmen { get; set; } = true;
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

        // Dokument-Zuordnung pro Anhang (Walter 10.08.2026): das Frontend
        // liefert pro Dokument Ziel-Typ + Beschreibung; Fallback (ohne
        // Zuordnung) = Typ «Bewerbung*»/«Sonstig*».
        var typen = await _db.DokumentTypen.AsNoTracking().Where(t => t.Aktiv).ToListAsync();
        var fallbackTyp = typen.FirstOrDefault(t => t.Name.Contains("Bewerbung", StringComparison.OrdinalIgnoreCase))
                       ?? typen.FirstOrDefault(t => t.Name.Contains("Sonstig", StringComparison.OrdinalIgnoreCase))
                       ?? typen.FirstOrDefault();
        if (fallbackTyp == null)
            return Conflict(new { error = "KEIN_DOKUMENTTYP", message = "Kein aktiver Dokumenttyp vorhanden." });
        var typById = typen.ToDictionary(t => t.Id);
        var zuordnung = (dto.Dokumente ?? new List<VerknuepfenDok>()).ToDictionary(d => d.DokId);

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
            // Zuordnung des Frontends: Typ + Beschreibung pro Dokument;
            // «nicht übernehmen» wird respektiert (Dokument bleibt nur beim
            // Kandidaten und verschwindet mit der 30-Tage-Routine).
            zuordnung.TryGetValue(d.Id, out var z);
            if (z != null && !z.Uebernehmen) continue;
            var typ = (z != null && typById.ContainsKey(z.DokumentTypId)) ? typById[z.DokumentTypId] : fallbackTyp;
            var bemerkung = !string.IsNullOrWhiteSpace(z?.Bemerkung)
                ? z!.Bemerkung!.Trim()
                : Path.GetFileNameWithoutExtension(d.OriginalFilename);

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
                Bemerkung = bemerkung,
                HochgeladenVon = UserId(),
                HochgeladenAm = DateTime.Now,
            });
            uebernommen++;
        }

        // Willkommenstag-Buchung an den MA übergeben (Walter 11.08.2026): die
        // Buchung aus der Willkommenstag-SMS (inkl. Bestätigungs-Status)
        // hängt danach am MA — die spätere Vertrags-SMS bucht nicht doppelt.
        var kandBuchungen = await _db.HrInterviewBuchungen
            .Where(b => b.KandidatId == k.Id).ToListAsync();
        foreach (var b in kandBuchungen) b.EmployeeId = emp.Id;

        // Wunschtermin des GF an den MA übergeben (Walter 10.08.2026) — er
        // erscheint beim Einladen im Onboarding-Kalender.
        if (k.WunschTerminId != null)
        {
            var vorhanden = await _db.OnboardingWuensche.FirstOrDefaultAsync(w => w.EmployeeId == emp.Id);
            if (vorhanden != null) vorhanden.TerminId = k.WunschTerminId.Value;
            else _db.OnboardingWuensche.Add(new OnboardingWunsch
            {
                EmployeeId = emp.Id,
                TerminId = k.WunschTerminId.Value,
                CreatedAt = DateTime.Now,
            });
        }

        // KEIN Sofort-Löschen (Walter-Korrektur 10.08.2026): der Kandidat wird
        // als ERLEDIGT markiert (mit MA-Referenz) und erst 30 Tage später von
        // der täglichen Routine entfernt — so bleibt nachvollziehbar, wer es
        // war, falls Rückfragen kommen.
        k.Status = "ERLEDIGT";
        k.ErledigtAm = DateTime.Now;
        k.VerknuepftEmployeeId = emp.Id;
        await _db.SaveChangesAsync();

        return Ok(new { ok = true, dokumente = uebernommen, employeeId = emp.Id });
    }

    /// <summary>
    /// Entscheid zurücknehmen (Walter 11.08.2026): ANGENOMMEN → NEU jederzeit
    /// (solange nicht verknüpft); ABGELEHNT → NEU nur solange die Absage noch
    /// NICHT versendet wurde.
    /// </summary>
    [HttpPost("{id:int}/entscheid-zuruecknehmen")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> EntscheidZuruecknehmen(int id)
    {
        var k = await _db.Kandidaten.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        if (k.Status == "ABGELEHNT" && k.AbsageGesendetAm != null)
            return Conflict(new { error = "ABSAGE_GESENDET", message = "Die Absage wurde bereits versendet — der Entscheid kann nicht mehr zurückgenommen werden." });
        if (k.Status != "ANGENOMMEN" && k.Status != "ABGELEHNT")
            return Conflict(new { error = "STATUS_UNGUELTIG", message = $"Status «{k.Status}» kann nicht zurückgenommen werden." });
        k.Status = "NEU";
        k.Ablehnungsgrund = null;
        k.DecidedAt = null;
        k.DecidedBy = null;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    public class TerminDto
    {
        public int? TerminId { get; set; }
    }

    /// <summary>
    /// HR setzt/ändert den Onboarding-Tag direkt in der Kandidaten-Karte
    /// (Walter 11.08.2026). Der Termin wird beim Verknüpfen als
    /// OnboardingWunsch an den MA übergeben und beim Einladen vorausgewählt.
    /// </summary>
    [HttpPost("{id:int}/termin")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> SetTermin(int id, [FromBody] TerminDto dto)
    {
        var k = await _db.Kandidaten.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        if (dto.TerminId != null && dto.TerminId != k.WunschTerminId)
        {
            var t = await _db.HrInterviewTermine.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dto.TerminId.Value);
            if (t == null) return NotFound(new { error = "TERMIN_FEHLT" });
            if (t.Datum < DateOnly.FromDateTime(DateTime.Now))
                return Conflict(new { error = "TERMIN_VERGANGEN", message = "Der gewählte Termin liegt in der Vergangenheit." });
            int belegt = await _db.HrInterviewBuchungen.CountAsync(b => b.TerminId == t.Id && b.Status == "GEPLANT");
            if (belegt >= t.Plaetze)
                return Conflict(new { error = "AUSGEBUCHT", message = "Der gewählte Termin ist ausgebucht." });
        }
        k.WunschTerminId = dto.TerminId;
        // Läuft bereits eine Willkommenstag-Buchung, zieht sie mit um
        // (Walter 11.08.2026) — neue Zeit = neue Bestätigung nötig.
        var bu = await _db.HrInterviewBuchungen.FirstOrDefaultAsync(b => b.KandidatId == id && b.Status == "GEPLANT");
        if (bu != null)
        {
            if (dto.TerminId == null)
            {
                bu.Status = "ABGESAGT"; // Termin entfernt → Platz frei
            }
            else if (bu.TerminId != dto.TerminId.Value)
            {
                bu.TerminId = dto.TerminId.Value;
                bu.MaAntwort = null;
                bu.MaAntwortAm = null;
            }
        }
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ══ Willkommenstag-SMS an den KANDIDATEN (Walter 11.08.2026) ═══════════
    // Neuer Ablauf: die Einladung zum Willkommenstag geht DIREKT an den
    // Kandidaten (VOR der easy@work-Erfassung). Eigener öffentlicher Link
    // /willkommen/{token} mit Annehmen/Absagen; Annahme = fix gebucht + HR-
    // Meldung, Absage = Platz frei + HR-Meldung. Die Vertrags-SMS (mit
    // Vertrag + Dokumenten) folgt später separat nach dem Import.

    private static string WkHash(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static (string token, string hash) WkNewToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, WkHash(token));
    }

    private static readonly string[] WkWochentage =
        { "Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag" };

    [HttpPost("{id:int}/willkommen-sms")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> WillkommenSms(int id)
    {
        var k = await _db.Kandidaten.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        if (k.Status != "ANGENOMMEN")
            return Conflict(new { error = "NICHT_ANGENOMMEN", message = "Die Willkommenstag-SMS gibt es erst nach der Annahme des Kandidaten." });
        var tel = (k.Telefon ?? "").Trim();
        if (tel.Length == 0)
            return BadRequest(new { error = "KEIN_TELEFON", message = "Für diesen Kandidaten ist keine Handynummer erfasst." });
        if (k.WunschTerminId == null)
            return Conflict(new { error = "KEIN_TERMIN", message = "Zuerst oben einen Onboarding-Tag wählen." });
        var termin = await _db.HrInterviewTermine.AsNoTracking().FirstOrDefaultAsync(t => t.Id == k.WunschTerminId.Value);
        if (termin == null) return NotFound(new { error = "TERMIN_FEHLT" });
        if (termin.Datum < DateOnly.FromDateTime(DateTime.Now))
            return Conflict(new { error = "TERMIN_VERGANGEN", message = "Der gewählte Termin liegt in der Vergangenheit." });

        // Kapazität nur prüfen, wenn dieser Kandidat den Platz noch nicht hält.
        var buchung = await _db.HrInterviewBuchungen.FirstOrDefaultAsync(b => b.KandidatId == k.Id && b.Status == "GEPLANT");
        if (buchung == null || buchung.TerminId != termin.Id)
        {
            int belegt = await _db.HrInterviewBuchungen.CountAsync(b => b.TerminId == termin.Id && b.Status == "GEPLANT");
            if (belegt >= termin.Plaetze)
                return Conflict(new { error = "AUSGEBUCHT", message = "Der gewählte Termin ist ausgebucht." });
        }

        var cp = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == k.CompanyProfileId);
        var firma = cp?.FullDisplayName ?? "McDonald's";
        var siteRow = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync();
        var baseUrl = (siteRow != null && !string.IsNullOrWhiteSpace(siteRow.SiteUrl))
            ? siteRow.SiteUrl.Trim() : "https://onecrew.ch/";
        var (token, hash) = WkNewToken();
        var url = $"{baseUrl.TrimEnd('/')}/willkommen/{token}";

        var wt = WkWochentage[(int)termin.Datum.DayOfWeek];
        var zeit = termin.BisZeit.HasValue
            ? $"{termin.VonZeit:HH\\:mm}–{termin.BisZeit.Value:HH\\:mm}"
            : $"{termin.VonZeit:HH\\:mm}";

        // SMS-Text aus der pflegbaren Moments-Vorlage WILLKOMMENSTAG.
        string smsText;
        var tpl = await _db.MomentTexts
            .Include(t => t.MomentType)
            .Where(t => t.IsActive && t.MomentType != null && t.MomentType.Code == "WILLKOMMENSTAG")
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .FirstOrDefaultAsync();
        if (tpl != null && !string.IsNullOrWhiteSpace(tpl.SmsText))
            smsText = tpl.SmsText
                .Replace("{Vorname}", k.Vorname)
                .Replace("{Firma}", firma)
                .Replace("{Wochentag}", wt)
                .Replace("{Datum}", termin.Datum.ToString("dd.MM.yyyy"))
                .Replace("{Zeit}", zeit)
                .Replace("{Link}", url);
        else
            smsText = $"Hallo {k.Vorname}, herzlich willkommen bei {firma}! Dein Willkommenstag: {wt}, {termin.Datum:dd.MM.yyyy} um {zeit}. Bitte bestätige hier: {url}";

        var res = await _sms.SendSmsAsync(tel, smsText, purpose: "KANDIDAT_WILLKOMMEN");
        if (!res.Ok)
            return StatusCode(502, new { error = $"SMS-Versand fehlgeschlagen: {res.Error}" });

        // Nach SMS-Erfolg: Platz buchen bzw. bestehende Buchung umziehen.
        var maName = $"{k.Vorname} {k.Name}".Trim();
        if (buchung == null)
        {
            _db.HrInterviewBuchungen.Add(new HrInterviewBuchung
            {
                TerminId = termin.Id,
                Kandidat = maName,
                Telefon = tel,
                Bemerkung = "Willkommenstag-Einladung",
                Status = "GEPLANT",
                CreatedAt = DateTime.Now,
                CreatedBy = "Willkommenstag-Einladung",
                KandidatId = k.Id,
            });
        }
        else
        {
            if (buchung.TerminId != termin.Id) { buchung.MaAntwort = null; buchung.MaAntwortAm = null; }
            buchung.TerminId = termin.Id;
            buchung.Kandidat = maName;
            buchung.Telefon = tel;
        }
        k.WillkommenTokenHash = hash;
        k.WillkommenGesendetAm = DateTime.Now;
        await _db.SaveChangesAsync();

        var redirect = await _db.EcallSettings.AsNoTracking()
            .Where(r => r.Id == 1).Select(r => r.TestRedirectTo).FirstOrDefaultAsync();
        return Ok(new
        {
            ok = true,
            to = tel,
            redirectedTo = string.IsNullOrWhiteSpace(redirect) ? null : redirect!.Trim(),
        });
    }

    // ── Öffentliche Landing-Page /willkommen/{token} ───────────────────────
    // OneCrew-Look (Walter 11.08.2026): warmer Verlauf, Glas-Karte, ruhiger
    // Kohlestift-Stil (Text #3f3f3f, Kohle-Pillen statt greller Farben).
    private static string WkHtml(string title, string inner) => $@"<!doctype html>
<html lang='de'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<meta name='robots' content='noindex'>
<title>{title}</title>
<style>
  body {{ margin:0; font-family:-apple-system,'Segoe UI',Roboto,sans-serif;
         background:linear-gradient(135deg,#f7f4ef 0%,#efebe3 48%,#faf8f5 100%);
         background-attachment:fixed; min-height:100vh; color:#3f3f3f; }}
  .wrap {{ max-width:560px; margin:0 auto; padding:28px 16px; }}
  .card {{ background:rgba(255,255,255,0.60); border:1px solid rgba(255,255,255,0.70);
          border-radius:18px; padding:24px 22px;
          box-shadow:0 14px 40px rgba(70,64,55,0.16), inset 0 1px 0 rgba(255,255,255,0.58); }}
  h1 {{ font-size:20px; font-weight:800; letter-spacing:-0.2px; margin:0 0 12px; color:#3f3f3f; }}
  p {{ color:#646464; }}
  .brand {{ display:flex; align-items:center; gap:8px; margin-bottom:14px;
           font-weight:800; font-size:13px; letter-spacing:1.4px; text-transform:uppercase; color:#8b8b8b; }}
  .brand::after {{ content:''; flex:1; height:1px; background:rgba(60,55,48,0.14); }}
</style></head>
<body><div class='wrap'><div class='card'><div class='brand'>OneCrew</div>{inner}</div>
<div style='text-align:center;color:#b0aca4;font-size:11px;margin-top:14px'>OneCrew · Schaub Restaurants</div>
</div></body></html>";

    [AllowAnonymous]
    [HttpGet("/willkommen/{token}")]
    public async Task<IActionResult> WillkommenLanding(string token)
    {
        var hash = WkHash(token);
        var k = await _db.Kandidaten.AsNoTracking().FirstOrDefaultAsync(x => x.WillkommenTokenHash == hash);
        if (k == null)
            return Content(WkHtml("Link nicht gefunden", "<h1>Link nicht gefunden</h1><p>Dieser Einladungs-Link ist nicht mehr gültig.</p>"), "text/html; charset=utf-8");

        var buchung = await _db.HrInterviewBuchungen.AsNoTracking()
            .Where(b => b.KandidatId == k.Id)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync();
        var terminId = buchung?.TerminId ?? k.WunschTerminId;
        var termin = terminId == null ? null
            : await _db.HrInterviewTermine.AsNoTracking().FirstOrDefaultAsync(t => t.Id == terminId.Value);
        var cp = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == k.CompanyProfileId);
        var firma = cp?.FullDisplayName ?? "McDonald's";
        if (termin == null)
            return Content(WkHtml("Willkommenstag", $"<h1>Herzlich willkommen bei {firma}!</h1><p>Dein Willkommenstag wird gerade geplant — das HR-Team meldet sich bei dir.</p>"), "text/html; charset=utf-8");

        var wt = WkWochentage[(int)termin.Datum.DayOfWeek];
        var zeit = termin.BisZeit.HasValue
            ? $"{termin.VonZeit:HH\\:mm}–{termin.BisZeit.Value:HH\\:mm}"
            : $"{termin.VonZeit:HH\\:mm}";
        var ort = string.IsNullOrWhiteSpace(cp?.City) ? "" : $" · {cp!.City}";
        // Termin-Box im ruhigen Kohlestift-Look: warmes Glas, Charcoal-Text.
        var terminBlock = $@"<div style='background:rgba(255,255,255,0.72);border:1px solid rgba(60,55,48,0.14);border-radius:14px;padding:14px 16px;margin:14px 0;font-size:16px;line-height:1.65;color:#3f3f3f;box-shadow:0 4px 14px rgba(70,64,55,0.08)'>
            📅 <b>{wt}, {termin.Datum:dd.MM.yyyy}</b><br>🕘 {zeit} Uhr<br>📍 {firma}{ort}</div>";

        // Einleitungstext der Link-Seite aus der Moments-Vorlage WILLKOMMENSTAG
        // (Feld «Mitteilung»/BodyText, Walter 12.08.2026) — gestaltbar unter
        // System → Moments-Texte → Willkommenstag-Einladung. Gleiche Platzhalter
        // wie die SMS: {Vorname} {Firma} {Wochentag} {Datum} {Zeit}.
        // Fallback (leer oder noch der Seed-Beschreibungstext) = Standard-Satz.
        var tplBody = await _db.MomentTexts.AsNoTracking()
            .Where(t => t.IsActive && t.MomentType != null && t.MomentType.Code == "WILLKOMMENSTAG")
            .OrderBy(t => t.SortOrder)
            .Select(t => t.BodyText)
            .FirstOrDefaultAsync();
        string? customIntro = null;
        if (!string.IsNullOrWhiteSpace(tplBody)
            && !tplBody.TrimStart().StartsWith("Vorlage für die Willkommenstag-SMS"))
        {
            var filled = tplBody
                .Replace("{Vorname}", k.Vorname ?? "")
                .Replace("{Firma}", firma)
                .Replace("{Wochentag}", wt)
                .Replace("{Datum}", termin.Datum.ToString("dd.MM.yyyy"))
                .Replace("{Zeit}", zeit);
            customIntro = "<p style='margin:0;white-space:pre-line'>" + System.Net.WebUtility.HtmlEncode(filled) + "</p>";
        }
        // Offener Zustand: eigener Text oder Standard-Einladungssatz.
        // Bestätigter Zustand: NUR der eigene Text (der Einladungssatz wäre
        // nach der Bestätigung unpassend), sonst nichts.
        var introHtml = customIntro
            ?? "<p style='margin:0'>Wir laden dich zu deinem <b style='color:#3f3f3f'>Willkommenstag</b> (Onboarding) ein:</p>";
        var introHtmlBestaetigt = customIntro ?? "";

        // Wegbeschreibung (Walter 12.08.2026): Anfahrts-Skizze (Fussweg vom
        // Bahnhof grün, Parkplatz P, Haupteingang) unter den Termin-Details.
        // Statische Datei in wwwroot/img — anonym erreichbar wie die Landing.
        var wegBlock = @"<div style='margin:14px 0 4px'>
            <div style='font-weight:800;font-size:14px;color:#3f3f3f;margin-bottom:8px'>📍 So findest du uns</div>
            <a href='/img/wegbeschreibung-willkommenstag.jpg' target='_blank' style='display:block'>
                <img src='/img/wegbeschreibung-willkommenstag.jpg' alt='Wegbeschreibung'
                     style='width:100%;border-radius:14px;border:1px solid rgba(60,55,48,0.14);box-shadow:0 4px 14px rgba(70,64,55,0.10)'></a>
            <div style='font-size:12px;color:#8b8b8b;margin-top:6px'>Grün = Fussweg vom Bahnhof · P = Parkplatz · Zum Vergrössern antippen</div>
        </div>";

        string inner;
        if (buchung?.MaAntwort == "ANGENOMMEN")
            inner = $@"<h1>Herzlich willkommen bei {firma}!</h1>
                {introHtmlBestaetigt}{terminBlock}
                <div style='background:#dcfce7;border:1px solid #86efac;border-radius:12px;padding:10px 14px;color:#166534;font-weight:600'>✓ Du hast den Termin bestätigt — wir freuen uns auf dich!</div>
                {wegBlock}
                <p style='margin-top:16px'><a href='/willkommen/{token}/kalender.ics' style='display:inline-block;background:#3f3f3f;color:#fff;text-decoration:none;border-radius:12px;padding:11px 20px;font-weight:700;box-shadow:0 4px 14px rgba(60,55,48,0.22)'>In Kalender speichern</a></p>";
        else if (buchung?.MaAntwort == "ABGELEHNT" || buchung?.Status == "ABGESAGT")
            inner = $@"<h1>Willkommenstag abgesagt</h1>{terminBlock}
                <div style='background:#fee2e2;border:1px solid #fca5a5;border-radius:12px;padding:10px 14px;color:#991b1b'>Du hast den Termin abgesagt. Das HR-Team meldet sich bei dir für einen neuen Termin.</div>";
        else if (termin.Datum < DateOnly.FromDateTime(DateTime.Now))
            inner = $@"<h1>Willkommenstag</h1>{terminBlock}<p>Dieser Termin liegt in der Vergangenheit — das HR-Team meldet sich bei dir.</p>";
        else
            inner = $@"<h1>Herzlich willkommen bei {firma}, {System.Net.WebUtility.HtmlEncode(k.Vorname)}!</h1>
                {introHtml}{terminBlock}
                <p style='margin:4px 0 8px;color:#646464;font-size:14px'>Passt dir dieser Termin?</p>
                <div id='tmAsk' style='display:flex;gap:10px;flex-wrap:wrap'>
                    <form method='post' action='/willkommen/{token}/antwort' style='margin:0'>
                        <input type='hidden' name='antwort' value='JA'>
                        <button type='submit' style='background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:12px 22px;font-size:16px;font-weight:700;cursor:pointer;box-shadow:0 4px 14px rgba(60,55,48,0.22)'>✓ Termin annehmen</button>
                    </form>
                    <button type='button'
                            onclick=""document.getElementById('tmAsk').style.display='none';document.getElementById('tmConfirm').style.display='block';""
                            style='background:rgba(255,255,255,0.72);color:#991b1b;border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:12px 22px;font-size:16px;font-weight:600;cursor:pointer'>✕ Termin absagen</button>
                </div>
                <div id='tmConfirm' style='display:none;margin-top:10px;background:rgba(255,255,255,0.72);border:1px solid rgba(60,55,48,0.18);border-radius:14px;padding:14px'>
                    <div style='font-size:15px;font-weight:800;color:#3f3f3f;margin-bottom:6px'>Willkommenstag wirklich absagen?</div>
                    <div style='font-size:13px;color:#646464;margin-bottom:12px'>Das HR-Team meldet sich dann bei dir für einen neuen Termin.</div>
                    <div style='display:flex;gap:10px;flex-wrap:wrap'>
                        <form method='post' action='/willkommen/{token}/antwort' style='margin:0'>
                            <input type='hidden' name='antwort' value='NEIN'>
                            <button type='submit' style='background:#991b1b;color:#fff;border:none;border-radius:12px;padding:10px 20px;font-size:14.5px;font-weight:700;cursor:pointer'>Ja, absagen</button>
                        </form>
                        <button type='button'
                                onclick=""document.getElementById('tmConfirm').style.display='none';document.getElementById('tmAsk').style.display='flex';""
                                style='background:#3f3f3f;color:#fff;border:none;border-radius:12px;padding:10px 20px;font-size:14.5px;font-weight:600;cursor:pointer'>Nein</button>
                    </div>
                </div>
                {wegBlock}
                <p style='margin-top:16px'><a href='/willkommen/{token}/kalender.ics' style='display:inline-block;background:rgba(255,255,255,0.72);color:#3f3f3f;text-decoration:none;border:1px solid rgba(60,55,48,0.18);border-radius:12px;padding:10px 18px;font-weight:600;font-size:14px'>In Kalender speichern</a></p>";

        return Content(WkHtml("Dein Willkommenstag", inner), "text/html; charset=utf-8");
    }

    [AllowAnonymous]
    [HttpPost("/willkommen/{token}/antwort")]
    public async Task<IActionResult> WillkommenAntwort(string token, [FromForm] string? antwort)
    {
        var hash = WkHash(token);
        var k = await _db.Kandidaten.AsNoTracking().FirstOrDefaultAsync(x => x.WillkommenTokenHash == hash);
        if (k == null) return NotFound("Dieser Einladungs-Link ist nicht mehr gültig.");
        var buchung = await _db.HrInterviewBuchungen
            .Where(b => b.KandidatId == k.Id)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync();
        var termin = buchung == null ? null
            : await _db.HrInterviewTermine.AsNoTracking().FirstOrDefaultAsync(t => t.Id == buchung.TerminId);
        if (buchung == null || termin == null
            || buchung.MaAntwort != null || buchung.Status != "GEPLANT"
            || termin.Datum < DateOnly.FromDateTime(DateTime.Now))
            return Redirect($"/willkommen/{token}");

        bool ja = string.Equals(antwort, "JA", StringComparison.OrdinalIgnoreCase);
        buchung.MaAntwort = ja ? "ANGENOMMEN" : "ABGELEHNT";
        buchung.MaAntwortAm = DateTime.Now;
        if (!ja) buchung.Status = "ABGESAGT"; // Platz wird frei
        await _db.SaveChangesAsync();

        // Mitteilung ins HR-Postfach (best-effort NACH dem Commit).
        try
        {
            var zeit = termin.BisZeit.HasValue
                ? $"{termin.VonZeit:HH\\:mm}–{termin.BisZeit.Value:HH\\:mm}"
                : $"{termin.VonZeit:HH\\:mm}";
            var text = ja
                ? $"Kandidat/in {buchung.Kandidat} hat den Willkommenstag {termin.Datum:dd.MM.yyyy} · {zeit} Uhr BESTÄTIGT — fix gebucht."
                : $"Kandidat/in {buchung.Kandidat} hat den Willkommenstag {termin.Datum:dd.MM.yyyy} · {zeit} Uhr ABGESAGT — bitte telefonisch einen neuen Termin vereinbaren (der Platz ist wieder frei).";
            _db.MailboxDocuments.Add(new MailboxDocument
            {
                CompanyProfileId = k.CompanyProfileId,
                UploadedBy = null,
                UploadedAt = DateTime.Now,
                OriginalFilename = $"Willkommenstag {(ja ? "bestätigt" : "abgesagt")} — {buchung.Kandidat}",
                StorageFilename = $"msg-{Guid.NewGuid():N}",
                MimeType = null,
                FileSizeBytes = null,
                Bemerkung = "Willkommenstag-Antwort",
                MessageBody = text,
                EmployeeId = null,
                NotifyUserId = null,
                TargetType = "HR",
            });
            await _db.SaveChangesAsync();
        }
        catch { /* best-effort */ }

        return Redirect($"/willkommen/{token}");
    }

    [AllowAnonymous]
    [HttpGet("/willkommen/{token}/kalender.ics")]
    public async Task<IActionResult> WillkommenKalender(string token)
    {
        var hash = WkHash(token);
        var k = await _db.Kandidaten.AsNoTracking().FirstOrDefaultAsync(x => x.WillkommenTokenHash == hash);
        if (k == null) return NotFound();
        var buchung = await _db.HrInterviewBuchungen.AsNoTracking()
            .Where(b => b.KandidatId == k.Id)
            .OrderByDescending(b => b.CreatedAt)
            .FirstOrDefaultAsync();
        var terminId = buchung?.TerminId ?? k.WunschTerminId;
        var termin = terminId == null ? null
            : await _db.HrInterviewTermine.AsNoTracking().FirstOrDefaultAsync(t => t.Id == terminId.Value);
        if (termin == null) return NotFound();
        var cp = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == k.CompanyProfileId);
        var firma = cp?.FullDisplayName ?? "McDonald's";
        var ort = cp?.City ?? "";

        var start = termin.Datum.ToDateTime(termin.VonZeit);
        var ende = termin.Datum.ToDateTime(termin.BisZeit ?? termin.VonZeit.AddHours(1));
        string Ics(DateTime d) => d.ToString("yyyyMMdd'T'HHmmss");
        var ics = string.Join("\r\n", new[]
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//OneCrew//Willkommenstag//DE",
            "BEGIN:VEVENT",
            $"UID:willkommen-{k.Id}@onecrew.ch",
            $"DTSTAMP:{DateTime.UtcNow:yyyyMMdd'T'HHmmss'Z'}",
            $"DTSTART;TZID=Europe/Zurich:{Ics(start)}",
            $"DTEND;TZID=Europe/Zurich:{Ics(ende)}",
            $"SUMMARY:Willkommenstag {firma}",
            $"LOCATION:{firma}{(string.IsNullOrWhiteSpace(ort) ? "" : ", " + ort)}",
            "DESCRIPTION:Dein Willkommenstag (Onboarding). Bitte pünktlich erscheinen — wir freuen uns auf dich!",
            "END:VEVENT",
            "END:VCALENDAR",
        });
        return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", "Willkommenstag.ics");
    }

    // ══ Onboarding-Übersicht pro Tag + Abschluss (Walter 11.08.2026) ═══════
    // Die Kandidaten/MA werden pro Onboarding-Tag gruppiert angezeigt; nach
    // dem Tag bestätigt HR pro Person «Onboarding abgeschlossen» — der GF
    // bekommt die Meldung ins Filial-Postfach, die Person läuft danach als
    // normaler MA weiter.

    [HttpGet("onboarding-tage")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> OnboardingTage()
    {
        var von = DateOnly.FromDateTime(DateTime.Now).AddDays(-30);
        var termine = await _db.HrInterviewTermine.AsNoTracking()
            .Where(t => t.Datum >= von)
            .OrderBy(t => t.Datum).ThenBy(t => t.VonZeit)
            .ToListAsync();
        var tIds = termine.Select(t => t.Id).ToList();
        var buchungen = await _db.HrInterviewBuchungen.AsNoTracking()
            .Where(b => tIds.Contains(b.TerminId) && b.Status == "GEPLANT")
            .OrderBy(b => b.Kandidat)
            .ToListAsync();

        // Kandidaten-Zusatzinfos (SMS-Zeitpunkt, Status, Filiale).
        var kandIds = buchungen.Where(b => b.KandidatId != null).Select(b => b.KandidatId!.Value).Distinct().ToList();
        var kandidaten = await _db.Kandidaten.AsNoTracking()
            .Where(k => kandIds.Contains(k.Id) || (k.Status == "ANGENOMMEN" && k.WunschTerminId != null))
            .ToListAsync();
        // Filialen der MA-Zeilen (neuester Vertrag).
        var empIds = buchungen.Where(b => b.EmployeeId != null).Select(b => b.EmployeeId!.Value).Distinct().ToList();
        var empCps = (await _db.Employments.AsNoTracking()
                .Where(em => empIds.Contains(em.EmployeeId) && em.CompanyProfileId != null)
                .Select(em => new { em.EmployeeId, em.CompanyProfileId, em.ContractStartDate })
                .ToListAsync())
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ContractStartDate).First().CompanyProfileId);
        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.WorkLocation, c.City, c.BranchName })
            .ToListAsync();
        string BranchName(int? cpId)
        {
            var b = branches.FirstOrDefault(x => x.Id == cpId);
            return b == null ? "" : (!string.IsNullOrWhiteSpace(b.WorkLocation) ? b.WorkLocation! : (b.City ?? b.BranchName ?? ""));
        }

        var result = termine.Select(t =>
        {
            var rows = new List<object>();
            foreach (var b in buchungen.Where(x => x.TerminId == t.Id))
            {
                var k = b.KandidatId == null ? null : kandidaten.FirstOrDefault(x => x.Id == b.KandidatId.Value);
                var cpId = k?.CompanyProfileId ?? (b.EmployeeId != null && empCps.TryGetValue(b.EmployeeId.Value, out var c) ? c : null);
                rows.Add(new
                {
                    buchungId = b.Id,
                    kandidatId = b.KandidatId,
                    employeeId = b.EmployeeId,
                    name = b.Kandidat,
                    telefon = b.Telefon,
                    filiale = BranchName(cpId),
                    b.MaAntwort,
                    verknuepft = b.EmployeeId != null,
                    willkommenGesendetAm = k?.WillkommenGesendetAm?.ToString("yyyy-MM-dd HH:mm"),
                    abgeschlossenAm = b.OnboardingAbgeschlossenAm?.ToString("yyyy-MM-dd HH:mm"),
                    abgeschlossenVon = b.OnboardingAbgeschlossenVon,
                });
            }
            // Angenommene Kandidaten mit diesem Wunschtermin, aber noch OHNE
            // Buchung (SMS noch nicht gesendet) — als «SMS offen»-Zeile zeigen.
            foreach (var k in kandidaten.Where(x => x.Status == "ANGENOMMEN" && x.WunschTerminId == t.Id
                                                    && !buchungen.Any(b => b.KandidatId == x.Id)))
            {
                rows.Add(new
                {
                    buchungId = (int?)null,
                    kandidatId = (int?)k.Id,
                    employeeId = (int?)null,
                    name = $"{k.Vorname} {k.Name}".Trim(),
                    telefon = k.Telefon,
                    filiale = BranchName(k.CompanyProfileId),
                    MaAntwort = (string?)null,
                    verknuepft = false,
                    willkommenGesendetAm = (string?)null,
                    abgeschlossenAm = (string?)null,
                    abgeschlossenVon = (string?)null,
                });
            }
            return new
            {
                t.Id,
                datum = t.Datum.ToString("yyyy-MM-dd"),
                von = t.VonZeit.ToString("HH:mm"),
                bis = t.BisZeit?.ToString("HH:mm"),
                t.Plaetze,
                t.Bemerkung,
                belegt = buchungen.Count(x => x.TerminId == t.Id),
                vergangen = t.Datum <= DateOnly.FromDateTime(DateTime.Now),
                rows,
            };
        })
        .Where(x => x.rows.Count > 0 || !x.vergangen)
        .ToList();
        return Ok(result);
    }

    [HttpPost("onboarding-abschliessen/{buchungId:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> OnboardingAbschliessen(int buchungId)
    {
        var b = await _db.HrInterviewBuchungen.FirstOrDefaultAsync(x => x.Id == buchungId);
        if (b == null) return NotFound();
        if (b.OnboardingAbgeschlossenAm != null)
            return Conflict(new { error = "BEREITS_ABGESCHLOSSEN" });
        var termin = await _db.HrInterviewTermine.AsNoTracking().FirstOrDefaultAsync(t => t.Id == b.TerminId);
        if (termin == null) return NotFound(new { error = "TERMIN_FEHLT" });
        if (termin.Datum > DateOnly.FromDateTime(DateTime.Now))
            return Conflict(new { error = "TERMIN_ZUKUNFT", message = "Der Willkommenstag liegt noch in der Zukunft — Abschluss erst danach." });

        var actor = await ActorNameAsync();
        b.OnboardingAbgeschlossenAm = DateTime.Now;
        b.OnboardingAbgeschlossenVon = actor;

        // Filiale bestimmen: Kandidat → dessen Filiale; sonst neuester Vertrag des MA.
        int? cpId = null;
        if (b.KandidatId != null)
            cpId = await _db.Kandidaten.AsNoTracking()
                .Where(k => k.Id == b.KandidatId.Value).Select(k => (int?)k.CompanyProfileId).FirstOrDefaultAsync();
        if (cpId == null && b.EmployeeId != null)
            cpId = await _db.Employments.AsNoTracking()
                .Where(em => em.EmployeeId == b.EmployeeId.Value && em.CompanyProfileId != null)
                .OrderByDescending(em => em.ContractStartDate)
                .Select(em => em.CompanyProfileId).FirstOrDefaultAsync();
        cpId ??= await _db.CompanyProfiles.AsNoTracking().OrderBy(c => c.Id).Select(c => c.Id).FirstAsync();
        await _db.SaveChangesAsync();

        // Meldung an den GF ins Filial-Postfach (best-effort NACH dem Commit).
        try
        {
            _db.MailboxDocuments.Add(new MailboxDocument
            {
                CompanyProfileId = cpId.Value,
                UploadedBy = UserId(),
                UploadedAt = DateTime.Now,
                OriginalFilename = $"Onboarding abgeschlossen — {b.Kandidat}",
                StorageFilename = $"msg-{Guid.NewGuid():N}",
                MimeType = null,
                FileSizeBytes = null,
                Bemerkung = "Onboarding-Abschluss",
                MessageBody = $"Onboarding abgeschlossen: {b.Kandidat} hat den Willkommenstag vom {termin.Datum:dd.MM.yyyy} absolviert. Der/die Mitarbeitende läuft ab jetzt regulär.",
                EmployeeId = null,
                NotifyUserId = null,
                TargetType = "BRANCH",
            });
            await _db.SaveChangesAsync();
        }
        catch { /* best-effort */ }

        return Ok(new { ok = true });
    }

    /// <summary>
    /// Kandidaten-Daten sofort löschen (Walter 11.08.2026) — z.B. Test-
    /// Einträge, statt auf die 30-Tage-Routine zu warten. Nur für bereits
    /// abgeschlossene Kandidaturen (ERLEDIGT/ABGELEHNT); Buchungen bleiben
    /// bestehen (Kandidaten-Bezug wird gelöst, MA-Bezug bleibt).
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Delete(int id)
    {
        var k = await _db.Kandidaten.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        if (k.Status == "NEU" || k.Status == "ANGENOMMEN")
            return Conflict(new { error = "NOCH_AKTIV", message = "Aktive Kandidaturen zuerst ablehnen oder verknüpfen — erst danach löschen." });

        await _db.HrInterviewBuchungen
            .Where(b => b.KandidatId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.KandidatId, (int?)null));
        await _db.KandidatDokumente.Where(d => d.KandidatId == id).ExecuteDeleteAsync();
        _db.Kandidaten.Remove(k);
        await _db.SaveChangesAsync();
        try { var dir = Path.Combine(_storageRoot, id.ToString()); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch { /* best-effort */ }
        return Ok(new { ok = true });
    }

    public class NotizDto
    {
        public string? Notiz { get; set; }
    }

    /// <summary>Freie HR-Notiz am Kandidaten (z.B. «hat sich nach Absage nochmals gemeldet»).</summary>
    [HttpPost("{id:int}/notiz")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> SetNotiz(int id, [FromBody] NotizDto dto)
    {
        var k = await _db.Kandidaten.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return NotFound();
        k.Notiz = string.IsNullOrWhiteSpace(dto.Notiz) ? null : dto.Notiz.Trim();
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
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
