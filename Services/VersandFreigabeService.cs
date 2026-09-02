using HrSystem.Data;
using HrSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Liest die Freigabe-Matrix (Tabelle versand_kategorie) und beantwortet
/// die einzige Frage, die Mail und SMS beide stellen: «Darf diese
/// Kategorie auf diesem Kanal scharf raus?»
///
/// Ein Dienst für beide Kanäle ist Absicht: eine Kandidaten-Absage geht
/// je nach Fall per SMS ODER per Mail raus. Zwei getrennte Schalter
/// dafür wären eine Falle — man schaltet einen frei und wundert sich,
/// warum der andere Kanal sich anders verhält.
///
/// Caching: 30 Sekunden, analog EmailService/SMTP-Konfig. Der Admin-
/// Controller ruft nach dem Speichern InvalidateCache() auf, damit die
/// Änderung sofort wirkt.
///
/// FAIL-SAFE: Fehlt eine Zeile, ist die Tabelle leer oder scheitert der
/// DB-Zugriff, gilt die Kategorie als NICHT scharf. Ein Ausfall führt
/// also zur Umleitung, nie zum ungewollten Scharfschalten.
/// </summary>
public class VersandFreigabeService
{
    private readonly AppDbContext _db;
    private readonly ILogger<VersandFreigabeService> _log;

    private static Dictionary<string, (bool Mail, bool Sms)>? _cache;
    private static DateTime _cacheUntil = DateTime.MinValue;
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public VersandFreigabeService(AppDbContext db, ILogger<VersandFreigabeService> log)
    {
        _db = db;
        _log = log;
    }

    public enum Kanal { Mail, Sms }

    /// <summary>
    /// true = scharf an den echten Empfänger, false = Test-Umleitung.
    /// </summary>
    public async Task<bool> IstScharfAsync(VersandKategorie kategorie, Kanal kanal,
                                           CancellationToken ct = default)
    {
        var map = await GetMapAsync(ct);
        if (!map.TryGetValue(VersandKategorien.Code(kategorie), out var haken))
            return false;                      // keine Zeile = nicht scharf
        return kanal == Kanal.Mail ? haken.Mail : haken.Sms;
    }

    /// <summary>Alle Haken — für die Systemsteuerung.</summary>
    public async Task<Dictionary<string, (bool Mail, bool Sms)>> GetMapAsync(CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_cache != null && DateTime.UtcNow < _cacheUntil) return _cache;
        }

        Dictionary<string, (bool, bool)> map;
        try
        {
            map = await _db.VersandKategorien.AsNoTracking()
                .ToDictionaryAsync(r => r.Code, r => (r.MailScharf, r.SmsScharf), ct);
        }
        catch (Exception ex)
        {
            // Tabelle fehlt (Migration noch nicht gelaufen) o.ä. — dann gilt
            // für alles «nicht scharf», also Umleitung. Bewusst kein Throw:
            // ein Konfigurationsproblem darf keinen Lohnlauf abbrechen.
            _log.LogError(ex, "[VersandFreigabe] Matrix nicht lesbar — alles gilt als NICHT scharf.");
            map = new Dictionary<string, (bool, bool)>();
        }

        lock (_cacheLock)
        {
            _cache = map;
            _cacheUntil = DateTime.UtcNow + CacheTtl;
        }
        return map;
    }

    public static void InvalidateCache()
    {
        lock (_cacheLock)
        {
            _cache = null;
            _cacheUntil = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Fehlende Kategorie-Zeilen anlegen (Standard aus dem Code). Wird vom
    /// Admin-Controller beim Laden aufgerufen, damit eine neu im Code
    /// ergänzte Kategorie ohne SQL-Skript in der Systemsteuerung auftaucht.
    /// </summary>
    public async Task EnsureRowsAsync(CancellationToken ct = default)
    {
        var vorhanden = await _db.VersandKategorien.Select(r => r.Code).ToListAsync(ct);
        var fehlend = VersandKategorien.All
            .Where(i => !vorhanden.Contains(i.Code))
            .Select(i => new VersandKategorieSetting
            {
                Code       = i.Code,
                MailScharf = i.StandardScharf && i.NutztMail,
                SmsScharf  = i.StandardScharf && i.NutztSms,
                UpdatedAt  = DateTime.Now,
            })
            .ToList();
        if (fehlend.Count == 0) return;

        _db.VersandKategorien.AddRange(fehlend);
        await _db.SaveChangesAsync(ct);
        InvalidateCache();
        _log.LogInformation("[VersandFreigabe] {N} fehlende Kategorie-Zeilen angelegt.", fehlend.Count);
    }
}
