using System.Text.Json;
using System.Xml.Linq;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;
using static HrSystem.Services.Elm.ElmGemeinsam;

namespace HrSystem.Services.Elm;

/// <summary>
/// Etappe E6 (Walter 27.09.2026): Monatsmeldung <c>DeclareMonthlySalary</c> —
/// Quellensteuer je Kanton + BFS-Statistik, aus den DEFINITIV abgeschlossenen
/// Lohnzetteln eines Monats über alle Filialen der Rechtseinheit.
///
/// <para>Vorlage und Soll: <c>SWISSCEC/RefXML/RefXML_202411_MONTHLY.xml</c>
/// (Muster AG). Geprüft wird die Meldung im Calculation Test des Quality Tools.</para>
///
/// <para>Bewusste Lücken, die der Vergleich rot zeigt und die gemeldet werden
/// (nicht stillschweigend gefüllt):</para>
/// <list type="bullet">
/// <item>CompanyWorkingTime: OneCrew kennt nur die Wochenstunden je Filiale, die
/// Referenz führt mehrere Modelle je Rechtseinheit (42 h, 40 h, 21 Lektionen,
/// 20 h + 10 Lektionen) mit eigener ID, auf die jede Person verweist. Dafür
/// braucht es die Arbeitszeitmodell-Verwaltung — eigener Auftrag.</item>
/// <item>Statistik-Stammdaten: Ausbildung (Swissdec-Stufen), Kaderfunktion und
/// Ferienanspruch in Tagen liegen so nicht in OneCrew
/// (<c>docs/swissdec-testmandant.md</c>, bekannte Lücken).</item>
/// </list>
/// </summary>
public class ElmMonthlyDeclarationBuilder
{
    private readonly AppDbContext _db;
    private readonly ElmXmlValidator _validator;

    private readonly ElmEinstellungen _einstellungen;

    public ElmMonthlyDeclarationBuilder(AppDbContext db, ElmXmlValidator validator, ElmEinstellungen einstellungen)
    {
        _db = db;
        _validator = validator;
        _einstellungen = einstellungen;
    }

    public record BuildResult(
        string Xml, int Personen, int QstZeilen, int StatistikZeilen,
        List<string> Warnungen, List<string> XsdFehler);

    /// <summary>
    /// Die Monatsmeldung führt nur Arbeitsorte, an denen im Monat jemand Lohn hat
    /// (Walter 28.09.2026, Quality Tool Nov 2024: nur #LU und #BE). Die Jahresmeldung
    /// bleibt bei allen Filialen — die FAK wird je Arbeitsort adressiert, auch ohne Personen.
    /// </summary>
    public static RechtseinheitStamm NurArbeitsorteMitLohn(RechtseinheitStamm stamm, IReadOnlySet<int> filialenMitLohn)
        => stamm with { Filialen = stamm.Filialen.Where(b => filialenMitLohn.Contains(b.Id)).ToList() };

    /// <summary>
    /// Anstellung, die im Meldemonat galt, als lückenlose Vertragskette: ein Modell- oder
    /// Filialwechsel ohne Unterbruch ist kein Austritt. Ende = null heisst offen.
    /// Quality Tool Dez 2024: TF16 Aebi 01.11.–20.12. (Wiedereintritt 15.01. = neue Kette),
    /// TF07 Burri Austritt 31.12.
    /// </summary>
    public static (DateTime Start, DateTime? Ende)? Anstellung(IEnumerable<Employment> abschnitte,
                                                             DateTime monatsAnfang, DateTime monatsEnde)
    {
        var liste = abschnitte.OrderBy(a => a.ContractStartDate).ToList();
        var idx = liste.FindLastIndex(a => a.ContractStartDate.Date <= monatsEnde
                                        && (a.ContractEndDate == null || a.ContractEndDate.Value.Date >= monatsAnfang));
        if (idx < 0) idx = liste.FindLastIndex(a => a.ContractStartDate.Date <= monatsEnde);
        if (idx < 0) return null;

        var start = liste[idx].ContractStartDate.Date;
        for (var i = idx - 1; i >= 0; i--)
        {
            var vorEnde = liste[i].ContractEndDate?.Date;
            if (vorEnde != null && vorEnde.Value.AddDays(1) < start) break;
            if (liste[i].ContractStartDate.Date < start) start = liste[i].ContractStartDate.Date;
        }

        var ende = liste[idx].ContractEndDate?.Date;
        for (var j = idx + 1; j < liste.Count && ende != null; j++)
        {
            if (liste[j].ContractStartDate.Date > ende.Value.AddDays(1)) break;
            var folgeEnde = liste[j].ContractEndDate?.Date;
            ende = folgeEnde == null ? null : folgeEnde > ende ? folgeEnde : ende;
        }
        return (start, ende);
    }

    /// <summary>
    /// Bewilligung, die am Monatsende galt und bis dahin bekannt war («erfahren am»).
    /// Quality Tool Dez 2024 TF14 Egli: settled-C ab 01.12., im November noch annual-B.
    /// Null = keine Historie — dann gilt die Bewilligung am MA.
    /// </summary>
    public static int? BewilligungAmStichtag(IEnumerable<EmployeePermitHistory> historie, DateOnly stichtag)
        => historie
            .Where(h => h.PermitTypeId != null && h.ValidFrom <= stichtag && (h.ErfahrenAm ?? h.ValidFrom) <= stichtag)
            .OrderByDescending(h => h.ValidFrom).ThenByDescending(h => h.Id)
            .Select(h => h.PermitTypeId)
            .FirstOrDefault();

    /// <summary>
    /// Lohnzeilen eines Lohnzettels in die Statistik-Töpfe (Lohnart → Topf, Richtlinien Kap. 12).
    /// <paramref name="warn"/> = null für Vormonate, damit Hinweise nicht mehrfach erscheinen.
    /// </summary>
    public static Dictionary<ElmStatistikCodes.Topf, decimal> Toepfe(
        JsonElement slip, IReadOnlyDictionary<string, string> lohnartByCode, List<string>? warn)
    {
        var topf = new Dictionary<ElmStatistikCodes.Topf, decimal>();
        void Buche(ElmStatistikCodes.Topf t, decimal betrag)
            => topf[t] = (topf.TryGetValue(t, out var v) ? v : 0m) + betrag;

        if (!slip.TryGetProperty("lohnLines", out var ll) || ll.ValueKind != JsonValueKind.Array) return topf;
        foreach (var z in ll.EnumerateArray())
        {
            var betrag = Num(z, "betrag");
            if (betrag == 0) continue;
            var code = (Str(z, "code") ?? "").Trim();
            var lohnartTxt = code.Length > 0 && lohnartByCode.TryGetValue(code, out var la) ? la : "";
            if (lohnartTxt.Length == 0) lohnartTxt = code;     // Testmandant führt die Lohnart als Code
            if (!int.TryParse(lohnartTxt, out var lohnart))
            {
                Buche(ElmStatistikCodes.Topf.Bruttolohn, betrag);
                warn?.Add($"Lohnzeile «{(Str(z, "bezeichnung") ?? code)}» ohne Swissdec-Lohnart — als Bruttolohn gemeldet "
                        + "(Lohnpositionen → Swissdec-Lohnart setzen).");
                continue;
            }
            var ziel = ElmStatistikCodes.TopfFuer(lohnart);
            if (ziel == null)
            {
                Buche(ElmStatistikCodes.Topf.Bruttolohn, betrag);
                warn?.Add($"Lohnart {lohnart} ist keinem Statistik-Topf zugeordnet — als Bruttolohn gemeldet. "
                        + "Zuordnung in ElmStatistikCodes.TopfFuer ergänzen (Richtlinien Kap. 12).");
                continue;
            }
            Buche(ziel.Value, betrag);
        }
        return topf;
    }

