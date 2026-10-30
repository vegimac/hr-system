using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Filialen für den FAK-Kantonswechsel: Arbeitskanton der Filiale und Mitgliednummer
/// bei der Ausgleichskasse. Die IKV ändert nur die FAK-Adresse, nicht den Arbeitskanton.
/// </summary>
public static class GastroSocialFilialen
{
    public static async Task<Dictionary<int, GastroSocialMeldungRegel.Filiale>> LadenAsync(AppDbContext db)
    {
        var profile = await db.CompanyProfiles.AsNoTracking()
            .Select(c => new { c.Id, c.KantonCode })
            .ToListAsync();
        var zu = await db.CompanyProfileEmpfaengers.AsNoTracking()
            .Where(z => z.IsActive && z.Empfaenger!.IsActive && z.Empfaenger.Art == "AUSGLEICHSKASSE")
            .Select(z => new { z.CompanyProfileId, z.Mitgliednummer, z.GueltigAb, z.GueltigBis })
            .ToListAsync();
        var nachFiliale = zu.GroupBy(z => z.CompanyProfileId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<GastroSocialMeldungRegel.Mitgliedschaft>)g
                    .Select(z => new GastroSocialMeldungRegel.Mitgliedschaft(z.Mitgliednummer, z.GueltigAb, z.GueltigBis))
                    .ToList());
        return profile.ToDictionary(
            p => p.Id,
            p => new GastroSocialMeldungRegel.Filiale(
                p.Id,
                p.KantonCode,
                nachFiliale.TryGetValue(p.Id, out var m) ? m : Array.Empty<GastroSocialMeldungRegel.Mitgliedschaft>()));
    }
}
