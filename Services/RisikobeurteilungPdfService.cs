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
        string? Telefon,        // Filial-Telefon (nie private Nummer)
        // Letzte Seite «wurde mit der schwangeren MA durchgegangen»
        // (Walter 16.07.2026): MA + verantwortliche Person ausfuellen.
        string? MaVorname = null,
        string? MaName = null,
        string? MaFunktion = null,
        DateTime? MaGeburtsdatum = null,
        string? VerantwortlichVorname = null,
        string? VerantwortlichName = null,
        string? VerantwortlichFunktion = null);

    public byte[] Generate(BetriebsAngaben b)
    {
        using var ms = new MemoryStream();
        using (var pdf = new PdfDocument(new PdfReader(TemplatePath), new PdfWriter(ms)))
        {
            var page = pdf.GetPage(1);
            var canvas = new PdfCanvas(page);
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var bold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);
            float pageH = page.GetPageSize().GetHeight();   // 840

            // Koordinaten aus der Vorlage vermessen (pdfplumber, top-basiert):
            // Name 62/547 · Adresse 62/563.6 · PLZ 312/563.6 ·
            // Kontaktperson 62/580.4 · Tel. 312/580.4 · Kurzbeschrieb 57/620.
            void Text(string? t, float x, float topY, float size = 9f, bool fett = false)
            {
                if (string.IsNullOrWhiteSpace(t)) return;
                canvas.BeginText()
                      .SetFontAndSize(fett ? bold : font, size)
                      .SetColor(ColorConstants.BLACK, true)
                      .MoveText(x, pageH - topY - 8f)
                      .ShowText(t)
                      .EndText();
            }

            // Ausfuell-Werte FETT + groesser (Walter-Feedback 16.07.2026).
            Text(b.Name,          150, 546f, 10.5f, true);
            Text(b.Strasse,       150, 562.6f, 10.5f, true);
            Text(b.PlzOrt,        360, 562.6f, 10.5f, true);
            Text(b.Kontaktperson, 150, 579.4f, 10.5f, true);
            Text(b.Telefon,       360, 579.4f, 10.5f, true);

            // Kurzbeschrieb: IN der grossen Box (Rahmen x 57-539, top 639.5-757.4
            // — vermessen; Walter-Feedback 16.07.2026: Text sass zu hoch und
            // lief rechts ueber den Rahmen).
            var lines = Wrap(BetriebsBeschrieb, font, 9f, 455f);
            float y = 648f;
            foreach (var line in lines)
            {
                Text(line, 66, y, 9f);
                y += 11.5f;
            }

            // ── Letzte Seite: «Diese Risikobeurteilung wurde mit der
            // schwangeren Mitarbeiterin durchgegangen …» — MA + verantwortliche
            // Person ausfuellen (Unterschriften bleiben handschriftlich).
            // Labels vermessen (Querformat 840x595.8, top-basiert):
            // links  Name 56.6/288.9 · Vorname 236/288.9 · Funktion 56.6/312.1 ·
            //        Geburtsdatum 56.6/335.3 · Datum 56.6/358.5
            // rechts Name 415.6/288.9 · Vorname 595/288.9 · Funktion 415.6/312.1 ·
            //        Telefonnummer 415.6/335.3
            var lastPage = pdf.GetPage(pdf.GetNumberOfPages());
            var lastCanvas = new PdfCanvas(lastPage);
            float lastH = lastPage.GetPageSize().GetHeight();
            // Ausfuell-Werte FETT + groesser (Walter-Feedback 16.07.2026:
            // «diese schrift zum ausfuellen groesser und fett»).
            void TextL(string? t, float x, float topY, float size = 12f)
            {
                if (string.IsNullOrWhiteSpace(t)) return;
                lastCanvas.BeginText()
                          .SetFontAndSize(bold, size)
                          .SetColor(ColorConstants.BLACK, true)
                          .MoveText(x, lastH - topY - 11f)
                          .ShowText(t)
                          .EndText();
            }
            // Abstand zum Label vergroessert (Walter 16.07.2026: «Eintraege
            // schoener verteilen» — Werte klebten am Doppelpunkt).
            TextL(b.MaName,      130, 288.9f);
            TextL(b.MaVorname,   310, 288.9f);
            TextL(b.MaFunktion,  135, 312.1f);
            TextL(b.MaGeburtsdatum.HasValue ? b.MaGeburtsdatum.Value.ToString("dd.MM.yyyy") : null, 165, 335.3f);
            TextL(DateTime.Today.ToString("dd.MM.yyyy"), 125, 358.5f);
            TextL(b.VerantwortlichName,     490, 288.9f);
            TextL(b.VerantwortlichVorname,  670, 288.9f);
            TextL(b.VerantwortlichFunktion, 490, 312.1f);
            TextL(b.Telefon,                530, 335.3f);
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
