using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// K3 (Walter 29.08.2026, Konzept Kap. 4 + Bauplan 2.3): Generisches
/// zinsloses MA-Darlehen / Vorschuss (z.B. «Vorschuss Hochzeit 2'000» oder
/// QST-Nachzahlung). Rückzahlung = automatischer Abzug nach Netto im
/// Definitivlauf (Engine/ConfirmPayroll); hier nur Verwaltung + Vertrag-PDF.
/// LohnEditLock: die START-Periode darf nicht in einer verarbeiteten
/// Lohnperiode liegen (CheckPeriodAsync). Bestehende RATEN sind eingefroren —
/// Löschen nur ohne Raten, sonst stornieren (stoppt künftige Raten).
/// </summary>
[ApiController]
[Route("api/employees/{employeeId:int}/darlehen")]
[Authorize(Roles = "admin,superuser")]
public class EmployeeDarlehenController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnEditLockService _editLock;
    private readonly DarlehenVertragPdfService _vertragPdf;

    public EmployeeDarlehenController(AppDbContext db, LohnEditLockService editLock,
        DarlehenVertragPdfService vertragPdf)
    {
        _db = db; _editLock = editLock; _vertragPdf = vertragPdf;
    }

    public class DarlehenDto
    {
        public int CompanyProfileId { get; set; }
        public string Zweck { get; set; } = "";
        public decimal Betrag { get; set; }
        public DateOnly? AuszahlungDatum { get; set; }
        /// <summary>Monatsrate — ODER AnzahlRaten (das andere wird gerechnet).</summary>
        public decimal? RateBetrag { get; set; }
        public int? AnzahlRaten { get; set; }
        public int StartJahr { get; set; }
        public int StartMonat { get; set; }
        /// <summary>BAR (Tresor) / LOHN (mit der Lohnzahlung) / KEINE (z.B. QST).</summary>
        public string? AuszahlungArt { get; set; }
        public string? Bemerkung { get; set; }
        /// <summary>OFFENE qst_korrektur-Posten, die in dieses Darlehen gewandelt werden (Status → IN_DARLEHEN).</summary>
        public int[]? QstKorrekturIds { get; set; }
    }

    // GET api/employees/{id}/darlehen — Liste mit Rest + Raten-Historie
    [HttpGet]
    public async Task<IActionResult> GetAll(int employeeId)
    {
        var darlehen = await _db.EmployeeDarlehen
            .Where(d => d.EmployeeId == employeeId)
            .OrderByDescending(d => d.Id)
            .ToListAsync();
        var raten = await _db.EmployeeDarlehenRaten
            .Where(r => r.EmployeeId == employeeId)
            .OrderBy(r => r.PeriodYear).ThenBy(r => r.PeriodMonth)
            .ToListAsync();

        return Ok(darlehen.Select(d =>
        {
            var eigene = raten.Where(r => r.DarlehenId == d.Id).ToList();
            var bezahlt = Math.Round(eigene.Sum(r => r.Betrag), 2);
            return new
            {
                d.Id, d.CompanyProfileId, d.Zweck, d.Betrag, d.AuszahlungDatum,
                d.AuszahlungArt,
                d.RateBetrag, d.StartJahr, d.StartMonat, d.Status, d.Bemerkung,
                d.CreatedAt, d.CreatedBy,
                bezahlt,
                rest = Math.Round(d.Betrag - bezahlt, 2),
                anzahlRatenGeplant = d.RateBetrag > 0 ? (int)Math.Ceiling(d.Betrag / d.RateBetrag) : 0,
                raten = eigene.Select(r => new { r.Id, r.PeriodYear, r.PeriodMonth, r.Betrag, r.SaldoNachher })
            };
        }));
    }

    // POST api/employees/{id}/darlehen
    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, [FromBody] DarlehenDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Zweck))
            return BadRequest(new { error = "ZWECK_FEHLT", message = "Bitte einen Verwendungszweck angeben (z.B. «Vorschuss Hochzeit»)." });
        if (dto.Betrag <= 0)
            return BadRequest(new { error = "BETRAG_UNGUELTIG", message = "Der Darlehensbetrag muss grösser als 0 sein." });
        if (dto.StartMonat is < 1 or > 12 || dto.StartJahr < 2020)
            return BadRequest(new { error = "START_UNGUELTIG", message = "Ungültige Start-Periode." });

        // Auszahlungsart (Walter 29.08.2026): BAR / LOHN / KEINE.
        var art = NormalizeAuszahlungArt(dto.AuszahlungArt);
        if (art is null)
            return BadRequest(new { error = "AUSZAHLUNG_ART_UNGUELTIG", message = "Auszahlungsart muss BAR, LOHN oder KEINE sein." });
        if (art == "LOHN" && dto.AuszahlungDatum is null)
            return BadRequest(new { error = "AUSZAHLUNG_DATUM_FEHLT",
                message = "Bei Auszahlung mit dem Lohn bitte das Auszahlungsdatum angeben — es bestimmt die Lohnperiode der Auszahlung." });

        // Rate aus Anzahl ODER direkt (Konzept: das andere wird gerechnet,
        // letzte Rate = Rest). Rundung auf 0.05 aufwärts, damit die letzte
        // Rate nie grösser als die regulären wird.
        decimal rate;
        if (dto.RateBetrag is > 0) rate = Math.Round(dto.RateBetrag.Value, 2);
        else if (dto.AnzahlRaten is > 0)
            rate = Math.Ceiling(dto.Betrag / dto.AnzahlRaten.Value * 20m) / 20m;
        else
            return BadRequest(new { error = "RATE_FEHLT", message = "Bitte Monatsrate ODER Anzahl Raten angeben." });
        if (rate <= 0 || rate > dto.Betrag) rate = Math.Min(Math.Max(rate, 0.05m), dto.Betrag);

        // Edit-Lock: Start-Periode darf nicht eingefroren sein.
        var lockRes = await _editLock.CheckPeriodAsync(User, dto.CompanyProfileId, dto.StartJahr, dto.StartMonat);
        if (lockRes.Locked)
            return Conflict(new { error = "LOHN_EDIT_LOCKED", message = lockRes.Reason,
                                  firstAllowedDate = lockRes.FirstAllowedDate?.ToString("yyyy-MM-dd") });

        var actor = User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        var d = new EmployeeDarlehen
        {
            EmployeeId       = employeeId,
            CompanyProfileId = dto.CompanyProfileId,
            Zweck            = dto.Zweck.Trim(),
            Betrag           = Math.Round(dto.Betrag, 2),
            AuszahlungDatum  = dto.AuszahlungDatum,
            AuszahlungArt    = art,
            RateBetrag       = rate,
            StartJahr        = dto.StartJahr,
            StartMonat       = dto.StartMonat,
            Status           = "OFFEN",
            Bemerkung        = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim(),
            CreatedAt        = DateTime.Now,
            CreatedBy        = actor
        };
        _db.EmployeeDarlehen.Add(d);

        // QST-Posten in Darlehen wandeln (Konzept Kap. 4): OFFENE Posten des
        // MA → IN_DARLEHEN (K2 verrechnet sie dann NICHT mehr im Lohnlauf —
        // die Schuld läuft über die Raten; K1 zählt IN_DARLEHEN als bezahlt).
        int gewandelt = 0;
        if (dto.QstKorrekturIds is { Length: > 0 })
        {
            var posten = await _db.QstKorrekturen
                .Where(k => dto.QstKorrekturIds.Contains(k.Id)
                         && k.EmployeeId == employeeId
                         && k.Status == "OFFEN")
                .ToListAsync();
            foreach (var k in posten) { k.Status = "IN_DARLEHEN"; gewandelt++; }
        }

        await _db.SaveChangesAsync();
        return Ok(new { d.Id, d.RateBetrag, qstPostenGewandelt = gewandelt });
    }

    // PUT api/employees/{id}/darlehen/{darlehenId} — Zweck/Bemerkung immer;
    // Betrag/Rate/Start nur solange KEINE Rate verrechnet ist (danach ist der
    // Plan Vertragbestandteil; Rate-Anpassung = neues Darlehen/Absprache).
    [HttpPut("{darlehenId:int}")]
    public async Task<IActionResult> Update(int employeeId, int darlehenId, [FromBody] DarlehenDto dto)
    {
        var d = await _db.EmployeeDarlehen
            .FirstOrDefaultAsync(x => x.Id == darlehenId && x.EmployeeId == employeeId);
        if (d is null) return NotFound();

        bool hatRaten = await _db.EmployeeDarlehenRaten.AnyAsync(r => r.DarlehenId == d.Id);

        d.Zweck     = string.IsNullOrWhiteSpace(dto.Zweck) ? d.Zweck : dto.Zweck.Trim();
        d.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        d.AuszahlungDatum = dto.AuszahlungDatum ?? d.AuszahlungDatum;
        var artUpd = string.IsNullOrWhiteSpace(dto.AuszahlungArt)
            ? null : NormalizeAuszahlungArt(dto.AuszahlungArt);
        if (artUpd is not null)
        {
            if (artUpd == "LOHN" && d.AuszahlungDatum is null)
                return BadRequest(new { error = "AUSZAHLUNG_DATUM_FEHLT",
                    message = "Bei Auszahlung mit dem Lohn bitte das Auszahlungsdatum angeben — es bestimmt die Lohnperiode der Auszahlung." });
            d.AuszahlungArt = artUpd;
        }

        if (!hatRaten)
        {
            if (dto.Betrag > 0) d.Betrag = Math.Round(dto.Betrag, 2);
            if (dto.RateBetrag is > 0) d.RateBetrag = Math.Round(dto.RateBetrag.Value, 2);
            else if (dto.AnzahlRaten is > 0)
                d.RateBetrag = Math.Ceiling(d.Betrag / dto.AnzahlRaten.Value * 20m) / 20m;
            if (dto.StartMonat is >= 1 and <= 12 && dto.StartJahr >= 2020)
            {
                var lockRes = await _editLock.CheckPeriodAsync(User, d.CompanyProfileId, dto.StartJahr, dto.StartMonat);
                if (lockRes.Locked)
                    return Conflict(new { error = "LOHN_EDIT_LOCKED", message = lockRes.Reason,
                                          firstAllowedDate = lockRes.FirstAllowedDate?.ToString("yyyy-MM-dd") });
                d.StartJahr = dto.StartJahr; d.StartMonat = dto.StartMonat;
            }
        }
        else if (dto.Betrag > 0 && Math.Round(dto.Betrag, 2) != d.Betrag)
        {
            return Conflict(new { error = "RATEN_VORHANDEN",
                message = "Es wurden bereits Raten verrechnet — Betrag/Rate/Start sind eingefroren. Für Änderungen: Darlehen stornieren und neu erfassen (Rest als neues Darlehen)." });
        }

        await _db.SaveChangesAsync();
        return Ok(new { d.Id });
    }

    // POST api/employees/{id}/darlehen/{darlehenId}/stornieren — stoppt
    // künftige Raten (bereits verrechnete bleiben; Rest via Absprache/e-Banking).
    [HttpPost("{darlehenId:int}/stornieren")]
    public async Task<IActionResult> Stornieren(int employeeId, int darlehenId)
    {
        var d = await _db.EmployeeDarlehen
            .FirstOrDefaultAsync(x => x.Id == darlehenId && x.EmployeeId == employeeId);
        if (d is null) return NotFound();
        d.Status = "STORNIERT";
        await _db.SaveChangesAsync();
        return Ok(new { d.Id, d.Status });
    }

    // DELETE api/employees/{id}/darlehen/{darlehenId} — nur OHNE Raten.
    [HttpDelete("{darlehenId:int}")]
    public async Task<IActionResult> Delete(int employeeId, int darlehenId)
    {
        var d = await _db.EmployeeDarlehen
            .FirstOrDefaultAsync(x => x.Id == darlehenId && x.EmployeeId == employeeId);
        if (d is null) return NotFound();
        if (await _db.EmployeeDarlehenRaten.AnyAsync(r => r.DarlehenId == d.Id))
            return Conflict(new { error = "RATEN_VORHANDEN",
                message = "Es wurden bereits Raten verrechnet — Löschen nicht möglich. Stattdessen stornieren." });
        _db.EmployeeDarlehen.Remove(d);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // GET api/employees/{id}/darlehen/{darlehenId}/vertrag-pdf
    [HttpGet("{darlehenId:int}/vertrag-pdf")]
    public async Task<IActionResult> VertragPdf(int employeeId, int darlehenId)
    {
        var d = await _db.EmployeeDarlehen
            .FirstOrDefaultAsync(x => x.Id == darlehenId && x.EmployeeId == employeeId);
        if (d is null) return NotFound();
        var emp = await _db.Employees.FindAsync(employeeId);
        if (emp is null) return NotFound();
        var cp = await _db.CompanyProfiles.FindAsync(d.CompanyProfileId);

        var pdf = _vertragPdf.Generate(new DarlehenVertragPdfService.DarlehenVertragData(
            ArbeitgeberName:    cp?.CompanyName,
            ArbeitgeberStrasse: cp?.Street,
            ArbeitgeberPlzOrt:  $"{cp?.ZipCode} {cp?.City}".Trim(),
            MaName:             $"{emp.FirstName} {emp.LastName}".Trim(),
            MaStrasse:          emp.Street,
            MaPlzOrt:           $"{emp.ZipCode} {emp.City}".Trim(),
            MaGeburtsdatum:     emp.DateOfBirth?.ToString("dd.MM.yyyy"),
            Zweck:              d.Zweck,
            Betrag:             d.Betrag,
            RateBetrag:         d.RateBetrag,
            StartJahr:          d.StartJahr,
            StartMonat:         d.StartMonat,
            AuszahlungDatum:    d.AuszahlungDatum,
            AuszahlungArt:      d.AuszahlungArt,
            AgVertreterName:    null));

        return File(pdf, "application/pdf",
            $"Darlehensvertrag_{emp.LastName}_{d.Id}.pdf");
    }

    /// <summary>null-tolerante Normalisierung: leer → null (kein Update) bzw.
    /// im Create-Pfad Default BAR; ungültig → null (Create validiert).</summary>
    private static string? NormalizeAuszahlungArt(string? art)
    {
        if (string.IsNullOrWhiteSpace(art)) return "BAR";
        var a = art.Trim().ToUpperInvariant();
        return a is "BAR" or "LOHN" or "KEINE" ? a : null;
    }
}
