using HrSystem.Models;
using HrSystem.Services;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Werdegang fürs Zeugnis (Walter 22.09.2026): Zusammenfassen gleicher Abschnitte,
/// Modell nur bei Wechsel, «seit …» für den laufenden Abschnitt, Schichtführer-Texte.
/// </summary>
public class ZeugnisWerdegangTests
{
    private static Employment E(string von, string? bis, string modell, string gruppe)
        => new()
        {
            ContractStartDate = DateTime.Parse(von),
            ContractEndDate   = bis == null ? null : DateTime.Parse(bis),
            EmploymentModel   = modell,
            JobGroupCode      = gruppe,
        };

    [Fact]
    public void Beispiel_Reinach_DreiStufen()
    {
        var emps = new[]
        {
            E("2024-09-04", "2025-09-30", "FLEX", "SHIFT_LEADER_1_6"),
            E("2025-10-01", "2025-12-31", "MTP",  "SHIFT_LEADER_1_6"),
            E("2026-01-01", "2026-07-31", "MTP",  "SHIFT_LEADER_1_6"),
            E("2026-08-01", null,         "FIX-M","SHIFT_LEADER_7_PLUS"),
        };
        var w = ZeugnisWerdegang.Baue(emps, female: true, stichtag: new DateOnly(2026, 9, 22));
        // Die beiden MTP-Abschnitte mit gleicher Funktion werden zusammengefasst.
        Assert.Equal(3, w.Count);
        Assert.Equal("04.09.2024 – 30.09.2025 · Schichtführerin in Ausbildung im Stundenlohn", w[0].Text);
        Assert.Equal("01.10.2025 – 31.07.2026 · Schichtführerin in Ausbildung mit garantierten Stunden", w[1].Text);
        Assert.Equal("seit 01.08.2026 · Schichtführerin im Monatslohn", w[2].Text);
    }

    [Fact]
    public void ModellNurWennEsWechselt()
    {
        var emps = new[]
        {
            E("2024-01-01", "2025-06-30", "FLEX", "CREW"),
            E("2025-07-01", null,         "FLEX", "HOST_CT"),
        };
        var w = ZeugnisWerdegang.Baue(emps, female: false, stichtag: new DateOnly(2026, 9, 22));
        Assert.Equal(2, w.Count);
        Assert.Equal("01.01.2024 – 30.06.2025 · Crewmitarbeiter", w[0].Text);       // kein «im Stundenlohn»
        Assert.Equal("seit 01.07.2025 · Crew-Trainer", w[1].Text);
    }

    [Fact]
    public void Schlusszeugnis_LetzterAbschnittEndetAmAustritt()
    {
        var emps = new[] { E("2025-01-01", null, "FIX", "CREW") };
        var w = ZeugnisWerdegang.Baue(emps, female: false,
            stichtag: new DateOnly(2026, 9, 22), bis: new DateOnly(2026, 8, 31));
        Assert.Single(w);
        Assert.Equal("01.01.2025 – 31.08.2026 · Crewmitarbeiter", w[0].Text);
    }

    [Fact]
    public void JobTitleMitCodeStichtDieFunktionsgruppe()
    {
        // Altbestand: job_title hält den historischen Code pro Abschnitt,
        // job_group_id wurde vom Sync auf die heutige Funktion gesetzt.
        var e = E("2024-01-01", null, "FLEX", "SHIFT_LEADER_1_6");
        e.JobTitle = "CREW";
        var w = ZeugnisWerdegang.Baue(new[] { e }, female: false, stichtag: new DateOnly(2026, 9, 22));
        Assert.Equal("seit 01.01.2024 · Crewmitarbeiter", w[0].Text);
    }

    [Fact]
    public void FreitextTitelBleibtFreitextNurOhneFunktionsgruppe()
    {
        var e = E("2024-01-01", null, "FLEX", "HOST_CT");
        e.JobTitle = "Shift Coordinator";      // kein bekannter Code → Gruppe gewinnt
        var w = ZeugnisWerdegang.Baue(new[] { e }, female: false, stichtag: new DateOnly(2026, 9, 22));
        Assert.Equal("seit 01.01.2024 · Crew-Trainer", w[0].Text);
    }

