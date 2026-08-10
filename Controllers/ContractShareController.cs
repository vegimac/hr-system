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
/// Öffentlicher Vertrags-Link (Walter 07.07.2026). HR erzeugt im MA-Detail einen
/// Token-Link, über den der MA sein Arbeitsvertrag-PDF OHNE Login öffnen kann.
/// Zwei Wege: POST / (Link + SMS-Text zum Kopieren) und POST /send
/// (Direktversand per SMS über eCall an die MA-Handynummer, Etappe 2).
///
/// Muster analog Moments/PostfachSetup: Klartext-Token NUR im Link, in der DB
/// ausschliesslich der SHA-256-Hash; die öffentliche Route läuft anonym allein
/// über den (gehashten) Token; abgelaufen → 410.
/// </summary>
[ApiController]
[Route("api/contract-share")]
public class ContractShareController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EcallSmsService _sms;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public ContractShareController(AppDbContext db, EcallSmsService sms,
                                   IConfiguration config, IWebHostEnvironment env)
    {
        _db = db;
        _sms = sms;
        _config = config;
        _env = env;
    }

    /// <summary>
    /// Onboarding-Dokumente der Filiale des Vertrags (Walter 10.08.2026):
    /// die FILIAL-DOKUMENTE mit Kategorie ONBOARDING (Pflege im Filial-Tab
    /// «Dokumente») — hängen als Download-Liste am öffentlichen Vertrags-Link.
    /// </summary>
    private async Task<List<(long Id, string Name)>> BranchDokListeAsync(int employmentId)
    {
        var cpId = await _db.Employments.AsNoTracking()
            .Where(em => em.Id == employmentId)
            .Select(em => em.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (!cpId.HasValue) return new List<(long, string)>();
        var doks = await _db.CompanyDokumente.AsNoTracking()
            .Where(d => d.CompanyProfileId == cpId.Value && d.Kategorie == "ONBOARDING")
            .Select(d => new { d.Id, d.OriginalFilename })
            .ToListAsync();
        return doks
            .Where(d => d.OriginalFilename.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.OriginalFilename, StringComparer.OrdinalIgnoreCase)
            .Select(d => (d.Id, d.OriginalFilename))
            .ToList();
    }

    private int? UserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

    /// <summary>
    /// Vertrags-Links senden/verwalten (Walter 10.08.2026): admin/superuser
    /// (inkl. buchhaltung via Doppel-Claim) immer; Rolle user nur mit dem
    /// Häkchen «Vertrags-SMS senden» auf der Filiale des Vertrags.
    /// </summary>
    private async Task<bool> DarfVertragSendenAsync(int? companyProfileId)
    {
        if (User.IsInRole("admin") || User.IsInRole("superuser")) return true;
        var uid = UserId();
        if (uid == null || !companyProfileId.HasValue) return false;
        return await _db.UserBranchAccesses.AsNoTracking().AnyAsync(u =>
            u.UserId == uid.Value && u.CompanyProfileId == companyProfileId.Value && u.CanVertragSms);
    }

    // ── Token: Klartext im Link, nur der SHA-256-Hash in der DB ─────────────
    private static (string token, string hash) NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        var token = Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        return (token, HashToken(token));
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private const int ExpiryDays = 14;

    public record CreateDto(int? EmployeeId, int? EmploymentId);

    // Ergebnis des gemeinsamen Aufbau-Schritts (Token + Link + SMS-Text).
    private sealed class ShareBuildResult
    {
        public IActionResult? Error;
        public Employee? Emp;
        public string Url = "";
        public string SmsText = "";
        public DateTime ExpiresAt;
        public int EmploymentId;
        public int TokenId;
    }

    // ── HR: Link + SMS-Text erzeugen (Copy-Variante) ────────────────────────
    [HttpPost]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> Create([FromBody] CreateDto dto)
    {
        var b = await BuildShareAsync(dto);
        if (b.Error != null) return b.Error;
        return Ok(new
        {
            url = b.Url,
            smsText = b.SmsText,
            expiresAt = b.ExpiresAt,
            employmentId = b.EmploymentId,
        });
    }

    // Sensitiv-Guard für den Vertrags-SMS-Text (Walter 07.07.2026, Punkt 7).
    // BEWUSST eine EIGENE, reduzierte Liste — die Moments-Liste enthält
    // «vertrag»/«arbeitsvertrag» und würde jede Vertrags-SMS blocken. Hier
    // geht es nur um Zahlen/Konditionen, die nie in eine SMS gehören.
    private static readonly string[] SmsSensitiveKeywords =
    {
        "lohn", "salär", "salaer", "gehalt", "chf", "quellensteuer", "steuer",
        "ahv-nr", "ahv nr", "iban", "konto", "passwort", "krankheit", "arztzeugnis",
    };

    private static string? FindSmsSensitive(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lower = text.ToLowerInvariant();
        foreach (var kw in SmsSensitiveKeywords)
            if (lower.Contains(kw)) return kw;
        return null;
    }

    // ── HR: Link erzeugen UND direkt per SMS an den MA senden (Etappe 2) ────
    // Der SMS-Text kommt aus derselben VERTRAG_LINK-Vorlage wie die Copy-
    // Variante. Versand über EcallSmsService — dessen Test-Umleitung greift
    // automatisch (dann geht die SMS an die Test-Nummer, redirectedTo in der
    // Antwort informiert die UI). Beim Neuversand werden alle ÄLTEREN aktiven
    // Links desselben Vertrags entwertet — es ist immer nur der zuletzt
    // versendete Link gültig (Walter 07.07.2026).
    [HttpPost("send")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> SendSms([FromBody] CreateDto dto)
    {
        var b = await BuildShareAsync(dto);
        if (b.Error != null) return b.Error;

        var phone = (b.Emp!.PhoneMobile ?? "").Trim();
        if (phone.Length == 0)
            return BadRequest(new { error = "Für diesen Mitarbeitenden ist keine Handynummer hinterlegt." });

        // Punkt 7: keine sensiblen Inhalte in der SMS — blockt auch eine
        // unglücklich editierte VERTRAG_LINK-Vorlage (z.B. mit Lohn drin).
        var hit = FindSmsSensitive(b.SmsText);
        if (hit != null)
            return Conflict(new { error = $"SMS nicht gesendet: Der SMS-Text enthält den sensiblen Begriff «{hit}». Bitte die Vorlage VERTRAG_LINK anpassen — Konditionen gehören nicht in eine SMS." });

        // Alte aktive Tokens desselben Vertrags entwerten (der neue aus
        // BuildShareAsync ist bereits gespeichert und bleibt gültig).
        var now = DateTime.Now;
        await _db.ContractShareTokens
            .Where(t => t.EmploymentId == b.EmploymentId && t.RevokedAt == null
                        && t.ExpiresAt > now && t.Id != b.TokenId)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));

        var res = await _sms.SendSmsAsync(phone, b.SmsText, purpose: "VERTRAG", employeeId: b.Emp.Id);
        if (!res.Ok)
            return StatusCode(502, new { error = $"SMS-Versand fehlgeschlagen: {res.Error}" });

        var redirect = await _db.EcallSettings.AsNoTracking()
            .Where(r => r.Id == 1).Select(r => r.TestRedirectTo).FirstOrDefaultAsync();

        return Ok(new
        {
            ok = true,
            to = phone,
            redirectedTo = string.IsNullOrWhiteSpace(redirect) ? null : redirect!.Trim(),
            url = b.Url,
            expiresAt = b.ExpiresAt,
            messageId = res.MessageId,
        });
    }

    // ── HR: Status für den Bestätigungs-Dialog + „bereits gesendet"-Hinweis ─
    // Liefert den letzten Vertrags-SMS-Versand (sms_log) und den aktuell
    // gültigen Link (inkl. geöffnet/PDF-abgerufen) für diesen Vertrag.
    [HttpGet("status")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> Status([FromQuery] int employmentId)
    {
        var employment = await _db.Employments.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employmentId);
        if (employment == null) return NotFound(new { error = "Vertrag nicht gefunden." });

        var lastSms = await _db.SmsLogs.AsNoTracking()
            .Where(l => l.EmployeeId == employment.EmployeeId && l.Purpose == "VERTRAG" && l.Ok)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (DateTime?)l.CreatedAt)
            .FirstOrDefaultAsync();

        var now = DateTime.Now;
        var active = await _db.ContractShareTokens.AsNoTracking()
            .Where(t => t.EmploymentId == employmentId && t.RevokedAt == null && t.ExpiresAt > now)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { t.CreatedAt, t.ExpiresAt, t.OpenedAt, t.UsedAt })
            .FirstOrDefaultAsync();

        return Ok(new { lastSmsSentAt = lastSms, activeLink = active });
    }

    // ── HR: SMS-/Link-Status ALLER Verträge eines MA in einem Aufruf ────────
    // (Walter 05.08.2026): für die Vertragszeile in der MA-Übersicht —
    // «gesendet · geöffnet · PDF». NUR Verträge, zu denen je ein Token
    // existiert; ob wirklich eine SMS raus ist, entscheidet das Frontend
    // über lastSmsSentAt (Link-only-Erzeugung ohne SMS zeigt nichts).
    [HttpGet("status-by-employee")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> StatusByEmployee([FromQuery] int employeeId)
    {
        var lastSms = await _db.SmsLogs.AsNoTracking()
            .Where(l => l.EmployeeId == employeeId && l.Purpose == "VERTRAG" && l.Ok)
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => (DateTime?)l.CreatedAt)
            .FirstOrDefaultAsync();

        // Neuester Token pro Vertrag (auch abgelaufene/ersetzte — der
        // Öffnungs-Status des letzten Versands bleibt sichtbar).
        var tokens = await _db.ContractShareTokens.AsNoTracking()
            .Where(t => t.EmployeeId == employeeId)
            .GroupBy(t => t.EmploymentId)
            .Select(g => g.OrderByDescending(t => t.CreatedAt)
                .Select(t => new { t.EmploymentId, t.CreatedAt, t.ExpiresAt, t.OpenedAt, t.UsedAt, t.RevokedAt })
                .First())
            .ToListAsync();

        return Ok(new { lastSmsSentAt = lastSms, tokens });
    }

    // ── HR: alle aktiven Links dieses Vertrags widerrufen (Punkt 2) ─────────
    [HttpPost("revoke")]
    [Authorize(Roles = "admin,superuser,user,buchhaltung")]
    public async Task<IActionResult> Revoke([FromBody] CreateDto dto)
    {
        if (dto?.EmploymentId == null)
            return BadRequest(new { error = "Bitte employmentId angeben." });
        var cpIdRevoke = await _db.Employments.AsNoTracking()
            .Where(e => e.Id == dto.EmploymentId.Value)
            .Select(e => e.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (!await DarfVertragSendenAsync(cpIdRevoke))
            return StatusCode(403, new { error = "KEIN_VERTRAG_SMS_RECHT",
                message = "Kein Recht zum Verwalten von Vertrags-Links (Häkchen «Vertrags-SMS senden» im Filial-Tab «Unterzeichner»)." });

        var now = DateTime.Now;
        var n = await _db.ContractShareTokens
            .Where(t => t.EmploymentId == dto.EmploymentId.Value && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));

        return Ok(new { ok = true, revoked = n });
    }

    // Gemeinsamer Aufbau: Vertrag/MA auflösen, Token anlegen, URL + SMS-Text bauen.
    private async Task<ShareBuildResult> BuildShareAsync(CreateDto dto)
    {
        if (dto == null || (dto.EmployeeId == null && dto.EmploymentId == null))
            return new ShareBuildResult { Error = BadRequest(new { error = "Bitte employeeId oder employmentId angeben." }) };

        // Employment bestimmen: entweder direkt oder das aktivste/neueste des MA.
        Employment? employment;
        if (dto.EmploymentId != null)
        {
            employment = await _db.Employments.FirstOrDefaultAsync(e => e.Id == dto.EmploymentId.Value);
            if (employment == null) return new ShareBuildResult { Error = NotFound(new { error = "Vertrag nicht gefunden." }) };
        }
        else
        {
            // Bevorzugt aktive Verträge, sonst der mit dem grössten ContractStartDate.
            employment = await _db.Employments
                .Where(e => e.EmployeeId == dto.EmployeeId!.Value)
                .OrderByDescending(e => e.IsActive)
                .ThenByDescending(e => e.ContractStartDate)
                .FirstOrDefaultAsync();
            if (employment == null) return new ShareBuildResult { Error = NotFound(new { error = "Für diesen Mitarbeitenden ist kein Vertrag erfasst." }) };
        }

        // Vertrags-SMS nur für ausgewählte Benutzer (Walter 10.08.2026):
        // admin/superuser immer; Rolle user braucht das Häkchen «Vertrags-SMS
        // senden» (user_branch_access) für die Filiale des Vertrags.
        if (!await DarfVertragSendenAsync(employment.CompanyProfileId))
            return new ShareBuildResult
            {
                Error = StatusCode(403, new { error = "KEIN_VERTRAG_SMS_RECHT",
                    message = "Kein Recht zum Versenden von Vertrags-Links — das Häkchen «Vertrags-SMS senden» wird im Filial-Tab «Unterzeichner» vergeben." }),
            };

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == employment.EmployeeId);
        if (emp == null) return new ShareBuildResult { Error = NotFound(new { error = "Mitarbeiter nicht gefunden." }) };

        // Firmenname der Filiale des Vertrags (für den SMS-Text).
        var firma = "deinem Arbeitgeber";
        if (employment.CompanyProfileId != null)
        {
            var cp = await _db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == employment.CompanyProfileId.Value);
            if (cp != null && !string.IsNullOrWhiteSpace(cp.FullDisplayName))
                firma = cp.FullDisplayName.Trim();
        }

        var (token, tokenHash) = NewToken();
        var expiresAt = DateTime.Now.AddDays(ExpiryDays);

        var tokenRow = new ContractShareToken
        {
            EmployeeId   = emp.Id,
            EmploymentId = employment.Id,
            TokenHash    = tokenHash,
            ExpiresAt    = expiresAt,
            CreatedAt    = DateTime.Now,
            CreatedBy    = UserId(),
        };
        _db.ContractShareTokens.Add(tokenRow);
        await _db.SaveChangesAsync();

        // Öffentliche Basis-URL für MA-Links = kanonische SiteUrl (dieselbe Quelle
        // wie die E-Mails / QR-Codes, aus smtp_setting), NICHT die Admin-Domain
        // (Request.Host). Fallback: https://onecrew.ch/.
        var siteRow = await _db.SmtpSettings.AsNoTracking().FirstOrDefaultAsync();
        var baseUrl = (siteRow != null && !string.IsNullOrWhiteSpace(siteRow.SiteUrl))
            ? siteRow.SiteUrl.Trim()
            : "https://onecrew.ch/";
        var url = $"{baseUrl.TrimEnd('/')}/vertrag/{token}";

        var vorname = (emp.FirstName ?? "").Trim();

        // SMS-Text bevorzugt aus der pflegbaren Moments-Vorlage (Typ VERTRAG_LINK).
        // Platzhalter {Vorname}/{Firma}/{Link}/{GueltigBis} werden ersetzt; fehlt eine
        // aktive Vorlage, greift der fest verdrahtete Fallback-Text.
        string smsText;
        var tpl = await _db.MomentTexts
            .Include(t => t.MomentType)
            .Where(t => t.IsActive && t.MomentType != null && t.MomentType.Code == "VERTRAG_LINK")
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
            .FirstOrDefaultAsync();
        if (tpl != null && !string.IsNullOrWhiteSpace(tpl.SmsText))
        {
            smsText = tpl.SmsText
                .Replace("{Vorname}", vorname)
                .Replace("{Firma}", firma)
                .Replace("{Link}", url)
                .Replace("{GueltigBis}", expiresAt.ToString("dd.MM.yyyy"));
        }
        else
        {
            var anrede = vorname.Length > 0 ? $"Hallo {vorname}, " : "Hallo, ";
            smsText = $"{anrede}hier ist dein Arbeitsvertrag bei {firma}: {url}";
        }

        return new ShareBuildResult
        {
            Emp          = emp,
            Url          = url,
            SmsText      = smsText,
            ExpiresAt    = expiresAt,
            EmploymentId = employment.Id,
            TokenId      = tokenRow.Id,
        };
    }

    // ── Öffentlich: NEUTRALE Landing-Page über den Token ────────────────────
    // WICHTIG (Walter-Vorgabe 07.07.2026): Der Link darf NICHT direkt das PDF
    // liefern — sonst erzeugen Messaging-Apps (iMessage/WhatsApp) eine
    // Rich-Vorschau und der Vertragsinhalt wird im Chat sichtbar. Deshalb
    // zeigt der Link nur eine neutrale Seite mit Button; das PDF lädt erst
    // beim Klick (eigene Route unten). Vorschau-Bots sehen nur diese Seite.
    [AllowAnonymous]
    [HttpGet("/vertrag/{token}")]
    public async Task<IActionResult> PublicLanding(string token)
    {
        var hash = HashToken(token);
        var t = await _db.ContractShareTokens.FirstOrDefaultAsync(x => x.TokenHash == hash);
        string html;
        if (t == null)
            html = LandingHtml("Link nicht gefunden", "Dieser Vertrags-Link ist ungültig.", null);
        else if (t.RevokedAt != null)
            html = LandingHtml("Link nicht mehr gültig", "Dieser Vertrags-Link wurde ersetzt oder zurückgezogen. Bitte fordere einen neuen an.", null);
        else if (t.ExpiresAt < DateTime.Now)
            html = LandingHtml("Link abgelaufen", "Dieser Vertrags-Link ist abgelaufen. Bitte fordere einen neuen an.", null);
        else
        {
            // Öffnungs-Log (Punkt 3): erstes Öffnen der Landing-Page festhalten.
            if (t.OpenedAt == null)
            {
                t.OpenedAt = DateTime.Now;
                try { await _db.SaveChangesAsync(); } catch { /* best-effort */ }
            }
            var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == t.EmployeeId);
            var vorname = (emp?.FirstName ?? "").Trim();
            var pdfHref = $"/vertrag/{token}/pdf";

            // Pflegbare Vorlage (VERTRAG_LINK) laden — deren MITTEILUNG (BodyText) wird
            // auf der Landing-Page angezeigt, mit ersetzten Platzhaltern. Ohne Vorlage
            // greift der schlichte Standardtext.
            var tpl = await _db.MomentTexts
                .Include(x => x.MomentType)
                .Where(x => x.IsActive && x.MomentType != null && x.MomentType.Code == "VERTRAG_LINK")
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                .FirstOrDefaultAsync();

            string bodyHtml;
            if (tpl != null && !string.IsNullOrWhiteSpace(tpl.BodyText))
            {
                var briefanrede = !string.IsNullOrWhiteSpace(emp?.LetterSalutation)
                    ? emp!.LetterSalutation!
                    : (vorname.Length > 0 ? $"Hallo {vorname}" : "Hallo");
                // {SenderName} = nur Vorname (Du-Ton / Moments-Konvention).
                var senderName = "";
                if (t.CreatedBy.HasValue)
                {
                    var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == t.CreatedBy.Value);
                    senderName = (u?.FirstName ?? "").Trim();
                }
                bodyHtml = RenderTemplateBody(tpl.BodyText, briefanrede, vorname, senderName,
                                              t.ExpiresAt.ToString("dd.MM.yyyy"), pdfHref);
            }
            else
            {
                bodyHtml = vorname.Length > 0
                    ? $"Hallo {System.Net.WebUtility.HtmlEncode(vorname)}, hier findest du deinen Arbeitsvertrag als PDF."
                    : "Hier findest du deinen Arbeitsvertrag als PDF.";
            }
            // Filial-Dokumente (Kategorie ONBOARDING: AGB, Hygiene, Datenschutz …)
            // als Download-Liste unter dem Vertrags-Button (Walter 10.08.2026).
            string docsHtml = "";
            var doks = await BranchDokListeAsync(t.EmploymentId);
            if (doks.Count > 0)
            {
                var items = string.Join("", doks.Select(d =>
                    $"<a class='doc' href='/vertrag/{token}/dok/{d.Id}'>📎 {System.Net.WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(d.Name))}</a>"));
                docsHtml = $"<div class='docs'><div class='docstitle'>Wichtige Dokumente deines Restaurants</div>{items}</div>";
            }
            html = LandingHtml("Dein Arbeitsvertrag", bodyHtml, pdfHref,
                               t.ExpiresAt.ToString("dd.MM.yyyy"), docsHtml);
        }
        return Content(html, "text/html; charset=utf-8");
    }

    // ── Öffentlich: Filial-Dokument (gleiche Token-Prüfung wie das PDF).
    //    Nur ONBOARDING-Dokumente der Filiale DES Vertrags sind erreichbar. ──
    [AllowAnonymous]
    [HttpGet("/vertrag/{token}/dok/{dokId:long}")]
    public async Task<IActionResult> PublicDok(string token, long dokId)
    {
        var hash = HashToken(token);
        var t = await _db.ContractShareTokens.AsNoTracking().FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (t == null)
            return NotFound("Dieser Vertrags-Link wurde nicht gefunden.");
        if (t.RevokedAt != null)
            return StatusCode(410, "Dieser Vertrags-Link wurde ersetzt oder zurückgezogen.");
        if (t.ExpiresAt < DateTime.Now)
            return StatusCode(410, "Dieser Vertrags-Link ist abgelaufen.");

        var cpId = await _db.Employments.AsNoTracking()
            .Where(em => em.Id == t.EmploymentId)
            .Select(em => em.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (!cpId.HasValue) return NotFound();
        var doc = await _db.CompanyDokumente.AsNoTracking().FirstOrDefaultAsync(d =>
            d.Id == dokId && d.CompanyProfileId == cpId.Value && d.Kategorie == "ONBOARDING");
        if (doc == null) return NotFound();

        // Gleiche Storage-Wurzel wie CompanyDokumenteController: filiale/{cpId}/.
        var root = _config["Documents:StoragePath"];
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(_env.ContentRootPath, "data", "documents");
        var path = Path.Combine(root, "filiale", doc.CompanyProfileId.ToString(), doc.StorageFilename);
        if (!System.IO.File.Exists(path)) return NotFound();
        return PhysicalFile(path, "application/pdf");
    }

    // Platzhalter der Vorlage ersetzen und in sicheres HTML wandeln: erst den
    // Vorlagentext encoden (kein HTML-Injection), dann Platzhalter durch encodete
    // Werte ersetzen, zuletzt Zeilenumbrüche → br.
    //
    // {Link} wird auf der Landing-Page NICHT als Inline-Link gerendert (Walter
    // 07.07.2026: der Link soll nur 1x erscheinen — als Button). Zeilen, die
    // {Link} enthalten, werden komplett weggelassen; das Gültig-bis-Datum zeigt
    // die Landing-Page klein unter dem Button. In der SMS (Etappe 2) wird
    // {Link} dagegen durch die echte URL ersetzt.
    private static string RenderTemplateBody(string raw, string briefanrede, string vorname,
                                             string senderName, string gueltigBis, string pdfHref)
    {
        static string E(string s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        var lines = (raw ?? "").Replace("\r\n", "\n").Split('\n')
            .Where(l => !l.Contains("{Link}"))
            .ToList();

        var s = E(string.Join("\n", lines))
            .Replace("{Briefanrede}", E(briefanrede))
            .Replace("{Vorname}", E(vorname))
            .Replace("{SenderName}", E(senderName))
            .Replace("{GueltigBis}", E(gueltigBis));
        return s.Replace("\n", "<br>");
    }

    // Neutrale HTML-Karte (kein Vertragsinhalt). Einfache Attribute in
    // Hochkommas, damit der C#-String keine doppelten Anführungszeichen braucht.
    // Karte sitzt OBEN (nicht vertikal zentriert) — auf dem Handy war sie sonst
    // zu weit unten (Walter 07.07.2026). Gültig-bis klein unter dem Button.
    private static string LandingHtml(string title, string bodyHtml, string? pdfHref,
                                      string? gueltigBis = null, string docsHtml = "")
    {
        var btn = pdfHref != null
            ? $"<a class='btn' href='{pdfHref}'>📄 Arbeitsvertrag öffnen</a>"
            : "";
        // Fester Hinweis (Punkt 6) — bewusst hardcoded, damit keine Vorlagen-
        // Änderung ihn versehentlich entfernt.
        var signNote = pdfHref != null
            ? "<div class='sign'>Dieser Vertrag dient zur Vorbereitung. Die Unterzeichnung erfolgt vor Ort.</div>"
            : "";
        var validNote = (pdfHref != null && !string.IsNullOrWhiteSpace(gueltigBis))
            ? $"<div class='valid'>Link gültig bis {System.Net.WebUtility.HtmlEncode(gueltigBis)}</div>"
            : "";
        return $@"<!DOCTYPE html>
<html lang='de'><head><meta charset='utf-8'>
<meta name='viewport' content='width=device-width, initial-scale=1'>
<meta name='description' content='Sicherer Link zu deinem Arbeitsvertrag.'>
<meta property='og:title' content='Arbeitsvertrag'>
<meta property='og:description' content='Sicherer Link zu deinem Arbeitsvertrag.'>
<title>Arbeitsvertrag — OneCrew</title>
<link rel='icon' href='/favicon.svg' type='image/svg+xml'>
<style>
  body{{margin:0;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f6f3ee;color:#3f3f3f;display:flex;min-height:100vh;align-items:flex-start;justify-content:center}}
  .card{{background:#faf8f5;border:1px solid rgba(255,255,255,.62);box-shadow:0 8px 30px rgba(60,55,48,.16);border-radius:18px;padding:34px 28px;max-width:440px;width:90%;box-sizing:border-box;text-align:center;margin-top:clamp(20px,7vh,90px);margin-bottom:40px}}
  h1{{font-size:19px;margin:0 0 12px}}
  .msg{{font-size:14px;color:#3f3f3f;margin:0 0 22px;line-height:1.6;text-align:left}}
  a.btn{{display:inline-block;background:#3f3f3f;color:#fff;text-decoration:none;padding:13px 24px;border-radius:12px;font-size:15px;font-weight:600}}
  .sign{{font-size:12.5px;color:#646464;margin-top:14px;padding-top:12px;border-top:1px solid rgba(139,139,139,.25)}}
  .valid{{font-size:12px;color:#8b8b8b;margin-top:8px}}
  .docs{{margin-top:20px;text-align:left}}
  .docstitle{{font-weight:700;font-size:13px;margin-bottom:7px;color:#3f3f3f}}
  a.doc{{display:block;font-size:13.5px;color:#3f3f3f;text-decoration:none;padding:8px 12px;border:1px solid rgba(139,139,139,.3);border-radius:10px;margin-bottom:6px;background:#fff}}
</style></head>
<body><div class='card'><h1>{title}</h1><div class='msg'>{bodyHtml}</div>{btn}{docsHtml}{signNote}{validNote}</div></body></html>";
    }

    // ── Öffentlich: das PDF (wird erst per Button-Klick geladen) ─────────────
    [AllowAnonymous]
    [HttpGet("/vertrag/{token}/pdf")]
    public async Task<IActionResult> PublicPdf(string token)
    {
        var hash = HashToken(token);
        var t = await _db.ContractShareTokens.FirstOrDefaultAsync(x => x.TokenHash == hash);
        if (t == null)
            return NotFound("Dieser Vertrags-Link wurde nicht gefunden.");
        if (t.RevokedAt != null)
            return StatusCode(410, "Dieser Vertrags-Link wurde ersetzt oder zurückgezogen. Bitte einen neuen Link anfordern.");
        if (t.ExpiresAt < DateTime.Now)
            return StatusCode(410, "Dieser Vertrags-Link ist abgelaufen. Bitte einen neuen Link anfordern.");

        var pdf = await ContractPdfBuilder.BuildAsync(_db, t.EmploymentId);
        if (pdf == null)
            return NotFound("Der Vertrag konnte nicht erzeugt werden.");

        if (t.UsedAt == null)
        {
            t.UsedAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }
        return File(pdf, "application/pdf");
    }
}
