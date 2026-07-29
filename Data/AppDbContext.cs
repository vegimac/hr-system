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
    public DbSet<EmploymentProbationLog> EmploymentProbationLogs => Set<EmploymentProbationLog>();
    public DbSet<Moment> Moments => Set<Moment>();
    public DbSet<MomentPage> MomentPages => Set<MomentPage>();
    public DbSet<EmployeeMomentConsent> EmployeeMomentConsents => Set<EmployeeMomentConsent>();
    public DbSet<MomentType> MomentTypes => Set<MomentType>();
    public DbSet<MomentTone> MomentTones => Set<MomentTone>();
    public DbSet<MomentText> MomentTexts => Set<MomentText>();
    public DbSet<WebAuthnCredential> WebAuthnCredentials => Set<WebAuthnCredential>();
    public DbSet<PostfachSetupToken> PostfachSetupTokens => Set<PostfachSetupToken>();
    public DbSet<ContractShareToken> ContractShareTokens => Set<ContractShareToken>();
    public DbSet<PermitReminderToken> PermitReminderTokens => Set<PermitReminderToken>();
    public DbSet<SmsLog>             SmsLogs             => Set<SmsLog>();
    public DbSet<EmployeeAvailability> EmployeeAvailabilities => Set<EmployeeAvailability>();
    public DbSet<EmployeeAvailabilitySlot> EmployeeAvailabilitySlots => Set<EmployeeAvailabilitySlot>();
    public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();
    public DbSet<CompanyProfileBankAccount> CompanyProfileBankAccounts => Set<CompanyProfileBankAccount>();
    public DbSet<EducationLevel> EducationLevels => Set<EducationLevel>();
    public DbSet<EmployeeEducationHistory> EmployeeEducationHistories => Set<EmployeeEducationHistory>();
    public DbSet<PermitType> PermitTypes => Set<PermitType>();
    public DbSet<MinimumWageRuleNew> MinimumWageRulesNew => Set<MinimumWageRuleNew>();
    public DbSet<JobGroup> JobGroups => Set<JobGroup>();
    public DbSet<DashboardWarningConfig> DashboardWarningConfigs => Set<DashboardWarningConfig>();
    public DbSet<AppText> AppTexts => Set<AppText>();
    public DbSet<Nationality> Nationalities => Set<Nationality>();
    public DbSet<EmployeeImportSnapshot> EmployeeImportSnapshots => Set<EmployeeImportSnapshot>();
    public DbSet<ContractText> ContractTexts => Set<ContractText>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<UserBranchAccess> UserBranchAccesses => Set<UserBranchAccess>();
    public DbSet<Arzt> Aerzte => Set<Arzt>();
    public DbSet<ExitSurveyResponse> ExitSurveyResponses => Set<ExitSurveyResponse>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<EmployeeFamilyMember> EmployeeFamilyMembers => Set<EmployeeFamilyMember>();
    public DbSet<EmployeeAddress> EmployeeAddresses => Set<EmployeeAddress>();
    public DbSet<FamilyMemberAllowance> FamilyMemberAllowances => Set<FamilyMemberAllowance>();
    public DbSet<EmployeeTimeEntry> EmployeeTimeEntries => Set<EmployeeTimeEntry>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<PayrollSaldo> PayrollSaldos => Set<PayrollSaldo>();
    public DbSet<LohnKontoMapping> LohnKontoMappings => Set<LohnKontoMapping>();
    public DbSet<KrankheitKarenzSaldo> KrankheitKarenzSaldos => Set<KrankheitKarenzSaldo>();
    public DbSet<EmployeeLohnDurchschnitt> EmployeeLohnDurchschnitte => Set<EmployeeLohnDurchschnitt>();
    public DbSet<EmployeeQuellensteuer> EmployeeQuellensteuer => Set<EmployeeQuellensteuer>();
    public DbSet<LohnZulagTyp> LohnZulagTypen => Set<LohnZulagTyp>();
    public DbSet<LohnZulage> LohnZulagen => Set<LohnZulage>();
    public DbSet<EmployeeRecurringWage> EmployeeRecurringWages => Set<EmployeeRecurringWage>();
    public DbSet<EmployeeBvgZusatzMember> EmployeeBvgZusatzMembers => Set<EmployeeBvgZusatzMember>();
    public DbSet<PregnancyRule>     PregnancyRules     => Set<PregnancyRule>();
    public DbSet<EmployeePregnancy> EmployeePregnancies => Set<EmployeePregnancy>();
    public DbSet<EmploymentModelComponent> EmploymentModelComponents => Set<EmploymentModelComponent>();
    public DbSet<SwissLocation> SwissLocations => Set<SwissLocation>();
    public DbSet<Behoerde> Behoerden => Set<Behoerde>();
    public DbSet<CompanyProfileSsl> CompanyProfileSsls => Set<CompanyProfileSsl>();
    public DbSet<FamilienzulagenTarif> FamilienzulagenTarife => Set<FamilienzulagenTarif>();
    public DbSet<EmployeeLohnAssignment> EmployeeLohnAssignments => Set<EmployeeLohnAssignment>();
    public DbSet<AbsenzTyp> AbsenzTypen => Set<AbsenzTyp>();
    public DbSet<EmployeeArbeitslosigkeit> EmployeeArbeitslosigkeiten => Set<EmployeeArbeitslosigkeit>();
    public DbSet<SocialInsuranceRate> SocialInsuranceRates => Set<SocialInsuranceRate>();
    public DbSet<Lohnposition> Lohnpositionen => Set<Lohnposition>();
    public DbSet<VertragstypLohnposition> VertragstypLohnpositionen => Set<VertragstypLohnposition>();
    public DbSet<PayrollPeriode>        PayrollPerioden        => Set<PayrollPeriode>();
    public DbSet<PayrollPeriodeAudit>   PayrollPeriodeAudits   => Set<PayrollPeriodeAudit>();
    public DbSet<AuditLog>              AuditLogs              => Set<AuditLog>();
    public DbSet<PayrollSnapshot>       PayrollSnapshots       => Set<PayrollSnapshot>();
    public DbSet<PayrollLohnAbtretungEntry> PayrollLohnAbtretungEntries => Set<PayrollLohnAbtretungEntry>();
    public DbSet<AkontoTermin>          AkontoTermine          => Set<AkontoTermin>();
    public DbSet<AkontoZahlung>         AkontoZahlungen        => Set<AkontoZahlung>();
    public DbSet<BankMaster>                BankMasters                 => Set<BankMaster>();
    public DbSet<EmployeeBankAccount>       EmployeeBankAccounts        => Set<EmployeeBankAccount>();
    public DbSet<DokumentKategorie>         DokumentKategorien          => Set<DokumentKategorie>();
    public DbSet<DokumentTyp>               DokumentTypen               => Set<DokumentTyp>();
    public DbSet<EmployeeDokument>          EmployeeDokumente           => Set<EmployeeDokument>();
    public DbSet<MailboxDocument>           MailboxDocuments            => Set<MailboxDocument>();
    public DbSet<BranchMinWage>             BranchMinWages              => Set<BranchMinWage>();
    public DbSet<SmtpSetting>               SmtpSettings                => Set<SmtpSetting>();
    public DbSet<EcallSetting>              EcallSettings               => Set<EcallSetting>();
    public DbSet<DvelopSetting>             DvelopSettings              => Set<DvelopSetting>();
    public DbSet<EmployeePermitHistory>     EmployeePermitHistories     => Set<EmployeePermitHistory>();
    public DbSet<EmployeeVerwarnung>        EmployeeVerwarnungen        => Set<EmployeeVerwarnung>();
    public DbSet<EasyAtWorkBranchMapping>   EasyAtWorkBranchMappings    => Set<EasyAtWorkBranchMapping>();
    public DbSet<EasyAtWorkSyncState>       EasyAtWorkSyncStates        => Set<EasyAtWorkSyncState>();
    public DbSet<EasyAtWorkEmployeeAlias>   EasyAtWorkEmployeeAliases   => Set<EasyAtWorkEmployeeAlias>();
    public DbSet<EasyAtWorkSyncLog>         EasyAtWorkSyncLogs          => Set<EasyAtWorkSyncLog>();
    public DbSet<EmployeeNumberAlias>       EmployeeNumberAliases       => Set<EmployeeNumberAlias>();

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
            entity.Property(e => e.MaidenName).HasColumnName("maiden_name").HasMaxLength(100);
            entity.Property(e => e.ShortName).HasColumnName("short_name").HasMaxLength(100);
            entity.Property(e => e.Street).HasColumnName("street");
            entity.Property(e => e.ZipCode).HasColumnName("zip_code");
            entity.Property(e => e.City).HasColumnName("city");
            entity.Property(e => e.Country).HasColumnName("country");
            entity.Property(e => e.CantonCode).HasColumnName("canton_code").HasMaxLength(2);
            entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("date");
            entity.Property(e => e.Nationality).HasColumnName("nationality");
            entity.Property(e => e.NationalityId).HasColumnName("nationality_id");
            entity.Property(e => e.LanguageCode).HasColumnName("language_code");
            entity.Property(e => e.PhoneMobile).HasColumnName("phone_mobile");
            entity.Property(e => e.Phone2).HasColumnName("phone2").HasMaxLength(50);
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.EntryDate).HasColumnName("entry_date").HasColumnType("date");
            entity.Property(e => e.ExitDate).HasColumnName("exit_date").HasColumnType("date");
            entity.Property(e => e.KuendigungAusgesprochenAm).HasColumnName("kuendigung_ausgesprochen_am").HasColumnType("date");
            entity.Property(e => e.KuendigungPer).HasColumnName("kuendigung_per").HasColumnType("date");
            entity.Property(e => e.KuendigungDurch).HasColumnName("kuendigung_durch");
            entity.Property(e => e.Austrittsgrund).HasColumnName("austrittsgrund");
            entity.Property(e => e.PermitTypeId).HasColumnName("permit_type_id");
            // permit_expiry_date entfernt 01.06.2026 — Dashboard liest jetzt
            // EmployeePermitHistory.ValidTo des jüngsten Eintrags.
            entity.Property(e => e.ZemisNumber).HasColumnName("zemis_number").HasMaxLength(50);
            entity.Property(e => e.QuellensteuerBefreitAb).HasColumnName("quellensteuer_befreit_ab").HasColumnType("date");
            // QST-Befreiung durch Steuerbehörde (Walter 26.05.2026)
            entity.Property(e => e.QstBefreitDurchBehoerde).HasColumnName("qst_befreit_durch_behoerde").HasDefaultValue(false);
            entity.Property(e => e.QstBefreiungDokumentId).HasColumnName("qst_befreiung_dokument_id");
            entity.Property(e => e.QstBefreiungGueltigAb).HasColumnName("qst_befreiung_gueltig_ab").HasColumnType("date");
            entity.Property(e => e.QstBefreiungGueltigBis).HasColumnName("qst_befreiung_gueltig_bis").HasColumnType("date");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.IsPayrollExcluded).HasColumnName("is_payroll_excluded").HasDefaultValue(false);
            // Walter-Vorgabe 12.06.2026: Soft-Delete-Flag (admin „Mitarbeiter löschen").
            entity.Property(e => e.IsHidden).HasColumnName("is_hidden").HasDefaultValue(false);
            // Walter-Vorgabe 13.06.2026: explizite Verknüpfungen MA → Beleg-Doku.
            entity.Property(e => e.IdPassDokumentId).HasColumnName("id_pass_dokument_id");
            entity.Property(e => e.CAusweisDokumentId).HasColumnName("c_ausweis_dokument_id");
            entity.Property(e => e.NightWorkExamValidUntil).HasColumnName("night_work_exam_valid_until").HasColumnType("date");
            entity.Property(e => e.NightWorkExamIssued).HasColumnName("night_work_exam_issued").HasColumnType("date");
            entity.Property(e => e.NightWorkExamEasyMismatch).HasColumnName("night_work_exam_easy_mismatch").HasDefaultValue(false);
            entity.Property(e => e.NightWorkExamDokumentId).HasColumnName("night_work_exam_dokument_id");
            entity.Property(e => e.NightWorkAusnahmeDokumentId).HasColumnName("night_work_ausnahme_dokument_id");
            entity.Property(e => e.ProbezeitGespraech1Am).HasColumnName("probezeit_gespraech1_am").HasColumnType("date");
            entity.Property(e => e.ProbezeitGespraech1DokumentId).HasColumnName("probezeit_gespraech1_dokument_id");
            entity.Property(e => e.ProbezeitGespraech2Am).HasColumnName("probezeit_gespraech2_am").HasColumnType("date");
            entity.Property(e => e.ProbezeitGespraech2DokumentId).HasColumnName("probezeit_gespraech2_dokument_id");
            entity.Property(e => e.EasyAtWorkEmployeeId).HasColumnName("easyatwork_employee_id");
            // GLOBALER QUERY FILTER: ALLE Employee-Queries blenden hidden MA
            // automatisch aus — kein manuelles WHERE in jedem Controller nötig.
            // Wer hidden MA explizit sehen will, ruft `.IgnoreQueryFilters()`
            // auf der Query (z.B. ein „Papierkorb"-View für Admin später).
            entity.HasQueryFilter(e => !e.IsHidden);
            // Walter-Vorgabe 07.06.2026: Anstellungs-Felder aus Mirus-HR-Review.
            // DB-Default beider Spalten = false, damit bestehende MA-Zeilen nicht
            // unbemerkt geändert werden. Bei NEU angelegten MA via Code setzt das
            // C#-Property LgavPflichtig=true (Schaub-Restaurants ist L-GAV-Branche).
            entity.Property(e => e.LgavPflichtig).HasColumnName("lgav_pflichtig").HasDefaultValue(false);
            entity.Property(e => e.TeilzeitUnter8hWoche).HasColumnName("teilzeit_unter_8h_woche").HasDefaultValue(false);
            entity.Property(e => e.KtgTagessatzManuell).HasColumnName("ktg_tagessatz_manuell").HasColumnType("numeric(10,2)");
            entity.Property(e => e.KtgKarenzAbgeschlossen).HasColumnName("ktg_karenz_abgeschlossen").HasDefaultValue(false);
            entity.Property(e => e.SocialSecurityNumber).HasColumnName("social_security_number").HasMaxLength(20);
            entity.Property(e => e.MaritalStatus).HasColumnName("marital_status").HasMaxLength(40);
            entity.Property(e => e.MaritalStatusSince).HasColumnName("marital_status_since").HasColumnType("date");
            entity.Property(e => e.SeparatedSince).HasColumnName("separated_since").HasColumnType("date");
            entity.Property(e => e.Religion).HasColumnName("religion").HasMaxLength(40);
            entity.Property(e => e.LetterSalutation).HasColumnName("letter_salutation").HasMaxLength(200);
            entity.Property(e => e.PlaceOfOrigin).HasColumnName("place_of_origin").HasMaxLength(150);
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
            entity.Property(e => e.JobGroupId).HasColumnName("job_group_id");
            entity.HasOne(e => e.JobGroup).WithMany().HasForeignKey(e => e.JobGroupId);
            entity.Property(e => e.ContractType).HasColumnName("contract_type");
            entity.Property(e => e.EducationLevelCode).HasColumnName("education_level_code").HasMaxLength(10);
            entity.Property(e => e.EmploymentPercentage).HasColumnName("employment_percentage");
            entity.Property(e => e.WeeklyHours).HasColumnName("weekly_hours");
            entity.Property(e => e.GuaranteedHoursPerWeek).HasColumnName("guaranteed_hours_per_week");
            entity.Property(e => e.MonthlySalaryFte).HasColumnName("monthly_salary_fte");
            entity.Property(e => e.MonthlySalary).HasColumnName("monthly_salary");
            entity.Property(e => e.HourlyRate).HasColumnName("hourly_rate");
            // Externe easy@work-Referenzen (Walter-Vorgabe 23.06.2026).
            entity.Property(e => e.EasyAtWorkContractId).HasColumnName("easyatwork_contract_id");
            entity.Property(e => e.EasyAtWorkPayRateId).HasColumnName("easyatwork_pay_rate_id");
            entity.Property(e => e.EasyAtWorkUpdatedAt)
                  .HasColumnName("easyatwork_updated_at")
                  .HasColumnType("timestamp without time zone");
            entity.Property(e => e.EasyAtWorkManualOverride)
                  .HasColumnName("easyatwork_manual_override")
                  .HasDefaultValue(false);
            // Walter-Vorgabe 06.06.2026 (Stufe 1b): VacationPercent, HolidayPercent,
            // ThirteenthSalaryPercent sind aus dem Model entfernt und Spalten droppe
            // ich via Migration `drop_employment_pct_fields.sql`. Werte kommen ab
            // jetzt aus CompanyProfile.Default* + altersaware Engine-Logik.
            entity.Property(e => e.VacationPaymentMode).HasColumnName("vacation_payment_mode");
            entity.Property(e => e.ProbationPeriodMonths).HasColumnName("probation_period_months");
            entity.Property(e => e.ProbationEndDate).HasColumnName("probation_end_date").HasColumnType("date");
            entity.Property(e => e.ProbationStartDate).HasColumnName("probation_start_date").HasColumnType("date");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasOne(e => e.Employee).WithMany(e => e.Employments).HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.CompanyProfile).WithMany().HasForeignKey(e => e.CompanyProfileId);
        });

        modelBuilder.Entity<Moment>(entity =>
        {
            entity.ToTable("moment");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Token).HasColumnName("token");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.Typ).HasColumnName("typ");
            entity.Property(e => e.Zustellung).HasColumnName("zustellung");
            entity.Property(e => e.MailboxDocumentId).HasColumnName("mailbox_document_id");
            entity.Property(e => e.Absender).HasColumnName("absender");
            entity.Property(e => e.DokumentName).HasColumnName("dokument_name");
            entity.Property(e => e.VerifiedAt).HasColumnName("verified_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.SmsText).HasColumnName("sms_text");
            entity.Property(e => e.FullText).HasColumnName("full_text");
            entity.Property(e => e.Antwortart).HasColumnName("antwortart");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedById).HasColumnName("created_by_id");
            entity.Property(e => e.OpenedAt).HasColumnName("opened_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.RespondedAt).HasColumnName("responded_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.ResponseValue).HasColumnName("response_value");
            entity.Property(e => e.ResponseText).HasColumnName("response_text");
            entity.Property(e => e.ResponseDokumentId).HasColumnName("response_dokument_id");
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });

        modelBuilder.Entity<MomentPage>(entity =>
        {
            entity.ToTable("moment_page");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.SenderId).HasColumnName("sender_id");
            entity.Property(e => e.MomentType).HasColumnName("moment_type");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.MessageHtml).HasColumnName("message_html");
            entity.Property(e => e.TokenHash).HasColumnName("token_hash");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.OpenedAt).HasColumnName("opened_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.RespondedAt).HasColumnName("responded_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.ResponseValue).HasColumnName("response_value");
            entity.Property(e => e.Status).HasColumnName("status");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.SmsText).HasColumnName("sms_text");
            entity.Property(e => e.Antwortart).HasColumnName("antwortart");
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });

        modelBuilder.Entity<EmployeeMomentConsent>(entity =>
        {
            entity.ToTable("employee_moment_consent");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.MomentsConsentEnabled).HasColumnName("moments_consent_enabled");
            entity.Property(e => e.AllowBirthdayAndAnniversaryMoments).HasColumnName("allow_birthday_anniversary");
            entity.Property(e => e.AllowAppreciationMoments).HasColumnName("allow_appreciation");
            entity.Property(e => e.AllowCareMoments).HasColumnName("allow_care");
            entity.Property(e => e.ConsentTextVersion).HasColumnName("consent_text_version");
            entity.Property(e => e.GrantedAt).HasColumnName("granted_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.LastChangedAt).HasColumnName("last_changed_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.LastChangedBy).HasColumnName("last_changed_by");
            entity.Property(e => e.Source).HasColumnName("source");
            entity.HasIndex(e => e.EmployeeId).IsUnique();
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });

        modelBuilder.Entity<MomentType>(entity =>
        {
            entity.ToTable("moment_type");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.ConsentCategory).HasColumnName("consent_category");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<MomentTone>(entity =>
        {
            entity.ToTable("moment_tone");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code");
            entity.Property(e => e.Name).HasColumnName("name");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<MomentText>(entity =>
        {
            entity.ToTable("moment_text");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MomentTypeId).HasColumnName("moment_type_id");
            entity.Property(e => e.MomentToneId).HasColumnName("moment_tone_id");
            entity.Property(e => e.Titel).HasColumnName("titel");
            entity.Property(e => e.SmsText).HasColumnName("sms_text");
            entity.Property(e => e.BodyText).HasColumnName("body_text");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.LanguageCode).HasColumnName("language_code");
            entity.Property(e => e.Version).HasColumnName("version");
            entity.Property(e => e.RequiresReview).HasColumnName("requires_review");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.HasOne(e => e.MomentType).WithMany().HasForeignKey(e => e.MomentTypeId);
            entity.HasOne(e => e.MomentTone).WithMany().HasForeignKey(e => e.MomentToneId);
            entity.HasIndex(e => new { e.MomentTypeId, e.MomentToneId });
        });

        modelBuilder.Entity<WebAuthnCredential>(entity =>
        {
            entity.ToTable("webauthn_credential");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppUserId).HasColumnName("app_user_id");
            entity.Property(e => e.CredentialId).HasColumnName("credential_id");
            entity.Property(e => e.PublicKey).HasColumnName("public_key");
            entity.Property(e => e.SignCount).HasColumnName("sign_count");
            entity.Property(e => e.UserHandle).HasColumnName("user_handle");
            entity.Property(e => e.Transports).HasColumnName("transports");
            entity.Property(e => e.Aaguid).HasColumnName("aaguid");
            entity.Property(e => e.DeviceLabel).HasColumnName("device_label");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.LastUsedAt).HasColumnName("last_used_at").HasColumnType("timestamp without time zone");
            entity.HasIndex(e => e.CredentialId).IsUnique();
            entity.HasOne(e => e.AppUser).WithMany().HasForeignKey(e => e.AppUserId);
        });

        modelBuilder.Entity<PostfachSetupToken>(entity =>
        {
            entity.ToTable("postfach_setup_token");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AppUserId).HasColumnName("app_user_id");
            entity.Property(e => e.TokenHash).HasColumnName("token_hash");
            entity.Property(e => e.Purpose).HasColumnName("purpose");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.UsedAt).HasColumnName("used_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.HasIndex(e => e.TokenHash).IsUnique();
            entity.HasOne(e => e.AppUser).WithMany().HasForeignKey(e => e.AppUserId);
        });

        modelBuilder.Entity<ContractShareToken>(entity =>
        {
            entity.ToTable("contract_share_token");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.EmploymentId).HasColumnName("employment_id");
            entity.Property(e => e.TokenHash).HasColumnName("token_hash");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.UsedAt).HasColumnName("used_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.OpenedAt).HasColumnName("opened_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.HasIndex(e => e.TokenHash).IsUnique();
        });

        // ── PermitReminderToken — SMS-Link Bewilligung abgelaufen ────────
        modelBuilder.Entity<PermitReminderToken>(entity =>
        {
            entity.ToTable("permit_reminder_token");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.PermitHistoryId).HasColumnName("permit_history_id");
            entity.Property(e => e.TokenHash).HasColumnName("token_hash");
            entity.Property(e => e.MessageHtml).HasColumnName("message_html");
            entity.Property(e => e.Title).HasColumnName("title");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.OpenedAt).HasColumnName("opened_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.HasIndex(e => e.TokenHash).IsUnique();
        });

        // ── SmsLog — Protokoll aller eCall-SMS-Versandversuche ────────────
        modelBuilder.Entity<SmsLog>(entity =>
        {
            entity.ToTable("sms_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.Purpose).HasColumnName("purpose");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.ToPhone).HasColumnName("to_phone");
            entity.Property(e => e.RedirectedTo).HasColumnName("redirected_to");
            entity.Property(e => e.Ok).HasColumnName("ok");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.Error).HasColumnName("error");
            entity.HasIndex(e => new { e.EmployeeId, e.Purpose });
        });

        modelBuilder.Entity<EmployeeAvailability>(entity =>
        {
            entity.ToTable("employee_availability");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.Type).HasColumnName("type");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.EasyAtWorkAvailabilityId).HasColumnName("easyatwork_availability_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.HasIndex(e => e.EmployeeId);
            entity.HasMany(e => e.Slots)
                  .WithOne(s => s.Availability!)
                  .HasForeignKey(s => s.AvailabilityId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmployeeAvailabilitySlot>(entity =>
        {
            entity.ToTable("employee_availability_slot");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.AvailabilityId).HasColumnName("availability_id");
            entity.Property(e => e.Von).HasColumnName("von").HasColumnType("time without time zone");
            entity.Property(e => e.Bis).HasColumnName("bis").HasColumnType("time without time zone");
            entity.Property(e => e.Mon).HasColumnName("mon");
            entity.Property(e => e.Tue).HasColumnName("tue");
            entity.Property(e => e.Wed).HasColumnName("wed");
            entity.Property(e => e.Thu).HasColumnName("thu");
            entity.Property(e => e.Fri).HasColumnName("fri");
            entity.Property(e => e.Sat).HasColumnName("sat");
            entity.Property(e => e.Sun).HasColumnName("sun");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.HasIndex(e => e.AvailabilityId);
        });

        modelBuilder.Entity<EmploymentProbationLog>(entity =>
        {
            entity.ToTable("employment_probation_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmploymentId).HasColumnName("employment_id");
            entity.Property(e => e.EventDate).HasColumnName("event_date").HasColumnType("date");
            entity.Property(e => e.EventType).HasColumnName("event_type");
            entity.Property(e => e.DeltaDays).HasColumnName("delta_days");
            entity.Property(e => e.Grund).HasColumnName("grund");
            entity.Property(e => e.ProbezeitEndeNachher).HasColumnName("probezeit_ende_nachher").HasColumnType("date");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.HasOne(e => e.Employment).WithMany().HasForeignKey(e => e.EmploymentId);
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
            entity.Property(e => e.KantonCode).HasColumnName("kanton_code").HasMaxLength(2);
            entity.Property(e => e.LoginPasswordPrefix).HasColumnName("login_password_prefix").HasMaxLength(5);
            entity.Property(e => e.Phone).HasColumnName("phone");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.NormalWeeklyHours).HasColumnName("normal_weekly_hours");
            entity.Property(e => e.MaxWeeklyHours).HasColumnName("max_weekly_hours").HasColumnType("numeric(5,2)");
            entity.Property(e => e.DefaultVacationWeeks).HasColumnName("default_vacation_weeks");
            entity.Property(e => e.WorkLocation).HasColumnName("work_location");
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
            entity.Property(e => e.VacationSixWeeksFromAge).HasColumnName("vacation_six_weeks_from_age").HasDefaultValue(50);
            entity.Property(e => e.DefaultThirteenthSalaryPercent).HasColumnName("default_thirteenth_salary_percent");
            entity.Property(e => e.ProbationMonths).HasColumnName("probation_months");
            entity.Property(e => e.NightStartTime).HasColumnName("night_start_time").HasMaxLength(5);
            entity.Property(e => e.NightEndTime).HasColumnName("night_end_time").HasMaxLength(5);
            entity.Property(e => e.ThirteenthMonthPayoutsPerYear).HasColumnName("thirteenth_month_payouts_per_year").HasDefaultValue(12);
            entity.Property(e => e.ThirteenthMonthPayoutMonths).HasColumnName("thirteenth_month_payout_months").HasMaxLength(40);
            entity.Property(e => e.AutoFerienGeldAuszahlungDezember).HasColumnName("auto_ferien_geld_auszahlung_dezember").HasDefaultValue(true);
            entity.Property(e => e.LohnausweisBoxFFreierTransport).HasColumnName("lohnausweis_box_f_freier_transport").HasDefaultValue(false);
            entity.Property(e => e.LohnausweisBoxGKantineGratis).HasColumnName("lohnausweis_box_g_kantine_gratis").HasDefaultValue(false);
            entity.Property(e => e.LohnausweisPos21VerpflegungMonat).HasColumnName("lohnausweis_pos_2_1_verpflegung_monat").HasColumnType("numeric(10,2)");
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
            entity.Property(e => e.AkontoProzentFix).HasColumnName("akonto_prozent_fix").HasColumnType("numeric(5,2)").HasDefaultValue(80m);
            entity.Property(e => e.AkontoProzentFixM).HasColumnName("akonto_prozent_fix_m").HasColumnType("numeric(5,2)").HasDefaultValue(90m);
            entity.Property(e => e.AkontoProzentHourly).HasColumnName("akonto_prozent_hourly").HasColumnType("numeric(5,2)").HasDefaultValue(100m);
            // Legacy-Bankverbindungs-Felder (vor Multi-Bank-Refactor). Bleiben
            // für Backward-Compat in der DB, werden vom UI nicht mehr genutzt.
            entity.Property(e => e.Iban).HasColumnName("iban").HasMaxLength(34);
            entity.Property(e => e.Bic).HasColumnName("bic").HasMaxLength(15);
            entity.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(200);
        });

        // ── CompanyProfileBankAccount ───────────────────────────────────────
        modelBuilder.Entity<CompanyProfileBankAccount>(entity =>
        {
            entity.ToTable("company_profile_bank_account");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.Iban).HasColumnName("iban").HasMaxLength(34);
            entity.Property(e => e.Bic).HasColumnName("bic").HasMaxLength(15);
            entity.Property(e => e.BankName).HasColumnName("bank_name").HasMaxLength(200);
            entity.Property(e => e.IsMain).HasColumnName("is_main").HasDefaultValue(true);
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.CompanyProfile).WithMany().HasForeignKey(e => e.CompanyProfileId);
            entity.HasIndex(e => new { e.CompanyProfileId, e.ValidFrom, e.ValidTo })
                  .HasDatabaseName("idx_cpba_period");
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
            entity.Property(e => e.AgeMax).HasColumnName("age_max");
            entity.Property(e => e.Confirmed).HasColumnName("confirmed").HasDefaultValue(false);
            entity.Property(e => e.JobGroupId).HasColumnName("job_group_id");
            entity.HasOne(e => e.EducationLevel).WithMany().HasForeignKey(e => e.EducationLevelId);
            entity.HasOne(e => e.JobGroup).WithMany().HasForeignKey(e => e.JobGroupId);
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

        modelBuilder.Entity<DashboardWarningConfig>(entity =>
        {
            entity.ToTable("dashboard_warning_config");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Category).HasColumnName("category");
            entity.Property(e => e.Label).HasColumnName("label");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.WarnDays).HasColumnName("warn_days");
            entity.Property(e => e.EscalateDays).HasColumnName("escalate_days");
            entity.Property(e => e.SeverityBase).HasColumnName("severity_base");
            entity.Property(e => e.SeverityEscalated).HasColumnName("severity_escalated");
            entity.Property(e => e.IsDateBased).HasColumnName("is_date_based");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.TodoPriority).HasColumnName("todo_priority");
            entity.Property(e => e.WarnColor).HasColumnName("warn_color");
            entity.HasIndex(e => e.Category).IsUnique();
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
            // Walter-Vorgabe 07.06.2026: optionaler Alternativ-Code (z.B. XZ
            // für Kosovo aus Mirus). Wird beim Import zusätzlich gematcht.
            entity.Property(e => e.Code2).HasColumnName("code2");
            // ISO alpha-3 (Ausweis-Kürzel BGR/MKD/…, Walter 12.07.2026).
            entity.Property(e => e.Code3).HasColumnName("code3");
            // Walter-Vorgabe 13.06.2026: deutscher Klartextname direkt aus
            // der DB — ersetzt die statische CountryNamesDe-Fallback-Tabelle.
            entity.Property(e => e.NameDe).HasColumnName("name_de");
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
            entity.Property(e => e.IsHrTeam).HasColumnName("is_hr_team");
            entity.Property(e => e.ReceivesMirusChangeDigest).HasColumnName("receives_mirus_change_digest").HasDefaultValue(false);
            entity.Property(e => e.IsSuperAdmin).HasColumnName("is_super_admin").HasDefaultValue(false);
            entity.Property(e => e.AllowedAreas).HasColumnName("allowed_areas");
            entity.Property(e => e.LastLoginAt).HasColumnName("last_login_at");
            entity.Property(e => e.SignaturePng).HasColumnName("signature_png");
            entity.Property(e => e.Theme).HasColumnName("theme").HasMaxLength(20).HasDefaultValue("light");
            entity.Property(e => e.PreferredLanguage).HasColumnName("preferred_language").HasMaxLength(5).HasDefaultValue("de");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.MustChangePassword).HasColumnName("must_change_password").HasDefaultValue(false);
            entity.Property(e => e.FailedLoginCount).HasColumnName("failed_login_count").HasDefaultValue(0);
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
            entity.Property(e => e.IdleTimeoutMinutes).HasColumnName("idle_timeout_minutes");
            entity.Property(e => e.MaxSessionMinutes).HasColumnName("max_session_minutes");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── AppSetting (globaler Key/Value-Store) ──────────────────────────
        modelBuilder.Entity<AppSetting>(entity =>
        {
            entity.ToTable("app_setting");
            entity.HasKey(e => e.Key);
            entity.Property(e => e.Key).HasColumnName("key").HasMaxLength(100);
            entity.Property(e => e.Value).HasColumnName("value");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone");
        });

        // ── UserBranchAccess ───────────────────────────────────────────────
        modelBuilder.Entity<Arzt>(entity =>
        {
            entity.ToTable("arzt");
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Titel).HasColumnName("titel");
            entity.Property(e => e.Vorname).HasColumnName("vorname");
            entity.Property(e => e.Nachname).HasColumnName("nachname");
            entity.Property(e => e.Fachgebiet).HasColumnName("fachgebiet");
            entity.Property(e => e.PraxisName).HasColumnName("praxis_name");
            entity.Property(e => e.Strasse).HasColumnName("strasse");
            entity.Property(e => e.Plz).HasColumnName("plz");
            entity.Property(e => e.Ort).HasColumnName("ort");
            entity.Property(e => e.Telefon).HasColumnName("telefon");
            entity.Property(e => e.Email).HasColumnName("email");
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.Aktiv).HasColumnName("aktiv");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
        });

        modelBuilder.Entity<ExitSurveyResponse>(entity =>
        {
            entity.ToTable("exit_survey_response");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.ReasonsJson).HasColumnName("reasons_json");
            entity.Property(e => e.ReasonOther).HasColumnName("reason_other");
            entity.Property(e => e.AtmosphereDetail).HasColumnName("atmosphere_detail");
            entity.Property(e => e.Rating).HasColumnName("rating");
            entity.Property(e => e.Comment).HasColumnName("comment");
            entity.Property(e => e.ImproveAnswer).HasColumnName("improve_answer");
            entity.Property(e => e.ImproveThemesJson).HasColumnName("improve_themes_json");
            entity.Property(e => e.IpHash).HasColumnName("ip_hash");
        });

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
            entity.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(50);
            entity.Property(e => e.LivesInSwitzerland).HasColumnName("lives_in_switzerland");
            entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth").HasColumnType("date");
            entity.Property(e => e.DateOfDeath).HasColumnName("date_of_death").HasColumnType("date");
            entity.Property(e => e.Allowance1Until).HasColumnName("allowance_1_until").HasColumnType("date");
            entity.Property(e => e.Allowance2Until).HasColumnName("allowance_2_until").HasColumnType("date");
            entity.Property(e => e.Allowance3Until).HasColumnName("allowance_3_until").HasColumnType("date");
            entity.Property(e => e.AlternativeAddressId).HasColumnName("alternative_address_id");
            entity.Property(e => e.QstDeductibleFrom).HasColumnName("qst_deductible_from").HasColumnType("date");
            entity.Property(e => e.QstDeductibleUntil).HasColumnName("qst_deductible_until").HasColumnType("date");
            entity.Property(e => e.PermitTypeId).HasColumnName("permit_type_id");
            entity.Property(e => e.PermitExpiryDate).HasColumnName("permit_expiry_date").HasColumnType("date");
            entity.Property(e => e.ZemisNumber).HasColumnName("zemis_number").HasMaxLength(40);
            entity.Property(e => e.NationalityId).HasColumnName("nationality_id");
            // Walter-Vorgabe 13.06.2026: explizite Verknüpfung zum Beleg-Doku
            // dieses Familienmitglieds (Pass / ID / Bewilligung).
            entity.Property(e => e.DokumentId).HasColumnName("dokument_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.PermitType).WithMany().HasForeignKey(e => e.PermitTypeId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.NationalityRef).WithMany().HasForeignKey(e => e.NationalityId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── EmployeeAddress ────────────────────────────────────────────────
        // Zusatz-Adressen pro MA (Korrespondenz, Ferienwohnung, Sozialamt, ...).
        // Hauptadresse bleibt direkt am Employee (für QST/Wohnkanton-Logik).
        modelBuilder.Entity<EmployeeAddress>(entity =>
        {
            entity.ToTable("employee_address");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.AddressType).HasColumnName("address_type").HasMaxLength(50);
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(150);
            entity.Property(e => e.Street).HasColumnName("street").HasMaxLength(150);
            entity.Property(e => e.Street2).HasColumnName("street2").HasMaxLength(150);
            entity.Property(e => e.PoBox).HasColumnName("po_box").HasMaxLength(50);
            entity.Property(e => e.ZipCode).HasColumnName("zip_code").HasMaxLength(10);
            entity.Property(e => e.City).HasColumnName("city").HasMaxLength(100);
            entity.Property(e => e.BfsNumber).HasColumnName("bfs_number").HasMaxLength(10);
            entity.Property(e => e.Canton).HasColumnName("canton").HasMaxLength(50);
            entity.Property(e => e.Country).HasColumnName("country").HasMaxLength(100);
            entity.Property(e => e.Phone).HasColumnName("phone").HasMaxLength(50);
            entity.Property(e => e.Phone2).HasColumnName("phone2").HasMaxLength(50);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(150);
            entity.Property(e => e.IncamailDisabled).HasColumnName("incamail_disabled");
            // Walter-Vorgabe 30.06.2026: Lokalzeit + timestamp without time zone.
            // Ohne HasColumnType mappt Npgsql 8 DateTime als timestamptz und
            // lehnt DateTime.Now (Kind=Local) ab → 500 beim Speichern.
            entity.Property(e => e.CreatedAt).HasColumnName("created_at")
                .HasColumnType("timestamp without time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at")
                .HasColumnType("timestamp without time zone");
            entity.HasIndex(e => e.EmployeeId);
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
        });

        // ── FamilyMemberAllowance ──────────────────────────────────────────
        // Versionierte Familienzulagen: pro (Familienmitglied, Gültigkeitsperiode)
        // ein Eintrag mit Monatsbetrag. Lebenslagen-Änderungen → neuer Eintrag
        // mit neuem ValidFrom (alter Eintrag bekommt ValidTo am Vortag).
        modelBuilder.Entity<FamilyMemberAllowance>(entity =>
        {
            entity.ToTable("family_member_allowance");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.FamilyMemberId).HasColumnName("family_member_id");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.MonthlyAmount).HasColumnName("monthly_amount").HasColumnType("numeric(10,2)");
            entity.Property(e => e.AllowanceType).HasColumnName("allowance_type").HasMaxLength(20);
            entity.Property(e => e.TarifSatzNr).HasColumnName("tarif_satz_nr");
            entity.Property(e => e.Note).HasColumnName("note").HasMaxLength(500);
            entity.Property(e => e.DokumentId).HasColumnName("dokument_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at")
                  .HasColumnType("timestamp without time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at")
                  .HasColumnType("timestamp without time zone");
            entity.HasOne(e => e.FamilyMember)
                  .WithMany(m => m.Allowances)
                  .HasForeignKey(e => e.FamilyMemberId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Dokument)
                  .WithMany()
                  .HasForeignKey(e => e.DokumentId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.FamilyMemberId);
            entity.HasIndex(e => e.DokumentId);
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
            // source-Spalte entfernt (Walter 17.06.2026) — siehe drop_employee_time_entry_source.sql
            entity.Property(e => e.EasyAtWorkTimepunchId).HasColumnName("easyatwork_timepunch_id");
            // Herkunft (Walter 21.06.2026): in welchem easy@work-Customer/Filiale gestempelt.
            entity.Property(e => e.EasyAtWorkCustomerId).HasColumnName("easyatwork_customer_id");
            entity.Property(e => e.SourceCompanyProfileId).HasColumnName("source_company_profile_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.OriginalTimeIn).HasColumnName("original_time_in").HasColumnType("timestamp without time zone");
            entity.Property(e => e.OriginalTimeOut).HasColumnName("original_time_out").HasColumnType("timestamp without time zone");
            entity.Property(e => e.OriginalComment).HasColumnName("original_comment");
            entity.Property(e => e.EditedBy).HasColumnName("edited_by").HasMaxLength(100);
            // edited_at ist in der DB „timestamp with time zone" → Npgsql 6+ verlangt
            // Kind=Utc beim Schreiben. ExtractEditorTime liefert daher UTC zurück.
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
            entity.UseXminAsConcurrencyToken();   // Optimistic Concurrency (Walter 20.05.2026): parallele Änderungen → DbUpdateConcurrencyException → 409
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
            // Natürlicher Schlüssel: EIN Saldo pro MA PRO FILIALE pro Periode.
            // company_profile_id MUSS rein — MA in mehreren Filialen hätten sonst
            // kollidierende Saldi, und der Upsert (FirstOrDefault) griffe willkürlich
            // einen (Walter-Vorgabe 20.05.2026). UNIQUE erzwingt die Eindeutigkeit.
            entity.HasIndex(e => new { e.EmployeeId, e.CompanyProfileId, e.PeriodYear, e.PeriodMonth })
                  .IsUnique()
                  .HasDatabaseName("ux_payroll_saldo_emp_branch_period");
        });

        // ── LohnKontoMapping (Kontoplan / Lohnart→Konten, Walter 22.05.2026) ──
        modelBuilder.Entity<LohnKontoMapping>(entity =>
        {
            entity.ToTable("lohn_konto_mapping");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Position).HasColumnName("position");
            entity.Property(e => e.SubPosition).HasColumnName("sub_position");
            entity.Property(e => e.Fibukonto).HasColumnName("fibukonto").HasMaxLength(10);
            entity.Property(e => e.Gegenkonto).HasColumnName("gegenkonto").HasMaxLength(10);
            entity.Property(e => e.KostenstelleNr).HasColumnName("kostenstelle_nr").HasMaxLength(10);
            entity.Property(e => e.KostenstelleName).HasColumnName("kostenstelle_name").HasMaxLength(60);
            entity.Property(e => e.Bezeichnung).HasColumnName("bezeichnung").HasMaxLength(200);
            entity.Property(e => e.IsVormonat).HasColumnName("is_vormonat").HasDefaultValue(false);
            entity.Property(e => e.SollBuchung).HasColumnName("soll_buchung").HasDefaultValue(true);
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
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
            entity.Property(e => e.VerlaengertProbezeit).HasColumnName("verlaengert_probezeit").HasDefaultValue(false);
            entity.Property(e => e.ReduziertSaldo).HasColumnName("reduziert_saldo").HasMaxLength(20);
            entity.Property(e => e.BasisStunden).HasColumnName("basis_stunden").HasMaxLength(10).HasDefaultValue("BETRIEB");
            entity.Property(e => e.LohnpositionAuszahlungCode).HasColumnName("lohnposition_auszahlung_code").HasMaxLength(20);
            entity.Property(e => e.LohnpositionKuerzungCode).HasColumnName("lohnposition_kuerzung_code").HasMaxLength(20);
            entity.Property(e => e.Pattern).HasColumnName("pattern").HasMaxLength(20).HasDefaultValue("KEIN");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order").HasDefaultValue(99);
            entity.Property(e => e.Aktiv).HasColumnName("aktiv").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.ZwischenverdienstKuerzel).HasColumnName("zwischenverdienst_kuerzel").HasMaxLength(2);
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
            entity.Property(e => e.LinkedFieldCode).HasColumnName("linked_field_code").HasMaxLength(50);
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
            // Lokalzeit — wie alle anderen timestamp-Spalten (Walter-Vorgabe 30.06.2026).
            // Ohne explizites ColumnType mappt Npgsql auf timestamptz → 500 bei Kind=Unspecified.
            entity.Property(e => e.HochgeladenAm).HasColumnName("hochgeladen_am")
                  .HasColumnType("timestamp without time zone");
            entity.Property(e => e.ErstelltAm).HasColumnName("erstellt_am").HasColumnType("timestamp without time zone");
            entity.Property(e => e.GeaendertAm).HasColumnName("geaendert_am").HasColumnType("timestamp without time zone");
            entity.Property(e => e.DateiGeaendertAm).HasColumnName("datei_geaendert_am").HasColumnType("timestamp without time zone");
            entity.Property(e => e.ZugriffAm).HasColumnName("zugriff_am").HasColumnType("timestamp without time zone");
            entity.Property(e => e.GeaendertVon).HasColumnName("geaendert_von");
            entity.Property(e => e.ZugriffVon).HasColumnName("zugriff_von");
            entity.Property(e => e.DvelopDokumentId).HasColumnName("dvelop_dokument_id").HasMaxLength(20);
            entity.HasIndex(e => e.EmployeeId);
            entity.HasIndex(e => e.DokumentTypId);
        });

        // ── MailboxDocument (Posteingang pro Filiale) ────────────────────────
        modelBuilder.Entity<MailboxDocument>(entity =>
        {
            entity.ToTable("mailbox_document");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.UploadedBy).HasColumnName("uploaded_by");
            entity.Property(e => e.UploadedAt).HasColumnName("uploaded_at")
                  .HasColumnType("timestamp without time zone");
            entity.Property(e => e.OriginalFilename).HasColumnName("original_filename");
            entity.Property(e => e.StorageFilename).HasColumnName("storage_filename");
            entity.Property(e => e.MimeType).HasColumnName("mime_type");
            entity.Property(e => e.FileSizeBytes).HasColumnName("file_size_bytes");
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.MessageBody).HasColumnName("message_body");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.NotifyUserId).HasColumnName("notify_user_id");
            entity.Property(e => e.TargetType).HasColumnName("target_type");
            entity.Property(e => e.TargetUserId).HasColumnName("target_user_id");
            entity.HasOne(e => e.CompanyProfile).WithMany().HasForeignKey(e => e.CompanyProfileId);
            entity.HasOne(e => e.Uploader).WithMany().HasForeignKey(e => e.UploadedBy);
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.NotifyUser).WithMany().HasForeignKey(e => e.NotifyUserId);
            entity.HasOne(e => e.TargetUser).WithMany().HasForeignKey(e => e.TargetUserId)
                  .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.CompanyProfileId, e.UploadedAt });
            entity.HasIndex(e => new { e.TargetType, e.TargetUserId });
        });

        // ── BranchMinWage (kommunaler Mindestlohn pro Filiale) ───────────────
        modelBuilder.Entity<BranchMinWage>(entity =>
        {
            entity.ToTable("branch_min_wage");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.AnnualSalary).HasColumnName("annual_salary").HasColumnType("numeric(10,2)");
            entity.Property(e => e.AppliesToYouth).HasColumnName("applies_to_youth");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasIndex(e => new { e.CompanyProfileId, e.ValidFrom });
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

        // ── EmployeeBvgZusatzMember ────────────────────────────────────────
        // Walter-Vorgabe 26.05.2026: versionierte BVG-Zusatz-Mitgliedschaft
        // pro MA (löst die hartcodierte EmploymentModelCode=FIX-M-Logik ab).
        modelBuilder.Entity<EmployeeBvgZusatzMember>(entity =>
        {
            entity.ToTable("employee_bvg_zusatz_member");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.CreatedBy).HasColumnName("created_by");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasIndex(e => new { e.EmployeeId, e.ValidFrom })
                  .HasDatabaseName("ix_bvg_member_emp_period");
        });

        // ── Mutterschafts-Modul (Walter 10.06.2026) ────────────────────────
        modelBuilder.Entity<PregnancyRule>(entity =>
        {
            entity.ToTable("pregnancy_rule");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Code).HasColumnName("code").HasMaxLength(30);
            entity.Property(e => e.Bezeichnung).HasColumnName("bezeichnung");
            entity.Property(e => e.Beschreibung).HasColumnName("beschreibung");
            entity.Property(e => e.Gesetz).HasColumnName("gesetz").HasMaxLength(100);
            entity.Property(e => e.BerechnungBasis).HasColumnName("berechnung_basis").HasMaxLength(20);
            entity.Property(e => e.OffsetMonate).HasColumnName("offset_monate");
            entity.Property(e => e.OffsetWochen).HasColumnName("offset_wochen");
            entity.Property(e => e.Richtung).HasColumnName("richtung").HasMaxLength(10);
            entity.Property(e => e.IstArbeitsverbot).HasColumnName("ist_arbeitsverbot");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.Aktiv).HasColumnName("aktiv");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            // Variante B (10.06.2026): Phasen-Ende + Lohn/Staffel.
            entity.Property(e => e.BasisEnde).HasColumnName("basis_ende").HasMaxLength(20);
            entity.Property(e => e.OffsetEndeMonate).HasColumnName("offset_ende_monate");
            entity.Property(e => e.OffsetEndeWochen).HasColumnName("offset_ende_wochen");
            entity.Property(e => e.RichtungEnde).HasColumnName("richtung_ende").HasMaxLength(10);
            entity.Property(e => e.LohnersatzPct).HasColumnName("lohnersatz_pct").HasColumnType("numeric(5,2)");
            entity.Property(e => e.MaxBetragProTag).HasColumnName("max_betrag_pro_tag").HasColumnType("numeric(8,2)");
            entity.Property(e => e.StaffelText).HasColumnName("staffel_text");
            entity.HasIndex(e => e.Code).IsUnique();
        });

        modelBuilder.Entity<EmployeePregnancy>(entity =>
        {
            entity.ToTable("employee_pregnancy");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.Meldedatum).HasColumnName("meldedatum").HasColumnType("date");
            entity.Property(e => e.ErrechneterTermin).HasColumnName("errechneter_termin").HasColumnType("date");
            entity.Property(e => e.Geburtsdatum).HasColumnName("geburtsdatum").HasColumnType("date");
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.ArztbestaetigungDokumentId).HasColumnName("arztbestaetigung_dokument_id");
            // Walter 20.07.2026: wie Rest des Systems — timestamp without time zone + DateTime.Now
            // (vorher TIMESTAMPTZ → Npgsql-Fehler «Cannot write DateTime with Kind=Local»).
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.ArztbestaetigungDokument).WithMany()
                .HasForeignKey(e => e.ArztbestaetigungDokumentId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.EmployeeId).HasDatabaseName("idx_pregnancy_employee");
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
        // Walter 29.07.2026: Ort = Ortschaftsname (Post), nicht politische Gemeinde.
        // Unique (plz4, ortschaftsname) — siehe reimport_swiss_location_ortschaft.sql.
        modelBuilder.Entity<SwissLocation>(entity =>
        {
            entity.ToTable("swiss_location");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Plz4).HasColumnName("plz4").HasMaxLength(4);
            entity.Property(e => e.Ortschaftsname).HasColumnName("ortschaftsname").HasMaxLength(80);
            entity.Property(e => e.Gemeindename).HasColumnName("gemeindename").HasMaxLength(80);
            entity.Property(e => e.BfsNr).HasColumnName("bfs_nr");
            entity.Property(e => e.Kantonskuerzel).HasColumnName("kantonskuerzel").HasMaxLength(2);
            entity.HasIndex(e => e.Plz4).HasDatabaseName("idx_swiss_location_plz");
            entity.HasIndex(e => new { e.Plz4, e.Ortschaftsname })
                  .IsUnique()
                  .HasDatabaseName("swiss_location_plz_ortschaft_unique");
        });

        // ── CompanyProfileSsl ──────────────────────────────────────────────
        // Eine SSL-Nummer pro (Filiale, Kanton). Eine Filiale kann mehrere SSLs
        // haben (eine pro Kanton, in dem sie QST-pflichtige MA beschäftigt).
        modelBuilder.Entity<CompanyProfileSsl>(entity =>
        {
            entity.ToTable("company_profile_ssl");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.KantonCode).HasColumnName("kanton_code").HasMaxLength(2);
            entity.Property(e => e.SslNummer).HasColumnName("ssl_nummer").HasMaxLength(30);
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung").HasMaxLength(500);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            // Eindeutigkeit: pro Filiale-Kanton-Kombination genau ein Eintrag.
            entity.HasIndex(e => new { e.CompanyProfileId, e.KantonCode }).IsUnique();
            entity.HasOne(e => e.CompanyProfile)
                  .WithMany(p => p.SslNummern)
                  .HasForeignKey(e => e.CompanyProfileId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // ── FamilienzulagenTarif ───────────────────────────────────────────
        // Kantonale FAK-Sätze, versioniert über (kanton_code, valid_from).
        // Wirkt im Lohnlauf nach company_profile.kanton_code (Standort der
        // Filiale) — NICHT nach Wohnsitz des MA wie die QST.
        modelBuilder.Entity<FamilienzulagenTarif>(entity =>
        {
            entity.ToTable("familienzulagen_tarif");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.KantonCode).HasColumnName("kanton_code").HasMaxLength(2).IsRequired();
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            entity.Property(e => e.KinderzulageSatz1).HasColumnName("kinderzulage_satz1").HasColumnType("numeric(8,2)");
            entity.Property(e => e.KinderzulageSatz2).HasColumnName("kinderzulage_satz2").HasColumnType("numeric(8,2)");
            entity.Property(e => e.KinderzulageSatz2AbAlter).HasColumnName("kinderzulage_satz2_ab_alter");
            entity.Property(e => e.AusbildungszulageSatz1).HasColumnName("ausbildungszulage_satz1").HasColumnType("numeric(8,2)");
            entity.Property(e => e.AusbildungszulageSatz2).HasColumnName("ausbildungszulage_satz2").HasColumnType("numeric(8,2)");
            entity.Property(e => e.AusbildungszulageSatz2AbAlter).HasColumnName("ausbildungszulage_satz2_ab_alter");
            entity.Property(e => e.SchwelleSatz2AnzahlKinder).HasColumnName("schwelle_satz2_anzahl_kinder");
            entity.Property(e => e.MindesterwerbseinkommenJahr).HasColumnName("mindesterwerbseinkommen_jahr").HasColumnType("numeric(10,2)");
            entity.Property(e => e.MindesterwerbseinkommenMonat).HasColumnName("mindesterwerbseinkommen_monat").HasColumnType("numeric(10,2)");
            entity.Property(e => e.GeburtszulageBetrag).HasColumnName("geburtszulage_betrag").HasColumnType("numeric(10,2)");
            entity.Property(e => e.AdoptionszulageBetrag).HasColumnName("adoptionszulage_betrag").HasColumnType("numeric(10,2)");
            entity.Property(e => e.AltersGrenzeKinder).HasColumnName("alters_grenze_kinder").HasDefaultValue(16);
            entity.Property(e => e.AltersGrenzeAusbildung).HasColumnName("alters_grenze_ausbildung").HasDefaultValue(25);
            entity.Property(e => e.Quelle).HasColumnName("quelle").HasMaxLength(200);
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.IsActive).HasColumnName("is_active").HasDefaultValue(true);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasIndex(e => new { e.KantonCode, e.ValidFrom }).IsUnique();
        });

        // ── Behoerde ───────────────────────────────────────────────────────
        modelBuilder.Entity<Behoerde>(entity =>
        {
            entity.ToTable("behoerde");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(200);
            entity.Property(e => e.Typ).HasColumnName("typ").HasMaxLength(30).HasDefaultValue("BETREIBUNGSAMT");
            entity.Property(e => e.KantonCode).HasColumnName("kanton_code").HasMaxLength(2);
            entity.Property(e => e.Adresse1).HasColumnName("adresse1").HasMaxLength(200);
            entity.Property(e => e.Adresse2).HasColumnName("adresse2").HasMaxLength(200);
            entity.Property(e => e.Adresse3).HasColumnName("adresse3").HasMaxLength(200);
            entity.Property(e => e.Plz).HasColumnName("plz").HasMaxLength(10);
            entity.Property(e => e.Ort).HasColumnName("ort").HasMaxLength(100);
            entity.Property(e => e.Telefon).HasColumnName("telefon").HasMaxLength(30);
            entity.Property(e => e.Email).HasColumnName("email").HasMaxLength(200);
            entity.Property(e => e.Kontaktperson).HasColumnName("kontaktperson").HasMaxLength(150);
            entity.Property(e => e.KontaktpersonRolle).HasColumnName("kontaktperson_rolle").HasMaxLength(100);
            entity.Property(e => e.Erreichbarkeit).HasColumnName("erreichbarkeit").HasMaxLength(150);
            entity.Property(e => e.Webseite).HasColumnName("webseite").HasMaxLength(300);
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
            entity.Property(e => e.KontoinhaberStrasse).HasColumnName("kontoinhaber_strasse").HasMaxLength(200);
            entity.Property(e => e.KontoinhaberPlz).HasColumnName("kontoinhaber_plz").HasMaxLength(20);
            entity.Property(e => e.KontoinhaberOrt).HasColumnName("kontoinhaber_ort").HasMaxLength(120);
            entity.Property(e => e.KontoinhaberLand).HasColumnName("kontoinhaber_land").HasMaxLength(2);
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
            // Tarif-Stammdaten (für Anmeldung & Tarifbestimmung)
            entity.Property(e => e.LivesInKonkubinat).HasColumnName("lives_in_konkubinat");
            entity.Property(e => e.HasJointParentalCare).HasColumnName("has_joint_parental_care");
            entity.Property(e => e.PaysAlimonyAdultChildren).HasColumnName("pays_alimony_adult_children");
            entity.Property(e => e.HasHigherIncomeThanPartner).HasColumnName("has_higher_income_than_partner");
            entity.Property(e => e.IsGrenzgaenger).HasColumnName("is_grenzgaenger");
            entity.Property(e => e.IsWochenaufenthalter).HasColumnName("is_wochenaufenthalter");
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
            entity.Property(e => e.MaxBaseMonthly).HasColumnName("max_base_monthly").HasColumnType("numeric(10,2)");
            entity.Property(e => e.MaxBaseFlatMonthly).HasColumnName("max_base_flat_monthly").HasColumnType("numeric(10,2)");
            entity.Property(e => e.MinBaseMonthly).HasColumnName("min_base_monthly").HasColumnType("numeric(10,2)");
            entity.Property(e => e.EntryThresholdYearly).HasColumnName("entry_threshold_yearly").HasColumnType("numeric(10,2)");
            entity.Property(e => e.OnlyQuellensteuer).HasColumnName("only_quellensteuer").HasDefaultValue(false);
            entity.Property(e => e.FibuPosition).HasColumnName("fibu_position");
            entity.Property(e => e.RateEmployer).HasColumnName("rate_employer").HasColumnType("numeric(6,3)");
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

        // ── PayrollPeriode ─────────────────────────────────────────────────
        modelBuilder.Entity<PayrollPeriode>(entity =>
        {
            entity.ToTable("payroll_periode");
            entity.UseXminAsConcurrencyToken();   // Optimistic Concurrency (Walter 20.05.2026)
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.Year).HasColumnName("year");
            entity.Property(e => e.Month).HasColumnName("month");
            entity.Property(e => e.PeriodFrom).HasColumnName("period_from").HasColumnType("date");
            entity.Property(e => e.PeriodTo).HasColumnName("period_to").HasColumnType("date");
            entity.Property(e => e.Label).HasColumnName("label").HasMaxLength(100);
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(40).HasDefaultValue("offen");
            entity.Property(e => e.AbgeschlossenAm).HasColumnName("abgeschlossen_am");
            entity.Property(e => e.AbgeschlossenVon).HasColumnName("abgeschlossen_von");
            entity.Property(e => e.ProvisorischAbgeschlossenAm).HasColumnName("provisorisch_abgeschlossen_am");
            entity.Property(e => e.ProvisorischAbgeschlossenVon).HasColumnName("provisorisch_abgeschlossen_von");
            entity.Property(e => e.Auszahlungsdatum).HasColumnName("auszahlungsdatum").HasColumnType("date");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.PdfFooterText).HasColumnName("pdf_footer_text");
            // Akonto-Workflow (Walter-Vorgabe 16.05.2026) — eigener Status-Strang.
            entity.Property(e => e.AkontoStatus).HasColumnName("akonto_status").HasMaxLength(30).HasDefaultValue("OFFEN");
            entity.Property(e => e.AkontoGfStartedAt).HasColumnName("akonto_gf_started_at");
            entity.Property(e => e.AkontoGfStartedBy).HasColumnName("akonto_gf_started_by");
            entity.Property(e => e.AkontoGfSentAt).HasColumnName("akonto_gf_sent_at");
            entity.Property(e => e.AkontoGfSentBy).HasColumnName("akonto_gf_sent_by");
            entity.Property(e => e.AkontoHrFreigegebenAt).HasColumnName("akonto_hr_freigegeben_at");
            entity.Property(e => e.AkontoHrFreigegebenBy).HasColumnName("akonto_hr_freigegeben_by");
            entity.Property(e => e.AkontoAusbezahltAt).HasColumnName("akonto_ausbezahlt_at");
            entity.Property(e => e.AkontoAusbezahltBy).HasColumnName("akonto_ausbezahlt_by");
            entity.Property(e => e.AkontoAuszahlungsdatum).HasColumnName("akonto_auszahlungsdatum");
            entity.Property(e => e.AkontoDtaRunId).HasColumnName("akonto_dta_run_id");
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyProfileId);
        });

        // ── PayrollPeriodeAudit ────────────────────────────────────────────
        modelBuilder.Entity<PayrollPeriodeAudit>(entity =>
        {
            entity.ToTable("payroll_periode_audit");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.PayrollPeriodeId).HasColumnName("payroll_periode_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.UserName).HasColumnName("user_name").HasMaxLength(200);
            entity.Property(e => e.Action).HasColumnName("action").HasMaxLength(40);
            entity.Property(e => e.Bemerkung).HasColumnName("bemerkung");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.HasOne(e => e.PayrollPeriode).WithMany().HasForeignKey(e => e.PayrollPeriodeId);
            entity.HasIndex(e => new { e.PayrollPeriodeId, e.CreatedAt })
                  .HasDatabaseName("idx_ppa_periode_time");
        });

        // ── AuditLog ───────────────────────────────────────────────────────
        // Walter-Vorgabe 27.05.2026: zentrales Audit fuer ALLE CRUD-Writes.
        // Wird vom AuditSaveChangesInterceptor automatisch befuellt.
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_log");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp without time zone");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.UserName).HasColumnName("user_name");
            entity.Property(e => e.UserRole).HasColumnName("user_role");
            entity.Property(e => e.EntityType).HasColumnName("entity_type");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.Action).HasColumnName("action");
            entity.Property(e => e.ChangesJson).HasColumnName("changes_json");
            entity.Property(e => e.Route).HasColumnName("route");
            entity.Property(e => e.IpAddress).HasColumnName("ip_address");
        });

        // ── PayrollSnapshot ────────────────────────────────────────────────
        modelBuilder.Entity<PayrollSnapshot>(entity =>
        {
            entity.ToTable("payroll_snapshot");
            entity.UseXminAsConcurrencyToken();   // Optimistic Concurrency (Walter 20.05.2026)
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
            entity.Property(e => e.AkontoBereitsAusbezahlt).HasColumnName("akonto_bereits_ausbezahlt").HasColumnType("numeric(10,2)").HasDefaultValue(0m);
            entity.Property(e => e.IsFinal).HasColumnName("is_final").HasDefaultValue(false);
            // 4-Augen-Workflow Walter 19.05.2026 — per-MA-Status analog AkontoZahlung
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("FREIGEGEBEN_GF");
            entity.Property(e => e.GfFreigegebenAt).HasColumnName("gf_freigegeben_at");
            entity.Property(e => e.GfFreigegebenBy).HasColumnName("gf_freigegeben_by");
            entity.Property(e => e.HrBestaetigtAt).HasColumnName("hr_bestaetigt_at");
            entity.Property(e => e.HrBestaetigtBy).HasColumnName("hr_bestaetigt_by");
            entity.Property(e => e.KommentarGf).HasColumnName("kommentar_gf");
            entity.Property(e => e.KommentarHr).HasColumnName("kommentar_hr");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Periode).WithMany(p => p.Snapshots).HasForeignKey(e => e.PayrollPeriodeId);
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasIndex(e => new { e.PayrollPeriodeId, e.EmployeeId })
                  .IsUnique().HasDatabaseName("UX_payroll_snapshot_periode_emp");
        });

        // ── AkontoTermin (Akonto-Lohn) ─────────────────────────────────────
        modelBuilder.Entity<AkontoTermin>(entity =>
        {
            entity.ToTable("akonto_termin");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.Year).HasColumnName("year");
            entity.Property(e => e.Month).HasColumnName("month");
            entity.Property(e => e.PayoutDate).HasColumnName("payout_date").HasColumnType("date");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyProfileId);
            entity.HasIndex(e => new { e.CompanyProfileId, e.Year, e.Month })
                  .IsUnique().HasDatabaseName("UX_akonto_termin_branch_year_month");
        });

        // ── AkontoZahlung (Akonto-Lohn) ────────────────────────────────────
        modelBuilder.Entity<AkontoZahlung>(entity =>
        {
            entity.ToTable("akonto_zahlung");
            entity.UseXminAsConcurrencyToken();   // Optimistic Concurrency (Walter 20.05.2026)
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.CompanyProfileId).HasColumnName("company_profile_id");
            entity.Property(e => e.PeriodYear).HasColumnName("period_year");
            entity.Property(e => e.PeriodMonth).HasColumnName("period_month");
            entity.Property(e => e.PayoutDate).HasColumnName("payout_date").HasColumnType("date");
            entity.Property(e => e.GeschaetzterBrutto).HasColumnName("geschaetzter_brutto").HasColumnType("numeric(10,2)");
            entity.Property(e => e.FeriengeldAnteil).HasColumnName("feriengeld_anteil").HasColumnType("numeric(10,2)");
            entity.Property(e => e.GeschaetzteAbzuege).HasColumnName("geschaetzte_abzuege").HasColumnType("numeric(10,2)");
            entity.Property(e => e.PfaendungAbzug).HasColumnName("pfaendung_abzug").HasColumnType("numeric(10,2)");
            entity.Property(e => e.NettoAkonto).HasColumnName("netto_akonto").HasColumnType("numeric(10,2)");
            entity.Property(e => e.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("BERECHNET");
            entity.Property(e => e.DtaRunId).HasColumnName("dta_run_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            // 4-Augen-Workflow (Walter-Vorgabe 16.05.2026) — GF-Freigabe + Korrektur-Kommentare.
            entity.Property(e => e.GfFreigegebenAt).HasColumnName("gf_freigegeben_at");
            entity.Property(e => e.GfFreigegebenBy).HasColumnName("gf_freigegeben_by");
            entity.Property(e => e.KommentarGf).HasColumnName("kommentar_gf");
            entity.Property(e => e.KommentarHr).HasColumnName("kommentar_hr");
            // Walter-Vorgabe 28.05.2026: Ausschluss-Grund + GF-Override-Flag
            entity.Property(e => e.ErrorReason).HasColumnName("error_reason");
            entity.Property(e => e.ForcePayout).HasColumnName("force_payout").HasDefaultValue(false);
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyProfileId);
            entity.HasOne(e => e.GfFreigegebenByUser).WithMany()
                  .HasForeignKey(e => e.GfFreigegebenBy).OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => new { e.CompanyProfileId, e.PeriodYear, e.PeriodMonth })
                  .HasDatabaseName("idx_akonto_zahlung_branch_period");
            entity.HasIndex(e => new { e.EmployeeId, e.PeriodYear, e.PeriodMonth })
                  .IsUnique().HasDatabaseName("UX_akonto_zahlung_emp_period");
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

        // ── EmployeePermitHistory ──────────────────────────────────────────
        modelBuilder.Entity<EmployeePermitHistory>(entity =>
        {
            entity.ToTable("employee_permit_history");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.PermitTypeId).HasColumnName("permit_type_id");
            entity.Property(e => e.ValidFrom).HasColumnName("valid_from").HasColumnType("date");
            entity.Property(e => e.ValidTo).HasColumnName("valid_to").HasColumnType("date");
            // permit_expiry_date entfernt 01.06.2026 — siehe Models/EmployeePermitHistory.cs.
            entity.Property(e => e.Note).HasColumnName("note");
            // Walter 14.06.2026: FK auf das Bewilligungs-PDF.
            entity.Property(e => e.DokumentId).HasColumnName("dokument_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.PermitType).WithMany().HasForeignKey(e => e.PermitTypeId);
        });

        // ── EmployeeVerwarnung (Walter-Vorgabe 14.07.2026) ────────────────
        modelBuilder.Entity<EmployeeVerwarnung>(entity =>
        {
            entity.ToTable("employee_verwarnung");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EmployeeId).HasColumnName("employee_id");
            entity.Property(e => e.Datum).HasColumnName("datum").HasColumnType("date");
            entity.Property(e => e.Stufe).HasColumnName("stufe");
            entity.Property(e => e.Gruende).HasColumnName("gruende");
            entity.Property(e => e.Beschreibung).HasColumnName("beschreibung");
            entity.Property(e => e.DokumentId).HasColumnName("dokument_id");
            entity.Property(e => e.Storniert).HasColumnName("storniert");
            entity.Property(e => e.StornoGrund).HasColumnName("storno_grund");
            entity.Property(e => e.ErstelltVon).HasColumnName("erstellt_von");
            entity.Property(e => e.ErstelltAm).HasColumnName("erstellt_am").HasColumnType("timestamp without time zone");
            entity.Property(e => e.GeaendertAm).HasColumnName("geaendert_am").HasColumnType("timestamp without time zone");
            entity.HasOne(e => e.Employee).WithMany().HasForeignKey(e => e.EmployeeId);
            entity.HasOne(e => e.Dokument).WithMany().HasForeignKey(e => e.DokumentId)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        // ── SmtpSetting (Singleton, Id=1) ──────────────────────────────────
        modelBuilder.Entity<SmtpSetting>(entity =>
        {
            entity.ToTable("smtp_setting");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Host).HasColumnName("host").HasMaxLength(200);
            entity.Property(e => e.Port).HasColumnName("port");
            entity.Property(e => e.Username).HasColumnName("username").HasMaxLength(200);
            entity.Property(e => e.PasswordEncrypted).HasColumnName("password_encrypted");
            entity.Property(e => e.FromName).HasColumnName("from_name").HasMaxLength(200);
            entity.Property(e => e.FromAddress).HasColumnName("from_address").HasMaxLength(200);
            entity.Property(e => e.TestRedirectTo).HasColumnName("test_redirect_to").HasMaxLength(200);
            entity.Property(e => e.SiteUrl).HasColumnName("site_url").HasMaxLength(300);
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
            entity.Property(e => e.UpdatedByUserId).HasColumnName("updated_by_user_id");
        });

        // ── DvelopSetting (Singleton, Id=1) — d.velop-API-Konfig (Walter 10.07.2026) ──
        modelBuilder.Entity<DvelopSetting>(entity =>
        {
            entity.ToTable("dvelop_setting");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(e => e.BaseUrl).HasColumnName("base_url");
            entity.Property(e => e.ApiKeyEncrypted).HasColumnName("api_key_encrypted");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp without time zone");
        });

        // ── EcallSetting (Singleton, Id=1) — eCall-SMS-Konfig ─────────────
        modelBuilder.Entity<EcallSetting>(entity =>
        {
            entity.ToTable("ecall_setting");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Enabled).HasColumnName("enabled");
            entity.Property(e => e.Username).HasColumnName("username");
            entity.Property(e => e.PasswordEncrypted).HasColumnName("password_encrypted");
            entity.Property(e => e.Sender).HasColumnName("sender");
            entity.Property(e => e.TestRedirectTo).HasColumnName("test_redirect_to");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at")
                  .HasColumnType("timestamp without time zone");
        });
    }
}