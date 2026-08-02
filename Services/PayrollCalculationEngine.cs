using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using HrSystem.Controllers;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using static HrSystem.Services.PayrollCalculations;

namespace HrSystem.Services;

// ============================================================================
// Lohn-Berechnungs-Engine, ausgelagert aus PayrollController.cs
// (Walter-Vorgabe 20.05.2026, Etappe 3 der Controller-Entflechtung).
// Enthaelt die komplette Calculate-Orchestrierung (UTP/MTP/FIX) inkl.
// Quellensteuer-Abzug (ComputeQstDeduction). Gibt IActionResult zurueck
// (OkObjectResult/NotFoundObjectResult/...), damit die Aufrufer
// (HTTP-Endpoint, ConfirmPayroll, GetPdf) ihr .Value unveraendert
// weiterverwenden. Reine Rechen-Helfer liegen in PayrollCalculations
// (static, via using static importiert).
// ============================================================================
public class PayrollCalculationEngine
{
    private readonly AppDbContext _db;
    private readonly QuellensteuerTarifService _tarifService;
    private readonly KtgTagessatzService _ktgService;
    private readonly KarenzService _karenz;
    private readonly LgavBeitragService _lgav;
    private readonly UniformDepotService _uniformDepot;
    private readonly FerienKuerzungService _ferienKuerzung;
    private readonly QstPflichtCheckService _qstCheck;

    public PayrollCalculationEngine(
        AppDbContext db,
        QuellensteuerTarifService tarifService,
        KtgTagessatzService ktgService,
        KarenzService karenz,
        LgavBeitragService lgav,
        UniformDepotService uniformDepot,
        FerienKuerzungService ferienKuerzung,
        QstPflichtCheckService qstCheck)
    {
        _db             = db;
        _tarifService   = tarifService;
        _ktgService     = ktgService;
        _karenz         = karenz;
        _lgav           = lgav;
        _uniformDepot   = uniformDepot;
        _ferienKuerzung = ferienKuerzung;
        _qstCheck       = qstCheck;
    }

