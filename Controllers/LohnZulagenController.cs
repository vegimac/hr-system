using System.Text.Json;
using System.Text.Json.Nodes;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static HrSystem.Services.PayrollCalculations;

namespace HrSystem.Controllers;

[Authorize]
[ApiController]
[Route("api")]
public class LohnZulagenController : ControllerBase
{
    private readonly AppDbContext             _db;
    private readonly LohnEditLockService      _editLock;
    private readonly PayrollCalculationEngine _calcEngine;
    public LohnZulagenController(AppDbContext db, LohnEditLockService editLock,
        PayrollCalculationEngine calcEngine)
    {
        _db         = db;
        _editLock   = editLock;
        _calcEngine = calcEngine;
    }

    /// <summary>
    /// Lohnlauf-Lock-Check für eine Periode (YYYY-MM) eines MA — Zulagen-spezifisch.
    ///
    /// Walter-Vorgabe 01.08.2026: Zulagen/Abzüge (inkl. QST-Korrektur Vormonate)
    /// bleiben während Akonto und HR-Kontrolle (<c>provisorisch_abgeschlossen</c>)
    /// erfassbar — genau dort werden Korrekturen oft noch nachgezogen.
    /// Gesperrt erst wenn der Definitiv-Lauf <c>abgeschlossen</c> ist (DTA).
    /// Der Akonto-Status allein sperrt nicht mehr (sonst keine Korrekturen
    /// im Definitivlauf nach Akonto-Auszahlung). Soft-Lock wie Verträge/QST.
    /// Filiale aus dem Lohnlauf mitgeben — sonst trifft der letzte aktive
    /// Vertrag eine andere Filiale und eine offene Periode wirkt «zu».
    /// </summary>
    private async Task<IActionResult?> CheckLohnLockAsync(
        int employeeId, string periode, int? companyProfileId = null)
    {
        if (periode.Length != 7 || periode[4] != '-') return null;
        if (!int.TryParse(periode[..4], out var y))    return null;
        if (!int.TryParse(periode[5..], out var m))    return null;

        var res = await _editLock.CheckZulageMonthAsync(employeeId, y, m, companyProfileId);
        if (!res.Locked) return null;

        return Conflict(new
        {
            error = "LOHN_EDIT_LOCKED",
            message = res.Reason,
        });
    }

    // ═══════════════════════════════════════════════════════
    //  LOHNPOSITIONEN ALS TYP-KATALOG  (für Zulagen/Abzüge-Dropdown)
    // ═══════════════════════════════════════════════════════

