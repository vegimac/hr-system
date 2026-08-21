using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Spec-Audit für die QST-Kurzmonats-Satzbestimmung (Walter-Vorgabe
/// 21.08.2026, KS 45 Monatsmodell): bei untermonatigem Ein-/Austritt eines
/// Monatslöhners (FIX/FIX-M/MTP) wird der IST-Betrag besteuert, aber zum
/// SATZ des auf den vollen Monat hochgerechneten PERIODISCHEN Lohns —
/// aperiodische Bestandteile (13. ML, Schlussabrechnung, Zulagen) zählen
/// satzbestimmend OHNE Hochrechnung. FLEX (Stundenlohn) bleibt bewusst auf
/// IST-Basis (Variante A).
///
/// Regex-Scan der Engine analog WorkflowSpecAuditTests: fällt der
/// Kurzmonats-Block einem Refactor zum Opfer, schlägt der Test fehl.
/// </summary>
public class QstKurzmonatSpecAuditTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
                dir = Directory.GetParent(dir)?.FullName;
            if (dir is null) throw new InvalidOperationException("hr-system.csproj nicht gefunden.");
            return dir;
        }
    }

    private static string Engine =>
        File.ReadAllText(Path.Combine(RepoRoot, "Services", "PayrollCalculationEngine.cs"));

    [Fact]
    public void Fix_Kurzmonat_ErsetztKurzlohnDurchVollenMonatslohn()
    {
        // svBasesFix.Qst - monthSalary + monthSalaryFull → Satzbasis voller Monat.
        Assert.Contains("svBasesFix.Qst - monthSalary + monthSalaryFull", Engine);
        Assert.Contains("isShortPeriod && monthSalaryFull > 0 && monthSalaryFull > monthSalary", Engine);
    }

    [Fact]
    public void Mtp_Kurzmonat_AddiertFehlendeFestlohnDifferenz()
    {
        // guaranteedH / 7 × (normalPeriodDays − shortPeriodDays) × hourlyRate.
        Assert.Contains("(normalPeriodDays - shortPeriodDays) * hourlyRate", Engine);
        Assert.Contains("svBasesMtp.Qst + mtpFestDiff", Engine);
    }

    [Fact]
    public void Satzbasis_UebersteuertNie_NachUnten()
    {
        // ComputeQstDeduction: satzbestimmend darf nie UNTER den IST-Brutto
        // fallen (Schutzklausel bleibt bestehen).
        Assert.Contains("if (satzBrutto < bruttolohn) satzBrutto = bruttolohn;", Engine);
    }
}
