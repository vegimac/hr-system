using System.Text.Json;
using HrSystem.Models;

namespace HrSystem.Services;

/// <summary>
/// Zeitgutschrift bei Krankheit/Unfall für FIX, FIX-M und MTP (Walter 05.10.2026,
/// docs/zeitgutschrift-krank-unfall-konzept.md). Die einzige Stelle für diese
/// Rechnung — Lohnrechnung, Absenz-Maske, Neuberechnung und Berichte rufen sie auf.
///
/// Methode der Filiale:
///   DIENSTPLAN_1_7 — bis «Dienstplan bis» (inkl.): angekreuzter Tag = Tagessoll (FIX/FIX-M
///                    Wochenstunden ÷ Arbeitstage, MTP Garantie ÷ 5), nicht angekreuzt = 0;
///                    danach bzw. ohne Datum: Wochenstunden ÷ 7 pro Kalendertag
///   KALENDER_1_7   — jeder Kalendertag Wochenstunden ÷ 7
///   MO_FR_1_5      — Mo–Fr Wochenstunden ÷ 5, Sa/So 0
/// Krank-% wirkt auf jeden Tag. Keine Wochengrenze — Plusstunden sind möglich.
/// Wochenstunden: FIX/FIX-M Vertrag (pensum-bereinigt), MTP Garantie.
/// Geld ist davon getrennt (Korrektur + Taggeld pro Kalendertag).
/// </summary>
public static class KrankUnfallZeitgutschrift
{
    public const string DienstplanDann17 = "DIENSTPLAN_1_7";
    public const string Kalender17       = "KALENDER_1_7";
    public const string MoFr15           = "MO_FR_1_5";
    public static readonly string[] Methoden = { DienstplanDann17, Kalender17, MoFr15 };

    public const decimal ArbeitstageMin = 0.5m;
    public const decimal ArbeitstageMax = 6m;

    public enum TagArt { Geplant, Frei, Kalender, Werktag, Wochenende }

    public record Tag(DateOnly Datum, TagArt Art, decimal Stunden);

    public static string Methode(string? methode)
        => Methoden.Contains(methode) ? methode! : DienstplanDann17;

    public static bool Betrifft(string? absenzTyp, string? modell)
        => (absenzTyp ?? "").ToUpperInvariant() is "KRANK" or "UNFALL"
        && Modell(modell) is "FIX" or "FIX-M" or "MTP";

    private static string Modell(string? modell)
        => (modell ?? "").Trim().ToUpperInvariant();

    /// <summary>FIX/FIX-M: Wochenstunden laut Vertrag (Betrieb × Pensum) · MTP: Garantie.</summary>
    public static decimal Wochenstunden(Employment emp, decimal? betriebWochenstunden)
    {
        decimal betrieb = betriebWochenstunden ?? 42m;
        if (Modell(emp.EmploymentModel) == "MTP")
            return emp.GuaranteedHoursPerWeek ?? emp.WeeklyHours ?? betrieb;
        return emp.WeeklyHours ?? betrieb * (emp.EmploymentPercentage ?? 100m) / 100m;
    }

    /// <summary>Arbeitstage pro Woche gibt es nur bei FIX/FIX-M; MTP rechnet immer 1/5 der Garantie.</summary>
    public static bool HatArbeitstage(string? modell) => Modell(modell) is "FIX" or "FIX-M";

    /// <summary>
    /// Vorschlag auf 0.5 gerundet: FIX/FIX-M 5 × Pensum (volle Tage, reduzierte Anzahl —
    /// 80 % → 4), höchstens 5. MTP immer 5 (Garantie ÷ 5 pro angekreuztem Tag).
    /// </summary>
    public static decimal ArbeitstageVorschlag(string? modell, decimal? pensum, decimal? garantie, decimal? betriebWochenstunden)
    {
        if (!HatArbeitstage(modell)) return 5m;
        decimal roh = 5m * (pensum is > 0 ? pensum.Value : 100m) / 100m;
        decimal gerundet = Math.Round(roh * 2m, MidpointRounding.AwayFromZero) / 2m;
        return Math.Clamp(gerundet, ArbeitstageMin, 5m);
    }

    public static decimal Arbeitstage(Employment emp, decimal? betriebWochenstunden)
        => HatArbeitstage(emp.EmploymentModel) && emp.ArbeitstageProWoche is >= ArbeitstageMin and <= ArbeitstageMax
            ? emp.ArbeitstageProWoche.Value
            : ArbeitstageVorschlag(emp.EmploymentModel, emp.EmploymentPercentage, emp.GuaranteedHoursPerWeek, betriebWochenstunden);

    /// <summary>
    /// Neuer Vertragsabschnitt (easy@work-Import, Lohnanpassung, neuer Vertrag): eine von Hand
    /// eingetragene Anzahl Arbeitstage vom Vorgänger übernehmen, solange beide FIX/FIX-M sind
    /// und das Pensum gleich bleibt — easy@work kennt das Feld nicht. Sonst gilt der Vorschlag.
    /// </summary>
    public static void ArbeitstageUebernehmen(Employment neu, Employment? vorgaenger)
    {
        if (neu.ArbeitstageProWoche != null || vorgaenger?.ArbeitstageProWoche is not decimal w) return;
        if (!HatArbeitstage(neu.EmploymentModel) || !HatArbeitstage(vorgaenger.EmploymentModel)) return;
        if ((neu.EmploymentPercentage ?? 100m) != (vorgaenger.EmploymentPercentage ?? 100m)) return;
        neu.ArbeitstageProWoche = w;
    }

