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
    public void Binggeli_Januar_SatzAusNebenerwerbNichtIst()
    {
        var r = QstJahresmodell.Rechne(4550.00m, 0m, 1, 10.90m, 6500.00m);
        Assert.Equal(6500.00m, r.SatzLohn);
        Assert.Equal(495.95m, r.QstMonat);
    }

    [Fact]
    public void Andrey_Januar_Pensum50Plus40()
    {
        var r = QstJahresmodell.Rechne(2600.00m, 0m, 1, 7.90m, 4680.00m);
        Assert.Equal(4680.00m, r.SatzLohn);
        Assert.Equal(205.40m, r.QstMonat);
    }

    [Fact]
    public void MeierChristian_Januar_NebenerwerbUnbekanntAuf100()
    {
        var r = QstJahresmodell.Rechne(2000.00m, 0m, 1, 8.50m, 5000.00m);
        Assert.Equal(5000.00m, r.SatzLohn);
        Assert.Equal(170.00m, r.QstMonat);
    }

    [Fact]
    public void Forster_Januar_SatzVollerLohnNichtChTage()
    {
        Assert.Equal(8000.00m, QstJahresmodell.SatzLohn(8000.00m, 1));
        var aufIst = QstJahresmodell.Rechne(4500.00m, 0m, 1, 10.20m, 8000.00m);
        Assert.Equal(8000.00m, aufIst.SatzLohn);
        Assert.Equal(459.00m, aufIst.QstMonat);
    }

    [Fact]
    public void Koller_Februar_BonusImSatzDurch12()
    {
        var satz = QstJahresmodell.SatzLohn(10000.00m, 2, 30000.00m);
        Assert.Equal(7500.00m, satz);
        var r = QstJahresmodell.Rechne(40000.00m, 425.00m, 2, 12.30m, 10000.00m, 30000.00m);
        Assert.Equal(7500.00m, r.SatzLohn);
        Assert.Equal(4495.00m, r.QstMonat);
    }

    [Fact]
    public void MeierChristian_Februar_SonderzulagePlusHochrechnung()
    {
        var satz = QstJahresmodell.SatzLohn(10000.00m, 2, 4500.00m);
        Assert.Equal(5375.00m, satz);
        var r = QstJahresmodell.Rechne(8500.00m, 170.00m, 2, 9.20m, 10000.00m, 4500.00m);
        Assert.Equal(612.00m, r.QstMonat);
    }

    [Fact]
    public void LeseSlip_LiestAperiodisch()
    {
        var json = """{"totalLohn":35000.00,"abzugLines":[{"categoryCode":"QST","betrag":-4495.00,"basis":35000.00,"satzBasis":5000.00,"satzAperiodisch":30000.00}]}""";
        var z = QstJahresmodell.LeseSlip(json);
        Assert.Equal(5000.00m, z.SatzBasis);
        Assert.Equal(30000.00m, z.SatzAperiodisch);
        Assert.Equal(5000.00m, QstJahresmodell.SatzDesMonats(z));
        Assert.Equal(30000.00m, QstJahresmodell.AperiodischDesMonats(z));
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
