using HrSystem.Controllers;
using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Walter-Entscheidung 04.08.2026: 13.-ML-Alt-Saldo (Code 906) auch für FLEX —
/// importierter Mirus-Alt-Saldo läuft über denselben 13.-Saldo wie der
/// Probezeit-Pot (prevThirteenth). Auszahlungs-Trigger vereinheitlicht:
///   1) erster Lohn nach bestandener Probezeit («Nachzahlung nach Probezeit»),
///   2) letzter Lohn (Austritts-Schlussabrechnung),
///   3) spätestens Dezember — sonst weitertragen.
/// Verfall bei Austritt IN der Probezeit gilt für den GANZEN Saldo (L-GAV).
/// Referenzfall: Patricia Rei Rodrigues Sobreira, Mirus-Alt-Saldo 278.54,
/// Austritt 31.07.2026 → «13. Monatslohn (Saldo-Auszahlung)» 278.54.
/// </summary>
public class FlexThirteenthAltSaldoTests
{
    // ── 1) Import-Relevanz: 906 jetzt auch FLEX (inkl. Legacy-Alias UTP) ────

    [Theory]
    [InlineData("FLEX")]
    [InlineData("UTP")]   // Legacy-Alias
    [InlineData("MTP")]
    [InlineData("FIX")]
    [InlineData("FIX-M")]
    public void Vortrag_906_ist_fuer_alle_Modelle_relevant(string model)
    {
        Assert.True(SaldoVortragImportController.IsDreizehnterVortragRelevantForModel(model));
        Assert.True(SaldoVortragController.IsVortragRelevantForModel("906", model));
    }

    // ── 2) Auszahlungs-Trigger + exakte Fibu-v3-Labels ──────────────────────

    [Fact]
    public void Nachzahlung_nach_Probezeit_hat_Vorrang_und_richtiges_Label()
    {
        var (payout, label) = PayrollCalculations.ResolveFlexThirteenthSaldoPayout(
            probationEndsThisPeriod: true, isLetzterLohn: false, month: 5);
        Assert.True(payout);
        Assert.Equal("13. Monatslohn (Nachzahlung nach Probezeit)", label);

        // Auch wenn gleichzeitig Dezember/letzter Lohn: Nachzahlung-Label gewinnt.
        var (p2, l2) = PayrollCalculations.ResolveFlexThirteenthSaldoPayout(
            probationEndsThisPeriod: true, isLetzterLohn: true, month: 12);
        Assert.True(p2);
        Assert.Equal("13. Monatslohn (Nachzahlung nach Probezeit)", l2);
    }

    [Fact]
    public void LetzterLohn_zahlt_Saldo_aus()
    {
        // Referenz Patricia: Austritt 31.07. → Juli-Lauf zahlt den Alt-Saldo.
        var (payout, label) = PayrollCalculations.ResolveFlexThirteenthSaldoPayout(
            probationEndsThisPeriod: false, isLetzterLohn: true, month: 7);
        Assert.True(payout);
        Assert.Equal("13. Monatslohn (Saldo-Auszahlung)", label);
    }

    [Fact]
    public void Dezember_zahlt_Saldo_spaetestens_aus()
    {
        var (payout, label) = PayrollCalculations.ResolveFlexThirteenthSaldoPayout(
            probationEndsThisPeriod: false, isLetzterLohn: false, month: 12);
        Assert.True(payout);
        Assert.Equal("13. Monatslohn (Saldo-Auszahlung)", label);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(11)]
    public void Normaler_Monat_traegt_Saldo_weiter(int month)
    {
        var (payout, _) = PayrollCalculations.ResolveFlexThirteenthSaldoPayout(
            probationEndsThisPeriod: false, isLetzterLohn: false, month: month);
        Assert.False(payout);
    }

    [Fact]
    public void Labels_entsprechen_exakt_den_Fibu_v3_Prefixen()
    {
        // ExtractBruttoUmgliederung bucht NUR diese zwei Muster als RST-Abbau
        // (S 2017/2016 / H 1920) — die Engine-Labels müssen exakt matchen.
        var (_, nachzahlung) = PayrollCalculations.ResolveFlexThirteenthSaldoPayout(true,  false, 5);
        var (_, saldoAusz)   = PayrollCalculations.ResolveFlexThirteenthSaldoPayout(false, true,  7);
        Assert.Contains(nachzahlung, FibuJournalService.Ml13AuszahlungPrefixes);
        Assert.Contains(saldoAusz,   FibuJournalService.Ml13AuszahlungPrefixes);
    }

    // ── 3) Verfall in Probezeit: gilt für den GANZEN Saldo inkl. Alt-Saldo ──

