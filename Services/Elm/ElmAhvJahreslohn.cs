using System.Text.Json;

namespace HrSystem.Services.Elm;

/// <summary>
/// AHV-Jahresmeldung: ein Abschnitt pro zusammenhängende Monate derselben Art und Anstellung.
/// Normal = AHV-, ALV- und ALVZ-Lohn aus den Abzugszeilen des Lohnzettels (die Engine hat dort den
/// kumulierten Höchstlohn schon gerechnet), der Rest über dem ALVZ-Höchstlohn in ALV-AC-Open.
/// Rentner (Lohnzettel ohne ALV-Zeilen) = Freibetrag in AHV-AVS-Open, der ganze Lohn in ALV-AC-Open.
/// Frei (Sonderfall, unter 18) = beide Löhne 0, der Lohn steht in AHV-AVS-Open und ALV-AC-Open.
/// Verzicht = AHV auf dem vollen Lohn, ALV-Lohn 0, der Lohn in ALV-AC-Open, plus das leere
/// Kennzeichen WaiveOfPensionDeduct.
/// Monate nach dem Austritt zählen zur Anstellung, zu der sie gehören (Referenz TF41 Meier Max:
/// Mai-Bonus im Abschnitt 01.01.–31.03.). Ohne Lohnzettel-Zeilen: gedeckelter Monatslohn wie bisher.
/// </summary>
public static class ElmAhvJahreslohn
{
    public enum Art { Normal, Frei, Verzicht, Rentner }

    /// <summary>Beitragspflichtige Basen aus den Abzugszeilen eines Lohnzettels; null = keine Zeile.</summary>
    public readonly record struct Zeilen(decimal? Ahv, decimal? Alv, decimal? Alvz)
    {
        public bool OhneAlv => Alv == null && Alvz == null;
    }

    public readonly record struct Monat(int Nr, decimal Basis, decimal AlvGedeckelt, Art Art)
    {
        /// <summary>Beginn der Anstellung, zu welcher der Monat gehört. Null = Monate nur zusammenhängend.</summary>
        public DateTime? Kette { get; init; }
        public Zeilen? Lohnzettel { get; init; }
    }

    public readonly record struct Abschnitt(
        int Von, int Bis, Art Art,
        decimal Basis, decimal AhvLohn, decimal AhvFrei, decimal AlvLohn, decimal AlvFrei,
        decimal AlvzLohn = 0m, bool MitAlvz = false);

    /// <summary>AHV-, ALV- und ALVZ-Basen der Abzugszeilen (categoryCode), über mehrere Lohnzettel des Monats summiert.</summary>
    public static Zeilen? AusLohnzetteln(IEnumerable<string?> lohnzettel)
    {
        decimal? ahv = null, alv = null, alvz = null;
        foreach (var json in lohnzettel)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("abzugLines", out var zeilen)
                || zeilen.ValueKind != JsonValueKind.Array) continue;
            foreach (var z in zeilen.EnumerateArray())
            {
                if (!z.TryGetProperty("categoryCode", out var k) || k.ValueKind != JsonValueKind.String) continue;
                var basis = z.TryGetProperty("basis", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetDecimal() : 0m;
                switch (k.GetString())
                {
                    case "AHV": ahv = (ahv ?? 0m) + basis; break;
                    case "ALV": alv = (alv ?? 0m) + basis; break;
                    case "ALVZ": alvz = (alvz ?? 0m) + basis; break;
                }
            }
        }
        return ahv == null && alv == null && alvz == null ? null : new Zeilen(ahv, alv, alvz);
    }

    public static List<Abschnitt> Abschnitte(IReadOnlyList<Monat> monate)
    {
        var list = monate.OrderBy(m => m.Nr).Select(m => m with { Art = ArtMitZeilen(m) }).ToList();
        var result = new List<Abschnitt>();
        var i = 0;
        while (i < list.Count)
        {
            var art = list[i].Art;
            var j = i;
            while (j + 1 < list.Count
                   && list[j + 1].Art == art
                   && (list[i].Kette != null
                       ? list[j + 1].Kette == list[i].Kette
                       : list[j + 1].Nr == list[j].Nr + 1))
                j++;

            decimal sv = 0, ahv = 0, alv = 0, alvz = 0;
            var mitAlvz = false;
            var mitZeilen = false;
            for (var k = i; k <= j; k++)
            {
                var z = list[k].Lohnzettel;
                sv += list[k].Basis;
                ahv += z?.Ahv ?? list[k].Basis;
                alv += z?.Alv ?? (z == null ? list[k].AlvGedeckelt : 0m);
                alvz += z?.Alvz ?? 0m;
                mitAlvz |= (z?.Alvz ?? 0m) != 0m;
                mitZeilen |= z != null;
            }

            decimal basis, ahvLohn, ahvFrei, alvLohn, alvFrei;
            switch (art)
            {
                case Art.Frei:
                    basis = sv; ahvLohn = 0; ahvFrei = sv; alvLohn = 0; alvFrei = sv; alvz = 0; mitAlvz = false;
                    break;
                case Art.Rentner:
                    basis = sv; ahvLohn = ahv; ahvFrei = sv - ahv; alvLohn = 0; alvFrei = sv; alvz = 0; mitAlvz = false;
                    break;
                case Art.Verzicht:
                    basis = ahv; ahvLohn = ahv; ahvFrei = 0; alvLohn = 0; alvFrei = ahv; alvz = 0; mitAlvz = false;
                    break;
                default:
                    basis = ahv; ahvLohn = ahv; ahvFrei = 0; alvLohn = alv;
                    alvFrei = mitZeilen ? ahv - alv - alvz : 0m;
                    break;
            }

            if (basis != 0 || ahvLohn != 0 || ahvFrei != 0 || alvLohn != 0 || alvFrei != 0)
                result.Add(new Abschnitt(list[i].Nr, list[j].Nr, art,
                    basis, ahvLohn, ahvFrei, alvLohn, alvFrei, alvz, mitAlvz));
            i = j + 1;
        }
        return result;
    }

    /// <summary>Normaler Monat, dessen Lohnzettel AHV, aber keine ALV-Zeile hat: Referenzalter erreicht.</summary>
    private static Art ArtMitZeilen(Monat m) =>
        m.Art == Art.Normal && m.Lohnzettel is { OhneAlv: true, Ahv: not null } ? Art.Rentner : m.Art;
}
