using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services.Vorsystem;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Import des Mirus-Lohnkontos (Walter 04.10.2026) in <c>vorsystem_lohnkonto</c>.
/// Onboarding «Neue Filiale importieren»: liefert die Monate vor OneCrew für den
/// Vergleich OneCrew ↔ Mirus und für unterjährige Starts. Schreibt keine Lohndaten
/// von OneCrew (kein Snapshot, kein Saldo) → kein LohnEditLock nötig.
/// Ersetzt beim Import alle Werte der Filiale für die Monate, die die Datei enthält.
/// </summary>
[Authorize(Roles = "admin,superuser")]
[ApiController]
[Route("api/mirus-lohnkonto-import")]
public class MirusLohnkontoImportController : ControllerBase
{
    private readonly AppDbContext _db;
    public MirusLohnkontoImportController(AppDbContext db) { _db = db; }

    public record Kandidat(int Id, string FirstName, string LastName, string? EmployeeNumber);

    public record PersonZeile(
        string Key, string Name, string? Personalnummer, string? Eintritt, string? Austritt,
        string? Geburtsdatum, string Status, int? EmployeeId, string? EmployeeName,
        string? EmployeeNumber, int MonateMitLohn, decimal Brutto, decimal Netto);

    public record MonatKontrolle(int Jahr, int Monat, decimal BruttoPersonen, decimal BruttoTotal,
        decimal NettoPersonen, decimal NettoTotal, bool Ok, int BereitsImportiert);

    private sealed record Treffer(string Status, Employee? Emp);

    [HttpPost("analyze")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Analyze([FromQuery] int companyProfileId, [FromForm] IFormFile file)
    {
        var (erg, fehler) = Lies(file);
        if (erg == null) return BadRequest(new { error = fehler });

        var cp = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyProfileId);
        if (cp == null) return NotFound(new { error = "Filiale nicht gefunden." });

        var emps = await LadeMaAsync();
        var personen = erg.Personen.Where(p => !p.IstTotal).ToList();
        var total = erg.Personen.Where(p => p.IstTotal).ToList();

        var zeilen = personen.Select(p =>
        {
            var t = Zuordnen(p, emps);
            var brutto = Summe(new[] { p }, "250.1");
            var netto = Summe(new[] { p }, "1000.1");
            var monate = p.Zeilen.Where(z => z.Sektion == "AN" && z.Code == "250.1")
                .SelectMany(z => z.Werte).Count(w => w.Betrag != 0);
            return new PersonZeile(p.Key, p.Name, p.Personalnummer, p.Eintritt, p.Austritt, p.Geburtsdatum,
                t.Status, t.Emp?.Id, t.Emp == null ? null : $"{t.Emp.FirstName} {t.Emp.LastName}".Trim(),
                t.Emp?.EmployeeNumber, monate, brutto, netto);
        }).ToList();

        var vorhanden = await _db.VorsystemLohnkonten.AsNoTracking()
            .Where(v => v.CompanyProfileId == companyProfileId)
            .GroupBy(v => new { v.Jahr, v.Monat })
            .Select(g => new { g.Key.Jahr, g.Key.Monat, Anzahl = g.Select(x => x.EmployeeId).Distinct().Count() })
            .ToListAsync();

        var kontrolle = erg.Monate.Select(m =>
        {
            decimal bp = Summe(personen, "250.1", m), bt = Summe(total, "250.1", m);
            decimal np = Summe(personen, "1000.1", m), nt = Summe(total, "1000.1", m);
            int schon = vorhanden.FirstOrDefault(v => v.Jahr == m.Jahr && v.Monat == m.Monat)?.Anzahl ?? 0;
            return new MonatKontrolle(m.Jahr, m.Monat, bp, bt, np, nt, total.Count == 0 || (bp == bt && np == nt), schon);
        }).ToList();

        string? filialWarnung = null;
        if (!GleicherRestaurantCode(erg.RestaurantCode, cp.RestaurantCode))
            filialWarnung = $"Die Datei gehört zu Restaurant {erg.RestaurantCode ?? "?"}, gewählt ist {cp.BranchName ?? cp.CompanyName} (Restaurant {cp.RestaurantCode ?? "?"}).";

        var kandidaten = await _db.Employees.AsNoTracking()
            .Where(e => e.Employments.Any(x => x.CompanyProfileId == companyProfileId))
            .OrderBy(e => e.FirstName).ThenBy(e => e.LastName)
            .Select(e => new Kandidat(e.Id, e.FirstName, e.LastName, e.EmployeeNumber))
            .ToListAsync();

        return Ok(new
        {
            companyProfileId,
            restaurantCode = erg.RestaurantCode,
            filialWarnung,
            totalGefunden = total.Count > 0,
            monate = kontrolle,
            personen = zeilen,
            kandidaten,
            warnungen = erg.Warnungen
        });
    }

