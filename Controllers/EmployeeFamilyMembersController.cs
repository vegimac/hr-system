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
        // Walter-Vorgabe 13.06.2026: Beleg-Doku-FK durchreichen — das Frontend
        // zeigt damit „📄 Doku verknüpft" am Ehepartner-Eintrag und kann den
        // Beleg im Vorschau-Panel öffnen.
        m.DokumentId,
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

        _context.EmployeeFamilyMembers.Add(member);
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
        existing.UpdatedAt            = DateTime.Now;

        await _context.SaveChangesAsync();
        return Ok(existing);
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

        member.DokumentId = dto.DokumentId;
        member.UpdatedAt  = DateTime.Now;
        await _context.SaveChangesAsync();
        return Ok(new { id = member.Id, dokumentId = member.DokumentId });
    }

    public class FamilyMemberDokumentDto
    {
        public int? DokumentId { get; set; }
    }
}
