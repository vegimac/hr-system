using System.Text.Json;
using System.Xml.Linq;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.Personaldaten;
using Microsoft.EntityFrameworkCore;
using static HrSystem.Services.Elm.ElmGemeinsam;
using E = HrSystem.Services.Elm.ElmEmaEreignisse;

namespace HrSystem.Services.Elm;

/// <summary>
/// EMA-Meldung NotifyChanges (E7): lädt den Monatsstand je Person, erkennt die Ereignisse
/// (<see cref="ElmEmaEreignisse"/>) und baut die Sammelmeldung eines Monats. Nichts wird gesendet.
/// </summary>
public class ElmEmaBuilder
{
    private readonly AppDbContext _db;
    private readonly ElmXmlValidator _validator;
    private readonly ElmEinstellungen _einstellungen;
    private readonly ElmZertifikatStore _store;

    /// <summary>Wie weit die Rückschau Lohnzettel lädt: 12 Läufe plus der Stand davor.</summary>
    private const int MonateZurueck = 24;

    public ElmEmaBuilder(AppDbContext db, ElmXmlValidator validator, ElmEinstellungen einstellungen, ElmZertifikatStore store)
    {
        _db = db;
        _validator = validator;
        _einstellungen = einstellungen;
        _store = store;
    }

    public sealed record Zeile(int EmployeeId, string Name, string? PersonalNr, E.Ereignis Ereignis, bool? Arbeitsfaehig);

    public sealed record Ergebnis(string Xml, List<Zeile> Ereignisse, List<string> Warnungen, List<string> XsdFehler);

    /// <summary>Monatsstand aller Personen der Rechtseinheit, nur lesend. Auch Grundlage der Prospective.</summary>
    public sealed record Personen(
        ElmGemeinsam.RechtseinheitStamm Stamm,
        List<Employee> Mitarbeitende,
        Dictionary<int, List<Employment>> Anstellungen,
        Dictionary<int, E.Person> Stand,
        Func<string, ElmBvgBasis.Steuerung?> Steuerung,
        List<EmployeeZivilstandHistory> Zivilstand,
        List<EmployeeWohnortHistory> Wohnort,
        List<ElmEmaManuell> Manuell,
        List<EmployeeVersicherungCode> Codes);

