using System.Globalization;
using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Lohnausweis-Angaben: Filiale als Vorgabe, Person überschreibt.
/// Texte nach der Wegleitung zum Lohnausweis, gültig ab 1. Januar 2026
/// (ESTV/SSK, Formular 11). Wo die Wegleitung nur ein Beispiel gibt und
/// der Lohnstandard einen festen Satz hat, steht der feste Satz.
/// </summary>
public static class LohnausweisAngaben
{
    public const string ArtLohn = "LOHN";
    public const string ArtRente = "RENTE";
    public const string ModusEigen = "EIGEN";
    public const string ModusKein = "KEIN";

    public static readonly string[] Kantone =
    {
        "AG", "AI", "AR", "BE", "BL", "BS", "FR", "GE", "GL", "GR",
        "JU", "LU", "NE", "NW", "OW", "SG", "SH", "SO", "SZ", "TG",
        "TI", "UR", "VD", "VS", "ZG", "ZH",
    };

    public readonly record struct Grant(DateOnly Datum, string Kanton);

    public sealed record NachgenussAngabe(
        decimal Betrag, string Name, string Vorname, string Strasse, string Plz, string Ort);

    /// <summary>Was auf dem Lohnausweis dieses Jahres steht.</summary>
    public sealed record Wirkung(
        bool IstRente,
        bool BoxF,
        bool BoxG,
        string? Ziffer14,
        Grant? Expat,
        bool WagenAbklaeren,
        bool WagenMinimal,
        Grant? Spesenreglement,
        bool Rz52,
        bool KinderzulageAhv,
        decimal? Umzugskosten,
        Grant? Verkehrswert,
        bool Gesperrt,
        bool NichtKotiert,
        bool Anwartschaft,
        bool Unecht,
        NachgenussAngabe? Nachgenuss,
        int? AnzahlLohnausweise,
        IReadOnlyList<string> StandardBemerkungen,
        string? SpesenHinweis);

    public static bool IstKanton(string? code) =>
        code != null && Kantone.Contains(code.Trim().ToUpperInvariant());

