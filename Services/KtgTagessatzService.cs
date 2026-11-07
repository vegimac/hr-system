using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Berechnet den KTG/UVG-Tagessatz (Krankentaggeld / Unfall) für einen Mitarbeiter.
///
/// Grundlage ist die Spezialistenvorgabe (Stand 2026):
///
///   <b>Regel A</b> – Vertragsdauer ≤ 4 abgeschlossene Perioden seit ContractStartDate:
///     • FIX / FIX-M : MonthlySalary × 12 / 365
///     • UTP         : max_part_time_hours × StdLohn_brutto × 52 / 365
///     • MTP         : GuaranteedHoursPerWeek × StdLohn_brutto × 52 / 365
///     StdLohn_brutto = HourlyRate × (1 + Ferien% + Feiertag%) × (1 + 13. ML%)
///     (L-GAV-Reihenfolge, Walter 07.11.2026 / D. Sägesser: 20.40 → 23.04 → 24.95)
///
///   <b>Regel B</b> – Vertragsdauer ≥ 4 abgeschlossene Perioden:
///     • FIX / FIX-M / UTP : Σ SvBasisAhv der letzten max. 12 Monate / n × 12 / 365
///     • MTP               : Garantie-Anteil wie Regel A
///                         + Ø «MTP + Stunden» × Brutto-Aufschlag × 12 / 365
///
///   Tagessatz 100 % = Bruttolohn pro Kalendertag (wie der versicherte AHV-Lohn).
///     • 80 % – Taggeld der Versicherung, pro Tag auf 5 Rappen, keine Zuschläge
///     • 88 % – Karenz: Grundlohn-Anteil × 88 %, Ferien/Feiertag/13. ML einzeln
///              (Total = 88 % des Tagessatzes, nur mit sichtbarem Rechenweg)
///
///   Vertragswechsel (neues Employment mit neuem ContractStartDate) setzt
///   die 4-Monats-Frist zurück.
/// </summary>
public class KtgTagessatzService
{
    private readonly AppDbContext _db;
    private readonly ILogger<KtgTagessatzService> _logger;

