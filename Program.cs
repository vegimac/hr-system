using HrSystem.Data;
using HrSystem.Models;
using Fido2NetLib;
using HrSystem.Services;
using HrSystem.Services.EasyAtWork;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// HttpContextAccessor — fuer den AuditSaveChangesInterceptor (User aus JWT).
builder.Services.AddHttpContextAccessor();
// Audit-Log-Interceptor (Walter 27.05.2026) — schreibt fuer JEDEN
// SaveChanges eine audit_log-Zeile pro geaenderter Entitaet
// (CREATE/UPDATE/DELETE). Singleton, weil zustandslos (zieht den User
// per IHttpContextAccessor pro Aufruf).
builder.Services.AddSingleton<HrSystem.Services.AuditSaveChangesInterceptor>();
// Audit-Log-Cleanup (Walter 27.05.2026): Eintraege aelter als 6 Monate
// werden automatisch geloescht. Laeuft im Hintergrund, einmal pro 24 h.
builder.Services.AddHostedService<HrSystem.Services.AuditLogCleanupService>();
// Monatliche Stempelzeiten-Aufbewahrung (Walter 21.06.2026) — separat vom Auto-Sync.
builder.Services.AddHostedService<HrSystem.Services.TimeEntryRetentionService>();

// Datenbank
// Walter-Vorgabe 13.06.2026: DB-Passwort kommt aus ENV `DB_PASSWORD`. In
// appsettings.json steht nur der Platzhalter `${DB_PASSWORD}` — wird hier
// vor dem Aufbau des DbContext ersetzt. Fehlende ENV → harter Startup-Fail.
var rawConn = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection fehlt in appsettings.json.");
var connectionString = rawConn;
if (rawConn.Contains("${DB_PASSWORD}"))
{
    var dbPwd = Environment.GetEnvironmentVariable("DB_PASSWORD")
        ?? throw new InvalidOperationException(
            "DB_PASSWORD Umgebungsvariable nicht gesetzt. Setze sie in "
            + "/etc/hr-system/env oder im Entwicklungs-Setup als Shell-ENV.");
    connectionString = rawConn.Replace("${DB_PASSWORD}", dbPwd);
}
// Include Error Detail aktivieren (Walter 29.06.2026, Diagnose): bei einer
// Constraint-Verletzung zeigt PostgreSQL dann die betroffene Spalte + den Wert
// in der Fehlermeldung, z.B. „Key (employee_number)=(580040) already exists."
// Ohne den Flag wird das Detail als „may contain sensitive data" verborgen.
// Einzelmandanten-System (nur Walter/HR) → unkritisch, hilft beim Aufspüren
// doppelter Personalnummern im easy@work-Import.
{
    var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { IncludeErrorDetail = true };
    connectionString = csb.ConnectionString;
}
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
    options.AddInterceptors(sp.GetRequiredService<HrSystem.Services.AuditSaveChangesInterceptor>());
});

// JWT-Authentifizierung
// Walter-Vorgabe 13.06.2026: KEIN hardgecodeter Fallback — Secret muss
// in appsettings.json oder als ENV `Jwt__Secret` / `JWT_SECRET` gesetzt
// sein, sonst startet die App nicht. Verhindert dass produktiv ein
// vorhersagbarer Default-Key verwendet wird.
var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? throw new InvalidOperationException(
        "JWT-Secret nicht konfiguriert. Setze Jwt:Secret in appsettings.json "
        + "oder die Umgebungsvariable JWT_SECRET (mind. 32 Zeichen).");
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
    // admin/superuser/user/buchhaltung/lowuser. Gilt für ALLE Endpoints mit
    // plain [Authorize] (DefaultPolicy) UND ohne jegliches Auth-Attribut
    // (FallbackPolicy). Damit ist die MA-Rolle "employee" standardmässig
    // ausgesperrt — ein Mitarbeiter mit Postfach-Login kann KEINE HR-/Lohn-
    // Endpunkte mehr lesen. "employee" wird NUR auf den explizit fürs MA-
    // Postfach gedachten Endpoints wieder zugelassen ([Authorize(Roles=
    // "admin,superuser,user,employee")] auf AuthController.Me/ChangePassword
    // + den MA-Mailbox-Methoden, die alle die Eigentümerschaft selbst prüfen).
    // Endpoints mit eigener, strengerer Policy ([Authorize(Roles="admin,
    // superuser")] o.ä.) bleiben unverändert.
    // [AllowAnonymous] (Login, WebDAV, Signatur-Bild) sticht alles.
    //
    // Walter-Vorgabe 14.06.2026: neue Rolle "lowuser" — eingeschränkter
    // Benutzer, der nur Mitarbeiter + Verträge + Dashboard sehen darf.
    // Wir lassen ihn in der DefaultPolicy zu (sonst kommt er nicht mal
    // ans Dashboard), Lohnlauf-Endpoints filtern ihn über ihre eigenen
    // strengeren [Authorize(Roles="admin,superuser,user")]-Attribute aus.
    // Die Frontend-Sidebar zeigt ihm Lohn-/HR-/Admin-Menüpunkte gar nicht
    // erst an — siehe startApp.
    var hrPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireRole("admin", "superuser", "user", "buchhaltung", "lowuser")
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
builder.Services.AddScoped<PregnancyPdfService>();
// Saldo-Listen zum Definitiv-Abschluss (Buchhaltung + GF) als PDF.
builder.Services.AddScoped<LohnSaldoListePdfService>();
// SECO-Formular „Eignung Schicht-/Nachtarbeit" vorausgefüllt (Walter 20.06.2026).
builder.Services.AddScoped<NachtEignungPdfService>();
// Verzicht auf medizinische Untersuchung Nachtarbeit (Beilage-Layout).
builder.Services.AddScoped<NachtVerzichtPdfService>();
// Ausnahmeregelung Tag-/Nachtarbeit (Anlage zum Arbeitsvertrag), vorausgefüllt.
builder.Services.AddScoped<NachtAusnahmePdfService>();
// Kündigungsschreiben (Walter-Vorgabe 22.06.2026).
builder.Services.AddScoped<KuendigungPdfService>();
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
// QST-Pflicht-Prüfung (CH/C/Behörde/Ehepartner → blockt Lohnlauf bei Lücke)
builder.Services.AddScoped<QstPflichtCheckService>();
// QST-Tarif-Vorschlag (Walter 14.06.2026): serverseitige Logik, die für
// neue QST-Einträge den passenden Tarif + Kinderzahl + Kirchensteuer aus
// Stammdaten ableitet und gegen die offizielle ESTV-Tariftabelle prüft.
builder.Services.AddScoped<QstTarifVorschlagService>();
// FAK-Tarif-Auflösung: pro Periode Kinderzulagen-Betrag aus Tarif + Alter (Walter 28.05.2026)
builder.Services.AddScoped<FamilienzulagenResolverService>();
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
// SMS-Versand über eCall (F24 Schweiz, REST). DB-gekoppelt → Scoped;
// nutzt IHttpClientFactory (via AddHttpClient unten registriert).
builder.Services.AddHttpClient();
builder.Services.AddScoped<EcallSmsService>();
// Word/Office → PDF-Vorschau via LibreOffice headless (Dokumentenverwaltung).
// Zustandslos → Singleton. Setzt LibreOffice auf dem Server voraus.
builder.Services.AddSingleton<OfficeToPdfService>();

