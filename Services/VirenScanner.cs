using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace HrSystem.Services;

public enum ScanStatus { Sauber, Fund, NichtBereit }

public sealed record ScanErgebnis(ScanStatus Status, string? Virus = null, string? Fehler = null)
{
    public static readonly ScanErgebnis Sauber = new(ScanStatus.Sauber);
}

/// <summary>Ergebnis einer manuellen Signatur-Aktualisierung (freshclam).</summary>
public sealed record SignaturUpdateErgebnis(
    bool Ok,
    string Meldung,
    DateTime? Stand = null,
    int? Version = null,
    string? Error = null);

/// <summary>
/// Virenprüfung über den ClamAV-Dienst (clamd) auf dem Server (Walter-Vorgabe 08.10.2026).
/// Die Datei geht über den lokalen Unix-Socket an clamd (INSTREAM) — sie verlässt den Server nicht.
/// Pflicht auf Linux (Prod + Testinstanz); lokal auf dem Mac ohne clamd wird nicht geprüft
/// (überschreibbar mit <c>VirenScanner:Pflicht</c>). Ist clamd nicht erreichbar oder meldet
/// einen Fehler, gilt das als «nicht bereit» — der Aufrufer lehnt den Upload ab.
/// </summary>
public sealed class VirenScanner
{
    private const int Block = 64 * 1024;
    private static readonly TimeSpan Zeitlimit = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan UpdateZeitlimit = TimeSpan.FromMinutes(10);
    private static readonly SemaphoreSlim UpdateSperre = new(1, 1);

    private readonly string _socket;
    private readonly string _datenbank;
    private readonly string _updateBefehl;
    private readonly ILogger<VirenScanner> _log;

    public bool Pflicht { get; }

    public VirenScanner(IConfiguration config, ILogger<VirenScanner> log)
    {
        _log = log;
        _socket = config["VirenScanner:Socket"] is { Length: > 0 } s ? s : "/var/run/clamav/clamd.ctl";
        _datenbank = config["VirenScanner:Datenbank"] is { Length: > 0 } d ? d : "/var/lib/clamav";
        // Standard: Hilfsskript per sudo (schreibt als root in /var/lib/clamav).
        _updateBefehl = config["VirenScanner:UpdateBefehl"] is { Length: > 0 } u
            ? u
            : "sudo -n /usr/local/sbin/hr-clamav-update";
        Pflicht = config.GetValue<bool?>("VirenScanner:Pflicht") ?? OperatingSystem.IsLinux();
    }

    public Task<ScanErgebnis> PruefeAsync(byte[] daten, CancellationToken ct = default)
        => PruefeAsync(new MemoryStream(daten, writable: false), ct);

    public async Task<ScanErgebnis> PruefeAsync(Stream daten, CancellationToken ct = default)
    {
        if (!Pflicht) return ScanErgebnis.Sauber;
        using var zeit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        zeit.CancelAfter(Zeitlimit);
        try
        {
            using var sock = await VerbindenAsync(zeit.Token);
            await using var ns = new NetworkStream(sock, ownsSocket: false);
            await ns.WriteAsync(Encoding.ASCII.GetBytes("zINSTREAM\0"), zeit.Token);

            var puffer = new byte[Block];
            var laenge = new byte[4];
            int n;
            while ((n = await daten.ReadAsync(puffer, zeit.Token)) > 0)
            {
                BinaryPrimitives.WriteUInt32BigEndian(laenge, (uint)n);
                await ns.WriteAsync(laenge, zeit.Token);
                await ns.WriteAsync(puffer.AsMemory(0, n), zeit.Token);
            }
            BinaryPrimitives.WriteUInt32BigEndian(laenge, 0);
            await ns.WriteAsync(laenge, zeit.Token);

            var antwort = await LiesAntwortAsync(ns, zeit.Token);
            var erg = Auswerten(antwort);
            if (erg.Status == ScanStatus.NichtBereit)
                _log.LogWarning("Virenscanner meldet Fehler: {Antwort}", antwort);
            return erg;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Virenscanner nicht erreichbar ({Socket})", _socket);
            return new ScanErgebnis(ScanStatus.NichtBereit, Fehler: ex.Message);
        }
    }

