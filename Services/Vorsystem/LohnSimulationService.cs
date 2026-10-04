using System.Text.Json;
using System.Text.Json.Nodes;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.Vorsystem;

/// <summary>
/// Lohn-Simulation (Walter 04.10.2026): OneCrew rechnet die Monate vor dem ersten
/// echten Lohnlauf mit derselben Engine nach — Start = Mirus-Saldi per 31.12.
/// (<see cref="SimulationVortrag"/>), Sonderzahlungen aus dem Mirus-Lohnkonto
/// (<see cref="VorsystemLohnkonto"/>), Ergebnis in <see cref="SimulationLohn"/>.
/// Schreibt NIE in payroll_periode / payroll_snapshot / payroll_saldo / lohn_zulage,
/// kein Postfach, keine E-Mail, kein DTA, keine Fibu.
/// </summary>
public class LohnSimulationService
{
    private readonly AppDbContext _db;
    private readonly PayrollCalculationEngine _engine;
    private readonly LohnSimulationKontext _sim;

    public LohnSimulationService(AppDbContext db, PayrollCalculationEngine engine, LohnSimulationKontext sim)
    {
        _db = db;
        _engine = engine;
        _sim = sim;
    }

    public record MonatErgebnis(int Jahr, int Monat, int Mitarbeiter, int Gerechnet, int MitFehler,
        List<string> Fehler, List<string> CodesOhneLohnposition);

