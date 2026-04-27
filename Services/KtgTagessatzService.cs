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
///     StdLohn_brutto = HourlyRate × (1 + Ferien%) × (1 + Feiertag%) × (1 + 8.33%)
///
///   <b>Regel B</b> – Vertragsdauer ≥ 4 abgeschlossene Perioden:
///     • FIX / FIX-M / UTP : Σ SvBasisAhv der letzten max. 12 Monate / n × 12 / 365
///     • MTP               : Garantie-Anteil wie Regel A
///                         + Ø (SvBasisAhv − Garantie-Basis) der letzten Perioden × 12 / 365
///
///   Faktoren:
///     • 88 % – Tagessatz während Karenzfrist
///     • 80 % – Tagessatz nach Karenz (auch Meldebetrag an Versicherung)
///
///   Vertragswechsel (neues Employment mit neuem ContractStartDate) setzt
///   die 4-Monats-Frist zurück.
/// </summary>
public class KtgTagessatzService
{
    private const decimal ZehnterMonatslohnPct = 8.33m; // 1/12 als % hart kodiert

    private readonly AppDbContext _db;
    private readonly ILogger<KtgTagessatzService> _logger;

    public KtgTagessatzService(AppDbContext db, ILogger<KtgTagessatzService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<KtgTagessatzResult?> CalculateAsync(int employeeId, int companyProfileId)
    {
        // Aktives Employment holen
        var employment = await _db.Employments
            .Include(e => e.CompanyProfile)
            .Where(e => e.EmployeeId == employeeId
                     && e.CompanyProfileId == companyProfileId
                     && e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .FirstOrDefaultAsync();

        if (employment is null)
        {
            _logger.LogWarning("Kein aktives Employment für Employee {EmpId}/Profile {ProfileId}", employeeId, companyProfileId);
            return null;
        }

        var company = employment.CompanyProfile
                    ?? await _db.CompanyProfiles.FirstOrDefaultAsync(c => c.Id == companyProfileId);
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
        string modell = (employment.EmploymentModel ?? "").ToUpperInvariant();

        // Ferien-/Feiertag-Prozente bestimmen
        decimal ferienPct    = ResolveFerienPct(employment, company);
        decimal feiertagPct  = employment.HolidayPercent ?? company.DefaultHolidayPercent ?? 0m;

        // Brutto-Stundenlohn (inkl. Ferien/Feiertag/13. ML)
        decimal stdLohnBasis  = employment.HourlyRate ?? 0m;
        decimal stdLohnBrutto = stdLohnBasis
                              * (1 + ferienPct    / 100m)
                              * (1 + feiertagPct  / 100m)
                              * (1 + ZehnterMonatslohnPct / 100m);

        decimal tagessatz100;
        var breakdown = new KtgBreakdown {
            FerienPct     = ferienPct,
            FeiertagPct   = feiertagPct,
            ZehnterMLPct  = ZehnterMonatslohnPct,
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

        if (regel == "A")
        {
            tagessatz100 = CalculateRegelA(employment, company, stdLohnBrutto, modell, breakdown);
        }
        else
        {
            tagessatz100 = CalculateRegelB(employment, company, stdLohnBrutto, modell, snapshots, breakdown);
        }

        tagessatz100 = Math.Round(tagessatz100, 2);

        return new KtgTagessatzResult(
            Regel:          regel,
            VertragsModell: modell,
            AnzahlPerioden: anzahlPerioden,
            VertragsStart:  vertragsStart,
            Tagessatz100:   tagessatz100,
            Tagessatz88:    Math.Round(tagessatz100 * 0.88m, 2),
            Tagessatz80:    Math.Round(tagessatz100 * 0.80m, 2),
            Breakdown:      breakdown
        );
    }

    // ── Regel A ────────────────────────────────────────────────────────────

    private static decimal CalculateRegelA(
        Employment emp, CompanyProfile company,
        decimal stdLohnBrutto, string modell,
        KtgBreakdown b)
    {
        b.StundenlohnBasis  = emp.HourlyRate;
        b.StundenlohnBrutto = Math.Round(stdLohnBrutto, 4);

        switch (modell)
        {
            case "FIX":
            case "FIX-M":
            {
                decimal monatsLohn = emp.MonthlySalary ?? 0m;
                b.MonatsLohn = monatsLohn;
                return monatsLohn * 12m / 365m;
            }

            case "MTP":
            {
                decimal wo = emp.GuaranteedHoursPerWeek ?? 0m;
                b.WochenStunden = wo;
                return wo * stdLohnBrutto * 52m / 365m;
            }

            default: // UTP, FLEX, leer → FLEX-Logik
            {
                decimal wo = company.MaxPartTimeHoursPerWeek ?? 17m;
                b.WochenStunden = wo;
                return wo * stdLohnBrutto * 52m / 365m;
            }
        }
    }

    // ── Regel B ────────────────────────────────────────────────────────────

    private static decimal CalculateRegelB(
        Employment emp, CompanyProfile company,
        decimal stdLohnBrutto, string modell,
        List<PayrollSnapshot> snapshots,
        KtgBreakdown b)
    {
        // Monatsdetails sind bereits in CalculateAsync gesetzt
        int n = snapshots.Count;
        decimal summeAhv = snapshots.Sum(s => s.SvBasisAhv);

        if (modell == "MTP")
        {
            // Garantie-Anteil (wie Regel A)
            decimal wo = emp.GuaranteedHoursPerWeek ?? 0m;
            b.WochenStunden     = wo;
            b.StundenlohnBasis  = emp.HourlyRate;
            b.StundenlohnBrutto = Math.Round(stdLohnBrutto, 4);

            decimal garantieBasisMonat = wo * stdLohnBrutto * 52m / 12m;
            b.GarantieBasisMonat       = Math.Round(garantieBasisMonat, 2);

            decimal garantieTagessatz  = wo * stdLohnBrutto * 52m / 365m;
            b.GarantieTagessatz        = Math.Round(garantieTagessatz, 2);

            // Mehrstunden-Anteil: Lohnposition "MTP + Stunden" aus SlipJson jedes Monats
            // auf Brutto-Niveau (Ferien/Feiertag/13. ML) bringen, dann Ø × 12 / 365.
            decimal bruttoAufschlag = (1 + (b.FerienPct ?? 0m) / 100m)
                                    * (1 + (b.FeiertagPct ?? 0m) / 100m)
                                    * (1 + ZehnterMonatslohnPct / 100m);

            decimal summeMehrBrutto = 0m;
            foreach (var s in snapshots)
            {
                decimal posBetrag = ExtractLohnPositionBetrag(s.SlipJson, "MTP + Stunden");
                summeMehrBrutto += posBetrag * bruttoAufschlag;
            }
            decimal mehrstundenMonatAvg  = n > 0 ? summeMehrBrutto / n : 0m;
            b.MehrstundenAnteilMonat     = Math.Round(mehrstundenMonatAvg, 2);

            decimal mehrstundenTagessatz = mehrstundenMonatAvg * 12m / 365m;
            b.MehrstundenTagessatz       = Math.Round(mehrstundenTagessatz, 2);

            return garantieTagessatz + mehrstundenTagessatz;
        }
        else
        {
            // FIX / FIX-M / UTP: reiner AHV-Brutto-Durchschnitt
            decimal avg = n > 0 ? summeAhv / n : 0m;
            return avg * 12m / 365m;
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
    /// Ermittelt den Ferien-Prozentsatz: Employment-Override, sonst Company-Default
    /// nach Beschäftigungsgrad (<50% = 5 Wochen, ≥50% = 6 Wochen).
    /// </summary>
    private static decimal ResolveFerienPct(Employment emp, CompanyProfile company)
    {
        if (emp.VacationPercent is { } v && v > 0) return v;

        decimal bg = emp.EmploymentPercentage ?? 0m;
        if (bg < 50m)
            return company.DefaultVacationPercent5Weeks ?? 10.64m;
        return company.DefaultVacationPercent6Weeks ?? 13.04m;
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
    KtgBreakdown Breakdown
);

public class KtgBreakdown
{
    // Allgemein
    public decimal? FerienPct      { get; set; }
    public decimal? FeiertagPct    { get; set; }
    public decimal? ZehnterMLPct   { get; set; }

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
