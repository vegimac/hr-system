using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Akonto-Lohn — Berechnung der Vorauszahlung pro MA nach Walter's 6-Regel-Werk
/// (Walter-Vorgabe 16.05.2026, Etappe 5):
///
///   Regel 1: kein Akonto wenn Vertragsende ≤ Periodenende
///   Regel 2: kein Akonto bei Krankheit/Unfall/Mutterschaft AM Stichtag
///   Regel 3: FIX     → AkontoProzentFix × Definitiv-Auszahlung
///   Regel 4: FIX-M   → wie Regel 3
///   Regel 5: MTP     → AkontoProzentHourly × (Stunden × Rate + Ferien-Pott − SV-Abzüge)
///   Regel 6: UTP     → wie Regel 5
///
/// Ferien-Pott (Regel 5/6, Walter 16.05.2026): nur Ferien-Bezüge die BIS zum
/// Akonto-Stichtag vollständig abgeschlossen sind (DateTo ≤ Stichtag) werden
/// anteilsmässig aus dem Pott ausbezahlt — siehe <see cref="FerienAuszahlungService"/>.
/// Ferien-Perioden die über den Stichtag hinausragen werden komplett ignoriert
/// und im Definitivlauf am Monatsende nachverrechnet.
///
/// FIX/FIX-M (Regel 3/4) werden in dieser Service nur grob geschätzt (Monatslohn
/// als Brutto-Proxy) — die exakte Korrektur erfolgt im Frontend via
/// <c>POST /api/akonto/workflow/sync-fix-from-slip</c>, das den Wert auf Basis
/// des echten Definitivlauf-Slips überschreibt.
///
/// Der Service ist read-only — schreibt nichts. Das Commit-Schreiben der
/// akonto_zahlung-Datensätze passiert im AkontoWorkflowController.
/// </summary>
public class AkontoLaufService
{
    private readonly AppDbContext        _db;
    private readonly Iso20022PainService _painSvc;
    private readonly LgavBeitragService  _lgav;
    private readonly KtgTagessatzService _ktgTagessatz;
    public AkontoLaufService(AppDbContext db, Iso20022PainService painSvc,
                             LgavBeitragService lgav, KtgTagessatzService ktgTagessatz)
    {
        _db = db;
        _painSvc = painSvc;
        _lgav = lgav;
        _ktgTagessatz = ktgTagessatz;
    }

    public record AkontoRowDto(
        int     EmployeeId,
        string? EmployeeNumber,
        string  FirstName,
        string  LastName,
        string  EmploymentModel,
        decimal? EmploymentPercentage,
        decimal GeschaetzterBrutto,
        decimal GeschaetzteAbzuege,
        decimal NettoVorPfaendung,
        decimal PfaendungAbzug,
        decimal NettoAkonto,
        string  BruttoErlaeuterung,
        bool    IsEligible,
        string? AusschlussGrund,
        bool    HasPfaendung,
        // Walter-Vorgabe 28.05.2026: GF-Override gesetzt? (TRUE → Ineligibility
        // wird ignoriert, Akonto wird trotzdem berechnet — AusschlussGrund
        // bleibt aber als Warnung sichtbar.)
        bool    ForcePayout = false);

    public record AkontoVorschauResponse(
        int     Year,
        int     Month,
        string  Stichtag,            // yyyy-MM-dd
        string  PeriodFrom,
        string  PeriodTo,
        string? PayoutDate,          // aus akonto_termin, falls hinterlegt
        decimal AkontoProzentFix,    // % der Filiale für FIX/FIX-M
        int     CountEligible,
        int     CountExcluded,
        decimal TotalNetto,
        List<AkontoRowDto> Rows);

