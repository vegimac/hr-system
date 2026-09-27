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

    public ElmMonthlyDeclarationBuilder(AppDbContext db, ElmXmlValidator validator)
    {
        _db = db;
        _validator = validator;
    }

    public record BuildResult(
        string Xml, int Personen, int QstZeilen, int StatistikZeilen,
        List<string> Warnungen, List<string> XsdFehler);

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

        var qstVersionen = await _db.EmployeeQuellensteuer.AsNoTracking()
            .Where(q => empIds.Contains(q.EmployeeId))
            .ToListAsync(ct);

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

                // Eintritt = Beginn der Anstellung, die IN DIESEM MONAT galt — nicht das
                // Eintrittsdatum am MA (Walter 27.09.2026, TF16 Aebi: der Wiedereintritt
                // 15.01.2025 gehört nicht in die Novembermeldung 2024).
                var eintrittMonat = em?.ContractStartDate ?? e.EntryDate ?? monatsAnfang;
                lseById.TryGetValue(e.Id, out var lse);
                statistikZeilen.Add(BaueStatistikZeile(e, em, filiale, slip, lohnartByCode, lse, stellungMapping,
                                                       monatStr, monatsAnfang, monatsEnde, eintrittMonat, warn));
                statZeilen++;

                var qst = BaueQstZeile(e, em, filiale, slip, qstVersionen, stamm, monatStr, monatsAnfang, warn);
                if (qst != null)
                {
                    qstZeilenPerson.Add(qst.Value.Zeile);
                    qstKantone.Add(qst.Value.Kanton);
                    var bisher = qstTotal.TryGetValue(qst.Value.Kanton, out var t) ? t : (0m, 0m);
                    qstTotal[qst.Value.Kanton] = (bisher.Item1 + qst.Value.Basis, bisher.Item2 + qst.Value.Steuer);
                    qstZeilen++;
                }
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
                Particulars(e, warn, WohnGemeinde(e)),
                new XElement(C + "Work",
                    new XAttribute("workID", WorkId(e)),
                    modell != null ? new XAttribute("companyWorkingTimeIDRef", "#" + modell.KennungOderId) : null,
                    new XElement(C + "WorkingTime", WorkingTime(emHaupt, stamm.Haupt.NormalWeeklyHours ?? 42m)),
                    new XElement(C + "EntryDate", (emHaupt?.ContractStartDate ?? e.EntryDate ?? monatsAnfang).ToString("yyyy-MM-dd"))),
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
                new XElement(Sd + "CurrentMonth", monatStr)))).ToList();

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

        var kontakt = await _db.AppUsers.AsNoTracking()
            .Where(u => u.IsActive && u.Role == "admin")
            .OrderBy(u => u.Id).FirstOrDefaultAsync(ct);

        var jetzt = DateTime.Now;
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null),
            new XElement(Sdcst + "DeclareMonthlySalary",
                new XAttribute(XNamespace.Xmlns + "sdcst", Sdcst),
                new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
                new XAttribute(XNamespace.Xmlns + "sd", Sd),
                new XAttribute(XNamespace.Xmlns + "ep", Ep),
                new XAttribute(XNamespace.Xmlns + "c", C),
                RequestContext(stamm.Firmenname, jetzt),
                new XElement(Sdc + "Job",
                    new XElement(Sdc + "Addressees", addressees),
                    // Übungs-/Testmeldung — nie als Produktivmeldung werten
                    new XElement(Sdc + "TestCase")),
                new XElement(Sd + "MonthlySalaryDeclaration",
                    new XAttribute("schemaVersion", "0.0"),
                    CompanyDescription(stamm, arbeitszeit),
                    new XElement(Sd + "Staff", personen),
                    new XElement(Sd + "Institutions", institutions),
                    totals.Count > 0 ? new XElement(Sd + "SalaryTotals", totals) : null,
                    new XElement(Sd + "SalaryCounters",
                        qstZeilen > 0 ? new XElement(Sd + "NumberOf-TaxAtSourceSalary-Tags", qstZeilen) : null,
                        statZeilen > 0 ? new XElement(Sd + "NumberOf-StatisticSalary-Tags", statZeilen) : null),
                    new XElement(Sd + "ContactPerson",
                        new XElement(Sd + "Name", ((kontakt?.FirstName + " " + kontakt?.LastName) ?? "").Trim() is { Length: > 0 } n ? n : stamm.Firmenname),
                        string.IsNullOrWhiteSpace(kontakt?.Email) ? null : new XElement(Sd + "EmailAddress", kontakt!.Email!.Trim()),
                        string.IsNullOrWhiteSpace(stamm.Haupt.Phone) ? null : new XElement(Sd + "PhoneNumber", stamm.Haupt.Phone!.Trim())))));

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
        DateTime von, DateTime bis, DateTime eintritt, List<string> warn)
    {
        var topf = new Dictionary<ElmStatistikCodes.Topf, decimal>();
        void Buche(ElmStatistikCodes.Topf t, decimal betrag)
            => topf[t] = (topf.TryGetValue(t, out var v) ? v : 0m) + betrag;
        decimal Wert(ElmStatistikCodes.Topf t) => topf.TryGetValue(t, out var v) ? v : 0m;

        if (slip.TryGetProperty("lohnLines", out var ll) && ll.ValueKind == JsonValueKind.Array)
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
                    warn.Add($"Lohnzeile «{(Str(z, "bezeichnung") ?? code)}» ohne Swissdec-Lohnart — als Bruttolohn gemeldet "
                           + "(Lohnpositionen → Swissdec-Lohnart setzen).");
                    continue;
                }
                var ziel = ElmStatistikCodes.TopfFuer(lohnart);
                if (ziel == null)
                {
                    Buche(ElmStatistikCodes.Topf.Bruttolohn, betrag);
                    warn.Add($"Lohnart {lohnart} ist keinem Statistik-Topf zugeordnet — als Bruttolohn gemeldet. "
                           + "Zuordnung in ElmStatistikCodes.TopfFuer ergänzen (Richtlinien Kap. 12).");
                    continue;
                }
                Buche(ziel.Value, betrag);
            }

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
                else if (cat == "BVG") bvg += betrag;
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
                // BVG-Fixbetrag der Kasse: NICHT auf 5 Rappen runden (Walter 21.09.2026)
                new XElement(Sd + "BVG-LPP-RegularContribution", Amt(bvg)),
                new XElement(Sd + "ShortTimeWorkCompensation", Betrag05(Wert(ElmStatistikCodes.Topf.Kurzarbeit)))),
            new XElement(Sd + "AnnualValues",
                new XElement(Sd + "Period",
                    // Beginn = Eintritt, wenn er in diesen Monat fällt (Walter 27.09.2026).
                    new XElement(Ep + "from", (eintritt > von && eintritt <= bis ? eintritt : von).ToString("yyyy-MM-dd")),
                    new XElement(Ep + "until", bis.ToString("yyyy-MM-dd"))),
                new XElement(Sd + "Overtime", Betrag05(Wert(ElmStatistikCodes.Topf.Ueberstunden))),
                new XElement(Sd + "Earnings13th", Betrag05(Wert(ElmStatistikCodes.Topf.Dreizehnter))),
                new XElement(Sd + "SporadicBenefits", Betrag05(Wert(ElmStatistikCodes.Topf.Unregelmaessig))),
                new XElement(Sd + "FringeBenefits", Betrag05(Wert(ElmStatistikCodes.Topf.Naturalleistungen))),
                new XElement(Sd + "CapitalPayment", Betrag05(Wert(ElmStatistikCodes.Topf.Kapitalleistung))),
                new XElement(Sd + "OtherBenefits", Betrag05(Wert(ElmStatistikCodes.Topf.Uebrige)))));
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
        var befristet = em?.ContractEndDate != null;

        if (model is "FIX" or "FIX-M" or "MTP")
            return new XElement(Sd + "KindOfWagePayment",
                new XElement(Sd + "Monthly",
                    new XElement(Sd + "Contract", befristet ? "fixedSalaryMth" : "indefiniteSalaryMth"),
                    new XElement(Sd + "ContractualMonthlyWage", Amt(em?.MonthlySalary ?? 0m)),
                    new XElement(Sd + "Contractual13th", dreizehnter)));

        var ferienProzent = Amt((filiale.DefaultVacationWeeks ?? 5) >= 6
            ? filiale.DefaultVacationPercent6Weeks ?? 13.04m
            : filiale.DefaultVacationPercent5Weeks ?? 10.65m);
        var stundenansatz = em?.HourlyRate ?? 0m;
        var lektionenansatz = em?.LessonRate ?? stundenansatz;
        var stunden = Num(slip, "workedHours");

        return new XElement(Sd + "KindOfWagePayment",
            new XElement(Sd + "Hourly",
                new XElement(Sd + "Contract", befristet ? "fixedSalaryHrs" : "indefiniteSalaryHrs"),
                new XElement(Sd + "ContractualHourlyWage",
                    new XElement(Sd + "Salary",
                        new XElement(Sd + "PaidByHour", Amt(stundenansatz)),
                        new XElement(Sd + "PaidByLesson", Amt(lektionenansatz))),
                    new XElement(Sd + "Vacation", ferienProzent),
                    new XElement(Sd + "PublicHolidayCompensation", Amt(filiale.DefaultHolidayPercent ?? 4m)),
                    new XElement(Sd + "Contractual13th", dreizehnter)),
                new XElement(Sd + "TotallyWorked",
                    new XElement(Sd + "TotalHoursOfWork", Amt(stunden)))));
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
        List<EmployeeQuellensteuer> versionen, RechtseinheitStamm stamm,
        string monatStr, DateTime monatsAnfang, List<string> warn)
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

        var wohnKanton = (e.CantonCode ?? "").Trim().ToUpperInvariant();
        var wohnAusland = !string.IsNullOrWhiteSpace(e.Country) && e.Country!.Trim().ToUpperInvariant() != "CH";
        var residence = wohnAusland || wohnKanton.Length != 2
            ? new XElement(Sd + "Residence", new XElement(Sd + "CountryAbroad",
                  string.IsNullOrWhiteSpace(version?.Wohnsitzstaat) ? "XX" : version!.Wohnsitzstaat!.Trim().ToUpperInvariant()))
            : new XElement(Sd + "Residence", new XElement(Sd + "CantonCH", wohnKanton));

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

        var konfession = MapKonfession(e.Religion);
        var x = new XElement(Sd + "TaxAtSourceSalary",
            new XAttribute("addresseeIDRef", $"#QST-{kanton}"),
            konfession == null ? null : new XElement(Sd + "AdditionalParticulars",
                new XElement(Sd + "Denomination", konfession)),
            new XElement(Sd + "TaxAtSourceCanton", kanton),
            version?.QstGemeindeBfsNr is > 0 ? new XElement(Sd + "TaxAtSourceMunicipalityID", version!.QstGemeindeBfsNr!.Value) : null,
            new XElement(Sd + "CurrentMonth", monatStr),
            new XElement(Sd + "Current",
                new XAttribute("workplaceIDRef", WpId(filiale)),
                new XElement(Sd + "TaxAtSourceCategory", new XElement(C + "TaxAtSourceCode", code)),
                new XElement(Sd + "TaxableEarning", Betrag05(basis)),
                new XElement(Sd + "AscertainedTaxableEarning", Betrag05(satzBasis)),
                new XElement(Sd + "TaxAtSource", Betrag05(steuer)),
                residence,
                stamm.GemeindeNr.TryGetValue(filiale.Id, out var wg) ? new XElement(Sd + "WorkMunicipalityID", wg) : null,
                declaration));
        return (x, kanton, basis, steuer);
    }

    /// <summary>Konfession → Swissdec-Denomination. Unbekannt = kein Element.</summary>
    public static string? MapKonfession(string? religion)
    {
        var r = (religion ?? "").ToLowerInvariant();
        if (r.Length == 0) return null;
        if (r.Contains("roem") || r.Contains("röm") || r.Contains("katholisch") && !r.Contains("christ")) return "romanCatholic";
        if (r.Contains("christkath") || r.Contains("altkath")) return "christCatholic";
        if (r.Contains("reformiert") || r.Contains("evang") || r.Contains("protest")) return "protestant";
        if (r.Contains("keine") || r.Contains("konfessionslos") || r.Contains("ohne")) return "other";
        return null;
    }
}
