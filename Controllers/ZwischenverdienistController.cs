using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ZwischenverdienistController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ZwischenverdienistPdfService _pdfService;
    private readonly KtgTagessatzService _ktgService;

    public ZwischenverdienistController(AppDbContext db, ZwischenverdienistPdfService pdfService, KtgTagessatzService ktgService)
    {
        _db = db;
        _pdfService = pdfService;
        _ktgService = ktgService;
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

        // Unterzeichner/Ansprechperson auf dem Formular = HR-Verantwortliche.
        // Priorität (Walter-Vorgabe: NIE Geschäftsführer, immer HR):
        //   1) UBA.Role == "HR_VERANTWORTLICH" für diese Filiale
        //   2) UBA mit User.IsHrTeam == true (HR-Team-Mitglied mit Filial-Zugang)
        //   3) UBA.IsDefault (Fallback — wenn keine HR-Person gepflegt ist)
        var branchUsers = await _db.UserBranchAccesses
            .Include(uba => uba.User)
            .Where(uba => uba.CompanyProfileId == companyProfileId && uba.User.IsActive)
            .ToListAsync();
        var signatoryUser = branchUsers.FirstOrDefault(uba => uba.Role == "HR_VERANTWORTLICH")
                         ?? branchUsers.FirstOrDefault(uba => uba.User.IsHrTeam)
                         ?? branchUsers.FirstOrDefault(uba => uba.IsDefault);
        // signatory auf null setzen da wir nur noch user_branch_access verwenden
        CompanySignatory? signatory = null;

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

        // ── AbsenzTyp-Komplett-Lookup für Kürzel + bezahlt-Flag ───────────
        // Wird verwendet für (1) Tagesraster-Kürzel, (2) bezahlte Absenz-Stunden
        // im Bruttolohn (siehe weiter unten).
        var absenzTypenAll = await _db.AbsenzTypen
            .Where(t => t.Aktiv)
            .ToListAsync();
        var absenzTypByCode = absenzTypenAll.ToDictionary(
            t => t.Code.ToUpperInvariant(), StringComparer.OrdinalIgnoreCase);
        var absenzKuerzel = absenzTypenAll
            .Where(t => !string.IsNullOrEmpty(t.ZwischenverdienstKuerzel))
            .ToDictionary(
                t => t.Code.ToUpperInvariant(),
                t => t.ZwischenverdienstKuerzel!,
                StringComparer.OrdinalIgnoreCase);

        // ── Tagesraster aufbauen ──────────────────────────────────────────
        // Offizielle ALK-Kürzel (steuerbar pro Absenztyp in der Admin-UI):
        //   A=Ferien, B=Krankheit/Schwangerschaft, C=Unfall,
        //   D=Mutterschaftsurlaub etc., E=Militär/Zivildienst,
        //   F=Betriebsferien, G=Unbezahlte Absenzen
        var tagesEintraege = new Dictionary<int, string>();

        // Absenzen als Codes eintragen (DB-driven via absenz_typ-Tabelle)
        foreach (var abs in absences)
        {
            if (string.IsNullOrEmpty(abs.AbsenceType)) continue;
            if (!absenzKuerzel.TryGetValue(abs.AbsenceType.ToUpperInvariant(), out var code))
                continue; // kein Kürzel hinterlegt → nicht eintragen

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
        // Anzahl Stunden = gearbeitete Stempel-Stunden + bezahlte Absenz-Stunden
        // (Krank, Unfall, Mutterschaft, Ferien-Bezug etc.). Damit entspricht der
        // Grundlohn dem AHV-pflichtigen Lohnersatz, den der MA effektiv erhält.
        decimal stempelStunden = timeEntries.Sum(t => t.TotalHours ?? t.DurationHours ?? 0);

        // Wochenstunden: Vertrag (für MTP) sonst Filiale
        decimal wochenStunden = employment?.GuaranteedHoursPerWeek
                                ?? employment?.WeeklyHours
                                ?? company.NormalWeeklyHours
                                ?? 42m;

        // ── UTP-Logik: Ferien/Mutterschaft etc. werden NICHT als Stunden zugeschlagen.
        // Bei UTP ist die Ferien-Entschädigung schon im Stundenlohn (10.64% etc.)
        // enthalten. Nur Krank/Unfall werden separat als "Taggeldleistungen"
        // ausgewiesen (per KTG-Tagessatz, nicht im Bruttolohn).
        bool isUtp = string.Equals(employment?.EmploymentModel, "UTP", StringComparison.OrdinalIgnoreCase);

        decimal absenzStunden = 0;
        int krankUnfallTage   = 0;
        foreach (var abs in absences)
        {
            if (string.IsNullOrEmpty(abs.AbsenceType)) continue;
            if (!absenzTypByCode.TryGetValue(abs.AbsenceType.ToUpperInvariant(), out var typ))
                continue;

            var days = GetAbsenceDays(abs, firstDay, lastDay);
            int tageImMonat = days.Count();
            if (tageImMonat == 0) continue;

            var kuerzel = (typ.ZwischenverdienstKuerzel ?? "").ToUpperInvariant();

            // Krank/Unfall (B/C): immer Taggeldleistungen via KTG-Tagessatz
            if (kuerzel == "B" || kuerzel == "C")
            {
                krankUnfallTage += tageImMonat;
                continue;
            }

            // Bei UTP: alle anderen bezahlten Absenzen NICHT zum Lohn addieren —
            // Ferien-Entschädigung ist bereits über die 10.64% im Grundlohn enthalten.
            if (isUtp) continue;

            // FIX/MTP: bezahlte Absenz-Stunden zum Lohn (Lohnersatz)
            bool bezahlt = !string.IsNullOrEmpty(typ.LohnpositionAuszahlungCode)
                        || (typ.Zeitgutschrift && !string.IsNullOrEmpty(typ.GutschriftModus));
            if (!bezahlt) continue;

            decimal stundenProTag = typ.GutschriftModus switch
            {
                "1/7" => Math.Round(wochenStunden / 7m, 4),
                _     => Math.Round(wochenStunden / 5m, 4),
            };
            absenzStunden += tageImMonat * stundenProTag;
        }
        absenzStunden = Math.Round(absenzStunden, 2);

        // Krank/Unfall-Karenz via KTG-Tagessatz → Feld "Taggeldleistungen"
        decimal krankUnfallCHF = 0;
        if (krankUnfallTage > 0)
        {
            var ktg = await _ktgService.CalculateAsync(employeeId, companyProfileId);
            if (ktg?.Tagessatz100 > 0)
                krankUnfallCHF = Math.Round(krankUnfallTage * ktg.Tagessatz100, 2);
        }

        // BVG-Logik: ab welcher monatlichen Lohnschwelle wird BVG abgezogen?
        // Wir verwenden den Koordinationsabzug aus social_insurance_rate (BVG-Sätze).
        // Gilt nur für altersgerechte BVG-Pflicht (typisch 25–64 für Sparbeiträge).
        var checkDateBvg = new DateOnly(year, month, 1);
        decimal bvgKoordinationsabzug = 0m;
        if (employee.DateOfBirth.HasValue)
        {
            int alter = checkDateBvg.Year - employee.DateOfBirth.Value.Year;
            if (checkDateBvg < DateOnly.FromDateTime(employee.DateOfBirth.Value.AddYears(alter))) alter--;

            var bvgRate = await _db.SocialInsuranceRates
                .Where(r => r.IsActive
                         && r.Code == "BVG"
                         && r.ValidFrom <= checkDateBvg
                         && (r.ValidTo == null || r.ValidTo >= checkDateBvg)
                         && (r.MinAge == null || r.MinAge <= alter)
                         && (r.MaxAge == null || r.MaxAge >= alter))
                .OrderByDescending(r => r.ValidFrom)
                .FirstOrDefaultAsync();
            bvgKoordinationsabzug = bvgRate?.CoordinationDeduction ?? 0m;
        }

        decimal totalStunden = stempelStunden + absenzStunden;
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
        // Walter-Vorgabe 06.06.2026 (Stufe 1b): nur noch Filial-Default
        decimal? feiertagPct  = company.DefaultHolidayPercent;
        decimal? dreizehnPct  = company.DefaultThirteenthSalaryPercent;

        decimal grundlohn = stundenlohn.HasValue
            ? Math.Round(totalStunden * stundenlohn.Value, 2)
            : monatslohn ?? 0;

        decimal? ferienCHF   = ferienPct.HasValue   ? Math.Round(grundlohn * ferienPct.Value   / 100m, 2) : null;
        decimal? feiertagCHF = feiertagPct.HasValue  ? Math.Round(grundlohn * feiertagPct.Value / 100m, 2) : null;

        // 13. ML-Basis = Stundenlohn + Feiertag + Ferien (analog Lohnzettel-Logik
        // mit ZaehltAlsBasis13ml-Flag — alle drei Positionen tragen für 13. ML).
        decimal basis13ml = grundlohn + (feiertagCHF ?? 0) + (ferienCHF ?? 0);
        decimal? dreizehnCHF = dreizehnPct.HasValue
            ? Math.Round(basis13ml * dreizehnPct.Value / 100m, 2)
            : null;

        // Taggeldleistungen (Krank/Unfall-Karenz) werden im Total Bruttolohn
        // mitgezählt, da sie AHV-pflichtiger Lohnersatz sind.
        decimal bruttolohnTotal = grundlohn
            + (ferienCHF   ?? 0)
            + (feiertagCHF ?? 0)
            + (dreizehnCHF ?? 0)
            + krankUnfallCHF;

        // ── DTO zusammenstellen ───────────────────────────────────────────
        string adresse = string.Join(", ", new[]
        {
            employee.Street,
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
            Zivilstand           = FormatZivilstand(employee.MaritalStatus),
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
            // Pro-Stunde-Aufteilung (gleiche Logik wie Punkt 9, aber pro Stunde):
            //   Feiertag = Stundenlohn × Feiertag-%
            //   Ferien   = Stundenlohn × Ferien-%
            //   13. ML   = (Stundenlohn + Feiertag + Ferien) × 13ML-%
            //   Brutto   = Summe der vier
            StundenlohnFeiertagCHF = stundenlohn.HasValue && feiertagPct.HasValue
                ? Math.Round(stundenlohn.Value * feiertagPct.Value / 100m, 2) : null,
            StundenlohnFerienCHF   = stundenlohn.HasValue && ferienPct.HasValue
                ? Math.Round(stundenlohn.Value * ferienPct.Value / 100m, 2) : null,
            StundenlohnDreizehnCHF = stundenlohn.HasValue && dreizehnPct.HasValue
                ? Math.Round((stundenlohn.Value
                    + Math.Round(stundenlohn.Value * (feiertagPct ?? 0) / 100m, 2)
                    + Math.Round(stundenlohn.Value * (ferienPct   ?? 0) / 100m, 2))
                    * dreizehnPct.Value / 100m, 2) : null,
            StundenlohnBruttoCHF   = stundenlohn.HasValue
                ? Math.Round(stundenlohn.Value
                    + (feiertagPct.HasValue ? Math.Round(stundenlohn.Value * feiertagPct.Value / 100m, 2) : 0)
                    + (ferienPct.HasValue   ? Math.Round(stundenlohn.Value * ferienPct.Value   / 100m, 2) : 0)
                    + (dreizehnPct.HasValue
                        ? Math.Round((stundenlohn.Value
                            + Math.Round(stundenlohn.Value * (feiertagPct ?? 0) / 100m, 2)
                            + Math.Round(stundenlohn.Value * (ferienPct   ?? 0) / 100m, 2))
                            * dreizehnPct.Value / 100m, 2)
                        : 0), 2)
                : null,
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

            // Taggeldleistungen: Karenz-Tagessatz × Krank/Unfall-Tage (KTG-Service)
            TaggeldleistungenCHF      = krankUnfallCHF > 0 ? krankUnfallCHF : null,
            TaggeldleistungenWelche   = krankUnfallCHF > 0 ? $"Karenz Krank/Unfall ({krankUnfallTage} Tage)" : null,

            // Abschnitt 11–18
            DreizehnterJahresendAuszahlung = dreizehnPct.HasValue ? false : null,
            // BVG: ja wenn Versicherer hinterlegt, sonst nein
            // BVG nur wenn Bruttolohn ≥ Koordinationsabzug (sonst keine BVG-Basis).
            // Ergibt JA/NEIN auf der Frage "Wurden auf dem Lohn Beiträge an die
            // berufliche Vorsorge erhoben?" — entscheidend ist der EFFEKTIVE Lohn,
            // nicht ob die Firma generell einen Versicherer hat.
            BvgErhoben             = bvgKoordinationsabzug > 0
                                     && bruttolohnTotal > bvgKoordinationsabzug
                                     && !string.IsNullOrWhiteSpace(company.BvgVersicherer),
            BvgVersicherer         = (bvgKoordinationsabzug > 0 && bruttolohnTotal > bvgKoordinationsabzug)
                                     ? company.BvgVersicherer : null,
            AhvKasse               = company.AhvKasse,
            KinderzulagenAusgerichtet = null,
            IstBeteiligt           = false,

            // Arbeitgeber
            OrtDatum               = $"{company.City}, {DateTime.Today:dd.MM.yyyy}",
            ArbeitgeberAdresse     = arbGeberAdresse,
            UidNummer              = company.UidNummer,
            // Telefon: Filial-Nummer bevorzugt (zentrale Nummer für Behörden);
            // E-Mail: persönliche der HR-Person bevorzugt.
            TelNummer              = !string.IsNullOrWhiteSpace(company.Phone)
                                       ? company.Phone : signatoryUser?.User.Phone,
            Email                  = !string.IsNullOrWhiteSpace(signatoryUser?.User.Email)
                                       ? signatoryUser?.User.Email : company.Email,
            BurNummer              = company.BurNummer,
            BranchenCode           = company.BranchenCode,
            AnsprechpersonName     = signatoryUser?.User.LastName ?? signatory?.LastName,
            AnsprechpersonVorname  = signatoryUser?.User.FirstName ?? signatory?.FirstName,
        };

        // Eingeloggter User: Unterschrift + Klarname für die AG-Unterschrift
        // (gleiche Konvention wie QST-Anmeldung — wer's generiert, signiert).
        byte[]? signaturePng = null;
        string? signerName   = null;
        var loggedInIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(loggedInIdStr, out var loggedInId))
        {
            var u = await _db.AppUsers
                .Where(x => x.Id == loggedInId)
                .Select(x => new { x.SignaturePng, x.FirstName, x.LastName, x.Username })
                .FirstOrDefaultAsync();
            if (u != null)
            {
                signaturePng = u.SignaturePng;
                var fullName = $"{u.FirstName} {u.LastName}".Trim();
                signerName   = string.IsNullOrWhiteSpace(fullName) ? u.Username : fullName;
            }
        }

        byte[] pdfBytes = _pdfService.Generate(data, signaturePng, signerName);

        string filename = $"Zwischenverdienst_{employee.LastName}_{year}-{month:D2}.pdf";
        return File(pdfBytes, "application/pdf", filename);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // MapAbsenzCode entfernt — Kürzel-Mapping läuft jetzt DB-driven via
    // absenz_typ.zwischenverdienst_kuerzel (siehe oben in Lookup-Dictionary).

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
