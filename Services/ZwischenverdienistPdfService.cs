using HrSystem.Models;
using iText.Forms;
using iText.Forms.Fields;
using iText.Kernel.Pdf;

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

    public byte[] Generate(ZwischenverdienistData d)
    {
        string templatePath = Path.Combine(
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
        var adressParts = (d.ArbeitgeberAdresse ?? "").Split(',', 3,
            StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string arbName = adressParts.Length > 1
            ? $"{adressParts[0]} {adressParts[adressParts.Length - 1]}"
            : adressParts.Length > 0 ? adressParts[0] : "";
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
            Set(form, "1.65", FormatChf(d.MonatslohnCHF.Value));
        if (d.StundenlohnCHF.HasValue)
            Set(form, "1.72", FormatChf(d.StundenlohnCHF.Value));

        // ── Seite 2: Abschnitt 9 – Zusammensetzung des Bruttoeinkommens ──────
        Set(form, "1.85", FormatNum(d.TotalStunden));

        Set(form, "4.141", FormatChf2(d.Grundlohn));

        if (d.FeiertagsprozentString is not null)
        {
            Set(form, "4.139", d.FeiertagsprozentString.TrimEnd('%'));
            Set(form, "4.140", FormatChf2(d.FeiertagsCHF));
        }
        if (d.FerienprozentString is not null)
        {
            Set(form, "4.147", d.FerienprozentString.TrimEnd('%'));
            Set(form, "4.143", FormatChf2(d.FerienCHF));
        }
        if (d.DreizehnterProzentString is not null)
        {
            Set(form, "4.146", d.DreizehnterProzentString.TrimEnd('%'));
            Set(form, "4.142", FormatChf2(d.DreizehnterCHF));
        }
        Set(form, "4.154", FormatChf2(d.BruttolohnTotal));

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

        pdf.Close();
        return ms.ToArray();
    }

    // ── Hilfsmethoden ────────────────────────────────────────────────────────

    private static void Set(PdfAcroForm form, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = form.GetField(fieldName);
        if (field is null) return;
        field.SetValue(value);
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
    public decimal? MonatslohnCHF               { get; set; }
    public decimal? TotalStunden                { get; set; }
    public decimal? BruttolohnTotal             { get; set; }
    public decimal? Grundlohn                   { get; set; }
    public string? FeiertagsprozentString       { get; set; }
    public decimal? FeiertagsCHF                { get; set; }
    public string? FerienprozentString          { get; set; }
    public decimal? FerienCHF                   { get; set; }
    public string? DreizehnterProzentString     { get; set; }
    public decimal? DreizehnterCHF              { get; set; }

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