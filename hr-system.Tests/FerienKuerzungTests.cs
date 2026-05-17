using HrSystem.Services;
using System.Reflection;

namespace hr_system.Tests;

/// <summary>
/// Tests für die Ferienkürzung nach Art. 329b OR.
/// Testet die statische Hilfslogik (BerechneKuerzung, GetDienstjahr, TageInRange)
/// ohne Datenbank.
/// </summary>
public class FerienKuerzungTests
{
    // ── BerechneKuerzung (private static) ────────────────────────────────────
    // Zugriff via Reflection, da private — alternativ könnte man die Methode
    // internal machen mit [InternalsVisibleTo], aber für den Anfang reicht das.

    private static decimal BerechneKuerzung(decimal tage, int schwellwertTage)
    {
        var method = typeof(FerienKuerzungService)
            .GetMethod("BerechneKuerzung", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (decimal)method.Invoke(null, new object[] { tage, schwellwertTage })!;
    }

    [Fact]
    public void UnterSchwellwert_KeineKuerzung()
    {
        // 50 Tage krank, Schwelle 60 → keine Kürzung
        Assert.Equal(0m, BerechneKuerzung(50m, 60));
    }

    [Fact]
    public void GenauSchwellwert_KeineKuerzung()
    {
        // Exakt 60 Tage → noch keine Kürzung (erst ÜBER dem Schwellwert)
        Assert.Equal(0m, BerechneKuerzung(60m, 60));
    }

    [Fact]
    public void EinVollerMonatUeberSchwellwert_EinZwoelftel()
    {
        // 95 Tage krank, Schwelle 60 → 35 Tage über → 1 voller Monat → 1/12
        Assert.Equal(1m, BerechneKuerzung(95m, 60));
    }

    [Fact]
    public void ZweiVolleMonateUeberSchwellwert()
    {
        // 125 Tage krank, Schwelle 60 → 65 Tage über → 2 volle Monate → 2/12
        Assert.Equal(2m, BerechneKuerzung(125m, 60));
    }

    [Fact]
    public void TeilmonatWirdNichtGezaehlt()
    {
        // 80 Tage krank, Schwelle 60 → 20 Tage über → 0 volle Monate → 0
        Assert.Equal(0m, BerechneKuerzung(80m, 60));
    }

    [Fact]
    public void UnbezahlterUrlaub_Schwelle30()
    {
        // Selbstverschuldet: Schwelle 30 Tage
        // 65 Tage unbez. Urlaub → 35 über Schwelle → 1 voller Monat
        Assert.Equal(1m, BerechneKuerzung(65m, 30));
    }

    [Fact]
    public void Mutterschaft_Schwelle90()
    {
        // Schwangerschaft: Schwelle 90 Tage
        // 130 Tage → 40 über Schwelle → 1 voller Monat
        Assert.Equal(1m, BerechneKuerzung(130m, 90));
    }

    // ── GetDienstjahr (private static) ───────────────────────────────────────

    private static (DateOnly von, DateOnly bis) GetDienstjahr(DateOnly hired, DateOnly periodEnd)
    {
        var method = typeof(FerienKuerzungService)
            .GetMethod("GetDienstjahr", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = method.Invoke(null, new object[] { hired, periodEnd })!;
        var tuple = ((DateOnly, DateOnly))result;
        return tuple;
    }

    [Fact]
    public void Dienstjahr_NachAnniversary()
    {
        // Eintritt 3.2.2020, Stichtag 25.4.2026
        // → Dienstjahr 3.2.2026 – 2.2.2027
        var (von, bis) = GetDienstjahr(
            new DateOnly(2020, 2, 3),
            new DateOnly(2026, 4, 25));

        Assert.Equal(new DateOnly(2026, 2, 3), von);
        Assert.Equal(new DateOnly(2027, 2, 2), bis);
    }

    [Fact]
    public void Dienstjahr_VorAnniversary()
    {
        // Eintritt 15.6.2022, Stichtag 10.3.2026
        // → Anniversary 2026 ist 15.6.2026, aber Stichtag ist davor
        // → Dienstjahr 15.6.2025 – 14.6.2026
        var (von, bis) = GetDienstjahr(
            new DateOnly(2022, 6, 15),
            new DateOnly(2026, 3, 10));

        Assert.Equal(new DateOnly(2025, 6, 15), von);
        Assert.Equal(new DateOnly(2026, 6, 14), bis);
    }

    [Fact]
    public void Dienstjahr_Schaltjahr_29Februar()
    {
        // Eintritt 29.2.2024 (Schaltjahr), Stichtag 15.5.2026 (kein Schaltjahr)
        // → Anniversary 2026 wäre 29.2. → 28.2.2026 (korrigiert)
        // → Dienstjahr 28.2.2026 – 27.2.2027
        var (von, bis) = GetDienstjahr(
            new DateOnly(2024, 2, 29),
            new DateOnly(2026, 5, 15));

        Assert.Equal(new DateOnly(2026, 2, 28), von);
        Assert.Equal(new DateOnly(2027, 2, 27), bis);
    }

    // ── TageInRange (private static) ────────────────────────────────────────

    private static int TageInRange(DateOnly dateFrom, DateOnly dateTo, decimal prozent,
        DateOnly von, DateOnly bis)
    {
        var absenceType = typeof(FerienKuerzungService).Assembly
            .GetType("HrSystem.Models.Absence")!;
        var absence = Activator.CreateInstance(absenceType)!;
        absenceType.GetProperty("DateFrom")!.SetValue(absence, dateFrom);
        absenceType.GetProperty("DateTo")!.SetValue(absence, dateTo);
        absenceType.GetProperty("Prozent")!.SetValue(absence, prozent);

        var method = typeof(FerienKuerzungService)
            .GetMethod("TageInRange", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (int)method.Invoke(null, new object[] { absence, von, bis })!;
    }

    [Fact]
    public void TageInRange_KomplettInnerhalb()
    {
        // Absenz 5.3.–10.3. im Dienstjahr 1.1.–31.12. → 6 Tage
        var tage = TageInRange(
            new DateOnly(2026, 3, 5), new DateOnly(2026, 3, 10), 100m,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Assert.Equal(6, tage);
    }

    [Fact]
    public void TageInRange_TeilweiseAusserhalb()
    {
        // Absenz 25.12.2025 – 10.1.2026, Dienstjahr ab 1.1.2026
        // → nur 10 Tage zählen (1.1. – 10.1.)
        var tage = TageInRange(
            new DateOnly(2025, 12, 25), new DateOnly(2026, 1, 10), 100m,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31));
        Assert.Equal(10, tage);
    }

    [Fact]
    public void TageInRange_KomplettAusserhalb()
    {
        // Absenz im März, Dienstjahr erst ab Juni → 0
        var tage = TageInRange(
            new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), 100m,
            new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31));
        Assert.Equal(0, tage);
    }
}
