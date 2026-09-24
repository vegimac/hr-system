using System.Globalization;
using System.Xml.Linq;

namespace HrSystem.Services.WebStamp;

/// <summary>
/// Aufbau und Auswertung der SOAP-Nachrichten für den Webservice WebStamp V6
/// (Walter 24.09.2026). Reine Funktionen ohne Netz/DB — darum hier getrennt
/// vom <see cref="WebStampClient"/> und direkt testbar.
///
/// Technik laut WSDL (docs/webstamp/wsws-v6.wsdl): SOAP 1.1, document/literal,
/// SOAPAction «https://webstamp.post.ch/wsws/soap/v6#&lt;methode&gt;». Das Methoden-
/// Element trägt den Namespace, alles darunter ist UNQUALIFIZIERT (das Schema
/// setzt kein elementFormDefault). Jede Methode ausser <c>ping</c> bekommt
/// genau ein Kind <c>args</c> mit dem Inhalt; Listen heissen immer <c>item</c>.
/// </summary>
public static class WebStampSoap
{
    public static readonly XNamespace Env = "http://schemas.xmlsoap.org/soap/envelope/";
    public static readonly XNamespace Ns = "https://webstamp.post.ch/wsws/soap/v6";

    /// <summary>Feldlängen laut WSDL — längere Werte weist die Post mit Fehler 2000 ab.</summary>
    public const int MaxReferenz = 50;
    public const int MaxNotiz = 40;

    /// <summary>
    /// Zugangsdaten eines Aufrufs. <see cref="ApplicationId"/> vergibt die Post pro
    /// Applikation (OneCrew), <see cref="KundenId"/> + <see cref="Passwort"/> stehen im
    /// WebStamp-Konto unter «WebStamp-Einstellungen / Webservice WebStamp».
    /// Das Passwort geht im Klartext über TLS (encryption_type leer = Standard der Post).
    /// </summary>
    public record Zugang(string ApplicationId, string? KundenId, string? Passwort, string Sprache = "de");

    public static string SoapAction(string methode) => $"\"{Ns.NamespaceName}#{methode}\"";

    public static XElement Identifikation(Zugang z)
    {
        var id = new XElement("identification",
            new XElement("application", z.ApplicationId.Trim()),
            new XElement("language", z.Sprache));
        if (!string.IsNullOrWhiteSpace(z.KundenId))
            id.Add(new XElement("userid", z.KundenId.Trim()));
        if (!string.IsNullOrEmpty(z.Passwort))
            id.Add(new XElement("password", z.Passwort));
        return id;
    }

