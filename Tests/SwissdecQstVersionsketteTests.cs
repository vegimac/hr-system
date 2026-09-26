using HrSystem.Controllers;
using HrSystem.Models;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Schritt 4c: die QST-Versionskette beim Anlegen/Nachtragen einer Mutation.
/// Anlass Walter 26.09.2026 (TF36 Maldini): Oktober wurde angelegt, als der
/// September noch falsch war → Kanton BE statt TI geerbt; nach der September-Reparatur
/// blieb die September-Version offen und ueberlappte Oktober/November («zwei AKTUELL»).
/// </summary>
public class SwissdecQstVersionsketteTests
{
    private static DateOnly D(int j, int m, int t) => new(j, m, t);

    private static EmployeeQuellensteuer V(string von, string? bis, string kanton, string code)
        => new()
        {
            Id = von.GetHashCode() & 0x7fffffff,
            ValidFrom = DateOnly.ParseExact(von, "dd.MM.yyyy"),
            ValidTo = bis == null ? null : DateOnly.ParseExact(bis, "dd.MM.yyyy"),
            Steuerkanton = kanton,
            QstCode = code,
        };

    [Fact]
    public void Vorgaenger_IstDieVersionDavor_NichtDieDesMonatsSelbst()
    {
        // Zweiter Lauf des Oktobers: der Oktober-Eintrag existiert schon.
        // Erben muss er trotzdem vom September (TI), nicht von sich selbst (BE).
        var liste = new[]
        {
            V("01.08.2025", "31.08.2025", "BE", "C0Y"),
            V("01.09.2025", "30.09.2025", "TI", "T0N"),
            V("01.10.2025", null,         "BE", "F0N"),
        };
        var vorgaenger = SwissdecTestmandantController.QstVorgaenger(liste, D(2025, 10, 1));
        Assert.NotNull(vorgaenger);
        Assert.Equal("TI", vorgaenger!.Steuerkanton);
        Assert.Equal(D(2025, 9, 1), vorgaenger.ValidFrom);
    }

    [Fact]
    public void Vorgaenger_NimmtDieOffeneVersionDieDasDatumAbdeckt()
    {
        var liste = new[] { V("01.09.2025", null, "TI", "T0N") };
        var v = SwissdecTestmandantController.QstVorgaenger(liste, D(2025, 10, 1));
        Assert.Equal("TI", v!.Steuerkanton);
    }

    [Fact]
    public void Vorgaenger_OhneEintragDavor_IstNull()
    {
        var liste = new[] { V("01.11.2025", null, "TI", "F1N") };
        var v = SwissdecTestmandantController.QstVorgaenger(liste, D(2025, 10, 1));
        Assert.Equal(D(2025, 11, 1), v!.ValidFrom);   // Rueckfall: irgendeiner, damit nichts leer bleibt
    }

    [Fact]
    public void EndeDerVersion_BisZumTagVorDerNaechsten()
    {
        var starts = new[] { D(2025, 8, 1), D(2025, 9, 1), D(2025, 11, 1), D(2025, 12, 1) };
        Assert.Equal(D(2025, 10, 31), SwissdecTestmandantController.QstEndeDerVersion(starts, D(2025, 10, 1)));
    }

    [Fact]
    public void EndeDerVersion_SeptemberEndetVorOktober_KeineZweiAktuellen()
    {
        // Genau der Fall Maldini: September wird nachtraeglich angelegt, Oktober/November stehen schon.
        var starts = new[] { D(2025, 10, 1), D(2025, 11, 1), D(2025, 12, 1) };
        Assert.Equal(D(2025, 9, 30), SwissdecTestmandantController.QstEndeDerVersion(starts, D(2025, 9, 1)));
    }

    [Fact]
    public void EndeDerVersion_OhneNachfolger_BleibtOffen()
    {
        var starts = new[] { D(2025, 8, 1), D(2025, 9, 1) };
        Assert.Null(SwissdecTestmandantController.QstEndeDerVersion(starts, D(2025, 12, 1)));
    }
}
