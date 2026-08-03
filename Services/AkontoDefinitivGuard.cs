using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HrSystem.Services;

/// <summary>
/// Walter-Vorgabe 03.08.2026: Sobald der Definitivlauf einer Periode läuft
/// oder abgeschlossen ist, gilt der Akonto-Strang als erledigt (kein Lock,
/// kein erneutes «Akonto vorbereiten»). Offene Mittel-Stati werden auf
/// <c>UEBERSPRUNGEN</c> geheilt — ohne AUSBEZAHLT-Zahlungen, damit der
/// Definitivlauf keine Phantom-Akonto-Verrechnung abzieht.
/// </summary>
public static class AkontoDefinitivGuard
{
    public const string StatusUebersprungen = "UEBERSPRUNGEN";

    public static bool IsDefinitivAdvanced(string? definitivStatus)
        => definitivStatus is "provisorisch_abgeschlossen" or "abgeschlossen";

    /// <summary>
    /// Akonto-Strang «fertig» für Lock: echt ausbezahlt, bewusst übersprungen,
    /// nie gestartet (OFFEN) — oder Definitiv hat bereits übernommen.
    /// </summary>
    public static bool IsAkontoStrangFertig(string? akontoStatus, string? definitivStatus)
    {
        if (IsDefinitivAdvanced(definitivStatus)) return true;
        return akontoStatus is "OFFEN" or "AUSBEZAHLT" or StatusUebersprungen;
    }

    /// <summary>
    /// Periode komplett für Sequenz / älteste offene Periode.
    /// </summary>
    public static bool IsPeriodeKomplett(string? akontoStatus, string? definitivStatus)
        => definitivStatus == "abgeschlossen"
           && IsAkontoStrangFertig(akontoStatus, definitivStatus);

    /// <summary>
    /// Wenn Definitiv schon fortgeschritten und Akonto noch in einem
    /// Zwischenstatus hängt → auf UEBERSPRUNGEN setzen, Vorbereitung
    /// löschen, bezahlte Zeilen stornieren. Idempotent.
    /// </summary>
    public static async Task<bool> TryAbandonMidFlightAsync(
        AppDbContext db, PayrollPeriode periode, int? userId, string? userName = null,
        ILogger? log = null, CancellationToken ct = default)
    {
        if (!IsDefinitivAdvanced(periode.Status)) return false;
        if (periode.AkontoStatus is "OFFEN" or "AUSBEZAHLT" or StatusUebersprungen)
            return false;

        var vorher = periode.AkontoStatus;
        var zahlungen = await db.AkontoZahlungen
            .Where(z => z.CompanyProfileId == periode.CompanyProfileId
                     && z.PeriodYear == periode.Year
                     && z.PeriodMonth == periode.Month)
            .ToListAsync(ct);

        int geloescht = 0, storniert = 0;
        foreach (var z in zahlungen)
        {
            if (z.Status == "AUSBEZAHLT")
            {
                z.Status = "STORNIERT";
                storniert++;
            }
            else if (z.Status != "STORNIERT")
            {
                db.AkontoZahlungen.Remove(z);
                geloescht++;
            }
        }

        periode.AkontoStatus = StatusUebersprungen;
        periode.AkontoGfStartedAt = null;
        periode.AkontoGfStartedBy = null;
        periode.AkontoGfSentAt = null;
        periode.AkontoGfSentBy = null;
        periode.AkontoHrFreigegebenAt = null;
        periode.AkontoHrFreigegebenBy = null;

        db.PayrollPeriodeAudits.Add(new PayrollPeriodeAudit
        {
            PayrollPeriodeId = periode.Id,
            UserId = userId,
            UserName = userName ?? (userId.HasValue ? $"User #{userId}" : "System"),
            Action = "AKONTO_UEBERSPRUNGEN_DEFINITIV",
            Bemerkung = $"Akonto-Status «{vorher}» → UEBERSPRUNGEN (Definitiv bereits «{periode.Status}»). "
                      + $"Vorbereitung gelöscht: {geloescht}, storniert: {storniert}.",
            CreatedAt = DateTime.Now,
        });

        await db.SaveChangesAsync(ct);
        log?.LogInformation(
            "[Akonto] Filiale={Cp} {Y}-{M}: Mid-flight Akonto «{Prev}» → UEBERSPRUNGEN (Definitiv={Def})",
            periode.CompanyProfileId, periode.Year, periode.Month, vorher, periode.Status);
        return true;
    }
}
