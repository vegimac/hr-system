using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Baut aus den DB-Daten eines Employments das Arbeitsvertrag-PDF (Walter 07.07.2026).
/// Ausgelagert aus <see cref="Controllers.ContractsController.DownloadContractPdf"/>,
/// damit sowohl der authentifizierte Download-Endpoint als auch der öffentliche
/// Token-Link (ContractShareController) EXAKT dasselbe PDF erzeugen. Die frühere
/// Inline-Logik im Controller ruft jetzt nur noch diese Methode auf — Verhalten
/// und PDF-Optik unverändert.
/// </summary>
public static class ContractPdfBuilder
{
    private static async Task<string?> GetJobTitleDisplayName(AppDbContext db, string? code, string lang)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var key = $"{code}.NAME";
        return await db.AppTexts
            .Where(t => t.Module == "JOB_GROUP" && t.TextKey == key && t.LanguageCode == lang && t.IsActive)
            .Select(t => t.Content)
            .FirstOrDefaultAsync();
    }

    private static string GetSalaryType(string? m) =>
        ((m ?? "").ToUpperInvariant() is "FIX" or "FIX-M") ? "monthly" : "hourly";

    /// <summary>
    /// Walter-Vorgabe 06.06.2026: Ferien-% für den Arbeitsvertrag-PDF.
    /// Nimmt den Filial-5-Wochen-Default. Ist der MA am Vertragsbeginn bereits
    /// ≥ company.VacationSixWeeksFromAge, wird auf den 6-Wochen-Default hochgesetzt.
    /// </summary>
    private static decimal? ResolveVacationPctForContract(
        Models.Employee employee, Models.CompanyProfile company, DateTime contractStartDate)
    {
        var fiveWeeks = company.DefaultVacationPercent5Weeks;
        var sixWeeks  = company.DefaultVacationPercent6Weeks;
        if (employee.DateOfBirth is null) return fiveWeeks;

        var dob = DateOnly.FromDateTime(employee.DateOfBirth.Value);
        var start = DateOnly.FromDateTime(contractStartDate);
        var schwelle = dob.AddYears(company.VacationSixWeeksFromAge);
        return schwelle <= start ? sixWeeks : fiveWeeks;
    }

    /// <summary>
    /// Lädt Employment + Employee + CompanyProfile + Unterzeichner und erzeugt das
    /// Vertrags-PDF. Gibt null zurück, wenn das Employment nicht existiert oder
    /// keine (gültige) Filiale hat.
    /// </summary>
    public static async Task<byte[]?> BuildAsync(AppDbContext db, int employmentId)
    {
        var employment = await db.Employments
            .Include(e => e.Employee)
            .FirstOrDefaultAsync(e => e.Id == employmentId);
        if (employment == null) return null;
        if (employment.CompanyProfileId == null) return null;

        var company = await db.CompanyProfiles
            .FirstOrDefaultAsync(c => c.Id == employment.CompanyProfileId.Value);
        if (company == null) return null;

        var employee = employment.Employee;
        if (employee == null) return null;

        var signatory = await db.UserBranchAccesses
            .Include(s => s.User)
            .Where(s => s.CompanyProfileId == employment.CompanyProfileId.Value
                     && s.IsDefault == true)
            .FirstOrDefaultAsync();

        var salaryType = employment.SalaryType ?? GetSalaryType(employment.EmploymentModel);
        var jobTitleDisplay = await GetJobTitleDisplayName(db, employment.JobTitle, "de") ?? employment.JobTitle ?? "";

        // ── Verfügbarkeit für Seite 3 (Walter-Vorgabe 09.07.2026) ────────────
        // Version gültig am Vertragsbeginn; sonst die heute gültige; sonst die
        // neueste. Keine Verfügbarkeit erfasst → leeres Formular wie bisher.
        ContractAvailability? availability = null;
        var avList = await db.EmployeeAvailabilities.AsNoTracking()
            .Include(a => a.Slots)
            .Where(a => a.EmployeeId == employee.Id)
            .ToListAsync();
        if (avList.Count > 0)
        {
            Models.EmployeeAvailability? PickAt(DateOnly d) => avList
                .Where(a => a.ValidFrom <= d && (!a.ValidTo.HasValue || a.ValidTo.Value >= d))
                .OrderByDescending(a => a.ValidFrom).ThenByDescending(a => a.Id)
                .FirstOrDefault();
            var pick = PickAt(DateOnly.FromDateTime(employment.ContractStartDate))
                       ?? PickAt(DateOnly.FromDateTime(DateTime.Today))
                       ?? avList.OrderByDescending(a => a.ValidFrom).ThenByDescending(a => a.Id).First();

            var rows = pick.Slots
                .OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
                .Select(s => new ContractAvailabilityRow(
                    s.Von == null && s.Bis == null ? "ganztags"
                        : s.Von != null && s.Bis != null ? $"{s.Von:HH\\:mm} – {s.Bis:HH\\:mm}"
                        : s.Von != null ? $"ab {s.Von:HH\\:mm}" : $"bis {s.Bis:HH\\:mm}",
                    new[] { s.Mon, s.Tue, s.Wed, s.Thu, s.Fri, s.Sat, s.Sun }))
                .ToList();
            availability = new ContractAvailability(
                Unrestricted: pick.Type == "unrestricted",
                ValidFrom: pick.ValidFrom,
                ValidTo: pick.ValidTo,
                Rows: rows);
        }

        var addressParts = new[] { company.Street, company.HouseNumber }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        var streetAddress = string.Join(" ", addressParts);
        var companyAddress = $"{streetAddress}, {company.ZipCode} {company.City}".Trim().TrimStart(',').Trim();

        var input = new ContractPdfInput(
            CompanyName:             company.FullDisplayName,
            CompanyAddress:          companyAddress,
            WorkLocation:            company.WorkLocation ?? "",
            SignatoryName:           signatory != null
                                         ? $"{signatory.User.FirstName} {signatory.User.LastName}".Trim()
                                         : "",
            SignatoryTitle:          signatory?.FunctionTitle ?? "",
            SignatureCity:           company.City ?? "",
            ContractDate:            DateTime.Today,
            DefaultVacationWeeks:    company.DefaultVacationWeeks,
            Salutation:              employee.Salutation ?? "",
            FirstName:               employee.FirstName,
            LastName:                employee.LastName,
            DateOfBirth:             employee.DateOfBirth,
            EmployeeStreet:          employee.Street,
            EmployeeZipCity:         !string.IsNullOrWhiteSpace(employee.ZipCode) || !string.IsNullOrWhiteSpace(employee.City)
                                         ? $"{employee.ZipCode} {employee.City}".Trim() : null,
            EmploymentModel:         employment.EmploymentModel,
            SalaryType:              salaryType,
            JobTitle:                jobTitleDisplay,
            ContractType:            employment.ContractType,
            ContractStartDate:       employment.ContractStartDate,
            ContractEndDate:         employment.ContractEndDate,
            ProbationMonths:         employment.ProbationPeriodMonths,
            MonthlySalary:           employment.MonthlySalary,
            MonthlySalaryFte:        employment.MonthlySalaryFte,
            HourlyRate:              employment.HourlyRate,
            EmploymentPercentage:    employment.EmploymentPercentage,
            WeeklyHours:             employment.EmploymentModel == "MTP"
                                         ? (decimal?)company.NormalWeeklyHours : employment.WeeklyHours,
            GuaranteedHoursPerWeek:  employment.GuaranteedHoursPerWeek,
            VacationPercent:         ResolveVacationPctForContract(employee, company, employment.ContractStartDate),
            HolidayPercent:          company.DefaultHolidayPercent,
            ThirteenthSalaryPercent: company.DefaultThirteenthSalaryPercent,
            Gender:                  employee.Gender,
            Availability:            availability
        );

        return new ContractPdfService().Generate(input);
    }
}
