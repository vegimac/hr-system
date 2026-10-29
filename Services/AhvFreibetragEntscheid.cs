namespace HrSystem.Services;

/// <summary>
/// AHV-Freibetrag ab Referenzalter (AHV-Merkblatt 2.01 Ziffer 16).
/// Erreicht jemand das Referenzalter im Juli, gilt der Freibetrag ab August
/// automatisch (1'400 pro Monat, im Erreichensjahr nur die Monate ab August).
/// Ein Verzicht darauf muss vor der ersten bestätigten Lohnabrechnung ab diesem
/// Monat feststehen und gilt dann bis Ende Jahr. Ab dem 1.1. des Folgejahres
/// kann die Wahl wieder ändern. Ohne neue Mitteilung bleibt sie.
/// </summary>
public static class AhvFreibetragEntscheid
{
    public const decimal FreibetragMonat = 1400m;

    public readonly record struct Spanne(DateOnly Von, DateOnly? Bis);

    public static bool GiltAm(IEnumerable<Spanne> spannen, DateOnly tag)
        => spannen.Any(s => s.Von <= tag && (s.Bis == null || s.Bis >= tag));

    /// <summary>Erster Tag, an dem der Freibetrag gilt: Folgemonat des Referenzalters.</summary>
    public static DateOnly? Folgemonat(string? gender, DateTime geburt, AhvBeitragspflichtStand? stand = null)
    {
        for (var y = geburt.Year + 58; y <= geburt.Year + 75; y++)
            for (var m = 1; m <= 12; m++)
                if (PayrollCalculations.HatReferenzalterErreicht(gender, geburt, y, m, stand))
                    return new DateOnly(y, m, 1);
        return null;
    }

    /// <summary>
    /// Frühester Beginn eines Verzichts in diesem Jahr: 1.1., oder später der
    /// Folgemonat bzw. ein Neueintritt in diesem Jahr.
    /// </summary>
    public static DateOnly? ErsterTag(int jahr, DateOnly? eintritt, DateOnly? folgemonat)
    {
        var start = new DateOnly(jahr, 1, 1);
        var ende = new DateOnly(jahr, 12, 31);
        if (folgemonat != null && folgemonat > ende) return null;
        if (eintritt != null && eintritt > ende) return null;
        if (folgemonat != null && folgemonat > start) start = folgemonat.Value;
        if (eintritt != null && eintritt > start) start = eintritt.Value;
        return start.Year == jahr ? start : null;
    }

    public static int MonateImJahr(DateOnly? erster, int jahr)
    {
        if (erster == null || erster.Value.Year > jahr) return 0;
        if (erster.Value.Year < jahr) return 12;
        return 12 - erster.Value.Month + 1;
    }

    /// <summary>
    /// Jahr ist gesperrt, sobald eine bestätigte Lohnabrechnung in einem Monat
    /// vorliegt, in dem der Freibetrag schon gilt. Die Lohnabrechnung im
    /// Erreichensmonat selbst sperrt noch nicht.
    /// </summary>
    public static HashSet<int> GesperrteJahre(
        IEnumerable<(int Jahr, int Monat)> lohnmonate,
        Func<int, int, bool> freibetragGilt)
    {
        var set = new HashSet<int>();
        foreach (var (jahr, monat) in lohnmonate)
            if (freibetragGilt(jahr, monat)) set.Add(jahr);
        return set;
    }

    public static string? Pruefe(
        IReadOnlyList<Spanne> vorher,
        IReadOnlyList<Spanne> nachher,
        IReadOnlySet<int> gesperrteJahre,
        DateOnly? eintritt,
        DateOnly? folgemonat)
    {
        if (!Gleich(vorher, nachher))
        {
            var form = PruefeForm(nachher, eintritt, folgemonat);
            if (form != null) return form;
        }
        foreach (var jahr in gesperrteJahre.OrderBy(j => j))
        {
            if (JahrGleich(vorher, nachher, jahr)) continue;
            return $"Die Wahl für {jahr} ist mit der ersten Lohnabrechnung festgelegt und gilt bis Ende Jahr. Eine Änderung ist ab 1.1.{jahr + 1} möglich.";
        }
        return null;
    }

