using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace HrSystem.Tests;

/// <summary>
/// Audit-Test (Walter-Vorgabe 17.05.2026, "königliche Kontrolle"):
///
/// Scannt ALLE Controller-Files im Repo und stellt sicher, dass jeder
/// POST/PUT/DELETE-Endpoint, der lohnrelevante Daten anfasst, entweder:
///
///   (a) den <c>LohnEditLockService</c> nutzt (per `_editLock`-Feld oder
///       direkte Verwendung der `LohnEditLockService`-Klasse), ODER
///   (b) in der LOCK_IRRELEVANT-Whitelist steht (= ausdrücklich
///       lohn-unkritisch, z.B. Logout, Lookups, Reports).
///
/// Damit fängt der Test JEDEN neuen Edit-Endpoint ab. Wer einen neuen
/// Controller mit Edit-Endpoint anlegt, ohne den Lock einzubauen, muss
/// sich aktiv entscheiden: entweder Lock einbauen, oder Endpoint
/// explizit in die Whitelist eintragen (mit Begründung).
///
/// Lauf:   dotnet test --filter EditLockEndpointAuditTests
/// </summary>
public class EditLockEndpointAuditTests
{
    private readonly ITestOutputHelper _out;
    public EditLockEndpointAuditTests(ITestOutputHelper outHelper)
    {
        _out = outHelper;
    }

