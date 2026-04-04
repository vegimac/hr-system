using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Employment> Employments => Set<Employment>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<CompanySignatory> CompanySignatories => Set<CompanySignatory>();
    public DbSet<EducationLevel> EducationLevels => Set<EducationLevel>();
    public DbSet<EmployeeEducationHistory> EmployeeEducationHistories => Set<EmployeeEducationHistory>();
    public DbSet<PermitType> PermitTypes => Set<PermitType>();
    public DbSet<MinimumWageRuleNew> MinimumWageRulesNew => Set<MinimumWageRuleNew>();
    public DbSet<JobGroup> JobGroups => Set<JobGroup>();
    public DbSet<AppText> AppTexts => Set<AppText>();
    public DbSet<Nationality> Nationalities => Set<Nationality>();
    public DbSet<EmployeeImportSnapshot> EmployeeImportSnapshots => Set<EmployeeImportSnapshot>();
    public DbSet<ContractText> ContractTexts => Set<ContractText>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("employee");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeNumber).HasColumnName("employee_number");
            entity.Property(e => e.Salutation).HasColumnName("salutation");
            entity.Property(e => e.FirstName).HasColumnName("first_name");
            entity.Property(e => e.LastName).HasColumnName("last_name");
            entity.Property(e => e.Street).HasColumnName("street");
            entity.Property(e => e.HouseNumber).HasColumnName("house_number");
            entity.Property(e => e.ZipCode).HasColumnName("zip_code");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("date");
            entity.Property(e => e.Nationality).HasColumnName("nationality");
            entity.Property(e => e.NationalityId).HasColumnName("nationality_id");
            entity.Property(e => e.LanguageCode).HasColumnName("language_code");
            entity.Property(e => e.PhoneMobile).HasColumnName("phone_mobile");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.EntryDate).HasColumnName("entry_date").HasColumnType("date");
            entity.Property(e => e.ExitDate).HasColumnName("exit_date").HasColumnType("date");
            entity.Property(e => e.PermitTypeId).HasColumnName("permit_type_id");
            entity.Property(e => e.PermitExpiryDate).HasColumnName("permit_expiry_date").HasColumnType("date");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasOne(e => e.PermitType).WithMany().HasForeignKey(e => e.PermitTypeId);
            entity.HasOne(e => e.NationalityRef).WithMany().HasForeignKey(e => e.NationalityId);
        });

        modelBuilder.Entity<Employment>(entity =>
        {
            entity.ToTable("employment");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.EmploymentModel).HasColumnName("employment_model");
            entity.Property(e => e.SalaryType).HasColumnName("salary_type");
            entity.Property(e => e.ContractStartDate).HasColumnName("contract_start_date").HasColumnType("date");
            entity.Property(e => e.ContractEndDate).HasColumnName("contract_end_date").HasColumnType("date");
            entity.Property(e => e.JobTitle).HasColumnName("job_title");
            entity.Property(e => e.ContractType).HasColumnName("contract_type");
            entity.Property(e => e.EmploymentPercentage).HasColumnName("employment_percentage");
            entity.Property(e => e.WeeklyHours).HasColumnName("weekly_hours");
            entity.Property(e => e.GuaranteedHoursPerWeek).HasColumnName("guaranteed_hours_per_week");
            entity.Property(e => e.MonthlySalary).HasColumnName("monthly_salary");
            entity.Property(e => e.HourlyRate).HasColumnName("hourly_rate");
            entity.Property(e => e.VacationPercent).HasColumnName("vacation_percent");
            entity.Property(e => e.HolidayPercent).HasColumnName("holiday_percent");
            entity.Property(e => e.ThirteenthSalaryPercent).HasColumnName("thirteenth_salary_percent");
            entity.Property(e => e.VacationPaymentMode).HasColumnName("vacation_payment_mode");
            entity.Property(e => e.ProbationPeriodMonths).HasColumnName("probation_period_months");
            entity.Property(e => e.ProbationEndDate).HasColumnName("probation_end_date").HasColumnType("date");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasOne(e => e.Employee).WithMany(e => e.Employments).HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.CompanyProfile).WithMany().HasForeignKey(e => e.CompanyProfileId);
        });

        modelBuilder.Entity<CompanyProfile>(entity =>
        {
            entity.ToTable("company_profile");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyName).HasColumnName("company_name");
            entity.Property(e => e.RestaurantCode).HasColumnName("restaurant_code");
            entity.Property(e => e.Street).HasColumnName("street");
            entity.Property(e => e.HouseNumber).HasColumnName("house_number");
            entity.Property(e => e.ZipCode).HasColumnName("zip_code");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.NormalWeeklyHours).HasColumnName("normal_weekly_hours");
            entity.Property(e => e.DefaultVacationWeeks).HasColumnName("default_vacation_weeks");
            entity.Property(e => e.WorkLocation).HasColumnName("work_location");
            entity.Property(e => e.PayrollPeriodStartDay).HasColumnName("payroll_period_start_day");
            entity.Property(e => e.MaxPartTimeHoursPerWeek).HasColumnName("max_part_time_hours_per_week");
            entity.Property(e => e.AllowFirst3Months8PercentReduction).HasColumnName("allow_first_3_months_8_percent_reduction");
            entity.Property(e => e.HoldBackVacationPayout).HasColumnName("hold_back_vacation_payout");
            entity.Property(e => e.NoticePeriodDuringProbationDays).HasColumnName("notice_period_during_probation_days");
            entity.Property(e => e.NoticePeriodAfterProbationMonths).HasColumnName("notice_period_after_probation_months");
            entity.Property(e => e.NoticePeriodFromTenthYearMonths).HasColumnName("notice_period_from_tenth_year_months");
            entity.Property(e => e.MinimumWageUnder18Monthly).HasColumnName("minimum_wage_under_18_monthly");
            entity.Property(e => e.MinimumWageUnder18Hourly).HasColumnName("minimum_wage_under_18_hourly");
            entity.Property(e => e.SelectedContractTemplateId).HasColumnName("selected_contract_template_id");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<CompanySignatory>(entity =>
        {
            entity.ToTable("company_signatory");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.FirstName).HasColumnName("first_name");
            entity.Property(e => e.LastName).HasColumnName("last_name");
            entity.Property(e => e.FunctionTitle).HasColumnName("function_title");
            entity.Property(e => e.IsDefault).HasColumnName("is_default");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasOne(e => e.CompanyProfile).WithMany(e => e.Signatories).HasForeignKey(e => e.CompanyProfileId);
        });

        modelBuilder.Entity<EducationLevel>(entity =>
        {
            entity.ToTable("education_level");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<EmployeeEducationHistory>(entity =>
        {
            entity.ToTable("employee_education_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.EducationLevelId).HasColumnName("education_level_id");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.Note).HasColumnName("note");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.EducationLevel).WithMany().HasForeignKey(e => e.EducationLevelId);
        });

        modelBuilder.Entity<PermitType>(entity =>
        {
            entity.ToTable("permit_type");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.PersonGroup).HasColumnName("person_group");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<MinimumWageRuleNew>(entity =>
        {
            entity.ToTable("minimum_wage_rule_new");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.JobGroupCode).HasColumnName("job_group_code");
            entity.Property(e => e.EmploymentModelCode).HasColumnName("employment_model_code");
            entity.Property(e => e.EducationLevelId).HasColumnName("education_level_id");
            entity.Property(e => e.SalaryType).HasColumnName("salary_type");
            entity.Property(e => e.Amount).HasColumnName("amount");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasOne(e => e.EducationLevel).WithMany().HasForeignKey(e => e.EducationLevelId);
        });

        modelBuilder.Entity<JobGroup>(entity =>
        {
            entity.ToTable("job_group");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<AppText>(entity =>
        {
            entity.ToTable("app_text");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Module).HasColumnName("module");
            entity.Property(e => e.TextKey).HasColumnName("text_key");
            entity.Property(e => e.LanguageCode).HasColumnName("language_code");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<Nationality>(entity =>
        {
            entity.ToTable("nationality");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
        });

        modelBuilder.Entity<EmployeeImportSnapshot>(entity =>
        {
            entity.ToTable("employee_import_snapshot");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.JobGroupCode).HasColumnName("job_group_code");
            entity.Property(e => e.EmploymentModel).HasColumnName("employment_model");
            entity.Property(e => e.ContractType).HasColumnName("contract_type");
            entity.Property(e => e.HourlyRate).HasColumnName("hourly_rate");
            entity.Property(e => e.MonthlySalary).HasColumnName("monthly_salary");
            entity.Property(e => e.WeeklyHours).HasColumnName("weekly_hours");
            entity.Property(e => e.JobTitle).HasColumnName("job_title");
            entity.Property(e => e.NationalityCode).HasColumnName("nationality_code");
            entity.Property(e => e.ImportedAt).HasColumnName("imported_at");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });

        // ── NEU: ContractText ──────────────────────────────────────────────
        modelBuilder.Entity<ContractText>(entity =>
        {
            entity.ToTable("contract_text");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedOnAdd();
            entity.Property(e => e.TextKey).HasColumnName("text_key").HasMaxLength(20).IsRequired();
            entity.Property(e => e.ContractTypes).HasColumnName("contract_types").HasMaxLength(50).HasDefaultValue("ALL");
            entity.Property(e => e.LanguageCode).HasColumnName("language_code").HasMaxLength(5).HasDefaultValue("de");
            entity.Property(e => e.Content).HasColumnName("content").IsRequired();
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.HasIndex(e => new { e.TextKey, e.LanguageCode }).HasDatabaseName("IX_contract_text_key_lang");
        });
    }
}