    [Fact]
    public void Austritt_in_Probezeit_verfaellt_auch_Alt_Saldo()
    {
        // Probezeit bis 30.09., Austritt 15.08. → Forfeited in der August-
        // Periode; die Engine setzt dann prevThirteenthForSaldoUtp = 0 —
        // der importierte 906-Vortrag verfällt mit (L-GAV, Walter 04.08.2026).
        var (inProbation, forfeited) = PayrollCalculations.ResolveThirteenthProbationStatus(
            probationEnd: new DateOnly(2026, 9, 30),
            austritt:     new DateOnly(2026, 8, 15),
            periodFrom:   new DateOnly(2026, 8, 1),
            periodToFull: new DateOnly(2026, 8, 31));
        Assert.False(inProbation);
        Assert.True(forfeited);
    }

    // ── 4) SaldoBlock→BuildResult-Math (Anzeige, ohne Engine/DB) ────────────

    private static Employee Emp() => new()
    {
        Id = 1, FirstName = "Patricia", LastName = "Rei Rodrigues Sobreira",
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
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in anon.GetType().GetProperties())
            dict[p.Name] = p.GetValue(anon);
        return dict;
    }

    private static SaldoBlock BaseSaldo(
        decimal pct, decimal prev, decimal basis13,
        decimal? prevDisp = null, decimal? accrualDisp = null, decimal? payout = null,
        bool showFlex13 = false) => new(
        VormonatHourSaldo: 0, NeuerHourSaldo: 0, WorkedHours: 100,
        SollStunden: 0, Mehrstunden: 0, AbsenzGutschrift: 0,
        NightHours: 0, NightBonus: 0, NachtKompStunden: 0,
        VormonatNachtSaldo: 0, NeuerNachtSaldo: 0,
        VacationWeeks: 5, VormonatFerienTage: 0, FerienTageAccrual: 0,
        FerienTageGenommen: 0, FerienTageSaldoNeu: 0,
        VormonatFerienGeld: 0, FerienGeldSaldoNeu: 0, FerienGeldAuszahlung: 0,
        VormonatFeiertagTage: 0, FeiertagTageAccrual: 0,
        FeiertagTageGenommen: 0, FeiertagTageSaldoNeu: 0,
        ThirteenthPct: pct,
        PrevThirteenth: prev,
        ThirteenthPrevForDisplay: prevDisp,
        ThirteenthAccrualForDisplay: accrualDisp,
        ThirteenthPayout: payout,
        Basis13ml: basis13,
        ShowFlexThirteenthSaldo: showFlex13);

    [Fact]
    public void AltSaldo_steht_nach_Probezeit_ohne_weiteres_Wachstum()
    {
        // Nach der Probezeit: pct=0 für den Saldo (laufender 13. wird monatlich
        // ausbezahlt), Alt-Saldo 278.54 wird unverändert weitergetragen.
        var r = Build(BaseSaldo(pct: 0m, prev: 278.54m, basis13: 0m, showFlex13: true), 5, 2000m);
        Assert.Equal(0m,      (decimal)r["thirteenthMonthly"]!);
        Assert.Equal(278.54m, (decimal)r["thirteenthAccumulated"]!);
        Assert.True((bool)r["showFlexThirteenthSaldo"]!);
    }

    [Fact]
    public void Austritt_zahlt_AltSaldo_aus_Referenz_Patricia()
    {
        // Juli-Lauf, Austritt 31.07.: Payout 278.54, Saldo neu 0.
        var r = Build(BaseSaldo(pct: 0m, prev: 0m, basis13: 0m,
            prevDisp: 278.54m, accrualDisp: 0m, payout: 278.54m, showFlex13: true), 7, 2697.49m);
        Assert.Equal(278.54m, (decimal)r["thirteenthPayout"]!);
        Assert.Equal(278.54m, (decimal)r["thirteenthPrevForDisplay"]!);
        Assert.Equal(0m,      (decimal)r["thirteenthAccumulated"]!);
    }

    [Fact]
    public void Nachzahlung_nach_Probezeit_inkl_AltSaldo()
    {
        // Probezeit-Pot 246.60 + Alt-Saldo 278.54 = 525.14 — die Engine führt
        // beide in EINEM prevThirteenth und zahlt sie zusammen aus.
        var r = Build(BaseSaldo(pct: 0m, prev: 0m, basis13: 0m,
            prevDisp: 525.14m, accrualDisp: 0m, payout: 525.14m, showFlex13: true), 6, 2200m);
        Assert.Equal(525.14m, (decimal)r["thirteenthPayout"]!);
        Assert.Equal(0m,      (decimal)r["thirteenthAccumulated"]!);
    }

    [Fact]
    public void Flex_ohne_Vortrag_bleibt_unveraendert()
    {
        var r = Build(BaseSaldo(pct: 0m, prev: 0m, basis13: 0m, showFlex13: false), 5, 2000m);
        Assert.Equal(0m, (decimal)r["thirteenthMonthly"]!);
        Assert.Equal(0m, (decimal)r["thirteenthAccumulated"]!);
        Assert.False((bool)r["showFlexThirteenthSaldo"]!);
    }
}
