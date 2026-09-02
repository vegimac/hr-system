using System.Globalization;
using System.Text.RegularExpressions;
using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Der Nummernkreis einer Filiale (Walter-Vorgabe 02.09.2026):
/// ein Präfix und eine feste Anzahl Stellen dahinter, z.B. «122» + 4 →
/// 1220001 bis 1229999.
///
/// Warum es das braucht: bisher galt eine Personalnummer als «zur Filiale
/// gehörig», sobald sie mit dem Restaurant-Code anfing — ohne Rücksicht auf
/// die Länge. Damit rutschte ein Vertipper wie «122023» (drei statt vier
/// Stellen) unbemerkt durch und wurde bei der Suche nach der letzten Nummer
/// sogar mitgezählt. Mit der festen Länge fliegt genau das auf.
///
/// WICHTIG: die Personalnummer ist bei uns KEIN Schlüssel, sondern ein
/// Infofeld. Der Nummernkreis kontrolliert Eingaben und schlägt die nächste
/// freie Nummer vor — er nummeriert NIE selbständig um.
/// </summary>
public sealed class Nummernkreis
{
    /// <summary>
    /// Nummern, die damit beginnen, sind alte Archivnummern aus der Zeit vor
    /// OneCrew (Walter). Sie gehören zu keiner laufenden Nummernfolge und
    /// zählen darum nirgends mit — weder als «letzte Nummer» noch als Beleg
    /// dafür, dass eine Nummer schon vergeben ist.
    /// </summary>
    public const string ArchivPraefix = "99";

    /// <summary>Präfix, z.B. «122». Leer = kein Präfix bekannt.</summary>
    public string Praefix { get; }

    /// <summary>
    /// Anzahl Stellen NACH dem Präfix. 0 = Länge nicht festgelegt; dann prüft
    /// der Kreis nur das Präfix — exakt das Verhalten vor dem 02.09.2026, für
    /// alle Filialen, bei denen der Kreis noch nicht gepflegt ist.
    /// </summary>
    public int Stellen { get; }

    public Nummernkreis(string? praefix, int? stellen)
    {
        Praefix = NurZiffern(praefix);
        var s = stellen ?? 0;
        Stellen = s > 0 && s <= 10 ? s : 0;
    }

    /// <summary>Länge ist festgelegt — nur dann greift die scharfe Prüfung.</summary>
    public bool HatLaenge => Praefix.Length > 0 && Stellen > 0;

    /// <summary>Gesamtlänge einer gültigen Nummer, 0 wenn nicht festgelegt.</summary>
    public int Gesamtlaenge => HatLaenge ? Praefix.Length + Stellen : 0;

    /// <summary>Anzeigemuster für die Maske, z.B. «122xxxx».</summary>
    public string Muster => HatLaenge ? Praefix + new string('x', Stellen) : Praefix + "…";

    /// <summary>Erste Nummer des Kreises, z.B. «1220001».</summary>
    public string Erste => HatLaenge ? Praefix + ((long)1).ToString(new string('0', Stellen), CultureInfo.InvariantCulture) : "";

    /// <summary>Letzte Nummer des Kreises, z.B. «1229999».</summary>
    public string Letzte => HatLaenge ? Praefix + new string('9', Stellen) : "";

    /// <summary>Satz für Fehlermeldungen: «122 + 4 Stellen (1220001–1229999)».</summary>
    public string Erwartung => HatLaenge
        ? $"{Praefix} + {Stellen} Stellen ({Erste}–{Letzte})"
        : (Praefix.Length > 0 ? $"beginnt mit {Praefix}" : "kein Nummernkreis hinterlegt");

    /// <summary>
    /// Gehört die Nummer in diesen Kreis? Rein numerisch, richtiges Präfix,
    /// richtige Länge (falls festgelegt), keine Archivnummer.
    /// </summary>
    public bool Passt(string? nummer)
    {
        var n = (nummer ?? "").Trim();
        if (n.Length == 0) return false;
        if (!Regex.IsMatch(n, @"^\d+$")) return false;
        if (n.StartsWith(ArchivPraefix, StringComparison.Ordinal)) return false;
        if (Praefix.Length > 0 && !n.StartsWith(Praefix, StringComparison.Ordinal)) return false;
        if (HatLaenge && n.Length != Gesamtlaenge) return false;
        return true;
    }