    /// <summary>Hülle um eine Methode; ohne Inhalt entsteht ein leeres Methoden-Element (ping).</summary>
    public static string Envelope(string methode, params object[] argsInhalt)
    {
        var aufruf = new XElement(Ns + methode);
        if (argsInhalt.Length > 0) aufruf.Add(new XElement("args", argsInhalt));
        var doc = new XDocument(
            new XElement(Env + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Env),
                new XAttribute(XNamespace.Xmlns + "ws", Ns),
                new XElement(Env + "Body", aufruf)));
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + doc.ToString(SaveOptions.DisableFormatting);
    }

    public static string Ping() => Envelope("ping");

    public static string KundenDaten(Zugang z) => Envelope("get_customer_data", Identifikation(z));

    /// <summary>Produkte für Geschäftskunden; mit gültigem Login liefert die Post das Sortiment des Kunden.</summary>
    public static string Produkte(Zugang z) =>
        Envelope("get_products", Identifikation(z), new XElement("customer_type", "gk"));

    /// <summary>
    /// Ein Brief für den Druck- und Versandservice: die Post sucht die Empfängeradresse
    /// im Sichtfenster des PDFs, setzt die Frankatur ein, druckt, couvertiert und
    /// verschickt (<c>printservice = true</c> + <c>document</c>).
    /// </summary>
    public record BriefAuftrag(int Produkt, byte[] Pdf, string? Referenz = null, string? Notiz = null);

    /// <param name="methode"><c>new_order_preview</c> (kostenlos) oder <c>new_order</c> (bestellt).</param>
    public static string Brief(string methode, Zugang z, BriefAuftrag a)
    {
        if (methode != "new_order_preview" && methode != "new_order")
            throw new ArgumentException("Nur new_order_preview oder new_order.", nameof(methode));
        var inhalt = new List<object>
        {
            Identifikation(z),
            new XElement("product", a.Produkt),
            new XElement("single", "false"),
            new XElement("file_type", "pdf"),
            new XElement("printservice", "true"),
            new XElement("document", Convert.ToBase64String(a.Pdf)),
        };
        var referenz = Kuerzen(a.Referenz, MaxReferenz);
        if (referenz != null) inhalt.Add(new XElement("reference", referenz));
        var notiz = Kuerzen(a.Notiz, MaxNotiz);
        if (notiz != null) inhalt.Add(new XElement("order_comment", notiz));
        return Envelope(methode, inhalt.ToArray());
    }

    private static string? Kuerzen(string? s, int max)
    {
        s = s?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length <= max ? s : s[..max];
    }

    // ── Antworten ────────────────────────────────────────────────────────────

    public record Fault(string? Code, string? Text, string? FehlerNummer, string? RequestId);

    /// <summary>SOAP-Fault aus einer Antwort — null, wenn keiner drin ist.</summary>
    public static Fault? LiesFault(XDocument doc)
    {
        var f = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault");
        if (f == null) return null;
        return new Fault(
            Kind(f, "faultcode"), Kind(f, "faultstring"),
            f.Descendants().FirstOrDefault(e => e.Name.LocalName == "code")?.Value,
            f.Descendants().FirstOrDefault(e => e.Name.LocalName == "request_id")?.Value);
    }

    /// <summary>Das eigentliche Resultat, z.B. &lt;new_orderResult&gt; unter &lt;new_orderResponse&gt;.</summary>
    public static XElement? LiesResultat(XDocument doc, string methode)
        => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == methode + "Response")
              ?.Elements().FirstOrDefault();

    public record Nachricht(string Typ, string? Text, string? Technisch, int? Code, DateTime? BestaetigenBis, string? Url);
    public record Sendung(int Nummer, string? Fenster, string? Status, string? Grund, List<int> Seiten, string? Sendungsnummer);
    public record Preisposition(string Typ, int Anzahl, decimal Betrag, string? Beschreibung);
    public record Frankiervermerk(int? StampId, int? Laufnummer, string? Sendungsnummer);

    public record Auftrag(
        int? OrderId,
        decimal Preis,
        decimal EinzelPreis,
        string? Referenz,
        int? ProduktNummer,
        DateTime? GueltigBis,
        byte[]? Druckdaten,
        List<Preisposition> Preise,
        List<Sendung> Sendungen,
        List<Nachricht> Nachrichten,
        List<Frankiervermerk> Frankiervermerke);

    public static Auftrag LiesAuftrag(XElement r) => new(
        Int(Kind(r, "order_id")),
        Dec(Kind(r, "price")) ?? 0m,
        Dec(Kind(r, "item_price")) ?? 0m,
        Leer(Kind(r, "reference")),
        Int(Kind(r, "product_number")),
        Datum(Kind(r, "valid_until")),
        Base64(Kind(r, "print_data")),
        Items(r, "price_details").Select(p => new Preisposition(
            Kind(p, "type") ?? "", Int(Kind(p, "quantity")) ?? 0,
            Dec(Kind(p, "amount")) ?? 0m, Leer(Kind(p, "description")))).ToList(),
        Items(r, "consignments").Select(c => new Sendung(
            Int(Kind(c, "number")) ?? 0, Leer(Kind(c, "window")), Leer(Kind(c, "state")),
            Leer(Kind(c, "reason")),
            Items(c, "pages").Select(p => Int(Kind(p, "number")) ?? 0).ToList(),
            Leer(Kind(c, "tracking_number")))).ToList(),
        LiesNachrichten(r),
        Items(r, "stamps").Select(s => new Frankiervermerk(
            Int(Kind(s, "stamp_id")), Int(Kind(s, "label_number")),
            Leer(Kind(s, "tracking_number")))).ToList());

    /// <summary>
    /// Mitteilungen der Post an den Kunden. Typ «confirm» (z.B. neue AGB) MUSS der
    /// Integrator anzeigen — nach Ablauf der Frist nimmt WebStamp keine Bestellung mehr an.
    /// </summary>
    public static List<Nachricht> LiesNachrichten(XElement r) =>
        Items(r, "messages").Select(m => new Nachricht(
            Kind(m, "message_type") ?? "", Leer(Kind(m, "customer_message")),
            Leer(Kind(m, "system_message")), Int(Kind(m, "system_message_code")),
            Datum(Kind(m, "confirm_until")), Leer(Kind(m, "url")))).ToList();

    public record Produkt(
        int Nummer, int PostNummer, string Name, string Kategorie, int KategorieNummer,
        decimal Preis, string? Zustellart, string? Format, int Zone, bool Barcode,
        string? Sortiment, int? MaxGewicht);

    /// <summary>Resultat von get_products: direkt eine Liste von item.</summary>
    public static List<Produkt> LiesProdukte(XElement r) =>
        r.Elements("item").Select(p => new Produkt(
            Int(Kind(p, "number")) ?? 0, Int(Kind(p, "post_product_number")) ?? 0,
            Kind(p, "name") ?? "", Kind(p, "category") ?? "", Int(Kind(p, "category_number")) ?? 0,
            Dec(Kind(p, "price")) ?? 0m, Leer(Kind(p, "delivery")), Leer(Kind(p, "format")),
            Int(Kind(p, "zone")) ?? 0, Kind(p, "barcode") == "true",
            Leer(Kind(p, "product_list")), Int(Kind(p, "max_weight")))).ToList();

    public record Kunde(string? Lizenz, string? Zahlungsart, List<int> Sortimente);

    public static Kunde LiesKunde(XElement r) => new(
        Leer(Kind(r, "license_state")), Leer(Kind(r, "payment_type")),
        Items(r, "product_lists").Select(i => int.TryParse(i.Value, out var n) ? n : 0).ToList());

    // ── Helfer ───────────────────────────────────────────────────────────────

    private static string? Kind(XElement e, string name) => e.Element(name)?.Value;
    private static IEnumerable<XElement> Items(XElement e, string liste)
        => e.Element(liste)?.Elements("item") ?? Enumerable.Empty<XElement>();
    private static string? Leer(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static int? Int(string? s)
        => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
    private static decimal? Dec(string? s)
        => decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? Math.Round(d, 2) : null;
    private static DateTime? Datum(string? s)
        => DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.LocalDateTime : null;
    private static byte[]? Base64(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        try { return Convert.FromBase64String(s.Trim()); } catch (FormatException) { return null; }
    }
}
