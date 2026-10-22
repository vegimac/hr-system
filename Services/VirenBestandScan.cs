using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Einmaliger Durchlauf über die ganze Dokumentablage (Documents:StoragePath) — Walter-Vorgabe
/// 08.10.2026. Läuft im Hintergrund, nur einer gleichzeitig; Fortschritt über <see cref="Stand"/>.
/// Funde werden gemeldet, die Datei bleibt liegen (ein Admin prüft und löscht sie).
/// Ist der Scanner unterwegs nicht mehr bereit, bricht der Durchlauf ab statt Dateien zu überspringen.
/// Parallelität (Walter 22.10.2026): mehrere Dateien gleichzeitig an clamd — sonst dauert
/// ein Bestand mit 10k+ Dateien viele Stunden (strikt nacheinander).
/// </summary>
public sealed class VirenBestandScan
{
    /// <summary>Gleichzeitige Prüfungen im Bestands-Scan. 1 Slot bleibt für Live-Uploads frei
    /// (siehe <see cref="VirenScanner"/>-Gesamtlimit). Nicht «Parallel» nennen — sonst
    /// verdeckt die Konstante <c>System.Threading.Tasks.Parallel</c>.</summary>
    private const int MaxGleichzeitig = 3;

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
        {
            var sekunden = _gestartetAm.HasValue
                ? Math.Max(1, (int)((_beendetAm ?? DateTime.Now) - _gestartetAm.Value).TotalSeconds)
                : 0;
            var proSek = sekunden > 0 && _geprueft > 0 ? Math.Round(_geprueft / (double)sekunden, 1) : 0;
            int? etaSek = null;
            if (_laeuft && proSek > 0 && _gesamt > _geprueft)
                etaSek = (int)Math.Ceiling((_gesamt - _geprueft) / proSek);

            return new
            {
                laeuft = _laeuft, gestartetAm = _gestartetAm, beendetAm = _beendetAm, gestartetVon = _gestartetVon,
                gesamt = _gesamt, geprueft = _geprueft, funde = _funde, unlesbar = _unlesbar, fehler = _fehler,
                dateienProSekunde = proSek,
                etaSekunden = etaSek,
                parallel = MaxGleichzeitig,
            };
        }
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
        using var abbruch = new CancellationTokenSource();
        try
        {
            var dateien = Directory.Exists(_root)
                ? Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToList()
                : new List<string>();
            lock (_sperre) _gesamt = dateien.Count;
            _log.LogInformation("Viren-Bestandsscan gestartet: {N} Dateien unter {Root}, parallel={P}",
                dateien.Count, _root, MaxGleichzeitig);

            var opts = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxGleichzeitig,
                CancellationToken = abbruch.Token,
            };

            try
            {
                await System.Threading.Tasks.Parallel.ForEachAsync(dateien, opts, async (pfad, ct) =>
                {
                    ScanErgebnis erg;
                    try
                    {
                        // Pfad-Scan (CONTSCAN), falls clamd die Datei lesen darf — sonst Stream.
                        erg = await _scanner.PruefePfadOderStreamAsync(pfad, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        lock (_sperre) { _unlesbar++; _geprueft++; }
                        return;
                    }

                    if (erg.Status == ScanStatus.NichtBereit)
                    {
                        lock (_sperre)
                        {
                            if (_fehler == null)
                                _fehler = $"Abgebrochen: Virenscanner nicht bereit ({erg.Fehler}). Bitte später neu starten.";
                        }
                        try { abbruch.Cancel(); } catch { /* ignore */ }
                        return;
                    }
                    if (erg.Status == ScanStatus.Fund)
                    {
                        try { await FundSpeichernAsync(pfad, erg.Virus!); }
                        catch (Exception ex)
                        {
                            _log.LogWarning(ex, "Fund speichern fehlgeschlagen ({Pfad})", pfad);
                        }
                        lock (_sperre) _funde++;
                    }
                    lock (_sperre) _geprueft++;
                });
            }
            catch (OperationCanceledException)
            {
                // Abbruch wegen NichtBereit — Fehlertext schon gesetzt.
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
