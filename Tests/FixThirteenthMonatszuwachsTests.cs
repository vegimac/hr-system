using System.Text.Json;
using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// FIX/FIX-M 13. ML = 1/12 der grünen Basis (YTD Round05).
/// Walter 17.09.2026: L-GAV Art. 12 Ziff. 1+3 Satz 1, Swissdec 1200.
/// 8.33 % bleibt nur FLEX/MTP (1201).
/// </summary>
public class FixThirteenthMonatszuwachsTests
{
    [Fact]
    public void AchtKomma33IstNichtEinZwoelftel()
    {
        Assert.Equal(416.50m, PayrollCalculations.Round05(5000m * 8.33m / 100m));
        Assert.Equal(416.65m, PayrollCalculations.FixThirteenthSollYtd(5000m));
    }

    [Fact]
    public void ZwolfMonate_Konstant_JahresgenauEinMonatslohn()
    {
        decimal ytd = 0, vor = 0, sumAccrual = 0;
        for (int m = 1; m <= 12; m++)
        {
            ytd += 2600m;
            sumAccrual += PayrollCalculations.FixThirteenthMonatszuwachs(ytd, vor);
            vor = ytd;
        }
        Assert.Equal(2600m, sumAccrual);
        Assert.Equal(2600m, PayrollCalculations.FixThirteenthSollYtd(31200m));
    }

    [Fact]
    public void Pensumwechsel_ZuwachsIstYtdDifferenz_NichtProzent()
    {
        decimal vor = 9600m * 4m;
        decimal ytd = vor + 30000m;
        decimal mai = PayrollCalculations.FixThirteenthMonatszuwachs(ytd, vor);
        Assert.Equal(
            PayrollCalculations.FixThirteenthSollYtd(ytd) - PayrollCalculations.FixThirteenthSollYtd(vor),
            mai);
        Assert.NotEqual(PayrollCalculations.Round05(30000m * 8.33m / 100m), mai);
    }

    [Fact]
    public void IstFixMonatslohnModell_NurFixUndFixM()
    {
        Assert.True(PayrollCalculations.IstFixMonatslohnModell("FIX"));
        Assert.True(PayrollCalculations.IstFixMonatslohnModell("FIX-M"));
        Assert.False(PayrollCalculations.IstFixMonatslohnModell("FLEX"));
        Assert.False(PayrollCalculations.IstFixMonatslohnModell("MTP"));
    }

    [Fact]
    public void LiesBasis13ml_FeldVorZeile()
    {
        using var doc = JsonDocument.Parse("""{"basis13ml":4800.00,"lohnLines":[]}""");
        Assert.Equal(4800m, PayrollCalculations.LiesBasis13ml(doc.RootElement));
    }

    [Fact]
    public void LiesBasis13ml_Fallback180_1()
    {
        using var doc = JsonDocument.Parse(
            """{"lohnLines":[{"code":"180.1","basis":9600.00,"betrag":0}]}""");
        Assert.Equal(9600m, PayrollCalculations.LiesBasis13ml(doc.RootElement));
    }

    [Fact]
    public void BuildResult_FixNutztAccrualForDisplay_NichtFilialProzent()
    {
        var emp = new Employee { Id = 1, FirstName = "Ann", LastName = "Fix", Street = "A", ZipCode = "6000", City = "Luzern" };
        var contract = new Employment { Id = 1, EmployeeId = 1, EmploymentModel = "FIX", MonthlySalary = 5000m, IsActive = true };
        var company = new CompanyProfile { Id = 1, CompanyName = "Test", Street = "S", HouseNumber = "1", ZipCode = "6000", City = "Luzern", DefaultThirteenthSalaryPercent = 8.33m };
        var saldo = new SaldoBlock(
            VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 0,
            SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
            NightHours: 0, NightBonus: 0, NachtKompStunden: 0,
            VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
            VacationWeeks: 5, VormonatFerienTage: 0, FerienTageAccrual: 0,
            FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
            VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
            VormonatFeiertagTage: 0, FeiertagTageAccrual: 0,
            FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
            ThirteenthPct: 8.33m,
            PrevThirteenth: 0m,
            ThirteenthAccrualForDisplay: 416.65m,
            Basis13ml: 5000m);

        var anon = PayrollCalculations.BuildResult(
            emp, contract, company, 2025, 1,
            new DateOnly(2025, 1, 1), new DateOnly(2025, 1, 31),
            new List<object>(), new List<object>(),
            new List<DeductionRule>(), 5000m,
            new SvBases(5000m, 5000m, 5000m, 5000m, 5000m),
            new List<object>(), 0, new List<object>(), 0,
            new List<object>(), 0, saldo,
            new List<EmployeeLohnAssignment>(),
            new List<EmployeeBankAccount>());
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in anon.GetType().GetProperties())
            dict[p.Name] = p.GetValue(anon);

