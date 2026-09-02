using HrSystem.Data;
using Microsoft.EntityFrameworkCore;

namespace HrSystem.Services;

/// <summary>
/// Ruft stündlich das Rückläufer-Postfach ab (Walter-Vorgabe 01.09.2026).
///
/// Warum stündlich und nicht nachts: Rückläufer kommen innert Minuten
/// nach einem Versand zurück. Wer abends eine Gruppen-Mail rausschickt,
/// will nicht bis zum nächsten Morgen warten, um zu sehen, was nicht
/// angekommen ist. Stündlich ist für ein Postfach mit einer Handvoll
/// Nachrichten pro Woche völlig unproblematisch.
///
/// Der Dienst läuft nur, wenn in den Systemeinstellungen ein Postfach
/// hinterlegt UND der Haken «Abruf aktiv» gesetzt ist. Der Haken wird bei
/// jedem Durchgang frisch gelesen, nicht beim Start — so wirkt eine
/// Änderung in der Maske spätestens nach einer Stunde, ohne Neustart.
/// </summary>
public class BounceAbrufBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<BounceAbrufBackgroundService> _log;

    private static readonly TimeSpan Intervall = TimeSpan.FromHours(1);

    public BounceAbrufBackgroundService(IServiceProvider services,
                                        ILogger<BounceAbrufBackgroundService> log)
    {
        _services = services;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Beim Start kurz warten: Migrationen und Seeds sollen durch sein,
        // bevor wir die erste Abfrage auf smtp_setting machen.
        try { await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken); }
        catch (TaskCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try { await EinmalAsync(stoppingToken); }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                // Ein Fehler beim Abruf darf den Dienst nie beenden — sonst
                // holt eine einzelne Netzstörung das Postfach bis zum
                // nächsten Deploy nicht mehr ab.
                _log.LogError(ex, "[Bounce] Durchgang fehlgeschlagen, weiter beim nächsten Mal.");
            }

            try { await Task.Delay(Intervall, stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    private async Task EinmalAsync(CancellationToken ct)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var aktiv = await db.SmtpSettings.AsNoTracking()
            .Where(x => x.Id == 1)
            .Select(x => x.BounceAbrufAktiv
                      && x.BounceImapHost != null && x.BounceImapHost != ""
                      && x.BounceImapUser != null && x.BounceImapUser != "")
            .FirstOrDefaultAsync(ct);
        if (!aktiv) return;

        var dienst = scope.ServiceProvider.GetRequiredService<BounceAbrufService>();
        var res = await dienst.AbrufenAsync(ct);
        if (res.Fehler != null)
            _log.LogWarning("[Bounce] Abruf mit Fehler beendet: {Fehler}", res.Fehler);
    }
}
