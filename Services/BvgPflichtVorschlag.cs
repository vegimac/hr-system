namespace HrSystem.Services;

/// <summary>
/// Vorschlag BVG versichert ja/nein (Walter 04.10.2026). Massgebend ist der
/// mutmassliche Jahreslohn inkl. 13. ML gegen die Eintrittsschwelle (22'680):
///   • FIX / FIX-M: Monatslohn × 12 + 13. ML
///   • MTP: garantierte Wochenstunden × Stundenlohn × 52 + 13. ML
///   • FLEX: Ø der Lohnmonate ohne Krankheit/Unfall (max. 12, mind. 3) × 12 + 13. ML
/// Reine Rechnung ohne DB — entschieden wird nie automatisch, nur vorgeschlagen.
/// </summary>
public static class BvgPflichtVorschlag
{
    /// <param name="BvgBasisOhne13ml">BVG-pflichtiger Monatslohn ohne 13.-ML-Zeilen.</param>
    public record LohnMonat(int Jahr, int Monat, decimal BvgBasisOhne13ml, bool KrankOderUnfall);

    public record Ergebnis(bool? Versichert, decimal? Jahreslohn, string Grundlage, decimal Schwelle, bool Voraussichtlich = false);

    /// <summary>Lohnart steht auf voraussichtlich: Jahreslohn = Monatsbetrag × Faktor.</summary>
    public readonly record struct Jahressteuerung(bool Voraussichtlich, decimal Faktor);

    public const int MindestMonateFlex = 3;
    public const int MaxMonate = 12;

    public static Ergebnis Berechne(
        string? modell, decimal? monatslohn, decimal? garantierteStdWoche, decimal? stundenlohn,
        bool mit13ml, decimal prozent13ml, decimal schwelle, IReadOnlyList<LohnMonat> monate,
        Jahressteuerung? steuerung = null)
    {
        if (schwelle <= 0)
            return new Ergebnis(null, null, "Keine Eintrittsschwelle in den SV-Sätzen (BVG) erfasst.", 0m);

        var faktor = mit13ml ? 1m + prozent13ml / 100m : 1m;
        var mit13Text = mit13ml ? " + 13. ML" : "";
        var m = (modell ?? "").Trim().ToUpperInvariant();

        if ((m == "FIX" || m == "FIX-M") && monatslohn is > 0)
        {
            if (steuerung is { Voraussichtlich: true })
            {
                var f = Faktor(steuerung.Value);
                var jahrVor = Math.Round(LohnartSteuerung.BvgJahreslohn(
                    LohnartSteuerung.BvgVoraussichtlich, monatslohn.Value, f, 0m), 0);
                return Mache(jahrVor, schwelle, $"Monatslohn {Chf(monatslohn.Value)} × {Txt(f)}", true);
            }
            var jahr = Math.Round(monatslohn.Value * 12m * faktor, 0);
            return Mache(jahr, schwelle, $"Monatslohn {Chf(monatslohn.Value)} × 12{mit13Text}");
        }

        if (m == "MTP" && garantierteStdWoche is > 0 && stundenlohn is > 0)
        {
            if (steuerung is { Voraussichtlich: true })
            {
                var f = Faktor(steuerung.Value);
                var monat = garantierteStdWoche.Value * stundenlohn.Value * 52m / 12m;
                var jahrVor = Math.Round(LohnartSteuerung.BvgJahreslohn(
                    LohnartSteuerung.BvgVoraussichtlich, monat, f, 0m), 0);
                return Mache(jahrVor, schwelle,
                    $"{garantierteStdWoche.Value:0.##} Std./Woche × {stundenlohn.Value:0.00} × 52 / 12 × {Txt(f)}", true);
            }
            var jahr = Math.Round(garantierteStdWoche.Value * stundenlohn.Value * 52m * faktor, 0);
            return Mache(jahr, schwelle,
                $"{garantierteStdWoche.Value:0.##} Std./Woche garantiert × {stundenlohn.Value:0.00} × 52 Wochen{mit13Text}");
        }

        var brauchbar = monate
            .Where(x => !x.KrankOderUnfall && x.BvgBasisOhne13ml > 0)
            .OrderByDescending(x => x.Jahr).ThenByDescending(x => x.Monat)
            .Take(MaxMonate).ToList();
        if (brauchbar.Count < MindestMonateFlex)
            return new Ergebnis(null, null,
                $"Noch zu wenig Lohnmonate ohne Krankheit/Unfall ({brauchbar.Count} von {MindestMonateFlex}) — Vorschlag folgt nach dem {MindestMonateFlex}. Lohnmonat.",
                schwelle);

        var schnitt = brauchbar.Average(x => x.BvgBasisOhne13ml);
        if (steuerung is { Voraussichtlich: true })
        {
            var f = Faktor(steuerung.Value);
            var jahrVor = Math.Round(LohnartSteuerung.BvgJahreslohn(
                LohnartSteuerung.BvgVoraussichtlich, schnitt, f, 0m), 0);
            return Mache(jahrVor, schwelle,
                $"Ø {Chf(schnitt)} aus {brauchbar.Count} Lohnmonaten ohne Krankheit/Unfall × {Txt(f)}", true);
        }
        var jahrLohn = Math.Round(schnitt * 12m * faktor, 0);
        return Mache(jahrLohn, schwelle,
            $"Ø {Chf(schnitt)} aus {brauchbar.Count} Lohnmonaten ohne Krankheit/Unfall × 12{mit13Text}");
    }

    /// <summary>Welche Lohnart den Faktor liefert: Festlohn, sonst Lektionenlohn, sonst Stundenlohn.</summary>
    public static string LohnartCode(string? modell, decimal? lektionenlohn)
    {
        var m = (modell ?? "").Trim().ToUpperInvariant();
        if (m is "FIX" or "FIX-M") return "1000";
        if (lektionenlohn is > 0) return "1006";
        return "1005";
    }

    private static decimal Faktor(Jahressteuerung steuerung) => steuerung.Faktor <= 0 ? 1m : steuerung.Faktor;

    private static string Txt(decimal faktor) => faktor.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("de-CH"));

    private static Ergebnis Mache(decimal jahr, decimal schwelle, string grundlage, bool voraussichtlich = false) =>
        new(jahr >= schwelle, jahr, $"{grundlage} = {Chf(jahr)} (Schwelle {Chf(schwelle)})", schwelle, voraussichtlich);

    private static string Chf(decimal v) =>
        v.ToString("#,##0.##", System.Globalization.CultureInfo.GetCultureInfo("de-CH"));
}
