using System.Collections.Generic;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

public class DokumentStrukturPdfTests
{
    [Fact]
    public void Generate_MitKategorienUndLeererKat_WirftNicht()
    {
        var svc = new DokumentStrukturPdfService();
        var pdf = svc.Generate(new List<DokumentStrukturPdfService.KategorieBlock>
        {
            new()
            {
                Name = "Lohn / Arbeitszeit",
                Aktiv = true,
                AnzahlDokumente = 953,
                Typen =
                {
                    new() { Name = "Lohnabrechnung", SortOrder = 10, Aktiv = true, AnzahlDokumente = 2 },
                    new() { Name = "Kinderzulagen", SortOrder = 40, Aktiv = true, LinkedFieldCode = "family_allowance", AnzahlDokumente = 472 },
                    new() { Name = "Diverses", SortOrder = 999, Aktiv = false, AnzahlDokumente = 0 },
                }
            },
            new() { Name = "Leer", Aktiv = true, Typen = { } }
        });
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 200);
        Assert.Equal(0x25, pdf[0]); // %PDF
    }
}
