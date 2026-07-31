using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        dto.CreatedAt = DateTime.Now;
        dto.UpdatedAt = DateTime.Now;
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
        existing.UpdatedAt        = DateTime.Now;
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

        // Wochenstunden: Vertrag (für MTP) sonst Filiale — vor Tagesraster,
        // damit bezahlte Absenzen ohne ALK-Kürzel (z.B. BEZ_ABSENZ) Stunden
        // statt Buchstaben eintragen können.
        decimal wochenStunden = employment?.GuaranteedHoursPerWeek
                                ?? employment?.WeeklyHours
                                ?? company.NormalWeeklyHours
                                ?? 42m;

        // ── Tagesraster aufbauen ──────────────────────────────────────────
        // Offizielle ALK-Kürzel (steuerbar pro Absenztyp in der Admin-UI):
        //   A=Ferien, B=Krankheit/Schwangerschaft, C=Unfall,
        //   D=Mutterschaftsurlaub etc., E=Militär/Zivildienst,
        //   F=Betriebsferien, G=Unbezahlte Absenzen
        // Walter 31.07.2026: bezahlte Absenz OHNE Kürzel (BEZ_ABSENZ) →
        // Stundenzahl im Raster (kein Buchstabe im offiziellen Formular).
        var tagesEintraege = new Dictionary<int, string>();

        foreach (var abs in absences)
        {
            if (string.IsNullOrEmpty(abs.AbsenceType)) continue;
            var typeKey = abs.AbsenceType.ToUpperInvariant();
            var days = GetAbsenceDays(abs, firstDay, lastDay);
            if (days.Count == 0) continue;

            // Walter 31.07.2026 (final): Ferienbezug (A / FERIEN / Betriebsferien)
            // NICHT im Tagesraster — die Ferienentschädigung-% auf den
            // effektiven Stunden (Stempel + Zeitgutschrift) deckt das ab.
            // Sonst Doppeldeklaration gegenüber dem RAV.
            if (typeKey is "FERIEN" or "BETRIEBSFERIEN")
                continue;

            if (absenzKuerzel.TryGetValue(typeKey, out var code))
            {
                if (code is "A" or "F") continue;
                foreach (int day in days)
                    tagesEintraege[day] = code;
                continue;
            }

            // Kein ALK-Kürzel: Typen mit Zeitgutschrift als Stunden —
            // z.B. Bezahlte Absenz (Arzt/Behörde/Trauer).
            if (!absenzTypByCode.TryGetValue(typeKey, out var typBez)) continue;
            bool alsStunden = typBez.Zeitgutschrift
                           && !string.IsNullOrEmpty(typBez.GutschriftModus);
            if (!alsStunden) continue;

            decimal stundenProTag = HoursPerAbsenceDay(abs, typBez, wochenStunden, days.Count);
            if (stundenProTag <= 0) continue;
            // Walter 31.07.2026: Tagesraster immer 2 Nachkommastellen (nicht 4.857).
            string hStr = FormatTagesStunden(stundenProTag);
            foreach (int day in days)
                tagesEintraege[day] = hStr;
        }

        // Gearbeitete Stunden eintragen (überschreiben Absenzen)
        foreach (var te in timeEntries)
        {
            decimal h = te.TotalHours ?? te.DurationHours ?? 0;
            if (h > 0)
                tagesEintraege[te.EntryDate.Day] = FormatTagesStunden(h);
        }

        // ── Lohnberechnung ────────────────────────────────────────────────
        // Walter 31.07.2026 (Anzahl Std. auf dem RAV):
        //   FLEX → nur Stempelzeiten
        //   MTP  → Aufschlüsselung auf dem Formular:
        //          garantierte Festlohn-Stunden (nach Ferien/Krank/Unfall/UU)
        //          + darüber hinaus geleistete Stunden (max(0, Ist − Garantie))
        //          Totalfeld = Summe beider (= max(Garantie, Ist))
        //          Ist = Stempel + Absenzen mit Zeitgutschrift (z.B. BEZ_ABSENZ)
        // Ferienbezug zählt weder als Stunden noch im Raster — die
        // Ferienentschädigung-% (und Feiertag-%) kommen auf den Grundlohn.
        //
        // WICHTIG (Walter 31.07.2026): Zwischenwerte EXAKT rechnen —
        // gar nicht runden, und wenn, dann erst ganz am Schluss auf die
        // Anzeigewerte (2 Dezimalen). Keine Pro-Tag- / Pro-Absenz-Zwischen-
        // rundung (sonst Drift vs. Lohn, z.B. 10×4.29 statt 10×30/7).
        // Tage wie Lohnlauf: CountAbsenceDaysInPeriod.
        decimal stempelStunden = timeEntries.Sum(t => t.TotalHours ?? 0);

        decimal absenzStundenExakt = 0;
        int krankUnfallTage   = 0;
        foreach (var abs in absences)
        {
            if (string.IsNullOrEmpty(abs.AbsenceType)) continue;
            var typeKey = abs.AbsenceType.ToUpperInvariant();
            if (!absenzTypByCode.TryGetValue(typeKey, out var typ))
                continue;

            // Krank/Unfall-Taggeld: Kalendertage im Formular-Monat (Raster-Logik)
            var daysForRaster = GetAbsenceDays(abs, firstDay, lastDay);
            int tageRaster = daysForRaster.Count;

            var kuerzel = (typ.ZwischenverdienstKuerzel ?? "").ToUpperInvariant();

            // Krank/Unfall (B/C): immer Taggeldleistungen via KTG-Tagessatz
            if (kuerzel == "B" || kuerzel == "C")
            {
                if (tageRaster > 0) krankUnfallTage += tageRaster;
                continue;
            }

            // Ferienbezug: nie in die Ist-Stunden
            if (typeKey is "FERIEN" or "BETRIEBSFERIEN" || kuerzel is "A" or "F")
                continue;

            // Andere ALK-Kürzel (D/E/G): Buchstabe im Raster, keine Stunden
            if (!string.IsNullOrEmpty(kuerzel)) continue;

            // Nur Absenzen mit Zeitgutschrift (z.B. BEZ_ABSENZ)
            bool mitZeitgutschrift = typ.Zeitgutschrift
                                  && !string.IsNullOrEmpty(typ.GutschriftModus);
            if (!mitZeitgutschrift) continue;

            int tageImMonat = PayrollCalculations.CountAbsenceDaysInPeriod(
                abs, firstDay, lastDay);
            if (tageImMonat == 0) continue;
            absenzStundenExakt += ComputeAbsenzStundenExakt(
                abs, typ, wochenStunden, tageImMonat);
        }

        string empModel = NormalizeEmploymentModel(employment);
        decimal totalStunden;
        decimal? stundenGarantiert = null;
        decimal? stundenDarueber   = null;
        if (empModel == "MTP")
        {
            decimal guaranteedH = employment?.GuaranteedHoursPerWeek
                               ?? employment?.WeeklyHours
                               ?? 0m;
            var (pFrom, pTo) = MtpPeriodBounds(employment, firstDay, lastDay);
            decimal garantiertExakt = CalcMtpGarantierteFestlohnStunden(
                guaranteedH, pFrom, pTo, absences);
            decimal istExakt = stempelStunden + absenzStundenExakt;
            decimal darueberExakt = Math.Max(0m, istExakt - garantiertExakt);
            decimal totalExakt = garantiertExakt + darueberExakt;

            // Erst hier runden — Anzeigewerte auf dem Formular
            stundenGarantiert = Math.Round(garantiertExakt, 2);
            totalStunden      = Math.Round(totalExakt, 2);
            // Mehrstunden so, dass Garantie + Mehrstunden = Total (kein 0.01-Drift)
            stundenDarueber   = Math.Round(totalStunden - stundenGarantiert.Value, 2);
        }
        else
        {
            // FLEX (und übrige Stundenlohn-Fälle): nur Stempelzeiten
            totalStunden = Math.Round(stempelStunden, 2);
        }

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

        // Ferien-% und Feiertag-% nur auf den effektiven Stunden (Grundlohn =
        // Stempel + Zeitgutschrift-Absenzen). Ferienbezug-Tage sind nicht
        // im Grundlohn — deshalb hier die %-Zeilen zeigen (Walter 31.07.2026).
        decimal? ferienCHF   = ferienPct.HasValue   ? Math.Round(grundlohn * ferienPct.Value   / 100m, 2) : null;
        decimal? feiertagCHF = feiertagPct.HasValue  ? Math.Round(grundlohn * feiertagPct.Value / 100m, 2) : null;

        // 13. ML-Basis = Grundlohn + Feiertag + Ferien
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
            // 3: JA nur bei Festanstellung (FIX/FIX-M) oder MTP, sonst NEIN
            WoechentlicheAzVereinbart   = empModel is "FIX" or "FIX-M" or "MTP",
            VereinbarteStundenProWoche   = empModel is "FIX" or "FIX-M" or "MTP"
                                            ? (empModel == "MTP"
                                                ? employment?.GuaranteedHoursPerWeek ?? employment?.WeeklyHours
                                                : employment?.WeeklyHours)
                                            : null,
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
            StundenGarantiert    = stundenlohn.HasValue ? stundenGarantiert : null,
            StundenDarueber      = stundenlohn.HasValue ? stundenDarueber : null,
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

    /// <summary>EmploymentModel bevorzugt; ContractType als Legacy-Fallback (UTP→FLEX).</summary>
    private static string NormalizeEmploymentModel(Employment? employment)
    {
        string model = (employment?.EmploymentModel ?? "").Trim().ToUpperInvariant();
        if (model is "FLEX" or "MTP" or "FIX" or "FIX-M") return model;
        if (model is "UTP") return "FLEX";

        string ct = (employment?.ContractType ?? "").Trim().ToUpperInvariant();
        if (ct is "FLEX" or "UTP" or "FLEXIBEL") return "FLEX";
        if (ct is "MTP" or "MTP/TPM" or "TPM") return "MTP";
        if (ct is "FIX-M" or "FIXM") return "FIX-M";
        if (ct is "FIX") return "FIX";
        return model;
    }

    /// <summary>MTP-Periode = Kalendermonat, gekürzt bei Ein-/Austritt mitten im Monat.</summary>
    private static (DateOnly From, DateOnly To) MtpPeriodBounds(
        Employment? employment, DateOnly firstDay, DateOnly lastDay)
    {
        var from = firstDay;
        var to   = lastDay;
        if (employment is null) return (from, to);

        var start = DateOnly.FromDateTime(employment.ContractStartDate);
        if (start > from) from = start;
        if (employment.ContractEndDate.HasValue)
        {
            var end = DateOnly.FromDateTime(employment.ContractEndDate.Value);
            if (end < to) to = end;
        }
        if (to < from) to = from;
        return (from, to);
    }

    /// <summary>
    /// MTP ausbezahlte Festlohn-Stunden (= Soll nach Kürzungen), analog
    /// PayrollCalculationEngine festlohnArbeitStunden:
    /// garantierte WoStd/7 × Periodentage − Ferien 1/7 − Krank/Unfall 1/5 Werktage − UU 1/7.
    /// </summary>
    private static decimal CalcMtpGarantierteFestlohnStunden(
        decimal guaranteedH,
        DateOnly periodFrom,
        DateOnly periodTo,
        List<Absence> absences)
    {
        if (guaranteedH <= 0) return 0m;
        int periodDays = periodTo.DayNumber - periodFrom.DayNumber + 1;
        if (periodDays <= 0) return 0m;

        decimal sollVoll = guaranteedH / 7m * periodDays;
        decimal ferienTage = 0m;
        decimal uuTage = 0m;
        decimal krankWerktage = 0m;
        decimal unfallWerktage = 0m;

        foreach (var a in absences)
        {
            string type = (a.AbsenceType ?? "").ToUpperInvariant();
            var dates = GetAbsenceDates(a, periodFrom, periodTo);
            if (dates.Count == 0) continue;
            decimal p = a.Prozent > 0 ? a.Prozent / 100m : 1m;

            if (type == "FERIEN")
            {
                ferienTage += dates.Count * p;
            }
            else if (type == "UNBEZ_URLAUB")
            {
                uuTage += dates.Count;
            }
            else if (type == "KRANK")
            {
                krankWerktage += dates.Count(d =>
                    d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) * p;
            }
            else if (type == "UNFALL")
            {
                unfallWerktage += dates.Count(d =>
                    d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday) * p;
            }
        }

        decimal exakt = sollVoll
            - ferienTage * guaranteedH / 7m
            - krankWerktage * guaranteedH / 5m
            - unfallWerktage * guaranteedH / 5m
            - uuTage * guaranteedH / 7m;
        if (exakt < 0m) exakt = 0m;
        // Toleranz-Clamp wie Lohnlauf — noch ungerundet zurückgeben
        if (Math.Abs(exakt) < 0.01m) exakt = 0m;
        return exakt;
    }

    private static List<DateOnly> GetAbsenceDates(Absence abs, DateOnly firstDay, DateOnly lastDay)
    {
        var days = new List<DateOnly>();
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
                            days.Add(d);
                    }
                    return days;
                }
            }
            catch { /* fallback */ }
        }

        var from = abs.DateFrom > firstDay ? abs.DateFrom : firstDay;
        var to   = abs.DateTo   < lastDay  ? abs.DateTo   : lastDay;
        for (var d = from; d <= to; d = d.AddDays(1))
            days.Add(d);
        return days;
    }

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

    /// <summary>
    /// Stunden pro Absenz-Tag NUR fürs Tagesraster (Anzeige).
    /// Total/Ist nutzt ComputeAbsenzStundenLikeLohn — nicht diese Methode.
    /// </summary>
    private static decimal HoursPerAbsenceDay(
        Absence abs, AbsenzTyp typ, decimal wochenStunden, int daysInMonth)
    {
        if (daysInMonth <= 0) return 0m;

        decimal pFactor = abs.Prozent > 0 ? abs.Prozent / 100m : 1m;

        if (abs.HoursCredited > 0)
        {
            // Gesamt-Tage der Absenz (nicht nur Monat) für faire Aufteilung
            // bei monatsübergreifenden Einträgen.
            var allDays = GetAbsenceDays(abs, abs.DateFrom, abs.DateTo);
            int totalDays = allDays.Count > 0 ? allDays.Count : daysInMonth;
            return Math.Round(abs.HoursCredited / totalDays, 2);
        }

        decimal basePerDay = typ.GutschriftModus switch
        {
            "1/7" => Math.Round(wochenStunden / 7m, 2),
            _     => Math.Round(wochenStunden / 5m, 2),
        };
        return Math.Round(basePerDay * pFactor, 2);
    }

    /// <summary>
    /// Absenz-Stunden EXAKT (Tage × WoStd / 5|7 × Prozent) — ohne Rundung.
    /// Rundung erst am Schluss auf die Formular-Anzeigewerte.
    /// </summary>
    private static decimal ComputeAbsenzStundenExakt(
        Absence abs, AbsenzTyp typ, decimal wochenStunden, int daysInPeriod)
    {
        if (daysInPeriod <= 0) return 0m;
        string modus = typ.GutschriftModus ?? "1/5";
        decimal divisor = modus == "1/7" ? 7m : 5m;
        decimal prozent = abs.Prozent > 0 ? abs.Prozent : 100m;
        return daysInPeriod * wochenStunden / divisor * prozent / 100m;
    }

    /// <summary>Tagesraster-Stunden immer mit 2 Nachkommastellen (Punkt).</summary>
    private static string FormatTagesStunden(decimal hours)
        => Math.Round(hours, 2).ToString("0.00", CultureInfo.InvariantCulture);
}
