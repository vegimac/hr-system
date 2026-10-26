namespace HrSystem.Services;

/// <summary>
/// Offizielle ESTV-Liste der italienischen Gemeinden in der 20-km-Zone,
/// gültig ab 1.1.2024, Datei für Lohnprogramme.
/// Quelle: qst-20231222-comuni-italiani-nei-20-km-de.xlsx
/// </summary>
public static class GrenzregionKatalog
{
    public const string DateiName = "grenzregion_it_20km.csv";

    public readonly record struct Zeile(string Plz, string Ort, string Region, string Land);

    public static List<Zeile> Lesen(string pfad)
    {
        var list = new List<Zeile>();
        foreach (var roh in File.ReadLines(pfad))
        {
            var line = roh.Trim();
            if (line.Length == 0) continue;
            var t = line.Split(';');
            if (t.Length < 4) continue;
            if (t[0].Equals("plz", StringComparison.OrdinalIgnoreCase)) continue;
            var plz = t[0].Trim();
            var ort = t[1].Trim();
            var region = t[2].Trim();
            var land = t[3].Trim().ToUpperInvariant();
            if (plz.Length < 3 || ort.Length < 2 || land.Length != 2) continue;
            list.Add(new Zeile(plz, ort, region, land));
        }
        return list;
    }
}
