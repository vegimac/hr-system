using System.Diagnostics;

namespace HrSystem.Services;

// ============================================================================
// Office → PDF Konvertierung via LibreOffice headless (Walter-Vorgabe 24.05.2026).
//
// Treibt die PDF-Vorschau von Word-/Office-Dokumenten in der Dokumentenverwaltung
// (DocumentsController.PreviewPdf). Der Browser kann .doc/.docx nicht direkt
// anzeigen — LibreOffice wandelt sie originalgetreu nach PDF, das dann im
// Vorschaufenster erscheint.
//
// VORAUSSETZUNG (Server): LibreOffice muss installiert sein. Auf dem Ubuntu-VPS:
//   sudo apt install -y libreoffice-writer libreoffice-calc libreoffice-impress
// (oder das volle `libreoffice`). Der Pfad zum Binary kann per EnvVar
// SOFFICE_PATH überschrieben werden; sonst werden Standardpfade probiert.
//
// Lokal (macOS, Walters Mac): /Applications/LibreOffice.app/Contents/MacOS/soffice
// — wird automatisch mitgeprüft, damit die Vorschau auch beim Entwickeln läuft.
// ============================================================================
public class OfficeToPdfService
{
    private readonly ILogger<OfficeToPdfService> _log;
    public OfficeToPdfService(ILogger<OfficeToPdfService> log) => _log = log;

    // Dateitypen, die LibreOffice nach PDF wandeln kann (Word/Excel/PowerPoint
    // + OpenDocument + RTF). Bestimmt auch, was der Endpoint überhaupt annimmt.
    public static readonly string[] ConvertibleExtensions =
        { ".doc", ".docx", ".odt", ".rtf", ".xls", ".xlsx", ".ods", ".ppt", ".pptx", ".odp" };

    public static bool CanConvert(string? filename)
    {
        var ext = Path.GetExtension(filename ?? "").ToLowerInvariant();
        return Array.IndexOf(ConvertibleExtensions, ext) >= 0;
    }

    /// <summary>
    /// Wandelt ein Office-Dokument (Bytes + Originaldateiname für die Endung)
    /// via LibreOffice headless nach PDF. Liefert die PDF-Bytes oder null bei
    /// Fehler (z.B. LibreOffice nicht installiert / Konvertierung gescheitert).
    /// </summary>
    public async Task<byte[]?> ConvertToPdfAsync(byte[] input, string originalFilename, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(originalFilename ?? "").ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = ".docx";

        // Eigenes Arbeits- und Profil-Verzeichnis pro Aufruf → parallele
        // Konvertierungen kollidieren nicht (LibreOffice ist sonst single-instance).
        var workDir    = Path.Combine(Path.GetTempPath(), "lo_conv_" + Guid.NewGuid().ToString("N"));
        var profileDir = Path.Combine(workDir, "profile");
        var inputPath  = Path.Combine(workDir, "input" + ext);
        Directory.CreateDirectory(workDir);

        try
        {
            await File.WriteAllBytesAsync(inputPath, input, ct);

            var soffice = ResolveSofficeBinary();
            var psi = new ProcessStartInfo
            {
                FileName               = soffice,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            // LibreOffice braucht unter systemd ein beschreibbares HOME (sonst
            // scheitert das Anlegen des Profils) — aufs temporäre Arbeitsverz. zeigen.
            psi.Environment["HOME"] = workDir;
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--norestore");
            psi.ArgumentList.Add("--nolockcheck");
            psi.ArgumentList.Add("--nodefault");
            // Eigenes User-Profil → kein $HOME nötig, keine Lock-Kollision.
            psi.ArgumentList.Add($"-env:UserInstallation=file://{profileDir}");
            psi.ArgumentList.Add("--convert-to");
            psi.ArgumentList.Add("pdf");
            psi.ArgumentList.Add("--outdir");
            psi.ArgumentList.Add(workDir);
            psi.ArgumentList.Add(inputPath);

            using var proc = Process.Start(psi);
            if (proc is null) { _log.LogError("LibreOffice ({Bin}) konnte nicht gestartet werden.", soffice); return null; }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(90));   // Erst-Start kann 1–3s dauern; großzügig.
            var stdoutTask = proc.StandardOutput.ReadToEndAsync();
            var stderrTask = proc.StandardError.ReadToEndAsync();
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { /* ignore */ }
                _log.LogError("LibreOffice-Konvertierung hat das Zeitlimit überschritten ({File}).", originalFilename);
                return null;
            }
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var outPdf = Path.Combine(workDir, "input.pdf");
            if (proc.ExitCode != 0 || !File.Exists(outPdf))
            {
                _log.LogError("LibreOffice-Konvertierung fehlgeschlagen (ExitCode {Code}). stdout={Out} stderr={Err}",
                    proc.ExitCode, stdout, stderr);
                return null;
            }
            return await File.ReadAllBytesAsync(outPdf, ct);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Office→PDF Konvertierung fehlgeschlagen für {File}", originalFilename);
            return null;
        }
        finally
        {
            try { Directory.Delete(workDir, true); } catch { /* best effort */ }
        }
    }

    // Findet das soffice-Binary. Reihenfolge: EnvVar SOFFICE_PATH → bekannte
    // Linux-Pfade → macOS-Pfad → blanker Name (PATH-Auflösung).
    private static string ResolveSofficeBinary()
    {
        var env = Environment.GetEnvironmentVariable("SOFFICE_PATH");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

        string[] candidates =
        {
            "/usr/bin/soffice",
            "/usr/bin/libreoffice",
            "/snap/bin/libreoffice",
            "/opt/libreoffice/program/soffice",
            "/Applications/LibreOffice.app/Contents/MacOS/soffice",   // macOS (lokal)
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;

        return "soffice";   // letzte Hoffnung: liegt im PATH
    }
}
