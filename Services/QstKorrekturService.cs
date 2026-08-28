using System.Text.Json;
using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// K1 QST-Korrektur (Walter 29.08.2026, docs/qst-korrektur-konzept.md):
/// Erzeugt beim Erfassen einer RÜCKWIRKENDEN QST-Version die Korrektur-
/// Posten für alle DEFINITIV abgeschlossenen Monate im Wirkungsbereich.
///
/// Prinzipien:
///  • Snapshots bleiben eingefroren — alt = QST-Zeile aus dem SlipJson
///    (plus bereits verrechnete Korrekturen desselben Monats).
///  • neu = Nachrechnung mit der neuen Version auf DERSELBEN Basis
///    (satzBasis aus der Slip-Zeile, sonst max(Basis, Medianlohn neu)).
///  • Jahresgrenze: Monate aus Vorjahren → Status VORJAHR (Verrechnung
///    nur via Steuerverwaltung, nicht über den Lohnlauf).
/// </summary>
public class QstKorrekturService
{
    private readonly AppDbContext _db;
    private readonly QuellensteuerTarifService _tarifService;

    public QstKorrekturService(AppDbContext db, QuellensteuerTarifService tarifService)
    {
        _db = db;
        _tarifService = tarifService;
    }

    public record KorrekturErgebnis(int Anzahl, decimal TotalDifferenz, int Vorjahr, List<object> Posten);

    /// <summary>
    /// Rechnet die Korrektur-Posten für eine neu erfasste rückwirkende
    /// Version. Ersetzt bestehende OFFENE/VORJAHR-Posten der betroffenen
    /// Monate (VERRECHNET/IN_DARLEHEN bleiben und zählen als «bereits
    /// bezahlt» in die Alt-Basis).
    /// </summary>
    public async Task<KorrekturErgebnis> ErzeugeKorrekturenAsync(
        EmployeeQuellensteuer neueVersion, string grund, string? erfasstVon,
        CancellationToken ct = default)
    {
        var vonMonat = new DateOnly(neueVersion.ValidFrom.Year, neueVersion.ValidFrom.Month, 1);
        var bisWirkung = neueVersion.ValidTo; // meist null (offen)

        // Alle DEFINITIV abgeschlossenen Snapshots des MA im Wirkungsbereich
        var rows = await (from s in _db.PayrollSnapshots
                          join p in _db.PayrollPerioden on s.PayrollPeriodeId equals p.Id
                          where s.EmployeeId == neueVersion.EmployeeId
                                && s.Status != "STORNIERT"
                                && p.Status == "abgeschlossen"
                          select new
                          {
                              s.Id, s.CompanyProfileId, s.SlipJson,
                              p.Year, p.Month
                          }).ToListAsync(ct);

        var betroffen = rows
            .Where(r =>
            {
                var mStart = new DateOnly(r.Year, r.Month, 1);
                if (mStart < vonMonat) return false;
                if (bisWirkung.HasValue && mStart > new DateOnly(bisWirkung.Value.Year, bisWirkung.Value.Month, 1)) return false;
                return true;
            })
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .ToList();

        var posten = new List<object>();
        decimal totalDiff = 0;
        int vorjahrCount = 0;
        var heute = DateTime.Now;

        foreach (var r in betroffen)
        {
            // Bereits bestehende Posten dieses Monats: OFFEN/VORJAHR ersetzen,
            // VERRECHNET/IN_DARLEHEN/GEMELDET zählen als «bereits bezahlt».
            var bestehende = await _db.QstKorrekturen
                .Where(k => k.EmployeeId == neueVersion.EmployeeId && k.Jahr == r.Year && k.Monat == r.Month)
                .ToListAsync(ct);
            var ersetzbar = bestehende.Where(k => k.Status is "OFFEN" or "VORJAHR").ToList();
            if (ersetzbar.Count > 0) _db.QstKorrekturen.RemoveRange(ersetzbar);
            decimal bereitsVerrechnet = bestehende
                .Where(k => k.Status is "VERRECHNET" or "IN_DARLEHEN" or "GEMELDET")
                .Sum(k => k.Differenz);

            // Alte QST-Zeile aus dem eingefrorenen Slip
            var (alterBetrag, basis, satzBasis) = LeseQstZeile(r.SlipJson);
            var effektivAlt = alterBetrag + bereitsVerrechnet;

            // Damals gültige Version (für alter Code / Referenz)
            var mStichtag = new DateOnly(r.Year, r.Month, 1);
            var alteVersion = await _db.EmployeeQuellensteuer
                .Where(q => q.EmployeeId == neueVersion.EmployeeId
                            && q.Id != neueVersion.Id
                            && q.ValidFrom <= mStichtag
                            && (q.ValidTo == null || q.ValidTo >= mStichtag))
                .OrderByDescending(q => q.ValidFrom)
                .FirstOrDefaultAsync(ct);

            // Neue QST nachrechnen — auf derselben Basis wie damals.
            decimal neuerBetrag;
            var satzBasisEff = satzBasis
                ?? Math.Max(basis, neueVersion.MindestlohnSatzbestimmung ?? 0m);
            if (satzBasisEff < basis) satzBasisEff = basis;

            if (neueVersion.Prozentsatz.HasValue)
            {
                neuerBetrag = Math.Round(basis * neueVersion.Prozentsatz.Value / 100m, 2);
            }
            else
            {
                var calc = _tarifService.Berechne(
                    neueVersion.Steuerkanton ?? "",
                    neueVersion.TarifCode ?? "",
                    neueVersion.AnzahlKinder,
                    neueVersion.Kirchensteuer,
                    satzbestimmenderBruttoCHF: satzBasisEff,
                    istBruttoCHF: basis,
                    jahr: r.Year);
                if (calc == null) continue; // Tarif nicht ladbar → Monat auslassen (Hinweis via Anzahl)
                neuerBetrag = calc.SteuerbetragCHF;
            }
            if (neuerBetrag < 0) neuerBetrag = 0;

            var diff = Math.Round(neuerBetrag - effektivAlt, 2);
            if (Math.Abs(diff) < 0.05m) continue; // keine relevante Differenz

            var status = r.Year < heute.Year ? "VORJAHR" : "OFFEN";
            if (status == "VORJAHR") vorjahrCount++;

            string neuerCode = !string.IsNullOrWhiteSpace(neueVersion.TarifCode)
                ? $"{neueVersion.TarifCode}{neueVersion.AnzahlKinder}{(neueVersion.Kirchensteuer ? 'Y' : 'N')}"
                : (neueVersion.QstCode ?? "");
            string? alterCode = alteVersion == null ? null
                : (!string.IsNullOrWhiteSpace(alteVersion.TarifCode)
                    ? $"{alteVersion.TarifCode}{alteVersion.AnzahlKinder}{(alteVersion.Kirchensteuer ? 'Y' : 'N')}"
                    : alteVersion.QstCode);

            var k = new QstKorrektur
            {
                EmployeeId = neueVersion.EmployeeId,
                CompanyProfileId = r.CompanyProfileId,
                Jahr = r.Year,
                Monat = r.Month,
                AlteVersionId = alteVersion?.Id,
                NeueVersionId = neueVersion.Id,
                AlterCode = alterCode,
                NeuerCode = neuerCode,
                AlterBetrag = effektivAlt,
                NeuerBetrag = neuerBetrag,
                Differenz = diff,
                Basis = basis,
                SatzBasis = satzBasisEff,
                Status = status,
                Grund = grund,
                CreatedAt = DateTime.Now,
                CreatedBy = erfasstVon
            };
            _db.QstKorrekturen.Add(k);
            totalDiff += diff;
            posten.Add(new
            {
                jahr = r.Year, monat = r.Month,
                alterCode, neuerCode,
                alterBetrag = effektivAlt, neuerBetrag, differenz = diff, status
            });
        }

        await _db.SaveChangesAsync(ct);
        return new KorrekturErgebnis(posten.Count, Math.Round(totalDiff, 2), vorjahrCount, posten);
    }

