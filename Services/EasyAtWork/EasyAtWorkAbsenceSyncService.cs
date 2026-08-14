using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.EasyAtWork;

using HrSystem.Services;   // AbsenceHoursRecalcService, LohnEditLockService

/// <summary>
/// Absenz-Sync easy@work → OneCrew (Walter-Vorgabe 14.08.2026, Etappe 2 —
/// auf Basis der Probe-Daten vom selben Tag verifiziert):
///
/// Quellen (Support-bestätigt): `customers/{c}/absences` («unforeseen»,
/// type_id → absence_types-Katalog) und `customers/{c}/off_times`
/// (vacation=true = Ferien). ALLE from/to sind UTC-Timestamps —
/// Kalendertage IMMER via EawDateUtil (from → ParseSwissDate,
/// to → ParseSwissInclusiveEndDate; Muster «21:59:59» = End-of-day lokal).
///
/// Standard-Mapping (Walter 14.08.2026): SICKNESS/CHILD_SICKNESS/
/// MATERNITY_SICKNESS→KRANK · ACCIDENT→UNFALL · MILITARY→MILITAER ·
/// MATERNITY/PATERNITY→MUTT_VATER · PAID_LEAVE→BEZ_ABSENZ ·
/// UNPAID_LEAVE→UNBEZ_URLAUB · SCHOOL→SCHULUNG · off_time vacation=true→
/// FERIEN. NICHT importiert: PUBLIC_HOLIDAY*, WEEKLY_DAY_OFF*,
/// *COMPENSATION, off_times mit vacation=false (Wunschfrei).
///
/// Upsert-Schlüssel: absence.easyatwork_ref («A{id}» aus absences,
/// «O{id}» aus off_times — getrennte ID-Räume!). Manuell/Mirus erfasste
/// Absenzen (ref NULL) bleiben unangetastet; Überlappung mit ihnen wird
/// als Konflikt gemeldet (easy gewinnt NICHT automatisch). Absenzen in
/// definitiv abgeschlossenen Perioden werden übersprungen (Soft-Lock).
/// Manuell mit Vorschau (Preview/Commit) — Auto-Sync erst nach Testphase.
/// </summary>
public class EasyAtWorkAbsenceSyncService
{
    private readonly AppDbContext _db;
    private readonly EasyAtWorkClient _client;
    private readonly LohnEditLockService _editLock;

    public EasyAtWorkAbsenceSyncService(AppDbContext db, EasyAtWorkClient client, LohnEditLockService editLock)
    {
        _db = db;
        _client = client;
        _editLock = editLock;
    }

    /// <summary>easy@work-Typname → unser absence_type-Code (null = nicht importieren).</summary>
    public static string? MapTypeName(string? name) => (name ?? "").Trim().ToUpperInvariant() switch
    {
        "SICKNESS" or "CHILD_SICKNESS" or "MATERNITY_SICKNESS" => "KRANK",
        "ACCIDENT" => "UNFALL",
        "MILITARY" => "MILITAER",
        "MATERNITY" or "PATERNITY" => "MUTT_VATER",
        "PAID_LEAVE" => "BEZ_ABSENZ",
        "UNPAID_LEAVE" => "UNBEZ_URLAUB",
        "SCHOOL" => "SCHULUNG",
        _ => null,   // PUBLIC_HOLIDAY*, WEEKLY_DAY_OFF*, *COMPENSATION, Unbekanntes
    };

    public record SyncRow(
        string Ref, int? EmployeeId, string MaName, string? EasyTyp, string? Code,
        string Von, string Bis, decimal Prozent, string Aktion, string? Hinweis);

    public record SyncResult(
        int Neu, int Geaendert, int Geloescht, int SchonErfasst, int Fehler, int Uebersprungen,
        List<SyncRow> Zeilen);

    /// <summary>
    /// Läuft den Sync für eine Filiale. dryRun=true → nur Vorschau.
    /// vonDatum: nur Absenzen, deren ENDE ≥ vonDatum liegt (Vergangenheit
    /// kommt aus dem Mirus-Import).
    /// </summary>
    public async Task<SyncResult> RunAsync(int companyProfileId, DateOnly vonDatum, bool dryRun, CancellationToken ct,
        HashSet<string>? excludeRefs = null, bool includeFerien = false)
    {
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == companyProfileId, ct)
            ?? throw new InvalidOperationException("Filiale hat kein easy@work-Mapping.");
        int customerId = mapping.EasyAtWorkCustomerId;

