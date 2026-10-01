using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
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
    private readonly LohnEditLockService _editLock;
    public SocialInsuranceRatesController(AppDbContext db, LohnEditLockService editLock)
    {
        _db = db;
        _editLock = editLock;
    }

    private static DateOnly Heute => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Gleicher Fach-Schlüssel = Vorgänger/Nachfolger desselben Satzes.</summary>
    private static bool GleicherSatz(SocialInsuranceRate a, SocialInsuranceRate b) =>
        a.Code == b.Code && a.MinAge == b.MinAge && a.MaxAge == b.MaxAge
        && a.EmploymentModelCode == b.EmploymentModelCode && a.OnlyQuellensteuer == b.OnlyQuellensteuer
        && a.BasisType == b.BasisType && a.CompanyProfileId == b.CompanyProfileId
        && a.Gender == b.Gender && (a.LoesungsCode ?? "") == (b.LoesungsCode ?? "");

    private async Task<List<SocialInsuranceRate>> GleicheSaetzeAsync(SocialInsuranceRate r)
        => (await _db.SocialInsuranceRates.Where(x => x.Code == r.Code && x.Id != r.Id && x.IsActive).ToListAsync())
            .Where(x => GleicherSatz(x, r)).ToList();

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

        var heute = DateOnly.FromDateTime(DateTime.Today);
        var result = rates.Select(r => new
        {
            zeitlage = StammdatenVersionRegel.Zeitlage(r.ValidFrom, r.ValidTo, heute),
            bearbeitbar = StammdatenVersionRegel.Bearbeitbar(
                StammdatenVersionRegel.Zeitlage(r.ValidFrom, r.ValidTo, heute),
                frozenPerioden.Any(p => r.ValidFrom <= p.PeriodTo && (r.ValidTo == null || r.ValidTo >= p.PeriodFrom))),
            r.Id, r.Code, r.Name, r.Description, r.Rate, r.RateEmployer, r.BasisType,
            r.EmploymentModelCode, r.MinAge, r.MaxAge,
            r.FreibetragMonthly, r.CoordinationDeduction, r.MaxBaseMonthly, r.MaxBaseFlatMonthly,
            r.MinBaseMonthly, r.EntryThresholdYearly,
            r.OnlyQuellensteuer, r.FibuPosition, r.ValidFrom, r.ValidTo,
            r.SortOrder, r.IsActive, r.CreatedAt,
            // SV-Sätze pro Filiale (Walter 05.08.2026): NULL = globaler Standard,
            // gesetzt = Override nur für diese Filiale.
            r.CompanyProfileId,
            // Geschlechts-Filter (Walter 06.08.2026): NULL = alle, «F»/«M».
            r.Gender,
            // Versicherungs-Lösung / Swissdec-Code + Lohnband «ab» (Walter 07.09.2026)
            r.LoesungsCode, r.IsDefaultCode, r.BandVonMonthly,
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
    [Authorize(Roles = "admin")]
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
             // Filial-Namensraum (Walter 05.08.2026): global vs. Filial-Override
             // mit gleichem Schlüssel ist KEIN Duplikat — nur gleiche Filiale
             // (bzw. beide global) kollidiert.
             && r.CompanyProfileId == dto.CompanyProfileId
             // Geschlechts-Namensraum (Walter 06.08.2026): F-/M-Zeilen desselben
             // Satzes sind KEIN Duplikat.
             && r.Gender == dto.Gender
             // Lösungs-Namensraum (Walter 07.09.2026): Zeilen «A»/«B»/«P» … sind KEIN Duplikat.
             && (r.LoesungsCode ?? "") == (dto.LoesungsCode ?? "")
             && r.ValidFrom == dto.ValidFrom);
        if (duplicate)
            return Conflict(new {
                error = $"Ein SV-Satz '{dto.Code}' mit gleichem Filter und Gültig-ab {dto.ValidFrom:yyyy-MM-dd} existiert bereits."
            });

        dto.Id        = 0;
        dto.IsActive  = true;
        dto.CreatedAt = DateTime.UtcNow;
        dto.LoesungsCode   = string.IsNullOrWhiteSpace(dto.LoesungsCode) ? null : dto.LoesungsCode.Trim().ToUpperInvariant();
        dto.IsDefaultCode  = dto.LoesungsCode != null && dto.IsDefaultCode;
        dto.BandVonMonthly = dto.BandVonMonthly is > 0 ? dto.BandVonMonthly : null;
        _db.SocialInsuranceRates.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(dto);
    }

    // PUT – Satz aktualisieren.
    // Sperre: wenn der Satz in einer eingefrorenen Periode liegt, wird 409
    // zurückgegeben; der User muss stattdessen „Neu ab" verwenden.
    [Authorize(Roles = "admin")]
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

        // Datums-Regeln (Walter-Vorgabe 01.10.2026): vergangen = nie; aktuell =
        // Beginn bleibt (sonst ändert sich rückwirkend, was schon galt), Ende
        // frühestens heute; geplant = Beginn bleibt in der Zukunft.
        var lage = StammdatenVersionRegel.Zeitlage(rate.ValidFrom, rate.ValidTo, Heute);
        if (lage == StammdatenVersionRegel.Vergangen)
            return Conflict(new { error = "SV_RATE_VERGANGEN",
                message = "Dieser SV-Satz ist abgelaufen und bleibt unverändert." });
        if (lage == StammdatenVersionRegel.Aktuell && dto.ValidFrom != rate.ValidFrom)
            return Conflict(new { error = "SV_RATE_BEGINN_FIX",
                message = $"Der Satz gilt bereits seit {rate.ValidFrom:dd.MM.yyyy} — das Gültig-ab bleibt. Für eine Änderung ab einem späteren Datum «Neu ab» verwenden." });
        if (lage == StammdatenVersionRegel.Geplant && !StammdatenVersionRegel.NeuerStartErlaubt(dto.ValidFrom, Heute))
            return Conflict(new { error = "NUR_KUENFTIG",
                message = "Ein geplanter Satz muss in der Zukunft beginnen." });
        if (dto.ValidTo.HasValue && dto.ValidTo.Value < Heute && dto.ValidTo != rate.ValidTo)
            return Conflict(new { error = "SV_RATE_ENDE_VERGANGEN",
                message = "Das Gültig-bis darf nicht in der Vergangenheit liegen." });
        if (dto.ValidTo.HasValue && dto.ValidTo.Value < dto.ValidFrom)
            return BadRequest(new { error = "INVALID_VALID_TO", message = "Gültig-bis liegt vor Gültig-ab." });

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
        rate.CompanyProfileId      = dto.CompanyProfileId;
        rate.Gender                = dto.Gender;
        rate.LoesungsCode          = string.IsNullOrWhiteSpace(dto.LoesungsCode) ? null : dto.LoesungsCode.Trim().ToUpperInvariant();
        rate.IsDefaultCode         = rate.LoesungsCode != null && dto.IsDefaultCode;
        rate.BandVonMonthly        = dto.BandVonMonthly is > 0 ? dto.BandVonMonthly : null;
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
    [Authorize(Roles = "admin")]
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

        // Neue Versionen nur in der Zukunft und nicht in einer schon
        // abgerechneten Periode (Akonto kann vor Monatsbeginn laufen).
        if (!StammdatenVersionRegel.NeuerStartErlaubt(dto.ValidFrom, Heute))
            return Conflict(new { error = "NUR_KUENFTIG",
                message = $"Eine neue Version muss in der Zukunft beginnen — {dto.ValidFrom:dd.MM.yyyy} ist heute oder vorbei." });
        var firstAllowed = await _editLock.GetGlobalFirstAllowedDateAsync();
        if (firstAllowed.HasValue && dto.ValidFrom < firstAllowed.Value)
            return Conflict(new { error = "LOHN_EDIT_LOCKED",
                message = $"Das Gültig-ab {dto.ValidFrom:dd.MM.yyyy} liegt in einer bereits verarbeiteten Lohnperiode. Frühestes Datum: {firstAllowed.Value:dd.MM.yyyy}." });

        // Nur von der jüngsten Version aus — sonst würde eine bestehende
        // spätere Version überlappt.
        var spaeter = (await GleicheSaetzeAsync(oldRate)).Where(x => x.ValidFrom > oldRate.ValidFrom)
            .OrderBy(x => x.ValidFrom).FirstOrDefault();
        if (spaeter != null)
            return Conflict(new { error = "SPAETERE_VERSION",
                message = $"Für diesen Satz gibt es schon eine Version ab {spaeter.ValidFrom:dd.MM.yyyy} — «Neu ab» bitte bei dieser jüngsten Version." });

        // Vorgänger begrenzen — nur wenn er sonst über den neuen Beginn hinaus
        // liefe (ein bereits beendeter Satz bleibt, wie er ist: gewollte Lücke).
        if (oldRate.ValidTo == null || oldRate.ValidTo.Value >= dto.ValidFrom)
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
            // Filial-Zugehörigkeit ist Teil des Fach-Schlüssels — der
            // Nachfolger bleibt in derselben Filiale (bzw. global).
            CompanyProfileId      = oldRate.CompanyProfileId,
            // Geschlecht ebenso Teil des Schlüssels (F-/M-Zeilen, Walter 06.08.2026).
            Gender                = oldRate.Gender,
            LoesungsCode          = oldRate.LoesungsCode,
            IsDefaultCode         = oldRate.IsDefaultCode,
            BandVonMonthly        = dto.BandVonMonthly ?? oldRate.BandVonMonthly,
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

    // DELETE – künftige Version entfernen (z.B. Datum vertippt). Vergangene und
    // in einem Lohnlauf verwendete Sätze bleiben (Walter-Vorgabe 01.10.2026);
    // ein aktueller, unbenutzter Satz wird wie bisher nur deaktiviert. Ein
    // geplanter wird ganz gelöscht (der Unique-Index zählt inaktive Zeilen mit)
    // und sein Vorgänger gilt wieder bis zum ursprünglichen Ende.
    [Authorize(Roles = "admin")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var rate = await _db.SocialInsuranceRates.FindAsync(id);
        if (rate is null) return NotFound();

        var lage = StammdatenVersionRegel.Zeitlage(rate.ValidFrom, rate.ValidTo, Heute);
        var verwendet = await IsRateInLohnVerwendetAsync(rate);
        var grund = StammdatenVersionRegel.Sperrgrund(lage, verwendet);
        if (grund != null)
            return Conflict(new { error = "SV_RATE_LOCKED", message = grund + " Löschen ist nicht möglich." });

        if (lage == StammdatenVersionRegel.Geplant)
        {
            var vortag = rate.ValidFrom.AddDays(-1);
            var vorgaenger = (await GleicheSaetzeAsync(rate)).FirstOrDefault(x => x.ValidTo == vortag);
            if (vorgaenger != null) vorgaenger.ValidTo = rate.ValidTo;
            _db.SocialInsuranceRates.Remove(rate);
        }
        else
        {
            rate.IsActive = false;
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
