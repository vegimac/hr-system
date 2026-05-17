using System.Text;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// BFS-LSE-Export (Lohnstrukturerhebung).
///
/// Phase 1 (dieser Entwurf): zieht alle Felder zusammen, die ohne weitere
/// Mapping-Tabellen direkt aus den bestehenden Daten verfügbar sind:
///   • Personalstammdaten (Geschlecht, Geburtsjahr, Nationalität, Bewilligung,
///     Wohnsitzkanton)
///   • Anstellungsdaten (Vertragsart, Beschäftigungsgrad, Stunden/Woche, Beginn)
///   • Lohndaten aus PayrollSnapshot (Brutto, AHV-Basis, Abzüge, QST)
///   • Arbeitsort (Filial-PLZ, -Kanton)
///   • NOGA: hardcoded "5610" (Restaurants/Schnellverpflegung) — könnte später
///     auf CompanyProfile ausgelagert werden, falls eine Filiale eine andere
///     NOGA hätte.
///
/// Was Phase 1 noch NICHT füllt:
///   • ISCO-08-Code (Beruf): braucht Mapping JobGroup → ISCO. In dieser Phase
///     wird der JobGroup-Code roh exportiert, damit ihr beim Treuhänder seht
///     was zu mappen ist.
///   • ISCED-Ausbildungsstufe: braucht Mapping EducationLevel → ISCED.
///     Phase 1 exportiert den EducationLevel-Code roh.
///   • Stellung im Betrieb (Vorgesetztenfunktion ja/nein, Kaderstufe): wird
///     näherungsweise aus JobGroup.IsKader abgeleitet.
///
/// Erhebungsmonat ist normalerweise Oktober. Service nimmt ein beliebiges
/// Jahr/Monat-Paar entgegen — pro MA wird der PayrollSnapshot dieser Periode
/// gesucht. Wenn keiner existiert (z.B. MA war noch nicht angestellt), wird
/// der MA übersprungen.
/// </summary>
public class LseExportService
{
    private readonly AppDbContext _db;
    private const string DefaultNoga = "5610";  // Restaurants und Schnellverpflegung

    public LseExportService(AppDbContext db) => _db = db;

    public class LseRecord
    {
        public string PseudoId            { get; set; } = "";  // anonymisierter MA-Identifikator
        public string EmployeeNumber      { get; set; } = "";  // intern, nur für Vorschau
        public string FirstName           { get; set; } = "";  // intern, nur für Vorschau
        public string LastName            { get; set; } = "";  // intern, nur für Vorschau
        public string Gender              { get; set; } = "";  // 1=M, 2=W
        public int    BirthYear           { get; set; }
        public string NationalityCode     { get; set; } = "";  // ISO-Alpha-3 oder BFS-Code
        public string PermitType          { get; set; } = "";
        public string ResidenceCanton     { get; set; } = "";  // Wohnkanton
        public string IscoRaw             { get; set; } = "";  // JobGroup-Code (Mapping fehlt)
        public string IscedRaw            { get; set; } = "";  // EducationLevel-Code (Mapping fehlt)
        public bool   IsSupervisor        { get; set; }        // aus JobGroup.IsKader abgeleitet
        public string EmploymentModel     { get; set; } = "";  // UTP/MTP/FIX/FIX-M
        public string ContractType        { get; set; } = "";  // unbefristet/befristet
        public DateTime? ContractStart    { get; set; }
        public DateTime? ContractEnd      { get; set; }
        public decimal EmploymentPercent  { get; set; }
        public decimal WeeklyHours        { get; set; }
        public string WorkplaceZip        { get; set; } = "";
        public string WorkplaceCanton     { get; set; } = "";
        public string Noga                { get; set; } = DefaultNoga;
        public decimal PaidHoursMonth     { get; set; }
        public decimal Brutto             { get; set; }
        public decimal SvBasisAhv         { get; set; }
        public decimal SvBasisBvg         { get; set; }
        public decimal QstBetrag          { get; set; }
        public decimal ThirteenthAccum    { get; set; }
        public string  BranchName         { get; set; } = "";  // intern, nur für Vorschau
    }

