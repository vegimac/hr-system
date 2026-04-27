using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Zentrale Logik für die Karenz bei Krankheit und Unfall:
///
///   • Karenzjahr-Berechnung pro MA anhand CompanyProfile.KarenzjahrBasis
///     (ARBEITSJAHR = ab Eintritt, rollend; KALENDERJAHR = 01.01.–31.12.).
///     Das Karenzjahr ist für beide Absenz-Typen identisch.
///
///   • Tag-für-Tag-Kumulation der Absenzen pro Karenzjahr. Jeder
///     Kalendertag zählt als 1 Karenz-Tag, unabhängig vom Ausfall-
///     Prozent der Absenz. Krankheit und Unfall werden SEPARAT kumuliert —
///     jeder Typ hat seine eigene Grenze (z.B. 14 Tage Krank, 2 Tage Unfall).
///
///   • Markierung des Tages an dem die Karenz-Grenze erreicht wurde
///     → Umschalt-Zeitpunkt von 88% auf 80% Lohnfortzahlung.
///
/// Wird vom History-Endpoint und vom PayrollController für die
/// Lohn-Berechnung verwendet.
/// </summary>
public class KarenzService
{
    private readonly AppDbContext _db;

    public KarenzService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Liefert die Tage-Max-Grenze pro Absenz-Typ aus dem CompanyProfile.
    /// </summary>
    private static decimal TageMaxFor(CompanyProfile profile, string absenceType)
        => absenceType switch
        {
            "UNFALL" => profile.KarenzTageMaxUnfall,
            _         => profile.KarenzTageMax,
        };

    // ── Datentypen ──────────────────────────────────────────────────────────

    public record KarenzjahrInfo(
        DateOnly  Von,
        DateOnly  Bis,
        decimal   TageMax,
        decimal   TageVerbraucht,
        DateOnly? GrenzErreichtAm
    );

    public record KrankheitInArbeitsjahr(
        int       AbsenceId,
        DateOnly  DateFrom,
        DateOnly  DateTo,
        decimal   Prozent,
        int       TageImJahr,             // markierte Tage, die in dieses Karenzjahr fallen
        decimal   KarenztageInDiesemJahr, // TageImJahr × Prozent/100
        decimal   KumuliertVor,
        decimal   KumuliertNach,
        string?   Notes
    );

    public record KarenzHistoryJahr(
        KarenzjahrInfo                 Info,
        List<KrankheitInArbeitsjahr>   Krankheiten
    );

    // ── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Berechnet das Karenzjahr (Von/Bis inkl.), in das ein bestimmtes Datum fällt.
    /// </summary>
    public (DateOnly Von, DateOnly Bis) ComputeKarenzjahr(
        DateOnly datum, Employee employee, CompanyProfile profile)
    {
        if ((profile.KarenzjahrBasis ?? "ARBEITSJAHR").Equals("KALENDERJAHR",
                StringComparison.OrdinalIgnoreCase))
        {
            return (new DateOnly(datum.Year, 1, 1),
                    new DateOnly(datum.Year, 12, 31));
        }

        // ARBEITSJAHR — Anker ist das Eintrittsdatum.
        if (!employee.EntryDate.HasValue)
        {
            // Ohne Eintrittsdatum fallen wir auf Kalenderjahr zurück.
            return (new DateOnly(datum.Year, 1, 1),
                    new DateOnly(datum.Year, 12, 31));
        }

        var hired = DateOnly.FromDateTime(employee.EntryDate.Value);

        int yd = datum.Year - hired.Year;
        var anniversaryInSameYear = SafeAnniversary(datum.Year, hired.Month, hired.Day);
        if (anniversaryInSameYear > datum) yd--;

        var von = SafeAnniversary(hired.Year + yd, hired.Month, hired.Day);
        var bis = SafeAnniversary(hired.Year + yd + 1, hired.Month, hired.Day).AddDays(-1);
        return (von, bis);
    }

    /// <summary>
    /// Liefert die Absenz-History (Krank oder Unfall) eines Mitarbeiters
    /// gruppiert nach Karenzjahren, inkl. kumulierten Karenztagen und
    /// Grenz-Datum. Default: Krankheit.
    /// </summary>
    public async Task<List<KarenzHistoryJahr>> GetHistoryAsync(
        int employeeId, int companyProfileId, string absenceType = "KRANK")
    {
        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee is null) return new();

