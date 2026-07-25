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
///   • Schutz gilt NUR solange tatsächlich AU besteht — höchstens für die
///     Sperrfrist-Tage. Endet die erfasste AU früher, endet der Schutz mit
///     dem letzten AU-Tag (nicht erst am Maximaldatum).
///   • Der erste AU-Tag zählt als Sperrtag 1 (inklusiv). Beispiel BS:
///     Krankheit ab 04.01., 90. Sperrtag = 03.04., kündbar ab 04.04.
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
        /// "SPERRFRIST_ABGELAUFEN" – AU noch dokumentiert, max. Sperrfrist vorbei → Kündigung möglich.
        /// "KUENDIGUNG_MOEGLICH" – AU kürzlich beendet, Sperrfrist schon abgelaufen → Kündigung möglich (ToDo).
        /// "AU_ENDE_UNBESTAETIGT" – dokumentierte AU endete kürzlich, theoretische Sperrfrist läuft noch.
        /// </summary>
        string    Status,
        string    StatusText,
        string?   Hinweis,

        DateOnly? EntryDate,
        int?      DienstjahrAmStichtag,
        DateOnly? ProbezeitEndDate,

        // AU-Kontext
        DateOnly? AuBeginn,
        DateOnly? AuEnde,               // dokumentiertes Ende der AU-Kette
        string?   AuGrund,              // "KRANK", "UNFALL", "KRANK+UNFALL", "MUTTERSCHAFT"
        int?      AuDauerTage,          // Sperrtag-Nr. am Stichtag (1-basiert inkl.)

        // Sperrfrist
        int?      SperrfristTage,       // 30 / 90 / 180
        int?      SperrfristTageHoechstenfalls,  // wenn Dienstjahr-Übergang
        /// <summary>Maximaler letzter Sperrtag bei fortdauernder AU (inklusiv).</summary>
        DateOnly? SperrfristEnde,
        /// <summary>Aktuell geschützt bis = min(AuEnde, SperrfristEnde).</summary>
        DateOnly? AktuellGeschuetztBis,
        DateOnly? KuendigungAbDatum,    // früheste Kündigung bei max. Sperrfrist = SperrfristEnde + 1
        int?      VerbleibendeTage      // Tage bis KuendigungAbDatum (Maximal-Fall)
    );

    // ── Public API ─────────────────────────────────────────────────────────

    public async Task<SperrfristInfo> ComputeAsync(int employeeId, DateOnly stichtag)
    {
        var employee = await _db.Employees.FindAsync(employeeId);
        if (employee is null || !employee.EntryDate.HasValue)
        {
            return Empty("KEIN_EINTRITT",
                "Kein Eintrittsdatum hinterlegt — Sperrfrist nicht berechenbar.");
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
                AuEnde:                null,
                AuGrund:               null,
                AuDauerTage:           null,
                SperrfristTage:        null,
                SperrfristTageHoechstenfalls: null,
                SperrfristEnde:        null,
                AktuellGeschuetztBis:  null,
                KuendigungAbDatum:     null,
                VerbleibendeTage:      null);
        }

        // ── Mutterschaft (Walter 10.06.2026): aktive Schwangerschaft greift
        //    nach OR Art. 336c Abs. 1 Bst. c als eigene Sperrfrist und ist
        //    UNABHÄNGIG von Krankheit/Unfall. Endet 16 Wochen nach Geburt
        //    (KUENDIG_SCHUTZ-Regel im Regelwerk).
        var pregnancy = await _db.EmployeePregnancies
            .Where(p => p.EmployeeId == employeeId && p.IsActive)
            .OrderByDescending(p => p.ErrechneterTermin)
            .FirstOrDefaultAsync();
        if (pregnancy != null)
        {
            // Walter-Vorgabe 16.07.2026: der Schutz gilt AB BEGINN der
            // Schwangerschaft (errechneter Termin − 280 Tage) BIS 16 Wochen
            // nach der Niederkunft (effektives Geburtsdatum; solange keines
            // erfasst ist, der errechnete Termin als Basis).
            var schutzBeginn = PregnancyFristCalculator.SchwangerschaftsBeginn(pregnancy);
            var schutzEnde   = PregnancyFristCalculator.KuendigungsschutzEnde(pregnancy);
            if (stichtag >= schutzBeginn && stichtag <= schutzEnde)
            {
                int verbleibendMts  = Math.Max(0, schutzEnde.DayNumber - stichtag.DayNumber);
                var kuendigungAbMts = schutzEnde.AddDays(1);
                return new SperrfristInfo(
                    Status:               "GESCHUETZT",
                    StatusText:           $"Kündigungsschutz wegen Schwangerschaft/Mutterschaft (OR Art. 336c Abs. 1 Bst. c) — frühestens am {kuendigungAbMts:dd.MM.yyyy} kündbar.",
                    Hinweis:              pregnancy.Geburtsdatum.HasValue
                        ? $"Schutz ab Beginn der Schwangerschaft ({schutzBeginn:dd.MM.yyyy}, ET − 280 Tage) bis 16 Wochen nach Geburt ({pregnancy.Geburtsdatum.Value:dd.MM.yyyy})."
                        : $"Schutz ab Beginn der Schwangerschaft ({schutzBeginn:dd.MM.yyyy}, ET − 280 Tage) bis 16 Wochen nach errechnetem Termin ({pregnancy.ErrechneterTermin:dd.MM.yyyy}) — wird mit dem effektiven Geburtsdatum aktualisiert.",
                    EntryDate:            entryDate,
                    DienstjahrAmStichtag: dienstjahr,
                    ProbezeitEndDate:     probezeitEnde,
                    AuBeginn:             schutzBeginn,
                    AuEnde:               schutzEnde,
                    AuGrund:              "MUTTERSCHAFT",
                    AuDauerTage:          stichtag.DayNumber - schutzBeginn.DayNumber + 1,
                    SperrfristTage:       schutzEnde.DayNumber - schutzBeginn.DayNumber + 1,
                    SperrfristTageHoechstenfalls: null,
                    SperrfristEnde:       schutzEnde,
                    AktuellGeschuetztBis: schutzEnde,
                    KuendigungAbDatum:    kuendigungAbMts,
                    VerbleibendeTage:     Math.Max(0, kuendigungAbMts.DayNumber - stichtag.DayNumber));
            }
        }

        // ── Aktuelle durchgängige AU-Kette finden ──────────────────────────
        var auKette = await FindeAuKetteAsync(employeeId, stichtag);
        if (auKette is null)
        {
            // Review-Fix 22.07.2026 (Art. 336c, konservativ): endete die
            // dokumentierte AU erst KUERZLICH und laeuft die theoretische
            // Sperrfrist (ab AU-Beginn) noch, ist unklar, ob die AU wirklich
            // vorbei ist (Zeugnis-Verlaengerung evtl. noch nicht erfasst).
            // Dann WEICHE Warnung statt «Kuendigung moeglich».
            var letzte = await FindeLetzteBeendeteAuAsync(employeeId, stichtag);
            if (letzte is not null)
            {
                int djLetzte = ComputeDienstjahr(entryDate, letzte.Beginn);
                int tageMax = SperrfristTageFuerDienstjahr(djLetzte);
                var sperrEndeMax = letzte.Beginn.AddDays(tageMax - 1);
                int djEnde = ComputeDienstjahr(entryDate, sperrEndeMax);
                if (djEnde > djLetzte)
                {
                    int hoeher = SperrfristTageFuerDienstjahr(djEnde);
                    if (hoeher > tageMax)
                    {
                        tageMax = hoeher;
                        sperrEndeMax = letzte.Beginn.AddDays(hoeher - 1);
                    }
                }
                int auDauer = letzte.Ende.DayNumber - letzte.Beginn.DayNumber + 1;
                int tageSeitAuEnde = stichtag.DayNumber - letzte.Ende.DayNumber;

                // Effektives Schutz-Ende = min(AU-Ende, max. Sperrfrist).
                // Endet die AU VOR dem max. Sperrfrist-Tag, endet der Schutz
                // mit dem letzten AU-Tag — danach normale Kündigungsfristen
                // (Walter 25.07.2026: kein «KÜNDBAR wegen Sperrfrist» bei
                // gesunden MA mit kurzer abgeschlossener Krankheit).
                var schutzEndeEffektiv = letzte.Ende < sperrEndeMax ? letzte.Ende : sperrEndeMax;
                bool sperrfristWaehrendAuAusgeschoepft = letzte.Ende >= sperrEndeMax;

                // Weiche Warnung nur wenn AU-Ende SEHR kürzlich und die
                // maximale Sperrfrist bei fortdauernder AU noch liefe
                // (Zeugnis-Verlängerung evtl. noch nicht erfasst).
                if (tageSeitAuEnde <= 14 && stichtag <= sperrEndeMax && !sperrfristWaehrendAuAusgeschoepft)
                {
                    var kuendAbTheo = sperrEndeMax.AddDays(1);
                    return new SperrfristInfo(
                        Status:                "AU_ENDE_UNBESTAETIGT",
                        StatusText:            $"Die dokumentierte Arbeitsunfähigkeit endete am {letzte.Ende:dd.MM.yyyy}. Dauert die AU tatsächlich noch an (z.B. Zeugnis-Verlängerung noch nicht erfasst), läuft die Sperrfrist bis {sperrEndeMax:dd.MM.yyyy} — vor einer Kündigung das AU-Ende ärztlich bestätigen lassen (Art. 336c OR).",
                        Hinweis:               "Weiche Warnung — blockiert nicht. Bei bestätigtem AU-Ende gelten normale Kündigungsfristen.",
                        EntryDate:             entryDate,
                        DienstjahrAmStichtag:  dienstjahr,
                        ProbezeitEndDate:      probezeitEnde,
                        AuBeginn:              letzte.Beginn,
                        AuEnde:                letzte.Ende,
                        AuGrund:               letzte.Grund,
                        AuDauerTage:           auDauer,
                        SperrfristTage:        tageMax,
                        SperrfristTageHoechstenfalls: null,
                        SperrfristEnde:        sperrEndeMax,
                        AktuellGeschuetztBis:  null,
                        KuendigungAbDatum:     kuendAbTheo,
                        VerbleibendeTage:      Math.Max(0, kuendAbTheo.DayNumber - stichtag.DayNumber));
                }

                // Langzeit-AU: Sperrfrist wurde WÄHREND der Krankheit
                // ausgeschöpft, AU kürzlich beendet → spezieller Hinweis/ToDo.
                if (sperrfristWaehrendAuAusgeschoepft && tageSeitAuEnde <= 90)
                {
                    var kuendAb = sperrEndeMax.AddDays(1);
                    return new SperrfristInfo(
                        Status:                "KUENDIGUNG_MOEGLICH",
                        StatusText:            $"Kündigung jetzt möglich — Sperrfrist ({tageMax} Tage) endete am {sperrEndeMax:dd.MM.yyyy} während der AU. Letzte durchgehende AU {letzte.Beginn:dd.MM.yyyy} – {letzte.Ende:dd.MM.yyyy}.",
                        Hinweis:               "Ordentliche Kündigung ist zulässig (Art. 336c OR). Prüfen, ob die AU wirklich beendet ist (Zeugnis).",
                        EntryDate:             entryDate,
                        DienstjahrAmStichtag:  dienstjahr,
                        ProbezeitEndDate:      probezeitEnde,
                        AuBeginn:              letzte.Beginn,
                        AuEnde:                letzte.Ende,
                        AuGrund:               letzte.Grund,
                        AuDauerTage:           auDauer,
                        SperrfristTage:        tageMax,
                        SperrfristTageHoechstenfalls: null,
                        SperrfristEnde:        sperrEndeMax,
                        AktuellGeschuetztBis:  null,
                        KuendigungAbDatum:     kuendAb,
                        VerbleibendeTage:      0);
                }

                // Gesunder MA (kurze AU vorbei oder Langzeit-AU lange her):
                // kein Sperrfrist-Sonderstatus — normale Kündigungsfristen.
                return new SperrfristInfo(
                    Status:                "KEINE_AU",
                    StatusText:            "Kein Kündigungsschutz aktiv — am Stichtag keine Arbeitsunfähigkeit. Ordentliche Kündigung mit normalen Fristen (L-GAV) möglich.",
                    Hinweis:               letzte.Ende < sperrEndeMax
                        ? $"Letzte AU {letzte.Beginn:dd.MM.yyyy} – {letzte.Ende:dd.MM.yyyy} ({auDauer} Tage): Schutz endete mit dem letzten AU-Tag ({schutzEndeEffektiv:dd.MM.yyyy})."
                        : $"Letzte AU {letzte.Beginn:dd.MM.yyyy} – {letzte.Ende:dd.MM.yyyy}: Sperrfrist war während der AU am {sperrEndeMax:dd.MM.yyyy} ausgeschöpft.",
                    EntryDate:             entryDate,
                    DienstjahrAmStichtag:  dienstjahr,
                    ProbezeitEndDate:      probezeitEnde,
                    AuBeginn:              null,
                    AuEnde:                null,
                    AuGrund:               null,
                    AuDauerTage:           null,
                    SperrfristTage:        null,
                    SperrfristTageHoechstenfalls: null,
                    SperrfristEnde:        null,
                    AktuellGeschuetztBis:  null,
                    KuendigungAbDatum:     null,
                    VerbleibendeTage:      null);
            }

            return new SperrfristInfo(
                Status:                "KEINE_AU",
                StatusText:            "Kein Kündigungsschutz aktiv — am Stichtag keine Arbeitsunfähigkeit. Ordentliche Kündigung mit normalen Fristen (L-GAV) möglich.",
                Hinweis:               null,
                EntryDate:             entryDate,
                DienstjahrAmStichtag:  dienstjahr,
                ProbezeitEndDate:      probezeitEnde,
                AuBeginn:              null,
                AuEnde:                null,
                AuGrund:               null,
                AuDauerTage:           null,
                SperrfristTage:        null,
                SperrfristTageHoechstenfalls: null,
                SperrfristEnde:        null,
                AktuellGeschuetztBis:  null,
                KuendigungAbDatum:     null,
                VerbleibendeTage:      null);
        }

        // ── Sperrfrist-Länge ermitteln ────────────────────────────────────
        // Primär: Dienstjahr am AU-Beginn. Wenn während der laufenden
        // Sperrfrist ins höhere Dienstjahr gewechselt wird, gilt ab diesem
        // Tag die längere Sperrfrist (PDF Ziff. 9.2), weitergezählt ab AU-
        // Beginn. Wir geben beide Werte aus — KuendigungAbDatum basiert auf
        // dem Maximum.
        //
        // Inklusiv-Zählung: 1. Sperrtag = AU-Beginn, n-ter = Beginn+(n-1).
        int dienstjahrBeiAu = ComputeDienstjahr(entryDate, auKette.Beginn);
        int sperrfristAmBeginn = SperrfristTageFuerDienstjahr(dienstjahrBeiAu);

        DateOnly sperrfristEnde = auKette.Beginn.AddDays(sperrfristAmBeginn - 1);

        int? sperrfristHoechstens = null;
        // Liegt der berechnete Sperrfrist-Endpunkt in einem höheren Dienstjahr?
        int dienstjahrAmEnde = ComputeDienstjahr(entryDate, sperrfristEnde);
        if (dienstjahrAmEnde > dienstjahrBeiAu)
        {
            int hoehere = SperrfristTageFuerDienstjahr(dienstjahrAmEnde);
            if (hoehere > sperrfristAmBeginn)
            {
                sperrfristHoechstens = hoehere;
                sperrfristEnde = auKette.Beginn.AddDays(hoehere - 1);
            }
        }

        int sperrTageMax = sperrfristHoechstens ?? sperrfristAmBeginn;
        var kuendigungAb = sperrfristEnde.AddDays(1); // Tag nach letztem Sperrtag
        int dauer        = stichtag.DayNumber - auKette.Beginn.DayNumber + 1;
        // Aktuell geschützt nur bis zum dokumentierten AU-Ende (höchstens Max).
        var aktuellBis   = auKette.Ende < sperrfristEnde ? auKette.Ende : sperrfristEnde;

        bool abgelaufen = stichtag > sperrfristEnde;

        string status     = abgelaufen ? "SPERRFRIST_ABGELAUFEN" : "GESCHUETZT";
        string statusText = abgelaufen
            ? $"Maximale Sperrfrist ist am {sperrfristEnde:dd.MM.yyyy} abgelaufen — Kündigung jetzt möglich (Art. 336c OR)."
            : $"Aktuell kündigungsgeschützt aufgrund Arbeitsunfähigkeit (ärztlich erfasst bis {auKette.Ende:dd.MM.yyyy}). Bei durchgehender AU maximale Sperrfrist bis {sperrfristEnde:dd.MM.yyyy} — Kündigung frühestens ab {kuendigungAb:dd.MM.yyyy}.";

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
            AuEnde:                auKette.Ende,
            AuGrund:               auKette.Grund,
            AuDauerTage:           dauer,
            SperrfristTage:        sperrTageMax,
            SperrfristTageHoechstenfalls: sperrfristHoechstens,
            SperrfristEnde:        sperrfristEnde,
            AktuellGeschuetztBis:  abgelaufen ? null : aktuellBis,
            KuendigungAbDatum:     kuendigungAb,
            VerbleibendeTage:      abgelaufen ? 0 : Math.Max(0, kuendigungAb.DayNumber - stichtag.DayNumber));
    }

    // ── Hilfsfunktionen ────────────────────────────────────────────────────

    private static SperrfristInfo Empty(string status, string text) => new(
        Status: status, StatusText: text, Hinweis: null,
        EntryDate: null, DienstjahrAmStichtag: null, ProbezeitEndDate: null,
        AuBeginn: null, AuEnde: null, AuGrund: null, AuDauerTage: null,
        SperrfristTage: null, SperrfristTageHoechstenfalls: null,
        SperrfristEnde: null, AktuellGeschuetztBis: null,
        KuendigungAbDatum: null, VerbleibendeTage: null);

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

    /// <summary>
    /// Letzte VOR dem Stichtag beendete AU-Kette (Bloecke wie in
    /// FindeAuKetteAsync zusammengefasst) — fuer die weiche Warnung
    /// «AU-Ende unbestaetigt» (Review-Fix 22.07.2026).
    /// </summary>
    private async Task<AuKette?> FindeLetzteBeendeteAuAsync(int employeeId, DateOnly stichtag)
    {
        var absenzen = await _db.Absences
            .Where(a => a.EmployeeId == employeeId
                     && (a.AbsenceType == "KRANK" || a.AbsenceType == "UNFALL"))
            .OrderBy(a => a.DateFrom)
            .ThenBy(a => a.DateTo)
            .ToListAsync();
        if (absenzen.Count == 0) return null;

        var bloecke = new List<(DateOnly Von, DateOnly Bis, HashSet<string> Typen)>();
        foreach (var a in absenzen)
        {
            if (bloecke.Count == 0 || a.DateFrom.DayNumber > bloecke[^1].Bis.DayNumber + 1)
                bloecke.Add((a.DateFrom, a.DateTo, new HashSet<string> { a.AbsenceType }));
            else
            {
                var last = bloecke[^1];
                var bis = a.DateTo > last.Bis ? a.DateTo : last.Bis;
                last.Typen.Add(a.AbsenceType);
                bloecke[^1] = (last.Von, bis, last.Typen);
            }
        }

        AuKette? result = null;
        foreach (var b in bloecke)
        {
            if (b.Bis < stichtag)
            {
                string grund = b.Typen.Count == 1 ? b.Typen.First() : "KRANK+UNFALL";
                result = new AuKette(b.Von, b.Bis, grund, b.Typen.Count > 1);
            }
        }
        return result;
    }
}
