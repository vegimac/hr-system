namespace HrSystem.Services;

/// <summary>Ausbildungs-Abschnitt eines Kindes (Sachverhalt). Von null = Beginn unbekannt (Altbestand).</summary>
public record KindAusbildung(
    DateOnly? Von,
    DateOnly? Bis,
    bool? Erstausbildung,          // null = ungeklärt
    bool Geprueft = true,
    string? Art = null,
    string? EndeGrund = null);

/// <summary>Verhältnis-Version ab Datum: Haushalt + Unterhalt zur Hauptsache (null = ungeklärt).</summary>
public record KindVerhaeltnis(DateOnly GueltigAb, bool? ImHaushalt, bool? UnterhaltHauptsache);

/// <summary>QST-Entscheid zum Kind: ANERKANNT / NICHT_ANERKANNT / IN_PRUEFUNG.</summary>
public record KindQstEntscheid(string Status, DateOnly? Von, DateOnly? Bis);

/// <summary>
/// Alles, was die QST-Kinderziffer eines Kindes bestimmt. Unterhalt steht nur
/// noch im Verlauf (Verhältnis-Versionen), seit 11.10.2026 kein Häkchen mehr.
/// </summary>
public record KindQstDaten(
    DateOnly? Geburt,
    IReadOnlyList<KindAusbildung> Ausbildungen,
    IReadOnlyList<KindVerhaeltnis> Verhaeltnisse,
    IReadOnlyList<KindQstEntscheid> Entscheide,
    // Haushalt, wenn keine Verhältnis-Version existiert (Snapshot am Kind).
    bool? HaushaltOhneVersion = null);

/// <summary>Ergebnis für einen Monat.</summary>
public record KindQstMonat(
    bool Zaehlt,
    bool? ImHaushalt,              // null = ungeklärt
    string Grund,
    IReadOnlyList<string> Offen);  // gelbe Punkte (ungeklärt / nicht geprüft / in Prüfung)

/// <summary>Zusammenhängende Monate mit gleichem Grund, in denen das Kind zählt.</summary>
public record KindQstWirkung(DateOnly VonMonat, DateOnly? BisMonat, string Grund);

/// <summary>
/// QST-Kinderziffer nach dem Verlauf am Kind (Walter 11.10.2026,
/// docs/kind-qst-verlauf-konzept.md). Einzige Regel-Stelle — Tarifvorschlag,
/// QST-Pflicht-Check, Familie-Karte und ELM rufen hier an.
///
/// Monatsregel KS 45 Ziff. 4.1: eine Änderung wirkt ab dem Folgemonat.
/// Ein Abschnitt [von, bis] zählt vom Monat nach «von» bis und mit dem Monat
/// von «bis»; lückenlos anschliessende Abschnitte gelten als einer.
/// Verhältnis-Versionen gelten ab dem Monat nach «gültig ab».
/// QST-Entscheide gelten so wie datiert: im Monat, dessen Erster im
/// Entscheid-Zeitraum liegt.
/// </summary>
public static class KindQstVerlauf
{
    public const string Anerkannt = "ANERKANNT";
    public const string NichtAnerkannt = "NICHT_ANERKANNT";
    public const string InPruefung = "IN_PRUEFUNG";

    static int MonatsIndex(DateOnly d) => d.Year * 12 + d.Month - 1;
    static DateOnly AusIndex(int i) => new(i / 12, i % 12 + 1, 1);

    record Abschnitt(DateOnly? Von, DateOnly? Bis, string Grund, bool Geprueft);

    /// <summary>Sachverhalt-Abschnitte (Minderjährigkeit + Erstausbildung = ja), zusammengelegt.</summary>
    static List<Abschnitt> Grundabschnitte(KindQstDaten d)
    {
        var roh = new List<Abschnitt>();
        if (d.Geburt.HasValue)
            roh.Add(new Abschnitt(d.Geburt, d.Geburt.Value.AddYears(18), "unter 18", true));
        foreach (var a in d.Ausbildungen.Where(a => a.Erstausbildung == true))
            roh.Add(new Abschnitt(a.Von, a.Bis, AusbildungText(a), a.Geprueft));

        var sortiert = roh.OrderBy(a => a.Von ?? DateOnly.MinValue).ToList();
        var liste = new List<Abschnitt>();
        foreach (var a in sortiert)
        {
            if (liste.Count > 0)
            {
                var l = liste[^1];
                bool anschluss = l.Bis == null || a.Von == null || a.Von.Value <= l.Bis.Value.AddDays(1);
                if (anschluss)
                {
                    var bis = l.Bis == null || a.Bis == null ? null : (DateOnly?)(a.Bis > l.Bis ? a.Bis : l.Bis);
                    liste[^1] = new Abschnitt(l.Von, bis, GruendeVerbinden(l.Grund, a.Grund),
                        l.Geprueft && a.Geprueft);
                    continue;
                }
            }
            liste.Add(a);
        }
        return liste;
    }

