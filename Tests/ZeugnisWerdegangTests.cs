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
    private static FunktionAusLohn.Vorschlag Std(decimal lohn, string? stufe = null)
        => FunktionAusLohn.Ermittle(new DateOnly(2025, 2, 1), "FLEX", "hourly", lohn, null, stufe);
    private static FunktionAusLohn.Vorschlag Mt(decimal lohn100)
        => FunktionAusLohn.Ermittle(new DateOnly(2026, 8, 1), "FIX-M", "monthly", null, lohn100, null);

    [Theory]
    [InlineData(20.14, "CREW")]      // Crew-Satz 2024
    [InlineData(20.36, "CREW")]      // Crew-Satz 2025
    [InlineData(20.40, "CREW")]      // Crew-Satz 2026 — Grenze gehört zu Crew
    [InlineData(20.41, "HOST_CT")]
    [InlineData(21.66, "HOST_CT")]   // Grenze gehört zu Host
    [InlineData(21.67, "SWING")]
    [InlineData(22.36, "SWING")]
    public void Stundenlohn_Schwellen(double lohn, string erwartet)
    {
        var v = Std((decimal)lohn);
        Assert.Equal(erwartet, v.JobGroupCode);
        Assert.Equal(FunktionAusLohn.Sicherheit.Exakt, v.Sicherheit);
    }

    [Theory]
    [InlineData(4200, "SHIFT_LEADER_1_6")]   // Untergrenze
    [InlineData(4295, "SHIFT_LEADER_1_6")]   // L-GAV erste Stufe 2023–2025
    [InlineData(4304, "SHIFT_LEADER_1_6")]   // L-GAV erste Stufe 2026
    [InlineData(4300, "SHIFT_LEADER_1_6")]
    [InlineData(4499, "SHIFT_LEADER_1_6")]
    [InlineData(4500, "SHIFT_LEADER_7_PLUS")]   // Walter 22.09.2026: Lücke 4500–4600 gehört zu 7+
    [InlineData(4600, "SHIFT_LEADER_7_PLUS")]
    [InlineData(4999, "SHIFT_LEADER_7_PLUS")]
    [InlineData(5150, "SHIFT_LEADER_7_PLUS")]   // Fall 580026: keine GF (L-GAV-RM ab 6'100)
    [InlineData(5999, "SHIFT_LEADER_7_PLUS")]
    [InlineData(6000, "REST_MANAGER")]
    [InlineData(6100, "REST_MANAGER")]          // L-GAV Restaurant Manager 6'100–7'000
    public void Monatslohn_Schwellen(double lohn, string erwartet)
    {
        var v = Mt((decimal)lohn);
        Assert.Equal(erwartet, v.JobGroupCode);
        Assert.Equal(FunktionAusLohn.Sicherheit.Exakt, v.Sicherheit);
    }

    [Theory]
    [InlineData(4600)]   // L-GAV zweite Stufe 2023–2025
    [InlineData(4610)]   // L-GAV zweite Stufe 2026
    public void ZweiteSchichtfuehrerStufe(double lohn)
        => Assert.Equal("SHIFT_LEADER_7_PLUS", Mt((decimal)lohn).JobGroupCode);

    [Fact]
    public void MonatslohnUnterSchichtfuehrerBereich_KeinVorschlag()
    {
        // Crew (3'713) / Host (3'943) / Swing (4'070) im Monatslohn gibt es bei Schaub
        // nicht (Walter 22.09.2026) — lieber nichts vorschlagen als daraus einen
        // Schichtführer zu machen.
        Assert.Null(Mt(4070m).JobGroupCode);   // Swing-Monatssatz 2026
        var v = Mt(3713m);
        Assert.Null(v.JobGroupCode);
        Assert.Equal(FunktionAusLohn.Sicherheit.Unklar, v.Sicherheit);
    }

    [Fact]
    public void AbStufeII_SagtDerLohnNichts()
    {
        // Erfahrene Crew verdient 22.36 (Stufe II) — ohne diese Bremse würde daraus
        // ein Swing Manager (Walter-Entscheid 22.09.2026, Punkt 4).
        var v = Std(22.36m, "II");
        Assert.Null(v.JobGroupCode);
        Assert.Equal(FunktionAusLohn.Sicherheit.Unklar, v.Sicherheit);
        // Ia/Ib lassen die Regel greifen.
        Assert.Equal("SWING", Std(22.36m, "Ia").JobGroupCode);
    }

    [Fact]
    public void KeinLohn_Unklar()
    {
        var v = FunktionAusLohn.Ermittle(new DateOnly(2025, 2, 1), "FLEX", "hourly", null, null, null);
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
