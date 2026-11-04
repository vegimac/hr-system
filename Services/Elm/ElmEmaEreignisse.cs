namespace HrSystem.Services.Elm;

/// <summary>
/// EMA-Ereignisse (NotifyChanges) für AHV, FAK und BVG, RL 8.1.9, 8.2.7, 8.6.8. Rein: Eingabe ist der
/// Monatsstand einer Person. Abgeglichen gegen alle 291 Ereignisse aus SWISSCEC/RefXML/*_EMA.xml.
/// Was RL 8.6.8 ausdrücklich dem Anwender überlässt (Arbeitsunterbruch, Teilpensionierung,
/// Arbeitsunfähigkeit), kommt als Eingabe. Es wird nie aus den Daten geraten.
/// </summary>
public static class ElmEmaEreignisse
{
    public const string Ahv = "AHV";
    public const string Fak = "FAK";
    public const string Bvg = "BVG";
    public const string Entry = "Entry";
    public const string Mutation = "Mutation";
    public const string Withdrawal = "Withdrawal";

    /// <summary>
    /// Ohne festes Pensum löst erst eine Abweichung über diesem Wert eine Lohnänderung aus.
    /// Abgeleitet, siehe docs/swissdec-abgeleitete-regeln.md Nr. 15: Der Lohntest trägt 10 bis 14 %.
    /// </summary>
    public const decimal SchwelleProzentOhnePensum = 10m;

    public sealed record Ereignis(
        DateOnly Monat, string Domaene, string Art, DateOnly ValidAsOf, string? Grund,
        string? BvgCode = null, decimal? Basis = null, string? Kanton = null, string? AlterBvgCode = null);

    public sealed record Manuell(DateOnly Monat, string Art, string Grund, DateOnly? ValidAsOf = null);

    public sealed record Monatsstand(
        DateOnly Monat,
        DateOnly? Eintritt,
        DateOnly? Austritt,
        string? Arbeitskanton,
        bool BvgVersichert,
        string? BvgCode,
        decimal? ManuelleBasis,
        /// <summary>H Stunden-/Lektionenlohn, M Monatslohn, N ohne Zeitvorgabe. Ein Wechsel beginnt die Rückschau neu (RL 8.6.6.2).</summary>
        char Vertragsart,
        ElmBvgBasis.Vertrag Vertrag,
        IReadOnlyDictionary<string, decimal> Lohnarten);

    public sealed record Person(
        DateOnly Geburt,
        string? Geschlecht,
        DateOnly? Todestag,
        bool AhvSonderfall,
        IReadOnlyList<Monatsstand> Monate,
        /// <summary>Zivilstand gültig ab, erfasst im Monat.</summary>
        IReadOnlyList<(DateOnly Datum, DateOnly Erfasst)> Zivilstandswechsel,
        /// <summary>Wegzugsdatum, erfasst im Monat.</summary>
        IReadOnlyList<(DateOnly Datum, DateOnly Erfasst)> Umzuege,
        IReadOnlyList<Manuell> Manuelle,
        /// <summary>Basis aus der voraussichtlichen Jahresmeldung: Monat des GeneralValidAsOf → Basis.</summary>
        IReadOnlyDictionary<DateOnly, decimal> Jahresmeldungen);

    /// <summary>Tag, an dem das Referenzalter erreicht ist. Austritt ab diesem Tag gilt als Pensionierung (TF16 Aebi: 11 Tage davor nicht).</summary>
    public static DateOnly Referenzdatum(DateOnly geburt, string? geschlecht)
        => geburt.AddMonths(PayrollCalculations.GetReferenzalterMonate(geschlecht, geburt.Year));

    /// <summary>AHV-Pflicht ab dem 1.1. nach dem 17. Geburtstag (AHVG Art. 3, TF13 Combertaldi).</summary>
    public static DateOnly AhvPflichtAb(DateOnly geburt) => new(geburt.Year + 18, 1, 1);

    /// <summary>Datum im Monat bleibt nur am 1.; sonst gilt der Erste des Folgemonats.</summary>
    public static DateOnly ErsterFolgemonat(DateOnly d)
        => d.Day == 1 ? d : new DateOnly(d.Year, d.Month, 1).AddMonths(1);