    /// <summary>
    /// Höchste Nummer aus der Liste, die in diesen Kreis passt. NULL = noch
    /// keine. Basis sind bewusst ALLE Mitarbeitenden inklusive der
    /// Ausgetretenen — vergeben ist vergeben (Walter 02.09.2026).
    /// </summary>
    public long? Hoechste(IEnumerable<string?> nummern)
    {
        long? max = null;
        foreach (var raw in nummern)
        {
            var n = (raw ?? "").Trim();
            if (!Passt(n)) continue;
            if (!long.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var v)) continue;
            if (max == null || v > max) max = v;
        }
        return max;
    }

    /// <summary>
    /// Nächste freie Nummer = höchste + 1, formatiert mit führenden Nullen.
    /// NULL, wenn kein Kreis definiert ist oder der Kreis voll wäre.
    /// </summary>
    public string? Naechste(IEnumerable<string?> nummern)
    {
        if (!HatLaenge) return null;
        var max = Hoechste(nummern);
        if (max == null) return Erste;
        var kandidat = (max.Value + 1).ToString(CultureInfo.InvariantCulture);
        return Passt(kandidat) ? kandidat : null;   // Kreis voll (…9999)
    }

    /// <summary>
    /// Klartext-Meldung, warum eine Nummer nicht passt. NULL = sie passt.
    /// Die Meldung nennt IMMER das erwartete Muster — «falsch» allein hilft
    /// niemandem, der die Nummer korrigieren soll.
    /// </summary>
    public string? Beanstandung(string? nummer, string? filialName = null)
    {
        var n = (nummer ?? "").Trim();
        if (Passt(n)) return null;

        // Zwei Faelle, zwei Formen — «Erwartet fuer der Filiale» liest sich
        // wie eine Maschine, und wer die Meldung nicht ernst nimmt, korrigiert
        // die Nummer auch nicht.
        var fuer = string.IsNullOrWhiteSpace(filialName) ? "diese Filiale" : $"die Filiale {filialName}";
        var von  = string.IsNullOrWhiteSpace(filialName) ? "dieser Filiale" : $"der Filiale {filialName}";
        if (n.Length == 0)
            return $"Personalnummer fehlt. Erwartet für {fuer}: {Erwartung}.";
        if (!Regex.IsMatch(n, @"^\d+$"))
            return $"Personalnummer «{n}» ist nicht rein numerisch. Erwartet für {fuer}: {Erwartung}.";
        if (n.StartsWith(ArchivPraefix, StringComparison.Ordinal))
            return $"Personalnummer «{n}» ist eine alte Archivnummer (beginnt mit {ArchivPraefix}) und darf nicht neu vergeben werden.";
        if (Praefix.Length > 0 && !n.StartsWith(Praefix, StringComparison.Ordinal))
            return $"Personalnummer «{n}» gehört nicht zum Nummernkreis {von}. Erwartet: {Erwartung}.";
        if (HatLaenge && n.Length != Gesamtlaenge)
            return $"Personalnummer «{n}» hat {n.Length} statt {Gesamtlaenge} Stellen. Erwartet für {fuer}: {Erwartung}.";
        return $"Personalnummer «{n}» passt nicht zum Nummernkreis {von}. Erwartet: {Erwartung}.";
    }

    /// <summary>
    /// Ziffern eines Codes, führende Nullen weg («0122» → «122»). Ersetzt die
    /// bisher an fünf Stellen kopierte NormalizeRestaurantPrefix.
    /// </summary>
    public static string NurZiffern(string? code)
    {
        var digits = Regex.Replace(code ?? "", @"\D", "").TrimStart('0');
        return digits;
    }

    /// <summary>
    /// Nummernkreis einer Filiale. Gepflegtes Präfix schlägt den
    /// Restaurant-Code; ist nichts gepflegt, bleibt es beim Restaurant-Code
    /// OHNE Längenprüfung — dann verhält sich OneCrew wie vor dem 02.09.2026.
    /// </summary>
    public static Nummernkreis Fuer(CompanyProfile? filiale)
    {
        if (filiale == null) return new Nummernkreis(null, null);
        var pfx = NurZiffern(filiale.PersonalnummerPraefix);
        if (pfx.Length > 0) return new Nummernkreis(pfx, filiale.PersonalnummerStellen);
        return new Nummernkreis(NurZiffern(filiale.RestaurantCode), null);
    }

    /// <summary>Variante für Aufrufer, die nur Code und Stellen zur Hand haben.</summary>
    public static Nummernkreis Fuer(string? praefix, int? stellen, string? restaurantCodeFallback)
    {
        var pfx = NurZiffern(praefix);
        if (pfx.Length > 0) return new Nummernkreis(pfx, stellen);
        return new Nummernkreis(NurZiffern(restaurantCodeFallback), null);
    }
}
