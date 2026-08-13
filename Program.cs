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
builder.Services.AddScoped<StundenkontrollePdfService>();
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
// AHV-Formular 318.260 «Anmeldung Versicherungsausweis» vorausgefüllt (Walter 06.08.2026).
builder.Services.AddScoped<AhvAnmeldungPdfService>();
// Manager-Dienstplan als A4-quer-PDF (Walter 09.08.2026, zustandslos).
builder.Services.AddSingleton<ManagerDienstplanPdfService>();
// Verzicht auf medizinische Untersuchung Nachtarbeit (Beilage-Layout).
builder.Services.AddScoped<NachtVerzichtPdfService>();
// Ausnahmeregelung Tag-/Nachtarbeit (Anlage zum Arbeitsvertrag), vorausgefüllt.
builder.Services.AddScoped<NachtAusnahmePdfService>();
// Kündigungsschreiben (Walter-Vorgabe 22.06.2026).
builder.Services.AddScoped<KuendigungPdfService>();
builder.Services.AddScoped<AufforderungZurArbeitPdfService>();
builder.Services.AddScoped<ArbeitszeugnisPdfService>();
builder.Services.AddScoped<VerwarnungPdfService>();
builder.Services.AddScoped<BewerbungsbogenPdfService>();
builder.Services.AddScoped<AuswertungenReportPdfService>();
builder.Services.AddScoped<ProbezeitberichtPdfService>();
builder.Services.AddScoped<MutterschaftPdfService>();
builder.Services.AddScoped<RisikobeurteilungPdfService>();
// Fibu-Journal-Generator (Buchungsjournal aus den bestätigten Snapshots).
builder.Services.AddScoped<FibuJournalService>();
// Edit-Sperre während HR Lohnlauf prüft (Walter-Vorgabe 17.05.2026, Variante 2).
builder.Services.AddScoped<LohnEditLockService>();
builder.Services.AddScoped<AbsenceHoursRecalcService>();
// pain.001-XML-Generator (ISO 20022) für DTA-Zahlungsexport
builder.Services.AddScoped<Iso20022PainService>();
// Sperrfrist-Service: Kündigungsschutz nach Art. 336c OR bei AU
builder.Services.AddScoped<SperrfristService>();
// L-GAV-Beitrag: automatischer Jahresabzug nach Vertragstyp/Pensum
builder.Services.AddScoped<LgavBeitragService>();
builder.Services.AddScoped<UniformDepotService>();
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
builder.Services.AddScoped<QstKonfessionSyncService>();
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
builder.Services.AddScoped<LseDatenService>();   // BFS Lohnstrukturerhebung (Walter 13.08.2026)
// Dashboard-Cockpit (Alarme: Bewilligungen, Probezeit, Verträge, Jubiläen ...)
builder.Services.AddScoped<DashboardService>();
// SMTP-Versand für MA-Postfach-Benachrichtigungen (Lohnzettel-Bereit etc.)
builder.Services.AddScoped<EmailService>();
// Täglicher Mirus-Änderungsdigest 06:00 (Walter 23.07.2026)
builder.Services.AddScoped<MirusChangeDigestService>();
builder.Services.AddHostedService<MirusChangeDigestBackgroundService>();
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
    c.DefaultRequestHeaders.UserAgent.ParseAdd("onecrew/1.0 (+onecrew.ch)");
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

    // Kündigungs-Daten am MA (Walter 16.07.2026): gesetzt beim Erstellen des
    // Kündigungsschreibens, gelöscht beim Kündigungsrückzug.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee
        ADD COLUMN IF NOT EXISTS kuendigung_ausgesprochen_am date,
        ADD COLUMN IF NOT EXISTS kuendigung_per date;
    ");
    // Kündigung durch uns (AG) oder durch Mitarbeiter (AN) — Walter 26.07.2026.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee
        ADD COLUMN IF NOT EXISTS kuendigung_durch text;
    ");
    // Austrittsgrund (kurze Codes, Statistik) — Walter 26.07.2026.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee
        ADD COLUMN IF NOT EXISTS austrittsgrund text;
    ");

    // Abacus-Export (Treuhänder-Vorgabe 05.08.2026): MWST-Konfiguration pro
    // Kontoplan-Zeile (wie Mirus-Fibukonto-Dialog «Mehrwertsteuer») + Seed für
    // die Personalaufwand-Zeilen (Soll 4xxx / Gegen 1920 → 1067 / Code 200),
    // sowie Buchungsnummer pro Lohnperiode (DocumentNumber im AbaConnect-XML).
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE lohn_konto_mapping
        ADD COLUMN IF NOT EXISTS mwst_konto   varchar(10),
        ADD COLUMN IF NOT EXISTS mwst_code    varchar(10),
        ADD COLUMN IF NOT EXISTS mwst_prozent numeric(5,2);
    ");
    db.Database.ExecuteSqlRaw(@"
        UPDATE lohn_konto_mapping
        SET mwst_konto = '1067', mwst_code = '200', mwst_prozent = 0
        WHERE mwst_konto IS NULL AND mwst_code IS NULL
          AND fibukonto LIKE '4%' AND gegenkonto = '1920';
    ");
    // Mirus export.xls (Walter 05.08.2026): Position 600 (Naturallohn
    // Verpflegung / Privatanteil Geschäftswagen) trägt Code 311 / 8.1% /
    // Konto 2065. Wird vom Journal heute nicht gebucht — Konfiguration
    // trotzdem vollständig übernehmen.
    db.Database.ExecuteSqlRaw(@"
        UPDATE lohn_konto_mapping
        SET mwst_konto = '2065', mwst_code = '311', mwst_prozent = 8.1
        WHERE mwst_konto IS NULL AND mwst_code IS NULL
          AND position = 600 AND fibukonto = '1920';
    ");
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE payroll_periode
        ADD COLUMN IF NOT EXISTS fibu_buchungsnummer varchar(20);
    ");

    // Verschollen-Wächter (Walter 05.08.2026): aktiver, easy@work-verknüpfter
    // MA taucht in keiner Aktiv-Liste mehr auf → Datum der Feststellung +
    // kritische Dashboard-Warnung «Austritt prüfen».
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee
        ADD COLUMN IF NOT EXISTS easy_missing_since date;
    ");
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO dashboard_warning_config
            (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
        VALUES
            ('easy_verschollen', 'MA in easy@work verschollen', TRUE, NULL, NULL, 'critical', NULL, FALSE, 25, 15, 'red')
        ON CONFLICT (category) DO NOTHING;
    ");

    // AHV-Nummer fehlt (Walter 06.08.2026): kritische Warnung für aktive MA
    // mit laufendem Vertrag ohne AHV-Nummer.
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO dashboard_warning_config
            (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
        VALUES
            ('ahv_nummer_fehlt', 'AHV-Nummer fehlt', TRUE, NULL, NULL, 'critical', NULL, FALSE, 26, 16, 'red')
        ON CONFLICT (category) DO NOTHING;
    ");

    // Anonymer Austritts-Fragebogen (Walter 26.07.2026) — ersetzt Google Forms.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS exit_survey_response (
            id                bigserial PRIMARY KEY,
            created_at        timestamp without time zone NOT NULL DEFAULT now(),
            company_profile_id integer,
            reasons_json      text NOT NULL DEFAULT '[]',
            reason_other      text,
            atmosphere_detail text,
            rating            integer,
            comment           text,
            ip_hash           text
        );
        CREATE INDEX IF NOT EXISTS ix_exit_survey_response_created_at
            ON exit_survey_response (created_at DESC);
    ");
    // Filiale am anonymen Fragebogen (Walter 26.07.2026) — kein MA-Bezug.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE exit_survey_response
        ADD COLUMN IF NOT EXISTS company_profile_id integer;
    ");
    // Frage 2 «besser werden» (Walter 26.07.2026) — JA/NEIN + Themen.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE exit_survey_response
        ADD COLUMN IF NOT EXISTS improve_answer text,
        ADD COLUMN IF NOT EXISTS improve_themes_json text NOT NULL DEFAULT '[]';
    ");

    // Ärzte-Verzeichnis (Walter 16.07.2026) — fuer den Brief an den
    // behandelnden Arzt (Eignungsuntersuchung Mutterschutz).
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS arzt (
            id           serial PRIMARY KEY,
            titel        text,
            vorname      text NOT NULL DEFAULT '',
            nachname     text NOT NULL,
            fachgebiet   text,
            praxis_name  text,
            strasse      text,
            plz          text,
            ort          text,
            telefon      text,
            email        text,
            bemerkung    text,
            aktiv        boolean NOT NULL DEFAULT true,
            created_at   timestamp without time zone NOT NULL DEFAULT now()
        );
    ");
    // Erst-Seed NUR in die leere Tabelle (CLAUDE.md-Muster): der von Walter
    // gelieferte erste Eintrag (Frauenzentrum Sursee).
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO arzt (titel, vorname, nachname, fachgebiet, praxis_name, strasse, plz, ort, telefon, email)
        SELECT 'Dr. med.', 'Málna', 'Makai', 'Gynäkologie/Geburtshilfe', 'Frauenzentrum Sursee',
               'Centralstrasse 14a', '6210', 'Sursee', '+41 41 921 70 22', 'info@frauenzentrum-sursee.ch'
        WHERE NOT EXISTS (SELECT 1 FROM arzt);
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



    // Rollback der Sync-Metadaten-Spalten auf timestamptz (wie vor dem
    // 18.07.2026-Experiment). Sync schreibt dort DateTime.UtcNow — wie bisher
    // funktionierend. Stempel-Wanduhrzeiten (time_in/out) bleiben without TZ.
    db.Database.ExecuteSqlRaw(@"
        DO $$
        DECLARE
            r record;
        BEGIN
            FOR r IN
                SELECT table_name, column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND udt_name = 'timestamp'
                  AND (
                        (table_name = 'employee_number_alias' AND column_name = 'created_at')
                     OR (table_name = 'easyatwork_employee_alias' AND column_name = 'created_at')
                     OR (table_name = 'employee_time_entry' AND column_name IN ('created_at','updated_at'))
                     OR (table_name = 'easyatwork_sync_state' AND column_name IN ('last_sync_at','last_seen_updated_at'))
                  )
            LOOP
                EXECUTE format(
                    'ALTER TABLE public.%I ALTER COLUMN %I TYPE timestamptz USING (%I AT TIME ZONE %L)',
                    r.table_name, r.column_name, r.column_name, 'Europe/Zurich');
            END LOOP;
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

    // Dokument-Zeitstempel → Lokalzeit (Walter 22.07.2026 / Vorgabe 30.06.2026).
    // employee_dokument.hochgeladen_am und mailbox_document.uploaded_at waren
    // als timestamptz angelegt → Npgsql verlangt UTC. Systemweit gilt:
    // timestamp without time zone + DateTime.Now. Check via udt_name
    // (= 'timestamptz') — zuverlässiger als data_type. Idempotent.
    db.Database.ExecuteSqlRaw(@"
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'employee_dokument'
                  AND column_name = 'hochgeladen_am'
                  AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE employee_dokument
                    ALTER COLUMN hochgeladen_am TYPE timestamp without time zone
                    USING (hochgeladen_am AT TIME ZONE 'Europe/Zurich');
            END IF;

            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'mailbox_document'
                  AND column_name = 'uploaded_at'
                  AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE mailbox_document
                    ALTER COLUMN uploaded_at TYPE timestamp without time zone
                    USING (uploaded_at AT TIME ZONE 'Europe/Zurich');
            END IF;

            -- Zusatzadressen (Walter 26.07.2026): Speichern scheiterte mit 500,
            -- wenn created_at/updated_at noch timestamptz waren (Npgsql + DateTime.Now).
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'employee_address'
                  AND column_name = 'created_at'
                  AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE employee_address
                    ALTER COLUMN created_at TYPE timestamp without time zone
                    USING (created_at AT TIME ZONE 'Europe/Zurich');
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'employee_address'
                  AND column_name = 'updated_at'
                  AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE employee_address
                    ALTER COLUMN updated_at TYPE timestamp without time zone
                    USING (updated_at AT TIME ZONE 'Europe/Zurich');
            END IF;

            -- Absenzen (Walter 31.07.2026): Speichern scheiterte mit 500,
            -- analog Zusatzadresse — created_at/updated_at noch timestamptz
            -- (Npgsql + DateTime.Now = Kind=Local).
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'absence'
                  AND column_name = 'created_at'
                  AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE absence
                    ALTER COLUMN created_at TYPE timestamp without time zone
                    USING (created_at AT TIME ZONE 'Europe/Zurich');
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = 'absence'
                  AND column_name = 'updated_at'
                  AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE absence
                    ALTER COLUMN updated_at TYPE timestamp without time zone
                    USING (updated_at AT TIME ZONE 'Europe/Zurich');
            END IF;
        END $$;
    ");

    // payroll_snapshot + payroll_saldo (Walter 04.08.2026): Vereinheitlichung
    // auf die System-Regel Lokalzeit + timestamp without time zone. Die beiden
    // Tabellen waren timestamptz-Ausreisser (03.08.) — ConfirmPayroll schreibt
    // DateTime.Now (Kind=Local) in gf_freigegeben_at/hr_bestaetigt_at → Npgsql
    // lehnte das ab (HTTP 500 beim Lohn bestätigen). Idempotent: konvertiert
    // nur Spalten, die noch timestamptz sind. Gleiches SQL auch in
    // migrations-archive/fix_payroll_snapshot_saldo_timestamps.sql (TablePlus).
    db.Database.ExecuteSqlRaw(@"
        DO $$
        DECLARE
            r record;
        BEGIN
            FOR r IN
                SELECT table_name, column_name
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND udt_name = 'timestamptz'
                  AND (
                        (table_name = 'payroll_snapshot'
                         AND column_name IN ('created_at','updated_at','gf_freigegeben_at','hr_bestaetigt_at'))
                     OR (table_name = 'payroll_saldo'
                         AND column_name IN ('created_at','updated_at'))
                     -- Geburt-eintragen-Crash 04.08.2026: Familienmitglieder
                     OR (table_name = 'employee_family_member'
                         AND column_name IN ('created_at','updated_at'))
                  )
            LOOP
                EXECUTE format(
                    'ALTER TABLE public.%I ALTER COLUMN %I TYPE timestamp without time zone USING (%I AT TIME ZONE %L)',
                    r.table_name, r.column_name, r.column_name, 'Europe/Zurich');
            END LOOP;
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
            ('permit_expiring',        'Aufenthaltsbewilligung läuft ab',       TRUE,   60,   30, 'warning',  'critical', TRUE,   2),
            ('probation_end',          'Probezeit endet',                       TRUE,   14,    7, 'info',     'warning',  TRUE,   3),
            ('contract_end',           'Befristeter Vertrag endet',             TRUE,   30,   14, 'info',     'warning',  TRUE,   4),
            ('exit_pending_active',    'Austritt steht bevor',                  TRUE,   30,    7, 'warning',  'critical', TRUE,   5),
            ('qst_pflicht_offen',      'QST-Pflicht offen (Lohnlauf gesperrt)', TRUE, NULL, NULL, 'critical', NULL,       FALSE,  6),
            ('spouse_doku_fehlt',      'Ausweis Ehepartner fehlt (QST)',        TRUE, NULL, NULL, 'critical', NULL,       FALSE,  7),
            ('employee_doku_fehlt',    'Ausweis Mitarbeiter fehlt (QST)',       TRUE, NULL, NULL, 'critical', NULL,       FALSE,  8),
            ('schwangerschaft',        'Mutterschaft / Schwangerschaft',        TRUE,   30, NULL, 'info',     'warning',  TRUE,   9),
            ('lohn_provisorisch',      'Lohn wartet auf Definitiv-Abschluss',   TRUE, NULL, NULL, 'warning',  NULL,       FALSE, 10),
            ('birthday',               'Geburtstage',                           TRUE,    7, NULL, 'info',     NULL,       TRUE,  11),
            ('anniversary',            'Dienstjubiläen',                        TRUE,   30, NULL, 'info',     NULL,       TRUE,  12),
            ('night_work_exam_expiring','Nachtarbeit-Bewilligung läuft ab',     TRUE,   30,    7, 'warning',  'critical', TRUE,  13),
            ('night_work_exam_fehlt',  'Nachtarbeit-Arztzeugnis fehlt',         TRUE, NULL, NULL, 'critical', NULL,       FALSE, 14),
            ('night_work_exam_mismatch','Nachtarbeit-Enddatum in easy@work falsch', TRUE, NULL, NULL, 'critical', NULL,   FALSE, 15),
            ('night_work_ausnahme_fehlt','Nachtarbeit-Ausnahmeregelung fehlt',  TRUE, NULL, NULL, 'critical', NULL,       FALSE, 23),
            ('availability_missing',   'Verfügbarkeit fehlt',                    TRUE, NULL, NULL, 'warning',  NULL,       FALSE, 16),
            ('permit_missing',         'Aufenthaltsbewilligung fehlt',           TRUE, NULL, NULL, 'critical', NULL,       FALSE, 17),
            ('night_work_untersuch_fehlt', 'Nacht Untersuch fehlt',              TRUE, NULL, NULL, 'critical', NULL,       FALSE, 18),
            ('probezeit_gespraech_offen',  'Probezeitgespräch offen',            TRUE,   14,    7, 'warning',  'critical', TRUE,  19),
            ('kuendigung_ablauf',          'Vertragsende wegen Kündigung',       TRUE,   14,    0, 'warning',  'critical', TRUE,  20),
            ('kuendigung_sperrfrist_ende', 'Kündigung möglich (Sperrfrist Ende)', TRUE,   90, NULL, 'warning',  NULL,       TRUE,  21),
            ('audit_log_stumm',            'Aktivitäts-Log schreibt nicht',      TRUE,    1, NULL, 'critical', NULL,       TRUE,  22)
        ON CONFLICT (category) DO NOTHING;
    ");
    // Priorität + Warnfarbe (Walter 19.07.2026) — editierbar in System → Warnungen.
    // «Bewilligung/Nacht läuft ab» decken auch den Abgelaufen-Fall ab (Titel + red_overdue).
    // permit_expired entfällt wieder (konsolidiert in permit_expiring).
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE dashboard_warning_config
            ADD COLUMN IF NOT EXISTS todo_priority INT  NOT NULL DEFAULT 100,
            ADD COLUMN IF NOT EXISTS warn_color    TEXT NOT NULL DEFAULT 'none';
        DELETE FROM dashboard_warning_config WHERE category = 'permit_expired';
        INSERT INTO dashboard_warning_config
            (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
        VALUES
            ('night_work_untersuch_fehlt', 'Nacht Untersuch fehlt', TRUE, NULL, NULL, 'critical', NULL, FALSE, 18, 30, 'red'),
            ('probezeit_gespraech_offen', 'Probezeitgespräch offen', TRUE, 14, 7, 'warning', 'critical', TRUE, 19, 45, 'none'),
            ('kuendigung_ablauf', 'Vertragsende wegen Kündigung', TRUE, 14, 0, 'warning', 'critical', TRUE, 20, 55, 'red_overdue'),
            ('kuendigung_sperrfrist_ende', 'Kündigung möglich (Sperrfrist Ende)', TRUE, 90, NULL, 'warning', NULL, TRUE, 21, 25, 'red'),
            ('audit_log_stumm', 'Aktivitäts-Log schreibt nicht', TRUE, 1, NULL, 'critical', NULL, TRUE, 22, 5, 'red')
        ON CONFLICT (category) DO NOTHING;
        UPDATE dashboard_warning_config SET todo_priority = 10,  warn_color = 'red'
            WHERE category = 'permit_missing' AND todo_priority = 100 AND warn_color = 'none';
        UPDATE dashboard_warning_config SET todo_priority = 20,  warn_color = 'red_overdue'
            WHERE category = 'permit_expiring' AND todo_priority = 100 AND warn_color = 'none';
        UPDATE dashboard_warning_config SET label = 'Aufenthaltsbewilligung läuft ab'
            WHERE category = 'permit_expiring' AND label = 'Bewilligung läuft ab';
        UPDATE dashboard_warning_config SET todo_priority = 30,  warn_color = 'red'
            WHERE category = 'night_work_untersuch_fehlt' AND todo_priority = 100 AND warn_color = 'none';
        UPDATE dashboard_warning_config SET todo_priority = 40,  warn_color = 'red_overdue'
            WHERE category = 'night_work_exam_expiring' AND todo_priority = 100 AND warn_color = 'none';
        UPDATE dashboard_warning_config SET todo_priority = 50,  warn_color = 'none'
            WHERE category = 'night_work_exam_fehlt' AND todo_priority = 100 AND warn_color = 'none';
        -- Walter 31.07.2026: Arztzeugnis und Ausnahmeregelung getrennt.
        -- Alte Sammel-Kategorie einmalig umbenennen + Kritisch.
        -- Walter 03.08.2026: Ausnahmeregelung ebenfalls Kritisch (nicht nur Wichtig).
        UPDATE dashboard_warning_config
           SET label = 'Nachtarbeit-Arztzeugnis fehlt',
               severity_base = 'critical'
         WHERE category = 'night_work_exam_fehlt'
           AND label = 'Nachtarbeit-Nachweise fehlen';
        INSERT INTO dashboard_warning_config
            (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
        VALUES
            ('night_work_ausnahme_fehlt', 'Nachtarbeit-Ausnahmeregelung fehlt', TRUE, NULL, NULL, 'critical', NULL, FALSE, 23, 52, 'none')
        ON CONFLICT (category) DO NOTHING;
        -- ACHTUNG (Walter-Bug 06.08.2026): hier stand ein UPDATE, das bei JEDEM
        -- Start severity_base auf 'critical' zurückzwang, sobald Walter die
        -- Stufe in der Warnungsverwaltung änderte (Guard hing am editierten
        -- Feld selbst — Stolperfalle 8). Entfernt; der Seed-Wert kommt nur
        -- noch über das INSERT oben in eine LEERE Zeile. User-Einstellungen
        -- in dashboard_warning_config NIE per Start-Seed überschreiben.
        UPDATE dashboard_warning_config SET todo_priority = 15,  warn_color = 'red'
            WHERE category = 'minimum_wage_violation' AND todo_priority = 100 AND warn_color = 'none';
        UPDATE dashboard_warning_config SET todo_priority = 45,  warn_color = 'none'
            WHERE category = 'probezeit_gespraech_offen' AND todo_priority = 100;
        -- Walter 23.07.2026: alter Seed war warn_days=NULL + escalate=14
        -- (= ganze Probezeit). Einmalig auf Vorlauf 14 / Kritisch ab 7 heben.
        UPDATE dashboard_warning_config SET warn_days = 14, escalate_days = 7
            WHERE category = 'probezeit_gespraech_offen'
              AND warn_days IS NULL
              AND escalate_days = 14;
        UPDATE dashboard_warning_config SET todo_priority = 55,  warn_color = 'red_overdue'
            WHERE category = 'kuendigung_ablauf' AND todo_priority = 100;
        -- Walter 26.07.2026: Austritt-ToDo bis zum Austrittstag (nicht mehr danach).
        UPDATE dashboard_warning_config
           SET label = 'Austritt steht bevor',
               warn_days = 30,
               escalate_days = 7,
               severity_base = 'warning',
               severity_escalated = 'critical',
               is_date_based = TRUE
         WHERE category = 'exit_pending_active'
           AND label = 'Austritt erfasst, MA noch aktiv';
        -- (Guard NUR am alten Label — warn_days/is_date_based würden nach
        --  User-Edits erneut zünden und Einstellungen überschreiben.)
        UPDATE dashboard_warning_config SET todo_priority = 25, warn_color = 'red',
               label = 'Kündigung möglich (Sperrfrist Ende)', warn_days = 90, severity_base = 'warning'
            WHERE category = 'kuendigung_sperrfrist_ende' AND todo_priority = 100;
        -- Walter 26.07.2026: Warnung wenn audit_log zu lange nichts schreibt (Default 1 Tag).
        INSERT INTO dashboard_warning_config
            (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
        VALUES
            ('audit_log_stumm', 'Aktivitäts-Log schreibt nicht', TRUE, 1, NULL, 'critical', NULL, TRUE, 22, 5, 'red')
        ON CONFLICT (category) DO NOTHING;
        -- Walter 04.08.2026: QST-Kanton-Mismatch-Wächter — Kanton der aktiven
        -- QST-Erfassung weicht vom Wohnkanton (employee.canton_code) ab
        -- (z.B. nach Adressänderung/easy@work-Sync) → falscher Tarif im Lohnlauf.
        INSERT INTO dashboard_warning_config
            (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
        VALUES
            ('qst_kanton_mismatch', 'QST-Kanton ≠ Wohnkanton', TRUE, NULL, NULL, 'critical', NULL, FALSE, 24, 16, 'red')
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

    // < 8 h / Wo. (NBU-Befreiung) am Vertrag statt am MA (Walter 31.07.2026).
    // Nur FLEX sinnvoll; Backfill aus bisherigem employee-Flag für laufende FLEX.
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employment
        ADD COLUMN IF NOT EXISTS teilzeit_unter_8h_woche boolean NOT NULL DEFAULT false;
    ");
    db.Database.ExecuteSqlRaw(@"
        UPDATE employment e
           SET teilzeit_unter_8h_woche = true
          FROM employee emp
         WHERE e.employee_id = emp.id
           AND emp.teilzeit_unter_8h_woche = true
           AND UPPER(TRIM(e.employment_model)) IN ('FLEX', 'UTP')
           AND (e.contract_end_date IS NULL OR e.contract_end_date >= CURRENT_DATE)
           AND e.teilzeit_unter_8h_woche = false;
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
          ('VERTRAG_LINK','Arbeitsvertrag-Link','SMS-Vorlage für den öffentlichen Vertrags-Link. Platzhalter (in geschweiften Klammern): Vorname, Firma, Link, GueltigBis','appreciation',8,true),
          ('BEWILLIGUNG_ABGELAUFEN','Bewilligung abgelaufen','Kurz-SMS + Link-Seite bei abgelaufener Bewilligung. SMS max. 160 Zeichen (Vorname); Mitteilung: Briefanrede, PermitCode, GueltigBis, SenderName','appreciation',9,true),
          ('WILLKOMMENSTAG','Willkommenstag-Einladung','SMS an den KANDIDATEN mit Einladung zum Willkommenstag (Onboarding). Platzhalter: Vorname, Firma, Arbeitsort, Wochentag, Datum, Zeit, Link','appreciation',10,true),
          ('WILLKOMMENSTAG_ERINNERUNG','Willkommenstag-Erinnerung','SMS beim ERNEUTEN Senden der Willkommenstag-Einladung (Erinnerung). Gleiche Platzhalter: Vorname, Firma, Arbeitsort, Wochentag, Datum, Zeit, Link. Ohne aktive Vorlage wird die normale Einladung verwendet.','appreciation',11,true)
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

        // WILLKOMMENSTAG (Walter 11.08.2026): SMS an den KANDIDATEN mit der
        // Einladung zum Willkommenstag — VOR der easy@work-Erfassung. Der Link
        // führt auf /willkommen/{token} mit Annehmen/Absagen.
        if (_mtTypeIds.TryGetValue("WILLKOMMENSTAG", out var _wtTypeId))
        {
            var _wtToneId = _mtToneIds.TryGetValue("Warm", out var _w) ? _w
                          : db.MomentTones.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => t.Id).FirstOrDefault();
            if (_wtToneId != 0 && !db.MomentTexts.Any(x => x.MomentTypeId == _wtTypeId))
            {
                db.MomentTexts.Add(new MomentText {
                    MomentTypeId = _wtTypeId, MomentToneId = _wtToneId,
                    Titel = "Willkommenstag-Einladung",
                    SmsText = "Hallo {Vorname}, herzlich willkommen bei {Firma}! Dein Willkommenstag: {Wochentag}, {Datum} um {Zeit}. Bitte bestätige hier: {Link}",
                    BodyText = "Vorlage für die Willkommenstag-SMS an den Kandidaten. Platzhalter: {Vorname}, {Firma}, {Wochentag}, {Datum}, {Zeit}, {Link}.",
                    LanguageCode = "de", Version = "1.0", RequiresReview = false,
                    IsActive = true, SortOrder = 0, CreatedAt = DateTime.Now });
            }
        }

        // WILLKOMMENSTAG_ERINNERUNG (Walter 12.08.2026): eigener SMS-Text für
        // das ERNEUTE Senden («SMS erneut») — z.B. freundliche Erinnerung
        // statt nochmals die identische Einladung.
        if (_mtTypeIds.TryGetValue("WILLKOMMENSTAG_ERINNERUNG", out var _weTypeId))
        {
            var _weToneId = _mtToneIds.TryGetValue("Warm", out var _w2) ? _w2
                          : db.MomentTones.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => t.Id).FirstOrDefault();
            if (_weToneId != 0 && !db.MomentTexts.Any(x => x.MomentTypeId == _weTypeId))
            {
                db.MomentTexts.Add(new MomentText {
                    MomentTypeId = _weTypeId, MomentToneId = _weToneId,
                    Titel = "Willkommenstag-Erinnerung",
                    SmsText = "Hallo {Vorname}, kleine Erinnerung an deinen Willkommenstag: {Wochentag}, {Datum} um {Zeit}. Bitte bestätige hier: {Link}",
                    BodyText = "",
                    LanguageCode = "de", Version = "1.0", RequiresReview = false,
                    IsActive = true, SortOrder = 0, CreatedAt = DateTime.Now });
            }
        }

        // BEWILLIGUNG_ABGELAUFEN (Walter 19.07.2026): Kurz-SMS + Link-Seite.
        // SmsText ≤ 160 Zeichen (Push); BodyText = Mitteilung auf /bewilligung/{token}.
        if (_mtTypeIds.TryGetValue("BEWILLIGUNG_ABGELAUFEN", out var _baTypeId))
        {
            var _baToneId = _mtToneIds.TryGetValue("Calm", out var _bc) ? _bc
                          : db.MomentTones.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).Select(t => t.Id).FirstOrDefault();
            if (_baToneId != 0 && !db.MomentTexts.Any(x => x.MomentTypeId == _baTypeId))
            {
                db.MomentTexts.Add(new MomentText {
                    MomentTypeId = _baTypeId, MomentToneId = _baToneId,
                    Titel = "Bewilligung abgelaufen",
                    SmsText = "Hallo {Vorname}, deine Bewilligung ist abgelaufen. Tippe auf den Link:",
                    BodyText = "{Briefanrede}\n\ndeine Bewilligung ({PermitCode}) ist am {GueltigBis} abgelaufen. Kannst du bitte die neue Bewilligung so bald wie möglich bei HR nachreichen?\n\nDanke und freundliche Grüsse\n{SenderName}",
                    LanguageCode = "de", Version = "1.0", RequiresReview = false,
                    IsActive = true, SortOrder = 0, CreatedAt = DateTime.Now });
            }
            else if (_baToneId != 0)
            {
                // Einmalig zu lange Alt-SMS kürzen (nur wenn > 160 Zeichen).
                foreach (var old in db.MomentTexts.Where(x => x.MomentTypeId == _baTypeId
                             && x.SmsText != null && x.SmsText.Length > 160).ToList())
                {
                    old.SmsText = "Hallo {Vorname}, deine Bewilligung ist abgelaufen. Tippe auf den Link:";
                    if (string.IsNullOrWhiteSpace(old.BodyText)
                        || old.BodyText.Contains("SMS-Vorlage bei abgelaufener"))
                    {
                        old.BodyText = "{Briefanrede}\n\ndeine Bewilligung ({PermitCode}) ist am {GueltigBis} abgelaufen. Kannst du bitte die neue Bewilligung so bald wie möglich bei HR nachreichen?\n\nDanke und freundliche Grüsse\n{SenderName}";
                    }
                    if (string.IsNullOrWhiteSpace(old.Titel)) old.Titel = "Bewilligung abgelaufen";
                }
            }
        }

        db.SaveChanges();
    }

    // ── Verwarnungs-Verlauf (Walter 14.07.2026, idempotent) ──
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS employee_verwarnung (
            id            serial PRIMARY KEY,
            employee_id   integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            datum         date NOT NULL,
            stufe         text NOT NULL DEFAULT 'VERWARNUNG_1',
            gruende       text,
            beschreibung  text,
            dokument_id   integer REFERENCES employee_dokument(id) ON DELETE SET NULL,
            storniert     boolean NOT NULL DEFAULT false,
            storno_grund  text,
            erstellt_von  text,
            erstellt_am   timestamp without time zone NOT NULL DEFAULT now(),
            geaendert_am  timestamp without time zone
        );
        CREATE INDEX IF NOT EXISTS ix_employee_verwarnung_emp ON employee_verwarnung(employee_id, datum);
    ");

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

        -- Lohnausweis-Download-Link an Behörde (Walter 30.07.2026, Lohnabtretung)
        ALTER TABLE employee_lohn_assignment
            ADD COLUMN IF NOT EXISTS lohnausweis_an_behoerde boolean NOT NULL DEFAULT false;
        CREATE TABLE IF NOT EXISTS lohnausweis_share_token (
            id                          serial PRIMARY KEY,
            employee_id                 integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            behoerde_id                 integer NOT NULL REFERENCES behoerde(id) ON DELETE CASCADE,
            employee_lohn_assignment_id integer NOT NULL REFERENCES employee_lohn_assignment(id) ON DELETE CASCADE,
            payroll_periode_id          integer REFERENCES payroll_periode(id) ON DELETE SET NULL,
            year                        integer NOT NULL,
            token_hash                  text NOT NULL,
            expires_at                  timestamp without time zone NOT NULL,
            opened_at                   timestamp without time zone,
            used_at                     timestamp without time zone,
            revoked_at                  timestamp without time zone,
            created_at                  timestamp without time zone NOT NULL DEFAULT now(),
            created_by                  integer
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_lohnausweis_share_token_hash
            ON lohnausweis_share_token (token_hash);
        CREATE INDEX IF NOT EXISTS ix_lohnausweis_share_token_assignment
            ON lohnausweis_share_token (employee_lohn_assignment_id);

        -- Bewilligungs-Erinnerung per Kurz-SMS + Link (Walter 19.07.2026)
        CREATE TABLE IF NOT EXISTS permit_reminder_token (
            id                 serial PRIMARY KEY,
            employee_id        integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            permit_history_id  integer NOT NULL,
            token_hash         text NOT NULL,
            message_html       text NOT NULL,
            title              text,
            expires_at         timestamp without time zone NOT NULL,
            opened_at          timestamp without time zone,
            revoked_at         timestamp without time zone,
            created_at         timestamp without time zone NOT NULL DEFAULT now(),
            created_by         integer
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_permit_reminder_token_hash ON permit_reminder_token (token_hash);
        CREATE INDEX IF NOT EXISTS ix_permit_reminder_token_employee ON permit_reminder_token (employee_id);
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
            ('195.2', 'Ferienentschädigung 10.65%',  'Ferienentsch.','ZULAGE', true,  true,  true,  true,  true,  'I',     196,  true),
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

    // lohn_zulage Zeitstempel: timestamp without time zone (Npgsql + DateTime.Now).
    // Walter 01.08.2026: hart + public-Schema — sonst Monatsblatt-Import 500
    // «Cannot write DateTime with Kind=Local to timestamptz».
    db.Database.ExecuteSqlRaw(@"
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'lohn_zulage'
                  AND column_name = 'created_at' AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE public.lohn_zulage
                    ALTER COLUMN created_at TYPE timestamp without time zone
                    USING (created_at AT TIME ZONE 'Europe/Zurich');
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'lohn_zulage'
                  AND column_name = 'updated_at' AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE public.lohn_zulage
                    ALTER COLUMN updated_at TYPE timestamp without time zone
                    USING (updated_at AT TIME ZONE 'Europe/Zurich');
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'behoerde'
                  AND column_name = 'created_at' AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE public.behoerde
                    ALTER COLUMN created_at TYPE timestamp without time zone
                    USING (created_at AT TIME ZONE 'Europe/Zurich');
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'behoerde'
                  AND column_name = 'updated_at' AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE public.behoerde
                    ALTER COLUMN updated_at TYPE timestamp without time zone
                    USING (updated_at AT TIME ZONE 'Europe/Zurich');
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'lohnposition'
                  AND column_name = 'created_at' AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE public.lohnposition
                    ALTER COLUMN created_at TYPE timestamp without time zone
                    USING (created_at AT TIME ZONE 'Europe/Zurich');
            END IF;

            -- Lohnabtretung (Walter 02.08.2026): Definitiv-Confirm mit aktiver
            -- Abtretung scheiterte mit 500 — updated_at war noch timestamptz,
            -- Confirm schreibt DateTime.Now (Kind=Local) auf bereits_abgezogen.
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'employee_lohn_assignment'
                  AND column_name = 'created_at' AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE public.employee_lohn_assignment
                    ALTER COLUMN created_at TYPE timestamp without time zone
                    USING (created_at AT TIME ZONE 'Europe/Zurich');
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_schema = 'public' AND table_name = 'employee_lohn_assignment'
                  AND column_name = 'updated_at' AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE public.employee_lohn_assignment
                    ALTER COLUMN updated_at TYPE timestamp without time zone
                    USING (updated_at AT TIME ZONE 'Europe/Zurich');
            END IF;
        END $$;
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

    // ── Saldo-Vortrag Lohnpositionen 901–906 (idempotent) ───────────────
    // Braucht Monatsblatt-/CHF-Import + Saldi-Vortrag-Seite. Fehlende Codes
    // führten beim Import zu HTTP 500 «Position fehlt».
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO lohnposition
            (code, bezeichnung, kategorie, typ,
             ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig, qst_pflichtig,
             lohnausweis_code, sort_order, is_active)
        SELECT
            v.code, v.bezeichnung, 'Saldo-Vortrag', 'ZULAGE',
            false, false, false, false, false,
            NULL, v.sort_order, true
        FROM (VALUES
            ('901', 'Vortrag Zeitsaldo (Stunden)',        901),
            ('902', 'Vortrag Feiertag-Saldo (Tage)',      902),
            ('903', 'Vortrag Ferien-Saldo (Tage)',        903),
            ('904', 'Vortrag Nacht-Saldo (Stunden)',      904),
            ('905', 'Vortrag Ferien-Geld-Saldo (CHF)',    905),
            ('906', 'Vortrag 13. Monatslohn-Saldo (CHF)', 906)
        ) AS v(code, bezeichnung, sort_order)
        WHERE NOT EXISTS (SELECT 1 FROM lohnposition lp WHERE lp.code = v.code);

        UPDATE lohnposition
           SET kategorie = 'Saldo-Vortrag',
               typ       = 'ZULAGE',
               is_active = true
         WHERE code IN ('901','902','903','904','905','906')
           AND (kategorie IS DISTINCT FROM 'Saldo-Vortrag' OR is_active IS NOT TRUE);
    ");

    // ── Korrektur Quellensteuer (Mirus 565) — Walter 01.08.2026 ─────────
    // Manuelle Nachzahlung QST aus Vormonaten als Perioden-Abzug.
    // Code = Fibu-Position 565 («Korr. QST-Abzug», Konten 1920/2010).
    // Nicht SV-/QST-pflichtig (der Betrag IST schon die Steuerkorrektur).
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO lohnposition
            (code, bezeichnung, kategorie, typ,
             ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig, qst_pflichtig,
             lohnausweis_code, sort_order, is_active,
             nicht_drucken_wenn_null, nicht_im_vertrag_drucken)
        SELECT
            '565', 'Korrektur Quellensteuer', 'Abzüge', 'ABZUG',
            false, false, false, false, false,
            NULL, 565, true,
            true, true
        WHERE NOT EXISTS (SELECT 1 FROM lohnposition lp WHERE lp.code = '565');

        UPDATE lohnposition
           SET bezeichnung = 'Korrektur Quellensteuer',
               kategorie   = 'Abzüge',
               typ         = 'ABZUG',
               is_active   = true,
               ahv_alv_pflichtig = false,
               nbuv_pflichtig    = false,
               ktg_pflichtig     = false,
               bvg_pflichtig     = false,
               qst_pflichtig     = false
         WHERE code = '565'
           AND (bezeichnung IS DISTINCT FROM 'Korrektur Quellensteuer'
                OR typ IS DISTINCT FROM 'ABZUG'
                OR is_active IS NOT TRUE);

        INSERT INTO lohn_konto_mapping
            (position, sub_position, fibukonto, gegenkonto, kostenstelle_nr, kostenstelle_name,
             bezeichnung, is_vormonat, soll_buchung, sort_order, is_active)
        SELECT
            565, NULL, '1920', '2010', NULL, NULL,
            'Korr. QST-Abzug', false, true, 1770, true
        WHERE EXISTS (
            SELECT 1 FROM information_schema.tables
             WHERE table_schema = 'public' AND table_name = 'lohn_konto_mapping'
        )
          AND NOT EXISTS (
            SELECT 1 FROM lohn_konto_mapping
             WHERE position = 565 AND fibukonto = '1920' AND gegenkonto = '2010'
          );
    ");

    // ── Uniformen-Depot (Walter Aug 2026) ────────────────────────────────
    // CHF 50 beim 1. Lohn; Rückerstattung bei ordentlichem Austritt.
    // Fibu 600.32 → 1920/2021 (Kontoplan-Seed).
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS employee_uniform_depot (
            id                   serial PRIMARY KEY,
            employee_id          integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            balance              numeric(10,2) NOT NULL DEFAULT 50,
            status               varchar(20) NOT NULL DEFAULT 'EINBEHALTEN',
            charged_periode      varchar(20) NULL,
            refund_periode       varchar(20) NULL,
            return_confirmed     boolean NULL,
            return_confirmed_at  timestamp without time zone NULL,
            return_confirmed_by  integer NULL,
            bemerkung            text NULL,
            created_at           timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at           timestamp without time zone NOT NULL DEFAULT CURRENT_TIMESTAMP
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_employee_uniform_depot_emp
            ON employee_uniform_depot (employee_id);

        INSERT INTO lohnposition
            (code, bezeichnung, kategorie, typ,
             ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig, qst_pflichtig,
             lohnausweis_code, sort_order, is_active,
             nicht_drucken_wenn_null, nicht_im_vertrag_drucken)
        SELECT
            '600.32', 'Uniformen-Depot', 'Abzüge', 'ABZUG',
            false, false, false, false, false,
            NULL, 632, true,
            true, true
        WHERE NOT EXISTS (SELECT 1 FROM lohnposition lp WHERE lp.code = '600.32');

        UPDATE lohnposition
           SET bezeichnung = 'Uniformen-Depot',
               kategorie   = 'Abzüge',
               typ         = 'ABZUG',
               is_active   = true,
               ahv_alv_pflichtig = false,
               nbuv_pflichtig    = false,
               ktg_pflichtig     = false,
               bvg_pflichtig     = false,
               qst_pflichtig     = false
         WHERE code = '600.32';

        UPDATE lohn_konto_mapping
           SET bezeichnung = 'Uniformen-Depot'
         WHERE position = 600 AND sub_position = 32
           AND bezeichnung IS DISTINCT FROM 'Uniformen-Depot';

        INSERT INTO lohn_konto_mapping
            (position, sub_position, fibukonto, gegenkonto, kostenstelle_nr, kostenstelle_name,
             bezeichnung, is_vormonat, soll_buchung, sort_order, is_active)
        SELECT
            600, 32, '1920', '2021', NULL, NULL,
            'Uniformen-Depot', false, true, 1950, true
        WHERE EXISTS (
            SELECT 1 FROM information_schema.tables
             WHERE table_schema = 'public' AND table_name = 'lohn_konto_mapping'
        )
          AND NOT EXISTS (
            SELECT 1 FROM lohn_konto_mapping
             WHERE position = 600 AND sub_position = 32
          );

        -- Backfill: Eintritt vor 01.07.2026 → Depot 50 ohne Lohn-Abzug
        -- Auch bereits Ausgetretene (Korrekturlohn / Nachzahlung) — Walter Aug 2026.
        INSERT INTO employee_uniform_depot
            (employee_id, balance, status, charged_periode, bemerkung, created_at, updated_at)
        SELECT e.id, 50, 'EINBEHALTEN', 'BACKFILL',
               'Backfill: Eintritt vor 01.07.2026',
               CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
          FROM employee e
         WHERE COALESCE(e.is_hidden, false) = false
           AND COALESCE(e.is_payroll_excluded, false) = false
           AND e.entry_date IS NOT NULL
           AND e.entry_date < DATE '2026-07-01'
           AND NOT EXISTS (
               SELECT 1 FROM employee_uniform_depot d WHERE d.employee_id = e.id
           );
    ");

    // ── Korrektur UVG/KTG Lohnpositionen (Mirus 65.1/65.2/75.1/75.2) ─────
    // Walter Aug 2026: SWICA-Nachzahlung (z.B. Qazimi CHF 344) als eigene
    // Lohnarten mit SV-Flags + Feiertags-Basis. Altes «65»/«75» (ABZUG
    // Festlohn-Kürzung FIX) bleibt unangetastet.
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO lohnposition (
            code, bezeichnung, kategorie, typ,
            ahv_alv_pflichtig, nbuv_pflichtig, ktg_pflichtig, bvg_pflichtig, qst_pflichtig,
            lohnausweis_code, dreijehnter_ml_pflichtig,
            zaehlt_als_basis_feiertag, zaehlt_als_basis_ferien, zaehlt_als_basis_13ml,
            lohnausweisfeld, lohnausweis_kreuz, statistik_code,
            nicht_drucken_wenn_null, nicht_im_vertrag_drucken,
            bvg_auf_100_rechnen, position_13ml, zaehlt_fuer_tagessatz,
            sort_order, is_active, created_at
        )
        SELECT v.code, v.bezeichnung, v.kategorie, 'ZULAGE',
               v.ahv, v.nbuv, v.ktg, v.bvg, v.qst,
               v.la_code, false,
               true, false, v.ml13,
               '1', false, v.stat,
               true, true,
               true, 0, true,
               v.sort_order, true, CURRENT_TIMESTAMP
          FROM (VALUES
            ('65.1', 'Korrektur UVG Taggeld Karenz AHV pflichtig', 'Korrektur Unfall',
             true,  true,  true,  true, true,  'I', true,  'I', 66),
            ('65.2', 'Korrektur UVG Taggeld Versicherung',         'Korrektur Unfall',
             false, false, false, true, true,  'Y', false, '0', 67),
            ('75.1', 'Korrektur KTG Taggeld Karenz AHV pflichtig', 'Korrektur Krankheit',
             true,  true,  true,  true, true,  'I', true,  'I', 76),
            ('75.2', 'Korrektur KTG Taggeld Versicherung',         'Korrektur Krankheit',
             false, false, false, true, true,  'Y', false, '0', 77)
          ) AS v(code, bezeichnung, kategorie, ahv, nbuv, ktg, bvg, qst, la_code, ml13, stat, sort_order)
         WHERE NOT EXISTS (SELECT 1 FROM lohnposition lp WHERE lp.code = v.code);

        -- Bestehende Zeilen: nur Name/Kategorie/Sortierung pflegen —
        -- SV-/Basis-Flags bewusst NICHT überschreiben (Walter kann sie in der UI anpassen).
        UPDATE lohnposition AS lp
           SET bezeichnung = v.bezeichnung,
               kategorie   = v.kategorie,
               typ         = 'ZULAGE',
               sort_order  = v.sort_order,
               is_active   = true
          FROM (VALUES
            ('65.1', 'Korrektur UVG Taggeld Karenz AHV pflichtig', 'Korrektur Unfall', 66),
            ('65.2', 'Korrektur UVG Taggeld Versicherung',         'Korrektur Unfall', 67),
            ('75.1', 'Korrektur KTG Taggeld Karenz AHV pflichtig', 'Korrektur Krankheit', 76),
            ('75.2', 'Korrektur KTG Taggeld Versicherung',         'Korrektur Krankheit', 77)
          ) AS v(code, bezeichnung, kategorie, sort_order)
         WHERE lp.code = v.code;
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
            '65.1',   -- Korrektur UVG Karenz AHV
            '70',     -- Krankheit (Karenzentschädigung)
            '75',     -- Korrektur Krankheit
            '75.1',   -- Korrektur KTG Karenz AHV
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

    // SV-Sätze pro Filiale (Walter-Vorgabe 05.08.2026): NULL = globaler
    // Standard für alle Filialen; gesetzt = Override nur für diese Filiale
    // (jede Filiale ist eine eigene GmbH, z.B. KTG 1.945% statt global 2.15%).
    // Auflösung zentral in PayrollCalculations.SelectSvRatesForBranch.
    // Doku für TablePlus: migrations-archive/add_sv_rate_company_profile.sql
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE social_insurance_rate
        ADD COLUMN IF NOT EXISTS company_profile_id integer;
    ");
    // Geschlechts-Filter (Walter 06.08.2026, KTG-Fall): NULL = alle,
    // «F» = nur Frauen, «M» = nur Männer (Versicherer führten beim KTG
    // zeitweise getrennte Sätze). Doku: migrations-archive/add_sv_rate_gender.sql
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE social_insurance_rate
        ADD COLUMN IF NOT EXISTS gender varchar(1);
    ");

    // Schutz gegen künftige Dubletten: Unique-Index auf den fachlichen Schlüssel
    // (identisch mit dem Duplikat-Check in SocialInsuranceRatesController.Create).
    // COALESCE, weil min_age/max_age/employment_model_code/company_profile_id
    // NULL sein dürfen und Postgres NULLs in Unique-Indizes sonst als
    // verschieden behandelt.
    // Seit 05.08.2026 inkl. COALESCE(company_profile_id, 0) — global und
    // Filial-Override mit gleichem Schlüssel sind KEIN Duplikat. Der alte
    // Index ohne Filial-Spalte wird idempotent gedroppt (Neu-Name …_natural2).
    // Defensiv: wird nur angelegt wenn aktuell keine Dubletten existieren — so
    // crasht der Startup nicht, falls die Alt-Daten noch nicht bereinigt sind
    // (Bereinigung läuft einmalig über migrations-archive/fix_social_insurance_rate_dedup.sql).
    // Seit 06.08.2026 zusätzlich COALESCE(gender, '') im Schlüssel — F-/M-Zeilen
    // desselben Satzes sind KEIN Duplikat (Neu-Name …_natural3, alte Indizes
    // idempotent gedroppt).
    db.Database.ExecuteSqlRaw(@"
        DO $$
        BEGIN
            DROP INDEX IF EXISTS ux_social_insurance_rate_natural;
            DROP INDEX IF EXISTS ux_social_insurance_rate_natural2;
            IF NOT EXISTS (
                SELECT 1 FROM (
                    SELECT 1 FROM social_insurance_rate
                    GROUP BY code, valid_from, COALESCE(min_age, -1),
                             COALESCE(max_age, -1), COALESCE(employment_model_code, ''),
                             basis_type, only_quellensteuer,
                             COALESCE(company_profile_id, 0), COALESCE(gender, '')
                    HAVING COUNT(*) > 1
                ) dup
            ) THEN
                CREATE UNIQUE INDEX IF NOT EXISTS ux_social_insurance_rate_natural3
                ON social_insurance_rate (
                    code, valid_from, COALESCE(min_age, -1),
                    COALESCE(max_age, -1), COALESCE(employment_model_code, ''),
                    basis_type, only_quellensteuer,
                    COALESCE(company_profile_id, 0), COALESCE(gender, '')
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
            ADD COLUMN IF NOT EXISTS webseite            VARCHAR(300),
            ADD COLUMN IF NOT EXISTS handy               VARCHAR(30),
            ADD COLUMN IF NOT EXISTS kontoinhaber        VARCHAR(200),
            ADD COLUMN IF NOT EXISTS kontoinhaber_behoerde_id INTEGER
                REFERENCES behoerde(id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS idx_behoerde_kontoinhaber_behoerde
            ON behoerde(kontoinhaber_behoerde_id);
    ");

    // Sachbearbeiter-Stamm pro Behörde (Walter 02.08.2026) — Zahlung an Behörde,
    // Lohnmeldung/Lohnausweis-Mail an gewählten SB. Idempotent.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS behoerde_sachbearbeiter (
            id              SERIAL PRIMARY KEY,
            behoerde_id     INTEGER      NOT NULL REFERENCES behoerde(id) ON DELETE CASCADE,
            name            VARCHAR(150) NOT NULL,
            rolle           VARCHAR(100),
            telefon         VARCHAR(30),
            handy           VARCHAR(30),
            email           VARCHAR(200),
            erreichbarkeit  VARCHAR(150),
            bemerkung       TEXT,
            is_active       BOOLEAN      NOT NULL DEFAULT true,
            created_at      TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW(),
            updated_at      TIMESTAMP WITHOUT TIME ZONE NOT NULL DEFAULT NOW()
        );
        CREATE INDEX IF NOT EXISTS idx_behoerde_sachbearbeiter_behoerde
            ON behoerde_sachbearbeiter(behoerde_id);
        ALTER TABLE employee_lohn_assignment
            ADD COLUMN IF NOT EXISTS behoerde_sachbearbeiter_id INTEGER
            REFERENCES behoerde_sachbearbeiter(id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS idx_emp_lohn_assignment_sb
            ON employee_lohn_assignment(behoerde_sachbearbeiter_id);
        -- Pflicht-Dokument an Lohnabtretung (Walter 02.08.2026)
        ALTER TABLE employee_lohn_assignment
            ADD COLUMN IF NOT EXISTS dokument_id INTEGER
            REFERENCES employee_dokument(id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS idx_emp_lohn_assignment_dokument
            ON employee_lohn_assignment(dokument_id);
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
        -- Walter 19.07.2026: FAK-/Entscheidungsdokument an Zulage
        ALTER TABLE family_member_allowance
            ADD COLUMN IF NOT EXISTS dokument_id INTEGER
            REFERENCES employee_dokument(id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS ix_family_member_allowance_dokument
            ON family_member_allowance(dokument_id);
    ");

    // Walter 29.07.2026: Telefonnummer am Familienmitglied (v.a. Ehepartner).
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE employee_family_member
            ADD COLUMN IF NOT EXISTS phone VARCHAR(50);
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

        -- Walter 23.07.2026: Empfänger-Flag für täglichen Mirus-Änderungsdigest
        ALTER TABLE app_user
            ADD COLUMN IF NOT EXISTS receives_mirus_change_digest BOOLEAN NOT NULL DEFAULT false;

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

        -- Walter 24.07.2026: persönliches Benutzer-Postfach (User→User)
        ALTER TABLE mailbox_document
            ADD COLUMN IF NOT EXISTS target_user_id integer NULL
                REFERENCES app_user(id) ON DELETE SET NULL;
        CREATE INDEX IF NOT EXISTS ix_mailbox_document_target_user
            ON mailbox_document (target_type, target_user_id)
            WHERE target_user_id IS NOT NULL;
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
        -- ZEMIS-Nr: Duplikat zemis_nr wieder in das bestehende zemis_number
        -- konsolidiert (Walter 12.07.2026) — Daten retten, dann Spalte weg.
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM information_schema.columns
                       WHERE table_name='employee' AND column_name='zemis_nr') THEN
                UPDATE employee SET zemis_number = zemis_nr
                 WHERE zemis_number IS NULL AND zemis_nr IS NOT NULL;
                ALTER TABLE employee DROP COLUMN zemis_nr;
            END IF;
        END $$;
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
        -- easy@work-Bis weicht vom Soll ab (Walter 26.07.2026) — OneCrew speichert
        -- trotzdem das gerechnete Ende; Flag steuert Chip/ToDo.
        ALTER TABLE employee ADD COLUMN IF NOT EXISTS night_work_exam_easy_mismatch BOOLEAN NOT NULL DEFAULT false;
        -- Probezeitgespräch 1/2 (Walter 20.07.2026, Restaurant Admin)
        ALTER TABLE employee ADD COLUMN IF NOT EXISTS probezeit_gespraech1_am DATE;
        ALTER TABLE employee ADD COLUMN IF NOT EXISTS probezeit_gespraech1_dokument_id INTEGER REFERENCES employee_dokument(id) ON DELETE SET NULL;
        ALTER TABLE employee ADD COLUMN IF NOT EXISTS probezeit_gespraech2_am DATE;
        ALTER TABLE employee ADD COLUMN IF NOT EXISTS probezeit_gespraech2_dokument_id INTEGER REFERENCES employee_dokument(id) ON DELETE SET NULL;
        CREATE TABLE IF NOT EXISTS employee_pregnancy (
            id                     SERIAL PRIMARY KEY,
            employee_id            INTEGER NOT NULL REFERENCES employee(id),
            meldedatum             DATE NOT NULL,
            errechneter_termin     DATE NOT NULL,
            geburtsdatum           DATE,
            bemerkung              TEXT,
            is_active              BOOLEAN DEFAULT true,
            created_at             timestamp without time zone DEFAULT NOW(),
            updated_at             timestamp without time zone
        );
        CREATE INDEX IF NOT EXISTS idx_pregnancy_employee ON employee_pregnancy(employee_id);
        -- Walter 10.06.2026: Altlast aus erster Version droppen (Arztzeugnisse
        -- werden über den Absenzen-Tab als KRANK erfasst, nicht doppelt hier).
        ALTER TABLE employee_pregnancy DROP COLUMN IF EXISTS arztzeugnis_vorhanden;
        -- Walter 20.07.2026: TIMESTAMPTZ → timestamp without time zone (System-Standard).
        -- Nur umstellen wenn noch timestamptz (idempotent).
        DO $$
        BEGIN
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'employee_pregnancy' AND column_name = 'created_at'
                  AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE employee_pregnancy
                    ALTER COLUMN created_at TYPE timestamp without time zone
                        USING (created_at AT TIME ZONE 'Europe/Zurich');
            END IF;
            IF EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = 'employee_pregnancy' AND column_name = 'updated_at'
                  AND udt_name = 'timestamptz'
            ) THEN
                ALTER TABLE employee_pregnancy
                    ALTER COLUMN updated_at TYPE timestamp without time zone
                        USING (updated_at AT TIME ZONE 'Europe/Zurich');
            END IF;
        END $$;
        -- ISO alpha-3 (Ausweis-Kürzel BGR/MKD/…, Walter 12.07.2026) — Seed
        -- unten in C# aus der statischen Tabelle CountryIso3 (nur wo leer).
        ALTER TABLE nationality ADD COLUMN IF NOT EXISTS code3 text;
    ");

    // Walter 20.07.2026: Arztbestätigung errechneter Termin — eigener Batch,
    // damit ein Fehler hier nicht den gesamten Startup-SQL-Block abbricht.
    try
    {
        db.Database.ExecuteSqlRaw(@"
            ALTER TABLE employee_pregnancy
                ADD COLUMN IF NOT EXISTS arztbestaetigung_dokument_id INTEGER
                    REFERENCES employee_dokument(id) ON DELETE SET NULL;
        ");
    }
    catch (Exception ex)
    {
        Console.WriteLine("WARN: arztbestaetigung_dokument_id Migration: " + ex.Message);
    }

    // ISO alpha-3 nachrüsten (Walter 12.07.2026): Ausweise drucken den
    // Dreibuchstaben-Code — idempotent nur füllen, wo code3 noch leer ist
    // (manuelle Korrekturen in der DB bleiben stehen, z.B. Kosovo).
    {
        var natsOhneCode3 = db.Nationalities
            .Where(n => n.Code3 == null || n.Code3 == "")
            .ToList();
        var natChanged = 0;
        foreach (var n in natsOhneCode3)
            if (HrSystem.Services.CountryIso3.ByAlpha2.TryGetValue((n.Code ?? "").ToUpperInvariant(), out var c3))
            { n.Code3 = c3; natChanged++; }
        if (natChanged > 0) db.SaveChanges();
    }

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

    // ── Filial-Dokumentenverwaltung (Walter-Vorgabe 06.08.2026) ───────────
    // Dokumente pro FILIALE (Versicherungspolicen, AHV-Korrespondenz, QST …)
    // + Benutzer-Häkchen «Zugriff Filial-Dokumente» (admin immer).
    // Doku-Migration: migrations-archive/add_company_dokument.sql
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS company_dokument (
            id                 bigserial PRIMARY KEY,
            company_profile_id integer NOT NULL,
            kategorie          text NOT NULL,
            original_filename  text NOT NULL,
            storage_filename   text NOT NULL UNIQUE,
            bemerkung          text,
            uploaded_by_name   text,
            created_at         timestamp without time zone NOT NULL DEFAULT now(),
            zugriff_am         timestamp without time zone,
            zugriff_von        text
        );
        CREATE INDEX IF NOT EXISTS ix_company_dokument_company_profile
            ON company_dokument (company_profile_id);

        ALTER TABLE app_user
            ADD COLUMN IF NOT EXISTS can_company_dokumente boolean NOT NULL DEFAULT false;
    ");

    // ── Lohndatenempfänger (Walter-Vorgabe 06.08.2026, Mirus-Vorbild) ─────
    // Zentraler Empfänger-Katalog (Adresse/Kassennummer EINMAL erfasst) +
    // Zuordnung pro Filiale mit Mitglied-/Subnummer (jede Filiale = eigene
    // GmbH = eigene Mitgliednummer). Grundlage für Behörden-Formulare + ELM.
    // Doku-Migration: migrations-archive/add_lohndaten_empfaenger.sql
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS lohndaten_empfaenger (
            id            serial PRIMARY KEY,
            art           text NOT NULL,
            bezeichnung   text NOT NULL,
            zusatz        text,
            uid_nummer    text,
            strasse       text,
            postfach      text,
            plz           text,
            ort           text,
            kanton_code   text,
            kassennummer  text,
            support_email text,
            bemerkung     text,
            is_active     boolean NOT NULL DEFAULT true,
            created_at    timestamp without time zone NOT NULL DEFAULT now(),
            updated_at    timestamp without time zone NOT NULL DEFAULT now()
        );
        CREATE TABLE IF NOT EXISTS company_profile_empfaenger (
            id                 serial PRIMARY KEY,
            company_profile_id integer NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
            empfaenger_id      integer NOT NULL REFERENCES lohndaten_empfaenger(id) ON DELETE CASCADE,
            mitgliednummer     text,
            subnummer          text,
            bemerkung          text,
            is_active          boolean NOT NULL DEFAULT true,
            created_at         timestamp without time zone NOT NULL DEFAULT now(),
            updated_at         timestamp without time zone NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS ix_cp_empfaenger_company_profile
            ON company_profile_empfaenger (company_profile_id);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_cp_empfaenger_cp_empf
            ON company_profile_empfaenger (company_profile_id, empfaenger_id);
        ALTER TABLE company_profile_empfaenger
            ADD COLUMN IF NOT EXISTS gueltig_ab date;
    ");

    // ── Wohnort-Historie (Walter 07.08.2026): PLZ/Ort/Kanton mit Gültig-ab —
    // Umzugs-Zeitpunkt für die QST (Kantonswechsel wirkt ab Folgemonat).
    // Doku-Migration: migrations-archive/add_employee_wohnort_history.sql
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS employee_wohnort_history (
            id          serial PRIMARY KEY,
            employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            plz         text,
            ort         text,
            kanton_code text,
            gueltig_ab  date,
            bemerkung   text,
            created_at  timestamp without time zone NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS ix_wohnort_history_employee
            ON employee_wohnort_history (employee_id);
        ALTER TABLE employee_wohnort_history
            ADD COLUMN IF NOT EXISTS datum_offen boolean NOT NULL DEFAULT false;
    ");

    // ── Manager-Dienstplan (Walter 08.08.2026, ersetzt Excel «Manager DP»):
    // Plan-Zellen pro FIX-M-MA/Tag + Kürzel-Katalog + Planungsrecht pro
    // User-Filiale. Doku: migrations-archive/add_manager_dienstplan.sql
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS manager_dienstplan (
            id          serial PRIMARY KEY,
            employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            datum       date NOT NULL,
            code        text NOT NULL,
            updated_at  timestamp without time zone NOT NULL DEFAULT now(),
            updated_by  text
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_manager_dienstplan_emp_datum
            ON manager_dienstplan (employee_id, datum);
        CREATE TABLE IF NOT EXISTS dienstplan_code (
            id          serial PRIMARY KEY,
            code        text NOT NULL UNIQUE,
            bezeichnung text NOT NULL,
            farbe       text,
            sort_order  integer NOT NULL DEFAULT 0,
            is_active   boolean NOT NULL DEFAULT true
        );
        INSERT INTO dienstplan_code (code, bezeichnung, farbe, sort_order) VALUES
            ('F',   'Früh',                    NULL,      10),
            ('M',   'Mittel',                  NULL,      20),
            ('S',   'Spät',                    NULL,      30),
            ('-',   'frei',                    '#fef9c3', 40),
            ('SK',  'Shake-Maschine reinigen', '#dbeafe', 50),
            ('IV',  'Inventar',                '#e0e7ff', 60),
            ('P',   'Plan',                    '#fce7f3', 70)
        ON CONFLICT (code) DO NOTHING;
        ALTER TABLE user_branch_access
            ADD COLUMN IF NOT EXISTS can_dienstplan boolean NOT NULL DEFAULT false;
        ALTER TABLE user_branch_access
            ADD COLUMN IF NOT EXISTS can_vertrag_sms boolean NOT NULL DEFAULT false;
    ");
    // Manager-DP: Feiertage (national/kantonal/Filiale) + Schulferien pro Filiale
    // (Walter 09.08.2026). Doku: migrations-archive/add_dienstplan_feiertage_schulferien.sql
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS dienstplan_feiertag (
            id                 serial PRIMARY KEY,
            datum              date NOT NULL,
            bezeichnung        text NOT NULL,
            scope              text NOT NULL DEFAULT 'NATIONAL'
                               CHECK (scope IN ('NATIONAL','KANTON','FILIALE')),
            kanton_code        text,
            company_profile_id integer REFERENCES company_profile(id) ON DELETE CASCADE,
            created_at         timestamp without time zone NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS ix_dienstplan_feiertag_datum ON dienstplan_feiertag (datum);
        CREATE TABLE IF NOT EXISTS branch_schulferien (
            id                 serial PRIMARY KEY,
            company_profile_id integer NOT NULL REFERENCES company_profile(id) ON DELETE CASCADE,
            bezeichnung        text NOT NULL,
            von                date NOT NULL,
            bis                date NOT NULL,
            created_at         timestamp without time zone NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS ix_branch_schulferien_cp ON branch_schulferien (company_profile_id);
    ");
    // Vorstellungsgespräch-Zeitfenster der GF (Walter 09.08.2026, Stufe 1).
    // Doku: migrations-archive/add_interview_fenster.sql
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS interview_fenster (
            id          serial PRIMARY KEY,
            employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            datum       date NOT NULL,
            von_zeit    time NOT NULL,
            bis_zeit    time NOT NULL,
            bemerkung   text,
            created_at  timestamp without time zone NOT NULL DEFAULT now(),
            created_by  text
        );
        CREATE INDEX IF NOT EXISTS ix_interview_fenster_emp_datum ON interview_fenster (employee_id, datum);
        CREATE TABLE IF NOT EXISTS interview_termin (
            id         serial PRIMARY KEY,
            fenster_id integer NOT NULL REFERENCES interview_fenster(id) ON DELETE CASCADE,
            von_zeit   time NOT NULL,
            kandidat   text NOT NULL,
            telefon    text,
            bemerkung  text,
            status     text NOT NULL DEFAULT 'GEPLANT' CHECK (status IN ('GEPLANT','ABGESAGT')),
            created_at timestamp without time zone NOT NULL DEFAULT now(),
            created_by text
        );
        CREATE INDEX IF NOT EXISTS ix_interview_termin_fenster ON interview_termin (fenster_id);
    ");
    // HR-Büro-Kalender für Vorstellungsgespräche (Walter 09.08.2026, ersetzt
    // den GF-Zeitfenster-Prozess). Doku: migrations-archive/add_hr_interview_kalender.sql
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS hr_interview_termin (
            id         serial PRIMARY KEY,
            datum      date NOT NULL,
            von_zeit   time NOT NULL,
            bis_zeit   time,
            plaetze    integer NOT NULL DEFAULT 1,
            bemerkung  text,
            created_at timestamp without time zone NOT NULL DEFAULT now(),
            created_by text
        );
        CREATE INDEX IF NOT EXISTS ix_hr_interview_termin_datum ON hr_interview_termin (datum);
        -- Durchführungs-Ort des Willkommenstags (Walter 12.08.2026): frei
        -- editierbar, NULL = Default «Schulungsraum, Luzernerstr. 2, Zofingen».
        ALTER TABLE hr_interview_termin ADD COLUMN IF NOT EXISTS ort text;

        -- ── BFS Lohnstrukturerhebung (Walter 13.08.2026) ────────────────────
        -- Doku: migrations-archive/add_lse_module.sql
        CREATE TABLE IF NOT EXISTS lse_version (
            id           serial PRIMARY KEY,
            survey_year  integer NOT NULL UNIQUE,
            spec_version text,
            is_active    boolean NOT NULL DEFAULT true,
            config_json  text NOT NULL DEFAULT '{}',
            created_at   timestamp without time zone NOT NULL DEFAULT now()
        );
        CREATE TABLE IF NOT EXISTS employee_lse (
            id                   serial PRIMARY KEY,
            employee_id          integer NOT NULL UNIQUE REFERENCES employee(id) ON DELETE CASCADE,
            education            integer,
            university_degree    integer,
            position_override    integer,
            practiced_profession varchar(255),
            in_house_id          varchar(50),
            updated_at           timestamp without time zone NOT NULL DEFAULT now(),
            updated_by           text
        );
        CREATE TABLE IF NOT EXISTS lse_lohnart_mapping (
            id            serial PRIMARY KEY,
            lohnart_code  text NOT NULL,
            bezeichnung   text,
            bfs_kategorie text,
            gueltig_ab    date,
            gueltig_bis   date,
            confirmed     boolean NOT NULL DEFAULT false,
            updated_at    timestamp without time zone NOT NULL DEFAULT now(),
            updated_by    text
        );
        CREATE INDEX IF NOT EXISTS ix_lse_lohnart_mapping_code ON lse_lohnart_mapping (lohnart_code);
        ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS bur_nr varchar(8);
        ALTER TABLE company_profile ADD COLUMN IF NOT EXISTS uid_bfs varchar(20);
        CREATE TABLE IF NOT EXISTS lse_code_mapping (
            id          serial PRIMARY KEY,
            mapping_typ text NOT NULL,
            source_code text NOT NULL,
            bfs_code    integer,
            confirmed   boolean NOT NULL DEFAULT false,
            updated_at  timestamp without time zone NOT NULL DEFAULT now(),
            updated_by  text,
            UNIQUE (mapping_typ, source_code)
        );
        CREATE TABLE IF NOT EXISTS hr_interview_buchung (
            id         serial PRIMARY KEY,
            termin_id  integer NOT NULL REFERENCES hr_interview_termin(id) ON DELETE CASCADE,
            kandidat   text NOT NULL,
            telefon    text,
            bemerkung  text,
            status     text NOT NULL DEFAULT 'GEPLANT' CHECK (status IN ('GEPLANT','ABGESAGT')),
            created_at timestamp without time zone NOT NULL DEFAULT now(),
            created_by text
        );
        CREATE INDEX IF NOT EXISTS ix_hr_interview_buchung_termin ON hr_interview_buchung (termin_id);
        -- Termin-Antwort des MA über den Vertrags-Link (Walter 10.08.2026).
        ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS employee_id integer;
        ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS ma_antwort text;
        ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS ma_antwort_am timestamp without time zone;
        -- Willkommenstag-SMS an den KANDIDATEN (Walter 11.08.2026).
        ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS kandidat_id integer;
        ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS willkommen_token_hash text;
        ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS willkommen_gesendet_am timestamp without time zone;
        -- Onboarding-Abschluss nach dem Willkommenstag (Walter 11.08.2026).
        ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS onboarding_abgeschlossen_am timestamp without time zone;
        ALTER TABLE hr_interview_buchung ADD COLUMN IF NOT EXISTS onboarding_abgeschlossen_von text;
        -- Erst-Abruf der Onboarding-Dokumente über den Vertrags-Link (Walter 10.08.2026).
        CREATE TABLE IF NOT EXISTS contract_share_dok_abruf (
            id           serial PRIMARY KEY,
            token_id     integer NOT NULL REFERENCES contract_share_token(id) ON DELETE CASCADE,
            dok_id       bigint NOT NULL,
            abgerufen_am timestamp without time zone NOT NULL DEFAULT now()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_cs_dok_abruf ON contract_share_dok_abruf (token_id, dok_id);
        -- Onboarding-Termin am Vertrags-Link (Kalender-Button, Walter 10.08.2026).
        ALTER TABLE contract_share_token
            ADD COLUMN IF NOT EXISTS onboarding_termin_id integer;
        -- Kandidaten-Pipeline GF → HR (Walter 10.08.2026, Etappe 1).
        CREATE TABLE IF NOT EXISTS kandidat (
            id                  serial PRIMARY KEY,
            company_profile_id  integer NOT NULL REFERENCES company_profile(id),
            vorname             text NOT NULL,
            name                text NOT NULL,
            telefon             text,
            fruehester_eintritt date,
            lgav_ausbildung     text,
            wunsch_termin_id    integer REFERENCES hr_interview_termin(id) ON DELETE SET NULL,
            bemerkung           text,
            status              text NOT NULL DEFAULT 'NEU'
                                CHECK (status IN ('NEU','ANGENOMMEN','ABGELEHNT','ERLEDIGT')),
            ablehnungsgrund     text,
            created_at          timestamp without time zone NOT NULL DEFAULT now(),
            created_by          text,
            decided_at          timestamp without time zone,
            decided_by          text
        );
        CREATE INDEX IF NOT EXISTS ix_kandidat_status ON kandidat (status);
        CREATE TABLE IF NOT EXISTS kandidat_dokument (
            id                serial PRIMARY KEY,
            kandidat_id       integer NOT NULL REFERENCES kandidat(id) ON DELETE CASCADE,
            original_filename text NOT NULL,
            storage_filename  text NOT NULL,
            created_at        timestamp without time zone NOT NULL DEFAULT now(),
            created_by        text
        );
        CREATE INDEX IF NOT EXISTS ix_kandidat_dokument_kandidat ON kandidat_dokument (kandidat_id);
        ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS email text;
        ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS absage_gesendet_am timestamp without time zone;
        ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS absage_kanal text;
        ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS erledigt_am timestamp without time zone;
        ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS verknuepft_employee_id integer;
        ALTER TABLE kandidat ADD COLUMN IF NOT EXISTS notiz text;
        -- Wunschtermin überlebt die Kandidat-Löschung am MA (Walter 10.08.2026).
        CREATE TABLE IF NOT EXISTS onboarding_wunsch (
            id          serial PRIMARY KEY,
            employee_id integer NOT NULL REFERENCES employee(id) ON DELETE CASCADE,
            termin_id   integer NOT NULL REFERENCES hr_interview_termin(id) ON DELETE CASCADE,
            created_at  timestamp without time zone NOT NULL DEFAULT now()
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_onboarding_wunsch_emp ON onboarding_wunsch (employee_id);
    ");

    // ── BFS LSE: Version 2024 seeden (Walter 13.08.2026, idempotent) ──
    // Grundlage: technische BFS-Spezifikation LSE 2024, V1.4/12.2024.
    // Codes/Bereiche/Pflichtfelder/Exportreihenfolge liegen als KONFIGURATION
    // in lse_version.config_json — die Labels sind dort editierbar und werden
    // beim Start NIE überschrieben (nur Insert in leere Version). LSE 2026 =
    // neue Zeile mit eigener Konfiguration, keine Code-Änderung.
    if (!db.LseVersions.Any(v => v.SurveyYear == 2024))
    {
        var lseConfig = new
        {
            columns = new[]
            {
                // A–S Unternehmensdaten (nur Zeile 2)
                "surveyYear","name1","name2","name3","street","zipCode","town",
                "postOfficeBoxNumber","postOfficeBoxText","lastName","firstName",
                "emailAddress","phoneNumber","mobilephoneNumber","numberOfEmployeesOct",
                "selection","payAgreement","uidBFS","Producer",
                // T–AS Mitarbeiterdaten (pro MA eine Zeile)
                "vn","education","universityDegree","entryDate","position","contract",
                "basisOfSalaryCalculation","contractualWorkingTime","activityRateOct",
                "leaveEntitlement","practicedProfessionOct","salaryOct","allowancesOct",
                "familyAllowanceOct","socialContributionsOct","bvgLPPRegularContributionsOct",
                "from","until","earnings13th","overtime","irregularPayments",
                "fringeBenefits","capitalPayments","othersBenefits","burNr","inHouseID",
            },
            codes = new
            {
                education = new[]
                {
                    new { code = 1, label = "Universität / ETH" },
                    new { code = 2, label = "Fachhochschule / Pädagogische Hochschule" },
                    new { code = 3, label = "Höhere Berufsausbildung (eidg. Fachausweis, Diplom HF)" },
                    new { code = 4, label = "Lehrkräfte-Ausbildung" },
                    new { code = 5, label = "Matura" },
                    new { code = 6, label = "Abgeschlossene Berufsausbildung (EFZ/EBA)" },
                    new { code = 7, label = "Unternehmensinterne Ausbildung" },
                    new { code = 8, label = "Ohne abgeschlossene Berufsausbildung" },
                },
                universityDegree = new[]
                {
                    new { code = 1, label = "Doktorat" },
                    new { code = 2, label = "Master" },
                    new { code = 3, label = "Bachelor" },
                },
                position = new[]
                {
                    new { code = 1, label = "Oberstes / oberes Kader" },
                    new { code = 2, label = "Mittleres Kader" },
                    new { code = 3, label = "Unteres Kader" },
                    new { code = 4, label = "Unterstes Kader" },
                    new { code = 5, label = "Ohne Kaderfunktion" },
                },
                contract = new[]
                {
                    new { code = 1, label = "Unbefristeter Vertrag" },
                    new { code = 2, label = "Befristeter Vertrag" },
                    new { code = 3, label = "Lehrvertrag" },
                    new { code = 4, label = "Praktikumsvertrag" },
                    new { code = 5, label = "Vertrag auf Abruf" },
                    new { code = 6, label = "Temporärvertrag" },
                    new { code = 7, label = "Anderer Vertrag" },
                },
                basisOfSalaryCalculation = new[]
                {
                    new { code = 1, label = "Monatslohn" },
                    new { code = 2, label = "Stundenlohn" },
                    new { code = 3, label = "Lektionslohn" },
                },
            },
            ranges = new
            {
                vnMin = 7560000000001L, vnMax = 7569999999999L,
                activityRateMin = 1, activityRateMax = 175,
                leaveMin = 0, leaveMax = 99,
                professionMaxLen = 255,
            },
            mandatory = new[]
            {
                "vn","education","entryDate","position","contract",
                "basisOfSalaryCalculation","contractualWorkingTime","activityRateOct",
                "leaveEntitlement","practicedProfessionOct","salaryOct",
                "socialContributionsOct","from","until",
            },
            referenceMonth = 10,
        };
        db.LseVersions.Add(new LseVersion
        {
            SurveyYear = 2024,
            SpecVersion = "1.4 / 12.2024",
            IsActive = true,
            ConfigJson = System.Text.Json.JsonSerializer.Serialize(lseConfig),
        });
        db.SaveChanges();
    }

    // Dashboard-Warnung: Umzugsdatum aus easy@work-Adresswechsel bestätigen.
    db.Database.ExecuteSqlRaw(@"
        INSERT INTO dashboard_warning_config
            (category, label, enabled, warn_days, escalate_days, severity_base, severity_escalated, is_date_based, sort_order, todo_priority, warn_color)
        VALUES
            ('umzug_datum_offen', 'Umzugsdatum bestätigen (QST)', TRUE, NULL, NULL, 'warning', NULL, FALSE, 27, 17, 'red')
        ON CONFLICT (category) DO NOTHING;
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
    // Kamera = (self): nötig für Scanbot-Dokument-Scan im Posteingang (getUserMedia).
    // Mikrofon/Geolocation bleiben bewusst gesperrt.
    h["Permissions-Policy"]     = "geolocation=(), microphone=(), camera=(self)";
    // HSTS: vom Browser nur über HTTPS beachtet, daher unbedingt setzen (nginx
    // terminiert TLS und proxyt HTTP an Kestrel — Request.IsHttps wäre hier false).
    h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    // Walter-Vorgabe 13.06.2026: CSP — erschwert XSS-Angriffe deutlich.
    //   • default-src 'self'                 — alles standardmäßig nur von eigener Domain
    //   • script-src 'self' 'unsafe-inline' + cdnjs (SheetJS/xlsx) + 'wasm-unsafe-eval'
    //     (Scanbot Web SDK / WebAssembly — ohne wasm-unsafe-eval scheitert Worker-Init)
    //   • worker-src 'self' blob: data:      — Scanbot WASM-Worker (blob:-URLs)
    //   • style-src 'self' 'unsafe-inline' + Google Fonts CSS
    //   • font-src  'self' + Google Fonts Files
    //   • img-src   'self' + data: + blob: (Foto-Preview, Doku-Vorschau)
    //   • media-src 'self' blob:             — Kamera-Stream für Dokument-Scan
    //   • frame-src 'self' + blob:           — PDF-Vorschau im <iframe> mit
    //                                          URL.createObjectURL(blob) → blob:https://…
    //   • connect-src 'self'                 — API-Calls nur an eigene Domain
    // Wenn nach Deploy etwas im Browser nicht mehr lädt → F12 Console zeigt
    // welche Direktive blockt → hier nachschärfen.
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'wasm-unsafe-eval' https://cdnjs.cloudflare.com; " +
        "worker-src 'self' blob: data:; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data: blob:; " +
        "media-src 'self' blob:; " +
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
// Scanbot Web SDK (WASM + Worker) — korrekte MIME, sonst laden die Engine-Files nicht.
mdMime.Mappings[".wasm"] = "application/wasm";
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

// PLZ/Ortschaft (AMTOVZ): bei altem/falschem Stand automatisch aus CSV neu laden
// (Walter 29.07.2026 — TablePlus-Reimport wurde oft übersprungen; Sentinel z.B.
// Thörigen unter PLZ 3360). CSV liegt neben der DLL (CopyToOutputDirectory).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await SwissLocationReimportService.EnsureFreshAsync(db, app.Environment.ContentRootPath);
}

app.Run();