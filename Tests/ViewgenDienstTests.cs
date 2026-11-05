using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Viewgen-Aufruf mit einem Ersatz für java: Nur ein echtes PDF zählt, Arbeitsdateien verschwinden.</summary>
public class ViewgenDienstTests : IDisposable
{
    private readonly string _ordner = Directory.CreateTempSubdirectory("viewgen_test_").FullName;

    public void Dispose() => Directory.Delete(_ordner, recursive: true);

    private ViewgenDienst Dienst(string skript, double sekunden = 20)
    {
        Directory.CreateDirectory(Path.Combine(_ordner, "app", "lib"));
        Directory.CreateDirectory(Path.Combine(_ordner, "jdk", "bin"));
        var java = Path.Combine(_ordner, "jdk", "bin", "java");
        File.WriteAllText(java, "#!/bin/sh\n" + skript + "\n");
        File.SetUnixFileMode(java, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return new ViewgenDienst(_ordner, TimeSpan.FromSeconds(sekunden));
    }

    private static IEnumerable<string> Arbeitsordner() =>
        Directory.GetDirectories(Path.GetTempPath(), "viewgen_*").Where(d => !Path.GetFileName(d).StartsWith("viewgen_test_"));

    [Theory]
    [InlineData("QstReport", false, true)]
    [InlineData("StatisticReport", false, true)]
    [InlineData("PlausibilityReport", true, true)]
    [InlineData("AhvReport", true, false)]
    [InlineData("FakDetailedReport", true, false)]
    public void Bericht_kommt_aus_der_richtigen_Meldung(string plugin, bool jahr, bool monat)
    {
        Assert.Equal(jahr, ViewgenDienst.AusJahr(plugin));
        Assert.Equal(monat, ViewgenDienst.AusMonat.Contains(plugin));
    }

    [Fact]
    public async Task Pdf_wird_geliefert_und_Arbeitsordner_entfernt()
    {
        var vorher = Arbeitsordner().ToHashSet();
        var r = await Dienst("printf '%%PDF-1.4 inhalt' > \"$PWD/bericht.pdf\"").ErzeugePdfAsync("<x/>", "AhvReport");
        Assert.Null(r.Fehler);
        Assert.Equal((byte)'%', r.Pdf![0]);
        Assert.Empty(Arbeitsordner().Except(vorher));
    }

    [Fact]
    public async Task Rueckgabewert_0_ohne_Pdf_gilt_als_Fehler()
    {
        var r = await Dienst("echo 'ERROR kaputt'; exit 0").ErzeugePdfAsync("<x/>", "AhvReport");
        Assert.Null(r.Pdf);
        Assert.Contains("kein PDF", r.Fehler);
        Assert.Contains("ERROR kaputt", r.Fehler);
    }

    [Fact]
    public async Task Datei_ohne_Pdf_Kennung_gilt_als_Fehler()
    {
        var r = await Dienst("echo nein > \"$PWD/bericht.pdf\"").ErzeugePdfAsync("<x/>", "AhvReport");
        Assert.Null(r.Pdf);
    }

    [Fact]
    public async Task Haengender_Lauf_wird_beendet()
    {
        var r = await Dienst("exec sleep 30", sekunden: 1).ErzeugePdfAsync("<x/>", "AhvReport");
        Assert.Null(r.Pdf);
        Assert.Contains("nicht geantwortet", r.Fehler);
    }

    [Fact]
    public async Task Unbekannter_Bericht_startet_nichts()
    {
        var r = await Dienst("touch \"" + _ordner + "/gestartet\"").ErzeugePdfAsync("<x/>", "GibtEsNicht");
        Assert.Null(r.Pdf);
        Assert.False(File.Exists(Path.Combine(_ordner, "gestartet")));
    }

    [Fact]
    public async Task Fehlende_Installation_wird_gemeldet()
    {
        var d = new ViewgenDienst(Path.Combine(_ordner, "fehlt"), TimeSpan.FromSeconds(1));
        Assert.False(d.Installiert);
        Assert.Contains("nicht installiert", (await d.ErzeugePdfAsync("<x/>", "AhvReport")).Fehler);
    }

    [Fact]
    public void Argumente_richten_Viewgen_auf_den_Arbeitsordner()
    {
        var a = ViewgenDienst.Argumente("/v/lib", "AhvReport", "/w/m.xml", "/w").ToList();
        Assert.Equal("/w", a[a.IndexOf("-o") + 1]);
        Assert.Equal("/w/m.xml", a[a.IndexOf("-x") + 1]);
        Assert.Contains("-Djava.io.tmpdir=/w", a);
        Assert.Contains("-Djava.awt.headless=true", a);
    }
}