    public static List<Ereignis> Erkenne(Person p, Func<string, ElmBvgBasis.Steuerung?> steuerung, DateOnly? festerMeldemonat = null)
    {
        var res = new List<Ereignis>();
        var monate = p.Monate.OrderBy(m => m.Monat).ToList();
        var laeufe = monate.Select(m => new ElmBvgBasis.LohnMonat(m.Monat, m.Lohnarten)).ToList();

        DateOnly? eintrittAlt = null;
        bool aktiv = false, ahvGemeldet = false, bvg = false;
        decimal? letzteBasis = null, manuAlt = null, voraussichtlichAlt = null;
        string? code = null;
        bool nachUnterbruch = false;
        DateOnly rueckschauAb = default;
        char vertragsart = '?';
        Monatsstand? vor = null;

        for (int i = 0; i < monate.Count; i++)
        {
            var s = monate[i];
            var monat = s.Monat;
            var ersterDesMonats = new DateOnly(monat.Year, monat.Month, 1);
            var letzterDesMonats = ersterDesMonats.AddMonths(1).AddDays(-1);
            var naechster = i + 1 < monate.Count ? monate[i + 1] : null;
            void Add(string domaene, string art, DateOnly ab, string? grund, string? c = null, decimal? b = null, string? kt = null)
                => res.Add(new Ereignis(monat, domaene, art, ab, grund, c, b, kt));

            bool neu = s.Eintritt is { } e0 && e0 <= letzterDesMonats && e0 != eintrittAlt;
            if (neu) eintrittAlt = s.Eintritt;

            if (vor != null && aktiv && ahvGemeldet && !neu && !Gleich(vor.Arbeitskanton, s.Arbeitskanton))
            {
                Add(Fak, Entry, ersterDesMonats, "cantonChange", kt: s.Arbeitskanton);
                Add(Fak, Withdrawal, ersterDesMonats.AddDays(-1), "cantonChange", kt: vor.Arbeitskanton);
            }
            if (neu) aktiv = true;
            if (aktiv && !ahvGemeldet && !p.AhvSonderfall && s.Eintritt is { } e1 && letzterDesMonats >= AhvPflichtAb(p.Geburt))
            {
                var ab = e1 > AhvPflichtAb(p.Geburt) ? e1 : AhvPflichtAb(p.Geburt);
                ahvGemeldet = true;
                Add(Ahv, Entry, ab, null);
                Add(Fak, Entry, ab, "entryCompany", kt: s.Arbeitskanton);
            }

            bool bvgNeu = false;
            var manuellEin = p.Manuelle.FirstOrDefault(m => m.Monat == monat && m.Art == Entry);
            if (s.BvgVersichert && aktiv && (!bvg || (neu && manuellEin != null)))
            {
                var ab = neu ? s.Eintritt!.Value : ersterDesMonats;
                bvg = true; bvgNeu = true;
                rueckschauAb = ab;
                vertragsart = s.Vertragsart;
                code = s.BvgCode;
                voraussichtlichAlt = ElmBvgBasis.Voraussichtlich(s.Vertrag, s.Lohnarten, steuerung);
                letzteBasis = s.ManuelleBasis ?? ElmBvgBasis.Runde5Rappen(voraussichtlichAlt.Value);
                // Der Eintritt nach einem gemeldeten Arbeitsunterbruch beendet diesen (TF40 Farine).
                Add(Bvg, Entry, ab, manuellEin?.Grund ?? (nachUnterbruch ? "interruptionOfEmployment" : "entryPensionPlan"), code, letzteBasis);
                nachUnterbruch = false;
            }
            // Der Januar stützt sich auf die voraussichtliche Jahresmeldung: freigegeben, sonst dieselbe Rechnung (TF37 Oberli).
            if (bvg && !bvgNeu && monat.Month == 1)
            {
                letzteBasis = p.Jahresmeldungen.TryGetValue(ersterDesMonats, out var jm) ? jm
                    : ElmBvgBasis.FuerJahresmeldung(ersterDesMonats, s.ManuelleBasis, s.Vertrag, s.Lohnarten, laeufe, rueckschauAb, steuerung);
                voraussichtlichAlt = ElmBvgBasis.Voraussichtlich(s.Vertrag, s.Lohnarten, steuerung);
            }

            if (bvg && !bvgNeu)
            {
                if (s.Vertragsart != vertragsart) { vertragsart = s.Vertragsart; rueckschauAb = ersterDesMonats; }
                var voraus = ElmBvgBasis.Voraussichtlich(s.Vertrag, s.Lohnarten, steuerung);
                decimal? neueBasis = null;
                if (s.ManuelleBasis is { } manu)
                {
                    if (manu != manuAlt && manu != letzteBasis) neueBasis = manu;
                }
                else
                {
                    var rechnung = ElmBvgBasis.Runde5Rappen(voraus + ElmBvgBasis.Rueckwirkend(
                        s.Vertrag.FestesPensum, laeufe, monat, rueckschauAb, steuerung));
                    if (vor != null && voraus != voraussichtlichAlt)
                        neueBasis = rechnung;
                    // Ohne festes Pensum ändert kein Vertragswert: Nur die Abweichung der Rückschau zeigt die Lohnänderung.
                    else if (s.Vertragsart == 'H' && !s.Vertrag.FestesPensum && letzteBasis is > 0m
                             && Math.Abs(rechnung - letzteBasis.Value) / letzteBasis.Value * 100m > SchwelleProzentOhnePensum
                             && !(s.Austritt is { } a0 && a0 <= letzterDesMonats))
                        neueBasis = rechnung;
                }
                voraussichtlichAlt = voraus;
                if (neueBasis is { } nb && nb != letzteBasis)
                {
                    letzteBasis = nb;
                    Add(Bvg, Mutation, ersterDesMonats, "changeSalary", s.BvgCode, letzteBasis);
                }
                foreach (var m in p.Manuelle.Where(m => m.Monat == monat && m.Art == Mutation))
                    Add(Bvg, Mutation, m.ValidAsOf ?? ersterDesMonats, m.Grund, s.BvgCode, letzteBasis);
                if (!Gleich(s.BvgCode, code))
                {
                    res.Add(new Ereignis(monat, Bvg, Mutation, ersterDesMonats, "changeBVG-LPP-Code", s.BvgCode, letzteBasis, AlterBvgCode: code));
                    code = s.BvgCode;
                }
            }
            manuAlt = s.ManuelleBasis;

            if (bvg)
            {
                foreach (var (grund, liste) in new[] { ("civilstate", p.Zivilstandswechsel), ("residence", p.Umzuege) })
                    foreach (var (datum, erfasst) in liste)
                    {
                        var ab = ErsterFolgemonat(datum);
                        var meldemonat = ab > erfasst ? new DateOnly(ab.Year, ab.Month, 1) : new DateOnly(erfasst.Year, erfasst.Month, 1);
                        if (meldemonat == ersterDesMonats) Add(Bvg, Mutation, ab, grund, code, letzteBasis);
                    }
            }

            if (s.Austritt is { } aus && aus >= ersterDesMonats && aus <= letzterDesMonats)
            {
                if (ahvGemeldet)
                {
                    Add(Ahv, Withdrawal, aus, null);
                    Add(Fak, Withdrawal, aus, "withdrawalCompany", kt: s.Arbeitskanton);
                }
                aktiv = false; ahvGemeldet = false;
                if (bvg)
                {
                    var grund = AustrittsGrund(p, monat, aus);
                    nachUnterbruch = grund.StartsWith("interruptionOfEmployment", StringComparison.Ordinal);
                    Add(Bvg, Withdrawal, aus, grund, code, letzteBasis);
                    bvg = false;
                }
            }
            else if (bvg && naechster is { BvgVersichert: false })
            {
                // Versicherung endet im Folgemonat ohne Austritt: gemeldet per Monatsende (TF03, TF05, TF44, TF02).
                Add(Bvg, Withdrawal, letzterDesMonats, AustrittsGrund(p, monat, letzterDesMonats), code, letzteBasis);
                bvg = false;
            }
            vor = s;
        }
        return festerMeldemonat is { } nur ? res.Where(r => r.Monat == nur).ToList() : res;
    }

