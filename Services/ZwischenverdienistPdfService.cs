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
///   Seite 2: Fragen 6–9, Bruttolohn-Zusammensetzung
///   Seite 3: Fragen 10–14 (wird zurzeit nicht automatisch befüllt)
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
            SetRight(form, "4.143", FormatChf2(d.FerienCHF));
        }
        if (d.DreizehnterProzentString is not null)
        {
            SetRight(form, "4.146", d.DreizehnterProzentString.TrimEnd('%'));
            SetRight(form, "4.142", FormatChf2(d.DreizehnterCHF));
        }
        // Taggeldleistungen (Krank/Unfall-Karenz via KTG-Tagessatz)
        // 4.144 = CHF-Betrag, 4.145 = "welche?"-Beschreibung
        if (d.TaggeldleistungenCHF.HasValue && d.TaggeldleistungenCHF.Value > 0)
        {
            SetRight(form, "4.144", FormatChf2(d.TaggeldleistungenCHF));
            if (!string.IsNullOrEmpty(d.TaggeldleistungenWelche))
                Set(form, "4.145", d.TaggeldleistungenWelche);  // Text → linksbündig
        }
        SetRight(form, "4.154", FormatChf2(d.BruttolohnTotal));

        // ── Seite 3: Frage 11 – BVG ──────────────────────────────────────────
        bool bvgJa = !string.IsNullOrWhiteSpace(d.BvgVersicherer) || d.BvgErhoben == true;
        SetRadio(form, "Optionsfeld 22", bvgJa ? "1" : "0");
        if (bvgJa)
            Set(form, "1.76", d.BvgVersicherer);

        // ── Seite 3: Frage 12 – Kinderzulagen ────────────────────────────────
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
        field.SetValue(value);
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
    public string? DreizehnterProzentString     { get; set; }
    public decimal? DreizehnterCHF              { get; set; }
    public decimal? TaggeldleistungenCHF        { get; set; }
    public string?  TaggeldleistungenWelche     { get; set; }

    public bool? DreizehnterJahresendAuszahlung { get; set; }
    public bool? BvgErhoben                     { get; set; }
    public string? BvgVersicherer               { get; set; }
    public string? AhvKasse                     { get; set; }
    public bool? KinderzulagenAusgerichtet      { get; set; }
    public int? AnzahlKinderzulagen             { get; set; }
    public int? AnzahlAusbildungszulagen        { get; set; }
    public bool? WeiterbeschaeftigtUnbefristet  { get; set; }
    public DateOnly? WeiterbeschaeftigtBis      { get; set; }
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