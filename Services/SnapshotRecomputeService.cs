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
    public async Task<int> RecomputeAsync(int companyProfileId, int year, int month, int? nurEmployeeId = null)
    {
        var periode = await _db.PayrollPerioden
            .FirstOrDefaultAsync(p => p.CompanyProfileId == companyProfileId && p.Year == year && p.Month == month);
        if (periode is null) return 0;

        var snaps = await _db.PayrollSnapshots
            .Where(s => s.PayrollPeriodeId == periode.Id && s.Status != "STORNIERT"
                        && (nurEmployeeId == null || s.EmployeeId == nurEmployeeId))
            .ToListAsync();
        if (snaps.Count == 0) return 0;

        var saldi = await _db.PayrollSaldos
            .Where(s => s.CompanyProfileId == companyProfileId && s.PeriodYear == year && s.PeriodMonth == month)
            .ToListAsync();
        var saldoByEmp = saldi.GroupBy(s => s.EmployeeId).ToDictionary(g => g.Key, g => g.First());

        int updated = 0;
        foreach (var s in snaps)
        {
            // Ferien-Kürzungs-Entscheid (Art. 329b OR) aus dem ALTEN SlipJson
            // übernehmen — das ist die einzige GF-Entscheidung, die die Engine
            // nicht selbst ableiten kann (analog ConfirmPayroll). Ohne diese
            // Übernahme würde ein Recompute eine angewendete Kürzung stillschweigend
            // zurückdrehen.
            bool kuerzungAngewendet = false;
            // Korrektur-/Sonderlohn (ausgetretene MA) steht nur im SlipJson («isCorrection»)
            // — den Recompute bisher immer als Normallohn gerechnet, was ohne Vertrag in der
            // Periode scheiterte und den Snapshot still stehen liess (TF41 Meier Max Mai;
            // Claude 21.09.2026). Jetzt im selben Modus neu rechnen wie beim Bestätigen.
            bool isCorrection = false;
            try
            {
                var oldNode = JsonNode.Parse(s.SlipJson ?? "{}");
                var kNode = oldNode?["ferienKuerzungAngewendet"];
                if (kNode != null) kuerzungAngewendet = kNode.GetValue<bool>();
                var cNode = oldNode?["isCorrection"];
                if (cNode != null) isCorrection = cNode.GetValue<bool>();
            }
            catch { /* Alt-Snapshot ohne Feld / defektes JSON → keine Kürzung */ }

            var calc = await _calcEngine.CalculateAsync(s.EmployeeId, year, month, companyProfileId,
                isCorrection: isCorrection, ignoreFrozenSnapshot: true);
            if (calc is not OkObjectResult ok || ok.Value is null) continue;

            // Posten, die der neu gerechnete Lohnzettel verrechnet, gehören dieser Periode —
            // wie beim Bestätigen (PayrollController K2). Ohne das blieb Rinaldis Mai-Posten
            // OFFEN: im Lohnzettel +190, in der ELM-Meldung nur Juni (QT Juli 2025, TF34).
            if (!isCorrection && _calcEngine.LetzteQstKorrekturIds.Count > 0)
            {
                var korrIds = _calcEngine.LetzteQstKorrekturIds;
                var offen = await _db.QstKorrekturen
                    .Where(k => korrIds.Contains(k.Id) && k.Status == "OFFEN")
                    .ToListAsync();
                foreach (var k in offen)
                {
                    k.Status              = "VERRECHNET";
                    k.VerrechnetPeriodeId = periode.Id;
                    k.VerrechnetAt        = DateTime.Now;
                }
            }

            var json = JsonSerializer.Serialize(ok.Value, Camel);
            JsonNode? node;
            try { node = JsonNode.Parse(json); } catch { continue; }
            if (node is null) continue;

            decimal gross = ReadDec(node, "totalLohn", s.Brutto);
            decimal net   = ReadDec(node, "nettolohn", s.Netto);

            // Ferien-Kürzung reproduzieren (server-berechneter Vorschlag, wie
            // ConfirmPayroll) + Entscheid wieder in den frischen Slip stempeln.
            decimal vorschlagTage = 0m;
            if (node["ferienKuerzung"]?["vorschlagTage"] is JsonNode vtNode)
            {
                try { vorschlagTage = vtNode.GetValue<decimal>(); }
                catch { try { vorschlagTage = (decimal)vtNode.GetValue<double>(); } catch { } }
            }
            decimal ferTageBase = ReadDec(node, "ferienTageSaldoNeu", 0m);
            decimal ferTage = kuerzungAngewendet
                ? Math.Round(ferTageBase - vorschlagTage, 4)
                : ferTageBase;
            node["ferienKuerzungAngewendet"]     = kuerzungAngewendet;
            node["ferienKuerzungAngewendetTage"] = kuerzungAngewendet ? vorschlagTage : 0m;
            node["famzZahlungen"] = JsonSerializer.SerializeToNode(_calcEngine.LetzteFamzZahlungen, Camel);
            json = node.ToJsonString();

            s.Brutto    = gross;
            s.Netto     = net;
            s.SlipJson  = json;
            // Spalten wie beim Bestätigen nachziehen (Walter 03.11.2026): die Höchstlohn-Kumulation
            // der Folgemonate liest SvBasis* aus diesen Spalten. Nach einer Korrektur im Lohnzettel
            // (TF25 Lehmann Februar 1001 −400) blieb hier 8'400 stehen → ALV Juni 400 zu tief.
            s.SvBasisAhv            = ReadDec(node, "svBasisAhv",            s.SvBasisAhv);
            s.SvBasisBvg            = ReadDec(node, "svBasisBvg",            s.SvBasisBvg);
            s.SvBasisNbuv           = ReadDec(node, "svBasisNbuv",           s.SvBasisNbuv);
            s.SvBasisKtg            = ReadDec(node, "svBasisKtg",            s.SvBasisKtg);
            s.QstBetrag             = ReadDec(node, "qstBetrag",             s.QstBetrag);
            s.ThirteenthAccumulated = ReadDec(node, "thirteenthAccumulated", s.ThirteenthAccumulated);
            s.FerienGeldSaldo       = ReadDec(node, "ferienGeldSaldoNeu",    s.FerienGeldSaldo);
            // payroll_snapshot.updated_at / payroll_saldo.updated_at sind seit
            // der Vereinheitlichung (Walter 04.08.2026, Migration
            // fix_payroll_snapshot_saldo_timestamps.sql) timestamp WITHOUT time
            // zone — wie überall im System gilt: DateTime.Now (Lokalzeit),
            // NIE UtcNow (Npgsql lehnt Kind=Utc auf without-time-zone ab).
            s.UpdatedAt = DateTime.Now;

            if (saldoByEmp.TryGetValue(s.EmployeeId, out var sal))
            {
                // ALLE Saldo-Felder nachziehen, nicht nur Gross/Net (Walter-Bug
                // 04.08.2026: nach 906-Vortrag-Import + Recompute zeigte der Slip
                // den korrekten Vormonat, aber payroll_saldo behielt die alten
                // Werte — der FOLGEMONAT liest seinen Vormonat aus payroll_saldo
                // und wäre falsch gestartet; auch Saldo-Listen + Fibu-RST-13
                // lesen payroll_saldo). Spiegelbild der srv*-Zuweisungen in
                // ConfirmPayroll.
                sal.GrossAmount                  = gross;
                sal.NetAmount                    = net;
                sal.HourSaldo                    = ReadDec(node, "neuerHourSaldo",       sal.HourSaldo);
                sal.NachtSaldo                   = ReadDec(node, "neuerNachtSaldo",      sal.NachtSaldo);
                sal.NightHoursWorked             = ReadDec(node, "nightHours",           sal.NightHoursWorked);
                sal.FerienGeldSaldo              = ReadDec(node, "ferienGeldSaldoNeu",   sal.FerienGeldSaldo);
                sal.FerienTageSaldo              = ferTage;
                sal.FeiertagTageSaldo            = ReadDec(node, "feiertagTageSaldoNeu", sal.FeiertagTageSaldo);
                sal.ThirteenthMonthMonthly       = ReadDec(node, "thirteenthMonthly",    sal.ThirteenthMonthMonthly);
                sal.ThirteenthMonthAccumulated   = ReadDec(node, "thirteenthAccumulated", sal.ThirteenthMonthAccumulated);
                sal.UpdatedAt                    = DateTime.Now;
            }
            updated++;
        }

        await _db.SaveChangesAsync();
        return updated;
    }
}
