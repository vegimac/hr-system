using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Einmaliger Durchlauf über die ganze Dokumentablage (Documents:StoragePath) — Walter-Vorgabe
/// 08.10.2026. Läuft im Hintergrund, nur einer gleichzeitig; Fortschritt über <see cref="Stand"/>.
/// Funde werden gemeldet, die Datei bleibt liegen (ein Admin prüft und löscht sie).
/// Ist der Scanner unterwegs nicht mehr bereit, bricht der Durchlauf ab statt Dateien zu überspringen.
/// </summary>
public sealed class VirenBestandScan
{
    private readonly IServiceScopeFactory _scopes;
    private readonly VirenScanner _scanner;
    private readonly ILogger<VirenBestandScan> _log;
    private readonly string _root;
    private readonly object _sperre = new();

    private bool _laeuft;
    private DateTime? _gestartetAm, _beendetAm;
    private string? _gestartetVon, _fehler;
    private int _gesamt, _geprueft, _funde, _unlesbar;

    public VirenBestandScan(IServiceScopeFactory scopes, VirenScanner scanner, ILogger<VirenBestandScan> log,
                            IConfiguration config, IWebHostEnvironment env)
    {
        _scopes = scopes;
        _scanner = scanner;
        _log = log;
        var configured = config["Documents:StoragePath"];
        _root = string.IsNullOrWhiteSpace(configured) ? Path.Combine(env.ContentRootPath, "data", "documents") : configured;
    }

    public object Stand()
    {
        lock (_sperre)
            return new
            {
                laeuft = _laeuft, gestartetAm = _gestartetAm, beendetAm = _beendetAm, gestartetVon = _gestartetVon,
                gesamt = _gesamt, geprueft = _geprueft, funde = _funde, unlesbar = _unlesbar, fehler = _fehler,
            };
    }

    public bool Starten(string? von)
    {
        lock (_sperre)
        {
            if (_laeuft) return false;
            _laeuft = true;
            _gestartetAm = DateTime.Now;
            _beendetAm = null;
            _gestartetVon = von;
            _fehler = null;
            _gesamt = _geprueft = _funde = _unlesbar = 0;
        }
        _ = Task.Run(LaufAsync);
        return true;
    }

    private async Task LaufAsync()
    {
        try
        {
            var dateien = Directory.Exists(_root)
                ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToList()
                : new List<string>();
            lock (_sperre) _gesamt = dateien.Count;
            _log.LogInformation("Viren-Bestandsscan gestartet: {N} Dateien unter {Root}", dateien.Count, _root);

            foreach (var pfad in dateien)
            {
                ScanErgebnis erg;
                try
                {
                    await using var fs = File.OpenRead(pfad);
                    erg = await _scanner.PruefeAsync(fs);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lock (_sperre) { _unlesbar++; _geprueft++; }
                    continue;
                }

                if (erg.Status == ScanStatus.NichtBereit)
                {
                    lock (_sperre) _fehler = $"Abgebrochen: Virenscanner nicht bereit ({erg.Fehler}). Bitte später neu starten.";
                    break;
                }
                if (erg.Status == ScanStatus.Fund)
                {
                    await FundSpeichernAsync(pfad, erg.Virus!);
                    lock (_sperre) _funde++;
                }
                lock (_sperre) _geprueft++;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Viren-Bestandsscan fehlgeschlagen");
            lock (_sperre) _fehler = ex.Message;
        }
        finally
        {
            lock (_sperre)
            {
                _beendetAm = DateTime.Now;
                _laeuft = false;
            }
            _log.LogInformation("Viren-Bestandsscan beendet: {Geprueft}/{Gesamt} geprüft, {Funde} Funde", _geprueft, _gesamt, _funde);
        }
    }

    private async Task FundSpeichernAsync(string pfad, string virus)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ort = Path.GetRelativePath(_root, pfad);
        if (await db.VirenFunde.AnyAsync(f => f.Quelle == VirenFund.QuelleBestand && f.Ort == ort && !f.Erledigt))
            return;

        var name = Path.GetFileName(pfad);
        var dok = await db.EmployeeDokumente.AsNoTracking()
            .Where(d => d.FilenameStorage == name)
            .Select(d => new { d.EmployeeId, d.FilenameOriginal })
            .FirstOrDefaultAsync();
        var post = dok != null ? null : await db.MailboxDocuments.AsNoTracking()
            .Where(d => d.StorageFilename == name)
            .Select(d => new { d.EmployeeId, d.OriginalFilename })
            .FirstOrDefaultAsync();

        await VirenScanFilter.FundMeldenAsync(db, VirenFund.QuelleBestand,
            dok?.FilenameOriginal ?? post?.OriginalFilename ?? name, virus, ort,
            null, null, dok?.EmployeeId ?? post?.EmployeeId, _log);
    }
}
