using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Bewilligungs-Verlauf pro Mitarbeiter.
///
/// Routen:
///   GET    /api/employees/{employeeId}/permit-history
///   POST   /api/employees/{employeeId}/permit-history
///   PUT    /api/employees/{employeeId}/permit-history/{id}
///   DELETE /api/employees/{employeeId}/permit-history/{id}
///
/// Auto-Sync: nach jedem Schreibvorgang wird der "aktuelle" Eintrag
/// (valid_from &lt;= heute, valid_to NULL oder &gt;= heute, höchstes
/// valid_from) ermittelt und auf employee.permit_type_id +
/// employee.permit_expiry_date geschrieben. Wenn aktuell kein gültiger
/// Eintrag existiert, werden beide Felder auf NULL gesetzt.
///
/// Beim Anlegen eines neuen Eintrags wird der vorherige offene Eintrag
/// automatisch geschlossen (valid_to = neuer.valid_from - 1 Tag).
/// </summary>
[Authorize]
[ApiController]
[Route("api/employees/{employeeId:int}/permit-history")]
public class EmployeePermitHistoryController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    private readonly EcallSmsService     _sms;

    // Kurz-SMS (≤ 160 Zeichen) — lange Mitteilung liegt auf der Link-Seite
    // (Walter 19.07.2026, analog Moments/Gratulation).
    private const string DefaultPermitExpiredSms =
        "Hallo {Vorname}, deine Bewilligung ist abgelaufen. Tippe auf den Link:";
    private const string DefaultPermitExpiredBody =
        "{Briefanrede}\n\ndeine Bewilligung ({PermitCode}) ist am {GueltigBis} abgelaufen. Kannst du bitte die neue Bewilligung so bald wie möglich bei HR nachreichen?\n\nDanke und freundliche Grüsse\n{SenderName}";
    private const int SmsMaxChars = 160;
    private const int LinkExpiryDays = 14;

    public EmployeePermitHistoryController(AppDbContext db, LohnEditLockService editLock, EcallSmsService sms)
    {
        _db = db; _editLock = editLock; _sms = sms;
    }

    private Task<int?> GetEmployeeBranchAsync(int employeeId)
        => _db.Employees
            .Where(e => e.Id == employeeId)
            .SelectMany(e => e.Employments)
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.ContractStartDate)
            .Select(x => (int?)x.CompanyProfileId)
            .FirstOrDefaultAsync();

    public class PermitHistoryDto
    {
        public int     Id { get; set; }
        public int     EmployeeId { get; set; }
        public int?    PermitTypeId { get; set; }
        public string? PermitCode { get; set; }
        public string? PermitDescription { get; set; }
        public DateOnly  ValidFrom { get; set; }
        public DateOnly? ValidTo   { get; set; }   // = behördliches Ablauf-Datum auf dem Ausweis
        public string?   Note { get; set; }
        // Walter 14.06.2026: Verknüpftes Bewilligungs-PDF.
        public int?      DokumentId { get; set; }
        public string?   DokumentName { get; set; }
        public DateTime CreatedAt { get; set; }
        public int?     CreatedByUserId { get; set; }
        public bool     IsCurrent { get; set; }   // heute gültig (from ≤ heute ≤ to/offen)
        public bool     InLohnVerwendet { get; set; }
    }

    public class PermitHistoryUpsertDto
    {
        public int?    PermitTypeId { get; set; }       // NULL = Einbürgerung / keine Bewilligung mehr
        public DateOnly  ValidFrom { get; set; }
        public DateOnly? ValidTo   { get; set; }       // Pflicht bei PermitTypeId != NULL
        public string?   Note { get; set; }
        // Walter 14.06.2026: optional bei POST/PUT auch das verknüpfte Doku mit setzen.
        public int?      DokumentId { get; set; }
    }

    /// <summary>Walter 14.06.2026: Patch-DTO nur für die Doku-Verknüpfung.</summary>
    public class PermitDokumentPatchDto
    {
        public int? DokumentId { get; set; }   // null = Verknüpfung lösen
    }

    /// <summary>
    /// Liefert die IDs aller Mitarbeiter mit MINDESTENS einem Permit-History-
    /// Eintrag (egal ob aktuell gültig oder abgelaufen). Frontend nutzt das
    /// für den Spezialfilter „Keine Bewilligung" → das Komplement.
    /// Walter-Vorgabe 18.05.2026 — analog /api/employee-bank-accounts/active-employee-ids.
    /// </summary>
    [HttpGet("/api/employee-permit-history/employee-ids-with-history")]
    public async Task<IActionResult> GetEmployeeIdsWithHistory()
    {
        var ids = await _db.EmployeePermitHistories
            .Select(h => h.EmployeeId)
            .Distinct()
            .ToListAsync();
        return Ok(ids);
    }

    /// <summary>
    /// MA-IDs deren massgebende Bewilligung abgelaufen ist (ValidTo &lt; heute).
    /// Auswahl pro MA wie Dashboard (Walter-Bug 15.07.2026): heute gültige
    /// gewinnt, sonst spätestes Ende. Frontend-Filter «Bewilligung abgelaufen»
    /// — die Spalte employee.permit_expiry_date gibt es seit 01.06.2026 nicht mehr
    /// (Walter-Bug 18.07.2026: Filter fand niemand).
    /// </summary>
    [HttpGet("/api/employee-permit-history/employee-ids-with-expired")]
    public async Task<IActionResult> GetEmployeeIdsWithExpiredPermit()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var histories = await _db.EmployeePermitHistories
            .AsNoTracking()
            .Where(h => h.PermitTypeId != null)
            .Select(h => new { h.Id, h.EmployeeId, h.ValidFrom, h.ValidTo })
            .ToListAsync();

        var expiredIds = histories
            .GroupBy(h => h.EmployeeId)
            .Select(g =>
            {
                var pool = g.Where(x => !x.ValidTo.HasValue || x.ValidTo.Value >= x.ValidFrom).ToList();
                if (pool.Count == 0) pool = g.ToList();
                return pool
                    .OrderByDescending(x => (x.ValidFrom <= today
                        && (!x.ValidTo.HasValue || x.ValidTo.Value >= today)) ? 1 : 0)
                    .ThenByDescending(x => x.ValidTo ?? DateOnly.MaxValue)
                    .ThenByDescending(x => x.Id)
                    .First();
            })
            .Where(h => h.ValidTo.HasValue && h.ValidTo.Value < today)
            .Select(h => h.EmployeeId)
            .Distinct()
            .ToList();

        return Ok(expiredIds);
    }

    [HttpGet]
    public async Task<IActionResult> List(int employeeId)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        var entries = await _db.EmployeePermitHistories
            .AsNoTracking()
            .Include(h => h.PermitType)
            .Where(h => h.EmployeeId == employeeId)
            .OrderByDescending(h => h.ValidFrom)
            .ThenByDescending(h => h.Id)
            .ToListAsync();

        // Walter 14.06.2026: Doku-Namen pro Permit-Eintrag (für Anzeige
        // im UI: „📎 Pass-Skarcheska.pdf" statt nur ID). Wir holen einmal
        // alle referenzierten Dokumente in einem Batch.
        var dokIds = entries.Where(h => h.DokumentId.HasValue)
                            .Select(h => h.DokumentId!.Value)
                            .Distinct().ToList();
        var dokNames = dokIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.EmployeeDokumente
                .AsNoTracking()
                .Where(d => dokIds.Contains(d.Id))
                .Select(d => new { d.Id, d.FilenameOriginal })
                .ToDictionaryAsync(d => d.Id, d => d.FilenameOriginal ?? "");

        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;

        // «AKTUELL» = heute gültig (ValidFrom ≤ heute ≤ ValidTo/offen).
        // Abgelaufene Einträge dürfen die Pille NICHT tragen — sonst steht
        // z.B. «30.6.2026» + AKTUELL obwohl Dashboard schon «seit N Tagen
        // abgelaufen» meldet (Walter-Bug 18.07.2026, Monika Tomikj).
        var today = DateOnly.FromDateTime(DateTime.Now);
        var max   = new DateOnly(9999, 12, 31);
        var currentId = entries
            .Where(h => h.ValidFrom <= today
                     && (!h.ValidTo.HasValue || h.ValidTo.Value >= today))
            .OrderByDescending(h => h.ValidTo ?? max)
            .ThenBy(h => h.ValidFrom)
            .ThenBy(h => h.Id)
            .Select(h => (int?)h.Id)
            .FirstOrDefault();

        var dtos = entries.Select(h => new PermitHistoryDto
        {
            Id                = h.Id,
            EmployeeId        = h.EmployeeId,
            PermitTypeId      = h.PermitTypeId,
            PermitCode        = h.PermitType?.Code,
            PermitDescription = h.PermitType?.Description,
            ValidFrom         = h.ValidFrom,
            ValidTo           = h.ValidTo,
            Note              = h.Note,
            DokumentId        = h.DokumentId,
            DokumentName      = h.DokumentId.HasValue
                                  && dokNames.TryGetValue(h.DokumentId.Value, out var nm)
                                  ? nm : null,
            CreatedAt         = h.CreatedAt,
            CreatedByUserId   = h.CreatedByUserId,
            IsCurrent         = h.Id == currentId,
            InLohnVerwendet   = firstAllowed.HasValue && h.ValidFrom < firstAllowed.Value
        }).ToList();

        return Ok(dtos);
    }

    [HttpPost]
    [Authorize(Roles = "admin,superuser,user")]
    public async Task<IActionResult> Create(int employeeId, [FromBody] PermitHistoryUpsertDto dto)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return NotFound();

        if (dto.PermitTypeId.HasValue)
        {
            var pt = await _db.PermitTypes.FirstOrDefaultAsync(p => p.Id == dto.PermitTypeId.Value);
            if (pt == null) return BadRequest(new { error = "Bewilligungstyp nicht gefunden." });
        }

        // Walter-Vorgabe 01.06.2026: ValidTo (= behördliches Ablauf-Datum auf dem Ausweis)
        // ist PFLICHT bei jedem Bewilligungs-Eintrag. Nur Einbürgerungs-Einträge
        // (PermitTypeId IS NULL → kein Ausweis, der ablaufen kann) dürfen NULL haben.
        if (dto.PermitTypeId.HasValue && !dto.ValidTo.HasValue)
            return BadRequest(new { error = "Gültig bis (Ablauf-Datum) ist Pflicht." });
        if (dto.ValidTo.HasValue && dto.ValidTo.Value < dto.ValidFrom)
            return BadRequest(new { error = "Gültig bis darf nicht vor Gültig ab liegen." });

        // Walter 17.05.2026: ValidFrom darf nicht rückwirkend in verarbeitete Periode.
        // AUSNAHME (Walter-Vorgabe 23.08.2026, Fall «neuer Ausweis einlesen»):
        // eine VERLÄNGERUNG derselben Kategorie (z.B. B → B, Ausstellungsdatum
        // liegt in einer verarbeiteten Periode) ist lohn-neutral — weder
        // QST-Pflicht noch irgendeine Lohnrechnung ändern sich dadurch
        // rückwirkend. Der Lock greift nur noch bei KATEGORIE-WECHSEL
        // (z.B. B → C: QST-Pflicht kippt!) oder Einbürgerung (Typ → NULL).
        var vorherigerTypId = await _db.EmployeePermitHistories.AsNoTracking()
            .Where(h => h.EmployeeId == employeeId)
            .OrderByDescending(h => h.ValidFrom)
            .Select(h => h.PermitTypeId)
            .FirstOrDefaultAsync();
        var gleicheKategorie = vorherigerTypId.HasValue
                            && dto.PermitTypeId.HasValue
                            && vorherigerTypId.Value == dto.PermitTypeId.Value;

        var branchId     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value)
            : null;
        if (!gleicheKategorie && firstAllowed.HasValue && dto.ValidFrom < firstAllowed.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"'Gültig ab {dto.ValidFrom:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes 'Gültig ab': {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        }

        // Walter-Vorgabe 07.06.2026 (final): Beim Anlegen einer neuen Bewilligung
        // werden ALLE Vorgänger-Einträge automatisch auf neuValidFrom-1 geschlossen,
        // wenn sie noch in den neuen Zeitraum hineinreichen. Damit gibt es nie
        // Überlappungen (Datensauberkeit). Greift nur für Einträge, deren
        // ValidFrom VOR der neuen ValidFrom liegt — historische Nachträge
        // (älterer Eintrag mit ValidTo vor neuer ValidFrom) bleiben unangetastet.
        var vorgaenger = await _db.EmployeePermitHistories
            .Where(h => h.EmployeeId == employeeId
                     && h.ValidFrom < dto.ValidFrom
                     && (h.ValidTo == null || h.ValidTo >= dto.ValidFrom))
            .ToListAsync();
        foreach (var p in vorgaenger)
        {
            p.ValidTo = dto.ValidFrom.AddDays(-1);
        }

        // Walter 14.06.2026: optional verknüpftes Doku validieren (muss dem MA gehören).
        if (dto.DokumentId.HasValue)
        {
            var dokOk = await _db.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == employeeId);
            if (!dokOk) return BadRequest(new { error = "Verknüpftes Dokument gehört nicht zu diesem MA." });
        }

        var entry = new EmployeePermitHistory
        {
            EmployeeId       = employeeId,
            PermitTypeId     = dto.PermitTypeId,
            ValidFrom        = dto.ValidFrom,
            ValidTo          = dto.ValidTo,
            Note             = dto.Note,
            DokumentId       = dto.DokumentId,
            CreatedAt        = DateTime.UtcNow,
            CreatedByUserId  = GetCurrentUserId()
        };
        _db.EmployeePermitHistories.Add(entry);
        await _db.SaveChangesAsync();

        await SyncEmployeeFromHistoryAsync(employeeId);
        await _db.SaveChangesAsync();

        return Ok(new { id = entry.Id });
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "admin,superuser,user")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] PermitHistoryUpsertDto dto)
    {
        var entry = await _db.EmployeePermitHistories
            .FirstOrDefaultAsync(h => h.Id == id && h.EmployeeId == employeeId);
        if (entry == null) return NotFound();

        // Walter 17.05.2026: bereits in Lohn verwendet → nicht editierbar.
        var branchIdU     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowedU = branchIdU.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchIdU.Value)
            : null;
        if (firstAllowedU.HasValue && entry.ValidFrom < firstAllowedU.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser Bewilligungs-Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet. Bitte einen neuen Eintrag ab frühestens {firstAllowedU:dd.MM.yyyy} anlegen.",
                firstAllowedDate = firstAllowedU?.ToString("yyyy-MM-dd")
            });
        }

        if (dto.PermitTypeId.HasValue)
        {
            var pt = await _db.PermitTypes.FirstOrDefaultAsync(p => p.Id == dto.PermitTypeId.Value);
            if (pt == null) return BadRequest(new { error = "Bewilligungstyp nicht gefunden." });
        }

        // ValidTo-Pflicht (Walter 01.06.2026, siehe Create-Pfad).
        if (dto.PermitTypeId.HasValue && !dto.ValidTo.HasValue)
            return BadRequest(new { error = "Gültig bis (Ablauf-Datum) ist Pflicht." });
        if (dto.ValidTo.HasValue && dto.ValidTo.Value < dto.ValidFrom)
            return BadRequest(new { error = "Gültig bis darf nicht vor Gültig ab liegen." });

        // Walter-Vorgabe 07.06.2026 (final): Beim Bearbeiten darf KEINE
        // Überlappung mit anderen Einträgen entstehen — Datensauberkeit.
        // Beim Anlegen (POST) wird Auto-Close angewandt; beim Editieren
        // erwarten wir, dass Walter die Datumsfenster bewusst sauber hält.
        var overlap = await FindOverlappingAsync(employeeId, dto.ValidFrom, dto.ValidTo, excludeId: entry.Id);
        if (overlap != null)
        {
            return Conflict(new {
                error = "PERMIT_OVERLAP",
                message = $"Die Periode {dto.ValidFrom:dd.MM.yyyy}–{(dto.ValidTo?.ToString("dd.MM.yyyy") ?? "offen")} überschneidet sich mit einer anderen Bewilligung ({overlap.ValidFrom:dd.MM.yyyy}–{(overlap.ValidTo?.ToString("dd.MM.yyyy") ?? "offen")}). Bitte das Bis-Datum des älteren Eintrags vor das Von-Datum der nächsten Bewilligung legen."
            });
        }

        // Walter 14.06.2026: Doku-Verknüpfung optional mit-updaten.
        if (dto.DokumentId.HasValue)
        {
            var dokOk = await _db.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == employeeId);
            if (!dokOk) return BadRequest(new { error = "Verknüpftes Dokument gehört nicht zu diesem MA." });
        }

        entry.PermitTypeId     = dto.PermitTypeId;
        entry.ValidFrom        = dto.ValidFrom;
        entry.ValidTo          = dto.ValidTo;
        entry.Note             = dto.Note;
        entry.DokumentId       = dto.DokumentId;

        await _db.SaveChangesAsync();
        await SyncEmployeeFromHistoryAsync(employeeId);
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Walter-Vorgabe 14.06.2026: NUR die Doku-Verknüpfung patchen. Wird vom
    /// „📎 Doku verknüpfen"-Modal in der Bewilligungs-Liste und im
    /// Aufenthalt-Block der MA-Maske aufgerufen. Body = { dokumentId: int? }
    /// — null bedeutet „Verknüpfung lösen".
    /// </summary>
    // Walter-Vorgabe 13.07.2026: auch GF (user) + Buchhaltung dürfen den
    // Ausweis-Scan verknüpfen — sie pflegen die MA ihrer Filialen. Das
    // ERFASSEN/ÄNDERN/LÖSCHEN der Bewilligungs-Einträge selbst bleibt
    // admin/superuser (QST-relevant).
    [HttpPatch("{id:int}/dokument")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> PatchDokument(int employeeId, int id, [FromBody] PermitDokumentPatchDto dto)
    {
        var entry = await _db.EmployeePermitHistories
            .FirstOrDefaultAsync(h => h.Id == id && h.EmployeeId == employeeId);
        if (entry == null) return NotFound();

        if (dto.DokumentId.HasValue)
        {
            var dokOk = await _db.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == employeeId);
            if (!dokOk) return BadRequest(new { error = "Verknüpftes Dokument gehört nicht zu diesem MA." });
        }

        entry.DokumentId = dto.DokumentId;
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>Sucht einen anderen Bewilligungs-Eintrag des MA, dessen
    /// Zeitfenster sich mit [newFrom..newTo (oder offen)] überschneidet.
    /// Liefert null wenn kein Konflikt.</summary>
    private async Task<EmployeePermitHistory?> FindOverlappingAsync(
        int employeeId, DateOnly newFrom, DateOnly? newTo, int? excludeId)
    {
        // Zwei Intervalle [a1..a2] und [b1..b2] überlappen ⇔ a1 ≤ b2 && b1 ≤ a2.
        // Wir nutzen MaxValue für „offen". DateOnly hat keinen MaxValue → wir
        // verwenden 9999-12-31 als Surrogat.
        var max = new DateOnly(9999, 12, 31);
        var newToEff = newTo ?? max;
        var others = await _db.EmployeePermitHistories
            .Where(h => h.EmployeeId == employeeId
                     && (excludeId == null || h.Id != excludeId.Value))
            .ToListAsync();
        foreach (var o in others)
        {
            var oTo = o.ValidTo ?? max;
            if (newFrom <= oTo && o.ValidFrom <= newToEff)
                return o;
        }
        return null;
    }

    // ── SMS-Erinnerung bei abgelaufener Bewilligung (Walter 19.07.2026) ──
    // Kurz-SMS (≤ 160) + Token-Link zur langen Mitteilung — analog Moments/
    // Gratulation. Vorlage BEWILLIGUNG_ABGELAUFEN: SmsText = Push, BodyText =
    // Landing-Page. Kein Lohn-Edit — EditLock greift hier nicht.
    /// <summary>
    /// Filial-Zugriffs-Check fuer die SMS-Endpoints (Walter 22.07.2026,
    /// Review-Fix): admin/reiner superuser frei; buchhaltung (Doppel-Claim
    /// ZUERST pruefen) und user (GF) nur auf user_branch_access-Filialen.
    /// Loest echte SMS-Kosten + PII-Link aus — darum hart geprueft.
    /// </summary>
    private async Task<IActionResult?> GuardBranchAsync(int employeeId)
    {
        if (User.IsInRole("admin")) return null;
        var restricted = User.IsInRole("buchhaltung") || !User.IsInRole("superuser");
        if (!restricted) return null;

        var cpId = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == employeeId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (cpId == null)
            return StatusCode(403, new { error = "BRANCH_REQUIRED",
                message = "Dieser Mitarbeiter hat keine Filial-Zuordnung — Versand nur für Admin/HR." });

        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(idStr, out var uid))
            return StatusCode(403, new { error = "NO_USER" });
        var ok = await _db.UserBranchAccesses
            .AnyAsync(a => a.UserId == uid && a.CompanyProfileId == cpId.Value);
        if (!ok)
            return StatusCode(403, new { error = "BRANCH_FORBIDDEN",
                message = "Kein Zugriff auf die Filiale dieses Mitarbeiters." });
        return null;
    }

    [HttpGet("{id:int}/sms-preview")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> SmsPreview(int employeeId, int id)
    {
        var guard = await GuardBranchAsync(employeeId);
        if (guard != null) return guard;
        var built = await BuildPermitExpiredSmsAsync(employeeId, id);
        if (built.Error != null) return built.Error;

        var lastSms = await _db.SmsLogs.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId && l.Purpose == "BEWILLIGUNG" && l.Ok)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (DateTime?)l.CreatedAt)
            .FirstOrDefaultAsync();

        return Ok(new
        {
            ok = true,
            phone = built.Phone,
            smsText = built.SmsText,
            bodyPreview = built.BodyPlain,
            smsChars = built.SmsText?.Length ?? 0,
            smsMaxChars = SmsMaxChars,
            permitCode = built.PermitCode,
            validTo = built.ValidTo?.ToString("yyyy-MM-dd"),
            lastSmsSentAt = lastSms,
            hint = "Die SMS enthält nur den Kurztext; die ausführliche Mitteilung öffnet der MA über den angehängten Link.",
        });
    }

    [HttpPost("{id:int}/send-sms")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> SendSms(int employeeId, int id)
    {
        var guard = await GuardBranchAsync(employeeId);
        if (guard != null) return guard;
        var built = await BuildPermitExpiredSmsAsync(employeeId, id);
        if (built.Error != null) return built.Error;

        var (token, tokenHash) = NewToken();
        var expiresAt = DateTime.Now.AddDays(LinkExpiryDays);

        // Alte aktive Links derselben Bewilligung entwerten.
        var now = DateTime.Now;
        await _db.PermitReminderTokens
            .Where(t => t.PermitHistoryId == id && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));

        _db.PermitReminderTokens.Add(new PermitReminderToken
        {
            EmployeeId = employeeId,
            PermitHistoryId = id,
            TokenHash = tokenHash,
            MessageHtml = built.BodyHtml!,
            Title = built.Title,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.Now,
            CreatedBy = GetCurrentUserId(),
        });
        await _db.SaveChangesAsync();

        var siteRow = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync();
        var baseUrl = (siteRow != null && !string.IsNullOrWhiteSpace(siteRow.SiteUrl))
            ? siteRow.SiteUrl.Trim()
            : "https://onecrew.ch/";
        var url = $"{baseUrl.TrimEnd('/')}/bewilligung/{token}";

        var smsBody = built.SmsText!.Contains("{Link}")
            ? built.SmsText.Replace("{Link}", url)
            : $"{built.SmsText}\n{url}";

        var res = await _sms.SendSmsAsync(built.Phone!, smsBody, Services.VersandKategorie.Bewilligung, employeeId: employeeId);
        if (!res.Ok)
            return StatusCode(502, new { error = $"SMS-Versand fehlgeschlagen: {res.Error}" });

        // Umleitung kommt aus dem Versand-Ergebnis, NICHT mehr aus den
        // Einstellungen: die Test-Nummer steht dauerhaft drin (Walter 01.09.2026).
        return Ok(new
        {
            ok = true,
            to = built.Phone,
            redirectedTo = res.RedirectedTo,
            messageId = res.MessageId,
            url,
            expiresAt,
        });
    }

    // Öffentliche Landing-Page (kurze SMS → lange Mitteilung).
    [AllowAnonymous]
    [HttpGet("/bewilligung/{token}")]
    public async Task<IActionResult> PublicLanding(string token)
    {
        var hash = HashToken(token);
        var t = await _db.PermitReminderTokens.FirstOrDefaultAsync(x => x.TokenHash == hash);
        string html;
        if (t == null)
            html = ReminderLandingHtml("Link nicht gefunden", "Dieser Link ist ungültig.", null);
        else if (t.RevokedAt != null)
            html = ReminderLandingHtml("Link nicht mehr gültig", "Dieser Link wurde ersetzt oder zurückgezogen. Bitte fordere einen neuen an.", null);
        else if (t.ExpiresAt < DateTime.Now)
            html = ReminderLandingHtml("Link abgelaufen", "Dieser Link ist abgelaufen. Bitte fordere einen neuen an.", null);
        else
        {
            if (t.OpenedAt == null)
            {
                t.OpenedAt = DateTime.Now;
                try { await _db.SaveChangesAsync(); } catch { /* best-effort */ }
            }
            var title = string.IsNullOrWhiteSpace(t.Title) ? "Bewilligung" : t.Title!;
            html = ReminderLandingHtml(title, t.MessageHtml, t.ExpiresAt.ToString("dd.MM.yyyy"));
        }
        return Content(html, "text/html; charset=utf-8");
    }

    private sealed class PermitSmsBuild
    {
        public IActionResult? Error { get; init; }
        public string? Phone { get; init; }
        public string? SmsText { get; init; }
        public string? BodyHtml { get; init; }
        public string? BodyPlain { get; init; }
        public string? Title { get; init; }
        public string? PermitCode { get; init; }
        public DateOnly? ValidTo { get; init; }
    }

    private async Task<PermitSmsBuild> BuildPermitExpiredSmsAsync(int employeeId, int id)
    {
        var emp = await _db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null)
            return new PermitSmsBuild { Error = NotFound(new { error = "Mitarbeiter nicht gefunden." }) };

        var entry = await _db.EmployeePermitHistories.AsNoTracking()
            .Include(h => h.PermitType)
            .FirstOrDefaultAsync(h => h.Id == id && h.EmployeeId == employeeId);
        if (entry == null)
            return new PermitSmsBuild { Error = NotFound(new { error = "Bewilligungs-Eintrag nicht gefunden." }) };

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (entry.ValidTo == null || entry.ValidTo >= today)
            return new PermitSmsBuild { Error = Conflict(new { error = "BEWILLIGUNG_NICHT_ABGELAUFEN", message = "SMS nur bei abgelaufener Bewilligung möglich." }) };

        var phone = (emp.PhoneMobile ?? "").Trim();
        if (phone.Length == 0)
            return new PermitSmsBuild { Error = BadRequest(new { error = "Für diesen Mitarbeitenden ist keine Handynummer hinterlegt." }) };

        var vorname = (emp.FirstName ?? "").Trim();
        var code = (entry.PermitType?.Code ?? "").Trim();
        if (code.Length == 0) code = "Bewilligung";
        var gueltigBis = entry.ValidTo!.Value.ToString("dd.MM.yyyy");
        var briefanrede = !string.IsNullOrWhiteSpace(emp.LetterSalutation)
            ? emp.LetterSalutation!.Trim()
            : (vorname.Length > 0 ? $"Hallo {vorname}" : "Hallo");

        // {SenderName} = nur Vorname (Du-Ton / Moments-Konvention).
        var senderName = "";
        var uid = GetCurrentUserId();
        if (uid.HasValue)
        {
            var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid.Value);
            senderName = (u?.FirstName ?? "").Trim();
        }

        var tpl = await _db.MomentTexts
            .Include(t => t.MomentType)
            .Where(t => t.IsActive && t.MomentType != null && t.MomentType.Code == "BEWILLIGUNG_ABGELAUFEN")
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .FirstOrDefaultAsync();

        var smsTpl = (tpl != null && !string.IsNullOrWhiteSpace(tpl.SmsText))
            ? tpl.SmsText! : DefaultPermitExpiredSms;
        var bodyTpl = (tpl != null && !string.IsNullOrWhiteSpace(tpl.BodyText)
                       && !tpl.BodyText.Contains("SMS-Vorlage bei abgelaufener", StringComparison.Ordinal))
            ? tpl.BodyText! : DefaultPermitExpiredBody;
        var title = !string.IsNullOrWhiteSpace(tpl?.Titel) ? tpl!.Titel!.Trim() : "Bewilligung abgelaufen";

        string Resolve(string s) => s
            .Replace("{Vorname}", vorname)
            .Replace("{PermitCode}", code)
            .Replace("{GueltigBis}", gueltigBis)
            .Replace("{Briefanrede}", briefanrede)
            .Replace("{SenderName}", senderName)
            .Replace("{Absender}", senderName);

        // SMS-Kurztext: {Link} zählt nicht zur 160-Grenze (wird erst beim Senden gesetzt).
        var smsRaw = Resolve(smsTpl).Replace("{Link}", "").Trim();
        if (smsRaw.Length == 0)
            return new PermitSmsBuild { Error = BadRequest(new { error = "SMS-Kurztext der Vorlage ist leer." }) };
        if (smsRaw.Length > SmsMaxChars)
            return new PermitSmsBuild { Error = Conflict(new {
                error = "SMS_TOO_LONG",
                message = $"Der SMS-Kurztext ist {smsRaw.Length} Zeichen lang (max. {SmsMaxChars}). Bitte unter Systemeinstellungen → Moments-Texte kürzen — die ausführliche Mitteilung gehört in das Feld «Mitteilung»."
            }) };

        var bodyPlain = Resolve(bodyTpl);
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");
        var bodyHtml = E(bodyPlain.Replace("\r\n", "\n")).Replace("\n", "<br>");

        return new PermitSmsBuild
        {
            Phone = phone,
            SmsText = smsRaw,
            BodyHtml = bodyHtml,
            BodyPlain = bodyPlain,
            Title = title,
            PermitCode = code,
            ValidTo = entry.ValidTo,
        };
    }

    private static (string token, string hash) NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, HashToken(token));
    }

    private static string HashToken(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ReminderLandingHtml(string title, string bodyHtml, string? gueltigBis)
    {
        var validNote = !string.IsNullOrWhiteSpace(gueltigBis)
            ? $"<div class='valid'>Link gültig bis {System.Net.WebUtility.HtmlEncode(gueltigBis)}</div>"
            : "";
        return $@"<!DOCTYPE html>
<html lang='de'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<meta name='description' content='Mitteilung zu deiner Bewilligung.'>
<meta property='og:title' content='Bewilligung'>
<meta property='og:description' content='Mitteilung zu deiner Bewilligung.'>
<title>Bewilligung — OneCrew</title>
<link rel='icon' href='/favicon.svg' type='image/svg+xml'>
<style>
  body{{margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f6f3ee;color:#3f3f3f;display:flex;min-height:100vh;align-items:flex-start;justify-content:center}}
  .card{{background:#faf8f5;border:1px solid rgba(255,255,255,.62);box-shadow:0 8px 30px rgba(60,55,48,.16);border-radius:18px;padding:34px 28px;max-width:440px;width:90%;box-sizing:border-box;text-align:center;margin-top:clamp(20px,7vh,90px);margin-bottom:40px}}
  h1{{font-size:19px;margin:0 0 12px}}
  .msg{{font-size:14px;color:#3f3f3f;margin:0 0 12px;line-height:1.6;text-align:left}}
  .valid{{font-size:12px;color:#8b8b8b;margin-top:8px}}
</style></head>
<body><div class='card'><h1>{System.Net.WebUtility.HtmlEncode(title)}</h1><div class='msg'>{bodyHtml}</div>{validNote}</div></body></html>";
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin,superuser,user")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var entry = await _db.EmployeePermitHistories
            .FirstOrDefaultAsync(h => h.Id == id && h.EmployeeId == employeeId);
        if (entry == null) return NotFound();

        // AUSNAHME (Walter-Vorgabe 23.08.2026, analog zum Create): das Löschen
        // ist lohn-neutral, solange ein ANDERER Eintrag derselben Kategorie
        // bestehen bleibt (z.B. Doppel-Erfassung derselben B-Verlängerung —
        // der MA bleibt danach lückenlos in derselben Kategorie, QST-Pflicht
        // und Lohnrechnung ändern sich rückwirkend nicht). Nur wenn der
        // LETZTE Eintrag einer Kategorie verschwinden würde, bleibt der Lock.
        var gleicheKategorieBleibt = entry.PermitTypeId.HasValue
            && await _db.EmployeePermitHistories.AsNoTracking()
                .AnyAsync(h => h.EmployeeId == employeeId
                            && h.Id != entry.Id
                            && h.PermitTypeId == entry.PermitTypeId);

        var branchIdD     = await GetEmployeeBranchAsync(employeeId);
        var firstAllowedD = branchIdD.HasValue
            ? await _editLock.GetFirstAllowedDateAsync(User, branchIdD.Value)
            : null;
        if (!gleicheKategorieBleibt && firstAllowedD.HasValue && entry.ValidFrom < firstAllowedD.Value)
        {
            return Conflict(new {
                error            = "LOHN_EDIT_LOCKED",
                message          = $"Dieser Bewilligungs-Eintrag (gültig ab {entry.ValidFrom:dd.MM.yyyy}) wurde bereits in einem Lohnlauf verwendet und kann nicht gelöscht werden.",
                firstAllowedDate = firstAllowedD?.ToString("yyyy-MM-dd")
            });
        }

        _db.EmployeePermitHistories.Remove(entry);
        await _db.SaveChangesAsync();
        await SyncEmployeeFromHistoryAsync(employeeId);
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// Setzt employee.permit_type_id auf den Eintrag, der heute gültig ist.
    /// Wenn keiner heute gültig ist, wird das Feld auf NULL gesetzt.
    /// permit_expiry_date wurde 01.06.2026 entfernt — Anzeige + Dashboard-
    /// Warnung lesen jetzt direkt EmployeePermitHistory.ValidTo des
    /// jüngsten Eintrags.
    /// </summary>
    private async Task SyncEmployeeFromHistoryAsync(int employeeId)
    {
        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null) return;

        // Walter-Vorgabe 07.06.2026 (final): „neueste" = höchstes ValidTo,
        // bei Gleichheit ÄLTESTES ValidFrom (= Original-Eintrag, nicht Import-
        // Duplikat). NULL-ValidTo = max(9999-12-31).
        var max = new DateOnly(9999, 12, 31);
        var current = await _db.EmployeePermitHistories
            .Where(h => h.EmployeeId == employeeId)
            .OrderByDescending(h => h.ValidTo ?? max)
            .ThenBy(h => h.ValidFrom)
            .ThenBy(h => h.Id)
            .FirstOrDefaultAsync();

        emp.PermitTypeId = current?.PermitTypeId;
    }

    private int? GetCurrentUserId()
    {
        var sub = User.FindFirstValue("sub")
               ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}
