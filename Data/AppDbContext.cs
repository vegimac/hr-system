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
    public DbSet<PayrollSaldo> PayrollSaldos => Set<PayrollSaldo>();
    public DbSet<KrankheitKarenzSaldo> KrankheitKarenzSaldos => Set<KrankheitKarenzSaldo>();
    public DbSet<EmployeeLohnDurchschnitt> EmployeeLohnDurchschnitte => Set<EmployeeLohnDurchschnitt>();
    public DbSet<EmployeeQuellensteuer> EmployeeQuellensteuer => Set<EmployeeQuellensteuer>();
    public DbSet<LohnZulagTyp> LohnZulagTypen => Set<LohnZulagTyp>();
    public DbSet<LohnZulage> LohnZulagen => Set<LohnZulage>();
    public DbSet<EmployeeRecurringWage> EmployeeRecurringWages => Set<EmployeeRecurringWage>();
    public DbSet<EmploymentModelComponent> EmploymentModelComponents => Set<EmploymentModelComponent>();
    public DbSet<SwissLocation> SwissLocations => Set<SwissLocation>();
    public DbSet<Behoerde> Behoerden => Set<Behoerde>();
    public DbSet<EmployeeLohnAssignment> EmployeeLohnAssignments => Set<EmployeeLohnAssignment>();
    public DbSet<AbsenzTyp> AbsenzTypen => Set<AbsenzTyp>();
    public DbSet<EmployeeArbeitslosigkeit> EmployeeArbeitslosigkeiten => Set<EmployeeArbeitslosigkeit>();
    public DbSet<SocialInsuranceRate> SocialInsuranceRates => Set<SocialInsuranceRate>();
    public DbSet<Lohnposition> Lohnpositionen => Set<Lohnposition>();
    public DbSet<VertragstypLohnposition> VertragstypLohnpositionen => Set<VertragstypLohnposition>();
    public DbSet<PayrollPeriodeConfig>  PayrollPeriodeConfigs  => Set<PayrollPeriodeConfig>();
    public DbSet<PayrollPeriode>        PayrollPerioden        => Set<PayrollPeriode>();
    public DbSet<PayrollSnapshot>       PayrollSnapshots       => Set<PayrollSnapshot>();
    public DbSet<PayrollLohnAbtretungEntry> PayrollLohnAbtretungEntries => Set<PayrollLohnAbtretungEntry>();
    public DbSet<BankMaster>                BankMasters                 => Set<BankMaster>();
    public DbSet<EmployeeBankAccount>       EmployeeBankAccounts        => Set<EmployeeBankAccount>();
    public DbSet<DokumentKategorie>         DokumentKategorien          => Set<DokumentKategorie>();
    public DbSet<DokumentTyp>               DokumentTypen               => Set<DokumentTyp>();
    public DbSet<EmployeeDokument>          EmployeeDokumente           => Set<EmployeeDokument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>(entity =>
        {
            entity.ToTable("employee");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeNumber).HasColumnName("employee_number");
            entity.Property(e => e.Salutation).HasColumnName("salutation");
            entity.Property(e => e.Gender).HasColumnName("gender");
            entity.Property(e => e.FirstName).HasColumnName("first_name");
            entity.Property(e => e.LastName).HasColumnName("last_name");
            entity.Property(e => e.Street).HasColumnName("street");
            entity.Property(e => e.HouseNumber).HasColumnName("house_number");
            entity.Property(e => e.ZipCode).HasColumnName("zip_code");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.CantonCode).HasColumnName("canton_code").HasMaxLength(2);
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
            entity.Property(e => e.MaritalStatus).HasColumnName("marital_status").HasMaxLength(40);
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
            entity.Property(e => e.ThirteenthMonthPayoutsPerYear).HasColumnName("thirteenth_month_payouts_per_year").HasDefaultValue(12);
            entity.Property(e => e.AutoFerienGeldAuszahlungDezember).HasColumnName("auto_ferien_geld_auszahlung_dezember").HasDefaultValue(true);
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.BurNummer).HasColumnName("bur_nummer").HasMaxLength(20);
            entity.Property(e => e.UidNummer).HasColumnName("uid_nummer").HasMaxLength(20);
            entity.Property(e => e.BranchenCode).HasColumnName("branchen_code").HasMaxLength(10);
            entity.Property(e => e.AhvKasse).HasColumnName("ahv_kasse").HasMaxLength(100);
            entity.Property(e => e.BvgVersicherer).HasColumnName("bvg_versicherer").HasMaxLength(100);
            entity.Property(e => e.GavName).HasColumnName("gav_name").HasMaxLength(100);
            entity.Property(e => e.IstGav).HasColumnName("ist_gav");
            entity.Property(e => e.KarenzjahrBasis).HasColumnName("karenzjahr_basis").HasMaxLength(20).HasDefaultValue("ARBEITSJAHR");
            entity.Property(e => e.KarenzTageMax).HasColumnName("karenz_tage_max").HasColumnType("numeric(5,2)").HasDefaultValue(14m);
            entity.Property(e => e.KarenzTageMaxUnfall).HasColumnName("karenz_tage_max_unfall").HasColumnType("numeric(5,2)").HasDefaultValue(2m);
            entity.Property(e => e.BvgWartefristMonate).HasColumnName("bvg_wartefrist_monate").HasDefaultValue(3);
            entity.Property(e => e.LgavAktiv).HasColumnName("lgav_aktiv").HasDefaultValue(true);
            entity.Property(e => e.LgavTriggerMonat).HasColumnName("lgav_trigger_monat").HasDefaultValue(1);
            entity.Property(e => e.LgavBeitragVoll).HasColumnName("lgav_beitrag_voll").HasColumnType("numeric(8,2)").HasDefaultValue(99m);
            entity.Property(e => e.LgavBeitragReduziert).HasColumnName("lgav_beitrag_reduziert").HasColumnType("numeric(8,2)").HasDefaultValue(49.5m);
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
            entity.Property(e => e.IsKader).HasColumnName("is_kader");
            entity.Property(e => e.MirusFunktionAliases).HasColumnName("mirus_funktion_aliases");
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
            entity.Property(e => e.EmploymentPercentage).HasColumnName("employment_percentage").HasColumnType("numeric(5,2)");
            entity.Property(e => e.ContractEndDate).HasColumnName("contract_end_date").HasColumnType("date");
            entity.Property(e => e.JobTitle).HasColumnName("job_title");
            entity.Property(e => e.NationalityCode).HasColumnName("nationality_code");
            entity.Property(e => e.Gender).HasColumnName("gender");
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
            entity.Property(e => e.FirstName).HasColumnName("first_name").HasMaxLength(100);
            entity.Property(e => e.LastName).HasColumnName("last_name").HasMaxLength(100);
            entity.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(50);
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
            entity.Property(e => e.Role).HasColumnName("role").HasMaxLength(50);
            entity.Property(e => e.FunctionTitle).HasColumnName("function_title").HasMaxLength(100);
            entity.Property(e => e.IsDefault).HasColumnName("is_default");
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
            // Stempelzeiten als Lokalzeit (timestamp ohne TZ) — keine UTC-Konvertierung
            entity.Property(e => e.TimeIn).HasColumnName("time_in").HasColumnType("timestamp without time zone");
            entity.Property(e => e.TimeOut).HasColumnName("time_out").HasColumnType("timestamp without time zone");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.DurationHours).HasColumnName("duration_hours").HasColumnType("numeric(6,2)");
            entity.Property(e => e.NightHours).HasColumnName("night_hours").HasColumnType("numeric(6,2)");
            entity.Property(e => e.TotalHours).HasColumnName("total_hours").HasColumnType("numeric(6,2)");
            entity.Property(e => e.Source).HasColumnName("source").HasMaxLength(50).HasDefaultValue("manual");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.OriginalTimeIn).HasColumnName("original_time_in").HasColumnType("timestamp without time zone");
            entity.Property(e => e.OriginalTimeOut).HasColumnName("original_time_out").HasColumnType("timestamp without time zone");
            entity.Property(e => e.OriginalComment).HasColumnName("original_comment");
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
            entity.Property(e => e.Prozent).HasColumnName("prozent").HasColumnType("numeric(5,2)").HasDefaultValue(100m);
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });

        // ── ContractText ───────────────────────────────────────────────────
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

        // ── KrankheitKarenzSaldo ──────────────────────────────────────────────
        modelBuilder.Entity<KrankheitKarenzSaldo>(entity =>
        {
            entity.ToTable("krankheit_karenz_saldo");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.ArbeitsjährVon).HasColumnName("arbeitsjahr_von").HasColumnType("date");
            entity.Property(e => e.ArbeitsjährBis).HasColumnName("arbeitsjahr_bis").HasColumnType("date");
            entity.Property(e => e.KarenztageUsed).HasColumnName("karenztage_used").HasColumnType("numeric(5,2)");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasIndex(e => new { e.EmployeeId, e.ArbeitsjährVon }).IsUnique();
        });

        // ── EmployeeLohnDurchschnitt ──────────────────────────────────────────
        modelBuilder.Entity<EmployeeLohnDurchschnitt>(entity =>
        {
            entity.ToTable("employee_lohn_durchschnitt");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.BerechnetPerYear).HasColumnName("berechnet_per_year");
            entity.Property(e => e.BerechnetPerMonth).HasColumnName("berechnet_per_month");
            entity.Property(e => e.MonateBasis).HasColumnName("monate_basis");
            entity.Property(e => e.DurchschnittBrutto).HasColumnName("durchschnitt_brutto").HasColumnType("numeric(10,2)");
            entity.Property(e => e.DurchschnittTaglohn).HasColumnName("durchschnitt_taglohn").HasColumnType("numeric(10,2)");
            entity.Property(e => e.DetailJson).HasColumnName("detail_json");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasIndex(e => new { e.EmployeeId, e.CompanyProfileId, e.BerechnetPerYear, e.BerechnetPerMonth }).IsUnique();
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
            entity.Property(e => e.FeiertagTageSaldo).HasColumnName("feiertag_tage_saldo").HasColumnType("numeric(8,4)").HasDefaultValue(0m);
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
            entity.Property(e => e.UtpAuszahlung).HasColumnName("utp_auszahlung").HasDefaultValue(false);
            entity.Property(e => e.ReduziertSaldo).HasColumnName("reduziert_saldo").HasMaxLength(20);
            entity.Property(e => e.BasisStunden).HasColumnName("basis_stunden").HasMaxLength(10).HasDefaultValue("BETRIEB");
            entity.Property(e => e.LohnpositionAuszahlungCode).HasColumnName("lohnposition_auszahlung_code").HasMaxLength(20);
            entity.Property(e => e.LohnpositionKuerzungCode).HasColumnName("lohnposition_kuerzung_code").HasMaxLength(20);
            entity.Property(e => e.Pattern).HasColumnName("pattern").HasMaxLength(20).HasDefaultValue("KEIN");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.Aktiv).HasColumnName("aktiv").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => e.Code).HasDatabaseName("IX_absenz_typ_code").IsUnique();
        });

        // ── DokumentKategorie ────────────────────────────────────────────────
        modelBuilder.Entity<DokumentKategorie>(entity =>
        {
            entity.ToTable("dokument_kategorie");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.Aktiv).HasColumnName("aktiv").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        // ── DokumentTyp ──────────────────────────────────────────────────────
        modelBuilder.Entity<DokumentTyp>(entity =>
        {
            entity.ToTable("dokument_typ");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.KategorieId).HasColumnName("kategorie_id");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.Aktiv).HasColumnName("aktiv").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        // ── EmployeeDokument ─────────────────────────────────────────────────
        modelBuilder.Entity<EmployeeDokument>(entity =>
        {
            entity.ToTable("employee_dokument");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.DokumentTypId).HasColumnName("dokument_typ_id");
            entity.Property(e => e.BranchCode).HasColumnName("branch_code");
            entity.Property(e => e.FilenameOriginal).HasColumnName("filename_original");
            entity.Property(e => e.FilenameStorage).HasColumnName("filename_storage");
            entity.Property(e => e.MimeType).HasColumnName("mime_type");
            entity.Property(e => e.GroesseBytes).HasColumnName("groesse_bytes");
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.GueltigVon).HasColumnName("gueltig_von").HasColumnType("date");
            entity.Property(e => e.GueltigBis).HasColumnName("gueltig_bis").HasColumnType("date");
            entity.Property(e => e.HochgeladenVon).HasColumnName("hochgeladen_von");
            entity.Property(e => e.HochgeladenAm).HasColumnName("hochgeladen_am");
            entity.HasIndex(e => e.EmployeeId);
            entity.HasIndex(e => e.DokumentTypId);
        });

        // ── VertragstypLohnposition ──────────────────────────────────────────
        modelBuilder.Entity<VertragstypLohnposition>(entity =>
        {
            entity.ToTable("vertragstyp_lohnposition");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.VertragstypCode).HasColumnName("vertragstyp_code").HasMaxLength(10);
            entity.Property(e => e.LohnpositionCode).HasColumnName("lohnposition_code").HasMaxLength(20);
            entity.Property(e => e.IsRequired).HasColumnName("is_required").HasDefaultValue(false);
            entity.Property(e => e.IsDefaultActive).HasColumnName("is_default_active").HasDefaultValue(true);
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => new { e.VertragstypCode, e.LohnpositionCode })
                  .HasDatabaseName("IX_vertragstyp_lohnposition_unique").IsUnique();
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
            entity.Property(e => e.LohnpositionCode).HasColumnName("lohnposition_code").HasMaxLength(20);
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
            entity.Property(e => e.LohnpositionId).HasColumnName("lohnposition_id");
            entity.Property(e => e.Betrag).HasColumnName("betrag").HasColumnType("numeric(10,2)");
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.Lohnposition).WithMany().HasForeignKey(e => e.LohnpositionId);
            entity.HasIndex(e => new { e.EmployeeId, e.Periode }).HasDatabaseName("IX_lohn_zulage_emp_periode");
        });

        // ── EmployeeRecurringWage ──────────────────────────────────────────
        modelBuilder.Entity<EmployeeRecurringWage>(entity =>
        {
            entity.ToTable("employee_recurring_wage");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.LohnpositionId).HasColumnName("lohnposition_id");
            entity.Property(e => e.Betrag).HasColumnName("betrag").HasColumnType("numeric(10,2)");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.Lohnposition).WithMany().HasForeignKey(e => e.LohnpositionId);
            entity.HasIndex(e => new { e.EmployeeId, e.ValidFrom, e.ValidTo })
                  .HasDatabaseName("idx_employee_recurring_wage_period");
        });

        // ── EmploymentModelComponent ───────────────────────────────────────
        modelBuilder.Entity<EmploymentModelComponent>(entity =>
        {
            entity.ToTable("employment_model_component");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmploymentModelCode).HasColumnName("employment_model_code").HasMaxLength(10);
            entity.Property(e => e.LohnpositionId).HasColumnName("lohnposition_id");
            entity.Property(e => e.Rate).HasColumnName("rate").HasColumnType("numeric(8,4)");
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Lohnposition).WithMany().HasForeignKey(e => e.LohnpositionId);
            entity.HasIndex(e => new { e.EmploymentModelCode, e.IsActive, e.SortOrder })
                  .HasDatabaseName("idx_employment_model_component_model");
            entity.HasIndex(e => new { e.EmploymentModelCode, e.LohnpositionId })
                  .IsUnique()
                  .HasDatabaseName("employment_model_component_unique");
        });

        // ── SwissLocation (PLZ-Lookup) ─────────────────────────────────────
        modelBuilder.Entity<SwissLocation>(entity =>
        {
            entity.ToTable("swiss_location");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Plz4).HasColumnName("plz4").HasMaxLength(4);
            entity.Property(e => e.Gemeindename).HasColumnName("gemeindename").HasMaxLength(80);
            entity.Property(e => e.BfsNr).HasColumnName("bfs_nr");
            entity.Property(e => e.Kantonskuerzel).HasColumnName("kantonskuerzel").HasMaxLength(2);
            entity.HasIndex(e => e.Plz4).HasDatabaseName("idx_swiss_location_plz");
            entity.HasIndex(e => new { e.Plz4, e.BfsNr })
                  .IsUnique()
                  .HasDatabaseName("swiss_location_plz_bfs_unique");
        });

        // ── Behoerde ───────────────────────────────────────────────────────
        modelBuilder.Entity<Behoerde>(entity =>
        {
            entity.ToTable("behoerde");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200);
            entity.Property(e => e.Typ).HasColumnName("typ").HasMaxLength(30).HasDefaultValue("BETREIBUNGSAMT");
            entity.Property(e => e.Adresse1).HasColumnName("adresse1").HasMaxLength(200);
            entity.Property(e => e.Adresse2).HasColumnName("adresse2").HasMaxLength(200);
            entity.Property(e => e.Adresse3).HasColumnName("adresse3").HasMaxLength(200);
            entity.Property(e => e.Plz).HasColumnName("plz").HasMaxLength(10);
            entity.Property(e => e.Ort).HasColumnName("ort").HasMaxLength(100);
            entity.Property(e => e.Telefon).HasColumnName("telefon").HasMaxLength(30);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(200);
            entity.Property(e => e.Iban).HasColumnName("iban").HasMaxLength(34);
            entity.Property(e => e.QrIban).HasColumnName("qr_iban").HasMaxLength(34);
            entity.Property(e => e.Bic).HasColumnName("bic").HasMaxLength(20);
            entity.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(100);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
        });

        // ── EmployeeLohnAssignment ─────────────────────────────────────────
        modelBuilder.Entity<EmployeeLohnAssignment>(entity =>
        {
            entity.ToTable("employee_lohn_assignment");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.BehoerdeId).HasColumnName("behoerde_id");
            entity.Property(e => e.Bezeichnung).HasColumnName("bezeichnung").HasMaxLength(100);
            entity.Property(e => e.Freigrenze).HasColumnName("freigrenze").HasColumnType("numeric(10,2)");
            entity.Property(e => e.Zielbetrag).HasColumnName("zielbetrag").HasColumnType("numeric(10,2)");
            entity.Property(e => e.BereitsAbgezogen).HasColumnName("bereits_abgezogen").HasColumnType("numeric(10,2)");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.ReferenzAmt).HasColumnName("referenz_amt").HasMaxLength(100);
            entity.Property(e => e.ZahlungsReferenz).HasColumnName("zahlungs_referenz").HasMaxLength(50);
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.Behoerde).WithMany().HasForeignKey(e => e.BehoerdeId);
            entity.HasIndex(e => new { e.EmployeeId, e.ValidFrom, e.ValidTo })
                  .HasDatabaseName("idx_employee_lohn_assignment_period");
        });

        // ── PayrollLohnAbtretungEntry ───────────────────────────────────────
        modelBuilder.Entity<PayrollLohnAbtretungEntry>(entity =>
        {
            entity.ToTable("payroll_lohn_abtretung_entry");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PayrollSnapshotId).HasColumnName("payroll_snapshot_id");
            entity.Property(e => e.EmployeeLohnAssignmentId).HasColumnName("employee_lohn_assignment_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.BehoerdeId).HasColumnName("behoerde_id");
            entity.Property(e => e.PeriodYear).HasColumnName("period_year");
            entity.Property(e => e.PeriodMonth).HasColumnName("period_month");
            entity.Property(e => e.Bezeichnung).HasColumnName("bezeichnung").HasMaxLength(100);
            entity.Property(e => e.ReferenzAmt).HasColumnName("referenz_amt").HasMaxLength(100);
            entity.Property(e => e.ZahlungsReferenz).HasColumnName("zahlungs_referenz").HasMaxLength(50);
            entity.Property(e => e.BehoerdeName).HasColumnName("behoerde_name").HasMaxLength(200);
            entity.Property(e => e.Iban).HasColumnName("iban").HasMaxLength(34);
            entity.Property(e => e.QrIban).HasColumnName("qr_iban").HasMaxLength(34);
            entity.Property(e => e.Betrag).HasColumnName("betrag").HasColumnType("numeric(10,2)");
            entity.Property(e => e.BereitsAbgezogenVorher).HasColumnName("bereits_abgezogen_vorher").HasColumnType("numeric(10,2)");
            entity.Property(e => e.BereitsAbgezogenNachher).HasColumnName("bereits_abgezogen_nachher").HasColumnType("numeric(10,2)");
            entity.Property(e => e.FibuBelegnr).HasColumnName("fibu_belegnr").HasMaxLength(50);
            entity.Property(e => e.FibuExportiertAm).HasColumnName("fibu_exportiert_am");
            entity.Property(e => e.DtaExportiertAm).HasColumnName("dta_exportiert_am");
            entity.Property(e => e.DtaExportRef).HasColumnName("dta_export_ref").HasMaxLength(50);
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");

            entity.HasOne(e => e.Snapshot).WithMany().HasForeignKey(e => e.PayrollSnapshotId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Assignment).WithMany().HasForeignKey(e => e.EmployeeLohnAssignmentId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Behoerde).WithMany().HasForeignKey(e => e.BehoerdeId).OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.PayrollSnapshotId, e.EmployeeLohnAssignmentId })
                  .IsUnique()
                  .HasDatabaseName("payroll_lohn_abtretung_entry_unique_per_snapshot");
            entity.HasIndex(e => new { e.EmployeeId, e.PeriodYear, e.PeriodMonth })
                  .HasDatabaseName("idx_plae_employee_period");
            entity.HasIndex(e => new { e.BehoerdeId, e.PeriodYear, e.PeriodMonth })
                  .HasDatabaseName("idx_plae_behoerde_period");
        });

        // ── BankMaster ──────────────────────────────────────────────────────
        modelBuilder.Entity<BankMaster>(entity =>
        {
            entity.ToTable("bank_master");
            entity.HasKey(e => e.Iid);
            entity.Property(e => e.Iid).HasColumnName("iid").HasMaxLength(10);
            entity.Property(e => e.Bic).HasColumnName("bic").HasMaxLength(15);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200);
            entity.Property(e => e.Ort).HasColumnName("ort").HasMaxLength(100);
            entity.Property(e => e.Strasse).HasColumnName("strasse").HasMaxLength(200);
            entity.Property(e => e.Plz).HasColumnName("plz").HasMaxLength(10);
            entity.Property(e => e.ImportedAt).HasColumnName("imported_at");
        });

        // ── EmployeeBankAccount ─────────────────────────────────────────────
        modelBuilder.Entity<EmployeeBankAccount>(entity =>
        {
            entity.ToTable("employee_bank_account");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.Iban).HasColumnName("iban").HasMaxLength(34);
            entity.Property(e => e.Bic).HasColumnName("bic").HasMaxLength(15);
            entity.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(200);
            entity.Property(e => e.Kontoinhaber).HasColumnName("kontoinhaber").HasMaxLength(200);
            entity.Property(e => e.Zahlungsreferenz).HasColumnName("zahlungsreferenz").HasMaxLength(50);
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.IsHauptbank).HasColumnName("is_hauptbank").HasDefaultValue(true);
            entity.Property(e => e.AufteilungTyp).HasColumnName("aufteilung_typ").HasMaxLength(20).HasDefaultValue("VOLL");
            entity.Property(e => e.AufteilungWert).HasColumnName("aufteilung_wert").HasColumnType("numeric(10,2)");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasIndex(e => new { e.EmployeeId, e.ValidFrom, e.ValidTo })
                  .HasDatabaseName("idx_emp_bank_period");
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

        // ── SocialInsuranceRate ────────────────────────────────────────────
        modelBuilder.Entity<SocialInsuranceRate>(entity =>
        {
            entity.ToTable("social_insurance_rate");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(20);
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(100);
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(200);
            entity.Property(e => e.Rate).HasColumnName("rate").HasColumnType("numeric(8,4)");
            entity.Property(e => e.BasisType).HasColumnName("basis_type").HasMaxLength(20).HasDefaultValue("gross");
            entity.Property(e => e.EmploymentModelCode).HasColumnName("employment_model_code").HasMaxLength(20);
            entity.Property(e => e.MinAge).HasColumnName("min_age");
            entity.Property(e => e.MaxAge).HasColumnName("max_age");
            entity.Property(e => e.FreibetragMonthly).HasColumnName("freibetrag_monthly").HasColumnType("numeric(10,2)");
            entity.Property(e => e.CoordinationDeduction).HasColumnName("coordination_deduction").HasColumnType("numeric(10,2)");
            entity.Property(e => e.OnlyQuellensteuer).HasColumnName("only_quellensteuer").HasDefaultValue(false);
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        // ── Lohnposition ──────────────────────────────────────────────────
        modelBuilder.Entity<Lohnposition>(entity =>
        {
            entity.ToTable("lohnposition");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(20);
            entity.Property(e => e.Bezeichnung).HasColumnName("bezeichnung").HasMaxLength(150);
            entity.Property(e => e.Kategorie).HasColumnName("kategorie").HasMaxLength(80);
            entity.Property(e => e.Typ).HasColumnName("typ").HasMaxLength(10).HasDefaultValue("ZULAGE");
            entity.Property(e => e.AhvAlvPflichtig).HasColumnName("ahv_alv_pflichtig").HasDefaultValue(true);
            entity.Property(e => e.NbuvPflichtig).HasColumnName("nbuv_pflichtig").HasDefaultValue(true);
            entity.Property(e => e.KtgPflichtig).HasColumnName("ktg_pflichtig").HasDefaultValue(true);
            entity.Property(e => e.BvgPflichtig).HasColumnName("bvg_pflichtig").HasDefaultValue(true);
            entity.Property(e => e.QstPflichtig).HasColumnName("qst_pflichtig").HasDefaultValue(true);
            entity.Property(e => e.LohnausweisCode).HasColumnName("lohnausweis_code").HasMaxLength(20);
            entity.Property(e => e.DreijehnterMlPflichtig).HasColumnName("dreijehnter_ml_pflichtig").HasDefaultValue(false);
            entity.Property(e => e.ZaehltAlsBasisFeiertag).HasColumnName("zaehlt_als_basis_feiertag").HasDefaultValue(false);
            entity.Property(e => e.ZaehltAlsBasisFerien).HasColumnName("zaehlt_als_basis_ferien").HasDefaultValue(false);
            entity.Property(e => e.ZaehltAlsBasis13ml).HasColumnName("zaehlt_als_basis_13ml").HasDefaultValue(false);
            // Mirus-Erweiterungen
            entity.Property(e => e.Lohnausweisfeld).HasColumnName("lohnausweisfeld").HasMaxLength(10);
            entity.Property(e => e.LohnausweisKreuz).HasColumnName("lohnausweis_kreuz").HasDefaultValue(false);
            entity.Property(e => e.StatistikCode).HasColumnName("statistik_code").HasMaxLength(20);
            entity.Property(e => e.NichtDruckenWennNull).HasColumnName("nicht_drucken_wenn_null").HasDefaultValue(true);
            entity.Property(e => e.NichtImVertragDrucken).HasColumnName("nicht_im_vertrag_drucken").HasDefaultValue(false);
            entity.Property(e => e.BvgAuf100Rechnen).HasColumnName("bvg_auf_100_rechnen").HasDefaultValue(false);
            entity.Property(e => e.Position13ml).HasColumnName("position_13ml").HasDefaultValue(0);
            entity.Property(e => e.ZaehltFuerTagessatz).HasColumnName("zaehlt_fuer_tagessatz").HasDefaultValue(true);
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => e.Code).HasDatabaseName("IX_lohnposition_code").IsUnique();
        });

        // ── PayrollPeriodeConfig ───────────────────────────────────────────
        modelBuilder.Entity<PayrollPeriodeConfig>(entity =>
        {
            entity.ToTable("payroll_periode_config");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.FromDay).HasColumnName("from_day").HasDefaultValue(1);
            entity.Property(e => e.ToDay).HasColumnName("to_day").HasDefaultValue(31);
            entity.Property(e => e.ValidFromYear).HasColumnName("valid_from_year");
            entity.Property(e => e.ValidFromMonth).HasColumnName("valid_from_month").HasDefaultValue(1);
            entity.Property(e => e.IsLocked).HasColumnName("is_locked").HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyProfileId);
            entity.HasIndex(e => new { e.CompanyProfileId, e.ValidFromYear, e.ValidFromMonth })
                  .IsUnique().HasDatabaseName("UX_payroll_periode_config_branch_year_month");
        });

        // ── PayrollPeriode ─────────────────────────────────────────────────
        modelBuilder.Entity<PayrollPeriode>(entity =>
        {
            entity.ToTable("payroll_periode");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.ConfigId).HasColumnName("config_id");
            entity.Property(e => e.Year).HasColumnName("year");
            entity.Property(e => e.Month).HasColumnName("month");
            entity.Property(e => e.PeriodFrom).HasColumnName("period_from").HasColumnType("date");
            entity.Property(e => e.PeriodTo).HasColumnName("period_to").HasColumnType("date");
            entity.Property(e => e.Label).HasColumnName("label").HasMaxLength(100);
            entity.Property(e => e.IsTransition).HasColumnName("is_transition").HasDefaultValue(false);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("offen");
            entity.Property(e => e.AbgeschlossenAm).HasColumnName("abgeschlossen_am");
            entity.Property(e => e.AbgeschlossenVon).HasColumnName("abgeschlossen_von");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.PdfFooterText).HasColumnName("pdf_footer_text");
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyProfileId);
            entity.HasOne(e => e.Config).WithMany().HasForeignKey(e => e.ConfigId);
        });

        // ── PayrollSnapshot ────────────────────────────────────────────────
        modelBuilder.Entity<PayrollSnapshot>(entity =>
        {
            entity.ToTable("payroll_snapshot");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PayrollPeriodeId).HasColumnName("payroll_periode_id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.SlipJson).HasColumnName("slip_json").HasColumnType("jsonb");
            entity.Property(e => e.Brutto).HasColumnName("brutto").HasColumnType("numeric(10,2)");
            entity.Property(e => e.Netto).HasColumnName("netto").HasColumnType("numeric(10,2)");
            entity.Property(e => e.SvBasisAhv).HasColumnName("sv_basis_ahv").HasColumnType("numeric(10,2)");
            entity.Property(e => e.SvBasisBvg).HasColumnName("sv_basis_bvg").HasColumnType("numeric(10,2)");
            entity.Property(e => e.QstBetrag).HasColumnName("qst_betrag").HasColumnType("numeric(10,2)");
            entity.Property(e => e.ThirteenthAccumulated).HasColumnName("thirteenth_accumulated").HasColumnType("numeric(10,2)");
            entity.Property(e => e.FerienGeldSaldo).HasColumnName("ferien_geld_saldo").HasColumnType("numeric(10,2)");
            entity.Property(e => e.IsFinal).HasColumnName("is_final").HasDefaultValue(false);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Periode).WithMany(p => p.Snapshots).HasForeignKey(e => e.PayrollPeriodeId);
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasIndex(e => new { e.PayrollPeriodeId, e.EmployeeId })
                  .IsUnique().HasDatabaseName("UX_payroll_snapshot_periode_emp");
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