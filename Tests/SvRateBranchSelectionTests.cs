using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// SV-Sätze pro Filiale (Walter-Vorgabe 06.08.2026).
///
/// Jede Filiale ist eine eigene GmbH — KTG/NBU/BU-Sätze können pro Filiale
/// abweichen (realer Fall: KTG ab 01.2026 in einer Filiale 1.945% statt
/// global 2.15%). Eine Satz-Zeile OHNE Filiale (CompanyProfileId NULL) ist
/// der globale Standard; eine Zeile MIT Filiale überschreibt den Standard
/// genau dort.
///
/// Die EINE Auswahl-Quelle ist <see cref="PayrollCalculations.SelectSvRatesForBranch"/> —
/// diese Tests nageln ihr Verhalten fest:
///   Filial-Zeile gewinnt pro Fach-Schlüssel (Code, MinAge, MaxAge,
///   EmploymentModelCode, OnlyQuellensteuer, BasisType) vor der globalen —
///   auch wenn die globale ein neueres ValidFrom hat —, innerhalb gleicher
///   Herkunft gewinnt das neueste ValidFrom; fremde Filial-Zeilen werden
///   ignoriert.
/// </summary>
public class SvRateBranchSelectionTests
{
    /// <summary>Kompakter Baukasten für Testzeilen (Pflichtfelder + Schlüssel).</summary>
    private static SocialInsuranceRate Rate(
        string code, decimal rate, int? cpId = null, string validFrom = "2026-01-01",
        int? minAge = null, int? maxAge = null, string? model = null,
        bool onlyQst = false, string basis = "gross", int sortOrder = 10)
        => new SocialInsuranceRate
        {
            Code                = code,
            Name                = code,
            Rate                = rate,
            CompanyProfileId    = cpId,
            ValidFrom           = DateOnly.Parse(validFrom),
            MinAge              = minAge,
            MaxAge              = maxAge,
            EmploymentModelCode = model,
            OnlyQuellensteuer   = onlyQst,
            BasisType           = basis,
            SortOrder           = sortOrder,
            IsActive            = true,
        };

    // (a) Filial-Zeile schlägt die globale Zeile gleichen Fach-Schlüssels.
    [Fact]
    public void FilialZeile_Schlaegt_GlobaleZeile_GleichenSchluessels()
    {
        var rates = new[]
        {
            Rate("KTG", 2.15m,  cpId: null),
            Rate("KTG", 1.945m, cpId: 5),
        };

        var result = PayrollCalculations.SelectSvRatesForBranch(rates, 5);

        var ktg = Assert.Single(result);
        Assert.Equal(1.945m, ktg.Rate);
        Assert.Equal(5, ktg.CompanyProfileId);
    }

    // (b) Ohne Filial-Zeile gilt die globale Zeile.
    [Fact]
    public void OhneFilialZeile_Gilt_Global()
    {
        var rates = new[] { Rate("KTG", 2.15m, cpId: null) };

        var result = PayrollCalculations.SelectSvRatesForBranch(rates, 5);

        var ktg = Assert.Single(result);
        Assert.Equal(2.15m, ktg.Rate);
        Assert.Null(ktg.CompanyProfileId);
    }

    // (c) Zeilen FREMDER Filialen werden komplett ignoriert.
    [Fact]
    public void FremdeFilialZeilen_Werden_Ignoriert()
    {
        var rates = new[]
        {
            Rate("KTG", 2.15m,  cpId: null),
            Rate("KTG", 1.945m, cpId: 7),    // andere Filiale
        };

        var result = PayrollCalculations.SelectSvRatesForBranch(rates, 5);

        var ktg = Assert.Single(result);
        Assert.Equal(2.15m, ktg.Rate);
        Assert.Null(ktg.CompanyProfileId);
    }

    // (d) Innerhalb gleicher Herkunft (beide global) gewinnt das neueste ValidFrom.
    [Fact]
    public void GleicheHerkunft_NeuestesValidFrom_Gewinnt()
    {
        var rates = new[]
        {
            Rate("AHV", 5.3m,  cpId: null, validFrom: "2024-01-01"),
            Rate("AHV", 5.35m, cpId: null, validFrom: "2026-01-01"),
        };

        var result = PayrollCalculations.SelectSvRatesForBranch(rates, 5);

        var ahv = Assert.Single(result);
        Assert.Equal(5.35m, ahv.Rate);
    }

    // (e) Die Filial-Zeile gewinnt AUCH, wenn die globale ein NEUERES ValidFrom hat.
    [Fact]
    public void FilialZeile_Gewinnt_Auch_Bei_NeuererGlobalerVersion()
    {
        var rates = new[]
        {
            Rate("KTG", 2.20m,  cpId: null, validFrom: "2026-06-01"),  // global, neuer
            Rate("KTG", 1.945m, cpId: 5,    validFrom: "2026-01-01"),  // Filiale, älter
        };

        var result = PayrollCalculations.SelectSvRatesForBranch(rates, 5);

        var ktg = Assert.Single(result);
        Assert.Equal(1.945m, ktg.Rate);
        Assert.Equal(5, ktg.CompanyProfileId);
    }

    // (f) companyProfileId = null (kein Filial-Kontext) liefert NUR globale Zeilen.
    [Fact]
    public void OhneFilialKontext_NurGlobaleZeilen()
    {
        var rates = new[]
        {
            Rate("KTG", 2.15m,  cpId: null),
            Rate("KTG", 1.945m, cpId: 5),
            Rate("NBUV", 0.9m,  cpId: 7),
        };

        var result = PayrollCalculations.SelectSvRatesForBranch(rates, null);

        var ktg = Assert.Single(result);
        Assert.Equal(2.15m, ktg.Rate);
        Assert.Null(ktg.CompanyProfileId);
    }

    // (g) Unterschiedliche Fach-Schlüssel (z.B. BVG-Altersbänder) bleiben
    //     ALLE erhalten — die Gruppierung fasst nur echte Duplikate zusammen.
    [Fact]
    public void UnterschiedlicheSchluessel_BleibenAlleErhalten()
    {
        var rates = new[]
        {
            Rate("BVG", 7.0m,  minAge: 25, maxAge: 34, basis: "bvg_basis", sortOrder: 50),
            Rate("BVG", 10.0m, minAge: 35, maxAge: 44, basis: "bvg_basis", sortOrder: 51),
            Rate("BVG", 15.0m, minAge: 45, maxAge: 54, basis: "bvg_basis", sortOrder: 52),
            Rate("BVG", 12.0m, minAge: 35, maxAge: 44, basis: "bvg_basis", sortOrder: 51, cpId: 5),
        };

        var result = PayrollCalculations.SelectSvRatesForBranch(rates, 5);

        Assert.Equal(3, result.Count);                       // drei Altersbänder
        Assert.Equal(7.0m,  result[0].Rate);                 // 25–34 global
        Assert.Equal(12.0m, result[1].Rate);                 // 35–44: Filial-Override
        Assert.Equal(5,     result[1].CompanyProfileId);
        Assert.Equal(15.0m, result[2].Rate);                 // 45–54 global
        // Ergebnis ist nach SortOrder sortiert
        Assert.True(result[0].SortOrder <= result[1].SortOrder
                 && result[1].SortOrder <= result[2].SortOrder);
    }
}
