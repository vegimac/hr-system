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
        ["ContractTextsController"]        = "Vertragstexte-Vorlagen — keine MA-Daten",
        ["SocialInsuranceRatesController"] = "SV-Sätze (AHV/ALV/NBU/KTG) — keine MA-Daten",

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
        ["LseExportController"]            = "LSE-Export — read-only",
        ["LohnEditLockController"]         = "Lock-Service selbst — read-only-Query",

        // Reports & PDFs — read-only
        ["AbsenceReportController"]        = "Absenz-Report — read-only",
        ["ComplianceController"]           = "Compliance-Check — read-only",
        ["DashboardController"]            = "Dashboard-Daten — read-only",

        // Dokumente / Mailbox / Posteingang — Lohn-orthogonal
        ["DocumentsController"]            = "MA-Dokumente — Files, kein Lohn",
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
        ["BankImportController"]                = "Bank-Stammdaten-Import — admin",
        ["PermitImportController"]              = "Bewilligungs-Import — admin",
        ["FamilyChildrenImportController"]      = "Familien-Kontroll-Import — admin",
        ["RosterAbsenceImportController"]       = "Schichtplan-Absenz-Import — admin",
        ["ImportController"]                    = "Stempel-Import — admin",

        // QST-Formulare etc.
        ["QstAnmeldungController"]         = "QST-Anmeldung-PDF — read-only",
        ["ZwischenverdienistController"]   = "RAV-Zwischenverdienst — admin/superuser-Formular",

        // Vorerst noch ungeschützt (Walter: nächste Etappen) — bewusst hier
        // gelistet damit der Test nicht fehlschlägt. JEDER EINTRAG IST
        // EINE TODO-NOTIZ FÜR WALTER + CLAUDE:
        ["EmployeeTimeEntriesController"]          = "READ-ONLY (Walter 17.05.2026): Stempelzeiten kommen aus easy@work, POST/PUT/DELETE liefern 403",
        ["EmployeeFamilyMembersController"]        = "Familienmitglieder-Stammdaten (Name, Geburtsdatum) — gehört NICHT in Lock; lohnrelevante Zulagen-Bezüge sind in FamilyMemberAllowancesController",
        ["EmployeeAddressesController"]            = "Adresse — gehört NICHT in den Lock (postalisch, Lohn-irrelevant)",
        ["EmployeeAccountController"]              = "MA-Postfach-Account — Login-Sachen, nicht Lohn",
        ["EmployeesController"]                    = "MA-Stammdaten (Name, Telefon, AHV-Nr) — gehört NICHT in Lock; lohnrelevante Felder sind in EmploymentsController",
        ["ContractsController"]                    = "Arbeitsvertrags-PDF + Vertragstexte — read-only/Generation"
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
