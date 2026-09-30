using System.Globalization;
using System.Xml.Linq;
using static HrSystem.Services.Elm.ElmGemeinsam;

namespace HrSystem.Services.Elm;

// ═══════════════════════════════════════════════════════════════════════════
//  Swissdec ELM 6.0 — Übermittlung (Foundation F05 / F06 / F08, nachts gebaut 30.09.2026)
//  Declare (Monat/Jahr) → GetStatus mit JobKey → Synchronize je Adressat,
//  dazu SubscribeOrganization. Hier nur Modelle + XML bauen/lesen (rein, testbar);
//  das Senden macht ElmUebermittlungService.
//  Quellen: Transmitter-Richtlinien Kap. 7–9, 11 (UC001–UC005), Anhang D/E/F;
//  Schemas SalaryDeclarationContainer.xsd / SwissdecComponents.xsd.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>Gespeicherter Stand aller Übermittlungen (Datei neben dem SUA-Fall).</summary>
public class ElmUebermittlungsStand
{
    public List<ElmVorgang> Vorgaenge { get; set; } = new();
    /// <summary>AB-10: doppelt empfangene ResponseIDs erkennen.</summary>
    public List<string> GeseheneResponseIds { get; set; } = new();
    /// <summary>Zuletzt gesendete RequestID — nur für die bewusste Probe F05_07.</summary>
    public string? LetzteRequestId { get; set; }
}

/// <summary>Eine Übermittlung: Declare-Job oder Anmeldung (SubscribeOrganization).</summary>
public class ElmVorgang
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    /// <summary>«monthly», «annual» oder «subscribe».</summary>
    public string Art { get; set; } = "monthly";
    public string Titel { get; set; } = "";
    public bool TestCase { get; set; }
    /// <summary>Ersatzmeldung: DeclarationID der ersetzten Meldung (AB-09).</summary>
    public string? Substitution { get; set; }
    public bool DoppeltSigniert { get; set; }
    public DateTime Gesendet { get; set; } = DateTime.Now;
    public string RequestId { get; set; } = "";
    public string? ResponseId { get; set; }
    public string? JobKey { get; set; }
    public bool JobFinished { get; set; }
    public DateTime? LetzteStatusAbfrage { get; set; }
    public int StatusAbfragen { get; set; }
    public string Uid { get; set; } = "";
    public string Firmenname { get; set; } = "";
    public List<ElmVorgangAdressat> Adressaten { get; set; } = new();
    public List<ElmProtokollZeile> Protokoll { get; set; } = new();
}

public class ElmProtokollZeile
{
    public DateTime Zeit { get; set; } = DateTime.Now;
    /// <summary>«declare», «status», «sync», «subscribe».</summary>
    public string Schritt { get; set; } = "";
    public string Text { get; set; } = "";
    public string? RequestId { get; set; }
    public string? ResponseId { get; set; }
    /// <summary>F04-Archivdateien (signierter Klartext) zu diesem Schritt.</summary>
    public string? ArchivAnfrage { get; set; }
    public string? ArchivAntwort { get; set; }
}

