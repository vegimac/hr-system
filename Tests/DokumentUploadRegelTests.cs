using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Nur PDF und Bilder werden gespeichert (Walter-Vorgabe 01.10.2026).
/// Word/Excel werden abgelehnt, aber als umwandelbar markiert; ZIP nicht.
/// </summary>
public class DokumentUploadRegelTests
{
    [Theory]
    [InlineData("Ausweis.pdf")]
    [InlineData("Ausweis.PDF")]
    [InlineData("foto.jpg")]
    [InlineData("scan.jpeg")]
    [InlineData("scan.png")]
    [InlineData("scan.tiff")]
    [InlineData("iphone.heic")]
    public void PdfUndBilderSindErlaubt(string name)
        => Assert.True(DokumentUploadRegel.IstErlaubt(name));

    [Theory]
    [InlineData("Vertrag.docx")]
    [InlineData("Vertrag.doc")]
    [InlineData("Liste.xlsx")]
    [InlineData("Liste.xls")]
    [InlineData("Unterlagen.zip")]
    [InlineData("notiz.txt")]
    [InlineData("export.csv")]
    [InlineData("ohne-endung")]
    [InlineData(null)]
    public void AnderesIstGesperrt(string? name)
        => Assert.False(DokumentUploadRegel.IstErlaubt(name));

    [Fact]
    public void WordIstUmwandelbar_ZipNicht()
    {
        Assert.True(OfficeToPdfService.CanConvert("Vertrag.docx"));
        Assert.True(OfficeToPdfService.CanConvert("Liste.xlsx"));
        Assert.False(OfficeToPdfService.CanConvert("Unterlagen.zip"));
    }

    [Fact]
    public void FehlerNenntDateiUndHinweis()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(DokumentUploadRegel.Fehler("Vertrag.docx"));
        Assert.Contains("NUR_PDF", json);
        Assert.Contains("Vertrag.docx", json);
        Assert.Contains("umwandelbar\":true", json);
    }
}
