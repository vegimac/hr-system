using System.Text.RegularExpressions;
using iText.Forms;
using iText.IO.Font.Constants;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.Kernel.Utils;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace HrSystem.Services;

/// <summary>
/// Kündigungsschreiben (Walter-Vorgabe 22.06.2026). Formeller Geschäftsbrief im
/// Haus-Stil (gelber Briefkopf): Absender-Filiale, Empfänger-MA, Ort/Datum,
/// Betreff, Text mit Kündigungsfrist + letztem Arbeitstag, optional Grund,
/// Unterschrift des eingeloggten Users (Bild + Klarname).
/// </summary>
public class KuendigungPdfService
{
    private const string Dark = "#1a1a1a";

    private static readonly byte[] BannerBytes =
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "letterhead_banner.png"));

    public record KuendigungData(
        // Arbeitgeber / Filiale
        string? FirmaName, string? FirmaStrasse, string? FirmaPlzOrt,
        // Mitarbeitende/r
        string? MaName, string? MaStrasse, string? MaPlzOrt,
        string  Briefanrede,          // "Sehr geehrte Frau Muster" / "Sehr geehrter Herr Muster"
        // Brief
        string  Ort, DateOnly KuendigungsDatum,
        string  FristText,            // z.B. "2 Monaten auf Ende eines Monats" / "7 Tagen"
        DateOnly LetzterArbeitstag,
        string? Grund,                // optional, sonst null
        string? UnterzeichnerName,
        bool    Eingeschrieben = false,    // true = EINSCHREIBEN; false = Übergeben (Aushändigung)
        string? UnterzeichnerFunktion = null);  // z.B. «HR-Verantwortliche» (user_branch_access.FunctionTitle)

    /// <summary>
    /// Rückzug einer ausgesprochenen Kündigung (Walter-Vorgabe 16.07.2026) —
    /// z.B. wegen nachträglich gemeldeter Schwangerschaft (Sperrfrist OR 336c).
    /// Rechtlich braucht der Rückzug das Einverständnis der/des MA → der Brief
    /// enthält unten einen Einverständnis-Block mit Unterschriftszeile.
    /// </summary>
    public record RueckzugData(
        string? FirmaName, string? FirmaStrasse, string? FirmaPlzOrt,
        string? MaName, string? MaStrasse, string? MaPlzOrt,
        string  Briefanrede,
        string  Ort, DateOnly Datum,
        DateOnly KuendigungVom,          // Datum der urspruenglichen Kuendigung
        string? Grund,                   // optionaler Rueckzugs-Grund
        string? UnterzeichnerName,
        string? UnterzeichnerFunktion = null,   // z.B. «HR-Verantwortliche»
        bool    Eingeschrieben = false,
        // Schwangerschafts-Variante (Walter-Text 16.07.2026): die Kuendigung
        // ist nach OR 336c NICHTIG — Bestaetigungs-Brief «Fortbestehen des
        // Arbeitsverhaeltnisses», KEIN Einverstaendnis-Block noetig.
        bool    NichtigSchwangerschaft = false,
        DateOnly? SchwangerschaftGemeldetAm = null);

    /// <summary>
    /// Kündigungsbestätigung (Walter 26.07.2026) — wenn der MA kündigt,
    /// bestätigt der AG den Erhalt und das Vertragsende. Vorlage:
    /// «Kündigungsbestätigung» (Du-Form, inkl. Austritts-Fragebogen-QR).
    /// Seite 2 = Referenzangaben · Seite 3 = Swica · Seiten 4–5 = GastroSocial
    /// «Überweisung Pensionskassenguthaben» (AcroForm vorausgefüllt).
    /// </summary>
    public record BestaetigungData(
        string? FirmaName, string? RestaurantName, string? FirmaStrasse, string? FirmaPlzOrt,
        string? MaName, string  MaVorname, string MaNachname, string? MaStrasse, string? MaPlzOrt,
        string  DuAnrede,                 // «Liebe Tiyara» / «Lieber Max»
        string  Ort, DateOnly Datum,      // Briefdatum
        DateOnly KuendigungsDatumMa,      // Kündigungsdatum des Mitarbeitenden
        DateOnly KuendigungAuf,           // Kündigung auf Datum (= letzter Tag)
        string? UnterzeichnerName,
        string? UnterzeichnerFunktion = null,
        bool    Eingeschrieben = false,
        string? ExitSurveyUrl = null,     // öffentliche URL des eigenen Fragebogens (QR)
        // PK-Überweisung (Seiten 4–5) — Stammdaten aus dem MA-Dossier
        string? MaAhvNummer = null,
        DateOnly? MaGeburtsdatum = null,
        string? MaTelefon = null,
        string? MaEmail = null,
        string? MaLand = null,
        string? MaZivilstand = null,
        DateOnly? MaZivilstandSeit = null);

    /// <summary>Fallback, falls SiteUrl nicht geladen werden kann.</summary>
    public const string DefaultExitSurveyUrl = "https://onecrew.ch/kuendigung/";

    private static readonly string SwicaTemplatePath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Forms", "Swica_Obligatorische_Mitarbeiter_Information.pdf");

    private static readonly string PkUeberweisungTemplatePath =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Forms", "Ueberweisung_PK_Guthaben.pdf");

    public byte[] GenerateBestaetigung(BestaetigungData d)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var firmaLines = new[] { d.FirmaName, d.RestaurantName, d.FirmaStrasse, d.FirmaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        var maLines = new[] { d.MaName, d.MaStrasse, d.MaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

        var surveyUrl = string.IsNullOrWhiteSpace(d.ExitSurveyUrl)
            ? DefaultExitSurveyUrl
            : d.ExitSurveyUrl!.Trim();

        byte[] qrPng;
        using (var qrGen = new QRCodeGenerator())
        using (var qrData = qrGen.CreateQrCode(surveyUrl, QRCodeGenerator.ECCLevel.M))
            qrPng = new PngByteQRCode(qrData).GetGraphic(4);

        // Seiten 1–2 im Haus-Briefstil (QuestPDF), Seite 3 = Swica-Original
        // (Overlay), Seiten 4–5 = GastroSocial PK-Überweisung (AcroForm).
        var briefPages = Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                // Grosszügiger Brief wie Kündigung/Rückzug (Walter 26.07.2026):
                // Adresse im C5-Fenstercouvert, Unterschrift mit Luft ganz unten.
                // QR neben Fragebogen-Text, damit Seite 1 trotzdem eine bleibt.
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.1f, Unit.Centimetre);
                page.MarginHorizontal(2.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).FontColor(Dark).LineHeight(1.32f));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    foreach (var ln in firmaLines)
                        col.Item().Text(ln).FontSize(8.5f).FontColor("#475569");

                    // MA-Adresse im COUVERT-FENSTER (wie Kündigung/Rückzug):
                    // Schweizer C5-Fenster links beginnt ~4.5 cm ab Papierkante.
                    // Bis hier ~3.0 cm (1 cm Rand + Banner + Abstände) —
                    // fixer Abstandhalter schiebt den Adressblock in die Zone.
                    col.Item().Height(40);
                    if (d.Eingeschrieben)
                        col.Item().Text("EINSCHREIBEN").Bold().LetterSpacing(0.06f).FontSize(9.5f);
                    col.Item().PaddingTop(d.Eingeschrieben ? 3 : 16).Column(c =>
                    {
                        foreach (var ln in maLines) c.Item().Text(ln);
                    });

                    col.Item().PaddingTop(24).Text($"{d.Ort}, {d.Datum:dd.MM.yyyy}");

                    col.Item().PaddingTop(18).Text("Kündigungsbestätigung").Bold().FontSize(12.5f);

                    col.Item().PaddingTop(14).Text($"{d.DuAnrede},");

                    col.Item().PaddingTop(12).Text(t =>
                    {
                        t.Span("Hiermit bestätigen wir den Erhalt deiner Kündigung vom ");
                        t.Span($"{d.KuendigungsDatumMa:dd.MM.yyyy}").Bold();
                        t.Span(" und das Ende unseres Arbeitsverhältnisses gemäss Kündigungsfrist auf den ");
                        t.Span($"{d.KuendigungAuf:dd.MM.yyyy}").Bold();
                        t.Span(".");
                    });

                    col.Item().PaddingTop(10).Text(
                        "Alle Gegenstände, die in deinem Besitz sind und dem Unternehmen gehören, müssen vor deinem Austreten deinem Vorgesetzten überreicht werden. Wir erinnern dich ebenfalls daran, dass du an die Geheimhaltungspflicht gebunden bist.");

                    col.Item().PaddingTop(10).Text(
                        "Im Anhang senden wir dir von der Swica das Informationsblatt «Taggeldversicherung und Unfallversicherung». Wenn du dieses Formular nicht zurücksendest, gehen wir davon aus, dass du von uns in Kenntnis gesetzt wurdest und wir von jeglicher Verantwortlichkeit entlassen sind.");

                    col.Item().PaddingTop(10).Text(
                        "Um dein BVG-Guthaben (2. Säule) an die Kasse deines neuen Arbeitgebers oder auf ein Freizügigkeitskonto zu überweisen, fülle bitte das beiliegende Formular «Überweisung Pensionskassenguthaben» aus und sende es direkt an GastroSocial.");

                    col.Item().PaddingTop(10).Text(
                        "Dein Arbeitszeugnis erhältst du so bald wie möglich.");

                    // Fragebogen + QR nebeneinander (hält Seite 1 auf einer Seite).
                    col.Item().PaddingTop(12).Row(r =>
                    {
                        r.RelativeItem().PaddingRight(12).AlignMiddle().Text(
                            "Damit wir uns als Arbeitgeber weiterhin verbessern können, sind wir auf deine Hilfe angewiesen. Um deine Gründe für die Kündigung besser zu verstehen, wären wir dir sehr dankbar, wenn du den kurzen Fragebogen mit dem QR-Code ausfüllen würdest. Deine Antworten bleiben anonym. Scanne den Code mit deinem Smartphone.");
                        r.ConstantItem(66).Column(c =>
                        {
                            c.Item().Width(60).Height(60).Image(qrPng).FitArea();
                            c.Item().PaddingTop(2).AlignCenter()
                                .Text("Fragebogen")
                                .FontSize(8f).FontColor("#475569");
                        });
                    });
                });

                // Schlusswunsch + Gruss + Unterschriftsraum ganz unten
                // (Footer = Seitenende; Adresse bleibt im Fenstercouvert).
                page.Footer().Column(col =>
                {
                    col.Item().Text(
                        "Wir wünschen dir einen guten Abschluss bei McDonald's und viel Erfolg und Zufriedenheit in deiner Zukunft. Wir danken dir herzlich für deinen Einsatz in unserem McDonald's.");
                    col.Item().PaddingTop(12).Text("Freundliche Grüsse");
                    if (!string.IsNullOrWhiteSpace(d.FirmaName))
                        col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();
                    if (!string.IsNullOrWhiteSpace(d.RestaurantName))
                        col.Item().Text(d.RestaurantName!);
                    col.Item().PaddingTop(6).Height(56);
                    col.Item().Text(d.UnterzeichnerName ?? "");
                    if (!string.IsNullOrWhiteSpace(d.UnterzeichnerFunktion))
                        col.Item().Text(d.UnterzeichnerFunktion!).FontColor("#475569");
                });
            });

            // Seite 2: Referenzangaben — Haus-Layout, vorausgefüllte Stammdaten
            // (Walter 26.07.2026). Ankreuzen + Unterschrift macht der MA.
            doc.Page(page => ComposeReferenzangabenPage(page, d));
        }).GeneratePdf();

        var swicaPage = StampSwicaPage(d);
        var pkPages = FillPkUeberweisungPages(d);
        return MergePdfs(new[] { briefPages, swicaPage, pkPages });
    }

    /// <summary>
    /// Seite 2 der Kündigungsbestätigung: Formular «Referenzangaben»
    /// im gelben Briefkopf-Stil. Name/Vorname des MA sowie Vertreter
    /// (Unterzeichner + Funktion) sind vorausgefüllt; Checkboxen und
    /// MA-Unterschrift bleiben leer. Abschnitte gleichmässig verteilt,
    /// Ort/Datum + Unterschrift im Footer ganz unten.
    /// </summary>
    private static void ComposeReferenzangabenPage(PageDescriptor page, BestaetigungData d)
    {
        page.Size(PageSizes.A4);
        page.MarginTop(1.0f, Unit.Centimetre);
        page.MarginBottom(1.4f, Unit.Centimetre);
        page.MarginHorizontal(2.2f, Unit.Centimetre);
        page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).FontColor(Dark).LineHeight(1.38f));

        page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

        page.Content().PaddingTop(24).Column(col =>
        {
            col.Item().Text("Referenzangaben").Bold().FontSize(15f);
            col.Item().PaddingTop(6).Text("Bitte eine Option ankreuzen und unterschreiben.")
                .FontSize(9.5f).FontColor("#64748b");

            // Block 1 — Person
            col.Item().PaddingTop(30).BorderBottom(0.6f).BorderColor("#cbd5e1").PaddingBottom(18).Column(c =>
            {
                c.Item().Text("Der/die Unterzeichnende").SemiBold().FontSize(11f);
                c.Item().PaddingTop(16).Element(e => FormField(e, "Name", d.MaNachname));
                c.Item().PaddingTop(12).Element(e => FormField(e, "Vorname", d.MaVorname));
            });

            // Block 2 — Option A
            col.Item().PaddingTop(28).Border(0.7f).BorderColor("#cbd5e1").Padding(16).Row(r =>
            {
                r.ConstantItem(26).Element(CheckBox);
                r.RelativeItem().Column(c =>
                {
                    c.Item().Text("erlaubt McDonald's Schweiz, vertreten durch");
                    c.Item().PaddingTop(14).Element(e =>
                        FormField(e, "Name, Vorname", (d.UnterzeichnerName ?? "").Trim()));
                    c.Item().PaddingTop(10).Element(e =>
                        FormField(e, "Funktion", (d.UnterzeichnerFunktion ?? "").Trim()));
                    c.Item().PaddingTop(14).Text("Referenzen über ihn/sie zu geben.");
                });
            });

            // Block 3 — Option B
            col.Item().PaddingTop(18).Border(0.7f).BorderColor("#cbd5e1").Padding(16).Row(r =>
            {
                r.ConstantItem(26).Element(CheckBox);
                r.RelativeItem().PaddingTop(1)
                    .Text("erlaubt nicht, dass McDonald's Schweiz Referenzen über ihn/sie gibt.");
            });
        });

        // Ort/Datum + Unterschrift ganz unten — Seite wirkt ausgewogen.
        page.Footer().PaddingTop(8).Column(col =>
        {
            col.Item().PaddingBottom(10).Text("Ort / Datum und Unterschrift des/der Mitarbeitenden")
                .FontSize(9f).FontColor("#64748b");
            col.Item().Row(r =>
            {
                r.RelativeItem().Column(c =>
                {
                    c.Item().BorderBottom(0.8f).BorderColor(Dark).Height(30);
                    c.Item().PaddingTop(5).Text("Ort, Datum").FontSize(8.5f).FontColor("#64748b");
                });
                r.ConstantItem(40);
                r.RelativeItem().Column(c =>
                {
                    c.Item().BorderBottom(0.8f).BorderColor(Dark).Height(30);
                    c.Item().PaddingTop(5).Text("Unterschrift").FontSize(8.5f).FontColor("#64748b");
                });
            });
        });
    }

    private static void CheckBox(IContainer e) =>
        e.PaddingTop(1).Width(14).Height(14).Border(1.15f).BorderColor(Dark);

    private static void FormField(IContainer e, string label, string value)
    {
        e.Row(r =>
        {
            r.ConstantItem(118).AlignMiddle()
                .Text(label + " :").FontSize(10.5f).FontColor("#64748b");
            r.RelativeItem().AlignMiddle().Column(c =>
            {
                c.Item().MinHeight(18).Text(string.IsNullOrWhiteSpace(value) ? " " : value)
                    .FontSize(11f).FontColor(Dark);
                c.Item().BorderBottom(0.75f).BorderColor("#94a3b8");
            });
        });
    }

    /// <summary>
    /// Offizielles Swica-Blatt «Obligatorische Mitarbeiter-Information»
    /// als Seite 3 — Original-PDF unverändert, Felder per Overlay vorausgefüllt
    /// (Name, Vorname, Datum, Name des versicherten Betriebes). Unterschrift leer.
    /// Koordinaten aus der Vorlage vermessen (pdfplumber, top-basiert).
    /// </summary>
    private static byte[] StampSwicaPage(BestaetigungData d)
    {
        using var ms = new MemoryStream();
        using (var pdf = new PdfDocument(new PdfReader(SwicaTemplatePath), new PdfWriter(ms)))
        {
            var page = pdf.GetPage(1);
            var canvas = new PdfCanvas(page);
            var font = PdfFontFactory.CreateFont(StandardFonts.HELVETICA);
            float pageH = page.GetPageSize().GetHeight();

            // Unterlinien (top vom oberen Rand): Name 687.8 · Vorname 715.8 ·
            // Datum 743.8 · Unterschrift 771.8 · Betrieb 799.8.
            // Werte rechts vom Label, knapp UEBER der Linie (Walter 26.07.2026:
            // etwas weiter nach oben). Unterschrift bleibt leer.
            void Text(string? t, float lineTop, float x, float size = 10.5f)
            {
                if (string.IsNullOrWhiteSpace(t)) return;
                canvas.BeginText()
                      .SetFontAndSize(font, size)
                      .SetColor(ColorConstants.BLACK, true)
                      .MoveText(x, pageH - lineTop + 7f)
                      .ShowText(t.Trim())
                      .EndText();
            }

            Text(d.MaNachname, 687.8f, 95f);
            Text(d.MaVorname, 715.8f, 105f);
            Text(d.Datum.ToString("dd.MM.yyyy"), 743.8f, 100f);
            // Volle Betriebsangabe: Firma · Filiale · Strasse · PLZ Ort
            var betrieb = string.Join(", ", new[]
            {
                d.FirmaName, d.RestaurantName, d.FirmaStrasse, d.FirmaPlzOrt
            }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()));
            // Etwas kleiner, damit Firma + Filiale + Adresse auf die Zeile passen.
            Text(betrieb, 799.8f, 195f, 8.5f);
        }
        return ms.ToArray();
    }

    /// <summary>
    /// GastroSocial «Überweisung Pensionskassenguthaben» als Seiten 4–5 —
    /// Original-AcroForm, vorausgefüllt wo möglich. Ziel-PK / Freizügigkeit
    /// und MA-Unterschrift bleiben leer (füllt der MA). Nach dem Füllen
    /// Flatten, damit der Merge mit den Brief-Seiten sauber bleibt.
    /// </summary>
    private static byte[] FillPkUeberweisungPages(BestaetigungData d)
    {
        using var ms = new MemoryStream();
        using (var pdf = new PdfDocument(new PdfReader(PkUeberweisungTemplatePath), new PdfWriter(ms)))
        {
            var form = PdfAcroForm.GetAcroForm(pdf, false);
            if (form != null)
            {
                form.SetNeedAppearances(true);

                // Datenfelder deutlich groesser (Vorlage hat Arial 0 = Auto-Shrink).
                // AHV bewusst ausgenommen — feste Ziffern-Box mit MaxLen 10.
                const float dataFont = 14f;
                SetAcro(form, "Name", d.MaNachname, dataFont);
                SetAcro(form, "Vorname", d.MaVorname, dataFont);
                SetAcro(form, "AHV-Nummer", FormatAhvForPkForm(d.MaAhvNummer)); // kleine Schrift
                if (d.MaGeburtsdatum.HasValue)
                    SetAcro(form, "Geburtsdatum", d.MaGeburtsdatum.Value.ToString("dd.MM.yyyy"), dataFont);
                SetAcro(form, "Strasse_Nummer", d.MaStrasse, dataFont);
                SetAcro(form, "PLZ, Ort", d.MaPlzOrt, dataFont);
                SetAcro(form, "Land", FormatLandForPkForm(d.MaLand), dataFont);
                SetAcro(form, "Telefon", d.MaTelefon, dataFont);
                SetAcro(form, "E-Mail", d.MaEmail, dataFont);

                var (zivilCode, eheDatum, partnerDatum) = MapZivilstandForPkForm(
                    d.MaZivilstand, d.MaZivilstandSeit);
                if (zivilCode != null)
                    SetAcroRadio(form, "Zivilstand", zivilCode);
                SetAcro(form, "Datum_Eheschliessung", eheDatum, dataFont);
                SetAcro(form, "Datum_Partnerschaft", partnerDatum, dataFont);

                SetAcro(form, "Letzter_Arbeitgeber_Name", d.FirmaName, dataFont);
                // Filial-Adresse untereinander:
                // Zeile 1 = Filiale · Zeile 2 = Strasse · (PLZ Ort folgt auf Zeile 2
                // mit Komma — nur 2 Adress-Felder im Formular).
                if (!string.IsNullOrWhiteSpace(d.RestaurantName))
                {
                    SetAcro(form, "Letzter_Arbeitgeber_Adresse", d.RestaurantName!.Trim(), dataFont);
                    var street = (d.FirmaStrasse ?? "").Trim();
                    var plzOrt = (d.FirmaPlzOrt ?? "").Trim();
                    var adr2 = !string.IsNullOrWhiteSpace(street) && !string.IsNullOrWhiteSpace(plzOrt)
                        ? $"{street}, {plzOrt}"
                        : (string.IsNullOrWhiteSpace(street) ? plzOrt : street);
                    SetAcro(form, "Letzter_Arbeitgeber_Adresse_2", adr2, dataFont);
                }
                else
                {
                    SetAcro(form, "Letzter_Arbeitgeber_Adresse", d.FirmaStrasse, dataFont);
                    SetAcro(form, "Letzter_Arbeitgeber_Adresse_2", d.FirmaPlzOrt, dataFont);
                }

                SetAcro(form, "Austrittsdatum", d.KuendigungAuf.ToString("dd.MM.yyyy"), dataFont);

                // Ort/Datum unten LEER — der MA fuellt es beim Unterschreiben aus
                // (Walter 26.07.2026).

                try { form.FlattenFields(); }
                catch { /* kein Showstopper — Merge funktioniert trotzdem */ }
            }
        }
        return ms.ToArray();
    }

    /// <summary>
    /// AHV-Feld der GastroSocial-Vorlage beginnt NACH dem vorgedruckten «756»
    /// und hat MaxLen=10 — also genau die 10 Rest-Ziffern ohne Punkte
    /// (die Vorlage setzt die Trennzeichen optisch selbst).
    /// </summary>
    private static string? FormatAhvForPkForm(string? ahv)
    {
        if (string.IsNullOrWhiteSpace(ahv)) return null;
        var digits = Regex.Replace(ahv, @"\D", "");
        if (digits.Length == 13 && digits.StartsWith("756"))
            digits = digits[3..];
        if (digits.Length >= 10) return digits[..10];
        return digits.Length > 0 ? digits : null;
    }

    private static string? FormatLandForPkForm(string? land)
    {
        if (string.IsNullOrWhiteSpace(land)) return "Schweiz";
        var t = land.Trim();
        if (t.Equals("CH", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Switzerland", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Suisse", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Svizzera", StringComparison.OrdinalIgnoreCase))
            return "Schweiz";
        return t;
    }

    /// <summary>
    /// GastroSocial-Radio «Zivilstand»: 1 ledig · 2 geschieden · 3 verwitwet ·
    /// 4 aufgelöste Partnerschaft · 5 verheiratet · 6 eingetragene Partnerschaft.
    /// «getrennt» zählt rechtlich als verheiratet.
    /// </summary>
    private static (string? code, string? eheDatum, string? partnerDatum) MapZivilstandForPkForm(
        string? zivilstand, DateOnly? seit)
    {
        var s = (zivilstand ?? "").Trim().ToLowerInvariant();
        var seitTxt = seit?.ToString("dd.MM.yyyy");
        return s switch
        {
            "ledig" => ("1", null, null),
            "geschieden" => ("2", null, null),
            "verwitwet" => ("3", null, null),
            "aufgeloeste_partnerschaft" or "aufgelöste_partnerschaft"
                or "aufgeloeste partnerschaft" or "aufgelöste partnerschaft"
                => ("4", null, null),
            "verheiratet" or "getrennt" => ("5", seitTxt, null),
            "eingetragene_partnerschaft" or "eingetragene partnerschaft"
                => ("6", null, seitTxt),
            _ => (null, null, null)
        };
    }

    private static void SetAcro(PdfAcroForm form, string fieldName, string? value, float? fontSize = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var field = form.GetField(fieldName);
        if (field is null) return;
        if (fontSize.HasValue)
        {
            try { field.SetFontSize(fontSize.Value); }
            catch { /* Fallback: Vorlagen-Default */ }
        }
        field.SetValue(value.Trim());
    }

    private static void SetAcroRadio(PdfAcroForm form, string fieldName, string value)
    {
        var field = form.GetField(fieldName);
        if (field is null) return;
        field.SetValue(value);
    }

    private static byte[] MergePdfs(IEnumerable<byte[]> pdfBytesList)
    {
        var list = pdfBytesList.Where(b => b is { Length: > 0 }).ToList();
        if (list.Count == 0) return Array.Empty<byte>();
        if (list.Count == 1) return list[0];

        using var output = new MemoryStream();
        var writer = new PdfWriter(output);
        using (var target = new PdfDocument(writer))
        {
            var merger = new PdfMerger(target);
            foreach (var bytes in list)
            {
                using var src = new MemoryStream(bytes);
                using var srcDoc = new PdfDocument(new PdfReader(src));
                merger.Merge(srcDoc, 1, srcDoc.GetNumberOfPages());
            }
        }
        return output.ToArray();
    }

    public byte[] GenerateRueckzug(RueckzugData d, byte[]? signaturePng)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var firmaLines = new[] { d.FirmaName, d.FirmaStrasse, d.FirmaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        var maLines = new[] { d.MaName, d.MaStrasse, d.MaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.3f, Unit.Centimetre);
                page.MarginHorizontal(2.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(10.5f).FontColor(Dark).LineHeight(1.4f));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    foreach (var ln in firmaLines)
                        col.Item().Text(ln).FontSize(8.5f).FontColor("#475569");

                    // MA-Adresse im COUVERT-FENSTER (Walter 16.07.2026): Schweizer
                    // C5-Fenster links beginnt ~4.5 cm ab Papierkante. Bis hier
                    // sind es ~3.0 cm (1 cm Rand + 1.1 cm Banner + Abstaende) —
                    // fixer Abstandhalter schiebt den Adressblock in die Zone.
                    col.Item().Height(40);
                    if (d.Eingeschrieben)
                        col.Item().Text("EINSCHREIBEN").Bold().LetterSpacing(0.06f).FontSize(9.5f);
                    col.Item().PaddingTop(d.Eingeschrieben ? 3 : 16).Column(c =>
                    {
                        foreach (var ln in maLines) c.Item().Text(ln);
                    });

                    col.Item().PaddingTop(34).Text($"{d.Ort}, {d.Datum:dd.MM.yyyy}");

                    if (d.NichtigSchwangerschaft)
                    {
                        // Walter-Textvorschlag 16.07.2026: nachtraeglich gemeldete
                        // Schwangerschaft → Kuendigung nichtig (OR 336c).
                        col.Item().PaddingTop(34).Text($"Kündigung vom {d.KuendigungVom:dd.MM.yyyy} – Fortbestehen des Arbeitsverhältnisses")
                            .Bold().FontSize(12.5f);

                        col.Item().PaddingTop(26).Text($"{d.Briefanrede},");

                        col.Item().PaddingTop(18).Text(t =>
                        {
                            t.Span("Sie haben uns");
                            if (d.SchwangerschaftGemeldetAm.HasValue)
                            {
                                t.Span(" am ");
                                t.Span($"{d.SchwangerschaftGemeldetAm.Value:dd.MM.yyyy}").Bold();
                            }
                            t.Span(" darüber informiert, dass Sie schwanger sind und die Schwangerschaft bereits zum Zeitpunkt der Zustellung unserer Kündigung vom ");
                            t.Span($"{d.KuendigungVom:dd.MM.yyyy}").Bold();
                            t.Span(" bestanden hat.");
                        });

                        col.Item().PaddingTop(18).Text(
                            "Gemäss Art. 336c OR ist eine nach Ablauf der Probezeit während der Schwangerschaft ausgesprochene Kündigung durch den Arbeitgeber nichtig.");

                        col.Item().PaddingTop(18).Text(t =>
                        {
                            t.Span("Wir bestätigen Ihnen deshalb, dass unsere Kündigung vom ");
                            t.Span($"{d.KuendigungVom:dd.MM.yyyy}").Bold();
                            t.Span(" keine Rechtswirkung entfaltet. Ihr Arbeitsverhältnis besteht ohne Unterbruch und zu den bisherigen vertraglichen Bedingungen weiter.");
                        });

                        col.Item().PaddingTop(18).Text(
                            "Sämtliche Rechte und Pflichten aus dem Arbeitsverhältnis bleiben unverändert bestehen.");

                        col.Item().PaddingTop(18).Text(
                            "Wir entschuldigen uns für die entstandene Unsicherheit.");
                    }
                    else
                    {
                        col.Item().PaddingTop(34).Text($"Rückzug unserer Kündigung vom {d.KuendigungVom:dd.MM.yyyy}")
                            .Bold().FontSize(12.5f);

                        col.Item().PaddingTop(26).Text($"{d.Briefanrede},");

                        col.Item().PaddingTop(18).Text(t =>
                        {
                            t.Span("hiermit ziehen wir die Ihnen gegenüber am ");
                            t.Span($"{d.KuendigungVom:dd.MM.yyyy}").Bold();
                            t.Span(" ausgesprochene Kündigung des Arbeitsverhältnisses zurück.");
                        });

                        if (!string.IsNullOrWhiteSpace(d.Grund))
                            col.Item().PaddingTop(18).Text($"Grund des Rückzugs: {d.Grund}");

                        col.Item().PaddingTop(18).Text(
                            "Das Arbeitsverhältnis wird unverändert und ohne Unterbruch zu den bisherigen Vertragsbedingungen fortgesetzt, wie wenn die Kündigung nie ausgesprochen worden wäre.");

                        col.Item().PaddingTop(18).Text(
                            "Da der Rückzug einer Kündigung rechtlich nur mit Ihrem Einverständnis wirksam wird, bitten wir Sie, Ihr Einverständnis mit Ihrer Unterschrift auf der Kopie dieses Schreibens zu bestätigen und uns diese zurückzugeben.");

                        col.Item().PaddingTop(18).Text(
                            "Wir freuen uns auf die weitere Zusammenarbeit mit Ihnen.");
                    }

                    // Gruss + Unterschrift direkt nach dem Text (Walter 16.07.2026:
                    // nicht mehr ganz unten am Seitenende). IMMER von Hand
                    // unterschreiben — kein Unterschrift-Bild, nur Freiraum.
                    col.Item().PaddingTop(34).Text("Freundliche Grüsse");
                    if (!string.IsNullOrWhiteSpace(d.FirmaName))
                        col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();
                    col.Item().PaddingTop(6).Height(56);
                    col.Item().Text(d.UnterzeichnerName ?? "");
                    if (!string.IsNullOrWhiteSpace(d.UnterzeichnerFunktion))
                        col.Item().Text(d.UnterzeichnerFunktion!).FontColor("#475569");
                });

                page.Footer().Column(col =>
                {
                    // Einverstaendnis-Block der/des MA — nur beim STANDARD-Rueckzug
                    // (bei der Schwangerschafts-Variante ist die Kuendigung von
                    // Gesetzes wegen nichtig, kein Einverstaendnis noetig).
                    if (!d.NichtigSchwangerschaft)
                    {
                        col.Item().PaddingTop(18).Text("Mit dem Rückzug der Kündigung einverstanden:").FontSize(9f).FontColor("#475569");
                        col.Item().PaddingTop(36).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Ort und Datum").FontSize(8.5f).FontColor("#475569");
                            });
                            r.ConstantItem(40);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"Unterschrift {d.MaName}").FontSize(8.5f).FontColor("#475569");
                            });
                        });
                    }
                });
            });
        }).GeneratePdf();
    }

    public byte[] Generate(KuendigungData d, byte[]? signaturePng)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        const float sizeText = 10.5f;

        var firmaLines = new[] { d.FirmaName, d.FirmaStrasse, d.FirmaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
        var maLines = new[] { d.MaName, d.MaStrasse, d.MaPlzOrt }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();

        return Document.Create(doc =>
        {
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.MarginTop(1.0f, Unit.Centimetre);
                page.MarginBottom(1.3f, Unit.Centimetre);
                page.MarginHorizontal(2.2f, Unit.Centimetre);
                page.DefaultTextStyle(s => s.FontFamily("Arial").FontSize(sizeText).FontColor(Dark).LineHeight(1.4f));

                page.Header().PaddingTop(12).Image(BannerBytes).FitWidth();

                page.Content().PaddingTop(14).Column(col =>
                {
                    // Absender (Filiale) — klein oben links (Walter 15.07.2026:
                    // Filiale, MA und Datum ALLE linksbuendig).
                    foreach (var ln in firmaLines)
                        col.Item().Text(ln).FontSize(8.5f).FontColor("#475569");

                    // MA-Adresse im COUVERT-FENSTER (Walter 16.07.2026, wie Rueckzug):
                    // fixer Abstandhalter schiebt den Block in die C5-Fensterzone
                    // (~4.5 cm ab Papierkante).
                    col.Item().Height(40);
                    // Zustellung: Einschreiben ODER persönliche Übergabe
                    // (oft am Probezeitgespräch, Walter 21.07.2026).
                    col.Item().Text(d.Eingeschrieben ? "EINSCHREIBEN" : "PERSÖNLICHE AUSHÄNDIGUNG")
                        .Bold().LetterSpacing(0.06f).FontSize(9.5f);

                    // Empfänger-Adressblock.
                    col.Item().PaddingTop(3).Column(c =>
                    {
                        foreach (var ln in maLines) c.Item().Text(ln);
                    });

                    // Ort, Datum — linksbuendig (Walter 15.07.2026).
                    col.Item().PaddingTop(30)
                        .Text($"{d.Ort}, {d.KuendigungsDatum:dd.MM.yyyy}");

                    // Betreff.
                    col.Item().PaddingTop(30).Text("Kündigung des Arbeitsverhältnisses").Bold().FontSize(12.5f);

                    // Anrede.
                    col.Item().PaddingTop(22).Text($"{d.Briefanrede},");

                    // Haupttext.
                    col.Item().PaddingTop(14).Text(t =>
                    {
                        t.Span("hiermit kündigen wir das mit Ihnen bestehende Arbeitsverhältnis ordentlich unter Einhaltung der vertraglichen bzw. gesetzlichen Kündigungsfrist von ");
                        t.Span(d.FristText).Bold();
                        t.Span(" per ");
                        t.Span($"{d.LetzterArbeitstag:dd.MM.yyyy}").Bold();
                        t.Span(" (letzter Arbeitstag).");
                    });

                    if (!string.IsNullOrWhiteSpace(d.Grund))
                        col.Item().PaddingTop(14).Text($"Grund der Kündigung: {d.Grund}");

                    col.Item().PaddingTop(14).Text(
                        "Wir bitten Sie, bis zu Ihrem letzten Arbeitstag Ihre Aufgaben ordnungsgemäss zu übergeben und sämtliches Firmeneigentum (Schlüssel, Badge, Uniform etc.) zurückzugeben.");

                    col.Item().PaddingTop(14).Text(
                        "Wir wünschen Ihnen für Ihre berufliche und private Zukunft alles Gute.");

                    // Persönliche Übergabe: Unterschriften IM Content (nicht Footer) —
                    // drei Spalten brauchen zu viel Höhe für den Footer-Slot
                    // (QuestPDF LayoutException, Walter 21.07.2026).
                    if (!d.Eingeschrieben)
                    {
                        col.Item().PaddingTop(28).Text("Freundliche Grüsse");
                        if (!string.IsNullOrWhiteSpace(d.FirmaName))
                            col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();

                        col.Item().PaddingTop(10).Text("Original persönlich übergeben:")
                            .FontSize(9f).FontColor("#475569");
                        // Kein Unterschrifts-Strich (Walter 21.07.2026) — nur Freiraum + Label.
                        col.Item().PaddingTop(28).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                if (signaturePng is { Length: > 0 })
                                    c.Item().Height(44).AlignLeft().Image(signaturePng).FitHeight();
                                else
                                    c.Item().Height(44);
                                c.Item().PaddingTop(6)
                                    .Text(d.UnterzeichnerName ?? "Arbeitgeber")
                                    .FontSize(8.5f).FontColor("#475569");
                                if (!string.IsNullOrWhiteSpace(d.UnterzeichnerFunktion))
                                    c.Item().Text(d.UnterzeichnerFunktion!).FontSize(8f).FontColor("#475569");
                            });
                            r.ConstantItem(12);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Height(44);
                                c.Item().PaddingTop(6).Text("Zeuge der Übergabe")
                                    .FontSize(8.5f).FontColor("#475569");
                            });
                            r.ConstantItem(12);
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Height(44);
                                c.Item().PaddingTop(6)
                                    .Text(string.IsNullOrWhiteSpace(d.MaName)
                                        ? "Mitarbeiter (Empfang)"
                                        : d.MaName!)
                                    .FontSize(8.5f).FontColor("#475569");
                            });
                        });
                    }
                });

                // Einschreiben: Gruss + AG-Unterschrift als Footer (wie bisher).
                if (d.Eingeschrieben)
                {
                    page.Footer().Column(col =>
                    {
                        col.Item().Text("Freundliche Grüsse");
                        if (!string.IsNullOrWhiteSpace(d.FirmaName))
                            col.Item().PaddingTop(2).Text(d.FirmaName!).Bold();

                        if (signaturePng is { Length: > 0 })
                            col.Item().PaddingTop(8).Height(48).AlignLeft().Image(signaturePng).FitHeight();
                        else
                            col.Item().PaddingTop(8).Height(40);

                        col.Item().PaddingTop(2).Text(d.UnterzeichnerName ?? "");
                        if (!string.IsNullOrWhiteSpace(d.UnterzeichnerFunktion))
                            col.Item().Text(d.UnterzeichnerFunktion!).FontColor("#475569");
                    });
                }
            });
        }).GeneratePdf();
    }
}