        // MA-Zuordnung: easy employee_id → unser Employee (nur gemappte MA).
        var empByEaw = await _db.Employees
            .Where(e => e.EasyAtWorkEmployeeId != null && !e.IsHidden)
            .ToDictionaryAsync(e => e.EasyAtWorkEmployeeId!.Value, e => e, ct);

        // Unsere Absenz-Typen (Katalog) für Stunden-Berechnung.
        var typen = await _db.AbsenzTypen.AsNoTracking().ToListAsync(ct);

        // easy-Typ-Katalog laden (type_id → Name).
        var typeNames = new Dictionary<long, string>();
        await foreach (var el in FetchPagedAsync($"customers/{customerId}/absence_types", ct))
        {
            if (el.TryGetProperty("id", out var idP) && el.TryGetProperty("name", out var nameP))
                typeNames[idP.GetInt64()] = nameP.GetString() ?? "";
        }

        // Gewünschte Ziel-Zeilen aus beiden Quellen einsammeln.
        var wanted = new Dictionary<string, (int EmpId, string Code, DateOnly Von, DateOnly Bis, decimal Prozent, string EasyTyp)>();
        var zeilen = new List<SyncRow>();
        int uebersprungen = 0;

        void Collect(string quelle, JsonElement el)
        {
            long id = el.GetProperty("id").GetInt64();
            string r = quelle + id;
            if (el.TryGetProperty("deleted_at", out var del) && del.ValueKind == JsonValueKind.String)
                return;   // gelöschte gar nicht wollen → führt unten zum Delete
            long eawEmp = el.GetProperty("employee_id").GetInt64();

            string easyTyp;
            string? code;
            decimal prozent = 100m;
            if (quelle == "A")
            {
                long typeId = el.GetProperty("type_id").GetInt64();
                easyTyp = typeNames.TryGetValue(typeId, out var tn) ? tn : $"type {typeId}";
                code = MapTypeName(easyTyp);
                if (el.TryGetProperty("grade", out var g) && g.ValueKind == JsonValueKind.Number)
                {
                    var gd = g.GetDecimal();
                    if (gd > 0 && gd <= 1) prozent = Math.Round(gd * 100m, 0);
                }
            }
            else
            {
                bool vacation = el.TryGetProperty("vacation", out var v) && v.ValueKind == JsonValueKind.True;
                easyTyp = vacation ? "VACATION" : "OFF_TIME";
                code = vacation ? "FERIEN" : null;
            }
            if (code is null) return;   // bewusst nicht importierte Typen

            var von = EawDateUtil.ParseSwissDate(el.GetProperty("from").GetString());
            var bis = EawDateUtil.ParseSwissInclusiveEndDate(el.GetProperty("to").GetString());
            if (von is null || bis is null || bis < von) return;
            if (bis.Value < vonDatum) return;   // Vergangenheit = Mirus-Domäne

            if (!empByEaw.TryGetValue((int)eawEmp, out var emp))
            {
                zeilen.Add(new SyncRow(r, null, $"easy@work-MA {eawEmp}", easyTyp, code,
                    von.Value.ToString("yyyy-MM-dd"), bis.Value.ToString("yyyy-MM-dd"), prozent,
                    "SKIP", "MA nicht in OneCrew gemappt"));
                return;
            }
            wanted[r] = (emp.Id, code, von.Value, bis.Value, prozent, easyTyp);
        }

        await foreach (var el in FetchPagedAsync($"customers/{customerId}/absences", ct))
            Collect("A", el);
        // Ferien (off_times) vorerst DEAKTIVIERT (Walter 14.08.2026): in easy
        // werden Freiwünsche teils fälschlich als Ferien (vacation=true)
        // erfasst — erst wenn die Erfassung sauber ist, wird der Schalter
        // «Ferien importieren» aktiviert.
        if (includeFerien)
            await foreach (var el in FetchPagedAsync($"customers/{customerId}/off_times", ct))
                Collect("O", el);

        // Bestehende Sync-Absenzen (ref gesetzt) der betroffenen MA laden.
        var empIds = empByEaw.Values.Select(e => e.Id).ToList();
        var existing = await _db.Absences
            .Where(a => a.EmployeeId != 0 && a.EasyatworkRef != null && empIds.Contains(a.EmployeeId))
            .ToListAsync(ct);
        var existingByRef = existing.ToDictionary(a => a.EasyatworkRef!, a => a);

        // Manuelle Absenzen (ref NULL) — TRACKED, damit ein exakter Treffer
        // beim Commit mit easy@work verknüpft werden kann (Walter 14.08.2026).
        var manuelle = await _db.Absences
            .Where(a => a.EasyatworkRef == null && empIds.Contains(a.EmployeeId))
            .ToListAsync(ct);

