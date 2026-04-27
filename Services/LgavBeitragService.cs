using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Automatische Jahresabrechnung des L-GAV-Vollzugsbeitrags.
///
/// Regeln (konfigurierbar pro Filiale, CompanyProfile.Lgav*):
///   • FIX / FIX-M                                   → voller Beitrag (Default 99.00)
///   • MTP mit > 50% der Betriebs-Wochenarbeitszeit
///     UND Anstellung > 6 Monate                      → voller Beitrag
///   • MTP sonst                                     → reduzierter Beitrag (Default 49.50)
///   • UTP                                            → reduzierter Beitrag
///
/// Trigger: im CompanyProfile.LgavTriggerMonat (typ. Januar) des jeweiligen
/// Jahres, oder — bei unterjährigem Eintritt nach dem Trigger-Monat — in der
/// ersten Lohnperiode des MA.
///
/// Duplikatsschutz: Pro MA und Kalenderjahr wird maximal EIN Eintrag
/// mit Lohnposition-Code 600.24 erzeugt. Geprüft wird gegen bestehende
/// LohnZulage-Einträge mit dieser Kombination.
///
/// Der Service schreibt die LohnZulage direkt in die DB. Der
/// PayrollController liest sie dann über die normale Zulagen-Pipeline.
/// </summary>
public class LgavBeitragService
{
    private readonly AppDbContext _db;
    private const string LgavCode = "140";  // Mirus-style: LGAV-Beitrag

    public LgavBeitragService(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Prüft für den gegebenen MA / Filiale / Lohnperiode ob der L-GAV-
    /// Beitrag fällig ist und fügt ihn ggf. als LohnZulage ein.
    /// Wird am Anfang der Payroll-Berechnung aufgerufen. Idempotent.
    /// </summary>
    public async Task EnsureAsync(
        Employee employee,
        Employment employment,
        CompanyProfile profile,
        int year,
        int month,
        DateOnly periodFrom,
        DateOnly periodTo)
    {
        if (!profile.LgavAktiv) return;
        if (employee is null || employment is null) return;

        // 1) Duplikatsschutz: bereits in diesem Jahr erfasst?
        var yearPrefix = $"{year}-";
        var lpId = await _db.Lohnpositionen
            .Where(l => l.Code == LgavCode && l.IsActive)
            .Select(l => (int?)l.Id)
            .FirstOrDefaultAsync();
        if (lpId is null) return;  // Lohnposition fehlt → Migration noch nicht ausgeführt

        bool bereitsAbgezogen = await _db.LohnZulagen
            .AnyAsync(z => z.EmployeeId == employee.Id
                        && z.LohnpositionId == lpId
                        && z.Periode.StartsWith(yearPrefix));
        if (bereitsAbgezogen) return;

        // 2) Trigger-Prüfung. Zwei Fälle:
        //    a) Trigger-Monat des Jahres erreicht: MA muss bei Periode-Beginn
        //       schon angestellt sein (Eintritt ≤ periodFrom).
        //    b) MA tritt unterjährig ein, NACH dem Trigger-Monat dieses
        //       Jahres: erste Lohnperiode nach Eintritt → triggern.
        bool isTrigger = false;
        DateOnly? eintritt = employee.EntryDate.HasValue
            ? DateOnly.FromDateTime(employee.EntryDate.Value)
            : null;

        if (month == profile.LgavTriggerMonat)
        {
            // Trigger-Monat: nur abziehen wenn MA zu Beginn der Periode schon
            // angestellt ist (kein unterjähriger Eintritt mitten im Trigger-Monat,
            // das würde Fall b) übernehmen).
            if (eintritt is null || eintritt.Value <= periodFrom)
                isTrigger = true;
        }

        // Fall b): Eintritt nach dem Trigger-Monat dieses Jahres
        if (!isTrigger && eintritt is not null
            && eintritt.Value.Year == year
            && eintritt.Value.Month > profile.LgavTriggerMonat
            && eintritt.Value >= periodFrom && eintritt.Value <= periodTo)
        {
            isTrigger = true;
        }

        if (!isTrigger) return;

        // 3) Betrag je nach Vertragstyp / Pensum / Dauer
        decimal betrag = BerechneBetrag(employment, profile, periodFrom);
        if (betrag <= 0) return;

        // 4) LohnZulage erzeugen
        var periode = $"{year}-{month:D2}";
        _db.LohnZulagen.Add(new LohnZulage
        {
            EmployeeId      = employee.Id,
            LohnpositionId  = lpId.Value,
            Periode         = periode,
            Betrag          = betrag,
            Bemerkung       = null,  // nur Lohnposition-Bezeichnung auf dem Lohnzettel
            CreatedAt       = DateTime.UtcNow,
            UpdatedAt       = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Ermittelt den Beitrag für den MA nach den vier Kategorien:
    ///   FIX/FIX-M/MTP(>50% & >6Mt.) → LgavBeitragVoll
    ///   MTP(≤50% ODER ≤6Mt.)        → LgavBeitragReduziert
    ///   UTP                          → LgavBeitragReduziert
    /// </summary>
    private static decimal BerechneBetrag(
        Employment employment,
        CompanyProfile profile,
        DateOnly periodFrom)
    {
        var model = (employment.EmploymentModel ?? "").Trim().ToUpperInvariant();

        if (model == "FIX" || model == "FIX-M")
            return profile.LgavBeitragVoll;

        if (model == "UTP")
            return profile.LgavBeitragReduziert;

        if (model == "MTP")
        {
            // Pensum: MTP hat garantierte Wochenstunden am Employment.
            // Schwelle = 50% der Betriebs-Wochenarbeitszeit (Default 42h → 21h).
            decimal normalWeekly = profile.NormalWeeklyHours ?? 42m;
            decimal schwelle     = normalWeekly * 0.5m;
            decimal guaranteed   = employment.GuaranteedHoursPerWeek ?? 0m;

            // Dauer: ≥ 6 Monate seit Vertragsbeginn bis periodFrom?
            var vertragStart = DateOnly.FromDateTime(employment.ContractStartDate);
            bool dauerOk     = VertragDauerMonate(vertragStart, periodFrom) >= 6;

            bool voll = guaranteed > schwelle && dauerOk;
            return voll ? profile.LgavBeitragVoll : profile.LgavBeitragReduziert;
        }

        // Unbekannter Vertragstyp → reduziert als konservative Wahl
        return profile.LgavBeitragReduziert;
    }

    private static int VertragDauerMonate(DateOnly von, DateOnly bis)
    {
        int monate = (bis.Year - von.Year) * 12 + (bis.Month - von.Month);
        if (bis.Day < von.Day) monate--;
        return Math.Max(0, monate);
    }
}
