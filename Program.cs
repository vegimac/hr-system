using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Datenbank
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// JWT-Authentifizierung
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? "SchaUbHrSyStEmSeCrEtKeY2026!!SuperSecure";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddAuthorization();

// Quellensteuer-Tarifdienst (Singleton: Dateien werden einmal beim Start eingelesen)
builder.Services.AddSingleton<QuellensteuerTarifService>();
// Zwischenverdienist-PDF-Service
builder.Services.AddScoped<ZwischenverdienistPdfService>();

// Controller / API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// DB-Schema-Migrations und Seeding
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Benutzerverwaltung: neue Tabellen
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS app_user (
            id SERIAL PRIMARY KEY,
            username TEXT NOT NULL,
            email TEXT NOT NULL UNIQUE,
            password_hash TEXT NOT NULL,
            role TEXT NOT NULL DEFAULT 'user',
            is_active BOOLEAN NOT NULL DEFAULT true,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS user_branch_access (
            id SERIAL PRIMARY KEY,
            user_id INTEGER NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
            company_profile_id INTEGER NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
            UNIQUE(user_id, company_profile_id)
        );
    ");

    // Admin-User anlegen falls noch nicht vorhanden
    var adminExists = db.AppUsers.Any(u => u.Email == "walter.schaub@gmail.com");
    if (!adminExists)
    {
        var admin = new AppUser
        {
            Username = "Walter Schaub",
            Email = "walter.schaub@gmail.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin2026!"),
            Role = "admin",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.AppUsers.Add(admin);
        db.SaveChanges();
    }

    // Schema: neue Spalten
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee_import_snapshot
        ADD COLUMN IF NOT EXISTS job_title TEXT;
    ");

    // Audit-Felder für Stempelzeit-Änderungen
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee_time_entry
        ADD COLUMN IF NOT EXISTS original_time_in  TIMESTAMPTZ,
        ADD COLUMN IF NOT EXISTS original_time_out TIMESTAMPTZ,
        ADD COLUMN IF NOT EXISTS edited_by         VARCHAR(100),
        ADD COLUMN IF NOT EXISTS edited_at         TIMESTAMPTZ;
    ");

    // Nachtstunden-Grenzen im Firmenstamm
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE company_profile
        ADD COLUMN IF NOT EXISTS night_start_time VARCHAR(5) DEFAULT '00:00',
        ADD COLUMN IF NOT EXISTS night_end_time   VARCHAR(5) DEFAULT '07:00';
    ");

    // Neue Job-Gruppen: 2. Assistent, 1. Assistent, Restaurant Manager
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO job_group (code, sort_order, is_active)
        SELECT 'ASST_2', 5, true WHERE NOT EXISTS (SELECT 1 FROM job_group WHERE code = 'ASST_2');

        INSERT INTO job_group (code, sort_order, is_active)
        SELECT 'ASST_1', 6, true WHERE NOT EXISTS (SELECT 1 FROM job_group WHERE code = 'ASST_1');

        INSERT INTO job_group (code, sort_order, is_active)
        SELECT 'REST_MANAGER', 7, true WHERE NOT EXISTS (SELECT 1 FROM job_group WHERE code = 'REST_MANAGER');
    ");

    // Deutsche Bezeichnungen für neue Job-Gruppen
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO app_text (module, text_key, language_code, content, is_active)
        SELECT 'JOB_GROUP', 'ASST_2.NAME', 'de', '2. Assistent/in', true
        WHERE NOT EXISTS (SELECT 1 FROM app_text WHERE module = 'JOB_GROUP' AND text_key = 'ASST_2.NAME' AND language_code = 'de');

        INSERT INTO app_text (module, text_key, language_code, content, is_active)
        SELECT 'JOB_GROUP', 'ASST_1.NAME', 'de', '1. Assistent/in', true
        WHERE NOT EXISTS (SELECT 1 FROM app_text WHERE module = 'JOB_GROUP' AND text_key = 'ASST_1.NAME' AND language_code = 'de');

        INSERT INTO app_text (module, text_key, language_code, content, is_active)
        SELECT 'JOB_GROUP', 'REST_MANAGER.NAME', 'de', 'Restaurant Manager/in', true
        WHERE NOT EXISTS (SELECT 1 FROM app_text WHERE module = 'JOB_GROUP' AND text_key = 'REST_MANAGER.NAME' AND language_code = 'de');
    ");

    // Mindestlöhne 2026 – FIX-M / monatlich
    // 2. Assistent: Ia–IIIb = 4800, IV = 5293
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO minimum_wage_rule_new
            (job_group_code, employment_model_code, education_level_id, salary_type, amount, valid_from, is_active)
        SELECT 'ASST_2', 'FIX-M', el.id, 'monthly',
               CASE WHEN el.code = 'IV' THEN 5293.00 ELSE 4800.00 END,
               '2026-01-01', true
        FROM education_level el
        WHERE NOT EXISTS (
            SELECT 1 FROM minimum_wage_rule_new r
            WHERE r.job_group_code = 'ASST_2'
              AND r.employment_model_code = 'FIX-M'
              AND r.education_level_id = el.id
        );
    ");

    // 1. Assistent: Ia–IIIb = 5200, IV = 5293
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO minimum_wage_rule_new
            (job_group_code, employment_model_code, education_level_id, salary_type, amount, valid_from, is_active)
        SELECT 'ASST_1', 'FIX-M', el.id, 'monthly',
               CASE WHEN el.code = 'IV' THEN 5293.00 ELSE 5200.00 END,
               '2026-01-01', true
        FROM education_level el
        WHERE NOT EXISTS (
            SELECT 1 FROM minimum_wage_rule_new r
            WHERE r.job_group_code = 'ASST_1'
              AND r.employment_model_code = 'FIX-M'
              AND r.education_level_id = el.id
        );
    ");

    // Restaurant Manager: alle = 6100
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO minimum_wage_rule_new
            (job_group_code, employment_model_code, education_level_id, salary_type, amount, valid_from, is_active)
        SELECT 'REST_MANAGER', 'FIX-M', el.id, 'monthly', 6100.00, '2026-01-01', true
        FROM education_level el
        WHERE NOT EXISTS (
            SELECT 1 FROM minimum_wage_rule_new r
            WHERE r.job_group_code = 'REST_MANAGER'
              AND r.employment_model_code = 'FIX-M'
              AND r.education_level_id = el.id
        );
    ");

    // ── DeductionRule: Kategorie-Felder nachrüsten ────────────────────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE deduction_rule
        ADD COLUMN IF NOT EXISTS category_code      VARCHAR(20)  NOT NULL DEFAULT '',
        ADD COLUMN IF NOT EXISTS category_name      VARCHAR(100) NOT NULL DEFAULT '',
        ADD COLUMN IF NOT EXISTS min_age            INTEGER,
        ADD COLUMN IF NOT EXISTS max_age            INTEGER,
        ADD COLUMN IF NOT EXISTS only_quellensteuer BOOLEAN      NOT NULL DEFAULT false,
        ADD COLUMN IF NOT EXISTS freibetrag_monthly NUMERIC(10,2);
    ");

    // ── Lohnabrechnung: neue Tabellen ─────────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS deduction_rule (
            id                    SERIAL PRIMARY KEY,
            company_profile_id    INTEGER NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
            name                  VARCHAR(100) NOT NULL,
            type                  VARCHAR(20)  NOT NULL DEFAULT 'percent',
            rate                  NUMERIC(8,4) NOT NULL DEFAULT 0,
            basis_type            VARCHAR(20)  NOT NULL DEFAULT 'gross',
            coordination_deduction NUMERIC(10,2),
            valid_from            DATE NOT NULL DEFAULT '2026-01-01',
            valid_to              DATE,
            sort_order            INTEGER NOT NULL DEFAULT 99,
            is_active             BOOLEAN NOT NULL DEFAULT true
        );

        CREATE TABLE IF NOT EXISTS payroll_saldo (
            id                           SERIAL PRIMARY KEY,
            employee_id                  INTEGER NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            company_profile_id           INTEGER NOT NULL REFERENCES company_profile(id),
            period_year                  INTEGER NOT NULL,
            period_month                 INTEGER NOT NULL,
            hour_saldo                   NUMERIC(8,2)  NOT NULL DEFAULT 0,
            thirteenth_month_monthly     NUMERIC(10,2) NOT NULL DEFAULT 0,
            thirteenth_month_accumulated NUMERIC(10,2) NOT NULL DEFAULT 0,
            gross_amount                 NUMERIC(10,2) NOT NULL DEFAULT 0,
            net_amount                   NUMERIC(10,2) NOT NULL DEFAULT 0,
            status                       VARCHAR(20) NOT NULL DEFAULT 'draft',
            created_at                   TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at                   TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE UNIQUE INDEX IF NOT EXISTS IX_payroll_saldo_emp_period
            ON payroll_saldo (employee_id, period_year, period_month);
    ");

    // ── PayrollSaldo: Nacht- und Ferien-Saldo-Felder nachrüsten ──────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE payroll_saldo
        ADD COLUMN IF NOT EXISTS nacht_saldo        NUMERIC(8,2)  NOT NULL DEFAULT 0,
        ADD COLUMN IF NOT EXISTS night_hours_worked NUMERIC(8,2)  NOT NULL DEFAULT 0,
        ADD COLUMN IF NOT EXISTS ferien_geld_saldo  NUMERIC(10,2) NOT NULL DEFAULT 0,
        ADD COLUMN IF NOT EXISTS ferien_tage_saldo  NUMERIC(8,4)  NOT NULL DEFAULT 0;
    ");

    // ── Employment + Snapshot: 100%-Lohn als separate Spalte ─────────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employment
        ADD COLUMN IF NOT EXISTS monthly_salary_fte NUMERIC(10,2);
    ");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee_import_snapshot
        ADD COLUMN IF NOT EXISTS monthly_salary_fte NUMERIC(10,2);
    ");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee
        ADD COLUMN IF NOT EXISTS quellensteuer_befreit_ab DATE;
    ");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS employee_quellensteuer (
            id                          SERIAL PRIMARY KEY,
            employee_id                 INTEGER NOT NULL REFERENCES employee(id),
            valid_from                  DATE    NOT NULL,
            valid_to                    DATE,
            steuerkanton                VARCHAR(10),
            steuerkanton_name           VARCHAR(100),
            qst_gemeinde                VARCHAR(100),
            qst_gemeinde_bfs_nr         INTEGER,
            tarifvorschlag_qst          BOOLEAN NOT NULL DEFAULT true,
            tarif_code                  VARCHAR(10),
            tarif_bezeichnung           VARCHAR(200),
            anzahl_kinder               INTEGER NOT NULL DEFAULT 0,
            kirchensteuer               BOOLEAN NOT NULL DEFAULT false,
            qst_code                    VARCHAR(10),
            speziell_bewilligt          BOOLEAN NOT NULL DEFAULT false,
            kategorie                   VARCHAR(100),
            prozentsatz                 NUMERIC(5,2),
            mindestlohn_satzbestimmung  NUMERIC(10,2),
            partner_employee_id         INTEGER,
            partner_einkommen_von       DATE,
            partner_einkommen_bis       DATE,
            arbeitsort_kanton           VARCHAR(10),
            weitere_beschaeftigungen    BOOLEAN NOT NULL DEFAULT false,
            gesamtpensum_weitere_ag     NUMERIC(5,2),
            gesamteinkommen_weitere_ag  NUMERIC(10,2),
            halbfamilie                 VARCHAR(100),
            wohnsitz_ausland            VARCHAR(100),
            wohnsitzstaat               VARCHAR(10),
            adresse_ausland             VARCHAR(500),
            created_at                  TIMESTAMP,
            updated_at                  TIMESTAMP
        );
        CREATE INDEX IF NOT EXISTS IX_emp_qst_emp_valid
            ON employee_quellensteuer(employee_id, valid_from);
    ");

    // ── Zwischenverdienist: neue Spalten + Tabelle ───────────────────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee
        ADD COLUMN IF NOT EXISTS zivilstand  VARCHAR(40);
    ");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE company_profile
        ADD COLUMN IF NOT EXISTS bur_nummer      VARCHAR(20),
        ADD COLUMN IF NOT EXISTS branchen_code   VARCHAR(10),
        ADD COLUMN IF NOT EXISTS ahv_kasse       VARCHAR(100),
        ADD COLUMN IF NOT EXISTS bvg_versicherer VARCHAR(100),
        ADD COLUMN IF NOT EXISTS gav_name        VARCHAR(100),
        ADD COLUMN IF NOT EXISTS ist_gav         BOOLEAN NOT NULL DEFAULT false;
    ");
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS employee_arbeitslosigkeit (
            id                SERIAL PRIMARY KEY,
            employee_id       INTEGER NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            angemeldet_seit   DATE    NOT NULL,
            abgemeldet_am     DATE,
            rav_stelle        VARCHAR(100),
            rav_kundennummer  VARCHAR(50),
            arbeitslosenkasse VARCHAR(100),
            bemerkung         TEXT,
            created_at        TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at        TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS IX_emp_arbeitslos_emp
            ON employee_arbeitslosigkeit(employee_id);
    ");
}

// Statische Dateien / Startseite
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();