    public KtgTagessatzService(AppDbContext db, ILogger<KtgTagessatzService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <param name="stichtag">Lohnlauf: Ende der Lohnperiode. Massgebend ist der Vertrag,
    /// der bis dahin begonnen hat — nicht <c>is_active</c>: beim Austritt ist der Vertrag
    /// schon inaktiv, die Krankheit im letzten Monat muss trotzdem bezahlt werden
    /// (Walter 04.10.2026, Radogoshi).</param>
    public async Task<KtgTagessatzResult?> CalculateAsync(int employeeId, int companyProfileId, DateOnly? stichtag = null)
    {
        var kandidaten = _db.Employments
            .Include(e => e.CompanyProfile)
            .Where(e => e.EmployeeId == employeeId && e.CompanyProfileId == companyProfileId);

        Employment? employment;
        if (stichtag is { } st)
        {
            var stDt = st.ToDateTime(TimeOnly.MinValue);
            employment = await kandidaten
                .Where(e => e.ContractStartDate <= stDt)
                .OrderByDescending(e => e.ContractStartDate)
                .FirstOrDefaultAsync();
        }
        else
        {
            employment = await kandidaten.Where(e => e.IsActive)
                    .OrderByDescending(e => e.ContractStartDate).FirstOrDefaultAsync()
                ?? await kandidaten.OrderByDescending(e => e.ContractStartDate).FirstOrDefaultAsync();
        }

        if (employment is null)
        {
            _logger.LogWarning("Kein Employment für Employee {EmpId}/Profile {ProfileId} (Stichtag {Stichtag})", employeeId, companyProfileId, stichtag);
            return null;
        }

        // Manuelle Overrides aus dem Employee laden — Walter-Vorgabe (10.05.2026)
        // für Legacy-MA aus dem alten Lohnsystem.
        var emp = await _db.Employees.FindAsync(employeeId);
        decimal? manuell           = emp?.KtgTagessatzManuell;
        bool     karenzAbgeschlossen = emp?.KtgKarenzAbgeschlossen ?? false;

        var company = employment.CompanyProfile
                    ?? await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.Id == companyProfileId);
        string modell = (employment.EmploymentModel ?? "").ToUpperInvariant();
        bool festlohn = modell is "FIX" or "FIX-M";

        // Zuschläge wie in der Lohnberechnung (PayrollCalculationEngine): Filial-%,
        // Ferien altersabhängig zum Stichtag, 13. ML nur mit Häkchen am Vertrag,
        // «Ferien/Feiertag im Ansatz» → 0 %. Gleiche Sätze ⇒ die Karenz-Zeilen
        // (Grundlohn-Anteil + Zuschläge) ergeben wieder 88 % des Tagessatzes.
        var stichtagDatum = stichtag ?? DateOnly.FromDateTime(DateTime.Today);
        decimal ferienPct   = company is null ? 0m : ResolveFerienPct(emp, company, stichtagDatum);
        decimal feiertagPct = company?.DefaultHolidayPercent ?? 0m;
        decimal ml13Pct     = employment.ThirteenthSalary ? (company?.DefaultThirteenthSalaryPercent ?? 0m) : 0m;
        if (employment.FerienFeiertagImAnsatz && !festlohn) { ferienPct = 0m; feiertagPct = 0m; }
        decimal aufschlag = PayrollCalculations.BruttoAufschlag(ferienPct, feiertagPct, ml13Pct);

        // Wenn manueller Tagessatz gesetzt: NICHT mehr auto-rechnen,
        // sondern direkt zurückgeben (88/80 daraus ableiten). Der Satz ist ein
        // Bruttolohn (Mirus: AHV-Durchschnitt) — Grundlohn-Anteil = Satz ÷ Aufschlag.
        if (manuell.HasValue && manuell.Value > 0)
        {
            var vertragsStartM = DateOnly.FromDateTime(employment.ContractStartDate);
            var s100 = Math.Round(manuell.Value, 2);
            var bdM = new KtgBreakdown {
                Hinweis = "Manueller Tagessatz — übersteuert die Auto-Berechnung (z.B. aus altem Lohnsystem übernommen).",
                WochenStunden = ResolveWochenStunden(employment, company),
            };
            decimal grundM = festlohn ? s100 : s100 / aufschlag;
            if (!festlohn) FuelleKarenzAufteilung(bdM, grundM, ferienPct, feiertagPct, ml13Pct,
                Math.Round(s100 * PayrollCalculations.KarenzAnteil, 2));
            return new KtgTagessatzResult(
                Regel:          "MANUELL",
                VertragsModell: modell,
                AnzahlPerioden: 0,
                VertragsStart:  vertragsStartM,
                Tagessatz100:   s100,
                Tagessatz88:    karenzAbgeschlossen ? 0m : Math.Round(s100 * PayrollCalculations.KarenzAnteil, 2),
                Tagessatz80:    PayrollCalculations.TaggeldProTag(s100),
                KarenzAbgeschlossen: karenzAbgeschlossen,
                Breakdown:      bdM,
                EnthaeltFeiertag: true,
                EnthaeltFerien:   true,
                Enthaelt13ml:     true,
                KarenzGrundTagessatz: grundM
            );
        }

        if (company is null) return null;

        var vertragsStart = DateOnly.FromDateTime(employment.ContractStartDate);

        // Alle Snapshots seit Vertragsstart holen (sortiert absteigend, max 12)
        var snapshots = await _db.PayrollSnapshots
            .Include(s => s.Periode)
            .Where(s => s.EmployeeId == employeeId
                     && s.CompanyProfileId == companyProfileId
                     && s.Periode != null
                     && s.Periode.PeriodFrom >= vertragsStart)
            .OrderByDescending(s => s.Periode!.PeriodFrom)
            .Take(12)
            .ToListAsync();

        int anzahlPerioden = snapshots.Count;
        string regel = anzahlPerioden >= 4 ? "B" : "A";

        // Brutto-Stundenlohn nach L-GAV (Walter 07.11.2026, D. Sägesser):
        // (Grundlohn + Ferien + Feiertag) × (1 + 13. ML). Der Tagessatz ist der
        // volle Bruttolohn pro Kalendertag — wie der AHV-Lohn, den die Versicherung
        // versichert. Das Taggeld (80 %) bekommt darum keine Zuschläge mehr; die
        // Karenz (88 %) rechnet auf dem Grundlohn-Anteil und weist Ferien, Feiertag
        // und 13. ML einzeln aus. Ersetzt «Tagessatz ohne Feiertag» vom 04.08.2026.
        decimal stdLohnBasis  = employment.HourlyRate ?? 0m;
        decimal stdLohnBrutto = stdLohnBasis * aufschlag;

        decimal tagessatz100;
        var breakdown = new KtgBreakdown {
            FerienPct     = ferienPct,
            FeiertagPct   = festlohn ? null : feiertagPct,
            ZehnterMLPct  = ml13Pct,
        };

        // Monatsliste immer mitliefern (bei Regel A als Info, bei Regel B als Berechnungsgrundlage)
        breakdown.Monate = snapshots
            .Where(s => s.Periode != null)
            .Select(s => new KtgMonatsSatz(
                s.Periode!.Year,
                s.Periode.Month,
                MonthName(s.Periode.Month),
                Math.Round(s.SvBasisAhv, 2)))
            .ToList();

        decimal grundTagessatz;
        if (regel == "A")
        {
            (tagessatz100, grundTagessatz) = CalculateRegelA(employment, company, stdLohnBrutto, modell, breakdown);
        }
        else
        {
            (tagessatz100, grundTagessatz) = CalculateRegelB(employment, company, stdLohnBrutto, aufschlag, modell, snapshots, breakdown);
        }

        tagessatz100 = Math.Round(tagessatz100, 2);
        if (!festlohn) FuelleKarenzAufteilung(breakdown, grundTagessatz, ferienPct, feiertagPct, ml13Pct,
            Math.Round(tagessatz100 * PayrollCalculations.KarenzAnteil, 2));

        return new KtgTagessatzResult(
            Regel:          regel,
            VertragsModell: modell,
            AnzahlPerioden: anzahlPerioden,
            VertragsStart:  vertragsStart,
            Tagessatz100:   tagessatz100,
            // Bei "Karenz bereits abgeschlossen" wird der 88%-Schritt
            // nicht mehr ausgewiesen — die Versicherung startet direkt
            // mit dem Meldebetrag (80%).
            Tagessatz88:    karenzAbgeschlossen ? 0m : Math.Round(tagessatz100 * PayrollCalculations.KarenzAnteil, 2),
            Tagessatz80:    PayrollCalculations.TaggeldProTag(tagessatz100),
            Breakdown:      breakdown,
            KarenzAbgeschlossen: karenzAbgeschlossen,
            // FLEX/MTP: Bruttolohn inkl. aller Zuschläge (Regel A Hochrechnung,
            // Regel B AHV-Durchschnitt). FIX/FIX-M: Monatslohn × 12/365 bzw.
            // AHV-Durchschnitt — unverändert.
            EnthaeltFeiertag: !festlohn || regel == "B",
            EnthaeltFerien:   !festlohn,
            Enthaelt13ml:     !festlohn,
            KarenzGrundTagessatz: grundTagessatz
        );
    }

