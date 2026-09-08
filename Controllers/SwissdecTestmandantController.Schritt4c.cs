using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Models;
using System.Globalization;
using System.Text.Json;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 4c: die 647 Mutationen der Testfälle (testcase_differences_export.csv)
/// in die bestehenden versionierten Strukturen: Personalstamm (Korrekturen),
/// Wohnort-/Zivilstand-Historie, neuer Vertragsabschnitt, Austritt, neuer QST-
/// Eintrag ab Datum, neue Versicherungs-Codes, Familie. Monatswerte (Stunden,
/// Lektionen, Arbeitstage CH, BVG-Basis pro Monat) und Nachzahlungen sind
/// Lohnlauf-Themen → Schritt 5 (werden hier nur gemeldet).
/// Vorschau/Anlegen pro Monat: ?monat=2025-03 (leer = alle), ?nur=TF01,TF02.
/// </summary>
public partial class SwissdecTestmandantController
{
    [HttpGet("schritt4c/vorschau")]
    public async Task<IActionResult> Schritt4cVorschau([FromQuery] string? monat, [FromQuery] string? nur) => await Schritt4c(true, monat, nur);

    [HttpPost("schritt4c/anlegen")]
    public async Task<IActionResult> Schritt4cAnlegen([FromQuery] string? monat, [FromQuery] string? nur) => await Schritt4c(false, monat, nur);

    private sealed record Mutation(string Fall, string Tag, string Label, DateOnly Monat, string? Alt, string? Neu);

    // Monatswerte → Schritt 5
    private static readonly HashSet<string> Monatswerte = new(StringComparer.OrdinalIgnoreCase)
    {
        "PersonNumberOfHours", "PersonNumberOfLessons", "PersonWorkingDaysCH", "PersonEffectiveWorkingDays",
        "PersonAdditionalPaymentAfterWithdrawal", "PersonTeleWorkPercentage",
        "PersonSplitCurrentYearIncome", "PersonSplitPreviousYearIncome", "PersonSplitPreviousYearPeriodFrom", "PersonSplitPreviousYearPeriodUntil",
        "PersonAdditionalDeliveryDate", "PersonTAXRectificateOriginalDate", "PersonTAXRectificateOriginalDocID", "PersonTAXRectificateRemark",
        "PersonWaiveOfPensionDeduct", "PersonTAXTASPeriodForObjection",
    };
    // Abgeleitete Hilfsfelder ohne eigene Bedeutung
    private static readonly HashSet<string> Ignorieren = new(StringComparer.OrdinalIgnoreCase)
    {
        "PersonPartnerAge", "PersonPartnerBirthMonthDate", "PersonChild1Age", "PersonChild1BirthMonthDate", "ChangeCivilStatusbeforeNnYears",
    };

    private async Task<IActionResult> Schritt4c(bool vorschau, string? monat, string? nur)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var pfad = CsvPfad("testcase_differences_export.csv");
        if (!System.IO.File.Exists(pfad))
            return NotFound(new { error = "CSV_FEHLT", message = $"Datei fehlt: {pfad}" });

