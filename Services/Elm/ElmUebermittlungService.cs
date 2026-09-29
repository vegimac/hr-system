using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using static HrSystem.Services.Elm.ElmGemeinsam;
using static HrSystem.Services.Elm.ElmUebermittlungXml;

namespace HrSystem.Services.Elm;

/// <summary>
/// Foundation F05 / F06 / F08 — Lohnmeldungen übermitteln (nachts gebaut 30.09.2026).
/// Nur manuell ausgelöst, nur Super-Admin (Controller), nur an Adressen aus
/// <see cref="ElmEndpunkte"/> oder eine vom Super-Admin eingegebene https-Adresse.
/// Inhalt: die Meldung aus ElmMonthly-/ElmAnnualDeclarationBuilder — also Kunstdaten
/// der Testinstanz (NIE echte Daten an die Swissdec-Testsysteme).
/// </summary>
public class ElmUebermittlungService
{
    /// <summary>UC002: automatisierte Statusabfragen mindestens 10 s auseinander.</summary>
    public const int MindestabstandStatusSekunden = 10;

    private readonly ElmTransmitterClient _client;
    private readonly ElmZertifikatStore _store;
    private readonly ElmMonthlyDeclarationBuilder _monat;
    private readonly ElmAnnualDeclarationBuilder _jahr;
    private readonly ElmEinstellungen _einst;
    private readonly ElmXmlValidator _validator;

    public ElmUebermittlungService(ElmTransmitterClient client, ElmZertifikatStore store,
        ElmMonthlyDeclarationBuilder monat, ElmAnnualDeclarationBuilder jahr,
        ElmEinstellungen einstellungen, ElmXmlValidator validator)
    {
        _client = client;
        _store = store;
        _monat = monat;
        _jahr = jahr;
        _einst = einstellungen;
        _validator = validator;
    }

    public record Ergebnis(bool Ok, string Meldung, ElmVorgang? Vorgang, ElmTransmitterClient.ElmCallResult? Call)
    {
        public List<Hinweis> Hinweise { get; init; } = new();
        public string? FaultCode { get; init; }
        public List<string> Warnungen { get; init; } = new();
    }

    public record DeclareOptionen(
        string Art, int Jahr, int? Monat,
        bool TestCase = true,
        string? Substitution = null,
        Dictionary<string, bool>? Adressaten = null,
        bool DoppeltSignieren = true,
        bool RequestIdWiederverwenden = false);

    public record DialogAntwort(string StoryId, Dictionary<short, string?> Werte);

    public record SubscribeOptionen(
        string? Uid, string? Firmenname, string? Kontakt, string? AddresseeIdentification, string? Domain,
        string? Plz, string? Ort, string? Versicherer, string? Kundennummer, string? Vertragsnummer,
        bool TestCase = true, bool DoppeltSignieren = true);

    // ── Vorschau ─────────────────────────────────────────────────────────

    public record Vorschau(string Art, string Titel, List<Adressat> Adressaten, string Uid, string Firmenname,
        List<string> Warnungen, List<string> XsdFehler);

    public async Task<Vorschau> VorschauAsync(string art, int jahr, int? monat, CancellationToken ct)
    {
        var (xml, titel, warn, xsd) = await BaueMeldungAsync(art, jahr, monat, ct);
        if (xml.Length == 0) return new Vorschau(art, titel, new(), "", "", warn, xsd);
        var v = LiesVorlage(XDocument.Parse(xml).Root!);
        return new Vorschau(v.Art, titel, v.Adressaten, v.Uid, v.Firmenname, warn, xsd);
    }

    private async Task<(string Xml, string Titel, List<string> Warn, List<string> Xsd)> BaueMeldungAsync(
        string art, int jahr, int? monat, CancellationToken ct)
    {
        if (art == "annual")
        {
            var r = await _jahr.BuildAhvAsync(jahr, ct);
            return (r.Xml, $"Jahresmeldung AHV {jahr}", r.Warnungen, r.XsdFehler);
        }
        if (monat is null or < 1 or > 12)
            throw new InvalidOperationException("Für die Monatsmeldung Monat 1–12 angeben.");
        var m = await _monat.BuildAsync(jahr, monat.Value, ct);
        return (m.Xml, $"Monatsmeldung {monat:00}.{jahr}", m.Warnungen, m.XsdFehler);
    }

    // ── Declare (UC001) ──────────────────────────────────────────────────

