using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;
using static HrSystem.Services.Elm.ElmGemeinsam;

namespace HrSystem.Services.Elm;

/// <summary>
/// Etappe E2 (Walter 27.08.2026, docs/swissdec-elm6-konzept.md):
/// erzeugt eine DeclareAnnualSalary-Jahresmeldung für die Domäne AHV
/// aus den PayrollSnapshots eines Jahres — über ALLE Filialen
/// (Meldeeinheit = Rechtseinheit Schaub Restaurants GmbH, Filialen =
/// Workplaces). Getestet wird ausschliesslich mit KUNSTDATEN der
/// Testinstanz (test.onecrew.ch); das XML wird im Refapps-Transmitter
/// hochgeladen (der signiert selbst — kein Transmitter-Zertifikat nötig).
///
/// Bewusste E2-Vereinfachungen (werden in E3/E5 ersetzt):
///  • AK-Nummer/UID aus CompanyProfile-Feldern, sonst Platzhalter (E3).
///  • ALV-Einkommen = Summe der monatlich auf 12'350 gedeckelten
///    AHV-Basen (Näherung; exakte Jahresrechnung analog Dezember-
///    Jahresausgleich folgt mit E5).
///  • UVG/BVG-Institution als NoneWithReason (eigene Domänen in E5).
/// </summary>
public class ElmAnnualDeclarationBuilder
{
    private readonly AppDbContext _db;
    private readonly ElmXmlValidator _validator;

    public ElmAnnualDeclarationBuilder(AppDbContext db, ElmXmlValidator validator)
    {
        _db = db;
        _validator = validator;
    }

    /// <summary>Monats-Höchstlohn ALV/NBU (148'200 / 12) — E2-Näherung.</summary>
    private const decimal AlvMonatsCap = 12350m;

    public record BuildResult(
        string Xml, int Personen, int Uebersprungen, decimal TotalAhv, decimal TotalAlv,
        List<string> Warnungen, List<string> XsdFehler);

    public async Task<BuildResult> BuildAhvAsync(int year, CancellationToken ct = default)
    {
        var warn = new List<string>();

        // ── Stammdaten Rechtseinheit (gemeinsam mit der Monatsmeldung) ────
        var stamm = await LadeRechtseinheitAsync(_db, warn, ct);
        if (stamm == null)
            return new BuildResult("", 0, 0, 0, 0, warn, new List<string>());
        var branches = stamm.Filialen;
        var main = stamm.Haupt;
        var uid = stamm.Uid;

        // «versichert seit» für UVG/BVG kommt weiterhin aus elm_stammdaten (E3).
        var st = await _db.ElmStammdaten.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);

        // Führende Quelle für Nummern = EMPFÄNGER-KATALOG (Walter 28.08.2026):
        // LohndatenEmpfaenger (zentral) + CompanyProfileEmpfaenger (Mitglied-/
        // Subnummer pro Filiale). elm_stammdaten liefert nur noch UID der
        // Rechtseinheit + «versichert seit».
        var empf = await _db.LohndatenEmpfaengers.AsNoTracking()
            .Include(e => e.Zuordnungen)
            .Where(e => e.IsActive)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
        var akE = empf.FirstOrDefault(e => e.Art == "AUSGLEICHSKASSE");
        static bool IstZusatz(Models.LohndatenEmpfaenger e) =>
            (e.Bezeichnung + " " + (e.Zusatz ?? "")).ToLowerInvariant().Contains("zusatz");
        var uvgE = empf.FirstOrDefault(e => e.Art == "UVG" && !IstZusatz(e));
        var bvgE = empf.FirstOrDefault(e => e.Art == "BVG");

        // Stichtag für die Zuordnung = 31.12. des Lohnjahres (Gültig ab/bis, Walter 07.09.2026)
        var stichtagZuord = new DateOnly(year, 12, 31);
        string? GemeinsameMitgliedNr(Models.LohndatenEmpfaenger? e)
        {
            if (e == null) return null;
            var nrs = e.Zuordnungen.Where(z => z.GiltAm(stichtagZuord))
                .Select(z => (z.Mitgliednummer ?? "").Trim())
                .Where(v => v.Length > 0).Distinct().ToList();
            return nrs.Count == 1 ? nrs[0] : null;
        }

        // Kassen-Nummer = Adressierung (Addressee), Abrechnungs-Nummer =
        // unsere Kundennummer bei der Kasse (AK-CC-CustomerNumber).
        var akKasse = (akE?.Kassennummer ?? "").Trim();
        if (string.IsNullOrEmpty(akKasse))
        {
            var legacy = Regex.Match(main.AhvKasse ?? "", @"[\d.\-/]{3,}");
            akKasse = legacy.Success ? legacy.Value : "";
        }
        if (string.IsNullOrEmpty(akKasse))
        {
            warn.Add("AHV-Kassen-Nummer fehlt — Platzhalter 001.234 eingesetzt (Empfänger-Katalog: Ausgleichskasse erfassen).");
            akKasse = "001.234";
        }
        var akAbrechnung = GemeinsameMitgliedNr(akE) ?? "";
        if (string.IsNullOrEmpty(akAbrechnung))
        {
            if (akE != null && akE.Zuordnungen.Any(z => z.GiltAm(stichtagZuord)))
                warn.Add("AK-Mitgliednummern sind pro Filiale unterschiedlich — im XML steht vorerst die Kassen-Nr. (Zuordnung pro Filiale kommt in E5).");
            akAbrechnung = akKasse;
        }