    // Controller, deren Edit-Endpoints LOHN-IRRELEVANT sind und daher
    // bewusst KEINEN LohnEditLockService brauchen. Pro Eintrag eine
    // Begründung — beim Hinzufügen einer Zeile die Begründung pflegen.
    //
    // WICHTIG: Wenn ein Controller hier steht, sind ALLE seine Edit-
    // Endpoints whitegelistet. Wenn nur einzelne Endpoints unkritisch
    // sind, das Whitelist-Pattern an den Endpoint-Namen verschieben.
    private static readonly Dictionary<string, string> LOCK_IRRELEVANT_CONTROLLERS = new()
    {
        // Authentifizierung / User-Verwaltung — kein Lohn-Bezug
        ["AuthController"]                 = "Login/Logout/Password — keine Lohndaten",
        ["UsersController"]                = "Benutzer-Stammdaten (Anlage, Rolle) — keine Lohndaten",
        ["UserBranchController"]           = "User↔Filial-Zuordnung — keine Lohndaten",
        ["AdminSmtpController"]            = "SMTP-Konfiguration — keine Lohndaten",
        ["ManagerSchulungenController"]    = "Manager-Schulungen (Nothelfer/Peak/Seco) + eID/SSO — Katalog/Stammdaten, kein Lohn",
        ["MaEmailController"]              = "Gruppen-E-Mail an MA — reiner Mail-Versand, keine Lohndaten",
        ["EcallController"]                = "eCall-SMS-Konfig + Test-Versand — keine Lohndaten",
        ["DvelopApiController"]            = "d.velop-API-Konfig + Read-only-Probe — keine Lohndaten",
        ["AppSettingsController"]          = "Globale App-Einstellung (Stempelzeiten-Aufbewahrung) — keine Lohndaten",
        ["AdminDataFixController"]         = "Admin Daten-Fix (Personalnummer) — Stammdaten-Korrektur, kein datum-basiertes Lohn-Objekt; Audit via Interceptor",

        // Stammdaten / Lookups / Kataloge — Lohn-neutral
        ["BanksController"]                = "Bank-Stammdaten (SIX-Liste) — keine MA-Daten",
        ["BehoerdenController"]            = "Behörden-Adressen — keine Lohndaten",
        ["JobGroupsController"]            = "Berufsgruppen-Katalog — keine Lohndaten",
        ["EducationLevelsController"]      = "Bildungsstufen-Katalog — keine Lohndaten",
        ["NationalitiesController"]        = "Nationalitäten-Katalog — keine Lohndaten",
        ["PermitTypesController"]          = "Bewilligungstypen-Katalog — keine Lohndaten",
        ["SwissLocationsController"]      = "PLZ/Gemeinde-Katalog — keine Lohndaten",
        ["AbsenzTypenController"]          = "Absenztypen-Katalog (KRANK/UNFALL/…) — keine MA-Daten",
        ["LohnpositionController"]         = "Lohnpositions-Katalog — keine MA-Daten",
        ["EmploymentModelComponentsController"] = "Vertragsmodell-Komponenten-Katalog",
        ["DeductionRulesController"]       = "Abzug-Regeln-Katalog — keine MA-Daten",
        ["QuellensteuerTarifController"]   = "QST-Tarif-Tabellen (Kanton) — keine MA-Daten",
        ["QuellensteuerAdminController"]   = "QST-Admin-Funktionen (Kanton-Konfig) — keine MA-Daten",
        ["FamilienzulagenTarifeController"]= "Familienzulagen-Tarife (Kanton) — keine MA-Daten",
        ["PregnancyRulesController"]       = "Mutterschafts-Fristen-/Lohnersatz-Regelwerk (Katalog, gesetzliche Fristen) — keine MA-Daten",
        ["ContractTextsController"]        = "Vertragstexte-Vorlagen — keine MA-Daten",
        ["SocialInsuranceRatesController"] = "SV-Sätze (AHV/ALV/NBU/KTG) — keine MA-Daten",
        ["MinimumWageRulesController"]     = "L-GAV Mindestlohn-Sätze (Katalog, versioniert) — keine MA-Daten",
        ["BranchMinWageController"]        = "Kommunaler Mindestlohn pro Filiale (Katalog, versioniert) — keine MA-Daten",
        ["LohnKontoMappingController"]     = "Kontoplan / Lohnart→Konten-Mapping (Katalog) — keine MA-Daten",
        ["DashboardWarningConfigController"] = "Dashboard-Warnungs-Konfig (global, an/aus/Vorlauf/Schweregrad) — keine MA-Daten",
        ["EmployeeAvailabilityController"] = "Verfügbarkeit (verfügbare Arbeitszeiten, versioniert am MA) — reine Planungsangabe, kein datum-basiertes Lohn-Objekt (kein Betrag/Absenz/Snapshot)",
        // EasyAtWorkController NICHT mehr whitelisted (Walter-Vorgabe 19.06.2026):
        // er schreibt via Stempelzeit-Commit lohnrelevante employee_time_entry-Daten
        // und ist deshalb jetzt ECHT lock-geschützt — der Commit-Endpoint berechnet
        // firstAllowed über _editLock (LohnEditLockService) und reicht es in den
        // gemeinsamen, lock-gegateten Schreibpfad. Der Audit erkennt das am
        // LohnEditLockService-Bezug im Controller.
        ["EasyAtWorkNeuzugangController"]  = "GF-Einzelimport neuer/aktiver MA (Walter 08.07.2026) — delegiert an EasyAtWorkEmployeeSyncService (derselbe lock-bewusste Schreibpfad wie der Admin-Emp-Sync: Verträge in abgeschlossenen Perioden → SkippedContracts, keine Stempel-/Betrags-Writes); OnlyActive=true fest verdrahtet",
        ["WebAuthnController"]             = "Passkey/WebAuthn-Login (Registrierung + Assertion) — reine Authentifizierung, keine Lohndaten",
        ["PostfachSetupController"]        = "Onboarding-/Reset-QR für das MA-Postfach (Token + Passwort-Setzen) — Login-Sachen, keine Lohndaten",
        ["MomentsController"]              = "Moments (persönliche Mitteilungen): Token-Link/Postfach-Notiz + eCall-SMS — keine Lohndaten, kein datum-basiertes Lohn-Objekt",
        ["MomentContentController"]        = "Moments-Vorlagen-Katalog (Typen, Emotionsgrade, Texte) — reine Vorlagen, keine MA-Daten",

        // Firmen-Stammdaten — admin only, gehört nicht in den User-Lock
        ["CompanyProfilesController"]      = "Filial-Stammdaten — admin-only, kein User-Edit-Pfad",
        ["CompanyProfileBankAccountsController"] = "Filial-Bankkonten — admin-only",
        ["CompanyProfileSslController"]    = "SSL-Konfig — admin-only",

        // Workflow-Endpoints — sind selbst der Lohnlauf-Workflow, nicht editierbar dadurch
        ["PayrollController"]              = "Lohnberechnung + Snapshot-Commit — kein normaler Edit",
        ["PayrollPeriodeController"]       = "Periode-Lifecycle — Lock-Quelle selbst",
        ["AkontoController"]               = "Akonto-Berechnungs-Endpoints — admin/superuser only",
        ["AkontoWorkflowController"]       = "Akonto-Workflow (Freigeben, DTA) — admin/superuser only",
        ["AkontoTerminController"]         = "Akonto-Termin-Konfig — admin only",
        ["LohnlaufController"]             = "Definitivlauf-Workflow — admin only",
        ["LohnausweisController"]          = "Lohnausweis-PDF generieren — read-only",
        ["KuendigungController"]           = "Kündigungsschreiben-PDF + optionales Eintragen von Gekündigt-am/per am MA — keine Lohndaten/Lohnedit",
        ["AufforderungZurArbeitController"] = "Aufforderung-zur-Arbeit-PDF (unentschuldigtes Fernbleiben) — reine Dokument-Generation, keine Lohndaten",
        ["ExitSurveyController"]           = "Anonymer Austritts-Fragebogen (öffentliche Abgabe + HR-Liste) — keine Lohndaten/Lohnedit",
        ["ArbeitszeugnisController"]       = "Arbeitszeugnis-PDF generieren — read-only, keine Lohndaten",
        ["EmployeeVerwarnungController"]   = "Verwarnungs-Verlauf — Personalakte, kein Lohnbezug (Storno statt Löschen)",
        ["MutterschaftVereinbarungController"] = "Mutterschafts-Checkliste + Vereinbarung als PDF — read-only, keine Lohndaten",
        ["AerzteController"]               = "Ärzte-Verzeichnis — Katalogdaten, kein Lohnbezug",
        ["LseExportController"]            = "LSE-Export — read-only",
        ["LohnEditLockController"]         = "Lock-Service selbst — read-only-Query",

        // Reports & PDFs — read-only
        ["AbsenceReportController"]        = "Absenz-Report — read-only",
        ["ComplianceController"]           = "Compliance-Check — read-only",
        ["DashboardController"]            = "Dashboard-Daten — read-only",
        ["AuditLogController"]             = "Audit-Log — read-only Admin-Sicht (kein Edit)",
        ["SearchController"]               = "Globale Suche — read-only über mehrere Quellen",
        ["MirusChangeDigestController"]    = "Mirus-Änderungsdigest Trigger — nur Mail-Versand, kein Lohn-Edit",

        // Dokumente / Mailbox / Posteingang — Lohn-orthogonal
        ["DocumentsController"]            = "MA-Dokumente — Files, kein Lohn",
        ["CompanyDokumenteController"]     = "Filial-Dokumente — Files, kein Lohn",
        ["MailboxController"]              = "Posteingang/Postfach — Files, kein Lohn",
        ["WebDavController"]               = "WebDAV-Zugriff — Files",

        // Importer — laufen typischerweise vor dem ersten Lohnlauf,
        // sind admin/superuser-restricted. Lock-Check hier wäre falsch
        // (wir wollen ja gerade Initialdaten anlegen können).
        ["EmployeeImportController"]            = "easy@work-Import — admin/superuser, vor Lohnlauf",
        ["EmployeeImportArchivedController"]    = "Archiv-Import — einmalig, admin",
        ["EmployeeImportSnapshotController"]    = "Snapshot-Import — admin",
        ["EmployeeStammdatenImportController"]  = "Stammdaten-Import — admin",
        ["DvelopImportController"]              = "d.velop-Import — admin",
        // BankImportController entfernt (Walter-Vorgabe 07.06.2026)
        ["PermitImportController"]              = "Bewilligungs-Import — admin",
        ["HrReviewImportController"]            = "Mirus HR-Review-Import — admin/superuser",
        ["QstImportController"]                 = "Mirus QST-Auswertung-Import — admin/superuser",
        ["KontrollListenController"]            = "Kontroll-Listen — read-only, keine Lohndaten",
        ["FamilyChildrenImportController"]      = "Familien-Kontroll-Import — admin",
        ["RosterAbsenceImportController"]       = "Schichtplan-Absenz-Import — admin",
        ["ImportController"]                    = "PDF-Stempelzeiten-Import ENTFERNT (Walter 19.06.2026) — Endpunkte liefern nur noch 410 Gone, kein Schreibpfad in employee_time_entry. Stempelzeiten kommen ausschliesslich über die easy@work-API.",
        ["SaldoVortragImportController"]        = "Saldo-Vortrag Bulk-Import (Mirus Saldomethode) — admin/superuser, einmalige Migration",
        ["MirusAddressCompareController"]       = "Mirus Adressliste-Vergleich — read-only Auswertung, kein Schreibpfad",

        // QST-Formulare etc.
        ["QstAnmeldungController"]         = "QST-Anmeldung-PDF — read-only",
        ["ZwischenverdienistController"]   = "RAV-Zwischenverdienst — admin/superuser-Formular",
        ["AhvAnmeldungController"]         = "AHV-Anmeldung 318.260 — reines Ausgabe-Formular (POST erzeugt nur PDF, persistiert nichts)",
        ["ManagerDienstplanController"]    = "Manager-Dienstplan (Schicht-Kürzel pro Tag) — reine Planung, keine Lohndaten",
        ["HrInterviewController"]          = "HR-Büro-Kalender Vorstellungsgespräche — reine Planung, keine Lohndaten",
        ["KandidatenController"]           = "Kandidaten-Pipeline GF→HR (Rekrutierung, Anhänge) — keine Lohndaten",
        ["LseController"]                  = "BFS Lohnstrukturerhebung — Statistik-Mappings/Ergänzungsfelder, keine lohnwirksamen Daten",
        ["OnboardingDokumenteController"]  = "Onboarding-PDF-Ordner pro Filiale (Vertrags-Link-Anhänge) — reine Dateiablage, keine Lohndaten",
        ["LohndatenEmpfaengerController"]  = "Lohndatenempfänger-Katalog + Filial-Zuordnung (Mitglied-/Subnummer; beide Controller in dieser Datei) — Stammdaten, keine Lohndaten",

        // Vorerst noch ungeschützt (Walter: nächste Etappen) — bewusst hier
        // gelistet damit der Test nicht fehlschlägt. JEDER EINTRAG IST
        // EINE TODO-NOTIZ FÜR WALTER + CLAUDE:
        ["EmployeeTimeEntriesController"]          = "READ-ONLY (Walter 17.05.2026): Stempelzeiten kommen aus easy@work, POST/PUT/DELETE liefern 403",
        ["EmployeeFamilyMembersController"]        = "Familienmitglieder-Stammdaten (Name, Geburtsdatum) — gehört NICHT in Lock; lohnrelevante Zulagen-Bezüge sind in FamilyMemberAllowancesController",
        ["PregnancyController"]                    = "Mutterschafts-Tracking pro MA (Melde-/Termin-/Geburtsdatum; Fristen live bei GET berechnet) — fliesst NICHT in den Lohnlauf (kein Payroll-Service liest EmployeePregnancies); die lohnrelevante Absenz läuft über AbsencesController (dort ist der Lock)",
        ["EmployeeAddressesController"]            = "Adresse — gehört NICHT in den Lock (postalisch, Lohn-irrelevant)",
        ["EmployeeAccountController"]              = "MA-Postfach-Account — Login-Sachen, nicht Lohn",
        ["EmployeeUniformDepotController"]         = "Uniformen-Depot Stammdaten + Rückgabe-Entscheidung + Admin-Backfill — Abzug/Refund läuft über Lohnlauf (Engine/Confirm), kein datum-basiertes Lohn-Edit",
        ["EmployeesController"]                    = "MA-Stammdaten (Name, Telefon, AHV-Nr) — gehört NICHT in Lock; lohnrelevante Felder sind in EmploymentsController",
        ["EmployeeNumberAliasController"]          = "Alte Personalnummern (Identitäts-/Stammdaten) — gehört NICHT in Lock, kein Lohn-Datum",
        ["EmployeeMergeController"]                = "Einmalige Duplikat-Bereinigung (admin) — Stammdaten-Zusammenführung, kein Lohn-Datum",
        ["ContractsController"]                    = "Arbeitsvertrags-PDF + Vertragstexte — read-only/Generation",
        ["ContractShareController"]                = "Öffentlicher Vertrags-Link-Token (Create) + anonyme PDF-Auslieferung — read-only-Generation, kein Lohn-Datum"
    };

