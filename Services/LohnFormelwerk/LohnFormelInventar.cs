namespace HrSystem.Services.LohnFormelwerk;

/// <summary>
/// Etappe 0 — Inventar Engine-Hardcodes → Formel / Swissdec / Schema-Art
/// (Walter 19.10.2026). Eine Wahrheit für Doku und Entwicklung-UI.
/// </summary>
public static class LohnFormelInventar
{
    public sealed record Zeile(
        string AlterCode,
        string SwissdecCode,
        string FormelId,
        string SchemaArt,
        string Bemerkung);

    public static IReadOnlyList<Zeile> Alle { get; } = new List<Zeile>
    {
        new("10.1", LohnCodes.Festlohn, LohnFormelwerk.FestlohnMonat, "automatisch", "FIX/FIX-M Monatslohn"),
        new("10.2", LohnCodes.FestlohnFerien, LohnFormelwerk.FerienFixSplit, "saldo", "FIX Ferien −Tage"),
        new("10.3", LohnCodes.FestlohnFeiertag, LohnFormelwerk.FestlohnMonat, "ereignis", "FIX Feiertag −Tage"),
        new("10.4", LohnCodes.Zusatzstunden, LohnFormelwerk.Zusatzstunden, "automatisch", "FIX Zusatzstunden"),
        new("20", LohnCodes.Stundenlohn, LohnFormelwerk.StundenlohnBetrag, "automatisch", "FLEX/MTP Stundenlohn"),
        new("20.1", LohnCodes.StundenlohnUmsatz1, LohnFormelwerk.StundenlohnBetrag, "automatisch", "Umsatzlohn 1"),
        new("20.2", LohnCodes.StundenlohnUmsatz2, LohnFormelwerk.StundenlohnBetrag, "automatisch", "Umsatzlohn 2"),
        new("20.3", LohnCodes.StundenlohnUmsatz3, LohnFormelwerk.StundenlohnBetrag, "automatisch", "Umsatzlohn 3"),
        new("22", LohnCodes.StundenlohnAlt, LohnFormelwerk.StundenlohnBetrag, "automatisch", "Stundenlohn Alt"),
        new("55.3", LohnCodes.MtpMehrstunden, LohnFormelwerk.StundenlohnBetrag, "automatisch", "MTP Mehrstunden → Aufwand 4001"),
        new("55.1", LohnCodes.Ueberstunden25, LohnFormelwerk.Ueberstunden25, "ereignis", "Überstunden 25 %"),
        new("55.2", LohnCodes.UeberstundenOhneZuschlag, LohnFormelwerk.StundenlohnBetrag, "ereignis", "Überstunden ohne Zuschlag"),
        new("55.10", LohnCodes.NachtKompensation, LohnFormelwerk.NachtKomp10, "automatisch", "Nacht 10 %; FIX: NachtFixSplit"),
        new("195.1", LohnCodes.FerienFlexTopf, LohnFormelwerk.FerienGeldFlex, "automatisch", "FLEX/MTP Ferien-% Akkumulation"),
        new("40.1", LohnCodes.FerienFlexTopf, LohnFormelwerk.FerienGeldFlex, "saldo", "Ferien-Pott-Auszahlung / 1166 FIX Split"),
        new("195.2", LohnCodes.FeiertagEntschaedigung, LohnFormelwerk.FeiertagGeldFlex, "automatisch", "Feiertag-%"),
        new("50.1", LohnCodes.FeiertagEntschaedigung, LohnFormelwerk.FeiertagGeldFlex, "saldo", "Feiertag ausbezahlt"),
        new("180.1", LohnCodes.Ml13, LohnFormelwerk.Ml13GeldFlex, "automatisch", "13. ML"),
        new("180.2", LohnCodes.Ml13, LohnFormelwerk.Ml13GeldFlex, "automatisch", "13. ML Alt"),
        new("180.3", LohnCodes.Ml13Auszahlen, LohnFormelwerk.Ml13GeldFlex, "austritt", "13. ML auszahlen (Auslöser → Code 1200, mirus 180.3)"),
        new("125.1", LohnCodes.KorrTaggeld, LohnFormelwerk.Round05, "ereignis", "Korr. Mutterschaft → 2050"),
        new("125.2", LohnCodes.KorrTaggeld, LohnFormelwerk.Round05, "ereignis", "Korr. Vaterschaft → 2050"),
        new("85.1", LohnCodes.KorrTaggeld, LohnFormelwerk.Round05, "ereignis", "Korr. Militär → 2050"),
        new("95.1", LohnCodes.KorrTaggeld, LohnFormelwerk.Round05, "ereignis", "Korr. Zivilschutz → 2050"),
        new("200.5", LohnCodes.McBonus, LohnFormelwerk.Round05, "manuell", "McBonus"),
        new("200.1", LohnCodes.DiverseSpesen, LohnFormelwerk.Round05, "manuell", "Diverse Spesen"),
        new("190.1", LohnCodes.Kinderzulage, LohnFormelwerk.Round05, "automatisch", "Kinderzulage"),
        new("190.2", LohnCodes.Ausbildungszulage, LohnFormelwerk.Round05, "automatisch", "Ausbildungszulage"),
        new("190.3", LohnCodes.Geburtszulage, LohnFormelwerk.Round05, "ereignis", "Geburtszulage"),
        new("70.1", LohnCodes.KrankKarenz, LohnFormelwerk.KrankKarenz100, "ereignis", "Krankheit Karenz"),
        new("70.2", LohnCodes.KrankTaggeld, LohnFormelwerk.Ktg80, "ereignis", "KTG 80 %"),
        new("60.2", LohnCodes.UnfallKarenz, LohnFormelwerk.UnfallKarenz100, "ereignis", "Unfall Karenz"),
        new("60.3", LohnCodes.UnfallTaggeld, LohnFormelwerk.Uvg80, "ereignis", "UVG 80 %"),
        new("75.1", LohnCodes.KorrKtg, LohnFormelwerk.KorrKtg75, "ereignis", "Korrektur KTG FIX/MTP"),
        new("65.1", LohnCodes.KorrUvg, LohnFormelwerk.KorrUvg65, "ereignis", "Korrektur UVG FIX/MTP"),
        new("80.1", LohnCodes.Militaer100, LohnFormelwerk.Round05, "ereignis", "Militär 100 %"),
        new("80.2", LohnCodes.Militaer88, LohnFormelwerk.Round05, "ereignis", "Militär 88 %"),
        new("80.3", LohnCodes.Militaer80, LohnFormelwerk.Round05, "ereignis", "Militär 80 %"),
        new("90.1", LohnCodes.Zivilschutz100, LohnFormelwerk.Round05, "ereignis", "Zivilschutz 100 %"),
        new("90.2", LohnCodes.Zivilschutz88, LohnFormelwerk.Round05, "ereignis", "Zivilschutz 88 %"),
        new("90.3", LohnCodes.Zivilschutz80, LohnFormelwerk.Round05, "ereignis", "Zivilschutz 80 %"),
        new("120.1", LohnCodes.MutterschaftEo, LohnFormelwerk.Round05, "ereignis", "Mutterschaft EO"),
        new("120.2", LohnCodes.VaterschaftEo, LohnFormelwerk.Round05, "ereignis", "Vaterschaft EO"),
        new("600.24", LohnCodes.Lgav, LohnFormelwerk.Round05, "automatisch", "L-GAV Beitrag"),
        new("600.32", LohnCodes.UniformDepot, LohnFormelwerk.Round05, "ereignis", "Uniform-Depot"),
        new("950.1", LohnCodes.Vorschuss, LohnFormelwerk.Round05, "manuell", "Vorschuss"),
        // Pendenz-Positionen (Etappe 4)
        new("(neu)", LohnCodes.FerienFixGeld, LohnFormelwerk.FerienFixSplit, "saldo", "FIX Ferien Split 1166"),
        new("(neu)", LohnCodes.FerienAustritt, LohnFormelwerk.FerienFixSplit, "austritt", "Ferien Austritt 1168"),
        new("55.10", LohnCodes.NachtKompensation, LohnFormelwerk.NachtFixSplit, "automatisch", "FIX Nacht Split 1076"),
    };
}
