using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Austritts-Schlussabrechnung (Walter-Vorgabe 04.08.2026): beim LETZTEN Lohn
/// eines MA werden alle Saldi ausbezahlt bzw. verrechnet. Getestet werden die
/// reinen Rechen-Helfer in PayrollCalculations:
///   • IsLetzterLohn — Trigger (Vertragsende in Periode, kein Folgevertrag)
///   • ExitSettlementBetrag — Betrag aus den ANGEZEIGTEN (gerundeten) Werten
///   • ExitStundensatzAusMonatslohn / ExitTagessatzFix — FIX/FIX-M-Sätze
/// </summary>
public class ExitSettlementTests
{
    // ── Referenzfall Patricia Rei Rodrigues Sobreira ──────────────────────
    // FLEX, Stundenlohn 20.40, Austritt 31.07.:
    // Nacht-Vortrag 0.18 + Juli-Zuwachs 0.13 = 0.31 h → 0.31 × 20.40 = 6.32
    // (Mirus-Referenz: 6.30 — Differenz aus Mirus-interner Rundung).
    [Fact]
    public void NachtSaldoAuszahlung_PatriciaReferenzfall()
    {
        decimal nachtSaldo = 0.18m + 0.13m;   // 0.31 h
        decimal betrag = PayrollCalculations.ExitSettlementBetrag(nachtSaldo, 20.40m);
        Assert.Equal(6.32m, betrag);
    }

    [Fact]
    public void ExitSettlementBetrag_RechnetMitAngezeigtenWerten()
    {
        // anzahl wird auf 2 Dez. gerundet BEVOR multipliziert wird:
        // 0.314 → 0.31 × 20.40 = 6.324 → 6.32 (nicht 0.314 × 20.40 = 6.4056 → 6.41)
        Assert.Equal(6.32m, PayrollCalculations.ExitSettlementBetrag(0.314m, 20.40m));

        // satz wird ebenfalls auf 2 Dez. gerundet (Anzeige-Basis):
        // Tagessatz 147.9452… → 147.95; 2 Tage × 147.95 = 295.90
        Assert.Equal(295.90m, PayrollCalculations.ExitSettlementBetrag(2m, 147.9452m));
    }

    [Fact]
    public void ExitSettlementBetrag_NegativeAnzahl_GibtNegativenBetrag()
    {
        // Verrechnung Minusstunden: −8.5 h × 24.66 = −209.61
        Assert.Equal(-209.61m, PayrollCalculations.ExitSettlementBetrag(-8.5m, 24.66m));
        // Verrechnung Ferien-Vorbezug: −2 Tage × Tagessatz 147.95 = −295.90
        Assert.Equal(-295.90m, PayrollCalculations.ExitSettlementBetrag(-2m, 147.9452m));
    }

    [Fact]
    public void ExitSettlementBetrag_NullSaldo_GibtNull()
    {
        Assert.Equal(0m, PayrollCalculations.ExitSettlementBetrag(0m, 20.40m));
        Assert.Equal(0m, PayrollCalculations.ExitSettlementBetrag(5m, 0m));
    }

    // ── FIX/FIX-M: Stundensatz aus Monatslohn ─────────────────────────────
    // Stundensatz = Monatslohn × 12 / 365 ÷ (WoStd / 7)
    [Fact]
    public void ExitStundensatzAusMonatslohn_Fix()
    {
        // 4'500 × 12 / 365 = 147.9452… Tagessatz; ÷ (42/7 = 6) = 24.6575…
        decimal satz = PayrollCalculations.ExitStundensatzAusMonatslohn(4500m, 42m);
        Assert.Equal(24.6575m, Math.Round(satz, 4));

        // Zeitsaldo-Auszahlung damit: 10 h × 24.66 (Anzeige) = 246.60
        Assert.Equal(246.60m, PayrollCalculations.ExitSettlementBetrag(10m, satz));
    }

    [Fact]
    public void ExitStundensatzAusMonatslohn_OhneWochenstunden_GibtNull()
    {
        Assert.Equal(0m, PayrollCalculations.ExitStundensatzAusMonatslohn(4500m, 0m));
    }

    [Fact]
    public void ExitTagessatzFix_Kalenderbasis()
    {
        // Monatslohn × 12 / 365 — identisch zu fixTagessatz in der Engine
        Assert.Equal(147.95m, Math.Round(PayrollCalculations.ExitTagessatzFix(4500m), 2));
        // Ferien-Tage-Auszahlung: 3 Tage × 147.95 = 443.85 (Anzeige-Rechnung)
        Assert.Equal(443.85m, PayrollCalculations.ExitSettlementBetrag(
            3m, PayrollCalculations.ExitTagessatzFix(4500m)));
    }

