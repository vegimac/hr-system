using System.Globalization;
using HrSystem.Models;
using iText.Forms;
using iText.Forms.Fields;
using iText.IO.Font.Constants;
using iText.IO.Image;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Annot;
using iText.Kernel.Pdf.Canvas;

namespace HrSystem.Services;

/// <summary>
/// Füllt das offizielle AcroForm-Formular "Bescheinigung über Zwischenverdienst" (ALV 716.105)
/// via iText7 PdfAcroForm – kein Koordinaten-Overlay, sondern direkte Feldbefüllung.
///
/// Feld-Mapping (ermittelt durch Analyse der AP/N-Schlüssel und Seitenposition):
///   Seite 1: Personaldaten, Arbeitgeberdaten, Kalenderraster, Fragen 2–5
///   Seite 2: Fragen 6–9, Bruttolohn-Zusammensetzung, Frage 10 (Weiterführung)
///   Seite 3: Fragen 11–13 (BVG, Kinderzulagen, Beteiligung) + Unterschrift
/// </summary>
public class ZwischenverdienistPdfService
{
    private readonly IWebHostEnvironment _env;

    public ZwischenverdienistPdfService(IWebHostEnvironment env)
    {
        _env = env;
    }

    // ── Kalenderraster: Kalendertag → Feldname ────────────────────────────────
    private static readonly Dictionary<int, string> DayFields = new()
    {
        {  1, "2.45" }, {  2, "2.46" }, {  3, "2.47" }, {  4, "2.48" }, {  5, "2.49" },
        {  6, "2.51" }, {  7, "2.50" }, {  8, "2.52" }, {  9, "2.53" }, { 10, "2.58" },
        { 11, "2.56" }, { 12, "2.60" }, { 13, "2.54" }, { 14, "2.59" }, { 15, "2.57" },
        { 16, "2.76" }, { 17, "2.61" }, { 18, "2.69" }, { 19, "2.65" }, { 20, "2.73" },
        { 21, "2.63" }, { 22, "2.71" }, { 23, "2.67" }, { 24, "2.75" }, { 25, "2.62" },
        { 26, "2.70" }, { 27, "2.66" }, { 28, "2.74" }, { 29, "2.64" }, { 30, "2.72" },
        { 31, "2.68" },
    };

