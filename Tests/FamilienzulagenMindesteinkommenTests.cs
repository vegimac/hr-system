using System.Text.RegularExpressions;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// FAK-Mindesteinkommen-Sperre auf ECHTER AHV-Basis
/// (Walter-Bug 04.08.2026: Feride Alimi, FLEX Juli 2026).
///
/// Die Familienzulagen-Sperre («Lohn zu tief») wurde früher auf einer
/// Lohn-SCHÄTZUNG entschieden (FLEX: workedHours × hourlyRate = 216.85) —
/// obwohl der echte AHV-pflichtige Lohn der Periode 1'027.93 betrug
/// (Stundenlohn 216.85 + Feiertag 4.92 + Ferienentschädigung-Auszahlung
/// 727.12 + 13. ML 79.04). Folge: Ausbildungszulage 278.00 fälschlich
/// gesperrt, Mirus zahlte sie. Seit dem Fix entscheidet die Engine mit
/// PayrollCalculations.IsFakMindesteinkommenGesperrt auf der echten
/// AHV-Basis, aufgerufen am Ende jedes Modell-Zweigs (MTP/FLEX/FIX inkl.
/// FIX-M) unmittelbar vor der SvBases-Konstruktion.
/// </summary>
public class FamilienzulagenMindesteinkommenTests
{
    // Kantonale Schwelle (LU): 7'560/Jahr ÷ 12 = 630/Mt.
    private const decimal Schwelle = 7560m / 12m;

    // ──────────────────────────────────────────────────────────────────
    // 1. Fall Feride Alimi: echte AHV-Basis 1'027.93 ≥ 630 → Zulage zahlt
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Alimi_EchteAhvBasis_UeberSchwelle_ZulageZahlt()
    {
        // Komponenten des echten AHV-pflichtigen Lohns (Juli 2026)
        decimal stundenlohn   = 216.85m;   // 10.63 h × 20.40
        decimal feiertag      =   4.92m;
        decimal ferienAusz    = 727.12m;   // Ferienentschädigung-Auszahlung aus dem Pott
        decimal dreizehnter   =  79.04m;
        decimal echteBasis    = stundenlohn + feiertag + ferienAusz + dreizehnter;

        Assert.Equal(1027.93m, echteBasis); // Rechen-Sanity: exakt wie im Bug-Report

        // Echte Basis liegt klar über 630 → NICHT gesperrt,
        // die Ausbildungszulage 278.00 muss fliessen.
        Assert.False(PayrollCalculations.IsFakMindesteinkommenGesperrt(echteBasis, Schwelle));
    }

    [Fact]
    public void Alimi_AlteSchaetzung_HaetteFaelschlichGesperrt()
    {
        // Dokumentation des Bugs: die frühere Schätzung (nur gestempelte
        // Stunden × Stundenlohn) lag unter der Schwelle — genau daran ist
        // die Sperre früher fälschlich ausgelöst worden. Der Test hält fest,
        // dass die Schätzung und die echte Basis unterschiedlich entscheiden.
        decimal alteSchaetzung = 10.63m * 20.40m;   // = 216.852

        Assert.True(PayrollCalculations.IsFakMindesteinkommenGesperrt(alteSchaetzung, Schwelle));
        Assert.False(PayrollCalculations.IsFakMindesteinkommenGesperrt(1027.93m, Schwelle));
    }

    // ──────────────────────────────────────────────────────────────────
    // 2. Fall echt-zu-tief: Basis < Schwelle → gesperrt («Lohn zu tief»)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void EchtZuTief_BasisUnterSchwelle_Gesperrt()
    {
        // MA mit wirklich zu tiefem AHV-Lohn (z.B. 2 h × 20.40 = 40.80,
        // keine Ferien-Auszahlung, kein 13. ML) → Sperre greift weiterhin,
        // die 0.00-Zeile «– Lohn zu tief» erscheint.
        Assert.True(PayrollCalculations.IsFakMindesteinkommenGesperrt(40.80m, Schwelle));
        Assert.True(PayrollCalculations.IsFakMindesteinkommenGesperrt(629.99m, Schwelle));
    }