    /// <summary>Erlaubte Handeingabe: 0.5 bis 6 in Schritten von 0.5.</summary>
    public static bool ArbeitstageGueltig(decimal wert)
        => wert >= ArbeitstageMin && wert <= ArbeitstageMax && wert * 2m == Math.Floor(wert * 2m);

    public static HashSet<DateOnly> GeplanteTage(string? workedDaysJson)
    {
        var set = new HashSet<DateOnly>();
        if (string.IsNullOrWhiteSpace(workedDaysJson)) return set;
        try
        {
            foreach (var s in JsonSerializer.Deserialize<string[]>(workedDaysJson) ?? Array.Empty<string>())
                if (DateOnly.TryParse(s, out var d)) set.Add(d);
        }
        catch (JsonException) { }
        return set;
    }

    /// <summary>Tage von <paramref name="von"/> bis <paramref name="bis"/> (inkl.) mit Stunden, exakt (ungerundet).</summary>
    public static IReadOnlyList<Tag> Tage(
        string? methode, DateOnly von, DateOnly bis, DateOnly? dienstplanBis,
        IReadOnlySet<DateOnly> geplant, decimal wochenstunden, decimal arbeitstage, decimal prozent)
    {
        var liste = new List<Tag>();
        if (bis < von || wochenstunden <= 0) return liste;
        decimal faktor = (prozent > 0 ? Math.Min(prozent, 100m) : 100m) / 100m;
        decimal proGeplant = arbeitstage > 0 ? wochenstunden / arbeitstage : wochenstunden / 5m;
        string m = Methode(methode);
        for (var d = von; d <= bis; d = d.AddDays(1))
        {
            TagArt art;
            decimal h;
            if (m == Kalender17)
            {
                art = TagArt.Kalender; h = wochenstunden / 7m;
            }
            else if (m == MoFr15)
            {
                bool we = d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
                art = we ? TagArt.Wochenende : TagArt.Werktag;
                h = we ? 0m : wochenstunden / 5m;
            }
            else if (dienstplanBis is DateOnly planEnde && d <= planEnde)
            {
                bool ja = geplant.Contains(d);
                art = ja ? TagArt.Geplant : TagArt.Frei;
                h = ja ? proGeplant : 0m;
            }
            else
            {
                art = TagArt.Kalender; h = wochenstunden / 7m;
            }
            liste.Add(new Tag(d, art, h * faktor));
        }
        return liste;
    }

    public static decimal Stunden(
        string? methode, DateOnly von, DateOnly bis, DateOnly? dienstplanBis,
        IReadOnlySet<DateOnly> geplant, decimal wochenstunden, decimal arbeitstage, decimal prozent)
        => Math.Round(Tage(methode, von, bis, dienstplanBis, geplant, wochenstunden, arbeitstage, prozent).Sum(t => t.Stunden), 2);

    /// <summary>Für eine Absenz, optional auf ein Fenster (Lohnperiode, Woche) beschnitten.</summary>
    public static IReadOnlyList<Tag> Tage(
        Absence a, Employment emp, CompanyProfile? filiale, DateOnly? fensterVon = null, DateOnly? fensterBis = null)
    {
        var von = fensterVon is DateOnly fv && fv > a.DateFrom ? fv : a.DateFrom;
        var bis = fensterBis is DateOnly fb && fb < a.DateTo ? fb : a.DateTo;
        return Tage(filiale?.ZeitgutschriftKrankMethode, von, bis, a.DienstplanBis,
            GeplanteTage(a.WorkedDays),
            Wochenstunden(emp, filiale?.NormalWeeklyHours),
            Arbeitstage(emp, filiale?.NormalWeeklyHours),
            a.Prozent);
    }

    public static decimal Stunden(
        Absence a, Employment emp, CompanyProfile? filiale, DateOnly? fensterVon = null, DateOnly? fensterBis = null)
        => Math.Round(Tage(a, emp, filiale, fensterVon, fensterBis).Sum(t => t.Stunden), 2);

    /// <summary>Kurzer Satz für Maske und Lohnzettel, z.B. «2 geplante Tage × 8.40 h + 3 Kalendertage × 6.00 h».</summary>
    public static string Erklaerung(IReadOnlyList<Tag> tage)
    {
        var teile = new List<string>();
        void Teil(TagArt art, string einzahl, string mehrzahl)
        {
            var g = tage.Where(t => t.Art == art).ToList();
            if (g.Count == 0) return;
            var proTag = g.Max(t => t.Stunden);
            teile.Add($"{g.Count} {(g.Count == 1 ? einzahl : mehrzahl)} × {proTag:0.00} h");
        }
        Teil(TagArt.Geplant, "geplanter Tag", "geplante Tage");
        Teil(TagArt.Werktag, "Werktag", "Werktage");
        Teil(TagArt.Kalender, "Kalendertag", "Kalendertage");
        int frei = tage.Count(t => t.Art is TagArt.Frei or TagArt.Wochenende);
        if (frei > 0) teile.Add($"{frei} × 0 h {(tage.Any(t => t.Art == TagArt.Frei) ? "nicht eingeplant" : "Wochenende")}");
        return string.Join(" + ", teile);
    }
}
