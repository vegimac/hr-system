using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HrSystem.Models;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HrSystem.Controllers;

/// <summary>
/// Schritt 4a des Swissdec-Testmandanten: die 44 Testfall-Personen mit Vertrag,
/// Familie, Zusatzadresse, Versicherungs-Codes und QST-Erfassung — Stand beim
/// Eintritt (testcases_export.csv). Mutationen (differences) kommen in 4c.
/// Nur normale Produktstrukturen: Employee, Employment (manuell + easy@work-
/// Block), EmployeeAddress «Wochenaufenthalt», EmployeeFamilyMember (Kind/
/// Ehepartner), EmployeeVersicherungCode, EmployeeQuellensteuer.
/// Idempotent: Person wird über die Personalnummer wiedergefunden.
/// </summary>
public partial class SwissdecTestmandantController
{
    [HttpGet("schritt4/vorschau")]
    public async Task<IActionResult> Schritt4Vorschau([FromQuery] string? nur) => await Schritt4(vorschau: true, nur);

    [HttpPost("schritt4/anlegen")]
    public async Task<IActionResult> Schritt4Anlegen([FromQuery] string? nur) => await Schritt4(vorschau: false, nur);

    private sealed record Testfall(string Id, string Name, DateOnly Entry, Dictionary<string, string> W);

    private async Task<IActionResult> Schritt4(bool vorschau, string? nur)
    {
        if (!IstTestinstanz())
            return StatusCode(403, new { error = "NUR_TESTINSTANZ", message = "Der Swissdec-Testmandant darf nur auf der Testinstanz geladen werden." });
        var pfad = CsvPfad("testcases_export.csv");
        if (!System.IO.File.Exists(pfad))
            return NotFound(new { error = "CSV_FEHLT", message = $"Datei fehlt: {pfad}" });

        var faelle = LeseTestfaelle(pfad);
        if (!string.IsNullOrWhiteSpace(nur))
        {
            var set = nur.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.ToUpperInvariant()).ToHashSet();
            faelle = faelle.Where(f => set.Contains(f.Id.ToUpperInvariant())).ToList();
        }

        var aktionen = new List<Aktion>();
        var hinweise = new List<string>();

        var hs = await _db.Hauptsitze.AsNoTracking().FirstOrDefaultAsync(h => h.Uid == "CHE-999.999.996");
        if (hs == null) return BadRequest(new { error = "SCHRITT1_FEHLT", message = "Zuerst Schritt 1 (Firma + Filialen) anlegen." });
        var filialen = await _db.CompanyProfiles.Where(c => c.HauptsitzId == hs.Id).ToListAsync();
        var permits = await _db.PermitTypes.AsNoTracking().ToListAsync();
        var nats = await _db.Nationalities.AsNoTracking().ToListAsync();
        var svLoesungen = await _db.SocialInsuranceRates.AsNoTracking().Where(r => r.IsActive && r.LoesungsCode != null).Select(r => new { r.Code, r.LoesungsCode }).ToListAsync();
        var eduCodes = await _db.EducationLevels.AsNoTracking().Select(e => e.Code).ToListAsync();
        var eduFehlend = new HashSet<string>();

        foreach (var f in faelle)
        {
            string? V(string k) => f.W.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) && v != "None" ? v.Trim() : null;
            var felder = new Dictionary<string, string?>();
            var probleme = new List<string>();