    private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Rechnet einen Monat für alle MA der Filiale (Vertrag überlappt den Monat, keine
    /// Phantom-MA). Ersetzt die Simulation dieser Filiale ab diesem Monat — spätere
    /// Monate bauen auf diesem auf und müssen danach neu gerechnet werden.
    /// </summary>
    public async Task<MonatErgebnis> RechneMonatAsync(int companyProfileId, int jahr, int monat)
    {
        var (pFrom, pTo) = PayrollCalculations.CalcPeriod(jahr, monat);
        var pFromDt = pFrom.ToDateTime(TimeOnly.MinValue);
        var pToDt = pTo.ToDateTime(TimeOnly.MinValue);

        var mitarbeiter = await _db.Employments.AsNoTracking()
            .Where(e => e.CompanyProfileId == companyProfileId
                     && e.ContractStartDate <= pToDt
                     && (e.ContractEndDate == null || e.ContractEndDate >= pFromDt))
            .Select(e => e.Employee!)
            .Where(e => !e.IsPayrollExcluded)
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .Distinct()
            .ToListAsync();
        var empIds = mitarbeiter.Select(m => m.Id).ToList();

        int schluessel = jahr * 100 + monat;
        var vortragAlle = await _db.SimulationVortraege.AsNoTracking()
            .Where(v => empIds.Contains(v.EmployeeId))
            .ToListAsync();
        var simFrueher = await _db.SimulationLoehne.AsNoTracking()
            .Where(z => empIds.Contains(z.EmployeeId) && z.Jahr * 100 + z.Monat < schluessel)
            .ToListAsync();

        var codes = LohnSimulationKontext.SonderzahlungsCodes.ToList();
        var lohnkonto = await _db.VorsystemLohnkonten.AsNoTracking()
            .Where(v => v.CompanyProfileId == companyProfileId && v.Jahr == jahr && v.Monat == monat
                     && v.Sektion == "AN" && codes.Contains(v.Code) && v.Betrag != 0)
            .ToListAsync();
        var anteil13McBonus = (await _db.VorsystemLohnkonten.AsNoTracking()
                .Where(v => v.CompanyProfileId == companyProfileId && v.Jahr == jahr && v.Monat == monat
                         && v.Sektion == "AN" && v.Code == LohnSimulationKontext.McBonus13mlAnteilCode)
                .ToListAsync())
            .GroupBy(v => v.EmployeeId).ToDictionary(g => g.Key, g => g.Sum(v => v.Betrag));
        var verknuepft = await _db.ElmLohnraster.AsNoTracking()
            .Where(r => codes.Contains(r.Code) && r.VerwendetLohnpositionId != null)
            .Select(r => new { r.Code, Lp = r.VerwendetLohnposition! })
            .ToListAsync();
        var suchCodes = codes.Concat(LohnSimulationKontext.GleicheLohnartWie.Values).Distinct().ToList();
        var gleicherCode = await _db.Lohnpositionen.AsNoTracking()
            .Where(l => suchCodes.Contains(l.Code) && l.IsActive)
            .ToListAsync();
        var lohnpositionen = WaehleLohnpositionen(
            codes,
            verknuepft.Select(v => (v.Code, v.Lp)).ToList(),
            gleicherCode);
        var wiederkehrend = (await _db.EmployeeRecurringWages.AsNoTracking()
                .Where(r => empIds.Contains(r.EmployeeId)
                         && r.ValidFrom <= pTo && (r.ValidTo == null || r.ValidTo >= pFrom))
                .Select(r => new { r.EmployeeId, r.LohnpositionId })
                .ToListAsync())
            .Select(r => (r.EmployeeId, r.LohnpositionId)).ToHashSet();

        var codesOhneLp = lohnkonto.Select(v => v.Code).Distinct()
            .Where(c => !lohnpositionen.ContainsKey(c)).OrderBy(c => c).ToList();

        var ergebnisse = new List<SimulationLohn>();
        var fehlerListe = new List<string>();
        var periode = $"{jahr:D4}-{monat:D2}";

        foreach (var ma in mitarbeiter.OrderBy(m => m.FirstName).ThenBy(m => m.LastName))
        {
            var eigeneFrueher = simFrueher.Where(z => z.EmployeeId == ma.Id).ToList();
            var vormonatZeile = LohnSimulationKontext.WaehleVormonat(eigeneFrueher, jahr, monat);
            var vortrag = vormonatZeile == null
                ? vortragAlle.Where(v => v.EmployeeId == ma.Id).GroupBy(v => v.Code)
                    .ToDictionary(g => g.Key, g => g.First().Betrag)
                : new Dictionary<string, decimal>();
            var vormonateJahr = eigeneFrueher.Where(z => z.Jahr == jahr && z.Monat < monat).ToList();

            var sonder = lohnkonto
                .Where(v => v.EmployeeId == ma.Id
                         && lohnpositionen.ContainsKey(v.Code)
                         && !wiederkehrend.Contains((ma.Id, lohnpositionen[v.Code].Id)))
                .ToList();
            var zulagen = sonder.Select(v => new LohnZulage
            {
                EmployeeId = ma.Id,
                Periode = periode,
                LohnpositionId = lohnpositionen[v.Code].Id,
                Lohnposition = lohnpositionen[v.Code],
                Betrag = SonderBetrag(v.Code, v.Betrag, lohnpositionen[v.Code],
                    anteil13McBonus.GetValueOrDefault(ma.Id)),
                Bemerkung = "aus Mirus-Lohnkonto",
            }).ToList();

            var zeile = new SimulationLohn
            {
                EmployeeId = ma.Id,
                CompanyProfileId = companyProfileId,
                Jahr = jahr,
                Monat = monat,
                Sonderzahlungen = sonder.Count == 0 ? null
                    : string.Join(" · ", sonder.Select(v => $"{v.Code} {v.Bezeichnung} {v.Betrag:0.00}")),
                BerechnetAm = DateTime.Now,
            };

            try
            {
                _sim.Setze(
                    vormonatZeile == null ? null : LohnSimulationKontext.AlsSaldo(vormonatZeile),
                    vortrag, vormonateJahr, zulagen);
                var aktion = await _engine.CalculateAsync(ma.Id, jahr, monat, companyProfileId,
                    ignoreFrozenSnapshot: true);
                if (aktion is OkObjectResult ok && ok.Value is not null)
                    Uebernimm(zeile, JsonNode.Parse(JsonSerializer.Serialize(ok.Value, Camel))!, vormonatZeile, vortrag);
                else
                    zeile.Fehler = FehlerText(aktion);
            }
            catch (Exception ex)
            {
                zeile.Fehler = ex.InnerException?.Message ?? ex.Message;
            }
            finally
            {
                _sim.Beende();
                _db.ChangeTracker.Clear();
            }

            if (zeile.Fehler != null) fehlerListe.Add($"{ma.FirstName} {ma.LastName}: {zeile.Fehler}");
            ergebnisse.Add(zeile);
        }

        await using var tx = await _db.Database.BeginTransactionAsync();
        await _db.SimulationLoehne
            .Where(z => z.CompanyProfileId == companyProfileId && z.Jahr * 100 + z.Monat >= schluessel)
            .ExecuteDeleteAsync();
        _db.SimulationLoehne.AddRange(ergebnisse);
        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        return new MonatErgebnis(jahr, monat, mitarbeiter.Count,
            ergebnisse.Count(e => e.Fehler == null), ergebnisse.Count(e => e.Fehler != null),
            fehlerListe, codesOhneLp);
    }

    /// <summary>Löscht Simulation (und auf Wunsch den Simulations-Vortrag) der Filiale.</summary>
    public async Task<(int Loehne, int Vortraege)> VerwerfenAsync(int companyProfileId, bool mitVortrag)
    {
        int loehne = await _db.SimulationLoehne
            .Where(z => z.CompanyProfileId == companyProfileId).ExecuteDeleteAsync();
        int vortraege = mitVortrag
            ? await _db.SimulationVortraege.Where(v => v.CompanyProfileId == companyProfileId).ExecuteDeleteAsync()
            : 0;
        return (loehne, vortraege);
    }

    /// <summary>
    /// OneCrew-Lohnart pro Mirus-Code: zuerst die Verknüpfung im «Lohnraster Mirus»
    /// (gleiche Nummer kann in OneCrew etwas anderes bedeuten), sonst gleicher Code,
    /// sonst die Lohnart aus <see cref="LohnSimulationKontext.GleicheLohnartWie"/>.
    /// Inaktive Lohnarten zählen nicht.
    /// </summary>
    public static Dictionary<string, Lohnposition> WaehleLohnpositionen(
        IEnumerable<string> codes,
        IReadOnlyList<(string Code, Lohnposition Lp)> verknuepft,
        IReadOnlyList<Lohnposition> gleicherCode)
    {
        var ergebnis = new Dictionary<string, Lohnposition>(StringComparer.Ordinal);
        foreach (var code in codes)
        {
            var ziel = LohnSimulationKontext.GleicheLohnartWie.GetValueOrDefault(code);
            var lp = verknuepft.Where(v => v.Code == code && v.Lp.IsActive).Select(v => v.Lp).FirstOrDefault()
                  ?? gleicherCode.Where(l => l.Code == code && l.IsActive).OrderBy(l => l.Id).FirstOrDefault()
                  ?? (ziel == null ? null
                      : gleicherCode.Where(l => l.Code == ziel && l.IsActive).OrderBy(l => l.Id).FirstOrDefault());
            if (lp != null) ergebnis[code] = lp;
        }
        return ergebnis;
    }

    /// <summary>
    /// Mirus führt McBonus (200.5) und dessen 13.-ML-Anteil (200.9) getrennt. Trägt
    /// die OneCrew-Lohnart das orange Häkchen (Betrag inkl. 13. ML, Engine teilt
    /// 12/13 + 1/13), ist der erfasste Betrag das Total beider Mirus-Zeilen.
    /// </summary>
    public static decimal SonderBetrag(string mirusCode, decimal betrag, Lohnposition lp, decimal anteil13McBonus) =>
        mirusCode == LohnSimulationKontext.McBonusCode && lp.DreijehnterMlPflichtig
            ? betrag + anteil13McBonus
            : betrag;

    public static void Uebernimm(SimulationLohn zeile, JsonNode slip, SimulationLohn? vormonat,
        IReadOnlyDictionary<string, decimal> vortrag)
    {
        decimal? Wert(string key)
        {
            var n = slip[key];
            return n != null && n.GetValueKind() == JsonValueKind.Number ? n.GetValue<decimal>() : null;
        }
        decimal Alt(string vortragCode, Func<SimulationLohn, decimal> feld) =>
            vormonat != null ? feld(vormonat) : (vortrag.TryGetValue(vortragCode, out var v) ? v : 0m);

        zeile.Brutto = Wert("totalLohn") ?? 0m;
        zeile.Netto = Wert("nettolohn") ?? 0m;
        zeile.Auszahlung = Wert("auszahlungsbetrag") ?? 0m;
        zeile.SvBasisAhv = Wert("svBasisAhv") ?? 0m;
        zeile.SvBasisNbuv = Wert("svBasisNbuv") ?? 0m;
        zeile.SvBasisKtg = Wert("svBasisKtg") ?? 0m;
        zeile.HourSaldo = Wert("neuerHourSaldo") ?? Alt("901", z => z.HourSaldo);
        zeile.NachtSaldo = Wert("neuerNachtSaldo") ?? Alt("904", z => z.NachtSaldo);
        zeile.FerienGeldSaldo = Wert("ferienGeldSaldoNeu") ?? Alt("905", z => z.FerienGeldSaldo);
        zeile.FerienTageSaldo = Wert("ferienTageSaldoNeu") ?? Alt("903", z => z.FerienTageSaldo);
        zeile.FeiertagTageSaldo = Wert("feiertagTageSaldoNeu") ?? Alt("902", z => z.FeiertagTageSaldo);
        zeile.ThirteenthAccumulated = Wert("thirteenthAccumulated") ?? Alt("906", z => z.ThirteenthAccumulated);
        zeile.SlipJson = slip.ToJsonString();
    }

    private static string FehlerText(IActionResult aktion)
    {
        object? wert = aktion is ObjectResult o ? o.Value : null;
        if (wert is null) return $"Lohnzettel nicht rechenbar ({aktion.GetType().Name}).";
        if (wert is string s) return s;
        try
        {
            var n = JsonNode.Parse(JsonSerializer.Serialize(wert, Camel));
            return n?["message"]?.ToString() ?? n?["error"]?.ToString() ?? n?.ToJsonString() ?? "Fehler";
        }
        catch { return wert.ToString() ?? "Fehler"; }
    }
}