        // ── Lohndaten des Jahres (alle Filialen, STORNIERTE ausgenommen) ──
        var rows = await (from s in _db.PayrollSnapshots
                          join p in _db.PayrollPerioden on s.PayrollPeriodeId equals p.Id
                          where p.Year == year && s.Status != "STORNIERT"
                          select new { s.EmployeeId, s.SvBasisAhv, p.Month, PeriodeStatus = p.Status })
                         .ToListAsync(ct);
        if (rows.Count == 0)
            return new BuildResult("", 0, 0, 0, 0,
                new List<string> { $"Keine Lohnabrechnungen für {year} gefunden." }, new List<string>());

        var offenePerioden = rows.Where(r => r.PeriodeStatus != "abgeschlossen")
            .Select(r => r.Month).Distinct().OrderBy(m => m).ToList();
        if (offenePerioden.Count > 0)
            warn.Add($"Nicht definitiv abgeschlossene Monate im XML enthalten: {string.Join(", ", offenePerioden)} — für Übungszwecke ok, für eine echte Meldung müssen alle Monate abgeschlossen sein.");

        var perEmp = rows.GroupBy(r => r.EmployeeId).Select(g => new
        {
            EmployeeId = g.Key,
            Ahv = g.Sum(r => r.SvBasisAhv),
            Alv = g.Sum(r => Math.Min(r.SvBasisAhv, AlvMonatsCap)),
            FirstMonth = g.Min(r => r.Month),
            LastMonth = g.Max(r => r.Month)
        }).Where(x => x.Ahv > 0).OrderBy(x => x.EmployeeId).ToList();

