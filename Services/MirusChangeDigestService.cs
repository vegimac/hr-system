using System.Globalization;
using System.Text;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Änderungsdigest für Mirus-Sachbearbeiter (Walter 23.07.2026).
/// Bis Mirus abgelöst ist: OneCrew pflegt Stammdaten/Verträge/Bank/QST/…,
/// Stempelzeiten+Absenzen gehen schon automatisch. Die Sachbearbeiter
/// brauchen morgens (Mo–Fr 06:00) eine Mail mit lohnkritischen Änderungen
/// seit dem letzten Werktag-Slot (Montag = Fr–Mo, sonst ca. 24 h).
///
/// Empfänger: AppUser.ReceivesMirusChangeDigest + aktive E-Mail.
/// Scope: Filialen aus user_branch_access (admin/superuser ohne UBA = alle).
/// Quelle: audit_log, gefiltert auf Whitelist Entity/Feld.
/// </summary>
public class MirusChangeDigestService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly ILogger<MirusChangeDigestService> _log;

    // Array (nicht HashSet): EF Core übersetzt local.Contains(column) zuverlässig zu SQL IN.
    private static readonly string[] WatchedEntityList =
    {
        "Employee", "Employment", "EmployeeBankAccount", "EmployeeQuellensteuer",
        "EmployeePermitHistory", "EmployeeRecurringWage", "EmployeeLohnAssignment",
        "EmployeeFamilyMember", "FamilyMemberAllowance", "EmployeeBvgZusatzMember",
        "LohnZulage", "EmployeeAddress"
    };
    private static readonly HashSet<string> WatchedEntities = new(WatchedEntityList, StringComparer.Ordinal);

    private static readonly HashSet<string> EmployeeFields = new(StringComparer.Ordinal)
    {
        // FirstName/LastName bewusst NICHT — Mirus-Mail enthält nie MA-Namen (Walter 23.07.2026)
        "AhvNumber", "Street", "HouseNumber", "Zip", "City",
        "CantonCode", "Country", "Nationality", "NationalityId", "MaritalStatus",
        "MaritalStatusSince", "MaidenName", "SeparatedSince", "Religion",
        "EntryDate", "ExitDate", "KuendigungAusgesprochenAm", "KuendigungPer",
        "KuendigungDurch", "Austrittsgrund", "ZemisNumber", "PlaceOfOrigin",
        "IsActive", "IsPayrollExcluded", "QstBefreitDurchBehoerde",
        "QstBefreiungGueltigAb", "QstBefreiungGueltigBis", "LgavPflichtig",
        "TeilzeitUnter8hWoche", "IdPassDokumentId", "CAusweisDokumentId",
        "BirthDate", "PhoneMobile", "Phone2", "Email", "Gender", "Salutation"
    };

    private static readonly HashSet<string> EmploymentFields = new(StringComparer.Ordinal)
    {
        "EmploymentModelCode", "HourlyRate", "MonthlySalary", "MonthlySalaryFte",
        "EmploymentPercentage", "GuaranteedHoursPerWeek", "JobTitle", "JobGroupId",
        "ContractStartDate", "ContractEndDate", "ProbationEndDate", "ProbationMonths",
        "IsActive", "CompanyProfileId", "EducationLevelCode", "WeeklyHours",
        "TeilzeitUnter8hWoche"
    };

    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.Ordinal)
    {
        ["FirstName"] = "Vorname", ["LastName"] = "Nachname", ["AhvNumber"] = "AHV-Nr.",
        ["Street"] = "Strasse", ["HouseNumber"] = "Hausnr.", ["Zip"] = "PLZ", ["City"] = "Ort",
        ["CantonCode"] = "Wohnkanton", ["Country"] = "Land", ["Nationality"] = "Nationalität",
        ["NationalityId"] = "Nationalität", ["MaritalStatus"] = "Zivilstand",
        ["MaritalStatusSince"] = "Zivilstand seit", ["MaidenName"] = "Ledigname",
        ["SeparatedSince"] = "Getrennt seit", ["Religion"] = "Konfession",
        ["EntryDate"] = "Eintritt", ["ExitDate"] = "Austritt",
        ["KuendigungAusgesprochenAm"] = "Kündigung am", ["KuendigungPer"] = "Kündigung per",
        ["KuendigungDurch"] = "Kündigung durch",
        ["Austrittsgrund"] = "Austrittsgrund",
        ["ZemisNumber"] = "ZEMIS-Nr.", ["PlaceOfOrigin"] = "Heimatort",
        ["IsActive"] = "Aktiv", ["IsPayrollExcluded"] = "MA ohne Lohn",
        ["QstBefreitDurchBehoerde"] = "QST Behörden-Befreiung",
        ["QstBefreiungGueltigAb"] = "Befreiung ab", ["QstBefreiungGueltigBis"] = "Befreiung bis",
        ["LgavPflichtig"] = "L-GAV pflichtig", ["TeilzeitUnter8hWoche"] = "Teilzeit &lt;8h/Wo",
        ["IdPassDokumentId"] = "Pass/ID-Dokument", ["CAusweisDokumentId"] = "C-Ausweis-Dokument",
        ["DokumentId"] = "Dokument",
        ["BirthDate"] = "Geburtsdatum", ["PhoneMobile"] = "Mobile",
        ["Phone2"] = "Telefon 2", ["Email"] = "E-Mail",
        ["Gender"] = "Geschlecht", ["Salutation"] = "Anrede",
        ["EmploymentModelCode"] = "Vertragsmodell", ["HourlyRate"] = "Stundenlohn",
        ["MonthlySalary"] = "Monatslohn", ["MonthlySalaryFte"] = "Monatslohn 100%",
        ["EmploymentPercentage"] = "Pensum %", ["GuaranteedHoursPerWeek"] = "Garantierte Std/Wo",
        ["JobTitle"] = "Funktion", ["JobGroupId"] = "Funktionsgruppe",
        ["ContractStartDate"] = "Vertragsbeginn", ["ContractEndDate"] = "Vertragsende",
        ["ProbationEndDate"] = "Probezeitende", ["ProbationMonths"] = "Probezeit",
        ["CompanyProfileId"] = "Filiale", ["EducationLevelCode"] = "Ausbildung",
        ["WeeklyHours"] = "Wochenstunden",
        ["Iban"] = "IBAN", ["Bic"] = "BIC", ["IsPrimary"] = "Hauptbank",
        ["ValidFrom"] = "Gültig ab", ["ValidTo"] = "Gültig bis",
        ["AufteilungTyp"] = "Aufteilung Typ", ["AufteilungWert"] = "Aufteilung Wert",
        ["QstCode"] = "QST-Tarif", ["Steuerkanton"] = "Steuerkanton",
        ["SteuerkantonName"] = "Steuerkanton", ["QstGemeinde"] = "Gemeinde",
        ["TarifCode"] = "Tarif", ["AnzahlKinder"] = "Kinder",
        ["Kirchensteuer"] = "Kirchensteuer", ["Kategorie"] = "Kategorie",
        ["PermitTypeId"] = "Bewilligungstyp", ["PermitExpiryDate"] = "Bewilligung gültig bis",
        ["Amount"] = "Betrag",
        ["Code"] = "Code", ["Betrag"] = "Betrag", ["Periode"] = "Periode",
        ["MonthlyAmount"] = "Monatsbetrag", ["AllowanceType"] = "Zulagenart",
        ["MemberType"] = "Familienmitglied", ["FamilyStatus"] = "Familienstand",
        ["LivesInSwitzerland"] = "Wohnt in CH",
        ["QstDeductibleFrom"] = "QST abziehbar ab", ["QstDeductibleUntil"] = "QST abziehbar bis",
        ["LohnpositionId"] = "Lohnposition",
        ["AddressType"] = "Adresstyp", ["ZipCode"] = "PLZ", ["Street2"] = "Adresszeile 2",
        ["PoBox"] = "Postfach", ["Canton"] = "Kanton", ["Description"] = "Beschreibung",
        ["IncamailDisabled"] = "Incamail aus"
    };

    private static readonly Dictionary<string, string> EntityTitles = new(StringComparer.Ordinal)
    {
        ["Employee"] = "Stammdaten",
        ["Employment"] = "Vertrag",
        ["EmployeeBankAccount"] = "Bank",
        ["EmployeeQuellensteuer"] = "Quellensteuer",
        ["EmployeePermitHistory"] = "Bewilligung",
        ["EmployeeRecurringWage"] = "Wiederkehrende Zulage/Abzug",
        ["EmployeeLohnAssignment"] = "Lohnabtretung",
        ["EmployeeFamilyMember"] = "Familie",
        ["FamilyMemberAllowance"] = "Familienzulage",
        ["EmployeeBvgZusatzMember"] = "BVG-Zusatz",
        ["LohnZulage"] = "Perioden-Zulage/Abzug",
        ["EmployeeAddress"] = "Weitere Adresse"
    };

    public MirusChangeDigestService(AppDbContext db, EmailService email, ILogger<MirusChangeDigestService> log)
    {
        _db = db;
        _email = email;
        _log = log;
    }

    public record DigestRunResult(int RecipientCount, int MailsSent, int ChangeCount, string Message);

    public record DigestPreviewResult(
        string Subject, string Html, string Text, int ChangeCount,
        DateTime SinceUtc, DateTime UntilUtc, string Message);

    public async Task<DigestRunResult> RunAsync(CancellationToken ct = default, DateTime? sinceUtc = null, DateTime? untilUtc = null)
    {
        // audit_log.created_at = timestamp without time zone, Schweizer Wanduhr.
        // Fenster = seit letztem Werktag-06:00 (Mo deckt Fr–Mo ab). Nie Kind=Utc.
        var (since, until) = ResolveWindow(sinceUtc, untilUtc);

        var recipients = await _db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive
                     && u.ReceivesMirusChangeDigest
                     && u.Email != null && u.Email != ""
                     && u.Role != "employee")
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Role, u.Username })
            .ToListAsync(ct);

        if (recipients.Count == 0)
            return new DigestRunResult(0, 0, 0, "Keine Empfänger mit Flag «Mirus-Änderungsmail».");

        var (changes, branchMeta, localFrom, localTo) = await LoadDigestAsync(since, until, ct);
        if (changes.Count == 0)
        {
            // Keine Mail bei leerem Digest — Sachbearbeiter nicht mit Leermails spammen.
            _log.LogInformation("Mirus-Digest: {N} Empfänger, 0 Änderungen ({From:u}–{To:u}).",
                recipients.Count, since, until);
            return new DigestRunResult(recipients.Count, 0, 0, "Keine lohnkritischen Änderungen — keine Mails gesendet.");
        }

        var ubaByUser = await _db.UserBranchAccesses.AsNoTracking()
            .Where(a => recipients.Select(r => r.Id).Contains(a.UserId))
            .GroupBy(a => a.UserId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.CompanyProfileId).ToHashSet(), ct);

        var subjectDate = localTo.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-CH"));
        var sent = 0;

        foreach (var r in recipients)
        {
            var unrestricted = r.Role is "admin" or "superuser";
            HashSet<int>? allowed = null;
            if (!unrestricted)
            {
                if (!ubaByUser.TryGetValue(r.Id, out allowed) || allowed.Count == 0)
                {
                    _log.LogWarning("Mirus-Digest: User {Id} ({Email}) hat Flag, aber keine Filial-Zuordnung — übersprungen.", r.Id, r.Email);
                    continue;
                }
            }

            var forUser = changes
                .Where(c => unrestricted
                    || (c.CompanyProfileId.HasValue && allowed!.Contains(c.CompanyProfileId.Value)))
                .ToList();
            if (forUser.Count == 0) continue;

            var name = $"{r.FirstName} {r.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(name)) name = r.Username;
            var (html, text) = RenderMail(forUser, branchMeta, localFrom, localTo, name);

            var subject = $"OneCrew → Mirus: Änderungen {subjectDate} ({forUser.Count})";
            // OneCrew-Benutzer (eigenes Team) — Kategorie INTERN.
            var ok = await _email.SendAsync(r.Email!, name, subject, html, text,
                VersandKategorie.Intern);
            if (ok) sent++;
        }

        _log.LogInformation("Mirus-Digest: {Changes} Änderungen, {Recipients} Empfänger, {Sent} Mails gesendet.",
            changes.Count, recipients.Count, sent);
        return new DigestRunResult(recipients.Count, sent, changes.Count,
            $"{sent} Mail(s) an {recipients.Count} Empfänger, {changes.Count} Änderungszeilen.");
    }

    /// <summary>
    /// Vorschau der Mail-HTML (keine Zustellung). Optional auf eine Filiale filtern
    /// (companyProfileId oder restaurantCode, z.B. «129» für Reinach).
    /// </summary>
    public async Task<DigestPreviewResult> PreviewAsync(
        CancellationToken ct = default,
        int? companyProfileId = null,
        string? restaurantCode = null,
        string recipientName = "Vorschau")
    {
        var (since, until) = ResolveWindow(null, null);
        var (changes, branchMeta, localFrom, localTo) = await LoadDigestAsync(since, until, ct);
        var builtCount = changes.Count;

        int? filterCpId = companyProfileId;
        string? filterLabel = null;
        if (filterCpId == null && !string.IsNullOrWhiteSpace(restaurantCode))
        {
            var code = restaurantCode.Trim();
            var br = await _db.CompanyProfiles.AsNoTracking()
                .Where(c => c.RestaurantCode == code)
                .Select(c => new { c.Id, c.RestaurantCode, Name = c.BranchName ?? c.CompanyName })
                .FirstOrDefaultAsync(ct);
            if (br == null)
            {
                return new DigestPreviewResult(
                    "OneCrew → Mirus: Vorschau",
                    $"<p>Keine Filiale mit Restaurant-Code «{Esc(code)}» gefunden.</p>",
                    $"Keine Filiale mit Restaurant-Code «{code}» gefunden.",
                    0, since, until, "Filiale nicht gefunden.");
            }
            filterCpId = br.Id;
            filterLabel = $"{br.RestaurantCode} – {br.Name}";
        }
        else if (filterCpId != null)
        {
            var br = await _db.CompanyProfiles.AsNoTracking()
                .Where(c => c.Id == filterCpId.Value)
                .Select(c => new { c.RestaurantCode, Name = c.BranchName ?? c.CompanyName })
                .FirstOrDefaultAsync(ct);
            if (br != null) filterLabel = $"{br.RestaurantCode} – {br.Name}";
        }

        if (filterCpId != null)
        {
            // MA zählt zur Filiale, wenn irgendein Vertrag dort liegt (nicht nur «Hauptfiliale»).
            var empAtBranch = (await _db.Employments.AsNoTracking()
                    .Where(e => e.CompanyProfileId == filterCpId)
                    .Select(e => e.EmployeeId)
                    .Distinct()
                    .ToListAsync(ct))
                .ToHashSet();
            changes = changes.Where(c =>
                    c.CompanyProfileId == filterCpId
                    || (c.EmployeeId.HasValue && empAtBranch.Contains(c.EmployeeId.Value)))
                .ToList();
        }

        var subjectDate = localTo.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-CH"));
        if (changes.Count == 0)
        {
            var watched = WatchedEntityList;
            var rawCount = await _db.AuditLogs.AsNoTracking()
                .CountAsync(a => a.CreatedAt >= since && a.CreatedAt < until
                              && watched.Contains(a.EntityType), ct);
            var qstCount = await _db.AuditLogs.AsNoTracking()
                .CountAsync(a => a.CreatedAt >= since && a.CreatedAt < until
                              && a.EntityType == "EmployeeQuellensteuer", ct);
            var de = CultureInfo.GetCultureInfo("de-CH");
            var emptyHtml =
                "<div style=\"font-family:-apple-system,Segoe UI,Roboto,sans-serif;font-size:14px;color:#1e293b;line-height:1.45\">"
                + $"<p>Guten Morgen {Esc(recipientName)},</p>"
                + "<p>In den letzten 24 Stunden gibt es <b>keine</b> lohnkritischen OneCrew-Änderungen"
                + (filterLabel != null ? $" für <b>{Esc(filterLabel)}</b>" : "")
                + " — deshalb würde <b>keine Mail</b> gesendet.</p>"
                + $"<p style=\"color:#64748b\">Zeitraum: {Esc(localFrom.ToString("dd.MM.yyyy HH:mm", de))} – {Esc(localTo.ToString("dd.MM.yyyy HH:mm", de))} (Europe/Zurich)</p>"
                + $"<p style=\"color:#94a3b8;font-size:12px\">Diagnose: Audit lohnkritisch={rawCount}, davon QST={qstCount}, nach Aufbereitung={builtCount}, nach Filial-Filter={changes.Count}."
                + (qstCount > 0 && changes.Count == 0
                    ? " → QST ist im Audit, fällt aber beim Filial-Filter raus (Sidebar-Filiale prüfen / MA-Vertrag)."
                    : rawCount == 0
                        ? " → Kein Audit im Fenster — Eintrag nochmals speichern."
                        : "")
                + "</p></div>";
            return new DigestPreviewResult(
                $"OneCrew → Mirus: Änderungen {subjectDate} (0)",
                emptyHtml, "Keine lohnkritischen Änderungen.", 0, since, until,
                $"Keine lohnkritischen Änderungen (Audit={rawCount}, QST={qstCount}, gebaut={builtCount}).");
        }

        var (html, text) = RenderMail(changes, branchMeta, localFrom, localTo, recipientName);
        var subject = $"OneCrew → Mirus: Änderungen {subjectDate} ({changes.Count})";
        return new DigestPreviewResult(subject, html, text, changes.Count, since, until,
            $"{changes.Count} Änderungszeilen (Vorschau, nicht gesendet).");
    }

    /// <summary>
    /// Vorschau-Mail wirklich senden (Walter 26.07.2026) — gleiche HTML wie
    /// Preview/06:00-Lauf, an eine Adresse (typisch der eingeloggte Admin).
    /// Optional Filial-Filter wie bei Preview.
    /// </summary>
    public async Task<(bool Ok, string Message, string? Subject)> SendPreviewAsync(
        string toEmail, string recipientName,
        int? companyProfileId = null, string? restaurantCode = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            return (false, "Keine E-Mail-Adresse.", null);

        var preview = await PreviewAsync(ct, companyProfileId, restaurantCode, recipientName);
        var subject = "[Vorschau] " + preview.Subject;
        // Vorschau an den eingeloggten Admin — interner Benutzer.
        var ok = await _email.SendAsync(toEmail.Trim(), recipientName, subject, preview.Html, preview.Text,
            VersandKategorie.Intern);
        return ok
            ? (true, $"Vorschau an {toEmail.Trim()} gesendet ({preview.ChangeCount} Änderungszeilen).", subject)
            : (false, $"Versand an {toEmail.Trim()} fehlgeschlagen (SMTP prüfen).", subject);
    }

    private async Task<(List<DigestChange> Changes, Dictionary<int, (string Code, string Name)> BranchMeta, DateTime LocalFrom, DateTime LocalTo)>
        LoadDigestAsync(DateTime since, DateTime until, CancellationToken ct)
    {
        // lokale Variable — EF Core Capturing für IN-Liste
        var watched = WatchedEntityList;
        var raw = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAt >= since && a.CreatedAt < until
                     && watched.Contains(a.EntityType))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var changes = await BuildChangesAsync(raw, ct);
        var allBranchIds = changes.Select(c => c.CompanyProfileId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var branchMeta = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => allBranchIds.Contains(c.Id))
            .Select(c => new { c.Id, c.RestaurantCode, Name = c.BranchName ?? c.CompanyName })
            .ToDictionaryAsync(c => c.Id, ct);
        var meta = branchMeta.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.RestaurantCode ?? "", kv.Value.Name ?? ("Filiale " + kv.Key)));
        // since/until sind bereits Schweizer Wanduhr (Unspecified)
        return (changes, meta, since, until);
    }

    private sealed record EmpName(string Number, string FirstName, string LastName);

    private sealed record DigestChange(
        DateTime CreatedAtUtc,
        int? CompanyProfileId,
        int? EmployeeId,
        string EmployeeNumber,
        string EmployeeName,
        string EntityType,
        string Action,
        string Summary,
        string? Actor);

    private async Task<List<DigestChange>> BuildChangesAsync(List<AuditLog> raw, CancellationToken ct)
    {
        // IDs vorsammeln für Batch-Lookups (EntityId ≤ 0 = CREATE vor Identity-Refresh → überspringen)
        var empIds = new HashSet<int>();
        var emplIds = new HashSet<int>();
        var bankIds = new HashSet<int>();
        var qstIds = new HashSet<int>();
        var permitIds = new HashSet<int>();
        var recIds = new HashSet<int>();
        var assignIds = new HashSet<int>();
        var famIds = new HashSet<int>();
        var allowIds = new HashSet<int>();
        var bvgIds = new HashSet<int>();
        var zulIds = new HashSet<int>();
        var addrIds = new HashSet<int>();
        // EmployeeIds aus JSON/Route — nötig wenn EntityId=0 und Child-Maps leer sind
        var empIdsFromAudit = new HashSet<int>();

        foreach (var a in raw)
        {
            var fromAudit = TryResolveEmployeeIdFromAudit(a);
            if (fromAudit is > 0) empIdsFromAudit.Add(fromAudit.Value);

            if (!int.TryParse(a.EntityId, out var id) || id <= 0) continue;
            switch (a.EntityType)
            {
                case "Employee": empIds.Add(id); break;
                case "Employment": emplIds.Add(id); break;
                case "EmployeeBankAccount": bankIds.Add(id); break;
                case "EmployeeQuellensteuer": qstIds.Add(id); break;
                case "EmployeePermitHistory": permitIds.Add(id); break;
                case "EmployeeRecurringWage": recIds.Add(id); break;
                case "EmployeeLohnAssignment": assignIds.Add(id); break;
                case "EmployeeFamilyMember": famIds.Add(id); break;
                case "FamilyMemberAllowance": allowIds.Add(id); break;
                case "EmployeeBvgZusatzMember": bvgIds.Add(id); break;
                case "LohnZulage": zulIds.Add(id); break;
                case "EmployeeAddress": addrIds.Add(id); break;
            }
        }

        var employments = emplIds.Count == 0
            ? new Dictionary<int, (int EmployeeId, int? CompanyProfileId)>()
            : (await _db.Employments.AsNoTracking()
                .Where(e => emplIds.Contains(e.Id))
                .Select(e => new { e.Id, e.EmployeeId, e.CompanyProfileId })
                .ToListAsync(ct))
              .ToDictionary(e => e.Id, e => (e.EmployeeId, e.CompanyProfileId));

        // Child-Entities → EmployeeId (leere ID-Mengen überspringen)
        var bankMap = bankIds.Count == 0 ? new Dictionary<int, int>()
            : (await _db.EmployeeBankAccounts.AsNoTracking()
                .Where(b => bankIds.Contains(b.Id)).Select(b => new { b.Id, b.EmployeeId }).ToListAsync(ct))
              .ToDictionary(b => b.Id, b => b.EmployeeId);
        var qstMap = qstIds.Count == 0 ? new Dictionary<int, int>()
            : (await _db.EmployeeQuellensteuer.AsNoTracking()
                .Where(b => qstIds.Contains(b.Id)).Select(b => new { b.Id, b.EmployeeId }).ToListAsync(ct))
              .ToDictionary(b => b.Id, b => b.EmployeeId);
        var permitMap = permitIds.Count == 0 ? new Dictionary<int, int>()
            : (await _db.EmployeePermitHistories.AsNoTracking()
                .Where(b => permitIds.Contains(b.Id)).Select(b => new { b.Id, b.EmployeeId }).ToListAsync(ct))
              .ToDictionary(b => b.Id, b => b.EmployeeId);
        var recMap = recIds.Count == 0 ? new Dictionary<int, int>()
            : (await _db.EmployeeRecurringWages.AsNoTracking()
                .Where(b => recIds.Contains(b.Id)).Select(b => new { b.Id, b.EmployeeId }).ToListAsync(ct))
              .ToDictionary(b => b.Id, b => b.EmployeeId);
        var assignMap = assignIds.Count == 0 ? new Dictionary<int, int>()
            : (await _db.EmployeeLohnAssignments.AsNoTracking()
                .Where(b => assignIds.Contains(b.Id)).Select(b => new { b.Id, b.EmployeeId }).ToListAsync(ct))
              .ToDictionary(b => b.Id, b => b.EmployeeId);
        var famMap = famIds.Count == 0 ? new Dictionary<int, int>()
            : (await _db.EmployeeFamilyMembers.AsNoTracking()
                .Where(b => famIds.Contains(b.Id)).Select(b => new { b.Id, b.EmployeeId }).ToListAsync(ct))
              .ToDictionary(b => b.Id, b => b.EmployeeId);
        var allowMap = allowIds.Count == 0 ? new Dictionary<int, int>()
            : (await (
                from a in _db.FamilyMemberAllowances.AsNoTracking()
                join f in _db.EmployeeFamilyMembers.AsNoTracking() on a.FamilyMemberId equals f.Id
                where allowIds.Contains(a.Id)
                select new { a.Id, EmpId = f.EmployeeId }
            ).ToListAsync(ct)).ToDictionary(b => b.Id, b => b.EmpId);
        var bvgMap = bvgIds.Count == 0 ? new Dictionary<int, int>()
            : (await _db.EmployeeBvgZusatzMembers.AsNoTracking()
                .Where(b => bvgIds.Contains(b.Id)).Select(b => new { b.Id, b.EmployeeId }).ToListAsync(ct))
              .ToDictionary(b => b.Id, b => b.EmployeeId);
        var zulMap = zulIds.Count == 0 ? new Dictionary<int, int>()
            : (await _db.LohnZulagen.AsNoTracking()
                .Where(b => zulIds.Contains(b.Id)).Select(b => new { b.Id, b.EmployeeId }).ToListAsync(ct))
              .ToDictionary(b => b.Id, b => b.EmployeeId);
        var addrMap = addrIds.Count == 0 ? new Dictionary<int, int>()
            : (await _db.EmployeeAddresses.AsNoTracking()
                .Where(b => addrIds.Contains(b.Id)).Select(b => new { b.Id, b.EmployeeId }).ToListAsync(ct))
              .ToDictionary(b => b.Id, b => b.EmployeeId);

        // Alle betroffenen MA — inkl. JSON/Route (CREATE mit EntityId=0)
        var allEmpIds = new HashSet<int>(empIds);
        foreach (var id in empIdsFromAudit) allEmpIds.Add(id);
        foreach (var e in employments.Values) allEmpIds.Add(e.EmployeeId);
        foreach (var id in bankMap.Values) allEmpIds.Add(id);
        foreach (var id in qstMap.Values) allEmpIds.Add(id);
        foreach (var id in permitMap.Values) allEmpIds.Add(id);
        foreach (var id in recMap.Values) allEmpIds.Add(id);
        foreach (var id in assignMap.Values) allEmpIds.Add(id);
        foreach (var id in famMap.Values) allEmpIds.Add(id);
        foreach (var id in allowMap.Values) allEmpIds.Add(id);
        foreach (var id in bvgMap.Values) allEmpIds.Add(id);
        foreach (var id in zulMap.Values) allEmpIds.Add(id);
        foreach (var id in addrMap.Values) allEmpIds.Add(id);
        allEmpIds.Remove(0);

        // Personalnummer + Name + Filiale erst NACH vollständiger ID-Auflösung laden
        var empDict = allEmpIds.Count == 0
            ? new Dictionary<int, EmpName>()
            : (await _db.Employees.AsNoTracking()
                .Where(e => allEmpIds.Contains(e.Id))
                .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
                .ToListAsync(ct))
              .ToDictionary(e => e.Id, e => new EmpName(e.EmployeeNumber ?? "", e.FirstName ?? "", e.LastName ?? ""));

        // Filiale pro MA — in Memory (kein EF-GroupBy+OrderBy), aktiv bevorzugt
        var empBranch = new Dictionary<int, int?>();
        if (allEmpIds.Count > 0)
        {
            var branchRows = await _db.Employments.AsNoTracking()
                .Where(e => allEmpIds.Contains(e.EmployeeId) && e.CompanyProfileId != null)
                .Select(e => new { e.EmployeeId, e.CompanyProfileId, e.IsActive, e.ContractStartDate })
                .ToListAsync(ct);
            foreach (var g in branchRows.GroupBy(x => x.EmployeeId))
            {
                var pick = g.OrderByDescending(x => x.IsActive)
                    .ThenByDescending(x => x.ContractStartDate)
                    .First();
                empBranch[g.Key] = pick.CompanyProfileId;
            }
        }

        // Dokument-Namen für lesbare Texte (statt nackter IDs)
        var docIds = CollectDokumentIds(raw);
        var docNames = docIds.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.EmployeeDokumente.AsNoTracking()
                .Where(d => docIds.Contains(d.Id))
                .Select(d => new { d.Id, Name = d.FilenameOriginal })
                .ToListAsync(ct))
              .ToDictionary(d => d.Id, d => string.IsNullOrWhiteSpace(d.Name) ? ("Dokument #" + d.Id) : d.Name);

        var result = new List<DigestChange>();
        foreach (var a in raw)
        {
            // EntityId «0»/null = CREATE vor Identity-Fix — Maps überspringen, JSON/Route nutzen
            var hasEntityId = int.TryParse(a.EntityId, out var entityId) && entityId > 0;
            if (!hasEntityId) entityId = 0;

            var flatEarly = TryReadFlat(a.ChangesJson);
            var summary = BuildSummary(a, flatEarly, docNames);
            if (summary == null) continue;

            int? employeeId = TryResolveEmployeeIdFromAudit(a);
            int? cpId = null;

            if (hasEntityId)
            {
                switch (a.EntityType)
                {
                    case "Employee":
                        employeeId ??= entityId;
                        empBranch.TryGetValue(entityId, out cpId);
                        break;
                    case "Employment":
                        if (employments.TryGetValue(entityId, out var em))
                        {
                            employeeId ??= em.EmployeeId;
                            cpId = em.CompanyProfileId;
                        }
                        break;
                    case "EmployeeBankAccount":
                        if (bankMap.TryGetValue(entityId, out var be)) employeeId ??= be;
                        break;
                    case "EmployeeQuellensteuer":
                        if (qstMap.TryGetValue(entityId, out var qe)) employeeId ??= qe;
                        break;
                    case "EmployeePermitHistory":
                        if (permitMap.TryGetValue(entityId, out var pe)) employeeId ??= pe;
                        break;
                    case "EmployeeRecurringWage":
                        if (recMap.TryGetValue(entityId, out var re)) employeeId ??= re;
                        break;
                    case "EmployeeLohnAssignment":
                        if (assignMap.TryGetValue(entityId, out var ae)) employeeId ??= ae;
                        break;
                    case "EmployeeFamilyMember":
                        if (famMap.TryGetValue(entityId, out var fe)) employeeId ??= fe;
                        break;
                    case "FamilyMemberAllowance":
                        if (allowMap.TryGetValue(entityId, out var ale)) employeeId ??= ale;
                        break;
                    case "EmployeeBvgZusatzMember":
                        if (bvgMap.TryGetValue(entityId, out var bve)) employeeId ??= bve;
                        break;
                    case "LohnZulage":
                        if (zulMap.TryGetValue(entityId, out var ze)) employeeId ??= ze;
                        break;
                    case "EmployeeAddress":
                        if (addrMap.TryGetValue(entityId, out var ade)) employeeId ??= ade;
                        break;
                }
            }

            if (flatEarly != null && flatEarly.TryGetValue("CompanyProfileId", out var cid)
                && int.TryParse(cid, out var cidI) && cidI > 0)
                cpId ??= cidI;

            if (employeeId == null || employeeId <= 0) continue;
            if (cpId == null) empBranch.TryGetValue(employeeId.Value, out cpId);

            string empName, empNr;
            if (empDict.TryGetValue(employeeId.Value, out var emp))
            {
                empName = $"{emp.FirstName} {emp.LastName}".Trim();
                empNr = emp.Number;
            }
            else
            {
                // Fallback nur wenn MA gelöscht — nie interne DB-Id als «MA #…»
                empName = "unbekannter MA";
                empNr = "";
            }

            result.Add(new DigestChange(
                a.CreatedAt, cpId, employeeId, empNr, empName,
                a.EntityType, a.Action, summary, a.UserName));
        }
        return result;
    }

    /// <summary>
    /// EmployeeId aus ChangesJson oder Route — unabhängig von EntityId
    /// (CREATE hatte früher EntityId=0 bevor die Identity geschrieben war).
    /// </summary>
    private static int? TryResolveEmployeeIdFromAudit(AuditLog a)
    {
        var flat = TryReadFlat(a.ChangesJson);
        if (flat != null && flat.TryGetValue("EmployeeId", out var eid)
            && int.TryParse(eid, out var eidI) && eidI > 0)
            return eidI;
        // UPDATE-Form {old,new}: TryReadFlat speichert oft «old → new» — zusätzlich roh parsen
        try
        {
            using var doc = JsonDocument.Parse(a.ChangesJson ?? "{}");
            if (doc.RootElement.TryGetProperty("EmployeeId", out var prop))
            {
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var n) && n > 0)
                    return n;
                if (prop.ValueKind == JsonValueKind.Object)
                {
                    if (prop.TryGetProperty("new", out var neu) && neu.ValueKind == JsonValueKind.Number
                        && neu.TryGetInt32(out var ni) && ni > 0) return ni;
                    if (prop.TryGetProperty("old", out var alt) && alt.ValueKind == JsonValueKind.Number
                        && alt.TryGetInt32(out var oi) && oi > 0) return oi;
                }
            }
        }
        catch { /* ignore */ }

        if (a.EntityType == "Employee"
            && int.TryParse(a.EntityId, out var selfId) && selfId > 0)
            return selfId;

        return TryParseEmployeeIdFromRoute(a.Route);
    }

    private static HashSet<int> CollectDokumentIds(List<AuditLog> raw)
    {
        var ids = new HashSet<int>();
        foreach (var a in raw)
        {
            var flat = TryReadFlat(a.ChangesJson);
            if (flat == null) continue;
            foreach (var key in new[] { "DokumentId", "IdPassDokumentId", "CAusweisDokumentId" })
            {
                if (flat.TryGetValue(key, out var v) && int.TryParse(v, out var id) && id > 0)
                    ids.Add(id);
                // UPDATE: TryReadFlat flacht {old,new} nicht — extra parsen
            }
            try
            {
                using var doc = JsonDocument.Parse(a.ChangesJson ?? "{}");
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.Name is not ("DokumentId" or "IdPassDokumentId" or "CAusweisDokumentId"))
                        continue;
                    if (prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        if (prop.Value.TryGetProperty("new", out var n) && n.ValueKind == JsonValueKind.Number
                            && n.TryGetInt32(out var ni) && ni > 0) ids.Add(ni);
                        if (prop.Value.TryGetProperty("old", out var o) && o.ValueKind == JsonValueKind.Number
                            && o.TryGetInt32(out var oi) && oi > 0) ids.Add(oi);
                    }
                    else if (prop.Value.ValueKind == JsonValueKind.Number
                             && prop.Value.TryGetInt32(out var id) && id > 0)
                        ids.Add(id);
                }
            }
            catch { /* ignore */ }
        }
        return ids;
    }

    private static string? BuildSummary(AuditLog a, Dictionary<string, string>? flat, Dictionary<int, string> docNames)
    {
        var title = EntityTitles.TryGetValue(a.EntityType, out var t) ? t : a.EntityType;
        if (a.Action == "CREATE")
        {
            if (a.EntityType == "EmployeeQuellensteuer")
            {
                var bits = new List<string>();
                if (flat != null)
                {
                    if (flat.TryGetValue("QstCode", out var code) && !string.IsNullOrWhiteSpace(code))
                        bits.Add($"Tarif {code}");
                    if (flat.TryGetValue("Steuerkanton", out var kt) && !string.IsNullOrWhiteSpace(kt))
                        bits.Add($"Kanton {kt}");
                    if (flat.TryGetValue("ValidFrom", out var vf) && !string.IsNullOrWhiteSpace(vf))
                        bits.Add($"gültig ab {Fmt(vf)}");
                }
                return bits.Count > 0
                    ? $"{title}: neu erfasst ({string.Join(", ", bits)})"
                    : $"{title}: neu erfasst";
            }
            if (a.EntityType == "EmployeePermitHistory")
            {
                if (flat != null && flat.TryGetValue("DokumentId", out var did)
                    && int.TryParse(did, out var docId) && docId > 0)
                {
                    var name = docNames.TryGetValue(docId, out var dn) ? dn : null;
                    return string.IsNullOrEmpty(name)
                        ? $"{title}: Dokument hinterlegt"
                        : $"{title}: Dokument hinterlegt («{name}»)";
                }
                return $"{title}: neu erfasst";
            }
            if (a.EntityType == "EmployeeAddress")
            {
                if (flat != null && flat.TryGetValue("AddressType", out var at)
                    && !string.IsNullOrWhiteSpace(at))
                    return $"{title}: neu erfasst ({at})";
                return $"{title}: neu erfasst";
            }
            if (a.EntityType == "EmployeeFamilyMember")
            {
                var bits = new List<string>();
                if (flat != null)
                {
                    if (flat.TryGetValue("MemberType", out var mt) && !string.IsNullOrWhiteSpace(mt))
                        bits.Add(mt);
                    if (flat.TryGetValue("PermitTypeId", out var pt) && !string.IsNullOrWhiteSpace(pt) && pt != "0")
                        bits.Add("mit Bewilligung");
                }
                return bits.Count > 0
                    ? $"{title}: neu erfasst ({string.Join(", ", bits)})"
                    : $"{title}: neu erfasst";
            }
            return $"{title}: neu erfasst";
        }
        if (a.Action == "DELETE")
            return $"{title}: gelöscht";

        // UPDATE — nur Whitelist-Felder
        HashSet<string>? whitelist = a.EntityType switch
        {
            "Employee" => EmployeeFields,
            "Employment" => EmploymentFields,
            _ => null // andere Entity-Typen: alle Felder ausser Noise
        };

        var parts = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(a.ChangesJson ?? "{}");
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                var field = prop.Name;
                if (IsNoiseField(field)) continue;
                if (whitelist != null && !whitelist.Contains(field)) continue;

                string? oldV = null, newV = null;
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    if (prop.Value.TryGetProperty("old", out var o)) oldV = JsonVal(o);
                    if (prop.Value.TryGetProperty("new", out var n)) newV = JsonVal(n);
                }
                else
                {
                    newV = JsonVal(prop.Value);
                }
                if (oldV == newV) continue;

                // Dokument-IDs → Klartext statt «— → 7401»
                if (field is "DokumentId" or "IdPassDokumentId" or "CAusweisDokumentId")
                {
                    var human = DescribeDokumentChange(field, oldV, newV, docNames);
                    if (human != null) parts.Add(human);
                    continue;
                }

                var label = FieldLabels.TryGetValue(field, out var fl) ? fl : field;
                if (field == "KuendigungDurch")
                {
                    parts.Add($"{label}: {FmtKuendigungDurch(oldV)} → {FmtKuendigungDurch(newV)}");
                    continue;
                }
                if (field == "Austrittsgrund")
                {
                    parts.Add($"{label}: {AustrittsgrundCodes.LabelOf(oldV)} → {AustrittsgrundCodes.LabelOf(newV)}");
                    continue;
                }
                parts.Add($"{label}: {Fmt(oldV)} → {Fmt(newV)}");
            }
        }
        catch { return null; }

        if (parts.Count == 0) return null;
        // Cap: max. 6 Felder pro Zeile
        if (parts.Count > 6)
            parts = parts.Take(6).Append($"… +{parts.Count - 6} weitere").ToList();
        return $"{title}: " + string.Join("; ", parts);
    }

    private static string? DescribeDokumentChange(string field, string? oldV, string? newV, Dictionary<int, string> docNames)
    {
        var label = field switch
        {
            "IdPassDokumentId" => "Pass/ID-Dokument",
            "CAusweisDokumentId" => "C-Ausweis-Dokument",
            _ => "Dokument"
        };
        var oldEmpty = string.IsNullOrWhiteSpace(oldV) || oldV == "0";
        var newEmpty = string.IsNullOrWhiteSpace(newV) || newV == "0";
        if (oldEmpty && newEmpty) return null;
        if (oldEmpty && !newEmpty)
        {
            var name = int.TryParse(newV, out var id) && docNames.TryGetValue(id, out var dn) ? dn : null;
            return string.IsNullOrEmpty(name)
                ? $"{label} hinterlegt"
                : $"{label} hinterlegt («{name}»)";
        }
        if (!oldEmpty && newEmpty)
            return $"{label} entfernt";
        {
            var name = int.TryParse(newV, out var id) && docNames.TryGetValue(id, out var dn) ? dn : null;
            return string.IsNullOrEmpty(name)
                ? $"{label} ersetzt"
                : $"{label} ersetzt («{name}»)";
        }
    }

    private static bool IsNoiseField(string field) =>
        field.EndsWith("At", StringComparison.Ordinal)
        || field.EndsWith("UpdatedAt", StringComparison.Ordinal)
        || field.StartsWith("EasyAtWork", StringComparison.Ordinal)
        || field is "xmin" or "Xmin" or "RowVersion" or "PasswordHash"
            or "MustChangePassword" or "FailedLoginCount" or "LockedUntil"
            or "ZugriffAm" or "ZugriffVon" or "GeaendertAm" or "GeaendertVon"
            or "DateiGeaendertAm" or "ErstelltAm" or "HochgeladenAm";

    private static string JsonVal(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null => "",
        JsonValueKind.True => "ja",
        JsonValueKind.False => "nein",
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Number => el.ToString(),
        _ => el.ToString()
    };

    private static string Fmt(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return "—";
        // ISO-Datum → dd.MM.yyyy
        if (DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
            && (v.Contains('T') || v.Length == 10))
            return dt.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("de-CH"));
        if (v.Length > 80) return v[..77] + "…";
        return v;
    }

    private static string FmtKuendigungDurch(string? v) =>
        (v ?? "").Trim().ToUpperInvariant() switch
        {
            "AG" => "durch uns",
            "AN" => "durch Mitarbeiter",
            _ => "—"
        };

    private static Dictionary<string, string>? TryReadFlat(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in doc.RootElement.EnumerateObject())
                d[p.Name] = JsonVal(p.Value);
            return d;
        }
        catch { return null; }
    }

    private static (string Html, string Text) RenderMail(
        List<DigestChange> changes,
        Dictionary<int, (string Code, string Name)> branches,
        DateTime localFrom, DateTime localTo, string recipientName)
    {
        var de = CultureInfo.GetCultureInfo("de-CH");
        var window = $"{localFrom.ToString("dd.MM.yyyy HH:mm", de)} – {localTo.ToString("dd.MM.yyyy HH:mm", de)}";

        var byBranch = changes
            .GroupBy(c => c.CompanyProfileId ?? 0)
            .OrderBy(g => {
                if (g.Key == 0) return "9999";
                return branches.TryGetValue(g.Key, out var b) ? (b.Code ?? "9999") : "9999";
            });

        var html = new StringBuilder();
        html.Append("<div style=\"font-family:-apple-system,Segoe UI,Roboto,sans-serif;font-size:14px;color:#1e293b;line-height:1.45\">");
        html.Append($"<p>Guten Morgen {Esc(recipientName)},</p>");
        html.Append("<p>lohnkritische Änderungen in <b>OneCrew</b> der letzten 24 Stunden ");
        html.Append($"(für die Lohnverarbeitung in Mirus).<br><span style=\"color:#64748b\">Zeitraum: {Esc(window)} (Europe/Zurich)</span></p>");

        var text = new StringBuilder();
        text.AppendLine($"Guten Morgen {recipientName},");
        text.AppendLine();
        text.AppendLine("lohnkritische Änderungen in OneCrew der letzten 24 Stunden (für Mirus).");
        text.AppendLine($"Zeitraum: {window} (Europe/Zurich)");
        text.AppendLine();

        foreach (var br in byBranch)
        {
            string brTitle;
            if (br.Key == 0) brTitle = "Ohne Filial-Zuordnung";
            else if (branches.TryGetValue(br.Key, out var bm))
                brTitle = string.IsNullOrEmpty(bm.Code) ? bm.Name : $"{bm.Code} – {bm.Name}";
            else brTitle = $"Filiale {br.Key}";

            html.Append($"<h2 style=\"font-size:15px;margin:22px 0 8px;color:#0f172a;border-bottom:1px solid #e2e8f0;padding-bottom:4px\">{Esc(brTitle)}</h2>");
            text.AppendLine($"══ {brTitle} ══");

            // Nur Personalnummer — nie Vor-/Nachname in der Mirus-Mail (Datenschutz / Alltag)
            var byEmp = br
                .GroupBy(c => c.EmployeeId)
                .OrderBy(g => g.First().EmployeeNumber ?? "")
                .ThenBy(g => g.Key);

            foreach (var empG in byEmp)
            {
                var sample = empG.First();
                var empHead = string.IsNullOrEmpty(sample.EmployeeNumber)
                    ? "ohne Personalnummer"
                    : sample.EmployeeNumber;
                html.Append($"<div style=\"margin:10px 0 4px;font-weight:700\">{Esc(empHead)}</div><ul style=\"margin:0 0 12px 18px;padding:0\">");
                text.AppendLine($"  · {empHead}");

                foreach (var c in empG.OrderBy(x => x.CreatedAtUtc))
                {
                    var when = FormatDbTime(c.CreatedAtUtc).ToString("HH:mm", de);
                    var actor = string.IsNullOrWhiteSpace(c.Actor) ? "" : $" — {c.Actor}";
                    html.Append($"<li style=\"margin:3px 0\"><span style=\"color:#64748b\">{when}</span> {Esc(c.Summary)}<span style=\"color:#94a3b8\">{Esc(actor)}</span></li>");
                    text.AppendLine($"      {when}  {c.Summary}{actor}");
                }
                html.Append("</ul>");
            }
            text.AppendLine();
        }

        html.Append("<p style=\"color:#64748b;font-size:12.5px;margin-top:24px\">Stempelzeiten und Absenzen sind nicht enthalten — die laufen schon automatisch nach Mirus.<br>Diese Mail wird Mo–Fr um 06:00 an Empfänger mit dem Flag «Mirus-Änderungsmail» gesendet (Montag deckt Freitag–Montag ab).</p>");
        html.Append("</div>");
        text.AppendLine();
        text.AppendLine("Stempelzeiten/Absenzen sind nicht enthalten (laufen automatisch).");
        text.AppendLine("Flag «Mirus-Änderungsmail» in der Benutzerverwaltung steuert den Empfang (Mo–Fr 06:00).");

        return (html.ToString(), text.ToString());
    }

    private static string Esc(string? s) =>
        string.IsNullOrEmpty(s) ? "" :
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private static readonly TimeZoneInfo SwissTz = FindSwissTz();
    private static TimeZoneInfo FindSwissTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }
    }

    /// <summary>
    /// Fenster seit letztem Mo–Fr-06:00-Slot (Walter 30.07.2026):
    /// Di–Fr ≈ 24 h, Mo = Fr 06:00–Mo (Wochenende mit abdecken).
    /// Zusätzlich UTC-Wanduhr abdecken (Alt-Einträge vor dem Zurich-Fix).
    /// Ergebnis immer Kind=Unspecified für Npgsql.
    /// </summary>
    private static (DateTime Since, DateTime Until) ResolveWindow(DateTime? sinceUtc, DateTime? untilUtc)
    {
        if (sinceUtc.HasValue || untilUtc.HasValue)
        {
            var until = AsUnspecified(untilUtc ?? SwissNow());
            var since = AsUnspecified(sinceUtc ?? PreviousWeekday0600(until));
            return (since, until);
        }

        var nowZ = SwissNow();
        var nowUtcFace = DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified);
        var untilW = nowZ >= nowUtcFace ? nowZ : nowUtcFace;
        var sinceZ = PreviousWeekday0600(nowZ);
        // gleiche Länge zurück auf der UTC-Wanduhr (Alt-Audit vor Zurich-Fix)
        var spanHours = Math.Max(24, (nowZ - sinceZ).TotalHours);
        var sinceU = nowUtcFace.AddHours(-spanHours);
        var sinceW = sinceZ <= sinceU ? sinceZ : sinceU;
        return (sinceW, untilW);
    }

    /// <summary>
    /// Letzter Mo–Fr 06:00 strikt vor dem heutigen 06:00-Slot
    /// (bei Lauf Mo 06:00 → Fr 06:00; bei Di 06:00 → Mo 06:00).
    /// </summary>
    public static DateTime PreviousWeekday0600(DateTime nowLocal)
    {
        var todaySlot = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, 6, 0, 0, DateTimeKind.Unspecified);
        var prev = todaySlot.AddDays(-1);
        while (prev.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            prev = prev.AddDays(-1);
        return DateTime.SpecifyKind(prev, DateTimeKind.Unspecified);
    }

    private static DateTime SwissNow()
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SwissTz);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    private static DateTime AsUnspecified(DateTime dt) =>
        DateTime.SpecifyKind(
            dt.Kind == DateTimeKind.Utc ? TimeZoneInfo.ConvertTimeFromUtc(dt, SwissTz)
            : dt.Kind == DateTimeKind.Local ? TimeZoneInfo.ConvertTime(dt, SwissTz)
            : dt,
            DateTimeKind.Unspecified);

    /// <summary>
    /// Anzeige: DB-Wert ist Schweizer Wanduhr → nicht nochmals umrechnen.
    /// Liegt der Wert klar in UTC-Nähe (Alt-Daten), + Zurich-Offset.
    /// Heuristik: wenn Uhrzeit ≈ UtcNow-Face (±3 Min) und ≠ Zurich-Now → als UTC lesen.
    /// Einfach: immer as-is (Audit-UI macht dasselbe mit Unspecified).
    /// </summary>
    private static DateTime FormatDbTime(DateTime db) =>
        DateTime.SpecifyKind(db, DateTimeKind.Unspecified);

    private static int? TryParseEmployeeIdFromRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route)) return null;
        // «POST /api/employees/3504/quellensteuer» oder «PUT /api/employees/3504»
        var m = System.Text.RegularExpressions.Regex.Match(
            route, @"/employees/(\d+)(?:/|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var id)) return id;
        return null;
    }
}