    /// <summary>
    /// Aktive Lohnpositionen vom Typ ZULAGE oder ABZUG — für das Erfassungs-Dropdown.
    /// Saldo-Vortrag-Lohnpositionen (Codes 901–906) werden ausgefiltert,
    /// da sie nicht als reguläre Zulagen verwendet werden sollen.
    /// </summary>
    [HttpGet("lohn-zulag-typen")]
    public async Task<IActionResult> GetZulagTypen()
    {
        var list = await _db.Lohnpositionen
            .Where(l => l.IsActive
                     && (l.Typ == "ZULAGE" || l.Typ == "ABZUG")
                     && l.Kategorie != "Saldo-Vortrag")
            .OrderBy(l => l.SortOrder)
            .ThenBy(l => l.Code)
            .Select(l => new
            {
                l.Id,
                l.Code,
                l.Bezeichnung,
                l.Typ,
                l.AhvAlvPflichtig,
                l.NbuvPflichtig,
                l.KtgPflichtig,
                l.BvgPflichtig,
                l.QstPflichtig,
                SvPflichtig = l.AhvAlvPflichtig || l.NbuvPflichtig || l.KtgPflichtig || l.BvgPflichtig,
                l.SortOrder,
                Aktiv       = l.IsActive
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>
    /// Stundensatz für Überstunden-Auszahlung 55.2 (Lohnlauf-Modal).
    /// Gleicher Satz wie Austritts-Zeitsaldo: HourlyRate, sonst Monatslohn × 12/365 ÷ (WoStd/7).
    /// </summary>
    [HttpGet("lohn-zulagen/ueberstunden-satz")]
    public async Task<IActionResult> GetUeberstundenSatz(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int? companyProfileId = null,
        [FromQuery] int? ohneEintragId = null)
    {
        var info = await LoadUeberstundenSatzAsync(employeeId, year, month, companyProfileId);
        if (info is null)
            return NotFound(new { error = "KEIN_VERTRAG", message = "Kein Vertrag in dieser Periode." });
        var verf = await LoadVerfuegbarAsync(employeeId, year, month, info.CompanyProfileId, ohneEintragId);
        return Ok(new
        {
            stundensatz = info.Stundensatz,
            modell = info.Modell,
            hourlyRate = info.HourlyRate,
            monthlySalary = info.MonthlySalary,
            weeklyHours = info.WeeklyHours,
            employmentPercentage = info.EmploymentPercentage,
            zeitsaldoVorAuszahlung = verf.ZeitsaldoVor,
            verfuegbarStunden = verf.Verfuegbar,
            verfuegbarFehler = verf.Error,
        });
    }

    private sealed record UeberstundenSatzInfo(
        decimal Stundensatz, string? Modell, decimal HourlyRate,
        decimal MonthlySalary, decimal WeeklyHours, decimal EmploymentPercentage,
        int CompanyProfileId);

    private sealed record UeberstundenVerfuegbar(
        decimal? ZeitsaldoVor, decimal? Verfuegbar, string? Error);

    /// <summary>
    /// Plus-Saldo, der mit 55.2 noch ausbezahlt werden darf: gleiche Rechnung wie der
    /// Lohnzettel (Engine), minus übrige 55.2-Stunden des Monats ausser <paramref name="ohneEintragId"/>.
    /// </summary>
    private async Task<UeberstundenVerfuegbar> LoadVerfuegbarAsync(
        int employeeId, int year, int month, int companyProfileId, int? ohneEintragId)
    {
        IActionResult calc;
        try { calc = await _calcEngine.CalculateAsync(employeeId, year, month, companyProfileId); }
        catch (Exception ex)
        {
            return new(null, null, $"Zeitsaldo konnte nicht gerechnet werden: {ex.Message}");
        }
        if (calc is not OkObjectResult ok || ok.Value is null)
            return new(null, null, "Zeitsaldo konnte nicht gerechnet werden.");

        var camel = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var node = JsonNode.Parse(JsonSerializer.Serialize(ok.Value, camel));
        var v = node?["zeitsaldoVorUeberstundenAuszahlung"];
        if (v is null)
            return new(null, null, "Dieses Vertragsmodell führt keinen Zeitsaldo — Überstunden-Auszahlung in Stunden nicht möglich.");
        decimal zeitsaldoVor;
        try { zeitsaldoVor = v.GetValue<decimal>(); }
        catch { zeitsaldoVor = (decimal)v.GetValue<double>(); }

        string periode = $"{year:D4}-{month:D2}";
        decimal uebrige = await _db.LohnZulagen
            .Where(z => z.EmployeeId == employeeId
                     && z.Periode == periode
                     && z.Lohnposition!.Code == "55.2"
                     && z.Stunden != null
                     && (ohneEintragId == null || z.Id != ohneEintragId.Value))
            .SumAsync(z => z.Stunden ?? 0m);

        return new(zeitsaldoVor, UeberstundenAuszahlbar(zeitsaldoVor, uebrige), null);
    }

    private async Task<UeberstundenSatzInfo?> LoadUeberstundenSatzAsync(
        int employeeId, int year, int month, int? companyProfileId)
    {
        var periodFrom = new DateOnly(year, month, 1);
        var periodTo   = periodFrom.AddMonths(1).AddDays(-1);

        var empQ = _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == employeeId
                     && e.ContractStartDate <= periodTo.ToDateTime(TimeOnly.MinValue)
                     && (e.ContractEndDate == null
                         || e.ContractEndDate >= periodFrom.ToDateTime(TimeOnly.MinValue)));
        if (companyProfileId.HasValue)
            empQ = empQ.Where(e => e.CompanyProfileId == companyProfileId.Value);

        var emp = await empQ
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefaultAsync();
        if (emp is null) return null;

        var company = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == emp.CompanyProfileId);

        decimal pct = emp.EmploymentPercentage ?? 100m;
        decimal hourly = emp.HourlyRate ?? 0m;
        decimal monthSal = emp.MonthlySalary
            ?? Rappen((emp.MonthlySalaryFte ?? 0m) * pct / 100m);
        decimal weekly = emp.WeeklyHours
            ?? ((company?.NormalWeeklyHours ?? 42m) * pct / 100m);
        decimal satz = UeberstundenStundensatz(hourly, monthSal, weekly);

        return new UeberstundenSatzInfo(
            Rappen(satz), emp.EmploymentModel, hourly, monthSal, weekly, pct,
            emp.CompanyProfileId ?? companyProfileId ?? 0);
    }

    // ═══════════════════════════════════════════════════════
    //  EINTRÄGE  (pro Mitarbeiter + Periode)
    // ═══════════════════════════════════════════════════════

    /// <summary>Alle Einträge eines Mitarbeiters für eine Periode (YYYY-MM).
    /// Saldo-Vortrag-Einträge (Codes 901–906, Kategorie "Saldo-Vortrag")
    /// werden hier ausgefiltert — sie werden über den separaten
    /// SaldoVortragController verwaltet und sollen nicht doppelt in der
    /// Lohn-Page-Zulagen-Liste auftauchen, wo sie irrtümlich als
    /// reguläre Zulagen erscheinen würden.</summary>
    [HttpGet("lohn-zulagen/{employeeId}/{periode}")]
    public async Task<IActionResult> GetZulagen(int employeeId, string periode)
    {
        var list = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.EmployeeId == employeeId
                     && z.Periode    == periode
                     && z.Lohnposition!.Kategorie != "Saldo-Vortrag")
            .OrderBy(z => z.Lohnposition!.SortOrder)
            .ThenBy(z => z.CreatedAt)
            .Select(z => new
            {
                z.Id,
                z.EmployeeId,
                z.Periode,
                LohnpositionId          = z.LohnpositionId,
                LohnpositionCode        = z.Lohnposition!.Code,
                LohnpositionBezeichnung = z.Lohnposition.Bezeichnung,
                Typ                     = z.Lohnposition.Typ,
                AhvAlvPflichtig         = z.Lohnposition.AhvAlvPflichtig,
                NbuvPflichtig           = z.Lohnposition.NbuvPflichtig,
                KtgPflichtig            = z.Lohnposition.KtgPflichtig,
                BvgPflichtig            = z.Lohnposition.BvgPflichtig,
                QstPflichtig            = z.Lohnposition.QstPflichtig,
                z.Betrag,
                z.Stunden,
                z.Bemerkung,
                z.CreatedAt
            })
            .ToListAsync();
        return Ok(list);
    }