        var profile = await _db.CompanyProfiles.FindAsync(companyProfileId);
        if (profile is null) return new();

        var krankAbsenzen = await _db.Absences
            .Where(a => a.EmployeeId == employeeId && a.AbsenceType == absenceType)
            .OrderBy(a => a.DateFrom)
            .ToListAsync();

        decimal tageMax = TageMaxFor(profile, absenceType);

        // Buckets: KarenzjahrVon → Liste von (Absence, markierte Tage im Jahr)
        var buckets = new Dictionary<DateOnly, List<(Absence abs, DateOnly[] days)>>();

        foreach (var a in krankAbsenzen)
        {
            // Karenz zählt IMMER auf Kalendertage — Krankheit/Unfall
            // laufen kalendarisch (Sa/So inklusive), unabhängig davon wann
            // der MA gearbeitet hätte. WorkedDays sind nur für die Stunden-
            // Zeitgutschrift relevant, nicht für Karenz/Lohnkürzung/KTG.
            var allDays = Enumerable.Range(0, a.DateTo.DayNumber - a.DateFrom.DayNumber + 1)
                .Select(i => a.DateFrom.AddDays(i))
                .ToArray();

            foreach (var grp in allDays.GroupBy(d => ComputeKarenzjahr(d, employee, profile).Von))
            {
                var daysSorted = grp.OrderBy(d => d).ToArray();
                if (!buckets.ContainsKey(grp.Key))
                    buckets[grp.Key] = new();
                buckets[grp.Key].Add((a, daysSorted));
            }
        }

        var result = new List<KarenzHistoryJahr>();
        foreach (var kv in buckets.OrderBy(x => x.Key))
        {
            // Von/Bis des Jahrs aus dem Dictionary-Key
            var (jahrVon, jahrBis) = ComputeKarenzjahr(kv.Key, employee, profile);

            decimal   kumuliert           = 0m;
            DateOnly? grenzErreichtAm     = null;
            var       krankheiten         = new List<KrankheitInArbeitsjahr>();

            // Absenzen chronologisch abarbeiten (nach DateFrom)
            foreach (var (abs, days) in kv.Value.OrderBy(x => x.abs.DateFrom))
            {
                // Jeder Kalendertag zählt als 1 Karenztag — unabhängig
                // vom Ausfall-Prozent. Ein halber Krank-Tag (50%) verbraucht
                // genauso einen ganzen Karenz-Tag wie ein voller (100%).
                int     tageImJahr  = days.Length;
                decimal karenztage  = tageImJahr;
                decimal kumVor      = kumuliert;

                // Falls Karenz-Grenze innerhalb dieser Absenz überschritten
                // wird: genauen Tag bestimmen.
                decimal kumLaufend = kumVor;
                foreach (var d in days)
                {
                    kumLaufend += 1m;
                    if (grenzErreichtAm is null && kumLaufend >= tageMax)
                    {
                        grenzErreichtAm = d;
                        break;
                    }
                }

                kumuliert = Math.Round(kumVor + karenztage, 2);

                krankheiten.Add(new KrankheitInArbeitsjahr(
                    AbsenceId:              abs.Id,
                    DateFrom:               abs.DateFrom,
                    DateTo:                 abs.DateTo,
                    Prozent:                abs.Prozent,
                    TageImJahr:             tageImJahr,
                    KarenztageInDiesemJahr: Math.Round(karenztage, 2),
                    KumuliertVor:           Math.Round(kumVor, 2),
                    KumuliertNach:          kumuliert,
                    Notes:                  abs.Notes
                ));
            }

            result.Add(new KarenzHistoryJahr(
                Info: new KarenzjahrInfo(
                    Von:                 jahrVon,
                    Bis:                 jahrBis,
                    TageMax:             tageMax,
                    TageVerbraucht:      kumuliert,
                    GrenzErreichtAm:     grenzErreichtAm),
                Krankheiten: krankheiten
            ));
        }

