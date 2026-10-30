using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NPOI.XSSF.UserModel;

namespace HrSystem.Controllers;

/// <summary>
/// AKISnet-Upload-Excel für GastroSocial (Walter-Vorgabe 06.08.2026):
/// MA-An-/Abmeldungen bei der Ausgleichskasse laufen über das AKIS-Portal
/// (akisnet.ch), das Excel-Sammeldateien akzeptiert. OneCrew füllt die
/// ORIGINAL-Vorlagen (Assets/Forms/Akis_*.xlsx, Daten ab Zeile 8) aus den
/// Ein-/Austritten der Filiale — hochladen bleibt manuell (kein API/ELM).
///
/// Anmeldung = Vertrag beginnt in der Filiale, am Vortag lief dort keiner
///             (nahtlose Anpassung = keine neue Anmeldung).
/// Abmeldung = Austritt mit «Gekündigt per», Filiale des letzten Vertrags,
///             oder Filialwechsel: letzter Vertragstag der alten Filiale,
///             sobald in einer anderen Filiale weitergearbeitet wird.
///             Befristet ohne Anschluss und ohne Kündigung per bleibt still.
/// Die Zeile erscheint erst am Folgetag. Jede Filiale ist ein eigenes Konto,
/// ausser die Mitgliednummer der Ausgleichskasse ist dieselbe: dann ist ein
/// Wechsel des Arbeitskantons (Kanton der Filiale) ein Kantonswechsel
/// am Beginn des neuen Arbeitsorts, ohne Ab- und ohne neue Anmeldung.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/akis-export")]
public class AkisExportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IWebHostEnvironment _env;
    public AkisExportController(AppDbContext db, IWebHostEnvironment env)
    {
        _db = db; _env = env;
    }

    // ── Kandidaten-Ermittlung ──────────────────────────────────────────

    private sealed record Row(int EmployeeId, string? Ahv, string Name, string Vorname,
        DateTime? GebDat, string? Geschlecht, DateTime Datum, string Sprache,
        string? AlterKanton = null, string? NeuerKanton = null);

    private async Task<(List<Row> rows, List<Row> ohneAhv)> AnmeldungenAsync(
        int cpId, DateTime from, DateTime to)
    {
        var vertraege = await VertraegeMitSchwesternAsync(cpId);
        if (vertraege.Count == 0) return (new List<Row>(), new List<Row>());
        var aus = GastroSocialMeldungRegel.Arbeitsort(vertraege, await GastroSocialFilialen.LadenAsync(_db));
        DateOnly von = DateOnly.FromDateTime(from), bis = DateOnly.FromDateTime(to);
        // Jede Anmeldung einzeln (Wiedereintritt im selben Jahr = zweite Zeile).
        // Gleicher Konto und anderer Arbeitskanton: das ist ein Kantonswechsel, keine Anmeldung.
        var kandidaten = GastroSocialMeldungRegel.Anmeldungen(vertraege)
            .Where(e => e.CompanyProfileId == cpId && e.Datum >= von && e.Datum <= bis && !aus.IstUnterdrueckt(e))
            .Select(e => (e.EmployeeId, e.Datum.ToDateTime(TimeOnly.MinValue)))
            .ToList();
        return await LoadRowsAsync(kandidaten);
    }

    /// <summary>
    /// Abmeldung am Folgetag: Firmenaustritt mit «Gekündigt per» (Filiale des letzten Vertrags)
    /// oder Filialwechsel (letzter Vertragstag der alten Filiale). Befristet ohne Anschluss bleibt draussen.
    /// </summary>
    private async Task<(List<Row> rows, List<Row> ohneAhv)> AbmeldungenAsync(
        int cpId, DateTime from, DateTime to)
    {
        // Der letzte Tag zählt noch: am 31.10. arbeitet der MA, ab 1.11. ist die Zeile fällig.
        var letzterTag = DateTime.Today.AddDays(-1);
        if (to.Date < letzterTag) letzterTag = to.Date;
        if (from.Date > letzterTag) return (new List<Row>(), new List<Row>());

        var idsInFiliale = await _db.Employments.AsNoTracking()
            .Where(e => e.CompanyProfileId == cpId)
            .Select(e => e.EmployeeId)
            .Distinct()
            .ToListAsync();
        if (idsInFiliale.Count == 0) return (new List<Row>(), new List<Row>());

        var alleVertraege = await _db.Employments.AsNoTracking()
            .Where(e => idsInFiliale.Contains(e.EmployeeId) && e.CompanyProfileId != null)
            .Select(e => new GastroSocialMeldungRegel.Vertrag(e.EmployeeId, e.CompanyProfileId, e.ContractStartDate, e.ContractEndDate))
            .ToListAsync();
        var austritte = (await _db.Employees.AsNoTracking()
            .Where(e => idsInFiliale.Contains(e.Id) && e.ExitDate != null)
            .Select(e => new { e.Id, e.ExitDate, e.KuendigungPer })
            .ToListAsync())
            .Select(e => new GastroSocialMeldungRegel.PersonAustritt(e.Id, e.ExitDate, e.KuendigungPer))
            .ToList();

        DateOnly von = DateOnly.FromDateTime(from.Date), bis = DateOnly.FromDateTime(letzterTag);
        var aus = GastroSocialMeldungRegel.Arbeitsort(alleVertraege, await GastroSocialFilialen.LadenAsync(_db));
        var kandidaten = GastroSocialMeldungRegel.Abmeldungen(alleVertraege, austritte)
            .Where(e => e.CompanyProfileId == cpId && e.Datum >= von && e.Datum <= bis && !aus.IstUnterdrueckt(e))
            .Select(e => (e.EmployeeId, e.Datum.ToDateTime(TimeOnly.MinValue)))
            .ToList();
        return await LoadRowsAsync(kandidaten);
    }

    /// <summary>
    /// Kantonswechsel der Filiale: gleiches Ausgleichskassen-Konto, anderer Arbeitskanton.
    /// Die Zeile hängt an der neuen Filiale, Datum = Beginn dort (auch künftig, wie die Anmeldung).
    /// </summary>
    private async Task<(List<Row> rows, List<Row> ohneAhv)> KantonswechselAsync(
        int cpId, DateTime from, DateTime to)
    {
        var vertraege = await VertraegeMitSchwesternAsync(cpId);
        if (vertraege.Count == 0) return (new List<Row>(), new List<Row>());
        var aus = GastroSocialMeldungRegel.Arbeitsort(vertraege, await GastroSocialFilialen.LadenAsync(_db));
        DateOnly von = DateOnly.FromDateTime(from), bis = DateOnly.FromDateTime(to);
        var faelle = aus.Kantonswechsel
            .Where(k => k.NeueFiliale == cpId && k.NeuerBeginn >= von && k.NeuerBeginn <= bis)
            .ToList();
        var nachZeile = faelle.ToDictionary(k => (k.EmployeeId, k.NeuerBeginn));
        var (rows, ohne) = await LoadRowsAsync(faelle
            .Select(k => (k.EmployeeId, k.NeuerBeginn.ToDateTime(TimeOnly.MinValue))).ToList());
        return (MitKanton(rows, nachZeile), MitKanton(ohne, nachZeile));
    }

    private static List<Row> MitKanton(List<Row> rows, Dictionary<(int, DateOnly), GastroSocialMeldungRegel.KantonswechselFall> nachZeile) =>
        rows.Select(r => nachZeile.TryGetValue((r.EmployeeId, DateOnly.FromDateTime(r.Datum)), out var k)
            ? r with { AlterKanton = k.AlterKanton, NeuerKanton = k.NeuerKanton } : r).ToList();

    /// <summary>Verträge der Filiale und aller anderen Filialen derselben Personen (für Konto und Kanton).</summary>
    private async Task<List<GastroSocialMeldungRegel.Vertrag>> VertraegeMitSchwesternAsync(int cpId)
    {
        var ids = await _db.Employments.AsNoTracking()
            .Where(e => e.CompanyProfileId == cpId)
            .Select(e => e.EmployeeId)
            .Distinct()
            .ToListAsync();
        if (ids.Count == 0) return new List<GastroSocialMeldungRegel.Vertrag>();
        return await _db.Employments.AsNoTracking()
            .Where(e => ids.Contains(e.EmployeeId) && e.CompanyProfileId != null)
            .Select(e => new GastroSocialMeldungRegel.Vertrag(e.EmployeeId, e.CompanyProfileId, e.ContractStartDate, e.ContractEndDate))
            .ToListAsync();
    }

    /// <summary>
    /// Info-Liste: befristete Verträge der Filiale, die im Zeitraum enden,
    /// OHNE Anschlussvertrag und OHNE erfassten Austritt — hier muss Walter
    /// entscheiden: verlängern oder Austritt erfassen.
    /// </summary>
    private async Task<List<Row>> BefristetOffenAsync(int cpId, DateTime from, DateTime to)
    {
        var emps = await _db.Employments.AsNoTracking()
            .Where(e => e.CompanyProfileId == cpId)
            .Select(e => new { e.EmployeeId, e.ContractStartDate, e.ContractEndDate })
            .ToListAsync();
        var byEmp = emps.GroupBy(e => e.EmployeeId).ToDictionary(g => g.Key, g => g.ToList());

        var kandidaten = new Dictionary<int, DateTime>();
        foreach (var (empId, list) in byEmp)
        {
            foreach (var c in list.Where(c => c.ContractEndDate != null
                                           && c.ContractEndDate >= from && c.ContractEndDate <= to))
            {
                var folgetag = c.ContractEndDate!.Value.AddDays(1);
                bool folgt = list.Any(o => o != c
                    && o.ContractStartDate <= folgetag
                    && (o.ContractEndDate == null || o.ContractEndDate > c.ContractEndDate));
                if (folgt) continue;
                if (!kandidaten.TryGetValue(empId, out var d) || c.ContractEndDate > d)
                    kandidaten[empId] = c.ContractEndDate!.Value;
            }
        }
        if (kandidaten.Count == 0) return new List<Row>();
        var ids = kandidaten.Keys.ToList();
        // Filialwechsel ist schon eine Abmeldung — nicht nochmals als «befristet, noch entscheiden».
        var alleFilialen = await _db.Employments.AsNoTracking()
            .Where(e => ids.Contains(e.EmployeeId) && e.CompanyProfileId != null)
            .Select(e => new GastroSocialMeldungRegel.Vertrag(e.EmployeeId, e.CompanyProfileId, e.ContractStartDate, e.ContractEndDate))
            .ToListAsync();
        var wechsel = GastroSocialMeldungRegel.AbmeldungenBeiFilialwechsel(alleFilialen)
            .Where(e => e.CompanyProfileId == cpId)
            .Select(e => (e.EmployeeId, e.Datum))
            .ToHashSet();
        // MA mit erfasstem Austritt raus (die sind echte Abmeldungen)
        var ohneExit = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && e.ExitDate == null)
            .Select(e => e.Id)
            .ToListAsync();
        var gefiltert = kandidaten
            .Where(k => ohneExit.Contains(k.Key)
                     && !wechsel.Contains((k.Key, DateOnly.FromDateTime(k.Value))))
            .Select(k => (k.Key, k.Value)).ToList();
        var (rows, ohneAhv) = await LoadRowsAsync(gefiltert);
        return rows.Concat(ohneAhv)
            .OrderBy(r => r.Vorname, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<(List<Row> rows, List<Row> ohneAhv)> LoadRowsAsync(List<(int EmpId, DateTime Datum)> kandidaten)
    {
        if (kandidaten.Count == 0) return (new List<Row>(), new List<Row>());
        var ids = kandidaten.Select(k => k.EmpId).Distinct().ToList();
        var people = await _db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.Id) && !e.IsPayrollExcluded && !e.IsHidden)
            .Select(e => new
            {
                e.Id, e.SocialSecurityNumber, e.FirstName, e.LastName,
                e.DateOfBirth, e.Gender, e.Salutation, e.LanguageCode,
            })
            .ToListAsync();

        var rows = new List<Row>();
        var personen = people.ToDictionary(p => p.Id);
        foreach (var (empId, datum) in kandidaten)
        {
            if (!personen.TryGetValue(empId, out var p)) continue;
            rows.Add(new Row(
                p.Id,
                string.IsNullOrWhiteSpace(p.SocialSecurityNumber) ? null : p.SocialSecurityNumber.Trim(),
                p.LastName ?? "", p.FirstName ?? "",
                p.DateOfBirth,
                MapGeschlecht(p.Gender, p.Salutation),
                datum,
                MapSprache(p.LanguageCode)));
        }
        // Sortierung Vorname (Projekt-Konvention)
        rows = rows.OrderBy(r => r.Vorname, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                   .ThenBy(r => r.Datum).ToList();
        return (rows.Where(r => r.Ahv != null).ToList(),
                rows.Where(r => r.Ahv == null).ToList());
    }

    private static string? MapGeschlecht(string? gender, string? salutation)
    {
        var g = (gender ?? "").Trim().ToLowerInvariant();
        if (g is "f" or "w" or "female" or "frau" or "weiblich") return "W";
        if (g is "m" or "male" or "mann" or "herr" or "männlich") return "M";
        var s = (salutation ?? "").Trim().ToLowerInvariant();
        if (s.StartsWith("frau")) return "W";
        if (s.StartsWith("herr")) return "M";
        return null;
    }

    private static string MapSprache(string? code) =>
        (code ?? "").Trim().ToLowerInvariant() switch
        {
            "fr" or "f" => "F",
            "it" or "i" => "I",
            "en" or "e" => "E",
            _           => "D",
        };

    // ── Zeitraum (Walter 13.10.2026: keine Datumswahl mehr) ─────────────
    // Gezeigt wird alles ab dem Start der Meldungs-Kontrolle (wie das To-do),
    // auch schon erfasste künftige Ein-/Austritte. Von/Bis nur noch optional.
    private static (DateTime von, DateTime bis) Zeitraum(DateTime? from, DateTime? to) =>
        ((from ?? GastroSocialMeldungRegel.ToDoAb.ToDateTime(TimeOnly.MinValue)).Date,
         (to ?? new DateTime(2099, 12, 31)).Date);

    private async Task<List<GastroSocialMeldung>> MeldungenAsync(int cpId) =>
        await _db.GastroSocialMeldungen.AsNoTracking()
            .Where(m => m.CompanyProfileId == cpId)
            .ToListAsync();

    /// <summary>Gemerkte Meldung, die diese Zeile abdeckt (±31 Tage), sonst null.</summary>
    private static GastroSocialMeldung? Gemeldet(Row r, int cpId, string art, List<GastroSocialMeldung> meldungen)
    {
        var treffer = GastroSocialMeldungRegel.Passende(
            new GastroSocialMeldungRegel.Ereignis(r.EmployeeId, cpId, art, DateOnly.FromDateTime(r.Datum)),
            meldungen.Select(m => new GastroSocialMeldungRegel.Meldung(m.EmployeeId, m.CompanyProfileId, m.Art, m.Stichtag)));
        return treffer == null ? null
            : meldungen.First(x => x.EmployeeId == treffer.EmployeeId && x.Art == treffer.Art && x.Stichtag == treffer.Stichtag);
    }

    // ── Vorschau (JSON für die UI) ─────────────────────────────────────

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(
        [FromQuery] int companyProfileId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var (von, bis) = Zeitraum(from, to);
        var (an, anOhne) = await AnmeldungenAsync(companyProfileId, von, bis);
        var (ab, abOhne) = await AbmeldungenAsync(companyProfileId, von, bis);
        var (kw, kwOhne) = await KantonswechselAsync(companyProfileId, von, bis);
        // Befristungen: ohne Zeitraum nur, was bald endet oder kürzlich endete.
        var befristet = await BefristetOffenAsync(companyProfileId,
            from?.Date ?? DateTime.Today.AddDays(-31), to?.Date ?? DateTime.Today.AddDays(60));
        // Gemerkte Meldungen (Walter 13.10.2026): pro Zeile «gemeldet am … von …».
        var meldungen = await MeldungenAsync(companyProfileId);
        object Map(Row r, string? art)
        {
            var m = art == null ? null : Gemeldet(r, companyProfileId, art, meldungen);
            return new
            {
                r.EmployeeId, ahv = r.Ahv, name = r.Name, vorname = r.Vorname,
                gebDat = r.GebDat?.ToString("dd.MM.yyyy"),
                geschlecht = r.Geschlecht,
                datum = r.Datum.ToString("dd.MM.yyyy"),
                datumIso = r.Datum.ToString("yyyy-MM-dd"),
                sprache = r.Sprache,
                alterKanton = r.AlterKanton,
                neuerKanton = r.NeuerKanton,
                gemeldet = m == null ? null : new
                {
                    m.Id, quelle = m.Quelle,
                    am = m.ErfasstAm.ToString("dd.MM.yyyy"),
                    von = m.ErfasstVon,
                    stichtag = m.Stichtag.ToString("dd.MM.yyyy"),
                },
            };
        }
        const string AN = GastroSocialMeldungRegel.Anmeldung, AB = GastroSocialMeldungRegel.Abmeldung;
        const string KW = GastroSocialMeldungRegel.Kantonswechsel;
        return Ok(new
        {
            anmeldungen = an.Select(r => Map(r, AN)),
            anmeldungenOhneAhv = anOhne.Select(r => Map(r, AN)),   // → zuerst AHV-Anmeldung 318.260!
            abmeldungen = ab.Select(r => Map(r, AB)),
            abmeldungenOhneAhv = abOhne.Select(r => Map(r, AB)),
            kantonswechsel = kw.Select(r => Map(r, KW)),
            kantonswechselOhneAhv = kwOhne.Select(r => Map(r, KW)),
            // Befristungen ohne erfassten Austritt: verlängern oder Austritt erfassen?
            befristetOffen = befristet.Select(r => Map(r, null)),
        });
    }

    // ── Excel-Erzeugung aus den ORIGINAL-Vorlagen ──────────────────────

    [HttpGet("anmeldung")]
    public async Task<IActionResult> AnmeldungXlsx(
        [FromQuery] int companyProfileId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? auswahl)
    {
        var (von, bis) = Zeitraum(from, to);
        var (alle, _) = await AnmeldungenAsync(companyProfileId, von, bis);
        // Nur, was noch nicht als gemeldet vermerkt ist — und ggf. nur die angekreuzten Zeilen.
        var meldungen = await MeldungenAsync(companyProfileId);
        var gewaehlt = Auswahl(auswahl);
        var rows = alle.Where(r => Gemeldet(r, companyProfileId, GastroSocialMeldungRegel.Anmeldung, meldungen) == null && gewaehlt(r)).ToList();
        var bytes = FillTemplate("Akis_AnmeldungMitarbeitende.xlsx", ws =>
        {
            int r = 7; // 0-basiert → Zeile 8
            foreach (var x in rows)
            {
                var row = ws.CreateRow(r++);
                row.CreateCell(0).SetCellValue(x.Ahv);
                row.CreateCell(1).SetCellValue(x.Name);
                row.CreateCell(2).SetCellValue(x.Vorname);
                row.CreateCell(3).SetCellValue(x.GebDat?.ToString("dd.MM.yyyy") ?? "");
                row.CreateCell(4).SetCellValue(x.Geschlecht ?? "");
                row.CreateCell(5).SetCellValue(x.Datum.ToString("dd.MM.yyyy"));
                row.CreateCell(6).SetCellValue(x.Sprache);
                // Spalte H (Duplikat Versicherungsausweis) bleibt leer.
            }
        });
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AnmeldungMitarbeitende_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    [HttpGet("abmeldung")]
    public async Task<IActionResult> AbmeldungXlsx(
        [FromQuery] int companyProfileId, [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] string? auswahl)
    {
        var (von, bis) = Zeitraum(from, to);
        var (alle, _) = await AbmeldungenAsync(companyProfileId, von, bis);
        var meldungen = await MeldungenAsync(companyProfileId);
        var gewaehlt = Auswahl(auswahl);
        var rows = alle.Where(r => Gemeldet(r, companyProfileId, GastroSocialMeldungRegel.Abmeldung, meldungen) == null && gewaehlt(r)).ToList();
        var bytes = FillTemplate("Akis_AbmeldungMitarbeitende.xlsx", ws =>
        {
            int r = 7;
            foreach (var x in rows)
            {
                var row = ws.CreateRow(r++);
                row.CreateCell(0).SetCellValue(x.Ahv);
                row.CreateCell(1).SetCellValue(x.Name);
                row.CreateCell(2).SetCellValue(x.Vorname);
                row.CreateCell(3).SetCellValue(x.Datum.ToString("dd.MM.yyyy"));
            }
        });
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"AbmeldungMitarbeitende_{DateTime.Today:yyyyMMdd}.xlsx");
    }

    /// <summary>
    /// Angekreuzte Zeilen (Walter 14.10.2026): «empId_yyyy-MM-dd» kommagetrennt.
    /// Fehlt der Parameter, gehen alle offenen Zeilen in die Excel.
    /// </summary>
    private static Func<Row, bool> Auswahl(string? auswahl)
    {
        if (auswahl == null) return _ => true;
        var set = auswahl.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
        return r => set.Contains($"{r.EmployeeId}_{r.Datum:yyyy-MM-dd}");
    }

    private byte[] FillTemplate(string templateFile, Action<NPOI.SS.UserModel.ISheet> fill)
    {
        var path = System.IO.Path.Combine(_env.ContentRootPath, "Assets", "Forms", templateFile);
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException($"Vorlage fehlt: {path}");
        using var fs = System.IO.File.OpenRead(path);
        var wb = new XSSFWorkbook(fs);
        var ws = wb.GetSheet("Mitarbeitende") ?? wb.GetSheetAt(0);
        fill(ws);
        // COUNTA-Formel (Anzahl MA) beim Öffnen neu rechnen lassen.
        wb.SetForceFormulaRecalculation(true);
        using var ms = new MemoryStream();
        wb.Write(ms, leaveOpen: true);
        return ms.ToArray();
    }
}
