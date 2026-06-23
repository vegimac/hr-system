using System;
using System.Collections.Generic;
using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Tests für die reine Vertrags-Mapping-Logik
/// <see cref="EasyAtWorkEmployeeSyncService.ComputeContractInfo"/> (Walter-Vorgabe
/// 23.06.2026). Schwerpunkt: easy@work liefert beim Monatslohn den EFFEKTIVEN
/// Pensumslohn; bei uns ist MonthlySalaryFte IMMER der 100%-Lohn → hochgerechnet.
/// amount_type "percent" ist ein Pensum-Monatslohnvertrag (FIX), kein Stundenlohn.
/// </summary>
public class EasyAtWorkContractMappingTests
{
    private static readonly DateOnly Stichtag = new(2026, 6, 23);

    // Beispiel Amire: percent / 60% / pay_rate month 2760, Position SHIFT_LEADER_7_PLUS.
    [Fact]
    public void Percent60_MonatslohnKader_RechnetFteHoch()
    {
        var c = new EawContract { AmountType = "percent", Amount = 60m, Percentage = 60m };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "month", Rate = 2760m, FromRaw = "2026-01-01 00:00:00" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag, isKader: true);

        Assert.Equal("FIX-M", info.EmploymentModel);
        Assert.Equal("monthly", info.SalaryType);
        Assert.Equal(60m, info.EmploymentPercentage);
        Assert.Equal(4600m, info.MonthlySalaryFte);   // 2760 / 60 × 100
        Assert.Equal(2760m, info.MonthlySalary);       // effektiver Pensumslohn
        Assert.Null(info.HourlyRate);
        Assert.Null(info.GuaranteedHoursPerWeek);
    }

    // Ohne Kader-Position bleibt das Modell FIX (Monatslohn), Felder identisch.
    [Fact]
    public void Percent60_OhneKader_IstFix()
    {
        var c = new EawContract { AmountType = "percent", Amount = 60m, Percentage = 60m };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "month", Rate = 2760m, FromRaw = "2026-01-01" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag, isKader: false);

        Assert.Equal("FIX", info.EmploymentModel);
        Assert.Equal("monthly", info.SalaryType);
        Assert.Equal(60m, info.EmploymentPercentage);
        Assert.Equal(4600m, info.MonthlySalaryFte);
        Assert.Equal(2760m, info.MonthlySalary);
        Assert.Null(info.HourlyRate);
    }

    // 100%-Vertrag: effektiv = FTE.
    [Fact]
    public void Percent100_FteGleichEffektiv()
    {
        var c = new EawContract { AmountType = "percent", Amount = 100m, Percentage = 100m };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "month", Rate = 5000m, FromRaw = "2025-01-01" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag);

        Assert.Equal("FIX", info.EmploymentModel);
        Assert.Equal(100m, info.EmploymentPercentage);
        Assert.Equal(5000m, info.MonthlySalaryFte);
        Assert.Equal(5000m, info.MonthlySalary);
    }

    // Stundenlohn-Verträge bleiben unberührt: week + 17 = UTP, week + 21 = MTP.
    [Fact]
    public void Week17_BleibtUtp()
    {
        var c = new EawContract { AmountType = "week", Amount = 17m };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "hour", Rate = 20.40m, FromRaw = "2026-01-01" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag);

        Assert.Equal("UTP", info.EmploymentModel);
        Assert.Equal(20.40m, info.HourlyRate);
        Assert.Null(info.EmploymentPercentage);
        Assert.Null(info.MonthlySalaryFte);
    }

    [Fact]
    public void Week21_IstMtpMitGarantierterStunden()
    {
        var c = new EawContract { AmountType = "week", Amount = 21m };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "hour", Rate = 20.40m, FromRaw = "2026-01-01" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag);

        Assert.Equal("MTP", info.EmploymentModel);
        Assert.Equal(20.40m, info.HourlyRate);
        Assert.Equal(21m, info.GuaranteedHoursPerWeek);
        Assert.Null(info.EmploymentPercentage);
    }
}
