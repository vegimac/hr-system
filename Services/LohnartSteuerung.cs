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
    public const string BvgKeine = "keine";

    /// <summary>
    /// Spalten aus showcase_wagetypes_2.0.9: voraussichtlich, Faktor, rückwirkende Gruppe.
    /// «x» ist der Monatslohn: 13 mit 13. Monatslohn, sonst 12.
    /// Gruppe 1 Stundenlohn, 2 Lektionenlohn, 3 übrige, 4 Taggeld.
    /// </summary>
    public readonly record struct BvgAbleitung(string Betrachtung, decimal Faktor, bool FaktorX, int RueckCode, bool Pflichtig);

    public static BvgAbleitung BvgAbleiten(int erwartet, int rueck, string? faktorRoh)
    {
        var roh = (faktorRoh ?? "").Trim();
        if (erwartet == 1)
        {
            var istX = roh.Equals("x", StringComparison.OrdinalIgnoreCase);
            decimal zahl = 1m;
            if (!istX && decimal.TryParse(roh, System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                zahl = parsed;
            return new BvgAbleitung(BvgVoraussichtlich, istX ? 0m : zahl, istX, 0, true);
        }
        if (rueck is >= 1 and <= 4)
            return new BvgAbleitung(BvgRueckwirkend, 1m, false, rueck, true);
        return new BvgAbleitung(BvgKeine, 1m, false, 0, false);
    }

    /// <summary>Faktor x aus dem Vertrag: mit 13. Monatslohn 13, sonst 12.</summary>
    public static decimal BvgFaktorAufloesen(bool faktorX, decimal faktor, bool mit13Monatslohn)
    {
        if (faktorX) return mit13Monatslohn ? 13m : 12m;
        return faktor <= 0 ? 1m : faktor;
    }

    /// <summary>
    /// Betrachtung, Faktor und Rück-Code. Das BVG-Häkchen folgt nur,
    /// wenn die Nummer eine BVG-Zelle im Stamm hat (<paramref name="pflichtAusBetrachtung"/>).
    /// </summary>
    public static void SetzeBvg(Lohnposition lp, string? betrachtung, decimal faktor, bool faktorX, int rueckCode, bool pflichtAusBetrachtung)
    {
        var b = (betrachtung ?? "").Trim();
        if (!b.Equals(BvgVoraussichtlich, StringComparison.OrdinalIgnoreCase)
            && !b.Equals(BvgKeine, StringComparison.OrdinalIgnoreCase))
            b = BvgRueckwirkend;
        else
            b = b.Equals(BvgVoraussichtlich, StringComparison.OrdinalIgnoreCase) ? BvgVoraussichtlich : BvgKeine;

        if (b == BvgKeine)
        {
            lp.BvgBetrachtung = BvgKeine;
            lp.BvgFaktor = 1m;
            lp.BvgFaktorX = false;
            lp.BvgRueckCode = 0;
            if (pflichtAusBetrachtung) lp.BvgPflichtig = false;
            return;
        }
        if (b == BvgVoraussichtlich)
        {
            lp.BvgBetrachtung = BvgVoraussichtlich;
            lp.BvgFaktorX = faktorX;
            lp.BvgFaktor = faktorX ? 0m : (faktor <= 0 ? 1m : faktor);
            lp.BvgRueckCode = 0;
            if (pflichtAusBetrachtung) lp.BvgPflichtig = true;
            return;
        }
        lp.BvgBetrachtung = BvgRueckwirkend;
        lp.BvgFaktor = 1m;
        lp.BvgFaktorX = false;
        lp.BvgRueckCode = rueckCode is >= 1 and <= 4 ? rueckCode : 3;
        if (pflichtAusBetrachtung) lp.BvgPflichtig = true;
    }

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
    /// Voraussichtlich: Monatsbetrag × Faktor, für die Eintrittsschwelle.
    /// Rückwirkend: die Summe der ausbezahlten BVG-pflichtigen Beträge.
    /// Der Abzug auf dem Lohnzettel bleibt der Lohn des Monats.
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
