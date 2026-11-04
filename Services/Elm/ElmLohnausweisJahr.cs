using System.Text.Json;

namespace HrSystem.Services.Elm;

/// <summary>
/// Beträge des Lohnausweises (Swissdec TaxSalary, Richtlinien Kapitel 9) aus den Lohnzetteln einer Person.
/// Zuordnung nach Swissdec-Lohnart und Lohnausweis-Code der Lohnposition. Rein, ohne DB.
/// Die Zuordnung ist aus der Referenz Dezember 2025 abgeleitet und wird im Quality Tool geprüft.
/// </summary>
public static class ElmLohnausweisJahr
{
    public enum Feld
    {
        Lohn, Verpflegung, Geschaeftswagen, Nebenleistung, Unregelmaessig, Kapital, Beteiligung, Verwaltungsrat, Uebrige,
        SpesenEffektiv, SpesenPauschalWagen, SpesenPauschalUebrige, Weiterbildung, Keine,
    }

    public sealed class Summen
    {
        public Dictionary<Feld, decimal> Betrag { get; } = new();
        public Dictionary<Feld, List<string>> Texte { get; } = new();
        public decimal Beitraege, Bvg, Quellensteuer;
        public decimal BvgEinkauf;

        public decimal Von(Feld f) => Betrag.TryGetValue(f, out var v) ? v : 0m;
        public string TextVon(Feld f) => Texte.TryGetValue(f, out var t) ? string.Join(", ", t) : "";

        /// <summary>Ziffer 8: Lohn, Nebenleistungen, Unregelmässiges, Kapital, Beteiligungen, Honorare, Übrige.</summary>
        public decimal Brutto => Von(Feld.Lohn) + Von(Feld.Verpflegung) + Von(Feld.Geschaeftswagen) + Von(Feld.Nebenleistung)
            + Von(Feld.Unregelmaessig) + Von(Feld.Kapital) + Von(Feld.Beteiligung) + Von(Feld.Verwaltungsrat) + Von(Feld.Uebrige);

        public decimal Netto => Brutto - Beitraege - Bvg - BvgEinkauf;
    }

    /// <summary>Lohnausweis-Feld einer Lohnzeile.</summary>
    public static Feld FeldFuer(string? code, string? lohnausweisCode)
    {
        var c = (code ?? "").Trim();
        var la = (lohnausweisCode ?? "").Trim().ToUpperInvariant();
        switch (c)
        {
            case "1900": return Feld.Verpflegung;
            case "1910": return Feld.Geschaeftswagen;
            case "1500": return Feld.Verwaltungsrat;
            case "1980": return Feld.Weiterbildung;
            case "3034": case "3001": return Feld.Unregelmaessig;
            case "2065": return Feld.Keine; // Richtlinien 8.7.2.5: zählt für AHV/UVG, aber nicht zum Bruttolohn
            case "6000": return Feld.SpesenEffektiv;
            case "6050": return Feld.SpesenPauschalWagen;
            case "6070": return Feld.SpesenPauschalUebrige;
        }
        if (c.StartsWith("196")) return Feld.Beteiligung;
        if (c.StartsWith("5") || c.StartsWith("6") || c.StartsWith("9")) return Feld.Keine;
        return la switch
        {
            "R" => Feld.Nebenleistung,
            "Q" or "P" or "K" or "205" => Feld.Unregelmaessig,
            "S" => Feld.Kapital,
            "T" or "Z" => Feld.Uebrige,
            _ => Feld.Lohn,
        };
    }

    /// <summary>Summe der Lohnart 2065 (Lohnausfall KA/SW): gehört zur AHV-/UVG-Basis, nicht zum Bruttolohn.</summary>
    public static decimal LohnausfallKurzarbeit(IEnumerable<string?> lohnzettel)
    {
        decimal summe = 0m;
        foreach (var json in lohnzettel)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("lohnLines", out var zeilen) || zeilen.ValueKind != JsonValueKind.Array) continue;
            foreach (var z in zeilen.EnumerateArray())
                if (z.TryGetProperty("code", out var c) && c.ToString() == "2065"
                    && z.TryGetProperty("betrag", out var b) && b.ValueKind == JsonValueKind.Number)
                    summe += b.GetDecimal();
        }
        return summe;
    }

    /// <summary>Summiert die Lohnzettel. lohnposition liefert je Code den Lohnausweis-Code und die Bezeichnung.</summary>
    public static Summen Rechne(IEnumerable<string?> lohnzettel, Func<string, (string? La, string? Name)> lohnposition)
    {
        var s = new Summen();
        foreach (var json in lohnzettel)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            using var doc = JsonDocument.Parse(json);
            foreach (var liste in new[] { "lohnLines", "zulagenExtraLines" })
            {
                if (!doc.RootElement.TryGetProperty(liste, out var zeilen) || zeilen.ValueKind != JsonValueKind.Array) continue;
                foreach (var z in zeilen.EnumerateArray())
                {
                    var code = z.TryGetProperty("code", out var c) ? c.ToString() : "";
                    var betrag = z.TryGetProperty("betrag", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetDecimal() : 0m;
                    if (betrag == 0m || code.Length == 0) continue;
                    var (la, name) = lohnposition(code);
                    var feld = FeldFuer(code, la);
                    if (feld == Feld.Keine) continue;
                    s.Betrag[feld] = s.Von(feld) + betrag;
                    // Richtlinien Tabelle 9.4: in Ziffer 7 und bei Ziffer 10 wieder abgezogen (Referenz TF01 Herz).
                    if (code == "1972") s.Bvg += betrag;
                    if (code == "1973") s.BvgEinkauf += betrag;
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        if (!s.Texte.TryGetValue(feld, out var t)) s.Texte[feld] = t = new List<string>();
                        if (!t.Contains(name!.Trim())) t.Add(name.Trim());
                    }
                }
            }
            if (!doc.RootElement.TryGetProperty("abzugLines", out var abz) || abz.ValueKind != JsonValueKind.Array) continue;
            foreach (var z in abz.EnumerateArray())
            {
                var cat = z.TryGetProperty("categoryCode", out var c) ? (c.GetString() ?? "").Trim().ToUpperInvariant() : "";
                var betrag = z.TryGetProperty("betrag", out var b) && b.ValueKind == JsonValueKind.Number ? -b.GetDecimal() : 0m;
                switch (cat)
                {
                    case "AHV": case "ALV": case "ALVZ": case "NBUV": s.Beitraege += betrag; break;
                    case "BVG": s.Bvg += betrag; break;
                    case "QST": case "QST_KORR": s.Quellensteuer += betrag; break;
                }
            }
        }
        return s;
    }
}