    public async Task<Ergebnis> DeclareAsync(string url, DeclareOptionen o, CancellationToken ct)
    {
        var art = o.Art == "annual" ? "annual" : "monthly";
        var (xml, titel, warn, xsd) = await BaueMeldungAsync(art, o.Jahr, o.Monat, ct);
        if (xml.Length == 0)
            return new Ergebnis(false, "Keine Meldung erzeugt: " + string.Join(" · ", warn), null, null) { Warnungen = warn };
        if (xsd.Count > 0)
            return new Ergebnis(false, "Die Meldung entspricht nicht dem ELM-Schema und wurde NICHT gesendet: "
                + string.Join(" · ", xsd.Take(3)), null, null) { Warnungen = warn };

        var stand = _store.LadeUebermittlungen();
        var requestId = o.RequestIdWiederverwenden && stand.LetzteRequestId != null
            ? stand.LetzteRequestId : NeueId();
        var root = XDocument.Parse(xml).Root!;
        var vorlage = LiesVorlage(root);
        var anfrage = BereiteDeclareVor(root, o.Adressaten, o.TestCase, o.Substitution, requestId, DateTime.Now);
        var gewaehlt = LiesVorlage(anfrage).Adressaten;
        if (gewaehlt.Count == 0)
            return new Ergebnis(false, "Die Meldung hat keinen Adressaten.", null, null);

        var s = await SendeAsync(url, anfrage, art == "annual" ? "declare-annual" : "declare-monthly",
            o.DoppeltSignieren, stand, requestId, ct);
        warn.AddRange(s.Warnungen);
        if (o.RequestIdWiederverwenden)
            warn.Insert(0, $"Bewusst mit der RequestID der letzten Anfrage gesendet ({requestId}) — Probe F05_07.");

        var jobKey = LiesStatus(s.Body).JobKey;
        if (s.Abbruch != null || jobKey == null)
        {
            _store.SpeichereUebermittlungen(stand);
            return new Ergebnis(false, s.Abbruch ?? "Antwort ohne JobKey — Antwort-XML prüfen.", null, s.Call)
            { Hinweise = s.Hinweise, FaultCode = s.FaultCode, Warnungen = warn };
        }

        var v = new ElmVorgang
        {
            Art = art,
            Titel = titel + (o.TestCase ? " · TestCase" : "") + (string.IsNullOrWhiteSpace(o.Substitution) ? "" : " · Ersatzmeldung"),
            TestCase = o.TestCase,
            Substitution = string.IsNullOrWhiteSpace(o.Substitution) ? null : o.Substitution.Trim(),
            DoppeltSigniert = s.Call.DoppeltSigniert,
            RequestId = requestId,
            ResponseId = s.Kopf.ResponseId,
            JobKey = jobKey,
            Uid = vorlage.Uid,
            Firmenname = vorlage.Firmenname,
            Adressaten = gewaehlt.Select(a => new ElmVorgangAdressat
            {
                AddresseeId = a.AddresseeId,
                Identification = a.Identification,
                Domain = a.Domain,
                Verarbeiten = a.Verarbeiten,
            }).ToList(),
        };
        v.Protokoll.Add(new ElmProtokollZeile
        {
            Schritt = "declare", RequestId = requestId, ResponseId = s.Kopf.ResponseId,
            Text = $"Gesendet an {string.Join(", ", gewaehlt.Select(a => a.Identification + (a.Verarbeiten ? "" : " (abgewählt)")))} — JobKey {jobKey}",
        });
        stand.Vorgaenge.Insert(0, v);
        _store.SpeichereUebermittlungen(stand);
        return new Ergebnis(true, $"Übermittelt — JobKey {jobKey}. Als Nächstes «Status abfragen».", v, s.Call)
        { Warnungen = warn, Hinweise = s.Hinweise };
    }

    // ── GetStatus (UC002) ────────────────────────────────────────────────

