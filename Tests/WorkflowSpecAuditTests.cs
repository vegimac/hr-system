using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace HrSystem.Tests;

/// <summary>
/// Audit-Tests für die Lohnlauf-Workflow-Spec (Walter-Vorgabe 19.05.2026,
/// CLAUDE.md → Bearbeitungs-Status-Map).
///
/// Diese Tests scannen die kritischen Code-Stellen per Regex und stellen
/// sicher, dass die in CLAUDE.md festgelegten Regeln eingehalten bleiben.
/// Sinn: bei einem Refactor merkt man sofort, wenn ein Schutzschild
/// wegfällt — der Test schlägt fehl, bevor Walter im UI einen Bug entdeckt.
///
/// Genaue Tests der Status-Übergänge folgen in WorkflowStatusTests.cs.
/// </summary>
public class WorkflowSpecAuditTests
{
    private readonly ITestOutputHelper _out;
    public WorkflowSpecAuditTests(ITestOutputHelper outHelper) { _out = outHelper; }

    private static string RepoRoot
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "hr-system.csproj")))
                dir = Directory.GetParent(dir)?.FullName;
            if (dir is null) throw new InvalidOperationException("hr-system.csproj nicht gefunden — Test muss im Tests-Subdir laufen.");
            return dir;
        }
    }

    private static string ReadAllText(string relPath) =>
        File.ReadAllText(Path.Combine(RepoRoot, relPath));

    // ──────────────────────────────────────────────────────────────────
    // 1. PayrollSnapshot-Default: muss BERECHNET sein, NICHT FREIGEGEBEN_GF
    //    (Walter-Bug 19.05.2026: Default war FREIGEGEBEN_GF → alle MA als
    //    bestätigt markiert, obwohl GF nichts geklickt hat.)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void PayrollSnapshot_DefaultStatus_IsBerechnet()
    {
        var src = ReadAllText("Models/PayrollSnapshot.cs");
        // Genau ein Vorkommen des Status-Property mit Default
        var match = Regex.Match(src, @"public\s+string\s+Status\s*\{\s*get;\s*set;\s*\}\s*=\s*""([A-Z_]+)""\s*;");
        Assert.True(match.Success, "Status-Property mit Default-Wert nicht gefunden in PayrollSnapshot.cs");
        Assert.Equal("BERECHNET", match.Groups[1].Value);
    }

    [Fact]
    public void Models_PayrollSnapshot_StaysBuildable()
    {
        // Sanity: Klasse existiert, hat Status-Property
        var snap = new HrSystem.Models.PayrollSnapshot();
        Assert.Equal("BERECHNET", snap.Status);
        Assert.False(snap.IsFinal);
    }

    // ──────────────────────────────────────────────────────────────────
    // 2. Provisorisch-Abschliessen darf NICHT IsFinal=true setzen.
    //    (Walter-Bug 19.05.2026: HR konnte nicht HR-bestätigen weil
    //    Snapshot vorzeitig final war.)
    //    IsFinal=true gehört ausschliesslich in DefinitivAbschliessen.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ProvisorischAbschliessen_DoesNotSetIsFinal()
    {
        var src = ReadAllText("Controllers/PayrollPeriodeController.cs");
        // Block der provisorischen-Abschluss-Methode finden.
        // Methode heisst AbschliessePeriode (= „An HR senden" / provisorisch).
        var abIdx = src.IndexOf("public async Task<IActionResult> AbschliessePeriode(", StringComparison.Ordinal);
        Assert.True(abIdx > 0, "AbschliessePeriode-Methode nicht gefunden.");
        var nextIdx = src.IndexOf("public async Task<IActionResult>", abIdx + 1, StringComparison.Ordinal);
        if (nextIdx < 0) nextIdx = src.Length;
        var block = src.Substring(abIdx, nextIdx - abIdx);

        // In diesem Block darf KEIN `snap.IsFinal = true` (oder ähnlich) stehen.
        var pattern = new Regex(@"\bIsFinal\s*=\s*true\b");
        Assert.False(pattern.IsMatch(block),
            "Abschliessen (provisorisch) darf IsFinal NICHT auf true setzen — sonst kann HR nicht HR-bestätigen. " +
            "IsFinal=true gehört in DefinitivAbschliessen.");
    }

    [Fact]
    public void DefinitivAbschliessen_DoesSetIsFinal()
    {
        var src = ReadAllText("Controllers/PayrollPeriodeController.cs");
        var dfIdx = src.IndexOf("public async Task<IActionResult> DefinitivAbschliessen(", StringComparison.Ordinal);
        Assert.True(dfIdx > 0, "DefinitivAbschliessen-Methode nicht gefunden.");
        var nextIdx = src.IndexOf("public async Task<IActionResult>", dfIdx + 1, StringComparison.Ordinal);
        if (nextIdx < 0) nextIdx = src.Length;
        var block = src.Substring(dfIdx, nextIdx - dfIdx);

        Assert.Matches(@"\bIsFinal\s*=\s*true\b", block);
        Assert.Contains("\"ABGESCHLOSSEN\"", block);
    }

    // ──────────────────────────────────────────────────────────────────
    // 3. Admin-Reset-Endpoints müssen Zahldatum prüfen (PAYOUT_DATE_REACHED).
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void WiederOeffnen_ChecksAuszahlungsdatum()
    {
        var src = ReadAllText("Controllers/PayrollPeriodeController.cs");
        var idx = src.IndexOf("public async Task<IActionResult> WiederOeffnen(", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var nextIdx = src.IndexOf("public async Task<IActionResult>", idx + 1, StringComparison.Ordinal);
        if (nextIdx < 0) nextIdx = src.Length;
        var block = src.Substring(idx, nextIdx - idx);

        Assert.Contains("PAYOUT_DATE_REACHED", block);
        Assert.Contains("Auszahlungsdatum", block);
    }

    [Fact]
    public void AkontoResetPeriode_ChecksAkontoAuszahlungsdatum()
    {
        var src = ReadAllText("Controllers/AkontoWorkflowController.cs");
        var idx = src.IndexOf("public async Task<IActionResult> ResetPeriode(", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var nextIdx = src.IndexOf("public async Task<IActionResult>", idx + 1, StringComparison.Ordinal);
        if (nextIdx < 0) nextIdx = src.Length;
        var block = src.Substring(idx, nextIdx - idx);

        Assert.Contains("PAYOUT_DATE_REACHED", block);
        Assert.Contains("AkontoAuszahlungsdatum", block);
    }

    // ──────────────────────────────────────────────────────────────────
    // 4. ZurueckAnGf (provisorisch → offen) muss Snapshot-Status sauber
    //    rückrollen: Status=BERECHNET, GF/HR-Spuren raus, IsFinal=false.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ZurueckAnGf_ResetsSnapshotStatus()
    {
        var src = ReadAllText("Controllers/PayrollPeriodeController.cs");
        var idx = src.IndexOf("public async Task<IActionResult> ZurueckAnGf(", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var nextIdx = src.IndexOf("public async Task<IActionResult>", idx + 1, StringComparison.Ordinal);
        if (nextIdx < 0) nextIdx = src.Length;
        var block = src.Substring(idx, nextIdx - idx);

        Assert.Contains("\"BERECHNET\"", block);
        Assert.Contains("GfFreigegebenAt", block);
        Assert.Contains("HrBestaetigtAt", block);
        Assert.Contains("IsFinal", block);
    }

    [Fact]
    public void ZurueckAnGf_ResetsPayrollSaldoToDraft()
    {
        var src = ReadAllText("Controllers/PayrollPeriodeController.cs");
        var idx = src.IndexOf("public async Task<IActionResult> ZurueckAnGf(", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var nextIdx = src.IndexOf("public async Task<IActionResult>", idx + 1, StringComparison.Ordinal);
        if (nextIdx < 0) nextIdx = src.Length;
        var block = src.Substring(idx, nextIdx - idx);

        // Saldo muss zurückgesetzt werden (sonst zeigt Frontend „bereits bestätigt")
        Assert.Contains("PayrollSaldos", block);
        Assert.Contains("\"draft\"", block);
    }

    // ──────────────────────────────────────────────────────────────────
    // 5. Beide DTA-Auszahlen-Endpoints persistieren das Zahldatum.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AkontoAuszahlen_PersistsAkontoAuszahlungsdatum()
    {
        var src = ReadAllText("Controllers/AkontoWorkflowController.cs");
        var idx = src.IndexOf("public async Task<IActionResult> Auszahlen(", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var nextIdx = src.IndexOf("public async Task<IActionResult>", idx + 1, StringComparison.Ordinal);
        if (nextIdx < 0) nextIdx = src.Length;
        var block = src.Substring(idx, nextIdx - idx);

        Assert.Contains("AkontoAuszahlungsdatum", block);
        Assert.Contains("\"AUSBEZAHLT\"", block);
    }

    [Fact]
    public void DefinitivAbschliessen_PersistsAuszahlungsdatum()
    {
        var src = ReadAllText("Controllers/PayrollPeriodeController.cs");
        var idx = src.IndexOf("public async Task<IActionResult> DefinitivAbschliessen(", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var nextIdx = src.IndexOf("public async Task<IActionResult>", idx + 1, StringComparison.Ordinal);
        if (nextIdx < 0) nextIdx = src.Length;
        var block = src.Substring(idx, nextIdx - idx);

        Assert.Contains("Auszahlungsdatum", block);
        Assert.Contains("\"abgeschlossen\"", block);
    }

    // ──────────────────────────────────────────────────────────────────
    // 6. HR-per-MA-Endpoints (Definitiv) lassen IsFinal=true durchgehen?
    //    Antwort: NEIN. HR-Bestätigen muss möglich sein solange IsFinal=false.
    //    Bei IsFinal=true → Snapshot eingefroren, kein HR-Edit mehr.
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void DefinitivHrBestaetigen_RejectsFinalSnapshots()
    {
        var src = ReadAllText("Controllers/PayrollController.cs");
        var idx = src.IndexOf("public async Task<IActionResult> HrBestaetigen(", StringComparison.Ordinal);
        Assert.True(idx > 0);
        var nextIdx = src.IndexOf("public async Task<IActionResult>", idx + 1, StringComparison.Ordinal);
        if (nextIdx < 0) nextIdx = src.Length;
        var block = src.Substring(idx, nextIdx - idx);

        Assert.Contains("IsFinal", block);
        Assert.Contains("provisorisch_abgeschlossen", block);
        Assert.Contains("FREIGEGEBEN_GF", block);
    }
}
