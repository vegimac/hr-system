using HrSystem.Controllers;
using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Schatten-Basen-Rechner (Swissdec Schritt 2, Walter-Vorgabe 17.08.2026).
///
/// Rechnet die SV-Basen (AHV/ALV · NBU · KTG · BVG · QST) ein ZWEITES Mal auf
/// einem unabhängigen Weg: rein aus den Lohnzeilen-Codes × Lohnpositions-Flags
/// (Lohnarten-Steuerung wie im Raster) — und vergleicht mit den Basen, die die
/// Engine über ihren Fest-Code-Pfad (mainLohn + deltas) ermittelt hat.
///
/// REINE KONTROLLE: verändert NICHTS an der Berechnung. Das Ergebnis wandert
/// als «schattenBasen»-Block in den Slip-JSON (unsichtbar im UI) und wird über
/// GET /api/payroll/schatten-report pro Periode aggregiert.
///
/// Erwartung: diff = 0.00 pro Kategorie. Bekannte legitime Abweichungen:
///  · BVG: die BVG-Wartefrist-Korrektur (MTP Krank/Unfall) ist ein Engine-
///    Aufschlag OHNE Lohnzeile → Schatten-BVG ist um diesen Betrag tiefer.
///  · Rappen-Differenzen: Lohnzeilen sind auf 0.01 gerundet, die Engine
///    summiert teils exakt — |diff| ≤ 0.05 gilt als Rundungsrauschen.
///  · Zeilen ohne Code («ohneCode») fliessen in keine Schatten-Basis —
///    jede solche Zeile ist ein noch nicht getaggter Kandidat.
/// </summary>
public static class SchattenBasenService
{
    /// <summary>Rundungs-Toleranz pro Kategorie (Rappen-Rauschen).</summary>
    public const decimal Toleranz = 0.05m;

    public static object Compute(
        IEnumerable<object> lohnLines,
        Dictionary<string, Lohnposition> lohnposByCode,
        SvBases engine)
    {
        decimal ahv = 0, nbuv = 0, ktg = 0, bvg = 0, qst = 0;
        var ohneCode = new List<object>();
        decimal ohneCodeSumme = 0;

        foreach (var line in lohnLines)
        {
            var t = line.GetType();
            decimal betrag = t.GetProperty("betrag")?.GetValue(line) as decimal? ?? 0m;
            if (betrag == 0m) continue;   // Accrual-/Info-Zeilen (Saldo-Aufbau) — SV erst bei Auszahlung

            string? code = t.GetProperty("code")?.GetValue(line) as string;
            if (string.IsNullOrEmpty(code) || !lohnposByCode.TryGetValue(code, out var lp))
            {
                string bez = t.GetProperty("bezeichnung")?.GetValue(line) as string ?? "?";
                ohneCode.Add(new { bezeichnung = bez, code, betrag });
                ohneCodeSumme += betrag;
                continue;
            }
            if (lp.AhvAlvPflichtig) ahv  += betrag;
            if (lp.NbuvPflichtig)   nbuv += betrag;
            if (lp.KtgPflichtig)    ktg  += betrag;
            if (lp.BvgPflichtig)    bvg  += betrag;
            if (lp.QstPflichtig)    qst  += betrag;
        }

        static object Cat(decimal schatten, decimal eng) => new
        {
            schatten = Math.Round(schatten, 2),
            engine   = Math.Round(eng, 2),
            diff     = Math.Round(schatten - eng, 2),
            ok       = Math.Abs(schatten - eng) <= Toleranz,
        };

        bool alleOk = Math.Abs(ahv  - engine.Ahv)  <= Toleranz
                   && Math.Abs(nbuv - engine.Nbuv) <= Toleranz
                   && Math.Abs(ktg  - engine.Ktg)  <= Toleranz
                   && Math.Abs(bvg  - engine.Bvg)  <= Toleranz
                   && Math.Abs(qst  - engine.Qst)  <= Toleranz
                   && ohneCode.Count == 0;

        return new
        {
            ahv  = Cat(ahv,  engine.Ahv),
            nbuv = Cat(nbuv, engine.Nbuv),
            ktg  = Cat(ktg,  engine.Ktg),
            bvg  = Cat(bvg,  engine.Bvg),
            qst  = Cat(qst,  engine.Qst),
            ok   = alleOk,
            ohneCode,
            ohneCodeSumme = Math.Round(ohneCodeSumme, 2),
        };
    }
}