    public async Task<Ergebnis> StatusAsync(string url, string vorgangId, CancellationToken ct)
    {
        var stand = _store.LadeUebermittlungen();
        var v = Finde(stand, vorgangId);
        if (v.Art == "subscribe")
            throw new InvalidOperationException("Die Anmeldung ist synchron — keinen Status abfragen, sondern synchronisieren.");
        if (v.JobFinished)
            throw new InvalidOperationException("Übermittlung abgeschlossen (JobFinished) — eine weitere Statusabfrage ist nicht zulässig.");
        if (v.LetzteStatusAbfrage is DateTime letzte)
        {
            var warten = MindestabstandStatusSekunden - (int)(DateTime.Now - letzte).TotalSeconds;
            if (warten > 0)
                throw new InvalidOperationException($"Nächste Statusabfrage frühestens in {warten} s (Mindestabstand {MindestabstandStatusSekunden} s).");
        }

        var requestId = NeueId();
        var anfrage = BaueGetStatus(v.Art, v.JobKey!, Kontext(v.Firmenname, requestId));
        v.LetzteStatusAbfrage = DateTime.Now;
        v.StatusAbfragen++;
        var s = await SendeAsync(url, anfrage, v.Art == "annual" ? "status-annual" : "status-monthly",
            v.DoppeltSigniert, stand, requestId, ct);
        if (s.Abbruch != null)
        {
            v.Protokoll.Add(new ElmProtokollZeile { Schritt = "status", RequestId = requestId, Text = s.Abbruch });
            _store.SpeichereUebermittlungen(stand);
            return new Ergebnis(false, s.Abbruch, v, s.Call) { Hinweise = s.Hinweise, FaultCode = s.FaultCode, Warnungen = s.Warnungen };
        }

        var st = LiesStatus(s.Body);
        foreach (var neu in st.Adressaten)
        {
            var a = v.Adressaten.FirstOrDefault(x => x.AddresseeId == neu.AddresseeId)
                 ?? v.Adressaten.FirstOrDefault(x => x.Identification == neu.Identification);
            if (a == null)
            {
                a = new ElmVorgangAdressat { AddresseeId = neu.AddresseeId, Identification = neu.Identification };
                v.Adressaten.Add(a);
            }
            UebernehmeStatus(a, neu);
        }
        if (st.JobFinished == true) v.JobFinished = true;
        var text = Zusammenfassung(v, st.JobFinished == true);
        v.Protokoll.Add(new ElmProtokollZeile { Schritt = "status", RequestId = requestId, ResponseId = s.Kopf.ResponseId, Text = text });
        _store.SpeichereUebermittlungen(stand);
        return new Ergebnis(true, text, v, s.Call) { Warnungen = s.Warnungen };
    }

    private static void UebernehmeStatus(ElmVorgangAdressat a, AdressatStatus n)
    {
        a.Zustand = n.Zustand;
        a.Verarbeiten = n.Verarbeiten;
        a.Fehler = n.Fehler;
        a.FehlerDetail = n.FehlerDetail;
        a.FehlerCode = n.FehlerCode;
        a.Wartung = n.Wartung;
        if (n.FallId != null) a.FallId = n.FallId;
        if (n.Key != null) a.Key = n.Key;
        if (n.Password != null) a.Password = n.Password;
        a.TestCaseBestaetigt = n.TestCase;
        if (n.Institution != null) a.InstitutionName = n.Institution;
        a.Hinweise = n.Hinweise.Select(ZuModell).ToList();
    }

    private static string Zusammenfassung(ElmVorgang v, bool fertig)
    {
        var teile = v.Adressaten.Select(a => a.Zustand switch
        {
            "Success" => $"{a.Identification}: erfolgreich, DeclarationID {a.FallId}",
            "Error" => $"{a.Identification}: Fehler — {a.Fehler}",
            "Ignored" => $"{a.Identification}: nicht verarbeitet (abgewählt)",
            "Processing" => $"{a.Identification}: in Bearbeitung",
            _ => $"{a.Identification}: {a.Zustand}",
        });
        return (fertig ? "JobFinished — " : "Noch nicht fertig — ") + string.Join(" · ", teile);
    }

    // ── Synchronize (UC005/UC008/UC009) ─────────────────────────────────