public class ElmVorgangAdressat
{
    /// <summary>addresseeID aus dem Job, z.B. «#QST-BE».</summary>
    public string AddresseeId { get; set; } = "";
    public string Identification { get; set; } = "";
    /// <summary>Domain fürs Synchronize (TaxAtSource, Statistic, AHV-AVS, UVG-LAA …).</summary>
    public string? Domain { get; set; }
    public bool Verarbeiten { get; set; } = true;
    /// <summary>offen / Ignored / Processing / Error / Success.</summary>
    public string Zustand { get; set; } = "offen";
    public string? Fehler { get; set; }
    public string? FehlerDetail { get; set; }
    public string? FehlerCode { get; set; }
    public string? Wartung { get; set; }
    /// <summary>DeclarationID (Declare) bzw. SubscriptionID (Anmeldung).</summary>
    public string? FallId { get; set; }
    public string? Key { get; set; }
    public string? Password { get; set; }
    public bool TestCaseBestaetigt { get; set; }
    public string? InstitutionName { get; set; }
    public List<ElmHinweis> Hinweise { get; set; } = new();
    /// <summary>Letzter State aus dem Synchronize (wird im nächsten als ReceivedState quittiert).</summary>
    public string? State { get; set; }
    public DateTime? LetzteSynchronisierung { get; set; }
    public int Synchronisierungen { get; set; }
    public List<ElmStoryEmpfangen> Stories { get; set; } = new();
    /// <summary>Eigene Stories (Dialog-Antworten), bis der Empfänger sie quittiert.</summary>
    public List<ElmStoryGesendet> Ausstehend { get; set; } = new();
    /// <summary>Vom Distributor unterdrückte Empfänger-Stories — bei jedem Synchronize mitsenden (UC009).</summary>
    public List<string> UnterdrueckteInstitutionStories { get; set; } = new();
    public List<string> UnterdrueckteSenderStories { get; set; } = new();
    public List<string> Verfuegbar { get; set; } = new();
    public ElmCompletionInfo? Completion { get; set; }
}

public class ElmHinweis
{
    /// <summary>Warning / Info / Error.</summary>
    public string Art { get; set; } = "Info";
    public string? Stufe { get; set; }
    public string? Code { get; set; }
    public string? Text { get; set; }
    public string? StoryId { get; set; }
}

public class ElmStoryEmpfangen
{
    public string StoryId { get; set; } = "";
    /// <summary>Elementname im Case, z.B. DialogMessage, Completion, TaxAtSource-Quittance.</summary>
    public string Art { get; set; } = "";
    public DateTime Empfangen { get; set; } = DateTime.Now;
    /// <summary>In ReceivedStoryIDs gesendet und die Antwort kam ohne Fehler.</summary>
    public bool Quittiert { get; set; }
    public string Xml { get; set; } = "";
    public bool Beantwortet { get; set; }
    public string? AntwortStoryId { get; set; }
    /// <summary>Kam dieselbe StoryID schon einmal (Doublette)?</summary>
    public int Empfangszaehler { get; set; } = 1;
}

public class ElmStoryGesendet
{
    public string StoryId { get; set; } = "";
    public string Art { get; set; } = "DialogMessage";
    public string Xml { get; set; } = "";
    public DateTime Erstellt { get; set; } = DateTime.Now;
    public int Gesendet { get; set; }
    public string? AntwortAuf { get; set; }
}

public class ElmCompletionInfo
{
    public string Url { get; set; } = "";
    public string AufrufUrl { get; set; } = "";
    public DateTime? Ablauf { get; set; }
    public string? Key { get; set; }
    public string? Password { get; set; }
    public bool KeyAusFall { get; set; }
}

/// <summary>XML für die Übermittlung bauen und Antworten lesen — ohne Netz, ohne DB.</summary>
public static class ElmUebermittlungXml
{
    public static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";

    public record Adressat(string AddresseeId, string Identification, string? Domain, bool Verarbeiten);

    public record DeclareVorlage(string Art, List<Adressat> Adressaten, string Uid, string Firmenname);

    public record Hinweis(string Art, string? Stufe, string? Code, string? Text, string? StoryId);

    public record Kopf(string? ResponseId, string? RequestId, string? Institution);

    public record Fault(string? Code, List<Hinweis> Hinweise);

    // ── Declare vorbereiten ──────────────────────────────────────────────

    public static string ArtVon(XElement root) => root.Name.LocalName switch
    {
        "DeclareMonthlySalary" => "monthly",
        "DeclareAnnualSalary" => "annual",
        _ => throw new InvalidOperationException($"Unbekannte Meldung «{root.Name.LocalName}».")
    };

