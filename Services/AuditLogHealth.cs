using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Prüft, ob das zentrale <c>audit_log</c> noch schreibt.
/// Walter-Vorgabe 26.07.2026: nach dem Stumm-Bug (23.–26.07.) früh warnen.
/// </summary>
public static class AuditLogHealth
{
    public const int DefaultSilenceDays = 1;

    public static async Task<AuditLogHealthResult> CheckAsync(
        AppDbContext db, int silenceDays, CancellationToken ct = default)
    {
        var days = Math.Max(1, silenceDays);
        var last = await db.AuditLogs.AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .Select(a => (DateTime?)a.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (!last.HasValue)
        {
            return new AuditLogHealthResult(
                Ok: false,
                LastCreatedAt: null,
                SilentHours: double.PositiveInfinity,
                ThresholdHours: days * 24.0,
                SilenceDays: days);
        }

        // created_at = Schweizer Wanduhr (Unspecified); DateTime.Now ebenfalls lokal.
        var silentHours = (DateTime.Now - last.Value).TotalHours;
        if (silentHours < 0) silentHours = 0;
        var threshold = days * 24.0;
        return new AuditLogHealthResult(
            Ok: silentHours < threshold,
            LastCreatedAt: last,
            SilentHours: silentHours,
            ThresholdHours: threshold,
            SilenceDays: days);
    }
}

public readonly record struct AuditLogHealthResult(
    bool Ok,
    DateTime? LastCreatedAt,
    double SilentHours,
    double ThresholdHours,
    int SilenceDays);
