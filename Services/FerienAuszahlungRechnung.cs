namespace HrSystem.Services;

/// <summary>
/// Ferien auszahlen ohne Bezug (Walter-Vorgabe 05.10.2026). HR erfasst Tage;
/// das Geld kommt im Verhältnis aus dem Ferien-Topf (FLEX/MTP) bzw. Tage ×
/// Tagessatz (FIX/FIX-M). «Saldo per 31.12.» zahlt die Tage und das Geld aus,
/// die Ende Vorjahr standen — abzüglich dem, was seit 1.1. schon bezogen,
/// gekürzt oder ausbezahlt wurde (alte Tage gehen zuerst weg).
/// Kein Vorbezug: nie mehr Tage als vorhanden, nie mehr Geld als im Topf.
/// </summary>
public static class FerienAuszahlungRechnung
{
    public const string ArtTage = "TAGE";
    public const string ArtVorjahr = "VORJAHR";

    public record Posten(int Id, string Art, decimal? Tage);

    /// <summary>Ferien-Saldo per 31.12. Vorjahr (Tage und CHF-Topf).</summary>
    public record Vorjahr(decimal Tage, decimal Chf);

    /// <summary>Aufgelöster Posten: Tage nach Deckelung, bei «Saldo 31.12.» der CHF-Anteil des Vorjahres-Topfs.</summary>
    public record Ergebnis(int Id, string Art, decimal Tage, decimal? ChfVorgabe, string? Hinweis);

    public record Zeile(int Id, string Art, decimal Tage, decimal Betrag, decimal? Satz, string? Hinweis);

    /// <param name="verfuegbar">Ferientage nach Gutschrift, Bezug und Kürzung dieses Monats.</param>
    /// <param name="verbrauchtSeitJanuar">Seit 1.1. bezogene, gekürzte und früher ausbezahlte Tage.</param>
    public static List<Ergebnis> TageAufloesen(IEnumerable<Posten> posten, decimal verfuegbar,
        Vorjahr? vorjahr, decimal verbrauchtSeitJanuar)
    {
        var liste = new List<Ergebnis>();
        decimal rest = Math.Max(0m, verfuegbar);
        decimal verbraucht = Math.Max(0m, verbrauchtSeitJanuar);
        foreach (var p in posten)
        {
            if (p.Art == ArtVorjahr)
            {
                if (vorjahr == null)
                {
                    liste.Add(new(p.Id, p.Art, 0m, 0m, "Saldo per 31.12. Vorjahr unbekannt"));
                    continue;
                }
                decimal restVj = Math.Max(0m, vorjahr.Tage - verbraucht);
                decimal t = Math.Round(Math.Min(restVj, rest), 4);
                decimal chf = vorjahr.Tage > 0m
                    ? vorjahr.Chf * t / vorjahr.Tage
                    : (verbraucht == 0m ? Math.Max(0m, vorjahr.Chf) : 0m);
                string? hinweis = t < restVj ? $"max. {rest:0.00} Tage vorhanden" : null;
                liste.Add(new(p.Id, p.Art, t, Math.Max(0m, chf), hinweis));
                rest -= t;
                verbraucht += t;
            }
            else
            {
                decimal wunsch = Math.Max(0m, p.Tage ?? 0m);
                decimal t = Math.Round(Math.Min(wunsch, rest), 4);
                string? hinweis = t < wunsch ? $"max. {rest:0.00} Tage vorhanden" : null;
                liste.Add(new(p.Id, p.Art, t, null, hinweis));
                rest -= t;
                verbraucht += t;
            }
        }
        return liste;
    }

    /// <summary>FLEX/MTP: CHF = Tage × (Topf ÷ Tage) bzw. Vorjahres-Anteil, nie mehr als im Topf.</summary>
    public static List<Zeile> ZeilenAusTopf(IEnumerable<Ergebnis> ergebnisse, decimal topfRest, decimal tageVorAuszahlung)
    {
        var zeilen = new List<Zeile>();
        decimal topf = Math.Max(0m, topfRest);
        decimal tage = tageVorAuszahlung;
        foreach (var e in ergebnisse)
        {
            decimal? satz = tage > 0m ? topf / tage : null;
            decimal chf = e.ChfVorgabe ?? (satz.HasValue ? e.Tage * satz.Value : 0m);
            chf = PayrollCalculations.Rappen(Math.Min(Math.Max(0m, chf), topf));
            string? hinweis = e.Hinweis;
            if (chf == 0m && hinweis == null) hinweis = "kein Ferien-Guthaben";
            zeilen.Add(new(e.Id, e.Art, e.Tage, chf, satz.HasValue ? PayrollCalculations.Rappen(satz.Value) : null, hinweis));
            topf -= chf;
            tage -= e.Tage;
        }
        return zeilen;
    }

    /// <summary>FIX/FIX-M: kein CHF-Topf, Tage × Tagessatz (Monatslohn × 12 / 365).</summary>
    public static List<Zeile> ZeilenFix(IEnumerable<Ergebnis> ergebnisse, decimal tagessatz) =>
        ergebnisse.Select(e =>
        {
            decimal chf = PayrollCalculations.Rappen(e.Tage * tagessatz);
            string? hinweis = e.Hinweis ?? (chf == 0m ? "keine Ferientage vorhanden" : null);
            return new Zeile(e.Id, e.Art, e.Tage, chf, PayrollCalculations.Rappen(tagessatz), hinweis);
        }).ToList();

    public static string Bezeichnung(Zeile z, string grundtext, int vorjahr)
    {
        string tage = PayrollCalculations.Rappen(z.Tage).ToString("0.00");
        string text = z.Art == ArtVorjahr
            ? $"{grundtext} Saldo 31.12.{vorjahr} ({tage} Tage)"
            : z.Satz.HasValue ? $"{grundtext} ({tage} × {z.Satz.Value:0.00})" : $"{grundtext} ({tage} Tage)";
        return z.Hinweis != null ? $"{text} – {z.Hinweis}" : text;
    }
}