            // ── Person ──
            var persNr = NummerOhneKomma(V("PersonEmployeeNumber")) ?? f.Id;
            var emp = await _db.Employees.Include(e => e.Employments).FirstOrDefaultAsync(e => e.EmployeeNumber == persNr);
            var neu = emp == null;
            emp ??= new Employee { EmployeeNumber = persNr };
            var sex = V("PersonSex") == "F" ? "female" : V("PersonSex") == "M" ? "male" : null;
            var natCode = V("PersonNationality");
            var nat = nats.FirstOrDefault(n => n.Code.Equals(natCode, StringComparison.OrdinalIgnoreCase));
            var land = LandCode(V("PersonCountry"));
            var zivil = MapZivilstand(V("PersonCivilStatus"));
            var permitCode = MapPermit(V("PersonResidenceCategory"), out var permitHinweis);
            var permit = permitCode == null ? null : permits.FirstOrDefault(p => p.Code.Equals(permitCode, StringComparison.OrdinalIgnoreCase));
            if (permitCode != null && permit == null) probleme.Add($"Bewilligung «{permitCode}» nicht im Katalog (permit_type).");
            if (permitHinweis != null) probleme.Add(permitHinweis);
            var svas = V("PersonSVASNumber"); if (svas == "unknown") svas = null;
            var geb = Datum(V("PersonDateOfBirth"));
            var wp = V("PersonWorkplace");
            var filiale = filialen.FirstOrDefault(c => string.Equals(c.RestaurantCode, wp, StringComparison.OrdinalIgnoreCase));
            if (filiale == null) probleme.Add($"Filiale «{wp}» nicht gefunden (Schritt 1).");

            felder["Person"] = $"{V("PersonFirstname")} {V("PersonLastname")} · {V("PersonSex")} · {geb:dd.MM.yyyy} · {natCode} · AHV {svas ?? "–"}";
            felder["Adresse"] = $"{V("PersonStreet")}, {NummerOhneKomma(V("PersonZIPCode"))} {V("PersonCity")} ({land}{(V("PersonResidenceCanton") != null ? " " + V("PersonResidenceCanton") : "")})";
            felder["Zivilstand"] = $"{zivil ?? "–"}{(Datum(V("PersonCivilStatusValidAsOf")) is { } zs ? " seit " + zs.ToString("dd.MM.yyyy") : "")} · Konfession {MapKonfession(V("PersonDenomination")) ?? "–"} · Bewilligung {permitCode ?? "–"}";

            if (!vorschau)
            {
                emp.FirstName = V("PersonFirstname") ?? ""; emp.LastName = V("PersonLastname") ?? "";
                emp.Gender = sex; emp.Salutation = sex == "female" ? "Frau" : sex == "male" ? "Herr" : null;
                emp.DateOfBirth = geb?.ToDateTime(TimeOnly.MinValue);
                emp.Nationality = natCode; emp.NationalityId = nat?.Id;
                emp.SocialSecurityNumber = svas;
                emp.Street = V("PersonStreet"); emp.ZipCode = NummerOhneKomma(V("PersonZIPCode")); emp.City = V("PersonCity");
                emp.Country = land; emp.CantonCode = land == "CH" ? V("PersonResidenceCanton") : null;
                emp.LanguageCode = V("PersonLanguageCode") ?? "de";
                emp.MaritalStatus = zivil; emp.MaritalStatusSince = Datum(V("PersonCivilStatusValidAsOf"));
                emp.Religion = MapKonfession(V("PersonDenomination"));
                emp.PermitTypeId = permit?.Id;
                emp.EntryDate = f.Entry.ToDateTime(TimeOnly.MinValue);
                emp.IsActive = true; emp.LgavPflichtig = false;
                if (neu) _db.Employees.Add(emp);
                await _db.SaveChangesAsync();
            }

