using HrSystem.Models;

namespace HrSystem.Services.EasyAtWork;

/// <summary>
/// Vertragsregeln einer Rechtseinheit (Walter-Vorgabe 01.09.2026).
///
/// Der easy@work-Sync prüft jeden Vertrag dagegen; was abweicht, wird NICHT
/// importiert und landet mit Klartext-Begründung auf der Fehlerliste.
///
/// Die Werte hängen am <see cref="Hauptsitz"/>, nicht an der Filiale:
/// Anstellungsbedingungen gehören zum Arbeitgeber. Alle Filialen einer GmbH
/// teilen dieselben Grenzen — sie können also nicht auseinanderdriften — und
/// ein zweiter Lizenznehmer mit eigener GmbH darf eigene setzen.
///
/// <see cref="Standard"/> greift, solange am Hauptsitz nichts erfasst ist
/// oder eine Filiale (noch) keinem Hauptsitz zugeordnet ist. Es gibt bewusst
/// keinen «ungeprüft»-Zustand: fehlende Konfiguration heisst Standardregeln,
/// nicht «alles erlaubt».
/// </summary>
public sealed record VertragsRegeln(
    IReadOnlyList<decimal> FixPensen,
    decimal FlexStundenMax,
    decimal MtpStundenMin,
    decimal MtpStundenMax)
{
    /// <summary>Vorgabe Walter 01.09.2026: FIX 50–100 in Zehnerschritten,
    /// FLEX höchstens 17 Std/Woche, MTP 17–38 Std/Woche.</summary>
    public static readonly VertragsRegeln Standard = new(
        new[] { 50m, 60m, 70m, 80m, 90m, 100m }, 17m, 17m, 38m);

    public bool FixPensumErlaubt(decimal pct) => FixPensen.Any(p => p == pct);

    /// <summary>Anzeigetext der erlaubten Pensen, z.B. «50, 60, 70, 80, 90, 100».</summary>
    public string FixPensenText => string.Join(", ", FixPensen.Select(p => p.ToString("0.##")));

    /// <summary>
    /// Regeln eines Hauptsitzes — jedes leere Feld fällt einzeln auf den
    /// Standard zurück, nicht der ganze Satz. So kann ein Lizenznehmer nur
    /// die MTP-Obergrenze anpassen und den Rest unangetastet lassen.
    /// </summary>
    public static VertragsRegeln Von(Hauptsitz? h)
    {
        if (h == null) return Standard;
        return new VertragsRegeln(
            ParsePensen(h.FixPensenErlaubt) ?? Standard.FixPensen,
            h.FlexStundenMax ?? Standard.FlexStundenMax,
            h.MtpStundenMin  ?? Standard.MtpStundenMin,
            h.MtpStundenMax  ?? Standard.MtpStundenMax);
    }

    /// <summary>«50,60,70» → Liste. Unlesbares oder Leeres ergibt null (= Standard).</summary>
    public static IReadOnlyList<decimal>? ParsePensen(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var teile = text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries
                                                      | StringSplitOptions.TrimEntries);
        var werte = new List<decimal>();
        foreach (var t in teile)
        {
            if (!decimal.TryParse(t.Replace("%", "").Trim(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                return null;              // ein unlesbarer Eintrag → ganze Liste verwerfen
            if (v > 0) werte.Add(v);
        }
        return werte.Count > 0 ? werte.Distinct().OrderBy(v => v).ToList() : null;
    }
}
