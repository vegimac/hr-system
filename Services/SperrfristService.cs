using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Berechnet den Kündigungsschutz (Sperrfrist) nach Art. 336c OR für einen
/// Mitarbeiter zu einem Stichtag. Die Sperrfrist greift bei unverschuldeter
/// Arbeitsunfähigkeit (Krankheit, Unfall) NACH Ablauf der Probezeit:
///
///   Dienstjahr 1            (Monat 1-12)   → 30 Tage Sperrfrist
///   Dienstjahr 2 bis 5      (Monat 13-60)  → 90 Tage Sperrfrist
///   Ab Dienstjahr 6         (ab Monat 61)  → 180 Tage Sperrfrist
///
///   (Werte gemäss GastroSuisse-Merkblatt / Art. 336c Abs. 1 Bst. b OR)
///
/// Kernregeln:
///   • Sperrfrist läuft ab BEGINN der durchgängigen Arbeitsunfähigkeit.
///   • Teilarbeitsunfähigkeit (z.B. 50%) zählt voll als Sperrfristtag.
///   • Kalendertage, nicht Arbeitstage.
///   • Bei NEUEM Grund (z.B. erst Krank, dann Unfall) → neue Sperrfrist.
///     Bei gleichem Grund / Rückfall → keine neue Sperrfrist.
///   • Wechselt der MA während der laufenden Sperrfrist in ein höheres
///     Dienstjahr, gilt die LÄNGERE Sperrfrist, aber weitergezählt ab
///     Beginn der Arbeitsunfähigkeit (nicht neu bei 0).
///
/// Nicht automatisch erkannt (MVP):
///   • Probezeit-Verlängerung wegen AU während Probezeit → wir nutzen
///     Employment.ProbationEndDate bzw. Eintritt + ProbationPeriodMonths
///     als Ist-Zustand; der AG ist dafür zuständig, dieses Datum bei AU
///     während Probezeit manuell nachzuziehen.
///   • Rückfall vs. neuer Grund → wir kennzeichnen unterschiedliche
///     Absenz-Typen in derselben AU-Kette optisch als Hinweis, übernehmen
///     aber den frühesten Beginn (konservativ zugunsten des MA).
/// </summary>
public class SperrfristService
{
    private readonly AppDbContext _db;

    public SperrfristService(AppDbContext db)
    {
        _db = db;
    }

    // ── DTOs ───────────────────────────────────────────────────────────────

    public record SperrfristInfo(
        /// <summary>
        /// "IN_PROBEZEIT"  – Stichtag liegt in der Probezeit; keine Sperrfrist.
        /// "KEIN_EINTRITT" – Employee hat kein Eintrittsdatum hinterlegt.
        /// "KEIN_EMPLOYMENT" – keine aktive Anstellung gefunden.
        /// "KEINE_AU"      – Stichtag: kein durchgehender AU-Block aktiv.
        /// "GESCHUETZT"    – aktuell Sperrfrist läuft noch; Kündigung unzulässig.
        /// "SPERRFRIST_ABGELAUFEN" – Sperrfrist ist vorbei, Kündigung möglich.
        /// </summary>
        string    Status,
        string    StatusText,
        string?   Hinweis,

        DateOnly? EntryDate,
        int?      DienstjahrAmStichtag,
        DateOnly? ProbezeitEndDate,

        // AU-Kontext
        DateOnly? AuBeginn,
        string?   AuGrund,              // "KRANK", "UNFALL", "KRANK+UNFALL"
        int?      AuDauerTage,

        // Sperrfrist
        int?      SperrfristTage,       // 30 / 90 / 180
        int?      SperrfristTageHoechstenfalls,  // wenn Dienstjahr-Übergang
        DateOnly? SperrfristEnde,
        DateOnly? KuendigungAbDatum,    // SperrfristEnde + 1 Tag
        int?      VerbleibendeTage      // Tage bis Kündigung wieder möglich
    );

    // ── Public API ─────────────────────────────────────────────────────────

    public async Task<SperrfristInfo> ComputeAsync(int employeeId, DateOnly stichtag)
    {
        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee is null || !employee.EntryDate.HasValue)
        {
            return new SperrfristInfo(
                Status:                "KEIN_EINTRITT",
                StatusText:            "Kein Eintrittsdatum hinterlegt — Sperrfrist nicht berechenbar.",
                Hinweis:               null,
                EntryDate:             null,
                DienstjahrAmStichtag:  null,
                ProbezeitEndDate:      null,
                AuBeginn:              null,
                AuGrund:               null,
                AuDauerTage:           null,
                SperrfristTage:        null,
                SperrfristTageHoechstenfalls: null,
                SperrfristEnde:        null,
                KuendigungAbDatum:     null,
                VerbleibendeTage:      null);
        }

