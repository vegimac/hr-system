using System.Text.Json;
using System.Text.Json.Serialization;

namespace HrSystem.Services;

/// <summary>
/// Swissdec-Musterlohnartenstamm (Assets/Swissdec/SwissdecLohnarten.json, aus der
/// offiziellen Wage_Types.xlsx von Swissdec, 172 Lohnarten mit Steuerung +/−). Pflichten und
/// Lohnausweis-Ziffer sind Startwerte für neu angelegte Lohnpositionen — danach
/// zählt, was an der OneCrew-Lohnposition steht (Walter 08.09.2026).
/// </summary>
public static class SwissdecLohnartenKatalog
{
    public sealed class Lohnart
    {
        [JsonPropertyName("code")]        public string Code { get; set; } = "";
        [JsonPropertyName("bezeichnung")] public string Bezeichnung { get; set; } = "";
        [JsonPropertyName("kategorie")]   public string Kategorie { get; set; } = "";
        [JsonPropertyName("typ")]         public string Typ { get; set; } = "ZULAGE";
        [JsonPropertyName("ahv")]         public bool Ahv { get; set; }
        [JsonPropertyName("brutto")]      public bool Brutto { get; set; }
        [JsonPropertyName("uvg")]         public bool Uvg { get; set; }
        [JsonPropertyName("uvgz")]        public bool Uvgz { get; set; }
        [JsonPropertyName("ktg")]         public bool Ktg { get; set; }
        [JsonPropertyName("bvg")]         public bool Bvg { get; set; }
        /// <summary>BVG_LPP_Expected. Leer im Katalog = Abzugszeile ohne BVG-Zelle, nicht überschreiben.</summary>
        [JsonPropertyName("bvgErwartet")] public int? BvgErwartet { get; set; }
        /// <summary>BVG_LPP_Retrospective: 0 keine, 1 Stundenlohn, 2 Lektionenlohn, 3 übrige, 4 Taggeld.</summary>
        [JsonPropertyName("bvgRueck")]    public int? BvgRueck { get; set; }
        /// <summary>Factor, nur wenn voraussichtlich. «x» oder eine Zahl wie 12.</summary>
        [JsonPropertyName("bvgFaktorRoh")] public string? BvgFaktorRoh { get; set; }
        [JsonPropertyName("qst")]         public bool Qst { get; set; }
        [JsonPropertyName("ml13")]        public bool Ml13 { get; set; }
        [JsonPropertyName("lohnausweis")] public string Lohnausweis { get; set; } = "";
        [JsonPropertyName("fibuKonto")]   public string? FibuKonto { get; set; }
        [JsonPropertyName("statistikJahr")] public int? StatistikJahr { get; set; }
    }
    private sealed class Datei
    {
        [JsonPropertyName("lohnarten")] public List<Lohnart> Lohnarten { get; set; } = new();
    }

    private static readonly Lazy<IReadOnlyList<Lohnart>> _alle = new(Laden);

    public static IReadOnlyList<Lohnart> Alle => _alle.Value;

    public static Lohnart? Finde(string code)
        => Alle.FirstOrDefault(l => l.Code == code);

    /// <summary>
    /// BVG-Zelle der Nummer. «1006.2» nimmt die Zelle von 1006, wenn die Variante selbst keine hat.
    /// Abzugszeilen ohne Zelle bleiben null.
    /// </summary>
    public static Lohnart? FindeBvg(string? code)
    {
        var c = (code ?? "").Trim();
        if (c.Length == 0) return null;
        var direkt = Finde(c);
        if (direkt?.BvgErwartet != null) return direkt;
        var punkt = c.IndexOf('.');
        if (punkt <= 0) return null;
        var basis = Finde(c[..punkt]);
        return basis?.BvgErwartet != null ? basis : null;
    }

    private static IReadOnlyList<Lohnart> Laden()
    {
        var pfad = Path.Combine(AppContext.BaseDirectory, "Assets", "Swissdec", "SwissdecLohnarten.json");
        if (!File.Exists(pfad)) return Array.Empty<Lohnart>();
        var d = JsonSerializer.Deserialize<Datei>(File.ReadAllText(pfad));
        return d?.Lohnarten ?? new List<Lohnart>();
    }
}
