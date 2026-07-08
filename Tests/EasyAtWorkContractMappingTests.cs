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

        Assert.Equal("FLEX", info.EmploymentModel);
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
    public void UeberlappendeVertraege_SindErfassungsfehler()
    {
        // Ende 1.4. + neuer Beginn 1.4. = 1 Tag Ueberlappung → Fehler.
        var contracts = new List<EawContract>
        {
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2025-10-10", ToRaw = "2026-04-01" },
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2026-04-01" },
        };
        var err = EasyAtWorkEmployeeSyncService.ValidateContractOverlaps(contracts);
        Assert.NotNull(err);
        Assert.Contains("überschneiden", err);
    }

    [Fact]
    public void OffenerAltVertrag_MitFolgevertrag_IstErfassungsfehler()
    {
        var contracts = new List<EawContract>
        {
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2025-10-10" },   // offen!
            new() { Type = "Fix",  AmountType = "percent", Amount = 100m, FromRaw = "2026-04-01" },
        };
        var err = EasyAtWorkEmployeeSyncService.ValidateContractOverlaps(contracts);
        Assert.NotNull(err);
        Assert.Contains("OFFEN", err);
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

        // Dieselbe Ueberlappung, aber der zweite Vertrag laeuft noch → Fehler.
        var aktiv = new List<EawContract>
        {
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2023-01-01", ToRaw = "2023-06-30" },
            new() { Type = "Flex", AmountType = "week", Amount = 17m, FromRaw = "2023-06-30" },
        };
        Assert.NotNull(EasyAtWorkEmployeeSyncService.ValidateContractOverlaps(aktiv, Stichtag));
    }
}



