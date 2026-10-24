using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Steuerung an der Lohnart, die über die Häkchen hinausgeht
/// (Quellensteuer-Charakter, BVG-Betrachtung, Statistik-Topf).
/// </summary>
public static class LohnartSteuerung
{
    public const string QstMonatlich = "monatlich";
    public const string QstJaehrlich = "jaehrlich";
    public const string BvgRueckwirkend = "rueckwirkend";
    public const string BvgVoraussichtlich = "voraussichtlich";

    /// <summary>
    /// Perioden-Topf (Hochrechnung, CH-Tage des Monats). Charakter gewinnt,
    /// wenn er gesetzt ist. Seed setzt ihn passend zur Periodizität.
    /// </summary>
    public static bool ImPeriodischenQstTopf(Lohnposition lp)
    {
        var c = (lp.QstCharakter ?? "").Trim();
        if (c.Equals(QstJaehrlich, StringComparison.OrdinalIgnoreCase)) return false;
        if (c.Equals(QstMonatlich, StringComparison.OrdinalIgnoreCase)) return true;
        return lp.QstPeriodisch;
    }

    /// <summary>
    /// Voraussichtlich: Monatsbetrag × Faktor. Rückwirkend: die Summe der
    /// ausbezahlten BVG-pflichtigen Beträge. Der Monatsabzug nutzt das nicht.
    /// </summary>
    public static decimal BvgJahreslohn(string? betrachtung, decimal monatsbetrag, decimal faktor, decimal istSumme)
    {
        if (string.Equals(betrachtung, BvgVoraussichtlich, StringComparison.OrdinalIgnoreCase))
        {
            var f = faktor <= 0 ? 1m : faktor;
            return Math.Round(monatsbetrag * f, 2, MidpointRounding.AwayFromZero);
        }
        return istSumme;
    }

    /// <summary>NBU, UVG und UVGZ parallel. Gleicher Startbestand → gleiche drei Summen.</summary>
    public static void UnfallDazu(bool nbuv, bool uvg, bool uvgz, decimal betrag,
        ref decimal nbuvSumme, ref decimal uvgSumme, ref decimal uvgzSumme)
    {
        if (nbuv) nbuvSumme += betrag;
        if (uvg) uvgSumme += betrag;
        if (uvgz) uvgzSumme += betrag;
    }
}