        var alle = LeseMutationen(pfad);
        DateOnly? nurMonat = null;
        if (!string.IsNullOrWhiteSpace(monat) && DateOnly.TryParseExact(monat.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var mm)) nurMonat = mm;
        var nurSet = string.IsNullOrWhiteSpace(nur) ? null : nur.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToUpperInvariant()).ToHashSet();
        var auswahl = alle.Where(m => (nurMonat == null || m.Monat == nurMonat) && (nurSet == null || nurSet.Contains(m.Fall.Split(' ')[0].ToUpperInvariant()))).ToList();

        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();
        var hs = await _db.Hauptsitze.AsNoTracking().FirstOrDefaultAsync(h => h.Uid == "CHE-999.999.996");
        if (hs == null) return BadRequest(new { error = "SCHRITT1_FEHLT", message = "Zuerst Schritt 1." });
        var filialen = await _db.CompanyProfiles.Where(c => c.HauptsitzId == hs.Id).ToListAsync();
        var permits = await _db.PermitTypes.AsNoTracking().ToListAsync();
        var monate = auswahl.Select(m => m.Monat).Distinct().OrderBy(x => x).ToList();
        int gruppen = 0;

        foreach (var (fall, mon) in auswahl.Select(m => (m.Fall, m.Monat)).Distinct().OrderBy(x => x.Monat).ThenBy(x => x.Fall))
        {
            var rows = auswahl.Where(m => m.Fall == fall && m.Monat == mon).ToList();
            var w = rows.GroupBy(r => r.Tag).ToDictionary(g => g.Key, g => g.Last().Neu?.Trim(), StringComparer.OrdinalIgnoreCase);
            string? V(string k) => w.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) && v != "None" ? v : null;
            bool Hat(string k) => w.ContainsKey(k);
            var persNr = NummerOhneKomma(fall.Split(' ')[0].Replace("TF", "").TrimStart('0'));
            var emp = await _db.Employees.Include(e => e.Employments).FirstOrDefaultAsync(e => e.EmployeeNumber == persNr);
            var felder = new Dictionary<string, string?>();
            var spaeter = new List<string>(); var probleme = new List<string>();
            if (emp == null) { aktionen.Add(new Aktion("aktualisieren", "Mutation", $"{fall} · {mon:MM.yyyy}", new() { ["⚠"] = "Person nicht gefunden — zuerst 4a." })); continue; }
            gruppen++;
            var tag1 = mon;                      // Wirkung ab Monatsanfang
            var vortag = tag1.AddDays(-1);

            // ── Monatswerte / später ──
            foreach (var r in rows.Where(r => Monatswerte.Contains(r.Tag))) spaeter.Add($"{r.Label}: {r.Alt ?? "–"} → {r.Neu ?? "–"}");
            var unbekannt = rows.Where(r => !Monatswerte.Contains(r.Tag) && !Ignorieren.Contains(r.Tag) && !Bekannt(r.Tag)).Select(r => $"{r.Tag} ({r.Alt}→{r.Neu})").ToList();
            if (unbekannt.Count > 0) probleme.Add("⚠ nicht abbildbar: " + string.Join(", ", unbekannt));

            // ── Personalstamm-Korrekturen ──
            var stamm = new List<string>();
            if (Hat("PersonLastname")) { stamm.Add($"Name → {V("PersonLastname")}"); if (!vorschau) emp.LastName = V("PersonLastname") ?? emp.LastName; }
            if (Hat("PersonFirstname")) { stamm.Add($"Vorname → {V("PersonFirstname")}"); if (!vorschau) emp.FirstName = V("PersonFirstname") ?? emp.FirstName; }
            if (Hat("PersonDateOfBirth")) { var d = Datum(V("PersonDateOfBirth")); stamm.Add($"Geburtsdatum → {d:dd.MM.yyyy}"); if (!vorschau && d != null) emp.DateOfBirth = d.Value.ToDateTime(TimeOnly.MinValue); }
            if (Hat("PersonResidenceCategory"))
            {
                var pc = MapPermit(V("PersonResidenceCategory"), out var ph); var p = pc == null ? null : permits.FirstOrDefault(x => x.Code.Equals(pc, StringComparison.OrdinalIgnoreCase));
                stamm.Add($"Bewilligung → {pc ?? "–"}"); if (ph != null) probleme.Add(ph);
                if (!vorschau) emp.PermitTypeId = p?.Id;
            }
            if (Hat("PersonDateOfDeath"))
            {
                var d = Datum(V("PersonDateOfDeath")); stamm.Add($"Todesfall {d:dd.MM.yyyy} → Austritt");
                if (!vorschau && d != null) { emp.ExitDate = d.Value.ToDateTime(TimeOnly.MinValue); emp.KuendigungPer = emp.ExitDate; emp.KuendigungDurch = "AN"; emp.Austrittsgrund = "TODESFALL"; }
            }
            if (Hat("PersonCivilStatus") || Hat("PersonCivilStatusValidAsOf"))
            {
                var z = MapZivilstand(V("PersonCivilStatus")) ?? emp.MaritalStatus; var ab = Datum(V("PersonCivilStatusValidAsOf")) ?? tag1;
                stamm.Add($"Zivilstand → {z} ab {ab:dd.MM.yyyy}");
                if (!vorschau)
                {
                    var hist = await _db.EmployeeZivilstandHistories.FirstOrDefaultAsync(h => h.EmployeeId == emp.Id && h.GueltigAb == ab);
                    if (hist == null && !string.IsNullOrEmpty(z))
                    {
                        if (!await _db.EmployeeZivilstandHistories.AnyAsync(h => h.EmployeeId == emp.Id) && !string.IsNullOrEmpty(emp.MaritalStatus))
                            _db.EmployeeZivilstandHistories.Add(new EmployeeZivilstandHistory { EmployeeId = emp.Id, Zivilstand = emp.MaritalStatus, GueltigAb = emp.MaritalStatusSince, Bemerkung = "Stand Eintritt (Swissdec-Testdaten)", CreatedAt = DateTime.Now });
                        _db.EmployeeZivilstandHistories.Add(new EmployeeZivilstandHistory { EmployeeId = emp.Id, Zivilstand = z, GueltigAb = ab, Bemerkung = "Swissdec-Testdaten Mutation", CreatedAt = DateTime.Now });
                    }
                    emp.MaritalStatus = z; emp.MaritalStatusSince = ab;
                }
            }
            // Umzug
            if (Hat("PersonStreet") || Hat("PersonZIPCode") || Hat("PersonCity") || Hat("PersonResidenceCanton") || Hat("PersonCountry") || Hat("PersonMunicipalityID"))
            {
                var land = LandCode(V("PersonCountry")) ?? emp.Country;
                var plz = NummerOhneKomma(V("PersonZIPCode")) ?? emp.ZipCode; var ort = V("PersonCity") ?? emp.City; var str = V("PersonStreet") ?? emp.Street;
                var kt = land == "CH" ? (V("PersonResidenceCanton") ?? emp.CantonCode) : null;
                if (kt == "EX") kt = null;
                stamm.Add($"Umzug → {str}, {plz} {ort} ({land}{(kt != null ? " " + kt : "")}) ab {tag1:dd.MM.yyyy}");
                if (!vorschau)
                {
                    if (!await _db.EmployeeWohnortHistories.AnyAsync(h => h.EmployeeId == emp.Id))
                        _db.EmployeeWohnortHistories.Add(new EmployeeWohnortHistory { EmployeeId = emp.Id, Plz = emp.ZipCode, Ort = emp.City, Strasse = emp.Street, KantonCode = emp.CantonCode, GueltigAb = null, Bemerkung = "Stand Eintritt (Swissdec-Testdaten)", CreatedAt = DateTime.Now });
                    if (!await _db.EmployeeWohnortHistories.AnyAsync(h => h.EmployeeId == emp.Id && h.GueltigAb == tag1))
                        _db.EmployeeWohnortHistories.Add(new EmployeeWohnortHistory { EmployeeId = emp.Id, Plz = plz, Ort = ort, Strasse = str, KantonCode = kt, GueltigAb = tag1, Bemerkung = "Swissdec-Testdaten Mutation", CreatedAt = DateTime.Now });
                    emp.Street = str; emp.ZipCode = plz; emp.City = ort; emp.Country = land; emp.CantonCode = kt;
                }
            }
            if (Hat("PersonDepartureDate")) { stamm.Add($"Wegzug aus der Schweiz per {Datum(V("PersonDepartureDate")):dd.MM.yyyy} (QST-Wirkung mit der Adressmutation)"); }
            if (Hat("PersonEntryDate") && !Hat("PersonWithdrawalDate"))
            {
                var d = Datum(V("PersonEntryDate")); stamm.Add($"Wiedereintritt {d:dd.MM.yyyy}");
                if (!vorschau && d != null) { emp.EntryDate = d.Value.ToDateTime(TimeOnly.MinValue); emp.ExitDate = null; emp.KuendigungPer = null; emp.IsActive = true; }
            }
            if (stamm.Count > 0) felder["Person"] = string.Join(" · ", stamm);
            if (!vorschau) await _db.SaveChangesAsync();

            // ── Familie ──
            var fam = new List<string>();
            if (rows.Any(r => r.Tag.StartsWith("PersonPartner")))
            {
                fam.Add($"Partner {V("PersonPartnerFirstname")} {V("PersonPartnerLastname")}".Trim() + (Hat("PersonPartnerStartActivity") ? $" · erwerbstätig ab {Datum(V("PersonPartnerStartActivity")):dd.MM.yyyy} ({V("PersonPartnerWorkplace")})" : "") + (Hat("PersonPartnerResidenceAbroadCountry") ? $" · Wohnsitz {V("PersonPartnerResidenceAbroadCountry")}" : ""));
                if (!vorschau)
                {
                    var p = await _db.EmployeeFamilyMembers.FirstOrDefaultAsync(m => m.EmployeeId == emp.Id && m.MemberType == "Ehepartner")
                         ?? new EmployeeFamilyMember { EmployeeId = emp.Id, MemberType = "Ehepartner", CreatedAt = DateTime.Now };
                    if (Hat("PersonPartnerFirstname")) p.FirstName = V("PersonPartnerFirstname");
                    if (Hat("PersonPartnerLastname")) p.LastName = V("PersonPartnerLastname");
                    if (Hat("PersonPartnerDateOfBirth")) p.DateOfBirth = Datum(V("PersonPartnerDateOfBirth"))?.ToDateTime(TimeOnly.MinValue);
                    if (Hat("PersonPartnerSVASNumber")) p.SocialSecurityNumber = V("PersonPartnerSVASNumber") == "unknown" ? null : V("PersonPartnerSVASNumber");
                    if (Hat("PersonPartnerResidenceCantonCH") || Hat("PersonPartnerCountry") || Hat("PersonPartnerResidenceAbroadCountry"))
                        p.LivesInSwitzerland = V("PersonPartnerResidenceCantonCH") != null || LandCode(V("PersonPartnerCountry")) == "CH";
                    if (Hat("PersonPartnerStartActivity"))
                    {
                        p.Erwerbstaetig = true; p.Stellenantritt = Datum(V("PersonPartnerStartActivity"))?.ToDateTime(TimeOnly.MinValue);
                        var pw = V("PersonPartnerWorkplace"); if (pw != null) { p.ArbeitgeberOrt = pw.Length > 2 ? pw : null; p.ArbeitgeberKanton = pw.Length == 2 ? pw : p.ArbeitgeberKanton; }
                    }
                    p.Gender = emp.Gender == "female" ? "male" : emp.Gender == "male" ? "female" : p.Gender;
                    p.UpdatedAt = DateTime.Now;
                    if (p.Id == 0) _db.EmployeeFamilyMembers.Add(p);
                    await _db.SaveChangesAsync();
                }
            }
            if (rows.Any(r => r.Tag.StartsWith("PersonChild1")))
            {
                var kv = V("PersonChild1Firstname"); var von = Datum(V("PersonChild1StartTAS")); var bis = Datum(V("PersonChild1EndTAS"));
                fam.Add($"Kind {kv} ({Datum(V("PersonChild1DateOfBirth")):dd.MM.yyyy}) QST-Abzug {von:dd.MM.yyyy}–{bis:dd.MM.yyyy}");
                if (!vorschau && kv != null)
                {
                    var kind = await _db.EmployeeFamilyMembers.FirstOrDefaultAsync(m => m.EmployeeId == emp.Id && m.MemberType == "Kind" && m.FirstName == kv)
                            ?? new EmployeeFamilyMember { EmployeeId = emp.Id, MemberType = "Kind", FirstName = kv, CreatedAt = DateTime.Now };
                    kind.LastName = V("PersonChild1Lastname") ?? emp.LastName;
                    if (Hat("PersonChild1DateOfBirth")) kind.DateOfBirth = Datum(V("PersonChild1DateOfBirth"))?.ToDateTime(TimeOnly.MinValue);
                    kind.QstDeductibleFrom = von?.ToDateTime(TimeOnly.MinValue); kind.QstDeductibleUntil = bis?.ToDateTime(TimeOnly.MinValue);
                    kind.LebtImHaushalt = true; kind.LivesInSwitzerland = emp.Country == "CH"; kind.UpdatedAt = DateTime.Now;
                    if (kind.Id == 0) _db.EmployeeFamilyMembers.Add(kind);
                    await _db.SaveChangesAsync();
                }
            }
            if (fam.Count > 0) felder["Familie"] = string.Join(" · ", fam);

            // ── Vertrag: Austritt / neuer Abschnitt ──
            var aktiv = emp.Employments.Where(x => x.IsActive).OrderByDescending(x => x.ContractStartDate).FirstOrDefault();
            if (Hat("PersonWithdrawalDate"))
            {
                var d = Datum(V("PersonWithdrawalDate"));
                felder["Austritt"] = d == null ? "Austritt zurückgenommen" : $"per {d:dd.MM.yyyy}";
                if (!vorschau)
                {
                    emp.ExitDate = d?.ToDateTime(TimeOnly.MinValue); emp.KuendigungPer = emp.ExitDate;
                    emp.KuendigungAusgesprochenAm = d == null ? null : mon.AddDays(-1).ToDateTime(TimeOnly.MinValue);
                    if (aktiv != null) aktiv.ContractEndDate = emp.ExitDate;
                    await _db.SaveChangesAsync();
                }
            }
            var vertragTags = new[] { "PersonAgreedWeeklyHours", "PersonActivityRateEmployer1", "PersonContractMonthly", "PersonContractHourly", "PersonContractNoTimeConstraint", "PersonContractNoTimeConstraintAnnualWage", "PersonWorkplace", "PersonCompanyWorkingTimeModel", "PersonContractHourlyWagePaidByHour", "PersonContractHourlyWagePaidByLesson", "PersonHourlyLessonWage", "PersonActivityRateUnsteady" };
            if (vertragTags.Any(Hat) && aktiv != null)
            {
                var aend = rows.Where(r => vertragTags.Contains(r.Tag)).Select(r => $"{r.Label}: {r.Alt ?? "–"} → {r.Neu ?? "–"}").ToList();
                var neuesModell = Hat("PersonContractMonthly") && V("PersonContractMonthly") != null ? "FIX" : Hat("PersonContractHourly") && V("PersonContractHourly") != null ? "FLEX" : aktiv.EmploymentModel;
                var wp = V("PersonWorkplace"); var fil = wp == null ? null : filialen.FirstOrDefault(c => string.Equals(c.RestaurantCode, wp, StringComparison.OrdinalIgnoreCase));
                if (wp != null && fil == null) probleme.Add($"Filiale «{wp}» nicht gefunden.");
                felder["Vertrag"] = $"neuer Abschnitt {neuesModell} ab {tag1:dd.MM.yyyy}: " + string.Join(" · ", aend);
                if (!vorschau)
                {
                    if (aktiv.ContractStartDate.Date >= tag1.ToDateTime(TimeOnly.MinValue).Date)
                    {
                        // Abschnitt beginnt im selben Monat → direkt anpassen
                        WendeVertragAn(aktiv, w, neuesModell, fil);
                    }
                    else
                    {
                        var neu = new Employment
                        {
                            EmployeeId = emp.Id, CompanyProfileId = aktiv.CompanyProfileId, EmploymentModel = aktiv.EmploymentModel, SalaryType = aktiv.SalaryType,
                            ContractStartDate = tag1.ToDateTime(TimeOnly.MinValue), ContractEndDate = aktiv.ContractEndDate, JobTitle = aktiv.JobTitle, JobGroupId = aktiv.JobGroupId,
                            ContractType = aktiv.ContractType, EducationLevelCode = aktiv.EducationLevelCode, EmploymentPercentage = aktiv.EmploymentPercentage,
                            WeeklyHours = aktiv.WeeklyHours, GuaranteedHoursPerWeek = aktiv.GuaranteedHoursPerWeek, LessonRate = aktiv.LessonRate, WeeklyLessons = aktiv.WeeklyLessons,
                            TeilzeitUnter8hWoche = aktiv.TeilzeitUnter8hWoche, MonthlySalaryFte = aktiv.MonthlySalaryFte, MonthlySalary = aktiv.MonthlySalary, HourlyRate = aktiv.HourlyRate,
                            EasyAtWorkManualOverride = true, VacationPaymentMode = aktiv.VacationPaymentMode, IsActive = true,
                            ThirteenthSalary = aktiv.ThirteenthSalary,
                        };
                        WendeVertragAn(neu, w, neuesModell, fil);
                        aktiv.ContractEndDate = vortag.ToDateTime(TimeOnly.MinValue);
                        _db.Employments.Add(neu); emp.Employments.Add(neu);
                    }
                    await _db.SaveChangesAsync();
                }
            }
            else if (vertragTags.Any(Hat)) probleme.Add("Vertragsänderung, aber kein aktiver Vertrag gefunden.");

            // ── Versicherungs-Codes ──
            var codeTags = new Dictionary<string, string> { ["PersonUVGLAACode"] = "UVG", ["PersonUVGZLAACCode1"] = "UVGZ", ["PersonUVGZLAACCode2"] = "UVGZ", ["PersonKTGAMCCode1"] = "KTG", ["PersonKTGAMCCode2"] = "KTG", ["PersonBVGLPPCode1"] = "BVG" };
            var betroffen = codeTags.Where(kv => Hat(kv.Key)).Select(kv => kv.Value).Distinct().ToList();
            if (Hat("PersonBVGLPPInsured") || Hat("PersonBVGLPPManuallyBase")) betroffen.Add("BVG");
            betroffen = betroffen.Distinct().ToList();
            if (betroffen.Count > 0)
            {
                var txt = new List<string>();
                var bestehend = await _db.EmployeeVersicherungCodes.Where(v => v.EmployeeId == emp.Id).ToListAsync();
                foreach (var art in betroffen)
                {
                    var aktuelle = bestehend.Where(v => v.Art == art && v.GiltAm(vortag)).ToList();
                    var neueCodes = new List<string>();
                    if (art == "BVG")
                    {
                        var vers = Hat("PersonBVGLPPInsured") ? V("PersonBVGLPPInsured") is "1" or "1.0" : aktuelle.Any();
                        var code = NummerOhneKomma(V("PersonBVGLPPCode1")) ?? aktuelle.FirstOrDefault()?.Code;
                        if (vers && code != null) neueCodes.Add(code);
                    }
                    else
                    {
                        foreach (var kv in codeTags.Where(kv => kv.Value == art))
                        {
                            var c = Hat(kv.Key) ? NummerOhneKomma(V(kv.Key)) : aktuelle.Select(a => a.Code).Skip(kv.Key.EndsWith("2") ? 1 : 0).FirstOrDefault();
                            if (c != null && c != "0") neueCodes.Add(c);
                        }
                        if (art == "UVG") neueCodes = neueCodes.Take(1).ToList();
                    }
                    neueCodes = neueCodes.Distinct().ToList();
                    var basis = Dez(V("PersonBVGLPPManuallyBase"));
                    txt.Add($"{art}: {string.Join("+", aktuelle.Select(a => a.Code)) } → {(neueCodes.Count == 0 ? "nicht versichert" : string.Join("+", neueCodes))}" + (art == "BVG" && Hat("PersonBVGLPPManuallyBase") ? $" · Basis manuell {(basis?.ToString("0") ?? "–")}" : ""));
                    if (vorschau) continue;
                    foreach (var a in aktuelle) if (a.ValidFrom < tag1) a.ValidTo = vortag; else _db.EmployeeVersicherungCodes.Remove(a);
                    var vorlage = aktuelle.FirstOrDefault(a => a.Art == "BVG");
                    foreach (var c in neueCodes)
                        _db.EmployeeVersicherungCodes.Add(new EmployeeVersicherungCode
                        {
                            EmployeeId = emp.Id, Art = art, Code = c.ToUpperInvariant(), ValidFrom = tag1, Bemerkung = "Swissdec-Testdaten Mutation", CreatedAt = DateTime.UtcNow,
                            BvgEintrittsgrund = art == "BVG" ? vorlage?.BvgEintrittsgrund : null, BvgVollArbeitsfaehig = art == "BVG" ? vorlage?.BvgVollArbeitsfaehig : null,
                            BvgBasisManuell = art == "BVG" ? (Hat("PersonBVGLPPManuallyBase") ? basis : vorlage?.BvgBasisManuell) : null,
                            BeitragFixAn = art == "BVG" ? vorlage?.BeitragFixAn : null, BeitragFixAg = art == "BVG" ? vorlage?.BeitragFixAg : null,
                        });
                }
                felder["Versicherungen"] = string.Join(" · ", txt);
                if (!vorschau) await _db.SaveChangesAsync();
            }

            // ── Quellensteuer: neuer Eintrag ab Datum ──
            var qstTags = new[] { "PersonTASCode", "PersonTASCodeValidAsOf", "PersonTASCanton", "PersonTASMunicipalityID", "PersonTASCalculationModel", "PersonTASTriggerOfChange", "PersonTASKindOfResidence", "PersonGrantTASCode", "PersonCrossborder", "PersonCrossborderPlaceOfBirth", "PersonCrossborderTaxID", "PersonCrossborderValidAsOf", "PersonOtherActivity", "PersonTotalOtherActivityRate" };
            if (qstTags.Any(Hat))
            {
                var ab = Datum(V("PersonTASCodeValidAsOf")) ?? tag1;
                var code = V("PersonTASCode"); var kt = V("PersonTASCanton");
                var aend = rows.Where(r => qstTags.Contains(r.Tag)).Select(r => $"{r.Label}: {r.Alt ?? "–"} → {r.Neu ?? "–"}").ToList();
                var beenden = Hat("PersonTASCode") && code == null && Hat("PersonTASCanton") && kt == null;
                felder["Quellensteuer"] = (beenden ? $"QST-Pflicht endet per {vortag:dd.MM.yyyy}: " : $"neuer Eintrag ab {ab:dd.MM.yyyy}: ") + string.Join(" · ", aend);
                if (!vorschau)
                {
                    var eintraege = await _db.EmployeeQuellensteuer.Where(q => q.EmployeeId == emp.Id).OrderByDescending(q => q.ValidFrom).ToListAsync();
                    var letzter = eintraege.FirstOrDefault(q => q.ValidFrom <= ab && (q.ValidTo == null || q.ValidTo >= ab)) ?? eintraege.FirstOrDefault();
                    if (beenden)
                    {
                        foreach (var q in eintraege.Where(q => q.ValidTo == null || q.ValidTo >= ab)) q.ValidTo = ab.AddDays(-1);
                    }
                    else
                    {
                        var q = eintraege.FirstOrDefault(x => x.ValidFrom == ab);
                        if (q == null)
                        {
                            q = new EmployeeQuellensteuer { EmployeeId = emp.Id, ValidFrom = ab, CreatedAt = DateTime.Now };
                            if (letzter != null)
                            {
                                q.Steuerkanton = letzter.Steuerkanton; q.SteuerkantonName = letzter.SteuerkantonName; q.QstGemeinde = letzter.QstGemeinde; q.QstGemeindeBfsNr = letzter.QstGemeindeBfsNr;
                                q.QstCode = letzter.QstCode; q.TarifCode = letzter.TarifCode; q.AnzahlKinder = letzter.AnzahlKinder; q.Kirchensteuer = letzter.Kirchensteuer;
                                q.ArbeitsortKanton = letzter.ArbeitsortKanton; q.WeitereBeschaftigungen = letzter.WeitereBeschaftigungen; q.GesamtpensumWeitereAg = letzter.GesamtpensumWeitereAg;
                                q.Halbfamilie = letzter.Halbfamilie; q.IsGrenzgaenger = letzter.IsGrenzgaenger; q.IsWochenaufenthalter = letzter.IsWochenaufenthalter;
                                q.Wohnsitzstaat = letzter.Wohnsitzstaat; q.WohnsitzAusland = letzter.WohnsitzAusland;
                                q.GrenzgaengerSteuerId = letzter.GrenzgaengerSteuerId; q.GrenzgaengerGeburtsort = letzter.GrenzgaengerGeburtsort; q.GrenzgaengerAb = letzter.GrenzgaengerAb;
                                q.SpezielBewilligt = letzter.SpezielBewilligt;
                            }
                            q.TarifvorschlagQst = false;
                            _db.EmployeeQuellensteuer.Add(q);
                            foreach (var alt in eintraege.Where(x => x.ValidFrom < ab && (x.ValidTo == null || x.ValidTo >= ab))) alt.ValidTo = ab.AddDays(-1);
                        }
                        if (code != null)
                        {
                            var m = System.Text.RegularExpressions.Regex.Match(code, @"^([A-Z]{1,2})(\d)([YN])$");
                            q.QstCode = code; q.TarifCode = m.Success ? m.Groups[1].Value : q.TarifCode;
                            q.AnzahlKinder = m.Success ? int.Parse(m.Groups[2].Value) : q.AnzahlKinder; q.Kirchensteuer = m.Success ? m.Groups[3].Value == "Y" : q.Kirchensteuer;
                        }
                        if (kt != null) { q.Steuerkanton = kt; q.SteuerkantonName = null; }
                        if (Hat("PersonTASMunicipalityID")) q.QstGemeindeBfsNr = int.TryParse(NummerOhneKomma(V("PersonTASMunicipalityID")), out var g) ? g : null;
                        if (Hat("PersonGrantTASCode")) { q.SpezielBewilligt = V("PersonGrantTASCode") != null; if (V("PersonGrantTASCode") != null) q.QstCode = V("PersonGrantTASCode"); }
                        if (Hat("PersonCrossborderTaxID")) q.GrenzgaengerSteuerId = V("PersonCrossborderTaxID");
                        if (Hat("PersonCrossborderPlaceOfBirth")) q.GrenzgaengerGeburtsort = V("PersonCrossborderPlaceOfBirth");
                        if (Hat("PersonCrossborderValidAsOf")) q.GrenzgaengerAb = Datum(V("PersonCrossborderValidAsOf"));
                        if (Hat("PersonCrossborder")) q.IsGrenzgaenger = V("PersonCrossborder") != null;
                        if (Hat("PersonOtherActivity")) q.WeitereBeschaftigungen = V("PersonOtherActivity") != null;
                        if (Hat("PersonTotalOtherActivityRate")) q.GesamtpensumWeitereAg = Dez(V("PersonTotalOtherActivityRate"));
                        if (Hat("PersonTASKindOfResidence")) q.IsWochenaufenthalter = V("PersonTASKindOfResidence") == "Weekly";
                        q.HerleitungJson = JsonSerializer.Serialize(new { quelle = "Swissdec-Testdaten Mutation", monat = mon.ToString("yyyy-MM"), ausloeser = V("PersonTASTriggerOfChange"), modell = V("PersonTASCalculationModel") });
                        q.UpdatedAt = DateTime.Now;
                    }
                    await _db.SaveChangesAsync();
                }
            }

            if (spaeter.Count > 0) felder["→ Schritt 5 (Monatswerte)"] = string.Join(" · ", spaeter);
            if (probleme.Count > 0) felder["⚠ Hinweise"] = string.Join(" | ", probleme);
            aktionen.Add(new Aktion(vorschau ? "aktualisieren" : "aktualisieren", "Mutation", $"{mon:MM.yyyy} · {fall}", felder));
        }

        hinweise.Insert(0, $"{auswahl.Count} Mutationszeilen in {gruppen} Person/Monat-Gruppen" + (nurMonat != null ? $" (Monat {nurMonat:MM.yyyy})" : $" über {monate.Count} Monate {monate.FirstOrDefault():MM.yyyy}–{monate.LastOrDefault():MM.yyyy}") + ".");
        hinweise.Add("Monatswerte (Stunden, Lektionen, Arbeitstage CH/effektiv, BVG-Basis pro Monat, Nachzahlung nach Austritt, Telearbeit, AHV-Splitting, Lohnausweis-Rektifikat) werden hier nur angezeigt — sie gehören in den jeweiligen Lohnlauf (Schritt 5).");
        hinweise.Add("Reihenfolge beachten: Mutationen chronologisch anlegen (leer = alle Monate in Reihenfolge), damit Verträge/QST-Einträge sauber aneinanderreihen. Wiederholtes Anlegen desselben Monats ist unkritisch (Einträge werden über das Datum wiedergefunden).");
        return Ok(new SchrittErgebnis("4c · Mutationen", vorschau, aktionen, hinweise));
    }

    private static bool Bekannt(string tag) =>
        tag.StartsWith("PersonPartner") || tag.StartsWith("PersonChild1") || tag.StartsWith("PersonCrossborder") || tag.StartsWith("PersonTAS")
        || tag is "PersonLastname" or "PersonFirstname" or "PersonDateOfBirth" or "PersonResidenceCategory" or "PersonDateOfDeath" or "PersonCivilStatus" or "PersonCivilStatusValidAsOf"
        or "PersonStreet" or "PersonZIPCode" or "PersonCity" or "PersonResidenceCanton" or "PersonCountry" or "PersonMunicipalityID" or "PersonDepartureDate" or "PersonEntryDate"
        or "PersonWithdrawalDate" or "PersonAgreedWeeklyHours" or "PersonActivityRateEmployer1" or "PersonContractMonthly" or "PersonContractHourly" or "PersonContractNoTimeConstraint"
        or "PersonContractNoTimeConstraintAnnualWage" or "PersonWorkplace" or "PersonCompanyWorkingTimeModel" or "PersonContractHourlyWagePaidByHour" or "PersonContractHourlyWagePaidByLesson"
        or "PersonHourlyLessonWage" or "PersonActivityRateUnsteady" or "PersonUVGLAACode" or "PersonUVGZLAACCode1" or "PersonUVGZLAACCode2" or "PersonKTGAMCCode1" or "PersonKTGAMCCode2"
        or "PersonBVGLPPCode1" or "PersonBVGLPPInsured" or "PersonBVGLPPManuallyBase" or "PersonGrantTASCode" or "PersonOtherActivity" or "PersonTotalOtherActivityRate";

    private static void WendeVertragAn(Employment v, Dictionary<string, string?> w, string modell, CompanyProfile? fil)
    {
        string? V(string k) => w.TryGetValue(k, out var x) && !string.IsNullOrWhiteSpace(x) && x != "None" ? x : null;
        bool Hat(string k) => w.ContainsKey(k);
        v.EmploymentModel = modell; v.SalaryType = modell == "FIX" ? "monthly" : "hourly";
        if (fil != null) v.CompanyProfileId = fil.Id;
        if (Hat("PersonAgreedWeeklyHours")) v.WeeklyHours = Dez(V("PersonAgreedWeeklyHours"));
        if (Hat("PersonActivityRateEmployer1")) v.EmploymentPercentage = modell == "FIX" ? Dez(V("PersonActivityRateEmployer1")) : v.EmploymentPercentage;
        if (Hat("PersonContractHourlyWagePaidByHour")) v.HourlyRate = Dez(V("PersonContractHourlyWagePaidByHour"));
        else if (Hat("PersonHourlyLessonWage") && modell == "FLEX") v.HourlyRate = Dez(V("PersonHourlyLessonWage"));
        if (Hat("PersonContractHourlyWagePaidByLesson")) v.LessonRate = Dez(V("PersonContractHourlyWagePaidByLesson"));
        if (Hat("PersonContractMonthly") && V("PersonContractMonthly") == "fixedSalaryMth") v.ContractType = "befristet";
        if (modell == "FIX") { v.HourlyRate = null; v.EmploymentPercentage ??= 100m; }
        else { v.MonthlySalary = null; v.MonthlySalaryFte = null; v.EmploymentPercentage = null; }
        if (Hat("PersonContractNoTimeConstraintAnnualWage") && Dez(V("PersonContractNoTimeConstraintAnnualWage")) is { } jl)
        { v.MonthlySalary = Math.Round(jl / 12m, 2); v.MonthlySalaryFte = v.MonthlySalary; }
    }

    private static List<Mutation> LeseMutationen(string pfad)
    {
        var res = new List<Mutation>();
        var text = System.IO.File.ReadAllText(pfad, System.Text.Encoding.UTF8);
        var zeilen = new List<List<string>>();
        var feld = new System.Text.StringBuilder(); var zeile = new List<string>(); bool inQ = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQ) { if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { feld.Append('"'); i++; } else if (c == '"') inQ = false; else feld.Append(c); }
            else if (c == '"') inQ = true;
            else if (c == ',') { zeile.Add(feld.ToString()); feld.Clear(); }
            else if (c == '\n' || c == '\r') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; zeile.Add(feld.ToString()); feld.Clear(); if (zeile.Any(x => x.Length > 0)) zeilen.Add(zeile); zeile = new List<string>(); }
            else feld.Append(c);
        }
        if (feld.Length > 0 || zeile.Count > 0) { zeile.Add(feld.ToString()); zeilen.Add(zeile); }
        foreach (var z in zeilen.Skip(1))
        {
            if (z.Count < 6) continue;
            if (!DateOnly.TryParseExact(z[3].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)) continue;
            res.Add(new Mutation(z[0].Trim(), z[1].Trim(), z[2].Trim(), m, string.IsNullOrWhiteSpace(z[4]) ? null : z[4].Trim(), string.IsNullOrWhiteSpace(z[5]) ? null : z[5].Trim()));
        }
        return res;
    }
}
