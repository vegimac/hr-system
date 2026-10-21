using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace HrSystem.Services;

/// <summary>
/// Eine einzelne GastroSocial-Mitteilung (Entscheid, Verfallanzeige oder Wegfallanzeige).
/// Kein Firmen-Import: ein PDF, die Kinder darauf, sonst nichts.
/// </summary>
public enum FamzMitteilungArt { Unbekannt, Entscheid, Verfall, Wegfall }

public sealed record FamzMitteilung(
    FamzMitteilungArt Art,
    DateOnly? Briefdatum,
    string? Ahv,
    string? Nachname,
    string? Vorname,
    string? FakKanton,
    IReadOnlyList<FamzMitteilungKind> Kinder,
    string? Fehler,
    DateOnly? Anspruchsende = null,
    string? Beendigungsgrund = null);

public sealed record FamzMitteilungKind(
    string Vorname,
    DateOnly Geburt,
    DateOnly? Von,
    DateOnly? Bis,
    string? Zulagenart,
    int? Beendigungsgrund,
    string? GrundText,
    decimal? Betrag);

/// <summary>
/// Liest den Text eines Familienzulagen-Entscheids, einer Verfallanzeige oder Wegfallanzeige.
/// Die Ziffer neben der Zulage ist der Beendigungsgrund, nicht der Tarif-Satz.
/// Wegfallanzeige («Anderer Bezüger») nennt keine Kinder — Anspruchsende gilt für alle Zulagen des MA.
/// </summary>
public static partial class FamzMitteilungLeser
{
    public static string TextAusPdf(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        using var reader = new PdfReader(ms);
        using var doc = new PdfDocument(reader);
        var sb = new StringBuilder();
        for (var i = 1; i <= doc.GetNumberOfPages(); i++)
            sb.AppendLine(PdfTextExtractor.GetTextFromPage(doc.GetPage(i)));
        return sb.ToString();
    }

    public static FamzMitteilung Lies(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Leer(FamzMitteilungArt.Unbekannt, "Das PDF enthält keinen Text.");

        // Wegfall vor Verfall: Titel «Wegfallanzeige» enthält kein «Verfall».
        var art = text.Contains("Wegfallanzeige", StringComparison.OrdinalIgnoreCase)
            ? FamzMitteilungArt.Wegfall
            : text.Contains("Verfallanzeige", StringComparison.OrdinalIgnoreCase)
                ? FamzMitteilungArt.Verfall
                : text.Contains("Familienzulagen", StringComparison.OrdinalIgnoreCase)
                  && text.Contains("Entscheid", StringComparison.OrdinalIgnoreCase)
                    ? FamzMitteilungArt.Entscheid
                    : FamzMitteilungArt.Unbekannt;

        var brief = Datum(DatumRx().Match(text).Groups[1].Value);
        var ahv = AhvDerBerechtigten(text);
        var fak = FakRx().Match(text);
        var fakKanton = fak.Success ? fak.Groups[1].Value.ToUpperInvariant() : null;
        var (nachname, vorname) = Berechtigte(text);

        if (art == FamzMitteilungArt.Unbekannt)
            return new FamzMitteilung(art, brief, ahv, nachname, vorname, fakKanton, [],
                "Das ist kein Familienzulagen-Entscheid, keine Verfallanzeige und keine Wegfallanzeige.");

        var kern = Kern(text);
        if (art == FamzMitteilungArt.Wegfall)
        {
            var (ende, grund) = WegfallEnde(kern);
            string? fehler = null;
            if (ahv == null) fehler = "Keine AHV-Nummer auf der Mitteilung.";
            else if (ende == null) fehler = "Kein Anspruchsende auf der Wegfallanzeige.";
            return new FamzMitteilung(art, brief, ahv, nachname, vorname, fakKanton, [], fehler, ende, grund);
        }

        var kinder = art == FamzMitteilungArt.Verfall ? Verfall(kern) : Entscheid(kern);
        string? fehlerKind = null;
        if (ahv == null) fehlerKind = "Keine AHV-Nummer auf der Mitteilung.";
        else if (kinder.Count == 0) fehlerKind = "Keine Kindzeile auf der Mitteilung.";
        return new FamzMitteilung(art, brief, ahv, nachname, vorname, fakKanton, kinder, fehlerKind);
    }

