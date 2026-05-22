using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/social-insurance-rates")]
[Authorize]
public class SocialInsuranceRatesController : ControllerBase
{
    private readonly AppDbContext _db;
    public SocialInsuranceRatesController(AppDbContext db) => _db = db;

    // GET – alle Sätze (aktiv + inaktiv), sortiert.
    // Liefert pro Zeile ein Flag `inLohnVerwendet` mit dem das Frontend
    // entscheidet, ob „Bearbeiten" gesperrt sein muss (Walter-Vorgabe
    // 18.05.2026: sobald ein abgeschlossener oder bei HR liegender Lohnlauf
    // den Satz verwendet hat, darf er nicht mehr direkt geändert werden —
    // stattdessen muss „Neu ab" eine Nachfolge-Version anlegen).
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var rates = await _db.SocialInsuranceRates
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Code)
            .ThenBy(r => r.ValidFrom)
            .ToListAsync();

        // Alle „eingefrorenen" Perioden vorladen — entweder Definitiv-Status
        // != offen ODER Akonto-Status jenseits der GF-Bearbeitung.
        var frozenPerioden = await _db.PayrollPerioden
            .Where(p => p.Status != "offen"
                     || (p.AkontoStatus != "OFFEN"
                      && p.AkontoStatus != "IN_BEARBEITUNG_GF"))
            .Select(p => new { p.PeriodFrom, p.PeriodTo })
            .ToListAsync();

        var result = rates.Select(r => new
        {
            r.Id, r.Code, r.Name, r.Description, r.Rate, r.RateEmployer, r.BasisType,
            r.EmploymentModelCode, r.MinAge, r.MaxAge,
            r.FreibetragMonthly, r.CoordinationDeduction, r.MaxBaseMonthly, r.MaxBaseFlatMonthly,
            r.MinBaseMonthly, r.EntryThresholdYearly,
            r.OnlyQuellensteuer, r.FibuPosition, r.ValidFrom, r.ValidTo,
            r.SortOrder, r.IsActive, r.CreatedAt,
            inLohnVerwendet = frozenPerioden.Any(p =>
                r.ValidFrom <= p.PeriodTo
             && (r.ValidTo == null || r.ValidTo >= p.PeriodFrom))
        });
        return Ok(result);
    }

    /// <summary>
    /// Prüft, ob ein konkreter SV-Satz schon in einem nicht-offenen
    /// Lohnlauf (Definitiv != offen ODER Akonto NOT IN OFFEN/IN_BEARBEITUNG_GF)
    /// verwendet wurde. Wird vom Update- und Neu-Version-Pfad aufgerufen.
    /// </summary>
    private async Task<bool> IsRateInLohnVerwendetAsync(SocialInsuranceRate rate)
    {
        return await _db.PayrollPerioden.AnyAsync(p =>
            (p.Status != "offen"
                || (p.AkontoStatus != "OFFEN" && p.AkontoStatus != "IN_BEARBEITUNG_GF"))
         && rate.ValidFrom <= p.PeriodTo
         && (rate.ValidTo == null || rate.ValidTo >= p.PeriodFrom));
    }

    // GET – nur aktuell gültige Sätze für ein bestimmtes Datum
    [HttpGet("effective")]
    public async Task<IActionResult> GetEffective([FromQuery] DateOnly? date)
    {
        var refDate = date ?? DateOnly.FromDateTime(DateTime.Today);
        var rates = await _db.SocialInsuranceRates
            .Where(r => r.IsActive
                     && r.ValidFrom <= refDate
                     && (r.ValidTo == null || r.ValidTo >= refDate))
            .OrderBy(r => r.SortOrder)
            .ThenBy(r => r.Code)
            .ToListAsync();
        return Ok(rates);
    }

    // POST – neuen Satz anlegen
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SocialInsuranceRate dto)
    {
        // Duplikat-Schutz: kein zweiter Eintrag mit denselben Schlüsselfeldern
        // und identischem Gültig-ab-Datum. Verhindert, dass durch Doppelklick
        // im Admin-UI oder paralleles Bearbeiten zwei "gleiche" Sätze entstehen,
        // die der PayrollController sonst zur Laufzeit deduplizieren muss.
        var duplicate = await _db.SocialInsuranceRates.AnyAsync(r =>
                r.Code == dto.Code
             && r.MinAge == dto.MinAge
             && r.MaxAge == dto.MaxAge
             && r.EmploymentModelCode == dto.EmploymentModelCode
             && r.OnlyQuellensteuer == dto.OnlyQuellensteuer
             && r.BasisType == dto.BasisType
             && r.ValidFrom == dto.ValidFrom);
        if (duplicate)
            return Conflict(new {
                error = $"Ein SV-Satz '{dto.Code}' mit gleichem Filter und Gültig-ab {dto.ValidFrom:yyyy-MM-dd} existiert bereits."
            });

        dto.Id        = 0;
        dto.IsActive  = true;
        dto.CreatedAt = DateTime.UtcNow;
        _db.SocialInsuranceRates.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(dto);
    }

    // PUT – Satz aktualisieren.
    // Sperre: wenn der Satz in einer eingefrorenen Periode liegt, wird 409
    // zurückgegeben; der User muss stattdessen „Neu ab" verwenden.
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SocialInsuranceRate dto)
    {
        var rate = await _db.SocialInsuranceRates.FindAsync(id);
        if (rate is null) return NotFound();

        if (await IsRateInLohnVerwendetAsync(rate))
        {
            return Conflict(new
            {
                error   = "SV_RATE_LOCKED",
                message = "Dieser SV-Satz wurde bereits in einer Lohnabrechnung verwendet - Direkt-Bearbeiten ist gesperrt. Bitte 'Neu ab' verwenden, um eine Nachfolge-Version mit neuem Gueltig-ab-Datum anzulegen."
            });
        }

        rate.Code                  = dto.Code;
        rate.Name                  = dto.Name;
        rate.Description           = dto.Description;
        rate.Rate                  = dto.Rate;
        rate.RateEmployer          = dto.RateEmployer;
        rate.BasisType             = dto.BasisType;
        rate.EmploymentModelCode   = dto.EmploymentModelCode;
        rate.MinAge                = dto.MinAge;
        rate.MaxAge                = dto.MaxAge;
        rate.FreibetragMonthly     = dto.FreibetragMonthly;
        rate.CoordinationDeduction = dto.CoordinationDeduction;
        rate.MaxBaseMonthly        = dto.MaxBaseMonthly;
        rate.MaxBaseFlatMonthly    = dto.MaxBaseFlatMonthly;
        rate.MinBaseMonthly        = dto.MinBaseMonthly;
        rate.EntryThresholdYearly  = dto.EntryThresholdYearly;
        rate.OnlyQuellensteuer     = dto.OnlyQuellensteuer;
        rate.FibuPosition          = dto.FibuPosition;
        rate.ValidFrom             = dto.ValidFrom;
        rate.ValidTo               = dto.ValidTo;
        rate.SortOrder             = dto.SortOrder;
        rate.IsActive              = dto.IsActive;

        await _db.SaveChangesAsync();
        return Ok(rate);
    }

    /// <summary>
    /// Versionierung: legt eine Nachfolge-Zeile mit neuem Gültig-ab an und
    /// begrenzt den Vorgänger atomisch auf ValidTo = neu.ValidFrom − 1 Tag.
    /// Walter-Vorgabe 18.05.2026 — Standard-Pattern für versionierte Stammdaten
    /// wie Bank/Vertrag/QST.
    /// </summary>
    [HttpPost("{id:int}/new-version")]
    public async Task<IActionResult> CreateNewVersion(int id, [FromBody] SocialInsuranceRate dto)
    {
        var oldRate = await _db.SocialInsuranceRates.FindAsync(id);
        if (oldRate is null) return NotFound();

        if (dto.ValidFrom <= oldRate.ValidFrom)
            return BadRequest(new
            {
                error   = "INVALID_VALID_FROM",
                message = $"Das neue Gültig-ab ({dto.ValidFrom:yyyy-MM-dd}) muss nach dem alten ({oldRate.ValidFrom:yyyy-MM-dd}) liegen."
            });

        // Falls Vorgänger schon eine ValidTo hat und das neue ValidFrom danach liegt,
        // entstünde eine Lücke — auch erlaubt, aber transparent halten.
        if (oldRate.ValidTo.HasValue && dto.ValidFrom > oldRate.ValidTo.Value.AddDays(1))
        {
            // Kein Fehler — Lücke kann gewollt sein (z.B. Pause in der Pflicht).
        }

        // Vorgänger atomisch begrenzen
        oldRate.ValidTo = dto.ValidFrom.AddDays(-1);

        // Neue Zeile mit den übermittelten Werten (Schlüsselfelder dürfen
        // sich nicht ändern — sonst wäre's kein Nachfolger sondern ein
        // anderer Satz; daher aus oldRate übernehmen, nur Rate und „Soft-Felder"
        // sowie Datum aus dto).
        var newRate = new SocialInsuranceRate
        {
            Code                  = oldRate.Code,
            Name                  = string.IsNullOrWhiteSpace(dto.Name) ? oldRate.Name : dto.Name,
            Description           = dto.Description ?? oldRate.Description,
            Rate                  = dto.Rate,
            RateEmployer          = dto.RateEmployer ?? oldRate.RateEmployer,
            BasisType             = oldRate.BasisType,
            EmploymentModelCode   = oldRate.EmploymentModelCode,
            MinAge                = oldRate.MinAge,
            MaxAge                = oldRate.MaxAge,
            FreibetragMonthly     = dto.FreibetragMonthly ?? oldRate.FreibetragMonthly,
            CoordinationDeduction = dto.CoordinationDeduction ?? oldRate.CoordinationDeduction,
            MaxBaseMonthly        = dto.MaxBaseMonthly ?? oldRate.MaxBaseMonthly,
            MaxBaseFlatMonthly    = dto.MaxBaseFlatMonthly ?? oldRate.MaxBaseFlatMonthly,
            MinBaseMonthly        = dto.MinBaseMonthly ?? oldRate.MinBaseMonthly,
            EntryThresholdYearly  = dto.EntryThresholdYearly ?? oldRate.EntryThresholdYearly,
            OnlyQuellensteuer     = oldRate.OnlyQuellensteuer,
            FibuPosition          = dto.FibuPosition ?? oldRate.FibuPosition,
            ValidFrom             = dto.ValidFrom,
            ValidTo               = dto.ValidTo,
            SortOrder             = dto.SortOrder == 0 ? oldRate.SortOrder : dto.SortOrder,
            IsActive              = true,
            CreatedAt             = DateTime.UtcNow,
        };
        _db.SocialInsuranceRates.Add(newRate);

        // SaveChangesAsync läuft in EF Core implizit als Transaktion
        // (alle Änderungen werden in einem DB-Roundtrip committet).
        await _db.SaveChangesAsync();
        return Ok(newRate);
    }

    // DELETE – soft-delete
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var rate = await _db.SocialInsuranceRates.FindAsync(id);
        if (rate is null) return NotFound();
        rate.IsActive = false;
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