    [Fact]
    public void Audit_AllControllerEditEndpoints_AreEitherLockedOrWhitelisted()
    {
        var controllersDir = FindControllersDir();
        var files = Directory.GetFiles(controllersDir, "*.cs", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".bak"))
            .ToArray();

        Assert.NotEmpty(files);
        _out.WriteLine($"Audit von {files.Length} Controller-Files in {controllersDir}");

        var unprotected = new List<string>();
        var lockedCount = 0;
        var whitelistedCount = 0;
        var editEndpointCount = 0;

        foreach (var file in files)
        {
            var controllerName = Path.GetFileNameWithoutExtension(file);
            var content = File.ReadAllText(file);

            // Hat der Controller den LohnEditLockService injected oder direkt verwendet?
            var hasLockService = Regex.IsMatch(content, @"LohnEditLockService|_editLock\b");
            var isWhitelisted  = LOCK_IRRELEVANT_CONTROLLERS.ContainsKey(controllerName);

            // Pattern: [HttpPost], [HttpPut], [HttpDelete] (mit optionalen Route-Args)
            var endpointMatches = Regex.Matches(content,
                @"\[Http(Post|Put|Delete|Patch)(?:\(.*?\))?\]",
                RegexOptions.IgnoreCase);

            if (endpointMatches.Count == 0) continue;

            editEndpointCount += endpointMatches.Count;

            if (hasLockService)
            {
                lockedCount += endpointMatches.Count;
                _out.WriteLine($"  ✓ {controllerName}: {endpointMatches.Count} Edit-Endpoint(s), Lock-Service eingebunden");
            }
            else if (isWhitelisted)
            {
                whitelistedCount += endpointMatches.Count;
                _out.WriteLine($"  ⚪ {controllerName}: {endpointMatches.Count} Edit-Endpoint(s), whitegelistet ({LOCK_IRRELEVANT_CONTROLLERS[controllerName]})");
            }
            else
            {
                unprotected.Add(
                    $"  ✗ {controllerName}: {endpointMatches.Count} Edit-Endpoint(s) ohne Lock-Service und ohne Whitelist-Eintrag.\n" +
                    $"     → ENTWEDER {controllerName} mit LohnEditLockService verdrahten und _editLock im Edit-Pfad aufrufen,\n" +
                    $"     ODER {controllerName} in LOCK_IRRELEVANT_CONTROLLERS aufnehmen mit Begründung.");
            }
        }

