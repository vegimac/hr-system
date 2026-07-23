using System.Globalization;
using System.Text;
using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Täglicher Änderungsdigest für Mirus-Sachbearbeiter (Walter 23.07.2026).
/// Bis Mirus abgelöst ist: OneCrew pflegt Stammdaten/Verträge/Bank/QST/…,
/// Stempelzeiten+Absenzen gehen schon automatisch. Die Sachbearbeiter
/// brauchen morgens eine Mail mit lohnkritischen Änderungen der letzten 24 h.
///
/// Empfänger: AppUser.ReceivesMirusChangeDigest + aktive E-Mail.
/// Scope: Filialen aus user_branch_access (admin/superuser ohne UBA = alle).
/// Quelle: audit_log (UTC), gefiltert auf Whitelist Entity/Feld.
/// </summary>
public class MirusChangeDigestService
{
    private readonly AppDbContext _db;
    private readonly EmailService _email;
    private readonly ILogger<MirusChangeDigestService> _log;

    private static readonly HashSet<string> WatchedEntities = new(StringComparer.Ordinal)
    {
        "Employee", "Employment", "EmployeeBankAccount", "EmployeeQuellensteuer",
        "EmployeePermitHistory", "EmployeeRecurringWage", "EmployeeLohnAssignment",
        "EmployeeFamilyMember", "FamilyMemberAllowance", "EmployeeBvgZusatzMember",
        "LohnZulage"
    };

    private static readonly HashSet<string> EmployeeFields = new(StringComparer.Ordinal)
    {
        "FirstName", "LastName", "AhvNumber", "Street", "HouseNumber", "Zip", "City",
        "CantonCode", "Country", "Nationality", "NationalityId", "MaritalStatus",
        "SeparatedSince", "Religion", "EntryDate", "ExitDate", "KuendigungPer",
        "IsActive", "IsPayrollExcluded", "QstBefreitDurchBehoerde",
        "QstBefreiungGueltigAb", "QstBefreiungGueltigBis", "LgavPflichtig",
        "TeilzeitUnter8hWoche", "IdPassDokumentId", "CAusweisDokumentId",
        "BirthDate", "PhoneMobile", "Email", "Gender", "Salutation"
    };