    /// <summary>
    /// Generiert das ALV-Bescheinigungs-PDF.
    /// </summary>
    /// <param name="d">DTO mit allen Feldwerten.</param>
    /// <param name="signaturePng">Optional: PNG/JPG-Bytes der Unterschrift des
    /// eingeloggten Users. Wird oberhalb des Datums-Feldes auf Seite 3
    /// (Ort/Datum) platziert; darunter wird der Klarname als gedruckte Zeile
    /// gerendert. Null = Stelle bleibt leer (User hat keine hinterlegt).</param>
    /// <param name="signerName">Klarname des Unterzeichners.</param>
    public byte[] Generate(
        ZwischenverdienistData d,
        byte[]? signaturePng = null,
        string? signerName = null)
    {
        string templatePath = System.IO.Path.Combine(
            _env.ContentRootPath, "Assets", "Forms", "Zwischenverdienst_AcroForm.pdf");

        using var ms     = new MemoryStream();
        using var reader = new PdfReader(templatePath);
        using var writer = new PdfWriter(ms);
        using var pdf    = new PdfDocument(reader, writer);

        var form = PdfAcroForm.GetAcroForm(pdf, false);
        form.SetNeedAppearances(true);

        // ── Seite 1: Kopfzeile ────────────────────────────────────────────────
        Set(form, "Textfeld 106", (d.Monat ?? "") + (d.Jahr ?? ""));

        // ── Seite 1: Personaldaten ────────────────────────────────────────────
        var nameParts = (d.NameVorname ?? "").Split(' ', 2);
        Set(form, "1.2", nameParts.Length > 0 ? nameParts[0] : "");
        Set(form, "1.3", nameParts.Length > 1 ? nameParts[1] : "");

        string ahvDigits = System.Text.RegularExpressions.Regex.Replace(
            d.AhvNummer ?? "", @"\D", "");
        if (ahvDigits.StartsWith("756"))
            ahvDigits = ahvDigits[3..];
        Set(form, "Textfeld 101", ahvDigits);

        // Geburtsdatum: "09.04.1988" → nur Ziffern → "09041988"
        string gebDat = System.Text.RegularExpressions.Regex.Replace(
            d.Geburtsdatum ?? "", @"\D", "");
        Set(form, "Textfeld 102", gebDat);
        Set(form, "1.35",         d.AusgeuebteTaetigkeit);

        // ── Seite 1: Angaben zum Arbeitgeber ─────────────────────────────────
        // Vollständige Filial-Anschrift im Feld "Name des Arbeitgebers":
        // "Schaub Restaurants GmbH, Äussere Luzernerstrasse 13, 4665 Oftringen"
        // Komma → Komma+Space, damit's auf einer Zeile lesbar bleibt.
        string arbName = (d.ArbeitgeberAdresse ?? "").Replace(", ", ", ").Trim();
        Set(form, "1.4", arbName);
        Set(form, "Textfeld 103", d.BurNummer);
        // UID: "CHE-262.373.037" → nur Ziffern ohne Prefix → "262373037"
        string uidDigits = System.Text.RegularExpressions.Regex.Replace(
            d.UidNummer ?? "", @"\D", "");
        if (uidDigits.StartsWith("756")) // falls AHV-ähnlicher Prefix
            uidDigits = uidDigits[3..];
        Set(form, "Textfeld 104", uidDigits);

        SetRadio(form, "Optionsfeld 21", "0");
        Set(form, "1.32", d.AnsprechpersonName);
        Set(form, "1.56", d.AnsprechpersonVorname);
        Set(form, "1.57", d.TelNummer);
        Set(form, "1.58", d.Email);

        // ── Seite 1: Kalenderraster (Frage 1) ────────────────────────────────
        foreach (var kv in d.TagesEintraege)
            if (DayFields.TryGetValue(kv.Key, out var fieldName))
                Set(form, fieldName, kv.Value);

        // ── Seite 1: Frage 2 – Schriftlicher Arbeitsvertrag ──────────────────
        SetRadio(form, "Optionsfeld 1", d.SchriftlicherArbeitsvertrag == true ? "1" : "0");

        // ── Seite 1: Frage 3 – Wöchentliche Arbeitszeit vereinbart ───────────
        SetRadio(form, "Optionsfeld 2", d.WoechentlicheAzVereinbart == true ? "1" : "0");
        if (d.WoechentlicheAzVereinbart == true)
            Set(form, "1.54", FormatNum(d.VereinbarteStundenProWoche));

        // ── Seite 1: Frage 4 – Normalarbeitszeit im Betrieb ──────────────────
        Set(form, "1.55", FormatNum(d.NormalarbeitszeitProWoche));

        // ── Seite 1: Frage 5 – GAV ───────────────────────────────────────────
        SetRadio(form, "Optionsfeld 3", d.IstGav == true ? "1" : "0");
        if (d.IstGav == true)
            Set(form, "1.70", d.GavName);

        // ── Seite 2: Frage 6 – Mehr Stunden angeboten ────────────────────────
        SetRadio(form, "Optionsfeld 4", d.MehrStundenAngeboten == true ? "1" : "0");
        if (d.MehrStundenAngeboten == true)
        {
            Set(form, "1.60", FormatNum(d.MehrStundenProTag));
            Set(form, "1.61", FormatNum(d.MehrStundenProWoche));
            Set(form, "1.64", FormatNum(d.MehrStundenProMonat));
        }

        // ── Seite 2: Abschnitt 8 – Vereinbartes Bruttoeinkommen ──────────────
        if (d.MonatslohnCHF.HasValue)
            SetRight(form, "1.65", FormatChf(d.MonatslohnCHF.Value));
        if (d.StundenlohnCHF.HasValue)
        {
            // Bei Stundenlohn: Grundlohn + Feiertag + Ferien + 13.ML + Bruttolohn pro Stunde
            decimal stdGrundlohn = d.StundenlohnCHF.Value;
            decimal stdFeiertag  = d.StundenlohnFeiertagCHF ?? 0;
            decimal stdFerien    = d.StundenlohnFerienCHF   ?? 0;
            decimal stdDreizehn  = d.StundenlohnDreizehnCHF ?? 0;
            decimal stdBrutto    = d.StundenlohnBruttoCHF   ?? (stdGrundlohn + stdFeiertag + stdFerien + stdDreizehn);

            SetRight(form, "1.72", FormatChf(stdGrundlohn));        // Grundlohn
            if (stdFeiertag > 0) SetRight(form, "1.73", FormatChf(stdFeiertag));   // Feiertagsentschädigung
            if (stdFerien   > 0) SetRight(form, "1.74", FormatChf(stdFerien));     // Ferienentschädigung
            if (stdDreizehn > 0) SetRight(form, "1.75", FormatChf(stdDreizehn));   // 13. Monatslohn
            if (stdBrutto   > 0) SetRight(form, "1.82", FormatChf(stdBrutto));     // Bruttolohn (pro Stunde)
        }

        // ── Seite 2: Abschnitt 9 – Zusammensetzung des Bruttoeinkommens ──────
        // Anzahl Std. = Total. Bei MTP daneben Aufschlüsselung
        // (garantierte + darüber hinaus); Summe steht im Feld 1.85.
        SetRight(form, "1.85", FormatNum(d.TotalStunden));
        if (d.StundenGarantiert.HasValue)
        {
            DrawMtpStundenAufschluesselung(
                pdf, form, "1.85",
                d.StundenGarantiert.Value,
                d.StundenDarueber ?? 0m);
        }

        SetRight(form, "4.141", FormatChf2(d.Grundlohn));

        if (d.FeiertagsprozentString is not null)
        {
            SetRight(form, "4.139", d.FeiertagsprozentString.TrimEnd('%'));
            SetRight(form, "4.140", FormatChf2(d.FeiertagsCHF));
        }
        if (d.FerienprozentString is not null)
        {
            SetRight(form, "4.147", d.FerienprozentString.TrimEnd('%'));
            var chfFerien = FormatChf2(d.FerienCHF);
            if (!string.IsNullOrWhiteSpace(d.FerienBemerkung) && chfFerien != null)
                chfFerien = "*" + chfFerien;
            SetRight(form, "4.143", chfFerien);
        }
        // FLEX-Pott: Bemerkung rechts, gleiche linke Kante wie 13.-Probezeit-Hinweis
        if (!string.IsNullOrWhiteSpace(d.FerienBemerkung))
            DrawRemarkNearField(pdf, form, "4.143", "Kontrollkästchen 20", d.FerienBemerkung!, wrap: true);

        if (d.DreizehnterProzentString is not null)
        {
            SetRight(form, "4.146", d.DreizehnterProzentString.TrimEnd('%'));
            // Probezeit: «*0.00» — Fussnote zur Bemerkung neben der Checkbox
            var chf13 = FormatChf2(d.DreizehnterCHF);
            if (!string.IsNullOrWhiteSpace(d.DreizehnterBemerkung) && chf13 != null)
                chf13 = "*" + chf13;
            SetRight(form, "4.142", chf13);
        }
        // Probezeit: Bemerkung UNTER «13. Monatslohn ist weder…» (gleiche Schrift wie Ferien)
        // Kontrollkästchen 20 bleibt aus — 13. ist vereinbart, nur noch nicht zahlbar.
        if (!string.IsNullOrWhiteSpace(d.DreizehnterBemerkung))
            DrawRemarkNearField(pdf, form, "Kontrollkästchen 20", "Kontrollkästchen 20",
                d.DreizehnterBemerkung!, wrap: true, yBelow: true);

        // Taggeldleistungen (aus Lohnbeleg: Karenz/Taggeld-Zeilen)
        // 4.144 = CHF-Betrag, 4.145 = "welche?"-Beschreibung
        if (d.TaggeldleistungenCHF.HasValue && d.TaggeldleistungenCHF.Value != 0)
        {
            SetRight(form, "4.144", FormatChf2(d.TaggeldleistungenCHF));
            if (!string.IsNullOrEmpty(d.TaggeldleistungenWelche))
                Set(form, "4.145", d.TaggeldleistungenWelche);  // Text → linksbündig
        }
        // Andere Lohnbestandteile (Zulagen, LGAV, …) + Bonus
        // 4.150 = CHF, 4.151 = "welche?", 4.153 = Bonus CHF
        if (d.AndereLohnbestandteileCHF.HasValue && d.AndereLohnbestandteileCHF.Value != 0)
        {
            SetRight(form, "4.150", FormatChf2(d.AndereLohnbestandteileCHF));
            if (!string.IsNullOrEmpty(d.AndereLohnbestandteileWelche))
                Set(form, "4.151", d.AndereLohnbestandteileWelche);
        }
        if (d.BonusCHF.HasValue && d.BonusCHF.Value != 0)
            SetRight(form, "4.153", FormatChf2(d.BonusCHF));
        SetRight(form, "4.154", FormatChf2(d.BruttolohnTotal));

        // ── Seite 2: Frage 10 – Arbeitsverhältnis weitergeführt? ─────────────
        // Optionsfeld 20 (Reihenfolge auf dem Formular, Textfeld 98 neben Zeile 1):
        //   /0 = befristet bis (+ Textfeld 98)   ← oben
        //   /1 = ja, unbefristet                 ← Mitte
        //   /2 = nein (+ Optionsfeld 30/31)      ← unten
        // Walter 09.10.2026: Mapping war vertauscht → unbefristet landete auf «befristet».
        if (d.ArbeitsverhaeltnisBeendet == true)
        {
            SetRadio(form, "Optionsfeld 20", "2");
            var durch = (d.KuendigungDurch ?? "").Trim().ToUpperInvariant();
            if (durch is "AG" or "ARBEITGEBER")
                SetRadio(form, "Optionsfeld 30", "0");
            else if (durch is "AN" or "ARBEITNEHMER" or "MA")
                SetRadio(form, "Optionsfeld 30", "1");
            // GG / unbekannt → Radios leer (Formular hat nur AG/AN)
            if (d.KuendigungSchriftlich == true)
                SetRadio(form, "Optionsfeld 31", "1");
            else if (d.KuendigungSchriftlich == false)
                SetRadio(form, "Optionsfeld 31", "0");
            Set(form, "Textfeld 96", FormatDatumZiffern(d.GekuendigtAm));
            Set(form, "Textfeld 97", FormatDatumZiffern(d.GekuendigtPer));
        }
        else if (d.WeiterbeschaeftigtBis.HasValue)
        {
            SetRadio(form, "Optionsfeld 20", "0");
            Set(form, "Textfeld 98", FormatDatumZiffern(d.WeiterbeschaeftigtBis));
        }
        else if (d.WeiterbeschaeftigtUnbefristet == true
              || d.ArbeitsverhaeltnisBeendet == false)
        {
            SetRadio(form, "Optionsfeld 20", "1");
        }

        // ── Seite 3: Frage 11 – BVG ──────────────────────────────────────────
        bool bvgJa = !string.IsNullOrWhiteSpace(d.BvgVersicherer) || d.BvgErhoben == true;
        SetRadio(form, "Optionsfeld 22", bvgJa ? "1" : "0");
        if (bvgJa)
            Set(form, "1.76", d.BvgVersicherer);

        // ── Seite 3: Frage 12 – Kinderzulagen ────────────────────────────────
        // Optionsfeld 23 vertikal: /0 = ja (oben), /1 = nein (unten)
        if (d.KinderzulagenAusgerichtet.HasValue)
        {
            SetRadio(form, "Optionsfeld 23", d.KinderzulagenAusgerichtet.Value ? "0" : "1");
            if (d.KinderzulagenAusgerichtet.Value)
            {
                if (d.AnzahlKinderzulagen.HasValue)
                    Set(form, "1.79", d.AnzahlKinderzulagen.Value.ToString());
                if (d.AnzahlAusbildungszulagen.HasValue)
                    Set(form, "1.78", d.AnzahlAusbildungszulagen.Value.ToString());
            }
        }

        // ── Seite 3: Frage 13 – Finanzbeteiligung ────────────────────────────
        SetRadio(form, "Optionsfeld 71", d.IstBeteiligt == true ? "1" : "0");

        // ── Seite 3: Ort und Datum ────────────────────────────────────────────
        // OrtDatum = "Oftringen, 12.04.2026"
        // Datum: Punkte sind statisch im Formular (MaxLen 8) → nur Ziffern "12042026"
        if (!string.IsNullOrWhiteSpace(d.OrtDatum))
        {
            var ortParts = d.OrtDatum.Split(',', 2, StringSplitOptions.TrimEntries);
            Set(form, "5.17", ortParts[0]);  // Ort

            string datumDigits = System.Text.RegularExpressions.Regex.Replace(
                ortParts.Length > 1 ? ortParts[1] : "", @"\D", "");
            Set(form, "Textfeld 95", datumDigits);  // → "12042026"
        }

        // ── Signatur einbetten (Konvention wie QST: User der's generiert) ──
        // Anker: Ort-Feld "5.17" auf Seite 3 (links, Ort/Datum-Zeile).
        // Bild UNTERHALB davon platziert — landet im "Unterschrift"-Kasten.
        // Klarname direkt UNTER dem Bild.
        if (signaturePng != null && signaturePng.Length > 0)
        {
            EmbedSignature(pdf, form, "5.17", signaturePng, signerName ?? "");
        }

        pdf.Close();
        return ms.ToArray();
    }

