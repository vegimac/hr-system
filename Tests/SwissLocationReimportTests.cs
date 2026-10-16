using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Stellt sicher, dass die AMTOVZ-CSV im Repo parsebar ist und die
/// bekannten Korrekturen (3360 ohne Thörigen, 4922 mit Bützberg) enthält.
/// </summary>
public class SwissLocationReimportTests
{
    private static string FindCsv()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, SwissLocationReimportService.CsvFileName);
            if (File.Exists(p)) return p;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(SwissLocationReimportService.CsvFileName);
    }

    [Fact]
    public void Csv_parses_and_has_expected_ortschaften()
    {
        var path = FindCsv();
        var rows = SwissLocationReimportService.ParseCsv(path);
        Assert.True(rows.Count > 3000, $"erwartet >3000 Zeilen, war {rows.Count}");

        // Thörigen nur unter 3367 — nie unter 3360 (alter Import-Bug).
        Assert.DoesNotContain(rows, r => r.Plz4 == "3360" && r.Ortschaftsname == "Thörigen");
        Assert.Contains(rows, r => r.Plz4 == "3360" && r.Ortschaftsname == "Herzogenbuchsee");
        Assert.Contains(rows, r => r.Plz4 == "3367" && r.Ortschaftsname == "Thörigen");

        // Ortschaft ≠ Gemeinde
        Assert.Contains(rows, r => r.Plz4 == "4922" && r.Ortschaftsname == "Bützberg"
            && r.Gemeindename == "Thunstetten");
    }

    [Fact]
    public void Amtovz_swisstopo_format_picks_highest_adressenanteil()
    {
        // Offizielles Format ab Jan 2026 (Ortschaftsname;PLZ4;…;Adressenanteil;…)
        var lines = new[]
        {
            "Ortschaftsname;PLZ4;Zusatzziffer;ZIP_ID;Gemeindename;BFS-Nr;Kantonskürzel;Adressenanteil;E;N;Sprache;Validity",
            "Lausanne;1015;00;150;Chavannes-près-Renens;5627;VD;7.586 %;1;1;fr;2008-07-01",
            "Lausanne;1015;00;150;Ecublens (VD);5635;VD;84.138 %;1;1;fr;2008-07-01",
            "Lausanne;1015;00;150;Saint-Sulpice (VD);5648;VD;8.276 %;1;1;fr;2008-07-01",
            "Bützberg;4922;00;1;Thunstetten;450;BE;100 %;1;1;de;2008-07-01",
        };
        var rows = SwissLocationReimportService.ParseCsvLines(lines);
        Assert.Equal(2, rows.Count);
        var lausanne = Assert.Single(rows, r => r.Plz4 == "1015");
        Assert.Equal("Ecublens (VD)", lausanne.Gemeindename);
        Assert.Equal(5635, lausanne.BfsNr);
        Assert.Contains(rows, r => r.Plz4 == "4922" && r.Ortschaftsname == "Bützberg"
            && r.Gemeindename == "Thunstetten" && r.BfsNr == 450);
    }
}
