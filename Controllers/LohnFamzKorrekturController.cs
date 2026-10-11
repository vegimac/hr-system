using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Familienzulagen-Warnungen im Lohnlauf (Walter 11.10.2026): Nachzahlungen und
/// Rückforderungen früherer Monate werden NICHT automatisch bezahlt. Sie erscheinen
/// unter «Zulagen &amp; Abzüge» als Warnung und müssen quittiert werden — entweder
/// «als Zulage übernehmen» (legt eine normale, frei änderbare Zulage 190.x an) oder
/// «nicht nötig» mit Bemerkung. Solange eine offen ist, sperrt sie «Lohn bestätigen».
/// Sperre wie Zulagen/Abzüge (<see cref="LohnEditLockService.CheckZulageMonthAsync"/>).
/// </summary>
[ApiController]
[Route("api/lohn-famz-korrektur")]
public class LohnFamzKorrekturController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public LohnFamzKorrekturController(AppDbContext db, LohnEditLockService editLock)
    {
        _db       = db;
        _editLock = editLock;
    }

    [HttpGet("{employeeId:int}/{year:int}/{month:int}")]
    public async Task<IActionResult> Liste(int employeeId, int year, int month)
    {
        var posten = await FamzKorrekturService.OffeneWarnungen(_db, employeeId, year, month)
            .OrderBy(k => k.Jahr).ThenBy(k => k.Monat).ThenBy(k => k.ChildName)
            .ToListAsync();
        return Ok(posten.Select(k => new
        {
            k.Id, k.Jahr, k.Monat, k.AllowanceType, k.ChildName,
            k.Betrag, k.AlterBetrag, k.NeuerBetrag, k.Grund,
            vorschlag = Vorschlagstext(k)
        }));
    }

    public class LohnDto
    {
        public int? CompanyProfileId { get; set; }
        public int  Year             { get; set; }
        public int  Month            { get; set; }
    }

    public class QuittierenDto : LohnDto
    {
        public string? Bemerkung { get; set; }
    }

    [HttpPost("{id:int}/uebernehmen")]
    [MaEintrag(typeof(FamzKorrektur))]
    public async Task<IActionResult> Uebernehmen(int id, [FromBody] LohnDto dto)
    {
        var (k, fehler) = await LadeOffenAsync(id, dto);
        if (fehler != null) return fehler;

        string code = string.Equals(k!.AllowanceType, "AZ", StringComparison.OrdinalIgnoreCase) ? "190.2"
                    : string.Equals(k.AllowanceType, "GZ", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(k.AllowanceType, "AdoptZ", StringComparison.OrdinalIgnoreCase) ? "190.3"
                    : "190.1";
        var lp = await _db.Lohnpositionen.FirstOrDefaultAsync(l => l.Code == code && l.IsActive);
        if (lp == null)
            return BadRequest(new { error = "LOHNPOSITION_FEHLT", message = $"Lohnposition {code} ist nicht aktiv." });

        var zulage = new LohnZulage
        {
            EmployeeId     = k.EmployeeId,
            Periode        = $"{dto.Year:D4}-{dto.Month:D2}",
            LohnpositionId = lp.Id,
            Betrag         = Math.Round(k.Betrag, 2),
            Bemerkung      = Vorschlagstext(k),
            CreatedAt      = DateTime.Now,
            UpdatedAt      = DateTime.Now
        };
        _db.LohnZulagen.Add(zulage);
        await _db.SaveChangesAsync();

        k.Status       = "ERLEDIGT";
        k.ErledigtArt  = "UEBERNOMMEN";
        k.LohnZulageId = zulage.Id;
        k.ErledigtAm   = DateTime.Now;
        k.ErledigtVon  = User.Identity?.Name;
        await _db.SaveChangesAsync();
        return Ok(new { k.Id, lohnZulageId = zulage.Id });
    }

    [HttpPost("{id:int}/quittieren")]
    [MaEintrag(typeof(FamzKorrektur))]
    public async Task<IActionResult> Quittieren(int id, [FromBody] QuittierenDto dto)
    {
        var bem = dto.Bemerkung?.Trim();
        if (string.IsNullOrEmpty(bem))
            return BadRequest(new { error = "BEMERKUNG_FEHLT",
                message = "Bitte festhalten, warum nichts nötig ist (z.B. «schon in Mirus bezahlt»)." });

        var (k, fehler) = await LadeOffenAsync(id, dto);
        if (fehler != null) return fehler;

        k!.Status      = "ERLEDIGT";
        k.ErledigtArt  = "NICHT_NOETIG";
        k.Bemerkung    = bem;
        k.ErledigtAm   = DateTime.Now;
        k.ErledigtVon  = User.Identity?.Name;
        await _db.SaveChangesAsync();
        return Ok(new { k.Id });
    }

    private async Task<(FamzKorrektur? K, IActionResult? Fehler)> LadeOffenAsync(int id, LohnDto dto)
    {
        if (dto.Year < 2000 || dto.Month is < 1 or > 12)
            return (null, BadRequest(new { error = "PERIODE", message = "Lohnperiode fehlt." }));
        var k = await _db.FamzKorrekturen.FirstOrDefaultAsync(x => x.Id == id);
        if (k == null) return (null, NotFound());
        bool offen = k.Status is "OFFEN" or "VORJAHR"
                  && k.Jahr * 12 + k.Monat < dto.Year * 12 + dto.Month;
        if (!offen)
            return (null, Conflict(new { error = "NICHT_OFFEN",
                message = "Diese Warnung ist nicht mehr offen — bitte die Liste neu laden." }));
        var sperre = await _editLock.CheckZulageMonthAsync(k.EmployeeId, dto.Year, dto.Month, dto.CompanyProfileId);
        if (sperre.Locked)
            return (null, Conflict(new { error = "LOHN_EDIT_LOCKED", message = sperre.Reason }));
        return (k, null);
    }

    internal static string Vorschlagstext(FamzKorrektur k)
    {
        string art = k.Betrag >= 0 ? "Nachzahlung" : "Rückforderung";
        string name = string.IsNullOrWhiteSpace(k.ChildName) ? "" : $" {k.ChildName}";
        return $"{art} {k.Monat}/{k.Jahr}{name}";
    }
}
