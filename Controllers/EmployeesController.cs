using System.Security.Claims;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
using Microsoft.AspNetCore.Authorization;
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
    private readonly QstKonfessionSyncService _qstKonfessionSync;

    public EmployeesController(
        AppDbContext context,
        EmployeePostfachService postfach,
        QstKonfessionSyncService qstKonfessionSync)
    {
        _context = context;
        _postfach = postfach;
        _qstKonfessionSync = qstKonfessionSync;
    }

    /// <summary>
    /// Walter-Vorgabe 13.06.2026: Phantom-MA (IsPayrollExcluded=true) sind
    /// nur für admin sichtbar. Für alle anderen Rollen (user/superuser/HR/
    /// buchhaltung) werden sie aus den MA-Listen herausgefiltert.
    /// </summary>
    private bool IsAdminUser() => User.IsInRole("admin");

    /// <summary>
    /// Erlaubte Filialen des eingeloggten Users (Walter 22.07.2026 —
    /// Riesen-Bock-Fix: GF sah via «Alle Filialen» ALLE MA). null =
    /// unbeschraenkt (admin + reiner superuser). buchhaltung (Doppel-Claim
    /// ZUERST pruefen, CLAUDE.md) sowie user/lowuser sind serverseitig auf
    /// ihre user_branch_access-Filialen beschraenkt.
    /// </summary>
    private async Task<List<int>?> GetAllowedBranchIdsAsync()
    {
        if (User.IsInRole("admin")) return null;
        var restricted = User.IsInRole("buchhaltung") || !User.IsInRole("superuser");
        if (!restricted) return null;
        var idStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(idStr, out var uid)) return new List<int>();
        return await _context.UserBranchAccesses.AsNoTracking()
            .Where(a => a.UserId == uid)
            .Select(a => a.CompanyProfileId)
            .ToListAsync();
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        // Walter-Vorgabe 12.06.2026: IsHidden ausblenden (Soft-Delete).
        // Walter-Vorgabe 13.06.2026: Phantom-MA nur für admin sichtbar.
        var isAdmin = IsAdminUser();
        // Serverseitige Filial-Beschraenkung (Walter 22.07.2026): GF/
        // buchhaltung/lowuser sehen NUR MA mit Vertrag in ihren Filialen —
        // unabhaengig davon, was das Frontend anfragt.
        var allowed = await GetAllowedBranchIdsAsync();
        var query = _context.Employees
            .Where(e => !e.IsHidden && (isAdmin || !e.IsPayrollExcluded));
        if (allowed != null)
            query = query.Where(e => e.Employments.Any(em =>
                em.CompanyProfileId != null && allowed.Contains(em.CompanyProfileId.Value)));
        var employees = await query
            .Include(e => e.Employments).ThenInclude(em => em.JobGroup)   // FK-Code für Frontend (Walter 26.05.2026)
            .Include(e => e.NationalityRef)  // für no-permit-Filter: Code CH statt nur Legacy-Freitext «Schweiz»
            .OrderBy(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim())
            .ToListAsync();

        // Schwangerschafts-/Mutterschutz-Marker für die MA-Liste (Walter 20.07.2026):
        // gleiche Fenster-Logik wie Header-Badge (bis 16 Wochen nach Geburt/ET).
        var today = DateOnly.FromDateTime(DateTime.Today);
        var pregnantIds = await _context.EmployeePregnancies
            .AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.EmployeeId, p.Geburtsdatum, p.ErrechneterTermin })
            .ToListAsync();
        // Zwei Zustände (Walter 12.08.2026): Geburt noch nicht erfasst =
        // «Schwanger»; Geburt erfasst + innerhalb 16 Wochen = «Mutterschutz».
        var inWindow = pregnantIds
            .Where(p =>
            {
                var basis = p.Geburtsdatum ?? p.ErrechneterTermin;
                return basis.AddDays(16 * 7) >= today;
            })
            .ToList();
        var pregnantSet  = inWindow.Where(p => p.Geburtsdatum == null).Select(p => p.EmployeeId).ToHashSet();
        var maternitySet = inWindow.Where(p => p.Geburtsdatum != null).Select(p => p.EmployeeId).ToHashSet();
        foreach (var e in employees)
        {
            e.IsMaternity = maternitySet.Contains(e.Id);
            e.IsPregnant  = !e.IsMaternity && pregnantSet.Contains(e.Id);
        }

        return Ok(employees);
    }

    /// <summary>ZEMIS-Nr setzen (Walter 12.07.2026) — von der Ausweis-OCR
    /// (MRZ-Zeile 1) oder manuell. Format 12345678.9. Schreibt in das
    /// EINZIGE ZEMIS-Feld <c>ZemisNumber</c> (zemis_number); das kurzlebige
    /// Duplikat zemis_nr ist konsolidiert/entfernt. OCR ÜBERSCHREIBT einen
    /// manuell erfassten Wert bewusst nicht (nur füllen wenn leer).</summary>
    [HttpPatch("{id:int}/zemis-nr")]
    public async Task<IActionResult> SetZemisNr(int id, [FromBody] ZemisDto dto)
    {
        var emp = await _context.Employees.FindAsync(id);
        if (emp == null) return NotFound();
        var neu = string.IsNullOrWhiteSpace(dto.ZemisNr) ? null : dto.ZemisNr.Trim();
        if (neu != null && !string.IsNullOrWhiteSpace(emp.ZemisNumber) && emp.ZemisNumber != neu)
            return Ok(new { ok = true, zemisNr = emp.ZemisNumber, kept = true });
        emp.ZemisNumber = neu ?? emp.ZemisNumber;
        await _context.SaveChangesAsync();
        return Ok(new { ok = true, zemisNr = emp.ZemisNumber });
    }
    public record ZemisDto(string? ZemisNr);

    [HttpGet("lookup")]
    public async Task<IActionResult> GetLookup()
    {
        var isAdmin = IsAdminUser();
        var allowed = await GetAllowedBranchIdsAsync();   // Filial-Schranke (22.07.2026)
        var q = _context.Employees
            .AsNoTracking()
            .Where(e => e.IsActive && !e.IsHidden && (isAdmin || !e.IsPayrollExcluded));
        if (allowed != null)
            q = q.Where(e => e.Employments.Any(em =>
                em.CompanyProfileId != null && allowed.Contains(em.CompanyProfileId.Value)));
        var employees = await q
            .OrderBy(e => ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim())
            .Select(e => new
            {
                Id = e.Id,
                DisplayName = ((e.FirstName ?? "") + " " + (e.LastName ?? "")).Trim() + " (" + e.EmployeeNumber + ")"
            })
            .ToListAsync();

        return Ok(employees);
    }

    /// <summary>
    /// Walter-Vorgabe 14.06.2026: leichter MA-Lookup-Endpoint für Picker /
    /// Datalists (Posteingang, HR-Lohnausweis/QST/RAV, d.velop-Importer,
    /// Dokumenten-Tab, Verträge-Liste …). Liefert NUR die Felder, die diese
    /// Dropdowns wirklich brauchen — kein Include-Graph, kein Stammdaten-
    /// blob. Schont Bandbreite + DB-Last bei jedem MA-Wechsel.
    ///
    /// Felder:
    ///   id, firstName, lastName, employeeNumber, isActive, isPayrollExcluded
    ///   employments[]: { companyProfileId, employmentModel, contractEndDate, isActive }
    ///
    /// Auch INAKTIVE MA + Phantom-MA (für Admin) sind enthalten — die
    /// Frontend-Picker filtern selbst nach Bedarf. Sortierung nach
    /// Vorname/Nachname wie überall im System.
    /// </summary>
    [HttpGet("lookup-full")]
    public async Task<IActionResult> GetLookupFull()
    {
        var isAdmin = IsAdminUser();
        var allowed = await GetAllowedBranchIdsAsync();   // Filial-Schranke (22.07.2026)
        var qf = _context.Employees
            .AsNoTracking()
            .Where(e => !e.IsHidden && (isAdmin || !e.IsPayrollExcluded));
        if (allowed != null)
            qf = qf.Where(e => e.Employments.Any(em =>
                em.CompanyProfileId != null && allowed.Contains(em.CompanyProfileId.Value)));
        var employees = await qf
            .OrderBy(e => (e.FirstName ?? "")).ThenBy(e => (e.LastName ?? ""))
            .Select(e => new
            {
                id                = e.Id,
                firstName         = e.FirstName,
                lastName          = e.LastName,
                employeeNumber    = e.EmployeeNumber,
                // Alte Personalnummern (Restaurant-Wechsel/Archiv, Walter 10.07.2026):
                // damit Picker/Auto-Matcher (z.B. d.velop-Import) einen MA auch über
                // seine frühere Nummer finden (104374 → heute 2300022).
                numberAliases     = e.NumberAliases.Select(a => a.Number).ToList(),
                dateOfBirth       = e.DateOfBirth,
                isActive          = e.IsActive,
                isPayrollExcluded = e.IsPayrollExcluded,
                employments = e.Employments.Select(em => new {
                    companyProfileId = em.CompanyProfileId,
                    employmentModel  = em.EmploymentModel,
                    contractEndDate  = em.ContractEndDate,
                    isActive         = em.IsActive
                }).ToList()
            })
            .ToListAsync();

        return Ok(employees);
    }

    [HttpGet("lookup/company/{companyId:int}")]
    public async Task<IActionResult> GetEmployeesForCompany(int companyId)
    {
        var allowedC = await GetAllowedBranchIdsAsync();   // Filial-Schranke (22.07.2026)
        if (allowedC != null && !allowedC.Contains(companyId))
            return StatusCode(403, new { error = "BRANCH_FORBIDDEN", message = "Kein Zugriff auf diese Filiale." });
        var company = await _context.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == companyId);

        if (company == null)
            return NotFound("Company not found.");

        var restaurantPrefix = NormalizeRestaurantPrefix(company.RestaurantCode);

        var isAdmin = IsAdminUser();
        var employees = await _context.Employees
            .Where(e => e.IsActive && !e.IsHidden && (isAdmin || !e.IsPayrollExcluded))
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
            .Include(e => e.PermitType)
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == id);

        if (employee == null)
            return NotFound();

        // Verträge separat AsNoTracking laden (Walter-Bug 26.07.2026): nach
        // easy@work-Sync immer frischer DB-Stand — kein veralteter Include-
        // Graph aus dem Change-Tracker. Sortierung wie bisher (neueste zuerst).
        employee.Employments = await _context.Employments.AsNoTracking()
            .Include(em => em.JobGroup)
            .Where(em => em.EmployeeId == id)
            .OrderByDescending(em => em.ContractStartDate)
            .ToListAsync();

        // Filial-Schranke (Walter 22.07.2026): beschraenkte Rollen kommen
        // nicht an MA fremder Filialen (auch nicht per direkter URL).
        var allowedG = await GetAllowedBranchIdsAsync();
        if (allowedG != null && !employee.Employments.Any(em =>
                em.CompanyProfileId != null && allowedG.Contains(em.CompanyProfileId.Value)))
            return StatusCode(403, new { error = "BRANCH_FORBIDDEN",
                message = "Kein Zugriff auf Mitarbeitende anderer Filialen." });

        // Klartext-Name der Nationalität (Walter-Vorgabe 14.05.2026, präzisiert
        // 13.06.2026): immer Volltext anzeigen, nie nur den ISO-Code. Quelle:
        //   1) `Nationality.NameDe` aus der DB (Walter pflegt die Liste in den
        //      Systemeinstellungen — fehlt sie, wird sie dort ergänzt)
        //   2) als allerletzter Ausweg der Code selbst
        // KEINE hardgecodete Fallback-Tabelle mehr.
        string? natName = null;
        var natCode = employee.NationalityRef?.Code ?? employee.Nationality;
        if (!string.IsNullOrWhiteSpace(natCode))
        {
            // Falls der FK gesetzt ist, sind Code + NameDe bereits geladen.
            // Sonst (Legacy-Pfad: nur `employee.Nationality` als String) noch
            // einmal anhand des Codes in die Tabelle schauen — auch da nur DB.
            var nameDe = employee.NationalityRef?.NameDe;
            if (string.IsNullOrWhiteSpace(nameDe))
            {
                nameDe = await _context.Nationalities
                    .Where(n => n.Code == natCode)
                    .Select(n => n.NameDe)
                    .FirstOrDefaultAsync();
            }
            natName = !string.IsNullOrWhiteSpace(nameDe) ? nameDe : natCode;
        }

        // permitExpiryDate (abgeleitet) + permitType:
        // Walter-Vorgabe 07.06.2026 (final): „neueste" = höchstes ValidTo,
        // bei Gleichheit ÄLTESTES ValidFrom (= Original-Eintrag, nicht
        // Import-Duplikat). Konsistent mit EmployeePermitHistoryController.
        // Walter 14.06.2026: zusätzlich CurrentPermitHistoryId + Dokument-
        // Verknüpfung liefern, damit der Aufenthalt-Block in der MA-Maske
        // den 📎-Button direkt rendern kann (ohne extra Async-Roundtrip).
        DateOnly? permitExpiryDate = null;
        PermitType? latestPermitType = null;
        int? currentPermitHistoryId = null;
        int? currentPermitDokumentId = null;
        string? currentPermitDokumentName = null;
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
                currentPermitHistoryId  = newest.Id;
                currentPermitDokumentId = newest.DokumentId;
                if (newest.DokumentId.HasValue)
                {
                    currentPermitDokumentName = await _context.EmployeeDokumente
                        .Where(d => d.Id == newest.DokumentId.Value)
                        .Select(d => d.FilenameOriginal)
                        .FirstOrDefaultAsync();
                }
            }
        }

        // Aktiver Vertrag = ContractEndDate IS NULL (kein Enddatum = laufend)
        // Fallback: neuester Vertrag
        var active = employee.Employments.FirstOrDefault(c => c.ContractEndDate == null)
                  ?? employee.Employments.FirstOrDefault();

        // Nachtarbeit-Compliance (ArGV1 Art. 30): >18 Nächte in 42 Tagen →
        // Arztzeugnis + Ausnahmeregelung Pflicht. Für die Übersicht-Karte,
        // damit fehlende Nachweise dort rot erscheinen (Walter 19.07.2026).
        // Gleiche Regel wie Dashboard / Kontroll-Liste. MA mit Austritt ≤ 30 Tage
        // werden nicht mehr als dokumentationspflichtig markiert.
        var todayNw = DateOnly.FromDateTime(DateTime.Today);
        bool nightWorkRequiresDocuments = false;
        int nightWorkMaxNights = 0;
        bool exitingSoon = employee.ExitDate.HasValue
            && DateOnly.FromDateTime(employee.ExitDate.Value) <= todayNw.AddDays(30);
        if (!exitingSoon)
        {
            var rollStart = new DateOnly(todayNw.Year, todayNw.Month, 1).AddMonths(-11);
            var nightDates = await _context.EmployeeTimeEntries.AsNoTracking()
                .Where(t => t.EmployeeId == employee.Id
                         && t.EntryDate >= rollStart
                         && t.EntryDate <= todayNw
                         && (t.NightHours ?? 0m) > 0m)
                .Select(t => t.EntryDate)
                .Distinct()
                .ToListAsync();
            if (nightDates.Count > 0)
            {
                var nw = NightWorkComplianceService.Evaluate(nightDates, todayNw);
                nightWorkRequiresDocuments = nw.RequiresDocuments;
                nightWorkMaxNights = nw.MaxNightsInSixWeeks;
            }
        }

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
            employee.MaidenName,
            employee.ShortName,
            employee.Gender,
            employee.DateOfBirth,
            employee.LanguageCode,
            employee.Nationality,
            employee.NationalityId,
            nationalityCode = natCode,
            nationalityCode3 = employee.NationalityRef?.Code3,  // Ausweis-Kürzel BGR/MKD/… (Walter 12.07.2026)
            nationalityName = natName,  // Klartext aus AppText (z.B. "Bosnien und Herzegowina")
            employee.PhoneMobile,
            employee.Phone2,
            employee.Email,
            employee.EntryDate,
            employee.ExitDate,
            // Kündigungs-Daten (Walter 16.07.2026): vom Kündigungsschreiben
            // gesetzt, vom Rückzug gelöscht; in der Anstellungs-Zeile editierbar.
            employee.KuendigungAusgesprochenAm,
            employee.KuendigungPer,
            employee.KuendigungDurch,
            employee.Austrittsgrund,
            employee.IsActive,
            employee.IsPayrollExcluded,
            employee.LgavPflichtig,
            employee.TeilzeitUnter8hWoche,
            // Nachtarbeit (Walter 26.07.2026): gültig-bis ist immer das gerechnete
            // Soll; nightWorkExamMismatch = easy@work-Bis weicht ab (Sync-Flag).
            employee.NightWorkExamValidUntil,
            employee.NightWorkExamIssued,
            nightWorkExamSollBis = employee.NightWorkExamIssued.HasValue
                ? Employee.NightWorkValidUntil(
                    DateOnly.FromDateTime(employee.NightWorkExamIssued.Value),
                    employee.DateOfBirth.HasValue ? DateOnly.FromDateTime(employee.DateOfBirth.Value) : (DateOnly?)null)
                  .ToDateTime(TimeOnly.MinValue)
                : (DateTime?)null,
            nightWorkExamMismatch = employee.NightWorkExamEasyMismatch,
            employee.NightWorkExamDokumentId,
            employee.NightWorkAusnahmeDokumentId,
            // Probezeitgespräch 1/2 (Walter 20.07.2026, Restaurant Admin)
            employee.ProbezeitGespraech1Am,
            employee.ProbezeitGespraech1DokumentId,
            employee.ProbezeitGespraech2Am,
            employee.ProbezeitGespraech2DokumentId,
            // ArGV1 Art. 30 — für rote «fehlt»-Hinweise auf der Nachtarbeit-Karte
            nightWorkRequiresDocuments,
            nightWorkMaxNightsInSixWeeks = nightWorkMaxNights,
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
            // Walter 14.06.2026: aktuelle Permit-History-ID + verknüpftes Doku
            // für den 📎-Button auf der Aufenthalt-Karte in der MA-Maske.
            currentPermitHistoryId    = currentPermitHistoryId,
            currentPermitDokumentId   = currentPermitDokumentId,
            currentPermitDokumentName = currentPermitDokumentName,
            zemisNumber = employee.ZemisNumber,
            eid = employee.Eid,
            sso = employee.Sso,
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
        if (dto.MaidenName   is not null) employee.MaidenName   = dto.MaidenName   == "" ? null : dto.MaidenName;
        if (dto.ShortName    is not null) employee.ShortName    = dto.ShortName    == "" ? null : dto.ShortName;
        if (dto.Salutation   is not null) employee.Salutation   = dto.Salutation   == "" ? null : dto.Salutation;
        if (dto.Gender       is not null) employee.Gender       = dto.Gender       == "" ? null : dto.Gender;
        if (dto.DateOfBirth  is not null) employee.DateOfBirth  = dto.DateOfBirth;
        if (dto.LanguageCode is not null) employee.LanguageCode = dto.LanguageCode == "" ? null : dto.LanguageCode;
        if (dto.NationalityId.HasValue)   employee.NationalityId = dto.NationalityId == 0 ? null : dto.NationalityId;
        if (dto.PhoneMobile  is not null) employee.PhoneMobile  = dto.PhoneMobile  == "" ? null : dto.PhoneMobile;
        if (dto.Phone2       is not null) employee.Phone2       = dto.Phone2       == "" ? null : dto.Phone2;
        if (dto.Email        is not null) employee.Email        = dto.Email        == "" ? null : dto.Email;

        // ── Adresse ───────────────────────────────────────────────────────
        // PLZ vor dem Überschreiben merken — wenn sie sich ändert, muss der
        // Kanton neu aus dem Ortschaftsverzeichnis abgeleitet werden.
        var zipBefore = employee.ZipCode?.Trim();
        if (dto.Street      is not null) employee.Street      = dto.Street      == "" ? null : dto.Street;
        if (dto.ZipCode     is not null) employee.ZipCode     = dto.ZipCode     == "" ? null : dto.ZipCode;
        // Kantons-Suffix («(BE)» / « BE») nie speichern — Walter 29.07.2026.
        if (dto.City        is not null) employee.City        = dto.City        == "" ? null
            : EasyAtWorkEmployeeSyncService.StripCityCantonSuffix(dto.City);
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

        // easy-Import: Ort muss zur PLZ passen — sonst 400 mit Klartext
        // (Walter 29.07.2026). Manuelles Editieren bleibt tolerant.
        if (dto.ForceCantonFromZip
            && !string.IsNullOrWhiteSpace(employee.ZipCode)
            && !string.IsNullOrWhiteSpace(employee.City))
        {
            var locs = await _context.SwissLocations.AsNoTracking()
                .Where(l => l.Plz4 == employee.ZipCode)
                .Select(l => new { l.Ortschaftsname, l.Gemeindename, l.Kantonskuerzel })
                .ToListAsync();
            var (city, canton, err) = EasyAtWorkEmployeeSyncService.ResolveCityFromLocations(
                employee.ZipCode!,
                employee.City,
                locs.Select(l => (l.Ortschaftsname, l.Gemeindename, l.Kantonskuerzel)).ToList());
            if (err != null)
                return BadRequest(new { error = "ORT_PLZ_MISMATCH", message = err });
            if (!string.IsNullOrWhiteSpace(city)) employee.City = city;
            if (!string.IsNullOrWhiteSpace(canton) && !cantonExplicit)
                employee.CantonCode = canton;
            await EnrichAddressFromZipAsync(employee, forceCantonRefresh: false);
        }
        else
        {
            await EnrichAddressFromZipAsync(employee, forceCantonRefresh: forceRefresh);
        }

        // ── Aufenthalt ────────────────────────────────────────────────────
        if (dto.PermitTypeId.HasValue)     employee.PermitTypeId     = dto.PermitTypeId == 0 ? null : dto.PermitTypeId;
        // dto.PermitExpiryDate wird IGNORIERT (Walter 01.06.2026) — das Ablauf-
        // Datum lebt nur noch auf EmployeePermitHistory.ValidTo. Frontend kann
        // das Feld noch senden, wird aber nicht mehr verarbeitet.
        if (dto.ZemisNumber is not null)   employee.ZemisNumber      = dto.ZemisNumber == "" ? null : dto.ZemisNumber;
        if (dto.Eid is not null)           employee.Eid              = dto.Eid == "" ? null : dto.Eid.Trim();
        if (dto.Sso is not null)           employee.Sso              = dto.Sso == "" ? null : dto.Sso.Trim();
        if (dto.QuellensteuerBefreitAbSet) employee.QuellensteuerBefreitAb = dto.QuellensteuerBefreitAb;

        // ── Ein-/Austritt ─────────────────────────────────────────────────
        if (dto.EntryDate.HasValue) employee.EntryDate = dto.EntryDate;
        if (dto.ExitDateSet)        employee.ExitDate  = dto.ExitDate;
        if (dto.KuendigungSet)
        {
            employee.KuendigungAusgesprochenAm = dto.KuendigungAusgesprochenAm;
            employee.KuendigungPer             = dto.KuendigungPer;
            // AG = durch uns, AN = durch Mitarbeiter; leer/null = zurücksetzen.
            var durch = string.IsNullOrWhiteSpace(dto.KuendigungDurch)
                ? null
                : dto.KuendigungDurch.Trim().ToUpperInvariant();
            if (durch != null && durch != "AG" && durch != "AN")
                return BadRequest(new { error = "KUENDIGUNG_DURCH_INVALID",
                    message = "Kündigung durch muss «AG» (durch uns) oder «AN» (durch Mitarbeiter) sein." });
            employee.KuendigungDurch = durch;
            // Austrittsgrund: leer = löschen; ungültiger Code → 400.
            if (string.IsNullOrWhiteSpace(dto.Austrittsgrund))
                employee.Austrittsgrund = null;
            else
            {
                var ag = AustrittsgrundCodes.Normalize(dto.Austrittsgrund);
                if (ag == null)
                    return BadRequest(new { error = "AUSTRITTSGRUND_INVALID",
                        message = "Ungültiger Austrittsgrund." });
                employee.Austrittsgrund = ag;
            }
        }

        // ── Nachtarbeit-Untersuchung gültig bis (Walter 20.06.2026) ──────────
        if (dto.NightWorkExamValidUntilSet) employee.NightWorkExamValidUntil = dto.NightWorkExamValidUntil;

        // ── ALV / Zwischenverdienst ───────────────────────────────────────
        if (dto.AhvNummer  is not null) employee.SocialSecurityNumber = dto.AhvNummer == "" ? null : dto.AhvNummer;
        if (dto.MaritalStatus is not null) employee.MaritalStatus = dto.MaritalStatus == "" ? null : dto.MaritalStatus;

        // ── Erweiterte Zivilstand-Angaben (allgemein) ────────────────────
        if (dto.MaritalStatusSinceSet) employee.MaritalStatusSince = dto.MaritalStatusSince;
        if (dto.SeparatedSinceSet)     employee.SeparatedSince     = dto.SeparatedSince;
        // Konfession → QST Kirchensteuer nachziehen (Walter 01.08.2026).
        // Sync läuft bei jedem Save der Religion mit (auch wenn der Wert
        // gleich bleibt) — fängt nachträgliche Korrekturen und bereits
        // gespeicherte, noch nicht nachgezogene QST-Einträge ab.
        bool religionInPayload = false;
        if (dto.Religion is not null)
        {
            employee.Religion = dto.Religion == "" ? null : dto.Religion;
            religionInPayload = true;
        }
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
            // Walter-Vorgabe 13.06.2026: „Kein Lohn"-Toggle ist admin-only.
            // Wenn ein non-admin den Wert ÄNDERN will → 403. Wenn er den
            // Wert nur unverändert mitsendet (Frontend-State), still
            // ignorieren — keine fälschliche Ablehnung des PUT-Calls.
            bool toggleAttempted = employee.IsPayrollExcluded != dto.IsPayrollExcluded.Value;
            if (toggleAttempted && !IsAdminUser())
            {
                return StatusCode(403, new {
                    error = "PHANTOM_TOGGLE_ADMIN_ONLY",
                    message = "Nur Admin darf „MA ohne Lohn“ setzen oder aufheben."
                });
            }
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

        // Offenen QST-Eintrag an die (ggf. neue) Konfession anpassen.
        // TrySync: Fehler dort dürfen den MA-Save nicht mehr killen.
        if (religionInPayload)
            await _qstKonfessionSync.TrySyncAsync(employee.Id, employee.Religion);

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
        if (dto.EasyAtWorkManualOverride.HasValue) emp.EasyAtWorkManualOverride = dto.EasyAtWorkManualOverride.Value;

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
    /// Walter-Vorgabe 13.06.2026: Beleg-Dokument für die automatische QST-Befreiung
    /// am MA verknüpfen oder aufheben. Welcher Slot bedient wird, hängt vom `kind`:
    ///   • kind = "id_pass"   → employee.id_pass_dokument_id   (für CH-Bürger)
    ///   • kind = "c_ausweis" → employee.c_ausweis_dokument_id (für C-Ausweis-Inhaber)
    /// Aufheben: dokumentId = null. Setzen: dokumentId muss diesem MA gehören.
    /// </summary>
    /// <summary>
    /// Nachtarbeit-Untersuchung „gültig bis" inline setzen (Walter 20.06.2026) —
    /// dedizierter Endpunkt, damit das Inline-Feld in der MA-Ansicht NICHT andere
    /// Anstellungs-Felder (Aktiv-Flag etc.) anfasst.
    /// </summary>
    [HttpPatch("{id:int}/night-work-exam-date")]
    public async Task<IActionResult> SetNightWorkExamDate(int id, [FromBody] NightExamDateDto dto)
    {
        var emp = await _context.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (emp == null) return NotFound();
        // Erfasst wird das AUSSTELLUNGSdatum; das Gültig-bis rechnen WIR mit der
        // zentralen Regel (Beginn + 1 bzw. 2 Jahre − 1 Tag, je Alter). Walter 05.07.2026.
        if (dto.Issued.HasValue)
        {
            var issued = DateOnly.FromDateTime(dto.Issued.Value);
            var dob = emp.DateOfBirth.HasValue ? DateOnly.FromDateTime(emp.DateOfBirth.Value) : (DateOnly?)null;
            emp.NightWorkExamIssued     = dto.Issued.Value.Date;
            emp.NightWorkExamValidUntil = Employee.NightWorkValidUntil(issued, dob).ToDateTime(TimeOnly.MinValue);
            emp.NightWorkExamEasyMismatch = false; // manuell → keine easy-Abweichung mehr anzeigen
        }
        else
        {
            emp.NightWorkExamIssued     = null;
            emp.NightWorkExamValidUntil = null;
            emp.NightWorkExamEasyMismatch = false;
        }
        await _context.SaveChangesAsync();
        return Ok(new { id = emp.Id, nightWorkExamIssued = emp.NightWorkExamIssued, nightWorkExamValidUntil = emp.NightWorkExamValidUntil });
    }
    public class NightExamDateDto { public DateTime? Issued { get; set; } }

    [HttpPatch("{id:int}/ausweis-doku")]
    public async Task<IActionResult> SetAusweisDoku(int id, [FromBody] AusweisDokuDto dto)
    {
        var emp = await _context.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (emp == null) return NotFound();

        var kind = (dto.Kind ?? "").Trim().ToLowerInvariant();
        if (kind != "id_pass" && kind != "c_ausweis" && kind != "night_work_exam"
            && kind != "night_work_ausnahme"
            && kind != "probezeit_gespraech1" && kind != "probezeit_gespraech2")
            return BadRequest(new { error = "KIND_INVALID", message = "kind ungültig." });

        if (dto.DokumentId.HasValue)
        {
            var dokOk = await _context.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == id);
            if (!dokOk)
                return BadRequest(new { error = "DOKUMENT_INVALID",
                    message = "Das verlinkte Dokument gehört nicht zu diesem Mitarbeiter." });
        }

        if (kind == "id_pass")                      emp.IdPassDokumentId               = dto.DokumentId;
        else if (kind == "c_ausweis")               emp.CAusweisDokumentId             = dto.DokumentId;
        else if (kind == "night_work_ausnahme")     emp.NightWorkAusnahmeDokumentId    = dto.DokumentId;
        else if (kind == "probezeit_gespraech1")    emp.ProbezeitGespraech1DokumentId  = dto.DokumentId;
        else if (kind == "probezeit_gespraech2")    emp.ProbezeitGespraech2DokumentId  = dto.DokumentId;
        else                                        emp.NightWorkExamDokumentId        = dto.DokumentId;

        await _context.SaveChangesAsync();
        return Ok(new
        {
            id                          = emp.Id,
            kind,
            idPassDokumentId            = emp.IdPassDokumentId,
            cAusweisDokumentId          = emp.CAusweisDokumentId,
            nightWorkExamDokumentId     = emp.NightWorkExamDokumentId,
            nightWorkAusnahmeDokumentId = emp.NightWorkAusnahmeDokumentId,
            probezeitGespraech1DokumentId = emp.ProbezeitGespraech1DokumentId,
            probezeitGespraech2DokumentId = emp.ProbezeitGespraech2DokumentId
        });
    }

    /// <summary>
    /// Probezeitgespräch 1 oder 2: Durchführungsdatum setzen/löschen
    /// (Walter 20.07.2026). Das ausgefüllte Protokoll wird separat via
    /// ausweis-doku (kind=probezeit_gespraech1|2) verknüpft.
    /// </summary>
    [HttpPatch("{id:int}/probezeit-gespraech")]
    // Review-Fix 22.07.2026: ohne eigenes Authorize galt die FallbackPolicy
    // inkl. lowuser (Nur-Lese-Rolle) — Schreiben jetzt explizit HR/GF.
    [Authorize(Roles = "admin,superuser,user")]
    public async Task<IActionResult> SetProbezeitGespraech(int id, [FromBody] ProbezeitGespraechDto dto)
    {
        var emp = await _context.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (emp == null) return NotFound();
        var nr = dto.Nr;
        if (nr != 1 && nr != 2)
            return BadRequest(new { error = "NR_INVALID", message = "nr muss 1 oder 2 sein." });
        if (nr == 1) emp.ProbezeitGespraech1Am = dto.Am?.Date;
        else         emp.ProbezeitGespraech2Am = dto.Am?.Date;
        await _context.SaveChangesAsync();
        return Ok(new
        {
            id = emp.Id,
            nr,
            probezeitGespraech1Am = emp.ProbezeitGespraech1Am,
            probezeitGespraech2Am = emp.ProbezeitGespraech2Am
        });
    }
    public class ProbezeitGespraechDto
    {
        public int Nr { get; set; }          // 1 oder 2
        public DateTime? Am { get; set; }    // null = zurücksetzen
    }

    /// <summary>
    /// Blanko-Formular «1. und 2. Gespräch» (Probezeit) — XLSX aus Assets,
    /// optional als PDF-Vorschau via LibreOffice (Walter 20.07.2026).
    /// </summary>
    /// <summary>
    /// Probezeit Gespräch / Gesprächsprotokoll als PDF (Walter 20.07.2026,
    /// 1 Seite ab 21.07.2026). MA + Ersteller vorausgefüllt; Beurteilungen
    /// und Unterschriften auf Papier. Speichert nichts.
    /// </summary>
    [HttpGet("{id:int}/probezeitbericht-pdf")]
    public async Task<IActionResult> GetProbezeitberichtPdf(
        int id,
        [FromQuery] int nr = 1,
        [FromServices] ProbezeitberichtPdfService pdf = null!)
    {
        if (nr != 1 && nr != 2) nr = 1;
        var e = await _context.Employees.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });

        var emp = await _context.Employments.AsNoTracking()
            .Include(em => em.JobGroup)
            .Include(em => em.CompanyProfile)
            .Where(em => em.EmployeeId == id && em.CompanyProfileId != null)
            .OrderByDescending(em => em.IsActive)
            .ThenByDescending(em => em.ContractStartDate)
            .FirstOrDefaultAsync();
        var cp = emp?.CompanyProfile;

        // Ersteller = eingeloggter User (Klarname); Telefon = Filiale (Walter).
        var uidStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        AppUser? user = null;
        if (int.TryParse(uidStr, out var uid))
            user = await _context.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == uid);

        string RolleLabel(string? role) => role switch
        {
            "admin" => "Admin",
            "superuser" => "HR",
            "buchhaltung" => "Buchhaltung",
            "user" => "GF / Restaurant",
            _ => role ?? ""
        };

        var gespraechAm = nr == 1 ? e.ProbezeitGespraech1Am : e.ProbezeitGespraech2Am;
        var abteilung = !string.IsNullOrWhiteSpace(emp?.JobTitle)
            ? emp!.JobTitle
            : (emp?.JobGroup?.Code ?? "");

        try
        {
            var input = new ProbezeitberichtInput(
                CompanyName: cp?.CompanyName ?? "Schaub Restaurants GmbH",
                RestaurantName: cp?.BranchName ?? cp?.FullDisplayName,
                MaNachname: e.LastName ?? "",
                MaVorname: e.FirstName ?? "",
                Abteilung: abteilung,
                Eintritt: e.EntryDate ?? emp?.ContractStartDate,
                ErstellerNachname: user?.LastName ?? "",
                ErstellerVorname: user?.FirstName ?? "",
                ErstellerFunktion: RolleLabel(user?.Role),
                ErstellerTelefon: cp?.Phone, // Filial-Telefon, nie persönliche Nummer
                GespraechAm: gespraechAm,
                GespraechOrt: cp?.City,
                GespraechNr: nr
            );
            var bytes = pdf.Generate(input);
            var fname = $"PZ-{(e.EmployeeNumber ?? id.ToString())}-{e.FirstName}.pdf"
                .Replace(" ", "_");
            return File(bytes, "application/pdf", fname);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = "PROBEZEITBERICHT_FEHLGESCHLAGEN",
                message = ex.GetBaseException().Message
            });
        }
    }

    /// <summary>Legacy blanko Excel «1. und 2. Gespräch» — optionaler Download.</summary>
    [HttpGet("probezeit-gespraech-formular")]
    public async Task<IActionResult> GetProbezeitGespraechFormular()
    {
        var path = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Forms", "Probezeitgespraech_1_und_2.xlsx");
        if (!System.IO.File.Exists(path))
            return NotFound(new { error = "FORMULAR_FEHLT", message = "Probezeitgespräch-Formular nicht auf dem Server gefunden." });
        var bytes = await System.IO.File.ReadAllBytesAsync(path);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "Probezeitgespraech_1_und_2.xlsx");
    }

    public class AusweisDokuDto
    {
        public string? Kind { get; set; }      // "id_pass" | "c_ausweis"
        public int?    DokumentId { get; set; } // null = Verknüpfung aufheben
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

        // Walter-Vorgabe 13.06.2026: aktuell verknüpfte Beleg-Doku-IDs
        // an das Frontend durchreichen, damit der QST-Banner den Beleg
        // direkt im Vorschau-Panel öffnen kann.
        int? idPassDokId      = null;
        int? cAusweisDokId    = null;
        int? spouseFamilyId   = null;
        int? spouseDokumentId = null;
        if (result.BefreiungsGrund == "CH-Buerger" || result.BefreiungsGrund == "C-Ausweis")
        {
            var emp = await _context.Employees
                .Where(e => e.Id == id)
                .Select(e => new { e.IdPassDokumentId, e.CAusweisDokumentId })
                .FirstOrDefaultAsync();
            if (emp != null)
            {
                if (result.BefreiungsGrund == "CH-Buerger") idPassDokId = emp.IdPassDokumentId;
                else                                          cAusweisDokId = emp.CAusweisDokumentId;
            }
        }
        if (result.BefreiungsGrund == "Ehepartner-CH" || result.BefreiungsGrund == "Ehepartner-C")
        {
            var spouse = await _context.EmployeeFamilyMembers
                .Where(f => f.EmployeeId == id && f.MemberType == "Ehepartner" && f.DateOfDeath == null)
                .OrderByDescending(f => f.Id)
                .Select(f => new { f.Id, f.DokumentId })
                .FirstOrDefaultAsync();
            if (spouse != null)
            {
                spouseFamilyId   = spouse.Id;
                spouseDokumentId = spouse.DokumentId;
            }
        }

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
            // Walter-Vorgabe 12.06.2026: bei Befreiung über Ehepartner (CH/C)
            // melden, ob der Ausweis des Ehepartners (linked_field_code=spouse)
            // beim MA hinterlegt ist — Frontend zeigt sonst einen roten
            // Warnbanner zusätzlich zum grünen Befreiungs-Banner.
            spouseDokumentFehlt   = result.SpouseDokumentFehlt,
            // Walter-Vorgabe 13.06.2026: dasselbe für den MA selbst:
            //   CH-Bürger → id_card/passport fehlt
            //   C-Ausweis → permit-Dokument fehlt
            employeeDokumentFehlt = result.EmployeeDokumentFehlt,
            // Aktuell verknüpfte Beleg-Doku-IDs (oben aufgelöst).
            idPassDokumentId   = idPassDokId,
            cAusweisDokumentId = cAusweisDokId,
            // Ehepartner-Beleg + Family-Member-ID für den Doku-Verknüpfungs-PATCH.
            spouseFamilyMemberId = spouseFamilyId,
            spouseDokumentId     = spouseDokumentId,
            stichtag = date
        });
    }

    // ══════════════════════════════════════════════════════════════════
    // MA LÖSCHEN (Walter-Vorgabe 12.06.2026)
    // ──────────────────────────────────────────────────────────────────
    // Zwei Pfade je nachdem, ob der MA Lohn-Daten hat:
    //   • Lohn-Daten vorhanden (PayrollSnapshot / PayrollSaldo / AkontoZahlung)
    //     → SOFT-DELETE: IsHidden = true + IsActive = false. Datensätze
    //     bleiben für Audit + Jahresauswertungen erhalten, MA ist aber in
    //     allen Listen, Pickern und im Lohnlauf ausgeblendet.
    //   • Keine Lohn-Daten → HARD-DELETE: alle abhängigen Tabellen werden
    //     in einer Transaktion gelöscht (Verträge, Bewilligungen, Doku,
    //     Bank, Familie, Absenzen, Stempelzeiten, etc.). Der app_user-
    //     Eintrag (MA-Postfach-Login) wird ebenfalls gelöscht.
    //
    // ZUGRIFF: nur admin (Walter-Vorgabe „admin und höher" — bei uns ist
    // admin die oberste Rolle, kein „und höher" möglich).
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Vorschau-Endpoint: zählt die abhängigen Datensätze und meldet den
    /// vorgesehenen Lösch-Modus (soft/hard), damit das Frontend die richtige
    /// Warnung anzeigen kann, BEVOR der User „Endgültig löschen" klickt.
    /// </summary>
    [HttpGet("{id:int}/delete-preview")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "admin")]
    public async Task<IActionResult> GetDeletePreview(int id)
    {
        var emp = await _context.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (emp == null) return NotFound();

        // Lohn-relevante Tabellen (entscheiden über soft vs. hard)
        var payrollSnapshotCount = await _context.PayrollSnapshots.CountAsync(p => p.EmployeeId == id);
        var payrollSaldoCount    = await _context.PayrollSaldos.CountAsync(s => s.EmployeeId == id);
        var akontoZahlungCount   = await _context.AkontoZahlungen.CountAsync(a => a.EmployeeId == id);
        var hasLohnData = payrollSnapshotCount > 0 || payrollSaldoCount > 0 || akontoZahlungCount > 0;

        // Weitere Datenmengen (informativ, damit Walter sieht, was hart
        // gelöscht würde — wird im Modal angezeigt).
        var employmentsCount  = await _context.Employments.CountAsync(e => e.EmployeeId == id);
        var documentsCount    = await _context.EmployeeDokumente.CountAsync(d => d.EmployeeId == id);
        var permitsCount      = await _context.EmployeePermitHistories.CountAsync(p => p.EmployeeId == id);
        var bankAccountsCount = await _context.EmployeeBankAccounts.CountAsync(b => b.EmployeeId == id);
        var familyCount       = await _context.EmployeeFamilyMembers.CountAsync(f => f.EmployeeId == id);
        var absencesCount     = await _context.Absences.CountAsync(a => a.EmployeeId == id);
        var timeEntriesCount  = await _context.EmployeeTimeEntries.CountAsync(t => t.EmployeeId == id);

        return Ok(new
        {
            employeeId     = id,
            employeeName   = $"{emp.FirstName} {emp.LastName}".Trim(),
            employeeNumber = emp.EmployeeNumber,
            isHidden       = emp.IsHidden,
            hasLohnData,
            mode           = hasLohnData ? "soft" : "hard",
            counts = new
            {
                payrollSnapshots = payrollSnapshotCount,
                payrollSaldi     = payrollSaldoCount,
                akontoZahlungen  = akontoZahlungCount,
                employments      = employmentsCount,
                documents        = documentsCount,
                permits          = permitsCount,
                bankAccounts     = bankAccountsCount,
                familyMembers    = familyCount,
                absences         = absencesCount,
                timeEntries      = timeEntriesCount
            }
        });
    }

    /// <summary>
    /// MA löschen. Entscheidet selbst soft vs. hard anhand der Lohn-Daten.
    /// Der `mode`-Query-Parameter erlaubt dem Frontend, einen erwarteten
    /// Modus mitzuschicken — wenn die Server-Realität abweicht (z.B. weil
    /// in der Zwischenzeit ein Lohnlauf abgeschlossen wurde), liefert der
    /// Server 409 statt unerwartet hart zu löschen.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id, [FromQuery] string? expectedMode = null)
    {
        var emp = await _context.Employees.FirstOrDefaultAsync(e => e.Id == id);
        if (emp == null) return NotFound();

        var hasLohnData = await _context.PayrollSnapshots.AnyAsync(p => p.EmployeeId == id)
                       || await _context.PayrollSaldos.AnyAsync(s => s.EmployeeId == id)
                       || await _context.AkontoZahlungen.AnyAsync(a => a.EmployeeId == id);
        var serverMode = hasLohnData ? "soft" : "hard";

        // Frontend hat einen Modus erwartet — wenn er sich vom Server-Modus
        // unterscheidet, abbrechen. Schützt vor versehentlichem Hart-Delete,
        // wenn jemand zwischendurch einen Lohnlauf bestätigt hat.
        if (!string.IsNullOrEmpty(expectedMode)
            && !string.Equals(expectedMode, serverMode, StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new
            {
                error   = "DELETE_MODE_CHANGED",
                message = $"Lösch-Modus hat sich geändert: erwartet '{expectedMode}', Server '{serverMode}'. Bitte Seite neu laden und Aktion bestätigen.",
                serverMode
            });
        }

        if (hasLohnData)
        {
            // ── SOFT-DELETE ──
            emp.IsHidden = true;
            emp.IsActive = false;
            await _context.SaveChangesAsync();
            return Ok(new
            {
                mode    = "soft",
                message = $"{emp.FirstName} {emp.LastName} wurde unsichtbar gemacht. Lohn-Daten bleiben erhalten."
            });
        }

        // ── HARD-DELETE ──
        // Alle Tabellen mit FK auf employee_id, Reihenfolge: zuerst Tabellen
        // mit indirekten Abhängigkeiten (FamilyMemberAllowance hängt an
        // EmployeeFamilyMember), dann der Rest, zuletzt der MA selbst.
        // Alles in einer Transaktion — bricht eine Anweisung, rollback alles.
        await using var tx = await _context.Database.BeginTransactionAsync();
        try
        {
            await HardDeleteEmployeeCoreAsync(id);
            await tx.CommitAsync();
            return Ok(new
            {
                mode    = "hard",
                message = $"{emp.FirstName} {emp.LastName} und alle zugehörigen Daten wurden gelöscht."
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return StatusCode(500, new
            {
                error   = "DELETE_FAILED",
                message = $"Löschen fehlgeschlagen: {ex.Message}"
            });
        }
    }

    // Hard-Delete-Kern (alle FK-Tabellen + MA selbst) — OHNE Transaktion,
    // der Aufrufer klammert. Geteilt von Delete (Einzel-MA) und
    // CleanupArchiv (Bulk «+alt»-Archiv-MA, Walter 05.08.2026).
    private async Task HardDeleteEmployeeCoreAsync(int id)
    {
        // 1) FamilyMemberAllowance — hängt an EmployeeFamilyMember
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM family_member_allowance WHERE family_member_id IN (SELECT id FROM employee_family_member WHERE employee_id = {0})", id);

        // 2) Alle direkten employee_id-Tabellen
        var directTables = new[]
        {
            "employee_family_member",
            "employee_address",
            "employee_education_history",
            "employee_import_snapshot",
            "employee_time_entry",
            "absence",
            "krankheit_karenz_saldo",
            "employee_lohn_durchschnitt",
            "employee_dokument",
            "mailbox_document",
            "lohn_zulage",
            "employee_recurring_wage",
            "employee_bvg_zusatz_member",
            "employee_pregnancy",
            "employee_lohn_assignment",
            "payroll_lohn_abtretung_entry",
            "employee_bank_account",
            "employee_quellensteuer",
            "employee_arbeitslosigkeit",
            "employee_permit_history",
            "employment"
        };
        foreach (var t in directTables)
        {
            await _context.Database.ExecuteSqlRawAsync(
                $"DELETE FROM {t} WHERE employee_id = {{0}}", id);
        }

        // 3) app_user (MA-Postfach-Login) — komplett entfernen, nicht
        // nur EmployeeId auf NULL setzen. Beim Hard-Delete soll auch
        // der Login weg.
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM app_user WHERE employee_id = {0}", id);

        // 4) MA selbst
        await _context.Database.ExecuteSqlRawAsync(
            "DELETE FROM employee WHERE id = {0}", id);
    }

    // ── Prä-Mirus-Karteileichen aufräumen (Walter 05.08.2026, final) ────────
    // Löscht ALLE inaktiven MA, deren letzte Anstellungs-Spur (Vertragsende/
    // Austritt) VOR dem 01.01.2025 (Mirus-Start) liegt — egal ob die Nummer
    // «…alt», «999…» oder normal ist. Sicherheitsnetz:
    //   • aktive MA / offene Verträge: nie angefasst
    //   • MA ohne jegliches Datum: übersprungen («kein Datum» ≠ «alt»)
    //   • Stempelzeiten / Lohn-Daten: nie gelöscht
    //   • Dokumente: übersprungen und namentlich gemeldet
    // Wiederkehr-Schutz: der Nacht-Sync holt nur HEUTE aktive easy-MA —
    // gelöschte Karteileichen kommen nicht zurück.
    // dryRun=true (Default) = nur Vorschau, es wird NICHTS gelöscht.
    [HttpPost("cleanup-archiv")]
    [Microsoft.AspNetCore.Authorization.Authorize(Roles = "admin")]
    public async Task<IActionResult> CleanupArchiv([FromQuery] bool dryRun = true)
    {
        // Prä-Mirus-Grenze (Walter 05.08.2026): Mirus-Einführung = 01.01.2025.
        // Ein «alt»-MA ist nur löschbar, wenn seine LETZTE Anstellungs-Spur
        // (Vertragsende/Austritt) VOR diesem Datum liegt. Die easy@work-
        // Verknüpfung allein blockiert NICHT (praktisch alle Alt-Importe
        // wurden per Duplikat-Merge verknüpft) — der Nacht-Sync zieht ohnehin
        // nur die HEUTE aktiven easy-MA, gelöschte Karteileichen kommen also
        // nicht zurück. Merge-Fälle mit echter Mirus-Ära-Historie (z.B.
        // Sweeba Akhtar: Wiedereintritt bis 2026) sind über Vertragsdaten/
        // Stempelzeiten geschützt.
        var mirusStart = new DateTime(2025, 1, 1);

        // Kriterium (Walter 05.08.2026, final): NICHT die Nummer entscheidet,
        // sondern die letzte Anstellungs-Spur — ALLE inaktiven MA mit Austritt/
        // Vertragsende VOR dem 01.01.2025 (Mirus-Start) werden erfasst, egal ob
        // «…alt», «999…» oder normale Nummer. MA ohne jegliches Datum bleiben
        // stehen («kein Datum» ist kein Beweis für «alt»).
        var alts = await _context.Employees.AsNoTracking()
            .Where(e => !e.IsHidden)
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.IsActive, e.ExitDate })
            .OrderBy(e => e.EmployeeNumber)
            .ToListAsync();

        var mitLohn = new List<object>();
        var mitDok  = new List<object>();
        var mitEaw  = new List<object>();
        var loeschbar = new List<(int Id, string Label)>();

        int geprueft = 0;
        foreach (var e in alts)
        {
            // Aktive MA sind nie Kandidaten — gar nicht erst auflisten.
            if (e.IsActive) continue;
            geprueft++;
            var label = $"{e.EmployeeNumber} — {e.FirstName} {e.LastName}";

            var vertragsEnden = await _context.Employments.AsNoTracking()
                .Where(em => em.EmployeeId == e.Id)
                .Select(em => em.ContractEndDate)
                .ToListAsync();
            bool offenerVertrag = vertragsEnden.Any(x => x == null);
            if (offenerVertrag)
            {
                mitEaw.Add(new { e.Id, label, grund = "hat offenen Vertrag" });
                continue;
            }

            // Letzte Anstellungs-Spur = spätestes Datum aus Vertragsende/Austritt.
            var maxEnde = vertragsEnden.Where(x => x != null).DefaultIfEmpty(null).Max();
            var spur = (maxEnde.HasValue && e.ExitDate.HasValue)
                ? (maxEnde.Value > e.ExitDate.Value ? maxEnde : e.ExitDate)
                : (maxEnde ?? e.ExitDate);
            if (!spur.HasValue)
            {
                mitEaw.Add(new { e.Id, label, grund = "kein Austritts-/Vertragsdatum — bitte manuell prüfen" });
                continue;
            }
            if (spur.Value >= mirusStart)
            {
                mitEaw.Add(new { e.Id, label, grund = $"Anstellung bis {spur.Value:dd.MM.yyyy} (Mirus-Ära)" });
                continue;
            }

            // Stempelzeiten sind bewusst KEIN Hindernis (Walter 05.08.2026) —
            // die Historie bleibt in easy@work nachschlagbar. Der Lohn-Guard
            // bleibt als stilles Sicherheitsnetz (prä-2025-MA können ohnehin
            // keine OneCrew-Lohndaten haben).
            bool hasLohn = await _context.PayrollSnapshots.AnyAsync(p => p.EmployeeId == e.Id)
                        || await _context.PayrollSaldos.AnyAsync(s => s.EmployeeId == e.Id)
                        || await _context.AkontoZahlungen.AnyAsync(a => a.EmployeeId == e.Id);
            if (hasLohn) { mitLohn.Add(new { e.Id, label }); continue; }

            var dokCount = await _context.EmployeeDokumente.CountAsync(d => d.EmployeeId == e.Id);
            if (dokCount > 0) { mitDok.Add(new { e.Id, label, dokCount }); continue; }

            loeschbar.Add((e.Id, label));
        }

        if (dryRun)
        {
            return Ok(new
            {
                dryRun = true,
                total = geprueft,
                loeschbar = loeschbar.Select(x => x.Label).ToList(),
                uebersprungenLohn = mitLohn,
                uebersprungenDokumente = mitDok,
                uebersprungenVerknuepft = mitEaw,
            });
        }

        int deleted = 0;
        var fehler = new List<string>();
        foreach (var (id, label) in loeschbar)
        {
            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                await HardDeleteEmployeeCoreAsync(id);
                await tx.CommitAsync();
                deleted++;
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                fehler.Add($"{label}: {ex.Message}");
            }
        }

        return Ok(new
        {
            dryRun = false,
            total = geprueft,
            geloescht = deleted,
            uebersprungenLohn = mitLohn,
            uebersprungenDokumente = mitDok,
            uebersprungenVerknuepft = mitEaw,
            fehler,
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
    public string?   MaidenName   { get; set; }
    public string?   ShortName    { get; set; }
    public string?   Salutation   { get; set; }
    public string?   Gender       { get; set; }
    public DateTime? DateOfBirth  { get; set; }
    public string?   LanguageCode { get; set; }
    public int?      NationalityId { get; set; }
    public string?   PhoneMobile  { get; set; }
    public string?   Phone2       { get; set; }
    public string?   Email        { get; set; }

    // Adresse
    public string?   Street      { get; set; }
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
    /// <summary>eID / SSO (Walter 14.08.2026) — null = nicht ändern, "" = löschen.</summary>
    public string?   Eid              { get; set; }
    public string?   Sso              { get; set; }

    /// <summary>Wenn true, wird QuellensteuerBefreitAb gesetzt (auch wenn null → Befreiung aufheben).</summary>
    public bool      QuellensteuerBefreitAbSet { get; set; } = false;
    public DateOnly? QuellensteuerBefreitAb    { get; set; }

    // Ein-/Austritt
    public DateTime? EntryDate   { get; set; }
    public bool      ExitDateSet { get; set; } = false;
    public DateTime? ExitDate    { get; set; }
    /// <summary>Kündigungs-Daten nur schreiben wenn true (alte Clients senden nichts → kein Wipe).</summary>
    public bool      KuendigungSet { get; set; } = false;
    public DateTime? KuendigungAusgesprochenAm { get; set; }
    public DateTime? KuendigungPer { get; set; }
    /// <summary>«AG» = durch uns, «AN» = durch Mitarbeiter, null/leer = löschen.</summary>
    public string?   KuendigungDurch { get; set; }
    /// <summary>Austrittsgrund-Code (siehe AustrittsgrundCodes), null/leer = löschen.</summary>
    public string?   Austrittsgrund { get; set; }

    // Nachtarbeit-Untersuchung gültig bis (Walter 20.06.2026)
    public bool      NightWorkExamValidUntilSet { get; set; } = false;
    public DateTime? NightWorkExamValidUntil    { get; set; }

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
    public bool?     EasyAtWorkManualOverride { get; set; }
}

/// <summary>QST-Befreiung durch Steuerbehörde (Walter 26.05.2026).</summary>
public class QstBefreiungDto
{
    public bool      Befreit    { get; set; }   // false → alle vier Felder zurücksetzen
    public int?      DokumentId { get; set; }   // FK auf employee_dokument; Pflicht wenn Befreit=true
    public DateOnly? GueltigAb  { get; set; }   // Pflicht wenn Befreit=true
    public DateOnly? GueltigBis { get; set; }   // NULL = unbefristet
}