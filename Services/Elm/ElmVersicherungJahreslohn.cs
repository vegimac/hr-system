namespace HrSystem.Services.Elm;

/// <summary>
/// Jahresmeldung UVG, UVGZ und KTG pro Person (Walter 02.11.2026, ELM 6.0 Richtlinien 8.3.7,
/// 8.4, 8.5). Ein Salary-Element je Beitragsdauer und Code: eine neue Beitragsdauer beginnt bei
/// Eintritt/Wiedereintritt und bei jedem Codewechsel. Beim KTG und UVGZ kann eine Person zwei
/// Codes gleichzeitig haben (11 Grundlohn + 12 Überschusslohn) — dann je Code eigene Zeilen.
///   • Brutto (nur UVG) = Summe aller aufbauenden Lohnarten.
///   • Basis = UVG-pflichtiger Lohn ohne Höchstlohn und ohne Rücksicht auf den Code.
///   • Beitragspflichtig = Basis der Abzugszeile im Lohnzettel (Höchstlohn, Lohnband und
///     Aufrollung hat die Lohnrechnung schon gemacht). Ohne Abzugszeile (UVG-Code 2/3 ohne
///     AN-Abzug) rechnet <see cref="Hoechstlohn"/> den Deckel der Beitragsdauer.
///   • KTG: Referenz = AHV-Lohn der Beitragsdauer.
/// Rein, ohne DB.
/// </summary>
public static class ElmVersicherungJahreslohn
{
    /// <summary>Ein Lohnmonat der Person für einen Code. Beitragspflichtig null = keine Abzugszeile.</summary>
    public readonly record struct Monat(int Nr, string Code, decimal Brutto, decimal Basis,
        decimal? Beitragspflichtig, decimal AhvLohn);

    public readonly record struct Abschnitt(int Von, int Bis, string Code, decimal Brutto, decimal Basis,
        decimal? Beitragspflichtig, decimal AhvLohn);

