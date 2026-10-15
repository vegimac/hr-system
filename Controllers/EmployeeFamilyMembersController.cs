using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using HrSystem.Services.Personaldaten;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

[ApiController]
[Route("api/employees/{employeeId:int}/family")]
public class EmployeeFamilyMembersController : ControllerBase
{
    private readonly AppDbContext _context;

    public EmployeeFamilyMembersController(AppDbContext context)
    {
        _context = context;
    }

    static DateOnly? AsDate(DateTime? dt) => dt.HasValue ? DateOnly.FromDateTime(dt.Value) : null;

    // GET /api/employees/{employeeId}/family
    [HttpGet]
    public async Task<IActionResult> GetByEmployee(int employeeId)
    {
        // Walter-Vorgabe 07.06.2026: PermitType (Code + Description) mit-laden,
        // damit das Frontend beim Ehepartner-Block den vollen Bewilligungs-Text
        // anzeigen kann statt „Typ 7".
        var members = await _context.EmployeeFamilyMembers
            .Include(m => m.PermitType)
            .Include(m => m.NationalityRef)
            .Where(m => m.EmployeeId == employeeId)
            .OrderBy(m => m.MemberType)
            .ThenBy(m => m.DateOfBirth)
            .ToListAsync();

        // Adress-IDs einsammeln und in einem Rutsch laden — damit das Frontend
        // die abweichende Adresse als Badge / im Modal anzeigen kann.
        var altIds = members.Where(m => m.AlternativeAddressId.HasValue)
                            .Select(m => m.AlternativeAddressId!.Value)
                            .Distinct()
                            .ToList();
        var altAddrs = altIds.Count == 0
            ? new Dictionary<int, EmployeeAddress>()
            : await _context.EmployeeAddresses
                .Where(a => altIds.Contains(a.Id))
                .ToDictionaryAsync(a => a.Id);

        var qst = await QstAnzeigeAsync(members);
        return Ok(members.Select(m => ProjectMember(m, altAddrs.GetValueOrDefault(m.AlternativeAddressId ?? 0),
            qst.GetValueOrDefault(m.Id))));
    }

    /// <summary>
    /// QST-Anzeige pro Kind (Walter 11.10.2026, Konzept QST-Verlauf): Monate,
    /// in denen das Kind zählt (Folgemonatsregel), offene Punkte im laufenden
    /// Monat und Hinweise (Ausbildungszulage ohne QST-Ausbildung, Adresse ≠ Haushalt).
    /// </summary>
    async Task<Dictionary<int, object>> QstAnzeigeAsync(IReadOnlyCollection<EmployeeFamilyMember> members)
    {
        var kinder = members.Where(m => m.MemberType == "Kind" && m.DateOfDeath == null).ToList();
        var result = new Dictionary<int, object>();
        if (kinder.Count == 0) return result;
        var verlauf = await KindQstVerlaufLader.LadeAsync(_context, kinder);
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var ids = kinder.Select(k => k.Id).ToList();
        var azAktiv = await _context.FamilyMemberAllowances.AsNoTracking()
            .Where(a => ids.Contains(a.FamilyMemberId) && a.AllowanceType == "AZ"
                     && a.ValidFrom <= heute && (a.ValidTo == null || a.ValidTo >= heute))
            .Select(a => a.FamilyMemberId).Distinct().ToListAsync();
        var ausbIds = await _context.FamilyMemberAusbildungen.AsNoTracking()
            .Where(a => ids.Contains(a.FamilyMemberId))
            .Select(a => a.FamilyMemberId).Distinct().ToListAsync();
        var zulageAktiv = await _context.FamilyMemberAllowances.AsNoTracking()
            .Where(a => ids.Contains(a.FamilyMemberId)
                     && a.ValidFrom <= heute && (a.ValidTo == null || a.ValidTo >= heute))
            .Select(a => a.FamilyMemberId).Distinct().ToListAsync();
        var elternIds = kinder.Where(k => k.AndererElternteilId.HasValue).Select(k => k.AndererElternteilId!.Value).Distinct().ToList();
        var eltern = elternIds.Count == 0
            ? new Dictionary<int, EmployeeFamilyMember>()
            : await _context.EmployeeFamilyMembers.AsNoTracking()
                .Where(m => elternIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
        foreach (var k in kinder)
        {
            var d = verlauf[k.Id];
            var monat = KindQstVerlauf.AmStichtag(d, heute);
            var hinweise = new List<string>();
            if (azAktiv.Contains(k.Id) && !ausbIds.Contains(k.Id))
                hinweise.Add("Ausbildungszulage läuft, aber keine Ausbildung für die QST erfasst");
            if (monat.ImHaushalt == true && !k.LebtImHaushalt)
                hinweise.Add("Adresse: nicht im Haushalt — QST-Haushalt noch «ja», Verhältnis prüfen");
            if (zulageAktiv.Contains(k.Id) && k.KeineUnterhaltspflicht)
                hinweise.Add("Unterhalt nein — Familienzulage läuft: Verfügung der Familienausgleichskasse prüfen");
            if (k.AndererElternteilId is int aeId && eltern.TryGetValue(aeId, out var ae)
                && ae.Erwerbstaetig == true && ae.MaHatHoeheresEinkommen == false)
                hinweise.Add($"{ae.FirstName} verdient mehr — in der Regel bezieht {ae.FirstName} die Kinderzulage (Erstanspruch)"
                    + (zulageAktiv.Contains(k.Id) ? ", Verfügung prüfen" : ""));
            result[k.Id] = new
            {
                wirkung = KindQstVerlauf.Wirkung(d, heute).Select(w => new
                {
                    von = w.VonMonat.ToString("yyyy-MM-dd"),
                    bis = w.BisMonat?.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"),
                    grund = w.Grund,
                }),
                zaehltHeute = monat.Zaehlt,
                grundHeute = monat.Grund,
                offen = monat.Offen,
                hinweise,
            };
        }
        return result;
    }

    // GET /api/employees/{employeeId}/family/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int employeeId, int id)
    {
        var member = await _context.EmployeeFamilyMembers
            .Include(m => m.PermitType)
            .Include(m => m.NationalityRef)
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);

        if (member == null) return NotFound();
        EmployeeAddress? alt = null;
        if (member.AlternativeAddressId.HasValue)
            alt = await _context.EmployeeAddresses.FirstOrDefaultAsync(a => a.Id == member.AlternativeAddressId.Value);
        var qst = await QstAnzeigeAsync(new[] { member });
        return Ok(ProjectMember(member, alt, qst.GetValueOrDefault(member.Id)));
    }