    public async Task<List<LseRecord>> BuildAsync(int year, int month, int? companyProfileId)
    {
        // Periode finden — kann mehrere geben (eine pro Filiale), wir filtern unten
        var periodeQ = _db.PayrollPerioden.AsQueryable()
            .Where(p => p.Year == year && p.Month == month);
        if (companyProfileId.HasValue)
            periodeQ = periodeQ.Where(p => p.CompanyProfileId == companyProfileId.Value);
        var perioden = await periodeQ.ToListAsync();
        if (perioden.Count == 0) return new List<LseRecord>();

        var periodeIds = perioden.Select(p => p.Id).ToList();

        var snapshots = await _db.PayrollSnapshots
            .Include(s => s.Employee).ThenInclude(e => e!.PermitType)
            .Include(s => s.Employee).ThenInclude(e => e!.NationalityRef)
            .Where(s => periodeIds.Contains(s.PayrollPeriodeId))
            .ToListAsync();

        // Branches und JobGroups und EducationLevels einmal laden
        var branchIds = snapshots.Select(s => s.CompanyProfileId).Distinct().ToList();
        var branches = await _db.CompanyProfiles
            .Where(b => branchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id);

        var jobGroups = await _db.JobGroups.AsNoTracking().ToDictionaryAsync(j => j.Id, j => j);

        // Aktive Anstellung pro MA zum Erhebungsmonat (Stichtag = letzter Tag des Monats)
        var stichtag = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var employeeIds = snapshots.Select(s => s.EmployeeId).Distinct().ToList();
        var employments = await _db.Employments
            .Where(e => employeeIds.Contains(e.EmployeeId)
                     && e.ContractStartDate <= stichtag
                     && (e.ContractEndDate == null || e.ContractEndDate >= stichtag))
            .ToListAsync();

        // Optional: bezahlte Stunden im Monat aus EmployeeTimeEntries
        var monthFrom = new DateOnly(year, month, 1);
        var monthTo   = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var timeAgg = await _db.EmployeeTimeEntries
            .Where(t => employeeIds.Contains(t.EmployeeId)
                     && t.EntryDate >= monthFrom
                     && t.EntryDate <= monthTo)
            .GroupBy(t => t.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Hours = g.Sum(x => (x.TotalHours ?? x.DurationHours ?? 0m)) })
            .ToDictionaryAsync(x => x.EmployeeId, x => x.Hours);

        var result = new List<LseRecord>();
        foreach (var snap in snapshots)
        {
            var emp    = snap.Employee;
            if (emp == null) continue;
            var branch = branches.TryGetValue(snap.CompanyProfileId, out var b) ? b : null;
            // Aktive Anstellung in dieser Filiale bevorzugt — sonst irgendeine aktive
            var employment = employments
                .Where(e => e.EmployeeId == snap.EmployeeId)
                .OrderByDescending(e => e.CompanyProfileId == snap.CompanyProfileId)
                .ThenByDescending(e => e.ContractStartDate)
                .FirstOrDefault();

            // JobGroup über Employment.JobTitle ist ein String-Lookup — wir versuchen
            // primär den Code in JobTitle (z.B. "CREW") gegen JobGroup.Code zu matchen.
            JobGroup? jg = null;
            if (employment != null && !string.IsNullOrWhiteSpace(employment.JobTitle))
            {
                var jt = employment.JobTitle.Trim().ToUpperInvariant();
                jg = jobGroups.Values.FirstOrDefault(x =>
                    string.Equals(x.Code, jt, StringComparison.OrdinalIgnoreCase));
            }

            var rec = new LseRecord
            {
                PseudoId          = $"LSE-{emp.Id:D6}",
                EmployeeNumber    = emp.EmployeeNumber ?? "",
                FirstName         = emp.FirstName ?? "",
                LastName          = emp.LastName ?? "",
                Gender            = MapGender(emp.Gender),
                BirthYear         = emp.DateOfBirth?.Year ?? 0,
                NationalityCode   = emp.NationalityRef?.Code ?? emp.Nationality ?? "",
                PermitType        = emp.PermitType?.Code ?? "",
                ResidenceCanton   = emp.CantonCode ?? "",
                IscoRaw           = jg?.Code ?? employment?.JobTitle ?? "",
                IscedRaw          = "",  // EducationLevel-Lookup folgt in Phase 2
                IsSupervisor      = jg?.IsKader ?? false,
                EmploymentModel   = employment?.EmploymentModel ?? "",
                ContractType      = employment?.ContractEndDate.HasValue == true ? "befristet" : "unbefristet",
                ContractStart     = employment?.ContractStartDate,
                ContractEnd       = employment?.ContractEndDate,
                EmploymentPercent = employment?.EmploymentPercentage ?? 0m,
                WeeklyHours       = employment?.WeeklyHours ?? employment?.GuaranteedHoursPerWeek ?? 0m,
                WorkplaceZip      = branch?.ZipCode ?? "",
                WorkplaceCanton   = branch?.KantonCode ?? "",
                Noga              = DefaultNoga,
                PaidHoursMonth    = timeAgg.TryGetValue(emp.Id, out var hrs) ? hrs : 0m,
                Brutto            = snap.Brutto,
                SvBasisAhv        = snap.SvBasisAhv,
                SvBasisBvg        = snap.SvBasisBvg,
                QstBetrag         = snap.QstBetrag,
                ThirteenthAccum   = snap.ThirteenthAccumulated,
                BranchName        = (branch?.RestaurantCode ?? "") + " " + (branch?.BranchName ?? branch?.CompanyName ?? "")
            };
            result.Add(rec);
        }

