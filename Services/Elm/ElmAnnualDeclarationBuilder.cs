using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HrSystem.Data;
using HrSystem.Models;
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

    private readonly ElmEinstellungen _einstellungen;

    public ElmAnnualDeclarationBuilder(AppDbContext db, ElmXmlValidator validator, ElmEinstellungen einstellungen)
    {
        _db = db;
        _validator = validator;
        _einstellungen = einstellungen;
    }

    /// <summary>Monats-Höchstlohn ALV/NBU (148'200 / 12) — E2-Näherung.</summary>
    private const decimal AlvMonatsCap = 12350m;

    public record BuildResult(
        string Xml, int Personen, int Uebersprungen, decimal TotalAhv, decimal TotalAlv,
        List<string> Warnungen, List<string> XsdFehler);

    public Task<BuildResult> BuildAhvAsync(int year, CancellationToken ct = default)
        => BuildAhvAsync(year, null, ct);

    /// <summary>Mit nurFiliale nur die Lohnzettel und Arbeitsorte dieser Filiale. Ohne: alle Filialen der Rechtseinheit.</summary>
    public async Task<BuildResult> BuildAhvAsync(int year, int? nurFiliale, CancellationToken ct = default)
    {
        var warn = new List<string>();

        // ── Stammdaten Rechtseinheit (gemeinsam mit der Monatsmeldung) ────
        var stamm = await LadeRechtseinheitAsync(_db, warn, ct);
        if (stamm == null)
            return new BuildResult("", 0, 0, 0, 0, warn, new List<string>());
        if (nurFiliale is int cp && cp > 0)
        {
            var nur = stamm.Filialen.Where(b => b.Id == cp).ToList();
            if (nur.Count == 0)
            {
                warn.Add("Diese Filiale ist in der Meldung nicht enthalten.");
                return new BuildResult("", 0, 0, 0, 0, warn, new List<string>());
            }
            stamm = stamm with { Filialen = nur };
        }
        var branches = stamm.Filialen;
        var filialIds = branches.Select(b => b.Id).ToHashSet();
        var ganzeFirma = nurFiliale is not > 0;
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
        // Stichtag für die Zuordnung = 31.12. des Lohnjahres (Gültig ab/bis, Walter 07.09.2026)
        var stichtagZuord = new DateOnly(year, 12, 31);
        bool GehoertHier(Models.LohndatenEmpfaenger e) =>
            ganzeFirma
            || e.Zuordnungen.Any(z => filialIds.Contains(z.CompanyProfileId) && z.GiltAm(stichtagZuord));
        var uvgE = empf.FirstOrDefault(e => e.Art == "UVG" && !IstZusatz(e) && GehoertHier(e));
        var bvgE = empf.FirstOrDefault(e => e.Art == "BVG" && GehoertHier(e));
        string? GemeinsameMitgliedNr(Models.LohndatenEmpfaenger? e)
        {
            if (e == null) return null;
            var nrs = e.Zuordnungen.Where(z => z.GiltAm(stichtagZuord)
                    && (ganzeFirma || filialIds.Contains(z.CompanyProfileId)))
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
            if (akE != null && akE.Zuordnungen.Any(z => z.GiltAm(stichtagZuord)
                    && (ganzeFirma || filialIds.Contains(z.CompanyProfileId))))
                warn.Add(nurFiliale is int and > 0
                    ? "AK-Mitgliednummer dieser Filiale ist nicht eindeutig — im XML steht die Kassen-Nr."
                    : "AK-Mitgliednummern sind pro Filiale unterschiedlich — im XML steht vorerst die Kassen-Nr. (Zuordnung pro Filiale kommt in E5).");
            akAbrechnung = akKasse;
        }

        // ── Lohndaten des Jahres (alle Filialen, STORNIERTE ausgenommen) ──
        var lohn = from s in _db.PayrollSnapshots
                   join p in _db.PayrollPerioden on s.PayrollPeriodeId equals p.Id
                   where p.Year == year && s.Status != "STORNIERT"
                   select new { s.EmployeeId, s.SvBasisAhv, p.Month, PeriodeStatus = p.Status, p.CompanyProfileId, s.SlipJson };
        if (nurFiliale is int fil && fil > 0)
            lohn = lohn.Where(r => r.CompanyProfileId == fil);
        var rows = await lohn.ToListAsync(ct);
        if (rows.Count == 0)
            return new BuildResult("", 0, 0, 0, 0,
                new List<string> { nurFiliale is int and > 0
                    ? $"Keine Lohnabrechnungen für {year} bei dieser Filiale gefunden."
                    : $"Keine Lohnabrechnungen für {year} gefunden." }, new List<string>());

        var offenePerioden = rows.Where(r => r.PeriodeStatus != "abgeschlossen")
            .Select(r => r.Month).Distinct().OrderBy(m => m).ToList();
        if (offenePerioden.Count > 0)
            warn.Add($"Nicht definitiv abgeschlossene Monate im XML enthalten: {string.Join(", ", offenePerioden)} — für Übungszwecke ok, für eine echte Meldung müssen alle Monate abgeschlossen sein.");

        var perEmp = rows.GroupBy(r => r.EmployeeId)
            .Select(g => new { EmployeeId = g.Key, Ahv = g.Sum(r => r.SvBasisAhv) })
            .Where(x => x.Ahv > 0).OrderBy(x => x.EmployeeId).ToList();
        var rowsByEmp = rows.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

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
        var versicherung = await _db.EmployeeVersicherungCodes.AsNoTracking()
            .Where(v => empIds.Contains(v.EmployeeId) && v.Art == EmployeeVersicherungCode.ArtAhv)
            .ToListAsync(ct);
        var codesByEmp = versicherung.GroupBy(v => v.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EmployeeVersicherungCode>)g.ToList());
        var ahvStand = await AhvBeitragspflichtStand.LadenAsync(_db);

        // FAK pro Person (Walter 01.11.2026): Kasse und Arbeitskanton je Monat aus der Filiale des
        // Lohnzettels, Zulagen aus den Lohnzeilen (Swissdec-Lohnart über Code bzw. Zusatzfeld).
        var workplaceIds = branches.Select(b => b.Id).ToHashSet();
        var kantonByFiliale = branches.ToDictionary(b => b.Id, b => b.KantonCode);
        var lohnartByCode = (await _db.Lohnpositionen.AsNoTracking()
                .Select(l => new { l.Code, l.SwissdecLohnart }).ToListAsync(ct))
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => (g.First().SwissdecLohnart ?? "").Trim());
        var kasseCache = new Dictionary<(int Filiale, int Monat), ElmFakAdressierung.Eintrag?>();
        ElmFakAdressierung.Eintrag? KasseIm(int filiale, int monat)
        {
            if (!kasseCache.TryGetValue((filiale, monat), out var k))
            {
                var stichtag = new DateOnly(year, monat, DateTime.DaysInMonth(year, monat));
                k = ElmFakAdressierung.FuerFiliale(empf, filiale, stichtag, workplaceIds, id => kantonByFiliale.GetValueOrDefault(id));
                kasseCache[(filiale, monat)] = k;
            }
            return k;
        }
        var fakGenutzt = new Dictionary<string, ElmFakAdressierung.Eintrag>(StringComparer.OrdinalIgnoreCase);
        var fakSummen = new Dictionary<string, Dictionary<string, ElmFakAdressierung.Summe>>(StringComparer.OrdinalIgnoreCase);
        var fakTags = 0;

        // ── Personen ──────────────────────────────────────────────────────
        var persons = new List<XElement>();
        int skipped = rows.GroupBy(r => r.EmployeeId).Count() - perEmp.Count;
        if (skipped > 0)
            warn.Add($"{skipped} MA ohne AHV-pflichtigen Lohn {year} übersprungen (z.B. unter 18 / nur 0-Läufe).");
        decimal totalAhv = 0, totalAlv = 0, totalAhvFrei = 0, totalAlvFrei = 0;
        var salaryTags = 0;

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

            codesByEmp.TryGetValue(x.EmployeeId, out var codes);
            var monate = rowsByEmp[x.EmployeeId]
                .GroupBy(r => r.Month)
                .OrderBy(g => g.Key)
                .Select(g => new ElmAhvJahreslohn.Monat(
                    g.Key,
                    g.Sum(r => r.SvBasisAhv),
                    g.Sum(r => Math.Min(r.SvBasisAhv, AlvMonatsCap)),
                    ArtDesMonats(e, year, g.Key, codes, ahvStand)))
                .ToList();

            // Ein-/Austritt im ersten bzw. letzten Monat eines Abschnitts: genaues Datum (Swissdec-Referenz).
            var eigeneVertraege = employments.Where(m => m.EmployeeId == x.EmployeeId).ToList();
            (DateTime Von, DateTime Bis) Zeit(int von, int bis)
            {
                var vonKette = ElmMonthlyDeclarationBuilder.Anstellung(eigeneVertraege,
                    new DateTime(year, von, 1), new DateTime(year, von, DateTime.DaysInMonth(year, von)));
                var bisKette = ElmMonthlyDeclarationBuilder.Anstellung(eigeneVertraege,
                    new DateTime(year, bis, 1), new DateTime(year, bis, DateTime.DaysInMonth(year, bis)));
                return ElmFakJahreslohn.Zeitraum(year, von, bis, vonKette?.Start, bisKette?.Ende);
            }

            var salaries = new XElement(Sd + "AHV-AVS-Salaries");
            var ahvAbschnitte = ElmAhvJahreslohn.Abschnitte(monate);
            foreach (var a in ahvAbschnitte)
            {
                var (from, until) = Zeit(a.Von, a.Bis);
                var salary = new XElement(Sd + "AHV-AVS-Salary",
                    new XAttribute("addresseeIDRef", "#ahv"),
                    new XElement(Sd + "AccountingTime",
                        new XElement(Ep + "from", from.ToString("yyyy-MM-dd")),
                        new XElement(Ep + "until", until.ToString("yyyy-MM-dd"))),
                    new XElement(Sd + "AHV-AVS-BaseSalary", Betrag05(a.Basis)),
                    new XElement(Sd + "AHV-AVS-Income", Betrag05(a.AhvLohn)));
                if (a.AhvFrei != 0)
                    salary.Add(new XElement(Sd + "AHV-AVS-Open", Betrag05(a.AhvFrei)));
                salary.Add(new XElement(Sd + "ALV-AC-Income", Betrag05(a.AlvLohn)));
                if (a.AlvFrei != 0)
                    salary.Add(new XElement(Sd + "ALV-AC-Open", Betrag05(a.AlvFrei)));
                if (a.Art == ElmAhvJahreslohn.Art.Verzicht)
                    salary.Add(new XElement(Sd + "WaiveOfPensionDeduct"));
                salaries.Add(salary);
                salaryTags++;
                // Totale = Summe der gemeldeten (gerundeten) Einzelwerte, sonst passen sie nicht zusammen.
                totalAhv += PayrollCalculations.Round05(a.AhvLohn);
                totalAlv += PayrollCalculations.Round05(a.AlvLohn);
                totalAhvFrei += PayrollCalculations.Round05(a.AhvFrei);
                totalAlvFrei += PayrollCalculations.Round05(a.AlvFrei);
            }

            var fakSalaries = FakSalaries(e, x.EmployeeId, monate, ahvAbschnitte, Zeit);
            persons.Add(new XElement(Sd + "Person", particulars, work, salaries, fakSalaries));
        }

        // FAK-CAF-Salaries einer Person: je AHV-Abschnitt mit AHV-Lohn, aufgeteilt nach Kasse und Kanton.
        XElement? FakSalaries(Employee e, int empId, List<ElmAhvJahreslohn.Monat> monate,
            List<ElmAhvJahreslohn.Abschnitt> ahvAbschnitte, Func<int, int, (DateTime Von, DateTime Bis)> zeit)
        {
            var fakMonate = new List<ElmFakJahreslohn.Monat>();
            var ohneKasse = new List<int>();
            foreach (var g in rowsByEmp[empId].GroupBy(r => r.Month))
            {
                var art = monate.FirstOrDefault(m => m.Nr == g.Key).Art;
                var filiale = g.OrderByDescending(r => r.SvBasisAhv).First().CompanyProfileId;
                var kasse = KasseIm(filiale, g.Key);
                if (kasse == null) ohneKasse.Add(g.Key);
                decimal wieder = 0, einmal = 0;
                foreach (var r in g) (wieder, einmal) = Zulagen(r.SlipJson, wieder, einmal);
                var kanton = (kantonByFiliale.GetValueOrDefault(filiale) ?? "").Trim().ToUpperInvariant();
                if (kanton.Length != 2) kanton = kasse?.Kantone.FirstOrDefault() ?? "EX";
                fakMonate.Add(new ElmFakJahreslohn.Monat(g.Key,
                    art == ElmAhvJahreslohn.Art.Frei ? 0m : g.Sum(r => r.SvBasisAhv),
                    wieder, einmal, kasse?.Kassennummer, kanton));
                if (kasse != null) fakGenutzt.TryAdd(kasse.Kassennummer, kasse);
            }
            if (ohneKasse.Count > 0)
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): keine FAK an der Filiale in Monat {string.Join(", ", ohneKasse.Distinct().OrderBy(m => m))} "
                       + "— dort fehlt die FAK-Zeile (Filiale → Lohndaten Empfänger, Art FAK).");

            var el = new XElement(Sd + "FAK-CAF-Salaries");
            foreach (var a in ahvAbschnitte.Where(a => a.AhvLohn != 0))
            {
                var (von, bis) = zeit(a.Von, a.Bis);
                foreach (var f in ElmFakJahreslohn.Abschnitte(year, a.Von, a.Bis, fakMonate, von, bis))
                {
                    var kasse = fakGenutzt[f.Kasse];
                    el.Add(new XElement(Sd + "FAK-CAF-Salary",
                        new XAttribute("addresseeIDRef", kasse.AddresseeId),
                        new XElement(Sd + "FAK-CAF-Period",
                            new XElement(Ep + "from", f.Von.ToString("yyyy-MM-dd")),
                            new XElement(Ep + "until", f.Bis.ToString("yyyy-MM-dd"))),
                        new XElement(Sd + "FAK-CAF-ContributorySalary", Betrag05(f.Lohn)),
                        new XElement(Sd + "FAK-CAF-FamilyIncomeSupplementRepetitive", Amt(f.Wiederkehrend)),
                        new XElement(Sd + "FAK-CAF-FamilyIncomeSupplementSingular", Amt(f.Einmalig)),
                        new XElement(Sd + "FAK-CAF-WorkplaceCanton", f.Kanton)));
                    fakTags++;
                    if (!fakSummen.TryGetValue(f.Kasse, out var jeKanton))
                        fakSummen[f.Kasse] = jeKanton = new Dictionary<string, ElmFakAdressierung.Summe>(StringComparer.OrdinalIgnoreCase);
                    var alt = jeKanton.TryGetValue(f.Kanton, out var v) ? v : default;
                    // Totale = Summe der gemeldeten (gerundeten) Einzelwerte, wie bei der AHV.
                    jeKanton[f.Kanton] = new ElmFakAdressierung.Summe(alt.Lohn + PayrollCalculations.Round05(f.Lohn),
                        alt.Wiederkehrend + f.Wiederkehrend, alt.Einmalig + f.Einmalig);
                }
            }
            return el.HasElements ? el : null;
        }

        // Zulagen eines Lohnzettels: Lohnzeilen und «Weitere Zahlungen» mit Swissdec-Lohnart 3000–3049.
        (decimal Wieder, decimal Einmal) Zulagen(string? slipJson, decimal wieder, decimal einmal)
        {
            if (string.IsNullOrWhiteSpace(slipJson)) return (wieder, einmal);
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(slipJson);
                foreach (var liste in new[] { "lohnLines", "zulagenExtraLines" })
                {
                    if (!doc.RootElement.TryGetProperty(liste, out var ll) || ll.ValueKind != System.Text.Json.JsonValueKind.Array) continue;
                    foreach (var z in ll.EnumerateArray())
                    {
                        var code = z.TryGetProperty("code", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String ? c.GetString() : null;
                        if (!int.TryParse(ElmMonthlyDeclarationBuilder.SwissdecNummer(code, lohnartByCode), out var nr)) continue;
                        var betrag = z.TryGetProperty("betrag", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.Number ? b.GetDecimal() : 0m;
                        switch (ElmFakJahreslohn.Zulage(nr))
                        {
                            case ElmFakJahreslohn.ZulageArt.Wiederkehrend: wieder += betrag; break;
                            case ElmFakJahreslohn.ZulageArt.Einmalig: einmal += betrag; break;
                        }
                    }
                }
            }
            catch (System.Text.Json.JsonException) { /* unlesbarer Lohnzettel: keine Zulagen */ }
            return (wieder, einmal);
        }

        // ── FAK Nullmeldung (S_ELM_V6_01_01_05 / Direktive 8.2.4) ─────────
        // Alle FAKs, die einem Workplace der Rechtseinheit am Stichtag
        // zugeordnet sind — auch ohne Personen an diesem Ort. Alte/abgelaufene
        // und nicht zugewiesene Kassen bleiben draussen.
        var faks = ElmFakAdressierung.Ergaenzt(
            ElmFakAdressierung.AusZuordnungen(empf, workplaceIds, stichtagZuord, id => kantonByFiliale.GetValueOrDefault(id)),
            fakGenutzt.Values);
        foreach (var f in faks.Where(x => x.Kantone.Count == 0))
            warn.Add($"FAK {f.Kassennummer} ({f.Bezeichnung}): kein Kanton an Filiale/Empfänger — Totals mit EX.");
        var ohnePersonen = faks.Count(f => !fakSummen.ContainsKey(f.Kassennummer));
        if (ohnePersonen > 0)
            warn.Add($"FAK Nullmeldung: {ohnePersonen} Kasse(n) ohne Personen im Job (zugeordnete Arbeitsorte).");

        // ── Firmenbeschreibung: Rechtseinheit + alle Filialen als Workplaces ─
        var companyName = stamm.Firmenname;
        var companyDescription = CompanyDescription(stamm, new[]
        {
            new XElement(C + "CompanyWorkingTime",
                new XAttribute("companyWorkingTimeID", "#cwt1"),
                new XElement(C + "WeeklyHours", Amt(main.NormalWeeklyHours ?? 42m)))
        });

        var jobAddressees = new List<XElement>
        {
            new(Sdc + "Addressee",
                new XAttribute("addresseeID", "#ahv"),
                new XElement(Ep + "AddresseeIdentification", akKasse),
                new XElement(Ep + "ProcessByDistributor", "true"))
        };
        jobAddressees.AddRange(faks.Select(ElmFakAdressierung.JobAddressee));

        var institutions = new List<XElement>
        {
            new(Sd + "AHV-AVS",
                new XAttribute("addresseeIDRef", "#ahv"),
                new XElement(Sd + "AK-CC-CustomerNumber", akAbrechnung),
                InsuranceBlock("UVG-LAA-Insurance", uvgE?.Bezeichnung, uvgE?.UidNummer, st?.UvgVersichertSeit,
                    "UVG-Meldung folgt in Aufbau-Etappe E5"),
                InsuranceBlock("BVG-LPP-Insurance", bvgE?.Bezeichnung, bvgE?.UidNummer, st?.BvgVersichertSeit,
                    "BVG-Meldung folgt in Aufbau-Etappe E5"))
        };
        institutions.AddRange(faks.Select(ElmFakAdressierung.Institution));

        var salaryTotals = new List<XElement>
        {
            new(Sd + "AHV-AVS-Totals",
                new XAttribute("addresseeIDRef", "#ahv"),
                new XElement(Sd + "Total-AHV-AVS-Incomes", Amt(totalAhv)),
                new XElement(Sd + "Total-AHV-AVS-Open", Amt(totalAhvFrei)),
                new XElement(Sd + "Total-ALV-AC-Incomes", Amt(totalAlv)),
                new XElement(Sd + "Total-ALVZ-ACS-Incomes", "0.00"),
                new XElement(Sd + "Total-ALV-AC-Open", Amt(totalAlvFrei)))
        };
        salaryTotals.AddRange(faks.Select(f => ElmFakAdressierung.Totals(f,
            fakSummen.TryGetValue(f.Kassennummer, out var jeKanton) ? jeKanton : new Dictionary<string, ElmFakAdressierung.Summe>())));

        var counters = new List<XElement>
        {
            new(Sd + "NumberOf-AHV-AVS-Salary-Tags", salaryTags)
        };
        if (faks.Count > 0)
            counters.Add(new XElement(Sd + "NumberOf-FAK-CAF-Salary-Tags", fakTags));

        // ── Gesamtdokument ────────────────────────────────────────────────
        var now = DateTime.Now;
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Sdst + "DeclareAnnualSalary",
                new XAttribute(XNamespace.Xmlns + "sdst", Sdst),
                new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
                new XAttribute(XNamespace.Xmlns + "sd", Sd),
                new XAttribute(XNamespace.Xmlns + "ep", Ep),
                new XAttribute(XNamespace.Xmlns + "c", C),
                RequestContext(companyName, now, _einstellungen),
                new XElement(Sdc + "Job",
                    new XElement(Sdc + "Addressees", jobAddressees),
                    // Übungs-/Testmeldung — nie als Produktivmeldung werten:
                    new XElement(Sdc + "TestCase")),
                new XElement(Sd + "AnnualSalaryDeclaration",
                    new XAttribute("schemaVersion", "0.0"),
                    companyDescription,
                    new XElement(Sd + "Staff", persons),
                    new XElement(Sd + "Institutions", institutions),
                    new XElement(Sd + "SalaryTotals", salaryTotals),
                    new XElement(Sd + "SalaryCounters", counters),
                    new XElement(Sd + "GeneralSalaryDeclarationDescription",
                        new XElement(Sd + "CreationDate", now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
                        new XElement(Sd + "AccountingPeriod", year)))));

        var xml = doc.Declaration + Environment.NewLine + doc.ToString();
        var xsdFehler = _validator.Validate(xml);

        return new BuildResult(xml, persons.Count, skipped, totalAhv, totalAlv, warn, xsdFehler);
    }

    /// <summary>
    /// Sonderfall und das Kalenderjahr vor dem 18. Geburtstag: nicht AHV-/ALV-pflichtig.
    /// Verzicht nur nach Referenzalter (Folgemonat). Sonst der normale Lohn.
    /// </summary>
    private static ElmAhvJahreslohn.Art ArtDesMonats(
        Employee e, int year, int month,
        IReadOnlyList<EmployeeVersicherungCode>? codes, AhvBeitragspflichtStand stand)
    {
        var erster = new DateOnly(year, month, 1);
        var letzter = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        bool gilt(EmployeeVersicherungCode c) =>
            c.ValidFrom <= letzter && (c.ValidTo == null || c.ValidTo >= erster);

        if (codes != null && codes.Any(c => c.IstAhvSonderfall && gilt(c)))
            return ElmAhvJahreslohn.Art.Frei;
        var geb = e.DateOfBirth!.Value;
        if (year <= geb.Year + 17)
            return ElmAhvJahreslohn.Art.Frei;
        if (PayrollCalculations.HatReferenzalterErreicht(e.Gender, geb, year, month, stand)
            && codes != null && codes.Any(c => c.IstAhvFreibetragVerzicht && gilt(c)))
            return ElmAhvJahreslohn.Art.Verzicht;
        return ElmAhvJahreslohn.Art.Normal;
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
