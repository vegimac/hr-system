using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Spec-Audit (Regex auf den Quellcode) — nagelt zwei Bugfixes vom 04.08.2026 fest:
///
/// 1) SnapshotRecomputeService muss beim Recompute ALLE Saldo-Felder des
///    PayrollSaldo nachziehen, nicht nur Gross/Net. Sonst liest der FOLGEMONAT
///    (prevSaldo) nach einem Vortrag-Import + Recompute veraltete Salden —
///    konkret passiert beim 906-Import: Slip zeigte Vormonat korrekt, aber
///    payroll_saldo behielt den alten 13.-ML-Saldo.
///
/// 2) FibuJournalService: RST-13-Buchungen (Position 2010) dürfen NIE blind auf
///    die erste 2010-Zeile des Kontoplans zurückfallen. Der Mirus-Kontoplan hat
///    keine 2010-Zeile für KSt 200 (Crew Flex) — der FLEX-Probezeit-Pot braucht
///    aber eine RST-Bildung. Fallback muss KSt-korrekt sein: Crew → 2017-Zeile,
///    Management/Gerant → 2016-Zeile.
/// </summary>
public class SnapshotRecomputeSaldoSpecTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
                dir = Directory.GetParent(dir)?.FullName;
            return dir ?? throw new InvalidOperationException("Repo-Root nicht gefunden");
        }
    }

    private static string Src(string relPath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relPath));

    [Fact]
    public void Recompute_zieht_alle_Saldo_Felder_nach()
    {
        var src = Src("Services/SnapshotRecomputeService.cs");

        // Alle Saldo-Felder, die ConfirmPayroll schreibt, müssen auch der
        // Recompute schreiben (Spiegelbild der srv*-Zuweisungen).
        string[] pflichtFelder =
        {
            "sal.HourSaldo",
            "sal.NachtSaldo",
            "sal.NightHoursWorked",
            "sal.FerienGeldSaldo",
            "sal.FerienTageSaldo",
            "sal.FeiertagTageSaldo",
            "sal.ThirteenthMonthMonthly",
            "sal.ThirteenthMonthAccumulated",
        };
        foreach (var feld in pflichtFelder)
            Assert.True(
                Regex.IsMatch(src, Regex.Escape(feld) + @"\s*="),
                $"SnapshotRecomputeService muss «{feld}» beim Recompute nachziehen " +
                "(Walter-Bug 04.08.2026: Folgemonat las veraltete Salden nach Vortrag-Import + Recompute).");
    }

    [Fact]
    public void Recompute_erhaelt_Ferien_Kuerzungs_Entscheid()
    {
        var src = Src("Services/SnapshotRecomputeService.cs");

        // Der GF-Entscheid Art. 329b OR steckt im alten SlipJson
        // (ferienKuerzungAngewendet) und muss beim Recompute übernommen und
        // wieder in den frischen Slip gestempelt werden — sonst dreht ein
        // Recompute eine angewendete Kürzung stillschweigend zurück.
        Assert.Contains("ferienKuerzungAngewendet", src);
        Assert.Contains("ferienKuerzungAngewendetTage", src);
        Assert.True(
            Regex.IsMatch(src, @"ferTageBase\s*-\s*vorschlagTage"),
            "Recompute muss die Ferien-Kürzung reproduzieren (ferienTageSaldoNeu − vorschlagTage).");
    }

    [Fact]
    public void Fibu_RST13_Fallback_ist_KSt_korrekt()
    {
        var src = Src("Services/FibuJournalService.cs");

        // Der gemeinsame Lookup muss existieren …
        Assert.Contains("Find13MlRst", src);
        // … und den KSt-korrekten Fallback enthalten (Crew → 2017, Mgmt → 2016).
        Assert.True(
            Regex.IsMatch(src, "Position\\s*==\\s*2010\\s*&&\\s*m\\.Gegenkonto\\s*==\\s*\"2016\""),
            "Find13MlRst braucht den Management/Gerant-Fallback auf die 2016-Zeile.");
        Assert.True(
            Regex.IsMatch(src, "Position\\s*==\\s*2010\\s*&&\\s*m\\.Gegenkonto\\s*==\\s*\"2017\""),
            "Find13MlRst braucht den Crew-Fallback auf die 2017-Zeile.");

        // RST-Bildung (Schritt 4) und Verfall (Schritt 1e) müssen BEIDE über
        // Find13MlRst laufen — kein FindByPosKst(2010, …) mehr (das fiel blind
        // auf die erste 2010-Zeile zurück, potenziell falsche Konten).
        Assert.False(
            Regex.IsMatch(src, @"FindByPosKst\(\s*2010"),
            "RST-13-Buchungen dürfen nicht mehr über FindByPosKst(2010, …) laufen — Find13MlRst verwenden.");

        // FLEX-Fallback-Zeile muss unterscheidbar beschriftet sein.
        Assert.Contains("\"RST 13. ML \" + KstName(kst)", src);
    }
}
