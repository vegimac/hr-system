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

    /// <summary>
    /// Empfänger einer Art: zuerst einer, der am Stichtag zugeordnet ist, sonst wie bisher der erste.
    /// Muster AG: Katalog mit Swica (S122) ohne Zuordnung, zugeordnet ist Backwork (S1000);
    /// AHV 2024 Spida (bis 31.12.2024), ab 2025 Ausgleichskasse Luzern.
    /// </summary>
    public static LohndatenEmpfaenger? WaehleEmpfaenger(IEnumerable<LohndatenEmpfaenger> kandidaten, DateOnly stichtag, IReadOnlySet<int>? nurFilialen)
    {
        var liste = kandidaten.ToList();
        return liste.FirstOrDefault(e => e.Zuordnungen.Any(z => z.GiltAm(stichtag)
                   && (nurFilialen == null || nurFilialen.Contains(z.CompanyProfileId))))
               ?? liste.FirstOrDefault();
    }

    public Task<BuildResult> BuildAhvAsync(int year, CancellationToken ct = default)
        => BuildAhvAsync(year, null, ct);

    /// <summary>Mit nurFiliale nur die Lohnzettel und Arbeitsorte dieser Filiale. Ohne: alle Filialen der Rechtseinheit.</summary>
    /// <param name="bisMonat">Meldung bis und mit diesem Monat (Lohntest YearlyRetrospective: je Monat kumuliert,
    /// Referenz 2025-04 = Januar–April). Null = ganzes Jahr.</param>
    public async Task<BuildResult> BuildAhvAsync(int year, int? nurFiliale, CancellationToken ct = default, int? bisMonat = null)
    {
        int letzterMonat = bisMonat is >= 1 and <= 12 ? bisMonat.Value : 12;
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
        static bool IstZusatz(Models.LohndatenEmpfaenger e) =>
            (e.Bezeichnung + " " + (e.Zusatz ?? "")).ToLowerInvariant().Contains("zusatz");
        // Stichtag für die Zuordnung = 31.12. des Lohnjahres (Gültig ab/bis, Walter 07.09.2026)
        var stichtagZuord = new DateOnly(year, letzterMonat, DateTime.DaysInMonth(year, letzterMonat));
        bool GehoertHier(Models.LohndatenEmpfaenger e) =>
            ganzeFirma
            || e.Zuordnungen.Any(z => filialIds.Contains(z.CompanyProfileId) && z.GiltAm(stichtagZuord));
        // Zugeordnet am Stichtag geht vor: Der Katalog führt auch Einträge, die der Firma nicht gehören.
        Models.LohndatenEmpfaenger? Waehle(Func<Models.LohndatenEmpfaenger, bool> art)
            => WaehleEmpfaenger(empf.Where(art), stichtagZuord, ganzeFirma ? null : filialIds);
        var akE = Waehle(e => e.Art == "AUSGLEICHSKASSE");
        var uvgE = Waehle(e => e.Art == "UVG" && !IstZusatz(e) && GehoertHier(e));
        var bvgE = Waehle(e => e.Art == "BVG" && GehoertHier(e));
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
                   where p.Year == year && p.Month <= letzterMonat && s.Status != "STORNIERT"
                   select new { s.EmployeeId, s.SvBasisAhv, s.SvBasisNbuv, s.Brutto, p.Month, PeriodeStatus = p.Status, p.CompanyProfileId, s.SlipJson };
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
        var yearEnd = new DateTime(year, letzterMonat, DateTime.DaysInMonth(year, letzterMonat));
        var stichtagEnde = DateOnly.FromDateTime(yearEnd);

        // Personenangaben am Ende der Meldeperiode, wie in der Monatsmeldung (Quality Tool 2024-12: TF07 Burri).
        var modelle = stamm.Hauptsitz == null
            ? new List<Arbeitszeitmodell>()
            : await _db.Arbeitszeitmodelle.AsNoTracking()
                .Where(m => m.HauptsitzId == stamm.Hauptsitz.Id && m.IsActive)
                .OrderBy(m => m.Id).ToListAsync(ct);
        var zuordnungen = modelle.Count == 0
            ? new List<EmployeeArbeitszeitmodell>()
            : await _db.EmployeeArbeitszeitmodelle.AsNoTracking()
                .Where(z => empIds.Contains(z.EmployeeId)).ToListAsync(ct);
        var bewilligungen = await _db.EmployeePermitHistories.AsNoTracking()
            .Where(h => empIds.Contains(h.EmployeeId)).ToListAsync(ct);
        var permitTypen = await _db.PermitTypes.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var zivilHist = await _db.EmployeeZivilstandHistories.AsNoTracking()
            .Where(h => empIds.Contains(h.EmployeeId)).ToListAsync(ct);
        var wohnHist = await _db.EmployeeWohnortHistories.AsNoTracking()
            .Where(h => empIds.Contains(h.EmployeeId)).ToListAsync(ct);
        var plzListe = emps.Select(e2 => (e2.ZipCode ?? "").Trim())
            .Concat(wohnHist.Select(h => (h.Plz ?? "").Trim()))
            .Where(z => z.Length == 4).Distinct().ToList();
        var orte = await _db.SwissLocations.AsNoTracking()
            .Where(l => plzListe.Contains(l.Plz4))
            .Select(l => new { l.Plz4, l.BfsNr, l.Ortschaftsname, l.Gemeindename })
            .ToListAsync(ct);
        int? WohnGemeinde(string? plzRoh, string? ortRoh)
        {
            var plz = (plzRoh ?? "").Trim();
            if (plz.Length != 4) return null;
            var ort = (ortRoh ?? "").Trim().ToLowerInvariant();
            var kand = orte.Where(o => o.Plz4 == plz).ToList();
            var best = kand.FirstOrDefault(o => (o.Ortschaftsname ?? "").ToLowerInvariant().StartsWith(ort)
                                             || (o.Gemeindename ?? "").ToLowerInvariant() == ort)?.BfsNr;
            if (best is > 0) return best;
            var nrs = kand.Select(o => o.BfsNr).Distinct().ToList();
            return nrs.Count == 1 ? nrs[0] : null;
        }
        var versicherungAlle = await _db.EmployeeVersicherungCodes.AsNoTracking()
            .Where(v => empIds.Contains(v.EmployeeId))
            .ToListAsync(ct);
        var codesByEmp = versicherungAlle.Where(v => v.Art == EmployeeVersicherungCode.ArtAhv)
            .GroupBy(v => v.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EmployeeVersicherungCode>)g.ToList());
        var versCodesByEmp = versicherungAlle.Where(v => v.Art is "UVG" or "UVGZ" or "KTG")
            .GroupBy(v => v.EmployeeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // UVG / UVGZ / KTG (Walter 02.11.2026): Versicherer aus dem Empfänger-Katalog, Codes je Monat
        // aus den Versicherungs-Codes der Person bzw. der Standard-Lösung der SV-Sätze.
        var svSaetze = await _db.SocialInsuranceRates.AsNoTracking()
            .Where(r => r.IsActive && (r.Code == "NBUV" || r.Code == "BUV" || r.Code == "UVGZ" || r.Code == "KTG"))
            .ToListAsync(ct);
        var uvgHoechstMonat = svSaetze.Where(r => r.Code is "NBUV" or "BUV" && r.MaxBaseMonthly is > 0)
            .Select(r => r.MaxBaseMonthly!.Value).DefaultIfEmpty(AlvMonatsCap).Max();
        var uvgzE = Waehle(e => (e.Art == "UVGZ" || (e.Art == "UVG" && IstZusatz(e))) && GehoertHier(e));
        var ktgE = Waehle(e => e.Art == "KTG" && GehoertHier(e));
        var vers = new Dictionary<string, Versicherer>();
        foreach (var (art, e, id) in new[] { ("UVG", uvgE, "#uvg"), ("UVGZ", uvgzE, "#uvgz"), ("KTG", ktgE, "#ktg") })
        {
            if (e == null) continue;
            var kasse = (e.Kassennummer ?? "").Trim();
            if (kasse.Length == 0)
            {
                warn.Add($"{art}-Versicherer «{e.Bezeichnung}»: Kassennummer fehlt (Empfänger-Katalog) — {art} nicht gemeldet.");
                continue;
            }
            var zu = e.Zuordnungen.Where(z => z.GiltAm(stichtagZuord) && (ganzeFirma || filialIds.Contains(z.CompanyProfileId))).ToList();
            string? Eindeutig(Func<Models.CompanyProfileEmpfaenger, string?> feld)
            {
                var w = zu.Select(z => (feld(z) ?? "").Trim()).Where(v => v.Length > 0).Distinct().ToList();
                return w.Count == 1 ? w[0] : null;
            }
            var kunde = Eindeutig(z => z.Mitgliednummer);
            var vertrag = Eindeutig(z => z.Subnummer);
            if (kunde == null || vertrag == null)
                warn.Add($"{art}-Versicherer «{e.Bezeichnung}»: Kunden- oder Vertragsnummer fehlt bzw. ist pro Filiale verschieden "
                       + "(Filiale → Lohndaten Empfänger: Mitgliednummer = Kunde, Subnummer = Vertrag) — im XML steht die Kassennummer.");
            vers[art] = new Versicherer(id, kasse, e.Bezeichnung.Trim(), kunde ?? kasse, vertrag ?? kasse);
        }
        var versZeilen = new Dictionary<string, List<ElmVersicherungJahreslohn.Zeile>>
        {
            ["UVG"] = new(), ["UVGZ"] = new(), ["KTG"] = new(),
        };
        var versTags = new Dictionary<string, int> { ["UVG"] = 0, ["UVGZ"] = 0, ["KTG"] = 0 };
        var versOhneVersicherer = new HashSet<string>();
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
        var lpByCode = (await _db.Lohnpositionen.AsNoTracking().ToListAsync(ct))
            .GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.First());
        var taxKantone = new SortedSet<string>(StringComparer.Ordinal);
        int taxTags = 0;
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

            var eigeneWohnorte = wohnHist.Where(h => h.EmployeeId == e.Id).ToList();
            var wohnAm = WohnortHistorieService.Am(eigeneWohnorte, stichtagEnde,
                e.Street, e.ZipCode, e.City, e.CantonCode, e.Country);
            var adresseAm = new PersonAdresseAm(wohnAm.Strasse, wohnAm.Plz, wohnAm.Ort, wohnAm.Land, wohnAm.KantonCode);
            var umzug = WohnortHistorieService.UmzugImMonat(eigeneWohnorte, stichtagEnde.AddYears(-1).AddDays(1), stichtagEnde);
            PersonAdresseAm? adresseVorher = umzug is { } u
                ? new PersonAdresseAm(u.Alt.Strasse, u.Alt.Plz, u.Alt.Ort, u.Alt.Land, u.Alt.KantonCode)
                : null;
            var (zivAm, zivSeitAm, _) = ZivilstandHistorieService.Am(
                zivilHist.Where(h => h.EmployeeId == e.Id), stichtagEnde, e.MaritalStatus, e.MaritalStatusSince);
            var permitId = ElmMonthlyDeclarationBuilder.BewilligungAmStichtag(bewilligungen.Where(h => h.EmployeeId == e.Id), stichtagEnde);
            var permitAmEnde = permitId != null && permitTypen.TryGetValue(permitId.Value, out var pt) ? pt : null;
            var particulars = Particulars(e, warn, WohnGemeinde(adresseAm.Zip, adresseAm.City), permitAmEnde,
                zivAm, zivSeitAm, adresseAm, adresseVorher, umzug?.Wegzug,
                adresseVorher == null ? null : WohnGemeinde(adresseVorher.Zip, adresseVorher.City));

            var zuordnung = zuordnungen
                .Where(z => z.EmployeeId == e.Id && z.GueltigAb <= stichtagEnde)
                .OrderByDescending(z => z.GueltigAb).ThenByDescending(z => z.Id).FirstOrDefault();
            var modell = (zuordnung != null ? modelle.FirstOrDefault(m => m.Id == zuordnung.ArbeitszeitmodellId) : null)
                         ?? modelle.FirstOrDefault();
            var workingTime = WorkingTime(em,
                modell?.Wochenstunden is > 0m ? modell.Wochenstunden!.Value : main.NormalWeeklyHours ?? 42m,
                zuordnung?.AbweichendeWochenstunden, zuordnung?.AbweichendeWochenlektionen, modell?.Wochenlektionen);

            var entry = e.EntryDate ?? em?.ContractStartDate ?? yearStart;
            var work = new XElement(C + "Work",
                new XAttribute("workID", $"#w{e.Id}"),
                new XAttribute("companyWorkingTimeIDRef", "#" + (modell?.KennungOderId ?? "cwt1")),
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
            persons.Add(new XElement(Sd + "Person", particulars, work, salaries, fakSalaries,
                VersicherungSalaries(e, x.EmployeeId, Zeit),
                TaxSalaries(rowsByEmp[x.EmployeeId].OrderBy(r => r.Month).Select(r => r.SlipJson), adresseAm,
                    Zeit(rowsByEmp[x.EmployeeId].Min(r => r.Month), rowsByEmp[x.EmployeeId].Max(r => r.Month)))));
        }

        // Lohnausweis einer Person (Richtlinien Kapitel 9): adressiert an den Wohnkanton, im Ausland EX.
        XElement TaxSalaries(IEnumerable<string?> lohnzettel, PersonAdresseAm adresse, (DateTime Von, DateTime Bis) periode)
        {
            var a = ElmLohnausweisJahr.Rechne(lohnzettel, c => lpByCode.TryGetValue(c, out var l) ? (l.LohnausweisCode, l.Bezeichnung) : (null, null));
            var land = (adresse.Country ?? "").Trim().ToUpperInvariant();
            var kanton = (adresse.Canton ?? "").Trim().ToUpperInvariant();
            if (land is not ("" or "CH") || kanton.Length != 2) kanton = "EX";
            taxKantone.Add(kanton);
            taxTags++;
            XElement Betrag(string name, decimal v) => new(Sd + name, Amt(PayrollCalculations.Round05(v)));
            XElement? WennBetrag(string name, decimal v) => v != 0m ? Betrag(name, v) : null;
            XElement? MitText(string name, ElmLohnausweisJahr.Feld f) => a.Von(f) != 0m
                ? new XElement(Sd + name, new XElement(Sd + "Text", a.TextVon(f)), Betrag("Sum", a.Von(f))) : null;
            var v = ElmLohnausweisJahr.Feld.Verpflegung;
            var w = ElmLohnausweisJahr.Feld.Geschaeftswagen;
            var n = ElmLohnausweisJahr.Feld.Nebenleistung;
            XElement? fringe = a.Von(v) != 0m || a.Von(w) != 0m || a.Von(n) != 0m
                ? new XElement(Sd + "FringeBenefits", WennBetrag("FoodLodging", a.Von(v)), WennBetrag("CompanyCar", a.Von(w)), MitText("Other", n))
                : null;
            var pw = a.Von(ElmLohnausweisJahr.Feld.SpesenPauschalWagen);
            var pu = ElmLohnausweisJahr.Feld.SpesenPauschalUebrige;
            var ef = a.Von(ElmLohnausweisJahr.Feld.SpesenEffektiv);
            var wb = a.Von(ElmLohnausweisJahr.Feld.Weiterbildung);
            XElement? spesen = ef != 0m || pw != 0m || a.Von(pu) != 0m || wb != 0m
                ? new XElement(Sd + "Charges",
                    ef != 0m ? new XElement(Sd + "Effective", Betrag("TravelFoodAccommodation", ef)) : null,
                    pw != 0m || a.Von(pu) != 0m ? new XElement(Sd + "LumpSum", WennBetrag("Car", pw), MitText("Other", pu)) : null,
                    WennBetrag("Education", wb))
                : null;
            return new XElement(Sd + "TaxSalaries", new XElement(Sd + "TaxSalary",
                new XAttribute("addresseeIDRef", "#TAX-" + kanton),
                new XElement(Sd + "DocID", Guid.NewGuid().ToString()),
                new XElement(Sd + "CreationDate", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff")),
                new XElement(Sd + "Period", new XElement(Ep + "from", periode.Von.ToString("yyyy-MM-dd")), new XElement(Ep + "until", periode.Bis.ToString("yyyy-MM-dd"))),
                WennBetrag("Income", a.Von(ElmLohnausweisJahr.Feld.Lohn)),
                fringe,
                MitText("SporadicBenefits", ElmLohnausweisJahr.Feld.Unregelmaessig),
                MitText("CapitalPayment", ElmLohnausweisJahr.Feld.Kapital),
                WennBetrag("OwnershipRight", a.Von(ElmLohnausweisJahr.Feld.Beteiligung)),
                WennBetrag("BoardOfDirectorsRemuneration", a.Von(ElmLohnausweisJahr.Feld.Verwaltungsrat)),
                MitText("OtherBenefits", ElmLohnausweisJahr.Feld.Uebrige),
                Betrag("GrossIncome", a.Brutto),
                WennBetrag("AHV-ALV-NBUV-AVS-AC-AANP-Contribution", a.Beitraege),
                a.Bvg != 0m ? new XElement(Sd + "BVG-LPP-Contribution", Betrag("Regular", a.Bvg)) : null,
                Betrag("NetIncome", a.Netto),
                WennBetrag("DeductionAtSource", a.Quellensteuer),
                spesen));
        }

        // UVG-, UVGZ- und KTG-Salaries einer Person: je Beitragsdauer und Code (ElmVersicherungJahreslohn).
        IEnumerable<XElement> VersicherungSalaries(Employee e, int empId, Func<int, int, (DateTime Von, DateTime Bis)> zeit)
        {
            var eintraege = versCodesByEmp.TryGetValue(empId, out var l) ? l : new List<EmployeeVersicherungCode>();
            var geschlecht = (e.Gender ?? "").ToLowerInvariant() is var g && (g.StartsWith("f") || g.StartsWith("w")) ? "F" : "M";
            var monateJeArt = new Dictionary<string, List<ElmVersicherungJahreslohn.Monat>>
            {
                ["UVG"] = new(), ["UVGZ"] = new(), ["KTG"] = new(),
            };
            var hinweise = new HashSet<string>();
            foreach (var mg in rowsByEmp[empId].GroupBy(r => r.Month).OrderBy(mg => mg.Key))
            {
                var filiale = mg.OrderByDescending(r => r.SvBasisAhv).First().CompanyProfileId;
                var stichtag = new DateOnly(year, mg.Key, DateTime.DaysInMonth(year, mg.Key));
                var saetze = svSaetze.Where(r => r.CompanyProfileId == null || r.CompanyProfileId == filiale).ToList();
                var zeilen = mg.SelectMany(r => VersicherungsZeilen(r.SlipJson)).ToList();
                decimal brutto = mg.Sum(r => r.Brutto), basis = mg.Sum(r => r.SvBasisNbuv), ahv = mg.Sum(r => r.SvBasisAhv);
                decimal? Summe(Func<(string Kat, string? Code, decimal Basis), bool> f)
                    => zeilen.Any(f) ? zeilen.Where(f).Sum(z => z.Basis) : null;

                // UVG: ein Code je Monat (Betriebsteil + Ziffer 0–3).
                var uvg = PayrollCalculations.EffektiverCode("UVG", eintraege, saetze, stichtag)?.Trim().ToUpperInvariant();
                var nbu = Summe(z => z.Kat == "NBUV");
                if (uvg is { Length: 1 })
                {
                    // Nur der Betriebsteil der Standard-Lösung: Ziffer aus dem Lohnzettel (NBU-Abzug ja/nein).
                    uvg += nbu != null ? "1" : "2";
                    hinweise.Add($"UVG-Code ohne Ziffer — als {uvg} gemeldet (Versicherungs-Codes erfassen).");
                }
                if (string.IsNullOrEmpty(uvg))
                    hinweise.Add($"kein UVG-Code in Monat {mg.Key} — UVG nicht gemeldet.");
                else
                    monateJeArt["UVG"].Add(new(mg.Key, uvg, brutto, basis,
                        ElmVersicherungJahreslohn.UvgVersichert(uvg) ? nbu ?? Summe(z => z.Kat == "BUV") : 0m, ahv));

                // UVGZ und KTG: ein oder mehrere Codes gleichzeitig.
                foreach (var art in new[] { "UVGZ", "KTG" })
                {
                    var codes = eintraege.Where(c => c.Art == art && c.GiltAm(stichtag) && !string.IsNullOrWhiteSpace(c.Code))
                        .Select(c => c.Code!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    if (codes.Count == 0 && PayrollCalculations.EffektiverCode(art, eintraege, saetze, stichtag) is string std)
                        codes.Add(std.Trim());
                    var artZeilen = zeilen.Where(z => z.Kat == art).ToList();
                    if (codes.Count == 0)
                    {
                        if (artZeilen.Count > 0) hinweise.Add($"{art}-Abzug ohne Code in Monat {mg.Key} — {art} nicht gemeldet.");
                        continue;
                    }
                    var ohneCode = artZeilen.Any(z => z.Code == null);
                    if (ohneCode && codes.Count > 1)
                        hinweise.Add($"{art} mit {codes.Count} Codes in Monat {mg.Key}: Lohnzettel ohne Code-Zuordnung — Lohnzettel neu berechnen.");
                    foreach (var code in codes)
                    {
                        var pflichtig = artZeilen.Where(z => string.Equals(z.Code, code, StringComparison.OrdinalIgnoreCase)
                                                             || (z.Code == null && codes.Count == 1)).Sum(z => z.Basis);
                        monateJeArt[art].Add(new(mg.Key, code, 0m, basis, pflichtig, ahv));
                    }
                }
            }
            foreach (var h in hinweise)
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): {h}");

            (DateTime Von, DateTime Bis) Dauer(string art, ElmVersicherungJahreslohn.Abschnitt a)
            {
                var (von, bis) = zeit(a.Von, a.Bis);
                // Codewechsel mitten im Monat: die Beitragsdauer beginnt am Gültig-ab des Codes.
                var ab = eintraege.Where(c => c.Art == art && string.Equals((c.Code ?? "").Trim(), a.Code, StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.ValidFrom.ToDateTime(TimeOnly.MinValue))
                    .Where(d => d.Year == year && d.Month == a.Von && d > von && d <= bis)
                    .DefaultIfEmpty(von).Max();
                return (ab, bis);
            }
            XElement Zeitraum((DateTime Von, DateTime Bis) d) => new(Sd + "AccountingTime",
                new XElement(Ep + "from", d.Von.ToString("yyyy-MM-dd")),
                new XElement(Ep + "until", d.Bis.ToString("yyyy-MM-dd")));

            var result = new List<XElement>();
            foreach (var (art, liste, element) in new[]
                     {
                         ("UVG", "UVG-LAA-Salaries", "UVG-LAA-Salary"),
                         ("UVGZ", "UVGZ-LAAC-Salaries", "UVGZ-LAAC-Salary"),
                         ("KTG", "KTG-AMC-Salaries", "KTG-AMC-Salary"),
                     })
            {
                var abschnitte = ElmVersicherungJahreslohn.Abschnitte(monateJeArt[art]);
                if (abschnitte.Count == 0) continue;
                if (!vers.TryGetValue(art, out var v)) { versOhneVersicherer.Add(art); continue; }
                var el = new XElement(Sd + liste);
                foreach (var a in abschnitte)
                {
                    var dauer = Dauer(art, a);
                    var pflichtig = a.Beitragspflichtig
                        ?? Math.Min(a.Basis, ElmVersicherungJahreslohn.Hoechstlohn(dauer.Von, dauer.Bis, uvgHoechstMonat));
                    var s = new XElement(Sd + element, new XAttribute("addresseeIDRef", v.Id), Zeitraum(dauer));
                    switch (art)
                    {
                        case "UVG":
                            s.Add(new XElement(Sd + "UVG-LAA-Code", a.Code),
                                new XElement(Sd + "UVG-LAA-GrossSalary", Betrag05(a.Brutto)),
                                new XElement(Sd + "UVG-LAA-BaseSalary", Betrag05(a.Basis)),
                                new XElement(Sd + "UVG-LAA-ContributorySalary", Betrag05(pflichtig)));
                            break;
                        case "UVGZ":
                            s.Add(new XElement(Sd + "UVGZ-LAAC-Code", a.Code),
                                new XElement(Sd + "UVGZ-LAAC-BaseSalary", Betrag05(a.Basis)),
                                new XElement(Sd + "UVGZ-LAAC-ContributorySalary", Betrag05(pflichtig)));
                            break;
                        default:
                            s.Add(new XElement(Sd + "KTG-AMC-Code", a.Code),
                                new XElement(Sd + "Reference-AHV-AVS-Salary", Betrag05(a.AhvLohn)),
                                new XElement(Sd + "KTG-AMC-ContributorySalary", Betrag05(pflichtig)));
                            break;
                    }
                    el.Add(s);
                    versTags[art]++;
                    // Totale = Summe der gemeldeten (gerundeten) Einzelwerte.
                    versZeilen[art].Add(new(a.Code, geschlecht, PayrollCalculations.Round05(pflichtig)));
                }
                result.Add(el);
            }
            return result;
        }

        // Abzugszeilen NBUV/BUV/UVGZ/KTG eines Lohnzettels mit Basis und Versicherungs-Code (falls gespeichert).
        static IEnumerable<(string Kat, string? Code, decimal Basis)> VersicherungsZeilen(string? slipJson)
        {
            var liste = new List<(string, string?, decimal)>();
            if (string.IsNullOrWhiteSpace(slipJson)) return liste;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(slipJson);
                if (!doc.RootElement.TryGetProperty("abzugLines", out var al) || al.ValueKind != System.Text.Json.JsonValueKind.Array)
                    return liste;
                foreach (var z in al.EnumerateArray())
                {
                    var kat = (z.TryGetProperty("categoryCode", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String
                        ? c.GetString() : null)?.Trim().ToUpperInvariant();
                    if (kat is not ("NBUV" or "BUV" or "UVGZ" or "KTG")) continue;
                    var code = z.TryGetProperty("loesungsCode", out var lc) && lc.ValueKind == System.Text.Json.JsonValueKind.String
                        ? lc.GetString()?.Trim() : null;
                    var basis = z.TryGetProperty("basis", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.Number ? b.GetDecimal() : 0m;
                    liste.Add((kat, string.IsNullOrEmpty(code) ? null : code, basis));
                }
            }
            catch (System.Text.Json.JsonException) { /* unlesbarer Lohnzettel: keine Zeilen */ }
            return liste;
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
        var companyDescription = CompanyDescription(stamm,
            ElmMonthlyDeclarationBuilder.ArbeitszeitModelle(modelle, zuordnungen, empIds, main.NormalWeeklyHours ?? 42m, warn));

        var jobAddressees = new List<XElement>
        {
            new(Sdc + "Addressee",
                new XAttribute("addresseeID", "#ahv"),
                new XElement(Ep + "AddresseeIdentification", akKasse),
                new XElement(Ep + "ProcessByDistributor", "true"))
        };
        jobAddressees.AddRange(faks.Select(ElmFakAdressierung.JobAddressee));
        foreach (var art in versOhneVersicherer)
            warn.Add($"{art}: Personen mit {art}-Code, aber kein {art}-Versicherer im Empfänger-Katalog — {art} nicht gemeldet.");
        // Nur Versicherer melden, für die es Personen gibt (wie die Referenz).
        var versGemeldet = new[] { "UVG", "UVGZ", "KTG" }.Where(a => vers.ContainsKey(a) && versTags[a] > 0).ToList();
        jobAddressees.AddRange(taxKantone.Select(k => new XElement(Sdc + "Addressee",
            new XAttribute("addresseeID", "#TAX-" + k),
            new XElement(Ep + "AddresseeIdentification", k),
            new XElement(Ep + "ProcessByDistributor", "true"))));
        jobAddressees.AddRange(versGemeldet.Select(a => new XElement(Sdc + "Addressee",
            new XAttribute("addresseeID", vers[a].Id),
            new XElement(Ep + "AddresseeIdentification", vers[a].Kassennummer),
            new XElement(Ep + "ProcessByDistributor", "true"))));

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
        institutions.AddRange(versGemeldet.Select(a => new XElement(Sd + VersElement[a],
            new XAttribute("addresseeIDRef", vers[a].Id),
            new XElement(Sd + "InsuranceCompanyName", vers[a].Name),
            new XElement(Sd + "CustomerIdentity", vers[a].Kunde),
            new XElement(Sd + "ContractIdentity", vers[a].Vertrag))));
        institutions.AddRange(taxKantone.Select(k => new XElement(Sd + "Tax", new XAttribute("addresseeIDRef", "#TAX-" + k))));

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

        // UVG-Totale je Betriebsteil und Geschlecht, UVGZ/KTG je Code; Codes der SV-Sätze ohne Personen mit 0.00.
        IEnumerable<string> SatzCodes(params string[] svCodes) => svSaetze
            .Where(r => svCodes.Contains(r.Code) && !string.IsNullOrWhiteSpace(r.LoesungsCode)
                        && (ganzeFirma || r.CompanyProfileId == null || filialIds.Contains(r.CompanyProfileId.Value)))
            .Select(r => r.LoesungsCode!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
        if (versGemeldet.Contains("UVG"))
        {
            var branchen = ElmVersicherungJahreslohn.UvgTotale(versZeilen["UVG"], SatzCodes("NBUV", "BUV"));
            salaryTotals.Add(new XElement(Sd + "UVG-LAA-Totals",
                new XAttribute("addresseeIDRef", vers["UVG"].Id),
                new XElement(Sd + "UVG-LAA-BranchTotals", branchen.Select(b => new XElement(Sd + "UVG-LAA-BranchTotal",
                    new XElement(Sd + "BranchIdentifier", b.Betriebsteil),
                    new XElement(Sd + "Female-Totals",
                        new XElement(Sd + "NBU-BU-ANP-AP-Total", Amt(b.FrauNbu)),
                        new XElement(Sd + "BU-AP-Total", Amt(b.FrauBu))),
                    new XElement(Sd + "Male-Totals",
                        new XElement(Sd + "NBU-BU-ANP-AP-Total", Amt(b.MannNbu)),
                        new XElement(Sd + "BU-AP-Total", Amt(b.MannBu)))))),
                new XElement(Sd + "UVG-LAA-MasterTotal",
                    Amt(branchen.Sum(b => b.FrauNbu + b.FrauBu + b.MannNbu + b.MannBu)))));
        }
        foreach (var (art, prefix) in new[] { ("UVGZ", "UVGZ-LAAC"), ("KTG", "KTG-AMC") })
        {
            if (!versGemeldet.Contains(art)) continue;
            var kat = ElmVersicherungJahreslohn.KategorieTotale(versZeilen[art], SatzCodes(art));
            salaryTotals.Add(new XElement(Sd + $"{prefix}-Totals",
                new XAttribute("addresseeIDRef", vers[art].Id),
                new XElement(Sd + $"{prefix}-CategoryTotals", kat.Select(k => new XElement(Sd + $"{prefix}-CategoryTotal",
                    new XElement(Sd + "CategoryCode", k.Code),
                    new XElement(Sd + "Female-Total", Amt(k.Frau)),
                    new XElement(Sd + "Male-Total", Amt(k.Mann))))),
                new XElement(Sd + $"{prefix}-MasterTotal", Amt(kat.Sum(k => k.Frau + k.Mann)))));
        }

        var counters = new List<XElement>
        {
            new(Sd + "NumberOf-AHV-AVS-Salary-Tags", salaryTags)
        };
        if (faks.Count > 0)
            counters.Add(new XElement(Sd + "NumberOf-FAK-CAF-Salary-Tags", fakTags));
        foreach (var a in versGemeldet)
            counters.Add(new XElement(Sd + $"NumberOf-{VersElement[a]}-Salary-Tags", versTags[a]));
        if (taxTags > 0) counters.Add(new XElement(Sd + "NumberOf-TaxSalary-Tags", taxTags));

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

    /// <summary>UVG-/UVGZ-/KTG-Versicherer der Meldung (Empfänger-Katalog).</summary>
    private sealed record Versicherer(string Id, string Kassennummer, string Name, string Kunde, string Vertrag);

    /// <summary>Elementname der Domäne im XML (Institution, Zähler).</summary>
    private static readonly Dictionary<string, string> VersElement = new()
    {
        ["UVG"] = "UVG-LAA", ["UVGZ"] = "UVGZ-LAAC", ["KTG"] = "KTG-AMC",
    };

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
