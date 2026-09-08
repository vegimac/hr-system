using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Models;
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
///       1160/1161/1162/1163/1200/1201/1202 → rechnet OneCrew selbst (Ferien-%,
///            Feiertag-%, 13. ML) — nur Soll-Vergleich
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
                ["Ferien % Standard"] = "8.33 (4 Wochen)", ["Ferien % erhöht"] = "8.33 · erhöht ab Alter 99 (nie)",
                ["Feiertag %"] = "4.00", ["13. ML %"] = "8.33 · Auszahlung Dezember (Stundenlohn monatlich)",
                ["Ferienwochen"] = "4", ["Wochenstunden"] = "42",
                ["Akonto-Lohn"] = "nein – nur Definitiv (Swissdec kennt keinen Akonto-Lauf)",
                ["Ferienentschädigung"] = "monatlich auszahlen (kein Ferien-Pott)",
                ["L-GAV-Vollzugsbeitrag"] = "deaktiviert (Muster AG ist kein Gastro-Betrieb; Swissdec-Soll kennt keinen L-GAV-Abzug)",
                ["bisher"] = $"Ferien {f.DefaultVacationPercent5Weeks}/{f.DefaultVacationPercent6Weeks} ab {f.VacationSixWeeksFromAge} · Feiertag {f.DefaultHolidayPercent} · 13. {f.DefaultThirteenthSalaryPercent} ({f.ThirteenthMonthPayoutMonths ?? "–"})",
            };
            aktionen.Add(new Aktion("aktualisieren", "Filiale", $"{f.RestaurantCode} · {f.BranchName}", felder));
            if (!vorschau)
            {
                f.DefaultVacationPercent5Weeks = 8.33m; f.DefaultVacationPercent6Weeks = 8.33m; f.VacationSixWeeksFromAge = 99;
                f.DefaultHolidayPercent = 4.00m; f.DefaultThirteenthSalaryPercent = 8.33m;
                f.ThirteenthMonthPayoutMonths = "12"; f.ThirteenthMonthPayoutsPerYear = 1;
                f.DefaultVacationWeeks = 4; f.NormalWeeklyHours ??= 42m;
                f.AkontoAktiv = false;
                f.FerienAuszahlungMonatlich = true;
                f.LgavAktiv = false;
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
        hinweise.Add("Muster AG: 20 Ferientage (8.33 %), keine altersabhängige Erhöhung bei den Stundenlöhnern (Testdaten: 8.33 % auch bei 64-Jährigen); 25/30 Tage ab 50/60 betreffen nur die Ferientage der Monatslöhner (Absenzen), nicht den Lohn.");
        hinweise.Add("13. Monatslohn: Monatslöhner im Dezember (Testdaten 1200 nur im Dezember), Stundenlöhner monatlich (1201) — FLEX rechnet OneCrew ohnehin monatlich.");
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
        var nurSet = string.IsNullOrWhiteSpace(nur) ? null : nur.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToUpperInvariant()).ToHashSet();

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

        // Swissdec-Lohnart → OneCrew-Lohnposition (Feld SwissdecLohnart, Schritt 4b).
        // Tragen mehrere Positionen dieselbe Swissdec-Lohnart (195.2 + 195.4 → 1161),
        // nimmt der Import die mit der kleinsten Sortierung — das betrifft nur Lohnarten,
        // die OneCrew ohnehin selbst rechnet und die hier nicht gebucht werden.
        var nachSwissdec = (await _db.Lohnpositionen.AsNoTracking()
                .Where(l => l.IsActive && l.SwissdecLohnart != null && l.SwissdecLohnart != "")
                .OrderBy(l => l.SortOrder).ThenBy(l => l.Id).ToListAsync())
            .GroupBy(l => l.SwissdecLohnart!).ToDictionary(g => g.Key, g => g.First());
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
                zulagen.Add($"{w.Code}→{lp.Code} {lp.Bezeichnung} {w.Betrag:0.00}");
                if (!vorschau)
                {
                    var vorhanden = await _db.LohnZulagen.FirstOrDefaultAsync(z => z.EmployeeId == emp.Id && z.Periode == periodeStr && z.LohnpositionId == lp.Id && z.Bemerkung == "Swissdec-Testdaten");
                    if (vorhanden == null) _db.LohnZulagen.Add(new LohnZulage { EmployeeId = emp.Id, Periode = periodeStr, LohnpositionId = lp.Id, Betrag = Math.Abs(w.Betrag), Bemerkung = "Swissdec-Testdaten", CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now });
                    else { vorhanden.Betrag = Math.Abs(w.Betrag); vorhanden.UpdatedAt = DateTime.Now; }
                    if (w.Betrag < 0 && lp.Typ != "ABZUG") probleme.Add($"{w.Code}: negativer Betrag auf Zulage-Position {lp.Code} — als positiver Betrag gespeichert, bitte prüfen");
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
        hinweise.Add("Ferien-/Feiertagsvergütung (1160–1163) und 13. Monatslohn (1200–1202) werden nicht importiert — OneCrew rechnet sie; die Soll-Beträge stehen pro Monat zum Vergleich in der Vorschau.");
        hinweise.Add("Nach dem Anlegen: Lohnlauf → Filiale → Monat → «Lohn bestätigen» pro MA, dann Vergleich mit den Swissdec-Sollwerten (Quality Tool).");
        return Ok(new SchrittErgebnis("5b · Monatswerte", vorschau, aktionen, hinweise));
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

    /// <summary>BVG-Fixbetrag (AN = AG, Annahme) ab Monat — neuer Eintrag nur bei Änderung.</summary>
    private async Task SetzeBvgFixAsync(int employeeId, DateOnly monat, decimal betrag)
    {
        var eintraege = await _db.EmployeeVersicherungCodes.Where(v => v.EmployeeId == employeeId && v.Art == "BVG").OrderBy(v => v.ValidFrom).ToListAsync();
        var aktiv = eintraege.LastOrDefault(v => v.GiltAm(monat));
        if (aktiv != null && aktiv.BeitragFixAn == betrag) return;
        if (aktiv != null && aktiv.ValidFrom == monat) { aktiv.BeitragFixAn = betrag; aktiv.BeitragFixAg = betrag; await _db.SaveChangesAsync(); return; }
        var neu = new EmployeeVersicherungCode
        {
            EmployeeId = employeeId, Art = "BVG", Code = aktiv?.Code, ValidFrom = monat, ValidTo = aktiv?.ValidTo,
            BeitragFixAn = betrag, BeitragFixAg = betrag, BvgEintrittsgrund = aktiv?.BvgEintrittsgrund, BvgVollArbeitsfaehig = aktiv?.BvgVollArbeitsfaehig,
            BvgBasisManuell = aktiv?.BvgBasisManuell, Bemerkung = "Swissdec-Testdaten (Lohnart 5050)", CreatedAt = DateTime.UtcNow,
        };
        if (aktiv != null) aktiv.ValidTo = monat.AddDays(-1);
        _db.EmployeeVersicherungCodes.Add(neu);
        await _db.SaveChangesAsync();
    }
}
