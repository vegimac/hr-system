using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

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

    private static readonly XNamespace Sdst = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:service:types";
    private static readonly XNamespace Sdc  = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration:container";
    private static readonly XNamespace Sd   = "urn:ch:swissdec:elm:v6:20260306:salarydeclaration";
    private static readonly XNamespace Ep   = "urn:ch:swissdec:basis:v1:20260306:components";
    private static readonly XNamespace C    = "urn:ch:swissdec:common:v3:20260306";

    /// <summary>Monats-Höchstlohn ALV/NBU (148'200 / 12) — E2-Näherung.</summary>
    private const decimal AlvMonatsCap = 12350m;

    public record BuildResult(
        string Xml, int Personen, int Uebersprungen, decimal TotalAhv, decimal TotalAlv,
        List<string> Warnungen, List<string> XsdFehler);

    private static string Amt(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);

    public async Task<BuildResult> BuildAhvAsync(int year, CancellationToken ct = default)
    {
        var warn = new List<string>();

        // ── Stammdaten Rechtseinheit ──────────────────────────────────────
        var branches = await _db.CompanyProfiles.AsNoTracking()
            .OrderBy(p => p.Id).ToListAsync(ct);
        if (branches.Count == 0)
            return new BuildResult("", 0, 0, 0, 0,
                new List<string> { "Keine Filialen (CompanyProfiles) vorhanden." }, new List<string>());
        var main = branches[0];

        // E3-Stammdaten der Rechtseinheit (eine Zeile) — primäre Quelle;
        // Fallback: CompanyProfile-Felder, zuletzt Platzhalter + Hinweis.
        var st = await _db.ElmStammdaten.AsNoTracking()
            .OrderBy(x => x.Id).FirstOrDefaultAsync(ct);

        var uid = (st?.Uid ?? main.UidBfs ?? main.UidNummer ?? "").Trim();
        if (!Regex.IsMatch(uid, @"^CHE-\d{3}\.\d{3}\.\d{3}$"))
        {
            warn.Add($"UID fehlt/ungültig («{uid}») — Platzhalter CHE-123.123.123 eingesetzt (E3-Karte: Stammdaten Rechtseinheit erfassen).");
            uid = "CHE-123.123.123";
        }

        // Kassen-Nummer = Adressierung (Addressee), Abrechnungs-Nummer =
        // unsere Kundennummer bei der Kasse (AK-CC-CustomerNumber).
        var akKasse = (st?.AkKassenNummer ?? "").Trim();
        if (string.IsNullOrEmpty(akKasse))
        {
            var legacy = Regex.Match(main.AhvKasse ?? "", @"[\d.\-/]{3,}");
            akKasse = legacy.Success ? legacy.Value : "";
        }
        if (string.IsNullOrEmpty(akKasse))
        {
            warn.Add("AHV-Ausgleichskassen-Nummer fehlt — Platzhalter 001.234 eingesetzt (E3-Karte: Stammdaten Rechtseinheit erfassen).");
            akKasse = "001.234";
        }
        var akAbrechnung = (st?.AkAbrechnungsNummer ?? "").Trim();
        if (string.IsNullOrEmpty(akAbrechnung)) akAbrechnung = akKasse;

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

            // SV-Nummer: 756.xxxx.xxxx.xx oder <unknown/>
            var svDigits = Regex.Replace(e.SocialSecurityNumber ?? "", @"\D", "");
            XElement svEl;
            if (svDigits.Length == 13)
                svEl = new XElement(C + "SV-AS-Number",
                    $"{svDigits[..3]}.{svDigits.Substring(3, 4)}.{svDigits.Substring(7, 4)}.{svDigits.Substring(11, 2)}");
            else
            {
                svEl = new XElement(C + "unknown");
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): AHV-Nummer fehlt/ungültig — als «unknown» gemeldet.");
            }

            var sex = (e.Gender ?? "").ToLowerInvariant();
            var sexCode = sex.StartsWith("f") || sex.StartsWith("w") ? "F" : "M";
            if (string.IsNullOrEmpty(sex))
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Geschlecht fehlt — als M gemeldet.");

            var civil = MapCivilStatus(e.MaritalStatus);
            var natCode = (e.NationalityRef?.Code ?? "").ToUpperInvariant();
            if (!Regex.IsMatch(natCode, "^[A-Z]{2}$"))
            {
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Nationalität fehlt — als CH gemeldet.");
                natCode = "CH";
            }

            var zip = string.IsNullOrWhiteSpace(e.ZipCode) ? "0000" : e.ZipCode!.Trim();
            var city = string.IsNullOrWhiteSpace(e.City) ? "Unbekannt" : e.City!.Trim();
            if (zip == "0000" || city == "Unbekannt")
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Adresse unvollständig — Platzhalter eingesetzt.");
            var canton = (e.CantonCode ?? "").ToUpperInvariant();
            var landCh = string.IsNullOrWhiteSpace(e.Country) || e.Country!.Trim().ToUpperInvariant() == "CH";
            if (!Regex.IsMatch(canton, "^[A-Z]{2}$"))
            {
                canton = landCh ? "LU" : "EX";
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Wohnkanton fehlt — als {canton} gemeldet.");
            }

            var civilEl = new XElement(C + "CivilStatus", new XElement(C + "Status", civil));
            if (e.MaritalStatusSince != null)
                civilEl.Add(new XElement(C + "ValidAsOf", e.MaritalStatusSince.Value.ToString("yyyy-MM-dd")));

            var particulars = new XElement(C + "Particulars",
                new XElement(C + "Social-InsuranceIdentification", svEl),
                new XElement(C + "EmployeeNumber", e.EmployeeNumber),
                new XElement(C + "Lastname", e.LastName),
                new XElement(C + "Firstname", e.FirstName),
                new XElement(C + "Sex", sexCode),
                new XElement(C + "DateOfBirth", e.DateOfBirth.Value.ToString("yyyy-MM-dd")),
                new XElement(C + "Nationality", natCode),
                civilEl,
                new XElement(C + "Addresses",
                    new XElement(C + "Address",
                        string.IsNullOrWhiteSpace(e.Street) ? null : new XElement(C + "Street", e.Street.Trim()),
                        new XElement(C + "ZIP-Code", zip),
                        new XElement(C + "City", city),
                        new XElement(C + "ResidenceCanton", canton))),
                new XElement(C + "LanguageCode",
                    new[] { "de", "fr", "it", "en" }.Contains((e.LanguageCode ?? "de").ToLowerInvariant())
                        ? (e.LanguageCode ?? "de").ToLowerInvariant() : "de"));

            // WorkingTime: FIX/FIX-M + MTP = Steady, FLEX/unbekannt = Unsteady
            XElement workingTime;
            var model = em?.EmploymentModel?.ToUpperInvariant() ?? "";
            var branchWeekly = main.NormalWeeklyHours ?? 42m;
            if ((model == "FIX" || model == "FIX-M") && em != null)
            {
                var pct = em.EmploymentPercentage ?? 100m;
                var weekly = Math.Round(branchWeekly * pct / 100m, 2);
                workingTime = new XElement(C + "Steady",
                    new XElement(C + "WeeklyHours", Amt(weekly)),
                    new XElement(C + "ActivityRate", Amt(pct)));
            }
            else if (model == "MTP" && em?.GuaranteedHoursPerWeek is decimal gh && gh > 0)
            {
                workingTime = new XElement(C + "Steady",
                    new XElement(C + "WeeklyHours", Amt(gh)),
                    new XElement(C + "ActivityRate", Amt(Math.Round(gh / branchWeekly * 100m, 2))));
            }
            else
            {
                workingTime = new XElement(C + "Unsteady");
            }

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
        var companyName = (main.CompanyName ?? "Schaub Restaurants GmbH").Trim();
        var companyDescription = new XElement(Sd + "CompanyDescription",
            new XElement(C + "Name", new XElement(C + "HR-RC-Name", companyName)),
            new XElement(C + "Address",
                string.IsNullOrWhiteSpace(main.Street) ? null : new XElement(C + "Street", main.Street.Trim()),
                new XElement(C + "ZIP-Code", string.IsNullOrWhiteSpace(main.ZipCode) ? "0000" : main.ZipCode!.Trim()),
                new XElement(C + "City", string.IsNullOrWhiteSpace(main.City) ? "Unbekannt" : main.City!.Trim())),
            new XElement(C + "UID-BFS", new XElement(Ep + "UID", uid)),
            branches.Select(b2 => new XElement(C + "Workplace",
                new XAttribute("workplaceID", $"#wp{b2.Id}"),
                // BUR-Nummer = offizielle Betriebsstätten-Kennung (Muster A63837147);
                // nur mitgeben, wenn sie dem XSD-Pattern [A-Z][0-9]{8} entspricht.
                Regex.IsMatch((b2.BurNummer ?? "").Trim(), "^[A-Z][0-9]{8}$")
                    ? new XElement(C + "BUR-REE-Number", b2.BurNummer!.Trim())
                    : null,
                new XElement(C + "AddressExtended",
                    new XElement(C + "ZIP-Code", string.IsNullOrWhiteSpace(b2.ZipCode) ? "0000" : b2.ZipCode!.Trim()),
                    new XElement(C + "City", string.IsNullOrWhiteSpace(b2.City) ? "Unbekannt" : b2.City!.Trim())))),
            new XElement(C + "CompanyWorkingTime",
                new XAttribute("companyWorkingTimeID", "#cwt1"),
                new XElement(C + "WeeklyHours", Amt(main.NormalWeeklyHours ?? 42m))));

        // ── Gesamtdokument ────────────────────────────────────────────────
        var now = DateTime.Now;
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Sdst + "DeclareAnnualSalary",
                new XAttribute(XNamespace.Xmlns + "sdst", Sdst),
                new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
                new XAttribute(XNamespace.Xmlns + "sd", Sd),
                new XAttribute(XNamespace.Xmlns + "ep", Ep),
                new XAttribute(XNamespace.Xmlns + "c", C),
                new XElement(Ep + "RequestContext",
                    new XElement(Ep + "UserAgent",
                        new XElement(Ep + "Producer", "Schaub Restaurants GmbH"),
                        new XElement(Ep + "Name", "OneCrew"),
                        new XElement(Ep + "Version", "2026.08"),
                        new XElement(Ep + "StandardVersion", "6.0"),
                        new XElement(Ep + "Certificate", "n/a")),
                    new XElement(Ep + "CompanyName", companyName),
                    new XElement(Ep + "TransmissionDate", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
                    new XElement(Ep + "RequestID", Guid.NewGuid().ToString("N")),
                    new XElement(Ep + "LanguageCode", "de")),
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
                            InsuranceBlock("UVG-LAA-Insurance", st?.UvgVersicherer, st?.UvgUid, st?.UvgVersichertSeit,
                                "UVG-Meldung folgt in Aufbau-Etappe E5"),
                            InsuranceBlock("BVG-LPP-Insurance", st?.BvgVersicherer, st?.BvgUid, st?.BvgVersichertSeit,
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

    private static string MapCivilStatus(string? ms)
    {
        var s = (ms ?? "").ToLowerInvariant();
        if (s.Contains("verheiratet")) return "married";
        if (s.Contains("getrennt")) return "separated";
        if (s.Contains("geschieden")) return "divorced";
        if (s.Contains("verwitwet")) return "widowed";
        if (s.Contains("aufgel")) return "partnershipDissolvedByLaw";
        if (s.Contains("partnerschaft")) return "registeredPartnership";
        if (s.Contains("ledig")) return "single";
        return "unknown";
    }
}
