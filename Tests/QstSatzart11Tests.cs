using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// ESTV Satzart 11 (Walter 12.09.2026). MEY aus der Datei, nicht aus dem Code.
/// </summary>
public class QstSatzart11Tests
{
    [Fact]
    public void Parse_BeHey_23()
    {
        var z = QstSatzart11.ParseZeile(
            "1101BEHEY       20250101000000100099999900 0000000000002300   ", "tar25be.txt");
        Assert.NotNull(z);
        Assert.Equal("HEY", z.Value.Code);
        Assert.Equal("BE", z.Value.Kanton);
        Assert.Equal(new DateOnly(2025, 1, 1), z.Value.ValidFrom);
        Assert.Equal(23.00m, z.Value.SatzPct);
    }

    [Fact]
    public void Parse_BeMey_29_5()
    {
        var z = QstSatzart11.ParseZeile(
            "1101BEMEY       20250101000000100099999900 0000000000002950   ");
        Assert.Equal(29.50m, z!.Value.SatzPct);
        Assert.Equal("MEY", z.Value.Code);
    }

    [Fact]
    public void Parse_IgnoriertSatzart06()
    {
        Assert.Null(QstSatzart11.ParseZeile("0601BEA0N       20250101000930100000005000 0000000000001500"));
        Assert.Null(QstSatzart11.ParseZeile("1101BEM0Y       20250101000000100099999900 0000000000000450"));
    }

    [Fact]
    public void SchliesseGueltigkeit_VorjahrEndetAmVortag()
    {
        var a = new QstSonderkategorieSatz
        {
            Code = "MEY", Kanton = "BE", SatzPct = 29.5m,
            Quelle = QstSatzart11.QuellePrefix + "tar24be.txt",
            ValidFrom = new DateOnly(2024, 1, 1),
        };
        var b = new QstSonderkategorieSatz
        {
            Code = "MEY", Kanton = "BE", SatzPct = 29.5m,
            Quelle = QstSatzart11.QuellePrefix + "tar25be.txt",
            ValidFrom = new DateOnly(2025, 1, 1),
        };
        QstSatzart11.SchliesseGueltigkeit([a, b]);
        Assert.Equal(new DateOnly(2024, 12, 31), a.ValidTo);
        Assert.Null(b.ValidTo);
    }

    [Fact]
    public void SatzFuer_StichtagWaehltVersion()
    {
        var saetze = new List<QstSonderkategorieSatz>
        {
            new() { Code = "HEY", Kanton = "BE", SatzPct = 22m, ValidFrom = new DateOnly(2024, 1, 1), ValidTo = new DateOnly(2024, 12, 31) },
            new() { Code = "HEY", Kanton = "BE", SatzPct = 23m, ValidFrom = new DateOnly(2025, 1, 1) },
        };
        Assert.Equal(22m, QstVordefinierteKategorie.SatzFuer("HEY", "BE", new DateOnly(2024, 4, 15), saetze));
        Assert.Equal(23m, QstVordefinierteKategorie.SatzFuer("HEY", "BE", new DateOnly(2025, 4, 15), saetze));
        Assert.Null(QstVordefinierteKategorie.SatzFuer("HEY", "AG", new DateOnly(2025, 4, 15), saetze));
    }

    [Fact]
    public void SatzFuer_NonIstNull()
    {
        Assert.Equal(0m, QstVordefinierteKategorie.SatzFuer("NON", "BE", new DateOnly(2025, 1, 1), []));
    }

    [Fact]
    public void VerzeichnisBereitsEingelesen_AlleDateienDa()
    {
        var quellen = new[]
        {
            "ESTV tar25be.txt Satzart 11",
            "ESTV tar26be.txt Satzart 11",
        };
        Assert.True(QstSatzart11.VerzeichnisBereitsEingelesen(
            quellen, new[] { "tar25be.txt", "tar26be.txt" }));
    }

    [Fact]
    public void VerzeichnisBereitsEingelesen_NeueJahresdateiFehlt()
    {
        var quellen = new[] { "ESTV tar25be.txt Satzart 11" };
        Assert.False(QstSatzart11.VerzeichnisBereitsEingelesen(
            quellen, new[] { "tar25be.txt", "tar27be.txt" }));
    }

    [Fact]
    public void VerzeichnisBereitsEingelesen_LeereTabelle()
    {
        Assert.False(QstSatzart11.VerzeichnisBereitsEingelesen(
            Array.Empty<string>(), new[] { "tar25be.txt" }));
    }

    [Fact]
    public void LiesDatei_Tar25be_HeyUndMey()
    {
        var path = FindTar25Be();
        Assert.True(File.Exists(path), path);
        var zeilen = QstSatzart11.LiesDatei(path);
        Assert.Contains(zeilen, z => z.Code == "HEY" && z.Kanton == "BE" && z.SatzPct == 23.00m);
        Assert.Contains(zeilen, z => z.Code == "MEY" && z.Kanton == "BE" && z.SatzPct == 29.50m);
        Assert.Contains(zeilen, z => z.Code == "HEN" && z.SatzPct == 23.00m);
        Assert.DoesNotContain(zeilen, z => z.Code.Length == 3 && z.Code[1] == '0');
    }

    private static string FindTar25Be()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var cand = Path.Combine(dir.FullName, "Assets", "Quellensteuer", "tar25be.txt");
            if (File.Exists(cand)) return cand;
            dir = dir.Parent;
        }
        return Path.Combine("Assets", "Quellensteuer", "tar25be.txt");
    }
}
