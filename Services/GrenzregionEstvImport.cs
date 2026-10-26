using HrSystem.Data;

namespace HrSystem.Services;

/// <summary>
/// Holt die Grenzgemeinden-Datei von der ESTV und schreibt sie in grenzregion.
/// Andere Länder (von Hand oder aus einer Ersatzdatei) bleiben stehen.
/// Eine zu kurze Datei wird abgelehnt, die bestehende Liste bleibt.
/// </summary>
public sealed class GrenzregionEstvImport
{
    public const string HttpClientName = "EstvGrenzregion";

    private readonly IHttpClientFactory _http;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<GrenzregionEstvImport> _log;
    private int _laufAktiv;

    public DateTime? LetzterStart { get; private set; }
    public DateTime? LetztesEnde { get; private set; }
    public string? LetzterAusloeser { get; private set; }
    public GrenzregionEstvErgebnis? LetztesErgebnis { get; private set; }
    public bool LaeuftGerade => Volatile.Read(ref _laufAktiv) == 1;

    public GrenzregionEstvImport(
        IHttpClientFactory http,
        IServiceScopeFactory scopes,
        ILogger<GrenzregionEstvImport> log)
    {
        _http = http;
        _scopes = scopes;
        _log = log;
    }

    public async Task<GrenzregionEstvErgebnis> HolenAsync(string ausloeser, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _laufAktiv, 1, 0) != 0)
            throw new InvalidOperationException("Der ESTV-Abruf läuft bereits.");

        LetzterStart = DateTime.Now;
        LetztesEnde = null;
        LetzterAusloeser = ausloeser;
        try
        {
            var erg = await HolenKernAsync(ct);
            LetztesErgebnis = erg;
            _log.LogInformation(
                "ESTV Grenzregion: {Anzahl} Gemeinden, {Neu} neu, {Aktualisiert} aktualisiert, {Entfernt} entfernt ({Ausloeser}).",
                erg.Anzahl, erg.Neu, erg.Aktualisiert, erg.Entfernt, ausloeser);
            return erg;
        }
        catch (Exception ex)
        {
            LetztesErgebnis = new GrenzregionEstvErgebnis(
                0, 0, 0, 0, "", DateTime.Now, false, ex.Message);
            _log.LogError(ex, "ESTV Grenzregion fehlgeschlagen ({Ausloeser}).", ausloeser);
            throw;
        }
        finally
        {
            LetztesEnde = DateTime.Now;
            Volatile.Write(ref _laufAktiv, 0);
        }
    }

    async Task<GrenzregionEstvErgebnis> HolenKernAsync(CancellationToken ct)
    {
        var client = _http.CreateClient(HttpClientName);
        using var seite = await client.GetAsync(GrenzregionEstv.SeitenUrl, ct);
        seite.EnsureSuccessStatusCode();
        var html = await seite.Content.ReadAsStringAsync(ct);
        var url = GrenzregionEstv.FindeDateiUrl(html);

        using var datei = await client.GetAsync(url, ct);
        datei.EnsureSuccessStatusCode();
        var bytes = await datei.Content.ReadAsByteArrayAsync(ct);
        var zeilen = GrenzregionEstv.LiesXlsx(bytes);
        if (zeilen.Count < GrenzregionEstv.Mindestzahl)
            throw new InvalidOperationException(
                $"Die ESTV-Datei enthält nur {zeilen.Count} Gemeinden. Erwartet sind mindestens {GrenzregionEstv.Mindestzahl}. Die bestehende Liste bleibt.");

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (neu, aktualisiert, entfernt, anzahl) = await GrenzregionEstv.AnwendenAsync(db, zeilen, ct);
        return new GrenzregionEstvErgebnis(anzahl, neu, aktualisiert, entfernt, url, DateTime.Now, true, null);
    }
}

public sealed record GrenzregionEstvErgebnis(
    int Anzahl,
    int Neu,
    int Aktualisiert,
    int Entfernt,
    string DateiUrl,
    DateTime FertigAm,
    bool Ok,
    string? Fehler);
