using System.Diagnostics;

namespace HrSystem.Services.Elm;

/// <summary>
/// Erzeugt Lohnausweis und Listen mit Swissdec Viewgen (Java-Version) aus einer fertigen ELM-Jahresmeldung.
/// Viewgen bleibt unverändert und wird als eigenes Programm aufgerufen (Lizenzbestimmungen Ziffer 4 und 5).
/// Die .NET-Version scheidet aus: Sie braucht für PDF und Barcode Windows (Test 05.11.2026).
///
/// Produktiv erst nach der Zertifizierung (Lizenzbestimmungen Ziffer 3): Der Controller gibt die Funktion
/// nur auf der Testinstanz frei.
///
/// Viewgen meldet auch bei fehlender Ausgabe den Rückgabewert 0. Massgebend ist darum nur, ob wirklich
/// ein PDF entstanden ist. Arbeitsdateien enthalten Lohndaten und werden nach jedem Aufruf gelöscht.
/// </summary>
public class ViewgenDienst
{
    /// <summary>Ablage auf dem Server. <c>VIEWGEN_HOME</c> enthält <c>app/lib</c> und <c>jdk/bin/java</c>.</summary>
    public const string StandardOrdner = "/opt/viewgen";

    private static readonly TimeSpan StandardFrist = TimeSpan.FromMinutes(3);

    public static readonly IReadOnlyDictionary<string, string> Berichte = new Dictionary<string, string>
    {
        ["TaxAccountingReport"] = "Lohnausweis",
        ["AhvReport"] = "AHV",
        ["AhvFreeReport"] = "AHV-freie Personen",
        ["AhvProofOfInsuranceReport"] = "AHV-Versicherungsausweis",
        ["FakReport"] = "FAK",
        ["FakDetailedReport"] = "FAK ausführlich",
        ["UvgReport"] = "UVG",
        ["UvgzReport"] = "UVGZ",
        ["KtgReport"] = "KTG",
        ["TxbReport"] = "Grenzgänger",
        ["QstReport"] = "Quellensteuer",
        ["QstResultReport"] = "Quellensteuer-Ergebnis",
        ["StatisticReport"] = "Statistik",
        ["PlausibilityReport"] = "Plausibilität",
    };

    /// <summary>Aus der Monatsmeldung: Quellensteuer und Statistik stehen nur dort.</summary>
    public static readonly IReadOnlySet<string> AusMonat = new HashSet<string> { "QstReport", "QstResultReport", "StatisticReport", "PlausibilityReport" };

    /// <summary>Aus der Jahresmeldung. Die Plausibilität gibt es für beide Meldungen.</summary>
    public static bool AusJahr(string plugin) => Berichte.ContainsKey(plugin) && (!AusMonat.Contains(plugin) || plugin == "PlausibilityReport");

    // Ein Bericht zur Zeit: Die Maschine trägt auch Prod, und jeder Lauf darf bis 1 GB belegen.
    private static readonly SemaphoreSlim NurEiner = new(1, 1);

    private readonly string _java;
    private readonly string _lib;
    private readonly TimeSpan _frist;

    public ViewgenDienst() : this(
        Environment.GetEnvironmentVariable("VIEWGEN_HOME") is { Length: > 0 } h ? h : StandardOrdner, StandardFrist)
    { }

    public ViewgenDienst(string ordner, TimeSpan frist)
    {
        _java = Path.Combine(ordner, "jdk", "bin", "java");
        _lib = Path.Combine(ordner, "app", "lib");
        _frist = frist;
    }

    public bool Installiert => File.Exists(_java) && Directory.Exists(_lib);

    public record Ergebnis(byte[]? Pdf, string? Fehler);

    public static IReadOnlyList<string> Argumente(string lib, string plugin, string xml, string ausgabe) => new[]
    {
        "-Djava.awt.headless=true",
        "-Djava.io.tmpdir=" + ausgabe,
        "-XX:ActiveProcessorCount=2",
        "-Xmx1G",
        "-Dfile.encoding=UTF8",
        "-cp", Path.Combine(lib, "*"),
        "ch.swissdec.viewgen.Viewgen",
        "-p", plugin,
        "-x", xml,
        "-o", ausgabe,
        "-L", "ERROR",
    };

    public async Task<Ergebnis> ErzeugePdfAsync(string xml, string plugin, CancellationToken ct = default)
    {
        if (!Berichte.ContainsKey(plugin)) return new(null, "Diesen Bericht gibt es nicht.");
        if (!Installiert) return new(null, "Viewgen ist auf diesem Server nicht installiert.");

        await NurEiner.WaitAsync(ct);
        var ordner = Directory.CreateTempSubdirectory("viewgen_").FullName; // nur für den Dienstbenutzer lesbar
        try
        {
            var eingabe = Path.Combine(ordner, "meldung.xml");
            await File.WriteAllTextAsync(eingabe, xml, ct);

            var psi = new ProcessStartInfo
            {
                FileName = _java,
                WorkingDirectory = ordner, // Viewgen legt hier seinen Ordner temp/ an
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            psi.Environment["HOME"] = ordner;
            foreach (var a in Argumente(_lib, plugin, eingabe, ordner)) psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc == null) return new(null, "Viewgen liess sich nicht starten.");

            var aus = proc.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var err = proc.StandardError.ReadToEndAsync(CancellationToken.None);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_frist);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* schon beendet */ }
                ct.ThrowIfCancellationRequested();
                return new(null, $"Viewgen hat nach {_frist.TotalSeconds:0} Sekunden nicht geantwortet und wurde beendet.");
            }
            await Task.WhenAll(aus, err);

            var pdf = Directory.GetFiles(ordner, "*.pdf").FirstOrDefault();
            if (pdf != null)
            {
                var bytes = await File.ReadAllBytesAsync(pdf, ct);
                if (bytes.Length > 4 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F')
                    return new(bytes, null);
            }
            return new(null, "Viewgen hat kein PDF erzeugt. " + Kurz(await err + "\n" + await aus));
        }
        finally
        {
            try { Directory.Delete(ordner, recursive: true); } catch { /* Temp-Bereinigung ist kein Abbruchgrund */ }
            NurEiner.Release();
        }
    }

    // Nur die Fehlerzeilen, damit keine Lohndaten aus längeren Ausgaben in die Antwort gelangen.
    private static string Kurz(string text)
    {
        var zeile = text.Split('\n').FirstOrDefault(z => z.Contains("ERROR") || z.Contains("Exception"))?.Trim();
        return zeile == null ? "" : zeile.Length > 200 ? zeile[..200] : zeile;
    }
}