        var entryDate = DateOnly.FromDateTime(employee.EntryDate.Value);

        // Aktives Employment holen (für Probezeit-Info)
        var employment = await _db.Employments
            .Where(e => e.EmployeeId == employeeId && e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefaultAsync();

        DateOnly? probezeitEnde = null;
        if (employment is not null)
        {
            if (employment.ProbationEndDate.HasValue)
            {
                probezeitEnde = DateOnly.FromDateTime(employment.ProbationEndDate.Value);
            }
            else if (employment.ProbationPeriodMonths.HasValue && employment.ProbationPeriodMonths.Value > 0)
            {
                probezeitEnde = entryDate.AddMonths(employment.ProbationPeriodMonths.Value);
            }
        }

        int dienstjahr = ComputeDienstjahr(entryDate, stichtag);

        // ── In Probezeit? ──────────────────────────────────────────────────
        if (probezeitEnde.HasValue && stichtag <= probezeitEnde.Value)
        {
            return new SperrfristInfo(
                Status:                "IN_PROBEZEIT",
                StatusText:            $"Probezeit läuft bis {probezeitEnde.Value:dd.MM.yyyy} — Kündigung jederzeit möglich (auch bei Krankheit/Unfall).",
                Hinweis:               "Während der Probezeit greifen die Sperrfristen nach Art. 336c OR noch nicht.",
                EntryDate:             entryDate,
                DienstjahrAmStichtag:  dienstjahr,
                ProbezeitEndDate:      probezeitEnde,
                AuBeginn:              null,
                AuGrund:               null,
                AuDauerTage:           null,
                SperrfristTage:        null,
                SperrfristTageHoechstenfalls: null,
                SperrfristEnde:        null,
                KuendigungAbDatum:     null,
                VerbleibendeTage:      null);
        }

        // ── Aktuelle durchgängige AU-Kette finden ──────────────────────────
        var auKette = await FindeAuKetteAsync(employeeId, stichtag);
        if (auKette is null)
        {
            return new SperrfristInfo(
                Status:                "KEINE_AU",
                StatusText:            "Kein aktiver Kündigungsschutz — der Mitarbeiter ist am Stichtag nicht arbeitsunfähig. Ordentliche Kündigung möglich.",
                Hinweis:               null,
                EntryDate:             entryDate,
                DienstjahrAmStichtag:  dienstjahr,
                ProbezeitEndDate:      probezeitEnde,
                AuBeginn:              null,
                AuGrund:               null,
                AuDauerTage:           null,
                SperrfristTage:        null,
                SperrfristTageHoechstenfalls: null,
                SperrfristEnde:        null,
                KuendigungAbDatum:     null,
                VerbleibendeTage:      null);
        }

        // ── Sperrfrist-Länge ermitteln ────────────────────────────────────
        // Primär: Dienstjahr am AU-Beginn. Wenn während der laufenden
        // Sperrfrist ins höhere Dienstjahr gewechselt wird, gilt ab diesem
        // Tag die längere Sperrfrist (PDF Ziff. 9.2), weitergezählt ab AU-
        // Beginn. Wir geben beide Werte aus — KuendigungAbDatum basiert auf
        // dem Maximum.
        int dienstjahrBeiAu = ComputeDienstjahr(entryDate, auKette.Beginn);
        int sperrfristAmBeginn = SperrfristTageFuerDienstjahr(dienstjahrBeiAu);

        DateOnly sperrfristEnde = auKette.Beginn.AddDays(sperrfristAmBeginn);

        int? sperrfristHoechstens = null;
        // Liegt der berechnete Sperrfrist-Endpunkt in einem höheren Dienstjahr?
        int dienstjahrAmEnde = ComputeDienstjahr(entryDate, sperrfristEnde);
        if (dienstjahrAmEnde > dienstjahrBeiAu)
        {
            int hoehere = SperrfristTageFuerDienstjahr(dienstjahrAmEnde);
            if (hoehere > sperrfristAmBeginn)
            {
                sperrfristHoechstens = hoehere;
                sperrfristEnde = auKette.Beginn.AddDays(hoehere);
            }
        }

        var kuendigungAb = sperrfristEnde.AddDays(1);
        int verbleibend  = Math.Max(0, sperrfristEnde.DayNumber - stichtag.DayNumber);
        int dauer        = stichtag.DayNumber - auKette.Beginn.DayNumber + 1;

        bool abgelaufen = stichtag > sperrfristEnde;

        string status     = abgelaufen ? "SPERRFRIST_ABGELAUFEN" : "GESCHUETZT";
        string statusText = abgelaufen
            ? $"Sperrfrist ist am {sperrfristEnde:dd.MM.yyyy} abgelaufen — Kündigung jetzt möglich (Art. 336c OR)."
            : $"Kündigungsschutz aktiv — frühestens am {kuendigungAb:dd.MM.yyyy} kündbar ({verbleibend} Tag{(verbleibend == 1 ? "" : "e")} verbleibend).";

        string? hinweis = null;
        if (auKette.GruendeGemischt)
        {
            hinweis = "AU-Kette enthält mehrere Gründe (Krank + Unfall). Bei NEUEM Grund löst das eine eigene Sperrfrist aus; bei Rückfall/Folge nicht. Im Zweifel manuell prüfen — konservativ wird hier der früheste Beginn angenommen.";
        }
        else if (sperrfristHoechstens.HasValue)
        {
            hinweis = $"Dienstjahr-Übergang während der Sperrfrist erkannt: längere Sperrfrist von {sperrfristHoechstens.Value} Tagen greift (PDF Ziff. 9.2).";
        }

        return new SperrfristInfo(
            Status:                status,
            StatusText:            statusText,
            Hinweis:               hinweis,
            EntryDate:             entryDate,
            DienstjahrAmStichtag:  dienstjahr,
            ProbezeitEndDate:      probezeitEnde,
            AuBeginn:              auKette.Beginn,
            AuGrund:               auKette.Grund,
            AuDauerTage:           dauer,
            SperrfristTage:        sperrfristHoechstens ?? sperrfristAmBeginn,
            SperrfristTageHoechstenfalls: sperrfristHoechstens,
            SperrfristEnde:        sperrfristEnde,
            KuendigungAbDatum:     kuendigungAb,
            VerbleibendeTage:      abgelaufen ? 0 : verbleibend);
    }

