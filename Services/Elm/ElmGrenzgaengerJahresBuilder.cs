using System.Text.Json;
using System.Xml.Linq;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;
using static HrSystem.Services.Elm.ElmGemeinsam;

namespace HrSystem.Services.Elm;

/// <summary>
/// Grenzgängermeldung als eigene Jahresmeldung (Swissdec TaxCrossborder, S03_08).
/// Drei getrennte DeclareAnnualSalary: Italien, Frankreich, SFN. Keine AHV, keine FAK,
/// und die drei Arten nie in derselben Datei. Personen kommen aus der Quellensteuer
/// (<see cref="GrenzgaengerMeldung"/>), Beträge aus den Lohnzetteln des Jahres.
/// </summary>
public class ElmGrenzgaengerJahresBuilder
{
    public const string ArtItalien = "grenz-it";
    public const string ArtFrankreich = "grenz-fr";
    public const string ArtSfn = "grenz-sfn";

    private readonly AppDbContext _db;
    private readonly ElmEinstellungen _einstellungen;
    private readonly ElmXmlValidator _validator;

    public ElmGrenzgaengerJahresBuilder(AppDbContext db, ElmEinstellungen einstellungen, ElmXmlValidator validator)
    {
        _db = db;
        _einstellungen = einstellungen;
        _validator = validator;
    }

    public record Ergebnis(string Xml, string Titel, List<string> Warnungen, List<string> XsdFehler);

