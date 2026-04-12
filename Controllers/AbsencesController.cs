using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HrSystem.Controllers;

[ApiController]
[Route("api/absences")]
public class AbsencesController : ControllerBase
{
    private readonly AppDbContext _db;
    public AbsencesController(AppDbContext db) => _db = db;

    // ── GET /api/absences/employee/{employeeId} ───────────────────────────
    [HttpGet("employee/{employeeId:int}")]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        var list = await _db.Absences
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.DateFrom)
            .Select(a => MapToDto(a))
            .ToListAsync();

        return Ok(list);
    }

    // ── POST /api/absences ────────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AbsenceDto dto)
    {
        var absence = new Absence
        {
            EmployeeId    = dto.EmployeeId,
            AbsenceType   = dto.AbsenceType.ToUpper(),
            DateFrom      = DateOnly.Parse(dto.DateFrom),
            DateTo        = DateOnly.Parse(dto.DateTo),
            WorkedDays    = dto.WorkedDays,
            HoursCredited = dto.HoursCredited,
            Notes         = dto.Notes,
            CreatedAt     = DateTime.UtcNow,
            UpdatedAt     = DateTime.UtcNow,
        };

        _db.Absences.Add(absence);
        await _db.SaveChangesAsync();

        return Ok(MapToDto(absence));
    }

    // ── PUT /api/absences/{id} ────────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] AbsenceDto dto)
    {
        var absence = await _db.Absences.FindAsync(id);
        if (absence == null) return NotFound();

        absence.AbsenceType   = dto.AbsenceType.ToUpper();
        absence.DateFrom      = DateOnly.Parse(dto.DateFrom);
        absence.DateTo        = DateOnly.Parse(dto.DateTo);
        absence.WorkedDays    = dto.WorkedDays;
        absence.HoursCredited = dto.HoursCredited;
        absence.Notes         = dto.Notes;
        absence.UpdatedAt     = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(MapToDto(absence));
    }

    // ── DELETE /api/absences/{id} ─────────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var absence = await _db.Absences.FindAsync(id);
        if (absence == null) return NotFound();

        _db.Absences.Remove(absence);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── Mapping ───────────────────────────────────────────────────────────
    private static object MapToDto(Absence a) => new
    {
        id            = a.Id,
        employeeId    = a.EmployeeId,
        absenceType   = a.AbsenceType,
        dateFrom      = a.DateFrom.ToString("yyyy-MM-dd"),
        dateTo        = a.DateTo.ToString("yyyy-MM-dd"),
        workedDays    = a.WorkedDays,
        hoursCredited = a.HoursCredited,
        notes         = a.Notes,
        createdAt     = a.CreatedAt,
    };
}

public class AbsenceDto
{
    public int    EmployeeId    { get; set; }
    public string AbsenceType   { get; set; } = "";
    public string DateFrom      { get; set; } = "";
    public string DateTo        { get; set; } = "";
    public string? WorkedDays   { get; set; }
    public decimal HoursCredited { get; set; }
    public string? Notes        { get; set; }
}
