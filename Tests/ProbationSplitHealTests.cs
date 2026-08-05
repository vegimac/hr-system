using System;
using System.Collections.Generic;
using HrSystem.Models;
using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Sync-Split-Heilung der Probezeit (Walter-Bug 05.08.2026, Dora Mustedanagic):
/// Nach einem easy@work-Sync-Split (1-Tages-Vertrag 27.7. + offener Vertrag ab
/// 28.7.) sass die Probezeit auf dem BEENDETEN Splitter — Anzeige und Lohnlauf
/// lesen aber den aktiven Vertrag, und «Probezeiten nachführen» übersprang den
/// MA («hat ja schon eine»). ResolveProbationTarget/MoveProbation hängen die
/// Probezeit auf den offenen Vertrag um; pro MA existiert genau EINE Probezeit.
/// </summary>
public class ProbationSplitHealTests
{
    private static Employment Emp(int id, string start, string? end,
        DateTime? probEnd = null, DateOnly? probStart = null, int? probMonths = null) => new()
    {
        Id = id,
        ContractStartDate     = DateTime.Parse(start),
        ContractEndDate       = end == null ? null : DateTime.Parse(end),
        ProbationEndDate      = probEnd,
        ProbationStartDate    = probStart,
        ProbationPeriodMonths = probMonths,
    };

    [Fact]
    public void Dora_Szenario_Probezeit_wird_vom_Splitter_auf_offenen_Vertrag_umgehaengt()
    {
        var splitter = Emp(1, "2026-07-27", "2026-07-27",
            probEnd: new DateTime(2026, 10, 26), probMonths: 3);
        var offen = Emp(2, "2026-07-28", null);
        var emps = new List<Employment> { splitter, offen };

        var (target, donor) = ProbationAnchor.ResolveProbationTarget(emps);
        Assert.Same(offen, target);      // Ziel = offener Vertrag
        Assert.Same(splitter, donor);    // Donor = beendeter Splitter mit Probezeit

        Assert.True(ProbationAnchor.MoveProbation(target, donor!));
        // Werte unverändert übernommen (Ende NICHT neu gerechnet) …
        Assert.Equal(new DateTime(2026, 10, 26), offen.ProbationEndDate);
        Assert.Equal(3, offen.ProbationPeriodMonths);
        // … und der Donor ist geleert (genau EINE Probezeit pro MA).
        Assert.Null(splitter.ProbationEndDate);
        Assert.Null(splitter.ProbationPeriodMonths);
        Assert.Null(splitter.ProbationStartDate);
    }

    [Fact]
    public void Verankerte_Probezeit_wandert_mit_Anker_Datum()
    {
        var splitter = Emp(1, "2026-07-27", "2026-07-27",
            probEnd: new DateTime(2026, 10, 26),
            probStart: new DateOnly(2026, 7, 27), probMonths: 3);
        var offen = Emp(2, "2026-07-28", null);

        Assert.True(ProbationAnchor.MoveProbation(offen, splitter));
        Assert.Equal(new DateOnly(2026, 7, 27), offen.ProbationStartDate);
    }

    [Fact]
    public void Ziel_hat_schon_Probezeit_dann_kein_Move()
    {
        var alt   = Emp(1, "2026-01-01", "2026-06-30", probEnd: new DateTime(2026, 3, 31));
        var offen = Emp(2, "2026-07-01", null,          probEnd: new DateTime(2026, 9, 30));

        var (target, donor) = ProbationAnchor.ResolveProbationTarget(new List<Employment> { alt, offen });
        Assert.Same(offen, target);
        // Move verweigert — bestehende Probezeit des Ziels bleibt unangetastet.
        Assert.False(ProbationAnchor.MoveProbation(target, donor!));
        Assert.Equal(new DateTime(2026, 9, 30), offen.ProbationEndDate);
        Assert.Equal(new DateTime(2026, 3, 31), alt.ProbationEndDate);
    }

    [Fact]
    public void Ohne_offenen_Vertrag_ist_der_frueheste_das_Ziel()
    {
        var a = Emp(1, "2026-07-27", "2026-07-27");
        var b = Emp(2, "2026-07-28", "2026-08-31");
        var (target, donor) = ProbationAnchor.ResolveProbationTarget(new List<Employment> { b, a });
        Assert.Same(a, target);
        Assert.Null(donor);
    }

    [Fact]
    public void ComputeEnd_3_Monate_minus_1_Tag()
    {
        // Walters Regel (05.08.2026): erste Stempelung 27.7. → Ende 26.10.
        Assert.Equal(new DateOnly(2026, 10, 26),
            ProbationAnchor.ComputeEnd(new DateOnly(2026, 7, 27), 3));
    }
}
