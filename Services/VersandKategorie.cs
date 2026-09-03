namespace HrSystem.Services;

/// <summary>
/// Verteiler-Kategorie für ausgehende Nachrichten (Walter-Vorgabe
/// 01.09.2026). Ersetzt das frühere Bool-Flag «bypassTestRedirect» beim
/// Mail und den freien purpose-String beim SMS.
///
/// Pro Kategorie wird in der Systemsteuerung je Kanal (Mail / SMS) ein
/// Haken gesetzt: HAKEN = scharf an den echten Empfänger, KEIN HAKEN =
/// Umleitung an die Test-Adresse bzw. Test-Nummer.
///
/// Die Test-Adresse bleibt dauerhaft in der Systemeinstellung stehen —
/// sie ist NICHT mehr der Hauptschalter, sondern nur noch das Ziel der
/// Umleitung. Gesteuert wird ausschliesslich über die Haken.
///
/// Der Schnitt folgt dem Schadensradius, nicht der technischen Herkunft:
/// eine einzelne Lohnzettel-Mail ist etwas anderes als ein Gruppenversand
/// an 200 MA, auch wenn beide «an MA» gehen.
/// </summary>
public enum VersandKategorie
{
    /// <summary>OneCrew-Benutzer (eigenes Team): Dokument-Benachrichtigung, Mirus-Digest.</summary>
    Intern = 0,
    /// <summary>Postfach-Einladung / Zugangslink an einen MA.</summary>
    Postfach = 1,
    /// <summary>«Lohnzettel ist bereit» aus dem Lohnlauf.</summary>
    Lohn = 2,
    /// <summary>Vertragsversand an einen MA.</summary>
    Vertrag = 3,
    /// <summary>Geburtstag, Jubiläum und weitere Moments.</summary>
    Moment = 4,
    /// <summary>Ablauf Aufenthaltsbewilligung.</summary>
    Bewilligung = 5,
    /// <summary>Massenversand aus der Mitarbeiter-Korrespondenz.</summary>
    GruppenMail = 6,
    /// <summary>Bewerber: Absage, Willkommen.</summary>
    Kandidat = 7,
    /// <summary>Externe Dritte: Arztpraxis, Behörden.</summary>
    Dritte = 8,
}

/// <summary>Stammdaten der Kategorien — Reihenfolge und Texte fürs UI.</summary>
public record VersandKategorieInfo(
    VersandKategorie Kategorie,
    string Code,
    string Bezeichnung,
    string Beschreibung,
    string Empfaenger,
    bool NutztMail,
    bool NutztSms,
    bool StandardScharf);

public static class VersandKategorien
{
    /// <summary>
    /// Reihenfolge = Anzeige-Reihenfolge in der Systemsteuerung.
    /// StandardScharf bildet den Stand vor dem Umbau ab: nur interne
    /// Benutzer-Mails liefen scharf, alles andere ging über die Umleitung.
    /// </summary>
    public static readonly IReadOnlyList<VersandKategorieInfo> All = new List<VersandKategorieInfo>
    {
        new(VersandKategorie.Intern,      "INTERN",       "Interne Benutzer-Mails",
            "Dokument-Benachrichtigung, Mirus-Digest, Digest-Vorschau", "OneCrew-Benutzer", true,  false, true),
        new(VersandKategorie.Postfach,    "POSTFACH",     "Postfach-Einladung",
            "Zugangslink zum persönlichen Postfach", "einzelne MA", true,  true,  false),
        new(VersandKategorie.Lohn,        "LOHN",         "Lohnzettel-Benachrichtigung",
            "«Dein Lohnzettel ist bereit» aus dem Lohnlauf", "alle MA eines Laufs", true,  false, false),
        new(VersandKategorie.Vertrag,     "VERTRAG",      "Vertragsversand",
            "Vertrag als Link an den MA", "einzelne MA", false, true,  false),
        new(VersandKategorie.Moment,      "MOMENT",       "Moments",
            "Geburtstag, Jubiläum und weitere Anlässe", "einzelne MA", false, true,  false),
        // Walter 03.09.2026: neben der SMS auch die E-Mail «neue Bewilligung
        // nachreichen» (inkl. Kopie an HR/GF) — Mail-Kanal freigeschaltet.
        new(VersandKategorie.Bewilligung, "BEWILLIGUNG",  "Bewilligung",
            "Hinweis auf abgelaufene Aufenthaltsbewilligung (SMS + E-Mail, Kopie an HR/GF)", "einzelne MA", true, true,  false),
        new(VersandKategorie.GruppenMail, "GRUPPEN_MAIL", "Gruppen-E-Mail",
            "Massenversand aus der Mitarbeiter-Korrespondenz", "alle MA einer Selektion", true,  false, false),
        new(VersandKategorie.Kandidat,    "KANDIDAT",     "Bewerber",
            "Absage, Willkommenstag", "Bewerber", true,  true,  false),
        new(VersandKategorie.Dritte,      "DRITTE",       "Externe Dritte",
            "Arztbrief an die Praxis, Behörden", "externe Dritte", true,  false, false),
    };

    private static readonly Dictionary<VersandKategorie, VersandKategorieInfo> ByEnum =
        All.ToDictionary(x => x.Kategorie);

    public static VersandKategorieInfo Info(VersandKategorie k) => ByEnum[k];

    /// <summary>DB-/Log-Code der Kategorie (z.B. «GRUPPEN_MAIL»).</summary>
    public static string Code(VersandKategorie k) => ByEnum[k].Code;

    /// <summary>Code → Kategorie; null wenn unbekannt (z.B. alter Log-Eintrag).</summary>
    public static VersandKategorie? FromCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var c = code.Trim().ToUpperInvariant();
        foreach (var i in All) if (i.Code == c) return i.Kategorie;
        return null;
    }
}
