using HrSystem.Data;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using V = HrSystem.Services.ArbeitszeitVerstoesse;

namespace HrSystem.Controllers;

/// <summary>
/// McAdmin-Berichte aus den Stempelzeiten (Walter 07.10.2026):
/// «Korrekturen» (von Hand geänderte/erfasste Stempel) und «Verstösse»
/// (Arbeitszeitgesetz/L-GAV, Regeln in <see cref="ArbeitszeitVerstoesse"/>). Nur lesend.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superuser,user")]
[Route("api/reports")]
public class StempelBerichteController : HrControllerBase
{
    private readonly StempelBerichtPdfService _pdf;

    public StempelBerichteController(AppDbContext db, StempelBerichtPdfService pdf) : base(db)
    {
        _pdf = pdf;
    }

    [HttpGet("stempel-verstoesse")]
    public async Task<IActionResult> Verstoesse([FromQuery] int companyProfileId, [FromQuery] string? from, [FromQuery] string? to)
    {
        var (fehler, daten) = await VerstoesseAsync(companyProfileId, from, to);
        return fehler ?? Ok(daten);
    }

    [HttpGet("stempel-verstoesse/pdf")]
    public async Task<IActionResult> VerstoessePdf([FromQuery] int companyProfileId, [FromQuery] string? from, [FromQuery] string? to,
        [FromQuery] int? managerId)
    {
        var (fehler, daten) = await VerstoesseAsync(companyProfileId, from, to);
        if (fehler != null) return fehler;
        daten = daten!.FuerManager(managerId);
        var bytes = _pdf.Verstoesse(daten);
        return File(bytes, "application/pdf", $"Arbeitszeit-Verstoesse_{daten.Von:yyyy-MM-dd}_{daten.Bis:yyyy-MM-dd}.pdf");
    }

    [HttpGet("stempel-korrekturen")]
    public async Task<IActionResult> Korrekturen([FromQuery] int companyProfileId, [FromQuery] string? from, [FromQuery] string? to)
    {
        var (fehler, daten) = await KorrekturenAsync(companyProfileId, from, to);
        return fehler ?? Ok(daten);
    }

    [HttpGet("stempel-korrekturen/pdf")]
    public async Task<IActionResult> KorrekturenPdf([FromQuery] int companyProfileId, [FromQuery] string? from, [FromQuery] string? to)
    {
        var (fehler, daten) = await KorrekturenAsync(companyProfileId, from, to);
        if (fehler != null) return fehler;
        var bytes = _pdf.Korrekturen(daten!);
        return File(bytes, "application/pdf", $"Stempel-Korrekturen_{daten!.Von:yyyy-MM-dd}_{daten.Bis:yyyy-MM-dd}.pdf");
    }

    /// <summary>
    /// Arbeitsmuster für die Dienstplanung (Walter 15.10.2026): wer arbeitet an welchem
    /// Wochentag zu welcher Stunde, aus den Stempeln DIESER Filiale. Standard = letzte 3 Monate bis gestern.
    /// </summary>
    [HttpGet("stempel-muster")]
    public async Task<IActionResult> Muster([FromQuery] int companyProfileId, [FromQuery] string? from, [FromQuery] string? to)
    {
        var gestern = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        var (fehler, r) = await RahmenAsync(companyProfileId,
            from ?? gestern.AddMonths(-3).AddDays(1).ToString("yyyy-MM-dd"),
            to ?? gestern.ToString("yyyy-MM-dd"));
        if (fehler != null) return fehler;

        var stempel = (await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => t.SourceCompanyProfileId == r!.Cp && t.TimeOut != null
                         && t.EntryDate >= r.Von.AddDays(-1) && t.EntryDate <= r.Bis)
                .Select(t => new { t.EmployeeId, t.TimeIn, t.TimeOut })
                .ToListAsync())
            .Select(t => new ArbeitsMuster.Stempel(t.EmployeeId, t.TimeIn, t.TimeOut!.Value))
            .ToList();
        var ma = await MitarbeiterAsync(stempel.Select(s => s.EmployeeId));
        var ergebnis = ArbeitsMuster.Rechne(stempel.Where(s => ma.ContainsKey(s.EmployeeId)), r!.Von, r.Bis);
        var proMa = ergebnis.Mitarbeiter.ToDictionary(m => m.EmployeeId);

