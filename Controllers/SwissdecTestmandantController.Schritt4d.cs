using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 4d: Lohnausweis-Angaben der Testfälle (PersonTAX… in testcases_export.csv) in die
/// Lohnausweis-Angaben der Person. Eigener Schritt, damit nichts an Personen und Mutationen ändert.
/// Was der Testfall nicht vorgibt, ist ausgeschaltet (KEIN), damit keine Filial-Vorgabe durchschlägt.
/// Beträge für Umzugskosten gelten je Jahr, darum je Jahr eine Fassung.
/// Dazu «versichert seit» von UVG und BVG aus company_export.csv.
/// </summary>
public partial class SwissdecTestmandantController
{
    private const string TmZweiteNebenleistung = "Stark verbilligte Abgabe eines Personenwagens";

    [HttpGet("schritt4d/vorschau")]
    public async Task<IActionResult> Schritt4dVorschau() => await Schritt4d(true);

    [HttpPost("schritt4d/anlegen")]
    public async Task<IActionResult> Schritt4dAnlegen() => await Schritt4d(false);

    private async Task<IActionResult> Schritt4d(bool vorschau)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var pfad = CsvPfad("testcases_export.csv");
        var firmaPfad = CsvPfad("company_export.csv");
        if (!System.IO.File.Exists(pfad) || !System.IO.File.Exists(firmaPfad))
            return NotFound(new { error = "CSV_FEHLT", message = $"Datei fehlt: {pfad}" });

        var firmaText = System.IO.File.ReadLines(firmaPfad)
            .FirstOrDefault(l => l.StartsWith("CompanyTaxOtherFringeBenefits,"))?.Split(',')[1].Trim();

        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();
        foreach (var f in LeseTestfaelle(pfad))
        {
            string? V(string k) => f.W.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) && v != "None" ? v.Trim() : null;
            bool X(string k) => V(k) != null;

