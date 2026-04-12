using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/employees/{employeeId:int}/quellensteuer")]
public class EmployeeQuellensteuerController : ControllerBase
{
    private readonly AppDbContext _db;
    public EmployeeQuellensteuerController(AppDbContext db) => _db = db;

    // GET /api/employees/{employeeId}/quellensteuer
    // Gibt alle QST-Einträge eines Mitarbeiters zurück (neueste zuerst)
    [HttpGet]
    public async Task<IActionResult> GetAll(int employeeId)
    {
        var entries = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId)
            .OrderByDescending(q => q.ValidFrom)
            .ToListAsync();
        return Ok(entries);
    }

    // GET /api/employees/{employeeId}/quellensteuer/current?date=2026-04-01
    // Gibt den für ein Datum gültigen QST-Eintrag zurück
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrent(int employeeId, [FromQuery] DateOnly? date)
    {
        var refDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var entry = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId
                     && q.ValidFrom <= refDate
                     && (q.ValidTo == null || q.ValidTo >= refDate))
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        return Ok(entry); // null wenn keiner gefunden
    }

    // GET /api/employees/{employeeId}/quellensteuer/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int employeeId, int id)
    {
        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();
        return Ok(entry);
    }

    // POST /api/employees/{employeeId}/quellensteuer
    // Neuen QST-Eintrag anlegen; schliesst vorherigen Eintrag automatisch ab
    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] EmployeeQuellensteuer dto)
    {
        // Vorherigen offenen Eintrag abschliessen (ValidTo = dto.ValidFrom - 1 Tag)
        var previous = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId && q.ValidTo == null)
            .OrderByDescending(q => q.ValidFrom)
            .FirstOrDefaultAsync();
        if (previous != null && previous.ValidFrom < dto.ValidFrom)
            previous.ValidTo = dto.ValidFrom.AddDays(-1);

        dto.Id         = 0;
        dto.EmployeeId = employeeId;
        dto.CreatedAt  = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
        dto.UpdatedAt  = dto.CreatedAt;

        _db.EmployeeQuellensteuer.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(dto);
    }

    // PUT /api/employees/{employeeId}/quellensteuer/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, [FromBody] EmployeeQuellensteuer dto)
    {
        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();

        entry.ValidFrom                  = dto.ValidFrom;
        entry.ValidTo                    = dto.ValidTo;
        entry.Steuerkanton               = dto.Steuerkanton;
        entry.SteuerkantonName           = dto.SteuerkantonName;
        entry.QstGemeinde                = dto.QstGemeinde;
        entry.QstGemeindeBfsNr           = dto.QstGemeindeBfsNr;
        entry.TarifvorschlagQst          = dto.TarifvorschlagQst;
        entry.TarifCode                  = dto.TarifCode;
        entry.TarifBezeichnung           = dto.TarifBezeichnung;
        entry.AnzahlKinder               = dto.AnzahlKinder;
        entry.Kirchensteuer              = dto.Kirchensteuer;
        entry.QstCode                    = dto.QstCode;
        entry.SpezielBewilligt           = dto.SpezielBewilligt;
        entry.Kategorie                  = dto.Kategorie;
        entry.Prozentsatz                = dto.Prozentsatz;
        entry.MindestlohnSatzbestimmung  = dto.MindestlohnSatzbestimmung;
        entry.PartnerEmployeeId          = dto.PartnerEmployeeId;
        entry.PartnerEinkommenVon        = dto.PartnerEinkommenVon;
        entry.PartnerEinkommenBis        = dto.PartnerEinkommenBis;
        entry.ArbeitsortKanton           = dto.ArbeitsortKanton;
        entry.WeitereBeschaftigungen     = dto.WeitereBeschaftigungen;
        entry.GesamtpensumWeitereAg      = dto.GesamtpensumWeitereAg;
        entry.GesamteinkommenWeitereAg   = dto.GesamteinkommenWeitereAg;
        entry.Halbfamilie                = dto.Halbfamilie;
        entry.WohnsitzAusland            = dto.WohnsitzAusland;
        entry.Wohnsitzstaat              = dto.Wohnsitzstaat;
        entry.AdresseAusland             = dto.AdresseAusland;
        entry.UpdatedAt                  = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);

        await _db.SaveChangesAsync();
        return Ok(entry);
    }

    // DELETE /api/employees/{employeeId}/quellensteuer/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var entry = await _db.EmployeeQuellensteuer
            .FirstOrDefaultAsync(q => q.Id == id && q.EmployeeId == employeeId);
        if (entry is null) return NotFound();
        _db.EmployeeQuellensteuer.Remove(entry);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
