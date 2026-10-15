namespace HrSystem.Services.Elm;

/// <summary>
/// Erneuert das SUA-Zertifikat kurz vor Ablauf selbst (ELM 6.0 Anhang C.2.3: «Kurz vor Ablauf initiiert
/// der Transmitter automatisch einen Erneuerungsprozess»; Walter 15.10.2026). Wann «kurz vor Ablauf» ist,
/// entscheidet <see cref="ElmZertifikatLaufzeit"/>. Höchstens ein Versuch pro Tag; scheitert er, steht der
/// Grund in <see cref="ElmSuaFall.AutoFehler"/> und das To-do <c>elm_zertifikat</c> meldet ihn.
/// </summary>
public class ElmZertifikatAutoErneuerung : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ElmZertifikatAutoErneuerung> _log;

    private static readonly TimeSpan Intervall = TimeSpan.FromHours(3);

    public ElmZertifikatAutoErneuerung(IServiceProvider services, ILogger<ElmZertifikatAutoErneuerung> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                await DurchgangAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[SUA-Erneuerung] Durchgang fehlgeschlagen, weiter beim nächsten Mal.");
            }

            try { await Task.Delay(Intervall, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    private async Task DurchgangAsync(IServiceProvider sp, CancellationToken ct)
    {
        var store = sp.GetRequiredService<ElmZertifikatStore>();
        var fall = store.LadeZertifikatFall();
        if (fall == null) return;

        DateTime? vorher;
        ElmZertifikatLaufzeit.Ergebnis l;
        using (var sua = store.LadeSua())
        {
            if (sua == null) return;
            vorher = sua.NotAfter;
            l = ElmZertifikatLaufzeit.Beurteile(sua.NotBefore, sua.NotAfter, sua.HasPrivateKey, fall.Erneuerungen, DateTime.Now);
        }
        if (!l.AutoFaellig) return;
        if (fall.AutoVersuchAm?.Date == DateTime.Today) return;

        fall.AutoVersuchAm = DateTime.Now;
        store.SpeichereZertifikatFall(fall);

        var url = ElmEndpunkte.IstSicher(fall.Url) ? fall.Url! : ElmEndpunkte.Standard().Url;
        string? fehler;
        try
        {
            var r = await sp.GetRequiredService<ElmSuaService>().SynchronizeAsync(url, null, renew: true, ct);
            using var neu = store.LadeSua();
            fehler = neu != null && neu.NotAfter != vorher ? null : (r.Meldung ?? "Keine Antwort von Swissdec.");
        }
        catch (InvalidOperationException ex) { fehler = ex.Message; }
        catch (HttpRequestException ex) { fehler = "Swissdec nicht erreichbar: " + ex.Message; }

        var aktuell = store.LadeZertifikatFall() ?? fall;
        aktuell.AutoVersuchAm = fall.AutoVersuchAm;
        aktuell.AutoFehler = fehler;
        store.SpeichereZertifikatFall(aktuell);

        if (fehler == null)
            _log.LogInformation("[SUA-Erneuerung] Zertifikat automatisch erneuert ({Url}).", url);
        else
            _log.LogWarning("[SUA-Erneuerung] Automatische Erneuerung gescheitert: {Fehler}", fehler);
    }
}
