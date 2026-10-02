using System.Xml.Linq;
using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Swissdec Foundation-Test **F06_01 «Verletzte Plausibilitätsregeln anzeigen»**
/// (Walter 02.10.2026). Der RefApps-Distributor prüft nicht — OneCrew prüft die fertige
/// Meldung selbst vor dem Senden (AB-12 Punkt 2).
/// </summary>
public class ElmPlausibilitaetTests
{
    private static readonly XNamespace Sd = "http://www.swissdec.ch/schema/sd/20200220/SalaryDeclaration";
    private static readonly XNamespace C = "http://www.swissdec.ch/schema/sd/20200220/SalaryDeclarationContainer";
    private static readonly DateTime Stichtag = new(2026, 1, 31);

    private static XElement Meldung(string geb, string? ahv = "756.9217.0769.85", string eintritt = "2020-01-01", string? austritt = null)
    {
        var part = new XElement(C + "Particulars",
            new XElement(C + "Social-InsuranceIdentification", ahv == null ? new XElement(C + "unknown") : new XElement(C + "SV-AS-Number", ahv)),
            new XElement(C + "EmployeeNumber", "4711"),
            new XElement(C + "Lastname", "Muster"),
            new XElement(C + "Firstname", "Anna"),
            new XElement(C + "DateOfBirth", geb));
        var work = new XElement(C + "Work", new XElement(C + "EntryDate", eintritt),
            austritt == null ? null : new XElement(C + "WithdrawalDate", austritt));
        return new XElement(Sd + "DeclareSalary", new XElement(Sd + "Staff", new XElement(Sd + "Person", part, work)));
    }

    [Fact]
    public void SaubereMeldung_KeinFehler()
        => Assert.Empty(ElmPlausibilitaet.Pruefe(Meldung("1990-05-17"), Stichtag));

    [Fact]
    public void Alter100_IstFehler_99_Nicht()
    {
        var f = Assert.Single(ElmPlausibilitaet.Pruefe(Meldung("1926-01-31"), Stichtag));
        Assert.Equal(ElmPlausibilitaet.CodeAlter, f.Code);
        Assert.Equal("Error", f.Art);
        Assert.Equal("Plausibility", f.Stufe);
        Assert.Contains("Anna Muster (4711)", f.Text);
        Assert.Empty(ElmPlausibilitaet.Pruefe(Meldung("1926-02-01"), Stichtag));
    }

    [Fact]
    public void FalscheAhvPruefziffer_IstFehler_UnknownNicht()
    {
        var f = Assert.Single(ElmPlausibilitaet.Pruefe(Meldung("1990-05-17", "756.6564.5197.22"), Stichtag));
        Assert.Equal(ElmPlausibilitaet.CodeAhv, f.Code);
        Assert.Empty(ElmPlausibilitaet.Pruefe(Meldung("1990-05-17", ahv: null), Stichtag));
    }

    [Theory]
    [InlineData("756.9217.0769.85", true)]
    [InlineData("7569217076985", true)]
    [InlineData("756.9217.0769.84", false)]
    [InlineData("756.9217.0769", false)]
    public void AhvPruefziffer(string ahv, bool ok) => Assert.Equal(ok, ElmPlausibilitaet.AhvPruefzifferStimmt(ahv));

    [Fact]
    public void AustrittVorEintritt_UndEintrittVorGeburt_SindFehler()
    {
        Assert.Contains(ElmPlausibilitaet.Pruefe(Meldung("1990-05-17", austritt: "2019-12-31"), Stichtag),
            h => h.Code == ElmPlausibilitaet.CodeDatum && h.Text!.Contains("Austritt"));
        Assert.Contains(ElmPlausibilitaet.Pruefe(Meldung("1990-05-17", eintritt: "1989-01-01"), Stichtag),
            h => h.Code == ElmPlausibilitaet.CodeDatum && h.Text!.Contains("Geburtsdatum"));
    }

    /// <summary>Die offiziellen Referenzmeldungen der Muster AG dürfen keinen Fehlalarm auslösen.</summary>
    [Fact]
    public void RefXmlMusterAg_OhneFehlalarm()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "hr-system.csproj"))) dir = dir.Parent;
        var ordner = Path.Combine(dir!.FullName, "SWISSCEC", "RefXML");
        if (!Directory.Exists(ordner)) return;
        var dateien = Directory.GetFiles(ordner, "RefXML_*MONTHLY*.xml");
        foreach (var datei in dateien)
        {
            var name = Path.GetFileName(datei);
            var m = System.Text.RegularExpressions.Regex.Match(name, @"(\d{4})-?(\d{2})");
            var jahr = int.Parse(m.Groups[1].Value);
            var monat = int.Parse(m.Groups[2].Value);
            var stichtag = new DateTime(jahr, monat, DateTime.DaysInMonth(jahr, monat));
            var fehler = ElmPlausibilitaet.Pruefe(XDocument.Load(datei).Root!, stichtag);
            Assert.True(fehler.Count == 0, $"{name}: " + string.Join(" · ", fehler.Select(f => f.Text)));
        }
    }

    [Fact]
    public void GeburtNachPeriodenende_IstFehler()
        => Assert.Contains(ElmPlausibilitaet.Pruefe(Meldung("2026-02-15", eintritt: "2026-03-01"), Stichtag),
            h => h.Code == ElmPlausibilitaet.CodeDatum && h.Text!.Contains("nach dem Ende"));
}
