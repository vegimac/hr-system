using HrSystem.Services.EasyAtWork;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// easy@work speichert das Intervall-Ende inkonsistent (Walter-Bug 22.09.2026,
/// MA 1220009 Acar-Hasanoglu): derselbe 31.01.2026 steht am Vertrag als
/// «22:59:59» (Tagesende) und am Lohnsatz als «23:00:00» (= 01.02. 00:00 Zürich).
/// Beide müssen als 31.01.2026 gelesen werden — sonst ist das Vertragsende einen
/// Tag zu lang und der Folge-Abschnitt beginnt falsch.
/// </summary>
public class EasyAtWorkIntervalEndTests
{
    [Fact]
    public void Vertragsende_TagesendeUndExklusiveMitternacht_GebenDasselbeDatum()
    {
        var c = new EawContract { ToRaw = "2026-01-31 22:59:59" };
        var r = new EawPayRate  { ToRaw = "2026-01-31 23:00:00" };
        Assert.Equal(new DateOnly(2026, 1, 31), c.To);
        Assert.Equal(new DateOnly(2026, 1, 31), r.To);
    }

    [Fact]
    public void Beginn_BleibtDerKalendertag()
    {
        // Sommerzeit (UTC+2): 22:00 des Vortags = Zürich 00:00 des Folgetags.
        var r = new EawPayRate { FromRaw = "2021-06-20 22:00:00" };
        Assert.Equal(new DateOnly(2021, 6, 21), r.From);
        // Winterzeit (UTC+1): 23:00 des Vortags.
        var c = new EawContract { FromRaw = "2024-12-31 23:00:00" };
        Assert.Equal(new DateOnly(2025, 1, 1), c.From);
    }

    [Fact]
    public void AnschlussSegmente_Luckenlos()
    {
        // Vertrag 1 endet, Vertrag 2 beginnt am Folgetag — ohne Überlappung.
        var alt = new EawContract { ToRaw   = "2022-12-20 22:59:59" };
        var neu = new EawContract { FromRaw = "2022-12-20 23:00:00" };
        Assert.Equal(new DateOnly(2022, 12, 20), alt.To);
        Assert.Equal(new DateOnly(2022, 12, 21), neu.From);
    }
}
