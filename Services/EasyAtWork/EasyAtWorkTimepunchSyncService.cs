using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Holt Stempelzeiten aus easy@work und schreibt sie nach Bestätigung in
/// <c>employee_time_entry</c>. Zwei Modi:
///   - <see cref="PreviewAsync"/>  — read-only, gibt Vorschau zurück.
///   - <see cref="CommitAsync"/>   — schreibt INSERTs.
///
/// Match-Strategie: per <c>employee_number</c> (easy@work liefert die als
/// <c>EawEmployee.Number</c>) auf unsere <c>Employee.EmployeeNumber</c>.
/// Wer nicht matched, landet in der „unmatched"-Liste — wir importieren
/// nichts „blind". Filial-Bindung über das <see cref="EasyAtWorkBranchMapping"/>
/// (eine Filiale = ein Customer).
///
/// Dedup: identisch zum PDF-Importer — Composite-Key
/// <c>$"{EmployeeId}|{TimeIn:yyyy-MM-ddTHH:mm:ss}"</c>. Damit kann derselbe
/// Stempel beliebig oft re-importiert werden, ohne dass Duplikate entstehen
/// (auch wenn Walter zuerst per PDF und später per API zieht).
///
/// Bewusst KEIN LohnEditLockService-Check (gleich wie der bestehende
/// PDF-Importer, der ebenfalls in beliebige Perioden importiert).
/// </summary>
public class EasyAtWorkTimepunchSyncService
{
    private readonly AppDbContext _db;
    private readonly EasyAtWorkClient _client;
    private readonly ILogger<EasyAtWorkTimepunchSyncService> _log;
    private readonly LohnEditLockService _lock;

    // Source-Tag entfernt (Walter 17.06.2026) — Spalte gibt's nicht mehr,
    // alle Stempel kommen ohnehin nur noch aus easy@work.

