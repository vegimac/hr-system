using System.Security.Claims;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Edit-Sperre für lohnrelevante datum-bezogene Edits (Walter-Vorgabe 17.05.2026).
///
/// REGEL: Absenzen, Stempelzeiten, Vorschuss, Lohnzulagen dürfen nur in einer
/// Periode angelegt/geändert/gelöscht werden, die noch komplett offen ist —
/// also weder im Definitiv-Lohnlauf-Status "provisorisch_abgeschlossen" oder
/// "abgeschlossen" noch im Akonto-Workflow gestartet (alles ausser "OFFEN").
///
/// Sobald irgendeine Lohnverarbeitung gestartet wurde (Akonto vorbereiten,
/// provisorischer Abschluss, Lohnlauf abschliessen) ist die Periode tabu —
/// und damit auch alle früheren Perioden.
///
/// Vergangenheits-Schutz: Edit-Datum muss > dem Ende der spätesten
/// "in Verarbeitung"-Periode liegen. Beispiel:
///   - Dez 2025 = abgeschlossen, Jan 2026 = AkontoStatus BEI_HR, Feb 2026 = offen
///   - FirstAllowedDate = 01.02.2026
///   - Edits in Dez 2025 und Jan 2026 = blockiert, Feb 2026 und später = erlaubt
///
/// Bypass: User in Rolle "admin" oder "superuser" — die fixen die Korrekturen
/// im HR-Modul und müssen schreibend durch.
///
/// Stammdaten (Adresse, Familie, Sprache, Anrede usw.) und versionierte Daten
/// (Verträge, Bankkonten, Bewilligungen, QST mit valid_from/valid_to) gehören
/// NICHT in diesen Service — die haben ihre eigene Logik.
/// </summary>
public class LohnEditLockService
{
    private readonly AppDbContext _db;
    public LohnEditLockService(AppDbContext db) => _db = db;

    public record LockResult(
        bool      Locked,
        string    Reason,
        DateOnly? FirstAllowedDate);

    /// <summary>
    /// Liefert die kleinste Periode, ab der für (companyProfileId) Edits
    /// erlaubt sind — bzw. (false, "", null) bei Bypass-Rolle.
    ///
    /// Algorithmus: spätestes (Year, Month) einer Periode finden, die NICHT
    /// (Status="offen" AND AkontoStatus="OFFEN") ist. FirstAllowedDate ist der
    /// erste Tag des Folgemonats. Wenn es keine solche Periode gibt: null
    /// (alles erlaubt).
    /// </summary>
    public async Task<DateOnly?> GetFirstAllowedDateAsync(
        ClaimsPrincipal? user,
        int companyProfileId)
    {
        // Walter-Vorgabe 17.05.2026 (final): KEIN Bypass — auch nicht für
        // admin/superuser. Lohn-relevante Edits sind in einer in-Verarbeitung-
        // oder abgeschlossenen Periode für JEDEN gesperrt. Der Admin muss die
        // Periode aktiv zurücksetzen / wieder öffnen, bevor er editieren kann.
        // Das zwingt eine bewusste Entscheidung mit Audit-Trail (Lohnzettel
        // aus MA-Postfach raus, Akonto-Zahlungen storniert etc.) statt einer
        // stillen Daten-Manipulation in einem laufenden Lohnlauf.
        //
        // Der user-Parameter bleibt in der Signatur erhalten — falls wir
        // später wieder ein Rollen-Modell brauchen (z.B. „SuperAdmin sieht
        // Read-only-Bleistift mit Warnung"), bleibt der Hook offen.
        _ = user;

        // "In Verarbeitung" = irgendein Schritt nach der GF-Vorbereitung ist
        // angefangen: Definitiv provisorisch_abgeschlossen / abgeschlossen,
        // oder Akonto BEI_HR / HR_FREIGEGEBEN / AUSBEZAHLT.
        // IN_BEARBEITUNG_GF ist NICHT gesperrt — der GF arbeitet ja gerade an
        // der Vorbereitung und braucht u.U. noch Absenz/Stempel-Korrekturen.
        //
        // QUELLE 1: payroll_periode (Definitiv- + Akonto-Status auf Periode)
        var fromPeriode = await _db.PayrollPerioden
            .Where(p => p.CompanyProfileId == companyProfileId)
            .Where(p =>
                p.Status == "provisorisch_abgeschlossen" ||
                p.Status == "abgeschlossen" ||
                p.AkontoStatus == "BEI_HR" ||
                p.AkontoStatus == "HR_FREIGEGEBEN" ||
                p.AkontoStatus == "AUSBEZAHLT")
            .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
            .Select(p => new { p.Year, p.Month })
            .FirstOrDefaultAsync();

        // QUELLE 2: akonto_zahlung — falls payroll_periode nicht synchronisiert
        // ist (z.B. weil der Workflow direkt auf akonto_zahlung läuft, oder
        // weil die Periode-Zeile fehlt), zeigen Akonto-Lohnzettel mit Status
        // != BERECHNET ebenfalls eine in-Verarbeitung-Periode an. Walter
        // 17.05.2026: königliche Kontrolle = beide Quellen verodert nehmen,
        // späteste gewinnt.
        var fromAkonto = await _db.AkontoZahlungen
            .Where(a => a.CompanyProfileId == companyProfileId)
            .Where(a => a.Status == "FREIGEGEBEN_GF"
                     || a.Status == "HR_BESTAETIGT"
                     || a.Status == "AUSBEZAHLT")
            .OrderByDescending(a => a.PeriodYear).ThenByDescending(a => a.PeriodMonth)
            .Select(a => new { Year = a.PeriodYear, Month = a.PeriodMonth })
            .FirstOrDefaultAsync();

        // Späteste der beiden Quellen
        (int Year, int Month)? winner = null;
        if (fromPeriode is not null)
            winner = (fromPeriode.Year, fromPeriode.Month);
        if (fromAkonto is not null)
        {
            if (winner is null
                || fromAkonto.Year > winner.Value.Year
                || (fromAkonto.Year == winner.Value.Year && fromAkonto.Month > winner.Value.Month))
            {
                winner = (fromAkonto.Year, fromAkonto.Month);
            }
        }

        if (winner is null) return null; // nichts blockiert

        // Erster Tag des Folgemonats nach der letzten gesperrten Periode.
        return new DateOnly(winner.Value.Year, winner.Value.Month, 1).AddMonths(1);
    }

    /// <summary>
    /// Prüft ob ein konkretes Datum gesperrt ist.
    /// </summary>
    public async Task<LockResult> CheckDateAsync(
        ClaimsPrincipal? user,
        int companyProfileId,
        DateOnly date)
    {
        var first = await GetFirstAllowedDateAsync(user, companyProfileId);
        if (first is null || date >= first.Value)
            return new LockResult(false, "", first);

        return new LockResult(
            Locked: true,
            Reason: $"Datum {date:dd.MM.yyyy} liegt in einer bereits verarbeiteten oder in Verarbeitung befindlichen Lohnperiode. " +
                    $"Frühestes erlaubtes Datum: {first:dd.MM.yyyy}.",
            FirstAllowedDate: first);
    }

    /// <summary>
    /// Prüft ob ein Datumsbereich gesperrt ist (irgendein Tag ≤ FirstAllowedDate
    /// → blockiert).
    /// </summary>
    public async Task<LockResult> CheckRangeAsync(
        ClaimsPrincipal? user,
        int companyProfileId,
        DateOnly from,
        DateOnly to)
    {
        if (to < from) (from, to) = (to, from);
        return await CheckDateAsync(user, companyProfileId, from);
    }
}
