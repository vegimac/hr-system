using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

// Sicherheit (Walter-Vorgabe 23.05.2026): Filial-Stammdaten/Einstellungen ändern
// nur admin. GET (Liste/Detail) bleibt für alle Rollen offen (Filial-Selektor,
// Anzeige) — daher KEIN klassenweites Rollen-Attribut, sondern [Authorize(Roles="admin")]
// auf jedem schreibenden Endpunkt.
[ApiController]
[Route("api/[controller]")]
public class CompanyProfilesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CompanyProfilesController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    // Walter 14.06.2026: + lowuser — braucht den Filial-Selektor genauso
    // wie alle anderen (sieht nur Dashboard + Mitarbeiter + Verträge, aber
    // auch dort wird pro Filiale gefiltert).
    [Authorize(Roles = "admin,superuser,user,buchhaltung,lowuser")]
    public async Task<IActionResult> GetAll()
    {
        // Einheitliche Sortierung für ALLE Stellen, an denen Filialen
        // gelistet werden (Dashboard, Filialen-Page, Branch-Selektor,
        // Lohn-Page, Zuweisungs-Dropdowns). Primär nach Restaurant-Code
        // numerisch, sekundär nach Branch-/Firmenname.
        var profiles = await _context.CompanyProfiles
            .ToListAsync();

        profiles = profiles
            .OrderBy(p => RestaurantCodeSortKey(p.RestaurantCode))
            .ThenBy(p => p.RestaurantCode ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.BranchName ?? p.CompanyName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Ok(profiles);
    }

    /// <summary>
    /// Restaurant-Codes sind i.d.R. numerisch (z.B. "101", "205"). Falls
    /// ein Code nicht als Zahl parsbar ist (oder fehlt), wandert er ans
    /// Ende — sekundär alphabetisch sortiert.
    /// </summary>
    private static int RestaurantCodeSortKey(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return int.MaxValue;
        return int.TryParse(code.Trim(), out var n) ? n : int.MaxValue;
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var profile = await _context.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == id);

        if (profile == null)
            return NotFound();

        return Ok(profile);
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create(CompanyProfile profile)
    {
        _context.CompanyProfiles.Add(profile);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = profile.Id }, profile);
    }

    // PATCH /api/companyprofiles/{id}/nighthours
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/nighthours")]
    public async Task<IActionResult> UpdateNightHours(int id, [FromBody] NightHoursDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.NightStartTime = dto.NightStartTime;
        profile.NightEndTime   = dto.NightEndTime;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record NightHoursDto(string NightStartTime, string NightEndTime);

    // PATCH /api/companyprofiles/{id}/max-weekly-hours
    // Maximale gestempelte Stunden pro Woche (Mo–So) — reine Anzeige-/Warngrenze
    // im Stempelzeiten-Tab. NULL = keine Grenze. (Walter-Vorgabe 24.05.2026)
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/max-weekly-hours")]
    public async Task<IActionResult> UpdateMaxWeeklyHours(int id, [FromBody] MaxWeeklyHoursDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.MaxWeeklyHours = dto.MaxWeeklyHours;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record MaxWeeklyHoursDto(decimal? MaxWeeklyHours);

    // PATCH /api/companyprofiles/{id}/vacation-six-weeks-from-age
    // Alter, ab dem die 6-Wochen-Ferien-Regel greift. L-GAV-Standard = 50.
    // Wird in PayrollCalculationEngine pro Lohnperiode geprüft (sobald der
    // X-te Geburtstag ≤ periodTo, gilt 13.04 %). (Walter-Vorgabe 06.06.2026)
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/vacation-six-weeks-from-age")]
    public async Task<IActionResult> UpdateVacationSixWeeksFromAge(int id, [FromBody] VacationSixWeeksFromAgeDto dto)
    {
        if (dto.VacationSixWeeksFromAge < 0 || dto.VacationSixWeeksFromAge > 100)
            return BadRequest(new { error = "Alter muss zwischen 0 und 100 liegen." });

        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.VacationSixWeeksFromAge = dto.VacationSixWeeksFromAge;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record VacationSixWeeksFromAgeDto(int VacationSixWeeksFromAge);

    // PATCH /api/companyprofiles/{id}/default-thirteenth-percent
    // 13.-Monatslohn-% pro Filiale (L-GAV-Standard 8.33). Engine, Importer und
    // Arbeitsvertrags-PDF fallen darauf zurück, wenn der Vertrag keinen Wert
    // hat. (Walter-Vorgabe 06.06.2026)
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/default-thirteenth-percent")]
    public async Task<IActionResult> UpdateDefaultThirteenthPercent(int id, [FromBody] DefaultThirteenthPercentDto dto)
    {
        if (dto.DefaultThirteenthSalaryPercent != null
            && (dto.DefaultThirteenthSalaryPercent < 0 || dto.DefaultThirteenthSalaryPercent > 100))
            return BadRequest(new { error = "13. ML % muss zwischen 0 und 100 liegen." });

        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.DefaultThirteenthSalaryPercent = dto.DefaultThirteenthSalaryPercent;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record DefaultThirteenthPercentDto(decimal? DefaultThirteenthSalaryPercent);

    // PATCH /api/companyprofiles/{id}/probation
    // Probezeit-Vorgabe pro Filiale (Walter-Vorgabe 29.06.2026): gespeichert als
    // 14 = 14 Tage, 1/2/3 = Monate. NULL = keine Vorgabe. KEINE manuelle
    // Verlängerung (verlängert sich später automatisch bei Krank/Unfall/Absenz).
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/probation")]
    public async Task<IActionResult> UpdateProbation(int id, [FromBody] ProbationDto dto)
    {
        if (dto.ProbationMonths != null && dto.ProbationMonths != 14
            && dto.ProbationMonths != 1 && dto.ProbationMonths != 2 && dto.ProbationMonths != 3)
            return BadRequest(new { error = "Probezeit muss 14 (Tage), 1, 2 oder 3 (Monate) sein." });

        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.ProbationMonths = dto.ProbationMonths;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record ProbationDto(int? ProbationMonths);

    // PATCH /api/companyprofiles/{id}/alv
    // Legacy-Endpoint, bleibt aus Rückwärtskompatibilität — neuer Code soll
    // /stammdaten verwenden, der alle Stammdaten in einem Rutsch updated.
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/alv")]
    public async Task<IActionResult> UpdateAlvDaten(int id, [FromBody] AlvDatenDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.BurNummer      = dto.BurNummer;
        profile.BranchenCode   = dto.BranchenCode;
        profile.AhvKasse       = dto.AhvKasse;
        profile.BvgVersicherer = dto.BvgVersicherer;
        profile.IstGav         = dto.IstGav;
        profile.GavName        = dto.GavName;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record AlvDatenDto(
        string? BurNummer,
        string? BranchenCode,
        string? AhvKasse,
        string? BvgVersicherer,
        bool    IstGav,
        string? GavName
    );

    // PATCH /api/companyprofiles/{id}/stammdaten
    // Vollständige Filial-Stammdaten (Adresse, Kontakt, Kanton, Sozialvers.,
    // GAV, BUR/Branchen-Code) in einem Rutsch. Ersetzt den ALV-Sub-Modal-Flow
    // — die UI öffnet jetzt EIN Stammdaten-Modal.
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/stammdaten")]
    public async Task<IActionResult> UpdateStammdaten(int id, [FromBody] CompanyStammdatenDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        // Adresse
        profile.CompanyName    = string.IsNullOrWhiteSpace(dto.CompanyName)    ? profile.CompanyName : dto.CompanyName.Trim();
        profile.BranchName     = string.IsNullOrWhiteSpace(dto.BranchName)     ? null : dto.BranchName.Trim();
        profile.RestaurantCode = string.IsNullOrWhiteSpace(dto.RestaurantCode) ? null : dto.RestaurantCode.Trim();
        profile.Street         = string.IsNullOrWhiteSpace(dto.Street)         ? null : dto.Street.Trim();
        profile.HouseNumber    = string.IsNullOrWhiteSpace(dto.HouseNumber)    ? null : dto.HouseNumber.Trim();
        profile.ZipCode        = string.IsNullOrWhiteSpace(dto.ZipCode)        ? null : dto.ZipCode.Trim();
        profile.City           = string.IsNullOrWhiteSpace(dto.City)           ? null : dto.City.Trim();
        profile.Country        = string.IsNullOrWhiteSpace(dto.Country)        ? null : dto.Country.Trim();
        // Arbeitsort im Vertragstext («im Restaurant in X») — leer = Fallback
        // auf den Ort (ContractPdfBuilder, Walter 05.08.2026).
        profile.WorkLocation   = string.IsNullOrWhiteSpace(dto.WorkLocation)   ? null : dto.WorkLocation.Trim();

        // Standort-Kanton (für Familienzulagen)
        if (string.IsNullOrWhiteSpace(dto.KantonCode))
        {
            profile.KantonCode = null;
        }
        else
        {
            var k = dto.KantonCode.Trim().ToUpperInvariant();
            if (k.Length != 2)
                return BadRequest(new { message = "Kanton-Code muss exakt 2 Zeichen haben (z.B. LU, AG, BE)." });
            profile.KantonCode = k;
        }

        // Kontakt
        profile.Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim();
        profile.Email = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim();

        // ALV / Sozialversicherungen / GAV
        profile.BurNummer      = string.IsNullOrWhiteSpace(dto.BurNummer)      ? null : dto.BurNummer.Trim();
        profile.UidNummer      = string.IsNullOrWhiteSpace(dto.UidNummer)      ? null : dto.UidNummer.Trim();
        profile.HauptsitzId    = dto.HauptsitzId; // Zuordnung Rechtseinheit (Walter 29.08.2026)
        profile.BranchenCode   = string.IsNullOrWhiteSpace(dto.BranchenCode)   ? null : dto.BranchenCode.Trim();
        // AhvKasse/BvgVersicherer: seit 06.08.2026 aus den Lohndatenempfängern
        // abgeleitet (Walter) — hier NICHT mehr mutieren, sonst nullt jeder
        // Stammdaten-Save die Legacy-Werte (UI sendet die Felder nicht mehr).
        profile.IstGav         = dto.IstGav ?? profile.IstGav;
        profile.GavName        = string.IsNullOrWhiteSpace(dto.GavName)        ? null : dto.GavName.Trim();

        // Lohnausweis-Standardwerte (Walter 13.05.2026: pro Filiale konfigurierbar)
        if (dto.LohnausweisBoxFFreierTransport.HasValue)
            profile.LohnausweisBoxFFreierTransport = dto.LohnausweisBoxFFreierTransport.Value;
        if (dto.LohnausweisBoxGKantineGratis.HasValue)
            profile.LohnausweisBoxGKantineGratis = dto.LohnausweisBoxGKantineGratis.Value;
        // Pos. 2.1: null = keine Verpflegungs-Pauschale (Crew zahlt 50%)
        profile.LohnausweisPos21VerpflegungMonat = dto.LohnausweisPos21VerpflegungMonat;

        // Probezeit-Vorgabe (Walter 29.06.2026): 14 = 14 Tage, 1/2/3 = Monate.
        if (dto.ProbationMonths != null && dto.ProbationMonths != 14
            && dto.ProbationMonths != 1 && dto.ProbationMonths != 2 && dto.ProbationMonths != 3)
            return BadRequest(new { message = "Probezeit muss 14 (Tage), 1, 2 oder 3 (Monate) sein." });
        profile.ProbationMonths = dto.ProbationMonths;

        await _context.SaveChangesAsync();
        return Ok(profile);
    }

    /// <summary>
    /// Nur die Hauptsitz-Zuordnung setzen (Walter 29.08.2026) — eigener
    /// Mini-Endpoint, weil PATCH /stammdaten ein Voll-Ersatz ist und bei
    /// Einzelfeld-Aufrufen alle übrigen Felder nullen würde. Wird vom
    /// Inline-Dropdown im Stammdaten-Tab genutzt (Sofort-Speichern).
    /// </summary>
    [HttpPatch("{id}/hauptsitz")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> SetHauptsitz(int id, [FromBody] HauptsitzZuordnungDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();
        if (dto.HauptsitzId.HasValue
            && !await _context.Hauptsitze.AnyAsync(h => h.Id == dto.HauptsitzId.Value))
            return BadRequest(new { message = "Hauptsitz nicht gefunden." });
        profile.HauptsitzId = dto.HauptsitzId;
        await _context.SaveChangesAsync();
        return Ok(new { profile.Id, profile.HauptsitzId });
    }

    public record HauptsitzZuordnungDto(int? HauptsitzId);

    public record CompanyStammdatenDto(
        string?  CompanyName,
        string?  BranchName,
        string?  RestaurantCode,
        string?  Street,
        string?  HouseNumber,
        string?  ZipCode,
        string?  City,
        string?  Country,
        string?  WorkLocation,
        string?  KantonCode,
        string?  Phone,
        string?  Email,
        string?  BurNummer,
        string?  UidNummer,
        int?     HauptsitzId,
        string?  BranchenCode,
        string?  AhvKasse,
        string?  BvgVersicherer,
        bool?    IstGav,
        string?  GavName,
        // Lohnausweis-Standardwerte
        bool?    LohnausweisBoxFFreierTransport,
        bool?    LohnausweisBoxGKantineGratis,
        decimal? LohnausweisPos21VerpflegungMonat,
        int?     ProbationMonths
    );

    // PATCH /api/companyprofiles/{id}/bank
    // Filial-Bankverbindung (Auftraggeber-Konto fürs DTA / Lohnlauf).
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/bank")]
    public async Task<IActionResult> UpdateBank(int id, [FromBody] CompanyBankDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.Iban     = string.IsNullOrWhiteSpace(dto.Iban)
                              ? null
                              : dto.Iban.Replace(" ", "").ToUpperInvariant();
        profile.Bic      = string.IsNullOrWhiteSpace(dto.Bic)
                              ? null
                              : dto.Bic.Replace(" ", "").ToUpperInvariant();
        profile.BankName = string.IsNullOrWhiteSpace(dto.BankName)
                              ? null
                              : dto.BankName.Trim();
        await _context.SaveChangesAsync();
        return Ok(profile);
    }

    public record CompanyBankDto(
        string? Iban,
        string? Bic,
        string? BankName
    );

    // SSL-Nummern werden über /api/companyprofiles/{id}/ssl
    // (CompanyProfileSslController) verwaltet — eine SSL pro (Filiale, Kanton).

    // PATCH /api/companyprofiles/{id}/kanton
    // Standort-Kanton der Filiale (für Familienzulagen-Berechnung).
    // Wird im Filial-Edit-Modal als Dropdown gepflegt.
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/kanton")]
    public async Task<IActionResult> UpdateKanton(int id, [FromBody] CompanyKantonDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        if (string.IsNullOrWhiteSpace(dto.KantonCode))
        {
            profile.KantonCode = null;
        }
        else
        {
            var k = dto.KantonCode.Trim().ToUpperInvariant();
            if (k.Length != 2)
                return BadRequest(new { message = "Kanton-Code muss exakt 2 Zeichen haben (z.B. LU, AG, BE)." });
            profile.KantonCode = k;
        }

        await _context.SaveChangesAsync();
        return Ok(new { id = profile.Id, kantonCode = profile.KantonCode });
    }

    public record CompanyKantonDto(string? KantonCode);

    // PATCH /api/companyprofiles/{id}/thirteenth-payouts
    // Akzeptiert entweder eine Monatsliste (Months: int[]) oder die Legacy-
    // PayoutsPerYear-Kodierung. Mindestens eines muss gesetzt sein.
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/thirteenth-payouts")]
    public async Task<IActionResult> UpdateThirteenthPayouts(int id, [FromBody] ThirteenthPayoutsDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        // Neue Monats-Liste hat Vorrang
        if (dto.Months is { Length: > 0 })
        {
            // Validierung: Werte müssen 1-12 sein, eindeutig, sortiert
            var monthSet = dto.Months
                .Where(m => m >= 1 && m <= 12)
                .Distinct()
                .OrderBy(m => m)
                .ToArray();
            if (monthSet.Length == 0)
                return BadRequest(new { message = "Mindestens ein Auszahlungsmonat (1-12) muss gewählt sein." });

            profile.ThirteenthMonthPayoutMonths = string.Join(",", monthSet);
            // Legacy-Feld synchron halten falls einer der Standard-Rhythmen
            profile.ThirteenthMonthPayoutsPerYear = monthSet.Length == 12 ? 12
                                                  : monthSet.Length == 4  ? 4
                                                  : monthSet.Length == 2  ? 2
                                                  : monthSet.Length == 1  ? 1
                                                  : monthSet.Length;   // sonst nur die Anzahl als Hinweis
        }
        else
        {
            // Legacy-Pfad: Anzahl pro Jahr
            if (dto.PayoutsPerYear != 12 && dto.PayoutsPerYear != 4
                && dto.PayoutsPerYear != 2 && dto.PayoutsPerYear != 1)
                return BadRequest(new { message = "Erlaubte Werte: 12, 4, 2 oder 1." });

            profile.ThirteenthMonthPayoutsPerYear = dto.PayoutsPerYear;
            profile.ThirteenthMonthPayoutMonths   = dto.PayoutsPerYear switch
            {
                1  => "12",
                2  => "6,12",
                4  => "3,6,9,12",
                _  => "1,2,3,4,5,6,7,8,9,10,11,12"
            };
        }

        await _context.SaveChangesAsync();
        return Ok(profile);
    }

    public record ThirteenthPayoutsDto(int PayoutsPerYear, int[]? Months = null);

    // PATCH /api/companyprofiles/{id}/auto-ferien-geld-dezember
    // Schaltet die automatische Jahresend-Auszahlung des Ferien-Geld-Saldos
    // (UTP/MTP) im Dezember an oder aus.
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/auto-ferien-geld-dezember")]
    public async Task<IActionResult> UpdateAutoFerienGeldDezember(int id, [FromBody] AutoFerienGeldDezemberDto dto)
    {
        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();
        profile.AutoFerienGeldAuszahlungDezember = dto.Aktiv;
        await _context.SaveChangesAsync();
        return Ok(profile);
    }

    public record AutoFerienGeldDezemberDto(bool Aktiv);

    // PATCH /api/companyprofiles/{id}/lgav
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/lgav")]
    public async Task<IActionResult> UpdateLgav(int id, [FromBody] LgavDto dto)
    {
        if (dto.LgavTriggerMonat < 1 || dto.LgavTriggerMonat > 12)
            return BadRequest(new { message = "LgavTriggerMonat muss zwischen 1 und 12 liegen." });
        if (dto.LgavBeitragVoll      < 0 || dto.LgavBeitragVoll      > 9999m)
            return BadRequest(new { message = "LgavBeitragVoll muss zwischen 0 und 9999 liegen." });
        if (dto.LgavBeitragReduziert < 0 || dto.LgavBeitragReduziert > 9999m)
            return BadRequest(new { message = "LgavBeitragReduziert muss zwischen 0 und 9999 liegen." });

        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.LgavAktiv            = dto.LgavAktiv;
        profile.LgavTriggerMonat     = dto.LgavTriggerMonat;
        profile.LgavBeitragVoll      = Math.Round(dto.LgavBeitragVoll,      2);
        profile.LgavBeitragReduziert = Math.Round(dto.LgavBeitragReduziert, 2);
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record LgavDto(
        bool    LgavAktiv,
        int     LgavTriggerMonat,
        decimal LgavBeitragVoll,
        decimal LgavBeitragReduziert);

    // PATCH /api/companyprofiles/{id}/karenz
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/karenz")]
    public async Task<IActionResult> UpdateKarenz(int id, [FromBody] KarenzDto dto)
    {
        var basis = (dto.KarenzjahrBasis ?? "ARBEITSJAHR").Trim().ToUpperInvariant();
        if (basis != "ARBEITSJAHR" && basis != "KALENDERJAHR")
            return BadRequest(new { message = "Erlaubte Werte für KarenzjahrBasis: ARBEITSJAHR oder KALENDERJAHR." });

        if (dto.KarenzTageMax < 0 || dto.KarenzTageMax > 365)
            return BadRequest(new { message = "KarenzTageMax (Krank) muss zwischen 0 und 365 liegen." });

        // Unfall-Tage: wenn nicht mitgeliefert, bestehenden Wert (bzw. Default 2) beibehalten.
        if (dto.KarenzTageMaxUnfall.HasValue &&
            (dto.KarenzTageMaxUnfall.Value < 0 || dto.KarenzTageMaxUnfall.Value > 365))
            return BadRequest(new { message = "KarenzTageMaxUnfall muss zwischen 0 und 365 liegen." });

        if (dto.BvgWartefristMonate.HasValue &&
            (dto.BvgWartefristMonate.Value < 0 || dto.BvgWartefristMonate.Value > 24))
            return BadRequest(new { message = "BvgWartefristMonate muss zwischen 0 und 24 liegen." });

        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        profile.KarenzjahrBasis            = basis;
        profile.KarenzTageMax              = Math.Round(dto.KarenzTageMax, 2);
        if (dto.KarenzTageMaxUnfall.HasValue)
            profile.KarenzTageMaxUnfall    = Math.Round(dto.KarenzTageMaxUnfall.Value, 2);
        if (dto.BvgWartefristMonate.HasValue)
            profile.BvgWartefristMonate    = dto.BvgWartefristMonate.Value;
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record KarenzDto(
        string?  KarenzjahrBasis,
        decimal  KarenzTageMax,
        decimal? KarenzTageMaxUnfall,
        int?     BvgWartefristMonate);

    // PATCH /api/companyprofiles/{id}/akonto-prozent
    // Akonto-Prozentsätze (Walter Regel 3/4 + 5/6):
    //   • AkontoProzentFix    — für FIX,        Default 80 %
    //   • AkontoProzentFixM   — für FIX-M,      Default 90 % (Walter 18.05.2026)
    //   • AkontoProzentHourly — für UTP/MTP,    Default 100 %
    // Alle drei optional im DTO; nur gesetzte Werte werden übernommen.
    [Authorize(Roles = "admin")]
    [HttpPatch("{id:int}/akonto-prozent")]
    public async Task<IActionResult> UpdateAkontoProzent(int id, [FromBody] AkontoProzentDto dto)
    {
        if (dto.AkontoProzentFix.HasValue
            && (dto.AkontoProzentFix.Value < 0 || dto.AkontoProzentFix.Value > 100))
            return BadRequest(new { message = "AkontoProzentFix muss zwischen 0 und 100 liegen." });
        if (dto.AkontoProzentFixM.HasValue
            && (dto.AkontoProzentFixM.Value < 0 || dto.AkontoProzentFixM.Value > 100))
            return BadRequest(new { message = "AkontoProzentFixM muss zwischen 0 und 100 liegen." });
        if (dto.AkontoProzentHourly.HasValue
            && (dto.AkontoProzentHourly.Value < 0 || dto.AkontoProzentHourly.Value > 100))
            return BadRequest(new { message = "AkontoProzentHourly muss zwischen 0 und 100 liegen." });

        var profile = await _context.CompanyProfiles.FindAsync(id);
        if (profile is null) return NotFound();

        if (dto.AkontoProzentFix.HasValue)
            profile.AkontoProzentFix    = Math.Round(dto.AkontoProzentFix.Value,    2);
        if (dto.AkontoProzentFixM.HasValue)
            profile.AkontoProzentFixM   = Math.Round(dto.AkontoProzentFixM.Value,   2);
        if (dto.AkontoProzentHourly.HasValue)
            profile.AkontoProzentHourly = Math.Round(dto.AkontoProzentHourly.Value, 2);
        await _context.SaveChangesAsync();

        return Ok(profile);
    }

    public record AkontoProzentDto(
        decimal? AkontoProzentFix,
        decimal? AkontoProzentFixM,
        decimal? AkontoProzentHourly);

    // POST /api/companyprofiles/{id}/copy-einstellungen-to-all
    // Kopiert den kompletten Einstellungen-Block dieser Filiale auf ALLE
    // anderen Filialen (Walter-Vorgabe 15.05.2026) — Nachtzeiten, Ferien-/
    // Feiertags-Vorgaben, Karenz, L-GAV, Akonto-%, 13.-ML-Monate UND die
    // Akonto-Termine des übergebenen Jahres. Es wird der GESPEICHERTE Stand
    // der Quell-Filiale übertragen; die Ziel-Filialen werden überschrieben.
    [Authorize(Roles = "admin")]
    [HttpPost("{id:int}/copy-einstellungen-to-all")]
    public async Task<IActionResult> CopyEinstellungenToAll(int id, [FromBody] CopyEinstellungenDto dto)
    {
        var source = await _context.CompanyProfiles.FindAsync(id);
        if (source is null) return NotFound();

        var targets = await _context.CompanyProfiles
            .Where(c => c.Id != id)
            .ToListAsync();

        foreach (var t in targets)
        {
            // ── Arbeitszeit + Ferien-/Feiertags-Vorgaben ──
            t.NightStartTime               = source.NightStartTime;
            t.NightEndTime                 = source.NightEndTime;
            t.NormalWeeklyHours            = source.NormalWeeklyHours;
            t.MaxWeeklyHours               = source.MaxWeeklyHours;
            t.DefaultVacationPercent5Weeks = source.DefaultVacationPercent5Weeks;
            t.DefaultVacationPercent6Weeks = source.DefaultVacationPercent6Weeks;
            t.DefaultHolidayPercent        = source.DefaultHolidayPercent;
            t.VacationSixWeeksFromAge      = source.VacationSixWeeksFromAge;
            t.DefaultThirteenthSalaryPercent = source.DefaultThirteenthSalaryPercent;
            t.ProbationMonths              = source.ProbationMonths;
            // ── 13. ML + Ferien-Geld Dezember ──
            t.ThirteenthMonthPayoutMonths     = source.ThirteenthMonthPayoutMonths;
            t.ThirteenthMonthPayoutsPerYear   = source.ThirteenthMonthPayoutsPerYear;
            t.AutoFerienGeldAuszahlungDezember = source.AutoFerienGeldAuszahlungDezember;
            // ── Karenz ──
            t.KarenzjahrBasis      = source.KarenzjahrBasis;
            t.KarenzTageMax        = source.KarenzTageMax;
            t.KarenzTageMaxUnfall  = source.KarenzTageMaxUnfall;
            t.BvgWartefristMonate  = source.BvgWartefristMonate;
            // ── L-GAV ──
            t.LgavAktiv            = source.LgavAktiv;
            t.LgavTriggerMonat     = source.LgavTriggerMonat;
            t.LgavBeitragVoll      = source.LgavBeitragVoll;
            t.LgavBeitragReduziert = source.LgavBeitragReduziert;
            // ── Akonto-Lohn ──
            t.AkontoProzentFix     = source.AkontoProzentFix;
            t.AkontoProzentFixM    = source.AkontoProzentFixM;
            t.AkontoProzentHourly  = source.AkontoProzentHourly;
        }

        // ── Akonto-Termine des Jahres kopieren (Upsert pro Ziel/Monat) ──
        var sourceTermine = await _context.AkontoTermine
            .Where(at => at.CompanyProfileId == id && at.Year == dto.Year)
            .ToListAsync();

        if (sourceTermine.Count > 0)
        {
            var targetIds = targets.Select(t => t.Id).ToList();
            var existingTargetTermine = await _context.AkontoTermine
                .Where(at => targetIds.Contains(at.CompanyProfileId) && at.Year == dto.Year)
                .ToListAsync();

            foreach (var t in targets)
            {
                foreach (var st in sourceTermine)
                {
                    var row = existingTargetTermine.FirstOrDefault(
                        x => x.CompanyProfileId == t.Id && x.Month == st.Month);
                    if (row == null)
                    {
                        _context.AkontoTermine.Add(new AkontoTermin
                        {
                            CompanyProfileId = t.Id,
                            Year             = dto.Year,
                            Month            = st.Month,
                            PayoutDate       = st.PayoutDate,
                            CreatedAt        = DateTime.UtcNow,
                            UpdatedAt        = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        row.PayoutDate = st.PayoutDate;
                        row.UpdatedAt  = DateTime.UtcNow;
                    }
                }
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { branchesUpdated = targets.Count, termineCopied = sourceTermine.Count });
    }

    public record CopyEinstellungenDto(int Year);
}