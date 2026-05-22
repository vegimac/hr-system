using System.Text.Json;
using System.Text.Json.Nodes;
using HrSystem.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Rechnet die Lohn-Snapshots einer Periode NEU und überschreibt Brutto + Netto +
/// SlipJson GEMEINSAM aus einer frischen Berechnung (Walter-Vorgabe 22.05.2026).
///
/// Hintergrund / Designprinzip:
/// Ein Snapshot ist die eingefrorene Lohnabrechnung. Solange eine Periode OFFEN ist
/// (offen / provisorisch / wieder geöffnet), darf der Snapshot NICHT veralten — er
/// muss die aktuellen Quelldaten widerspiegeln. Ändert jemand zwischen Bestätigen und
/// Wieder-Öffnen einen Lohn, würde ein nur teilweise nachgezogener Snapshot (z.B. nur
/// SlipJson, nicht aber Brutto/Netto) auseinanderlaufen → Fibu-Journal/DTA stimmen
/// nicht mehr. Darum: bei JEDEM Zurückstellen/Wieder-Öffnen ALLE Snapshots der Periode
/// frisch rechnen, damit Brutto = Netto + Abzüge gilt und die Codes nativ drin sind.
///
/// Bei ABGESCHLOSSENER Periode ist der Snapshot eingefroren UND die Quelldaten sind
/// gesperrt (LohnEditLockService) → Live-Rechnung ergäbe ohnehin dasselbe.
///
/// Status/Workflow (BERECHNET/FREIGEGEBEN_GF/HR_BESTAETIGT/…) werden NICHT angetastet.
/// </summary>
public class SnapshotRecomputeService
{
    private readonly AppDbContext _db;
    private readonly PayrollCalculationEngine _calcEngine;

    public SnapshotRecomputeService(AppDbContext db, PayrollCalculationEngine calcEngine)
    {
        _db = db;
        _calcEngine = calcEngine;
    }

    private static readonly JsonSerializerOptions Camel =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static decimal ReadDec(JsonNode root, string key, decimal fb)
    {
        var n = root[key];
        if (n is null) return fb;
        try { return n.GetValue<decimal>(); }
        catch { try { return (decimal)n.GetValue<double>(); } catch { return fb; } }
    }

    /// <summary>
    /// Rechnet alle nicht-stornierten Snapshots der Periode neu. Liefert die Anzahl
    /// aktualisierter Snapshots. Macht ein eigenes SaveChanges (self-contained).
    /// </summary>
    public async Task<int> RecomputeAsync(int companyProfileId, int year, int month)
    {
        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId && p.Year == year && p.Month == month);
        if (periode is null) return 0;

        var snaps = await _db.PayrollSnapshots
            .Where(s => s.PayrollPeriodeId == periode.Id && s.Status != "STORNIERT")
            .ToListAsync();
        if (snaps.Count == 0) return 0;

        var saldi = await _db.PayrollSaldos
            .Where(s => s.CompanyProfileId == companyProfileId && s.PeriodYear == year && s.PeriodMonth == month)
            .ToListAsync();
        var saldoByEmp = saldi.GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        int updated = 0;
        foreach (var s in snaps)
        {
            var calc = await _calcEngine.CalculateAsync(s.EmployeeId, year, month, companyProfileId);
            if (calc is not OkObjectResult ok || ok.Value is null) continue;

            var json = JsonSerializer.Serialize(ok.Value, Camel);
            JsonNode? node;
            try { node = JsonNode.Parse(json); } catch { continue; }
            if (node is null) continue;

            decimal gross = ReadDec(node, "totalLohn", s.Brutto);
            decimal net   = ReadDec(node, "nettolohn", s.Netto);

            s.Brutto    = gross;
            s.Netto     = net;
            s.SlipJson  = json;
            s.UpdatedAt = DateTime.UtcNow;

            if (saldoByEmp.TryGetValue(s.EmployeeId, out var sal))
            {
                sal.GrossAmount = gross;
                sal.NetAmount   = net;
            }
            updated++;
        }

        await _db.SaveChangesAsync();
        return updated;
    }
}