    // ── Signatur-Embedding (gleiche Logik wie QstAnmeldungPdfService) ────────
    // Layout: Bild im Unterschrift-Kasten unter dem Ort-Feld;
    // Klarname direkt unter dem Bild.
    //   SigOffsetY = Y-Verschiebung des Bild-Unterrand relativ zur Anker-
    //                Feld-UNTERkante (negativ = unterhalb).
    private const float SigOffsetX = 0f;
    private const float SigOffsetY = -55f;   // 55 pt unterhalb des Ort-Feldes
    private const float SigWidth   = 130f;
    private const float SigHeight  = 32f;

    private static void EmbedSignature(
        PdfDocument pdf, PdfAcroForm form, string anchorFieldName,
        byte[] signatureBytes, string signerName)
    {
        PdfFormField? anchor = null;
        try { anchor = form.GetField(anchorFieldName); } catch { }
        if (anchor == null) return;

        var widgets = anchor.GetWidgets();
        if (widgets == null || widgets.Count == 0) return;

        var widget = widgets[0];
        var rectArr = widget.GetRectangle();
        if (rectArr == null) return;
        var anchorRect = rectArr.ToRectangle();

        // Seite des Widgets ermitteln (Annotation → Page-Lookup).
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

        float x = anchorRect.GetLeft()   + SigOffsetX;
        float y = anchorRect.GetBottom() + SigOffsetY;
        var imgRect = new Rectangle(x, y, SigWidth, SigHeight);

        var canvas = new PdfCanvas(widgetPage);
        try
        {
            var imgData = ImageDataFactory.Create(signatureBytes);
            canvas.AddImageFittedIntoRectangle(imgData, imgRect, false);
        }
        catch { /* defektes Bild → Klarname trotzdem rendern */ }

        if (!string.IsNullOrWhiteSpace(signerName))
        {
            try
            {
                var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                canvas.SaveState();
                canvas.SetFillColor(ColorConstants.BLACK);
                canvas.BeginText()
                      .SetFontAndSize(font, 7.5f)
                      .MoveText(x + 2, y - 9)    // 9 pt unter der Bild-Unterkante
                      .ShowText(signerName)
                      .EndText();
                canvas.RestoreState();
            }
            catch { }
        }
    }

