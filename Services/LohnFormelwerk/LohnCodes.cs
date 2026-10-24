namespace HrSystem.Services.LohnFormelwerk;

/// <summary>
/// Verbindliche Lohnpositions-Codes = Swissdec-Showcase-Stamm (Walter 19.10.2026).
/// Keine OneCrew-/Mirus-Erfindungen als <c>code</c>. Mirus nur in <c>mirus_code</c>.
/// Betrieb (LGAV/Depot/Vorschuss) und Saldo-Vorträge (901–906) sind intern — kein ELM-Code.
/// </summary>
public static class LohnCodes
{
    // ── Lohn (Swissdec) ──────────────────────────────────────────────────
    public const string Festlohn = "1000";              // nur FIX/FIX-M (Monatslohn)
    public const string Stundenlohn = "1005";           // FLEX + MTP (Garantie und Mehrstunden)
    public const string Ueberstunden25 = "1061";
    public const string UeberstundenOhneZuschlag = "1065";
    public const string NachtKompensation = "1076";

    // ── Ferien / Feiertag ────────────────────────────────────────────────
    public const string FerienFlexTopf = "1160";
    public const string FeiertagEntschaedigung = "1161";
    public const string FerienFixGeld = "1166";
    public const string FerienAustritt = "1168";
    /// <summary>Alias: ausbezahlte Feiertage — Swissdec 1161.</summary>
    public const string FeiertagAusbezahlt = "1161";

    // ── 13. ML / Bonus ───────────────────────────────────────────────────
    public const string Ml13 = "1200";
    /// <summary>Auslöser «13. ML auszahlen» = dieselbe Nr. 1200; Erkennung über mirus_code 180.3 / Bezeichnung.</summary>
    public const string Ml13Auszahlen = "1200";
    public const string McBonus = "1210";
    public const string DiverseSpesen = "6070";

    // ── Familienzulagen ──────────────────────────────────────────────────
    public const string Kinderzulage = "3000";
    public const string Ausbildungszulage = "3010";
    public const string Geburtszulage = "3034";

    // ── Krankheit / Unfall / Korrekturen ─────────────────────────────────
    public const string KrankKarenz = "1301";
    public const string KrankTaggeld = "2035";
    public const string UnfallKarenz = "1300";
    public const string UnfallTaggeld = "2030";
    public const string KorrTaggeld = "2050";
    public const string KorrKtg = "2050";
    public const string KorrUvg = "2050";

    // ── EO ───────────────────────────────────────────────────────────────
    public const string EoTaggeld = "2000";
    public const string Militaer100 = "2000";
    public const string Militaer88 = "2000";
    public const string Militaer80 = "2000";
    public const string Zivilschutz100 = "2000";
    public const string Zivilschutz88 = "2000";
    public const string Zivilschutz80 = "2000";
    public const string MutterschaftEo = "2001";
    public const string VaterschaftEo = "2002";

    // ── SV / QST (Swissdec, Beleg + ELM) ──────────────────────────────────
    public const string Ahv = "5010";
    public const string Alv = "5012";
    public const string Alvz = "5014";
    public const string Nbuv = "5020";
    public const string Uvgz = "5030";
    public const string Ktg = "5042";
    public const string Bvg = "5050";
    public const string Qst = "5100";
    public const string QstKorr = "5102";
    public const string BvgAusgleichAg = "5212";

    // ── Betrieb intern (kein Swissdec — Fibu/Engine, UI-Sektion «Betrieb») ─
    public const string Lgav = "600.24";
    public const string UniformDepot = "600.32";
    public const string Vorschuss = "950.1";
    /// <summary>Legacy-Alias; kanonisch <see cref="BvgAusgleichAg"/> (5212).</summary>
    public const string BvgKorrVorjahr = "5212";

    // ── Saldo-Vortrag intern (901–906, nie ELM) ─────────────────────────
    public const string VortragZeit = "901";
    public const string VortragFeiertag = "902";
    public const string VortragFerienTage = "903";
    public const string VortragNacht = "904";
    public const string VortragFerienGeld = "905";
    public const string Vortrag13Ml = "906";

    /// <summary>901–906. Der Lohn erkennt den Vortrag an der Nummer, nicht an einem Text.</summary>
    public static readonly string[] VortragCodes =
    [
        VortragZeit, VortragFeiertag, VortragFerienTage,
        VortragNacht, VortragFerienGeld, Vortrag13Ml,
    ];

    public static bool IstSaldoVortrag(string? code)
        => !string.IsNullOrWhiteSpace(code) && VortragCodes.Contains(code.Trim());

    // ── Legacy-Aliase (gleiche Swissdec-Nr., nur Lesbarkeit im Code) ─────
    public const string FestlohnFerien = Festlohn;
    public const string FestlohnFeiertag = Festlohn;
    public const string Zusatzstunden = Festlohn;
    public const string StundenlohnUmsatz1 = Stundenlohn;
    public const string StundenlohnUmsatz2 = Stundenlohn;
    public const string StundenlohnUmsatz3 = Stundenlohn;
    public const string StundenlohnAlt = Stundenlohn;
    /// <summary>MTP Garantie-Stunden (Soll) — Swissdec 1005, nie 1000 Festlohn (Walter 19.10.2026).</summary>
    public const string MtpGarantiestunden = Stundenlohn;
    /// <summary>MTP Mehrstunden über Soll — Swissdec 1005.</summary>
    public const string MtpMehrstunden = Stundenlohn;
    public const string FerienAccrual = FerienFlexTopf;
    public const string Ml13Alt = Ml13;