        return Ok(new
        {
            filiale = r.Filiale,
            von = r.Von.ToString("yyyy-MM-dd"),
            bis = r.Bis.ToString("yyyy-MM-dd"),
            wochentagAnzahl = ergebnis.WochentagAnzahl,
            raster = ergebnis.Raster.Select(tag => tag.Select(z => new
            {
                besetzung = z.Besetzung,
                ma = z.Mitarbeiter.Select(m => new { id = m.EmployeeId, anteil = m.Anteil }),
            })),
            mitarbeiter = NachVorname(ma.Values.Where(m => proMa.ContainsKey(m.Id))).Select(m => new
            {
                id = m.Id,
                nummer = m.Nummer,
                vorname = m.Vorname,
                nachname = m.Nachname,
                stunden = proMa[m.Id].Stunden,
                stundenProWoche = proMa[m.Id].StundenProWoche,
                stundenProWochentag = proMa[m.Id].StundenProWochentag,
                hauptzeiten = proMa[m.Id].Hauptzeiten,
            }),
        });
    }

    // ── gemeinsame Vorbereitung ──────────────────────────────────────────

    record Rahmen(int Cp, string Filiale, DateOnly Von, DateOnly Bis, List<int> VertragIds);

    async Task<(IActionResult? Fehler, Rahmen? R)> RahmenAsync(int cp, string? from, string? to)
    {
        if (cp <= 0)
            return (BadRequest(new { error = "FILIALE_FEHLT", message = "Bitte oben links eine Filiale wählen." }), null);
        if (!await CanAccessBranchAsync(cp))
            return (Forbid(), null);
        var profil = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cp);
        if (profil == null)
            return (NotFound(new { error = "FILIALE_NICHT_GEFUNDEN", message = "Filiale nicht gefunden." }), null);

        var (zeitFehler, von, bis) = Zeitraum(from, to);
        if (zeitFehler != null) return (zeitFehler, null);

        var vonDt = von.ToDateTime(TimeOnly.MinValue);
        var bisDt = bis.ToDateTime(TimeOnly.MinValue);
        var vertragIds = await _db.Employments.AsNoTracking()
            .Where(em => em.CompanyProfileId == cp && em.ContractStartDate <= bisDt
                      && (em.ContractEndDate == null || em.ContractEndDate >= vonDt))
            .Select(em => em.EmployeeId).Distinct().ToListAsync();

        var titel = string.Join(" ", new[] { profil.RestaurantCode, profil.City }
            .Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
        if (titel.Length == 0) titel = profil.BranchName ?? "";
        return (null, new Rahmen(cp, titel, von, bis, vertragIds));
    }

    (IActionResult? Fehler, DateOnly Von, DateOnly Bis) Zeitraum(string? from, string? to)
    {
        var heute = DateOnly.FromDateTime(DateTime.Today);
        var vormonat = new DateOnly(heute.Year, heute.Month, 1).AddMonths(-1);
        var von = DateOnly.TryParse(from, out var f) ? f : vormonat;
        var bis = DateOnly.TryParse(to, out var t) ? t : vormonat.AddMonths(1).AddDays(-1);
        if (bis < von)
            return (BadRequest(new { error = "ZEITRAUM", message = "«Bis» liegt vor «Von»." }), von, bis);
        if (bis > von.AddYears(1))
            return (BadRequest(new { error = "ZEITRAUM", message = "Höchstens ein Jahr auf einmal." }), von, bis);
        return (null, von, bis);
    }

    record MaKopf(int Id, string? Nummer, string? Vorname, string? Nachname, DateTime? Geburt);

    async Task<Dictionary<int, MaKopf>> MitarbeiterAsync(IEnumerable<int> ids)
    {
        var liste = ids.Distinct().ToList();
        return (await _db.Employees.AsNoTracking()
                .Where(e => liste.Contains(e.Id) && !e.IsHidden)
                .Select(e => new { e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth })
                .ToListAsync())
            .ToDictionary(e => e.Id, e => new MaKopf(e.Id, e.EmployeeNumber, e.FirstName, e.LastName, e.DateOfBirth));
    }

    static IOrderedEnumerable<MaKopf> NachVorname(IEnumerable<MaKopf> l) =>
        l.OrderBy(m => m.Vorname ?? "", StringComparer.OrdinalIgnoreCase)
         .ThenBy(m => m.Nachname ?? "", StringComparer.OrdinalIgnoreCase);

    // ── Verstösse ────────────────────────────────────────────────────────

    async Task<(IActionResult?, StempelVerstoesseDaten?)> VerstoesseAsync(int cp, string? from, string? to)
    {
        var (fehler, r) = await RahmenAsync(cp, from, to);
        if (fehler != null) return (fehler, null);

        var stempelIds = await _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => t.SourceCompanyProfileId == r!.Cp && t.EntryDate >= r.Von && t.EntryDate <= r.Bis)
            .Select(t => t.EmployeeId).Distinct().ToListAsync();
        var ma = await MitarbeiterAsync(stempelIds.Concat(r!.VertragIds));
        var ids = ma.Keys.ToList();

        var ladeVon = r.Von.AddDays(-14);
        var ladeBis = r.Bis.AddDays(7);
        var stempelRoh = await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => ids.Contains(t.EmployeeId) && t.TimeOut != null
                         && t.EntryDate >= ladeVon && t.EntryDate <= ladeBis)
                .Select(t => new { t.EmployeeId, t.EntryDate, t.TimeIn, t.TimeOut, t.SourceCompanyProfileId })
                .ToListAsync();
        var nachtCpIds = stempelRoh.Where(t => t.SourceCompanyProfileId != null)
            .Select(t => t.SourceCompanyProfileId!.Value).Distinct().ToList();
        var nachtFenster = (await _db.CompanyProfiles.AsNoTracking()
                .Where(c => nachtCpIds.Contains(c.Id))
                .Select(c => new { c.Id, c.NightStartTime, c.NightEndTime })
                .ToListAsync())
            .ToDictionary(c => c.Id, c => FilialNachtfenster.Aus(c.NightStartTime, c.NightEndTime));
        var stempel = stempelRoh
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(t =>
            {
                var (nVon, nBis) = t.SourceCompanyProfileId is int cpId && nachtFenster.TryGetValue(cpId, out var f)
                    ? f : FilialNachtfenster.Aus(null, null);
                return new V.Stempel(t.EntryDate, t.TimeIn, t.TimeOut!.Value, nVon, nBis);
            }).ToList());

        var nachtVon = r.Von.AddDays(-V.NaechteFensterTage);
        var naechte = (await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => ids.Contains(t.EmployeeId) && t.NightHours > 0
                         && t.EntryDate >= nachtVon && t.EntryDate <= r.Bis)
                .Select(t => new { t.EmployeeId, t.EntryDate })
                .ToListAsync())
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<DateOnly>)g.Select(t => t.EntryDate).Distinct().ToList());

        var nachweise = (await _db.Employees.AsNoTracking()
                .Where(e => ids.Contains(e.Id))
                .Select(e => new { e.Id, e.NightWorkExamDokumentId, e.NightWorkExamIssued, e.NightWorkExamValidUntil, e.NightWorkAusnahmeDokumentId })
                .ToListAsync())
            .ToDictionary(e => e.Id, e => new V.NachtNachweis(
                e.NightWorkExamDokumentId.HasValue,
                e.NightWorkExamIssued.HasValue ? DateOnly.FromDateTime(e.NightWorkExamIssued.Value) : null,
                e.NightWorkExamValidUntil.HasValue ? DateOnly.FromDateTime(e.NightWorkExamValidUntil.Value) : null,
                e.NightWorkAusnahmeDokumentId.HasValue));

        var schwanger = (await _db.EmployeePregnancies.AsNoTracking()
                .Where(p => ids.Contains(p.EmployeeId) && p.IsActive)
                .Select(p => new { p.EmployeeId, p.Meldedatum, p.ErrechneterTermin, p.Geburtsdatum })
                .ToListAsync())
            .GroupBy(p => p.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<V.Schwangerschaft>)g
                .Select(p => new V.Schwangerschaft(p.Meldedatum, p.ErrechneterTermin, p.Geburtsdatum)).ToList());

        var einst = await ArbeitszeitEinstellungenLader.FuerFilialeAsync(_db, r.Cp);
        var filialNacht = await _db.CompanyProfiles.AsNoTracking().Where(c => c.Id == r.Cp)
            .Select(c => new { c.KantonCode, c.NightStartTime, c.NightEndTime }).FirstOrDefaultAsync();
        var (berichtNachtVon, berichtNachtBis) = FilialNachtfenster.Aus(filialNacht?.NightStartTime, filialNacht?.NightEndTime);
        var kanton = filialNacht?.KantonCode;
        var sonntagsgleich = (await _db.DienstplanFeiertage.AsNoTracking()
                .Where(f => f.Datum >= r.Von && f.Datum <= r.Bis).ToListAsync())
            .Where(f => FeiertagVorschlag.GiltFuer(f, r.Cp, kanton?.Trim()) && FeiertagVorschlag.IstSonntagsgleich(f))
            .GroupBy(f => f.Datum)
            .ToDictionary(g => g.Key, g => g.First().Bezeichnung);

        var (managerAuswahl, managerDabei) = await ManagerDerFilialeAsync(r.Cp, r.Von, r.Bis, ladeVon, ladeBis);

        var gruppen = new List<StempelVerstossMa>();
        foreach (var m in NachVorname(ma.Values))
        {
            var person = new V.Person(m.Id, m.Geburt,
                stempel.TryGetValue(m.Id, out var st) ? st : new List<V.Stempel>(),
                naechte.TryGetValue(m.Id, out var n) ? n : Array.Empty<DateOnly>(),
                nachweise.GetValueOrDefault(m.Id),
                schwanger.GetValueOrDefault(m.Id));
            var liste = V.Pruefe(person, r.Von, r.Bis, einst, sonntagsgleich);
            if (liste.Count == 0) continue;
            gruppen.Add(new StempelVerstossMa(m.Id, m.Nummer, m.Vorname, m.Nachname,
                liste.Select(v =>
                {
                    // Nächte tragen keine Einzelstempel. Dann gelten die Schichten
                    // der Person im Zeitraum, damit der Manager der Nachtschicht bleibt.
                    IReadOnlyList<V.Stempel> quelle = v.Stempel.Count > 0
                        ? v.Stempel
                        : (st ?? new List<V.Stempel>()).Where(s => s.Tag >= v.Von && s.Tag <= v.Bis).ToList();
                    return new StempelVerstossZeile(v.Art, V.Titel(v.Art, einst), v.Von, v.Bis, v.Text,
                        v.Ist, v.Grenze, v.Recht,
                        v.Stempel.OrderBy(s => s.Ein).Select(s => ZeileVon(s)).ToList(),
                        managerDabei(quelle));
                }).ToList()));
        }

        var proArt = V.Reihenfolge.Where(einst.IstAktiv)
            .Select(a => new StempelVerstossArt(a, V.Titel(a, einst), V.Beschreibung(a, einst),
                gruppen.Sum(g => g.Verstoesse.Count(v => v.Art == a))))
            .ToList();
        var aus = V.Reihenfolge.Where(a => !einst.IstAktiv(a)).Select(a => V.Titel(a, einst)).ToList();
        static string Hhmm(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";
        return (null, new StempelVerstoesseDaten(r.Filiale, r.Von, r.Bis, ids.Count, proArt, gruppen, aus,
            Hhmm(berichtNachtVon), Hhmm(berichtNachtBis), managerAuswahl));
    }

    /// <summary>
    /// Schichtführung der Filiale im Zeitraum: FIX-M, und MTP mit Kader-Funktion
    /// (Restaurant- und Schichtführung). Gezählt wird, wer zur Stempelzeit des
    /// Verstosses selbst gestempelt hat — nicht der Dienstplan.
    /// </summary>
    async Task<(List<StempelManager> Auswahl, Func<IReadOnlyList<V.Stempel>, List<StempelManager>> Dabei)>
        ManagerDerFilialeAsync(int cp, DateOnly von, DateOnly bis, DateOnly ladeVon, DateOnly ladeBis)
    {
        var vonDt = von.ToDateTime(TimeOnly.MinValue);
        var bisDt = bis.ToDateTime(TimeOnly.MaxValue);
        var kader = await _db.JobGroups.AsNoTracking()
            .Where(g => g.IsKader).Select(g => g.Id).ToListAsync();
        var managerIds = await _db.Employments.AsNoTracking()
            .Where(em => em.CompanyProfileId == cp
                      && em.ContractStartDate <= bisDt
                      && (em.ContractEndDate == null || em.ContractEndDate >= vonDt)
                      && (em.EmploymentModel == "FIX-M"
                          || (em.EmploymentModel == "MTP" && em.JobGroupId != null && kader.Contains(em.JobGroupId.Value))))
            .Select(em => em.EmployeeId).Distinct().ToListAsync();
        var namen = (await _db.Employees.AsNoTracking()
                .Where(e => managerIds.Contains(e.Id) && !e.IsHidden)
                .Select(e => new { e.Id, e.FirstName, e.LastName })
                .ToListAsync())
            .Select(e => new StempelManager(e.Id, e.FirstName, e.LastName))
            .OrderBy(e => e.Vorname ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Nachname ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
        var bekannt = namen.Select(n => n.Id).ToList();
        var schichten = (await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => bekannt.Contains(t.EmployeeId) && t.SourceCompanyProfileId == cp && t.TimeOut != null
                         && t.EntryDate >= ladeVon && t.EntryDate <= ladeBis)
                .Select(t => new { t.EmployeeId, t.TimeIn, t.TimeOut })
                .ToListAsync())
            .Select(t => new ManagerSchicht.Schicht(t.EmployeeId, t.TimeIn, t.TimeOut!.Value))
            .ToList();
        var nachId = namen.ToDictionary(n => n.Id);

        List<StempelManager> Dabei(IReadOnlyList<V.Stempel> stempel)
        {
            var fenster = stempel.Select(s => new ManagerSchicht.Fenster(s.Ein, s.Aus)).ToList();
            return ManagerSchicht.Dabei(fenster, schichten)
                .Where(nachId.ContainsKey)
                .Select(id => nachId[id])
                .OrderBy(n => n.Vorname ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n.Nachname ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return (namen, Dabei);
    }

    static StempelZeit ZeileVon(V.Stempel s)
    {
        var basis = s.Tag.ToDateTime(TimeOnly.MinValue);
        return new StempelZeit(s.Tag, s.Ein.ToString("HH:mm"), s.Aus.ToString("HH:mm"),
            (int)Math.Round((s.Ein - basis).TotalMinutes), (int)Math.Round((s.Aus - basis).TotalMinutes), s.Minuten);
    }

    // ── Korrekturen ──────────────────────────────────────────────────────

    async Task<(IActionResult?, StempelKorrekturenDaten?)> KorrekturenAsync(int cp, string? from, string? to)
    {
        var (fehler, r) = await RahmenAsync(cp, from, to);
        if (fehler != null) return (fehler, null);
        var vertrag = r!.VertragIds;

        var basis = _db.EmployeeTimeEntries.AsNoTracking()
            .Where(t => t.EntryDate >= r.Von && t.EntryDate <= r.Bis
                     && (t.SourceCompanyProfileId == r.Cp
                         || (t.SourceCompanyProfileId == null && vertrag.Contains(t.EmployeeId))));
        var proTag = await basis.GroupBy(t => t.EntryDate)
            .Select(g => new { Tag = g.Key, Anzahl = g.Count() }).ToListAsync();
        int total = proTag.Sum(x => x.Anzahl);
        var proMonat = proTag.GroupBy(x => x.Tag.ToString("yyyy-MM"))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Anzahl));
        var roh = await basis
            .Where(t => t.EditedBy != null || t.OriginalTimeIn != null || t.OriginalTimeOut != null
                     || (t.Comment != null && t.Comment != ""))
            .Select(t => new { t.Id, t.EmployeeId, t.EntryDate, t.TimeIn, t.TimeOut, t.OriginalTimeIn,
                               t.OriginalTimeOut, t.EditedBy, t.EditedAt, t.Comment, t.OriginalComment })
            .ToListAsync();

        var ma = await MitarbeiterAsync(roh.Select(x => x.EmployeeId));
        var zh = Zuerich();
        var gruppen = new List<StempelKorrekturMa>();
        foreach (var m in NachVorname(ma.Values))
        {
            var zeilen = roh.Where(x => x.EmployeeId == m.Id).OrderBy(x => x.TimeIn).Select(x =>
            {
                bool zeit = (x.OriginalTimeIn.HasValue && !GleicheMinute(x.OriginalTimeIn, x.TimeIn))
                         || (x.OriginalTimeOut.HasValue && !GleicheMinute(x.OriginalTimeOut, x.TimeOut));
                var protokoll = Protokoll(x.OriginalComment, x.Comment);
                string art = zeit ? "ZEIT"
                    : (protokoll ?? "").Contains("anuell", StringComparison.OrdinalIgnoreCase) ? "MANUELL"
                    : x.EditedBy != null ? "BEARBEITET" : "KOMMENTAR";
                DateTime? am = x.EditedAt is { } e
                    ? (e.Kind == DateTimeKind.Utc ? TimeZoneInfo.ConvertTimeFromUtc(e, zh) : e) : null;
                return new StempelKorrekturZeile(x.Id, x.EntryDate, x.TimeIn.ToString("HH:mm"), x.TimeOut?.ToString("HH:mm"),
                    zeit ? x.OriginalTimeIn?.ToString("HH:mm") : null, zeit ? x.OriginalTimeOut?.ToString("HH:mm") : null,
                    art, x.EditedBy, am, string.IsNullOrWhiteSpace(x.Comment) ? null : x.Comment.Trim(), protokoll);
            }).ToList();
            gruppen.Add(new StempelKorrekturMa(m.Id, m.Nummer, m.Vorname, m.Nachname, zeilen));
        }

        var proBearbeiter = gruppen.SelectMany(g => g.Zeilen)
            .Where(z => !string.IsNullOrWhiteSpace(z.Von))
            .GroupBy(z => z.Von!)
            .Select(g => new StempelAnzahl(g.Key, g.Count()))
            .OrderByDescending(x => x.Anzahl).ThenBy(x => x.Name).ToList();
        return (null, new StempelKorrekturenDaten(r.Filiale, r.Von, r.Bis, total,
            gruppen.Sum(g => g.Zeilen.Count), proBearbeiter, gruppen, proMonat));
    }

    // ── Filialvergleich (HR-Hub → Auswertungen, Walter 08.10.2026) ───────
    // Rechnet pro Filiale exakt die beiden Einzelberichte — die Zahlen müssen
    // mit «Korrekturen Stempelzeiten» / «Arbeitszeit-Verstösse» übereinstimmen.

    [HttpGet("stempel-filialvergleich")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> Filialvergleich([FromQuery] string? from, [FromQuery] string? to)
    {
        var (fehler, daten) = await FilialvergleichAsync(from, to);
        return fehler ?? Ok(daten);
    }

    [HttpGet("stempel-filialvergleich/pdf")]
    [Authorize(Roles = "admin,superuser")]
    public async Task<IActionResult> FilialvergleichPdf([FromQuery] string? from, [FromQuery] string? to)
    {
        var (fehler, daten) = await FilialvergleichAsync(from, to);
        if (fehler != null) return fehler;
        var bytes = _pdf.Filialvergleich(daten!);
        return File(bytes, "application/pdf", $"Stempelzeiten-Filialvergleich_{daten!.Von:yyyy-MM-dd}_{daten.Bis:yyyy-MM-dd}.pdf");
    }

    async Task<(IActionResult? Fehler, StempelFilialvergleichDaten? Daten)> FilialvergleichAsync(string? from, string? to)
    {
        var (zeitFehler, von, bis) = Zeitraum(from, to);
        if (zeitFehler != null) return (zeitFehler, null);
        var vonIso = von.ToString("yyyy-MM-dd");
        var bisIso = bis.ToString("yyyy-MM-dd");

        var monate = new List<string>();
        for (var m = new DateOnly(von.Year, von.Month, 1); m <= bis; m = m.AddMonths(1))
            monate.Add(m.ToString("yyyy-MM"));

        var filialIds = await _db.CompanyProfiles.AsNoTracking()
            .Where(c => c.IsActive).Select(c => c.Id).ToListAsync();
        var zeilen = new List<StempelFilialZeile>();
        var ohneStempel = new List<string>();
        foreach (var id in filialIds)
        {
            if (!await CanAccessBranchAsync(id)) continue;
            var (fk, k) = await KorrekturenAsync(id, vonIso, bisIso);
            var (fv, v) = await VerstoesseAsync(id, vonIso, bisIso);
            if (fk != null || fv != null) continue;

            var korrekturen = k!.Mitarbeiter.SelectMany(m => m.Zeilen).ToList();
            var verstoesse = v!.Mitarbeiter.SelectMany(m => m.Verstoesse).ToList();
            if (k.StempelTotal == 0 && verstoesse.Count == 0)
            {
                ohneStempel.Add(k.Filiale);
                continue;
            }

            var aktiv = v.ProArt.Select(a => a.Art).ToHashSet();
            var proMonat = monate.Select(mo => new StempelFilialMonat(mo,
                k.StempelProMonat?.GetValueOrDefault(mo) ?? 0,
                korrekturen.Count(z => z.Tag.ToString("yyyy-MM") == mo),
                verstoesse.Count(x => x.Bis.ToString("yyyy-MM") == mo))).ToList();

            zeilen.Add(new StempelFilialZeile(id, k.Filiale, k.StempelTotal, k.Korrigiert, k.Mitarbeiter.Count,
                korrekturen.GroupBy(z => z.Art).ToDictionary(g => g.Key, g => g.Count()),
                v.AnzahlMa, verstoesse.Count, v.Mitarbeiter.Count,
                v.ProArt.ToDictionary(a => a.Art, a => a.Anzahl),
                V.Reihenfolge.Where(a => !aktiv.Contains(a)).ToList(),
                proMonat));
        }

        var arten = V.Reihenfolge.Select(a => new StempelFilialArt(a, V.Titel(a), V.Beschreibung(a))).ToList();
        return (null, new StempelFilialvergleichDaten(von, bis, arten,
            zeilen.OrderBy(z => z.Filiale, StringComparer.OrdinalIgnoreCase).ToList(), ohneStempel));
    }

    static bool GleicheMinute(DateTime? a, DateTime? b) =>
        a.HasValue && b.HasValue && Math.Abs((a.Value - b.Value).TotalSeconds) < 60;

    static string? Protokoll(string? original, string? kommentar)
    {
        if (string.IsNullOrWhiteSpace(original)) return null;
        var teile = original.Split(" / ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => string.IsNullOrWhiteSpace(kommentar) || !kommentar.Contains(t, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return teile.Count == 0 ? null : string.Join(" · ", teile);
    }

    static TimeZoneInfo Zuerich()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich"); }
        catch { return TimeZoneInfo.Local; }
    }
}
