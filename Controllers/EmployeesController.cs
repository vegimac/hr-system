using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EmployeePostfachService _postfach;

    public EmployeesController(AppDbContext context, EmployeePostfachService postfach)
    {
        _context = context;
        _postfach = postfach;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var employees = await _context.Employees
            .Include(e => e.Employments).ThenInclude(em => em.JobGroup)   // FK-Code für Frontend (Walter 26.05.2026)
            .OrderBy(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim())
            .ToListAsync();

        return Ok(employees);
    }

    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup()
    {
        var employees = await _context.Employees
            .Where(e => e.IsActive)
            .OrderBy(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim())
            .Select(e => new
            {
                Id = e.Id,
                DisplayName = ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim() + " (" + e.EmployeeNumber + ")"
            })
            .ToListAsync();

        return Ok(employees);
    }

    [HttpGet("lookup/company/{companyId:int}")]
    public async Task<IActionResult> GetEmployeesForCompany(int companyId)
    {
        var company = await _context.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
            return NotFound("Company not found.");

        var restaurantPrefix = NormalizeRestaurantPrefix(company.RestaurantCode);

        var employees = await _context.Employees
            .Where(e => e.IsActive)
            .ToListAsync();

        var filtered = employees
            .Where(e => NormalizeEmployeeNumber(e.EmployeeNumber).StartsWith(restaurantPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim())
            .Select(e => new
            {
                Id = e.Id,
                DisplayName = ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim() + " (" + e.EmployeeNumber + ")",
                Gender = e.Gender,
                DateOfBirth = e.DateOfBirth  // NEU
            })
            .ToList();

        return Ok(filtered);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var employee = await _context.Employees
            .Include(e => e.Employments.OrderByDescending(c => c.ContractStartDate)).ThenInclude(em => em.JobGroup)
            .Include(e => e.PermitType)
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
            return NotFound();

        // Klartext-Name der Nationalität (Walter-Vorgabe 14.05.2026: immer
        // Volltext anzeigen, nie nur den ISO-Code). Fallback-Kette:
        //   1) AppText (Module=NATIONALITY, TextKey={CODE}.NAME, Sprache de)
        //   2) statische ISO-Tabelle CountryNamesDe (deckt alle ISO-3166-Codes)
        //   3) als allerletzter Ausweg der Code selbst
        // Sprache: aktuell hardcoded auf 'de' — könnte später aus User-Sprache kommen.
        string? natName = null;
        var natCode = employee.NationalityRef?.Code ?? employee.Nationality;
        if (!string.IsNullOrWhiteSpace(natCode))
        {
            var key = $"{natCode}.NAME";
            var txt = await _context.AppTexts
                .Where(t => t.Module == "NATIONALITY" && t.LanguageCode == "de" && t.TextKey == key)
                .Select(t => t.Content)
                .FirstOrDefaultAsync();
            natName = !string.IsNullOrWhiteSpace(txt)
                ? txt
                : (CountryNamesDe.Resolve(natCode) ?? natCode);
        }

        // permitExpiryDate (abgeleitet) + permitType:
        // Walter-Vorgabe 07.06.2026 (final): „neueste" = höchstes ValidTo,
        // bei Gleichheit ÄLTESTES ValidFrom (= Original-Eintrag, nicht
        // Import-Duplikat). Konsistent mit EmployeePermitHistoryController.
        DateOnly? permitExpiryDate = null;
        PermitType? latestPermitType = null;
        {
            var maxDate = new DateOnly(9999, 12, 31);
            var newest = await _context.EmployeePermitHistories
                .Where(h => h.EmployeeId == employee.Id && h.PermitTypeId != null)
                .Include(h => h.PermitType)
                .OrderByDescending(h => h.ValidTo ?? maxDate)
                .ThenBy(h => h.ValidFrom)
                .ThenBy(h => h.Id)
                .FirstOrDefaultAsync();
            if (newest != null)
            {
                permitExpiryDate = newest.ValidTo;
                latestPermitType = newest.PermitType;
            }
        }

        // Aktiver Vertrag = ContractEndDate IS NULL (kein Enddatum = laufend)
        // Fallback: neuester Vertrag
        var active = employee.Employments.FirstOrDefault(c => c.ContractEndDate == null)
                  ?? employee.Employments.FirstOrDefault();

        // Flache Felder des aktiven Vertrags direkt in die Antwort einbauen
        // damit das bestehende UI (emp.employmentModel, emp.employmentPercentage usw.)
        // ohne Änderung weiter funktioniert
        return Ok(new
        {
            // ── Employee-Felder ──────────────────────────────────────────
            employee.Id,
            employee.EmployeeNumber,
            employee.Salutation,
            employee.FirstName,
            employee.LastName,
            employee.Gender,
            employee.DateOfBirth,
            employee.LanguageCode,
            employee.Nationality,
            employee.NationalityId,
            nationalityCode = natCode,
            nationalityName = natName,  // Klartext aus AppText (z.B. "Bosnien und Herzegowina")
            employee.PhoneMobile,
            employee.Email,
            employee.EntryDate,
            employee.ExitDate,
            employee.IsActive,
            employee.IsPayrollExcluded,
            employee.LgavPflichtig,
            employee.TeilzeitUnter8hWoche,
            // Walter-Vorgabe 07.06.2026: permitType + Code/Beschreibung kommen
            // aus der „neuesten" History-Bewilligung (siehe oben latestPermitType),
            // nicht aus dem denormalisierten employee.PermitType — damit Frontend
            // und QST-Pflicht-Check immer denselben Eintrag sehen, auch wenn der
            // Cache nach einem Schema-Wechsel noch nicht resynct wurde.
            PermitTypeId          = latestPermitType?.Id,
            permitType            = latestPermitType == null ? null : new {
                latestPermitType.Id,
                latestPermitType.Code,
                latestPermitType.Description
            },
            permitTypeCode        = latestPermitType?.Code,
            permitTypeDescription = latestPermitType?.Description,
            permitExpiryDate      = permitExpiryDate,
            zemisNumber = employee.ZemisNumber,
            employee.QuellensteuerBefreitAb,
            // QST-Befreiung durch Steuerbehörde (Walter 26.05.2026)
            employee.QstBefreitDurchBehoerde,
            employee.QstBefreiungDokumentId,
            employee.QstBefreiungGueltigAb,
            employee.QstBefreiungGueltigBis,
            employee.SocialSecurityNumber,
            employee.MaritalStatus,
            employee.MaritalStatusSince,
            employee.SeparatedSince,
            employee.Religion,
            employee.LetterSalutation,
            employee.PlaceOfOrigin,

            // ── Adresse (werden u.a. im QST-Modal angezeigt) ─────────────
            employee.Street,
            employee.HouseNumber,
            employee.ZipCode,
            employee.City,
            employee.Country,
            employee.CantonCode,

            // ── Felder aus aktivem Vertrag (flach) ───────────────────────
            employmentModel        = active?.EmploymentModel,
            salaryType             = active?.SalaryType,
            contractStartDate      = active?.ContractStartDate,
            contractEndDate        = active?.ContractEndDate,
            contractType           = active?.ContractType,
            jobTitle               = active?.JobTitle,
            employmentPercentage   = active?.EmploymentPercentage,
            weeklyHours            = active?.WeeklyHours,
            guaranteedHoursPerWeek = active?.GuaranteedHoursPerWeek,
            monthlySalary          = active?.MonthlySalary,
            hourlyRate             = active?.HourlyRate,
            vacationPaymentMode    = active?.VacationPaymentMode,
            probationPeriodMonths  = active?.ProbationPeriodMonths,
            probationEndDate       = active?.ProbationEndDate,
            activeContractId       = active?.Id,

            // ── Alle Verträge (History) ──────────────────────────────────
            employments = employee.Employments
        });
    }

    /// <summary>
    /// Leitet Wohnkanton + Land aus der PLZ ab (Walter-Vorgabe 13.05.2026).
    /// Der easy@work-CSV-Import liefert weder Kanton noch Land — beide werden
    /// aus dem amtlichen Ortschaftsverzeichnis (SwissLocations) ergänzt.
    ///   • Country: auf „Schweiz" wenn leer (PLZ stammt aus CH-Verzeichnis).
    ///   • CantonCode: aus eindeutigem PLZ-Lookup — gesetzt wenn leer ODER
    ///     wenn forceCantonRefresh=true (= PLZ hat sich geändert).
    /// Mehrdeutige PLZ (über Kantonsgrenze) werden übersprungen statt geraten.
    /// </summary>
    private async Task EnrichAddressFromZipAsync(Employee emp, bool forceCantonRefresh = false)
    {
        var zip = emp.ZipCode?.Trim();
        if (string.IsNullOrWhiteSpace(zip)) return;

        // Land-Standard systemweit: ISO-Code „CH" (Walter-Vorgabe 13.05.2026).
        if (string.IsNullOrWhiteSpace(emp.Country))
            emp.Country = "CH";

        if (forceCantonRefresh || string.IsNullOrWhiteSpace(emp.CantonCode))
        {
            var kantone = await _context.SwissLocations
                .Where(l => l.Plz4 == zip)
                .Select(l => l.Kantonskuerzel)
                .Distinct()
                .ToListAsync();
            if (kantone.Count == 1 && !string.IsNullOrWhiteSpace(kantone[0]))
                emp.CantonCode = kantone[0];
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(Employee employee)
    {
        // Kanton + Land aus PLZ ableiten, falls nicht mitgeliefert.
        await EnrichAddressFromZipAsync(employee);

        _context.Employees.Add(employee);
        await _context.SaveChangesAsync();

        // Auto-Postfach für jeden neu angelegten MA. Falls die Hauptfiliale
        // (Employment) zu diesem Zeitpunkt noch nicht erfasst ist, wird der
        // Account mit "MA"-Default-Präfix angelegt und kann später per
        // Passwort-Reset auf den korrekten Filial-Präfix gebracht werden.
        var primary = await _postfach.GetPrimaryCompanyAsync(employee.Id);
        await _postfach.EnsureAccountAsync(employee, primary);

        return CreatedAtAction(nameof(GetById), new { id = employee.Id }, employee);
    }

    // PUT /api/employees/{id} – Mitarbeiterstammdaten aktualisieren
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] EmployeeUpdateDto dto)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee is null) return NotFound();

        // ── Personalien ───────────────────────────────────────────────────
        if (dto.FirstName    is not null) employee.FirstName    = dto.FirstName;
        if (dto.LastName     is not null) employee.LastName     = dto.LastName;
        if (dto.Salutation   is not null) employee.Salutation   = dto.Salutation   == "" ? null : dto.Salutation;
        if (dto.Gender       is not null) employee.Gender       = dto.Gender       == "" ? null : dto.Gender;
        if (dto.DateOfBirth  is not null) employee.DateOfBirth  = dto.DateOfBirth;
        if (dto.LanguageCode is not null) employee.LanguageCode = dto.LanguageCode == "" ? null : dto.LanguageCode;
        if (dto.NationalityId.HasValue)   employee.NationalityId = dto.NationalityId == 0 ? null : dto.NationalityId;
        if (dto.PhoneMobile  is not null) employee.PhoneMobile  = dto.PhoneMobile  == "" ? null : dto.PhoneMobile;
        if (dto.Email        is not null) employee.Email        = dto.Email        == "" ? null : dto.Email;

        // ── Adresse ───────────────────────────────────────────────────────
        // PLZ vor dem Überschreiben merken — wenn sie sich ändert, muss der
        // Kanton neu aus dem Ortschaftsverzeichnis abgeleitet werden.
        var zipBefore = employee.ZipCode?.Trim();
        if (dto.Street      is not null) employee.Street      = dto.Street      == "" ? null : dto.Street;
        if (dto.HouseNumber is not null) employee.HouseNumber = dto.HouseNumber == "" ? null : dto.HouseNumber;
        if (dto.ZipCode     is not null) employee.ZipCode     = dto.ZipCode     == "" ? null : dto.ZipCode;
        if (dto.City        is not null) employee.City        = dto.City        == "" ? null : dto.City;
        if (dto.Country     is not null) employee.Country     = dto.Country     == "" ? null : dto.Country;
        if (dto.CantonCode  is not null) employee.CantonCode  = dto.CantonCode  == "" ? null : dto.CantonCode.ToUpperInvariant();

        // Kanton + Land aus PLZ ableiten (Walter-Vorgabe 13.05.2026):
        // Land → „Schweiz" wenn leer. Kanton → neu ableiten wenn die PLZ
        // sich geändert hat (Umzug = ggf. anderer Kanton), sonst nur wenn leer.
        // Ausnahme: wenn der Aufrufer explizit einen Kanton mitschickt (z.B.
        // manueller Editor, Grenzgänger-Spezialfall), gewinnt dieser — dann
        // KEIN Force-Refresh. Der easy@work-Import sendet keinen Kanton → Lookup.
        var zipAfter   = employee.ZipCode?.Trim();
        var zipChanged = !string.Equals(zipBefore, zipAfter, StringComparison.OrdinalIgnoreCase);
        var cantonExplicit = !string.IsNullOrWhiteSpace(dto.CantonCode);
        // Walter-Vorgabe 06.06.2026: Importer setzt ForceCantonFromZip=true →
        // PLZ-Lookup wird ALWAYS ausgeführt (korrigiert frühere CSV-Region-Fehler).
        // Nur unterdrückt, wenn der Aufrufer explizit selbst einen Kanton mitschickt.
        var forceRefresh = (zipChanged || dto.ForceCantonFromZip) && !cantonExplicit;
        await EnrichAddressFromZipAsync(employee, forceCantonRefresh: forceRefresh);

        // ── Aufenthalt ────────────────────────────────────────────────────
        if (dto.PermitTypeId.HasValue)     employee.PermitTypeId     = dto.PermitTypeId == 0 ? null : dto.PermitTypeId;
        // dto.PermitExpiryDate wird IGNORIERT (Walter 01.06.2026) — das Ablauf-
        // Datum lebt nur noch auf EmployeePermitHistory.ValidTo. Frontend kann
        // das Feld noch senden, wird aber nicht mehr verarbeitet.
        if (dto.ZemisNumber is not null)   employee.ZemisNumber      = dto.ZemisNumber == "" ? null : dto.ZemisNumber;
        if (dto.QuellensteuerBefreitAbSet) employee.QuellensteuerBefreitAb = dto.QuellensteuerBefreitAb;

        // ── Ein-/Austritt ─────────────────────────────────────────────────
        if (dto.EntryDate.HasValue) employee.EntryDate = dto.EntryDate;
        if (dto.ExitDateSet)        employee.ExitDate  = dto.ExitDate;

        // ── ALV / Zwischenverdienst ───────────────────────────────────────
        if (dto.AhvNummer  is not null) employee.SocialSecurityNumber = dto.AhvNummer == "" ? null : dto.AhvNummer;
        if (dto.MaritalStatus is not null) employee.MaritalStatus = dto.MaritalStatus == "" ? null : dto.MaritalStatus;

        // ── Erweiterte Zivilstand-Angaben (allgemein) ────────────────────
        if (dto.MaritalStatusSinceSet) employee.MaritalStatusSince = dto.MaritalStatusSince;
        if (dto.SeparatedSinceSet)     employee.SeparatedSince     = dto.SeparatedSince;
        if (dto.Religion is not null) employee.Religion = dto.Religion == "" ? null : dto.Religion;
        if (dto.LetterSalutation is not null) employee.LetterSalutation = dto.LetterSalutation == "" ? null : dto.LetterSalutation;
        if (dto.PlaceOfOrigin   is not null) employee.PlaceOfOrigin   = dto.PlaceOfOrigin   == "" ? null : dto.PlaceOfOrigin;

        // ── "Kein Lohn"-Flag setzen ──────────────────────────────────────
        // Rollen-Beschränkung wird im Frontend durchgesetzt (Toggle wird nur
        // für admin/superuser angezeigt). Backend-Role-Check entfernt weil
        // er bei Walter's JWT-Setup nicht zuverlässig griff.
        //
        // Walter-Vorgabe 01.06.2026: beim Umstellen auf "kein Lohn" werden
        // automatisch alle Snapshots/Saldos/Akonto-Zahlungen und Verträge
        // in NICHT-abgeschlossenen Perioden (Status != 'abgeschlossen')
        // gelöscht. Abgeschlossene Perioden bleiben unangetastet (sind
        // historische Belege). So bleibt der Lohnlauf-Workflow konsistent.
        if (dto.IsPayrollExcluded.HasValue)
        {
            bool wirdPhantom = !employee.IsPayrollExcluded && dto.IsPayrollExcluded.Value;
            employee.IsPayrollExcluded = dto.IsPayrollExcluded.Value;

            if (wirdPhantom)
            {
                // IDs der offenen (nicht abgeschlossenen) Perioden laden.
                var offenePeriodenIds = await _context.PayrollPerioden
                    .Where(p => p.Status != "abgeschlossen")
                    .Select(p => p.Id)
                    .ToListAsync();
                var offeneYearMonth = await _context.PayrollPerioden
                    .Where(p => p.Status != "abgeschlossen")
                    .Select(p => new { p.Year, p.Month })
                    .ToListAsync();

                // 1) Snapshots in offenen Perioden
                var snapsRaus = await _context.PayrollSnapshots
                    .Where(s => s.EmployeeId == employee.Id
                             && offenePeriodenIds.Contains(s.PayrollPeriodeId))
                    .ToListAsync();
                _context.PayrollSnapshots.RemoveRange(snapsRaus);

                // 2) Akonto-Zahlungen (period_year/period_month)
                var akontoRaus = await _context.AkontoZahlungen
                    .Where(a => a.EmployeeId == employee.Id)
                    .ToListAsync();
                var akontoFiltered = akontoRaus.Where(a =>
                    offeneYearMonth.Any(p => p.Year == a.PeriodYear && p.Month == a.PeriodMonth))
                    .ToList();
                _context.AkontoZahlungen.RemoveRange(akontoFiltered);

                // 3) Saldos (period_year/period_month)
                var saldoAll = await _context.PayrollSaldos
                    .Where(s => s.EmployeeId == employee.Id)
                    .ToListAsync();
                var saldoFiltered = saldoAll.Where(s =>
                    offeneYearMonth.Any(p => p.Year == s.PeriodYear && p.Month == s.PeriodMonth))
                    .ToList();
                _context.PayrollSaldos.RemoveRange(saldoFiltered);

                // 4) Verträge ohne Snapshot in irgendeiner ABGESCHLOSSENEN
                //    Periode löschen. Wenn ein Vertrag bereits in einer
                //    abgeschlossenen Periode war, muss er als historischer
                //    Beleg bestehen bleiben.
                var verträgeAll = await _context.Employments
                    .Where(em => em.EmployeeId == employee.Id)
                    .ToListAsync();
                var abgeschlossenePerioden = await _context.PayrollPerioden
                    .Where(p => p.Status == "abgeschlossen")
                    .Select(p => new { p.Id, p.PeriodFrom, p.PeriodTo })
                    .ToListAsync();
                var snapshotsAbgeschlossen = await _context.PayrollSnapshots
                    .Where(s => s.EmployeeId == employee.Id)
                    .Select(s => s.PayrollPeriodeId)
                    .ToListAsync();
                var abgeschlossPerioden = abgeschlossenePerioden
                    .Where(p => snapshotsAbgeschlossen.Contains(p.Id))
                    .ToList();
                foreach (var v in verträgeAll)
                {
                    var startD = DateOnly.FromDateTime(v.ContractStartDate);
                    var endD = v.ContractEndDate.HasValue
                        ? DateOnly.FromDateTime(v.ContractEndDate.Value)
                        : new DateOnly(9999, 12, 31);
                    bool inAbgeschlossener = abgeschlossPerioden.Any(p =>
                        startD <= p.PeriodTo && endD >= p.PeriodFrom);
                    if (!inAbgeschlossener)
                        _context.Employments.Remove(v);
                }
            }
        }

        // KTG/UVG-Overrides
        if (dto.KtgTagessatzManuellSet)
            employee.KtgTagessatzManuell = dto.KtgTagessatzManuell;
        if (dto.KtgKarenzAbgeschlossen.HasValue)
            employee.KtgKarenzAbgeschlossen = dto.KtgKarenzAbgeschlossen.Value;

        // ── Aktiv-Status ──────────────────────────────────────────────────
        // Walter-Vorgabe 18.05.2026: KEIN Auto-Sync mehr aus ExitDate. Grund:
        // ein MA der unerwartet mitten im Monat austritt bekommt Ende Monat
        // trotzdem noch einen Lohn — also muss er aktiv bleiben bis Walter
        // den Haken im UI explizit entfernt (nach dem letzten Lohnlauf).
        //
        // Der Massenimport (EmployeeImportController) hat seine eigene Logik
        // für leere Filialen — die bleibt unverändert, weil dort beim ersten
        // Import alle inaktiven MA vom CSV-Stand übernommen werden müssen.
        //
        // Sicherheits-Lock (Walter 18.05.2026): Setzen auf inaktiv ist nur
        // erlaubt wenn KEINE offene Lohnperiode (Definitiv != abgeschlossen)
        // existiert, in deren Zeitraum für diesen MA noch Stempelzeiten oder
        // Absenzen erfasst sind. Sonst würde ein noch zu berechnender Lohn
        // verlorengehen. Frontend ruft dieselbe Logik vorab via GET
        // /api/employees/{id}/deactivate-check auf, damit der User die Sperre
        // direkt beim Klick auf die Checkbox sieht.
        if (dto.IsActive.HasValue)
        {
            if (employee.IsActive && !dto.IsActive.Value)
            {
                var blockers = await ComputeDeactivateBlockersAsync(id);
                if (blockers.Count > 0)
                {
                    return Conflict(new
                    {
                        error   = "MA_HAS_OPEN_PERIOD_DATA",
                        message = BuildBlockerMessage(blockers),
                        blockers
                    });
                }
            }
            employee.IsActive = dto.IsActive.Value;
        }

        // Walter-Vorgabe 07.06.2026: Anstellungs-Booleans übernehmen, wenn gesendet.
        if (dto.LgavPflichtig.HasValue)        employee.LgavPflichtig        = dto.LgavPflichtig.Value;
        if (dto.TeilzeitUnter8hWoche.HasValue) employee.TeilzeitUnter8hWoche = dto.TeilzeitUnter8hWoche.Value;

        await _context.SaveChangesAsync();

        // Postfach-Login mit Aktiv-Status synchronisieren (idempotent).
        // Greift jetzt nur noch wenn Walter den Haken bewusst geändert hat —
        // beim reinen ExitDate-Setzen bleibt das Postfach offen.
        await _postfach.SyncActiveStateAsync(employee);

        return Ok(employee);
    }

    // GET /api/employees/{id}/deactivate-check
    // Live-Check fürs Frontend (Walter-Vorgabe 18.05.2026): wird beim Klick
    // auf die „Aktiv"-Checkbox im MA-Edit-Modal aufgerufen, bevor gespeichert
    // wird. Antwort listet alle blockierenden Lohnperioden + den Grund je
    // Periode (Stempelzeiten ja/nein, Absenz-Typen + Datumsbereiche). Damit
    // sieht der User SOFORT warum Inaktiv-Setzen nicht geht.
    [HttpGet("{id:int}/deactivate-check")]
    public async Task<IActionResult> DeactivateCheck(int id)
    {
        var employee = await _context.Employees.FindAsync(id);
        if (employee is null) return NotFound();
        var blockers = await ComputeDeactivateBlockersAsync(id);
        return Ok(new
        {
            employeeId     = id,
            canDeactivate  = blockers.Count == 0,
            blockers,
            message        = blockers.Count == 0
                                ? "MA kann inaktiv gesetzt werden."
                                : BuildBlockerMessage(blockers)
        });
    }

    /// <summary>
    /// Sammelt alle blockierenden Lohnperioden für eine Inaktivsetzung
    /// (Walter-Vorgabe 18.05.2026): pro noch nicht definitiv abgeschlossener
    /// Periode in einer Filiale des MA prüfen, ob Stempelzeiten oder Absenzen
    /// im Periode-Range hängen. Reine Liste, kein Fehlerzustand — die
    /// Aufrufer (PUT-Endpoint + Live-Check) entscheiden ob 409 oder OK.
    /// </summary>
    private async Task<List<DeactivateBlocker>> ComputeDeactivateBlockersAsync(int employeeId)
    {
        var maFilialIds = await _context.Employments
            .Where(em => em.EmployeeId == employeeId && em.CompanyProfileId.HasValue)
            .Select(em => em.CompanyProfileId!.Value)
            .Distinct()
            .ToListAsync();

        var openPeriods = await _context.PayrollPerioden
            .Where(p => p.Status != "abgeschlossen"
                     && maFilialIds.Contains(p.CompanyProfileId))
            .OrderBy(p => p.PeriodFrom)
            .ToListAsync();

        var blockers = new List<DeactivateBlocker>();
        foreach (var period in openPeriods)
        {
            var timeEntriesCount = await _context.EmployeeTimeEntries.CountAsync(t =>
                t.EmployeeId == employeeId
             && t.EntryDate >= period.PeriodFrom
             && t.EntryDate <= period.PeriodTo);
            var absencesInPeriod = await _context.Absences
                .Where(a => a.EmployeeId == employeeId
                         && a.DateFrom <= period.PeriodTo
                         && a.DateTo   >= period.PeriodFrom)
                .OrderBy(a => a.DateFrom)
                .Select(a => new AbsenceSummary {
                    Id        = a.Id,
                    Type      = a.AbsenceType,
                    DateFrom  = a.DateFrom,
                    DateTo    = a.DateTo
                })
                .ToListAsync();
            if (timeEntriesCount == 0 && absencesInPeriod.Count == 0) continue;

            var periodLabel = string.IsNullOrWhiteSpace(period.Label)
                ? $"{period.Year}-{period.Month:D2}"
                : period.Label;

            blockers.Add(new DeactivateBlocker
            {
                PeriodId         = period.Id,
                PeriodLabel      = periodLabel,
                PeriodFrom       = period.PeriodFrom,
                PeriodTo         = period.PeriodTo,
                TimeEntriesCount = timeEntriesCount,
                Absences         = absencesInPeriod
            });
        }
        return blockers;
    }

    private static string BuildBlockerMessage(List<DeactivateBlocker> blockers)
    {
        if (blockers.Count == 0) return string.Empty;
        var b = blockers[0];
        var arten = new List<string>();
        if (b.TimeEntriesCount > 0) arten.Add($"{b.TimeEntriesCount} Stempel-Eintrag(e)");
        if (b.Absences.Count > 0)
        {
            // Absenz-Typen zusammenfassen (z.B. "Krankheit" + "Ferien")
            var typLabels = b.Absences
                .Select(a => AbsenceTypeLabel(a.Type))
                .Distinct()
                .ToList();
            arten.Add(string.Join(" / ", typLabels));
        }
        var artenText = string.Join(" + ", arten);
        return $"MA kann nicht inaktiv gesetzt werden - in der noch offenen Lohnperiode '{b.PeriodLabel}' sind {artenText} erfasst. Bitte zuerst den Lohnlauf abschliessen oder die Daten loeschen.";
    }

    private static string AbsenceTypeLabel(string type) => type switch
    {
        "KRANK"      => "Krankheit",
        "UNFALL"     => "Unfall",
        "FERIEN"     => "Ferien",
        "SCHULUNG"   => "Schulung",
        "MUTT_VATER" => "Mutter-/Vaterschaft",
        _            => type
    };

    public class DeactivateBlocker
    {
        public int      PeriodId         { get; set; }
        public string   PeriodLabel      { get; set; } = "";
        public DateOnly PeriodFrom       { get; set; }
        public DateOnly PeriodTo         { get; set; }
        public int      TimeEntriesCount { get; set; }
        public List<AbsenceSummary> Absences { get; set; } = new();
    }

    public class AbsenceSummary
    {
        public int      Id       { get; set; }
        public string   Type     { get; set; } = "";
        public DateOnly DateFrom { get; set; }
        public DateOnly DateTo   { get; set; }
    }

    // PUT /api/employees/{id}/employment/{employmentId} – Vertragsdaten aktualisieren
    [HttpPut("{id:int}/employment/{employmentId:int}")]
    public async Task<IActionResult> UpdateEmployment(int id, int employmentId, [FromBody] EmploymentUpdateDto dto)
    {
        var emp = await _context.Employments
            .FirstOrDefaultAsync(e => e.Id == employmentId && e.EmployeeId == id);
        if (emp is null) return NotFound();

        if (dto.JobTitle        is not null) emp.JobTitle        = dto.JobTitle        == "" ? null : dto.JobTitle;
        if (dto.ContractType    is not null) emp.ContractType    = dto.ContractType    == "" ? null : dto.ContractType;
        if (dto.EmploymentModel is not null) emp.EmploymentModel = dto.EmploymentModel;
        if (dto.EmploymentPercentage.HasValue)    emp.EmploymentPercentage    = dto.EmploymentPercentage;
        if (dto.WeeklyHours.HasValue)             emp.WeeklyHours             = dto.WeeklyHours;
        if (dto.GuaranteedHoursPerWeek.HasValue)  emp.GuaranteedHoursPerWeek  = dto.GuaranteedHoursPerWeek;
        if (dto.HourlyRate.HasValue)              emp.HourlyRate              = dto.HourlyRate;
        if (dto.MonthlySalary.HasValue)           emp.MonthlySalary           = dto.MonthlySalary;
        if (dto.MonthlySalaryFte.HasValue)        emp.MonthlySalaryFte        = dto.MonthlySalaryFte;
        if (dto.ContractStartDate.HasValue)       emp.ContractStartDate       = dto.ContractStartDate.Value;
        if (dto.ContractEndDateSet)               emp.ContractEndDate         = dto.ContractEndDate;
        if (dto.ProbationPeriodMonths.HasValue)   emp.ProbationPeriodMonths   = dto.ProbationPeriodMonths;
        if (dto.ProbationEndDate.HasValue)        emp.ProbationEndDate        = dto.ProbationEndDate;

        await _context.SaveChangesAsync();
        return Ok(emp);
    }

    /// <summary>
    /// QST-Befreiung durch die Steuerbehörde setzen / aufheben (Walter-Vorgabe
    /// 26.05.2026). Befreiung benötigt: Dokument-ID (Bestätigungsschreiben aus
    /// dem MA-Dokumente-Tab) UND Gueltig-ab-Datum. Aufheben = `befreit:false`.
    /// </summary>
    [HttpPatch("{id:int}/qst-befreiung")]
    public async Task<IActionResult> SetQstBefreiung(int id, [FromBody] QstBefreiungDto dto)
    {
        var emp = await _context.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (emp == null) return NotFound();

        if (!dto.Befreit)
        {
            // Aufheben — alle vier Felder zurücksetzen
            emp.QstBefreitDurchBehoerde = false;
            emp.QstBefreiungDokumentId  = null;
            emp.QstBefreiungGueltigAb   = null;
            emp.QstBefreiungGueltigBis  = null;
        }
        else
        {
            // Setzen — Dok-ID und Gueltig-ab Pflicht
            if (!dto.DokumentId.HasValue)
                return BadRequest(new { error = "DOKUMENT_PFLICHT", message = "Befreiung benötigt das Bestätigungsschreiben der Steuerbehörde als verlinktes Dokument." });
            if (!dto.GueltigAb.HasValue)
                return BadRequest(new { error = "GUELTIG_AB_PFLICHT", message = "Befreiung benötigt ein Gültig-ab-Datum." });

            // Dokument muss diesem MA gehören
            var dokOk = await _context.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == id);
            if (!dokOk)
                return BadRequest(new { error = "DOKUMENT_INVALID", message = "Das verlinkte Dokument gehört nicht zu diesem Mitarbeiter." });

            emp.QstBefreitDurchBehoerde = true;
            emp.QstBefreiungDokumentId  = dto.DokumentId;
            emp.QstBefreiungGueltigAb   = dto.GueltigAb;
            emp.QstBefreiungGueltigBis  = dto.GueltigBis;
        }

        await _context.SaveChangesAsync();
        return Ok(new
        {
            id = emp.Id,
            qstBefreitDurchBehoerde = emp.QstBefreitDurchBehoerde,
            qstBefreiungDokumentId  = emp.QstBefreiungDokumentId,
            qstBefreiungGueltigAb   = emp.QstBefreiungGueltigAb,
            qstBefreiungGueltigBis  = emp.QstBefreiungGueltigBis
        });
    }

    /// <summary>
    /// Live-Check: ist der MA am angegebenen Stichtag QST-pflichtig + ist eine
    /// QST-Erfassung vorhanden? Verwendet für den QST-Tab-Banner und das
    /// Dashboard-Audit. Stichtag default = heute.
    /// </summary>
    [HttpGet("{id:int}/qst-pflicht")]
    public async Task<IActionResult> GetQstPflicht(int id, [FromQuery] DateOnly? stichtag, [FromServices] QstPflichtCheckService check)
    {
        var date = stichtag ?? DateOnly.FromDateTime(DateTime.Today);
        var result = await check.CheckAsync(id, date);
        return Ok(new
        {
            isPflichtOffen = result.IsPflichtOffen,
            isQstPflichtig = result.IsQstPflichtig,
            hasErfassung   = result.HasErfassung,
            befreiungsGrund = result.BefreiungsGrund,
            message = result.Message,
            // Walter-Vorgabe 28.05.2026: bei Behörden-Befreiung Dok-ID +
            // Gültig-ab/bis durchreichen, damit das Frontend das Bestätigungs-
            // schreiben direkt im Vorschau-Side-Panel öffnen kann.
            befreiungsDokumentId  = result.BefreiungsDokumentId,
            befreiungsGueltigAb   = result.BefreiungsGueltigAb,
            befreiungsGueltigBis  = result.BefreiungsGueltigBis,
            stichtag = date
        });
    }

    private static string NormalizeRestaurantPrefix(string? restaurantCode)
    {
        var digits = Regex.Replace(restaurantCode ?? "", @"\D", "");
        digits = digits.TrimStart('0');
        return string.IsNullOrWhiteSpace(digits) ? "" : digits;
    }

    private static string NormalizeEmployeeNumber(string? employeeNumber)
    {
        return Regex.Replace(employeeNumber ?? "", @"\s", "");
    }
}

