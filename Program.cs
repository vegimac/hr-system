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
// Secure-by-default (Walter-Vorgabe 20.05.2026): JEDER Controller/Endpoint
// verlangt einen authentifizierten User — AUSSER er ist explizit mit
// [AllowAnonymous] markiert (aktuell: AuthController.Login = Token-Ausgabe inkl.
// MA-Postfach-Login, WebDavController = eigene HTTP-Basic-Auth, UsersController
// Signatur-Bild für <img src>). Damit sind auch Controller OHNE explizites
// [Authorize] geschützt (vorher waren u.a. /api/payroll/* und /api/employees
// offen). [Authorize(Roles=...)] auf einzelnen Endpoints greift weiterhin
// zusätzlich. Statische Dateien (SPA, import.html, JS) laufen über
// UseStaticFiles und sind von der Policy NICHT betroffen.
builder.Services.AddAuthorization(options =>
{
    // HR-Default (Walter-Vorgabe 20.05.2026): eingeloggt UND Rolle
    // admin/superuser/user. Gilt für ALLE Endpoints mit plain [Authorize]
    // (DefaultPolicy) UND ohne jegliches Auth-Attribut (FallbackPolicy).
    // Damit ist die MA-Rolle "employee" standardmässig ausgesperrt — ein
    // Mitarbeiter mit Postfach-Login kann KEINE HR-/Lohn-Endpunkte mehr lesen.
    // "employee" wird NUR auf den explizit fürs MA-Postfach gedachten Endpoints
    // wieder zugelassen ([Authorize(Roles="admin,superuser,user,employee")] auf
    // AuthController.Me/ChangePassword + den MA-Mailbox-Methoden, die alle die
    // Eigentümerschaft selbst prüfen). Endpoints mit eigener, strengerer Policy
    // ([Authorize(Roles="admin,superuser")] o.ä.) bleiben unverändert.
    // [AllowAnonymous] (Login, WebDAV, Signatur-Bild) sticht alles.
    var hrPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole("admin", "superuser", "user")
        .Build();
    options.DefaultPolicy  = hrPolicy;
    options.FallbackPolicy = hrPolicy;
});

// Quellensteuer-Tarifdienst (Singleton: Dateien werden einmal beim Start eingelesen)
builder.Services.AddSingleton<QuellensteuerTarifService>();
// Zwischenverdienist-PDF-Service
builder.Services.AddScoped<ZwischenverdienistPdfService>();
// Quellensteuer-Anmeldeformular-PDF-Service (kantonal, aktuell SO-Template)
builder.Services.AddScoped<QstAnmeldungPdfService>();
// Lohnausweis-PDF-Service (ESTV Form 11 dfe — jährlicher Lohnausweis)
builder.Services.AddScoped<LohnausweisBarcodeService>();
builder.Services.AddScoped<LohnausweisPdfService>();
// KTG/UVG-Tagessatz-Service (Regel A/B nach Spezialistenvorgabe)
builder.Services.AddScoped<KtgTagessatzService>();
// Krankheits-Karenz-Service (zentrale Logik: Karenzjahr + Tag-für-Tag-Kumulation)
builder.Services.AddScoped<KarenzService>();
// Ferienanspruch-Kürzungs-Service (Art. 329b OR)
builder.Services.AddScoped<FerienKuerzungService>();
// PDF-Generator für Lohnabrechnung
builder.Services.AddScoped<PayrollPdfService>();
// Lohnlauf-Orchestrator (Vorab-PDF, DTA-Generierung, Vorbedingungen-Check)
builder.Services.AddScoped<LohnlaufService>();
// Akonto-Lohn-Berechnung (Vorab-Auszahlung Mitte Monat — Walter-Vorgabe).
builder.Services.AddScoped<AkontoLaufService>();
// Akonto-Zahlungsliste als PDF (Begleitliste zum DTA, Buchhaltungs-Beleg).
builder.Services.AddScoped<AkontoListePdfService>();
// Saldo-Listen zum Definitiv-Abschluss (Buchhaltung + GF) als PDF.
builder.Services.AddScoped<LohnSaldoListePdfService>();
// Fibu-Journal-Generator (Buchungsjournal aus den bestätigten Snapshots).
builder.Services.AddScoped<FibuJournalService>();
// Edit-Sperre während HR Lohnlauf prüft (Walter-Vorgabe 17.05.2026, Variante 2).
builder.Services.AddScoped<LohnEditLockService>();
// pain.001-XML-Generator (ISO 20022) für DTA-Zahlungsexport
builder.Services.AddScoped<Iso20022PainService>();
// Sperrfrist-Service: Kündigungsschutz nach Art. 336c OR bei AU
builder.Services.AddScoped<SperrfristService>();
// L-GAV-Beitrag: automatischer Jahresabzug nach Vertragstyp/Pensum
builder.Services.AddScoped<LgavBeitragService>();
builder.Services.AddScoped<PayrollCalculationEngine>();
// Snapshot-Neuberechnung (hält offene Perioden frisch — Walter-Vorgabe 22.05.2026).
builder.Services.AddScoped<SnapshotRecomputeService>();
builder.Services.AddScoped<MinimumWageCheckService>();
builder.Services.AddScoped<WageAdjustmentService>();
// Bank-Lookup: IBAN → Bank-Stammdaten aus Data/bank_master.csv (SIX-Liste)
builder.Services.AddSingleton<BankLookupService>();
// MA-Postfach: Login-Account-Verwaltung pro Mitarbeiter
builder.Services.AddScoped<EmployeePostfachService>();
// AES-Helper für verschlüsselte Secrets in der DB (z.B. SMTP-Passwort)
builder.Services.AddSingleton<SimpleAesService>();
// BFS-LSE-Export (Lohnstrukturerhebung)
builder.Services.AddScoped<LseExportService>();
// Dashboard-Cockpit (Alarme: Bewilligungen, Probezeit, Verträge, Jubiläen ...)
builder.Services.AddScoped<DashboardService>();
// SMTP-Versand für MA-Postfach-Benachrichtigungen (Lohnzettel-Bereit etc.)
builder.Services.AddScoped<EmailService>();

