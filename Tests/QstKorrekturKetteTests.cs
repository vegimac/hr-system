using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Zweite Korrektur desselben Monats (TF33 Châtelain, Muster AG BE):
/// Mai wird im Juni von A0Y auf B0Y korrigiert, im Juli nochmals auf B1Y.
/// Der Juli-Beleg muss «Mai B0Y→B1Y» zeigen (Swissdec Old-Block), nicht den
/// Ur-Code A0Y aus dem eingefrorenen Mai-Beleg. Claude 26.09.2026.
/// </summary>
public class QstKorrekturKetteTests
{
    private sealed record Posten(int Id, int Jahr, int Monat, string? AlterCode, string? NeuerCode);

    /// <summary>Spiegelt die Ableitung im Lohnlauf (PayrollCalculationEngine, K2-Label).</summary>
    private static string? AltFuerAnzeige(IEnumerable<Posten> kette, Posten p)
        => kette
               .Where(f => f.Jahr == p.Jahr && f.Monat == p.Monat && f.Id < p.Id
                        && !string.IsNullOrWhiteSpace(f.NeuerCode))
               .OrderByDescending(f => f.Id)
               .Select(f => f.NeuerCode)
               .FirstOrDefault()
           ?? p.AlterCode;

    [Fact]
    public void ZweiteKorrektur_ZeigtZuletztGemeldetenCode()
    {
        // Juni-Lauf: April + Mai von A0Y auf B0Y. Juli-Lauf: Mai + Juni auf B1Y.
        // Der Mai-Posten aus dem Juli trägt gespeichert noch den Ur-Code A0Y.
        var kette = new List<Posten>
        {
            new(1, 2025, 4, "A0Y", "B0Y"),
            new(2, 2025, 5, "A0Y", "B0Y"),
            new(3, 2025, 5, "A0Y", "B1Y"),
            new(4, 2025, 6, "B0Y", "B1Y"),
        };
        Assert.Equal("B0Y", AltFuerAnzeige(kette, kette[2]));   // Mai: Kette sticht den Ur-Code
        Assert.Equal("B0Y", AltFuerAnzeige(kette, kette[3]));   // Juni: erste Korrektur, bleibt
        Assert.Equal("A0Y", AltFuerAnzeige(kette, kette[0]));   // April: erste Korrektur, bleibt
    }

    [Fact]
    public void ErsteKorrektur_OhneKette_BleibtBeimBelegCode()
    {
        var kette = new List<Posten> { new(7, 2025, 4, "A0N", "B0N") };
        Assert.Equal("A0N", AltFuerAnzeige(kette, kette[0]));
    }

    [Fact]
    public void AndererMonat_ZaehltNicht()
    {
        var kette = new List<Posten>
        {
            new(1, 2025, 4, "A0Y", "B0Y"),
            new(2, 2025, 5, "A0Y", "B1Y"),
        };
        Assert.Equal("A0Y", AltFuerAnzeige(kette, kette[1]));   // April färbt nicht auf Mai ab
    }
}
