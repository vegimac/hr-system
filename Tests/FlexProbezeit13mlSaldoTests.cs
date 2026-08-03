using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// FLEX 13. ML während Probezeit → Saldo mitführen; nach Probezeit Auszahlung.
/// Walter-Vorgabe 01.08.2026 / L-GAV Art. 12 Ziff. 2.
/// Prüft die SaldoBlock→BuildResult-Math (ohne volle Engine/DB).
/// </summary>
public class FlexProbezeit13mlSaldoTests
{
    private static Employee Emp() => new()
    {
        Id = 1, FirstName = "Melike", LastName = "Toprak",
        Street = "Test", ZipCode = "4665", City = "Oftringen"
    };

    private static Employment Contract() => new()
    {
        Id = 1, EmployeeId = 1, EmploymentModel = "FLEX",
        HourlyRate = 20.40m, IsActive = true
    };

    private static CompanyProfile Company() => new()
    {
        Id = 1, CompanyName = "Test GmbH", BranchName = "Oftringen",
        Street = "Bahnhof", HouseNumber = "1", ZipCode = "4665", City = "Oftringen",
        DefaultThirteenthSalaryPercent = 8.33m
    };

    private static IDictionary<string, object?> Build(SaldoBlock saldo, int month, decimal totalLohn)
    {
        var days = DateTime.DaysInMonth(2026, month);
        var anon = PayrollCalculations.BuildResult(
            Emp(), Contract(), Company(), 2026, month,
            new DateOnly(2026, month, 1), new DateOnly(2026, month, days),
            new List<object>(), new List<object>(),
            new List<DeductionRule>(), totalLohn,
            new SvBases(totalLohn, totalLohn, totalLohn, totalLohn, totalLohn),
            new List<object>(), 0, new List<object>(), 0,
            new List<object>(), 0, saldo,
            new List<EmployeeLohnAssignment>(),
            new List<EmployeeBankAccount>());
        // Anonymous type → Dict via Reflection
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in anon.GetType().GetProperties())
            dict[p.Name] = p.GetValue(anon);
        return dict;
    }

    [Fact]
    public void Probezeit_Saldo_VormonatPlusMonatszuwachs()
    {
        // Vormonat-Pott 80, Basis 13.ML 2000 → Monatszuwachs 166.60, Saldo 246.60
        var saldo = new SaldoBlock(
            VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 100,
            SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
            NightHours: 0, NightBonus: 0, NachtKompStunden: 0,
            VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
            VacationWeeks: 5, VormonatFerienTage: 0, FerienTageAccrual: 0,
            FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
            VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
            VormonatFeiertagTage: 0, FeiertagTageAccrual: 0,
            FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
            ThirteenthPct: 8.33m,
            PrevThirteenth: 80m,
            Basis13ml: 2000m);

        var r = Build(saldo, 5, 2000m);
        Assert.Equal(166.60m, (decimal)r["thirteenthMonthly"]!);
        Assert.Equal(246.60m, (decimal)r["thirteenthAccumulated"]!);
    }

    [Fact]
    public void NachProbezeit_KeinStehenderSaldo()
    {
        var saldo = new SaldoBlock(
            VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 100,
            SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
            NightHours: 0, NightBonus: 0, NachtKompStunden: 0,
            VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
            VacationWeeks: 5, VormonatFerienTage: 0, FerienTageAccrual: 0,
            FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
            VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
            VormonatFeiertagTage: 0, FeiertagTageAccrual: 0,
            FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
            ThirteenthPct: 0m,
            PrevThirteenth: 0m,
            ThirteenthPrevForDisplay: 246.60m,
            ThirteenthAccrualForDisplay: 0m,
            ThirteenthPayout: 246.60m,
            Basis13ml: 0m);

        var r = Build(saldo, 6, 2200m);
        Assert.Equal(0m, (decimal)r["thirteenthMonthly"]!);
        Assert.Equal(0m, (decimal)r["thirteenthAccumulated"]!);
        Assert.Equal(246.60m, (decimal)r["thirteenthPayout"]!);
    }
}
