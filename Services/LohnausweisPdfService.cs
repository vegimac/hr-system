using HrSystem.Models;
using iText.Barcodes;
using iText.Forms;
using iText.Forms.Fields;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Pdf.Xobject;

namespace HrSystem.Services;

/// <summary>
/// Generiert den amtlichen ESTV-Lohnausweis (Form 11 dfe — deutsch/français/italiano).
/// Phase 1: jährliche Aggregation der PayrollSnapshot-Daten in das AcroForm-Template
/// "Assets/Forms/Lohnausweis_Form11_DFE.pdf".
///
/// Feld-Mapping (44 AcroForm-Felder):
///   • Empfänger-Block (oben):   TextMehrzeiligLinks_Empfaenger
///   • AHV-Nr (Ziffer C):        AHVLinks_C  + TextLinks_C-GebDatum
///   • Adresse (Ziffer D):       TextLinks_D
///   • Periode (Ziffer E):       TextLinks_E-von / TextLinks_E-bis
///   • Heimatort (Ziffer I):     TextLinks_I
///   • Checkboxen:               OptionKreuzOhneRahmen_A / _B / _F / _G / _13_1_1
///   • Lohnpositionen 1..15:     DezZahlNull_X + TextLinks_X-Art
///   • Bestätigung AG (unten):   TextMehrzeiligLinks_Bestaetigung
///
/// Walter (12.05.2026):
///   • Form 11 dfe Version 01.21 (Januar 2021), trilingual deutsch/français/italiano.
///   • Bei McDonald's Schaub: Box F = false (kein Werks-Bus),
///     Box G = false (Crew bezahlt 50%-Anteil → keine unentgeltliche Verpflegung).
///   • Ziffer 2.1 (Verpflegung/Unterkunft): standardmäßig 0 wegen 50%-Anteil.
/// </summary>
public class LohnausweisPdfService
{
    private readonly IWebHostEnvironment _env;
    private readonly LohnausweisBarcodeService _barcode;

    public LohnausweisPdfService(IWebHostEnvironment env, LohnausweisBarcodeService barcode)
    {
        _env = env;
        _barcode = barcode;
    }

    /// <summary>
    /// Generiert das ausgefüllte Lohnausweis-PDF.
    /// </summary>
    /// <param name="d">DTO mit allen Feld-Werten.</param>
    /// <param name="signaturePng">Optional: PNG/JPG-Bytes der Unterschrift des
    /// eingeloggten Users. Wird im Bestätigungs-Block unten platziert; darunter
    /// wird der Klarname als gedruckte Zeile gerendert. Null = Stelle bleibt leer.</param>
    /// <param name="signerName">Klarname des Unterzeichners — wird unter dem Bild gedruckt.</param>
    /// <param name="companyUid">UID des Arbeitgebers (CHE-XXX.XXX.XXX) für den Barcode.</param>
    public byte[] Generate(
        LohnausweisData d,
        byte[]? signaturePng = null,
        string? signerName = null,
        string? companyUid = null)
    {
        var templatePath = System.IO.Path.Combine(
            _env.ContentRootPath, "Assets", "Forms", "Lohnausweis_Form11_DFE.pdf");

        if (!File.Exists(templatePath))
            throw new FileNotFoundException(
                $"Lohnausweis-Template nicht gefunden: {templatePath}");

        using var ms     = new MemoryStream();
        using var reader = new PdfReader(templatePath);
        using var writer = new PdfWriter(ms);
        using var pdf    = new PdfDocument(reader, writer);

        var form = PdfAcroForm.GetAcroForm(pdf, false);
        // KEIN SetNeedAppearances(true) mehr (Walter-Befund 16.08.2026):
        // NeedAppearances delegiert das Zeichnen der Feldwerte an den
        // PDF-Viewer — Adobe Acrobat rendert das, einfache/unabhaengige
        // Renderer zeigen dann LEERE Textfelder. Ohne das Flag erzeugt
        // iText beim SetValue eigene gueltige Appearance-Streams (/AP).

        Map(form, d);

        // Unterschrift im AG-Bestätigungs-Block einbetten
        if (signaturePng != null && signaturePng.Length > 0)
            EmbedSignature(pdf, form, signaturePng, signerName ?? "");

        // FLATTEN (Walter-Vorgabe 16.08.2026): alle Feldwerte + Checkboxen
        // werden fester Bestandteil des Seiteninhalts — identische Anzeige
        // in jedem Viewer und beim Druck, keine Formularfelder mehr im
        // ausgegebenen Lohnausweis. (Bearbeitet wird ohnehin nur im
        // OneCrew-Vorschau-Modal, nie im PDF.) MUSS nach Map/EmbedSignature
        // und vor EmbedBarcode/Close laufen.
        form.FlattenFields();

        // ESTV-Barcode (PDF417) oben rechts (Position H) einbetten
        EmbedBarcode(pdf, d, companyUid);

        pdf.Close();
        return ms.ToArray();
    }

