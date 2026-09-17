using System.Security.Claims;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Lohnlauf-Bestätigung pro Filiale (Walter 17.09.2026): Standard GF + HR
/// (Vier-Augen). Bei <see cref="Models.CompanyProfile.LohnlaufNurHr"/> bestätigt
/// nur HR — ein Schritt BERECHNET → HR_BESTAETIGT, «An HR senden» entfällt.
/// </summary>
public static class LohnlaufBestaetigung
{
    public static bool IstHr(ClaimsPrincipal user)
        => user.IsInRole("admin") || user.IsInRole("superuser") || user.IsInRole("buchhaltung");

    public static async Task<bool> IstNurHrAsync(AppDbContext db, int companyProfileId, CancellationToken ct = default)
        => await db.CompanyProfiles
            .Where(c => c.Id == companyProfileId)
            .Select(c => c.LohnlaufNurHr)
            .FirstOrDefaultAsync(ct);
}
