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
        Assert.Equal(444.65m, r.Jahressteuer);
        Assert.Equal(444.65m, r.QstMonat);
    }

    [Fact]
    public void Rechne_MonatsabzugNichtAuf5RpRunden()
    {
        var r = QstJahresmodell.Rechne(5111.00m, 100.01m, 1, 8.70m);
        Assert.Equal(444.65m, r.Jahressteuer);
        Assert.Equal(344.64m, r.QstMonat);
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
    public void QstTage_EintrittZehnter_FebruarIst21()
        => Assert.Equal(21, QstJahresmodell.QstTageDesMonats(
            2025, 2, new DateOnly(2025, 2, 10), null));

    [Fact]
    public void QstTage_AustrittFuenfter_MaerzIst15()
        => Assert.Equal(15, QstJahresmodell.QstTageDesMonats(
            2025, 3, new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 15)));

    [Fact]
    public void SatzLohnAusTagen_GanzerMonatGleichAnzahlMonate()
    {
        Assert.Equal(
            QstJahresmodell.SatzLohn(10000m, 2, 30000m),
            QstJahresmodell.SatzLohnAusTagen(10000m, 60, 30000m));
    }

    [Fact]
    public void Bucher_Juli_ZweiToepfeNichtGanzesJahrAufC()
    {
        var svc = CreateTarifService();
        decimal satzLohn = 4858.25m;
        var aPct = svc.GetSteuersatzProzent("TI", "A", 0, false, satzLohn, 2025)!.Value;
        var cPct = svc.GetSteuersatzProzent("TI", "C", 0, false, satzLohn, 2025)!.Value;
        var toepfe = new Dictionary<string, decimal>
        {
            ["A0N"] = 28503.75m,
            ["C0N"] = 5504.15m,
        };
        decimal paid = 444.65m + 371.15m + 407.90m + 480.65m + 335.15m + 240.80m;
        var t = QstJahresmodell.RechneToepfe(satzLohn, toepfe, c => c == "C0N" ? cPct : aPct, paid);
        Assert.InRange(t.QstMonat, 509.20m, 509.40m);
        var einTopf = QstJahresmodell.Rechne(28503.75m + 5504.15m, paid, 7, cPct);
        Assert.True(Math.Abs(einTopf.QstMonat - 509.30m) > 1m, "Ein Topf C aufs ganze Jahr darf nicht 509.30 treffen");
    }

    [Fact]
    public void LeseSlip_LiestCodeUndAperiodisch()
    {
        var json = """{"totalLohn":35000.00,"abzugLines":[{"categoryCode":"QST","bezeichnung":"Quellensteuer A0N TI","betrag":-4495.00,"basis":35000.00,"satzBasis":5000.00,"satzAperiodisch":30000.00,"qstCode":"A0N"}]}""";
        var z = QstJahresmodell.LeseSlip(json);
        Assert.Equal(5000.00m, z.SatzBasis);
        Assert.Equal(30000.00m, z.SatzAperiodisch);
        Assert.Equal("A0N", z.TarifCode);
        Assert.Equal(5000.00m, QstJahresmodell.SatzDesMonats(z));
        Assert.Equal(30000.00m, QstJahresmodell.AperiodischDesMonats(z));
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
        decimal paid = 444.65m + 371.15m + 407.90m + 480.65m + 335.15m;
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

    [Fact]
    public void TopfCode_RueckwirkendeVersion_WandertAbErfahrenMonatInNeuenTopf()
    {
        // TF31 Bolletto: A0N ab 1.1., B0N gültig ab 1.4., erfahren am 15.6.
        var versionen = new List<EmployeeQuellensteuer>
        {
            new() { Id = 1, Steuerkanton = "VD", TarifCode = "A", AnzahlKinder = 0, Kirchensteuer = false, ValidFrom = new DateOnly(2025, 1, 1) },
            new() { Id = 2, Steuerkanton = "VD", TarifCode = "B", AnzahlKinder = 0, Kirchensteuer = false, ValidFrom = new DateOnly(2025, 4, 1), ErfahrenAm = new DateOnly(2025, 6, 15) },
        };
        // Mai-Lohnlauf (Wissen bis 31.5.): April noch A0N.
        Assert.Equal("A0N", QstJahresmodell.TopfCodeFuerMonat(versionen, 2025, 4, new DateOnly(2025, 5, 31), "VD"));
        // Juni-Lohnlauf (Wissen bis 30.6.): April und Mai im B0N-Topf, März bleibt A0N.
        Assert.Equal("B0N", QstJahresmodell.TopfCodeFuerMonat(versionen, 2025, 4, new DateOnly(2025, 6, 30), "VD"));
        Assert.Equal("B0N", QstJahresmodell.TopfCodeFuerMonat(versionen, 2025, 5, new DateOnly(2025, 6, 30), "VD"));
        Assert.Equal("A0N", QstJahresmodell.TopfCodeFuerMonat(versionen, 2025, 3, new DateOnly(2025, 6, 30), "VD"));
        // Anderer Kanton → nicht Teil der Kette.
        Assert.Null(QstJahresmodell.TopfCodeFuerMonat(versionen, 2025, 4, new DateOnly(2025, 6, 30), "TI"));
    }

    [Fact]
    public void Bolletto_Dezember_MitUmgebuchtenToepfenUndK1AlsBezahlt()
    {
        // Anhang 1 Y23/Y40 + RefXML TF31: A0N Jan–Mär 15'000, B0N Apr–Dez 50'000 (inkl. 13. ML),
        // Satz-Lohn Dezember 5'416.65. Bezahlt = 3×419 + 2×419 + Juni (117 + K1 −604) + 5×117.
        var svc = CreateTarifService();
        decimal satzLohn = 5416.65m;
        var aPct = svc.GetSteuersatzProzent("VD", "A", 0, false, satzLohn, 2025) ?? throw new InvalidOperationException("tar25vd A0N fehlt");
        var bPct = svc.GetSteuersatzProzent("VD", "B", 0, false, satzLohn, 2025) ?? throw new InvalidOperationException("tar25vd B0N fehlt");
        var toepfe = new Dictionary<string, decimal> { ["A0N"] = 15000m, ["B0N"] = 50000m };
        decimal bezahlt = 3 * 419m + 2 * 419m + 117m + (-604m) + 5 * 117m;
        var t = QstJahresmodell.RechneToepfe(satzLohn, toepfe, c => c == "B0N" ? bPct : aPct, bezahlt);
        Assert.Equal(1035.00m, t.QstMonat);
        // Ohne Umbuchung (April/Mai bleiben im A-Topf, Posten nicht bezahlt) läge Dezember bei 1'062 — falsch.
        var falsch = QstJahresmodell.RechneToepfe(satzLohn,
            new Dictionary<string, decimal> { ["A0N"] = 25000m, ["B0N"] = 40000m },
            c => c == "B0N" ? bPct : aPct, 5 * 419m + 6 * 117m);
        Assert.Equal(1062.00m, falsch.QstMonat);
    }

    [Fact]
    public void Y11_WiedereintrittSetztStartNichtZurueck_LueckenmonateNullTage()
    {
        // Anhang 1 Y1.1 / TF41: Vertrag 1.1.–31.3., Wiedereintritt 1.7. → Start bleibt Januar,
        // April–Juni 0 QST-Tage, kumuliert 90 → 90 → 120 …
        var vertraege = new List<QstJahresmodell.Zeitraum>
        {
            new(new DateOnly(2025, 1, 1), new DateOnly(2025, 3, 31)),
            new(new DateOnly(2025, 7, 1), null),
        };
        Assert.Equal(new DateOnly(2025, 1, 1), QstJahresmodell.ErsterEintrittImJahr(vertraege, 2025));
        Assert.Equal(30, QstJahresmodell.QstTageDesMonats(2025, 3, vertraege));
        Assert.Equal(0, QstJahresmodell.QstTageDesMonats(2025, 4, vertraege));
        Assert.Equal(0, QstJahresmodell.QstTageDesMonats(2025, 6, vertraege));
        Assert.Equal(30, QstJahresmodell.QstTageDesMonats(2025, 7, vertraege));
        Assert.Equal(90, QstJahresmodell.QstTageKumuliertAusVertraegen(2025, 1, 6, vertraege));
    }

    [Fact]
    public void QstTage_EintrittUndAustrittTagesgenau_MonatsendeIst30()
    {
        // Y31: Eintritt 10.2. → 21 Tage; Austritt 15.6. → 15 Tage; Austritt 28.2. = Monatsende = 30.
        var v1 = new List<QstJahresmodell.Zeitraum> { new(new DateOnly(2025, 2, 10), new DateOnly(2025, 6, 15)) };
        Assert.Equal(21, QstJahresmodell.QstTageDesMonats(2025, 2, v1));
        Assert.Equal(30, QstJahresmodell.QstTageDesMonats(2025, 3, v1));
        Assert.Equal(15, QstJahresmodell.QstTageDesMonats(2025, 6, v1));
        Assert.Equal(0, QstJahresmodell.QstTageDesMonats(2025, 7, v1));
        var v2 = new List<QstJahresmodell.Zeitraum> { new(new DateOnly(2025, 1, 1), new DateOnly(2025, 2, 28)) };
        Assert.Equal(30, QstJahresmodell.QstTageDesMonats(2025, 2, v2));
    }

    [Fact]
    public void Y31_SatzLohnImEintrittsmonat_NurUeberQstTage()
    {
        // Jenzer/Lehmann Feb: 8'400 ÷ 21 × 360 ÷ 12 = 12'000 — ohne zusätzliche Kurzmonat-Hochrechnung.
        Assert.Equal(12000.00m, QstJahresmodell.SatzLohnAusTagen(8400m, 21));
        // Swissdec-Lohn 8'000 (A7) → 11'428.55 wie RefXML.
        Assert.Equal(11428.55m, QstJahresmodell.SatzLohnAusTagen(8000m, 21));
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