// Request-Size-Limits hochsetzen — Mirus-Stempelzeiten-PDFs für grosse
// Filialen können >50 MB sein. Die Kestrel-Default-Grenze (30 MB) und
// FormOptions-Default (128 MB) müssen explizit gesetzt werden, sonst
// schlagen die [RequestSizeLimit(...)]-Attribute auf den Endpoints fehl.
builder.WebHost.ConfigureKestrel(opts =>
{
    opts.Limits.MaxRequestBodySize = 300_000_000;   // 300 MB
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 300_000_000;
    o.ValueLengthLimit         = int.MaxValue;
    o.MultipartHeadersLengthLimit = int.MaxValue;
});

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

    // ── Super-Admin-Schutz (Walter-Vorgabe 15.05.2026) ────────────────────
    // Ein Super-Admin-Account kann nicht gelöscht werden, und nur ein
    // Super-Admin darf Administratoren löschen. Wird ausschliesslich per SQL
    // gesetzt — kein API-Pfad ändert dieses Flag.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE app_user
        ADD COLUMN IF NOT EXISTS is_super_admin BOOLEAN NOT NULL DEFAULT false;

        -- Walter Schaub als Super-Admin markieren (idempotent).
        UPDATE app_user
           SET is_super_admin = true
         WHERE LOWER(email) = 'walter.schaub@gmail.com'
           AND is_super_admin = false;
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

    // ── Lohnposition: ZaehltAlsBasis13ml-Default für Standard-Positionen ──
    // Damit der 13.-ML-Akkumulator die regulären Lohnarten (Festlohn,
    // Stundenlohn, Karenz etc.) automatisch in die Basis nimmt. Wirkt nur,
    // wenn das Flag noch nicht manuell gesetzt wurde (idempotent: NULL-Sicher).
    db.Database.ExecuteSqlRaw(@"
        UPDATE lohnposition
        SET    zaehlt_als_basis_13ml = true
        WHERE  code IN (
            '10',     -- Festlohn
            '2',      -- Festlohn für bezogene Ferien
            '3',      -- Festlohn für bezogene Feiertage
            '4',      -- Zusatzstunden (MTP)
            '20',     -- Stundenlohn
            '22',     -- Stundenlohn Ferien
            '50',     -- Ausbezahlte Feiertage (UTP)
            '60',     -- Unfall (Karenzentschädigung)
            '65',     -- Korrektur Unfall
            '70',     -- Krankheit (Karenzentschädigung)
            '75',     -- Korrektur Krankheit
            '195.3'   -- Ferien-Geld-Auszahlung
        )
          AND zaehlt_als_basis_13ml = false;
    ");

    // ── Konkrete Lohnperioden ─────────────────────────────────────────────
    // Walter-Vorgabe 20.05.2026: Lohnperiode = IMMER Kalendermonat. Keine
    // Periodenregel-Konfiguration, keine Übergangs-Lohnläufe mehr.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS payroll_periode (
            id                   SERIAL PRIMARY KEY,
            company_profile_id   INTEGER NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
            year                 INTEGER NOT NULL,
            month                INTEGER NOT NULL,
            period_from          DATE    NOT NULL,
            period_to            DATE    NOT NULL,
            label                VARCHAR(100) NOT NULL DEFAULT '',
            status               VARCHAR(20)  NOT NULL DEFAULT 'offen',
            abgeschlossen_am     TIMESTAMPTZ,
            abgeschlossen_von    INTEGER,
            created_at           TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS UX_payroll_periode_branch_year_month
            ON payroll_periode(company_profile_id, year, month);
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

    // ── Walter-Vorgabe 20.05.2026: Lohnperiode = IMMER Kalendermonat ──────
    // Die frühere Periodenregel (payroll_periode_config, Starttag 21/1,
    // Übergangs-Lohnläufe) ist entfernt. Beim Startup normalisieren wir die
    // Datumsgrenzen aller noch nicht eingefrorenen Perioden auf 1.–letzter Tag
    // und droppen den alten Schema-Ballast (config_id, is_transition,
    // payroll_period_start_day, payroll_periode_config).
    db.Database.ExecuteSqlRaw(@"
        -- Offene/provisorische Perioden ohne Snapshots auf Kalendermonat ziehen.
        UPDATE payroll_periode pp
        SET    period_from = make_date(pp.year, pp.month, 1),
               period_to   = (make_date(pp.year, pp.month, 1) + interval '1 month - 1 day')::date
        WHERE  NOT EXISTS (SELECT 1 FROM payroll_snapshot ps WHERE ps.payroll_periode_id = pp.id);

        -- Schema-Ballast der alten Periodenflexibilität droppen (idempotent).
        -- DROP COLUMN is_transition entfernt automatisch den partiellen
        -- UNIQUE-Index (WHERE is_transition=false), daher danach neu anlegen.
        ALTER TABLE payroll_periode  DROP COLUMN IF EXISTS config_id;
        ALTER TABLE payroll_periode  DROP COLUMN IF EXISTS is_transition;
        ALTER TABLE company_profile  DROP COLUMN IF EXISTS payroll_period_start_day;
        DROP TABLE IF EXISTS payroll_periode_config;

        -- Vollständigen UNIQUE-Index sicherstellen (1 Periode pro Filiale+Monat).
        DROP INDEX IF EXISTS UX_payroll_periode_branch_year_month;
        CREATE UNIQUE INDEX IF NOT EXISTS UX_payroll_periode_branch_year_month
            ON payroll_periode(company_profile_id, year, month);
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

    // Erst-Installations-Seed: GastroSocial Uno Basis 2026 + Kaderlösung Zusatz (McD).
    // WICHTIG: Läuft NUR wenn die Tabelle komplett leer ist (gleiches Muster wie der
    // lohnposition-Seed). Früher hing der Guard an einer einzigen Sentinel-Zeile
    // (code='KTG' AND valid_from='2026-01-01'); wurde diese Zeile gelöscht oder ihr
    // Datum im UI geändert, hat der Seed bei JEDEM Start alle 11 Sätze erneut
    // eingefügt → Dubletten. Im laufenden Betrieb werden die SV-Sätze über das UI
    // (/api/social-insurance-rates) gepflegt, NICHT über diesen Seed.
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
        WHERE NOT EXISTS (SELECT 1 FROM social_insurance_rate);
    ");

    // Schutz gegen künftige Dubletten: Unique-Index auf den fachlichen Schlüssel
    // (identisch mit dem Duplikat-Check in SocialInsuranceRatesController.Create).
    // COALESCE, weil min_age/max_age/employment_model_code NULL sein dürfen und
    // Postgres NULLs in Unique-Indizes sonst als verschieden behandelt.
    // Defensiv: wird nur angelegt wenn aktuell keine Dubletten existieren — so
    // crasht der Startup nicht, falls die Alt-Daten noch nicht bereinigt sind
    // (Bereinigung läuft einmalig über migrations-archive/fix_social_insurance_rate_dedup.sql).
    db.Database.ExecuteSqlRaw(@"
        DO $$
        BEGIN
            IF NOT EXISTS (
                SELECT 1 FROM (
                    SELECT 1 FROM social_insurance_rate
                    GROUP BY code, valid_from, COALESCE(min_age, -1),
                             COALESCE(max_age, -1), COALESCE(employment_model_code, ''),
                             basis_type, only_quellensteuer
                    HAVING COUNT(*) > 1
                ) dup
            ) THEN
                CREATE UNIQUE INDEX IF NOT EXISTS ux_social_insurance_rate_natural
                ON social_insurance_rate (
                    code, valid_from, COALESCE(min_age, -1),
                    COALESCE(max_age, -1), COALESCE(employment_model_code, ''),
                    basis_type, only_quellensteuer
                );
            END IF;
        END $$;
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

    // ── AbsenzTyp: Zwischenverdienst-Kürzel ───────────────────────────────
    // Buchstaben-Kürzel für das offizielle ALK-Zwischenverdienst-Formular.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE absenz_typ
        ADD COLUMN IF NOT EXISTS zwischenverdienst_kuerzel VARCHAR(2);
    ");

    // ── Mailbox / Posteingang pro Filiale ─────────────────────────────────
    // Geschäftsführer laden Dokumente hoch (Arztzeugnisse, unterschriebene
    // Verträge etc.), Admin/Superuser sortieren sie in die MA-Personalakte
    // (employee_dokument) ein oder löschen sie.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS mailbox_document (
            id                  SERIAL PRIMARY KEY,
            company_profile_id  INTEGER NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
            uploaded_by         INTEGER REFERENCES app_user(id) ON DELETE SET NULL,
            uploaded_at         TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            original_filename   TEXT NOT NULL,
            storage_filename    TEXT NOT NULL,
            mime_type           TEXT,
            file_size_bytes     BIGINT,
            bemerkung           TEXT,
            employee_id         INTEGER REFERENCES employee(id) ON DELETE SET NULL,
            notify_user_id      INTEGER REFERENCES app_user(id) ON DELETE SET NULL,
            CONSTRAINT mailbox_document_unique_storage UNIQUE (storage_filename)
        );
        CREATE INDEX IF NOT EXISTS IX_mailbox_branch_uploaded
            ON mailbox_document (company_profile_id, uploaded_at DESC);
    ");

    // Persönliche Stammdaten (Zivilstand-Detail + Konfession) am Mitarbeiter
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee
            ADD COLUMN IF NOT EXISTS marital_status_since           DATE,
            ADD COLUMN IF NOT EXISTS separated_since                DATE,
            ADD COLUMN IF NOT EXISTS religion                        TEXT;
    ");

    // Behoerde: zusätzliche Stammdaten für Kontaktperson + Kanton-Verknüpfung.
    // KantonCode wird gebraucht, um beim QST-Anmeldeformular automatisch das
    // Steueramt zur Kanton-spezifischen Filiale zu finden.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE behoerde
            ADD COLUMN IF NOT EXISTS kanton_code         VARCHAR(2),
            ADD COLUMN IF NOT EXISTS kontaktperson       VARCHAR(150),
            ADD COLUMN IF NOT EXISTS kontaktperson_rolle VARCHAR(100),
            ADD COLUMN IF NOT EXISTS erreichbarkeit      VARCHAR(150),
            ADD COLUMN IF NOT EXISTS webseite            VARCHAR(300);
    ");

    // Familienzulagen pro Familienmitglied, zeitlich versioniert (Von/Bis/Monatsbetrag).
    // Ersetzt die alten Allowance1/2/3Until-Slots auf employee_family_member.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS family_member_allowance (
            id                SERIAL PRIMARY KEY,
            family_member_id  INTEGER     NOT NULL REFERENCES employee_family_member(id) ON DELETE CASCADE,
            valid_from        DATE        NOT NULL,
            valid_to          DATE,
            monthly_amount    NUMERIC(10,2) NOT NULL DEFAULT 0,
            allowance_type    VARCHAR(20),
            note              TEXT,
            created_at        TIMESTAMP   NOT NULL DEFAULT NOW(),
            updated_at        TIMESTAMP   NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS ix_family_member_allowance_member
            ON family_member_allowance(family_member_id);
    ");

    // SSL-Nummern pro (Filiale, Kanton) — eigene Tabelle, weil ein Arbeitgeber
    // sich in jedem Kanton, in dem er QST-pflichtige MA beschäftigt, separat
    // anmelden muss und dort eine eigene Nummer erhält. Eine Filiale kann
    // also mehrere SSLs haben, eine pro Kanton.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS company_profile_ssl (
            id                  SERIAL PRIMARY KEY,
            company_profile_id  INTEGER     NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
            kanton_code         VARCHAR(2)  NOT NULL,
            ssl_nummer          VARCHAR(30) NOT NULL,
            bemerkung           TEXT,
            created_at          TIMESTAMP   NOT NULL DEFAULT NOW(),
            updated_at          TIMESTAMP   NOT NULL DEFAULT NOW()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ix_company_profile_ssl_filiale_kanton
            ON company_profile_ssl(company_profile_id, kanton_code);
    ");

    // Falls die alte einzelne ssl_nummer-Spalte aus früherer Iteration noch da ist:
    // Daten in die neue Tabelle migrieren (Kanton bleibt vorerst leer — Walter muss
    // einmal pro Filiale festlegen, für welchen Kanton die Nummer galt) und dann droppen.
    db.Database.ExecuteSqlRaw(@"
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                 WHERE table_name='company_profile' AND column_name='ssl_nummer'
            ) THEN
                ALTER TABLE company_profile DROP COLUMN ssl_nummer;
            END IF;
        END$$;
    ");

    // QST-Tarif-relevante Stammdaten an employee_quellensteuer (versioniert via valid_from/to)
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee_quellensteuer
            ADD COLUMN IF NOT EXISTS lives_in_konkubinat            BOOLEAN NOT NULL DEFAULT false,
            ADD COLUMN IF NOT EXISTS has_joint_parental_care        BOOLEAN NOT NULL DEFAULT false,
            ADD COLUMN IF NOT EXISTS pays_alimony_adult_children    BOOLEAN NOT NULL DEFAULT false,
            ADD COLUMN IF NOT EXISTS has_higher_income_than_partner BOOLEAN NOT NULL DEFAULT false,
            ADD COLUMN IF NOT EXISTS is_grenzgaenger                 BOOLEAN NOT NULL DEFAULT false,
            ADD COLUMN IF NOT EXISTS is_wochenaufenthalter           BOOLEAN NOT NULL DEFAULT false;
    ");

    // Falls die alten Employee-Spalten noch existieren (von früherer Version):
    // Daten in den aktuellsten QST-Eintrag des MA übertragen, dann Spalten entfernen.
    db.Database.ExecuteSqlRaw(@"
        DO $$
        DECLARE col_exists BOOLEAN;
        BEGIN
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                 WHERE table_name='employee' AND column_name='lives_in_konkubinat'
            ) INTO col_exists;
            IF col_exists THEN
                -- übertrage in den jeweils aktuellsten (kein valid_to oder neuestes) QST-Eintrag
                UPDATE employee_quellensteuer eq
                   SET lives_in_konkubinat            = e.lives_in_konkubinat,
                       has_joint_parental_care        = e.has_joint_parental_care,
                       pays_alimony_adult_children    = e.pays_alimony_adult_children,
                       has_higher_income_than_partner = e.has_higher_income_than_partner,
                       is_grenzgaenger                = e.is_grenzgaenger,
                       is_wochenaufenthalter          = e.is_wochenaufenthalter
                  FROM employee e
                 WHERE eq.employee_id = e.id
                   AND eq.id = (
                       SELECT id FROM employee_quellensteuer
                        WHERE employee_id = e.id
                        ORDER BY valid_from DESC LIMIT 1
                   );

                ALTER TABLE employee
                  DROP COLUMN IF EXISTS lives_in_konkubinat,
                  DROP COLUMN IF EXISTS has_joint_parental_care,
                  DROP COLUMN IF EXISTS pays_alimony_adult_children,
                  DROP COLUMN IF EXISTS has_higher_income_than_partner,
                  DROP COLUMN IF EXISTS is_grenzgaenger,
                  DROP COLUMN IF EXISTS is_wochenaufenthalter;
            END IF;
        END$$;
    ");

    // Erweiterung: Postfach-Typ (BRANCH/HR/ADMIN) und HR-Team-Flag für User
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE app_user
            ADD COLUMN IF NOT EXISTS is_hr_team BOOLEAN NOT NULL DEFAULT false;

        ALTER TABLE mailbox_document
            ADD COLUMN IF NOT EXISTS target_type TEXT NOT NULL DEFAULT 'BRANCH';
        CREATE INDEX IF NOT EXISTS IX_mailbox_target_type
            ON mailbox_document (target_type, uploaded_at DESC);

        -- Pat Wackernagel initial als HR-Team-Mitglied markieren (idempotent)
        UPDATE app_user
           SET is_hr_team = true
         WHERE LOWER(email) = 'pat@srgmbh.ch'
            OR LOWER(username) LIKE 'pat%walckernagel%'
            OR LOWER(username) LIKE 'patricia%walckernagel%';

        -- Bestehende Dokumente, die per notify_user_id an Admin/HR adressiert
        -- waren, in den richtigen Postfach-Typ verschieben.
        -- (Idempotent: wirkt nur auf target_type='BRANCH' und nur wenn Empfänger gesetzt.)
        UPDATE mailbox_document md
           SET target_type = 'ADMIN'
          FROM app_user u
         WHERE md.notify_user_id = u.id
           AND u.role = 'admin'
           AND md.target_type = 'BRANCH';

        UPDATE mailbox_document md
           SET target_type = 'HR'
          FROM app_user u
         WHERE md.notify_user_id = u.id
           AND u.is_hr_team = true
           AND md.target_type = 'BRANCH';
    ");

    // Seed: Default-Kürzel basierend auf Code (idempotent — nur wenn NULL)
    db.Database.ExecuteSqlRaw(@"
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'A' WHERE code = 'FERIEN'        AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'B' WHERE code = 'KRANK'         AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'B' WHERE code = 'SCHWANGER'     AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'C' WHERE code = 'UNFALL'        AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'D' WHERE code = 'MUTTER'        AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'D' WHERE code = 'VATERSCHAFT'   AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'D' WHERE code = 'BETREUUNG'     AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'E' WHERE code = 'MILITAER'      AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'E' WHERE code = 'ZIVIL'         AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'E' WHERE code = 'ZIVILSCHUTZ'   AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'F' WHERE code = 'BETRIEBSFERIEN' AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'G' WHERE code = 'UNBEZ_URLAUB'  AND zwischenverdienst_kuerzel IS NULL;
        UPDATE absenz_typ SET zwischenverdienst_kuerzel = 'G' WHERE code = 'UTP'           AND zwischenverdienst_kuerzel IS NULL;
    ");

    // Seed: Mutter-/Vaterschaftsurlaub als kombinierter Absenz-Typ (Walter-Vorgabe
    // 15.05.2026 — der Dienstplan-Code „MV" fasst beide zusammen). Verhalten wie
    // Krank: Zeitgutschrift Ja, 1/5 Arbeitstag, Basis Betrieb. Lohnpositionen
    // bleiben offen (Pattern KEIN) — die Auszahlung läuft separat über die
    // EO-Erstattung; die Stundengutschrift sorgt für korrekte Saldi.
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO absenz_typ
            (code, bezeichnung, zeitgutschrift, gutschrift_modus, utp_auszahlung,
             basis_stunden, pattern, sort_order, aktiv, zwischenverdienst_kuerzel)
        SELECT 'MUTT_VATER', 'Mutter-/Vaterschaftsurlaub', true, '1/5', false,
               'BETRIEB', 'KEIN', 45, true, 'D'
        WHERE NOT EXISTS (SELECT 1 FROM absenz_typ WHERE code = 'MUTT_VATER');
    ");

    // Seed: Frei-Kompensation (Plus-Stunden-Verbrauch) — Walter-Vorgabe
    // 15.05.2026. Dienstplan-Code „FK" = bezahlter freier Tag, der aus
    // bestehenden Plus-Stunden gespeist wird. Konfig:
    //   Zeitgutschrift = false  → keine zusätzliche Saldo-Gutschrift
    //   Pattern        = KEIN   → keine Lohn-Position (Festlohn deckt's bei FIX/MTP)
    // Effekt: Sollstunden für den Tag werden NICHT zusätzlich gutgeschrieben,
    // die Plus-Stunden im Saldo werden über die normale Soll/Ist-Differenz
    // verbraucht. Verhalten ähnlich UNBEZ_URLAUB, aber semantisch separat.
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO absenz_typ
            (code, bezeichnung, zeitgutschrift, gutschrift_modus, utp_auszahlung,
             basis_stunden, pattern, sort_order, aktiv, zwischenverdienst_kuerzel)
        SELECT 'FREI_KOMP', 'Frei-Kompensation', false, NULL, false,
               'BETRIEB', 'KEIN', 35, true, NULL
        WHERE NOT EXISTS (SELECT 1 FROM absenz_typ WHERE code = 'FREI_KOMP');
    ");

    // Seed: Bezahlte Absenz (Walter-Vorgabe 15.05.2026). Dienstplan-Code „ZF".
    // Volle Zeitgutschrift 1/5 wie ein Krankheitstag — der Tag zählt voll als
    // Soll-Stunden, ohne Lohnabzug. Sinnvoll für Arzt-/Behördentermine, Trauer-
    // tag, Hochzeit etc. Konfig spiegelt KRANK ohne Karenz-/KTG-Mechanik:
    //   Zeitgutschrift = true, Modus = 1/5, Basis = Betrieb
    //   UtpAuszahlung  = false (analog KRANK — UTP wird ggf. manuell ausbezahlt)
    //   Pattern        = KEIN  (Festlohn deckt's bei FIX/MTP, keine Lohnposition)
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO absenz_typ
            (code, bezeichnung, zeitgutschrift, gutschrift_modus, utp_auszahlung,
             basis_stunden, pattern, sort_order, aktiv, zwischenverdienst_kuerzel)
        SELECT 'BEZ_ABSENZ', 'Bezahlte Absenz', true, '1/5', false,
               'BETRIEB', 'KEIN', 37, true, NULL
        WHERE NOT EXISTS (SELECT 1 FROM absenz_typ WHERE code = 'BEZ_ABSENZ');
    ");
}

// Security-Header (Walter-Vorgabe 23.05.2026): „einfache" Härtung, gilt für ALLE
// Antworten (statische Dateien + API). Bewusst OHNE Content-Security-Policy — die
// SPA nutzt viel Inline-JS/-CSS (onclick=…, <style>), eine echte CSP käme erst mit
// einem Refactor (Handler auslagern + Nonces). Header werden ganz am Anfang der
// Pipeline gesetzt, damit nichts an der Response schon „gestartet" ist.
// Hinweis: dieselben Header NICHT zusätzlich in nginx setzen (sonst doppelt).
// `server_tokens off` (nginx-Versionsnummer verstecken) bleibt nginx-seitig.
app.Use(async (context, next) =>
{
    var h = context.Response.Headers;
    h["X-Content-Type-Options"] = "nosniff";
    h["X-Frame-Options"]        = "SAMEORIGIN";
    h["Referrer-Policy"]        = "strict-origin-when-cross-origin";
    h["Permissions-Policy"]     = "geolocation=(), microphone=(), camera=()";
    // HSTS: vom Browser nur über HTTPS beachtet, daher unbedingt setzen (nginx
    // terminiert TLS und proxyt HTTP an Kestrel — Request.IsHttps wäre hier false).
    h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    await next();
});

// Statische Dateien / Startseite
app.UseDefaultFiles();
app.UseStaticFiles();

// Optimistic-Concurrency-Handler (Walter-Vorgabe 20.05.2026): ändern zwei
// Requests dieselbe Zeile der Workflow-Tabellen (payroll_snapshot/_saldo/
// _periode, akonto_zahlung) parallel, wirft EF wegen xmin-Token-Mismatch eine
// DbUpdateConcurrencyException. Die fangen wir GLOBAL ab und liefern 409 statt
// 500 — kein still verlorenes Update mehr, das Frontend kann „bitte neu laden"
// zeigen. Muss VOR UseRouting/MapControllers stehen, damit es die Controller umhüllt.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (DbUpdateConcurrencyException)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = 409;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error   = "CONCURRENCY_CONFLICT",
                message = "Diese Daten wurden zwischenzeitlich von jemand anderem geändert. Bitte die Seite neu laden und erneut versuchen."
            });
        }
    }
});

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