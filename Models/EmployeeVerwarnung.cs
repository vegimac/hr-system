namespace HrSystem.Models;

/// <summary>
/// Verwarnung wegen Fehlverhaltens (Walter-Vorgabe 14.07.2026). Bildet den
/// Eskalations-Verlauf ab, auf den sich eine spätere Kündigung stützen kann.
/// Pro Verwarnung MUSS ein Dokument hinterlegt sein (unterschriebenes
/// Verwarnungsschreiben). Verwarnungen werden NIE gelöscht, sondern nur
/// storniert (Storno-Flag + Grund) — der Verlauf bleibt lückenlos.
/// </summary>
public class EmployeeVerwarnung
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    /// <summary>Datum der Verwarnung (Aussprache/Übergabe).</summary>
    public DateOnly Datum { get; set; }

    /// <summary>Eskalationsstufe: VERWARNUNG_1 | VERWARNUNG_2 | LETZTE
    /// (= letzte Verwarnung mit Kündigungsandrohung).</summary>
    public string Stufe { get; set; } = "VERWARNUNG_1";

    /// <summary>Angekreuzte Gründe aus dem Verwarnungs-Formular (Mehrfach-
    /// auswahl, newline-getrennt) — z.B. «Hygienevorschrift missachtet»,
    /// «Unentschuldigt zu spät erschienen», «Diebstahl».</summary>
    public string? Gruende { get; set; }

    /// <summary>Freitext: was ist vorgefallen, was wird erwartet.</summary>
    public string? Beschreibung { get; set; }

    /// <summary>Pflicht-Verweis auf das hinterlegte Verwarnungsschreiben.</summary>
    public int? DokumentId { get; set; }
    public EmployeeDokument? Dokument { get; set; }

    /// <summary>Storno statt Löschen (nur admin/superuser, mit Grund).</summary>
    public bool Storniert { get; set; }
    public string? StornoGrund { get; set; }

    public string? ErstelltVon { get; set; }
    public DateTime ErstelltAm { get; set; } = DateTime.Now;
    public DateTime? GeaendertAm { get; set; }
}
