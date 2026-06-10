using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Zentrale Prüfung: ist der MA am Stichtag QST-pflichtig?
///
/// Walter-Vorgabe 26.05.2026 (ABSOLUT): Ein MA ist QST-pflichtig, AUSSER eine
/// dieser fünf Bedingungen ist erfüllt:
///   1. MA ist Schweizer Staatsbürger (NationalityRef.Code = "CH").
///   2. MA hat einen C-Ausweis (EmployeePermitHistory: PermitType.Code = "C"
///      am Stichtag gültig).
///   3. MA hat eine Befreiung der Steuerbehörde (QstBefreitDurchBehoerde + Dok +
///      Gueltig-ab/bis-Fenster).
///   4. MA ist verheiratet (marital_status) mit einem Schweizer Ehepartner
///      (EmployeeFamilyMember MemberType="Ehepartner", NationalityRef.Code="CH").
///   5. MA ist verheiratet mit einem C-Ausweis-Ehepartner (PermitType.Code="C"
///      mit gültiger Bewilligung).
///
/// Wenn KEINE Bedingung erfüllt UND keine QST-Erfassung am Stichtag
/// (EmployeeQuellensteuer mit ValidFrom &lt;= Stichtag, ValidTo &gt;= Stichtag
/// oder NULL), dann ist der MA QST-pflichtig OHNE Erfassung — das blockt den
/// Lohnlauf.
/// </summary>
public class QstPflichtCheckService
{
    private readonly AppDbContext _db;

    public QstPflichtCheckService(AppDbContext db) { _db = db; }

    public record QstPflichtCheckResult(
        bool   IsPflichtOffen,    // true = QST-pflichtig UND keine Erfassung am Stichtag → Lohnlauf-Block
        bool   IsQstPflichtig,    // true = MA grundsätzlich QST-pflichtig (kein Befreiungs-Grund)
        bool   HasErfassung,      // true = es gibt einen gültigen EmployeeQuellensteuer-Eintrag am Stichtag
        string? BefreiungsGrund,  // "CH-Buerger" | "C-Ausweis" | "Behoerde" | "Ehepartner-CH" | "Ehepartner-C" | null
        string Message,           // Klartext für UI
        // Walter-Vorgabe 28.05.2026: bei Behörden-Befreiung das hinterlegte
        // Bestätigungsschreiben zurückgeben, damit das Frontend das Dokument
        // direkt im Vorschau-Panel öffnen kann („von rechts hineinziehen").
        int?     BefreiungsDokumentId = null,
        DateOnly? BefreiungsGueltigAb = null,
        DateOnly? BefreiungsGueltigBis = null
    );

    public async Task<QstPflichtCheckResult> CheckAsync(int employeeId, DateOnly stichtag)
    {
        var emp = await _db.Employees
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null)
            return new QstPflichtCheckResult(false, false, false, null, "MA nicht gefunden.");

        // ── 1. CH-Bürger? ──
        if (string.Equals(emp.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase))
            return new QstPflichtCheckResult(false, false, false, "CH-Buerger",
                "Schweizer Staatsbürger — nicht QST-pflichtig.");

        // ── 2. MA hat C-Ausweis am Stichtag? ──
        var hasCSelf = await _db.EmployeePermitHistories
            .Include(h => h.PermitType)
            .AnyAsync(h => h.EmployeeId == employeeId
                        && h.ValidFrom <= stichtag
                        && (h.ValidTo == null || h.ValidTo >= stichtag)
                        && h.PermitType != null
                        && h.PermitType.Code == "C");
        if (hasCSelf)
            return new QstPflichtCheckResult(false, false, false, "C-Ausweis",
                "C-Ausweis (Niederlassung) — nicht QST-pflichtig.");

        // ── 3. Behörden-Befreiung gültig am Stichtag (+ Dok vorhanden) ──
        if (emp.QstBefreitDurchBehoerde
            && emp.QstBefreiungDokumentId.HasValue
            && (emp.QstBefreiungGueltigAb == null || emp.QstBefreiungGueltigAb <= stichtag)
            && (emp.QstBefreiungGueltigBis == null || emp.QstBefreiungGueltigBis >= stichtag))
        {
            return new QstPflichtCheckResult(false, false, false, "Behoerde",
                "Befreiung durch Steuerbehörde (Bestätigungsschreiben hinterlegt).",
                BefreiungsDokumentId:  emp.QstBefreiungDokumentId,
                BefreiungsGueltigAb:   emp.QstBefreiungGueltigAb,
                BefreiungsGueltigBis:  emp.QstBefreiungGueltigBis);
        }

        // ── 4./5. Ehepartner CH oder C-Ausweis? ──
        bool isVerheiratet = !string.IsNullOrWhiteSpace(emp.MaritalStatus)
                          && (emp.MaritalStatus!.Equals("verheiratet", StringComparison.OrdinalIgnoreCase)
                           || emp.MaritalStatus!.Equals("eingetragene Partnerschaft", StringComparison.OrdinalIgnoreCase));
        if (isVerheiratet)
        {
            var spouse = await _db.EmployeeFamilyMembers
                .Include(f => f.NationalityRef)
                .Include(f => f.PermitType)
                .Where(f => f.EmployeeId == employeeId
                         && f.MemberType == "Ehepartner"
                         && f.DateOfDeath == null)
                .OrderByDescending(f => f.Id)
                .FirstOrDefaultAsync();
            if (spouse != null)
            {
                // 4. Spouse Schweizer?
                if (string.Equals(spouse.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase))
                    return new QstPflichtCheckResult(false, false, false, "Ehepartner-CH",
                        "Verheiratet mit Schweizer/in — nicht QST-pflichtig.");

                // 5. Spouse C-Ausweis (mit gültigem Ablauf)?
                bool spouseHatC = spouse.PermitType?.Code == "C"
                               && (spouse.PermitExpiryDate == null
                                   || spouse.PermitExpiryDate.Value >= stichtag.ToDateTime(TimeOnly.MinValue));
                if (spouseHatC)
                    return new QstPflichtCheckResult(false, false, false, "Ehepartner-C",
                        "Verheiratet mit C-Ausweis-Inhaber — nicht QST-pflichtig.");
            }
        }

        // ── MA ist QST-pflichtig. Gibt es eine Erfassung am Stichtag? ──
        bool hasErfassung = await _db.EmployeeQuellensteuer
            .AnyAsync(q => q.EmployeeId == employeeId
                        && q.ValidFrom <= stichtag
                        && (q.ValidTo == null || q.ValidTo >= stichtag));

        if (hasErfassung)
            return new QstPflichtCheckResult(false, true, true, null,
                "QST-pflichtig — Erfassung vorhanden.");

        return new QstPflichtCheckResult(true, true, false, null,
            "QST-Pflicht offen — kein Befreiungs-Grund, keine QST-Erfassung. "
            + "Höchsten Tarif erfassen ODER Befreiungs-Schreiben der Steuerbehörde hinterlegen.");
    }

    /// <summary>Mass-Variante für Dashboard/Lohnlauf-Check: gibt die MA-IDs zurück, bei denen <see cref="QstPflichtCheckResult.IsPflichtOffen"/> = true ist.</summary>
    public async Task<List<int>> FindPflichtOffenAsync(IEnumerable<int> employeeIds, DateOnly stichtag)
    {
        var ids = employeeIds.Distinct().ToList();
        var result = new List<int>();
        foreach (var id in ids)
        {
            var r = await CheckAsync(id, stichtag);
            if (r.IsPflichtOffen) result.Add(id);
        }
        return result;
    }
}
