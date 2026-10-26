using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Die mitgelieferte ESTV-Datei ist die 20-km-Liste ab 1.1.2024,
/// nicht eine Handvoll Probeorte.
/// </summary>
public class GrenzregionKatalogTests
{
    private static string FindCsv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, "data", GrenzregionKatalog.DateiName);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(GrenzregionKatalog.DateiName);
    }

    [Fact]
    public void Liste_hat_518_Gemeinden_und_Como()
    {
        var zeilen = GrenzregionKatalog.Lesen(FindCsv());
        Assert.Equal(518, zeilen.Count);
        Assert.Contains(zeilen, z => z.Plz == "22100" && z.Ort == "Como" && z.Land == "IT" && z.Region == "Como, Lombardia");
        Assert.Contains(zeilen, z => z.Ort == "Campione d'Italia");
        Assert.DoesNotContain(zeilen, z => z.Ort == "Monza");
        Assert.All(zeilen, z => Assert.Equal("IT", z.Land));
    }
}
