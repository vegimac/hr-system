using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Models;
using HrSystem.Services;
using System.Globalization;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 5 des Swissdec-Testmandanten — Lohnläufe Nov 2024 bis Feb 2026.
///   5a: Filial-Einstellungen der Muster AG (Ferien 8.33 %, Feiertag 4 %, keine
///       Alterserhöhung, 13. ML im Dezember) + Lohnperioden pro Filiale.
///   5b: Monatswerte aus wagetypes_export.csv pro Monat:
///       1000 Monatslohn → Vertrag (neuer Abschnitt bei Änderung)
///       1005 Stundenlohn → Stunden des Monats (Mutation «Anzahl Stunden») als
///            Stempelzeiten, gleichmässig auf die Werktage verteilt («Swissdec-Testdaten»)
///       1160/1161/1162/1163/1201/1202 → rechnet OneCrew selbst (Ferien-%,
///            Feiertag-%, 13. ML monatlich) — nur Soll-Vergleich
///       1200 13. ML Monatslöhner → OneCrew rechnet; ausserhalb des Filial-
///            Rasters (Muster AG = Dezember) legt 5b den Auslöser 180.3 an
///            (Betrag 0 — Engine zahlt Pott + Monat, Fibu RST-Abbau wie Dezember)
///   5c: Lohnzettel + Saldi der Muster AG löschen, Perioden wieder offen
///            (für Neu-Rechnung 1/12). Stammdaten/Zulagen bleiben.
///       5050 BVG-Beitrag → BVG-Fixbetrag am Versicherungs-Eintrag
///       alle übrigen → Zulagen/Abzüge der Periode (LohnZulage) auf der Lohnposition
///            über das Feld SwissdecLohnart der Lohnposition (Schritt 4b)
/// </summary>
public partial class SwissdecTestmandantController
{
    [HttpGet("schritt5a/vorschau")]
    public async Task<IActionResult> Schritt5aVorschau() => await Schritt5a(true);
    [HttpPost("schritt5a/anlegen")]
    public async Task<IActionResult> Schritt5aAnlegen() => await Schritt5a(false);

    [HttpGet("schritt5b/vorschau")]
    public async Task<IActionResult> Schritt5bVorschau([FromQuery] string? monat, [FromQuery] string? nur) => await Schritt5b(true, monat, nur);
    [HttpPost("schritt5b/anlegen")]
    public async Task<IActionResult> Schritt5bAnlegen([FromQuery] string? monat, [FromQuery] string? nur) => await Schritt5b(false, monat, nur);

    [HttpGet("schritt5c/vorschau")]
    public async Task<IActionResult> Schritt5cVorschau([FromQuery] string? nur) => await Schritt5c(true, nur);
    [HttpPost("schritt5c/anlegen")]
    public async Task<IActionResult> Schritt5cAnlegen([FromQuery] string? nur) => await Schritt5c(false, nur);

    private static readonly DateOnly TmVon = new(2024, 11, 1);
    private static readonly DateOnly TmBis = new(2026, 2, 1);

    // ── 5a ──────────────────────────────────────────────────────────────
    private async Task<IActionResult> Schritt5a(bool vorschau)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var hs = await _db.Hauptsitze.AsNoTracking().FirstOrDefaultAsync(h => h.Uid == "CHE-999.999.996");
        if (hs == null) return BadRequest(new { error = "SCHRITT1_FEHLT", message = "Zuerst Schritt 1." });
        var filialen = await _db.CompanyProfiles.Where(c => c.HauptsitzId == hs.Id).OrderBy(c => c.RestaurantCode).ToListAsync();
        var perioden = await _db.PayrollPerioden.Where(p => filialen.Select(f => f.Id).Contains(p.CompanyProfileId)).ToListAsync();
        var aktionen = new List<Aktion>(); var hinweise = new List<string>();