    public async Task<Personen?> LadeAsync(int year, int month, int? nurFiliale, List<string> warn, CancellationToken ct)
    {
        var stamm = await LadeRechtseinheitAsync(_db, warn, ct);
        if (stamm == null) return null;
        if (nurFiliale is > 0)
        {
            stamm = stamm with { Filialen = stamm.Filialen.Where(b => b.Id == nurFiliale).ToList() };
            if (stamm.Filialen.Count == 0) { warn.Add("Diese Filiale ist in der Meldung nicht enthalten."); return null; }
        }
        var filialIds = stamm.Filialen.Select(b => b.Id).ToList();
        var bis = new DateOnly(year, month, 1);
        var bisEnde = bis.AddMonths(1).AddDays(-1).ToDateTime(TimeOnly.MinValue);
        var von = bis.AddMonths(-MonateZurueck);

        var vertraege = await _db.Employments.AsNoTracking()
            .Where(v => v.CompanyProfileId != null && filialIds.Contains(v.CompanyProfileId.Value) && v.ContractStartDate <= bisEnde)
            .ToListAsync(ct);
        var ids = vertraege.Select(v => v.EmployeeId).Distinct().ToList();
        var mitarbeitende = await _db.Employees.AsNoTracking()
            .Include(e => e.NationalityRef).Include(e => e.PermitType)
            .Where(e => ids.Contains(e.Id) && !e.IsHidden && !e.IsPayrollExcluded)
            .OrderBy(e => e.Id).ToListAsync(ct);

        var codes = await _db.EmployeeVersicherungCodes.AsNoTracking()
            .Where(c => ids.Contains(c.EmployeeId) && (c.Art == "BVG" || c.Art == EmployeeVersicherungCode.ArtAhv))
            .ToListAsync(ct);
        var pflicht = await _db.EmployeeBvgPflichten.AsNoTracking().Where(p => ids.Contains(p.EmployeeId)).ToListAsync(ct);
        var zivil = await _db.EmployeeZivilstandHistories.AsNoTracking().Where(z => ids.Contains(z.EmployeeId)).ToListAsync(ct);
        var wohn = await _db.EmployeeWohnortHistories.AsNoTracking().Where(w => ids.Contains(w.EmployeeId) && !w.DatumOffen).ToListAsync(ct);
        var manuell = await _db.ElmEmaManuelle.AsNoTracking().Where(m => ids.Contains(m.EmployeeId)).ToListAsync(ct);
        var firmen = stamm.Filialen.ToDictionary(c => c.Id, c => c.DefaultThirteenthSalaryPercent ?? 8.33m);

        var positionen = await _db.Lohnpositionen.AsNoTracking()
            .Select(l => new { l.Code, l.SwissdecLohnart, l.BvgBetrachtung, l.BvgFaktor, l.BvgFaktorX, l.BvgRueckCode })
            .ToListAsync(ct);
        var steuerungJeCode = positionen.GroupBy(p => p.Code).ToDictionary(g => g.Key, g => g.First());
        ElmBvgBasis.Steuerung? Steuerung(string code)
        {
            if (!steuerungJeCode.TryGetValue(code, out var p)) return ElmBvgBasis.AusKatalog(code);
            if (string.Equals(p.BvgBetrachtung, LohnartSteuerung.BvgVoraussichtlich, StringComparison.OrdinalIgnoreCase))
                return new ElmBvgBasis.Steuerung(true, p.BvgFaktor, p.BvgFaktorX, 0);
            if (string.Equals(p.BvgBetrachtung, LohnartSteuerung.BvgRueckwirkend, StringComparison.OrdinalIgnoreCase))
                return new ElmBvgBasis.Steuerung(false, 1m, false, p.BvgRueckCode);
            return null;
        }

        // Lohnzeilen je Person und Monat aus den definitiv abgeschlossenen Läufen.
        var lauefe = await (from s in _db.PayrollSnapshots.AsNoTracking()
                            join p in _db.PayrollPerioden.AsNoTracking() on s.PayrollPeriodeId equals p.Id
                            where ids.Contains(s.EmployeeId) && s.Status != "STORNIERT" && p.Status == "abgeschlossen"
                                  && filialIds.Contains(p.CompanyProfileId)
                                  && p.PeriodFrom >= von && p.PeriodFrom <= bis
                            select new { s.EmployeeId, p.PeriodFrom, s.SlipJson }).ToListAsync(ct);
        var lohnJe = new Dictionary<(int, DateOnly), Dictionary<string, decimal>>();
        foreach (var l in lauefe)
        {
            var monat = new DateOnly(l.PeriodFrom.Year, l.PeriodFrom.Month, 1);
            if (!lohnJe.TryGetValue((l.EmployeeId, monat), out var zeilen)) lohnJe[(l.EmployeeId, monat)] = zeilen = new();
            foreach (var (code, betrag) in LohnZeilen(l.SlipJson))
                zeilen[code] = zeilen.GetValueOrDefault(code) + betrag;
        }

        var jahresmeldungen = _store.LadeBvgLohnsummen().Where(j => j.Freigegeben).ToList();
        var anstellungen = vertraege.GroupBy(v => v.EmployeeId).ToDictionary(g => g.Key, g => g.OrderBy(v => v.ContractStartDate).ToList());
        var stand = new Dictionary<int, E.Person>();
        foreach (var e in mitarbeitende)
        {
            if (e.DateOfBirth == null)
            {
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Geburtsdatum fehlt (Pflichtfeld) — nicht gemeldet.");
                continue;
            }
            var eigene = anstellungen[e.Id];
            var ersterVertrag = DateOnly.FromDateTime(eigene[0].ContractStartDate);
            var start = new DateOnly(ersterVertrag.Year, ersterVertrag.Month, 1);
            if (start < von) start = von;
            var monate = new List<E.Monatsstand>();
            for (var m = start; m <= bis; m = m.AddMonths(1))
            {
                var anfang = m.ToDateTime(TimeOnly.MinValue);
                var ende = m.AddMonths(1).AddDays(-1);
                var endeDt = ende.ToDateTime(TimeOnly.MinValue);
                var kette = ElmMonthlyDeclarationBuilder.Anstellung(eigene, anfang, endeDt);
                var em = eigene.Where(v => v.ContractStartDate <= endeDt && (v.ContractEndDate == null || v.ContractEndDate >= anfang))
                             .OrderByDescending(v => v.ContractStartDate).FirstOrDefault()
                         ?? eigene.Where(v => v.ContractStartDate <= endeDt).OrderByDescending(v => v.ContractStartDate).FirstOrDefault();
                if (em == null) continue;
                var zeile = codes.Where(c => c.Art == "BVG" && !string.IsNullOrWhiteSpace(c.Code) && c.EmployeeId == e.Id && c.GiltAm(ende))
                    .OrderByDescending(c => c.ValidFrom).ThenByDescending(c => c.Id).FirstOrDefault();
                var eigenePflicht = pflicht.Where(p => p.EmployeeId == e.Id).ToList();
                bool versichert = zeile != null && (eigenePflicht.Count == 0
                    || eigenePflicht.Any(p => p.Versichert && p.GueltigAb <= ende && (p.GueltigBis == null || p.GueltigBis >= ende)));
                var filiale = em.CompanyProfileId is int cp ? stamm.Filialen.FirstOrDefault(b => b.Id == cp) : null;
                lohnJe.TryGetValue((e.Id, m), out var lohn);
                monate.Add(new E.Monatsstand(m,
                    kette == null ? null : DateOnly.FromDateTime(kette.Value.Start),
                    kette?.Ende is DateTime aus ? DateOnly.FromDateTime(aus) : null,
                    filiale?.KantonCode, versichert, zeile?.Code, zeile?.BvgBasisManuell,
                    Vertragsart(em), Vertrag(em, em.CompanyProfileId is int f && firmen.TryGetValue(f, out var pct) ? pct : 8.33m),
                    lohn ?? LohnAusVertrag(em)));
            }
            stand[e.Id] = new E.Person(
                DateOnly.FromDateTime(e.DateOfBirth.Value), e.Gender,
                e.ExitDate is DateTime tod && string.Equals(e.Austrittsgrund, "TODESFALL", StringComparison.OrdinalIgnoreCase)
                    ? DateOnly.FromDateTime(tod) : null,
                codes.Any(c => c.EmployeeId == e.Id && c.IstAhvSonderfall),
                monate,
                zivil.Where(z => z.EmployeeId == e.Id && z.GueltigAb != null)
                    .Select(z => (z.GueltigAb!.Value, z.ErfahrenAm ?? z.GueltigAb!.Value)).ToList(),
                wohn.Where(w => w.EmployeeId == e.Id && w.GueltigAb != null)
                    .Select(w => (w.GueltigAb!.Value, w.ErfahrenAm ?? w.GueltigAb!.Value)).ToList(),
                manuell.Where(x => x.EmployeeId == e.Id && x.Art != ElmEmaManuell.ArtFit)
                    .Select(x => new E.Manuell(x.Monat, x.Art, x.Grund, x.ValidAsOf)).ToList(),
                jahresmeldungen.SelectMany(j => j.Zeilen.Where(z => z.EmployeeId == e.Id)
                        .Select(z => (Tag: new DateOnly(j.Jahr, 1, 1), z.Betrag)))
                    .ToDictionary(x => x.Tag, x => x.Betrag));
        }
        return new Personen(stamm, mitarbeitende, anstellungen, stand, Steuerung, zivil, wohn, manuell, codes);
    }

