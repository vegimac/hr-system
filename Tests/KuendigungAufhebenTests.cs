using HrSystem.Models;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Erledigte Kündigung aufheben (Walter-Vorgabe 24.09.2026, Fall Simona Dan).
///
/// «Gekündigt am», «Kündigung per», «Kündigung durch» und der Austrittsgrund gehören
/// zum beendeten Vertrag. Nach dem Übertritt nach Reinach stand am Mitarbeitenden
/// weiterhin «Gekündigt per 31.07.2026», obwohl seit dem 22.09. ein neuer Vertrag
/// läuft — die Maske meldete eine Kündigung, die es nicht mehr gibt.
///
/// Die Regel hängt bewusst am NEUEN VERTRAG, nicht am Austrittsdatum: easy@work kennt
/// keine Kündigung, nur ein «Eingestellt bis». Eine laufende, noch nicht vollzogene
/// Kündigung darf der Sync nie anfassen.
/// </summary>
public class KuendigungAufhebenTests
{
    private static string Quelle()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!, "Services/EasyAtWork/EasyAtWorkEmployeeSyncService.cs"));
    }

    [Fact]
    public void AlleVierFelder_WerdenGeloescht()
    {
        // Ein halb geleerter Zustand wäre schlimmer als gar keiner: «Kündigung per»
        // weg, «Austrittsgrund Familie» stehen geblieben.
        var src = Quelle();
        var i = src.IndexOf("private static bool KuendigungZuruecknehmen", StringComparison.Ordinal);
        Assert.True(i > 0, "Helfer KuendigungZuruecknehmen nicht gefunden.");
        // Leerzeichen zusammenziehen, damit die Ausrichtung im Quelltext egal ist.
        var block = System.Text.RegularExpressions.Regex.Replace(src.Substring(i, 900), @"\s+", " ");
        foreach (var feld in new[] { "KuendigungAusgesprochenAm", "KuendigungPer",
                                     "KuendigungDurch", "Austrittsgrund" })
            Assert.Contains($"emp.{feld} = null;", block);
    }

    [Fact]
    public void Aufhebung_HaengtAmNeuenVertrag_NichtAmAustrittsdatum()
    {
        var src = Quelle();
        var i = src.IndexOf("KuendigungNachNeuemVertragAufhebenAsync(Employee emp", StringComparison.Ordinal);
        Assert.True(i > 0, "Methode nicht gefunden.");
        var block = src.Substring(i, 700);
        // Bedingung: es gibt einen Vertrag, der NACH dem Kündigungstermin beginnt.
        Assert.Contains("em.ContractStartDate > gekuendigtPer", block);
        // Ohne Kündigungstermin passiert nichts.
        Assert.Contains("if (!emp.KuendigungPer.HasValue) return false;", block);
    }

    [Fact]
    public void WirdNachDerVertragsTimelineAufgerufen()
    {
        // Vorher gäbe es den neuen Vertrag noch gar nicht — die Prüfung liefe ins Leere.
        var src = Quelle();
        var timeline = src.IndexOf("await SyncEmploymentTimelineAsync(_db, temp,", StringComparison.Ordinal);
        var aufruf   = src.IndexOf("await KuendigungNachNeuemVertragAufhebenAsync(temp2", StringComparison.Ordinal);
        Assert.True(timeline > 0 && aufruf > timeline,
            "Die Aufhebung muss NACH dem Schreiben der Vertrags-Timeline laufen.");
    }

    [Fact]
    public void Reaktivierung_NimmtDieKuendigungMit()
    {
        // Überall, wo ein stehengebliebenes Austrittsdatum entfernt wird, muss auch
        // die Kündigung fallen — sonst bleibt die halbe Wahrheit stehen.
        var src = Quelle();
        var treffer = System.Text.RegularExpressions.Regex.Matches(src, @"KuendigungZuruecknehmen\(").Count;
        Assert.True(treffer >= 7, $"Erwartet: Aufruf an jeder Reaktivierungs-Stelle, gefunden {treffer}.");
    }
}