    public async Task<Ergebnis> SynchronizeAsync(string url, string vorgangId, string addresseeId,
        List<DialogAntwort>? antworten, bool abmelden, CancellationToken ct)
    {
        var stand = _store.LadeUebermittlungen();
        var v = Finde(stand, vorgangId);
        var a = v.Adressaten.FirstOrDefault(x => x.AddresseeId == addresseeId)
            ?? throw new InvalidOperationException("Adressat nicht gefunden.");
        if (a.Zustand != "Success" || string.IsNullOrEmpty(a.FallId))
            throw new InvalidOperationException(a.Zustand == "Ignored"
                ? "Dieser Adressat wurde abgewählt (ProcessByDistributor=false) — es gibt keinen Fall zum Synchronisieren."
                : "Noch kein Fall für diesen Adressaten — zuerst «Status abfragen», bis er erfolgreich ist.");
        if (v.Art != "subscribe" && (string.IsNullOrEmpty(a.Key) || string.IsNullOrEmpty(a.Password)))
            throw new InvalidOperationException("Credentials fehlen — sie kommen mit der erfolgreichen Statusabfrage.");
        var quittieren = ZuQuittieren(a);
        if (a.State is "Finished" or "closed" && quittieren.Count == 0 && (antworten?.Count ?? 0) == 0 && !abmelden)
            throw new InvalidOperationException($"Fall abgeschlossen ({a.State}) — es gibt nichts mehr zu synchronisieren.");

        var warn = new List<string>();
        foreach (var an in antworten ?? new())
        {
            var story = a.Stories.FirstOrDefault(x => x.StoryId == an.StoryId && x.Art == "DialogMessage")
                ?? throw new InvalidOperationException($"Dialog {an.StoryId} nicht gefunden.");
            var fehler = new List<string>();
            var neueId = NeueId();
            var xml = ElmDialog.BaueAntwort(XElement.Parse(story.Xml), an.Werte, fehler, DateTime.Now, neueId);
            if (fehler.Count > 0)
                throw new InvalidOperationException("Antwort nicht gesendet: " + string.Join(" ", fehler));
            a.Ausstehend.Add(new ElmStoryGesendet { StoryId = neueId, Xml = xml.ToString(SaveOptions.DisableFormatting), AntwortAuf = story.StoryId });
            story.Beantwortet = true;
            story.AntwortStoryId = neueId;
        }

        var requestId = NeueId();
        var kontext = Kontext(v.Firmenname, requestId);
        var anfrage = v.Art == "subscribe"
            ? BaueSynchronizeSubscribe(v, a, quittieren, abmelden, kontext)
            : BaueSynchronizeDeclare(v, a, quittieren, kontext);
        foreach (var g in a.Ausstehend) g.Gesendet++;
        var s = await SendeAsync(url, anfrage, v.Art switch
        {
            "subscribe" => "sync-subscribe",
            "annual" => "sync-annual",
            _ => "sync-monthly",
        }, v.DoppeltSigniert, stand, requestId, ct);
        warn.AddRange(s.Warnungen);
        if (s.Abbruch != null)
        {
            v.Protokoll.Add(new ElmProtokollZeile { Schritt = "sync", RequestId = requestId, Text = $"{a.Identification}: {s.Abbruch}" });
            _store.SpeichereUebermittlungen(stand);
            return new Ergebnis(false, s.Abbruch, v, s.Call) { Hinweise = s.Hinweise, FaultCode = s.FaultCode, Warnungen = warn };
        }

        var sync = LiesSynchronize(s.Body);
        if (sync.FallId != null && sync.FallId != a.FallId)
            warn.Add($"Die Antwort nennt eine andere Fall-ID ({sync.FallId}) als gesendet ({a.FallId}).");
        var neuVorher = a.Stories.Count;
        Uebernehme(a, sync, quittieren);
        var doppelt = sync.Stories.Where(x => a.Stories.Any(y => y.StoryId == x.StoryId && y.Empfangszaehler > 1)).ToList();
        foreach (var d in doppelt)
            warn.Add($"Story {d.StoryId} ({d.Art}) kam erneut — wird nochmals quittiert.");

        string text;
        if (sync.Fehler != null)
            text = $"{a.Identification}: Fehler — {sync.Fehler}";
        else
        {
            var neu = a.Stories.Count - neuVorher;
            text = $"{a.Identification}: {StateText(a.State)}"
                 + (quittieren.Count > 0 ? $" · {quittieren.Count} Story(s) quittiert" : "")
                 + (neu > 0 ? $" · {neu} neue Story(s): {string.Join(", ", a.Stories.Skip(neuVorher).Select(x => x.Art))}" : "")
                 + (a.Ausstehend.Count > 0 ? $" · {a.Ausstehend.Count} eigene Antwort(en) noch nicht quittiert" : "");
        }
        v.Protokoll.Add(new ElmProtokollZeile { Schritt = "sync", RequestId = requestId, ResponseId = s.Kopf.ResponseId, Text = text });
        _store.SpeichereUebermittlungen(stand);
        return new Ergebnis(sync.Fehler == null, text, v, s.Call)
        { Hinweise = sync.Hinweise, Warnungen = warn };
    }

