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
}