    public static Wirkung Aufloesen(
        CompanyProfile? firma,
        EmployeeLohnausweis? person,
        int jahr,
        int? anzahlLohnausweise)
    {
        var art = (person?.Art ?? ArtLohn).Trim().ToUpperInvariant();
        var rente = art == ArtRente;

        var boxF = person?.BoxF ?? firma?.LohnausweisBoxFFreierTransport ?? false;
        var boxG = person?.BoxG ?? firma?.LohnausweisBoxGKantineGratis ?? false;
        var z14 = Text(person?.Ziffer14Modus, person?.Ziffer14Text, firma?.LohnausweisZiffer14);

        var expat = GrantVon(person?.ExpatModus, person?.ExpatDatum, person?.ExpatKanton,
            firma?.LohnausweisExpatDatum, firma?.LohnausweisExpatKanton);
        var wagenAb = person?.WagenAbklaeren ?? firma?.LohnausweisWagenAbklaeren ?? false;
        var wagenMin = person?.WagenMinimal ?? firma?.LohnausweisWagenMinimal ?? false;
        var spesen = GrantVon(person?.SpesenModus, person?.SpesenDatum, person?.SpesenKanton,
            firma?.LohnausweisSpesenDatum, firma?.LohnausweisSpesenKanton);
        var rz52Gespeichert = person?.Rz52 ?? firma?.LohnausweisRz52 ?? false;
        // Wegleitung 2026 Rz 65: genehmigtes Reglement ⇒ kein Kreuz in 13.1.1.
        // Das ELM-Schema erlaubt nur eines von beiden.
        var rz52 = spesen == null && rz52Gespeichert;

        var wert = GrantVon(person?.WertModus, person?.WertDatum, person?.WertKanton,
            firma?.LohnausweisWertDatum, firma?.LohnausweisWertKanton);
        var (gesperrt, nichtKotiert, anwartschaft, unecht) = Gruende(person, firma);

        var kinderzulage = person?.KinderzulageAhv ?? false;
        decimal? umzug = person?.UmzugBetrag is decimal u && u > 0 && person.UmzugJahr == jahr
            ? u : null;

        NachgenussAngabe? nachgenuss = null;
        if (person?.Nachgenuss == true
            && person.NachgenussJahr == jahr
            && person.NachgenussBetrag is decimal nb && nb > 0
            && !string.IsNullOrWhiteSpace(person.NachgenussName)
            && !string.IsNullOrWhiteSpace(person.NachgenussVorname)
            && !string.IsNullOrWhiteSpace(person.NachgenussStrasse)
            && !string.IsNullOrWhiteSpace(person.NachgenussPlz)
            && !string.IsNullOrWhiteSpace(person.NachgenussOrt))
        {
            nachgenuss = new NachgenussAngabe(
                nb,
                person.NachgenussName.Trim(),
                person.NachgenussVorname.Trim(),
                person.NachgenussStrasse.Trim(),
                person.NachgenussPlz.Trim(),
                person.NachgenussOrt.Trim());
        }

        var anzahl = anzahlLohnausweise is > 1 ? anzahlLohnausweise : null;
        var bemerkungen = Bemerkungen(
            kinderzulage, umzug, wert, gesperrt, nichtKotiert, anwartschaft, unecht,
            wagenAb, wagenMin, nachgenuss, expat, spesen, anzahl);

        var hinweis = spesen != null && rz52Gespeichert
            ? "Genehmigtes Spesenreglement und die Angabe RZ 52 sind beide gesetzt. "
              + "Auf dem Lohnausweis gilt das Reglement; das Kreuz in Ziffer 13.1.1 entfällt "
              + "(Wegleitung 2026, Randziffer 65)."
            : null;

        return new Wirkung(
            rente, boxF, boxG, z14, expat, wagenAb, wagenMin, spesen, rz52,
            kinderzulage, umzug, wert, gesperrt, nichtKotiert, anwartschaft, unecht,
            nachgenuss, anzahl, bemerkungen, hinweis);
    }

    /// <summary>
    /// Vier Zeilen des Bemerkungsblocks (14.1, 14.2, 15.1, 15.2).
    /// Ziffer 14 bleibt den nicht bewerteten Nebenleistungen, Ziffer 15
    /// den Standardbemerkungen und den freien Hinweisen.
    /// </summary>
    public static void Zeilen(
        Wirkung w,
        IReadOnlyList<string> frei,
        out string? z141, out string? z142, out string? z151, out string? z152)
    {
        var z14 = Split(w.Ziffer14, 110).Take(2).ToList();
        var z15 = new List<string>();
        foreach (var b in w.StandardBemerkungen)
            z15.Add(b);
        foreach (var f in frei)
            if (!string.IsNullOrWhiteSpace(f)) z15.Add(f.Trim());
        while (z15.Count > 2)
        {
            var letzte = z15[^1];
            z15.RemoveAt(z15.Count - 1);
            z15[^1] = z15[^1] + " · " + letzte;
        }

        z141 = z14.ElementAtOrDefault(0);
        z142 = z14.ElementAtOrDefault(1);
        z151 = z15.ElementAtOrDefault(0);
        z152 = z15.ElementAtOrDefault(1);
    }

    public static string? EinerVon(int? anzahl)
    {
        if (anzahl is null or < 2) return null;
        var n = anzahl.Value;
        var wort = n switch
        {
            2 => "zwei",
            3 => "drei",
            4 => "vier",
            5 => "fünf",
            6 => "sechs",
            7 => "sieben",
            8 => "acht",
            9 => "neun",
            10 => "zehn",
            _ => n.ToString(CultureInfo.InvariantCulture),
        };
        return "Einer von " + wort + " Lohnausweisen";
    }