    public static string? Ahv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var ziffern = new string(raw.Where(char.IsDigit).ToArray());
        if (ziffern.Length != 13 || !ziffern.StartsWith("756", StringComparison.Ordinal)) return null;
        return $"{ziffern[..3]}.{ziffern[3..7]}.{ziffern[7..11]}.{ziffern[11..]}";
    }

    /// <summary>Zweistellig: 00–50 → 2000er, 51–99 → 1900er (Geburt 05 → 2005, 99 → 1999).</summary>
    public static DateOnly? Datum(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var p = raw.Trim().Split('.');
        if (p.Length != 3) return null;
        if (!int.TryParse(p[0], out var tag) || !int.TryParse(p[1], out var monat) || !int.TryParse(p[2], out var jahr))
            return null;
        if (p[2].Length <= 2)
            jahr += jahr <= 50 ? 2000 : 1900;
        try { return new DateOnly(jahr, monat, tag); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    static string Kern(string text)
    {
        var cut = text.IndexOf("Wichtige Hinweise", StringComparison.OrdinalIgnoreCase);
        if (cut < 0) cut = text.IndexOf("Legende zum Beendigungsgrund", StringComparison.OrdinalIgnoreCase);
        return cut > 0 ? text[..cut] : text;
    }

    /// <summary>
    /// AHV der Berechtigten (MA), nicht die erste Nummer auf dem Blatt.
    /// Bei Direktauszahlung steht oben links oft die AHV des Kindes («AHVNr.») —
    /// die des MA folgt erst bei «Berechtigte» / «AHV-Nummer:» (Fall Islamaj).
    /// </summary>
    public static string? AhvDerBerechtigten(string text)
    {
        var block = BerechtigteBlock(text);
        if (block != null)
        {
            var imBlock = AhvRx().Match(block);
            if (imBlock.Success) return Ahv(imBlock.Value);
        }
        // «AHV-Nummer:» = Berechtigte; Kopf «AHVNr.» bewusst nicht (Kind bei Direktauszahlung).
        var label = AhvNummerLabelRx().Match(text);
        if (label.Success) return Ahv(label.Groups[1].Value);
        return Ahv(AhvRx().Match(text).Value);
    }

    static string? BerechtigteBlock(string text)
    {
        var start = text.IndexOf("Berechtigte", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var rest = text[start..];
        var ende = rest.IndexOf("Kind", StringComparison.OrdinalIgnoreCase);
        if (ende < 0) ende = rest.IndexOf("Der Zulagenanspruch", StringComparison.OrdinalIgnoreCase);
        if (ende < 0) ende = rest.IndexOf("Für folgende Person", StringComparison.OrdinalIgnoreCase);
        if (ende > 0) rest = rest[..ende];
        return rest;
    }

    static (string? Nachname, string? Vorname) Berechtigte(string text)
    {
        var rest = BerechtigteBlock(text);
        if (rest == null) return (null, null);
        var m = NameKommaRx().Match(rest);
        if (!m.Success) return (null, null);
        return (m.Groups[1].Value.Trim(), m.Groups[2].Value.Trim());
    }

    /// <summary>
    /// Anspruchsende + Begründung. Primär «Zahlungen per … eingestellt», sonst Tabellenzeile.
    /// </summary>
    static (DateOnly? Ende, string? Grund) WegfallEnde(string text)
    {
        DateOnly? ende = null;
        string? grund = null;
        var per = EingestelltRx().Match(text);
        if (per.Success) ende = Datum(per.Groups[1].Value);

        var zeile = WegfallZeileRx().Match(text);
        if (zeile.Success)
        {
            ende ??= Datum(zeile.Groups["ende"].Value);
            grund = zeile.Groups["grund"].Value.Trim();
            if (grund.Length < 3) grund = null;
        }
        if (grund == null)
        {
            var g = BegruendungRx().Match(text);
            if (g.Success)
            {
                grund = g.Groups[1].Value.Trim();
                if (grund.Length < 3) grund = null;
            }
        }
        return (ende, grund);
    }

    static List<FamzMitteilungKind> Entscheid(string text)
    {
        var liste = new List<FamzMitteilungKind>();
        foreach (Match m in EntscheidRx().Matches(text))
        {
            var name = m.Groups["name"].Value;
            if (IstKeinKindname(name)) continue;
            var geburt = Datum(m.Groups["geb"].Value);
            var von = Datum(m.Groups["von"].Value);
            var bis = Datum(m.Groups["bis"].Value);
            if (geburt == null || von == null || bis == null) continue;
            if (!decimal.TryParse(m.Groups["betrag"].Value.Replace("'", "").Replace("’", ""),
                    NumberStyles.Number, CultureInfo.InvariantCulture, out var betrag))
                continue;
            var art = m.Groups["art"].Value.ToUpperInvariant();
            if (art == "ADOPTZ") art = "AdoptZ";
            int? grund = int.TryParse(m.Groups["grund"].Value, out var g) ? g : null;
            liste.Add(new FamzMitteilungKind(name, geburt.Value, von, bis, art, grund, null, betrag));
        }
        return liste;
    }

    static List<FamzMitteilungKind> Verfall(string text)
    {
        var liste = new List<FamzMitteilungKind>();
        foreach (Match m in VerfallRx().Matches(text))
        {
            var name = m.Groups["name"].Value;
            if (IstKeinKindname(name)) continue;
            var geburt = Datum(m.Groups["geb"].Value);
            var ende = Datum(m.Groups["ende"].Value);
            if (geburt == null || ende == null) continue;
            var grund = m.Groups["grund"].Value.Trim();
            if (grund.Length < 3) continue;
            liste.Add(new FamzMitteilungKind(name, geburt.Value, null, ende, null, null, grund, null));
        }
        return liste;
    }

    static bool IstKeinKindname(string name) =>
        name.Equals("Anspruch", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Kind", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Kinder", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Gültig", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Gueltig", StringComparison.OrdinalIgnoreCase);

    static FamzMitteilung Leer(FamzMitteilungArt art, string fehler) =>
        new(art, null, null, null, null, null, [], fehler);

    [GeneratedRegex(@"Datum:\s*(\d{2}\.\d{2}\.\d{4})", RegexOptions.CultureInvariant)]
    private static partial Regex DatumRx();

    [GeneratedRegex(@"756[\s.]*\d{4}[\s.]*\d{4}[\s.]*\d{2}", RegexOptions.CultureInvariant)]
    private static partial Regex AhvRx();

    [GeneratedRegex(@"AHV[\s\-]*Nummer\s*:\s*(756[\s.]*\d{4}[\s.]*\d{4}[\s.]*\d{2})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AhvNummerLabelRx();

    [GeneratedRegex(@"FAK\s+([A-Za-z]{2})", RegexOptions.CultureInvariant)]
    private static partial Regex FakRx();

    [GeneratedRegex(@"([A-ZÄÖÜÀ-Ý][\p{L}'’\-]+),\s+([A-ZÄÖÜÀ-Ý][\p{L}'’\-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex NameKommaRx();

    [GeneratedRegex(
        @"(?<name>[A-ZÄÖÜÀ-Ý][\p{L}'’\-]+)\s+(?<geb>\d{2}\.\d{2}\.\d{2,4})\s+(?<von>\d{2}\.\d{2}\.\d{2,4})\s+(?<bis>\d{2}\.\d{2}\.\d{2,4})\s+(?<art>KZ|AZ|GZ|AdoptZ|ADOPTZ)\s*(?<grund>\d)\s+[A-Z]{2}\s+(?<betrag>\d{1,3}(?:['’]\d{3})*|\d+)\.\d{2}",
        RegexOptions.CultureInvariant)]
    private static partial Regex EntscheidRx();

    [GeneratedRegex(
        @"(?<name>[A-ZÄÖÜÀ-Ý][\p{L}'’\-]+)\s+(?<geb>\d{2}\.\d{2}\.\d{2,4})\s+(?<ende>\d{2}\.\d{2}\.\d{4})\s+(?<grund>[^\r\n]{3,80})",
        RegexOptions.CultureInvariant)]
    private static partial Regex VerfallRx();

    [GeneratedRegex(@"per\s+(\d{2}\.\d{2}\.\d{4})\s+eingestellt",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EingestelltRx();

    /// <summary>
    /// Tabellenzeile: AHV · Name, Vorname · Anspruchsende · Begründung
    /// (Sithiravel Wegfallanzeige 20.10.2026).
    /// </summary>
    [GeneratedRegex(
        @"756[\s.]*\d{4}[\s.]*\d{4}[\s.]*\d{2}\s+[A-ZÄÖÜÀ-Ý][\p{L}'’\-]+\s*,\s+[A-ZÄÖÜÀ-Ý][\p{L}'’\-]+\s+(?<ende>\d{2}\.\d{2}\.\d{4})\s+(?<grund>[^\r\n]{3,80})",
        RegexOptions.CultureInvariant)]
    private static partial Regex WegfallZeileRx();

    [GeneratedRegex(@"Begründung\s*\r?\n[^\r\n]*?(Anderer Bezüger|[^\r\n]{3,60})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BegruendungRx();
}
