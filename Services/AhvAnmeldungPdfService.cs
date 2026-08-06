using iText.Forms;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;

namespace HrSystem.Services;

/// <summary>
/// Füllt das amtliche AHV/IV-Formular 318.260 «Anmeldung für einen
/// Versicherungsausweis» (Anmeldung zur Erlangung einer AHV-Nummer, z.B. bei
/// Zuzug aus dem Ausland). Walter-Vorgabe 06.08.2026.
///
/// Gleiche Mechanik wie NachtEignungPdfService: Template in Assets/Forms/,
/// iText-Koordinaten-Overlay (das abgelegte 318.260-PDF ist eine geflachte
/// Version des Online-Formulars — KEINE AcroForm-Felder). Koordinaten wurden
/// aus dem Template vermessen (pdfplumber) und visuell verifiziert.
/// Alle Werte kommen als bereits editierter DTO aus dem Frontend — der HR-User
/// sieht die Vorbefüllung und kann alles überschreiben (Eltern-Namen kennt das
/// System nicht, die trägt Walter im Formular-UI ein).
/// </summary>
public class AhvAnmeldungPdfService
{
    private readonly IWebHostEnvironment _env;
    public AhvAnmeldungPdfService(IWebHostEnvironment env) => _env = env;

    private const string TemplateFile = "AhvAnmeldung_318_260.pdf";
    private const float H = 842f; // A4 hoch; gemessene Koordinaten sind top-basiert

    public record AhvAnmeldungData(
        // Seite 1 — Personalien
        string? Wohnsitzland,          // «Schweiz»
        bool    Grenzgaenger,          // ja/nein
        string? Name,                  // 1.1
        string? Ledigname,             // 1.2
        string? Vornamen,              // 1.3 (Rufname in GROSSBUCHSTABEN)
        string? Geburtsdatum,          // 1.4 dd.MM.yyyy
        string? AhvNummer,             // 1.5 nur die Stellen NACH «756» (meist leer)
        string? Geschlecht,            // «M» | «F» | null
        string? Strasse,               // 1.7
        string? HausNr,
        string? Plz,
        string? Ort,
        string? Telefon,
        string? Email,
        string? Staatsangehoerigkeit,  // 1.8
        string? Geburtsort,            // 1.9 «Ort / Staat»
        string? MutterName,            // 2.1
        string? MutterVornamen,        // 2.2
        // Seite 2
        string? VaterName,             // 2.3
        string? VaterVornamen,         // 2.4
        string? Grund,                 // GRENZGAENGER | ZUZUG | AENDERUNG | ANDERE
        string? GrundText,             // «Bitte ergänzen» bei ANDERE
        string? Firmenname,            // 4.
        string? Abrechnungsnummer,
        string? FirmaStrasse,
        string? FirmaHausNr,
        string? FirmaPlz,
        string? FirmaOrt,
        string? Stellenantritt,        // dd.MM.yyyy
        bool    BeilageAusweiskopie    // Beilagen-Checkbox
    );

    private string TemplatePath() =>
        System.IO.Path.Combine(_env.ContentRootPath, "Assets", "Forms", TemplateFile);