    [Fact]
    public void SchichtfuehrerErsetztShiftLeader()
    {
        Assert.Equal("Schichtführerin in Ausbildung", ZeugnisWerdegang.FunktionText("SHIFT_LEADER_1_6", null, true));
        Assert.Equal("Schichtführer", ZeugnisWerdegang.FunktionText("SHIFT_LEADER_7_PLUS", null, false));
    }
}

/// <summary>
/// Funktion aus dem Lohn rekonstruieren (Walter 22.09.2026, Fall 1290025):
/// easy@work liefert keine Funktions-Historie, der Sync hatte die heutige
/// Funktion auf alle Abschnitte geschrieben.
/// </summary>
public class FunktionAusLohnTests
{
    private static readonly FunktionAusLohn.Satz[] Saetze2025 =
    {
        new("CREW",                "FLEX",  "hourly",  20.36m, new DateOnly(2025,1,1), new DateOnly(2025,12,31), "Ia"),
        new("HOST_CT",             "FLEX",  "hourly",  21.66m, new DateOnly(2025,1,1), new DateOnly(2025,12,31), "Ia"),
        new("SWING",               "FLEX",  "hourly",  22.36m, new DateOnly(2025,1,1), new DateOnly(2025,12,31), "Ia"),
        new("CREW",                "FIX",   "monthly", 3713m,  new DateOnly(2026,1,1), null, "Ia"),
        new("SHIFT_LEADER_1_6",    "FIX-M", "monthly", 4300m,  new DateOnly(2026,1,1), null, "Ia"),
        new("SHIFT_LEADER_7_PLUS", "FIX-M", "monthly", 4600m,  new DateOnly(2026,1,1), null, "Ia"),
        new("REST_MANAGER",        "FIX-M", "monthly", 6100m,  new DateOnly(2026,1,1), null, "Ia"),
    };

    [Fact]
    public void Stundenlohn_TrifftCrewExakt()
    {
        var v = FunktionAusLohn.Ermittle(new DateOnly(2025, 2, 1), "FLEX", "hourly",
            stundenlohn: 20.36m, monatslohn100: null, educationLevelCode: null, Saetze2025);
        Assert.Equal("CREW", v.JobGroupCode);
        Assert.Equal(FunktionAusLohn.Sicherheit.Exakt, v.Sicherheit);
    }

    [Fact]
    public void Monatslohn4300_IstSchichtfuehrer()
    {
        var v = FunktionAusLohn.Ermittle(new DateOnly(2026, 8, 1), "FIX-M", "monthly",
            stundenlohn: null, monatslohn100: 4300m, educationLevelCode: null, Saetze2025);
        Assert.Equal("SHIFT_LEADER_1_6", v.JobGroupCode);
        Assert.Equal(FunktionAusLohn.Sicherheit.Exakt, v.Sicherheit);
    }

    [Fact]
    public void LohnUeberMinimum_HoechsteDarunter_AberNurUngefaehr()
    {
        var v = FunktionAusLohn.Ermittle(new DateOnly(2025, 2, 1), "FLEX", "hourly",
            stundenlohn: 21.00m, monatslohn100: null, educationLevelCode: null, Saetze2025);
        Assert.Equal("CREW", v.JobGroupCode);
        Assert.Equal(FunktionAusLohn.Sicherheit.Ungefaehr, v.Sicherheit);
    }

    [Fact]
    public void KeinLohn_Unklar()
    {
        var v = FunktionAusLohn.Ermittle(new DateOnly(2025, 2, 1), "FLEX", "hourly",
            null, null, null, Saetze2025);
        Assert.Equal(FunktionAusLohn.Sicherheit.Unklar, v.Sicherheit);
        Assert.Null(v.JobGroupCode);
    }

    [Fact]
    public void SchichtfuehrerStufe_NachSechsMonaten()
    {
        var start = new DateOnly(2026, 8, 1);
        Assert.Equal("SHIFT_LEADER_1_6",    FunktionAusLohn.SchichtfuehrerStufe(start, new DateOnly(2026, 8, 1)));
        Assert.Equal("SHIFT_LEADER_1_6",    FunktionAusLohn.SchichtfuehrerStufe(start, new DateOnly(2027, 1, 31)));
        Assert.Equal("SHIFT_LEADER_7_PLUS", FunktionAusLohn.SchichtfuehrerStufe(start, new DateOnly(2027, 2, 1)));
    }
}