            // ── Vertrag ──
            var monatlich = V("PersonContractMonthly") != null;
            var stundenlohn = V("PersonContractHourly") != null;
            var ohneZeit = V("PersonContractNoTimeConstraint");
            var pensum = Dez(V("PersonActivityRateEmployer1"));
            var wochenStd = Dez(V("PersonAgreedWeeklyHours"));
            var lohnMt = Dez(V("PersonContractMonthly")) ?? Dez(V("PersonMonthlySalary"));
            // Monatslohn-Betrag steht nicht in der CSV-Stammzeile (kommt mit den Lohnarten 1000) → aus wagetypes später; hier Struktur.
            var stdSatz = Dez(V("PersonContractHourlyWagePaidByHour")) ?? Dez(V("PersonHourlyLessonWage"));
            var lektSatz = Dez(V("PersonContractHourlyWagePaidByLesson"));
            var wochenLekt = Dez(V("PersonAgreedWeeklyLessons"));
            string modell = monatlich ? "FIX" : stundenlohn ? "FLEX" : (ohneZeit != null ? "FIX" : "FLEX");
            if (!monatlich && !stundenlohn) probleme.Add($"Kein Monats-/Stundenlohnvertrag in den Testdaten ({ohneZeit ?? "–"}) — Vertrag als {modell} ohne Lohn angelegt (Honorar/VR über Lohnarten).");
            felder["Vertrag"] = $"{modell} ab {f.Entry:dd.MM.yyyy} · Filiale {wp} · Pensum {pensum?.ToString("0.#") ?? "–"} % · {wochenStd?.ToString("0.#") ?? "–"} h/Wo"
                + (stdSatz != null ? $" · CHF {stdSatz:0.00}/h" : "") + (lektSatz != null && (wochenLekt != null || V("PersonNumberOfLessons") != null) ? $" · CHF {lektSatz:0.00}/Lektion, {wochenLekt?.ToString("0.#") ?? "–"} Lekt./Wo" : "")
                + (V("PersonContractMonthly13th") != null ? " · 13. ML" : "") + (V("PersonContractMonthly14th") != null ? " · 14. ML (als Lohnposition)" : "")
                + $" · {V("PersonJobTitle")} ({V("PersonPosition")}) · Ausbildung {V("PersonEducation")}";
            var edu = MapEducation(V("PersonEducation"));
            if (edu != null && !eduCodes.Contains(edu)) { eduFehlend.Add(edu); edu = null; }

            if (!vorschau && filiale != null)
            {
                var vertrag = emp.Employments.FirstOrDefault(x => x.EasyAtWorkContractId == null && x.ContractStartDate.Date == f.Entry.ToDateTime(TimeOnly.MinValue).Date)
                           ?? emp.Employments.FirstOrDefault(x => x.EasyAtWorkContractId == null && x.IsActive);
                var vNeu = vertrag == null;
                vertrag ??= new Employment { EmployeeId = emp.Id };
                vertrag.CompanyProfileId = filiale.Id;
                vertrag.EmploymentModel = modell;
                vertrag.SalaryType = modell == "FIX" ? "monthly" : "hourly";
                vertrag.ContractStartDate = f.Entry.ToDateTime(TimeOnly.MinValue);
                vertrag.ContractEndDate = null;
                vertrag.ContractType = V("PersonContractMonthly") == "fixedSalaryMth" ? "befristet" : "unbefristet";
                vertrag.JobTitle = V("PersonJobTitle");
                vertrag.EducationLevelCode = edu;
                vertrag.EmploymentPercentage = modell == "FIX" ? (pensum ?? 100m) : null;
                vertrag.WeeklyHours = wochenStd;
                vertrag.HourlyRate = modell == "FLEX" ? stdSatz : null;
                vertrag.LessonRate = (wochenLekt != null || V("PersonNumberOfLessons") != null) ? lektSatz : null;
                vertrag.WeeklyLessons = wochenLekt;
                vertrag.MonthlySalary = modell == "FIX" ? lohnMt : null;
                vertrag.MonthlySalaryFte = modell == "FIX" && lohnMt != null && pensum is > 0 ? Math.Round(lohnMt.Value * 100m / pensum.Value, 2) : null;
                vertrag.TeilzeitUnter8hWoche = false;
                vertrag.ThirteenthSalary = V("PersonContractMonthly13th") != null || V("PersonContractHourly13th") != null;
                vertrag.EasyAtWorkManualOverride = true;   // manuell erfasst → geschützt (Walter 07.09.2026)
                vertrag.IsActive = true;
                if (vNeu) { _db.Employments.Add(vertrag); emp.Employments.Add(vertrag); }
                await _db.SaveChangesAsync();
            }

            // ── Wochenaufenthalt (Zusatzadresse) ──
            if (V("PersonTASWeeklyCity") != null)
            {
                felder["Wochenaufenthalt"] = $"{V("PersonTASWeeklyStreet")}, {NummerOhneKomma(V("PersonTASWeeklyZIPCode"))} {V("PersonTASWeeklyCity")}";
                if (!vorschau)
                {
                    var wa = await _db.EmployeeAddresses.FirstOrDefaultAsync(a => a.EmployeeId == emp.Id && a.AddressType == "Wochenaufenthalt")
                          ?? new EmployeeAddress { EmployeeId = emp.Id, AddressType = "Wochenaufenthalt", CreatedAt = DateTime.Now };
                    wa.Street = V("PersonTASWeeklyStreet"); wa.ZipCode = NummerOhneKomma(V("PersonTASWeeklyZIPCode")); wa.City = V("PersonTASWeeklyCity");
                    wa.BfsNumber = NummerOhneKomma(V("PersonTASWeeklyMunicipalityID")); wa.Country = LandCode(V("PersonTASWeeklyCountry")) ?? "CH";
                    wa.ValidFrom = f.Entry; wa.Description = "Swissdec-Testdaten"; wa.UpdatedAt = DateTime.Now;
                    if (wa.Id == 0) _db.EmployeeAddresses.Add(wa);
                    await _db.SaveChangesAsync();
                }
            }

            // ── Familie: Kinder + Partner ──
            var kinder = new List<string>();
            for (int i = 1; i <= 3; i++)
            {
                var kv = V($"PersonChild{i}Firstname"); if (kv == null) continue;
                var kGeb = Datum(V($"PersonChild{i}DateOfBirth"));
                var von = Datum(V($"PersonChild{i}StartTAS")); var bis = Datum(V($"PersonChild{i}EndTAS"));
                kinder.Add($"{kv} {V($"PersonChild{i}Lastname")} ({kGeb:dd.MM.yyyy}), QST-Abzug {von:dd.MM.yyyy}–{bis:dd.MM.yyyy}");
                if (vorschau) continue;
                var kind = await _db.EmployeeFamilyMembers.FirstOrDefaultAsync(m => m.EmployeeId == emp.Id && m.MemberType == "Kind" && m.FirstName == kv)
                        ?? new EmployeeFamilyMember { EmployeeId = emp.Id, MemberType = "Kind", CreatedAt = DateTime.Now };
                kind.FirstName = kv; kind.LastName = V($"PersonChild{i}Lastname") ?? emp.LastName;
                kind.DateOfBirth = kGeb?.ToDateTime(TimeOnly.MinValue); kind.LebtImHaushalt = true; kind.LivesInSwitzerland = land == "CH";
                kind.QstDeductibleFrom = von?.ToDateTime(TimeOnly.MinValue); kind.QstDeductibleUntil = bis?.ToDateTime(TimeOnly.MinValue);
                kind.UpdatedAt = DateTime.Now;
                if (kind.Id == 0) _db.EmployeeFamilyMembers.Add(kind);
                await _db.SaveChangesAsync();
            }
            if (kinder.Count > 0) felder["Kinder"] = string.Join(" · ", kinder);
            if (V("PersonPartnerLastname") != null)
            {
                var pLand = LandCode(V("PersonPartnerCountry"));
                felder["Partner"] = $"{V("PersonPartnerFirstname")} {V("PersonPartnerLastname")} ({Datum(V("PersonPartnerDateOfBirth")):dd.MM.yyyy}) · {V("PersonPartnerStreet")}, {NummerOhneKomma(V("PersonPartnerZIPCode"))} {V("PersonPartnerCity")} {pLand} {V("PersonPartnerResidenceCantonCH")}";
                if (!vorschau)
                {
                    var p = await _db.EmployeeFamilyMembers.FirstOrDefaultAsync(m => m.EmployeeId == emp.Id && m.MemberType == "Ehepartner")
                         ?? new EmployeeFamilyMember { EmployeeId = emp.Id, MemberType = "Ehepartner", CreatedAt = DateTime.Now };
                    p.FirstName = V("PersonPartnerFirstname"); p.LastName = V("PersonPartnerLastname");
                    p.DateOfBirth = Datum(V("PersonPartnerDateOfBirth"))?.ToDateTime(TimeOnly.MinValue);
                    p.SocialSecurityNumber = V("PersonPartnerSVASNumber") == "unknown" ? null : V("PersonPartnerSVASNumber");
                    p.LivesInSwitzerland = pLand == "CH" || V("PersonPartnerResidenceCantonCH") != null;
                    p.Gender = sex == "female" ? "male" : sex == "male" ? "female" : null;
                    p.UpdatedAt = DateTime.Now;
                    if (p.Id == 0) _db.EmployeeFamilyMembers.Add(p);
                    await _db.SaveChangesAsync();
                }
                if (V("PersonPartnerStreet") != null) probleme.Add("Partner-Adresse (getrennter Wohnsitz) steht in den Testdaten — im Familie-Tab als alternative Adresse nachtragen, falls ein Testfall sie braucht.");
            }

            // ── Versicherungs-Codes ──
            var codes = new List<(string Art, string Code)>();
            if (V("PersonUVGLAACode") is { } uvg) codes.Add(("UVG", uvg));
            foreach (var n in new[] { "1", "2" })
            {
                if (V($"PersonUVGZLAACCode{n}") is { } z) codes.Add(("UVGZ", NummerOhneKomma(z)!));
                if (V($"PersonKTGAMCCode{n}") is { } k) codes.Add(("KTG", NummerOhneKomma(k)!));
            }
            var bvgVersichert = V("PersonBVGLPPInsured") == "1.0" || V("PersonBVGLPPInsured") == "1";
            if (bvgVersichert && V("PersonBVGLPPCode1") is { } b) codes.Add(("BVG", NummerOhneKomma(b)!));
            var codeTexte = new List<string>();
            foreach (var (art, code) in codes)
            {
                var svCodes = EmployeeVersicherungCode.SvCodesFuer(art);
                var bekannt = svLoesungen.Any(s => svCodes.Contains(s.Code) && string.Equals(s.LoesungsCode, code, StringComparison.OrdinalIgnoreCase));
                codeTexte.Add($"{art} {code}{(bekannt ? "" : " (keine Satz-Zeile → kein Abzug)")}");
            }
            if (bvgVersichert) codeTexte.Add($"BVG: {V("PersonBVGLPPEntryReason")} · {V("PersonBVGLPPFullyFitForWorkEntry")}" + (Dez(V("PersonBVGLPPManuallyBase")) is { } mb ? $" · Basis manuell {mb:0}" : ""));
            else codeTexte.Add("BVG: nicht versichert");
            felder["Versicherungen"] = string.Join(" · ", codeTexte);
            if (!vorschau)
            {
                var alt = await _db.EmployeeVersicherungCodes.Where(v => v.EmployeeId == emp.Id && v.ValidFrom == f.Entry).ToListAsync();
                _db.EmployeeVersicherungCodes.RemoveRange(alt);
                foreach (var (art, code) in codes)
                {
                    var e = new EmployeeVersicherungCode { EmployeeId = emp.Id, Art = art, Code = code.ToUpperInvariant(), ValidFrom = f.Entry, Bemerkung = "Swissdec-Testdaten", CreatedAt = DateTime.UtcNow };
                    if (art == "BVG")
                    {
                        e.BvgEintrittsgrund = V("PersonBVGLPPEntryReason");
                        e.BvgVollArbeitsfaehig = V("PersonBVGLPPFullyFitForWorkEntry") == null ? null : V("PersonBVGLPPFullyFitForWorkEntry") == "FullyFitForWork";
                        e.BvgBasisManuell = Dez(V("PersonBVGLPPManuallyBase"));
                    }
                    _db.EmployeeVersicherungCodes.Add(e);
                }
                await _db.SaveChangesAsync();
            }
            if (V("PersonAHVALVSpecialCase") != null) probleme.Add("AHV/ALV-Sonderfall markiert (Testdaten «x») — im Programm noch kein Feld; klären wir am Testfall.");

            // ── Quellensteuer ──
            if (V("PersonTASCanton") is { } tasKt)
            {
                var tasCode = V("PersonTASCode") ?? "";
                var tasAb = Datum(V("PersonTASCodeValidAsOf")) ?? f.Entry;
                var modellQ = V("PersonTASCalculationModel");
                var m = Regex.Match(tasCode, @"^([A-Z]{1,2})(\d)([YN])$");
                felder["Quellensteuer"] = $"Kanton {tasKt} · Code {tasCode} ab {tasAb:dd.MM.yyyy} · Modell {(modellQ == "Y" ? "Jahr" : "Monat")}"
                    + (V("PersonTASMunicipalityID") != null ? $" · Gemeinde {NummerOhneKomma(V("PersonTASMunicipalityID"))}" : "")
                    + (V("PersonOtherActivity") != null ? $" · Nebenbeschäftigung {Dez(V("PersonTotalOtherActivityRate"))?.ToString("0.#") ?? "?"} %" : "")
                    + (V("PersonTASKindOfResidence") is { } kor ? $" · {kor}" : "")
                    + (V("PersonCrossborderTaxID") != null ? $" · Grenzgänger Steuer-ID {V("PersonCrossborderTaxID")}, {V("PersonCrossborderPlaceOfBirth")}, ab {Datum(V("PersonCrossborderValidAsOf")):dd.MM.yyyy}" : "");
                if (modellQ == "Y") probleme.Add("QST-Jahresmodell (TI/VD) — Berechnung folgt in Schritt 5.");
                if (!vorschau)
                {
                    var q = await _db.EmployeeQuellensteuer.FirstOrDefaultAsync(x => x.EmployeeId == emp.Id && x.ValidFrom == tasAb)
                         ?? new EmployeeQuellensteuer { EmployeeId = emp.Id, ValidFrom = tasAb, CreatedAt = DateTime.Now };
                    q.Steuerkanton = tasKt; q.QstCode = tasCode;
                    q.TarifCode = m.Success ? m.Groups[1].Value : null;
                    q.AnzahlKinder = m.Success ? int.Parse(m.Groups[2].Value) : 0;
                    q.Kirchensteuer = m.Success && m.Groups[3].Value == "Y";
                    q.TarifvorschlagQst = false;
                    q.QstGemeindeBfsNr = int.TryParse(NummerOhneKomma(V("PersonTASMunicipalityID")), out var g) ? g : null;
                    q.ArbeitsortKanton = wp;
                    q.WeitereBeschaftigungen = V("PersonOtherActivity") != null;
                    q.GesamtpensumWeitereAg = Dez(V("PersonTotalOtherActivityRate"));
                    q.Halbfamilie = V("PersonSingleParentFamily") != null ? "ja" : null;
                    q.IsGrenzgaenger = land != "CH";
                    q.Wohnsitzstaat = land != "CH" ? land : null;
                    q.WohnsitzAusland = land != "CH" ? land : null;
                    q.IsWochenaufenthalter = V("PersonTASWeeklyCity") != null || V("PersonTASKindOfResidence") == "Weekly";
                    q.GrenzgaengerSteuerId = V("PersonCrossborderTaxID");
                    q.GrenzgaengerGeburtsort = V("PersonCrossborderPlaceOfBirth");
                    q.GrenzgaengerAb = Datum(V("PersonCrossborderValidAsOf"));
                    q.HerleitungJson = JsonSerializer.Serialize(new { quelle = "Swissdec-Testdaten", modell = modellQ, kindOfResidence = V("PersonTASKindOfResidence") });
                    q.UpdatedAt = DateTime.Now;
                    if (q.Id == 0) _db.EmployeeQuellensteuer.Add(q);
                    await _db.SaveChangesAsync();
                }
            }

            // Lohnausweis-/Statistik-Angaben nur melden (kommen mit Lohnausweis/Statistik)
            var sonst = new[] { "PersonTAXCanteenLunchCheck", "PersonTAXFreeTransport", "PersonTAXRelocationCosts", "PersonTAXStaffShareMarketValueDate", "PersonTAXExpatriateRulingDate", "PersonTAXChildAllowancePerAHVAVS", "PersonTAXCompanyCarClarify", "PersonTAXBlockedOptions", "PersonTAXUnquotedOptions" }
                .Where(k => V(k) != null).Select(k => k.Replace("PersonTAX", "")).ToList();
            if (sonst.Count > 0) probleme.Add("Lohnausweis-Angaben in den Testdaten: " + string.Join(", ", sonst) + " — Lohnausweis-Modul (später).");
            if (V("PersonLeaveEntitlement") is { } fer) felder["Ferien"] = $"{Dez(fer):0} Tage/Jahr (Testdaten; OneCrew rechnet Ferienwochen über Filiale/Alter)";

            if (probleme.Count > 0) felder["⚠ Hinweise"] = string.Join(" | ", probleme);
            aktionen.Add(new Aktion(neu ? "anlegen" : "aktualisieren", "Person", $"{f.Id} · Nr. {persNr}", felder));
        }

        hinweise.Add("Monatslohn-Beträge stehen nicht in der Stammzeile (kommen als Lohnart 1000 pro Monat) — der FIX-Vertrag wird ohne Lohnbetrag angelegt; Schritt 5 liefert die Löhne aus wagetypes_export.csv.");
        hinweise.Add("Ferienprozente der Stundenlöhner (8.33 %) und Ferientage nach Alter (20/25/30) der Muster AG weichen von den L-GAV-Vorgaben ab — Filial-Einstellung folgt vor Schritt 5.");
        hinweise.Add("Personalnummern = Swissdec-Nummern 1–44 (kein Nummernkreis der Filiale).");
        if (eduFehlend.Count > 0)
            hinweise.Add("Ausbildung: Swissdec-Stufen (" + string.Join(", ", eduFehlend) + ") passen nicht auf die Ausbildungscodes im Katalog (" + string.Join(", ", eduCodes) + ") — bleibt leer; Zuordnung klären wir bei der Statistik (E7).");
        if (!vorschau) _log.LogInformation("Swissdec-Testmandant Schritt 4a angelegt: {N} Personen", aktionen.Count);
        return Ok(new SchrittErgebnis("4a · Personen + Verträge", vorschau, aktionen, hinweise));
    }

