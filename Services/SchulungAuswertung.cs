using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Lädt Verzeichnis, Einträge und Vertragsdaten und rechnet pro MA den Stand jeder
/// Schulung (<see cref="SchulungStatus"/>). Eine Stelle für MA-Tab, Übersicht,
/// Dashboard und Kontrolle, damit alle dasselbe sagen.
/// </summary>
public static class SchulungAuswertung
{
    public sealed record TypStand(SchulungStatus.Typ Typ, SchulungStatus.Ergebnis Ergebnis);

    public sealed record MaStand(
        int EmployeeId,
        string Vorname,
        string Nachname,
        string EmployeeNumber,
        int? CompanyProfileId,
        SchulungStatus.Kontext Kontext,
        DateOnly? Austritt,
        List<TypStand> Staende,
        List<SchulungStatus.Eintrag> Eintraege);

    public static async Task<List<SchulungStatus.Typ>> TypenAsync(AppDbContext db, bool nurAktive = true)
        => (await db.SchulungTypen.AsNoTracking()
                .Where(t => !nurAktive || t.Aktiv)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
                .ToListAsync())
            .Select(ZuTyp).ToList();

    public static SchulungStatus.Typ ZuTyp(SchulungTyp t)
        => new(t.Id, t.Code, t.Name, t.RefreshMonate, t.FristTage, t.Zielgruppe, t.FredMoeglich, t.WarnenAbTage, t.CreatedAt);

    /// <summary>Vertrag, der am Stichtag gilt — sonst der nächste künftige, sonst der jüngste.</summary>
    public static Employment? VertragAm(IEnumerable<Employment> abschnitte, DateOnly stichtag)
    {
        var liste = abschnitte.OrderBy(e => e.ContractStartDate).ToList();
        var tag = stichtag.ToDateTime(TimeOnly.MinValue);
        return liste.LastOrDefault(e => e.ContractStartDate.Date <= tag
                                     && (e.ContractEndDate == null || e.ContractEndDate.Value.Date >= tag))
            ?? liste.FirstOrDefault(e => e.ContractStartDate.Date > tag)
            ?? liste.LastOrDefault();
    }

    /// <summary>
    /// Stand aller aktiven Schulungen für die gewählten MA. Ohne <paramref name="employeeIds"/>:
    /// alle MA mit laufendem oder künftigem Vertrag (optional nur einer Filiale).
    /// </summary>
    public static async Task<List<MaStand>> LadeAsync(AppDbContext db, DateOnly heute,
        IReadOnlyCollection<int>? employeeIds = null, int? companyProfileId = null,
        IReadOnlyCollection<int>? nurFilialen = null)
    {
        var typen = await TypenAsync(db);
        var heuteDt = heute.ToDateTime(TimeOnly.MinValue);

        var empQuery = db.Employees.AsNoTracking().Where(e => !e.IsHidden);
        if (employeeIds != null)
            empQuery = empQuery.Where(e => employeeIds.Contains(e.Id));
        else
            empQuery = empQuery.Where(e => e.IsActive && !e.IsPayrollExcluded
                && !e.EmployeeNumber.ToLower().EndsWith("alt")
                && e.Employments.Any(em => em.ContractEndDate == null || em.ContractEndDate >= heuteDt));

        var emps = await empQuery
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber, e.EntryDate, e.ExitDate, e.DienstalterSeit })
            .ToListAsync();
        var ids = emps.Select(e => e.Id).ToList();

        var vertraege = await db.Employments.AsNoTracking()
            .Include(em => em.JobGroup)
            .Where(em => ids.Contains(em.EmployeeId))
            .ToListAsync();
        var vertragProMa = vertraege.GroupBy(v => v.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var eintraege = await db.EmployeeSchulungen.AsNoTracking()
            .Where(s => ids.Contains(s.EmployeeId))
            .Select(s => new { s.EmployeeId, s.Id, s.SchulungTypId, s.Datum, s.Art, s.DokumentId })
            .ToListAsync();
        var eintragProMa = eintraege.GroupBy(x => x.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(x => new SchulungStatus.Eintrag(x.Id, x.SchulungTypId, x.Datum, x.Art, x.DokumentId)).ToList());

        var result = new List<MaStand>();
        foreach (var e in emps)
        {
            var abschnitte = vertragProMa.TryGetValue(e.Id, out var vl) ? vl : new List<Employment>();
            var vertrag = VertragAm(abschnitte, heute);
            if (employeeIds == null)
            {
                if (vertrag == null) continue;
                if (companyProfileId.HasValue && vertrag.CompanyProfileId != companyProfileId) continue;
                if (nurFilialen != null && (!vertrag.CompanyProfileId.HasValue || !nurFilialen.Contains(vertrag.CompanyProfileId.Value))) continue;
            }

            var empStub = new Employee { EntryDate = e.EntryDate, DienstalterSeit = e.DienstalterSeit };
            var eintritt = Dienstalter.Massgebend(empStub, abschnitte, heute);
            var kontext = new SchulungStatus.Kontext(
                vertrag?.EmploymentModel,
                vertrag?.JobGroup?.Code ?? vertrag?.JobTitle,
                vertrag?.EducationLevelCode,
                eintritt);

            var eig = eintragProMa.TryGetValue(e.Id, out var el) ? el : new List<SchulungStatus.Eintrag>();
            var staende = typen.Select(t => new TypStand(t, SchulungStatus.Berechne(t, eig, kontext, heute))).ToList();

            result.Add(new MaStand(e.Id, e.FirstName ?? "", e.LastName ?? "", e.EmployeeNumber,
                vertrag?.CompanyProfileId, kontext,
                e.ExitDate.HasValue ? DateOnly.FromDateTime(e.ExitDate.Value) : null,
                staende, eig));
        }
        return result
            .OrderBy(m => m.Vorname, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Nachname, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Dokument-Typ für Schulungsnachweise: über den Feld-Code «schulung», sonst angelegt.</summary>
    public static async Task<DokumentTyp> EnsureDokumentTypAsync(AppDbContext db)
    {
        var typ = await db.DokumentTypen.Where(t => t.Aktiv && t.LinkedFieldCode == "schulung")
                      .OrderBy(t => t.Id).FirstOrDefaultAsync()
               ?? await db.DokumentTypen.Where(t => t.Aktiv && t.LinkedFieldCode == "andere_weiterbildung")
                      .OrderBy(t => t.Id).FirstOrDefaultAsync();
        if (typ != null) return typ;

        var kat = await db.DokumentKategorien
            .Where(k => k.Aktiv && k.Name.ToLower().Contains("mitarbeiterentwicklung"))
            .OrderBy(k => k.SortOrder).FirstOrDefaultAsync();
        if (kat == null)
        {
            kat = new DokumentKategorie { Name = "Mitarbeiterentwicklung", SortOrder = 50, Aktiv = true };
            db.DokumentKategorien.Add(kat);
            await db.SaveChangesAsync();
        }
        typ = new DokumentTyp { KategorieId = kat.Id, Name = "Ausbildung / Schulung", SortOrder = 20, LinkedFieldCode = "schulung" };
        db.DokumentTypen.Add(typ);
        await db.SaveChangesAsync();
        return typ;
    }
}