    // ════════════════════════════════════════════════════════════════════
    // BARCODE-EMBEDDING (PDF417, Position H oben rechts)
    // ════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Zeichnet den ESTV-Lohnausweis-Barcode (PDF417) oben rechts auf
    /// Position H. Inhalt ist der Swissdec-XML-Payload aus dem
    /// LohnausweisBarcodeService (Pre-Header + ZIP + XML, byte[]).
    ///
    /// Position auf der Form 11 dfe (A4-Seite, Punkte von unten links gemessen):
    ///   • H-Bereich: x ≈ 220–545, y ≈ 620–720 (oberhalb der Empfänger-Adresse,
    ///     unterhalb der Ziffer-C/D/E-Felder).
    /// </summary>
    private void EmbedBarcode(PdfDocument pdf, LohnausweisData d, string? companyUid)
    {
        try
        {
            // UID nachreichen falls der Aufrufer das DTO-Feld nicht selbst gesetzt hat
            if (string.IsNullOrWhiteSpace(d.CompanyUidFormatted) && !string.IsNullOrWhiteSpace(companyUid))
                d.CompanyUidFormatted = companyUid;

            byte[] payload = _barcode.BuildPayload(d);
            if (payload == null || payload.Length == 0) return;

            var barcode = new BarcodePDF417();
            // SetCode(byte[]) aktiviert den Byte-Compaction-Modus automatisch —
            // notwendig für unsere Binärdaten (Pre-Header + ZIP).
            barcode.SetCode(payload);
            barcode.SetErrorLevel(2);              // Fehlertoleranz-Level 2 (Swissdec-Standard)
            // ── Symbol-Geometrie: FIXE 15 Spalten (Walter-Prüfung 16.08.2026) ──
            // Exakte Rechnung fuer den realen Payload (~780 B ⇒ 662 Codewords
            // inkl. SLD + EC-Level-2) im Feld H (225×90 pt), proportional
            // gefittet, Zeilenhoehe = 3×Modulbreite:
            //
            //   Spalten  Zeilen  Ratio h/w  Modulbreite   Symbolgroesse
            //      8       83      1.215      5.0 mil     26.1×31.8 mm  ← Swissdec-Soll 1.2
            //     15       45      0.417      9.26 mil    76.2×31.8 mm  ← MAXIMUM im Feld H
            //
            //   Swissdec-Soll «Ratio 1.2 UND 12 mil» braeuchte 177×215 pt —
            //   das Feld H bietet nur 225×90 pt. 12 mil sind im Feld H fuer
            //   diesen Payload GRUNDSAETZLICH unerreichbar (breiteste
            //   12-mil-Variante: 158 pt hoch; hoechste: 353 pt breit).
            //   15 fixe Spalten liefern die maximal moegliche Modulbreite
            //   (9.26 mil) — beste physisch erreichbare Scanbarkeit, und die
            //   Geometrie ist DETERMINISTISCH (unabhaengig von der iText-
            //   AspectRatio-Interpretation).
            barcode.SetOptions(BarcodePDF417.PDF417_FIXED_COLUMNS);
            barcode.SetCodeColumns(15);

            var page = pdf.GetPage(1);
            var canvas = new PdfCanvas(page);

            // Position H (rechte Seite, neben dem Empfänger-Block):
            //   Empfänger-Block: x=57–312, y=620–704 (Fenstercouvert-Adresse MA)
            //   Barcode-Zone H : x=320+, y=620–720
            // X muss ab 320 starten, damit die MA-Adresse links daneben sichtbar
            // bleibt — sonst überlappt der Barcode mit dem Adress-Feld
            // (Walter-Feedback 13.05.2026 „adresse ma deckt barcode ab").
            const float BarcodeX      = 320f;   // direkt rechts vom Empfänger-Block
            const float BarcodeY      = 620f;   // unterkant gleich wie Empfänger
            const float BarcodeWidth  = 225f;   // x=320–545, lässt rechts ~50pt Rand für H-Label
            const float BarcodeHeight = 90f;    // y=620–710

            // PROPORTIONALE Platzierung (Walter 16.08.2026): das fruehere
            // AddXObjectFittedIntoRectangle hat das Symbol non-proportional auf
            // die Zone gestreckt (Module verzerrt). Jetzt: uniform skalieren
            // (Modulproportionen bleiben exakt), in der Zone zentrieren —
            // maximale Modulbreite, beste Scanbarkeit.
            var formXObject = barcode.CreateFormXObject(ColorConstants.BLACK, pdf);
            var bboxArr = formXObject.GetPdfObject().GetAsArray(PdfName.BBox);
            float bw = bboxArr.GetAsNumber(2).FloatValue() - bboxArr.GetAsNumber(0).FloatValue();
            float bh = bboxArr.GetAsNumber(3).FloatValue() - bboxArr.GetAsNumber(1).FloatValue();
            if (bw <= 0 || bh <= 0) { bw = BarcodeWidth; bh = BarcodeHeight; }
            float scale = Math.Min(BarcodeWidth / bw, BarcodeHeight / bh);
            float px = BarcodeX + (BarcodeWidth  - bw * scale) / 2f;
            float py = BarcodeY + (BarcodeHeight - bh * scale) / 2f;
            canvas.AddXObjectWithTransformationMatrix(formXObject, scale, 0, 0, scale, px, py);
        }
        catch (InvalidOperationException)
        {
            // Pflichtfeld-Validierung (GrossIncome/NetIncome fehlt) —
            // NICHT schlucken: der Aufrufer soll die Klartext-Meldung sehen
            // statt still einen Lohnausweis ohne Barcode zu erhalten
            // (Walter-Vorgabe 16.08.2026).
            throw;
        }
        catch
        {
            // Technische Barcode-Fehler dürfen den PDF-Druck nicht blockieren.
            // Bei Fehler bleibt der Lohnausweis gültig — Steueramt müsste
            // Werte dann manuell abtippen.
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // FELD-MAPPING
    // ════════════════════════════════════════════════════════════════════════
    private static void Map(PdfAcroForm form, LohnausweisData d)
    {
        // ── Empfänger-Adresse (oben links) ───────────────────────────────
        // Hier kommt typischerweise die Mitarbeiter-Adresse rein
        // (der Lohnausweis wird IHM zugestellt). Mehrzeilig.
        SetMulti(form, "TextMehrzeiligLinks_Empfaenger", d.EmpfaengerAdresse);

        // ── Ziffer A / B — Form-Typ ─────────────────────────────────────
        // A = Lohnausweis (für Arbeitsverhältnisse)
        // B = Rentenbescheinigung (für Vorsorgeleistungen)
        // Nur EINES davon ankreuzen — wir nutzen das System nur für Lohnausweise,
        // also IMMER Box A, NIE Box B.
        if (d.IstLohnausweis) Check(form, "OptionKreuzOhneRahmen_A");
        // Box B bleibt leer (würde nur bei Rentenbescheinigung angekreuzt).

        // ── Ziffer C — AHV-Nr + Geburtsdatum ────────────────────────────
        // AHV-Nr immer im Schweizer Standard-Format 756.XXXX.XXXX.XX anzeigen,
        // egal wie sie in der DB gespeichert ist (mit oder ohne Punkte).
        Set(form, "AHVLinks_C",            FormatAhv(d.AhvNummer));
        Set(form, "TextLinks_C-GebDatum",  d.Geburtsdatum);

        // ── Ziffer D — Steuerjahr (4 Ziffern, z.B. "2026") ──────────────
        // Achtung: Feld ist nur 62 Pt breit, NICHT für MA-Namen gedacht!
        // Der MA-Name + Adresse stehen im Empfänger-Block (Fenstercouvert).
        Set(form, "TextLinks_D", d.Jahr);

        // ── Ziffer E — Anstellungs-Periode ──────────────────────────────
        Set(form, "TextLinks_E-von", d.PeriodeVon);
        Set(form, "TextLinks_E-bis", d.PeriodeBis);

        // ── Ziffer F / G — Filial-Defaults (Lohnausweis-Flags) ──────────
        if (d.BoxFFreierTransport) Check(form, "OptionKreuzOhneRahmen_F");
        if (d.BoxGKantineGratis)   Check(form, "OptionKreuzOhneRahmen_G");

        // ── Ziffer I — Ort und Datum (unten links beim Bestätigungs-Block) ──
        // Achtung: Trotz Feldname "I" ist das NICHT der Heimatort, sondern das
        // "Ort und Datum / Lieu et date / Place and date"-Feld unten links.
        // Aufgebaut aus Ziffer151Ort + Ziffer152Datum (z.B. "Sursee, 13.05.2026").
        var ortDatum = (d.Ziffer151Ort, d.Ziffer152Datum) switch
        {
            (string o, string dt) when !string.IsNullOrWhiteSpace(o) && !string.IsNullOrWhiteSpace(dt) => $"{o}, {dt}",
            (string o, _)        when !string.IsNullOrWhiteSpace(o)  => o,
            (_, string dt)       when !string.IsNullOrWhiteSpace(dt) => dt,
            _ => null
        };
        Set(form, "TextLinks_I", ortDatum);

        // ── Ziffer 1 — Lohn ──────────────────────────────────────────────
        SetDec(form, "DezZahlNull_1", d.Ziffer1Lohn);

        // ── Ziffer 2.1 — Verpflegung / Unterkunft ───────────────────────
        SetDec(form, "DezZahlNull_2_1", d.Ziffer21VerpflegungUnterkunft);

        // ── Ziffer 2.2 — Privatanteil Geschäftswagen ────────────────────
        SetDec(form, "DezZahlNull_2_2", d.Ziffer22PrivatanteilFahrzeug);

        // ── Ziffer 2.3 — Andere Gehaltsnebenleistungen ──────────────────
        SetDec(form, "DezZahlNull_2_3",      d.Ziffer23AndereGehaltsnebenleistungen);
        Set   (form, "TextLinks_2_3-Art",    d.Ziffer23Art);

        // ── Ziffer 3 — Unregelmässige Leistungen ────────────────────────
        SetDec(form, "DezZahlNull_3",   d.Ziffer3Unregelmaessige);
        Set   (form, "TextLinks_3-Art", d.Ziffer3Art);

        // ── Ziffer 4 — Kapitalleistungen ────────────────────────────────
        SetDec(form, "DezZahlNull_4",   d.Ziffer4Kapitalleistungen);
        Set   (form, "TextLinks_4-Art", d.Ziffer4Art);

        // ── Ziffer 5 — Beteiligungsrechte ───────────────────────────────
        SetDec(form, "DezZahlNull_5", d.Ziffer5Beteiligungsrechte);

        // ── Ziffer 6 — VR-Entschädigungen ───────────────────────────────
        SetDec(form, "DezZahlNull_6", d.Ziffer6VrEntschaedigung);

        // ── Ziffer 7 — Andere Leistungen ────────────────────────────────
        SetDec(form, "DezZahlNull_7",   d.Ziffer7AndereLeistungen);
        Set   (form, "TextLinks_7-Art", d.Ziffer7Art);

        // ── Ziffer 8 — Bruttoeinkommen Total ────────────────────────────
        SetDec(form, "DezZahlNull_8", d.Ziffer8BruttoTotal);

        // ── Ziffer 9 — Beiträge AHV/IV/EO/ALV/NBUV ──────────────────────
        SetDec(form, "DezZahlNull_9", d.Ziffer9AhvIvEoAlvNbu);

        // ── Ziffer 10 — BVG-Beiträge ────────────────────────────────────
        SetDec(form, "DezZahlNull_10_1", d.Ziffer101BvgOrdentlich);
        SetDec(form, "DezZahlNull_10_2", d.Ziffer102BvgEinkauf);

        // ── Ziffer 11 — Nettolohn ────────────────────────────────────────
        SetDec(form, "DezZahlNull_11", d.Ziffer11Nettolohn);

        // ── Ziffer 12 — Quellensteuer-Abzug ─────────────────────────────
        SetDec(form, "DezZahlNull_12", d.Ziffer12Quellensteuer);

        // ── Ziffer 13.1.1 — Spesen effektiv ─────────────────────────────
        if (d.Ziffer1311EffektivOhneBeleg) Check(form, "OptionKreuzOhneRahmen_13_1_1");
        SetDec(form, "DezZahlNull_13_1_1", d.Ziffer1311SpesenEffektivBetrag);

        // ── Ziffer 13.1.2 — Spesen pauschal ─────────────────────────────
        SetDec(form, "DezZahlNull_13_1_2",      d.Ziffer1312SpesenPauschal);
        Set   (form, "TextLinks_13_1_2-Art",    d.Ziffer1312Art);

        // ── Ziffer 13.2.1 — Repräsentationspauschale ────────────────────
        SetDec(form, "DezZahlNull_13_2_1", d.Ziffer1321Repraesentation);

        // ── Ziffer 13.2.2 — Auto-Pauschale ──────────────────────────────
        SetDec(form, "DezZahlNull_13_2_2", d.Ziffer1322Autopauschale);

        // ── Ziffer 13.2.3 — Andere Pauschalen ───────────────────────────
        SetDec(form, "DezZahlNull_13_2_3",      d.Ziffer1323AnderePauschalen);
        Set   (form, "TextLinks_13_2_3-Art",    d.Ziffer1323Art);

        // ── Ziffer 13.3 — Aus-/Weiterbildung ────────────────────────────
        SetDec(form, "DezZahlNull_13_3", d.Ziffer133AusWeiterbildung);

        // ── Ziffer 14 + 15 — Bemerkungen (4 Zeilen durchgehend) ─────────
        // Beide Ziffern bilden zusammen den Bemerkungs-Block am unteren
        // Lohnausweis-Rand. Reihenfolge: 14_1 → 14_2 → 15_1 → 15_2.
        Set(form, "TextLinks_14_1", d.Ziffer141Bemerkungen);
        Set(form, "TextLinks_14_2", d.Ziffer142Bemerkungen);
        Set(form, "TextLinks_15_1", d.Ziffer151Bemerkungen);
        Set(form, "TextLinks_15_2", d.Ziffer152Bemerkungen);

        // ── Bestätigung AG (Adress-Block beim Unterschriften-Stempel) ──
        // Schriftgrösse explizit verkleinert (8 pt statt Default 12 pt), damit
        // alle 6 Zeilen (UID, Firma, HR-Name, Strasse, PLZ/Ort, Telefon) ohne
        // Abschneiden ins fixe Feld passen.
        SetMultiSmall(form, "TextMehrzeiligLinks_Bestaetigung", d.BestaetigungAgBlock, 8f);
    }

    // ════════════════════════════════════════════════════════════════════════
    // SIGNATUR-EMBEDDING
    // ════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Verankert die Unterschrift am Bestätigungs-Feld (unten rechts beim AG-Block).
    /// Werte sind Erfahrungswerte und können bei optischer Abweichung getunt werden.
    /// </summary>
    private static readonly (string AnchorField, float OffsetX, float OffsetY, float Width, float Height) _sigPlacement =
        ("TextMehrzeiligLinks_Bestaetigung", 5f, -45f, 130f, 32f);

    private static void EmbedSignature(
        PdfDocument pdf, PdfAcroForm form,
        byte[] signatureBytes, string signerName)
    {
        var cfg = _sigPlacement;

        PdfFormField? anchor = null;
        try { anchor = form.GetField(cfg.AnchorField); } catch { /* fehlt → kein Embedding */ }
        if (anchor == null) return;

        var widgets = anchor.GetWidgets();
        if (widgets == null || widgets.Count == 0) return;

        var widget = widgets[0];
        var rectArr = widget.GetRectangle();
        if (rectArr == null) return;
        var anchorRect = rectArr.ToRectangle();

        // Seite des Widgets finden
        PdfPage? widgetPage = null;
        for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
        {
            var page = pdf.GetPage(i);
            foreach (var an in page.GetAnnotations())
            {
                if (an.GetPdfObject() == widget.GetPdfObject())
                {
                    widgetPage = page;
                    break;
                }
            }
            if (widgetPage != null) break;
        }
        if (widgetPage == null) widgetPage = pdf.GetPage(pdf.GetNumberOfPages());

        float x = anchorRect.GetLeft()   + cfg.OffsetX;
        float y = anchorRect.GetBottom() + cfg.OffsetY;
        var imgRect = new Rectangle(x, y, cfg.Width, cfg.Height);

        var canvas = new PdfCanvas(widgetPage);
        try
        {
            var imgData = ImageDataFactory.Create(signatureBytes);
            canvas.AddImageFittedIntoRectangle(imgData, imgRect, false);
        }
        catch { /* Bild defekt — Name trotzdem ausdrucken */ }

        if (!string.IsNullOrWhiteSpace(signerName))
        {
            try
            {
                var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                canvas.SaveState();
                canvas.SetFillColor(ColorConstants.BLACK);
                canvas.BeginText()
                      .SetFontAndSize(font, 7.5f)
                      .MoveText(x + 2, y - 9)
                      .ShowText(signerName)
                      .EndText();
                canvas.RestoreState();
            }
            catch { /* Font-Erstellung fehlgeschlagen — Text weglassen */ }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // HELPERS
    // ════════════════════════════════════════════════════════════════════════
    private static void Set(PdfAcroForm form, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = form.GetField(fieldName);
        if (field is null) return;
        field.SetValue(value);
    }

    /// <summary>
    /// Formatiert die AHV-Nummer ins Schweizer Standard-Format
    /// <c>756.XXXX.XXXX.XX</c> (3 + 4 + 4 + 2). Akzeptiert beide
    /// Eingaben — mit oder ohne Punkte, mit beliebigen Trennzeichen.
    /// Bei abweichender Länge (≠ 13 Ziffern) gibt es den Originalwert zurück.
    /// </summary>
    private static string? FormatAhv(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        // Nur Ziffern extrahieren (Punkte, Spaces, sonstige Zeichen entfernen)
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length != 13) return raw;   // unerwartete Länge → unverändert
        return $"{digits.Substring(0, 3)}.{digits.Substring(3, 4)}.{digits.Substring(7, 4)}.{digits.Substring(11, 2)}";
    }

    /// <summary>
    /// Setzt ein mehrzeiliges Textfeld. Newlines werden im PDF als Zeilenumbrüche
    /// dargestellt — iText kümmert sich darum, sofern das Feld als "Multi-line"
    /// markiert ist (TextMehrzeiligLinks_* sind das im Template).
    /// </summary>
    private static void SetMulti(PdfAcroForm form, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = form.GetField(fieldName);
        if (field is null) return;
        field.SetValue(value);
    }

    /// <summary>
    /// Wie <see cref="SetMulti"/>, aber mit explizit kleinerer Schriftgrösse,
    /// damit mehrzeilige Adress-Blöcke nicht abgeschnitten werden.
    /// </summary>
    private static void SetMultiSmall(PdfAcroForm form, string fieldName, string? value, float fontSize)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = form.GetField(fieldName);
        if (field is null) return;
        try { field.SetFontSize(fontSize); } catch { /* fallback: default */ }
        field.SetValue(value);
    }

    /// <summary>
    /// Formatiert einen Dezimalbetrag CHF mit deutschem Tausendertrennzeichen
    /// (Apostroph) und exakt 2 Nachkommastellen (Format Lohnausweis-Norm).
    /// Beispiel: 1234.5 → "1'234.50". Null oder 0 → leer (DezZahlNull-Konvention).
    /// </summary>
    private static void SetDec(PdfAcroForm form, string fieldName, decimal? value)
    {
        if (value == null || value == 0m) return;     // "DezZahlNull" → Null/0 nicht drucken
        var field = form.GetField(fieldName);
        if (field is null) return;
        var swiss = new System.Globalization.NumberFormatInfo
        {
            NumberDecimalSeparator = ".",
            NumberGroupSeparator   = "'",
            NumberGroupSizes       = new[] { 3 }
        };
        // Wegleitung zum Lohnausweis: Betraege in GANZEN Franken (Walter-
        // Vorgabe 16.08.2026, kaufmaennisch gerundet). Identische Rundung
        // wie im TxAB-Barcode (LohnausweisBarcodeService.FormatMoney) —
        // PDF und Barcode duerfen nie auseinanderlaufen.
        var gerundet = Math.Round(value.Value, 0, MidpointRounding.AwayFromZero);
        if (gerundet == 0m) return;
        field.SetValue(gerundet.ToString("N0", swiss));
    }

    /// <summary>
    /// Setzt eine einzelne Checkbox auf "an". iText probiert mehrere
    /// On-Werte ("Yes", "1", "On") — robust gegen unterschiedliche
    /// Template-Konventionen.
    /// </summary>
    private static void Check(PdfAcroForm form, string fieldName)
    {
        var field = form.GetField(fieldName);
        if (field is null) return;
        foreach (var v in new[] { "Yes", "1", "On", "Ja" })
        {
            try { field.SetValue(v); return; }
            catch { /* nächster Wert */ }
        }
    }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

public class LohnausweisData
{
    // ── Finalisierung (Walter 16.08.2026) ────────────────────────────────
    // Entwurf: DocID «Entwurf - Brouillon - Bozza», CreationDate = Druckzeit.
    // Final:   persistierte UUID + eingefrorenes CreationDate (Wiederdruck
    //          traegt dieselbe Identifikation — lohnausweis_final-Tabelle).
    public bool    IstFinal { get; set; }
    public string? DocId { get; set; }
    public string? CreationDateFinal { get; set; }   // ISO yyyy-MM-ddTHH:mm:ss

    // Empfänger (Adresse oben links — typischerweise MA-Anschrift)
    public string? EmpfaengerAdresse { get; set; }

    // Periode-Flags (oben rechts)
    public bool IstGanzesJahr  { get; set; } = true;
    public bool IstLohnausweis { get; set; } = true;

    // Ziffer C — AHV + Geburtsdatum
    public string? AhvNummer    { get; set; }
    public string? Geburtsdatum { get; set; }

    // Ziffer D — Steuerjahr (4 Ziffern, z.B. "2026")
    public string? Jahr { get; set; }

    // MA-Name + Adresse für den Empfänger-Block (Fenstercouvert).
    // NICHT für Ziffer D — die Form 11 hat dafür kein eigenes Feld.
    public string? MitarbeiterNameAdresse { get; set; }

    // Ziffer E — Anstellungs-Periode
    public string? PeriodeVon { get; set; }
    public string? PeriodeBis { get; set; }

    // Ziffer F / G — Filial-Defaults
    public bool BoxFFreierTransport { get; set; }
    public bool BoxGKantineGratis   { get; set; }

    // Ziffer I — Heimatort
    public string? Heimatort { get; set; }

    // Ziffer 1
    public decimal? Ziffer1Lohn { get; set; }

    // Ziffer 2 — Gehaltsnebenleistungen
    public decimal? Ziffer21VerpflegungUnterkunft       { get; set; }
    public decimal? Ziffer22PrivatanteilFahrzeug        { get; set; }
    public decimal? Ziffer23AndereGehaltsnebenleistungen { get; set; }
    public string?  Ziffer23Art                          { get; set; }

    // Ziffer 3-7
    public decimal? Ziffer3Unregelmaessige  { get; set; }
    public string?  Ziffer3Art              { get; set; }
    public decimal? Ziffer4Kapitalleistungen { get; set; }
    public string?  Ziffer4Art              { get; set; }
    public decimal? Ziffer5Beteiligungsrechte { get; set; }
    public decimal? Ziffer6VrEntschaedigung   { get; set; }
    public decimal? Ziffer7AndereLeistungen   { get; set; }
    public string?  Ziffer7Art                { get; set; }

    // Ziffer 8 — Bruttoeinkommen Total
    public decimal? Ziffer8BruttoTotal { get; set; }

    // Ziffer 9 — AHV/IV/EO/ALV/NBUV
    public decimal? Ziffer9AhvIvEoAlvNbu { get; set; }

    // Ziffer 10 — BVG
    public decimal? Ziffer101BvgOrdentlich { get; set; }
    public decimal? Ziffer102BvgEinkauf    { get; set; }

    // Ziffer 11 — Nettolohn
    public decimal? Ziffer11Nettolohn { get; set; }

    // Ziffer 12 — Quellensteuer
    public decimal? Ziffer12Quellensteuer { get; set; }

    // Ziffer 13 — Spesenvergütungen
    public bool     Ziffer1311EffektivOhneBeleg     { get; set; }
    public decimal? Ziffer1311SpesenEffektivBetrag  { get; set; }
    public decimal? Ziffer1312SpesenPauschal         { get; set; }
    public string?  Ziffer1312Art                    { get; set; }
    public decimal? Ziffer1321Repraesentation        { get; set; }
    public decimal? Ziffer1322Autopauschale          { get; set; }
    public decimal? Ziffer1323AnderePauschalen       { get; set; }
    public string?  Ziffer1323Art                    { get; set; }
    public decimal? Ziffer133AusWeiterbildung        { get; set; }

    // Ziffer 14 + 15 — Bemerkungen (4 Zeilen durchgehender Block)
    public string? Ziffer141Bemerkungen { get; set; }
    public string? Ziffer142Bemerkungen { get; set; }
    public string? Ziffer151Bemerkungen { get; set; }
    public string? Ziffer152Bemerkungen { get; set; }

    // Ziffer I — Ort + Datum (unten links beim Bestätigungs-Block)
    public string? Ziffer151Ort   { get; set; }
    public string? Ziffer152Datum { get; set; }

    // Bestätigungs-Block AG (Stempel + Unterschrift unten)
    public string? BestaetigungAgBlock { get; set; }

    // ════════════════════════════════════════════════════════════════════
    // Swissdec-Barcode-Felder (strukturiert, fürs Barcode-XML)
    // ════════════════════════════════════════════════════════════════════
    /// <summary>UID mit Punkten, z.B. "CHE-350.063.866".</summary>
    public string? CompanyUidFormatted   { get; set; }

    /// <summary>Firmenname (HR-RC-Name im XML, wird auf 30 Zeichen getrimmt).</summary>
    public string? CompanyName           { get; set; }

    /// <summary>Filial-/Standort-Bezeichnung (CL im XML), z.B. "Zweigniederlassung Langenthal".</summary>
    public string? BranchName            { get; set; }

    public string? CompanyStreet         { get; set; }
    public string? CompanyZip            { get; set; }
    public string? CompanyCity           { get; set; }
    public string? CompanyCountry        { get; set; } = "SWITZERLAND";
    public string? CompanyPhone          { get; set; }

    /// <summary>HR-Verantwortliche/r (Person-Attribut im &lt;Company&gt;-Tag).</summary>
    public string? HrVerantwortlicherName { get; set; }

    // Mitarbeiter-Stammdaten für PersonID-Element
    public string? MaLastname            { get; set; }
    public string? MaFirstname           { get; set; }
    public string? MaStreet              { get; set; }
    public string? MaZip                 { get; set; }
    public string? MaCity                { get; set; }
    public string? MaCountry             { get; set; } = "SWITZERLAND";
}
