using System.Text.Json;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Fibu-Journal v3 (Walter-Vorgabe 04.08.2026): Brutto-Umgliederung.
/// Testet die statische Summen-Extraktion aus dem SlipJson —
/// FibuJournalService.ExtractBruttoUmgliederung (kein DB-Zugriff):
///   • Familienzulagen → 2071 (Durchlauf)
///   • KTG-/UVG-Taggeld 80% → 2014 (Forderung Versicherung), Karenz 88% NICHT
///   • 13.-ML-Saldo-Auszahlung / Nachzahlung nach Probezeit → 2017/2016,
///     «akt. Monat» und FLEX-Monatszeile bleiben Aufwand
///   • 13.-ML-Verfall (betrag=0, Wert in accrued) → RST-Auflösung
/// Alt-Snapshot-Sicherheit: lohnLines tragen keine Codes — Matching über
/// DB-Bezeichnungen + fixe Engine-Fallback-Prefixe; findet nichts → Leer
/// (Verhalten wie v2.3, nichts doppelt, nichts vergessen).
/// </summary>
public class FibuJournalUmgliederungTests
{
    private static JsonElement Slip(string lohnLinesJson)
    {
        using var doc = JsonDocument.Parse("{\"lohnLines\":" + lohnLinesJson + "}");
        return doc.RootElement.Clone();
    }

    private static readonly IReadOnlyList<string> FamzNamen =
        new[] { "Kinderzulage", "Ausbildungszulage", "Geburts-/Adoptionszulage" };
    private static readonly IReadOnlyList<string> KtgNamen = new[] { "Krankheit (Taggeld 80%)" };
    private static readonly IReadOnlyList<string> UvgNamen = new[] { "Unfall (Taggeld 80%)" };

    [Fact]
    public void Familienzulagen_MitUndOhneBemerkung_WerdenSummiert()
    {
        var slip = Slip("""
        [
          { "bezeichnung": "Kinderzulage (Arman, 15 J.)",        "betrag": 200.00 },
          { "bezeichnung": "Kinderzulage (Leyla, 3 J.)",         "betrag": 200.00 },
          { "bezeichnung": "Ausbildungszulage (Deniz, 17 J.)",   "betrag": 250.00 },
          { "bezeichnung": "Geburts-/Adoptionszulage (Geburt Nino)", "betrag": 1000.00 },
          { "bezeichnung": "Stundenlohn", "betrag": 3500.00 }
        ]
        """);
        var r = FibuJournalService.ExtractBruttoUmgliederung(slip, FamzNamen, KtgNamen, UvgNamen);
        Assert.Equal(1650.00m, r.Famz);
        Assert.Equal(0m, r.KtgTaggeld);
        Assert.Equal(1650.00m, r.AufwandAbzug);
    }

    [Fact]
    public void Familienzulage_LohnZuTief_Betrag0_ZaehltNicht()
    {
        var slip = Slip("""
        [ { "bezeichnung": "Kinderzulage (Arman, 15 J. – Lohn zu tief)", "betrag": 0.00 } ]
        """);
        var r = FibuJournalService.ExtractBruttoUmgliederung(slip, FamzNamen, KtgNamen, UvgNamen);
        Assert.Equal(0m, r.Famz);
    }

    [Fact]
    public void Taggeld80_Ja_Karenz88_Nein()
    {
        var slip = Slip("""
        [
          { "bezeichnung": "Krankheit (Karenzentschädigung)", "betrag": 880.00 },
          { "bezeichnung": "Krankheit (Taggeld 80%)",         "betrag": 2943.55 },
          { "bezeichnung": "Unfall (Karenzentschädigung)",    "betrag": 176.00 },
          { "bezeichnung": "Unfall (Taggeld 80%)",            "betrag": 512.40 }
        ]
        """);
        var r = FibuJournalService.ExtractBruttoUmgliederung(slip, FamzNamen, KtgNamen, UvgNamen);
        Assert.Equal(2943.55m, r.KtgTaggeld);
        Assert.Equal(512.40m,  r.UvgTaggeld);
        // Karenz 88% bleibt Personalaufwand — darf in KEINER Summe stecken.
        Assert.Equal(3455.95m, r.AufwandAbzug);
    }

    [Fact]
    public void Taggeld_AltSnapshot_FallbackPrefix_OhneDbNamen()
    {
        // Alt-Snapshot / umbenannte LP: DB-Namen leer → fixe Engine-Prefixe greifen.
        var slip = Slip("""
        [ { "bezeichnung": "Krankheit (Taggeld 80%)", "betrag": 100.00 } ]
        """);
        var r = FibuJournalService.ExtractBruttoUmgliederung(slip, null, null, null);
        Assert.Equal(100.00m, r.KtgTaggeld);
    }

