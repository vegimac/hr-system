using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// GastroSocial «Anmeldung für Familien-/Geburtszulagen» (FOR 0402-dt) —
/// Walter 11.10.2026, Muster wie die AHV-Anmeldung: GET liefert die
/// Vorbefüllung aus MA, Familie und Filiale, das Frontend zeigt alles
/// editierbar, POST erzeugt das PDF. Es wird nichts gespeichert.
/// Konzept: docs/famz-anmeldung-konzept.md.
/// </summary>
[Authorize(Roles = "admin,superuser,user")]
[ApiController]
[Route("api/famz-anmeldung")]
public class FamzAnmeldungController : ControllerBase
{
    public const decimal MindestEinkommenJahr = 7560m;

    private readonly AppDbContext _db;
    private readonly FamzAnmeldungPdfService _pdf;
    public FamzAnmeldungController(AppDbContext db, FamzAnmeldungPdfService pdf)
    {
        _db = db; _pdf = pdf;
    }

    static string? Dt(DateTime? d) => d?.ToString("dd.MM.yyyy");
    static string? Dt(DateOnly? d) => d?.ToString("dd.MM.yyyy");
    static string? Join(string sep, params string?[] teile)
    {
        var s = string.Join(sep, teile.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));
        return s.Length == 0 ? null : s;
    }

    /// <summary>Vorbefüllung. <paramref name="kinder"/> = Komma-Liste der Kind-Ids (leer = Standardauswahl).</summary>
    [HttpGet("{empId:int}/prefill")]
    public async Task<IActionResult> Prefill(int empId, [FromQuery] string? kinder = null)
    {
        var e = await _db.Employees.AsNoTracking()
            .Include(x => x.NationalityRef)
            .Include(x => x.PermitType)
            .FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });
        var heute = DateOnly.FromDateTime(DateTime.Today);

        var familie = await _db.EmployeeFamilyMembers.AsNoTracking()
            .Include(m => m.NationalityRef)
            .Include(m => m.PermitType)
            .Where(m => m.EmployeeId == empId)
            .ToListAsync();
        var adrIds = familie.Where(m => m.AlternativeAddressId.HasValue).Select(m => m.AlternativeAddressId!.Value).ToList();
        var adressen = adrIds.Count == 0
            ? new Dictionary<int, EmployeeAddress>()
            : await _db.EmployeeAddresses.AsNoTracking().Where(a => adrIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        var natNamen = await _db.Nationalities.AsNoTracking()
            .Where(n => n.NameDe != null)
            .Select(n => new { n.Id, n.Code, n.NameDe })
            .ToListAsync();
        string? NatName(Nationality? n) => n == null ? null
            : natNamen.FirstOrDefault(x => x.Id == n.Id)?.NameDe ?? n.Code;
        bool IstCh(Nationality? n) => string.Equals(n?.Code, "CH", StringComparison.OrdinalIgnoreCase);

        // ── Kinder: alle bis 25, nicht verstorben ────────────────────────
        var alleKinder = familie
            .Where(m => m.MemberType == "Kind" && m.DateOfDeath == null
                     && (m.DateOfBirth == null || DateOnly.FromDateTime(m.DateOfBirth.Value).AddYears(25) >= heute))
            .OrderBy(m => m.DateOfBirth ?? DateTime.MaxValue).ThenBy(m => m.Id)
            .ToList();
        var zulagenIds = await _db.FamilyMemberAllowances.AsNoTracking()
            .Where(a => a.ValidTo == null || a.ValidTo >= heute)
            .Select(a => a.FamilyMemberId).Distinct().ToListAsync();
        var gewaehlt = (kinder ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var i) ? i : 0)
            .Where(i => alleKinder.Any(k => k.Id == i))
            .ToList();
        if (gewaehlt.Count == 0) gewaehlt = alleKinder.Select(k => k.Id).ToList();
        var ausgewaehlt = alleKinder.Where(k => gewaehlt.Contains(k.Id)).Take(FamzAnmeldungPdfService.MaxKinder).ToList();

        // ── Anstellung + Filiale ─────────────────────────────────────────
        var vertraege = await _db.Employments.AsNoTracking()
            .Where(x => x.EmployeeId == empId && x.CompanyProfileId != null)
            .OrderBy(x => x.ContractStartDate)
            .ToListAsync();
        var tag = heute.ToDateTime(TimeOnly.MinValue);
        var aktuell = vertraege.LastOrDefault(x => x.ContractStartDate <= tag && (x.ContractEndDate == null || x.ContractEndDate >= tag))
                   ?? vertraege.FirstOrDefault(x => x.ContractStartDate > tag)
                   ?? vertraege.LastOrDefault();
        var cp = aktuell?.CompanyProfileId is int cpId
            ? await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cpId)
            : null;
        var (beschVon, beschBis) = aktuell == null ? (null, null) : KetteInFiliale(vertraege, aktuell);

        string? abrechnungsNr = null;
        if (cp != null)
        {
            var kandidaten = await _db.CompanyProfileEmpfaengers.AsNoTracking()
                .Where(z => z.CompanyProfileId == cp.Id && z.IsActive
                         && (z.Empfaenger!.Art == "FAK" || z.Empfaenger!.Art == "AUSGLEICHSKASSE")
                         && (z.GueltigAb == null || z.GueltigAb <= heute)
                         && (z.GueltigBis == null || z.GueltigBis >= heute))
                .Select(z => new { z.Empfaenger!.Art, z.Mitgliednummer, z.GueltigAb })
                .ToListAsync();
            abrechnungsNr = kandidaten.Where(k => !string.IsNullOrWhiteSpace(k.Mitgliednummer))
                .OrderBy(k => k.Art == "FAK" ? 0 : 1).ThenByDescending(k => k.GueltigAb)
                .Select(k => k.Mitgliednummer).FirstOrDefault();
        }

        var bvg = await new BvgPflichtService(_db).VorschlagAsync(empId, heute);
        decimal? jahreslohn = bvg?.Jahreslohn;
        string? bruttolohn = aktuell == null ? null : LohnText(aktuell, jahreslohn);

        var weitere = await _db.WeitereArbeitgeber.AsNoTracking()
            .Where(w => w.EmployeeId == empId
                     && (w.GueltigVon == null || w.GueltigVon <= heute)
                     && (w.GueltigBis == null || w.GueltigBis >= heute))
            .OrderByDescending(w => w.IstHauptarbeitgeber).ThenBy(w => w.Id)
            .ToListAsync();

        var leistung = await LeistungAsync(empId, heute);

        // ── Zivilstand MA ────────────────────────────────────────────────
        var zivil = (e.MaritalStatus ?? "").Trim().ToLowerInvariant();
        DateOnly? zivilSeit = zivil == "getrennt" ? e.SeparatedSince ?? e.MaritalStatusSince : e.MaritalStatusSince;
        if (zivilSeit == null && zivil.Length > 0)
            zivilSeit = await _db.EmployeeZivilstandHistories.AsNoTracking()
                .Where(h => h.EmployeeId == empId && h.Zivilstand == zivil && h.GueltigAb != null)
                .OrderByDescending(h => h.GueltigAb)
                .Select(h => h.GueltigAb).FirstOrDefaultAsync();

        string? maGeschlecht = NormalizeGender(e.Gender, e.Salutation);

        // ── Anderer Elternteil + Ehepartner ──────────────────────────────
        var hinweise = new List<string>();
        var aeIds = ausgewaehlt.Where(k => k.AndererElternteilId.HasValue).Select(k => k.AndererElternteilId!.Value).ToList();
        int? aeId = aeIds.GroupBy(i => i).OrderByDescending(g => g.Count()).Select(g => (int?)g.Key).FirstOrDefault();
        if (aeIds.Distinct().Count() > 1)
            hinweise.Add("Die gewählten Kinder haben verschiedene andere Elternteile — das Formular kennt nur einen. Pro Elternteil ein eigenes Formular erstellen.");
        if (ausgewaehlt.Any(k => k.AndererElternteilId == null && k.GemeinsamesKindMitPartner != false))
            hinweise.Add("Bei mindestens einem Kind ist der andere Elternteil nicht erfasst (Familie → Kind → «Anderer Elternteil»).");
        var ae = aeId is int a1 ? familie.FirstOrDefault(m => m.Id == a1) : null;
        var ehepartner = familie
            .Where(m => m.MemberType == "Ehepartner" && m.DateOfDeath == null)
            .OrderByDescending(m => m.Id).FirstOrDefault();
        if (ehepartner != null && ae != null && ehepartner.Id == ae.Id) ehepartner = null;

        FamzElternteilDto? Elternteil(EmployeeFamilyMember? m)
        {
            if (m == null) return null;
            EmployeeAddress? adr = m.AlternativeAddressId is int adrId ? adressen.GetValueOrDefault(adrId) : null;
            bool zuHause = m.LebtImHaushalt && adr == null;
            var zs = (m.FamilyStatus ?? "").Trim().ToLowerInvariant();
            if (zs.Length == 0 && m.MemberType == "Ehepartner") zs = zivil is "getrennt" ? "getrennt" : "verheiratet";
            return new FamzElternteilDto
            {
                FamilyMemberId = m.Id,
                NameVorname = Join(" ", m.LastName, m.FirstName),
                StrasseNr = zuHause ? e.Street : adr?.Street,
                PlzOrt = zuHause ? Join(" ", e.ZipCode, e.City) : Join(" ", adr?.ZipCode, adr?.City),
                Kanton = zuHause ? e.CantonCode : adr?.Canton,
                Geburtsdatum = Dt(m.DateOfBirth),
                AhvNummer = m.SocialSecurityNumber,
                Geschlecht = NormalizeGender(m.Gender, null),
                Zivilstand = zs.Length == 0 ? null : zs,
                ZivilstandSeit = m.MemberType == "Ehepartner" && zs == zivil ? Dt(zivilSeit) : null,
                Nationalitaet = NatName(m.NationalityRef),
                Auslaenderausweis = IstCh(m.NationalityRef) ? null : m.PermitType?.Code,
                Angestellt = m.Erwerbstaetig,
                ArbeitgeberZeile1 = m.Erwerbstaetig == true ? Join(", ", m.ArbeitgeberName, m.ArbeitgeberStrasse) : null,
                ArbeitgeberZeile2 = m.Erwerbstaetig == true ? Join(", ", Join(" ", m.ArbeitgeberPlz, m.ArbeitgeberOrt), m.ArbeitgeberTelefon) : null,
                ArbeitsortKanton = m.Erwerbstaetig == true ? m.ArbeitgeberKanton : null,
                Arbeitspensum = m.Erwerbstaetig == true && m.Arbeitspensum.HasValue ? $"{m.Arbeitspensum.Value:0.##}" : null,
                EinkommenHoeher = m.MaHatHoeheresEinkommen.HasValue ? !m.MaHatHoeheresEinkommen.Value : null,
                EinkommenUeberMinimum = m.Erwerbstaetig == false ? false : m.EinkommenUeberMinimum,
                SeNe = m.SeNeAngemeldet,
                Ausgleichskasse = m.SeNeAusgleichskasse,
                LeistungBezug = m.BeziehtLeistung,
                LeistungVon1 = m.LeistungVon,
            };
        }

        var aeDto = Elternteil(ae);
        var partnerDto = Elternteil(ehepartner);
        foreach (var p in new[] { aeDto, partnerDto })
            if (p?.EinkommenHoeher == true)
                hinweise.Add($"{p.NameVorname} verdient mehr als {e.FirstName} — in der Regel hat {p.NameVorname} den Erstanspruch auf die Kinderzulage. Die Kasse entscheidet; Anmeldung nur einreichen, wenn das geklärt ist.");
            else if (p != null && p.Angestellt == true && p.EinkommenHoeher == null)
                hinweise.Add($"Bei {p.NameVorname} fehlt «verdient mehr als der MA?» — das entscheidet, wer die Zulage erhält.");

        // ── Kinder-Zeilen + Sorge / wohnt bei ────────────────────────────
        var kinderDto = new List<FamzKindDto>();
        var sorgen = new List<(string Name, string? Wert)>();
        var wohnen = new List<(string Name, string? Wert)>();
        var beilagen = new List<string>();
        foreach (var k in ausgewaehlt)
        {
            EmployeeAddress? adr = k.AlternativeAddressId is int adrId ? adressen.GetValueOrDefault(adrId) : null;
            string? wohnadresse = k.LebtImHaushalt ? null
                : adr == null ? "(Adresse nicht erfasst)"
                : Join(", ", adr.Street, Join(" ", adr.ZipCode, adr.City), adr.Country);
            kinderDto.Add(new FamzKindDto
            {
                FamilyMemberId = k.Id,
                Name = k.LastName,
                Vorname = k.FirstName,
                Geburtsdatum = Dt(k.DateOfBirth),
                Geschlecht = NormalizeGender(k.Gender, null),
                Beziehung = k.BeziehungZumMa,
                Erwerbsunfaehig = k.Erwerbsunfaehig,
                Wohnadresse = wohnadresse,
            });
            sorgen.Add((k.FirstName ?? "", k.ElterlicheSorge));
            var kae = k.AndererElternteilId is int x ? familie.FirstOrDefault(m => m.Id == x) : null;
            wohnen.Add((k.FirstName ?? "", WohntBei(k, kae, maGeschlecht)));

            if (k.BeziehungZumMa == null)
                hinweise.Add($"{k.FirstName}: Beziehung zum Kind (leiblich, Stiefkind …) fehlt.");
            if (k.KeineUnterhaltspflicht)
                hinweise.Add($"{k.FirstName}: laut Verlauf heute kein Unterhalt zur Hauptsache — Anspruch mit der Kasse klären.");
            var alter = k.DateOfBirth.HasValue ? AlterAm(DateOnly.FromDateTime(k.DateOfBirth.Value), heute) : (int?)null;
            if (alter >= 15)
                beilagen.Add($"{k.FirstName}: aktuelle Ausbildungsbestätigung" + (k.Erwerbsunfaehig ? " bzw. Arztzeugnis (erwerbsunfähig)" : ""));
            if (adr != null && !IstSchweiz(adr.Country))
                beilagen.Add($"{k.FirstName}: Bestätigung des Amts für Kindergeld im Wohnstaat ({adr.Country})");
            if (!IstCh(k.NationalityRef) && k.NationalityRef != null)
                beilagen.Add($"{k.FirstName}: Ausländerausweis / Aufenthaltsbewilligung und Geburtsschein");
        }
        if (alleKinder.Count(k => gewaehlt.Contains(k.Id)) > FamzAnmeldungPdfService.MaxKinder)
            hinweise.Add($"Mehr als {FamzAnmeldungPdfService.MaxKinder} Kinder gewählt — das Formular hat nur {FamzAnmeldungPdfService.MaxKinder} Zeilen, für die übrigen ein zweites Formular erstellen.");

        var (sorge, sorgeBem) = Zusammenfassen(sorgen);
        var (lebenBei, lebenBeiBem) = Zusammenfassen(wohnen);

        // Beilagen MA / Elternteil
        if (IstCh(e.NationalityRef))
            beilagen.Insert(0, zivil == "verheiratet" ? "Familienbüchlein oder Eheschein + Geburtsscheine der Kinder" : "Geburtsscheine der Kinder");
        else
            beilagen.Insert(0, "Ausländerausweise der Eltern und Kinder, Eheschein, Geburtsscheine der Kinder");
        if (zivil == "ledig" && ausgewaehlt.Any(k => k.AndererElternteilId != null))
            beilagen.Add("Bestätigung gemeinsame elterliche Sorge / Vaterschaftsanerkennung");
        if (zivil is "geschieden" or "getrennt")
            beilagen.Add("Auszug aus dem Scheidungs- bzw. Trennungsurteil (Sorge-/Obhutsrecht)");
        if (leistung != null || aeDto?.LeistungBezug == true || partnerDto?.LeistungBezug == true)
            beilagen.Add("Bestätigung des Leistungserbringers (IV, ALV, UVG, KTG, MSE)");

        // Ab wann: frühestes «Kind geboren bzw. Stellenantritt» der gewählten Kinder
        DateOnly? abWann = null;
        foreach (var k in ausgewaehlt)
        {
            var geb = k.DateOfBirth.HasValue ? DateOnly.FromDateTime(k.DateOfBirth.Value) : (DateOnly?)null;
            var ab = geb.HasValue && beschVon.HasValue ? (geb > beschVon ? geb : beschVon) : geb ?? beschVon;
            if (ab.HasValue && (abWann == null || ab < abWann)) abWann = ab;
        }

        var firma = cp == null ? null : string.IsNullOrWhiteSpace(cp.BranchName) ? cp.CompanyName : $"{cp.CompanyName}, {cp.BranchName}";
        var dto = new FamzAnmeldungDto
        {
            Beantrage = "FAMILIENZULAGE",
            Status = "ARBEITNEHMER",
            Antragsteller = new FamzPersonDto
            {
                NameVorname = Join(" ", e.LastName, e.FirstName),
                StrasseNr = e.Street,
                PlzOrt = Join(" ", e.ZipCode, e.City),
                Kanton = e.CantonCode,
                Geburtsdatum = Dt(e.DateOfBirth),
                AhvNummer = e.SocialSecurityNumber,
                Geschlecht = maGeschlecht,
                Zivilstand = zivil.Length == 0 ? null : zivil,
                ZivilstandSeit = zivil is "ledig" ? null : Dt(zivilSeit),
                Nationalitaet = NatName(e.NationalityRef) ?? e.Nationality,
                Auslaenderausweis = IstCh(e.NationalityRef) ? null : e.PermitType?.Code,
            },
            Erreichbar = Join(", ", e.PhoneMobile ?? e.Phone2, e.Email),
            AbWann = Dt(abWann),
            LeistungBezug = leistung != null,
            LeistungVon = leistung,
            AgName = firma,
            AgAbrechnungsnummer = abrechnungsNr,
            AgStrasse = cp == null ? null : Join(" ", cp.Street, cp.HouseNumber),
            AgPlzOrt = cp == null ? null : Join(" ", cp.ZipCode, cp.City),
            BeschaeftigtVon = Dt(beschVon),
            BeschaeftigtBis = Dt(beschBis),
            Arbeitspensum = aktuell == null ? null
                : (aktuell.EmploymentModel ?? "").ToUpperInvariant() == "FLEX" ? "variabel (Stundenlohn)"
                : aktuell.EmploymentPercentage.HasValue ? $"{aktuell.EmploymentPercentage.Value:0.##}" : null,
            ArbeitsortKanton = cp?.KantonCode,
            Bruttolohn = bruttolohn,
            EinkommenUeberMinimum = jahreslohn.HasValue ? jahreslohn.Value > MindestEinkommenJahr : null,
            WeitereArbeitgeber = weitere.Count > 0,
            WeitereAg1 = weitere.Count > 0 ? WeitererAgText(weitere[0]) : null,
            WeitereAg2 = weitere.Count > 1 ? WeitererAgText(weitere[1]) : null,
            HoeheresEinkommenBei = weitere.Count == 0 ? null : weitere.FirstOrDefault(w => w.IstHauptarbeitgeber)?.Name ?? firma,
            AndererElternteil = aeDto,
            Partner = partnerDto,
            Kinder = kinderDto,
            Sorge = sorge,
            SorgeBemerkung = sorgeBem,
            LebenBei = lebenBei,
            LebenBeiBemerkung = lebenBeiBem,
            OrtDatumArbeitgeber = cp == null ? null : Join(", ", cp.City, DateTime.Today.ToString("dd.MM.yyyy")),
        };

        return Ok(new
        {
            employeeId = e.Id,
            employeeNumber = e.EmployeeNumber,
            name = Join(" ", e.FirstName, e.LastName),
            formular = dto,
            kinderAuswahl = alleKinder.Select(k => new
            {
                id = k.Id,
                name = Join(" ", k.FirstName, k.LastName),
                geburtsdatum = Dt(k.DateOfBirth),
                hatZulage = zulagenIds.Contains(k.Id),
                gewaehlt = ausgewaehlt.Any(a => a.Id == k.Id),
            }),
            hinweise,
            beilagen = beilagen.Distinct().ToList(),
        });
    }

    /// <summary>PDF aus den (ggf. editierten) Werten erzeugen.</summary>
    [HttpPost("{empId:int}/pdf")]
    public async Task<IActionResult> Pdf(int empId, [FromBody] FamzAnmeldungDto dto)
    {
        var e = await _db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == empId);
        if (e == null) return NotFound(new { error = "EMP_NOT_FOUND" });
        if ((dto.Kinder?.Count ?? 0) > FamzAnmeldungPdfService.MaxKinder)
            return BadRequest(new { error = "ZU_VIELE_KINDER", message = $"Das Formular hat Platz für {FamzAnmeldungPdfService.MaxKinder} Kinder." });

        byte[] bytes;
        try { bytes = _pdf.Generate(dto); }
        catch (FileNotFoundException ex)
        { return StatusCode(500, new { error = "TEMPLATE_MISSING", message = ex.Message }); }

        return File(bytes, "application/pdf", $"{e.EmployeeNumber}-Anmeldung-Familienzulagen.pdf");
    }

    // ── Helfer ───────────────────────────────────────────────────────────

    /// <summary>
    /// Beginn/Ende der lückenlosen Vertragskette in der Filiale des aktuellen
    /// Vertrags (jede Filiale ist ein eigener Arbeitgeber mit eigener Abrechnungsnummer).
    /// </summary>
    public static (DateOnly? Von, DateOnly? Bis) KetteInFiliale(IReadOnlyList<Employment> vertraege, Employment aktuell)
    {
        var filiale = vertraege.Where(v => v.CompanyProfileId == aktuell.CompanyProfileId)
            .OrderBy(v => v.ContractStartDate).ToList();
        int idx = filiale.FindIndex(v => v.Id == aktuell.Id);
        if (idx < 0) return (DateOnly.FromDateTime(aktuell.ContractStartDate), aktuell.ContractEndDate.HasValue ? DateOnly.FromDateTime(aktuell.ContractEndDate.Value) : null);
        var start = filiale[idx].ContractStartDate;
        for (int i = idx - 1; i >= 0; i--)
        {
            var ende = filiale[i].ContractEndDate;
            if (ende == null || ende.Value.Date.AddDays(1) >= start.Date)
                start = filiale[i].ContractStartDate < start ? filiale[i].ContractStartDate : start;
            else break;
        }
        DateTime? bis = filiale[idx].ContractEndDate;
        for (int i = idx + 1; i < filiale.Count && bis != null; i++)
        {
            if (filiale[i].ContractStartDate.Date <= bis.Value.Date.AddDays(1))
                bis = filiale[i].ContractEndDate == null ? null : (filiale[i].ContractEndDate > bis ? filiale[i].ContractEndDate : bis);
            else break;
        }
        return (DateOnly.FromDateTime(start), bis.HasValue ? DateOnly.FromDateTime(bis.Value) : null);
    }

    static readonly System.Globalization.CultureInfo Ch = new("de-CH");
    static string Chf(decimal b, string format = "#,##0.00") => "CHF " + b.ToString(format, Ch);

    public static string LohnText(Employment v, decimal? jahreslohn)
    {
        var modell = (v.EmploymentModel ?? "").ToUpperInvariant();
        string grund = modell switch
        {
            "FIX" or "FIX-M" when v.MonthlySalary.HasValue => Chf(v.MonthlySalary.Value) + "/Mt.",
            "MTP" when v.HourlyRate.HasValue => Chf(v.HourlyRate.Value) + $"/Std., garantiert {(v.GuaranteedHoursPerWeek ?? 0).ToString("0.##", Ch)} Std./Wo.",
            _ when v.HourlyRate.HasValue => Chf(v.HourlyRate.Value) + "/Std.",
            _ when v.MonthlySalary.HasValue => Chf(v.MonthlySalary.Value) + "/Mt.",
            _ => "",
        };
        if (jahreslohn.HasValue && modell is not ("FIX" or "FIX-M"))
            grund = Join("; ", grund, "ca. " + Chf(jahreslohn.Value, "#,##0") + "/Jahr") ?? "";
        return grund;
    }

    async Task<string?> LeistungAsync(int empId, DateOnly heute)
    {
        var abs = await _db.Absences.AsNoTracking()
            .Where(a => a.EmployeeId == empId && a.DateFrom <= heute && a.DateTo >= heute)
            .Select(a => new { a.AbsenceType, a.DateFrom })
            .ToListAsync();
        foreach (var a in abs.OrderBy(a => a.DateFrom))
        {
            var typ = (a.AbsenceType ?? "").ToUpperInvariant();
            if (typ == "UNFALL") return $"UVG (Unfall seit {a.DateFrom:dd.MM.yyyy})";
            if (typ == "KRANK") return $"KTG (Krankheit seit {a.DateFrom:dd.MM.yyyy})";
            if (typ.StartsWith("MUTTER")) return $"MSE (Mutterschaft seit {a.DateFrom:dd.MM.yyyy})";
        }
        return null;
    }

    static string WeitererAgText(WeitererArbeitgeber w)
        => Join(", ", w.Name, w.Strasse, Join(" ", w.Plz, w.Ort), w.PensumProzent.HasValue ? $"{w.PensumProzent.Value:0.##} %" : null) ?? w.Name;

    /// <summary>
    /// Bei wem lebt das Kind? Im Haushalt des MA und der andere Elternteil auch → Eltern;
    /// nur beim MA → MA; nicht beim MA → beim anderen Elternteil (falls bekannt).
    /// </summary>
    public static string? WohntBei(EmployeeFamilyMember kind, EmployeeFamilyMember? andererElternteil, string? maGeschlecht)
    {
        string? Rolle(string? g) => g == "F" ? "MUTTER" : g == "M" ? "VATER" : null;
        if (kind.LebtImHaushalt)
            return andererElternteil?.LebtImHaushalt == true && andererElternteil.AlternativeAddressId == null
                ? "ELTERN" : Rolle(maGeschlecht);
        if (andererElternteil == null) return null;
        return Rolle(NormalizeGender(andererElternteil.Gender, null));
    }

    /// <summary>Alle gleich → dieser Wert; sonst kein Kreuz und eine Bemerkung pro Kind.</summary>
    public static (string? Wert, string? Bemerkung) Zusammenfassen(IReadOnlyList<(string Name, string? Wert)> werte)
    {
        var verschieden = werte.Select(w => w.Wert).Distinct().ToList();
        if (verschieden.Count == 1) return (verschieden[0], null);
        if (werte.Count == 0) return (null, null);
        string Text(string? w) => w switch { "ELTERN" => "Eltern", "MUTTER" => "Mutter", "VATER" => "Vater", _ => "offen" };
        return (null, string.Join(", ", werte.Select(w => $"{w.Name}: {Text(w.Wert)}")));
    }

    static int AlterAm(DateOnly geburt, DateOnly tag)
    {
        int a = tag.Year - geburt.Year;
        if (geburt.AddYears(a) > tag) a--;
        return a;
    }

    static bool IstSchweiz(string? land)
        => string.IsNullOrWhiteSpace(land) || land.Trim().ToUpperInvariant() is "CH" or "SCHWEIZ" or "SUISSE" or "SVIZZERA" or "SWITZERLAND";

    public static string? NormalizeGender(string? gender, string? salutation)
    {
        var g = (gender ?? "").Trim().ToLowerInvariant();
        if (g is "f" or "w" or "female" or "frau" or "weiblich") return "F";
        if (g is "m" or "male" or "mann" or "herr" or "männlich") return "M";
        var s = (salutation ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("frau")) return "F";
        if (s.StartsWith("herr")) return "M";
        return null;
    }
}