/// <summary>DTO für Updates auf Employee-Stammdaten (Personalien, Adresse, Aufenthalt).</summary>
public class EmployeeUpdateDto
{
    // Personalien
    public string?   FirstName    { get; set; }
    public string?   LastName     { get; set; }
    public string?   Salutation   { get; set; }
    public string?   Gender       { get; set; }
    public DateTime? DateOfBirth  { get; set; }
    public string?   LanguageCode { get; set; }
    public int?      NationalityId { get; set; }
    public string?   PhoneMobile  { get; set; }
    public string?   Email        { get; set; }

    // Adresse
    public string?   Street      { get; set; }
    public string?   HouseNumber { get; set; }
    public string?   ZipCode     { get; set; }
    public string?   City        { get; set; }
    public string?   Country     { get; set; }
    public string?   CantonCode  { get; set; }

    /// <summary>
    /// Walter-Vorgabe 06.06.2026: Importer/Wartung kann explizit eine Re-Ableitung
    /// des Kantons aus dem PLZ-Verzeichnis erzwingen — auch wenn die PLZ
    /// unverändert ist. Korrigiert frühere Falscheinträge (z.B. easy@work-Import
    /// mit fehlerhafter Region-Spalte). Wirkt nur, wenn KEIN expliziter CantonCode
    /// im DTO mitkommt.
    /// </summary>
    public bool      ForceCantonFromZip { get; set; }

