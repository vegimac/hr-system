using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// CRUD für die Zusatz-Adressen eines Mitarbeiters (z.B. Korrespondenzadresse,
/// Ferienwohnung, Sozialamt). Die HAUPTADRESSE liegt direkt am Employee und
/// wird über EmployeesController gepflegt.
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/addresses")]
[Authorize]
public class EmployeeAddressesController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeAddressesController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAll(int employeeId)
    {
        var list = await _db.EmployeeAddresses
            .Where(a => a.EmployeeId == employeeId)
            .OrderBy(a => a.AddressType).ThenBy(a => a.Id)
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, EmployeeAddress dto)
    {
        var employeeExists = await _db.Employees.AnyAsync(e => e.Id == employeeId);
        if (!employeeExists)
            return NotFound(new { error = $"Mitarbeiter {employeeId} nicht gefunden." });

        dto.Id = 0;
        dto.EmployeeId = employeeId;
        dto.CreatedAt = DateTime.UtcNow;
        dto.UpdatedAt = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(dto.AddressType))
            dto.AddressType = "Korrespondenzadresse";
        // Land-Standard systemweit: ISO-Code „CH" (Walter-Vorgabe 13.05.2026).
        if (string.IsNullOrWhiteSpace(dto.Country))
            dto.Country = "CH";

        _db.EmployeeAddresses.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(dto);
    }

    [HttpPut("{addressId:int}")]
    public async Task<IActionResult> Update(int employeeId, int addressId, EmployeeAddress dto)
    {
        var existing = await _db.EmployeeAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.EmployeeId == employeeId);
        if (existing == null)
            return NotFound(new { error = "Adresse nicht gefunden." });

        existing.AddressType      = string.IsNullOrWhiteSpace(dto.AddressType) ? "Korrespondenzadresse" : dto.AddressType;
        existing.ValidFrom        = dto.ValidFrom;
        existing.Description      = dto.Description;
        existing.Street           = dto.Street;
        existing.Street2          = dto.Street2;
        existing.PoBox            = dto.PoBox;
        existing.ZipCode          = dto.ZipCode;
        existing.City             = dto.City;
        existing.BfsNumber        = dto.BfsNumber;
        existing.Canton           = dto.Canton;
        existing.Country          = string.IsNullOrWhiteSpace(dto.Country) ? "CH" : dto.Country;
        existing.Phone            = dto.Phone;
        existing.Phone2           = dto.Phone2;
        existing.Email            = dto.Email;
        existing.IncamailDisabled = dto.IncamailDisabled;
        existing.UpdatedAt        = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("{addressId:int}")]
    public async Task<IActionResult> Delete(int employeeId, int addressId)
    {
        var existing = await _db.EmployeeAddresses
            .FirstOrDefaultAsync(a => a.Id == addressId && a.EmployeeId == employeeId);
        if (existing == null)
            return NotFound(new { error = "Adresse nicht gefunden." });

        _db.EmployeeAddresses.Remove(existing);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
    }
}