    [Fact]
    public void Grenzfall_BasisGenauAufSchwelle_NichtGesperrt()
    {
        // Wie bisher strikt «<»: exakt auf der Schwelle = Anspruch besteht.
        Assert.False(PayrollCalculations.IsFakMindesteinkommenGesperrt(Schwelle, Schwelle));
    }

    [Fact]
    public void OhneTarifSchwelle_NieGesperrt()
    {
        // Kein FAK-Tarif / keine Schwelle hinterlegt → keine Sperre
        // (entspricht dem bisherigen Verhalten: threshold == null).
        Assert.False(PayrollCalculations.IsFakMindesteinkommenGesperrt(0m, null));
    }

    // ──────────────────────────────────────────────────────────────────
    // 3. Spec-Audit (Muster WorkflowSpecAuditTests): die Engine darf die
    //    Schätzung nicht wieder einführen und muss die Sperre in ALLEN
    //    drei Modell-Zweigen VOR der SvBases-Konstruktion entscheiden.
    // ──────────────────────────────────────────────────────────────────

    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
                dir = Directory.GetParent(dir)?.FullName;
            if (dir is null) throw new InvalidOperationException("hr-system.csproj nicht gefunden — Test muss im Tests-Subdir laufen.");
            return dir;
        }
    }

    private static string EngineSrc =>
        File.ReadAllText(Path.Combine(RepoRoot, "Services", "PayrollCalculationEngine.cs"));

    [Fact]
    public void Engine_SchaetzVariable_IstEntfernt()
    {
        // Die Variable darf nur noch in Kommentaren vorkommen — keine
        // Deklaration/Zuweisung mehr (Regression-Schutz gegen Wiedereinbau).
        Assert.DoesNotMatch(
            new Regex(@"decimal\s+estimatedAhvBruttoForFak"),
            EngineSrc);
        Assert.DoesNotMatch(
            new Regex(@"estimatedAhvBruttoForFak\s*="),
            EngineSrc);
    }

    [Theory]
    [InlineData("mainLohnMtp", "dreizehnterMtp", "svBasesMtp")]   // MTP
    [InlineData("mainLohnUtp", "dreizehnterUtp", "svBasesUtp")]   // FLEX (UTP)
    [InlineData("mainLohnFix", "dreizehnterFix", "svBasesFix")]   // FIX + FIX-M
    public void Engine_SperreLaeuft_InJedemModellZweig_VorSvBases(
        string mainLohn, string dreizehnter, string svBasesVar)
    {
        var src = EngineSrc;

        // Aufruf auf der echten AHV-Basis (identisch zur SvBases-Ahv-Zeile)
        var call = $"ApplyFamilienzulagenSperre({mainLohn} + deltaAhv + {dreizehnter})";
        int callIdx = src.IndexOf(call, StringComparison.Ordinal);
        Assert.True(callIdx >= 0,
            $"Aufruf «{call}» nicht gefunden — FAK-Sperre fehlt im Modell-Zweig.");

        // ... und zwar VOR der SvBases-Konstruktion (sonst würde die QST-Basis
        // gesperrte FamZ noch enthalten).
        int svIdx = src.IndexOf($"var {svBasesVar} = new SvBases(", StringComparison.Ordinal);
        Assert.True(svIdx >= 0, $"SvBases-Konstruktion «{svBasesVar}» nicht gefunden.");
        Assert.True(callIdx < svIdx,
            $"ApplyFamilienzulagenSperre muss VOR «var {svBasesVar} = new SvBases(...)» laufen.");
    }

    [Fact]
    public void Engine_SperrEntscheid_NutztPureHelper()
    {
        // Die Entscheidung selbst liegt als reine Funktion in
        // PayrollCalculations (unit-testbare Schicht, siehe Tests oben).
        Assert.Contains("PayrollCalculations.IsFakMindesteinkommenGesperrt(", EngineSrc);
    }
}