// ─────────────────── WebAuthn / Passkeys (Face ID etc., Walter 01.07.2026) ───────────────────
// RP-ID + erlaubte Origins konfigurierbar (appsettings „WebAuthn" oder ENV), damit
// auf test.hr-srgmbh.ch getestet werden kann; produktiv onecrew.ch. Passkeys sind
// domaingebunden. Challenges werden kurzlebig im MemoryCache gehalten.
builder.Services.AddMemoryCache();
var webAuthnRpId  = builder.Configuration["WebAuthn:RpId"]
                    ?? Environment.GetEnvironmentVariable("WEBAUTHN_RPID")
                    ?? "onecrew.ch";
var webAuthnName  = builder.Configuration["WebAuthn:ServerName"] ?? "OneCrew";
var webAuthnOrigins = builder.Configuration.GetSection("WebAuthn:Origins").Get<string[]>()
                    ?? (Environment.GetEnvironmentVariable("WEBAUTHN_ORIGINS")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    ?? new[] { "https://onecrew.ch" };
builder.Services.AddFido2(options =>
{
    options.ServerDomain = webAuthnRpId;
    options.ServerName   = webAuthnName;
    options.Origins      = new HashSet<string>(webAuthnOrigins);
    options.TimestampDriftTolerance = 300000;
});

// ─────────────────────── easy@work API (Walter 17.06.2026) ───────────────────────
// Settings aus appsettings.json (Section "EasyAtWork") ODER aus ENV
// (EASYATWORK_CLIENT_ID / _CLIENT_SECRET / _BASE_URL). Bewusst KEIN
// hardgecodeter Fallback — wenn nichts konfiguriert ist, läuft die App
// normal weiter, der Connector-Endpoint meldet aber „nicht konfiguriert".
// Pattern identisch zu JWT-Secret + DB-Passwort + ADMIN_INIT_PASSWORD.
var eawSettings = new EasyAtWorkSettings
{
    BaseUrl      = (builder.Configuration["EasyAtWork:BaseUrl"]
                   ?? Environment.GetEnvironmentVariable("EASYATWORK_BASE_URL")
                   ?? "").TrimEnd('/'),
    ClientId     = builder.Configuration["EasyAtWork:ClientId"]
                   ?? Environment.GetEnvironmentVariable("EASYATWORK_CLIENT_ID")
                   ?? "",
    ClientSecret = builder.Configuration["EasyAtWork:ClientSecret"]
                   ?? Environment.GetEnvironmentVariable("EASYATWORK_CLIENT_SECRET")
                   ?? "",
};
builder.Services.AddSingleton(eawSettings);
// HttpClientFactory pflegen (für Connection-Pooling), aber den EasyAtWorkClient
// selbst als SINGLETON registrieren — der hält den OAuth-Token im Speicher und
// soll nicht pro Request neu instanziiert werden (sonst Token-Cache leer).
builder.Services.AddHttpClient("EasyAtWork", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    // envoy (Reverse-Proxy bei easy@work) liefert 403 ohne User-Agent (Bot-Schutz).
    // Wir identifizieren uns mit einem sprechenden UA — auch hilfreich beim Debugging
    // im easy@work-Access-Log.
    c.DefaultRequestHeaders.UserAgent.ParseAdd("hr-srgmbh-cowork/1.0 (+test.hr-srgmbh.ch)");
    c.DefaultRequestHeaders.Accept.Add(
        new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
});
builder.Services.AddSingleton<EasyAtWorkClient>(sp =>
{
    var factory = sp.GetRequiredService<IHttpClientFactory>();
    var http    = factory.CreateClient("EasyAtWork");
    return new EasyAtWorkClient(
        http,
        sp.GetRequiredService<EasyAtWorkSettings>(),
        sp.GetRequiredService<ILogger<EasyAtWorkClient>>());
});
// Stempelzeit-Sync (Phase 2) — DB-gekoppelt → Scoped.
builder.Services.AddScoped<EasyAtWorkTimepunchSyncService>();
// Mitarbeiter-Stammdaten-Sync (Phase 3.1)
builder.Services.AddScoped<EasyAtWorkEmployeeSyncService>();
// Status-Speicher für den asynchronen Filial-Import (Walter 29.06.2026).
builder.Services.AddSingleton<EasyAtWorkImportJobService>();
// Automatischer Stempelzeit-Sync (Walter-Vorgabe 19.06.2026): Orchestrator
// (Singleton, erzeugt pro Filiale eigenen Scope) + täglicher 05:00-Scheduler.
builder.Services.AddSingleton<EasyAtWorkAutoSyncRunner>();
builder.Services.AddHostedService<EasyAtWorkAutoSyncBackgroundService>();

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
    // Walter-Vorgabe 13.06.2026: Init-Passwort aus ENV ADMIN_INIT_PASSWORD.
    // Fallback "Admin2026!" bleibt — nach erstem Login MUSS Walter es eh
    // wechseln (siehe MustChangePassword-Flag). Aber für Produktion sollte
    // die ENV gesetzt sein, damit das Default-Passwort nicht im Code steht.
    var adminExists = db.AppUsers.Any(u => u.Email == "walter.schaub@gmail.com");
    if (!adminExists)
    {
        var adminInitPassword = Environment.GetEnvironmentVariable("ADMIN_INIT_PASSWORD")
                             ?? "Admin2026!";
        var admin = new AppUser
        {
            Username = "Walter Schaub",
            Email = "walter.schaub@gmail.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminInitPassword),
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

    // Herkunftsfelder für Stempelzeiten (Walter 21.06.2026): in welcher Filiale
    // (easy@work-Customer) wurde gestempelt — bleibt nachvollziehbar, auch wenn
    // der Stempel auf den Lohn-MA einer anderen Filiale gespeichert wird.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee_time_entry
        ADD COLUMN IF NOT EXISTS easyatwork_customer_id     INTEGER,
        ADD COLUMN IF NOT EXISTS source_company_profile_id  INTEGER;
    ");

    // Alte Personalnummern in eigene Tabelle (Walter 21.06.2026). Ersetzt die
    // früheren Felder employee_number_alt1/alt2. Idempotent: migriert die alten
    // Spalten in die Tabelle und droppt sie (nur solange sie noch existieren).
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS employee_number_alias (
            id          SERIAL PRIMARY KEY,
            employee_id INTEGER NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            number      TEXT NOT NULL,
            valid_from  DATE,
            valid_to    DATE,
            source      VARCHAR(50) DEFAULT 'manual',
            created_at  TIMESTAMPTZ DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_emp_number_alias_number ON employee_number_alias(number);
        CREATE INDEX IF NOT EXISTS idx_emp_number_alias_emp    ON employee_number_alias(employee_id);
        DO $$
        BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_name = 'employee' AND column_name = 'employee_number_alt1') THEN
                INSERT INTO employee_number_alias (employee_id, number, source)
                SELECT id, employee_number_alt1, 'migration'
                FROM employee WHERE employee_number_alt1 IS NOT NULL AND employee_number_alt1 <> '';
                INSERT INTO employee_number_alias (employee_id, number, source)
                SELECT id, employee_number_alt2, 'migration'
                FROM employee WHERE employee_number_alt2 IS NOT NULL AND employee_number_alt2 <> '';
                ALTER TABLE employee DROP COLUMN IF EXISTS employee_number_alt1;
                ALTER TABLE employee DROP COLUMN IF EXISTS employee_number_alt2;
            END IF;
        END $$;
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

    // ── Warnungsverwaltung (Walter-Vorgabe 06.07.2026) ────────────────────
    // Globale Dashboard-Warnungs-Konfig. Idempotent: Tabelle + UNIQUE +
    // ON CONFLICT DO NOTHING. Seed = heutiges DashboardService-Verhalten.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS dashboard_warning_config (
            id                 SERIAL PRIMARY KEY,
            category           TEXT    NOT NULL UNIQUE,
            label              TEXT    NOT NULL,
            enabled            BOOLEAN NOT NULL DEFAULT TRUE,
            warn_days          INT,
            escalate_days      INT,
            severity_base      TEXT    NOT NULL DEFAULT 'warning',
            severity_escalated TEXT,
            is_date_based      BOOLEAN NOT NULL DEFAULT FALSE,
            sort_order         INT     NOT NULL DEFAULT 0
        );
        INSERT INTO dashboard_warning_config
            (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order)
        VALUES
            ('minimum_wage_violation', 'Mindestlohn unterschritten',            TRUE, NULL, NULL, 'critical', NULL,       FALSE,  1),
            ('permit_expiring',        'Bewilligung läuft ab',                  TRUE,   60,   30, 'warning',  'critical', TRUE,   2),
            ('probation_end',          'Probezeit endet',                       TRUE,   14,    7, 'info',     'warning',  TRUE,   3),
            ('contract_end',           'Befristeter Vertrag endet',             TRUE,   30,   14, 'info',     'warning',  TRUE,   4),
            ('exit_pending_active',    'Austritt erfasst, MA noch aktiv',       TRUE, NULL,   30, 'warning',  'critical', FALSE,  5),
            ('qst_pflicht_offen',      'QST-Pflicht offen (Lohnlauf gesperrt)', TRUE, NULL, NULL, 'critical', NULL,       FALSE,  6),
            ('spouse_doku_fehlt',      'Ausweis Ehepartner fehlt (QST)',        TRUE, NULL, NULL, 'critical', NULL,       FALSE,  7),
            ('employee_doku_fehlt',    'Ausweis Mitarbeiter fehlt (QST)',       TRUE, NULL, NULL, 'critical', NULL,       FALSE,  8),
            ('schwangerschaft',        'Mutterschaft / Schwangerschaft',        TRUE,   30, NULL, 'info',     'warning',  TRUE,   9),
            ('lohn_provisorisch',      'Lohn wartet auf Definitiv-Abschluss',   TRUE, NULL, NULL, 'warning',  NULL,       FALSE, 10),
            ('birthday',               'Geburtstage',                           TRUE,    7, NULL, 'info',     NULL,       TRUE,  11),
            ('anniversary',            'Dienstjubiläen',                        TRUE,   30, NULL, 'info',     NULL,       TRUE,  12),
            ('night_work_exam_expiring','Nachtarbeit-Bewilligung läuft ab',     TRUE,   30,    7, 'warning',  'critical', TRUE,  13),
            ('night_work_exam_fehlt',  'Nachtarbeit-Nachweise fehlen',          TRUE, NULL, NULL, 'critical', NULL,       FALSE, 14),
            ('night_work_exam_mismatch','Nachtarbeit-Enddatum in easy@work falsch', TRUE, NULL, NULL, 'critical', NULL,   FALSE, 15),
            ('availability_missing',   'Verfügbarkeit fehlt',                    TRUE, NULL, NULL, 'warning',  NULL,       FALSE, 16)
        ON CONFLICT (category) DO NOTHING;
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

    // ── OneCrew Moments: Tabellen + Seed (Walter 30.06./01.07.2026) ───────
    // Idempotent beim Start — Walter muss nichts in TablePlus ausführen.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS moment_page (
            id serial PRIMARY KEY,
            employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            sender_id integer,
            moment_type text NOT NULL DEFAULT '',
            title text,
            message_html text,
            token_hash text NOT NULL,
            expires_at timestamp without time zone,
            opened_at timestamp without time zone,
            responded_at timestamp without time zone,
            response_value text,
            status text NOT NULL DEFAULT 'erstellt',
            created_at timestamp without time zone NOT NULL DEFAULT now(),
            sms_text text,
            antwortart text NOT NULL DEFAULT 'lesen'
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_moment_page_token_hash ON moment_page (token_hash);
        CREATE INDEX IF NOT EXISTS ix_moment_page_employee ON moment_page (employee_id);

        CREATE TABLE IF NOT EXISTS employee_moment_consent (
            id serial PRIMARY KEY,
            employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            moments_consent_enabled boolean NOT NULL DEFAULT false,
            allow_birthday_anniversary boolean NOT NULL DEFAULT false,
            allow_appreciation boolean NOT NULL DEFAULT false,
            allow_care boolean NOT NULL DEFAULT false,
            consent_text_version text,
            granted_at timestamp without time zone,
            revoked_at timestamp without time zone,
            last_changed_at timestamp without time zone NOT NULL DEFAULT now(),
            last_changed_by text,
            source text
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_employee_moment_consent_employee ON employee_moment_consent (employee_id);

        CREATE TABLE IF NOT EXISTS moment_type (
            id serial PRIMARY KEY, code text NOT NULL, name text NOT NULL, description text,
            consent_category text NOT NULL, sort_order integer NOT NULL DEFAULT 0, is_active boolean NOT NULL DEFAULT true
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_moment_type_code ON moment_type (code);
        ALTER TABLE moment_type ADD COLUMN IF NOT EXISTS description text;

        CREATE TABLE IF NOT EXISTS moment_tone (
            id serial PRIMARY KEY, code text NOT NULL, name text NOT NULL, description text,
            sort_order integer NOT NULL DEFAULT 0, is_active boolean NOT NULL DEFAULT true
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_moment_tone_code ON moment_tone (code);
        ALTER TABLE moment_tone ADD COLUMN IF NOT EXISTS description text;

        CREATE TABLE IF NOT EXISTS moment_text (
            id serial PRIMARY KEY,
            moment_type_id integer NOT NULL REFERENCES moment_type(id) ON DELETE CASCADE,
            moment_tone_id integer NOT NULL REFERENCES moment_tone(id) ON DELETE CASCADE,
            titel text, sms_text text, body_text text NOT NULL,
            is_active boolean NOT NULL DEFAULT true, sort_order integer NOT NULL DEFAULT 0,
            language_code text DEFAULT 'de', version text, requires_review boolean NOT NULL DEFAULT true,
            created_at timestamp without time zone NOT NULL DEFAULT now(), created_by text
        );
        CREATE INDEX IF NOT EXISTS ix_moment_text_combo ON moment_text (moment_type_id, moment_tone_id);
        ALTER TABLE moment_text ADD COLUMN IF NOT EXISTS language_code text DEFAULT 'de';
        ALTER TABLE moment_text ADD COLUMN IF NOT EXISTS version text;
        ALTER TABLE moment_text ADD COLUMN IF NOT EXISTS requires_review boolean NOT NULL DEFAULT true;
    ");

    // Seed Moment-Typen + Emotionsgrade (idempotent Upsert per Code; keine geschweiften Klammern → safe in Raw-SQL)
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO moment_type (code,name,description,consent_category,sort_order,is_active) VALUES
          ('EmployeeBirthday','Geburtstag','Geburtstagsgruss an den Mitarbeitenden','birthday',1,true),
          ('WorkAnniversary','Arbeitsjubiläum','Dank zum Eintritts-/Arbeitsjubiläum','birthday',2,true),
          ('Appreciation','Danke / Wertschätzung','Persönliches Dankeschön für Einsatz oder Verhalten','appreciation',3,true),
          ('PromotionCongratulations','Gratulation','Gratulation zu Beförderung oder neuer Aufgabe','appreciation',4,true),
          ('WelcomeBackVacation','Willkommen zurück','Rückkehr aus den Ferien','care',5,true),
          ('CareHeatNotice','Fürsorge-Hinweis','Kurzer Hinweis bei Hitze oder ähnlicher Belastung','care',6,true),
          ('WelcomeBackNeutral','Schön, dass du wieder da bist','Neutrale Willkommensnachricht ohne Angabe des Grundes','care',7,true),
          ('VERTRAG_LINK','Arbeitsvertrag-Link','SMS-Vorlage für den öffentlichen Vertrags-Link. Platzhalter (in geschweiften Klammern): Vorname, Firma, Link, GueltigBis','appreciation',8,true)
        ON CONFLICT (code) DO UPDATE SET
          name = EXCLUDED.name, description = EXCLUDED.description,
          consent_category = EXCLUDED.consent_category, sort_order = EXCLUDED.sort_order, is_active = EXCLUDED.is_active;

        -- Emotionsgrade nur einfügen wenn neu (schützt spätere UI-Änderungen an Name/Beschreibung).
        INSERT INTO moment_tone (code,name,description,sort_order,is_active) VALUES
          ('Calm','Kurz & ruhig','Sehr schlicht, zurückhaltend, sachlich-warm',1,true),
          ('Warm','Herzlich','Freundlich, menschlich, aber nicht kitschig',2,true),
          ('Personal','Persönlich','Etwas wärmer und persönlicher, aber weiterhin professionell',3,true)
        ON CONFLICT (code) DO NOTHING;

        -- Frühere Platzhalter-Emotionsgrade deaktivieren (nicht löschen → keine Datenverluste)
        UPDATE moment_tone SET is_active = false WHERE code NOT IN ('Calm','Warm','Personal');
    ");

    // Seed der Text-Vorlagen (Walter-Vorgabe 01.07.2026) via EF — Platzhalter
    // {Briefanrede}/{Years} als Parameter (kein Brace-Problem in Raw-SQL). Idempotent
    // je Kombination Typ × Emotionsgrad × Version „1.0": nur einfügen wenn NICHT vorhanden
    // (spätere UI-Änderungen bleiben erhalten). Frühere Platzhalter-Texte bleiben unberührt.
    {
        var _mtTypeIds = db.MomentTypes.ToDictionary(t => t.Code, t => t.Id);
        var _mtToneIds = db.MomentTones.ToDictionary(t => t.Code, t => t.Id);
        void UpsertMomentText(string typeCode, string toneCode, string body)
        {
            if (!_mtTypeIds.TryGetValue(typeCode, out var ti)) return;
            if (!_mtToneIds.TryGetValue(toneCode, out var oi)) return;
            // Nur einfügen, wenn für diese Kombination noch keine 1.0-Vorlage existiert
            // → spätere UI-Änderungen an den Texten werden beim Neustart NICHT überschrieben.
            var exists = db.MomentTexts.Any(x => x.MomentTypeId == ti && x.MomentToneId == oi && x.Version == "1.0");
            if (exists) return;
            db.MomentTexts.Add(new MomentText {
                MomentTypeId = ti, MomentToneId = oi,
                // Signatur des Absenders am Ende (aus dem „Absender / HR"-Feld → {SenderName}).
                BodyText = body + "\n\n{SenderName}",
                LanguageCode = "de", Version = "1.0", RequiresReview = true,
                IsActive = true, SortOrder = 0, CreatedAt = DateTime.Now });
        }

        // EmployeeBirthday
        UpsertMomentText("EmployeeBirthday", "Calm",     "{Briefanrede}\nalles Gute zu deinem Geburtstag. Wir wünschen dir einen schönen Tag.");
        UpsertMomentText("EmployeeBirthday", "Warm",     "{Briefanrede}\nalles Gute zu deinem Geburtstag. Schön, dass du Teil unserer Crew bist. Wir wünschen dir einen wunderbaren Tag.");
        UpsertMomentText("EmployeeBirthday", "Personal", "{Briefanrede}\nzu deinem Geburtstag wünsche ich dir von Herzen alles Gute. Schön, dass du bei uns bist und unsere Crew mitprägst.");
        // WorkAnniversary
        UpsertMomentText("WorkAnniversary", "Calm",     "{Briefanrede}\nheute bist du seit {Years} Jahr(en) Teil unserer Crew. Vielen Dank für deinen Einsatz.");
        UpsertMomentText("WorkAnniversary", "Warm",     "{Briefanrede}\nheute bist du seit {Years} Jahr(en) bei uns. Danke für deine Treue, deinen Einsatz und dafür, dass du Teil unserer Crew bist.");
        UpsertMomentText("WorkAnniversary", "Personal", "{Briefanrede}\n{Years} Jahr(e) OneCrew. Das ist etwas Besonderes. Danke für deinen Einsatz, deine Treue und alles, was du in dieser Zeit beigetragen hast.");
        // Appreciation
        UpsertMomentText("Appreciation", "Calm",     "{Briefanrede}\nich möchte dir kurz Danke sagen. Dein Einsatz ist aufgefallen.");
        UpsertMomentText("Appreciation", "Warm",     "{Briefanrede}\nich möchte dir persönlich Danke sagen. Dein Einsatz und deine Unterstützung werden sehr geschätzt.");
        UpsertMomentText("Appreciation", "Personal", "{Briefanrede}\nich habe gesehen, wie du dich eingesetzt hast. Genau solche Momente machen unsere Crew stark. Danke dir dafür.");
        // PromotionCongratulations
        UpsertMomentText("PromotionCongratulations", "Calm",     "{Briefanrede}\nherzliche Gratulation zu deiner neuen Aufgabe. Wir wünschen dir viel Freude und Erfolg.");
        UpsertMomentText("PromotionCongratulations", "Warm",     "{Briefanrede}\nherzliche Gratulation zu deiner neuen Aufgabe. Wir freuen uns sehr für dich und wünschen dir einen guten Start.");
        UpsertMomentText("PromotionCongratulations", "Personal", "{Briefanrede}\nich freue mich sehr über deinen nächsten Schritt. Herzliche Gratulation zu deiner neuen Aufgabe. Du hast dir das verdient.");
        // WelcomeBackVacation
        UpsertMomentText("WelcomeBackVacation", "Calm",     "{Briefanrede}\nschön, dass du wieder zurück bist. Wir wünschen dir einen guten Start.");
        UpsertMomentText("WelcomeBackVacation", "Warm",     "{Briefanrede}\nwillkommen zurück. Schön, dass du wieder da bist. Wir hoffen, du konntest die freie Zeit geniessen.");
        UpsertMomentText("WelcomeBackVacation", "Personal", "{Briefanrede}\nschön, dass du wieder bei uns bist. Ich hoffe, du konntest gut abschalten und startest mit neuer Energie.");
        // CareHeatNotice
        UpsertMomentText("CareHeatNotice", "Calm",     "{Briefanrede}\nmorgen wird es sehr heiss. Bitte trink genug und achte gut auf dich.");
        UpsertMomentText("CareHeatNotice", "Warm",     "{Briefanrede}\nmorgen wird es sehr heiss. Bitte denk daran, genug zu trinken und gut auf dich und deine Crew zu achten.");
        UpsertMomentText("CareHeatNotice", "Personal", "{Briefanrede}\nmorgen wird ein heisser Tag. Bitte nimm dir bewusst Zeit zum Trinken und achte gut auf dich. Deine Gesundheit ist wichtig.");
        // WelcomeBackNeutral
        UpsertMomentText("WelcomeBackNeutral", "Calm",     "{Briefanrede}\nschön, dass du wieder da bist. Wir wünschen dir einen guten Start.");
        UpsertMomentText("WelcomeBackNeutral", "Warm",     "{Briefanrede}\nschön, dass du wieder bei uns bist. Wir freuen uns, dich wieder im Team zu haben.");
        UpsertMomentText("WelcomeBackNeutral", "Personal", "{Briefanrede}\nschön, dich wieder bei uns zu haben. Starte ruhig, und melde dich, falls du Unterstützung brauchst.");

        // VERTRAG_LINK (Walter 07.07.2026): SMS-Vorlage für den öffentlichen Vertrags-Link.
        // Hier zählt der SmsText (nicht der BodyText); Platzhalter {Vorname}/{Firma}/{Link}/{GueltigBis}
        // werden von ContractShareController.Create ersetzt. Nur einfügen, wenn für diesen Typ
        // noch KEIN Text existiert (spätere UI-Änderungen bleiben erhalten).
        if (_mtTypeIds.TryGetValue("VERTRAG_LINK", out var _vlTypeId))
        {
            var _vlToneId = _mtToneIds.TryGetValue("Calm", out var _c) ? _c
                          : db.MomentTones.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => t.Id).FirstOrDefault();
            if (_vlToneId != 0 && !db.MomentTexts.Any(x => x.MomentTypeId == _vlTypeId))
            {
                db.MomentTexts.Add(new MomentText {
                    MomentTypeId = _vlTypeId, MomentToneId = _vlToneId,
                    Titel = "Arbeitsvertrag-Link",
                    SmsText = "Hallo {Vorname}, hier ist dein Arbeitsvertrag bei {Firma}: {Link}",
                    BodyText = "Vorlage für den SMS-Text des öffentlichen Vertrags-Links. Platzhalter: {Vorname}, {Firma}, {Link}, {GueltigBis}.",
                    LanguageCode = "de", Version = "1.0", RequiresReview = false,
                    IsActive = true, SortOrder = 0, CreatedAt = DateTime.Now });
            }
        }

        db.SaveChanges();
    }

    // ── WebAuthn / Passkeys: Credential-Tabelle (Walter 01.07.2026, idempotent) ──
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS webauthn_credential (
            id             serial PRIMARY KEY,
            app_user_id    integer NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
            credential_id  bytea NOT NULL,
            public_key     bytea NOT NULL,
            sign_count     bigint NOT NULL DEFAULT 0,
            user_handle    bytea,
            transports     text,
            aaguid         text,
            device_label   text,
            created_at     timestamp without time zone NOT NULL DEFAULT now(),
            last_used_at   timestamp without time zone
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_webauthn_credential_credid ON webauthn_credential (credential_id);
        CREATE INDEX IF NOT EXISTS ix_webauthn_credential_user ON webauthn_credential (app_user_id);

        CREATE TABLE IF NOT EXISTS postfach_setup_token (
            id           serial PRIMARY KEY,
            app_user_id  integer NOT NULL REFERENCES app_user(id) ON DELETE CASCADE,
            token_hash   text NOT NULL,
            purpose      text NOT NULL DEFAULT 'onboarding',
            expires_at   timestamp without time zone NOT NULL,
            used_at      timestamp without time zone,
            created_at   timestamp without time zone NOT NULL DEFAULT now(),
            created_by   integer
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_postfach_setup_token_hash ON postfach_setup_token (token_hash);
        CREATE INDEX IF NOT EXISTS ix_postfach_setup_token_user ON postfach_setup_token (app_user_id);

        -- Öffentlicher Vertrags-Link-Token (Walter 07.07.2026) ─────────────────
        CREATE TABLE IF NOT EXISTS contract_share_token (
            id            serial PRIMARY KEY,
            employee_id   integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            employment_id integer NOT NULL,
            token_hash    text NOT NULL,
            expires_at    timestamp without time zone NOT NULL,
            used_at       timestamp without time zone,
            created_at    timestamp without time zone NOT NULL DEFAULT now(),
            created_by    integer
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_contract_share_token_hash ON contract_share_token (token_hash);
        CREATE INDEX IF NOT EXISTS ix_contract_share_token_employee ON contract_share_token (employee_id);
    ");

    // ── Verfügbare Arbeitszeiten pro MA (versioniert) — Walter 07.07.2026 ──
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS employee_availability (
            id          serial PRIMARY KEY,
            employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            type        text NOT NULL,
            valid_from  date NOT NULL,
            valid_to    date,
            bemerkung   text,
            created_at  timestamp without time zone NOT NULL DEFAULT now(),
            created_by  integer
        );
        CREATE INDEX IF NOT EXISTS ix_employee_availability_employee ON employee_availability (employee_id);

        CREATE TABLE IF NOT EXISTS employee_availability_slot (
            id              serial PRIMARY KEY,
            availability_id integer NOT NULL REFERENCES employee_availability(id) ON DELETE CASCADE,
            von             time without time zone,
            bis             time without time zone,
            mon             boolean NOT NULL DEFAULT false,
            tue             boolean NOT NULL DEFAULT false,
            wed             boolean NOT NULL DEFAULT false,
            thu             boolean NOT NULL DEFAULT false,
            fri             boolean NOT NULL DEFAULT false,
            sat             boolean NOT NULL DEFAULT false,
            sun             boolean NOT NULL DEFAULT false,
            sort_order      integer NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS ix_employee_availability_slot_avail ON employee_availability_slot (availability_id);

        -- easy@work-Sync-Quelle (Walter 09.07.2026): availability.id aus easy@work,
        -- NULL = manuell erfasst. Upsert-Schlüssel für den Verfügbarkeits-Sync.
        ALTER TABLE employee_availability ADD COLUMN IF NOT EXISTS easyatwork_availability_id bigint;
        CREATE INDEX IF NOT EXISTS ix_employee_availability_eaw ON employee_availability (easyatwork_availability_id);
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
            ADD COLUMN IF NOT EXISTS religion                        TEXT,
            ADD COLUMN IF NOT EXISTS phone2                          VARCHAR(50),
            ADD COLUMN IF NOT EXISTS maiden_name                     VARCHAR(100),
            ADD COLUMN IF NOT EXISTS short_name                      VARCHAR(100);
    ");

    // Mitarbeiter-Hauptadresse: easy@work liefert Strasse + Hausnummer in einem
    // Feld. Cowork führt das ab jetzt ebenfalls nur noch in employee.street.
    db.Database.ExecuteSqlRaw(@"
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'employee' AND column_name = 'house_number'
            ) THEN
                UPDATE employee
                SET street = trim(both from concat_ws(' ', nullif(trim(street), ''), nullif(trim(house_number), '')))
                WHERE house_number IS NOT NULL
                  AND trim(house_number) <> ''
                  AND (
                      street IS NULL OR trim(street) = ''
                      OR right(trim(street), length(trim(house_number))) <> trim(house_number)
                  );

                ALTER TABLE employee DROP COLUMN house_number;
            END IF;
        END $$;
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

    // ── Mutterschafts-Modul (Walter 10.06.2026) ─────────────────────────────
    // Globales Regelwerk (pregnancy_rule) + pro-MA-Schwangerschaften
    // (employee_pregnancy). Fristen werden im PregnancyController live aus
    // dem Regelwerk berechnet, nicht denormalisiert gespeichert.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS pregnancy_rule (
            id                SERIAL PRIMARY KEY,
            code              VARCHAR(30) NOT NULL UNIQUE,
            bezeichnung       TEXT NOT NULL,
            beschreibung      TEXT,
            gesetz            VARCHAR(100),
            berechnung_basis  VARCHAR(20) NOT NULL DEFAULT 'ET',
            offset_monate     INTEGER DEFAULT 0,
            offset_wochen     INTEGER DEFAULT 0,
            richtung          VARCHAR(10) NOT NULL DEFAULT 'VORHER',
            ist_arbeitsverbot BOOLEAN DEFAULT false,
            sort_order        INTEGER DEFAULT 99,
            aktiv             BOOLEAN DEFAULT true,
            created_at        TIMESTAMPTZ DEFAULT NOW()
        );
        -- Variante B (Walter 10.06.2026): Phasen-Ende + Lohn/Staffel.
        ALTER TABLE pregnancy_rule ADD COLUMN IF NOT EXISTS basis_ende         VARCHAR(20);
        ALTER TABLE pregnancy_rule ADD COLUMN IF NOT EXISTS offset_ende_monate INTEGER;
        ALTER TABLE pregnancy_rule ADD COLUMN IF NOT EXISTS offset_ende_wochen INTEGER;
        ALTER TABLE pregnancy_rule ADD COLUMN IF NOT EXISTS richtung_ende      VARCHAR(10);
        ALTER TABLE pregnancy_rule ADD COLUMN IF NOT EXISTS lohnersatz_pct     NUMERIC(5,2);
        ALTER TABLE pregnancy_rule ADD COLUMN IF NOT EXISTS max_betrag_pro_tag NUMERIC(8,2);
        ALTER TABLE pregnancy_rule ADD COLUMN IF NOT EXISTS staffel_text       TEXT;
        -- easy@work Sync-Log: Detail der echten Änderungen pro Lauf (Variante A, Walter 20.06.2026)
        ALTER TABLE easyatwork_sync_log ADD COLUMN IF NOT EXISTS detail_json TEXT;
        -- Benutzerbezogene Session-/Logout-Policy (Walter 21.06.2026)
        ALTER TABLE app_user ADD COLUMN IF NOT EXISTS idle_timeout_minutes integer;
        ALTER TABLE app_user ADD COLUMN IF NOT EXISTS max_session_minutes integer;
        -- Globaler Key/Value-Einstellungs-Store (Walter 21.06.2026)
        CREATE TABLE IF NOT EXISTS app_setting (
            key        text PRIMARY KEY,
            value      text NOT NULL DEFAULT '',
            updated_at timestamptz NOT NULL DEFAULT now()
        );
        -- eCall-SMS-Konfiguration (F24 Schweiz, REST). Singleton, Id=1 (Walter 07.07.2026)
        CREATE TABLE IF NOT EXISTS dvelop_setting (
            id                 integer PRIMARY KEY,
            base_url           text,
            api_key_encrypted  text,
            updated_at         timestamp without time zone NOT NULL DEFAULT now()
        );
        CREATE TABLE IF NOT EXISTS ecall_setting (
            id                 integer PRIMARY KEY,
            enabled            boolean NOT NULL DEFAULT false,
            username           text,
            password_encrypted text,
            sender             text,
            test_redirect_to   text,
            updated_at         timestamp without time zone NOT NULL DEFAULT now()
        );
        -- SMS-Test-Umleitung analog SMTP-Test-Umleitung (Walter 07.07.2026)
        ALTER TABLE ecall_setting ADD COLUMN IF NOT EXISTS test_redirect_to text;
        -- Vertrags-Link: Öffnungs-Log + manueller Widerruf (Walter 07.07.2026)
        ALTER TABLE contract_share_token ADD COLUMN IF NOT EXISTS opened_at  timestamp without time zone;
        ALTER TABLE contract_share_token ADD COLUMN IF NOT EXISTS revoked_at timestamp without time zone;
        -- Vertragsmodell-Rename UTP → FLEX (Walter 08.07.2026) — idempotent.
        -- FLEX ist der easy@work-Begriff; «UTP» war der alte Mirus-/interne Code.
        -- ACHTUNG: absenz_typ.code = 'UTP' ist ein ANDERER Namensraum
        -- (Absenz-Typ) und bleibt bewusst unangetastet.
        UPDATE employment SET employment_model = 'FLEX' WHERE employment_model = 'UTP';
        UPDATE minimum_wage_rule_new SET employment_model_code = 'FLEX' WHERE employment_model_code = 'UTP';
        UPDATE social_insurance_rate SET employment_model_code = 'FLEX' WHERE employment_model_code = 'UTP';
        UPDATE vertragstyp_lohnposition SET vertragstyp_code = 'FLEX' WHERE vertragstyp_code = 'UTP';
        UPDATE employment_model_component SET employment_model_code = 'FLEX' WHERE employment_model_code = 'UTP';
        UPDATE contract_text SET contract_types = REPLACE(contract_types, 'UTP', 'FLEX') WHERE contract_types LIKE '%UTP%';
        -- SMS-Versand-Protokoll (Walter 07.07.2026, Stufe 1)
        CREATE TABLE IF NOT EXISTS sms_log (
            id            serial PRIMARY KEY,
            created_at    timestamp without time zone NOT NULL DEFAULT now(),
            purpose       text,
            employee_id   integer,
            to_phone      text,
            redirected_to text,
            ok            boolean NOT NULL DEFAULT false,
            message_id    text,
            error         text
        );
        CREATE INDEX IF NOT EXISTS ix_sms_log_employee_purpose ON sms_log (employee_id, purpose);
        -- Nachtarbeit-Untersuchung am MA (Walter 20.06.2026, ArG)
        ALTER TABLE employee ADD COLUMN IF NOT EXISTS night_work_exam_valid_until DATE;
        ALTER TABLE employee ADD COLUMN IF NOT EXISTS night_work_exam_dokument_id INTEGER REFERENCES employee_dokument(id) ON DELETE SET NULL;
        -- Zweiter Nachtarbeit-Beleg: unterschriebene Ausnahmeregelung (Walter 22.06.2026)
        ALTER TABLE employee ADD COLUMN IF NOT EXISTS night_work_ausnahme_dokument_id INTEGER REFERENCES employee_dokument(id) ON DELETE SET NULL;
        CREATE TABLE IF NOT EXISTS employee_pregnancy (
            id                     SERIAL PRIMARY KEY,
            employee_id            INTEGER NOT NULL REFERENCES employee(id),
            meldedatum             DATE NOT NULL,
            errechneter_termin     DATE NOT NULL,
            geburtsdatum           DATE,
            bemerkung              TEXT,
            is_active              BOOLEAN DEFAULT true,
            created_at             TIMESTAMPTZ DEFAULT NOW(),
            updated_at             TIMESTAMPTZ
        );
        CREATE INDEX IF NOT EXISTS idx_pregnancy_employee ON employee_pregnancy(employee_id);
        -- Walter 10.06.2026: Altlast aus erster Version droppen (Arztzeugnisse
        -- werden über den Absenzen-Tab als KRANK erfasst, nicht doppelt hier).
        ALTER TABLE employee_pregnancy DROP COLUMN IF EXISTS arztzeugnis_vorhanden;
    ");

    // Seed: gesetzliche Default-Regeln. ON CONFLICT (code) DO NOTHING — Walter
    // kann die Regeln per UI anpassen, ohne dass der Seed sie zurücksetzt.
    // Walter-Vorgabe 10.06.2026: Default-Seed nach GastroSuisse-Merkblatt 2024.
    // ON CONFLICT DO NOTHING — vorhandene Regeln werden nicht überschrieben,
    // Walter kann sie weiter über die Admin-UI pflegen.
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO pregnancy_rule
          (code, bezeichnung, beschreibung, gesetz,
           berechnung_basis, offset_monate, offset_wochen, richtung,
           basis_ende, offset_ende_monate, offset_ende_wochen, richtung_ende,
           lohnersatz_pct, max_betrag_pro_tag, staffel_text,
           ist_arbeitsverbot, sort_order) VALUES
        ('RISIKO',           'Risiko-Assessment durchführen',
         'Gefährdungsbeurteilung am Arbeitsplatz mit der schwangeren MA. Checkliste Arbeitssicherheit Mutterschutz (GastroSuisse). Kein schweres Heben >5 kg.',
         'ArGV 1 Art. 62',
         'MELDUNG', 0, 0, 'NACHHER', 'GEBURT', 0, 0, 'NACHHER',
         NULL, NULL, NULL, false, 10),
        ('MAX_9H',           'Maximale Arbeitszeit 9 Stunden/Tag',
         'Mehr als 9 Stunden pro Tag darf nicht gearbeitet werden — auch nicht in Ausnahmesituationen.',
         'ArGV 1 Art. 60',
         'MELDUNG', 0, 0, 'NACHHER', 'GEBURT', 0, 0, 'NACHHER',
         NULL, NULL, NULL, false, 11),
        ('EINVERSTAENDNIS',  'Beschäftigung nur mit Einverständnis',
         'Schwangere und Stillende dürfen generell nur mit ihrem Einverständnis beschäftigt werden.',
         'ArG Art. 35',
         'MELDUNG', 0, 0, 'NACHHER', 'GEBURT', 0,16, 'NACHHER',
         NULL, NULL, NULL, false, 12),
        ('NACHT_WUNSCH',     'Auf Verlangen: keine Nachtarbeit (20–06 Uhr)',
         'Schwangere können verlangen, nicht zwischen 20 und 6 Uhr eingesetzt zu werden. AG muss eine gleichwertige Tagesarbeit anbieten. Ab 8 Wochen vor ET wird das Nachtverbot verpflichtend.',
         'ArG Art. 35a Abs. 4',
         'MELDUNG', 0, 0, 'NACHHER', 'ET', 0, 8, 'VORHER',
         NULL, NULL, NULL, false, 13),
        ('FERNBLEIBEN_RECHT','Recht der Schwangeren fernzubleiben',
         'Auf blosse Anzeige hin von der Arbeit fernbleiben oder die Arbeit verlassen. Ohne Arztzeugnis erhält die MA keinen Lohn (Art. 31 L-GAV).',
         'ArG Art. 35a Abs. 2',
         'MELDUNG', 0, 0, 'NACHHER', 'GEBURT', 0, 0, 'NACHHER',
         NULL, NULL, NULL, false, 14),
        ('KEIN_RAUCHERBETR', 'Kein Einsatz in Raucherbetrieben/Fumoirs',
         'Gastronomie-spezifisch: Schwangere sollten nicht in Raucherbetrieben oder Fumoirs eingesetzt werden — auch nicht mit ihrer ausdrücklichen Zustimmung (Passivrauchschutz).',
         'Passivrauchschutz-VO',
         'MELDUNG', 0, 0, 'NACHHER', 'GEBURT', 0, 0, 'NACHHER',
         NULL, NULL, NULL, false, 15),
        ('RUHEZEIT_KURZ',    'Ruhezeit 12 h + Kurzpause 10 Min alle 2 h',
         'Bei hauptsächlich stehender Tätigkeit: 12 h Ruhezeit, alle 2 Arbeitsstunden zusätzliche Kurzpause 10 Min.',
         'ArGV 1 Art. 61',
         'ET', 5, 0, 'VORHER', 'ET', 0, 0, 'NACHHER',
         NULL, NULL, NULL, false, 20),
        ('STEHEN_4H',        'Stehende Tätigkeit max. 4 h/Tag',
         'Ab dem 4. Schwangerschaftsmonat: bei hauptsächlich stehender Tätigkeit nur noch 4 h/Tag stehend.',
         'ArGV 1 Art. 61 Abs. 3',
         'ET', 5, 0, 'VORHER', 'ET', 0, 0, 'NACHHER',
         NULL, NULL, NULL, false, 21),
        ('STEHEN_6M',        'Hauptsächlich stehende Tätigkeit verschärft',
         'Ab dem 6. Schwangerschaftsmonat: hauptsächlich stehende Tätigkeit nur 4 h/Tag — für die restliche Zeit gleichwertige sitzende Tätigkeit anbieten.',
         'ArGV 1 Art. 61 Abs. 2',
         'ET', 3, 0, 'VORHER', 'ET', 0, 0, 'NACHHER',
         NULL, NULL, NULL, false, 22),
        ('UEBERZEIT',        'Keine Überstunden',
         'Überzeitverbot ab dem 8. Schwangerschaftsmonat.',
         'ArG Art. 35a Abs. 2',
         'ET', 1, 0, 'VORHER', 'ET', 0, 0, 'NACHHER',
         NULL, NULL, NULL, false, 30),
        ('NACHT_VERBOT',     'Verpflichtendes Nachtarbeitsverbot (20–06 Uhr)',
         'Die letzten 8 Wochen vor ET dürfen Schwangere nicht zwischen 20 und 6 Uhr beschäftigt werden. AG muss eine gleichwertige Tagesarbeit anbieten.',
         'ArG Art. 35a Abs. 4',
         'ET', 0, 8, 'VORHER', 'ET', 0, 0, 'NACHHER',
         NULL, NULL, NULL, false, 40),
        ('ERSATZARBEIT_80',  'Lohnersatz 80 % bei fehlender Ersatzarbeit',
         'Kann der AG keine gleichwertige Tagesarbeit anbieten, hat die MA Anspruch auf 80 % des Bruttolohnes. Lässt sich nicht versichern (zu Lasten AG).',
         'ArG Art. 35b Abs. 2',
         'ET', 0, 8, 'VORHER', 'ET', 0, 0, 'NACHHER',
         80.00, NULL, NULL, false, 41),
        ('VERBOT_NACH',      'Absolutes Arbeitsverbot 8 Wochen nach Geburt',
         'Absolutes Beschäftigungsverbot. AG darf die MA auf keinen Fall arbeiten lassen, selbst wenn sie das selbst wünscht.',
         'ArG Art. 35a Abs. 3',
         'GEBURT', 0, 0, 'NACHHER', 'GEBURT', 0, 8, 'NACHHER',
         NULL, NULL, NULL, true, 50),
        ('MSE_14W',          'Mutterschaftsentschädigung (MSE) 14 Wochen',
         'Taggeld 80 % des durchschnittlichen Erwerbseinkommens (inkl. 13. ML), max. CHF 220.–/Tag (entspricht Brutto CHF 8 250.–/Mt.). 98 Tage ab Niederkunft. Beantragung bei AHV-Ausgleichskasse. Sonderfall: Hospitalisierung Kind > 2 Wochen → MSE +max. 56 Tage. Tod eines Elternteils innert 6 Mt. → +2 Wochen.',
         'EOG Art. 16b',
         'GEBURT', 0, 0, 'NACHHER', 'GEBURT', 0,14, 'NACHHER',
         80.00, 220.00, NULL, false, 51),
        ('KEINE_FERIENKUERZ','Keine Ferienkürzung während MSE',
         'Während des Bezuges des gesetzlichen Mutterschaftsurlaubs (14 Wochen) ist eine Kürzung des Ferienanspruchs durch den AG unzulässig.',
         'OR Art. 329b Abs. 3 / 329f',
         'GEBURT', 0, 0, 'NACHHER', 'GEBURT', 0,14, 'NACHHER',
         NULL, NULL, NULL, false, 52),
        ('FREIWILLIG',       'Freiwillige Wiederaufnahme (Woche 9–16)',
         'Bis zur 16. Woche darf die Wöchnerin arbeiten wenn sie will — AG darf es nicht verlangen. Bleibt sie freiwillig fern, muss die Zeit nicht entschädigt werden.',
         'ArG Art. 35a Abs. 3',
         'GEBURT', 0, 8, 'NACHHER', 'GEBURT', 0,16, 'NACHHER',
         NULL, NULL, NULL, false, 60),
        ('WIEDERAUFNAHME',   'Pflicht zur Wiederaufnahme der Arbeit',
         'Ab der 17. Woche (113. Tag) ist die MA zur Wiederaufnahme im gewohnten Umfang gehalten. Pensum-Reduktion kann vereinbart werden — Vertragsänderung schriftlich.',
         'ArG Art. 35a',
         'GEBURT', 0,16, 'NACHHER', NULL, NULL, NULL, NULL,
         NULL, NULL, NULL, false, 70),
        ('KUENDIG_SCHUTZ',   'Kündigungsschutz (Sperrfrist)',
         'Kündigung durch AG ist nichtig — von Beginn der Schwangerschaft (auch ohne Kenntnis) bis 16 Wochen nach Niederkunft. Greift erst nach Ablauf der Probezeit. Eine vor der Schwangerschaft gültig ausgesprochene Kündigung wird unterbrochen und läuft erst nach Sperrfristende weiter.',
         'OR Art. 336c Abs. 1 Bst. c',
         'MELDUNG', 0, 0, 'NACHHER', 'GEBURT', 0,16, 'NACHHER',
         NULL, NULL, NULL, false, 80),
        ('STILLZEIT',        'Bezahlte Stillzeit',
         'Während des 1. Lebensjahres bezahlte Stillzeit (auch beim Abpumpen). Abgestuftes Modell nach täglicher Arbeitszeit. Gilt für Stillen im Betrieb UND ausserhalb. Gilt pro Kind.',
         'ArGV 1 Art. 60 Abs. 2',
         'GEBURT', 0, 0, 'NACHHER', 'GEBURT',12, 0, 'NACHHER',
         NULL, NULL,
         'Tagesarbeitszeit ≤ 4 h: mind. 30 Min bezahlt · 4–7 h: mind. 60 Min · > 7 h: mind. 90 Min',
         false, 90)
        ON CONFLICT (code) DO NOTHING;
    ");

    // Walter-Vorgabe 10.06.2026: Negative Offsets aus alten Seeds auf Beträge
    // ziehen (Engine rechnet Math.Abs + Vorzeichen aus `richtung`).
    db.Database.ExecuteSqlRaw(@"
        UPDATE pregnancy_rule SET offset_monate = ABS(offset_monate) WHERE offset_monate < 0;
        UPDATE pregnancy_rule SET offset_wochen = ABS(offset_wochen) WHERE offset_wochen < 0;
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
    // Walter-Vorgabe 13.06.2026: CSP — erschwert XSS-Angriffe deutlich.
    //   • default-src 'self'                 — alles standardmäßig nur von eigener Domain
    //   • script-src 'self' 'unsafe-inline' + cdnjs (SheetJS/xlsx-Library für Excel-Im-/Export)
    //   • style-src 'self' 'unsafe-inline' + Google Fonts CSS
    //   • font-src  'self' + Google Fonts Files
    //   • img-src   'self' + data: + blob: (Foto-Preview, Doku-Vorschau)
    //   • frame-src 'self' + blob:           — PDF-Vorschau im <iframe> mit
    //                                          URL.createObjectURL(blob) → blob:https://…
    //   • connect-src 'self'                 — API-Calls nur an eigene Domain
    // Wenn nach Deploy etwas im Browser nicht mehr lädt → F12 Console zeigt
    // welche Direktive blockt → hier nachschärfen.
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' https://cdnjs.cloudflare.com; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: blob:; " +
        "frame-src 'self' blob:; " +
        "connect-src 'self'";
    await next();
});

// Favicon/Manifest: OneCrew-Branding für BEIDE Domains (Walter-Vorgabe 05.07.2026 —
// test.hr-srgmbh.ch und onecrew.ch laufen auf demselben Programm, überall OneCrew).
// Die Dateien liegen direkt im wwwroot-Root und werden von UseStaticFiles bedient;
// keine Host-Weiche mehr nötig.

// Statische Dateien / Startseite
app.UseDefaultFiles();
// Walter-Vorgabe 27.05.2026: .md-Files (Hilfe-Texte unter wwwroot/help/)
// muessen mit text/markdown ausgeliefert werden, sonst meldet der Browser
// "Datei zum Download" statt im Helper-Panel anzuzeigen.
var mdMime = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
mdMime.Mappings[".md"] = "text/markdown; charset=utf-8";
app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions {
    ContentTypeProvider = mdMime,
    // Walter-Vorgabe 27.06.2026: HTML-Dokumente (index.html, import.html) NIE aus
    // dem Browser-Cache ausliefern. Das gesamte CSS/Markup steckt inline in der
    // HTML; die JS-Module werden per ?v= gebustet, die HTML selbst aber nicht —
    // dadurch sah man nach einem Deploy alte Stände (z.B. verdeckte Buttons,
    // alte Farben). no-cache zwingt eine Revalidierung → geänderte HTML wird
    // sofort frisch geladen. JS/CSS-?v=-Busting bleibt unberührt.
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers["Pragma"]        = "no-cache";
            ctx.Context.Response.Headers["Expires"]       = "0";
        }
    }
});

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