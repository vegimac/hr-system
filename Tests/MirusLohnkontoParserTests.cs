using System.IO.Compression;
using System.Security;
using System.Text;
using HrSystem.Services.Vorsystem;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Mirus-Lohnkonto (Word): Personen, Folgeseiten, AG-Sektion, Total-Seite, Monatskopf.
/// Kunstdaten — nie echte Lohnkonten in Tests.
/// </summary>
public class MirusLohnkontoParserTests
{
    private static string Zeile(params string[] zellen)
        => "<w:tr>" + string.Concat(zellen.Select(z =>
               $"<w:tc><w:p><w:r><w:t>{SecurityElement.Escape(z)}</w:t></w:r></w:p></w:tc>")) + "</w:tr>";

    private static MemoryStream Docx(IEnumerable<string> zeilen)
    {
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
                  "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:tbl>" +
                  string.Concat(zeilen) + "</w:tbl></w:body></w:document>";
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry("word/document.xml");
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        ms.Position = 0;
        return ms;
    }

    private static IEnumerable<string> Seite(string? name, string? pnr, params string[][] zeilen)
    {
        yield return Zeile("999 Muster Restaurant", "Lohnkonto");
        if (name != null)
        {
            yield return Zeile("Name", name, "Versicherten Nr.", "756.0000.0000.00", "Eintritt", "01.03.2025", "Austritt");
            yield return Zeile("Personal Nr.", pnr!, "Geburtsdatum", "01.01.1990");
        }
        yield return Zeile("Nr.", "Bezeichnung", "Jan. 2026", "Feb 2026", "März 2026", "Total");
        foreach (var z in zeilen) yield return Zeile(z);
    }

    [Fact]
    public void Personen_Folgeseite_AgSektion_TotalSeite()
    {
        var rows = new List<string>();
        rows.AddRange(Seite("Muster Anna", "9990001",
            new[] { "10", "1", "Festlohn", "1'000.00", "1'100.00", "0.00", "2'100.00" },
            new[] { "250", "1", "Bruttolohn", "1'000.00", "1'100.00", "0.00", "2'100.00" }));
        // Folgeseite derselben Person: wiederholte Name-Zeile, dann AG-Teil
        rows.AddRange(Seite("Muster Anna", "9990001",
            new[] { "Arbeitgeber Betrag" },
            new[] { "2500", "103", "AHV / IV / EO", "53.00", "58.30", "0.00", "111.30" }));
        rows.AddRange(Seite("Beispiel Ben", "9990002",
            new[] { "250", "1", "Bruttolohn", "500.00", "0.00", "250.00", "750.00" }));
        // Total-Seite: Kopf ohne Name-Zeile
        rows.AddRange(Seite(null, null,
            new[] { "250", "1", "Bruttolohn", "1'500.00", "1'100.00", "250.00", "2'850.00" }));

        var erg = MirusLohnkontoParser.Lies(Docx(rows));

        Assert.Equal("999", erg.RestaurantCode);
        Assert.Empty(erg.Warnungen);
        Assert.Equal(new[] { (2026, 1), (2026, 2), (2026, 3) }, erg.Monate);

        var personen = erg.Personen.Where(p => !p.IstTotal).ToList();
        Assert.Equal(2, personen.Count);
        var anna = personen[0];
        Assert.Equal("9990001", anna.Personalnummer);
        Assert.Equal("01.03.2025", anna.Eintritt);
        Assert.Equal("", anna.Austritt);
        Assert.Contains(anna.Zeilen, z => z.Sektion == "AG" && z.Code == "2500.103");
        Assert.Equal(1100.00m, anna.Zeilen.Single(z => z.Code == "10.1").Werte.Single(w => w.Monat == 2).Betrag);

        var total = erg.Personen.Single(p => p.IstTotal);
        Assert.Equal(2850.00m, total.Zeilen.Single(z => z.Code == "250.1").Total);

        // Nullen werden weggelassen
        var werte = MirusLohnkontoParser.Werte(anna).ToList();
        Assert.DoesNotContain(werte, w => w.Betrag == 0);
        Assert.Equal(6, werte.Count);
    }

    [Theory]
    [InlineData("Jan. 2026", 1)]
    [InlineData("März 2026", 3)]
    [InlineData("Juni 2026", 6)]
    [InlineData("Sept 2026", 9)]
    [InlineData("Nov. 2026", 11)]
    [InlineData("Dez 2026", 12)]
    public void Monatskopf(string text, int monat)
        => Assert.Equal((2026, monat), MirusLohnkontoParser.ParseMonat(text));

    [Fact]
    public void Keine_Word_Datei_gibt_klare_Meldung()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) zip.CreateEntry("x.txt");
        ms.Position = 0;
        var ex = Assert.Throws<InvalidDataException>(() => MirusLohnkontoParser.Lies(ms));
        Assert.Contains("Word", ex.Message);
    }
}
