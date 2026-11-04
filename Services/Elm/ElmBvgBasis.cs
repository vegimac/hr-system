namespace HrSystem.Services.Elm;

/// <summary>
/// BVG-Basis nach RL 8.6.6: voraussichtlich + rückwirkend. Dieselbe Rechnung gilt für die
/// EMA-Meldung (Wirkungsdatum = ValidAsOf) und die voraussichtliche Jahresmeldung
/// (GeneralValidAsOf). Rein: keine Datenbank, damit der Test gegen RefXML direkt rechnen kann.
/// </summary>
public static class ElmBvgBasis
{
    /// <summary>Steuerung einer Lohnart: voraussichtlich mit Faktor oder rückwirkend mit Gruppe.</summary>
    public readonly record struct Steuerung(bool Voraussichtlich, decimal Faktor, bool FaktorX, int RueckCode);

    /// <summary>Vertrag per Wirkungsdatum. Stunden- und Lektionensatz nur bei festem Pensum.</summary>
    public sealed record Vertrag(
        bool StundenOderLektionenlohn,
        bool FestesPensum,
        decimal? Wochenstunden,
        decimal? Wochenlektionen,
        decimal? Stundensatz,
        decimal? Lektionensatz,
        decimal? Prozent13,
        bool Mit13,
        bool Mit14);

    public sealed record LohnMonat(DateOnly Monat, IReadOnlyDictionary<string, decimal> Lohnarten);

    /// <summary>
    /// Regelmässige Lohnarten mal Faktor. Bei festem Pensum im Stunden- oder Lektionenlohn
    /// Wochenansatz × Satz × 52, und der vertragliche 13. in Prozent kommt dazu (TF08 Lamon).
    /// </summary>
    public static decimal Voraussichtlich(
        Vertrag v, IReadOnlyDictionary<string, decimal> lohnartenImMonat, Func<string, Steuerung?> steuerung)
    {
        decimal summe = 0m;
        foreach (var (code, betrag) in lohnartenImMonat)
        {
            if (steuerung(code) is not { Voraussichtlich: true } s) continue;
            summe += betrag * (s.FaktorX ? 12 + (v.Mit13 ? 1 : 0) + (v.Mit14 ? 1 : 0) : s.Faktor);
        }
        if (v is { StundenOderLektionenlohn: true, FestesPensum: true })
        {
            var jahr = (v.Wochenstunden ?? 0m) * (v.Stundensatz ?? 0m) * 52m
                     + (v.Wochenlektionen ?? 0m) * (v.Lektionensatz ?? v.Stundensatz ?? 0m) * 52m;
            summe += v.Prozent13 is { } p ? jahr * (1m + p / 100m) : jahr;
        }
        return summe;
    }

    /// <summary>
    /// Rückwirkende Lohnarten der letzten 12 abgeschlossenen Läufe bis und mit <paramref name="bisMonat"/>,
    /// über die SV-Tage ab <paramref name="periodeAb"/> auf 360 Tage hochgerechnet. Bei festem Pensum
    /// zählen die Gruppen Stunden- und Lektionenlohn nicht: Sie stecken schon im voraussichtlichen Teil.
    /// </summary>
    public static decimal Rueckwirkend(
        bool festesPensum, IEnumerable<LohnMonat> laeufe, DateOnly bisMonat, DateOnly periodeAb,
        Func<string, Steuerung?> steuerung)
    {
        var bis = new DateOnly(bisMonat.Year, bisMonat.Month, 1);
        var von = bis.AddMonths(-11);
        decimal summe = 0m;
        int tage = 0;
        var nachMonat = laeufe.ToDictionary(l => new DateOnly(l.Monat.Year, l.Monat.Month, 1));
        for (var m = von; m <= bis; m = m.AddMonths(1))
        {
            if (m.AddMonths(1).AddDays(-1) < periodeAb) continue;
            tage += periodeAb <= m ? 30 : 30 - Math.Min(periodeAb.Day, 30) + 1;
            if (!nachMonat.TryGetValue(m, out var lauf)) continue;
            foreach (var (code, betrag) in lauf.Lohnarten)
            {
                if (steuerung(code) is not { Voraussichtlich: false } s) continue;
                bool zaehlt = s.RueckCode is 3 or 4 || (!festesPensum && s.RueckCode is 1 or 2);
                if (zaehlt) summe += betrag;
            }
        }
        return tage == 0 ? 0m : summe * 360m / tage;
    }

    /// <summary>
    /// Basis der voraussichtlichen Jahresmeldung per GeneralValidAsOf: manuell, sonst der
    /// voraussichtliche Teil des Stichtagsmonats plus die 12 Läufe davor (TF11 Bosshard 2026).
    /// </summary>
    public static decimal FuerJahresmeldung(
        DateOnly stichtag, decimal? manuell, Vertrag v, IReadOnlyDictionary<string, decimal> lohnartenStichtag,
        IEnumerable<LohnMonat> laeufe, DateOnly periodeAb, Func<string, Steuerung?> steuerung)
    {
        if (manuell is { } m) return m;
        var voraus = Voraussichtlich(v, lohnartenStichtag, steuerung);
        var rueck = periodeAb < stichtag
            ? Rueckwirkend(v.FestesPensum, laeufe, stichtag.AddMonths(-1), periodeAb, steuerung)
            : 0m;
        return Runde5Rappen(voraus + rueck);
    }

    public static decimal Runde5Rappen(decimal betrag)
        => Math.Round(betrag * 20m, MidpointRounding.AwayFromZero) / 20m;

    /// <summary>Steuerung aus dem Swissdec-Musterstamm, für Tests und als Vorgabe neuer Lohnarten.</summary>
    public static Steuerung? AusKatalog(string code)
    {
        var la = SwissdecLohnartenKatalog.FindeBvg(code);
        if (la == null) return null;
        var a = LohnartSteuerung.BvgAbleiten(la.BvgErwartet ?? 0, la.BvgRueck ?? 0, la.BvgFaktorRoh);
        if (!a.Pflichtig) return null;
        return new Steuerung(a.Betrachtung == LohnartSteuerung.BvgVoraussichtlich, a.Faktor, a.FaktorX, a.RueckCode);
    }
}