        var profiles = await _db.CompanyProfiles.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var employments = await _db.Employments.AsNoTracking()
            .Where(e => empIds.Contains(e.EmployeeId))
            .ToListAsync(ct);

        var lockCache = new Dictionary<int, DateOnly?>();
        async Task<bool> IstGesperrtAsync(int employeeId, DateOnly von, DateOnly bis)
        {
            var branchId = employments
                .Where(x => x.EmployeeId == employeeId && x.IsActive)
                .OrderByDescending(x => x.ContractStartDate)
                .Select(x => (int?)x.CompanyProfileId)
                .FirstOrDefault();
            if (branchId is null) return false;
            var r = await _editLock.CheckRangePeriodAsync(null, branchId.Value, von, bis);
            return r.Locked;
        }

        int neu = 0, geaendert = 0, geloescht = 0, schonErfasst = 0, fehler = 0;
        // Vertragsmodell am Stichtag (für die Krank/Unfall-Tagesauswahl-Regel).
        string Modell(int empId, DateOnly am)
        {
            var amDt = am.ToDateTime(TimeOnly.MinValue);
            return employments
                .Where(x => x.EmployeeId == empId
                         && x.ContractStartDate <= amDt
                         && (x.ContractEndDate == null || x.ContractEndDate >= amDt))
                .OrderByDescending(x => x.ContractStartDate)
                .Select(x => x.EmploymentModel)
                .FirstOrDefault()
                ?? employments.Where(x => x.EmployeeId == empId && x.IsActive)
                    .OrderByDescending(x => x.ContractStartDate)
                    .Select(x => x.EmploymentModel).FirstOrDefault() ?? "";
        }
        // Krank/Unfall bei FIX/FIX-M/MTP: Tagesauswahl («hätte gearbeitet»)
        // muss der Benutzer nach dem Import im Absenzen-Tab eintragen.
        bool BrauchtTagesauswahl(string code, int empId, DateOnly von)
            => (code == "KRANK" || code == "UNFALL")
               && Modell(empId, von) is "FIX" or "FIX-M" or "MTP";
        string Name(int empId) => empByEaw.Values.Where(e => e.Id == empId)
            .Select(e => $"{e.FirstName} {e.LastName}".Trim()).FirstOrDefault() ?? $"MA {empId}";

