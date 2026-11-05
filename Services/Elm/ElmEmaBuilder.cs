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

    /// <summary><paramref name="FrageOffen"/>: 100-%-Arbeitsfähigkeit noch nicht beantwortet (RL 8.6.8.2).</summary>
    public sealed record Zeile(int EmployeeId, string Name, string? PersonalNr, E.Ereignis Ereignis, bool? Arbeitsfaehig, bool FrageOffen = false);

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
        var stamm = await LadeRechtseinheitAsync(_db, warn, ct, nurFiliale);
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

        // Auch die Verträge nach dem Meldemonat: Sonst sieht jeder Vertragswechsel wie ein Austritt aus (TF11 Bosshard Mai 2025).
        var vertraege = await _db.Employments.AsNoTracking()
            .Where(v => v.CompanyProfileId != null && filialIds.Contains(v.CompanyProfileId.Value))
            .ToListAsync(ct);
        var mitVertragImMonat = vertraege.Where(v => v.ContractStartDate <= bisEnde).Select(v => v.EmployeeId).ToHashSet();
        vertraege = vertraege.Where(v => mitVertragImMonat.Contains(v.EmployeeId)).ToList();
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
        var zeitmodelle = await _db.EmployeeArbeitszeitmodelle.AsNoTracking().Where(z => ids.Contains(z.EmployeeId)).ToListAsync(ct);
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

        // Bei «Je Filiale» zählen für einen Folgevertrag auch die Verträge der anderen Filialen.
        var alleVertraege = (stamm.JeFiliale
                ? await _db.Employments.AsNoTracking().Where(v => ids.Contains(v.EmployeeId)).ToListAsync(ct)
                : vertraege)
            .GroupBy(v => v.EmployeeId).ToDictionary(g => g.Key, g => (IReadOnlyList<Employment>)g.ToList());
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
            // Der Folgemonat zeigt, ob die Versicherung per Monatsende endet (TF05 Moser). Gemeldet wird nur der Meldemonat.
            for (var m = start; m <= bis.AddMonths(1); m = m.AddMonths(1))
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
                var zeit = zeitmodelle.Where(z => z.EmployeeId == e.Id && z.GueltigAb <= ende)
                    .OrderByDescending(z => z.GueltigAb).ThenByDescending(z => z.Id).FirstOrDefault();
                monate.Add(new E.Monatsstand(m,
                    kette == null ? null : DateOnly.FromDateTime(kette.Value.Start),
                    kette?.Ende is DateTime aus ? DateOnly.FromDateTime(aus) : null,
                    filiale?.KantonCode, versichert, zeile?.Code, zeile?.BvgBasisManuell,
                    Vertragsart(em), Vertrag(em, em.CompanyProfileId is int f && firmen.TryGetValue(f, out var pct) ? pct : 8.33m,
                        zeit?.AbweichendeWochenstunden, zeit?.AbweichendeWochenlektionen),
                    MitVertragslohn(lohn, em)));
            }
            stand[e.Id] = new E.Person(
                DateOnly.FromDateTime(e.DateOfBirth.Value), e.Gender,
                Todestag(e, eigene),
                codes.Any(c => c.EmployeeId == e.Id && c.IstAhvSonderfall),
                monate,
                zivil.Where(z => z.EmployeeId == e.Id && z.GueltigAb != null
                        && (!stamm.JeFiliale || GiltInFiliale(eigene, Meldetag(z.GueltigAb!.Value, z.ErfahrenAm ?? z.GueltigAb!.Value))))
                    .Select(z => (z.GueltigAb!.Value, z.ErfahrenAm ?? z.GueltigAb!.Value)).ToList(),
                wohn.Where(w => w.EmployeeId == e.Id && w.GueltigAb != null
                        && (!stamm.JeFiliale || GiltInFiliale(eigene, Meldetag(w.GueltigAb!.Value, w.ErfahrenAm ?? w.GueltigAb!.Value))))
                    .Select(w => (w.GueltigAb!.Value, w.ErfahrenAm ?? w.GueltigAb!.Value)).ToList(),
                manuell.Where(x => x.EmployeeId == e.Id && x.Art != ElmEmaManuell.ArtFit)
                    .Where(x => !stamm.JeFiliale || x.Art != E.Mutation || GiltInFiliale(eigene, x.ValidAsOf ?? x.Monat))
                    .Select(x => new E.Manuell(x.Monat, x.Art, x.Grund, x.ValidAsOf))
                    .Concat(UnterbruecheAusVertraegen(eigene, alleVertraege.GetValueOrDefault(e.Id)))
                    .Concat(TeilpensionierungenAusVertraegen(eigene)).ToList(),
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

        var kopf = await LadeKopfdatenAsync(geladen, ct);

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
                return Antwort(x, a) == true;
            }
            bool? Antwort(E.Ereignis x, ElmEmaManuell? a)
            {
                if (a != null) return a.Grund == "ja";
                if (x.Art == E.Entry) return BvgVollArbeitsfaehig(e.Id, x.ValidAsOf);
                var tag = x.ValidAsOf.ToDateTime(TimeOnly.MinValue);
                return eigene.FirstOrDefault(v => v.ContractEndDate?.Date == tag)?.EndeVollArbeitsfaehig;
            }
            bool FrageOffen(E.Ereignis x)
            {
                if (x.Domaene != E.Bvg || x.Art == E.Mutation || x.Grund == "deceased") return false;
                if (Antwort(x, geladen.Manuell.FirstOrDefault(m => m.EmployeeId == e.Id && m.Art == ElmEmaManuell.ArtFit && m.ValidAsOf == x.ValidAsOf)) != null) return false;
                ohneFrage.Add($"{e.FirstName} {e.LastName}: {(x.Art == E.Entry ? "Eintritt" : "Austritt")} {x.ValidAsOf:dd.MM.yyyy}");
                return true;
            }
            bool? BvgVollArbeitsfaehig(int id, DateOnly tag) => geladen.Codes
                .Where(c => c.EmployeeId == id && c.Art == "BVG" && c.GiltAm(tag))
                .OrderByDescending(c => c.ValidFrom).Select(c => c.BvgVollArbeitsfaehig).FirstOrDefault();

            var (kopfInhalt, vorKetten) = PersonKopf(e, geladen, kopf, year, monatsEndeTag, warn);
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
                // Eine Kasse kann mehrere Arbeitsorte haben (TF15 Degelo: ZG über die Kasse LU), keiner darf verloren gehen.
                fakKassen[k.AddresseeId] = fakKassen.TryGetValue(k.AddresseeId, out var bisher)
                    ? bisher with { Kantone = bisher.Kantone.Concat(k.Kantone).Distinct().ToList() }
                    : k;
                return k.AddresseeId;
            }

            var persZeilen = ereignisse.OrderBy(x => x.Domaene == E.Ahv ? 0 : x.Domaene == E.Fak ? 1 : 2).ToList();
            var bloecke = E.Bloecke(persZeilen, Adressat, WorkVon, Fit).ToList();
            foreach (var x in persZeilen)
                zeilen.Add(new Zeile(e.Id, $"{e.FirstName} {e.LastName}".Trim(), e.EmployeeNumber, x,
                    x.Domaene == E.Bvg && x.Art != E.Mutation ? Fit(x) : null, FrageOffen(x)));

            personen.Add(new XElement(Sd + "Person", kopfInhalt, bloecke));
        }

        if (personen.Count == 0)
        {
            warn.Add($"Keine EMA-Ereignisse für {month:00}.{year}.");
            return leer;
        }
        if (ohneFrage.Count > 0)
            warn.Add($"100-%-Arbeitsfähigkeit noch nicht beantwortet, so kann die Meldung nicht gesendet werden (RL 8.6.8.2): {string.Join("; ", ohneFrage.Distinct())}.");

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
            addressees.Add(Adr("#AHV", ElmFakAdressierung.KasseFuerMeldung(akE?.Kassennummer)));
            institutions.Add(new XElement(Sd + "AHV-AVS", new XAttribute("addresseeIDRef", "#AHV"),
                new XElement(Sd + "AK-CC-CustomerNumber", Mitglied(akE) ?? (akE?.Kassennummer ?? "").Trim()),
                ElmAnnualDeclarationBuilder.InsuranceBlock("UVG-LAA-Insurance", uvgE?.Bezeichnung, uvgE?.UidNummer, st?.UvgVersichertSeit, "Keine UVG-Versicherung erfasst"),
                ElmAnnualDeclarationBuilder.InsuranceBlock("BVG-LPP-Insurance", bvgE?.Bezeichnung, bvgE?.UidNummer, st?.BvgVersichertSeit, "Keine BVG-Versicherung erfasst")));
        }
        foreach (var k in KassenNachArbeitsort(fakKassen.Values, stamm.Filialen.Select(b => b.KantonCode).ToList()))
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

        var arbeitsorte = await ArbeitsorteAsync(filialIds, year, month, ct);
        var arbeitszeit = ElmMonthlyDeclarationBuilder.ArbeitszeitModelle(kopf.Modelle, kopf.Zuordnungen, geladen.Stand.Keys, stamm.Haupt.NormalWeeklyHours ?? 42m, warn);

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
                    new XElement(Sd + "ContactPerson", Kontakt(stamm, warn)))));

        var xml = doc.Declaration + Environment.NewLine + doc.ToString();
        var fehler = _validator.Validate(xml);
        // Swissdec 8.1.3 / 8.2.3: Der Katalog lehnt «000.003» ab, alte Einträge dürfen trotzdem nicht hinaus.
        if (ahvGemeldet && AusgleichskasseNummer.Fehler(akE?.Kassennummer, AusgleichskasseNummer.BegriffAhv) is { } ahvFehler) fehler.Add(ahvFehler);
        fehler.AddRange(fakKassen.Values.Select(k => AusgleichskasseNummer.Fehler(k.Kassennummer, AusgleichskasseNummer.BegriffFak))
            .OfType<string>().Distinct());
        return new Ergebnis(xml, zeilen, warn, fehler);
    }

    /// <summary>
    /// Voraussichtliche BVG-Jahresmeldung (YearlyProspective): wer am 1.1. versichert ist, mit der Basis per
    /// GeneralValidAsOf (RL 8.6.6). Dieselbe Rechnung wie die EMA.
    /// </summary>
    public async Task<Ergebnis> BuildProspectiveAsync(int year, int? nurFiliale, CancellationToken ct = default)
    {
        var warn = new List<string>();
        var leer = new Ergebnis("", new(), warn, new());
        var geladen = await LadeAsync(year, 1, nurFiliale, warn, ct);
        if (geladen == null) return leer;
        var stamm = geladen.Stamm;
        var stichtag = new DateOnly(year, 1, 1);
        var kopf = await LadeKopfdatenAsync(geladen, ct);
        var personen = new List<XElement>();
        foreach (var z in ProspectiveZeilen(geladen, stichtag, warn))
        {
            var (kopfInhalt, _) = PersonKopf(z.Mitarbeiter, geladen, kopf, year, stichtag.AddMonths(1).AddDays(-1), warn);
            personen.Add(new XElement(Sd + "Person", kopfInhalt,
                new XElement(Sd + "BVG-LPP-Salaries",
                    new XElement(Sd + "BVG-LPP-Salary",
                        new XAttribute("addresseeIDRef", "#BVG"),
                        new XAttribute("workIDRef", WorkId(z.Mitarbeiter)),
                        new XElement(Sd + "BVG-LPP-Code", z.Code),
                        new XElement(Sd + "BVG-LPP-AnnualBasis", Amt(z.Basis))))));
        }
        if (personen.Count == 0)
        {
            warn.Add($"Am 01.01.{year} ist niemand im BVG versichert.");
            return leer;
        }

        var filialIds = stamm.Filialen.Select(b => b.Id).ToHashSet();
        var empf = await _db.LohndatenEmpfaengers.AsNoTracking().Include(x => x.Zuordnungen)
            .Where(x => x.IsActive).OrderBy(x => x.Id).ToListAsync(ct);
        var bvgE = ElmAnnualDeclarationBuilder.WaehleEmpfaenger(empf.Where(x => x.Art == "BVG"), stichtag, nurFiliale is > 0 ? filialIds : null);
        if (bvgE == null) warn.Add("Keine Vorsorgeeinrichtung im Empfänger-Katalog: Meldung ohne Adressat.");
        var zuordnung = bvgE?.Zuordnungen.FirstOrDefault(z => z.GiltAm(stichtag) && filialIds.Contains(z.CompanyProfileId));
        var arbeitsorte = await ArbeitsorteAsync(filialIds, year, 1, ct);
        var arbeitszeit = ElmMonthlyDeclarationBuilder.ArbeitszeitModelle(kopf.Modelle, kopf.Zuordnungen, geladen.Stand.Keys, stamm.Haupt.NormalWeeklyHours ?? 42m, warn);
        var jetzt = DateTime.Now;
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Sdcst + "DeclareAnnualSalary",
                new XAttribute(XNamespace.Xmlns + "sdcst", Sdcst),
                new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
                new XAttribute(XNamespace.Xmlns + "sd", Sd),
                new XAttribute(XNamespace.Xmlns + "ep", Ep),
                new XAttribute(XNamespace.Xmlns + "c", C),
                RequestContext(stamm.Firmenname, jetzt, _einstellungen),
                new XElement(Sdc + "Job", new XElement(Sdc + "Addressees",
                    new XElement(Sdc + "Addressee", new XAttribute("addresseeID", "#BVG"),
                        new XElement(Ep + "AddresseeIdentification", (bvgE?.Kassennummer ?? "").Trim()),
                        new XElement(Ep + "ProcessByDistributor", "true")))),
                new XElement(Sd + "AnnualSalaryDeclaration",
                    new XAttribute("schemaVersion", "0.0"),
                    CompanyDescription(ElmMonthlyDeclarationBuilder.NurArbeitsorteMitLohn(stamm, arbeitsorte), arbeitszeit),
                    new XElement(Sd + "Staff", personen),
                    new XElement(Sd + "Institutions",
                        new XElement(Sd + "BVG-LPP", new XAttribute("addresseeIDRef", "#BVG"),
                            new XElement(Sd + "InsuranceCompanyName", bvgE?.Bezeichnung ?? ""),
                            new XElement(Sd + "CustomerIdentity", (zuordnung?.Mitgliednummer ?? "").Trim()),
                            new XElement(Sd + "ContractIdentity", (zuordnung?.Subnummer ?? "").Trim()),
                            new XElement(Sd + "GeneralValidAsOf", stichtag.ToString("yyyy-MM-dd")))),
                    new XElement(Sd + "SalaryTotals"),
                    new XElement(Sd + "SalaryCounters", new XElement(Sd + "NumberOf-BVG-LPP-Salary-Tags", personen.Count)),
                    new XElement(Sd + "GeneralSalaryDeclarationDescription",
                        new XElement(Sd + "CreationDate", jetzt.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz")),
                        new XElement(Sd + "AccountingPeriod", year),
                        new XElement(Sd + "ContactPerson", Kontakt(stamm, warn))))));
        var xml = doc.Declaration + Environment.NewLine + doc.ToString();
        return new Ergebnis(xml, new(), warn, _validator.Validate(xml));
    }

    public sealed record ProspectiveZeile(Employee Mitarbeiter, string? Code, decimal Gerechnet, decimal Basis);

    /// <summary>Wer am 1.1. versichert ist, mit der Basis nach RL 8.6.6. Auch der Vorschlag im Cockpit.</summary>
    public async Task<List<ProspectiveZeile>> ProspectiveZeilenAsync(int year, int? nurFiliale, CancellationToken ct = default)
    {
        var warn = new List<string>();
        var geladen = await LadeAsync(year, 1, nurFiliale, warn, ct);
        return geladen == null ? new() : ProspectiveZeilen(geladen, new DateOnly(year, 1, 1), warn);
    }

    private static List<ProspectiveZeile> ProspectiveZeilen(Personen geladen, DateOnly stichtag, List<string> warn)
    {
        var res = new List<ProspectiveZeile>();
        foreach (var e in geladen.Mitarbeitende)
        {
            if (!geladen.Stand.TryGetValue(e.Id, out var stand)) continue;
            var ohneMeldung = stand with { Jahresmeldungen = new Dictionary<DateOnly, decimal>() };
            if (E.BvgPeriodeAm(E.Erkenne(ohneMeldung, geladen.Steuerung), stichtag) is not { } ab) continue;
            var s = stand.Monate.FirstOrDefault(m => m.Monat == stichtag);
            if (s == null) continue;
            var laeufe = stand.Monate.Select(m => new ElmBvgBasis.LohnMonat(m.Monat, m.Lohnarten));
            var gerechnet = ElmBvgBasis.FuerJahresmeldung(stichtag, s.ManuelleBasis, s.Vertrag, s.Lohnarten, laeufe, ab, geladen.Steuerung);
            var basis = gerechnet;
            // Der Anwender prüft die Basen und korrigiert sie vor der Übermittlung (RL 8.6.6): Freigegeben geht vor.
            if (stand.Jahresmeldungen.TryGetValue(stichtag, out var freigegeben))
            {
                if (freigegeben != gerechnet)
                    warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): freigegebene BVG-Basis {Amt(freigegeben)} statt gerechnet {Amt(gerechnet)}.");
                basis = freigegeben;
            }
            res.Add(new ProspectiveZeile(e, s.BvgCode, gerechnet, basis));
        }
        return res;
    }

    private sealed record Kopfdaten(
        List<Arbeitszeitmodell> Modelle, List<EmployeeArbeitszeitmodell> Zuordnungen, Func<string?, string?, int?> Gemeinde,
        List<EmployeePermitHistory> Permits, Dictionary<int, PermitType> PermitTypen);

    private async Task<Kopfdaten> LadeKopfdatenAsync(Personen geladen, CancellationToken ct)
    {
        var stamm = geladen.Stamm;
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
        return new Kopfdaten(modelle, zuordnungen, Gemeinde, permits, permitTypen);
    }

    /// <summary>Particulars und Work einer Person am Stichtag, für EMA und Prospective.</summary>
    private static (List<XElement> Inhalt, List<(DateTime Start, DateTime Ende)> VorKetten) PersonKopf(
        Employee e, Personen geladen, Kopfdaten k, int year, DateOnly monatsEndeTag, List<string> warn)
    {
        var monatsEnde = monatsEndeTag.ToDateTime(TimeOnly.MinValue);
        var monatsAnfang = new DateTime(monatsEnde.Year, monatsEnde.Month, 1);
        var eigene = geladen.Anstellungen[e.Id];
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
        var zuordnung = k.Zuordnungen.Where(z => z.EmployeeId == e.Id && z.GueltigAb <= monatsEndeTag)
            .OrderByDescending(z => z.GueltigAb).ThenByDescending(z => z.Id).FirstOrDefault();
        var modell = (zuordnung != null ? k.Modelle.FirstOrDefault(m => m.Id == zuordnung.ArbeitszeitmodellId) : null) ?? k.Modelle.FirstOrDefault();
        var emHaupt = eigene.Where(v => v.ContractStartDate <= monatsEnde && (v.ContractEndDate == null || v.ContractEndDate >= monatsAnfang))
                          .OrderByDescending(v => v.ContractStartDate).FirstOrDefault()
                      ?? eigene.OrderByDescending(v => v.ContractStartDate).FirstOrDefault();
        var vollzeit = modell?.Wochenstunden is > 0m ? modell.Wochenstunden!.Value : geladen.Stamm.Haupt.NormalWeeklyHours ?? 42m;

        var (ziv, zivSeit, _) = ZivilstandHistorieService.Am(geladen.Zivilstand.Where(h => h.EmployeeId == e.Id), monatsEndeTag, e.MaritalStatus, e.MaritalStatusSince);
        // Ledig ohne Datum: seit Geburt (TF36 Maldini Januar 2025, Eintrag «seit jeher»).
        if (zivSeit == null && MapCivilStatus(ziv) == "single") zivSeit = DateOnly.FromDateTime(e.DateOfBirth!.Value);
        var wohnHist = geladen.Wohnort.Where(h => h.EmployeeId == e.Id).ToList();
        var wohnAm = WohnortHistorieService.Am(wohnHist, monatsEndeTag, e.Street, e.ZipCode, e.City, e.CantonCode, e.Country);
        var adresseAm = new PersonAdresseAm(wohnAm.Strasse, wohnAm.Plz, wohnAm.Ort, wohnAm.Land, wohnAm.KantonCode);
        var umzug = WohnortHistorieService.UmzugImMonat(wohnHist, monatsEndeTag.AddYears(-1).AddDays(1), monatsEndeTag);
        PersonAdresseAm? adresseVorher = umzug is { } u ? new PersonAdresseAm(u.Alt.Strasse, u.Alt.Plz, u.Alt.Ort, u.Alt.Land, u.Alt.KantonCode) : null;
        var permitId = ElmMonthlyDeclarationBuilder.BewilligungAmStichtag(k.Permits.Where(h => h.EmployeeId == e.Id), monatsEndeTag);
        var permitAmEnde = permitId != null && k.PermitTypen.TryGetValue(permitId.Value, out var pt) ? pt : null;

        var inhalt = new List<XElement>
        {
            Particulars(e, warn, k.Gemeinde(adresseAm.Zip, adresseAm.City), permitAmEnde, ziv, zivSeit, adresseAm, adresseVorher, umzug?.Wegzug,
                adresseVorher == null ? null : k.Gemeinde(adresseVorher.Zip, adresseVorher.City)),
        };
        inhalt.AddRange(vorKetten.Select((vk, i) => new XElement(C + "Work",
            new XAttribute("workID", WorkId(e) + "_" + i),
            modell != null ? new XAttribute("companyWorkingTimeIDRef", "#" + modell.KennungOderId) : null,
            new XElement(C + "WorkingTime", WorkingTime(
                eigene.Where(m => m.ContractStartDate <= vk.Ende).OrderByDescending(m => m.ContractStartDate).FirstOrDefault() ?? emHaupt,
                vollzeit, zuordnung?.AbweichendeWochenstunden, zuordnung?.AbweichendeWochenlektionen, modell?.Wochenlektionen)),
            new XElement(C + "EntryDate", vk.Start.ToString("yyyy-MM-dd")),
            new XElement(C + "WithdrawalDate", vk.Ende.ToString("yyyy-MM-dd")))));
        inhalt.Add(new XElement(C + "Work",
            new XAttribute("workID", WorkId(e)),
            modell != null ? new XAttribute("companyWorkingTimeIDRef", "#" + modell.KennungOderId) : null,
            new XElement(C + "WorkingTime", WorkingTime(emHaupt, vollzeit, zuordnung?.AbweichendeWochenstunden, zuordnung?.AbweichendeWochenlektionen, modell?.Wochenlektionen)),
            new XElement(C + "EntryDate", (kette?.Start ?? emHaupt?.ContractStartDate ?? e.EntryDate ?? monatsAnfang).ToString("yyyy-MM-dd")),
            kette?.Ende is DateTime wd && wd <= monatsEnde ? new XElement(C + "WithdrawalDate", wd.ToString("yyyy-MM-dd")) : null));
        return (inhalt, vorKetten);
    }

    /// <summary>Arbeitsorte wie in der Monatsmeldung (Rückblick, docs/swissdec-abgeleitete-regeln.md Nr. 1).</summary>
    private async Task<HashSet<int>> ArbeitsorteAsync(HashSet<int> filialIds, int year, int month, CancellationToken ct)
    {
        var rueckblick = await (from sn in _db.PayrollSnapshots.AsNoTracking()
                                join pp in _db.PayrollPerioden.AsNoTracking() on sn.PayrollPeriodeId equals pp.Id
                                where sn.Status != "STORNIERT" && filialIds.Contains(pp.CompanyProfileId)
                                      && pp.Year >= year - 1 && pp.Year <= year
                                select new { pp.CompanyProfileId, pp.Year, pp.Month }).Distinct().ToListAsync(ct);
        return rueckblick.Where(x => (x.Year == year && x.Month == month)
                || ElmMonthlyDeclarationBuilder.ImArbeitsortRueckblick(x.Year, x.Month, year, month))
            .Select(x => x.CompanyProfileId).ToHashSet();
    }

    private static IEnumerable<XElement?> Kontakt(RechtseinheitStamm stamm, List<string> warn)
    {
        var name = (stamm.Hauptsitz?.KontaktName ?? "").Trim();
        if (name.Length == 0)
        {
            warn.Add("Keine Kontaktperson am Hauptsitz erfasst. Erfassung: System → Hauptsitze → Kontaktperson für Lohnmeldungen.");
            name = stamm.Firmenname;
        }
        var mail = (stamm.Hauptsitz?.KontaktEmail ?? "").Trim();
        var tel = PersonaldatenPruefung.TelefonFuerSwissdec((stamm.Hauptsitz?.KontaktTelefon ?? "").Trim());
        var mobil = PersonaldatenPruefung.TelefonFuerSwissdec((stamm.Hauptsitz?.KontaktMobil ?? "").Trim());
        return new[]
        {
            new XElement(Sd + "Name", name),
            mail.Length == 0 ? null : new XElement(Sd + "EmailAddress", mail),
            tel == null ? null : new XElement(Sd + "PhoneNumber", tel),
            mobil == null ? null : new XElement(Sd + "MobilePhoneNumber", mobil),
        };
    }

    /// <summary>
    /// Die Institutionen folgen den Arbeitsorten (Referenz 2025-01: LU, BE, VD, TI, AG).
    /// Die Katalog-Reihenfolge gilt für die Totale der Jahresmeldung, dort steht AG vor TI.
    /// </summary>
    public static IEnumerable<ElmFakAdressierung.Eintrag> KassenNachArbeitsort(
        IEnumerable<ElmFakAdressierung.Eintrag> kassen, IReadOnlyList<string?> kantoneDerArbeitsorte)
    {
        int Rang(ElmFakAdressierung.Eintrag k)
        {
            for (int i = 0; i < kantoneDerArbeitsorte.Count; i++)
                if (k.Kantone.Contains(kantoneDerArbeitsorte[i] ?? "", StringComparer.OrdinalIgnoreCase)) return i;
            return int.MaxValue;
        }
        return kassen.OrderBy(Rang).ThenBy(k => k.Kassennummer, StringComparer.Ordinal);
    }

    /// <summary>Am Vertrag hinterlegter Arbeitsunterbruch. Mit nahtlosem Folgevertrag endet nichts, die Angabe zählt dann nicht.</summary>
    /// <param name="alleVertraege">Auch die der anderen Filialen: Ein Folgevertrag dort ist kein Unterbruch.</param>
    public static IEnumerable<E.Manuell> UnterbruecheAusVertraegen(IReadOnlyList<Employment> vertraege, IReadOnlyList<Employment>? alleVertraege = null)
    {
        foreach (var v in vertraege)
        {
            if (v.ContractEndDate is not DateTime ende || string.IsNullOrWhiteSpace(v.EndeArt) || v.EndeArt == Employment.EndeTodesfall) continue;
            if (HatFolgevertrag(alleVertraege ?? vertraege, ende)) continue;
            yield return new E.Manuell(new DateOnly(ende.Year, ende.Month, 1), E.Withdrawal, v.EndeArt);
        }
    }

    /// <summary>Bei «Je Filiale» meldet eine Mutation nur die Filiale, in der die Person an diesem Tag angestellt ist.</summary>
    public static bool GiltInFiliale(IReadOnlyList<Employment> vertraege, DateOnly tag)
    {
        var t = tag.ToDateTime(TimeOnly.MinValue);
        return vertraege.Any(v => v.ContractStartDate.Date <= t && (v.ContractEndDate == null || v.ContractEndDate.Value.Date >= t));
    }

    /// <summary>Tag, an dem Zivilstand oder Umzug gemeldet wird (wie ElmEmaEreignisse): Gültigkeit, sonst das Bekanntwerden.</summary>
    public static DateOnly Meldetag(DateOnly gueltigAb, DateOnly erfahren)
    {
        var ab = E.ErsterFolgemonat(gueltigAb);
        return ab > erfahren ? ab : erfahren;
    }

    /// <summary>Teilpensionierung ab Beginn des Vertrags, gemeldet im Monat des Vertragsbeginns (TF12 Casanova).</summary>
    public static IEnumerable<E.Manuell> TeilpensionierungenAusVertraegen(IReadOnlyList<Employment> vertraege)
        => vertraege.Where(v => v.Teilpensionierung).Select(v =>
        {
            var ab = DateOnly.FromDateTime(v.ContractStartDate);
            return new E.Manuell(new DateOnly(ab.Year, ab.Month, 1), E.Mutation, "partialRetirement", ab);
        });

    /// <summary>Todesfall am Vertrag. Der alte Austrittsgrund TODESFALL gilt weiter.</summary>
    public static DateOnly? Todestag(Employee e, IReadOnlyList<Employment> vertraege)
    {
        var v = vertraege.LastOrDefault(x => x.EndeArt == Employment.EndeTodesfall && x.ContractEndDate != null && !HatFolgevertrag(vertraege, x.ContractEndDate.Value));
        if (v != null) return DateOnly.FromDateTime(v.ContractEndDate!.Value);
        return e.ExitDate is DateTime tod && string.Equals(e.Austrittsgrund, "TODESFALL", StringComparison.OrdinalIgnoreCase)
            ? DateOnly.FromDateTime(tod) : null;
    }

    private static bool HatFolgevertrag(IReadOnlyList<Employment> vertraege, DateTime ende)
        => vertraege.Any(n => n.ContractStartDate.Date == ende.Date.AddDays(1));

    /// <summary>H, M oder N: Ein Wechsel beginnt die Rückschau der BVG-Basis neu (RL 8.6.6.2).</summary>
    public static char Vertragsart(Employment em)
    {
        var art = (em.SwissdecVertragsart ?? "").Trim();
        if (art.Contains("NoTimeConstraint", StringComparison.OrdinalIgnoreCase) || art == "administrativeBoard") return 'N';
        return IstMonatslohnModell(em.EmploymentModel) ? 'M' : 'H';
    }

    /// <param name="abweichendeStunden">Vom Arbeitszeitmodell abweichende Wochenstunden. Sie gehen vor, wie in der gemeldeten Arbeitszeit (TF01 Herz 42 h).</param>
    public static ElmBvgBasis.Vertrag Vertrag(Employment em, decimal prozent13Firma, decimal? abweichendeStunden = null, decimal? abweichendeLektionen = null)
    {
        var art = Vertragsart(em);
        bool stundenlohn = art == 'H';
        var stunden = abweichendeStunden is > 0m ? abweichendeStunden : em.WeeklyHours is > 0m ? em.WeeklyHours : em.GuaranteedHoursPerWeek;
        var lektionen = abweichendeLektionen is > 0m ? abweichendeLektionen : em.WeeklyLessons;
        bool fest = stundenlohn && em.Regelmaessig != false && (stunden is > 0m || lektionen is > 0m);
        return new ElmBvgBasis.Vertrag(stundenlohn, fest, stunden, lektionen, em.HourlyRate, em.LessonRate,
            stundenlohn && em.ThirteenthSalary ? prozent13Firma : null,
            art == 'M' && em.ThirteenthSalary, art == 'M' && em.VierzehnterMonatslohn);
    }

    /// <summary>
    /// Lohnzeilen für die BVG-Basis. Der Monatslohn zählt aus dem Vertrag: Der Lohnzettel ist im Ein- und
    /// Austrittsmonat anteilig (TF25 Lehmann 8'400 statt 12'000, RL 8.6.6.3 «fix zugeteilte Lohnarten»).
    /// Der 13. eines Stundenlohns steht in älteren Lohnzetteln als 1200 und gehört in die Rückschau (TF02 Paganini).
    /// </summary>
    public static Dictionary<string, decimal> MitVertragslohn(IReadOnlyDictionary<string, decimal>? lohn, Employment em)
    {
        var res = new Dictionary<string, decimal>();
        var art = Vertragsart(em);
        foreach (var (code, betrag) in lohn ?? new Dictionary<string, decimal>())
        {
            var c = art == 'H' && code == "1200" ? "1201" : code;
            res[c] = res.GetValueOrDefault(c) + betrag;
        }
        if (art == 'M' && em.MonthlySalary is > 0m) res["1000"] = em.MonthlySalary.Value;
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