    /// <summary>Adressaten, Domains und Absender aus einer fertigen Meldung.</summary>
    public static DeclareVorlage LiesVorlage(XElement root)
    {
        var art = ArtVon(root);
        var institutionen = root.Descendants().Where(e => e.Name.LocalName == "Institutions")
            .SelectMany(i => i.Elements()).ToList();
        var job = root.Element(Sdc + "Job") ?? throw new InvalidOperationException("Meldung ohne Job.");
        var adressaten = job.Element(Sdc + "Addressees")?.Elements(Sdc + "Addressee")
            .Select(a =>
            {
                var id = (string?)a.Attribute("addresseeID") ?? "";
                var inst = institutionen.FirstOrDefault(i => (string?)i.Attribute("addresseeIDRef") == id);
                return new Adressat(id,
                    a.Element(Ep + "AddresseeIdentification")?.Value.Trim() ?? "",
                    inst?.Name.LocalName,
                    !string.Equals(a.Element(Ep + "ProcessByDistributor")?.Value.Trim(), "false", StringComparison.Ordinal));
            }).ToList() ?? new();
        var uid = root.Descendants().Where(e => e.Name.LocalName == "UID-BFS")
            .Select(e => e.Elements().FirstOrDefault(k => k.Name.LocalName == "UID")?.Value.Trim())
            .FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
        var firma = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "HR-RC-Name")?.Value.Trim()
            ?? root.Descendants().FirstOrDefault(e => e.Name.LocalName == "CompanyName")?.Value.Trim() ?? "";
        return new DeclareVorlage(art, adressaten, uid, firma);
    }

    /// <summary>
    /// Job der Meldung für den Versand setzen: ProcessByDistributor je Adressat (AB-07),
    /// TestCase (AB-11), Substitution (AB-09), frische RequestID und Sendezeit.
    /// Reihenfolge im Job laut Schema: Addressees, TestCase, Substitution.
    /// </summary>
    public static XElement BereiteDeclareVor(XElement root, IReadOnlyDictionary<string, bool>? auswahl,
        bool testCase, string? substitution, string requestId, DateTime jetzt)
    {
        var el = new XElement(root);
        SetzeKopf(el, requestId, jetzt);
        var job = el.Element(Sdc + "Job") ?? throw new InvalidOperationException("Meldung ohne Job.");
        if (auswahl != null)
        {
            foreach (var a in job.Element(Sdc + "Addressees")?.Elements(Sdc + "Addressee") ?? Enumerable.Empty<XElement>())
            {
                var id = (string?)a.Attribute("addresseeID") ?? "";
                if (auswahl.TryGetValue(id, out var ja))
                    a.Element(Ep + "ProcessByDistributor")?.SetValue(ja ? "true" : "false");
            }
        }
        job.Element(Sdc + "TestCase")?.Remove();
        job.Element(Sdc + "Substitution")?.Remove();
        if (testCase) job.Add(new XElement(Sdc + "TestCase"));
        var sub = (substitution ?? "").Trim();
        if (sub.Length > 0)
            job.Add(new XElement(Sdc + "Substitution",
                new XElement(Sdc + "PredecessorDeclarationIDWithAcceptedState", sub)));
        return el;
    }

    /// <summary>RequestID und TransmissionDate im RequestContext neu setzen (AB-10).</summary>
    public static void SetzeKopf(XElement root, string requestId, DateTime jetzt)
    {
        var rc = root.Element(Ep + "RequestContext");
        if (rc == null) return;
        rc.Element(Ep + "RequestID")?.SetValue(requestId);
        rc.Element(Ep + "TransmissionDate")?.SetValue(jetzt.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz", CultureInfo.InvariantCulture));
    }

    // ── GetStatus / Synchronize / Subscribe bauen ────────────────────────

    public static XElement BaueGetStatus(string art, string jobKey, XElement requestContext)
        => new(Sdst + (art == "annual" ? "GetStatusFromDeclareAnnualSalary" : "GetStatusFromDeclareMonthlySalary"),
            new XAttribute(XNamespace.Xmlns + "sdst", Sdst),
            new XAttribute(XNamespace.Xmlns + "ep", Ep),
            requestContext,
            new XElement(Ep + "JobKey", jobKey));

    private static XElement? StoryListe(string name, IEnumerable<string> ids)
    {
        var l = ids.Where(i => !string.IsNullOrWhiteSpace(i)).Distinct().ToList();
        return l.Count == 0 ? null : new XElement(Ep + name, l.Select(i => new XElement(Ep + "StoryID", i)));
    }

    private static XElement Sender(string uid)
        => new(Ep + "Sender", new XElement(Ep + "UID-BFS", new XElement(Ep + "UID", uid)));

    private static IEnumerable<XAttribute> NsAttribute() => new[]
    {
        new XAttribute("schemaVersion", "0.0"),
        new XAttribute(XNamespace.Xmlns + "sdst", Sdst),
        new XAttribute(XNamespace.Xmlns + "sdc", Sdc),
        new XAttribute(XNamespace.Xmlns + "sd", Sd),
        new XAttribute(XNamespace.Xmlns + "ep", Ep),
        new XAttribute(XNamespace.Xmlns + "c", C),
    };

    /// <summary>
    /// Synchronize für einen Declare-Fall (UC005): quittiert erhaltene Stories und den
    /// letzten State, spiegelt DeclarationID + Credentials, trägt TestCase weiter und
    /// sendet eigene, noch nicht quittierte Dialog-Antworten erneut.
    /// </summary>
    public static XElement BaueSynchronizeDeclare(ElmVorgang v, ElmVorgangAdressat a,
        IEnumerable<string> quittieren, XElement requestContext)
    {
        var name = v.Art == "annual" ? "SynchronizeDeclareAnnualSalary" : "SynchronizeDeclareMonthlySalary";
        return new XElement(Sdst + name, NsAttribute(),
            requestContext,
            Sender(v.Uid),
            new XElement(Sdc + "Addressee",
                new XElement(Ep + "AddresseeIdentification", a.Identification),
                new XElement(Sd + "Domain", a.Domain ?? "")),
            new XElement(Sdc + "Case",
                new XElement(Sdc + "CaseContext",
                    StoryListe("ReceivedStoryIDs", quittieren),
                    StoryListe("SuppressedSenderStoryIDs", a.UnterdrueckteSenderStories),
                    StoryListe("SuppressedInstitutionStoryIDs", a.UnterdrueckteInstitutionStories),
                    new XElement(Ep + "Credentials",
                        new XElement(Ep + "Key", a.Key ?? ""),
                        new XElement(Ep + "Password", a.Password ?? "")),
                    new XElement(Sdc + "DeclarationID", a.FallId ?? ""),
                    v.TestCase ? new XElement(Sdc + "TestCase") : null),
                a.State != null ? new XElement(Sdc + "ReceivedState", a.State) : null,
                a.Ausstehend.Select(s => XElement.Parse(s.Xml))));
    }

    public static XElement BaueSubscribe(string uid, string firma, string kontakt, string addresseeIdentification,
        string domain, string plz, string ort, string versicherer, string kundennummer, string vertragsnummer,
        bool testCase, XElement requestContext)
    {
        const string refId = "#addressee";
        return new XElement(Sdst + "SubscribeOrganization", NsAttribute(),
            requestContext,
            new XElement(Sdc + "Job",
                new XElement(Sdc + "Addressee",
                    new XAttribute("addresseeID", refId),
                    new XElement(Ep + "AddresseeIdentification", addresseeIdentification),
                    new XElement(Ep + "ProcessByDistributor", "true")),
                testCase ? new XElement(Sdc + "TestCase") : null),
            new XElement(Sdc + "Company",
                new XElement(Sd + "Institution",
                    new XElement(Sd + domain,
                        new XAttribute("addresseeIDRef", refId),
                        new XElement(C + "InsuranceCompanyName", versicherer),
                        new XElement(C + "CustomerIdentity", kundennummer),
                        new XElement(C + "ContractIdentity", vertragsnummer))),
                new XElement(Sdc + "CompanyDescription",
                    new XElement(C + "Name", new XElement(C + "HR-RC-Name", firma)),
                    new XElement(C + "Address",
                        new XElement(C + "ZIP-Code", plz),
                        new XElement(C + "City", ort)),
                    new XElement(C + "UID-BFS", new XElement(Ep + "UID", uid))),
                new XElement(Sdc + "Contact", new XElement(C + "Name", kontakt))));
    }

    public static XElement BaueSynchronizeSubscribe(ElmVorgang v, ElmVorgangAdressat a,
        IEnumerable<string> quittieren, bool abmelden, XElement requestContext)
        => new(Sdst + "SynchronizeSubscribeOrganization", NsAttribute(),
            requestContext,
            Sender(v.Uid),
            new XElement(Sdc + "Addressee",
                new XElement(Ep + "AddresseeIdentification", a.Identification),
                new XElement(Sd + "Domain", a.Domain ?? "UVG-LAA")),
            new XElement(Sdc + "Case",
                new XElement(Sdc + "CaseContext",
                    StoryListe("ReceivedStoryIDs", quittieren),
                    StoryListe("SuppressedSenderStoryIDs", a.UnterdrueckteSenderStories),
                    StoryListe("SuppressedInstitutionStoryIDs", a.UnterdrueckteInstitutionStories),
                    new XElement(Sdc + "SubscriptionID", a.FallId ?? ""),
                    v.TestCase ? new XElement(Sdc + "TestCase") : null),
                a.State != null ? new XElement(Sdc + "ReceivedState", a.State) : null,
                abmelden ? new XElement(Sdc + "Unsubscribe") : null));

    // ── Antworten lesen ──────────────────────────────────────────────────

    /// <summary>Der eigentliche Antwort-Body (erstes Kind von soap:Body), sonst die Wurzel.</summary>
    public static XElement? AntwortBody(XDocument doc)
    {
        var body = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Body");
        return body?.Elements().FirstOrDefault() ?? doc.Root;
    }

    private static XElement? K(XElement? e, string name)
        => e?.Elements().FirstOrDefault(k => k.Name.LocalName == name);

    private static string? T(XElement? e, string name) => K(e, name)?.Value.Trim();

    public static Kopf LiesKopf(XElement? body)
    {
        var rc = K(body, "ResponseContext");
        return new Kopf(T(rc, "ResponseID"), T(rc, "RequestID"), T(rc, "InstitutionName"));
    }

    /// <summary>SalaryDeclarationFault im SOAP-Fault (Code NOT_plausible / NOT_valid / NOT_accepted).</summary>
    public static Fault? LiesFault(XDocument doc)
    {
        var sdf = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "SalaryDeclarationFault");
        if (sdf == null) return null;
        var f = K(sdf, "Fault");
        return new Fault(T(f, "Code"), Hinweise(f));
    }

    /// <summary>
    /// Alle Notification-Zeilen unter <paramref name="scope"/>. Die Art (Error/Warning/Info)
    /// steht am Eltern- oder Grosselternelement — je nach Typ (NotificationsType hat
    /// Notification-Kinder, FeedbackNotificationsType direkt Warning/Info).
    /// </summary>
    public static List<Hinweis> Hinweise(XElement? scope)
    {
        if (scope == null) return new();
        return scope.DescendantsAndSelf()
            .Where(e => e.Elements().Any(k => k.Name.LocalName == "QualityLevel"))
            .Select(n =>
            {
                var art = n.Name.LocalName is "Warning" or "Info" or "Error" ? n.Name.LocalName
                        : n.Parent?.Name.LocalName is "Warning" or "Info" or "Error" ? n.Parent!.Name.LocalName
                        : "Info";
                return new Hinweis(art, T(n, "QualityLevel"), T(n, "DescriptionCode"), T(n, "Description"), T(n, "StoryID"));
            }).ToList();
    }

    public static ElmHinweis ZuModell(Hinweis h)
        => new() { Art = h.Art, Stufe = h.Stufe, Code = h.Code, Text = h.Text, StoryId = h.StoryId };

    public record AdressatStatus(
        string AddresseeId, string Identification, bool Verarbeiten, string Zustand,
        string? Fehler, string? FehlerDetail, string? FehlerCode, string? Wartung,
        string? FallId, string? Key, string? Password, bool TestCase, string? Institution,
        List<Hinweis> Hinweise);

    public record StatusAntwort(bool? JobFinished, string? JobKey, List<AdressatStatus> Adressaten);

    /// <summary>
    /// GetStatusFrom…Response und SubscribeOrganizationResponse (gleicher Aufbau je Adressat:
    /// Ignored / Processing / Error / Success). DeclareResponse liefert nur den JobKey.
    /// </summary>
    public static StatusAntwort LiesStatus(XElement? body)
    {
        var fertig = T(body, "JobFinished");
        var liste = new List<AdressatStatus>();
        foreach (var a in K(body, "Addressees")?.Elements().Where(e => e.Name.LocalName == "Addressee") ?? Enumerable.Empty<XElement>())
        {
            var id = (string?)a.Attribute("addresseeID") ?? "";
            var ident = T(a, "AddresseeIdentification") ?? "";
            var verarbeiten = !string.Equals(T(a, "ProcessByDistributor"), "false", StringComparison.Ordinal);
            string zustand = "offen";
            string? fehler = null, detail = null, code = null, wartung = null, fallId = null, key = null, pw = null, inst = null;
            var test = false;
            var hinweise = new List<Hinweis>();
            if (K(a, "Ignored") != null) zustand = "Ignored";
            else if (K(a, "Processing") != null) zustand = "Processing";
            else if (K(a, "Error") is XElement err)
            {
                zustand = "Error";
                fehler = T(err, "EndUserInformation");
                detail = T(err, "DetailInformation");
                var fi = K(err, "FaultInformation");
                code = T(K(fi, "FaultState"), "Code");
                if (K(err, "PlannedMaintenance") is XElement pm)
                    wartung = $"{T(pm, "Start")} – {T(pm, "End")}: {T(pm, "Message")}";
                hinweise = Hinweise(fi);
            }
            else if (K(a, "Success") is XElement ok)
            {
                zustand = "Success";
                var ctx = K(ok, "AddresseeContext");
                fallId = T(ctx, "DeclarationID") ?? T(ctx, "SubscriptionID");
                test = K(ctx, "TestCase") != null;
                inst = T(ctx, "InstitutionName");
                hinweise = Hinweise(ctx);
                var cred = K(ok, "Credentials");
                key = T(cred, "Key");
                pw = T(cred, "Password");
            }
            liste.Add(new AdressatStatus(id, ident, verarbeiten, zustand, fehler, detail, code, wartung,
                fallId, key, pw, test, inst, hinweise));
        }
        return new StatusAntwort(
            fertig == null ? null : string.Equals(fertig, "true", StringComparison.Ordinal),
            T(body, "JobKey"), liste);
    }

    public record SyncStory(string StoryId, string Art, string Xml);

    public record SyncAntwort(
        string? Fehler, string? FehlerDetail, string? State, string? FallId,
        List<string> Quittiert, List<string> UnterdruecktSender, List<string> UnterdruecktInstitution,
        List<SyncStory> Stories, List<Hinweis> Hinweise, List<string> Verfuegbar,
        string? CompletionUrl, DateTime? CompletionAblauf, string? CompletionKey, string? CompletionPassword,
        bool TestCase);

    /// <summary>Synchronize…Response lesen: Error ODER Consumer mit Case (+ Available bei der Anmeldung).</summary>
    public static SyncAntwort LiesSynchronize(XElement? body)
    {
        var leer = new List<string>();
        if (K(body, "Error") is XElement err)
            return new SyncAntwort(T(err, "EndUserInformation") ?? "Fehler ohne Text", T(err, "DetailInformation"),
                null, null, leer, new(), new(), new(), Hinweise(err), new(), null, null, null, null, false);

        var consumer = body?.Elements().FirstOrDefault(e => e.Name.LocalName.EndsWith("Consumer", StringComparison.Ordinal));
        var fall = K(consumer, "Case");
        var ctx = K(fall, "CaseContext");
        List<string> Ids(string liste) => K(ctx, liste)?.Elements().Select(e => e.Value.Trim())
            .Where(v => v.Length > 0).ToList() ?? new();

        var stories = new List<SyncStory>();
        string? cUrl = null, cKey = null, cPw = null;
        DateTime? cAblauf = null;
        foreach (var s in fall?.Elements() ?? Enumerable.Empty<XElement>())
        {
            if (s.Name.LocalName is "CaseContext" or "State") continue;
            var sid = T(s, "StoryID");
            if (string.IsNullOrEmpty(sid)) continue;
            stories.Add(new SyncStory(sid, s.Name.LocalName, s.ToString(SaveOptions.DisableFormatting)));
            if (s.Name.LocalName == "Completion")
            {
                var ci = K(s, "Completion");
                cUrl = T(ci, "Url");
                if (DateTime.TryParse(T(ci, "ExpiryDate"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    cAblauf = d;
                var cr = K(K(s, "Credentials"), "Credentials");
                cKey = T(cr, "Key");
                cPw = T(cr, "Password");
            }
        }
        var hinweise = Hinweise(K(consumer, "AddresseeContext"));
        hinweise.AddRange(Hinweise(K(ctx, "Warning")));
        hinweise.AddRange(Hinweise(K(ctx, "Info")));
        var verfuegbar = K(consumer, "Available")?.Descendants()
            .Where(e => e.Name.LocalName == "DeclarationID")
            .Select(e => $"{e.Parent?.Name.LocalName}: {e.Value.Trim()}").ToList() ?? new();

        return new SyncAntwort(null, null, T(fall, "State"),
            T(ctx, "DeclarationID") ?? T(ctx, "SubscriptionID"),
            Ids("ReceivedStoryIDs"), Ids("SuppressedSenderStoryIDs"), Ids("SuppressedInstitutionStoryIDs"),
            stories, hinweise, verfuegbar, cUrl, cAblauf, cKey, cPw, K(ctx, "TestCase") != null);
    }

    /// <summary>
    /// Stand eines Falls nach einer Synchronize-Antwort übernehmen (UC005/UC008/UC009).
    /// <paramref name="gesendetQuittiert"/> = StoryIDs, die wir in DIESEM Request als
    /// ReceivedStoryIDs geschickt haben — sie gelten jetzt als quittiert.
    /// </summary>
    public static void Uebernehme(ElmVorgangAdressat a, SyncAntwort s, IReadOnlyCollection<string> gesendetQuittiert)
    {
        a.Synchronisierungen++;
        a.LetzteSynchronisierung = DateTime.Now;
        if (s.Fehler != null)
        {
            a.Fehler = s.Fehler;
            a.FehlerDetail = s.FehlerDetail;
            return;
        }
        foreach (var st in a.Stories.Where(x => gesendetQuittiert.Contains(x.StoryId)))
            st.Quittiert = true;
        if (s.State != null) a.State = s.State;
        // Eigene Stories, die der Empfänger quittiert oder der Distributor unterdrückt hat,
        // nicht mehr senden.
        a.Ausstehend.RemoveAll(x => s.Quittiert.Contains(x.StoryId) || s.UnterdruecktSender.Contains(x.StoryId));
        foreach (var id in s.UnterdruecktSender)
            if (!a.UnterdrueckteSenderStories.Contains(id)) a.UnterdrueckteSenderStories.Add(id);
        foreach (var id in s.UnterdruecktInstitution)
            if (!a.UnterdrueckteInstitutionStories.Contains(id)) a.UnterdrueckteInstitutionStories.Add(id);
        foreach (var st in s.Stories)
        {
            var alt = a.Stories.FirstOrDefault(x => x.StoryId == st.StoryId);
            if (alt != null)
            {
                // Schon einmal erhalten: nochmals quittieren (die Quittung kam offenbar nicht an).
                alt.Empfangszaehler++;
                alt.Quittiert = false;
                continue;
            }
            a.Stories.Add(new ElmStoryEmpfangen { StoryId = st.StoryId, Art = st.Art, Xml = st.Xml });
        }
        a.Hinweise = s.Hinweise.Select(ZuModell).ToList();
        a.Verfuegbar = s.Verfuegbar;
        if (s.CompletionUrl != null)
        {
            var key = s.CompletionKey;
            var pw = s.CompletionPassword;
            var ausFall = key == null && pw == null;
            if (ausFall) { key = a.Key; pw = a.Password; }
            a.Completion = new ElmCompletionInfo
            {
                Url = s.CompletionUrl,
                AufrufUrl = ElmCompletion.Url(s.CompletionUrl, key, pw),
                Ablauf = s.CompletionAblauf,
                Key = key,
                Password = pw,
                KeyAusFall = ausFall,
            };
        }
        a.Fehler = null;
        a.FehlerDetail = null;
    }

    /// <summary>
    /// Ergebniszeile eines Synchronize: welche Stories wir quittiert haben, welche neu
    /// kamen und welche der Empfänger nochmals geschickt hat — je mit Art und StoryID,
    /// damit man im Protokoll ohne Archiv sieht, was passiert ist (F08_03).
    /// </summary>
    public static string SyncText(ElmVorgangAdressat a, IReadOnlyCollection<string> quittiert,
        IReadOnlyCollection<string> neu, IReadOnlyCollection<string> erneut)
    {
        string Liste(IEnumerable<string> ids) => string.Join(", ", ids.Select(id =>
        {
            var art = a.Stories.FirstOrDefault(x => x.StoryId == id)?.Art;
            return art == null ? id : $"{art} {id}";
        }));
        return $"{a.Identification}: {StateText(a.State)}"
             + (quittiert.Count > 0 ? $" · quittiert: {Liste(quittiert)}" : "")
             + (neu.Count > 0 ? $" · neu: {Liste(neu)}" : "")
             + (erneut.Count > 0 ? $" · erneut erhalten: {Liste(erneut)}" : "")
             + (a.Ausstehend.Count > 0 ? $" · {a.Ausstehend.Count} eigene Antwort(en) noch nicht quittiert" : "");
    }

    /// <summary>StoryIDs, die im nächsten Synchronize quittiert werden müssen.</summary>
    public static List<string> ZuQuittieren(ElmVorgangAdressat a)
        => a.Stories.Where(s => !s.Quittiert && !a.UnterdrueckteInstitutionStories.Contains(s.StoryId))
            .Select(s => s.StoryId).Distinct().ToList();

    public static string StateText(string? state) => state switch
    {
        "Accepted" => "Accepted — Meldung angenommen.",
        "CompletionReleaseMissing" => "CompletionReleaseMissing — Freigabe beim Empfänger fehlt (Completion-Link öffnen).",
        "DialogMessagePending" => "DialogMessagePending — der Empfänger wartet auf eine Dialog-Antwort.",
        "Processing" => "Processing — in Bearbeitung beim Empfänger.",
        "Finished" => "Finished — Meldung abgeschlossen.",
        "Rejected" => "Rejected — Meldung abgelehnt.",
        "subscribed" => "subscribed — angemeldet.",
        "closed" => "closed — Anmeldung beendet.",
        null => "—",
        _ => state,
    };

    public static string FaultText(string? code) => code switch
    {
        "NOT_plausible" => "NOT_plausible — die Meldung verletzt Plausibilitätsregeln des Distributors",
        "NOT_valid" => "NOT_valid — die Meldung entspricht nicht dem Schema",
        "NOT_accepted" => "NOT_accepted — die Meldung wurde nicht angenommen",
        null => "Fehler",
        _ => code,
    };
}
