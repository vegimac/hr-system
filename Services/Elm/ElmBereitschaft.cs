using System.Security.Cryptography.X509Certificates;
using static HrSystem.Services.Elm.ElmTransmitterClient;

namespace HrSystem.Services.Elm;

/// <summary>
/// Bereitschafts-Check vor dem Foundation-Termin mit itserv (Walter 29.09.2026):
/// ein Knopf, der alles prüft, was am Termin funktionieren muss — Zertifikate samt
/// Restlaufzeit, MonitoringID, Ping und CheckInterop. Jeder Punkt ist grün, orange oder rot
/// und sagt, was zu tun ist.
/// </summary>
public static class ElmBereitschaft
{
    public const string Ok = "ok";
    public const string Warn = "warn";
    public const string Rot = "rot";

    /// <summary>Ab so vielen Resttagen wird das SUA-Zertifikat orange.</summary>
    public const int SuaWarnTage = 3;

    public record Punkt(string Titel, string Stufe, string Text, string? Tipp = null);

    public static Punkt Ziel(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return new("Ziel", Rot, "Keine Adresse gewählt.", "Einrichtung → «Refapps Receiver (Test)» wählen.");
        if (url == ElmEndpunkte.TestUrl)
            return new("Ziel", Ok, "Refapps Receiver (Testinfrastruktur).");
        if (url == ElmEndpunkte.ProdUrl)
            return new("Ziel", Rot, "Produktiver Distributor gewählt — der Foundation-Test läuft gegen die Testinfrastruktur.",
                "Einrichtung → «Refapps Receiver (Test)» wählen.");
        return new("Ziel", Warn, $"Eigene Adresse: {url}",
            "Nur verwenden, wenn itserv diese Adresse vorgegeben hat.");
    }

    public static Punkt Erp(X509Certificate2? erp, DateTime jetzt)
    {
        if (erp == null)
            return new("ERP-Zertifikat", Rot, "Kein ERP-Zertifikat hinterlegt — ohne kann OneCrew weder signieren noch entschlüsseln.",
                "Einrichtung → Swissdec-.pfx importieren.");
        if (!erp.HasPrivateKey)
            return new("ERP-Zertifikat", Rot, "Das ERP-Zertifikat hat keinen privaten Schlüssel.",
                "Die .pfx mit Schlüssel erneut importieren.");
        if (jetzt < erp.NotBefore || jetzt > erp.NotAfter)
            return new("ERP-Zertifikat", Rot, $"Abgelaufen bzw. noch nicht gültig (Laufzeit {erp.NotBefore:dd.MM.yyyy} – {erp.NotAfter:dd.MM.yyyy}).");
        var name = erp.GetNameInfo(X509NameType.SimpleName, false);
        if (string.Equals(erp.Subject, erp.Issuer, StringComparison.OrdinalIgnoreCase))
            return new("ERP-Zertifikat", Warn, $"«{name}» ist selbst signiert — RefApps lehnt das ab.",
                "Einrichtung → Swissdec-.pfx importieren.");
        return new("ERP-Zertifikat", Ok, $"«{name}», ausgestellt von «{erp.GetNameInfo(X509NameType.SimpleName, true)}», gültig bis {erp.NotAfter:dd.MM.yyyy}.");
    }

    public static Punkt Sua(X509Certificate2? sua, DateTime jetzt)
    {
        if (sua == null)
            return new("SUA-Zertifikat", Rot, "Kein SUA-Zertifikat — F07_07 und F07_08 lassen sich so nicht vorführen.",
                "F07 von vorne: F07_01 Registrieren, dann Synchronisieren mit Einmalpasswort.");
        if (!sua.HasPrivateKey)
            return new("SUA-Zertifikat", Rot, "Das SUA-Zertifikat hat keinen privaten Schlüssel — keine zweite Signatur möglich.",
                "F07 von vorne durchlaufen.");
        if (jetzt > sua.NotAfter)
            return new("SUA-Zertifikat", Rot, $"Abgelaufen am {sua.NotAfter:dd.MM.yyyy HH:mm}. Erneuern geht jetzt nicht mehr.",
                "F07 von vorne: neu registrieren, neues Einmalpasswort.");
        var rest = sua.NotAfter - jetzt;
        var tage = (int)Math.Floor(rest.TotalDays);
        var restText = tage >= 1 ? $"noch {tage} Tag{(tage == 1 ? "" : "e")}" : $"noch {Math.Max(0, (int)rest.TotalHours)} Stunden";
        var text = $"Gültig bis {sua.NotAfter:dd.MM.yyyy HH:mm} ({restText}).";
        var tipp = $"Liegt der Termin nach dem {sua.NotAfter:dd.MM.yyyy}: vorher erneuern (F07 → F07_07 → Erneuern). "
                 + "Nach Ablauf ist kein Erneuern mehr möglich.";
        return new("SUA-Zertifikat", tage < SuaWarnTage ? Warn : Ok, text, tipp);
    }

