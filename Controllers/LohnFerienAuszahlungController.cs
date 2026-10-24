using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.LohnFormelwerk;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Ferien auszahlen ohne Bezug — Erfassung im Lohnlauf (Walter 11.10.2026: «In den
/// Absenzen erfassen die Geschäftsführer die effektiven Absenzen. Korrekturen nur im
/// Lohnlauf.»). Tabelle <c>ferien_auszahlung</c> und Rechnung bleiben unverändert
/// (<see cref="FerienAuszahlungRechnung"/>); das Datum ergibt sich aus der Lohnperiode.
/// Sperre wie Zulagen/Abzüge (<see cref="LohnEditLockService.CheckZulageMonthAsync"/>).
/// </summary>
[ApiController]
[Route("api/lohn-ferien-auszahlung")]
public class LohnFerienAuszahlungController : ControllerBase
{
    private readonly AppDbContext        _db;
    private readonly LohnEditLockService _editLock;
    public LohnFerienAuszahlungController(AppDbContext db, LohnEditLockService editLock)
    {
        _db       = db;
        _editLock = editLock;
    }

    public record Vertrag(DateTime Start, int? CompanyProfileId);

    /// <summary>
    /// Buchungsdatum im Monat, an dem die Engine den Eintrag dieser Filiale zuordnet
    /// (jüngster Vertrag mit Start ≤ Datum). Spätester solcher Tag, sonst Monatsende.
    /// </summary>
    public static DateOnly BuchungsDatum(IEnumerable<Vertrag> vertraege, int companyProfileId, int year, int month)
    {
        var liste = vertraege.Where(v => v.CompanyProfileId != null).ToList();
        var von = new DateOnly(year, month, 1);
        var bis = von.AddMonths(1).AddDays(-1);
        for (var d = bis; d >= von; d = d.AddDays(-1))
        {
            var dt = d.ToDateTime(TimeOnly.MinValue);
            var cp = liste.Where(v => v.Start <= dt).OrderByDescending(v => v.Start).FirstOrDefault()?.CompanyProfileId;
            if (cp == null || cp == companyProfileId) return d;
        }
        return bis;
    }

    private async Task<IActionResult?> SperreAsync(int employeeId, int year, int month, int? companyProfileId)
    {
        var res = await _editLock.CheckZulageMonthAsync(employeeId, year, month, companyProfileId);
        return res.Locked ? Conflict(new { error = "LOHN_EDIT_LOCKED", message = res.Reason }) : null;
    }

    [HttpGet("{employeeId:int}/{year:int}/{month:int}")]
    public async Task<IActionResult> Liste(int employeeId, int year, int month)
    {
        var von = new DateOnly(year, month, 1);
        var bis = von.AddMonths(1).AddDays(-1);
        return Ok(await _db.FerienAuszahlungen.AsNoTracking()
            .Where(f => f.EmployeeId == employeeId && f.Datum >= von && f.Datum <= bis)
            .OrderBy(f => f.Datum).ThenBy(f => f.Id)
            .Select(f => new { f.Id, f.EmployeeId, datum = f.Datum.ToString("yyyy-MM-dd"), f.Art, f.Tage,
                               f.Bemerkung, f.ErstelltVon, f.ErstelltAm })
            .ToListAsync());
    }

    [HttpGet("{employeeId:int}/{year:int}/{month:int}/info")]
    public async Task<IActionResult> Info(int employeeId, int year, int month, [FromQuery] int? companyProfileId)
    {
        var saldi = await _db.PayrollSaldos.AsNoTracking().Where(s => s.EmployeeId == employeeId).ToListAsync();
        var vm = PayrollCalculations.WaehleVormonatsSaldo(saldi, companyProfileId ?? 0, year, month);
        object? vormonat = null;
        if (vm != null)
            vormonat = new { quelle = "LOHNLAUF", periode = $"{vm.PeriodYear}-{vm.PeriodMonth:D2}",
                             tage = Math.Round(vm.FerienTageSaldo, 2), chf = Math.Round(vm.FerienGeldSaldo, 2) };
        else
        {
            var periode = $"{year}-{month:D2}";
            var vortrag = await _db.LohnZulagen.AsNoTracking()
                .Where(z => z.EmployeeId == employeeId && z.Periode == periode
                         && (z.Lohnposition!.Code == LohnCodes.VortragFerienTage
                             || z.Lohnposition.Code == LohnCodes.VortragFerienGeld))
                .Select(z => new { z.Lohnposition!.Code, z.Betrag })
                .ToListAsync();
            if (vortrag.Count > 0)
                vormonat = new { quelle = "VORTRAG", periode,
                                 tage = Math.Round(vortrag.Where(v => v.Code == "903").Sum(v => v.Betrag), 2),
                                 chf  = Math.Round(vortrag.Where(v => v.Code == "905").Sum(v => v.Betrag), 2) };
        }
        var vj = await FerienAuszahlungDaten.SaldoVorjahrAsync(_db, employeeId, year);
        return Ok(new
        {
            vormonat,
            vorjahr = vj == null ? null : new { jahr = year - 1, tage = Math.Round(vj.Tage, 2), chf = Math.Round(vj.Chf, 2) },
        });
    }

    public class Dto
    {
        public int      EmployeeId       { get; set; }
        public int?     CompanyProfileId { get; set; }
        public int      Year             { get; set; }
        public int      Month            { get; set; }
        public string   Art              { get; set; } = FerienAuszahlungRechnung.ArtTage;
        public decimal? Tage             { get; set; }
        public string?  Bemerkung        { get; set; }
    }

    [HttpPost]
    public Task<IActionResult> Anlegen([FromBody] Dto dto) => SpeichernAsync(null, dto);

    [HttpPut("{id:int}")]
    [MaEintrag(typeof(FerienAuszahlungEintrag))]
    public Task<IActionResult> Aendern(int id, [FromBody] Dto dto) => SpeichernAsync(id, dto);

    private async Task<IActionResult> SpeichernAsync(int? id, Dto dto)
    {
        if (dto.Year < 2000 || dto.Month is < 1 or > 12)
            return BadRequest(new { error = "PERIODE", message = "Lohnperiode fehlt." });
        var art = (dto.Art ?? "").Trim().ToUpperInvariant();
        if (art != FerienAuszahlungRechnung.ArtTage && art != FerienAuszahlungRechnung.ArtVorjahr)
            return BadRequest(new { error = "ART", message = "Bitte «Anzahl Tage» oder «Saldo per 31.12.» wählen." });
        decimal? tage = null;
        if (art == FerienAuszahlungRechnung.ArtTage)
        {
            tage = Math.Round(dto.Tage ?? 0m, 2);
            if (tage <= 0)
                return BadRequest(new { error = "TAGE", message = "Bitte die Anzahl Tage angeben." });
        }

        FerienAuszahlungEintrag? e = null;
        if (id.HasValue)
        {
            e = await _db.FerienAuszahlungen.FirstOrDefaultAsync(f => f.Id == id.Value);
            if (e == null) return NotFound();
            var sperreAlt = await SperreAsync(e.EmployeeId, e.Datum.Year, e.Datum.Month, dto.CompanyProfileId);
            if (sperreAlt != null) return sperreAlt;
        }
        int empId = e?.EmployeeId ?? dto.EmployeeId;
        var sperre = await SperreAsync(empId, dto.Year, dto.Month, dto.CompanyProfileId);
        if (sperre != null) return sperre;

        var datum = new DateOnly(dto.Year, dto.Month, 1).AddMonths(1).AddDays(-1);
        if (dto.CompanyProfileId is int cp)
        {
            var vertraege = await _db.Employments.AsNoTracking()
                .Where(x => x.EmployeeId == empId)
                .Select(x => new Vertrag(x.ContractStartDate, x.CompanyProfileId))
                .ToListAsync();
            datum = BuchungsDatum(vertraege, cp, dto.Year, dto.Month);
        }

        if (art == FerienAuszahlungRechnung.ArtVorjahr)
        {
            bool doppelt = await _db.FerienAuszahlungen.AnyAsync(f => f.EmployeeId == empId
                && f.Art == FerienAuszahlungRechnung.ArtVorjahr && f.Datum.Year == datum.Year && f.Id != (id ?? 0));
            if (doppelt)
                return Conflict(new { error = "VORJAHR_DOPPELT",
                    message = $"Der Saldo per 31.12.{datum.Year - 1} ist schon zur Auszahlung erfasst." });
        }

        if (e == null)
        {
            e = new FerienAuszahlungEintrag { EmployeeId = empId };
            _db.FerienAuszahlungen.Add(e);
        }
        e.Datum       = datum;
        e.Art         = art;
        e.Tage        = tage;
        e.Bemerkung   = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        e.ErstelltVon = User.Identity?.Name;
        e.ErstelltAm  = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new { e.Id, datum = e.Datum.ToString("yyyy-MM-dd"), e.Art, e.Tage });
    }

    [HttpDelete("{id:int}")]
    [MaEintrag(typeof(FerienAuszahlungEintrag))]
    public async Task<IActionResult> Loeschen(int id, [FromQuery] int? companyProfileId)
    {
        var e = await _db.FerienAuszahlungen.FirstOrDefaultAsync(f => f.Id == id);
        if (e == null) return NotFound();
        var sperre = await SperreAsync(e.EmployeeId, e.Datum.Year, e.Datum.Month, companyProfileId);
        if (sperre != null) return sperre;
        _db.FerienAuszahlungen.Remove(e);
        await _db.SaveChangesAsync();
        return Ok();
    }
}
