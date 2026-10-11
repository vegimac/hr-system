using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Familienzulagen-Prüffälle (Walter 18.09.2026, seit 11.10.2026 nur Warnung):
/// Wirkung = ValidFrom/ValidTo, Wissen = ErfahrenAm.
/// Zwischenmonate (ValidFrom … Vormonat ErfahrenAm) → Nachzahlung (+).
/// Monate ausserhalb ValidTo, die in einem abgeschlossenen Lohn bezahlt
/// wurden (famzZahlungen im Lohnzettel) → Rückforderung prüfen (−).
/// Der Lohnlauf zahlt nichts davon automatisch: Warnung, die als Zulage
/// übernommen oder als «nicht nötig» quittiert wird.
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

        // Betrag wie im Lohn: FAK-Tarif des Filial-Kantons im jeweiligen Monat, Satz vom
        // Eintrag am Kind. Der gespeicherte Betrag nur, wenn kein Tarif greift (Alt-Einträge).
        var kantone = await _db.CompanyProfiles.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.KantonCode, ct);
        var tarife = await _db.FamilienzulagenTarife.AsNoTracking()
            .Where(t => t.IsActive).ToListAsync(ct);
        string typ = allowance.AllowanceType is "AZ" or "GZ" or "AdoptZ" ? allowance.AllowanceType : "KZ";
        decimal MonatsBetrag(int cp, DateOnly mStart)
        {
            var mEnd = mStart.AddMonths(1).AddDays(-1);
            var kanton = kantone.TryGetValue(cp, out var k) ? k : null;
            var tarif = string.IsNullOrWhiteSpace(kanton) ? null : tarife
                .Where(t => t.KantonCode == kanton && t.ValidFrom <= mEnd && (t.ValidTo == null || t.ValidTo >= mStart))
                .OrderByDescending(t => t.ValidFrom)
                .FirstOrDefault();
            var betrag = FamilienzulagenResolverService.ResolveBySatz(tarif, typ, allowance.TarifSatzNr).Amount
                         ?? allowance.MonthlyAmount;
            return Math.Round(betrag, 2);
        }

        var rows = await (from s in _db.PayrollSnapshots.AsNoTracking()
                          join p in _db.PayrollPerioden.AsNoTracking() on s.PayrollPeriodeId equals p.Id
                          where s.EmployeeId == member.EmployeeId
                                && s.Status != "STORNIERT"
                                && p.Status == "abgeschlossen"
                          select new { p.Year, p.Month, s.CompanyProfileId, s.SlipJson }).ToListAsync(ct);

        // Was im abgeschlossenen Monat für DIESEN Eintrag wirklich bezahlt wurde
        // (famzZahlungen im Slip). Fehlt das Feld (Snapshot vor Stand 59) → null.
        var bezahltLive = rows
            .GroupBy(r => (r.Year, r.Month))
            .ToDictionary(g => g.Key, g =>
            {
                decimal? summe = null;
                foreach (var r in g)
                {
                    var z = FamzZahlungenAusSlip(r.SlipJson);
                    if (z == null) continue;
                    summe = (summe ?? 0m) + z.Where(x => x.AllowanceId == allowance.Id).Sum(x => x.Betrag);
                }
                return summe;
            });

        // Bestehende Korrekturen dieser Zulage (für Rückforderungs-Monate)
        var altePostenMonate = await _db.FamzKorrekturen.AsNoTracking()
            .Where(k => k.AllowanceId == allowance.Id)
            .Select(k => new { k.Jahr, k.Monat })
            .ToListAsync(ct);

        var kandidatenMap = new Dictionary<(int Year, int Month), int>();
        foreach (var r in rows)
        {
            var mStart = new DateOnly(r.Year, r.Month, 1);
            var mEnd = mStart.AddMonths(1).AddDays(-1);
            bool imFenster = allowance.ValidFrom <= mEnd
                && (allowance.ValidTo == null || allowance.ValidTo >= mStart);
            bool vorKenntnis = mEnd < bekanntMonat;
            bool hattePosten = altePostenMonate.Any(p => p.Jahr == r.Year && p.Monat == r.Month);
            // Nachzahlung: im Fenster + vor Kenntnis
            // Rückforderung: nicht mehr im Fenster, aber früher Posten/Zahlung möglich
            if ((imFenster && vorKenntnis) || (!imFenster && (hattePosten || mStart >= allowance.ValidFrom)))
                kandidatenMap[(r.Year, r.Month)] = r.CompanyProfileId;
        }

        // Monate vor Kenntnis ohne abgeschlossenen Lohn, aber mit OneCrew-Lohnperiode der
        // Filiale (z.B. noch offen): Nachzahlung, sofern der MA angestellt war. Monate vor
        // dem ersten OneCrew-Lohnmonat gehören dem Vorsystem — dort keine Warnung
        // (Walter 11.10.2026, Djonlagic: Start Januar 2026, kein Posten für 2025).
        var perioden = (await _db.PayrollPerioden.AsNoTracking()
                .Select(p => new { p.CompanyProfileId, p.Year, p.Month })
                .ToListAsync(ct))
            .Select(p => (p.CompanyProfileId, p.Year, p.Month))
            .ToHashSet();
        var anstellungen = (await _db.Employments.AsNoTracking()
                .Where(e => e.EmployeeId == member.EmployeeId)
                .Select(e => new { e.ContractStartDate, e.ContractEndDate, e.CompanyProfileId })
                .ToListAsync(ct))
            .Select(e => (Von: DateOnly.FromDateTime(e.ContractStartDate),
                          Bis: e.ContractEndDate.HasValue ? DateOnly.FromDateTime(e.ContractEndDate.Value) : (DateOnly?)null,
                          Cp: e.CompanyProfileId ?? 0))
            .ToList();
        for (var mStart = new DateOnly(allowance.ValidFrom.Year, allowance.ValidFrom.Month, 1);
             mStart < bekanntMonat && (allowance.ValidTo == null || mStart <= allowance.ValidTo);
             mStart = mStart.AddMonths(1))
        {
            if (kandidatenMap.ContainsKey((mStart.Year, mStart.Month))) continue;
            var mEnd = mStart.AddMonths(1).AddDays(-1);
            var anst = anstellungen
                .Where(a => a.Von <= mEnd && (a.Bis == null || a.Bis >= mStart))
                .OrderByDescending(a => a.Von)
                .FirstOrDefault();
            if (anst == default) continue;
            if (!perioden.Contains((anst.Cp, mStart.Year, mStart.Month))) continue;
            kandidatenMap[(mStart.Year, mStart.Month)] = anst.Cp;
        }

        var geschwister = await _db.FamilyMemberAllowances.AsNoTracking()
            .Where(a => a.FamilyMemberId == allowance.FamilyMemberId && a.Id != allowance.Id
                     && a.AllowanceType != "GZ" && a.AllowanceType != "AdoptZ")
            .Select(a => new { a.ValidFrom, a.ValidTo })
            .ToListAsync(ct);
        bool vorgaenger = geschwister.Any(g => g.ValidTo == allowance.ValidFrom.AddDays(-1));
        bool nachfolger = allowance.ValidTo.HasValue
            && geschwister.Any(g => g.ValidFrom == allowance.ValidTo.Value.AddDays(1));

        var kandidaten = kandidatenMap
            .Select(kv => (kv.Key.Year, kv.Key.Month, CompanyProfileId: kv.Value))
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .ToList();

        // Offene Posten aus einem früheren Stand (z.B. «Erfahren am» vertippt und
        // korrigiert, Walter 11.10.2026 Djonlagic) für Monate, die jetzt keine
        // Korrektur mehr brauchen — sonst zahlt der Lohnlauf sie trotzdem aus.
        var veraltet = (await _db.FamzKorrekturen
                .Where(k => k.AllowanceId == allowance.Id && (k.Status == "OFFEN" || k.Status == "VORJAHR"))
                .ToListAsync(ct))
            .Where(k => !kandidatenMap.ContainsKey((k.Jahr, k.Monat)))
            .ToList();
        if (veraltet.Count > 0) _db.FamzKorrekturen.RemoveRange(veraltet);

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
            var monatsBetrag = MonatsBetrag(cp != 0 ? cp : branchId, mStart);

            decimal soll = (imFenster && vorKenntnis)
                ? PayrollCalculations.FamzAnteilBetrag(monatsBetrag,
                    PayrollCalculations.FamzMonatsAnteil(mStart, allowance.ValidFrom, allowance.ValidTo, vorgaenger, nachfolger))
                : 0m;
            // Ab Erfahrungsmonat zahlt der Live-Lauf — hier keine Korrektur.
            if (imFenster && !vorKenntnis) continue;

            var bestehende = await _db.FamzKorrekturen
                .Where(k => k.AllowanceId == allowance.Id && k.Jahr == y && k.Monat == m)
                .ToListAsync(ct);
            var offen = bestehende.Where(k => k.Status is "OFFEN" or "VORJAHR")
                .OrderBy(k => k.Id).ToList();

            // Quittierte Warnungen gelten mit dem gemeldeten Betrag als erledigt — auch wenn
            // die übernommene Zulage danach von Hand geändert wurde oder «nicht nötig» war.
            decimal bereits = bestehende
                .Where(k => k.Status is "VERRECHNET" or "GEMELDET" or "ERLEDIGT")
                .Sum(k => k.Betrag);

            // Live bezahlt: aus dem abgeschlossenen Lohn; ältere Snapshots ohne Angabe →
            // vor Kenntnis 0, danach im Fenster der volle Betrag.
            decimal liveDamals = bezahltLive.TryGetValue((y, m), out var bez) && bez.HasValue
                ? bez.Value
                : (imFenster && !vorKenntnis) ? monatsBetrag : 0m;
            decimal effektivAlt = liveDamals + bereits;
            var diff = Math.Round(soll - effektivAlt, 2);
            if (Math.Abs(diff) < 0.05m)
            {
                if (offen.Count > 0) _db.FamzKorrekturen.RemoveRange(offen);
                continue;
            }
            if (y < laufendesJahr) vorjahr++;

            var k = offen.FirstOrDefault();
            if (offen.Count > 1) _db.FamzKorrekturen.RemoveRange(offen.Skip(1));
            if (k == null)
            {
                k = new FamzKorrektur
                {
                    EmployeeId = member.EmployeeId,
                    FamilyMemberId = allowance.FamilyMemberId,
                    AllowanceId = allowance.Id,
                    Jahr = y,
                    Monat = m,
                    CreatedAt = DateTime.Now,
                    CreatedBy = erfasstVon
                };
                _db.FamzKorrekturen.Add(k);
            }

            string art = diff > 0 ? "Nachzahlung" : "Rückforderung prüfen";
            k.CompanyProfileId = cp != 0 ? cp : branchId;
            k.AlterBetrag = effektivAlt;
            k.NeuerBetrag = soll;
            k.Betrag = diff;
            k.AllowanceType = allowance.AllowanceType;
            k.ChildName = string.IsNullOrWhiteSpace(childName) ? null : childName;
            k.Status = "OFFEN";
            k.Grund = $"{art} {m}.{y}"
                + (string.IsNullOrWhiteSpace(childName) ? "" : $" ({childName})")
                + $" — gültig ab {allowance.ValidFrom:dd.MM.yyyy}"
                + (allowance.ValidTo.HasValue ? $" bis {allowance.ValidTo:dd.MM.yyyy}" : "")
                + $", erfahren {bekannt:dd.MM.yyyy}";
            total += diff;
            postenOut.Add(new
            {
                jahr = y, monat = m,
                alterBetrag = effektivAlt, neuerBetrag = soll,
                betrag = diff, status = k.Status, art
            });
        }

        await _db.SaveChangesAsync(ct);
        return new KorrekturErgebnis(postenOut.Count, Math.Round(total, 2), vorjahr, postenOut);
    }

    public static void Wiedereroeffnen(FamzKorrektur k)
    {
        k.Status = "OFFEN";
        k.ErledigtArt = null;
        k.LohnZulageId = null;
        k.Bemerkung = null;
        k.ErledigtAm = null;
        k.ErledigtVon = null;
    }

    /// <summary>
    /// Offene Warnungen, die im Lohn dieses Monats quittiert werden müssen:
    /// alle früheren Monate dieses MA (Vorjahr eingeschlossen).
    /// </summary>
    public static IQueryable<FamzKorrektur> OffeneWarnungen(AppDbContext db, int employeeId, int year, int month)
        => db.FamzKorrekturen.Where(k => k.EmployeeId == employeeId
                                      && (k.Status == "OFFEN" || k.Status == "VORJAHR")
                                      && k.Jahr * 12 + k.Monat < year * 12 + month);

    /// <summary>Liest «famzZahlungen» aus einem gespeicherten Lohnzettel; null = Feld fehlt.</summary>
    public static List<(int AllowanceId, decimal Betrag)>? FamzZahlungenAusSlip(string? slipJson)
    {
        if (string.IsNullOrWhiteSpace(slipJson)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(slipJson);
            if (!doc.RootElement.TryGetProperty("famzZahlungen", out var arr)
                || arr.ValueKind != System.Text.Json.JsonValueKind.Array) return null;
            var liste = new List<(int, decimal)>();
            foreach (var e in arr.EnumerateArray())
            {
                if (!e.TryGetProperty("allowanceId", out var id) || !e.TryGetProperty("betrag", out var b)) continue;
                liste.Add((id.GetInt32(), b.GetDecimal()));
            }
            return liste;
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>
    /// Jüngster definitiv abgeschlossener Lohnmonat, in dem der Eintrag verwendet wurde
    /// (laufend bezahlt oder Nachzahlung/Rückforderung verrechnet). Ab dann ist er
    /// eingefroren (Walter 11.10.2026, wie die QST-Versionen).
    /// </summary>
    public async Task<Dictionary<int, (int Jahr, int Monat)>> VerwendetBisAsync(
        int employeeId, IReadOnlyCollection<FamilyMemberAllowance> eintraege, CancellationToken ct = default)
    {
        var ergebnis = new Dictionary<int, (int Jahr, int Monat)>();
        if (eintraege.Count == 0) return ergebnis;

        var abgeschlossen = await (from s in _db.PayrollSnapshots.AsNoTracking()
                                   join p in _db.PayrollPerioden.AsNoTracking() on s.PayrollPeriodeId equals p.Id
                                   where s.EmployeeId == employeeId
                                         && s.Status != "STORNIERT"
                                         && p.Status == "abgeschlossen"
                                   select new { p.Year, p.Month, s.SlipJson }).ToListAsync(ct);
        var ids = eintraege.Select(a => a.Id).ToList();
        var verrechnet = await (from k in _db.FamzKorrekturen.AsNoTracking()
                                join p in _db.PayrollPerioden.AsNoTracking() on k.VerrechnetPeriodeId equals p.Id
                                where ids.Contains(k.AllowanceId)
                                      && k.Status == "VERRECHNET"
                                      && p.Status == "abgeschlossen"
                                select new { k.AllowanceId, p.Year, p.Month }).ToListAsync(ct);

        void Merke(int id, int y, int m)
        {
            if (!ergebnis.TryGetValue(id, out var bis) || y * 12 + m > bis.Jahr * 12 + bis.Monat)
                ergebnis[id] = (y, m);
        }

        foreach (var s in abgeschlossen)
        {
            var zahlungen = FamzZahlungenAusSlip(s.SlipJson);
            var mStart = new DateOnly(s.Year, s.Month, 1);
            var mEnd = mStart.AddMonths(1).AddDays(-1);
            foreach (var a in eintraege)
            {
                bool bezahlt = zahlungen != null
                    ? zahlungen.Any(z => z.AllowanceId == a.Id)
                    : a.ValidFrom <= mEnd && (a.ValidTo == null || a.ValidTo >= mStart)
                      && BekanntAb(a) <= mEnd;
                if (bezahlt) Merke(a.Id, s.Year, s.Month);
            }
        }
        foreach (var v in verrechnet) Merke(v.AllowanceId, v.Year, v.Month);
        return ergebnis;
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
        // Übernommene Zulage wurde im Lohnlauf wieder gelöscht → Warnung ist wieder offen.
        var uebernommen = await _db.FamzKorrekturen
            .Where(k => k.EmployeeId == employeeId && k.Status == "ERLEDIGT" && k.LohnZulageId != null)
            .ToListAsync(ct);
        if (uebernommen.Count > 0)
        {
            var zIds = uebernommen.Select(k => k.LohnZulageId!.Value).ToList();
            var vorhanden = (await _db.LohnZulagen.AsNoTracking()
                    .Where(z => zIds.Contains(z.Id)).Select(z => z.Id).ToListAsync(ct))
                .ToHashSet();
            foreach (var k in uebernommen.Where(k => !vorhanden.Contains(k.LohnZulageId!.Value)))
                Wiedereroeffnen(k);
            await _db.SaveChangesAsync(ct);
        }

        var mitOffenenPosten = (await _db.FamzKorrekturen.AsNoTracking()
                .Where(k => k.EmployeeId == employeeId && (k.Status == "OFFEN" || k.Status == "VORJAHR"))
                .Select(k => k.AllowanceId).Distinct().ToListAsync(ct))
            .ToHashSet();

        foreach (var a in allowances)
        {
            var bekannt = BekanntAb(a);
            var vonMonat = new DateOnly(a.ValidFrom.Year, a.ValidFrom.Month, 1);
            var bekanntMonat = new DateOnly(bekannt.Year, bekannt.Month, 1);
            bool faellig = bekannt <= periodTo
                && (vonMonat < bekanntMonat || (bekannt.Year == year && bekannt.Month == month));
            if (faellig || mitOffenenPosten.Contains(a.Id))
                await ErzeugeKorrekturenAsync(a, erfasstVon, ct);
        }

        // Posten einer gelöschten Zulage
        var ids = allowances.Select(a => a.Id).ToHashSet();
        var verwaist = (await _db.FamzKorrekturen
                .Where(k => k.EmployeeId == employeeId && (k.Status == "OFFEN" || k.Status == "VORJAHR"))
                .ToListAsync(ct))
            .Where(k => !ids.Contains(k.AllowanceId))
            .ToList();
        if (verwaist.Count > 0)
        {
            _db.FamzKorrekturen.RemoveRange(verwaist);
            await _db.SaveChangesAsync(ct);
        }
    }
}