    // ── Hilfsmethoden ────────────────────────────────────────────────────────

    /// <summary>
    /// Kursiv-Bemerkung (8 pt wie Ferien): X an xAlignField (Label-Spalte), Y an yAnchorField.
    /// yBelow = unter dem Anker (z.B. unter Formulartext «13. Monatslohn ist weder…»).
    /// </summary>
    private static void DrawRemarkNearField(
        PdfDocument pdf, PdfAcroForm form,
        string yAnchorField, string xAlignField, string text,
        bool wrap, bool yBelow = false)
    {
        if (!TryGetFieldRect(pdf, form, yAnchorField, out var yRect, out var page)) return;
        if (!TryGetFieldRect(pdf, form, xAlignField, out var xRect, out _)) return;

        try
        {
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA_OBLIQUE);
            const float fontSize = 8f;
            float x = xRect.GetRight() + 4f;
            // yBelow: unter Formularzeile «13. Monatslohn ist weder…» —
            // Zeilen NUR nach unten stapeln (sonst überdeckt die 1. Zeile den Text).
            float y = yBelow ? yRect.GetBottom() - 10f : yRect.GetBottom() + 6f;
            float maxWidth = 250f;
            float lineStep = fontSize + 1.5f;

            var lines = BuildRemarkLines(font, fontSize, text, maxWidth, wrap);
            var canvas = new PdfCanvas(page);
            canvas.SaveState();
            canvas.SetFillColor(ColorConstants.BLACK);
            // Ferien (neben der Zeile): Block nach oben wachsen lassen.
            // 13. (unter dem Formulartext): erste Zeile bei y, Rest darunter.
            float lineY = yBelow ? y : y + (lines.Length - 1) * lineStep;
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) { lineY -= lineStep; continue; }
                canvas.BeginText()
                      .SetFontAndSize(font, fontSize)
                      .MoveText(x, lineY)
                      .ShowText(line)
                      .EndText();
                lineY -= lineStep;
            }
            canvas.RestoreState();
        }
        catch { /* Overlay optional */ }
    }

    private static bool TryGetFieldRect(
        PdfDocument pdf, PdfAcroForm form, string fieldName,
        out Rectangle rect, out PdfPage page)
    {
        rect = new Rectangle(0, 0);
        page = pdf.GetPage(1);
        PdfFormField? field = null;
        try { field = form.GetField(fieldName); } catch { }
        if (field is null) return false;

        var widgets = field.GetWidgets();
        if (widgets == null || widgets.Count == 0) return false;

        var widget = widgets[0];
        var rectArr = widget.GetRectangle();
        if (rectArr == null) return false;
        rect = rectArr.ToRectangle();

        PdfPage? widgetPage = null;
        for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
        {
            var p = pdf.GetPage(i);
            foreach (var an in p.GetAnnotations())
            {
                if (an.GetPdfObject() == widget.GetPdfObject())
                {
                    widgetPage = p;
                    break;
                }
            }
            if (widgetPage != null) break;
        }
        if (widgetPage == null) return false;
        page = widgetPage;
        return true;
    }

    private static string[] BuildRemarkLines(
        PdfFont font, float fontSize, string text, float maxWidth, bool wrap)
    {
        var paragraphs = text.Replace("\r\n", "\n").Split('\n');
        if (!wrap)
            return paragraphs;

        var lines = new List<string>();
        foreach (var para in paragraphs)
            lines.AddRange(WrapText(font, fontSize, para, maxWidth));
        return lines.Count > 0 ? lines.ToArray() : new[] { text };
    }

    private static string[] WrapText(PdfFont font, float fontSize, string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return new[] { "" };
        var words = text.Split(' ');
        var lines = new List<string>();
        var cur = "";
        foreach (var w in words)
        {
            var trial = string.IsNullOrEmpty(cur) ? w : cur + " " + w;
            if (font.GetWidth(trial, fontSize) <= maxWidth)
            {
                cur = trial;
            }
            else
            {
                if (!string.IsNullOrEmpty(cur)) lines.Add(cur);
                cur = w;
            }
        }
        if (!string.IsNullOrEmpty(cur)) lines.Add(cur);
        return lines.Count > 0 ? lines.ToArray() : new[] { text };
    }

    /// <summary>
    /// MTP-Aufschlüsselung rechts neben «Anzahl Std.»:
    /// Zeile 1 garantierte Std., Zeile 2 darüber hinaus — Total bleibt im Feld.
    /// </summary>
    private static void DrawMtpStundenAufschluesselung(
        PdfDocument pdf, PdfAcroForm form, string fieldName,
        decimal garantiert, decimal darueber)
    {
        PdfFormField? field = null;
        try { field = form.GetField(fieldName); } catch { }
        if (field is null) return;

        var widgets = field.GetWidgets();
        if (widgets == null || widgets.Count == 0) return;

        var widget = widgets[0];
        var rectArr = widget.GetRectangle();
        if (rectArr == null) return;
        var rect = rectArr.ToRectangle();

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
        if (widgetPage == null) return;

        // Labels ohne Umlaute: Helvetica (WinAnsi) hat kein zuverlässiges «ü».
        string gStr = garantiert.ToString("0.00", CultureInfo.InvariantCulture);
        string dStr = darueber.ToString("0.00", CultureInfo.InvariantCulture);
        string line1 = $"garantierte Std. {gStr}";
        string line2 = $"+ Mehrstunden {dStr}";
        try
        {
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            float x = rect.GetRight() + 8f;
            float yTop = rect.GetTop() - 2f;
            float lineH = 9f;
            var canvas = new PdfCanvas(widgetPage);
            canvas.SaveState();
            canvas.SetFillColor(ColorConstants.BLACK);
            canvas.BeginText()
                  .SetFontAndSize(font, 7f)
                  .MoveText(x, yTop - lineH)
                  .ShowText(line1)
                  .MoveText(0, -lineH)
                  .ShowText(line2)
                  .EndText();
            canvas.RestoreState();
        }
        catch { /* Aufschlüsselung optional — Total im Feld bleibt */ }
    }

    private static void Set(PdfAcroForm form, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = form.GetField(fieldName);
        if (field is null) return;
        field.SetValue(value);
    }

    /// <summary>Wie Set, aber rechtsbündig (für CHF-/Zahlen-Felder).</summary>
    private static void SetRight(PdfAcroForm form, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = form.GetField(fieldName);
        if (field is null) return;
        field.SetValue(value);
        field.SetJustification(iText.Layout.Properties.TextAlignment.RIGHT);
    }

    private static void SetRadio(PdfAcroForm form, string fieldName, string value)
    {
        var field = form.GetField(fieldName);
        if (field is null) return;
        try
        {
            field.SetValue(value);
            return;
        }
        catch { /* Opt-Namen versuchen */ }

        // Manche Radios exportieren als «Auswahl1» statt «0» (Frage 10).
        if (int.TryParse(value, out var idx) && idx >= 0)
        {
            try { field.SetValue("Auswahl" + (idx + 1)); }
            catch { /* Feld bleibt leer */ }
        }
    }

    private static string? FormatNum(decimal? v) =>
        v.HasValue ? v.Value.ToString("G") : null;

    private static string? FormatChf(decimal? v) =>
        v.HasValue ? FormatChf(v.Value) : null;

    private static string FormatChf(decimal v)
    {
        // Beispiel: 1347.50 → "1'347.50"
        string s = v.ToString("N2");   // "1,347.50" (en-US)
        return s.Replace(",", "'");    // → "1'347.50"
    }

    private static string? FormatChf2(decimal? v) => FormatChf(v);

    /// <summary>Formular-Datumsfelder ohne Punkte: 09.10.2026 → «09102026».</summary>
    private static string? FormatDatumZiffern(DateOnly? d)
        => d.HasValue ? d.Value.ToString("ddMMyyyy", CultureInfo.InvariantCulture) : null;
}

