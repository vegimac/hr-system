using System.Text.RegularExpressions;
using HrSystem.Models;

namespace HrSystem.Services.WebStamp;

/// <summary>
/// Anschrift für den WebStamp-Selbstdruck. Strasse, PLZ und Ort setzt die Post
/// ins Fenstercouvert.
/// </summary>
public record WebStampAnschrift(
    string? Organisation,
    string? Zusatz,
    string? Titel,
    string? Vorname,
    string? Nachname,
    string Strasse,
    string Plz,
    string Ort,
    string? Land)
{
    private static readonly Regex PlzOrt = new(@"^(\d{4})\s+(\S.*)$", RegexOptions.Compiled);

    private static readonly HashSet<string> Anreden = new(StringComparer.OrdinalIgnoreCase)
    {
        "Herr", "Herrn", "Frau", "Familie", "Fam.",
    };

    public static WebStampAnschrift AusMitarbeiter(Employee e) => new(
        null, null, Leer(e.Salutation), Leer(e.FirstName), Leer(e.LastName),
        (e.Street ?? "").Trim(), (e.ZipCode ?? "").Trim(), (e.City ?? "").Trim(), LandCode(e.Country));

    /// <summary>
    /// Freie Adresse, eine Zeile pro Feld. Unten «PLZ Ort» (z.B. 6000 Luzern),
    /// darüber die Strasse, darüber der Name. Optional eine Anrede zuoberst
    /// und ein Land als letzte Zeile.
    /// </summary>
    public static WebStampAnschrift? AusZeilen(IReadOnlyList<string> zeilen)
    {
        var z = zeilen.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        if (z.Count < 3) return null;
        string? land = null;
        if (!IstPlzOrt(z[^1]) && IstPlzOrt(z[^2]))
        {
            land = z[^1];
            z.RemoveAt(z.Count - 1);
        }
        if (!IstPlzOrt(z[^1])) return null;
        var m = PlzOrt.Match(z[^1]);
        var strasse = z[^2];
        var kopf = z.Take(z.Count - 2).ToList();
        if (kopf.Count == 0 || strasse.Length == 0) return null;

        string? titel = null;
        if (Anreden.Contains(kopf[0]))
        {
            titel = kopf[0];
            kopf.RemoveAt(0);
        }
        if (kopf.Count == 0) return null;

        string? organisation = null;
        if (kopf.Count >= 2)
            organisation = string.Join(", ", kopf.Take(kopf.Count - 1));
        var name = kopf[^1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var nachname = name[^1];
        var vorname = name.Length > 1 ? string.Join(' ', name.Take(name.Length - 1)) : null;
        return new WebStampAnschrift(organisation, null, titel, vorname, nachname,
            strasse, m.Groups[1].Value, m.Groups[2].Value.Trim(), LandCode(land));
    }

    public static WebStampAnschrift? AusFiliale(CompanyProfile c)
    {
        var strasse = $"{c.Street} {c.HouseNumber}".Trim();
        if (strasse.Length == 0 || string.IsNullOrWhiteSpace(c.ZipCode) || string.IsNullOrWhiteSpace(c.City))
            return null;
        var org = (c.CompanyName ?? "").Trim();
        var zusatz = string.IsNullOrWhiteSpace(c.BranchName)
                     || string.Equals(c.BranchName.Trim(), org, StringComparison.OrdinalIgnoreCase)
            ? null : c.BranchName.Trim();
        return new WebStampAnschrift(org.Length == 0 ? null : org, zusatz, null, null, null,
            strasse, c.ZipCode.Trim(), c.City.Trim(), LandCode(c.Country));
    }

    private static bool IstPlzOrt(string zeile) => PlzOrt.IsMatch(zeile);

    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public static string LandCode(string? land)
    {
        land = land?.Trim() ?? "";
        if (land.Length == 0
            || land.Equals("CH", StringComparison.OrdinalIgnoreCase)
            || land.Equals("CHE", StringComparison.OrdinalIgnoreCase)
            || land.StartsWith("Schweiz", StringComparison.OrdinalIgnoreCase)
            || land.StartsWith("Switzerland", StringComparison.OrdinalIgnoreCase))
            return "CH";
        return land;
    }
}
