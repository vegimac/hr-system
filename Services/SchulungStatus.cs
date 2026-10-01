namespace HrSystem.Services;

/// <summary>
/// Schulungen &amp; Ausbildungen (Walter-Vorgabe 01.10.2026): reine Rechnung, ob eine
/// Schulung für einen MA gilt, erledigt, bald fällig, abgelaufen oder überfällig ist.
/// Geteilt von MA-Tab, Dashboard-Warnungen, Kontrolle «Lücken in Personalakten» und
/// der Übersicht «Schulungen». Seiteneffektfrei — alle Daten als Parameter.
///
/// Regeln:
/// - Zielgruppe: ALLE, FIXM (Vertragsmodell FIX-M oder Geschäftsführer), GF (Funktion
///   REST_MANAGER). Die Gastro-Ausbildung ist KEINE Schulung: sie kommt als Einstufung
///   aus easy@work und wird nur über den Mindestlohn-Check geprüft.
/// - Ablauf = Datum des jüngsten Eintrags + Auffrischung (Monate). Ohne Auffrischung
///   gilt der Eintrag unbegrenzt.
/// - Wiedereintritt: bei Schulungen mit Frist ab Eintritt zählt ein Eintrag vor dem
///   massgebenden Eintritt nicht mehr (Toleranz 30 Tage für ein Onboarding vor
///   Vertragsbeginn). Der nahtlose Filialwechsel ist kein neuer Eintritt — das regelt
///   <see cref="Dienstalter.Massgebend"/>, das der Aufrufer als Eintritt übergibt.
/// - Gemeldet wird «fehlt» nur, wenn es etwas zu mahnen gibt: bei Schulungen mit Frist
///   erst nach Fristablauf und nur für Eintritte ab Erfassung der Schulung im
///   Verzeichnis (sonst würde der ganze Altbestand auf einmal gemahnt); bei Schulungen
///   mit Auffrischung und ohne Frist (Nothelfer, Peak, SECO) sofort. Schulungen ohne
///   beides bleiben «offen», ohne To-do.
/// - «Warnen ab» leer = diese Schulung erzeugt keine To-dos.
/// </summary>
public static class SchulungStatus
{
    public const int WiedereintrittToleranzTage = 30;

    public static readonly string[] Zielgruppen = { "ALLE", "FIXM", "GF" };

    public sealed record Typ(int Id, string Code, string Name, int? RefreshMonate, int? FristTage,
                             string Zielgruppe, bool FredMoeglich, int? WarnenAbTage, DateTime CreatedAt);

    public sealed record Eintrag(int Id, int TypId, DateOnly Datum, string Art, int? DokumentId);

    /// <summary>Vertragsmodell, Funktion und Einstufung des am Stichtag geltenden Vertrags.</summary>
    public sealed record Kontext(string? Modell, string? JobGroupCode, string? EducationLevelCode, DateOnly? Eintritt);

    public enum Zustand { NichtBetroffen, Gueltig, LaeuftAb, Abgelaufen, OffenInFrist, Offen, Fehlt }

    public sealed record Ergebnis(
        Zustand Zustand,
        Eintrag? Letzter,
        DateOnly? GueltigBis,
        DateOnly? FaelligAm,
        int? TageBis,
        bool Melden);

    public static bool Betrifft(string? zielgruppe, Kontext k) => (zielgruppe ?? "ALLE").ToUpperInvariant() switch
    {
        "FIXM" => IstGf(k) || string.Equals(k.Modell, "FIX-M", StringComparison.OrdinalIgnoreCase),
        "GF"   => IstGf(k),
        _      => true,
    };

    private static bool IstGf(Kontext k) => string.Equals(k.JobGroupCode, "REST_MANAGER", StringComparison.OrdinalIgnoreCase);

    public static string ZielgruppeLabel(string? z) => (z ?? "ALLE").ToUpperInvariant() switch
    {
        "FIXM" => "FIX-M (Management)",
        "GF"   => "Geschäftsführer",
        _      => "alle",
    };

    /// <summary>Einträge, die für den aktuellen Eintritt zählen (Wiedereintritt-Regel).</summary>
    public static IEnumerable<Eintrag> Zaehlende(Typ typ, IEnumerable<Eintrag> eintraege, DateOnly? eintritt)
    {
        var eigene = eintraege.Where(e => e.TypId == typ.Id);
        if (typ.FristTage.HasValue && eintritt.HasValue)
        {
            var ab = eintritt.Value.AddDays(-WiedereintrittToleranzTage);
            eigene = eigene.Where(e => e.Datum >= ab);
        }
        return eigene;
    }

    public static Ergebnis Berechne(Typ typ, IEnumerable<Eintrag> eintraege, Kontext k, DateOnly heute)
    {
        if (!Betrifft(typ.Zielgruppe, k))
            return new Ergebnis(Zustand.NichtBetroffen, null, null, null, null, false);

        bool warnt = typ.WarnenAbTage.HasValue;
        var letzter = Zaehlende(typ, eintraege, k.Eintritt)
            .OrderByDescending(e => e.Datum).ThenByDescending(e => e.Id)
            .FirstOrDefault();

        if (letzter != null)
        {
            if (!typ.RefreshMonate.HasValue)
                return new Ergebnis(Zustand.Gueltig, letzter, null, null, null, false);

            var bis = letzter.Datum.AddMonths(typ.RefreshMonate.Value);
            int tage = bis.DayNumber - heute.DayNumber;
            if (tage < 0)
                return new Ergebnis(Zustand.Abgelaufen, letzter, bis, null, tage, warnt);
            if (warnt && tage <= typ.WarnenAbTage!.Value)
                return new Ergebnis(Zustand.LaeuftAb, letzter, bis, null, tage, true);
            return new Ergebnis(Zustand.Gueltig, letzter, bis, null, tage, false);
        }

        if (typ.FristTage.HasValue && k.Eintritt.HasValue)
        {
            var faellig = k.Eintritt.Value.AddDays(Math.Max(1, typ.FristTage.Value) - 1);
            int tage = faellig.DayNumber - heute.DayNumber;
            if (tage >= 0)
                return new Ergebnis(Zustand.OffenInFrist, null, null, faellig, tage, false);
            bool neuerEintritt = k.Eintritt.Value >= DateOnly.FromDateTime(typ.CreatedAt);
            return new Ergebnis(Zustand.Fehlt, null, null, faellig, tage, warnt && neuerEintritt);
        }

        bool mahnen = warnt && !typ.FristTage.HasValue && typ.RefreshMonate.HasValue;
        return new Ergebnis(mahnen ? Zustand.Fehlt : Zustand.Offen, null, null, null, null, mahnen);
    }

    /// <summary>Kurzlabel für Listen und die Übersicht.</summary>
    public static string ZustandLabel(Zustand z) => z switch
    {
        Zustand.Gueltig      => "erledigt",
        Zustand.LaeuftAb     => "läuft bald ab",
        Zustand.Abgelaufen   => "abgelaufen",
        Zustand.OffenInFrist => "offen (in Frist)",
        Zustand.Offen        => "offen",
        Zustand.Fehlt        => "fehlt",
        _                    => "",
    };
}
