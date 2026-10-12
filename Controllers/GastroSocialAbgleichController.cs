using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// HR-Hub → Kontrolle → «AHV-Abgleich GastroSocial» (Walter 11.10.2026).
/// Liste «Mitarbeitende» aus dem GastroSocial-Portal gegen die MA der Sidebar-Filiale.
/// Logik: <see cref="GastroSocialAbgleich"/>. Schreibt nichts.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/gastrosocial-abgleich")]
public class GastroSocialAbgleichController : ControllerBase
{
    private readonly AppDbContext _db;
    public GastroSocialAbgleichController(AppDbContext db) => _db = db;

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze(
        [FromForm] IFormFile file,
        [FromForm] int companyProfileId = 0,
        CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { error = "Datei fehlt." });
        if (companyProfileId <= 0)
            return BadRequest(new { error = "Bitte zuerst links eine Filiale wählen." });

        var filialen = await _db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.RestaurantCode, c.City, c.BranchName })
            .ToListAsync(ct);
        var branch = filialen.FirstOrDefault(c => c.Id == companyProfileId);
        if (branch == null)
            return BadRequest(new { error = "Filiale nicht gefunden." });
        string FilialName(int id)
        {
            var f = filialen.FirstOrDefault(x => x.Id == id);
            if (f == null) return "anderer Filiale";
            var n = !string.IsNullOrWhiteSpace(f.BranchName) ? f.BranchName! : f.City ?? "";
            return string.IsNullOrWhiteSpace(f.RestaurantCode) ? n : $"{f.RestaurantCode} {n}".Trim();
        }

        List<GastroSocialAbgleich.Zeile> zeilen;
        try
        {
            await using var stream = file.OpenReadStream();
            zeilen = GastroSocialAbgleich.LiesExcel(stream, file.FileName);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Datei konnte nicht gelesen werden: {ex.Message}" });
        }
        if (zeilen.Count == 0)
            return BadRequest(new { error = "Keine Datenzeilen gefunden. Erwartet: GastroSocial-Liste «Mitarbeitende» mit Spalten «Name», «Vorname», «AHV-Nummer»." });

        var heute = DateTime.Today;
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden)
            .Select(e => new
            {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.SocialSecurityNumber, e.DateOfBirth, e.Gender,
                e.EntryDate, e.ExitDate, e.IsActive, e.IsPayrollExcluded,
                Vertraege = e.Employments.Select(em => new { em.CompanyProfileId, em.ContractStartDate, em.ContractEndDate }).ToList(),
            })
            .ToListAsync(ct);

        var heuteD = DateOnly.FromDateTime(heute);
        var zulagen = (await (
                from a in _db.FamilyMemberAllowances.AsNoTracking()
                join f in _db.EmployeeFamilyMembers.AsNoTracking() on a.FamilyMemberId equals f.Id
                select new { f.EmployeeId, a.FamilyMemberId, f.FirstName, a.AllowanceType, a.MonthlyAmount, a.ValidFrom, a.ValidTo })
            .ToListAsync(ct))
            .ToLookup(x => x.EmployeeId);

        var alle = emps.Select(e =>
        {
            var hier = e.Vertraege.Where(v => v.CompanyProfileId == companyProfileId).ToList();
            bool Laeuft(DateTime? ende) => ende == null || ende.Value.Date >= heute;
            bool nichtAusgetreten = e.IsActive && (e.ExitDate == null || e.ExitDate.Value.Date >= heute);
            var anderswo = nichtAusgetreten
                ? e.Vertraege.Where(v => v.CompanyProfileId != companyProfileId && v.CompanyProfileId != null && Laeuft(v.ContractEndDate))
                    .Select(v => v.CompanyProfileId!.Value).Distinct().ToList()
                : new List<int>();
            DateTime? austritt = null;
            if (hier.Count > 0 && !hier.Any(v => Laeuft(v.ContractEndDate)))
                austritt = hier.Max(v => v.ContractEndDate);
            if (e.ExitDate != null && e.ExitDate.Value.Date < heute && (austritt == null || e.ExitDate < austritt))
                austritt = e.ExitDate;
            var eintrittHier = hier.Where(v => Laeuft(v.ContractEndDate)).Select(v => (DateTime?)v.ContractStartDate).Min();
            // Nahtlose Vorgänger-Verträge (Start ≤ Ende + 1 Tag) zählen zum selben Eintritt.
            var kette = eintrittHier;
            while (kette != null)
            {
                var k = kette.Value.Date;
                var vor = hier.Where(v => v.ContractStartDate.Date < k && v.ContractEndDate != null && v.ContractEndDate.Value.Date >= k.AddDays(-1))
                    .Select(v => (DateTime?)v.ContractStartDate.Date).Min();
                if (vor == null) break;
                kette = vor;
            }
            var fz = zulagen[e.Id].ToList();
            var fzHeute = fz.Where(a => a.ValidFrom <= heuteD && (a.ValidTo == null || a.ValidTo >= heuteD)).ToList();
            var fzText = string.Join(", ", fzHeute.OrderBy(a => a.FirstName)
                .Select(a => $"{a.FirstName} {a.AllowanceType ?? "Zulage"}{(a.MonthlyAmount > 0 ? $" {a.MonthlyAmount:0.00}" : "")}".Trim()));
            return new GastroSocialAbgleich.Ma
            {
                FzKinderAktiv = fzHeute.Select(a => a.FamilyMemberId).Distinct().Count(),
                FzText = fzText.Length > 0 ? fzText : null,
                FzLetztesEnde = fzHeute.Count == 0 ? fz.Where(a => a.ValidTo != null && a.ValidTo < heuteD).Max(a => a.ValidTo) : null,
                FzErfasst = fz.Count > 0,
                Id = e.Id,
                Nummer = e.EmployeeNumber,
                Vorname = e.FirstName,
                Nachname = e.LastName,
                Ahv = e.SocialSecurityNumber,
                Geburtsdatum = e.DateOfBirth,
                Geschlecht = e.Gender,
                Eintritt = eintrittHier ?? e.EntryDate,
                EintrittKette = kette,
                InFiliale = hier.Count > 0,
                AktivInFiliale = nichtAusgetreten && hier.Any(v => Laeuft(v.ContractEndDate)),
                OhneLohn = e.IsPayrollExcluded,
                AustrittFiliale = austritt,
                AustrittGeplant = e.ExitDate != null && e.ExitDate.Value.Date >= heute ? e.ExitDate : null,
                AktivInAndererFiliale = anderswo.Count > 0 ? string.Join(", ", anderswo.Select(FilialName)) : null,
            };
        }).ToList();

        var res = GastroSocialAbgleich.Vergleiche(zeilen, alle, heute);
        return Ok(new { filiale = FilialName(companyProfileId), ergebnis = res });
    }
}
