using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 4d: Lohnausweis-Angaben der Testfälle (PersonTAX… in testcases_export.csv) in die
/// Lohnausweis-Angaben der Person. Eigener Schritt, damit nichts an Personen und Mutationen ändert.
/// Beträge für Umzugskosten gelten je Jahr, darum je Jahr eine Fassung.
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
                ["Spesen"] = grund.SpesenModus != null ? $"{grund.SpesenDatum:dd.MM.yyyy} {grund.SpesenKanton}" : grund.Rz52 == true ? "RZ 52" : null,
                ["Ziffer 14"] = grund.Ziffer14Text,
                ["Umzug"] = umzug?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
            };
            var bisher = await _db.EmployeeLohnausweise.Where(x => x.EmployeeId == emp.Id).ToListAsync();
            aktionen.Add(new Aktion(bisher.Count == 0 ? "anlegen" : "aktualisieren", "Lohnausweis-Angaben", $"{f.Id} · {f.Name}", felder));
            if (vorschau) continue;
            _db.EmployeeLohnausweise.RemoveRange(bisher);
            _db.EmployeeLohnausweise.AddRange(fassungen);
        }
        if (!vorschau) await _db.SaveChangesAsync();
        return Ok(new SchrittErgebnis("4d · Lohnausweis-Angaben", vorschau, aktionen, hinweise));
    }

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
