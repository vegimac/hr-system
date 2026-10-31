using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace HrSystem.Controllers;

/// <summary>
/// Versicherungs-Codes eines Mitarbeiters (Walter 07.09.2026, Swissdec-Lösungen):
/// UVG / UVGZ / KTG / BVG, versioniert (ab/bis). Ohne Eintrag gilt der Standard
/// aus den SV-Sätzen — GET liefert deshalb pro Art auch den EFFEKTIVEN Code und
/// die wählbaren Codes (aus social_insurance_rate.loesungs_code).
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/versicherung-codes")]
[Authorize(Roles = "admin,superuser,user")]
public class EmployeeVersicherungCodeController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnEditLockService _editLock;
    public EmployeeVersicherungCodeController(AppDbContext db, LohnEditLockService editLock)
    { _db = db; _editLock = editLock; }

    public record UpsertDto(string Art, string? Code, DateOnly ValidFrom, DateOnly? ValidTo,
                            decimal? BeitragFixAn, decimal? BeitragFixAg, string? Bemerkung,
                            string? BvgEintrittsgrund = null, bool? BvgVollArbeitsfaehig = null, decimal? BvgBasisManuell = null,
                            bool Zusaetzlich = false,   // true = bisherige Codes derselben Art bleiben (z.B. KTG 11 + 12)
                            int? EmpfaengerId = null);  // UVG: welcher Versicherer für diese Person

    private static readonly string[] Arten = { "UVG", "UVGZ", "KTG", "BVG", "AHV" };

    [HttpGet]
    public async Task<IActionResult> List(int employeeId, [FromQuery] DateOnly? stichtag)
    {
        var tag = stichtag ?? DateOnly.FromDateTime(DateTime.Today);
        var eintraege = await _db.EmployeeVersicherungCodes.AsNoTracking()
            .Where(v => v.EmployeeId == employeeId)
            .OrderBy(v => v.Art).ThenByDescending(v => v.ValidFrom)
            .ToListAsync();
        var branchId = await GetEmployeeBranchAsync(employeeId);
        var renten = await VerzichtLageAsync(employeeId);
        var saetze = await _db.SocialInsuranceRates.AsNoTracking()
            .Where(r => r.IsActive && r.LoesungsCode != null
                     && (r.CompanyProfileId == null || r.CompanyProfileId == branchId))
            .ToListAsync();

        var arten = Arten.Select(art =>
        {
            var svCodes = EmployeeVersicherungCode.SvCodesFuer(art);
            var optionen = art == EmployeeVersicherungCode.ArtAhv
                // AHV/ALV kennt keine Lösungs-Codes in den SV-Sätzen — Abweichungen: Sonderfall, Freibetrag-Verzicht.
                ? new[]
                {
                    new { code = EmployeeVersicherungCode.CodeSonderfall, name = "AHV/ALV-Sonderfall – nicht beitragspflichtig (z.B. Versicherung im Ausland, A1)", istStandard = false },
                    new { code = EmployeeVersicherungCode.CodeFreibetragVerzicht, name = "MA wünscht Verzicht auf AHV-Freibetrag 1'400/Mt. (erst ab Referenzalter, voller Lohn)", istStandard = false },
                }.ToList()
                : saetze.Where(s => svCodes.Contains(s.Code, StringComparer.OrdinalIgnoreCase))
                .GroupBy(s => s.LoesungsCode!.ToUpperInvariant())
                .Select(g => new {
                    code = g.Key,
                    name = g.OrderByDescending(x => x.ValidFrom).First().Name,
                    istStandard = g.Any(x => x.IsDefaultCode),
                })
                .OrderBy(o => o.code).ToList();
            var amTag = eintraege.Where(e => e.Art == art && e.GiltAm(tag)).ToList();
            var explizit = art == "BVG"
                ? (PayrollCalculations.WaehleBvgFix(amTag) ?? amTag.FirstOrDefault())
                : amTag.FirstOrDefault();
            var alleCodes = amTag.Where(e => !string.IsNullOrWhiteSpace(e.Code)).Select(e => e.Code!).Distinct().ToList();
            var betriebsteile = art != "UVG"
                ? new List<object>()
                : saetze.Where(s => svCodes.Contains(s.Code, StringComparer.OrdinalIgnoreCase)
                                 && !string.IsNullOrWhiteSpace(s.LoesungsCode))
                    .Select(s => new { Satz = s, C = s.LoesungsCode!.Trim().ToUpperInvariant() })
                    .Where(x => x.C.Length > 0 && x.C[0] >= 'A' && x.C[0] <= 'Z')
                    .GroupBy(x => x.C[0])
                    .OrderBy(g => g.Key)
                    .Select(g => (object)new
                    {
                        code = g.Key.ToString(),
                        name = g.Select(x => x.Satz.Description).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)) ?? ""
                    })
                    .ToList();
            return new
            {
                art,
                effektiverCode = PayrollCalculations.EffektiverCode(art, eintraege, saetze, tag),
                weitereCodes = alleCodes.Skip(1).ToList(),
                betriebsteile,
                herkunft = explizit?.Code != null ? "manuell" : art == EmployeeVersicherungCode.ArtAhv ? "beitragspflichtig" : (optionen.Any(o => o.istStandard) ? "standard" : (optionen.Count == 0 ? "keine Lösungen erfasst" : "ohne Code")),
                explizit = explizit == null ? null : new { explizit.Id, explizit.Code, explizit.ValidFrom, explizit.ValidTo, explizit.BeitragFixAn, explizit.BeitragFixAg, explizit.Bemerkung,
                                                           explizit.BvgEintrittsgrund, explizit.BvgVollArbeitsfaehig, explizit.BvgBasisManuell, explizit.EmpfaengerId },
                optionen,
            };
        }).ToList();

        return Ok(new
        {
            stichtag = tag,
            arten,
            eintraege = eintraege.Select(e => new { e.Id, e.Art, e.Code, e.ValidFrom, e.ValidTo, e.BeitragFixAn, e.BeitragFixAg, e.Bemerkung, e.CreatedAt,
                                                    e.BvgEintrittsgrund, e.BvgVollArbeitsfaehig, e.BvgBasisManuell, e.EmpfaengerId,
                                                    isCurrent = e.GiltAm(tag) }),
            uvgVersicherer = await OffeneUvgVersichererAsync(branchId, tag),
            imRentenalter = renten.Erreicht,
            darfVerzicht = renten.Darf,
            verzichtHinweis = renten.Hinweis,
            verzicht = new
            {
                jahr = renten.Jahr,
                gesperrt = renten.Gesperrt,
                naechstesJahr = renten.Jahr + 1,
                naechstesGesperrt = renten.NaechstesGesperrt,
                ersterTag = renten.ErsterTag?.ToString("yyyy-MM-dd"),
                freibetragMonate = renten.FreibetragMonate,
            },
            rentenalterStichtag = renten.Heute,
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] UpsertDto dto)
    {
        if (!await _db.Employees.AnyAsync(e => e.Id == employeeId)) return NotFound();
        var fehler = Pruefe(dto);
        if (fehler != null) return BadRequest(new { error = "UNGUELTIG", message = fehler });
        var uvgCode = await PruefeUvgCodeAsync(employeeId, dto);
        if (uvgCode != null) return uvgCode;
        var verzicht = await PruefeVerzichtAlterAsync(employeeId, dto);
        if (verzicht != null) return verzicht;
        var versichererFehler = await PruefeUvgVersichererAsync(employeeId, dto);
        if (versichererFehler != null) return BadRequest(new { error = "UVG_VERSICHERER", message = versichererFehler });

        var lock1 = await PruefeEditLockAsync(employeeId, dto.ValidFrom);
        if (lock1 != null) return lock1;

        var neuCode = Norm(dto.Code);
        var offeneQ = _db.EmployeeVersicherungCodes
            .Where(v => v.EmployeeId == employeeId && v.Art == dto.Art
                     && (v.ValidTo == null || v.ValidTo >= dto.ValidFrom) && v.ValidFrom < dto.ValidFrom);
        if (dto.Art == EmployeeVersicherungCode.ArtAhv && neuCode != null)
            offeneQ = offeneQ.Where(v => v.Code == neuCode);
        var offene = dto.Zusaetzlich ? new List<EmployeeVersicherungCode>() : await offeneQ.ToListAsync();
        foreach (var o in offene) o.ValidTo = dto.ValidFrom.AddDays(-1);
        var gleich = await _db.EmployeeVersicherungCodes
            .AnyAsync(v => v.EmployeeId == employeeId && v.Art == dto.Art && v.ValidFrom == dto.ValidFrom && v.Code == neuCode);
        if (gleich) return Conflict(new { error = "CODE_BEREITS_AB_DATUM", message = $"Für {dto.Art} gibt es bereits einen Eintrag ab {dto.ValidFrom:dd.MM.yyyy}." });

        var e = new EmployeeVersicherungCode
        {
            EmployeeId = employeeId, Art = dto.Art, Code = Norm(dto.Code),
            ValidFrom = dto.ValidFrom, ValidTo = dto.ValidTo,
            BeitragFixAn = dto.Art == "BVG" && dto.BeitragFixAn is > 0 ? dto.BeitragFixAn : null,
            BeitragFixAg = dto.Art == "BVG" && dto.BeitragFixAg is > 0 ? dto.BeitragFixAg : null,
            Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            EmpfaengerId = dto.Art == "UVG" ? dto.EmpfaengerId : null,
            CreatedAt = DateTime.Now, CreatedBy = GetCurrentUserId(),
        };
        UebernehmeBvg(e, dto);
        _db.EmployeeVersicherungCodes.Add(e);
        await _db.SaveChangesAsync();
        return Ok(new { id = e.Id, vorgaengerBeendet = offene.Count });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] UpsertDto dto)
    {
        var e = await _db.EmployeeVersicherungCodes.FirstOrDefaultAsync(v => v.Id == id && v.EmployeeId == employeeId);
        if (e == null) return NotFound();
        var fehler = Pruefe(dto);
        if (fehler != null) return BadRequest(new { error = "UNGUELTIG", message = fehler });
        var uvgCode = await PruefeUvgCodeAsync(employeeId, dto);
        if (uvgCode != null) return uvgCode;
        var verzicht = await PruefeVerzichtAlterAsync(employeeId, dto, e);
        if (verzicht != null) return verzicht;
        var versichererFehler = await PruefeUvgVersichererAsync(employeeId, dto);
        if (versichererFehler != null) return BadRequest(new { error = "UVG_VERSICHERER", message = versichererFehler });
        var lock1 = await PruefeEditLockAsync(employeeId, dto.ValidFrom < e.ValidFrom ? dto.ValidFrom : e.ValidFrom);
        if (lock1 != null) return lock1;

        e.Art = dto.Art; e.Code = Norm(dto.Code); e.ValidFrom = dto.ValidFrom; e.ValidTo = dto.ValidTo;
        e.BeitragFixAn = dto.Art == "BVG" && dto.BeitragFixAn is > 0 ? dto.BeitragFixAn : null;
        e.BeitragFixAg = dto.Art == "BVG" && dto.BeitragFixAg is > 0 ? dto.BeitragFixAg : null;
        e.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        e.EmpfaengerId = dto.Art == "UVG" ? dto.EmpfaengerId : null;
        UebernehmeBvg(e, dto);
        await _db.SaveChangesAsync();
        return Ok(new { id = e.Id });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var e = await _db.EmployeeVersicherungCodes.FirstOrDefaultAsync(v => v.Id == id && v.EmployeeId == employeeId);
        if (e == null) return NotFound();
        var verzicht = await PruefeVerzichtAlterAsync(employeeId, null, e);
        if (verzicht != null) return verzicht;
        var lock1 = await PruefeEditLockAsync(employeeId, e.ValidFrom);
        if (lock1 != null) return lock1;
        var reaktiviert = await VorgaengerWiederOeffnenAsync(employeeId, e);
        _db.EmployeeVersicherungCodes.Remove(e);
        await _db.SaveChangesAsync();
        return Ok(new { reaktiviert });
    }

    /// <summary>
    /// Die abgeschlossene Zeile gilt wieder ohne Enddatum.
    /// Ein Nachfolger, der in diese Zeit reicht, bleibt — den zuerst löschen,
    /// das Löschen öffnet den Vorgänger von selbst.
    /// </summary>
    [HttpPost("{id:int}/wieder-oeffnen")]
    public async Task<IActionResult> WiederOeffnen(int employeeId, int id)
    {
        var e = await _db.EmployeeVersicherungCodes.FirstOrDefaultAsync(v => v.Id == id && v.EmployeeId == employeeId);
        if (e == null) return NotFound();
        if (e.ValidTo == null) return Ok(new { id = e.Id, bereitsOffen = true });

        var ab = e.ValidTo.Value.AddDays(1);
        var lock1 = await PruefeEditLockAsync(employeeId, ab);
        if (lock1 != null) return lock1;

        var andere = await _db.EmployeeVersicherungCodes
            .Where(v => v.EmployeeId == employeeId && v.Art == e.Art && v.Id != e.Id)
            .ToListAsync();
        var block = andere.FirstOrDefault(v =>
            VersicherungNachfolge.GleicherSchluss(e.Art, e.Code, v.Code)
            && VersicherungNachfolge.BlockiertWiederOeffnen(e.ValidFrom, v.ValidTo));
        if (block != null)
            return Conflict(new
            {
                error = "NACHFOLGER",
                message = $"Ab {block.ValidFrom:dd.MM.yyyy} gilt bereits ein anderer Eintrag. Diesen zuerst löschen, dann gilt der ältere wieder."
            });

        if (e.IstAhvFreibetragVerzicht)
        {
            var verzicht = await PruefeVerzichtWiederOeffnenAsync(employeeId, e);
            if (verzicht != null) return verzicht;
        }

        e.ValidTo = null;
        await _db.SaveChangesAsync();
        return Ok(new { id = e.Id });
    }

    private async Task<int> VorgaengerWiederOeffnenAsync(int employeeId, EmployeeVersicherungCode geloescht)
    {
        var andere = await _db.EmployeeVersicherungCodes
            .Where(v => v.EmployeeId == employeeId && v.Art == geloescht.Art && v.Id != geloescht.Id)
            .ToListAsync();
        var naechster = andere
            .Where(v => v.ValidFrom > geloescht.ValidFrom && VersicherungNachfolge.GleicherSchluss(geloescht.Art, geloescht.Code, v.Code))
            .OrderBy(v => v.ValidFrom)
            .FirstOrDefault();
        var n = 0;
        foreach (var p in andere)
        {
            if (!VersicherungNachfolge.GleicherSchluss(geloescht.Art, geloescht.Code, p.Code)) continue;
            if (!VersicherungNachfolge.WurdeDurchNachfolgerBeendet(p.ValidTo, geloescht.ValidFrom)) continue;
            if (p.ValidFrom >= geloescht.ValidFrom) continue;
            var ende = VersicherungNachfolge.EndeNachLoeschen(geloescht.ValidTo, naechster?.ValidFrom);
            if (ende != null && ende < p.ValidFrom) continue;
            p.ValidTo = ende;
            n++;
        }
        return n;
    }

    private async Task<IActionResult?> PruefeVerzichtWiederOeffnenAsync(int employeeId, EmployeeVersicherungCode e)
    {
        var lage = await VerzichtLageAsync(employeeId);
        if (!lage.Darf)
            return BadRequest(new { error = "NICHT_IM_RENTENALTER", message = lage.Hinweis });
        var bestehende = await _db.EmployeeVersicherungCodes.AsNoTracking()
            .Where(v => v.EmployeeId == employeeId
                     && v.Art == EmployeeVersicherungCode.ArtAhv
                     && v.Code == EmployeeVersicherungCode.CodeFreibetragVerzicht)
            .ToListAsync();
        var vorher = bestehende.Select(x => new AhvFreibetragEntscheid.Spanne(x.ValidFrom, x.ValidTo)).ToList();
        var nachher = bestehende.Select(x =>
            x.Id == e.Id
                ? new AhvFreibetragEntscheid.Spanne(x.ValidFrom, null)
                : new AhvFreibetragEntscheid.Spanne(x.ValidFrom, x.ValidTo)).ToList();
        var fehler = AhvFreibetragEntscheid.Pruefe(vorher, nachher, lage.GesperrteJahre, lage.Eintritt, lage.Folgemonat);
        if (fehler != null)
            return BadRequest(new { error = "VERZICHT_JAHR", message = fehler });
        return null;
    }

    // ── Helfer ──────────────────────────────────────────────────────────
    private static string? Norm(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();

    private static void UebernehmeBvg(EmployeeVersicherungCode e, UpsertDto dto)
    {
        var bvg = dto.Art == "BVG";
        e.BvgEintrittsgrund    = bvg && !string.IsNullOrWhiteSpace(dto.BvgEintrittsgrund) ? dto.BvgEintrittsgrund.Trim() : null;
        e.BvgVollArbeitsfaehig = bvg ? dto.BvgVollArbeitsfaehig : null;
        e.BvgBasisManuell      = bvg && dto.BvgBasisManuell is > 0 ? dto.BvgBasisManuell : null;
    }

    private static string? Pruefe(UpsertDto dto)
    {
        if (!Arten.Contains(dto.Art)) return "Art muss UVG, UVGZ, KTG, BVG oder AHV sein.";
        if (dto.Art == EmployeeVersicherungCode.ArtAhv)
        {
            var ahv = (dto.Code ?? "").Trim().ToUpperInvariant();
            if (ahv != EmployeeVersicherungCode.CodeSonderfall && ahv != EmployeeVersicherungCode.CodeFreibetragVerzicht)
                return "Bei AHV nur SONDERFALL (nicht beitragspflichtig) oder VERZICHT (kein Freibetrag 1'400) — Normalfall = kein Eintrag.";
        }
        if (dto.ValidTo != null && dto.ValidTo < dto.ValidFrom) return "«Gültig bis» liegt vor «Gültig ab».";
        var hatCode = !string.IsNullOrWhiteSpace(dto.Code);
        var hatFix  = dto.Art == "BVG" && (dto.BeitragFixAn is > 0 || dto.BeitragFixAg is > 0);
        if (dto.Art != "UVG" && !hatCode && !hatFix)
            return dto.Art == "BVG" ? "Code oder fester Beitrag angeben." : "Code angeben.";
        return null;
    }

    private async Task<IActionResult?> PruefeUvgCodeAsync(int employeeId, UpsertDto dto)
    {
        if (dto.Art != "UVG") return null;
        var branchId = await GetEmployeeBranchAsync(employeeId);
        var zeilen = await _db.SocialInsuranceRates.AsNoTracking()
            .Where(r => r.IsActive && r.LoesungsCode != null
                     && (r.CompanyProfileId == null || r.CompanyProfileId == branchId))
            .Select(r => new { r.Code, r.LoesungsCode })
            .ToListAsync();
        var codes = zeilen
            .Where(r => string.Equals(r.Code, "NBUV", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(r.Code, "BUV", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.LoesungsCode);
        var fehler = UvgLoesung.PersonenCodeFehler(dto.Code, codes);
        if (fehler == null) return null;
        return BadRequest(new { error = "UVG_CODE", message = fehler });
    }

    private async Task<IActionResult?> PruefeEditLockAsync(int employeeId, DateOnly validFrom)
    {
        var branchId = await GetEmployeeBranchAsync(employeeId);
        var firstAllowed = branchId.HasValue ? await _editLock.GetFirstAllowedDateAsync(User, branchId.Value) : null;
        if (firstAllowed.HasValue && validFrom < firstAllowed.Value)
            return Conflict(new
            {
                error = "LOHN_EDIT_LOCKED",
                message = $"'Gültig ab {validFrom:dd.MM.yyyy}' liegt in einer bereits in Verarbeitung befindlichen Lohnperiode. Frühestes erlaubtes 'Gültig ab': {firstAllowed.Value:dd.MM.yyyy}.",
                firstAllowedDate = firstAllowed.Value.ToString("yyyy-MM-dd")
            });
        return null;
    }

    private async Task<string?> PruefeUvgVersichererAsync(int employeeId, UpsertDto dto)
    {
        if (dto.Art != "UVG") return null;
        var branchId = await GetEmployeeBranchAsync(employeeId);
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var offen = await OffeneUvgVersichererAsync(branchId, heute);
        var fehlt = UvgMehrereVersicherer.ZuweisungFehlt(offen.Count, dto.EmpfaengerId);
        if (fehlt != null) return fehlt;
        if (dto.EmpfaengerId is not int id) return null;
        var art = await _db.LohndatenEmpfaengers.AsNoTracking()
            .Where(e => e.Id == id).Select(e => e.Art).FirstOrDefaultAsync();
        if (art == null) return "UVG-Versicherer nicht gefunden.";
        if (!string.Equals(art, "UVG", StringComparison.OrdinalIgnoreCase))
            return "Die Zuweisung muss ein UVG-Versicherer sein.";
        if (branchId == null) return "Die Person hat keine Filiale — den UVG-Versicherer dort zuerst zuordnen.";
        var zeilen = await _db.CompanyProfileEmpfaengers.AsNoTracking()
            .Where(z => z.CompanyProfileId == branchId && z.EmpfaengerId == id && z.IsActive)
            .Select(z => new { z.GueltigAb, z.GueltigBis })
            .ToListAsync();
        var zugeordnet = zeilen.Any(z => (z.GueltigAb == null || z.GueltigAb <= heute)
                                 && (z.GueltigBis == null || z.GueltigBis >= heute));
        if (!zugeordnet) return "Diesen UVG-Versicherer zuerst der Filiale zuordnen.";
        return null;
    }

    private sealed record UvgVersichererWahl(int Id, string Bezeichnung, string? Kassennummer);

    private async Task<List<UvgVersichererWahl>> OffeneUvgVersichererAsync(int? branchId, DateOnly tag)
    {
        if (branchId == null) return new List<UvgVersichererWahl>();
        var zeilen = await _db.CompanyProfileEmpfaengers.AsNoTracking()
            .Where(z => z.CompanyProfileId == branchId && z.IsActive && z.Empfaenger!.Art == "UVG")
            .Select(z => new { z.EmpfaengerId, z.Empfaenger!.Bezeichnung, z.Empfaenger.Kassennummer, z.GueltigAb, z.GueltigBis })
            .ToListAsync();
        return zeilen
            .Where(z => (z.GueltigAb == null || z.GueltigAb <= tag) && (z.GueltigBis == null || z.GueltigBis >= tag))
            .GroupBy(z => z.EmpfaengerId)
            .Select(g => new UvgVersichererWahl(g.Key, g.First().Bezeichnung, g.First().Kassennummer))
            .OrderBy(x => x.Bezeichnung)
            .ToList();
    }

    private sealed record VerzichtLage(
        bool Erreicht, bool Darf, string Hinweis, DateOnly Heute,
        int Jahr, bool Gesperrt, bool NaechstesGesperrt,
        DateOnly? ErsterTag, int FreibetragMonate,
        DateOnly? Folgemonat, DateOnly? Eintritt, HashSet<int> GesperrteJahre);

    /// <summary>
    /// Freibetrag ab dem Folgemonat des Referenzalters, automatisch.
    /// Verzicht darf schon im Erreichensjahr ab diesem Monat erfasst werden
    /// (Juli → ab August), nicht erst am 1.1. Maßstab ist heute: eine alte
    /// offene Lohnperiode auf der Testinstanz darf eine 66-jährige Person
    /// nicht vom Bearbeiten aussperren. Gesperrt ist ein Jahr erst, wenn darin
    /// eine bestätigte Lohnabrechnung in einem Freibetrag-Monat liegt.
    /// </summary>
    private async Task<VerzichtLage> VerzichtLageAsync(int employeeId)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp?.DateOfBirth == null)
        {
            var leer = new HashSet<int>();
            return new VerzichtLage(false, false, "Verzicht auf den Freibetrag braucht ein Geburtsdatum.",
                heute, heute.Year, false, false, null, 0, null, null, leer);
        }
        var stand = await AhvBeitragspflichtStand.LadenAsync(_db);
        var folge = AhvFreibetragEntscheid.Folgemonat(emp.Gender, emp.DateOfBirth.Value, stand);
        var vertraege = await _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId).ToListAsync();
        var eintritt = Dienstalter.Massgebend(emp, vertraege, heute);
        var erster = AhvFreibetragEntscheid.ErsterTag(heute.Year, eintritt, folge);
        var lohn = await (
            from s in _db.PayrollSnapshots.AsNoTracking()
            join p in _db.PayrollPerioden.AsNoTracking() on s.PayrollPeriodeId equals p.Id
            where s.EmployeeId == employeeId
               && (s.Status == "FREIGEGEBEN_GF" || s.Status == "HR_BESTAETIGT" || s.Status == "ABGESCHLOSSEN")
            select new { p.Year, p.Month }
        ).Distinct().ToListAsync();
        bool freibetragGilt(int j, int m) => PayrollCalculations.HatReferenzalterErreicht(
            emp.Gender, emp.DateOfBirth.Value, j, m, stand);
        var gesperrt = AhvFreibetragEntscheid.GesperrteJahre(lohn.Select(x => (x.Year, x.Month)), freibetragGilt);
        var erreicht = PayrollCalculations.HatReferenzalterErreicht(
            emp.Gender, emp.DateOfBirth.Value, heute.Year, heute.Month, stand);
        var darf = folge != null && folge.Value.Year <= heute.Year;
        var hinweis = AhvFreibetragEntscheid.Hinweis(heute, folge, erster, gesperrt.Contains(heute.Year));
        return new VerzichtLage(
            erreicht, darf, hinweis, heute, heute.Year,
            gesperrt.Contains(heute.Year), gesperrt.Contains(heute.Year + 1),
            erster, AhvFreibetragEntscheid.MonateImJahr(erster ?? folge, heute.Year),
            folge, eintritt, gesperrt);
    }

    private async Task<IActionResult?> PruefeVerzichtAlterAsync(int employeeId, UpsertDto? dto, EmployeeVersicherungCode? ersetzt = null)
    {
        var istNeu = dto != null
            && dto.Art == EmployeeVersicherungCode.ArtAhv
            && string.Equals((dto.Code ?? "").Trim(), EmployeeVersicherungCode.CodeFreibetragVerzicht, StringComparison.OrdinalIgnoreCase);
        var istAlt = ersetzt?.IstAhvFreibetragVerzicht == true;
        if (!istNeu && !istAlt) return null;

        var lage = await VerzichtLageAsync(employeeId);
        if (istNeu && !lage.Darf)
            return BadRequest(new { error = "NICHT_IM_RENTENALTER", message = lage.Hinweis });

        var bestehende = await _db.EmployeeVersicherungCodes.AsNoTracking()
            .Where(v => v.EmployeeId == employeeId
                     && v.Art == EmployeeVersicherungCode.ArtAhv
                     && v.Code == EmployeeVersicherungCode.CodeFreibetragVerzicht)
            .ToListAsync();
        var vorher = bestehende
            .Select(e => new AhvFreibetragEntscheid.Spanne(e.ValidFrom, e.ValidTo)).ToList();
        var nachher = new List<AhvFreibetragEntscheid.Spanne>();
        foreach (var e in bestehende)
        {
            if (ersetzt != null && e.Id == ersetzt.Id) continue;
            nachher.Add(new AhvFreibetragEntscheid.Spanne(e.ValidFrom, e.ValidTo));
        }
        if (istNeu && dto != null)
        {
            if (!dto.Zusaetzlich)
            {
                for (var i = 0; i < nachher.Count; i++)
                {
                    var s = nachher[i];
                    if ((s.Bis == null || s.Bis >= dto.ValidFrom) && s.Von < dto.ValidFrom)
                        nachher[i] = new AhvFreibetragEntscheid.Spanne(s.Von, dto.ValidFrom.AddDays(-1));
                }
            }
            nachher.Add(new AhvFreibetragEntscheid.Spanne(dto.ValidFrom, dto.ValidTo));
        }
        var fehler = AhvFreibetragEntscheid.Pruefe(vorher, nachher, lage.GesperrteJahre, lage.Eintritt, lage.Folgemonat);
        if (fehler != null)
            return BadRequest(new { error = "VERZICHT_JAHR", message = fehler });
        return null;
    }

    private async Task<int?> GetEmployeeBranchAsync(int employeeId)
    {
        return await _db.Employments
            .Where(e => e.EmployeeId == employeeId && e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .Select(e => (int?)e.CompanyProfileId)
            .FirstOrDefaultAsync();
    }

    private int? GetCurrentUserId()
    {
        var s = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(s, out var id) ? id : null;
    }
}
