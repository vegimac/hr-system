using HrSystem.Models;
using HrSystem.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// QST-Jahresmodell GE/FR/VD/VS/TI (Walter 19.09.2026, O1).
/// TF22 Bucher Jan–Mai gegen tar25ti; Juni-XML −2039.50 nicht nachbauen.
/// </summary>
public class QstJahresmodellTests
{
    [Fact]
    public void Rechne_Januar_SatzLohnIstYtd()
    {
        var r = QstJahresmodell.Rechne(5111.00m, 0m, 1, 8.70m);
        Assert.Equal(5111.00m, r.SatzLohn);
        Assert.Equal(8.70m, r.SatzPct);
        Assert.Equal(444.66m, r.Jahressteuer);
        Assert.Equal(444.66m, r.QstMonat);
    }

    [Fact]
    public void Rechne_Negativ_BleibtRueckerstattung()
    {
        var r = QstJahresmodell.Rechne(1000m, 1500m, 2, 0m);
        Assert.Equal(-1500.00m, r.QstMonat);
    }

    [Fact]
    public void AnzahlMonate_EintrittMaerz_JuniIst4()
    {
        var n = QstJahresmodell.AnzahlMonate(new DateOnly(2025, 3, 1), new DateOnly(2025, 6, 1));
        Assert.Equal(4, n);
    }

    [Fact]
    public void KantonBeginn_TarifwechselGleicherKanton_StartetNichtNeu()
    {
        var a = new EmployeeQuellensteuer
        {
            Id = 1, Steuerkanton = "TI", TarifCode = "A", ValidFrom = new DateOnly(2025, 1, 1)
        };
        var c = new EmployeeQuellensteuer
        {
            Id = 2, Steuerkanton = "TI", TarifCode = "C", ValidFrom = new DateOnly(2025, 7, 1)
        };
        var begin = QstJahresmodell.KantonBeginn(new[] { a, c }, "TI", new DateOnly(2025, 7, 31));
        Assert.Equal(new DateOnly(2025, 1, 1), begin);
    }

    [Fact]
    public void KantonBeginn_WechselLuNachTi_StartetImTiMonat()
    {
        var lu = new EmployeeQuellensteuer
        {
            Id = 1, Steuerkanton = "LU", ValidFrom = new DateOnly(2025, 1, 1)
        };
        var ti = new EmployeeQuellensteuer
        {
            Id = 2, Steuerkanton = "TI", ValidFrom = new DateOnly(2025, 4, 1)
        };
        var begin = QstJahresmodell.KantonBeginn(new[] { lu, ti }, "TI", new DateOnly(2025, 4, 30));
        Assert.Equal(new DateOnly(2025, 4, 1), begin);
    }

    [Fact]
    public void LeseSlip_AbzugIstPositivBezahlt()
    {
        var json = """{"totalLohn":5111.00,"abzugLines":[{"categoryCode":"QST","betrag":-444.65,"basis":5111.00,"satzBasis":5111.00}]}""";
        var z = QstJahresmodell.LeseSlip(json);
        Assert.Equal(444.65m, z.QstBezahlt);
        Assert.Equal(5111.00m, z.IstBasis);
        Assert.Equal(5111.00m, z.SatzBasis);
    }

    [Fact]
    public void LeseSlip_GutschriftIstNegativBezahlt()
    {
        var json = """{"totalLohn":3931.55,"abzugLines":[{"categoryCode":"QST","betrag":2039.50,"basis":3931.55}]}""";
        var z = QstJahresmodell.LeseSlip(json);
        Assert.Equal(-2039.50m, z.QstBezahlt);
    }

    [Fact]
    public void Bucher_JanBisMai_InnerhalbZweiRappenDerXml()
    {
        var svc = CreateTarifService();
        var monate = new (decimal ist, decimal xml)[]
        {
            (5111.00m, 444.65m),
            (4717.85m, 371.15m),
            (4914.45m, 407.90m),
            (5307.60m, 480.65m),
            (4521.30m, 335.15m),
        };
        decimal ytd = 0, paid = 0;
        for (int i = 0; i < monate.Length; i++)
        {
            ytd += monate[i].ist;
            int n = i + 1;
            var satzLohn = PayrollCalculations.Round05(ytd / n);
            var satz = svc.GetSteuersatzProzent("TI", "A", 0, false, satzLohn, 2025)
                       ?? throw new InvalidOperationException("tar25ti A0N fehlt");
            var r = QstJahresmodell.Rechne(ytd, paid, n, satz);
            Assert.True(Math.Abs(r.QstMonat - monate[i].xml) <= 0.02m,
                $"Monat {n}: Formel {r.QstMonat} XML {monate[i].xml} Satz {satz}% auf {satzLohn}");
            paid += r.QstMonat;
        }
    }

    [Fact]
    public void Bucher_Juni_IstPlus240NichtVollrueckerstattung()
    {
        var svc = CreateTarifService();
        decimal ytd = 5111.00m + 4717.85m + 4914.45m + 5307.60m + 4521.30m;
        decimal paid = 444.66m + 371.14m + 407.89m + 480.63m + 335.14m;
        ytd += 3931.55m;
        var satzLohn = PayrollCalculations.Round05(ytd / 6);
        var satz = svc.GetSteuersatzProzent("TI", "A", 0, false, satzLohn, 2025)!.Value;
        var r = QstJahresmodell.Rechne(ytd, paid, 6, satz);
        Assert.True(r.QstMonat > 0, $"Juni muss nachladen, nicht XML −2039.50 (war {r.QstMonat})");
        Assert.InRange(r.QstMonat, 240.00m, 242.00m);
    }

    private static QuellensteuerTarifService CreateTarifService()
    {
        var env = new StubEnv(RepoRoot);
        return new QuellensteuerTarifService(env, NullLogger<QuellensteuerTarifService>.Instance);
    }

    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Assets", "Quellensteuer", "tar25ti.txt")))
                    return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Assets/Quellensteuer/tar25ti.txt nicht gefunden.");
        }
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