        foreach (var f in filialen)
        {
            var felder = new Dictionary<string, string?>
            {
                ["Ferien % Standard"] = "8.33 (4 Wochen / 20 Tage)", ["Ferien % erhöht"] = "13.04 · erhöht ab Alter 60 (6 Wochen / 30 Tage)",
                ["Feiertag %"] = "4.00", ["13. ML %"] = "8.33 · Auszahlung Dezember (Stundenlohn monatlich)",
                ["Ferienwochen"] = "4 Standard / 6 ab Alter 60", ["Wochenstunden"] = "42",
                ["Akonto-Lohn"] = "nein – nur Definitiv (Swissdec kennt keinen Akonto-Lauf)",
                ["Ferienentschädigung"] = "monatlich auszahlen (kein Ferien-Pott)",
                ["L-GAV-Vollzugsbeitrag"] = "deaktiviert (Muster AG ist kein Gastro-Betrieb; Swissdec-Soll kennt keinen L-GAV-Abzug)",
                ["Teilmonat"] = "30-Tage-Methode (Swissdec: Monatslohn voll + Lohnkorrektur 1001)",
                ["Ferien-Tage am Austritt"] = "nicht in CHF auszahlen (Saldo bleibt in Tagen)",
                ["Feiertag-Tage am Austritt"] = "nicht in CHF auszahlen (Saldo bleibt in Tagen)",
                ["Stunden-Saldo im Lohn"] = "nicht verrechnen (Soll/Ist nur Anzeige; Quality Tool kennt keine Saldo-Auszahlung)",
                ["Uniform-Depot"] = "deaktiviert (kein CHF-50-Abzug beim ersten Lohn)",
                ["Lohnlauf-Bestätigung"] = "nur HR (kein GF-Schritt, Muster AG ohne Restaurant-GF)",
                ["bisher"] = $"Ferien {f.DefaultVacationPercent5Weeks}/{f.DefaultVacationPercent6Weeks} ab {f.VacationSixWeeksFromAge} · Feiertag {f.DefaultHolidayPercent} · 13. {f.DefaultThirteenthSalaryPercent} ({f.ThirteenthMonthPayoutMonths ?? "–"})",
            };
            aktionen.Add(new Aktion("aktualisieren", "Filiale", $"{f.RestaurantCode} · {f.BranchName}", felder));
            if (!vorschau)
            {
                f.DefaultVacationPercent5Weeks = 8.33m; f.DefaultVacationPercent6Weeks = 13.04m; f.VacationSixWeeksFromAge = 60;
                f.DefaultHolidayPercent = 4.00m; f.DefaultThirteenthSalaryPercent = 8.33m;
                f.ThirteenthMonthPayoutMonths = "12"; f.ThirteenthMonthPayoutsPerYear = 1;
                f.DefaultVacationWeeks = 4; f.NormalWeeklyHours ??= 42m;
                f.AkontoAktiv = false;
                f.LohnlaufNurHr = true;
                f.FerienAuszahlungMonatlich = true;
                f.LgavAktiv = false;
                f.TeilmonatMethode = "TAGE30";
                // Schlussabrechnung / Stunden im Lohn: aus (Walter 10.09.2026)
                f.FerientageAmAustrittAuszahlen = false;
                f.FeiertagstageAmAustrittAuszahlen = false;
                f.StundenSaldoImLohnVerrechnen = false;
                f.UniformDepotAktiv = false;   // Walter 10.09.2026
                // Bereits angelegte Depot-Zeilen der Muster-AG-Personen entfernen (sonst
                // Rückgabe-Hinweis beim letzten Lohn), es gab dafür nie einen Abzug.
                var depotEmpIds = await _db.Employments.Where(e => e.CompanyProfileId == f.Id).Select(e => e.EmployeeId).Distinct().ToListAsync();
                var depots = await _db.EmployeeUniformDepots.Where(d => depotEmpIds.Contains(d.EmployeeId)).ToListAsync();
                if (depots.Count > 0) _db.EmployeeUniformDepots.RemoveRange(depots);
            }
            int neu = 0;
            for (var m = TmVon; m <= TmBis; m = m.AddMonths(1))
            {
                if (perioden.Any(p => p.CompanyProfileId == f.Id && p.Year == m.Year && p.Month == m.Month)) continue;
                neu++;
                if (vorschau) continue;
                _db.PayrollPerioden.Add(new PayrollPeriode
                {
                    CompanyProfileId = f.Id, Year = m.Year, Month = m.Month,
                    PeriodFrom = m, PeriodTo = new DateOnly(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month)),
                    Label = $"{CultureInfo.GetCultureInfo("de-CH").DateTimeFormat.GetMonthName(m.Month)} {m.Year}", Status = "offen",
                });
            }
            aktionen.Add(new Aktion(neu > 0 ? "anlegen" : "aktualisieren", "Lohnperioden", $"{f.RestaurantCode}: {neu} neue Perioden {TmVon:MM.yyyy}–{TmBis:MM.yyyy}", new()));
        }
        if (!vorschau) await _db.SaveChangesAsync();
        hinweise.Add("Muster AG: 20 Ferientage (8.33 %) bis Alter 59, 30 Tage (13.04 %) ab 60 — gilt für den Stundenlohn-Zuschlag (CSV TF02 Paganini 1160 = 13.04 %). Die 25 Tage ab 50 (Monatslöhner-Tage) haben bei uns kein drittes %-Band; FLEX springt 8.33 → 13.04.");
        hinweise.Add("Muster AG: Lohnlauf-Bestätigung nur HR (kein GF-Schritt) — Filial-Einstellungen «Wer bestätigt».");
        hinweise.Add("13. Monatslohn: Monatslöhner im Dezember (Testdaten 1200 nur im Dezember; 1200 ausserhalb des Rasters → Auslöser 180.3), Stundenlöhner monatlich (1201) — FLEX rechnet OneCrew ohnehin monatlich.");
        return Ok(new SchrittErgebnis("5a · Filial-Einstellungen + Perioden", vorschau, aktionen, hinweise));
    }

    // ── 5b ──────────────────────────────────────────────────────────────
    private static readonly HashSet<string> RechnetOneCrew = new() { "1160", "1161", "1162", "1163", "1200", "1201", "1202" };

    private async Task<IActionResult> Schritt5b(bool vorschau, string? monat, string? nur)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var pfad = CsvPfad("wagetypes_export.csv");
        if (!System.IO.File.Exists(pfad)) return NotFound(new { error = "CSV_FEHLT", message = $"Datei fehlt: {pfad}" });

        // Header: testcaseId,sscTag,label,2024-11-01,…,total
        var zeilen = System.IO.File.ReadAllLines(pfad);
        var kopf = zeilen[0].Split(',');
        var monate = new List<(int Idx, DateOnly Monat)>();
        for (int i = 3; i < kopf.Length; i++)
            if (DateOnly.TryParseExact(kopf[i].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) monate.Add((i, d));
        DateOnly? nurMonat = null;
        if (!string.IsNullOrWhiteSpace(monat) && DateOnly.TryParseExact(monat.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var mm)) nurMonat = mm;
        var nurSet = NurSet(nur);

        // Zeilen → (Fall, Code, Label, Monat, Betrag)
        var werte = new List<(string Fall, string Code, string Label, DateOnly Monat, decimal Betrag)>();
        foreach (var ln in zeilen.Skip(1))
        {
            var t = ln.Split(',');
            if (t.Length < 4) continue;
            var fall = t[0].Trim().ToUpperInvariant();
            if (nurSet != null && !nurSet.Contains(fall)) continue;
            foreach (var (idx, m) in monate)
            {
                if (nurMonat != null && m != nurMonat) continue;
                if (idx >= t.Length) continue;
                if (decimal.TryParse(t[idx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b) && b != 0)
                    werte.Add((fall, t[1].Trim(), t[2].Trim(), m, b));
            }
        }

        // Stunden/Lektionen pro Monat: Startwert (testcases) + Mutationen (differences)
        var stundenJeMonat = LeseMonatsreihe("PersonNumberOfHours");
        var lektionenJeMonat = LeseMonatsreihe("PersonNumberOfLessons");
        // Arbeitstage effektiv / CH pro Monat (QST bei Wohnsitz Ausland, Walter 11.09.2026)
        var tageEffJeMonat = LeseMonatsreihe("PersonEffectiveWorkingDays");
        var tageChJeMonat  = LeseMonatsreihe("PersonWorkingDaysCH");

        // Swissdec-Lohnart → OneCrew-Lohnposition (Feld SwissdecLohnart, Schritt 4b).
        // Tragen mehrere Positionen dieselbe Swissdec-Lohnart (195.2 + 195.4 → 1161),
        // nimmt der Import die mit der kleinsten Sortierung — das betrifft nur Lohnarten,
        // die OneCrew ohnehin selbst rechnet und die hier nicht gebucht werden.
        var nachSwissdec = (await _db.Lohnpositionen.AsNoTracking()
                .Where(l => l.IsActive && l.SwissdecLohnart != null && l.SwissdecLohnart != "")
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToListAsync())
            .GroupBy(l => l.SwissdecLohnart!).ToDictionary(g => g.Key, g => g.First());
        var lp1803 = await _db.Lohnpositionen.AsNoTracking()
            .FirstOrDefaultAsync(l => l.IsActive && l.Code == PayrollCalculations.Code13mlAuszahlen);
        var filialen = await _db.CompanyProfiles.AsNoTracking().ToDictionaryAsync(c => c.Id);
        var aktionen = new List<Aktion>(); var hinweise = new List<string>();
        var fehlendePos = new HashSet<string>();

        foreach (var grp in werte.GroupBy(w => (w.Fall, w.Monat)).OrderBy(g => g.Key.Monat).ThenBy(g => g.Key.Fall))
        {
            var (fall, m) = grp.Key;
            var persNr = fall.Replace("TF", "").TrimStart('0');
            var emp = await _db.Employees.Include(e => e.Employments).FirstOrDefaultAsync(e => e.EmployeeNumber == persNr);
            var felder = new Dictionary<string, string?>();
            if (emp == null) { aktionen.Add(new Aktion("aktualisieren", "Monat", $"{m:MM.yyyy} · {fall}", new() { ["⚠"] = "Person nicht gefunden — zuerst 4a." })); continue; }
            var periodeStr = $"{m.Year:D4}-{m.Month:D2}";
            var monatsEnde = new DateOnly(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));
            var vertrag = emp.Employments.Where(v => DateOnly.FromDateTime(v.ContractStartDate) <= monatsEnde && (v.ContractEndDate == null || DateOnly.FromDateTime(v.ContractEndDate.Value) >= m))
                                         .OrderByDescending(v => v.ContractStartDate).FirstOrDefault();
            var probleme = new List<string>();
            if (vertrag == null) probleme.Add("kein Vertrag in diesem Monat (Nachzahlung nach Austritt? → Korrekturlohn)");

            var zulagen = new List<string>(); var soll = new List<string>();

            // Arbeitstage effektiv / CH → employee_qst_arbeitstage (nur Wohnsitz Ausland).
            // Wohnsitz CH in diesem Monat (TF25 Lehmann ab 1.5. Malters, Umzug = QST)
            // → CSV-Werte nicht übernehmen.
            var tEff = WertImMonat(tageEffJeMonat, fall, m); var tCh = WertImMonat(tageChJeMonat, fall, m);
            if (tEff is > 0 && tCh != null)
            {
                var wohnCh = await QstKantonswechselService.WohnsitzSchweizAmAsync(_db, emp.Id, m);
                if (wohnCh)
                {
                    felder["Arbeitstage"] = $"{tEff:0.#} effektiv · {tCh:0.#} CH (CSV; Wohnsitz CH → nicht übernommen)";
                }
                else
                {
                    felder["Arbeitstage"] = $"{tEff:0.#} effektiv · {tCh:0.#} CH" + (tCh < tEff ? " → QST-Anteil bei Wohnsitz Ausland" : "");
                    if (!vorschau)
                    {
                        var at = await _db.EmployeeQstArbeitstage.FirstOrDefaultAsync(a => a.EmployeeId == emp.Id && a.Year == m.Year && a.Month == m.Month)
                              ?? new EmployeeQstArbeitstage { EmployeeId = emp.Id, Year = m.Year, Month = m.Month, CreatedAt = DateTime.Now };
                        at.TageEffektiv = tEff.Value; at.TageCh = tCh.Value; at.Bemerkung = "Swissdec-Testdaten"; at.UpdatedAt = DateTime.Now;
                        if (at.Id == 0) _db.EmployeeQstArbeitstage.Add(at);
                        await _db.SaveChangesAsync();
                    }
                }
            }
            foreach (var w in grp.OrderBy(x => x.Code))
            {
                // 1000 Monatslohn → Vertrag
                if (w.Code == "1000")
                {
                    felder["Monatslohn"] = $"{w.Betrag:0.00}" + (vertrag != null && vertrag.MonthlySalary != w.Betrag ? $" (Vertrag bisher {vertrag.MonthlySalary?.ToString("0.00") ?? "–"} → {(vertrag.ContractStartDate.Date == m.ToDateTime(TimeOnly.MinValue).Date || vertrag.MonthlySalary == null ? "setzen" : "neuer Abschnitt ab " + m.ToString("dd.MM.yyyy"))})" : "");
                    if (!vorschau && vertrag != null && vertrag.MonthlySalary != w.Betrag)
                    {
                        if (vertrag.ContractStartDate.Date == m.ToDateTime(TimeOnly.MinValue).Date || vertrag.MonthlySalary == null)
                        {
                            vertrag.MonthlySalary = w.Betrag;
                            vertrag.MonthlySalaryFte = vertrag.EmploymentPercentage is > 0 ? Math.Round(w.Betrag * 100m / vertrag.EmploymentPercentage.Value, 2) : w.Betrag;
                            if (vertrag.EmploymentModel == "FLEX") { vertrag.EmploymentModel = "FIX"; vertrag.SalaryType = "monthly"; vertrag.EmploymentPercentage ??= 100m; }
                        }
                        else
                        {
                            var neu = KopiereVertrag(vertrag, m);
                            neu.MonthlySalary = w.Betrag;
                            neu.MonthlySalaryFte = neu.EmploymentPercentage is > 0 ? Math.Round(w.Betrag * 100m / neu.EmploymentPercentage.Value, 2) : w.Betrag;
                            vertrag.ContractEndDate = m.AddDays(-1).ToDateTime(TimeOnly.MinValue);
                            _db.Employments.Add(neu); emp.Employments.Add(neu); vertrag = neu;
                        }
                        await _db.SaveChangesAsync();
                    }
                    continue;
                }
                // 1005 Stundenlohn → Stunden des Monats als Stempelzeiten
                if (w.Code == "1005")
                {
                    var std = WertImMonat(stundenJeMonat, fall, m);
                    var satz = vertrag?.HourlyRate;
                    var stdAusBetrag = satz is > 0 ? Math.Round(w.Betrag / satz.Value, 2) : (decimal?)null;
                    var stunden = std ?? stdAusBetrag;
                    felder["Stunden"] = $"{stunden?.ToString("0.##") ?? "?"} h (Lohnart 1005 = {w.Betrag:0.00}{(satz != null ? $" bei CHF {satz:0.00}/h" : "")})" + (std != null && stdAusBetrag != null && Math.Abs(std.Value - stdAusBetrag.Value) > 0.05m ? $" ⚠ Stunden {std} ≠ Betrag/Satz {stdAusBetrag}" : "");
                    if (!vorschau && stunden is > 0 && vertrag != null)
                        await SchreibeStempelzeitenAsync(emp.Id, vertrag.CompanyProfileId, m, stunden.Value);
                    continue;
                }
                if (w.Code == "1006")
                {
                    var lekt = WertImMonat(lektionenJeMonat, fall, m);
                    felder["Lektionen"] = $"{lekt?.ToString("0.##") ?? "?"} Lektionen (Lohnart 1006 = {w.Betrag:0.00}) → als Zulage auf Lohnposition 1006";
                    // fällt durch → Zulage
                }
                if (w.Code == "1200")
                {
                    soll.Add($"{w.Code} {w.Label} {w.Betrag:0.00}");
                    bool rasterMonat = vertrag?.CompanyProfileId is int cpid
                        && filialen.TryGetValue(cpid, out var filial)
                        && PayrollCalculations.IsThirteenthPayoutMonth(filial, m.Month);
                    if (w.Betrag > 0 && !rasterMonat)
                    {
                        zulagen.Add("1200→180.3 13. Monatslohn auszahlen (Auslöser, Betrag ignoriert)");
                        if (!vorschau)
                        {
                            if (lp1803 == null)
                                probleme.Add("180.3 fehlt (Schema-Stand 14 — Server neu starten)");
                            else
                            {
                                var vorhanden = await _db.LohnZulagen.FirstOrDefaultAsync(z =>
                                    z.EmployeeId == emp.Id && z.Periode == periodeStr
                                    && z.LohnpositionId == lp1803.Id && z.Bemerkung == "Swissdec-Testdaten");
                                if (vorhanden == null)
                                    _db.LohnZulagen.Add(new LohnZulage
                                    {
                                        EmployeeId = emp.Id, Periode = periodeStr,
                                        LohnpositionId = lp1803.Id, Betrag = 0m,
                                        Bemerkung = "Swissdec-Testdaten",
                                        CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now
                                    });
                                else { vorhanden.Betrag = 0m; vorhanden.UpdatedAt = DateTime.Now; }
                            }
                        }
                    }
                    else if (!vorschau && lp1803 != null)
                    {
                        var alt = await _db.LohnZulagen.FirstOrDefaultAsync(z =>
                            z.EmployeeId == emp.Id && z.Periode == periodeStr
                            && z.LohnpositionId == lp1803.Id && z.Bemerkung == "Swissdec-Testdaten");
                        if (alt != null) _db.LohnZulagen.Remove(alt);
                    }
                    continue;
                }
                if (RechnetOneCrew.Contains(w.Code)) { soll.Add($"{w.Code} {w.Label} {w.Betrag:0.00}"); continue; }
                // 5050 BVG-Beitrag → Fixbetrag
                if (w.Code == "5050")
                {
                    felder["BVG-Beitrag fix"] = $"{w.Betrag:0.00} AN (AG gleich hoch, Annahme)";
                    if (!vorschau) await SetzeBvgFixAsync(emp.Id, m, w.Betrag);
                    continue;
                }
                // übrige → LohnZulage
                if (!nachSwissdec.TryGetValue(w.Code, out var lp))
                {
                    fehlendePos.Add($"{w.Code} {w.Label}"); probleme.Add($"{w.Code} {w.Label}: keine Lohnposition (4b)");
                    continue;
                }
                // 1001 Lohnkorrektur (Walter 09.09.2026 / 17.09.2026): Swissdec zahlt im
                // Ein-/Austrittsmonat den vollen Monatslohn und kürzt mit 1001. OneCrew
                // rechnet den Teilmonat selbst (TAGE30, Eintrittstag zählt). 1001 im
                // Teilmonat daher NIE importieren — sonst doppelt, und eine abweichende
                // Swissdec-Tageszahl (TF25/26 Feb: 20 statt 21) verbiegt den Lohn (A7).
                // 1001 in einem vollen Monat (echte Korrektur/Nachzahlung) weiter buchen.
                if (w.Code == "1001" && vertrag != null)
                {
                    var lohn1000 = grp.Where(x => x.Code == "1000").Select(x => (decimal?)x.Betrag).FirstOrDefault();
                    var monatslohn = lohn1000 ?? vertrag.MonthlySalary ?? 0m;
                    var von = DateOnly.FromDateTime(vertrag.ContractStartDate) > m ? DateOnly.FromDateTime(vertrag.ContractStartDate) : m;
                    var bisD = vertrag.ContractEndDate.HasValue && DateOnly.FromDateTime(vertrag.ContractEndDate.Value) < monatsEnde ? DateOnly.FromDateTime(vertrag.ContractEndDate.Value) : monatsEnde;
                    bool teilmonat = von > m || bisD < monatsEnde;
                    if (teilmonat && monatslohn > 0)
                    {
                        var anteil = PayrollCalculationEngine.TeilmonatAnteil("TAGE30", monatslohn, von, bisD, bisD.DayNumber - von.DayNumber + 1, monatsEnde.Day);
                        var csvNetto = monatslohn + w.Betrag;
                        var abweichend = Math.Abs(csvNetto - anteil) > 0.05m;
                        felder["Lohnkorrektur 1001"] = abweichend
                            ? $"{w.Betrag:0.00} Teilmonat ({von:dd.MM.}–{bisD:dd.MM.}) → nicht importiert, OneCrew {anteil:0.00} (Eintrittstag zählt), CSV {csvNetto:0.00} (A7)"
                            : $"{w.Betrag:0.00} = Teilmonat ({von:dd.MM.}–{bisD:dd.MM.}, 30-Tage-Anteil {anteil:0.00}) → nicht importiert, OneCrew rechnet anteilig";
                        if (!vorschau)
                        {
                            var alt = await _db.LohnZulagen.FirstOrDefaultAsync(z => z.EmployeeId == emp.Id && z.Periode == periodeStr && z.LohnpositionId == lp.Id && z.Bemerkung == "Swissdec-Testdaten");
                            if (alt != null) _db.LohnZulagen.Remove(alt);
                        }
                        continue;
                    }
                }
                // Vorzeichen: Betrag IMMER wie geliefert — auch bei Abzugs-Positionen. Ein
                // negativer Abzug ist eine Gutschrift (TF11 Bosshard Juni: 5210 Ausgleich
                // geldwerte Vorteile −19'750 = Storno der März-Beteiligung); die Engine dreht
                // das Vorzeichen selbst (betrag = −b). Math.Abs machte daraus einen Abzug.
                var betragBuchung = w.Betrag;
                zulagen.Add($"{w.Code}→{lp.Code} {lp.Bezeichnung} {betragBuchung:0.00}");
                if (!vorschau)
                {
                    var vorhanden = await _db.LohnZulagen.FirstOrDefaultAsync(z => z.EmployeeId == emp.Id && z.Periode == periodeStr && z.LohnpositionId == lp.Id && z.Bemerkung == "Swissdec-Testdaten");
                    if (vorhanden == null) _db.LohnZulagen.Add(new LohnZulage { EmployeeId = emp.Id, Periode = periodeStr, LohnpositionId = lp.Id, Betrag = betragBuchung, Bemerkung = "Swissdec-Testdaten", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
                    else { vorhanden.Betrag = betragBuchung; vorhanden.UpdatedAt = DateTime.Now; }
                }
            }
            if (!vorschau) await _db.SaveChangesAsync();
            if (zulagen.Count > 0) felder["Zulagen/Abzüge"] = string.Join(" · ", zulagen);
            if (soll.Count > 0) felder["Soll (rechnet OneCrew)"] = string.Join(" · ", soll);
            if (probleme.Count > 0) felder["⚠ Hinweise"] = string.Join(" | ", probleme);
            aktionen.Add(new Aktion("aktualisieren", "Monat", $"{m:MM.yyyy} · {fall} {emp.FirstName} {emp.LastName}", felder));
        }

        hinweise.Insert(0, $"{werte.Count} Lohnzeilen" + (nurMonat != null ? $" im Monat {nurMonat:MM.yyyy}" : $" über {monate.Count} Monate") + $" · {aktionen.Count} Person/Monat-Gruppen.");
        if (fehlendePos.Count > 0) hinweise.Add("Ohne Lohnposition (Schritt 4b prüfen): " + string.Join(", ", fehlendePos.OrderBy(x => x)));
        hinweise.Add("Stempelzeiten: Monatsstunden gleichmässig auf Mo–Fr verteilt, 08:00 Uhr beginnend, Kommentar «Swissdec-Testdaten» — keine Nachtstunden. Bestehende Test-Stempelzeiten des Monats werden ersetzt.");
        hinweise.Add("Ferien-/Feiertagsvergütung (1160–1163) und 13. Monatslohn (1201/1202) werden nicht importiert — OneCrew rechnet sie. 1200 ausserhalb des Filial-Rasters legt den Auslöser 180.3 an (Betrag 0); die Soll-Beträge stehen pro Monat zum Vergleich in der Vorschau.");
        hinweise.Add("Nach dem Anlegen: Lohnlauf → Filiale → Monat → «Lohn bestätigen» pro MA, dann Vergleich mit den Swissdec-Sollwerten (Quality Tool).");
        return Ok(new SchrittErgebnis("5b · Monatswerte", vorschau, aktionen, hinweise));
    }

    // ── 5c ──────────────────────────────────────────────────────────────
    // Bestätigte Lohnzettel + Saldi der Muster AG weg, Perioden wieder offen.
    // Zulagen/5b, Verträge, Stempelzeiten bleiben. Dann Jan → aktuell neu
    // bestätigen (1/12 braucht die Vormonate in Reihenfolge).
    private async Task<IActionResult> Schritt5c(bool vorschau, string? nur)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var hs = await _db.Hauptsitze.AsNoTracking().FirstOrDefaultAsync(h => h.Uid == "CHE-999.999.996");
        if (hs == null) return NotFound(new { error = "HAUPTSITZ_FEHLT", message = "Muster AG fehlt — zuerst Schritt 1." });
        var filialen = await _db.CompanyProfiles.Where(c => c.HauptsitzId == hs.Id)
            .OrderBy(c => c.RestaurantCode).ToListAsync();
        var nurSet = NurSet(nur);
        if (nurSet != null)
            filialen = filialen.Where(c => nurSet.Contains((c.RestaurantCode ?? "").ToUpperInvariant())).ToList();
        var filialIds = filialen.Select(c => c.Id).ToList();
        var perioden = await _db.PayrollPerioden.Where(p => filialIds.Contains(p.CompanyProfileId)).ToListAsync();
        var periodeIds = perioden.Select(p => p.Id).ToList();
        var snaps = periodeIds.Count == 0
            ? new List<PayrollSnapshot>()
            : await _db.PayrollSnapshots.Where(s => periodeIds.Contains(s.PayrollPeriodeId)).ToListAsync();
        var saldi = await _db.PayrollSaldos.Where(s => filialIds.Contains(s.CompanyProfileId)).ToListAsync();

        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();
        foreach (var f in filialen)
        {
            var perIds = perioden.Where(p => p.CompanyProfileId == f.Id).Select(p => p.Id).ToHashSet();
            var nPer = perIds.Count;
            var nSnap = snaps.Count(s => perIds.Contains(s.PayrollPeriodeId));
            var nSaldo = saldi.Count(s => s.CompanyProfileId == f.Id);
            aktionen.Add(new Aktion("aktualisieren", "Lohnlauf",
                $"{f.RestaurantCode}: {nPer} Perioden → offen, {nSnap} Lohnzettel + {nSaldo} Saldi löschen", new()));
        }
        if (!vorschau)
        {
            if (snaps.Count > 0) _db.PayrollSnapshots.RemoveRange(snaps);
            if (saldi.Count > 0) _db.PayrollSaldos.RemoveRange(saldi);
            foreach (var p in perioden)
            {
                p.Status = "offen";
                p.AbgeschlossenAm = null;
                p.AbgeschlossenVon = null;
                p.ProvisorischAbgeschlossenAm = null;
                p.ProvisorischAbgeschlossenVon = null;
            }
            await _db.SaveChangesAsync();
        }
        hinweise.Add("Zulagen, Stempelzeiten und Verträge bleiben. 180.3 (Bosshard Mai) bleibt.");
        hinweise.Add("Danach im Lohnlauf den ÄLTESTEN offenen Monat zuerst bestätigen (Nov 2024 bzw. Eintritt), dann den nächsten — sonst fehlt der 13.-Pott.");
        hinweise.Add("Stunden-Saldo rot bei FIX (Ist = 0) ist nur Anzeige; Muster AG verrechnet ihn nicht in CHF.");
        if (nurSet != null) hinweise.Insert(0, "Nur Filiale " + string.Join(", ", nurSet) + ".");
        return Ok(new SchrittErgebnis("5c · Lohnläufe verwerfen", vorschau, aktionen, hinweise));
    }

    // ── Hilfen 5 ────────────────────────────────────────────────────────
    private static Employment KopiereVertrag(Employment a, DateOnly ab) => new()
    {
        EmployeeId = a.EmployeeId, CompanyProfileId = a.CompanyProfileId, EmploymentModel = a.EmploymentModel, SalaryType = a.SalaryType,
        ContractStartDate = ab.ToDateTime(TimeOnly.MinValue), ContractEndDate = a.ContractEndDate, JobTitle = a.JobTitle, JobGroupId = a.JobGroupId,
        ContractType = a.ContractType, EducationLevelCode = a.EducationLevelCode, EmploymentPercentage = a.EmploymentPercentage,
        WeeklyHours = a.WeeklyHours, GuaranteedHoursPerWeek = a.GuaranteedHoursPerWeek, LessonRate = a.LessonRate, WeeklyLessons = a.WeeklyLessons,
        TeilzeitUnter8hWoche = a.TeilzeitUnter8hWoche, MonthlySalaryFte = a.MonthlySalaryFte, MonthlySalary = a.MonthlySalary, HourlyRate = a.HourlyRate,
        EasyAtWorkManualOverride = true, VacationPaymentMode = a.VacationPaymentMode, ThirteenthSalary = a.ThirteenthSalary, IsActive = true,
    };

    /// <summary>Startwert aus testcases_export + Änderungen aus testcase_differences → Wert je Fall ab Monat.</summary>
    private Dictionary<string, SortedDictionary<DateOnly, decimal>> LeseMonatsreihe(string tag)
    {
        var res = new Dictionary<string, SortedDictionary<DateOnly, decimal>>(StringComparer.OrdinalIgnoreCase);
        var tc = CsvPfad("testcases_export.csv");
        if (System.IO.File.Exists(tc))
            foreach (var f in LeseTestfaelle(tc))
                if (f.W.TryGetValue(tag, out var v) && decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                    res[f.Id.ToUpperInvariant()] = new SortedDictionary<DateOnly, decimal> { [f.Entry] = d };
        var df = CsvPfad("testcase_differences_export.csv");
        if (System.IO.File.Exists(df))
            foreach (var mu in LeseMutationen(df).Where(x => x.Tag.Equals(tag, StringComparison.OrdinalIgnoreCase)))
            {
                var id = mu.Fall.Split(' ')[0].ToUpperInvariant();
                if (!res.TryGetValue(id, out var reihe)) res[id] = reihe = new SortedDictionary<DateOnly, decimal>();
                reihe[mu.Monat] = decimal.TryParse(mu.Neu, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
            }
        return res;
    }

    private static decimal? WertImMonat(Dictionary<string, SortedDictionary<DateOnly, decimal>> reihen, string fall, DateOnly m)
    {
        if (!reihen.TryGetValue(fall, out var reihe)) return null;
        decimal? w = null;
        foreach (var kv in reihe) { if (kv.Key <= m) w = kv.Value; else break; }
        return w;
    }

    /// <summary>Monatsstunden gleichmässig auf Mo–Fr verteilen (max. 10 h/Tag, Rest auf weitere Tage), ab 08:00, ohne Nacht.</summary>
    private async Task SchreibeStempelzeitenAsync(int employeeId, int? companyProfileId, DateOnly monat, decimal stunden)
    {
        var von = monat; var bis = new DateOnly(monat.Year, monat.Month, DateTime.DaysInMonth(monat.Year, monat.Month));
        var alte = await _db.EmployeeTimeEntries.Where(t => t.EmployeeId == employeeId && t.EntryDate >= von && t.EntryDate <= bis && t.Comment == "Swissdec-Testdaten").ToListAsync();
        _db.EmployeeTimeEntries.RemoveRange(alte);
        var tage = new List<DateOnly>();
        for (var d = von; d <= bis; d = d.AddDays(1)) if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday) tage.Add(d);
        if (tage.Count == 0) return;
        var proTag = Math.Round(stunden / tage.Count, 2);
        if (proTag > 10m) { proTag = 10m; }
        decimal rest = stunden;
        foreach (var d in tage)
        {
            if (rest <= 0) break;
            var h = Math.Min(proTag, rest);
            if (d == tage.Last() || rest - h < 0.01m) h = Math.Min(rest, 10m);
            var timeIn = DateTime.SpecifyKind(d.ToDateTime(new TimeOnly(8, 0)), DateTimeKind.Unspecified);
            _db.EmployeeTimeEntries.Add(new EmployeeTimeEntry
            {
                EmployeeId = employeeId, EntryDate = d, TimeIn = timeIn, TimeOut = timeIn.AddHours((double)h),
                DurationHours = h, NightHours = 0m, TotalHours = h, Comment = "Swissdec-Testdaten", SourceCompanyProfileId = companyProfileId,
            });
            rest = Math.Round(rest - h, 2);
        }
        await _db.SaveChangesAsync();
    }

    /// <summary>BVG-Fixbetrag (AN = AG, Annahme) ab Monat — neuer Eintrag nur bei Änderung. Dubletten ab demselben Tag werden zusammengelegt (5050 gewinnt).</summary>
    private async Task SetzeBvgFixAsync(int employeeId, DateOnly monat, decimal betrag)
    {
        var eintraege = await _db.EmployeeVersicherungCodes.Where(v => v.EmployeeId == employeeId && v.Art == "BVG").OrderBy(v => v.ValidFrom).ToListAsync();
        var amMonat = eintraege.Where(v => v.ValidFrom == monat).ToList();
        if (amMonat.Count > 0)
        {
            var keep = PayrollCalculations.WaehleBvgFix(amMonat) ?? amMonat[0];
            keep.BeitragFixAn = betrag;
            keep.BeitragFixAg = betrag;
            if (keep.Bemerkung == null || keep.Bemerkung.IndexOf("5050", StringComparison.OrdinalIgnoreCase) < 0)
                keep.Bemerkung = "Swissdec-Testdaten (Lohnart 5050)";
            foreach (var d in amMonat.Where(x => x.Id != keep.Id))
                _db.EmployeeVersicherungCodes.Remove(d);
            await _db.SaveChangesAsync();
            return;
        }
        var aktiv = eintraege.LastOrDefault(v => v.GiltAm(monat));
        if (aktiv != null && aktiv.BeitragFixAn == betrag) return;
        var neu = new EmployeeVersicherungCode
        {
            EmployeeId = employeeId, Art = "BVG", Code = aktiv?.Code, ValidFrom = monat, ValidTo = aktiv?.ValidTo,
            BeitragFixAn = betrag, BeitragFixAg = betrag, BvgEintrittsgrund = aktiv?.BvgEintrittsgrund, BvgVollArbeitsfaehig = aktiv?.BvgVollArbeitsfaehig,
            BvgBasisManuell = aktiv?.BvgBasisManuell, Bemerkung = "Swissdec-Testdaten (Lohnart 5050)", CreatedAt = DateTime.Now,
        };
        if (aktiv != null) aktiv.ValidTo = monat.AddDays(-1);
        _db.EmployeeVersicherungCodes.Add(neu);
        await _db.SaveChangesAsync();
    }
}
