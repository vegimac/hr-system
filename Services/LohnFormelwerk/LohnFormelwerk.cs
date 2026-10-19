using HrSystem.Services;
using static HrSystem.Services.PayrollCalculations;

namespace HrSystem.Services.LohnFormelwerk;

/// <summary>
/// Zentrale Formel-Registry (Walter 19.10.2026). Engine ruft hier nach — keine verstreuten
/// Hardcodes mehr. Entwicklung → Lohn-Formeln liest dieselbe Liste. Kein Freitext-Interpreter.
/// </summary>
public static class LohnFormelwerk
{
    public const string TagessatzFix = "TagessatzFix";
    public const string TagessatzMtp = "TagessatzMtp";
    public const string FerienPctFlex = "FerienPctFlex";
    public const string FeiertagPctFlex = "FeiertagPctFlex";
    public const string Ml13PctFlex = "Ml13PctFlex";
    public const string NachtKomp10 = "NachtKomp10";
    public const string Ktg80 = "Ktg80";
    public const string Uvg80 = "Uvg80";
    public const string Round05 = "Round05";
    public const string FerienGeldFlex = "FerienGeldFlex";
    public const string FeiertagGeldFlex = "FeiertagGeldFlex";
    public const string Ml13GeldFlex = "Ml13GeldFlex";
    public const string FerienFixSplit = "FerienFixSplit";
    public const string NachtFixSplit = "NachtFixSplit";
    public const string StundenlohnBetrag = "StundenlohnBetrag";
    public const string FestlohnMonat = "FestlohnMonat";
    public const string Zusatzstunden = "Zusatzstunden";
    public const string Ueberstunden25 = "Ueberstunden25";
    public const string KrankKarenz100 = "KrankKarenz100";
    public const string UnfallKarenz100 = "UnfallKarenz100";
    public const string KorrKtg75 = "KorrKtg75";
    public const string KorrUvg65 = "KorrUvg65";

    private static readonly IReadOnlyList<LohnFormelDefinition> _alle = BuildAll();

    public static IReadOnlyList<LohnFormelDefinition> Alle => _alle;