    // «unter 18» steht immer vorne, auch wenn ein Abschnitt ohne Beginn (Altbestand) früher sortiert.
    static string GruendeVerbinden(string a, string b)
    {
        var teile = a.Split(" / ").Concat(b.Split(" / ")).Distinct().ToList();
        return string.Join(" / ", teile.OrderBy(t => t == "unter 18" ? 0 : 1));
    }

    static string AusbildungText(KindAusbildung a) => (a.Art ?? "").ToUpperInvariant() switch
    {
        "LEHRE" => "Lehre",
        "SCHULE" => "Schule",
        "STUDIUM" => "Studium",
        "BRUECKE" => "Brückenangebot",
        _ => "Ausbildung",
    };

    static bool AbschnittWirkt(DateOnly? von, DateOnly? bis, int monat)
        => (von == null || MonatsIndex(von.Value) < monat)
        && (bis == null || MonatsIndex(bis.Value) >= monat);

    static bool UmfasstErsten(DateOnly? von, DateOnly? bis, DateOnly erster)
        => (von == null || von.Value <= erster) && (bis == null || bis.Value >= erster);

    static KindVerhaeltnis? VerhaeltnisImMonat(KindQstDaten d, int monat)
    {
        if (d.Verhaeltnisse.Count == 0) return null;
        var sort = d.Verhaeltnisse.OrderBy(v => v.GueltigAb).ToList();
        KindVerhaeltnis? treffer = null;
        foreach (var v in sort)
            if (MonatsIndex(v.GueltigAb) < monat) treffer = v;
        // Vor der ersten Version: die erste gilt rückwärts (Altbestand ab Geburt).
        return treffer ?? sort[0];
    }

    public static KindQstMonat ImMonat(KindQstDaten d, int jahr, int monat)
        => ImMonat(d, jahr * 12 + monat - 1);

    public static KindQstMonat AmStichtag(KindQstDaten d, DateOnly stichtag)
        => ImMonat(d, MonatsIndex(stichtag));

    static KindQstMonat ImMonat(KindQstDaten d, int idx)
    {
        var offen = new List<string>();
        var erster = AusIndex(idx);
        var verh = VerhaeltnisImMonat(d, idx);
        bool? haushalt = verh != null ? verh.ImHaushalt : d.HaushaltOhneVersion;
        bool? unterhalt = verh != null ? verh.UnterhaltHauptsache : true;

        var entscheid = d.Entscheide
            .Where(e => UmfasstErsten(e.Von, e.Bis, erster))
            .OrderBy(e => e.Von ?? DateOnly.MinValue)
            .LastOrDefault();
        if (entscheid?.Status == NichtAnerkannt)
            return new KindQstMonat(false, haushalt, "QST-Entscheid: nicht anerkannt", offen);
        if (entscheid?.Status == Anerkannt)
        {
            if (haushalt == null) offen.Add("Haushalt ungeklärt");
            return new KindQstMonat(true, haushalt, "QST-Entscheid: anerkannt", offen);
        }
        if (entscheid?.Status == InPruefung) offen.Add("QST-Entscheid in Prüfung");

        if (d.Geburt.HasValue && MonatsIndex(d.Geburt.Value) >= idx)
            return new KindQstMonat(false, haushalt, "noch nicht geboren", offen);

        var abschnitt = Grundabschnitte(d).FirstOrDefault(a => AbschnittWirkt(a.Von, a.Bis, idx));
        if (abschnitt == null)
        {
            if (d.Ausbildungen.Any(a => a.Erstausbildung == null && AbschnittWirkt(a.Von, a.Bis, idx)))
                offen.Add("Erstausbildung ungeklärt");
            string grund = d.Geburt.HasValue ? "über 18, keine Erstausbildung" : "Geburtsdatum fehlt";
            return new KindQstMonat(false, haushalt, grund, offen);
        }

        if (unterhalt == false)
            return new KindQstMonat(false, haushalt, "Unterhalt nicht zur Hauptsache", offen);
        if (unterhalt == null)
        {
            offen.Add("Unterhalt ungeklärt");
            return new KindQstMonat(false, haushalt, "Unterhalt ungeklärt", offen);
        }
        if (!abschnitt.Geprueft) offen.Add("Ausbildung nicht geprüft");
        if (haushalt == null) offen.Add("Haushalt ungeklärt");
        return new KindQstMonat(true, haushalt, abschnitt.Grund, offen);
    }