    /// <summary>True = echte Showcase-/Treuhand-Nr. (nur Ziffern, ≥ 4).</summary>
    public static bool IstSwissdecStammNr(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var c = code.Trim();
        return c.Length >= 4 && c.Length <= 5 && c.All(char.IsDigit);
    }

    /// <summary>Betrieb oder Saldo-Vortrag — aktiv für Engine, kein ELM-Code.</summary>
    public static bool IstInternOhneSwissdec(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        var c = code.Trim();
        if (c is Lgav or UniformDepot or Vorschuss) return true;
        if (c is VortragZeit or VortragFeiertag or VortragFerienTage
            or VortragNacht or VortragFerienGeld or Vortrag13Ml) return true;
        return c.StartsWith("600.", StringComparison.Ordinal) || c.StartsWith("950.", StringComparison.Ordinal);
    }

    /// <summary>SV-/QST-Kategorie → Swissdec-Nr. für Belegzeile.</summary>
    public static string? SwissdecFuerSvKategorie(string? categoryCode) =>
        (categoryCode ?? "").ToUpperInvariant() switch
        {
            "AHV" => Ahv,
            "ALV" => Alv,
            "ALVZ" => Alvz,
            "NBUV" or "NBU" or "UVG" => Nbuv,
            "UVGZ" => Uvgz,
            "KTG" => Ktg,
            "BVG" or "BVG_ZUSATZ" => Bvg,
            "QST" => Qst,
            "QST_KORR" => QstKorr,
            _ => null
        };

    /// <summary>Swissdec-Nr. für ELM (leer = Betrieb/Vortrag/unbekannt).</summary>
    public static string SwissdecVon(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var c = code.Trim();
        if (IstInternOhneSwissdec(c)) return "";
        // Auslöser / Mirus-Alias → Treuhand-Nr.
        var viaAlias = LohnCodeAlias.Normiere(c);
        if (!string.Equals(viaAlias, c, StringComparison.OrdinalIgnoreCase)
            && IstSwissdecStammNr(viaAlias))
            return viaAlias;
        if (IstSwissdecStammNr(c)) return c.Length > 4 ? c[..4] : c;
        var punkt = c.IndexOf('.');
        var kopf = punkt > 0 ? c[..punkt] : c;
        if (kopf is "600" or "950" or "901" or "902" or "903" or "904" or "905" or "906") return "";
        if (kopf.Length >= 4 && kopf.All(char.IsDigit)) return kopf[..4];
        return "";
    }

    /// <summary>Mirus-Altcode für Parallelvergleich / Kontoplan (eine typische Nr.).</summary>
    public static readonly IReadOnlyDictionary<string, string> MirusVonSwissdec =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Festlohn] = "10.1",
            [Stundenlohn] = "20",
            [Ueberstunden25] = "55.1",
            [UeberstundenOhneZuschlag] = "55.2",
            [NachtKompensation] = "55.10",
            [FerienFlexTopf] = "40.1",
            [FerienFixGeld] = "40.1",
            [FerienAustritt] = "40.1",
            [FeiertagEntschaedigung] = "195.2",
            [Ml13] = "180.1",
            [McBonus] = "200.5",
            [DiverseSpesen] = "200.1",
            [Kinderzulage] = "190.1",
            [Ausbildungszulage] = "190.2",
            [Geburtszulage] = "190.3",
            [KrankKarenz] = "70.1",
            [KrankTaggeld] = "70.2",
            [UnfallKarenz] = "60.2",
            [UnfallTaggeld] = "60.3",
            [KorrTaggeld] = "75.1",
            [EoTaggeld] = "80.1",
            [MutterschaftEo] = "120.1",
            [VaterschaftEo] = "120.2",
            [Ahv] = "103",
            [Alv] = "104",
            [Ktg] = "107",
            [Nbuv] = "110",
            [Bvg] = "111",
            [Qst] = "120",
            [QstKorr] = "565",
            [BvgAusgleichAg] = "600.5",
            [Lgav] = "600.24",
            [UniformDepot] = "600.32",
            [Vorschuss] = "950.1",
        };

    public static string? MirusFuer(string? swissdecCode)
    {
        if (string.IsNullOrWhiteSpace(swissdecCode)) return null;
        return MirusVonSwissdec.TryGetValue(swissdecCode.Trim(), out var m) ? m : null;
    }

    public static (int Position, int? Sub) KontoplanSchluessel(string? mirusOderCode)
    {
        var raw = mirusOderCode?.Trim() ?? "";
        if (MirusVonSwissdec.TryGetValue(raw, out var mirus)) raw = mirus;
        var teile = raw.Split('.', 2);
        if (!int.TryParse(teile[0], out var pos)) return (0, null);
        int? sub = teile.Length > 1 && int.TryParse(teile[1], out var s) ? s : null;
        return (pos, sub);
    }
}
