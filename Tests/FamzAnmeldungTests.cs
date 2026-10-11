using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// GastroSocial-Anmeldung Familienzulagen (Walter 11.10.2026,
/// docs/famz-anmeldung-konzept.md): Formular füllen + Vorbefüllungs-Regeln.
/// </summary>
public class FamzAnmeldungTests
{
    static FamzAnmeldungDto Beispiel() => new()
    {
        Antragsteller = new FamzPersonDto
        {
            NameVorname = "Laci Agneza", StrasseNr = "Bahnhofstrasse 5", PlzOrt = "6210 Sursee", Kanton = "LU",
            Geburtsdatum = "04.05.1990", AhvNummer = "756.1234.5678.97", Geschlecht = "F",
            Zivilstand = "verheiratet", ZivilstandSeit = "12.06.2015", Nationalitaet = "Kosovo", Auslaenderausweis = "C",
        },
        Erreichbar = "+41 79 123 45 67, agneza@example.ch",
        AbWann = "01.03.2026",
        LeistungBezug = false,
        AgName = "Schaub Restaurants GmbH, Sursee",
        AgAbrechnungsnummer = "629.0535.00",
        AgStrasse = "Allmendstrasse 1",
        AgPlzOrt = "6210 Sursee",
        BeschaeftigtVon = "01.03.2026",
        Arbeitspensum = "80",
        ArbeitsortKanton = "LU",
        Bruttolohn = "CHF 4'200.00/Mt.",
        EinkommenUeberMinimum = true,
        WeitereArbeitgeber = false,
        AndererElternteil = new FamzElternteilDto
        {
            NameVorname = "Laci Arben", Geschlecht = "M", Zivilstand = "verheiratet",
            Angestellt = true, ArbeitgeberZeile1 = "Bau AG, Hauptstrasse 3", ArbeitgeberZeile2 = "6000 Luzern, 041 000 00 00",
            ArbeitsortKanton = "LU", Arbeitspensum = "100",
            EinkommenHoeher = true, EinkommenUeberMinimum = true, SeNe = "NEIN", LeistungBezug = false,
        },
        Partner = new FamzElternteilDto
        {
            NameVorname = "Muster Peter", Geschlecht = "M", Zivilstand = "verheiratet", AhvNummer = "756.9999.8888.77",
            Angestellt = true, ArbeitgeberZeile1 = "Garage AG", ArbeitsortKanton = "AG", Arbeitspensum = "60",
            EinkommenHoeher = false, EinkommenUeberMinimum = true, SeNe = "SE", Ausgleichskasse = "SVA Aargau",
            LeistungBezug = true, LeistungVon1 = "ALV",
        },
        Kinder = new()
        {
            new FamzKindDto { Name = "Laci", Vorname = "Elira", Geburtsdatum = "02.02.2016", Geschlecht = "F", Beziehung = "L" },
            new FamzKindDto { Name = "Laci", Vorname = "Dren", Geburtsdatum = "07.07.2019", Geschlecht = "M", Beziehung = "L" },
            new FamzKindDto { Name = "Berisha", Vorname = "Lea", Geburtsdatum = "01.01.2012", Geschlecht = "F", Beziehung = "L",
                              Wohnadresse = "Rr. Nena Tereze 1, 10000 Prishtina, Kosovo" },
        },
        Sorge = "ELTERN",
        LebenBei = "ELTERN",
        OrtDatumArbeitgeber = "Sursee, 11.10.2026",
    };

    [Fact]
    public void Formular_wird_gefuellt_und_ist_flach()
    {
        var svc = new FamzAnmeldungPdfService(new StubEnv(FindRepoRoot()));
        var bytes = svc.Generate(Beispiel());
        var ziel = Environment.GetEnvironmentVariable("FAMZ_PDF_OUT");
        if (!string.IsNullOrWhiteSpace(ziel)) File.WriteAllBytes(ziel, bytes);

        using var pdf = new PdfDocument(new PdfReader(new MemoryStream(bytes)));
        Assert.Equal(5, pdf.GetNumberOfPages());
        var s1 = PdfTextExtractor.GetTextFromPage(pdf.GetPage(1));
        Assert.Contains("Laci Agneza", s1);
        Assert.Contains("Sursee", s1);
        var s4 = PdfTextExtractor.GetTextFromPage(pdf.GetPage(4));
        Assert.Contains("Elira", s4);
        Assert.Contains("Prishtina", s4);
        // geflacht: keine Formularfelder mehr
        var form = iText.Forms.PdfAcroForm.GetAcroForm(pdf, false);
        Assert.True(form == null || form.GetAllFormFields().Count == 0);
    }