    /// <summary>Karenz-Rechenweg pro Tag fürs Fenster (Grundlohn × 88 % + Zuschläge).
    /// Der 13. ML nimmt die Rundung auf, damit die vier Teile das angezeigte
    /// 88-%-Total ergeben (die Lohnabrechnung rundet jede Zeile selbst).</summary>
    private static void FuelleKarenzAufteilung(KtgBreakdown b, decimal grundTagessatz100,
        decimal ferienPct, decimal feiertagPct, decimal ml13Pct, decimal tagessatz88)
    {
        var k = PayrollCalculations.KarenzAufteilen(grundTagessatz100, ferienPct, feiertagPct, ml13Pct);
        b.GrundTagessatz    = Math.Round(grundTagessatz100, 2);
        b.KarenzGrundlohn   = Math.Round(k.Karenz, 2);
        b.KarenzFerien      = Math.Round(k.Ferien, 2);
        b.KarenzFeiertag    = Math.Round(k.Feiertag, 2);
        b.Karenz13ml        = ml13Pct > 0
            ? tagessatz88 - b.KarenzGrundlohn - b.KarenzFerien - b.KarenzFeiertag
            : 0m;
    }

    // ── Regel A ────────────────────────────────────────────────────────────

    /// <returns>(Tagessatz 100 %, Grundlohn-Anteil pro Tag ohne Zuschläge)</returns>
    private static (decimal Tagessatz100, decimal Grund) CalculateRegelA(
        Employment emp, CompanyProfile company,
        decimal stdLohnBrutto, string modell,
        KtgBreakdown b)
    {
        b.StundenlohnBasis  = emp.HourlyRate;
        b.StundenlohnBrutto = Math.Round(stdLohnBrutto, 4);
        decimal stdLohnBasis = emp.HourlyRate ?? 0m;

        switch (modell)
        {
            case "FIX":
            case "FIX-M":
            {
                decimal monatsLohn = emp.MonthlySalary ?? 0m;
                b.MonatsLohn = monatsLohn;
                b.WochenStunden = ResolveWochenStunden(emp, company);
                decimal satz = monatsLohn * 12m / 365m;
                return (satz, satz);
            }

            default: // MTP (garantierte Stunden), FLEX/UTP/leer (Filial-Stunden)
            {
                decimal wo = ResolveWochenStunden(emp, company) ?? 0m;
                b.WochenStunden = wo;
                return (wo * stdLohnBrutto * 52m / 365m, wo * stdLohnBasis * 52m / 365m);
            }
        }
    }

