using System.Xml.Linq;
using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services.Elm;

/// <summary>
/// Einstellungen für die Swissdec-Übermittlung, die pro Installation anders sind.
///
/// **MonitoringID** (Walter 24.09.2026): Die Richtlinie sagt dazu — «Diese ID wird verwendet,
/// um auf den Swissdec Testsystemen den Softwarehersteller zu identifizieren. Die MonitoringID
/// ist für die Verwendung der Testsysteme **zwingend**, wird aber im produktiven Umfeld nicht
/// gebraucht (sollte auf produktiven Systemen leer sein).» In der Referenzapplikation teilt sie
/// die eingehenden Daten dem richtigen Benutzer zu — ohne sie findet man seine eigene
/// Übermittlung dort nicht wieder.
///
/// <para>
/// Quelle (Walter 27.09.2026, in dieser Reihenfolge): der in der ELM-Werkstatt gespeicherte
/// Wert (<c>app_setting</c>, Schlüssel <see cref="SettingKey"/>), sonst die Server-Variable
/// <c>Swissdec:MonitoringId</c> (bzw. <c>Swissdec__MonitoringId</c> in systemd). Leer = kein
/// Element im XML — im Schema ist es optional, und auf der Produktion soll es fehlen.
/// </para>
/// </summary>
public class ElmEinstellungen
{
    /// <summary>Laut Schema (`MonitoringIDType`) 1–32 Zeichen.</summary>
    public const int MaxLaenge = 32;

    public const string SettingKey = "Elm.MonitoringId";

    private readonly string? _ausKonfiguration;
    private readonly IServiceScopeFactory? _scopes;

    private string? _ausDb;
    private DateTime _dbGelesen = DateTime.MinValue;
    private static readonly TimeSpan CacheDauer = TimeSpan.FromSeconds(30);
    private readonly object _sperre = new();

    public ElmEinstellungen(IConfiguration config, IServiceScopeFactory? scopes = null)
    {
        _ausKonfiguration = Bereinige(config["Swissdec:MonitoringId"]);
        _scopes = scopes;
    }

    /// <summary>Trimmen, leer = nicht gesetzt, auf 32 Zeichen kürzen.</summary>
    public static string? Bereinige(string? wert)
    {
        var w = (wert ?? "").Trim();
        return w.Length == 0 ? null : w.Length > MaxLaenge ? w[..MaxLaenge] : w;
    }

    /// <summary>Gespeicherter Wert zuerst, sonst die Server-Variable.</summary>
    public string? MonitoringId => AusDatenbank() ?? _ausKonfiguration;

    /// <summary>Nur der gespeicherte Wert (für die Anzeige in der Werkstatt).</summary>
    public string? MonitoringIdGespeichert => AusDatenbank();

    /// <summary>Nur die Server-Variable (für die Anzeige in der Werkstatt).</summary>
    public string? MonitoringIdAusKonfiguration => _ausKonfiguration;

    private string? AusDatenbank()
    {
        if (_scopes == null) return null;
        lock (_sperre)
        {
            if (DateTime.Now - _dbGelesen < CacheDauer) return _ausDb;
            try
            {
                using var scope = _scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var wert = db.AppSettings.AsNoTracking()
                    .Where(x => x.Key == SettingKey)
                    .Select(x => x.Value)
                    .FirstOrDefault();
                _ausDb = Bereinige(wert);
            }
            catch
            {
                // Beim Start (Migration läuft noch) darf das Lesen scheitern — dann
                // gilt die Server-Variable. Nie den ganzen Request daran hängen.
                _ausDb = null;
            }
            _dbGelesen = DateTime.Now;
            return _ausDb;
        }
    }

    /// <summary>Nach dem Speichern den Zwischenspeicher verwerfen.</summary>
    public void Verwerfen()
    {
        lock (_sperre) { _dbGelesen = DateTime.MinValue; }
    }

    /// <summary>Das Element für den Request — NULL, wenn keine ID gesetzt ist.</summary>
    public XElement? MonitoringElement(XNamespace ep)
        => MonitoringId is { } id ? new XElement(ep + "MonitoringID", id) : null;
}
