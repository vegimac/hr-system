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
/// Füllt das amtliche Anmeldeformular für quellensteuerpflichtige
/// Arbeitnehmer/innen. Jeder Kanton hat sein eigenes PDF-Template mit teils
/// sehr unterschiedlichen AcroForm-Feldnamen und unterschiedlichen Modellen
/// für Ja/Nein-Felder. Deshalb dispatcht <see cref="Generate(QstAnmeldungData,string)"/>
/// nach Kanton und ruft den passenden Mapper auf.
///
/// Aktuell unterstützt:
///   • SO  — Solothurn   (Radiogruppen mit "0"/"1")
///   • AG  — Aargau      (zwei separate Checkboxen pro Ja/Nein-Frage)
///   • ZH  — Zürich      (gepunktete Feldnamen ag.*, an.p1.*, an.p2.*; Radio-Werte "1".."6")
///   • BE  — Bern        (generisch nummerierte Felder Text##/Group##; Radio-Werte "Auswahl1".."Auswahl5",
///                        plus Position-basiertes Mapping)
///
/// Fallback bei unbekanntem Kanton: SO-Template.
/// Walter hat erwähnt LU sieht aus wie AG — sobald LU-PDF da ist, prüfen
/// ob Feldnamen identisch sind (dann nur Dispatch hinzufügen) oder eigener Mapper nötig.
/// </summary>
public class QstAnmeldungPdfService
{
    private readonly IWebHostEnvironment _env;

    public QstAnmeldungPdfService(IWebHostEnvironment env)
    {
        _env = env;
    }

    /// <summary>
    /// Generiert das ausgefüllte QST-Anmeldeformular.
    /// </summary>
    /// <param name="d">DTO mit allen Feld-Werten.</param>
    /// <param name="kanton">2-Zeichen-Kantonscode für die Template-Auswahl.</param>
    /// <param name="signaturePng">Optional: PNG/JPG-Bytes der Unterschrift des
    /// eingeloggten Users. Wird oberhalb des „Ort, Datum"-Feldes des AG-Blocks
    /// platziert; darunter wird der Klarname als gedruckte Zeile gerendert.
    /// Null = Stelle bleibt leer (User hat keine Unterschrift hinterlegt).</param>
    /// <param name="signerName">Klarname des Unterzeichners (Vor- + Nachname),
    /// wird unter dem Bild ausgedruckt. Pflicht wenn signaturePng gesetzt ist.</param>
    public byte[] Generate(
        QstAnmeldungData d,
        string kanton = "SO",
        byte[]? signaturePng = null,
        string? signerName = null)
    {
        var k = (kanton ?? "SO").Trim().ToUpperInvariant();
        var formsDir = System.IO.Path.Combine(_env.ContentRootPath, "Assets", "Forms");

        // Welcher Mapper wird gebraucht?
        Action<PdfAcroForm, QstAnmeldungData> mapper = k switch
        {
            "AG" => MapAg,
            "BE" => MapBe,
            "SO" => MapSo,
            "ZH" => MapZh,
            _    => MapSo,    // Default = SO bis weitere Templates dazukommen
        };

        // Template-Datei: erst Kanton-spezifisch, sonst SO-Fallback
        var preferredPath = System.IO.Path.Combine(formsDir, $"QstAnmeldung_{k}.pdf");
        var fallbackPath  = System.IO.Path.Combine(formsDir, "QstAnmeldung_SO.pdf");
        string templatePath;
        bool usedFallbackTemplate = false;
        if (File.Exists(preferredPath))
        {
            templatePath = preferredPath;
        }
        else
        {
            // Wenn das Template fehlt, fällt auch der Mapper auf SO zurück —
            // sonst würden AG-Feldnamen ins SO-Template geschrieben → alles leer.
            templatePath = fallbackPath;
            mapper = MapSo;
            usedFallbackTemplate = true;
        }

        using var ms     = new MemoryStream();
        using var reader = new PdfReader(templatePath);
        using var writer = new PdfWriter(ms);
        using var pdf    = new PdfDocument(reader, writer);

        var form = PdfAcroForm.GetAcroForm(pdf, false);
        form.SetNeedAppearances(true);

        mapper(form, d);

        // Signatur einbetten — pro Kanton an dem Datums-Feld des AG-Blocks
        // verankert (= dort wo "Ort, Datum / Stempel / Unterschrift AG" steht).
        // Konvention: alle QST-Formulare haben diese Datum-Stelle in der unteren
        // Hälfte der Seite. Die Unterschrift wird darüber platziert mit dem
        // Klarnamen als gedruckte Zeile direkt darunter.
        if (signaturePng != null && signaturePng.Length > 0)
        {
            // Bei Template-Fallback (z.B. unbekannter Kanton → SO-Template) den
            // SO-Anker verwenden, weil eben das SO-Template gerendert wird.
            var anchorKanton = usedFallbackTemplate ? "SO" : k;
            EmbedSignature(pdf, form, anchorKanton, signaturePng, signerName ?? "");
        }

        pdf.Close();
        return ms.ToArray();
    }

    // ════════════════════════════════════════════════════════════════════════
    // SIGNATUR-EMBEDDING
    // ════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// Pro Kanton: an welchem AcroForm-Feld die Unterschrift verankert wird,
    /// und wie das Bild relativ dazu positioniert ist.
    ///   AnchorField = Feldname mit Datum/Ort des AG-Blocks
    ///   OffsetXFromAnchor = horizontaler Offset (relativ zur linken Ankerkante)
    ///   OffsetYFromAnchor = vertikaler Offset (positiv = nach OBEN über das Feld)
    ///   Width / Height    = Bild-Maße in PDF-Punkten (1 Punkt ≈ 0.353 mm)
    /// Werte sind Erfahrungswerte; bei optischer Abweichung pro Kanton tunen.
    /// </summary>
    private static readonly Dictionary<string, (string AnchorField, float OffsetX, float OffsetY, float Width, float Height)> _sigPlacement = new()
    {
        // (AnchorField, OffsetX, OffsetY, Width, Height) — alle in PDF-Punkten
        // OffsetY positiv = oberhalb der Datums-Bottom, negativ = unterhalb.
        // Default: Bild knapp UNTER dem Datumsfeld (auf der AG-Unterschriftslinie).
        ["SO"] = ("Ort-Datum_2", -5f, -85f, 130f, 32f),
        ["AG"] = ("ort_datum",   -5f, -85f, 130f, 32f),
        ["BE"] = ("Text53",      -5f, -85f, 130f, 32f),
        ["ZH"] = ("allg.datum",  -5f, -85f, 130f, 32f),
    };