    public static LohnausweisZusatz AlsZusatz(Wirkung w, string? freieBemerkung) => new()
    {
        IstRente = w.IstRente,
        Ziffer14 = w.Ziffer14,
        SpesenDatumIso = w.Spesenreglement?.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        SpesenKanton = w.Spesenreglement?.Kanton,
        Rz52 = w.Rz52,
        KinderzulageAhv = w.KinderzulageAhv,
        Umzugskosten = w.Umzugskosten,
        WertDatumIso = w.Verkehrswert?.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        WertKanton = w.Verkehrswert?.Kanton,
        Gesperrt = w.Gesperrt,
        NichtKotiert = w.NichtKotiert,
        Anwartschaft = w.Anwartschaft,
        Unecht = w.Unecht,
        WagenAbklaeren = w.WagenAbklaeren,
        WagenMinimal = w.WagenMinimal,
        NachgenussName = w.Nachgenuss?.Name,
        NachgenussVorname = w.Nachgenuss?.Vorname,
        NachgenussStrasse = w.Nachgenuss?.Strasse,
        NachgenussPlz = w.Nachgenuss?.Plz,
        NachgenussOrt = w.Nachgenuss?.Ort,
        NachgenussBetrag = w.Nachgenuss?.Betrag,
        ExpatDatumIso = w.Expat?.Datum.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ExpatKanton = w.Expat?.Kanton,
        AnzahlLohnausweise = w.AnzahlLohnausweise,
        Standardtext = w.StandardBemerkungen.Count == 0
            ? null
            : string.Join(" · ", w.StandardBemerkungen),
        FreieBemerkung = string.IsNullOrWhiteSpace(freieBemerkung) ? null : freieBemerkung.Trim(),
    };

    private static List<string> Bemerkungen(
        bool kinderzulage, decimal? umzug, Grant? wert,
        bool gesperrt, bool nichtKotiert, bool anwartschaft, bool unecht,
        bool wagenAb, bool wagenMin, NachgenussAngabe? nachgenuss,
        Grant? expat, Grant? spesen, int? anzahl)
    {
        var list = new List<string>();
        if (spesen is Grant sg)
            list.Add($"Spesenreglement durch Kanton {sg.Kanton} am {Datum(sg.Datum)} genehmigt");
        if (expat is Grant eg)
            list.Add($"Expatriateruling durch Kanton {eg.Kanton} am {Datum(eg.Datum)} genehmigt");
        if (EinerVon(anzahl) is string einer)
            list.Add(einer);
        if (wert is Grant wg)
            list.Add($"Verkehrswert durch Kanton {wg.Kanton} am {Datum(wg.Datum)} genehmigt");
        var gruende = new List<string>();
        if (gesperrt) gruende.Add("Gesperrte Mitarbeiteroptionen");
        if (nichtKotiert) gruende.Add("Nicht börsenkotierte Mitarbeiteroptionen");
        if (anwartschaft) gruende.Add("Anwartschaften auf Mitarbeiteraktien");
        if (unecht) gruende.Add("Unechte Mitarbeiterbeteiligungen");
        if (gruende.Count > 0)
            list.Add("Mitarbeiterbeteiligung ohne steuerbares Einkommen. Grund: " + string.Join(", ", gruende));
        // Wegleitung 2026 Rz 22: «Geschäftsfahrzeug» (die Checkliste sagt noch «Geschäftswagen»).
        if (wagenAb)
            list.Add("Privatanteil Geschäftsfahrzeug im Veranlagungsverfahren abzuklären");
        if (wagenMin)
            list.Add("Privatanteil wird vom Arbeitnehmer bezahlt");
        if (umzug is decimal uz)
            list.Add("Umzugskosten von CHF " + Franken(uz) + " bezahlt");
        if (kinderzulage)
            list.Add("Kinderzulagen im LA nicht enthalten. Auszahlung durch Ausgleichskasse");
        if (nachgenuss is NachgenussAngabe ng)
            list.Add($"Besoldungsnachgenuss an {ng.Name}, {ng.Vorname}, {ng.Strasse}, {ng.Plz} {ng.Ort}");
        return list;
    }