    // ── SubscribeOrganization (Kap. 7, synchron) ────────────────────────

    public async Task<Ergebnis> SubscribeAsync(string url, SubscribeOptionen o, CancellationToken ct)
    {
        var uid = (o.Uid ?? "").Trim();
        var firma = (o.Firmenname ?? "").Trim();
        var kontakt = (o.Kontakt ?? "").Trim();
        if (uid.Length == 0 || firma.Length == 0 || kontakt.Length == 0)
            throw new InvalidOperationException("UID, Firmenname und Kontakt sind Pflicht.");
        var domain = ElmSuaService.DomainOderStandard(o.Domain);
        var ident = string.IsNullOrWhiteSpace(o.AddresseeIdentification) ? "1234" : o.AddresseeIdentification.Trim();

        var stand = _store.LadeUebermittlungen();
        var requestId = NeueId();
        var anfrage = BaueSubscribe(uid, firma, kontakt, ident, domain,
            string.IsNullOrWhiteSpace(o.Plz) ? "6000" : o.Plz.Trim(),
            string.IsNullOrWhiteSpace(o.Ort) ? "Luzern" : o.Ort.Trim(),
            string.IsNullOrWhiteSpace(o.Versicherer) ? "Test Versicherer" : o.Versicherer.Trim(),
            string.IsNullOrWhiteSpace(o.Kundennummer) ? "CustomerIdentity" : o.Kundennummer.Trim(),
            string.IsNullOrWhiteSpace(o.Vertragsnummer) ? "ContractIdentity" : o.Vertragsnummer.Trim(),
            o.TestCase, Kontext(firma, requestId));
        var s = await SendeAsync(url, anfrage, "subscribe", o.DoppeltSignieren, stand, requestId, ct);
        if (s.Abbruch != null)
        {
            _store.SpeichereUebermittlungen(stand);
            return new Ergebnis(false, s.Abbruch, null, s.Call) { Hinweise = s.Hinweise, FaultCode = s.FaultCode, Warnungen = s.Warnungen };
        }

        var st = LiesStatus(s.Body);
        var v = new ElmVorgang
        {
            Art = "subscribe",
            Titel = $"Anmeldung {domain} {ident}" + (o.TestCase ? " · TestCase" : ""),
            TestCase = o.TestCase,
            DoppeltSigniert = s.Call.DoppeltSigniert,
            RequestId = requestId,
            ResponseId = s.Kopf.ResponseId,
            JobFinished = true,
            Uid = uid,
            Firmenname = firma,
        };
        foreach (var n in st.Adressaten)
        {
            var a = new ElmVorgangAdressat { AddresseeId = n.AddresseeId, Identification = n.Identification, Domain = domain };
            UebernehmeStatus(a, n);
            v.Adressaten.Add(a);
        }
        var text = v.Adressaten.Count == 0 ? "Antwort ohne Adressat — Antwort-XML prüfen."
            : string.Join(" · ", v.Adressaten.Select(a => a.Zustand == "Success"
                ? $"{a.Identification}: angemeldet, SubscriptionID {a.FallId}"
                : a.Zustand == "Error" ? $"{a.Identification}: Fehler — {a.Fehler}" : $"{a.Identification}: {a.Zustand}"));
        v.Protokoll.Add(new ElmProtokollZeile { Schritt = "subscribe", RequestId = requestId, ResponseId = s.Kopf.ResponseId, Text = text });
        stand.Vorgaenge.Insert(0, v);
        _store.SpeichereUebermittlungen(stand);
        return new Ergebnis(v.Adressaten.Any(a => a.Zustand == "Success"), text, v, s.Call) { Warnungen = s.Warnungen };
    }

    // ── Verwaltung ───────────────────────────────────────────────────────

    public List<ElmVorgang> Liste() => _store.LadeUebermittlungen().Vorgaenge;

    public bool Loesche(string id)
    {
        var stand = _store.LadeUebermittlungen();
        var weg = stand.Vorgaenge.RemoveAll(v => v.Id == id) > 0;
        if (weg) _store.SpeichereUebermittlungen(stand);
        return weg;
    }

    private static ElmVorgang Finde(ElmUebermittlungsStand stand, string id)
        => stand.Vorgaenge.FirstOrDefault(v => v.Id == id)
           ?? throw new InvalidOperationException("Übermittlung nicht gefunden.");