    /// <summary>clamd-Antwort auf INSTREAM: «stream: OK», «stream: Name FOUND» oder «… ERROR».</summary>
    public static ScanErgebnis Auswerten(string? antwort)
    {
        var a = (antwort ?? "").Trim('\0', ' ', '\n', '\r');
        if (a.EndsWith(" FOUND", StringComparison.Ordinal))
        {
            var teil = a[..^" FOUND".Length];
            var trenner = teil.IndexOf(": ", StringComparison.Ordinal);
            var virus = trenner >= 0 ? teil[(trenner + 2)..] : teil;
            return new ScanErgebnis(ScanStatus.Fund, Virus: virus.Length > 0 ? virus : "unbekannt");
        }
        if (a == "OK" || a.EndsWith(": OK", StringComparison.Ordinal))
            return ScanErgebnis.Sauber;
        return new ScanErgebnis(ScanStatus.NichtBereit, Fehler: a.Length == 0 ? "keine Antwort vom Virenscanner" : a);
    }

    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            using var zeit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            zeit.CancelAfter(TimeSpan.FromSeconds(5));
            using var sock = await VerbindenAsync(zeit.Token);
            await using var ns = new NetworkStream(sock, ownsSocket: false);
            await ns.WriteAsync(Encoding.ASCII.GetBytes("zPING\0"), zeit.Token);
            return (await LiesAntwortAsync(ns, zeit.Token)).Trim('\0', '\n') == "PONG";
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stand der Tages-Signaturen aus dem Kopf von daily.cld/daily.cvd
    /// («ClamAV-VDB:08 Oct 2026 07-49 +0000:28147:…»). clamd liefert VERSION unter Ubuntu nicht.
    /// </summary>
    public (DateTime? Stand, int? Version) Signaturen()
    {
        try
        {
            var datei = new[] { "daily.cld", "daily.cvd" }
                .Select(n => new FileInfo(Path.Combine(_datenbank, n)))
                .Where(f => f.Exists)
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();
            if (datei == null) return (null, null);

            var kopf = new byte[512];
            using (var fs = datei.OpenRead())
                fs.ReadExactly(kopf, 0, Math.Min(kopf.Length, (int)datei.Length));
            var teile = Encoding.ASCII.GetString(kopf).Split(':');
            if (teile.Length < 3 || teile[0] != "ClamAV-VDB") return (null, null);

            DateTime? stand = null;
            var datum = teile[1].Trim();
            var ohneZone = datum.Length > 6 ? datum[..^6] : datum;
            if (DateTime.TryParseExact(ohneZone, "dd MMM yyyy HH-mm", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var utc))
                stand = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime();
            int? version = int.TryParse(teile[2], out var v) ? v : null;
            return (stand, version);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Signaturen jetzt aktualisieren (Walter 22.10.2026): ruft <c>hr-clamav-update</c>
    /// (freshclam) auf. Nur auf dem Server sinnvoll; lokal ohne ClamAV → klare Meldung.
    /// </summary>
    public async Task<SignaturUpdateErgebnis> SignaturenAktualisierenAsync(CancellationToken ct = default)
    {
        if (!Pflicht)
            return new(false, "Auf diesem Rechner läuft kein Virenscanner — Signaturen werden nur auf dem Server aktualisiert.",
                Error: "KEIN_SCANNER");

        if (!await UpdateSperre.WaitAsync(0, ct))
            return new(false, "Eine Aktualisierung läuft bereits — bitte kurz warten.", Error: "LAEUFT_SCHON");

        try
        {
            using var zeit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            zeit.CancelAfter(UpdateZeitlimit);

            var teile = _updateBefehl.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (teile.Length == 0)
                return new(false, "Kein Update-Befehl konfiguriert (VirenScanner:UpdateBefehl).", Error: "KONFIG");

            var psi = new ProcessStartInfo
            {
                FileName = teile[0],
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            for (var i = 1; i < teile.Length; i++)
                psi.ArgumentList.Add(teile[i]);

            using var proc = new Process { StartInfo = psi };
            try
            {
                if (!proc.Start())
                    return new(false, "Aktualisierung konnte nicht gestartet werden.", Error: "START");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Signatur-Update Start fehlgeschlagen ({Befehl})", _updateBefehl);
                return new(false, $"Aktualisierung fehlgeschlagen: {ex.Message}", Error: "START");
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(zeit.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(zeit.Token);
            try
            {
                await proc.WaitForExitAsync(zeit.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return new(false, "Aktualisierung abgebrochen (Zeitlimit).", Error: "TIMEOUT");
            }

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();
            var ausgabe = string.Join("\n", new[] { stdout, stderr }.Where(s => s.Length > 0));
            var (stand, version) = Signaturen();

            if (proc.ExitCode != 0)
            {
                var setupFehlt = ausgabe.Contains("password is required", StringComparison.OrdinalIgnoreCase)
                    || ausgabe.Contains("a terminal is required", StringComparison.OrdinalIgnoreCase)
                    || ausgabe.Contains("no password was provided", StringComparison.OrdinalIgnoreCase)
                    || ausgabe.Contains("command not found", StringComparison.OrdinalIgnoreCase)
                    || ausgabe.Contains("unable to locate", StringComparison.OrdinalIgnoreCase)
                    || (proc.ExitCode == 1 && ausgabe.Length == 0 && _updateBefehl.Contains("sudo", StringComparison.Ordinal))
                    || (_updateBefehl.Contains("hr-clamav-update", StringComparison.Ordinal)
                        && !File.Exists("/usr/local/sbin/hr-clamav-update"));
                if (setupFehlt)
                {
                    _log.LogWarning("Signatur-Update: Hilfsskript/sudo fehlt (Exit {Code}): {Out}", proc.ExitCode, ausgabe);
                    return new(false,
                        "Signatur-Update ist auf dem Server noch nicht freigeschaltet. Einmal als root installieren: "
                        + "sudo install -m 755 …/server/hr-clamav-update /usr/local/sbin/hr-clamav-update && "
                        + "sudo install -m 440 …/server/hr-clamav-update.sudoers /etc/sudoers.d/hr-clamav-update",
                        stand, version, "SETUP_FEHLT");
                }

                _log.LogWarning("Signatur-Update Exit {Code}: {Out}", proc.ExitCode, ausgabe);
                var kurz = ausgabe.Length > 400 ? ausgabe[..400] + "…" : ausgabe;
                return new(false,
                    string.IsNullOrEmpty(kurz) ? $"Aktualisierung fehlgeschlagen (Exit {proc.ExitCode})." : kurz,
                    stand, version, "FEHLER");
            }

            _log.LogInformation("Signatur-Update ok — Stand {Stand} Version {Version}", stand, version);
            var msg = stand != null
                ? $"Signaturen aktualisiert — Stand {stand:dd.MM.yyyy HH:mm}"
                  + (version != null ? $" (Version {version})" : "") + "."
                : "Signatur-Update abgeschlossen.";
            return new(true, msg, stand, version);
        }
        finally
        {
            UpdateSperre.Release();
        }
    }

    private async Task<Socket> VerbindenAsync(CancellationToken ct)
    {
        var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await sock.ConnectAsync(new UnixDomainSocketEndPoint(_socket), ct);
            return sock;
        }
        catch
        {
            sock.Dispose();
            throw;
        }
    }

    private static async Task<string> LiesAntwortAsync(NetworkStream ns, CancellationToken ct)
    {
        var puffer = new byte[4096];
        var gelesen = 0;
        while (gelesen < puffer.Length)
        {
            var n = await ns.ReadAsync(puffer.AsMemory(gelesen), ct);
            if (n == 0) break;
            gelesen += n;
            if (puffer[gelesen - 1] == 0) break;
        }
        return Encoding.UTF8.GetString(puffer, 0, gelesen);
    }
}
