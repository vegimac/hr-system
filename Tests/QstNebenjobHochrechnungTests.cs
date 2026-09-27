using HrSystem.Models;
using Xunit;
using static HrSystem.Services.PayrollCalculations;

namespace HrSystem.Tests;

/// <summary>
/// KS 45 Variante B: satzbestimmend = IST × (Eigen + Andere) / Eigen — OHNE Deckel bei 100 %
/// (ESTV-FAQ zum KS 45 Ziff. 7.3.2: «Dieser kann auch grösser als 100% sein»).
/// TF17 Binggeli Jan 2025: 70 % hier + 30 % andere AG = 100 % → 4'550 → 6'500.
/// Bei Stundenlöhnern ist der eigene Grad = Stunden ÷ betriebsübliche Monats-Arbeitszeit
/// (Wochenstunden × 52 ÷ 12), nicht ÷ 180 — die 180 gelten nur ohne monatliche Lohnzahlung
/// (KS 45 Ziff. 6.4, FAQ 7.3.3).
/// </summary>
public class QstNebenjobHochrechnungTests
{
    private static EmployeeQuellensteuer Qst(decimal anderePct) => new()
    {
        WeitereBeschaftigungen = true,
        GesamtpensumWeitereAg = anderePct,
    };

    private static readonly CompanyProfile Firma = new();

    [Fact]
    public void Binggeli_70_plus_30_gibt_Vollpensum_6500()
    {
        var satz = ComputeSatzBruttoForNebenjob(Qst(30), 4550m, 0, Firma, pensumPct: 70);
        Assert.Equal(6500.00m, satz);
    }

    [Fact]
    public void Unter_100_Prozent_rechnet_auf_die_Summe()
    {
        var satz = ComputeSatzBruttoForNebenjob(Qst(20), 2000m, 0, Firma, pensumPct: 40);
        Assert.Equal(3000.00m, satz);
    }

    [Fact]
    public void Ueber_100_Prozent_wirdNichtGedeckelt()
    {
        // 60 % hier + 50 % anderswo = 110 %: 2'000 × 110 / 60 = 3'666.67.
        // Frueher auf 100 % gekappt (3'333.33) — das war eine Annahme ohne Quelle.
        var satz = ComputeSatzBruttoForNebenjob(Qst(50), 2000m, 0, Firma, pensumPct: 60);
        Assert.Equal(3666.67m, satz);
    }

    /// <summary>
    /// TF18 Blanc: Stundenlohn, 60 % bei anderen Arbeitgebern, Filiale mit 42-Stunden-Woche
    /// (= 182 Monatsstunden). Drei Monate gegen RefXML `AscertainedTaxableEarning`.
    /// </summary>
    [Theory]
    [InlineData(35, 30.0, 1179.45, 4859.35)]   // Januar bis August
    [InlineData(75, 35.0, 2948.65, 7241.90)]   // September — Gesamtpensum 101 %, kein Deckel
    [InlineData(62, 35.0, 2437.55, 6730.80)]   // Oktober
    public void Blanc_Stundenlohn_MitBetriebsueblicherArbeitszeit(decimal stunden, decimal ansatz, decimal ist, decimal erwartet)
    {
        var firma42 = new CompanyProfile { NormalWeeklyHours = 42m };
        var satz = ComputeSatzBruttoForNebenjob(Qst(60), ist, stunden, firma42);
        Assert.NotNull(satz);
        Assert.InRange(satz!.Value, erwartet - 0.05m, erwartet + 0.05m);
        Assert.Equal(ansatz, Math.Round(ist / stunden / 1.1233m, 0));   // Kontrolle: Stundenansatz
    }

    [Fact]
    public void Monatsstunden_KommenAusDerFiliale_NichtAusEinerKonstante()
    {
        Assert.Equal(182.00m, MonatsstundenVollzeit(new CompanyProfile { NormalWeeklyHours = 42m }));
        Assert.Equal(173.33m, MonatsstundenVollzeit(new CompanyProfile { NormalWeeklyHours = 40m }));
        // Ohne Angabe bewusst die L-GAV-Woche, nicht die alten 180 h.
        Assert.Equal(182.00m, MonatsstundenVollzeit(new CompanyProfile()));
    }

    [Fact]
    public void Arbenz_Bonus_bleibt_100_Prozent()
    {
        // 6'000 × 80/60 + 20'000 Bonus = 28'000, nicht 26'000 × 80/60.
        var satz = ComputeSatzBruttoForNebenjob(
            Qst(20), 26000m, 0, Firma, pensumPct: 60, einmaligNichtHochrechnen: 20000);
        Assert.Equal(28000.00m, satz);
    }

    [Fact]
    public void Ohne_Bonus_nur_Monatslohn_hochrechnen()
    {
        var satz = ComputeSatzBruttoForNebenjob(Qst(20), 6000m, 0, Firma, pensumPct: 60);
        Assert.Equal(8000.00m, satz);
    }
}
