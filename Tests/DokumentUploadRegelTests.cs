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

    // ── Inhalt muss zur Endung passen (Walter-Vorgabe 08.10.2026) ──────────

    private static byte[] B(params int[] b) => b.Select(x => (byte)x).ToArray();
    private static byte[] A(string s) => System.Text.Encoding.ASCII.GetBytes(s);
    private static byte[] Ftyp(string marke, params string[] kompatibel)
    {
        var box = new List<byte>();
        var laenge = 16 + 4 * kompatibel.Length;
        box.AddRange(B(0, 0, 0, laenge));
        box.AddRange(A("ftyp" + marke));
        box.AddRange(B(0, 0, 0, 0));
        foreach (var k in kompatibel) box.AddRange(A(k));
        return box.ToArray();
    }

    public static IEnumerable<object[]> EchteDateien() => new[]
    {
        new object[] { "Ausweis.pdf", A("%PDF-1.7\n%âãÏÓ") },
        new object[] { "Scan.pdf",    A("\r\n\r\n%PDF-1.4 mit Vorspann") },
        new object[] { "foto.jpg",    B(0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10) },
        new object[] { "scan.png",    B(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0) },
        new object[] { "falsch.jpg",  B(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0) },
        new object[] { "bild.gif",    A("GIF89a....") },
        new object[] { "scan.tif",    B(0x49, 0x49, 0x2A, 0x00, 8, 0) },
        new object[] { "scan.tiff",   B(0x4D, 0x4D, 0x00, 0x2A, 0, 8) },
        new object[] { "web.webp",    A("RIFF\0\0\0\0WEBPVP8 ") },
        new object[] { "iphone.heic", Ftyp("heic", "mif1", "heic") },
        new object[] { "iphone.heif", Ftyp("mif1", "heic") },
    };

    [Theory]
    [MemberData(nameof(EchteDateien))]
    public void EchterInhaltPasst(string name, byte[] kopf)
        => Assert.True(DokumentUploadRegel.InhaltPasst(name, kopf));

    public static IEnumerable<object[]> GetarnteDateien() => new[]
    {
        new object[] { "rechnung.pdf", A("MZ\x90\0") },                          // Windows-Programm
        new object[] { "rechnung.pdf", A("<html><script>alert(1)</script>") },
        new object[] { "rechnung.pdf", B(0x50, 0x4B, 0x03, 0x04) },              // ZIP/DOCX
        new object[] { "rechnung.pdf", B(0xFF, 0xD8, 0xFF, 0xE0) },              // Foto als .pdf
        new object[] { "foto.jpg",     A("%PDF-1.7") },                          // PDF als .jpg
        new object[] { "foto.png",     A("<svg onload=alert(1)>") },
        new object[] { "foto.heic",    Ftyp("isom", "mp42") },                   // MP4-Video
        new object[] { "leer.pdf",     Array.Empty<byte>() },
    };

    [Theory]
    [MemberData(nameof(GetarnteDateien))]
    public void GetarnterInhaltWirdAbgelehnt(string name, byte[] kopf)
        => Assert.False(DokumentUploadRegel.InhaltPasst(name, kopf));

    private static Microsoft.AspNetCore.Http.IFormFile Datei(string name, byte[] inhalt)
        => new Microsoft.AspNetCore.Http.FormFile(new MemoryStream(inhalt), 0, inhalt.Length, "file", name);

    [Fact]
    public void Pruefe_umbenanntesProgramm_meldetInhaltPasstNicht()
    {
        var fehler = DokumentUploadRegel.Pruefe(Datei("rechnung.pdf", A("MZ\x90\0 This program cannot be run in DOS mode")));
        var json = System.Text.Json.JsonSerializer.Serialize(fehler);
        Assert.Contains("INHALT_PASST_NICHT", json);
        Assert.Contains("rechnung.pdf", json);
    }

    [Fact]
    public void Pruefe_echtesPdf_istOk_undWordBleibtNurPdf()
    {
        Assert.Null(DokumentUploadRegel.Pruefe(Datei("Ausweis.pdf", A("%PDF-1.7\n"))));
        Assert.Contains("NUR_PDF", System.Text.Json.JsonSerializer.Serialize(DokumentUploadRegel.Pruefe(Datei("Vertrag.docx", B(0x50, 0x4B, 3, 4)))));
    }

    [Fact]
    public void Postfach_prueftNurInhalt_EndungEgal()
    {
        Assert.True(DokumentUploadRegel.IstPdfOderBild(Datei("image", B(0xFF, 0xD8, 0xFF, 0xE1))));
        Assert.False(DokumentUploadRegel.IstPdfOderBild(Datei("foto.jpg", A("MZ\x90\0"))));
    }
}