    /// <summary>
    /// Projektion: Member-Felder (alle) + zusätzlich altAddress mit den
    /// wichtigsten Adress-Feldern für die Anzeige im Frontend.
    /// </summary>
    private static object ProjectMember(EmployeeFamilyMember m, EmployeeAddress? alt,
        object? qstVerlauf = null) => new
    {
        m.Id,
        m.EmployeeId,
        m.MemberType,
        m.Gender,
        m.FamilyStatus,
        m.LastName,
        m.MaidenName,
        m.FirstName,
        m.SocialSecurityNumber,
        m.Phone,
        m.LivesInSwitzerland,
        m.DateOfBirth,
        m.DateOfDeath,
        m.Allowance1Until,
        m.Allowance2Until,
        m.Allowance3Until,
        m.AlternativeAddressId,
        // Walter-Vorgabe 25.08.2026: expliziter Haushalt-Status (3 Fälle).
        m.LebtImHaushalt,
        // Kind: «Erfahren am» steuert nur noch die Pendenz «QST-Korrektur
        // prüfen» (Konzept QST-Verlauf 11.10.2026), der Zeitraum steht im Verlauf.
        m.ErfahrenAm,
        m.PermitTypeId,
        // Walter-Vorgabe 07.06.2026: PermitType-Klartext mitliefern.
        permitType = m.PermitType == null ? null : new {
            id          = m.PermitType.Id,
            code        = m.PermitType.Code,
            description = m.PermitType.Description
        },
        m.PermitExpiryDate,
        m.ZemisNumber,
        m.NationalityId,
        // Walter-Vorgabe 07.06.2026: NationalityCode mitliefern, damit das
        // Frontend „CH-Bürger" statt „ohne Bewilligung" anzeigen kann.
        nationalityCode = m.NationalityRef?.Code,
        // Walter-Vorgabe 20.08.2026: QST-Relevanz-Felder.
        m.Erwerbstaetig,
        m.ArbeitgeberName,
        m.ArbeitgeberStrasse,
        m.ArbeitgeberPlz,
        m.ArbeitgeberOrt,
        m.ArbeitgeberKanton,
        m.Stellenantritt,
        m.InErstausbildung,
        m.KeineUnterhaltspflicht,
        qstVerlauf,
        // Konkubinats-Logik (Walter 25.08.2026, docs/konkubinat-qst-konzept.md)
        m.MaHatHoeheresEinkommen,
        m.GemeinsamesKindMitPartner,
        // Familienzulagen-Anmeldung (Walter 11.10.2026)
        m.BeziehungZumMa,
        m.Erwerbsunfaehig,
        m.ElterlicheSorge,
        m.AndererElternteilId,
        m.Arbeitspensum,
        m.ArbeitgeberTelefon,
        m.EinkommenUeberMinimum,
        m.SeNeAngemeldet,
        m.SeNeAusgleichskasse,
        m.BeziehtLeistung,
        m.LeistungVon,
        // Walter-Vorgabe 13.06.2026: Beleg-Doku-FK durchreichen — das Frontend
        // zeigt damit „📄 Doku verknüpft" am Ehepartner-Eintrag und kann den
        // Beleg im Vorschau-Panel öffnen.
        m.DokumentId,
        m.GeburtsurkundeDokumentId,
        m.CreatedAt,
        m.UpdatedAt,
        alternativeAddress = alt == null ? null : new {
            alt.Id,
            alt.Description,
            alt.Street,
            alt.Street2,
            alt.PoBox,
            alt.ZipCode,
            alt.City,
            alt.Canton,
            alt.Country,
        }
    };

