using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.Elm;

/// <summary>
/// Schreibt den Swissdec-Wert der Testdaten in den Statistik-Katalog der Person.
/// Gibt es die Zeile (nach dem Start-Seed), gewinnt sie. Sonst bleiben die
/// LSE-Zahlen, aus denen die Meldung denselben Wert ableitet.
/// </summary>
public static class StatistikZuordnung
{
    public static async Task Anwenden(AppDbContext db, EmployeeLse lse, string? education, string? stellung)
    {
        var edu = (education ?? "").Trim();
        if (edu.Length > 0)
        {
            var row = await db.StatistikAusbildungen.AsNoTracking()
                .FirstOrDefaultAsync(a => a.Code == edu);
            if (row != null)
            {
                lse.StatistikAusbildungId = row.Id;
                lse.Education = row.LseAusbildung;
                lse.UniversityDegree = row.LseTitel;
            }
            else
            {
                var bfs = ElmStatistikCodes.BfsAusbildung(edu);
                if (bfs != null)
                {
                    lse.Education = bfs;
                    lse.UniversityDegree = ElmStatistikCodes.BfsHochschultitel(edu);
                }
            }
        }

        var pos = (stellung ?? "").Trim();
        if (pos.Length > 0)
        {
            var row = await db.StatistikStellungen.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Code == pos);
            if (row != null)
            {
                lse.StatistikStellungId = row.Id;
                lse.PositionOverride = row.LseCode;
            }
            else
            {
                var bfs = ElmStatistikCodes.BfsStellung(pos);
                if (bfs != null) lse.PositionOverride = bfs;
            }
        }
    }
}