    /// <summary>
    /// Schweizer Zeitzone für die UTC→Lokalzeit-Umrechnung. Linux nutzt IANA
    /// (Europe/Zurich), Windows den alten Namen — beide Versuche, damit der
    /// Code auf macOS-Dev UND Ubuntu-Server gleich läuft.
    /// </summary>
    private static readonly TimeZoneInfo SwissTz = FindSwissTz();
    private static TimeZoneInfo FindSwissTz()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"); }
        catch
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time"); }
            catch { return TimeZoneInfo.Utc; } // Fallback (Drift, aber kein Crash)
        }
    }

    /// <summary>
    /// Konvertiert ein DateTime-Feld aus easy@work (kommt als UTC) in
    /// Schweizer Lokalzeit OHNE Zeitzonen-Stempel — passt zum
    /// <c>employee_time_entry.time_in/out</c> Spaltentyp
    /// (<c>timestamp without time zone</c>).
    /// </summary>
    private static DateTime UtcToSwissLocal(DateTime utc)
    {
        var src = utc.Kind == DateTimeKind.Utc ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(src, SwissTz);
        return DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
    }

    /// <summary>"HH:mm" -> TimeSpan; bei Parse-Fehler Default.</summary>
    private static TimeSpan ParseHhmm(string? s, TimeSpan fallback)
    {
        if (TimeSpan.TryParse(s, out var ts)) return ts;
        return fallback;
    }

    /// <summary>
    /// Schnittmenge des Stempel-Intervalls [in, out] mit dem täglich
    /// wiederkehrenden Nachtfenster [nightStart, nightEnd). Wenn das Fenster
    /// über Mitternacht geht (z.B. 22:00-06:00), wird es korrekt als zwei
    /// Teilintervalle behandelt. Rückgabe in Stunden (decimal).
    /// </summary>
    public static decimal CalcNightHours(DateTime tin, DateTime tout, TimeSpan nightStart, TimeSpan nightEnd)
    {
        if (tout <= tin) return 0m;
        if (nightStart == nightEnd) return 0m;

        // Iteriere über jeden Tag, den die Schicht berührt, und summiere die
        // Schnittmengen mit dem Tages-Nachtfenster.
        decimal total = 0m;
        var day = tin.Date;
        var lastDay = tout.Date;
        while (day <= lastDay)
        {
            foreach (var (winStart, winEnd) in NightWindowsForDay(day, nightStart, nightEnd))
            {
                var aStart = tin  > winStart ? tin  : winStart;
                var aEnd   = tout < winEnd   ? tout : winEnd;
                if (aEnd > aStart)
                    total += (decimal)(aEnd - aStart).TotalHours;
            }
            day = day.AddDays(1);
        }
        return Math.Round(total, 2);
    }

    private static EawTimepunch? _punchById(int id, List<EawTimepunch> punches)
        => punches.FirstOrDefault(p => p.Id == id);

    /// <summary>
    /// Liest den Bearbeiter-Namen. Reihenfolge:
    /// 1. Display-Name aus dem Comment (created_by_name etc.)
    /// 2. edited_by_id auflösen über die MA-Map (sowohl als interne Id als auch
    ///    als employee_number — Walter-Hinweis 17.06.2026: easy@work liefert die
    ///    Personalnummer des bearbeitenden Managers).
    /// 3. Fallback "User #xxx" oder "easy@work".
    /// </summary>
    private static string ExtractEditorName(
        EawTimepunch? p,
        Dictionary<int, EawEmployee> empById,
        Dictionary<int, string> coworkNameByEawId)
    {
        if (p == null) return "easy@work";

        if (p.EditedById.HasValue)
        {
            var id = p.EditedById.Value;
            // 1) Cowork-DB (über easyatwork_employee_id — gesetzt beim MA-Sync,
            //    via EawEmployee.Id ODER user_id, je nachdem was matched)
            if (coworkNameByEawId.TryGetValue(id, out var coName) && !string.IsNullOrWhiteSpace(coName))
                return coName;
            // 2) easy@work-MA-Liste der aktuellen Filiale: erst via Employee.Id,
            //    dann via user_id (edited_by_id ist meist die Login-User-ID),
            //    dann via number (Personalnummer als Zahl).
            if (empById.TryGetValue(id, out var emp))
                return $"{emp.FirstName} {emp.LastName}".Trim();
            var byUserId = empById.Values.FirstOrDefault(e => e.UserId == id);
            if (byUserId != null)
                return $"{byUserId.FirstName} {byUserId.LastName}".Trim();
            var asNumber = id.ToString();
            var byNumber = empById.Values.FirstOrDefault(e => (e.Number ?? "").Trim() == asNumber);
            if (byNumber != null)
                return $"{byNumber.FirstName} {byNumber.LastName}".Trim();
            // 3) Comment-Display-Name
            var fromComment = p.Comments?
                .Select(c => c.EditorDisplayName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (!string.IsNullOrWhiteSpace(fromComment)) return fromComment!;
            return $"User #{id}";
        }

        var anyName = p.Comments?
            .Select(c => c.EditorDisplayName)
            .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        return string.IsNullOrWhiteSpace(anyName) ? "easy@work" : anyName!;
    }

    /// <summary>
    /// Liest den Zeitpunkt der Korrektur aus dem ersten Comment-CreatedAt.
    /// Fallback: API-`updated_at`. Bleibt in UTC — DB-Spalte `edited_at` ist
    /// `timestamp with time zone` und Npgsql 6+ verlangt Kind=Utc. Browser
    /// konvertiert beim Anzeigen automatisch nach lokal.
    /// </summary>
    private static DateTime? ExtractEditorTime(EawTimepunch? p)
    {
        if (p == null) return null;
        var utc = p.Comments?
            .Select(c => c.CreatedAt)
            .FirstOrDefault(t => t.HasValue);
        utc ??= p.UpdatedAt;
        if (!utc.HasValue) return null;
        return utc.Value.Kind == DateTimeKind.Utc
            ? utc.Value
            : DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc);
    }

    /// <summary>
    /// Parst aus dem easy@work-Audit-Text die Original-Zeiten:
    ///   "Ein vom 17 Januar 07:38 bis zum 17 Jan 07:15 geändert" → OriginalIn 07:38
    ///   "Aus vom 13 Juni 14:00 bis zum 13 Jun 15:00 geändert"  → OriginalOut 14:00
    /// Liest die HH:MM direkt (Audit-Text ist bereits in Lokalzeit, KEINE
    /// weitere Konvertierung nötig). Datum kommt aus business_date.
    /// </summary>
    private static (DateTime? originalIn, DateTime? originalOut) ParseEditedTimesFromComments(
        DateOnly businessDate, List<EawTimepunchComment>? comments)
    {
        if (comments == null || comments.Count == 0) return (null, null);
        DateTime? oin = null, oout = null;
        var inRx  = new System.Text.RegularExpressions.Regex(@"^Ein\s+vom\s+.+?(\d{1,2}):(\d{2})\s+bis\s+zum\s+.+?\d{1,2}:\d{2}\s+geändert",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var outRx = new System.Text.RegularExpressions.Regex(@"^Aus\s+vom\s+.+?(\d{1,2}):(\d{2})\s+bis\s+zum\s+.+?\d{1,2}:\d{2}\s+geändert",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var baseDate = businessDate.ToDateTime(TimeOnly.MinValue);
        foreach (var c in comments)
        {
            var text = (c.AnyText ?? "").Trim();
            if (string.IsNullOrEmpty(text)) continue;
            var mIn = inRx.Match(text);
            if (mIn.Success && !oin.HasValue)
            {
                oin = baseDate.AddHours(int.Parse(mIn.Groups[1].Value))
                              .AddMinutes(int.Parse(mIn.Groups[2].Value));
                continue;
            }
            var mOut = outRx.Match(text);
            if (mOut.Success && !oout.HasValue)
            {
                oout = baseDate.AddHours(int.Parse(mOut.Groups[1].Value))
                               .AddMinutes(int.Parse(mOut.Groups[2].Value));
            }
        }
        return (oin, oout);
    }

    private static IEnumerable<(DateTime start, DateTime end)> NightWindowsForDay(DateTime day, TimeSpan ns, TimeSpan ne)
    {
        if (ne > ns)
        {
            yield return (day.Add(ns), day.Add(ne));
        }
        else
        {
            // Über Mitternacht: zwei Stücke. z.B. 22:00-06:00:
            //   - day 22:00 .. day+1 00:00
            //   - day 00:00 .. day 06:00
            yield return (day.Add(ns), day.AddDays(1));
            yield return (day, day.Add(ne));
        }
    }

    public EasyAtWorkTimepunchSyncService(
        AppDbContext db,
        EasyAtWorkClient client,
        ILogger<EasyAtWorkTimepunchSyncService> log,
        LohnEditLockService lockService)
    {
        _db = db;
        _client = client;
        _log = log;
        _lock = lockService;
    }

    // ─────────────────────────── DTOs ───────────────────────────────

    public class SyncRequest
    {
        public int      CompanyProfileId { get; set; }
        public DateOnly From             { get; set; }
        public DateOnly To               { get; set; }
        /// <summary>
        /// easy@work-employee_ids, die der Admin bewusst ÜBERSPRINGT (Walter-
        /// Vorgabe 20.06.2026) — z.B. alte/gelöschte Datensätze ohne Cowork-MA.
        /// Ihre Stempel blockieren den Import nicht mehr und werden nicht
        /// geschrieben (als „Skipped" gezählt).
        /// </summary>
        public List<int> SkipEawEmployeeIds { get; set; } = new();
        /// <summary>
        /// Walter-Vorgabe 21.06.2026: einmaliger Tief-Import. Wenn gesetzt, wird
        /// dieser Stichtag statt <see cref="EmployeeCutoff"/> (1.1.2025) verwendet
        /// — so lassen sich alte Stempel (z.B. ab 1.1.2021) importieren. Die
        /// Abgeschlossene-Periode-Sperre bleibt unberührt.
        /// </summary>
        public DateOnly? EmployeeCutoffOverride { get; set; }
        /// <summary>
        /// Walter-Vorgabe 21.06.2026: einmaliger historischer Batch-Import. Wenn
        /// true, blockieren nicht zuordenbare easy@work-MA (MissingEmployees) den
        /// Commit NICHT — ihre Stempel werden still fallen gelassen statt den
        /// ganzen Lauf zu stoppen. Nur für den bewussten Tief-Import gedacht; der
        /// reguläre manuelle/automatische Sync lässt das auf false (Block bleibt).
        /// </summary>
        public bool IgnoreMissing { get; set; } = false;
    }

    public class TimepunchPreviewRow
    {
        public int       EawTimepunchId { get; set; }
        /// <summary>easy@work-interne employee_id des Stempels (für Alias-Zuordnung).</summary>
        public int       EawEmployeeId  { get; set; }
        public string?   EawEmployeeNumber { get; set; }
        public string?   EawEmployeeName   { get; set; }
        public int?      CoworkEmployeeId  { get; set; }
        public DateOnly  BusinessDate      { get; set; }
        public DateTime? TimeIn            { get; set; }
        public DateTime? TimeOut           { get; set; }
        public DateTime? OriginalTimeIn    { get; set; }
        public DateTime? OriginalTimeOut   { get; set; }
        public decimal?  Hours             { get; set; }
        public decimal?  NightHours        { get; set; }
        public string?   Comment           { get; set; }
        public bool      IsEdited          { get; set; }
        /// <summary>NEW / DUPLICATE / UNMATCHED / SOFT_DELETED / INVALID</summary>
        public string    Status            { get; set; } = "NEW";
        public string?   Reason            { get; set; }
    }

    public class SyncResult
    {
        public bool   IsPreview { get; set; }
        public int    CustomerId { get; set; }
        public DateOnly From { get; set; }
        public DateOnly To   { get; set; }
        public int    CountTotal     { get; set; }
        public int    CountNew       { get; set; }
        public int    CountDuplicate { get; set; }
        public int    CountUnmatched { get; set; }
        public int    CountSoftDeleted { get; set; }
        public int    CountInvalid   { get; set; }
        public int    CountLocked    { get; set; }
        public int    CountSkipped   { get; set; }   // bewusst übersprungene MA
        public int    CountInserted  { get; set; }
        public List<TimepunchPreviewRow> Rows { get; set; } = new();
        public List<string> Notes { get; set; } = new();
        /// <summary>
        /// Blockierend: easy@work-MA mit importierbaren Stempeln, die NICHT auf
        /// eine Cowork-Personalnummer (oder einen Alias) abbildbar sind. Ist die
        /// Liste nicht leer, wird der Commit verweigert.
        /// </summary>
        public List<MissingEmployee> MissingEmployees { get; set; } = new();
        public int    CountMissing => MissingEmployees.Count;
        /// <summary>
        /// Blockierend (Walter 21.06.2026): easy@work-MA, für den MEHRERE Cowork-
        /// MA mit IsPayrollExcluded=false existieren — der Lohn-MA ist nicht
        /// eindeutig. Muss bereinigt werden, BLOCKIERT immer (auch beim Tief-
        /// Import mit IgnoreMissing), weil es ein Datenfehler ist.
        /// </summary>
        public List<MissingEmployee> AmbiguousEmployees { get; set; } = new();
        public int    CountAmbiguous => AmbiguousEmployees.Count;
        public bool   IsBlocked    => MissingEmployees.Count > 0 || AmbiguousEmployees.Count > 0;
    }

    /// <summary>Ein easy@work-MA, der für den Import nicht zugeordnet werden kann.</summary>
    public class MissingEmployee
    {
        public int     EawEmployeeId     { get; set; }
        public string? EawEmployeeNumber { get; set; }
        public string? EawEmployeeName   { get; set; }
        public int     TimepunchCount    { get; set; }
        public string  Reason            { get; set; } = "";
    }

    /// <summary>Ergebnis eines automatischen (Hintergrund-)Sync-Laufs pro Filiale.</summary>
    public class AutoSyncResult
    {
        public DateOnly  From { get; set; }
        public DateOnly  To   { get; set; }
        public bool      UsedUpdatesFeed { get; set; }
        public int       Inserted { get; set; }
        public int       Updated  { get; set; }
        public int       Deleted  { get; set; }
        public int       LockedSkipped { get; set; }   // wegen gesperrter Periode übersprungen
        public int       Skipped  { get; set; }        // Duplikate / nicht zuordenbar (Delete-Ziel fehlt)
        public DateTime? MaxUpdatedAt { get; set; }     // höchstes updated_at → neuer Cursor
        public List<MissingEmployee> MissingEmployees { get; set; } = new();
        public List<string> Notes { get; set; } = new();
        public bool      IsBlocked => MissingEmployees.Count > 0;
        /// <summary>Insert + Update + Delete (für last_row_count).</summary>
        public int       RowCount => Inserted + Updated + Deleted;
        /// <summary>Detail der ECHTEN Änderungen (neu + Wert-geändert) für das
        /// Protokoll-Drill-Down — KEINE identischen Neuschreibungen.</summary>
        public List<SyncChange> Changes { get; set; } = new();
    }

    /// <summary>Eine echte Stempel-Änderung für das Detail-Log (Variante A).</summary>
    public class SyncChange
    {
        public int      EmployeeId { get; set; }
        public DateOnly Date       { get; set; }
        public string   Action     { get; set; } = "";   // "neu" | "geaendert"
        public decimal? OldTotal   { get; set; }
        public decimal  NewTotal   { get; set; }
        public decimal? OldNight   { get; set; }
        public decimal  NewNight   { get; set; }
    }

    // ──────────────── Reine, testbare Matching-Logik ─────────────────
    // Walter-Vorgabe 18.06.2026: MA-Liste = ALLE (inkl. inaktive), lokal nur
    // die ganz alten Austritte (vor 2025-01-01 = vor der Mirus-Migration)
    // wegfiltern. Davor + Preflight als seiteneffektfreie Funktionen, damit
    // sie ohne DB/HTTP unit-getestet werden können.

    /// <summary>Stichtag: MA mit Austritt VOR diesem Datum sind irrelevant (Pre-Mirus).</summary>
    public static readonly DateOnly EmployeeCutoff = new(2025, 1, 1);

    /// <summary>
    /// Filtert aus der (inkl. inaktive geladenen) easy@work-MA-Liste die ganz
    /// alten Austritte heraus: wer ein Austrittsdatum (To) VOR
    /// <see cref="EmployeeCutoff"/> hat, fällt weg. Ohne To (kein Austritt) oder
    /// Austritt >= Stichtag bleibt drin — auch MA, die erst mitten im Zeitraum
    /// eingetreten sind (From spielt für diesen Filter keine Rolle).
    /// </summary>
    public static List<EawEmployee> FilterRelevantEmployees(IEnumerable<EawEmployee> emps)
        => FilterRelevantEmployees(emps, EmployeeCutoff);

    /// <summary>
    /// Wie oben, aber mit explizitem Stichtag — für den einmaligen Tief-Import
    /// (Walter-Vorgabe 21.06.2026, z.B. zurück bis 1.1.2021). MA mit Austritt
    /// VOR <paramref name="cutoff"/> fallen weg.
    /// </summary>
    public static List<EawEmployee> FilterRelevantEmployees(IEnumerable<EawEmployee> emps, DateOnly cutoff)
        => emps.Where(e => !(e.To.HasValue && e.To.Value < cutoff)).ToList();

    /// <summary>
    /// Preflight: ermittelt alle easy@work-MA, die im Zeitraum importierbare
    /// Stempel haben, sich aber NICHT auf eine Cowork-Personalnummer abbilden
    /// lassen (Nummer fehlt ODER existiert nicht in Cowork) UND auch keinen
    /// Alias haben. Solange diese Liste nicht leer ist, darf NICHT committet
    /// werden — sonst gingen Stempel verloren bzw. landeten beim falschen MA.
    /// Seiteneffektfrei (alle Daten als Parameter) → unit-testbar.
    /// </summary>
    public static List<MissingEmployee> ComputePreflightMissing(
        IEnumerable<EawTimepunch> punches,
        IReadOnlyDictionary<int, EawEmployee> eawEmpById,
        ISet<string> coworkNumbers,
        IReadOnlyDictionary<int, int> aliasMap,
        ISet<int>? coworkEawIds = null)
    {
        var missing = new Dictionary<int, MissingEmployee>();
        foreach (var p in punches)
        {
            // Nur Stempel zählen, die überhaupt importiert würden.
            if (p.DeletedAt != null) continue;   // in easy@work gelöscht
            if (!p.In.HasValue)      continue;   // ungültig (kein TimeIn)

            eawEmpById.TryGetValue(p.EmployeeId, out var emp);

            // Per hinterlegter easy@work-ID auflösbar → kein Problem (Walter
            // 21.06.2026, primärer Match — deckt Pre-Mirus-„alt"-MA ab). Der
            // Stempel referenziert die employee.id; gespeichert wurde `user_id
            // ?? id` → beide Kandidaten prüfen.
            if (coworkEawIds != null && (coworkEawIds.Contains(p.EmployeeId)
                || (emp?.UserId is int uid && coworkEawIds.Contains(uid)))) continue;

            // Per hinterlegtem Alias auflösbar → kein Problem.
            if (aliasMap.ContainsKey(p.EmployeeId)) continue;

            var number = emp?.Number?.Trim();

            // Sauber abbildbar?
            if (!string.IsNullOrEmpty(number) && coworkNumbers.Contains(number))
                continue;

            // Fehlt → in die Block-Liste (pro MA EIN Eintrag, Stempel zählen).
            if (missing.TryGetValue(p.EmployeeId, out var existing))
            {
                existing.TimepunchCount++;
                continue;
            }
            string reason;
            if (emp == null)
                reason = "easy@work-MA nicht in der Mitarbeiterliste auffindbar (alter/gelöschter Datensatz) — bitte zuordnen.";
            else if (string.IsNullOrEmpty(number))
                reason = "easy@work-MA hat keine Personalnummer — bitte zuordnen oder in easy@work nachtragen.";
            else
                reason = $"Personalnummer '{number}' existiert nicht in Cowork — MA zuerst anlegen/importieren.";

            missing[p.EmployeeId] = new MissingEmployee
            {
                EawEmployeeId     = p.EmployeeId,
                EawEmployeeNumber = number,
                EawEmployeeName   = emp == null ? null : $"{emp.FirstName} {emp.LastName}".Trim(),
                TimepunchCount    = 1,
                Reason            = reason,
            };
        }
        return missing.Values.OrderBy(m => m.EawEmployeeName ?? "").ToList();
    }

    // ───────────────── Payroll-Sink-Auflösung (Walter 21.06.2026) ─────────────────
    // Ein easy@work-MA kann in mehreren Filialen stempeln, hat dort aber IMMER
    // dieselbe easy@work-employee-id. Bei uns ist er ggf. mehrfach abgelegt (1×
    // pro Filiale). Regel: alle Stempel landen auf dem EINEN Cowork-MA mit
    // IsPayrollExcluded=false (= seine Lohnfiliale). Sind alle Kandidaten
    // ausgeschlossen (z.B. Supervisor wie Nihat, überall true) → keine Stempel.
    // Gibt es mehrere nicht-ausgeschlossene Kandidaten → Datenfehler → Block.

    public enum PayrollMatchKind { Matched, AllExcluded, Ambiguous, NoCandidate }

    public readonly record struct CoworkCandidate(int EmployeeId, bool IsPayrollExcluded);

    public readonly record struct PayrollMatchResult(PayrollMatchKind Kind, int? SinkEmployeeId, int PayrollCandidateCount);

    /// <summary>
    /// Wählt aus den Cowork-Kandidaten (alle MA, die zur selben Person/easy@work-
    /// id bzw. Personalnummer gehören) den Lohn-MA. Seiteneffektfrei → unit-testbar.
    /// </summary>
    public static PayrollMatchResult ResolvePayrollSink(IEnumerable<CoworkCandidate> candidates)
    {
        var distinct = candidates
            .GroupBy(c => c.EmployeeId)
            .Select(g => g.First())
            .ToList();
        if (distinct.Count == 0)
            return new PayrollMatchResult(PayrollMatchKind.NoCandidate, null, 0);

        var payroll = distinct.Where(c => !c.IsPayrollExcluded).ToList();
        if (payroll.Count == 0)
            return new PayrollMatchResult(PayrollMatchKind.AllExcluded, null, 0);
        if (payroll.Count > 1)
            return new PayrollMatchResult(PayrollMatchKind.Ambiguous, null, payroll.Count);

        return new PayrollMatchResult(PayrollMatchKind.Matched, payroll[0].EmployeeId, 1);
    }

    /// <summary>
    /// Berechnet das Sync-Fenster für eine Filiale (Walter-Vorgabe 19.06.2026):
    ///   from = max(Start der ältesten NICHT definitiv abgeschlossenen Periode,
    ///              today − 40 Tage),  to = today.
    /// Gibt null zurück, wenn keine offene Periode existiert (→ Sync überspringen).
    /// Seiteneffektfrei → unit-testbar.
    /// </summary>
    public static (DateOnly From, DateOnly To)? ComputeSyncWindow(
        DateOnly? oldestOpenPeriodStart, DateOnly today)
    {
        if (oldestOpenPeriodStart is null) return null;
        var floor = today.AddDays(-40);
        var from  = oldestOpenPeriodStart.Value > floor ? oldestOpenPeriodStart.Value : floor;
        if (from > today) from = today;   // nie ein negatives Fenster
        return (from, today);
    }

    /// <summary>
    /// Lock-Gating: ein Stempel an <paramref name="date"/> darf nur geschrieben/
    /// geändert/gelöscht werden, wenn seine Periode nicht gesperrt ist — also
    /// firstAllowed == null (keine Sperre) ODER date &gt;= firstAllowed.
    /// </summary>
    public static bool IsEditable(DateOnly date, DateOnly? firstAllowed)
        => firstAllowed is null || date >= firstAllowed.Value;

    /// <summary>
    /// Import-Sperre PRO PERIODE (Walter-Vorgabe 20.06.2026): ein Stempel darf nur
    /// importiert/geändert/gelöscht werden, wenn sein Datum NICHT in einer
    /// ABGESCHLOSSENEN Lohnperiode liegt. Vor und nach einer abgeschlossenen
    /// Periode ist erlaubt — auch historische Daten (2025) bleiben importierbar,
    /// solange für ihren Monat keine abgeschlossene Periode existiert. Untergrenze
    /// ist der Mirus-Stichtag <see cref="EmployeeCutoff"/> (1.1.2025); davor (Pre-
    /// Mirus) wird nichts importiert.
    /// </summary>
    public static bool IsImportable(DateOnly date, List<(DateOnly From, DateOnly To)> closedPeriods)
        => IsImportable(date, closedPeriods, EmployeeCutoff);

    /// <summary>
    /// Wie oben, aber mit explizitem Stichtag — für den einmaligen Tief-Import
    /// alter Stempel (Walter-Vorgabe 21.06.2026, z.B. ab 1.1.2021). Die
    /// Abgeschlossene-Periode-Sperre bleibt immer aktiv.
    /// </summary>
    public static bool IsImportable(DateOnly date, List<(DateOnly From, DateOnly To)> closedPeriods, DateOnly cutoff)
        => date >= cutoff && !closedPeriods.Any(r => date >= r.From && date <= r.To);

    /// <summary>Datumsbereiche der ABGESCHLOSSENEN Lohnperioden einer Filiale.</summary>
    private async Task<List<(DateOnly From, DateOnly To)>> GetClosedPeriodRangesAsync(int cpId, CancellationToken ct)
        => (await _db.PayrollPerioden.AsNoTracking()
                .Where(p => p.CompanyProfileId == cpId && p.Status == "abgeschlossen")
                .Select(p => new { p.PeriodFrom, p.PeriodTo })
                .ToListAsync(ct))
            .Select(p => (p.PeriodFrom, p.PeriodTo)).ToList();

    // ────────────────────────── Public API ──────────────────────────

    public Task<SyncResult> PreviewAsync(SyncRequest req, CancellationToken ct = default)
        => SyncCoreAsync(req, commit: false, ct);

    /// <summary>
    /// Manueller Commit (Walter-Vorgabe 19.06.2026): nutzt JETZT denselben
    /// lock-gegateten Schreibpfad wie der Auto-Sync (<see cref="ApplyTimepunchesAsync"/>)
    /// — schreibt also NICHT mehr in gesperrte Lohnperioden. <paramref name="firstAllowed"/>
    /// kommt aus dem Controller (User-aware LohnEditLockService). Aktualisiert
    /// zusätzlich den TIMEPUNCH-Sync-State (Cursor + UI-Status).
    /// </summary>
    public async Task<AutoSyncResult> CommitAsync(SyncRequest req, DateOnly? firstAllowed, CancellationToken ct = default)
    {
        var res = new AutoSyncResult { From = req.From, To = req.To };
        if (req.To < req.From) { res.Notes.Add("Ungültiger Datumsbereich (Bis ist vor Von)."); return res; }
        if ((req.To.DayNumber - req.From.DayNumber) > 92) { res.Notes.Add("Bereich auf max. 92 Tage begrenzt."); return res; }

        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == req.CompanyProfileId, ct);
        if (mapping == null) { res.Notes.Add("Diese Filiale hat kein easy@work-Mapping."); return res; }

        List<EawTimepunch> punches;
        try { punches = await _client.GetAllTimepunchesAsync(mapping.EasyAtWorkCustomerId, req.From, req.To, ct); }
        catch (Exception ex) { res.Notes.Add($"easy@work-Aufruf fehlgeschlagen: {ex.Message}"); return res; }

        var maxUpd = punches.Where(p => p.UpdatedAt.HasValue).Select(p => p.UpdatedAt!.Value).DefaultIfEmpty().Max();

        res = await ApplyTimepunchesAsync(mapping, punches, req.From, req.To, firstAllowed, req.SkipEawEmployeeIds, ct);
        res.UsedUpdatesFeed = false;
        res.MaxUpdatedAt = maxUpd == default(DateTime) ? null : maxUpd;

        // Sync-State aktualisieren (Cursor + UI-Status) — nur wenn nicht blockiert.
        if (!res.IsBlocked)
        {
            var st = await _db.EasyAtWorkSyncStates
                .FirstOrDefaultAsync(s => s.CompanyProfileId == req.CompanyProfileId && s.Resource == "TIMEPUNCH", ct);
            if (st == null)
            {
                st = new EasyAtWorkSyncState { CompanyProfileId = req.CompanyProfileId, Resource = "TIMEPUNCH" };
                _db.EasyAtWorkSyncStates.Add(st);
            }
            st.LastSyncAt = DateTime.UtcNow;
            if (res.MaxUpdatedAt.HasValue) st.LastSeenUpdatedAt = res.MaxUpdatedAt;
            st.LastRowCount = res.RowCount;
            st.LastError = null;
            await _db.SaveChangesAsync(ct);
        }
        return res;
    }

    /// <summary>
    /// Automatischer (Hintergrund-)Sync EINER Filiale im Fenster [from,to]
    /// (Walter-Vorgabe 19.06.2026). Quelle: timepunch_updates wenn ein Cursor
    /// (last_seen_updated_at) existiert, sonst die volle timepunches-Liste.
    /// Schreibt/ändert/löscht NUR Stempel, deren Periode nicht durch den
    /// LohnEditLockService gesperrt ist. Bei fehlenden MA (Preflight) wird der
    /// Sync der Filiale blockiert (Rückgabe mit MissingEmployees, kein Schreiben).
    /// </summary>
    public async Task<AutoSyncResult> AutoSyncAsync(
        EasyAtWorkBranchMapping mapping, DateOnly from, DateOnly to,
        EasyAtWorkSyncState? state, CancellationToken ct = default)
    {
        // 1) Quelle wählen: Delta-Feed wenn Cursor vorhanden, sonst Vollabzug.
        //    Ist der Delta-Endpunkt (noch) nicht verfügbar, Fallback auf den
        //    vollen Abzug — der Job bleibt so nicht hängen.
        var useUpdates = state?.LastSeenUpdatedAt.HasValue == true;
        List<EawTimepunch> punches;
        if (useUpdates)
        {
            try
            {
                punches = await _client.GetAllTimepunchUpdatesAsync(
                    mapping.EasyAtWorkCustomerId, state!.LastSeenUpdatedAt!.Value, ct);
            }
            catch (HttpRequestException ex)
            {
                _log.LogWarning(ex, "easy@work timepunch_updates nicht verfügbar — Fallback auf vollen Abzug.");
                useUpdates = false;
                punches = await _client.GetAllTimepunchesAsync(mapping.EasyAtWorkCustomerId, from, to, ct);
            }
        }
        else
        {
            punches = await _client.GetAllTimepunchesAsync(mapping.EasyAtWorkCustomerId, from, to, ct);
        }

        // Cursor: höchstes updated_at über ALLE geholten Punches (auch die wir
        // gleich ausserhalb des Fensters ignorieren) → nächster Lauf macht weiter.
        var maxUpd = punches.Where(p => p.UpdatedAt.HasValue).Select(p => p.UpdatedAt!.Value)
            .DefaultIfEmpty().Max();

        // Gemeinsamer Schreibpfad (denselben nutzt der manuelle Commit).
        var res = await ApplyTimepunchesAsync(mapping, punches, from, to, firstAllowedOverride: null, skipEawIds: null, ct: ct);
        res.UsedUpdatesFeed = useUpdates;
        res.MaxUpdatedAt = maxUpd == default(DateTime) ? state?.LastSeenUpdatedAt : maxUpd;
        return res;
    }

    /// <summary>
    /// GEMEINSAME Schreiblogik für Auto-Sync UND manuellen Commit (Walter-Vorgabe
    /// 19.06.2026): lokaler [from,to]-Filter, Preflight NUR über editierbare (nicht
    /// gesperrte) Stempel, dann lock-gegateter Insert/Update/Delete. Schreibt am
    /// Ende einmal in die DB.
    /// <paramref name="firstAllowedOverride"/>: vom manuellen Commit aus dem
    /// Controller (User-aware <see cref="LohnEditLockService"/>) durchgereicht;
    /// null = hier selbst berechnen (Auto-Sync).
    /// </summary>
    public async Task<AutoSyncResult> ApplyTimepunchesAsync(
        EasyAtWorkBranchMapping mapping, List<EawTimepunch> punches,
        DateOnly from, DateOnly to, DateOnly? firstAllowedOverride,
        IReadOnlyCollection<int>? skipEawIds = null, CancellationToken ct = default)
    {
        var res = new AutoSyncResult { From = from, To = to };
        var skipSet = skipEawIds is { Count: > 0 } ? new HashSet<int>(skipEawIds) : new HashSet<int>();

        // 2) Lokaler Fenster-Filter (gilt AUCH für den Delta-Feed): nur Punches mit
        //    Datum in [from,to]. Datum = business_date, sonst aus In abgeleitet.
        DateOnly? PunchDate(EawTimepunch p) =>
            p.BusinessDate ?? (p.In.HasValue ? DateOnly.FromDateTime(UtcToSwissLocal(p.In.Value)) : (DateOnly?)null);
        var windowed = punches
            .Where(p => { var d = PunchDate(p); return d.HasValue && d.Value >= from && d.Value <= to; })
            .ToList();
        if (windowed.Count == 0) return res;

        // 3) MA-Pool (inkl. inaktive, Pre-2025-Austritte gefiltert) + Alias.
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden)
            .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName })
            .ToListAsync(ct);
        // Alte Personalnummern aus der Alias-Tabelle (Walter-Vorgabe 21.06.2026).
        var aliasNumsByEmp = (await _db.EmployeeNumberAliases.AsNoTracking()
                .Select(a => new { a.Number, a.EmployeeId }).ToListAsync(ct))
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Number).ToList());
        // Keys = aktuelle Personalnummer UND alle Alias-Nummern.
        var byNumber = emps
            .SelectMany(e =>
            {
                var keys = new List<string>();
                if (!string.IsNullOrWhiteSpace(e.EmployeeNumber)) keys.Add(e.EmployeeNumber!.Trim());
                if (aliasNumsByEmp.TryGetValue(e.Id, out var al))
                    keys.AddRange(al.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()));
                return keys.Select(k => new { Key = k, Emp = e });
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Emp, StringComparer.OrdinalIgnoreCase);
        var aliasMap = (await _db.EasyAtWorkEmployeeAliases.AsNoTracking()
                .Select(a => new { a.EasyAtWorkId, a.EmployeeId }).ToListAsync(ct))
            .GroupBy(a => a.EasyAtWorkId).ToDictionary(g => g.Key, g => g.First().EmployeeId);

        // easy@work-MA-Liste (für Number-Auflösung). Schlägt der Aufruf fehl,
        // propagiert die Exception zum Orchestrator → landet in last_error.
        Dictionary<int, EawEmployee> eawEmpById = new();
        var eawEmps = FilterRelevantEmployees(
            await _client.GetAllEmployeesIncludingInactiveAsync(mapping.EasyAtWorkCustomerId, ct));
        foreach (var e in eawEmps) eawEmpById[e.Id] = e;

        var coworkByEawId = await _db.Employees.AsNoTracking()
            .Where(e => e.EasyAtWorkEmployeeId.HasValue && !e.IsHidden)
            .Select(e => new { e.EasyAtWorkEmployeeId, e.FirstName, e.LastName }).ToListAsync(ct);
        var coworkNameByEawId = coworkByEawId
            .GroupBy(x => x.EasyAtWorkEmployeeId!.Value)
            .ToDictionary(g => g.Key, g => $"{g.First().FirstName} {g.First().LastName}".Trim());

        // 4) Import-Sperre PRO PERIODE (Walter-Vorgabe 20.06.2026): Stempel in einer
        //    ABGESCHLOSSENEN Lohnperiode werden NICHT geschrieben — davor und danach
        //    (inkl. historische 2025-Daten) ist erlaubt. Der Preflight prüft dann
        //    NUR Stempel, die tatsächlich importiert würden.
        var closedPeriods = await GetClosedPeriodRangesAsync(mapping.CompanyProfileId, ct);
        // Übersprungene MA (Walter-Vorgabe 20.06.2026) fliessen NICHT in den
        // Preflight-Block ein — sie werden weiter unten als „Skipped" gezählt.
        var editableForPreflight = windowed
            .Where(p => { var d = PunchDate(p); return d.HasValue && IsImportable(d.Value, closedPeriods)
                          && !skipSet.Contains(p.EmployeeId); });
        res.MissingEmployees = ComputePreflightMissing(
            editableForPreflight, eawEmpById, new HashSet<string>(byNumber.Keys), aliasMap);
        if (res.IsBlocked) return res;

        // 5) Nachtfenster der Filiale.
        var cp = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == mapping.CompanyProfileId, ct);
        var nightStart = ParseHhmm(cp?.NightStartTime, new TimeSpan(0, 0, 0));
        var nightEnd   = ParseHhmm(cp?.NightEndTime,   new TimeSpan(7, 0, 0));

        // 6) Vorhandene DB-Stempel: per easy@work-ID (Update/Delete, TRACKED) +
        //    Lokalzeit-Key (Dedup für Inserts ohne ID-Match).
        var punchIds = windowed.Where(p => p.Id != 0).Select(p => p.Id).Distinct().ToList();
        var dbByEawId = (await _db.EmployeeTimeEntries
                .Where(t => t.EasyAtWorkTimepunchId.HasValue && punchIds.Contains(t.EasyAtWorkTimepunchId.Value))
                .ToListAsync(ct))
            .GroupBy(t => t.EasyAtWorkTimepunchId!.Value).ToDictionary(g => g.Key, g => g.First());
        var fromDt = from.ToDateTime(TimeOnly.MinValue);
        var toDt   = to.ToDateTime(TimeOnly.MaxValue);
        var existingKeys = new HashSet<string>(await _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => t.TimeIn >= fromDt && t.TimeIn <= toDt)
            .Select(t => t.EmployeeId + "|" + t.TimeIn.ToString("yyyy-MM-ddTHH:mm:ss"))
            .ToListAsync(ct));

        // 7) Verarbeiten — Lock-Gating gilt für INSERT, UPDATE UND DELETE.
        var seenBatch = new HashSet<int>();
        foreach (var p in windowed)
        {
            if (p.Id != 0 && !seenBatch.Add(p.Id)) continue; // Pagination-Dublette
            if (skipSet.Contains(p.EmployeeId)) { res.Skipped++; continue; } // bewusst übersprungener MA
            var pDate = PunchDate(p)!.Value;
            if (!IsImportable(pDate, closedPeriods)) { res.LockedSkipped++; continue; }

            dbByEawId.TryGetValue(p.Id, out var existing);

            // a) In easy@work gelöscht → Cowork-Zeile entfernen (falls vorhanden).
            if (p.DeletedAt != null)
            {
                if (existing != null) { _db.EmployeeTimeEntries.Remove(existing); res.Deleted++; }
                continue;
            }
            if (!p.In.HasValue) { res.Skipped++; continue; }

            // MA auflösen (Number, sonst Alias) — Preflight hat das bereits abgesichert.
            int? coworkId = null;
            if (eawEmpById.TryGetValue(p.EmployeeId, out var eawEmp)
                && !string.IsNullOrWhiteSpace(eawEmp.Number)
                && byNumber.TryGetValue(eawEmp.Number!.Trim(), out var byNum))
                coworkId = byNum.Id;
            else if (aliasMap.TryGetValue(p.EmployeeId, out var aliasId)) coworkId = aliasId;
            if (coworkId == null) { res.Skipped++; continue; }

            var inLocal  = UtcToSwissLocal(p.In.Value);
            var outLocal = p.Out.HasValue ? UtcToSwissLocal(p.Out.Value) : (DateTime?)null;
            decimal total = (outLocal.HasValue && outLocal.Value > inLocal)
                ? Math.Round((decimal)(outLocal.Value - inLocal).TotalHours, 2)
                : (p.Hours ?? 0m);
            decimal night = outLocal.HasValue ? CalcNightHours(inLocal, outLocal.Value, nightStart, nightEnd) : 0m;
            decimal duration = Math.Round(total - night, 2);
            var businessDate = p.BusinessDate ?? DateOnly.FromDateTime(inLocal);
            var (origIn, origOut) = ParseEditedTimesFromComments(businessDate, p.Comments);
            var editorName = p.IsEdited ? ExtractEditorName(p, eawEmpById, coworkNameByEawId) : null;
            var editorTime = p.IsEdited ? ExtractEditorTime(p) : (DateTime?)null;

            // b) Bekannte easy@work-ID → UPDATE.
            if (existing != null)
            {
                // ECHTE Änderung erkennen (vor dem Überschreiben), um identische
                // Neuschreibungen NICHT ins Detail-Log zu nehmen (Variante A).
                bool realChange = existing.TotalHours != total
                               || existing.NightHours != night
                               || existing.TimeIn     != inLocal
                               || existing.TimeOut    != outLocal
                               || existing.EntryDate  != businessDate;
                decimal? oldTotal = existing.TotalHours;
                decimal? oldNight = existing.NightHours;

                existing.EmployeeId    = coworkId.Value;
                existing.EntryDate     = businessDate;
                existing.TimeIn        = inLocal;
                existing.TimeOut       = outLocal;
                existing.Comment       = p.JoinedComments;
                existing.TotalHours    = total;
                existing.NightHours    = night;
                existing.DurationHours = duration;
                existing.UpdatedAt     = DateTime.UtcNow;
                existing.EditedBy      = editorName;
                existing.EditedAt      = editorTime;
                existing.OriginalTimeIn  = origIn;
                existing.OriginalTimeOut = origOut;
                existing.EasyAtWorkCustomerId   = mapping.EasyAtWorkCustomerId;
                existing.SourceCompanyProfileId = mapping.CompanyProfileId;
                res.Updated++;
                if (realChange)
                    res.Changes.Add(new SyncChange {
                        EmployeeId = coworkId.Value, Date = businessDate, Action = "geaendert",
                        OldTotal = oldTotal, NewTotal = total, OldNight = oldNight, NewNight = night
                    });
                continue;
            }

            // c) Dedup gegen Lokalzeit → sonst INSERT.
            var key = coworkId.Value + "|" + inLocal.ToString("yyyy-MM-ddTHH:mm:ss");
            if (existingKeys.Contains(key)) { res.Skipped++; continue; }
            _db.EmployeeTimeEntries.Add(new EmployeeTimeEntry
            {
                EmployeeId    = coworkId.Value,
                EntryDate     = businessDate,
                TimeIn        = inLocal,
                TimeOut       = outLocal,
                Comment       = p.JoinedComments,
                TotalHours    = total,
                NightHours    = night,
                DurationHours = duration,
                CreatedAt     = DateTime.UtcNow,
                UpdatedAt     = DateTime.UtcNow,
                EditedBy      = editorName,
                EditedAt      = editorTime,
                EasyAtWorkTimepunchId = p.Id,
                EasyAtWorkCustomerId    = mapping.EasyAtWorkCustomerId,
                SourceCompanyProfileId  = mapping.CompanyProfileId,
                OriginalTimeIn  = origIn,
                OriginalTimeOut = origOut,
                OriginalComment = p.JoinedComments,
            });
            existingKeys.Add(key);
            res.Inserted++;
            res.Changes.Add(new SyncChange {
                EmployeeId = coworkId.Value, Date = businessDate, Action = "neu",
                OldTotal = null, NewTotal = total, OldNight = null, NewNight = night
            });
        }

        await _db.SaveChangesAsync(ct);
        return res;
    }

    // ─────────────────────────── Core ───────────────────────────────

    private async Task<SyncResult> SyncCoreAsync(SyncRequest req, bool commit, CancellationToken ct)
    {
        var res = new SyncResult { IsPreview = !commit, From = req.From, To = req.To };

        if (req.To < req.From)
        {
            res.Notes.Add("Ungültiger Datumsbereich (Bis ist vor Von).");
            return res;
        }
        if ((req.To.DayNumber - req.From.DayNumber) > 92)
        {
            res.Notes.Add("Bereich auf max. 92 Tage begrenzt — zu viele Tage auf einmal.");
            return res;
        }

        // 1) Mapping CompanyProfile → easy@work-Customer + Filial-Settings (Nachtfenster)
        var mapping = await _db.EasyAtWorkBranchMappings.AsNoTracking()
            .FirstOrDefaultAsync(m => m.CompanyProfileId == req.CompanyProfileId, ct);
        if (mapping == null)
        {
            res.Notes.Add("Diese Filiale hat kein easy@work-Mapping. Bitte zuerst in 'easy@work API -> Filial-Mappings' hinterlegen.");
            return res;
        }
        res.CustomerId = mapping.EasyAtWorkCustomerId;

        // Nachtfenster aus CompanyProfile lesen (Default 00:00-07:00).
        var cp = await _db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == req.CompanyProfileId, ct);
        var nightStart = ParseHhmm(cp?.NightStartTime, new TimeSpan(0, 0, 0));
        var nightEnd   = ParseHhmm(cp?.NightEndTime,   new TimeSpan(7, 0, 0));

        // Stichtag für diesen MANUELLEN Lauf: Standard 1.1.2025, beim einmaligen
        // Tief-Import überschrieben (Walter-Vorgabe 21.06.2026). Der tägliche
        // Auto-Sync nutzt weiterhin den festen Standard.
        var cutoff = req.EmployeeCutoffOverride ?? EmployeeCutoff;

        // 2) Mitarbeiter-Pool für den Match. Employee hat KEINE direkte Filial-FK
        //    (die läuft über Employment.CompanyProfileId pro Vertrag). Schaub
        //    Restaurants GmbH ist EIN AHV-Arbeitgeber → Personalnummern sind
        //    konzernweit eindeutig, also matchen wir gegen ALLE nicht-versteckten
        //    MA. Die Stempelzeit kommt nachweisbar vom easy@work-Customer dieser
        //    Filiale, also ist der MA-Match per Personalnummer unkritisch.
        var emps = await _db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden)
            .Select(e => new {
                e.Id,
                e.EmployeeNumber,
                e.FirstName,
                e.LastName,
                e.IsPayrollExcluded,
                e.EasyAtWorkEmployeeId
            })
            .ToListAsync(ct);
        // Alte Personalnummern aus der Alias-Tabelle (Walter-Vorgabe 21.06.2026).
        var aliasNumsByEmp = (await _db.EmployeeNumberAliases.AsNoTracking()
                .Select(a => new { a.Number, a.EmployeeId }).ToListAsync(ct))
            .GroupBy(a => a.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(a => a.Number).ToList());
        // GRUPPEN (nicht .First()): zu einer Nummer / easy@work-id können MEHRERE
        // Cowork-MA gehören (derselbe Mensch in mehreren Filialen abgelegt).
        // Die Lohn-MA-Auswahl (IsPayrollExcluded=false) passiert pro Stempel via
        // ResolvePayrollSink. Keys = aktuelle Personalnummer UND alle Alias-Nummern,
        // damit ein MA auch unter einer alten Nummer gefunden wird.
        var byNumber = emps
            .SelectMany(e =>
            {
                var keys = new List<string>();
                if (!string.IsNullOrWhiteSpace(e.EmployeeNumber)) keys.Add(e.EmployeeNumber!.Trim());
                if (aliasNumsByEmp.TryGetValue(e.Id, out var al))
                    keys.AddRange(al.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!.Trim()));
                return keys.Select(k => new { Key = k, Emp = e });
            })
            .GroupBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Emp).ToList(), StringComparer.OrdinalIgnoreCase);
        // Cowork-MA per interner Id (für die Alias-Auflösung unten).
        var empById = emps.ToDictionary(e => e.Id, e => e);
        // Cowork-MA per hinterlegter easy@work-employee-id (Walter-Vorgabe
        // 21.06.2026). PRIMÄRER Match-Schlüssel: kollisionssicher und immun
        // gegen den „alt"-Suffix bei Pre-Mirus-Personalnummern UND gegen
        // Nummern-Wiederverwendung nach der Mirus-Migration. Ein 2021er-Stempel
        // darf NIE einem aktuellen MA zugeordnet werden, der dieselbe Nummer
        // (ohne „alt") wiederverwendet — die easy@work-ID verhindert das.
        var byEawId = emps
            .Where(e => e.EasyAtWorkEmployeeId.HasValue)
            .GroupBy(e => e.EasyAtWorkEmployeeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Alias-Map: alte/zweite easy@work-employee_id → Cowork-MA-Id. Greift als
        // Fallback, wenn ein Stempel auf eine ID zeigt, die die normale MA-Liste
        // nicht kennt (ID-Wechsel in easy@work). Walter 18.06.2026.
        // MUSS vor GatherCandidates deklariert sein (die lokale Funktion nutzt sie).
        var aliasMap = (await _db.EasyAtWorkEmployeeAliases.AsNoTracking()
                .Select(a => new { a.EasyAtWorkId, a.EmployeeId })
                .ToListAsync(ct))
            .GroupBy(a => a.EasyAtWorkId)
            .ToDictionary(g => g.Key, g => g.First().EmployeeId);

        // Sammelt für einen Stempel ALLE Cowork-Kandidaten (per easy@work-id,
        // per user_id, per Personalnummer, per Alias) — dedupliziert über die
        // interne Cowork-Id. Lokale Funktion, weil sie die anonym-typisierten
        // Maps oben braucht. Walter-Vorgabe 21.06.2026.
        List<CoworkCandidate> GatherCandidates(int eawEmployeeId, EawEmployee? ee, string? number)
        {
            var seen = new HashSet<int>();
            var list = new List<CoworkCandidate>();
            void AddId(int id, bool excluded) { if (seen.Add(id)) list.Add(new CoworkCandidate(id, excluded)); }
            if (byEawId.TryGetValue(eawEmployeeId, out var g1)) foreach (var e in g1) AddId(e.Id, e.IsPayrollExcluded);
            if (ee?.UserId is int uid && byEawId.TryGetValue(uid, out var g2)) foreach (var e in g2) AddId(e.Id, e.IsPayrollExcluded);
            var n = number?.Trim();
            if (!string.IsNullOrEmpty(n) && byNumber.TryGetValue(n, out var g3)) foreach (var e in g3) AddId(e.Id, e.IsPayrollExcluded);
            if (aliasMap.TryGetValue(eawEmployeeId, out var aid) && empById.TryGetValue(aid, out var ae)) AddId(ae.Id, ae.IsPayrollExcluded);
            return list;
        }

        // 3) Stempelzeiten aus easy@work holen (alle Seiten)
        List<EawTimepunch> punches;
        try
        {
            punches = await _client.GetAllTimepunchesAsync(mapping.EasyAtWorkCustomerId, req.From, req.To, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "easy@work GET timepunches fehlgeschlagen");
            res.Notes.Add($"easy@work-Aufruf fehlgeschlagen: {ex.Message}");
            return res;
        }

        // 4) Vorhandene Stempel laden (für Dedup) — gleiche Filiale, gleicher Zeitraum.
        //    Zwei Dedup-Pfade: (a) easy@work-Timepunch-Id wenn vorhanden (sauberer),
        //    (b) TimeIn-Hash als Fallback für Alt-Daten ohne ID.
        var fromDt = req.From.ToDateTime(TimeOnly.MinValue);
        var toDt   = req.To.ToDateTime(TimeOnly.MaxValue);
        var existing = await _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => t.TimeIn >= fromDt && t.TimeIn <= toDt)
            .Select(t => new { t.EmployeeId, t.TimeIn, t.EasyAtWorkTimepunchId })
            .ToListAsync(ct);
        var existingKeys = new HashSet<string>(
            existing.Select(t => $"{t.EmployeeId}|{t.TimeIn:yyyy-MM-ddTHH:mm:ss}"));
        // Dedup über die easy@work-Timepunch-Id NICHT auf den Datumsbereich
        // beschränken: ein Stempel kann unter einem TimeIn knapp ausserhalb
        // [from,to] schon importiert sein (Zeitzonen-Rand). Wir fragen die bereits
        // vorhandenen IDs direkt für die geholten Punch-IDs ab — so wird ein
        // bereits importierter Stempel in JEDEM Fall als DUPLICATE erkannt und
        // nicht erneut eingefügt (sonst → Unique-Index-Verletzung beim Commit).
        var fetchedPunchIds = punches.Where(p => p.Id != 0).Select(p => p.Id).Distinct().ToList();
        var existingByEawId = new HashSet<int>(
            await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => t.EasyAtWorkTimepunchId.HasValue && fetchedPunchIds.Contains(t.EasyAtWorkTimepunchId.Value))
                .Select(t => t.EasyAtWorkTimepunchId!.Value)
                .ToListAsync(ct));

        // 5) Wir müssen pro easy@work-Punch den MA finden. Die easy@work-API liefert
        //    auf `/customers/{c}/timepunches` ein EawTimepunch mit `EmployeeId`
        //    (= easy@work-employee-id), aber NICHT die employee_number. Wir holen
        //    daher zusätzlich die Employee-Liste der Filiale (1 Aufruf) und mappen.
        Dictionary<int, EawEmployee> eawEmpById = new();
        try
        {
            // Diese Liste dient zum Auflösen von employee_id → Personalnummer
            // (+ Bearbeiter-Namen), also muss sie so breit wie möglich sein. Ein
            // Stichtag-Filter (`active=req.From`) verfehlt MA, die ERST IM MONAT
            // eingetreten sind (z.B. Angela Miteva am 28.02. mit Eintritt nach
            // Periodenbeginn) oder andere Aktiv-Lücken haben → deren Stempel
            // landeten fälschlich als UNMATCHED. Darum laden wir ALLE MA der
            // Filiale inkl. ausgetretene und filtern nur die ganz alten Austritte
            // (vor 2025-01-01 = Pre-Mirus) lokal weg. (Walter-Vorgabe 18.06.2026.)
            var eawEmps = FilterRelevantEmployees(
                await _client.GetAllEmployeesIncludingInactiveAsync(mapping.EasyAtWorkCustomerId, ct), cutoff);
            foreach (var e in eawEmps) eawEmpById[e.Id] = e;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "easy@work GET employees fehlgeschlagen");
            res.Notes.Add($"MA-Liste konnte nicht geladen werden ({ex.Message}). Manche MA werden ggf. als 'unbekannt' angezeigt.");
        }

        // Cowork-Lookup: easy@work-Employee-ID → Cowork-MA-Name (für edited_by_id-
        // Auflösung von Managern, die ggf. nicht in der aktuellen Filiale stehen).
        var coworkByEawId = await _db.Employees.AsNoTracking()
            .Where(e => e.EasyAtWorkEmployeeId.HasValue && !e.IsHidden)
            .Select(e => new { e.EasyAtWorkEmployeeId, e.FirstName, e.LastName })
            .ToListAsync(ct);
        var coworkNameByEawId = coworkByEawId
            .GroupBy(x => x.EasyAtWorkEmployeeId!.Value)
            .ToDictionary(g => g.Key, g => $"{g.First().FirstName} {g.First().LastName}".Trim());

        // Import-Sperre PRO PERIODE (Walter-Vorgabe 20.06.2026): die Vorschau
        // kennzeichnet Stempel in ABGESCHLOSSENEN Lohnperioden als LOCKED (der
        // Commit überspringt sie); davor/danach (inkl. 2025) ist erlaubt.
        var closedPeriods = await GetClosedPeriodRangesAsync(req.CompanyProfileId, ct);
        var skipSet = req.SkipEawEmployeeIds is { Count: > 0 }
            ? new HashSet<int>(req.SkipEawEmployeeIds) : new HashSet<int>();

        // 5b) Preflight (Walter-Vorgabe 18.06.2026): jeder easy@work-MA mit
        //     EDITIERBAREN importierbaren Stempeln muss auf eine Cowork-Personal-
        //     nummer (oder einen Alias) abbildbar sein. Sonst Block. Bewusst
        //     übersprungene MA (Walter 20.06.2026) blockieren NICHT.
        var editablePunches = punches.Where(p =>
        {
            var d = p.BusinessDate ?? (p.In.HasValue ? DateOnly.FromDateTime(UtcToSwissLocal(p.In.Value)) : (DateOnly?)null);
            return d.HasValue && IsImportable(d.Value, closedPeriods, cutoff) && !skipSet.Contains(p.EmployeeId);
        });
        // Preflight nach Payroll-Sink-Regel (Walter 21.06.2026): pro easy@work-MA
        // mit importierbaren Stempeln den Lohn-MA auflösen. KEIN Kandidat → Missing
        // (Block, sofern nicht IgnoreMissing). MEHRERE Lohn-MA → Ambiguous (Block,
        // immer). Alle ausgeschlossen → kein Problem (Stempel werden übersprungen).
        var missingPf  = new Dictionary<int, MissingEmployee>();
        var ambigPf    = new Dictionary<int, MissingEmployee>();
        foreach (var p in editablePunches)
        {
            if (p.DeletedAt != null || !p.In.HasValue) continue;
            eawEmpById.TryGetValue(p.EmployeeId, out var ee);
            var match = ResolvePayrollSink(GatherCandidates(p.EmployeeId, ee, ee?.Number));
            if (match.Kind == PayrollMatchKind.Matched || match.Kind == PayrollMatchKind.AllExcluded)
                continue;
            var bucket = match.Kind == PayrollMatchKind.Ambiguous ? ambigPf : missingPf;
            if (bucket.TryGetValue(p.EmployeeId, out var ex)) { ex.TimepunchCount++; continue; }
            var num0 = ee?.Number?.Trim();
            bucket[p.EmployeeId] = new MissingEmployee
            {
                EawEmployeeId     = p.EmployeeId,
                EawEmployeeNumber = num0,
                EawEmployeeName   = ee == null ? null : $"{ee.FirstName} {ee.LastName}".Trim(),
                TimepunchCount    = 1,
                Reason            = match.Kind == PayrollMatchKind.Ambiguous
                    ? $"Mehrere Cowork-Lohn-MA (IsPayrollExcluded=false) für diese Person — Lohn-MA nicht eindeutig. Bitte bereinigen (genau einen als Lohn-MA führen)."
                    : (ee == null
                        ? "easy@work-MA nicht in der Mitarbeiterliste auffindbar — bitte zuordnen."
                        : string.IsNullOrEmpty(num0)
                            ? "easy@work-MA hat keine Personalnummer — bitte zuordnen."
                            : $"Personalnummer '{num0}' existiert nicht in Cowork — MA zuerst anlegen/importieren."),
            };
        }
        res.MissingEmployees   = missingPf.Values.OrderBy(m => m.EawEmployeeName ?? "").ToList();
        res.AmbiguousEmployees = ambigPf.Values.OrderBy(m => m.EawEmployeeName ?? "").ToList();

        // 6) Pro Punch Status berechnen
        // Innerhalb DIESER Antwort schon gesehene easy@work-IDs — die API kann
        // denselben Stempel über zwei Pagination-Seiten doppelt liefern. Ohne
        // diese Sperre würden beide als NEW eingefügt → Unique-Index-Crash.
        var seenBatchIds = new HashSet<int>();
        foreach (var p in punches)
        {
            // easy@work liefert TimeIn/Out in UTC → in Schweizer Lokalzeit umrechnen,
            // damit Dedup gegen die bestehenden (Lokalzeit-)Stempel matched UND
            // die DB den korrekten Wert speichert (Spalte ist „timestamp without
            // time zone" = Lokalzeit-Konvention).
            var inLocal  = p.In.HasValue  ? UtcToSwissLocal(p.In.Value)  : (DateTime?)null;
            var outLocal = p.Out.HasValue ? UtcToSwissLocal(p.Out.Value) : (DateTime?)null;
            // Stunden: API liefert teils nichts → aus Lokalzeit-Intervall rechnen.
            // Gesamt-Stunden (immer aus Lokalzeit rechnen, API-`hours` ist optional)
            decimal? hours = (inLocal.HasValue && outLocal.HasValue && outLocal > inLocal)
                ? Math.Round((decimal)(outLocal.Value - inLocal.Value).TotalHours, 2)
                : p.Hours;
            // Nacht-Stunden für die Vorschau (selbe Logik wie beim Commit)
            decimal? nightPreview = (inLocal.HasValue && outLocal.HasValue)
                ? CalcNightHours(inLocal.Value, outLocal.Value, nightStart, nightEnd)
                : (decimal?)null;
            var row = new TimepunchPreviewRow
            {
                EawTimepunchId   = p.Id,
                EawEmployeeId    = p.EmployeeId,
                BusinessDate     = p.BusinessDate ?? (inLocal.HasValue ? DateOnly.FromDateTime(inLocal.Value) : DateOnly.MinValue),
                TimeIn           = inLocal,
                TimeOut          = outLocal,
                Hours            = hours,
                NightHours       = nightPreview,
                Comment          = p.JoinedComments,
                IsEdited         = p.IsEdited,
            };
            // Bewusst übersprungener MA (Walter 20.06.2026): als SKIPPED zeigen,
            // nicht importieren, nicht blockieren.
            if (skipSet.Contains(p.EmployeeId))
            {
                row.Status = "SKIPPED";
                row.Reason = "Übersprungen — nicht zugeordneter MA.";
                res.Rows.Add(row); res.CountSkipped++; continue;
            }
            // Original-Zeit (vor manueller Korrektur):
            // 1. PRIMÄR: aus dem Audit-Text in den Comments parsen (Walter 17.06.2026).
            //    Format: "Ein vom 17 Januar 07:38 bis zum 17 Jan 07:15 geändert".
            //    Diese Zeiten sind in LOKALZEIT (wie easy@work-UI sie zeigt) → KEINE
            //    weitere Konvertierung.
            // 2. Fallback (nur wenn Comments nichts ergeben): aus `created_at` ableiten
            //    (Ur-Stempel-Zeitpunkt; weniger zuverlässig, kann durch DB-Latenz
            //    leicht abweichen).
            var (origIn, origOut) = ParseEditedTimesFromComments(row.BusinessDate, p.Comments);
            row.OriginalTimeIn  = origIn;
            row.OriginalTimeOut = origOut;
            if (!row.OriginalTimeIn.HasValue && p.IsEdited && p.CreatedAt.HasValue && inLocal.HasValue
                && Math.Abs((p.CreatedAt.Value - p.In!.Value).TotalMinutes) >= 1)
            {
                row.OriginalTimeIn = UtcToSwissLocal(p.CreatedAt.Value);
            }

            var eawResolved = eawEmpById.TryGetValue(p.EmployeeId, out var eawEmp);
            if (eawResolved)
            {
                row.EawEmployeeNumber = eawEmp!.Number;
                row.EawEmployeeName   = $"{eawEmp.FirstName} {eawEmp.LastName}".Trim();
            }
            else
            {
                // MA konnte nicht aufgelöst werden (war nicht in der geladenen
                // Mitarbeiterliste). Statt „?" wenigstens die easy@work-interne
                // ID zeigen, damit Walter die Person identifizieren kann.
                row.EawEmployeeName = $"easy@work-MA #{p.EmployeeId}";
            }

            // Soft-deleted in easy@work? — überspringen.
            if (p.DeletedAt != null)
            {
                row.Status = "SOFT_DELETED";
                row.Reason = "In easy@work gelöscht — wird nicht importiert.";
                res.Rows.Add(row); res.CountSoftDeleted++; continue;
            }

            // Ungültig: ohne TimeIn lässt sich nichts speichern (Tabellen-NOT-NULL).
            if (!p.In.HasValue)
            {
                row.Status = "INVALID";
                row.Reason = "Kein TimeIn-Zeitstempel.";
                res.Rows.Add(row); res.CountInvalid++; continue;
            }

            // MA-Match nach Payroll-Sink-Regel (Walter 21.06.2026): ALLE Cowork-
            // Kandidaten der Person sammeln (per easy@work-id, user_id, Personal-
            // nummer, Alias) und den EINEN Lohn-MA (IsPayrollExcluded=false) wählen.
            // 1 Treffer → Stempel dorthin; 0 Lohn-MA (alle ausgeschlossen, z.B.
            // Supervisor) → übersprungen; mehrere → blockiert; keiner → UNMATCHED.
            var num = (row.EawEmployeeNumber ?? "").Trim();
            var match = ResolvePayrollSink(GatherCandidates(p.EmployeeId, eawResolved ? eawEmp : null, num));

            if (match.Kind == PayrollMatchKind.AllExcluded)
            {
                row.Status = "SKIPPED";
                row.Reason = "Alle Cowork-Datensätze dieser Person sind vom Lohn ausgeschlossen (IsPayrollExcluded) — keine Stempelzeiten.";
                res.Rows.Add(row); res.CountSkipped++; continue;
            }
            if (match.Kind == PayrollMatchKind.Ambiguous)
            {
                row.Status = "UNMATCHED";
                row.Reason = "Mehrere Lohn-MA (IsPayrollExcluded=false) für diese Person — Lohn-MA nicht eindeutig. Import blockiert, bitte bereinigen.";
                res.Rows.Add(row); res.CountUnmatched++; continue;
            }
            if (match.Kind == PayrollMatchKind.NoCandidate || match.SinkEmployeeId is null)
            {
                row.Status = "UNMATCHED";
                if (!string.IsNullOrEmpty(num))
                    row.Reason = $"Keine Cowork-MA mit Personalnr. '{num}'.";
                else if (!eawResolved)
                    row.Reason = $"easy@work-MA #{p.EmployeeId} war nicht in der Mitarbeiterliste — evtl. erst später eingetreten, schon ausgetreten oder Konto ohne Personalnummer.";
                else
                    row.Reason = $"easy@work-MA '{row.EawEmployeeName}' (#{p.EmployeeId}) hat keine Personalnummer hinterlegt.";
                res.Rows.Add(row); res.CountUnmatched++; continue;
            }
            var coEmp = empById[match.SinkEmployeeId.Value];
            row.CoworkEmployeeId = coEmp.Id;

            // Dublette INNERHALB dieser easy@work-Antwort (Pagination-Überschneidung)?
            // Erste Vorkommnis: Add() == true → durchlassen. Zweite: == false → DUPLICATE.
            if (p.Id != 0 && !seenBatchIds.Add(p.Id))
            {
                row.Status = "DUPLICATE";
                row.Reason = "Mehrfach in der easy@work-Antwort enthalten.";
                res.Rows.Add(row); res.CountDuplicate++; continue;
            }

            // Dedup: zuerst über die easy@work-Timepunch-Id, dann über die
            // Lokalzeit (für Alt-Daten ohne ID).
            if (existingByEawId.Contains(p.Id))
            {
                row.Status = "DUPLICATE";
                row.Reason = "Bereits importiert (easy@work-ID übereinstimmt).";
                res.Rows.Add(row); res.CountDuplicate++; continue;
            }
            var key = $"{coEmp.Id}|{inLocal!.Value:yyyy-MM-ddTHH:mm:ss}";
            if (existingKeys.Contains(key))
            {
                row.Status = "DUPLICATE";
                row.Reason = "Stempel mit gleichem TimeIn existiert bereits.";
                res.Rows.Add(row); res.CountDuplicate++; continue;
            }

            // In abgeschlossener Lohnperiode (oder vor 1.1.2025)? Vorschau zeigt
            // LOCKED — der Commit überspringt ihn.
            if (!IsImportable(row.BusinessDate, closedPeriods, cutoff))
            {
                row.Status = "LOCKED";
                row.Reason = row.BusinessDate < cutoff
                    ? $"Vor {cutoff:dd.MM.yyyy} — wird nicht importiert."
                    : "Liegt in einer abgeschlossenen Lohnperiode — wird nicht importiert.";
                res.Rows.Add(row); res.CountLocked++; continue;
            }

            row.Status = "NEW";
            res.Rows.Add(row); res.CountNew++;
        }

        res.CountTotal = res.Rows.Count;

        // Mehrere Lohn-MA (IsPayrollExcluded=false) für dieselbe Person = Daten-
        // fehler → IMMER blockieren, auch beim Tief-Import (IgnoreMissing). Walter
        // 21.06.2026. Muss zuerst bereinigt werden (genau einen Lohn-MA führen).
        if (commit && res.AmbiguousEmployees.Count > 0)
        {
            res.Notes.Add($"Import blockiert: {res.CountAmbiguous} Person(en) mit MEHREREN Lohn-MA (IsPayrollExcluded=false). Bitte je Person genau einen Lohn-MA führen, die übrigen auf 'kein Lohn' setzen.");
            return res;
        }

        // Blockierender Preflight: bei fehlenden Zuordnungen wird NICHT geschrieben
        // (auch keine Teil-Menge) — sonst gingen die Stempel der nicht zuordenbaren
        // MA still verloren. Der Preview zeigt die Block-Liste; hier wird der
        // Commit verweigert. Walter-Vorgabe 18.06.2026.
        if (commit && res.MissingEmployees.Count > 0 && !req.IgnoreMissing)
        {
            res.Notes.Add($"Import blockiert: {res.CountMissing} easy@work-MA ohne gültige Cowork-Zuordnung. Bitte die betroffenen MA zuerst zuordnen oder in Cowork anlegen.");
            return res;
        }
        if (commit && res.MissingEmployees.Count > 0 && req.IgnoreMissing)
        {
            // Tief-Import: nicht zuordenbare MA werden NICHT geschrieben (die
            // Insert-Schleife unten filtert auf CoworkEmployeeId.HasValue), aber
            // sie blockieren den Lauf nicht. Nur als Hinweis protokollieren.
            res.Notes.Add($"{res.CountMissing} nicht zuordenbare easy@work-MA übersprungen (Tief-Import).");
        }

        // 7) Commit-Pfad: NEW-Zeilen tatsächlich schreiben.
        if (commit && res.CountNew > 0)
        {
            foreach (var row in res.Rows.Where(r => r.Status == "NEW" && r.CoworkEmployeeId.HasValue && r.TimeIn.HasValue))
            {
                // Mirus/PDF-Konvention für employee_time_entry:
                //   TotalHours    = Gesamt   (TimeOut - TimeIn)
                //   NightHours    = davon Nacht (Schnitt mit Filial-Nachtfenster)
                //   DurationHours = Tag      (Total - Night)
                decimal? total = (row.TimeIn.HasValue && row.TimeOut.HasValue && row.TimeOut.Value > row.TimeIn.Value)
                    ? Math.Round((decimal)(row.TimeOut.Value - row.TimeIn.Value).TotalHours, 2)
                    : row.Hours;
                decimal? night = (row.TimeIn.HasValue && row.TimeOut.HasValue)
                    ? CalcNightHours(row.TimeIn.Value, row.TimeOut.Value, nightStart, nightEnd)
                    : (decimal?)null;
                decimal? duration = (total.HasValue)
                    ? Math.Round(total.Value - (night ?? 0m), 2)
                    : (decimal?)null;

                var entry = new EmployeeTimeEntry
                {
                    EmployeeId   = row.CoworkEmployeeId!.Value,
                    EntryDate    = row.BusinessDate == DateOnly.MinValue
                                   ? DateOnly.FromDateTime(row.TimeIn!.Value)
                                   : row.BusinessDate,
                    TimeIn       = row.TimeIn!.Value,
                    TimeOut      = row.TimeOut,
                    Comment      = row.Comment,
                    DurationHours= duration,
                    NightHours   = night,
                    TotalHours   = total ?? 0m,
                    CreatedAt    = DateTime.UtcNow,
                    UpdatedAt    = DateTime.UtcNow,
                    // Bearbeiter + Zeitpunkt: edited_by_id → erst Cowork-DB-Lookup
                    // (via easyatwork_employee_id), dann easy@work-MA-Liste, dann
                    // Comment-Display-Name. Walter 17.06.2026.
                    EditedBy     = row.IsEdited ? ExtractEditorName(_punchById(row.EawTimepunchId, punches), eawEmpById, coworkNameByEawId) : null,
                    EditedAt     = row.IsEdited ? ExtractEditorTime(_punchById(row.EawTimepunchId, punches)) : (DateTime?)null,
                    EasyAtWorkTimepunchId = row.EawTimepunchId,
                    // Herkunft (Walter 21.06.2026): in welcher Filiale (easy@work-
                    // Customer) wurde gestempelt — bleibt nachvollziehbar, auch wenn
                    // der Stempel auf den Lohn-MA einer anderen Filiale gespeichert
                    // wird. Der ganze Lauf gehört zu EINEM Customer/Filiale.
                    EasyAtWorkCustomerId    = mapping.EasyAtWorkCustomerId,
                    SourceCompanyProfileId  = req.CompanyProfileId,
                    // Original-Zeit (vor manueller Korrektur) — bereits in der
                    // Preview-Row korrekt aus created_at abgeleitet, hier nur durchreichen.
                    OriginalTimeIn  = row.OriginalTimeIn,
                    OriginalTimeOut = row.OriginalTimeOut,
                    OriginalComment = row.Comment,   // Audit-Kommentar aus easy@work
                };
                _db.EmployeeTimeEntries.Add(entry);
                res.CountInserted++;
            }
            await _db.SaveChangesAsync(ct);

            // Sync-State updaten
            var stateRes = "TIMEPUNCH";
            var st = await _db.EasyAtWorkSyncStates
                .FirstOrDefaultAsync(s => s.CompanyProfileId == req.CompanyProfileId && s.Resource == stateRes, ct);
            if (st == null)
            {
                st = new EasyAtWorkSyncState { CompanyProfileId = req.CompanyProfileId, Resource = stateRes };
                _db.EasyAtWorkSyncStates.Add(st);
            }
            st.LastSyncAt = DateTime.UtcNow;
            st.LastRowCount = res.CountInserted;
            st.LastError = null;
            var maxUpd = punches.Where(p => p.UpdatedAt.HasValue).Select(p => p.UpdatedAt!.Value).DefaultIfEmpty().Max();
            if (maxUpd != default) st.LastSeenUpdatedAt = maxUpd;
            await _db.SaveChangesAsync(ct);
        }

        return res;
    }
}
