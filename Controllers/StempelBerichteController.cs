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
    public async Task<IActionResult> VerstoessePdf([FromQuery] int companyProfileId, [FromQuery] string? from, [FromQuery] string? to)
    {
        var (fehler, daten) = await VerstoesseAsync(companyProfileId, from, to);
        if (fehler != null) return fehler;
        var bytes = _pdf.Verstoesse(daten!);
        return File(bytes, "application/pdf", $"Arbeitszeit-Verstoesse_{daten!.Von:yyyy-MM-dd}_{daten.Bis:yyyy-MM-dd}.pdf");
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

        var heute = DateOnly.FromDateTime(DateTime.Today);
        var vormonat = new DateOnly(heute.Year, heute.Month, 1).AddMonths(-1);
        var von = DateOnly.TryParse(from, out var f) ? f : vormonat;
        var bis = DateOnly.TryParse(to, out var t) ? t : vormonat.AddMonths(1).AddDays(-1);
        if (bis < von)
            return (BadRequest(new { error = "ZEITRAUM", message = "«Bis» liegt vor «Von»." }), null);
        if (bis > von.AddYears(1))
            return (BadRequest(new { error = "ZEITRAUM", message = "Höchstens ein Jahr auf einmal." }), null);

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

        var ladeVon = r.Von.AddDays(-7);
        var ladeBis = r.Bis.AddDays(7);
        var stempel = (await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => ids.Contains(t.EmployeeId) && t.TimeOut != null
                         && t.EntryDate >= ladeVon && t.EntryDate <= ladeBis)
                .Select(t => new { t.EmployeeId, t.EntryDate, t.TimeIn, t.TimeOut })
                .ToListAsync())
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => g.Select(t => new V.Stempel(t.EntryDate, t.TimeIn, t.TimeOut!.Value)).ToList());

        var nachtVon = r.Von.AddDays(-V.NaechteFensterTage);
        var naechte = (await _db.EmployeeTimeEntries.AsNoTracking()
                .Where(t => ids.Contains(t.EmployeeId) && t.NightHours > 0
                         && t.EntryDate >= nachtVon && t.EntryDate <= r.Bis)
                .Select(t => new { t.EmployeeId, t.EntryDate })
                .ToListAsync())
            .GroupBy(t => t.EmployeeId)
            .ToDictionary(g => g.Key, g => (IReadOnlyCollection<DateOnly>)g.Select(t => t.EntryDate).Distinct().ToList());

        var gruppen = new List<StempelVerstossMa>();
        foreach (var m in NachVorname(ma.Values))
        {
            var person = new V.Person(m.Id, m.Geburt,
                stempel.TryGetValue(m.Id, out var st) ? st : new List<V.Stempel>(),
                naechte.TryGetValue(m.Id, out var n) ? n : Array.Empty<DateOnly>());
            var liste = V.Pruefe(person, r.Von, r.Bis);
            if (liste.Count == 0) continue;
            gruppen.Add(new StempelVerstossMa(m.Id, m.Nummer, m.Vorname, m.Nachname,
                liste.Select(v => new StempelVerstossZeile(v.Art, V.Titel[v.Art], v.Von, v.Bis, v.Text,
                    v.Ist, v.Grenze, v.Recht,
                    v.Stempel.OrderBy(s => s.Ein).Select(s => ZeileVon(s)).ToList())).ToList()));
        }

        var proArt = V.Reihenfolge
            .Select(a => new StempelVerstossArt(a, V.Titel[a], V.Regel[a],
                gruppen.Sum(g => g.Verstoesse.Count(v => v.Art == a))))
            .ToList();
        return (null, new StempelVerstoesseDaten(r.Filiale, r.Von, r.Bis, ids.Count, proArt, gruppen));
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
        int total = await basis.CountAsync();
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
            gruppen.Sum(g => g.Zeilen.Count), proBearbeiter, gruppen));
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
