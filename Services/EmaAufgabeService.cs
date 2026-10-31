using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Lädt die Fakten und baut die EMA-Aufgaben für BVG und Quellensteuer.
/// </summary>
public static class EmaAufgabeService
{
    public sealed record Zeile(
        EmaAufgabeRegel.Aufgabe Aufgabe, int CompanyProfileId,
        string Vorname, string Name, int? FreigabeId, string? FreigegebenAm, string? FreigegebenVon);

    public static async Task<List<Zeile>> LadenAsync(AppDbContext db, int? companyProfileId)
    {
        var grenze = EmaAufgabeRegel.ToDoAb.ToDateTime(TimeOnly.MinValue);
        var maQuery = db.Employees.AsNoTracking()
            .Where(e => !e.IsHidden && !e.IsPayrollExcluded && (e.ExitDate == null || e.ExitDate >= grenze));
        if (companyProfileId != null)
        {
            var inFiliale = db.Employments.Where(v => v.CompanyProfileId == companyProfileId).Select(v => v.EmployeeId);
            maQuery = maQuery.Where(e => inFiliale.Contains(e.Id));
        }
        var ma = await maQuery
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EntryDate, e.ExitDate, e.Austrittsgrund, e.Nationality })
            .ToListAsync();
        if (ma.Count == 0) return new List<Zeile>();
        var ids = ma.Select(e => e.Id).ToList();

        var vertraege = (await db.Employments.AsNoTracking()
            .Where(v => ids.Contains(v.EmployeeId) && v.CompanyProfileId != null)
            .Select(v => new { v.EmployeeId, v.CompanyProfileId, v.ContractStartDate, v.ContractEndDate, v.HourlyRate, v.MonthlySalary })
            .ToListAsync())
            .Select(v => new VertragZeile(v.EmployeeId, v.CompanyProfileId!.Value, v.ContractStartDate, v.ContractEndDate, v.HourlyRate, v.MonthlySalary))
            .ToList();
        var pflicht = await db.EmployeeBvgPflichten.AsNoTracking()
            .Where(p => ids.Contains(p.EmployeeId) && p.Versichert)
            .Select(p => new { p.EmployeeId, p.GueltigAb, p.GueltigBis })
            .ToListAsync();
        var codes = await db.EmployeeVersicherungCodes.AsNoTracking()
            .Where(c => ids.Contains(c.EmployeeId) && c.Art == "BVG")
            .Select(c => new { c.EmployeeId, c.ValidFrom, c.ValidTo, c.Code, c.BvgEintrittsgrund, c.BvgVollArbeitsfaehig })
            .ToListAsync();
        var absenzen = await db.Absences.AsNoTracking()
            .Where(a => ids.Contains(a.EmployeeId) && (a.AbsenceType == "KRANK" || a.AbsenceType == "UNFALL"))
            .Select(a => new { a.EmployeeId, a.DateFrom, a.DateTo, a.AbsenceType })
            .ToListAsync();
        var adressen = await db.EmployeeWohnortHistories.AsNoTracking()
            .Where(w => ids.Contains(w.EmployeeId) && !w.DatumOffen && w.GueltigAb != null)
            .Select(w => new { w.EmployeeId, w.GueltigAb, w.Plz, w.Ort, w.KantonCode })
            .ToListAsync();
        var zivil = await db.EmployeeZivilstandHistories.AsNoTracking()
            .Where(z => ids.Contains(z.EmployeeId) && z.GueltigAb != null)
            .Select(z => new { z.EmployeeId, z.GueltigAb, z.Zivilstand })
            .ToListAsync();
        var qst = await db.EmployeeQuellensteuer.AsNoTracking()
            .Where(q => ids.Contains(q.EmployeeId))
            .Select(q => new { q.EmployeeId, q.ValidFrom, q.ValidTo, q.QstCode, q.Steuerkanton, q.AnzahlKinder, q.Kirchensteuer, q.QstGemeinde, q.ArbeitsortKanton })
            .ToListAsync();
        var permits = await db.EmployeePermitHistories.AsNoTracking()
            .Where(p => ids.Contains(p.EmployeeId))
            .Select(p => new { p.EmployeeId, p.ValidFrom, p.ValidTo, p.PermitTypeId })
            .ToListAsync();
        var permitCodes = await db.PermitTypes.AsNoTracking()
            .Select(t => new { t.Id, t.Code })
            .ToDictionaryAsync(t => t.Id, t => t.Code);
        var frei = await db.EmaFreigaben.AsNoTracking()
            .Where(f => ids.Contains(f.EmployeeId))
            .ToListAsync();

        var personen = ma.Select(e => new EmaAufgabeRegel.Person(
            e.Id,
            e.EntryDate == null ? null : DateOnly.FromDateTime(e.EntryDate.Value),
            e.ExitDate == null ? null : DateOnly.FromDateTime(e.ExitDate.Value),
            e.Austrittsgrund, e.Nationality)).ToList();
        var spannen = pflicht.Select(p => new EmaAufgabeRegel.BvgSpanne(p.EmployeeId, p.GueltigAb, p.GueltigBis));
        var bvgCodes = codes.Select(c => new EmaAufgabeRegel.BvgCode(
            c.EmployeeId, c.ValidFrom, c.ValidTo, c.Code, c.BvgEintrittsgrund, c.BvgVollArbeitsfaehig));
        var krank = absenzen.Select(a => new EmaAufgabeRegel.Abwesenheit(a.EmployeeId, a.DateFrom, a.DateTo, a.AbsenceType));
        var lohn = vertraege
            .Select(v => new EmaAufgabeRegel.Wechsel(v.EmployeeId, DateOnly.FromDateTime(v.Start),
                $"{v.Hourly:0.00}|{v.Monthly:0.00}"))
            .ToList();
        var adr = adressen.Select(w => new EmaAufgabeRegel.Wechsel(
            w.EmployeeId, w.GueltigAb!.Value, $"{w.Plz}|{w.Ort}|{w.KantonCode}"));
        var ziv = zivil.Select(z => new EmaAufgabeRegel.Wechsel(z.EmployeeId, z.GueltigAb!.Value, z.Zivilstand));

        string? Bewilligung(int emp, DateOnly tag)
        {
            var p = permits.Where(x => x.EmployeeId == emp && x.ValidFrom <= tag && (x.ValidTo == null || x.ValidTo >= tag))
                .OrderByDescending(x => x.ValidFrom).FirstOrDefault();
            if (p == null) return null;
            if (p.PermitTypeId == null) return "CH";
            return permitCodes.TryGetValue(p.PermitTypeId.Value, out var code) ? code : null;
        }
        var qstVersionen = qst.Select(q => new EmaAufgabeRegel.QstVersion(
            q.EmployeeId, q.ValidFrom, q.ValidTo, q.QstCode, q.Steuerkanton, q.AnzahlKinder, q.Kirchensteuer,
            q.QstGemeinde, q.ArbeitsortKanton, Bewilligung(q.EmployeeId, q.ValidFrom)));

        var aufgaben = EmaAufgabeRegel.BvgAufgaben(spannen, bvgCodes, krank, lohn, adr, ziv, personen);
        aufgaben.AddRange(EmaAufgabeRegel.QstAufgaben(qstVersionen, ziv, personen));

        var vertraegeBy = vertraege.ToLookup(v => v.EmployeeId);
        var namen = ma.ToDictionary(e => e.Id);
        var freiBy = frei.ToLookup(f => (f.EmployeeId, f.Domaene, f.Grund, f.Stichtag));
        var res = new List<Zeile>();
        foreach (var a in aufgaben)
        {
            var fil = FilialeAm(vertraegeBy[a.EmployeeId], a.Stichtag);
            if (fil == null) continue;
            if (companyProfileId != null && fil != companyProfileId) continue;
            var n = namen[a.EmployeeId];
            var f = freiBy[(a.EmployeeId, a.Domaene, a.Grund, a.Stichtag)].FirstOrDefault();
            res.Add(new Zeile(a, fil.Value, n.FirstName ?? "", n.LastName ?? "",
                f?.Id, f?.ErfasstAm.ToString("dd.MM.yyyy"), f?.ErfasstVon));
        }
        return res.OrderBy(z => z.Vorname, StringComparer.OrdinalIgnoreCase)
            .ThenBy(z => z.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(z => z.Aufgabe.Stichtag)
            .ToList();
    }

    private sealed record VertragZeile(int EmployeeId, int CompanyProfileId, DateTime Start, DateTime? Ende, decimal? Hourly, decimal? Monthly);

    private static int? FilialeAm(IEnumerable<VertragZeile> vertraege, DateOnly tag)
    {
        var liste = vertraege.Select(v => (
            v.CompanyProfileId,
            Start: DateOnly.FromDateTime(v.Start),
            Ende: v.Ende == null ? (DateOnly?)null : DateOnly.FromDateTime(v.Ende.Value)
        )).ToList();
        var deckt = liste.Where(v => v.Start <= tag && (v.Ende == null || v.Ende >= tag))
            .OrderByDescending(v => v.Start).FirstOrDefault();
        if (deckt.CompanyProfileId != 0) return deckt.CompanyProfileId;
        var davor = liste.Where(v => v.Start <= tag).OrderByDescending(v => v.Start).FirstOrDefault();
        return davor.CompanyProfileId == 0 ? null : davor.CompanyProfileId;
    }
}