    private static void EmbedSignature(
        PdfDocument pdf, PdfAcroForm form, string kanton,
        byte[] signatureBytes, string signerName)
    {
        if (!_sigPlacement.TryGetValue(kanton, out var cfg)) return;

        // Anker-Feld finden
        PdfFormField? anchor = null;
        try { anchor = form.GetField(cfg.AnchorField); } catch { /* fehlt → kein Embedding */ }
        if (anchor == null) return;

        var widgets = anchor.GetWidgets();
        if (widgets == null || widgets.Count == 0) return;

        var widget = widgets[0];
        var rectArr = widget.GetRectangle();
        if (rectArr == null) return;
        var anchorRect = rectArr.ToRectangle();

        // Auf welcher Seite ist das Widget?
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

        // Position berechnen: Anker = Bottom-Kante des Datumsfeldes.
        //   OffsetY > 0  → Bild oberhalb des Datumsfeldes
        //   OffsetY < 0  → Bild unterhalb des Datumsfeldes
        //   OffsetY = 0  → Bildunterkante exakt auf Datums-Bottom
        float x = anchorRect.GetLeft()   + cfg.OffsetX;
        float y = anchorRect.GetBottom() + cfg.OffsetY;
        var imgRect = new Rectangle(x, y, cfg.Width, cfg.Height);

        // Bild + Klarname auf die Seite zeichnen
        var canvas = new PdfCanvas(widgetPage);
        try
        {
            var imgData = ImageDataFactory.Create(signatureBytes);
            canvas.AddImageFittedIntoRectangle(imgData, imgRect, false);
        }
        catch
        {
            // Bild defekt / falscher MIME — Klarnamen trotzdem rendern.
        }

        if (!string.IsNullOrWhiteSpace(signerName))
        {
            try
            {
                var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
                canvas.SaveState();
                canvas.SetFillColor(ColorConstants.BLACK);
                canvas.BeginText()
                      .SetFontAndSize(font, 7.5f)
                      .MoveText(x + 2, y - 9)        // 9 pt unter der Bild-Unterkante
                      .ShowText(signerName)
                      .EndText();
                canvas.RestoreState();
            }
            catch { /* Font-Erstellung kann bei Embedded-PDFs fehlschlagen — Text dann eben weglassen. */ }
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // SO — Kanton Solothurn
    // ════════════════════════════════════════════════════════════════════════
    // Field-Mapping basiert auf den AcroForm-Feldnamen aus dem SO-Template:
    //   QA-... = Quellensteuerpflichtige/r Arbeitnehmer/in
    //   EP-... = Ehepartner/in
    // Ja/Nein-Konvention SO:
    //   • Standard-Felder (Trennung, EP-Erwerbstätigkeit, Andere Erwerbstätigkeit):
    //       "1" = Ja, "0" = Nein
    //   • Elterntarif-Block (Kinder/Konkubinat/Sorge/Unterhalt/Höh.Einkommen):
    //       "0" = Ja, "1" = Nein   (umgekehrt — siehe Controller-Konstanten Elt_Ja/Elt_Nein)
    private static void MapSo(PdfAcroForm form, QstAnmeldungData d)
    {
        // ── Arbeitgeber-Block ────────────────────────────────────────────
        Set(form, "SSLNr",          d.SslNummer);
        Set(form, "UIDNr",          d.UidNummer);
        Set(form, "Firma",          d.Firma);
        Set(form, "Adresse",        d.Adresse);
        Set(form, "PLZOrtKanton",   d.PlzOrtKanton);
        Set(form, "Kontaktperson",  d.Kontaktperson);
        Set(form, "Telefon",        d.Telefon);
        Set(form, "EMail",          d.Email);

        // ── Quellensteuerpflichtige/r Arbeitnehmer/in ───────────────────
        SetRadio(form, "QA-Geschlecht", d.QaGeschlecht);   // "0"=m, "1"=w
        Set(form, "QA-Name",          d.QaName);
        Set(form, "QA-Vorname",       d.QaVorname);
        Set(form, "QA-Strasse",       d.QaStrasse);
        Set(form, "QA-PLZOrtLand",    d.QaPlzOrtLand);
        Set(form, "QA-Geb",           d.QaGeburtsdatum);
        Set(form, "QA-Nation",        d.QaNationalitaet);
        Set(form, "QA-Bewilligung",   d.QaBewilligung);
        Set(form, "QA-SVNr",          d.QaSvNummer);

        // ── Zivilstand + Trennung + Datum ───────────────────────────────
        SetRadio(form, "Zivilstand",   d.Zivilstand);
        SetRadio(form, "Trennung",     d.GetrenntJaNein);
        Set(form, "Datum Zivilstand",  d.DatumZivilstand);

        // ── Konfession ──────────────────────────────────────────────────
        SetRadio(form, "Konfession",   d.Konfession);

        // ── Aufenthaltsadresse in der Schweiz ───────────────────────────
        Set(form, "Aufenthalt-Adresse",       d.AufenthaltAdresse);
        Set(form, "Aufenthalt-PLZOrtKanton",  d.AufenthaltPlzOrtKanton);

        // ── Beruf ────────────────────────────────────────────────────────
        Set(form, "Beruf Datum",          d.StellenAntrittDatum);
        Set(form, "Beruf Buttolohn",      d.BruttolohnMonat);
        Set(form, "Beruf Arbeitspensum",  d.ArbeitspensumProzent);
        SetRadio(form, "Beruf",           d.BerufFlag);   // "0"=Grenzgänger, "1"=Wochenaufenthalter

        // ── Andere Erwerbstätigkeit ─────────────────────────────────────
        SetRadio(form, "Erwerbstätigkeit",          d.HatAndereErwerbJaNein);
        Set(form, "Erwerbstätigkeit-Ja",            d.AndereArbeitgeberName);
        Set(form, "Erwerbstätigkeit-Arbeitgeber",   d.AndereArbeitgeberName2);
        Set(form, "Erwerbstätigkeit-Strasse",       d.AndereArbeitgeberStrasse);
        Set(form, "Erwerbstätigkeit-PLZOrtKanton",  d.AndereArbeitgeberPlzOrtKanton);
        Set(form, "Erwerbstätigkeit-Land",          d.AndereArbeitgeberLand);
        Set(form, "Erwerbstätigkeit-Pensum",        d.GesamtPensumProzent);

        // ── Ehepartner/in ───────────────────────────────────────────────
        SetRadio(form, "EP-Geschlecht", d.EpGeschlecht);
        Set(form, "EP-Name",            d.EpName);
        Set(form, "EP-Vorname",         d.EpVorname);
        Set(form, "EP-Strasse",         d.EpStrasse);
        Set(form, "EP-PLZOrtLand",      d.EpPlzOrtLand);
        Set(form, "EP-Geb",             d.EpGeburtsdatum);
        Set(form, "EP-Nation",          d.EpNationalitaet);
        Set(form, "EP-Bewilligung",     d.EpBewilligung);
        Set(form, "EP-SVNr",            d.EpSvNummer);

        SetRadio(form, "EP-Erwerbstätigkeit",        d.EpHatErwerbJaNein);
        Set(form, "EP-Erwerbstätigkeit-Arbeitgeber", d.EpArbeitgeberName);
        Set(form, "EP-Erwerbstätigkeit-Strasse",     d.EpArbeitgeberStrasse);
        Set(form, "EP-Erwerbstätigkeit-PLZOrtLand",  d.EpArbeitgeberPlzOrtLand);

        // ── Kinder ──────────────────────────────────────────────────────
        Set(form, "Anzahl-Kinder", d.AnzahlKinder);
        if (d.Kinder != null)
        {
            for (int i = 0; i < d.Kinder.Count && i < 4; i++)
                Set(form, $"Kind {i + 1}", d.Kinder[i]);
        }

        // ── Abklärung Elterntarif (invertiert "0"=Ja, "1"=Nein) ─────────
        SetRadio(form, "Kinder",                  d.LebenKinderImHaushaltJaNein);
        SetRadio(form, "Konkubinat",              d.LebtImKonkubinatJaNein);
        SetRadio(form, "Elterliche-Sorge",        d.UebtElterlicheSorgeJaNein);
        SetRadio(form, "Unterhaltszahlung",       d.ZahltUnterhaltVolljaehrigJaNein);
        SetRadio(form, "Höheres-Bruttoeinkommen", d.HoeheresBruttoEinkommenJaNein);

        // ── Bemerkungen ─────────────────────────────────────────────────
        Set(form, "Bemerkung 1", d.Bemerkung1);
        Set(form, "Bemerkung 2", d.Bemerkung2);
        Set(form, "Bemerkung 3", d.Bemerkung3);

        // ── Ort/Datum ───────────────────────────────────────────────────
        // Nur das AG-Feld vorausfüllen — MA-Feld füllt der Mitarbeitende beim Unterschreiben.
        Set(form, "Ort-Datum_2", d.OrtDatum);
    }

    // ════════════════════════════════════════════════════════════════════════
    // AG — Kanton Aargau
    // ════════════════════════════════════════════════════════════════════════
    // Field-Mapping basiert auf den AcroForm-Feldnamen aus dem AG-Template:
    //   AN_...   = Arbeitnehmer/in
    //   Part_... = Partner/in
    //   Firma*   = Arbeitgeber
    // Ja/Nein-Modell: pro Frage zwei separate Checkboxen ("_j" und "_n").
    // Wir checken jeweils nur die "richtige" Box an.
    private static void MapAg(PdfAcroForm form, QstAnmeldungData d)
    {
        // ── Arbeitgeber-Block ────────────────────────────────────────────
        Set(form, "SSL",            d.SslNummer);
        Set(form, "UID",            d.UidNummer);
        Set(form, "firma",          d.Firma);
        Set(form, "Firmaadresse1",  d.Adresse);
        Set(form, "Firmaplz",       d.PlzOrtKanton);
        Set(form, "Firmakontakt",   d.Kontaktperson);
        Set(form, "Firmatel",       d.Telefon);
        Set(form, "Firmamail",      d.Email);

        // ── Quellensteuerpflichtige/r Arbeitnehmer/in ───────────────────
        // Geschlecht: zwei Einzel-Checkboxen
        if (d.QaGeschlecht == "0") Check(form, "AN_mann");
        else if (d.QaGeschlecht == "1") Check(form, "AN_frau");

        Set(form, "AN_SV_Nr",       d.QaSvNummer);
        Set(form, "AN_name",        d.QaName);
        Set(form, "AN_vorname",     d.QaVorname);
        Set(form, "AN_strasse",     d.QaStrasse);
        Set(form, "AN_plz",         d.QaPlzOrtLand);
        Set(form, "AN_geburt",      d.QaGeburtsdatum);
        Set(form, "AN_nation",      d.QaNationalitaet);
        Set(form, "AN_bewilligung", d.QaBewilligung);

        // Zivilstand: pro Wert eine eigene Checkbox
        switch (d.Zivilstand)
        {
            case "0": Check(form, "AN_ledig"); break;
            case "1": Check(form, "AN_geschieden"); break;
            case "2": Check(form, "AN_verwitwet"); break;
            case "3": Check(form, "AN_verheiratet"); break;
            case "4": Check(form, "AN_partner"); break;
            case "5": Check(form, "AN_expartner"); break;
        }

        // Getrennt: "1" = Ja, "0" = Nein  (Standard-Konvention)
        if (d.GetrenntJaNein == "1") Check(form, "AN_getrennt_j");
        else if (d.GetrenntJaNein == "0") Check(form, "AN_getrennt_n");

        Set(form, "Datum_Zivilstand", d.DatumZivilstand);

        // Konfession: pro Wert eine eigene Checkbox
        switch (d.Konfession)
        {
            case "0": Check(form, "AN_ev-ref"); break;
            case "1": Check(form, "AN_r-k");    break;
            case "2": Check(form, "AN_c-k");    break;
            case "3": Check(form, "AN_and");    break;
        }

        // ── Aufenthaltsadresse in der Schweiz ───────────────────────────
        Set(form, "AN_aufenthalt",      d.AufenthaltAdresse);
        Set(form, "AN_aufenthalt_plz",  d.AufenthaltPlzOrtKanton);

        // ── Beruf ────────────────────────────────────────────────────────
        Set(form, "AN_stelle_datum", d.StellenAntrittDatum);
        Set(form, "AN_lohn",         d.BruttolohnMonat);
        Set(form, "AN_pensum",       d.ArbeitspensumProzent);

        // Arbeitsort: Grenzgänger vs Wochenaufenthalter (zwei Einzel-Checkboxen)
        if (d.BerufFlag == "0") Check(form, "AN_grenzgaenger");
        else if (d.BerufFlag == "1") Check(form, "AN_woche");

        // ── Andere Erwerbstätigkeit (Standard "1"=Ja "0"=Nein) ──────────
        if (d.HatAndereErwerbJaNein == "1") Check(form, "AN_and_erwerb_j");
        else if (d.HatAndereErwerbJaNein == "0") Check(form, "AN_and_erwerb_n");
        Set(form, "AN_ARG",      d.AndereArbeitgeberName2 ?? d.AndereArbeitgeberName);
        Set(form, "AN_ARG_str",  d.AndereArbeitgeberStrasse);
        Set(form, "AN_ARG_plz",  d.AndereArbeitgeberPlzOrtKanton);
        Set(form, "AN_ARG_land", d.AndereArbeitgeberLand);
        Set(form, "AN_pensum_1", d.GesamtPensumProzent);
        // AN_pensum_2 (in Std) — nicht zentral gepflegt, bleibt leer

        // ── Ehepartner/in ───────────────────────────────────────────────
        // Hinweis: das AG-PDF nennt die Partner-Geschlecht-Checkboxen
        // verwirrenderweise "AN_mann_2" / "AN_frau_2" (Copy-Paste-Bug der Vorlage).
        if (d.EpGeschlecht == "0") Check(form, "AN_mann_2");
        else if (d.EpGeschlecht == "1") Check(form, "AN_frau_2");

        Set(form, "Part_SV_Nr",       d.EpSvNummer);
        Set(form, "Part_Name",        d.EpName);
        Set(form, "Part_vorname",     d.EpVorname);
        Set(form, "Part_strasse",     d.EpStrasse);
        Set(form, "Part_plz",         d.EpPlzOrtLand);
        Set(form, "Part_geburt",      d.EpGeburtsdatum);
        Set(form, "Part_nation",      d.EpNationalitaet);
        Set(form, "Part_bewilligung", d.EpBewilligung);

        if (d.EpHatErwerbJaNein == "1") Check(form, "Part_erwerb_j");
        else if (d.EpHatErwerbJaNein == "0") Check(form, "Part_erwerb_n");

        Set(form, "Part_ARG",                d.EpArbeitgeberName);
        Set(form, "Part_ARG_str",            d.EpArbeitgeberStrasse);
        Set(form, "Part_ARG_plz",            d.EpArbeitgeberPlzOrtLand);
        // Part_ARG_stellenantritt kennen wir nicht zentral

        // ── Kinder ──────────────────────────────────────────────────────
        Set(form, "anz_kinder", d.AnzahlKinder);
        if (d.Kinder != null)
        {
            for (int i = 0; i < d.Kinder.Count && i < 4; i++)
                Set(form, $"kind_{i + 1}", d.Kinder[i]);
        }

        // ── Abklärung Elterntarif (invertiert "0"=Ja, "1"=Nein) ─────────
        SetJaNeinElt(form, "haushalt_j",  "haushalt_n",  d.LebenKinderImHaushaltJaNein);
        SetJaNeinElt(form, "konk_j",      "konk_n",      d.LebtImKonkubinatJaNein);
        SetJaNeinElt(form, "sorge_j",     "sorge_n",     d.UebtElterlicheSorgeJaNein);
        SetJaNeinElt(form, "unterhalt_j", "unterhalt_n", d.ZahltUnterhaltVolljaehrigJaNein);
        SetJaNeinElt(form, "erwerb_j",    "erwerb_n",    d.HoeheresBruttoEinkommenJaNein);

        // ── Bemerkungen ─────────────────────────────────────────────────
        Set(form, "bemerkungen1", d.Bemerkung1);
        Set(form, "bemerkungen2", d.Bemerkung2);

        // ── Ort/Datum ───────────────────────────────────────────────────
        // AG hat nur ein Ort/Datum-Feld
        Set(form, "ort_datum", d.OrtDatum);
    }

    // ════════════════════════════════════════════════════════════════════════
    // ZH — Kanton Zürich
    // ════════════════════════════════════════════════════════════════════════
    // Field-Mapping basiert auf dem strukturierten ZH-Template:
    //   ag.*       = Arbeitgeber (unsere Filiale)
    //   ag.weitere = Andere Erwerbstätigkeit (.0 = Slot 1, .1 = Slot 2)
    //   an.p1.*    = Quellensteuerpflichtige Person
    //   an.p2.*    = Partner/in
    //   allg.*     = Allgemein (Ort, Datum, Bemerkungen)
    //
    // Radio-Werte (vom Template vorgegeben):
    //   geschlecht    : "1"=männlich, "2"=weiblich
    //   zivilstand    : "1".."6" — die genaue Reihenfolge ist
    //                   1=ledig, 2=verheiratet, 3=geschieden, 4=verwitwet,
    //                   5=eingetr.Partnerschaft, 6=aufgel.Partnerschaft (Annahme,
    //                   beim ersten Test verifizieren).
    //   konfession    : "1".."5" — typischerweise
    //                   1=ev-ref, 2=röm-kath, 3=christ-kath, 4=andere, 5=keine.
    private static void MapZh(PdfAcroForm form, QstAnmeldungData d)
    {
        // ── Arbeitgeber (Schaub Restaurants) ────────────────────────────
        Set(form, "ag.agnr",     d.SslNummer);
        Set(form, "ag.firma",    d.Firma);
        Set(form, "ag.adresse",  d.Adresse);
        Set(form, "ag.ort",      d.PlzOrtKanton);
        Set(form, "ag.kontakt",  d.Kontaktperson);
        Set(form, "ag.tel",      d.Telefon);
        Set(form, "ag.mail",     d.Email);
        // ag.fax — kennen wir nicht
        // ag.qst.kanton (dropdown) + ag.qst.check — bewusst leer lassen,
        // damit Walter den passenden Eintrag manuell setzen kann.

        // ── Quellensteuerpflichtige Person (an.p1) ──────────────────────
        SetRadio(form, "an.p1.geschlecht", MapGeschlechtZh(d.QaGeschlecht));
        Set(form, "an.p1.ahv",        d.QaSvNummer);
        Set(form, "an.p1.name",       d.QaName);
        Set(form, "an.p1.vorname",    d.QaVorname);
        Set(form, "an.p1.adresse",    d.QaStrasse);
        Set(form, "an.p1.ort",        d.QaPlzOrtLand);
        Set(form, "an.p1.gebdat",     d.QaGeburtsdatum);
        Set(form, "an.p1.nationalitaet", d.QaNationalitaet);
        Set(form, "an.p1.ausweis",    d.QaBewilligung);

        SetRadio(form, "an.p1.zivilstand", MapZivilstandZh(d.Zivilstand));
        SetRadio(form, "an.p1.konfession", MapKonfessionZh(d.Konfession));

        // ZH hat separate Datums-Felder pro Lebenslagen-Ereignis. Aus unseren
        // Daten kennen wir nur das aktuelle Zivilstands-Datum + Trennungsdatum.
        switch (d.Zivilstand)
        {
            case "3":           // geschieden
                Set(form, "an.p1.dat.scheidung", d.DatumZivilstand);
                break;
            case "0": case "1": case "2": case "4": case "5":
                Set(form, "an.p1.dat.heirat", d.DatumZivilstand);
                break;
        }
        // Trennung: wenn Ja → Trennungsdatum würde uns fehlen; wir setzen es nicht.

        Set(form, "an.p1.dat.stellenantritt", d.StellenAntrittDatum);
        Set(form, "an.p1.beruf",      null);   // Stellenbezeichnung — kennen wir aktuell nicht zentral
        Set(form, "an.p1.bruttolohn", d.BruttolohnMonat);
        Set(form, "an.p1.pensum",     d.ArbeitspensumProzent);
        // an.p1.stunden — leer lassen, wir geben Pensum in %
        Set(form, "an.p1.arbeitsort", null);   // kennen wir aktuell nicht zentral

        // Grenzgänger: ZH hat dafür einen eigenen Checkbox-Sub-Group an.p1.grenzgaenger.check
        if (d.BerufFlag == "0") // Grenzgänger
        {
            // Versuche die Box anzukreuzen (mehrere Werte probieren)
            CheckMultivalue(form, "an.p1.grenzgaenger.check", "1", "Yes", "On");
        }
        // Wochenaufenthalter — ZH hat dafür kein eigenes Feld auf der ersten Seite

        Set(form, "an.p1.kinder", d.AnzahlKinder);

        // ── Partner (an.p2) ─────────────────────────────────────────────
        Set(form, "an.p2.ahv",        d.EpSvNummer);
        Set(form, "an.p2.name",       d.EpName);
        Set(form, "an.p2.vorname",    d.EpVorname);
        Set(form, "an.p2.adresse",    d.EpStrasse);
        Set(form, "an.p2.ort",        d.EpPlzOrtLand);
        Set(form, "an.p2.gebdat",     d.EpGeburtsdatum);
        Set(form, "an.p2.nationalitaet", d.EpNationalitaet);
        Set(form, "an.p2.ausweis",    d.EpBewilligung);
        SetRadio(form, "an.p2.erwerbstaetig", d.EpHatErwerbJaNein == "1" ? "1" : (d.EpHatErwerbJaNein == "0" ? "2" : null));
        Set(form, "an.p2.det.ag",         d.EpArbeitgeberName);
        Set(form, "an.p2.det.bruttolohn", null);
        Set(form, "an.p2.det.pensum",     null);

        // ── Andere Erwerbstätigkeit (Slot 0) ────────────────────────────
        Set(form, "ag.weitere.firma.0",   d.AndereArbeitgeberName2 ?? d.AndereArbeitgeberName);
        Set(form, "ag.weitere.adresse.0", d.AndereArbeitgeberStrasse);
        Set(form, "ag.weitere.ort.0",     d.AndereArbeitgeberPlzOrtKanton);

        // ── Allgemein ────────────────────────────────────────────────────
        // ZH hat allg.ort + allg.datum getrennt — wir splitten unser kombiniertes Feld
        var (ort, datum) = SplitOrtDatum(d.OrtDatum);
        Set(form, "allg.ort",   ort);
        Set(form, "allg.datum", datum);
    }

    // ════════════════════════════════════════════════════════════════════════
    // BE — Kanton Bern
    // ════════════════════════════════════════════════════════════════════════
    // Field-Mapping basiert auf POSITIONEN (siehe Position-Analyse), weil
    // alle Text-Felder generisch nummeriert sind (Text00..Text53) und
    // Radio-Gruppen Group2..Group15 heissen. Werte sind "Auswahl1".."Auswahl5"
    // (mit einer Ausnahme bei Group10: "0"/"1").
    //
    // Mapping-Tabelle (oben = unten, links = rechts):
    //   Text01 = Referenz/ZPV-Nr  Text02 = UID  Text03 = Firma
    //   Text04 = Strasse           Text05 = PLZ  Text06 = Kontaktperson
    //   Text07 = Telefon           Text08 = E-Mail
    //   Group2 = AN Geschlecht     Group7 = EP Geschlecht
    //   Text09 = AN ZPV/AHV-Nr     Text25 = EP Vorname
    //   Text10 = AN Vorname        Text26 = EP Name
    //   Text11 = AN Name           Text27 = EP Strasse
    //   Text12 = AN Strasse        Text28 = EP PLZ/Ort/Land
    //   Text13 = AN PLZ/Ort/Land   Text29 = EP Geburtsdatum
    //   Text14 = AN Geburtsdatum   Text30 = EP Nationalität
    //   Text15 = AN Nationalität   Text30-1 = EP Bewilligung
    //   Group3 = AN Zivilstand     Group8 = EP Erwerbstätigkeit ja/nein
    //   Group4 = AN getrennt j/n   Text31 = EP Arbeitgeber
    //   Group10 = AN aufgelöst j/n Text32 = EP Strasse
    //   Text16 = AN Datum Zivilst. Text33 = EP PLZ/Ort/Land
    //   Group5 = AN Konfession     Text34/35/36/37 = Kind 1 (Name+Tag/Mt/Jahr)
    //   Text17 = Aufenthalt Str.   Text38/39/40/41 = Kind 2
    //   Text18 = Aufenthalt PLZ    Text42/43/44/45 = Kind 3
    //   Text20 = Stellenantritt    Text46/47/48/49 = Kind 4
    //   Text21 = Bruttolohn        Group15 = Kinder im Haushalt j/n
    //   Text22/22-1 = Pensum %/Std Text50  = Anzahl Kinder
    //   Group6 = Andere Erwerb j/n Group11 = Konkubinat j/n
    //   Text23 = Andere AG Name    Group12 = Elterl. Sorge j/n
    //   Text24 = Andere AG Str.    Group13 = Unterhalt j/n
    //   Text24-1 = Andere AG PLZ   Group14 = Höh. Einkommen j/n
    //   Text24-2 = Andere AG Land  Text51 = Bemerkungen
    //   Text24-3 = Gesamtpensum %  Text52 = Datum AN-Unterschrift
    //   Text24-4 = Bewilligungsart Text53 = Datum AG-Stempel
    //   Group9 = Grenzg/Wochenauf.
    private static void MapBe(PdfAcroForm form, QstAnmeldungData d)
    {
        // Helper: Standard-Konvention "1"=Ja "0"=Nein → Auswahl1/Auswahl2
        string? jaNein(string? v) => v == "1" ? "Auswahl1" : (v == "0" ? "Auswahl2" : null);
        // Helper: Elterntarif-Konvention "0"=Ja "1"=Nein → Auswahl1/Auswahl2
        string? jaNeinElt(string? v) => v == "0" ? "Auswahl1" : (v == "1" ? "Auswahl2" : null);

        // ── Arbeitgeber-Block ────────────────────────────────────────────
        Set(form, "Text01", d.SslNummer);     // Referenz-/ZPV-Nr.
        Set(form, "Text02", d.UidNummer);
        Set(form, "Text03", d.Firma);
        Set(form, "Text04", d.Adresse);
        Set(form, "Text05", d.PlzOrtKanton);
        Set(form, "Text06", d.Kontaktperson);
        Set(form, "Text07", d.Telefon);
        Set(form, "Text08", d.Email);

        // ── Quellensteuerpflichtige Person ──────────────────────────────
        // Geschlecht: "0"=m → Auswahl1, "1"=w → Auswahl2
        SetRadio(form, "Group2", d.QaGeschlecht == "0" ? "Auswahl1"
                              : d.QaGeschlecht == "1" ? "Auswahl2" : null);
        Set(form, "Text09", d.QaSvNummer);
        Set(form, "Text10", d.QaVorname);
        Set(form, "Text11", d.QaName);
        Set(form, "Text12", d.QaStrasse);
        Set(form, "Text13", d.QaPlzOrtLand);
        Set(form, "Text14", d.QaGeburtsdatum);
        Set(form, "Text15", d.QaNationalitaet);

        // Zivilstand — BE-Reihenfolge:
        //   Auswahl1=ledig | Auswahl2=geschieden | Auswahl3=verwitwet
        //   Auswahl4=verheiratet | Auswahl5=eingetragene Partnerschaft
        // Unsere DTO-Werte sind 0..5 (siehe MapZivilstand im Controller).
        SetRadio(form, "Group3", d.Zivilstand switch
        {
            "0" => "Auswahl1",  // ledig
            "1" => "Auswahl2",  // geschieden
            "2" => "Auswahl3",  // verwitwet
            "3" => "Auswahl4",  // verheiratet
            "4" => "Auswahl5",  // eingetragene Partnerschaft
            _   => null         // aufgelöste Partnerschaft → wird über Group10 ausgedrückt
        });
        SetRadio(form, "Group4",  jaNein(d.GetrenntJaNein));      // verheiratet>getrennt ja/nein
        // Group10 hat eigene Konvention "0"/"1" (statt Auswahl1/2)
        SetRadio(form, "Group10", d.Zivilstand == "5" ? "0" : null);  // eingetr.Part. aufgelöst
        Set(form, "Text16", d.DatumZivilstand);

        // Konfession — Auswahl1..4
        SetRadio(form, "Group5", d.Konfession switch
        {
            "0" => "Auswahl1",   // ev.-reformiert
            "1" => "Auswahl2",   // römisch-katholisch
            "2" => "Auswahl3",   // christ-katholisch
            "3" => "Auswahl4",   // andere/keine
            _   => null
        });

        // Aufenthaltsadresse
        Set(form, "Text17", d.AufenthaltAdresse);
        Set(form, "Text18", d.AufenthaltPlzOrtKanton);

        // Beruf
        Set(form, "Text20",   d.StellenAntrittDatum);
        Set(form, "Text21",   d.BruttolohnMonat);
        Set(form, "Text22",   d.ArbeitspensumProzent);   // %
        // Text22-1 (Std) — leer lassen

        SetRadio(form, "Group6", jaNein(d.HatAndereErwerbJaNein));  // Andere Erwerbstätigkeit ja/nein
        Set(form, "Text23",   d.AndereArbeitgeberName2 ?? d.AndereArbeitgeberName);
        Set(form, "Text24",   d.AndereArbeitgeberStrasse);
        Set(form, "Text24-1", d.AndereArbeitgeberPlzOrtKanton);
        Set(form, "Text24-2", d.AndereArbeitgeberLand);
        Set(form, "Text24-3", d.GesamtPensumProzent);
        Set(form, "Text24-4", d.QaBewilligung);

        // Grenzgänger / Wochenaufenthalter — Group9 mit zwei Optionen
        SetRadio(form, "Group9", d.BerufFlag switch
        {
            "0" => "Auswahl1",   // Grenzgänger
            "1" => "Auswahl2",   // Wochenaufenthalter
            _   => null
        });

        // ── Ehepartner/in ───────────────────────────────────────────────
        SetRadio(form, "Group7", d.EpGeschlecht == "0" ? "Auswahl1"
                              : d.EpGeschlecht == "1" ? "Auswahl2" : null);
        Set(form, "Text25",   d.EpVorname);
        Set(form, "Text26",   d.EpName);
        Set(form, "Text27",   d.EpStrasse);
        Set(form, "Text28",   d.EpPlzOrtLand);
        Set(form, "Text29",   d.EpGeburtsdatum);
        Set(form, "Text30",   d.EpNationalitaet);
        Set(form, "Text30-1", d.EpBewilligung);
        SetRadio(form, "Group8", jaNein(d.EpHatErwerbJaNein));
        Set(form, "Text31",   d.EpArbeitgeberName);
        Set(form, "Text32",   d.EpArbeitgeberStrasse);
        Set(form, "Text33",   d.EpArbeitgeberPlzOrtLand);

        // ── Kinder (4 Slots à 4 Felder) ─────────────────────────────────
        if (d.Kinder != null)
        {
            string[] nameFields = { "Text34", "Text38", "Text42", "Text46" };
            for (int i = 0; i < d.Kinder.Count && i < 4; i++)
                Set(form, nameFields[i], d.Kinder[i]);
        }

        // ── Abklärung Elterntarif (Konvention "0"=Ja "1"=Nein) ──────────
        SetRadio(form, "Group15", jaNeinElt(d.LebenKinderImHaushaltJaNein));
        Set(form, "Text50", d.AnzahlKinder);
        SetRadio(form, "Group11", jaNeinElt(d.LebtImKonkubinatJaNein));
        SetRadio(form, "Group12", jaNeinElt(d.UebtElterlicheSorgeJaNein));
        SetRadio(form, "Group13", jaNeinElt(d.ZahltUnterhaltVolljaehrigJaNein));
        SetRadio(form, "Group14", jaNeinElt(d.HoeheresBruttoEinkommenJaNein));

        // ── Bemerkungen + Ort/Datum ─────────────────────────────────────
        Set(form, "Text51", JoinNonEmpty(" / ", d.Bemerkung1, d.Bemerkung2, d.Bemerkung3));
        // Datum/Unterschrift AN: leer (MA füllt selbst aus)
        Set(form, "Text53", d.OrtDatum);   // Datum/Stempel/Unterschrift AG — vorausgefüllt
    }

    // ── ZH-Helpers ───────────────────────────────────────────────────────
    private static string? MapGeschlechtZh(string? g) => g switch
    {
        "0" => "1",   // männlich
        "1" => "2",   // weiblich
        _   => null
    };

    private static string? MapZivilstandZh(string? z) => z switch
    {
        // SO-Werte → ZH-Werte. Reihenfolge im ZH-Formular Annahme:
        //   1=ledig, 2=verheiratet, 3=geschieden, 4=verwitwet,
        //   5=eingetr.Partnerschaft, 6=aufgel.Partnerschaft
        // (nach erstem Test ggf. anpassen)
        "0" => "1",   // ledig
        "3" => "2",   // verheiratet
        "1" => "3",   // geschieden
        "2" => "4",   // verwitwet
        "4" => "5",   // eingetragene Partnerschaft
        "5" => "6",   // aufgelöste Partnerschaft
        _   => null
    };

    private static string? MapKonfessionZh(string? k) => k switch
    {
        "0" => "1",   // ev.-reformiert
        "1" => "2",   // römisch-katholisch
        "2" => "3",   // christ-katholisch
        "3" => "4",   // andere
        _   => null
    };

    /// <summary>
    /// Splittet "Ort, Datum" → ("Ort", "Datum"). Falls kein Komma → ganzer
    /// String als Datum (Ort wird leer).
    /// </summary>
    private static (string? ort, string? datum) SplitOrtDatum(string? combined)
    {
        if (string.IsNullOrWhiteSpace(combined)) return (null, null);
        var idx = combined.IndexOf(',');
        if (idx < 0) return (null, combined.Trim());
        return (combined.Substring(0, idx).Trim(), combined.Substring(idx + 1).Trim());
    }

    private static string JoinNonEmpty(string sep, params string?[] parts) =>
        string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>
    /// Versucht eine Checkbox/Button mit verschiedenen On-Werten zu setzen,
    /// falls "Yes" nicht funktioniert (manche PDFs verwenden "1", "On" etc.).
    /// </summary>
    private static void CheckMultivalue(PdfAcroForm form, string fieldName, params string[] candidates)
    {
        var field = form.GetField(fieldName);
        if (field is null) return;
        foreach (var c in candidates)
        {
            try { field.SetValue(c); return; }
            catch { /* nächsten Wert probieren */ }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────
    private static void Set(PdfAcroForm form, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = form.GetField(fieldName);
        if (field is null) return;
        field.SetValue(value);
    }

    /// <summary>
    /// Setzt einen Radio-Button (Gruppe). Wert ist der Index/Name der Option.
    /// </summary>
    private static void SetRadio(PdfAcroForm form, string fieldName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = form.GetField(fieldName);
        if (field is null) return;
        field.SetValue(value);
    }

    /// <summary>
    /// Setzt eine einzelne Checkbox auf "an". Häufige On-Werte sind "Yes"
    /// oder der Feldname selbst — iText probiert das richtige Appearance
    /// zu finden, wenn man den Standard-Wert "Yes" übergibt.
    /// </summary>
    private static void Check(PdfAcroForm form, string fieldName)
    {
        var field = form.GetField(fieldName);
        if (field is null) return;
        field.SetValue("Yes");
    }

    /// <summary>
    /// Hilfsmethode für Elterntarif-Felder im AG-PDF: zwei separate Checkboxen
    /// (Ja-Box, Nein-Box). DTO-Konvention für Elterntarif: "0" = Ja, "1" = Nein
    /// (siehe Controller-Konstanten Elt_Ja/Elt_Nein).
    /// </summary>
    private static void SetJaNeinElt(PdfAcroForm form, string jaField, string neinField, string? value)
    {
        if (value == "0") Check(form, jaField);
        else if (value == "1") Check(form, neinField);
    }
}

// ── DTO ──────────────────────────────────────────────────────────────────────

public class QstAnmeldungData
{
    // Arbeitgeber
    public string? SslNummer        { get; set; }
    public string? UidNummer        { get; set; }
    public string? Firma            { get; set; }
    public string? Adresse          { get; set; }
    public string? PlzOrtKanton     { get; set; }
    public string? Kontaktperson    { get; set; }
    public string? Telefon          { get; set; }
    public string? Email            { get; set; }

    // Arbeitnehmer/in
    public string? QaGeschlecht       { get; set; } // "0"=männlich, "1"=weiblich
    public string? QaName             { get; set; }
    public string? QaVorname          { get; set; }
    public string? QaStrasse          { get; set; }
    public string? QaPlzOrtLand       { get; set; }
    public string? QaGeburtsdatum     { get; set; }
    public string? QaNationalitaet    { get; set; }
    public string? QaBewilligung      { get; set; }
    public string? QaSvNummer         { get; set; }

    // Zivilstand
    public string? Zivilstand         { get; set; } // "0".."5"
    public string? GetrenntJaNein     { get; set; } // "1"=Ja "0"=Nein
    public string? DatumZivilstand    { get; set; }
    public string? Konfession         { get; set; } // "0"-"3"

    // Aufenthaltsadresse
    public string? AufenthaltAdresse        { get; set; }
    public string? AufenthaltPlzOrtKanton   { get; set; }

    // Beruf
    public string? StellenAntrittDatum  { get; set; }
    public string? BruttolohnMonat      { get; set; }
    public string? ArbeitspensumProzent { get; set; }
    public string? BerufFlag            { get; set; } // "0"=Grenzgänger, "1"=Wochenaufenthalter

    // Andere Erwerbstätigkeit
    public string? HatAndereErwerbJaNein            { get; set; }
    public string? AndereArbeitgeberName            { get; set; }
    public string? AndereArbeitgeberName2           { get; set; }
    public string? AndereArbeitgeberStrasse         { get; set; }
    public string? AndereArbeitgeberPlzOrtKanton    { get; set; }
    public string? AndereArbeitgeberLand            { get; set; }
    public string? GesamtPensumProzent              { get; set; }

    // Ehepartner
    public string? EpGeschlecht       { get; set; }
    public string? EpName             { get; set; }
    public string? EpVorname          { get; set; }
    public string? EpStrasse          { get; set; }
    public string? EpPlzOrtLand       { get; set; }
    public string? EpGeburtsdatum     { get; set; }
    public string? EpNationalitaet    { get; set; }
    public string? EpBewilligung      { get; set; }
    public string? EpSvNummer         { get; set; }
    public string? EpHatErwerbJaNein  { get; set; }
    public string? EpArbeitgeberName       { get; set; }
    public string? EpArbeitgeberStrasse    { get; set; }
    public string? EpArbeitgeberPlzOrtLand { get; set; }

    // Kinder + Elterntarif
    public string? AnzahlKinder       { get; set; }
    public List<string>? Kinder       { get; set; }
    public string? LebenKinderImHaushaltJaNein     { get; set; }
    public string? LebtImKonkubinatJaNein          { get; set; }
    public string? UebtElterlicheSorgeJaNein       { get; set; }
    public string? ZahltUnterhaltVolljaehrigJaNein { get; set; }
    public string? HoeheresBruttoEinkommenJaNein   { get; set; }

    // Bemerkungen + Unterschrift
    public string? Bemerkung1   { get; set; }
    public string? Bemerkung2   { get; set; }
    public string? Bemerkung3   { get; set; }
    public string? OrtDatum     { get; set; }
}
