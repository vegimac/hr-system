using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/employees/{employeeId:int}/family")]
public class EmployeeFamilyMembersController : ControllerBase
{
    private readonly AppDbContext _context;

    public EmployeeFamilyMembersController(AppDbContext context)
    {
        _context = context;
    }

    static DateOnly? AsDate(DateTime? dt) => dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;

    // GET /api/employees/{employeeId}/family
    [HttpGet]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        // Walter-Vorgabe 07.06.2026: PermitType (Code + Description) mit-laden,
        // damit das Frontend beim Ehepartner-Block den vollen Bewilligungs-Text
        // anzeigen kann statt „Typ 7".
        var members = await _context.EmployeeFamilyMembers
            .Include(m => m.PermitType)
            .Include(m => m.NationalityRef)
            .Where(m => m.EmployeeId == employeeId)
            .OrderBy(m => m.MemberType)
            .ThenBy(m => m.DateOfBirth)
            .ToListAsync();

        // Adress-IDs einsammeln und in einem Rutsch laden — damit das Frontend
        // die abweichende Adresse als Badge / im Modal anzeigen kann.
        var altIds = members.Where(m => m.AlternativeAddressId.HasValue)
                            .Select(m => m.AlternativeAddressId!.Value)
                            .Distinct()
                            .ToList();
        var altAddrs = altIds.Count == 0
            ? new Dictionary<int, EmployeeAddress>()
            : await _context.EmployeeAddresses
                .Where(a => altIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id);