        _out.WriteLine("");
        _out.WriteLine($"Zusammenfassung: {editEndpointCount} Edit-Endpoints in {files.Length} Controllern");
        _out.WriteLine($"  Lock-Service eingebunden : {lockedCount}");
        _out.WriteLine($"  Whitegelistet            : {whitelistedCount}");
        _out.WriteLine($"  Ungeschützt              : {unprotected.Count}");

        Assert.Empty(unprotected);
    }

    [Fact]
    public void Audit_WhitelistEntries_AllReferenceExistingControllers()
    {
        var controllersDir = FindControllersDir();
        var existingControllers = Directory.GetFiles(controllersDir, "*.cs")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(n => !n.EndsWith(".bak"))
            .ToHashSet();

        var stale = LOCK_IRRELEVANT_CONTROLLERS.Keys
            .Where(c => !existingControllers.Contains(c))
            .ToList();

        Assert.True(stale.Count == 0,
            "Stale Whitelist-Einträge (Controller existiert nicht mehr):\n  " + string.Join("\n  ", stale));
    }

    // ──────────────────────────────────────────────────────────────────
    // Helper: Controllers-Verzeichnis finden (Test läuft aus bin/Debug)
    // ──────────────────────────────────────────────────────────────────

    private static string FindControllersDir()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "Controllers");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "AbsencesController.cs")))
                return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new DirectoryNotFoundException(
            "Konnte das Controllers/-Verzeichnis nicht finden. Test muss aus dem Repo-Wurzel laufen.");
    }
}
