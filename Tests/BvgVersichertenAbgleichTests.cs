using HrSystem.Services;
using NPOI.XSSF.UserModel;
using Xunit;
using Z = HrSystem.Services.BvgVersichertenAbgleich.Zeile;
using M = HrSystem.Services.BvgVersichertenAbgleich.Ma;

namespace HrSystem.Tests;

public class BvgVersichertenAbgleichTests
{
    private static readonly DateTime Heute = new(2026, 10, 31);

    private static Z Liste(string vor, string name, string ahv, DateTime ab, DateTime? bis = null, string? plan = "Uno Basis")
        => new() { Vorname = vor, Nachname = name, Ahv = ahv, GueltigAb = ab, GueltigBis = bis, Bezeichnung = plan };

    private static M Oc(int id, string vor, string name, string? ahv, bool inFiliale = true, bool aktiv = true,
        DateOnly? ab = null, DateOnly? bis = null, string? andere = null)
        => new()
        {
            Id = id, Vorname = vor, Nachname = name, Ahv = ahv,
            InFiliale = inFiliale, AktivInFiliale = aktiv, AndereFiliale = andere,
            VersichertAb = ab, VersichertBis = bis,
        };

    [Fact]
    public void Liste_ohne_Eintrag_wird_festgelegt()
    {
        var r = BvgVersichertenAbgleich.Vergleiche(
            new[] { Liste("Ksenija", "Adamovic", "756.2449.5776.26", new DateTime(2021, 1, 1), plan: "Uno Integral") },
            new[] { Oc(1, "Ksenija", "Adamović", "7562449577626") }, Heute);
        var t = Assert.Single(r.Festlegen);
        Assert.Equal(BvgVersichertenAbgleich.Anlegen, t.Aktion);
        Assert.Equal("Uno Integral", t.Bezeichnung);
        Assert.Empty(r.Passt);
    }

    [Fact]
    public void Gleicher_Beginn_passt()
    {
        var r = BvgVersichertenAbgleich.Vergleiche(
            new[] { Liste("Anna", "Muster", "756.1234.5678.97", new DateTime(2024, 1, 1)) },
            new[] { Oc(1, "Anna", "Muster", "756.1234.5678.97", ab: new DateOnly(2024, 1, 1)) }, Heute);
        Assert.Single(r.Passt);
        Assert.Empty(r.Festlegen);
    }

    [Fact]
    public void OneCrew_beginnt_spaeter_ist_abweichend()
    {
        var r = BvgVersichertenAbgleich.Vergleiche(
            new[] { Liste("Anna", "Muster", "756.1234.5678.97", new DateTime(2021, 1, 1)) },
            new[] { Oc(1, "Anna", "Muster", "756.1234.5678.97", ab: new DateOnly(2025, 3, 1)) }, Heute);
        Assert.Single(r.Abweichend);
        Assert.Empty(r.Festlegen);
    }

    [Fact]
    public void Ende_auf_der_Liste_beendet_offenen_Eintrag()
    {
        var r = BvgVersichertenAbgleich.Vergleiche(
            new[] { Liste("Anna", "Muster", "756.1234.5678.97", new DateTime(2021, 1, 1), new DateTime(2026, 10, 31)) },
            new[] { Oc(1, "Anna", "Muster", "756.1234.5678.97", ab: new DateOnly(2021, 1, 1)) }, Heute);
        var t = Assert.Single(r.Beenden);
        Assert.Equal(BvgVersichertenAbgleich.Beenden, t.Aktion);
    }

    [Fact]
    public void Andere_Filiale_wird_nicht_festgelegt()
    {
        var r = BvgVersichertenAbgleich.Vergleiche(
            new[] { Liste("Anna", "Muster", "756.1234.5678.97", new DateTime(2021, 1, 1)) },
            new[] { Oc(1, "Anna", "Muster", "756.1234.5678.97", inFiliale: false, aktiv: false, andere: "122 Sursee") }, Heute);
        Assert.Single(r.AndereFiliale);
        Assert.Empty(r.Festlegen);
    }

    [Fact]
    public void Unbekannt_und_nur_in_OneCrew()
    {
        var r = BvgVersichertenAbgleich.Vergleiche(
            new[] { Liste("Fremd", "Person", "756.1111.2222.33", new DateTime(2021, 1, 1)) },
            new[] { Oc(2, "Ben", "Versichert", "756.9999.0000.11", ab: new DateOnly(2024, 1, 1)) }, Heute);
        Assert.Single(r.NichtZuordenbar);
        Assert.Equal(2, Assert.Single(r.NurInOneCrew).EmployeeId);
    }

    [Fact]
    public void Excel_liest_Versichertenliste()
    {
        var wb = new XSSFWorkbook();
        var sheet = wb.CreateSheet("Sheet1");
        var kopf = sheet.CreateRow(0);
        var titel = new[] { "Name", "Vorname", "AHV-Nummer", "Geburtsdatum", "Bezeichnung", "Gültig ab", "Gültig bis" };
        for (int i = 0; i < titel.Length; i++) kopf.CreateCell(i).SetCellValue(titel[i]);
        var row = sheet.CreateRow(1);
        row.CreateCell(0).SetCellValue("Adamovic");
        row.CreateCell(1).SetCellValue("Ksenija");
        row.CreateCell(2).SetCellValue("756.2449.5776.26");
        row.CreateCell(3).SetCellValue("22.11.1971");
        row.CreateCell(4).SetCellValue("Uno Integral Mc Donald's");
        row.CreateCell(5).SetCellValue("01.01.2021");
        using var ms = new MemoryStream();
        wb.Write(ms, leaveOpen: true);
        ms.Position = 0;
        var z = Assert.Single(BvgVersichertenAbgleich.LiesExcel(ms, "liste.xlsx"));
        Assert.Equal("Ksenija", z.Vorname);
        Assert.Equal(new DateTime(2021, 1, 1), z.GueltigAb);
        Assert.Null(z.GueltigBis);
        Assert.Contains("Integral", z.Bezeichnung);
    }
}
