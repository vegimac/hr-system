using System.IO.Compression;
using System.Text;
using HrSystem.Data;
using HrSystem.Models;
using HrSystem.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HrSystem.Tests;

public class GrenzregionEstvTests
{
    [Fact]
    public void FindeDateiUrl_nimmt_die_Grenzgemeinden_nicht_ein_anderes_Excel()
    {
        var html = """
            <a href="https://www.estv.admin.ch/dam/de/sd-web/WH7JhQ6osZ2Z/qst-230101-abrechnungsformulare-ersatzeinkuenfte-de.xlsx">Formular</a>
            <a href="https://www.estv.admin.ch/dam/de/sd-web/jjD-hrVGX46W/qst-20231222-comuni-italiani-nei-20-km-de.xlsx">Liste</a>
            """;
        var url = GrenzregionEstv.FindeDateiUrl(html);
        Assert.EndsWith("comuni-italiani-nei-20-km-de.xlsx", url);
        Assert.StartsWith("https://www.estv.admin.ch/", url);
    }

    [Fact]
    public void LiesXlsx_liest_Comune_Provinz_und_Region()
    {
        var zeilen = GrenzregionEstv.LiesXlsx(MiniXlsx());
        Assert.Single(zeilen);
        Assert.Equal("22100", zeilen[0].Plz);
        Assert.Equal("Como", zeilen[0].Ort);
        Assert.Equal("Como, Lombardia", zeilen[0].Region);
        Assert.Equal("IT", zeilen[0].Land);
    }

    [Fact]
    public void Echte_Estv_Datei_hat_518_Gemeinden_und_Como()
    {
        var pfad = "/tmp/estv-grenz.xlsx";
        if (!File.Exists(pfad)) return;
        var zeilen = GrenzregionEstv.LiesXlsx(File.ReadAllBytes(pfad));
        Assert.Equal(518, zeilen.Count);
        Assert.Contains(zeilen, z => z.Plz == "22100" && z.Ort == "Como" && z.Region == "Como, Lombardia");
        Assert.Contains(zeilen, z => z.Ort == "Campione d'Italia");
        Assert.DoesNotContain(zeilen, z => z.Ort == "Monza");
        Assert.All(zeilen, z => Assert.Equal("IT", z.Land));
    }

    [Fact]
    public void Planen_unter_400_aendert_nichts()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            GrenzregionEstv.Planen(Array.Empty<GrenzregionEstv.Bestand>(), new List<GrenzregionKatalog.Zeile>
            {
                new("22100", "Como", "Como, Lombardia", "IT"),
            }));
        Assert.Contains("bestehende Liste bleibt", ex.Message);
    }

    [Fact]
    public async Task Anwenden_aktualisiert_Como_entfernt_Monza_laesst_Konstanz()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("GrenzEstv_" + Guid.NewGuid()).Options);
        db.Grenzregionen.Add(new Grenzregion { Plz = "22100", Ort = "Como", Region = "IT", LandIso = "IT" });
        db.Grenzregionen.Add(new Grenzregion { Plz = "20900", Ort = "Monza", Region = "IT", LandIso = "IT" });
        db.Grenzregionen.Add(new Grenzregion { Plz = "78462", Ort = "Konstanz", Region = "DE", LandIso = "DE" });
        await db.SaveChangesAsync();

        var liste = Enumerable.Range(0, 400)
            .Select(i => new GrenzregionKatalog.Zeile((10000 + i).ToString(), "Ort" + i, "R", "IT"))
            .ToList();
        liste[0] = new GrenzregionKatalog.Zeile("22100", "Como", "Como, Lombardia", "IT");

        var erg = await GrenzregionEstv.AnwendenAsync(db, liste, CancellationToken.None);
        Assert.Equal(399, erg.Neu);
        Assert.Equal(1, erg.Aktualisiert);
        Assert.Equal(1, erg.Entfernt);

        var alle = await db.Grenzregionen.OrderBy(g => g.LandIso).ThenBy(g => g.Ort).ToListAsync();
        Assert.Contains(alle, g => g.LandIso == "DE" && g.Ort == "Konstanz");
        Assert.DoesNotContain(alle, g => g.Ort == "Monza");
        var como = Assert.Single(alle, g => g.Ort == "Como");
        Assert.Equal("Como, Lombardia", como.Region);
        Assert.Equal(400, alle.Count(g => g.LandIso == "IT"));
    }

    private static byte[] MiniXlsx()
    {
        var shared = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="6" uniqueCount="6">
              <si><t>CAP</t></si>
              <si><t>Comune</t></si>
              <si><t>Provincia</t></si>
              <si><t>Regione</t></si>
              <si><t>Como</t></si>
              <si><t>Lombardia</t></si>
            </sst>
            """;
        var sheet = """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                <row r="2">
                  <c r="B2" t="s"><v>0</v></c>
                  <c r="C2" t="s"><v>1</v></c>
                  <c r="F2" t="s"><v>2</v></c>
                  <c r="G2" t="s"><v>3</v></c>
                </row>
                <row r="4">
                  <c r="B4" t="s"><v>4</v></c>
                  <c r="C4" t="s"><v>4</v></c>
                  <c r="F4" t="s"><v>4</v></c>
                  <c r="G4" t="s"><v>5</v></c>
                </row>
              </sheetData>
            </worksheet>
            """;
        // B4 muss die PLZ sein, nicht der Ortsname. CAP als Zahl, Ort über Shared String.
        sheet = sheet.Replace(
            """<c r="B4" t="s"><v>4</v></c>""",
            """<c r="B4"><v>22100</v></c>""");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            Schreib(zip, "xl/sharedStrings.xml", shared);
            Schreib(zip, "xl/worksheets/sheet1.xml", sheet);
        }
        return ms.ToArray();
    }

    private static void Schreib(ZipArchive zip, string name, string xml)
    {
        var entry = zip.CreateEntry(name);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(xml);
    }
}