        return result.OrderBy(r => r.BranchName).ThenBy(r => r.LastName).ThenBy(r => r.FirstName).ToList();
    }

    /// <summary>
    /// Gibt eine CSV-Vorschau zurück. BFS-Format wird in Phase 2 final gemacht
    /// (sobald die exakte Spec vom BFS vorliegt). Bis dahin: alle Felder, die
    /// wir haben, in einer breiten CSV — der Treuhänder/das BFS-Tool kann
    /// daraus die finalen Felder extrahieren.
    /// </summary>
    public string ToCsv(List<LseRecord> records)
    {
        var sb = new StringBuilder();
        // UTF-8 BOM, damit Excel die Umlaute richtig anzeigt
        sb.Append('﻿');
        sb.AppendLine(string.Join(";", new[] {
            "PseudoId","Personalnr","Vorname","Nachname","Geschlecht","Geburtsjahr",
            "Nationalität","Bewilligung","Wohnkanton",
            "Beruf_Roh","Ausbildung_Roh","Vorgesetzt",
            "Vertragsmodell","Vertragstyp","Vertragsbeginn","Vertragsende",
            "Beschäftigungsgrad_%","Wochenstunden_Vertrag",
            "Arbeitsort_PLZ","Arbeitsort_Kanton","NOGA",
            "Bezahlte_Stunden_Monat",
            "Brutto","SV_Basis_AHV","SV_Basis_BVG","QST_Abzug","13ML_Kumuliert",
            "Filiale"
        }));
        foreach (var r in records)
        {
            sb.AppendLine(string.Join(";", new[] {
                Csv(r.PseudoId), Csv(r.EmployeeNumber), Csv(r.FirstName), Csv(r.LastName),
                Csv(r.Gender), r.BirthYear.ToString(),
                Csv(r.NationalityCode), Csv(r.PermitType), Csv(r.ResidenceCanton),
                Csv(r.IscoRaw), Csv(r.IscedRaw), r.IsSupervisor ? "1" : "0",
                Csv(r.EmploymentModel), Csv(r.ContractType),
                r.ContractStart?.ToString("yyyy-MM-dd") ?? "",
                r.ContractEnd?.ToString("yyyy-MM-dd") ?? "",
                Num(r.EmploymentPercent), Num(r.WeeklyHours),
                Csv(r.WorkplaceZip), Csv(r.WorkplaceCanton), Csv(r.Noga),
                Num(r.PaidHoursMonth),
                Num(r.Brutto), Num(r.SvBasisAhv), Num(r.SvBasisBvg),
                Num(r.QstBetrag), Num(r.ThirteenthAccum),
                Csv(r.BranchName)
            }));
        }
        return sb.ToString();
    }

    private static string Csv(string? v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        if (v.Contains(';') || v.Contains('"') || v.Contains('\n'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }

    private static string Num(decimal v) => v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static string MapGender(string? gender)
    {
        if (string.IsNullOrWhiteSpace(gender)) return "";
        var g = gender.Trim().ToLowerInvariant();
        if (g.StartsWith("m") || g == "male" || g == "männlich") return "1";
        if (g.StartsWith("w") || g == "f" || g == "female" || g == "weiblich") return "2";
        return "";
    }
}