    public async Task<IActionResult> CalculateAsync(
        int employeeId, int year, int month, int companyProfileId,
        bool isCorrection = false)
    {
      if (isCorrection)
          return await CalculateCorrectionAsync(employeeId, year, month, companyProfileId);

      try {
        // ── Stammdaten laden ───────────────────────────────────────────────
        var employee = await _db.Employees
            .Include(e => e.Employments)
            .Include(e => e.NationalityRef)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (employee is null) return new NotFoundObjectResult("Mitarbeiter nicht gefunden.");
        if (employee.IsPayrollExcluded)
            return new BadRequestObjectResult(new { message = "Dieser Mitarbeiter ist als ‚Kein Lohn' markiert und wird nicht abgerechnet." });

        var company = await _db.CompanyProfiles.FindAsync(companyProfileId);
        if (company is null) return new NotFoundObjectResult("Filiale nicht gefunden.");

        // ── Akonto-Verrechnung-Lookup (Walter-Vorgabe 17.05.2026) ──────────
        // Wenn der Akonto-Lauf dieser Periode bereits AUSBEZAHLT ist, wird der
        // ausbezahlte Netto-Betrag im Definitiv-Lohnzettel als „Akonto-
        // Vorauszahlung vom dd.MM.yyyy"-Zeile unten ausgewiesen und vom
        // auszahlungsbetrag abgezogen. Der Lookup passiert hier einmal und
        // wird an BuildResult durchgereicht (das ist static und kann selber
        // kein async-DB nutzen).
        var akontoAusbezahlt = await _db.AkontoZahlungen
            .Where(z => z.EmployeeId == employeeId
                     && z.CompanyProfileId == companyProfileId
                     && z.PeriodYear == year && z.PeriodMonth == month
                     && z.Status == "AUSBEZAHLT")
            .Select(z => new { z.NettoAkonto, z.PayoutDate })
            .FirstOrDefaultAsync();
        decimal   akontoBereitsAusbezahlt      = akontoAusbezahlt != null ? Math.Round(akontoAusbezahlt.NettoAkonto, 2) : 0m;
        DateOnly? akontoBereitsAusbezahltDatum = akontoAusbezahlt?.PayoutDate;

        // ── Dezember-Jahresausgleich für gedeckelte SV (ALV/NBU) ───────────
        // Walter-Vorgabe 20.05.2026: ALV und NBU sind nur bis CHF 148'200/Jahr
        // (= 12'350/Mt.) beitragspflichtig. Im Dezember rechnen wir auf
        // JAHRESBASIS ab (Aufrollverfahren), weil zum Dezemberlohn noch Boni
        // hinzukommen — die flache Monatsdeckelung würde den Bonus fälschlich
        // kappen ODER (bei tiefen Vormonaten) zu wenig verbeitragen. Dazu
        // brauchen wir die AHV/ALV-Basis ALLER Vormonate (Jan–Nov) desselben
        // Jahres. SvBasisAhv ist UNGEDECKELT gespeichert → dient als Proxy für
        // ALV UND NBU (NBU-Basis ≈ AHV-Basis). Schaub Restaurants GmbH ist EIN
        // Arbeitgeber (eine AHV-Abrechnung) über alle Filialen → Summe über
        // ALLE Filialen des MA, nicht nur die aktuelle. STORNIERTE Snapshots
        // zählen nicht. NULL ausserhalb Dezember = flache Monatsdeckelung wie
        // bisher. Leere Liste (Dezember ohne Vormonate, z.B. Eintritt im Dez)
        // = Jahresausgleich gegen Jahres-Höchstlohn ab 0.
        List<decimal>? ytdSvBasesDez = null;
        if (month == 12)
        {
            ytdSvBasesDez = await (
                from s in _db.PayrollSnapshots
                join p in _db.PayrollPerioden on s.PayrollPeriodeId equals p.Id
                where s.EmployeeId == employeeId
                   && p.Year == year
                   && p.Month >= 1 && p.Month <= 11
                   && s.Status != "STORNIERT"
                select s.SvBasisAhv
            ).ToListAsync();
        }

        // ── Lohnperiode berechnen ──────────────────────────────────────────
        // Wichtig: Periode muss VOR der Vertragsauswahl berechnet werden,
        // damit wir den Vertrag laden können, der in dieser Periode gültig
        // war (nicht den neuesten verfügbaren).
        //
        // Walter-Vorgabe 20.05.2026: die Lohnperiode ist IMMER der Kalendermonat
        // (1.–letzter Tag). Keine Periodenregel-Konfiguration mehr, kein Rückgriff
        // auf gespeicherte PeriodFrom/PeriodTo (die könnten aus der alten 21.–20.-
        // Ära stammen). Gesetzliche Berechnungen (QST, ALV, AHV) laufen ohnehin
        // kalendermonatlich; der Akonto-Lauf deckt die Zahlung vor Monatsende ab.
        var existingPeriod = await _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == companyProfileId
                     && p.Year == year && p.Month == month)
            .FirstOrDefaultAsync();

        var (periodFrom, periodToFull) = CalcPeriod(year, month);
        int normalPeriodDays = periodToFull.DayNumber - periodFrom.DayNumber + 1;

        // Bemerkungstext für die Lohnabrechnung (Periode-spezifisch falls
        // vorhanden, sonst Filial-Default)
        string? periodeFooterText = existingPeriod?.PdfFooterText;

        // ── Den Vertrag laden, der in DIESER Periode gültig war ──────────
        // Regel (Walter-Vorgabe 31.05.2026, ABSOLUT): ContractStartDate <= periodToFull
        // UND (ContractEndDate IS NULL ODER ContractEndDate >= periodFrom).
        // Employment.IsActive wird BEWUSST IGNORIERT — der Lebenszyklus eines Vertrages
        // ist ausschliesslich durch sein Datum bestimmt. Ein eben ausgetretener MA muss
        // im Austritts-Monat noch im Lohnlauf erscheinen, auch wenn der Auto-Setter
        // IsActive=false gesetzt hat. Und ein erst diesen Monat gestarteter Vertrag
        // (z.B. 12.1.) muss greifen, selbst wenn er als „abgeschlossen" markiert ist.
        // Wenn mehrere matchen (z.B. weil eine Lohnänderung mitten in die Periode fiel),
        // nehmen wir den mit dem spätesten Vertragsbeginn.
        var emp = employee.Employments
            .Where(e => e.CompanyProfileId == companyProfileId
                     && DateOnly.FromDateTime(e.ContractStartDate) <= periodToFull
                     && (!e.ContractEndDate.HasValue
                         || DateOnly.FromDateTime(e.ContractEndDate.Value) >= periodFrom))
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefault();
        if (emp is null) return new NotFoundObjectResult(
            $"Kein in der Lohnperiode {periodFrom:dd.MM.yyyy}–{periodToFull:dd.MM.yyyy} gültiger Vertrag gefunden.");

        // ── Kurzperiode wegen Austritt UND/ODER Eintritt in der Periode ─────
        // Gesetzliche Regel CH: Arbeitsverhältnis endet auf Monatsende.
        // Wenn ContractEndDate (z.B. 31.3.) vor dem regulären Periodenende
        // (20.4.) liegt, wird der Monatslohn anteilig per Tagessatz-Formel
        // (MonthlySalary × 12 / 365 × Tage) berechnet. Prozent-basierte
        // Positionen (13. ML, Ferien, Feiertag) skalieren automatisch mit.
        // Genauso bei Mid-Period-Eintritt: Vertragsbeginn nach Periodenanfang
        // → ab Vertragsbeginn rechnen, gleiche Tages-Pro-Rata.
        DateOnly periodTo = periodToFull;
        DateOnly periodEffectiveFrom = periodFrom;     // ggf. nach hinten verschoben bei Mid-Period-Eintritt
        bool isShortPeriod = false;
        bool shortReasonStart = false;                  // Kurzperiode wg. Eintritt
        bool shortReasonEnd   = false;                  // Kurzperiode wg. Austritt
        int shortPeriodDays = normalPeriodDays;

        // Eintritt mid-period?
        var startDateOnly = DateOnly.FromDateTime(emp.ContractStartDate);
        if (startDateOnly > periodFrom)
        {
            periodEffectiveFrom = startDateOnly;
            isShortPeriod = true;
            shortReasonStart = true;
        }

        // Austritt mid-period?
        if (emp.ContractEndDate.HasValue)
        {
            var endDateOnly = DateOnly.FromDateTime(emp.ContractEndDate.Value);
            if (endDateOnly >= periodEffectiveFrom && endDateOnly < periodToFull)
            {
                periodTo = endDateOnly;
                isShortPeriod = true;
                shortReasonEnd = true;
            }
        }
        if (isShortPeriod)
        {
            shortPeriodDays = periodTo.DayNumber - periodEffectiveFrom.DayNumber + 1;
        }

        // ── Stempelzeiten laden ────────────────────────────────────────────
        var timeEntries = await _db.EmployeeTimeEntries
            .Where(t => t.EmployeeId == employeeId
                     && t.EntryDate >= periodFrom
                     && t.EntryDate <= periodTo)
            .ToListAsync();

        // ── Absenzen laden ────────────────────────────────────────────────
        var absences = await _db.Absences
            .Where(a => a.EmployeeId == employeeId
                     && a.DateFrom <= periodTo
                     && a.DateTo   >= periodFrom)
            .ToListAsync();

        // ── Absenz-Typen Konfiguration laden (Zeitgutschrift-Regeln) ─────
        var absenzTypConfig = await _db.AbsenzTypen
            .Where(t => t.Aktiv)
            .ToDictionaryAsync(t => t.Code, t => t);
        // Fallback-Konfiguration falls Tabelle noch leer (Backward-Compatibility)
        AbsenzTyp GetAbsenzTyp(string code) => absenzTypConfig.TryGetValue(code, out var t) ? t
            : new AbsenzTyp { Code = code, Zeitgutschrift = code != "FEIERTAG", GutschriftModus = code == "FERIEN" ? "1/7" : "1/5" };

        // ── Mitarbeiter-Alter berechnen (für BVG, AHV-Schwellen) ─────────────
        // Schweizer Regel: Beitragspflicht gilt ab 1.1. des Jahres, in dem das
        // massgebende Alter erreicht wird — unabhängig vom genauen Geburtstagsdatum.
        // Daher: Alter = Lohnperioden-Jahr − Geburtsjahr (kein Monats-Adjustment).
        int? employeeAge = null;
        if (employee.DateOfBirth.HasValue)
            employeeAge = year - employee.DateOfBirth.Value.Year;

        // ── AHV-21 Referenzalter (Walter-Vorgabe 09.06.2026) ─────────────────
        // Männer immer 65; Frauen gestaffelt nach Jahrgang (Übergangsgeneration
        // 1961–1963: 64J3M / 64J6M / 64J9M). Ab Erreichen des Referenzalters:
        //   • AHV: Freibetrag 1'400/Mt. (die SV-Satz-Zeile mit MinAge=65 greift)
        //   • ALV: weg
        //   • BVG/BVG_ZUSATZ: weg (Pensionierung)
        // Trick: effectiveAge wird auf 65 angehoben (falls niedriger), damit
        //   automatisch die MaxAge=64-Regeln (ALV/BVG) rausfallen und die
        //   MinAge=65-AHV-Variante greift. Zusätzlich harter Ausschluss von
        //   ALV/BVG/BVG_ZUSATZ — falls jemand die MaxAge in der DB auf >64
        //   gesetzt hat, fallen sie trotzdem weg.
        bool ueberReferenzalter = employee.DateOfBirth.HasValue
            && PayrollCalculations.HatReferenzalterErreicht(
                employee.Gender, employee.DateOfBirth.Value, year, month);
        int? effectiveAge = employeeAge;
        if (ueberReferenzalter && (effectiveAge == null || effectiveAge < 65))
            effectiveAge = 65;

        // ── Quellensteuer-Pflicht (Walter-Vorgabe 09.06.2026) ──────────────────
        // Single Source of Truth ist `QstPflichtCheckService.CheckAsync`. Dieser
        // prüft alle 5 Befreiungsgründe konsistent mit dem Lohnlauf-Block, dem
        // Dashboard und dem QST-Tab im MA-Modul:
        //   1) MA ist CH-Bürger
        //   2) MA hat C-Ausweis (am Stichtag gültig)
        //   3) MA hat Behörden-Befreiung (mit Dok + Gültigkeit)
        //   4) MA verheiratet mit CH-Bürger
        //   5) MA verheiratet mit C-Ausweis-Inhaber
        // Vorher prüfte die Engine NUR (1) und das Legacy-Feld `QuellensteuerBefreitAb`
        // — ein MA mit C-Ausweis oder Schweizer Ehepartner bekam ungerechtfertigt
        // QST abgezogen.
        // Stichtag = Periodenende: bei Eintritt mitten im Monat (z.B. 2.7.)
        // ist die QST-Erfassung oft ab Eintrittsdatum gültig — am 1. des
        // Monats noch nicht. ConfirmPayroll/Freigeben prüfen ebenfalls periodTo.
        var qstPflicht = await _qstCheck.CheckAsync(employeeId, periodTo);

        EmployeeQuellensteuer? qstEinstellung = null;
        if (qstPflicht.IsQstPflichtig)
        {
            // Überlappung mit der Lohnperiode (nicht nur gültig am 1.):
            // ValidFrom 2.7. muss im Juli-Lauf greifen (Walter 02.08.2026,
            // Ana Petkovic 580104 — sonst keine QST-Zeile trotz Erfassung).
            qstEinstellung = await _db.EmployeeQuellensteuer
                .Where(q => q.EmployeeId == employeeId
                         && q.ValidFrom <= periodTo
                         && (q.ValidTo == null || q.ValidTo >= periodFrom))
                .OrderByDescending(q => q.ValidFrom)
                .FirstOrDefaultAsync();
        }

        // QST rechnen wenn der Service sagt „pflichtig" UND ein Tarif erfasst ist.
        // Ist er pflichtig OHNE Erfassung, blockt ConfirmPayroll/Freigeben bereits
        // den Lohnlauf (QST_PFLICHT_OFFEN 409); für CalculateAsync rechnen wir hier
        // ohne QST weiter, damit die Vorschau lädt.
        bool isQuellensteuer = qstPflicht.IsQstPflichtig && qstEinstellung != null;
        if (!isQuellensteuer) qstEinstellung = null;

        // ── Abzugsregeln: ausschliesslich aus social_insurance_rate ───────────
        bool usingDefaultDeductions = false;
        var globalRates = await _db.SocialInsuranceRates
            .Where(r => r.IsActive
                     && r.ValidFrom <= periodTo
                     && (r.ValidTo == null || r.ValidTo >= periodFrom)
                     // AG-only-Sätze (Rate 0 + rate_employer gesetzt, z.B. FAK) sind
                     // KEIN AN-Abzug → nicht in die AN-Berechnung aufnehmen, sonst
                     // entstünde eine Phantom-„0.00"-Zeile im Lohnzettel. Sie werden
                     // nur im Fibu-Journal als AG-Beitrag verbucht.
                     && !(r.Rate == 0 && r.RateEmployer != null))
            .ToListAsync();

        // Deduplizieren: pro (Code + Altersband + Vertragsmodell + OnlyQst) nur
        // die Regel mit dem neuesten ValidFrom. Verhindert Doppel-Abzüge wenn
        // in der DB alte und neue Regeln mit überlappender Gültigkeit liegen
        // (z.B. Rate ab 2024 und Rate ab 2026 beide noch IsActive/ValidTo=null).
        globalRates = globalRates
            .GroupBy(r => new {
                r.Code,
                r.MinAge,
                r.MaxAge,
                r.EmploymentModelCode,
                r.OnlyQuellensteuer,
                r.BasisType
            })
            .Select(g => g.OrderByDescending(r => r.ValidFrom).First())
            .OrderBy(r => r.SortOrder)
            .ToList();

        List<DeductionRule> allRules;
        if (globalRates.Any())
        {
            allRules = globalRates.Select(r => new DeductionRule
            {
                Id                    = -r.Id,
                CompanyProfileId      = companyProfileId,
                CategoryCode          = r.Code,
                CategoryName          = r.Name,
                Name                  = r.Name,
                Type                  = "percent",
                Rate                  = r.Rate,
                RateEmployer          = r.RateEmployer,
                BasisType             = r.BasisType,
                MinAge                = r.MinAge,
                MaxAge                = r.MaxAge,
                FreibetragMonthly     = r.FreibetragMonthly,
                CoordinationDeduction = r.CoordinationDeduction,
                MaxBaseMonthly        = r.MaxBaseMonthly,
                MaxBaseFlatMonthly    = r.MaxBaseFlatMonthly,
                MinBaseMonthly        = r.MinBaseMonthly,
                EntryThresholdYearly  = r.EntryThresholdYearly,
                OnlyQuellensteuer     = r.OnlyQuellensteuer,
                EmploymentModelCode   = r.EmploymentModelCode,
                ValidFrom             = r.ValidFrom,
                SortOrder             = r.SortOrder,
                IsActive              = true,
            }).ToList();
        }
        else
        {
            allRules = BuildSwissStandardDeductions(companyProfileId);
            usingDefaultDeductions = true;
        }

        // Vertragstyp des Mitarbeiters (für EmploymentModel-spezifische Regeln,
        // z.B. wenn BVG eine andere Staffel pro Modell hätte).
        string? empModelCode = emp.EmploymentModel; // UTP | MTP | FIX | FIX-M

        // BVG-Zusatz-Mitgliedschaft am Periodenanfang (Walter-Vorgabe 26.05.2026):
        // Vorher hartcodiert über EmploymentModelCode=FIX-M; jetzt pro MA als
        // versionierte Mitgliedschaft (`employee_bvg_zusatz_member`). Mehrere
        // Einträge pro MA möglich (rein/raus/wieder rein).
        var bvgZusatzActive = await _db.EmployeeBvgZusatzMembers
            .AnyAsync(m => m.EmployeeId == employeeId
                        && m.ValidFrom <= periodFrom
                        && (m.ValidTo == null || m.ValidTo >= periodFrom));

        // Altersfilter + QST-Filter + Vertragstyp-Filter + BVG-Zusatz-Filter
        // in Memory anwenden. effectiveAge berücksichtigt das AHV-21-
        // Referenzalter; ALV/BVG/BVG_ZUSATZ werden ab Referenzalter zusätzlich
        // hart ausgeschlossen (Doppelsicherung gegen DB-Fehlkonfiguration).
        var deductions = allRules
            .Where(r => (r.MinAge == null || effectiveAge == null || effectiveAge >= r.MinAge)
                     && (r.MaxAge == null || effectiveAge == null || effectiveAge <= r.MaxAge)
                     && (!r.OnlyQuellensteuer || isQuellensteuer)
                     // Vertragstyp: NULL = gilt für alle; gesetzt = nur wenn MA-Modell übereinstimmt
                     && (r.EmploymentModelCode == null
                         || string.Equals(r.EmploymentModelCode, empModelCode,
                                          StringComparison.OrdinalIgnoreCase))
                     // BVG_ZUSATZ-Regeln NUR anwenden wenn MA am Periodenanfang
                     // Mitglied im Vorsorge-Programm ist (Walter 26.05.2026).
                     // BVG_ZUSATZ kommt aus SocialInsuranceRate.Code, der hier
                     // in DeductionRule.CategoryCode landet (siehe Mapping oben).
                     && (!string.Equals(r.CategoryCode, "BVG_ZUSATZ", StringComparison.OrdinalIgnoreCase)
                         || bvgZusatzActive)
                     // Ab Referenzalter (AHV 21) keine ALV/BVG-Pflicht mehr.
                     // AHV bleibt — die MinAge=65-Variante (Freibetrag 1'400)
                     // greift dann automatisch.
                     && (!ueberReferenzalter
                         || (!string.Equals(r.CategoryCode, "ALV",        StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(r.CategoryCode, "BVG",        StringComparison.OrdinalIgnoreCase)
                          && !string.Equals(r.CategoryCode, "BVG_ZUSATZ", StringComparison.OrdinalIgnoreCase)))
                     // UVG Art. 1a Abs. 6 (Walter-Vorgabe 09.06.2026 / 31.07.2026):
                     // FLEX mit Arbeitszeit < 8h/Woche sind von NBU befreit.
                     // Flag sitzt am Vertrag (Employment); Legacy-Fallback am MA.
                     // NBUV ist reiner AN-Abzug; BU (AG) bleibt unberührt.
                     && (!IsNbuBefreitUnter8h(emp, employee)
                         || !string.Equals(r.CategoryCode, "NBUV", StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // ── Vormonat-Saldo ─────────────────────────────────────────────────
        // Auch hier CompanyProfileId mitfiltern — sonst könnte der Vormonats-
        // Saldo aus einer anderen Filiale stammen, wenn der MA dort ebenfalls
        // einen Saldo-Eintrag hat.
        var (prevYear, prevMonth) = PrevPeriod(year, month);
        var prevSaldo = await _db.PayrollSaldos
            .FirstOrDefaultAsync(s => s.EmployeeId       == employeeId
                                   && s.PeriodYear       == prevYear
                                   && s.PeriodMonth      == prevMonth
                                   && s.CompanyProfileId == companyProfileId);
        decimal vormonatHourSaldo    = prevSaldo?.HourSaldo        ?? 0;
        decimal vormonatNachtSaldo   = prevSaldo?.NachtSaldo       ?? 0;
        decimal vormonatFerienGeld   = prevSaldo?.FerienGeldSaldo  ?? 0;
        decimal vormonatFerienTage   = prevSaldo?.FerienTageSaldo  ?? 0;
        decimal prevThirteenth       = prevSaldo?.ThirteenthMonthAccumulated ?? 0;

        // ── SALDO-VORTRAG (Migrations-Initialwerte) ────────────────────
        // Wenn keine Vorperiode existiert (= MA neu im System) UND für die
        // aktuelle Periode Vortrag-Buchungen erfasst sind, werden diese als
        // Initial-Vormonat-Saldi verwendet. Dadurch starten die Saldi mit
        // den Werten aus dem Vorsystem statt bei 0. Idempotent: bei einer
        // Neuberechnung der Migrations-Periode greifen die Vortrag-Werte
        // erneut, da prevSaldo weiterhin null ist.
        var aktuellePeriode = $"{year}-{month:D2}";
        var vortragLookup = prevSaldo == null
            ? await _db.LohnZulagen
                .Include(z => z.Lohnposition)
                .Where(z => z.EmployeeId == employeeId
                         && z.Periode == aktuellePeriode
                         && z.Lohnposition!.Kategorie == "Saldo-Vortrag")
                .ToDictionaryAsync(z => z.Lohnposition!.Code, z => z.Betrag)
            : new Dictionary<string, decimal>();
        if (vortragLookup.Count > 0)
        {
            if (vortragLookup.TryGetValue("901", out var v901)) vormonatHourSaldo  = v901;
            if (vortragLookup.TryGetValue("903", out var v903)) vormonatFerienTage = v903;
            if (vortragLookup.TryGetValue("904", out var v904)) vormonatNachtSaldo = v904;
            if (vortragLookup.TryGetValue("905", out var v905)) vormonatFerienGeld = v905;
            if (vortragLookup.TryGetValue("906", out var v906)) prevThirteenth     = v906;
            // 902 (Feiertag-Tage-Saldo) wird weiter unten injiziert,
            // direkt vor der Feiertag-Saldo-Berechnung — siehe dort.
        }

        // ── KTG/UVG: Krankheit + Unfall Absenzen laden ─────────────────────────
        var krankAbsenzen  = absences.Where(a => a.AbsenceType == "KRANK").ToList();
        var unfallAbsenzen = absences.Where(a => a.AbsenceType == "UNFALL").ToList();

        // Karenz-Saldo für diesen MA in seinem aktuellen Arbeitsjahr
        // (Legacy: die echte Karenz-Grenze kommt aus CompanyProfile.KarenzTageMax
        // und wird im KarenzService angewendet — diese Werte sind nur noch für
        // das alte Arbeitsjahr-Anker-Datum nötig.)
        decimal karenztageVerbraucht = 0;
        DateOnly arbeitsjahrVon = periodFrom, arbeitsjahrBis = periodFrom;
        if (krankAbsenzen.Any() && employee.EntryDate.HasValue)
        {
            var hired = DateOnly.FromDateTime(employee.EntryDate.Value);
            int yd = periodFrom.Year - hired.Year;
            if (new DateOnly(periodFrom.Year, hired.Month, hired.Day) > periodFrom) yd--;
            arbeitsjahrVon = new DateOnly(hired.Year + yd, hired.Month, hired.Day);
            arbeitsjahrBis = arbeitsjahrVon.AddYears(1).AddDays(-1);
            var ks = await _db.KrankheitKarenzSaldos
                .FirstOrDefaultAsync(k => k.EmployeeId == employeeId
                                       && k.ArbeitsjährVon == arbeitsjahrVon);
            karenztageVerbraucht = ks?.KarenztageUsed ?? 0;
        }

        // ── Berechnung je Modell ───────────────────────────────────────────
        var isMTP = emp.EmploymentModel == "MTP";
        var isUTP = emp.EmploymentModel == "FLEX";
        var isFIX = emp.EmploymentModel is "FIX" or "FIX-M";

        decimal hourlyRate    = emp.HourlyRate      ?? 0;
        // Walter-Vorgabe 06.06.2026 (Stufe 1b): Ferien %, Feiertag %, 13. ML %
        // kommen ab jetzt AUSSCHLIESSLICH aus der Filiale. Vertragsfelder wurden
        // entfernt. Ferien-% wird unten altersaware auf 6 Wochen hochgesetzt.
        decimal vacationPct   = company.DefaultVacationPercent5Weeks    ?? 0;
        decimal holidayPct    = company.DefaultHolidayPercent           ?? 0;
        decimal thirteenthPct = company.DefaultThirteenthSalaryPercent  ?? 0;

        // ── Probezeit-Sperre für 13. ML (L-GAV Art. 12 Ziffer 2) ───────────
        // Während der Probezeit: akkumulieren, nicht auszahlen.
        // Am Periodenende bestanden (ProbezeitEnde == Periodenende) → für
        // diesen Lohn freigeben / nachzahlen. Verfall NUR wenn Austritt in
        // dieser Periode und Austritt ≤ ProbezeitEnde — befristetes Ende
        // NACH der Probezeit lässt den Saldo stehen (Walter 01.08.2026).
        DateOnly? probationEnd13 = emp.ProbationEndDate.HasValue
            ? DateOnly.FromDateTime(emp.ProbationEndDate.Value) : null;
        DateOnly? austritt13 = ResolveAustrittDate(employee.ExitDate, emp.ContractEndDate);
        var (isInProbation, thirteenthForfeited) = ResolveThirteenthProbationStatus(
            probationEnd13, austritt13, periodFrom, periodToFull);
        // FLEX-Saldi-Zeile zeigen während Probezeit, im Bestands-Monat
        // (ProbezeitEnde in dieser Periode) und bei Verfall — auch wenn 0.00
        // (sonst «sehe den 13. Saldo nicht», Walter 02.08.2026).
        bool probationEndsThisPeriod = probationEnd13.HasValue
            && probationEnd13.Value >= periodFrom
            && probationEnd13.Value <= periodToFull;
        bool showFlexThirteenthSaldo = isUTP
            && (isInProbation || thirteenthForfeited || probationEndsThisPeriod);

        // ── Ferien-% Auto-Upgrade ab definierter Alters-Schwelle (CH-GAV-Standard 50) ──
        // Mitarbeiter ab vollendetem Lebensjahr X (Walter-Vorgabe 06.06.2026:
        // pro Filiale konfigurierbar in company.VacationSixWeeksFromAge, Default 50)
        // haben Anspruch auf 6 Wochen Ferien. Wir prüfen tag-genau: Sobald der
        // X-te Geburtstag innerhalb oder vor der aktuellen Lohnperiode liegt
        // (≤ periodTo), gilt der 6-Wochen-Satz für diese und alle Folgeperioden.
        // Beispiel mit Schwelle 50: Geboren 15.5.1976 → 50. Geburtstag 15.5.2026.
        //   Periode 21.4.-20.5.2026: 50. Geb. liegt in Periode → 6 Wochen ✓
        //   Periode 21.3.-20.4.2026: 50. Geb. nach periodTo → noch 5 Wochen
        // Wir ziehen NIE runter — wenn der Vertrag z.B. 15% (7 Wochen) hat,
        // bleibt das so.
        if (employee.DateOfBirth.HasValue)
        {
            var dob = DateOnly.FromDateTime(employee.DateOfBirth.Value);
            int sixWeeksFromAge = company.VacationSixWeeksFromAge;
            var sechsWochenSchwelle = dob.AddYears(sixWeeksFromAge);
            if (sechsWochenSchwelle <= periodTo)
            {
                decimal sechsWochenPct = company.DefaultVacationPercent6Weeks ?? 13.04m;
                if (vacationPct < sechsWochenPct)
                    vacationPct = sechsWochenPct;
            }
        }

        // Tatsächlich gestempelte Stunden (exkl. NACHT_KOMP-Gutschriften)
        decimal workedHours = timeEntries.Sum(t => t.TotalHours ?? 0);

        // Nachtstunden dieser Periode
        decimal nightHours = timeEntries.Sum(t => t.NightHours ?? 0);

        // Nacht-Zeitzuschlag: 10% der Nachtstunden → Saldo-Zuwachs
        // Exakt — Rundung erst beim Nacht-Saldo / Anzeige (Walter 31.07.2026)
        decimal nightBonus = nightHours * 0.10m;

        // Absenz-Buckets
        decimal absenzGutschrift      = 0;   // Zeitgutschrift auf Stunden-Saldo (FIX/MTP)
        decimal feiertagStunden       = 0;   // ausbezahlte Feiertage (MTP)
        decimal nachtKompStunden      = 0;   // reduzieren Nacht-Saldo
        decimal utpAuszahlungStunden  = 0;   // UTP: als Stundenlohn auszahlen (z. B. NACHT_KOMP)
        decimal ferienStundenMtp      = 0;   // MTP: Ferien-Anteil für 10.6-Lohnzeile separat ausweisen

        // Aufschlüsselung der Absenz-Stunden pro AbsenceType — für die
        // Anzeige im Lohnzettel ("gestempelt 168.71 + Krank 8.4 + Feiertag
        // 16.8 + …"). Enthält nur Zeitgutschrift-Anteile, keine
        // Feiertag/UTP-Auszahlung-Anteile (die sind im normalen Lohn-Block
        // separat).
        var absenzBreakdown = new Dictionary<string, decimal>();
        void AddBreakdown(string type, decimal hours)
        {
            if (hours <= 0) return;
            absenzBreakdown[type] = (absenzBreakdown.TryGetValue(type, out var prev) ? prev : 0m) + hours;
        }

        // Helper: berechne Zeitgutschrift dynamisch aus den AbsenzTyp-Regeln
        // statt aus dem gespeicherten HoursCredited. So passen sich historische
        // Absenzen automatisch an Regeländerungen an (z.B. Wochensoll für
        // FIX/FIX-M Ferien/Feiertag pensum-adjustiert).
        //
        // Walter 31.07.2026: EXAKT zurückgeben — keine Zwischenrundung.
        // Runden erst am Schluss (Anzeige / CHF / Saldo-Felder).
        decimal ComputeAbsenzHours(Absence a, AbsenzTyp typCfg)
        {
            int daysInPeriod = CountAbsenceDaysInPeriod(a, periodFrom, periodTo);
            if (daysInPeriod == 0) return 0;

            decimal betriebWeekly = company.NormalWeeklyHours ?? 42m;
            decimal pct           = emp.EmploymentPercentage ?? 100m;
            decimal weeklyH       = betriebWeekly;

            // Walter-Vorgabe 30.05.2026 (override): bei MTP IMMER die garantierten
            // Wochenstunden als Basis — unabhängig vom AbsenzTyp-Setting BasisStunden.
            // MTP ist konzeptionell ein Stundenlöhner mit Garantie; bei
            // Krank/Unfall-Tagen gilt die Garantie als Lohn-Maßstab, nicht
            // die Betriebs-Wochenstunden. Sonst würde z.B. ein MA mit 25 h
            // Garantie fälschlich 42 h/5 = 8.40 h pro Krank-Tag bekommen
            // statt 25 h/5 = 5 h.
            if (emp.EmploymentModel == "MTP")
            {
                weeklyH = emp.GuaranteedHoursPerWeek
                       ?? emp.WeeklyHours
                       ?? betriebWeekly;
            }
            else if (typCfg.BasisStunden == "VERTRAG")
            {
                if (emp.EmploymentModel == "FIX" || emp.EmploymentModel == "FIX-M")
                {
                    // Walter-Regel: FIX/FIX-M nur bei FERIEN/FEIERTAG pensum-adjustiert
                    // (1/7-Modus). Krank/Unfall/Schulung etc. weiter Betriebs-Wochen.
                    if (a.AbsenceType == "FERIEN" || a.AbsenceType == "FEIERTAG")
                    {
                        // Pensum-adjustierte Wochensoll (exakt; Rundung erst am Schluss)
                        weeklyH = betriebWeekly * pct / 100m;
                    }
                    // sonst: weeklyH = betriebWeekly (Default)
                }
                // UTP: bleibt auf betriebWeekly
            }

            string modus = typCfg.GutschriftModus ?? "1/5";
            decimal divisor = modus == "1/7" ? 7m : 5m;
            decimal prozent = a.Prozent > 0 ? a.Prozent : 100m;
            return daysInPeriod * weeklyH / divisor * prozent / 100m;
        }

        foreach (var a in absences)
        {
            var typCfg = GetAbsenzTyp(a.AbsenceType);

            // Stunden dynamisch berechnen (statt aus a.HoursCredited).
            // Damit sind alte Datensätze automatisch konsistent mit den neuen
            // Regeln, sobald Walter eine AbsenzTyp-Konfig anpasst.
            decimal hours = ComputeAbsenzHours(a, typCfg);
            if (hours <= 0) continue;

            // 1) Saldo-Reduktion (flag-basiert statt hart verdrahtet)
            //    FERIEN_TAGE wird separat aus ferienTageGenommen gezählt.
            if (typCfg.ReduziertSaldo == "NACHT_STUNDEN")
                nachtKompStunden += hours;

            // 2) Wohin fliessen die Stunden?
            if (a.AbsenceType == "UNBEZ_URLAUB")
            {
                // Unbezahlter Urlaub (Walter-Vorgabe 27.06.2026): KEINE
                // Zeitgutschrift und KEINE Auszahlung — darf NICHT in
                // feiertagStunden landen, sonst würde er fälschlich ausbezahlt.
                // Die Festlohn-/Sollstunden-Kürzung passiert pro Modell weiter
                // unten (FIX/FIX-M: Festlohn-Split per Tagessatz; MTP: garantierte
                // Soll-Stunden runter), analog FERIEN. UTP: keine Wirkung
                // (ungestempelt = unbezahlt). Hier nur fürs Lohnzettel-Breakdown /
                // die Bemerkung tracken.
                AddBreakdown(a.AbsenceType, hours);
            }
            else if (isUTP)
            {
                // UTP: nur wenn der Typ explizit UTP-Auszahlung aktiviert hat
                // (heute: NACHT_KOMP). Sonst keine automatische Wirkung.
                if (typCfg.UtpAuszahlung)
                    utpAuszahlungStunden += hours;
            }
            else if (isMTP && a.AbsenceType == "FERIEN")
            {
                // MTP + FERIEN: KEINE Zeitgutschrift (Walter-Regel).
                // Die eigentliche Verarbeitung passiert im MTP-Block weiter
                // unten:
                //   - Sollstunden werden um die Ferientage (× GuarH/7) reduziert,
                //   - Festlohn (10.5) wird per Tagessatz-Formel gekürzt,
                //   - Auszahlung (10.6) erfolgt aus FerienGeldSaldo.
                // Hier nur zusätzlich das Stunden-Äquivalent tracken, damit
                // MTP-Block darauf zugreifen kann (Backward-Compat mit alten
                // Absenzen, bei denen HoursCredited noch mit Zeitgutschrift
                // gefüllt war — neue Absenzen senden HoursCredited=0).
                ferienStundenMtp += hours;
                // ACHTUNG: absenzGutschrift NICHT mehr addieren.
            }
            else if (a.AbsenceType == "FEIERTAG" && isFIX)
            {
                // FIX/FIX-M: Feiertage sind durch den Monatslohn abgedeckt.
                // → als normale Gutschrift zählen, damit der Stunden-Saldo nicht negativ wird.
                absenzGutschrift += hours;
                AddBreakdown(a.AbsenceType, hours);
            }
            else if (a.AbsenceType == "FEIERTAG" || !typCfg.Zeitgutschrift)
            {
                // Feiertag (ausbezahlt) oder Typ ohne Zeitgutschrift (MTP): separat ausbezahlen.
                feiertagStunden += hours;
            }
            else if (typCfg.Zeitgutschrift)
            {
                // Alle anderen Typen mit Zeitgutschrift (KRANK, UNFALL, SCHULUNG, MILITAER etc.)
                // Walter-Vorgabe 30.05.2026: bei MTP werden KRANK und UNFALL
                // NICHT als Zeitgutschrift gezählt — die Stunden werden im MTP-
                // Block direkt von den Soll-Stunden abgezogen (festlohnKrank/
                // UnfallKuerzung mit MTP-Tagessatz). Würden sie zusätzlich in
                // absenzGutschrift landen, entstünden Phantom-Mehrstunden in der
                // „MTP + Stunden"-Zeile. AddBreakdown bleibt drin, weil der
                // Krank/Unfall-Block die Karenz/Taggeld-Tage daraus berechnet.
                bool mtpKrankOderUnfall = isMTP && (a.AbsenceType == "KRANK" || a.AbsenceType == "UNFALL");
                if (!mtpKrankOderUnfall) absenzGutschrift += hours;
                AddBreakdown(a.AbsenceType, hours);
            }
        }

        // ── Ferien-Tage-Saldo (alle Modelle) ──────────────────────────────
        // 5 Wochen = 35 Tage/Jahr (vacationPct < ~12.5%)
        // 6 Wochen = 42 Tage/Jahr (vacationPct >= 12.5%)
        int     vacationWeeks       = vacationPct >= 12.5m ? 6 : 5;
        decimal annualFerienTage    = vacationWeeks * 7m;
        decimal ferienTageAccrual   = Math.Round(annualFerienTage / 12m, 4); // monatliche Gutschrift

        // Walter-Vorgabe 27.06.2026: Bei UNBEZAHLTEM URLAUB wird der Ferien-
        // ANSPRUCH in TAGEN für ALLE Modelle anteilig gekürzt — während des
        // unbezahlten Urlaubs entsteht kein Ferienanspruch. Es wird NUR die
        // Tage-Gutschrift reduziert, NICHT das Ferien-Geld (CHF, separate
        // %-Rechnung). Anteil = UU-Kalendertage / Kalendertage der Periode.
        decimal unbezUrlaubTageFerien = absences
            .Where(a => a.AbsenceType == "UNBEZ_URLAUB")
            .Sum(a => (decimal)CountAbsenceDaysInPeriod(a, periodFrom, periodTo));
        if (unbezUrlaubTageFerien > 0)
        {
            // 365tel-Basis (Walter-Vorgabe 27.06.2026, konsistent mit dem
            // Tagessatz 12/365): pro UU-Kalendertag entfällt Jahresanspruch/365
            // an Ferienanspruch. Beispiel 5 Wochen = 35 Tage/Jahr → 35/365 × UU-Tage.
            decimal ferienKuerzungUu = annualFerienTage / 365m * unbezUrlaubTageFerien;
            ferienTageAccrual = Math.Round(Math.Max(0m, ferienTageAccrual - ferienKuerzungUu), 4);
        }

        // Tatsächlich bezogene Ferientage aus FERIEN-Absenzen — nur Tage in
        // der aktuellen Lohnperiode zählen (Absenzen können sich über mehrere
        // Perioden erstrecken).
        decimal ferienTageGenommen = 0;
        foreach (var a in absences.Where(x => x.AbsenceType == "FERIEN"))
        {
            ferienTageGenommen += CountAbsenceDaysInPeriod(a, periodFrom, periodTo);
        }
        decimal ferienTageSaldoNeu = Math.Round(vormonatFerienTage + ferienTageAccrual - ferienTageGenommen, 4);

        // ── Ferienanspruch-Kürzungs-Vorschlag (Art. 329b OR) ──────────────
        // Berechnet kumulierte Abwesenheits-Tage pro Dienstjahr und schlägt
        // ggfs. eine Kürzung vor (1/12 pro vollem Monat über Schwellwert).
        // Operator entscheidet pro Lohnabrechnung ob anwenden.
        var kuerzungVorschlag = await _ferienKuerzung.CalculateAsync(employeeId, periodTo);
        // Wenn vorhandener Saldo bereits eine angewendete Kürzung enthält
        // (aus früherer Periode), wird sie nicht erneut abgezogen — der
        // ferienTageSaldoNeu hat sie schon drin (über vormonatFerienTage).
        decimal kuerzungVorschlagTage = kuerzungVorschlag.HasKuerzungVorschlag
            ? Math.Round(kuerzungVorschlag.TotalKuerzung12tel * (vacationWeeks * 7m) / 12m, 2)
            : 0m;

        // ── Feiertag-Tage-Saldo (nur FIX / FIX-M) ─────────────────────────
        // Monatliche Gutschrift: +0.5 Tage (fix); Abzug bei FEIERTAG-Absenz
        // anteilig nach Prozent (100% → 1 Tag, 50% → 0.5 Tag). Andere Modelle:
        // Feld bleibt 0 — sie bekommen Feiertage separat über 50.1/MTP-Logik.
        //
        // Bei FIX/FIX-M wird der Feiertag-Saldo NICHT in Geld ausbezahlt,
        // sondern muss als Tage bezogen werden (analog Ferien-Tage-Saldo).
        // Daher kein Auszahlungs-Mechanismus, nur Saldo-Tracking.
        //
        // Vortrag-Injection: bei Migration aus Vorsystem wird der Anfangs-
        // Saldo aus Lohnposition 902 ("Vortrag Feiertag-Saldo (Tage)") als
        // initialer Vormonat verwendet, sofern noch kein PayrollSaldo der
        // Vorperiode existiert. Greift nur bei FIX/FIX-M, da andere
        // Vertragsmodelle das Feld nicht nutzen.
        decimal vormonatFeiertagTage   = prevSaldo?.FeiertagTageSaldo ?? 0m;
        if (prevSaldo == null && vortragLookup.TryGetValue("902", out var v902))
            vormonatFeiertagTage = v902;
        decimal feiertagTageAccrual    = 0m;
        decimal feiertagTageGenommen   = 0m;
        if (isFIX)
        {
            feiertagTageAccrual = 0.5m;
            // UU-Kürzung auf 365tel-Basis (Walter-Vorgabe 27.06.2026): der
            // Jahres-Feiertaganspruch (0.5/Mt × 12 = 6 Tage) / 365 × UU-Tage
            // entfällt — gleiche Logik wie bei den Ferien-Tagen.
            if (unbezUrlaubTageFerien > 0)
            {
                decimal feiertagKuerzungUu = (0.5m * 12m) / 365m * unbezUrlaubTageFerien;
                feiertagTageAccrual = Math.Round(Math.Max(0m, feiertagTageAccrual - feiertagKuerzungUu), 4);
            }
            foreach (var a in absences.Where(x => x.AbsenceType == "FEIERTAG"))
            {
                decimal prozent = a.Prozent > 0 ? a.Prozent : 100m;
                int tageInPeriode = CountAbsenceDaysInPeriod(a, periodFrom, periodTo);
                feiertagTageGenommen += tageInPeriode * (prozent / 100m);
            }
        }
        decimal feiertagTageSaldoNeu = Math.Round(
            vormonatFeiertagTage + feiertagTageAccrual - feiertagTageGenommen, 4);

        // ── Ferien-Geld-Saldo (nur UTP + MTP) ─────────────────────────────
        // Ferienentschädigung wird NICHT monatlich ausbezahlt, sondern akkumuliert.
        // Bei Ferienbezug: proportionale Auszahlung (Tage genommen / Saldo vorher)
        decimal ferienGeldSaldoNeu   = vormonatFerienGeld;  // wird unten angepasst
        decimal ferienGeldAuszahlung = 0;
        // Wird nach Modell-Berechnung gesetzt (ferienEnt ist modellabhängig)


        // ── L-GAV-Jahresbeitrag: automatisch einfügen wenn fällig ─────────
        // Idempotent — erzeugt pro MA/Jahr maximal einen Eintrag auf
        // Lohnposition 600.24. Wird VOR dem Laden der Zulagen aufgerufen
        // damit der neu angelegte Abzug in dieser Periode mit berechnet wird.
        await _lgav.EnsureAsync(employee, emp, company, year, month, periodFrom, periodTo);

        // Uniformen-Depot CHF 50 beim 1. Lohn (Walter Aug 2026) — idempotent,
        // schreibt LohnZulage 600.32 + employee_uniform_depot vor dem Laden.
        await _uniformDepot.EnsureChargeAsync(employee, year, month);

        // ── Zulagen & Abzüge für diese Periode laden ──────────────────────
        // Einmalige Einträge (manuell pro Periode erfasst) + wiederkehrende
        // Einträge (Mitarbeiter-Stammdaten) werden zusammengeführt und gleich
        // behandelt. Wiederkehrende liefern eine "synthetische" LohnZulage
        // in-memory (ohne DB-Eintrag), damit die bestehende Berechnungslogik
        // unverändert bleibt.
        string periodeStr = $"{year:D4}-{month:D2}";
        var einmaligeZulagen = await _db.LohnZulagen
            .Include(z => z.Lohnposition)
            .Where(z => z.EmployeeId == employeeId && z.Periode == periodeStr)
            .OrderBy(z => z.Lohnposition!.SortOrder)
            .ThenBy(z => z.CreatedAt)
            .ToListAsync();

        // Wiederkehrende Einträge, die in diese Periode fallen
        // (Überlappung von [valid_from, valid_to] mit [periodFrom, periodTo])
        var wiederkehrendeRaw = await _db.EmployeeRecurringWages
            .Include(r => r.Lohnposition)
            .Where(r => r.EmployeeId == employeeId
                     && r.ValidFrom <= periodTo
                     && (r.ValidTo == null || r.ValidTo >= periodFrom))
            .OrderBy(r => r.Lohnposition!.SortOrder)
            .ThenBy(r => r.CreatedAt)
            .ToListAsync();

        // ── Familienzulagen (FAK) als synthetische Einträge ─────────────────
        // Walter-Anforderung: Familienzulagen automatisch auf den Lohn, sobald
        // pro Kind in family_member_allowance ein Eintrag mit MonthlyAmount
        // hinterlegt ist. Auszahlung bleibt streng manuell — ohne Eintrag mit
        // ValidFrom/ValidTo wird nichts ausgezahlt (FAK-Entscheid abwarten).
        //
        // SV-Behandlung steckt in den Lohnpositionen 190.1/190.2 (siehe
        // add_familienzulagen_lohnpositionen.sql): nicht AHV/ALV/NBU/KTG/BVG-
        // pflichtig, aber QST-pflichtig.
        //
        // Mindesteinkommen-Check (GastroSocial-Bedingung): wenn der MA in
        // diesem Monat unter dem AHV-pflichtigen Mindesteinkommen bleibt
        // (LU = 630 CHF), wird die FAK NICHT ausgezahlt — Lohnzeile bleibt
        // mit Betrag 0 und Hinweistext drin, damit Walter den Anspruch
        // weiterhin sieht und manuell nachzahlen kann wenn der Brutto in
        // einem Folgemonat wieder erreicht wird.
        //
        // Pro-Rata: aktuell voller Monatsbetrag wenn der Allowance-Eintrag
        // irgendwann in der Periode aktiv war. Mid-Period-Tarifwechsel
        // (z.B. Kind wird 12 in LU) → Walter legt zwei Einträge an, beide
        // zählen anteilig. Eine Tagesgenaue Aufteilung machen wir später.
        // Walter-Vorgabe 28.05.2026: a.MonthlyAmount wird NICHT mehr als Filter
        // verwendet — der Betrag kommt nun pro Periode aus dem FAK-Tarif (siehe
        // Resolve-Logik unten). Die DB-Spalte bleibt als Snapshot/Audit-Wert
        // stehen, ist aber nicht mehr authoritative für den Lohnlauf.
        var familienzulagenRaw = await (
            from a in _db.FamilyMemberAllowances
            join m in _db.EmployeeFamilyMembers on a.FamilyMemberId equals m.Id
            where m.EmployeeId == employeeId
               && a.ValidFrom <= periodTo
               && (a.ValidTo == null || a.ValidTo >= periodFrom)
            select new {
                AllowanceId    = a.Id,
                MonthlyAmount  = a.MonthlyAmount,    // Snapshot zum ValidFrom — Fallback wenn kein Tarif
                AllowanceType  = a.AllowanceType,    // "KZ" | "AZ" | "GZ" | "AdoptZ" | NULL
                TarifSatzNr    = a.TarifSatzNr,      // 1, 2, oder NULL (Pauschal/Alt-Daten)
                ValidFrom      = a.ValidFrom,
                ValidTo        = a.ValidTo,
                Note           = a.Note,
                ChildFirstName = m.FirstName,
                ChildLastName  = m.LastName,
                ChildBirth     = m.DateOfBirth,
                CreatedAt      = a.CreatedAt
            }
        ).ToListAsync();

        // Lohnpositionen 190.1 (KZ) / 190.2 (AZ) / 190.3 (GZ+AdoptZ) holen —
        // falls eine Migration nicht ausgeführt wurde, fällt die jeweilige
        // FAK-Art still aus (kein Crash, aber auch keine Lohnzeile).
        var lpKz = await _db.Lohnpositionen.FirstOrDefaultAsync(l => l.Code == "190.1" && l.IsActive);
        var lpAz = await _db.Lohnpositionen.FirstOrDefaultAsync(l => l.Code == "190.2" && l.IsActive);
        var lpGz = await _db.Lohnpositionen.FirstOrDefaultAsync(l => l.Code == "190.3" && l.IsActive);

        // ── Mindesteinkommen-Check ─────────────────────────────────────────
        // Tarif für die Filiale (Standort-Kanton) zur Periode laden und
        // approximativen AHV-Brutto schätzen (Vertrag-Festlohn bzw. effektiv
        // gestempelte Stunden × Stundenlohn bei UTP). Wenn Schwelle
        // unterschritten → fakSuppressed=true, Synthetics werden mit
        // Betrag 0 und Hinweistext erstellt (statt komplett ausgelassen).
        FamilienzulagenTarif? fakTarif = null;
        if (familienzulagenRaw.Count > 0 && !string.IsNullOrWhiteSpace(company.KantonCode))
        {
            fakTarif = await _db.FamilienzulagenTarife
                .Where(t => t.IsActive
                         && t.KantonCode == company.KantonCode
                         && t.ValidFrom <= periodTo
                         && (t.ValidTo == null || t.ValidTo >= periodFrom))
                .OrderByDescending(t => t.ValidFrom)
                .FirstOrDefaultAsync();
        }

        // Schwellwert: bevorzugt Monats-Wert, sonst Jahres-Wert / 12
        decimal? mindesteinkommenMonatThreshold = fakTarif?.MindesterwerbseinkommenMonat
            ?? (fakTarif?.MindesterwerbseinkommenJahr.HasValue == true
                ? fakTarif.MindesterwerbseinkommenJahr!.Value / 12m
                : (decimal?)null);

        // Approximation des AHV-Brutto für den Check.
        // UTP / MTP (Stundenlohn-Modelle): workedHours × hourlyRate.
        //   Wichtig: MTP hat MonthlySalary = null — dort darf auf KEINEN Fall
        //   der Monatslohn als Basis genommen werden (sonst greift fälschlich
        //   die FAK-Sperre auch bei normal arbeitenden MTP-MA).
        // FIX / FIX-M: vertraglicher Monatslohn.
        // Walter-Vorgabe 30.05.2026: bei MTP wird die FAK-Mindesteinkommen-Prüfung
        // gegen den GARANTIERTEN Lohn der Periode geprüft (guaranteedH/7 × Tage
        // × StdLohn) statt nur gegen die tatsächlich gestempelten Stunden. Sonst
        // fällt ein bei Krank/Unfall ausgefallener MA fälschlich unter die
        // Schwelle, obwohl er aus Krank-Taggeld + Garantie genug Einkommen
        // hat. Wenn die effektive Arbeit über der Garantie liegt (Mehrstunden),
        // greift der höhere Wert (max).
        decimal estimatedAhvBruttoForFak;
        if (isFIX)
            estimatedAhvBruttoForFak = emp.MonthlySalary ?? 0m;
        else if (isMTP)
        {
            decimal guarH = emp.GuaranteedHoursPerWeek ?? 0m;
            // Exakt für Schwellen-Vergleich — keine Zwischenrundung
            decimal sollLohn = guarH / 7m * normalPeriodDays * hourlyRate;
            decimal istLohn  = workedHours * hourlyRate;
            estimatedAhvBruttoForFak = Math.Max(sollLohn, istLohn);
        }
        else
            estimatedAhvBruttoForFak = workedHours * hourlyRate;

        bool fakSuppressed = mindesteinkommenMonatThreshold.HasValue
                          && familienzulagenRaw.Count > 0
                          && estimatedAhvBruttoForFak < mindesteinkommenMonatThreshold.Value;

        var familienzulagenSynth = new List<LohnZulage>();
        foreach (var fa in familienzulagenRaw)
        {
            // AllowanceType-Auflösung:
            //   "GZ" / "AdoptZ"  → einmalige Geburts-/Adoptionszulage (190.3)
            //   "AZ"             → Ausbildungszulage (190.2)
            //   "KZ" / NULL      → Kinderzulage (190.1)
            //   bei NULL und Kind ≥16 J. → AZ-Heuristik
            bool istGz = string.Equals(fa.AllowanceType, "GZ",     StringComparison.OrdinalIgnoreCase)
                      || string.Equals(fa.AllowanceType, "AdoptZ", StringComparison.OrdinalIgnoreCase);
            bool istAz = !istGz && string.Equals(fa.AllowanceType, "AZ", StringComparison.OrdinalIgnoreCase);
            if (!istGz && string.IsNullOrWhiteSpace(fa.AllowanceType) && fa.ChildBirth.HasValue)
            {
                var ageAtPeriodEnd = periodTo.Year - fa.ChildBirth.Value.Year
                    - (periodTo.Month < fa.ChildBirth.Value.Month
                       || (periodTo.Month == fa.ChildBirth.Value.Month && periodTo.Day < fa.ChildBirth.Value.Day) ? 1 : 0);
                if (ageAtPeriodEnd >= 16) istAz = true;
            }

            Lohnposition? lp;
            if (istGz)      lp = lpGz;
            else if (istAz) lp = lpAz;
            else            lp = lpKz;
            if (lp == null) continue;   // Migration nicht ausgeführt → still überspringen

            // Bemerkung: Kindesname + Alter zur Periode (z.B. "Arman, 15 J.")
            // Bei GZ/AdoptZ explizit den Zulagen-Typ voranstellen.
            var name = string.Join(" ",
                new[] { fa.ChildFirstName, fa.ChildLastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            string ageSuffix = "";
            if (fa.ChildBirth.HasValue)
            {
                var age = periodTo.Year - fa.ChildBirth.Value.Year
                    - (periodTo.Month < fa.ChildBirth.Value.Month
                       || (periodTo.Month == fa.ChildBirth.Value.Month && periodTo.Day < fa.ChildBirth.Value.Day) ? 1 : 0);
                ageSuffix = $", {age} J.";
            }

            string bemerkung;
            bool istAdoptZ = string.Equals(fa.AllowanceType, "AdoptZ", StringComparison.OrdinalIgnoreCase);
            if (istGz)
            {
                // 190.3 ist eine Sammelposition für Geburt + Adoption — der
                // konkrete Anlass steht in der Bemerkung.
                string anlass = istAdoptZ ? "Adoption" : "Geburt";
                bemerkung = !string.IsNullOrWhiteSpace(name) ? $"{anlass} {name}{ageSuffix}" : anlass;
            }
            else if (!string.IsNullOrWhiteSpace(name))
            {
                bemerkung = $"{name}{ageSuffix}";
            }
            else
            {
                bemerkung = istAz ? "Ausbildungszulage" : "Kinderzulage";
            }

            // Walter-Vorgabe 28.05.2026 (v3): User wählt pro Kind den KONKRETEN
            // Tarif-Satz (KZ Satz 1, KZ Satz 2 ab 12J., AZ Satz 1, …) mit
            // eigenem von/bis-Zeitfenster. Engine schaut pro Periode:
            //   1. welcher Allowance-Eintrag ist gültig (über ValidFrom/To)
            //   2. welcher Satz ist gewählt (TarifSatzNr)
            //   3. holt den AKTUELL gültigen Wert aus dem FAK-Tarif der Filiale.
            // Bei Tarif-Wechsel (z.B. neue Sätze ab 1.1.2026) greift der neue
            // Betrag automatisch — Walter muss am Kind nichts ändern.
            //
            // Fallback: kein Tarif-Satz hinterlegt → gespeicherter fa.MonthlyAmount
            // (Backward-Compat für Alt-Einträge ohne tarif_satz_nr).
            string? typeForResolve = istGz
                ? (istAdoptZ ? "AdoptZ" : "GZ")
                : (istAz ? "AZ" : "KZ");
            var resolved = FamilienzulagenResolverService.ResolveBySatz(fakTarif, typeForResolve, fa.TarifSatzNr);

            decimal betrag;
            if (fakSuppressed)
            {
                betrag = 0m;
                bemerkung += " – Lohn zu tief";
            }
            else if (resolved.Amount.HasValue)
            {
                betrag = Math.Round(resolved.Amount.Value, 2);
            }
            else
            {
                // Fallback: kein Tarif-Satz hinterlegt → gespeicherten Wert nehmen
                // (Backward-Compat für Alt-Einträge ohne Tarif-Pflege).
                betrag = Math.Round(fa.MonthlyAmount, 2);
            }
            // Synthetic-Zeile mit 0 nur ausgeben, wenn auch wirklich etwas hätte
            // anfallen sollen — sonst keine Phantom-Zeile.
            if (betrag == 0m && !fakSuppressed && fa.MonthlyAmount == 0m)
            {
                continue;
            }

            familienzulagenSynth.Add(new LohnZulage
            {
                Id             = -1_000_000 - fa.AllowanceId, // grosser Negativ-Bereich → kein Konflikt mit RecurringWage-IDs
                EmployeeId     = employeeId,
                Periode        = periodeStr,
                LohnpositionId = lp.Id,
                Lohnposition   = lp,
                Betrag         = betrag,
                Bemerkung      = bemerkung,
                CreatedAt      = fa.CreatedAt
            });
        }

        var zulagenEntries = einmaligeZulagen
            .Concat(wiederkehrendeRaw.Select(r => new LohnZulage
            {
                Id             = -r.Id,            // negative ID kennzeichnet "virtuell"
                EmployeeId     = r.EmployeeId,
                Periode        = periodeStr,
                LohnpositionId = r.LohnpositionId,
                Lohnposition   = r.Lohnposition,
                Betrag         = r.Betrag,
                Bemerkung      = r.Bemerkung,
                CreatedAt      = r.CreatedAt
            }))
            .Concat(familienzulagenSynth)
            .OrderBy(z => z.Lohnposition!.SortOrder)
            .ThenBy(z => z.CreatedAt)
            .ToList();

        // ── Lohnabtretungen (Lohnpfändung / Sozialamt) laden ─────────────
        // Aktive Zuweisungen für diesen Mitarbeiter im Perioden-Zeitraum.
        // Werden nach Netto vom Lohn abgezogen.
        // Nur mit verknüpftem Dokument gültig (Walter 02.08.2026) —
        // verhindert Lohn-Abzweig ohne Beleg.
        var lohnAssignments = await _db.EmployeeLohnAssignments
            .Include(la => la.Behoerde!)
                .ThenInclude(b => b.KontoinhaberBehoerde)
            .Where(la => la.EmployeeId == employeeId
                      && la.DokumentId != null
                      && la.ValidFrom <= periodTo
                      && (la.ValidTo == null || la.ValidTo >= periodFrom))
            .OrderBy(la => la.ValidFrom)
            .ThenBy(la => la.Id)
            .ToListAsync();

        // ── Bankverbindungen des MA laden (für Auszahlungs-Sektion im PDF) ──
        // Im Perioden-Zeitraum gültige Konten, Hauptbank zuerst.
        var bankAccounts = await _db.EmployeeBankAccounts
            .Where(b => b.EmployeeId == employeeId
                     && b.ValidFrom <= periodTo
                     && (b.ValidTo == null || b.ValidTo >= periodFrom))
            .OrderByDescending(b => b.IsHauptbank)
            .ThenBy(b => b.ValidFrom)
            .ThenBy(b => b.Id)
            .ToListAsync();

        // Fallback: war zur Periode keine Bankverbindung erfasst, aber der MA
        // hat heute ein aktives Konto (z.B. nachträglich erfasst, abweichender
        // Empfänger heute eingetragen, ValidFrom = heute), dann dieses für die
        // Auszahlungs-Sektion verwenden — sonst stünde auf dem Lohnzettel
        // "keine Bankverbindung" obwohl die Bankdaten längst hinterlegt sind.
        if (bankAccounts.Count == 0)
        {
            var heute = DateOnly.FromDateTime(DateTime.Today);
            bankAccounts = await _db.EmployeeBankAccounts
                .Where(b => b.EmployeeId == employeeId
                         && b.ValidFrom <= heute
                         && (b.ValidTo == null || b.ValidTo >= heute))
                .OrderByDescending(b => b.IsHauptbank)
                .ThenBy(b => b.ValidFrom)
                .ThenBy(b => b.Id)
                .ToListAsync();
        }

        // ── Lohnposition-Katalog + flag-basiertes Basis-Tracking ──────────
        // Die Feiertags-/Ferien-/13.-ML-Basis wird aus den Beträgen pro
        // Lohnarten-Code gebildet — je nach dem, welche Flags die Lohnart
        // in `lohnposition` trägt (ZaehltAlsBasisFeiertag, -Ferien, -13ml).
        // Ersetzt die frühere hart verdrahtete Zuordnung
        // (z. B. "feiertagBasis = festlohn + mtpBasis" im MTP-Modell).
        var lohnposByCode = await _db.Lohnpositionen
            .Where(l => l.IsActive)
            .ToDictionaryAsync(l => l.Code);

        // Walter 31.07.2026: hier EXAKTE Beträge für Flag-Summen
        // (Ferien%/Feiertag%/13.ML). Keine gerundeten Zwischenresultate —
        // Lohnzeilen-CHF werden am Add-Ort separat gerundet.
        var codeAmounts = new Dictionary<string, decimal>();
        void AddAmount(string code, decimal amtExact)
        {
            if (string.IsNullOrEmpty(code) || amtExact == 0) return;
            codeAmounts[code] = (codeAmounts.TryGetValue(code, out var v) ? v : 0m) + amtExact;
        }
        decimal SumByFlag(Func<Lohnposition, bool> selector)
        {
            decimal sum = 0;
            foreach (var kv in codeAmounts)
                if (lohnposByCode.TryGetValue(kv.Key, out var lp) && selector(lp))
                    sum += kv.Value;
            return sum;
        }
        // Walter-Vorgabe 28.05.2026: Helfer für die SV-pflicht-Anwendung — die
        // Engine hat ein paar Stellen (Krank-/Unfall-Taggeld 80%, BVG-Wartefrist-
        // Korrekturen), wo Beträge AUSSERHALB des mainLohn-Pfads auf die
        // SV-Basen aufgeschlagen werden. Statt dort hardcoded zu sagen „nur
        // BVG + QST", schlägt diese Methode auf SV-Typ-Booleans auf, die der
        // Aufrufer dann gegen die Lohnposition-Flags abprüft. Vorteil: jede
        // Lohnposition entscheidet selbst, in welche SV-Basis sie fliesst.
        //
        // Fallback `fallbackBvg`/`fallbackQst` wird verwendet, wenn die
        // Lohnposition (noch) nicht angelegt ist — so bleibt das Verhalten
        // identisch zum bisherigen hardcoded Default.
        (bool ahv, bool nbuv, bool ktg, bool bvg, bool qst) LpFlagsOr(string code,
            bool fallbackAhv = false, bool fallbackNbuv = false, bool fallbackKtg = false,
            bool fallbackBvg = false, bool fallbackQst = false)
        {
            if (lohnposByCode.TryGetValue(code, out var lp) && lp != null)
                return (lp.AhvAlvPflichtig, lp.NbuvPflichtig, lp.KtgPflichtig,
                        lp.BvgPflichtig, lp.QstPflichtig);
            return (fallbackAhv, fallbackNbuv, fallbackKtg, fallbackBvg, fallbackQst);
        }
        // Lohnposition-Bezeichnung aus dem Katalog holen (mit Fallback).
        // Damit erscheint auf dem Lohnzettel der Name den der Admin in der
        // Lohnposition-Verwaltung hinterlegt hat (z.B. "KTG Karenzentschädigung"),
        // nicht ein hart kodierter Text.
        string LabelFor(string code, string fallback)
            => lohnposByCode.TryGetValue(code, out var lp) && !string.IsNullOrWhiteSpace(lp.Bezeichnung)
                ? lp.Bezeichnung
                : fallback;

        // SV-pflichtige Zulagen → werden zu totalLohn addiert (fliessen in SV-Basen ein)
        // SV-Flags kommen direkt aus Lohnposition (kein Umweg über LohnZulagTyp mehr)
        var zulagenSvLines  = new List<object>();
        decimal zulagenSvTotal = 0;
        // Per-SV-Typ Zulage-Deltas (für separate SV-Basen)
        decimal deltaAhv = 0, deltaNbuv = 0, deltaKtg = 0, deltaBvg = 0, deltaQst = 0;

        // Saldo-Vortrag-Lohnpositionen (Codes 901-906, Kategorie "Saldo-Vortrag")
        // werden im Lohnzettel separat als Saldo-Initialwerte behandelt — sie
        // fliessen weder in den Bruttolohn noch in die "Weitere Zahlungen"
        // Sektion ein. Stattdessen erscheinen sie in der Saldi-Übersicht als
        // initialer Vormonat-Saldo. Helper für saubere Filterung.
        bool IsVortrag(LohnZulage z) => z.Lohnposition?.Kategorie == "Saldo-Vortrag";

        foreach (var z in zulagenEntries.Where(z => z.Lohnposition!.Typ == "ZULAGE" && !IsVortrag(z)))
        {
            decimal b  = Math.Round(z.Betrag, 2);
            var     lp = z.Lohnposition!;

            bool anyFlag = lp.AhvAlvPflichtig || lp.NbuvPflichtig || lp.KtgPflichtig
                        || lp.BvgPflichtig    || lp.QstPflichtig;
            if (!anyFlag) continue; // → geht in zulagenExtraLines

            if (lp.DreijehnterMlPflichtig && b > 0)
            {
                // Split: Eingegebener Betrag = Total (inkl. 13. ML)
                // Basis  = Total × 12/13  (auf 2 Dezimalen)
                // 13. ML = Total − Basis  (Rest → Summe bleibt exakt)
                decimal basis13  = Math.Round(b * 12m / 13m, 2);
                decimal ml13     = b - basis13;
                string  bez      = lp.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : "");

                zulagenSvLines.Add(new { bezeichnung = bez,
                    anzahl = (decimal?)null, prozent = (decimal?)null, basis = (decimal?)null, betrag = basis13 });
                zulagenSvLines.Add(new { bezeichnung = $"13. ML a/{lp.Bezeichnung}",
                    anzahl = (decimal?)null, prozent = (decimal?)8.33m, basis = (decimal?)basis13, betrag = ml13 });
                zulagenSvTotal += b;  // Beide Zeilen fliessen in SV-Basis (Summe = b)
            }
            else
            {
                zulagenSvLines.Add(new {
                    bezeichnung = lp.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""),
                    anzahl = (decimal?)null, prozent = (decimal?)null, basis = (decimal?)null, betrag = b
                });
                zulagenSvTotal += b;
            }

            if (lp.AhvAlvPflichtig) deltaAhv  += b;
            if (lp.NbuvPflichtig)   deltaNbuv += b;
            if (lp.KtgPflichtig)    deltaKtg  += b;
            if (lp.BvgPflichtig)    deltaBvg  += b;
            if (lp.QstPflichtig)    deltaQst  += b;

            // Beitrag in das flag-basierte Basis-Tracking aufnehmen
            AddAmount(lp.Code, b);
        }

        // Nicht-SV-pflichtige Zulagen → separate Zeilen nach Nettolohn (Spesen etc.)
        // Vortrag-Lohnpositionen werden hier explizit ausgefiltert — sie sind
        // KEINE Auszahlung sondern reine Saldo-Eröffnung.
        var zulagenExtraLines = new List<object>();
        decimal zulagenExtraTotal = 0;
        foreach (var z in zulagenEntries.Where(z => z.Lohnposition!.Typ == "ZULAGE" && !IsVortrag(z)))
        {
            var lp2 = z.Lohnposition!;
            bool anyFlag2 = lp2.AhvAlvPflichtig || lp2.NbuvPflichtig || lp2.KtgPflichtig
                         || lp2.BvgPflichtig    || lp2.QstPflichtig;
            if (anyFlag2) continue; // bereits in zulagenSvLines

            decimal b = Math.Round(z.Betrag, 2);
            zulagenExtraLines.Add(new { bezeichnung = lp2.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""), betrag = b });
            zulagenExtraTotal += b;
        }

        // Saldo-Vortrag separat einsammeln — nur für die Saldi-Übersicht im
        // Lohnzettel (Initial-Vormonat). Wird im Result-Block übergeben.
        var vortragEntries = zulagenEntries.Where(IsVortrag).ToList();
        decimal vortragZeit       = vortragEntries.FirstOrDefault(z => z.Lohnposition!.Code == "901")?.Betrag ?? 0;
        decimal vortragFeiertag   = vortragEntries.FirstOrDefault(z => z.Lohnposition!.Code == "902")?.Betrag ?? 0;
        decimal vortragFerien     = vortragEntries.FirstOrDefault(z => z.Lohnposition!.Code == "903")?.Betrag ?? 0;
        decimal vortragNacht      = vortragEntries.FirstOrDefault(z => z.Lohnposition!.Code == "904")?.Betrag ?? 0;
        decimal vortragFerienGeld = vortragEntries.FirstOrDefault(z => z.Lohnposition!.Code == "905")?.Betrag ?? 0;
        decimal vortrag13Ml       = vortragEntries.FirstOrDefault(z => z.Lohnposition!.Code == "906")?.Betrag ?? 0;
        bool   hatVortrag         = vortragEntries.Any();

        // Lohnpositions-Abzüge (z. B. LGAV-Beitrag): NICHT SV-pflichtig, aber
        // echte Lohnabzüge → laufen mit den SV-Abzügen in den Total-Abzüge-
        // Block (vor Nettolohn). Reduzieren dadurch den Nettolohn direkt.
        // Werden in BuildResult an abzugResult angehängt und in totalAbzuege
        // mitgerechnet.
        var lohnposAbzugLines  = new List<object>();
        decimal lohnposAbzugTotal = 0;
        foreach (var z in zulagenEntries.Where(z => z.Lohnposition!.Typ == "ABZUG" && !IsVortrag(z)))
        {
            decimal b  = Math.Round(z.Betrag, 2);
            var     lp = z.Lohnposition!;
            lohnposAbzugLines.Add(new {
                bezeichnung = lp.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""),
                code        = lp.Code,   // Lohnpos-Code (z.B. "600.24" LGAV) fürs Fibu-Journal
                prozent     = (decimal?)null,
                basis       = (decimal?)null,
                betrag      = -b
            });
            lohnposAbzugTotal += b;
        }

        // Uniformen-Depot Rückerstattung (Walter Aug 2026): positiver Betrag
        // in abzugLines (= Auszahlung) wenn Austritt + Uniform zurückgegeben.
        // Status-Wechsel erst bei Confirm (ApplyAfterConfirmAsync).
        var (depotRefund, depotAmt, depotLabel) =
            await _uniformDepot.GetPendingRefundAsync(employeeId, periodFrom, periodTo);
        if (depotRefund && depotAmt > 0)
        {
            lohnposAbzugLines.Add(new {
                bezeichnung = depotLabel ?? "Uniformen-Depot Rückerstattung",
                code        = UniformDepotService.LohnpositionCode,
                prozent     = (decimal?)null,
                basis       = (decimal?)null,
                betrag      = depotAmt   // positiv = Refund (Fibu tauscht Konten)
            });
            lohnposAbzugTotal -= depotAmt; // reduziert totalAbzuege → höherer Netto
        }

        // Info-Hinweis bei NBU-Befreiung (Walter-Vorgabe 09.06.2026 / 31.07.2026):
        // Flag am FLEX-Vertrag (Employment); Legacy-Fallback am MA.
        // 0-CHF-Zeile im Lohnzettel — code=null → keine Fibu-Buchung.
        if (IsNbuBefreitUnter8h(emp, employee))
        {
            lohnposAbzugLines.Add(new {
                bezeichnung = "ℹ NBU-befreit (< 8h/Woche, UVG Art. 1a)",
                code        = (string?)null,
                prozent     = (decimal?)null,
                basis       = (decimal?)null,
                betrag      = 0m
            });
        }

        // "Weitere Abzüge" (nach Netto) — wird unten von der Lohnabtretungs-
        // Schleife befüllt. Reine Auszahlungs-Routing-Einträge.
        var abzuegeExtraLines = new List<object>();
        decimal abzuegeExtraTotal = 0;

        var lohnLines  = new List<object>();
        var abzugLines = new List<object>();
        decimal totalLohn = 0;

        // Walter-Vorgabe 28.05.2026: lohnLines werden am Ende nach Lohnposition-
        // SortOrder sortiert. Walter pflegt die Reihenfolge im UI (Spalte
        // Sortierung) — der Engine ordnet die Anzeige automatisch entsprechend.
        // Prefix-Mapping, weil die meisten Bezeichnungen via LabelFor(code, …)
        // erzeugt werden ODER feste Strings sind. SV-Berechnung ist davon nicht
        // betroffen — nur die Reihenfolge im Lohnzettel.
        // Bezeichnungs-Prefix → Lohnposition-Code (längste Prefixe zuerst!).
        var bezToCodeMap = new (string prefix, string code)[]
        {
            ("Festlohn für bezogene Ferien",     "2"),
            ("Festlohn für bezogene Feiertage",  "3"),
            ("Festlohn",                          "10"),    // greift NACH den zwei spezifischeren oben
            ("Monatslohn",                        "10"),    // FIX-Block
            ("MTP + Stunden",                     "4"),
            ("Stundenlohn Ferien",                "22"),
            ("Stundenlohn",                       "20"),
            ("Ausbezahlte Feiertage",             "50"),
            ("Korrektur Krankheit",               "75"),
            ("Krankheit (Karenzentschädigung)",   "70"),
            ("Krankheit (Taggeld 80%)",           "70.2"),
            ("Krankheit (Taggeld",                "70.2"),  // Fallback
            ("Korrektur Unfall",                  "65"),
            ("Unfall (Karenzentschädigung)",      "60"),
            ("Unfall (Taggeld 80%)",              "60.2"),
            ("Unfall (Taggeld",                   "60.2"),
            ("Unbezahlter Urlaub",                "110"),
            ("Feiertagentschädigung",             "_feiertag_ent"),
            ("Ferienentschädigung-Auszahlung",    "_ferien_ausz"),
            ("Ferienentschädigung",               "_ferien_ent"),
            ("13. Monatslohn",                    "_13ml"),
            ("Nacht-Kompensation",                "_nacht"),
        };
        var fallbackSortOrder = new Dictionary<string, int>
        {
            ["_ferien_ent"]    = 81,
            ["_feiertag_ent"]  = 82,
            ["_ferien_ausz"]   = 83,
            ["_13ml"]          = 200,
            ["_nacht"]         = 25,
        };
        int GetSortOrderForLine(object line, int fallbackIdx)
        {
            var bez = line.GetType().GetProperty("bezeichnung")?.GetValue(line) as string ?? "";
            foreach (var (prefix, code) in bezToCodeMap)
            {
                if (bez.StartsWith(prefix))
                {
                    if (lohnposByCode.TryGetValue(code, out var lp)) return lp.SortOrder;
                    if (fallbackSortOrder.TryGetValue(code, out var fb)) return fb;
                    return 9000 + fallbackIdx;
                }
            }
            return 9000 + fallbackIdx;
        }
        void SortLohnLines()
        {
            var sorted = lohnLines
                .Select((line, idx) => (sortOrder: GetSortOrderForLine(line, idx), idx, line))
                .OrderBy(x => x.sortOrder).ThenBy(x => x.idx)
                .Select(x => x.line)
                .ToList();
            lohnLines.Clear();
            lohnLines.AddRange(sorted);
        }

        // ── Manuelle Ferien-Geld-Saldo-Auszahlung (Code 195.3) ──────────
        // Wird bei Austritt oder Jahresende gebucht — die entsprechende
        // Zulage wurde bereits oben als SV-pflichtige Zeile verarbeitet
        // (fließt in totalLohn, alle Sozialversicherungen und 13.-ML-Basis).
        // Hier lesen wir nur den Gesamtbetrag, um im MTP/UTP-Block damit
        // das Ferien-Geld-Saldo zu reduzieren.
        decimal ferienGeldAuszahlungManuell = zulagenEntries
            .Where(z => z.Lohnposition?.Code == "195.3" && z.Lohnposition.Typ == "ZULAGE")
            .Sum(z => Math.Round(z.Betrag, 2));

        // ── Automatische Ferien-Geld-Auszahlung im Dezember (UTP/MTP) ──
        // Wenn am CompanyProfile aktiviert (AutoFerienGeldAuszahlungDezember)
        // und der Lohnlauf im Dezember ist: nach CalcFerienGeld wird der
        // verbleibende Saldo als synthetische 195.3-Lohnzeile ausbezahlt.
        // Lohnposition 195.3 einmalig laden — wird unten benötigt für
        // Bezeichnung, SV-Flags und Basis-Tracking.
        Lohnposition? lpFerienAuszahlung = null;
        bool autoDezemberAuszahlung = month == 12 && company.AutoFerienGeldAuszahlungDezember;
        if (autoDezemberAuszahlung)
        {
            lpFerienAuszahlung = await _db.Lohnpositionen
                .FirstOrDefaultAsync(l => l.Code == "195.3" && l.IsActive);
        }

        // ── Krankheit & Unfall: tag-genaue Listen (Datum + Prozent + InKarenz)
        // Arbeitet auf der tatsächlichen Lohnperiode (z.B. 21.01.–20.02.),
        // nicht auf Kalendermonaten. Die Karenz-Kumulation berücksichtigt
        // dabei frühere Tage des Karenzjahrs (auch aus Vorperioden).
        // Krank und Unfall laufen getrennt — eigene Tage-Grenze pro Typ
        // (Default: 14 Krank, 2 Unfall), aber gleiche Lohn-Logik.
        var krankBreakdown = await _karenz.GetPeriodBreakdownAsync(
            employeeId, companyProfileId, periodFrom, periodTo, "KRANK");
        var unfallBreakdown = await _karenz.GetPeriodBreakdownAsync(
            employeeId, companyProfileId, periodFrom, periodTo, "UNFALL");

        // Walter-Override (10.05.2026): Wenn beim MA "Karenz bereits
        // abgeschlossen" gesetzt ist (Legacy-Migration aus altem System),
        // dann verlassen ALLE Tage direkt die Karenz → durchgehend 80%.
        // Wir mutieren die Records via 'with { InKarenz = false }'.
        if (employee.KtgKarenzAbgeschlossen)
        {
            krankBreakdown  = krankBreakdown .Select(t => t with { InKarenz = false }).ToList();
            unfallBreakdown = unfallBreakdown.Select(t => t with { InKarenz = false }).ToList();
        }

        if (isMTP)
        {
            // ── MTP ──────────────────────────────────────────────────────
            decimal guaranteedH    = emp.GuaranteedHoursPerWeek ?? 0;
            // Walter-Vorgabe 30.05.2026: MTP-Festlohn-Stunden = garantierte
            // Wochenstunden / 7 × Anzahl Periodentage. Konsistent zu FIX (dort
            // schwankt das Sollstunden-Saldo auch monatlich nach Periodenlänge).
            // Bei MTP ist die garantierte Stundenzahl die Lohn-Basis — ein 31-
            // Tage-Monat muss mehr Stunden ergeben als ein 28-Tage-Monat, sonst
            // ist die „Garantie" im Februar schwächer als sie sein sollte.
            // Frühere 52/12-Glättung (= 147.33h bei 34h/Woche, konstant) ist
            // ersetzt durch pro-rata. Bei Kurzperiode (Ein-/Austritt mitten
            // im Monat): selbe Formel mit shortPeriodDays.
            // Jahres-Drift: 365/7 = 52.14 Wochen → +0.14 Wochen/Jahr gegenüber
            // 52/12 (bei 34h ≈ +4.86 h/Jahr; Schaltjahr +9.71 h). Mathematisch
            // korrekt, monatlich aber spürbarere Schwankung (Februar −11h vs.
            // Glättung). Tagessatz für Ferien-Abzug bleibt unverändert
            // (guaranteedH × hourlyRate / 7).
            int mtpPeriodTage = isShortPeriod ? shortPeriodDays : normalPeriodDays;
            // Walter-Vorgabe 30.05.2026: mit EXAKTEN Werten rechnen — runden erst
            // am Ende. Sonst entstehen kleine Rundungs-Differenzen bei voll
            // abgedeckten Perioden (z.B. 5 Ferien + 26 Krank → -0.01h statt 0).
            decimal sollStundenVollExakt = guaranteedH / 7m * mtpPeriodTage;
            decimal festlohnVollExakt    = sollStundenVollExakt * hourlyRate;
            decimal sollStundenVoll = Math.Round(sollStundenVollExakt, 2);  // nur Anzeige
            decimal festlohnVoll    = Math.Round(festlohnVollExakt,    2);  // nur Anzeige

            // ── MTP + FERIEN Regel (Walter 24.04.2026, präzisiert 26.05.2026) ────────────────
            // Pro Ferientag:
            //   • Sollstunden um GuaranteedH/7 reduzieren (MA muss an diesen
            //     Tagen nicht arbeiten — keine Minus-Stunden im Saldo).
            //   • Festlohn (10.5) wird um Tagessatz × Ferientage gekürzt.
            //     Walter-Vorgabe 26.05.2026: MTP ist im Grundsatz ein
            //     Stundenlöhner (kein fester Monatslohn — schwankt nach
            //     Anzahl Tagen). Deshalb 1/7-Logik konsistent mit der
            //     Ferien-Stunden-Gutschrift (ferienStundenMtp × 7 / guaranteedH):
            //         mtpTagessatz = garantierte WoStd × Stdlohn / 7
            //     (NICHT die FIX-Kalenderformel × 12/365 und NICHT die
            //     KTG-Formel — die hat ihren eigenen Service.)
            //   • Die Auszahlung aus FerienGeldSaldo erfolgt separat durch
            //     CalcFerienGeld() weiter unten ("Ferienentschädigung-
            //     Auszahlung" anteilig vom akkumulierten Guthaben).
            //   • Kein 10.6-Split mehr — die frühere Zeile "MTP Festlohn Ferien"
            //     (aus festlohn split) wird nicht mehr gebucht, sonst wäre
            //     der Betrag doppelt ausbezahlt (einmal im festlohn, einmal
            //     aus dem Saldo).
            // Walter-Vorgabe 30.05.2026: mtpFerienTage IMMER direkt aus den
            // Absencen zählen (Tage × Prozent/100). Keine Zwischenrundung.
            decimal mtpFerienTage = absences
                .Where(a => a.AbsenceType == "FERIEN")
                .Sum(a => (decimal)CountAbsenceDaysInPeriod(a, periodFrom, periodTo)
                          * (a.Prozent > 0 ? a.Prozent / 100m : 1m));
            // Fallback NUR wenn keine Absencen erfasst sind, ferienStundenMtp
            // aber > 0 (Backward-Compat mit alten Daten) — exakt.
            if (mtpFerienTage == 0m && ferienStundenMtp > 0 && guaranteedH > 0)
                mtpFerienTage = ferienStundenMtp * 7m / guaranteedH;

            // ── MTP Krank/Unfall-Kürzung am Festlohn (Walter-Vorgabe 30.05.2026) ────
            // Bei MTP wird der Festlohn um Krank-/Unfall-WERKTAGE gekürzt mit
            // der 1/5-Wochenstunden-Logik (analog FIX-Saldo-Gutschrift). Sa+So
            // Krank-/Unfall-Tage zählen NICHT für die Festlohn-Reduktion, weil
            // der MA an Wochenenden ohnehin nicht arbeiten würde — aber die
            // Krank-Taggeld 80% / Karenzentschädigung 88% bleibt auf Krank-
            // KALENDERtagen (Versicherung kompensiert alle Tage).
            //
            // Bei voll abgedeckter Periode (alle Werktage Krank + Ferien) kann
            // das Stunden-Total der Abzüge das Pro-Rata-Soll geringfügig
            // übersteigen (z.B. 19 Krank-Werktage × WoStd/5 = 129.2h und
            // 5 Ferien-Tage × WoStd/7 = 24.29h, Summe 153.49h vs. Soll 150.57h).
            // Der Festlohn wird dann per Math.Max(0, …)-Clamp auf 0 begrenzt.
            //
            // Eliminiert die früheren „Korrektur Krankheit/Unfall"-Zeilen
            // (Code 75/65) — bei FIX/FIX-M bleibt das Korrektur-Modell.
            // Kürzung läuft über Stunden-Äquivalente → Soll (unten), nicht über
            // vorgerundete Tagessatz-CHF.
            decimal mtpKrankWerktage = krankBreakdown
                .Where(t => t.Datum.DayOfWeek != DayOfWeek.Saturday && t.Datum.DayOfWeek != DayOfWeek.Sunday)
                .Sum(t => t.Prozent / 100m);
            decimal mtpUnfallWerktage = unfallBreakdown
                .Where(t => t.Datum.DayOfWeek != DayOfWeek.Saturday && t.Datum.DayOfWeek != DayOfWeek.Sunday)
                .Sum(t => t.Prozent / 100m);
            // Tage-Anzeige (für Label/Saldo): mtpKrankTage/mtpUnfallTage spiegeln
            // die Werktag-Zählung, NICHT die Kalendertage. Das ist konsistent
            // zur 1/5-Logik (sonst wäre das Label inkonsistent zur Berechnung).
            decimal mtpKrankTage  = mtpKrankWerktage;
            decimal mtpUnfallTage = mtpUnfallWerktage;

            // Stunden-Äquivalente (exakt, ungerundet) für die Subtraktion
            // Ferien: 1/7-Kalender (alle Tage zählen)
            // Krank/Unfall: 1/5-Werktag (NUR Mo-Fr zählen)
            decimal ferienStundenAequivalent = mtpFerienTage * guaranteedH / 7m;
            decimal krankStundenAequivalent  = mtpKrankWerktage  * guaranteedH / 5m;
            decimal unfallStundenAequivalent = mtpUnfallWerktage * guaranteedH / 5m;
            // Unbezahlter Urlaub (Walter-Vorgabe 27.06.2026): garantierte Soll-
            // Stunden um die UU-Tage kürzen — 1/7-Kalender wie Ferien. Dadurch
            // sinkt der Festlohn (= Soll × Stundenlohn); erreicht der MA die
            // reduzierte Garantie nicht, ergibt sich ein Minus-Saldo, erreicht/
            // übertrifft er sie, wird's im Stundenlohn ausbezahlt.
            decimal mtpUnbezUrlaubTage = absences
                .Where(a => a.AbsenceType == "UNBEZ_URLAUB")
                .Sum(a => (decimal)CountAbsenceDaysInPeriod(a, periodFrom, periodTo));
            decimal unbezUrlaubStundenAequivalent = mtpUnbezUrlaubTage * guaranteedH / 7m;
            // Sollstunden für Stunden-Saldo + Festlohn-Anzahl-Spalte —
            // mit EXAKTEN Werten, dann Cap auf 0 (Festlohn kann nie negativ).
            decimal sollStundenExakt = sollStundenVollExakt
                - ferienStundenAequivalent
                - krankStundenAequivalent
                - unfallStundenAequivalent
                - unbezUrlaubStundenAequivalent;
            // Cap: bei voll abgedeckter Periode kann das Stunden-Total der
            // Abzüge das Pro-Rata-Soll geringfügig übersteigen (Ferien 1/7 +
            // Krank 1/5 mischen sich) → auf 0 clampen.
            if (sollStundenExakt < 0m) sollStundenExakt = 0m;
            // Toleranz-Clamp für Rundungs-Drift aus decimal-Arithmetik.
            if (Math.Abs(sollStundenExakt) < 0.01m) sollStundenExakt = 0m;
            // Walter 31.07.2026 — keine Zwischenrundung:
            //   1) Stunden exakt bis Anzeige
            //   2) CHF-Produkt = Anzeige-Stunden × Satz (exakt, z.B. 81.43×21.66)
            //   3) Round nur für Lohnzeile / Saldo; AddAmount bekommt das EXAKTE Produkt
            //      (Ferien%/Feiertag%/13.ML ohne Drift aus Zeilen-Rundung)
            decimal sollStunden = Math.Round(sollStundenExakt, 2);
            decimal festlohnArbeitStunden = sollStunden;
            decimal festlohnExact = sollStunden * hourlyRate;
            decimal festlohnArbeitBetrag = Math.Round(festlohnExact, 2);

            // Stunden-Saldo inkl. Vormonat — EXAKT gegen sollStundenExakt
            // absenzGutschrift enthält nur noch Krank/Schulung/BEZ_ABSENZ/etc.
            decimal nettoH         = workedHours + absenzGutschrift - sollStundenExakt + vormonatHourSaldo;
            decimal mehrstundenAus = Math.Round(Math.Max(0, nettoH), 2);
            decimal neuerSaldo     = Math.Round(Math.Min(0, nettoH), 2);

            decimal mtpExact = mehrstundenAus * hourlyRate;
            decimal mtpBasis = Math.Round(mtpExact, 2);

            // Ausbezahlte Feiertage (eigene Stunden-Auszahlung)
            decimal feiertagExact = feiertagStunden * hourlyRate;
            decimal feiertagAusz  = Math.Round(feiertagExact, 2);

            // Basis für Minimum-Lohn-Kontrolle = Stundenlohn
            // Walter-Vorgabe 30.05.2026: Festlohn-Zeile auch dann zeigen, wenn der
            // Betrag 0 ist (z.B. ganzer Monat krank). Sonst fehlt die nachvollziehbare
            // Aufschlüsselung „Soll − Ferien − Krank − Unfall" und der Lohnzettel
            // wirkt unvollständig.
            bool zeigeFestlohnZeile = festlohnArbeitBetrag > 0
                || (mtpFerienTage == 0 && mtpKrankTage == 0 && mtpUnfallTage == 0)
                || sollStundenVoll > 0;
            if (zeigeFestlohnZeile)
            {
                // Label dynamisch erweitern: Soll, dann pro Absenz-Typ eine
                // Minus-Komponente (Ferien / Krank / Unfall) — nur wenn > 0.
                string mtpFestlohnLabel;
                if (isShortPeriod) {
                    string reasonTxt = (shortReasonStart && shortReasonEnd)
                        ? $"Eintritt {periodEffectiveFrom:dd.MM.yyyy} / Austritt {periodTo:dd.MM.yyyy}"
                        : shortReasonStart
                            ? $"Eintritt {periodEffectiveFrom:dd.MM.yyyy}"
                            : $"Austritt {periodTo:dd.MM.yyyy}";
                    mtpFestlohnLabel = $"{LabelFor("10", "Festlohn")} ({shortPeriodDays} von {normalPeriodDays} Tagen – {reasonTxt})";
                } else if (mtpFerienTage > 0 || mtpKrankTage > 0 || mtpUnfallTage > 0 || mtpUnbezUrlaubTage > 0) {
                    // Walter-Vorgabe 30.05.2026: nur Stunden im Label, keine CHF.
                    // Soll-Stunden minus Stunden-Äquivalente pro Absenz-Typ.
                    var teile = new List<string> { $"{sollStundenVoll:0.00}h Soll" };
                    if (ferienStundenAequivalent  > 0) teile.Add($"− {ferienStundenAequivalent:0.00}h Ferien");
                    if (krankStundenAequivalent   > 0) teile.Add($"− {krankStundenAequivalent:0.00}h Krank");
                    if (unfallStundenAequivalent  > 0) teile.Add($"− {unfallStundenAequivalent:0.00}h Unfall");
                    if (unbezUrlaubStundenAequivalent > 0) teile.Add($"− {unbezUrlaubStundenAequivalent:0.00}h Unbez. Urlaub");
                    mtpFestlohnLabel = $"{LabelFor("10", "Festlohn")} ({string.Join(" ", teile)})";
                } else {
                    mtpFestlohnLabel = LabelFor("10", "Festlohn");
                }
                lohnLines.Add(new {
                    bezeichnung = mtpFestlohnLabel,
                    anzahl  = (decimal?)festlohnArbeitStunden,
                    prozent = (decimal?)null,
                    basis   = (decimal?)hourlyRate,
                    betrag  = festlohnArbeitBetrag,
                    accrued = (decimal?)festlohnArbeitBetrag
                });
                totalLohn += festlohnArbeitBetrag;
                AddAmount("10", festlohnExact);
            }

            // Walter-Vorgabe 01.08.2026: Unbezahlter Urlaub auch auf dem
            // Lohnzettel aufführen (Info-Zeile). Der Festlohn ist oben bereits
            // um die UU-Stunden gekürzt — hier keine zweite CHF-Kürzung.
            if (mtpUnbezUrlaubTage > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = "Unbezahlter Urlaub",
                    anzahl  = (decimal?)Math.Round(mtpUnbezUrlaubTage, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)null,
                    betrag  = 0m,
                    accrued = (decimal?)0m
                });
            }

            // Hinweis: MTP Ferien-Auszahlung wird weiter unten verbucht
            // (nach ferienEnt-Berechnung), weil sie auf dem aktuellen
            // FerienGeldSaldo (inkl. Accrual dieser Periode) basiert.

            // Auszahlung"-Zeile eingefügt.

            if (feiertagAusz > 0)
            {
                decimal feiertagStdAnzeige = Math.Round(feiertagStunden, 2);
                lohnLines.Add(new { bezeichnung = $"{feiertagStdAnzeige} Ausbezahlte Feiertage", anzahl = (decimal?)feiertagStdAnzeige, prozent = (decimal?)null, basis = (decimal?)null, betrag = feiertagAusz, accrued = (decimal?)feiertagAusz });
                totalLohn += feiertagAusz;
            }

            if (mehrstundenAus > 0)
            {
                // Walter-Vorgabe 30.05.2026: Label transparent machen — Walter
                // soll im Label sehen WIE die Mehrstunden entstehen.
                // Formel: nettoH = workedHours + absenzGutschrift - sollStundenExakt + vormonat
                // Anzeige: Ist/Soll gerundet (Rechnung blieb exakt).
                decimal istStunden = Math.Round(workedHours + absenzGutschrift, 2);
                string mtpStdLabel = $"MTP + Stunden ({istStunden:0.00}h Ist − {sollStunden:0.00}h Soll";
                if (vormonatHourSaldo > 0) mtpStdLabel += $" + {vormonatHourSaldo:0.00}h Vormonat";
                else if (vormonatHourSaldo < 0) mtpStdLabel += $" − {Math.Abs(vormonatHourSaldo):0.00}h Vormonat";
                mtpStdLabel += ")";
                lohnLines.Add(new { bezeichnung = mtpStdLabel, anzahl = (decimal?)mehrstundenAus, prozent = (decimal?)100m, basis = (decimal?)hourlyRate, betrag = mtpBasis, accrued = (decimal?)mtpBasis });
                totalLohn += mtpBasis;
                AddAmount("4", mtpExact);  // exaktes Produkt für Flag-Summen
            }

            // ── Krankheit: Lohnkürzung + 88%-Gutschrift (im Karenzfenster) ──
            // Basis MTP: Tagessatz100 aus KtgTagessatzService — enthält
            // Garantie-Anteil + Ø Mehrstunden-Anteil (bei Regel B, also
            // ab 4 abgeschlossenen Perioden). Fallback auf die statische
            // Regel-A-Formel (guaranteedH × hourlyRate × 52/365), falls
            // der Service null liefert (z.B. kein Employment gefunden).
            // 80%-Gutschrift kommt nach dem mainLohn-Snapshot, siehe unten.
            // Für Tage innerhalb der BVG-Wartefrist (3 Kalendermonate ab
            // AU-Beginn) wird die fehlende Differenz zu 100% in deltaBvg
            // geschoben → BVG-Basis steht unverändert auf bisherigem Lohn.
            decimal krankTagesBasisMtp = 0m;
            if (krankBreakdown.Count > 0 || unfallBreakdown.Count > 0)
            {
                var ktgMtp = await _ktgService.CalculateAsync(employeeId, companyProfileId);
                krankTagesBasisMtp = ktgMtp?.Tagessatz100
                                  ?? (guaranteedH * hourlyRate * 52m / 365m);
            }
            decimal krankAbzugMtp = 0m, krank88Mtp = 0m, krank80Mtp = 0m;
            decimal krankTage88Mtp = 0m, krankTage80Mtp = 0m;
            decimal krankBvgKorrekturMtp = 0m;
            foreach (var t in krankBreakdown)
            {
                decimal tagWert = krankTagesBasisMtp * (t.Prozent / 100m);
                krankAbzugMtp += tagWert;
                if (t.InKarenz)
                {
                    krank88Mtp += tagWert * 0.88m;
                    krankTage88Mtp += t.Prozent / 100m;
                    if (t.BvgAuf100) krankBvgKorrekturMtp += tagWert * 0.12m;  // fehlende 12%
                }
                else
                {
                    krank80Mtp += tagWert * 0.80m;
                    krankTage80Mtp += t.Prozent / 100m;
                    if (t.BvgAuf100) krankBvgKorrekturMtp += tagWert * 0.20m;  // fehlende 20%
                }
            }
            krankAbzugMtp        = Math.Round(krankAbzugMtp,        2);
            krank88Mtp           = Math.Round(krank88Mtp,           2);
            krank80Mtp           = Math.Round(krank80Mtp,           2);
            krankBvgKorrekturMtp = Math.Round(krankBvgKorrekturMtp, 2);

            // Walter-Vorgabe 30.05.2026: Korrektur Krankheit (Code 75) wird bei MTP
            // NICHT mehr gebucht — die Lohn-Kürzung wegen Krankheit ist bereits
            // direkt am Festlohn vorgenommen (festlohnKrankKuerzung mit MTP-Tagessatz).
            // Eine zusätzliche Korrektur-Zeile mit KTG-Tagessatz wäre Doppelbuchung.
            // krankBvgKorrekturMtp bleibt unberührt (BVG-Wartefrist).
            if (krank88Mtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70", "Krankheit (Karenzentschädigung)"),
                    anzahl  = (decimal?)krankTage88Mtp,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(krankTagesBasisMtp, 2),
                    betrag  = krank88Mtp,
                    accrued = (decimal?)krank88Mtp
                });
                totalLohn += krank88Mtp;
                AddAmount("70", krank88Mtp);
            }

