using System.Text.RegularExpressions;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Gelöst wird eine Doku-Verknüpfung dort, wo sie gemacht wurde — in der Maske
/// (Walter-Vorgabe 28.09.2026). In der Dokumentenverwaltung sieht man nicht, was
/// an einem Zeiger hängt: eine QST-Bestätigung dort zu lösen, wäre gefährlich.
///
/// <para>Darum diese Wache: JEDER Doku-Knopf in einer Maske, der verknüpfen kann,
/// muss auch lösen können. Genau eine Stelle hatte das nicht — die Geburtsurkunde
/// am Kind; sie liess sich verknüpfen und dann nie mehr lösen.</para>
/// </summary>
public class DokuLoesenVollstaendigTests
{
    [Fact]
    public void JederDokuKnopf_DerVerknuepfenKann_KannAuchLoesen()
    {
        var luecken = new List<string>();

        foreach (var datei in Dateien())
        {
            var quelle = File.ReadAllText(datei);
            foreach (Match m in Regex.Matches(quelle, @"docIconBtn\(\{"))
            {
                var block = KlammerBlock(quelle, m.Index + m.Length - 1);
                if (block == null || !block.Contains("verknuepfen", StringComparison.Ordinal)) continue;
                if (block.Contains("loesen", StringComparison.Ordinal)) continue;

                var was = Regex.Match(block, @"was:\s*'([^']*)'");
                var zeile = quelle[..m.Index].Count(c => c == '\n') + 1;
                luecken.Add($"{Path.GetFileName(datei)}:{zeile} — «{(was.Success ? was.Groups[1].Value : "?")}»");
            }
        }

        Assert.True(luecken.Count == 0,
            "Diese Doku-Knöpfe können verknüpfen, aber nicht lösen:\n  " + string.Join("\n  ", luecken));
    }

    /// <summary>Von der öffnenden Klammer bis zur zugehörigen schliessenden.</summary>
    private static string? KlammerBlock(string s, int start)
    {
        var tiefe = 0;
        for (var i = start; i < s.Length; i++)
        {
            if (s[i] == '{') tiefe++;
            else if (s[i] == '}' && --tiefe == 0) return s[start..(i + 1)];
        }
        return null;
    }

    private static IEnumerable<string> Dateien()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        var wwwroot = Path.Combine(dir!, "wwwroot");
        return Directory.GetFiles(wwwroot, "*.js")
            .Concat(Directory.GetFiles(Path.Combine(wwwroot, "js"), "*.js"));
    }
}
