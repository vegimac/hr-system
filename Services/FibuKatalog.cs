namespace HrSystem.Services;

/// <summary>
/// Kontoplan = dieselben Nummern wie die Lohnpositionen (Walter 21.10.2026).
/// Soll/Haben sind die Abacus-Konten, die das Journal für diese Lohnart bucht.
/// «VERTRAG» = Aufwand 4000 Crew / 4001 Flex / 4055 Management.
/// Reine Buchungen ohne Lohnart (Nettolohn, Rückstellungen) stehen nicht hier.
/// </summary>
public static class FibuKatalog
{
    public const string VomVertrag = "VERTRAG";

    /// <param name="MapPosition">Mirus-Position der Kontoplan-Zeile, falls diese Lohnart eine eigene Zeile hat.</param>
    /// <param name="MapFibukonto">Nur die Zeile mit diesem Soll (trennt AN 1920 von AG 4062).</param>
    public readonly record struct Konten(
        string Soll,
        string Haben,
        int? MapPosition = null,
        int? MapSub = null,
        string? MapFibukonto = null,
        string? AgSoll = null,
        string? AgHaben = null,
        string? Hinweis = null);

    private static readonly Dictionary<string, Konten> Tabelle =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Lohn — im Journal als Bruttolohn gesammelt (Position 10)
            ["1000"] = new(VomVertrag, "1920"),
            ["1005"] = new(VomVertrag, "1920"),
            ["1061"] = new(VomVertrag, "1920"),
            ["1065"] = new(VomVertrag, "1920"),
            ["1076"] = new(VomVertrag, "1920", Hinweis: "Wie der übrige Lohn, nicht ein eigenes Konto."),
            ["1160"] = new(VomVertrag, "1920"),
            ["1161"] = new(VomVertrag, "1920"),
            ["1166"] = new(VomVertrag, "1920"),
            ["1168"] = new(VomVertrag, "1920"),
            ["1200"] = new(VomVertrag, "1920", Hinweis: "Laufender Monat ist Aufwand. Die Auszahlung aus dem Saldo bucht 180.3."),
            ["1210"] = new(VomVertrag, "1920", MapPosition: 200, MapSub: 5),
            ["1300"] = new(VomVertrag, "1920", Hinweis: "Karenz zahlt der Arbeitgeber, bleibt Personalaufwand."),
            ["1301"] = new(VomVertrag, "1920", Hinweis: "Karenz zahlt der Arbeitgeber, bleibt Personalaufwand."),
            ["2050"] = new(VomVertrag, "1920", Hinweis: "Korrektur der Karenz wie Lohn. Eine Taggeld-Korrektur der Versicherung bucht wie 2030/2035 über 2014."),

            // Versicherung erstattet — Forderung 2014, nicht Aufwand
            ["2030"] = new("2014", "1920", MapPosition: 60, MapSub: 3, MapFibukonto: "2014"),
            ["2035"] = new("2014", "1920", MapPosition: 70, MapSub: 2, MapFibukonto: "2014"),
            ["2000"] = new("2014", "1920", MapPosition: 80, MapSub: 3, MapFibukonto: "2014",
                Hinweis: "EO-Taggeld ist Forderung an die Versicherung. Der Arbeitgeber-Zuschlag 8/20 % bleibt Aufwand."),
            ["2001"] = new("2014", "1920", MapPosition: 120, MapFibukonto: "2014"),
            ["2002"] = new("2014", "1920", MapPosition: 120, MapFibukonto: "2014"),

            // Familienzulagen — FAK erstattet, Konto 2071
            ["3000"] = new("2071", "1920", MapPosition: 190, MapFibukonto: "2071"),
            ["3010"] = new("2071", "1920", MapPosition: 190, MapFibukonto: "2071"),
            ["3034"] = new("2071", "1920", MapPosition: 200, MapSub: 20, MapFibukonto: "2071"),

            // Spesen ausserhalb des Personalaufwands
            ["6070"] = new("4095", "1920", MapPosition: 200, MapSub: 1, MapFibukonto: "4095"),

            // 13. ML aus dem Saldo — die Lohnart 180.3 ist nur der Auslöser
            ["180.3"] = new("2017", "1920", MapPosition: 180,
                Hinweis: "Crew und Flex 2017, Management 2016, an 1920. Die Lohnart selbst hat keinen Betrag."),

            // Abzüge Arbeitnehmer. Arbeitgeber daneben, berührt 1920 nicht.
            ["5010"] = new("1920", "2070", MapPosition: 500, MapFibukonto: "1920", AgSoll: "4062", AgHaben: "2070"),
            ["5012"] = new("1920", "2070", MapPosition: 510, MapFibukonto: "1920", AgSoll: "4062", AgHaben: "2070"),
            ["5014"] = new("1920", "2070", MapPosition: 511, MapFibukonto: "1920", AgSoll: "4062", AgHaben: "2070"),
            ["5020"] = new("1920", "2090", MapPosition: 540, MapFibukonto: "1920", AgSoll: "4061", AgHaben: "2090"),
            ["5030"] = new("1920", "2090", MapPosition: 541, MapFibukonto: "1920", AgSoll: "4061", AgHaben: "2090"),
            ["5042"] = new("1920", "2090", MapPosition: 530, MapFibukonto: "1920", AgSoll: "4061", AgHaben: "2090"),
            ["5050"] = new("1920", "2080", MapPosition: 550, MapFibukonto: "1920", AgSoll: "4060", AgHaben: "2080"),
            ["5100"] = new("1920", "2010", MapPosition: 560, MapFibukonto: "1920"),
            ["5102"] = new("1920", "2010", MapPosition: 565, MapFibukonto: "1920"),
            ["5212"] = new("1920", "2080", MapPosition: 600, MapSub: 5, MapFibukonto: "1920", AgSoll: "4060", AgHaben: "2080"),

            // Betrieb, keine Swissdec-Lohnart, aber eine Buchung
            ["600.24"] = new("1920", "2023", MapPosition: 600, MapSub: 24, MapFibukonto: "1920"),
            ["600.32"] = new("1920", "2021", MapPosition: 600, MapSub: 32, MapFibukonto: "1920"),
            ["950.1"] = new("1920", "2050", MapPosition: 950, MapFibukonto: "1920"),
        };

    public static bool Kennt(string? code)
        => !string.IsNullOrWhiteSpace(code) && Tabelle.ContainsKey(code.Trim());

    public static Konten? Fuer(string? code)
        => Kennt(code) ? Tabelle[code!.Trim()] : null;

    /// <summary>Saldo-Vortrag. Steht im Lohnartenstamm, erzeugt keine Journalzeile.</summary>
    public static bool OhneBuchung(string? code)
    {
        var c = (code ?? "").Trim();
        return c is "901" or "902" or "903" or "904" or "905" or "906";
    }

    /// <summary>Jede Katalog-Zeile mit eigener Kontoplan-Position, für den Nummern-Abgleich.</summary>
    public static IReadOnlyList<(string Code, Konten Konten)> MitEigenerZeile()
        => Tabelle
            .Where(p => p.Value.MapPosition != null)
            .Select(p => (p.Key, p.Value))
            .ToList();
}
