namespace HrSystem.Services.Elm;

/// <summary>
/// Holt den Stand gesendeter Lohnmeldungen selbst ab (Walter 15.10.2026): Status, solange Swissdec
/// verteilt, danach Quittungen und Rückfragen der Empfänger. Abstände in <see cref="ElmMeldungAnzeige"/>
/// (Status 30 Min, Synchronisieren 60 Min, Meldungen bis 90 Tage). Ist gerade eine Übermittlung
/// von Hand im Gang, wartet der Dienst bis zum nächsten Durchgang.
/// </summary>
public class ElmMeldungenAutoAbfrage : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<ElmMeldungenAutoAbfrage> _log;

    private static readonly TimeSpan Intervall = TimeSpan.FromMinutes(10);

    public ElmMeldungenAutoAbfrage(IServiceProvider services, ILogger<ElmMeldungenAutoAbfrage> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(4), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dienst = scope.ServiceProvider.GetRequiredService<ElmMeldungenService>();
                var jetzt = DateTime.Now;
                foreach (var v in dienst.Liste().Where(v => ElmMeldungAnzeige.StatusFaellig(v, jetzt)
                                                            || ElmMeldungAnzeige.SyncFaellig(v, jetzt).Count > 0))
                {
                    try
                    {
                        var r = await dienst.AbfragenAsync(v.Id, automatisch: true, stoppingToken);
                        if (!r.Ok)
                            _log.LogWarning("[ELM-Abfrage] {Titel}: {Fehler}", v.Titel, r.Fehler);
                    }
                    catch (InvalidOperationException ex)
                    {
                        _log.LogInformation("[ELM-Abfrage] {Titel} übersprungen: {Grund}", v.Titel, ex.Message);
                    }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                _log.LogError(ex, "[ELM-Abfrage] Durchgang fehlgeschlagen, weiter beim nächsten Mal.");
            }

            try { await Task.Delay(Intervall, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }
}
