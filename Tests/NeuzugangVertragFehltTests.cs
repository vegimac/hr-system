using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Übertritt: MA vorhanden, Vertrag der neuen Filiale fehlt (Walter-Bug 24.09.2026,
/// Fall Simona Dan Sursee → Reinach).
///
/// Ablauf des Fehlers: Beim Übertritt ist der MA-Datensatz schon da, und nach einem
/// ersten Import stimmen auch die Stammdaten. Die Vorschau stuft ihn darum als
/// UNCHANGED ein — und genau die Zeilen liess «Neuer MA aus easy@work» weg. Damit
/// war er nicht anwählbar, ohne Auswahl gibt es keinen Commit, ohne Commit entsteht
/// der Vertrag nie, und weil die Filial-Liste über die Anstellungen geht, blieb er
/// in der neuen Filiale unsichtbar. Auf dem Bildschirm stand «Alles aktuell».
///
/// Der Marker dafür ist <c>EmploymentInfo == "wird nachgeholt"</c>: MA vorhanden,
/// Anstellung dieser Filiale fehlt. Dieser Test hält fest, dass die Neuzugangs-Liste
/// diese Zeilen führt — ein Quelltext-Test, weil der Pfad sonst nur mit einer
/// echten easy@work-Verbindung prüfbar wäre.
/// </summary>
public class NeuzugangVertragFehltTests
{
    private static string Quelle(string datei)
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        var pfad = Path.Combine(dir!, datei);
        Assert.True(File.Exists(pfad), datei + " nicht gefunden.");
        return File.ReadAllText(pfad);
    }

    [Fact]
    public void NeuzugangListe_FuehrtMaOhneVertragDieserFiliale()
    {
        var src = Quelle("Controllers/EasyAtWorkNeuzugangController.cs");

        // Die Bedingung muss UNCHANGED + «wird nachgeholt» abdecken …
        Assert.Contains("r.Status == \"UNCHANGED\" && r.EmploymentInfo == \"wird nachgeholt\"", src);
        // … und in der Zeilen-Auswahl tatsächlich verwendet werden.
        Assert.Contains("|| AnstellungFehlt(r)", src);
        // Der Benutzer muss sehen, warum die Zeile da ist.
        Assert.Contains("vertragFehlt", src);
    }

    [Fact]
    public void MarkerText_StimmtMitDerVorschauUeberein()
    {
        // Wird der Text in der Vorschau umbenannt, greift der Filter oben ins Leere
        // und der Fehler wäre zurück — ohne dass es jemand merkt.
        var sync = Quelle("Services/EasyAtWork/EasyAtWorkEmployeeSyncService.cs");
        Assert.Contains("\"wird nachgeholt\"", sync);
    }

    [Fact]
    public void Oberflaeche_ZeigtDieEigeneMarke()
    {
        var js = Quelle("wwwroot/employees.js");
        Assert.Contains("VERTRAG FEHLT", js);
        Assert.Contains("x.vertragFehlt", js);
    }
}