    [Theory]
    [InlineData("756.1234.5678.97", "1234567897")]
    [InlineData("7561234567897", "1234567897")]
    [InlineData("", null)]
    public void Ahv_ohne_Laendercode(string ein, string? aus)
        => Assert.Equal(aus, FamzAnmeldungPdfService.OhneLaendercode(ein));

    [Theory]
    [InlineData("629.0535.00", "629053500")]
    [InlineData("LU 629.0535.00", "629053500")]
    [InlineData("", null)]
    public void Abrechnungsnummer_nur_Ziffern(string ein, string? aus)
        => Assert.Equal(aus, FamzAnmeldungPdfService.NurZiffern(ein));

    [Fact]
    public void Wohnt_bei_Eltern_wenn_beide_im_Haushalt()
    {
        var kind = new EmployeeFamilyMember { MemberType = "Kind", LebtImHaushalt = true };
        var vater = new EmployeeFamilyMember { MemberType = "Ehepartner", Gender = "M", LebtImHaushalt = true };
        Assert.Equal("ELTERN", FamzAnmeldungController.WohntBei(kind, vater, "F"));
    }

    [Fact]
    public void Wohnt_bei_MA_wenn_anderer_Elternteil_ausgezogen()
    {
        var kind = new EmployeeFamilyMember { MemberType = "Kind", LebtImHaushalt = true };
        var vater = new EmployeeFamilyMember { MemberType = "AndererElternteil", Gender = "M", LebtImHaushalt = false };
        Assert.Equal("MUTTER", FamzAnmeldungController.WohntBei(kind, vater, "F"));
    }

    [Fact]
    public void Wohnt_beim_anderen_Elternteil_wenn_nicht_im_Haushalt()
    {
        var kind = new EmployeeFamilyMember { MemberType = "Kind", LebtImHaushalt = false };
        var mutter = new EmployeeFamilyMember { MemberType = "AndererElternteil", Gender = "F", LebtImHaushalt = false };
        Assert.Equal("MUTTER", FamzAnmeldungController.WohntBei(kind, mutter, "M"));
    }

    [Fact]
    public void Sorge_gemischt_ergibt_Bemerkung_statt_Kreuz()
    {
        var (wert, bem) = FamzAnmeldungController.Zusammenfassen(new[] { ("Elira", (string?)"ELTERN"), ("Lea", "MUTTER") });
        Assert.Null(wert);
        Assert.Equal("Elira: Eltern, Lea: Mutter", bem);
        Assert.Equal(("ELTERN", (string?)null), FamzAnmeldungController.Zusammenfassen(new[] { ("A", (string?)"ELTERN"), ("B", "ELTERN") }));
    }

    [Fact]
    public void Beschaeftigt_von_folgt_der_Kette_in_derselben_Filiale()
    {
        var v1 = new Employment { Id = 1, CompanyProfileId = 7, ContractStartDate = new DateTime(2023, 1, 1), ContractEndDate = new DateTime(2024, 12, 31) };
        var v2 = new Employment { Id = 2, CompanyProfileId = 7, ContractStartDate = new DateTime(2025, 1, 1) };
        var andere = new Employment { Id = 3, CompanyProfileId = 8, ContractStartDate = new DateTime(2020, 1, 1), ContractEndDate = new DateTime(2022, 12, 31) };
        var (von, bis) = FamzAnmeldungController.KetteInFiliale(new[] { andere, v1, v2 }, v2);
        Assert.Equal(new DateOnly(2023, 1, 1), von);
        Assert.Null(bis);
    }

    [Fact]
    public void Wiedereintritt_nach_Luecke_beginnt_neu()
    {
        var v1 = new Employment { Id = 1, CompanyProfileId = 7, ContractStartDate = new DateTime(2023, 1, 1), ContractEndDate = new DateTime(2024, 6, 30) };
        var v2 = new Employment { Id = 2, CompanyProfileId = 7, ContractStartDate = new DateTime(2024, 9, 1) };
        var (von, _) = FamzAnmeldungController.KetteInFiliale(new[] { v1, v2 }, v2);
        Assert.Equal(new DateOnly(2024, 9, 1), von);
    }

    static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Assets", "Forms", FamzAnmeldungPdfService.TemplateFile)))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Repo-Root mit dem FAK-Formular nicht gefunden.");
    }

    private sealed class StubEnv : IWebHostEnvironment
    {
        public StubEnv(string contentRoot) => ContentRootPath = contentRoot;
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = "";
        public string ContentRootPath { get; set; }
        public Microsoft.Extensions.FileProviders.IFileProvider WebRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