    // ── Trigger: IsLetzterLohn ────────────────────────────────────────────
    private static readonly DateOnly Jul1  = new(2026, 7, 1);
    private static readonly DateOnly Jul31 = new(2026, 7, 31);

    [Fact]
    public void IsLetzterLohn_EndeInPeriode_OhneFolgevertrag_True()
    {
        bool result = PayrollCalculations.IsLetzterLohn(
            new DateOnly(2026, 7, 31), Jul1, Jul31,
            andereVertragsStarts: Array.Empty<DateOnly>());
        Assert.True(result);
    }

    [Fact]
    public void IsLetzterLohn_EndeMitteMonat_True()
    {
        bool result = PayrollCalculations.IsLetzterLohn(
            new DateOnly(2026, 7, 15), Jul1, Jul31,
            andereVertragsStarts: Array.Empty<DateOnly>());
        Assert.True(result);
    }

    [Fact]
    public void IsLetzterLohn_MitFolgevertrag_False()
    {
        // Folgevertrag ab 1.8. (auch in anderer Filiale) → kein letzter Lohn
        bool result = PayrollCalculations.IsLetzterLohn(
            new DateOnly(2026, 7, 31), Jul1, Jul31,
            andereVertragsStarts: new[] { new DateOnly(2026, 8, 1) });
        Assert.False(result);
    }

    [Fact]
    public void IsLetzterLohn_AeltererParallelVertrag_ZaehltNichtAlsFolgevertrag()
    {
        // Vertrag mit Beginn VOR dem Vertragsende (z.B. Vorgänger-Vertrag)
        // ist kein Folgevertrag → letzter Lohn.
        bool result = PayrollCalculations.IsLetzterLohn(
            new DateOnly(2026, 7, 31), Jul1, Jul31,
            andereVertragsStarts: new[] { new DateOnly(2025, 1, 1) });
        Assert.True(result);
    }

    [Fact]
    public void IsLetzterLohn_OhneVertragsende_False()
    {
        bool result = PayrollCalculations.IsLetzterLohn(
            null, Jul1, Jul31,
            andereVertragsStarts: Array.Empty<DateOnly>());
        Assert.False(result);
    }

    [Fact]
    public void IsLetzterLohn_EndeAusserhalbPeriode_False()
    {
        // Ende erst im Folgemonat → dieser Lauf ist nicht der letzte
        Assert.False(PayrollCalculations.IsLetzterLohn(
            new DateOnly(2026, 8, 31), Jul1, Jul31, Array.Empty<DateOnly>()));
        // Ende lag im Vormonat → Vertrag ist in dieser Periode gar nicht gültig
        Assert.False(PayrollCalculations.IsLetzterLohn(
            new DateOnly(2026, 6, 30), Jul1, Jul31, Array.Empty<DateOnly>()));
    }
}

/// <summary>
/// 13. Monatslohn auf Saldo-AUSZAHLUNGEN (Walter-Vorgabe 04.08.2026):
/// Ferien-Geld wird auf der GUTSCHRIFT bewusst OHNE 13. ML akkumuliert —
/// der 13. gehört erst auf die AUSZAHLUNG (Ferienbezug aus dem Pott bzw.
/// Austritts-Schlussabrechnung, inkl. Nacht-Saldo-Auszahlung). Getestet wird
/// der reine Basis-Helfer ThirteenthBasisMitAuszahlungen plus das
/// Probezeit-Routing (ResolveThirteenthProbationStatus): bei Verfall kein
/// 13. auch auf den Auszahlungen, in der Probezeit fliesst die
/// Auszahlungs-Basis in die Rückstellung.
/// </summary>
public class ThirteenthAufAuszahlungenTests
{
    private const decimal Pct = 8.33m;

    private static decimal Accrual(decimal basis)
        => Math.Round(basis * Pct / 100m, 2);