    /// <summary>
    /// Beitragsdauern: je Code die aufeinanderfolgenden Monate. Ein Monat ohne Lohnzettel
    /// unterbricht (Austritt und Wiedereintritt). Sortiert nach Beginn, dann Code.
    /// Beitragspflichtig bleibt null, wenn kein Monat der Dauer eine Abzugszeile hatte.
    /// </summary>
    public static List<Abschnitt> Abschnitte(IEnumerable<Monat> monate)
    {
        var result = new List<Abschnitt>();
        foreach (var jeCode in monate.Where(m => !string.IsNullOrWhiteSpace(m.Code))
                     .GroupBy(m => m.Code.Trim().ToUpperInvariant()))
        {
            var liste = jeCode.OrderBy(m => m.Nr).ToList();
            var i = 0;
            while (i < liste.Count)
            {
                var j = i;
                while (j + 1 < liste.Count && liste[j + 1].Nr == liste[j].Nr + 1) j++;
                decimal brutto = 0, basis = 0, ahv = 0, pflichtig = 0;
                var mitZeile = false;
                for (var k = i; k <= j; k++)
                {
                    brutto += liste[k].Brutto;
                    basis += liste[k].Basis;
                    ahv += liste[k].AhvLohn;
                    if (liste[k].Beitragspflichtig is decimal p) { pflichtig += p; mitZeile = true; }
                }
                result.Add(new Abschnitt(liste[i].Nr, liste[j].Nr, jeCode.Key, brutto, basis,
                    mitZeile ? pflichtig : null, ahv));
                i = j + 1;
            }
        }
        return result.OrderBy(a => a.Von).ThenBy(a => a.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Höchstlohn einer Beitragsdauer nach der Aufrollmethode: Monats-Höchstlohn je ganzen
    /// Monat, Teilmonat anteilig auf 30 Tage (wie die Lohnrechnung, Swissdec-Richtlinien).
    /// </summary>
    public static decimal Hoechstlohn(DateTime von, DateTime bis, decimal monatsHoechstlohn)
    {
        decimal summe = 0;
        for (var m = new DateTime(von.Year, von.Month, 1); m <= bis; m = m.AddMonths(1))
        {
            var ende = new DateTime(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));
            var a = von > m ? von : m;
            var b = bis < ende ? bis : ende;
            // 30-Tage-Basis wie die Lohnrechnung: Monatsletzter = Tag 30, auch als erster Tag.
            int start = PayrollCalculations.Tag30(a.Day, ende.Day);
            int schluss = b.Day >= ende.Day ? 30 : Math.Min(b.Day, 30);
            summe += monatsHoechstlohn * Math.Max(0, schluss - start + 1) / 30m;
        }
        return summe;
    }

    /// <summary>UVG-Code: zweite Stelle 0 = nicht versichert ⇒ kein UVG-Lohn.</summary>
    public static bool UvgVersichert(string code) => code.Trim().Length < 2 || code.Trim()[1] != '0';

    /// <summary>Eine gemeldete Zeile für die Totale (Geschlecht «F» oder «M», Betrag schon gerundet).</summary>
    public readonly record struct Zeile(string Code, string Geschlecht, decimal Betrag);

    public sealed record UvgBranche(string Betriebsteil, decimal FrauNbu, decimal FrauBu, decimal MannNbu, decimal MannBu);

    /// <summary>
    /// UVG-Totale je Betriebsteil (erste Stelle) und Geschlecht: Code x1/x2 = BU+NBU-versichert,
    /// x3 = nur BU, x0 zählt nicht. Ohne Zeilen bleiben die Betriebsteile <paramref name="alleTeile"/>
    /// mit 0.00 stehen.
    /// </summary>
    public static List<UvgBranche> UvgTotale(IEnumerable<Zeile> zeilen, IEnumerable<string>? alleTeile = null)
    {
        var map = new SortedDictionary<string, decimal[]>(StringComparer.Ordinal);
        foreach (var t in alleTeile ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(t)) map.TryAdd(t.Trim().ToUpperInvariant()[..1], new decimal[4]);
        foreach (var z in zeilen)
        {
            var c = z.Code.Trim().ToUpperInvariant();
            if (c.Length < 2 || c[1] == '0') continue;
            var teil = c[..1];
            if (!map.TryGetValue(teil, out var v)) map[teil] = v = new decimal[4];
            var frau = z.Geschlecht == "F";
            var nurBu = c[1] == '3';
            v[(frau ? 0 : 2) + (nurBu ? 1 : 0)] += z.Betrag;
        }
        return map.Select(kv => new UvgBranche(kv.Key, kv.Value[0], kv.Value[1], kv.Value[2], kv.Value[3])).ToList();
    }

    /// <summary>
    /// UVGZ-/KTG-Totale je Code und Geschlecht. Codes aus <paramref name="alleCodes"/> ohne Zeilen
    /// bleiben mit 0.00 stehen (wie die Referenz mit Kategorie 10).
    /// </summary>
    public static List<(string Code, decimal Frau, decimal Mann)> KategorieTotale(
        IEnumerable<Zeile> zeilen, IEnumerable<string>? alleCodes = null)
    {
        var reihenfolge = new List<string>();
        var map = new Dictionary<string, decimal[]>(StringComparer.OrdinalIgnoreCase);
        void Neu(string c) { if (!map.ContainsKey(c)) { map[c] = new decimal[2]; reihenfolge.Add(c); } }
        foreach (var z in zeilen)
        {
            var c = z.Code.Trim();
            Neu(c);
            map[c][z.Geschlecht == "F" ? 0 : 1] += z.Betrag;
        }
        foreach (var c in alleCodes ?? Array.Empty<string>())
            if (!string.IsNullOrWhiteSpace(c)) Neu(c.Trim());
        return reihenfolge.Select(c => (c, map[c][0], map[c][1])).ToList();
    }
}
