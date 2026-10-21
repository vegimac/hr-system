using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// HR-Hub → Kontrolle → «FZ-Abgleich GastroSocial» (Walter 12.10.2026).
/// «Familienzulagenbescheinigung» aus dem GastroSocial-Portal gegen die Zulagen bei den Kindern
/// der MA der Sidebar-Filiale. Logik: <see cref="GastroSocialFzAbgleich"/>. Schreibt nichts
/// (ausser kind-ahv). PDF = offene Pendenzen zum Abarbeiten (Walter 21.10.2026).
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/imports/gastrosocial-fz-abgleich")]
public class GastroSocialFzAbgleichController : ControllerBase
{
    private readonly AppDbContext _db;
    public GastroSocialFzAbgleichController(AppDbContext db) => _db = db;

    [HttpPost("analyze")]
    public async Task<IActionResult> Analyze(
        [FromForm] IFormFile file,
        [FromForm] int companyProfileId = 0,
        CancellationToken ct = default)
    {
        var (err, filiale, ergebnis) = await AuswertenAsync(file, companyProfileId, ct);
        if (err != null) return err;
        return Ok(new { filiale, ergebnis });
    }

    /// <summary>PDF der offenen Pendenzen (ohne «Passt») — Checkbox zum Abhaken.</summary>
    [HttpPost("pdf")]
    public async Task<IActionResult> Pdf(
        [FromForm] IFormFile file,
        [FromForm] int companyProfileId = 0,
        CancellationToken ct = default)
    {
        var (err, filiale, ergebnis) = await AuswertenAsync(file, companyProfileId, ct);
        if (err != null) return err;
        var bytes = new GastroSocialFzAbgleichPdfService().Generate(filiale!, ergebnis!);
        var name = $"FZ-Abgleich-Pendenzen-{(filiale ?? "Filiale").Replace(' ', '-')}-{DateTime.Now:yyyy-MM-dd}.pdf";
        return File(bytes, "application/pdf", name);
    }

    public record KindAhvDto(int FamilyMemberId, string? Ahv);

    /// <summary>
    /// AHV-Nummer des Kindes aus der Bescheinigung übernehmen (Walter 12.10.2026).
    /// Nur wenn beim Kind noch keine steht — eine abweichende Nummer wird nie überschrieben.
    /// </summary>
    [HttpPost("kind-ahv")]
    public async Task<IActionResult> KindAhvUebernehmen([FromBody] KindAhvDto dto, CancellationToken ct = default)
    {
        var ahv = GastroSocialAbgleich.FormatAhv(dto.Ahv);
        if (ahv == null || !Services.Elm.ElmPlausibilitaet.AhvPruefzifferStimmt(ahv))
            return BadRequest(new { error = "AHV_UNGUELTIG", message = "AHV-Nummer ungültig (Prüfziffer)." });
        var kind = await _db.EmployeeFamilyMembers.FirstOrDefaultAsync(f => f.Id == dto.FamilyMemberId, ct);
        if (kind == null)
            return NotFound(new { error = "KIND_NICHT_GEFUNDEN", message = "Familienmitglied nicht gefunden." });
        if (!string.IsNullOrWhiteSpace(kind.SocialSecurityNumber))
        {
            if (GastroSocialAbgleich.NormAhv(kind.SocialSecurityNumber) == GastroSocialAbgleich.NormAhv(ahv))
                return Ok(new { ahv = kind.SocialSecurityNumber });
            return Conflict(new { error = "AHV_SCHON_ERFASST", message = $"Beim Kind ist bereits eine andere AHV-Nummer erfasst ({kind.SocialSecurityNumber}) — bitte im MA → Familie prüfen." });
        }
        kind.SocialSecurityNumber = ahv;
        kind.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return Ok(new { ahv });
    }

    async Task<(IActionResult? Error, string? Filiale, GastroSocialFzAbgleich.Ergebnis? Ergebnis)> AuswertenAsync(
        IFormFile? file, int companyProfileId, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return (BadRequest(new { error = "Datei fehlt." }), null, null);
        if (companyProfileId <= 0)
            return (BadRequest(new { error = "Bitte zuerst links eine Filiale wählen." }), null, null);

        var branch = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.Id == companyProfileId)
            .Select(c => new { c.Id, c.RestaurantCode, c.City, c.BranchName, c.KantonCode })
            .FirstOrDefaultAsync(ct);
        if (branch == null)
            return (BadRequest(new { error = "Filiale nicht gefunden." }), null, null);
        var filialName = !string.IsNullOrWhiteSpace(branch.BranchName) ? branch.BranchName! : branch.City ?? "";
        if (!string.IsNullOrWhiteSpace(branch.RestaurantCode)) filialName = $"{branch.RestaurantCode} {filialName}".Trim();

        List<GastroSocialFzAbgleich.Zeile> zeilen;
        try
        {
            await using var stream = file.OpenReadStream();
            zeilen = GastroSocialFzAbgleich.LiesExcel(stream, file.FileName);
        }
        catch (Exception ex)
        {
            return (BadRequest(new { error = $"Datei konnte nicht gelesen werden: {ex.Message}" }), null, null);
        }
        if (zeilen.Count == 0)
            return (BadRequest(new { error = "Keine Datenzeilen gefunden. Erwartet: GastroSocial «Familienzulagenbescheinigung» mit Spalten «Bezüger AHV-Nr.», «Kind Vorname», «Von», «Bis», «Ansatz»." }), null, null);