        // 1) Neu + geändert
        foreach (var (r, w) in wanted)
        {
            if (existingByRef.TryGetValue(r, out var ex))
            {
                bool same = ex.EmployeeId == w.EmpId && ex.AbsenceType == w.Code
                         && ex.DateFrom == w.Von && ex.DateTo == w.Bis && ex.Prozent == w.Prozent;
                if (same) continue;
                if (await IstGesperrtAsync(ex.EmployeeId, ex.DateFrom < w.Von ? ex.DateFrom : w.Von, ex.DateTo > w.Bis ? ex.DateTo : w.Bis))
                {
                    uebersprungen++;
                    zeilen.Add(new SyncRow(r, w.EmpId, Name(w.EmpId), w.EasyTyp, w.Code,
                        w.Von.ToString("yyyy-MM-dd"), w.Bis.ToString("yyyy-MM-dd"), w.Prozent,
                        "SKIP", "Lohnperiode abgeschlossen — Änderung nicht übernommen"));
                    continue;
                }
                geaendert++;
                zeilen.Add(new SyncRow(r, w.EmpId, Name(w.EmpId), w.EasyTyp, w.Code,
                    w.Von.ToString("yyyy-MM-dd"), w.Bis.ToString("yyyy-MM-dd"), w.Prozent,
                    "UPDATE", $"vorher {ex.DateFrom:dd.MM.yyyy}–{ex.DateTo:dd.MM.yyyy} {ex.AbsenceType}"));
                if (!dryRun)
                {
                    bool datumNeu = ex.DateFrom != w.Von || ex.DateTo != w.Bis;
                    ex.EmployeeId = w.EmpId;
                    ex.AbsenceType = w.Code;
                    ex.DateFrom = w.Von;
                    ex.DateTo = w.Bis;
                    ex.Prozent = w.Prozent;
                    FillHours(ex, w.Code, profiles, employments, typen);
                    // Datumsänderung bei Krank/Unfall FIX/FIX-M/MTP: alte
                    // Tagesauswahl passt nicht mehr → leeren + Hinweis.
                    if (datumNeu && BrauchtTagesauswahl(w.Code, w.EmpId, w.Von))
                        ex.WorkedDays = null;
                    ex.UpdatedAt = DateTime.Now;
                }
                continue;
            }

            // Abgleich mit manuellen/Mirus-Absenzen (Walter 14.08.2026):
            // EXAKT gleich (Typ + Von + Bis) → «schon erfasst», beim Commit
            // wird die bestehende Absenz mit easy verknüpft (ref setzen) —
            // künftige Syncs verfolgen dann Änderungen/Löschungen in easy.
            var exakt = manuelle.FirstOrDefault(m => m.EmployeeId == w.EmpId
                && m.AbsenceType == w.Code && m.DateFrom == w.Von && m.DateTo == w.Bis);
            if (exakt != null)
            {
                schonErfasst++;
                zeilen.Add(new SyncRow(r, w.EmpId, Name(w.EmpId), w.EasyTyp, w.Code,
                    w.Von.ToString("yyyy-MM-dd"), w.Bis.ToString("yyyy-MM-dd"), w.Prozent,
                    "SCHON_ERFASST", "bereits erfasst — wird mit easy@work verknüpft"));
                if (!dryRun) exakt.EasyatworkRef = r;
                continue;
            }
            // Abweichung in Datum ODER Typ → Fehlerprotokoll, NICHT importieren.
            var konflikt = manuelle.FirstOrDefault(m => m.EmployeeId == w.EmpId
                && m.DateFrom <= w.Bis && m.DateTo >= w.Von);
            if (konflikt != null)
            {
                fehler++;
                zeilen.Add(new SyncRow(r, w.EmpId, Name(w.EmpId), w.EasyTyp, w.Code,
                    w.Von.ToString("yyyy-MM-dd"), w.Bis.ToString("yyyy-MM-dd"), w.Prozent,
                    "FEHLER", $"Abweichung zu erfasster Absenz {konflikt.AbsenceType} "
                            + $"{konflikt.DateFrom:dd.MM.yyyy}–{konflikt.DateTo:dd.MM.yyyy} — nicht importiert, bitte manuell klären"));
                continue;
            }
            if (await IstGesperrtAsync(w.EmpId, w.Von, w.Bis))
            {
                uebersprungen++;
                zeilen.Add(new SyncRow(r, w.EmpId, Name(w.EmpId), w.EasyTyp, w.Code,
                    w.Von.ToString("yyyy-MM-dd"), w.Bis.ToString("yyyy-MM-dd"), w.Prozent,
                    "SKIP", "Lohnperiode abgeschlossen"));
                continue;
            }

            // In der Vorschau abgewählte Zeilen (Walter 14.08.2026 — z.B.
            // Freiwünsche, die in easy fälschlich als Ferien erfasst sind):
            // NICHT anlegen; bestehende Sync-Absenzen bleiben unberührt.
            if (excludeRefs != null && excludeRefs.Contains(r))
            {
                uebersprungen++;
                zeilen.Add(new SyncRow(r, w.EmpId, Name(w.EmpId), w.EasyTyp, w.Code,
                    w.Von.ToString("yyyy-MM-dd"), w.Bis.ToString("yyyy-MM-dd"), w.Prozent,
                    "SKIP", "in der Vorschau abgewählt"));
                continue;
            }
            neu++;
            var tagesauswahl = BrauchtTagesauswahl(w.Code, w.EmpId, w.Von);
            zeilen.Add(new SyncRow(r, w.EmpId, Name(w.EmpId), w.EasyTyp, w.Code,
                w.Von.ToString("yyyy-MM-dd"), w.Bis.ToString("yyyy-MM-dd"), w.Prozent, "NEU",
                tagesauswahl ? "⚠ Arbeitstage («hätte gearbeitet») im Absenzen-Tab eintragen" : null));
            if (!dryRun)
            {
                var a = new Absence
                {
                    EmployeeId = w.EmpId,
                    AbsenceType = w.Code,
                    DateFrom = w.Von,
                    DateTo = w.Bis,
                    Prozent = w.Prozent,
                    EasyatworkRef = r,
                    Notes = $"easy@work-Sync ({w.EasyTyp})"
                          + (tagesauswahl ? " · ⚠ Arbeitstage (Tagesauswahl) noch eintragen" : ""),
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now,
                };
                FillHours(a, w.Code, profiles, employments, typen);
                // Krank/Unfall FIX/FIX-M/MTP: Tagesauswahl bewusst LEER lassen —
                // die Engine nutzt bis zur Pflege den Mo–Fr-Fallback (CLAUDE.md).
                if (tagesauswahl) a.WorkedDays = null;
                _db.Absences.Add(a);
            }
        }