    // ── Senden ───────────────────────────────────────────────────────────

    private record Gesendet(
        ElmTransmitterClient.ElmCallResult Call, XElement? Body, Kopf Kopf,
        string? Abbruch, string? FaultCode, List<Hinweis> Hinweise, List<string> Warnungen);

    private static string NeueId() => Guid.NewGuid().ToString("N");

    private XElement Kontext(string firma, string requestId)
    {
        var rc = RequestContext(string.IsNullOrWhiteSpace(firma) ? "OneCrew" : firma, DateTime.Now, _einst);
        rc.Element(Ep + "RequestID")?.SetValue(requestId);
        return rc;
    }

    /// <summary>
    /// Schema prüfen, signieren (doppelt, wenn SUA vorhanden und gewünscht — AB-18),
    /// verschlüsseln, senden, Antwort lesen. Eine Antwort mit ungültiger WS-Security
    /// wird NICHT übernommen (wie bei F02); ein Fault wird im Klartext zurückgegeben.
    /// </summary>
    private async Task<Gesendet> SendeAsync(string url, XElement anfrage, string archivName, bool doppelt,
        ElmUebermittlungsStand stand, string requestId, CancellationToken ct)
    {
        var xsd = _validator.Validate(anfrage.ToString());
        if (xsd.Count > 0)
            throw new InvalidOperationException($"Die Anfrage ({anfrage.Name.LocalName}) entspricht nicht dem ELM-Schema und wurde NICHT gesendet: "
                + string.Join(" · ", xsd.Take(3)));
        var erp = _store.LadeErp()
            ?? throw new InvalidOperationException("ERP-Zertifikat fehlt (Einrichtung).");
        X509Certificate2? sua = doppelt ? _store.LadeSuaZumSignieren() : null;

        var call = await _client.PostGesichertAsync(url, anfrage, erp, _store.LadeEmpfaengerFuerVerschluesselung(),
            archivName, ct, sua);
        stand.LetzteRequestId = requestId;

        var warn = new List<string>();
        if (doppelt && sua == null)
            warn.Add("Kein SUA-Zertifikat mit Schlüssel — nur mit dem ERP-Zertifikat signiert.");

        XDocument? doc = null;
        try { if (!string.IsNullOrWhiteSpace(call.ResponseXml)) doc = XDocument.Parse(call.ResponseXml); }
        catch { /* unten als unlesbar gemeldet */ }
        var body = doc == null ? null : AntwortBody(doc);
        var kopf = LiesKopf(body);
        if (kopf.RequestId != null && kopf.RequestId != requestId)
            warn.Add($"Die Antwort bezieht sich auf RequestID {kopf.RequestId}, gesendet wurde {requestId}.");
        if (kopf.ResponseId != null)
        {
            if (stand.GeseheneResponseIds.Contains(kopf.ResponseId))
                warn.Add($"ResponseID {kopf.ResponseId} wurde schon einmal empfangen (Doublette).");
            else
                stand.GeseheneResponseIds.Add(kopf.ResponseId);
        }

        var sicherheit = ElmTransmitterClient.DeuteSicherheitsFault(call);
        if (sicherheit != null)
            return new Gesendet(call, null, kopf, "WS-Security: " + sicherheit.Meldung, call.FaultCode, new(), warn);

        var fault = doc == null ? null : LiesFault(doc);
        if (call.FaultCode != null || fault != null)
        {
            var hinweise = fault?.Hinweise ?? new();
            var grund = FaultText(fault?.Code);
            var erste = hinweise.FirstOrDefault();
            var text = $"Abgewiesen — {grund}"
                + (erste != null ? $" ({erste.Code}: {erste.Text})" : call.FaultText != null ? $" ({call.FaultText})" : "");
            return new Gesendet(call, null, kopf, text, fault?.Code ?? call.FaultCode, hinweise, warn);
        }
        if (call.Security != null && !call.Security.Ok)
            return new Gesendet(call, null, kopf, "WS-Security: " + call.Security.Meldung + " — Antwort nicht übernommen.", null, new(), warn);
        if (!call.Ok || body == null)
            return new Gesendet(call, null, kopf, call.Error ?? $"Keine lesbare Antwort (HTTP {call.HttpStatus}).", null, new(), warn);
        return new Gesendet(call, body, kopf, null, null, new(), warn);
    }
}
