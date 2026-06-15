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
        DateOnly? BefreiungsGueltigBis = null,
        // Walter-Vorgabe 12.06.2026: bei Befreiung über Ehepartner (CH oder C)
        // zusätzlich prüfen, ob der Ausweis des Ehepartners als Dokument vorliegt
        // (DokumentTyp.LinkedFieldCode = "spouse"). Falls nicht → Frontend zeigt
        // einen roten „Ausweis Ehepartner fehlt"-Warnhinweis ZUSÄTZLICH zum
        // grünen Befreiungs-Banner. Spiegelt die Logik in KontrollListenController.
        bool SpouseDokumentFehlt = false,
        // Walter-Vorgabe 13.06.2026: gleiche Prüfung auch für den MA selbst:
        //   • CH-Bürger → Identitätskarte (LinkedFieldCode='id_card') ODER
        //     Pass (LinkedFieldCode='passport') muss als Dokument vorliegen
        //   • C-Ausweis → Bewilligungs-Dokument (LinkedFieldCode='permit')
        //     muss als Dokument vorliegen
        // Falls nicht → roter Warnhinweis zusätzlich zum grünen Banner.
        bool EmployeeDokumentFehlt = false,
        // Walter-Vorgabe 14.06.2026: bei C-Ausweis die ID der jüngsten Permit-
        // History des MA mitliefern, damit das Frontend den 📎-Doku-Picker
        // auf GENAU diesen History-Eintrag richten kann (statt auf das alte
        // Employee.CAusweisDokumentId-Feld). NULL bei allen anderen Gründen.
        int? CurrentPermitHistoryId = null
    );

    public async Task<QstPflichtCheckResult> CheckAsync(int employeeId, DateOnly stichtag)
    {
        var emp = await _db.Employees
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (emp == null)
            return new QstPflichtCheckResult(false, false, false, null, "MA nicht gefunden.");

        // ── 0. Phantom-MA (Walter-Vorgabe 13.06.2026) ──
        // MA mit IsPayrollExcluded=true ist ein „Phantom-MA" (z.B. Supervisor
        // mit easy@work-Zugang ohne eigene Lohnzahlung). Für diese MA findet
        // gar keine QST-Prüfung statt — keine Pflicht, kein Befreiungs-Grund
        // nötig, keine Bewilligungs-Doku-Kontrolle. Frontend zeigt deshalb
        // bei IsQstPflichtig=false + BefreiungsGrund=null + IsPhantom=true
        // GAR KEIN Banner an.
        if (emp.IsPayrollExcluded)
            return new QstPflichtCheckResult(false, false, false, null,
                "MA ohne Lohn — keine QST-Prüfung erforderlich.");

        // ── 1. CH-Bürger? ──
        if (string.Equals(emp.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase))
        {
            // Walter-Vorgabe 13.06.2026: explizite Verknüpfung MA → Beleg-Doku
            // (Pass ODER ID-Karte) statt unscharfem linked_field_code-Scan.
            // employee.id_pass_dokument_id muss gesetzt sein UND das Dokument
            // muss noch existieren (nicht gelöscht).
            bool hasIdPassDoc = emp.IdPassDokumentId.HasValue
                && await _db.EmployeeDokumente.AnyAsync(d => d.Id == emp.IdPassDokumentId.Value);
            return new QstPflichtCheckResult(false, false, false, "CH-Buerger",
                "Schweizer Staatsbürger — nicht QST-pflichtig.",
                EmployeeDokumentFehlt: !hasIdPassDoc);
        }

        // ── 2. MA hat IRGENDWANN einen C-Ausweis bekommen? ──
        // Walter-Vorgabe 14.06.2026: „einmal C immer C" — wir prüfen NICHT
        // mehr das Ablaufdatum, sondern nur ob in der Permit-History
        // mindestens ein Eintrag mit PermitType=C existiert. C ist eine
        // Niederlassung, sie läuft administrativ nie ab (sie wird nur
        // erneuert oder durch Einbürgerung ersetzt). Das verknüpfte Doku
        // zum C-Eintrag (PermitHistory.DokumentId) zählt jetzt als Beleg.
        var cEintrag = await _db.EmployeePermitHistories
            .AsNoTracking()
            .Include(h => h.PermitType)
            .Where(h => h.EmployeeId == employeeId
                     && h.PermitType != null
                     && h.PermitType.Code == "C")
            .OrderByDescending(h => h.ValidFrom)
            .ThenByDescending(h => h.Id)
            .FirstOrDefaultAsync();
        if (cEintrag != null)
        {
            // Walter-Vorgabe 14.06.2026: Beleg-Doku jetzt am Permit-History-
            // Eintrag (DokumentId). Backwards-Compat: wenn der neue FK noch
            // nicht gesetzt ist, fällt der Check auf Employee.CAusweisDokumentId
            // zurück (alte Verknüpfungen wurden per Backfill auf die jüngste
            // Permit-History gewandert — Fallback nur falls die Migration aus
            // irgendeinem Grund nicht alle erreicht hat).
            int? belegDokId = cEintrag.DokumentId ?? emp.CAusweisDokumentId;
            bool hasCAusweisDoc = belegDokId.HasValue
                && await _db.EmployeeDokumente.AnyAsync(d => d.Id == belegDokId.Value);
            return new QstPflichtCheckResult(false, false, false, "C-Ausweis",
                "C-Ausweis (Niederlassung) — nicht QST-pflichtig.",
                EmployeeDokumentFehlt: !hasCAusweisDoc,
                CurrentPermitHistoryId: cEintrag.Id);
        }

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
                // Walter-Vorgabe 13.06.2026: explizite Verknüpfung
                // employee_family_member.dokument_id (FK auf employee_dokument).
                // Vorher: unscharfer linked_field_code='spouse'-Scan. Jetzt:
                // gleicher Mechanismus wie MA-Ausweis. Wenn das Dokument
                // gelöscht wurde, fällt der Check zurück auf „fehlt".
                bool spouseDokFehlt = !spouse.DokumentId.HasValue
                    || !await _db.EmployeeDokumente.AnyAsync(d => d.Id == spouse.DokumentId.Value);

                // 4. Spouse Schweizer?
                if (string.Equals(spouse.NationalityRef?.Code, "CH", StringComparison.OrdinalIgnoreCase))
                    return new QstPflichtCheckResult(false, false, false, "Ehepartner-CH",
                        "Verheiratet mit Schweizer/in — nicht QST-pflichtig.",
                        SpouseDokumentFehlt: spouseDokFehlt);

                // 5. Spouse C-Ausweis (mit gültigem Ablauf)?
                bool spouseHatC = spouse.PermitType?.Code == "C"
                               && (spouse.PermitExpiryDate == null
                                   || spouse.PermitExpiryDate.Value >= stichtag.ToDateTime(TimeOnly.MinValue));
                if (spouseHatC)
                    return new QstPflichtCheckResult(false, false, false, "Ehepartner-C",
                        "Verheiratet mit C-Ausweis-Inhaber — nicht QST-pflichtig.",
                        SpouseDokumentFehlt: spouseDokFehlt);
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
