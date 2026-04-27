using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Schlägt Schweizer Bankdaten anhand der Institut-ID (IID) einer IBAN nach.
/// Daten kommen aus der Tabelle bank_master (wird über Admin-UI importiert).
/// Der Service hält einen In-Memory Cache; bei Bedarf via <see cref="Reload"/>
/// neu befüllen (z. B. nach CSV-Import).
///
/// Erstbefüllung: wenn bank_master leer ist und Data/bank_master.csv
/// existiert, wird die CSV als Initial-Seed in die DB geschrieben.
///
/// Bewusst offline / lokal: keine IBAN verlässt den Server.
/// </summary>
public class BankLookupService
{
    public record BankInfo(
        string Iid,
        string? Bic,
        string Name,
        string? Ort,
        string? Strasse,
        string? Plz
    );

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BankLookupService> _logger;
    private readonly IHostEnvironment _env;
    private readonly SemaphoreSlim _reloadLock = new(1, 1);
    private Dictionary<string, BankInfo> _byIid = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized = false;

    public BankLookupService(
        IServiceScopeFactory scopeFactory,
        ILogger<BankLookupService> logger,
        IHostEnvironment env)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
        _env          = env;
    }

    /// <summary>
    /// IBAN → BankInfo für CH/LI. Liefert null wenn keine gültige IID
    /// extrahierbar oder kein Eintrag in der DB.
    /// </summary>
    public BankInfo? LookupByIban(string? iban)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(iban)) return null;
        var clean = new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
        if (clean.Length < 9) return null;
        if (!clean.StartsWith("CH") && !clean.StartsWith("LI")) return null;

        var iid = clean.Substring(4, 5);
        return _byIid.TryGetValue(iid, out var info) ? info : null;
    }

    /// <summary>Cache neu befüllen — nach CSV-Import oder manueller Änderung.</summary>
    public async Task ReloadAsync()
    {
        await _reloadLock.WaitAsync();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var list = await db.BankMasters.AsNoTracking().ToListAsync();
            _byIid = list.ToDictionary(
                b => b.Iid,
                b => new BankInfo(b.Iid, b.Bic, b.Name, b.Ort, b.Strasse, b.Plz),
                StringComparer.OrdinalIgnoreCase);
            _initialized = true;
            _logger.LogInformation("BankLookupService: {Count} Einträge aus DB geladen.", _byIid.Count);
        }
        finally
        {
            _reloadLock.Release();
        }
    }

    /// <summary>Lädt die Initial-CSV wenn bank_master leer — nur einmal beim Start.</summary>
    public async Task SeedFromCsvIfEmptyAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (await db.BankMasters.AnyAsync()) return;   // schon Daten drin

        string path = Path.Combine(_env.ContentRootPath, "Data", "bank_master.csv");
        if (!File.Exists(path))
        {
            _logger.LogWarning("bank_master.csv für Initial-Seed nicht gefunden ({Path}). Tabelle bleibt leer — per Admin-UI importieren.", path);
            return;
        }

        try
        {
            int count = 0;
            var now = DateTime.UtcNow;
            foreach (var (line, i) in File.ReadLines(path).Select((l, i) => (l, i)))
            {
                if (i == 0) continue;
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                var cols = line.Split(';');
                if (cols.Length < 3) continue;
                var iid = cols[0].Trim();
                if (string.IsNullOrEmpty(iid)) continue;
                db.BankMasters.Add(new BankMaster
                {
                    Iid        = iid,
                    Bic        = Get(cols, 1),
                    Name       = Get(cols, 2) ?? iid,
                    Ort        = Get(cols, 3),
                    Strasse    = Get(cols, 4),
                    Plz        = Get(cols, 5),
                    ImportedAt = now
                });
                count++;
            }
            if (count > 0) await db.SaveChangesAsync();
            _logger.LogInformation("BankLookupService: Initial-Seed {Count} Einträge aus CSV.", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fehler beim Initial-Seed von bank_master.csv");
        }

        static string? Get(string[] cols, int idx)
            => idx < cols.Length && !string.IsNullOrWhiteSpace(cols[idx]) ? cols[idx].Trim() : null;
    }

    private void EnsureInitialized()
    {
        if (_initialized) return;
        // Blocking: wird nur beim allerersten Lookup ausgelöst.
        ReloadAsync().GetAwaiter().GetResult();
    }
}
