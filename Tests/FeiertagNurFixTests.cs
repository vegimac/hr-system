using HrSystem.Controllers;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter-Vorgabe 05.10.2026: MTP und FLEX haben keinen Feiertag zugut
/// (Entschädigung als % pro Stunde). Erfassen ist nur bei FIX/FIX-M erlaubt;
/// ein importierter Feiertag wirkt bei MTP/FLEX weder auf Zeit noch Geld.
/// </summary>
public class FeiertagNurFixTests
{
    [Theory]
    [InlineData(new[] { "FIX" }, true)]
    [InlineData(new[] { "FIX-M" }, true)]
    [InlineData(new[] { "MTP", "FIX" }, true)]
    [InlineData(new[] { "MTP" }, false)]
    [InlineData(new[] { "FLEX" }, false)]
    [InlineData(new[] { "MTP", "FLEX" }, false)]
    [InlineData(new string[0], true)]
    public void Erfassen_NurWennFixVertragImZeitraum(string[] modelle, bool erlaubt)
        => Assert.Equal(erlaubt, AbsencesController.FeiertagErfassbar(modelle));

    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
                dir = Directory.GetParent(dir)?.FullName;
            return dir ?? throw new InvalidOperationException("hr-system.csproj nicht gefunden.");
        }
    }

    [Fact]
    public void Engine_FeiertagFolgtDemKatalog_KeineFesteAuszahlung()
    {
        // Wirkung kommt aus dem Katalog (wirkung_mtp/flex = KEINE); kein fest
        // verdrahteter Zweig «Feiertag → 50.1» mehr, der den Katalog übergeht.
        var src = File.ReadAllText(Path.Combine(RepoRoot, "Services/PayrollCalculationEngine.cs"));
        Assert.DoesNotContain("feiertagStunden += hours", src);
    }

    [Fact]
    public void Engine_TageSaldiAusDemKatalog()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "Services/PayrollCalculationEngine.cs"));
        Assert.Contains("GetAbsenzTyp(x.AbsenceType).ReduziertSaldo == \"FERIEN_TAGE\"", src);
        Assert.Contains("GetAbsenzTyp(x.AbsenceType).ReduziertSaldo == \"FEIERTAG_TAGE\"", src);
        var prog = File.ReadAllText(Path.Combine(RepoRoot, "Program.cs"));
        Assert.Matches(@"SET reduziert_saldo = 'FEIERTAG_TAGE'\s+WHERE code = 'FEIERTAG' AND reduziert_saldo IS NULL", prog);
        Assert.Matches(@"SET reduziert_saldo = 'FERIEN_TAGE'\s+WHERE code = 'FERIEN' AND reduziert_saldo IS NULL", prog);
    }

    [Fact]
    public void Katalog_FeiertagMtpFlexNeutral()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "Program.cs"));
        Assert.Matches(@"UPDATE absenz_typ SET wirkung_mtp = 'KEINE'\s+WHERE code = 'FEIERTAG'", src);
        Assert.Matches(@"UPDATE absenz_typ SET wirkung_flex = 'KEINE'\s+WHERE code = 'FEIERTAG'", src);
    }

    [Fact]
    public void Controller_PrueftBeiAnlegenUndTypwechsel()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot, "Controllers/AbsencesController.cs"));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(src, @"await CheckFeiertagNurFixAsync\(").Count);
        Assert.Contains("FEIERTAG_NUR_FIX", src);
    }
}