    /// <summary>
    /// Liest die QST-Abzugszeile aus dem eingefrorenen SlipJson:
    /// (|betrag|, basis, satzBasis?). Ohne QST-Zeile: (0, brutto, null).
    /// </summary>
    private static (decimal betrag, decimal basis, decimal? satzBasis) LeseQstZeile(string slipJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(slipJson);
            // Root-Feld heisst «totalLohn» (Slip-Struktur PayrollCalculationService)
            decimal brutto = 0;
            if (doc.RootElement.TryGetProperty("totalLohn", out var b) && b.ValueKind == JsonValueKind.Number)
                brutto = b.GetDecimal();

            if (doc.RootElement.TryGetProperty("abzugLines", out var lines)
                && lines.ValueKind == JsonValueKind.Array)
            {
                foreach (var line in lines.EnumerateArray())
                {
                    if (line.TryGetProperty("categoryCode", out var cc)
                        && cc.ValueKind == JsonValueKind.String
                        && cc.GetString() == "QST")
                    {
                        decimal betrag = line.TryGetProperty("betrag", out var be) && be.ValueKind == JsonValueKind.Number
                            ? Math.Abs(be.GetDecimal()) : 0;
                        decimal basis = line.TryGetProperty("basis", out var ba) && ba.ValueKind == JsonValueKind.Number
                            ? ba.GetDecimal() : brutto;
                        decimal? satzBasis = line.TryGetProperty("satzBasis", out var sb) && sb.ValueKind == JsonValueKind.Number
                            ? sb.GetDecimal() : null;
                        return (betrag, basis, satzBasis);
                    }
                }
            }
            return (0, brutto, null);
        }
        catch
        {
            return (0, 0, null);
        }
    }
}
