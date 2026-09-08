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

    private static IReadOnlyList<Lohnart> Laden()
    {
        var pfad = Path.Combine(AppContext.BaseDirectory, "Assets", "Swissdec", "SwissdecLohnarten.json");
        if (!File.Exists(pfad)) return Array.Empty<Lohnart>();
        var d = JsonSerializer.Deserialize<Datei>(File.ReadAllText(pfad));
        return d?.Lohnarten ?? new List<Lohnart>();
    }
}