    [Fact]
    public void Ml13_NurSaldoAuszahlungUndNachzahlung_NichtAktMonatOderMonatlich()
    {
        var slip = Slip("""
        [
          { "bezeichnung": "13. Monatslohn (akt. Monat)",       "betrag": 300.00, "accrued": 300.00 },
          { "bezeichnung": "13. Monatslohn (Saldo-Auszahlung)", "betrag": 3002.70, "accrued": 3002.70 },
          { "bezeichnung": "13. Monatslohn",                    "betrag": 250.00, "accrued": 250.00 },
          { "bezeichnung": "13. Monatslohn (Nachzahlung nach Probezeit)", "betrag": 246.60, "accrued": 246.60 },
          { "bezeichnung": "13. ML a/Treueprämie",              "betrag": 8.33 }
        ]
        """);
        var r = FibuJournalService.ExtractBruttoUmgliederung(slip, FamzNamen, KtgNamen, UvgNamen);
        // Nur Saldo-Auszahlung + Nachzahlung nach Probezeit = RST-Abbau.
        Assert.Equal(3249.30m, r.Ml13Auszahlung);
        Assert.Equal(0m, r.Ml13Verfall);
    }

    [Fact]
    public void Ml13_Verfall_Betrag0_WertAusAccrued()
    {
        var slip = Slip("""
        [ { "bezeichnung": "13. Monatslohn (verfallen — Auflösung in Probezeit)", "betrag": 0.00, "accrued": 246.60 } ]
        """);
        var r = FibuJournalService.ExtractBruttoUmgliederung(slip, FamzNamen, KtgNamen, UvgNamen);
        Assert.Equal(246.60m, r.Ml13Verfall);
        // Verfall steckt NICHT im Brutto → darf den Aufwand-Abzug nicht erhöhen.
        Assert.Equal(0m, r.AufwandAbzug);
    }

    [Fact]
    public void MirusReferenz_Juli_Oftringen_AlleDreiThemen()
    {
        // Mirus-Referenzjournal (Juli, Oftringen): FamZ 1959.00 / KTG Crew Flex
        // 2943.55 / Auszahlung 13. ML Crew Flex 3002.70.
        var slip = Slip("""
        [
          { "bezeichnung": "Stundenlohn",                       "betrag": 4100.00 },
          { "bezeichnung": "Kinderzulage (A, 4 J.)",            "betrag": 1959.00 },
          { "bezeichnung": "Krankheit (Taggeld 80%)",           "betrag": 2943.55 },
          { "bezeichnung": "13. Monatslohn (Saldo-Auszahlung)", "betrag": 3002.70 }
        ]
        """);
        var r = FibuJournalService.ExtractBruttoUmgliederung(slip, FamzNamen, KtgNamen, UvgNamen);
        Assert.Equal(1959.00m, r.Famz);
        Assert.Equal(2943.55m, r.KtgTaggeld);
        Assert.Equal(3002.70m, r.Ml13Auszahlung);
        Assert.Equal(7905.25m, r.AufwandAbzug);
    }

    [Fact]
    public void OhneLohnLines_OderLeerer_Slip_LiefertLeer()
    {
        using var doc = JsonDocument.Parse("{}");
        var r = FibuJournalService.ExtractBruttoUmgliederung(doc.RootElement.Clone(), FamzNamen, KtgNamen, UvgNamen);
        Assert.Equal(FibuJournalService.BruttoUmgliederung.Leer, r);

        var r2 = FibuJournalService.ExtractBruttoUmgliederung(Slip("[]"), FamzNamen, KtgNamen, UvgNamen);
        Assert.Equal(0m, r2.AufwandAbzug);
    }

    [Fact]
    public void DbUmbenannteLohnposition_MatchtUeberDbNamen()
    {
        // Walter benennt die LP um («KTG-Taggeld 80 Prozent») — der Slip trägt
        // dann den neuen Namen; das Matching läuft über die DB-Bezeichnung.
        var slip = Slip("""
        [ { "bezeichnung": "KTG-Taggeld 80 Prozent", "betrag": 400.00 } ]
        """);
        var r = FibuJournalService.ExtractBruttoUmgliederung(
            slip, FamzNamen, new[] { "KTG-Taggeld 80 Prozent" }, UvgNamen);
        Assert.Equal(400.00m, r.KtgTaggeld);
    }

    [Fact]
    public void NegativeFamz_Rueckforderung_WirdSigniertSummiert()
    {
        var slip = Slip("""
        [
          { "bezeichnung": "Kinderzulage (A)", "betrag": 200.00 },
          { "bezeichnung": "Kinderzulage (Rückforderung B)", "betrag": -150.00 }
        ]
        """);
        var r = FibuJournalService.ExtractBruttoUmgliederung(slip, FamzNamen, KtgNamen, UvgNamen);
        Assert.Equal(50.00m, r.Famz);
    }
}
