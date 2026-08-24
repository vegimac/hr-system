using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Formular «Quellensteuer-Informationen» (Walter-Vorgabe 23.08.2026).
/// GET-only Formular-Generator — Blanko pro Filiale ODER vorbefüllt mit den
/// beim MA bekannten Daten (employeeId). Kein Lohn-Edit, im EditLock-Audit
/// unkritisch (analog BewerbungsbogenController).
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user")]
[Route("api/qst-info-formular")]
public class QstInfoFormularController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly QstInfoFormularPdfService _pdf;

    public QstInfoFormularController(AppDbContext db, QstInfoFormularPdfService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    /// <summary>GET /api/qst-info-formular/pdf?companyProfileId=…&amp;employeeId=…</summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf([FromQuery] int companyProfileId, [FromQuery] int? employeeId = null)
    {
        if (companyProfileId <= 0)
            return BadRequest(new { error = "FILIALE_FEHLT", message = "Bitte eine Filiale wählen." });

        var cp = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == companyProfileId);
        if (cp is null)
            return NotFound(new { error = "FILIALE_NICHT_GEFUNDEN", message = "Filiale nicht gefunden." });

        var street = string.Join(" ", new[] { cp.Street, cp.HouseNumber }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        var plzOrt = string.Join(" ", new[] { cp.ZipCode, cp.City }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

        QstInfoPrefill? prefill = null;
        if (employeeId is > 0)
        {
            prefill = await BuildPrefillAsync(employeeId.Value);
            if (prefill == null)
                return NotFound(new { error = "MA_NICHT_GEFUNDEN", message = "Mitarbeiter nicht gefunden." });
        }

        byte[] bytes;
        try
        {
            bytes = _pdf.Generate(new QstInfoFormularInput(
                CompanyName: string.IsNullOrWhiteSpace(cp.CompanyName) ? "Schaub Restaurants GmbH" : cp.CompanyName,
                RestaurantName: cp.BranchName,
                Strasse: string.IsNullOrWhiteSpace(street) ? null : street,
                PlzOrt: string.IsNullOrWhiteSpace(plzOrt) ? null : plzOrt,
                Telefon: string.IsNullOrWhiteSpace(cp.Phone) ? null : cp.Phone.Trim(),
                Email: string.IsNullOrWhiteSpace(cp.Email) ? null : cp.Email.Trim(),
                Prefill: prefill));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "QST_INFO_PDF_FEHLER",
                message = "Formular konnte nicht erzeugt werden.",
                detail = ex.GetBaseException().Message
            });
        }

        return File(bytes, "application/pdf", "Quellensteuer-Informationen.pdf");
    }

    /// <summary>
    /// Vorbefüllung aus den beim MA bekannten Daten (Walter 23.08.2026 v2):
    /// Person, Zivilstand/Konfession, QST-Flags (Grenzgänger/Wochenaufenthalt/
    /// Nebenerwerb), Ehepartner + Kinder aus dem Familie-Tab. Unbekanntes
    /// bleibt leer — der MA ergänzt von Hand.
    /// </summary>
    private async Task<QstInfoPrefill?> BuildPrefillAsync(int empId)
    {
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return null;

        static string? D(DateOnly? d)  => d?.ToString("dd.MM.yyyy");
        static string? Dt(DateTime? d) => d?.ToString("dd.MM.yyyy");

        // Nationalität als deutscher Name (Nationality.NameDe, Fallback Code).
        async Task<string?> NatAsync(int? natId) => natId == null ? null
            : await _db.Nationalities.AsNoTracking()
                .Where(n => n.Id == natId.Value)
                .Select(n => n.NameDe ?? n.Code)
                .FirstOrDefaultAsync();

        // Aktuellste Bewilligung (höchstes ValidTo, NULL zuerst — wie Sync).
        var permit = await _db.EmployeePermitHistories.AsNoTracking()
            .Where(h => h.EmployeeId == empId)
            .OrderByDescending(h => h.ValidTo == null)
            .ThenByDescending(h => h.ValidTo)
            .Select(h => h.PermitType != null ? h.PermitType.Code : null)
            .FirstOrDefaultAsync();

        // Zivilstand-Mapping auf die Formular-Kästchen.
        var ms = (e.MaritalStatus ?? "").Trim().ToLowerInvariant();
        string? zivilstand = ms switch
        {
            "ledig"                     => "ledig",
            "verheiratet"               => "verheiratet",
            "getrennt"                  => "verheiratet",   // rechtlich verheiratet + getrennt lebend
            "geschieden"                => "geschieden",
            "verwitwet"                 => "verwitwet",
            "eingetragene_partnerschaft"=> "eingetragen",
            _                           => null
        };
        var getrennt = ms == "getrennt" || e.SeparatedSince != null;

        string? konfession = (e.Religion ?? "").Trim().ToLowerInvariant() switch
        {
            "evangelisch_reformiert" => "ref",
            "roemisch_katholisch"    => "rk",
            "christ_katholisch"      => "ck",
            "andere"                 => "andere",
            "keine"                  => "keine",
            _                        => null
        };

        // Aktive QST-Erfassung: Grenzgänger/Wochenaufenthalt/Ausland/Nebenerwerb.
        var qst = await _db.EmployeeQuellensteuer.AsNoTracking()
            .Where(q => q.EmployeeId == empId && q.ValidTo == null)
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        string? ausland = null;
        if (qst != null && (!string.IsNullOrWhiteSpace(qst.Wohnsitzstaat) || !string.IsNullOrWhiteSpace(qst.AdresseAusland)))
            ausland = string.Join(" · ", new[] { qst.Wohnsitzstaat, qst.AdresseAusland }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        // Ehepartner + Kinder aus dem Familie-Tab.
        var familie = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => f.EmployeeId == empId && f.DateOfDeath == null)
            .Include(f => f.PermitType)
            .ToListAsync();
        var spouse = familie.FirstOrDefault(f => f.MemberType == "Ehepartner");

        string? spouseAdresse = null;
        if (spouse?.AlternativeAddressId != null)
        {
            spouseAdresse = await _db.EmployeeAddresses.AsNoTracking()
                .Where(a => a.Id == spouse.AlternativeAddressId.Value)
                .Select(a => ((a.Street ?? "") + ", " + (a.ZipCode ?? "") + " " + (a.City ?? "")).Trim())
                .FirstOrDefaultAsync();
        }

        var heute = DateTime.Today;
        var kinder = familie
            .Where(f => f.MemberType == "Kind")
            .OrderBy(f => f.DateOfBirth)
            .Select(f =>
            {
                int? alter = null;
                if (f.DateOfBirth.HasValue)
                {
                    var g = f.DateOfBirth.Value.Date;
                    alter = heute.Year - g.Year - (heute < g.AddYears(heute.Year - g.Year) ? 1 : 0);
                }
                return new QstInfoKind(
                    Name:          $"{f.LastName} {f.FirstName}".Trim(),
                    Geburtsdatum:  Dt(f.DateOfBirth),
                    Haushalt:      f.AlternativeAddressId == null ? true : false,
                    // Erstausbildung nur ab 18 relevant — darunter bleibt das
                    // Kästchen leer (der MA muss dort nichts ankreuzen).
                    Erstausbildung: alter >= 18 ? f.InErstausbildung : (bool?)null);
            })
            .ToList();

        var plzOrtKanton = string.Join(" ", new[] { e.ZipCode, e.City }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (!string.IsNullOrWhiteSpace(e.CantonCode)) plzOrtKanton = $"{plzOrtKanton} {e.CantonCode}".Trim();

        return new QstInfoPrefill(
            Personalnummer:   e.EmployeeNumber,
            NameVorname:      $"{e.LastName} {e.FirstName}".Trim(),
            Geburtsdatum:     Dt(e.DateOfBirth),
            AhvNr:            e.SocialSecurityNumber,
            Nationalitaet:    await NatAsync(e.NationalityId),
            Bewilligung:      permit,
            StrasseNr:        e.Street,
            PlzOrtKanton:     string.IsNullOrWhiteSpace(plzOrtKanton) ? null : plzOrtKanton,
            Beruf:            null,   // employee.profession ist im Model nicht gemappt — MA füllt von Hand
            Zivilstand:       zivilstand,
            GetrenntLebend:   getrennt,
            ZivilstandSeit:   D(e.MaritalStatusSince),
            GetrenntSeit:     D(e.SeparatedSince),
            Konfession:       konfession,
            Grenzgaenger:     qst?.IsGrenzgaenger == true,
            Wochenaufenthalter: qst?.IsWochenaufenthalter == true,
            AuslandAdresse:   ausland,
            WeitereErwerb:    qst?.WeitereBeschaftigungen,
            GesamtPensum:     qst?.GesamtpensumWeitereAg?.ToString("0.##"),
            PartnerName:      spouse != null ? $"{spouse.LastName} {spouse.FirstName}".Trim() : null,
            PartnerGeburtsdatum: Dt(spouse?.DateOfBirth),
            PartnerAhv:       spouse?.SocialSecurityNumber,
            PartnerNationalitaet: await NatAsync(spouse?.NationalityId),
            PartnerBewilligung: spouse?.PermitType?.Code,
            PartnerAdresse:   spouseAdresse,
            PartnerErwerb:    spouse?.Erwerbstaetig,
            PartnerArbeitgeber: spouse?.ArbeitgeberName,
            PartnerAgStrasse: spouse?.ArbeitgeberStrasse,
            PartnerAgOrt:     spouse == null ? null
                : string.Join(" ", new[] { spouse.ArbeitgeberPlz, spouse.ArbeitgeberOrt, spouse.ArbeitgeberKanton }
                    .Where(s => !string.IsNullOrWhiteSpace(s))).Trim(),
            PartnerStellenantritt: Dt(spouse?.Stellenantritt),
            PartnerArbeitskanton: spouse?.ArbeitgeberKanton,
            Kinder:           kinder);
    }
}
