using iText.Forms;
using iText.Forms.Fields;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

namespace HrSystem.Services;

/// <summary>
/// Füllt das amtliche SECO-Formular „Ärztliches Zeugnis für die Eignung für
/// Schicht- und Nachtarbeit" (Version Dezember 2025) für die Abgabe an einen
/// Mitarbeiter vor. Wir füllen NUR die beiden Felder, die der Arbeitgeber
/// ausfüllt: den Betrieb (Filiale/CompanyProfile) und die untersuchte Person
/// (Name, Vorname, Geburtsdatum, Adresse). Alles Übrige (Arzt-Box, Entscheid-
/// Ankreuzfelder, Ort/Datum, Unterschrift) bleibt leer — das füllt die Ärztin
/// oder der Arzt.
///
/// Walter-Vorgabe 20.06.2026: gleiche Mechanik wie QstAnmeldungPdfService —
/// Template in Assets/Forms/, iText. Hier per Koordinaten-Overlay auf Seite 1
/// (das SECO-Formular hat keine zuverlässig benannten AcroForm-Felder), was
/// unabhängig davon funktioniert, ob das PDF flach oder fillbar ist. Eine
/// vorhandene AcroForm wird vor dem Stempeln geflättet, damit keine leeren
/// Formularfelder über dem gestempelten Text liegen.
/// </summary>
public class NachtEignungPdfService
{
    private readonly IWebHostEnvironment _env;
    public NachtEignungPdfService(IWebHostEnvironment env) => _env = env;

    private const string TemplateFile = "Nachtarbeit_Eignungsentscheid.pdf";

    public record NachtEignungData(
        // Betrieb (Filiale)
        string? BetriebName,
        string? BetriebStrasse,     // Strasse + Hausnummer kombiniert
        string? BetriebPlzOrt,      // "4800 Zofingen"
        string? BetriebTelefon,
        // Untersuchte Person
        string  Nachname,
        string  Vorname,
        string? Geburtsdatum,       // bereits formatiert dd.MM.yyyy
        string? PersonStrasse,      // Strasse + Hausnummer
        string? PersonPlzOrt        // "4800 Zofingen"
    );

    private string TemplatePath() =>
        System.IO.Path.Combine(_env.ContentRootPath, "Assets", "Forms", TemplateFile);

    /// <summary>Erzeugt das vorausgefüllte PDF (alle Seiten des Templates bleiben erhalten).</summary>
    public byte[] Generate(NachtEignungData d)
    {
        var templatePath = TemplatePath();
        if (!System.IO.File.Exists(templatePath))
            throw new FileNotFoundException(
                $"Template fehlt: {templatePath}. Bitte 'Nachtarbeit_Eignungsentscheid.pdf' in Assets/Forms/ ablegen.");

        using var reader = new PdfReader(templatePath);
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using (var pdf = new PdfDocument(reader, writer))
        {
            // Eventuelle AcroForm flätten (verhindert leere Felder über dem Text).
            var form = PdfAcroForm.GetAcroForm(pdf, false);
            if (form != null && form.GetAllFormFields().Count > 0)
            {
                try { form.FlattenFields(); } catch { /* kein Showstopper */ }
            }

            var page = pdf.GetPage(1);
            var canvas = new PdfCanvas(page);
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);

            void Text(string? s, float x, float y, float size = 11.5f)
            {
                if (string.IsNullOrWhiteSpace(s)) return;
                canvas.SaveState();
                canvas.SetFillColor(ColorConstants.BLACK)
                      .BeginText()
                      .SetFontAndSize(font, size)
                      .MoveText(x, y)
                      .ShowText(s)
                      .EndText();
                canvas.RestoreState();
            }

            // ── Betrieb-Box (rechts oben). Koordinaten in pt von unten-links;
            //    A4 = 595.32 × 841.92. Iterativ justierbar. ──
            float bx = 316f;
            Text(d.BetriebName,    bx, 594f);
            Text(d.BetriebStrasse, bx, 580f);
            Text(d.BetriebPlzOrt,  bx, 566f);
            Text(d.BetriebTelefon, bx, 552f);

            // ── Untersuchte Person ──
            // Zeile 1: Name | Vorname | Geburtsdatum — auf der Label-Grundlinie.
            Text(d.Nachname,     101f, 500f);
            Text(d.Vorname,      279f, 500f);
            Text(d.Geburtsdatum, 464f, 500f);
            // Zeile 2: Adresse (Strasse, dann PLZ/Ort) — auf der Adresse-Grundlinie.
            var adresse = string.Join(", ",
                new[] { d.PersonStrasse, d.PersonPlzOrt }
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
            Text(adresse, 113f, 483f);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// Diagnose: listet alle AcroForm-Feldnamen des Templates auf. Wird vom
    /// Controller über ?debug=fields ausgegeben, damit bei Bedarf auf präzises
    /// Feld-Mapping statt Koordinaten-Overlay umgestellt werden kann.
    /// </summary>
    public List<string> ListTemplateFields()
    {
        var templatePath = TemplatePath();
        if (!System.IO.File.Exists(templatePath)) return new List<string> { "(Template fehlt)" };
        using var reader = new PdfReader(templatePath);
        using var pdf = new PdfDocument(reader);
        var form = PdfAcroForm.GetAcroForm(pdf, false);
        if (form == null) return new List<string>();
        return form.GetAllFormFields().Keys.ToList();
    }
}
