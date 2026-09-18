using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Familienzulagen-Korrekturen (Walter 18.09.2026):
/// Wirkung = ValidFrom/ValidTo, Wissen = ErfahrenAm.
/// Zwischenmonate (ValidFrom … Vormonat ErfahrenAm) in abgeschlossenen
/// Perioden → Nachzahlung (+). Monate ausserhalb ValidTo, die schon
/// wirkten/bezahlt wurden → Rückforderung (−).
/// Swissdec TF34: Marc gültig ab 1.4., erfahren 1.7. → Apr–Juni 3×215.
/// </summary>
public class FamzKorrekturService
{
    private readonly AppDbContext _db;

    public FamzKorrekturService(AppDbContext db) => _db = db;

    public static DateOnly BekanntAb(FamilyMemberAllowance a)
        => a.ErfahrenAm ?? a.ValidFrom;

    public record KorrekturErgebnis(int Anzahl, decimal TotalBetrag, int Vorjahr, List<object> Posten);

    public async Task<KorrekturErgebnis> ErzeugeKorrekturenAsync(
        FamilyMemberAllowance allowance, string? erfasstVon,
        CancellationToken ct = default)
    {
        var member = await _db.EmployeeFamilyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == allowance.FamilyMemberId, ct);
        if (member == null)
            return new KorrekturErgebnis(0, 0, 0, new List<object>());

