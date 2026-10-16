using HrSystem.Services.Elm;
using Xunit;

namespace HrSystem.Tests;

/// <summary>Bericht des Swissdec Quality Tools lesen (Walter 15.10.2026) — Auszug eines echten Berichts.</summary>
public class QualityToolBerichtTests
{
    private const string Beispiel = """
    {"monitoringId":"schaub","score":3.4,"apiMessage":"Report has been generated","sequences":[
      {"sequence":"2025-01","subprocesses":[{"subprocess":"MonthlySalary","testcases":[
        {"testcaseId":"catchAll","sequence":"2025-01","subprocess":"MonthlySalary","name":"catchAll","type":"catchAll","results":[
          {"declarationID":"d2","requestID":"r2","requestTs":"2026-09-28 23:36:46.623","outcome":"different","type":"child_lookup",
           "controlvalue":"null","testvalue":"{urn:ch:swissdec:elm:v6:20260306:salarydeclaration}SporadicBenefits",
           "controlxpath":"null","testxpath":"/DeclareMonthlySalary[1]/MonthlySalaryDeclaration[1]/Staff[1]/Person[23]/TaxAtSourceSalaries[1]/TaxAtSourceSalary[1]/Current[1]/SporadicBenefits[1]"}]},
        {"testcaseId":"18","sequence":"2025-01","subprocess":"MonthlySalary","name":"Blanc Pierre","type":"Person","results":[
          {"declarationID":"d2","requestID":"r2","requestTs":"2026-09-28 23:36:46.623","outcome":"different","type":"text_value",
           "controlvalue":"19.23","testvalue":"19.25",
           "controlxpath":"/DeclareMonthlySalary[1]/MonthlySalaryDeclaration[1]/Staff[1]/Person[14]/TaxAtSourceSalaries[1]/TaxAtSourceSalary[1]/Current[1]/OtherActivities[1]/HourlyOrLessonSalary[1]/text()[1]",
           "testxpath":"/DeclareMonthlySalary[1]/MonthlySalaryDeclaration[1]/Staff[1]/Person[14]/TaxAtSourceSalaries[1]/TaxAtSourceSalary[1]/Current[1]/OtherActivities[1]/HourlyOrLessonSalary[1]/text()[1]"}]}]}]},
      {"sequence":"2024-11","subprocesses":[{"subprocess":"MonthlySalary","testcases":[
        {"testcaseId":"SalaryTotals","sequence":"2024-11","subprocess":"MonthlySalary","name":"SalaryTotals","type":"SalaryTotals","results":[
          {"declarationID":"d1","requestID":"r1","requestTs":"2026-09-28 22:26:16.288","outcome":"different","type":"text_value",
           "controlvalue":"34.00","testvalue":"36.05",
           "controlxpath":"/DeclareMonthlySalary[1]/MonthlySalaryDeclaration[1]/SalaryTotals[1]/TaxAtSourceTotals[2]/TotalMonth[1]/TotalTaxAtSource[1]/text()[1]",
           "testxpath":"/x"}]},
        {"testcaseId":"14","sequence":"2024-11","subprocess":"MonthlySalary","name":"Egli Anna","type":"Person","results":[
          {"declarationID":"d1","requestID":"r1","requestTs":"2026-09-28 22:26:16.288","outcome":"different","type":"text_value",
           "controlvalue":"34.00","testvalue":"36.05",
           "controlxpath":"/DeclareMonthlySalary[1]/MonthlySalaryDeclaration[1]/Staff[1]/Person[2]/TaxAtSourceSalaries[1]/TaxAtSourceSalary[1]/Current[1]/TaxAtSource[1]/text()[1]",
           "testxpath":"/x"},
          {"declarationID":"d1","requestID":"r1","requestTs":"2026-09-28 22:26:16.288","outcome":"different","type":"text_value",
           "controlvalue":"A0N","testvalue":"A0Y",
           "controlxpath":"/DeclareMonthlySalary[1]/MonthlySalaryDeclaration[1]/Staff[1]/Person[2]/TaxAtSourceSalaries[1]/TaxAtSourceSalary[1]/Current[1]/TaxAtSourceCategory[1]/TaxAtSourceCode[1]/text()[1]",
           "testxpath":"/x"}]},
        {"testcaseId":"3","sequence":"2024-11","subprocess":"MonthlySalary","name":"Ohne Befund","type":"Person","results":[]}]}]}]}
    """;

    [Fact]
    public void Monate_chronologisch_mit_Anzahl()
    {
        var b = QualityToolBericht.Lies(Beispiel);
        Assert.Equal("schaub", b.MonitoringId);
        Assert.Equal(3.4, b.Punkte);
        Assert.Equal(new[] { "2024-11", "2025-01" }, b.Monate.Select(m => m.Sequenz));
        Assert.Equal(5, b.Anzahl);
        Assert.Equal(3, b.Monate[0].Anzahl);
    }

    [Fact]
    public void Personen_vor_Summen_und_Faelle_ohne_Befund_fehlen()
    {
        var nov = QualityToolBericht.Lies(Beispiel).Monate[0];
        Assert.Equal(new[] { "TF14 Egli Anna", "Summen der Meldung" }, nov.Faelle.Select(f => f.Name));
    }

