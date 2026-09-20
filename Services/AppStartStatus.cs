namespace HrSystem.Services;

/// <summary>
/// Nach einem Deploy ist die App erst hörbereit, dann noch am Warmlaufen
/// (EF-Modell, erste HTTP-Pfade, QST-Tarife). Die Login-Sanduhr und
/// <c>/api/instance-info.laden</c> lesen dieses Flag.
/// </summary>
public static class AppStartStatus
{
    public static bool Bereit { get; set; }
}