        var childName = string.Join(" ",
            new[] { member.FirstName, member.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

        var branchId = await _db.Employments.AsNoTracking()
            .Where(e => e.EmployeeId == member.EmployeeId && e.IsActive)
            .OrderByDescending(e => e.ContractStartDate)
            .Select(e => (int?)e.CompanyProfileId)
            .FirstOrDefaultAsync(ct)
            ?? await _db.Employments.AsNoTracking()
                .Where(e => e.EmployeeId == member.EmployeeId)
                .OrderByDescending(e => e.ContractStartDate)
                .Select(e => (int?)e.CompanyProfileId)
                .FirstOrDefaultAsync(ct)
            ?? 0;

        var bekannt = BekanntAb(allowance);
        var bekanntMonat = new DateOnly(bekannt.Year, bekannt.Month, 1);
        var laufendesJahr = bekannt.Year;
        var monatsBetrag = Math.Round(allowance.MonthlyAmount, 2);

        var rows = await (from s in _db.PayrollSnapshots.AsNoTracking()
                          join p in _db.PayrollPerioden.AsNoTracking() on s.PayrollPeriodeId equals p.Id
                          where s.EmployeeId == member.EmployeeId
                                && s.Status != "STORNIERT"
                                && p.Status == "abgeschlossen"
                          select new { p.Year, p.Month, s.CompanyProfileId }).ToListAsync(ct);

        // Bestehende Korrekturen dieser Zulage (für Rückforderungs-Monate)
        var altePostenMonate = await _db.FamzKorrekturen.AsNoTracking()
            .Where(k => k.AllowanceId == allowance.Id)
            .Select(k => new { k.Jahr, k.Monat })
            .ToListAsync(ct);

        var kandidaten = rows
            .Select(r => (r.Year, r.Month, r.CompanyProfileId))
            .Where(r =>
            {
                var mStart = new DateOnly(r.Year, r.Month, 1);
                var mEnd = mStart.AddMonths(1).AddDays(-1);
                bool imFenster = allowance.ValidFrom <= mEnd
                    && (allowance.ValidTo == null || allowance.ValidTo >= mStart);
                bool vorKenntnis = mEnd < bekanntMonat;
                bool hattePosten = altePostenMonate.Any(p => p.Jahr == r.Year && p.Monat == r.Month);
                // Nachzahlung: im Fenster + vor Kenntnis
                // Rückforderung: nicht mehr im Fenster, aber früher Posten/Zahlung möglich
                return (imFenster && vorKenntnis) || (!imFenster && (hattePosten || mStart >= allowance.ValidFrom));
            })
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .ToList();

        var postenOut = new List<object>();
        decimal total = 0;
        int vorjahr = 0;

        foreach (var (y, m, cp) in kandidaten)
        {
            var mStart = new DateOnly(y, m, 1);
            var mEnd = mStart.AddMonths(1).AddDays(-1);
            bool imFenster = allowance.ValidFrom <= mEnd
                && (allowance.ValidTo == null || allowance.ValidTo >= mStart);
            bool vorKenntnis = mEnd < bekanntMonat;

            decimal soll = (imFenster && vorKenntnis) ? monatsBetrag : 0m;
            // Ab Erfahrungsmonat zahlt der Live-Lauf — hier keine Korrektur.
            if (imFenster && !vorKenntnis) continue;

            var bestehende = await _db.FamzKorrekturen
                .Where(k => k.AllowanceId == allowance.Id && k.Jahr == y && k.Monat == m)
                .ToListAsync(ct);
            var ersetzbar = bestehende.Where(k => k.Status is "OFFEN" or "VORJAHR").ToList();
            if (ersetzbar.Count > 0) _db.FamzKorrekturen.RemoveRange(ersetzbar);

            decimal bereits = bestehende
                .Where(k => k.Status is "VERRECHNET" or "GEMELDET")
                .Sum(k => k.Betrag);

            // Vor Kenntnis stand 0 auf dem Beleg; danach im Fenster = voller Betrag live.
            decimal liveDamals = (imFenster && !vorKenntnis) ? monatsBetrag : 0m;
            decimal effektivAlt = liveDamals + bereits;
            var diff = Math.Round(soll - effektivAlt, 2);
            if (Math.Abs(diff) < 0.05m) continue;

            var status = y < laufendesJahr ? "VORJAHR" : "OFFEN";
            if (status == "VORJAHR") vorjahr++;

            string art = diff > 0 ? "Nachzahlung" : "Rückforderung";
            var k = new FamzKorrektur
            {
                EmployeeId = member.EmployeeId,
                CompanyProfileId = cp != 0 ? cp : branchId,
                FamilyMemberId = allowance.FamilyMemberId,
                AllowanceId = allowance.Id,
                Jahr = y,
                Monat = m,
                AlterBetrag = effektivAlt,
                NeuerBetrag = soll,
                Betrag = diff,
                AllowanceType = allowance.AllowanceType,
                ChildName = string.IsNullOrWhiteSpace(childName) ? null : childName,
                Status = status,
                Grund = $"{art} {m}.{y}"
                    + (string.IsNullOrWhiteSpace(childName) ? "" : $" ({childName})")
                    + $" — gültig ab {allowance.ValidFrom:dd.MM.yyyy}, erfahren {bekannt:dd.MM.yyyy}",
                CreatedAt = DateTime.Now,
                CreatedBy = erfasstVon
            };
            _db.FamzKorrekturen.Add(k);
            total += diff;
            postenOut.Add(new
            {
                jahr = y, monat = m,
                alterBetrag = effektivAlt, neuerBetrag = soll,
                betrag = diff, status, art
            });
        }

        await _db.SaveChangesAsync(ct);
        return new KorrekturErgebnis(postenOut.Count, Math.Round(total, 2), vorjahr, postenOut);
    }

    public async Task EnsureKorrekturenFuerLohnlaufAsync(
        int employeeId, int year, int month, string? erfasstVon, CancellationToken ct = default)
    {
        var periodTo = new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        var allowances = await (
            from a in _db.FamilyMemberAllowances
            join m in _db.EmployeeFamilyMembers on a.FamilyMemberId equals m.Id
            where m.EmployeeId == employeeId
            select a).ToListAsync(ct);

        foreach (var a in allowances)
        {
            var bekannt = BekanntAb(a);
            if (bekannt > periodTo) continue;
            var vonMonat = new DateOnly(a.ValidFrom.Year, a.ValidFrom.Month, 1);
            var bekanntMonat = new DateOnly(bekannt.Year, bekannt.Month, 1);
            if (vonMonat < bekanntMonat || (bekannt.Year == year && bekannt.Month == month))
                await ErzeugeKorrekturenAsync(a, erfasstVon, ct);
        }
    }
}