// ── DTO ───────────────────────────────────────────────────────────────────────

public class ZwischenverdienistData
{
    public string? NameVorname            { get; set; }
    public string? PersNr                 { get; set; }
    public string? AhvNummer              { get; set; }
    public string? Adresse                { get; set; }
    public string? Geburtsdatum           { get; set; }
    public string? Zivilstand             { get; set; }
    public string? Monat                  { get; set; }
    public string? Jahr                   { get; set; }
    public string? AusgeuebteTaetigkeit   { get; set; }

    public Dictionary<int, string> TagesEintraege { get; set; } = new();

    public bool? SchriftlicherArbeitsvertrag   { get; set; }
    public bool? WoechentlicheAzVereinbart     { get; set; }
    public decimal? VereinbarteStundenProWoche  { get; set; }
    public decimal? NormalarbeitszeitProWoche   { get; set; }
    public bool? IstGav                         { get; set; }
    public string? GavName                      { get; set; }
    public bool? MehrStundenAngeboten           { get; set; }
    public decimal? MehrStundenProTag           { get; set; }
    public decimal? MehrStundenProWoche         { get; set; }
    public decimal? MehrStundenProMonat         { get; set; }

    public decimal? StundenlohnCHF              { get; set; }
    /// <summary>Pro-Stunde-Felder in Punkt 8 "Bei Stundenlohn"</summary>
    public decimal? StundenlohnFeiertagCHF      { get; set; }
    public decimal? StundenlohnFerienCHF        { get; set; }
    public decimal? StundenlohnDreizehnCHF      { get; set; }
    public decimal? StundenlohnBruttoCHF        { get; set; }