    public async Task<Ergebnis> BuildAsync(int year, int month, int? nurFiliale, CancellationToken ct = default)
    {
        var warn = new List<string>();
        var leer = new Ergebnis("", new(), warn, new());
        var geladen = await LadeAsync(year, month, nurFiliale, warn, ct);
        if (geladen == null) return leer;
        var stamm = geladen.Stamm;
        var meldemonat = new DateOnly(year, month, 1);
        var monatsAnfang = meldemonat.ToDateTime(TimeOnly.MinValue);
        var monatsEndeTag = meldemonat.AddMonths(1).AddDays(-1);
        var monatsEnde = monatsEndeTag.ToDateTime(TimeOnly.MinValue);

        var offen = await _db.PayrollPerioden.AsNoTracking()
            .CountAsync(p => p.Year == year && p.Month == month && p.Status != "abgeschlossen", ct);
        if (offen > 0)
            warn.Add($"{month:00}.{year}: {offen} Lohnperiode(n) noch nicht abgeschlossen. Die rückwirkende BVG-Basis rechnet ohne diesen Monat.");

        var empf = await _db.LohndatenEmpfaengers.AsNoTracking().Include(x => x.Zuordnungen)
            .Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(ct);
        var filialIds = stamm.Filialen.Select(b => b.Id).ToHashSet();
        var kanton = stamm.Filialen.ToDictionary(b => b.Id, b => b.KantonCode);
        var ausgewaehlt = nurFiliale is > 0 ? filialIds : null;
        var akE = ElmAnnualDeclarationBuilder.WaehleEmpfaenger(empf.Where(x => x.Art == "AUSGLEICHSKASSE"), monatsEndeTag, ausgewaehlt);
        var uvgE = ElmAnnualDeclarationBuilder.WaehleEmpfaenger(empf.Where(x => x.Art == "UVG"), monatsEndeTag, ausgewaehlt);
        var bvgE = ElmAnnualDeclarationBuilder.WaehleEmpfaenger(empf.Where(x => x.Art == "BVG"), monatsEndeTag, ausgewaehlt);
        var st = await _db.ElmStammdaten.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct);

