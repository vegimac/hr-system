namespace HrSystem.Services;

/// <summary>
/// Vergleicht eine eingelesene Mitteilung mit den Zulagen, die beim Kind schon stehen.
/// Neu, ändern (alte endet am Vortag), beenden, oder nichts. Betrag nur gegen den
/// Tarif der Filiale am «Gültig ab» — nicht gegen eine feste Liste.
/// </summary>
public static class FamzMitteilungVorschlag
{
    public const string Neu = "neu";
    public const string Aendern = "aendern";
    public const string Beenden = "beenden";
    public const string Nichts = "nichts";
    public const string Unklar = "unklar";

    public const string ModusNeu = "neu";
    public const string ModusTeilen = "teilen";
    public const string ModusSetzen = "setzen";
    public const string ModusEnde = "ende";
    public const string ModusBeleg = "beleg";

    public static FamzVorschlag Erstelle(FamzMitteilung m, FamzKontext k)
    {
        if (m.Art == FamzMitteilungArt.Unbekannt || m.Kinder.Count == 0)
            return new FamzVorschlag(m, k, []);

        var zeilen = new List<FamzZeile>();
        foreach (var kind in m.Kinder)
            zeilen.Add(m.Art == FamzMitteilungArt.Verfall ? Verfall(m, kind, k) : Entscheid(m, kind, k));
        var gesehen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < zeilen.Count; i++)
        {
            if (!gesehen.Add(zeilen[i].Schluessel))
                zeilen[i] = zeilen[i] with { Schluessel = zeilen[i].Schluessel + "-" + i };
        }
        return new FamzVorschlag(m, k, zeilen);
    }

    static FamzZeile Entscheid(FamzMitteilung m, FamzMitteilungKind kind, FamzKontext k)
    {
        if (kind.Von == null || kind.Bis == null || kind.Zulagenart == null || kind.Betrag == null)
            return UnklarZeile(kind, "Die Kindzeile ist unvollständig.");

        var kinder = Finde(k, kind);
        if (kinder.Count == 0)
            return UnklarZeile(kind, $"Kind «{kind.Vorname}», geboren {D(kind.Geburt)}, steht nicht in der Familie.");
        if (kinder.Count > 1)
            return UnklarZeile(kind, $"Mehrere Kinder heissen «{kind.Vorname}» und sind am {D(kind.Geburt)} geboren.");
        var wer = kinder[0];

        var tarif = k.Tarife.FirstOrDefault(t => t.Tag == kind.Von.Value);
        if (tarif == null || string.IsNullOrWhiteSpace(tarif.Kanton))
            return UnklarZeile(kind, $"Kein Vertrag mit Kanton am {D(kind.Von)}.");
        if (!string.IsNullOrWhiteSpace(m.FakKanton)
            && !string.Equals(m.FakKanton, tarif.Kanton, StringComparison.OrdinalIgnoreCase))
            return UnklarZeile(kind,
                $"Der Entscheid ist FAK {m.FakKanton}, der Vertrag am {D(kind.Von)} liegt in {tarif.Filiale} ({tarif.Kanton}).");

        var treffer = tarif.Saetze
            .Where(s => string.Equals(s.Art, kind.Zulagenart, StringComparison.OrdinalIgnoreCase)
                        && s.Betrag == kind.Betrag.Value)
            .ToList();
        if (treffer.Count != 1)
            return UnklarZeile(kind,
                $"{kind.Betrag.Value:0.00} CHF ist am {D(kind.Von)} kein {ArtWort(kind.Zulagenart)}-Satz in {tarif.Kanton} ({tarif.Filiale}).");
        var satz = treffer[0];

        var gleich = wer.Zulagen.FirstOrDefault(z =>
            ArtGleich(z.Art, satz.Art) && z.Satz == satz.SatzNr && z.Von == kind.Von && z.Bis == kind.Bis);
        if (gleich != null)
            return Zeile(Nichts, true, ModusBeleg, kind, wer.Id, gleich.Id, satz, kind.Von, kind.Bis, null,
                $"{kind.Vorname}: steht schon so ({satz.Label}, {D(kind.Von)}–{D(kind.Bis)}). Beleg kann an den Eintrag.");

        var ueber = wer.Zulagen
            .Where(z => Ueberlappt(z.Von, z.Bis, kind.Von.Value, kind.Bis))
            .OrderByDescending(z => z.Von)
            .ToList();
        if (ueber.Count > 1)
            return UnklarZeile(kind, $"{kind.Vorname}: mehrere Zulagen überlappen {D(kind.Von)}–{D(kind.Bis)}.");

        var erfahren = ErfahrenAm(m.Briefdatum, kind.Von.Value, tarif.ErsterOffener);
        var zeit = $"{satz.Label}, {D(kind.Von)}–{D(kind.Bis)}, erfahren am {D(erfahren)}";
        if (ueber.Count == 0)
            return Zeile(Neu, true, ModusNeu, kind, wer.Id, null, satz, kind.Von, kind.Bis, erfahren,
                $"{kind.Vorname}: neu {zeit}.");

        var alt = ueber[0];
        if (kind.Von.Value > alt.Von)
            return Zeile(Aendern, true, ModusTeilen, kind, wer.Id, alt.Id, satz, kind.Von, kind.Bis, erfahren,
                $"{kind.Vorname}: bisherige Zulage endet am {D(kind.Von.Value.AddDays(-1))}, neu {zeit}.");

        if (kind.Von.Value == alt.Von)
        {
            var kern = !ArtGleich(alt.Art, satz.Art) || alt.Satz != satz.SatzNr;
            if (kern && alt.Verwendet)
                return UnklarZeile(kind, $"{kind.Vorname}: die Zulage ab {D(alt.Von)} ist schon im Lohn verwendet. Art und Satz lassen sich nicht mehr ändern.");
            return Zeile(Aendern, true, ModusSetzen, kind, wer.Id, alt.Id, satz, kind.Von, kind.Bis, erfahren,
                $"{kind.Vorname}: ändern auf {zeit}.");
        }

        return UnklarZeile(kind,
            $"{kind.Vorname}: eine Zulage beginnt erst am {D(alt.Von)}, der Entscheid gilt schon ab {D(kind.Von)}.");
    }

    static FamzZeile Verfall(FamzMitteilung m, FamzMitteilungKind kind, FamzKontext k)
    {
        if (kind.Bis == null)
            return UnklarZeile(kind, "Kein Anspruchsende auf der Verfallanzeige.");
        var ende = kind.Bis.Value;
        var kinder = Finde(k, kind);
        if (kinder.Count == 0)
            return UnklarZeile(kind, $"Kind «{kind.Vorname}», geboren {D(kind.Geburt)}, steht nicht in der Familie.");
        if (kinder.Count > 1)
            return UnklarZeile(kind, $"Mehrere Kinder heissen «{kind.Vorname}» und sind am {D(kind.Geburt)} geboren.");
        var wer = kinder[0];
        var grund = string.IsNullOrWhiteSpace(kind.GrundText) ? "" : $" ({kind.GrundText})";

        var laufend = wer.Zulagen
            .Where(z => z.Von <= ende && (z.Bis == null || z.Bis.Value >= ende))
            .OrderByDescending(z => z.Von)
            .ToList();
        if (laufend.Count > 1)
            return UnklarZeile(kind, $"{kind.Vorname}: mehrere Zulagen laufen am {D(ende)}.");
        if (laufend.Count == 1)
        {
            var z = laufend[0];
            if (z.Bis == ende)
                return Zeile(Nichts, true, ModusBeleg, kind, wer.Id, z.Id, null, null, ende, null,
                    $"{kind.Vorname}: endet schon am {D(ende)}{grund}. Beleg kann an den Eintrag.");
            var bisher = z.HatBeleg ? " Der bisherige Beleg bleibt im Dossier." : "";
            return Zeile(Beenden, true, ModusEnde, kind, wer.Id, z.Id, null, z.Von, ende, null,
                $"{kind.Vorname}: beenden per {D(ende)}{grund}.{bisher}");
        }

        var letzte = wer.Zulagen.OrderByDescending(z => z.Von).FirstOrDefault();
        if (letzte == null)
            return UnklarZeile(kind, $"{kind.Vorname}: keine Zulage zum Beenden.");
        if (letzte.Bis == ende)
            return Zeile(Nichts, true, ModusBeleg, kind, wer.Id, letzte.Id, null, null, ende, null,
                $"{kind.Vorname}: endet schon am {D(ende)}{grund}. Beleg kann an den Eintrag.");
        var bisTxt = letzte.Bis == null ? "ohne Ende" : $"am {D(letzte.Bis)}";
        return UnklarZeile(kind, $"{kind.Vorname}: die letzte Zulage endet {bisTxt}, die Verfallanzeige nennt {D(ende)}.");
    }

    public static DateOnly ErfahrenAm(DateOnly? brief, DateOnly von, DateOnly? ersterOffener)
    {
        var ea = brief == null || brief.Value < von ? von : brief.Value;
        if (ersterOffener is DateOnly offen && von < offen && ea < offen)
            ea = offen;
        return ea;
    }

    static List<FamzKind> Finde(FamzKontext k, FamzMitteilungKind kind) =>
        k.Kinder.Where(c =>
            string.Equals(c.Vorname.Trim(), kind.Vorname.Trim(), StringComparison.OrdinalIgnoreCase)
            && c.Geburt == kind.Geburt).ToList();

    static bool Ueberlappt(DateOnly von, DateOnly? bis, DateOnly neuVon, DateOnly? neuBis)
    {
        var neuEnde = neuBis ?? DateOnly.MaxValue;
        var ende = bis ?? DateOnly.MaxValue;
        return von <= neuEnde && neuVon <= ende;
    }

    static bool ArtGleich(string? a, string? b) =>
        string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase);

    static string ArtWort(string art) => art.ToUpperInvariant() switch
    {
        "KZ" => "Kinderzulagen",
        "AZ" => "Ausbildungszulagen",
        "GZ" => "Geburtszulagen",
        "ADOPTZ" => "Adoptionszulagen",
        _ => art
    };

    static string D(DateOnly? d) => d == null ? "—" : d.Value.ToString("dd.MM.yyyy");

    static FamzZeile UnklarZeile(FamzMitteilungKind kind, string text) =>
        new(Schluessel(kind), Unklar, false, kind.Vorname, kind.Geburt, text,
            null, null, "", null, null, null, null, null, null);

    static FamzZeile Zeile(
        string aktion, bool waehlbar, string modus, FamzMitteilungKind kind,
        int familyMemberId, int? allowanceId, SatzOption? satz,
        DateOnly? von, DateOnly? bis, DateOnly? erfahren, string text) =>
        new(Schluessel(kind), aktion, waehlbar, kind.Vorname, kind.Geburt, text,
            familyMemberId, allowanceId, modus, satz?.Art, satz?.SatzNr, satz?.Betrag, von, bis, erfahren);

    static string Schluessel(FamzMitteilungKind kind)
    {
        var name = kind.Vorname.Trim().ToLowerInvariant();
        var geb = kind.Geburt.ToString("yyyyMMdd");
        var tag = (kind.Von ?? kind.Bis)?.ToString("yyyyMMdd") ?? "0";
        return $"{name}|{geb}|{tag}";
    }
}

