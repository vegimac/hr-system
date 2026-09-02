namespace HrSystem.Services;

/// <summary>
/// Schaut alle fünf Minuten nach, ob eine gemerkte Mail wieder dran ist
/// (Walter-Vorgabe 01.09.2026).
///
/// Fünf Minuten, obwohl die kürzeste Staffel 15 Minuten beträgt: der Takt
/// bestimmt nur, wie genau der geplante Zeitpunkt getroffen wird. Bei einem
/// Takt von 15 Minuten würde aus «in 15 Minuten» im ungünstigen Fall eine
/// halbe Stunde. Ein Durchgang ohne fällige Fälle ist eine einzige Abfrage
/// auf einen Index — das kostet nichts.
///
/// Der Dienst prüft die SMTP-Konfiguration bei jedem Durchgang neu, nicht
/// beim Start: wer sie nachträgt, muss den Server nicht neu starten.
/// </summary>
public class MailWiedervorlageBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MailWiedervorlageBackgroundService> _log;

    private static readonly TimeSpan Intervall = TimeSpan.FromMinutes(5);

    /// <summary>Aufräumen läuft nicht bei jedem Durchgang, sondern einmal täglich.</summary>
    private DateTime _naechstesAufraeumen = DateTime.Now.AddHours(1);

    public MailWiedervorlageBackgroundService(IServiceProvider services,
                                              ILogger<MailWiedervorlageBackgroundService> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Beim Start kurz warten: Migrationen und Seeds sollen durch sein,
        // bevor wir zum ersten Mal auf mail_wiedervorlage zugreifen.
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dienst = scope.ServiceProvider.GetRequiredService<MailWiedervorlageService>();

                await dienst.FaelligeVerarbeitenAsync(stoppingToken);

                if (DateTime.Now >= _naechstesAufraeumen)
                {
                    _naechstesAufraeumen = DateTime.Now.AddDays(1);
                    var weg = await dienst.AufraeumenAsync(stoppingToken);
                    if (weg > 0)
                        _log.LogInformation("[Wiedervorlage] {N} zugestellte Fälle nach 90 Tagen entfernt.", weg);
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Ein Fehler darf den Dienst nie beenden — sonst bleiben alle
                // weiteren Mails bis zum nächsten Deploy liegen, und genau
                // das Liegenbleiben soll er ja verhindern.
                _log.LogError(ex, "[Wiedervorlage] Durchgang fehlgeschlagen, weiter beim nächsten Mal.");
            }

            try { await Task.Delay(Intervall, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }
}