    // POST /api/employees/{employeeId}/family
    [HttpPost]
    public async Task<IActionResult> Create(int employeeId, EmployeeFamilyMember member)
    {
        member.EmployeeId = employeeId;
        member.CreatedAt = DateTime.Now;
        member.UpdatedAt = DateTime.Now;
        if (await FamilieUngueltigAsync(employeeId, member, null) is IActionResult pdStop) return pdStop;

        // AlternativeAddressId nur akzeptieren, wenn die Zusatzadresse
        // tatsächlich zum gleichen MA gehört (Schutz vor Cross-MA-IDs).
        member.AlternativeAddressId = await ValidateAlternativeAddressAsync(employeeId, member.AlternativeAddressId);

        // Walter 25.08.2026: Konsistenz-Guard — eine erfasste Zusatzadresse
        // bedeutet IMMER «nicht im gleichen Haushalt».
        if (member.AlternativeAddressId != null) member.LebtImHaushalt = false;
        if (member.MemberType == "Kind" && !member.ErfahrenAm.HasValue)
            member.ErfahrenAm = DateOnly.FromDateTime(DateTime.Now);
        if (member.MemberType == "Kind") member.KeineUnterhaltspflicht = member.StartUnterhalt == false;

        var famzFehler = await FamzFelderUebernehmenAsync(employeeId, member, member);
        if (famzFehler != null) return famzFehler;

        _context.EmployeeFamilyMembers.Add(member);
        await _context.SaveChangesAsync();
        if (member.MemberType == "Kind")
            await KindQstVerlaufLader.ErsteVersionSicherstellenAsync(_context, member);
        try { await SyncHistoriesAsync(member, member); }
        catch (InvalidOperationException ex) when (ex.Message == "ERFAHREN_VOR_GUELTIG")
        {
            return BadRequest(new { error = "ERFAHREN_VOR_GUELTIG", message = "«Erfahren am» darf nicht vor «Seit» liegen." });
        }
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { employeeId, id = member.Id }, member);
    }

    /// <summary>
    /// Personaldaten-Plausibilität Familie (Walter 15.10.2026): ungültige AHV-Nummer oder
    /// unmögliches Geburtsdatum nicht speichern — nur was die Eingabe neu falsch macht.
    /// </summary>
    private async Task<IActionResult?> FamilieUngueltigAsync(int employeeId, EmployeeFamilyMember neu, EmployeeFamilyMember? alt)
    {
        var maGeb = await _context.Employees.AsNoTracking()
            .Where(e => e.Id == employeeId).Select(e => e.DateOfBirth).FirstOrDefaultAsync();
        DateOnly? maGeburt = maGeb is DateTime mg ? DateOnly.FromDateTime(mg) : null;
        var heute = DateOnly.FromDateTime(DateTime.Today);
        static PdFamilie F(EmployeeFamilyMember m) => new()
        {
            Art = m.MemberType,
            Vorname = m.FirstName,
            Geburt = m.DateOfBirth is DateTime g ? DateOnly.FromDateTime(g) : null,
            Ahv = m.SocialSecurityNumber,
        };
        var neuB = PersonaldatenPruefung.PruefeFamilie(F(neu), maGeburt, heute).Where(b => b.Stufe == PdStufe.Fehler);
        var altB = alt != null ? PersonaldatenPruefung.PruefeFamilie(F(alt), maGeburt, heute) : null;
        var neue = PersonaldatenPruefung.NurNeue(neuB, altB);
        return neue.Count == 0 ? null : BadRequest(new
        {
            error = "PERSONALDATEN_UNGUELTIG",
            message = PersonaldatenPruefung.Zusammenfassung(neue),
            befunde = neue,
        });
    }

    // PUT /api/employees/{employeeId}/family/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int employeeId, int id, EmployeeFamilyMember member)
    {
        var existing = await _context.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);

        if (existing == null) return NotFound();
        if (await FamilieUngueltigAsync(employeeId, member, existing) is IActionResult pdStop) return pdStop;

        existing.MemberType           = member.MemberType;
        existing.Gender               = member.Gender;
        existing.FamilyStatus         = member.FamilyStatus;
        existing.LastName             = member.LastName;
        existing.MaidenName           = member.MaidenName;
        existing.FirstName            = member.FirstName;
        existing.SocialSecurityNumber = member.SocialSecurityNumber;
        existing.Phone                = string.IsNullOrWhiteSpace(member.Phone) ? null : member.Phone.Trim();
        existing.LivesInSwitzerland   = member.LivesInSwitzerland;
        existing.DateOfBirth          = member.DateOfBirth;
        existing.DateOfDeath          = member.DateOfDeath;
        existing.Allowance1Until      = member.Allowance1Until;
        existing.Allowance2Until      = member.Allowance2Until;
        existing.Allowance3Until      = member.Allowance3Until;
        existing.AlternativeAddressId = await ValidateAlternativeAddressAsync(employeeId, member.AlternativeAddressId);
        // QST-Zeitraum und Erstausbildung pflegt der QST-Verlauf am Kind
        // (Endpunkte …/ausbildung, …/verhaeltnis, …/qst-entscheid) — hier nicht.
        // «Erfahren am» ist nicht mehr in der Maske: leer lässt den Wert stehen.
        existing.ErfahrenAm          = member.ErfahrenAm ?? existing.ErfahrenAm;
        existing.PermitTypeId         = member.PermitTypeId;
        existing.PermitExpiryDate     = member.PermitExpiryDate;
        existing.ZemisNumber          = string.IsNullOrWhiteSpace(member.ZemisNumber) ? null : member.ZemisNumber.Trim();
        existing.NationalityId        = member.NationalityId;
        // Walter-Vorgabe 20.08.2026: QST-Relevanz-Felder (Ehepartner-Erwerb,
        // Kind-Erstausbildung).
        // Walter 25.08.2026: Haushalt-Status — Guard: Zusatzadresse gesetzt
        // bedeutet immer «nicht im gleichen Haushalt».
        existing.LebtImHaushalt       = existing.AlternativeAddressId != null ? false : member.LebtImHaushalt;
        existing.Erwerbstaetig        = member.Erwerbstaetig;
        existing.ArbeitgeberName      = string.IsNullOrWhiteSpace(member.ArbeitgeberName)    ? null : member.ArbeitgeberName.Trim();
        existing.ArbeitgeberStrasse   = string.IsNullOrWhiteSpace(member.ArbeitgeberStrasse) ? null : member.ArbeitgeberStrasse.Trim();
        existing.ArbeitgeberPlz       = string.IsNullOrWhiteSpace(member.ArbeitgeberPlz)     ? null : member.ArbeitgeberPlz.Trim();
        existing.ArbeitgeberOrt       = string.IsNullOrWhiteSpace(member.ArbeitgeberOrt)     ? null : member.ArbeitgeberOrt.Trim();
        existing.ArbeitgeberKanton    = string.IsNullOrWhiteSpace(member.ArbeitgeberKanton)  ? null : member.ArbeitgeberKanton.Trim();
        existing.Stellenantritt       = member.Stellenantritt;
        // «keine Unterhaltspflicht» ist nur noch Abbild des Verlaufs (SnapshotNachfuehren).
        // Konkubinats-Logik (Walter 25.08.2026, docs/konkubinat-qst-konzept.md)
        existing.MaHatHoeheresEinkommen     = member.MaHatHoeheresEinkommen;
        existing.GemeinsamesKindMitPartner  = member.GemeinsamesKindMitPartner;
        var famzFehler = await FamzFelderUebernehmenAsync(employeeId, existing, member);
        if (famzFehler != null) return famzFehler;
        existing.UpdatedAt            = DateTime.Now;
        if (existing.MemberType == "Kind")
        {
            await KindQstVerlaufLader.ErsteVersionSicherstellenAsync(_context, existing);
            await _context.SaveChangesAsync();
            await KindQstVerlaufLader.SnapshotNachfuehrenAsync(_context, existing);
            if (existing.AlternativeAddressId != null) existing.LebtImHaushalt = false;
        }

        try { await SyncHistoriesAsync(existing, member); }
        catch (InvalidOperationException ex) when (ex.Message == "ERFAHREN_VOR_GUELTIG")
        {
            return BadRequest(new { error = "ERFAHREN_VOR_GUELTIG", message = "«Erfahren am» darf nicht vor «Seit» liegen." });
        }
        await _context.SaveChangesAsync();
        return Ok(existing);
    }

    // GET …/family/{id}/permit-history
    [HttpGet("{id:int}/permit-history")]
    public async Task<IActionResult> ListPermitHistory(int employeeId, int id)
    {
        if (!await MemberOk(employeeId, id)) return NotFound();
        var list = await _context.FamilyMemberPermitHistories.AsNoTracking()
            .Include(h => h.PermitType)
            .Where(h => h.FamilyMemberId == id)
            .OrderByDescending(h => h.ValidFrom).ThenByDescending(h => h.Id)
            .Select(h => new
            {
                h.Id,
                h.PermitTypeId,
                permitCode = h.PermitType != null ? h.PermitType.Code : null,
                permitLabel = h.PermitType != null ? (h.PermitType.Description ?? h.PermitType.Code) : "CH / keine",
                validFrom = h.ValidFrom.ToString("yyyy-MM-dd"),
                erfahrenAm = h.ErfahrenAm.HasValue ? h.ErfahrenAm.Value.ToString("yyyy-MM-dd") : null,
                validTo = h.ValidTo.HasValue ? h.ValidTo.Value.ToString("yyyy-MM-dd") : null,
                h.Note,
            })
            .ToListAsync();
        return Ok(list);
    }

    // GET …/family/{id}/erwerb-history
    [HttpGet("{id:int}/erwerb-history")]
    public async Task<IActionResult> ListErwerbHistory(int employeeId, int id)
    {
        if (!await MemberOk(employeeId, id)) return NotFound();
        var list = await _context.FamilyMemberErwerbHistories.AsNoTracking()
            .Where(h => h.FamilyMemberId == id)
            .OrderByDescending(h => h.ValidFrom).ThenByDescending(h => h.Id)
            .Select(h => new
            {
                h.Id,
                h.Erwerbstaetig,
                h.ArbeitgeberName,
                h.ArbeitgeberKanton,
                stellenantritt = h.Stellenantritt.HasValue ? h.Stellenantritt.Value.ToString("yyyy-MM-dd") : null,
                validFrom = h.ValidFrom.ToString("yyyy-MM-dd"),
                erfahrenAm = h.ErfahrenAm.HasValue ? h.ErfahrenAm.Value.ToString("yyyy-MM-dd") : null,
                h.Note,
            })
            .ToListAsync();
        return Ok(list);
    }

    [HttpDelete("{id:int}/permit-history/{histId:int}")]
    public async Task<IActionResult> DeletePermitHistory(int employeeId, int id, int histId)
    {
        if (!await MemberOk(employeeId, id)) return NotFound();
        var h = await _context.FamilyMemberPermitHistories.FirstOrDefaultAsync(x => x.Id == histId && x.FamilyMemberId == id);
        if (h == null) return NotFound();
        _context.FamilyMemberPermitHistories.Remove(h);
        await _context.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    [HttpDelete("{id:int}/erwerb-history/{histId:int}")]
    public async Task<IActionResult> DeleteErwerbHistory(int employeeId, int id, int histId)
    {
        if (!await MemberOk(employeeId, id)) return NotFound();
        var h = await _context.FamilyMemberErwerbHistories.FirstOrDefaultAsync(x => x.Id == histId && x.FamilyMemberId == id);
        if (h == null) return NotFound();
        _context.FamilyMemberErwerbHistories.Remove(h);
        await _context.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    public sealed class PermitStatusDto
    {
        public int? PermitTypeId { get; set; }
        public string? Seit { get; set; }
        public string? ErfahrenAm { get; set; }
    }

    public sealed class ErwerbStatusDto
    {
        public bool? Erwerbstaetig { get; set; }
        public string? Seit { get; set; }
        public string? ErfahrenAm { get; set; }
    }

    /// <summary>Bewilligungsstatus ändern (QST) — Seit + Erfahren am (Walter 08.10.2026).</summary>
    [HttpPost("{id:int}/permit-status")]
    public async Task<IActionResult> ChangePermitStatus(int employeeId, int id, [FromBody] PermitStatusDto dto)
    {
        var m = await _context.EmployeeFamilyMembers.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (m == null) return NotFound();
        var seit = EmployeeQuellensteuerController.ParseDatum(dto.Seit);
        if (seit == null) return BadRequest(new { error = "DATUM_UNGUELTIG", message = "«Seit» ist Pflicht." });
        DateOnly? ea = null;
        if (!string.IsNullOrWhiteSpace(dto.ErfahrenAm))
        {
            ea = EmployeeQuellensteuerController.ParseDatum(dto.ErfahrenAm);
            if (ea == null) return BadRequest(new { error = "DATUM_UNGUELTIG", message = "«Erfahren am» ist ungültig." });
            if (ea.Value < seit.Value)
                return BadRequest(new { error = "ERFAHREN_VOR_GUELTIG", message = "«Erfahren am» darf nicht vor «Seit» liegen." });
        }
        m.PermitTypeId = dto.PermitTypeId is > 0 ? dto.PermitTypeId : null;
        m.UpdatedAt = DateTime.Now;
        _context.FamilyMemberPermitHistories.Add(new FamilyMemberPermitHistory
        {
            FamilyMemberId = m.Id,
            PermitTypeId = m.PermitTypeId,
            ValidFrom = seit.Value,
            ErfahrenAm = ea,
            Note = "Statusänderung",
            CreatedAt = DateTime.Now,
        });
        await _context.SaveChangesAsync();
        return Ok(new { ok = true, permitTypeId = m.PermitTypeId, seit = seit.Value.ToString("yyyy-MM-dd") });
    }

    /// <summary>Erwerbsstatus ändern (Tarif B/C) — Seit + Erfahren am (Walter 08.10.2026).</summary>
    [HttpPost("{id:int}/erwerb-status")]
    public async Task<IActionResult> ChangeErwerbStatus(int employeeId, int id, [FromBody] ErwerbStatusDto dto)
    {
        var m = await _context.EmployeeFamilyMembers.FirstOrDefaultAsync(x => x.Id == id && x.EmployeeId == employeeId);
        if (m == null) return NotFound();
        if (dto.Erwerbstaetig == null)
            return BadRequest(new { error = "ERWERB_FEHLT", message = "Neuer Status (Ja/Nein) fehlt." });
        var seit = EmployeeQuellensteuerController.ParseDatum(dto.Seit);
        if (seit == null) return BadRequest(new { error = "DATUM_UNGUELTIG", message = "«Seit» ist Pflicht." });
        DateOnly? ea = null;
        if (!string.IsNullOrWhiteSpace(dto.ErfahrenAm))
        {
            ea = EmployeeQuellensteuerController.ParseDatum(dto.ErfahrenAm);
            if (ea == null) return BadRequest(new { error = "DATUM_UNGUELTIG", message = "«Erfahren am» ist ungültig." });
            if (ea.Value < seit.Value)
                return BadRequest(new { error = "ERFAHREN_VOR_GUELTIG", message = "«Erfahren am» darf nicht vor «Seit» liegen." });
        }
        m.Erwerbstaetig = dto.Erwerbstaetig;
        m.UpdatedAt = DateTime.Now;
        _context.FamilyMemberErwerbHistories.Add(new FamilyMemberErwerbHistory
        {
            FamilyMemberId = m.Id,
            Erwerbstaetig = dto.Erwerbstaetig,
            ValidFrom = seit.Value,
            ErfahrenAm = ea,
            Note = "Statusänderung",
            CreatedAt = DateTime.Now,
        });
        await _context.SaveChangesAsync();
        return Ok(new { ok = true, erwerbstaetig = m.Erwerbstaetig, seit = seit.Value.ToString("yyyy-MM-dd") });
    }

    // ── QST-Verlauf am Kind (Walter 11.10.2026, docs/kind-qst-verlauf-konzept.md) ──

    static string? Iso(DateOnly? d) => d?.ToString("yyyy-MM-dd");
    string? Wer() => User.Identity?.Name;

    async Task<EmployeeFamilyMember?> KindAsync(int employeeId, int id)
        => await _context.EmployeeFamilyMembers.FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId && m.MemberType == "Kind");

    async Task<string?> DokumentPruefenAsync(int employeeId, int? dokumentId)
    {
        if (dokumentId is not > 0) return null;
        var ok = await _context.EmployeeDokumente.AnyAsync(d => d.Id == dokumentId && d.EmployeeId == employeeId);
        return ok ? null : "Das Dokument gehört nicht zu diesem Mitarbeiter.";
    }

    async Task<IActionResult> NachAenderungAsync(EmployeeFamilyMember kind)
    {
        await _context.SaveChangesAsync();
        await KindQstVerlaufLader.SnapshotNachfuehrenAsync(_context, kind);
        await _context.SaveChangesAsync();
        return await QstVerlauf(kind.EmployeeId, kind.Id);
    }

    /// <summary>Alle drei Listen + Wirkung + Hinweise für das Familien-Fenster.</summary>
    [HttpGet("{id:int}/qst-verlauf")]
    public async Task<IActionResult> QstVerlauf(int employeeId, int id)
    {
        var kind = await _context.EmployeeFamilyMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId && m.MemberType == "Kind");
        if (kind == null) return NotFound();
        var ausb = await _context.FamilyMemberAusbildungen.AsNoTracking().Where(a => a.FamilyMemberId == id)
            .OrderBy(a => a.Von).ThenBy(a => a.Id).ToListAsync();
        var verh = await _context.FamilyMemberVerhaeltnisse.AsNoTracking().Where(v => v.FamilyMemberId == id)
            .OrderBy(v => v.GueltigAb).ThenBy(v => v.Id).ToListAsync();
        var ents = await _context.FamilyMemberQstEntscheide.AsNoTracking().Where(e => e.FamilyMemberId == id)
            .OrderBy(e => e.Von).ThenBy(e => e.Id).ToListAsync();
        var az = await _context.FamilyMemberAllowances.AsNoTracking()
            .Where(a => a.FamilyMemberId == id && a.AllowanceType == "AZ")
            .OrderBy(a => a.ValidFrom).ToListAsync();
        var anzeige = await QstAnzeigeAsync(new[] { kind });

        return Ok(new
        {
            ausbildungen = ausb.Select(a => new
            {
                a.Id, von = Iso(a.Von), bis = Iso(a.Bis), a.Art, a.EndeGrund, a.Erstausbildung, a.Geprueft,
                a.DokumentId, a.Quelle, erfahrenAm = Iso(a.ErfahrenAm), a.Bemerkung,
            }),
            verhaeltnisse = verh.Select(v => new
            {
                v.Id, gueltigAb = Iso(v.GueltigAb), v.ImHaushalt, v.UnterhaltHauptsache, v.Quelle,
                erfahrenAm = Iso(v.ErfahrenAm), v.Bemerkung,
            }),
            entscheide = ents.Select(e => new
            {
                e.Id, e.Status, von = Iso(e.Von), bis = Iso(e.Bis), e.Grund, e.DokumentId, e.Quelle,
                erfahrenAm = Iso(e.ErfahrenAm),
            }),
            // Ausbildungszulagen, die noch nicht als QST-Ausbildung übernommen sind.
            zulagenOhneAusbildung = az
                .Where(z => !ausb.Any(a => a.Quelle == "ZULAGE" && a.Von == z.ValidFrom))
                .Select(z => new { z.Id, von = Iso(z.ValidFrom), bis = Iso(z.ValidTo), z.DokumentId }),
            anzeige = anzeige.GetValueOrDefault(id),
        });
    }

    public sealed class AusbildungDto
    {
        public string? Von { get; set; }
        public string? Bis { get; set; }
        public string? Art { get; set; }
        public string? EndeGrund { get; set; }
        public bool? Erstausbildung { get; set; }
        public bool Geprueft { get; set; } = true;
        public int? DokumentId { get; set; }
        public string? ErfahrenAm { get; set; }
        public string? Bemerkung { get; set; }
    }

    public sealed class VerhaeltnisDto
    {
        public string? GueltigAb { get; set; }
        public bool? ImHaushalt { get; set; }
        public bool? UnterhaltHauptsache { get; set; }
        public string? ErfahrenAm { get; set; }
        public string? Bemerkung { get; set; }
    }

    public sealed class QstEntscheidDto
    {
        public string? Status { get; set; }
        public string? Von { get; set; }
        public string? Bis { get; set; }
        public string? Grund { get; set; }
        public int? DokumentId { get; set; }
        public string? ErfahrenAm { get; set; }
    }

    static readonly string[] AusbildungArten = { "LEHRE", "SCHULE", "STUDIUM", "BRUECKE", "ANDERE" };
    static readonly string[] EndeGruende = { "ABSCHLUSS", "ABBRUCH", "UNTERBRUCH" };

    /// <summary>Datum lesen: leer = null, ungültig = Fehlertext.</summary>
    static (DateOnly? Wert, string? Fehler) Datum(string? s, string feld)
    {
        if (string.IsNullOrWhiteSpace(s)) return (null, null);
        var d = EmployeeQuellensteuerController.ParseDatum(s);
        return d == null ? (null, $"«{feld}» ist ungültig.") : (d, null);
    }

    static IActionResult Fehler(string text) => new BadRequestObjectResult(new { error = "UNGUELTIG", message = text });

    async Task<IActionResult?> AusbildungUebernehmenAsync(int employeeId, FamilyMemberAusbildung a, AusbildungDto dto)
    {
        var (von, f1) = Datum(dto.Von, "Von"); if (f1 != null) return Fehler(f1);
        var (bis, f2) = Datum(dto.Bis, "Bis"); if (f2 != null) return Fehler(f2);
        var (ea, f3) = Datum(dto.ErfahrenAm, "Erfahren am"); if (f3 != null) return Fehler(f3);
        if (von == null) return Fehler("«Von» ist Pflicht.");
        if (bis != null && bis < von) return Fehler("«Bis» liegt vor «Von».");
        var art = (dto.Art ?? "ANDERE").Trim().ToUpperInvariant();
        if (!AusbildungArten.Contains(art)) return Fehler("Unbekannte Ausbildungsart.");
        var ende = string.IsNullOrWhiteSpace(dto.EndeGrund) ? null : dto.EndeGrund.Trim().ToUpperInvariant();
        if (ende != null && !EndeGruende.Contains(ende)) return Fehler("Unbekannter Grund für das Ende.");
        var dokFehler = await DokumentPruefenAsync(employeeId, dto.DokumentId);
        if (dokFehler != null) return Fehler(dokFehler);
        a.Von = von; a.Bis = bis; a.Art = art; a.EndeGrund = bis == null ? null : ende;
        a.Erstausbildung = dto.Erstausbildung; a.Geprueft = dto.Geprueft;
        a.DokumentId = dto.DokumentId is > 0 ? dto.DokumentId : null;
        a.ErfahrenAm = ea; a.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        a.ErfasstAm = DateTime.Now; a.ErfasstVon = Wer();
        return null;
    }

    [HttpPost("{id:int}/ausbildung")]
    public async Task<IActionResult> AusbildungNeu(int employeeId, int id, [FromBody] AusbildungDto dto)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var a = new FamilyMemberAusbildung { FamilyMemberId = id, Quelle = "HAND" };
        var fehler = await AusbildungUebernehmenAsync(employeeId, a, dto);
        if (fehler != null) return fehler;
        _context.FamilyMemberAusbildungen.Add(a);
        return await NachAenderungAsync(kind);
    }

    [HttpPut("{id:int}/ausbildung/{aid:int}")]
    public async Task<IActionResult> AusbildungAendern(int employeeId, int id, int aid, [FromBody] AusbildungDto dto)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var a = await _context.FamilyMemberAusbildungen.FirstOrDefaultAsync(x => x.Id == aid && x.FamilyMemberId == id);
        if (a == null) return NotFound();
        var fehler = await AusbildungUebernehmenAsync(employeeId, a, dto);
        if (fehler != null) return fehler;
        return await NachAenderungAsync(kind);
    }

    [HttpDelete("{id:int}/ausbildung/{aid:int}")]
    public async Task<IActionResult> AusbildungLoeschen(int employeeId, int id, int aid)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var a = await _context.FamilyMemberAusbildungen.FirstOrDefaultAsync(x => x.Id == aid && x.FamilyMemberId == id);
        if (a == null) return NotFound();
        _context.FamilyMemberAusbildungen.Remove(a);
        return await NachAenderungAsync(kind);
    }

    /// <summary>
    /// Ausbildungszulage als QST-Ausbildung übernehmen (Beleg: Zulagenentscheid).
    /// Erstausbildung = ja; HR kann es danach ändern.
    /// </summary>
    [HttpPost("{id:int}/ausbildung/aus-zulage/{allowanceId:int}")]
    public async Task<IActionResult> AusbildungAusZulage(int employeeId, int id, int allowanceId)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var z = await _context.FamilyMemberAllowances.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == allowanceId && x.FamilyMemberId == id && x.AllowanceType == "AZ");
        if (z == null) return NotFound();
        if (await _context.FamilyMemberAusbildungen.AnyAsync(a => a.FamilyMemberId == id && a.Quelle == "ZULAGE" && a.Von == z.ValidFrom))
            return Conflict(new { error = "SCHON_UEBERNOMMEN", message = "Diese Ausbildungszulage ist schon übernommen." });
        _context.FamilyMemberAusbildungen.Add(new FamilyMemberAusbildung
        {
            FamilyMemberId = id,
            Von = z.ValidFrom,
            Bis = z.ValidTo,
            Art = "ANDERE",
            Erstausbildung = true,
            Geprueft = true,
            DokumentId = z.DokumentId,
            Quelle = "ZULAGE",
            Bemerkung = "Beleg: Zulagenentscheid",
            ErfasstAm = DateTime.Now,
            ErfasstVon = Wer(),
        });
        return await NachAenderungAsync(kind);
    }

    async Task<IActionResult?> VerhaeltnisUebernehmenAsync(FamilyMemberVerhaeltnis v, VerhaeltnisDto dto)
    {
        var (ab, f1) = Datum(dto.GueltigAb, "Gültig ab"); if (f1 != null) return Fehler(f1);
        var (ea, f2) = Datum(dto.ErfahrenAm, "Erfahren am"); if (f2 != null) return Fehler(f2);
        if (ab == null) return Fehler("«Gültig ab» ist Pflicht.");
        if (await _context.FamilyMemberVerhaeltnisse.AnyAsync(x => x.FamilyMemberId == v.FamilyMemberId && x.GueltigAb == ab && x.Id != v.Id))
            return Fehler($"Ab {ab:dd.MM.yyyy} gibt es schon einen Eintrag — diesen bearbeiten.");
        v.GueltigAb = ab.Value; v.ImHaushalt = dto.ImHaushalt; v.UnterhaltHauptsache = dto.UnterhaltHauptsache;
        v.ErfahrenAm = ea; v.Bemerkung = string.IsNullOrWhiteSpace(dto.Bemerkung) ? null : dto.Bemerkung.Trim();
        v.ErfasstAm = DateTime.Now; v.ErfasstVon = Wer();
        return null;
    }

    [HttpPost("{id:int}/verhaeltnis")]
    public async Task<IActionResult> VerhaeltnisNeu(int employeeId, int id, [FromBody] VerhaeltnisDto dto)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var v = new FamilyMemberVerhaeltnis { FamilyMemberId = id, Quelle = "HAND" };
        var fehler = await VerhaeltnisUebernehmenAsync(v, dto);
        if (fehler != null) return fehler;
        _context.FamilyMemberVerhaeltnisse.Add(v);
        return await NachAenderungAsync(kind);
    }

    [HttpPut("{id:int}/verhaeltnis/{vid:int}")]
    public async Task<IActionResult> VerhaeltnisAendern(int employeeId, int id, int vid, [FromBody] VerhaeltnisDto dto)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var v = await _context.FamilyMemberVerhaeltnisse.FirstOrDefaultAsync(x => x.Id == vid && x.FamilyMemberId == id);
        if (v == null) return NotFound();
        var fehler = await VerhaeltnisUebernehmenAsync(v, dto);
        if (fehler != null) return fehler;
        return await NachAenderungAsync(kind);
    }

    [HttpDelete("{id:int}/verhaeltnis/{vid:int}")]
    public async Task<IActionResult> VerhaeltnisLoeschen(int employeeId, int id, int vid)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var v = await _context.FamilyMemberVerhaeltnisse.FirstOrDefaultAsync(x => x.Id == vid && x.FamilyMemberId == id);
        if (v == null) return NotFound();
        if (!await _context.FamilyMemberVerhaeltnisse.AnyAsync(x => x.FamilyMemberId == id && x.Id != vid))
            return Fehler("Der letzte Eintrag kann nicht gelöscht werden — bearbeiten statt löschen.");
        _context.FamilyMemberVerhaeltnisse.Remove(v);
        return await NachAenderungAsync(kind);
    }

    async Task<IActionResult?> EntscheidUebernehmenAsync(int employeeId, FamilyMemberQstEntscheid e, QstEntscheidDto dto)
    {
        var status = (dto.Status ?? "").Trim().ToUpperInvariant();
        if (status is not (KindQstVerlauf.Anerkannt or KindQstVerlauf.NichtAnerkannt or KindQstVerlauf.InPruefung))
            return Fehler("Status fehlt.");
        var (von, f1) = Datum(dto.Von, "Von"); if (f1 != null) return Fehler(f1);
        var (bis, f2) = Datum(dto.Bis, "Bis"); if (f2 != null) return Fehler(f2);
        var (ea, f3) = Datum(dto.ErfahrenAm, "Erfahren am"); if (f3 != null) return Fehler(f3);
        if (von == null) return Fehler("«Von» ist Pflicht.");
        if (bis != null && bis < von) return Fehler("«Bis» liegt vor «Von».");
        var dokFehler = await DokumentPruefenAsync(employeeId, dto.DokumentId);
        if (dokFehler != null) return Fehler(dokFehler);
        e.Status = status; e.Von = von; e.Bis = bis;
        e.Grund = string.IsNullOrWhiteSpace(dto.Grund) ? null : dto.Grund.Trim();
        e.DokumentId = dto.DokumentId is > 0 ? dto.DokumentId : null;
        e.ErfahrenAm = ea; e.ErfasstAm = DateTime.Now; e.ErfasstVon = Wer();
        return null;
    }

    [HttpPost("{id:int}/qst-entscheid")]
    public async Task<IActionResult> EntscheidNeu(int employeeId, int id, [FromBody] QstEntscheidDto dto)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var e = new FamilyMemberQstEntscheid { FamilyMemberId = id, Quelle = "HAND" };
        var fehler = await EntscheidUebernehmenAsync(employeeId, e, dto);
        if (fehler != null) return fehler;
        _context.FamilyMemberQstEntscheide.Add(e);
        return await NachAenderungAsync(kind);
    }

    [HttpPut("{id:int}/qst-entscheid/{eid:int}")]
    public async Task<IActionResult> EntscheidAendern(int employeeId, int id, int eid, [FromBody] QstEntscheidDto dto)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var e = await _context.FamilyMemberQstEntscheide.FirstOrDefaultAsync(x => x.Id == eid && x.FamilyMemberId == id);
        if (e == null) return NotFound();
        var fehler = await EntscheidUebernehmenAsync(employeeId, e, dto);
        if (fehler != null) return fehler;
        return await NachAenderungAsync(kind);
    }

    [HttpDelete("{id:int}/qst-entscheid/{eid:int}")]
    public async Task<IActionResult> EntscheidLoeschen(int employeeId, int id, int eid)
    {
        var kind = await KindAsync(employeeId, id);
        if (kind == null) return NotFound();
        var e = await _context.FamilyMemberQstEntscheide.FirstOrDefaultAsync(x => x.Id == eid && x.FamilyMemberId == id);
        if (e == null) return NotFound();
        _context.FamilyMemberQstEntscheide.Remove(e);
        return await NachAenderungAsync(kind);
    }

    Task<bool> MemberOk(int employeeId, int id)
        => _context.EmployeeFamilyMembers.AnyAsync(m => m.Id == id && m.EmployeeId == employeeId);

    /// <summary>
    /// Bewilligungs-/Erwerbs-Historie nachführen, wenn Seit/ErfahrenAm
    /// mitgeliefert werden oder sich der Snapshot ändert (Walter 08.10.2026).
    /// </summary>
    async Task SyncHistoriesAsync(EmployeeFamilyMember snap, EmployeeFamilyMember dto)
    {
        var typ = (snap.MemberType ?? "").Trim();
        if (typ is not ("Ehepartner" or "Konkubinatspartner" or "AndererElternteil")) return;

        // ── Bewilligung ──────────────────────────────────────────────────
        var permitHist = await _context.FamilyMemberPermitHistories
            .Where(h => h.FamilyMemberId == snap.Id)
            .OrderBy(h => h.ValidFrom).ThenBy(h => h.Id)
            .ToListAsync();
        // Historie nur Bewilligungs-Typ + Seit/Erfahren (Walter 08.10.2026) —
        // kein «Gültig bis» mehr in der Maske.
        bool hatPermitDaten = snap.PermitTypeId != null || snap.NationalityId != null;
        var letzterP = permitHist.LastOrDefault();
        bool permitGeaendert = letzterP == null || letzterP.PermitTypeId != snap.PermitTypeId;
        var permitSeit = dto.PermitSeit ?? DateOnly.FromDateTime(DateTime.Today);
        if (hatPermitDaten && (dto.PermitSeit.HasValue || permitHist.Count == 0 || permitGeaendert))
        {
            var ea = dto.PermitErfahrenAm;
            if (ea.HasValue && ea.Value < permitSeit)
                throw new InvalidOperationException("ERFAHREN_VOR_GUELTIG");
            if (letzterP != null && letzterP.ValidFrom == permitSeit)
            {
                letzterP.PermitTypeId = snap.PermitTypeId;
                letzterP.ErfahrenAm = ea;
                letzterP.ValidTo = null;
            }
            else if (permitGeaendert || dto.PermitSeit.HasValue || permitHist.Count == 0)
            {
                _context.FamilyMemberPermitHistories.Add(new FamilyMemberPermitHistory
                {
                    FamilyMemberId = snap.Id,
                    PermitTypeId = snap.PermitTypeId,
                    ValidFrom = permitSeit,
                    ErfahrenAm = ea,
                    ValidTo = null,
                    Note = permitHist.Count == 0 ? "Ersterfassung" : null,
                    CreatedAt = DateTime.Now,
                });
            }
        }

        // ── Erwerbstätigkeit: nur Ja/Nein-Wechsel (kein Arbeitgeber) ─────
        var erwerbHist = await _context.FamilyMemberErwerbHistories
            .Where(h => h.FamilyMemberId == snap.Id)
            .OrderBy(h => h.ValidFrom).ThenBy(h => h.Id)
            .ToListAsync();
        var letzterE = erwerbHist.LastOrDefault();
        bool erwerbGeaendert = letzterE == null || letzterE.Erwerbstaetig != snap.Erwerbstaetig;
        var erwerbSeit = dto.ErwerbSeit ?? DateOnly.FromDateTime(DateTime.Today);
        if (snap.Erwerbstaetig != null && (dto.ErwerbSeit.HasValue || erwerbHist.Count == 0 || erwerbGeaendert))
        {
            var ea = dto.ErwerbErfahrenAm;
            if (ea.HasValue && ea.Value < erwerbSeit)
                throw new InvalidOperationException("ERFAHREN_VOR_GUELTIG");
            if (letzterE != null && letzterE.ValidFrom == erwerbSeit)
            {
                letzterE.Erwerbstaetig = snap.Erwerbstaetig;
                letzterE.ErfahrenAm = ea;
            }
            else if (erwerbGeaendert || dto.ErwerbSeit.HasValue || erwerbHist.Count == 0)
            {
                _context.FamilyMemberErwerbHistories.Add(new FamilyMemberErwerbHistory
                {
                    FamilyMemberId = snap.Id,
                    Erwerbstaetig = snap.Erwerbstaetig,
                    ValidFrom = erwerbSeit,
                    ErfahrenAm = ea,
                    Note = erwerbHist.Count == 0 ? "Ersterfassung" : null,
                    CreatedAt = DateTime.Now,
                });
            }
        }
    }

    public static readonly string[] Beziehungen = { "L", "A", "S", "P", "G", "E" };
    public static readonly string[] Sorgen = { "ELTERN", "MUTTER", "VATER" };
    public static readonly string[] ElternTypen = { "Ehepartner", "Konkubinatspartner", "AndererElternteil" };
    static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// Felder der Familienzulagen-Anmeldung (Walter 11.10.2026) übernehmen und
    /// prüfen. Kind: anderer Elternteil muss ein Partner / anderer Elternteil
    /// desselben MA sein; ist es der Konkubinatspartner, gilt das Kind als
    /// gemeinsames Kind (QST H1/A0) — sonst nicht.
    /// </summary>
    async Task<IActionResult?> FamzFelderUebernehmenAsync(int employeeId, EmployeeFamilyMember ziel, EmployeeFamilyMember dto)
    {
        bool kind = ziel.MemberType == "Kind";
        bool elternteil = ElternTypen.Contains(ziel.MemberType);

        var bez = Leer(dto.BeziehungZumMa)?.ToUpperInvariant();
        if (bez != null && !Beziehungen.Contains(bez)) return Fehler("Unbekannte Beziehung zum Kind.");
        var sorge = Leer(dto.ElterlicheSorge)?.ToUpperInvariant();
        if (sorge != null && !Sorgen.Contains(sorge)) return Fehler("Unbekannte Angabe zur elterlichen Sorge.");
        ziel.BeziehungZumMa = kind ? bez : null;
        ziel.ElterlicheSorge = kind ? sorge : null;
        ziel.Erwerbsunfaehig = kind && dto.Erwerbsunfaehig;

        if (kind && dto.AndererElternteilId is int aeId)
        {
            var ae = await _context.EmployeeFamilyMembers.AsNoTracking()
                .Where(m => m.Id == aeId && m.EmployeeId == employeeId)
                .Select(m => new { m.MemberType })
                .FirstOrDefaultAsync();
            if (ae == null || !ElternTypen.Contains(ae.MemberType))
                return Fehler("Der andere Elternteil muss ein Partner oder «anderer Elternteil» dieses Mitarbeiters sein.");
            ziel.AndererElternteilId = aeId;
            ziel.GemeinsamesKindMitPartner = ae.MemberType == "Konkubinatspartner";
        }
        else
        {
            // Ohne verknüpften Elternteil: «nicht erfasst» schickt die Maske als
            // gemeinsames Kind = nein, «offen» als NULL.
            ziel.AndererElternteilId = null;
            if (kind) ziel.GemeinsamesKindMitPartner = dto.GemeinsamesKindMitPartner;
        }

        var se = Leer(dto.SeNeAngemeldet)?.ToUpperInvariant();
        if (se != null && se is not ("NEIN" or "SE" or "NE")) return Fehler("SE/NE: unbekannter Wert.");
        if (dto.Arbeitspensum is < 0 or > 100) return Fehler("Arbeitspensum muss zwischen 0 und 100 % liegen.");
        ziel.Arbeitspensum = elternteil ? dto.Arbeitspensum : null;
        ziel.ArbeitgeberTelefon = elternteil ? Leer(dto.ArbeitgeberTelefon) : null;
        ziel.EinkommenUeberMinimum = elternteil ? dto.EinkommenUeberMinimum : null;
        ziel.SeNeAngemeldet = elternteil ? se : null;
        ziel.SeNeAusgleichskasse = elternteil && se is "SE" or "NE" ? Leer(dto.SeNeAusgleichskasse) : null;
        ziel.BeziehtLeistung = elternteil ? dto.BeziehtLeistung : null;
        ziel.LeistungVon = elternteil && dto.BeziehtLeistung == true ? Leer(dto.LeistungVon) : null;
        return null;
    }

    /// <summary>
    /// Schutz: nimmt AlternativeAddressId nur an, wenn die referenzierte
    /// Zusatzadresse tatsächlich zum selben MA gehört. Sonst NULL.
    /// </summary>
    private async Task<int?> ValidateAlternativeAddressAsync(int employeeId, int? altId)
    {
        if (!altId.HasValue) return null;
        var ok = await _context.EmployeeAddresses
            .AnyAsync(a => a.Id == altId.Value && a.EmployeeId == employeeId);
        return ok ? altId : null;
    }

    // DELETE /api/employees/{employeeId}/family/{id}
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int employeeId, int id)
    {
        var member = await _context.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);

        if (member == null) return NotFound();

        _context.EmployeeFamilyMembers.Remove(member);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Walter-Vorgabe 13.06.2026: Beleg-Dokument für dieses Familienmitglied
    /// verknüpfen oder aufheben. Wird vor allem für den Spouse-Doku-Check
    /// in QstPflichtCheckService genutzt (Ehepartner-CH / Ehepartner-C).
    /// PATCH /api/employees/{employeeId}/family/{id}/dokument
    /// Body: { dokumentId: int | null }
    /// </summary>
    [HttpPatch("{id:int}/dokument")]
    public async Task<IActionResult> SetDokument(int employeeId, int id, [FromBody] FamilyMemberDokumentDto dto)
    {
        var member = await _context.EmployeeFamilyMembers
            .FirstOrDefaultAsync(m => m.Id == id && m.EmployeeId == employeeId);
        if (member == null) return NotFound();

        if (dto.DokumentId.HasValue)
        {
            var dokOk = await _context.EmployeeDokumente
                .AnyAsync(d => d.Id == dto.DokumentId.Value && d.EmployeeId == employeeId);
            if (!dokOk)
                return BadRequest(new { error = "DOKUMENT_INVALID",
                    message = "Das verlinkte Dokument gehört nicht zu diesem Mitarbeiter." });
        }

        // Art (Walter 23.09.2026): «geburtsurkunde» → eigenes Feld; sonst wie
        // bisher der Ausweis/Beleg (beim Ehepartner zugleich QST-Beleg).
        if (string.Equals(dto.Art, "geburtsurkunde", StringComparison.OrdinalIgnoreCase))
            member.GeburtsurkundeDokumentId = dto.DokumentId;
        else
            member.DokumentId = dto.DokumentId;
        member.UpdatedAt  = DateTime.Now;
        await _context.SaveChangesAsync();
        return Ok(new { id = member.Id, dokumentId = member.DokumentId, geburtsurkundeDokumentId = member.GeburtsurkundeDokumentId });
    }

    public class FamilyMemberDokumentDto
    {
        public int? DokumentId { get; set; }
        /// <summary>NULL/«ausweis» = Ausweis-Beleg, «geburtsurkunde» = Geburtsurkunde.</summary>
        public string? Art { get; set; }
    }
}