    public static LohnFormelDefinition? Finde(string id) =>
        _alle.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<LohnFormelDefinition> BuildAll() => new List<LohnFormelDefinition>
    {
        new(TagessatzFix, "Tagessatz FIX/FIX-M",
            "Monatslohn ÷ 21.7  (21.7 = 260 ATwerktage ÷ 12)",
            "Vertrag.MonthlyWage; Konstante 21.7",
            "PayrollCalculations.CalcTagesSatz / LohnFormelwerk.BerechneTagessatzFix",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.Festlohn),

        new(TagessatzMtp, "Tagessatz MTP",
            "Garantierte Std./Woche × Stundenlohn × 52 ÷ 12 ÷ 21.7",
            "Vertrag.GuaranteedHoursPerWeek, Vertrag.HourlyWage",
            "PayrollCalculations.CalcTagesSatzMtp / LohnFormelwerk.BerechneTagessatzMtp",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.Stundenlohn),

        new(FerienPctFlex, "Ferien-% FLEX/MTP",
            "Filiale.FerienPercent (Standard 10.64 % bei 5 Wochen)",
            "CompanyProfile.FerienPercent",
            "PayrollCalculations.CalcFerienGeld",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.FerienFlexTopf),

        new(FeiertagPctFlex, "Feiertag-% FLEX/MTP",
            "Filiale.FeiertagPercent (Standard 2.27 %)",
            "CompanyProfile.FeiertagPercent",
            "PayrollCalculations.CalcFeiertagGeld",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.FeiertagEntschaedigung),

        new(Ml13PctFlex, "13. ML-% FLEX",
            "Filiale.ThirteenthMonthPercent (Standard 8.33 %)",
            "CompanyProfile.ThirteenthMonthPercent",
            "PayrollCalculations.Calc13MlGeld",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.Ml13),

        new(FerienGeldFlex, "Feriengeld FLEX (Topf)",
            "Round05(Basis × Ferien%) — Basis = SV-pflichtige Stundenlohn-Zeilen",
            "FerienPercent; Slip-Basis",
            "PayrollCalculations.CalcFerienGeld",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.FerienFlexTopf),

        new(FeiertagGeldFlex, "Feiertagsentschädigung FLEX",
            "Round05(Basis × Feiertag%)",
            "FeiertagPercent; Slip-Basis",
            "PayrollCalculations.CalcFeiertagGeld",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.FeiertagEntschaedigung),

        new(Ml13GeldFlex, "13. Monatslohn (FLEX monatlich / sonst Topf)",
            "Round05(Basis × 13.ML%)",
            "ThirteenthMonthPercent; Slip-Basis",
            "PayrollCalculations.Calc13MlGeld",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.Ml13),

        new(StundenlohnBetrag, "Stundenlohn-Betrag",
            "Rappen(Stunden × Stundenlohn) — FLEX Ist; MTP Garantie + Mehrstunden (beide 1005)",
            "Stempelzeiten; Vertrag.HourlyWage / Garantie",
            "PayrollCalculationEngine (FLEX/MTP-Zweig)",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.Stundenlohn),

        new(FestlohnMonat, "Festlohn Monat",
            "Monatslohn (ggf. Pro-Rata Ein-/Austritt) — nur FIX/FIX-M, nie MTP",
            "Vertrag.MonthlyWage; Periodenanteil",
            "PayrollCalculationEngine (FIX-Zweig)",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.Festlohn),

        new(Zusatzstunden, "Zusatzstunden FIX",
            "Round05(ExtraStunden × Stundenlohn)",
            "Stempel − Soll; Vertrag.HourlyWage",
            "PayrollCalculationEngine",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.Zusatzstunden),

        new(Ueberstunden25, "Überstunden 25 %",
            "Round05(Std × Stundenlohn × 1.25)",
            "Überstunden-Saldo; Vertrag.HourlyWage",
            "PayrollCalculationEngine",
            SchemaArt: "ereignis", SwissdecCode: LohnCodes.Ueberstunden25),

        new(NachtKomp10, "Nachtkompensation 10 %",
            "Round05(Nachtstunden × Stundenlohn × 0.10)",
            "Stempel Nachtfenster Filiale; Vertrag.HourlyWage",
            "PayrollCalculationEngine / FilialNachtfenster",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.NachtKompensation),

        new(NachtFixSplit, "Nacht FIX Split (Brutto ±0)",
            "−Tagessatz × Nacht-Tage (1000) + Nachtkomp 10 % (1076); Brutto unverändert",
            "TagessatzFix; Nachtstunden; Monatslohn",
            "PayrollCalculationEngine (FIX-Nacht)",
            SchemaArt: "automatisch", SwissdecCode: LohnCodes.NachtKompensation),

        new(FerienFixSplit, "Ferien FIX Split (1166)",
            "−Tagessatz × Ferientage (1000.2) + Ferienlohn (1166); Brutto ±0",
            "TagessatzFix; Ferien-Absenzen",
            "PayrollCalculationEngine",
            SchemaArt: "saldo", SwissdecCode: LohnCodes.FerienFixGeld),

        new(KrankKarenz100, "Krankheit Karenz 100 %",
            "Tagessatz × Karenz-Tage (max. laut Filiale)",
            "Tagessatz; KarenzService; Absenz KRANK",
            "PayrollCalculationEngine.AddLohnersatz",
            SchemaArt: "ereignis", SwissdecCode: LohnCodes.KrankKarenz),

        new(Ktg80, "KTG-Taggeld 80 %",
            "Round05(Tagessatz × 0.80 × Taggeld-Tage)",
            "Tagessatz; KtgService",
            "PayrollCalculationEngine.AddLohnersatz",
            SchemaArt: "ereignis", SwissdecCode: LohnCodes.KrankTaggeld),

        new(UnfallKarenz100, "Unfall Karenz 100 %",
            "Tagessatz × Karenz-Tage",
            "Tagessatz; KarenzService; Absenz UNFALL",
            "PayrollCalculationEngine.AddLohnersatz",
            SchemaArt: "ereignis", SwissdecCode: LohnCodes.UnfallKarenz),

        new(Uvg80, "UVG-Taggeld 80 %",
            "Round05(Tagessatz × 0.80 × Taggeld-Tage)",
            "Tagessatz; UVG-Logik",
            "PayrollCalculationEngine.AddLohnersatz",
            SchemaArt: "ereignis", SwissdecCode: LohnCodes.UnfallTaggeld),

        new(KorrKtg75, "Korrektur KTG (→2050)",
            "FIX/MTP: −(Garantie÷7 × Stundenlohn) pro Kalendertag Krankheit nach Karenz",
            "KrankUnfallZeitgutschrift; Vertrag",
            "KrankUnfallZeitgutschrift / Engine",
            SchemaArt: "ereignis", SwissdecCode: LohnCodes.KorrKtg),

        new(KorrUvg65, "Korrektur UVG (→2050)",
            "FIX/MTP: −(Garantie÷7 × Stundenlohn) pro Kalendertag Unfall nach Karenz",
            "KrankUnfallZeitgutschrift; Vertrag",
            "KrankUnfallZeitgutschrift / Engine",
            SchemaArt: "ereignis", SwissdecCode: LohnCodes.KorrUvg),

        new(Round05, "Rappenrundung 0.05",
            "Round(Betrag × 20) / 20  (kaufmännisch auf 5 Rappen)",
            "jeder Geldbetrag auf dem Slip",
            "PayrollCalculations.Round05",
            SchemaArt: null, SwissdecCode: null),
    };