    /// <summary>Zahl aus dem Lohnzettel (slip_json), 0 wenn nicht vorhanden.</summary>
    private static decimal Num(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;

    private static string? Str(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public async Task<BuildResult> BuildAsync(int year, int month, CancellationToken ct = default)
    {
        var warn = new List<string>();
        var stamm = await LadeRechtseinheitAsync(_db, warn, ct);
        if (stamm == null) return new BuildResult("", 0, 0, 0, warn, new List<string>());

        var filialIds = stamm.Filialen.Select(b => b.Id).ToList();
        var filialeById = stamm.Filialen.ToDictionary(b => b.Id);

        // ── Perioden: NUR definitiv abgeschlossene (Walter 27.09.2026) ────────
        var perioden = await _db.PayrollPerioden.AsNoTracking()
            .Where(p => p.Year == year && p.Month == month && filialIds.Contains(p.CompanyProfileId))
            .ToListAsync(ct);
        if (perioden.Count == 0)
            return new BuildResult("", 0, 0, 0,
                new List<string> { $"Für {month:00}.{year} gibt es in keiner Filiale eine Lohnperiode." }, new List<string>());

        var offen = perioden.Where(p => p.Status != "abgeschlossen")
            .Select(p => filialeById.TryGetValue(p.CompanyProfileId, out var b) ? b.FullDisplayName : $"Filiale {p.CompanyProfileId}")
            .OrderBy(x => x).ToList();
        if (offen.Count > 0)
            return new BuildResult("", 0, 0, 0,
                new List<string> { $"Nicht definitiv abgeschlossen: {string.Join(", ", offen)}. Eine Monatsmeldung wird nur aus abgeschlossenen Lohnläufen erzeugt." },
                new List<string>());

        var periodeIds = perioden.Select(p => p.Id).ToList();
        var snaps = await _db.PayrollSnapshots.AsNoTracking()
            .Where(s => periodeIds.Contains(s.PayrollPeriodeId) && s.Status != "STORNIERT")
            .Select(s => new { s.EmployeeId, s.PayrollPeriodeId, s.SlipJson })
            .ToListAsync(ct);
        if (snaps.Count == 0)
            return new BuildResult("", 0, 0, 0,
                new List<string> { $"Keine Lohnzettel für {month:00}.{year}." }, new List<string>());

        var periodeFiliale = perioden.ToDictionary(p => p.Id, p => p.CompanyProfileId);

        // ── Statistik-Zuordnung der Lohnarten (ELM-Lohnraster, Spalte StatistikCode) ─
        // Zuordnung Lohnzeile → Statistik-Topf über die SWISSDEC-LOHNART der
        // Lohnposition (Walter 27.09.2026). Der Code der Lohnposition ist unser
        // eigener; massgebend ist die Lohnart des Musterlohnartenstamms.
        var positionen = await _db.Lohnpositionen.AsNoTracking()
            .Select(l => new { l.Code, l.SwissdecLohnart })
            .ToListAsync(ct);
        var lohnartByCode = positionen
            .GroupBy(x => x.Code)
            .ToDictionary(g => g.Key, g => (g.First().SwissdecLohnart ?? "").Trim());

        var empIds = snaps.Select(s => s.EmployeeId).Distinct().ToList();
        var emps = await _db.Employees.AsNoTracking()
            .Include(e => e.NationalityRef)
            .Include(e => e.PermitType)
            .Where(e => empIds.Contains(e.Id))
            .ToListAsync(ct);
        var empById = emps.ToDictionary(e => e.Id);
        var employments = await _db.Employments.AsNoTracking()
            .Where(em => empIds.Contains(em.EmployeeId))
            .ToListAsync(ct);
        // Statistik-Stammdaten: Ausbildung und Stellung stehen bereits in den
        // LSE-Feldern (Walter 27.09.2026) — nicht nochmals erfassen, nur uebersetzen.
        var lseJeMa = await _db.EmployeeLse.AsNoTracking()
            .Where(l => empIds.Contains(l.EmployeeId))
            .ToListAsync(ct);
        var lseById = lseJeMa.ToDictionary(l => l.EmployeeId);
        var stellungMapping = await _db.LseCodeMappings.AsNoTracking()
            .Where(m => m.MappingTyp == "STELLUNG")
            .ToListAsync(ct);

        // Arbeitszeitmodelle der Rechtseinheit + Zuordnung je Person (Gueltig-ab).
        var modelle = stamm.Hauptsitz == null
            ? new List<Arbeitszeitmodell>()
            : await _db.Arbeitszeitmodelle.AsNoTracking()
                .Where(m => m.HauptsitzId == stamm.Hauptsitz.Id && m.IsActive)
                .OrderBy(m => m.Id).ToListAsync(ct);
        var zuordnungen = modelle.Count == 0
            ? new List<EmployeeArbeitszeitmodell>()
            : await _db.EmployeeArbeitszeitmodelle.AsNoTracking()
                .Where(z => empIds.Contains(z.EmployeeId))
                .ToListAsync(ct);
        var unbrauchbar = modelle.Where(m => !m.IstMeldefaehig).ToList();
        if (unbrauchbar.Count > 0)
            return new BuildResult("", 0, 0, 0,
                new List<string> { "Arbeitszeitmodelle ohne Wochenstunden UND ohne Wochenlektionen: "
                    + string.Join(", ", unbrauchbar.Select(m => $"«{m.Bezeichnung}»"))
                    + ". Swissdec verlangt einen der beiden Werte — bitte am Hauptsitz ergaenzen." },
                new List<string>());

        var bewilligungen = await _db.EmployeePermitHistories.AsNoTracking()
            .Where(h => empIds.Contains(h.EmployeeId))
            .ToListAsync(ct);
        var permitTypen = await _db.PermitTypes.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);

        // Jahreswerte der Statistik sind kumuliert ab Beginn der Anstellung im Jahr
        // (Quality Tool Dez 2024: TF14 Egli 13. ML 175.45 + 304.10 = 479.55, TF16 Aebi
        // übrige Leistungen 500 + 750). Dafür die Lohnzettel der Vormonate desselben Jahres.
        var vorPerioden = month == 1
            ? new List<PayrollPeriode>()
            : await _db.PayrollPerioden.AsNoTracking()
                .Where(p => p.Year == year && p.Month < month && p.Status == "abgeschlossen"
                         && filialIds.Contains(p.CompanyProfileId))
                .ToListAsync(ct);
        var vorPeriodeIds = vorPerioden.Select(p => p.Id).ToList();
        var vorSnaps = vorPeriodeIds.Count == 0
            ? new List<(int EmployeeId, int PayrollPeriodeId, string? SlipJson)>()
            : (await _db.PayrollSnapshots.AsNoTracking()
                .Where(s => vorPeriodeIds.Contains(s.PayrollPeriodeId) && empIds.Contains(s.EmployeeId) && s.Status != "STORNIERT")
                .Select(s => new { s.EmployeeId, s.PayrollPeriodeId, s.SlipJson })
                .ToListAsync(ct))
              .Select(s => (s.EmployeeId, s.PayrollPeriodeId, SlipJson: (string?)s.SlipJson)).ToList();
        var vorPeriodeById = vorPerioden.ToDictionary(p => p.Id);

        var qstVersionen = await _db.EmployeeQuellensteuer.AsNoTracking()
            .Where(q => empIds.Contains(q.EmployeeId))
            .ToListAsync(ct);
        var familie = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Where(f => empIds.Contains(f.EmployeeId))
            .ToListAsync(ct);
        var wochenAdressen = await _db.EmployeeAddresses.AsNoTracking()
            .Where(a => empIds.Contains(a.EmployeeId) && a.AddressType == "Wochenaufenthalt")
            .ToListAsync(ct);

        // QST-Korrekturen der Vormonate, die in diesem Monat verrechnet wurden (TF14 Egli Dez 2024).
        var korrekturen = await _db.QstKorrekturen.AsNoTracking()
            .Where(k => k.VerrechnetPeriodeId != null && periodeIds.Contains(k.VerrechnetPeriodeId.Value))
            .OrderBy(k => k.Jahr).ThenBy(k => k.Monat).ThenBy(k => k.Id)
            .ToListAsync(ct);
        foreach (var k in korrekturen.Where(x => !empIds.Contains(x.EmployeeId)))
            warn.Add($"QST-Korrektur {k.Monat:00}.{k.Jahr} (MA-Id {k.EmployeeId}) ohne Lohnzettel in diesem Monat — nicht gemeldet.");
        var versionById = qstVersionen.ToDictionary(v => v.Id);
        var korrTotal = new Dictionary<string, SortedDictionary<string, (decimal Basis, decimal Steuer)>>(StringComparer.Ordinal);

        // Wohngemeinde der Personen: die Statistik verlangt die BFS-Nummer in der
        // Adresse. OneCrew führt sie am MA nicht — sie wird über die PLZ aus dem
        // Ortschaftsverzeichnis abgeleitet, wie bei den Filialen.
        var plzListe = emps.Select(e2 => (e2.ZipCode ?? "").Trim()).Where(z => z.Length == 4).Distinct().ToList();
        var orte = await _db.SwissLocations.AsNoTracking()
            .Where(l => plzListe.Contains(l.Plz4))
            .Select(l => new { l.Plz4, l.BfsNr, l.Ortschaftsname, l.Gemeindename })
            .ToListAsync(ct);
        int? WohnGemeinde(Employee e2)
        {
            var plz = (e2.ZipCode ?? "").Trim();
            if (plz.Length != 4) return null;
            var ort = (e2.City ?? "").Trim().ToLowerInvariant();
            var kand = orte.Where(o => o.Plz4 == plz).ToList();
            var best = kand.FirstOrDefault(o => (o.Ortschaftsname ?? "").ToLowerInvariant().StartsWith(ort)
                                             || (o.Gemeindename ?? "").ToLowerInvariant() == ort)?.BfsNr;
            if (best is > 0) return best;
            var nrs = kand.Select(o => o.BfsNr).Distinct().ToList();
            return nrs.Count == 1 ? nrs[0] : null;
        }

        var monatsAnfang = new DateTime(year, month, 1);
        var monatsEnde = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var monatStr = $"{year:0000}-{month:00}";

        var personen = new List<XElement>();
        var qstKantone = new SortedSet<string>(StringComparer.Ordinal);
        var qstTotal = new Dictionary<string, (decimal Basis, decimal Steuer)>(StringComparer.Ordinal);
        var gemeldeteFilialen = new HashSet<int>();
        int qstZeilen = 0, statZeilen = 0;

        foreach (var g in snaps.GroupBy(s => s.EmployeeId).OrderBy(g => g.Key))
        {
            if (!empById.TryGetValue(g.Key, out var e)) continue;
            if (e.DateOfBirth == null)
            {
                warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Geburtsdatum fehlt (Pflichtfeld) — nicht gemeldet.");
                continue;
            }

            var statistikZeilen = new List<XElement>();
            var qstZeilenPerson = new List<XElement>();

            var anstellung = Anstellung(employments.Where(m => m.EmployeeId == e.Id), monatsAnfang, monatsEnde);
            var jahrStart = new DateTime(year, 1, 1);
            var periodeVon = anstellung == null ? monatsAnfang
                : anstellung.Value.Start > jahrStart ? anstellung.Value.Start : jahrStart;
            var austritt = anstellung?.Ende is DateTime aus && aus >= monatsAnfang && aus <= monatsEnde ? aus : (DateTime?)null;
            var periodeBis = austritt ?? monatsEnde;
            var ersterMonatDerPeriode = new DateTime(periodeVon.Year, periodeVon.Month, 1);

            foreach (var s in g.OrderBy(x => x.PayrollPeriodeId))
            {
                if (string.IsNullOrWhiteSpace(s.SlipJson)) continue;
                JsonElement slip;
                try { slip = JsonDocument.Parse(s.SlipJson).RootElement; }
                catch (JsonException)
                {
                    warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Lohnzettel nicht lesbar — nicht gemeldet.");
                    continue;
                }

                var filialId = periodeFiliale[s.PayrollPeriodeId];
                if (!filialeById.TryGetValue(filialId, out var filiale)) continue;

                var em = employments
                    .Where(m => m.EmployeeId == e.Id && m.CompanyProfileId == filialId
                             && m.ContractStartDate <= monatsEnde
                             && (m.ContractEndDate == null || m.ContractEndDate >= monatsAnfang))
                    .OrderByDescending(m => m.ContractStartDate).FirstOrDefault()
                    ?? employments.Where(m => m.EmployeeId == e.Id)
                           .OrderByDescending(m => m.ContractStartDate).FirstOrDefault();

                var jahresToepfe = Toepfe(slip, lohnartByCode, null);
                foreach (var vor in vorSnaps.Where(v => v.EmployeeId == e.Id))
                {
                    var vp = vorPeriodeById[vor.PayrollPeriodeId];
                    if (vp.CompanyProfileId != filialId || new DateTime(year, vp.Month, 1) < ersterMonatDerPeriode) continue;
                    if (string.IsNullOrWhiteSpace(vor.SlipJson)) continue;
                    JsonElement vorSlip;
                    try { vorSlip = JsonDocument.Parse(vor.SlipJson).RootElement; }
                    catch (JsonException) { continue; }
                    foreach (var (t, b) in Toepfe(vorSlip, lohnartByCode, null))
                        jahresToepfe[t] = (jahresToepfe.TryGetValue(t, out var v0) ? v0 : 0m) + b;
                }

                lseById.TryGetValue(e.Id, out var lse);
                statistikZeilen.Add(BaueStatistikZeile(e, em, filiale, slip, lohnartByCode, lse, stellungMapping,
                                                       monatStr, periodeVon, periodeBis, jahresToepfe, warn));
                statZeilen++;
                gemeldeteFilialen.Add(filialId);

                var qst = BaueQstZeile(e, em, filiale, slip, qstVersionen,
                                       familie.Where(k => k.EmployeeId == e.Id && k.MemberType == "Kind").ToList(),
                                       stamm, monatStr, monatsAnfang, warn,
                                       wochenAdressen.Where(a => a.EmployeeId == e.Id)
                                           .OrderByDescending(a => a.ValidFrom).FirstOrDefault());
                if (qst != null)
                {
                    qstZeilenPerson.Add(qst.Value.Zeile);
                    qstKantone.Add(qst.Value.Kanton);
                    var bisher = qstTotal.TryGetValue(qst.Value.Kanton, out var t) ? t : (0m, 0m);
                    qstTotal[qst.Value.Kanton] = (bisher.Item1 + qst.Value.Basis, bisher.Item2 + qst.Value.Steuer);
                    qstZeilen++;
                }
            }

            var permitId = BewilligungAmStichtag(bewilligungen.Where(h => h.EmployeeId == e.Id), DateOnly.FromDateTime(monatsEnde));
            var permitAmEnde = permitId != null && permitTypen.TryGetValue(permitId.Value, out var pt) ? pt : null;

            foreach (var k in korrekturen.Where(x => x.EmployeeId == e.Id))
            {
                versionById.TryGetValue(k.NeueVersionId, out var neu);
                var alt = k.AlteVersionId is int aid && versionById.TryGetValue(aid, out var av) ? av : null;
                var kt = (neu?.Steuerkanton ?? alt?.Steuerkanton ?? "").Trim().ToUpperInvariant();
                if (kt.Length != 2 || !filialeById.TryGetValue(k.CompanyProfileId, out var kFiliale))
                {
                    warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): QST-Korrektur {k.Monat:00}.{k.Jahr} ohne Steuerkanton oder Filiale — nicht gemeldet.");
                    continue;
                }
                var grund = QstKorrekturGrund(k, (permitAmEnde ?? e.PermitType)?.Code, e.NationalityRef?.Code,
                                              alt?.Steuerkanton, neu?.Steuerkanton);
                var block = QstKorrekturBlock(k, WpId(kFiliale), neu?.ValidFrom ?? new DateOnly(k.Jahr, k.Monat, 1), grund);

                var ziel = qstZeilenPerson.FirstOrDefault(z => (string?)z.Attribute("addresseeIDRef") == $"#QST-{kt}");
                if (ziel == null)
                {
                    // Nur Korrektur, kein laufender Abzug (Egli Dez 2024: C-Ausweis, NON).
                    var kopf = QstVersionWahl.Waehle(qstVersionen.Where(v => v.EmployeeId == e.Id), DateOnly.FromDateTime(monatsAnfang)) ?? neu;
                    var gemeinde = neu?.QstGemeindeBfsNr ?? kopf?.QstGemeindeBfsNr;
                    ziel = new XElement(Sd + "TaxAtSourceSalary",
                        new XAttribute("addresseeIDRef", $"#QST-{kt}"),
                        QstZusatz(e, kopf, familie.Where(f => f.EmployeeId == e.Id && f.MemberType == "Kind").ToList()),
                        new XElement(Sd + "TaxAtSourceCanton", kt),
                        gemeinde is > 0 ? new XElement(Sd + "TaxAtSourceMunicipalityID", gemeinde.Value) : null,
                        new XElement(Sd + "CurrentMonth", monatStr));
                    qstZeilenPerson.Add(ziel);
                    qstKantone.Add(kt);
                    if (!qstTotal.ContainsKey(kt)) qstTotal[kt] = (0m, 0m);
                    qstZeilen++;
                }
                ziel.Add(block);
                gemeldeteFilialen.Add(k.CompanyProfileId);

                var (dBasis, dSteuer) = QstKorrekturWirkung(k);
                if (!korrTotal.TryGetValue(kt, out var jeMonat)) korrTotal[kt] = jeMonat = new SortedDictionary<string, (decimal, decimal)>(StringComparer.Ordinal);
                var mKey = $"{k.Jahr:0000}-{k.Monat:00}";
                var bisherK = jeMonat.TryGetValue(mKey, out var bk) ? bk : (0m, 0m);
                jeMonat[mKey] = (bisherK.Item1 + dBasis, bisherK.Item2 + dSteuer);
            }

            if (statistikZeilen.Count == 0 && qstZeilenPerson.Count == 0) continue;

            // Stand des MELDEMONATS, nie der heutige (Walter 27.09.2026): Pensum,
            // Wochenstunden und Eintritt kommen aus der Anstellung, die im Monat galt.
            // Beleg TF44 Lusser Nov 2024 = 100 % / 42 h, nicht das heutige 20 % / 8.4 h.
            var emHaupt = employments
                .Where(m => m.EmployeeId == e.Id
                         && m.ContractStartDate <= monatsEnde
                         && (m.ContractEndDate == null || m.ContractEndDate >= monatsAnfang))
                .OrderByDescending(m => m.ContractStartDate).FirstOrDefault()
                ?? employments.Where(m => m.EmployeeId == e.Id)
                       .OrderByDescending(m => m.ContractStartDate).FirstOrDefault();
            // Am Monatsende gueltiges Modell (juengstes Gueltig-ab <= Monatsende).
            var modellId = zuordnungen
                .Where(z => z.EmployeeId == e.Id && z.GueltigAb <= DateOnly.FromDateTime(monatsEnde))
                .OrderByDescending(z => z.GueltigAb).ThenByDescending(z => z.Id)
                .Select(z => (int?)z.ArbeitszeitmodellId).FirstOrDefault();
            var modell = modellId != null ? modelle.FirstOrDefault(m => m.Id == modellId) : null;
            modell ??= modelle.FirstOrDefault();   // Rueckfall: Standardmodell der Rechtseinheit

            var person = new XElement(Sd + "Person",
                Particulars(e, warn, WohnGemeinde(e), permitAmEnde),
                new XElement(C + "Work",
                    new XAttribute("workID", WorkId(e)),
                    modell != null ? new XAttribute("companyWorkingTimeIDRef", "#" + modell.KennungOderId) : null,
                    new XElement(C + "WorkingTime", WorkingTime(emHaupt,
                        modell?.Wochenstunden is > 0m ? modell.Wochenstunden!.Value : stamm.Haupt.NormalWeeklyHours ?? 42m)),
                    new XElement(C + "EntryDate", (emHaupt?.ContractStartDate ?? e.EntryDate ?? monatsAnfang).ToString("yyyy-MM-dd")),
                    austritt == null ? null : new XElement(C + "WithdrawalDate", austritt.Value.ToString("yyyy-MM-dd"))),
                // Reihenfolge laut XSD (MonthlyPersonType): Quellensteuer VOR Statistik.
                qstZeilenPerson.Count > 0 ? new XElement(Sd + "TaxAtSourceSalaries", qstZeilenPerson) : null,
                statistikZeilen.Count > 0 ? new XElement(Sd + "StatisticSalaries", statistikZeilen) : null);
            personen.Add(person);
        }

        if (personen.Count == 0)
            return new BuildResult("", 0, 0, 0,
                new List<string> { $"Keine meldbaren Personen für {month:00}.{year}." }, new List<string>());

        // ── Empfänger: nur wer Daten bekommt (Nullmeldungs-Regel) ────────────
        var addressees = new List<XElement>();
        foreach (var kt in qstKantone)
            addressees.Add(new XElement(Sdc + "Addressee",
                new XAttribute("addresseeID", $"#QST-{kt}"),
                new XElement(Ep + "AddresseeIdentification", kt),
                new XElement(Ep + "ProcessByDistributor", "true")));
        if (statZeilen > 0)
            addressees.Add(new XElement(Sdc + "Addressee",
                new XAttribute("addresseeID", "#BFS"),
                new XElement(Ep + "AddresseeIdentification", "Statistic"),
                new XElement(Ep + "ProcessByDistributor", "true")));

        // ── Institutions: QST-Schuldnernummer je Kanton aus dem Empfänger-Katalog ─
        var empfaenger = await _db.LohndatenEmpfaengers.AsNoTracking()
            .Include(x => x.Zuordnungen)
            .Where(x => x.IsActive && x.Art == "QST")
            .ToListAsync(ct);
        var institutions = new List<XElement>();
        var fehlendeQstNummern = new List<string>();
        foreach (var kt in qstKantone)
        {
            var kasse = empfaenger.FirstOrDefault(x => (x.KantonCode ?? "").Trim().ToUpperInvariant() == kt);
            var nummer = (kasse?.Kassennummer ?? "").Trim();
            if (nummer.Length == 0)
            {
                nummer = (kasse?.Zuordnungen.Select(z => (z.Mitgliednummer ?? "").Trim()).FirstOrDefault(v => v.Length > 0)) ?? "";
            }
            if (nummer.Length == 0)
                fehlendeQstNummern.Add(kt);
            institutions.Add(new XElement(Sd + "TaxAtSource",
                new XAttribute("addresseeIDRef", $"#QST-{kt}"),
                new XElement(Sd + "CustomerIdentity", nummer),
                new XElement(Sd + "TaxAtSourceType", "salaries")));
        }
        if (statZeilen > 0)
            institutions.Add(new XElement(Sd + "Statistic",
                new XAttribute("addresseeIDRef", "#BFS"),
                new XElement(Sd + "PayAgreement", "individualContract")));
        // Ohne Schuldnernummer weist die Steuerverwaltung die Meldung zurück — dann
        // lieber gar keine Meldung als eine mit Platzhalter (Walter 27.09.2026).
        if (fehlendeQstNummern.Count > 0)
            return new BuildResult("", 0, 0, 0,
                new List<string> { "Keine Schuldner-/Abrechnungsnummer im Empfänger-Katalog für die Quellensteuer "
                    + string.Join(", ", fehlendeQstNummern)
                    + ". Erfassen unter System → Behörden & Empfänger (Art «QST», Kanton, Kassennummer); ohne sie wird die Meldung zurückgewiesen." },
                new List<string>());

        // ── Summen je QST-Kanton ─────────────────────────────────────────────
        var totals = qstKantone.Select(kt => new XElement(Sd + "TaxAtSourceTotals",
            new XAttribute("addresseeIDRef", $"#QST-{kt}"),
            new XElement(Sd + "TotalMonth",
                new XElement(Sd + "TotalTaxableEarning", Betrag05(qstTotal[kt].Basis)),
                new XElement(Sd + "TotalTaxAtSource", Betrag05(qstTotal[kt].Steuer)),
                new XElement(Sd + "TotalCommission", "0.00"),
                new XElement(Sd + "CurrentMonth", monatStr)),
            korrTotal.TryGetValue(kt, out var jeMonat)
                ? jeMonat.Select(m => new XElement(Sd + "CorrectionMonth",
                    new XElement(Sd + "TotalTaxableEarning", Amt(m.Value.Basis)),
                    new XElement(Sd + "TotalTaxAtSource", Amt(m.Value.Steuer)),
                    new XElement(Sd + "TotalCommission", "0.00"),
                    new XElement(Sd + "Month", m.Key)))
                : null)).ToList();

        // ── Arbeitszeitmodelle der Rechtseinheit ─────────────────────────────
        // Ohne erfasste Modelle gilt ein Standardmodell aus den Wochenstunden der
        // Filiale — damit verhaelt sich Schaub wie bisher (42 h).
        var arbeitszeit = new List<XElement>();
        if (modelle.Count == 0)
        {
            arbeitszeit.Add(new XElement(C + "CompanyWorkingTime",
                new XAttribute("companyWorkingTimeID", "#cwt1"),
                new XElement(C + "WeeklyHours", Amt(stamm.Haupt.NormalWeeklyHours ?? 42m))));
            warn.Add("Keine Arbeitszeitmodelle erfasst — gemeldet wird ein Standardmodell aus den Wochenstunden der Filiale "
                   + $"({Amt(stamm.Haupt.NormalWeeklyHours ?? 42m)} h). Erfassung: Hauptsitz → Arbeitszeitmodelle.");
        }
        else
        {
            foreach (var m in modelle)
            {
                XElement inhalt;
                if (m.Wochenstunden is > 0m && m.Wochenlektionen is > 0m)
                    inhalt = new XElement(C + "WeeklyHoursAndLessons",
                        new XElement(C + "WeeklyHours", Amt(m.Wochenstunden!.Value)),
                        new XElement(C + "WeeklyLessons", Amt(m.Wochenlektionen!.Value)));
                else if (m.Wochenlektionen is > 0m)
                    inhalt = new XElement(C + "WeeklyLessons", Amt(m.Wochenlektionen!.Value));
                else
                    inhalt = new XElement(C + "WeeklyHours", Amt(m.Wochenstunden!.Value));
                arbeitszeit.Add(new XElement(C + "CompanyWorkingTime",
                    new XAttribute("companyWorkingTimeID", "#" + m.KennungOderId),
                    inhalt));
            }
            var ohneZuordnung = empIds.Count(id => !zuordnungen.Any(z => z.EmployeeId == id));
            if (ohneZuordnung > 0)
                warn.Add($"{ohneZuordnung} Personen ohne Arbeitszeitmodell — gemeldet wird «{modelle[0].Bezeichnung}» "
                       + "als Standard der Rechtseinheit (MA → Arbeitszeitmodell).");
        }

        // Kontaktperson der Rechtseinheit (Walter 27.09.2026): wer eine Rückfrage zur
        // Meldung bekommt. Bewusst am Hauptsitz gepflegt, nicht der angemeldete
        // Benutzer — die Meldung gehört der Firma, nicht dem, der sie erzeugt hat.
        var kontaktName = (stamm.Hauptsitz?.KontaktName ?? "").Trim();
        var kontaktMail = (stamm.Hauptsitz?.KontaktEmail ?? "").Trim();
        var kontaktTel  = (stamm.Hauptsitz?.KontaktTelefon ?? "").Trim();
        if (kontaktName.Length == 0)
        {
            var admin = await _db.AppUsers.AsNoTracking()
                .Where(u => u.IsActive && u.Role == "admin")
                .OrderBy(u => u.Id).FirstOrDefaultAsync(ct);
            kontaktName = $"{admin?.FirstName} {admin?.LastName}".Trim();
            if (kontaktMail.Length == 0) kontaktMail = (admin?.Email ?? "").Trim();
            if (kontaktTel.Length == 0) kontaktTel = (stamm.Haupt.Phone ?? "").Trim();
            warn.Add("Keine Kontaktperson am Hauptsitz erfasst — gemeldet wird der erste Admin-Benutzer. "
                   + "Erfassung: System → Hauptsitze → Kontaktperson für Lohnmeldungen.");
        }
        if (kontaktName.Length == 0) kontaktName = stamm.Firmenname;

        var jetzt = DateTime.Now;
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Sdcst + "DeclareMonthlySalary",
                new XAttribute(XNamespace.Xmlns + "sdcst", Sdcst),
                new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
                new XAttribute(XNamespace.Xmlns + "sd", Sd),
                new XAttribute(XNamespace.Xmlns + "ep", Ep),
                new XAttribute(XNamespace.Xmlns + "c", C),
                RequestContext(stamm.Firmenname, jetzt, _einstellungen),
                // Kein <sdc:TestCase/> (Walter 27.09.2026): die Referenzmeldung hat
                // keines, und das Quality Tool prüft gegen sie. Solange kein
                // Transmitter-Zertifikat existiert, kann ohnehin nichts produktiv
                // übermittelt werden.
                new XElement(Sdc + "Job",
                    new XElement(Sdc + "Addressees", addressees)),
                new XElement(Sd + "MonthlySalaryDeclaration",
                    new XAttribute("schemaVersion", "0.0"),
                    CompanyDescription(NurArbeitsorteMitLohn(stamm, gemeldeteFilialen), arbeitszeit),
                    new XElement(Sd + "Staff", personen),
                    new XElement(Sd + "Institutions", institutions),
                    totals.Count > 0 ? new XElement(Sd + "SalaryTotals", totals) : null,
                    new XElement(Sd + "SalaryCounters",
                        qstZeilen > 0 ? new XElement(Sd + "NumberOf-TaxAtSourceSalary-Tags", qstZeilen) : null,
                        statZeilen > 0 ? new XElement(Sd + "NumberOf-StatisticSalary-Tags", statZeilen) : null),
                    new XElement(Sd + "ContactPerson",
                        new XElement(Sd + "Name", kontaktName),
                        kontaktMail.Length == 0 ? null : new XElement(Sd + "EmailAddress", kontaktMail),
                        kontaktTel.Length == 0 ? null : new XElement(Sd + "PhoneNumber", kontaktTel)))));

        var xml = doc.Declaration + Environment.NewLine + doc.ToString();
        var xsdFehler = _validator.Validate(xml);
        return new BuildResult(xml, personen.Count, qstZeilen, statZeilen, warn, xsdFehler);
    }

    /// <summary>
    /// Eine BFS-Statistikzeile je Person und Filiale. Die Monatswerte kommen aus dem
    /// Lohnzettel über den StatistikCode der Lohnart (ELM-Lohnraster):
    /// I = Bruttolohn, J = Zulagen, K = Familienzulagen, Y = Drittleistungen,
    /// P = Überstunden, O = 13. Monatslohn. Sozialabgaben und BVG kommen aus den
    /// Abzugszeilen (AHV + ALV + NBU bzw. BVG) und sind negativ.
    /// </summary>
    private XElement BaueStatistikZeile(
        Employee e, Employment? em, CompanyProfile filiale, JsonElement slip,
        Dictionary<string, string> lohnartByCode, EmployeeLse? lse,
        List<LseCodeMapping> stellungMapping, string monatStr,
        DateTime periodeVon, DateTime periodeBis,
        Dictionary<ElmStatistikCodes.Topf, decimal> jahresToepfe, List<string> warn)
    {
        var topf = Toepfe(slip, lohnartByCode, warn);
        decimal Wert(ElmStatistikCodes.Topf t) => topf.TryGetValue(t, out var v) ? v : 0m;
        decimal Jahr(ElmStatistikCodes.Topf t) => jahresToepfe.TryGetValue(t, out var v) ? v : 0m;

        // Sozialabgaben: Swissdec rundet JEDEN Beitrag einzeln auf 5 Rappen und
        // summiert erst dann (Walter 27.09.2026, an TF16 Nov 2024 nachgerechnet:
        // 686.80 + 135.85 + 3.05 + 198.35 = 1'024.05; die Summe der rappengenauen
        // Beträge ergäbe 1'024.00). Die Lohnbelege bleiben rappengenau.
        decimal sozial = 0, bvg = 0;
        if (slip.TryGetProperty("abzugLines", out var al2) && al2.ValueKind == JsonValueKind.Array)
            foreach (var z in al2.EnumerateArray())
            {
                var cat = (Str(z, "categoryCode") ?? "").Trim().ToUpperInvariant();
                var betrag = Num(z, "betrag");   // negativ
                if (cat is "AHV" or "ALV" or "ALVZ" or "NBUV") sozial += PayrollCalculations.Round05(betrag);
                else if (cat == "BVG") bvg += PayrollCalculations.Round05(betrag);
            }

        var stat1 = new XElement(Sd + "StatisticSalary",
            new XAttribute("workplaceIDRef", WpId(filiale)),
            new XAttribute("addresseeIDRef", "#BFS"),
            new XAttribute("workIDRef", WorkId(e)),
            new XElement(Sd + "CurrentMonth", monatStr),
            BaueStatistikStammdaten(e, em, filiale, slip, lse, stellungMapping, warn),
            KindOfWagePayment(em, filiale, slip),
            new XElement(Sd + "MonthlyValues",
                new XElement(Sd + "GrossBaseSalaryAndRegularAllowance", Betrag05(Wert(ElmStatistikCodes.Topf.Bruttolohn))),
                new XElement(Sd + "Allowances", Betrag05(Wert(ElmStatistikCodes.Topf.Zulagen))),
                new XElement(Sd + "FamilyIncomeSupplement", Betrag05(Wert(ElmStatistikCodes.Topf.Familienzulagen))),
                new XElement(Sd + "PaymentsByThird", Betrag05(Wert(ElmStatistikCodes.Topf.Drittleistungen))),
                new XElement(Sd + "SocialContributions", Amt(sozial)),
                // Auch BVG auf 5 Rappen (Walter 28.09.2026, Swissdec-Berater: keine ungerundeten
                // Beträge; Quality Tool TF16 Aebi −758.35). Ersetzt «BVG nicht runden» vom 21.09.;
                // der Lohnbeleg bleibt beim Fixbetrag der Kasse.
                new XElement(Sd + "BVG-LPP-RegularContribution", Amt(bvg)),
                new XElement(Sd + "ShortTimeWorkCompensation", Betrag05(Wert(ElmStatistikCodes.Topf.Kurzarbeit)))),
            new XElement(Sd + "AnnualValues",
                // Kumuliert ab Beginn der Anstellung im Jahr bis Monatsende bzw. Austritt.
                new XElement(Sd + "Period",
                    new XElement(Ep + "from", periodeVon.ToString("yyyy-MM-dd")),
                    new XElement(Ep + "until", periodeBis.ToString("yyyy-MM-dd"))),
                new XElement(Sd + "Overtime", Betrag05(Jahr(ElmStatistikCodes.Topf.Ueberstunden))),
                new XElement(Sd + "Earnings13th", Betrag05(Jahr(ElmStatistikCodes.Topf.Dreizehnter))),
                new XElement(Sd + "SporadicBenefits", Betrag05(Jahr(ElmStatistikCodes.Topf.Unregelmaessig))),
                new XElement(Sd + "FringeBenefits", Betrag05(Jahr(ElmStatistikCodes.Topf.Naturalleistungen))),
                new XElement(Sd + "CapitalPayment", Betrag05(Jahr(ElmStatistikCodes.Topf.Kapitalleistung))),
                new XElement(Sd + "OtherBenefits", Betrag05(Jahr(ElmStatistikCodes.Topf.Uebrige)))));
        return stat1;
    }

    /// <summary>
    /// Lohnart der Statistik: Monatslohn oder Stundenlohn. Reihenfolge und Aufbau
    /// laut XSD (StatisticMonthlyType / StatisticHourlyType) — beim Stundenlohn
    /// gehoeren Ansatz, Ferien-, Feiertags- und 13.-ML-Prozent in den Block
    /// ContractualHourlyWage, danach die tatsaechlich geleistete Zeit.
    /// </summary>
    private static XElement KindOfWagePayment(Employment? em, CompanyProfile filiale, JsonElement slip)
    {
        var dreizehnter = Amt(em?.ThirteenthSalary == true ? filiale.DefaultThirteenthSalaryPercent ?? 8.33m : 0m);
        var model = em?.EmploymentModel?.ToUpperInvariant() ?? "";
        var art = (em?.SwissdecVertragsart ?? "").Trim();

        // Verträge ohne Zeitbindung (Honorar) melden einen Jahreslohn.
        if (art is "indefiniteSalaryNoTimeConstraint" or "fixedSalaryNoTimeConstraint" or "administrativeBoard")
            return new XElement(Sd + "KindOfWagePayment",
                new XElement(Sd + "NoTimeConstraint",
                    new XElement(Sd + "Contract", art),
                    new XElement(Sd + "ContractualAnnualWage", Amt(em?.JahreslohnOhneZeitbindung ?? 0m))));

        var monatsvertrag = model is "FIX" or "FIX-M" or "MTP";
        if (art.Length > 0) monatsvertrag = art is "indefiniteSalaryMth" or "indefiniteSalaryMthAWT" or "fixedSalaryMth" or "apprentice" or "internshipContract";

        if (monatsvertrag)
            return new XElement(Sd + "KindOfWagePayment",
                new XElement(Sd + "Monthly",
                    // Ohne erfasste Vertragsart gilt «unbefristet»: ein Enddatum allein
                    // heisst nicht befristet — die Vertragskette setzt es auch bei
                    // Modell- und Filialwechseln (Walter 27.09.2026).
                    new XElement(Sd + "Contract", art.Length > 0 ? art : "indefiniteSalaryMth"),
                    new XElement(Sd + "ContractualMonthlyWage", Amt(em?.MonthlySalary ?? 0m)),
                    new XElement(Sd + "Contractual13th", dreizehnter)));

        var ferienProzent = Amt((filiale.DefaultVacationWeeks ?? 5) >= 6
            ? filiale.DefaultVacationPercent6Weeks ?? 13.04m
            : filiale.DefaultVacationPercent5Weeks ?? 10.65m);
        var stundenansatz = em?.HourlyRate ?? 0m;
        var lektionenansatz = em?.LessonRate ?? stundenansatz;

        return new XElement(Sd + "KindOfWagePayment",
            new XElement(Sd + "Hourly",
                new XElement(Sd + "Contract", art.Length > 0 ? art : "indefiniteSalaryHrs"),
                new XElement(Sd + "ContractualHourlyWage",
                    new XElement(Sd + "Salary",
                        new XElement(Sd + "PaidByHour", Amt(stundenansatz)),
                        new XElement(Sd + "PaidByLesson", Amt(lektionenansatz))),
                    new XElement(Sd + "Vacation", ferienProzent),
                    new XElement(Sd + "PublicHolidayCompensation", Amt(filiale.DefaultHolidayPercent ?? 4m)),
                    new XElement(Sd + "Contractual13th", dreizehnter)),
                new XElement(Sd + "TotallyWorked",
                    new XElement(Sd + "TotalHoursOfWork", Amt(Num(slip, "workedHours"))))));
    }

    /// <summary>
    /// Statistik-Stammdaten der Person. Alle vier Felder sind im Schema PFLICHT
    /// (StatisticAdditionalParticularsType): Ausbildung, berufliche Stellung,
    /// Funktion, Ferienanspruch in Tagen. Ausbildung und Stellung kommen aus den
    /// LSE-Feldern und werden hier nur uebersetzt; fehlen sie, meldet OneCrew den
    /// vorsichtigsten Wert UND sagt es.
    /// </summary>
    private XElement BaueStatistikStammdaten(
        Employee e, Employment? em, CompanyProfile filiale, JsonElement slip,
        EmployeeLse? lse, List<LseCodeMapping> stellungMapping, List<string> warn)
    {
        var ausbildungCode = lse?.Education;
        if (!ElmStatistikCodes.AusbildungErfasst(ausbildungCode))
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Ausbildung für die Statistik nicht erfasst "
                   + "— gemeldet wird «ohne abgeschlossene Berufsausbildung» (MA → BFS/Statistik).");

        var stellungCode = lse?.PositionOverride;
        if (stellungCode == null && !string.IsNullOrWhiteSpace(em?.JobTitle))
            stellungCode = stellungMapping
                .FirstOrDefault(m => string.Equals(m.SourceCode, em!.JobTitle!.Trim(), StringComparison.OrdinalIgnoreCase))?.BfsCode;
        if (!ElmStatistikCodes.StellungErfasst(stellungCode))
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): berufliche Stellung nicht erfasst "
                   + "— gemeldet wird «ohne Kaderfunktion» (LSE-Zuordnung Funktion → Stellung).");

        var stundenlohn = (em?.EmploymentModel?.ToUpperInvariant() ?? "") is not ("FIX" or "FIX-M" or "MTP");
        var ferienwochen = (filiale.DefaultVacationWeeks ?? 5) >= 6 ? 6 : 5;
        var tage = ElmStatistikCodes.Ferientage(stundenlohn, ferienwochen, lse?.LeaveEntitlementDays);

        return new XElement(Sd + "AdditionalParticulars",
            new XElement(Sd + "Education", ElmStatistikCodes.Ausbildung(ausbildungCode)),
            new XElement(Sd + "Position", ElmStatistikCodes.Stellung(stellungCode)),
            new XElement(Sd + "JobTitle", string.IsNullOrWhiteSpace(em?.JobTitle) ? "—" : em!.JobTitle!.Trim()),
            new XElement(Sd + "LeaveEntitlement", ((int)Math.Round(tage, 0)).ToString()));
    }

    /// <summary>
    /// Quellensteuer-Zeile aus dem Lohnzettel. Gemeldet wird nur, wenn der Monat
    /// tatsächlich eine QST-Zeile hat — sonst gehört die Person nicht in die
    /// QST-Meldung dieses Kantons.
    /// </summary>
    private (XElement Zeile, string Kanton, decimal Basis, decimal Steuer)? BaueQstZeile(
        Employee e, Employment? em, CompanyProfile filiale, JsonElement slip,
        List<EmployeeQuellensteuer> versionen, List<EmployeeFamilyMember> kinder,
        RechtseinheitStamm stamm, string monatStr, DateTime monatsAnfang, List<string> warn,
        EmployeeAddress? wochenAdresse = null)
    {
        if (!slip.TryGetProperty("abzugLines", out var al) || al.ValueKind != JsonValueKind.Array) return null;
        JsonElement? qstZeile = null;
        foreach (var z in al.EnumerateArray())
            if ((Str(z, "categoryCode") ?? "") == "QST") { qstZeile = z; break; }
        if (qstZeile == null) return null;

        var zeile = qstZeile.Value;
        var basis = Num(zeile, "basis");
        var satzBasis = zeile.TryGetProperty("satzBasis", out var sb) && sb.ValueKind == JsonValueKind.Number
            ? sb.GetDecimal() : basis;
        var steuer = -Num(zeile, "betrag");          // im Slip negativ
        var code = (Str(zeile, "qstCode") ?? "").Trim().ToUpperInvariant();

        var stichtag = DateOnly.FromDateTime(monatsAnfang);
        var version = QstVersionWahl.Waehle(versionen.Where(v => v.EmployeeId == e.Id).ToList(), stichtag);
        var kanton = (version?.Steuerkanton ?? "").Trim().ToUpperInvariant();
        if (kanton.Length != 2)
        {
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Quellensteuer ohne Steuerkanton — Zeile nicht gemeldet.");
            return null;
        }
        if (code.Length == 0)
        {
            warn.Add($"{e.FirstName} {e.LastName} ({e.EmployeeNumber}): Quellensteuer ohne Tarifcode — Zeile nicht gemeldet.");
            return null;
        }

        var residence = QstResidence(e.CantonCode, e.Country, version?.Wohnsitzstaat,
            version?.IsWochenaufenthalter == true, wochenAdresse);

        // Ein- und Austritt melden, wenn sie in diesen Monat fallen
        XElement? declaration = null;
        var monatsEnde = monatsAnfang.AddMonths(1).AddDays(-1);
        if (e.EntryDate is DateTime ed && ed >= monatsAnfang && ed <= monatsEnde)
            declaration = new XElement(Sd + "DeclarationCategory",
                new XElement(Sd + "Entry",
                    new XElement(Sd + "ValidAsOf", ed.ToString("yyyy-MM-dd")),
                    new XElement(Sd + "Reason", "entryCompany")));
        else if (e.ExitDate is DateTime xd && xd >= monatsAnfang && xd <= monatsEnde)
            declaration = new XElement(Sd + "DeclarationCategory",
                new XElement(Sd + "Withdrawal",
                    new XElement(Sd + "ValidAsOf", xd.ToString("yyyy-MM-dd")),
                    new XElement(Sd + "Reason", "withdrawalCompany")));

        var x = new XElement(Sd + "TaxAtSourceSalary",
            new XAttribute("addresseeIDRef", $"#QST-{kanton}"),
            QstZusatz(e, version, kinder),
            new XElement(Sd + "TaxAtSourceCanton", kanton),
            version?.QstGemeindeBfsNr is > 0 ? new XElement(Sd + "TaxAtSourceMunicipalityID", version!.QstGemeindeBfsNr!.Value) : null,
            new XElement(Sd + "CurrentMonth", monatStr),
            new XElement(Sd + "Current",
                new XAttribute("workplaceIDRef", WpId(filiale)),
                QstKategorie(code),
                new XElement(Sd + "TaxableEarning", Betrag05(basis)),
                new XElement(Sd + "AscertainedTaxableEarning", Betrag05(satzBasis)),
                new XElement(Sd + "TaxAtSource", Betrag05(steuer)),
                residence,
                stamm.GemeindeNr.TryGetValue(filiale.Id, out var wg) ? new XElement(Sd + "WorkMunicipalityID", wg) : null,
                declaration));
        return (x, kanton, basis, steuer);
    }

    /// <summary>
    /// Personenangaben der QST-Zeile: Konfession, Alleinerziehende und Kinder mit
    /// Anspruchsdauer (Walter 27.09.2026). Null = nichts zu melden.
    /// </summary>
    private static XElement? QstZusatz(Employee e, EmployeeQuellensteuer? version, List<EmployeeFamilyMember> kinder)
    {
        // Reihenfolge laut XSD: Denomination, SingleParentFamily, MarriagePartner, Children.
        var konfession = MapKonfession(e.Religion);
        XElement? alleinerziehend = null;
        if (string.Equals(version?.Halbfamilie, "ja", StringComparison.OrdinalIgnoreCase))
        {
            // Vier Fälle laut Schema: kein Konkubinat, oder im Konkubinat mit
            // alleinigem Sorgerecht / geteiltem Sorgerecht und höherem Einkommen /
            // volljährigem Kind und höherem Einkommen. Ohne erfasste Art gilt die
            // vorsichtige Annahme «kein Konkubinat».
            var art2 = (version!.SorgerechtCode ?? "").Trim();
            if (art2.Length == 0 && version.LivesInKonkubinat) art2 = "ShareCustodyAndHigherIncome";
            alleinerziehend = new XElement(Sd + "SingleParentFamily",
                art2 switch
                {
                    "SoleCustody" => new XElement(Sd + "Concubinage", new XElement(Sd + "SoleCustody")),
                    "ShareCustodyAndHigherIncome" => new XElement(Sd + "Concubinage", new XElement(Sd + "ShareCustodyAndHigherIncome")),
                    "AdultChildAndHigherIncome" => new XElement(Sd + "Concubinage", new XElement(Sd + "AdultChildAndHigherIncome")),
                    _ => new XElement(Sd + "NoConcubinage"),
                });
        }
        var kinderEl = kinder
            .Where(k => k.QstDeductibleFrom != null)
            .OrderBy(k => k.DateOfBirth)
            .Select(k => new XElement(Sd + "Children",
                new XElement(Sd + "Lastname", (k.LastName ?? e.LastName ?? "").Trim()),
                new XElement(Sd + "Firstname", (k.FirstName ?? "").Trim()),
                new XElement(Sd + "DateOfBirth", (k.DateOfBirth ?? k.QstDeductibleFrom!.Value).ToString("yyyy-MM-dd")),
                new XElement(Sd + "Start", k.QstDeductibleFrom!.Value.ToString("yyyy-MM-dd")),
                k.QstDeductibleUntil == null ? null : new XElement(Sd + "End", k.QstDeductibleUntil.Value.ToString("yyyy-MM-dd"))))
            .ToList();
        if (konfession == null && alleinerziehend == null && kinderEl.Count == 0) return null;
        return new XElement(Sd + "AdditionalParticulars",
            konfession == null ? null : new XElement(Sd + "Denomination", konfession),
            alleinerziehend,
            kinderEl);
    }

    /// <summary>
    /// QST-Wohnsitz laut Schema (TaxAtSourceResidenceType): Wohnkanton CH, sonst Wohnsitzstaat
    /// plus Pflichtangabe KindOfResidence — Wochenaufenthalt mit Adresse in der Schweiz, sonst
    /// tägliche Rückkehr (RefXML Jan 2025: TF28 Arbenz IT Weekly Bern, TF29 Forster IT Daily,
    /// TF30 Müller DE Daily).
    /// </summary>
    public static XElement QstResidence(string? wohnKanton, string? land, string? wohnsitzstaat,
        bool wochenaufenthalter, EmployeeAddress? wochenAdresse)
    {
        var kanton = (wohnKanton ?? "").Trim().ToUpperInvariant();
        var landCode = (land ?? "").Trim().ToUpperInvariant();
        if ((landCode.Length == 0 || landCode == "CH") && kanton.Length == 2)
            return new XElement(Sd + "Residence", new XElement(Sd + "CantonCH", kanton));

        var staat = !string.IsNullOrWhiteSpace(wohnsitzstaat) ? wohnsitzstaat!.Trim().ToUpperInvariant()
                  : landCode.Length == 2 && landCode != "CH" ? landCode : "XX";
        XElement art = wochenaufenthalter && wochenAdresse != null
            ? new XElement(Sd + "Weekly",
                string.IsNullOrWhiteSpace(wochenAdresse.Street) ? null : new XElement(C + "Street", wochenAdresse.Street.Trim()),
                new XElement(C + "ZIP-Code", (wochenAdresse.ZipCode ?? "").Trim()),
                new XElement(C + "City", (wochenAdresse.City ?? "").Trim()),
                new XElement(C + "Country", ElmGemeinsam.LandName(wochenAdresse.Country)))
            : new XElement(Sd + "Daily");
        return new XElement(Sd + "Residence",
            new XElement(Sd + "AbroadCountry", staat),
            new XElement(Sd + "KindOfResidence", art));
    }

    /// <summary>Tarifcode (A0N …) oder vordefinierte Kategorie (NON, MEY …) laut Schema.</summary>
    public static XElement QstKategorie(string? code)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return new XElement(Sd + "TaxAtSourceCategory", QstVordefinierteKategorie.Parse(c) != null
            ? new XElement(C + "CategoryPredefined", c)
            : new XElement(C + "TaxAtSourceCode", c));
    }

    private static bool IstNullKategorie(string? code)
        => QstVordefinierteKategorie.Parse(code) is { } k && QstVordefinierteKategorie.IstNullAbzug(k.Art);

    /// <summary>
    /// Korrektur eines Vormonats aus einem im Meldemonat verrechneten QST-Posten
    /// (Swissdec CompanyCorrection; RefXML Dez 2024 TF14 Egli, Jun 2025 TF31, Jul 2025 TF33).
    /// Old = das Gemeldete mit umgekehrtem Vorzeichen, New = die Nachrechnung. NON/NOY melden
    /// 0.00 und einen Austritt, alle anderen eine Mutation.
    /// </summary>
    public static XElement QstKorrekturBlock(QstKorrektur k, string workplaceIdRef, DateOnly gueltigAb, string grund)
    {
        var neuNull = IstNullKategorie(k.NeuerCode);
        return new XElement(Sd + "Correction",
            new XElement(Sd + "Month", $"{k.Jahr:0000}-{k.Monat:00}"),
            new XElement(Sd + "Old",
                new XAttribute("workplaceIDRef", workplaceIdRef),
                QstKategorie(k.AlterCode),
                new XElement(Sd + "TaxableEarning", Betrag05(-k.Basis)),
                new XElement(Sd + "AscertainedTaxableEarning", Betrag05(-k.SatzBasis)),
                new XElement(Sd + "TaxAtSource", Betrag05(-k.AlterBetrag))),
            new XElement(Sd + "New",
                new XAttribute("workplaceIDRef", workplaceIdRef),
                QstKategorie(k.NeuerCode),
                new XElement(Sd + "TaxableEarning", Betrag05(neuNull ? 0m : k.Basis)),
                new XElement(Sd + "AscertainedTaxableEarning", Betrag05(neuNull ? 0m : k.SatzBasis)),
                new XElement(Sd + "TaxAtSource", Betrag05(neuNull ? 0m : k.NeuerBetrag)),
                new XElement(Sd + "DeclarationCategory",
                    new XElement(Sd + (neuNull ? "Withdrawal" : "Mutation"),
                        new XElement(Sd + "ValidAsOf", gueltigAb.ToString("yyyy-MM-dd")),
                        new XElement(Sd + "Reason", grund)))));
    }

    /// <summary>Summen-Wirkung einer Korrektur (New + Old), je Betrag auf 5 Rappen.</summary>
    public static (decimal Basis, decimal Steuer) QstKorrekturWirkung(QstKorrektur k)
    {
        var neuNull = IstNullKategorie(k.NeuerCode);
        return (PayrollCalculations.Round05(neuNull ? 0m : k.Basis) - PayrollCalculations.Round05(k.Basis),
                PayrollCalculations.Round05(neuNull ? 0m : k.NeuerBetrag) - PayrollCalculations.Round05(k.AlterBetrag));
    }

    /// <summary>
    /// Grund der Korrektur laut Schema. Rückwirkend nicht pflichtig: C-Bewilligung →
    /// settled-C, Schweizer → naturalization. Codewechsel: Buchstabe → civilstate,
    /// Kinderzahl → childrenDeduction, Kirchensteuer → churchTax, Kanton → residence.
    /// </summary>
    public static string QstKorrekturGrund(QstKorrektur k, string? bewilligungAmEnde, string? nationalitaet,
                                           string? kantonAlt, string? kantonNeu)
    {
        if (IstNullKategorie(k.NeuerCode))
            return string.Equals(bewilligungAmEnde, "C", StringComparison.OrdinalIgnoreCase) ? "settled-C"
                 : string.Equals(nationalitaet, "CH", StringComparison.OrdinalIgnoreCase) ? "naturalization"
                 : "others";
        var alt = System.Text.RegularExpressions.Regex.Match((k.AlterCode ?? "").Trim().ToUpperInvariant(), @"^([A-Z]{1,2})(\d)([YN])$");
        var neu = System.Text.RegularExpressions.Regex.Match((k.NeuerCode ?? "").Trim().ToUpperInvariant(), @"^([A-Z]{1,2})(\d)([YN])$");
        if (!string.IsNullOrWhiteSpace(kantonAlt) && !string.IsNullOrWhiteSpace(kantonNeu)
            && !string.Equals(kantonAlt.Trim(), kantonNeu.Trim(), StringComparison.OrdinalIgnoreCase))
            return "residence";
        if (!alt.Success || !neu.Success) return "others";
        if (alt.Groups[1].Value != neu.Groups[1].Value) return "civilstate";
        if (alt.Groups[2].Value != neu.Groups[2].Value) return "childrenDeduction";
        if (alt.Groups[3].Value != neu.Groups[3].Value) return "churchTax";
        return "others";
    }

    /// <summary>Konfession → Swissdec-Denomination. Unbekannt = kein Element.</summary>
    /// <summary>
    /// Konfession → Swissdec <c>DenominationType</c>. Die erlaubten Werte stehen im
    /// Schema: romanCatholic, christianCatholic, reformedEvangelical, jewishCommunity,
    /// otherOrNone. Unbekannt = kein Element (lieber keine Angabe als eine falsche).
    /// </summary>
    public static string? MapKonfession(string? religion)
    {
        var r = (religion ?? "").ToLowerInvariant();
        if (r.Length == 0) return null;
        if (r.Contains("christkath") || r.Contains("altkath")) return "christianCatholic";
        if (r.Contains("roem") || r.Contains("röm") || r.Contains("katholisch")) return "romanCatholic";
        if (r.Contains("reformiert") || r.Contains("evang") || r.Contains("protest")) return "reformedEvangelical";
        if (r.Contains("jued") || r.Contains("jüd") || r.Contains("israel")) return "jewishCommunity";
        if (r.Contains("keine") || r.Contains("konfessionslos") || r.Contains("ohne") || r.Contains("andere")) return "otherOrNone";
        return null;
    }
}