    /// <summary>
    /// Versichert am Stichtag der Jahresmeldung? Liefert den Beginn der laufenden BVG-Periode,
    /// sonst null. Austritt am Vortag zählt nicht mehr (TF07 Burri 31.12.2024 fehlt in der Prospective 2025).
    /// </summary>
    public static DateOnly? BvgPeriodeAm(IEnumerable<Ereignis> ereignisse, DateOnly stichtag)
    {
        DateOnly? ab = null;
        foreach (var e in ereignisse.Where(e => e.Domaene == Bvg && e.ValidAsOf <= stichtag)
                     .OrderBy(e => e.ValidAsOf).ThenBy(e => e.Art == Withdrawal ? 0 : 1))
        {
            if (e.Art == Entry) ab = e.ValidAsOf;
            else if (e.Art == Withdrawal && e.ValidAsOf < stichtag) ab = null;
        }
        return ab;
    }

    /// <summary>
    /// XML der Ereignisse einer Person, in der Reihenfolge AHV, FAK, BVG (XSD). FullyFitForWork nur
    /// bei BVG-Eintritt und -Austritt und nur, wenn der Anwender die Frage mit Ja beantwortet hat (RL 8.6.8.2).
    /// </summary>
    public static IEnumerable<System.Xml.Linq.XElement> Bloecke(
        IReadOnlyList<Ereignis> ereignisse, Func<Ereignis, string> adressat, Func<Ereignis, string> workId,
        Func<Ereignis, bool> vollArbeitsfaehig)
    {
        var sd = ElmGemeinsam.Sd;
        System.Xml.Linq.XElement Kopf(Ereignis e) => new(sd + e.Art,
            new System.Xml.Linq.XElement(sd + "ValidAsOf", e.ValidAsOf.ToString("yyyy-MM-dd")),
            e.Grund == null ? null : new System.Xml.Linq.XElement(sd + "Reason", e.Grund),
            e.Domaene == Bvg && e.Art != Mutation && vollArbeitsfaehig(e) ? new System.Xml.Linq.XElement(sd + "FullyFitForWork") : null);

        var ahv = ereignisse.Where(e => e.Domaene == Ahv).ToList();
        if (ahv.Count > 0)
            yield return new(sd + "AHV-AVS-ChangeNotifications", ahv.Select(e => new System.Xml.Linq.XElement(sd + "AHV-AVS-ChangeNotification",
                new System.Xml.Linq.XAttribute("addresseeIDRef", adressat(e)), Kopf(e))));
        var fak = ereignisse.Where(e => e.Domaene == Fak).ToList();
        if (fak.Count > 0)
            yield return new(sd + "FAK-CAF-ChangeNotifications", fak.Select(e => new System.Xml.Linq.XElement(sd + "FAK-CAF-ChangeNotification",
                new System.Xml.Linq.XAttribute("addresseeIDRef", adressat(e)), Kopf(e),
                new System.Xml.Linq.XElement(sd + "FAK-CAF-WorkplaceCanton", e.Kanton))));
        var bvg = ereignisse.Where(e => e.Domaene == Bvg).ToList();
        if (bvg.Count > 0)
            yield return new(sd + "BVG-LPP-ChangeNotifications", bvg.Select(e => new System.Xml.Linq.XElement(sd + "BVG-LPP-ChangeNotification",
                new System.Xml.Linq.XAttribute("addresseeIDRef", adressat(e)),
                new System.Xml.Linq.XAttribute("workIDRef", workId(e)),
                Kopf(e),
                new System.Xml.Linq.XElement(sd + "BVG-LPP-Code",
                    e.AlterBvgCode == null ? null : new System.Xml.Linq.XAttribute("oldCode", e.AlterBvgCode), e.BvgCode),
                new System.Xml.Linq.XElement(sd + "BVG-LPP-AnnualBasis", ElmGemeinsam.Amt(e.Basis ?? 0m)))));
    }

    private static string AustrittsGrund(Person p, DateOnly monat, DateOnly datum)
    {
        var m = p.Manuelle.FirstOrDefault(x => x.Monat == monat && x.Art == Withdrawal);
        if (m != null) return m.Grund;
        if (p.Todestag != null && p.Todestag <= datum) return "deceased";
        return datum >= Referenzdatum(p.Geburt, p.Geschlecht) ? "retirement" : "withdrawalPensionPlan";
    }

    private static bool Gleich(string? a, string? b)
        => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
}