    /// <summary>
    /// Monate, in denen das Kind zählt — zusammengefasst nach Grund. BisMonat
    /// null = läuft offen weiter. <paramref name="heute"/> bestimmt, wie weit
    /// gerechnet wird.
    /// </summary>
    public static IReadOnlyList<KindQstWirkung> Wirkung(KindQstDaten d, DateOnly heute)
    {
        var daten = new List<DateOnly> { heute };
        if (d.Geburt.HasValue) daten.Add(d.Geburt.Value.AddYears(18));
        foreach (var a in d.Ausbildungen) { if (a.Von.HasValue) daten.Add(a.Von.Value); if (a.Bis.HasValue) daten.Add(a.Bis.Value); }
        foreach (var v in d.Verhaeltnisse) daten.Add(v.GueltigAb);
        foreach (var e in d.Entscheide) { if (e.Von.HasValue) daten.Add(e.Von.Value); if (e.Bis.HasValue) daten.Add(e.Bis.Value); }

        int start = d.Geburt.HasValue
            ? MonatsIndex(d.Geburt.Value) + 1
            : MonatsIndex(daten.Min()) ;
        var fruehesterEntscheid = d.Entscheide.Where(e => e.Von.HasValue).Select(e => MonatsIndex(e.Von!.Value)).DefaultIfEmpty(start).Min();
        start = Math.Min(start, fruehesterEntscheid);
        int ende = MonatsIndex(daten.Max()) + 2;

        var liste = new List<KindQstWirkung>();
        int? laufVon = null; string laufGrund = "";
        for (int i = start; i <= ende; i++)
        {
            var m = ImMonat(d, i);
            if (m.Zaehlt && laufVon != null && m.Grund == laufGrund) continue;
            if (laufVon != null)
                liste.Add(new KindQstWirkung(AusIndex(laufVon.Value), AusIndex(i - 1), laufGrund));
            laufVon = m.Zaehlt ? i : null;
            laufGrund = m.Zaehlt ? m.Grund : "";
        }
        if (laufVon != null)
            liste.Add(new KindQstWirkung(AusIndex(laufVon.Value), null, laufGrund));
        return liste;
    }

    /// <summary>
    /// Kinderabzugs-Zeiträume für die ELM-Meldung (Sachverhalt, ohne Monats-
    /// verschiebung): anerkannte QST-Entscheide, wenn vorhanden; sonst
    /// Minderjährigkeit + Erstausbildung (zusammengelegt).
    /// </summary>
    public static IReadOnlyList<(DateOnly Von, DateOnly? Bis)> ElmZeitraeume(KindQstDaten d)
    {
        if (d.Verhaeltnisse.Count > 0 && d.Verhaeltnisse.All(v => v.UnterhaltHauptsache == false))
            return Array.Empty<(DateOnly, DateOnly?)>();
        var anerkannt = d.Entscheide.Where(e => e.Status == Anerkannt && e.Von.HasValue)
            .OrderBy(e => e.Von).Select(e => (e.Von!.Value, e.Bis)).ToList();
        if (anerkannt.Count > 0) return anerkannt;
        return Grundabschnitte(d)
            .Select(a => (a.Von ?? d.Geburt, a.Bis))
            .Where(a => a.Item1.HasValue)
            .Select(a => (a.Item1!.Value, a.Bis))
            .ToList();
    }

    /// <summary>
    /// Alter Zeitraum «QST abzugsberechtigt von/bis» → QST-Entscheide, die das
    /// bisherige Verhalten erhalten (Altbestand, Swissdec-Testdaten).
    /// Standard (Geburt bis 18. Geburtstag) ergibt keinen Entscheid.
    /// </summary>
    public static IReadOnlyList<KindQstEntscheid> EntscheideAusZeitraum(
        DateOnly? geburt, DateOnly? von, DateOnly? bis, bool erstausbildung)
    {
        if (von == null && bis == null) return Array.Empty<KindQstEntscheid>();
        bool standard = (von == null || von == geburt)
            && (bis == null || (geburt.HasValue && bis == geburt.Value.AddYears(18)));
        if (standard) return Array.Empty<KindQstEntscheid>();
        var start = von ?? geburt;
        var liste = new List<KindQstEntscheid> { new(Anerkannt, start, bis) };
        if (geburt.HasValue && start.HasValue && start.Value > geburt.Value)
            liste.Insert(0, new KindQstEntscheid(NichtAnerkannt, geburt, start.Value.AddDays(-1)));
        if (bis.HasValue && !erstausbildung)
            liste.Add(new KindQstEntscheid(NichtAnerkannt, bis.Value.AddDays(1), null));
        return liste;
    }
}
