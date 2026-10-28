namespace HrSystem.Models;

/// <summary>
/// Höchste abgeschlossene Ausbildung für die BFS-Statistik (Swissdec 12.3.1).
/// Die Liste ist bewusst eine Tabelle: Bezeichnung und XML-Wert lassen sich
/// anpassen, ohne die Lohnmeldung umzubauen. Die gastronomische Stufe
/// (education_level, Ia/Ib/II) ist eine andere Liste und bleibt dort.
/// </summary>
public class StatistikAusbildung
{
    public int Id { get; set; }

    /// <summary>XML-Wert, z. B. universityMaster. Muss zum Swissdec-Schema passen.</summary>
    public string Code { get; set; } = "";

    /// <summary>Text in der Erfassungsmaske.</summary>
    public string Bezeichnung { get; set; } = "";

    /// <summary>Hilfetext aus den Richtlinien, unter der Auswahl.</summary>
    public string? Beschreibung { get; set; }

    public int Sortierung { get; set; }

    /// <summary>Brücke zur LSE-Spalte education (1–8). Leer = keine Entsprechung.</summary>
    public int? LseAusbildung { get; set; }

    /// <summary>Brücke zur LSE-Spalte university_degree (1 Doktorat, 2 Master, 3 Bachelor).</summary>
    public int? LseTitel { get; set; }

    public bool Aktiv { get; set; } = true;
}

/// <summary>
/// Berufliche Stellung für die BFS-Statistik (Swissdec 12.3.2).
/// Fünf Zeilen, Maskentext änderbar, XML-Wert in <see cref="Code"/>.
/// </summary>
public class StatistikStellung
{
    public int Id { get; set; }

    /// <summary>XML-Wert, z. B. highestCadre.</summary>
    public string Code { get; set; } = "";

    public string Bezeichnung { get; set; } = "";

    public string? Beschreibung { get; set; }

    public int Sortierung { get; set; }

    /// <summary>BFS-Code 1–5, gleiche Skala wie lse_code_mapping STELLUNG.</summary>
    public int? LseCode { get; set; }

    public bool Aktiv { get; set; } = true;
}