    public decimal? MonatslohnCHF               { get; set; }
    public decimal? TotalStunden                { get; set; }
    /// <summary>MTP: garantierte Festlohn-Stunden (Aufschlüsselung neben Anzahl Std.).</summary>
    public decimal? StundenGarantiert           { get; set; }
    /// <summary>MTP: Stunden über der Garantie (Ist − Garantie, min. 0).</summary>
    public decimal? StundenDarueber             { get; set; }
    public decimal? BruttolohnTotal             { get; set; }
    public decimal? Grundlohn                   { get; set; }
    public string? FeiertagsprozentString       { get; set; }
    public decimal? FeiertagsCHF                { get; set; }
    public string? FerienprozentString          { get; set; }
    public decimal? FerienCHF                   { get; set; }
    /// <summary>FLEX-Pott: Hinweis rechts neben Ferien-Zeile.</summary>
    public string? FerienBemerkung              { get; set; }
    public string? DreizehnterProzentString     { get; set; }
    public decimal? DreizehnterCHF              { get; set; }
    /// <summary>z.B. «13. wird erst nach Probezeit ausbezahlt» — Overlay oberhalb Checkbox.</summary>
    public string? DreizehnterBemerkung         { get; set; }
    public decimal? TaggeldleistungenCHF        { get; set; }
    public string?  TaggeldleistungenWelche     { get; set; }
    /// <summary>Andere Lohnbestandteile (Zulagen, LGAV-Ausnahme, …) — nicht Bonus.</summary>
    public decimal? AndereLohnbestandteileCHF   { get; set; }
    public string?  AndereLohnbestandteileWelche { get; set; }
    /// <summary>Bonus / Gratifikation (eigene Zeile auf dem Formular).</summary>
    public decimal? BonusCHF                    { get; set; }