    private static string Datum(DateOnly d) => d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string Franken(decimal betrag)
    {
        var ganz = Math.Round(betrag, 0, MidpointRounding.AwayFromZero);
        var ch = new NumberFormatInfo
        {
            NumberDecimalSeparator = ".",
            NumberGroupSeparator = "'",
            NumberGroupSizes = new[] { 3 },
        };
        return ganz.ToString("N2", ch);
    }

    private static string? Text(string? modus, string? person, string? firma)
    {
        modus = Norm(modus);
        if (modus == ModusKein) return null;
        if (modus == ModusEigen)
            return string.IsNullOrWhiteSpace(person) ? null : person.Trim();
        return string.IsNullOrWhiteSpace(firma) ? null : firma.Trim();
    }

    private static Grant? GrantVon(
        string? modus, DateOnly? personDatum, string? personKanton,
        DateOnly? firmaDatum, string? firmaKanton)
    {
        modus = Norm(modus);
        if (modus == ModusKein) return null;
        if (modus == ModusEigen)
            return Paar(personDatum, personKanton);
        return Paar(firmaDatum, firmaKanton);
    }

    private static Grant? Paar(DateOnly? datum, string? kanton)
    {
        if (datum is not DateOnly d || !IstKanton(kanton)) return null;
        return new Grant(d, kanton!.Trim().ToUpperInvariant());
    }

    private static (bool Gesperrt, bool NichtKotiert, bool Anwartschaft, bool Unecht) Gruende(
        EmployeeLohnausweis? person, CompanyProfile? firma)
    {
        var modus = Norm(person?.BeteiligungModus);
        if (modus == ModusKein) return (false, false, false, false);
        if (modus == ModusEigen && person != null)
            return (person.BeteiligungGesperrt, person.BeteiligungNichtKotiert,
                person.BeteiligungAnwartschaft, person.BeteiligungUnecht);
        return (
            firma?.LohnausweisBetGesperrt ?? false,
            firma?.LohnausweisBetNichtKotiert ?? false,
            firma?.LohnausweisBetAnwartschaft ?? false,
            firma?.LohnausweisBetUnecht ?? false);
    }

    private static string Norm(string? modus) => (modus ?? "").Trim().ToUpperInvariant();

    private static IEnumerable<string> Split(string? text, int max)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var rest = text.Trim();
        while (rest.Length > max)
        {
            var schnitt = rest.LastIndexOf(' ', max);
            if (schnitt < 20) schnitt = max;
            yield return rest[..schnitt].Trim();
            rest = rest[schnitt..].Trim();
        }
        if (rest.Length > 0) yield return rest;
    }
}

/// <summary>
/// Aufgelöste Angaben für den Barcode. Daten, die der Vorschau-Dialog
/// nicht mitschickt, setzt der Server vor dem Druck aus der Datenbank.
/// </summary>
public class LohnausweisZusatz
{
    public bool IstRente { get; set; }
    public string? Ziffer14 { get; set; }
    public string? SpesenDatumIso { get; set; }
    public string? SpesenKanton { get; set; }
    public bool Rz52 { get; set; }
    public bool KinderzulageAhv { get; set; }
    public decimal? Umzugskosten { get; set; }
    public string? WertDatumIso { get; set; }
    public string? WertKanton { get; set; }
    public bool Gesperrt { get; set; }
    public bool NichtKotiert { get; set; }
    public bool Anwartschaft { get; set; }
    public bool Unecht { get; set; }
    public bool WagenAbklaeren { get; set; }
    public bool WagenMinimal { get; set; }
    public string? NachgenussName { get; set; }
    public string? NachgenussVorname { get; set; }
    public string? NachgenussStrasse { get; set; }
    public string? NachgenussPlz { get; set; }
    public string? NachgenussOrt { get; set; }
    public decimal? NachgenussBetrag { get; set; }
    public string? ExpatDatumIso { get; set; }
    public string? ExpatKanton { get; set; }
    public int? AnzahlLohnausweise { get; set; }
    public string? Standardtext { get; set; }
    public string? FreieBemerkung { get; set; }
}