            // ── Unfall: Lohnkürzung + 88%-Gutschrift (im Karenzfenster) ───
            // Berechnung identisch zu Krankheit, nur mit eigener Tage-Grenze
            // (Default 2 Tage) und eigener BVG-Wartefrist — Unfall hat seine
            // separate 3-Monate-Wartefrist, unabhängig von Krank (andere
            // Versicherung, daher eigene Zählung).
            decimal unfallTagesBasisMtp = krankTagesBasisMtp;  // gleicher Tageswert
            decimal unfallAbzugMtp = 0m, unfall88Mtp = 0m, unfall80Mtp = 0m;
            decimal unfallTage88Mtp = 0m, unfallTage80Mtp = 0m;
            decimal unfallBvgKorrekturMtp = 0m;
            foreach (var t in unfallBreakdown)
            {
                decimal tagWert = unfallTagesBasisMtp * (t.Prozent / 100m);
                unfallAbzugMtp += tagWert;
                if (t.InKarenz)
                {
                    unfall88Mtp += tagWert * 0.88m;
                    unfallTage88Mtp += t.Prozent / 100m;
                    if (t.BvgAuf100) unfallBvgKorrekturMtp += tagWert * 0.12m;
                }
                else
                {
                    unfall80Mtp += tagWert * 0.80m;
                    unfallTage80Mtp += t.Prozent / 100m;
                    if (t.BvgAuf100) unfallBvgKorrekturMtp += tagWert * 0.20m;
                }
            }
            unfallAbzugMtp        = Math.Round(unfallAbzugMtp,        2);
            unfall88Mtp           = Math.Round(unfall88Mtp,           2);
            unfall80Mtp           = Math.Round(unfall80Mtp,           2);
            unfallBvgKorrekturMtp = Math.Round(unfallBvgKorrekturMtp, 2);