        Assert.Equal(416.65m, (decimal)dict["thirteenthMonthly"]!);
        Assert.Equal(5000m, (decimal)dict["basis13ml"]!);
        // Filial-8.33 % auf 5'000 wäre 416.50 — darf nicht gewinnen.
        Assert.NotEqual(416.50m, (decimal)dict["thirteenthMonthly"]!);
    }

    [Fact]
    public void Code13mlAuszahlen_Ist1803_Nicht1802()
    {
        Assert.Equal("180.3", PayrollCalculations.Code13mlAuszahlen);
        Assert.NotEqual("180.2", PayrollCalculations.Code13mlAuszahlen);
    }

    [Fact]
    public void Filialwechsel_NimmtLetztenSaldoDerFrueherenFiliale()
    {
        var ti = new PayrollSaldo
        {
            Id = 1, EmployeeId = 25, CompanyProfileId = 10,
            PeriodYear = 2025, PeriodMonth = 3,
            ThirteenthMonthAccumulated = 1366.65m, HourSaldo = -337.14m
        };
        var aelter = new PayrollSaldo
        {
            Id = 2, EmployeeId = 25, CompanyProfileId = 10,
            PeriodYear = 2025, PeriodMonth = 2,
            ThirteenthMonthAccumulated = 366.65m
        };
        var gewaehlt = PayrollCalculations.WaehleVormonatsSaldo(
            new[] { aelter, ti }, companyProfileId: 20, year: 2025, month: 4);
        Assert.Same(ti, gewaehlt);
    }

    [Fact]
    public void DieselbeFiliale_VormonatGewinntVorAelteremAnderen()
    {
        var lu = new PayrollSaldo
        {
            Id = 3, CompanyProfileId = 20, PeriodYear = 2025, PeriodMonth = 4,
            ThirteenthMonthAccumulated = 2366.65m
        };
        var ti = new PayrollSaldo
        {
            Id = 1, CompanyProfileId = 10, PeriodYear = 2025, PeriodMonth = 3,
            ThirteenthMonthAccumulated = 1366.65m
        };
        var gewaehlt = PayrollCalculations.WaehleVormonatsSaldo(
            new[] { ti, lu }, companyProfileId: 20, year: 2025, month: 5);
        Assert.Same(lu, gewaehlt);
    }

    [Fact]
    public void Luecke_NimmtLetztenSaldoDesMa_AuchInDerselbenFiliale()
    {
        var februar = new PayrollSaldo
        {
            Id = 4, CompanyProfileId = 20, PeriodYear = 2025, PeriodMonth = 2,
            ThirteenthMonthAccumulated = 800m
        };
        var gewaehlt = PayrollCalculations.WaehleVormonatsSaldo(
            new[] { februar }, companyProfileId: 20, year: 2025, month: 4);
        Assert.Same(februar, gewaehlt);
    }

    [Fact]
    public void Januar_NimmtDezemberDesVorjahres_AuchAndereFiliale()
    {
        var dez = new PayrollSaldo
        {
            Id = 9, EmployeeId = 25, CompanyProfileId = 10,
            PeriodYear = 2025, PeriodMonth = 12,
            ThirteenthMonthAccumulated = 4200m, FerienTageSaldo = 8.75m, HourSaldo = -12m
        };
        var aelter = new PayrollSaldo
        {
            Id = 8, EmployeeId = 25, CompanyProfileId = 10,
            PeriodYear = 2025, PeriodMonth = 11,
            ThirteenthMonthAccumulated = 3500m
        };
        var gewaehlt = PayrollCalculations.WaehleVormonatsSaldo(
            new[] { aelter, dez }, companyProfileId: 20, year: 2026, month: 1);
        Assert.Same(dez, gewaehlt);
    }

    [Fact]
    public void GleicherMonatZweiFilialen_AktuelleFilialeGewinnt()
    {
        var alt = new PayrollSaldo
        {
            Id = 1, CompanyProfileId = 10, PeriodYear = 2025, PeriodMonth = 12,
            ThirteenthMonthAccumulated = 100m
        };
        var hier = new PayrollSaldo
        {
            Id = 2, CompanyProfileId = 20, PeriodYear = 2025, PeriodMonth = 12,
            ThirteenthMonthAccumulated = 200m
        };
        var gewaehlt = PayrollCalculations.WaehleVormonatsSaldo(
            new[] { alt, hier }, companyProfileId: 20, year: 2026, month: 1);
        Assert.Same(hier, gewaehlt);
    }
}