    /// <summary>
    /// Wochenstunden für UI/Meta: MTP = garantiert, FIX = Vertrag/Pensum,
    /// FLEX = Filial-Default.
    /// </summary>
    private static decimal? ResolveWochenStunden(Employment emp, CompanyProfile? company)
    {
        var modell = (emp.EmploymentModel ?? "").ToUpperInvariant();
        if (modell is "MTP")
            return emp.GuaranteedHoursPerWeek ?? emp.WeeklyHours;
        if (modell is "FIX" or "FIX-M")
        {
            if (emp.WeeklyHours.HasValue) return emp.WeeklyHours;
            if (emp.GuaranteedHoursPerWeek.HasValue) return emp.GuaranteedHoursPerWeek;
            // Pensum % × normale Wochenstunden der Filiale
            var normal = company?.NormalWeeklyHours ?? 42m;
            if (emp.EmploymentPercentage.HasValue)
                return Math.Round(normal * emp.EmploymentPercentage.Value / 100m, 2);
            return null;
        }
        // FLEX / UTP
        return company?.MaxPartTimeHoursPerWeek ?? 17m;
    }

    // ── Regel B ────────────────────────────────────────────────────────────

    /// <returns>(Tagessatz 100 %, Grundlohn-Anteil pro Tag ohne Zuschläge)</returns>
    private static (decimal Tagessatz100, decimal Grund) CalculateRegelB(
        Employment emp, CompanyProfile company,
        decimal stdLohnBrutto, decimal aufschlag, string modell,
        List<PayrollSnapshot> snapshots,
        KtgBreakdown b)
    {
        // Monatsdetails sind bereits in CalculateAsync gesetzt
        int n = snapshots.Count;
        decimal summeAhv = snapshots.Sum(s => s.SvBasisAhv);

        if (modell == "MTP")
        {
            // Garantie-Anteil (wie Regel A)
            decimal wo = ResolveWochenStunden(emp, company) ?? 0m;
            b.WochenStunden     = wo;
            b.StundenlohnBasis  = emp.HourlyRate;
            b.StundenlohnBrutto = Math.Round(stdLohnBrutto, 4);

            decimal garantieBasisMonat = wo * stdLohnBrutto * 52m / 12m;
            b.GarantieBasisMonat       = Math.Round(garantieBasisMonat, 2);

            decimal garantieTagessatz  = wo * stdLohnBrutto * 52m / 365m;
            b.GarantieTagessatz        = Math.Round(garantieTagessatz, 2);

            // Mehrstunden-Anteil: Lohnposition "MTP + Stunden" aus SlipJson jedes Monats
            // auf Brutto-Niveau bringen (Ferien + Feiertag, 13. ML darauf), dann Ø × 12 / 365.
            decimal summeMehrGrund = 0m;
            foreach (var s in snapshots)
                summeMehrGrund += ExtractLohnPositionBetrag(s.SlipJson, "MTP + Stunden");
            decimal mehrGrundMonatAvg    = n > 0 ? summeMehrGrund / n : 0m;
            decimal mehrstundenMonatAvg  = mehrGrundMonatAvg * aufschlag;
            b.MehrstundenAnteilMonat     = Math.Round(mehrstundenMonatAvg, 2);

            decimal mehrstundenTagessatz = mehrstundenMonatAvg * 12m / 365m;
            b.MehrstundenTagessatz       = Math.Round(mehrstundenTagessatz, 2);

            decimal grund = wo * (emp.HourlyRate ?? 0m) * 52m / 365m + mehrGrundMonatAvg * 12m / 365m;
            return (garantieTagessatz + mehrstundenTagessatz, grund);
        }
        else
        {
            // FIX / FIX-M / UTP: reiner AHV-Brutto-Durchschnitt. Er enthält Ferien,
            // Feiertag und 13. ML schon — für die Karenz (FLEX) wird der
            // Grundlohn-Anteil herausgerechnet, sonst kämen die Zuschläge doppelt.
            b.WochenStunden = ResolveWochenStunden(emp, company);
            decimal avg = n > 0 ? summeAhv / n : 0m;
            decimal satz = avg * 12m / 365m;
            bool festlohn = modell is "FIX" or "FIX-M";
            return (satz, festlohn || aufschlag <= 0 ? satz : satz / aufschlag);
        }
    }