    // ── Hilfen ──────────────────────────────────────────────────────────
    private static List<Testfall> LeseTestfaelle(string pfad)
    {
        var res = new List<Testfall>();
        foreach (var ln in System.IO.File.ReadAllLines(pfad).Skip(1))
        {
            var m = Regex.Match(ln, "^([^,]+),([^,]*),\"*(\\{.*\\})\"*$");
            if (!m.Success) continue;
            var js = m.Groups[3].Value.Replace("\"\"", "\"");
            Dictionary<string, string> w;
            try
            {
                using var doc = JsonDocument.Parse(js);
                w = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString());
            }
            catch { continue; }
            var name = m.Groups[1].Value.Trim();
            var id = name.Split(' ')[0];
            var entry = DateOnly.TryParse(m.Groups[2].Value, out var d) ? d : (Datum(w.GetValueOrDefault("PersonEntryDate")) ?? DateOnly.MinValue);
            res.Add(new Testfall(id, name, entry, w));
        }
        return res;
    }

    /// <summary>«1.0» → «1», «6020.0» → «6020», «22.0» → «22».</summary>
    private static string? NummerOhneKomma(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return Regex.IsMatch(s, @"^\d+\.0+$") ? s[..s.IndexOf('.')] : s;
    }

    private static decimal? Dez(string? s) => decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>dd.MM.yyyy, yyyy-MM-dd oder Excel-Seriennummer («48852.0»).</summary>
    private static DateOnly? Datum(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || s == "None") return null;
        s = s.Trim();
        if (DateOnly.TryParseExact(s, new[] { "dd.MM.yyyy", "yyyy-MM-dd", "d.M.yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var serial) && serial > 20000 && serial < 80000)
            return DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays((double)serial));
        return null;
    }

    private static string? LandCode(string? s) => s?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "SWITZERLAND" or "CH" or "SCHWEIZ" => "CH",
        "ITALY" or "IT" => "IT",
        "GERMANY" or "DE" => "DE",
        "FRANCE" or "FR" => "FR",
        "AUSTRIA" or "AT" => "AT",
        var x => x.Length == 2 ? x : x[..2],
    };

    private static string? MapZivilstand(string? s) => s switch
    {
        "single" => "ledig", "married" => "verheiratet", "divorced" => "geschieden", "separated" => "getrennt",
        "widowed" => "verwitwet", "registeredPartnership" => "eingetragene_partnerschaft",
        "partnershipDissolvedByLaw" or "partnershipDissolvedByDeath" or "partnershipDissolvedByDeclarationOfLost" => "aufgeloeste_partnerschaft",
        _ => null,
    };

    private static string? MapKonfession(string? s) => s switch
    {
        "romanCatholic" => "roemisch_katholisch", "reformedEvangelical" => "evangelisch_reformiert",
        "christianCatholic" => "christ_katholisch", "jewishCommunity" => "israelitisch", "otherOrNone" => "keine", _ => null,
    };

    private static string? MapPermit(string? s, out string? hinweis)
    {
        hinweis = null;
        switch (s)
        {
            case null: return null;
            case "settled-C": return "C";
            case "annual-B": return "B";
            case "crossBorder-G": return "G";
            case "shortTerm-L": return "L";
            case "ProvisionallyAdmittedForeigners-F": return "F";
            case "asylumSeeker-N": return "N";
            case "peopleInNeedOfProtection-S": return "S";
            case "NotificationProcedureForShorttermWork90Days": hinweis = "Meldeverfahren 90 Tage — keine Bewilligungsart im Katalog; bleibt leer."; return null;
            case "NotificationProcedureForShorttermWork120Days": hinweis = "Meldeverfahren 120 Tage — keine Bewilligungsart im Katalog; bleibt leer."; return null;
            case "othersNotSwiss": hinweis = "Aufenthaltskategorie «othersNotSwiss» (z.B. Grenzgänger ohne G) — Bewilligung bleibt leer."; return null;
            default: hinweis = $"Aufenthaltskategorie «{s}» unbekannt."; return null;
        }
    }

    private static string? MapEducation(string? s) => s switch
    {
        "mandatorySchoolOnly" => "obligatorische_schule", "enterpriseEducation" => "anlehre",
        "vocEducationCompl" => "berufslehre", "universityEntranceCertificate" => "matura",
        "teacherCertificate" => "lehrerpatent", "higherVocEducation" => "hoehere_berufsbildung",
        "higherEducationBachelor" => "fachhochschule", "universityBachelor" => "universitaet",
        "universityMaster" => "universitaet", "doctorate" => "universitaet", _ => null,
    };
}
