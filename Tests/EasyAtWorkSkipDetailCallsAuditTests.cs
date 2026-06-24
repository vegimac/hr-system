using System;
using System.IO;
using Xunit;

namespace HrSystem.Tests;

/// <summary>
/// Source-Audit (Walter-Vorgabe 23.06.2026): Die Vertrags-/Lohn-/Funktionshistorie
/// (contracts, pay_rates, positions) darf NIE an <c>SkipDetailCalls</c> hängen —
/// sonst kann der Timeline-Sync alte falsche Verträge nicht korrigieren. Nur
/// optionale Zusatz-Stammdaten (Fiscal/IBAN, Properties/AHV/Zivilstand) dürfen
/// bei <c>SkipDetailCalls=true</c> übersprungen werden.
///
/// Der Test prüft die Struktur des Detail-Fetch-Blocks in
/// EasyAtWorkEmployeeSyncService: GetContractsAsync / GetPayRatesAsync /
/// GetPositionsAsync müssen VOR dem <c>if (!req.SkipDetailCalls)</c>-Gate stehen.
/// Verschiebt jemand sie wieder in das Gate, schlägt dieser Test fehl.
/// </summary>
public class EasyAtWorkSkipDetailCallsAuditTests
{
    [Fact]
    public void Vertragshistorie_LaedtUnabhaengigVonSkipDetailCalls()
    {
        var file = FindServiceFile();
        var src  = File.ReadAllText(file);

        // Detail-Fetch-Block: beginnt mit der Mengen-Bedingung (NICHT mehr mit SkipDetailCalls).
        var fetchStart = src.IndexOf("if (rowsToProcess.Count > 0)", StringComparison.Ordinal);
        Assert.True(fetchStart > 0, "Detail-Fetch-Block (if (rowsToProcess.Count > 0)) nicht gefunden.");

        // Das optionale Gate MUSS innerhalb des Detail-Fetch existieren …
        var gate = src.IndexOf("if (!req.SkipDetailCalls)", fetchStart, StringComparison.Ordinal);
        Assert.True(gate > fetchStart, "SkipDetailCalls-Gate im Detail-Fetch nicht gefunden.");

        // … und die Pflicht-Endpunkte müssen VOR dem Gate geladen werden.
        foreach (var call in new[] { "GetContractsAsync(", "GetPayRatesAsync(", "GetPositionsAsync(" })
        {
            var idx = src.IndexOf(call, fetchStart, StringComparison.Ordinal);
            Assert.True(idx > fetchStart && idx < gate,
                $"{call} muss im Detail-Fetch VOR dem SkipDetailCalls-Gate geladen werden " +
                $"(Pflicht für die Vertragshistorie, niemals optional).");
        }

        // Negativ-Absicherung: zwischen Gate und Block-Ende darf KEIN Contracts-/
        // PayRates-Load stehen (kein versehentliches Gating).
        var afterGate = src.Substring(gate, Math.Min(2000, src.Length - gate));
        Assert.DoesNotContain("GetContractsAsync(", afterGate);
        Assert.DoesNotContain("GetPayRatesAsync(", afterGate);
    }

    private static string FindServiceFile()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "Services", "EasyAtWork", "EasyAtWorkEmployeeSyncService.cs");
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent is null) break;
            dir = parent.FullName;
        }
        throw new FileNotFoundException("EasyAtWorkEmployeeSyncService.cs nicht gefunden (Test muss aus dem Repo laufen).");
    }
}
