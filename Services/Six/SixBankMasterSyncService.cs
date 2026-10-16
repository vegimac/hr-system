using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.Six;

/// <summary>
/// Holt den Bankenstamm von SIX und ersetzt <c>bank_master</c>.
/// MA-Bankverbindungen bleiben unberührt (BIC/Name dort gespeichert).
/// </summary>
public sealed class SixBankMasterSyncService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly SixBankMasterClient _client;
    private readonly BankLookupService _lookup;
    private readonly ILogger<SixBankMasterSyncService> _log;
    private int _laufAktiv;

    public DateTime? LetzterStart { get; private set; }
    public DateTime? LetztesEnde { get; private set; }
    public string? LetzterAusloeser { get; private set; }
    public SixBankMasterSyncErgebnis? LetztesErgebnis { get; private set; }
    public bool LaeuftGerade => Volatile.Read(ref _laufAktiv) == 1;

    public SixBankMasterSyncService(
        IServiceScopeFactory scopes,
        SixBankMasterClient client,
        BankLookupService lookup,
        ILogger<SixBankMasterSyncService> log)
    {
        _scopes = scopes;
        _client = client;
        _lookup = lookup;
        _log = log;
    }

    public async Task<SixBankMasterSyncErgebnis> SyncAsync(string ausloeser, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _laufAktiv, 1, 0) != 0)
            throw new InvalidOperationException("SIX-Bankenstamm-Sync läuft bereits.");

        LetzterStart = DateTime.Now;
        LetztesEnde = null;
        LetzterAusloeser = ausloeser;
        try
        {
            var erg = await SyncKernAsync(ct);
            LetztesErgebnis = erg;
            _log.LogInformation(
                "SIX Bankenstamm: {Count} Banken (validOn {ValidOn}) — Auslöser {Ausloeser}.",
                erg.Anzahl, erg.ValidOn?.ToString("yyyy-MM-dd") ?? "?", ausloeser);
            return erg;
        }
        catch (Exception ex)
        {
            LetztesErgebnis = new SixBankMasterSyncErgebnis(0, null, DateTime.Now, false, ex.Message);
            _log.LogError(ex, "SIX Bankenstamm-Sync fehlgeschlagen ({Ausloeser}).", ausloeser);
            throw;
        }
        finally
        {
            LetztesEnde = DateTime.Now;
            Volatile.Write(ref _laufAktiv, 0);
        }
    }

    async Task<SixBankMasterSyncErgebnis> SyncKernAsync(CancellationToken ct)
    {
        var resp = await _client.FetchBankMasterAsync(ct);
        var rows = MapEntries(resp.Entries);
        if (rows.Count < 500)
            throw new InvalidOperationException(
                $"SIX lieferte nur {rows.Count} Banken — Abbruch (erwartet ~1000+).");

        var now = DateTime.Now;
        foreach (var r in rows) r.ImportedAt = now;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.ExecuteSqlRawAsync("DELETE FROM bank_master", ct);
        db.BankMasters.AddRange(rows);
        await db.SaveChangesAsync(ct);
        await _lookup.ReloadAsync();

        return new SixBankMasterSyncErgebnis(rows.Count, resp.ValidOn, now, true, null);
    }

    /// <summary>Nur echte BankMaster-Zeilen; Concatenated (Fusion) werden übersprungen.</summary>
    public static List<BankMaster> MapEntries(IEnumerable<SixBankMasterEntry> entries)
    {
        var list = new List<BankMaster>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var e in entries)
        {
            if (!string.Equals(e.EntryType, "BankMaster", StringComparison.OrdinalIgnoreCase))
                continue;
            if (e.Iid <= 0) continue;
            var name = (e.BankOrInstitutionName ?? "").Trim();
            if (name.Length == 0) continue;

            var iid = e.Iid.ToString();
            if (!seen.Add(iid)) continue;

            var strasse = JoinStrasse(e.StreetName, e.BuildingNumber);
            list.Add(new BankMaster
            {
                Iid = iid,
                Bic = string.IsNullOrWhiteSpace(e.Bic) ? null : e.Bic.Trim(),
                Name = name,
                Ort = string.IsNullOrWhiteSpace(e.TownName) ? null : e.TownName.Trim(),
                Strasse = strasse,
                Plz = string.IsNullOrWhiteSpace(e.PostCode) ? null : e.PostCode.Trim()
            });
        }
        return list;
    }

    static string? JoinStrasse(string? street, string? nr)
    {
        street = street?.Trim();
        nr = nr?.Trim();
        if (string.IsNullOrEmpty(street) && string.IsNullOrEmpty(nr)) return null;
        if (string.IsNullOrEmpty(nr)) return street;
        if (string.IsNullOrEmpty(street)) return nr;
        return $"{street} {nr}";
    }
}

public sealed record SixBankMasterSyncErgebnis(
    int Anzahl,
    DateOnly? ValidOn,
    DateTime FertigAm,
    bool Ok,
    string? Fehler);
