namespace HrSystem.Services.LohnFormelwerk;

/// <summary>
/// Brücke Mirus-Altcode ↔ Swissdec-Treuhand-Nr. (Walter 19.10.2026).
/// Streng nur Swissdec-Nummern aus der Treuhandliste — keine erfundenen Suffixe.
/// </summary>
public static class LohnCodeAlias
{
    public static readonly IReadOnlyDictionary<string, string> MirusNachSwissdec =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["10.1"] = LohnCodes.Festlohn,
            ["10.2"] = LohnCodes.Festlohn,
            ["10.3"] = LohnCodes.Festlohn,
            ["10.4"] = LohnCodes.Festlohn,
            ["10.5"] = LohnCodes.Festlohn,
            ["20"] = LohnCodes.Stundenlohn,
            ["20.1"] = LohnCodes.Stundenlohn,
            ["20.2"] = LohnCodes.Stundenlohn,
            ["20.3"] = LohnCodes.Stundenlohn,
            ["22"] = LohnCodes.Stundenlohn,
            ["55.3"] = LohnCodes.Stundenlohn,
            ["55.1"] = LohnCodes.Ueberstunden25,
            ["55.2"] = LohnCodes.UeberstundenOhneZuschlag,
            ["55.10"] = LohnCodes.NachtKompensation,
            ["55.11"] = LohnCodes.NachtKompensation,
            ["55.12"] = LohnCodes.NachtKompensation,
            ["40.1"] = LohnCodes.FerienFlexTopf,
            ["195.1"] = LohnCodes.FerienFlexTopf,
            ["195.3"] = LohnCodes.FerienFlexTopf,
            ["195.5"] = LohnCodes.FerienFlexTopf,
            ["195.6"] = LohnCodes.FerienFlexTopf,
            ["195.2"] = LohnCodes.FeiertagEntschaedigung,
            ["195.4"] = LohnCodes.FeiertagEntschaedigung,
            ["50.1"] = LohnCodes.FeiertagEntschaedigung,
            ["180.1"] = LohnCodes.Ml13,
            ["180.2"] = LohnCodes.Ml13,
            ["180.3"] = LohnCodes.Ml13, // Auslöser: Erkennung über mirus_code, nicht Code
            ["200.5"] = LohnCodes.McBonus,
            ["200.1"] = LohnCodes.DiverseSpesen,
            ["190.1"] = LohnCodes.Kinderzulage,
            ["190.2"] = LohnCodes.Kinderzulage, // Mirus «Familienzulagen Teilzeit» → Kinderzulage
            ["190.3"] = LohnCodes.Geburtszulage,
            ["200.190"] = LohnCodes.Kinderzulage,
            ["200.10"] = LohnCodes.Ml13,
            ["565"] = LohnCodes.QstKorr,
            ["565.1"] = LohnCodes.QstKorr,
            ["565.2"] = LohnCodes.QstKorr,
            ["70.1"] = LohnCodes.KrankKarenz,
            ["70.2"] = LohnCodes.KrankTaggeld,
            ["60.2"] = LohnCodes.UnfallKarenz,
            ["60.1"] = LohnCodes.UnfallKarenz,
            ["60.3"] = LohnCodes.UnfallTaggeld,
            ["75"] = LohnCodes.KorrTaggeld,
            ["75.1"] = LohnCodes.KorrTaggeld,
            ["75.2"] = LohnCodes.KorrTaggeld,
            ["180"] = LohnCodes.Ml13,
            ["200"] = LohnCodes.DiverseSpesen,
            // SV-Abzüge (Stamm früher Kurz-Nr.)
            ["103"] = "5010",
            ["104"] = "5012",
            ["107"] = "5042",
            ["110"] = "5020",
            ["111"] = "5050",
            ["65.1"] = LohnCodes.KorrTaggeld,
            ["65.2"] = LohnCodes.KorrTaggeld,
            ["85.1"] = LohnCodes.KorrTaggeld,
            ["95.1"] = LohnCodes.KorrTaggeld,
            ["125.1"] = LohnCodes.KorrTaggeld,
            ["125.2"] = LohnCodes.KorrTaggeld,
            ["80.1"] = LohnCodes.EoTaggeld,
            ["80.2"] = LohnCodes.EoTaggeld,
            ["80.3"] = LohnCodes.EoTaggeld,
            ["90.1"] = LohnCodes.EoTaggeld,
            ["90.2"] = LohnCodes.EoTaggeld,
            ["90.3"] = LohnCodes.EoTaggeld,
            ["120.1"] = LohnCodes.MutterschaftEo,
            // OneCrew: 120.2 = Vaterschaft (ELM 2002). Treuhand listet 120.2 auch unter 2001 —
            // Betrieb und Engine bleiben bei 2002; VATERSCHAFT ebenfalls.
            ["120.2"] = LohnCodes.VaterschaftEo,
            ["VATERSCHAFT"] = LohnCodes.VaterschaftEo,
            ["600.24"] = LohnCodes.Lgav,
            ["600.32"] = LohnCodes.UniformDepot,
            ["950.1"] = LohnCodes.Vorschuss,
            ["600.5"] = LohnCodes.BvgAusgleichAg,
            // Erfundene Alt-Suffixe → Treuhand
            ["2001.2"] = LohnCodes.VaterschaftEo,
            ["1000.2"] = LohnCodes.Festlohn,
            ["1000.3"] = LohnCodes.Festlohn,
            ["1000.4"] = LohnCodes.Festlohn,
            ["1005.1"] = LohnCodes.Stundenlohn,
            ["1005.2"] = LohnCodes.Stundenlohn,
            ["1005.3"] = LohnCodes.Stundenlohn,
            ["1005.22"] = LohnCodes.Stundenlohn,
            ["1005.3m"] = LohnCodes.Stundenlohn,
            ["1160.195"] = LohnCodes.FerienFlexTopf,
            ["1161.50"] = LohnCodes.FeiertagEntschaedigung,
            ["1200.2"] = LohnCodes.Ml13,
            ["1200.3"] = LohnCodes.Ml13,
            ["2050.75"] = LohnCodes.KorrTaggeld,
            ["2050.75b"] = LohnCodes.KorrTaggeld,
            ["2050.65"] = LohnCodes.KorrTaggeld,
            ["2050.65b"] = LohnCodes.KorrTaggeld,
            ["2000.80"] = LohnCodes.EoTaggeld,
            ["2000.81"] = LohnCodes.EoTaggeld,
            ["2000.82"] = LohnCodes.EoTaggeld,
            ["2000.90"] = LohnCodes.EoTaggeld,
            ["2000.91"] = LohnCodes.EoTaggeld,
            ["2000.92"] = LohnCodes.EoTaggeld,
            ["1300.1"] = LohnCodes.UnfallKarenz,
        };

    public static string Normiere(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var c = code.Trim();
        if (MirusNachSwissdec.TryGetValue(c, out var s)) return s;
        return c;
    }

    public static bool Gleich(string? a, string? b) =>
        !string.IsNullOrEmpty(Normiere(a))
        && string.Equals(Normiere(a), Normiere(b), StringComparison.OrdinalIgnoreCase);

    public static bool Ist(string? code, params string[] erlaubte)
    {
        var n = Normiere(code);
        foreach (var e in erlaubte)
        {
            if (string.Equals(n, Normiere(e), StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(code?.Trim(), e, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>13.-ML-Auslöser (früher 180.3) — Erkennung über mirus_code, Code ist 1200.</summary>
    public static bool Ist13mlAuszahlen(string? code, string? mirusCode, string? bezeichnung = null)
    {
        if (string.Equals(mirusCode?.Trim(), "180.3", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(code?.Trim(), "180.3", StringComparison.OrdinalIgnoreCase)) return true;
        if (bezeichnung != null
            && bezeichnung.Contains("auszahlen", StringComparison.OrdinalIgnoreCase)
            && Ist(code, LohnCodes.Ml13))
            return true;
        return false;
    }
}