        // 2) In easy gelöschte/weggefallene Sync-Absenzen entfernen
        //    (nur im Fenster ab vonDatum — ältere Sync-Refs bleiben stehen).
        foreach (var ex in existing)
        {
            // Ferien-Quelle deaktiviert → bereits importierte O-Refs NICHT
            // löschen (sie fehlen nur, weil off_times nicht abgerufen wurden).
            if (!includeFerien && ex.EasyatworkRef!.StartsWith("O")) continue;
            if (wanted.ContainsKey(ex.EasyatworkRef!)) continue;
            if (ex.DateTo < vonDatum) continue;
            if (await IstGesperrtAsync(ex.EmployeeId, ex.DateFrom, ex.DateTo))
            {
                uebersprungen++;
                zeilen.Add(new SyncRow(ex.EasyatworkRef!, ex.EmployeeId, Name(ex.EmployeeId), null, ex.AbsenceType,
                    ex.DateFrom.ToString("yyyy-MM-dd"), ex.DateTo.ToString("yyyy-MM-dd"), ex.Prozent,
                    "SKIP", "in easy gelöscht, aber Lohnperiode abgeschlossen"));
                continue;
            }
            geloescht++;
            zeilen.Add(new SyncRow(ex.EasyatworkRef!, ex.EmployeeId, Name(ex.EmployeeId), null, ex.AbsenceType,
                ex.DateFrom.ToString("yyyy-MM-dd"), ex.DateTo.ToString("yyyy-MM-dd"), ex.Prozent,
                "DELETE", "in easy@work gelöscht"));
            if (!dryRun) _db.Absences.Remove(ex);
        }

        if (!dryRun) await _db.SaveChangesAsync(ct);

        return new SyncResult(neu, geaendert, geloescht, schonErfasst, fehler, uebersprungen,
            zeilen.OrderBy(z => z.MaName, StringComparer.OrdinalIgnoreCase).ThenBy(z => z.Von).ToList());
    }

    /// <summary>WorkedDays + HoursCredited wie der Absenzen-Tab (BuildDays/ComputeHours).</summary>
    private static void FillHours(Absence a, string code,
        Dictionary<int, CompanyProfile> profiles, List<Employment> employments, List<AbsenzTyp> typen)
    {
        var typ = typen.FirstOrDefault(t => t.Code == code);
        if (typ is null) { a.WorkedDays = null; a.HoursCredited = 0m; return; }
        var fromDt = a.DateFrom.ToDateTime(TimeOnly.MinValue);
        var emp = employments
            .Where(x => x.EmployeeId == a.EmployeeId
                     && x.ContractStartDate <= fromDt
                     && (x.ContractEndDate == null || x.ContractEndDate >= fromDt))
            .OrderByDescending(x => x.ContractStartDate)
            .FirstOrDefault()
            ?? employments.Where(x => x.EmployeeId == a.EmployeeId && x.IsActive)
                .OrderByDescending(x => x.ContractStartDate)
                .FirstOrDefault();
        CompanyProfile? profile = null;
        if (emp?.CompanyProfileId is int bid) profiles.TryGetValue(bid, out profile);
        var days = AbsenceHoursRecalcService.BuildDaysForModus(a.DateFrom, a.DateTo, typ.GutschriftModus ?? "1/5");
        a.WorkedDays = JsonSerializer.Serialize(days);
        a.HoursCredited = AbsenceHoursRecalcService.ComputeHours(
            code, emp?.EmploymentModel ?? "", typ, profile, emp, days.Count, a.Prozent);
    }

    /// <summary>Laravel-paginiert alle Seiten durchlaufen (data[] pro Seite).</summary>
    private async IAsyncEnumerable<JsonElement> FetchPagedAsync(
        string basePath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        int page = 1;
        while (true)
        {
            var sep = basePath.Contains('?') ? '&' : '?';
            var (status, body) = await _client.GetRawAsync($"{basePath}{sep}per_page=100&page={page}", ct);
            if (status != 200)
                throw new InvalidOperationException($"easy@work {basePath} → HTTP {status}");
            var root = JsonSerializer.Deserialize<JsonElement>(body);
            if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                yield break;
            foreach (var el in data.EnumerateArray()) yield return el;
            int lastPage = root.TryGetProperty("last_page", out var lp) && lp.ValueKind == JsonValueKind.Number
                ? lp.GetInt32() : page;
            if (page >= lastPage) yield break;
            page++;
        }
    }
}