    // Aufenthalt
    public int?      PermitTypeId     { get; set; }
    // PermitExpiryDate-Property bleibt akzeptiert (für Rückwärtskompatibilität mit
    // älteren Frontends), wird aber im Update-Pfad IGNORIERT (Walter 01.06.2026).
    public DateTime? PermitExpiryDate { get; set; }
    public string?   ZemisNumber      { get; set; }

    /// <summary>Wenn true, wird QuellensteuerBefreitAb gesetzt (auch wenn null → Befreiung aufheben).</summary>
    public bool      QuellensteuerBefreitAbSet { get; set; } = false;
    public DateOnly? QuellensteuerBefreitAb    { get; set; }

    // Ein-/Austritt
    public DateTime? EntryDate   { get; set; }
    public bool      ExitDateSet { get; set; } = false;
    public DateTime? ExitDate    { get; set; }

    // ALV / Zwischenverdienst
    public string? AhvNummer  { get; set; }
    public string? MaritalStatus { get; set; }

    // Erweiterte Zivilstand-Angaben (allgemein, nicht nur QST)
    public bool      MaritalStatusSinceSet { get; set; } = false;
    public DateOnly? MaritalStatusSince    { get; set; }
    public bool      SeparatedSinceSet     { get; set; } = false;
    public DateOnly? SeparatedSince        { get; set; }
    public string?   Religion              { get; set; }
    public string?   LetterSalutation      { get; set; }
    public string?   PlaceOfOrigin         { get; set; }