            var persNr = NummerOhneKomma(V("PersonEmployeeNumber")) ?? f.Id;
            var emp = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.EmployeeNumber == persNr);
            if (emp == null) { hinweise.Add($"{f.Name}: Person Nr. {persNr} fehlt, zuerst Schritt 4a."); continue; }

            var grund = new EmployeeLohnausweis
            {
                EmployeeId = emp.Id,
                Ziffer14Modus = "KEIN",
                SpesenModus = "KEIN",
                WertModus = "KEIN",
                ExpatModus = "KEIN",
                BeteiligungModus = "KEIN",
                BoxF = X("PersonTAXFreeTransport"),
                BoxG = X("PersonTAXCanteenLunchCheck"),
                WagenAbklaeren = X("PersonTAXCompanyCarClarify"),
                WagenMinimal = X("PersonTAXMinimalEmployeeCarPartPercentage"),
                Rz52 = X("PersonTAXChargesRuleGuidance"),
                KinderzulageAhv = X("PersonTAXChildAllowancePerAHVAVS"),
            };
            var nebenleistung = V("PersonTAXOtherFringeBenefits");
            if (nebenleistung != null)
            {
                grund.Ziffer14Modus = "EIGEN";
                grund.Ziffer14Text = nebenleistung.EndsWith("2") ? $"{firmaText}, {TmZweiteNebenleistung}" : firmaText;
            }
            if (TmGrant(V("PersonTAXChargesRuleDate"), V("PersonTAXChargesRuleCanton")) is { } spesen)
            {
                grund.SpesenModus = "EIGEN"; grund.SpesenDatum = spesen.Datum; grund.SpesenKanton = spesen.Kanton;
            }
            if (TmGrant(V("PersonTAXStaffShareMarketValueDate"), V("PersonTAXStaffShareMarketValueCanton")) is { } wert)
            {
                grund.WertModus = "EIGEN"; grund.WertDatum = wert.Datum; grund.WertKanton = wert.Kanton;
            }
            if (TmGrant(V("PersonTAXExpatriateRulingDate"), V("PersonTAXExpatriateRulingCanton")) is { } expat)
            {
                grund.ExpatModus = "EIGEN"; grund.ExpatDatum = expat.Datum; grund.ExpatKanton = expat.Kanton;
            }
            if (X("PersonTAXBlockedOptions") || X("PersonTAXUnquotedOptions") || X("PersonTAXDeferredBenefitsStaffShares"))
            {
                grund.BeteiligungModus = "EIGEN";
                grund.BeteiligungGesperrt = X("PersonTAXBlockedOptions");
                grund.BeteiligungNichtKotiert = X("PersonTAXUnquotedOptions");
                grund.BeteiligungAnwartschaft = X("PersonTAXDeferredBenefitsStaffShares");
            }

            var umzug = decimal.TryParse(V("PersonTAXRelocationCosts"), System.Globalization.CultureInfo.InvariantCulture, out var u) ? u : (decimal?)null;
            var fassungen = umzug == null
                ? new List<EmployeeLohnausweis> { grund }
                : new[] { 2024, 2025, 2026 }.Select(jahr =>
                {
                    var kopie = TmKopie(grund);
                    kopie.GueltigAb = jahr;
                    kopie.GueltigBis = jahr == 2026 ? null : jahr;
                    kopie.UmzugBetrag = umzug;
                    kopie.UmzugJahr = jahr;
                    return kopie;
                }).ToList();

            var felder = new Dictionary<string, string?>
            {
                ["Spesen"] = grund.SpesenDatum != null ? $"{grund.SpesenDatum:dd.MM.yyyy} {grund.SpesenKanton}" : grund.Rz52 == true ? "RZ 52" : null,
                ["Ziffer 14"] = grund.Ziffer14Text,
                ["Umzug"] = umzug?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            };
            var bisher = await _db.EmployeeLohnausweise.Where(x => x.EmployeeId == emp.Id).ToListAsync();
            aktionen.Add(new Aktion(bisher.Count == 0 ? "anlegen" : "aktualisieren", "Lohnausweis-Angaben", $"{f.Id} · {f.Name}", felder));
            if (vorschau) continue;
            _db.EmployeeLohnausweise.RemoveRange(bisher);
            _db.EmployeeLohnausweise.AddRange(fassungen);
        }
        // Lohnausweis-Texte kommen aus der Lohnposition: dort die Bezeichnung der Testdaten
        // (Quality Tool 2025-01: TF37 Oberli, 1210 «Bonuszahlung» statt «McBonus»).
        var lohnarten = CsvPfad("wagetypes_export.csv");
        if (System.IO.File.Exists(lohnarten))
        {
            var texte = System.IO.File.ReadLines(lohnarten).Skip(1).Select(l => l.Split(','))
                .Where(s => s.Length > 2 && s[1].Length > 0 && s[2].Length > 0)
                .GroupBy(s => s[1]).ToDictionary(g => g.Key, g => g.First()[2].Trim());
            foreach (var lp in await _db.Lohnpositionen.Where(l => texte.Keys.Contains(l.Code)).ToListAsync())
            {
                if (lp.Bezeichnung.Trim() == texte[lp.Code]) continue;
                aktionen.Add(new Aktion("aktualisieren", "Lohnposition", lp.Code, new Dictionary<string, string?> { ["Bezeichnung"] = texte[lp.Code] }));
                if (!vorschau) lp.Bezeichnung = texte[lp.Code];
            }
        }
        // Grenzgänger Italien: Steuernummern der Testfälle und Mutationen tragen den Datenaustausch, sonst keiner.
        var steuerIds = new[] { pfad, CsvPfad("testcase_differences_export.csv") }.Where(System.IO.File.Exists)
            .SelectMany(System.IO.File.ReadLines)
            .SelectMany(l => System.Text.RegularExpressions.Regex.Matches(l, @"[A-Z]{6}\d{2}[A-Z]\d{2}[A-Z]\d{3}[A-Z]").Select(m => m.Value))
            .ToHashSet();
        foreach (var q in await _db.EmployeeQuellensteuer.ToListAsync())
        {
            var soll = q.GrenzgaengerSteuerId != null && steuerIds.Contains(q.GrenzgaengerSteuerId.Trim());
            if (q.DatenaustauschItalien == soll) continue;
            aktionen.Add(new Aktion("aktualisieren", "Quellensteuer", $"Mitarbeiter {q.EmployeeId} ab {q.ValidFrom:dd.MM.yyyy}",
                new Dictionary<string, string?> { ["Datenaustausch Italien"] = soll ? "ja" : "nein" }));
            if (!vorschau) q.DatenaustauschItalien = soll;
        }
        // Reihenfolge der FAK-Totale wie die Referenz (Quality Tool vergleicht der Reihe nach, Frage 13 an Swissdec).
        var fakFolge = new[] { "003", "079", "002", "022", "019", "021" };
        foreach (var k in await _db.LohndatenEmpfaengers.Where(x => x.Art == "FAK" && x.IsActive).ToListAsync())
        {
            var nr = Array.IndexOf(fakFolge, (k.Kassennummer ?? "").Trim());
            int? soll = nr >= 0 ? nr + 1 : null;
            if (k.MeldungReihenfolge == soll) continue;
            aktionen.Add(new Aktion("aktualisieren", "FAK-Kasse", k.Bezeichnung, new Dictionary<string, string?> { ["Reihenfolge"] = soll?.ToString() }));
            if (!vorschau) k.MeldungReihenfolge = soll;
        }
        // Mutationen, die in den Lohnausweis-Angaben oder der Historie wirken: Wegzug wird erst im Mutationsmonat bekannt
        // (TF36 Maldini 28.08., gemeldet ab September), Aufteilung negativer AHV-Löhne (TF44 Lusser, TF09 Estermann).
        var personJeFall = new Dictionary<string, int>();
        foreach (var f in LeseTestfaelle(pfad))
        {
            var nr = NummerOhneKomma(f.W.GetValueOrDefault("PersonEmployeeNumber")) ?? f.Id;
            var id = await _db.Employees.AsNoTracking().Where(e => e.EmployeeNumber == nr).Select(e => (int?)e.Id).FirstOrDefaultAsync();
            if (id != null) personJeFall[f.Id] = id.Value;
        }
        var splits = new Dictionary<(int Emp, int Jahr), EmployeeAhvSplit>();
        foreach (var datei in new[] { "testcase_differences_export.csv", "testcase_differences_zusatz.csv" }.Select(CsvPfad).Where(System.IO.File.Exists))
            foreach (var zeile in System.IO.File.ReadLines(datei).Skip(1))
            {
                var s = zeile.Split(',');
                if (s.Length < 6 || !personJeFall.TryGetValue(s[0].Split(' ')[0], out var empId)) continue;
                var neuWert = s[^1].Trim();
                if (neuWert.Length == 0 || !DateOnly.TryParse(s[^3], System.Globalization.CultureInfo.InvariantCulture, out var mutation)) continue;
                if (s[1] == "PersonDepartureDate" && Datum(neuWert) is DateOnly wegzug)
                {
                    var ab = wegzug.AddDays(1);
                    var umzug = await _db.EmployeeWohnortHistories.FirstOrDefaultAsync(h => h.EmployeeId == empId && h.GueltigAb == ab);
                    if (umzug != null && umzug.ErfahrenAm != mutation)
                    {
                        aktionen.Add(new Aktion("aktualisieren", "Wohnort", $"{s[0]} ab {ab:dd.MM.yyyy}", new Dictionary<string, string?> { ["Erfahren am"] = mutation.ToString("dd.MM.yyyy") }));
                        if (!vorschau) umzug.ErfahrenAm = mutation;
                    }
                }
                if (s[1] is not ("PersonSplitPreviousYearIncome" or "PersonSplitPreviousYearPeriodFrom" or "PersonSplitPreviousYearPeriodUntil" or "PersonAdditionalDeliveryDate")) continue;
                if (!splits.TryGetValue((empId, mutation.Year), out var split))
                    splits[(empId, mutation.Year)] = split = new EmployeeAhvSplit { EmployeeId = empId, Jahr = mutation.Year };
                switch (s[1])
                {
                    case "PersonSplitPreviousYearIncome": split.VorjahrLohn = decimal.Parse(neuWert, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "PersonSplitPreviousYearPeriodFrom": split.VorjahrVon = Datum(neuWert); break;
                    case "PersonSplitPreviousYearPeriodUntil": split.VorjahrBis = Datum(neuWert); break;
                    case "PersonAdditionalDeliveryDate": split.NachlieferungAm = Datum(neuWert); break;
                }
            }
        aktionen.AddRange(splits.Values.Select(sp => new Aktion("aktualisieren", "AHV-Aufteilung", $"Mitarbeiter {sp.EmployeeId}, {sp.Jahr}", new Dictionary<string, string?>
        {
            ["Vorjahr"] = sp.VorjahrLohn?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            ["Mitteilung am"] = sp.NachlieferungAm?.ToString("dd.MM.yyyy"),
        })));
        if (!vorschau)
        {
            _db.EmployeeAhvSplits.RemoveRange(_db.EmployeeAhvSplits);
            _db.EmployeeAhvSplits.AddRange(splits.Values);
        }

        var uvgSeit = TmFirmaDatum(firmaPfad, "CompanyAHVAVSUVGLAAInsurerValidAsOf");
        var bvgSeit = TmFirmaDatum(firmaPfad, "CompanyAHVAVSBVGLPPInsurerValidAsOf");
        aktionen.Add(new Aktion("aktualisieren", "Stammdaten", "Versichert seit", new Dictionary<string, string?>
        {
            ["UVG"] = uvgSeit?.ToString("dd.MM.yyyy"), ["BVG"] = bvgSeit?.ToString("dd.MM.yyyy"),
        }));
        if (!vorschau)
        {
            var st = await _db.ElmStammdaten.OrderBy(x => x.Id).FirstOrDefaultAsync();
            if (st == null) _db.ElmStammdaten.Add(st = new ElmStammdaten());
            st.UvgVersichertSeit = uvgSeit;
            st.BvgVersichertSeit = bvgSeit;
            st.UpdatedAt = DateTime.Now;
        }
        if (!vorschau) await _db.SaveChangesAsync();
        return Ok(new SchrittErgebnis("4d · Lohnausweis-Angaben", vorschau, aktionen, hinweise));
    }

    /// <summary>Einzelne Lohnzettel neu rechnen, die vor einer Korrektur abgeschlossen wurden; der Rest bleibt unberührt.</summary>
    private static readonly (string Nr, int Jahr, int Monat)[] TmNeuRechnen =
    {
        ("1", 2025, 1), ("15", 2025, 3), ("11", 2025, 3),
        ("1", 2025, 10), ("11", 2025, 10), ("40", 2025, 10),
        ("11", 2025, 11),
        ("11", 2025, 12), ("9", 2025, 12),
    };

    [HttpGet("schritt5d/vorschau")]
    public async Task<IActionResult> Schritt5dVorschau() => await Schritt5d(true);

    [HttpPost("schritt5d/anlegen")]
    public async Task<IActionResult> Schritt5dAnlegen() => await Schritt5d(false);

    private async Task<IActionResult> Schritt5d(bool vorschau)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var recompute = HttpContext.RequestServices.GetRequiredService<HrSystem.Services.SnapshotRecomputeService>();
        var aktionen = new List<Aktion>();
        foreach (var (nr, jahr, monat) in TmNeuRechnen)
        {
            var snap = await (from s in _db.PayrollSnapshots
                              join p in _db.PayrollPerioden on s.PayrollPeriodeId equals p.Id
                              join e in _db.Employees on s.EmployeeId equals e.Id
                              where e.EmployeeNumber == nr && p.Year == jahr && p.Month == monat && s.Status != "STORNIERT"
                              select new { s.Id, s.EmployeeId, p.CompanyProfileId, s.Netto, s.SvBasisAhv, e.LastName }).FirstOrDefaultAsync();
            if (snap == null) continue;
            var felder = new Dictionary<string, string?> { ["vorher Netto"] = snap.Netto.ToString("0.00"), ["vorher AHV-Basis"] = snap.SvBasisAhv.ToString("0.00") };
            if (!vorschau)
            {
                await recompute.RecomputeAsync(snap.CompanyProfileId, jahr, monat, snap.EmployeeId);
                await _db.SaveChangesAsync();
                var neu = await _db.PayrollSnapshots.AsNoTracking().Where(s => s.Id == snap.Id).Select(s => new { s.Netto, s.SvBasisAhv }).FirstAsync();
                felder["Netto"] = neu.Netto.ToString("0.00");
                felder["AHV-Basis"] = neu.SvBasisAhv.ToString("0.00");
            }
            aktionen.Add(new Aktion("aktualisieren", "Lohnzettel", $"{snap.LastName} {monat:00}.{jahr}", felder));
        }
        return Ok(new SchrittErgebnis("5d · Einzelne Lohnzettel neu rechnen", vorschau, aktionen, new List<string>()));
    }

    private static DateOnly? TmFirmaDatum(string pfad, string feld) =>
        DateOnly.TryParse(System.IO.File.ReadLines(pfad).FirstOrDefault(l => l.StartsWith(feld + ","))?.Split(',')[1],
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    private static (DateOnly Datum, string Kanton)? TmGrant(string? datum, string? kanton) =>
        DateOnly.TryParse(datum, System.Globalization.CultureInfo.InvariantCulture, out var d) && !string.IsNullOrWhiteSpace(kanton)
            ? (d, kanton.Trim()) : null;

    private static EmployeeLohnausweis TmKopie(EmployeeLohnausweis q) => new()
    {
        EmployeeId = q.EmployeeId, BoxF = q.BoxF, BoxG = q.BoxG, WagenAbklaeren = q.WagenAbklaeren, WagenMinimal = q.WagenMinimal,
        Rz52 = q.Rz52, KinderzulageAhv = q.KinderzulageAhv, Ziffer14Modus = q.Ziffer14Modus, Ziffer14Text = q.Ziffer14Text,
        SpesenModus = q.SpesenModus, SpesenDatum = q.SpesenDatum, SpesenKanton = q.SpesenKanton,
        WertModus = q.WertModus, WertDatum = q.WertDatum, WertKanton = q.WertKanton,
        ExpatModus = q.ExpatModus, ExpatDatum = q.ExpatDatum, ExpatKanton = q.ExpatKanton,
        BeteiligungModus = q.BeteiligungModus, BeteiligungGesperrt = q.BeteiligungGesperrt,
        BeteiligungNichtKotiert = q.BeteiligungNichtKotiert, BeteiligungAnwartschaft = q.BeteiligungAnwartschaft,
    };
}