        return Ok(members.Select(m => ProjectMember(m, altAddrs.GetValueOrDefault(m.AlternativeAddressId ?? 0))));
    }

    // GET /api/employees/{employeeId}/family/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int employeeId, int id)
    {
        var member = await _context.EmployeeFamilyMembers
            .Include(m => m.PermitType)
            .Include(m => m.NationalityRef)
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);

        if (member == null) return NotFound();
        EmployeeAddress? alt = null;
        if (member.AlternativeAddressId.HasValue)
            alt = await _context.EmployeeAddresses.FirstOrDefaultAsync(a => a.Id == member.AlternativeAddressId.Value);
        return Ok(ProjectMember(member, alt));
    }

    /// <summary>
    /// Projektion: Member-Felder (alle) + zusätzlich altAddress mit den
    /// wichtigsten Adress-Feldern für die Anzeige im Frontend.
    /// </summary>
    private static object ProjectMember(EmployeeFamilyMember m, EmployeeAddress? alt) => new
    {
        m.Id,
        m.EmployeeId,
        m.MemberType,
        m.Gender,
        m.FamilyStatus,
        m.LastName,
        m.MaidenName,
        m.FirstName,
        m.SocialSecurityNumber,
        m.Phone,
        m.LivesInSwitzerland,
        m.DateOfBirth,
        m.DateOfDeath,
        m.Allowance1Until,
        m.Allowance2Until,
        m.Allowance3Until,
        m.AlternativeAddressId,
        // Walter-Vorgabe 25.08.2026: expliziter Haushalt-Status (3 Fälle).
        m.LebtImHaushalt,
        m.QstDeductibleFrom,
        m.QstDeductibleUntil,
        // Wissens-Achse Kind (Walter 15.09.2026 / UI 18.09.2026): Wirkung =
        // QstDeductibleFrom, Wissen = ErfahrenAm (NULL = gleich Wirkung).
        m.ErfahrenAm,
        m.PermitTypeId,
        // Walter-Vorgabe 07.06.2026: PermitType-Klartext mitliefern.
        permitType = m.PermitType == null ? null : new {
            id          = m.PermitType.Id,
            code        = m.PermitType.Code,
            description = m.PermitType.Description
        },
        m.PermitExpiryDate,
        m.ZemisNumber,
        m.NationalityId,
        // Walter-Vorgabe 07.06.2026: NationalityCode mitliefern, damit das
        // Frontend „CH-Bürger" statt „ohne Bewilligung" anzeigen kann.
        nationalityCode = m.NationalityRef?.Code,
        // Walter-Vorgabe 20.08.2026: QST-Relevanz-Felder.
        m.Erwerbstaetig,
        m.ArbeitgeberName,
        m.ArbeitgeberStrasse,
        m.ArbeitgeberPlz,
        m.ArbeitgeberOrt,
        m.ArbeitgeberKanton,
        m.Stellenantritt,
        m.InErstausbildung,
        m.KeineUnterhaltspflicht,
        // Konkubinats-Logik (Walter 25.08.2026, docs/konkubinat-qst-konzept.md)
        m.MaHatHoeheresEinkommen,
        m.GemeinsamesKindMitPartner,
        // Walter-Vorgabe 13.06.2026: Beleg-Doku-FK durchreichen — das Frontend
        // zeigt damit „📄 Doku verknüpft" am Ehepartner-Eintrag und kann den
        // Beleg im Vorschau-Panel öffnen.
        m.DokumentId,
        m.GeburtsurkundeDokumentId,
        m.CreatedAt,
        m.UpdatedAt,
        alternativeAddress = alt == null ? null : new {
            alt.Id,
            alt.Description,
            alt.Street,
            alt.Street2,
            alt.PoBox,
            alt.ZipCode,
            alt.City,
            alt.Canton,
            alt.Country,
        }
    };

    // POST /api/employees/{employeeId}/family
    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, EmployeeFamilyMember member)
    {
        member.EmployeeId = employeeId;
        member.CreatedAt = DateTime.Now;
        member.UpdatedAt = DateTime.Now;

        // AlternativeAddressId nur akzeptieren, wenn die Zusatzadresse
        // tatsächlich zum gleichen MA gehört (Schutz vor Cross-MA-IDs).
        member.AlternativeAddressId = await ValidateAlternativeAddressAsync(employeeId, member.AlternativeAddressId);

        // Walter 25.08.2026: Konsistenz-Guard — eine erfasste Zusatzadresse
        // bedeutet IMMER «nicht im gleichen Haushalt».
        if (member.AlternativeAddressId != null) member.LebtImHaushalt = false;
        if (member.MemberType == "Kind" && !member.ErfahrenAm.HasValue)
            member.ErfahrenAm = DateOnly.FromDateTime(DateTime.Now);

        _context.EmployeeFamilyMembers.Add(member);
        await _context.SaveChangesAsync();
        try { await SyncHistoriesAsync(member, member); }
        catch (InvalidOperationException ex) when (ex.Message == "ERFAHREN_VOR_GUELTIG")
        {
            return BadRequest(new { error = "ERFAHREN_VOR_GUELTIG", message = "«Erfahren am» darf nicht vor «Seit» liegen." });
        }
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { employeeId, id = member.Id }, member);
    }

    // PUT /api/employees/{employeeId}/family/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, EmployeeFamilyMember member)
    {
        var existing = await _context.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);

        if (existing == null) return NotFound();

        existing.MemberType           = member.MemberType;
        existing.Gender               = member.Gender;
        existing.FamilyStatus         = member.FamilyStatus;
        existing.LastName             = member.LastName;
        existing.MaidenName           = member.MaidenName;
        existing.FirstName            = member.FirstName;
        existing.SocialSecurityNumber = member.SocialSecurityNumber;
        existing.Phone                = string.IsNullOrWhiteSpace(member.Phone) ? null : member.Phone.Trim();
        existing.LivesInSwitzerland   = member.LivesInSwitzerland;
        existing.DateOfBirth          = member.DateOfBirth;
        existing.DateOfDeath          = member.DateOfDeath;
        existing.Allowance1Until      = member.Allowance1Until;
        existing.Allowance2Until      = member.Allowance2Until;
        existing.Allowance3Until      = member.Allowance3Until;
        existing.AlternativeAddressId = await ValidateAlternativeAddressAsync(employeeId, member.AlternativeAddressId);
        existing.QstDeductibleFrom    = member.QstDeductibleFrom;
        existing.QstDeductibleUntil   = member.QstDeductibleUntil;
        // Kind: Erfahren am editierbar (Swissdec TF34 Marc: ab 1.5., erfahren 1.7.).
        // Leer → NULL (= Wissen = Wirkung). Neu ohne Wert: heute (Create-Pfad).
        existing.ErfahrenAm          = member.ErfahrenAm;
        existing.PermitTypeId         = member.PermitTypeId;
        existing.PermitExpiryDate     = member.PermitExpiryDate;
        existing.ZemisNumber          = string.IsNullOrWhiteSpace(member.ZemisNumber) ? null : member.ZemisNumber.Trim();
        existing.NationalityId        = member.NationalityId;
        // Walter-Vorgabe 20.08.2026: QST-Relevanz-Felder (Ehepartner-Erwerb,
        // Kind-Erstausbildung).
        // Walter 25.08.2026: Haushalt-Status — Guard: Zusatzadresse gesetzt
        // bedeutet immer «nicht im gleichen Haushalt».
        existing.LebtImHaushalt       = existing.AlternativeAddressId != null ? false : member.LebtImHaushalt;
        existing.Erwerbstaetig        = member.Erwerbstaetig;
        existing.ArbeitgeberName      = string.IsNullOrWhiteSpace(member.ArbeitgeberName)    ? null : member.ArbeitgeberName.Trim();
        existing.ArbeitgeberStrasse   = string.IsNullOrWhiteSpace(member.ArbeitgeberStrasse) ? null : member.ArbeitgeberStrasse.Trim();
        existing.ArbeitgeberPlz       = string.IsNullOrWhiteSpace(member.ArbeitgeberPlz)     ? null : member.ArbeitgeberPlz.Trim();
        existing.ArbeitgeberOrt       = string.IsNullOrWhiteSpace(member.ArbeitgeberOrt)     ? null : member.ArbeitgeberOrt.Trim();
        existing.ArbeitgeberKanton    = string.IsNullOrWhiteSpace(member.ArbeitgeberKanton)  ? null : member.ArbeitgeberKanton.Trim();
        existing.Stellenantritt       = member.Stellenantritt;
        existing.InErstausbildung     = member.InErstausbildung;
        existing.KeineUnterhaltspflicht = member.KeineUnterhaltspflicht;
        // Konkubinats-Logik (Walter 25.08.2026, docs/konkubinat-qst-konzept.md)
        existing.MaHatHoeheresEinkommen     = member.MaHatHoeheresEinkommen;
        existing.GemeinsamesKindMitPartner  = member.GemeinsamesKindMitPartner;
        existing.UpdatedAt            = DateTime.Now;

        try { await SyncHistoriesAsync(existing, member); }
        catch (InvalidOperationException ex) when (ex.Message == "ERFAHREN_VOR_GUELTIG")
        {
            return BadRequest(new { error = "ERFAHREN_VOR_GUELTIG", message = "«Erfahren am» darf nicht vor «Seit» liegen." });
        }
        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    // GET …/family/{id}/permit-history
    [HttpGet("{id:int}/permit-history")]
    public async Task<IActionResult> ListPermitHistory(int employeeId, int id)
    {
        if (!await MemberOk(employeeId, id)) return NotFound();
        var list = await _context.FamilyMemberPermitHistories.AsNoTracking()
            .Include(h => h.PermitType)
            .Where(h => h.FamilyMemberId == id)
            .OrderByDescending(h => h.ValidFrom).ThenByDescending(h => h.Id)
            .Select(h => new
            {
                h.Id,
                h.PermitTypeId,
                permitCode = h.PermitType != null ? h.PermitType.Code : null,
                permitLabel = h.PermitType != null ? (h.PermitType.Description ?? h.PermitType.Code) : "CH / keine",
                validFrom = h.ValidFrom.ToString("yyyy-MM-dd"),
                erfahrenAm = h.ErfahrenAm.HasValue ? h.ErfahrenAm.Value.ToString("yyyy-MM-dd") : null,
                validTo = h.ValidTo.HasValue ? h.ValidTo.Value.ToString("yyyy-MM-dd") : null,
                h.Note,
            })
            .ToListAsync();
        return Ok(list);
    }

    // GET …/family/{id}/erwerb-history
    [HttpGet("{id:int}/erwerb-history")]
    public async Task<IActionResult> ListErwerbHistory(int employeeId, int id)
    {
        if (!await MemberOk(employeeId, id)) return NotFound();
        var list = await _context.FamilyMemberErwerbHistories.AsNoTracking()
            .Where(h => h.FamilyMemberId == id)
            .OrderByDescending(h => h.ValidFrom).ThenByDescending(h => h.Id)
            .Select(h => new
            {
                h.Id,
                h.Erwerbstaetig,
                h.ArbeitgeberName,
                h.ArbeitgeberKanton,
                stellenantritt = h.Stellenantritt.HasValue ? h.Stellenantritt.Value.ToString("yyyy-MM-dd") : null,
                validFrom = h.ValidFrom.ToString("yyyy-MM-dd"),
                erfahrenAm = h.ErfahrenAm.HasValue ? h.ErfahrenAm.Value.ToString("yyyy-MM-dd") : null,
                h.Note,
            })
            .ToListAsync();
        return Ok(list);
    }

    [HttpDelete("{id:int}/permit-history/{histId:int}")]
    public async Task<IActionResult> DeletePermitHistory(int employeeId, int id, int histId)
    {
        if (!await MemberOk(employeeId, id)) return NotFound();
        var h = await _context.FamilyMemberPermitHistories.FirstOrDefaultAsync(x => x.Id == histId && x.FamilyMemberId == id);
        if (h == null) return NotFound();
        _context.FamilyMemberPermitHistories.Remove(h);
        await _context.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:int}/erwerb-history/{histId:int}")]
    public async Task<IActionResult> DeleteErwerbHistory(int employeeId, int id, int histId)
    {
        if (!await MemberOk(employeeId, id)) return NotFound();
        var h = await _context.FamilyMemberErwerbHistories.FirstOrDefaultAsync(x => x.Id == histId && x.FamilyMemberId == id);
        if (h == null) return NotFound();
        _context.FamilyMemberErwerbHistories.Remove(h);
        await _context.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    public sealed class PermitStatusDto
    {
        public int? PermitTypeId { get; set; }
        public string? Seit { get; set; }
        public string? ErfahrenAm { get; set; }
    }

    public sealed class ErwerbStatusDto
    {
        public bool? Erwerbstaetig { get; set; }
        public string? Seit { get; set; }
        public string? ErfahrenAm { get; set; }
    }

    /// <summary>Bewilligungsstatus ändern (QST) — Seit + Erfahren am (Walter 08.10.2026).</summary>
    [HttpPost("{id:int}/permit-status")]
    public async Task<IActionResult> ChangePermitStatus(int employeeId, int id, [FromBody] PermitStatusDto dto)
    {
        var m = await _context.EmployeeFamilyMembers.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (m == null) return NotFound();
        var seit = EmployeeQuellensteuerController.ParseDatum(dto.Seit);
        if (seit == null) return BadRequest(new { error = "DATUM_UNGUELTIG", message = "«Seit» ist Pflicht." });
        DateOnly? ea = null;
        if (!string.IsNullOrWhiteSpace(dto.ErfahrenAm))
        {
            ea = EmployeeQuellensteuerController.ParseDatum(dto.ErfahrenAm);
            if (ea == null) return BadRequest(new { error = "DATUM_UNGUELTIG", message = "«Erfahren am» ist ungültig." });
            if (ea.Value < seit.Value)
                return BadRequest(new { error = "ERFAHREN_VOR_GUELTIG", message = "«Erfahren am» darf nicht vor «Seit» liegen." });
        }
        m.PermitTypeId = dto.PermitTypeId is > 0 ? dto.PermitTypeId : null;
        m.UpdatedAt = DateTime.Now;
        _context.FamilyMemberPermitHistories.Add(new FamilyMemberPermitHistory
        {
            FamilyMemberId = m.Id,
            PermitTypeId = m.PermitTypeId,
            ValidFrom = seit.Value,
            ErfahrenAm = ea,
            Note = "Statusänderung",
            CreatedAt = DateTime.Now,
        });
        await _context.SaveChangesAsync();
        return Ok(new { ok = true, permitTypeId = m.PermitTypeId, seit = seit.Value.ToString("yyyy-MM-dd") });
    }

    /// <summary>Erwerbsstatus ändern (Tarif B/C) — Seit + Erfahren am (Walter 08.10.2026).</summary>
    [HttpPost("{id:int}/erwerb-status")]
    public async Task<IActionResult> ChangeErwerbStatus(int employeeId, int id, [FromBody] ErwerbStatusDto dto)
    {
        var m = await _context.EmployeeFamilyMembers.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (m == null) return NotFound();
        if (dto.Erwerbstaetig == null)
            return BadRequest(new { error = "ERWERB_FEHLT", message = "Neuer Status (Ja/Nein) fehlt." });
        var seit = EmployeeQuellensteuerController.ParseDatum(dto.Seit);
        if (seit == null) return BadRequest(new { error = "DATUM_UNGUELTIG", message = "«Seit» ist Pflicht." });
        DateOnly? ea = null;
        if (!string.IsNullOrWhiteSpace(dto.ErfahrenAm))
        {
            ea = EmployeeQuellensteuerController.ParseDatum(dto.ErfahrenAm);
            if (ea == null) return BadRequest(new { error = "DATUM_UNGUELTIG", message = "«Erfahren am» ist ungültig." });
            if (ea.Value < seit.Value)
                return BadRequest(new { error = "ERFAHREN_VOR_GUELTIG", message = "«Erfahren am» darf nicht vor «Seit» liegen." });
        }
        m.Erwerbstaetig = dto.Erwerbstaetig;
        m.UpdatedAt = DateTime.Now;
        _context.FamilyMemberErwerbHistories.Add(new FamilyMemberErwerbHistory
        {
            FamilyMemberId = m.Id,
            Erwerbstaetig = dto.Erwerbstaetig,
            ValidFrom = seit.Value,
            ErfahrenAm = ea,
            Note = "Statusänderung",
            CreatedAt = DateTime.Now,
        });
        await _context.SaveChangesAsync();
        return Ok(new { ok = true, erwerbstaetig = m.Erwerbstaetig, seit = seit.Value.ToString("yyyy-MM-dd") });
    }

    Task<bool> MemberOk(int employeeId, int id)
        => _context.EmployeeFamilyMembers.AnyAsync(m => m.Id == id && m.EmployeeId == employeeId);

    /// <summary>
    /// Bewilligungs-/Erwerbs-Historie nachführen, wenn Seit/ErfahrenAm
    /// mitgeliefert werden oder sich der Snapshot ändert (Walter 08.10.2026).
    /// </summary>
    async Task SyncHistoriesAsync(EmployeeFamilyMember snap, EmployeeFamilyMember dto)
    {
        var typ = (snap.MemberType ?? "").Trim();
        if (typ is not ("Ehepartner" or "Konkubinatspartner")) return;

        // ── Bewilligung ──────────────────────────────────────────────────
        var permitHist = await _context.FamilyMemberPermitHistories
            .Where(h => h.FamilyMemberId == snap.Id)
            .OrderBy(h => h.ValidFrom).ThenBy(h => h.Id)
            .ToListAsync();
        // Historie nur Bewilligungs-Typ + Seit/Erfahren (Walter 08.10.2026) —
        // kein «Gültig bis» mehr in der Maske.
        bool hatPermitDaten = snap.PermitTypeId != null || snap.NationalityId != null;
        var letzterP = permitHist.LastOrDefault();
        bool permitGeaendert = letzterP == null || letzterP.PermitTypeId != snap.PermitTypeId;
        var permitSeit = dto.PermitSeit ?? DateOnly.FromDateTime(DateTime.Today);
        if (hatPermitDaten && (dto.PermitSeit.HasValue || permitHist.Count == 0 || permitGeaendert))
        {
            var ea = dto.PermitErfahrenAm;
            if (ea.HasValue && ea.Value < permitSeit)
                throw new InvalidOperationException("ERFAHREN_VOR_GUELTIG");
            if (letzterP != null && letzterP.ValidFrom == permitSeit)
            {
                letzterP.PermitTypeId = snap.PermitTypeId;
                letzterP.ErfahrenAm = ea;
                letzterP.ValidTo = null;
            }
            else if (permitGeaendert || dto.PermitSeit.HasValue || permitHist.Count == 0)
            {
                _context.FamilyMemberPermitHistories.Add(new FamilyMemberPermitHistory
                {
                    FamilyMemberId = snap.Id,
                    PermitTypeId = snap.PermitTypeId,
                    ValidFrom = permitSeit,
                    ErfahrenAm = ea,
                    ValidTo = null,
                    Note = permitHist.Count == 0 ? "Ersterfassung" : null,
                    CreatedAt = DateTime.Now,
                });
            }
        }

        // ── Erwerbstätigkeit: nur Ja/Nein-Wechsel (kein Arbeitgeber) ─────
        var erwerbHist = await _context.FamilyMemberErwerbHistories
            .Where(h => h.FamilyMemberId == snap.Id)
            .OrderBy(h => h.ValidFrom).ThenBy(h => h.Id)
            .ToListAsync();
        var letzterE = erwerbHist.LastOrDefault();
        bool erwerbGeaendert = letzterE == null || letzterE.Erwerbstaetig != snap.Erwerbstaetig;
        var erwerbSeit = dto.ErwerbSeit ?? DateOnly.FromDateTime(DateTime.Today);
        if (snap.Erwerbstaetig != null && (dto.ErwerbSeit.HasValue || erwerbHist.Count == 0 || erwerbGeaendert))
        {
            var ea = dto.ErwerbErfahrenAm;
            if (ea.HasValue && ea.Value < erwerbSeit)
                throw new InvalidOperationException("ERFAHREN_VOR_GUELTIG");
            if (letzterE != null && letzterE.ValidFrom == erwerbSeit)
            {
                letzterE.Erwerbstaetig = snap.Erwerbstaetig;
                letzterE.ErfahrenAm = ea;
            }
            else if (erwerbGeaendert || dto.ErwerbSeit.HasValue || erwerbHist.Count == 0)
            {
                _context.FamilyMemberErwerbHistories.Add(new FamilyMemberErwerbHistory
                {
                    FamilyMemberId = snap.Id,
                    Erwerbstaetig = snap.Erwerbstaetig,
                    ValidFrom = erwerbSeit,
                    ErfahrenAm = ea,
                    Note = erwerbHist.Count == 0 ? "Ersterfassung" : null,
                    CreatedAt = DateTime.Now,
                });
            }
        }
    }

    /// <summary>
    /// Schutz: nimmt AlternativeAddressId nur an, wenn die referenzierte
    /// Zusatzadresse tatsächlich zum selben MA gehört. Sonst NULL.
    /// </summary>
    private async Task<int?> ValidateAlternativeAddressAsync(int employeeId, int? altId)
    {
        if (!altId.HasValue) return null;
        var ok = await _context.EmployeeAddresses
            .AnyAsync(a => a.Id == altId.Value && a.EmployeeId == employeeId);
        return ok ? altId : null;
    }

    // DELETE /api/employees/{employeeId}/family/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var member = await _context.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);

        if (member == null) return NotFound();

        _context.EmployeeFamilyMembers.Remove(member);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Walter-Vorgabe 13.06.2026: Beleg-Dokument für dieses Familienmitglied
    /// verknüpfen oder aufheben. Wird vor allem für den Spouse-Doku-Check
    /// in QstPflichtCheckService genutzt (Ehepartner-CH / Ehepartner-C).
    /// PATCH /api/employees/{employeeId}/family/{id}/dokument
    /// Body: { dokumentId: int | null }
    /// </summary>
    [HttpPatch("{id:int}/dokument")]
    public async Task<IActionResult> SetDokument(int employeeId, int id, [FromBody] FamilyMemberDokumentDto dto)
    {
        var member = await _context.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);
        if (member == null) return NotFound();

        if (dto.DokumentId.HasValue)
        {
            var dokOk = await _context.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == employeeId);
            if (!dokOk)
                return BadRequest(new { error = "DOKUMENT_INVALID",
                    message = "Das verlinkte Dokument gehört nicht zu diesem Mitarbeiter." });
        }

        // Art (Walter 23.09.2026): «geburtsurkunde» → eigenes Feld; sonst wie
        // bisher der Ausweis/Beleg (beim Ehepartner zugleich QST-Beleg).
        if (string.Equals(dto.Art, "geburtsurkunde", StringComparison.OrdinalIgnoreCase))
            member.GeburtsurkundeDokumentId = dto.DokumentId;
        else
            member.DokumentId = dto.DokumentId;
        member.UpdatedAt  = DateTime.Now;
        await _context.SaveChangesAsync();
        return Ok(new { id = member.Id, dokumentId = member.DokumentId, geburtsurkundeDokumentId = member.GeburtsurkundeDokumentId });
    }

    public class FamilyMemberDokumentDto
    {
        public int? DokumentId { get; set; }
        /// <summary>NULL/«ausweis» = Ausweis-Beleg, «geburtsurkunde» = Geburtsurkunde.</summary>
        public string? Art { get; set; }
    }
}
