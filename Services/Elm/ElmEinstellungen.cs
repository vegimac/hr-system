using System.Xml.Linq;

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
/// Gesetzt wird sie über `Swissdec:MonitoringId` (appsettings oder
/// `Swissdec__MonitoringId` in der systemd-Umgebung). Leer = kein Element im XML,
/// denn im Schema ist es optional und in der Produktion soll es fehlen.
/// </summary>
public class ElmEinstellungen
{
    /// <summary>Laut Schema (`MonitoringIDType`) 1–32 Zeichen.</summary>
    public const int MaxLaenge = 32;

    public string? MonitoringId { get; }

    public ElmEinstellungen(IConfiguration config)
    {
        var wert = (config["Swissdec:MonitoringId"] ?? "").Trim();
        MonitoringId = wert.Length == 0 ? null
            : wert.Length > MaxLaenge ? wert[..MaxLaenge]
            : wert;
    }

    /// <summary>Das Element für den Request — NULL, wenn keine ID gesetzt ist.</summary>
    public XElement? MonitoringElement(XNamespace ep)
        => MonitoringId == null ? null : new XElement(ep + "MonitoringID", MonitoringId);
}