            // Walter-Vorgabe 30.05.2026: Korrektur Unfall (Code 65) wird bei MTP
            // NICHT mehr gebucht — Festlohn-Kürzung erfolgt bereits direkt am
            // Festlohn (festlohnUnfallKuerzung mit MTP-Tagessatz). unfallBvgKorrekturMtp
            // bleibt unberührt (BVG-Wartefrist).
            if (unfall88Mtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60", "Unfall (Karenzentschädigung)"),
                    anzahl  = (decimal?)unfallTage88Mtp,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(unfallTagesBasisMtp, 2),
                    betrag  = unfall88Mtp,
                    accrued = (decimal?)unfall88Mtp
                });
                totalLohn += unfall88Mtp;
                AddAmount("60", unfall88Mtp);
            }

            // MTP: Feiertag-/Ferien-Basis aus Lohnpositions-Flags
            //   → Festlohn (10.1) + Zusatzstunden (10.4) haben ZaehltAlsBasisFeiertag=true
            //   → nur Zusatzstunden (10.4) hat ZaehltAlsBasisFerien=true
            // Zusätzlich tragen alle Zulagen bei, deren Lohnart die Flags trägt.
            // Flag-Summen = exakte Produkte (nicht Zeilen-CHF)
            decimal feiertagBasisExact = SumByFlag(lp => lp.ZaehltAlsBasisFeiertag);
            decimal ferienBasisExact   = SumByFlag(lp => lp.ZaehltAlsBasisFerien);
            decimal ferienEntExact     = ferienBasisExact * vacationPct / 100m;
            decimal feiertagEntExact   = feiertagBasisExact * holidayPct / 100m;
            decimal ferienEnt   = Math.Round(ferienEntExact, 2);
            decimal feiertagEnt = Math.Round(feiertagEntExact, 2);

            if (ferienEnt > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = "Ferienentschädigung",
                    anzahl  = (decimal?)null,
                    prozent = (decimal?)vacationPct,
                    basis   = (decimal?)Math.Round(ferienBasisExact, 2),
                    betrag  = 0m,           // wandert in den Saldo
                    accrued = (decimal?)ferienEnt
                });
            }

            if (feiertagEnt > 0)
            {
                lohnLines.Add(new { bezeichnung = "Feiertagentschädigung", anzahl = (decimal?)null, prozent = (decimal?)holidayPct, basis = (decimal?)Math.Round(feiertagBasisExact, 2), betrag = feiertagEnt, accrued = (decimal?)feiertagEnt });
                totalLohn += feiertagEnt;
                AddAmount("3", feiertagEntExact);  // exakt für 13.ML-Flags
            }

            // ── MTP Ferien-Auszahlung anteilsmässig aus Pott (Walter 09.05.2026) ─
            // Regel-Update gegenüber älterer Version: der Pott schliesst den
            // **aktuellen Monat** ein — sowohl beim CHF-Saldo als auch bei den
            // Tagen. Damit kann ein MA Ferien beziehen sobald genug Tage im
            // Pott sind (inkl. dem diesen Monat akkumulierten Anteil).
            //
            //   Pott CHF   = vormonatFerienGeld + ferienEnt (akkumuliert akt.)
            //   Pott Tage  = vormonatFerienTage + ferienTageAccrual
            //   Tagessatz  = Pott CHF / Pott Tage
            //   Auszahlung = Tagessatz × bezogene Tage diesen Monat
            //
            // Beispiel: 800 + 200 CHF / (8 + 2) Tage = 100/Tag, 6 Tage bezogen
            //   → 600 CHF Auszahlung.
            //
            // Cap: Pott CHF (kein Vorbezug über den Pott hinaus).
            decimal pottFerienGeldChf   = vormonatFerienGeld + ferienEnt;
            decimal pottFerienGeldTage  = vormonatFerienTage + ferienTageAccrual;
            decimal mtpFerienAuszahlungBetrag = 0;
            decimal mtpAvgTagessatz           = 0;
            decimal mtpFerienAuszahlungExact = 0;
            if (mtpFerienTage > 0 && pottFerienGeldTage > 0 && pottFerienGeldChf > 0)
            {
                mtpAvgTagessatz = pottFerienGeldChf / pottFerienGeldTage;
                mtpFerienAuszahlungExact = mtpAvgTagessatz * mtpFerienTage;
                // Cap: nie mehr als der gesamte Pott (Saldo bleibt ≥ 0)
                if (mtpFerienAuszahlungExact > pottFerienGeldChf)
                    mtpFerienAuszahlungExact = pottFerienGeldChf;
                mtpFerienAuszahlungBetrag = Math.Round(mtpFerienAuszahlungExact, 2);
            }

            if (mtpFerienAuszahlungBetrag > 0)
            {
                // Walter-Vorgabe 26.05.2026: Label knapp und knackig — Tage ×
                // Ø Tagessatz; bei Pott-Cap zusätzlich „max <PottCHF>". Pott-
                // Logik (Tagessatz = Pott CHF / Pott Tage) siehe CLAUDE.md.
                bool _capped = mtpFerienAuszahlungExact + 0.005m < mtpAvgTagessatz * mtpFerienTage;
                string _labelExtra = _capped
                    ? $"({Math.Round(mtpFerienTage,2)} × {mtpAvgTagessatz:F2}, max {mtpFerienAuszahlungBetrag:F2})"
                    : $"({Math.Round(mtpFerienTage,2)} × {mtpAvgTagessatz:F2})";
                lohnLines.Add(new {
                    bezeichnung = $"{LabelFor("2", "Festlohn bezogene Ferien")} {_labelExtra}",
                    anzahl  = (decimal?)Math.Round(mtpFerienTage, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)null,
                    betrag  = mtpFerienAuszahlungBetrag,
                    accrued = (decimal?)mtpFerienAuszahlungBetrag
                });
                totalLohn += mtpFerienAuszahlungBetrag;
                AddAmount("2", mtpFerienAuszahlungExact);
            }

            // Ferien-Geld-Saldo neu: Pott − Auszahlung (exakt, dann Schluss-Rundung)
            ferienGeldSaldoNeu  = Math.Round(pottFerienGeldChf - mtpFerienAuszahlungExact, 2);
            ferienGeldAuszahlung = mtpFerienAuszahlungBetrag;

            // Manuelle Ferien-Geld-Saldo-Auszahlung (Code 195.3): reduziert
            // das Saldo. Der Betrag wurde schon als SV-pflichtige Zulage
            // zu totalLohn addiert — hier nur noch die Saldo-Führung.
            if (ferienGeldAuszahlungManuell > 0)
            {
                ferienGeldAuszahlung += ferienGeldAuszahlungManuell;
                ferienGeldSaldoNeu   = Math.Max(0m, ferienGeldSaldoNeu - ferienGeldAuszahlungManuell);
            }

            // Automatische Jahresend-Auszahlung des Ferien-Geld-Saldos (MTP).
            // Synthetische 195.3-Zeile mit dem aktuellen Saldo, voll SV-pflichtig.
            if (autoDezemberAuszahlung && lpFerienAuszahlung != null && ferienGeldSaldoNeu > 0)
            {
                decimal autoBetrag = Math.Round(ferienGeldSaldoNeu, 2);
                lohnLines.Add(new {
                    bezeichnung = lpFerienAuszahlung.Bezeichnung + " (Jahresende)",
                    anzahl  = (decimal?)null,
                    prozent = (decimal?)null,
                    basis   = (decimal?)null,
                    betrag  = autoBetrag,
                    accrued = (decimal?)autoBetrag
                });
                totalLohn += autoBetrag;
                if (lpFerienAuszahlung.AhvAlvPflichtig) deltaAhv  += autoBetrag;
                if (lpFerienAuszahlung.NbuvPflichtig)   deltaNbuv += autoBetrag;
                if (lpFerienAuszahlung.KtgPflichtig)    deltaKtg  += autoBetrag;
                if (lpFerienAuszahlung.BvgPflichtig)    deltaBvg  += autoBetrag;
                if (lpFerienAuszahlung.QstPflichtig)    deltaQst  += autoBetrag;
                AddAmount(lpFerienAuszahlung.Code, autoBetrag);
                ferienGeldAuszahlung += autoBetrag;
                ferienGeldSaldoNeu    = 0m;
            }

            // Nacht-Saldo
            decimal neuerNachtSaldo = Math.Round(vormonatNachtSaldo + nightBonus - nachtKompStunden, 2);

            // ── SV-pflichtige Zulagen zu totalLohn addieren (MTP) ─────────
            decimal mainLohnMtp = totalLohn;
            lohnLines.AddRange(zulagenSvLines);
            totalLohn += zulagenSvTotal;

            // ── 13. Monatslohn: Auszahlung oder Rückstellung je Firmen-Rhythmus ─
            // Basis = Summe aller Lohnpositionen mit Flag "Basis für 13. Monatslohn"
            // (ZaehltAlsBasis13ml = true). Voll Daten-getrieben — Walter steuert
            // pro Lohnposition in der Admin-UI ob sie zählt.
            // Probezeit überdrückt die Auszahlung — siehe isInProbation oben.
            // Verfall (Austritt ≤ Probezeit) sticht Auszahlungsmonat.
            bool isPayoutMonthMtp = IsThirteenthPayoutMonth(company, month) && !isInProbation && !thirteenthForfeited;
            decimal dreizehnterMtp = 0;
            decimal thirteenthPctForSaldo  = thirteenthPct;   // Wird akkumuliert …
            decimal prevThirteenthForSaldo = prevThirteenth;
            decimal mtp13BasisExact = SumByFlag(lp => lp.ZaehltAlsBasis13ml);
            decimal mtp13Basis = Math.Round(mtp13BasisExact, 2); // Anzeige / Saldo-Input
            // Display-Werte für die Saldi-Sektion im Auszahlungsmonat:
            // Vormonat / Aktueller Zuwachs / Bezogen / Saldo. Werden nur in
            // Auszahlungsmonaten gefüllt, sonst null.
            decimal? thirteenthPrevForDisplay     = null;
            decimal? thirteenthAccrualForDisplay  = null;
            decimal? thirteenthPayoutForDisplay   = null;
            if (thirteenthForfeited && thirteenthPct > 0)
            {
                decimal currentAccrual = Math.Round(mtp13BasisExact * thirteenthPct / 100m, 2);
                decimal forfeitedAmt = Math.Round(prevThirteenth + currentAccrual, 2);
                if (forfeitedAmt > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn (verfallen — Auflösung in Probezeit)",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)mtp13Basis,
                        betrag      = 0m,
                        accrued     = (decimal?)forfeitedAmt
                    });
                }
                thirteenthPctForSaldo  = 0;
                prevThirteenthForSaldo = 0;
            }
            else if (thirteenthPct > 0 && isPayoutMonthMtp)
            {
                // MTP-Auszahlung: prevThirteenth (aufgelaufener Saldo bis
                // Vormonat) + currentAccrual (aktueller Monat). Saldo wird
                // komplett geleert. Für Buchhaltungs-/Abacus-Export werden
                // beide Anteile als SEPARATE Lohnpositions-Zeilen gerendert,
                // damit FIBU-Kontierung sie unterscheiden kann (aktueller
                // Aufwand vs. Saldo-Auflösung).
                decimal currentAccrualExact = mtp13BasisExact * thirteenthPct / 100m;
                decimal currentAccrual = Math.Round(currentAccrualExact, 2);
                dreizehnterMtp = Math.Round(prevThirteenth + currentAccrualExact, 2);
                if (currentAccrual > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn (akt. Monat)",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)mtp13Basis,
                        betrag      = currentAccrual,
                        accrued     = (decimal?)currentAccrual
                    });
                    totalLohn += currentAccrual;
                }
                if (prevThirteenth > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn (Saldo-Auszahlung)",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)null,
                        basis       = (decimal?)null,
                        betrag      = prevThirteenth,
                        accrued     = (decimal?)prevThirteenth
                    });
                    totalLohn += prevThirteenth;
                }
                // Display-Werte für Saldi-Sektion: Vormonat, aktueller Zuwachs,
                // Bezogen-Betrag — Saldo neu = 0 (wird im Frontend rekonstruiert).
                thirteenthPrevForDisplay    = prevThirteenth;
                thirteenthAccrualForDisplay = currentAccrual;
                thirteenthPayoutForDisplay  = dreizehnterMtp;

                thirteenthPctForSaldo  = 0;   // Saldo geleert, keine weitere Rückstellung
                prevThirteenthForSaldo = 0;
            }
            else if (thirteenthPct > 0)
            {
                // Nicht-Auszahlungsmonat: 13.-ML-Zuwachs als reine Berechnungs-Zeile
                // anzeigen (betrag=0, accrued=currentAccrual) — analog zur
                // Ferienentschädigung. So sieht der MA monatlich, wie sich der
                // 13.-ML akkumuliert. Der Betrag wandert über thirteenthPctForSaldo
                // weiter in den Saldo-Block "Rückst. 13. Monatslohn".
                decimal currentAccrual = Math.Round(mtp13BasisExact * thirteenthPct / 100m, 2);
                if (currentAccrual > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)mtp13Basis,
                        betrag      = 0m,                 // keine Auszahlung
                        accrued     = (decimal?)currentAccrual
                    });
                }
            }

            // ── Krankheit: 80%-Gutschrift (nach Karenz, nach 13. ML einfügen) ──
            // Versicherungsleistung — SV-Flags kommen aus Lohnposition 70.2
            // (Walter-Vorgabe 28.05.2026: nichts mehr hardcoded). Fallback
            // wenn LP nicht angelegt: nur BVG + QST (= L-GAV Art. 23-Default).
            if (krank80Mtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70.2", "Krankheit (Taggeld 80%)"),
                    anzahl  = (decimal?)krankTage80Mtp,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(krankTagesBasisMtp, 2),
                    betrag  = krank80Mtp,
                    accrued = (decimal?)krank80Mtp
                });
                totalLohn += krank80Mtp;
                AddAmount("70.2", krank80Mtp);
                var f = LpFlagsOr("70.2", fallbackBvg: true, fallbackQst: true);
                if (f.ahv)  deltaAhv  += krank80Mtp;
                if (f.nbuv) deltaNbuv += krank80Mtp;
                if (f.ktg)  deltaKtg  += krank80Mtp;
                if (f.bvg)  deltaBvg  += krank80Mtp;
                if (f.qst)  deltaQst  += krank80Mtp;
            }

            // Unfall: 80%-Gutschrift — analog Krank, Flags aus LP 60.2.
            if (unfall80Mtp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60.2", "Unfall (Taggeld 80%)"),
                    anzahl  = (decimal?)unfallTage80Mtp,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(unfallTagesBasisMtp, 2),
                    betrag  = unfall80Mtp,
                    accrued = (decimal?)unfall80Mtp
                });
                totalLohn += unfall80Mtp;
                AddAmount("60.2", unfall80Mtp);
                var f = LpFlagsOr("60.2", fallbackBvg: true, fallbackQst: true);
                if (f.ahv)  deltaAhv  += unfall80Mtp;
                if (f.nbuv) deltaNbuv += unfall80Mtp;
                if (f.ktg)  deltaKtg  += unfall80Mtp;
                if (f.bvg)  deltaBvg  += unfall80Mtp;
                if (f.qst)  deltaQst  += unfall80Mtp;
            }

            // BVG-Wartefrist (GastroSocial, 3 Monate auf 100%-Lohn).
            // Krank und Unfall haben jeweils ihre eigene Wartefrist — die
            // fehlende Differenz zum Vollohn wird hier zur BVG-Basis addiert.
            deltaBvg += krankBvgKorrekturMtp + unfallBvgKorrekturMtp;

            var svBasesMtp = new SvBases(mainLohnMtp + deltaAhv  + dreizehnterMtp,
                                         mainLohnMtp + deltaNbuv + dreizehnterMtp,
                                         mainLohnMtp + deltaKtg  + dreizehnterMtp,
                                         mainLohnMtp + deltaBvg  + dreizehnterMtp,
                                         mainLohnMtp + deltaQst  + dreizehnterMtp);

            // ── Quellensteuer-Abzug (MTP) ─────────────────────────────────
            // Wie UTP: nur Hochrechnen wenn Nebenbeschäftigung gemeldet
            // (siehe ausführlicher Kommentar im UTP-Block).
            decimal? satzBruttoMtp = ComputeSatzBruttoForNebenjob(
                qstEinstellung, svBasesMtp.Qst, workedHours, company);
            var qstRule = ComputeQstDeduction(qstEinstellung, svBasesMtp.Qst, companyProfileId, periodFrom, satzBruttoMtp);
            if (qstRule is not null) deductions.Add(qstRule);

            SortLohnLines();  // Walter-Vorgabe 28.05.2026: Reihenfolge nach Lohnposition.SortOrder
            var result = BuildResult(employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn, svBasesMtp,
                zulagenExtraLines, zulagenExtraTotal, abzuegeExtraLines, abzuegeExtraTotal,
                lohnposAbzugLines, lohnposAbzugTotal,
                new SaldoBlock(
                    VormonatHourSaldo:    vormonatHourSaldo,
                    NeuerHourSaldo:       neuerSaldo,
                    WorkedHours:          workedHours,
                    SollStunden:          sollStunden,
                    Mehrstunden:          mehrstundenAus,
                    // Anzeige gerundet — Rechnung lief exakt bis Mehrstunden/CHF
                    AbsenzGutschrift:     Math.Round(absenzGutschrift, 2),
                    AbsenzBreakdown:      absenzBreakdown.ToDictionary(
                                             kv => kv.Key, kv => Math.Round(kv.Value, 2)),
                    SollStundenVoll:        sollStundenVoll,
                    SollFerienReduktion:    Math.Round(ferienStundenAequivalent, 2),
                    SollKrankReduktion:     Math.Round(krankStundenAequivalent, 2),
                    SollUnfallReduktion:    Math.Round(unfallStundenAequivalent, 2),
                    GuaranteedHoursPerWeek: guaranteedH,
                    FerienTageInPeriode:    Math.Round(mtpFerienTage, 2),
                    FerienKuerzungVorschlag:     kuerzungVorschlag,
                    FerienKuerzungVorschlagTage: kuerzungVorschlagTage,
                    NightHours:           nightHours,
                    NightBonus:           nightBonus,
                    NachtKompStunden:     Math.Round(nachtKompStunden, 2),
                    VormonatNachtSaldo:   vormonatNachtSaldo,
                    NeuerNachtSaldo:      neuerNachtSaldo,
                    VacationWeeks:        vacationWeeks,
                    VormonatFerienTage:   vormonatFerienTage,
                    FerienTageAccrual:    ferienTageAccrual,
                    FerienTageGenommen:   ferienTageGenommen,
                    FerienTageSaldoNeu:   ferienTageSaldoNeu,
                    VormonatFerienGeld:   vormonatFerienGeld,
                    FerienGeldSaldoNeu:   ferienGeldSaldoNeu,
                    FerienGeldAuszahlung: ferienGeldAuszahlung,
                    VormonatFeiertagTage: vormonatFeiertagTage,
                    FeiertagTageAccrual:  feiertagTageAccrual,
                    FeiertagTageGenommen: feiertagTageGenommen,
                    FeiertagTageSaldoNeu: feiertagTageSaldoNeu,
                    ThirteenthPct:        thirteenthPctForSaldo,
                    PrevThirteenth:       prevThirteenthForSaldo,
                    Basis13ml:            mtp13BasisExact,
                    ThirteenthPrevForDisplay:    thirteenthPrevForDisplay,
                    ThirteenthAccrualForDisplay: thirteenthAccrualForDisplay,
                    ThirteenthPayout:            thirteenthPayoutForDisplay),
                lohnAssignments, bankAccounts, usingDefaultDeductions,
                periodeFooterText: periodeFooterText,
                akontoBereitsAusbezahlt: akontoBereitsAusbezahlt,
                akontoBereitsAusbezahltDatum: akontoBereitsAusbezahltDatum,
                ytdSvBasesDezember: ytdSvBasesDez);
            return new OkObjectResult(result);
        }
        else if (isUTP)
        {
            // ── UTP / FLEX ────────────────────────────────────────────────
            // Walter 31.07.2026: Produkte EXAKT → AddAmount; Round nur Lohnzeile.
            // Brutto/Netto/Auszahlung am Ende kaufmännisch auf 0.05 (Round05).
            decimal workedHoursAnzeige = Math.Round(workedHours, 2);
            decimal lohnExact          = workedHoursAnzeige * hourlyRate;
            decimal nachtKompExact     = Math.Round(utpAuszahlungStunden, 2) * hourlyRate;
            decimal feiertagAuszExact  = feiertagStunden * hourlyRate;
            decimal lohnBrutto       = Math.Round(lohnExact, 2);
            decimal nachtKompBrutto  = Math.Round(nachtKompExact, 2);
            decimal feiertagAusz     = Math.Round(feiertagAuszExact, 2);

            // UTP: Feiertag-Basis aus Lohnpositions-Flags
            //   → Stundenlohn (20.1) trägt ZaehltAlsBasisFeiertag=true
            //   → zusätzlich fliessen alle Zulagen mit der Flag ein.
            //   → Nacht-Kompensation wird unter demselben Code geführt
            //     (SV-gleich wie Stundenlohn).
            AddAmount("20", lohnExact + nachtKompExact);
            decimal feiertagBasisUtpExact = SumByFlag(lp => lp.ZaehltAlsBasisFeiertag);
            decimal feiertagEntExact      = feiertagBasisUtpExact * holidayPct / 100m;
            decimal feiertagEnt           = Math.Round(feiertagEntExact, 2);

            lohnLines.Add(new { bezeichnung = "Stundenlohn", anzahl = (decimal?)workedHoursAnzeige, prozent = (decimal?)null, basis = (decimal?)hourlyRate, betrag = lohnBrutto, accrued = (decimal?)lohnBrutto });
            totalLohn += lohnBrutto;

            // Walter-Vorgabe 01.08.2026: Unbezahlter Urlaub auf dem Lohnzettel
            // aufführen. Bei FLEX keine CHF-Wirkung (ungestempelt = unbezahlt),
            // aber die Tage müssen sichtbar sein (Transparenz / Kontrolle).
            if (unbezUrlaubTageFerien > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = "Unbezahlter Urlaub",
                    anzahl  = (decimal?)Math.Round(unbezUrlaubTageFerien, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)null,
                    betrag  = 0m,
                    accrued = (decimal?)0m
                });
            }

            if (nachtKompBrutto > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = "Nacht-Kompensation",
                    anzahl  = (decimal?)Math.Round(utpAuszahlungStunden, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)hourlyRate,
                    betrag  = nachtKompBrutto,
                    accrued = (decimal?)nachtKompBrutto
                });
                totalLohn += nachtKompBrutto;
            }

            if (feiertagAusz > 0)
            {
                lohnLines.Add(new { bezeichnung = "Ausbezahlte Feiertage", anzahl = (decimal?)Math.Round(feiertagStunden, 2), prozent = (decimal?)null, basis = (decimal?)null, betrag = feiertagAusz, accrued = (decimal?)feiertagAusz });
                totalLohn += feiertagAusz;
            }
            if (feiertagEnt > 0)
            {
                lohnLines.Add(new { bezeichnung = "Feiertagentschädigung", anzahl = (decimal?)null, prozent = (decimal?)holidayPct, basis = (decimal?)Math.Round(feiertagBasisUtpExact, 2), betrag = feiertagEnt, accrued = (decimal?)feiertagEnt });
                totalLohn += feiertagEnt;
                AddAmount("50", feiertagEntExact);
            }

            // UTP-Kaskade: Ferien-Basis enthält auch die Feiertagentschädigung.
            //   → Stundenlohn (20.1) und Stundenlohn Feiertage (20.3) tragen beide
            //     ZaehltAlsBasisFerien=true.
            decimal ferienBasisUtpExact = SumByFlag(lp => lp.ZaehltAlsBasisFerien);
            decimal ferienEnt           = Math.Round(ferienBasisUtpExact * vacationPct / 100m, 2);
            if (ferienEnt > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = "Ferienentschädigung",
                    anzahl  = (decimal?)null,
                    prozent = (decimal?)vacationPct,
                    basis   = (decimal?)Math.Round(ferienBasisUtpExact, 2),
                    betrag  = 0m,
                    accrued = (decimal?)ferienEnt
                });
            }

            // Pott inkl. aktueller Monat (Walter 01.08.2026) — gleiche Formel wie MTP.
            // Parameter tageAccrual = monatliche Ferien-Tage-Gutschrift (nicht Saldo neu).
            (ferienGeldAuszahlung, ferienGeldSaldoNeu) = CalcFerienGeld(
                vormonatFerienGeld, ferienEnt, vormonatFerienTage, ferienTageAccrual,
                ferienTageGenommen, ref lohnLines, ref totalLohn, vacationPct, lohnBrutto);

            // Manuelle Ferien-Geld-Saldo-Auszahlung (Code 195.3): reduziert
            // das Saldo. Der Betrag wurde schon als SV-pflichtige Zulage
            // zu totalLohn addiert — hier nur noch die Saldo-Führung.
            if (ferienGeldAuszahlungManuell > 0)
            {
                ferienGeldAuszahlung += ferienGeldAuszahlungManuell;
                ferienGeldSaldoNeu   = Math.Max(0m, ferienGeldSaldoNeu - ferienGeldAuszahlungManuell);
            }

            // Automatische Jahresend-Auszahlung des Ferien-Geld-Saldos (UTP).
            // Synthetische 195.3-Zeile mit dem aktuellen Saldo, voll SV-pflichtig.
            if (autoDezemberAuszahlung && lpFerienAuszahlung != null && ferienGeldSaldoNeu > 0)
            {
                decimal autoBetrag = Math.Round(ferienGeldSaldoNeu, 2);
                lohnLines.Add(new {
                    bezeichnung = lpFerienAuszahlung.Bezeichnung + " (Jahresende)",
                    anzahl  = (decimal?)null,
                    prozent = (decimal?)null,
                    basis   = (decimal?)null,
                    betrag  = autoBetrag,
                    accrued = (decimal?)autoBetrag
                });
                totalLohn += autoBetrag;
                if (lpFerienAuszahlung.AhvAlvPflichtig) deltaAhv  += autoBetrag;
                if (lpFerienAuszahlung.NbuvPflichtig)   deltaNbuv += autoBetrag;
                if (lpFerienAuszahlung.KtgPflichtig)    deltaKtg  += autoBetrag;
                if (lpFerienAuszahlung.BvgPflichtig)    deltaBvg  += autoBetrag;
                if (lpFerienAuszahlung.QstPflichtig)    deltaQst  += autoBetrag;
                AddAmount(lpFerienAuszahlung.Code, autoBetrag);
                ferienGeldAuszahlung += autoBetrag;
                ferienGeldSaldoNeu    = 0m;
            }

            decimal neuerNachtSaldoUtp = Math.Round(vormonatNachtSaldo + nightBonus - nachtKompStunden, 2);

            // ── SV-pflichtige Zulagen zu totalLohn addieren (UTP) ─────────
            decimal mainLohnUtp = totalLohn;
            lohnLines.AddRange(zulagenSvLines);
            totalLohn += zulagenSvTotal;

            // ── 13. Monatslohn (FLEX) ───────────────────────────────────────
            // Standard: monatlich. Probezeit → Saldo. Am Periodenende bestanden
            // → nachzahlen + monatlich. Austritt ≤ ProbezeitEnde → Verfall.
            decimal dreizehnterUtp = 0;
            decimal prevThirteenthForSaldoUtp = 0;
            decimal basis13ForSaldoUtp = 0;
            decimal thirteenthPctForSaldoUtp = 0;
            decimal? thirteenthPrevForDisplayUtp = null;
            decimal? thirteenthAccrualForDisplayUtp = null;
            decimal? thirteenthPayoutForDisplayUtp = null;
            if (thirteenthPct > 0)
            {
                decimal basis13Exact = SumByFlag(lp => lp.ZaehltAlsBasis13ml);
                decimal basis13 = Math.Round(basis13Exact, 2);
                decimal currentAccrualExact = basis13Exact * thirteenthPct / 100m;
                decimal currentAccrual = Math.Round(currentAccrualExact, 2);

                if (thirteenthForfeited)
                {
                    // Verfall: Pott + Monatszuwachs entfallen (kein SV, Saldo 0).
                    decimal forfeitedAmt = Math.Round(prevThirteenth + currentAccrualExact, 2);
                    if (forfeitedAmt > 0)
                    {
                        lohnLines.Add(new {
                            bezeichnung = "13. Monatslohn (verfallen — Auflösung in Probezeit)",
                            anzahl      = (decimal?)null,
                            prozent     = (decimal?)thirteenthPct,
                            basis       = (decimal?)basis13,
                            betrag      = 0m,
                            accrued     = (decimal?)forfeitedAmt
                        });
                    }
                }
                else if (isInProbation)
                {
                    // Akkumulieren, nicht auszahlen.
                    if (currentAccrual > 0)
                    {
                        lohnLines.Add(new {
                            bezeichnung = "13. Monatslohn (Probe.Z. Rückstellung)",
                            anzahl      = (decimal?)null,
                            prozent     = (decimal?)thirteenthPct,
                            basis       = (decimal?)basis13,
                            betrag      = 0m,
                            accrued     = (decimal?)currentAccrual
                        });
                    }
                    prevThirteenthForSaldoUtp = prevThirteenth;
                    basis13ForSaldoUtp = basis13Exact;
                    thirteenthPctForSaldoUtp = thirteenthPct;
                }
                else
                {
                    // Bestanden (auch am Periodenende) → monatlich + Nachzahlung.
                    if (currentAccrual > 0)
                    {
                        lohnLines.Add(new {
                            bezeichnung = "13. Monatslohn",
                            anzahl      = (decimal?)null,
                            prozent     = (decimal?)thirteenthPct,
                            basis       = (decimal?)basis13,
                            betrag      = currentAccrual,
                            accrued     = (decimal?)currentAccrual
                        });
                        totalLohn += currentAccrual;
                    }
                    if (prevThirteenth > 0)
                    {
                        lohnLines.Add(new {
                            bezeichnung = "13. Monatslohn (Nachzahlung nach Probezeit)",
                            anzahl      = (decimal?)null,
                            prozent     = (decimal?)null,
                            basis       = (decimal?)null,
                            betrag      = prevThirteenth,
                            accrued     = (decimal?)prevThirteenth
                        });
                        totalLohn += prevThirteenth;
                        thirteenthPrevForDisplayUtp = prevThirteenth;
                        thirteenthAccrualForDisplayUtp = 0m;
                        thirteenthPayoutForDisplayUtp = prevThirteenth;
                    }
                    dreizehnterUtp = Math.Round(currentAccrual + prevThirteenth, 2);
                    prevThirteenthForSaldoUtp = 0;
                }
            }

            // ── Krankheit UTP: 88%/80% vom KTG-Tagessatz (inkl. Aufschläge) ──
            // Basis = Tagessatz100 aus KtgTagessatzService — der enthält bereits
            // Ferien/Feiertag/13. ML (Regel A: MaxPartTimeHours × stdLohnBrutto × 52/365;
            // Regel B: AHV-Ø der letzten Monate × 12/365). Wir fügen NACH 13. ML
            // ein, damit darauf kein weiterer Aufschlag gerechnet wird, und
            // schreiben direkt in delta* um die SV-Basis korrekt zu setzen.
            // Unfall UTP nutzt denselben Tagessatz (gleiche Berechnung).
            decimal krankTagesBasisUtp = 0m;
            if (krankBreakdown.Count > 0 || unfallBreakdown.Count > 0)
            {
                var ktgUtp = await _ktgService.CalculateAsync(employeeId, companyProfileId);
                krankTagesBasisUtp = ktgUtp?.Tagessatz100 ?? 0m;
            }
            decimal krank88Utp    = 0m;
            decimal krank80Utp    = 0m;
            decimal krankTage88Utp = 0m, krankTage80Utp = 0m;
            decimal krankBvgKorrekturUtp = 0m;
            if (krankTagesBasisUtp > 0)
            {
                foreach (var t in krankBreakdown)
                {
                    decimal tagWert = krankTagesBasisUtp * (t.Prozent / 100m);
                    if (t.InKarenz)
                    {
                        krank88Utp     += tagWert * 0.88m;
                        krankTage88Utp += t.Prozent / 100m;
                        if (t.BvgAuf100) krankBvgKorrekturUtp += tagWert * 0.12m;
                    }
                    else
                    {
                        krank80Utp     += tagWert * 0.80m;
                        krankTage80Utp += t.Prozent / 100m;
                        if (t.BvgAuf100) krankBvgKorrekturUtp += tagWert * 0.20m;
                    }
                }
                krank88Utp           = Math.Round(krank88Utp,           2);
                krank80Utp           = Math.Round(krank80Utp,           2);
                krankBvgKorrekturUtp = Math.Round(krankBvgKorrekturUtp, 2);
            }

            // 88%: voll SV-pflichtig (AhvAlv/Nbu/Ktg/Bvg/Qst). NACH 13. ML,
            // damit kein weiterer 13. ML-Aufschlag (Tagessatz100 enthält bereits 8.33%).
            if (krank88Utp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70", "Krankheit (Karenzentschädigung)"),
                    anzahl  = (decimal?)krankTage88Utp,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(krankTagesBasisUtp, 2),
                    betrag  = krank88Utp,
                    accrued = (decimal?)krank88Utp
                });
                totalLohn += krank88Utp;
                // Manuelle Delta-Updates (kein AddAmount — Aufschläge sind
                // schon im Tagessatz100 enthalten).
                deltaAhv  += krank88Utp;
                deltaNbuv += krank88Utp;
                deltaKtg  += krank88Utp;
                deltaBvg  += krank88Utp;
                deltaQst  += krank88Utp;
            }
            // 80%: Versicherungsleistung — SV-Flags aus LP 70.2 (Walter 28.05.2026).
            if (krank80Utp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70.2", "Krankheit (Taggeld 80%)"),
                    anzahl  = (decimal?)krankTage80Utp,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(krankTagesBasisUtp, 2),
                    betrag  = krank80Utp,
                    accrued = (decimal?)krank80Utp
                });
                totalLohn += krank80Utp;
                AddAmount("70.2", krank80Utp);
                var f = LpFlagsOr("70.2", fallbackBvg: true, fallbackQst: true);
                if (f.ahv)  deltaAhv  += krank80Utp;
                if (f.nbuv) deltaNbuv += krank80Utp;
                if (f.ktg)  deltaKtg  += krank80Utp;
                if (f.bvg)  deltaBvg  += krank80Utp;
                if (f.qst)  deltaQst  += krank80Utp;
            }

            // ── Unfall UTP: identische Logik wie Krankheit UTP ────────────
            // Gleicher Tagessatz (KtgTagessatzService), gleiche SV-Behandlung.
            // Eigene Kumulation → eigene Tage-Grenze (Default 2) aus
            // CompanyProfile.KarenzTageMaxUnfall.
            decimal unfall88Utp    = 0m;
            decimal unfall80Utp    = 0m;
            decimal unfallTage88Utp = 0m, unfallTage80Utp = 0m;
            decimal unfallBvgKorrekturUtp = 0m;
            if (krankTagesBasisUtp > 0)
            {
                foreach (var t in unfallBreakdown)
                {
                    decimal tagWert = krankTagesBasisUtp * (t.Prozent / 100m);
                    if (t.InKarenz)
                    {
                        unfall88Utp     += tagWert * 0.88m;
                        unfallTage88Utp += t.Prozent / 100m;
                        if (t.BvgAuf100) unfallBvgKorrekturUtp += tagWert * 0.12m;
                    }
                    else
                    {
                        unfall80Utp     += tagWert * 0.80m;
                        unfallTage80Utp += t.Prozent / 100m;
                        if (t.BvgAuf100) unfallBvgKorrekturUtp += tagWert * 0.20m;
                    }
                }
                unfall88Utp           = Math.Round(unfall88Utp,           2);
                unfall80Utp           = Math.Round(unfall80Utp,           2);
                unfallBvgKorrekturUtp = Math.Round(unfallBvgKorrekturUtp, 2);
            }
            if (unfall88Utp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60", "Unfall (Karenzentschädigung)"),
                    anzahl  = (decimal?)unfallTage88Utp,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(krankTagesBasisUtp, 2),
                    betrag  = unfall88Utp,
                    accrued = (decimal?)unfall88Utp
                });
                totalLohn += unfall88Utp;
                deltaAhv  += unfall88Utp;
                deltaNbuv += unfall88Utp;
                deltaKtg  += unfall88Utp;
                deltaBvg  += unfall88Utp;
                deltaQst  += unfall88Utp;
            }
            if (unfall80Utp > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60.2", "Unfall (Taggeld 80%)"),
                    anzahl  = (decimal?)unfallTage80Utp,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(krankTagesBasisUtp, 2),
                    betrag  = unfall80Utp,
                    accrued = (decimal?)unfall80Utp
                });
                totalLohn += unfall80Utp;
                AddAmount("60.2", unfall80Utp);
                var f = LpFlagsOr("60.2", fallbackBvg: true, fallbackQst: true);
                if (f.ahv)  deltaAhv  += unfall80Utp;
                if (f.nbuv) deltaNbuv += unfall80Utp;
                if (f.ktg)  deltaKtg  += unfall80Utp;
                if (f.bvg)  deltaBvg  += unfall80Utp;
                if (f.qst)  deltaQst  += unfall80Utp;
            }

            // BVG-Wartefrist: siehe MTP-Kommentar.
            deltaBvg += krankBvgKorrekturUtp + unfallBvgKorrekturUtp;

            var svBasesUtp = new SvBases(mainLohnUtp + deltaAhv  + dreizehnterUtp,
                                         mainLohnUtp + deltaNbuv + dreizehnterUtp,
                                         mainLohnUtp + deltaKtg  + dreizehnterUtp,
                                         mainLohnUtp + deltaBvg  + dreizehnterUtp,
                                         mainLohnUtp + deltaQst  + dreizehnterUtp);

            // ── Quellensteuer-Abzug (UTP) ─────────────────────────────────
            // Schweizer ESTV-Wegleitung (Kreisschreiben 45):
            //   Variante A — NUR 1 Arbeitgeber, keine Nebenbeschäftigung:
            //     Satzbestimmender Lohn = AHV-Lohn (IST-Brutto direkt).
            //     Keine Hochrechnung. Tarif aus Tabelle bei IST-Brutto;
            //     bei niedrigem Brutto greift der kantonale Mindestbetrag.
            //
            //   Variante B — Mehrere Arbeitgeber (Nebenbeschäftigung):
            //     Hochrechnung auf Gesamtpensum bzw. 100%.
            //
            // Steuerung über qst.WeitereBeschaftigungen + GesamtpensumWeitereAg
            // im QST-Eintrag.
            decimal? satzBruttoUtp = ComputeSatzBruttoForNebenjob(
                qstEinstellung, svBasesUtp.Qst, workedHours, company);
            var qstRuleUtp = ComputeQstDeduction(qstEinstellung, svBasesUtp.Qst, companyProfileId, periodFrom, satzBruttoUtp);
            if (qstRuleUtp is not null) deductions.Add(qstRuleUtp);

            // FLEX: 13. ML standardmässig monatlich. Während Probezeit → Saldo
            // (Basis13ml + PrevThirteenth); Nachzahlung leert den Saldo.
            SortLohnLines();  // Walter-Vorgabe 28.05.2026: Reihenfolge nach Lohnposition.SortOrder
            var result = BuildResult(employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn, svBasesUtp,
                zulagenExtraLines, zulagenExtraTotal, abzuegeExtraLines, abzuegeExtraTotal,
                lohnposAbzugLines, lohnposAbzugTotal,
                new SaldoBlock(
                    VormonatHourSaldo:    0,
                    NeuerHourSaldo:       0,
                    WorkedHours:          workedHours,
                    SollStunden:          0,
                    Mehrstunden:          0,
                    AbsenzGutschrift:     0m,
                    NightHours:           nightHours,
                    NightBonus:           nightBonus,
                    NachtKompStunden:     nachtKompStunden,
                    VormonatNachtSaldo:   vormonatNachtSaldo,
                    NeuerNachtSaldo:      neuerNachtSaldoUtp,
                    VacationWeeks:        vacationWeeks,
                    VormonatFerienTage:   vormonatFerienTage,
                    FerienTageAccrual:    ferienTageAccrual,
                    FerienTageGenommen:   ferienTageGenommen,
                    FerienTageSaldoNeu:   ferienTageSaldoNeu,
                    VormonatFerienGeld:   vormonatFerienGeld,
                    FerienGeldSaldoNeu:   ferienGeldSaldoNeu,
                    FerienGeldAuszahlung: ferienGeldAuszahlung,
                    VormonatFeiertagTage: vormonatFeiertagTage,
                    FeiertagTageAccrual:  feiertagTageAccrual,
                    FeiertagTageGenommen: feiertagTageGenommen,
                    FeiertagTageSaldoNeu: feiertagTageSaldoNeu,
                    ThirteenthPct:        thirteenthPctForSaldoUtp,
                    PrevThirteenth:       prevThirteenthForSaldoUtp,
                    ThirteenthPrevForDisplay:    thirteenthPrevForDisplayUtp,
                    ThirteenthAccrualForDisplay: thirteenthAccrualForDisplayUtp,
                    ThirteenthPayout:            thirteenthPayoutForDisplayUtp,
                    FerienKuerzungVorschlag:     kuerzungVorschlag,
                    FerienKuerzungVorschlagTage: kuerzungVorschlagTage,
                    Basis13ml:            basis13ForSaldoUtp,
                    IsInProbation:        isInProbation,
                    ThirteenthForfeited:  thirteenthForfeited,
                    ShowFlexThirteenthSaldo: showFlexThirteenthSaldo),
                lohnAssignments, bankAccounts, usingDefaultDeductions,
                periodeFooterText: periodeFooterText,
                akontoBereitsAusbezahlt: akontoBereitsAusbezahlt,
                akontoBereitsAusbezahltDatum: akontoBereitsAusbezahltDatum,
                ytdSvBasesDezember: ytdSvBasesDez);
            return new OkObjectResult(result);
        }
        else // FIX / FIX-M – Monatslohn + Stunden-Saldo (Soll/Ist), kein Mehrstunden-Auszahlung
        {
            decimal pct            = emp.EmploymentPercentage ?? 100m;
            // MonthlySalary enthält den tatsächlichen Lohn (nach Pensum), MonthlySalaryFte den 100%-Wert
            // Monatslohn: auf 2 Dezimalen (keine 0.05-Pre-Rundung)
            decimal monthSalaryFull = emp.MonthlySalary ?? Math.Round((emp.MonthlySalaryFte ?? 0) * pct / 100m, 2);
            decimal fteSalary       = emp.MonthlySalaryFte ?? (pct > 0 ? Math.Round(monthSalaryFull * 100m / pct, 2) : monthSalaryFull);

            // Bei Eintritt/Austritt innerhalb der Periode: Monatslohn per Tagessatz-Formel
            //   Tagessatz = MonthlySalary × 12 / 365
            //   Lohn      = Tagessatz × Kalendertage der Kurzperiode
            // Schluss-Rundung auf den Periodenlohn (Finalbetrag).
            decimal monthSalaryExact = isShortPeriod
                ? monthSalaryFull * 12m / 365m * shortPeriodDays
                : monthSalaryFull;
            decimal monthSalary = Math.Round(monthSalaryExact, 2);

            string fixReasonTxt = (shortReasonStart && shortReasonEnd)
                ? $"Eintritt {periodEffectiveFrom:dd.MM.yyyy} / Austritt {periodTo:dd.MM.yyyy}"
                : shortReasonStart
                    ? $"Eintritt {periodEffectiveFrom:dd.MM.yyyy}"
                    : $"Austritt {periodTo:dd.MM.yyyy}";
            string monatslohnLabel = isShortPeriod
                ? $"Monatslohn ({shortPeriodDays} von {normalPeriodDays} Tagen – {fixReasonTxt})"
                : "Monatslohn";

            // ── FIX/FIX-M Festlohn-Split (Mirus-Style) ────────────────────
            // Festlohn wird in 3 Lohnzeilen aufgeteilt:
            //   10  "Festlohn"                       (Arbeit, gekürzt)
            //   2   "Festlohn für bezogene Ferien"   (Tagessatz × Ferientage)
            //   3   "Festlohn für bezogene Feiertage" (Tagessatz × Feiertage)
            // Total = monthSalary. Ferien/Feiertag runden; Arbeit = Rest
            // (keine dritte Rundung, sonst Σ ≠ Monatslohn).
            decimal fixTagessatz = monthSalaryFull * 12m / 365m;
            decimal ferienExactFix   = fixTagessatz * ferienTageGenommen;
            decimal feiertagExactFix = fixTagessatz * feiertagTageGenommen;
            decimal ferienBetragFix   = Math.Round(ferienExactFix, 2);
            decimal feiertagBetragFix = Math.Round(feiertagExactFix, 2);
            decimal festlohnArbeitFix = monthSalary - ferienBetragFix - feiertagBetragFix;

            lohnLines.Add(new
            {
                bezeichnung = monatslohnLabel,
                anzahl      = (decimal?)null,
                prozent     = pct < 100m ? (decimal?)pct : (decimal?)null,
                basis       = pct < 100m ? (decimal?)Math.Round(fteSalary, 2) : (decimal?)null,
                betrag      = festlohnArbeitFix,
                accrued     = (decimal?)festlohnArbeitFix
            });
            totalLohn += festlohnArbeitFix;
            AddAmount("10", festlohnArbeitFix);

            if (ferienBetragFix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("2", "Festlohn für bezogene Ferien"),
                    anzahl  = (decimal?)Math.Round(ferienTageGenommen, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(fixTagessatz, 2),
                    betrag  = ferienBetragFix,
                    accrued = (decimal?)ferienBetragFix
                });
                totalLohn += ferienBetragFix;
                AddAmount("2", ferienExactFix);
            }

            if (feiertagBetragFix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("3", "Festlohn für bezogene Feiertage"),
                    anzahl  = (decimal?)Math.Round(feiertagTageGenommen, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(fixTagessatz, 2),
                    betrag  = feiertagBetragFix,
                    accrued = (decimal?)feiertagBetragFix
                });
                totalLohn += feiertagBetragFix;
                AddAmount("3", feiertagExactFix);
            }

            // ── Unbezahlter Urlaub: Festlohn-Kürzung (FIX / FIX-M) ─────────
            // Walter-Vorgabe 27.06.2026: pro UU-Tag wird der Festlohn um den
            // Tagessatz (Monatslohn × 12/365, Kalenderbasis wie Ferien) gekürzt.
            // Eigene Negativ-Zeile; reduziert totalLohn (und damit die SV-Basis,
            // unbezahlter Urlaub ist nicht SV-pflichtig).
            decimal unbezUrlaubTageFix = absences
                .Where(a => a.AbsenceType == "UNBEZ_URLAUB")
                .Sum(a => (decimal)CountAbsenceDaysInPeriod(a, periodFrom, periodTo));
            decimal unbezUrlaubExactFix = fixTagessatz * unbezUrlaubTageFix;
            decimal unbezUrlaubBetragFix = Math.Round(unbezUrlaubExactFix, 2);
            if (unbezUrlaubBetragFix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = "Unbezahlter Urlaub",
                    anzahl  = (decimal?)Math.Round(unbezUrlaubTageFix, 2),
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(fixTagessatz, 2),
                    betrag  = -unbezUrlaubBetragFix,
                    accrued = (decimal?)(-unbezUrlaubBetragFix)
                });
                totalLohn -= unbezUrlaubBetragFix;
                AddAmount("10", -unbezUrlaubExactFix);
            }

            // ── Krankheit: Lohnkürzung + 88%-Gutschrift (FIX / FIX-M) ──
            // Basis = MonthlySalary × 12 / 365 (1/365-Tageswert des Fixlohns).
            // 80%-Gutschrift kommt nach dem mainLohn-Snapshot, siehe unten.
            // BVG-Korrektur für Tage in der 3-Monate-Wartefrist: siehe MTP.
            // Tagessatz für Krankheit basiert immer auf dem VOLLEN Monatslohn,
            // nicht auf dem bereits kurz-pro-ratierten monthSalary (sonst würde
            // der Abzug in der Austritts-Kurzperiode doppelt reduziert).
            decimal krankTagesBasisFix = monthSalaryFull * 12m / 365m;
            decimal krankAbzugFix = 0m, krank88Fix = 0m, krank80Fix = 0m;
            decimal krankTage88Fix = 0m, krankTage80Fix = 0m;
            decimal krankBvgKorrekturFix = 0m;
            foreach (var t in krankBreakdown)
            {
                decimal tagWert = krankTagesBasisFix * (t.Prozent / 100m);
                krankAbzugFix += tagWert;
                if (t.InKarenz)
                {
                    krank88Fix += tagWert * 0.88m;
                    krankTage88Fix += t.Prozent / 100m;
                    if (t.BvgAuf100) krankBvgKorrekturFix += tagWert * 0.12m;
                }
                else
                {
                    krank80Fix += tagWert * 0.80m;
                    krankTage80Fix += t.Prozent / 100m;
                    if (t.BvgAuf100) krankBvgKorrekturFix += tagWert * 0.20m;
                }
            }
            krankAbzugFix        = Math.Round(krankAbzugFix,        2);
            krank88Fix           = Math.Round(krank88Fix,           2);
            krank80Fix           = Math.Round(krank80Fix,           2);
            krankBvgKorrekturFix = Math.Round(krankBvgKorrekturFix, 2);

            if (krankAbzugFix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("75", "Korrektur Krankheit"),
                    anzahl  = (decimal?)krankBreakdown.Count,
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(krankTagesBasisFix, 2),
                    betrag  = -krankAbzugFix,
                    accrued = (decimal?)(-krankAbzugFix)
                });
                totalLohn -= krankAbzugFix;
                AddAmount("75", -krankAbzugFix);
            }
            if (krank88Fix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70", "Krankheit (Karenzentschädigung)"),
                    anzahl  = (decimal?)krankTage88Fix,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(krankTagesBasisFix, 2),
                    betrag  = krank88Fix,
                    accrued = (decimal?)krank88Fix
                });
                totalLohn += krank88Fix;
                AddAmount("70", krank88Fix);
            }

            // ── Unfall FIX/FIX-M: identische Logik wie Krankheit ──────────
            // Gleicher Tageswert (monthSalary × 12 / 365), eigene Tage-Grenze
            // (Default 2) aus CompanyProfile.KarenzTageMaxUnfall. BVG-
            // Wartefrist läuft separat (eigene 3 Monate ab Unfall-Beginn).
            decimal unfallTagesBasisFix = krankTagesBasisFix;
            decimal unfallAbzugFix = 0m, unfall88Fix = 0m, unfall80Fix = 0m;
            decimal unfallTage88Fix = 0m, unfallTage80Fix = 0m;
            decimal unfallBvgKorrekturFix = 0m;
            foreach (var t in unfallBreakdown)
            {
                decimal tagWert = unfallTagesBasisFix * (t.Prozent / 100m);
                unfallAbzugFix += tagWert;
                if (t.InKarenz)
                {
                    unfall88Fix += tagWert * 0.88m;
                    unfallTage88Fix += t.Prozent / 100m;
                    if (t.BvgAuf100) unfallBvgKorrekturFix += tagWert * 0.12m;
                }
                else
                {
                    unfall80Fix += tagWert * 0.80m;
                    unfallTage80Fix += t.Prozent / 100m;
                    if (t.BvgAuf100) unfallBvgKorrekturFix += tagWert * 0.20m;
                }
            }
            unfallAbzugFix        = Math.Round(unfallAbzugFix,        2);
            unfall88Fix           = Math.Round(unfall88Fix,           2);
            unfall80Fix           = Math.Round(unfall80Fix,           2);
            unfallBvgKorrekturFix = Math.Round(unfallBvgKorrekturFix, 2);

            if (unfallAbzugFix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("65", "Korrektur Unfall"),
                    anzahl  = (decimal?)unfallBreakdown.Count,
                    prozent = (decimal?)null,
                    basis   = (decimal?)Math.Round(unfallTagesBasisFix, 2),
                    betrag  = -unfallAbzugFix,
                    accrued = (decimal?)(-unfallAbzugFix)
                });
                totalLohn -= unfallAbzugFix;
                AddAmount("65", -unfallAbzugFix);
            }
            if (unfall88Fix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60", "Unfall (Karenzentschädigung)"),
                    anzahl  = (decimal?)unfallTage88Fix,
                    prozent = (decimal?)88m,
                    basis   = (decimal?)Math.Round(unfallTagesBasisFix, 2),
                    betrag  = unfall88Fix,
                    accrued = (decimal?)unfall88Fix
                });
                totalLohn += unfall88Fix;
                AddAmount("60", unfall88Fix);
            }

            // ── Stunden-Saldo (FIX / FIX-M) ─────────────────────────────
            // Sollstunden für die Lohnperiode =
            //   wöchentliche Sollstunden / 7 × Anzahl Kalendertage der Periode.
            //
            // Wöchentliche Sollstunden:
            //   1. expliziter WeeklyHours-Wert auf der Anstellung
            //   2. sonst aus Pensum × NormalWeeklyHours der Filiale
            //   3. Fallback: 42h (GAV Gastro)
            //
            // Der Wert variiert dadurch monatlich (z. B. 31 vs. 28 Tage) und
            // entspricht der gleichen Logik, die bei Absenzberechnungen im
            // Frontend schon verwendet wird.
            decimal normalWeekly = company.NormalWeeklyHours ?? 42m;
            // Exakt rechnen (Walter 31.07.2026); Anzeige-Soll erst danach runden.
            decimal weeklySoll   = emp.WeeklyHours ?? (normalWeekly * pct / 100m);
            int periodDays       = periodTo.DayNumber - periodFrom.DayNumber + 1;
            decimal sollStundenFixExakt = weeklySoll / 7m * periodDays;
            decimal sollStundenFix = Math.Round(sollStundenFixExakt, 2);

            // Ist-/Saldo-Berechnung (wie MTP, aber ohne Payout) — exaktes Soll:
            //   Netto = Worked + AbsenzGutschrift − Soll + Vormonat-Saldo
            //   → Neuer Saldo (kann positiv oder negativ sein; keine Auszahlung).
            decimal nettoHFix      = workedHours + absenzGutschrift - sollStundenFixExakt + vormonatHourSaldo;
            decimal neuerHourSaldoFix = Math.Round(nettoHFix, 2);

            decimal neuerNachtSaldoFix = Math.Round(vormonatNachtSaldo + nightBonus - nachtKompStunden, 2);

            // ── SV-pflichtige Zulagen zu totalLohn addieren (FIX) ─────────
            decimal mainLohnFix = totalLohn;
            lohnLines.AddRange(zulagenSvLines);
            totalLohn += zulagenSvTotal;

            // ── 13. Monatslohn: Auszahlung oder Rückstellung je Firmen-Rhythmus ─
            // Basis = Summe aller Lohnpositionen mit Flag "Basis für 13. Monatslohn"
            // (ZaehltAlsBasis13ml = true). Voll Daten-getrieben — Walter steuert
            // pro Lohnposition in der Admin-UI ob sie zählt.
            // Probezeit überdrückt die Auszahlung — siehe isInProbation oben.
            // Verfall (Austritt ≤ Probezeit) sticht Auszahlungsmonat.
            bool isPayoutMonthFix = IsThirteenthPayoutMonth(company, month) && !isInProbation && !thirteenthForfeited;
            decimal dreizehnterFix = 0;
            decimal thirteenthPctForSaldoFix  = thirteenthPct;
            decimal prevThirteenthForSaldoFix = prevThirteenth;
            decimal fix13BasisExact = SumByFlag(lp => lp.ZaehltAlsBasis13ml);
            decimal fix13Basis = Math.Round(fix13BasisExact, 2);
            // Display-Werte für Saldi-Sektion im Auszahlungsmonat (FIX/FIX-M)
            decimal? fix13PrevForDisplay    = null;
            decimal? fix13AccrualForDisplay = null;
            decimal? fix13PayoutForDisplay  = null;
            if (thirteenthForfeited && thirteenthPct > 0)
            {
                decimal currentAccrual = Math.Round(fix13BasisExact * thirteenthPct / 100m, 2);
                decimal forfeitedAmt = Math.Round(prevThirteenth + currentAccrual, 2);
                if (forfeitedAmt > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn (verfallen — Auflösung in Probezeit)",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)fix13Basis,
                        betrag      = 0m,
                        accrued     = (decimal?)forfeitedAmt
                    });
                }
                thirteenthPctForSaldoFix  = 0;
                prevThirteenthForSaldoFix = 0;
            }
            else if (thirteenthPct > 0 && isPayoutMonthFix)
            {
                // FIX/FIX-M-Auszahlung: identisches Splitting wie MTP. Aktueller
                // Monatsanteil und Saldo-Auszahlung als getrennte Lohnposition-
                // Zeilen, damit FIBU/Abacus-Export sie unterscheiden kann.
                decimal currentAccrualExact = fix13BasisExact * thirteenthPct / 100m;
                decimal currentAccrual = Math.Round(currentAccrualExact, 2);
                dreizehnterFix = Math.Round(prevThirteenth + currentAccrualExact, 2);
                fix13PrevForDisplay    = prevThirteenth;
                fix13AccrualForDisplay = currentAccrual;
                fix13PayoutForDisplay  = dreizehnterFix;
                if (currentAccrual > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn (akt. Monat)",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)fix13Basis,
                        betrag      = currentAccrual,
                        accrued     = (decimal?)currentAccrual
                    });
                    totalLohn += currentAccrual;
                }
                if (prevThirteenth > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn (Saldo-Auszahlung)",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)null,
                        basis       = (decimal?)null,
                        betrag      = prevThirteenth,
                        accrued     = (decimal?)prevThirteenth
                    });
                    totalLohn += prevThirteenth;
                }
                thirteenthPctForSaldoFix  = 0;
                prevThirteenthForSaldoFix = 0;
            }
            else if (thirteenthPct > 0)
            {
                // Nicht-Auszahlungsmonat: 13.-ML-Zuwachs als reine Berechnungs-Zeile
                // anzeigen (betrag=0, accrued=currentAccrual) — analog MTP.
                // So sieht der MA monatlich, wie sich der 13.-ML akkumuliert.
                decimal currentAccrual = Math.Round(fix13BasisExact * thirteenthPct / 100m, 2);
                if (currentAccrual > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "13. Monatslohn",
                        anzahl      = (decimal?)null,
                        prozent     = (decimal?)thirteenthPct,
                        basis       = (decimal?)fix13Basis,
                        betrag      = 0m,                 // keine Auszahlung
                        accrued     = (decimal?)currentAccrual
                    });
                }
            }

            // ── Krankheit: 80%-Gutschrift — SV-Flags aus LP 70.2 (Walter 28.05.2026) ──
            if (krank80Fix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("70.2", "Krankheit (Taggeld 80%)"),
                    anzahl  = (decimal?)krankTage80Fix,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(krankTagesBasisFix, 2),
                    betrag  = krank80Fix,
                    accrued = (decimal?)krank80Fix
                });
                totalLohn += krank80Fix;
                AddAmount("70.2", krank80Fix);
                var f = LpFlagsOr("70.2", fallbackBvg: true, fallbackQst: true);
                if (f.ahv)  deltaAhv  += krank80Fix;
                if (f.nbuv) deltaNbuv += krank80Fix;
                if (f.ktg)  deltaKtg  += krank80Fix;
                if (f.bvg)  deltaBvg  += krank80Fix;
                if (f.qst)  deltaQst  += krank80Fix;
            }

            // Unfall: 80%-Gutschrift — SV-Flags aus LP 60.2.
            if (unfall80Fix > 0)
            {
                lohnLines.Add(new {
                    bezeichnung = LabelFor("60.2", "Unfall (Taggeld 80%)"),
                    anzahl  = (decimal?)unfallTage80Fix,
                    prozent = (decimal?)80m,
                    basis   = (decimal?)Math.Round(unfallTagesBasisFix, 2),
                    betrag  = unfall80Fix,
                    accrued = (decimal?)unfall80Fix
                });
                totalLohn += unfall80Fix;
                AddAmount("60.2", unfall80Fix);
                var f = LpFlagsOr("60.2", fallbackBvg: true, fallbackQst: true);
                if (f.ahv)  deltaAhv  += unfall80Fix;
                if (f.nbuv) deltaNbuv += unfall80Fix;
                if (f.ktg)  deltaKtg  += unfall80Fix;
                if (f.bvg)  deltaBvg  += unfall80Fix;
                if (f.qst)  deltaQst  += unfall80Fix;
            }

            // BVG-Wartefrist: siehe MTP-Kommentar.
            deltaBvg += krankBvgKorrekturFix + unfallBvgKorrekturFix;

            var svBasesFix = new SvBases(mainLohnFix + deltaAhv  + dreizehnterFix,
                                          mainLohnFix + deltaNbuv + dreizehnterFix,
                                          mainLohnFix + deltaKtg  + dreizehnterFix,
                                          mainLohnFix + deltaBvg  + dreizehnterFix,
                                          mainLohnFix + deltaQst  + dreizehnterFix);

            // ── Quellensteuer-Abzug (FIX) ─────────────────────────────────
            // Wie UTP: nur Hochrechnen wenn Nebenbeschäftigung gemeldet.
            // Bei FIX wird in der Hochrechnungs-Logik das Pensum genutzt.
            decimal? satzBruttoFix = ComputeSatzBruttoForNebenjob(
                qstEinstellung, svBasesFix.Qst, workedHours: 0, company,
                pensumPct: emp.EmploymentPercentage);
            var qstRuleFix = ComputeQstDeduction(qstEinstellung, svBasesFix.Qst, companyProfileId, periodFrom, satzBruttoFix);
            if (qstRuleFix is not null) deductions.Add(qstRuleFix);

            SortLohnLines();  // Walter-Vorgabe 28.05.2026: Reihenfolge nach Lohnposition.SortOrder
            var result = BuildResult(employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn, svBasesFix,
                zulagenExtraLines, zulagenExtraTotal, abzuegeExtraLines, abzuegeExtraTotal,
                lohnposAbzugLines, lohnposAbzugTotal,
                new SaldoBlock(
                    VormonatHourSaldo:    vormonatHourSaldo,
                    NeuerHourSaldo:       neuerHourSaldoFix,
                    WorkedHours:          workedHours,
                    SollStunden:          sollStundenFix,
                    Mehrstunden:          0,
                    AbsenzGutschrift:     Math.Round(absenzGutschrift, 2),
                    AbsenzBreakdown:      absenzBreakdown.ToDictionary(
                                             kv => kv.Key, kv => Math.Round(kv.Value, 2)),
                    NightHours:           nightHours,
                    NightBonus:           nightBonus,
                    NachtKompStunden:     Math.Round(nachtKompStunden, 2),
                    VormonatNachtSaldo:   vormonatNachtSaldo,
                    NeuerNachtSaldo:      neuerNachtSaldoFix,
                    VacationWeeks:        vacationWeeks,
                    VormonatFerienTage:   vormonatFerienTage,
                    FerienTageAccrual:    ferienTageAccrual,
                    FerienTageGenommen:   ferienTageGenommen,
                    FerienTageSaldoNeu:   ferienTageSaldoNeu,
                    // FIX: kein Ferien-Geld-Saldo (Feriengeld ist im Monatslohn enthalten)
                    VormonatFerienGeld:   0,
                    FerienGeldSaldoNeu:   0,
                    FerienGeldAuszahlung: 0,
                    VormonatFeiertagTage: vormonatFeiertagTage,
                    FeiertagTageAccrual:  feiertagTageAccrual,
                    FeiertagTageGenommen: feiertagTageGenommen,
                    FeiertagTageSaldoNeu: feiertagTageSaldoNeu,
                    ThirteenthPct:        thirteenthPctForSaldoFix,
                    PrevThirteenth:       prevThirteenthForSaldoFix,
                    ThirteenthPrevForDisplay:    fix13PrevForDisplay,
                    ThirteenthAccrualForDisplay: fix13AccrualForDisplay,
                    ThirteenthPayout:            fix13PayoutForDisplay,
                    FerienKuerzungVorschlag:     kuerzungVorschlag,
                    FerienKuerzungVorschlagTage: kuerzungVorschlagTage,
                    Basis13ml:            fix13BasisExact),
                lohnAssignments, bankAccounts, usingDefaultDeductions,
                periodeFooterText: periodeFooterText,
                akontoBereitsAusbezahlt: akontoBereitsAusbezahlt,
                akontoBereitsAusbezahltDatum: akontoBereitsAusbezahltDatum,
                ytdSvBasesDezember: ytdSvBasesDez);
            return new OkObjectResult(result);
        }
      } // end try
      catch (Exception ex)
      {
          var inner = ex.InnerException?.Message ?? "";
          return new ObjectResult(new { error = ex.Message, detail = inner }) { StatusCode = 500 };
      }
    }

    /// <summary>
    /// Berechnet den Quellensteuer-Abzug aus dem Tarif-Service und gibt eine
    /// synthetische DeductionRule zurück (Type = "fixed", Rate = CHF-Betrag positiv).
    /// Gibt null zurück wenn kein Tarif gefunden oder Betrag = 0.
    /// </summary>
    private DeductionRule? ComputeQstDeduction(
        EmployeeQuellensteuer? einstellung,
        decimal bruttolohn,
        int companyProfileId,
        DateOnly periodFrom,
        decimal? satzbestimmenderBrutto = null)
    {
        if (einstellung is null
            || string.IsNullOrEmpty(einstellung.Steuerkanton)
            || string.IsNullOrEmpty(einstellung.TarifCode))
            return null;

        // ── Satzbestimmender Lohn ──────────────────────────────────────────
        // Reihenfolge:
        //   1. einstellung.MindestlohnSatzbestimmung (manuell gepflegt; z.B. 4500 CHF
        //      für Crew). Hat absoluten Vorrang weil bewusst gesetzt.
        //   2. Vom Aufrufer übergebener Wert (für UTP über Stunden-Hochrechnung
        //      bzw. FIX/MTP über Pensum-Hochrechnung).
        //   3. Fallback: der Brutto selbst → keine Hochrechnung.
        decimal satzBrutto = einstellung.MindestlohnSatzbestimmung
            ?? satzbestimmenderBrutto
            ?? bruttolohn;
        // Schutz: nie unter den IST-Brutto fallen (sonst wäre Steuer < eigentlich
        // geschuldete; satzbestimmend MUSS ≥ IST-Brutto sein).
        if (satzBrutto < bruttolohn) satzBrutto = bruttolohn;

        decimal qstBetrag;
        decimal? satzPct;

        if (einstellung.Prozentsatz.HasValue)
        {
            // Manuell überschriebener Prozentsatz — direkt auf IST-Brutto.
            qstBetrag = Math.Round(bruttolohn * einstellung.Prozentsatz.Value / 100m, 2);
            satzPct = einstellung.Prozentsatz;
        }
        else
        {
            // Dynamisch aus ESTV-Tarifdatei: Steuersatz + Mindeststeuer (Pos 46–54)
            // zum SATZBESTIMMENDEN Lohn, Betrag auf IST-Brutto (ESTV 4.4:
            // wenn IST × Satz < Mindeststeuer → Mindeststeuer).
            // So zahlt ein Stundenlöhner mit 13h/Mt nicht 0.00, wenn die
            // Stufe eine Mindeststeuer hat (AG CHF 2, LU CHF 13, …).
            var qstCalc = _tarifService.Berechne(
                einstellung.Steuerkanton,
                einstellung.TarifCode,
                einstellung.AnzahlKinder,
                einstellung.Kirchensteuer,
                satzbestimmenderBruttoCHF: satzBrutto,
                istBruttoCHF: bruttolohn);
            if (qstCalc is null) return null;
            qstBetrag = qstCalc.SteuerbetragCHF;

            // Walter-Vorgabe 27.05.2026: bei Mindeststeuer effektiven Satz zeigen
            // (Betrag/Brutto), damit die Zeile auf dem Lohnzettel aufgeht.
            if (qstCalc.MindeststeuerAngewendet && bruttolohn > 0)
                satzPct = Math.Round(qstBetrag / bruttolohn * 100m, 2);
            else
                satzPct = qstCalc.SteuersatzPct;
        }

        // Walter-Vorgabe 27.05.2026: bei QST-pflichtigem MA mit erfasstem Tarif
        // IMMER eine Zeile zeigen — auch bei 0.00 (Tarif ohne Mindeststeuer und
        // 0%-Stufe). Sonst denkt der GF, die QST sei «nicht berechnet».
        if (qstBetrag < 0) qstBetrag = 0;

        string qstCode     = einstellung.QstCode ?? $"{einstellung.TarifCode}{einstellung.AnzahlKinder}{(einstellung.Kirchensteuer ? 'Y' : 'N')}";

        return new DeductionRule
        {
            Id               = -99,
            CompanyProfileId = companyProfileId,
            CategoryCode     = "QST",
            CategoryName     = "Quellensteuer",
            // Satz nicht mehr im Namen — kommt über DisplayRatePercent in die
            // Prozent-Spalte des Lohnzettels (konsistent mit AHV/ALV/NBU/...).
            Name             = $"Quellensteuer {qstCode} {einstellung.Steuerkanton}",
            Type             = "fixed",
            Rate             = qstBetrag,   // BuildResult negiert diesen Wert
            BasisType        = "gross",
            IsActive         = true,
            ValidFrom        = periodFrom,
            SortOrder        = 90,
            DisplayRatePercent = satzPct,   // transient, nur für die Anzeige
        };
    }

    /// <summary>
    /// Korrektur-/Sonderlohn für ausgetretene MA (Walter Aug 2026).
    /// Kein Stempel/Absenz/Saldo-Fortschreibung — nur manuelle LohnZulagen
    /// der Periode + ggf. Uniformen-Depot-Refund. Letzter Vertrag der Filiale
    /// dient nur als Kontext (Modell/Alter).
    /// </summary>
    private async Task<IActionResult> CalculateCorrectionAsync(
        int employeeId, int year, int month, int companyProfileId)
    {
        try
        {
            var employee = await _db.Employees
                .Include(e => e.Employments)
                .FirstOrDefaultAsync(e => e.Id == employeeId);
            if (employee is null) return new NotFoundObjectResult("Mitarbeiter nicht gefunden.");
            if (employee.IsPayrollExcluded)
                return new BadRequestObjectResult(new { message = "Dieser Mitarbeiter ist als «Kein Lohn» markiert." });

            var company = await _db.CompanyProfiles.FindAsync(companyProfileId);
            if (company is null) return new NotFoundObjectResult("Filiale nicht gefunden.");

            var (periodFrom, periodTo) = CalcPeriod(year, month);

            // Letzter Vertrag dieser Filiale (auch abgelaufen) — Kontext für Modell/Alter.
            var emp = employee.Employments
                .Where(e => e.CompanyProfileId == companyProfileId)
                .OrderByDescending(e => e.ContractStartDate)
                .FirstOrDefault();
            if (emp is null)
                return new NotFoundObjectResult(
                    "Kein Vertrag in dieser Filiale gefunden — Korrekturlohn nicht möglich.");

            // Kein LGAV / kein 1.-Lohn-Depot-Charge bei Korrektur (nur Refund möglich).
            string periodeStr = $"{year:D4}-{month:D2}";
            var zulagen = await _db.LohnZulagen
                .Include(z => z.Lohnposition)
                .Where(z => z.EmployeeId == employeeId && z.Periode == periodeStr)
                .Where(z => z.Lohnposition != null && z.Lohnposition.Kategorie != "Saldo-Vortrag")
                .OrderBy(z => z.Lohnposition!.SortOrder)
                .ThenBy(z => z.CreatedAt)
                .ToListAsync();

            var zulagenSvLines = new List<object>();
            decimal zulagenSvTotal = 0;
            decimal deltaAhv = 0, deltaNbuv = 0, deltaKtg = 0, deltaBvg = 0, deltaQst = 0;
            var zulagenExtraLines = new List<object>();
            decimal zulagenExtraTotal = 0;
            var lohnposAbzugLines = new List<object>();
            decimal lohnposAbzugTotal = 0;
            // Feiertags-Bemessungsgrundlage aus Lohnpositions-Flags
            // (z.B. 65.2 Korrektur UVG Versicherung → L-GAV 2.27 %).
            decimal feiertagBasisExact = 0m;

            foreach (var z in zulagen.Where(z => z.Lohnposition!.Typ == "ZULAGE"))
            {
                decimal b = Math.Round(z.Betrag, 2);
                var lp = z.Lohnposition!;
                bool anyFlag = lp.AhvAlvPflichtig || lp.NbuvPflichtig || lp.KtgPflichtig
                            || lp.BvgPflichtig || lp.QstPflichtig;
                string bez = lp.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : "");
                if (lp.ZaehltAlsBasisFeiertag) feiertagBasisExact += b;
                if (anyFlag)
                {
                    zulagenSvLines.Add(new {
                        bezeichnung = bez,
                        code = lp.Code,
                        anzahl = (decimal?)null, prozent = (decimal?)null,
                        basis = (decimal?)null, betrag = b
                    });
                    zulagenSvTotal += b;
                    if (lp.AhvAlvPflichtig) deltaAhv  += b;
                    if (lp.NbuvPflichtig)   deltaNbuv += b;
                    if (lp.KtgPflichtig)    deltaKtg  += b;
                    if (lp.BvgPflichtig)    deltaBvg  += b;
                    if (lp.QstPflichtig)    deltaQst  += b;
                }
                else
                {
                    zulagenExtraLines.Add(new { bezeichnung = bez, code = lp.Code, betrag = b });
                    zulagenExtraTotal += b;
                }
            }

            foreach (var z in zulagen.Where(z => z.Lohnposition!.Typ == "ABZUG"))
            {
                decimal b = Math.Round(z.Betrag, 2);
                var lp = z.Lohnposition!;
                lohnposAbzugLines.Add(new {
                    bezeichnung = lp.Bezeichnung + (z.Bemerkung != null ? $" ({z.Bemerkung})" : ""),
                    code = lp.Code,
                    prozent = (decimal?)null,
                    basis = (decimal?)null,
                    betrag = -b
                });
                lohnposAbzugTotal += b;
            }

            // Uniformen-Depot-Refund (auch Monate nach Austritt)
            var (depotRefund, depotAmt, depotLabel) =
                await _uniformDepot.GetPendingRefundAsync(employeeId, periodFrom, periodTo);
            if (depotRefund && depotAmt > 0)
            {
                lohnposAbzugLines.Add(new {
                    bezeichnung = depotLabel ?? "Uniformen-Depot Rückerstattung",
                    code = UniformDepotService.LohnpositionCode,
                    prozent = (decimal?)null,
                    basis = (decimal?)null,
                    betrag = depotAmt
                });
                lohnposAbzugTotal -= depotAmt;
            }

            var lohnLines = new List<object>(zulagenSvLines);
            decimal totalLohn = zulagenSvTotal;
            var abzugLines = new List<object>();

            // Feiertagsentschädigung (L-GAV Art. 18) — nur Stundenlohn-Modelle
            // mit laufender %-Auszahlung (FLEX/MTP). FIX führt Feiertage als Tage-Saldo.
            // Beispiel Qazimi: 65.2 = 344 → Feiertag 2.27 % = 7.80 (AHV-pflichtig).
            var modelCorr = emp.EmploymentModel ?? "";
            bool isHourlyFeiertag = string.Equals(modelCorr, "FLEX", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(modelCorr, "UTP", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(modelCorr, "MTP", StringComparison.OrdinalIgnoreCase);
            decimal holidayPctCorr = company.DefaultHolidayPercent ?? 0m;
            if (isHourlyFeiertag && holidayPctCorr > 0 && feiertagBasisExact > 0)
            {
                decimal feiertagEntExact = feiertagBasisExact * holidayPctCorr / 100m;
                decimal feiertagEnt = Math.Round(feiertagEntExact, 2);
                if (feiertagEnt > 0)
                {
                    lohnLines.Add(new {
                        bezeichnung = "Feiertagentschädigung",
                        code = "195.2",
                        anzahl = (decimal?)null,
                        prozent = (decimal?)holidayPctCorr,
                        basis = (decimal?)Math.Round(feiertagBasisExact, 2),
                        betrag = feiertagEnt,
                        accrued = (decimal?)feiertagEnt
                    });
                    totalLohn += feiertagEnt;
                    // Feiertagsentschädigung = AHV-pflichtiger Lohn (im Gegensatz zum UVG-Taggeld)
                    deltaAhv  += feiertagEnt;
                    deltaNbuv += feiertagEnt;
                    deltaKtg  += feiertagEnt;
                    deltaBvg  += feiertagEnt;
                    deltaQst  += feiertagEnt;
                }
            }

            // SV-Regeln (Alter), ohne QST-Auto — Korrekturen kommen manuell (565 etc.).
            int? employeeAge = employee.DateOfBirth.HasValue
                ? year - employee.DateOfBirth.Value.Year : null;
            bool ueberRef = employee.DateOfBirth.HasValue
                && PayrollCalculations.HatReferenzalterErreicht(
                    employee.Gender, employee.DateOfBirth.Value, year, month);
            int? effectiveAge = employeeAge;
            if (ueberRef && (effectiveAge == null || effectiveAge < 65))
                effectiveAge = 65;

            var globalRates = await _db.SocialInsuranceRates
                .Where(r => r.IsActive
                         && r.ValidFrom <= periodTo
                         && (r.ValidTo == null || r.ValidTo >= periodFrom)
                         && !(r.Rate == 0 && r.RateEmployer != null))
                .ToListAsync();
            globalRates = globalRates
                .GroupBy(r => new { r.Code, r.MinAge, r.MaxAge, r.EmploymentModelCode, r.OnlyQuellensteuer, r.BasisType })
                .Select(g => g.OrderByDescending(r => r.ValidFrom).First())
                .OrderBy(r => r.SortOrder)
                .ToList();

            List<DeductionRule> allRules = globalRates.Any()
                ? globalRates.Select(r => new DeductionRule
                {
                    Id = -r.Id, CompanyProfileId = companyProfileId,
                    CategoryCode = r.Code, CategoryName = r.Name, Name = r.Name,
                    Type = "percent", Rate = r.Rate, RateEmployer = r.RateEmployer,
                    BasisType = r.BasisType, MinAge = r.MinAge, MaxAge = r.MaxAge,
                    FreibetragMonthly = r.FreibetragMonthly,
                    CoordinationDeduction = r.CoordinationDeduction,
                    MaxBaseMonthly = r.MaxBaseMonthly,
                    MaxBaseFlatMonthly = r.MaxBaseFlatMonthly,
                    MinBaseMonthly = r.MinBaseMonthly,
                    EntryThresholdYearly = r.EntryThresholdYearly,
                    OnlyQuellensteuer = r.OnlyQuellensteuer,
                    EmploymentModelCode = r.EmploymentModelCode,
                    ValidFrom = r.ValidFrom, SortOrder = r.SortOrder, IsActive = true,
                }).ToList()
                : BuildSwissStandardDeductions(companyProfileId);

            string? empModelCode = emp.EmploymentModel;
            var deductions = allRules
                .Where(r => (r.MinAge == null || effectiveAge == null || effectiveAge >= r.MinAge)
                         && (r.MaxAge == null || effectiveAge == null || effectiveAge <= r.MaxAge)
                         && !r.OnlyQuellensteuer
                         && (r.EmploymentModelCode == null
                             || string.Equals(r.EmploymentModelCode, empModelCode, StringComparison.OrdinalIgnoreCase))
                         && !string.Equals(r.CategoryCode, "BVG_ZUSATZ", StringComparison.OrdinalIgnoreCase)
                         && !(ueberRef && (string.Equals(r.CategoryCode, "ALV", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(r.CategoryCode, "BVG", StringComparison.OrdinalIgnoreCase))))
                .ToList();

            var svBases = new SvBases(deltaAhv, deltaNbuv, deltaKtg, deltaBvg, deltaQst);

            // Saldi unverändert durchreichen (kein Accrual bei Korrektur)
            var (prevYear, prevMonth) = PrevPeriod(year, month);
            var prevSaldo = await _db.PayrollSaldos
                .FirstOrDefaultAsync(s => s.EmployeeId == employeeId
                                       && s.PeriodYear == prevYear
                                       && s.PeriodMonth == prevMonth
                                       && s.CompanyProfileId == companyProfileId);
            // Falls kein Vormonat: aktueller Saldo dieser Periode (falls schon vorhanden)
            var curSaldo = await _db.PayrollSaldos
                .FirstOrDefaultAsync(s => s.EmployeeId == employeeId
                                       && s.PeriodYear == year
                                       && s.PeriodMonth == month
                                       && s.CompanyProfileId == companyProfileId);
            decimal h  = curSaldo?.HourSaldo ?? prevSaldo?.HourSaldo ?? 0;
            decimal n  = curSaldo?.NachtSaldo ?? prevSaldo?.NachtSaldo ?? 0;
            decimal fg = curSaldo?.FerienGeldSaldo ?? prevSaldo?.FerienGeldSaldo ?? 0;
            decimal ft = curSaldo?.FerienTageSaldo ?? prevSaldo?.FerienTageSaldo ?? 0;
            decimal fe = curSaldo?.FeiertagTageSaldo ?? prevSaldo?.FeiertagTageSaldo ?? 0;
            decimal t13 = curSaldo?.ThirteenthMonthAccumulated ?? prevSaldo?.ThirteenthMonthAccumulated ?? 0;

            var bankAccounts = await _db.EmployeeBankAccounts
                .Where(b => b.EmployeeId == employeeId
                         && b.ValidFrom <= periodTo
                         && (b.ValidTo == null || b.ValidTo >= periodFrom))
                .OrderByDescending(b => b.IsHauptbank)
                .ThenBy(b => b.ValidFrom)
                .ToListAsync();
            if (bankAccounts.Count == 0)
            {
                var heute = DateOnly.FromDateTime(DateTime.Today);
                bankAccounts = await _db.EmployeeBankAccounts
                    .Where(b => b.EmployeeId == employeeId
                             && b.ValidFrom <= heute
                             && (b.ValidTo == null || b.ValidTo >= heute))
                    .OrderByDescending(b => b.IsHauptbank)
                    .ToListAsync();
            }

            var existingPeriod = await _db.PayrollPerioden
                .Where(p => p.CompanyProfileId == companyProfileId
                         && p.Year == year && p.Month == month)
                .FirstOrDefaultAsync();

            var result = BuildResult(
                employee, emp, company, year, month, periodFrom, periodTo,
                lohnLines, abzugLines, deductions, totalLohn, svBases,
                zulagenExtraLines, zulagenExtraTotal,
                new List<object>(), 0m,
                lohnposAbzugLines, lohnposAbzugTotal,
                new SaldoBlock(
                    VormonatHourSaldo: h, NeuerHourSaldo: h,
                    WorkedHours: 0, SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
                    NightHours: 0, NightBonus: 0, NachtKompStunden: 0,
                    VormonatNachtSaldo: n, NeuerNachtSaldo: n,
                    VacationWeeks: 5,
                    VormonatFerienTage: ft, FerienTageAccrual: 0, FerienTageGenommen: 0, FerienTageSaldoNeu: ft,
                    VormonatFerienGeld: fg, FerienGeldSaldoNeu: fg, FerienGeldAuszahlung: 0,
                    VormonatFeiertagTage: fe, FeiertagTageAccrual: 0, FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: fe,
                    ThirteenthPct: 0, PrevThirteenth: t13, Basis13ml: 0
                ),
                new List<EmployeeLohnAssignment>(),
                bankAccounts,
                usingDefaultDeductions: !globalRates.Any(),
                periodeFooterText: existingPeriod?.PdfFooterText,
                akontoBereitsAusbezahlt: 0m,
                akontoBereitsAusbezahltDatum: null,
                ytdSvBasesDezember: null);

            // isCorrection-Flag auf Result setzen (BuildResult ist anonym)
            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
            var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(result, opts))!;
            node["isCorrection"] = true;
            node["periodLabel"] = $"Korrektur {month:00}/{year}";
            return new OkObjectResult(node);
        }
        catch (Exception ex)
        {
            return new ObjectResult(new { error = "CORRECTION_FAILED", message = ex.Message })
                { StatusCode = 500 };
        }
    }

    /// <summary>
    /// UVG Art. 1a Abs. 6: NBU-Befreiung bei &lt; 8 h/Woche.
    /// Quelle = FLEX-Vertrag; Legacy-Fallback = MA-Flag (vor Migration 31.07.2026).
    /// </summary>
    private static bool IsNbuBefreitUnter8h(Employment employment, Employee employee)
    {
        var model = employment.EmploymentModel ?? "";
        var isFlex = string.Equals(model, "FLEX", StringComparison.OrdinalIgnoreCase)
                  || string.Equals(model, "UTP", StringComparison.OrdinalIgnoreCase);
        if (!isFlex) return false;
        return employment.TeilzeitUnter8hWoche || employee.TeilzeitUnter8hWoche;
    }
}