    // persistLgav (Walter-Vorgabe 20.05.2026): steuert, ob der LGAV-Auto-Eintrag
    // (LohnZulage Code 140) in die DB geschrieben wird. Default FALSE → die
    // Vorschau ist READ-ONLY (keine Daten-Änderung beim blossen Anschauen).
    // Nur die bewussten Commit-Pfade (Akonto-Start, Commit) übergeben TRUE.
    // EnsureAsync ist ohnehin idempotent (legt pro MA/Jahr höchstens einen
    // Eintrag an), aber eine Vorschau soll grundsätzlich nichts persistieren.
    public async Task<AkontoVorschauResponse> PreviewAsync(
        int companyProfileId, int year, int month, DateOnly stichtag,
        bool persistLgav = false,
        // Walter-Vorgabe 28.05.2026: Force-Payout-Overrides pro MA. Wenn TRUE
        // für einen empId, wird der Eligibility-Check ignoriert und Brutto/Netto
        // normal berechnet (z.B. „Krank am Stichtag aber Akonto trotzdem
        // auszahlen, weil 3 Tage AG-Karenz fällig"). Default: leeres Dict.
        IDictionary<int, bool>? forcePayoutByEmp = null)
    {
        var profile = await _db.CompanyProfiles.FindAsync(companyProfileId)
            ?? throw new InvalidOperationException("Filiale nicht gefunden.");

        var periodFrom = new DateOnly(year, month, 1);
        var periodTo   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));

        // Stichtag in der Periode normalisieren — wenn ausserhalb, auf Periodenende setzen.
        if (stichtag < periodFrom) stichtag = periodFrom;
        if (stichtag > periodTo)   stichtag = periodTo;

        // MA der Filiale mit Employment, das in der Periode gültig ist.
        // Phantom-MA (IsPayrollExcluded=true, z.B. Supervisor mit easy@work-
        // Zugang ohne Lohn) bewusst ausschliessen — sie kriegen NIE einen
        // Akonto (Walter-Vorgabe 16.05.2026).
        // Walter-Vorgabe 31.05.2026: KEIN IsActive-Filter mehr auf Employment —
        // beim Austritt wird das Flag oft automatisch auf false gesetzt, der
        // Vertrag gilt aber für den letzten Lohnmonat noch. Einzig massgeblich:
        // ContractStartDate <= periodTo UND (ContractEndDate >= periodFrom).
        var employees = await _db.Employees
            .Include(e => e.Employments)
            .Where(e => !e.IsPayrollExcluded
                     && e.Employments.Any(emp =>
                            emp.CompanyProfileId == companyProfileId
                         && emp.ContractStartDate <= periodTo.ToDateTime(TimeOnly.MinValue)
                         && (emp.ContractEndDate == null
                             || emp.ContractEndDate >= periodFrom.ToDateTime(TimeOnly.MinValue))))
            .ToListAsync();
        var empIds = employees.Select(e => e.Id).ToList();

        // SV-Sätze gültig am Stichtag, dedupliziert nach fachlichem Schlüssel
        // (latest-ValidFrom gewinnt). QST-only und inaktive sind ausgeschlossen.
        var allSv = await _db.SocialInsuranceRates
            .Where(r => r.IsActive
                     && !r.OnlyQuellensteuer
                     && r.ValidFrom <= stichtag
                     && (r.ValidTo == null || r.ValidTo >= stichtag))
            .ToListAsync();
        var svRates = allSv
            .GroupBy(r => new {
                r.Code, r.MinAge, r.MaxAge,
                EmpModel = r.EmploymentModelCode ?? "",
                r.BasisType
            })
            .Select(g => g.OrderByDescending(r => r.ValidFrom).First())
            .ToList();

        // Absenzen, Stempelzeiten, Pfändungen vorladen
        var absences = await _db.Absences
            .Where(a => empIds.Contains(a.EmployeeId)
                     && a.DateFrom <= periodTo
                     && a.DateTo   >= periodFrom)
            .ToListAsync();
        var timeEntries = await _db.EmployeeTimeEntries
            .Where(t => empIds.Contains(t.EmployeeId)
                     && t.EntryDate >= periodFrom
                     && t.EntryDate <= stichtag)
            .ToListAsync();
        // Nur mit Dokument gültig (Walter 02.08.2026) — analog Definitivlauf.
        var assignments = await _db.EmployeeLohnAssignments
            .Where(la => empIds.Contains(la.EmployeeId)
                      && la.DokumentId != null
                      && la.ValidFrom <= periodTo
                      && (la.ValidTo == null || la.ValidTo >= periodFrom))
            .ToListAsync();

        var akontoTermin = await _db.AkontoTermine
            .FirstOrDefaultAsync(t => t.CompanyProfileId == companyProfileId
                                    && t.Year == year && t.Month == month);

        // Vormonat-PayrollSaldos (Walter Regel 5/6): brauchen wir für die
        // Ferien-Pott-Berechnung bei UTP/MTP. Wir nehmen pro MA den jüngsten
        // Saldo vor der aktuellen Periode — egal welche Filiale, weil ein MA
        // typischerweise nur in einer Filiale Lohn bekommt (Phantom-MA sind
        // schon oben gefiltert). Bei Periode 01/2026 → letzte Saldo aus 12/2025.
        var refKey = year * 12 + month;
        var allSaldos = await _db.PayrollSaldos
            .Where(s => empIds.Contains(s.EmployeeId))
            .ToListAsync();
        var lastSaldoByEmp = allSaldos
            .Where(s => s.PeriodYear * 12 + s.PeriodMonth < refKey)
            .GroupBy(s => s.EmployeeId)
            .ToDictionary(g => g.Key,
                          g => g.OrderByDescending(s => s.PeriodYear * 12 + s.PeriodMonth).First());

        // LGAV-Auto-Eintrag pro MA (Walter-Vorgabe 19.05.2026): wenn der Filial-
        // Trigger erreicht ist, fügt LgavBeitragService.EnsureAsync eine
        // LohnZulage mit Code 140 für den MA + Periode ein (idempotent). Damit
        // taucht der LGAV-Beitrag automatisch in der Akonto-Liste der manuellen
        // Zulagen/Abzüge auf und reduziert das Akonto entsprechend.
        // NUR im Commit-Pfad (persistLgav=true) persistieren — die reine Vorschau
        // schreibt nichts (Walter-Vorgabe 20.05.2026).
        if (persistLgav)
        {
            foreach (var e in employees)
            {
                // Walter-Vorgabe 31.05.2026: KEIN IsActive-Filter (siehe oben).
                var emp = e.Employments
                    .Where(x => x.CompanyProfileId == companyProfileId)
                    .Where(x => DateOnly.FromDateTime(x.ContractStartDate) <= periodTo)
                    .Where(x => !x.ContractEndDate.HasValue
                             || DateOnly.FromDateTime(x.ContractEndDate.Value) >= periodFrom)
                    .OrderByDescending(x => x.ContractStartDate)
                    .FirstOrDefault();
                if (emp == null) continue;
                await _lgav.EnsureAsync(e, emp, profile, year, month, periodFrom, periodTo);
            }
        }

        // Manuelle Zulagen/Abzüge für diese Periode (Walter-Vorgabe 19.05.2026)
        // — werden im Akonto BEREITS berücksichtigt, nicht erst beim Definitiv-
        // lauf. Format Periode: "yyyy-MM" wie in LohnZulage hinterlegt.
        var periodKey = $"{year}-{month:D2}";
        var manualZulagen = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => empIds.Contains(z.EmployeeId) && z.Periode == periodKey)
            .ToListAsync();
        var manualByEmp = manualZulagen
            .GroupBy(z => z.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Walter-Vorgabe 28.05.2026: alle Filialen vorab laden, damit der
        // Fehler-Pfad „Vertrag hängt an Filiale X" den Namen der fremden
        // Filiale ohne N+1-Query nennen kann.
        var allProfiles = await _db.CompanyProfiles
            .Select(p => new { p.Id, p.CompanyName, p.RestaurantCode })
            .ToDictionaryAsync(p => p.Id);

        var rows = new List<AkontoRowDto>();
        foreach (var e in employees)
        {
            // Vertrag muss in der Lohnperiode tatsächlich gültig sein
            // (Walter-Bug 16.05.2026: bisher reichte IsActive — MA mit Vertrag
            // der erst im Folgemonat beginnt oder schon vor der Periode endete
            // bekamen einen Akonto, obwohl die Lohnzettel-Vorschau "kein
            // gültiger Vertrag in der Periode" sagte).
            // Walter-Vorgabe 31.05.2026: KEIN IsActive-Filter mehr — siehe
            // Begründung beim oberen Employees-Filter (Austrittsmonat-Bug).
            var emp = e.Employments
                .Where(x => x.CompanyProfileId == companyProfileId)
                .Where(x => DateOnly.FromDateTime(x.ContractStartDate) <= periodTo)
                .Where(x => !x.ContractEndDate.HasValue
                         || DateOnly.FromDateTime(x.ContractEndDate.Value) >= periodFrom)
                .OrderByDescending(x => x.ContractStartDate)
                .FirstOrDefault();
            if (emp is null)
            {
                // Walter-Vorgabe 28.05.2026: MA mit aktivem Vertrag in DIESER Filiale
                // aber KEIN Vertrag in der Periode → trotzdem zeigen, mit klarer
                // Fehlermeldung. Ausgetretene MA (ExitDate vor Periodenende) NICHT
                // zeigen — die wurden bewusst beendet, keine Fehler-Anzeige nötig.
                DateOnly? exit = e.ExitDate.HasValue
                    ? DateOnly.FromDateTime(e.ExitDate.Value)
                    : (DateOnly?)null;
                if (exit.HasValue && exit.Value < periodFrom) continue;

                // Walter-Vorgabe 28.05.2026 (Erweiterung): MA mit Zukunfts-Vertrag
                // (Eintritt erst NACH der Periode) ebenfalls NICHT als Fehler zeigen.
                // Beispiel: Eintritt 16.2.2026, Lohnlauf für Januar 2026 → MA war
                // noch gar nicht da. Greift, wenn ALLE Verträge in dieser Filiale
                // erst nach Periodenende beginnen.
                // IsActive bewusst weggelassen (Walter-Vorgabe 31.05.2026): Vertragslebenszyklus
                // ist datum-basiert. Hier zählt jeder Vertrag in dieser Filiale, der
                // datums-mässig in die Zukunft fällt — IsActive ist irrelevant.
                bool alleZukunftsVertrag = e.Employments
                    .Where(x => x.CompanyProfileId == companyProfileId)
                    .All(x => DateOnly.FromDateTime(x.ContractStartDate) > periodTo);
                if (alleZukunftsVertrag) continue;

                // Diagnostik: wenn dieser MA einen Vertrag in einer ANDEREN Filiale
                // hat, das in der Meldung nennen — typisches Walter-Szenario
                // (Personalnummer suggeriert Filiale X, aber Vertrag hängt an Y).
                var fremderVertrag = e.Employments
                    .Where(x => x.CompanyProfileId != companyProfileId
                             && DateOnly.FromDateTime(x.ContractStartDate) <= periodTo
                             && (!x.ContractEndDate.HasValue
                                 || DateOnly.FromDateTime(x.ContractEndDate.Value) >= periodFrom))
                    .OrderByDescending(x => x.ContractStartDate)
                    .FirstOrDefault();
                string reason;
                if (fremderVertrag != null
                    && fremderVertrag.CompanyProfileId.HasValue
                    && allProfiles.TryGetValue(fremderVertrag.CompanyProfileId.Value, out var fpProfile))
                {
                    var fremdName = $"{fpProfile.RestaurantCode} {fpProfile.CompanyName}".Trim();
                    reason = $"⚠ Vertrag hängt an Filiale \"{fremdName}\" - in dieser Filiale kein Vertrag";
                }
                else if (fremderVertrag != null)
                {
                    reason = $"⚠ Vertrag hängt an Filiale #{fremderVertrag.CompanyProfileId} - in dieser Filiale kein Vertrag";
                }
                else
                {
                    reason = "⚠ Kein gültiger Vertrag in dieser Periode - bitte Vertrag erfassen";
                }

                rows.Add(new AkontoRowDto(
                    EmployeeId:          e.Id,
                    EmployeeNumber:      e.EmployeeNumber,
                    FirstName:           e.FirstName ?? "",
                    LastName:            e.LastName ?? "",
                    EmploymentModel:     "",
                    EmploymentPercentage:null,
                    GeschaetzterBrutto:  0m,
                    GeschaetzteAbzuege:  0m,
                    NettoVorPfaendung:   0m,
                    PfaendungAbzug:      0m,
                    NettoAkonto:         0m,
                    BruttoErlaeuterung:  "",
                    IsEligible:          false,
                    AusschlussGrund:     reason,
                    HasPfaendung:        false,
                    ForcePayout:         false));
                continue;
            }

            lastSaldoByEmp.TryGetValue(e.Id, out var lastSaldo);
            manualByEmp.TryGetValue(e.Id, out var maZulagen);
            bool forcePayout = forcePayoutByEmp != null
                            && forcePayoutByEmp.TryGetValue(e.Id, out var fp) && fp;

            rows.Add(await BuildRow(e, emp, profile, stichtag, periodFrom, periodTo,
                              svRates, absences, timeEntries, assignments, lastSaldo,
                              maZulagen ?? new List<LohnZulage>(),
                              forcePayout: forcePayout));
        }

        // Sortierung nach Vorname (CLAUDE.md-Konvention für alle MA-Listen,
        // Walter-Vorgabe 16.05.2026); Tie-Break über Nachname.
        rows = rows
            .OrderBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.LastName,   StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AkontoVorschauResponse(
            year, month,
            stichtag.ToString("yyyy-MM-dd"),
            periodFrom.ToString("yyyy-MM-dd"),
            periodTo.ToString("yyyy-MM-dd"),
            akontoTermin?.PayoutDate.ToString("yyyy-MM-dd"),
            profile.AkontoProzentFix,
            rows.Count(r => r.IsEligible),
            rows.Count(r => !r.IsEligible),
            rows.Sum(r => r.NettoAkonto),
            rows);
    }

    // ── Pro-MA-Berechnung ───────────────────────────────────────────────────

    private async Task<AkontoRowDto> BuildRow(
        Employee e, Employment emp, CompanyProfile profile,
        DateOnly stichtag, DateOnly periodFrom, DateOnly periodTo,
        List<SocialInsuranceRate> svRates,
        List<Absence> absences,
        List<EmployeeTimeEntry> timeEntries,
        List<EmployeeLohnAssignment> assignments,
        PayrollSaldo? lastSaldo,
        List<LohnZulage> manualZulagen,
        // Walter-Vorgabe 28.05.2026: GF-Override pro MA
        bool forcePayout = false)
    {
        var model = (emp.EmploymentModel ?? "").ToUpperInvariant();

        // 1) Eligibility (Regeln 1 + 2)
        var (isEligible, reason) = CheckEligibility(e, emp, stichtag, periodFrom, periodTo, absences);

        // Pfändung-Datensatz (auch wenn nicht eligible, fürs Anzeigen).
        var assignment = assignments.FirstOrDefault(la => la.EmployeeId == e.Id);

        // Walter-Vorgabe 28.05.2026: GF-Override — bei ineligible MA trotzdem
        // berechnen, AusschlussGrund bleibt als Warnung sichtbar.
        if (!isEligible && !forcePayout)
        {
            return new AkontoRowDto(
                e.Id, e.EmployeeNumber, e.FirstName ?? "", e.LastName ?? "",
                model, emp.EmploymentPercentage,
                0m, 0m, 0m, 0m, 0m, "",
                false, reason, assignment != null,
                ForcePayout: false);
        }

        // 2) Brutto-Schätzung pro Modell
        decimal brutto;
        string  bruttoErlaeuterung;
        switch (model)
        {
            case "FLEX":
            case "MTP":
                // Regel 5/6: Stunden bis Stichtag × Rate + Ferien-Pott (nur abgeschlossene Bezüge)
                (brutto, bruttoErlaeuterung) = ComputeBruttoHourly(
                    e, emp, profile, periodFrom, periodTo, stichtag, timeEntries, absences, lastSaldo);
                // Walter-Vorgabe 30.05.2026: bei UTP zusätzlich Krank-Karenz 88%
                // (Tage VOR Stichtag × KTG-Tagessatz × 88%) und Feiertagentschädigung
                // (% auf Brutto) ins Akonto-Brutto einrechnen. Diese Komponenten
                // sind zum Stichtag bereits feststehend und der MA hat darauf
                // einen festen Anspruch — sonst wäre der UTP-Akonto bei Krank-MA
                // strukturell viel zu niedrig (Definitiv-Netto >> Akonto).
                // MTP braucht das nicht: dort deckt der Garantie-Festlohn bereits
                // die Krank/Feiertag-Komponenten ab (kein Lohn-Ausfall bei MTP).
                if (model == "FLEX")
                {
                    // Krank-Karenz 88% (nur Tage VOR Stichtag)
                    decimal krankTageBisStichtag = absences
                        .Where(a => a.EmployeeId == e.Id
                                 && a.AbsenceType == "KRANK"
                                 && a.DateFrom <= stichtag)
                        .Sum(a => CountAbsenceTageBisStichtag(a, periodFrom, stichtag)
                                  * (a.Prozent > 0 ? a.Prozent / 100m : 1m));
                    if (krankTageBisStichtag > 0)
                    {
                        var ktg = await _ktgTagessatz.CalculateAsync(e.Id, profile.Id);
                        decimal tagessatz100 = ktg?.Tagessatz100 ?? 0m;
                        if (tagessatz100 > 0)
                        {
                            decimal karenz88 = Math.Round(krankTageBisStichtag * tagessatz100 * 0.88m, 2);
                            brutto += karenz88;
                            bruttoErlaeuterung += $" + Krank-Karenz {krankTageBisStichtag:0.00} Tg × CHF {tagessatz100:0.00} × 88% = CHF {karenz88:0.00}";
                        }
                    }
                    // Feiertagentschädigung (% auf Brutto)
                    // Walter-Vorgabe 06.06.2026 (Stufe 1b): nur noch Filial-Default
                    decimal feiertagPct = profile.DefaultHolidayPercent ?? 2.27m;
                    if (feiertagPct > 0)
                    {
                        decimal feiertagEnt = Math.Round(brutto * feiertagPct / 100m, 2);
                        brutto += feiertagEnt;
                        bruttoErlaeuterung += $" + Feiertag {feiertagPct:0.000}% = CHF {feiertagEnt:0.00}";
                    }
                }
                break;
            case "FIX":
            case "FIX-M":
                // Regel 3/4: grobe Schätzung über Monatslohn. Exakte Korrektur
                // erfolgt via Frontend-Trigger /sync-fix-from-slip (PayrollController
                // liefert dort die echte voraussichtliche Auszahlung).
                brutto = emp.MonthlySalary ?? emp.MonthlySalaryFte ?? 0m;
                bruttoErlaeuterung = $"Monatslohn CHF {brutto:0.00} (Vorschätzung, wird via Slip-Sync korrigiert)";
                break;
            default:
                brutto = 0m;
                bruttoErlaeuterung = $"Unbekanntes Modell '{model}'";
                break;
        }

        // 3) Manuelle Zulagen/Abzüge (Walter 19.05.2026): erfasst pro MA+Periode
        //    via /api/lohn-zulagen. ZULAGE addiert zum Brutto (geht in SV-Basis),
        //    ABZUG wird nach Netto verrechnet (NICHT SV-pflichtig, z.B. LGAV).
        //
        // Walter-Vorgabe 30.05.2026: Saldo-Vortrags-Lohnpositionen (Codes 901–906,
        // Kategorie "Saldo-Vortrag") werden NICHT als Brutto-Zulage gezählt — sie
        // sind reine Migrations-Einträge die nur die Saldi aufbauen (Ferien-Geld,
        // 13. ML, Stunden-Saldo, Nacht-Saldo, Ferien-Tage-Saldo). Im Definitivlauf
        // läuft das gleich (PayrollCalculationEngine schließt Kategorie =
        // "Saldo-Vortrag" über IsVortrag aus). Vorher hat der Akonto-Lauf diese
        // Vorträge fälschlich addiert → Brutto-Schätzung zu hoch → Über-Akonto.
        decimal zulagenSum = 0m;
        decimal manuelleAbzuege = 0m;
        foreach (var z in manualZulagen)
        {
            if (z.Lohnposition == null) continue;
            if (z.Lohnposition.Kategorie == "Saldo-Vortrag") continue;  // nicht ins Brutto
            if (z.Lohnposition.Typ == "ZULAGE")
                zulagenSum += z.Betrag;
            else if (z.Lohnposition.Typ == "ABZUG")
                manuelleAbzuege += z.Betrag;
        }
        brutto += zulagenSum;
        if (zulagenSum > 0)
            bruttoErlaeuterung += $" + Zulagen CHF {zulagenSum:0.00}";

        // 4) Abzüge (SV + BVG, kein QST)
        int? age = AgeAt(e.DateOfBirth, stichtag);
        decimal abzuege = ComputeDeductions(brutto, svRates, model, age);

        // 5) Netto-Vorschlag — AkontoProzent je nach Modell
        //    Regel 3:  FIX    → AkontoProzentFix    (Default 80%)
        //    Regel 4:  FIX-M  → AkontoProzentFixM   (Default 90%, Walter 18.05.2026)
        //    Regel 5/6: UTP/MTP → AkontoProzentHourly (Default 100%)
        decimal nettoVoll = brutto - abzuege - manuelleAbzuege;
        decimal factor = model switch
        {
            "FIX"   => Math.Clamp(profile.AkontoProzentFix,    0m, 100m) / 100m,
            "FIX-M" => Math.Clamp(profile.AkontoProzentFixM,   0m, 100m) / 100m,
            _       => Math.Clamp(profile.AkontoProzentHourly, 0m, 100m) / 100m,
        };
        decimal nettoVor = nettoVoll * factor;

        // 5) Auf CHF 10 abrunden, untere Grenze 0
        nettoVor = Math.Floor(nettoVor / 10m) * 10m;
        if (nettoVor < 0m) nettoVor = 0m;

        // 6) Pfändungs-Cap
        decimal pfaendungAbzug = 0m;
        decimal nettoAkonto    = nettoVor;
        bool hasPfaendung = assignment != null;
        if (assignment != null)
        {
            if (assignment.Freigrenze <= 0m)
            {
                pfaendungAbzug = nettoVor;
                nettoAkonto    = 0m;
                isEligible     = false;
                reason         = "Lohnpfändung — Freigrenze 0";
            }
            else if (nettoVor > assignment.Freigrenze)
            {
                pfaendungAbzug = nettoVor - assignment.Freigrenze;
                nettoAkonto    = assignment.Freigrenze;
            }
        }

        // Walter-Vorgabe 28.05.2026: bei GF-Override behalten wir den ursprünglichen
        // AusschlussGrund als Warnung sichtbar — IsEligible ist TRUE (Akonto wird
        // berechnet), aber das Frontend kann die Warnung anzeigen ("trotzdem
        // ausgezahlt obwohl Krank am Stichtag").
        string? finalReason = reason;
        bool    finalEligible = isEligible || forcePayout;
        if (forcePayout && !isEligible && finalReason != null)
        {
            finalReason = $"⚠ Override aktiv: {finalReason}";
        }

        return new AkontoRowDto(
            e.Id, e.EmployeeNumber, e.FirstName ?? "", e.LastName ?? "",
            model, emp.EmploymentPercentage,
            Math.Round(brutto, 2),
            Math.Round(abzuege, 2),
            Math.Round(nettoVor, 2),
            Math.Round(pfaendungAbzug, 2),
            Math.Round(nettoAkonto, 2),
            bruttoErlaeuterung,
            finalEligible,
            finalReason,
            hasPfaendung,
            ForcePayout: forcePayout);
    }

    // ── Eligibility ────────────────────────────────────────────────────────

    private static (bool ok, string? reason) CheckEligibility(
        Employee e, Employment emp,
        DateOnly stichtag, DateOnly periodFrom, DateOnly periodTo,
        List<Absence> absences)
    {
        // Probezeit aktiv (ProbationEndDate liegt am oder nach dem Stichtag).
        if (emp.ProbationEndDate.HasValue
            && DateOnly.FromDateTime(emp.ProbationEndDate.Value) >= stichtag)
            return (false, "in Probezeit");

        // Regel 1 (Walter 16.05.2026): kein Akonto wenn Vertragsende ≤ Periodenende.
        // ExitDate am Employee oder ContractEndDate am Employment — was zuerst kommt.
        DateOnly? exit = e.ExitDate.HasValue
            ? DateOnly.FromDateTime(e.ExitDate.Value)
            : (emp.ContractEndDate.HasValue
                ? DateOnly.FromDateTime(emp.ContractEndDate.Value)
                : (DateOnly?)null);
        if (exit.HasValue && exit.Value <= periodTo)
            return (false, $"Austritt {exit.Value:dd.MM.yyyy}");

        // Krankheit/Unfall/Mutterschaft AM STICHTAG aktiv (Walter-Vorgabe
        // 15.05.2026): nur ausschliessen wenn die Absenz zum Auszahlungs-
        // Zeitpunkt noch läuft — kurze Absenzen vor dem Stichtag (z.B.
        // 1-Tages-Krank Anfang Monat) hindern den Akonto NICHT.
        var k = absences.FirstOrDefault(a => a.EmployeeId == e.Id
                                          && a.AbsenceType == "KRANK"
                                          && a.DateFrom <= stichtag && a.DateTo >= stichtag);
        if (k != null) return (false, $"Krank am Stichtag ({k.DateFrom:dd.MM.}–{k.DateTo:dd.MM.})");
        var u = absences.FirstOrDefault(a => a.EmployeeId == e.Id
                                          && a.AbsenceType == "UNFALL"
                                          && a.DateFrom <= stichtag && a.DateTo >= stichtag);
        if (u != null) return (false, $"Unfall am Stichtag ({u.DateFrom:dd.MM.}–{u.DateTo:dd.MM.})");
        var m = absences.FirstOrDefault(a => a.EmployeeId == e.Id
                                          && a.AbsenceType == "MUTT_VATER"
                                          && a.DateFrom <= stichtag && a.DateTo >= stichtag);
        if (m != null) return (false, $"Mutter/Vater am Stichtag ({m.DateFrom:dd.MM.}–{m.DateTo:dd.MM.})");

        return (true, null);
    }

    // ── Brutto-Schätzung UTP/MTP (Walter Regel 5/6, 16.05.2026) ────────────
    //
    // Brutto = gestempelte Stunden bis Stichtag × HourlyRate
    //        + Ferien-Auszahlung aus Pott (nur abgeschlossene Bezüge ≤ Stichtag)
    //
    // Ferien-Pott (FerienAuszahlungService) berücksichtigt:
    //   • Vormonats-Feriengeld-Saldo (PayrollSaldo) + Akkumulation diesen Monat
    //   • Vormonats-Tage-Saldo + Tage-Accrual diesen Monat
    //   • Tagessatz × bezogene Tage = Auszahlung, gedeckelt auf Pott CHF
    //
    // KEIN Voll-Tagessatz mehr (alter Bug: 6 Tage × WeeklyH/5 × HourlyRate
    // überschätzte den Definitiv-Tagessatz; jetzt wird der echte Pott-Ø-Satz
    // verwendet → Akonto kann nie höher sein als der Definitivlohn).
    // Walter-Vorgabe 30.05.2026: zählt die Tage einer Absenz, die in
    // [periodFrom..stichtag] liegen — analog CountAbsenceDaysInPeriod im
    // PayrollCalculationService, aber mit Stichtag statt periodTo.
    private static int CountAbsenceTageBisStichtag(Absence a, DateOnly periodFrom, DateOnly stichtag)
    {
        var from = a.DateFrom;
        var to   = a.DateTo;
        if (from < periodFrom) from = periodFrom;
        if (to   > stichtag)   to   = stichtag;
        if (to < from) return 0;
        return to.DayNumber - from.DayNumber + 1;
    }

    private static (decimal Brutto, string Erlaeuterung) ComputeBruttoHourly(
        Employee e, Employment emp, CompanyProfile profile,
        DateOnly periodFrom, DateOnly periodTo, DateOnly stichtag,
        List<EmployeeTimeEntry> timeEntries,
        List<Absence> absences,
        PayrollSaldo? lastSaldo)
    {
        decimal hourly = emp.HourlyRate ?? 0m;
        var model = (emp.EmploymentModel ?? "").ToUpperInvariant();

        // Walter-Vorgabe 30.05.2026: bei MTP wird das Akonto auf den GARANTIERTEN
        // Pro-Rata-Festlohn der Periode gedeckelt — NICHT auf workedHours bis
        // Stichtag (die könnten höher sein als am Monatsende → Über-Akonto →
        // negativer Auszahlungsbetrag). Der Definitivlauf rechnet Mehrstunden,
        // Pott-Auszahlung und Feiertag-Entschädigung exakt, da gibt es keinen
        // Grund das Akonto damit aufzublähen.
        //   MTP: brutto = (guaranteedH / 7 × Kalendertage) × Stundenlohn
        //   UTP: brutto = workedHours_bis_Stichtag × Stundenlohn (bleibt)
        decimal hours;
        decimal bruttoStunden;
        string hourErl;
        if (model == "MTP")
        {
            decimal guarH = emp.GuaranteedHoursPerWeek ?? 0m;
            // Anzahl Tage der vollen Lohnperiode (Kalendermonat)
            int normalPeriodDays = new DateOnly(periodFrom.Year, periodFrom.Month, 1)
                .AddMonths(1).AddDays(-1).Day;
            decimal sollStunden = Math.Round(guarH / 7m * normalPeriodDays, 2);
            hours          = sollStunden;
            bruttoStunden  = Math.Round(sollStunden * hourly, 2);
            hourErl        = $"{sollStunden:0.00}h Garantie ({guarH} h/Wo × {normalPeriodDays}/7) × CHF {hourly:0.00} = CHF {bruttoStunden:0.00}";
        }
        else
        {
            hours = TimeEntryHours.SumAbsolute(timeEntries.Where(t => t.EmployeeId == e.Id));
            bruttoStunden = hours * hourly;
            hourErl       = $"{hours:0.00}h × CHF {hourly:0.00} = CHF {bruttoStunden:0.00}";
        }

        // Ferien-Pott (Walter-Vorgabe 30.05.2026): bei MTP NICHT mehr vorgezogen
        // — der Definitivlauf zahlt das exakt aus. Bei UTP bleibt der Pott-
        // Vorzug, weil UTP-MA in Krankheitsmonaten oft KEIN Festlohn bekommen
        // und auf das Pott-Geld angewiesen sind.
        decimal pottAuszahlung = 0m, pottTagessatz = 0m, pottTageBezogen = 0m;
        if (model != "MTP")
        {
            var empAbsences = absences.Where(a => a.EmployeeId == e.Id).ToList();
            decimal bezogeneTage = FerienAuszahlungService
                .SumAbgeschlosseneFerientageBisStichtag(empAbsences, periodFrom, stichtag);
            // Walter-Vorgabe 06.06.2026 (Stufe 1b): aus der Filiale, altersaware
            decimal vacationPct = profile.DefaultVacationPercent5Weeks ?? 10.64m;
            if (e.DateOfBirth.HasValue)
            {
                var dob = DateOnly.FromDateTime(e.DateOfBirth.Value);
                if (dob.AddYears(profile.VacationSixWeeksFromAge) <= periodTo)
                    vacationPct = profile.DefaultVacationPercent6Weeks ?? 13.04m;
            }
            int vacationWeeks   = vacationPct >= 12.5m ? 6 : 5;
            decimal vormonatChf  = lastSaldo?.FerienGeldSaldo ?? 0m;
            decimal vormonatTage = lastSaldo?.FerienTageSaldo ?? 0m;
            decimal accrualChf  = Math.Round(bruttoStunden * vacationPct / 100m, 2);
            decimal accrualTage = Math.Round(vacationWeeks * 7m / 12m, 4);
            var pott = FerienAuszahlungService.Compute(
                vormonatChf, accrualChf, vormonatTage, accrualTage, bezogeneTage);
            pottAuszahlung  = pott.AuszahlungChf;
            pottTagessatz   = pott.Tagessatz;
            pottTageBezogen = pott.BezogeneTage;
        }

        decimal brutto = bruttoStunden + pottAuszahlung;
        string s = hourErl
                 + (pottAuszahlung > 0
                     ? $" + Ferien-Pott {pottTageBezogen:0.00} Tg × Ø CHF {pottTagessatz:0.00} = CHF {pottAuszahlung:0.00}"
                     : "");
        return (brutto, s);
    }

    // ── Abzüge-Schätzung ────────────────────────────────────────────────────
    // Schleife über die effektiv gültigen SV-Sätze. Filter:
    //   • EmploymentModelCode null = alle Modelle, sonst gleich.
    //   • Alters-Filter: MinAge/MaxAge (falls am MA Geburtsdatum hinterlegt).
    // BasisType:
    //   • gross           → Rate × max(brutto − FreibetragMonthly, 0)
    //   • bvg_basis       → Rate × max(brutto − CoordinationDeduction, 0)
    //   • coord_deduction → Rate × CoordinationDeduction (Kaderlösung)
    private static decimal ComputeDeductions(
        decimal brutto, List<SocialInsuranceRate> svRates, string modelCode, int? age)
    {
        if (brutto <= 0m) return 0m;
        decimal total = 0m;
        foreach (var r in svRates)
        {
            if (!string.IsNullOrEmpty(r.EmploymentModelCode)
                && !r.EmploymentModelCode.Equals(modelCode, StringComparison.OrdinalIgnoreCase))
                continue;
            if (r.MinAge.HasValue && age.HasValue && age.Value < r.MinAge.Value) continue;
            if (r.MaxAge.HasValue && age.HasValue && age.Value > r.MaxAge.Value) continue;

            decimal basis;
            switch (r.BasisType)
            {
                case "bvg_basis":
                {
                    var koord = r.CoordinationDeduction ?? 0m;
                    basis = brutto > koord ? brutto - koord : 0m;
                    break;
                }
                case "coord_deduction":
                    basis = r.CoordinationDeduction ?? 0m;
                    break;
                default:    // "gross"
                {
                    var freibetrag = r.FreibetragMonthly ?? 0m;
                    basis = brutto > freibetrag ? brutto - freibetrag : 0m;
                    break;
                }
            }
            total += basis * (r.Rate / 100m);
        }
        return total;
    }

    private static int? AgeAt(DateTime? dob, DateOnly stichtag)
    {
        if (!dob.HasValue) return null;
        var d = DateOnly.FromDateTime(dob.Value);
        int age = stichtag.Year - d.Year;
        if (stichtag.Month < d.Month || (stichtag.Month == d.Month && stichtag.Day < d.Day)) age--;
        return age;
    }

    // ── DTA-Generierung (Walter-Vorgabe 17.05.2026, Phase 3d) ─────────────
    //
    // Erzeugt das pain.001-XML für den Akonto-Lauf einer Periode. Wie beim
    // Definitivlauf (LohnlaufService.GenerateDtaMaAsync) on-demand — kein
    // File-Storage. Re-Download via gleichem Endpoint, generiert frisch aus
    // der DB. Voraussetzung: AkontoStatus = AUSBEZAHLT.
    //
    // Empfänger ist die HAUPTBANK des MA (akonto wird nicht aufgeteilt — der
    // Netto-Akonto fliesst als Ganzes auf die Hauptbank). Wenn der MA einen
    // abweichenden Empfänger gesetzt hat (Kontoinhaber/Adresse, z.B. Revolut),
    // wird dessen Adresse als Cdtr verwendet — analog Definitivlauf.

    public async Task<byte[]> GenerateDtaAsync(int companyProfileId, int year, int month)
    {
        var periode = await _db.PayrollPerioden
            .Include(p => p.Company)
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId
                                   && p.Year == year && p.Month == month);
        if (periode is null)
            throw new InvalidOperationException($"Periode {year}-{month:D2} für Filiale {companyProfileId} nicht gefunden.");
        if (periode.Company is null)
            throw new InvalidOperationException("Filiale-Stammdaten fehlen.");
        if (periode.AkontoStatus != "AUSBEZAHLT")
            throw new InvalidOperationException(
                $"Akonto-Status ist '{periode.AkontoStatus}' — DTA nur aus AUSBEZAHLT generierbar.");

        var zahlungen = await _db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == companyProfileId
                     && z.PeriodYear == year && z.PeriodMonth == month
                     && z.Status == "AUSBEZAHLT")
            .OrderBy(z => z.EmployeeId)
            .ToListAsync();

        if (zahlungen.Count == 0)
            throw new InvalidOperationException("Keine ausbezahlten Akonto-Datensätze — DTA leer.");

        var empIds = zahlungen.Select(z => z.EmployeeId).Distinct().ToList();
        var employees = await _db.Employees
            .Where(e => empIds.Contains(e.Id))
            .ToListAsync();
        // Bankverbindung pro MA (zum Akonto-Zeitpunkt). Walter-Vorgabe
        // 18.05.2026: bevorzugt Hauptbank, aber Fallback auf beliebige
        // aktive Bank — sonst fällt ein MA mit nur einer (nicht als
        // Hauptbank markierter) Bank komplett aus dem DTA raus.
        var stichtag = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var allBanks = await _db.EmployeeBankAccounts
            .Where(b => empIds.Contains(b.EmployeeId)
                     && b.ValidFrom <= stichtag
                     && (b.ValidTo == null || b.ValidTo >= new DateOnly(year, month, 1)))
            .OrderByDescending(b => b.IsHauptbank)
            .ThenByDescending(b => b.ValidFrom)
            .ToListAsync();
        var banks = allBanks
            .GroupBy(b => b.EmployeeId)
            .Select(g => g.First())
            .ToList();

        // Filial-Hauptbank als Debtor
        var dbtrBank = await _db.CompanyProfileBankAccounts
            .Where(b => b.CompanyProfileId == companyProfileId
                     && b.IsMain
                     && b.ValidFrom <= stichtag
                     && (b.ValidTo == null || b.ValidTo >= new DateOnly(year, month, 1)))
            .OrderByDescending(b => b.ValidFrom)
            .FirstOrDefaultAsync();
        if (dbtrBank is null)
            throw new InvalidOperationException(
                "Filiale hat keine gültige Hauptbank für diese Periode. " +
                "Bitte unter Filiale → Bankverbindungen erfassen + als Hauptbank markieren.");

        var payments = new List<Iso20022PainService.PaymentInstruction>();
        var skipped = new List<string>();
        foreach (var z in zahlungen)
        {
            if (z.NettoAkonto <= 0) continue;
            var emp = employees.FirstOrDefault(e => e.Id == z.EmployeeId);
            if (emp is null) continue;
            var bank = banks.FirstOrDefault(b => b.EmployeeId == z.EmployeeId);
            if (bank is null)
            {
                skipped.Add($"{emp.FirstName} {emp.LastName} (keine Hauptbank)");
                continue;
            }

            var maName = $"{emp.FirstName} {emp.LastName}".Trim();
            string  cdtrName;
            string? cdtrStreet, cdtrPlz, cdtrCity, cdtrCountry, cdtrBic;
            string  rmtInf;

            if (!string.IsNullOrWhiteSpace(bank.Kontoinhaber))
            {
                // Abweichender Empfänger (z.B. Revolut Bank UAB für Auslands-MA)
                cdtrName    = bank.Kontoinhaber!;
                cdtrStreet  = bank.KontoinhaberStrasse;
                cdtrPlz     = bank.KontoinhaberPlz;
                cdtrCity    = bank.KontoinhaberOrt;
                cdtrCountry = string.IsNullOrWhiteSpace(bank.KontoinhaberLand) ? "CH" : bank.KontoinhaberLand!;
                cdtrBic     = bank.Bic;
                rmtInf      = $"Akonto {periode.Label ?? $"{month:D2}/{year}"} - {maName}";
            }
            else
            {
                // MA selbst
                cdtrName   = maName;
                cdtrStreet = emp.Street;
                cdtrPlz    = emp.ZipCode;
                cdtrCity   = emp.City;
                cdtrCountry = string.IsNullOrWhiteSpace(emp.Country) ? "CH" : emp.Country!;
                cdtrBic    = bank.Bic;
                rmtInf     = $"Akonto {periode.Label ?? $"{month:D2}/{year}"}";
            }

            payments.Add(new Iso20022PainService.PaymentInstruction(
                EndToEndId:         $"AK{year}{month:D2}-{emp.Id}",
                Amount:             Math.Round(z.NettoAkonto, 2),
                CreditorName:       cdtrName,
                CreditorStreet:     cdtrStreet,
                CreditorPostalCode: cdtrPlz,
                CreditorCity:       cdtrCity,
                CreditorCountry:    cdtrCountry,
                CreditorIban:       bank.Iban,
                CreditorBic:        cdtrBic,
                RemittanceInfo:     rmtInf
            ));
        }

        if (payments.Count == 0)
            throw new InvalidOperationException("Keine Akonto-Auszahlungen mit Bankverbindung — DTA leer." +
                (skipped.Count > 0 ? $" Übersprungen: {string.Join(", ", skipped)}" : ""));

        // Bank-Ausführungsdatum (ReqdExctnDt im pain.001):
        // Walter-Vorgabe 19.05.2026 — explizites Feld AkontoAuszahlungsdatum
        // wird beim „DTA an Bank senden"-Bestätigungsschritt erfasst und
        // direkt im DTA verwendet. Fallback: AkontoAusbezahltAt.Date für alte
        // Datensätze ohne das Feld; ansonsten heute.
        var execDate = periode.AkontoAuszahlungsdatum
                     ?? (periode.AkontoAusbezahltAt.HasValue
                            ? DateOnly.FromDateTime(periode.AkontoAusbezahltAt.Value.ToLocalTime())
                            : DateOnly.FromDateTime(DateTime.Today));

        var initiator = string.IsNullOrWhiteSpace(periode.Company.BranchName)
            ? periode.Company.CompanyName
            : $"{periode.Company.CompanyName} {periode.Company.BranchName}";
        var initStreet = string.IsNullOrWhiteSpace(periode.Company.HouseNumber)
            ? periode.Company.Street
            : $"{periode.Company.Street} {periode.Company.HouseNumber}".Trim();

        var dtaReq = new Iso20022PainService.DtaRequest(
            MessageId:           $"AKONTO-{periode.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}",
            CreationDateTime:    DateTime.UtcNow,
            InitiatorName:       initiator,
            InitiatorStreet:     initStreet,
            InitiatorPostalCode: periode.Company.ZipCode,
            InitiatorCity:       periode.Company.City,
            InitiatorCountry:    string.IsNullOrWhiteSpace(periode.Company.Country) ? "CH" : periode.Company.Country!,
            ExecutionDate:       execDate,
            DebtorName:          initiator,
            DebtorIban:          dbtrBank.Iban,
            DebtorBic:           dbtrBank.Bic,
            Payments:            payments
        );

        return _painSvc.Generate(dtaReq);
    }
}