public sealed record FamzKontext(
    int? EmployeeId,
    string? Vorname,
    string? Nachname,
    string? Filiale,
    string? FilialKanton,
    bool OhneLohn,
    DateOnly? ErsterOffener,
    IReadOnlyList<FamzKind> Kinder,
    IReadOnlyList<TarifAmTag> Tarife);

public sealed record FamzKind(int Id, string Vorname, DateOnly Geburt, IReadOnlyList<FamzZulage> Zulagen);

public sealed record FamzZulage(
    int Id, string? Art, int? Satz, DateOnly Von, DateOnly? Bis, bool Verwendet, bool HatBeleg);

public sealed record TarifAmTag(
    DateOnly Tag, string? Kanton, string Filiale, string? BranchCode, int? CompanyProfileId,
    DateOnly? ErsterOffener,
    IReadOnlyList<SatzOption> Saetze);

public sealed record SatzOption(string Art, int? SatzNr, decimal Betrag, string Label);

public sealed record FamzZeile(
    string Schluessel,
    string Aktion,
    bool Waehlbar,
    string Kind,
    DateOnly Geburt,
    string Text,
    int? FamilyMemberId,
    int? AllowanceId,
    string Modus,
    string? AllowanceType,
    int? TarifSatzNr,
    decimal? Betrag,
    DateOnly? Von,
    DateOnly? Bis,
    DateOnly? ErfahrenAm);

public sealed record FamzVorschlag(FamzMitteilung Mitteilung, FamzKontext Kontext, IReadOnlyList<FamzZeile> Zeilen);