    [Fact]
    public void Wertabweichung_mit_Soll_Ist_und_deutschem_Feld()
    {
        var egli = QualityToolBericht.Lies(Beispiel).Monate[0].Faelle[0];
        var qst = egli.Abweichungen[0];
        Assert.Equal("Wert", qst.Art);
        Assert.Equal("34.00", qst.Soll);
        Assert.Equal("36.05", qst.Ist);
        Assert.Equal("Quellensteuer-Zeile › Laufender Monat › Quellensteuer-Betrag", qst.Feld);
        Assert.Equal("Laufender Monat › QST-Kategorie › QST-Code", egli.Abweichungen[1].Feld);
        Assert.Contains("Betrag weicht ab", qst.Klartext);
    }

    [Fact]
    public void Element_zu_viel_nimmt_den_Pfad_der_Meldung()
    {
        var jan = QualityToolBericht.Lies(Beispiel).Monate[1];
        var uebrig = jan.Faelle.Single(f => f.Id == "catchAll");
        Assert.Equal("Übrige Abweichungen", uebrig.Name);
        var a = uebrig.Abweichungen.Single();
        Assert.Equal("Element zu viel", a.Art);
        Assert.Null(a.Soll);
        Assert.Equal("Aperiodische Leistungen", a.Ist);
        Assert.Contains("Person[23]", a.Pfad);
        Assert.Equal("Quellensteuer-Zeile › Laufender Monat › Aperiodische Leistungen", a.Feld);
        Assert.Contains("erwartet es nicht", a.Klartext);
    }

    [Theory]
    [InlineData(
        "/DeclareMonthlySalary[1]/Staff[1]/Person[1]/StatisticSalaries[1]/StatisticSalary[1]/AnnualValues[1]/Earnings13th[1]/text()[1]",
        "Statistik › Jahreswerte › 13. Monatslohn")]
    [InlineData(
        "/…/TaxAtSourceSalaries[1]/TaxAtSourceSalary[1]/AdditionalParticulars[1]/MarriagePartner[1]",
        "Quellensteuer-Zeile › Zusatzangaben › Ehepartner")]
    [InlineData(
        "/…/TaxAtSourceSalary[1]/Current[1]/DeclarationCategory[1]",
        "Quellensteuer-Zeile › Laufender Monat › An-/Abmeldung")]
    public void Feld_uebersetzt_typische_QT_Pfade(string xpath, string erwartet)
        => Assert.Equal(erwartet, QualityToolBericht.Feld(xpath));

    [Fact]
    public void Leere_Antwort_ergibt_leeren_Bericht()
    {
        var b = QualityToolBericht.Lies("{}");
        Assert.Equal(0, b.Punkte);
        Assert.Empty(b.Monate);
    }

    [Fact]
    public void Uebersicht_zeigt_jeden_gesendeten_Monat_auch_ohne_Abweichung()
    {
        var b = QualityToolBericht.Lies(Beispiel); // Abweichungen in 2024-11 und 2025-01
        var sendungen = new[]
        {
            new ElmVorgang
            {
                Art = "monthly", Jahr = 2024, Monat = 11, Titel = "Monatsmeldung 11.2024",
                Url = ElmEndpunkte.QualityToolUrl, JobFinished = true,
                Adressaten = { new ElmVorgangAdressat { Zustand = "Success", State = "Finished" } },
            },
            new ElmVorgang
            {
                Art = "monthly", Jahr = 2024, Monat = 12, Titel = "Monatsmeldung 12.2024",
                Url = ElmEndpunkte.QualityToolUrl, JobFinished = true,
                Adressaten = { new ElmVorgangAdressat { Zustand = "Success", State = "Finished" } },
            },
            new ElmVorgang
            {
                Art = "monthly", Jahr = 2025, Monat = 2, Titel = "Monatsmeldung 02.2025",
                Url = ElmEndpunkte.QualityToolUrl, JobFinished = true,
                Adressaten =
                {
                    new ElmVorgangAdressat
                    {
                        Zustand = "Success", State = "CompletionReleaseMissing",
                        Completion = new ElmCompletionInfo { AufrufUrl = "https://x" },
                    },
                },
            },
        };
        var u = QualityToolBericht.Uebersicht(b, sendungen);
        // Bericht hat 2024-11 + 2025-01; Sendungen zusätzlich 2024-12 + 2025-02
        Assert.Equal(new[] { "2024-11", "2024-12", "2025-01", "2025-02" }, u.Select(x => x.Sequenz));
        Assert.Equal("rot", u[0].Status);   // Nov: Abweichungen im Bericht
        Assert.Equal("ok", u[1].Status);    // Dez: gesendet, keine Abweichung → Stimmt
        Assert.Equal("rot", u[2].Status);   // Jan: Abweichungen im Bericht (sticht Dialog)
        Assert.Equal("warn", u[3].Status);  // Feb: Dialog/Freigabe offen, noch keine QT-Abweichung
        Assert.Contains("wartet", u[3].Text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(u[1].GesendetAm);   // Dez: Sendezeit aus unserem Vorgang
        Assert.NotNull(u[3].GesendetAm);
    }

    [Fact]
    public void Quality_Tool_ist_auf_der_Produktion_kein_Meldungsziel()
    {
        if (ElmEndpunkte.IstTestinstanz()) return;
        Assert.Equal(new[] { "prod" }, ElmEndpunkte.MeldungsZiele().Select(z => z.Schluessel));
        Assert.Throws<InvalidOperationException>(() => ElmMeldungenService.ZielFuerNeue("qualitytool"));
        Assert.Equal("prod", ElmMeldungenService.ZielFuerNeue(null).Schluessel);
    }
}