    public static Punkt Empfaenger(X509Certificate2? empf, IReadOnlyList<X509Certificate2> vertrauensliste)
    {
        if (empf == null)
            return new("Empfängerzertifikat", Rot, "Kein Zertifikat zum Verschlüsseln der Anfragen.",
                "Einrichtung → Empfängerzertifikat hochladen (SwissdecDistributorELMv6Test).");
        var name = empf.GetNameInfo(X509NameType.SimpleName, false);
        if (!ElmWsSecurity.IstVertrauenswuerdig(empf, vertrauensliste))
            return new("Empfängerzertifikat", Warn, $"«{name}» steht nicht in der Vertrauensliste.",
                "Einrichtung → SwissdecDistributorELMv6Test hochladen.");
        return new("Empfängerzertifikat", Ok, $"«{name}», gültig bis {empf.NotAfter:dd.MM.yyyy}.");
    }

    public static Punkt Vertrauen(IReadOnlyList<X509Certificate2> vertrauensliste)
    {
        if (vertrauensliste.Count == 0)
            return new("Vertrauensliste (F02_08)", Rot, "Leer — jede Antwort würde als «nicht vertrauenswürdig» abgelehnt.",
                "Assets/Swissdec fehlt im Programmverzeichnis.");
        var namen = string.Join(", ", vertrauensliste.Select(z => $"«{z.GetNameInfo(X509NameType.SimpleName, false)}»"));
        return new("Vertrauensliste (F02_08)", Ok, $"{vertrauensliste.Count} Zertifikate: {namen}.");
    }

    public static Punkt Monitoring(string? id)
        => string.IsNullOrWhiteSpace(id)
            ? new("MonitoringID", Rot, "Keine MonitoringID — die Testsysteme verlangen sie in jeder Anfrage.",
                "Einrichtung → MonitoringID eintragen.")
            : new("MonitoringID", Ok, id);

    public static Punkt Archiv(int anzahl, bool beschreibbar)
        => !beschreibbar
            ? new("Archiv (F04)", Rot, "Der Archiv-Ordner ist nicht beschreibbar.")
            : new("Archiv (F04)", Ok, anzahl == 0 ? "Beschreibbar, noch leer." : $"Beschreibbar, {anzahl} Nachrichten archiviert.");

    public static Punkt Ping(ElmCallResult r)
    {
        var zeit = r.DiffSekunden.HasValue ? $" · Zeitabweichung {Math.Abs(r.DiffSekunden.Value):0.0} s" : "";
        if (!r.Ok)
            return new("Ping", Rot, $"Fehlgeschlagen: {r.FaultText ?? r.Error ?? "HTTP " + r.HttpStatus}.",
                "Internetverbindung und Ziel prüfen.");
        if (r.ZeitAbweichung)
            return new("Ping", Rot, $"Antwort erhalten, aber die Serveruhr weicht mehr als {ZeitToleranzSekunden} s ab{zeit}.",
                "Serverzeit (NTP) prüfen.");
        return new("Ping", Ok, $"HTTP {r.HttpStatus} in {r.DauerMs} ms{zeit}.");
    }

    public static Punkt Interop(ElmCallResult r, bool suaVorhanden)
    {
        const string titel = "CheckInterop";
        if (r.FaultCode != null || r.FaultText != null)
            return new(titel, Rot, $"Abgewiesen: {r.FaultCode} {r.FaultText}".Trim() + ".");
        if (!r.Ok)
            return new(titel, Rot, $"Fehlgeschlagen: {r.Error ?? "HTTP " + r.HttpStatus}.");
        if (r.Security == null)
            return new(titel, Rot, "Die Antwort wurde nicht sicherheitsgeprüft (kein ERP-Zertifikat?).");
        if (!r.Security.Ok)
            return new(titel, Rot, $"WS-Security: {r.Security.Meldung}");
        if (r.Interop is not { Ok: true })
            return new(titel, Rot, r.Interop?.Meldung ?? "Die Antwort liess sich nicht nachrechnen.");
        var teile = new List<string> { r.Security.Meldung.TrimEnd('.'), "Rechnung stimmt" };
        if (r.Tls != null) teile.Add(r.Tls.Protokoll);
        if (r.DoppeltSigniert) teile.Add("doppelt signiert (ERP + SUA)");
        var text = string.Join(" · ", teile) + ".";
        if (suaVorhanden && !r.DoppeltSigniert)
            return new(titel, Warn, text + " Die Anfrage war aber nur einfach signiert, obwohl ein SUA-Zertifikat da ist.");
        if (r.ZeitAbweichung)
            return new(titel, Warn, text + $" Zeitabweichung über {ZeitToleranzSekunden} s.");
        return new(titel, Ok, text);
    }
}
