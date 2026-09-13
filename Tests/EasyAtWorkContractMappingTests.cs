using System;
using System.Collections.Generic;
using System.Linq;
using HrSystem.Models;
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

    // Ohne Typ-Name: week + 17 = FLEX (Default). Mit Typ MTP/TPM: MTP — auch bei 17 Std.
    [Fact]
    public void Week17_OhneTyp_BleibtFlex()
    {
        var c = new EawContract { AmountType = "week", Amount = 17m };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "hour", Rate = 20.40m, FromRaw = "2026-01-01" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag);

        Assert.Equal("FLEX", info.EmploymentModel);
        Assert.Equal(20.40m, info.HourlyRate);
        Assert.Null(info.EmploymentPercentage);
        Assert.Null(info.MonthlySalaryFte);
    }

    // Walter 02.08.2026: MTP mit 17 Std/Woche (Fall 580046) — Typ führend, nicht Stunden.
    [Fact]
    public void Week17_MitTypMtp_IstMtp()
    {
        var c = new EawContract
        {
            Type = "MTP/TPM",
            AmountType = "week",
            Amount = 17m,
        };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "hour", Rate = 21.66m, FromRaw = "2026-01-01" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag);

        Assert.Equal("MTP", info.EmploymentModel);
        Assert.Equal(17m, info.GuaranteedHoursPerWeek);
        Assert.Equal(21.66m, info.HourlyRate);
    }

    [Fact]
    public void ApplyContractTypeNames_FuelltTypeAusTypeId()
    {
        var c = new EawContract { TypeId = 105, AmountType = "week", Amount = 17m };
        EasyAtWorkEmployeeSyncService.ApplyContractTypeNames(
            new[] { c }, new Dictionary<int, string> { [105] = "MTP/TPM" });
        Assert.Equal("MTP/TPM", c.Type);

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, new List<EawPayRate>
        {
            new EawPayRate { Type = "hour", Rate = 21.66m, FromRaw = "2026-01-01" },
        }, Stichtag);
        Assert.Equal("MTP", info.EmploymentModel);
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

    [Fact]
    public void MtpTpm25_ImportiertVertragsbeginnFunktionUndStunden()
    {
        var c = new EawContract
        {
            Type = "MTP/TPM",
            Title = "HOST_CT",
            AmountType = "week",
            Amount = 25m,
            FromRaw = "2025-10-01",
        };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "hour", Rate = 21.66m, FromRaw = "2026-01-01" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag);

        Assert.Equal("MTP", info.EmploymentModel);
        Assert.Equal("hourly", info.SalaryType);
        Assert.Equal("MTP/TPM", info.ContractType);
        Assert.Equal("HOST_CT", info.JobTitle);
        Assert.Equal(new DateOnly(2025, 10, 1), info.ContractFrom);
        Assert.Equal(new DateOnly(2026, 1, 1), info.RateFrom);
        Assert.Equal(25m, info.GuaranteedHoursPerWeek);
        Assert.Equal(21.66m, info.HourlyRate);
        Assert.Null(info.EmploymentPercentage);
    }

    // Erfassungsfehler (Walter-Vorgabe 08.07.2026): FLEX/MTP haben IMMER Stunden
    // pro WOCHE — «Flex, 17.00, Monat» ist ein easy@work-Erfassungsfehler und
    // darf NIE importiert werden (Fall Beza 750080: wurde still zu FIX ohne
    // Monatslohn klassifiziert und blieb als Dauer-Hinweis haengen).
    [Fact]
    public void FlexMitStundenProMonat_IstErfassungsfehler()
    {
        var c = new EawContract { Type = "Flex", AmountType = "month", Amount = 17m };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "hour", Rate = 20.40m, FromRaw = "2026-04-01" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag);

        Assert.NotNull(info.DataError);
        Assert.Equal("FLEX", info.EmploymentModel);   // Anzeige nach Typ, importiert wird nichts
    }

    [Fact]
    public void FlexMitStundenProWoche_IstKeinFehler()
    {
        var c = new EawContract { Type = "Flex", AmountType = "week", Amount = 17m };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "hour", Rate = 20.40m, FromRaw = "2026-04-01" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, Stichtag);

        Assert.Null(info.DataError);
        Assert.Equal("FLEX", info.EmploymentModel);
        Assert.Equal("hourly", info.SalaryType);
        Assert.Equal(20.40m, info.HourlyRate);
    }

    // ───── STRICT-Validierungen (Walter-Vorgabe 08.07.2026) ─────

    [Fact]
    public void UeberlappendeVertraege_WerdenAmVortagGeschlossen()
    {
        var contracts = new List<EawContract>
        {
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2025-10-10", ToRaw = "2026-04-01" },
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2026-04-01" },
        };
        Assert.Null(EasyAtWorkEmployeeSyncService.ValidateContractOverlaps(contracts));
        Assert.Equal(new DateOnly(2026, 3, 31), contracts[0].To);
    }

    [Fact]
    public void OffenerAltVertrag_WirdAmVortagDesNeuenGeschlossen()
    {
        var contracts = new List<EawContract>
        {
            new() { Type = "MTP/TPM", AmountType = "week", Amount = 34m, FromRaw = "2025-09-01" },
            new() { Type = "Fix", AmountType = "percent", Amount = 100m, Percentage = 100m, FromRaw = "2026-10-01" },
        };
        Assert.Null(EasyAtWorkEmployeeSyncService.ValidateContractOverlaps(contracts));
        Assert.Equal(new DateOnly(2026, 9, 30), contracts[0].To);
        Assert.Null(contracts[1].To);
    }

    [Fact]
    public void NahtloseVertraege_SindKeinFehler()
    {
        var contracts = new List<EawContract>
        {
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2025-10-10", ToRaw = "2026-03-31" },
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2026-04-01" },
        };
        Assert.Null(EasyAtWorkEmployeeSyncService.ValidateContractOverlaps(contracts));
    }

    [Fact]
    public void FlexOhneLohn_IstErfassungsfehler()
    {
        var c = new EawContract { Type = "Flex", AmountType = "week", Amount = 17m };
        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, new List<EawPayRate>(), Stichtag);
        Assert.NotNull(info.DataError);
        Assert.Contains("Stundenlohn", info.DataError);
    }

    [Fact]
    public void FixOhneLohn_IstErfassungsfehler()
    {
        var c = new EawContract { AmountType = "percent", Amount = 100m, Percentage = 100m };
        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, new List<EawPayRate>(), Stichtag, isKader: false);
        Assert.NotNull(info.DataError);
        Assert.Contains("Monatslohn", info.DataError);
    }

    [Fact]
    public void MtpWoche_BleibtMtp_AuchWennAktuellePositionKader()
    {
        var c = new EawContract
        {
            Type = "MTP/TPM",
            AmountType = "week",
            Amount = 34m,
            FromRaw = "2025-09-01",
            ToRaw = "2026-09-30",
        };
        var rates = new List<EawPayRate>
        {
            new EawPayRate { Type = "hour", Rate = 21.66m, FromRaw = "2026-01-01", ToRaw = "2026-09-30" },
        };

        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, rates, new DateOnly(2026, 1, 1), isKader: true);

        Assert.Null(info.DataError);
        Assert.Equal("MTP", info.EmploymentModel);
        Assert.Equal("hourly", info.SalaryType);
        Assert.Equal(34m, info.GuaranteedHoursPerWeek);
        Assert.Equal(21.66m, info.HourlyRate);
    }

    [Fact]
    public void Befoerderung_MtpDannFix_KaderGiltNurAbFix()
    {
        var contracts = new List<EawContract>
        {
            new() { Type = "MTP/TPM", AmountType = "week", Amount = 34m, FromRaw = "2025-09-01" },
            new() { Type = "Fix", AmountType = "percent", Amount = 100m, Percentage = 100m, FromRaw = "2026-10-01" },
        };
        var rates = new List<EawPayRate>
        {
            new() { Type = "hour", Rate = 21.50m, FromRaw = "2025-01-01", ToRaw = "2025-12-31" },
            new() { Type = "hour", Rate = 21.66m, FromRaw = "2026-01-01", ToRaw = "2026-09-30" },
            new() { Type = "month", Rate = 4300m, FromRaw = "2026-10-01" },
        };

        var tl = EasyAtWorkEmployeeSyncService.BuildEmploymentTimeline(
            contracts, rates, new DateOnly(2026, 9, 13), isKader: true);

        Assert.All(tl, s => Assert.Null(s.Info.DataError));
        Assert.Contains(tl, s => s.Info.EmploymentModel == "MTP");
        var mtp = tl.First(s => s.Start == new DateOnly(2026, 1, 1));
        Assert.Equal("MTP", mtp.Info.EmploymentModel);
        Assert.Equal(34m, mtp.Info.GuaranteedHoursPerWeek);
        Assert.Equal(21.66m, mtp.Info.HourlyRate);

        var fix = tl.First(s => s.Start >= new DateOnly(2026, 10, 1));
        Assert.Equal("FIX-M", fix.Info.EmploymentModel);
        Assert.Equal(100m, fix.Info.EmploymentPercentage);
        Assert.Equal(4300m, fix.Info.MonthlySalary);
        Assert.Equal(new DateOnly(2026, 9, 30), contracts[0].To);
        Assert.True(tl.Where(s => s.Info.EmploymentModel == "MTP").All(s => s.End <= new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void OffenerMtp_WirdAmVortagDesFixM_Geschlossen()
    {
        var rows = new List<Employment>
        {
            new()
            {
                Id = 1, ContractStartDate = new DateTime(2026, 1, 1),
                ContractEndDate = null, IsActive = true, EmploymentModel = "MTP",
            },
            new()
            {
                Id = 2, ContractStartDate = new DateTime(2026, 10, 1),
                ContractEndDate = null, IsActive = true, EmploymentModel = "FIX-M",
            },
        };
        EasyAtWorkEmployeeSyncService.SchliesseEmploymentVorgaengerAmTagVorNachfolger(
            rows, firstAllowedDate: new DateOnly(2026, 9, 1), today: new DateOnly(2026, 9, 13));

        Assert.Equal(new DateTime(2026, 9, 30), rows[0].ContractEndDate);
        Assert.True(rows[0].IsActive);
        Assert.Null(rows[1].ContractEndDate);
    }

    [Fact]
    public void FixM_OhneLohn_IstLegal()
    {
        // GF-Fall: FIX-M darf ohne Lohn sein (vertraulich, wird in OneCrew erfasst).
        var c = new EawContract { AmountType = "percent", Amount = 100m, Percentage = 100m };
        var info = EasyAtWorkEmployeeSyncService.ComputeContractInfo(c, new List<EawPayRate>(), Stichtag, isKader: true);
        Assert.Null(info.DataError);
        Assert.Equal("FIX-M", info.EmploymentModel);
    }

    [Fact]
    public void HistorischeUeberlappung_WirdIgnoriert_AktiveGemeldet()
    {
        // Rein historische Ueberlappung (beide Vertraege abgelaufen) → kein Fehler
        // (Walter 08.07.2026: Historie lebt im alten Lohnprogramm).
        var vergangen = new List<EawContract>
        {
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2023-01-01", ToRaw = "2023-06-30" },
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2023-06-30", ToRaw = "2023-12-31" },
        };
        Assert.Null(EasyAtWorkEmployeeSyncService.ValidateContractOverlaps(vergangen, Stichtag));

        // Dieselbe Ueberlappung, aber der zweite Vertrag laeuft noch → Vorgänger endet am Vortag.
        var aktiv = new List<EawContract>
        {
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2023-01-01", ToRaw = "2023-06-30" },
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2023-06-30" },
        };
        Assert.Null(EasyAtWorkEmployeeSyncService.ValidateContractOverlaps(aktiv, Stichtag));
        Assert.Equal(new DateOnly(2023, 6, 29), aktiv[0].To);
    }
}



