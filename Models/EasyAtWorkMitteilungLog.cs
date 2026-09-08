namespace HrSystem.Models;

/// <summary>
/// Versandprotokoll easy@work-Mitteilung (Walter 08.09.2026) — ein Eintrag pro
/// Versand, analog zu <see cref="GruppenMailLog"/>. Wer was bekommen hat,
/// steht als JSON in <see cref="DetailsJson"/> (Name, Nummer, Ergebnis).
/// </summary>
public class EasyAtWorkMitteilungLog
{
    public int Id { get; set; }
    public DateTime GesendetAm { get; set; } = DateTime.Now;
    public int? GesendetVonUserId { get; set; }
    public AppUser? GesendetVonUser { get; set; }

    public string Betreff { get; set; } = "";
    public string Filiale { get; set; } = "";
    public string Modelle { get; set; } = "";
    public string Funktionen { get; set; } = "";

    /// <summary>Unterzeichner/in der Mitteilung (Name, ggf. Funktion) — leer = ohne.</summary>
    public string? Unterzeichner { get; set; }
    /// <summary>Angehängte Datei statt Text-PDF.</summary>
    public string? AnhangName { get; set; }
    public bool MitText { get; set; }
    /// <summary>true = scharf an die echten MA; false = nur an die Test-Personalnummer.</summary>
    public bool Scharf { get; set; }

    public int AnzahlGesendet { get; set; }
    public int AnzahlFehlgeschlagen { get; set; }
    public int AnzahlOhneEawId { get; set; }
    public int AnzahlUmgeleitet { get; set; }

    public string? DetailsJson { get; set; }
}
