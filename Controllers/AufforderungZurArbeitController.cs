using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Aufforderung zur Arbeit (Walter 30.07.2026). Brief an MA, die unentschuldigt
/// der Arbeit fernbleiben — analog Kündigungsschreiben (Preview/PDF, keine
/// Stammdaten-Mutation). GF (user) inkl. aus Restaurant Admin.
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/aufforderung-arbeit")]
public class AufforderungZurArbeitController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AufforderungZurArbeitPdfService _pdf;

    public AufforderungZurArbeitController(AppDbContext db, AufforderungZurArbeitPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    public class AufforderungPdfDto
    {
        public DateOnly? Datum { get; set; }
        public DateOnly? FristBis { get; set; }
        public string?   Ort { get; set; }
        public string?   KontaktName { get; set; }
        public string?   KontaktTelefon { get; set; }
        public string?   KontaktFunktion { get; set; }
        public bool      Eingeschrieben { get; set; }
        /// <summary>User-Id des Unterzeichners (Filial-Zugang). Null = eingeloggter User.</summary>
        public int?      SignerUserId { get; set; }
    }

    [HttpGet("{empId:int}/info")]
    public async Task<IActionResult> GetInfo(int empId)
    {
        var guard = await GuardBranchAsync(empId);
        if (guard != null) return guard;
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, _, cp) = ctx.Value;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var (kontaktName, kontaktFunktion, kontaktTel) = await ResolveKontaktAsync(cp?.Id);
        var signers = await ListSignersAsync(cp?.Id);
        var loggedInId = CurrentUserId();
        var defaultSignerId = signers
            .Select(s => (int?)s.UserId)
            .FirstOrDefault(id => id == loggedInId)
            ?? signers.FirstOrDefault(s => s.IsDefault)?.UserId
            ?? signers.FirstOrDefault()?.UserId;

        return Ok(new
        {
            employee = new
            {
                id = e.Id,
                name = $"{e.FirstName} {e.LastName}".Trim(),
                anrede = ResolveAnrede(e),
                gutenTagAnrede = GutenTagAnrede(e),
                strasse = e.Street,
                plzOrt = Join(e.ZipCode, e.City),
            },
            company = new
            {
                name = cp?.CompanyName,
                restaurant = cp?.BranchName,
                strasse = Join(cp?.Street, cp?.HouseNumber),
                plzOrt = Join(cp?.ZipCode, cp?.City),
                // Brief-Ort ohne Kantons-Suffix («Reinach (AG)» → «Reinach»).
                ort = EasyAtWorkEmployeeSyncService.StripCityCantonSuffix(cp?.City) ?? cp?.City,
                phone = cp?.Phone,
            },
            datum = today.ToString("yyyy-MM-dd"),
            fristBis = today.AddDays(10).ToString("yyyy-MM-dd"),
            kontaktName,
            kontaktFunktion,
            kontaktTelefon = kontaktTel,
            signers = signers.Select(s => new
            {
                userId = s.UserId,
                name = s.Name,
                funktion = s.Funktion,
                hasSignature = s.HasSignature,
                isDefault = s.IsDefault,
            }),
            defaultSignerUserId = defaultSignerId,
        });
    }

    [HttpPost("{empId:int}/pdf")]
    public async Task<IActionResult> GetPdf(int empId, [FromBody] AufforderungPdfDto dto)
    {
        var guard = await GuardBranchAsync(empId);
        if (guard != null) return guard;
        var ctx = await LoadContextAsync(empId);
        if (ctx is null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var (e, _, cp) = ctx.Value;

        var today = DateOnly.FromDateTime(DateTime.Today);
        var datum = dto.Datum ?? today;
        var frist = dto.FristBis ?? today.AddDays(10);
        if (frist < datum)
            return BadRequest(new { error = "FRIST_VOR_DATUM",
                message = "Die Meldefrist darf nicht vor dem Briefdatum liegen." });

        // Walter 30.07.2026: «Reinach (AG)» → «Reinach» auf dem Brief.
        var ortRaw = string.IsNullOrWhiteSpace(dto.Ort) ? (cp?.City ?? "") : dto.Ort!.Trim();
        var ort = EasyAtWorkEmployeeSyncService.StripCityCantonSuffix(ortRaw) ?? ortRaw;
        var (defName, defFunktion, defTel) = await ResolveKontaktAsync(cp?.Id);
        var kontaktName = string.IsNullOrWhiteSpace(dto.KontaktName) ? (defName ?? "") : dto.KontaktName!.Trim();
        if (string.IsNullOrWhiteSpace(kontaktName))
            return BadRequest(new { error = "KONTAKT_FEHLT",
                message = "Bitte den Namen des Restaurantleiters / der Kontaktperson angeben." });
        var kontaktTel = string.IsNullOrWhiteSpace(dto.KontaktTelefon) ? defTel : dto.KontaktTelefon!.Trim();
        var kontaktFunktion = string.IsNullOrWhiteSpace(dto.KontaktFunktion)
            ? (defFunktion ?? "Restaurantleiter")
            : dto.KontaktFunktion!.Trim();

        var (sigPng, signerName, signerFunktion) = await ResolveSignerAsync(cp?.Id, dto.SignerUserId);
        if (string.IsNullOrWhiteSpace(signerName))
            return BadRequest(new { error = "SIGNER_FEHLT",
                message = "Bitte einen Unterzeichner wählen (Filial-Zugang mit Name)." });

        var data = new AufforderungZurArbeitPdfService.AufforderungData(
            FirmaName: cp?.CompanyName,
            RestaurantName: cp?.BranchName,
            FirmaStrasse: Join(cp?.Street, cp?.HouseNumber),
            FirmaPlzOrt: Join(cp?.ZipCode, cp?.City),
            MaAnrede: ResolveAnrede(e),
            MaName: $"{e.FirstName} {e.LastName}".Trim(),
            MaStrasse: e.Street,
            MaPlzOrt: Join(e.ZipCode, e.City),
            GutenTagAnrede: GutenTagAnrede(e),
            Ort: ort,
            Datum: datum,
            FristBis: frist,
            KontaktName: kontaktName,
            KontaktTelefon: kontaktTel,
            KontaktFunktion: kontaktFunktion,
            UnterzeichnerName: signerName,
            UnterzeichnerFunktion: signerFunktion,
            Eingeschrieben: dto.Eingeschrieben);

        try
        {
            var bytes = _pdf.Generate(data, sigPng);
            return File(bytes, "application/pdf", $"{e.EmployeeNumber}-Aufforderung-zur-Arbeit.pdf");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "PDF_FEHLER", message = ex.GetBaseException().Message });
        }
    }

    // ── Helfer ──────────────────────────────────────────────────────────────

    private async Task<IActionResult?> GuardBranchAsync(int empId)
    {
        if (User.IsInRole("admin")) return null;
        var restricted = User.IsInRole("buchhaltung") || !User.IsInRole("superuser");
        if (!restricted) return null;

        var cpId = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .Select(em => em.CompanyProfileId)
            .FirstOrDefaultAsync();
        if (cpId == null)
            return StatusCode(403, new { error = "BRANCH_REQUIRED",
                message = "Dieser Mitarbeiter hat keine Filial-Zuordnung — Zugriff nur für Admin/HR." });

        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idStr, out var uid))
            return StatusCode(403, new { error = "NO_USER" });
        var ok = await _db.UserBranchAccesses
            .AnyAsync(a => a.UserId == uid && a.CompanyProfileId == cpId.Value);
        if (!ok)
            return StatusCode(403, new { error = "BRANCH_FORBIDDEN",
                message = "Kein Zugriff auf die Filiale dieses Mitarbeiters." });
        return null;
    }

    private async Task<(Employee e, Employment? emp, CompanyProfile? cp)?> LoadContextAsync(int empId)
    {
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return null;

        var emp = await _db.Employments.AsNoTracking()
            .Where(em => em.EmployeeId == empId)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync();

        CompanyProfile? cp = null;
        if (emp?.CompanyProfileId != null)
            cp = await _db.CompanyProfiles.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == emp.CompanyProfileId.Value);

        return (e, emp, cp);
    }

    /// <summary>
    /// Default-Kontakt = Restaurantleiter / GF der Filiale (wie Aufhebung),
    /// Telefon aus AppUser.Phone, Fallback Filial-Telefon.
    /// </summary>
    private async Task<(string? name, string? funktion, string? telefon)> ResolveKontaktAsync(int? companyProfileId)
    {
        if (!companyProfileId.HasValue) return (null, null, null);

        var list = await _db.UserBranchAccesses.AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.CompanyProfileId == companyProfileId.Value && a.User != null)
            .OrderBy(a => a.Id)
            .ToListAsync();

        static bool TitleLooksLikeRl(string? t)
        {
            if (string.IsNullOrWhiteSpace(t)) return false;
            var s = t.Trim().ToLowerInvariant();
            return s.Contains("restaurantleiter") || s.Contains("geschäftsführer")
                || s.Contains("geschaeftsfuehrer") || s.Contains("filialleiter");
        }

        var pick = list.FirstOrDefault(a => a.Role == "GESCHAEFTSFUEHRER" && a.User!.IsActive)
                ?? list.FirstOrDefault(a => a.IsDefault && a.User!.IsActive)
                ?? list.FirstOrDefault(a => TitleLooksLikeRl(a.FunctionTitle) && a.User!.IsActive)
                ?? list.FirstOrDefault(a => a.Role == "GESCHAEFTSFUEHRER")
                ?? list.FirstOrDefault(a => a.IsDefault)
                ?? list.FirstOrDefault(a => TitleLooksLikeRl(a.FunctionTitle));

        string? name = null;
        string? funktion = null;
        string? tel = null;
        if (pick?.User != null)
        {
            var full = $"{pick.User.FirstName} {pick.User.LastName}".Trim();
            name = string.IsNullOrWhiteSpace(full) ? pick.User.Username : full;
            funktion = !string.IsNullOrWhiteSpace(pick.FunctionTitle)
                ? pick.FunctionTitle!.Trim()
                : "Restaurantleiter";
            tel = string.IsNullOrWhiteSpace(pick.User.Phone) ? null : pick.User.Phone.Trim();
        }

        if (string.IsNullOrWhiteSpace(tel))
        {
            var cpPhone = await _db.CompanyProfiles.AsNoTracking()
                .Where(c => c.Id == companyProfileId.Value)
                .Select(c => c.Phone)
                .FirstOrDefaultAsync();
            tel = string.IsNullOrWhiteSpace(cpPhone) ? null : cpPhone.Trim();
        }

        return (name, funktion, tel);
    }

    private sealed record SignerOption(int UserId, string Name, string? Funktion, bool HasSignature, bool IsDefault);

    private async Task<List<SignerOption>> ListSignersAsync(int? companyProfileId)
    {
        if (!companyProfileId.HasValue) return new List<SignerOption>();

        var rows = await _db.UserBranchAccesses.AsNoTracking()
            .Include(a => a.User)
            .Where(a => a.CompanyProfileId == companyProfileId.Value
                     && a.User != null && a.User.IsActive)
            .OrderByDescending(a => a.IsDefault)
            .ThenBy(a => a.User!.FirstName)
            .ThenBy(a => a.User!.LastName)
            .ToListAsync();

        // Ein User kann mehrfach zugewiesen sein — einmal pro User.
        var seen = new HashSet<int>();
        var list = new List<SignerOption>();
        foreach (var a in rows)
        {
            if (a.User == null || !seen.Add(a.UserId)) continue;
            var full = $"{a.User.FirstName} {a.User.LastName}".Trim();
            var name = string.IsNullOrWhiteSpace(full) ? a.User.Username : full;
            var funktion = !string.IsNullOrWhiteSpace(a.FunctionTitle)
                ? a.FunctionTitle!.Trim()
                : (a.Role == "GESCHAEFTSFUEHRER" ? "Geschäftsführer/in"
                    : a.Role == "HR_VERANTWORTLICH" ? "HR-Verantwortliche/r" : null);
            list.Add(new SignerOption(
                a.UserId,
                name,
                funktion,
                a.User.SignaturePng is { Length: > 0 },
                a.IsDefault));
        }
        return list;
    }

    /// <summary>
    /// Unterzeichner wählen: explizite User-Id (muss Filial-Zugang haben)
    /// oder Fallback eingeloggter User.
    /// </summary>
    private async Task<(byte[]? png, string? name, string? funktion)> ResolveSignerAsync(
        int? companyProfileId, int? signerUserId)
    {
        var uid = signerUserId ?? CurrentUserId();
        if (!uid.HasValue) return (null, null, null);

        if (companyProfileId.HasValue)
        {
            var uba = await _db.UserBranchAccesses.AsNoTracking()
                .Include(a => a.User)
                .Where(a => a.CompanyProfileId == companyProfileId.Value
                         && a.UserId == uid.Value
                         && a.User != null && a.User.IsActive)
                .OrderByDescending(a => a.IsDefault)
                .FirstOrDefaultAsync();
            if (uba?.User == null)
            {
                // Gewählter User hat keinen Zugang zu dieser Filiale.
                if (signerUserId.HasValue)
                    return (null, null, null);
            }
            else
            {
                var full = $"{uba.User.FirstName} {uba.User.LastName}".Trim();
                var name = string.IsNullOrWhiteSpace(full) ? uba.User.Username : full;
                var funktion = !string.IsNullOrWhiteSpace(uba.FunctionTitle)
                    ? uba.FunctionTitle!.Trim() : null;
                return (uba.User.SignaturePng, name, funktion);
            }
        }

        // Fallback: eingeloggter User ohne Filial-Match (admin/HR global).
        var u = await _db.AppUsers.AsNoTracking()
            .Where(x => x.Id == uid.Value)
            .Select(x => new { x.SignaturePng, x.FirstName, x.LastName, x.Username })
            .FirstOrDefaultAsync();
        if (u == null) return (null, null, null);
        var fullName = $"{u.FirstName} {u.LastName}".Trim();
        return (u.SignaturePng, string.IsNullOrWhiteSpace(fullName) ? u.Username : fullName, null);
    }

    private int? CurrentUserId()
    {
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idStr, out var uid) ? uid : null;
    }

    private static string? ResolveAnrede(Employee e)
    {
        var anrede = !string.IsNullOrWhiteSpace(e.Salutation) ? e.Salutation!.Trim()
            : (e.Gender == "female" ? "Frau" : e.Gender == "male" ? "Herr" : "");
        if (string.Equals(anrede, "Divers", StringComparison.OrdinalIgnoreCase)
            || string.Equals(anrede, "Diverse", StringComparison.OrdinalIgnoreCase))
            return null;
        return string.IsNullOrWhiteSpace(anrede) ? null : anrede;
    }

    /// <summary>«Guten Tag Frau Duqi» / «Guten Tag Herr Muster» — Vorlage-Form.</summary>
    private static string GutenTagAnrede(Employee e)
    {
        var anrede = ResolveAnrede(e);
        var ln = (e.LastName ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(anrede) && ln.Length > 0)
            return $"Guten Tag {anrede} {ln}";
        if (ln.Length > 0) return $"Guten Tag {ln}";
        var fn = (e.FirstName ?? "").Trim();
        if (fn.Length > 0) return $"Guten Tag {fn}";
        return "Guten Tag";
    }

    private static string? Join(string? a, string? b)
    {
        var s = string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }
}