    public async Task<Ergebnis> BuildAsync(string art, int year, CancellationToken ct = default)
    {
        var titel = art switch
        {
            ArtItalien => $"Grenzgänger Italien {year}",
            ArtFrankreich => $"Grenzgänger Frankreich {year}",
            ArtSfn => $"Grenzgänger SFN {year}",
            _ => throw new InvalidOperationException($"Unbekannte Grenzgängermeldung «{art}»."),
        };
        var warn = new List<string>();
        var stamm = await LadeRechtseinheitAsync(_db, warn, ct);
        if (stamm == null)
            return new Ergebnis("", titel, warn, new List<string>());

        var von = new DateOnly(year, 1, 1);
        var bis = new DateOnly(year, 12, 31);
        var versionen = await _db.EmployeeQuellensteuer.AsNoTracking()
            .Where(q => q.ValidFrom <= bis && (q.ValidTo == null || q.ValidTo >= von))
            .ToListAsync(ct);
        var gewaehlt = versionen
            .GroupBy(q => q.EmployeeId)
            .Select(g => Waehle(art, g.OrderByDescending(q => q.ValidFrom).ToList()))
            .Where(q => q != null)
            .Cast<EmployeeQuellensteuer>()
            .ToList();
        if (gewaehlt.Count == 0)
        {
            warn.Add(art switch
            {
                ArtItalien => $"Keine Person mit Datenaustausch Italien im Jahr {year}.",
                ArtFrankreich => $"Keine Person mit Wohnsitz Frankreich und Arbeitsort Schweiz im Jahr {year}.",
                _ => $"Keine Person mit Tarifcode SFN im Jahr {year}.",
            });
            return new Ergebnis("", titel, warn, new List<string>());
        }

        var empIds = gewaehlt.Select(q => q.EmployeeId).ToList();
        var emps = await _db.Employees.AsNoTracking().Include(e => e.NationalityRef)
            .Where(e => empIds.Contains(e.Id)).ToListAsync(ct);
        var empById = emps.ToDictionary(e => e.Id);
        var employments = await _db.Employments.AsNoTracking()
            .Where(em => empIds.Contains(em.EmployeeId)).ToListAsync(ct);
        var filialen = stamm.Filialen.ToDictionary(b => b.Id);

        var slips = await (from s in _db.PayrollSnapshots
                           join p in _db.PayrollPerioden on s.PayrollPeriodeId equals p.Id
                           where p.Year == year && s.Status != "STORNIERT" && empIds.Contains(s.EmployeeId)
                           select new { s.EmployeeId, s.SlipJson, p.Month, p.Status })
                          .ToListAsync(ct);
        var offene = slips.Where(s => s.Status != "abgeschlossen").Select(s => s.Month).Distinct().OrderBy(m => m).ToList();
        if (offene.Count > 0)
            warn.Add($"Nicht definitiv abgeschlossene Monate im XML enthalten: {string.Join(", ", offene)}.");

        var kassen = await _db.LohndatenEmpfaengers.AsNoTracking()
            .Where(e => e.Art == "QST" && e.IsActive)
            .ToListAsync(ct);

        var yearStart = new DateTime(year, 1, 1);
        var yearEnd = new DateTime(year, 12, 31);
        var personen = new List<(string Kanton, decimal Steuerbar, XElement Person)>();
        foreach (var q in gewaehlt.OrderBy(x => x.EmployeeId))
        {
            if (!empById.TryGetValue(q.EmployeeId, out var e) || e.IsPayrollExcluded) continue;
            if (art == ArtFrankreich && !PasstFrankreich(q, e, employments, filialen, yearStart, yearEnd))
                continue;
            var name = $"{e.FirstName} {e.LastName}".Trim();
            if (e.DateOfBirth == null)
            {
                warn.Add($"{name}: Geburtsdatum fehlt — nicht in der Meldung.");
                continue;
            }
            var em = employments
                .Where(m => m.EmployeeId == e.Id
                            && m.ContractStartDate <= yearEnd
                            && (m.ContractEndDate == null || m.ContractEndDate >= yearStart))
                .OrderByDescending(m => m.ContractStartDate)
                .FirstOrDefault();
            if (em == null)
            {
                warn.Add($"{name}: kein Vertrag im Jahr {year} — nicht in der Meldung.");
                continue;
            }
            filialen.TryGetValue(em.CompanyProfileId ?? 0, out var filiale);

            var kanton = (q.Steuerkanton ?? "").Trim().ToUpperInvariant();
            if (kanton.Length != 2)
            {
                warn.Add($"{name}: Steuerkanton fehlt — nicht in der Meldung.");
                continue;
            }
            var gehalt = Gehalt(art, q, e, kanton, von, bis, em, slips.Where(s => s.EmployeeId == e.Id).Select(s => s.SlipJson), warn);
            if (gehalt == null) continue;

            var work = new XElement(C + "Work",
                new XAttribute("workID", WorkId(e)),
                new XElement(C + "WorkingTime", WorkingTime(em, filiale?.NormalWeeklyHours ?? stamm.Haupt.NormalWeeklyHours ?? 42m)),
                new XElement(C + "EntryDate", (e.EntryDate ?? em.ContractStartDate).ToString("yyyy-MM-dd")));
            if (e.ExitDate != null && e.ExitDate.Value >= yearStart && e.ExitDate.Value <= yearEnd)
                work.Add(new XElement(C + "WithdrawalDate", e.ExitDate.Value.ToString("yyyy-MM-dd")));
            personen.Add((kanton, gehalt.Value.Steuerbar, new XElement(Sd + "Person", Particulars(e, warn), work, gehalt.Value.Xml)));
        }

        var kantone = personen.Select(p => p.Kanton).Distinct().OrderBy(k => k).ToList();
        var mitKasse = new List<string>();
        foreach (var kt in kantone)
        {
            var nummer = (kassen.FirstOrDefault(x => (x.KantonCode ?? "").Trim().ToUpperInvariant() == kt)?.Kassennummer ?? "").Trim();
            if (nummer.Length == 0)
            {
                warn.Add($"Kanton {kt}: SSL-Nummer fehlt — diese Personen sind nicht in der Meldung.");
                continue;
            }
            mitKasse.Add(kt);
        }
        personen = personen.Where(p => mitKasse.Contains(p.Kanton)).ToList();
        if (personen.Count == 0)
        {
            if (!warn.Any(w => w.Contains("SSL-Nummer") || w.Contains("nicht in der Meldung")))
                warn.Add(art switch
                {
                    ArtItalien => $"Keine Person mit Datenaustausch Italien im Jahr {year}.",
                    ArtFrankreich => $"Keine Person mit Wohnsitz Frankreich und Arbeitsort Schweiz im Jahr {year}.",
                    _ => $"Keine Person mit Tarifcode SFN im Jahr {year}.",
                });
            return new Ergebnis("", titel, warn, new List<string>());
        }

        var job = new List<XElement>();
        var institutions = new List<XElement>();
        var totals = new List<XElement>();
        foreach (var kt in mitKasse)
        {
            var id = $"#QST-{kt}";
            var nummer = kassen.First(x => (x.KantonCode ?? "").Trim().ToUpperInvariant() == kt).Kassennummer!.Trim();
            job.Add(new XElement(Sdc + "Addressee",
                new XAttribute("addresseeID", id),
                new XElement(Ep + "AddresseeIdentification", kt),
                new XElement(Ep + "ProcessByDistributor", "true")));
            institutions.Add(new XElement(Sd + "TaxCrossborder",
                new XAttribute("addresseeIDRef", id),
                new XElement(Sd + "CustomerIdentity", nummer),
                new XElement(Sd + "TaxAtSourceType", "salaries")));
            totals.Add(new XElement(Sd + "TaxCrossborderTotals",
                new XAttribute("addresseeIDRef", id),
                new XElement(Sd + "Period",
                    new XElement(Ep + "from", von.ToString("yyyy-MM-dd")),
                    new XElement(Ep + "until", bis.ToString("yyyy-MM-dd"))),
                new XElement(Sd + "TaxAtSourceCanton", kt),
                new XElement(Sd + "TaxableEarning", Betrag05(personen.Where(p => p.Kanton == kt).Sum(p => p.Steuerbar)))));
        }

        var now = DateTime.Now;
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Sdst + "DeclareAnnualSalary",
                new XAttribute(XNamespace.Xmlns + "sdst", Sdst),
                new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
                new XAttribute(XNamespace.Xmlns + "sd", Sd),
                new XAttribute(XNamespace.Xmlns + "ep", Ep),
                new XAttribute(XNamespace.Xmlns + "c", C),
                RequestContext(stamm.Firmenname, now, _einstellungen),
                new XElement(Sdc + "Job",
                    new XElement(Sdc + "Addressees", job),
                    new XElement(Sdc + "TestCase")),
                new XElement(Sd + "AnnualSalaryDeclaration",
                    new XAttribute("schemaVersion", "0.0"),
                    CompanyDescription(stamm, new[]
                    {
                        new XElement(C + "CompanyWorkingTime",
                            new XAttribute("companyWorkingTimeID", "#cwt1"),
                            new XElement(C + "WeeklyHours", Amt(stamm.Haupt.NormalWeeklyHours ?? 42m)))
                    }),
                    new XElement(Sd + "Staff", personen.Select(p => p.Person)),
                    new XElement(Sd + "Institutions", institutions),
                    new XElement(Sd + "SalaryTotals", totals),
                    new XElement(Sd + "SalaryCounters",
                        new XElement(Sd + "NumberOf-TaxCrossborderSalary-Tags", personen.Count)),
                    new XElement(Sd + "GeneralSalaryDeclarationDescription",
                        new XElement(Sd + "CreationDate", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
                        new XElement(Sd + "AccountingPeriod", year)))));

