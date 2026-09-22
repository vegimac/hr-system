using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Syntax-Wächter für das Frontend (Claude 22.09.2026, Walter-Bug «openContractEditModal
/// is not defined»): In `contracts-edit.js` stand seit dem 08.09.2026 ein doppeltes
/// `else` — die Datei liess sich nicht parsen, damit war JEDE Funktion daraus weg und
/// «Vertrag bearbeiten» überall kaputt. Im Browser fällt so etwas nur auf, wenn man
/// genau diese Stelle benutzt; hier fällt es beim Testlauf auf.
///
/// Prüft mit `node --check` jede .js-Datei unter wwwroot. Ist node nicht installiert,
/// wird der Test übersprungen statt rot — die Testsuite soll nicht an der Werkzeugkette
/// scheitern.
/// </summary>
public class JavaScriptSyntaxTests
{
    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "hr-system.csproj")))
            dir = dir.Parent;
        return dir?.FullName;
    }

    private static bool NodeVorhanden()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("node", "--version")
            { RedirectStandardOutput = true, RedirectStandardError = true });
            p!.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public void AlleFrontendDateien_SindSyntaktischGueltig()
    {
        var root = RepoRoot();
        if (root == null || !NodeVorhanden()) return;   // ohne node/Repo: überspringen

        var wwwroot = Path.Combine(root, "wwwroot");
        var dateien = Directory.GetFiles(wwwroot, "*.js", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/lib/") && !f.Contains("\\lib\\"))
            .OrderBy(f => f)
            .ToList();
        Assert.NotEmpty(dateien);

        var kaputt = new List<string>();
        foreach (var f in dateien)
        {
            using var p = Process.Start(new ProcessStartInfo("node", $"--check \"{f}\"")
            { RedirectStandardOutput = true, RedirectStandardError = true })!;
            var err = p.StandardError.ReadToEnd();
            p.WaitForExit(20000);
            if (p.ExitCode != 0)
            {
                var zeile = err.Split('\n').FirstOrDefault(x => x.Contains("Error")) ?? err;
                kaputt.Add($"{Path.GetRelativePath(root, f)}: {zeile.Trim()}");
            }
        }

        Assert.True(kaputt.Count == 0,
            "JavaScript-Syntaxfehler — die Datei wird im Browser NICHT geladen, alle Funktionen daraus fehlen:\n"
            + string.Join("\n", kaputt));
    }
}