        // Absteigend sortieren — neueste Jahre zuerst (UI-freundlich).
        result.Sort((a, b) => b.Info.Von.CompareTo(a.Info.Von));
        return result;
    }

    /// <summary>
    /// Ermittelt die Karenz-Situation für ein Stichdatum (z. B. Lohnlauf-Start).
    /// Liefert Von/Bis des laufenden Karenzjahrs, bisher verbrauchte Tage,
    /// sowie das Grenz-Datum falls schon erreicht. Default-Absenz-Typ: Krankheit.
    /// </summary>
    public async Task<KarenzjahrInfo?> GetCurrentAsync(
        int employeeId, int companyProfileId, DateOnly datum, string absenceType = "KRANK")
    {
        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee is null) return null;
        var profile = await _db.CompanyProfiles.FindAsync(companyProfileId);
        if (profile is null) return null;

        var (von, bis) = ComputeKarenzjahr(datum, employee, profile);
        decimal tageMax = TageMaxFor(profile, absenceType);

        // Alle Absenzen des gewünschten Typs im Karenzjahr, chronologisch
        var absenzen = await _db.Absences
            .Where(a => a.EmployeeId == employeeId
                     && a.AbsenceType == absenceType
                     && a.DateFrom   <= bis
                     && a.DateTo     >= von)
            .OrderBy(a => a.DateFrom)
            .ToListAsync();

        decimal   kumuliert       = 0m;
        DateOnly? grenzErreichtAm = null;

        foreach (var a in absenzen)
        {
            // Kalendertage (nicht WorkedDays) — Krankheit/Unfall laufen
            // kalendarisch und zählen pro Tag als 1, unabhängig vom
            // Ausfall-Prozent. Siehe Doku im Klassen-Kommentar.
            var days = Enumerable.Range(0, a.DateTo.DayNumber - a.DateFrom.DayNumber + 1)
                .Select(i => a.DateFrom.AddDays(i))
                .ToArray();
            // Nur Tage im Karenzjahr
            var daysInYear = days.Where(d => d >= von && d <= bis).OrderBy(d => d).ToArray();

            foreach (var d in daysInYear)
            {
                kumuliert += 1m;
                if (grenzErreichtAm is null && kumuliert >= tageMax)
                    grenzErreichtAm = d;
            }
        }

        return new KarenzjahrInfo(
            Von:             von,
            Bis:             bis,
            TageMax:         tageMax,
            TageVerbraucht:  Math.Round(kumuliert, 2),
            GrenzErreichtAm: grenzErreichtAm);
    }

    // ── Monats-Breakdown für Lohnrechnung ──────────────────────────────────

    public record MonatsKrankTag(
        DateOnly Datum,
        decimal  Prozent,      // 1–100 (der Absenz am Tag)
        bool     InKarenz,     // true = noch innerhalb der Karenzgrenze → 88%
        bool     BvgAuf100,    // true = Tag liegt innerhalb der BVG-Wartefrist
                               //        (3 Monate ab AU-Beginn, monats-gerundet)
                               //        → BVG-Basis bleibt auf 100%-Lohn
        int      AbsenceId
    );

    /// <summary>
    /// Liefert für jeden Absenz-Tag (Krank oder Unfall) innerhalb eines
    /// Lohnperioden-Intervalls (periodFrom … periodTo, inkl.) Datum,
    /// Prozent-Gewichtung und ob er noch innerhalb der Karenzgrenze des
    /// zuständigen Karenzjahrs liegt (→ 88% Faktor) oder darüber (→ 80%).
    ///
    /// Die Kumulation erfolgt pro Absenz-Typ chronologisch über alle Tage
    /// des Karenzjahrs bis zum betreffenden Tag — frühere Lohnperioden
    /// fliessen also in die Bewertung ein, erscheinen aber nicht im
    /// Ergebnis. Krank- und Unfall-Kumulation laufen getrennt: z.B. 2 Tage
    /// Unfall verbrauchen NICHT die 14 Tage Krank.
    ///
    /// Wichtig: arbeitet mit Lohnperioden (z.B. 21.01.–20.02.), nicht mit
    /// Kalendermonaten. Der Aufrufer muss die echten Perioden-Grenzen
    /// liefern (im PayrollController aus CompanyProfile.PayrollPeriodStartDay).
    ///
    /// Default-Absenz-Typ: Krankheit. Für Unfall "UNFALL" übergeben.
    /// </summary>
    public async Task<List<MonatsKrankTag>> GetPeriodBreakdownAsync(
        int employeeId, int companyProfileId, DateOnly periodFrom, DateOnly periodTo,
        string absenceType = "KRANK")
    {
        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee is null) return new();
        var profile = await _db.CompanyProfiles.FindAsync(companyProfileId);
        if (profile is null) return new();

        decimal tageMax = TageMaxFor(profile, absenceType);

        // Alle Absenzen des MA (des gewünschten Typs) holen. Filter auf
        // Periode kann nicht vorgezogen werden — wir brauchen frühere Tage
        // fürs Karenzjahr-Kumulation. Erst beim Ergebnis-Schreiben wird auf
        // Periode gefiltert.
        var alleAbsenzen = await _db.Absences
            .Where(a => a.EmployeeId == employeeId && a.AbsenceType == absenceType)
            .OrderBy(a => a.DateFrom)
            .ToListAsync();

        // Alle Tage chronologisch, inkl. Absence + Prozent.
        // Kalendertage (nicht WorkedDays) — Krankheit/Unfall laufen
        // kalendarisch. WorkedDays sind nur für die Stunden-Zeitgutschrift
        // gedacht; Lohnkürzung und KTG-Entschädigung zählen jeden Tag.
        var allKrankTage = new List<(DateOnly datum, Absence abs, decimal prozent)>();
        foreach (var a in alleAbsenzen)
        {
            var days = Enumerable.Range(0, a.DateTo.DayNumber - a.DateFrom.DayNumber + 1)
                .Select(i => a.DateFrom.AddDays(i))
                .ToArray();
            foreach (var d in days) allKrankTage.Add((d, a, a.Prozent));
        }
        allKrankTage.Sort((x, y) => x.datum.CompareTo(y.datum));

        // ── BVG-Wartefrist-Fenster pro AU-Kette bestimmen ──────────────────
        // Eine AU-Kette = lückenlose Folge von Absenz-Records desselben
        // Typs, deren Kalender-Bereiche [DateFrom..DateTo] nahtlos (oder
        // überlappend) aneinander anschliessen. Wichtig: wir nehmen hier
        // NICHT die WorkedDays-Liste, sondern die vollen Kalender-Ranges —
        // sonst würden Wochenenden fälschlich als Kettenbruch zählen, weil
        // WorkedDays typischerweise nur Mo–Fr enthält. Die AU läuft aber
        // auch am Wochenende weiter.
        //
        // Der User kann Krankheit also beliebig stückeln (z.B. monatlich
        // neu erfassen weil die Dauer am Anfang unbekannt ist) — solange
        // die Date-Ranges aneinander anschliessen, gilt es als eine AU-
        // Kette mit einer Wartefrist.
        //
        // Wartefrist-Start: wenn AU-Beginn am 1.–15. → 1. dieses Monats;
        //                   wenn AU-Beginn ab 16.    → 1. des Folgemonats.
        // Wartefrist-Ende:  letzter Tag des (Start + BvgWartefristMonate − 1) Monats.
        int wartefristMon = Math.Max(0, profile.BvgWartefristMonate);
        // Liste merged Ranges + ihr Wartefrist-Fenster
        var ketten = new List<(DateOnly Von, DateOnly Bis, DateOnly WfVon, DateOnly WfBis)>();
        if (wartefristMon > 0 && alleAbsenzen.Count > 0)
        {
            // Ranges chronologisch, zusammenhängende zusammenfügen
            var sorted = alleAbsenzen
                .Select(a => (Von: a.DateFrom, Bis: a.DateTo))
                .OrderBy(r => r.Von).ThenBy(r => r.Bis)
                .ToList();

            DateOnly curVon = sorted[0].Von;
            DateOnly curBis = sorted[0].Bis;
            for (int i = 1; i < sorted.Count; i++)
            {
                var r = sorted[i];
                if (r.Von.DayNumber <= curBis.DayNumber + 1)
                {
                    // nahtlos oder überlappend → Kette verlängern
                    if (r.Bis > curBis) curBis = r.Bis;
                }
                else
                {
                    // Lücke → Kette abschliessen, neue beginnen
                    ketten.Add(BuildKette(curVon, curBis, wartefristMon));
                    curVon = r.Von;
                    curBis = r.Bis;
                }
            }
            ketten.Add(BuildKette(curVon, curBis, wartefristMon));
        }

        static (DateOnly Von, DateOnly Bis, DateOnly WfVon, DateOnly WfBis) BuildKette(
            DateOnly von, DateOnly bis, int wartefristMon)
        {
            var wfVon = von.Day <= 15
                ? new DateOnly(von.Year, von.Month, 1)
                : new DateOnly(von.Year, von.Month, 1).AddMonths(1);
            var endeMonat = wfVon.AddMonths(wartefristMon - 1);
            var wfBis = new DateOnly(endeMonat.Year, endeMonat.Month,
                                     DateTime.DaysInMonth(endeMonat.Year, endeMonat.Month));
            return (von, bis, wfVon, wfBis);
        }

        // Schneller Lookup: für einen AU-Tag die passende Kette finden
        (DateOnly WfVon, DateOnly WfBis)? FindWartefrist(DateOnly d)
        {
            foreach (var k in ketten)
                if (d >= k.Von && d <= k.Bis) return (k.WfVon, k.WfBis);
            return null;
        }

        // Chronologische Kumulation pro Karenzjahr bis zum jeweiligen Tag.
        var kumPerKarenzjahr = new Dictionary<DateOnly, decimal>();

        var result = new List<MonatsKrankTag>();
        foreach (var (d, abs, proz) in allKrankTage)
        {
            var (kjVon, _) = ComputeKarenzjahr(d, employee, profile);
            if (!kumPerKarenzjahr.ContainsKey(kjVon))
                kumPerKarenzjahr[kjVon] = 0m;

            decimal kumVor = kumPerKarenzjahr[kjVon];
            // Jeder Kalendertag zählt als 1 Karenztag — Ausfall-Prozent
            // wirkt NICHT auf die Karenz-Zählung (ein 50%-Krank-Tag
            // verbraucht einen ganzen Karenz-Tag, wie ein 100%-Tag).
            decimal anteil = 1m;
            kumPerKarenzjahr[kjVon] = kumVor + anteil;

            // Nur Tage innerhalb der Lohnperiode ins Ergebnis.
            if (d >= periodFrom && d <= periodTo)
            {
                // "InKarenz" = der ganze Tag fällt noch unter die Karenz-Grenze
                // (z.B. 14 Tage Krank / 2 Tage Unfall → 88%-Phase). Ein
                // halbtägiger Split (Grenze mitten im Tag) ist buchhalterisch
                // exakter, aber in der Praxis selten relevant — ein 50%-Tag
                // wird sowieso schon als 0.5 Karenztag gezählt.
                bool inKarenz = kumVor < tageMax;

                // "BvgAuf100" = Tag liegt innerhalb der 3-Monate-Wartefrist
                // der AU-Kette (monats-gerundet). BVG-Basis bleibt auf
                // 100%-Lohn, auch wenn effektiv nur 88%/80% ausbezahlt werden.
                // Nach Wartefrist: Phase 1-Default = BVG läuft auf tatsächlichem
                // Lohn weiter (Beitragsbefreiung nach AU-Grad wird erst in
                // Phase 2 umgesetzt).
                var wf = FindWartefrist(d);
                bool bvgAuf100 = wf.HasValue && d >= wf.Value.WfVon && d <= wf.Value.WfBis;

                result.Add(new MonatsKrankTag(d, proz, inKarenz, bvgAuf100, abs.Id));
            }
        }

        return result;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static DateOnly SafeAnniversary(int year, int month, int day)
    {
        // 29.02.-Geburts-/Eintrittsdaten → in Nicht-Schaltjahren auf 28.02.
        int maxDay = DateTime.DaysInMonth(year, month);
        return new DateOnly(year, month, Math.Min(day, maxDay));
    }

    private static DateOnly[] ParseWorkedDays(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Array.Empty<DateOnly>();
        try
        {
            var arr = JsonSerializer.Deserialize<string[]>(json);
            if (arr is null) return Array.Empty<DateOnly>();
            return arr
                .Select(s => DateOnly.TryParse(s, out var d) ? d : (DateOnly?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToArray();
        }
        catch
        {
            return Array.Empty<DateOnly>();
        }
    }
}