    [HttpPost("commit")]
    [Authorize(Roles = "admin")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> Commit([FromQuery] int companyProfileId, [FromForm] IFormFile file,
                                            [FromForm] string? zuordnung)
    {
        var (erg, fehler) = Lies(file);
        if (erg == null) return BadRequest(new { error = fehler });

        var cp = await _db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == companyProfileId);
        if (cp == null) return NotFound(new { error = "Filiale nicht gefunden." });
        if (!GleicherRestaurantCode(erg.RestaurantCode, cp.RestaurantCode))
            return BadRequest(new { error = "FALSCHE_FILIALE",
                message = $"Die Datei gehört zu Restaurant {erg.RestaurantCode ?? "?"} — bitte die passende Filiale im Hauptmenü wählen." });

        Dictionary<string, int> manuell = new();
        if (!string.IsNullOrWhiteSpace(zuordnung))
        {
            try { manuell = JsonSerializer.Deserialize<Dictionary<string, int>>(zuordnung) ?? new(); }
            catch { return BadRequest(new { error = "Zuordnung ungültig." }); }
        }

        var emps = await LadeMaAsync();
        var gueltigeIds = emps.Select(e => e.Id).ToHashSet();
        var summen = new Dictionary<(int Emp, string Sek, string Code, int J, int M), (string Bez, decimal Betrag)>();
        var uebersprungen = new List<string>();
        var importiert = new HashSet<int>();

        foreach (var p in erg.Personen.Where(p => !p.IstTotal))
        {
            int? empId = manuell.TryGetValue(p.Key, out var m) && m > 0 ? m : Zuordnen(p, emps).Emp?.Id;
            if (empId is not int id || !gueltigeIds.Contains(id))
            {
                uebersprungen.Add($"{p.Name} ({p.Personalnummer})");
                continue;
            }
            importiert.Add(id);
            foreach (var w in MirusLohnkontoParser.Werte(p))
            {
                var k = (id, w.Sektion, w.Code, w.Jahr, w.Monat);
                summen[k] = summen.TryGetValue(k, out var alt)
                    ? (alt.Bez, alt.Betrag + w.Betrag)
                    : (w.Bezeichnung, w.Betrag);
            }
        }

        var monatsSchluessel = erg.Monate.Select(x => x.Jahr * 100 + x.Monat).ToList();
        var von = await AkteurAsync();
        var jetzt = DateTime.Now;

        await using var tx = await _db.Database.BeginTransactionAsync();
        var geloescht = await _db.VorsystemLohnkonten
            .Where(v => v.CompanyProfileId == companyProfileId && monatsSchluessel.Contains(v.Jahr * 100 + v.Monat))
            .ExecuteDeleteAsync();
        _db.VorsystemLohnkonten.AddRange(summen.Where(s => s.Value.Betrag != 0).Select(s => new VorsystemLohnkonto
        {
            EmployeeId = s.Key.Emp, CompanyProfileId = companyProfileId,
            Jahr = s.Key.J, Monat = s.Key.M, Sektion = s.Key.Sek, Code = s.Key.Code,
            Bezeichnung = s.Value.Bez, Betrag = s.Value.Betrag,
            Quelle = "MIRUS", Dateiname = file.FileName, ImportiertAm = jetzt, ImportiertVon = von
        }));
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return Ok(new
        {
            personen = importiert.Count,
            zeilen = summen.Count(s => s.Value.Betrag != 0),
            ersetzt = geloescht,
            uebersprungen
        });
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] int companyProfileId)
    {
        var rows = await _db.VorsystemLohnkonten.AsNoTracking()
            .Where(v => v.CompanyProfileId == companyProfileId)
            .GroupBy(v => new { v.Jahr, v.Monat })
            .Select(g => new
            {
                g.Key.Jahr, g.Key.Monat,
                Personen = g.Select(x => x.EmployeeId).Distinct().Count(),
                Brutto = g.Where(x => x.Sektion == "AN" && x.Code == "250.1").Sum(x => x.Betrag),
                ImportiertAm = g.Max(x => x.ImportiertAm),
                Dateiname = g.Max(x => x.Dateiname)
            })
            .OrderBy(x => x.Jahr).ThenBy(x => x.Monat)
            .ToListAsync();
        return Ok(rows);
    }

    [HttpDelete]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Loeschen([FromQuery] int companyProfileId, [FromQuery] int jahr)
    {
        var n = await _db.VorsystemLohnkonten
            .Where(v => v.CompanyProfileId == companyProfileId && v.Jahr == jahr)
            .ExecuteDeleteAsync();
        return Ok(new { geloescht = n });
    }

    // ── Hilfen ────────────────────────────────────────────────────────────

    private static (MirusLohnkontoParser.Ergebnis? Erg, string? Fehler) Lies(IFormFile? file)
    {
        if (file == null || file.Length == 0) return (null, "Datei fehlt.");
        try
        {
            using var s = file.OpenReadStream();
            var erg = MirusLohnkontoParser.Lies(s);
            if (erg.Personen.All(p => p.IstTotal))
                return (null, "Keine Personen gefunden — ist das ein Mirus-Lohnkonto (Mitarbeiterbezogen)?");
            return (erg, null);
        }
        catch (InvalidDataException ex) { return (null, ex.Message); }
        catch (Exception ex) { return (null, "Datei konnte nicht gelesen werden: " + ex.Message); }
    }

    private Task<List<Employee>> LadeMaAsync()
        => _db.Employees.AsNoTracking().ToListAsync();

    private static Treffer Zuordnen(MirusLohnkontoParser.Person p, List<Employee> emps)
    {
        var pnr = (p.Personalnummer ?? "").Trim();
        if (pnr.Length > 0)
        {
            var t = emps.Where(e => (e.EmployeeNumber ?? "").Trim() == pnr).ToList();
            if (t.Count == 1) return new("PNR", t[0]);
        }
        var ahv = Ziffern(p.Versichertennummer);
        if (ahv.Length == 13)
        {
            var t = emps.Where(e => Ziffern(e.SocialSecurityNumber) == ahv).ToList();
            if (t.Count == 1) return new("AHV", t[0]);
        }
        var tokens = Tokens(p.Name);
        var gleicherName = emps.Where(e => tokens.SetEquals(Tokens($"{e.FirstName} {e.LastName}"))).ToList();
        if (DateTime.TryParseExact(p.Geburtsdatum, "dd.MM.yyyy", null, System.Globalization.DateTimeStyles.None, out var geb))
        {
            var t = gleicherName.Where(e => e.DateOfBirth?.Date == geb.Date).ToList();
            if (t.Count == 1) return new("NAME_GEB", t[0]);
        }
        if (gleicherName.Count == 1) return new("NAME", gleicherName[0]);
        return new("KEIN_MATCH", null);
    }

    private static string Ziffern(string? s) => Regex.Replace(s ?? "", @"\D", "");

    private static HashSet<string> Tokens(string s)
        => s.ToLowerInvariant().Split(new[] { ' ', '-', ',' }, StringSplitOptions.RemoveEmptyEntries).ToHashSet();

    private static bool GleicherRestaurantCode(string? datei, string? filiale)
    {
        if (string.IsNullOrWhiteSpace(datei) || string.IsNullOrWhiteSpace(filiale)) return true;
        return int.TryParse(Ziffern(datei), out var a) && int.TryParse(Ziffern(filiale), out var b) ? a == b : true;
    }

    private static decimal Summe(IEnumerable<MirusLohnkontoParser.Person> ps, string code, (int Jahr, int Monat)? monat = null)
        => ps.SelectMany(p => p.Zeilen.Where(z => z.Sektion == "AN" && z.Code == code))
             .SelectMany(z => z.Werte)
             .Where(w => monat == null || (w.Jahr == monat.Value.Jahr && w.Monat == monat.Value.Monat))
             .Sum(w => w.Betrag);

    private async Task<string?> AkteurAsync()
    {
        if (int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
        {
            var u = await _db.AppUsers.AsNoTracking().Where(x => x.Id == uid)
                .Select(x => new { x.FirstName, x.LastName, x.Username }).FirstOrDefaultAsync();
            if (u != null)
            {
                var n = $"{u.FirstName} {u.LastName}".Trim();
                return n.Length > 0 ? n : u.Username;
            }
        }
        return User.Identity?.Name;
    }
}
