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
// KTG/UVG-Tagessatz-Service (Regel A/B nach Spezialistenvorgabe)
builder.Services.AddScoped<KtgTagessatzService>();
// Krankheits-Karenz-Service (zentrale Logik: Karenzjahr + Tag-für-Tag-Kumulation)
builder.Services.AddScoped<KarenzService>();
// Ferienanspruch-Kürzungs-Service (Art. 329b OR)
builder.Services.AddScoped<FerienKuerzungService>();
// PDF-Generator für Lohnabrechnung
builder.Services.AddScoped<PayrollPdfService>();
// Sperrfrist-Service: Kündigungsschutz nach Art. 336c OR bei AU
builder.Services.AddScoped<SperrfristService>();
// L-GAV-Beitrag: automatischer Jahresabzug nach Vertragstyp/Pensum
builder.Services.AddScoped<LgavBeitragService>();
// Bank-Lookup: IBAN → Bank-Stammdaten aus Data/bank_master.csv (SIX-Liste)
builder.Services.AddSingleton<BankLookupService>();

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
        ADD COLUMN IF NOT EXISTS original_comment  TEXT,
        ADD COLUMN IF NOT EXISTS edited_by         VARCHAR(100),
        ADD COLUMN IF NOT EXISTS edited_at         TIMESTAMPTZ;
    ");

    // Performance-Indices (für Queries pro MA + Zeitraum und Duplikat-Checks)
    db.Database.ExecuteSqlRaw(@"
        CREATE INDEX IF NOT EXISTS ix_time_entry_emp_date
            ON employee_time_entry (employee_id, entry_date);
        CREATE INDEX IF NOT EXISTS ix_time_entry_emp_timein
            ON employee_time_entry (employee_id, time_in);
    ");

    // Zeitzonen-Refactor: Stempelzeiten als `timestamp without time zone`
    // speichern (= Lokalzeit des Restaurants). Bisher als TIMESTAMPTZ, was
    // zu +1h-Offsets im UI geführt hat. Idempotent: nur konvertieren, wenn
    // die Spalte noch timestamptz ist.
    db.Database.ExecuteSqlRaw(@"
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'employee_time_entry'
                  AND column_name = 'time_in'
                  AND data_type   = 'timestamp with time zone'
            ) THEN
                ALTER TABLE employee_time_entry
                    ALTER COLUMN time_in           TYPE timestamp USING time_in           AT TIME ZONE 'UTC',
                    ALTER COLUMN time_out          TYPE timestamp USING time_out          AT TIME ZONE 'UTC',
                    ALTER COLUMN original_time_in  TYPE timestamp USING original_time_in  AT TIME ZONE 'UTC',
                    ALTER COLUMN original_time_out TYPE timestamp USING original_time_out AT TIME ZONE 'UTC';
            END IF;
        END $$;
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

    // ── job_group: is_kader-Flag + Mirus-Funktion-Aliases ─────────────────
    // Kader-Funktionen bekommen FIX-M (Kaderversicherung) bei Fix-Verträgen.
    // mirus_funktion_aliases: kommaseparierte CSV-Funktion-Strings die auf
    // diese Gruppe gemappt werden (case-insensitive).
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE job_group
        ADD COLUMN IF NOT EXISTS is_kader BOOLEAN NOT NULL DEFAULT false,
        ADD COLUMN IF NOT EXISTS mirus_funktion_aliases TEXT;
    ");

    // Seed: Kader-Flag + Mirus-Aliases (idempotent — UPDATE auch bei bestehenden)
    db.Database.ExecuteSqlRaw(@"
        UPDATE job_group SET is_kader = true,  mirus_funktion_aliases = '1st Assistant'
            WHERE code = 'ASST_1';
        UPDATE job_group SET is_kader = true,  mirus_funktion_aliases = '2nd Assistant, Assistant Trainee'
            WHERE code = 'ASST_2';
        UPDATE job_group SET is_kader = true,  mirus_funktion_aliases = 'Restaurant Manager - Niveau 1, Restaurant Manager - Niveau 2, Restaurant Manager - Neveau 1, Restaurant Manager - Neveau 2, Junior Restaurant Manager, Manager, Restaurant Manager'
            WHERE code = 'REST_MANAGER';
        UPDATE job_group SET is_kader = true,  mirus_funktion_aliases = 'Shift Coordinator'
            WHERE code = 'SHIFT_LEADER_1_6';
        UPDATE job_group SET is_kader = true,  mirus_funktion_aliases = NULL
            WHERE code = 'SHIFT_LEADER_7_PLUS';
        UPDATE job_group SET is_kader = false, mirus_funktion_aliases = 'Crew, Hostess / Host, Night Cleaner, Intern'
            WHERE code = 'CREW';
        UPDATE job_group SET is_kader = false, mirus_funktion_aliases = 'Crew Trainer, Field Trainer, Guest Experience Leader'
            WHERE code = 'HOST_CT';
        UPDATE job_group SET is_kader = false, mirus_funktion_aliases = NULL
            WHERE code = 'SWING';
    ");

    // Mindestlöhne 2026 – FIX-M / monatlich
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

    // deduction_rule entfernt – SV-Sätze nur noch über social_insurance_rate
    db.Database.ExecuteSqlRaw(@"
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
        ADD COLUMN IF NOT EXISTS monthly_salary_fte      NUMERIC(10,2),
        ADD COLUMN IF NOT EXISTS employment_percentage   NUMERIC(5,2),
        ADD COLUMN IF NOT EXISTS contract_end_date       DATE;
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

    // ── Mitarbeiter: neue Spalten ─────────────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee
        ADD COLUMN IF NOT EXISTS marital_status VARCHAR(40);
    ");

    // ── Benutzer-Filial-Zugang: Rolle, Funktion, Standard ─────────────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE user_branch_access
        ADD COLUMN IF NOT EXISTS role           VARCHAR(50),
        ADD COLUMN IF NOT EXISTS function_title VARCHAR(100),
        ADD COLUMN IF NOT EXISTS is_default     BOOLEAN NOT NULL DEFAULT false;
    ");

    // ── Benutzer: Vor-/Nachname + Telefon ─────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE app_user
        ADD COLUMN IF NOT EXISTS first_name VARCHAR(100),
        ADD COLUMN IF NOT EXISTS last_name  VARCHAR(100),
        ADD COLUMN IF NOT EXISTS phone      VARCHAR(50);
    ");

    // ── Firmenprofil: ALV-Felder ──────────────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE company_profile
        ADD COLUMN IF NOT EXISTS bur_nummer      VARCHAR(20),
        ADD COLUMN IF NOT EXISTS branchen_code   VARCHAR(10),
        ADD COLUMN IF NOT EXISTS ahv_kasse       VARCHAR(100),
        ADD COLUMN IF NOT EXISTS bvg_versicherer VARCHAR(100),
        ADD COLUMN IF NOT EXISTS gav_name        VARCHAR(100),
        ADD COLUMN IF NOT EXISTS ist_gav         BOOLEAN NOT NULL DEFAULT false;
    ");

    // ── Lohnpositionen (Lohnraster) ───────────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS lohnposition (
            id                SERIAL PRIMARY KEY,
            code              VARCHAR(20)  NOT NULL,
            bezeichnung       VARCHAR(150) NOT NULL,
            kategorie         VARCHAR(80)  NOT NULL DEFAULT '',
            typ               VARCHAR(10)  NOT NULL DEFAULT 'ZULAGE',
            ahv_alv_pflichtig BOOLEAN      NOT NULL DEFAULT true,
            nbuv_pflichtig    BOOLEAN      NOT NULL DEFAULT true,
            ktg_pflichtig     BOOLEAN      NOT NULL DEFAULT true,
            bvg_pflichtig     BOOLEAN      NOT NULL DEFAULT true,
            qst_pflichtig     BOOLEAN      NOT NULL DEFAULT true,
            lohnausweis_code  VARCHAR(20),
            sort_order        INTEGER      NOT NULL DEFAULT 99,
            is_active         BOOLEAN      NOT NULL DEFAULT true,
            created_at        TIMESTAMPTZ  NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_lohnposition_code
            ON lohnposition (code) WHERE is_active = true;
    ");

    // Seed: McDonald's Lohnraster-Positionen (wird nur eingespielt wenn Tabelle leer)
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO lohnposition
            (code, bezeichnung, kategorie, typ,
             ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig, qst_pflichtig,
             lohnausweis_code, sort_order, is_active)
        SELECT * FROM (VALUES
            -- ── Festlohn ──────────────────────────────────────────────────
            ('10.1',  'Festlohn',                    'Festlohn',     'ZULAGE', true,  true,  true,  true,  true,  'I',      10,  true),
            ('10.2',  'Festlohn Ferien',              'Festlohn',     'ZULAGE', true,  true,  true,  true,  true,  'I',      11,  true),
            ('10.3',  'Festlohn Feiertage',           'Festlohn',     'ZULAGE', true,  true,  true,  true,  true,  'I',      12,  true),
            ('10.4',  'Zusatzstunden',                'Festlohn',     'ZULAGE', true,  true,  true,  true,  true,  'I',      13,  true),
            -- ── Stundenlohn ───────────────────────────────────────────────
            ('20.1',  'Stundenlohn',                  'Stundenlohn',  'ZULAGE', true,  true,  true,  true,  true,  'I',      20,  true),
            ('20.2',  'Stundenlohn Ferien',            'Stundenlohn',  'ZULAGE', true,  true,  true,  true,  true,  'I',      21,  true),
            ('20.3',  'Stundenlohn Feiertage',         'Stundenlohn',  'ZULAGE', true,  true,  true,  true,  true,  'I',      22,  true),
            -- ── Überstunden ───────────────────────────────────────────────
            ('55.1',  'Überstunden 25%',              'Überstunden',  'ZULAGE', true,  true,  true,  true,  true,  'P',      55,  true),
            ('55.2',  'Überstunden ohne Zuschlag',    'Überstunden',  'ZULAGE', true,  true,  true,  true,  true,  'P',      56,  true),
            ('55.3',  'MTP Mehrstunden',               'Überstunden',  'ZULAGE', true,  true,  true,  true,  true,  'P',      57,  true),
            ('55.11', 'Nachtstunden 25% (00–05)',      'Überstunden',  'ZULAGE', true,  true,  true,  true,  true,  'P',      58,  true),
            ('55.12', 'Nachtstunden 50% (00–05)',      'Überstunden',  'ZULAGE', true,  true,  true,  true,  true,  'P',      59,  true),
            -- ── UVG / KTG Taggelder ───────────────────────────────────────
            ('60.1',  'UVG Karenzentschädigung',      'Taggelder',    'ZULAGE', true,  true,  true,  true,  true,  'I',      60,  true),
            ('60.3',  'UVG Taggeld',                  'Taggelder',    'ZULAGE', false, false, false, true,  true,  'Y',      63,  true),
            ('70.1',  'KTG Karenzentschädigung',      'Taggelder',    'ZULAGE', true,  true,  true,  true,  true,  'I',      70,  true),
            ('70.2',  'KTG Taggeld',                  'Taggelder',    'ZULAGE', false, false, false, true,  true,  'Y',      73,  true),
            -- ── 13. Monatslohn ────────────────────────────────────────────
            ('180.1', '13. Monatslohn',               '13. ML',       'ZULAGE', true,  true,  true,  true,  true,  'O',     180,  true),
            -- ── Familienzulagen ───────────────────────────────────────────
            ('190.1', 'Kinderzulage',                 'Familienzulagen','ZULAGE',false, false, false, false, true,  'K',     190,  true),
            ('190.2', 'Ausbildungszulage',            'Familienzulagen','ZULAGE',false, false, false, false, true,  'K',     191,  true),
            -- ── Ferienentschädigung ───────────────────────────────────────
            ('195.1', 'Ferienentschädigung 8.33%',   'Ferienentsch.','ZULAGE', true,  true,  true,  true,  true,  'I',     195,  true),
            ('195.2', 'Ferienentschädigung 10.64%',  'Ferienentsch.','ZULAGE', true,  true,  true,  true,  true,  'I',     196,  true),
            ('195.3', 'Ferienentschädigung 13.04%',  'Ferienentsch.','ZULAGE', true,  true,  true,  true,  true,  'I',     197,  true),
            -- ── Boni / Sondervergütungen ──────────────────────────────────
            ('200.5', 'McBonus',                      'Bonus',        'ZULAGE', true,  true,  true,  true,  true,  NULL,    200,  true),
            -- ── Spesen ───────────────────────────────────────────────────
            ('200.1', 'Pauschalspesen',               'Spesen',       'ZULAGE', false, false, false, false, false, '13.2.3',205,  true),
            -- ── Quellensteuer-Abzug ───────────────────────────────────────
            ('900.1', 'Quellensteuer',                'Abzüge',       'ABZUG',  false, false, false, false, false, NULL,    900,  true)
        ) AS v(code, bezeichnung, kategorie, typ,
               ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig, qst_pflichtig,
               lohnausweis_code, sort_order, is_active)
        WHERE NOT EXISTS (SELECT 1 FROM lohnposition LIMIT 1);
    ");

    // ── LohnZulage: lohnposition_id sicherstellen ───────────────────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE lohn_zulage
            ADD COLUMN IF NOT EXISTS lohnposition_id INTEGER REFERENCES lohnposition(id);
    ");

    // ── Lohnposition: 13. ML Flag ─────────────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE lohnposition
        ADD COLUMN IF NOT EXISTS dreijehnter_ml_pflichtig BOOLEAN NOT NULL DEFAULT false;
    ");
    // Seed: McBonus-Positionen auf dreijehnter_ml_pflichtig = true setzen
    // (Kategorie 'Bonus' oder Bezeichnung enthält 'Bonus'/'Prämie')
    db.Database.ExecuteSqlRaw(@"
        UPDATE lohnposition
        SET    dreijehnter_ml_pflichtig = true
        WHERE  kategorie = 'Bonus'
           AND dreijehnter_ml_pflichtig = false;
    ");

    // ── Payroll-Perioden-Konfiguration ────────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS payroll_periode_config (
            id                   SERIAL PRIMARY KEY,
            company_profile_id   INTEGER NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
            from_day             INTEGER NOT NULL DEFAULT 1,
            to_day               INTEGER NOT NULL DEFAULT 31,
            valid_from_year      INTEGER NOT NULL,
            valid_from_month     INTEGER NOT NULL DEFAULT 1,
            is_locked            BOOLEAN NOT NULL DEFAULT false,
            created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE(company_profile_id, valid_from_year, valid_from_month)
        );
    ");

    // ── Konkrete Lohnperioden ─────────────────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS payroll_periode (
            id                   SERIAL PRIMARY KEY,
            company_profile_id   INTEGER NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
            config_id            INTEGER REFERENCES payroll_periode_config(id),
            year                 INTEGER NOT NULL,
            month                INTEGER NOT NULL,
            period_from          DATE    NOT NULL,
            period_to            DATE    NOT NULL,
            label                VARCHAR(100) NOT NULL DEFAULT '',
            is_transition        BOOLEAN NOT NULL DEFAULT false,
            status               VARCHAR(20)  NOT NULL DEFAULT 'offen',
            abgeschlossen_am     TIMESTAMPTZ,
            abgeschlossen_von    INTEGER,
            created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_payroll_periode_branch_year_month
            ON payroll_periode(company_profile_id, year, month)
            WHERE is_transition = false;
    ");

    // ── Lohnzettel-Snapshots ──────────────────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS payroll_snapshot (
            id                      SERIAL PRIMARY KEY,
            payroll_periode_id      INTEGER NOT NULL REFERENCES payroll_periode(id),
            employee_id             INTEGER NOT NULL REFERENCES employee(id),
            company_profile_id      INTEGER NOT NULL REFERENCES company_profile(id),
            slip_json               JSONB   NOT NULL DEFAULT '{{}}',
            brutto                  NUMERIC(10,2) NOT NULL DEFAULT 0,
            netto                   NUMERIC(10,2) NOT NULL DEFAULT 0,
            sv_basis_ahv            NUMERIC(10,2) NOT NULL DEFAULT 0,
            sv_basis_bvg            NUMERIC(10,2) NOT NULL DEFAULT 0,
            qst_betrag              NUMERIC(10,2) NOT NULL DEFAULT 0,
            thirteenth_accumulated  NUMERIC(10,2) NOT NULL DEFAULT 0,
            ferien_geld_saldo       NUMERIC(10,2) NOT NULL DEFAULT 0,
            is_final                BOOLEAN NOT NULL DEFAULT false,
            created_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at              TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE(payroll_periode_id, employee_id)
        );
    ");

    // ── Perioden-Datumskorrrektur: falsch angelegte 1-31 Perioden neu berechnen ──
    // Wenn payroll_periode_config fehlt aber company_profile.payroll_period_start_day != 1/null,
    // wurden die Perioden mit period_from = 1. des Monats angelegt (falscher Fallback).
    // Diese Migration korrigiert period_from/period_to für alle Perioden deren Filiale
    // einen PayrollPeriodStartDay != 1 hat UND noch keine payroll_periode_config hat.
    db.Database.ExecuteSqlRaw(@"
        DO $$
        DECLARE
            r RECORD;
            sd INTEGER;
            new_from DATE;
            new_to   DATE;
        BEGIN
            FOR r IN
                SELECT pp.id, pp.company_profile_id, pp.year, pp.month,
                       pp.period_from, pp.period_to
                FROM   payroll_periode pp
                WHERE  pp.config_id IS NULL
                  AND  pp.is_transition = false
                  AND  NOT EXISTS (
                      SELECT 1 FROM payroll_snapshot ps WHERE ps.payroll_periode_id = pp.id
                  )
            LOOP
                SELECT COALESCE(cp.payroll_period_start_day, 1)
                INTO   sd
                FROM   company_profile cp
                WHERE  cp.id = r.company_profile_id;

                IF sd > 1 THEN
                    -- Datum neu berechnen
                    new_to   := make_date(r.year, r.month, sd - 1);
                    IF r.month = 1 THEN
                        new_from := make_date(r.year - 1, 12, LEAST(sd, 31));
                    ELSE
                        new_from := make_date(r.year, r.month - 1,
                                       LEAST(sd, date_part('day', (date_trunc('month', make_date(r.year, r.month, 1)) - interval '1 day'))::int));
                    END IF;
                    UPDATE payroll_periode
                    SET    period_from = new_from, period_to = new_to
                    WHERE  id = r.id;
                END IF;
            END LOOP;
        END $$;
    ");

    // ── Arbeitslosigkeit ──────────────────────────────────────────────────
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

    // ── Globale Sozialversicherungssätze ──────────────────────────────────
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS social_insurance_rate (
            id                     SERIAL PRIMARY KEY,
            code                   VARCHAR(20)  NOT NULL,
            name                   VARCHAR(100) NOT NULL,
            description            VARCHAR(200),
            rate                   NUMERIC(8,4) NOT NULL DEFAULT 0,
            basis_type             VARCHAR(20)  NOT NULL DEFAULT 'gross',
            min_age                INTEGER,
            max_age                INTEGER,
            freibetrag_monthly     NUMERIC(10,2),
            coordination_deduction NUMERIC(10,2),
            only_quellensteuer     BOOLEAN NOT NULL DEFAULT false,
            valid_from             DATE NOT NULL,
            valid_to               DATE,
            sort_order             INTEGER NOT NULL DEFAULT 99,
            is_active              BOOLEAN NOT NULL DEFAULT true,
            created_at             TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        ALTER TABLE social_insurance_rate
            ADD COLUMN IF NOT EXISTS employment_model_code VARCHAR(20);
    ");

    // Migration: alte falsche BVG-Bänder + NBUV bereinigen, falls neue Version noch nicht eingespielt
    db.Database.ExecuteSqlRaw(@"
        DELETE FROM social_insurance_rate
        WHERE valid_from = '2026-01-01'
          AND NOT EXISTS (
              SELECT 1 FROM social_insurance_rate AS chk
              WHERE chk.code = 'KTG' AND chk.valid_from = '2026-01-01'
          );
    ");

    // BVG 65+ entfernen (Rentner zahlen bei McDonald's keine BVG-Beiträge)
    db.Database.ExecuteSqlRaw(@"
        DELETE FROM social_insurance_rate
        WHERE code = 'BVG' AND min_age = 65 AND valid_from = '2026-01-01';
    ");

    // Seed v2: GastroSocial Uno Basis 2026 + Kaderlösung Zusatz (McD)
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO social_insurance_rate
            (code, name, description, rate, basis_type, employment_model_code,
             min_age, max_age, freibetrag_monthly, coordination_deduction,
             valid_from, sort_order, is_active)
        SELECT * FROM (VALUES
            -- AHV / IV / EO
            ('AHV', 'AHV / IV / EO',
             'AN-Anteil, Alter 18–64',
             5.3::numeric, 'gross', NULL::varchar,
             18, 64, NULL::numeric, NULL::numeric,
             '2026-01-01'::date, 10, true),
            ('AHV', 'AHV / IV / EO (65+)',
             'AN-Anteil ab Rentenalter – kein ALV mehr',
             5.3::numeric, 'gross', NULL::varchar,
             65, NULL, 1400.0::numeric, NULL::numeric,
             '2026-01-01'::date, 11, true),
            -- ALV
            ('ALV', 'Arbeitslosenversicherung',
             'ALV I, bis CHF 148''200/Jahr',
             1.1::numeric, 'gross', NULL::varchar,
             18, 64, NULL::numeric, NULL::numeric,
             '2026-01-01'::date, 20, true),
            -- NBUV (korrekter McDonald''s Satz 2026)
            ('NBUV', 'Nichtberufsunfallversicherung',
             'NBU-Prämie AN – McDonald''s 2026',
             1.521::numeric, 'gross', NULL::varchar,
             NULL, NULL, NULL::numeric, NULL::numeric,
             '2026-01-01'::date, 30, true),
            -- KTG (L-GAV)
            ('KTG', 'Krankentaggeldversicherung',
             'L-GAV AN-Beitrag',
             2.15::numeric, 'gross', NULL::varchar,
             NULL, NULL, NULL::numeric, NULL::numeric,
             '2026-01-01'::date, 35, true),
            -- BVG GastroSocial Uno Basis
            ('BVG', 'GastroSocial Uno Basis (18–24)',
             'Nur Risikobeitrag, kein Sparanteil',
             0.5::numeric, 'bvg_basis', NULL::varchar,
             18, 24, NULL::numeric, 2205.0::numeric,
             '2026-01-01'::date, 50, true),
            ('BVG', 'GastroSocial Uno Basis (25–64)',
             'AN-Anteil 7 %% inkl. Sparanteil – Eintrittsschwelle CHF 1''890/Mt.',
             7.0::numeric, 'bvg_basis', NULL::varchar,
             25, 64, NULL::numeric, 2205.0::numeric,
             '2026-01-01'::date, 51, true),
            -- BVG Zusatz – Uno International McD (nur FIX-M / Kader)
            ('BVG_ZUSATZ', 'Uno Int McD Zusatz (25–34)',
             'Kaderlösung: Basis = Koordinationsabzug (CHF 2''205/Mt.)',
             5.0::numeric, 'coord_deduction', 'FIX-M',
             25, 34, NULL::numeric, 2205.0::numeric,
             '2026-01-01'::date, 55, true),
            ('BVG_ZUSATZ', 'Uno Int McD Zusatz (35–44)',
             'Kaderlösung: Basis = Koordinationsabzug (CHF 2''205/Mt.)',
             6.5::numeric, 'coord_deduction', 'FIX-M',
             35, 44, NULL::numeric, 2205.0::numeric,
             '2026-01-01'::date, 56, true),
            ('BVG_ZUSATZ', 'Uno Int McD Zusatz (45–54)',
             'Kaderlösung: Basis = Koordinationsabzug (CHF 2''205/Mt.)',
             9.0::numeric, 'coord_deduction', 'FIX-M',
             45, 54, NULL::numeric, 2205.0::numeric,
             '2026-01-01'::date, 57, true),
            ('BVG_ZUSATZ', 'Uno Int McD Zusatz (55–65)',
             'Kaderlösung: Basis = Koordinationsabzug (CHF 2''205/Mt.)',
             10.5::numeric, 'coord_deduction', 'FIX-M',
             55, 65, NULL::numeric, 2205.0::numeric,
             '2026-01-01'::date, 58, true)
        ) AS v(code, name, description, rate, basis_type, employment_model_code,
               min_age, max_age, freibetrag_monthly, coordination_deduction,
               valid_from, sort_order, is_active)
        WHERE NOT EXISTS (
            SELECT 1 FROM social_insurance_rate
            WHERE code = 'KTG' AND valid_from = '2026-01-01'
        );
    ");

    // ── KTG/UVG: Karenz-Tracking + 6-Monats-Durchschnitt ───────────────────
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS krankheit_karenz_saldo (
            id                  SERIAL PRIMARY KEY,
            employee_id         INTEGER NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            company_profile_id  INTEGER NOT NULL REFERENCES company_profile(id),
            arbeitsjahr_von     DATE NOT NULL,
            arbeitsjahr_bis     DATE NOT NULL,
            karenztage_used     NUMERIC(5,2) NOT NULL DEFAULT 0,
            updated_at          TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE(employee_id, arbeitsjahr_von)
        );

        CREATE TABLE IF NOT EXISTS employee_lohn_durchschnitt (
            id                    SERIAL PRIMARY KEY,
            employee_id           INTEGER NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            company_profile_id    INTEGER NOT NULL REFERENCES company_profile(id),
            berechnet_per_year    INTEGER NOT NULL,
            berechnet_per_month   INTEGER NOT NULL,
            monate_basis          INTEGER NOT NULL,
            durchschnitt_brutto   NUMERIC(10,2) NOT NULL,
            durchschnitt_taglohn  NUMERIC(10,2) NOT NULL,
            detail_json           TEXT NOT NULL DEFAULT '[]',
            updated_at            TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            UNIQUE(employee_id, company_profile_id, berechnet_per_year, berechnet_per_month)
        );
    ");
}

// Statische Dateien / Startseite
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Bank-Master: Initial-Seed aus CSV falls DB-Tabelle leer, Cache laden
using (var scope = app.Services.CreateScope())
{
    var bankSvc = scope.ServiceProvider.GetRequiredService<BankLookupService>();
    await bankSvc.SeedFromCsvIfEmptyAsync();
    await bankSvc.ReloadAsync();
}

app.Run();