    public static string Hinweis(DateOnly heute, DateOnly? folgemonat, DateOnly? ersterImJahr, bool gesperrt)
    {
        if (folgemonat == null)
            return "Verzicht auf den Freibetrag braucht ein Geburtsdatum.";
        if (folgemonat.Value.Year > heute.Year)
            return $"Der Freibetrag gilt ab {MonatName(folgemonat.Value)} {folgemonat.Value.Year}. Vorher gibt es keinen Freibetrag.";

        var jahr = heute.Year;
        var start = ersterImJahr ?? folgemonat.Value;
        var monate = MonateImJahr(start, jahr);
        var betrag = Franken((int)FreibetragMonat * monate);
        if (gesperrt)
            return $"Die Wahl für {jahr} ist mit der ersten Lohnabrechnung festgelegt. Der Freibetrag gilt ab {MonatName(start)} ({monate} × CHF 1'400 = CHF {betrag}). Eine Änderung ist ab 1.1.{jahr + 1} möglich. Ohne neue Mitteilung bleibt die bisherige Wahl.";

        if (folgemonat.Value.Year == jahr)
        {
            var refMonat = folgemonat.Value.AddMonths(-1);
            return $"Referenzalter im {MonatName(refMonat)} {refMonat.Year}. Ab {MonatName(start)} gilt der Freibetrag automatisch: {monate} × CHF 1'400 = CHF {betrag} bis Dezember. Ein Verzicht darauf ist ab {MonatName(start)} möglich, spätestens bei der ersten Lohnabrechnung ab {MonatName(start)}. Ab 1.1.{jahr + 1} kann die Wahl wieder ändern. Ohne neue Mitteilung bleibt sie.";
        }
        if (start.Year == jahr && start.Month > 1)
            return $"Ab {MonatName(start)} gilt der Freibetrag automatisch: {monate} × CHF 1'400 = CHF {betrag} bis Dezember. Ein Verzicht muss vor der ersten Lohnabrechnung ab {MonatName(start)} feststehen. Ab 1.1.{jahr + 1} kann die Wahl wieder ändern.";

        return $"Der Freibetrag von CHF 1'400 pro Monat gilt automatisch. Ein Verzicht muss vor der ersten Lohnabrechnung {jahr} feststehen und gilt das ganze Jahr. Ab 1.1.{jahr + 1} kann die Wahl wieder ändern. Ohne neue Mitteilung bleibt sie.";
    }

    static string? PruefeForm(IReadOnlyList<Spanne> spannen, DateOnly? eintritt, DateOnly? folgemonat)
    {
        var jahre = new HashSet<int>();
        foreach (var s in spannen)
        {
            jahre.Add(s.Von.Year);
            if (s.Bis != null) jahre.Add(s.Bis.Value.Year);
        }
        foreach (var jahr in jahre.OrderBy(j => j))
        {
            var erster = ErsterTag(jahr, eintritt, folgemonat);
            if (erster == null) continue;
            bool? lauf = null;
            var wechsel = 0;
            for (var d = erster.Value; d.Year == jahr; d = d.AddDays(1))
            {
                var g = GiltAm(spannen, d);
                if (lauf == null) lauf = g;
                else if (g != lauf) { wechsel++; lauf = g; }
            }
            if (wechsel == 0) continue;
            if (!GiltAm(spannen, erster.Value))
                return $"Der Verzicht muss ab {erster.Value:dd.MM.yyyy} für den Rest des Jahres gelten. Ab 1.1.{jahr + 1} kann die Wahl wieder ändern.";
            return $"Der Verzicht gilt bis Ende {jahr}. Ein Ende während des Jahres ist nicht möglich, nur der 31.12.{jahr}. Ab 1.1.{jahr + 1} gilt dann wieder der Freibetrag.";
        }
        return null;
    }

    static bool JahrGleich(IReadOnlyList<Spanne> a, IReadOnlyList<Spanne> b, int jahr)
    {
        for (var d = new DateOnly(jahr, 1, 1); d.Year == jahr; d = d.AddDays(1))
            if (GiltAm(a, d) != GiltAm(b, d)) return false;
        return true;
    }

    static bool Gleich(IReadOnlyList<Spanne> a, IReadOnlyList<Spanne> b)
    {
        if (a.Count != b.Count) return false;
        var aa = a.OrderBy(s => s.Von).ThenBy(s => s.Bis ?? DateOnly.MaxValue).ToList();
        var bb = b.OrderBy(s => s.Von).ThenBy(s => s.Bis ?? DateOnly.MaxValue).ToList();
        for (var i = 0; i < aa.Count; i++)
            if (aa[i].Von != bb[i].Von || aa[i].Bis != bb[i].Bis) return false;
        return true;
    }

    static string MonatName(DateOnly d)
        => d.Month switch
        {
            1 => "Januar", 2 => "Februar", 3 => "März", 4 => "April",
            5 => "Mai", 6 => "Juni", 7 => "Juli", 8 => "August",
            9 => "September", 10 => "Oktober", 11 => "November", _ => "Dezember",
        };

    static string Franken(int n) => n >= 1000 ? $"{n / 1000}'{n % 1000:000}" : n.ToString();
}