        var modelle = stamm.Hauptsitz == null ? new List<Arbeitszeitmodell>()
            : await _db.Arbeitszeitmodelle.AsNoTracking().Where(m => m.HauptsitzId == stamm.Hauptsitz.Id && m.IsActive).OrderBy(m => m.Id).ToListAsync(ct);
        var ids = geladen.Stand.Keys.ToList();
        var zuordnungen = modelle.Count == 0 ? new List<EmployeeArbeitszeitmodell>()
            : await _db.EmployeeArbeitszeitmodelle.AsNoTracking().Where(z => ids.Contains(z.EmployeeId)).ToListAsync(ct);
        var plz = geladen.Mitarbeitende.Select(e => (e.ZipCode ?? "").Trim())
            .Concat(geladen.Wohnort.Select(h => (h.Plz ?? "").Trim())).Where(z => z.Length == 4).Distinct().ToList();
        var orte = await _db.SwissLocations.AsNoTracking().Where(l => plz.Contains(l.Plz4))
            .Select(l => new { l.Plz4, l.BfsNr, l.Ortschaftsname, l.Gemeindename }).ToListAsync(ct);
        int? Gemeinde(string? plzRoh, string? ortRoh)
        {
            var z = (plzRoh ?? "").Trim();
            if (z.Length != 4) return null;
            var o = (ortRoh ?? "").Trim().ToLowerInvariant();
            var kand = orte.Where(x => x.Plz4 == z).ToList();
            var best = kand.FirstOrDefault(x => (x.Ortschaftsname ?? "").ToLowerInvariant().StartsWith(o) || (x.Gemeindename ?? "").ToLowerInvariant() == o)?.BfsNr;
            if (best is > 0) return best;
            var nrs = kand.Select(x => x.BfsNr).Distinct().ToList();
            return nrs.Count == 1 ? nrs[0] : null;
        }
        var permits = await _db.EmployeePermitHistories.AsNoTracking().Where(h => ids.Contains(h.EmployeeId)).ToListAsync(ct);
        var permitTypen = await _db.PermitTypes.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);

        var fakKassen = new Dictionary<string, ElmFakAdressierung.Eintrag>(StringComparer.OrdinalIgnoreCase);
        var personen = new List<XElement>();
        var zeilen = new List<Zeile>();
        bool ahvGemeldet = false, bvgGemeldet = false;
        var ohneFrage = new List<string>();

        foreach (var e in geladen.Mitarbeitende)
        {
            if (!geladen.Stand.TryGetValue(e.Id, out var stand)) continue;
            var ereignisse = E.Erkenne(stand, geladen.Steuerung, meldemonat);
            if (ereignisse.Count == 0) continue;
            var eigene = geladen.Anstellungen[e.Id];

            ElmFakAdressierung.Eintrag? Kasse(E.Ereignis x)
            {
                var tag = x.ValidAsOf.ToDateTime(TimeOnly.MinValue);
                var em = eigene.Where(v => v.ContractStartDate <= tag).OrderByDescending(v => v.ContractStartDate).FirstOrDefault();
                return em?.CompanyProfileId is int f
                    ? ElmFakAdressierung.FuerFiliale(empf, f, x.ValidAsOf, filialIds, id => kanton.GetValueOrDefault(id))
                    : null;
            }
            bool Fit(E.Ereignis x)
            {
                if (x.Grund == "deceased") return false;
                var a = geladen.Manuell.FirstOrDefault(m => m.EmployeeId == e.Id && m.Art == ElmEmaManuell.ArtFit && m.ValidAsOf == x.ValidAsOf);
                if (a != null) return a.Grund == "ja";
                var c = x.Art == E.Entry ? BvgVollArbeitsfaehig(e.Id, x.ValidAsOf) : null;
                if (c is bool b) return b;
                ohneFrage.Add($"{e.FirstName} {e.LastName}: {(x.Art == E.Entry ? "Eintritt" : "Austritt")} {x.ValidAsOf:dd.MM.yyyy}");
                return false;
            }
            bool? BvgVollArbeitsfaehig(int id, DateOnly tag) => geladen.Codes
                .Where(c => c.EmployeeId == id && c.Art == "BVG" && c.GiltAm(tag))
                .OrderByDescending(c => c.ValidFrom).Select(c => c.BvgVollArbeitsfaehig).FirstOrDefault();

            // Frühere Anstellungen des Jahres: je eine eigene Work (TF40 Farine _0, _1, _2).
            var jahrStart = new DateTime(year, 1, 1);
            var kette = ElmMonthlyDeclarationBuilder.Anstellung(eigene, monatsAnfang, monatsEnde);
            var vorKetten = new List<(DateTime Start, DateTime Ende)>();
            if (kette is { } akt && akt.Start > jahrStart)
            {
                var vor = akt.Start;
                while (vor > jahrStart)
                {
                    var vk = ElmMonthlyDeclarationBuilder.Anstellung(eigene, jahrStart, vor.AddDays(-1));
                    if (vk is not { Ende: DateTime vkEnde } || vkEnde >= vor || vkEnde < jahrStart) break;
                    vorKetten.Insert(0, (vk.Value.Start, vkEnde));
                    vor = vk.Value.Start;
                }
            }
            string WorkVon(E.Ereignis x)
            {
                var tag = x.ValidAsOf.ToDateTime(TimeOnly.MinValue);
                var i = vorKetten.FindIndex(v => v.Start <= tag && v.Ende >= tag);
                return i >= 0 ? WorkId(e) + "_" + i : WorkId(e);
            }
            string Adressat(E.Ereignis x)
            {
                if (x.Domaene == E.Ahv) { ahvGemeldet = true; return "#AHV"; }
                if (x.Domaene == E.Bvg) { bvgGemeldet = true; return "#BVG"; }
                var k = Kasse(x);
                if (k == null)
                {
                    warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): keine FAK an der Filiale per {x.ValidAsOf:dd.MM.yyyy}. Dort fehlt die FAK-Zeile (Filiale → Lohndaten Empfänger, Art FAK).");
                    return "#FAK";
                }
                fakKassen[k.AddresseeId] = k;
                return k.AddresseeId;
            }

            var zuordnung = zuordnungen.Where(z => z.EmployeeId == e.Id && z.GueltigAb <= monatsEndeTag)
                .OrderByDescending(z => z.GueltigAb).ThenByDescending(z => z.Id).FirstOrDefault();
            var modell = (zuordnung != null ? modelle.FirstOrDefault(m => m.Id == zuordnung.ArbeitszeitmodellId) : null) ?? modelle.FirstOrDefault();
            var emHaupt = eigene.Where(v => v.ContractStartDate <= monatsEnde && (v.ContractEndDate == null || v.ContractEndDate >= monatsAnfang))
                              .OrderByDescending(v => v.ContractStartDate).FirstOrDefault()
                          ?? eigene.OrderByDescending(v => v.ContractStartDate).FirstOrDefault();
            var vollzeit = modell?.Wochenstunden is > 0m ? modell.Wochenstunden!.Value : stamm.Haupt.NormalWeeklyHours ?? 42m;

            var (ziv, zivSeit, _) = ZivilstandHistorieService.Am(geladen.Zivilstand.Where(h => h.EmployeeId == e.Id), monatsEndeTag, e.MaritalStatus, e.MaritalStatusSince);
            var wohnHist = geladen.Wohnort.Where(h => h.EmployeeId == e.Id).ToList();
            var wohnAm = WohnortHistorieService.Am(wohnHist, monatsEndeTag, e.Street, e.ZipCode, e.City, e.CantonCode, e.Country);
            var adresseAm = new PersonAdresseAm(wohnAm.Strasse, wohnAm.Plz, wohnAm.Ort, wohnAm.Land, wohnAm.KantonCode);
            var umzug = WohnortHistorieService.UmzugImMonat(wohnHist, monatsEndeTag.AddYears(-1).AddDays(1), monatsEndeTag);
            PersonAdresseAm? adresseVorher = umzug is { } u ? new PersonAdresseAm(u.Alt.Strasse, u.Alt.Plz, u.Alt.Ort, u.Alt.Land, u.Alt.KantonCode) : null;
            var permitId = ElmMonthlyDeclarationBuilder.BewilligungAmStichtag(permits.Where(h => h.EmployeeId == e.Id), monatsEndeTag);
            var permitAmEnde = permitId != null && permitTypen.TryGetValue(permitId.Value, out var pt) ? pt : null;

            var persZeilen = ereignisse.OrderBy(x => x.Domaene == E.Ahv ? 0 : x.Domaene == E.Fak ? 1 : 2).ToList();
            var bloecke = E.Bloecke(persZeilen, Adressat, WorkVon, Fit).ToList();
            foreach (var x in persZeilen)
                zeilen.Add(new Zeile(e.Id, $"{e.FirstName} {e.LastName}".Trim(), e.EmployeeNumber, x,
                    x.Domaene == E.Bvg && x.Art != E.Mutation ? Fit(x) : null));

            personen.Add(new XElement(Sd + "Person",
                Particulars(e, warn, Gemeinde(adresseAm.Zip, adresseAm.City), permitAmEnde, ziv, zivSeit, adresseAm, adresseVorher, umzug?.Wegzug,
                    adresseVorher == null ? null : Gemeinde(adresseVorher.Zip, adresseVorher.City)),
                vorKetten.Select((vk, i) => new XElement(C + "Work",
                    new XAttribute("workID", WorkId(e) + "_" + i),
                    modell != null ? new XAttribute("companyWorkingTimeIDRef", "#" + modell.KennungOderId) : null,
                    new XElement(C + "WorkingTime", WorkingTime(
                        eigene.Where(m => m.ContractStartDate <= vk.Ende).OrderByDescending(m => m.ContractStartDate).FirstOrDefault() ?? emHaupt,
                        vollzeit, zuordnung?.AbweichendeWochenstunden, zuordnung?.AbweichendeWochenlektionen, modell?.Wochenlektionen)),
                    new XElement(C + "EntryDate", vk.Start.ToString("yyyy-MM-dd")),
                    new XElement(C + "WithdrawalDate", vk.Ende.ToString("yyyy-MM-dd")))),
                new XElement(C + "Work",
                    new XAttribute("workID", WorkId(e)),
                    modell != null ? new XAttribute("companyWorkingTimeIDRef", "#" + modell.KennungOderId) : null,
                    new XElement(C + "WorkingTime", WorkingTime(emHaupt, vollzeit, zuordnung?.AbweichendeWochenstunden, zuordnung?.AbweichendeWochenlektionen, modell?.Wochenlektionen)),
                    new XElement(C + "EntryDate", (kette?.Start ?? emHaupt?.ContractStartDate ?? e.EntryDate ?? monatsAnfang).ToString("yyyy-MM-dd")),
                    kette?.Ende is DateTime wd && wd <= monatsEnde ? new XElement(C + "WithdrawalDate", wd.ToString("yyyy-MM-dd")) : null),
                bloecke));
        }

        if (personen.Count == 0)
        {
            warn.Add($"Keine EMA-Ereignisse für {month:00}.{year}.");
            return leer;
        }
        if (ohneFrage.Count > 0)
            warn.Add($"100-%-Arbeitsfähigkeit noch nicht beantwortet, ohne FullyFitForWork gemeldet (RL 8.6.8.2): {string.Join("; ", ohneFrage.Distinct())}.");

        var addressees = new List<XElement>();
        XElement Adr(string id, string? nr) => new(Sdc + "Addressee", new XAttribute("addresseeID", id),
            new XElement(Ep + "AddresseeIdentification", (nr ?? "").Trim()),
            new XElement(Ep + "ProcessByDistributor", "true"));
        var institutions = new List<XElement>();
        string? Mitglied(LohndatenEmpfaenger? x) => x?.Zuordnungen
            .Where(z => z.GiltAm(monatsEndeTag) && filialIds.Contains(z.CompanyProfileId))
            .Select(z => (z.Mitgliednummer ?? "").Trim()).FirstOrDefault(v => v.Length > 0);
        if (ahvGemeldet)
        {
            if (akE == null) warn.Add("Keine Ausgleichskasse im Empfänger-Katalog: AHV-Ereignisse ohne Adressat.");
            addressees.Add(Adr("#AHV", akE?.Kassennummer));
            institutions.Add(new XElement(Sd + "AHV-AVS", new XAttribute("addresseeIDRef", "#AHV"),
                new XElement(Sd + "AK-CC-CustomerNumber", Mitglied(akE) ?? (akE?.Kassennummer ?? "").Trim()),
                ElmAnnualDeclarationBuilder.InsuranceBlock("UVG-LAA-Insurance", uvgE?.Bezeichnung, uvgE?.UidNummer, st?.UvgVersichertSeit, "Keine UVG-Versicherung erfasst"),
                ElmAnnualDeclarationBuilder.InsuranceBlock("BVG-LPP-Insurance", bvgE?.Bezeichnung, bvgE?.UidNummer, st?.BvgVersichertSeit, "Keine BVG-Versicherung erfasst")));
        }
        foreach (var k in fakKassen.Values.OrderBy(k => k.Kassennummer))
        {
            addressees.Add(ElmFakAdressierung.JobAddressee(k));
            institutions.Add(ElmFakAdressierung.Institution(k));
        }
        if (bvgGemeldet)
        {
            if (bvgE == null) warn.Add("Keine Vorsorgeeinrichtung im Empfänger-Katalog: BVG-Ereignisse ohne Adressat.");
            addressees.Add(Adr("#BVG", bvgE?.Kassennummer));
            var zuordnung = bvgE?.Zuordnungen.FirstOrDefault(z => z.GiltAm(monatsEndeTag) && filialIds.Contains(z.CompanyProfileId));
            institutions.Add(new XElement(Sd + "BVG-LPP", new XAttribute("addresseeIDRef", "#BVG"),
                new XElement(Sd + "InsuranceCompanyName", bvgE?.Bezeichnung ?? ""),
                new XElement(Sd + "CustomerIdentity", (zuordnung?.Mitgliednummer ?? "").Trim()),
                new XElement(Sd + "ContractIdentity", (zuordnung?.Subnummer ?? "").Trim()),
                new XElement(Sd + "GeneralValidAsOf", new DateOnly(year, 1, 1).ToString("yyyy-MM-dd"))));
        }

        // Arbeitsorte wie in der Monatsmeldung (Rückblick, docs/swissdec-abgeleitete-regeln.md Nr. 1).
        var rueckblick = await (from sn in _db.PayrollSnapshots.AsNoTracking()
                                join pp in _db.PayrollPerioden.AsNoTracking() on sn.PayrollPeriodeId equals pp.Id
                                where sn.Status != "STORNIERT" && filialIds.Contains(pp.CompanyProfileId)
                                      && pp.Year >= year - 1 && pp.Year <= year
                                select new { pp.CompanyProfileId, pp.Year, pp.Month }).Distinct().ToListAsync(ct);
        var arbeitsorte = rueckblick.Where(x => (x.Year == year && x.Month == month)
                || ElmMonthlyDeclarationBuilder.ImArbeitsortRueckblick(x.Year, x.Month, year, month))
            .Select(x => x.CompanyProfileId).ToHashSet();
        var arbeitszeit = ElmMonthlyDeclarationBuilder.ArbeitszeitModelle(modelle, zuordnungen, ids, stamm.Haupt.NormalWeeklyHours ?? 42m, warn);

        var kontaktName = (stamm.Hauptsitz?.KontaktName ?? "").Trim();
        if (kontaktName.Length == 0)
        {
            warn.Add("Keine Kontaktperson am Hauptsitz erfasst. Erfassung: System → Hauptsitze → Kontaktperson für Lohnmeldungen.");
            kontaktName = stamm.Firmenname;
        }
        var kontaktMail = (stamm.Hauptsitz?.KontaktEmail ?? "").Trim();
        var kontaktTel = PersonaldatenPruefung.TelefonFuerSwissdec((stamm.Hauptsitz?.KontaktTelefon ?? "").Trim());
        var kontaktMobil = PersonaldatenPruefung.TelefonFuerSwissdec((stamm.Hauptsitz?.KontaktMobil ?? "").Trim());

        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Sdcst + "NotifyChanges",
                new XAttribute(XNamespace.Xmlns + "sdcst", Sdcst),
                new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
                new XAttribute(XNamespace.Xmlns + "sd", Sd),
                new XAttribute(XNamespace.Xmlns + "ep", Ep),
                new XAttribute(XNamespace.Xmlns + "c", C),
                RequestContext(stamm.Firmenname, DateTime.Now, _einstellungen),
                new XElement(Sdc + "Job", new XElement(Sdc + "Addressees", addressees)),
                new XElement(Sd + "ChangeNotification",
                    new XAttribute("schemaVersion", "0.0"),
                    CompanyDescription(ElmMonthlyDeclarationBuilder.NurArbeitsorteMitLohn(stamm, arbeitsorte), arbeitszeit),
                    new XElement(Sd + "Staff", personen),
                    new XElement(Sd + "Institutions", institutions),
                    new XElement(Sd + "ContactPerson",
                        new XElement(Sd + "Name", kontaktName),
                        kontaktMail.Length == 0 ? null : new XElement(Sd + "EmailAddress", kontaktMail),
                        kontaktTel == null ? null : new XElement(Sd + "PhoneNumber", kontaktTel),
                        kontaktMobil == null ? null : new XElement(Sd + "MobilePhoneNumber", kontaktMobil)))));

        var xml = doc.Declaration + Environment.NewLine + doc.ToString();
        return new Ergebnis(xml, zeilen, warn, _validator.Validate(xml));
    }

    /// <summary>H, M oder N: Ein Wechsel beginnt die Rückschau der BVG-Basis neu (RL 8.6.6.2).</summary>
    public static char Vertragsart(Employment em)
    {
        var art = (em.SwissdecVertragsart ?? "").Trim();
        if (art.Contains("NoTimeConstraint", StringComparison.OrdinalIgnoreCase) || art == "administrativeBoard") return 'N';
        return IstMonatslohnModell(em.EmploymentModel) ? 'M' : 'H';
    }

    public static ElmBvgBasis.Vertrag Vertrag(Employment em, decimal prozent13Firma)
    {
        var art = Vertragsart(em);
        bool stundenlohn = art == 'H';
        bool fest = stundenlohn && em.Regelmaessig != false && (em.GuaranteedHoursPerWeek is > 0m || em.WeeklyLessons is > 0m);
        return new ElmBvgBasis.Vertrag(stundenlohn, fest, em.GuaranteedHoursPerWeek, em.WeeklyLessons, em.HourlyRate, em.LessonRate,
            stundenlohn && em.ThirteenthSalary ? prozent13Firma : null,
            art == 'M' && em.ThirteenthSalary, art == 'M' && em.VierzehnterMonatslohn);
    }

    /// <summary>Noch kein abgeschlossener Lauf im Monat: der Monatslohn des Vertrags per Wirkungsdatum (RL 8.6.6.1).</summary>
    private static Dictionary<string, decimal> LohnAusVertrag(Employment em)
    {
        var res = new Dictionary<string, decimal>();
        if (Vertragsart(em) == 'M' && em.MonthlySalary is > 0m) res["1000"] = em.MonthlySalary.Value;
        return res;
    }

    private static IEnumerable<(string Code, decimal Betrag)> LohnZeilen(string? slipJson)
    {
        if (string.IsNullOrWhiteSpace(slipJson)) yield break;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(slipJson); }
        catch (JsonException) { yield break; }
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("lohnLines", out var ll) || ll.ValueKind != JsonValueKind.Array) yield break;
            foreach (var z in ll.EnumerateArray())
            {
                var code = z.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? (c.GetString() ?? "").Trim() : "";
                var betrag = z.TryGetProperty("betrag", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetDecimal() : 0m;
                if (code.Length > 0 && betrag != 0m) yield return (code, betrag);
            }
        }
    }
}
