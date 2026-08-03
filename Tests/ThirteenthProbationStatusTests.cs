using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// L-GAV Art. 12 Ziff. 2 / Walter 01.08.2026:
/// Probezeit am Periodenende = bestanden; Verfall nur bei Austritt ≤ Probezeit.
/// </summary>
public class ThirteenthProbationStatusTests
{
    private static readonly DateOnly JulFrom = new(2026, 7, 1);
    private static readonly DateOnly JulTo   = new(2026, 7, 31);

    [Fact]
    public void ProbezeitAmPeriodenende_Bestanden_KeinVerfall()
    {
        // Ljubinka: Probezeit bis 31.7., befristet danach (z.B. 31.10.)
        var (inPz, forfeit) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2026, 7, 31),
            austritt:     new DateOnly(2026, 10, 31),
            periodFrom:   JulFrom,
            periodToFull: JulTo);

        Assert.False(inPz);
        Assert.False(forfeit);
    }

    [Fact]
    public void ProbezeitAmPeriodenende_OhneAustritt_Bestanden()
    {
        var (inPz, forfeit) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2026, 7, 31),
            austritt:     null,
            periodFrom:   JulFrom,
            periodToFull: JulTo);

        Assert.False(inPz);
        Assert.False(forfeit);
    }

    [Fact]
    public void NochInProbezeit_Akkumulieren()
    {
        var (inPz, forfeit) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2026, 8, 15),
            austritt:     null,
            periodFrom:   JulFrom,
            periodToFull: JulTo);

        Assert.True(inPz);
        Assert.False(forfeit);
    }

    [Fact]
    public void AustrittGleichProbezeit_Verfall()
    {
        var (inPz, forfeit) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2026, 7, 31),
            austritt:     new DateOnly(2026, 7, 31),
            periodFrom:   JulFrom,
            periodToFull: JulTo);

        Assert.False(inPz);
        Assert.True(forfeit);
    }

    [Fact]
    public void AustrittVorProbezeitende_Verfall()
    {
        var (inPz, forfeit) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2026, 7, 31),
            austritt:     new DateOnly(2026, 7, 15),
            periodFrom:   JulFrom,
            periodToFull: JulTo);

        Assert.False(inPz);
        Assert.True(forfeit);
    }

    [Fact]
    public void BefristetNachProbezeit_KeinVerfallImProbezeitMonat()
    {
        // Austritt 31.10. liegt nicht in Juli → kein Verfall; Probezeit am 31.7. bestanden
        var (inPz, forfeit) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2026, 7, 31),
            austritt:     new DateOnly(2026, 10, 31),
            periodFrom:   JulFrom,
            periodToFull: JulTo);

        Assert.False(forfeit);
        Assert.False(inPz);
    }

    [Theory]
    [InlineData("2026-07-10", "2026-07-31", "2026-07-10")]
    [InlineData("2026-07-31", "2026-07-10", "2026-07-10")]
    [InlineData(null, "2026-07-15", "2026-07-15")]
    [InlineData("2026-08-01", null, "2026-08-01")]
    public void ResolveAustrittDate_NimmtFrueheres(string? exitIso, string? contractIso, string expectedIso)
    {
        DateTime? exit = exitIso == null ? null : DateTime.Parse(exitIso);
        DateTime? ce   = contractIso == null ? null : DateTime.Parse(contractIso);
        var got = PayrollCalculations.ResolveAustrittDate(exit, ce);
        Assert.Equal(DateOnly.Parse(expectedIso), got);
    }
}