    /// <summary>
    /// Extrahiert den Betrag einer Lohn-Position aus dem <c>SlipJson</c> per
    /// Bezeichnung. Gibt 0 zurück wenn die Position fehlt oder parsing fehlschlägt.
    /// </summary>
    private static decimal ExtractLohnPositionBetrag(string slipJson, string bezeichnung)
    {
        if (string.IsNullOrWhiteSpace(slipJson) || slipJson == "{}") return 0m;
        try
        {
            using var doc  = JsonDocument.Parse(slipJson);
            var       root = doc.RootElement;

            if (!root.TryGetProperty("lohnLines", out var lines)
             || lines.ValueKind != JsonValueKind.Array) return 0m;

            decimal summe = 0m;
            foreach (var line in lines.EnumerateArray())
            {
                if (!line.TryGetProperty("bezeichnung", out var bez)) continue;
                if (bez.GetString() != bezeichnung) continue;
                if (!line.TryGetProperty("betrag", out var bt)) continue;
                if (bt.TryGetDecimal(out var v)) summe += v;
            }
            return summe;
        }
        catch
        {
            return 0m;
        }
    }

    // ── Hilfsmethoden ──────────────────────────────────────────────────────

    /// <summary>
    /// Ermittelt den Ferien-Prozentsatz aus dem Filial-Default — altersaware:
    /// Ab company.VacationSixWeeksFromAge greift der 6-Wochen-Satz (Default 50).
    /// Walter-Vorgabe 06.06.2026: konsistent zur Engine, kein Vertrags-Override mehr.
    /// </summary>
    private static decimal ResolveFerienPct(Employee? employee, CompanyProfile company, DateOnly stichtag)
    {
        decimal pct = company.DefaultVacationPercent5Weeks ?? 10.65m;
        if (employee?.DateOfBirth != null)
        {
            // Wie die Engine: 6-Wochen-Satz, sobald der Geburtstag bis zum
            // Stichtag (Periodenende) erreicht ist — nie herunter.
            var dob = DateOnly.FromDateTime(employee.DateOfBirth.Value);
            if (dob.AddYears(company.VacationSixWeeksFromAge) <= stichtag)
                pct = Math.Max(pct, company.DefaultVacationPercent6Weeks ?? 13.04m);
        }
        return pct;
    }