        var empIds = perEmp.Select(x => x.EmployeeId).ToList();
        var emps = await _db.Employees.AsNoTracking()
            .Include(e => e.NationalityRef)
            .Where(e => empIds.Contains(e.Id))
            .ToListAsync(ct);
        var empById = emps.ToDictionary(e => e.Id);
        var employments = await _db.Employments.AsNoTracking()
            .Where(em => empIds.Contains(em.EmployeeId))
            .ToListAsync(ct);

        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);

        // ── Personen ──────────────────────────────────────────────────────
        var persons = new List<XElement>();
        int skipped = rows.GroupBy(r => r.EmployeeId).Count() - perEmp.Count;
        if (skipped > 0)
            warn.Add($"{skipped} MA ohne AHV-pflichtigen Lohn {year} übersprungen (z.B. unter 18 / nur 0-Läufe).");
        decimal totalAhv = 0, totalAlv = 0;

        foreach (var x in perEmp)
        {
            if (!empById.TryGetValue(x.EmployeeId, out var e)) continue;
            if (e.DateOfBirth == null)
            {
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Geburtsdatum fehlt (Pflichtfeld) — MA übersprungen.");
                continue;
            }

            // Vertrag mit Überlappung ins Jahr (neuester zuerst)
            var em = employments
                .Where(m => m.EmployeeId == x.EmployeeId
                            && m.ContractStartDate <= yearEnd
                            && (m.ContractEndDate == null || m.ContractEndDate >= yearStart))
                .OrderByDescending(m => m.ContractStartDate)
                .FirstOrDefault()
                ?? employments.Where(m => m.EmployeeId == x.EmployeeId)
                       .OrderByDescending(m => m.ContractStartDate).FirstOrDefault();

            var particulars = Particulars(e, warn);

            var workingTime = WorkingTime(em, main.NormalWeeklyHours ?? 42m);

            var entry = e.EntryDate ?? em?.ContractStartDate ?? yearStart;
            var work = new XElement(C + "Work",
                new XAttribute("workID", $"#w{e.Id}"),
                new XElement(C + "WorkingTime", workingTime),
                new XElement(C + "EntryDate", entry.ToString("yyyy-MM-dd")));
            if (e.ExitDate != null && e.ExitDate.Value >= yearStart && e.ExitDate.Value <= yearEnd)
                work.Add(new XElement(C + "WithdrawalDate", e.ExitDate.Value.ToString("yyyy-MM-dd")));

            var from = new DateTime(year, x.FirstMonth, 1);
            var until = new DateTime(year, x.LastMonth, DateTime.DaysInMonth(year, x.LastMonth));

            persons.Add(new XElement(Sd + "Person",
                particulars,
                work,
                new XElement(Sd + "AHV-AVS-Salaries",
                    new XElement(Sd + "AHV-AVS-Salary",
                        new XAttribute("addresseeIDRef", "#ahv"),
                        new XElement(Sd + "AccountingTime",
                            new XElement(Ep + "from", from.ToString("yyyy-MM-dd")),
                            new XElement(Ep + "until", until.ToString("yyyy-MM-dd"))),
                        new XElement(Sd + "AHV-AVS-BaseSalary", Amt(x.Ahv)),
                        new XElement(Sd + "AHV-AVS-Income", Amt(x.Ahv)),
                        new XElement(Sd + "ALV-AC-Income", Amt(x.Alv))))));

            totalAhv += x.Ahv;
            totalAlv += x.Alv;
        }

        // ── Firmenbeschreibung: Rechtseinheit + alle Filialen als Workplaces ─
        var companyName = stamm.Firmenname;
        var companyDescription = CompanyDescription(stamm, new[]
        {
            new XElement(C + "CompanyWorkingTime",
                new XAttribute("companyWorkingTimeID", "#cwt1"),
                new XElement(C + "WeeklyHours", Amt(main.NormalWeeklyHours ?? 42m)))
        });

        // ── Gesamtdokument ────────────────────────────────────────────────
        var now = DateTime.Now;
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Sdst + "DeclareAnnualSalary",
                new XAttribute(XNamespace.Xmlns + "sdst", Sdst),
                new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
                new XAttribute(XNamespace.Xmlns + "sd", Sd),
                new XAttribute(XNamespace.Xmlns + "ep", Ep),
                new XAttribute(XNamespace.Xmlns + "c", C),
                RequestContext(companyName, now),
                new XElement(Sdc + "Job",
                    new XElement(Sdc + "Addressees",
                        new XElement(Sdc + "Addressee",
                            new XAttribute("addresseeID", "#ahv"),
                            new XElement(Ep + "AddresseeIdentification", akKasse),
                            new XElement(Ep + "ProcessByDistributor", "true"))),
                    // Übungs-/Testmeldung — nie als Produktivmeldung werten:
                    new XElement(Sdc + "TestCase")),
                new XElement(Sd + "AnnualSalaryDeclaration",
                    new XAttribute("schemaVersion", "0.0"),
                    companyDescription,
                    new XElement(Sd + "Staff", persons),
                    new XElement(Sd + "Institutions",
                        new XElement(Sd + "AHV-AVS",
                            new XAttribute("addresseeIDRef", "#ahv"),
                            new XElement(Sd + "AK-CC-CustomerNumber", akAbrechnung),
                            InsuranceBlock("UVG-LAA-Insurance", uvgE?.Bezeichnung, uvgE?.UidNummer, st?.UvgVersichertSeit,
                                "UVG-Meldung folgt in Aufbau-Etappe E5"),
                            InsuranceBlock("BVG-LPP-Insurance", bvgE?.Bezeichnung, bvgE?.UidNummer, st?.BvgVersichertSeit,
                                "BVG-Meldung folgt in Aufbau-Etappe E5"))),
                    new XElement(Sd + "SalaryTotals",
                        new XElement(Sd + "AHV-AVS-Totals",
                            new XAttribute("addresseeIDRef", "#ahv"),
                            new XElement(Sd + "Total-AHV-AVS-Incomes", Amt(totalAhv)),
                            new XElement(Sd + "Total-AHV-AVS-Open", "0.00"),
                            new XElement(Sd + "Total-ALV-AC-Incomes", Amt(totalAlv)),
                            new XElement(Sd + "Total-ALVZ-ACS-Incomes", "0.00"),
                            new XElement(Sd + "Total-ALV-AC-Open", "0.00"))),
                    new XElement(Sd + "SalaryCounters",
                        new XElement(Sd + "NumberOf-AHV-AVS-Salary-Tags", persons.Count)),
                    new XElement(Sd + "GeneralSalaryDeclarationDescription",
                        new XElement(Sd + "CreationDate", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
                        new XElement(Sd + "AccountingPeriod", year)))));

        var xml = doc.Declaration + Environment.NewLine + doc.ToString();
        var xsdFehler = _validator.Validate(xml);

        return new BuildResult(xml, persons.Count, skipped, totalAhv, totalAlv, warn, xsdFehler);
    }

    /// <summary>
    /// UVG-/BVG-Versicherungsblock im AHV-Institutions-Teil: mit Name + UID +
    /// «versichert seit» (aus elm_stammdaten, E3) — sonst NoneWithReason.
    /// InsuranceControlType = choice( Name+UID-BFS+ValidAsOf | NoneWithReason ).
    /// </summary>
    private static XElement InsuranceBlock(string elementName, string? name, string? uid, DateOnly? seit, string fallbackGrund)
    {
        name = (name ?? "").Trim();
        uid = (uid ?? "").Trim();
        if (name.Length > 0 && seit != null && Regex.IsMatch(uid, @"^CHE-\d{3}\.\d{3}\.\d{3}$"))
            return new XElement(Sd + elementName,
                new XElement(Sd + "Name", name),
                new XElement(Sd + "UID-BFS", new XElement(Ep + "UID", uid)),
                new XElement(Sd + "ValidAsOf", seit.Value.ToString("yyyy-MM-dd")));
        return new XElement(Sd + elementName,
            new XElement(Sd + "NoneWithReason", fallbackGrund));
    }

}
