using HrSystem.Services;
using HrSystem.Services.Meldepflicht;
using iText.Forms;
using iText.Kernel.Pdf;
using Xunit;

namespace HrSystem.Tests;

public class Formular2bTests
{
    [Theory]
    [InlineData("AG", "meldung.arbeit@ag.ch")]
    [InlineData("so", "bewilligungen@ddi.so.ch")]
    [InlineData("LU", "migration@lu.ch")]
    [InlineData("BE", "meldeverfahren.midi@be.ch")]
    public void Behoerde_Nach_Arbeitsort(string kanton, string erwartet)
        => Assert.Equal(erwartet, MigrationsamtAdressen.Fuer(kanton)[0]);

    [Theory]
    [InlineData("Schichtkoordinator/in ab 7. Monat", true, "Schichtkoordinatorin ab 7. Monat")]
    [InlineData("Schichtkoordinator/in ab 7. Monat", false, "Schichtkoordinator ab 7. Monat")]
    [InlineData("Restaurant Manager/in", true, "Restaurant Managerin")]
    public void Funktion_Wie_Im_Vertrag_Mit_Geschlecht(string roh, bool weiblich, string erwartet)
        => Assert.Equal(erwartet, Funktionsbezeichnung.Geschlecht(roh, weiblich));

    [Fact]
    public void Unbekannter_Kanton_Hat_Keine_Adresse()
        => Assert.Empty(MigrationsamtAdressen.Fuer("XX"));

    [Fact]
    public void Pdf_Traegt_Beendigung_Und_Namen()
    {
        var vorlage = FindeVorlage();
        var pdf = Formular2bPdf.Fuellen(File.ReadAllBytes(vorlage), new Formular2bDaten
        {
            Name = "Muster",
            Vorname = "Ada",
            Beendigung = true,
            Datum = "20.10.2026",
            Kanton = "AG",
            Stundenlohn = true,
            Lohn = "22.50",
            Weiblich = true,
        });
        using var doc = new PdfDocument(new PdfReader(new MemoryStream(pdf)));
        var form = PdfAcroForm.GetAcroForm(doc, false);
        Assert.Equal("Muster", form.GetField("Noms").GetValueAsString());
        Assert.Equal("Ada", form.GetField("Prénoms").GetValueAsString());
        Assert.Equal("20.10.2026", form.GetField("DtEndTaetigkeit").GetValueAsString());
        Assert.Equal("AG", form.GetField("cbxVille").GetValueAsString());
    }

    static string FindeVorlage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var kandidat = Path.Combine(dir.FullName, "Assets", "Formulare", "formular-2b.pdf");
            if (File.Exists(kandidat)) return kandidat;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("formular-2b.pdf");
    }
}
