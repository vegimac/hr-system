using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ZwischenverdienistController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ZwischenverdienistPdfService _pdfService;

    public ZwischenverdienistController(AppDbContext db, ZwischenverdienistPdfService pdfService)
    {
        _db = db;
        _pdfService = pdfService;
    }

    // ── Arbeitslosigkeit CRUD ─────────────────────────────────────────────

    [HttpGet("arbeitslosigkeit/{employeeId}")]
    public async Task<IActionResult> GetArbeitslosigkeit(int employeeId)
    {
        var list = await _db.EmployeeArbeitslosigkeiten
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.AngemeldetSeit)
            .ToListAsync();
        return Ok(list);
    }

    [HttpPost("arbeitslosigkeit")]
    public async Task<IActionResult> CreateArbeitslosigkeit([FromBody] EmployeeArbeitslosigkeit dto)
    {
        dto.Id = 0;
        dto.CreatedAt = DateTime.UtcNow;
        dto.UpdatedAt = DateTime.UtcNow;
        _db.EmployeeArbeitslosigkeiten.Add(dto);
        await _db.SaveChangesAsync();
        return Ok(dto);
    }

    [HttpPut("arbeitslosigkeit/{id}")]
    public async Task<IActionResult> UpdateArbeitslosigkeit(int id, [FromBody] EmployeeArbeitslosigkeit dto)
    {
        var existing = await _db.EmployeeArbeitslosigkeiten.FindAsync(id);
        if (existing is null) return NotFound();
        existing.AngemeldetSeit   = dto.AngemeldetSeit;
        existing.AbgemeldetAm     = dto.AbgemeldetAm;
        existing.RavStelle        = dto.RavStelle;
        existing.RavKundennummer  = dto.RavKundennummer;
        existing.Arbeitslosenkasse = dto.Arbeitslosenkasse;
        existing.Bemerkung        = dto.Bemerkung;
        existing.UpdatedAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(existing);
    }

    [HttpDelete("arbeitslosigkeit/{id}")]
    public async Task<IActionResult> DeleteArbeitslosigkeit(int id)
    {
        var existing = await _db.EmployeeArbeitslosigkeiten.FindAsync(id);
        if (existing is null) return NotFound();
        _db.EmployeeArbeitslosigkeiten.Remove(existing);
        await _db.SaveChangesAsync();
        return Ok();
    }

    // ── PDF-Generierung ───────────────────────────────────────────────────

    /// <summary>
    /// Generiert die "Bescheinigung über Zwischenverdienst" (ALV 716.105) als PDF.
    /// GET /api/zwischenverdienist/pdf?employeeId=X&year=2026&month=3&companyProfileId=1
    /// </summary>
    [HttpGet("pdf")]
    public async Task<IActionResult> GetPdf(
        [FromQuery] int employeeId,
        [FromQuery] int year,
        [FromQuery] int month,
        [FromQuery] int companyProfileId)
    {
        // ── Stammdaten laden ──────────────────────────────────────────────
        var employee = await _db.Employees
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null) return NotFound("Mitarbeiter nicht gefunden");

        var employment = await _db.Employments
            .Where(e => e.EmployeeId == employeeId && e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefaultAsync();

        // Standard-Unterzeichner (Verantwortliche Ansprechperson)
        var signatory = await _db.CompanySignatories
            .Where(s => s.CompanyProfileId == companyProfileId && s.IsActive)
            .OrderByDescending(s => s.IsDefault)
            .FirstOrDefaultAsync();

        var company = await _db.CompanyProfiles.FindAsync(companyProfileId);
        if (company is null) return NotFound("Firmenprofil nicht gefunden");

        // ── Kalendermonat bestimmen ───────────────────────────────────────
        var firstDay = new DateOnly(year, month, 1);
        var lastDay  = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // ── Stempelzeiten (Stunden pro Kalendertag) ───────────────────────
        var timeEntries = await _db.EmployeeTimeEntries
            .Where(t => t.EmployeeId == employeeId
                     && t.EntryDate >= firstDay
                     && t.EntryDate <= lastDay)
            .ToListAsync();

        // ── Absenzen im Monat ─────────────────────────────────────────────
        var absences = await _db.Absences
            .Where(a => a.EmployeeId == employeeId
                     && a.DateFrom <= lastDay
                     && a.DateTo   >= firstDay)
            .ToListAsync();

        // ── Tagesraster aufbauen ──────────────────────────────────────────
        // Absenz-Code-Mapping:
        //   KRANK / UNFALL / MUTTER → A
        //   MILITAER / ZIVIL        → B
        //   FEIERT / NACHT_KOMP     → C (andere bezahlte Absenz)
        //   UTP / UNBEZAHLT         → D
        //   FERIEN / URLAUB         → E
        var tagesEintraege = new Dictionary<int, string>();

        // Absenzen als Codes eintragen
        foreach (var abs in absences)
        {
            string code = MapAbsenzCode(abs.AbsenceType);
            if (string.IsNullOrEmpty(code)) continue;

            // Tage aus WorkedDays JSON oder Datumsbereich
            var days = GetAbsenceDays(abs, firstDay, lastDay);
            foreach (int day in days)
                tagesEintraege[day] = code;
        }

        // Gearbeitete Stunden eintragen (überschreiben Absenzen)
        foreach (var te in timeEntries)
        {
            decimal h = te.TotalHours ?? te.DurationHours ?? 0;
            if (h > 0)
                tagesEintraege[te.EntryDate.Day] = h.ToString("G");
        }

        // ── Lohnberechnung ────────────────────────────────────────────────
        decimal totalStunden = timeEntries.Sum(t => t.TotalHours ?? t.DurationHours ?? 0);
        decimal? stundenlohn  = employment?.HourlyRate;
        decimal? monatslohn   = employment?.MonthlySalary;
        // Ferienprozent: immer aus CompanyProfile berechnen (Alter im Abrechnungsmonat)
        decimal? ferienPct = null;
        if (employee.DateOfBirth.HasValue)
        {
            var checkDate = new DateOnly(year, month, 1);
            int age = checkDate.Year - employee.DateOfBirth.Value.Year;
            if (checkDate < DateOnly.FromDateTime(employee.DateOfBirth.Value.AddYears(age))) age--;
            ferienPct = age >= 50
                ? (company.DefaultVacationPercent6Weeks ?? 13.04m)
                : (company.DefaultVacationPercent5Weeks ?? 10.64m);
        }
        else
        {
            // Kein Geburtsdatum → Standardwert 5 Wochen
            ferienPct = company.DefaultVacationPercent5Weeks ?? 10.64m;
}
        decimal? feiertagPct  = employment?.HolidayPercent;
        decimal? dreizehnPct  = employment?.ThirteenthSalaryPercent;

        decimal grundlohn = stundenlohn.HasValue
            ? Math.Round(totalStunden * stundenlohn.Value, 2)
            : monatslohn ?? 0;

        decimal? ferienCHF   = ferienPct.HasValue   ? Math.Round(grundlohn * ferienPct.Value   / 100m, 2) : null;
        decimal? feiertagCHF = feiertagPct.HasValue  ? Math.Round(grundlohn * feiertagPct.Value / 100m, 2) : null;
        decimal? dreizehnCHF = dreizehnPct.HasValue  ? Math.Round(grundlohn * dreizehnPct.Value / 100m, 2) : null;

        decimal bruttolohnTotal = grundlohn
            + (ferienCHF   ?? 0)
            + (feiertagCHF ?? 0)
            + (dreizehnCHF ?? 0);

        // ── DTO zusammenstellen ───────────────────────────────────────────
        string adresse = string.Join(", ", new[]
        {
            $"{employee.Street} {employee.HouseNumber}".Trim(),
            $"{employee.ZipCode} {employee.City}".Trim()
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        string arbGeberAdresse = string.Join(", ", new[]
        {
            company.CompanyName,
            $"{company.Street} {company.HouseNumber}".Trim(),
            $"{company.ZipCode} {company.City}".Trim()
        }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var data = new ZwischenverdienistData
        {
            NameVorname          = $"{employee.LastName} {employee.FirstName}",
            PersNr               = employee.EmployeeNumber,
            AhvNummer            = employee.SocialSecurityNumber,
            Adresse              = adresse,
            Geburtsdatum         = employee.DateOfBirth.HasValue
                                    ? employee.DateOfBirth.Value.ToString("dd.MM.yyyy")
                                    : "",
            Zivilstand           = FormatZivilstand(employee.Zivilstand),
            Monat                = month.ToString("D2"),
            Jahr                 = year.ToString(),
            AusgeuebteTaetigkeit = employment?.JobTitle,
            TagesEintraege       = tagesEintraege,

            // Abschnitt 2–7
            // 2: immer JA (schriftlicher Vertrag vorhanden)
            SchriftlicherArbeitsvertrag = true,
            // 3: JA nur bei Festanstellung (FIX) oder MTP, sonst NEIN
            WoechentlicheAzVereinbart   = employment?.ContractType is "FIX" or "MTP",
            VereinbarteStundenProWoche   = employment?.ContractType is "FIX" or "MTP"
                                            ? employment.WeeklyHours : null,
            NormalarbeitszeitProWoche    = company.NormalWeeklyHours,
            // 5: immer JA, L-GAV
            IstGav                       = true,
            GavName                      = !string.IsNullOrWhiteSpace(company.GavName)
                                            ? company.GavName : "L-GAV",
            // 6: immer NEIN
            MehrStundenAngeboten         = false,

            // Abschnitt 8–10
            StundenlohnCHF       = stundenlohn,
            MonatslohnCHF        = monatslohn,
            TotalStunden         = stundenlohn.HasValue ? totalStunden : null,
            BruttolohnTotal      = bruttolohnTotal,
            Grundlohn            = grundlohn,

            FeiertagsprozentString = feiertagPct.HasValue ? feiertagPct.Value.ToString("G") + "%" : null,
            FeiertagsCHF           = feiertagCHF,
            FerienprozentString    = ferienPct.HasValue   ? ferienPct.Value.ToString("G")   + "%" : null,
            FerienCHF              = ferienCHF,
            DreizehnterProzentString = dreizehnPct.HasValue ? dreizehnPct.Value.ToString("G") + "%" : null,
            DreizehnterCHF           = dreizehnCHF,

            // Abschnitt 11–18
            DreizehnterJahresendAuszahlung = dreizehnPct.HasValue ? false : null,
            // BVG: ja wenn Versicherer hinterlegt, sonst nein
            BvgErhoben             = !string.IsNullOrWhiteSpace(company.BvgVersicherer),
            BvgVersicherer         = company.BvgVersicherer,
            AhvKasse               = company.AhvKasse,
            KinderzulagenAusgerichtet = null,
            IstBeteiligt           = false,

            // Arbeitgeber
            OrtDatum               = $"{company.City}, {DateTime.Today:dd.MM.yyyy}",
            ArbeitgeberAdresse     = arbGeberAdresse,
            UidNummer              = company.UidNummer,
            TelNummer              = company.Phone,
            Email                  = company.Email,
            BurNummer              = company.BurNummer,
            BranchenCode           = company.BranchenCode,
            AnsprechpersonName     = signatory?.LastName,
            AnsprechpersonVorname  = signatory?.FirstName,
        };

        byte[] pdfBytes = _pdfService.Generate(data);

        string filename = $"Zwischenverdienst_{employee.LastName}_{year}-{month:D2}.pdf";
        return File(pdfBytes, "application/pdf", filename);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static string MapAbsenzCode(string absenceType) => absenceType.ToUpper() switch
    {
        "KRANK"      => "A",
        "UNFALL"     => "A",
        "MUTTER"     => "A",
        "MILITAER"   => "B",
        "ZIVIL"      => "B",
        "FEIERT"     => "C",
        "NACHT_KOMP" => "C",
        "FERIEN"     => "E",
        "URLAUB"     => "E",
        "UTP"        => "D",
        _            => ""
    };

    private static string FormatZivilstand(string? code) => code switch
    {
        "ledig"                   => "ledig",
        "verheiratet"             => "verheiratet",
        "geschieden"              => "geschieden",
        "verwitwet"               => "verwitwet",
        "eingetragene_partnerschaft"  => "eingetr. Partnerschaft",
        "aufgeloeste_partnerschaft"   => "aufgel. Partnerschaft",
        _ => code ?? ""
    };

    private static List<int> GetAbsenceDays(Absence abs, DateOnly firstDay, DateOnly lastDay)
    {
        var days = new List<int>();

        // Versuche WorkedDays JSON zu parsen
        if (!string.IsNullOrEmpty(abs.WorkedDays))
        {
            try
            {
                var dates = JsonSerializer.Deserialize<List<string>>(abs.WorkedDays);
                if (dates != null)
                {
                    foreach (var ds in dates)
                    {
                        if (DateOnly.TryParse(ds, out var d) && d >= firstDay && d <= lastDay)
                            days.Add(d.Day);
                    }
                    return days;
                }
            }
            catch { /* fallback auf Datumsbereich */ }
        }

        // Fallback: alle Kalendertage im Bereich
        var from = abs.DateFrom > firstDay ? abs.DateFrom : firstDay;
        var to   = abs.DateTo   < lastDay  ? abs.DateTo   : lastDay;
        for (var d = from; d <= to; d = d.AddDays(1))
            days.Add(d.Day);

        return days;
    }
}
