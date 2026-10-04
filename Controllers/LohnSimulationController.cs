using System.Text.Json.Nodes;
using HrSystem.Data;
using HrSystem.Services.Vorsystem;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Controllers;

/// <summary>
/// Lohn-Simulation (Walter 04.10.2026): OneCrew rechnet die Monate vor dem ersten
/// echten Lohnlauf nach und vergleicht mit dem Mirus-Lohnkonto. Schreibt nur in
/// simulation_lohn — nie in Perioden, Lohnzettel, Saldi, Zulagen; kein Versand.
/// </summary>
[ApiController]
[Route("api/lohn-simulation")]
[Authorize(Roles = "admin,superuser")]
public class LohnSimulationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly LohnSimulationService _service;

    public LohnSimulationController(AppDbContext db, LohnSimulationService service)
    {
        _db = db;
        _service = service;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status([FromQuery] int companyProfileId, [FromQuery] int jahr)
    {
        var vortragMa = await _db.SimulationVortraege
            .Where(v => v.CompanyProfileId == companyProfileId)
            .Select(v => v.EmployeeId).Distinct().CountAsync();
        var lohnkontoMonate = await _db.VorsystemLohnkonten
            .Where(v => v.CompanyProfileId == companyProfileId && v.Jahr == jahr
                     && v.Sektion == "AN" && v.Code == "250.1" && v.Betrag != 0)
            .GroupBy(v => v.Monat)
            .Select(g => new { monat = g.Key, personen = g.Select(x => x.EmployeeId).Distinct().Count(), brutto = g.Sum(x => x.Betrag) })
            .OrderBy(x => x.monat)
            .ToListAsync();
        var simMonate = await _db.SimulationLoehne
            .Where(z => z.CompanyProfileId == companyProfileId && z.Jahr == jahr)
            .GroupBy(z => z.Monat)
            .Select(g => new
            {
                monat = g.Key,
                personen = g.Count(),
                mitFehler = g.Count(x => x.Fehler != null),
                brutto = g.Sum(x => x.Brutto),
                berechnetAm = g.Max(x => x.BerechnetAm),
            })
            .OrderBy(x => x.monat)
            .ToListAsync();
        return Ok(new { vortragMa, lohnkontoMonate, simMonate });
    }

    [HttpPost("rechnen")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Rechnen([FromQuery] int companyProfileId, [FromQuery] int jahr, [FromQuery] int monat)
    {
        if (monat < 1 || monat > 12) return BadRequest(new { error = "Monat ungültig." });
        var r = await _service.RechneMonatAsync(companyProfileId, jahr, monat);
        return Ok(r);
    }

    [HttpDelete]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Verwerfen([FromQuery] int companyProfileId, [FromQuery] bool mitVortrag = false)
    {
        var (loehne, vortraege) = await _service.VerwerfenAsync(companyProfileId, mitVortrag);
        return Ok(new { loehne, vortraege });
    }

    /// <summary>
    /// Brutto / Netto / Auszahlung pro MA und Monat: Simulation (Ist) gegen Mirus (Soll).
    /// Mirus: 250.1 Bruttolohn, 1000.1 Nettolohn, 6000.1 Auszahlung (AG-Teil).
    /// </summary>
    [HttpGet("vergleich")]
    public async Task<IActionResult> Vergleich([FromQuery] int companyProfileId, [FromQuery] int jahr)
    {
        var sim = await _db.SimulationLoehne.AsNoTracking()
            .Where(z => z.CompanyProfileId == companyProfileId && z.Jahr == jahr)
            .Select(z => new { z.EmployeeId, z.Monat, z.Brutto, z.Netto, z.Auszahlung, z.Fehler, z.Sonderzahlungen })
            .ToListAsync();
        var codes = new[] { "250.1", "1000.1", "6000.1" };
        var mirus = await _db.VorsystemLohnkonten.AsNoTracking()
            .Where(v => v.CompanyProfileId == companyProfileId && v.Jahr == jahr && codes.Contains(v.Code)
                     && ((v.Sektion == "AN" && v.Code != "6000.1") || (v.Sektion == "AG" && v.Code == "6000.1")))
            .Select(v => new { v.EmployeeId, v.Monat, v.Code, v.Betrag })
            .ToListAsync();
        var nachtragsCodes = LohnSimulationKontext.NachtragsCodes.ToList();
        var nachtraege = await _db.VorsystemLohnkonten.AsNoTracking()
            .Where(v => v.CompanyProfileId == companyProfileId && v.Jahr == jahr && v.Sektion == "AN"
                     && nachtragsCodes.Contains(v.Code) && v.Betrag != 0)
            .Select(v => new { v.EmployeeId, v.Monat, v.Code, v.Bezeichnung, v.Betrag })
            .ToListAsync();
        var simMonate = sim.Select(s => s.Monat).Distinct().ToHashSet();
        var empIds = sim.Select(s => s.EmployeeId)
            .Concat(mirus.Where(m => simMonate.Contains(m.Monat)).Select(m => m.EmployeeId))
            .Distinct().ToList();
        var namen = await _db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.EmployeeNumber })
            .ToListAsync();

        decimal M(int emp, int monat, string code) =>
            mirus.Where(m => m.EmployeeId == emp && m.Monat == monat && m.Code == code).Sum(m => m.Betrag);

        var zeilen = new List<object>();
        foreach (var n in namen.OrderBy(n => n.FirstName).ThenBy(n => n.LastName))
        {
            var monate = new List<object>();
            foreach (var monat in simMonate.OrderBy(m => m))
            {
                var s = sim.FirstOrDefault(x => x.EmployeeId == n.Id && x.Monat == monat);
                decimal mb = M(n.Id, monat, "250.1"), mn = M(n.Id, monat, "1000.1"), ma = M(n.Id, monat, "6000.1");
                if (s == null && mb == 0 && mn == 0 && ma == 0) continue;
                var nt = nachtraege.Where(x => x.EmployeeId == n.Id && x.Monat == monat).ToList();
                monate.Add(new
                {
                    monat,
                    simuliert = s != null,
                    fehler = s?.Fehler,
                    sonderzahlungen = s?.Sonderzahlungen,
                    nachtraegeMirus = nt.Count == 0 ? null
                        : string.Join(" · ", nt.Select(x => $"{x.Code} {x.Bezeichnung} {x.Betrag:0.00}")),
                    brutto = s?.Brutto ?? 0m, bruttoMirus = mb,
                    netto = s?.Netto ?? 0m, nettoMirus = mn,
                    auszahlung = s?.Auszahlung ?? 0m, auszahlungMirus = ma,
                });
            }
            if (monate.Count == 0) continue;
            zeilen.Add(new { employeeId = n.Id, name = $"{n.FirstName} {n.LastName}".Trim(), personalnummer = n.EmployeeNumber, monate });
        }
        return Ok(new { monate = simMonate.OrderBy(m => m).ToList(), zeilen });
    }

    /// <summary>Alle Zeilen eines simulierten Lohnzettels neben den Mirus-Zeilen desselben Monats.</summary>
    [HttpGet("detail")]
    public async Task<IActionResult> Detail([FromQuery] int companyProfileId, [FromQuery] int employeeId,
        [FromQuery] int jahr, [FromQuery] int monat)
    {
        var sim = await _db.SimulationLoehne.AsNoTracking()
            .FirstOrDefaultAsync(z => z.CompanyProfileId == companyProfileId && z.EmployeeId == employeeId
                                   && z.Jahr == jahr && z.Monat == monat);
        var mirus = await _db.VorsystemLohnkonten.AsNoTracking()
            .Where(v => v.CompanyProfileId == companyProfileId && v.EmployeeId == employeeId
                     && v.Jahr == jahr && v.Monat == monat)
            .OrderBy(v => v.Sektion).ThenBy(v => v.Id)
            .Select(v => new { v.Sektion, v.Code, v.Bezeichnung, v.Betrag })
            .ToListAsync();
        return Ok(new
        {
            fehler = sim?.Fehler,
            sonderzahlungen = sim?.Sonderzahlungen,
            oneCrew = SlipZeilen(sim?.SlipJson),
            saldi = sim == null ? null : new
            {
                stunden = sim.HourSaldo, nacht = sim.NachtSaldo, ferienGeld = sim.FerienGeldSaldo,
                ferienTage = sim.FerienTageSaldo, feiertagTage = sim.FeiertagTageSaldo, dreizehnter = sim.ThirteenthAccumulated,
            },
            mirus,
        });
    }

    /// <summary>Jede Zeile aus den «…Lines»-Listen des Lohnzettels (Lohn, Zulagen, Abzüge).</summary>
    public static List<object> SlipZeilen(string? slipJson)
    {
        var liste = new List<object>();
        if (string.IsNullOrWhiteSpace(slipJson)) return liste;
        if (JsonNode.Parse(slipJson) is not JsonObject root) return liste;
        foreach (var (key, node) in root)
        {
            if (!key.EndsWith("Lines", StringComparison.Ordinal) || node is not JsonArray arr) continue;
            foreach (var item in arr.OfType<JsonObject>())
            {
                var betrag = item["betrag"] ?? item["amount"];
                if (betrag is null) continue;
                liste.Add(new
                {
                    gruppe = key,
                    code = item["code"]?.ToString() ?? item["categoryCode"]?.ToString(),
                    bezeichnung = item["bezeichnung"]?.ToString() ?? item["label"]?.ToString(),
                    betrag = betrag.ToString(),
                });
            }
        }
        return liste;
    }
}
