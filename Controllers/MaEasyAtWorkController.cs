using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace HrSystem.Controllers;

/// <summary>
/// easy@work-Mitteilung an Mitarbeitende (Walter-Vorgabe 08.09.2026) — dritter
/// Kanal neben Gruppen-E-Mail und SMS, gleiche Empfängerwahl wie die
/// Gruppen-E-Mail (Filiale × Vertragsmodell × Funktion, Empfänger-Vorschau).
///
/// easy@work kennt keine Textnachrichten: OneCrew macht aus Betreff + Text ein
/// PDF im Haus-Stil (wählbare Unterzeichnung) und legt es als HR-Datei ins
/// Dossier jedes MA; easy@work benachrichtigt den MA in seiner App. Statt des
/// Text-PDFs kann auch eine Datei mitgegeben werden.
///
/// Freigabe-Matrix: Kategorie EASYATWORK, Kanal easy@work. Kein Haken → die
/// Mitteilung geht NUR an die Test-Personalnummer (Systemsteuerung), alle
/// anderen Empfänger werden als «umgeleitet» gezählt.
/// </summary>
[ApiController]
[Route("api/ma-eaw")]
[Authorize(Roles = "admin,superuser")]
public class MaEasyAtWorkController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly EasyAtWorkClient _client;
    private readonly VersandFreigabeService _freigabe;
    private readonly MitteilungPdfService _pdf;
    private readonly ILogger<MaEasyAtWorkController> _log;

    /// <summary>Name des Dateityps in easy@work, unter dem Mitteilungen abgelegt werden.</summary>
    public const string DateitypKey = "EasyAtWork.MitteilungDateityp";
    public const string DateitypStandard = "Information";

    /// <summary>Rate-Limit easy@work: 240 Anfragen/Minute → wir bleiben deutlich darunter.</summary>
    private static readonly TimeSpan Drossel = TimeSpan.FromMilliseconds(350);

    public MaEasyAtWorkController(AppDbContext db, EasyAtWorkClient client, VersandFreigabeService freigabe,
                                  MitteilungPdfService pdf, ILogger<MaEasyAtWorkController> log)
    {
        _db = db; _client = client; _freigabe = freigabe; _pdf = pdf; _log = log;
    }

    // ── GET /api/ma-eaw/status ──────────────────────────────────────────
    /// <summary>Für die Seite: Freigabe-Stand, Test-Personalnummer, Dateityp-Name.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var scharf = await _freigabe.IstScharfAsync(VersandKategorie.EasyAtWork, VersandFreigabeService.Kanal.EasyAtWork, ct);
        var testNr = await _freigabe.GetEawTestNummerAsync(ct);
        return Ok(new
        {
            konfiguriert = _client.IsConfigured,
            scharf,
            testNummer = testNr,
            dateityp = await DateitypNameAsync(ct),
        });
    }

    // ── GET /api/ma-eaw/unterzeichner?companyProfileId= ─────────────────
    /// <summary>
    /// Wählbare Unterzeichner/innen: bei gewählter Filiale die Benutzer mit
    /// Filial-Zugang (Funktion aus UserBranchAccess), sonst alle aktiven
    /// OneCrew-Benutzer. Der eingeloggte Benutzer ist vorausgewählt.
    /// </summary>
    [HttpGet("unterzeichner")]
    public async Task<IActionResult> Unterzeichner([FromQuery] int? companyProfileId, CancellationToken ct)
    {
        var me = CurrentUserId();
        var list = new List<object>();
        var seen = new HashSet<int>();
        if (companyProfileId.HasValue)
        {
            var rows = await _db.UserBranchAccesses.AsNoTracking().Include(a => a.User)
                .Where(a => a.CompanyProfileId == companyProfileId.Value && a.User != null && a.User.IsActive && a.User.Role != "employee")
                .OrderByDescending(a => a.IsDefault).ThenBy(a => a.User!.FirstName).ThenBy(a => a.User!.LastName)
                .ToListAsync(ct);
            foreach (var a in rows)
            {
                if (a.User == null || !seen.Add(a.UserId)) continue;
                var funktion = !string.IsNullOrWhiteSpace(a.FunctionTitle) ? a.FunctionTitle!.Trim()
                    : a.Role == "GESCHAEFTSFUEHRER" ? "Geschäftsführer/in"
                    : a.Role == "HR_VERANTWORTLICH" ? "HR-Verantwortliche/r" : null;
                list.Add(new { userId = a.UserId, name = a.User.DisplayName, funktion, hatUnterschrift = a.User.SignaturePng is { Length: > 0 }, istIch = a.UserId == me });
            }
        }
        // Eingeloggter Benutzer immer dabei (Admin/HR ohne Filial-Zugang), plus alle übrigen aktiven Benutzer.
        var users = await _db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive && u.Role != "employee")
            .OrderBy(u => u.FirstName).ThenBy(u => u.LastName)
            .ToListAsync(ct);
        foreach (var u in users)
        {
            if (!seen.Add(u.Id)) continue;
            list.Add(new { userId = u.Id, name = u.DisplayName, funktion = (string?)null, hatUnterschrift = u.SignaturePng is { Length: > 0 }, istIch = u.Id == me });
        }
        return Ok(list);
    }

    // ── POST /api/ma-eaw/check  { employeeIds: [..] } ───────────────────
    /// <summary>Welche der gewählten MA haben eine easy@work-ID (= erreichbar)?</summary>
    public sealed record CheckDto(List<int> EmployeeIds);

    [HttpPost("check")]
    public async Task<IActionResult> Check([FromBody] CheckDto dto, CancellationToken ct)
    {
        var ids = dto?.EmployeeIds ?? new List<int>();
        var rows = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id))
            .Select(e => new { employeeId = e.Id, number = e.EmployeeNumber, hatEawId = e.EasyAtWorkEmployeeId != null && e.EasyAtWorkEmployeeId > 0 })
            .ToListAsync(ct);
        return Ok(rows);
    }

    // ── POST /api/ma-eaw/senden (multipart) ─────────────────────────────
    [HttpPost("senden")]
    [RequestSizeLimit(30_000_000)]
    public async Task<IActionResult> Senden(
        [FromForm] string? betreff,
        [FromForm] string? text,
        [FromForm] string? employeeIds,
        [FromForm] IFormFile? anhang,
        [FromForm] int? unterzeichnerUserId,
        [FromForm] bool ohneUnterschrift = false,
        [FromForm] string? filialeText = null,
        [FromForm] string? modelleText = null,
        [FromForm] string? funktionenText = null,
        [FromForm] int? companyProfileId = null,
        CancellationToken ct = default)
    {
        if (!_client.IsConfigured) return StatusCode(503, new { error = "EAW_NOT_CONFIGURED", message = "easy@work ist nicht konfiguriert." });
        if (string.IsNullOrWhiteSpace(betreff))
            return BadRequest(new { error = "BETREFF_FEHLT", message = "Bitte einen Betreff eingeben." });
        var maIds = ParseIds(employeeIds);
        if (maIds.Count == 0)
            return BadRequest(new { error = "KEINE_EMPFAENGER", message = "Bitte mindestens einen Empfänger wählen." });
        var hatAnhang = anhang != null && anhang.Length > 0;
        var reinText = (text ?? "").Trim();
        if (reinText.Length == 0 && !hatAnhang)
            return BadRequest(new { error = "TEXT_FEHLT", message = "Bitte einen Mitteilungstext eingeben oder ein Dokument anhängen." });

        // Freigabe (Walter 08.09.2026): kein Haken → nur an die Test-Personalnummer.
        var scharf = await _freigabe.IstScharfAsync(VersandKategorie.EasyAtWork, VersandFreigabeService.Kanal.EasyAtWork, ct);
        var testNr = await _freigabe.GetEawTestNummerAsync(ct);
        if (!scharf && string.IsNullOrWhiteSpace(testNr))
            return Conflict(new { error = "EAW_BLOCKIERT", message = "easy@work-Mitteilungen sind nicht scharf geschaltet und es ist keine Test-Personalnummer hinterlegt (Systemsteuerung → Freigabe-Matrix)." });

        // Nur AKTIVE MA (Walter-Vorgabe wie bei der Gruppen-E-Mail) — die Liste im Browser kann alt sein.
        var heute = DateTime.Today;
        var aktivIds = await _db.Employments.AsNoTracking()
            .Where(em => em.IsActive && em.ContractStartDate <= heute
                      && (em.ContractEndDate == null || em.ContractEndDate >= heute)
                      && em.Employee!.IsActive && !em.Employee!.IsHidden && maIds.Contains(em.EmployeeId))
            .Select(em => em.EmployeeId).Distinct().ToListAsync(ct);
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => aktivIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.EasyAtWorkEmployeeId })
            .ToListAsync(ct);
        // Filiale je MA (jüngste Anstellung) → Customer.
        var empBranch = (await _db.Employments.AsNoTracking()
                .Where(em => aktivIds.Contains(em.EmployeeId) && em.CompanyProfileId != null)
                .Select(em => new { em.EmployeeId, em.CompanyProfileId, em.ContractStartDate })
                .ToListAsync(ct))
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.ContractStartDate).First().CompanyProfileId!.Value);
        var branches = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.CompanyName, c.BranchName, c.City }).ToListAsync(ct);
        var mappings = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .Select(m => new { m.CompanyProfileId, m.EasyAtWorkCustomerId }).ToListAsync(ct);
        var alleCustomers = mappings.Select(m => m.EasyAtWorkCustomerId).Distinct().ToList();

        // Umleitung: nicht scharf → Empfängerliste = nur der Test-MA (einmal).
        var umgeleitet = new List<object>();
        if (!scharf)
        {
            var test = await _db.Employees.AsNoTracking()
                .Where(e => !e.IsHidden && e.EmployeeNumber == testNr!.Trim())
                .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.EasyAtWorkEmployeeId })
                .FirstOrDefaultAsync(ct);
            if (test == null)
                return Conflict(new { error = "TEST_MA_FEHLT", message = $"Test-Personalnummer {testNr} ist in OneCrew nicht vorhanden." });
            foreach (var e in emps.Where(e => e.Id != test.Id))
                umgeleitet.Add(new { name = $"{e.FirstName} {e.LastName}".Trim(), number = e.EmployeeNumber });
            emps = new[] { test }.ToList();
            if (!empBranch.ContainsKey(test.Id))
            {
                var tb = await _db.Employments.AsNoTracking().Where(em => em.EmployeeId == test.Id && em.CompanyProfileId != null)
                    .OrderByDescending(em => em.ContractStartDate).Select(em => em.CompanyProfileId).FirstOrDefaultAsync(ct);
                if (tb is int tbv) empBranch[test.Id] = tbv;
            }
        }

        // Unterzeichnung (wählbar): Benutzer + Funktion aus dem Filial-Zugang + Unterschrift-Bild.
        string? signerName = null, signerFunktion = null; byte[]? signerPng = null;
        if (!ohneUnterschrift)
        {
            var uid = unterzeichnerUserId ?? CurrentUserId();
            if (uid.HasValue)
            {
                var u = await _db.AppUsers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid.Value, ct);
                if (u != null)
                {
                    signerName = u.DisplayName;
                    signerPng = u.SignaturePng;
                    if (companyProfileId.HasValue)
                    {
                        var uba = await _db.UserBranchAccesses.AsNoTracking()
                            .Where(a => a.UserId == u.Id && a.CompanyProfileId == companyProfileId.Value)
                            .OrderByDescending(a => a.IsDefault).FirstOrDefaultAsync(ct);
                        signerFunktion = !string.IsNullOrWhiteSpace(uba?.FunctionTitle) ? uba!.FunctionTitle!.Trim()
                            : uba?.Role == "GESCHAEFTSFUEHRER" ? "Geschäftsführer/in"
                            : uba?.Role == "HR_VERANTWORTLICH" ? "HR-Verantwortliche/r" : null;
                    }
                }
            }
        }

        byte[]? anhangBytes = null; string anhangName = "", anhangMime = "application/octet-stream";
        if (hatAnhang)
        {
            using var ms = new MemoryStream();
            await anhang!.CopyToAsync(ms, ct);
            anhangBytes = ms.ToArray();
            anhangName = Path.GetFileName(anhang.FileName);
            anhangMime = string.IsNullOrWhiteSpace(anhang.ContentType) ? "application/octet-stream" : anhang.ContentType;
        }

        // Protokoll VOR dem Versand anlegen (wie Gruppen-E-Mail).
        var protokoll = new EasyAtWorkMitteilungLog
        {
            GesendetAm = DateTime.Now,
            GesendetVonUserId = CurrentUserId(),
            Betreff = betreff.Trim(),
            Filiale = string.IsNullOrWhiteSpace(filialeText) ? "Alle Filialen" : filialeText.Trim(),
            Modelle = string.IsNullOrWhiteSpace(modelleText) ? "alle" : modelleText.Trim(),
            Funktionen = string.IsNullOrWhiteSpace(funktionenText) ? "alle" : funktionenText.Trim(),
            Unterzeichner = ohneUnterschrift ? null : (signerName + (signerFunktion != null ? $" ({signerFunktion})" : "")),
            AnhangName = hatAnhang ? anhangName : null,
            MitText = reinText.Length > 0,
            Scharf = scharf,
        };
        try { _db.EasyAtWorkMitteilungLogs.Add(protokoll); await _db.SaveChangesAsync(ct); }
        catch (Exception ex) { _log.LogWarning(ex, "easy@work-Mitteilung: Protokoll konnte nicht angelegt werden"); }

        var dateitypName = await DateitypNameAsync(ct);
        var typeIdProCustomer = new Dictionary<int, int?>();
        var gesendet = new List<object>();
        var fehlgeschlagen = new List<object>();
        var ohneEawId = new List<object>();
        var datum = DateOnly.FromDateTime(DateTime.Now);

        foreach (var e in emps)
        {
            var name = $"{e.FirstName} {e.LastName}".Trim();
            if (e.EasyAtWorkEmployeeId is not int eawId || eawId <= 0)
            { ohneEawId.Add(new { name, number = e.EmployeeNumber }); continue; }

            // Customer-Kandidaten: Filiale des MA zuerst, dann alle übrigen.
            var cands = new List<int>();
            if (empBranch.TryGetValue(e.Id, out var cpId))
            {
                var m = mappings.FirstOrDefault(x => x.CompanyProfileId == cpId);
                if (m != null) cands.Add(m.EasyAtWorkCustomerId);
            }
            foreach (var c in alleCustomers) if (!cands.Contains(c)) cands.Add(c);

            var filiale = empBranch.TryGetValue(e.Id, out var cp2) ? branches.FirstOrDefault(b => b.Id == cp2) : null;

            byte[] bytes; string fileName, mime;
            if (anhangBytes != null) { bytes = anhangBytes; fileName = anhangName; mime = anhangMime; }
            else
            {
                bytes = _pdf.Generate(new MitteilungPdfService.MitteilungData(
                    FirmaName: filiale?.CompanyName, RestaurantName: filiale?.BranchName,
                    EmpfaengerName: name, Betreff: betreff.Trim(), Text: reinText, Datum: datum,
                    Ort: filiale?.City, AbsenderName: signerName, AbsenderFunktion: signerFunktion), signerPng);
                var safe = string.Concat(betreff.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')).Trim('-');
                if (safe.Length > 60) safe = safe[..60];
                fileName = $"Mitteilung-{datum:yyyy-MM-dd}-{(safe.Length > 0 ? safe : "OneCrew")}.pdf";
                mime = "application/pdf";
            }

            string? fehler = null; int? usedCustomer = null; long? fileId = null;
            foreach (var cid in cands)
            {
                // Dateityp je Customer (einmal auflösen, bei Bedarf anlegen).
                if (!typeIdProCustomer.TryGetValue(cid, out var typeId))
                {
                    typeId = await ResolveOrCreateTypeAsync(cid, dateitypName, ct);
                    typeIdProCustomer[cid] = typeId;
                    await Task.Delay(Drossel, ct);
                }
                if (typeId == null) { fehler = $"Dateityp «{dateitypName}» bei Customer {cid} nicht anlegbar"; continue; }

                var (status, body) = await _client.UploadHrFileRawAsync(cid, eawId, bytes, fileName, mime,
                    typeId.Value, betreff.Trim(), notify: true, ct: ct);
                await Task.Delay(Drossel, ct);
                if (status >= 200 && status < 300)
                {
                    usedCustomer = cid;
                    try { var el = JsonSerializer.Deserialize<JsonElement>(body); if (el.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number) fileId = idEl.GetInt64(); } catch { }
                    fehler = null;
                    break;
                }
                // 404 = MA gehört nicht zu diesem Customer → nächsten probieren; alles andere = Fehler.
                fehler = $"{status}: {Kurz(body)}";
                if (status != 404) break;
            }

            if (usedCustomer != null) gesendet.Add(new { name, number = e.EmployeeNumber, customerId = usedCustomer, fileId });
            else fehlgeschlagen.Add(new { name, number = e.EmployeeNumber, fehler = fehler ?? "kein Customer gefunden" });
        }

        try
        {
            protokoll.AnzahlGesendet = gesendet.Count;
            protokoll.AnzahlFehlgeschlagen = fehlgeschlagen.Count;
            protokoll.AnzahlOhneEawId = ohneEawId.Count;
            protokoll.AnzahlUmgeleitet = umgeleitet.Count;
            protokoll.DetailsJson = JsonSerializer.Serialize(new { gesendet, fehlgeschlagen, ohneEawId, umgeleitet });
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) { _log.LogWarning(ex, "easy@work-Mitteilung: Protokoll konnte nicht nachgetragen werden"); }

        return Ok(new
        {
            scharf,
            testNummer = scharf ? null : testNr,
            gesendet = gesendet.Count,
            fehlgeschlagen,
            ohneEawId,
            umgeleitet,
            anhang = hatAnhang ? anhangName : null,
            details = gesendet,
        });
    }

    // ── GET /api/ma-eaw/log ─────────────────────────────────────────────
    [HttpGet("log")]
    public async Task<IActionResult> Log([FromQuery] int limit = 25, CancellationToken ct = default)
    {
        var rows = await _db.EasyAtWorkMitteilungLogs.AsNoTracking().Include(l => l.GesendetVonUser)
            .OrderByDescending(l => l.GesendetAm).Take(Math.Clamp(limit, 1, 200)).ToListAsync(ct);
        return Ok(rows.Select(l => new
        {
            l.Id, l.GesendetAm, l.Betreff, l.Filiale, l.Modelle, l.Funktionen, l.Unterzeichner, l.AnhangName, l.MitText, l.Scharf,
            l.AnzahlGesendet, l.AnzahlFehlgeschlagen, l.AnzahlOhneEawId, l.AnzahlUmgeleitet,
            von = l.GesendetVonUser?.DisplayName,
        }));
    }

    [HttpGet("log/{id:int}/details")]
    public async Task<IActionResult> LogDetails(int id, CancellationToken ct)
    {
        var l = await _db.EasyAtWorkMitteilungLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (l == null) return NotFound();
        object? details = null;
        try { if (!string.IsNullOrWhiteSpace(l.DetailsJson)) details = JsonSerializer.Deserialize<JsonElement>(l.DetailsJson); } catch { }
        return Ok(new { l.Id, details });
    }

    // ── Hilfen ──────────────────────────────────────────────────────────

    private async Task<string> DateitypNameAsync(CancellationToken ct)
    {
        try
        {
            var v = await _db.AppSettings.AsNoTracking().Where(a => a.Key == DateitypKey).Select(a => a.Value).FirstOrDefaultAsync(ct);
            return string.IsNullOrWhiteSpace(v) ? DateitypStandard : v.Trim();
        }
        catch { return DateitypStandard; }
    }

    /// <summary>Dateityp beim Customer per Name finden (Gross/Klein egal), sonst anlegen (nur PDF, nicht Pflicht).</summary>
    private async Task<int?> ResolveOrCreateTypeAsync(int customerId, string name, CancellationToken ct)
    {
        try
        {
            var (status, body) = await _client.GetHrFileTypesRawAsync(customerId, ct);
            if (status >= 200 && status < 300)
            {
                var el = JsonSerializer.Deserialize<JsonElement>(body);
                if (el.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                    foreach (var t in data.EnumerateArray())
                    {
                        var n = t.TryGetProperty("name", out var nn) ? nn.GetString() : null;
                        var deleted = t.TryGetProperty("deleted_at", out var d) && d.ValueKind != JsonValueKind.Null;
                        if (!deleted && string.Equals(n?.Trim(), name, StringComparison.OrdinalIgnoreCase)
                            && t.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                            return id.GetInt32();
                    }
            }
            var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["name"] = name, ["mandatory"] = false, ["accept_file_types"] = new[] { "application/pdf" },
            });
            var (s2, b2) = await _client.SendRawAsync(HttpMethod.Post, $"customers/{customerId}/hr_file_types",
                () => new StringContent(payload, System.Text.Encoding.UTF8, "application/json"), ct);
            if (s2 >= 200 && s2 < 300)
            {
                var el = JsonSerializer.Deserialize<JsonElement>(b2);
                if (el.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number) return id.GetInt32();
            }
            _log.LogWarning("easy@work-Mitteilung: Dateityp «{Name}» bei Customer {Cid} nicht anlegbar ({Status}): {Body}", name, customerId, s2, Kurz(b2));
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "easy@work-Mitteilung: Dateityp-Auflösung bei Customer {Cid} fehlgeschlagen", customerId);
            return null;
        }
    }

    private static string Kurz(string s) => string.IsNullOrEmpty(s) ? "" : (s.Length > 200 ? s[..200] + "…" : s);

    private static List<int> ParseIds(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var v) ? v : 0).Where(v => v > 0).Distinct().ToList();

    private int? CurrentUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(sub, out var id) ? id : null;
    }
}
