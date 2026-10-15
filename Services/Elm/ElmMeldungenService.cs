namespace HrSystem.Services.Elm;

/// <summary>
/// Lohnmeldungen senden und verfolgen — für den Benutzer (Walter 15.10.2026). Baut auf
/// <see cref="ElmUebermittlungService"/> auf, aber ohne Adresswahl und ohne Prüfschalter:
/// Ziel = <see cref="ElmEndpunkte.Standard"/>, TestCase = Testinfrastruktur, immer doppelt signiert,
/// alle Empfänger. Die Ersatzmeldung übernimmt die DeclarationID selbst (F05_06).
/// Produktiv (Distributor) nur mit <c>Swissdec:ProduktivSenden=true</c> — Parallelbetrieb mit Mirus.
/// </summary>
public class ElmMeldungenService
{
    private readonly ElmUebermittlungService _dienst;
    private readonly IConfiguration _cfg;

    private readonly ElmTransmitterClient _client;
    private readonly ElmZertifikatStore _store;
    private static readonly SemaphoreSlim VerbindungSperre = new(1, 1);

    public ElmMeldungenService(ElmUebermittlungService dienst, IConfiguration cfg,
        ElmTransmitterClient client, ElmZertifikatStore store)
    {
        _dienst = dienst;
        _cfg = cfg;
        _client = client;
        _store = store;
    }

    public ElmEndpunkte.Ziel Ziel => ElmEndpunkte.Standard();

    /// <summary>Grund, warum hier (noch) nicht gesendet werden darf — NULL = erlaubt.</summary>
    public string? SendenGesperrt()
        => Ziel.IstTest || _cfg.GetValue<bool>("Swissdec:ProduktivSenden") ? null
           : "Echte Lohnmeldungen an Swissdec sind noch nicht freigegeben (Parallelbetrieb mit Mirus).";

    public static string UrlVon(ElmVorgang v) => ElmEndpunkte.IstSicher(v.Url) ? v.Url! : ElmEndpunkte.Standard().Url;

    public List<ElmVorgang> Liste() => _dienst.Liste().Where(v => v.Art is "monthly" or "annual").ToList();

    private ElmVorgang Finde(string id)
        => _dienst.Liste().FirstOrDefault(v => v.Id == id)
           ?? throw new InvalidOperationException("Meldung nicht gefunden.");

    public Task<ElmUebermittlungService.Vorschau> VorschauAsync(string art, int jahr, int? monat, CancellationToken ct)
        => _dienst.VorschauAsync(art, jahr, monat, ct);

    public async Task<ElmUebermittlungService.Ergebnis> SendenAsync(string art, int jahr, int? monat, string? ersetztId, CancellationToken ct)
    {
        if (SendenGesperrt() is { } grund) throw new InvalidOperationException(grund);
        string? substitution = null;
        if (!string.IsNullOrWhiteSpace(ersetztId))
        {
            var alt = Finde(ersetztId);
            if (!ElmMeldungAnzeige.Ersetzbar(alt))
                throw new InvalidOperationException("Diese Meldung lässt sich (noch) nicht ersetzen — sie wurde von keinem Empfänger angenommen.");
            var (j, m) = ElmMeldungAnzeige.Periode(alt);
            if (j != jahr || (alt.Art == "monthly" && m != monat) || alt.Art != art)
                throw new InvalidOperationException("Die Ersatzmeldung muss dieselbe Meldung (Art und Zeitraum) betreffen.");
            substitution = ElmMeldungAnzeige.DeclarationId(alt);
        }
        return await _dienst.DeclareAsync(Ziel.Url, new ElmUebermittlungService.DeclareOptionen(
            art, jahr, art == "monthly" ? monat : null,
            TestCase: Ziel.IstTest, Substitution: substitution, Adressaten: null, DoppeltSignieren: true), ct);
    }

    public record Abfrage(bool Ok, string? Fehler);