    private static string MonthName(int month) => month switch
    {
        1 => "Januar",  2 => "Februar", 3 => "März",     4 => "April",
        5 => "Mai",     6 => "Juni",    7 => "Juli",     8 => "August",
        9 => "September", 10 => "Oktober", 11 => "November", 12 => "Dezember",
        _ => "?"
    };
}

// ── Rückgabetypen ──────────────────────────────────────────────────────────

public record KtgTagessatzResult(
    string       Regel,
    string       VertragsModell,
    int          AnzahlPerioden,
    DateOnly     VertragsStart,
    decimal      Tagessatz100,
    decimal      Tagessatz88,
    decimal      Tagessatz80,
    KtgBreakdown Breakdown,
    bool         KarenzAbgeschlossen = false,
    /// <summary>Tagessatz 100 % enthält die Feiertagentschädigung schon. FLEX/MTP
    /// immer (Bruttolohn nach L-GAV, Walter 07.11.2026); FIX/FIX-M nur Regel B.</summary>
    bool         EnthaeltFeiertag = false,
    /// <summary>Tagessatz 100 % enthält Ferien% bzw. 13. ML schon (FLEX/MTP und
    /// manuell ja, FIX/FIX-M nein). Das Taggeld bekommt dann keine Zuschläge.</summary>
    bool         EnthaeltFerien = false,
    bool         Enthaelt13ml = false,
    /// <summary>Grundlohn-Anteil pro Kalendertag (100 %, ohne Ferien/Feiertag/13. ML).
    /// Basis der Karenz FLEX/MTP: Karenz = Grundlohn-Anteil × 88 %, darauf Ferien,
    /// Feiertag und 13. ML einzeln (L-GAV, D. Sägesser, Walter 07.11.2026).
    /// Ungerundet. FIX/FIX-M = Tagessatz100.</summary>
    decimal      KarenzGrundTagessatz = 0m
);

public class KtgBreakdown
{
    /// <summary>Optionaler Klartext-Hinweis (z.B. "manuell übersteuert").</summary>
    public string?  Hinweis { get; set; }

    // Allgemein
    public decimal? FerienPct      { get; set; }
    /// <summary>Feiertag-% im Brutto-Stundenlohn (FLEX/MTP, seit 07.11.2026 wieder
    /// enthalten — L-GAV). FIX/FIX-M: null.</summary>
    public decimal? FeiertagPct    { get; set; }
    public decimal? ZehnterMLPct   { get; set; }

    // Karenz-Rechenweg pro Tag (FLEX/MTP): Grundlohn × 88 %, Ferien, Feiertag, 13. ML
    public decimal? GrundTagessatz   { get; set; }
    public decimal? KarenzGrundlohn  { get; set; }
    public decimal? KarenzFerien     { get; set; }
    public decimal? KarenzFeiertag   { get; set; }
    public decimal? Karenz13ml       { get; set; }

    // Regel A (Hochrechnung)
    public decimal? StundenlohnBasis  { get; set; }
    public decimal? StundenlohnBrutto { get; set; }
    public decimal? WochenStunden     { get; set; }
    public decimal? MonatsLohn        { get; set; }

    // Regel B (Durchschnitt)
    public List<KtgMonatsSatz>? Monate { get; set; }

    // MTP-spezifisch (bei Regel B)
    public decimal? GarantieBasisMonat     { get; set; }
    public decimal? MehrstundenAnteilMonat { get; set; }
    public decimal? GarantieTagessatz      { get; set; }
    public decimal? MehrstundenTagessatz   { get; set; }
}

public record KtgMonatsSatz(int Jahr, int Monat, string MonatName, decimal Brutto);
