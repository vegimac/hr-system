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
///  • neu = Nachrechnung mit DER NEUEN Version (Wirkung/Gültig-ab), auf
///    derselben Basis — Swissdec CompanyCorrection: A0N→B0N rückwirkend,
///    NICHT «was wir damals schon wussten» (das gilt nur für den Live-
///    Lohnlauf via QstVersionWahl.Waehle). Walter 18.09.2026, TF34.
///  • Zwischenmonate: ValidFrom … Vormonat von Erfahren am (sonst bis ValidTo).
///  • Jahresgrenze: Postenjahr &lt; Jahr von «Erfahren am» → VORJAHR
///    (Steuerverwaltung). Referenz = BekanntAb.Year, nicht DateTime.Now.
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
        // Wissens-Achse (Walter 15.09.2026):
        //  • Erfahren am NACH Gültig-ab → nur die Zwischenmonate (alter Code
        //    stand noch auf dem Beleg; Verrechnung im Erfahrungsmonat).
        //  • Sonst klassisch K1: alle abgeschlossenen Monate ab Gültig-ab
        //    bis ValidTo (null = offen).
        var letzterUnbekannt = QstVersionWahl.LetzterUnbekannterMonat(neueVersion);
        DateOnly? bisMonat = letzterUnbekannt
            ?? (neueVersion.ValidTo is { } vt
                ? new DateOnly(vt.Year, vt.Month, 1)
                : null);

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
                if (bisMonat.HasValue && mStart > bisMonat.Value) return false;
                return true;
            })
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .ToList();

        var posten = new List<object>();
        decimal totalDiff = 0;
        int vorjahrCount = 0;
        // Laufendes Steuerjahr = Jahr der Kenntnis/Verrechnung, nicht Kalender-heute.
        var laufendesSteuerjahr = QstVersionWahl.BekanntAb(neueVersion).Year;

        var alleVersionen = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == neueVersion.EmployeeId)
            .ToListAsync(ct);
        var jahresNeu = QstJahresmodell.GiltFuer(neueVersion.Steuerkanton)
            ? await RechneJahresKorrekturKetteAsync(
                neueVersion,
                betroffen.Select(r => (r.Year, r.Month, r.SlipJson)).ToList(),
                alleVersionen, ct)
            : null;

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
            alterBetrag = PayrollCalculations.Round05(alterBetrag);
            var effektivAlt = alterBetrag + bereitsVerrechnet;

            // NEU = die rückwirkende Version (Gültig-ab), nicht Waehle(Stichtag).
            // Live-Lohnlauf kennt B0N im April noch nicht (Waehle) — die
            // Korrektur im Erfahrungsmonat zieht ihn aber nach (Swissdec
            // RefXML Juni TF34: Old A0N −425 / New B0N +185 für Apr+Mai).
            var sollVersion = neueVersion;
            var mStichtag = new DateOnly(r.Year, r.Month, 1).AddMonths(1).AddDays(-1);

            // Alter Code = was wir am Monatsende kannten (ohne die neue Version)
            // — Referenz fürs Protokoll / Swissdec Old-Block.
            var aufBelegVersion = QstVersionWahl.Waehle(
                alleVersionen.Where(q => q.Id != neueVersion.Id), mStichtag);

            // Soll-QST nachrechnen — auf derselben Basis wie damals.
            decimal neuerBetrag;
            var satzBasisEff = satzBasis
                ?? Math.Max(basis, sollVersion.MindestlohnSatzbestimmung ?? 0m);
            if (satzBasisEff < basis) satzBasisEff = basis;

            if (jahresNeu != null)
            {
                if (!jahresNeu.TryGetValue((r.Year, r.Month), out neuerBetrag))
                    continue;
            }
            else if (sollVersion.Prozentsatz.HasValue)
            {
                neuerBetrag = PayrollCalculations.Round05(basis * sollVersion.Prozentsatz.Value / 100m);
            }
            else
            {
                var calc = _tarifService.Berechne(
                    sollVersion.Steuerkanton ?? "",
                    sollVersion.TarifCode ?? "",
                    sollVersion.AnzahlKinder,
                    sollVersion.Kirchensteuer,
                    satzbestimmenderBruttoCHF: satzBasisEff,
                    istBruttoCHF: basis,
                    jahr: r.Year);
                if (calc == null) continue; // Tarif nicht ladbar → Monat auslassen (Hinweis via Anzahl)
                neuerBetrag = calc.MindeststeuerAngewendet
                    ? calc.SteuerbetragCHF
                    : PayrollCalculations.Round05(calc.SteuerbetragCHF);
            }
            if (neuerBetrag < 0 && jahresNeu == null) neuerBetrag = 0;

            var diff = Math.Round(neuerBetrag - effektivAlt, 2);
            if (Math.Abs(diff) < 0.05m) continue; // keine relevante Differenz

            var status = r.Year < laufendesSteuerjahr ? "VORJAHR" : "OFFEN";
            if (status == "VORJAHR") vorjahrCount++;

            string neuerCode = !string.IsNullOrWhiteSpace(sollVersion.TarifCode)
                ? $"{sollVersion.TarifCode}{sollVersion.AnzahlKinder}{(sollVersion.Kirchensteuer ? 'Y' : 'N')}"
                : (sollVersion.QstCode ?? "");
            string? alterCode = aufBelegVersion == null ? null
                : (!string.IsNullOrWhiteSpace(aufBelegVersion.TarifCode)
                    ? $"{aufBelegVersion.TarifCode}{aufBelegVersion.AnzahlKinder}{(aufBelegVersion.Kirchensteuer ? 'Y' : 'N')}"
                    : aufBelegVersion.QstCode);

            var k = new QstKorrektur
            {
                EmployeeId = neueVersion.EmployeeId,
                CompanyProfileId = r.CompanyProfileId,
                Jahr = r.Year,
                Monat = r.Month,
                AlteVersionId = aufBelegVersion?.Id,
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
    /// Materialisiert fehlende K1-Posten für Versionen, deren «Erfahren am»
    /// in diesem Lohnmonat (oder früher) liegt — z.B. Testmandant-4c hat die
    /// Version schon angelegt, die Vormonate waren damals noch nicht
    /// abgeschlossen. Idempotent (OFFEN/VORJAHR werden ersetzt).
    /// </summary>
    public async Task EnsureKorrekturenFuerLohnlaufAsync(
        int employeeId, int year, int month, string? erfasstVon, CancellationToken ct = default)
    {
        var periodTo = new DateOnly(year, month, 1).AddMonths(1).AddDays(-1);
        var versionen = await _db.EmployeeQuellensteuer
            .Where(q => q.EmployeeId == employeeId)
            .ToListAsync(ct);
        foreach (var v in versionen)
        {
            if (QstVersionWahl.LetzterUnbekannterMonat(v) == null) continue;
            if (QstVersionWahl.BekanntAb(v) > periodTo) continue;
            var grund = $"Gültig ab {v.ValidFrom:dd.MM.yyyy}, erfahren am {QstVersionWahl.BekanntAb(v):dd.MM.yyyy}";
            await ErzeugeKorrekturenAsync(v, grund, erfasstVon, ct);
        }
    }

    /// <summary>
    /// Jahresmodell: Monate der Korrektur-Kette nacheinander mit neuem Tarif
    /// aufrollen (YTD + n), «bereits bezahlt» = neu berechnete Vormonate.
    /// </summary>
    private async Task<Dictionary<(int Jahr, int Monat), decimal>> RechneJahresKorrekturKetteAsync(
        EmployeeQuellensteuer neu,
        IReadOnlyList<(int Year, int Month, string SlipJson)> betroffen,
        List<EmployeeQuellensteuer> versionen,
        CancellationToken ct)
    {
        var map = new Dictionary<(int, int), decimal>();
        if (betroffen.Count == 0) return map;
        var kanton = (neu.Steuerkanton ?? "").Trim();
        var emp = await _db.Employees.AsNoTracking()
            .Include(e => e.Employments)
            .FirstOrDefaultAsync(e => e.Id == neu.EmployeeId, ct);
        // Verträge statt Eintritt/Austritt am MA: Wiedereintritt setzt die Töpfe nicht zurück (Y1.1).
        var vertraege = QstJahresmodell.Vertragszeitraeume(emp);

        foreach (var yg in betroffen.GroupBy(r => r.Year))
        {
            int year = yg.Key;
            int maxM = yg.Max(x => x.Month);
            var periodTo = new DateOnly(year, maxM, 1).AddMonths(1).AddDays(-1);
            var start = QstJahresmodell.ModellStart(
                year, QstJahresmodell.ErsterEintrittImJahr(vertraege, year), versionen, kanton, periodTo);

            var slips = await (
                from s in _db.PayrollSnapshots
                join p in _db.PayrollPerioden on s.PayrollPeriodeId equals p.Id
                where s.EmployeeId == neu.EmployeeId
                   && p.Year == year
                   && p.Month >= start.Month && p.Month <= maxM
                   && s.Status != "STORNIERT"
                select new { p.Month, s.SlipJson }
            ).ToListAsync(ct);

            var istByM = new Dictionary<int, decimal>();
            var satzByM = new Dictionary<int, decimal>();
            var aperByM = new Dictionary<int, decimal>();
            var altByM = new Dictionary<int, decimal>();
            foreach (var g in slips.GroupBy(x => x.Month))
            {
                foreach (var row in g)
                {
                    var z = QstJahresmodell.LeseSlip(row.SlipJson);
                    istByM[g.Key] = istByM.GetValueOrDefault(g.Key) + z.IstBasis;
                    satzByM[g.Key] = satzByM.GetValueOrDefault(g.Key) + QstJahresmodell.SatzDesMonats(z);
                    aperByM[g.Key] = aperByM.GetValueOrDefault(g.Key) + QstJahresmodell.AperiodischDesMonats(z);
                    altByM[g.Key] = altByM.GetValueOrDefault(g.Key) + z.QstBezahlt;
                }
            }
            foreach (var r in yg)
            {
                if (istByM.ContainsKey(r.Month)) continue;
                var z = QstJahresmodell.LeseSlip(r.SlipJson);
                istByM[r.Month] = z.IstBasis;
                satzByM[r.Month] = QstJahresmodell.SatzDesMonats(z);
                aperByM[r.Month] = QstJahresmodell.AperiodischDesMonats(z);
                altByM[r.Month] = z.QstBezahlt;
            }

            var betroffenMonate = yg.Select(x => x.Month).ToHashSet();
            decimal ytdPer = 0, ytdAper = 0, paidNew = 0;
            int qstTage = 0;
            var toepfe = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            var neuCode = QstJahresmodell.CodeVon(neu);
            for (int m = start.Month; m <= maxM; m++)
            {
                ytdPer += satzByM.GetValueOrDefault(m);
                ytdAper += aperByM.GetValueOrDefault(m);
                qstTage += QstJahresmodell.QstTageDesMonats(year, m, vertraege);
                var stichtag = new DateOnly(year, m, 1).AddMonths(1).AddDays(-1);
                var code = neu.ValidFrom <= stichtag
                    ? neuCode
                    : QstJahresmodell.CodeVon(QstVersionWahl.Waehle(versionen, stichtag));
                var istM = istByM.GetValueOrDefault(m);
                if (!string.IsNullOrWhiteSpace(code) && istM != 0)
                    toepfe[code] = toepfe.GetValueOrDefault(code) + istM;
                if (!betroffenMonate.Contains(m))
                {
                    paidNew += altByM.GetValueOrDefault(m);
                    continue;
                }
                var satzLohn = QstJahresmodell.SatzLohnAusTagen(ytdPer, qstTage, ytdAper);
                if (neu.Prozentsatz.HasValue)
                {
                    var ytdIst = toepfe.Values.Sum();
                    var jm = QstJahresmodell.Rechne(
                        ytdIst, paidNew, Math.Max(1, qstTage / 30), neu.Prozentsatz.Value,
                        ytdPer, ytdAper);
                    map[(year, m)] = jm.QstMonat;
                    paidNew += jm.QstMonat;
                }
                else
                {
                    var t = QstJahresmodell.RechneToepfe(
                        satzLohn, toepfe,
                        c =>
                        {
                            if (!QstJahresmodell.TryParseCode(c, out var tarif, out var kinder, out var kirche))
                                return 0;
                            return _tarifService.GetSteuersatzProzent(
                                kanton, tarif, kinder, kirche, satzLohn, year) ?? 0m;
                        },
                        paidNew);
                    map[(year, m)] = t.QstMonat;
                    paidNew += t.QstMonat;
                }
            }
        }
        return map;
    }

    /// <summary>
    /// Liest die QST-Abzugszeile aus dem eingefrorenen SlipJson:
    /// (bezahlt vorzeichenbehaftet, basis, satzBasis?). Ohne QST-Zeile: (0, brutto, null).
    /// </summary>
    private static (decimal betrag, decimal basis, decimal? satzBasis) LeseQstZeile(string slipJson)
    {
        var z = QstJahresmodell.LeseSlip(slipJson);
        return (z.QstBezahlt, z.IstBasis, z.SatzBasis);
    }
}
