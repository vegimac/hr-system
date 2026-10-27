using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>Eine Übergangszeile. Leeres Von = alle älteren Jahrgänge, leeres Bis = alle jüngeren.</summary>
public readonly record struct AhvUebergang(int? Von, int? Bis, int Jahre, int Monate);

/// <summary>
/// Beginn und Ende der AHV-Beitragspflicht, plus Übergänge wie AHV 21.
/// Ohne gespeicherten Stand gilt das Gesetz: 18 / 65 / 65 und die Frauen-Jahrgänge 1960–1963.
/// </summary>
public sealed record AhvBeitragspflichtStand(
    int Beginn,
    int EndeMaenner,
    int EndeFrauen,
    IReadOnlyList<AhvUebergang> Uebergaenge)
{
    public static AhvBeitragspflichtStand Gesetzlich { get; } = new(
        18, 65, 65,
        new AhvUebergang[]
        {
            new(null, 1960, 64, 0),
            new(1961, 1961, 64, 3),
            new(1962, 1962, 64, 6),
            new(1963, 1963, 64, 9),
        });

    public int ReferenzalterMonate(string? gender, int birthYear)
    {
        if (!PayrollCalculations.IstWeiblich(gender))
            return EndeMaenner * 12;
        foreach (var u in Uebergaenge)
        {
            if ((u.Von == null || birthYear >= u.Von) && (u.Bis == null || birthYear <= u.Bis))
                return u.Jahre * 12 + u.Monate;
        }
        return EndeFrauen * 12;
    }

    /// <summary>
    /// Stand aus ahv_beitragspflicht (eine Zeile, alle Filialen).
    /// Fehlt die Zeile, gilt das Gesetz inklusive AHV 21.
    /// </summary>
    public static async Task<AhvBeitragspflichtStand> LadenAsync(AppDbContext db)
    {
        var kopf = await db.AhvBeitragspflichten.AsNoTracking().FirstOrDefaultAsync(a => a.Id == 1);
        if (kopf == null) return Gesetzlich;
        var rows = await db.AhvBeitragspflichtUebergaenge.AsNoTracking()
            .OrderBy(u => u.JahrgangVon ?? int.MinValue)
            .ToListAsync();
        return new AhvBeitragspflichtStand(
            kopf.BeginnAlter, kopf.EndeMaenner, kopf.EndeFrauen,
            rows.Select(u => new AhvUebergang(u.JahrgangVon, u.JahrgangBis, u.Jahre, u.Monate)).ToList());
    }
}