    // ── Typisierte Aufrufe (Engine bindet hier an) ───────────────────────

    /// <summary>FIX/FIX-M Tagessatz (Austritt/RST): Monatslohn × 12 / 365.</summary>
    public static decimal BerechneTagessatzFix(decimal monatslohn) =>
        ExitTagessatzFix(monatslohn);

    /// <summary>MTP-Tagessatz-Näherung: Garantie-Woche × Satz × 52 / 12 / 21.7.</summary>
    public static decimal BerechneTagessatzMtp(decimal guaranteedHoursPerWeek, decimal hourlyWage)
    {
        if (guaranteedHoursPerWeek <= 0 || hourlyWage <= 0) return 0m;
        decimal monatslohn = guaranteedHoursPerWeek * hourlyWage * 52m / 12m;
        return Round05(monatslohn / 21.7m);
    }

    /// <summary>FLEX/MTP Ferien-% auf Basis (Akkumulation, nicht Pott-Bezug).</summary>
    public static decimal BerechneFerienGeld(decimal basis, decimal ferienPct) =>
        Round05(basis * ferienPct / 100m);

    public static decimal BerechneFeiertagGeld(decimal basis, decimal feiertagPct) =>
        Round05(basis * feiertagPct / 100m);

    public static decimal Berechne13MlGeld(decimal basis, decimal pct) =>
        Round05(basis * pct / 100m);

    public static decimal BerechneNachtKomp(decimal nachtStunden, decimal stundenlohn) =>
        Round05(nachtStunden * stundenlohn * 0.10m);

    public static decimal BerechneUeberstunden25(decimal std, decimal stundenlohn) =>
        Round05(std * stundenlohn * 1.25m);

    public static decimal BerechneStundenlohn(decimal stunden, decimal satz) =>
        Round05(stunden * satz);

    public static decimal BerechneTaggeld80(decimal tagessatz, int tage) =>
        Round05(tagessatz * 0.80m * tage);

    public static decimal BerechneRunden(decimal betrag) => Round05(betrag);

    /// <summary>Generischer Einstieg — für UI/Tests; Engine nutzt typisierte Methoden.</summary>
    public static LohnFormelErgebnis Berechne(string formelId, IReadOnlyDictionary<string, decimal> p)
    {
        decimal G(string k) => p.TryGetValue(k, out var v) ? v : 0m;
        return formelId switch
        {
            TagessatzFix => new(BerechneTagessatzFix(G("monatslohn"))),
            TagessatzMtp => new(BerechneTagessatzMtp(G("garantieStdWoche"), G("stundenlohn"))),
            FerienGeldFlex or FerienPctFlex => new(BerechneFerienGeld(G("basis"), G("ferienPct"))),
            FeiertagGeldFlex or FeiertagPctFlex => new(BerechneFeiertagGeld(G("basis"), G("feiertagPct"))),
            Ml13GeldFlex or Ml13PctFlex => new(Berechne13MlGeld(G("basis"), G("ml13Pct"))),
            NachtKomp10 => new(BerechneNachtKomp(G("nachtStunden"), G("stundenlohn"))),
            Ueberstunden25 => new(BerechneUeberstunden25(G("stunden"), G("stundenlohn"))),
            StundenlohnBetrag => new(BerechneStundenlohn(G("stunden"), G("stundenlohn"))),
            Ktg80 or Uvg80 => new(BerechneTaggeld80(G("tagessatz"), (int)G("tage"))),
            Round05 => new(BerechneRunden(G("betrag"))),
            _ => new(0m, $"Unbekannte Formel «{formelId}»"),
        };
    }
}
