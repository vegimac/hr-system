using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Stempel-Berichte im McAdmin: PDFs lassen sich erzeugen (QuestPDF-Layout prüft erst zur Laufzeit).</summary>
public class StempelBerichtPdfTests
{
    static readonly DateOnly Tag = new(2026, 3, 2);

    [Fact]
    public void Verstoesse_PDF_mit_vielen_Zeilen()
    {
        var stempel = new List<StempelZeit> { new(Tag, "08:00", "14:10", 480, 850, 370), new(Tag, "14:30", "17:00", 870, 1020, 150) };
        var zeilen = Enumerable.Range(0, 60).Select(i => new StempelVerstossZeile(
            ArbeitszeitVerstoesse.Reihenfolge[i % 8], "Titel", Tag.AddDays(i), Tag.AddDays(i + (i % 3 == 0 ? 6 : 0)),
            "Anna Muster hat zu lange gearbeitet — ein längerer Text, damit die Zeile umbricht und mehrere Seiten entstehen.",
            370, 330, "Art. 18 ArGV1", stempel)).ToList();
        var daten = new StempelVerstoesseDaten("999 Musterhausen", Tag, Tag.AddMonths(1), 12,
            ArbeitszeitVerstoesse.Reihenfolge.Select(a => new StempelVerstossArt(a, ArbeitszeitVerstoesse.Titel[a], ArbeitszeitVerstoesse.Regel[a], 3)).ToList(),
            new List<StempelVerstossMa> { new(1, "9990001", "Anna", "Muster", zeilen), new(2, null, "Beat", "Beispiel", zeilen.Take(2).ToList()) });
        var pdf = new StempelBerichtPdfService().Verstoesse(daten);
        Assert.True(pdf.Length > 1000);
    }

    [Fact]
    public void Korrekturen_PDF_auch_leer()
    {
        var zeilen = Enumerable.Range(0, 50).Select(i => new StempelKorrekturZeile(i, Tag.AddDays(i % 20), "10:02", i % 4 == 0 ? null : "15:30",
            i % 2 == 0 ? "10:30" : null, null, new[] { "ZEIT", "MANUELL", "BEARBEITET", "KOMMENTAR" }[i % 4],
            "Beat Beispiel", new DateTime(2026, 3, 3, 9, 15, 0), "vergessen auszustempeln", "Manuell erstellter Zeitstempel")).ToList();
        var daten = new StempelKorrekturenDaten("999 Musterhausen", Tag, Tag.AddMonths(1), 1200, 50,
            new List<StempelAnzahl> { new("Beat Beispiel", 50) },
            new List<StempelKorrekturMa> { new(1, "9990001", "Anna", "Muster", zeilen) });
        Assert.True(new StempelBerichtPdfService().Korrekturen(daten).Length > 1000);
        var leer = daten with { Korrigiert = 0, ProBearbeiter = new(), Mitarbeiter = new() };
        Assert.True(new StempelBerichtPdfService().Korrekturen(leer).Length > 500);
    }
}
