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
        // Basen aus dem Lohnzettel: die Spalten des Snapshots können nach einer Nachberechnung abweichen (Referenz 2025-01: TF01 Herz).
        var rows = (await lohn.ToListAsync(ct)).Select(r => new
        {
            r.EmployeeId, r.Brutto, r.Month, r.PeriodeStatus, r.CompanyProfileId, r.SlipJson,
            SvBasisAhv = ElmAhvJahreslohn.BasisAusLohnzettel(r.SlipJson, "svBasisAhv") ?? r.SvBasisAhv,
            SvBasisNbuv = ElmAhvJahreslohn.BasisAusLohnzettel(r.SlipJson, "svBasisNbuv") ?? r.SvBasisNbuv,
        }).ToList();
        if (rows.Count == 0)
            return new BuildResult("", 0, 0, 0, 0,
                new List<string> { nurFiliale is int and > 0
                    ? $"Keine Lohnabrechnungen für {year} bei dieser Filiale gefunden."
                    : $"Keine Lohnabrechnungen für {year} gefunden." }, new List<string>());

        var offenePerioden = rows.Where(r => r.PeriodeStatus != "abgeschlossen")
            .Select(r => r.Month).Distinct().OrderBy(m => m).ToList();
        if (offenePerioden.Count > 0)
            warn.Add($"Nicht definitiv abgeschlossene Monate im XML enthalten: {string.Join(", ", offenePerioden)} — für Übungszwecke ok, für eine echte Meldung müssen alle Monate abgeschlossen sein.");

        // Lohnzettel ohne jeden Betrag gehören nicht in die Meldung (Referenz 2025-02: TF15 Degelo, TF39 Hasler).
        var perEmp = rows.GroupBy(r => r.EmployeeId)
            .Where(g => g.Any(r => r.Brutto != 0m || r.SvBasisAhv != 0m))
            .Select(g => new { EmployeeId = g.Key, Ahv = g.Sum(r => r.SvBasisAhv) })
            .OrderBy(x => x.EmployeeId).ToList();
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
        var kasseCache = new Dictionary<(int Filiale, DateOnly Am), ElmFakAdressierung.Eintrag?>();
        ElmFakAdressierung.Eintrag? KasseIm(int filiale, int monat, DateOnly? am = null)
        {
            var stichtag = am ?? new DateOnly(year, monat, DateTime.DaysInMonth(year, monat));
            if (!kasseCache.TryGetValue((filiale, stichtag), out var k))
            {
                k = ElmFakAdressierung.FuerFiliale(empf, filiale, stichtag, workplaceIds, id => kantonByFiliale.GetValueOrDefault(id));
                kasseCache[(filiale, stichtag)] = k;
            }
            return k;
        }
        var fakGenutzt = new Dictionary<string, ElmFakAdressierung.Eintrag>(StringComparer.OrdinalIgnoreCase);
        var fakSummen = new Dictionary<string, Dictionary<string, ElmFakAdressierung.Summe>>(StringComparer.OrdinalIgnoreCase);
        var fakTags = 0;

        // ── Personen ──────────────────────────────────────────────────────
        var lpByCode = (await _db.Lohnpositionen.AsNoTracking().ToListAsync(ct))
            .GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.First());
        var personenLa = await _db.EmployeeLohnausweise.AsNoTracking().Where(p => empIds.Contains(p.EmployeeId)).ToListAsync(ct);
        var fassungenLa = await _db.CompanyLohnausweise.AsNoTracking().Where(f => filialIds.Contains(f.CompanyProfileId)).ToListAsync(ct);
        var qstZeiten = await _db.EmployeeQuellensteuer.AsNoTracking().Where(q => empIds.Contains(q.EmployeeId)).ToListAsync(ct);
        var taxKantone = new SortedSet<string>(StringComparer.Ordinal);
        var grenzSteuerbar = new SortedDictionary<string, decimal>(StringComparer.Ordinal);
        var grenzTags = 0;
        int taxTags = 0;
        var persons = new List<XElement>();
        decimal totalAhv = 0, totalAlv = 0, totalAlvz = 0, totalAhvFrei = 0, totalAlvFrei = 0;
        // Ausgleichskasse je Abschnitt: Lohn nach dem Austritt geht an die Kasse von damals (Referenz 2025-01: TF07 Burri).
        var ahvKassen = new List<AhvKasseTotal> { new("#ahv", akKasse, akAbrechnung) };
        AhvKasseTotal AhvKasseAm(DateOnly am)
        {
            var e = WaehleEmpfaenger(empf.Where(x => x.Art == "AUSGLEICHSKASSE"), am, ganzeFirma ? null : filialIds);
            var nummer = (e?.Kassennummer ?? "").Trim();
            if (nummer.Length == 0) return ahvKassen[0];
            var da = ahvKassen.FirstOrDefault(k => k.Kasse == nummer);
            if (da != null) return da;
            var mitglied = e!.Zuordnungen.Where(z => z.GiltAm(am) && (ganzeFirma || filialIds.Contains(z.CompanyProfileId)))
                .Select(z => (z.Mitgliednummer ?? "").Trim()).FirstOrDefault(v => v.Length > 0);
            var neu = new AhvKasseTotal("#ahv-" + nummer, nummer, mitglied ?? nummer);
            ahvKassen.Add(neu);
            return neu;
        }
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
                zivAm, zivSeitAm ?? (MapCivilStatus(zivAm) == "single" ? DateOnly.FromDateTime(e.DateOfBirth.Value) : null), adresseAm, adresseVorher, umzug?.Wegzug,
                adresseVorher == null ? null : WohnGemeinde(adresseVorher.Zip, adresseVorher.City));

            var zuordnung = zuordnungen
                .Where(z => z.EmployeeId == e.Id && z.GueltigAb <= stichtagEnde)
                .OrderByDescending(z => z.GueltigAb).ThenByDescending(z => z.Id).FirstOrDefault();
            var modell = (zuordnung != null ? modelle.FirstOrDefault(m => m.Id == zuordnung.ArbeitszeitmodellId) : null)
                         ?? modelle.FirstOrDefault();
            // Eine Work je Anstellung mit Lohn in der Meldeperiode, Daten wie damals: ein späterer
            // Wiedereintritt überschreibt die Stammdaten (Quality Tool 2024-11: TF16 Aebi).
            var eigeneVertraege = employments.Where(m => m.EmployeeId == x.EmployeeId).ToList();
            (DateTime Start, DateTime? Ende)? KetteIm(int monat) => ElmMonthlyDeclarationBuilder.Anstellung(eigeneVertraege,
                new DateTime(year, monat, 1), new DateTime(year, monat, DateTime.DaysInMonth(year, monat)));
            var ketten = rowsByEmp[x.EmployeeId].Select(r => r.Month).Distinct().OrderBy(m => m)
                .Select(KetteIm).Where(k => k != null).Select(k => k!.Value)
                .DistinctBy(k => k.Start).ToList();
            if (ketten.Count == 0)
                ketten.Add((e.EntryDate ?? em?.ContractStartDate ?? yearStart, e.ExitDate));
            var works = ketten.Select((k, nr) =>
            {
                // Vertrag am Ende der Meldeperiode, nicht der heutige (Quality Tool 2024-11: TF14 Egli, TF44 Lusser Hans).
                var vertrag = eigeneVertraege.Where(m => m.ContractStartDate >= k.Start && m.ContractStartDate <= yearEnd && (k.Ende == null || m.ContractStartDate <= k.Ende))
                    .OrderByDescending(m => m.ContractStartDate).FirstOrDefault() ?? em;
                var work = new XElement(C + "Work",
                    new XAttribute("workID", nr == 0 ? $"#w{e.Id}" : $"#w{e.Id}_{nr}"),
                    new XAttribute("companyWorkingTimeIDRef", "#" + (modell?.KennungOderId ?? "cwt1")),
                    new XElement(C + "WorkingTime", WorkingTime(vertrag,
                        modell?.Wochenstunden is > 0m ? modell.Wochenstunden!.Value : main.NormalWeeklyHours ?? 42m,
                        zuordnung?.AbweichendeWochenstunden, zuordnung?.AbweichendeWochenlektionen, modell?.Wochenlektionen)),
                    new XElement(C + "EntryDate", k.Start.ToString("yyyy-MM-dd")));
                if (k.Ende != null && k.Ende.Value <= yearEnd)
                    work.Add(new XElement(C + "WithdrawalDate", k.Ende.Value.ToString("yyyy-MM-dd")));
                return work;
            }).ToList();

            codesByEmp.TryGetValue(x.EmployeeId, out var codes);
            var monate = rowsByEmp[x.EmployeeId]
                .GroupBy(r => r.Month)
                .OrderBy(g => g.Key)
                .Select(g => new ElmAhvJahreslohn.Monat(
                    g.Key,
                    g.Sum(r => r.SvBasisAhv),
                    g.Sum(r => Math.Min(r.SvBasisAhv, AlvMonatsCap)),
                    ArtDesMonats(e, year, g.Key, codes, ahvStand))
                {
                    Kette = KetteIm(g.Key)?.Start,
                    Lohnzettel = ElmAhvJahreslohn.AusLohnzetteln(g.Select(r => r.SlipJson)),
                })
                .ToList();

            // Zeitraum eines Abschnitts: genaues Datum bei Ein-/Austritt im ersten bzw. letzten Monat.
            // Lohn nach dem Austritt zählt zur beendeten Anstellung, auch aus dem Vorjahr
            // (Referenz 2026-02: TF06 Zahnd 01.01.–31.10.2025).
            (DateTime Von, DateTime Bis) Zeit(int von, int bis)
            {
                var vonKette = KetteIm(von);
                var bisKette = KetteIm(bis);
                var (zeitVon, zeitBis) = ElmFakJahreslohn.Zeitraum(year, von, bis, vonKette?.Start, bisKette?.Ende);
                if (vonKette?.Ende is DateTime vonEnde && vonEnde < new DateTime(year, von, 1))
                    zeitVon = vonKette.Value.Start > new DateTime(vonEnde.Year, 1, 1) ? vonKette.Value.Start : new DateTime(vonEnde.Year, 1, 1);
                if (bisKette?.Ende is DateTime bisEnde && bisEnde < new DateTime(year, bis, 1))
                    zeitBis = bisEnde;
                return (zeitVon, zeitBis);
            }

            var salaries = new XElement(Sd + "AHV-AVS-Salaries");
            var ahvAbschnitte = ElmAhvJahreslohn.Abschnitte(monate);
            foreach (var a in ahvAbschnitte)
            {
                var (from, until) = Zeit(a.Von, a.Bis);
                var ahvKasse = AhvKasseAm(DateOnly.FromDateTime(until));
                var salary = new XElement(Sd + "AHV-AVS-Salary",
                    new XAttribute("addresseeIDRef", ahvKasse.Id),
                    new XElement(Sd + "AccountingTime",
                        new XElement(Ep + "from", from.ToString("yyyy-MM-dd")),
                        new XElement(Ep + "until", until.ToString("yyyy-MM-dd"))),
                    new XElement(Sd + "AHV-AVS-BaseSalary", Betrag05(a.Basis)),
                    new XElement(Sd + "AHV-AVS-Income", Betrag05(a.AhvLohn)));
                if (a.AhvFrei != 0)
                    salary.Add(new XElement(Sd + "AHV-AVS-Open", Betrag05(a.AhvFrei)));
                salary.Add(new XElement(Sd + "ALV-AC-Income", Betrag05(a.AlvLohn)));
                if (a.MitAlvz)
                    salary.Add(new XElement(Sd + "ALVZ-ACS-Income", Betrag05(a.AlvzLohn)));
                if (a.AlvFrei != 0)
                    salary.Add(new XElement(Sd + "ALV-AC-Open", Betrag05(a.AlvFrei)));
                if (a.Art == ElmAhvJahreslohn.Art.Verzicht)
                    salary.Add(new XElement(Sd + "WaiveOfPensionDeduct"));
                salaries.Add(salary);
                salaryTags++;
                // Totale = Summe der gemeldeten (gerundeten) Einzelwerte, sonst passen sie nicht zusammen.
                totalAhv += PayrollCalculations.Round05(a.AhvLohn);
                totalAlv += PayrollCalculations.Round05(a.AlvLohn);
                if (a.MitAlvz) totalAlvz += PayrollCalculations.Round05(a.AlvzLohn);
                totalAhvFrei += PayrollCalculations.Round05(a.AhvFrei);
                totalAlvFrei += PayrollCalculations.Round05(a.AlvFrei);
                ahvKasse.Ahv += PayrollCalculations.Round05(a.AhvLohn);
                ahvKasse.Alv += PayrollCalculations.Round05(a.AlvLohn);
                if (a.MitAlvz) ahvKasse.Alvz += PayrollCalculations.Round05(a.AlvzLohn);
                ahvKasse.AhvFrei += PayrollCalculations.Round05(a.AhvFrei);
                ahvKasse.AlvFrei += PayrollCalculations.Round05(a.AlvFrei);
            }

            var fakSalaries = FakSalaries(e, x.EmployeeId, monate, ahvAbschnitte, Zeit);
            // Lohnausweis-Angaben wie auf dem Papier-Lohnausweis: Filiale des letzten Lohnzettels als Vorgabe, Person überschreibt.
            var letzteZeile = rowsByEmp[x.EmployeeId].OrderBy(r => r.Month).Last();
            var filialeLa = branches.FirstOrDefault(b => b.Id == letzteZeile.CompanyProfileId);
            var firmaLa = filialeLa == null ? null : LohnausweisFassung.ProfilFuerJahr(filialeLa,
                fassungenLa.Where(f => f.CompanyProfileId == filialeLa.Id), year);
            var wirkung = LohnausweisAngaben.Aufloesen(firmaLa,
                LohnausweisFassung.FuerJahr(personenLa.Where(p => p.EmployeeId == e.Id), year, p => p.GueltigAb, p => p.GueltigBis),
                year, null);
            var qstBis = stichtagEnde;
            // Code NON = rückwirkend nicht quellensteuerpflichtig (Quality Tool 2024-12: TF14 Egli, Bewilligung C ab 01.11.).
            var qstAmEnde = qstZeiten.Any(q => q.EmployeeId == e.Id && q.ValidFrom <= qstBis && (q.ValidTo == null || q.ValidTo >= qstBis)
                                               && !string.Equals(q.QstCode, "NON", StringComparison.OrdinalIgnoreCase));
            // Regelmässiges Pensum unter 100 % (Quality Tool 2025-01: TF17 Binggeli, TF05 Moser).
            var teilzeit = works[^1].Descendants(C + "ActivityRate").Any(r => decimal.TryParse(r.Value, CultureInfo.InvariantCulture, out var satz) && satz < 100m);
            var taxPeriode = Zeit(rowsByEmp[x.EmployeeId].Min(r => r.Month), rowsByEmp[x.EmployeeId].Max(r => r.Month));
            var grenz = Grenzgaenger(e, eigeneVertraege, taxPeriode);
            persons.Add(new XElement(Sd + "Person", particulars, works, salaries, fakSalaries,
                VersicherungSalaries(e, x.EmployeeId, Zeit, m => KetteIm(m)?.Start),
                TaxSalaries(rowsByEmp[x.EmployeeId].OrderBy(r => r.Month).Select(r => r.SlipJson), adresseAm,
                    taxPeriode, wirkung, qstAmEnde, teilzeit),
                grenz));
        }

        // Grenzgänger: TaxCrossborderSalaries neben dem Lohnausweis (Referenz 2025-01: TF34 Rinaldi); Inhalt wie die eigene
        // Grenzgänger-Meldung. Massgebend ist die Quellensteuer-Version am Ende der Periode; der Zeitraum beginnt mit der
        // ersten Version derselben Art (Referenz 2025-09: TF36 Maldini ab 01.09.; 2025-11: TF38 Jung Frankreich + SFN).
        XElement? Grenzgaenger(Employee e, List<Employment> vertraege, (DateTime Von, DateTime Bis) periode)
        {
            var periodeVon = DateOnly.FromDateTime(periode.Von);
            var periodeBis = DateOnly.FromDateTime(periode.Bis);
            var versionen = qstZeiten.Where(q => q.EmployeeId == e.Id && q.ValidFrom <= periodeBis && (q.ValidTo == null || q.ValidTo >= periodeVon))
                .OrderByDescending(q => q.ValidFrom).ToList();
            var vertrag = vertraege.Where(m => m.ContractStartDate <= periode.Bis).OrderByDescending(m => m.ContractStartDate).FirstOrDefault();
            if (versionen.Count == 0 || vertrag == null) return null;
            var aktuell = versionen[0];
            var kanton = (aktuell.Steuerkanton ?? "").Trim().ToUpperInvariant();
            var xml = new XElement(Sd + "TaxCrossborderSalaries");
            decimal? steuerbar = null;
            foreach (var art in new[] { ElmGrenzgaengerJahresBuilder.ArtItalien, ElmGrenzgaengerJahresBuilder.ArtFrankreich, ElmGrenzgaengerJahresBuilder.ArtSfn })
            {
                if (!ElmGrenzgaengerJahresBuilder.GiltFuer(art, aktuell)) continue;
                if (kanton.Length != 2)
                {
                    warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Grenzgänger ohne Steuerkanton — nicht gemeldet.");
                    return null;
                }
                var ab = versionen.TakeWhile(q => ElmGrenzgaengerJahresBuilder.GiltFuer(art, q)).Last().ValidFrom;
                if (ab < periodeVon) ab = periodeVon;
                var slips = rowsByEmp[e.Id].Where(r => ab.Year < year || r.Month >= ab.Month).Select(r => r.SlipJson ?? "");
                var g = ElmGrenzgaengerJahresBuilder.Gehalt(art, aktuell, e, kanton, ab, periodeBis, vertrag, slips, warn);
                if (g == null) continue;
                xml.Add(g.Value.Xml.Elements());
                steuerbar ??= g.Value.Steuerbar;
                grenzTags++;
            }
            if (steuerbar == null) return null;
            grenzSteuerbar[kanton] = grenzSteuerbar.GetValueOrDefault(kanton) + steuerbar.Value;
            return xml;
        }

        // Lohnausweis einer Person (Richtlinien Kapitel 9): adressiert an den Wohnkanton, im Ausland EX.
        XElement TaxSalaries(IEnumerable<string?> lohnzettel, PersonAdresseAm adresse, (DateTime Von, DateTime Bis) periode,
            LohnausweisAngaben.Wirkung angaben, bool qstAmEnde, bool teilzeit)
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
            var eu = ElmLohnausweisJahr.Feld.SpesenEffektivUebrige;
            // Expat-Spesen: keine Deklaration, wenn ein Ruling vorliegt (Richtlinien Tabelle 9.9).
            var mitEu = a.Von(eu) != 0m && angaben.Expat == null && angaben.Spesenreglement == null && !angaben.Rz52;
            var wb = a.Von(ElmLohnausweisJahr.Feld.Weiterbildung);
            // Reglement oder RZ 52: effektive Spesen stehen nicht auf dem Lohnausweis (Quality Tool 2024-11: TF16 Aebi).
            if (angaben.Spesenreglement != null || angaben.Rz52) ef = 0m;
            XElement Grant(string name, LohnausweisAngaben.Grant g) => new(Sd + name,
                new XElement(Sd + "Allowed", g.Datum.ToString("yyyy-MM-dd")), new XElement(Sd + "Canton", g.Kanton));
            XElement? regel = angaben.Spesenreglement is { } sr
                ? new XElement(Sd + "ChargesRule", Grant("WithRegulation", sr))
                : angaben.Rz52 ? new XElement(Sd + "ChargesRule", new XElement(Sd + "Guidance")) : null;
            XElement? Kreuz(string name, bool an) => an ? new XElement(Sd + name) : null;
            var gruende = new[]
            {
                Kreuz("BlockedOptions", angaben.Gesperrt), Kreuz("UnquotedOptions", angaben.NichtKotiert),
                Kreuz("DeferredBenefitsStaffShares", angaben.Anwartschaft), Kreuz("FictitiousStaffShare", angaben.Unecht),
            }.Where(k => k != null).ToList();
            var qstBemerkung = qstAmEnde || a.Quellensteuer != 0m;
            var bemerkungen = new XElement(Sd + "StandardRemark",
                Kreuz("ChildAllowancePerAHV-AVS", angaben.KinderzulageAhv),
                angaben.Umzugskosten is decimal uk ? Betrag("RelocationCosts", uk) : null,
                angaben.Verkehrswert is { } vw ? Grant("StaffShareMarketValue", vw) : null,
                gruende.Count > 0 ? new XElement(Sd + "StaffShareWithoutTaxableIncome", gruende) : null,
                Kreuz("CompanyCarClarify", angaben.WagenAbklaeren),
                Kreuz("MinimalEmployeeCarPartPercentage", angaben.WagenMinimal),
                Kreuz("TaxAtSourcePeriodForObjection", qstBemerkung),
                angaben.Expat is { } ex ? Grant("ExpatriateRuling", ex) : null,
                Kreuz("PartTimeEmployment", teilzeit));
            XElement? spesen = ef != 0m || mitEu || pw != 0m || a.Von(pu) != 0m || wb != 0m
                ? new XElement(Sd + "Charges",
                    ef != 0m || mitEu ? new XElement(Sd + "Effective", ef != 0m ? Betrag("TravelFoodAccommodation", ef) : null, mitEu ? MitText("Other", eu) : null) : null,
                    pw != 0m || a.Von(pu) != 0m ? new XElement(Sd + "LumpSum", WennBetrag("Car", pw), MitText("Other", pu)) : null,
                    WennBetrag("Education", wb))
                : null;
            return new XElement(Sd + "TaxSalaries", new XElement(Sd + "TaxSalary",
                new XAttribute("addresseeIDRef", "#TAX-" + kanton),
                new XElement(Sd + "DocID", Guid.NewGuid().ToString()),
                new XElement(Sd + "CreationDate", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fff")),
                new XElement(Sd + "Period", new XElement(Ep + "from", periode.Von.ToString("yyyy-MM-dd")), new XElement(Ep + "until", periode.Bis.ToString("yyyy-MM-dd"))),
                Kreuz("FreeTransport", angaben.BoxF),
                Kreuz("CanteenLunchCheck", angaben.BoxG),
                WennBetrag("Income", a.Von(ElmLohnausweisJahr.Feld.Lohn)),
                fringe,
                MitText("SporadicBenefits", ElmLohnausweisJahr.Feld.Unregelmaessig),
                MitText("CapitalPayment", ElmLohnausweisJahr.Feld.Kapital),
                WennBetrag("OwnershipRight", a.Von(ElmLohnausweisJahr.Feld.Beteiligung)),
                WennBetrag("BoardOfDirectorsRemuneration", a.Von(ElmLohnausweisJahr.Feld.Verwaltungsrat)),
                MitText("OtherBenefits", ElmLohnausweisJahr.Feld.Uebrige),
                Betrag("GrossIncome", a.Brutto),
                WennBetrag("AHV-ALV-NBUV-AVS-AC-AANP-Contribution", a.Beitraege),
                a.Bvg != 0m || a.BvgEinkauf != 0m
                    ? new XElement(Sd + "BVG-LPP-Contribution", WennBetrag("Regular", a.Bvg), WennBetrag("Purchase", a.BvgEinkauf)) : null,
                Betrag("NetIncome", a.Netto),
                WennBetrag("DeductionAtSource", a.Quellensteuer),
                regel,
                spesen,
                string.IsNullOrWhiteSpace(angaben.Ziffer14) ? null : new XElement(Sd + "OtherFringeBenefits", angaben.Ziffer14),
                bemerkungen.HasElements ? bemerkungen : null));
        }

        // UVG-, UVGZ- und KTG-Salaries einer Person: je Beitragsdauer und Code (ElmVersicherungJahreslohn).
        IEnumerable<XElement> VersicherungSalaries(Employee e, int empId, Func<int, int, (DateTime Von, DateTime Bis)> zeit, Func<int, DateTime?> kette)
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
                // Lohnausfall KA/SW zählt für die UVG-Basis, nicht zum Bruttolohn (Referenz 2025-02: TF02 Paganini).
                decimal brutto = mg.Sum(r => r.Brutto) - ElmLohnausweisJahr.LohnausfallKurzarbeit(mg.Select(r => r.SlipJson)),
                    basis = mg.Sum(r => r.SvBasisNbuv), ahv = mg.Sum(r => r.SvBasisAhv);
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
                        ElmVersicherungJahreslohn.UvgVersichert(uvg) ? nbu ?? Summe(z => z.Kat == "BUV") : 0m, ahv) { Kette = kette(mg.Key) });

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
                        monateJeArt[art].Add(new(mg.Key, code, 0m, basis, pflichtig, ahv) { Kette = kette(mg.Key) });
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
                    // Ältere Lohnzettel: Code steht nur im Text («KTG 11 — KTG-Lohn», Referenz 2025-01: TF11 Bosshard).
                    if (string.IsNullOrEmpty(code) && kat is "UVGZ" or "KTG" && z.TryGetProperty("bezeichnung", out var bz) && bz.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var m = Regex.Match(bz.GetString() ?? "", @"^(?:KTG|UVG-Zusatz)\s+(\w+)\s+—");
                        if (m.Success) code = m.Groups[1].Value;
                    }
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
                var monat = monate.FirstOrDefault(m => m.Nr == g.Key);
                var art = monat.Art;
                var filiale = g.OrderByDescending(r => r.SvBasisAhv).First().CompanyProfileId;
                // Lohn nach dem Austritt gehört der Kasse von damals (Referenz 2025-01: TF07 Burri).
                var kasse = KasseIm(filiale, g.Key, DateOnly.FromDateTime(zeit(g.Key, g.Key).Bis));
                if (kasse == null) ohneKasse.Add(g.Key);
                decimal wieder = 0, einmal = 0;
                foreach (var r in g) (wieder, einmal) = Zulagen(r.SlipJson, wieder, einmal);
                var kanton = (kantonByFiliale.GetValueOrDefault(filiale) ?? "").Trim().ToUpperInvariant();
                if (kanton.Length != 2) kanton = kasse?.Kantone.FirstOrDefault() ?? "EX";
                fakMonate.Add(new ElmFakJahreslohn.Monat(g.Key,
                    // AHV-Lohn der Zeile: ohne Freibetrag (Referenz 2025-01: TF09 Estermann, TF01 Herz).
                    art == ElmAhvJahreslohn.Art.Frei ? 0m : monat.Lohnzettel?.Ahv ?? g.Sum(r => r.SvBasisAhv),
                    wieder, einmal, kasse?.Kassennummer, kanton));
                if (kasse != null) fakGenutzt.TryAdd(kasse.Kassennummer, kasse);
            }
            if (ohneKasse.Count > 0)
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): keine FAK an der Filiale in Monat {string.Join(", ", ohneKasse.Distinct().OrderBy(m => m))} "
                       + "— dort fehlt die FAK-Zeile (Filiale → Lohndaten Empfänger, Art FAK).");

            var el = new XElement(Sd + "FAK-CAF-Salaries");
            // Je Anstellung, nicht je AHV-Abschnitt: Referenzalter oder Lücken trennen die FAK nicht (Referenz 2025-03: TF44 Lusser, TF41 Meier).
            foreach (var kette in monate.Where(m => m.Art != ElmAhvJahreslohn.Art.Frei).GroupBy(m => m.Kette))
            {
                var vonNr = kette.Min(m => m.Nr);
                var bisNr = kette.Max(m => m.Nr);
                var (von, bis) = zeit(vonNr, bisNr);
                foreach (var roh in ElmFakJahreslohn.Abschnitte(year, vonNr, bisNr, fakMonate.Where(m => kette.Any(k => k.Nr == m.Nr)).ToList(), von, bis, ueberLuecken: true))
                {
                    // Lohn nach dem Austritt: Zeitraum der beendeten Anstellung (Referenz 2025-01: TF07 Burri).
                    var f = roh with { Von = roh.Von > bis ? von : roh.Von, Bis = roh.Bis > bis ? bis : roh.Bis };
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

        // Kanton mit Lohnzettel, aber ohne gemeldete Person: 0.00 in den Totalen (Referenz 2025-02: FAK LU, TF15 Degelo in ZG).
        foreach (var r in rows)
        {
            var kasse = KasseIm(r.CompanyProfileId, r.Month);
            var kanton = (kantonByFiliale.GetValueOrDefault(r.CompanyProfileId) ?? "").Trim().ToUpperInvariant();
            if (kasse == null || kanton.Length != 2) continue;
            fakGenutzt.TryAdd(kasse.Kassennummer, kasse);
            if (!fakSummen.TryGetValue(kasse.Kassennummer, out var jeKanton))
                fakSummen[kasse.Kassennummer] = jeKanton = new Dictionary<string, ElmFakAdressierung.Summe>(StringComparer.OrdinalIgnoreCase);
            jeKanton.TryAdd(kanton, default);
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
        var companyDescription = CompanyDescription(
            ElmMonthlyDeclarationBuilder.NurArbeitsorteMitLohn(stamm, rows.Select(r => r.CompanyProfileId).ToHashSet()),
            ElmMonthlyDeclarationBuilder.ArbeitszeitModelle(modelle, zuordnungen, empIds, main.NormalWeeklyHours ?? 42m, warn));

        var jobAddressees = ahvKassen.Select(k => new XElement(Sdc + "Addressee",
            new XAttribute("addresseeID", k.Id),
            new XElement(Ep + "AddresseeIdentification", k.Kasse),
            new XElement(Ep + "ProcessByDistributor", "true"))).ToList();
        jobAddressees.AddRange(faks.Select(ElmFakAdressierung.JobAddressee));
        foreach (var art in versOhneVersicherer)
            warn.Add($"{art}: Personen mit {art}-Code, aber kein {art}-Versicherer im Empfänger-Katalog — {art} nicht gemeldet.");
        // Nur Versicherer melden, für die es Personen gibt (wie die Referenz).
        var versGemeldet = new[] { "UVG", "UVGZ", "KTG" }.Where(a => vers.ContainsKey(a) && versTags[a] > 0).ToList();
        jobAddressees.AddRange(taxKantone.Select(k => new XElement(Sdc + "Addressee",
            new XAttribute("addresseeID", "#TAX-" + k),
            new XElement(Ep + "AddresseeIdentification", k),
            new XElement(Ep + "ProcessByDistributor", "true"))));
        jobAddressees.AddRange(grenzSteuerbar.Keys.Select(k => new XElement(Sdc + "Addressee",
            new XAttribute("addresseeID", "#QST-" + k),
            new XElement(Ep + "AddresseeIdentification", k),
            new XElement(Ep + "ProcessByDistributor", "true"))));
        jobAddressees.AddRange(versGemeldet.Select(a => new XElement(Sdc + "Addressee",
            new XAttribute("addresseeID", vers[a].Id),
            new XElement(Ep + "AddresseeIdentification", vers[a].Kassennummer),
            new XElement(Ep + "ProcessByDistributor", "true"))));

        var institutions = ahvKassen.Select(k => new XElement(Sd + "AHV-AVS",
                new XAttribute("addresseeIDRef", k.Id),
                new XElement(Sd + "AK-CC-CustomerNumber", k.Abrechnung),
                InsuranceBlock("UVG-LAA-Insurance", uvgE?.Bezeichnung, uvgE?.UidNummer, st?.UvgVersichertSeit,
                    "UVG-Meldung folgt in Aufbau-Etappe E5"),
                InsuranceBlock("BVG-LPP-Insurance", bvgE?.Bezeichnung, bvgE?.UidNummer, st?.BvgVersichertSeit,
                    "BVG-Meldung folgt in Aufbau-Etappe E5"))).ToList();
        institutions.AddRange(faks.Select(ElmFakAdressierung.Institution));
        institutions.AddRange(versGemeldet.Select(a => new XElement(Sd + VersElement[a],
            new XAttribute("addresseeIDRef", vers[a].Id),
            new XElement(Sd + "InsuranceCompanyName", vers[a].Name),
            new XElement(Sd + "CustomerIdentity", vers[a].Kunde),
            new XElement(Sd + "ContractIdentity", vers[a].Vertrag))));
        institutions.AddRange(taxKantone.Select(k => new XElement(Sd + "Tax", new XAttribute("addresseeIDRef", "#TAX-" + k))));
        var qstKassen = empf.Where(x => x.Art == "QST" && x.IsActive).ToList();
        foreach (var k in grenzSteuerbar.Keys)
        {
            var nummer = ElmGrenzgaengerJahresBuilder.SslNummer(qstKassen, k);
            if (nummer.Length == 0) warn.Add($"Kanton {k}: SSL-Nummer fehlt (Empfänger-Katalog QST) — im XML steht das Kantonskürzel.");
            institutions.Add(new XElement(Sd + "TaxCrossborder",
                new XAttribute("addresseeIDRef", "#QST-" + k),
                new XElement(Sd + "CustomerIdentity", nummer.Length == 0 ? k : nummer),
                new XElement(Sd + "TaxAtSourceType", "salaries")));
        }

        var salaryTotals = ahvKassen.Select(k => new XElement(Sd + "AHV-AVS-Totals",
            new XAttribute("addresseeIDRef", k.Id),
            new XElement(Sd + "Total-AHV-AVS-Incomes", Amt(k.Ahv)),
            new XElement(Sd + "Total-AHV-AVS-Open", Amt(k.AhvFrei)),
            new XElement(Sd + "Total-ALV-AC-Incomes", Amt(k.Alv)),
            new XElement(Sd + "Total-ALVZ-ACS-Incomes", Amt(k.Alvz)),
            new XElement(Sd + "Total-ALV-AC-Open", Amt(k.AlvFrei)))).ToList();
        // Quality Tool vergleicht die Kassen der Reihe nach (2025-01); ohne Angabe im Katalog bleibt es bei der Kassennummer.
        int? ReihenfolgeDerKasse(string nummer) => empf
            .Where(x => x.Art == "FAK" && (x.Kassennummer ?? "").Trim() == nummer).Select(x => x.MeldungReihenfolge).FirstOrDefault(r => r != null);
        salaryTotals.AddRange(faks.OrderBy(f => ReihenfolgeDerKasse(f.Kassennummer) ?? int.MaxValue).ThenBy(f => f.Kassennummer, StringComparer.OrdinalIgnoreCase)
            .Select(f => ElmFakAdressierung.Totals(f,
            fakSummen.TryGetValue(f.Kassennummer, out var jeKanton) ? jeKanton : new Dictionary<string, ElmFakAdressierung.Summe>())));

        // UVG-Totale nur für Betriebsteile mit Personen (Quality Tool 2024-11); UVGZ/KTG je Code, Codes der SV-Sätze ohne Personen mit 0.00.
        IEnumerable<string> SatzCodes(params string[] svCodes) => svSaetze
            .Where(r => svCodes.Contains(r.Code) && !string.IsNullOrWhiteSpace(r.LoesungsCode)
                        && (ganzeFirma || r.CompanyProfileId == null || filialIds.Contains(r.CompanyProfileId.Value)))
            .Select(r => r.LoesungsCode!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase);
        if (versGemeldet.Contains("UVG"))
        {
            var branchen = ElmVersicherungJahreslohn.UvgTotale(versZeilen["UVG"]);
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

        salaryTotals.AddRange(grenzSteuerbar.Select(g => new XElement(Sd + "TaxCrossborderTotals",
            new XAttribute("addresseeIDRef", "#QST-" + g.Key),
            new XElement(Sd + "Period",
                new XElement(Ep + "from", yearStart.ToString("yyyy-MM-dd")),
                new XElement(Ep + "until", yearEnd.ToString("yyyy-MM-dd"))),
            new XElement(Sd + "TaxAtSourceCanton", g.Key),
            new XElement(Sd + "TaxableEarning", Betrag05(g.Value)))));

        var counters = new List<XElement>
        {
            new(Sd + "NumberOf-AHV-AVS-Salary-Tags", salaryTags)
        };
        if (faks.Count > 0)
            counters.Add(new XElement(Sd + "NumberOf-FAK-CAF-Salary-Tags", fakTags));
        foreach (var a in versGemeldet)
            counters.Add(new XElement(Sd + $"NumberOf-{VersElement[a]}-Salary-Tags", versTags[a]));
        if (taxTags > 0) counters.Add(new XElement(Sd + "NumberOf-TaxSalary-Tags", taxTags));
        if (grenzTags > 0) counters.Add(new XElement(Sd + "NumberOf-TaxCrossborderSalary-Tags", grenzTags));

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

        return new BuildResult(xml, persons.Count, 0, totalAhv, totalAlv, warn, xsdFehler);
    }

    /// <summary>Ausgleichskasse der Meldung mit ihren Totalen.</summary>
    private sealed record AhvKasseTotal(string Id, string Kasse, string Abrechnung)
    {
        public decimal Ahv, Alv, Alvz, AhvFrei, AlvFrei;
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