    public byte[] Generate(AhvAnmeldungData d)
    {
        var templatePath = TemplatePath();
        if (!System.IO.File.Exists(templatePath))
            throw new FileNotFoundException(
                $"Template fehlt: {templatePath}. Bitte «AhvAnmeldung_318_260.pdf» in Assets/Forms/ ablegen.");

        using var reader = new PdfReader(templatePath);
        using var ms = new MemoryStream();
        using var writer = new PdfWriter(ms);
        using (var pdf = new PdfDocument(reader, writer))
        {
            var form = PdfAcroForm.GetAcroForm(pdf, false);
            if (form != null && form.GetAllFormFields().Count > 0)
            {
                try { form.FlattenFields(); } catch { /* kein Showstopper */ }
            }

            var font     = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            var fontBold = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_BOLD);

            // ── Seite 1 ────────────────────────────────────────────────
            var c1 = new PdfCanvas(pdf.GetPage(1));
            void T1(string? s, float x, float topY, float size = 10f)
                => Stamp(c1, font, s, x, topY, size);
            void X1(float x, float topY)
                => Stamp(c1, fontBold, "X", x, topY, 10f);

            // Werte-Grundlinien: Label-Top + ~24.5 → vertikal mittig in der Box
            // (Walter-Feedback 06.08.2026: vorher klebten die Werte am oberen Rand).
            T1(d.Wohnsitzland, 40, 160.5f);
            // Grenzgänger: Kreis vor «ja» (x0 48.6) bzw. «nein» (x0 69.0)
            if (d.Grenzgaenger) X1(40.0f, 190.5f); else X1(60.5f, 190.5f);
            T1(d.Name,         40, 231.5f);
            T1(d.Ledigname,    40, 266.5f);
            T1(d.Vornamen,     40, 301.5f);
            T1(d.Geburtsdatum, 40, 354.5f);
            T1(d.AhvNummer,   330, 384);   // Grundlinie des vorgedruckten «756»
            // Geschlecht: Kreis vor «männlich» (x0 48.6) bzw. «weiblich» (x0 100.2)
            var g = (d.Geschlecht ?? "").Trim().ToUpperInvariant();
            if (g is "M") X1(40.0f, 447.0f);
            else if (g is "F" or "W") X1(91.5f, 447.0f);
            T1(d.Strasse,       40, 504.5f);
            T1(d.HausNr,       432, 504.5f);
            T1(d.Plz,           40, 539.5f);
            T1(d.Ort,          170, 539.5f);
            T1(d.Telefon,       40, 574.5f);
            T1(d.Email,        301, 574.5f);
            T1(d.Staatsangehoerigkeit, 40, 614.5f);
            T1(d.Geburtsort,   316, 614.5f);
            T1(d.MutterName,    40, 699.5f);
            T1(d.MutterVornamen, 40, 746.5f);

            // ── Seite 2 ────────────────────────────────────────────────
            var c2 = new PdfCanvas(pdf.GetPage(2));
            void T2(string? s, float x, float topY, float size = 10f)
                => Stamp(c2, font, s, x, topY, size);
            void X2(float x, float topY)
                => Stamp(c2, fontBold, "X", x, topY, 9f);

            T2(d.VaterName,     40, 69.5f);
            T2(d.VaterVornamen, 40, 116.5f);
            // Grund-Kreise: Texte bei x0=60.7, Kreis davor (Spalte x≈42.8)
            var grundTop = (d.Grund ?? "").Trim().ToUpperInvariant() switch
            {
                "GRENZGAENGER" => 193.0f,
                "ZUZUG"        => 209.8f,
                "AENDERUNG"    => 226.6f,
                "ANDERE"       => 243.5f,
                _              => 0f,
            };
            if (grundTop > 0) X2(42.8f, grundTop);
            T2(d.GrundText,     40, 275.5f);
            T2(d.Firmenname,    40, 356.5f);
            T2(d.Abrechnungsnummer, 388, 356.5f);
            T2(d.FirmaStrasse,  40, 390.5f);
            T2(d.FirmaHausNr,  432, 390.5f);
            T2(d.FirmaPlz,      40, 425.5f);
            T2(d.FirmaOrt,     170, 425.5f);
            T2(d.Stellenantritt, 40, 464.5f);
            if (d.BeilageAusweiskopie) X2(42.8f, 546.5f);
        }
        return ms.ToArray();
    }

    /// <summary>Text auf top-basierter Y-Koordinate stempeln (A4, 842 pt hoch).</summary>
    private static void Stamp(PdfCanvas canvas, PdfFont font, string? s, float x, float topY, float size)
    {
        if (string.IsNullOrWhiteSpace(s)) return;
        canvas.SaveState();
        canvas.SetFillColor(ColorConstants.BLACK)
              .BeginText()
              .SetFontAndSize(font, size)
              .MoveText(x, H - topY)
              .ShowText(s)
              .EndText();
        canvas.RestoreState();
    }
}