        var von = zeilen.Where(z => z.Von != null).Select(z => z.Von!.Value).DefaultIfEmpty(DateTime.Today).Min();
        var bis = zeilen.Where(z => z.Bis != null).Select(z => z.Bis!.Value).DefaultIfEmpty(DateTime.Today).Max();
        var monate = GastroSocialFzAbgleich.Monate(von, bis).ToList();

        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden)
            .Select(e => new
            {
                e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.SocialSecurityNumber,
                Vertraege = e.Employments.Where(em => em.CompanyProfileId == companyProfileId)
                    .Select(em => new { em.ContractStartDate, em.ContractEndDate }).ToList(),
            })
            .ToListAsync(ct);
        var kinder = (await _db.EmployeeFamilyMembers.AsNoTracking()
                .Select(f => new { f.Id, f.EmployeeId, f.MemberType, f.FirstName, f.LastName, f.SocialSecurityNumber, f.DateOfBirth, f.Gender })
                .ToListAsync(ct))
            .ToLookup(f => f.EmployeeId);
        var zulagen = (await _db.FamilyMemberAllowances.AsNoTracking()
                .Select(a => new { a.Id, a.FamilyMemberId, a.AllowanceType, a.TarifSatzNr, a.MonthlyAmount, a.ValidFrom, a.ValidTo })
                .ToListAsync(ct))
            .ToLookup(a => a.FamilyMemberId);

        var tarife = string.IsNullOrWhiteSpace(branch.KantonCode)
            ? new List<Models.FamilienzulagenTarif>()
            : await _db.FamilienzulagenTarife.AsNoTracking()
                .Where(t => t.IsActive && t.KantonCode == branch.KantonCode).ToListAsync(ct);

        var alle = emps.Select(e => new GastroSocialFzAbgleich.Ma
        {
            Id = e.Id,
            Nummer = e.EmployeeNumber,
            Vorname = e.FirstName,
            Nachname = e.LastName,
            Ahv = e.SocialSecurityNumber,
            InFiliale = e.Vertraege.Count > 0,
            VertraegeHier = e.Vertraege.Select(v => (v.ContractStartDate, v.ContractEndDate)).ToList(),
            Kinder = kinder[e.Id]
                .Where(f => zulagen.Contains(f.Id) || string.Equals(f.MemberType, "Kind", StringComparison.OrdinalIgnoreCase))
                .Select(f => new GastroSocialFzAbgleich.Kind
                {
                    Id = f.Id,
                    Vorname = f.FirstName,
                    Nachname = f.LastName,
                    Ahv = f.SocialSecurityNumber,
                    Geburt = f.DateOfBirth,
                    Geschlecht = f.Gender,
                    Zulagen = zulagen[f.Id].Select(a =>
                    {
                        static bool Laufend(string? typ) =>
                            !string.Equals(typ, "GZ", StringComparison.OrdinalIgnoreCase)
                            && !string.Equals(typ, "AdoptZ", StringComparison.OrdinalIgnoreCase);
                        var geschwister = zulagen[f.Id];
                        bool vorgaenger = geschwister.Any(o => o.Id != a.Id && Laufend(o.AllowanceType)
                            && o.ValidTo == a.ValidFrom.AddDays(-1));
                        bool nachfolger = a.ValidTo.HasValue && geschwister.Any(o => o.Id != a.Id && Laufend(o.AllowanceType)
                            && o.ValidFrom == a.ValidTo.Value.AddDays(1));
                        var betraege = new Dictionary<DateOnly, decimal>();
                        foreach (var m in monate)
                        {
                            if (a.ValidFrom > m.AddMonths(1).AddDays(-1) || (a.ValidTo != null && a.ValidTo < m)) continue;
                            var ende = m.AddMonths(1).AddDays(-1);
                            var tarif = tarife.Where(t => t.ValidFrom <= ende && (t.ValidTo == null || t.ValidTo >= m))
                                .OrderByDescending(t => t.ValidFrom).FirstOrDefault();
                            var typ = a.AllowanceType;
                            if (string.IsNullOrWhiteSpace(typ))
                                typ = f.DateOfBirth != null && f.DateOfBirth.Value.AddYears(16) <= ende.ToDateTime(TimeOnly.MinValue) ? "AZ" : "KZ";
                            var betrag = FamilienzulagenResolverService.ResolveBySatz(tarif, typ, a.TarifSatzNr).Amount ?? a.MonthlyAmount;
                            if (!Laufend(typ))
                            {
                                betraege[m] = Math.Round(betrag, 2);
                                continue;
                            }
                            var anteil = PayrollCalculations.FamzMonatsAnteil(m, a.ValidFrom, a.ValidTo, vorgaenger, nachfolger);
                            if (anteil <= 0m) continue;
                            betraege[m] = PayrollCalculations.FamzAnteilBetrag(betrag, anteil);
                        }
                        return new GastroSocialFzAbgleich.Zulage
                        {
                            Id = a.Id,
                            Typ = a.AllowanceType,
                            Satz = a.TarifSatzNr,
                            Von = a.ValidFrom,
                            Bis = a.ValidTo,
                            Betraege = betraege,
                        };
                    }).ToList(),
                }).ToList(),
        }).ToList();

        return (null, filialName, GastroSocialFzAbgleich.Vergleiche(zeilen, alle));
    }
}