    // ── Referenzfall Patricia Rei Rodrigues Sobreira ──────────────────────
    // FLEX, 580062, Juli 2026, Austritt 31.07., Probezeit längst vorbei.
    // Alt:  Basis 2'256.55 (Stundenlohn 2'206.46 + Feiertag 50.09) → 187.97
    // Neu:  + Ferien-Auszahlung 434.62 + Nacht-Auszahlung 6.32
    //       = 2'697.49 → × 8.33 % = 224.70 (Mirus 224.85 — ±0.2 ok).
    [Fact]
    public void Basis13_PatriciaReferenzfall_MitAuszahlungen()
    {
        decimal flagBasis  = 2206.46m + 50.09m;          // 2'256.55
        decimal auszahlungen = 434.62m + 6.32m;          // Ferien + Nacht

        decimal basisNeu = PayrollCalculations.ThirteenthBasisMitAuszahlungen(
            flagBasis, auszahlungen);
        Assert.Equal(2697.49m, basisNeu);

        // Ohne Auszahlungen (alter Stand): 187.97
        Assert.Equal(187.97m, Accrual(flagBasis));
        // Mit Auszahlungen: 224.70 — innerhalb ±0.2 der Mirus-Referenz 224.85
        decimal ml13 = Accrual(basisNeu);
        Assert.Equal(224.70m, ml13);
        Assert.True(Math.Abs(ml13 - 224.85m) <= 0.2m,
            $"13. ML {ml13} weicht mehr als 0.2 von Mirus 224.85 ab");
    }

    [Fact]
    public void Basis13_OhneAuszahlungen_Unveraendert()
    {
        Assert.Equal(2256.55m,
            PayrollCalculations.ThirteenthBasisMitAuszahlungen(2256.55m, 0m));
    }

    [Fact]
    public void Basis13_NurPottFerienbezug_OhneAustritt()
    {
        // Regulärer Ferienbezug aus dem Pott (kein Austritt): die Pott-
        // Auszahlung trägt keinen Lohnpositions-Code → fliesst explizit in
        // die Basis. 8.33 % darauf.
        decimal basis = PayrollCalculations.ThirteenthBasisMitAuszahlungen(
            2000m, 600m);
        Assert.Equal(2600m, basis);
        Assert.Equal(216.58m, Accrual(basis));   // 2'600 × 8.33 %
    }

    // ── Probezeit-Routing (sticht die Auszahlungs-Verzinsung) ─────────────
    private static readonly DateOnly Jul1  = new(2026, 7, 1);
    private static readonly DateOnly Jul31 = new(2026, 7, 31);

    [Fact]
    public void Austritt_NachProbezeit_NormaleAuszahlung()
    {
        // Patricia: Probezeit längst vorbei → weder InProbation noch
        // Forfeited → 13. wird ausbezahlt, inkl. Anteil auf den Auszahlungen.
        var (inProbation, forfeited) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2025, 10, 31),
            austritt:     new DateOnly(2026, 7, 31),
            periodFrom: Jul1, periodToFull: Jul31);
        Assert.False(inProbation);
        Assert.False(forfeited);
    }

    [Fact]
    public void Austritt_InProbezeit_Verfall_Auch_Auf_Auszahlungen()
    {
        // Austritt 31.07. ≤ Probezeitende 30.09. → Verfall. Die Engine rechnet
        // dann AUCH auf Nacht-/Ferien-Geld-Auszahlung keinen 13. — die
        // erweiterte Basis läuft nur in die Verfall-Anzeige (betrag = 0).
        var (inProbation, forfeited) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2026, 9, 30),
            austritt:     new DateOnly(2026, 7, 31),
            periodFrom: Jul1, periodToFull: Jul31);
        Assert.False(inProbation);
        Assert.True(forfeited);

        // Verfall-Betrag (Anzeige, accrued): Vormonats-Saldo + Zuwachs auf
        // der erweiterten Basis — ausbezahlt wird davon nichts.
        decimal basis = PayrollCalculations.ThirteenthBasisMitAuszahlungen(
            2256.55m, 440.94m);
        decimal forfeitedAmt = Math.Round(150m + basis * Pct / 100m, 2);
        Assert.Equal(374.70m, forfeitedAmt);      // 150.00 + 224.70
    }

    [Fact]
    public void Probezeit_OhneAustritt_AuszahlungsBasisInRueckstellung()
    {
        // FLEX in Probezeit, regulärer Ferienbezug aus dem Pott (kein
        // Austritt): kein Verfall, aber Rückstellung — die Auszahlungs-Basis
        // fliesst in den 13.-ML-Saldo statt in die Auszahlung.
        var (inProbation, forfeited) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2026, 9, 30),
            austritt:     null,
            periodFrom: Jul1, periodToFull: Jul31);
        Assert.True(inProbation);
        Assert.False(forfeited);

        // Saldo-Zuwachs (wie PayrollCalculations-Saldo: Basis13ml × Pct):
        decimal basis = PayrollCalculations.ThirteenthBasisMitAuszahlungen(
            2000m, 600m);
        Assert.Equal(216.58m, Math.Round(basis * Pct / 100m, 2));
    }
}
