using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

namespace HrSystem.Services;

/// <summary>
/// Risikobeurteilung Mutterschutz «Für den Arzt» (Walter-Vorgabe 16.07.2026):
/// das offizielle 7-seitige McDonald's-Betriebsgruppenlösungs-PDF wird auf
/// Seite 1 mit den Filial-/MA-Angaben ergänzt (Overlay-Stamping — das PDF hat
/// KEIN AcroForm): Name, Adresse, PLZ/Ort, Kontaktperson + Filial-Telefon
/// und der Kurzbeschrieb des Betriebs. Beilage zum «Brief an den
/// behandelnden Arzt». Seiten 2-7 (Tätigkeits-Beurteilungen) bleiben
/// unverändert; die ja/nein-Kästchen werden im Gespräch von Hand angekreuzt.
/// </summary>
public class RisikobeurteilungPdfService
{
    private static readonly string TemplatePath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Forms", "Risikobeurteilung_Mutterschutz_Arzt.pdf");

    /// <summary>
    /// Kurzbeschrieb des Betriebs (Walters Text, sprachlich gestrafft) —
    /// gilt für alle McDonald's-Filialen der Gruppe.
    /// </summary>
    public const string BetriebsBeschrieb =
        "Systemgastronomiebetrieb (McDonald's-Restaurant) mit Produktion und Verkauf von Speisen und Getränken. "
        + "Die Mitarbeitenden werden je nach Funktion in Küche und Produktion, im Gästebereich, an der Kasse und im "
        + "McDrive sowie in der Warenbewirtschaftung und Reinigung eingesetzt. Gearbeitet wird im Schichtbetrieb, "
        + "überwiegend stehend, mit zeitweise erhöhter Arbeitsintensität während der Stosszeiten.";

    public record BetriebsAngaben(
        string? Name,           // Firma · Filiale
        string? Strasse,        // Strasse Nr.
        string? PlzOrt,
        string? Kontaktperson,  // Unterschriftsberechtigte der Filiale
        string? Telefon);       // Filial-Telefon (nie private Nummer)

    public byte[] Generate(BetriebsAngaben b)
    {
        using var ms = new MemoryStream();
        using (var pdf = new PdfDocument(new PdfReader(TemplatePath), new PdfWriter(ms)))
        {
            var page = pdf.GetPage(1);
            var canvas = new PdfCanvas(page);
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            float pageH = page.GetPageSize().GetHeight();   // 840

            // Koordinaten aus der Vorlage vermessen (pdfplumber, top-basiert):
            // Name 62/547 · Adresse 62/563.6 · PLZ 312/563.6 ·
            // Kontaktperson 62/580.4 · Tel. 312/580.4 · Kurzbeschrieb 57/620.
            void Text(string? t, float x, float topY, float size = 9f)
            {
                if (string.IsNullOrWhiteSpace(t)) return;
                canvas.BeginText()
                      .SetFontAndSize(font, size)
                      .SetColor(ColorConstants.BLACK, true)
                      .MoveText(x, pageH - topY - 8f)
                      .ShowText(t)
                      .EndText();
            }

            Text(b.Name,          150, 547f);
            Text(b.Strasse,       150, 563.6f);
            Text(b.PlzOrt,        360, 563.6f);
            Text(b.Kontaktperson, 150, 580.4f);
            Text(b.Telefon,       360, 580.4f);

            // Kurzbeschrieb: rechts neben dem Label, umbrochen (max ~290pt breit).
            var lines = Wrap(BetriebsBeschrieb, font, 8.5f, 300f);
            float y = 616f;
            foreach (var line in lines)
            {
                Text(line, 250, y, 8.5f);
                y += 10.5f;
            }
        }
        return ms.ToArray();
    }

    private static List<string> Wrap(string text, PdfFont font, float size, float maxWidth)
    {
        var words = text.Split(' ');
        var lines = new List<string>();
        var cur = "";
        foreach (var w in words)
        {
            var probe = cur.Length == 0 ? w : cur + " " + w;
            if (font.GetWidth(probe, size) > maxWidth && cur.Length > 0)
            {
                lines.Add(cur);
                cur = w;
            }
            else cur = probe;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }
}