    /// <summary>Neuen Eintrag erfassen</summary>
    [HttpPost("lohn-zulagen")]
    public async Task<IActionResult> CreateZulage([FromBody] LohnZulageDto dto)
    {
        if (dto.Periode.Length != 7 || dto.Periode[4] != '-')
            return BadRequest("Periode muss im Format YYYY-MM sein.");

        var locked = await CheckLohnLockAsync(dto.EmployeeId, dto.Periode, dto.CompanyProfileId);
        if (locked != null) return locked;

        var lp = await _db.Lohnpositionen.FindAsync(dto.LohnpositionId);
        if (lp is null) return BadRequest("Unbekannte Lohnposition.");
        if (lp.Typ != "ZULAGE" && lp.Typ != "ABZUG")
            return BadRequest("Lohnposition muss Typ ZULAGE oder ABZUG haben.");

        var (betrag, stunden, err) = await ResolveBetragStundenAsync(
            lp, dto.EmployeeId, dto.Periode, dto.CompanyProfileId, dto.Betrag, dto.Stunden);
        if (err != null) return BadRequest(new { message = err });

        var entry = new LohnZulage
        {
            EmployeeId     = dto.EmployeeId,
            Periode        = dto.Periode,
            LohnpositionId = dto.LohnpositionId,
            Betrag         = betrag,
            Stunden        = stunden,
            Bemerkung      = dto.Bemerkung?.Trim(),
            CreatedAt      = DateTime.Now,
            UpdatedAt      = DateTime.Now
        };
        _db.LohnZulagen.Add(entry);
        await _db.SaveChangesAsync();

        return Ok(ToResponse(entry, lp));
    }

    /// <summary>Eintrag aktualisieren (Betrag / Bemerkung / Stunden)</summary>
    [HttpPut("lohn-zulagen/{id}")]
    public async Task<IActionResult> UpdateZulage(
        int id,
        [FromBody] LohnZulageUpdateDto dto,
        [FromQuery] int? companyProfileId = null)
    {
        var entry = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .FirstOrDefaultAsync(z => z.Id == id);
        if (entry is null) return NotFound();

        var locked = await CheckLohnLockAsync(entry.EmployeeId, entry.Periode, companyProfileId);
        if (locked != null) return locked;

        var lp = entry.Lohnposition!;
        if (dto.LohnpositionId.HasValue && dto.LohnpositionId.Value != entry.LohnpositionId)
        {
            var lpNeu = await _db.Lohnpositionen.FirstOrDefaultAsync(l => l.Id == dto.LohnpositionId.Value && l.IsActive);
            if (lpNeu is null) return BadRequest("Lohnposition nicht gefunden oder inaktiv.");
            lp = lpNeu;
        }

        // Prüfung VOR jeder Änderung am Eintrag: die Saldo-Rechnung läuft über die
        // Lohn-Engine, die selbst speichern kann.
        var (betrag, stunden, err) = await ResolveBetragStundenAsync(
            lp, entry.EmployeeId, entry.Periode, companyProfileId, dto.Betrag, dto.Stunden, entry.Id);
        if (err != null) return BadRequest(new { message = err });

        entry.LohnpositionId = lp.Id;
        entry.Lohnposition   = lp;
        entry.Betrag    = betrag;
        entry.Stunden   = stunden;
        entry.Bemerkung = dto.Bemerkung?.Trim();
        entry.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        return Ok(ToResponse(entry, lp));
    }

