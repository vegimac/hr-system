using HrSystem.Controllers;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Verknüpfungen lösen (Walter 28.09.2026). Die Feldliste am Mitarbeiter ist die
/// einzige Quelle für Anzeigen UND Lösen — läuft sie auseinander, zeigt die Liste
/// eine Verknüpfung, die sich nicht lösen lässt (genau Walters Fall).
/// </summary>
public class DokumentVerknuepfungTests
{
    /// <summary>
    /// Jedes Feld der Liste muss die EF-Abfrage in <c>VerknuepfungenAsync</c> auch
    /// wirklich abdecken. Beides steht ausgeschrieben da (EF kann keine Delegaten
    /// übersetzen), darum hier der Abgleich Zeichen für Zeichen.
    /// </summary>
    [Fact]
    public void JedesMaFeld_StehtAuchInDerAbfrage()
    {
        var quelle = System.IO.File.ReadAllText(PfadZu("Controllers/DocumentsController.cs"));

        var von = quelle.IndexOf("MaDokumentFelder =", System.StringComparison.Ordinal);
        var bis = quelle.IndexOf("};", von, System.StringComparison.Ordinal);
        var tabelle = quelle[von..bis];

        var abfrageVon = quelle.IndexOf("var emps = await _db.Employees", System.StringComparison.Ordinal);
        var abfrage = quelle[abfrageVon..(quelle.IndexOf(".ToListAsync();", abfrageVon, System.StringComparison.Ordinal))];

        var felder = System.Text.RegularExpressions.Regex
            .Matches(tabelle, @"e\.(\w+DokumentId)")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();

        Assert.NotEmpty(felder);
        foreach (var f in felder)
            Assert.True(abfrage.Contains("e." + f + " == docId", System.StringComparison.Ordinal),
                $"Feld {f} fehlt in der EF-Abfrage — die Verknüpfung erschiene, liesse sich aber nicht lösen.");
    }

    private static string PfadZu(string relativ)
    {
        var dir = System.AppContext.BaseDirectory;
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir, "hr-system.csproj")))
            dir = System.IO.Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return System.IO.Path.Combine(dir!, relativ);
    }
}
