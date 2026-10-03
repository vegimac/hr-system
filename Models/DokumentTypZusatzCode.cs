namespace HrSystem.Models;

/// <summary>
/// Weitere Ablage-Angabe für einen Dokument-Typ (Walter 03.10.2026): mehrere
/// Angaben dürfen auf denselben Typ zeigen (z.B. Zivilstand UND Geburtsurkunde
/// → «Familienbuch»), eine Angabe aber nie auf mehrere Typen. Die erste Angabe
/// bleibt in <see cref="DokumentTyp.LinkedFieldCode"/>; diese Tabelle gilt nur
/// für die Ablage nach Angabe (<c>DokumentAblageService</c>).
/// </summary>
public class DokumentTypZusatzCode
{
    public int Id { get; set; }
    public int DokumentTypId { get; set; }
    public string Code { get; set; } = "";
}