        var xml = doc.Declaration + Environment.NewLine + doc.ToString();
        return new Ergebnis(xml, titel, warn, _validator.Validate(xml));
    }

    /// <summary>Die Version, die diese Meldung trägt. SFN gehört nur in die SFN-Meldung, nicht nach Frankreich.</summary>
    private static EmployeeQuellensteuer? Waehle(string art, List<EmployeeQuellensteuer> versionen)
    {
        bool Sfn(EmployeeQuellensteuer q) => GrenzgaengerMeldung.Sfn(q.QstCode) || GrenzgaengerMeldung.Sfn(q.TarifCode);
        return art switch
        {
            ArtItalien => versionen.FirstOrDefault(q => q.DatenaustauschItalien),
            ArtSfn => versionen.FirstOrDefault(Sfn),
            ArtFrankreich => versionen.Any(Sfn) || versionen.Any(q => q.DatenaustauschItalien)
                ? null : versionen.FirstOrDefault(),
            _ => null,
        };
    }

    private static bool PasstFrankreich(EmployeeQuellensteuer q, Employee e, List<Employment> employments,
        Dictionary<int, CompanyProfile> filialen, DateTime yearStart, DateTime yearEnd)
    {
        var arbeitCh = employments.Any(m => m.EmployeeId == e.Id
            && m.ContractStartDate <= yearEnd
            && (m.ContractEndDate == null || m.ContractEndDate >= yearStart)
            && FilialeSchweiz(m.CompanyProfileId is int id && filialen.TryGetValue(id, out var b) ? b : null));
        var nat = e.NationalityRef?.Code;
        return GrenzgaengerMeldung.Frankreich(q.Wohnsitzstaat ?? e.Country, arbeitCh, nat, false)
               || GrenzgaengerMeldung.Frankreich(e.Country, arbeitCh, nat, false);
    }

    private static bool FilialeSchweiz(CompanyProfile? b)
    {
        if (b == null) return true;
        var land = (b.Country ?? "").Trim().ToUpperInvariant();
        if (land.Length > 0 && land is not ("CH" or "CHE" or "SCHWEIZ" or "SWITZERLAND" or "SUISSE" or "SVIZZERA"))
            return false;
        return (b.KantonCode ?? "").Trim().ToUpperInvariant() != "EX";
    }

    private static (XElement Xml, decimal Steuerbar)? Gehalt(string art, EmployeeQuellensteuer q, Employee e,
        string kanton, DateOnly jahrVon, DateOnly jahrBis, Employment em, IEnumerable<string> slips, List<string> warn)
    {
        var name = $"{e.FirstName} {e.LastName}".Trim();
        var von = q.GrenzgaengerAb is DateOnly ab && ab > jahrVon ? ab : jahrVon;
        var bis = jahrBis;
        var start = DateOnly.FromDateTime(em.ContractStartDate);
        if (start > von) von = start;
        if (em.ContractEndDate is DateTime ende)
        {
            var endeTag = DateOnly.FromDateTime(ende);
            if (endeTag < bis) bis = endeTag;
        }
        if (q.ValidTo is DateOnly vt && vt < bis) bis = vt;
        if (von > bis)
        {
            warn.Add($"{name}: kein Zeitraum im Jahr — nicht in der Meldung.");
            return null;
        }

        var id = $"#QST-{kanton}";
        var wohn = Ausland(q.Wohnsitzstaat, e.Country);
        decimal steuerbar = 0, abzug = 0, ahv = 0, alv = 0, alvz = 0, nbu = 0, bvg = 0;
        foreach (var roh in slips)
        {
            if (string.IsNullOrWhiteSpace(roh)) continue;
            try
            {
                using var doc = JsonDocument.Parse(roh);
                if (!doc.RootElement.TryGetProperty("abzugLines", out var lines) || lines.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var z in lines.EnumerateArray())
                {
                    var cat = "";
                    if (z.TryGetProperty("categoryCode", out var c) && c.ValueKind == JsonValueKind.String)
                        cat = (c.GetString() ?? "").Trim().ToUpperInvariant();
                    var betrag = Zahl(z, "betrag");
                    switch (cat)
                    {
                        case "QST":
                            steuerbar += PayrollCalculations.Round05(Zahl(z, "basis"));
                            abzug += PayrollCalculations.Round05(Math.Abs(betrag));
                            break;
                        case "AHV": ahv += betrag; break;
                        case "ALV": alv += betrag; break;
                        case "ALVZ": alvz += betrag; break;
                        case "NBUV": nbu += betrag; break;
                        case "BVG": bvg += betrag; break;
                    }
                }
            }
            catch (JsonException) { /* unleserlicher Lohnzettel zählt nicht */ }
        }
        var sv = Math.Abs(PayrollCalculations.Round05(ahv)) + Math.Abs(PayrollCalculations.Round05(alv))
                 + Math.Abs(PayrollCalculations.Round05(alvz)) + Math.Abs(PayrollCalculations.Round05(nbu));
        var bvgGerundet = Math.Abs(PayrollCalculations.Round05(bvg));

        XElement Periode() => new(Sd + "Period",
            new XElement(Ep + "from", von.ToString("yyyy-MM-dd")),
            new XElement(Ep + "until", bis.ToString("yyyy-MM-dd")));

        if (art == ArtItalien)
        {
            var ort = (q.GrenzgaengerGeburtsort ?? "").Trim();
            var steuerId = (q.GrenzgaengerSteuerId ?? "").Trim().ToUpperInvariant().Replace(" ", "");
            if (ort.Length == 0 || q.GrenzgaengerAb == null || !CodiceFiscale.Gueltig(steuerId, out _))
            {
                warn.Add($"{name}: Geburtsort, italienische Steuernummer oder Grenzgänger-Datum fehlt — nicht in der Meldung.");
                return null;
            }
            return (new XElement(Sd + "TaxCrossborderSalaries",
                new XElement(Sd + "ItalianCrossborderSalary",
                    new XAttribute("addresseeIDRef", id),
                    Periode(),
                    new XElement(Sd + "TaxAtSourceCanton", kanton),
                    new XElement(Sd + "ResidenceAbroadCountry", wohn ?? "IT"),
                    new XElement(Sd + "PlaceOfBirth", ort),
                    new XElement(Sd + "TaxID", steuerId),
                    new XElement(Sd + "CrossborderValidAsOf", q.GrenzgaengerAb.Value.ToString("yyyy-MM-dd")),
                    new XElement(Sd + "TaxableEarning", Betrag05(steuerbar)),
                    new XElement(Sd + "DeductionAtSource", Betrag05(abzug)),
                    new XElement(Sd + "AHV-ALV-NBUV-AVS-AC-AANP-Contribution", Betrag05(sv)),
                    new XElement(Sd + "BVG-LPP-ContributionRegular", Betrag05(bvgGerundet)))), steuerbar);
        }

        if (art == ArtSfn)
        {
            return (new XElement(Sd + "TaxCrossborderSalaries",
                new XElement(Sd + "SFNCrossborderSalary",
                    new XAttribute("addresseeIDRef", id),
                    Periode(),
                    new XElement(Sd + "TaxAtSourceCanton", kanton),
                    new XElement(Sd + "ResidenceAbroadCountry", wohn ?? "FR"),
                    new XElement(Sd + "TaxableEarning", Betrag05(steuerbar)))), steuerbar);
        }

        if (q.TelearbeitProzent == null)
            warn.Add($"{name}: Telearbeit nicht erfasst — als 0 % gemeldet.");
        var prozent = q.TelearbeitProzent ?? 0m;
        if (prozent < 0 || prozent > 100) prozent = 0;
        var fr = new XElement(Sd + "FrenchCrossborderSalary",
            new XAttribute("addresseeIDRef", id),
            Periode(),
            new XElement(Sd + "TaxAtSourceCanton", kanton),
            new XElement(Sd + "ResidenceAbroadCountry", "FR"));
        var geburt = (q.GrenzgaengerGeburtsort ?? "").Trim();
        if (geburt.Length > 0) fr.Add(new XElement(Sd + "PlaceOfBirth", geburt));
        var frId = (q.GrenzgaengerSteuerId ?? "").Trim();
        if (frId.Length > 0) fr.Add(new XElement(Sd + "TaxID", frId));
        fr.Add(new XElement(Sd + "TaxableEarning", Betrag05(steuerbar)));
        fr.Add(new XElement(Sd + "TeleWorkPercentage", Amt(prozent)));
        return (new XElement(Sd + "TaxCrossborderSalaries", fr), steuerbar);
    }

    private static decimal Zahl(JsonElement z, string name)
        => z.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;

    /// <summary>Wohnsitz als ISO-2. Schweiz zählt hier nicht.</summary>
    private static string? Ausland(string? wohnsitz, string? person)
    {
        foreach (var roh in new[] { wohnsitz, person })
        {
            var c = (roh ?? "").Trim().ToUpperInvariant();
            if (c is "FR" or "FRA" or "FRANKREICH" or "FRANCE") return "FR";
            if (c is "IT" or "ITA" or "ITALIEN" or "ITALIA" or "ITALY") return "IT";
            if (c is "DE" or "DEU" or "DEUTSCHLAND" or "GERMANY") return "DE";
            if (c.Length == 2 && c is not ("CH" or "EX")) return c;
        }
        return null;
    }
}