    // ── Hilfsfunktionen ────────────────────────────────────────────────────

    /// <summary>
    /// Dienstjahr am gegebenen Datum (1-basiert). Volle 12 Monate seit
    /// Eintritt ergeben einen Dienstjahres-Wechsel. Entspricht Monaten /
    /// 12 + 1 nach Merkblatt-Logik (Monat 1-12 = DJ 1, Monat 13-24 = DJ 2).
    /// </summary>
    private static int ComputeDienstjahr(DateOnly entryDate, DateOnly datum)
    {
        if (datum < entryDate) return 1;
        int monate = (datum.Year - entryDate.Year) * 12 + (datum.Month - entryDate.Month);
        if (datum.Day < entryDate.Day) monate--;
        if (monate < 0) monate = 0;
        return (monate / 12) + 1;
    }

    private static int SperrfristTageFuerDienstjahr(int dienstjahr) => dienstjahr switch
    {
        <= 1 => 30,           // 1. Dienstjahr
        <= 5 => 90,           // 2. bis 5. Dienstjahr
        _    => 180,          // ab 6. Dienstjahr
    };

    private record AuKette(DateOnly Beginn, DateOnly Ende, string Grund, bool GruendeGemischt);

    /// <summary>
    /// Sucht die längste durchgängige Arbeitsunfähigkeits-Kette (KRANK/UNFALL)
    /// die am Stichtag aktiv ist. "Durchgängig" heißt: keine AU-freien
    /// Kalendertage dazwischen — eine Absenz endet, die nächste beginnt am
    /// selben oder am folgenden Tag.
    /// </summary>
    private async Task<AuKette?> FindeAuKetteAsync(int employeeId, DateOnly stichtag)
    {
        // Alle Krank/Unfall-Absenzen chronologisch
        var absenzen = await _db.Absences
            .Where(a => a.EmployeeId == employeeId
                     && (a.AbsenceType == "KRANK" || a.AbsenceType == "UNFALL"))
            .OrderBy(a => a.DateFrom)
            .ThenBy(a => a.DateTo)
            .ToListAsync();

        if (absenzen.Count == 0) return null;

        // Absenzen zu durchgehenden Blöcken zusammenfassen
        var bloecke = new List<(DateOnly Von, DateOnly Bis, HashSet<string> Typen)>();
        foreach (var a in absenzen)
        {
            if (bloecke.Count == 0 || a.DateFrom.DayNumber > bloecke[^1].Bis.DayNumber + 1)
            {
                bloecke.Add((a.DateFrom, a.DateTo, new HashSet<string> { a.AbsenceType }));
            }
            else
            {
                var last = bloecke[^1];
                var bis  = a.DateTo > last.Bis ? a.DateTo : last.Bis;
                last.Typen.Add(a.AbsenceType);
                bloecke[^1] = (last.Von, bis, last.Typen);
            }
        }

        // Block suchen der den Stichtag enthält
        foreach (var b in bloecke)
        {
            if (b.Von <= stichtag && stichtag <= b.Bis)
            {
                string grund = b.Typen.Count == 1
                    ? b.Typen.First()
                    : "KRANK+UNFALL";
                return new AuKette(b.Von, b.Bis, grund, b.Typen.Count > 1);
            }
        }

        return null;
    }
}