    public bool? DreizehnterJahresendAuszahlung { get; set; }
    public bool? BvgErhoben                     { get; set; }
    public string? BvgVersicherer               { get; set; }
    public string? AhvKasse                     { get; set; }
    public bool? KinderzulagenAusgerichtet      { get; set; }
    public int? AnzahlKinderzulagen             { get; set; }
    public int? AnzahlAusbildungszulagen        { get; set; }

    /// <summary>Frage 10: true = beendet (nein), false = läuft weiter, null = nicht gesetzt.</summary>
    public bool? ArbeitsverhaeltnisBeendet      { get; set; }
    /// <summary>Frage 10: ja, unbefristet (nur wenn nicht beendet und kein Bis-Datum).</summary>
    public bool? WeiterbeschaeftigtUnbefristet  { get; set; }
    /// <summary>Frage 10: befristet bis — setzt Optionsfeld 20=/1 + Textfeld 98.</summary>
    public DateOnly? WeiterbeschaeftigtBis      { get; set; }
    /// <summary>Frage 10 bei nein: AG / AN (GG bleibt leer).</summary>
    public string? KuendigungDurch              { get; set; }
    public DateOnly? GekuendigtAm               { get; set; }
    public DateOnly? GekuendigtPer              { get; set; }
    public bool? KuendigungSchriftlich          { get; set; }

    public bool? IstBeteiligt                   { get; set; }

    public string? OrtDatum                     { get; set; }
    public string? ArbeitgeberAdresse           { get; set; }
    public string? UidNummer                    { get; set; }
    public string? TelNummer                    { get; set; }
    public string? Email                        { get; set; }
    public string? BurNummer                    { get; set; }
    public string? BranchenCode                 { get; set; }
    public string? AnsprechpersonName           { get; set; }
    public string? AnsprechpersonVorname        { get; set; }
}