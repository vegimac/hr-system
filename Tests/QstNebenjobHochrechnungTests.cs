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
    [InlineData(53, 35.0, 2083.70, 6376.90)]   // November
    [InlineData(71, 35.0, 2791.40, 7084.65)]   // Dezember
    public void Blanc_Stundenlohn_MitBetriebsueblicherArbeitszeit(decimal stunden, decimal ansatz, decimal ist, decimal erwartet)
    {
        var firma42 = new CompanyProfile { NormalWeeklyHours = 42m };
        var satz = ComputeSatzBruttoForNebenjob(Qst(60), ist, stunden, firma42);
        Assert.NotNull(satz);
        Assert.InRange(satz!.Value, erwartet - 0.05m, erwartet + 0.05m);
        Assert.Equal(ansatz, Math.Round(ist / stunden / 1.1233m, 0));   // Kontrolle: Stundenansatz
    }

    /// <summary>
    /// Ausfallstunden zaehlen zum eigenen Beschaeftigungsgrad. Beispiel aus
    /// Swissdec, Richtlinien fuer Lohndatenverarbeitung ELM 6.0, Ausgabe 06.03.2026,
    /// Kap. 10.6.4.3, gedruckte S. 287 (PDF-Seite 300):
    /// `docs/swissdec/ELM_6.0_Richtlinien_Lohndatenverarbeitung_20260306.pdf`.
    /// 3 gearbeitete Stunden + 32 Ausfallstunden (Unfall) = 35 → 35/182 = 19.23 %;
    /// andere AG 60 % → Total 79.23 %; Brutto 858.00 (90.00 Stundenlohn + 768.00
    /// Unfall-Taggeld) → QST-SB-periodisch 858.00 / 19.23 × 79.23 = 3'535.07.
    /// </summary>
    [Fact]
    public void Richtlinien_Beispiel_10_6_4_3_DreiStundenPlusZweiunddreissigAusfall()
    {
        var firma42 = new CompanyProfile { NormalWeeklyHours = 42m };
        var satz = ComputeSatzBruttoForNebenjob(Qst(60), 858.00m, workedHours: 3m, firma42, ausfallStunden: 32m);
        Assert.NotNull(satz);
        // Geprueft wird der Punkt des Beispiels: 3 + 32 = 35 Stunden zaehlen fuer den Grad.
        // Ohne die Ausfallstunden waeren es 3/182 = 1.65 % und der Satz-Lohn ueber 32'000.
        Assert.InRange(satz!.Value, 3534.90m, 3535.10m);
        Assert.True(ComputeSatzBruttoForNebenjob(Qst(60), 858.00m, 3m, firma42) > 30000m,
            "ohne Ausfallstunden muesste der Satz-Lohn voellig aus dem Ruder laufen");
        // Die Richtlinie schreibt 3'535.07, weil sie mit dem ANGEZEIGTEN, auf zwei Stellen
        // gerundeten Grad (19.23 / 79.23) vorrechnet. Die RefXML rechnet mit dem genauen
        // Verhaeltnis — nur so treffen Blancs fuenf Monate oben auf den Rappen. Wir folgen
        // der RefXML; der Unterschied sind 11 Rappen im satzbestimmenden Lohn und aendert
        // die Tarifstufe nicht.
    }

    [Fact]
    public void OhneAusfallstunden_BleibtAllesWieVorher()
    {
        var firma42 = new CompanyProfile { NormalWeeklyHours = 42m };
        var ohne = ComputeSatzBruttoForNebenjob(Qst(60), 2948.65m, 75m, firma42);
        var mitNull = ComputeSatzBruttoForNebenjob(Qst(60), 2948.65m, 75m, firma42, ausfallStunden: 0m);
        Assert.Equal(ohne, mitNull);
        Assert.InRange(ohne!.Value, 7241.85m, 7241.95m);
    }

    [Fact]
    public void AusfallStunden_NurAusLohnausfallTypen()
    {
        var breakdown = new Dictionary<string, decimal>
        {
            ["KRANK"] = 8.4m, ["UNFALL"] = 16m, ["MILITAER"] = 4m,
            ["FERIEN"] = 42m, ["FEIERTAG"] = 8.4m, ["SCHULUNG"] = 3m,
        };
        Assert.Equal(28.4m, QstAusfallStunden(breakdown));
        Assert.Equal(0m, QstAusfallStunden(null));
    }

    [Fact]
    public void OhneNebenbeschaeftigung_KeineHochrechnung()
    {
        // Egli, Bucher: kein Nebenjob → die Funktion greift gar nicht, der Beleg bleibt gleich.
        var ohneNebenjob = new EmployeeQuellensteuer { WeitereBeschaftigungen = false };
        Assert.Null(ComputeSatzBruttoForNebenjob(ohneNebenjob, 5111m, 130m, Firma));
        Assert.Null(ComputeSatzBruttoForNebenjob(null, 5111m, 130m, Firma));
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