    /// <summary>
    /// Stand holen: solange der Job läuft Status, danach jeden offenen Empfänger synchronisieren
    /// (Quittungen und Rückfragen abholen, Erhaltenes quittieren). <paramref name="automatisch"/>
    /// = Hintergrunddienst, dann gelten die Abstände aus <see cref="ElmMeldungAnzeige"/>.
    /// </summary>
    public async Task<Abfrage> AbfragenAsync(string id, bool automatisch, CancellationToken ct)
    {
        var jetzt = DateTime.Now;
        var v = Finde(id);
        var url = UrlVon(v);
        var fehler = new List<string>();

        var statusNoetig = automatisch ? ElmMeldungAnzeige.StatusFaellig(v, jetzt)
            : v.Art is "monthly" or "annual" && !v.JobFinished && !string.IsNullOrEmpty(v.JobKey);
        if (statusNoetig)
        {
            if (!automatisch && v.LetzteStatusAbfrage is DateTime t
                && (jetzt - t).TotalSeconds < ElmUebermittlungService.MindestabstandStatusSekunden)
                return new(true, null);
            var r = await _dienst.StatusAsync(url, id, ct);
            if (r.Uebertragungsfehler) fehler.Add(r.Meldung);
            v = Finde(id);
        }

        var offen = automatisch ? ElmMeldungAnzeige.SyncFaellig(v, jetzt)
            : !v.JobFinished ? new()
            : v.Adressaten.Where(a => a.Zustand == "Success" && !string.IsNullOrEmpty(a.FallId)
                                      && !string.IsNullOrEmpty(a.Key) && !string.IsNullOrEmpty(a.Password)
                                      && (!ElmMeldungAnzeige.Erledigt(a) || ElmUebermittlungXml.ZuQuittieren(a).Count > 0)).ToList();
        foreach (var a in offen)
        {
            var r = await _dienst.SynchronizeAsync(url, id, a.AddresseeId, null, false, ct);
            if (r.Uebertragungsfehler) fehler.Add(ElmMeldungAnzeige.EmpfaengerName(a.Domain, a.Identification, a.InstitutionName) + ": " + r.Meldung);
        }
        return new(fehler.Count == 0, fehler.Count == 0 ? null : string.Join(" · ", fehler));
    }

    // ── Verbindung (drei Ampeln) ─────────────────────────────────────────────

    public ElmVerbindungStand? LetzteVerbindung() => _store.LadeVerbindung();

    /// <summary>
    /// Ping, dann CheckInteroperability gegen das Standard-Ziel — nur auf Knopfdruck. Die
    /// Testnachricht geht nur raus, wenn Swissdec auf den Ping geantwortet hat.
    /// </summary>
    public async Task<ElmVerbindungStand> VerbindungPruefenAsync(string? von, CancellationToken ct)
    {
        if (!await VerbindungSperre.WaitAsync(0))
            throw new InvalidOperationException("Die Verbindung wird gerade geprüft — bitte einen Moment warten.");
        try
        {
            var warten = ElmVerbindung.WartenSekunden(_store.LadeVerbindung(), DateTime.Now);
            if (warten > 0)
                throw new InvalidOperationException($"Bitte {warten} Sekunden warten bis zur nächsten Prüfung.");

            var ziel = Ziel;
            var ping = await _client.PingAsync(ziel.Url, 0, ct);
            System.Security.Cryptography.X509Certificates.X509Certificate2? erp = null;
            try { erp = _store.LadeErp(); } catch { /* unten als fehlend gemeldet */ }
            var zertifikatDa = erp?.HasPrivateKey == true;
            var suaDa = _store.LadeSuaZumSignieren() != null;
            ElmTransmitterClient.ElmCallResult? interop = null;
            if (ping.Ok && zertifikatDa)
                interop = await _client.CheckInteroperabilityAsync(ziel.Url, 0.01m, 0, ct);

            var stand = new ElmVerbindungStand
            {
                GeprueftAm = DateTime.Now,
                Ziel = ziel.Name,
                GeprueftVon = von,
                Erreichbar = ElmVerbindung.Erreichbar(ping),
                Uhrzeit = ElmVerbindung.Uhrzeit(ping, interop),
                Sicher = ElmVerbindung.Sicher(interop, zertifikatDa, suaDa, ping.Ok),
            };
            _store.SpeichereVerbindung(stand);
            return stand;
        }
        finally { VerbindungSperre.Release(); }
    }

    // ── Archiv je Meldung ────────────────────────────────────────────────────

    /// <summary>
    /// Archivdatei einer Lohnmeldung zum Herunterladen — nur Dateien, die im Verlauf einer
    /// Monats-/Jahresmeldung stehen; Zugangsdaten der Freigabe ausgeblendet. NULL = nicht erlaubt/fehlt.
    /// </summary>
    public string? ArchivDatei(string name)
    {
        var erlaubt = Liste().Any(v => v.Protokoll.Any(p => p.ArchivAnfrage == name || p.ArchivAntwort == name));
        if (!erlaubt) return null;
        var inhalt = _store.LeseArchiv(name);
        return inhalt == null ? null : ElmVerbindung.OhneZugangsdaten(inhalt);
    }

    public Task<ElmUebermittlungService.Ergebnis> AntwortenAsync(string id, string addresseeId, string storyId,
        Dictionary<short, string?> werte, CancellationToken ct)
    {
        var v = Finde(id);
        return _dienst.SynchronizeAsync(UrlVon(v), id, addresseeId,
            new() { new ElmUebermittlungService.DialogAntwort(storyId, werte) }, false, ct);
    }
}