    // "Kein Lohn"-Flag — nur durch admin/superuser setzbar.
    // null = nicht ändern; true/false = setzen.
    public bool?     IsPayrollExcluded     { get; set; }

    // Aktiv-Flag (Walter-Vorgabe 18.05.2026): explizit gesetzt vom UI,
    // KEIN Auto-Sync mehr aus ExitDate. Grund: MA kann unerwartet mitten
    // im Monat austreten, bekommt aber Ende Monat noch einen Lohn — also
    // muss er bis nach dem letzten Lohnlauf aktiv bleiben.
    // null = nicht ändern; true/false = setzen.
    public bool?     IsActive              { get; set; }

    // Anstellungs-Booleans (Walter 07.06.2026)
    public bool?     LgavPflichtig         { get; set; }
    public bool?     TeilzeitUnter8hWoche  { get; set; }

    // KTG/UVG-Overrides für Legacy-MA aus dem alten Lohnsystem.
    // Set-Flags damit "null absichtlich = Auto zurück" möglich ist.
    public bool      KtgTagessatzManuellSet { get; set; } = false;
    public decimal?  KtgTagessatzManuell    { get; set; }
    public bool?     KtgKarenzAbgeschlossen { get; set; }
}

/// <summary>DTO für Updates auf Employment-Vertragsdaten.</summary>
public class EmploymentUpdateDto
{
    public string?   JobTitle               { get; set; }
    public string?   ContractType           { get; set; }
    public string?   EmploymentModel        { get; set; }
    public decimal?  EmploymentPercentage   { get; set; }
    public decimal?  WeeklyHours            { get; set; }
    public decimal?  GuaranteedHoursPerWeek { get; set; }
    public decimal?  HourlyRate             { get; set; }
    public decimal?  MonthlySalary          { get; set; }
    public decimal?  MonthlySalaryFte       { get; set; }
    public DateTime? ContractStartDate      { get; set; }
    public bool      ContractEndDateSet     { get; set; } = false;
    public DateTime? ContractEndDate        { get; set; }
    public int?      ProbationPeriodMonths  { get; set; }
    public DateTime? ProbationEndDate       { get; set; }
}

/// <summary>QST-Befreiung durch Steuerbehörde (Walter 26.05.2026).</summary>
public class QstBefreiungDto
{
    public bool      Befreit    { get; set; }   // false → alle vier Felder zurücksetzen
    public int?      DokumentId { get; set; }   // FK auf employee_dokument; Pflicht wenn Befreit=true
    public DateOnly? GueltigAb  { get; set; }   // Pflicht wenn Befreit=true
    public DateOnly? GueltigBis { get; set; }   // NULL = unbefristet
}