    private static readonly HashSet<string> EmploymentFields = new(StringComparer.Ordinal)
    {
        "EmploymentModelCode", "HourlyRate", "MonthlySalary", "MonthlySalaryFte",
        "EmploymentPercentage", "GuaranteedHoursPerWeek", "JobTitle", "JobGroupId",
        "ContractStartDate", "ContractEndDate", "ProbationEndDate", "ProbationMonths",
        "IsActive", "CompanyProfileId", "EducationLevelCode", "WeeklyHours"
    };

    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.Ordinal)
    {
        ["FirstName"] = "Vorname", ["LastName"] = "Nachname", ["AhvNumber"] = "AHV-Nr.",
        ["Street"] = "Strasse", ["HouseNumber"] = "Hausnr.", ["Zip"] = "PLZ", ["City"] = "Ort",
        ["CantonCode"] = "Wohnkanton", ["Country"] = "Land", ["Nationality"] = "Nationalität",
        ["NationalityId"] = "Nationalität", ["MaritalStatus"] = "Zivilstand",
        ["SeparatedSince"] = "Getrennt seit", ["Religion"] = "Konfession",
        ["EntryDate"] = "Eintritt", ["ExitDate"] = "Austritt", ["KuendigungPer"] = "Kündigung per",
        ["IsActive"] = "Aktiv", ["IsPayrollExcluded"] = "MA ohne Lohn",
        ["QstBefreitDurchBehoerde"] = "QST Behörden-Befreiung",
        ["QstBefreiungGueltigAb"] = "Befreiung ab", ["QstBefreiungGueltigBis"] = "Befreiung bis",
        ["LgavPflichtig"] = "L-GAV pflichtig", ["TeilzeitUnter8hWoche"] = "Teilzeit &lt;8h/Wo",
        ["IdPassDokumentId"] = "Pass/ID-Dokument", ["CAusweisDokumentId"] = "C-Ausweis-Dokument",
        ["BirthDate"] = "Geburtsdatum", ["PhoneMobile"] = "Mobile", ["Email"] = "E-Mail",
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
        ["QstCode"] = "QST-Tarif", ["TaxCanton"] = "Steuerkanton", ["TaxMunicipality"] = "Gemeinde",
        ["NumberOfChildren"] = "Kinder", ["ChurchTax"] = "Kirchensteuer",
        ["PermitTypeId"] = "Bewilligungstyp", ["Amount"] = "Betrag",
        ["Code"] = "Code", ["Betrag"] = "Betrag", ["Periode"] = "Periode",
        ["MonthlyAmount"] = "Monatsbetrag", ["AllowanceType"] = "Zulagenart",
        ["MemberType"] = "Familienmitglied", ["LohnpositionId"] = "Lohnposition"
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
        ["LohnZulage"] = "Perioden-Zulage/Abzug"
    };

    public MirusChangeDigestService(AppDbContext db, EmailService email, ILogger<MirusChangeDigestService> log)
    {
        _db = db;
        _email = email;
        _log = log;
    }

    public record DigestRunResult(int RecipientCount, int MailsSent, int ChangeCount, string Message);

    public async Task<DigestRunResult> RunAsync(CancellationToken ct = default, DateTime? sinceUtc = null, DateTime? untilUtc = null)
    {
        var until = untilUtc ?? DateTime.UtcNow;
        var since = sinceUtc ?? until.AddHours(-24);

        var recipients = await _db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive
                     && u.ReceivesMirusChangeDigest
                     && u.Email != null && u.Email != ""
                     && u.Role != "employee")
            .Select(u => new { u.Id, u.Email, u.FirstName, u.LastName, u.Role, u.Username })
            .ToListAsync(ct);

        if (recipients.Count == 0)
            return new DigestRunResult(0, 0, 0, "Keine Empfänger mit Flag «Mirus-Änderungsmail».");

        var raw = await _db.AuditLogs.AsNoTracking()
            .Where(a => a.CreatedAt >= since && a.CreatedAt < until
                     && WatchedEntities.Contains(a.EntityType))
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(ct);

        var changes = await BuildChangesAsync(raw, ct);
        if (changes.Count == 0)
        {
            // Keine Mail bei leerem Digest — Sachbearbeiter nicht mit Leermails spammen.
            _log.LogInformation("Mirus-Digest: {N} Empfänger, 0 Änderungen ({From:u}–{To:u}).",
                recipients.Count, since, until);
            return new DigestRunResult(recipients.Count, 0, 0, "Keine lohnkritischen Änderungen — keine Mails gesendet.");
        }

        var allBranchIds = changes.Select(c => c.CompanyProfileId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var branchMeta = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => allBranchIds.Contains(c.Id))
            .Select(c => new { c.Id, c.RestaurantCode, Name = c.BranchName ?? c.CompanyName })
            .ToDictionaryAsync(c => c.Id, ct);

        var ubaByUser = await _db.UserBranchAccesses.AsNoTracking()
            .Where(a => recipients.Select(r => r.Id).Contains(a.UserId))
            .GroupBy(a => a.UserId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.CompanyProfileId).ToHashSet(), ct);

        var localFrom = ToZurich(since);
        var localTo = ToZurich(until);
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
            var (html, text) = RenderMail(forUser, branchMeta.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.RestaurantCode ?? "", kv.Value.Name ?? ("Filiale " + kv.Key))),
                localFrom, localTo, name);

            var subject = $"OneCrew → Mirus: Änderungen {subjectDate} ({forUser.Count})";
            var ok = await _email.SendAsync(r.Email, name, subject, html, text);
            if (ok) sent++;
        }

        _log.LogInformation("Mirus-Digest: {Changes} Änderungen, {Recipients} Empfänger, {Sent} Mails gesendet.",
            changes.Count, recipients.Count, sent);
        return new DigestRunResult(recipients.Count, sent, changes.Count,
            $"{sent} Mail(s) an {recipients.Count} Empfänger, {changes.Count} Änderungszeilen.");
    }

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
        // IDs vorsammeln für Batch-Lookups
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

        foreach (var a in raw)
        {
            if (!int.TryParse(a.EntityId, out var id)) continue;
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
            }
        }

        var employees = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
            .ToDictionaryAsync(e => e.Id, ct);

        var employments = await _db.Employments.AsNoTracking()
            .Where(e => emplIds.Contains(e.Id))
            .Select(e => new { e.Id, e.EmployeeId, e.CompanyProfileId })
            .ToDictionaryAsync(e => e.Id, ct);

        // Für Employee-Zeilen: Filiale aus ältestem aktivem Vertrag
        var empBranch = await _db.Employments.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId) && e.IsActive && e.CompanyProfileId != null)
            .GroupBy(e => e.EmployeeId)
            .Select(g => new {
                EmployeeId = g.Key,
                CompanyProfileId = g.OrderBy(x => x.ContractStartDate).Select(x => x.CompanyProfileId).FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.CompanyProfileId, ct);

        // Child-Entities → EmployeeId
        var bankMap = await _db.EmployeeBankAccounts.AsNoTracking()
            .Where(b => bankIds.Contains(b.Id))
            .Select(b => new { b.Id, b.EmployeeId })
            .ToDictionaryAsync(b => b.Id, b => b.EmployeeId, ct);
        var qstMap = await _db.EmployeeQuellensteuer.AsNoTracking()
            .Where(b => qstIds.Contains(b.Id))
            .Select(b => new { b.Id, b.EmployeeId })
            .ToDictionaryAsync(b => b.Id, b => b.EmployeeId, ct);
        var permitMap = await _db.EmployeePermitHistories.AsNoTracking()
            .Where(b => permitIds.Contains(b.Id))
            .Select(b => new { b.Id, b.EmployeeId })
            .ToDictionaryAsync(b => b.Id, b => b.EmployeeId, ct);
        var recMap = await _db.EmployeeRecurringWages.AsNoTracking()
            .Where(b => recIds.Contains(b.Id))
            .Select(b => new { b.Id, b.EmployeeId })
            .ToDictionaryAsync(b => b.Id, b => b.EmployeeId, ct);
        var assignMap = await _db.EmployeeLohnAssignments.AsNoTracking()
            .Where(b => assignIds.Contains(b.Id))
            .Select(b => new { b.Id, b.EmployeeId })
            .ToDictionaryAsync(b => b.Id, b => b.EmployeeId, ct);
        var famMap = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(b => famIds.Contains(b.Id))
            .Select(b => new { b.Id, b.EmployeeId })
            .ToDictionaryAsync(b => b.Id, b => b.EmployeeId, ct);
        var allowMap = await (
            from a in _db.FamilyMemberAllowances.AsNoTracking()
            join f in _db.EmployeeFamilyMembers.AsNoTracking() on a.FamilyMemberId equals f.Id
            where allowIds.Contains(a.Id)
            select new { a.Id, EmpId = f.EmployeeId }
        ).ToDictionaryAsync(b => b.Id, b => b.EmpId, ct);
        var bvgMap = await _db.EmployeeBvgZusatzMembers.AsNoTracking()
            .Where(b => bvgIds.Contains(b.Id))
            .Select(b => new { b.Id, b.EmployeeId })
            .ToDictionaryAsync(b => b.Id, b => b.EmployeeId, ct);
        var zulMap = await _db.LohnZulagen.AsNoTracking()
            .Where(b => zulIds.Contains(b.Id))
            .Select(b => new { b.Id, b.EmployeeId })
            .ToDictionaryAsync(b => b.Id, b => b.EmployeeId, ct);

        // Alle betroffenen MA nachladen (für Anzeige)
        var allEmpIds = new HashSet<int>(empIds);
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

        var missingEmp = allEmpIds.Where(id => !employees.ContainsKey(id)).ToList();
        if (missingEmp.Count > 0)
        {
            var extra = await _db.Employees.AsNoTracking()
                .Where(e => missingEmp.Contains(e.Id))
                .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
                .ToListAsync(ct);
            foreach (var e in extra) employees[e.Id] = e;
        }

        var missingBranchEmp = allEmpIds.Where(id => !empBranch.ContainsKey(id)).ToList();
        if (missingBranchEmp.Count > 0)
        {
            var more = await _db.Employments.AsNoTracking()
                .Where(e => missingBranchEmp.Contains(e.EmployeeId) && e.CompanyProfileId != null)
                .GroupBy(e => e.EmployeeId)
                .Select(g => new {
                    EmployeeId = g.Key,
                    CompanyProfileId = g.OrderByDescending(x => x.IsActive)
                        .ThenByDescending(x => x.ContractStartDate)
                        .Select(x => x.CompanyProfileId).FirstOrDefault()
                })
                .ToListAsync(ct);
            foreach (var x in more) empBranch[x.EmployeeId] = x.CompanyProfileId;
        }

        var result = new List<DigestChange>();
        foreach (var a in raw)
        {
            if (!int.TryParse(a.EntityId, out var entityId)) continue;
            var summary = BuildSummary(a);
            if (summary == null) continue;

            int? employeeId = null;
            int? cpId = null;
            switch (a.EntityType)
            {
                case "Employee":
                    employeeId = entityId;
                    empBranch.TryGetValue(entityId, out cpId);
                    break;
                case "Employment":
                    if (employments.TryGetValue(entityId, out var em))
                    {
                        employeeId = em.EmployeeId;
                        cpId = em.CompanyProfileId;
                    }
                    // Fallback aus ChangesJson bei DELETE
                    if (employeeId == null)
                    {
                        var flat = TryReadFlat(a.ChangesJson);
                        if (flat != null && flat.TryGetValue("EmployeeId", out var eid) && int.TryParse(eid, out var eidI))
                            employeeId = eidI;
                        if (flat != null && flat.TryGetValue("CompanyProfileId", out var cid) && int.TryParse(cid, out var cidI))
                            cpId = cidI;
                    }
                    break;
                case "EmployeeBankAccount":
                    if (bankMap.TryGetValue(entityId, out var be)) employeeId = be;
                    break;
                case "EmployeeQuellensteuer":
                    if (qstMap.TryGetValue(entityId, out var qe)) employeeId = qe;
                    break;
                case "EmployeePermitHistory":
                    if (permitMap.TryGetValue(entityId, out var pe)) employeeId = pe;
                    break;
                case "EmployeeRecurringWage":
                    if (recMap.TryGetValue(entityId, out var re)) employeeId = re;
                    break;
                case "EmployeeLohnAssignment":
                    if (assignMap.TryGetValue(entityId, out var ae)) employeeId = ae;
                    break;
                case "EmployeeFamilyMember":
                    if (famMap.TryGetValue(entityId, out var fe)) employeeId = fe;
                    break;
                case "FamilyMemberAllowance":
                    if (allowMap.TryGetValue(entityId, out var ale)) employeeId = ale;
                    break;
                case "EmployeeBvgZusatzMember":
                    if (bvgMap.TryGetValue(entityId, out var bve)) employeeId = bve;
                    break;
                case "LohnZulage":
                    if (zulMap.TryGetValue(entityId, out var ze)) employeeId = ze;
                    break;
            }

            // DELETE: Entity oft schon weg → EmployeeId aus ChangesJson
            if (employeeId == null)
            {
                var flat = TryReadFlat(a.ChangesJson);
                if (flat != null && flat.TryGetValue("EmployeeId", out var eid) && int.TryParse(eid, out var eidI))
                    employeeId = eidI;
            }
            if (employeeId == null) continue;
            if (cpId == null) empBranch.TryGetValue(employeeId.Value, out cpId);

            employees.TryGetValue(employeeId.Value, out var emp);
            var empName = emp != null
                ? $"{emp.FirstName} {emp.LastName}".Trim()
                : $"MA #{employeeId}";
            var empNr = emp?.EmployeeNumber ?? "";

            result.Add(new DigestChange(
                a.CreatedAt, cpId, employeeId, empNr, empName,
                a.EntityType, a.Action, summary, a.UserName));
        }
        return result;
    }

    private static string? BuildSummary(AuditLog a)
    {
        var title = EntityTitles.TryGetValue(a.EntityType, out var t) ? t : a.EntityType;
        if (a.Action == "CREATE")
            return $"{title}: neu angelegt";
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
                var label = FieldLabels.TryGetValue(field, out var fl) ? fl : field;
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

            var byEmp = br
                .GroupBy(c => c.EmployeeId)
                .OrderBy(g => g.First().EmployeeName.Split(' ').FirstOrDefault() ?? "")
                .ThenBy(g => g.First().EmployeeName);

            foreach (var empG in byEmp)
            {
                var sample = empG.First();
                var empHead = string.IsNullOrEmpty(sample.EmployeeNumber)
                    ? sample.EmployeeName
                    : $"{sample.EmployeeName} (Nr. {sample.EmployeeNumber})";
                html.Append($"<div style=\"margin:10px 0 4px;font-weight:700\">{Esc(empHead)}</div><ul style=\"margin:0 0 12px 18px;padding:0\">");
                text.AppendLine($"  · {empHead}");

                foreach (var c in empG.OrderBy(x => x.CreatedAtUtc))
                {
                    var when = ToZurich(c.CreatedAtUtc).ToString("HH:mm", de);
                    var actor = string.IsNullOrWhiteSpace(c.Actor) ? "" : $" — {c.Actor}";
                    html.Append($"<li style=\"margin:3px 0\"><span style=\"color:#64748b\">{when}</span> {Esc(c.Summary)}<span style=\"color:#94a3b8\">{Esc(actor)}</span></li>");
                    text.AppendLine($"      {when}  {c.Summary}{actor}");
                }
                html.Append("</ul>");
            }
            text.AppendLine();
        }

        html.Append("<p style=\"color:#64748b;font-size:12.5px;margin-top:24px\">Stempelzeiten und Absenzen sind nicht enthalten — die laufen schon automatisch nach Mirus.<br>Diese Mail wird täglich um 06:00 an Empfänger mit dem Flag «Mirus-Änderungsmail» gesendet.</p>");
        html.Append("</div>");
        text.AppendLine();
        text.AppendLine("Stempelzeiten/Absenzen sind nicht enthalten (laufen automatisch).");
        text.AppendLine("Flag «Mirus-Änderungsmail» in der Benutzerverwaltung steuert den Empfang.");

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

    private static DateTime ToZurich(DateTime utc)
    {
        var u = utc.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utc, DateTimeKind.Utc)
            : utc.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeFromUtc(u, SwissTz);
    }
}
