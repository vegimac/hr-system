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
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserBranchAccess> UserBranchAccesses => Set<UserBranchAccess>();
    public DbSet<EmployeeFamilyMember> EmployeeFamilyMembers => Set<EmployeeFamilyMember>();
    public DbSet<EmployeeTimeEntry> EmployeeTimeEntries => Set<EmployeeTimeEntry>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<DeductionRule> DeductionRules => Set<DeductionRule>();
    public DbSet<PayrollSaldo> PayrollSaldos => Set<PayrollSaldo>();
    public DbSet<EmployeeQuellensteuer> EmployeeQuellensteuer => Set<EmployeeQuellensteuer>();
    public DbSet<LohnZulagTyp> LohnZulagTypen => Set<LohnZulagTyp>();
    public DbSet<LohnZulage> LohnZulagen => Set<LohnZulage>();
    public DbSet<AbsenzTyp> AbsenzTypen => Set<AbsenzTyp>();
    public DbSet<EmployeeArbeitslosigkeit> EmployeeArbeitslosigkeiten => Set<EmployeeArbeitslosigkeit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("employee");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeNumber).HasColumnName("employee_number");
            entity.Property(e => e.Salutation).HasColumnName("salutation");
            entity.Property(e => e.Gender).HasColumnName("gender");  // NEU
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
            entity.Property(e => e.QuellensteuerBefreitAb).HasColumnName("quellensteuer_befreit_ab").HasColumnType("date");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
           entity.Property(e => e.SocialSecurityNumber).HasColumnName("social_security_number").HasMaxLength(20);
            entity.Property(e => e.Zivilstand).HasColumnName("marital_status").HasMaxLength(40);
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
            entity.Property(e => e.MonthlySalaryFte).HasColumnName("monthly_salary_fte");
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
            entity.Property(e => e.DefaultVacationPercent5Weeks).HasColumnName("default_vacation_percent_5weeks");
            entity.Property(e => e.DefaultVacationPercent6Weeks).HasColumnName("default_vacation_percent_6weeks");
            entity.Property(e => e.DefaultHolidayPercent).HasColumnName("default_holiday_percent");    
            entity.Property(e => e.NightStartTime).HasColumnName("night_start_time").HasMaxLength(5);
            entity.Property(e => e.NightEndTime).HasColumnName("night_end_time").HasMaxLength(5);
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.BurNummer).HasColumnName("bur_nummer").HasMaxLength(20);
            entity.Property(e => e.UidNummer).HasColumnName("uid_nummer").HasMaxLength(20);
            entity.Property(e => e.BranchenCode).HasColumnName("branchen_code").HasMaxLength(10);
            entity.Property(e => e.AhvKasse).HasColumnName("ahv_kasse").HasMaxLength(100);
            entity.Property(e => e.BvgVersicherer).HasColumnName("bvg_versicherer").HasMaxLength(100);
            entity.Property(e => e.GavName).HasColumnName("gav_name").HasMaxLength(100);
            entity.Property(e => e.IstGav).HasColumnName("ist_gav");
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
            entity.Property(e => e.MonthlySalaryFte).HasColumnName("monthly_salary_fte");
            entity.Property(e => e.MonthlySalary).HasColumnName("monthly_salary");
            entity.Property(e => e.WeeklyHours).HasColumnName("weekly_hours");
            entity.Property(e => e.JobTitle).HasColumnName("job_title");
            entity.Property(e => e.NationalityCode).HasColumnName("nationality_code");
            entity.Property(e => e.Gender).HasColumnName("gender");  // NEU
            entity.Property(e => e.ImportedAt).HasColumnName("imported_at");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });

        // ── AppUser ────────────────────────────────────────────────────────
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("app_user");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Username).HasColumnName("username");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.PasswordHash).HasColumnName("password_hash");
            entity.Property(e => e.Role).HasColumnName("role");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        // ── UserBranchAccess ───────────────────────────────────────────────
        modelBuilder.Entity<UserBranchAccess>(entity =>
        {
            entity.ToTable("user_branch_access");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.HasOne(e => e.User).WithMany(e => e.BranchAccess).HasForeignKey(e => e.UserId);
            entity.HasOne(e => e.CompanyProfile).WithMany().HasForeignKey(e => e.CompanyProfileId);
        });

        // ── EmployeeFamilyMember ───────────────────────────────────────────
        modelBuilder.Entity<EmployeeFamilyMember>(entity =>
        {
            entity.ToTable("employee_family_member");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.MemberType).HasColumnName("member_type");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.FamilyStatus).HasColumnName("family_status");
            entity.Property(e => e.LastName).HasColumnName("last_name");
            entity.Property(e => e.MaidenName).HasColumnName("maiden_name");
            entity.Property(e => e.FirstName).HasColumnName("first_name");
            entity.Property(e => e.SocialSecurityNumber).HasColumnName("social_security_number");
            entity.Property(e => e.LivesInSwitzerland).HasColumnName("lives_in_switzerland");
            entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("date");
            entity.Property(e => e.DateOfDeath).HasColumnName("date_of_death").HasColumnType("date");
            entity.Property(e => e.Allowance1Until).HasColumnName("allowance_1_until").HasColumnType("date");
            entity.Property(e => e.Allowance2Until).HasColumnName("allowance_2_until").HasColumnType("date");
            entity.Property(e => e.Allowance3Until).HasColumnName("allowance_3_until").HasColumnType("date");
            entity.Property(e => e.AlternativeAddressId).HasColumnName("alternative_address_id");
            entity.Property(e => e.QstDeductibleFrom).HasColumnName("qst_deductible_from").HasColumnType("date");
            entity.Property(e => e.QstDeductibleUntil).HasColumnName("qst_deductible_until").HasColumnType("date");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });

        // ── EmployeeTimeEntry ──────────────────────────────────────────────
        modelBuilder.Entity<EmployeeTimeEntry>(entity =>
        {
            entity.ToTable("employee_time_entry");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.EntryDate).HasColumnName("entry_date").HasColumnType("date");
            entity.Property(e => e.TimeIn).HasColumnName("time_in");
            entity.Property(e => e.TimeOut).HasColumnName("time_out");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.DurationHours).HasColumnName("duration_hours").HasColumnType("numeric(6,2)");
            entity.Property(e => e.NightHours).HasColumnName("night_hours").HasColumnType("numeric(6,2)");
            entity.Property(e => e.TotalHours).HasColumnName("total_hours").HasColumnType("numeric(6,2)");
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(50).HasDefaultValue("manual");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.OriginalTimeIn).HasColumnName("original_time_in");
            entity.Property(e => e.OriginalTimeOut).HasColumnName("original_time_out");
            entity.Property(e => e.EditedBy).HasColumnName("edited_by").HasMaxLength(100);
            entity.Property(e => e.EditedAt).HasColumnName("edited_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });

        // ── Absence ────────────────────────────────────────────────────────
        modelBuilder.Entity<Absence>(entity =>
        {
            entity.ToTable("absence");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.AbsenceType).HasColumnName("absence_type").HasMaxLength(20);
            entity.Property(e => e.DateFrom).HasColumnName("date_from").HasColumnType("date");
            entity.Property(e => e.DateTo).HasColumnName("date_to").HasColumnType("date");
            entity.Property(e => e.WorkedDays).HasColumnName("worked_days");
            entity.Property(e => e.HoursCredited).HasColumnName("hours_credited").HasColumnType("numeric(8,2)");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
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

        // ── DeductionRule ──────────────────────────────────────────────────
        modelBuilder.Entity<DeductionRule>(entity =>
        {
            entity.ToTable("deduction_rule");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.CategoryCode).HasColumnName("category_code").HasMaxLength(20).HasDefaultValue("");
            entity.Property(e => e.CategoryName).HasColumnName("category_name").HasMaxLength(100).HasDefaultValue("");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100);
            entity.Property(e => e.Type).HasColumnName("type").HasMaxLength(20).HasDefaultValue("percent");
            entity.Property(e => e.Rate).HasColumnName("rate").HasColumnType("numeric(8,4)");
            entity.Property(e => e.BasisType).HasColumnName("basis_type").HasMaxLength(20).HasDefaultValue("gross");
            entity.Property(e => e.CoordinationDeduction).HasColumnName("coordination_deduction").HasColumnType("numeric(10,2)");
            entity.Property(e => e.MinAge).HasColumnName("min_age");
            entity.Property(e => e.MaxAge).HasColumnName("max_age");
            entity.Property(e => e.FreibetragMonthly).HasColumnName("freibetrag_monthly").HasColumnType("numeric(10,2)");
            entity.Property(e => e.OnlyQuellensteuer).HasColumnName("only_quellensteuer").HasDefaultValue(false);
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.HasOne(e => e.CompanyProfile).WithMany().HasForeignKey(e => e.CompanyProfileId);
        });

        // ── PayrollSaldo ───────────────────────────────────────────────────
        modelBuilder.Entity<PayrollSaldo>(entity =>
        {
            entity.ToTable("payroll_saldo");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.PeriodYear).HasColumnName("period_year");
            entity.Property(e => e.PeriodMonth).HasColumnName("period_month");
            entity.Property(e => e.HourSaldo).HasColumnName("hour_saldo").HasColumnType("numeric(8,2)");
            entity.Property(e => e.NachtSaldo).HasColumnName("nacht_saldo").HasColumnType("numeric(8,2)");
            entity.Property(e => e.NightHoursWorked).HasColumnName("night_hours_worked").HasColumnType("numeric(8,2)");
            entity.Property(e => e.FerienGeldSaldo).HasColumnName("ferien_geld_saldo").HasColumnType("numeric(10,2)");
            entity.Property(e => e.FerienTageSaldo).HasColumnName("ferien_tage_saldo").HasColumnType("numeric(8,4)");
            entity.Property(e => e.ThirteenthMonthMonthly).HasColumnName("thirteenth_month_monthly").HasColumnType("numeric(10,2)");
            entity.Property(e => e.ThirteenthMonthAccumulated).HasColumnName("thirteenth_month_accumulated").HasColumnType("numeric(10,2)");
            entity.Property(e => e.GrossAmount).HasColumnName("gross_amount").HasColumnType("numeric(10,2)");
            entity.Property(e => e.NetAmount).HasColumnName("net_amount").HasColumnType("numeric(10,2)");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("draft");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasIndex(e => new { e.EmployeeId, e.PeriodYear, e.PeriodMonth }).HasDatabaseName("IX_payroll_saldo_emp_period");
        });

        // ── AbsenzTyp ──────────────────────────────────────────────────────
        modelBuilder.Entity<AbsenzTyp>(entity =>
        {
            entity.ToTable("absenz_typ");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(20);
            entity.Property(e => e.Bezeichnung).HasColumnName("bezeichnung").HasMaxLength(100);
            entity.Property(e => e.Zeitgutschrift).HasColumnName("zeitgutschrift").HasDefaultValue(true);
            entity.Property(e => e.GutschriftModus).HasColumnName("gutschrift_modus").HasMaxLength(5);
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.Aktiv).HasColumnName("aktiv").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => e.Code).HasDatabaseName("IX_absenz_typ_code").IsUnique();
        });

        // ── LohnZulagTyp ───────────────────────────────────────────────────
        modelBuilder.Entity<LohnZulagTyp>(entity =>
        {
            entity.ToTable("lohn_zulag_typ");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Bezeichnung).HasColumnName("bezeichnung").HasMaxLength(100);
            entity.Property(e => e.Typ).HasColumnName("typ").HasMaxLength(10).HasDefaultValue("ZULAGE");
            entity.Property(e => e.SvPflichtig).HasColumnName("sv_pflichtig").HasDefaultValue(false);
            entity.Property(e => e.QstPflichtig).HasColumnName("qst_pflichtig").HasDefaultValue(false);
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.Aktiv).HasColumnName("aktiv").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        // ── LohnZulage ─────────────────────────────────────────────────────
        modelBuilder.Entity<LohnZulage>(entity =>
        {
            entity.ToTable("lohn_zulage");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.Periode).HasColumnName("periode").HasMaxLength(7);
            entity.Property(e => e.TypId).HasColumnName("typ_id");
            entity.Property(e => e.Betrag).HasColumnName("betrag").HasColumnType("numeric(10,2)");
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.Typ).WithMany().HasForeignKey(e => e.TypId);
            entity.HasIndex(e => new { e.EmployeeId, e.Periode }).HasDatabaseName("IX_lohn_zulage_emp_periode");
        });

        modelBuilder.Entity<EmployeeQuellensteuer>(entity =>
        {
            entity.ToTable("employee_quellensteuer");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.Steuerkanton).HasColumnName("steuerkanton").HasMaxLength(10);
            entity.Property(e => e.SteuerkantonName).HasColumnName("steuerkanton_name").HasMaxLength(100);
            entity.Property(e => e.QstGemeinde).HasColumnName("qst_gemeinde").HasMaxLength(100);
            entity.Property(e => e.QstGemeindeBfsNr).HasColumnName("qst_gemeinde_bfs_nr");
            entity.Property(e => e.TarifvorschlagQst).HasColumnName("tarifvorschlag_qst").HasDefaultValue(true);
            entity.Property(e => e.TarifCode).HasColumnName("tarif_code").HasMaxLength(10);
            entity.Property(e => e.TarifBezeichnung).HasColumnName("tarif_bezeichnung").HasMaxLength(200);
            entity.Property(e => e.AnzahlKinder).HasColumnName("anzahl_kinder").HasDefaultValue(0);
            entity.Property(e => e.Kirchensteuer).HasColumnName("kirchensteuer").HasDefaultValue(false);
            entity.Property(e => e.QstCode).HasColumnName("qst_code").HasMaxLength(10);
            entity.Property(e => e.SpezielBewilligt).HasColumnName("speziell_bewilligt").HasDefaultValue(false);
            entity.Property(e => e.Kategorie).HasColumnName("kategorie").HasMaxLength(100);
            entity.Property(e => e.Prozentsatz).HasColumnName("prozentsatz").HasColumnType("numeric(5,2)");
            entity.Property(e => e.MindestlohnSatzbestimmung).HasColumnName("mindestlohn_satzbestimmung").HasColumnType("numeric(10,2)");
            entity.Property(e => e.PartnerEmployeeId).HasColumnName("partner_employee_id");
            entity.Property(e => e.PartnerEinkommenVon).HasColumnName("partner_einkommen_von").HasColumnType("date");
            entity.Property(e => e.PartnerEinkommenBis).HasColumnName("partner_einkommen_bis").HasColumnType("date");
            entity.Property(e => e.ArbeitsortKanton).HasColumnName("arbeitsort_kanton").HasMaxLength(10);
            entity.Property(e => e.WeitereBeschaftigungen).HasColumnName("weitere_beschaeftigungen").HasDefaultValue(false);
            entity.Property(e => e.GesamtpensumWeitereAg).HasColumnName("gesamtpensum_weitere_ag").HasColumnType("numeric(5,2)");
            entity.Property(e => e.GesamteinkommenWeitereAg).HasColumnName("gesamteinkommen_weitere_ag").HasColumnType("numeric(10,2)");
            entity.Property(e => e.Halbfamilie).HasColumnName("halbfamilie").HasMaxLength(100);
            entity.Property(e => e.WohnsitzAusland).HasColumnName("wohnsitz_ausland").HasMaxLength(100);
            entity.Property(e => e.Wohnsitzstaat).HasColumnName("wohnsitzstaat").HasMaxLength(10);
            entity.Property(e => e.AdresseAusland).HasColumnName("adresse_ausland").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasIndex(e => new { e.EmployeeId, e.ValidFrom }).HasDatabaseName("IX_emp_qst_emp_valid");
        });

        // ── EmployeeArbeitslosigkeit ───────────────────────────────────────
        modelBuilder.Entity<EmployeeArbeitslosigkeit>(entity =>
        {
            entity.ToTable("employee_arbeitslosigkeit");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.AngemeldetSeit).HasColumnName("angemeldet_seit").HasColumnType("date");
            entity.Property(e => e.AbgemeldetAm).HasColumnName("abgemeldet_am").HasColumnType("date");
            entity.Property(e => e.RavStelle).HasColumnName("rav_stelle").HasMaxLength(100);
            entity.Property(e => e.RavKundennummer).HasColumnName("rav_kundennummer").HasMaxLength(50);
            entity.Property(e => e.Arbeitslosenkasse).HasColumnName("arbeitslosenkasse").HasMaxLength(100);
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });
    }
}
