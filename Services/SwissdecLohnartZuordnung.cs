namespace HrSystem.Services;

/// <summary>
/// Feste Zuordnung OneCrew-Lohnpositions-Code → Swissdec-Musterlohnart
/// (Walter 17.10.2026). Quelle ursprünglich Testmandant-Schritt 4b —
/// gilt für Test und Prod gleich (Zertifizierung = Endstand).
/// Mehrere OneCrew-Positionen dürfen dieselbe Swissdec-Nummer tragen.
/// </summary>
public static class SwissdecLohnartZuordnung
{
    /// <summary>OneCrew-Code → Swissdec-Code (vierstellig).</summary>
    public static readonly IReadOnlyDictionary<string, string> NachOneCrewCode =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Festlohn
            ["10.1"] = "1000", ["10.2"] = "1000", ["10.3"] = "1000", ["10.4"] = "1000", ["10.5"] = "1000",
            // Stundenlohn
            ["20"] = "1005", ["20.1"] = "1005", ["20.2"] = "1005", ["20.3"] = "1005",
            ["22"] = "1005", ["55.3"] = "1005",
            // Überstunden / Nacht
            ["55.1"] = "1061", ["55.2"] = "1065",
            // 55.11/55.12: Mirus-Geld-Nachtzuschlag — Katalog seit Stand 81 inaktiv;
            // Mapping bleibt für Alt-Snapshots. Neu: nur 55.10 (Zeitgutschrift).
            ["55.10"] = "1076", ["55.11"] = "1076", ["55.12"] = "1076",
            // ausbezahlte Ferien-/Feiertage
            ["40.1"] = "1160", ["50.1"] = "1161",
            // UVG / KTG — Karenz = Lohnfortzahlung; Versicherung = Taggeld
            ["60.1"] = "1300", ["60.2"] = "1300", ["60.3"] = "2030",
            ["70.1"] = "1301", ["70.2"] = "2035",
            // Korrektur Taggelder UVG/KTG
            ["65.1"] = "2050", ["65.2"] = "2050", ["75.1"] = "2050", ["75.2"] = "2050",
            // Militär / Zivilschutz (EO)
            ["80.1"] = "2000", ["80.2"] = "2000", ["80.3"] = "2000",
            ["85.1"] = "2050",
            ["90.1"] = "2000", ["90.2"] = "2000", ["90.3"] = "2000",
            ["95.1"] = "2050",
            // Mutterschaft / Vaterschaft (Treuhand: 2001 / 2002; Korrektur → 2050)
            ["120.1"] = "2001", ["120.2"] = "2002",
            ["125.1"] = "2050", ["125.2"] = "2050",
            ["VATERSCHAFT"] = "2002",
            ["2001"] = "2001", ["2002"] = "2002", ["2000"] = "2000", ["2050"] = "2050",
            // SV-Abzüge
            ["103"] = "5010", ["104"] = "5012", ["107"] = "5042",
            ["110"] = "5020", ["111"] = "5050",
            ["5010"] = "5010", ["5012"] = "5012", ["5014"] = "5014",
            ["5020"] = "5020", ["5030"] = "5030", ["5042"] = "5042", ["5050"] = "5050",
            // 13. Monatslohn
            ["180.1"] = "1200", ["180.2"] = "1200", ["180.3"] = "1200",
            // Familienzulagen
            ["190.1"] = "3000", ["190.2"] = "3000", ["190.3"] = "3034",
            ["200.190"] = "3000", ["200.10"] = "1200",
            ["565"] = "5102", ["565.1"] = "5102", ["565.2"] = "5102",
            // Ferien-/Feiertagentschädigung FLEX/MTP
            ["195.1"] = "1160", ["195.3"] = "1160", ["195.5"] = "1160", ["195.6"] = "1160",
            ["195.2"] = "1161", ["195.4"] = "1161",
            // Diverse (Muster AG / Sonderfälle)
            ["200.1"] = "6070", ["200.5"] = "1210", ["200.11"] = "1230",
            ["200.20"] = "3034", ["200.50"] = "1910", ["200.60"] = "1900",
            // SV-Abzüge (falls als eigene Lohnposition geführt)
            ["5010"] = "5010", ["5012"] = "5012", ["5014"] = "5014",
            ["5020"] = "5020", ["5050"] = "5050",
        };

    public static string? FuerOneCrewCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim();
        // Neu: code ist bereits Swissdec (ggf. mit Suffix) — vierstelligen Kopf liefern
        if (c.Length >= 4 && char.IsDigit(c[0]) && !c.Contains('.') && c.Length == 4)
            return c;
        if (NachOneCrewCode.TryGetValue(c, out var s)) return s;
        // Swissdec mit Suffix (1000.2 → 1000) oder bereits gemappt
        var swiss = LohnFormelwerk.LohnCodes.SwissdecVon(c);
        if (!string.IsNullOrEmpty(swiss) && swiss.Length == 4) return swiss;
        // Mirus-Alias → Swissdec
        var norm = LohnFormelwerk.LohnCodeAlias.Normiere(c);
        if (!string.Equals(norm, c, StringComparison.OrdinalIgnoreCase))
            return LohnFormelwerk.LohnCodes.SwissdecVon(norm) is { Length: >= 4 } sw ? sw : norm;
        return null;
    }

    public static IReadOnlyList<(string OneCrew, string Swissdec)> AlsListe()
        => NachOneCrewCode.Select(kv => (kv.Key, kv.Value))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