    /// <summary>Eintrag löschen</summary>
    [HttpDelete("lohn-zulagen/{id}")]
    public async Task<IActionResult> DeleteZulage(int id, [FromQuery] int? companyProfileId = null)
    {
        var entry = await _db.LohnZulagen.FindAsync(id);
        if (entry is null) return NotFound();

        var locked = await CheckLohnLockAsync(entry.EmployeeId, entry.Periode, companyProfileId);
        if (locked != null) return locked;

        _db.LohnZulagen.Remove(entry);
        await _db.SaveChangesAsync();
        return Ok();
    }

    /// <summary>
    /// 55.2 mit Stunden → Betrag serverseitig aus Std × Satz.
    /// Andere Lohnarten: nur Betrag, Stunden = null.
    /// </summary>
    private async Task<(decimal Betrag, decimal? Stunden, string? Error)> ResolveBetragStundenAsync(
        Lohnposition lp, int employeeId, string periode, int? companyProfileId,
        decimal betrag, decimal? stunden, int? ohneEintragId = null)
    {
        if (lp.Code == "55.2" && stunden.HasValue)
        {
            if (stunden.Value <= 0m)
                return (0, null, "Stunden müssen grösser als 0 sein.");
            if (periode.Length != 7
                || !int.TryParse(periode[..4], out var y)
                || !int.TryParse(periode[5..], out var m))
                return (0, null, "Periode muss im Format YYYY-MM sein.");
            var info = await LoadUeberstundenSatzAsync(employeeId, y, m, companyProfileId);
            if (info is null)
                return (0, null, "Kein Vertrag für Stundensatz.");
            if (info.Stundensatz <= 0m)
                return (0, null, "Stundensatz konnte nicht ermittelt werden.");

            decimal std = Math.Round(stunden.Value, 2);
            var verf = await LoadVerfuegbarAsync(employeeId, y, m, info.CompanyProfileId, ohneEintragId);
            if (verf.Error != null)
                return (0, null, verf.Error);
            if (std > verf.Verfuegbar!.Value)
                return (0, null,
                    $"Höchstens {verf.Verfuegbar.Value:0.00} Std. auszahlbar — Zeitsaldo vor Auszahlung "
                    + $"{verf.ZeitsaldoVor!.Value:0.00} Std. Überstunden nur bis zum Plus-Saldo, nie ins Minus.");

            return (ExitSettlementBetrag(std, info.Stundensatz), std, null);
        }

        if (betrag == 0)
            return (0, null, "Betrag darf nicht 0 sein.");
        return (Math.Round(betrag, 2), null, null);
    }

    private static object ToResponse(LohnZulage entry, Lohnposition lp) => new
    {
        entry.Id, entry.EmployeeId, entry.Periode,
        LohnpositionId          = lp.Id,
        LohnpositionCode        = lp.Code,
        LohnpositionBezeichnung = lp.Bezeichnung,
        Typ                     = lp.Typ,
        AhvAlvPflichtig         = lp.AhvAlvPflichtig,
        NbuvPflichtig           = lp.NbuvPflichtig,
        KtgPflichtig            = lp.KtgPflichtig,
        BvgPflichtig            = lp.BvgPflichtig,
        QstPflichtig            = lp.QstPflichtig,
        entry.Betrag,
        entry.Stunden,
        entry.Bemerkung,
        entry.CreatedAt
    };
}

// ─── DTOs ───────────────────────────────────────────────────────────────────

public record LohnZulageDto(
    int      EmployeeId,
    string   Periode,
    int      LohnpositionId,
    decimal  Betrag,
    string?  Bemerkung,
    int?     CompanyProfileId = null,
    decimal? Stunden = null
);

public record LohnZulageUpdateDto(
    decimal  Betrag,
    string?  Bemerkung,
    int?     LohnpositionId = null,   // Lohnart wechseln (Walter 21.09.2026) — null = unverändert
    decimal? Stunden = null
);
