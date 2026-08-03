using HrSystem.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// ESTV-Tarifdatei liefert pro Stufe Mindeststeuer (Pos 46–54) + %-Satz (55–59).
/// Regel 4.4: max(IST × Satz, Mindeststeuer). Kein Hardcode pro Kanton.
/// </summary>
public class QuellensteuerTarifMindeststeuerTests
{
    private static QuellensteuerTarifService CreateService()
    {
        var env = new StubEnv(FindRepoRoot());
        return new QuellensteuerTarifService(env, NullLogger<QuellensteuerTarifService>.Instance);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Assets", "Quellensteuer", "tar26ag.txt")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException(
            "Repo-Root mit Assets/Quellensteuer/tar26ag.txt nicht gefunden.");
    }

    [Fact]
    public void Ag_C2N_TieferBrutto_NimmtMindeststeuerAusTabelle()
    {
        var svc = CreateService();
        var r = svc.Berechne("AG", "C", 2, false, 1369.25m, 1369.25m, jahr: 2026);
        Assert.NotNull(r);
        Assert.Equal(0m, r!.SteuersatzPct);
        Assert.Equal(2.00m, r.MindeststeuerCHF);
        Assert.True(r.MindeststeuerAngewendet);
        Assert.Equal(2.00m, r.SteuerbetragCHF);
    }

    [Fact]
    public void Lu_A0N_TieferBrutto_NimmtMindeststeuerAusTabelle()
    {
        var svc = CreateService();
        // Stufe mit 0%: Betrag muss LU-Mindeststeuer 13.00 aus der Datei sein
        var r = svc.Berechne("LU", "A", 0, false, 50m, 50m, jahr: 2026);
        Assert.NotNull(r);
        Assert.Equal(13.00m, r!.MindeststeuerCHF);
        Assert.True(r.MindeststeuerAngewendet);
        Assert.Equal(13.00m, r.SteuerbetragCHF);
    }

    [Fact]
    public void GetSteuerBetrag_Entspricht_Berechne()
    {
        var svc = CreateService();
        var betrag = svc.GetSteuerBetrag("AG", "C", 2, false, 1369.25m, jahr: 2026);
        Assert.Equal(2.00m, betrag);
    }

    private sealed class StubEnv : IWebHostEnvironment
    {
        public StubEnv(string contentRoot) => ContentRootPath = contentRoot;
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
