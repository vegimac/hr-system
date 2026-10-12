namespace HrSystem.Services;

/// <summary>
/// Soll-Wert «Familienbetreuung» (ArG Art. 36: Erziehung von Kindern bis 15) —
/// Walter-Vorgabe 12.10.2026. Grundlage: Kinder im Haushalt aus dem Familie-Tab.
///
/// Pro Kind gilt die Zeit von der Geburt bis zum Tag vor dem 15. Geburtstag;
/// überlappende oder direkt anschliessende Zeiten werden zusammengelegt. Beginn
/// frühestens am massgebenden Eintritt (<see cref="Dienstalter.Massgebend"/>).
/// Massgebend ist der Zeitraum, der am Stichtag gilt, sonst der nächste künftige;
/// sind alle vorbei bzw. gibt es kein Kind unter 15 ⇒ «Nein».
/// </summary>
public static class FamilienbetreuungSoll
{
    public record Kind(DateOnly? Geburt);

    public record Ergebnis(
        bool? Ja,                     // null = nicht berechenbar (Geburtsdatum fehlt bei allen Kindern)
        DateOnly? Von,
        DateOnly? Bis,
        DateOnly? JuengstesKind15Am,  // nur bei Ja
        bool GeburtsdatumFehlt);

    public static Ergebnis Berechne(IEnumerable<Kind> kinderImHaushalt, DateOnly? eintritt, DateOnly stichtag)
    {
        var kinder = kinderImHaushalt.ToList();
        bool fehlt = kinder.Any(k => k.Geburt == null);
        var mitDatum = kinder.Where(k => k.Geburt.HasValue).Select(k => k.Geburt!.Value).OrderBy(d => d).ToList();
        if (mitDatum.Count == 0)
            return new Ergebnis(fehlt ? null : false, null, null, null, fehlt);

        var zeiten = new List<(DateOnly Von, DateOnly Bis)>();
        foreach (var geb in mitDatum)
        {
            var von = geb;
            var bis = geb.AddYears(15).AddDays(-1);
            if (zeiten.Count > 0 && von <= zeiten[^1].Bis.AddDays(1))
                zeiten[^1] = (zeiten[^1].Von, bis > zeiten[^1].Bis ? bis : zeiten[^1].Bis);
            else
                zeiten.Add((von, bis));
        }

        var gekuerzt = zeiten
            .Select(z => (Von: eintritt.HasValue && eintritt.Value > z.Von ? eintritt.Value : z.Von, z.Bis))
            .Where(z => z.Von <= z.Bis)
            .ToList();
        var treffer = gekuerzt.FirstOrDefault(z => z.Von <= stichtag && stichtag <= z.Bis);
        if (treffer == default) treffer = gekuerzt.FirstOrDefault(z => z.Von > stichtag);
        if (treffer == default)
            return new Ergebnis(fehlt ? null : false, null, null, null, fehlt);

        return new Ergebnis(true, treffer.Von, treffer.Bis, treffer.Bis.AddDays(1), fehlt);
    }

    /// <summary>Abweichung je Feld (easy@work gegen Soll). Bei «Nein» zählen keine Daten.</summary>
    public static (bool Ja, bool Von, bool Bis) Abweichung(
        bool istJa, DateOnly? istVon, DateOnly? istBis, Ergebnis soll)
    {
        if (soll.Ja == null) return (false, false, false);
        bool sollJa = soll.Ja.Value;
        DateOnly? iv = istJa ? istVon : null, ib = istJa ? istBis : null;
        DateOnly? sv = sollJa ? soll.Von : null, sb = sollJa ? soll.Bis : null;
        return (istJa != sollJa, iv != sv, ib != sb);
    }
}
